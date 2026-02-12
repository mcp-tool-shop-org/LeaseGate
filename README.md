# LeaseGate

LeaseGate is a local-first AI governance control plane that issues execution leases, enforces policy and budgets, and produces tamper-evident governance evidence.

## Current Status

Phases 1-5 are implemented and tested, including:

- Lease admission and TTL-based release safety
- Multi-pool governance (concurrency, rate, context, compute, spend)
- Durable SQLite state with restart recovery
- Hash-chained audit entries and release receipts
- Signed policy bundle stage/activate flow
- Tool isolation with governed sub-leases
- Hub/Agent distributed mode with degraded local behavior
- RBAC, service accounts, hierarchical quotas, fairness controls
- Approval queue with reviewer trails
- Intent routing with deterministic fallback plans
- Context governance with governed summarization traces
- Safety automation (cooldown, clamp, circuit-breaker)
- Governance proof export and verification

## Solution Layout

```text
LeaseGate.sln
src/
  LeaseGate.Protocol/     # DTOs, enums, serializer + framing
  LeaseGate.Policy/       # policy model, evaluator, GitOps loader
  LeaseGate.Audit/        # append-only hash-chained audit writer
  LeaseGate.Service/      # governor, pools, approvals, safety, tool isolation
  LeaseGate.Client/       # SDK commands + governed call wrapper
  LeaseGate.Providers/    # provider interface + adapters
  LeaseGate.Storage/      # durable SQLite-backed state
  LeaseGate.Hub/          # distributed quota and attribution control plane
  LeaseGate.Agent/        # hub-aware agent with local degraded fallback
  LeaseGate.Receipt/      # proof export + verification services
samples/
  LeaseGate.SampleCli/    # end-to-end scenarios and proof/report commands
  LeaseGate.AuditVerifier/# audit chain verification sample
tests/
  LeaseGate.Tests/        # unit/integration coverage through phase 5
policies/
  org.yml
  models.yml
  tools.yml
  workspaces/*.yml
```

## Quick Start

### Build and test

```powershell
dotnet restore LeaseGate.sln
dotnet build LeaseGate.sln
dotnet test LeaseGate.sln
```

### Run sample scenarios

```powershell
dotnet run --project samples/LeaseGate.SampleCli -- simulate-all
```

### Operational commands

```powershell
dotnet run --project samples/LeaseGate.SampleCli -- daily-report
dotnet run --project samples/LeaseGate.SampleCli -- export-proof
dotnet run --project samples/LeaseGate.SampleCli -- verify-receipt
```

## Integration Snapshot

Typical application flow:

1. Build an `AcquireLeaseRequest` with actor, org/workspace, intent, model, estimated usage, tools, and context contributions.
2. Acquire through `LeaseGateClient.AcquireAsync(...)`.
3. Execute model/tool work (or use `GovernedModelCall.ExecuteProviderCallAsync(...)`).
4. Release through `LeaseGateClient.ReleaseAsync(...)` with actual telemetry and outcomes.
5. Persist/verify receipt evidence when needed.

See [docs/Protocol.md](docs/Protocol.md) and [docs/Architecture.md](docs/Architecture.md).

## GitOps Policy Workflow

Policy source lives in `policies/` and is loaded through GitOps YAML composition.

- `org.yml` for shared defaults and global thresholds
- `models.yml` for model allowlists and workspace model overrides
- `tools.yml` for denied/approval-required categories and reviewer requirements
- `workspaces/*.yml` for workspace-level budgets and role capability maps

CI validation and bundle signing are provided by:

- `.github/workflows/policy-ci.yml`
- `scripts/build-policy-bundle.ps1`

## Documentation Index

- [docs/Architecture.md](docs/Architecture.md)
- [docs/Protocol.md](docs/Protocol.md)
- [docs/Policy.md](docs/Policy.md)
- [docs/Operations.md](docs/Operations.md)
- [docs/Development.md](docs/Development.md)
- [CONTRIBUTING.md](CONTRIBUTING.md)
- [CHANGELOG.md](CHANGELOG.md)
