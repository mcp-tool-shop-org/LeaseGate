# LeaseGate Architecture

## Purpose

LeaseGate provides a governance control plane for AI/model/tool execution where every unit of work is lease-gated, policy-constrained, and auditable.

Core guarantees:

- Explicit lease admission before model or tool execution
- Deterministic deny reasons and operator recommendations
- Bounded execution via pooled resource controls
- Durable state and restart-safe recovery
- Tamper-evident audit and verifiable governance receipts

## Runtime Topology

LeaseGate supports both local-only and distributed operation.

### Local mode

```text
Client SDK -> Named Pipe Server -> LeaseGovernor
```

### Distributed mode

```text
Client SDK -> LeaseGateAgent -> HubControlPlane
                        \-> local LeaseGovernor (degraded mode fallback)
```

In distributed mode, the Hub provides shared accounting and quota authority. If Hub access degrades, the Agent can continue with constrained local enforcement and marks responses as degraded.

## Acquire / Release Flow

```text
Acquire
  Client -> AcquireLeaseRequest
  Governor pipeline:
    1) identity + RBAC/service-account checks
    2) policy evaluation (models, capabilities, risk, tools)
    3) intent routing + fallback plan generation
    4) pool checks (concurrency, rate, context, compute, spend)
    5) approvals + reviewer requirements
    6) safety automation checks (cooldown/clamp/circuit-breaker)
    7) lease persistence + audit event
  <- AcquireLeaseResponse

Release
  Client -> ReleaseLeaseRequest
  Governor pipeline:
    1) settle spend + utilization + tool outcomes
    2) update attribution and alerts
    3) append hash-chained audit entry
    4) mint release receipt bound to policy/audit hash
  <- ReleaseLeaseResponse
```

## Component Map

### LeaseGate.Protocol

- Shared wire DTOs and enums (protocol v0.1)
- Length-prefixed framed messages for named pipe transport
- Stable JSON serialization via `ProtocolJson`

### LeaseGate.Service

- `LeaseGovernor`: core orchestration
- Resource pools: concurrency, rate, context, compute, daily spend
- Approval queue/review workflows
- Tool sub-leases and guarded tool execution
- Safety state machine and runaway suppression hooks
- Diagnostics and report export surfaces
- `NamedPipeGovernorServer`: command transport

### LeaseGate.Policy

- Policy model with org/workspace/role constraints
- GitOps YAML composition (`org.yml`, `models.yml`, `tools.yml`, `workspaces/*.yml`)
- Signed bundle stage/activate path
- Policy linting and immutable hash/version propagation

### LeaseGate.Storage

- Durable SQLite stores for leases, approvals, rate events, budget, policy metadata
- Restart recovery and stale lease cleanup

### LeaseGate.Audit

- Append-only JSONL audit writer
- Hash-chain (`prevHash`, `entryHash`) for tamper evidence
- Best-effort writes that do not crash governor paths

### LeaseGate.Hub

- Cross-agent distributed quota accounting
- Cost attribution aggregation and reporting support

### LeaseGate.Agent

- Hub-forwarding edge component for clients
- Degraded local behavior when hub is unavailable

### LeaseGate.Receipt

- Governance proof bundle generation
- Signature and anchor verification against audit chain

### LeaseGate.Client / LeaseGate.Providers

- SDK command surface for all governor operations
- Provider abstraction and governed execution wrapper
- Deterministic fake provider for tests and demos

## Key Data Artifacts

- Audit log: append-only JSONL with chain hashes
- SQLite durable state: active/pending governance state
- Policy bundles: signed payloads with version/hash metadata
- Daily reports: spend, deny distributions, alerts
- Governance proof bundles: receipts + verification material
