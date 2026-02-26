<p align="center">
  <a href="README.ja.md">日本語</a> | <a href="README.zh.md">中文</a> | <a href="README.es.md">Español</a> | <a href="README.fr.md">Français</a> | <a href="README.hi.md">हिन्दी</a> | <a href="README.it.md">Italiano</a> | <a href="README.pt-BR.md">Português (BR)</a>
</p>

<p align="center">
  <img src="https://raw.githubusercontent.com/mcp-tool-shop-org/brand/main/logos/LeaseGate/readme.png" alt="LeaseGate" width="400">
</p>

<p align="center">
  <a href="https://github.com/mcp-tool-shop-org/LeaseGate/actions/workflows/policy-ci.yml"><img src="https://github.com/mcp-tool-shop-org/LeaseGate/actions/workflows/policy-ci.yml/badge.svg" alt="CI"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-yellow" alt="MIT License"></a>
  <a href="https://mcp-tool-shop-org.github.io/LeaseGate/"><img src="https://img.shields.io/badge/Landing_Page-live-blue" alt="Landing Page"></a>
</p>

**一种本地优先的 AI 治理控制平面，它会颁发执行许可，执行策略和预算控制，并生成具有防篡改功能的治理证据。**

---

## 概述

- **许可管理**：基于 TTL 的执行许可，采用多池治理。
- **策略执行**：基于 GitOps 的 YAML 策略，采用签名捆绑的部署/激活流程。
- **防篡改审计**：采用哈希链式、只追加的记录和发布凭证。
- **安全自动化**：包括冷却、限制和断路器机制。
- **工具隔离**：采用受控的子许可，并防止命令注入。
- **分布式模式**：采用中心/代理架构，并提供降级后的本地回退功能。

---

## 当前状态 — v0.1.0

阶段 1-5 已实现、测试并经过安全加固，包括：

- 许可管理和基于 TTL 的发布安全机制。
- 多池治理（并发、速率、上下文、计算、支出）。
- 具有重启恢复功能的持久化 SQLite 状态。
- 哈希链式审计记录和发布凭证。
- 签名策略捆绑的部署/激活流程。
- 具有受控子许可的工具隔离。
- 具有降级本地行为的中心/代理分布式模式。
- 基于角色的访问控制 (RBAC)、服务帐户、分层配额和公平性控制。
- 审批队列，并记录审查者信息。
- 基于意图的路由，并具有确定性的回退计划。
- 上下文治理，包括受控的摘要跟踪。
- 安全自动化（冷却、限制、断路器）。
- 治理证明的导出和验证。

### v0.1.0 安全加固

- 工具隔离中的命令注入防护（阻止 shell 元字符 + 直接执行）。
- 服务帐户令牌哈希（SHA-256，并支持明文兼容）。
- 具有故障跟踪的弹性审计写入（不再存在静默的“火与忘”）。
- 管道消息格式的有效负载大小限制（上限 16 MB）。
- 线程安全的注册表和客户端状态（整个系统使用 ConcurrentDictionary）。
- 并发命名管道连接（无需阻塞监听器即可分发）。
- 所有导出端点的路径遍历防护。
- 报告导出中的 CSV 公式注入防护。
- 安全自动化状态的无界增长限制。
- 策略重新加载错误的跟踪和暴露。
- 治理凭证签名支持外部密钥。

---

## 解决方案架构

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

---

## 快速入门

### 构建和测试

```powershell
dotnet restore LeaseGate.sln
dotnet build LeaseGate.sln
dotnet test LeaseGate.sln
```

### 运行示例场景

```powershell
dotnet run --project samples/LeaseGate.SampleCli -- simulate-all
```

### 操作命令

```powershell
dotnet run --project samples/LeaseGate.SampleCli -- daily-report
dotnet run --project samples/LeaseGate.SampleCli -- export-proof
dotnet run --project samples/LeaseGate.SampleCli -- verify-receipt
```

---

## 集成快照

典型的应用程序流程：

1. 构建一个 `AcquireLeaseRequest` 对象，包含参与者、组织/工作区、意图、模型、预估使用量、工具和上下文信息。
2. 通过 `LeaseGateClient.AcquireAsync(...)` 获取许可。
3. 执行模型/工具操作（或使用 `GovernedModelCall.ExecuteProviderCallAsync(...)`）。
4. 通过 `LeaseGateClient.ReleaseAsync(...)` 发布许可，并提供实际的遥测数据和结果。
5. 根据需要，持久化/验证凭证证据。

请参阅 [docs/Protocol.md](docs/Protocol.md) 和 [docs/Architecture.md](docs/Architecture.md)。

---

## GitOps 策略工作流程

策略源文件位于 `policies/` 目录中，并通过 GitOps YAML 组合加载。

- `org.yml`：用于共享默认值和全局阈值。
- `models.yml`：用于模型白名单和工作区模型覆盖。
- `tools.yml`：用于禁止/需要审批的类别以及审查者要求。
- `workspaces/*.yml`：用于工作区级别的预算和角色能力映射。

CI 验证和捆绑签名由以下内容提供：

- `.github/workflows/policy-ci.yml`
- `scripts/build-policy-bundle.ps1`

---

## 文档

- [docs/Architecture.md](docs/Architecture.md)
- [docs/Protocol.md](docs/Protocol.md)
- [docs/Policy.md](docs/Policy.md)
- [docs/Operations.md](docs/Operations.md)
- [docs/Development.md](docs/Development.md)
- [CONTRIBUTING.md](CONTRIBUTING.md)
- [CHANGELOG.md](CHANGELOG.md)
- [SECURITY.md](SECURITY.md)

---

## 许可证

[MIT](LICENSE)
