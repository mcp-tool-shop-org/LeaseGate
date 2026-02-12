# Changelog

## 0.2.0 - 2026-02-12

Phase 2 real AI governance release.

### Release Notes

LeaseGate now governs real AI execution paths with explicit controls across throughput, context, compute, tool access, and risky-action approvals. This release is focused on operational predictability: deterministic deny reasons, scoped approval tokens, and a telemetry snapshot that aligns with audit events.

### Added

- Multi-pool resource controls:
	- rate pool (requests/tokens per rolling minute)
	- context pool (prompt/chunk/tool-output limits)
	- compute pool (weighted slot capacity)
- Rich lease constraints in acquire responses
- Provider abstraction project (`LeaseGate.Providers`) with deterministic adapter
- Structured tool governance (`ToolRegistry`, tool intents, tool usage summaries)
- Approval workflow commands and scoped approval tokens
- Metrics snapshot API and in-memory counters by reason
- Stress harness command (`simulate-stress`) with summary report
- Additional unit tests for pools, approvals, constraints, and metrics

### Integration Migration Checklist

- Update `AcquireLeaseRequest` population to include context/compute/tool fields:
	- `requestedContextTokens`
	- `requestedRetrievedChunks`
	- `estimatedToolOutputTokens`
	- `estimatedComputeUnits`
	- `requestedTools`
- Read and apply lease constraints from `AcquireLeaseResponse.constraints`:
	- `maxOutputTokensOverride`
	- `maxToolCalls`
	- `maxContextTokens`
	- `cooldownMs`
- Update release reporting to include provider/tool telemetry:
	- `latencyMs`
	- `providerErrorClassification`
	- `toolCalls[]`
- Handle approval-required flow in client code:
	- catch `ApprovalRequiredException`
	- call `RequestApprovalAsync(...)`
	- call `GrantApprovalAsync(...)` (or `DenyApprovalAsync(...)`)
	- retry acquire with returned `approvalToken`
- Adopt provider adapter path where possible:
	- implement `IModelProvider`
	- use `GovernedModelCall.ExecuteProviderCallAsync(...)`
- Add operational checks around telemetry and stress behavior:
	- call `GetMetrics` snapshot periodically
	- verify deny distributions and active-lease return-to-zero under load

## 0.1.0 - 2026-02-12

Initial Phase 1 MVP release.

### Added

- .NET 8 multi-project solution with clear boundaries
- Protocol v0.1 contracts for acquire/release flow
- Stable JSON serialization and framed pipe messaging
- Local governor daemon with named-pipe transport
- In-memory concurrency and daily-budget pools
- Lease storage with TTL expiration and reclamation
- Policy engine for model/capability/risk gating
- Append-only JSONL audit writer with policy hash
- Client SDK with dev/prod service-unavailable fallback modes
- Governed model call wrapper for integration points
- Sample CLI scenarios for concurrency/budget/policy behavior
- Unit tests for protocol, service limits/TTL, and fallback paths
