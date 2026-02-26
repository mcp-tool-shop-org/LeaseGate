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

**ローカルファーストのAIガバナンス制御システム。実行権限の発行、ポリシーと予算の適用、改ざん防止機能を持つガバナンス証拠の生成を行います。**

---

## 概要

- **実行権限の付与:** TTL（Time To Live）に基づいた実行権限と、複数のプールによるガバナンス
- **ポリシー適用:** GitOps YAMLによるポリシー適用。署名されたバンドルのステージング/アクティベーションフロー
- **改ざん防止監査:** ハッシュチェーンによる追記専用のエントリと、リリースレシート
- **安全対策の自動化:** クールダウン、制限、サーキットブレーカーのパターン
- **ツールの隔離:** ガバナンス対象のサブリースによる、コマンドインジェクションの防止
- **分散モード:** ハブ/エージェントアーキテクチャ。ローカルでのフォールバック機能

---

## 現在の状態：v0.1.0

フェーズ1～5は実装、テスト済み、セキュリティ強化済みです。

- 実行権限の付与と、TTLに基づいたリリース時の安全対策
- 複数のプールによるガバナンス（同時実行数、レート、コンテキスト、計算リソース、消費量）
- SQLiteによる永続的な状態管理と、再起動時の復旧機能
- ハッシュチェーンによる監査エントリと、リリースレシート
- 署名されたポリシーバンドルのステージング/アクティベーションフロー
- ガバナンス対象のサブリースによるツールの隔離
- ハブ/エージェント分散モード。ローカルでのフォールバック動作
- RBAC（Role-Based Access Control）、サービスアカウント、階層的なクォータ、公平性制御
- レビュー担当者による承認キュー
- 意図に基づいたルーティングと、確実なフォールバックプラン
- ガバナンス対象の要約追跡によるコンテキスト管理
- 安全対策の自動化（クールダウン、制限、サーキットブレーカー）
- ガバナンス証明のインポートと検証

### v0.1.0のセキュリティ強化

- ツールの隔離におけるコマンドインジェクションの防止（シェルメタ文字のブロックリスト + ダイレクト実行）
- サービスアカウントのトークンハッシュ化（SHA-256。互換性のためにプレーンテキストもサポート）
- 障害追跡機能付きの堅牢な監査ログ（サイレントなエラーは発生しない）
- パイプメッセージのフレームにおけるペイロードサイズの制限（最大16MB）
- スレッドセーフなレジストリとクライアントの状態（ConcurrentDictionaryを使用）
- 同時接続可能な名前付きパイプ（ブロックしないリスナーによるディスパッチ）
- すべてのエクスポートエンドポイントにおけるパストラバーサル保護
- レポートのエクスポートにおけるCSVフォーミュラインジェクションの防止
- 安全対策の状態における無制限な成長の抑制
- ポリシーのリロードエラーの追跡と表示
- ガバナンスレシートの署名のための外部キーサポート

---

## システム構成

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

## クイックスタート

### ビルドとテスト

```powershell
dotnet restore LeaseGate.sln
dotnet build LeaseGate.sln
dotnet test LeaseGate.sln
```

### サンプルシナリオの実行

```powershell
dotnet run --project samples/LeaseGate.SampleCli -- simulate-all
```

### 操作コマンド

```powershell
dotnet run --project samples/LeaseGate.SampleCli -- daily-report
dotnet run --project samples/LeaseGate.SampleCli -- export-proof
dotnet run --project samples/LeaseGate.SampleCli -- verify-receipt
```

---

## 統合の概要

典型的なアプリケーションフロー：

1. `AcquireLeaseRequest`を作成します。アクタ、組織/ワークスペース、意図、モデル、推定使用量、ツール、コンテキスト情報を含めます。
2. `LeaseGateClient.AcquireAsync(...)`を使用して取得します。
3. モデル/ツールの実行（または、`GovernedModelCall.ExecuteProviderCallAsync(...)`を使用します）。
4. `LeaseGateClient.ReleaseAsync(...)`を使用してリリースします。実際のテレメトリーデータと結果を含めます。
5. 必要に応じて、レシートの証拠を永続化/検証します。

[docs/Protocol.md](docs/Protocol.md) および [docs/Architecture.md](docs/Architecture.md) を参照してください。

---

## GitOpsポリシーワークフロー

ポリシーのソースは `policies/` にあり、GitOps YAMLによる構成で読み込まれます。

- `org.yml`: 共通のデフォルト値とグローバルな閾値
- `models.yml`: モデルの許可リストと、ワークスペースごとのモデルのオーバーライド
- `tools.yml`: 拒否/承認が必要なカテゴリと、レビュー担当者の要件
- `workspaces/*.yml`: ワークスペースごとの予算と、ロールごとの機能マップ

CIによる検証とバンドル署名は、以下のファイルによって提供されます。

- `.github/workflows/policy-ci.yml`
- `scripts/build-policy-bundle.ps1`

---

## ドキュメント

- [docs/Architecture.md](docs/Architecture.md)
- [docs/Protocol.md](docs/Protocol.md)
- [docs/Policy.md](docs/Policy.md)
- [docs/Operations.md](docs/Operations.md)
- [docs/Development.md](docs/Development.md)
- [CONTRIBUTING.md](CONTRIBUTING.md)
- [CHANGELOG.md](CHANGELOG.md)
- [SECURITY.md](SECURITY.md)

---

## ライセンス

[MIT](LICENSE)
