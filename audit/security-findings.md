# Security findings (evidence in repo; no secret values)
- S-SCAN: gitleaks v8.24.3 full history — CLEAN both repos. npm audit (lockfile) 0 vulns.
  NuGet --vulnerable: none reported. (verified this run)
- BE-M1 MEDIUM: forgot-password/reset-password unthrottled → reset-email bombing, outbox+Brevo
  quota exhaustion (AuthController.cs:107, only limiter is login). FIX PLANNED.
- BE-M2 MEDIUM: Cloudinary attachment URLs are public capability URLs (CloudinaryFileStorageService
  stores secure_url; anyone with the URL fetches forever). OWNER DECISION — not auto-fixed
  (needs private-delivery design + existing-asset migration).
- BE-M3 MEDIUM: register unthrottled (mass account creation, BCrypt cost abuse). FIX PLANNED.
- BE-L1 LOW: client-supplied Content-Type echoed on attachment download (TicketService.cs:486;
  mitigated by Content-Disposition attachment). FIX PLANNED (derive from extension).
- BE-L2 LOW-intentional: demo users seedable in Production by env flag (portfolio design).
  Mitigation: startup WARNING log when enabled in Production. FIX PLANNED (warning only).
- BE-L3 LOW: unbounded search/filter lengths (TicketDtos.cs:18, UsersController.cs:23) →
  LIKE scans. FIX PLANNED (MaximumLength clamp).
- BE-L4 LOW: RequirePermission returns 403 for anonymous (should be 401); admin endpoints rely
  on JWT permission claims until expiry (15 min) — accepted JWT trade-off, documented.
  FIX PLANNED (401 for unauthenticated).
- BE-N1/N2 NIT: dev JWT secret on disk (gitignored, treated burned); appsettings.json committed
  localhost dev creds (standard docker default — left, documented).
- FE-H1 LOW: no security headers on client pages (HSTS only, via Vercel) nor API responses
  (passive curl). FIX PLANNED (next.config headers() + API header middleware; no CSP — Swagger
  + Next inline scripts).
- FE-R1 LOW (doc): README previously claimed no middleware — proxy.ts IS an edge route gate
  (Next 16 rename). FIX PLANNED (README truth-fix).
- FE-N1 NIT: login ?next= validated (startsWith / , rejects //) — add backslash-prefix reject.
- CLEAN list: IDOR on tickets/messages/attachments verified covered (EnsureCanView everywhere);
  mass-assignment impossible (fixed DTOs); no raw SQL; pagination clamped 1-100; exception
  middleware leaks nothing; JWT params sound; reset token 256-bit single-use; no secret
  logging; notifications have no cross-user surface; XSS sinks absent in client; env files clean.
