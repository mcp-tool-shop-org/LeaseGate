# LeaseGate Architecture

## Purpose

LeaseGate provides a local control plane for AI/model/tool execution:

- Requests are admitted through explicit lease acquisition
- Resources are constrained by policy and token pools
- Every decision is auditable
- Deadlocks are avoided via TTL-based lease expiry

## High-Level Flow

```text
Client SDK
  -> Named Pipe Request (Acquire)
  -> Governor
      -> PolicyEngine (allow/deny)
      -> ConcurrencyPool (max in-flight)
      -> RatePool (requests/tokens rolling window)
      -> ContextPool (prompt/chunks/tool-output budgets)
      -> ComputePool (weighted slots)
      -> ToolRegistry (tool + category validation)
      -> ApprovalStore (scoped human approval tokens)
      -> DailyBudgetPool (daily cents)
      -> LeaseStore (active + idempotency + TTL)
      -> MetricsRegistry (grant/deny/utilization counters)
      -> AuditWriter (event log)
  <- AcquireLeaseResponse

Client executes provider/tool work

Client SDK
  -> Named Pipe Request (Release)
  -> Governor
      -> release tokens + settle budget
      -> classify provider/tool failure outcomes
      -> audit release
  <- ReleaseLeaseResponse
```

## Components

### LeaseGate.Protocol

- Wire-level DTOs and enums
- Version marker: `0.1`
- Stable serializer settings via `ProtocolJson`
- Length-prefixed message framing for pipe transport

### LeaseGate.Service

- `LeaseGovernor`: orchestration core
- `ConcurrencyPool`: in-flight control
- `RatePool`: requests/tokens rolling-window control
- `ContextPool`: context/tool-output boundary control
- `ComputePool`: weighted compute slot control
- `DailyBudgetPool`: UTC day rollover budget tracking
- `LeaseStore`: active lease and idempotency tracking
- `ToolRegistry`: canonical tool definitions and categories
- `ApprovalStore`: approval request + scoped token lifecycle
- `MetricsRegistry`: operational counters and utilization views
- `NamedPipeGovernorServer`: local daemon transport

### LeaseGate.Providers

- `IModelProvider`: normalized provider contract
- `ModelCallSpec` + `ModelCallResult`: governed execution inputs/outputs
- `DeterministicFakeProviderAdapter`: deterministic adapter for local/demo/test
- `ProviderFailureClassifier`: stable error -> outcome mapping

### LeaseGate.Policy

- Human-editable policy model
- Evaluate acquire requests for model/capability/risk constraints
- Optional file-watch hot reload
- Immutable snapshots with SHA-256 `policyHash`

### LeaseGate.Audit

- Best-effort append-only JSONL writer
- Never crashes governor on logging failure
- Daily file naming for practical rotation

### LeaseGate.Client

- Acquire/release SDK calls
- Approval helper calls and metrics snapshot call
- Service-unavailable fallback behavior:
  - Dev mode: bounded local allowance
  - Prod mode: deny risky/non-read-only actions
- `GovernedModelCall` wrapper for delegates and provider adapters
- `ApprovalRequiredException` for explicit human-in-the-loop escalation

## Non-Goals (Current)

- Distributed/multi-host daemon mode
- centralized policy service / remote orchestration
- cryptographic approval attestation (beyond scoped local token)

## Future Evolution

- Provider adapters
- explicit approvals
- richer recommendation engine
- additional resource pools
- centralized fleet policy orchestration
