# Cari / Counterparty Migration Artifact Sözleşmesi v1.0

- Tarih: 2026-08-29
- Görev: `CARI-MIGRATION-ARTIFACT-CONTRACT`
- Sahip: `CHATGPT-CARI`
- Taban: `main@a3b58b9266ea192dd029889ac60abfe8b2ec194e`
- Durum: **Cari command contract GO / protected shared CI entegrasyonu CODEX-PLATFORM işi**
- Makine-okunur kaynak: `src/backend/Modules/Counterparty/COUNTERPARTY_MIGRATION_ARTIFACT.json`

## 1. Amaç

Counterparty migration SQL artifact'ının hangi EF project, startup project, DbContext, configuration ve output yoluyla üretileceğini exact ve fail-closed biçimde tanımlamak. Böylece shared CI entegrasyonu Cari migration sahipliğini tahmin etmez ve yanlış context/project üzerinden eksik veya başka modül tablolarını sahiplenen script üretmez.

Bu sözleşme `.github/**` dosyalarını değiştirmez, migration eklemez ve production schema davranışı oluşturmaz.

## 2. Exact artifact komutu

Generic CI entegrasyonunun üretmesi gereken semantik komut:

```text
dotnet tool run dotnet-ef migrations script
  --idempotent
  --no-build
  --configuration Release
  --project src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Infrastructure/GoymenERP.Modules.Counterparty.Infrastructure.csproj
  --startup-project src/backend/GoymenERP.Api/GoymenERP.Api.csproj
  --context CounterpartyDbContext
  --output artifacts/counterparty-migration.sql
```

PR #31 merge edilmişse her riskli dotnet sınırından önce onun bootstrap preflight'i ve desteklenen host komutlarında `-noAutoResponse` politikası ayrıca korunur. Bu belge shared workflow implementasyonunu kopyalamaz.

## 3. Migration sahipliği

Counterparty artifact'ı yalnız Counterparty-owned schema nesnelerini üretir. Mevcut iki migration:

1. `20260828123500_InitialCounterparty`
2. `20260828150000_AddCounterpartyWriteSupport`

`AuditEvent` ve `CommandExecution` mapping'leri atomik transaction sidecar'ıdır; Counterparty migration sahipliğine geçmez. Artifact:

- `audit.AuditEvents` tablosunu CREATE etmez,
- `idempotency.CommandExecutions` tablosunu CREATE etmez,
- ilgili platform migration history'sini sahiplenmez.

## 4. Sahiplik sınırı

- `CHATGPT-CARI`: DbContext, migration IDs, schema ownership ve expected script acceptance.
- `CODEX-PLATFORM`: `.github/workflows/ci.yml` içindeki generic artifact step, ordering ve protected CI davranışı.
- Aynı shared workflow üzerinde PR #31 ile eşzamanlı ikinci writer çalışmaz.
- Platform entegrasyonu manifestteki değerleri exact tüketir; context/project/output uydurmaz.
- Cari tarafı shared workflow branch'ine production write yapmaz.

## 5. CI entegrasyon sırası

1. Bu contract exact CI + Codex read-only review'dan geçer.
2. Human merge kararıyla `main`e alınır.
3. PR #31 human review ve merge kapısından geçer; yeni main CI yeşil olur.
4. CODEX-PLATFORM ayrı branch/PR ile manifesti okuyan veya exact değerleri guard'la kilitleyen Counterparty artifact adımını ekler.
5. Artifact üretimi, migration drift ve gerçek SQL acceptance current-main üzerinde yeniden doğrulanır.
6. Workflow değişikliği security/CI yüzeyi olduğu için auto-merge edilmez.

## 6. Kabul kriterleri

- Manifest schema/owner/module exact.
- Project, startup project ve contract source repository içinde gerçek dosyadır.
- Context exact `CounterpartyDbContext` ve design-time factory tarafından desteklenir.
- Configuration exact `Release`.
- Output exact `artifacts/counterparty-migration.sql`; absolute/path traversal/wildcard yok.
- `idempotent=true`, `no_build=true`.
- Runtime migration seti manifest migration IDs ile birebir.
- Generated idempotent SQL mevcut Counterparty script testlerini geçer.
- Audit/Idempotency sidecar tabloları artifact tarafından CREATE edilmez.
- Yeni migration eklendiğinde manifest, contract test, CI artifact ve review exact yeni head üzerinde birlikte güncellenir.

## 7. Kapsam dışı

- PR #31'in bootstrap implementasyonunu bu branch'te değiştirmek
- Migration deployment/promotion/rollback stratejisi
- Production DB provider/edition/topology seçimi
- Counterparty schema'ya yeni alan veya migration eklemek
- PR #36 permission/role upgrade kararı
