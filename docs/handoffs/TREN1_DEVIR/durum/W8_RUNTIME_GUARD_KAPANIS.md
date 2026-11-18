# W8 runtime guard kayıt kapanışı

Durum: kaynak kayıtları güncellendi; birleşik mimari test, gerçek SQL ve bağımsız K3 incelemesi henüz bu checkpoint için alınmadı. Bu çalışma yazarın kaynak hizalamasıdır, bağımsız review değildir.

## Kaynak ve kapsam

- Başlangıç: `a8f4440ec88da785ee08ff5d2fa4500e72bfb4e8`.
- Ölçülen yerel kaynak: `7c3a8855c6cec76cfa38ab80d2ff1515a68618cf`; `c43344f97727fd88bd33402b731c27a39c7197e3` şirket predicate düzeltmesi ve `fc7f70cdb61c31f9d6e28972d2aa89b297fc1dce` açık DTO türleri normal birleştirmeyle alındı.
- Yalnız `PlatformTenantQueryFilterBypassGuardTests.cs` içindeki `ReviewedRuntimeDatabaseBypassSources` ve `ReviewedRuntimeContextConstructions` değişti. Tarayıcı, assertion, diğer registry, uygulama kodu ve politikalar değişmedi.
- İlk başarısızlık listesinin 92 adayı gerçek kaynak/owner/şirket/transaction sınırlarıyla okundu. Önceki notlar kaynak yardımcısı olarak kullanıldı; inceleme imzası veya çalışma kanıtı sayılmadı.

## Sonuç

| İşlem | Sayı |
|---|---:|
| Runtime registry, önce → sonra | 139 → 221 |
| Yeni, kaynak gerekçeli runtime girdisi | 82 |
| Mevcut girdilerin güncellenen parmakizi | 35 |
| Yeni girdi gerektirmeyen mevcut aday | 7 |
| Gerçek API kalmadığı için eklenmeyen aday | 3 |
| DTO düzeltmesi sonrası sıfır olan context kaydı kaldırıldı | 22 |
| Gerçek context sayısı düzeltildi | 1 |
| Yeni gerçek owner context kaydı | 16 |
| Context registry, önce → sonra | 53 → 47 |

Üç sıfır aday: `InventoryNegativeStockAttemptRecording.cs`, `InventoryPhysicalStockRefusalProblem.cs`, `InventoryNegativeStockAttempts.cs`. Bunlardaki `FindEvidence` typed exception incelemesidir; DbSet erişimi değildir.

Mevcut API sayısındaki iki değişiklik kaynakla ayrıştırıldı: `PurchaseGoodsReceiptHost` içindeki `GetDbConnection` 6→7, aynı Purchasing işlemi içindeki typed receipt-cost owner oturumudur. `SettingsCollateralTypeAdministration` içindeki bir `FindAsync`, şirket filtreli `CollateralTypeReads` helper çağrısıdır; DbSet.Find değildir.

Yeni ham sorgu yüzeyleri yalnız Finance/Inventory/Purchasing/Sales outbox sağlayıcılarındaki sabit `SELECT MIN_ACTIVE_ROWVERSION() AS [Value]` görünürlük ufkudur. Tenant satırları ayrıca CompanyId filtreli EF ile okunur; ham tenant sorgusu/DML veya dispatcher aktivasyonu eklenmedi. Bu kayıtların K3 incelemesi bekliyor.

Üç Inventory Local kaynağında staged satır seçimlerinin açık CompanyId predicate taşıdığı son düzeltme esas alındı. Global Product sınıflaması ve global CLI kimlik kurtarma kaynakları tenant verisi gibi gösterilmedi; gerçek global owner/actor/başlatan şirket sınırları açıklamada korundu.

W5’teki 12 bypass girdisinin API sayıları ve 5 context girdisinin `1 / 4 / 6 / 4 / 11` sayıları korundu. Önceki K3 bekleyen durumları kapatılmadı. Yeni runtime ve gerçek context girdileri de `K3 review pending` olarak işaretlendi.

## Gerçek context kayıtları

`PurchaseAcceptanceParticipant` için iki DTO artık açık türdedir; gerçek borrowed Purchasing context sayısı 3→1 olarak ölçüldü. Yeni gerçek kayıtlar:

| Kaynak | Context | Sayı |
|---|---|---:|
| `AccountingPeriodStatusReadSession.cs` | `AccountingDbContext` | 1 |
| `DocumentBrandingService.cs` | `DocumentsDbContext` | 1 |
| `FinancePeriodCarryForwardSession.cs` | `FinanceDbContext` | 1 |
| `LocalTwoFactorOperatorRecoveryServiceV1.cs` | `IdentityDbContext` | 2 |
| `InventoryGoodsReceiptReversalParticipant.cs` | `InventoryDbContext` | 1 |
| `InventoryHistoricalSalesLinksV1.cs` | `InventoryDbContext` | 1 |
| `InventoryManufacturingCostGenerationParticipant.cs` | `InventoryDbContext` | 1 |
| `InventoryManufacturingCostReadSession.cs` | `InventoryDbContext` | 1 |
| `InventoryNegativeStockValidationGuard.cs` | `InventoryDbContext` | 1 |
| `InventorySubcontractDeliveryParticipant.cs` | `InventoryDbContext` | 1 |
| `LogisticsSubcontractDeliveryReadSession.cs` | `LogisticsDbContext` | 1 |
| `ManufacturingSubcontractCompletionReadSessionV2.cs` | `ManufacturingDbContext` | 1 |
| `PurchaseReceiptAdjustmentParticipant.cs` | `PurchasingDbContext` | 1 |
| `PurchaseReceiptCostProof.cs` | `PurchasingDbContext` | 1 |
| `SalesSubcontractInvoiceClaimReadSession.cs` | `SalesDbContext` | 1 |
| `SalesSubcontractServiceOrderReadSession.cs` | `SalesDbContext` | 1 |

Sıfır sayımlı kaldırılan context kayıtları: `ManufacturingInventoryInputIssueHost.cs`, `ManufacturingInventoryReturnOutputHost.cs`, `ProductRuntimeExtensions.cs`, `PurchaseGoodsReceiptBatchHost.cs`, `PurchaseGoodsReceiptHost.cs`, `FinanceOfficialSalesInvoiceHost.cs`, `AccountingStore.cs`, `ApprovedAccountingSourceCatalogStoreV1.cs`, `FinanceAllocationReadService.cs`, `FinanceInvoiceOpenItemCancellationService.cs`, `FinanceOfficialInvoiceImportReadPortV1.cs`, `FinanceOfficialSalesInvoiceImportDocumentsV2.cs`, `FinanceOfficialSalesInvoiceReadPortV1.cs`, `FinanceOfficialSalesInvoiceServiceV1.cs`, `FinanceOpenItemSourceAdmissionService.cs`, `FinancePartyCollateralService.cs`, `FinancePartyCreditPanelService.cs`, `PartyLedgerReadService.cs`, `InventoryLabelDispositionReader.cs`, `LogisticsTransferOutgoingDocumentStore.cs`, `PurchaseFutureSupplyReadPort.cs`, `PurchasingStore.cs`.

## Son kaynak bağımlılıkları

Aşağıdaki committed kaynaklar bu dalda **NOT_INTEGRATED**; parmakizleri depodaki immutable blob üzerinden mevcut guard üreticisiyle ölçüldü. Kaynak düzeltmeleri ayrı sahip dalında kaldı. Birleştiren koordinatör bu commitleri almalı ve son birleşik kaynakta üretici/testi yeniden çalıştırmalıdır.

| Kaynak | Ölçülen commit | Guard etkisi |
|---|---|---|
| `PurchasingStore.IncomingInvoices.cs`, `PurchasingStore.InvoiceImports.cs` | `4c77e73cd` | İki yeni runtime girdisinin son kaynak hash’i; API sayıları aynı |
| `SalesStore.OfficialCancellations.cs` | `1e78622ad` | API sayısı 0; yeni runtime girdi yok |
| `FinanceCashAccountService.cs` | `754c5d17d` | Mevcut kaydın hash’i; SqlClient sayısı 1 aynı |

## Doğrulama sınırı

- Mevcut derlenmiş guardın `FindDirectDatabaseBypassOccurrences`, `ComputeReviewedSourceFingerprint` ve context ölçüm yordamları dışarıdaki salt okuma yardımcılarıyla kullanıldı; yeni tarayıcı semantiği veya paket eklenmedi.
- Kaynak karşılaştırması: iki sözlüğün dışı değişmemiş; duplicate key yok; 221 runtime girdisinin API sayısı/hash’i ölçümle eşleşmiş; context ekleme/çıkarma ve W5 koruma kontrolleri exit 0.
- `git diff --check`: exit 0.
- .NET/Node derleme ve mimari test: **NOT_RUN**. Gerçek SQL, browser, CI ve bağımsız exact-head K3 review: **NOT_RUN**.
- Bu çıktı review/merge/activation yetkisi veya guard test PASS iddiası değildir. Son birleşik kaynakta eski ve yeni pinlerin doğrulanması koordinatörün kapanış adımındadır.
