---
title: Documentation
description: Overview of LeaseGate's linked documentation guides.
sidebar:
  order: 3
---

LeaseGate ships with detailed documentation covering architecture, protocol, policy, operations, and development. This page provides an overview of each guide and what you will find inside.

## Architecture guide

The [Architecture guide](https://github.com/mcp-tool-shop-org/LeaseGate/blob/main/docs/Architecture.md) describes the runtime topology, the acquire/release flow, and the component map for all ten projects in the solution.

Key topics covered:

- **Runtime topology** — Both local mode (Client SDK to Named Pipe Server to LeaseGovernor) and distributed mode (Client SDK to Agent to Hub, with local degraded fallback).
- **Acquire/release flow** — The full governor pipeline for lease admission (identity, policy, intent routing, pools, approvals, safety) and release (spend settlement, attribution, audit chaining, receipt minting).
- **Component map** — Detailed responsibilities for Protocol, Service, Policy, Storage, Audit, Hub, Agent, Receipt, Client, and Providers.
- **Key data artifacts** — Audit logs (append-only JSONL with chain hashes), SQLite durable state, signed policy bundles, daily reports, and governance proof bundles.

## Protocol reference

The [Protocol reference](https://github.com/mcp-tool-shop-org/LeaseGate/blob/main/docs/Protocol.md) defines the deterministic command contracts used for communication over local named pipes.

Key topics covered:

- **Command set** — Fourteen commands covering lease lifecycle (`Acquire`, `Release`), approvals (`RequestApproval`, `GrantApproval`, `DenyApproval`, `ListPendingApprovals`, `ReviewApproval`), tool governance (`RequestToolSubLease`, `ExecuteToolCall`), policy lifecycle (`StagePolicyBundle`, `ActivatePolicy`), and operational reporting (`GetMetrics`, `GetStatus`, `ExportDiagnostics`, `ExportRunawayReport`).
- **Envelope and versioning** — Protocol v0.1, length-prefixed framed messages with a 16 MB payload cap, and stable JSON serialization via `ProtocolJson`.
- **Request/response contracts** — Full field documentation for `AcquireLeaseRequest` (identity, scope, intent, estimates, context contributions, tools, risk flags), `AcquireLeaseResponse` (constraints, deny reasons, fallback plans, locality), and release contracts (telemetry, tool outcomes, receipts).
- **Idempotency** — Most state-mutating requests support `idempotencyKey` for safe retries.

## Policy guide

The [Policy guide](https://github.com/mcp-tool-shop-org/LeaseGate/blob/main/docs/Policy.md) covers the GitOps YAML policy system and its control domains.

Key topics covered:

- **Policy sources** — Composition from `org.yml`, `models.yml`, `tools.yml`, and `workspaces/*.yml`.
- **Control domains** — Capacity and budgets, context governance, tool and compute governance, model and intent controls, identity and approvals, and safety automation.
- **Signed policy bundles** — Stage/activate lifecycle with signature enforcement and allowed public key configuration.
- **Linting and CI** — Policy validation for safety conditions (positive budgets, model allowlist presence, deny-by-default categories, reviewer count validity).
- **Operational guidance** — Best practices for role capability sets, intent-based model tiers, reviewer requirements, and policy change management.

## Operations guide

The [Operations guide](https://github.com/mcp-tool-shop-org/LeaseGate/blob/main/docs/Operations.md) covers day-to-day operational tasks, observability, audit artifacts, and incident response.

Key topics covered:

- **Sample host commands** — Complete reference for all `SampleCli` scenarios and operational commands.
- **Runtime observability** — `GetMetrics` (active leases, spend, pool utilization, grant/deny distributions, failed audit writes), `GetStatus` (health, uptime, durable state, policy version), `ExportDiagnostics`, and `ExportRunawayReport`.
- **Audit and evidence artifacts** — Daily JSONL audit files with event types (`lease_acquired`, `lease_denied`, `lease_released`, `lease_expired`, `lease_expired_by_restart`) and hash-chain linkage. Governance receipts with usage summaries, policy hashes, audit anchors, approval chains, and context summarization traces.
- **Failure and safety behavior** — Client fallback modes (dev vs. prod), approval pipeline lifecycle, and runaway suppression via safety automation.
- **Routine operator checks** — Eight-item checklist covering pipe health, policy version, deny distribution drift, incident diagnostics, daily spend review, receipt verification, audit subsystem health, and export path constraints.
- **Incident playbook** — Five-step response procedure from audit inspection through policy correction and recovery monitoring.

## Additional documentation

| Document | Purpose |
|---|---|
| [CONTRIBUTING.md](https://github.com/mcp-tool-shop-org/LeaseGate/blob/main/CONTRIBUTING.md) | Contribution guidelines and development workflow |
| [CHANGELOG.md](https://github.com/mcp-tool-shop-org/LeaseGate/blob/main/CHANGELOG.md) | Version history and release notes |
| [SECURITY.md](https://github.com/mcp-tool-shop-org/LeaseGate/blob/main/SECURITY.md) | Security policy, vulnerability reporting, and defense-in-depth design |
| [Development guide](https://github.com/mcp-tool-shop-org/LeaseGate/blob/main/docs/Development.md) | Developer setup and testing instructions |
