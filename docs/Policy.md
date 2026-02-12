# Policy Guide

## Policy File

Policy is JSON in Phase 1. Sample:

- `samples/LeaseGate.SampleCli/policy.json`

Example schema:

```json
{
  "maxInFlight": 4,
  "dailyBudgetCents": 120,
  "maxRequestsPerMinute": 90,
  "maxTokensPerMinute": 10000,
  "maxContextTokens": 2000,
  "maxRetrievedChunks": 8,
  "maxToolOutputTokens": 300,
  "maxToolCallsPerLease": 4,
  "maxComputeUnits": 4,
  "allowedModels": ["gpt-4o-mini"],
  "allowedCapabilities": {
    "chatCompletion": ["chat"],
    "toolCall": ["read"]
  },
  "allowedToolsByActorWorkspace": {
    "demo|sample": ["fs.read", "net.fetch"]
  },
  "deniedToolCategories": ["exec"],
  "approvalRequiredToolCategories": ["networkWrite", "fileWrite"],
  "riskRequiresApproval": ["network_write", "file_write", "exec"]
}
```

## Rules

### Capacity and budget

- `maxInFlight`: hard cap for concurrent active leases
- `dailyBudgetCents`: UTC daily spend limit

### Model allowlist

If `allowedModels` is non-empty, model ID must be included.

### Capability allowlist

Capabilities are constrained by `actionType`.

If an action has an allowlist entry, every requested capability must match it.

### Tool allowlist

Tools can be constrained per `actorId|workspaceId` using `allowedToolsByActorWorkspace`.

### Tool category deny list

`deniedToolCategories` hard-denies specific categories, even if tool IDs are otherwise allowed.

### Approval-required categories

`approvalRequiredToolCategories` enforces human grant flow.

Requests in those categories must include a valid scoped approval token.

### Risk gate

If request `riskFlags` intersects `riskRequiresApproval`, request is denied in Phase 1 with approval recommendation.

## Hot Reload

`PolicyEngine` supports optional file-watch reloading.

- Invalid updates are ignored (last good snapshot remains active)
- `policyHash` changes when policy content changes

## Operational Tips

- Start with strict, minimal capability sets
- Keep model allowlist narrow in production
- Track policy changes through source control
- Pair policy updates with audit review windows
- Use narrow approval scopes (actor + workspace + tool/category + short TTL + single-use)
