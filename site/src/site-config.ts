import type { SiteConfig } from '@mcptoolshop/site-theme';

export const config: SiteConfig = {
  title: 'LeaseGate',
  description: 'Local-first AI governance control plane — lease admission, policy enforcement, tamper-evident audit',
  logoBadge: 'LG',
  brandName: 'LeaseGate',
  repoUrl: 'https://github.com/mcp-tool-shop-org/LeaseGate',
  footerText: 'MIT Licensed \u2014 built by <a href="https://github.com/mcp-tool-shop-org" style="color:var(--color-muted);text-decoration:underline">mcp-tool-shop-org</a>',

  hero: {
    badge: '.NET / SQLite',
    headline: 'LeaseGate,',
    headlineAccent: 'governance before the model runs.',
    description: 'Local-first AI governance control plane. Lease admission, multi-pool policy enforcement, hash-chained audit, and tamper-evident proof export \u2014 all before a single token is spent.',
    primaryCta: { href: '#quick-start', label: 'Get started' },
    secondaryCta: { href: 'handbook/', label: 'Read the Handbook' },
    previews: [
      { label: 'Build', code: 'dotnet build LeaseGate.sln' },
      { label: 'Test', code: 'dotnet test LeaseGate.sln' },
      { label: 'Run', code: 'dotnet run --project samples/LeaseGate.SampleCli -- simulate-all' },
    ],
  },

  sections: [
    {
      kind: 'features',
      id: 'features',
      title: 'Why LeaseGate?',
      subtitle: 'Governance that runs before the model, not after.',
      features: [
        { title: 'Lease Admission', desc: 'TTL-based execution leases with concurrency, rate, context, compute, and spend pools. No lease, no execution.' },
        { title: 'Policy Enforcement', desc: 'GitOps YAML policies with signed bundle stage/activate flow. Org, model, tool, and workspace-level controls.' },
        { title: 'Tamper-Evident Audit', desc: 'Hash-chained append-only audit entries and release receipts. Every governance decision produces verifiable evidence.' },
        { title: 'Safety Automation', desc: 'Cooldown, clamp, and circuit-breaker patterns. Automatic responses to budget exhaustion and anomalous usage.' },
        { title: 'Tool Isolation', desc: 'Governed sub-leases for tool invocations. Shell metacharacter blocklist and direct execution prevent command injection.' },
        { title: 'Distributed Mode', desc: 'Hub/Agent architecture with degraded local fallback. Quota enforcement continues even when the hub is unreachable.' },
      ],
    },
    {
      kind: 'code-cards',
      id: 'quick-start',
      title: 'Quick Start',
      cards: [
        {
          title: 'Build & test',
          code: 'git clone https://github.com/mcp-tool-shop-org/LeaseGate.git\ncd LeaseGate\n\ndotnet restore LeaseGate.sln\ndotnet build LeaseGate.sln\ndotnet test LeaseGate.sln',
        },
        {
          title: 'Run scenarios',
          code: '# Run all sample scenarios\ndotnet run --project samples/LeaseGate.SampleCli -- simulate-all\n\n# Operational commands\ndotnet run --project samples/LeaseGate.SampleCli -- daily-report\ndotnet run --project samples/LeaseGate.SampleCli -- export-proof\ndotnet run --project samples/LeaseGate.SampleCli -- verify-receipt',
        },
      ],
    },
    {
      kind: 'data-table',
      id: 'projects',
      title: 'Solution Layout',
      subtitle: 'Ten focused projects in one solution.',
      columns: ['Project', 'Purpose'],
      rows: [
        ['LeaseGate.Protocol', 'DTOs, enums, serializer + framing'],
        ['LeaseGate.Policy', 'Policy model, evaluator, GitOps loader'],
        ['LeaseGate.Audit', 'Append-only hash-chained audit writer'],
        ['LeaseGate.Service', 'Governor, pools, approvals, safety, tool isolation'],
        ['LeaseGate.Client', 'SDK commands + governed call wrapper'],
        ['LeaseGate.Providers', 'Provider interface + adapters'],
        ['LeaseGate.Storage', 'Durable SQLite-backed state'],
        ['LeaseGate.Hub', 'Distributed quota and attribution control plane'],
        ['LeaseGate.Agent', 'Hub-aware agent with local degraded fallback'],
        ['LeaseGate.Receipt', 'Proof export + verification services'],
      ],
    },
    {
      kind: 'data-table',
      id: 'governance',
      title: 'Governance Pools',
      subtitle: 'Five pool types enforce resource limits before execution.',
      columns: ['Pool', 'What It Controls'],
      rows: [
        ['Concurrency', 'Maximum simultaneous leases per actor/workspace'],
        ['Rate', 'Requests per time window with sliding window enforcement'],
        ['Context', 'Token budget for context contributions with governed summarization'],
        ['Compute', 'Model call budgets with per-model cost accounting'],
        ['Spend', 'Dollar-denominated budgets with hierarchical quota rollup'],
      ],
    },
    {
      kind: 'data-table',
      id: 'security',
      title: 'Security Hardening',
      subtitle: 'Defense-in-depth across every layer.',
      columns: ['Area', 'Protection'],
      rows: [
        ['Command injection', 'Shell metacharacter blocklist + direct execution in tool isolation'],
        ['Service accounts', 'SHA-256 token hashing with plaintext backward compatibility'],
        ['Audit writes', 'Resilient writes with failure tracking \u2014 no silent fire-and-forget'],
        ['Pipe framing', 'Payload size limits (16 MB cap) on named pipe messages'],
        ['Thread safety', 'ConcurrentDictionary throughout registries and client state'],
        ['Path traversal', 'Protection on all export endpoints'],
        ['CSV injection', 'Formula prevention in report exports'],
      ],
    },
    {
      kind: 'data-table',
      id: 'policy',
      title: 'GitOps Policy',
      subtitle: 'YAML-based policy composition with signed bundles.',
      columns: ['File', 'Scope'],
      rows: [
        ['org.yml', 'Shared defaults and global thresholds'],
        ['models.yml', 'Model allowlists and workspace model overrides'],
        ['tools.yml', 'Denied/approval-required categories and reviewer requirements'],
        ['workspaces/*.yml', 'Workspace-level budgets and role capability maps'],
      ],
    },
  ],
};
