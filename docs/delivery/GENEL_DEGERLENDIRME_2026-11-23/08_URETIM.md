# Üretim — durum ve yapılacaklar

> Kapsam: Üretim (Manufacturing) modülü; arka uç, HTTP uçları, ekranlar, diğer modüllerle bağlantılar.
> HEAD `ddc6cba`, 23 Kasım 2026. Salt okunur kod incelemesidir; derleme ve test koşulmadı.
> Bu belge karar veya onay değildir; hiçbir `OPEN-*` kapatmaz, politika seçmez.

## 1. Kısa sonuç

Üretim modülü kod olarak büyük ve ciddidir: 47 HTTP ucu, 6 ekran, 29 veritabanı geçişi, altı gerçek hesap
motoru, 974 birim testi vardır. Bugün kodda şu zincir baştan sona yazılmıştır: ana rulo (etiket) seçerek plan →
ikinci kişinin onayı (onayda hammadde stokta ayrılır) → üretim emri → girdi verme / iade → çıktı (sağlam, ikinci
kalite, kullanılabilir artık, hurda, proses kaybı) → kütle dengesi kontrolüyle kapatma → maliyet (TL ve USD) →
soy ağacı. Stok hareketi ile üretim kaydı aynı veritabanı işleminde yazılır; aynı olay iki kez hareket üretmez.

Bugün **kullanılamayan** kısım ve en önemli eksikler:

1. **Yetki yok.** Üretimin 17 izninin hiçbiri başlangıç rollerinde değildir; iş rolü tanımlı değildir. Menüde
   "Üretim" hiçbir kullanıcıya görünmez.
2. **Plan ekranı hesap motorlarının istediği değerleri soramıyor.** Dilmede net kullanılabilir en ve hat hızı,
   boru/profilde paket–adet–boy–birim kg, boyada adet–boy–çevre, soğuk haddede hedef kalınlık için ekranda giriş
   alanı yoktur. Arka uç bunları kabul eder; ekran göndermez. Bantlı dilme planı bu yüzden ekrandan
   onaylanamaz görünüyor; öbür proseslerde hesap "değer eksik" diye boş kalır.
3. **Plan ve emrin okunur numarası yok.** Listeler kimliğin son 8 karakterini gösterir; arama yoktur.
4. **Kontrol Merkezi, Gantt, operatör/tablet ekranı, barkod okutma, vardiya, duruş, kalıp, bakım, makine verimi
   (OEE) ve malzeme ihtiyaç planlaması (MRP) yoktur.** Kuyruk yalnız bir planın ayrıntı sayfasında görünür.
5. **Fason:** gelen fasonun arka ucu vardır, ekranı yoktur (sipariş bağlama ekranı ve müşteri malı seçimi).
   Giden fasonun üretim tarafı (dönüş dengesi) hiç yazılmamıştır.
6. Çıktı etiket numarasını Ayarlar'dan üretecek bağlantı kayıtlı değildir; her çıktıda numara elle girilir.
   Kapalı emri yeniden açma arka uçta bağlıdır ama ekranda kapalı işaretlidir.

## 2. Hedef: bu modülden ne isteniyor

| Kaynak | İstenen |
|---|---|
| Master §7.7 | Beş üretim motoru (boy kesme, dilme, boru/profil, boyama, soğuk hadde) ortak plan kabuğunda; plan akışı: hedef → hammadde → hesap → çıktılar → denge → kapasite/onay |
| Master §7.7 | Üretim Kontrol Merkezi: kuyruk, sıra, hat, kalıp/takım, vardiya, tahmini başlangıç/bitiş; kurulum, bakım, duruş; Gantt; yapay zekâ kuyruk önerisi |
| Master §7.7 | Gerçekleştirme: Hızlı Tamamla, Fiilî Değerleri Düzenle, İstisna/Kalite Kaydı. Plan stok hareketi yaratmaz, ayırma (tahsis) yaratabilir |
| Master §8.3 | Etiket → girdi tahsisi → plan → kuyruk → fiilî tüketim → çıktı/fire/ikinci kalite → etiketler → soy ağacı → kalite serbest bırakma → satış tahsisi → yükleme |
| Master §28.3–28.9, §28.11 | Formüller CUT, SLT, WND, TUB, PNT, CR, BAL; her biri sürümlü ve golden testli; katsayılar kodda sabit değil; takvim ve kapasite |
| Master §29.5 | AT-041…AT-052 kabul senaryoları |
| 1 Kasım kararları (URT-D1…D18) | Tek plan = tek emir; tek onay, hazırlayan ≠ onaylayan; etiket miktarla bölünür; kısmi gerçekleştirme; fiilî tartım + öneri; hat başına ±%2 kütle toleransı; %5 üstü fire onay ister; hurda etiketle stoğa girer; parametreler Ayarlar'da; isteğe bağlı sipariş bağı; **üretimde kalite karantinası yok**, yalnız ikinci kalite ayrımı; **iki yönlü fason** ilk sürümde |
| 2 Kasım ekleri (URT-D19…D27) | Hammadde plan onayında ayrılır; müşteri malı yalnız kendi fason siparişine; boya cinsi/tedarikçi/renk; soğuk hadde tek geçiş; paket içi adet ürün kartından öneri |
| 12 Kasım (URT-D28…D39) | Çıktı düzeltme ters kayıtla; emir iptali, plan Onaylı'ya döner; kapalı emri gerekçeyle yeniden açma (muhasebe dönemi kapalıysa açılmaz); termin elle girilir; yeni emirde etiketi kullanıcı seçer |
| 13–14 Kasım (URT-D40…D59) | Çıkan girenden fazla olamaz (rulo bazında); maliyet TL ve döviz; hurda aylık fiyatla; fire maliyete katılım ayarı; yuvarlama kuralı; yeniden açılan emrin maliyeti yeniden hesaplanır |
| 14 Kasım izlenebilirlik (URT-D62, D63) | Çıktıya bağımsız etiket numarası (biçim Ayarlar'dan); çok girdili çıktıda girdi payı tüketilen kg oranında |
| Sonraki cevaplar (URT-D66…D112) | Termin kuralları, takvim ve kapasiteye dayalı termin önerisi, kuyruk sırası ve öncelik puanı, elle sıra, adetli çıktı, aylık hurda fiyatı düzeltmesi |

Not: 1 Kasım kararı (URT-D14) master §8.3'teki "kalite serbest bırakma" adımını ve AT-051'i üretim kapsamından
çıkarmıştır. 12 Kasım kararı (URT-D29) kapasite/takvim hesabını v1 dışı saymış, sonraki cevaplar (URT-D86…D91)
bunu **öneri** olarak geri getirmiştir; termin yine elle onaylanır.

## 3. Bugün var olan

Sütunlar: Arka uç / Ekran / Menü = var (✓), yok (–), kısmi (◐). Seviye: 0, 1, 1-kısmi.
Kısa yollar: `BE` = `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing`,
`API` = `src/backend/GoymenERP.Api`, `FE` = `src/frontend/goymen-erp-web/src/manufacturing`,
`UT` = `tests/GoymenERP.Manufacturing.UnitTests`, `IT` = `tests/GoymenERP.Api.IntegrationTests`.

### 3.1 Hesap motorları ve kütle dengesi

| Yetenek | Arka uç | Ekran | Test kaynağı | Seviye | Not / Kanıt |
|---|---|---|---|---|---|
| Dilme (SLT-001…007, 010) | ✓ | ◐ | ✓ golden | 1-kısmi | `BE.Domain/Engines/Slitting/SlittingEngineV1.cs`; golden: `UT/Engines/SlittingWindingGoldenTests.cs`. Ekranda net en ve hat hızı alanı yok (§4.3) |
| Sarım (WND-001…009) | ✓ | ◐ | ✓ golden | 1-kısmi | `…/Engines/Winding/WindingEngineV1.cs`; dilme onay kapısında kullanılır (`BE.Domain/Plans/SlittingApproval.cs`). Ekranda yalnız "sarım hedefi kg" var |
| Boy kesme (CUT-001…006) | ✓ | ✓ | ✓ golden | 1 | `…/Engines/CutToLength/CutToLengthEngineV1.cs`; `UT/Engines/CutToLengthEngineTests.cs`; ekran `FE/CutToLengthLinesEditor.tsx` |
| Boru/Profil (TUB-001…004) | ✓ | – | ✓ golden | 1-kısmi | `…/Engines/TubeProfile/TubeProfileEngineV1.cs`. Paket, adet, boy, birim kg için ekran alanı yok |
| Boyama (PNT-001…006) | ✓ | ◐ | ✓ golden | 1-kısmi | `…/Engines/Painting/PaintingEngineV1.cs`. Ekranda yalnız boya ürünü/tedarikçi/renk seçimi var (`FE/PaintSelectionEditor.tsx`); adet, boy, çevre alanı yok |
| Soğuk hadde (CR-001…005) | ✓ | – | ✓ golden | 1-kısmi | `…/Engines/ColdRolling/ColdRollingEngineV1.cs`. Hedef kalınlık için ekran alanı yok |
| Kütle dengesi (BAL-003, 004, 006) | ✓ | ✓ | ✓ | 1 | `…/Engines/Balance/MassBalanceEngineV1.cs`; emir ekranında `GET orders/{id}/mass-balance` |
| BAL-005 (boş gerçek miktarın dağıtımı) | – | – | – | 0 | Motorun kendi notu: "Not in version 1" |
| CUT-007/008 (ham boydan kesim adedi, boy firesi) | doğrulanmadı | – | – | – | Kesim payı (kerf) Ayarlar anahtarı olarak var (`kerfMm`, `kerfMode`); formülün kendisini ayrıca aramadım |

Altı motorun hepsi gerçek hesaptır: saf, `decimal`, girdi eksikse "değer eksik" döner ve eksik parametreyi adıyla
söyler; sıfır ya da varsayılan uydurmaz. Master §28'deki golden örneklerin sayıları test dosyalarında geçer
(18545.625, 25300, 1371.14, 76.8, 1627.742, 9.177 araması 5 dosyada eşleşti).

**Parametreler Ayarlar'dan gelir.** Yoğunluk 7850 kg/m³, boya hızı 20 m/dk, 80 mikron, katı madde 0,55, fire
katsayısı 1,10, eşzamanlı 3 profil, kerf 0 mm, kenar kaybı 0 mm değerleri üç şirket için onaylı başlangıç
kataloğundadır (`src/backend/Modules/Settings/…Application/ApprovedManufacturingProfileCatalog.cs`). İş merkezi
profili proses profilinden üstündür; plan onayında değerler plana kopyalanır ve sonraki ayar değişikliği eski
planı etkilemez (`BE.Application/ManufacturingPlanParametersV1.cs`). Boru/Profil için katalog profili yoktur;
dilme ve boru hat hızı katalogda yoktur.

**Ekran yeniden hesaplamaz.** Plan önizlemesi sunucudaki tek hesaplayıcıyı çağırır
(`POST plans/preview`, `BE.Domain/Plans/ProductionPlanCalculatorV1.cs`); ekran yalnız cevabı gösterir
(`FE/planPreview.ts` başlık notu; `FE` içinde yoğunluk/hız/formül sabiti araması 0 sonuç). Maliyet panelleri de
toplam ve kur hesaplamaz. Ayrı bir üretim raporu ekranı olmadığı için "rapor aynı hesaplayıcıyı kullanıyor mu"
sorusu yalnız maliyet izi ve emir ekranı için geçerlidir; ikisi de sunucu değerini gösterir.

### 3.2 Plan

| Yetenek | Arka uç | Ekran | Menü | Test | Seviye | Not / Kanıt |
|---|---|---|---|---|---|---|
| Taslak plan kaydı (iş merkezi, ana rulolar, kg, hedef) | ✓ | ✓ | ◐ | ✓ | 1 | `POST plans`; `FE/PlanCreatePage.tsx`. Etiket koduyla aranır. Menüde doğrudan giriş yok (§4.2) |
| Canlı önizleme | ✓ | ✓ | – | ✓ | 1 | `POST plans/preview`; `FE/usePlanPreview.ts` |
| Taslağı düzeltme ve geçmiş | ✓ | ✓ | – | ✓ | 1 | `POST plans/{id}/revision`, `GET …/revisions`; `FE/PlanEditForm.tsx`, `PlanHistory.tsx` |
| Onay (hazırlayan onaylayamaz) | ✓ | ✓ | – | ✓ | 1 | `POST plans/{id}/approval`; kural kodu `PlanSelfApproval` |
| Onayda hammadde ayırma | ✓ | ✓ | – | ✓ | 1 | `API/ManufacturingRuntime/ManufacturingPlanAllocationHost.cs`; stok yetmezse eksik gösterilir (`FE/PlanAllocationShortfallPanel.tsx`) |
| Ret, iptal | ✓ | ✓ | – | ✓ | 1 | `…/rejection`, `…/cancellation` |
| Emir iptalinden sonra yeniden onay | ✓ | ✓ | – | ✓ | 1 | `…/reapproval`; `FE/PlanReapprovalSection.tsx`; ayar değiştiyse reddeder |
| Girdi uygunluğu | ✓ | ✓ | – | ✓ | 1 | `GET plans/{id}/input-availability`; `FE/PlanInputAvailabilityPanel.tsx` |
| Termin (elle) | ✓ | ✓ | – | ✓ | 1 | `POST plans/{id}/due-date`; onay termin ister, geçmiş gün reddedilir |
| Termin önerisi (takvim + kapasite + kuyruk) | ✓ | ✓ | – | ✓ | 1 | `BE.Domain/Plans/ProductionDueDateSuggestionCalculatorV1.cs`; kapasite `inputCapacityKgPerHour` Ayarlar'dan; öneri kaydetmez |
| Öncelik puanı | ✓ | ✓ | – | ✓ | 1 | `GET/POST plans/{id}/priority`; gerekçe zorunlu |
| İş merkezi kuyruğu ve elle sıra | ✓ | ◐ | – | ✓ | 1-kısmi | `GET …/work-center-queue`, `POST …/reorder`; ekran yalnız plan ayrıntısındaki panel (`FE/PlanQueuePanel.tsx`). Ayrı kuyruk/kontrol ekranı yok |
| Satış siparişi satırına bağ | ✓ | – | – | ✓ | 1-kısmi | `POST plans/{id}/sales-order-link`. Ekranda yalnız salt okunur gösterim (`FE/PlanDetailPage.tsx:408`); bağlayan form yok (`sales-order-link` araması ön yüzde 0) |
| Plan listesi | ✓ | ✓ | ◐ | ✓ | 1 | `GET plans`; `FE/PlanListPage.tsx` |
| Sonlu kapasite çizelgeleme, Gantt | – | – | – | – | 0 | `Gantt`, `SetupMinutes`, `CleanupMinutes` araması 0 |
| Yapay zekâ kuyruk önerisi | – | – | – | – | 0 | Modülde AI çağrısı yok |

### 3.3 Üretim emri ve gerçekleştirme

| Yetenek | Arka uç | Ekran | Test | Seviye | Not / Kanıt |
|---|---|---|---|---|---|
| Onaylı plandan emir (plan başına bir açık emir) | ✓ | ✓ | ✓ | 1 | `POST orders`; kural `PlanOrderExists` |
| Girdi verme (stoktan çıkış) | ✓ | ✓ | ✓ | 1 | `POST orders/{id}/input-issues`; `API/ManufacturingRuntime/ManufacturingInventoryInputIssueHost.cs` |
| Kullanılmayan girdinin iadesi | ✓ | ✓ | ✓ | 1 | `…/input-returns`; özgün çıkış hareketi seçilebilir |
| Çıktı bildirimi: sağlam, ikinci kalite, kullanılabilir artık, hurda | ✓ | ✓ | ✓ | 1 | `…/outputs`; sınıflar `BE.Domain/Orders/OutputClass.cs`; ürün ve lokasyon seçimi `references/output-products`, `output-locations` |
| Proses kaybı (stok dışı) | ✓ | ✓ | ✓ | 1 | `…/process-losses` |
| Yarı mamul (WIP) okuması | ✓ | ✓ | ✓ | 1 | `…/wip-measurements` |
| Adetli çıktı (boru/profil) ve kg sapması | ✓ | ✓ | ✓ | 1 | `BE.Application/ManufacturingOutputPiecesV1.cs`; `FE/OutputPieceTable.tsx` |
| Kg dışı girdi (boya litre, metre, adet) | ✓ | ✓ | ✓ | 1 | `GET/POST …/non-kg-inputs`; `FE/NonKgInputPanel.tsx` |
| Çıktı düzeltme (ters kayıt + doğru tartım) | ✓ | ✓ | ✓ | 1 | `…/facts/{factId}/correction`; `FE/OutputCorrectionSection.tsx`; ayrı izin `manufacturing.facts.correct` |
| Kapatma: rulo bazında denge, tolerans, fire eşiği | ✓ | ✓ | ✓ | 1 | `…/close`; `BE.Domain/Orders/OrderClosureAssessment.cs`; tolerans Ayarlar profilinden |
| Tolerans dışı kapanışa istisna onayı | ✓ | ✓ | ✓ | 1 | Ayrı izin `manufacturing.orders.close-exception.approve` |
| İptal (net etkisi kalmamış emir) | ✓ | ✓ | ✓ | 1 | `…/cancellation`; `FE/OrderCancelSection.tsx` |
| Kapalı emri yeniden açma | ✓ | ◐ | ✓ | 1-kısmi | `…/reopening`, muhasebe dönemi okunur. Ekran bileşeni var (`FE/OrderReopenSection.tsx`) ama kabuk kaydında `ImplementationAvailable = false` (`BE.Infrastructure/Shell/ManufacturingShellCapabilityProvider.cs:234`); düğme kimseye görünmez |
| Emrin termini | ✓ | ✓ | ✓ | 1 | `POST orders/{id}/due-date` |
| Çıktı etiketi ve bağımsız numara | ✓ | ◐ | ✓ | 1-kısmi | Elle girilen numara çalışır. Ayarlar biçiminden otomatik numara: port kayıtlı değil (§4.4) |
| Soy ağacı (girdi payıyla, ileri/geri) | ✓ | ✓ | ✓ | 1 | `GET orders/{id}/lineage`; `BE.Domain/Lineage/ProductionLineageShareCalculatorV1.cs`; `FE/OrderLineagePanel.tsx` |
| Emir listesi (duruma göre süzme) | ✓ | ✓ | ✓ | 1 | `GET orders`; `FE/OrderListPage.tsx` |
| "Çıkan girenden fazla kapanmış emirler" raporu | ✓ | – | ✓ | 1-kısmi | `GET reports/output-above-input-closures`; ekranı yok (ön yüzde arama 0) |
| Kalite karantinası / serbest bırakma | – | – | – | 0 | `Quarantine`, `Karantina`, `QualityRelease` araması 0. Karar gereği kapsam dışı (URT-D14) |
| Hızlı Tamamla modu | – | – | – | 0 | `quick`, `Hızlı` araması ön yüzde 0 |
| Etiket basımı, barkod | – | – | – | 0 | `barcode`, `barkod`, `print`, `pdf` araması `FE` içinde 0 |

### 3.4 Maliyet

| Yetenek | Arka uç | Ekran | Test | Seviye | Not / Kanıt |
|---|---|---|---|---|---|
| Üretim maliyeti (TL): girdi maliyeti → çıktılara dağıtım | ✓ | ✓ | ✓ golden | 1 | `BE.Domain/Costing/ProductionReceiptCostCalculatorV1.cs`, `ProductionScrapCostDistributionCalculatorV1.cs`; `GET orders/{id}/production-cost`; `FE/OrderCostPanel.tsx` |
| Yuvarlama ve kalan kuruş | ✓ | – | ✓ | 1 | `…/Costing/ProductionCostRoundingCalculatorV1.cs` |
| USD maliyeti (gösterim) | ✓ | ✓ | ✓ | 1 | `GET …/production-cost-usd`; `FE/OrderUsdCostPanel.tsx`; hurda için tartım günü kuru `API/ManufacturingRuntime/ManufacturingScrapUsdRateReader.cs` |
| Maliyet tanıma ve nesil geçmişi (kapanışta, gecikmişse sonradan) | ✓ | ✓ | ✓ | 1 | `GET …/cost-generations`, `POST …/cost-recognition`; `API/ManufacturingRuntime/ManufacturingCostGenerationHost.cs`; `FE/CostGenerationPanel.tsx` |
| Maliyet izi (emir bazında süre, girdi, fire) | ✓ | ✓ | ✓ | 1 | `GET …/cost-trace`; `FE/OrderCostTracePanel.tsx` |
| Aylık hurda fiyatı: giriş ve düzeltme | ✓ | ✓ | ✓ | 1 | `GET/POST scrap-prices`, `…/corrections`; `FE/ScrapPriceListPage.tsx` |
| Fire maliyete katılım ayarı | ✓ | ✓ | ✓ | 1 | Ayarlar: `settings.manufacturing-scrap-cost.manage` (menü "Fire maliyete katılımı") |
| Maliyet farkının muhasebeye önerilmesi | ✓ | ✓ | ✓ | 1 | Muhasebe tarafında: `API/Endpoints/AccountingEndpoints.CostDifferences.cs`; menü "Üretim maliyet farkları" |
| İşçilik, enerji, genel üretim gideri payı | – | – | – | 0 | `Labor`, `Overhead`, `Energy` kavramı modülde yok; maliyet yalnız malzeme maliyetinin dağıtımıdır |

### 3.5 Fason

| Yetenek | Arka uç | Ekran | Seviye | Not / Kanıt |
|---|---|---|---|---|
| Gelen fason: müşteri malı planı yalnız o müşterinin fason hizmet siparişine bağlanır | ✓ | – | 1-kısmi | `BE.Application/ManufacturingSubcontractServiceOrderV1.cs`, `ManufacturingSalesOrderLinkV1.cs`. Bağlama ekranı yok; plan oluşturma ekranı müşteri malı bağlamı göndermiyor (`FE/PlanCreatePage.tsx` içinde `commercialContext` araması 0) |
| Gelen fason: girdi verirken sahiplik kontrolü | ✓ | – | 1 | `BE.Application/ManufacturingCustomerOwnedInputGuardV1.cs` |
| Gelen fason: tamamlanan üretimin sevke bildirilmesi | ✓ | – | 1 | `ManufacturingSubcontractCompletionReadPortV2`; Lojistik okur (`API/LogisticsRuntime/LogisticsSubcontractDeliveryHost*.cs`) |
| Giden fason: dönüş dengesi (çıktı + fire = gönderilen) | – | – | 0 | Plandaki `POST subcontract-returns` ve `manufacturing.subcontract.record` izni kodda yok. Satın Alma'da hizmet siparişi uçları var (`API/Endpoints/PurchasingEndpoints.ServiceOrders.cs`), üretim bağlantısı yok |

### 3.6 Sahada olması beklenen, kodda olmayanlar

Aşağıdakiler için `src/backend/Modules/Manufacturing` içinde arama yapıldı; hepsi 0 sonuç verdi:
`Operator|Operatör`, `OEE`, `Maintenance|Bakım`, `Gantt`, `MRP|MaterialRequirement`,
`BillOfMaterial|Recipe|Reçete`, `Routing`, `QualityPlan|Inspection|Muayene`, `Die|Kalıp|Tooling`.
`Shift|Vardiya` yalnız termin önerisinin okuduğu takvimde geçer. `Stoppage` yalnız maliyet izinde bir alan
adıdır; duruş kaydı girilen bir yer yoktur.

Var olanlar (Üretim'in dışında): iş merkezi kartı ve çalışma takvimi Organizasyon modülündedir
(`src/backend/Modules/Organization/…Domain/WorkCenters/`; ekran `src/frontend/…/organization/WorkCenterDetailPage.tsx`,
`WorkCenterCalendarSection.tsx`). İş merkezi türleri beştir: boya, boru/profil, dilme, soğuk hadde, boy kesme.
Saatlik kapasite Ayarlar'dadır (`src/frontend/…/settings/WorkCenterCapacityPage.tsx`).

### 3.7 Kabul senaryoları (master §29.5)

| Senaryo | Durum | Not |
|---|---|---|
| AT-041 Kaynağı hazır olmayan plan hazır olamaz | Kodda var | Onayda stok ayırma; yetmezse onay reddedilir |
| AT-042 Tüketim tahsis ve mevcudu aşamaz | Kodda var | Girdi verme stoktaki ayırmadan düşer; `AllocationExceeded`, `PhysicalStockInsufficient` |
| AT-043 Boy kesme dengesi | Kodda var | Motor + kapanış dengesi |
| AT-044 Dilme genişlik ve kenar firesi | Kodda var, ekran eksik | Net en ekrandan girilemiyor |
| AT-045 Boru/profil golden | Motor var, ekran yok | `UT/Engines/TubeProfileAndPaintingEngineTests.cs` |
| AT-046 Boyama dengesi | Kısmi | "Yeniden işleme" çıktı sınıfı yok; sınıflar beş tanedir |
| AT-047 Soğuk hadde dengesi | Motor var, ekran yok | |
| AT-048 Tolerans dışı fark onaysız kapanamaz | Kodda var | İstisna onayı ayrı izinle |
| AT-049 Yeni sürüm geçmişi değiştirmez | Kodda var | Plan onayında ayar kopyası |
| AT-050 Çıktı etiketi girdilere izlenir | Kodda var | Soy ağacı |
| AT-051 Karantinadaki çıktı sevk edilemez | Kapsam dışı | URT-D14 |
| AT-052 Düzeltme silmez, ters kayıt kurar | Kodda var | `OutputReversals`, ekleme-yalnız |

### 3.8 Test kaynağı

- Birim testi: `UT` altında 974 `[Fact]`/`[Theory]`.
- Gerçek veritabanı ve HTTP kabul testleri: `IT` altında adında "Manufacturing" geçen 109 dosya;
  `tools/relational_inventory/manufacturing.txt` 197 satır.
- Ön yüz: `FE` altında 48 test dosyası, 465 `it`/`test`.
- Tarayıcı uçtan uca senaryosu: üretim için ayrı bir Playwright dosyası bulamadım.
- `docs/handoffs/TREN1_DEVIR/durum/W8_uretim.md` son dalganın (yeniden açma, maliyet nesli, elle kuyruk, kg dışı
  girdi) testlerinin yazıldığını ama **koşulmadığını** ("NOT_RUN") söyler. Bu kayıt 16 Kasım civarıdır; bugünkü
  durumu koordinatörün test sonucu belirler.

## 4. Kullanılabilirlik engelleri

### 4.1 Rol ve yetki (engelleyici)

`ErpPermissions.cs` içinde 17 üretim izni vardır: `manufacturing.plans.read / write / approve / cancel / reorder`,
`orders.read / create / write / close / close-exception.approve / cancel / reopen`, `facts.record / correct`,
`scrap-prices.read / write`, `reports.read`. `src/backend/GoymenERP.Api/Seed/identity-seed.json` içinde
`manufactur` araması 0 sonuç verir; dosyadaki izinler yalnız `identity`, `organization`, `counterparty`
alanlarındadır. Ayarlar'daki üretim izinleri de (`settings.manufacturing-parameter-profiles.*`,
`settings.manufacturing-tolerance-profiles.*`, `settings.manufacturing-scrap-cost.manage`) aynı durumdadır.
Kararlar kişileri tarif eder (Üretim planlama hazırlar, Üretim müdürü onaylar; raporu Üretim Müdürü, Üretim
Planlama, Genel Müdür okur) ama rol ataması insan kapısıdır.

Ayrıca dilme planının onayı etiketi Stok'tan okur; onaylayan kişide `inventory.labels.read` ve
`inventory.balances.read` yoksa yalnız dilme planında "yetki yok" alır (kabuk sağlayıcısındaki not).

### 4.2 Menü

Kenar menüde Üretim altında tek giriş vardır: "Üretim emirleri" (`shell/navigation.ts:496`), yalnız
`manufacturing.orders.read` iznine bağlıdır. Planlar, yeni plan ve hurda fiyatları sayfalarına emir listesinin
üstündeki bağlantılardan gidilir (`FE/OrderListPage.tsx:152-162`). Sonuç: plan izni olup emir okuma izni olmayan
bir planlamacı menüden üretime giremez. Master'daki "Kontrol Merkezi; Planlar; Gerçekleştirme" üçlüsü menüde yoktur.

### 4.3 Plan ekranında eksik giriş alanları (engelleyici)

Arka uç plan isteğinde şu değerleri kabul eder; ekran hiçbirini sormaz:

| Değer | Hangi proses | Arka uç | Ekran |
|---|---|---|---|
| Net kullanılabilir en (mm) | Dilme | `SlittingSpecRequestV1.NetUsableWidthMm` | `FE/PlanCreatePage.tsx` ve `PlanEditForm.tsx` içinde arama 0 |
| Hat hızı (m/dk) | Dilme, boru | `lineSpeedMetresPerMinute` | Giriş alanı yok; düzeltme formu yalnız var olanı korur |
| Paket sayısı, paket içi adet, boy, birim kg | Boru/profil | `PlanProcessSpec` | Alan yok |
| Paket, adet, profil boyu, çevre | Boyama | `PlanProcessSpec` | `PaintSelectionEditor.tsx` bunları `null` gönderir |
| Hedef kalınlık | Soğuk hadde | `PlanProcessSpec.TargetThicknessMm` | Alan yok |

Etkisi: bant satırı içeren bir dilme planının onay kapısı (`SlittingApprovalGate.Require`) net eni yalnız plandan
okur; değer yoksa "planın hesabı için gereken değerler eksik" diye reddeder. Hat hızı Ayarlar'da iş merkezi
profiline girilirse oradan gelir; net en için böyle bir yol yoktur. Bant satırı olmayan (yalnız rulo + kg) plan
bu kapıdan geçmez ve onaylanabilir. Bu sonucu koddan okudum, çalıştırarak doğrulamadım (§9).

### 4.4 Kayıtlı olmayan bağlantılar

- `IManufacturingOutputLabelCodePolicyReadPortV1` (Ayarlar'daki çıktı etiket numarası biçimi) ve
  `IManufacturingLabelCodeRegisterReadPortV1` (numaranın stokta kullanılıp kullanılmadığı) için kayıt
  bulunamadı. Bu yüzden biçim "girilmemiş" sayılır ve her çıktı elle numara ister (`OutputLabelCodeRequired`).
  Modülün öbür 40'tan fazla portunun hepsi kayıtlıdır; sahte sağlayıcı görmedim.
- Yeniden açma: arka uç bağlı (`ManufacturingCostRuntimeComposition.cs`), kabukta kapalı (§3.3).

### 4.5 Kurulum verisi

Üretime başlamadan önce her şirkette şunların girilmiş olması gerekir; hepsinin ekranı vardır:
iş merkezleri ve türleri (Organizasyon), çalışma takvimi (Organizasyon), saatlik kapasite (Ayarlar), parametre
ve tolerans profilleri (Ayarlar; onaylı başlangıç değerleri hazır), fire maliyete katılım ayarı (Ayarlar),
aylık hurda fiyatı (Üretim), ürün kartlarında kalınlık/en ve adetli ürünlerde birim kg (Ürün), stokta etiketli
ana rulolar (Stok). Başlangıç kataloğu üç şirketin sabit kimliklerine bağlıdır; test ortamındaki şirket
kimliklerinin bunlarla eşleştiğini doğrulamadım.

### 4.6 İnsan kapıları

Rol/izin ataması; `production.execution.enabled` adıyla anılan üretim aktivasyonu (kodda böyle bir anahtar
bulamadım: `execution.enabled` araması 0; `appsettings*.json` içinde üretim anahtarı yok); `OPEN-005`,
`OPEN-007`, `OPEN-008` değerlerinin saha onayı.

## 5. Eksikler

### 5.1 İsteklerine göre

| # | Hedef | Kaynak | Durum |
|---|---|---|---|
| 1 | Beş prosesin ortak plan kabuğunda eksiksiz planlanması | Master §7.7, URT-D25…D27 | Motorlar tam; ekran yalnız dilme bantları, boy kesme satırları ve boya seçimini soruyor (§4.3) |
| 2 | Üretim Kontrol Merkezi | Master §7.7 | Yok. Kuyruk yalnız plan ayrıntısında |
| 3 | Gantt/zaman çizelgesi, kurulum–bakım–duruş etkisi | Master §7.7, §28.11 | Yok. Termin önerisi yalnız vardiya takvimi + saatlik kapasite + kuyruktaki kg ile çalışır |
| 4 | İsteğe bağlı sipariş bağı; iptalde bağlı üretim görünür | URT-D13 | Arka uç ve Satış tarafı okuma var; bağlayan ekran yok |
| 5 | İki yönlü fason | URT-D15…D17 | Gelen: arka uç var, ekran yok. Giden: üretim tarafı yok |
| 6 | Çıktı etiket numarası biçimi Ayarlar'dan | URT-D62 | Hesap var, Ayarlar bağlantısı kayıtlı değil |
| 7 | Kapalı emri yeniden açma | URT-D32 | Arka uç var, ekranda kapalı |
| 8 | Paket içi adet ürün kartından öneri | URT-D27 | Ekran alanı olmadığından öneri de yok |
| 9 | Fire cinsi listesi Ayarlar'da | URT-D8 | Doğrulanamadı; çıktı sınıfı beş sabit değerdir |
| 10 | Kısmi hammaddeyle başlatma eşiği, plan sürerken hammadde ekleme/değiştirme | Master §7.7 | Ayrı bir kural görmedim; etiket değişimi plan düzeltme + yeniden onay yoluyla |
| 11 | Çok geçişli soğuk hadde | URT-D26 | Bilinçli ertelendi; tek geçiş var |
| 12 | "Çıkan > giren" kapanmış emirler listesi | URT-D42, D52 | Uç var, ekran yok |
| 13 | Hurda fiyatının Satış'taki gibi ilerlemesi | URT-D57 | Aylık hurda fiyatı Üretim'de ayrı ekrandır; Satış fiyat akışıyla bağını görmedim |
| 14 | Plan onayının onay kutusuna (Work) düşmesi | Hazırlık planı U5-d | Yok; onay yalnız plan sayfasından |

### 5.2 Bir ERP'de olması gerekene göre

Çelik servis merkezi ve boru/profil üreten üç şirket için, hedef belgelerinde ya hiç olmayan ya da ertelenmiş
ama günlük işte aranacak olanlar:

- **Okunur belge numarası.** Plan ve emrin şirket/yıl bazlı sıra numarası yok; sahada "şu emir" denemez.
- **Operatör akışı.** Makine başında tablet ekranı, barkodla girdi rulosunu okutma, kantardan otomatik tartım,
  çıktı etiketini anında basma yok. Bugünkü emir ekranı tek sayfada çok sayıda form içeren bir büro ekranıdır
  (`FE/OrderDetailPage.tsx` 1772 satır).
- **Vardiya, operatör, duruş ve hız kaydı.** Kim, hangi vardiyada, ne kadar sürede üretti; neden durdu. Bunlar
  olmadan makine verimi (OEE) ve gerçek kapasite ölçülemez; termin önerisi yalnız girilen sabit kapasiteye dayanır.
- **Malzeme ihtiyaç planlaması.** Satış siparişlerinden üretim ihtiyacı, üretim planından hammadde ihtiyacı
  çıkaran bir hesap yok. Plan bugün elle yapılır: planlamacı stoktaki etiketi seçer, kg yazar, termin girer.
- **Ürün reçetesi / kesim reçetesi kütüphanesi.** Sık tekrarlanan bant kombinasyonları ve boy listeleri
  saklanıp yeniden kullanılamaz; her plan sıfırdan girilir.
- **Bıçak, kalıp, takım ve ayar süresi.** Boru hattında kalıp değişimi termini belirler; kodda yok.
- **Dönüşüm maliyeti.** İşçilik, enerji, amortisman payı yok; mamul maliyeti = hammadde maliyeti (fire payıyla).
  Fason hizmet fiyatlaması ve kârlılık için gerekecektir.
- **Bakım.** Master'da ayrı keşif paketine bırakılmıştır; üretimi durduran planlı bakım takvimde yalnız
  "kapalı gün" olarak girilebilir.
- **Kalite kaydı.** Karantina istenmedi; ancak müşteri şikâyeti ve ikinci kalite sebebi için ölçü/yüzey
  kontrol kaydı, sertifika (mill test) bağı ve sebep kodu yoktur.
- **Üretim raporları.** Dönemlik fire oranı, hat bazında üretim, plan–gerçek farkı, açık emirler yaşlandırması
  yok; rapor olarak yalnız emir bazında maliyet izi ve bir istisna listesi ucu var.

### 5.3 Ekran ve kullanım kolaylığı

- Yükleniyor, boş, yetkisiz, bulunamadı, çakışma, bağlantı hatası durumları ortak bileşenle ele alınmış
  (`StateBlock`, `RemoteViewState`); belirsiz sonuçta aynı istek aynı anahtarla yinelenir. Bu taraf sağlamdır.
- Listelerde yalnız durum süzgeci var; arama, tarih/iş merkezi/ürün süzgeci, sıralama, toplu işlem yok.
- Bazı panellerde ürün ve etiket ham kimlikle gösterilir (ör. `FE/PlanAllocationShortfallPanel.tsx`, plan
  ayrıntısındaki sipariş bağı): kullanıcı kod ya da ad görmez.
- Çıktı/PDF, Excel dışa aktarım, yazdırma yok (`FE` içinde arama 0): iş emri formu, üretim föyü, etiket.
- Mobil/tablet uyumu için üretim stil dosyalarında tek bir `@media` kuralı var (`PlanPage.module.css`);
  emir ekranında yok.
- Emir ekranı tek sayfada girdi, iade, çıktı, WIP, kayıp, düzeltme, kapatma, iptal, termin, maliyet ve soy
  ağacını toplar; master'daki üç gerçekleştirme modu ayrımı yoktur.

## 6. Diğer modüllerle bağlantılar

### 6.1 Üretim kimden ne alır

| Kimden | Ne | Mekanizma | Kanıt |
|---|---|---|---|
| Stok | Ana rulo adayları, etiket ayrıntısı, serbest miktar, etiket durumu | Okuma portu | `IInventoryParentRollValidationV1`, `IInventoryLabelDetailsV1`, `IInventoryFreeQuantityReadPortV1` |
| Stok | Girdi ve çıktı hareketlerinin maliyeti | Okuma portu | `API/ManufacturingRuntime/ManufacturingInventoryCostMovementReader.cs` |
| Ürün | Rulo kalınlık/en (plana kopyalanır), çıktı ürünleri, boya ürünleri | Okuma | `API/ManufacturingRuntime/ManufacturingOutputSelectionReader.cs`, `ManufacturingPaintSelectionSourceV1.cs` |
| Ayarlar | Parametre ve tolerans profili, fire maliyet ayarı, kapasite, kg yuvarlama | Okuma portu | `BE.Application/ManufacturingPlanParametersV1.cs`; `ManufacturingSettingsScrapCostPolicyReader.cs` |
| Organizasyon | İş merkezi ve çalışma takvimi | Okuma + yabancı anahtar | `ManufacturingModuleExtensions.cs` (Organizasyon'dan sonra kurulur) |
| Satış | Sipariş satırı onaylı mı; fason hizmet siparişi; girdi kuru için sipariş bağı | Okuma portu (API katmanı uyarlar) | `API/Endpoints/ManufacturingSalesLinkedSupplyHostPorts.cs`, `ManufacturingSubcontractOrderHostPorts.cs` |
| Cari | Müşteri malının sahibi (taraf ve hesap) | Okuma portu | `ICounterpartyManufacturingPartyAccountSourceV1` |
| Muhasebe | Dönem açık mı (yeniden açma için) | Okuma portu | `API/ManufacturingRuntime/ManufacturingReopeningAccountingPeriodStatusPortV1.cs` |

### 6.2 Üretim kime ne verir

| Kime | Ne | Mekanizma | Kanıt |
|---|---|---|---|
| Stok | Plan onayında girdi ayırma; iptal/kapanışta bırakma | Ortak işlem (tek veritabanı işlemi) | `ManufacturingPlanAllocationHost.cs` |
| Stok | Girdi çıkışı, iade, çıktı girişi (yeni etiket), çıktı ters kaydı | Ortak işlem | `ManufacturingInventoryInputIssueHost.cs`, `ManufacturingInventoryReturnOutputHost.cs`; Stok tarafı `InventoryManufacturingRealizationParticipant*.cs` |
| Stok | Kapanışta çıktı etiketlerine maliyet; yeniden açmada ters maliyet | Ortak işlem | `ManufacturingCostGenerationHost.cs`; `InventoryManufacturingCostGenerationParticipant.cs` |
| Stok | Soy ağacı kenarları ve girdi payları (iz ekranı için) | Okuma portu | `BE.Infrastructure/Persistence/ManufacturingLabelLineageReadPort.cs` |
| Satış | Siparişe bağlı üretim (iptal/zorla kapatmada görünür); fason siparişin carisi kilitli mi | Okuma portu | `ManufacturingSalesOrderSupplyReadPort.cs`, `ManufacturingSalesOrderBindingReadPort.cs` |
| Lojistik | Fason üretimin tamamlanan miktarı (teslim için) | Okuma portu | `ManufacturingSubcontractCompletionReadPortV2.cs` |
| Muhasebe | Üretim maliyet farkı önerisi | Olay kutusu: Stok yayımlar, Muhasebe tüketir | `Accounting…/ManufacturingCostDifferenceInboxConsumerV1.cs`; `API/Shell/TrainCompletionHostComposition.cs:97` |

**Çift hareket riski.** Her gerçekleşme komutu bir tekrar-önleme anahtarı (`Idempotency-Key`), beklenen sürüm ve
kendi olgu kimliğini taşır; üretim kaydı ile stok hareketi aynı işlemde yazılır, biri başarısız olursa ikisi de
geri alınır. Aynı istek yinelenirse ilk cevap döner. Düzeltme silmez, ters kayıt yazar. Kodda aynı olayın ikinci
bir stok hareketi üretmesine yol açan bir yol görmedim. Üretim başka modülün tablosuna doğrudan yazmaz; yazma
Stok'un kendi katılımcı sınıfı üzerinden olur.

### 6.3 Kopuk ya da eksik halkalar

1. **Satış → Üretim.** Siparişten plan açma düğmesi yok (`uretim/plan` yolu yalnız üretim klasöründe ve kabuk
   tablosunda geçer). Bağ arka uçta kurulabilir, ekrandan kurulamaz.
2. **Üretim → Satın Alma.** Satın alma talebi "Üretim" kaynağını tanır (`purchasing/contract.ts:343`), ama
   üretim tarafında eksik hammaddeden talep açan bir işlem yok; eksik yalnız ekranda gösterilir.
3. **Üretim → Muhasebe.** Giden şey maliyet **farkı** önerisidir. Üretim'in kendisi fiş kesmez; mamul maliyet
   fişinin (ilk madde tüketimi, mamul girişi) Muhasebe'de nasıl oluştuğunu bu incelemede izlemedim (§9).
4. **Giden fason.** Satın Alma hizmet siparişi ile üretim arasında port yok.
5. **Onay kutusu (Work).** Plan onayı ve kapanış istisnası görev olarak düşmez.
6. **Kalite → sevk.** Karar gereği yok; ikinci kalite ayrı etiketle stoğa girer, sevki engelleyen bir blok yoktur.

## 7. Açık kararlar

| Kimlik | Konu | Kullanımı engeller mi |
|---|---|---|
| `OPEN-005` | Kütle dengesi toleransının (±%2) saha doğrulaması ve nihai onayı | Hayır; başlangıç değeri geçerli (URT-D23) |
| `OPEN-007` | Kesin yoğunluk, hız, kerf, kaplama değerleri ve veri sahibi | Kısmen: dilme ve boru hat hızı ile gerçek kerf girilmeden süre ve fire hesabı eksik kalır |
| `OPEN-008` | Stok değerleme ve üretim maliyet yöntemi | Test kullanımını engellemez; maliyet rakamına güvenmeden önce karar gerekir |
| `OPEN-004` | Tartı/ölçü toleransı bantları | Hayır |
| Fire gider hesabı eşlemesi (`NEEDS-SPEC`, SAT-D308) | Fire maliyete yüklenmediğinde hangi gider hesabı | Hayır; yalnız raporlanır |
| Yuvarlama yönü (URT-D54 notu) | Maliyet yuvarlamasında yön | Hayır |
| Toplu mal kabulde kg farkı toleransı (KLN-D43 "Açık") | Üretim girdisinin stokta doğru kg ile durması | Dolaylı |
| Rol ve izin ataması | Kim planlar, kim onaylar, kim kapatır, kim maliyet görür | **Evet** |
| Hurda fiyatını kimin gireceği (URT-D57) | "Satış gibi ilerler" denmiş; izin kimseye verilmemiş | Maliyet için evet |

`docs/decisions/ACIK_KARARLAR.md` bu dört `OPEN` satırında "henüz uygulanmadı", "maliyet modülü henüz yok"
diyor; kod bunları uygulamış durumda (§8 belge notu).

## 8. Yapılacaklar (öncelik sırasıyla)

| # | İş | Neden | Öncelik | Büyüklük | Ön koşul |
|---|---|---|---|---|---|
| 1 | Üretim iş rollerini tanımla ve 17 üretim + ilgili Ayarlar izinlerini ata (planlama, üretim müdürü, operatör, maliyet okuyan) | Bugün hiç kimse üretim ekranını göremez | P0 | K | Sinan'ın rol kararı (insan kapısı) |
| 2 | Plan ekranına eksik alanları ekle: net kullanılabilir en, hat hızı, boru/profil ve boya ölçüleri, soğuk hadde hedef kalınlığı | Dilme bant planı onaylanamıyor; dört proseste hesap boş | P0 | O | – |
| 3 | Uçtan uca kabul koşusu: gerçek veritabanında plan → onay → emir → girdi → çıktı → kapanış → maliyet, her proses için bir örnek | Son dalganın testleri "koşulmadı" kaydıyla duruyor | P0 | O | 1, 2 |
| 4 | Plan ve emre okunur sıra numarası; listelerde arama ve iş merkezi/tarih süzgeci | Sahada emir kimlikle anılamaz | P0 | O | Numara biçimi kararı |
| 5 | Menüye "Planlar" girişini ekle (plan iznine bağlı) | Planlamacı emir izni olmadan giremez | P0 | K | – |
| 6 | Çıktı etiket numarası biçimini Ayarlar'a bağla (iki portu kaydet, Ayarlar ekranı) | Her çıktıda elle numara; URT-D62 | P0 | O | Ayarlar tarafı |
| 7 | Yeniden açma düğmesini aç (kabuk kaydı) ve kabulünü koş | Arka uç hazır, ekranda kapalı | P0 | K | 3 |
| 8 | Kurulum kontrol listesi ve üç şirket için başlangıç verisi (iş merkezi, takvim, kapasite, profiller, hurda fiyatı) | Veri olmadan plan açılamaz | P0 | K | 1 |
| 9 | Ham kimlik gösteren panellerde ürün/etiket kodu ve adı | Kullanıcı okuyamıyor | P0 | K | – |
| 10 | Sipariş bağlama ekranı ve Satış siparişinden "üretim planı aç" | URT-D13; gelen fasonun ön koşulu | P1 | O | – |
| 11 | Gelen fason ekran akışı (müşteri malı seçimi, fason siparişe bağ, tamamlanma) | URT-D15, D16 | P1 | O | 10 |
| 12 | Giden fason: gönderim, dönüş, denge, soy ağacı | URT-D17 | P1 | B | Satın Alma portu |
| 13 | İş merkezi kuyruk ekranı (Kontrol Merkezi'nin ilk hâli): hat seç, sırayı gör, elle sırala | Master §7.7 | P1 | O | – |
| 14 | Operatör ekranı: tablet düzeni, barkodla rulo okutma, çıktı etiketi basımı | Sahada kullanım | P1 | B | Etiket/barkod altyapısı (Stok) |
| 15 | İş emri ve üretim föyü çıktısı (PDF), liste dışa aktarım | Master §5 | P1 | O | Belge altyapısı |
| 16 | Eksik hammaddeden satın alma talebi açma | Üretim → Satın Alma halkası | P1 | K | – |
| 17 | "Çıkan > giren" listesi ve temel üretim raporları (fire %, hat üretimi, plan–gerçek) | URT-D42; yönetim görünürlüğü | P1 | O | – |
| 18 | Plan onayı ve kapanış istisnasını onay kutusuna bağla | Onaylayan işi görmeli | P1 | O | Work sözleşmesi |
| 19 | Vardiya, operatör, duruş ve süre kaydı; makine verimi | Gerçek kapasite ve OEE | P2 | B | 14 |
| 20 | Kesim/bant reçetesi kütüphanesi, ürün reçetesi | Tekrarlayan planlar | P2 | O | – |
| 21 | Malzeme ihtiyaç planlaması ve sonlu kapasite çizelgesi, Gantt | Master §7.7, §28.11 | P2 | B | 13, 19 |
| 22 | Kalıp/takım ve ayar süresi; bakım takvimi bağı | Boru hattı termini | P2 | B | 21 |
| 23 | Dönüşüm maliyeti (işçilik, enerji, genel gider) | Gerçek mamul maliyeti | P2 | B | `OPEN-008` |
| 24 | Çok geçişli soğuk hadde; BAL-005 dağıtımı; yeniden işleme sınıfı | Ertelenen formül kapsamı | P2 | O | Karar |

Belge notu (iş değil, düzeltme): `ACIK_KARARLAR.md` üretim satırları ve modül `README.md` başlığındaki eski
dilim notları bugünkü kodun gerisindedir.

## 9. Doğrulanamayanlar

1. **Bantlı dilme planının ekrandan onaylanamaması** kod okumasına dayanır (`SlittingApprovalGate` net eni
   yalnız plandan alır; ekran göndermez). Çalıştırıp görmedim; koordinatörün bir denemeyle teyit etmesi iyi olur.
2. **Müşteri malı (gelen fason) planının ekrandan açılamaması:** oluşturma ekranı müşteri bağlamı göndermiyor;
   sunucunun bu bağlamı etiketten kendiliğinden türetip türetmediğini sonuna kadar izlemedim.
3. Testlerin bugünkü sonucu ve CI durumu (koşmadım). W8 devir notundaki "NOT_RUN" kayıtlarının hâlâ geçerli
   olup olmadığını bilmiyorum.
4. Muhasebe'de üretim maliyet fişinin tam akışı (yalnız maliyet farkı önerisini gördüm).
5. Fire cinsi listesinin Ayarlar'da olup olmadığı (URT-D8); CUT-007/008 formüllerinin kodda olup olmadığı.
6. Test ortamındaki şirket kimliklerinin başlangıç parametre kataloğundaki GDC/GSP/GIN kimlikleriyle eşleşmesi.
7. Rolü olan bir kullanıcıya ekrandan izin atanabiliyor mu (Kimlik modülü incelemesinin konusu).
8. Stok tarafında çıktı etiketinin basımı ve barkodu (Stok modülü incelemesinin konusu); üretim klasöründe yok.
9. Hurda fiyatının Satış fiyat akışıyla ilişkisi (URT-D57) kodda nasıl karşılandı.
