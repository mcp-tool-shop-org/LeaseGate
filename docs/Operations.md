# Operations Guide

## Running the Service (Sample Host)

Phase 1 includes an in-process sample host in `LeaseGate.SampleCli`.

```powershell
dotnet run --project samples/LeaseGate.SampleCli -- simulate-all
```

Commands:

- `simulate-concurrency`
- `simulate-adapter`
- `simulate-approval`
- `simulate-high-cost`
- `simulate-stress`
- `simulate-all`

## Audit Logs

Audit files are JSONL in a daily file:

- `leasegate-audit-YYYY-MM-DD.jsonl`

Events:

- `lease_acquired`
- `lease_denied`
- `lease_released`
- `lease_expired`

Common fields:

- `timestampUtc`
- `protocolVersion`
- `policyHash`
- request summary fields
- decision/recommendation details

## Failure Behavior

### Client-side fallback

If service is unavailable:

- Dev mode: allows bounded low-risk operations
- Prod mode: denies risky operations, allows limited read-only chat

### Approval workflow

- `RequestApproval` creates pending request with scope and TTL
- `GrantApproval` mints scoped token
- `DenyApproval` blocks request
- Single-use grants are consumed on first valid acquire

### Lease expiry safety

If client crashes or never releases:

- lease expires by TTL
- governor returns reserved resources
- expiry is logged

## Health Checks

Recommended checks for host processes:

- named pipe bind success
- acquire/release latency percentiles
- deny rate by reason
- audit write error count
- metrics snapshot from `GetMetrics` command

## Stress Test

Run:

```powershell
dotnet run --project samples/LeaseGate.SampleCli -- simulate-stress
```

Output report includes:

- total grant/deny counts
- active lease count (leak check)
- top deny reasons

## Incident Playbook

1. Inspect latest audit file for deny spikes and reason distributions
2. Verify current `policyHash` and corresponding policy file revision
3. Confirm budget and in-flight settings are appropriate for load
4. Validate client fallback mode in production configuration
