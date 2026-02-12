# Security Policy

## Supported Versions

| Version | Supported |
|---------|-----------|
| v0.1.0  | Yes       |

## Reporting a Vulnerability

If you discover a security vulnerability in LeaseGate:

1. **Do not** open a public GitHub issue.
2. Report privately to the maintainers via [GitHub Security Advisories](https://github.com/mcp-tool-shop-org/LeaseGate/security/advisories/new).
3. Include a clear description and reproduction steps.
4. We will acknowledge receipt within 48 hours and provide a timeline for resolution.

## Security Design

LeaseGate enforces defense-in-depth across several areas:

### Tool Isolation

- Shell metacharacters are blocked before process execution
- Commands execute directly (no `cmd.exe /c` shell indirection)
- File path and network host allowlists are enforced before tool execution
- Tool output size and execution timeout are bounded by policy and sub-lease constraints

### Authentication

- Service account tokens are compared via SHA-256 hash (`TokenHash`) by default
- Plaintext `Token` field is supported for backward compatibility but should be migrated
- Use `ServiceAccountPolicy.HashToken()` to generate hashes for policy files

### Transport

- Named pipe framing enforces a 16 MB maximum payload size
- Pipe connections are per-request (no session persistence)

### Export Safety

- All file export paths are validated against directory traversal (`..`)
- Exports are constrained to temp and local app data directories
- CSV exports escape formula injection characters (`=`, `+`, `-`, `@`)

### Audit Integrity

- Append-only JSONL audit log with SHA-256 hash chaining
- Governance receipt bundles are ECDSA-signed with verifiable anchors
- Audit write failures are tracked and exposed in metrics (not silently dropped)

### Resource Bounds

- All internal state maps are capped to prevent unbounded memory growth
- Pipe payload sizes are bounded
- Safety automation state has entry limits with eviction
