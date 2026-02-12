# Protocol v0.1

## Overview

Protocol v0.1 defines deterministic request/response contracts for commands:

- `Acquire`
- `Release`
- `RequestApproval`
- `GrantApproval`
- `DenyApproval`
- `GetMetrics`

Transport is local named pipes with length-prefixed JSON payloads.

## Versioning

- `ProtocolVersion`: `0.1`
- Included in top-level pipe envelope
- Included in audit events

## AcquireLeaseRequest

Fields:

- `actorId`
- `workspaceId`
- `actionType` (`chatCompletion`, `embedding`, `toolCall`, `workflowStep`)
- `modelId`
- `providerId`
- `estimatedPromptTokens`
- `maxOutputTokens`
- `estimatedCostCents`
- `requestedContextTokens`
- `requestedRetrievedChunks`
- `estimatedToolOutputTokens`
- `estimatedComputeUnits`
- `requestedCapabilities` (list)
- `requestedTools` (structured list: `toolId`, `category`)
- `riskFlags` (list)
- `approvalToken`
- `idempotencyKey`

## AcquireLeaseResponse

Fields:

- `granted`
- `leaseId`
- `expiresAtUtc`
- `constraints` (`maxOutputTokensOverride`, `forcedModelId`, `maxToolCalls`, `maxContextTokens`, `cooldownMs`)
- `deniedReason`
- `retryAfterMs`
- `recommendation`
- `idempotencyKey`

## ReleaseLeaseRequest

Fields:

- `leaseId`
- `actualPromptTokens`
- `actualOutputTokens`
- `actualCostCents`
- `toolCallsCount`
- `bytesIn`
- `bytesOut`
- `latencyMs`
- `providerErrorClassification`
- `toolCalls[]` (`toolId`, `category`, `durationMs`, `bytesIn`, `bytesOut`, `outcome`)
- `outcome` (`success`, `providerRateLimit`, `timeout`, `policyDenied`, `toolError`, `unknownError`)
- `idempotencyKey`

## ReleaseLeaseResponse

Fields:

- `classification` (`recorded`, `leaseNotFound`, `leaseExpired`)
- `recommendation`
- `idempotencyKey`

## Additional Phase 2 DTOs

- `ApprovalRequest` / `ApprovalRequestResponse`
- `GrantApprovalRequest` / `GrantApprovalResponse`
- `DenyApprovalRequest` / `DenyApprovalResponse`
- `MetricsSnapshot` (active leases, spend, utilization, grants/denies by reason)

## Deny Semantics

Denies are explicit and testable:

- Concurrency full -> `concurrency_limit_reached`
- Daily budget exceeded -> `daily_budget_exceeded`
- Rate window exceeded -> `rate_limit_reached`
- Context/chunk/tool-output exceeded -> context-specific reasons
- Compute pool exhausted -> `compute_capacity_reached`
- Tool category/policy block -> tool-specific reason
- Risky tool without valid scoped approval -> `approval_required`
- Policy model/capability/risk reject -> reason from policy evaluator

Responses include actionable recommendations where possible.

## Serialization

`ProtocolJson` settings are pinned:

- `camelCase`
- string enums
- null omission

This keeps wire contracts stable across client/service boundaries.

## Idempotency

`idempotencyKey` is carried on acquire and release DTOs.

- Repeated acquire with same key returns same active lease when present.
- Retries are safe for transient client/service issues.
