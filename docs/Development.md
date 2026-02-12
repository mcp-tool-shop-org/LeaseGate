# Development Guide

## Prerequisites

- .NET SDK 8.x
- PowerShell or shell environment with `dotnet`

## Build and test

```powershell
dotnet restore LeaseGate.sln
dotnet build LeaseGate.sln
dotnet test LeaseGate.sln
```

## Code Organization

- Keep protocol contracts deterministic and backward-safe
- Keep service logic explicit and deny reasons testable
- Keep policy evaluation side-effect free
- Keep audit writer best-effort and non-blocking for critical paths

## Testing Focus

Current tests cover:

- protocol serialization stability
- pool behavior (rate/context/compute)
- concurrency, budget, context deny paths
- approval-required flow + scoped single-use token behavior
- constraints and metrics snapshot behavior
- client fallback behavior in dev/prod modes

When extending Phase 1:

- add targeted tests for new deny reasons
- test idempotency behavior for retries
- test policy edge cases (empty allowlists, unknown action mappings)

## Local Workflow

1. Modify source under `src/`
2. Run unit tests
3. Run sample scenario (`simulate-all`)
4. Run stress scenario (`simulate-stress`)
5. Inspect audit JSONL output and metrics snapshot

## Suggested Next Docs for Phase 2

- Provider adapter contract
- Approval token lifecycle
- Multi-pool token accounting model
- Security threat model
