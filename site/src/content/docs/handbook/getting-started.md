---
title: Getting Started
description: Build, test, and run LeaseGate for the first time.
sidebar:
  order: 1
---

This page walks you through building LeaseGate from source, running the test suite, and executing your first sample scenarios.

## Prerequisites

- **.NET 8 SDK** or later
- **Git** for cloning the repository
- No external services or cloud dependencies are required — LeaseGate is entirely local-first

## Build and test

Clone the repository and build the solution:

```powershell
git clone https://github.com/mcp-tool-shop-org/LeaseGate.git
cd LeaseGate

dotnet restore LeaseGate.sln
dotnet build LeaseGate.sln
dotnet test LeaseGate.sln
```

The test suite covers unit and integration scenarios through all five implementation phases, including lease admission, policy evaluation, pool enforcement, tool isolation, distributed mode, and governance proof verification.

## Run sample scenarios

The `LeaseGate.SampleCli` project provides end-to-end scenarios that exercise the full governance pipeline:

```powershell
# Run all scenarios
dotnet run --project samples/LeaseGate.SampleCli -- simulate-all

# Individual scenarios
dotnet run --project samples/LeaseGate.SampleCli -- simulate-concurrency
dotnet run --project samples/LeaseGate.SampleCli -- simulate-adapter
dotnet run --project samples/LeaseGate.SampleCli -- simulate-approval
dotnet run --project samples/LeaseGate.SampleCli -- simulate-high-cost
dotnet run --project samples/LeaseGate.SampleCli -- simulate-stress
```

## Operational commands

Once LeaseGate is running, use these commands for routine checks:

```powershell
# Generate a daily spend and governance report
dotnet run --project samples/LeaseGate.SampleCli -- daily-report

# Export a governance proof bundle
dotnet run --project samples/LeaseGate.SampleCli -- export-proof

# Verify a governance receipt against the audit chain
dotnet run --project samples/LeaseGate.SampleCli -- verify-receipt
```

## Solution layout

The repository is organized into ten focused projects plus supporting samples and tests:

```text
LeaseGate.sln
src/
  LeaseGate.Protocol/     # DTOs, enums, serializer + framing
  LeaseGate.Policy/       # Policy model, evaluator, GitOps loader
  LeaseGate.Audit/        # Append-only hash-chained audit writer
  LeaseGate.Service/      # Governor, pools, approvals, safety, tool isolation
  LeaseGate.Client/       # SDK commands + governed call wrapper
  LeaseGate.Providers/    # Provider interface + adapters
  LeaseGate.Storage/      # Durable SQLite-backed state
  LeaseGate.Hub/          # Distributed quota and attribution control plane
  LeaseGate.Agent/        # Hub-aware agent with local degraded fallback
  LeaseGate.Receipt/      # Proof export + verification services
samples/
  LeaseGate.SampleCli/    # End-to-end scenarios and proof/report commands
  LeaseGate.AuditVerifier/# Audit chain verification sample
tests/
  LeaseGate.Tests/        # Unit/integration coverage through phase 5
policies/
  org.yml                 # Global defaults and thresholds
  models.yml              # Model allowlists
  tools.yml               # Tool category rules
  workspaces/*.yml        # Workspace-level budgets and roles
```

### Project responsibilities

| Project | Responsibility |
|---|---|
| **Protocol** | Shared wire DTOs, enums, and length-prefixed framed messages for named pipe transport (16 MB payload cap) |
| **Policy** | Policy model with org/workspace/role constraints, GitOps YAML composition, signed bundle stage/activate path |
| **Audit** | Append-only JSONL audit writer with SHA-256 hash chaining, streaming tail recovery, failure tracking |
| **Service** | Core `LeaseGovernor` orchestration, resource pools, approval queue, tool sub-leases, safety state machine |
| **Client** | SDK command surface for all governor operations with governed execution wrapper |
| **Providers** | Provider abstraction and adapters including a deterministic fake provider for tests |
| **Storage** | Durable SQLite stores for leases, approvals, rate events, budget, and policy metadata |
| **Hub** | Cross-agent distributed quota accounting and cost attribution aggregation |
| **Agent** | Hub-forwarding edge component with degraded local behavior when hub is unavailable |
| **Receipt** | Governance proof bundle generation with ECDSA signing and anchor verification |

## Current status — v1.0.0

Phases 1 through 5 are fully implemented, tested, and security-hardened. This includes lease admission with TTL-based release safety, multi-pool governance across five resource types, durable SQLite state with restart recovery, hash-chained audit entries with release receipts, signed policy bundle lifecycle, tool isolation with governed sub-leases, Hub/Agent distributed mode, RBAC with service accounts and hierarchical quotas, approval queue with reviewer trails, intent routing with deterministic fallback plans, context governance with governed summarization traces, safety automation with cooldown/clamp/circuit-breaker patterns, and governance proof export with verification.
