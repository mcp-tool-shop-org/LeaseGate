# Development Guide

## Prerequisites

- .NET SDK 8.x
- PowerShell (or equivalent shell) with `dotnet`

## Build and test

```powershell
dotnet restore LeaseGate.sln
dotnet build LeaseGate.sln
dotnet test LeaseGate.sln
```

## Project organization

- `src/LeaseGate.Protocol`: wire contracts and framing
- `src/LeaseGate.Policy`: policy model, evaluator, GitOps loader
- `src/LeaseGate.Service`: governor and command handlers
- `src/LeaseGate.Storage`: durable SQLite persistence
- `src/LeaseGate.Hub` and `src/LeaseGate.Agent`: distributed quota path
- `src/LeaseGate.Receipt`: governance proof verification/export
- `samples/LeaseGate.SampleCli`: operational scenarios and report/proof commands
- `tests/LeaseGate.Tests`: phase-spanning unit/integration tests

## Engineering principles

- Keep protocol changes backward-safe and additive where possible.
- Keep deny reasons deterministic and assertion-friendly.
- Keep policy evaluation side-effect free.
- Keep audit and evidence generation resilient (best-effort, non-crashing).
- Prefer explicit guardrails over implicit behavior.

## Testing focus

Current suites cover:

- acquire/release lifecycle and idempotency
- pool enforcement (concurrency/rate/context/compute/budget)
- approvals queue and reviewer threshold behavior
- tool sub-lease and isolation controls
- durable restart recovery and stale-lease expiry
- signed policy stage/activate paths
- distributed quota/fairness and degraded mode
- intent routing, fallback planning, context governance
- runaway suppression and diagnostics/report exports
- governance receipt export and verification
- command injection rejection (shell metacharacters)
- payload size enforcement (16 MB cap)
- path traversal rejection on export endpoints
- CSV formula injection prevention
- service account token hash comparison

When adding features:

- add tests for new deny/recommendation branches
- include upgrade-safe serialization tests for changed DTOs
- add negative-path tests for policy and signature validation
- add security boundary tests (injection, traversal, size limits) when touching isolation/export/framing

## Local workflow

1. Update source under `src/` and related tests.
2. Run `dotnet test LeaseGate.sln`.
3. Run `simulate-all` and `simulate-stress` from sample CLI.
4. Run `daily-report` and `export-proof`/`verify-receipt` when touching ops/evidence paths.
5. Review generated diagnostics and audit artifacts for expected metadata.

## Policy workflow for contributors

1. Update YAML under `policies/`.
2. Run tests and any policy lint/build scripts.
3. Validate staged/activated bundle behavior in local scenarios.
4. Document behavioral changes in `CHANGELOG.md` and relevant docs.
