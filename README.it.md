<p align="center">
  <a href="README.ja.md">日本語</a> | <a href="README.zh.md">中文</a> | <a href="README.es.md">Español</a> | <a href="README.fr.md">Français</a> | <a href="README.hi.md">हिन्दी</a> | <a href="README.it.md">Italiano</a> | <a href="README.pt-BR.md">Português (BR)</a>
</p>

<p align="center">
  <img src="https://raw.githubusercontent.com/mcp-tool-shop-org/LeaseGate/main/assets/logo-leasegate.png" alt="LeaseGate" width="400">
</p>

<p align="center">
  <a href="https://github.com/mcp-tool-shop-org/LeaseGate/actions/workflows/policy-ci.yml"><img src="https://github.com/mcp-tool-shop-org/LeaseGate/actions/workflows/policy-ci.yml/badge.svg" alt="CI"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-yellow" alt="MIT License"></a>
  <a href="https://mcp-tool-shop-org.github.io/LeaseGate/"><img src="https://img.shields.io/badge/Landing_Page-live-blue" alt="Landing Page"></a>
</p>

**Piattaforma di controllo della governance dell'intelligenza artificiale, progettata per funzionare localmente, che rilascia permessi di esecuzione, applica politiche e budget, e genera prove di governance resistenti alla manomissione.**

---

## Panoramica

- **Concessione di permessi:** Permessi di esecuzione basati su TTL con governance multi-pool.
- **Applicazione delle politiche:** Politiche YAML basate su GitOps con flusso di attivazione/implementazione del pacchetto firmato.
- **Audit resistente alla manomissione:** Entrate a concatenazione hash e ricevute di rilascio.
- **Automazione della sicurezza:** Modelli di raffreddamento, limitazione e interruttore automatico.
- **Isolamento degli strumenti:** Sottoperessi gestiti con prevenzione dell'iniezione di comandi.
- **Modalità distribuita:** Architettura Hub/Agent con fallback locale in caso di problemi.

---

## Stato attuale — v0.1.0

Le fasi 1-5 sono state implementate, testate e rafforzate dal punto di vista della sicurezza, e includono:

- Concessione di permessi e sicurezza del rilascio basata su TTL.
- Governance multi-pool (concorrenza, velocità, contesto, calcolo, spesa).
- Stato SQLite durevole con ripristino in caso di riavvio.
- Entrate di audit a concatenazione hash e ricevute di rilascio.
- Flusso di attivazione/implementazione del pacchetto firmato.
- Isolamento degli strumenti con sottoperessi gestiti.
- Modalità distribuita Hub/Agent con comportamento locale degradato.
- RBAC, account di servizio, quote gerarchiche, controlli di equità.
- Coda di approvazione con tracciamento dei revisori.
- Instradamento degli intenti con piani di fallback deterministici.
- Governance del contesto con tracciamento riepilogativo gestito.
- Automazione della sicurezza (raffreddamento, limitazione, interruttore automatico).
- Esportazione e verifica delle prove di governance.

### Rafforzamento della sicurezza v0.1.0

- Prevenzione dell'iniezione di comandi nell'isolamento degli strumenti (blocco di caratteri speciali della shell + esecuzione diretta).
- Hashing dei token degli account di servizio (SHA-256 con compatibilità all'indietro in testo semplice).
- Scritture di audit resilienti con tracciamento degli errori (nessuna più scrittura silenziosa e "fuoco e dimentica").
- Limiti delle dimensioni del payload per l'incapsulamento dei messaggi tramite pipe (limite di 16 MB).
- Registri e stato del client thread-safe (ConcurrentDictionary in tutto il sistema).
- Connessioni a pipe denominate concorrenti (invio senza bloccare l'ascoltatore).
- Protezione contro la navigazione di percorsi in tutti i punti di esportazione.
- Prevenzione dell'iniezione di formule CSV nelle esportazioni di report.
- Limiti di crescita illimitati per lo stato dell'automazione della sicurezza.
- Tracciamento e segnalazione degli errori di ricaricamento delle politiche.
- Supporto per chiavi esterne per la firma delle ricevute di governance.

---

## Struttura della soluzione

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

## Guida rapida

### Compilazione e test

```powershell
dotnet restore LeaseGate.sln
dotnet build LeaseGate.sln
dotnet test LeaseGate.sln
```

### Esecuzione di scenari di esempio

```powershell
dotnet run --project samples/LeaseGate.SampleCli -- simulate-all
```

### Comandi operativi

```powershell
dotnet run --project samples/LeaseGate.SampleCli -- daily-report
dotnet run --project samples/LeaseGate.SampleCli -- export-proof
dotnet run --project samples/LeaseGate.SampleCli -- verify-receipt
```

---

## Snapshot di integrazione

Flusso di applicazione tipico:

1. Creare una richiesta `AcquireLeaseRequest` con attore, organizzazione/workspace, intento, modello, utilizzo stimato, strumenti e contributi al contesto.
2. Acquisire tramite `LeaseGateClient.AcquireAsync(...)`.
3. Eseguire il lavoro del modello/strumento (o utilizzare `GovernedModelCall.ExecuteProviderCallAsync(...)`).
4. Rilasciare tramite `LeaseGateClient.ReleaseAsync(...)` con telemetria e risultati effettivi.
5. Persistere/verificare le prove della ricevuta quando necessario.

Consultare [docs/Protocol.md](docs/Protocol.md) e [docs/Architecture.md](docs/Architecture.md).

---

## Flusso di lavoro delle politiche GitOps

Le politiche risiedono nella directory `policies/` e vengono caricate tramite la composizione YAML di GitOps.

- `org.yml` per impostazioni predefinite condivise e soglie globali.
- `models.yml` per liste consentite di modelli e sovrascritture di modelli per workspace.
- `tools.yml` per categorie negate/che richiedono approvazione e requisiti dei revisori.
- `workspaces/*.yml` per budget a livello di workspace e mappe di capacità dei ruoli.

La convalida CI e la firma del pacchetto sono fornite da:

- `.github/workflows/policy-ci.yml`
- `scripts/build-policy-bundle.ps1`

---

## Documentazione

- [docs/Architecture.md](docs/Architecture.md)
- [docs/Protocol.md](docs/Protocol.md)
- [docs/Policy.md](docs/Policy.md)
- [docs/Operations.md](docs/Operations.md)
- [docs/Development.md](docs/Development.md)
- [CONTRIBUTING.md](CONTRIBUTING.md)
- [CHANGELOG.md](CHANGELOG.md)
- [SECURITY.md](SECURITY.md)

---

## Licenza

[MIT](LICENSE)
