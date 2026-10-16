# TicketDeskPro-API

[![CI](https://github.com/morshedmilton/TicketDeskPro-API/actions/workflows/ci.yml/badge.svg)](https://github.com/morshedmilton/TicketDeskPro-API/actions/workflows/ci.yml)
![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-17-4169E1)
![License](https://img.shields.io/badge/license-private-lightgrey)

Enterprise Support Ticketing & SLA Platform — backend API built with **ASP.NET Core 10**, **EF Core 10**, **PostgreSQL**, **Redis** and **SignalR**.

🚀 **Live:** https://ticketdeskpro-api.onrender.com · Swagger: https://ticketdeskpro-api.onrender.com/swagger · Frontend: https://ticketdeskpro-client.vercel.app

## Architecture

Clean Architecture, five layers (see `brain/03_FRD.md`):

```
src/
├── TicketDeskPro.Api            # Controllers, Hubs, middleware, pipeline
├── TicketDeskPro.Application    # Services, DTOs, validators, mappings
├── TicketDeskPro.Domain         # Entities, enums, interfaces (no dependencies)
├── TicketDeskPro.Infrastructure # EF Core, repositories, background jobs, external services
└── TicketDeskPro.Shared         # Exceptions, constants, extensions
```

## Features & Modules

Every module ships with a simple diagram so a reader understands it at a glance.

### 🔐 Authentication (Phase 2)

Login with email + password. Rate-limited per (IP, email) via a Redis sliding window, account locks after 5 failed attempts, and the response never reveals whether an email exists.

```mermaid
sequenceDiagram
    actor C as Client
    participant API as Auth API
    participant R as Redis
    participant DB as PostgreSQL

    C->>API: POST /auth/login (email, password)
    API->>R: sliding-window check (IP + email)
    alt limit exceeded
        R-->>API: window full
        API-->>C: 429 Too Many Requests
    else allowed
        R-->>API: ok
        API->>DB: load user + roles + permissions
        alt credentials valid
            DB-->>API: user found
            API->>API: BCrypt verify password
            API->>DB: store login attempt (success)
            API->>R: reset counter
            API-->>C: 200 accessToken (15 min) + refreshToken as HttpOnly cookie (7 days)
        else invalid
            API->>DB: store login attempt (failed)
            API-->>C: 401 generic "Invalid email or password"
        end
    end
```

### 🔄 Refresh Token Rotation (Phase 2)

Every refresh burns the old token and issues a new one in the same session family. Presenting an already-used token means it leaked — the whole family dies and everyone must log in again.

```mermaid
sequenceDiagram
    actor C as Client
    participant API as Auth API
    participant DB as PostgreSQL

    C->>API: POST /auth/refresh (HttpOnly cookie rides along)
    API->>DB: look up token by SHA-256 hash
    alt token already used (revoked)
        DB-->>API: revoked token found
        API->>DB: revoke the ENTIRE token family
        API-->>C: 401 invalid token state (reuse detected)
    else token expired
        API-->>C: 401 token expired
    else token valid
        DB-->>API: active token + user
        API->>DB: rotate in one transaction (revoke old, insert new, link audit)
        alt rotation lost a race
            API-->>C: 401 invalid token state
        else rotation won
            API-->>C: 200 new accessToken + rotated HttpOnly cookie
        end
    end
```

### 🔑 Password Reset (Phase 2)

Forgot-password always returns the same response (no email enumeration); the reset email goes through the transactional outbox; resetting the password kills every active session.

```mermaid
sequenceDiagram
    actor U as User
    participant API as Auth API
    participant DB as PostgreSQL
    participant W as Outbox Worker (Phase 4)

    U->>API: POST /auth/forgot-password (email)
    API->>DB: find user
    alt user exists
        API->>DB: store reset token hash + 1h expiry
        API->>DB: queue email in outbox (same transaction)
    end
    API-->>U: 200 always (never reveals if email exists)
    W->>DB: poll outbox, send email with reset link
    U->>API: POST /auth/reset-password (token, newPassword)
    API->>DB: validate token hash + expiry
    API->>DB: set new BCrypt hash, clear token, revoke ALL refresh tokens
    API-->>U: 200 password updated
```

### 🛡️ Permission-Based Authorization (Phase 2)

Permissions ride the JWT; a `RequirePermission` filter gates each endpoint; the service adds ownership rules on top (customers only ever see their own tickets — as a 404, so attackers can't probe which tickets exist).

```mermaid
flowchart LR
    REQ["🔒 Request with JWT"] --> CLAIMS["Token carries<br/>role + permissions claims"]
    CLAIMS --> GATE{"RequirePermission<br/>filter"}
    GATE -->|"claim present"| NEXT["Endpoint executes"]
    GATE -->|"claim missing"| DENY["403 Forbidden<br/>AUTH_004"]
    NEXT --> OWN{"Ownership check<br/>in service"}
    OWN -->|"customer + own ticket"| OK["Allowed"]
    OWN -->|"customer + other's ticket"| HIDE["404 (no existence leak)"]

    classDef good fill:#D1FAE5,stroke:#059669,color:#064E3B
    classDef bad fill:#FEE2E2,stroke:#DC2626,color:#7F1D1D
    classDef step fill:#DBEAFE,stroke:#3B82F6,color:#1E3A8A
    class REQ,CLAIMS step
    class NEXT,OK good
    class DENY,HIDE bad
```

### 🎫 Ticket Lifecycle (Phase 3)

A 7-state workflow enforced by a server-side state machine — invalid jumps return `409`. An Admin may force-close from **any** state (BR-018, dashed), and a resolved ticket auto-closes after 48h once the Phase 4 job lands.

```mermaid
flowchart LR
    OPEN(("Open")) --> INPROG(("In&nbsp;Progress"))
    INPROG --> AWAIT(("Awaiting&nbsp;Customer"))
    AWAIT --> REPLIED(("Customer&nbsp;Replied"))
    REPLIED --> INPROG
    INPROG --> RESOLVED(("Resolved"))
    RESOLVED -->|"within 48h (BR-019)"| REOPENED(("Reopened"))
    REOPENED --> INPROG
    RESOLVED --> CLOSED(("Closed"))
    OPEN -.->|"Admin only — from any state (BR-018)"| CLOSED
    RESOLVED -.->|"auto after 48h (Phase 4)"| CLOSED

    classDef terminal fill:#FEE2E2,stroke:#DC2626,color:#7F1D1D
    classDef active fill:#DBEAFE,stroke:#3B82F6,color:#1E3A8A
    classDef waiting fill:#FEF3C7,stroke:#F59E0B,color:#92400E
    class CLOSED terminal
    class OPEN,INPROG,REPLIED,REOPENED active
    class AWAIT,RESOLVED waiting
```

### 🆕 Creating a Ticket (Phase 3)

One transaction writes the ticket, its SLA stopwatch (deadlines from the priority policy) and the audit row. Concurrency-safe `TKT-0001` numbering; response carries an `ETag` for optimistic updates.

```mermaid
sequenceDiagram
    actor C as Customer
    participant API as Tickets API
    participant DB as PostgreSQL

    C->>API: POST /tickets (title, description, priority, category)
    API->>API: FluentValidation (BR-007 / BR-008)
    API->>DB: next ticket number (TKT-0001, collision-safe)
    API->>DB: INSERT ticket + SLA timer (deadlines from policy) + audit row — one transaction
    DB-->>API: committed
    API-->>C: 201 TKT-0001 with ETag header
    Note over API,DB: the SLA stopwatch starts the moment the ticket exists
```

### 💬 Replies & Internal Notes (Phase 3)

Customers and agents talk in one thread. Agents can post internal notes that customers never see — and the first public agent reply stamps the SLA first-response time.

```mermaid
flowchart TD
    MSG["New message on a ticket"] --> KIND{"Is it internal?"}
    KIND -->|"No (public reply)"| PERM{"Author has<br/>agent role?"}
    PERM -->|"Yes"| FR{"First public agent reply?"}
    FR -->|"Yes"| STAMP["Stamp SLA FirstResponseAt"]
    FR -->|"No"| STORE
    PERM -->|"No (customer)"| STORE["Stored, visible to everyone"]
    KIND -->|"Yes (internal note)"| NOTE_PERM{"Author has<br/>internal-note permission?"}
    NOTE_PERM -->|"Yes"| NOTE["Stored — agents & admins only"]
    NOTE_PERM -->|"No (customer)"| DENY403["403 Forbidden"]
    NOTE -.->|"hidden from customers (BR-023)"| HIDE["Customer never sees it"]

    classDef good fill:#D1FAE5,stroke:#059669,color:#064E3B
    classDef bad fill:#FEE2E2,stroke:#DC2626,color:#7F1D1D
    classDef step fill:#EDE9FE,stroke:#8B5CF6,color:#5B21B6
    class STORE,NOTE,STAMP,HIDE good
    class DENY403 bad
    class KIND,PERM,FR,NOTE_PERM step
```

### 📎 File Attachments (Phase 3)

Uploads pass a strict gate before touching disk: safe extension list, 5MB cap, 5 files per ticket. Storage paths are sanitized against traversal attacks.

```mermaid
flowchart TD
    UP["Upload attachment (max 5MB)"] --> TYPE{"Extension in<br/>safe list?<br/>jpg png gif pdf docx xlsx txt"}
    TYPE -->|"No"| REJ409["409 CPL_006"]
    TYPE -->|"Yes"| SIZE{"Size ≤ 5MB?"}
    SIZE -->|"No"| REJ422["422 validation error"]
    SIZE -->|"Yes"| COUNT{"Ticket already has<br/>5 attachments?"}
    COUNT -->|"Yes"| REJ409b["409 CPL_006"]
    COUNT -->|"No"| STORE["Stored as<br/>uploads/tickets/{id}/{guid}_{name}"]
    STORE --> OK201["201 attachment id"]

    classDef bad fill:#FEE2E2,stroke:#DC2626,color:#7F1D1D
    classDef good fill:#D1FAE5,stroke:#059669,color:#064E3B
    classDef step fill:#FEF3C7,stroke:#F59E0B,color:#92400E
    class REJ409,REJ422,REJ409b bad
    class STORE,OK201 good
    class TYPE,SIZE,COUNT step
```

### 🗑️ Soft Delete & Restore (Phase 1 + 3)

Delete never removes rows — an interceptor flips `isDeleted` and stamps who did it; an audit row captures the action. Admins can restore a deleted ticket at any time.

```mermaid
sequenceDiagram
    actor Adm as Admin
    participant API as Tickets API
    participant DB as PostgreSQL

    Adm->>API: DELETE /tickets/{id}
    API->>API: require tickets.delete permission
    API->>DB: UPDATE ticket SET isDeleted = true, deletedBy = adminId (interceptor)
    API->>DB: write audit row (action = Delete)
    API-->>Adm: 200 deleted
    Note over DB: row stays in the table — the app never hard-deletes

    Adm->>API: POST /tickets/{id}/restore
    API->>DB: UPDATE ticket SET isDeleted = false
    API-->>Adm: 200 restored (visible in queues again)
```

### 📮 Outbox Pattern — Guaranteed Email Delivery (Phase 4)

Emails are never written directly inside a request. The email row is committed **in the same transaction** as the business change; a background worker sends it with retries over Brevo's HTTPS API (Render free blocks outbound SMTP ports). If the email API dies, no email is lost — it waits.

```mermaid
sequenceDiagram
    participant API as Business code
    participant DB as PostgreSQL
    participant J as Outbox Worker (10s)
    participant BR as Brevo HTTPS API

    Note over API,DB: Transaction 1 — atomic, email cannot be lost
    API->>DB: UPDATE ticket + INSERT EmailOutbox (Pending)
    DB-->>API: commit

    loop every 10 seconds
        J->>DB: pick due Pending rows (NextAttemptAt <= now)
        alt EmailSettings:ApiKey configured
            J->>BR: POST /v3/smtp/email (port 443)
            alt sent
                DB-->>J: mark Sent
            else failed
                J->>DB: attempts++, backoff 2^attempts minutes
                note right of J: after 3 attempts -> Failed (dead letter)
            end
        else no API key (dev)
            J->>J: hold rows as Pending, log warning
        end
    end

    Note over J,DB: retention: Sent pruned after 7d, Failed after 30d
```

### ⏱️ SLA Checker Worker (Phase 4)

A watchdog that wakes every 60 seconds: warns at 75% elapsed, records breaches (escalating priority), and auto-closes tickets that sat Resolved for 48 hours.

```mermaid
flowchart TD
    TICK["⏱️ Every 60 seconds"] --> W{"75% of first-response<br/>window elapsed,<br/>not yet answered?"}
    W -->|"Yes, once"| WARN["Warn assigned agent<br/>(in-app + email, BR-035)"]
    W -->|"No"| FR
    WARN --> FR{"First-response deadline<br/>passed, ticket not closed?"}
    FR -->|"Yes"| FRB["Flag + escalate priority (BR-034)<br/>+ breach record + alert agent & admins"]
    FR -->|"No"| RES{"Resolution deadline passed<br/>and ticket still open<br/>(not paused, not resolved)?"}
    RES -->|"Yes"| RESB["Flag + escalate priority<br/>+ breach record + alert"]
    RES -->|"No"| CLOSE{"Resolved > 48 hours?<br/>(BR-018)"}
    CLOSE -->|"Yes"| AUTOCLOSE["Auto-close + history row<br/>+ customer email"]
    CLOSE -->|"No"| DONE["next pass in 60s"]
    AUTOCLOSE --> DONE

    classDef alert fill:#FEE2E2,stroke:#DC2626,color:#7F1D1D
    classDef warn fill:#FEF3C7,stroke:#F59E0B,color:#92400E
    classDef step fill:#DBEAFE,stroke:#3B82F6,color:#1E3A8A
    class FRB,RESB,AUTOCLOSE alert
    class WARN warn
    class TICK,FR,RES,CLOSE,DONE step
```

### 📡 Domain Events → Notifications & Real-time (Phase 4)

Raising an event on an entity is all a service does — after the transaction commits, handlers fan out to in-app notifications, outbox emails and SignalR pushes. A failing handler is logged, never breaks the request.

```mermaid
flowchart LR
    SVC["Service changes data<br/>(e.g. ticket created)"] --> SAVE["SaveChangesAsync"]
    SAVE --> TX["DB transaction commits"]
    TX --> CLEAR["Collect domain events<br/>from tracked entities"]
    CLEAR --> DISPATCH["Dispatch to handlers"]
    DISPATCH --> H1["NotificationDispatcher:<br/>in-app rows + outbox emails"]
    DISPATCH --> H2["SignalRRealtimeNotifier:<br/>push to user:{id} & role:agents"]
    H1 --> GUARD["Handler failures are logged,<br/>never fail the HTTP request"]
    H2 --> GUARD

    classDef step fill:#EDE9FE,stroke:#8B5CF6,color:#5B21B6
    classDef good fill:#D1FAE5,stroke:#059669,color:#064E3B
    class SVC,SAVE,TX,CLEAR,DISPATCH step
    class H1,H2,GUARD good
```

Also in this phase: **manual ticket assignment** (`POST /tickets/{id}/assign`, claim-to-InProgress), **round-robin auto-assignment** to the least-loaded active agent on creation, and **SignalR hub** at `/hubs/notifications` (JWT via header or `?access_token=`).

### 📊 Dashboard & Redis Cache-Aside (Phase 5)

Dashboard numbers are expensive to compute, so they live in Redis for 30 seconds — and every ticket mutation deletes the cache so the next read is always fresh. Redis failing never breaks a request: reads fall through to the database, invalidations are skipped.

```mermaid
sequenceDiagram
    participant C as Controller
    participant S as DashboardService
    participant R as Redis (TTL 30s)
    participant DB as PostgreSQL

    Note over C,DB: Read path — cache-aside
    C->>S: GET /dashboard/kpis
    S->>R: GET dash:kpis
    alt cache hit
        R-->>S: cached KPIs
    else cache miss
        S->>DB: aggregate queries
        DB-->>S: raw numbers
        S->>R: SET dash:kpis (30s)
    end
    S-->>C: KPIs

    Note over C,DB: Write path — invalidation on every ticket mutation
    C->>S: POST/PATCH /tickets...
    S->>DB: commit business change
    S->>R: delete dash:* (best-effort)
    Note right of R: next read recomputes — stale data capped by invalidation, not just TTL
```

### 🏥 Liveness vs Readiness (Phase 5)

Two probes, two questions: `/healthz` = "is the process alive?" (touches nothing). `/readyz` = "can it serve?" (pings PostgreSQL + Redis, answers 503 degraded when either is down). The full paths are **`/api/v1/health/healthz`** and **`/api/v1/health/readyz`** — Render's deploy health check and UptimeRobot probe the first one, so treat it as a stable contract (a wrong path 404s and turns every deploy into a 15-minute timeout). Plus structured logging: Serilog writes console + daily rolling files with the `CorrelationId` on every line.

```mermaid
flowchart LR
    P["Platform probe"] --> L{"GET /healthz<br/>(liveness)"}
    L -->|"process up"| OK1["200 healthy"]
    L -->|"crashed"| DOWN1["connection refused"]
    P --> R{"GET /readyz<br/>(readiness)"}
    R --> DB{"Postgres<br/>answers?"}
    DB -->|"Yes"| RD{"Redis<br/>answers?"}
    DB -->|"No"| D503["503 degraded"]
    RD -->|"Yes"| OK2["200 ready"]
    RD -->|"No"| D503

    classDef good fill:#D1FAE5,stroke:#059669,color:#064E3B
    classDef bad fill:#FEE2E2,stroke:#DC2626,color:#7F1D1D
    classDef step fill:#DBEAFE,stroke:#3B82F6,color:#1E3A8A
    class OK1,OK2 good
    class DOWN1,D503 bad
    class P,L,R,DB,RD step
```

Also in this phase: **admin panel endpoints** — user management (with BR-38 SuperAdmin guards and escalation protection), SuperAdmin role/permission builder (system roles locked), category & tag CRUD, filterable audit-log viewer, and per-user notification preferences.

## Database Schema

20 tables, code-first via EF Core. Colors group the modules:
**blue** = Identity & Access · **violet** = Ticketing · **amber** = SLA · **emerald** = Audit & Notifications · **slate** = join tables.

```mermaid
---
title: TicketDesk Pro — Database Schema
config:
  layout: elk
---
erDiagram
    USERS ||--o{ USER_ROLES : "has"
    ROLES ||--o{ USER_ROLES : "granted to"
    ROLES ||--o{ ROLE_PERMISSIONS : "granted via"
    PERMISSIONS ||--o{ ROLE_PERMISSIONS : "maps to"

    USERS ||--o{ TICKETS : "creates"
    USERS |o--o{ TICKETS : "assigned"
    CATEGORIES ||--o{ TICKETS : "groups"
    USERS ||--o{ TICKET_MESSAGES : "authors"
    TICKETS ||--o{ TICKET_MESSAGES : "contains"
    USERS ||--o{ TICKET_ATTACHMENTS : "uploads"
    TICKETS ||--o{ TICKET_ATTACHMENTS : "has"
    USERS ||--o{ TICKET_STATUS_HISTORY : "records"
    TICKETS ||--o{ TICKET_STATUS_HISTORY : "tracked by"
    TICKETS ||--o{ TICKET_TAGS : "tagged"
    TAGS ||--o{ TICKET_TAGS : "applied to"
    TICKETS o|--|| SLA_TIMERS : "monitored by"
    SLA_POLICIES ||--o{ SLA_TIMERS : "defines"

    USERS ||--o{ REFRESH_TOKENS : "holds"
    USERS o|--|| NOTIFICATION_PREFERENCES : "configures"
    USERS |o--o{ AUDIT_LOGS : "performs"
    USERS ||--o{ NOTIFICATIONS : "receives"

    USERS {
        uuid id PK
        varchar email UK
        varchar password_hash
        varchar first_name
        varchar last_name
        varchar phone
        varchar password_reset_token_hash
        timestamp password_reset_token_expires_at
        bool is_active
        bool is_deleted
        timestamp created_at
    }
    ROLES {
        uuid id PK
        varchar name UK
        bool is_system
    }
    PERMISSIONS {
        uuid id PK
        varchar name UK
        varchar module
    }
    USER_ROLES {
        uuid user_id FK
        uuid role_id FK
    }
    ROLE_PERMISSIONS {
        uuid role_id FK
        uuid permission_id FK
    }
    CATEGORIES {
        uuid id PK
        varchar name UK
        bool is_active
    }
    TAGS {
        uuid id PK
        varchar name UK
    }
    TICKETS {
        uuid id PK
        varchar ticket_number UK
        varchar title
        int status
        int priority
        uuid category_id FK
        uuid created_by FK
        uuid assigned_to FK
        uint xmin "concurrency"
    }
    TICKET_MESSAGES {
        uuid id PK
        uuid ticket_id FK
        text content
        bool is_internal
        uuid created_by FK
    }
    TICKET_ATTACHMENTS {
        uuid id PK
        uuid ticket_id FK
        varchar file_name
        varchar stored_path
        bigint file_size_bytes
        uuid uploaded_by FK
    }
    TICKET_STATUS_HISTORY {
        uuid id PK
        uuid ticket_id FK
        int from_status
        int to_status
        uuid changed_by FK
    }
    TICKET_TAGS {
        uuid ticket_id FK
        uuid tag_id FK
    }
    SLA_POLICIES {
        uuid id PK
        int priority UK
        int first_response_minutes
        int resolution_minutes
    }
    SLA_TIMERS {
        uuid id PK
        uuid ticket_id FK
        uuid sla_policy_id FK
        timestamp first_response_deadline
        timestamp resolution_deadline
        timestamp first_response_at
        bool is_first_response_breached
        bool is_resolution_breached
    }
    REFRESH_TOKENS {
        uuid id PK
        uuid user_id FK
        varchar token_hash UK
        varchar replaced_by_token_hash
        uuid family_id
        timestamp expires_at
        timestamp revoked_at
        uint xmin "concurrency"
    }
    LOGIN_ATTEMPTS {
        uuid id PK
        varchar email
        varchar ip_address
        bool is_success
    }
    AUDIT_LOGS {
        uuid id PK
        uuid user_id FK
        varchar action
        varchar entity_type
        varchar entity_id
        text before_json
        text after_json
        datetime timestamp
    }
    NOTIFICATIONS {
        uuid id PK
        uuid user_id FK
        int type
        varchar title
        varchar message
        bool is_read
    }
    EMAIL_OUTBOX {
        uuid id PK
        varchar to_email
        varchar subject
        text body
        varchar status
        int attempts
        timestamp sent_at
    }
    NOTIFICATION_PREFERENCES {
        uuid id PK
        uuid user_id FK
        bool email_on_ticket_created
        bool email_on_status_changed
        bool in_app_enabled
    }

    classDef identity fill:#3B82F6,stroke:#1E40AF,color:#FFFFFF
    classDef ticketing fill:#8B5CF6,stroke:#5B21B6,color:#FFFFFF
    classDef sla fill:#F59E0B,stroke:#B45309,color:#111111
    classDef audit fill:#10B981,stroke:#047857,color:#FFFFFF
    classDef join fill:#64748B,stroke:#334155,color:#FFFFFF

    class USERS,ROLES,PERMISSIONS identity
    class CATEGORIES,TAGS,TICKETS,TICKET_MESSAGES,TICKET_ATTACHMENTS,TICKET_STATUS_HISTORY ticketing
    class SLA_POLICIES,SLA_TIMERS sla
    class AUDIT_LOGS,EMAIL_OUTBOX,NOTIFICATIONS,NOTIFICATION_PREFERENCES,REFRESH_TOKENS,LOGIN_ATTEMPTS audit
    class USER_ROLES,ROLE_PERMISSIONS,TICKET_TAGS join
```

<br>

<sub>Notes: every table also carries `id`, `created_at`, `updated_at` and soft-delete columns (`is_deleted`, `deleted_at`, `deleted_by`) from `BaseEntity` — except the three join tables (composite keys) and `audit_logs` (append-only by design). `EMAIL_OUTBOX` and `LOGIN_ATTEMPTS` intentionally have **no foreign keys** — they address users by email string, which is why they float free above.</sub>

## Roadmap

| Phase | Module | Status |
|---|---|---|
| 1 | Database design, EF Core migrations, seed data | ✅ Shipped |
| 2 | JWT auth, refresh-token rotation, RBAC | ✅ Shipped |
| 3 | Ticket CRUD, state machine, messages, attachments, audit | ✅ Shipped |
| 4 | SLA engine + notifications (Outbox, SignalR) | ✅ Shipped |
| 5 | Dashboards & analytics (Redis KPIs, admin panel, health checks) | ✅ Shipped — backend complete |
| 6 | Frontend auth & layout (`TicketDeskPro-Client`) | ✅ Shipped |
| 7 | Frontend tickets & dashboard UI | ✅ Shipped |
| 8 | Production deployment (Render + Vercel, CI/CD) | ✅ Shipped |

## Prerequisites

- .NET SDK 10.0.x
- Docker (for PostgreSQL + Redis)

## Getting started

```bash
# 1. Start PostgreSQL + Redis
docker compose up -d

# 2. Run the API (migrates + seeds automatically on startup)
dotnet run --project src/TicketDeskPro.Api
```

Health check: `GET /health`

## Deployment (Phase 8)

$0 production: **Render** (Docker web service) → **Neon** Postgres → **Upstash** Redis · **Vercel** for the client · deploy hook fired by GitHub Actions after tests pass · UptimeRobot pings `/api/v1/health/healthz` every 5 min so the free instance never spins down.

```mermaid
flowchart LR
    GH["GitHub main push"] --> CI["GitHub Actions: unit + integration tests"]
    CI -->|green| HOOK["Render deploy hook"]
    HOOK --> R["Render: ticketdeskpro-api<br/>(Docker, free tier)"]
    V["Vercel: ticketdeskpro-client<br/>(auto-deploy on push)"] -->|JWT + CORS| R
    R --> NEON[("Neon PostgreSQL<br/>(free, SSL)")]
    R --> UP[("Upstash Redis<br/>(free, TLS)")]
    R --> MAIL["Brevo HTTPS API (optional, email)"]
    U["UptimeRobot /healthz<br/every 5 min"] --> R
```

Full step-by-step: `brain/06_DEPLOYMENT_GUIDE.md` (private).

## Seeded demo data

All demo users share the password `Admin123!`. Local dev always seeds them; production only when `SeedDemoUsers=true` (the live portfolio demo):

| Email | Role |
|---|---|
| superadmin@ticketdesk.dev | SuperAdmin |
| admin@ticketdesk.dev | Admin |
| agent@ticketdesk.dev | Agent |
| customer@ticketdesk.dev | Customer |

Also seeded: 4 system roles, 24 permissions, 4 SLA policies, 4 ticket categories.

## Useful commands

```bash
dotnet build
dotnet ef migrations add <Name> --project src/TicketDeskPro.Infrastructure --startup-project src/TicketDeskPro.Api
```

## Project docs

The `brain/` folder holds the full requirement set: BRD, PRD, FRD, API Specification, Database Design.
