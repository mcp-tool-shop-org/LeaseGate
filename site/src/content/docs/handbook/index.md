---
title: LeaseGate Handbook
description: Complete guide to the LeaseGate AI governance control plane.
sidebar:
  order: 0
---

Welcome to the LeaseGate handbook. This guide covers everything you need to understand, deploy, and operate LeaseGate as your local-first AI governance control plane.

## What is LeaseGate?

LeaseGate is a governance layer that sits between your application and AI model execution. Every unit of work — every model call, every tool invocation — requires an explicit lease before it can proceed. No lease, no execution. This approach gives operators complete visibility and control over AI resource consumption, policy compliance, and cost management.

LeaseGate is built on five core guarantees:

- **Explicit lease admission** before any model or tool execution begins.
- **Deterministic deny reasons** with actionable operator recommendations when a request cannot proceed.
- **Bounded execution** via five pooled resource controls (concurrency, rate, context, compute, spend).
- **Durable state** with SQLite-backed persistence and restart-safe recovery.
- **Tamper-evident audit** with hash-chained logs and verifiable governance receipts.

## Key capabilities

| Capability | What it does |
|---|---|
| Lease admission | TTL-based execution leases with multi-pool governance |
| Policy enforcement | GitOps YAML policies with signed bundle stage/activate flow |
| Tamper-evident audit | Hash-chained append-only entries and release receipts |
| Safety automation | Cooldown, clamp, and circuit-breaker patterns |
| Tool isolation | Governed sub-leases with command injection prevention |
| Distributed mode | Hub/Agent architecture with degraded local fallback |
| RBAC and quotas | Service accounts, hierarchical quotas, and fairness controls |
| Approval workflows | Approval queue with reviewer trails and threshold enforcement |
| Intent routing | Deterministic fallback plans when primary intent is denied |
| Context governance | Governed summarization traces with token budget enforcement |

## Handbook contents

This handbook is organized into the following sections:

1. **[Getting Started](/LeaseGate/handbook/getting-started/)** — Build instructions, solution layout, current status, and your first sample run.
2. **[Integration](/LeaseGate/handbook/integration/)** — The acquire/release lifecycle, GitOps policy workflow, and distributed operation.
3. **[Documentation](/LeaseGate/handbook/documentation/)** — Overview of linked guides covering architecture, protocol, policy, and operations.
4. **[Reference](/LeaseGate/handbook/reference/)** — Security design, data scope, threat model, and the project scorecard.

## Current status

LeaseGate is at **v1.0.0** with Phases 1 through 5 fully implemented, tested, and security-hardened. The solution includes ten focused .NET projects, a comprehensive test suite, and sample CLI tooling for operational validation.
