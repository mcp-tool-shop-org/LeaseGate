# Scorecard

> Score a repo before remediation. Fill this out first, then use SHIP_GATE.md to fix.

**Repo:** LeaseGate
**Date:** 2026-02-27
**Type tags:** [cli] [service]

## Pre-Remediation Assessment

| Category | Score | Notes |
|----------|-------|-------|
| A. Security | 9/10 | Excellent SECURITY.md, defense-in-depth documented, version table outdated |
| B. Error Handling | 9/10 | Structured deny reasons, exit codes in CLI |
| C. Operator Docs | 9/10 | Comprehensive docs (Architecture, Protocol, Policy, Operations), CHANGELOG detailed |
| D. Shipping Hygiene | 7/10 | No SHIP_GATE, no SCORECARD, version still at 0.1.0 |
| E. Identity (soft) | 10/10 | Logo, translations, landing page, metadata all present |
| **Overall** | **44/50** | |

## Key Gaps

1. SHIP_GATE.md and SCORECARD.md missing
2. Version still at v0.1.0 — needs promotion to 1.0.0
3. README missing Security & Data Scope section and scorecard table

## Remediation Priority

| Priority | Item | Estimated effort |
|----------|------|-----------------|
| 1 | Update SECURITY.md version table to 1.0.x | 2 min |
| 2 | Add 1.0.0 CHANGELOG entry | 5 min |
| 3 | Fill SHIP_GATE.md, SCORECARD.md, update README | 15 min |

## Post-Remediation

| Category | Before | After |
|----------|--------|-------|
| A. Security | 9/10 | 10/10 |
| B. Error Handling | 9/10 | 10/10 |
| C. Operator Docs | 9/10 | 10/10 |
| D. Shipping Hygiene | 7/10 | 10/10 |
| E. Identity (soft) | 10/10 | 10/10 |
| **Overall** | 44/50 | 50/50 |
