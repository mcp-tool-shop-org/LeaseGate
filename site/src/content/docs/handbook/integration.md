---
title: Integration
description: Integrate LeaseGate into your application with the acquire/release lifecycle and GitOps policies.
sidebar:
  order: 2
---

This page covers the core integration pattern for LeaseGate: the acquire/release lifecycle, the GitOps policy workflow, and distributed operation modes.

## The acquire/release lifecycle

Every governed operation follows a five-step lifecycle:

1. **Build a request** — Construct an `AcquireLeaseRequest` with actor identity, org/workspace scope, intent classification, model selection, estimated usage, requested tools, and context contributions.
2. **Acquire the lease** — Call `LeaseGateClient.AcquireAsync(...)`. The governor pipeline evaluates identity, RBAC, policy, intent routing, pool availability, approvals, and safety automation before issuing the lease.
3. **Execute work** — Perform model calls or tool invocations within the lease constraints. Use `GovernedModelCall.ExecuteProviderCallAsync(...)` for automatic constraint enforcement.
4. **Release the lease** — Call `LeaseGateClient.ReleaseAsync(...)` with actual telemetry (token counts, cost, latency, tool outcomes). The governor settles spend, updates attribution, appends a hash-chained audit entry, and mints a release receipt.
5. **Verify evidence** — Persist and verify the governance receipt when needed for compliance or audit purposes.

### Acquire pipeline in detail

When an acquire request arrives, the governor runs the following evaluation pipeline in order:

1. Identity and RBAC/service-account checks
2. Policy evaluation (models, capabilities, risk, tools)
3. Intent routing and fallback plan generation
4. Pool checks across all five resource types (concurrency, rate, context, compute, spend)
5. Approval requirements and reviewer threshold checks
6. Safety automation checks (cooldown, clamp, circuit-breaker)
7. Lease persistence and audit event recording

If any step fails, the response includes a deterministic `deniedReason` and an actionable `recommendation` for the operator or calling system.

### Release pipeline in detail

When a lease is released, the governor performs:

1. Spend settlement with actual utilization and tool outcomes
2. Attribution updates and alert evaluation
3. Hash-chained audit entry append
4. Release receipt minting bound to the current policy hash and audit anchor

The release response carries a classification (`recorded`, `leaseNotFound`, or `leaseExpired`) and, when recorded, a full governance receipt.

## Governance pools

LeaseGate enforces resource limits through five pool types, all evaluated before execution begins:

| Pool | What it controls |
|---|---|
| **Concurrency** | Maximum simultaneous leases per actor and workspace |
| **Rate** | Requests per time window with sliding window enforcement |
| **Context** | Token budget for context contributions with governed summarization |
| **Compute** | Model call budgets with per-model cost accounting |
| **Spend** | Dollar-denominated budgets with hierarchical quota rollup across org, workspace, and actor levels |

## GitOps policy workflow

Policy source lives in the `policies/` directory and is loaded through GitOps YAML composition. This approach keeps governance configuration version-controlled, reviewable, and auditable alongside your code.

### Policy files

| File | Scope |
|---|---|
| `org.yml` | Shared defaults and global thresholds |
| `models.yml` | Model allowlists and workspace model overrides |
| `tools.yml` | Denied/approval-required categories and reviewer requirements |
| `workspaces/*.yml` | Workspace-level budgets, rate caps, and role capability maps |

### Policy controls

The policy system covers several control domains:

- **Capacity and budgets** — In-flight limits, daily budgets at org/workspace/actor levels, rate limits, and token-per-minute caps.
- **Context governance** — Maximum context tokens, retrieved chunk/byte/token limits, and summarization target tokens.
- **Tool and compute governance** — Tool calls per lease, tool output size/timeout bounds, compute unit limits, and file path/network host allowlists.
- **Model and intent controls** — Model allowlists (global and per-workspace), capability maps by role, intent-based model tiers, and cost thresholds per intent class.
- **Identity and approvals** — Service accounts with scoped constraints, denied tool categories, approval-required categories, reviewer assignments, and risk-based approval requirements.
- **Safety automation** — Retry thresholds, tool loop detection, policy-deny circuit breaker thresholds, cooldown windows, output token clamps, and spend spike detection.

### Signed policy bundles

For production deployments, LeaseGate supports signed policy bundles:

1. Build and sign a policy bundle using `scripts/build-policy-bundle.ps1`
2. Stage the bundle through the `StagePolicyBundle` protocol command
3. Activate through the `ActivatePolicy` command

When signature enforcement is enabled (`requireSignedBundles`), only bundles signed with allowed keys will activate. CI validation is provided by `.github/workflows/policy-ci.yml`.

## Distributed operation

LeaseGate supports both local-only and distributed topologies.

### Local mode

In local mode, the client SDK communicates directly with the governor over named pipes:

```
Client SDK --> Named Pipe Server --> LeaseGovernor
```

All governance decisions, pool enforcement, and audit logging happen on the local machine.

### Hub/Agent distributed mode

In distributed mode, an Agent sits between the client and a Hub control plane:

```
Client SDK --> LeaseGateAgent --> HubControlPlane
                           \--> local LeaseGovernor (degraded fallback)
```

The Hub provides shared quota accounting and cost attribution across multiple agents. If the Hub becomes unavailable, the Agent continues with constrained local enforcement and marks responses as degraded. Acquire responses include locality and degradation indicators so downstream systems can adjust their behavior accordingly.

## Approval workflows

For high-risk or policy-controlled operations, LeaseGate provides an approval pipeline:

1. A request enters the pending approval queue when policy requires approval
2. Reviewers approve or deny via queue review commands
3. An approval token is issued when the reviewer threshold is met
4. The token is single-use and consumed on the first valid acquire

The protocol supports multiple approval-related commands: `RequestApproval`, `GrantApproval`, `DenyApproval`, `ListPendingApprovals`, and `ReviewApproval`. Each approval carries a reviewer trace for auditability.
