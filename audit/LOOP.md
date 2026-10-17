# Loop Engineering — TicketDeskPro Autonomous Hardening (emulated, not native runtime)

Methodology source: github.com/cobusgreyling/loop-engineering (patterns only — no CLI/MCP
scaffold was installed; this Cowork session emulates the loop with persistent state files,
bounded iterations and maker/checker verification via real sub-agents).

## Objective
Discover, fix and verify security/quality defects across TicketDeskPro-API and
TicketDeskPro-Client; deliver review-ready branches + PRs. No auto-merge, no auto-deploy.

## Cycle
DISCOVER → PRIORITIZE → PLAN → DELEGATE → INVESTIGATE → IMPLEMENT → TEST →
INDEPENDENT VERIFY → REVIEW DIFF → COMMIT → PUSH → UPDATE STATE → REPEAT

## Scope
IN: both connected repos (branches autonomous/ticketdeskpro-hardening), passive live-site
observation, dependency audits, tests, README updates on the branch.
OUT: production env/credentials, database contents, main-branch pushes, merges, deploys,
destructive live testing, secret values in any artifact.

## Stopping conditions
Budget exhausted (see loop-budget.md) · 3 failed repair attempts on one task · high-risk
change needing owner approval (isolate + record) · integrity/compromise evidence → halt all writes.
