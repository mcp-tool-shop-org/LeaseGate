# Policy Guide

## Policy Sources

LeaseGate supports policy composition from GitOps YAML under `policies/`:

- `org.yml`: shared defaults and global constraints
- `models.yml`: model allowlists and workspace model overrides
- `tools.yml`: denied/approval-required categories and reviewer requirements
- `workspaces/*.yml`: workspace budgets, rate caps, and role capability maps

The composed policy maps to `LeaseGatePolicy` and is loaded by `PolicyGitOpsLoader`.

## Core control domains

### Capacity and budgets

- `maxInFlight`, `maxInFlightPerActor`
- `dailyBudgetCents`, `orgDailyBudgetCents`, `workspaceDailyBudgetCents`, `actorDailyBudgetCents`
- `maxRequestsPerMinute`, `maxTokensPerMinute`
- org/workspace/actor rate overrides

### Context governance

- `maxContextTokens`
- `maxRetrievedChunks`, `maxRetrievedBytes`, `maxRetrievedTokens`
- `summarizationTargetTokens`

### Tool and compute governance

- `maxToolCallsPerLease`
- `maxToolOutputTokens`, `maxToolOutputBytes`
- `maxComputeUnits`
- `defaultToolTimeoutMs`
- `allowedFileRoots`, `allowedNetworkHosts`

### Model, capability, and intent controls

- `allowedModels`
- `allowedModelsByWorkspace`
- `allowedCapabilities`
- `allowedCapabilitiesByRole`
- `intentModelTiers`
- `intentMaxCostCents`

### Identity and approvals

- `serviceAccounts[]` with scoped token/org/workspace/role/capability/model/tool controls
  - Prefer `TokenHash` (SHA-256) over plaintext `Token` — use `ServiceAccountPolicy.HashToken()` to generate
  - Plaintext `Token` is supported for backward compatibility but should be migrated
- `deniedToolCategories`
- `approvalRequiredToolCategories`
- `approvalReviewersByToolCategory`
- `riskRequiresApproval`

### Safety automation

- `retryThresholdPerLease`
- `toolLoopThreshold`
- `policyDenyCircuitBreakerThreshold`
- `safetyCooldownMs`
- `clampedMaxOutputTokens`
- `spendSpikeCents`

## Evaluation behavior

At acquire time, policy checks are applied before final pool admission.

Typical deny classes include:

- disallowed model/capability
- tool category blocked
- tool/risk requires approval
- service account constraint mismatch
- intent/cost threshold mismatch

Responses include explicit `deniedReason` and actionable `recommendation`.

## Signed policy bundles

Policy bundles can be staged and activated through protocol commands:

- `StagePolicyBundle`
- `ActivatePolicy`

`PolicyEngineOptions` supports signature enforcement:

- `requireSignedBundles`
- `allowedPublicKeysBase64`

When enabled, only bundles with valid signatures and allowed keys activate.

## Linting and CI

`PolicyGitOpsLoader.Lint(...)` validates key safety conditions, including:

- positive budgets/rate limits/in-flight limits
- required model allowlist presence
- deny-by-default category expectations
- reviewer count validity per tool category

Run lint/sign checks in CI via `.github/workflows/policy-ci.yml`.

## Operational guidance

- Keep role capability sets minimal and explicit.
- Reserve high-cost intents for constrained model tiers.
- Require multiple reviewers for high-risk categories.
- Pair policy changes with report/audit review windows.
- Prefer signed bundle activation in production.
