# Satış modül bağlantıları analizi v1.1

Tarih: 2026-11-02 · Yazan: Satış şeridi (Claude) · Taban: `origin/main` @ `1db70046` (#896 sonrası; #898 fason, #906 kur
kaydı dahil) · v1.1: #919 review kalanları ve Sinan'ın 2 Kasım modül sırası kararı, `origin/main` @ `6949e64c`

Bu not bir **durum fotoğrafıdır**; iş kararı değildir (para/stok/üretim/yetki politikası seçmez). Sondaki "Kalıcı kural" bir **süreç kuralıdır** ve kaynağı orada belirtilmiştir. Karar dosyaları değiştirilmedi. Kaynak önceliği `AGENTS.md`'deki gibidir:
modüler monolit, hiçbir modül başkasının tablosuna yazmaz; modüller arası bağ yalnız sürümlü port (`*PortV1`, `*ReaderV1`,
`*ResolverV1`) veya outbox olayıyla kurulur.

## v1.1 — Sinan kararları: modül sırası ve satış faturasının alacak kaydı (2 Kasım 2026)

Kaynak: `docs/decisions/SINAN_KARARLARI_2026-11-02_SATIS_EK35.md` — **SAT-D209** (modül sırası) ve **SAT-D208** (1a). Kararlar önce koordinatör oturumu üzerinden
iletilmiş, Sinan'ın kendi teyidiyle EK35'e yazılmıştır. Bu not karar dosyasını değiştirmez; yalnız uygular.

- **Ürün, Lojistik/Stok, Ayarlar ve Cari** modüllerinin **Claude geliştirmesi henüz başlamadı** (SAT-D209). Önce süren dört modül — **Satış, Alış,
  Finans, Üretim** — bitirilir; diğerleri sonra başlar ve **bağları o zaman yeniden incelenir**.
- O zamana kadar Ürün, Lojistik/Stok, Ayarlar ve Cari modüllerinde **yeni port, tablo veya ekran açılmaz** (kaynak: `docs/decisions/SINAN_KARARLARI_2026-11-02_SATIS_EK35.md`
  SAT-D209). Mevcut portları tüketilir; mevcut olmayan bağlar **fail-closed** kalır (bugünkü `NoSource` / `Unavailable` / 503
  davranışı). Bu kural **Satış şeridini bağlar**; ALS-D45 ve FNS-D1'in Alış ve Finans şeritlerine o modüllerde küçük ekler için
  verdiği izinler değişmez. Çelişki olup olmadığı Issue #66'da `NEEDS-SINAN` olarak soruldu:
  https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6590215022
- Bu nedenle sahibi bu modüller olan eksikler aşağıda **"ertelendi: <modül> başlayınca"** diye işaretlidir. Satış tarafında
  yalnız mevcut portu tüketen veya test ekleyen işler (yeni port/tablo/ekran açmadan) ertelenmez.
- **1a kararı (`docs/decisions/SINAN_KARARLARI_2026-11-02_SATIS_EK35.md` SAT-D208):** Satış faturası → Finans alacak açık kalemi **tek kayıttır**; Cari kartına ayrıca hareket
  yazılmaz. 120 Alıcılar fişi **aynı kaynak olaydan** Muhasebe'de **taslak fiş** olarak üretilir; kullanıcı onaylayınca
  kesinleşir, resmî defter dış sistemde kalır (DEC-FIN-001/DEC-FIN-004). 120 eşlemesi, mali müşavir teyit edene kadar FNS-D7'deki
  anlam eşlemesidir. Sözleşme bir
  **outbox olayıdır:** `sales.invoice.finalized.v1`; tüketicileri Finans (API-10, alacak açık kalemi) ve Muhasebe (fiş).
  Sahipler: **Satış** — olay sözleşmesi + Satış outbox'ı; **Finans** — API-10 tüketicisi (inbox/dedup); **Muhasebe** — taslak
  fiş tüketicisi (inbox/dedup). İdempotency anahtarı fatura kimliği + sürümü; alan listesi Finans ile fatura dalgasında
  netleşir. Finans şeridine (FNS-D1) devir notu koordinatör oturumu üzerinden `send_message` ile iletildi.

## Okuma yöntemi

- `src/backend` üzerinde `PortV1`, `ReaderV1`, `ResolverV1`, `Outbox`, `IntegrationEvent`, `AddScoped`, `TryAdd` taraması;
  Satış'ın tüketici fabrikası `SalesModuleExtensions.cs` (`CreateDraftService`, `CreateDispatchReadService`) ve host kayıtları
  `GoymenERP.Api/Program.cs` satır satır okundu.
- Test kanıtı `tests/` altında arandı: birim/sözleşme testi ve `[SqlServerFact]` gerçek-SQL kabul testi ayrı sayıldı.
- **Olay (outbox) durumu:** Satış'ın outbox tablosu yok (`SalesDbContext.cs` içinde `Outbox` geçmez); Satış hiçbir
  `sales.*.v1` olayı yayımlamaz ve hiçbir olay tüketmez. Bugünkü bütün bağlar **senkron in-process port**tur. Outbox'ı olan
  modüller yalnız Stok (`InventoryDbContext.OutboxMessages`) ve İş (`WorkDbContext.OutboxMessages`).

Durum işaretleri (tek kural, bütün satırlara aynı uygulanır):

- ✅ gerçek uygulama + main'de DI kaydı + **gerçek uygulamayı çalıştıran** test; bağ veritabanı okuyorsa en az bir ucu
  `[SqlServerFact]` gerçek-SQL testiyle kanıtlı (veritabanı okumayan bağda — statik onaylı katalog, kabuk — gerçek uygulamayı
  çalıştıran test yeter; bu satırlar ayrıca belirtilir).
- 🟡 kısmi: yalnız arayüz; fail-closed/`Unavailable` yer tutucu; tüketici yok; ya da test yalnız sahte/stub port üzerinden
  ve gerçek-SQL kanıtı yok.
- ❌ yok (port, uygulama veya sözleşme main'de yok).

Kısaltmalar: `SME` = `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/SalesModuleExtensions.cs`,
`SC/` = `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Application/Contracts/`, `API/` = `src/backend/GoymenERP.Api/`,
`IT/` = `tests/GoymenERP.Api.IntegrationTests/`, `SUT/` = `tests/GoymenERP.Sales.UnitTests/`.

## Bağlantı tablosu

### 1. Finans

| Bağlantı | Üretici | Tüketici | Port/olay | main'de impl + DI | Test | Durum |
|---|---|---|---|---|---|---|
| 1a. Satış faturası → Finans alacak açık kalemi + Muhasebe fişi (120) | Satış (fatura çekirdeği) | Finans (API-10), Muhasebe (fiş) | Karar (EK35 SAT-D208): outbox olayı `sales.invoice.finalized.v1` (tek kayıt; Cari kartına hareket yazılmaz; 120 fişi taslak, onayla kesinleşir). Finans'ın senkron `IFinanceInvoiceOpenItemAdmissionPortV1` önerisi (`docs/delivery/FINANS_HAZIRLIK_PLANI_v1.md` §4) bu kararla olay tüketimine döner | **Yok.** Satış'ta fatura varlığı ve outbox yok; Finans açık kalemleri yalnız CSV içe aktarılan `official.invoice-installment.v1` kaynağından doğuyor (`Modules/Finance/.../FinanceOpenItemSourcePolicy.cs`); Muhasebe'de tüketici yok | — | ❌ (yaklaşım kararlı, uygulama yok) |
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
| 3c. Onaylı sipariş satırı referansı (yükleme talimatı, sevk gerçekleşmesi, fiziksel tahsis) | Satış | Lojistik, Stok (host) | `ISalesOrderLineReferencePortV1` | `SalesOrderLineReferencePort`, `SME:80`; tüketiciler `LogisticsModuleExtensions.cs:106` (yükleme talimatı ve `LogisticsOperationsWorkService`, AT-025: `Modules/Logistics/GoymenERP.Modules.Logistics.Application/LogisticsOperationsWorkService.cs:30`), `API/Endpoints/InventoryPhysicalAllocationHostPorts.cs:51` | `tests/GoymenERP.Logistics.UnitTests/LogisticsInstructionServiceTests.cs`, `tests/GoymenERP.Logistics.UnitTests/LogisticsOperationsWorkServiceTests.cs`, `IT/InventoryPhysicalAllocationHostServiceTests.cs`, SQL: `IT/LogisticsInstructionSqlServerRelationalAcceptanceTests.cs`, `IT/InventoryAllocationSqlServerRelationalAcceptanceTests.cs` (+ 9 SQL dosyası daha) | ✅ |
| 3d. İrsaliye listesi ve fatura adayı sevkiyatlar (API-30 / API-31) | Lojistik | Satış | `ISalesDispatchNoteReadPortV1`, `ISalesInvoiceableShipmentReadPortV1` (`SC/ISalesDispatchReadPortsV1.cs`) | `LogisticsSalesDispatchReadPorts`, `LogisticsModuleExtensions.cs:77-79`; Satış tarafı `SalesOrderDispatchReferenceReader` (`SME:87`) | `tests/GoymenERP.Logistics.UnitTests/LogisticsSalesDispatchReadPortsTests.cs`, `SUT/SalesDraftServiceTests.Dispatches.cs`, SQL: `IT/SalesDispatchSqlServerRelationalAcceptanceTests.cs` | ✅ |
| 3e. Onaylı müşteri iadeleri (SAT-D8 / API-10) | Lojistik | Satış | `ISalesOrderCustomerReturnReadPortV1` | `LogisticsSalesOrderCustomerReturnReadPort` (`.../LogisticsCustomerReturnReadPorts.cs:72`), `LogisticsModuleExtensions.cs:73` | SQL: `IT/SalesCustomerReturnsSqlServerRelationalAcceptanceTests.cs` (ayrı Lojistik birim testi yok) | ✅ |
| 3f. "Faturalanmamış irsaliye" iptal etkisi | Lojistik / Satış fatura | Satış | `UninvoicedDispatchNotes = "invoicing.uninvoiced-dispatch-notes"` | Lojistik'in aktif onaylı irsaliye sayısından okunuyor; fatura tüketen sahip olmadığından onaylı her aday "faturalanmamış" sayılıyor | 3b'deki testler | 🟡 (fatura çekirdeği gelince kesinleşir) |
| 3g. Aktif fiziksel tahsis (iptal etkisi) | Stok | Satış | `ISalesOrderAllocationReadPortV1` (`SC/SalesDraftCancellationContractsV1.cs:19`) | `InventorySalesOrderAllocationReadPort`, `Modules/Inventory/.../InventoryModuleExtensions.cs:59` | `tests/GoymenERP.Inventory.UnitTests/InventorySalesOrderAllocationReadPortTests.cs`, SQL: `IT/InventorySalesOrderCancellationSqlServerRelationalAcceptanceTests.cs`, `IT/InventoryAllocationChangeSqlServerRelationalAcceptanceTests.cs` | ✅ |
| 3h. Miktar bazlı stok rezervasyonu | Stok | Satış | Tanımlı port yok | main'de yalnız etiket bazlı fiziksel tahsis var; ayrı rezervasyon kavramı yok | — | ❌ — ertelendi: Lojistik/Stok başlayınca |

### 4. Ayarlar

| Bağlantı | Üretici | Tüketici | Port/olay | main'de impl + DI | Test | Durum |
|---|---|---|---|---|---|---|
| 4a. Döviz kuru ve kur günü (#906, SAT-D47/D54/D55) | Ayarlar | Satış (risk çevrimi) | `ISettingsExchangeRateResolverV1` | `SettingsExchangeRateResolverV1`, `Modules/Settings/.../SettingsModuleExtensions.cs:55`; TCMB kaynakları yapılandırmayla kapalı (`DisabledExchangeRateBulletinSource`) | `SUT/SalesDraftServiceTests.RiskCheck.cs` (sahte), SQL: `IT/SettingsExchangeRateSqlServerRelationalAcceptanceTests.cs`. Satış+kur birlikte SQL testi yok | 🟡 (bağ ✅; Satış ucunda gerçek-SQL kanıtı eksik) |
| 4b. Satış iş günü (SAT-D10) | Satış (kendi) | — | `SalesBusinessDay` (Europe/Istanbul) — Ayarlar portu değil | `Modules/Sales/GoymenERP.Modules.Sales.Domain/SalesBusinessDay.cs` | Satış birim testleri | ✅ (modüller arası bağ değil) |
| 4c. Satır vergisi (MOD-03, SAT-D93/D139/D140) | Ayarlar | Satış | `ISettingsLineTaxResolverV1` | `SettingsTaxMasterReader`, `SettingsModuleExtensions.cs:47` | SQL: `IT/SettingsTaxMasterSqlServerRelationalAcceptanceTests.cs`, `IT/SalesOrderTaxSqlServerRelationalAcceptanceTests.cs` | ✅ |
| 4d. KDV oranı / tevkifat listeleri (SAT-D181) | Ayarlar | Satış | `ISettingsTaxRateListReaderV1` | `SettingsTaxRateListReader`, `SettingsModuleExtensions.cs:50`; liste boş olduğu sürece satırda KDV/tevkifat seçimi kapalı kalır (fail-closed) | `SUT/SalesDraftServiceTests.Tax.cs` (sahte), `tests/GoymenERP.Settings.UnitTests/TaxRateListCatalogTests.cs` (katalog); okuyucunun kendi SQL testi yok | 🟡 (test eksiği; bakım ekranı ertelendi: Ayarlar başlayınca) |
| 4e. Satış politika kaydı (tolerans, iskonto limiti; SAT-D2/D5/D61…D64) | Ayarlar | Satış | `ISettingsSalesPolicyProviderV1` | `API/Program.cs:198` onaylı katalog; `SettingsModuleExtensions.cs:36` `Replace` → `SettingsSalesPolicyProviderV1` | `tests/GoymenERP.Settings.UnitTests/ApprovedSalesPolicyProviderTests.cs`, SQL: `IT/SettingsSalesDiscountLimitSqlServerRelationalAcceptanceTests.cs` | ✅ |
| 4f. İskonto limit grubu üyeliği (SAT-D63/D66) | Ayarlar (+ Kimlik) | Satış | `ISalesDiscountLimitGroupSourceV1` ← `ISettingsSalesDiscountLimitGroupMembershipReaderV1` | Host `SalesDiscountLimitGroupHost` (`API/Endpoints/SettingsSalesDiscountLimitHosts.cs`), `Program.cs:329` | `IT/SettingsSalesDiscountLimitPortTests.cs`, SQL: `IT/SettingsSalesDiscountLimitSqlServerRelationalAcceptanceTests.cs` | ✅ |
| 4g. Teklif politikası (kayıp nedeni, varsayılan geçerlilik; SAT-D25/D39) | Ayarlar | Satış | `ISettingsSalesQuotePolicyProviderV1` | `ApprovedSettingsSalesQuotePolicyProvider`, `Program.cs:204`; veri statik kod kataloğundan (`ApprovedSalesQuotePolicyCatalog`), katalog **boş** → kayıp 503, geçerlilik önerisi yok | Satış teklif testleri, SQL: `IT/SalesQuoteSqlServerRelationalAcceptanceTests.cs` | 🟡 — veri: `NEEDS-SINAN`; kalıcı kayıt/ekran ertelendi: Ayarlar başlayınca |
| 4h. Para profilleri / para birimi adayları (SAT-D18) | Ayarlar | Satış | `ISettingsPrerequisiteProvider`, `ISettingsMoneyCandidateReader`, `ISettingsMoneyCandidateReaderV2` | `ApprovedSettingsPrerequisiteProvider`, `Program.cs:191-195` (onaylı statik katalog) | `tests/GoymenERP.Settings.UnitTests/ApprovedMoneyProfileProviderTests.cs`, `SUT/SalesReferenceReadHandlerTests.cs`, `IT/SettingsHostWiringTests.cs` (statik onaylı katalog; veritabanı okumaz) | ✅ |

### 5. Ürün, Cari, İş onayları, Kimlik, Alış

| Bağlantı | Üretici | Tüketici | Port/olay | main'de impl + DI | Test | Durum |
|---|---|---|---|---|---|---|
| 5a. Ürün satış uygunluğu / aday listesi ¹ | Ürün | Satış | `IProductSalesEligibilityV1`, `IProductSalesCandidatesV1` | `API/ProductRuntime/ProductRuntimeExtensions.cs:101`; runtime kapalıyken `UnconfiguredProductEligibility` (503) | `tests/GoymenERP.Product.UnitTests/ProductEligibilityHostCompositionTests.cs`, `IT/SalesProductCandidateHttpAcceptanceTests.cs`, SQL: `IT/ProductSalesEligibilityAuthoritySqlServerRelationalAcceptanceTests.cs` | ✅ |
| 5b. Fiyat listesi kademe zinciri (SAT-D9, MOD-02) ¹ | Ürün | Satış | `IProductQuantityPriceReferenceReaderV2` | `Modules/Product/.../PriceLists/ProductPriceListRegistration.cs:50` — yalnız Ürün runtime açıkken; kapalıyken kayıt yok → Satış 503 | `SUT/SalesDraftServiceTests.LinePricing.cs`, `IT/ProductPriceListHostCompositionTests.cs`, SQL: `IT/ProductPriceListSqlServerRelationalAcceptanceTests.cs` | ✅ |
| 5c. Kalem tipi okuması (SAT-D176, fason hizmet satırı) ¹ | Ürün | Satış (+ Ayarlar vergi) | `IProductItemTypeReaderV1` | `ProductPriceListRegistration.cs:55`; runtime kapalıyken `UnavailableProductItemTypeService` (`API/ProductRuntime/ProductPriceListHostComposition.cs:40-45`, fail-closed) | `SUT/SalesDraftServiceTests.Subcontract.cs`, `IT/SettingsHostWiringTests.cs`; SQL: `IT/SettingsTaxMasterSqlServerRelationalAcceptanceTests.cs:850` gerçek `ProductItemTypeService`'i SQL üzerinde `SettingsProductItemTypeHost` ile çalıştırıyor; fason dolaylı: `IT/SalesSubcontractServiceOrderSqlServerRelationalAcceptanceTests.cs` | ✅ |
| 5d. Müşteri hesabı doğrulama | Cari | Satış | `ISalesPartyAccountPortV1` | Host `CounterpartySalesPartyAccountPortV1` (`API/Endpoints/SalesEndpoints.cs:2052`), `Program.cs:219` (zorunlu) | `IT/SalesReferenceHttpAcceptanceTests.cs`; SQL: `IT/SalesDraftApiSqlServerRelationalAcceptanceTests.cs:84-170` gerçek portu SQL üzerinde çalıştırıyor, ama yalnız **olumsuz yol** (bilinmeyen müşteri → 409 `account_state`, taslak yazılmaz). Olumlu yol (`Found`) testlerde `SyntheticAccounts` ile değiştiriliyor (`:262-263`, `:374-375`) | 🟡 (yalnız olumsuz yol kanıtlı) |
| 5e. Teslim adresi / ilgili kişi (SALES-A2a) | Cari | Satış | `ISalesPartyContactInformationPortV1` | Aynı host adaptörü, `Program.cs:222` | `SUT/SalesReferenceReadHandlerTests.cs` (sahte); `IT/SalesDraftSqlServerRelationalAcceptanceTests.cs` portu çağırmıyor, `IT/SalesDraftApiSqlServerRelationalAcceptanceTests.cs:376-377` `SyntheticContactInformation` ile değiştiriyor. Cari tarafı: `IT/CounterpartyContactInformationSqlServerRelationalAcceptanceTests.cs` (Satış portu üzerinden değil) | 🟡 |
| 5f. Müşteri seçimi ve kayıt gösterimi | Cari | Satış | `ISalesAccountSelectionSourceV1`, `ISalesPartyDisplaySourceV1` | `CounterpartySalesAccountSelectionSourceV1` (`SalesEndpoints.cs:1989`, `Program.cs:207`); `SalesPartyDisplayHost` (`API/Endpoints/SalesPartyDisplayHost.cs`, `Program.cs:320`) | `IT/SalesDraftHttpAcceptanceTests.cs`, `IT/SalesReferenceHttpAcceptanceTests.cs` (stub kaynaklı; iki adaptörün gerçek-SQL testi yok) | 🟡 |
| 5g. Alıcı vergi durumu | Ayarlar (liste) | Satış | Sipariş başına Ayarlar alıcı durumu listesinden seçim (SAT-D140/D158); Cari'den okunmuyor | `SalesDraftService.Tax.cs` | `IT/SalesOrderTaxSqlServerRelationalAcceptanceTests.cs` | ✅ (tasarım gereği Cari bağı yok) |
| 5h. Cari e-belge profili (e-Fatura/e-Arşiv mükellefiyeti, senaryo; #860) | Cari | Satış (fatura) | `ICounterpartyAccountEDocumentProfileSourceV1` | `CounterpartyAccountEDocumentProfileSourceV1` (`Modules/Counterparty/GoymenERP.Modules.Counterparty.Infrastructure/Persistence/CounterpartyAccountEDocumentProfileSourceV1.cs`), `CounterpartyModuleExtensions.cs:66-68`. Satış'ta tüketici yok (fatura dalgasında tüketilecek) | SQL: `IT/CounterpartyEDocumentProfileSqlServerRelationalAcceptanceTests.cs` | 🟡 (Cari ✅, Satış tüketicisi yok) |
| 5i. İş onay kutusu (sipariş taslağı onayları) | Satış | İş (Work) | `ISalesOrderDraftPendingApprovalReadServiceV1` → `INativeApprovalReadAdapterV1` (`sales.order-draft.v1`) | `SME:75`; host `SalesOrderDraftPendingApprovalAdapterV1` (`API/WorkRuntime/`), `Program.cs:186` | `IT/WorkApprovalEndpointTests.cs`, `IT/NativeApprovalReadQueryV1Tests.cs`, SQL: `IT/SalesOrderDraftPendingApprovalReadSqlServerRelationalAcceptanceTests.cs` | ✅ |
| 5j. İş onay kutusu (teklif süre aşımı onayı vb.) | Satış | İş | Teklif onayları için adaptör yok; yalnız sipariş taslağı `PendingApproval` listeleniyor | — | — | 🟡 |
| 5k. Satış sahibi gösterimi ve seçici (SAT-D27, MOD-08) | Kimlik | Satış | `ISalesUserDisplaySourceV1`, `ISalesOwnerCandidateSourceV1` ← `IUserDirectoryReadPortV1` | `SalesUserDisplayHost`, `SalesOwnerCandidateHost` (`API/Endpoints/SalesUserDisplayHost.cs`), `Program.cs:323-325`; `IdentityModuleExtensions.cs:74` | `SUT/SalesReferenceReadHandlerTests.SatD27.cs`, `IT/SalesReferencePoolPermissionHttpTests.cs`, SQL: `IT/IdentityUserDirectorySqlServerRelationalAcceptanceTests.cs` | ✅ |
| 5k2. Aktif rol kodu okuması (onay/limit kararları) | Kimlik | Satış | `IActiveRoleCodeReader` | `ErpAuthorizationService`, `Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/IdentityModuleExtensions.cs:64`; Satış `SME:110` `GetRequiredService` | `tests/GoymenERP.Identity.UnitTests/ActiveRoleCodeReaderTests.cs`, `tests/GoymenERP.Identity.UnitTests/IdentityAuthorizationCompositionTests.cs`, Satış SQL testleri rol okuyucusunu stub'lıyor (ör. `IT/SalesDeliveryScheduleSqlServerRelationalAcceptanceTests.cs:275` `NoActiveRoles`, `IT/SalesDraftApprovalSqlServerRelationalAcceptanceTests.cs:1271` `FixedActiveRoleCodeReader`) — gerçek okuyucuyla Satış SQL testi yok | 🟡 |
| 5l. "Bağlı satın alma" iptal etkisi (Alış MOD-03) | Alış | Satış | `LinkedPurchasing = "purchasing.linked-orders"` | Port yok → `NoSource`. Alış'taki `IPurchaseSupplyReferencePortV1` yalnız `ISalesOrderLineReferencePortV1`'in aynası; Satış'a bağ kurmuyor | — | ❌ |
| 5m. "Tedarikçide hazır" / alış gelecek arzı (Alış MOD-16, ALS-D22/D30) | Alış | Satış (kaynak planı, sipariş ekranı) | `IPurchaseSupplyReferencePortV1` v2 (öneri; `docs/delivery/ALIS_HAZIRLIK_PLANI_v1.md:351`) | Yok. Bugünkü `IPurchaseSupplyReferencePortV1` v1 yalnız onaylı alış satırı referansı; gelecek arz/tedarikçide hazır alanı yok. Görev notu `ALIS_SATIS_DEVIR_v1.md` main'de yok | — | ❌ |

¹ Ürün runtime'ı varsayılan olarak **kapalıdır** (`API/ProductRuntime/ProductRuntimeOptions.cs:8`: `IsEnabled => Issuer is not null`; varsayılan yapılandırmada `Issuer` yok). Kapalıyken 5a–5c yer tutucudur: `UnconfiguredProductEligibility`, fiyat okuyucusu kaydı yok (Satış 503), `UnavailableProductItemTypeService`. Durum işaretleri runtime açık kompozisyonu içindir.

### 6. Diğer Satış bağları

| Bağlantı | Üretici | Tüketici | Port/olay | main'de impl + DI | Test | Durum |
|---|---|---|---|---|---|---|
| 6a. Belge eki sahip bağı | Satış | Belgeler | `IDocumentOwnerLinkPortV1` | `SalesDocumentOwnerLinkPort` (`API/DocumentsRuntime/SalesDocumentOwnerLinkPort.cs`), `DocumentsRuntimeExtensions.cs:24` | `IT/SalesDocumentOwnerLinkPortTests.cs` (stub; gerçek-SQL testi yok) | 🟡 |
| 6b. Sipariş PDF'i (MOD-07) | Satış | Belge üretimi | `ISalesOrderPrintReadServiceV1` + `SalesOrderPdfComposer` | `SME:74`; `API/SalesRuntime/SalesOrderPdfComposer.cs` | `IT/SalesOrderPdfHttpAcceptanceTests.cs`, SQL: `IT/SalesOrderPdfSqlServerRelationalAcceptanceTests.cs` | ✅ |
| 6c. Genel arama | Satış | Arama (host) | `SalesOrderGlobalSearchSource` | `Search/SalesGlobalSearchRegistration.cs`, `API/Search/GlobalSearchEndpoints.cs:34` | `SUT/SalesOrderGlobalSearchSourceTests.cs`, SQL: `IT/GlobalSearchSqlServerRelationalAcceptanceTests.cs` | ✅ |
| 6d. Kabuk yetenekleri (menü) | Satış | Kabuk | `SalesShellCapabilityProvider` | `Program.cs:381` | Kabuk/C01 testleri (veritabanı okumaz) | ✅ |
| 6e. Sipariş taslağı okuma servisi | Satış | Host uçları (PDF, okuma) | `ISalesDraftReadServiceV1` | `Program.cs:334` | `IT/SalesDraftHttpAcceptanceTests.cs`, `IT/SalesOrderPdfHttpAcceptanceTests.cs`, SQL: `IT/SalesOrderPdfSqlServerRelationalAcceptanceTests.cs` | ✅ |
| 6f. Satış olayları (outbox) | Satış | Finans, Muhasebe (fatura); Üretim, Lojistik (öneri) | **Kararlı:** `sales.invoice.finalized.v1` (EK35 SAT-D208). Öneri: `sales.order.approved.v1`, `sales.order.cancelled.v1` | Satış'ta outbox yok | — | ❌ (ERP-000F dağıtıcısına bağlı) |

Bakış açısı farkı: Üretim planı §11a (`docs/delivery/URETIM_HAZIRLIK_PLANI_v1.md`, #918) aynı bağları **tüketici (Üretim) ucundan** işaretler — ör. sipariş satırı bağı orada ❌ (Üretim okumuyor), burada 🟡 (Satış portu hazır); S-1 orada 🟡 (`NoSource` davranışı var), burada ❌ (iki port da yok). Bu not **Satış ucundan** bakar; iki tablo birlikte okunur.

## Bağımsız analizle karşılaştırma (koordinatör oturumu; v1 main `1db70046`, v1.1 taban `6949e64c`)

| Bağımsız bulgu | Bu not | Fark / kanıt |
|---|---|---|
| ❌ Satış faturası → Finans alacak; `sales.invoice.finalized.v1` yok, Finans kabul portu (API-10) ile birlikte planlanacak | 1a ❌ | Uyumlu. Kanıt: `SalesDbContext.cs` outbox yok; `IFinanceInvoiceOpenItemAdmissionPortV1` kodda yok; plan `docs/delivery/FINANS_HAZIRLIK_PLANI_v1.md` §4 |
| ❌ `IManufacturingSalesOrderLinkReadPortV1` / `ISalesOrderLinkedSupplyReadPortV1` yok; `ISalesSubcontractOrderBindingReadPortV1` uygulamasız (fail-closed) | 2a ❌, 2e 🟡 | Uyumlu. Bağ portunu 🟡 işaretledim: arayüz ve Satış tüketicisi (`SME:175`, `GetService` → 503) var, eksik olan yalnız Üretim+Stok host adaptörü |
| ❌ Alış → Satış "bağlı tedarik / tedarikçide hazır" (MOD-03/MOD-16) yok; Alış sağlar, Satış tüketir | 5l ❌, 5m ❌ | Uyumlu; bu karşılaştırmayla 5m eklendi. Ek ayrıntı: Alış planına göre `ISalesOrderLinkedSupplyReadPortV1` sözleşmesinin sahibi Satış'tır, Alış uyar (`ALIS_HAZIRLIK_PLANI_v1.md:339`) — aynı Satış portu S-1 (Üretim) ve MOD-03 (Alış) için ortak kullanılır |
| ✅ Lojistik, stok tahsisi, kur, Cari, Ürün, İş bağları sağlam | 3a–3e, 3g, 5a–5c, 5i, 5k ✅ (5d, 5e, 5f, 5k2 review sonrası 🟡) | **İki küçük fark:** (1) **Kur (4a)** burada 🟡: bağ çalışıyor ve Ayarlar tarafında SQL testi var (`IT/SettingsExchangeRateSqlServerRelationalAcceptanceTests.cs`), ama Satış risk çevrimi yalnız sahte çözümleyiciyle test ediliyor (`SUT/SalesDraftServiceTests.RiskCheck.cs`); `IT/SalesRiskCheckSqlServerRelationalAcceptanceTests.cs` kuru kullanmıyor. (2) **İş (5j)** burada 🟡: Work kutusuna yalnız sipariş taslağı adaptörü bağlı (`API/Program.cs:186` `SalesOrderDraftPendingApprovalAdapterV1`); teklif süre aşımı onayı için adaptör yok. Ayrıca 3f (faturalanmamış irsaliye) fatura sahibi olmadığı için 🟡 |

## Eksikler ve sahipleri

| # | Bağlantı | Durum | Eksik taraf | Sahibi |
|---|---|---|---|---|
| 1a | Satış faturası → alacak açık kalemi + 120 fişi | ❌ | Satış fatura çekirdeği + `sales.invoice.finalized.v1` olay sözleşmesi + Satış outbox'ı | **Satış/Claude şeridi** (ALS-D44: fatura çekirdeği Satış'ta) |
| 1a | — | ❌ | API-10 olay tüketicisi (inbox/dedup), taksit başına `Receivable` açık kalem (MOD-15/MOD-04) | **Finans** |
| 1a | — | ❌ | Aynı olaydan 120 Alıcılar **taslak** fişi üreten tüketici (inbox/dedup; onayla kesinleşir, resmî defter dış sistemde; 120 eşlemesi müşavir teyidine kadar FNS-D7 anlam eşlemesi) | **Muhasebe** |
| 1c | Kredi limiti / teminat | 🟡 | Limit ve teminat master'ı (API-85; OPEN-010 açık) | **Finans** |
| 1d | Finans olayları → Satış | ❌ | Finans outbox + Satış inbox (dağıtıcı ERP-000F) | **Finans** (üretici), Satış (tüketici; dağıtıcı sonrası) |
| 1e | Eşleşmemiş avans (iptal etkisi) | ❌ | Siparişe bağlı avans kaydı ve okuma portu (Finans API-21) | **Finans**; Satış tarafı `NoSource` dalını porta bağlar |
| 2a | S-1 bağlı üretim (iptal/zorla kapatma) | ❌ | Satış `ISalesOrderLinkedSupplyReadPortV1` + `SalesDraftService.Cancellation.cs` / `.ForceClosure.cs` içindeki `LinkedProduction` dalının bu porttan beslenmesi | **Satış/Claude şeridi** |
| 2a | — | ❌ | `IManufacturingSalesOrderLinkReadPortV1` (Üretim **U5-a**); Satış portuna bağlayan host adaptörü Satış'ın onayıyla | **Üretim** (port), host adaptörü Üretim + **Satış onayı** |
| 2b | S-2 kaynak planı ekranı/kaydı (API-11) | ❌ | Satır kaynak stratejisi kaydı + ekran; "üretim" seçeneği Satış **S6** dalgasında | **Satış/Claude şeridi** |
| 2c | Üretim planı ↔ sipariş satırı | 🟡 | `ISalesOrderLineReferencePortV1.GetApprovedLineAsync` tüketicisi (Üretim API-16, **U2-b**); Satış tarafı hazır | **Üretim** |
| 2d | Fason hizmet siparişi okuma | 🟡 | Üretim tüketici adaptörü (URT-D22, **U5-b**); Satış tarafı hazır | **Üretim** |
| 2e | Fason bağ portu | 🟡 | `ISalesSubcontractOrderBindingReadPortV1` host adaptörü + DI kaydı + SQL testi (**U5-b**) | **Üretim + Lojistik/Stok** (host adaptörü; müşteri malı anahtarı Stok'ta). Stok'ta yeni anahtar/tablo gerekiyorsa o kısım **ertelendi: Lojistik/Stok başlayınca**; o zamana kadar 503 fail-closed |
| 2f | Üretim → Satış durum dönüşü | ❌ | Port/olay tasarımı | **Üretim** (üretici), Satış (gösterim) |
| 3f | Faturalanmamış irsaliye | 🟡 | Fatura tüketimi sahibi | **Satış/Claude şeridi** (fatura dalgası) |
| 3h | Miktar bazlı rezervasyon | ❌ | Kapsam kararı + port | **Lojistik/Stok** — **ertelendi: Lojistik/Stok başlayınca** |
| 4a | Kur çözümleyici — Satış ucunda gerçek-SQL | 🟡 | Satış risk + kur birlikte `[SqlServerFact]` testi | **Satış/Claude şeridi** |
| 4d | KDV/tevkifat listesi okuyucusu | 🟡 | (a) Okuyucunun gerçek-SQL testi — yalnız test, yeni port/tablo/ekran açmaz, **ertelenmez**; (b) bakım ekranı (SAT-D146). Liste boş kaldıkça KDV/tevkifat seçimi kapalıdır | (a) **Satış/Claude şeridi**; (b) **Ayarlar** — **ertelendi: Ayarlar başlayınca** |
| 4g | Teklif politikası | 🟡 | (a) Veri: kayıp nedeni listesi ve varsayılan geçerlilik süresi statik kataloğa (`ApprovedSalesQuotePolicyCatalog`) girilir; (b) kalıcı kayıt + bakım ekranı | (a) **Satış/Claude + Sinan** — `NEEDS-SINAN` (kayıp nedenleri, varsayılan geçerlilik); (b) **Ayarlar** — **ertelendi: Ayarlar başlayınca** |
| 5h | Cari e-belge profili | 🟡 | Cari portu (`ICounterpartyAccountEDocumentProfileSourceV1`, #860) hazır; Satış fatura dalgasında tüketici | **Satış/Claude şeridi** |
| 5d | Müşteri hesabı doğrulama adaptörü | 🟡 | `CounterpartySalesPartyAccountPortV1`'in **olumlu yol** gerçek-SQL testi (olumsuz yol `:84-170`'te var; yeni Cari portu/tablosu gerekmez) | **Satış/Claude şeridi** (host adaptörü Satış'ın); Cari bağının yeniden incelenmesi — **ertelendi: Cari başlayınca** |
| 5e | Teslim adresi / ilgili kişi adaptörü | 🟡 | Aynı adaptörün iletişim bilgisi yolunun gerçek-SQL testi | **Satış/Claude şeridi**; Cari bağının yeniden incelenmesi — **ertelendi: Cari başlayınca** |
| 5f | Müşteri seçimi / kayıt gösterimi adaptörleri | 🟡 | Host adaptörlerinin gerçek-SQL testi | **Satış/Claude şeridi** (host adaptörleri Satış'ın); Cari bağının yeniden incelenmesi — **ertelendi: Cari başlayınca** |
| 5k2 | Aktif rol kodu okuması | 🟡 | Gerçek `IActiveRoleCodeReader` ile Satış SQL testi | **Satış/Claude şeridi** |
| 6a | Belge eki sahip bağı | 🟡 | `SalesDocumentOwnerLinkPort` gerçek-SQL testi | **Satış/Claude şeridi** |
| 5j | Teklif onaylarının İş kutusu | 🟡 | Teklif onayları için `INativeApprovalReadAdapterV1` | **Satış/Claude şeridi** |
| 5l | Bağlı satın alma (iptal etkisi, Alış MOD-03) | ❌ | Veri sağlayan Alış portu (Satış'ın `ISalesOrderLinkedSupplyReadPortV1` sözleşmesine uyar) | **Alış** (üretici); sözleşme sahibi ve tüketici **Satış/Claude** (`docs/delivery/ALIS_HAZIRLIK_PLANI_v1.md:339`) |
| 5m | "Tedarikçide hazır" dahil alış gelecek arzı (Alış MOD-16) | ❌ | `IPurchaseSupplyReferencePortV1` v2 (MOD-14 ile aynı port) | **Alış** (üretici); kaynak planı/sipariş ekranı tüketicisi **Satış/Claude** (`ALIS_HAZIRLIK_PLANI_v1.md:351`) |
| 6f | Satış outbox olayları | ❌ | Satış outbox tablosu + `sales.invoice.finalized.v1` (SAT-D208, kararlı) ve önerilen sipariş olayları | **Satış/Claude şeridi**; yayın ERP-000F dağıtıcısına bağlı |

### Üretim şeridinin teyidi (2026-11-02, koordinatör üzerinden)

| Konu | Durum | Sorumlu ve dalga |
|---|---|---|
| S-1 bağlı üretim | ❌ | Satış: `ISalesOrderLinkedSupplyReadPortV1` + `LinkedProduction` dalı (**Satış/Claude**). Üretim: `IManufacturingSalesOrderLinkReadPortV1` (**Üretim U5-a**). Host adaptörü Satış'ın onayıyla bağlanır |
| `ISalesSubcontractOrderBindingReadPortV1` | 🟡 | Host adaptörü **Üretim + Stok, U5-b**; o zamana kadar fason siparişin carisi değişmez (503) |
| S-2 kaynak planı "üretim" seçeneği | ❌ | **Satış/Claude, S6 dalgası** |
| S-2 plan satırı ↔ sipariş satırı | 🟡 | **Üretim U2-b**, `ISalesOrderLineReferencePortV1.GetApprovedLineAsync` ile (Satış portu ✅) |
| `ISalesSubcontractServiceOrderReadPortV1` tüketimi | 🟡 | **Üretim U5-b** (Satış portu ✅) |

Ürün satırları (5a–5c): eksik yok; Ürün bağları runtime yapılandırmasına bağlıdır (dipnot ¹) — yeniden inceleme **ertelendi: Ürün
başlayınca**. Cari satırları (5d–5f, 5h): Cari tarafında yeni port/tablo/ekran istenmiyor; kalan işler Satış'ın host adaptörü testleri
ve fatura dalgasındaki tüketimdir — Cari bağlarının yeniden incelenmesi **ertelendi: Cari başlayınca**. Kimlik 5k'de eksik yok.

## Kalıcı kural

Kaynak: Sinan'ın 2 Kasım 2026 talimatı (koordinatör oturumu session_015Usc2qbq4qsgayZcDY3UTb üzerinden iletildi; koordinatörün
devir notu: https://github.com/sinangym/GoymenERP/issues/66). Bu bir
**süreç kuralıdır**, iş kararı değildir; karar sicilini değiştirmez.

Bundan sonra açılan **her Satış şeridi PR'ının açıklamasına bir "Modül bağlantıları" satırı** eklenir: PR'ın eklediği,
değiştirdiği veya tükettiği port/olay adları ve sürümleri (ör. `ISalesPartyRiskReadPortV1`,
`sales.subcontract-service-order.v1`), karşı modül ve durum (✅/🟡/❌). Bağ yoksa satır "Modül bağlantıları: yok" yazar. Bu
tablo o satırlardan güncellenir. Üretim planı §11a (#918) aynı kuralı Üretim PR'larına uygular.
