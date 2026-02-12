# Changelog

## v0.1.0 - 2026-02-12

First tagged release with security hardening across all phases.

### Security

- **Command injection fix**: `IsolatedToolRunner` no longer uses `cmd.exe /c`; shell metacharacters are blocked and commands execute directly via process FileName/Arguments
- **Token hashing**: `ServiceAccountPolicy` now supports `TokenHash` (SHA-256) with backward-compatible plaintext fallback; `PolicyEngine.TryResolveServiceAccount` prefers hash comparison
- **Pipe payload bounds**: `PipeMessageFraming.ReadAsync` enforces a 16 MB maximum payload size
- **Path traversal protection**: `ExportDiagnostics`, `ExportRunawayReport`, and `ExportDailySummary` validate output paths are under temp or app data directories and reject `..` traversal
- **CSV formula injection**: `HubControlPlane.ExportDailySummary` escapes CSV fields starting with `=`, `+`, `-`, `@`

### Reliability

- **Audit write resilience**: All fire-and-forget `_ = _audit.WriteAsync(...)` calls replaced with `AuditFireAndForget()` helper that catches failures and tracks count via `MetricsSnapshot.FailedAuditWrites`
- **Semaphore race fix**: `JsonlAuditWriter` uses boolean `acquired` flag instead of racy `_gate.CurrentCount == 0` check
- **Concurrent pipe connections**: `NamedPipeGovernorServer` dispatches connections via `Task.Run` instead of blocking the listen loop
- **Policy reload error tracking**: `PolicyEngine.TryReload` now captures and exposes errors via `LastReloadError` property instead of swallowing silently

### Thread Safety

- **ToolRegistry**: `Dictionary` replaced with `ConcurrentDictionary`
- **LeaseGateClient**: `HashSet<string>` replaced with `ConcurrentDictionary<string, byte>`

### Resource Bounds

- **SafetyAutomationState**: All internal dictionaries capped at 10K entries; interventions list capped at 1K with eviction
- **HubControlPlane**: `_leaseRequests` map capped at 10K entries with oldest-first eviction
- **JsonlAuditWriter.LoadTailState**: Replaced `File.ReadAllLines` with streaming `StreamReader` for constant memory usage

### Improvements

- **DRY refactor**: Duplicate auto-summarization blocks in `LeaseGovernor.AcquireAsync` extracted into `TryAutoSummarize()` helper
- **Receipt signing**: `GovernanceReceiptService` now documents ephemeral key limitation and provides `ExportProof` overload accepting external `ECDsa` key
- **MetricsSnapshot**: Added `FailedAuditWrites` field for audit health monitoring

## 0.5.0 - 2026-02-12

Phase 5 autonomous governance and proof release.

### Added

- GitOps policy-as-code flow with composed YAML policy sets under `policies/`
- CI policy validation and signed policy bundle creation workflow
- Intent-aware routing rules with deterministic fallback plan generation
- Context governance controls for chunk/token/byte thresholds and governed summarization path
- Safety automation for runaway suppression:
	- lease retry thresholding
	- tool loop detection
	- policy deny circuit breaker
	- cooldown and output clamp recommendations
- Governance proof tooling:
	- receipt bundle export
	- signature verification
	- audit-anchor verification against hash-chained logs
- CLI commands for `daily-report`, `export-proof`, and `verify-receipt`

## 0.4.0 - 2026-02-12

Phase 4 organization-scale governance release.

### Added

- RBAC-aware acquire flow with principal and role propagation
- Service account policy controls with scoped capabilities/models/tools
- Hub + Agent distributed mode with fallback to local degraded enforcement
- Hierarchical quotas (org, workspace, actor) with fairness controls
- Approval queue APIs with reviewer trails and multi-reviewer support
- Cost attribution tracking and daily governance report model
- Alert signal generation and export summary/report surfaces
- Expanded distributed and governance test coverage

## 0.3.0 - 2026-02-12

Phase 3 production-hardening release.

### Added

- Durable governor state via SQLite (`leases`, approvals, rate events, budget, policy metadata) with restart recovery
- Restart-expiry handling for stale leases with explicit `lease_expired_by_restart` auditing
- Tamper-evident audit hash chaining (`prevHash`, `entryHash`) and release receipts bound to audit hash + policy hash
- Policy bundle stage/activate controls with signature verification path and active policy metadata propagation
- Tool sub-leases and isolation boundaries (scoped calls, timeout/output ceilings, category/path/host checks)
- Runtime ops endpoints:
	- `GetStatus` snapshot for health + policy/runtime state
	- `ExportDiagnostics` bundle for status/metrics/pool configuration snapshots
- Expanded Phase 3 test coverage for durability recovery, clock-skew recovery, chaos burst loops, and diagnostics export

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
