# LeaseGate

Local AI-governance control plane for acquiring and releasing execution leases with policy enforcement and audit logging.

## Phase 2 Status

LeaseGate Phase 2 is implemented and runnable:

- Deterministic protocol (`Acquire`/`Release`)
- Local governor daemon (named pipes)
- Multi-pool governance (concurrency, rate, context, compute, daily budget)
- Human-editable policy allowlists and risk gates
- Tool registry and structured tool usage governance
- Approval workflow for risky actions (`RequestApproval` / `GrantApproval` / `DenyApproval`)
- Provider adapter abstraction with deterministic fake adapter
- Telemetry snapshot API and stress harness
- Append-only JSONL audit trail
- Client SDK with governed call wrapper
- Sample CLI scenario runner
- Unit tests for pools, approvals, constraints, and fallback modes

## Solution Layout

```text
LeaseGate.sln
src/
  LeaseGate.Protocol/   # DTOs, enums, serializer + framing
  LeaseGate.Service/    # governor, pools, lease store, named-pipe server
  LeaseGate.Client/     # SDK + governed model call wrapper
  LeaseGate.Providers/  # provider interface + adapter implementations
  LeaseGate.Policy/     # policy model, loader, evaluator, hot reload
  LeaseGate.Audit/      # resilient append-only JSONL writer
samples/
  LeaseGate.SampleCli/  # end-to-end demo scenarios
tests/
  LeaseGate.Tests/      # unit tests
```

## Quick Start

### 1) Build and test

```powershell
dotnet build LeaseGate.sln
dotnet test LeaseGate.sln
```

### 2) Run sample workflow

```powershell
dotnet run --project samples/LeaseGate.SampleCli -- simulate-all
```

Expected output includes:

- Concurrency denies when in-flight leases exceed limit
- Budget deny with recommendation
- Policy deny for disallowed model/capability
- Approval-required deny and grant/retry path
- Stress report with top deny reasons
- Audit location path with JSONL events

## Integration Snapshot

Typical app integration uses `LeaseGateClient`, an `IModelProvider` adapter, and `GovernedModelCall.ExecuteProviderCallAsync(...)`:

1. Estimate tokens/cost and build `AcquireLeaseRequest`
2. Acquire lease from local governor
3. Execute provider/tool delegate
4. Release lease with actual usage and classified outcome

See [docs/Protocol.md](docs/Protocol.md) and [docs/Architecture.md](docs/Architecture.md).

## Migration Quickstart

Upgrading from Phase 1 to Phase 2:

1. Populate new acquire fields (`requestedContextTokens`, `requestedRetrievedChunks`, `estimatedToolOutputTokens`, `estimatedComputeUnits`, `requestedTools`).
2. Read `AcquireLeaseResponse.constraints` and enforce overrides/caps in your execution path.
3. Send richer release telemetry (`latencyMs`, `providerErrorClassification`, `toolCalls[]`).
4. Handle `ApprovalRequiredException` by requesting/granting approval and retrying with `approvalToken`.
5. Prefer adapter-based execution via `IModelProvider` + `GovernedModelCall.ExecuteProviderCallAsync(...)`.

Full checklist: [CHANGELOG.md](CHANGELOG.md) under **0.2.0 → Integration Migration Checklist**.

## Configuration

Sample policy lives at:

- `samples/LeaseGate.SampleCli/policy.json`

Core policy fields:

- `maxInFlight`
- `dailyBudgetCents`
- `maxRequestsPerMinute`
- `maxTokensPerMinute`
- `maxContextTokens`
- `maxToolCallsPerLease`
- `allowedModels`
- `allowedCapabilities`
- `allowedToolsByActorWorkspace`
- `approvalRequiredToolCategories`
- `riskRequiresApproval`

Details: [docs/Policy.md](docs/Policy.md).

## Audit Output

Audit events are append-only JSONL, one event per line:

- `lease_acquired`
- `lease_denied`
- `lease_released`
- `lease_expired`

Each event includes `protocolVersion` and `policyHash`.

Details: [docs/Operations.md](docs/Operations.md).

## Document Index

- [docs/Architecture.md](docs/Architecture.md)
- [docs/Protocol.md](docs/Protocol.md)
- [docs/Policy.md](docs/Policy.md)
- [docs/Operations.md](docs/Operations.md)
- [docs/Development.md](docs/Development.md)
- [CONTRIBUTING.md](CONTRIBUTING.md)
- [CHANGELOG.md](CHANGELOG.md)
