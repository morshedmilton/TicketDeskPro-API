# Remediation plan (priority order)
| ID | Slice | Acceptance | Verify |
|----|-------|-----------|--------|
| BE-M1/M3 | S-A throttle forgot+register via bucketed Redis limiter | unit: AuthService hits limiter with buckets forgot/register; suites green | dotnet test |
| BE-L1 | S-B derive download Content-Type from extension allowlist | unit/integration: client content-type ignored | dotnet test |
| BE-L4 | S-C unauthenticated → 401 on RequirePermission | integration: anon gets 401 (was 403); authed unchanged | dotnet test |
| BE-L3 | S-D cap search lengths (200) | validator clamp; suites green | dotnet test |
| BE-L2 | S-E warn when demo users seeded in Production | log line only; no behavior change | build |
| FE-H1 | S-F API response headers middleware | nosniff/frame-options/referrer present on live after merge | curl |
| FE-H1 | S-G client headers() + ?next= backslash reject | tsc+build green; headers after merge | tsc/build |
| FE-R1 | S-H README truth-fixes (proxy.ts, hardening notes) both repos | docs-only review | manual |
| BE-M2 | OWNER: Cloudinary private delivery decision | documented; no auto-fix | — |
Not run/not tested: live authenticated crawling (out of scope), Docker builds (unavailable).
