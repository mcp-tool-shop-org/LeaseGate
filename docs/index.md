# LeaseGate

Local-first AI governance control plane — lease admission, policy enforcement, and tamper-evident audit.

## What It Does

LeaseGate issues execution leases for AI workloads, enforces policy and budgets across multiple governance pools, and produces hash-chained, tamper-evident audit evidence. Everything runs locally with no cloud dependency.

## Key Features

- **Lease admission** with TTL-based release safety
- **Multi-pool governance** — concurrency, rate, context, compute, spend
- **Durable SQLite state** with restart recovery
- **Hash-chained audit** entries and release receipts
- **Signed policy bundles** via GitOps stage/activate flow
- **RBAC** with service accounts, hierarchical quotas, fairness controls
- **Safety automation** — cooldown, clamp, circuit-breaker

## Documentation

- [Architecture](Architecture.md)
- [Protocol](Protocol.md)
- [Policy](Policy.md)
- [Operations](Operations.md)
- [Development](Development.md)

## Links

- [GitHub Repository](https://github.com/mcp-tool-shop-org/LeaseGate)
- [LeaseGate-Lite](https://github.com/mcp-tool-shop-org/LeaseGate-Lite) — lightweight MAUI control surface
- [MCP Tool Shop](https://github.com/mcp-tool-shop-org)
