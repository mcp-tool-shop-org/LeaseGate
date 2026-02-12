# Protocol v0.1

## Overview

LeaseGate protocol v0.1 defines deterministic command contracts over local named pipes using length-prefixed JSON envelopes.

Core command set:

- `Acquire`
- `Release`
- `RequestApproval`
- `GrantApproval`
- `DenyApproval`
- `ListPendingApprovals`
- `ReviewApproval`
- `GetMetrics`
- `GetStatus`
- `ExportDiagnostics`
- `ExportRunawayReport`
- `StagePolicyBundle`
- `ActivatePolicy`
- `RequestToolSubLease`
- `ExecuteToolCall`

## Envelope and versioning

- `ProtocolVersion`: `0.1` (from `ProtocolVersionInfo.ProtocolVersion`)
- Commands are wrapped in `PipeCommandRequest` / `PipeCommandResponse`
- Maximum payload size: 16 MB per message
- Protocol version and policy metadata are propagated into audit events and responses

## AcquireLeaseRequest

Primary identity and scope fields:

- `sessionId`, `clientInstanceId`
- `orgId`, `actorId`, `workspaceId`
- `principalType`, `role`, `authToken`

Execution intent and estimate fields:

- `actionType`, `modelId`, `providerId`, `intentClass`
- `autoApplyConstraints`
- `estimatedPromptTokens`, `maxOutputTokens`, `estimatedCostCents`
- `estimatedComputeUnits`, `estimatedToolOutputTokens`

Context governance fields:

- `requestedContextTokens`, `requestedRetrievedChunks`
- `requestedRetrievedBytes`, `requestedRetrievedTokens`
- `contextContributions[]` (`sourceId`, `chunks`, `bytes`, `tokens`)

Tool/risk fields:

- `requestedCapabilities[]`
- `requestedTools[]` (`toolId`, `category`)
- `riskFlags[]`
- `approvalToken`
- `idempotencyKey`

## AcquireLeaseResponse

- `granted`, `leaseId`, `expiresAtUtc`
- `constraints` (`maxOutputTokensOverride`, `forcedModelId`, `maxToolCalls`, `maxContextTokens`, `cooldownMs`)
- `deniedReason`, `retryAfterMs`, `recommendation`
- `idempotencyKey`
- `policyVersion`, `policyHash`
- `orgId`, `principalType`, `role`
- `leaseLocality` (`localIssued` or `hubIssued`)
- `degradedMode`
- `fallbackPlan[]` (`rank`, `action`, `detail`)

## Release contracts

### ReleaseLeaseRequest

- `leaseId`
- actual usage telemetry (`actualPromptTokens`, `actualOutputTokens`, `actualCostCents`, `latencyMs`, `bytesIn`, `bytesOut`)
- tool telemetry (`toolCallsCount`, `toolCalls[]` with sub-lease and per-call outcome)
- `providerErrorClassification`
- `outcome`
- `idempotencyKey`

### ReleaseLeaseResponse

- `classification` (`recorded`, `leaseNotFound`, `leaseExpired`)
- `recommendation`, `idempotencyKey`
- `policyVersion`, `policyHash`
- `receipt` (when recorded)

`receipt` includes:

- lease + policy + usage summary
- `auditEntryHash`
- `approvalChain[]`
- `contextSummaries[]`

## Approvals and reviews

- `ApprovalRequest` / `ApprovalRequestResponse`
- `GrantApprovalRequest` / `GrantApprovalResponse`
- `DenyApprovalRequest` / `DenyApprovalResponse`
- `ApprovalQueueRequest` / `ApprovalQueueResponse`
- `ReviewApprovalRequest` / `ReviewApprovalResponse`

Approval queue items include reviewer traces, required reviewer counts, and current approval count.

## Tool governance contracts

- `ToolSubLeaseRequest` / `ToolSubLeaseResponse`
- `ToolExecutionRequest` / `ToolExecutionResponse`

These contracts enforce bounded calls, timeout ceilings, output-size ceilings, and path/host/command policy checks.

## Operational/reporting contracts

- `MetricsSnapshot` (includes `FailedAuditWrites` counter)
- `GovernorStatusResponse`
- `ExportDiagnosticsRequest` / `ExportDiagnosticsResponse`
- `ExportRunawayReportRequest` / `ExportRunawayReportResponse`
- `DailyReportResponse`

## Policy lifecycle contracts

- `PolicyBundle`
- `StagePolicyBundleResponse`
- `ActivatePolicyRequest`
- `ActivatePolicyResponse`

## Serialization and idempotency

`ProtocolJson` settings are stable and deterministic:

- camelCase field naming
- string enum serialization
- null omission

Most state-mutating requests support `idempotencyKey`, and repeated keys are handled safely for retry scenarios.
