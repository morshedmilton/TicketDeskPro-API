# TicketDeskPro-API

Enterprise Support Ticketing & SLA Platform — backend API built with **ASP.NET Core 10**, **EF Core 10**, **PostgreSQL**, **Redis** and **SignalR**.

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
    USERS }o--o{ TICKETS : "assigned"
    CATEGORIES ||--o{ TICKETS : "groups"
    TICKETS ||--o{ TICKET_MESSAGES : "contains"
    TICKETS ||--o{ TICKET_ATTACHMENTS : "has"
    TICKETS ||--o{ TICKET_STATUS_HISTORY : "tracked by"
    TICKETS ||--o{ TICKET_TAGS : "tagged"
    TAGS ||--o{ TICKET_TAGS : "applied to"
    TICKETS ||--|| SLA_TIMERS : "monitored by"
    SLA_POLICIES ||--o{ SLA_TIMERS : "defines"

    USERS ||--o{ REFRESH_TOKENS : "holds"
    USERS ||--o{ LOGIN_ATTEMPTS : "generates"
    USERS ||--o{ AUDIT_LOGS : "performs"
    USERS ||--o{ NOTIFICATIONS : "receives"
    USERS ||--|| NOTIFICATION_PREFERENCES : "configures"

    USERS {
        uuid id PK
        varchar email UK
        varchar password_hash
        varchar first_name
        varchar last_name
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
        uuid ticket_id UK
        uuid sla_policy_id FK
        timestamp first_response_deadline
        timestamp resolution_deadline
        bool first_response_breached
        bool resolution_breached
    }
    REFRESH_TOKENS {
        uuid id PK
        uuid user_id FK
        varchar token_hash UK
        uuid family_id
        timestamp expires_at
        timestamp revoked_at
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
    }
    NOTIFICATIONS {
        uuid id PK
        uuid user_id FK
        int type
        varchar title
        bool is_read
    }
    EMAIL_OUTBOX {
        uuid id PK
        varchar to_email
        varchar subject
        varchar status
        int attempts
        timestamp sent_at
    }
    NOTIFICATION_PREFERENCES {
        uuid id PK
        uuid user_id UK
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

<sub>Every table also carries `id`, `created_at`, `updated_at` and soft-delete columns (`is_deleted`, `deleted_at`) from `BaseEntity` — omitted above for readability.</sub>

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

## Seeded demo data

All demo users share the password `Admin123!` (local development only):

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
