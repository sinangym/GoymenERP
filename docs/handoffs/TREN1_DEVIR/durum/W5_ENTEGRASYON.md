# tren-1 dalga 5 entegrasyonu — durum raporu

- Dal: `claude/tren-1-entegre-w5` (taban `origin/claude/tren-1` = 318c02000)
- **Head: `7fdf2aa10b4c9304433654439218dd389d3235fc`**, pushlandı; `claude/tren-1` aynı sha'ya fast-forward edildi (`git ls-remote` doğruladı: iki ref de 7fdf2aa10). PR açılmadı.
- Takım head'leri beklenenle aynıydı (değişiklik yok).
- NEEDS-SINAN (entegrasyondan): yok. Takım raporlarından taşınan: aşağıda.

## Merge edilen dallar (sırayla, hepsi `--no-ff`)

| Sıra | Dal | Head | Merge commit |
|---|---|---|---|
| 1 | platform | daade3796 | 5a17d3cd2 |
| 2 | satis | c1b159bc2 | 18d1305fb |
| 3 | alis_cari | 161d4ae03 | 7e86db27c |
| 4 | uretim | 44b8be52b | c2a330c86 |
| 5 | finans | ce00fce92 | 7e7b43981 |
| 6 | kimlik | a134ac646 | 1967fa9dc |
| 7 | stok_lojistik | 85eb14085 | 7fdf2aa10 (çakışma çözümü dahil) |

## Çakışmalar (1 dosya, tek merge: stok_lojistik)

- `src/backend/GoymenERP.Api/ProductRuntime/ProductPriceListHostComposition.cs` — **iki kayıt da korundu**:
  platformun URN-D23 kaydı (`IProductProfileWeightUploadCeilingSourceV1` → `DocumentsProductProfileWeightUploadCeilingSource`)
  ve stokun SAT-D382 kaydı (`IProductPackageStandardCustomerPortV3` → `CounterpartyProductPackageStandardCustomerPortV3`),
  ikisi de `AddProductPriceLists`'ten önce `TryAddScoped`. Politika seçimi gerekmedi.

Pinli/üretilmiş dosyalarda çakışma olmadı (STALE PIN seçimi yapılmadı; hiçbir takım merkezi pin'i değiştirmedi).
EF model snapshot çakışması yok (her snapshot'a tek takım dokundu).

Çakışmasız ama birden çok takımın dokunduğu dosyalar (auto-merge, derleme temiz): `ErpPermissions.cs` (alis_cari, uretim,
finans, kimlik, stok_lojistik), `Program.cs` (satis, alis_cari, uretim, finans, kimlik), `ProductPriceListRegistration.cs`
(platform, stok_lojistik).

## Migration'lar

Görevde istenen iki migration takım dallarında dotnet-ef ile zaten üretilmişti; entegrasyonda yeni migration üretilmedi.

| DbContext | Migration | Down guard | Kaynak |
|---|---|---|---|
| ManufacturingDbContext | `20261116104108_AddManufacturingOrderCancellation` | THROW 51870 (iptal edilmiş emir, PlanReapprovals satırı veya planda >1 tahsis PlanVersion varken), Down'ın ilk işlemi | uretim 44b8be52b |
| SettingsDbContext | `20261116120112_AddReferenceListEntries` | THROW 51886 ([settings].[ReferenceListEntries] / [ReferenceListEntryVersions] satır varken), Down'ın ilk işlemi; Up saf DDL, seed yok | kimlik 552fde81b |

- THROW 51870 ve 51886 depoda yalnız bu migration'larda (+ README/test) geçiyor; çakışan numara yok.
- KIM-S18 migration fact'i `SettingsReferenceListPersistenceModelTests.The_migration_Up_is_pure_DDL_and_its_Down_refuses_first_while_any_entry_or_version_exists` olarak 552fde81b'de geri konmuş; ek değişiklik gerekmedi.
- `dotnet ef migrations has-pending-model-changes` (birleşik ağaç, `--no-build`): ManufacturingDbContext exit 0, SettingsDbContext exit 0 ("No changes have been made to the model since the last migration").

## Derleme sonucu (head 7fdf2aa10)

| Kontrol | Exit |
|---|---|
| `dotnet build` GoymenERP.Api (`-nr:false -p:UseSharedCompilation=false -p:OpenApiGenerateDocumentsOnBuild=false -p:OpenApiGenerateDocuments=false`, flock altında) | 0 (0 uyarı) |
| `dotnet build` Api.IntegrationTests, ArchitectureTests ve 18 UnitTests projesi (aynı bayraklar) | 0 (hepsi, 0 uyarı) |
| frontend `tsc -b` (takeover/wt_tren1 node_modules symlink'iyle; symlink kaldırıldı) | 0 |
| `git diff --check origin/claude/tren-1 HEAD` (318c02000..7fdf2aa10) | 0 |

Derleme düzeltme commit'i gerekmedi. Test koşulmadı (GOV-D9/D10). bin/obj silme komutu izin sınıflandırıcısı tarafından
reddedildi; klasörler gitignored, commit'te yok.

## Birleşik regen listesi (GOV-D9: el ile hash birleştirilmedi)

- **ShellCapabilityRegistryAuthorities** (önce yapılmalı — Api host ve OpenAPI-on-build bu pin olmadan başlamaz; tabanda zaten bayat): Settings provider 1.6 (kimlik); Product provider sürümü + `product.package-standards.approve` (stok); Purchasing `future-allocation.release`, `supplier-returns.record` ve Counterparty `legal_name.propose/approve` (alis_cari); Manufacturing `manufacturing.facts.correct`, `manufacturing.orders.cancel` ve provider kontrolleri `manufacturing.orders.output-correction`, `manufacturing.orders.cancel` (uretim).
- **OpenAPI** (`contracts/openapi/goymen-erp-v1.json` + OpenApiContractGenerationTests):
  - alis_cari: SC33 sipariş kuru GET/POST + approve 409 `purchasing_order_exchange_rate_required`, SC34 `futureSupplyReleases`, S3 gelecek tahsis uçları, S5 tedarikçi iade uçları, SC47 legal-name change-requests/decisions (eski PUT ve `canUpdateLegalName` kalktı);
  - finans: FIN-S9 3 reversal rotası + durum/problem kodları, FIN-SC47 7 credit-accounts ucu ve DTO'ları;
  - kimlik: settings company-policies 6 uç, reference-lists 12 uç, manufacturing profile approve RetiredApproval 409, organization tax-id corrections GET/apply;
  - satis: S2 taksit GET/422, S13 kantar farkı GET/DTO/503, SC34 409 kodları;
  - stok: SC36-c V3 paket standardı uçları, SC36-a/b yeni 409 kodları;
  - uretim: CorrectManufacturingOutputV1, emir iptali `/orders/{orderId}/cancellation`, plan yeniden onayı, yeni 409/422 kodları (`manufacturing_output_label_code_entry_not_allowed`).
  - Frontend generated client (OpenAPI'den üretiliyorsa) — uretim notu.
- **Recovery + release pin'leri** (`tools/generate_recovery_surface_inventory.py`, `contracts/recovery/*.schema.json`, `tests/recovery_surface_inventory`, `tests/release_manifest`): yeni migration'lar —
  Purchasing AddPurchaseOrderExchangeRate, AddPurchaseSupplierReturns; Counterparty AddCounterpartyLegalNameChangeRequests; Finance AddOpeningBalanceReversalsAndMovements, AddCreditAccounts; Settings AddCompanyPolicyVersions, AddReferenceListEntries, AdoptManufacturingProfileSingleFlow; SecurityEvents AddFounderRecoverySecurityEvents; Organization StageCompanyTaxIdCorrections (ClearPlaceholderCompanyVkn gövdesi değişti); Manufacturing AddManufacturingOrderCancellation (+ PlanReapprovals tablosu); Inventory AddInventoryTransferOverageRenewalsAndLossWithdrawals; Product AddProductPackageStandardApprovalAndFiles — ayrıca store/DbContext/partial değişiklikleri ve ProductProfileWeightTableService.cs + yeni Api dosyası (platform).
- **Architecture guard** (`PlatformTenantQueryFilterBypassGuardTests.cs`):
  - fingerprint: PurchasingDbContext, PurchasingStore, PurchaseSupplierReturnStore, PurchaseAcceptanceParticipant, PurchaseOrderExchangeRateReadPort, PurchasingModuleExtensions, CounterpartyDbContext, Api host participant'ları; FinanceDbContext, FinanceModuleExtensions; SettingsDbContext + partial'lar, SettingsModuleExtensions, OrganizationDbContext, IdentityModuleExtensions, SqlLocalCredentialStore/CommandStore, FounderRecoveryService/CompanyLock; SalesModuleExtensions, SalesStore.CustomerReturns/RiskExceptionUsages; InventoryStore, InventoryDbContext, ProductPriceListDbContext, ModuleExtensions; ManufacturingDbContext, ManufacturingStore, ManufacturingInventoryReturnOutputHost (3 GetDbConnection); ProductPriceListRegistration.cs; Program.cs, ProductPriceListHostComposition.cs (birleşim).
  - ReviewedMigrationMutationSources: AddPurchaseOrderExchangeRate (Sql=1, 51833), AddPurchaseSupplierReturns (Sql=1), AddCounterpartyLegalNameChangeRequests (Sql=1), Finance iki migration (51841/51851), AddCompanyPolicyVersions (Sql=1), AddReferenceListEntries (Sql=1, 51886), AdoptManufacturingProfileSingleFlow (51345), AddFounderRecoverySecurityEvents (Sql=1), StageCompanyTaxIdCorrections (Sql=2), ClearPlaceholderCompanyVkn girdisi güncelle/kaldır, AddManufacturingOrderCancellation (Sql=1, 51870), Inventory yeni migration (Sql), Product yeni migration (Sql=2).
- **Relational inventory + RCSI/shard sayıları** (`tools/relational_inventory/*.txt`): purchasing, counterparty, finance, platform (+2), sales, inventory, product — takımlar sıralı ekledi, sayı/fingerprint pin'leri eski; doğrulanmalı.
- **Test-group verifier pin'leri**: yeni sınıflar/fact'ler (ör. PurchaseOrderExchangeRateTests, yeni persistence-model ve SqlServerRelationalAcceptance fact'leri); satış SC33 kayıt testi silindi.
- **Doğrulama**: SecurityEventsPersistenceModelTests snapshot fact'i (KIM-S7 migration/snapshot elle yazıldı) entegrasyonda mutlaka koşulmalı.

## NEEDS-SINAN (takım raporlarından; entegrasyon yeni soru üretmedi)

- KLN-D110 (platform): uygulama içi bildirimler ne kadar süre saklanır (saklama politikası)? Platform bildirim deposu bundan önce kurulamaz.

## Bilinen eksikler (takım raporlarından)

- Uretim: çıktı düzeltme host transaction'ı, iptal migration'ı Down guard'ı (51870) ve SC33 kuyruk okuması için gerçek-SQL fact'i yok (K3 öncesi gerekli).
- Genişletilmiş başlıklı kg/m XLSX birim testi (W4'ten) hâlâ bekliyor olabilir.
