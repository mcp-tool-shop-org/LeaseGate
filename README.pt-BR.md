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

**Plataforma de governança de IA local que concede permissões de execução, aplica políticas e orçamentos, e gera evidências de governança à prova de adulteração.**

---

## Visão Geral

- **Concessão de permissões:** Permissões de execução baseadas em TTL com governança multi-pool.
- **Aplicação de políticas:** Políticas YAML do tipo GitOps com fluxo de assinatura para etapas de implantação/ativação.
- **Auditoria à prova de adulteração:** Entradas de auditoria encadeadas por hash e recibos de lançamento.
- **Automação de segurança:** Padrões de resfriamento, limitação e disjuntor.
- **Isolamento de ferramentas:** Sub-permissões gerenciadas com prevenção de injeção de comandos.
- **Modo distribuído:** Arquitetura Hub/Agente com fallback local em caso de degradação.

---

## Status Atual — v0.1.0

As fases 1 a 5 estão implementadas, testadas e com segurança reforçada, incluindo:

- Concessão de permissões e segurança de lançamento baseada em TTL.
- Governança multi-pool (concorrência, taxa, contexto, computação, gastos).
- Estado durável SQLite com recuperação de reinicialização.
- Entradas de auditoria encadeadas por hash e recibos de lançamento.
- Fluxo de assinatura para etapas de implantação/ativação de políticas.
- Isolamento de ferramentas com sub-permissões gerenciadas.
- Modo distribuído Hub/Agente com comportamento local em caso de degradação.
- RBAC, contas de serviço, cotas hierárquicas, controles de justiça.
- Fila de aprovação com rastreamento de revisores.
- Roteamento de intenções com planos de fallback determinísticos.
- Governança de contexto com rastreamento de sumarização gerenciado.
- Automação de segurança (resfriamento, limitação, disjuntor).
- Exportação e verificação de evidências de governança.

### Reforço de Segurança v0.1.0

- Prevenção de injeção de comandos no isolamento de ferramentas (lista de bloqueio de metacaracteres do shell + execução direta).
- Hashing de tokens de contas de serviço (SHA-256 com compatibilidade com texto simples).
- Escrita de auditoria resiliente com rastreamento de falhas (sem mais operações silenciosas de "disparar e esquecer").
- Limites de tamanho de payload em enquadramento de mensagens de pipe (limite de 16 MB).
- Registros e estado do cliente thread-safe (ConcurrentDictionary em todo o sistema).
- Conexões de pipe nomeadas concorrentes (despacho sem bloqueio do listener).
- Proteção contra travessia de caminho em todos os endpoints de exportação.
- Prevenção de injeção de fórmulas CSV em exportações de relatórios.
- Limites de crescimento ilimitado no estado da automação de segurança.
- Rastreamento e exposição de erros de recarregamento de políticas.
- Suporte a chaves externas para assinatura de recibos de governança.

---

## Estrutura da Solução

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

## Como Começar

### Construção e teste

```powershell
dotnet restore LeaseGate.sln
dotnet build LeaseGate.sln
dotnet test LeaseGate.sln
```

### Execução de cenários de exemplo

```powershell
dotnet run --project samples/LeaseGate.SampleCli -- simulate-all
```

### Comandos operacionais

```powershell
dotnet run --project samples/LeaseGate.SampleCli -- daily-report
dotnet run --project samples/LeaseGate.SampleCli -- export-proof
dotnet run --project samples/LeaseGate.SampleCli -- verify-receipt
```

---

## Visão Geral da Integração

Fluxo de aplicação típico:

1. Crie um `AcquireLeaseRequest` com ator, organização/espaço de trabalho, intenção, modelo, uso estimado, ferramentas e contribuições de contexto.
2. Adquira através de `LeaseGateClient.AcquireAsync(...)`.
3. Execute o trabalho do modelo/ferramenta (ou use `GovernedModelCall.ExecuteProviderCallAsync(...)`).
4. Libere através de `LeaseGateClient.ReleaseAsync(...)` com telemetria e resultados reais.
5. Persista/verifique as evidências do recibo quando necessário.

Consulte [docs/Protocol.md](docs/Protocol.md) e [docs/Architecture.md](docs/Architecture.md).

---

## Fluxo de Política GitOps

O código das políticas reside em `policies/` e é carregado através da composição YAML do GitOps.

- `org.yml` para padrões compartilhados e limites globais.
- `models.yml` para listas de permissão de modelos e substituições de modelos do espaço de trabalho.
- `tools.yml` para categorias negadas/que exigem aprovação e requisitos de revisor.
- `workspaces/*.yml` para orçamentos de nível de espaço de trabalho e mapeamentos de capacidade de função.

A validação e a assinatura do pacote são fornecidas por:

- `.github/workflows/policy-ci.yml`
- `scripts/build-policy-bundle.ps1`

---

## Documentação

- [docs/Architecture.md](docs/Architecture.md)
- [docs/Protocol.md](docs/Protocol.md)
- [docs/Policy.md](docs/Policy.md)
- [docs/Operations.md](docs/Operations.md)
- [docs/Development.md](docs/Development.md)
- [CONTRIBUTING.md](CONTRIBUTING.md)
- [CHANGELOG.md](CHANGELOG.md)
- [SECURITY.md](SECURITY.md)

---

## Licença

[MIT](LICENSE)
