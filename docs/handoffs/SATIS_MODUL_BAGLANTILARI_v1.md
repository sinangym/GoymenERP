# Satış modül bağlantıları analizi v1

Tarih: 2026-11-02 · Yazan: Satış şeridi (Claude) · Taban: `origin/main` @ `1db70046` (#896 sonrası; #898 fason, #906 kur
kaydı dahil)

Bu not bir **durum fotoğrafıdır**; karar değildir. Karar dosyaları değiştirilmedi. Kaynak önceliği `AGENTS.md`'deki gibidir:
modüler monolit, hiçbir modül başkasının tablosuna yazmaz; modüller arası bağ yalnız sürümlü port (`*PortV1`, `*ReaderV1`,
`*ResolverV1`) veya outbox olayıyla kurulur.

## Okuma yöntemi

- `src/backend` üzerinde `PortV1`, `ReaderV1`, `ResolverV1`, `Outbox`, `IntegrationEvent`, `AddScoped`, `TryAdd` taraması;
  Satış'ın tüketici fabrikası `SalesModuleExtensions.cs` (`CreateDraftService`, `CreateDispatchReadService`) ve host kayıtları
  `GoymenERP.Api/Program.cs` satır satır okundu.
- Test kanıtı `tests/` altında arandı: birim/sözleşme testi ve `[SqlServerFact]` gerçek-SQL kabul testi ayrı sayıldı.
- **Olay (outbox) durumu:** Satış'ın outbox tablosu yok (`SalesDbContext.cs` içinde `Outbox` geçmez); Satış hiçbir
  `sales.*.v1` olayı yayımlamaz ve hiçbir olay tüketmez. Bugünkü bütün bağlar **senkron in-process port**tur. Outbox'ı olan
  modüller yalnız Stok (`InventoryDbContext.OutboxMessages`) ve İş (`WorkDbContext.OutboxMessages`).

Durum işaretleri: ✅ uygulama + DI kaydı + test var · 🟡 kısmi (yalnız arayüz, fail-closed/`Unavailable` yer tutucu, tüketici
yok veya test eksik) · ❌ yok.

Kısaltmalar: `SME` = `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/SalesModuleExtensions.cs`,
`SC/` = `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Application/Contracts/`, `API/` = `src/backend/GoymenERP.Api/`,
`IT/` = `tests/GoymenERP.Api.IntegrationTests/`, `SUT/` = `tests/GoymenERP.Sales.UnitTests/`.

## Bağlantı tablosu

### 1. Finans

| Bağlantı | Üretici | Tüketici | Port/olay | main'de impl + DI | Test | Durum |
|---|---|---|---|---|---|---|
| 1a. Satış faturası → Finans alacak açık kalemi | Satış (fatura çekirdeği) | Finans | Öneri: `sales.invoice.finalized.v1` + senkron `IFinanceInvoiceOpenItemAdmissionPortV1` (Finans API-10, `docs/delivery/FINANS_HAZIRLIK_PLANI_v1.md` §4) | **Yok.** Satış'ta fatura varlığı yok; Finans açık kalemleri yalnız CSV içe aktarılan `official.invoice-installment.v1` kaynağından doğuyor (`Modules/Finance/.../FinanceOpenItemSourcePolicy.cs`). Port adı kodda geçmiyor | — | ❌ |
| 1b. Finans → Satış risk okuması: açık alacak − dağıtılmamış tahsilat | Finans | Satış (MOD-01 / API-06 risk kontrolü) | `ISalesPartyRiskReadPortV1` (`SC/ISalesPartyRiskReadPortV1.cs`) | `FinanceSalesPartyRiskReadPortV1` (`Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceSalesPartyRiskReadPortV1.cs`), `FinanceModuleExtensions.cs:90` `AddScoped`; Satış `SME` `GetService` (yoksa `Unavailable`, risk asla "Within" değil) | `tests/GoymenERP.Finance.UnitTests/FinanceSalesPartyRiskReadPortV1Tests.cs`, `SUT/SalesDraftServiceTests.RiskCheck.cs`, SQL: `IT/SalesRiskCheckSqlServerRelationalAcceptanceTests.cs`, `IT/FinanceRecordSqlServerRelationalAcceptanceTests.cs` | ✅ |
| 1c. Finans → Satış: kredi limiti ve teminat | Finans | Satış | Aynı port, `CreditLimit` / `Collateral` figürleri | Port her zaman `NoSource` döner (`finance_credit_limit_owner_missing`); master yok (Finans API-85, OPEN-010) | Birim testte `NoSource` doğrulanıyor | 🟡 |
| 1d. Finans → Satış: açık kalem kapandı / ödeme kaydedildi olayı (risk tazeleme) | Finans | Satış | Öneri: `finance.open-item.settled.v1`, `finance.payment.recorded.v1` | Finans'ta outbox yok; Satış inbox yok. Risk bugün her okumada port üzerinden taze hesaplandığı için işlevsel boşluk yok | — | ❌ (gerek: dağıtıcı ERP-000F) |
| 1e. Sipariş iptali etki kategorisi "eşleşmemiş avans/dekont" | Finans | Satış (DEC-SAL-005) | `SalesCancellationImpactSourcesV1.UnmatchedAdvances = "finance.unmatched-advances"` (`SC/SalesDraftCancellationContractsV1.cs`) | Port yok; `SalesDraftService.Cancellation.cs:270` `_ => NoSource` dalına düşüyor (engellemiyor) | — | ❌ |

### 2. Üretim

| Bağlantı | Üretici | Tüketici | Port/olay | main'de impl + DI | Test | Durum |
|---|---|---|---|---|---|---|
| 2a. S-1 Sipariş iptali / zorla kapatmada "bağlı üretim" | Üretim | Satış | Öneri: Satış `ISalesOrderLinkedSupplyReadPortV1` ← Üretim `IManufacturingSalesOrderLinkReadPortV1` (`docs/handoffs/URETIM_SATIS_DEVIR_v1.md` S-1) | İki arayüz de kodda yok. `LinkedProduction = "manufacturing.linked-orders"` → `NoSource` (`SalesDraftService.Cancellation.cs:270`, `SalesDraftService.ForceClosure.cs`) | — | ❌ |
| 2b. S-2 Kaynak planı (Satış API-11, DEC-SAL-004) — Satış ekranı/kaydı | Satış | Satış / Üretim | Satır kaynak stratejisi kaydı | Yalnız başlıkta opak `SourceStrategy` sahip referansı var (`SC/SalesDraftContractsV1.cs:15`, `SalesRows.cs:70`); satır bazlı kaynak planı, ekran ve uç yok | — | ❌ |
| 2c. S-2 Üretim planı satırı ↔ sipariş satırı bağı (Üretim API-16) | Satış | Üretim | `ISalesOrderLineReferencePortV1.GetApprovedLineAsync` (`SC/ISalesOrderLineReferencePortV1.cs`) | Satış tarafı hazır: `SalesOrderLineReferencePort`, `SME:80` `AddScoped`. Üretim'de tüketici yok (Manufacturing'de `Sales` referansı yalnız yorum; Üretim U2-b) | Satış portu: `SUT/` + çok sayıda SQL testi (aşağıda 3c) | 🟡 |
| 2d. S-3 Fason hizmet siparişi okuma (#898) | Satış | Üretim (URT-D22) | `ISalesSubcontractServiceOrderReadPortV1`, sözleşme `sales.subcontract-service-order.v1` (`SC/ISalesSubcontractServiceOrderReadPortV1.cs:28`) | `SalesSubcontractServiceOrderReadPort` (`.../Persistence/SalesSubcontractServiceOrderReadPort.cs`), `SME:83` `AddScoped`. Üretim tüketici adaptörü main'de yok (Üretim U5-b) | `SUT/SalesSubcontractServiceOrderReadPortContractTests.cs`, `SUT/SalesOrderDraftSubcontractTests.cs`, SQL: `IT/SalesSubcontractServiceOrderSqlServerRelationalAcceptanceTests.cs` | 🟡 (Satış ✅, tüketici yok) |
| 2e. Fason siparişin üretim/müşteri malı bağı (cari değişikliği kapısı) | Üretim (host adaptörü) | Satış (SAT-D175) | `ISalesSubcontractOrderBindingReadPortV1` (`SC/ISalesSubcontractServiceOrderReadPortV1.cs:154`) | **Uygulama ve DI kaydı yok.** `SME:175` `GetService` → null → fason siparişin carisi değiştirilemez (503, fail-closed) | Yalnız Satış birim testi (`SUT/SalesDraftServiceTests.Subcontract.cs`, sahte port); SQL yok | 🟡 |
| 2f. Üretim → Satış durum dönüşü (planlandı/üretildi/kapandı) | Üretim | Satış | Tanımlı port/olay yok | — | — | ❌ |

### 3. Lojistik ve Stok

| Bağlantı | Üretici | Tüketici | Port/olay | main'de impl + DI | Test | Durum |
|---|---|---|---|---|---|---|
| 3a. Teslim edilen / kalan miktar (AT-024) | Lojistik | Satış | `ISalesOrderDeliveryReadPortV1` (`SC/ISalesOrderDeliveryReadPortV1.cs`) | `LogisticsSalesOrderDeliveryReadPort`, `Modules/Logistics/.../LogisticsModuleExtensions.cs:60` | `tests/GoymenERP.Logistics.UnitTests/LogisticsSalesOrderDeliveryReadPortTests.cs`, SQL: `IT/SalesDeliveryProgressSqlServerRelationalAcceptanceTests.cs`, `IT/SalesOrderStatusSqlServerRelationalAcceptanceTests.cs` | ✅ |
| 3b. Yükleme talimatı satırları (iptal etkisi, kalan değişikliği) | Lojistik | Satış (DEC-SAL-005) | `ISalesOrderLoadingReadPortV1` (`SC/SalesDraftCancellationContractsV1.cs:44`) | `LogisticsSalesOrderLoadingReadPort`, `LogisticsModuleExtensions.cs:63` | `tests/GoymenERP.Logistics.UnitTests/LogisticsSalesOrderLoadingReadPortTests.cs`, SQL: `IT/SalesRemainderChangeSqlServerRelationalAcceptanceTests.cs`, `IT/SalesOrderCancellationSqlServerRelationalAcceptanceTests.cs` | ✅ |
| 3c. Onaylı sipariş satırı referansı (yükleme talimatı, sevk gerçekleşmesi, fiziksel tahsis) | Satış | Lojistik, Stok (host) | `ISalesOrderLineReferencePortV1` | `SalesOrderLineReferencePort`, `SME:80`; tüketiciler `LogisticsModuleExtensions.cs:106`, `API/Endpoints/InventoryPhysicalAllocationHostPorts.cs:51` | `tests/GoymenERP.Logistics.UnitTests/LogisticsInstructionServiceTests.cs`, `IT/InventoryPhysicalAllocationHostServiceTests.cs`, SQL: `IT/LogisticsInstructionSqlServerRelationalAcceptanceTests.cs`, `IT/InventoryAllocationSqlServerRelationalAcceptanceTests.cs` (+ 9 SQL dosyası daha) | ✅ |
| 3d. İrsaliye listesi ve fatura adayı sevkiyatlar (API-30 / API-31) | Lojistik | Satış | `ISalesDispatchNoteReadPortV1`, `ISalesInvoiceableShipmentReadPortV1` (`SC/ISalesDispatchReadPortsV1.cs`) | `LogisticsSalesDispatchReadPorts`, `LogisticsModuleExtensions.cs:77-78`; Satış tarafı `SalesOrderDispatchReferenceReader` (`SME:87`) | `tests/GoymenERP.Logistics.UnitTests/LogisticsSalesDispatchReadPortsTests.cs`, `SUT/SalesDraftServiceTests.Dispatches.cs`, SQL: `IT/SalesDispatchSqlServerRelationalAcceptanceTests.cs` | ✅ |
| 3e. Onaylı müşteri iadeleri (SAT-D8 / API-10) | Lojistik | Satış | `ISalesOrderCustomerReturnReadPortV1` | `LogisticsSalesOrderCustomerReturnReadPort` (`.../LogisticsCustomerReturnReadPorts.cs:72`), `LogisticsModuleExtensions.cs:73` | SQL: `IT/SalesCustomerReturnsSqlServerRelationalAcceptanceTests.cs` (ayrı Lojistik birim testi yok) | ✅ |
| 3f. "Faturalanmamış irsaliye" iptal etkisi | Lojistik / Satış fatura | Satış | `UninvoicedDispatchNotes = "invoicing.uninvoiced-dispatch-notes"` | Lojistik'in aktif onaylı irsaliye sayısından okunuyor; fatura tüketen sahip olmadığından onaylı her aday "faturalanmamış" sayılıyor | 3b'deki testler | 🟡 (fatura çekirdeği gelince kesinleşir) |
| 3g. Aktif fiziksel tahsis (iptal etkisi) | Stok | Satış | `ISalesOrderAllocationReadPortV1` (`SC/SalesDraftCancellationContractsV1.cs:19`) | `InventorySalesOrderAllocationReadPort`, `Modules/Inventory/.../InventoryModuleExtensions.cs:59` | `tests/GoymenERP.Inventory.UnitTests/InventorySalesOrderAllocationReadPortTests.cs`, SQL: `IT/InventorySalesOrderCancellationSqlServerRelationalAcceptanceTests.cs`, `IT/InventoryAllocationChangeSqlServerRelationalAcceptanceTests.cs` | ✅ |
| 3h. Miktar bazlı stok rezervasyonu | Stok | Satış | Tanımlı port yok | main'de yalnız etiket bazlı fiziksel tahsis var; ayrı rezervasyon kavramı yok | — | ❌ (kapsam kararı Stok/Satış planında) |

### 4. Ayarlar

| Bağlantı | Üretici | Tüketici | Port/olay | main'de impl + DI | Test | Durum |
|---|---|---|---|---|---|---|
| 4a. Döviz kuru ve kur günü (#906, SAT-D47/D54/D55) | Ayarlar | Satış (risk çevrimi) | `ISettingsExchangeRateResolverV1` | `SettingsExchangeRateResolverV1`, `Modules/Settings/.../SettingsModuleExtensions.cs:55`; TCMB kaynakları yapılandırmayla kapalı (`DisabledExchangeRateBulletinSource`) | `SUT/SalesDraftServiceTests.RiskCheck.cs` (sahte), SQL: `IT/SettingsExchangeRateSqlServerRelationalAcceptanceTests.cs`. Satış+kur birlikte SQL testi yok | 🟡 (bağ ✅; Satış ucunda gerçek-SQL kanıtı eksik) |
| 4b. Satış iş günü (SAT-D10) | Satış (kendi) | — | `SalesBusinessDay` (Europe/Istanbul) — Ayarlar portu değil | `Modules/Sales/GoymenERP.Modules.Sales.Domain/SalesBusinessDay.cs` | Satış birim testleri | ✅ (modüller arası bağ değil) |
| 4c. Satır vergisi (MOD-03, SAT-D93/D139/D140) | Ayarlar | Satış | `ISettingsLineTaxResolverV1` | `SettingsTaxMasterReader`, `SettingsModuleExtensions.cs:47` | SQL: `IT/SettingsTaxMasterSqlServerRelationalAcceptanceTests.cs`, `IT/SalesOrderTaxSqlServerRelationalAcceptanceTests.cs` | ✅ |
| 4d. KDV oranı / tevkifat listeleri (SAT-D181) | Ayarlar | Satış | `ISettingsTaxRateListReaderV1` | `SettingsTaxRateListReader`, `SettingsModuleExtensions.cs:50` (veri girilene kadar boş liste) | `SUT/SalesDraftServiceTests.Tax.cs` (sahte); okuyucunun kendi SQL testi yok | 🟡 |
| 4e. Satış politika kaydı (tolerans, iskonto limiti; SAT-D2/D5/D61…D64) | Ayarlar | Satış | `ISettingsSalesPolicyProviderV1` | `API/Program.cs:198` onaylı katalog; `SettingsModuleExtensions.cs:36` `Replace` → `SettingsSalesPolicyProviderV1` | `tests/GoymenERP.Settings.UnitTests/ApprovedSalesPolicyProviderTests.cs`, SQL: `IT/SalesRequestSqlServerRelationalAcceptanceTests.cs` | ✅ |
| 4f. İskonto limit grubu üyeliği (SAT-D63/D66) | Ayarlar (+ Kimlik) | Satış | `ISalesDiscountLimitGroupSourceV1` ← `ISettingsSalesDiscountLimitGroupMembershipReaderV1` | Host `SalesDiscountLimitGroupHost` (`API/Endpoints/SettingsSalesDiscountLimitHosts.cs`), `Program.cs:329` | `IT/SettingsSalesDiscountLimitPortTests.cs`, SQL: `IT/SettingsSalesDiscountLimitSqlServerRelationalAcceptanceTests.cs` | ✅ |
| 4g. Teklif politikası (kayıp nedeni, varsayılan geçerlilik; SAT-D25/D39) | Ayarlar | Satış | `ISettingsSalesQuotePolicyProviderV1` | `ApprovedSettingsSalesQuotePolicyProvider`, `Program.cs:204`; katalog **boş** → kayıp 503, geçerlilik önerisi yok | Satış teklif testleri, SQL: `IT/SalesQuoteSqlServerRelationalAcceptanceTests.cs` | 🟡 (kalıcı kayıt/ekran yok; veri Sinan'da) |
| 4h. Para profilleri / para birimi adayları (SAT-D18) | Ayarlar | Satış | `ISettingsPrerequisiteProvider`, `ISettingsMoneyCandidateReader`, `ISettingsMoneyCandidateReaderV2` | `ApprovedSettingsPrerequisiteProvider`, `Program.cs:191-195` (onaylı statik katalog) | `tests/GoymenERP.Settings.UnitTests/ApprovedMoneyProfileProviderTests.cs`, `SUT/SalesReferenceReadHandlerTests.cs`, `IT/SettingsHostWiringTests.cs` | ✅ |

### 5. Ürün, Cari, İş onayları, Kimlik, Alış

| Bağlantı | Üretici | Tüketici | Port/olay | main'de impl + DI | Test | Durum |
|---|---|---|---|---|---|---|
| 5a. Ürün satış uygunluğu / aday listesi | Ürün | Satış | `IProductSalesEligibilityV1`, `IProductSalesCandidatesV1` | `API/ProductRuntime/ProductRuntimeExtensions.cs:101`; runtime kapalıyken `UnconfiguredProductEligibility` (503) | `tests/GoymenERP.Product.UnitTests/ProductEligibilityHostCompositionTests.cs`, `IT/SalesProductCandidateHttpAcceptanceTests.cs` | ✅ |
| 5b. Fiyat listesi kademe zinciri (SAT-D9, MOD-02) | Ürün | Satış | `IProductQuantityPriceReferenceReaderV2` | `Modules/Product/.../PriceLists/ProductPriceListRegistration.cs:50` — yalnız Ürün runtime açıkken; kapalıyken kayıt yok → Satış 503 | `SUT/SalesDraftServiceTests.LinePricing.cs`, `IT/ProductPriceListHostCompositionTests.cs`, SQL: `IT/ProductPriceListSqlServerRelationalAcceptanceTests.cs` | ✅ |
| 5c. Kalem tipi okuması (SAT-D176, fason hizmet satırı) | Ürün | Satış (+ Ayarlar vergi) | `IProductItemTypeReaderV1` | `ProductPriceListRegistration.cs:55`; runtime kapalıyken `UnavailableProductItemTypeService` (`API/ProductRuntime/ProductPriceListHostComposition.cs:40-45`, fail-closed) | `SUT/SalesDraftServiceTests.Subcontract.cs`, `IT/SettingsHostWiringTests.cs`; fason SQL testi dolaylı (`IT/SalesSubcontractServiceOrderSqlServerRelationalAcceptanceTests.cs`) | ✅ |
| 5d. Müşteri hesabı doğrulama | Cari | Satış | `ISalesPartyAccountPortV1` | Host `CounterpartySalesPartyAccountPortV1` (`API/Endpoints/SalesEndpoints.cs:2052`), `Program.cs:219` (zorunlu) | `IT/SalesReferenceHttpAcceptanceTests.cs`, SQL: `IT/SalesDraftApiSqlServerRelationalAcceptanceTests.cs` | ✅ |
| 5e. Teslim adresi / ilgili kişi (SALES-A2a) | Cari | Satış | `ISalesPartyContactInformationPortV1` | Aynı host adaptörü, `Program.cs:222` | `SUT/SalesReferenceReadHandlerTests.cs`, SQL: `IT/SalesDraftSqlServerRelationalAcceptanceTests.cs` | ✅ |
| 5f. Müşteri seçimi ve kayıt gösterimi | Cari | Satış | `ISalesAccountSelectionSourceV1`, `ISalesPartyDisplaySourceV1` | `CounterpartySalesAccountSelectionSourceV1` (`SalesEndpoints.cs:1989`, `Program.cs:207`); `SalesPartyDisplayHost` (`API/Endpoints/SalesPartyDisplayHost.cs`, `Program.cs:320`) | `IT/SalesDraftHttpAcceptanceTests.cs`, `IT/SalesReferenceHttpAcceptanceTests.cs` (gösterim adaptörünün SQL testi yok) | ✅ |
| 5g. Alıcı vergi durumu | Ayarlar (liste) | Satış | Sipariş başına Ayarlar alıcı durumu listesinden seçim (SAT-D140/D158); Cari'den okunmuyor | `SalesDraftService.Tax.cs` | `IT/SalesOrderTaxSqlServerRelationalAcceptanceTests.cs` | ✅ (tasarım gereği Cari bağı yok) |
| 5h. Cari e-belge (e-Fatura/e-Arşiv mükellefiyeti, senaryo) | Cari | Satış (fatura) | Tanımlı Satış portu yok; `ISettingsEInvoiceScenarioPolicyProviderV1` yalnız Cari'ye bağlı (`Program.cs:201`) | — | — | ❌ (fatura dalgasıyla) |
| 5i. İş onay kutusu (sipariş taslağı onayları) | Satış | İş (Work) | `ISalesOrderDraftPendingApprovalReadServiceV1` → `INativeApprovalReadAdapterV1` (`sales.order-draft.v1`) | `SME:75`; host `SalesOrderDraftPendingApprovalAdapterV1` (`API/WorkRuntime/`), `Program.cs:186` | `IT/WorkApprovalEndpointTests.cs`, `IT/NativeApprovalReadQueryV1Tests.cs`, SQL: `IT/SalesOrderDraftPendingApprovalReadSqlServerRelationalAcceptanceTests.cs` | ✅ |
| 5j. İş onay kutusu (teklif süre aşımı onayı vb.) | Satış | İş | Teklif onayları için adaptör yok; yalnız sipariş taslağı `PendingApproval` listeleniyor | — | — | 🟡 |
| 5k. Satış sahibi gösterimi ve seçici (SAT-D27, MOD-08) | Kimlik | Satış | `ISalesUserDisplaySourceV1`, `ISalesOwnerCandidateSourceV1` ← `IUserDirectoryReadPortV1` | `SalesUserDisplayHost`, `SalesOwnerCandidateHost` (`API/Endpoints/SalesUserDisplayHost.cs`), `Program.cs:323-325`; `IdentityModuleExtensions.cs:74` | `SUT/SalesReferenceReadHandlerTests.SatD27.cs`, `IT/SalesReferencePoolPermissionHttpTests.cs`, SQL: `IT/IdentityUserDirectorySqlServerRelationalAcceptanceTests.cs` | ✅ |
| 5l. "Bağlı satın alma" iptal etkisi (Alış MOD-03) | Alış | Satış | `LinkedPurchasing = "purchasing.linked-orders"` | Port yok → `NoSource`. Alış'taki `IPurchaseSupplyReferencePortV1` yalnız `ISalesOrderLineReferencePortV1`'in aynası; Satış'a bağ kurmuyor | — | ❌ |
| 5m. "Tedarikçide hazır" / alış gelecek arzı (Alış MOD-16, ALS-D22/D30) | Alış | Satış (kaynak planı, sipariş ekranı) | `IPurchaseSupplyReferencePortV1` v2 (öneri; `docs/delivery/ALIS_HAZIRLIK_PLANI_v1.md:351`) | Yok. Bugünkü `IPurchaseSupplyReferencePortV1` v1 yalnız onaylı alış satırı referansı; gelecek arz/tedarikçide hazır alanı yok. Görev notu `ALIS_SATIS_DEVIR_v1.md` main'de yok | — | ❌ |

### 6. Diğer Satış bağları

| Bağlantı | Üretici | Tüketici | Port/olay | main'de impl + DI | Test | Durum |
|---|---|---|---|---|---|---|
| 6a. Belge eki sahip bağı | Satış | Belgeler | `IDocumentOwnerLinkPortV1` | `SalesDocumentOwnerLinkPort` (`API/DocumentsRuntime/SalesDocumentOwnerLinkPort.cs`), `DocumentsRuntimeExtensions.cs:24` | `IT/SalesDocumentOwnerLinkPortTests.cs` (SQL yok) | ✅ |
| 6b. Sipariş PDF'i (MOD-07) | Satış | Belge üretimi | `ISalesOrderPrintReadServiceV1` + `SalesOrderPdfComposer` | `SME:74`; `API/SalesRuntime/SalesOrderPdfComposer.cs` | `IT/SalesOrderPdfHttpAcceptanceTests.cs`, SQL: `IT/SalesOrderPdfSqlServerRelationalAcceptanceTests.cs` | ✅ |
| 6c. Genel arama | Satış | Arama (host) | `SalesOrderGlobalSearchSource` | `Search/SalesGlobalSearchRegistration.cs`, `API/Search/GlobalSearchEndpoints.cs:34` | `SUT/SalesOrderGlobalSearchSourceTests.cs` | ✅ |
| 6d. Kabuk yetenekleri (menü) | Satış | Kabuk | `SalesShellCapabilityProvider` | `Program.cs:381` | Kabuk/C01 testleri | ✅ |
| 6e. Sipariş taslağı okuma servisi | Satış | Host uçları (PDF, okuma) | `ISalesDraftReadServiceV1` | `Program.cs:334` | `IT/SalesDraftHttpAcceptanceTests.cs`, `IT/SalesOrderPdfHttpAcceptanceTests.cs` | ✅ |
| 6f. Satış olayları (outbox) | Satış | Finans, Üretim, Lojistik | Öneri: `sales.order.approved.v1`, `sales.order.cancelled.v1`, `sales.invoice.finalized.v1` | Satış'ta outbox yok | — | ❌ (ERP-000F dağıtıcısı yok; bugün senkron port yeterli) |

## Bağımsız analizle karşılaştırma (koordinatör oturumu, main `1db70046`)

| Bağımsız bulgu | Bu not | Fark / kanıt |
|---|---|---|
| ❌ Satış faturası → Finans alacak; `sales.invoice.finalized.v1` yok, Finans kabul portu (API-10) ile birlikte planlanacak | 1a ❌ | Uyumlu. Kanıt: `SalesDbContext.cs` outbox yok; `IFinanceInvoiceOpenItemAdmissionPortV1` kodda yok; plan `docs/delivery/FINANS_HAZIRLIK_PLANI_v1.md` §4 |
| ❌ `IManufacturingSalesOrderLinkReadPortV1` / `ISalesOrderLinkedSupplyReadPortV1` yok; `ISalesSubcontractOrderBindingReadPortV1` uygulamasız (fail-closed) | 2a ❌, 2e 🟡 | Uyumlu. Bağ portunu 🟡 işaretledim: arayüz ve Satış tüketicisi (`SME:175`, `GetService` → 503) var, eksik olan yalnız Üretim+Stok host adaptörü |
| ❌ Alış → Satış "bağlı tedarik / tedarikçide hazır" (MOD-03/MOD-16) yok; Alış sağlar, Satış tüketir | 5l ❌, 5m ❌ | Uyumlu; bu karşılaştırmayla 5m eklendi. Ek ayrıntı: Alış planına göre `ISalesOrderLinkedSupplyReadPortV1` sözleşmesinin sahibi Satış'tır, Alış uyar (`ALIS_HAZIRLIK_PLANI_v1.md:339`) — aynı Satış portu S-1 (Üretim) ve MOD-03 (Alış) için ortak kullanılır |
| ✅ Lojistik, stok tahsisi, kur, Cari, Ürün, İş bağları sağlam | 3a–3e, 3g, 5a–5f, 5i, 5k ✅ | **İki küçük fark:** (1) **Kur (4a)** burada 🟡: bağ çalışıyor ve Ayarlar tarafında SQL testi var (`IT/SettingsExchangeRateSqlServerRelationalAcceptanceTests.cs`), ama Satış risk çevrimi yalnız sahte çözümleyiciyle test ediliyor (`SUT/SalesDraftServiceTests.RiskCheck.cs`); `IT/SalesRiskCheckSqlServerRelationalAcceptanceTests.cs` kuru kullanmıyor. (2) **İş (5j)** burada 🟡: Work kutusuna yalnız sipariş taslağı adaptörü bağlı (`API/Program.cs:186` `SalesOrderDraftPendingApprovalAdapterV1`); teklif süre aşımı onayı için adaptör yok. Ayrıca 3f (faturalanmamış irsaliye) fatura sahibi olmadığı için 🟡 |

## Eksikler ve sahipleri

| # | Bağlantı | Durum | Eksik taraf | Sahibi |
|---|---|---|---|---|
| 1a | Satış faturası → alacak açık kalemi | ❌ | Satış fatura çekirdeği + `sales.invoice.finalized.v1` içeriği | **Satış/Claude şeridi** (ALS-D44: fatura çekirdeği Satış'ta) |
| 1a | — | ❌ | `IFinanceInvoiceOpenItemAdmissionPortV1` kabul portu, `Receivable` yönü (MOD-15/MOD-04, Finans API-10) | **Finans** |
| 1c | Kredi limiti / teminat | 🟡 | Limit ve teminat master'ı (API-85; OPEN-010 açık) | **Finans** |
| 1d | Finans olayları → Satış | ❌ | Finans outbox + Satış inbox (dağıtıcı ERP-000F) | **Finans** (üretici), Satış (tüketici; dağıtıcı sonrası) |
| 1e | Eşleşmemiş avans (iptal etkisi) | ❌ | Siparişe bağlı avans kaydı ve okuma portu (Finans API-21) | **Finans**; Satış tarafı `NoSource` dalını porta bağlar |
| 2a | S-1 bağlı üretim (iptal/zorla kapatma) | ❌ | Satış `ISalesOrderLinkedSupplyReadPortV1` + `SalesDraftService.Cancellation.cs` / `.ForceClosure.cs` içindeki `LinkedProduction` dalının bu porttan beslenmesi | **Satış/Claude şeridi** |
| 2a | — | ❌ | `IManufacturingSalesOrderLinkReadPortV1` (Üretim **U5-a**); Satış portuna bağlayan host adaptörü Satış'ın onayıyla | **Üretim** (port), host adaptörü Üretim + **Satış onayı** |
| 2b | S-2 kaynak planı ekranı/kaydı (API-11) | ❌ | Satır kaynak stratejisi kaydı + ekran; "üretim" seçeneği Satış **S6** dalgasında | **Satış/Claude şeridi** |
| 2c | Üretim planı ↔ sipariş satırı | 🟡 | `ISalesOrderLineReferencePortV1.GetApprovedLineAsync` tüketicisi (Üretim API-16, **U2-b**); Satış tarafı hazır | **Üretim** |
| 2d | Fason hizmet siparişi okuma | 🟡 | Üretim tüketici adaptörü (URT-D22, **U5-b**); Satış tarafı hazır | **Üretim** |
| 2e | Fason bağ portu | 🟡 | `ISalesSubcontractOrderBindingReadPortV1` host adaptörü + DI kaydı + SQL testi (**U5-b**) | **Üretim + Lojistik/Stok** (host adaptörü; müşteri malı anahtarı Stok'ta) |
| 2f | Üretim → Satış durum dönüşü | ❌ | Port/olay tasarımı | **Üretim** (üretici), Satış (gösterim) |
| 3f | Faturalanmamış irsaliye | 🟡 | Fatura tüketimi sahibi | **Satış/Claude şeridi** (fatura dalgası) |
| 3h | Miktar bazlı rezervasyon | ❌ | Kapsam kararı + port | **Lojistik/Stok** (karar gerekirse `NEEDS-SINAN`) |
| 4a | Kur çözümleyici — Satış ucunda gerçek-SQL | 🟡 | Satış risk + kur birlikte `[SqlServerFact]` testi | **Satış/Claude şeridi** |
| 4d | KDV/tevkifat listesi okuyucusu | 🟡 | Okuyucunun gerçek-SQL testi; bakım ekranı (SAT-D146) | **Ayarlar** (bu repoda Satış şeridi MOD-03 ile birlikte yürütüyor) |
| 4g | Teklif politikası | 🟡 | Kalıcı kayıt + bakım ekranı; veri girişi | **Ayarlar**; veri **Sinan** |
| 5h | Cari e-belge durumu | ❌ | Cari'den e-belge mükellefiyet okuma portu | **Cari** (üretici), Satış (fatura tüketicisi) |
| 5j | Teklif onaylarının İş kutusu | 🟡 | Teklif onayları için `INativeApprovalReadAdapterV1` | **Satış/Claude şeridi** |
| 5l | Bağlı satın alma (iptal etkisi, Alış MOD-03) | ❌ | Veri sağlayan Alış portu (Satış'ın `ISalesOrderLinkedSupplyReadPortV1` sözleşmesine uyar) | **Alış** (üretici); sözleşme sahibi ve tüketici **Satış/Claude** (`docs/delivery/ALIS_HAZIRLIK_PLANI_v1.md:339`) |
| 5m | "Tedarikçide hazır" dahil alış gelecek arzı (Alış MOD-16) | ❌ | `IPurchaseSupplyReferencePortV1` v2 (MOD-14 ile aynı port) | **Alış** (üretici); kaynak planı/sipariş ekranı tüketicisi **Satış/Claude** (`ALIS_HAZIRLIK_PLANI_v1.md:351`) |
| 6f | Satış outbox olayları | ❌ | Satış outbox tablosu + olay sözleşmeleri | **Satış/Claude şeridi** (dağıtıcı ERP-000F geldikten sonra) |

### Üretim şeridinin teyidi (2026-11-02, koordinatör üzerinden)

| Konu | Durum | Sorumlu ve dalga |
|---|---|---|
| S-1 bağlı üretim | ❌ | Satış: `ISalesOrderLinkedSupplyReadPortV1` + `LinkedProduction` dalı (**Satış/Claude**). Üretim: `IManufacturingSalesOrderLinkReadPortV1` (**Üretim U5-a**). Host adaptörü Satış'ın onayıyla bağlanır |
| `ISalesSubcontractOrderBindingReadPortV1` | 🟡 | Host adaptörü **Üretim + Stok, U5-b**; o zamana kadar fason siparişin carisi değişmez (503) |
| S-2 kaynak planı "üretim" seçeneği | ❌ | **Satış/Claude, S6 dalgası** |
| S-2 plan satırı ↔ sipariş satırı | 🟡 | **Üretim U2-b**, `ISalesOrderLineReferencePortV1.GetApprovedLineAsync` ile (Satış portu ✅) |
| `ISalesSubcontractServiceOrderReadPortV1` tüketimi | 🟡 | **Üretim U5-b** (Satış portu ✅) |

Ürün (5a–5c) ve Kimlik (5k) bağlarında eksik yok; Ürün bağları runtime yapılandırmasına bağlıdır (kapalıyken fail-closed 503).

## Kalıcı kural

Bundan sonra açılan **her PR açıklamasına bir "Modül bağlantıları" satırı** eklenir: PR'ın eklediği, değiştirdiği veya
tükettiği port/olay adları ve sürümleri (ör. `ISalesPartyRiskReadPortV1`, `sales.subcontract-service-order.v1`), karşı
modül ve durum (✅/🟡/❌). Bağ yoksa satır "Modül bağlantıları: yok" yazar. Bu tablo o satırlardan güncellenir.
