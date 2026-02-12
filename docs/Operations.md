# Operations Guide

## Sample host commands

`LeaseGate.SampleCli` provides operational and validation scenarios:

```powershell
dotnet run --project samples/LeaseGate.SampleCli -- simulate-concurrency
dotnet run --project samples/LeaseGate.SampleCli -- simulate-adapter
dotnet run --project samples/LeaseGate.SampleCli -- simulate-approval
dotnet run --project samples/LeaseGate.SampleCli -- simulate-high-cost
dotnet run --project samples/LeaseGate.SampleCli -- simulate-stress
dotnet run --project samples/LeaseGate.SampleCli -- simulate-all
dotnet run --project samples/LeaseGate.SampleCli -- daily-report
dotnet run --project samples/LeaseGate.SampleCli -- export-proof
dotnet run --project samples/LeaseGate.SampleCli -- verify-receipt
```

## Runtime observability endpoints

Use client/governor commands for runtime checks:

- `GetMetrics`: active leases, spend, pool utilization, grant/deny distributions
- `GetStatus`: health, uptime, durable-state info, policy version/hash
- `ExportDiagnostics`: status + metrics + effective runtime snapshot
- `ExportRunawayReport`: runaway/safety incident summary

## Audit and evidence artifacts

### Audit logs

Daily JSONL file:

- `leasegate-audit-YYYY-MM-DD.jsonl`

Common event types include:

- `lease_acquired`
- `lease_denied`
- `lease_released`
- `lease_expired`
- `lease_expired_by_restart`

Each entry carries policy metadata and hash-chain linkage.

### Governance receipts

Release responses may include a `receipt` with:

- lease usage summary
- policy hash
- audit anchor hash
- approval review trace
- context summarization trace

Use proof workflows to export and verify receipts.

## Distributed operation (Hub/Agent)

- Agent forwards governance requests to Hub for shared quota/accounting when available.
- If Hub is unavailable, Agent can continue in degraded mode with constrained local enforcement.
- Acquire responses expose locality/degraded indicators for downstream handling.

## Failure and safety behavior

### Client fallback

If governor is unavailable:

- Dev mode: bounded low-risk fallback
- Prod mode: deny risky operations and constrain read-only paths

### Approval pipeline

- Request enters pending queue
- Reviewers approve/deny via queue review commands
- Token is issued when reviewer threshold is met
- Single-use tokens are consumed on first valid acquire

### Runaway suppression

Safety automation reacts to repeated failures or policy-deny bursts by issuing recommendations such as cooldown windows and output clamps.

## Routine operator checks

1. Confirm named pipe bind and process health.
2. Check `GetStatus` for policy version/hash and durable-state health.
3. Review deny distribution drift in `GetMetrics`.
4. Export diagnostics during incidents.
5. Review daily report spend/alerts.
6. Verify receipt proofs for sampled high-risk actions.

## Incident playbook

1. Inspect latest audit file and runaway report for onset conditions.
2. Correlate deny spikes to policy hash/version and recent policy bundle activation.
3. Validate org/workspace/actor quota headroom and fairness settings.
4. Review approval queue backlog and reviewer throughput.
5. If needed, stage/activate corrected policy bundle and monitor recovery metrics.
