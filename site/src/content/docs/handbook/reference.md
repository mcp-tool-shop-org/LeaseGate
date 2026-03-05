---
title: Reference
description: Security design, data scope, threat model, and scorecard for LeaseGate.
sidebar:
  order: 4
---

This page covers LeaseGate's security posture, data handling scope, and quality scorecard.

## Security design

LeaseGate enforces defense-in-depth across every layer of the governance pipeline. The security model is designed for a local-first control plane where all data stays on the operator's machine.

### Tool isolation

Tool execution is the highest-risk surface in any AI governance system. LeaseGate applies multiple layers of protection:

- **Command injection prevention** — Shell metacharacters are blocked before process execution. Commands execute directly without shell indirection (no `cmd.exe /c` or `/bin/sh -c` wrappers).
- **Allowlist enforcement** — File path roots and network hosts are checked against policy allowlists before any tool execution proceeds.
- **Bounded execution** — Tool output size and execution timeout are constrained by both policy-level and sub-lease-level limits.
- **Governed sub-leases** — Each tool invocation receives its own sub-lease with independent resource bounds.

### Authentication

- Service account tokens are compared via **SHA-256 hash** (`TokenHash`) by default. The plaintext `Token` field is supported for backward compatibility but should be migrated.
- Use `ServiceAccountPolicy.HashToken()` to generate hashes for policy files.
- Service accounts support scoped constraints: org, workspace, role, capability, model, and tool restrictions.

### Transport

- Named pipe framing enforces a **16 MB maximum payload size** per message.
- Pipe connections are per-request with no session persistence, preventing state confusion across requests.
- The named pipe server supports **concurrent connections** with dispatched handling, avoiding listener blocking.

### Export safety

- All file export paths are validated against **directory traversal** (`..` sequences are rejected).
- Exports are constrained to temp and local app data directories.
- CSV exports escape **formula injection characters** (`=`, `+`, `-`, `@`) to prevent spreadsheet-based attacks.

### Audit integrity

- The audit log is **append-only JSONL** with SHA-256 hash chaining (`prevHash` and `entryHash` on every entry).
- Governance receipt bundles are **ECDSA-signed** with verifiable anchors against the audit chain.
- Audit write failures are **tracked and exposed** in metrics via `FailedAuditWrites` — failures are never silently dropped.
- Streaming tail recovery reads the chain in constant memory regardless of log size.

### Resource bounds

- All internal state maps are **capped** to prevent unbounded memory growth.
- Safety automation state has entry limits with oldest-first eviction.
- Thread safety is enforced via `ConcurrentDictionary` throughout registries and client state.

## Data scope

LeaseGate is a **local-first** system. Understanding what data it touches — and what it does not — is critical for evaluating its fit in your environment.

### Data accessed

| Data type | Storage | Purpose |
|---|---|---|
| Lease state | SQLite | Active and pending lease records with durable restart recovery |
| Policy YAML | Local filesystem | GitOps policy composition from `policies/` directory |
| Audit logs | JSONL files | Hash-chained append-only governance events |
| Governance receipts | Export files | ECDSA-signed proof bundles for compliance verification |
| Named pipe transport | Localhost IPC | Client-governor communication (no network exposure) |

### Data NOT accessed

LeaseGate does **not** perform any of the following:

- No cloud sync or remote state replication
- No telemetry or analytics collection
- No external API calls or network egress
- No user tracking or behavioral profiling

### Permissions

LeaseGate requires only:

- **Local filesystem access** for state, audit, policy, and export files
- **Named pipes** for inter-process communication (localhost only)
- **No network egress** — the governance plane is entirely offline

## Threat model

The threat model addresses risks relevant to a local governance control plane:

| Threat | Mitigation |
|---|---|
| Command injection via tool arguments | Shell metacharacter blocklist + direct execution (no shell indirection) |
| Token theft for service accounts | SHA-256 hashed token storage, scoped constraints per service account |
| Audit log tampering | SHA-256 hash chaining, append-only writes, ECDSA-signed receipts |
| Silent audit failures | `FailedAuditWrites` counter in metrics, resilient write with failure tracking |
| Path traversal on exports | Directory traversal validation, constrained export directories |
| CSV formula injection | Escape of `=`, `+`, `-`, `@` characters in report exports |
| Unbounded memory growth | State map caps, safety automation eviction, payload size limits |
| Pipe message overflow | 16 MB payload cap on named pipe framing |
| Thread safety violations | `ConcurrentDictionary` throughout, dispatched pipe connections |
| Policy rollback attacks | Signed bundles with version/hash metadata, allowed public key enforcement |

## Vulnerability reporting

If you discover a security vulnerability in LeaseGate:

1. **Do not** open a public GitHub issue.
2. Report privately via [GitHub Security Advisories](https://github.com/mcp-tool-shop-org/LeaseGate/security/advisories/new).
3. Include a clear description and reproduction steps.
4. The maintainers will acknowledge receipt within 48 hours.

## Scorecard

LeaseGate has been evaluated against the [Shipcheck](https://github.com/mcp-tool-shop-org/shipcheck) product standards framework. All hard gates pass.

| Category | Score | Coverage |
|---|---|---|
| **A. Security** | 10/10 | SECURITY.md, threat model, no secrets, no telemetry, constrained file ops, no network egress, no stack traces |
| **B. Error Handling** | 10/10 | Structured error shape (code/message/hint/cause/retryable), exit codes, graceful degradation |
| **C. Operator Docs** | 10/10 | README, CHANGELOG, LICENSE, architecture/protocol/policy/operations guides |
| **D. Shipping Hygiene** | 10/10 | Verify script, version tagging, dependency scanning, clean packaging |
| **E. Identity (soft)** | 10/10 | Logo, translations, landing page, GitHub metadata |
| **Overall** | **50/50** | All hard gates pass, all soft gates satisfied |

## License

LeaseGate is released under the [MIT License](https://github.com/mcp-tool-shop-org/LeaseGate/blob/main/LICENSE).
