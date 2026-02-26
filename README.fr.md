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

**Plateforme de contrôle de gouvernance de l'IA, conçue pour fonctionner localement, qui attribue des licences d'exécution, applique les politiques et les budgets, et génère des preuves de gouvernance inviolables.**

---

## Aperçu

- **Attribution de licences** : Licences d'exécution basées sur le TTL (Time To Live) avec gouvernance multi-pools.
- **Application des politiques** : Politiques GitOps YAML avec flux de déploiement/activation de paquets signés.
- **Audit inviolable** : Entrées à ajout unique et chaînées par hachage, ainsi que reçus de publication.
- **Automatisation de la sécurité** : Mécanismes de refroidissement, de limitation et de disjoncteur.
- **Isolation des outils** : Sous-licences gérées avec prévention de l'injection de commandes.
- **Mode distribué** : Architecture Hub/Agent avec repli local dégradé.

---

## État actuel — v0.1.0

Les phases 1 à 5 sont implémentées, testées et sécurisées, et comprennent :

- Attribution de licences et sécurité des publications basée sur le TTL.
- Gouvernance multi-pools (concurrence, débit, contexte, calcul, dépenses).
- État SQLite durable avec récupération en cas de redémarrage.
- Entrées d'audit et reçus de publication chaînés par hachage.
- Flux de déploiement/activation de paquets de politiques signés.
- Isolation des outils avec sous-licences gérées.
- Mode distribué Hub/Agent avec comportement local dégradé.
- Contrôle d'accès basé sur les rôles (RBAC), comptes de service, quotas hiérarchiques, contrôles d'équité.
- File d'attente d'approbation avec suivi des examinateurs.
- Routage des intentions avec plans de repli déterministes.
- Gouvernance du contexte avec traçabilité de la synthèse gérée.
- Automatisation de la sécurité (refroidissement, limitation, disjoncteur).
- Exportation et vérification des preuves de gouvernance.

### Sécurité renforcée de v0.1.0

- Prévention de l'injection de commandes dans l'isolation des outils (liste noire des métacaractères de shell + exécution directe).
- Hachage des jetons des comptes de service (SHA-256 avec compatibilité ascendante en texte clair).
- Écriture d'audit robuste avec suivi des erreurs (plus de fonctionnement silencieux).
- Limites de taille des charges utiles sur l'encapsulation des messages (limite de 16 Mo).
- Registres et état client sécurisés par thread (ConcurrentDictionary partout).
- Connexions de tuyaux nommés concurrentes (dispatch sans blocage de l'écouteur).
- Protection contre le parcours de chemin sur tous les points d'exportation.
- Prévention de l'injection de formules CSV dans les exports de rapports.
- Limites de croissance illimitée pour l'état de l'automatisation de la sécurité.
- Suivi et affichage des erreurs de rechargement des politiques.
- Prise en charge des clés externes pour la signature des reçus de gouvernance.

---

## Architecture de la solution

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

## Démarrage rapide

### Construction et test

```powershell
dotnet restore LeaseGate.sln
dotnet build LeaseGate.sln
dotnet test LeaseGate.sln
```

### Exécution de scénarios d'exemple

```powershell
dotnet run --project samples/LeaseGate.SampleCli -- simulate-all
```

### Commandes opérationnelles

```powershell
dotnet run --project samples/LeaseGate.SampleCli -- daily-report
dotnet run --project samples/LeaseGate.SampleCli -- export-proof
dotnet run --project samples/LeaseGate.SampleCli -- verify-receipt
```

---

## Vue d'ensemble de l'intégration

Flux d'application typique :

1. Créer une requête `AcquireLeaseRequest` avec l'acteur, l'organisation/l'espace de travail, l'intention, le modèle, l'utilisation estimée, les outils et les contributions au contexte.
2. Acquérir la licence via `LeaseGateClient.AcquireAsync(...)`.
3. Exécuter le modèle/l'outil (ou utiliser `GovernedModelCall.ExecuteProviderCallAsync(...)`).
4. Libérer la licence via `LeaseGateClient.ReleaseAsync(...)` avec les données télémétriques et les résultats réels.
5. Persister/vérifier les preuves du reçu si nécessaire.

Consulter [docs/Protocol.md](docs/Protocol.md) et [docs/Architecture.md](docs/Architecture.md).

---

## Flux de travail de la politique GitOps

Les sources de politiques se trouvent dans le répertoire `policies/` et sont chargées via la composition YAML GitOps.

- `org.yml` pour les valeurs par défaut partagées et les seuils globaux.
- `models.yml` pour les listes d'autorisation des modèles et les remplacements de modèles par espace de travail.
- `tools.yml` pour les catégories interdites/nécessitant une approbation et les exigences d'examen.
- `workspaces/*.yml` pour les budgets au niveau de l'espace de travail et les cartes de capacités des rôles.

La validation et la signature des paquets sont fournies par :

- `.github/workflows/policy-ci.yml`
- `scripts/build-policy-bundle.ps1`

---

## Documentation

- [docs/Architecture.md](docs/Architecture.md)
- [docs/Protocol.md](docs/Protocol.md)
- [docs/Policy.md](docs/Policy.md)
- [docs/Operations.md](docs/Operations.md)
- [docs/Development.md](docs/Development.md)
- [CONTRIBUTING.md](CONTRIBUTING.md)
- [CHANGELOG.md](CHANGELOG.md)
- [SECURITY.md](SECURITY.md)

---

## Licence

[MIT](LICENSE)
