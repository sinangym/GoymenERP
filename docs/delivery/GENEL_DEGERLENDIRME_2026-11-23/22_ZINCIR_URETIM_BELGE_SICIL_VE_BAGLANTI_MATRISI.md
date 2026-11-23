# Uçtan uca zincirler (üretim, belge, sicil) ve modüller arası bağlantı matrisi — durum ve yapılacaklar

> Kapsam: master rapor §8.3 (hammaddeden izlenebilir ürüne), §8.4 (belgeden kayda), §8.5 (sicil değişikliğinden
> cari güncellemeye) ve bütün sistem için modüller arası bağlantı envanteri. HEAD `ddc6cba`, 23 Kasım 2026.
> Yalnız kod okunarak hazırlandı; derleme ve test koşulmadı. Bu belge karar veya onay değildir.
>
> Yol kısaltmaları: `Api/` = `src/backend/GoymenERP.Api/`, `Mod/X/` = `src/backend/Modules/X/GoymenERP.Modules.X.`,
> `web/` = `src/frontend/goymen-erp-web/src/`.
>
> Seviye ölçüsü: 0 = yok, 1 = kod yazılmış ama bu çalışmada doğrulanmamış, 1-kısmi = akışın bir bölümü var.
> Geçiş türü: (a) otomatik — ortak işlem ya da olay; (b) okuma bağlantısı + kullanıcının ayrı işlemi;
> (c) elle yeniden giriş; (d) yok.

## 1. Kısa sonuç

**A — Hammaddeden izlenebilir ürüne.** Zincirin üretim çekirdeği kodda baştan sona yazılmış: stoktan rulo
seçip plan hazırlama, onayda girdinin stokta ayrılması, emir açma, girdi çıkışı, çıktı/fire/ikinci kalite
bildirimi, çıktı etiketi ve soy ağacı, emir kapanışı ve maliyet. Üretim ile Stok arasındaki her adım tek
veritabanı işleminde birlikte yazılıyor; çift hareket riski düşük. Ekranları da var (`/uretim/...`, beş rota).
Zincirin iki ucu ise kullanıcının ayrı işlemine bağlı: üretilen etiket bağlı satış siparişine kendiliğinden
ayrılmıyor, yükleme ayrı talimatla başlıyor. "Kalite serbest bırakma" halkası 1 Kasım 2026 kararıyla (URT-D14)
kapsamdan çıkarıldı; buna karşın çıktı ekranı hâlâ "Bloklu" seçtiriyor ve bloğu kaldıran hiçbir komut yok.
Bugün kullanımı engelleyen iki şey üretime özgü değil: üretim, stok ve lojistik izinleri hiçbir başlangıç
rolünde yok ve olay dağıtıcısı kapalı olduğundan üretim maliyet farkı muhasebeye ulaşmıyor.

**B — Belgeden kayda.** Zincirin yalnız ilk ve son halkaları var: dosya yükleme, içerik özeti (hash), aynı
içerik uyarısı, zararlı yazılım taraması, belgenin bir kayda bağlanması ve denetim izi. E-posta ve taramadan
alma, OCR, sınıflandırma ve yapay zekâ taslağı yok. Belgeden kayıt üreten tek gerçek yol e-fatura XML'inden
alış faturası içe aktarmadır; bu yol kuralla çalışır, yapay zekâ kullanmaz.

**C — Sicil değişikliğinden cari güncellemeye.** Zincirin baş tarafı (kaynak tarama, eşleştirme, fark, yapay
zekâ açıklaması) hiç yok. Son tarafı elle kullanılabilir: unvan değişikliği talep/karar akışı, vergi kimliği ve
ticari ad değişikliği, kimlik tarihçesi ve devir (halefiyet) kaydı kodda ve Cari ekranlarında var. Değişiklikten
sonra bağlı risk ya da belge görevi üreten bir bağlantı yok.

**D — Bağlantı envanteri.** Mimari kural korunuyor: hiçbir modül başka modülün veritabanı bağlamını
kullanmıyor, başka şemaya ham SQL yazan kod bulunmadı, izin listesindeki 18 kenar gerçek proje bağımlılıklarıyla
bire bir aynı. Modüller birbirine üç yolla bağlı: uygulama katmanı sözleşmeleri (izin listesi bunları
denetlemiyor), API katmanındaki bağdaştırıcılar ve olay kutusu (outbox). Olay tarafı bugün çalışmıyor:
dağıtıcı ayarı yok, varsayılan kapalı. Bu yüzden 7 olay kaynağı ve 13 tüketici kayıtlı olduğu hâlde hiçbir
olay karşı tarafa ulaşmıyor. Ayrıca 5 modülün yazdığı 13 olay türünün yayımlayıcısı, 1 yayımlanan olayın da tüketicisi
yok.

## 2. A zinciri — hammaddeden izlenebilir ürüne

### 2.1 Halka tablosu

| # | Halka | Modül | Komut / uç | Ekran | Sonraki halkaya geçiş | Taşınan veri | Seviye | Kanıt |
|---|---|---|---|---|---|---|---|---|
| 1 | Stok / etiket | Stok | `GET /api/v1/inventory/stock-positions`, etiket ayrıntısı, ana rulo adayları | `/stok/pozisyonlar`, `/stok/etiket/…`; satırda "Üretim planla" eylemi | (b) plan ekranı satır bağlamıyla açılır | etiket kimliği, ürün, kg, sahip, konum | 1 | `Api/Endpoints/InventoryEndpoints.cs` (634, 725); `web/inventory/stockRowActions.ts:97` |
| 2 | Plan hazırlama ve önizleme | Üretim | `POST /plans`, `/plans/preview`, `/plans/{id}/revision` | `/uretim/plan/yeni`, `/uretim/plan/:planId` | (b) onay ayrı kullanıcı işlemi; hazırlayan onaylayamaz | girdi ruloları, planlanan kg, proses değerleri, termin | 1 | `Api/Endpoints/ManufacturingEndpoints.cs` (395, 418, 587); `web/manufacturing/PlanCreatePage.tsx` |
| 3 | Üretim girdi tahsisi | Üretim → Stok | `POST /plans/{id}/approval` | Plan ayrıntısı; eksik kalan için `PlanAllocationShortfallPanel` | (a) ortak işlem: onay ve stok ayırma birlikte yazılır | etiket sürümü, ayrılan kg, tahsis kimliği | 1 | `Api/ManufacturingRuntime/ManufacturingPlanAllocationHost.cs`; `Mod/Inventory/Infrastructure/Persistence/InventoryManufacturingRealizationParticipant.PlanAllocations.cs` |
| 4 | Kuyruk ve öncelik | Üretim | `GET/POST /plans/{id}/work-center-queue[/reorder]`, `/plans/{id}/priority` | `PlanQueuePanel`, `ManualQueueControls` | (b) emir açma ayrı işlem | iş merkezi sırası, öncelik | 1 | `ManufacturingEndpoints.cs` (627–673); `Mod/Manufacturing/Infrastructure/Persistence/ManufacturingStore.WorkCenterQueue.cs` |
| 5 | Emir açma | Üretim | `POST /orders` (bir plan = bir emir) | Plan ayrıntısından | (b) girdi çıkışı ayrı işlem | planın girdi ve hedef kopyası, termin | 1 | `ManufacturingEndpoints.cs:752`; `web/manufacturing/planCommandApi.ts:926` |
| 6 | Fiilî tüketim (girdi çıkışı, iade, yarı mamul ölçümü) | Üretim → Stok | `POST /orders/{id}/input-issues`, `/input-returns`, `/wip-measurements`, `/non-kg-inputs` | `/uretim/emir/:orderId` | (a) ortak işlem: üretim kaydı ve stok çıkış hareketi birlikte | etiket, tahsis kimliği, kg, hareket kimliği | 1 | `Api/ManufacturingRuntime/ManufacturingInventoryInputIssueHost.cs`; `InventoryManufacturingRealizationParticipant.cs` (101, 153) |
| 7 | Çıktı / fire / ikinci kalite | Üretim → Stok | `POST /orders/{id}/outputs`, `/process-losses` | Emir ayrıntısı (çıktı formu) | (a) ortak işlem: çıktı kaydı, yeni parti, yeni etiket ve stok giriş hareketi birlikte | çıktı sınıfı, ürün, kg, adet, konum, blok durumu | 1 | `Api/ManufacturingRuntime/ManufacturingInventoryReturnOutputHost.cs:473`; `InventoryManufacturingRealizationParticipant.cs:319` |
| 8 | Etiketler | Stok | çıktıyla birlikte oluşur; `POST /inventory/labels/{id}/pdf…` | Etiket ayrıntısı, `LabelPrintPanel` | (b) baskı ayrı işlem | etiket kodu, parti, döküm/rulo numarası | 1 | `Mod/Manufacturing/Infrastructure/Persistence/ManufacturingStore.OutputLabelCodes.cs`; `Api/Endpoints/InventoryEndpoints.LabelPdf.cs` |
| 9 | Soy ağacı | Stok + Üretim | çıktıyla birlikte yazılır; `GET /orders/{id}/lineage`, `GET /inventory/labels/{id}/trace` | `OrderLineagePanel`, `InventoryLabelTracePage` | (a) ortak işlem (yazma); okuma bağlantı ile | girdi etiketi → çıktı etiketi, tüketilen kg, pay | 1 | `Mod/Manufacturing/Infrastructure/Persistence/ManufacturingStore.Lineage.cs`; `Mod/Inventory/Infrastructure/Persistence/InventoryStore.Genealogy.cs`; `Api/Endpoints/InventoryLabelTraceHostSources.cs` |
| 10 | Kütle dengesi ve kapanış | Üretim (+ Ayarlar, Stok) | `GET /orders/{id}/mass-balance`, `POST /orders/{id}/close` | Emir ayrıntısı | (a) kapanış ve maliyet üretimi aynı işlemde | rulo başına denge, tolerans kaynağı, kapanış kararı | 1 | `Mod/Manufacturing/Domain/Engines/Balance/MassBalanceEngineV1.cs`; `Api/ManufacturingRuntime/ManufacturingCostGenerationHost.cs` |
| 11 | Kalite serbest bırakma | — | yok | yok | (d) | — | 0 (kapsam dışı) | URT-D14 kararı; bkz. 2.2-a |
| 12 | Satış tahsisi | Stok ↔ Satış | `POST /inventory/physical-allocations` (+ `/release`, `/change`) | Stok satırında "Satışa tahsis et", `LabelAllocationPanel` | (b) kullanıcı çıktı etiketini siparişe elle ayırır | etiket, sipariş satırı, kg | 1 | `Api/Endpoints/InventoryEndpoints.cs` (1184–1348); `web/inventory/LabelAllocationPanel.tsx` |
| 13 | Yükleme talimatı | Lojistik ← Satış | `POST /api/v1/logistics/instructions`, `/issue`, `/lines/{id}/loads`, `/weighings` | `/lojistik/yukleme-talimatlari[/yeni]` | (b) talimat onaylı sipariş satırından elle açılır | sipariş satırı, birim, miktar | 1 | `Api/Endpoints/LogisticsEndpoints.cs` (41–213); `web/logistics/InstructionCreatePage.tsx` |
| 14 | Sevk (stok çıkışı) | Lojistik → Stok | `POST …/lines/{lineId}/shipments` | `RealizationCommands` | (a) ortak işlem: sevk ve stok çıkış hareketi birlikte; sonrası olayla Satış'a (dağıtıcı kapalı) | tahsis kimliği, etiket sürümü, kg | 1 | `Api/LogisticsRuntime/LogisticsRealizationTransactionRunner.cs`; `Mod/Inventory/Infrastructure/Persistence/InventoryDefiniteShipmentParticipant.cs` |
| 15 | Üretim maliyeti → stok maliyeti | Üretim → Stok | kapanışta otomatik; sonradan `POST /orders/{id}/cost-recognition` | `OrderCostPanel`, `CostGenerationPanel`, `OrderCostTracePanel` | (a) ortak işlem | girdi maliyeti, hurda payı, çıktı birim maliyeti | 1 | `ManufacturingCostGenerationHost.cs`; `Mod/Inventory/Infrastructure/Persistence/InventoryManufacturingCostGenerationParticipant.cs` |
| 16 | Maliyet farkı → muhasebe | Stok → Muhasebe | olay `inventory.manufacturing-cost-difference.proposed.v1`; `POST /manufacturing-cost-differences/preparation`, `/approval` | Muhasebe menüsünde hazırlama ve onay girişleri | (a) olay — **bugün ulaşmıyor** (dağıtıcı kapalı) | TL fark tutarı, kaynak hareket | 1-kısmi | `Api/Shell/TrainCompletionHostComposition.cs` (79–98); `Api/Endpoints/AccountingEndpoints.CostDifferences.cs` |

Test kaynağı var: `tests/GoymenERP.Manufacturing.UnitTests` (91 dosya), gerçek SQL kabul testleri
`tools/relational_inventory/manufacturing.txt` (197 satır), `inventory.txt` (367), `logistics.txt` (88),
tarayıcı testi `tests/e2e/manufacturing-mfg`, ön yüzde `web/manufacturing/*.test.ts(x)`. Sonuçları bu çalışmada
koşulmadı.

### 2.2 Sorunlu geçişler

**a) Kalite serbest bırakma halkası yok; "Bloklu" çıktı kilitli kalıyor.** Karar açık: üretimde kalite
karantinası uygulanmaz, yalnız birinci/ikinci kalite ayrımı kalır (URT-D14,
`docs/decisions/SINAN_KARARLARI_2026-11-01_URETIM.md:45`). Koddaki durum bununla tam örtüşmüyor: çıktı formu
operatöre "Serbest" ya da "Bloklu" seçtiriyor (`web/manufacturing/OrderDetailPage.tsx:1640`) ve sunucu ikisini
de kabul ediyor (`Mod/Manufacturing/Application/ManufacturingApiV1.cs:1853`). Stok alan adında blok koyma ve
kaldırma hareket türleri tanımlı (`Mod/Inventory/Domain/Movements/StockMovement.cs`: `Block`, `Release`), ancak
bu türde hareket üreten bir komut ya da uç yok (`new StockMovement(` çağrıları yalnız giriş, çıkış, iade, sayım
ve ters kayıt türlerini üretiyor; `Unblock`, `ReleaseBlock` araması 0 sonuç). Satışa ayırma yalnız serbest
miktardan yapılabildiği için "Bloklu" seçilen çıktı satılamaz ve sevk edilemez; tek çıkış yolu çıktıyı düzeltip
yeniden bildirmek.

**b) Üretilen mal bağlı siparişe kendiliğinden ayrılmıyor.** Plan bir satış siparişi satırına bağlanabiliyor
(`POST /plans/{id}/sales-order-link`) ve Satış bu bağı "bağlı tedarik" olarak okuyabiliyor
(`Api/Endpoints/ManufacturingSalesLinkedSupplyHostPorts.cs`). Çıktı bildirimi ise bu bağı kullanmıyor: çıktı
sunucusunda sipariş kimliği geçmiyor (`ManufacturingInventoryReturnOutputHost.cs` içinde `SalesOrder` araması 0
sonuç). Kullanıcı her çıktı etiketini stok ekranından siparişe elle ayırmak zorunda.

**c) Plan–sipariş bağının ekranı yok.** `sales-order-link` ucu var, ön yüzde çağıran kod yok
(`web/manufacturing` içinde `sales-order-link` araması 0 sonuç). Müşteri malı (fason) planı bu bağ olmadan
ilerleyemediği için fason üretim ekrandan tamamlanamaz (bkz. 2.7).

**d) Çıktıda döküm ve rulo numarası elle yeniden giriliyor.** Çıktı formu döküm numarası, rulo numarası ve
gerekli belge listesini operatörden istiyor (`OrderDetailPage.tsx` 1334–1336, 1613). Girdi etiketindeki değer
kendiliğinden taşınmıyor; bağ soy ağacı üzerinden kuruluyor. Yanlış yazım izlenebilirliği bozmaz ama etiket
üzerindeki bilgiyi bozar.

**e) Maliyet farkı muhasebeye ulaşmıyor.** Üretim maliyeti stok maliyetine aynı işlemde işleniyor (halka 15).
Sonradan kesinleşen fark için Stok bir olay yazıyor, Muhasebe bunu öneriye çeviriyor; ancak dağıtıcı kapalı
(bkz. 5.3). Ayrıca üretim tüketimi ve çıktısı için ayrı bir muhasebe fişi türü yok: Muhasebe'nin stok maliyeti
türleri yalnız "faturalanmış satış sevkiyatı" ve "alış faturası farkı"
(`Mod/Accounting/Application/AccountingInventoryCostV1.cs:7`). Bunun bilinçli bir tasarım mı olduğu
doğrulanamadı (bkz. 8).

**f) Sıra farkı.** Master zinciri "girdi tahsisi → plan" der; kodda önce plan hazırlanır, tahsis plan onayında
yapılır. Sonuç aynı; onaydan önce rulo başkasına ayrılabilir ve onay bu durumda reddeder.

### 2.3 Kütle dengesi ve tolerans kontrolünün yeri

- Denge her girdi rulosu için ayrı hesaplanır: `çıkan − iade = iyi ürün + ikinci kalite + kullanılabilir artık +
  hurda + proses kaybı + yarı mamul + açıklanamayan fark`. Rulolar birbirini dengelemez; açıklanamayan fark
  hiçbir sınıfa kendiliğinden eklenmez (`Mod/Manufacturing/Domain/Orders/MassBalance.cs`,
  `…/Engines/Balance/MassBalanceEngineV1.cs`).
- Kontrol tek yerde, emir kapanışında yapılır (`POST /orders/{id}/close`). Tolerans Ayarlar modülünden okunur:
  önce iş merkezi sürümü, sonra proses sürümü, sonra onaylı başlangıç değerleri (fire %5, kütle dengesi ±%2).
  İki sınırın içindeyse emir kapanır; dışındaysa neden ve ayrı bir onay izni
  (`manufacturing.orders.close-exception.approve`) gerekir. Kapanış kararı ve kullanılan tolerans kaynağı
  saklanır (`Mod/Manufacturing/Domain/Orders/OrderClosureAssessment.cs`).
- Boya gibi ana rulosu olmayan proseste ölçüm emir toplamı üzerinden yapılır (URT-D68/D69).
- Ekran: `GET /orders/{id}/mass-balance` emir ayrıntısında gösterilir; plan aşamasında ayrıca düğmesiz anlık
  önizleme vardır (`/plans/preview`, `web/manufacturing/PlanPreviewPanel.tsx`).
- Stok tarafı kendi sınırını bağımsız tutar: girdi çıkışı tahsisi ve fiziksel serbest miktarı aşamaz, çıktı
  yalnız o emre çıkışı yapılmış ruloya bağlanabilir (`InventoryManufacturingRealizationParticipant.cs` 328–349).

### 2.4 İzlenebilirlik

**Geriye doğru (sevkiyattan hammaddeye ve tedarikçiye): kısmen.** Sevk kaydı etiketi ve tahsisi taşır
(`RecordShipmentRequest`, `InventoryDefiniteShipmentRequestV1`). Etiket izleme okuması o etiketten üretim
girdilerine, oradan mal kabul hareketine ve tedarikçiye yürür; tedarikçinin kendi etiket numarasıyla arama da
vardır (`Mod/Inventory/Application/InventoryLabelTraceContractsV1.cs`, `web/inventory/InventoryLabelTracePage.tsx`).
Üç sınır var:
1. Tedarikçi adı yalnız onaylı (açık) alış siparişi için cevaplanıyor; kapanmış siparişin tedarikçisi
   "bilinmiyor" kalıyor (`Api/Endpoints/InventoryLabelTraceHostSources.cs` 74–78; kod bu eksiği kendi notunda
   söylüyor).
2. İzleme etiketten başlar; irsaliye ya da fatura numarasından başlayan bir izleme ekranı yok.
3. Okuma bir yürüyüşte sınırlı sayıda etiket döndürür; uzun zincirde "pencere doldu" işareti verir.

**İleriye doğru (hammadde rulosundan müşterilere): kısmen.** Aynı izleme okuması ileri yönde üretim çıktılarını
ve müşteri sevkiyatlarını, sevkiyatın müşterisiyle birlikte döndürür (müşteri Lojistik'ten okunur). Sınırlar
aynıdır; bir döküm numarasına ait bütün ruloları toplu izleyen (geri çağırma listesi) bir rapor yok.

Döküm/rulo numarası parti kaydında tutulur; malzeme sertifikası (MTC) alanı bilinçli olarak yok
(`Mod/Inventory/Domain/Lots/MaterialLot.cs:12`). "Lot/rulo/ısı/MTC bağı girişten çıkışa izlenir" senaryosunun
MTC kısmı bu yüzden karşılanmıyor.

### 2.5 Ters akışlar

| Ters akış | Uç | Ekran | Yapılan | Kanıt |
|---|---|---|---|---|
| Kullanılmayan girdinin iadesi | `POST /orders/{id}/input-returns` | Emir ayrıntısı | Stokta iade hareketi; tüketilmiş miktarı aşamaz | `ManufacturingInventoryReturnOutputHost.cs:398` |
| Çıktı düzeltme | `POST /orders/{id}/facts/{factId}/correction` | `OutputCorrectionSection` | Bildirim silinmez; ters kayıt eklenir, stokta "üretim çıktısı ters kaydı" hareketi | `InventoryManufacturingRealizationParticipant.OutputReversal.cs` |
| Emir iptali | `POST /orders/{id}/cancellation` | `OrderCancelSection` | Yalnız net etkisi sıfırlanmış emir iptal olur; planın tahsisi bırakılır | `Mod/Manufacturing/Application/ManufacturingOrderCancellationV1.cs` |
| Planın yeniden onayı | `POST /plans/{id}/reapproval` | `PlanReapprovalSection` | İptal sonrası plan yeniden tahsis edilir | `ManufacturingEndpoints.cs:471` |
| Kapanmış emri yeniden açma | `POST /orders/{id}/reopening` | `OrderReopenSection` | Muhasebe dönemi açık mı diye sorar; maliyet üretimi yeni kuşakla yenilenir | `Api/ManufacturingRuntime/ManufacturingReopeningAccountingPeriodStatusPortV1.cs` |
| Plan reddi / iptali | `POST /plans/{id}/rejection`, `/cancellation` | Plan ayrıntısı | Plan taslağa döner ya da iptal olur; silinmez | `ManufacturingEndpoints.cs` (496, 515) |
| Hurda fiyatı düzeltme | `POST /scrap-prices/{yıl}/{ay}/corrections` | `/uretim/hurda-fiyatlari` | Eski sürüm kalır, yeni sürüm eklenir | `ManufacturingEndpoints.cs:1146` |

Hepsi "silme yok, ters kayıt" ilkesine uyuyor. Eksik olan: iptal ve yeniden açma izinleri
(`manufacturing.orders.cancel`, `manufacturing.plans.cancel`) hiçbir role verilmemiş.

### 2.6 Çift hareket riski

Düşük. Üretim kendi kaydını, Stok kendi hareketini yazar; ikisi aynı veritabanı işleminde birlikte işlenir ya
da birlikte geri alınır (üretim sunucuları `manufacturing.Database.BeginTransactionAsync` ile başlar ve Stok
katılımcısına aynı bağlantıyı verir). Hareket kimliğini istemci üretir, tekrar gönderim aynı cevabı döndürür,
aynı kimlikle ikinci hareket reddedilir. Üretim, Stok tablolarına yazmaz; yalnız `Stok` sözleşmesini çağırır.
İki dikkat noktası:
- Aynı fiziksel sevk için iki ayrı olay yazılıyor: Lojistik `logistics.definite-shipment.recorded.v1` ve Stok
  `inventory.definite-shipment.recorded.v1`. Stok'unki yayımlanmıyor; ileride ikisi de tüketilirse çift etki
  doğabilir.
- Olay teslimi "en az bir kez"dir; tüketicinin kendi tekrar korumasına dayanır (`Mod/Work/Infrastructure/Outbox/`).

### 2.7 Fason üretim, şirketler arası üretim ve transfer

- **Gelen fason (müşteri malı): 1-kısmi.** Stok, mal sahibini ve malı elinde tutanı ayrı tutar
  (`Mod/Inventory/Domain/Stock/StockPosition.cs`). Müşteri malı içeren plan yalnız o müşterinin fason hizmet
  siparişine bağlanabilir; Satış sorgulanır (`Api/Endpoints/ManufacturingSubcontractOrderHostPorts.cs`). Mamulün
  müşteriye teslimi Lojistik'te ayrı akıştır (`/lojistik/fason-teslimler`,
  `Api/LogisticsRuntime/LogisticsSubcontractDeliveryHost*.cs`), kullanılmayan ayırmanın bırakılması Stok'tadır
  (`/stok/fason-ayirma-birak`), hizmet faturası Satış'tadır (`Api/Endpoints/SalesEndpoints.SubcontractServiceInvoices.cs`).
  İki kopukluk: (1) müşteri malını stoka alan bir günlük kabul komutu bulunamadı — karşı taraf sahipli stok yalnız
  açılış sayımında oluşturuluyor (`Mod/Inventory/Infrastructure/InventoryOpeningCommandService.cs:455`; diğer
  `new StockOwner(` çağrıları mevcut sahibi kopyalıyor); (2) plan–fason siparişi bağının ekranı yok (2.2-c).
- **Giden fason (bizim malın fasoncuda işlenmesi): 0/1-kısmi.** Satın Alma'da "hizmet ve fason alışları" var,
  ancak kendi sözleşmesi stok hareketi üretmediğini söylüyor
  (`Mod/Purchasing/Application/Contracts/PurchaseServiceOrder*.cs`: "No movement or goods receipt is
  manufactured"). Malın fasoncuya gidişi, orada dönüşmesi ve geri gelişi için bağlı bir zincir görülmedi.
- **Şirketler arası üretim / grup içi işlem: 0.** `InterCompany`, `Intercompany`, `GroupCompany`, "şirketler
  arası", "grup içi" aramaları arka uçta 0 sonuç. GDC'nin GSP için ürettiği durum bugün yalnız "müşteri malı
  fason" ya da ayrı satış–alış belgeleriyle temsil edilebilir; iki şirketin kayıtlarını birbirine bağlayan
  bir mekanizma yok.
- **Depolar arası transfer: 1.** Planlama, Lojistik çıkış belgesi, çıkış, kabul, kantar kontrolü, fazla ve kayıp
  onayları, ters kayıt uçları ve ekranları var (`Api/Endpoints/InventoryEndpoints.Transfers.cs`,
  `LogisticsTransferDepartureEndpoints.cs`, `web/inventory/InventoryTransferPage.tsx`,
  `web/logistics/TransferDeparturePage.tsx`). Çıkış, Lojistik'in belgesi geçerliyse yapılır (belge kanıtı
  bağlantısı). Transfer aynı şirketin depoları içindir.

## 3. B zinciri — belgeden kayda

| Halka | Durum | Kanıt / ne arandı |
|---|---|---|
| Dosya yükleme | Var (1) | `POST /api/v1/documents`, sürüm ekleme, içerik indirme: `Api/Endpoints/DocumentEndpoints.cs`; ekran parçası `web/documents/DocumentsPanel.tsx` |
| E-posta ve tarayıcıdan alma | Yok (0) | `Imap`, `Pop3`, `MailKit`, `InboundEmail`, `Smtp` aramaları 0 sonuç |
| İçerik özeti (hash) ve mükerrer uyarısı | Var (1) | SHA-256, aynı içerik uyarıdır, ret değildir: `Mod/Documents/Application/DocumentService.cs` (27, 586) |
| Zararlı yazılım taraması | Var, ortama bağlı | Geliştirmede sahte tarayıcı (`DevelopmentSignatureScanner`); diğer ortamda Windows Defender ayarlanmamışsa yükleme ve indirme 503 verir (`Api/appsettings.json` → `Documents:Scanner:Mode` boş) |
| OCR | Yok (0) | `Ocr`, `OCR`, `Tesseract`, `TextExtraction` aramaları 0 sonuç |
| Sınıflandırma | Yok (0) | `DocumentClassif…`, `ClassifyDocument` aramaları 0 sonuç; belge türü kullanıcı seçimidir |
| Yapay zekâ taslağı | Yok (0) | `Ai:Enabled=false`, `AllowedTools` boş, sağlayıcı sahte (`Api/Ai/AiModelProvider.cs`: `FakeAiModelProvider`); AI yalnız okuma çerçevesi |
| Ana veri kontrolü + kullanıcı doğrulaması + ERP kaydı | Yalnız e-fatura XML alış faturası (1-kısmi) | `Api/Endpoints/PurchasingEndpoints.InvoiceImports.cs`: tedarikçi çözme, ürün adayları, önizleme, kayıt; Finans'ta resmî fatura içe aktarma onayı (`finance.official-invoice-import.*`) |
| Belge referansı | Var (1) | Belge 7 modülün kaydına bağlanabiliyor: `Api/DocumentsRuntime/*DocumentOwnerLinkPort*.cs` (Cari, Finans, Stok, Lojistik, Üretim, Satın Alma, Satış) |
| Onay / denetim izi | Var (1) | Yükleme, sürüm ve bağ aynı işlemde denetim kaydıyla yazılır (izin listesi: Documents → Audit) |

Menüde bağımsız bir "Belgeler" girişi yok; yalnız şablon ve antet ayar girişleri var
(`web/shell/navigation.ts` 360–366). Belge işlemi ilgili kaydın ekranındaki panelden yapılır. Belge izinleri
(10 ad) hiçbir başlangıç rolünde yok.

## 4. C zinciri — sicil değişikliğinden cari güncellemeye

| Halka | Durum | Kanıt / ne arandı |
|---|---|---|
| Kaynak tarama (ticaret sicili, GİB, MERSİS) | Yok (0) | `Mersis`, `Gazette`, `TradeRegistry`, `VknLookup` aramaları 0 sonuç; dış kaynak bağlantısı yok |
| Taraf eşleştirme | Yok (0); elle mükerrer kontrolü var | `POST /{partyId}/tax-identity/duplicates`: `Api/Endpoints/CounterpartyEndpoints.TaxIdentity.cs:71` |
| Eski/yeni fark ve yapay zekâ açıklaması | Yok (0) | AI kapalı (bkz. B) |
| Veri sorumlusu incelemesi ve onay | Var (1), yalnız unvan için talep/karar | `…/legal-name/change-requests`, `…/change-decisions`: `Api/Endpoints/CounterpartyEndpoints.LegalName.cs` (62, 71) |
| Tarihçeli ana veri değişikliği | Var (1) | Vergi kimliği, ticari ad, kimlik tarihçesi, devir kaydı: `CounterpartyEndpoints.TaxIdentity.cs`, `.TradeName.cs`, `.IdentityHistory.cs`; ekran `web/cari/CariLegalNamePanel.tsx`, `CariTaxIdentityPanel.tsx`, `CariTradeNamePanel.tsx`, `CariIdentityHistoryPanel.tsx` |
| Bağlı risk / belge görevi | Yok (0) | Cari pasifleştirme ve birleştirme olayları yazılıyor ama yayımlayıcısı ve tüketicisi yok (bkz. 5.3) |

Sonuç: kullanıcı sicil değişikliğini kendisi fark edip Cari ekranından işlerse tarihçe doğru tutulur; sistemin
kendi fark edip görev açması yoktur.

## 5. D — modüller arası bağlantı envanteri

### 5.1 İzin listesi ile gerçek bağımlılıkların karşılaştırması

- `docs/architecture/MODULE_DEPENDENCY_ALLOWLIST.json` 18 kenar içerir. Kural testi
  (`tests/GoymenERP.ArchitectureTests/ModuleDependencyGuardTests.cs`) yalnız başka modülün **Domain** ve
  **Infrastructure** projesine verilen doğrudan referansı denetler. Bu kapsamda gerçek durum listeyle bire bir
  aynıdır: 13 modül → `Audit.Infrastructure`, 4 modül (Organizasyon, Cari, Ayarlar, Kimlik) →
  `Idempotency.Infrastructure`, Ürün → `Idempotency.Domain`. Hepsi "iş kaydı ile denetim kaydı aynı işlemde"
  gerekçelidir.
- Kuralın **görmediği** 23 kenar daha var; bunlar başka modülün Application ya da Contracts projesine (yalnız
  sözleşme) referanstır: Üretim → Stok, Muhasebe, Cari, Organizasyon, Ürün, Ayarlar; Lojistik → Satış, Satın
  Alma, Ayarlar; Stok → Satış, Ayarlar; Finans → Satış, Ayarlar; Muhasebe → Finans, Ayarlar; Satış → Ürün,
  Ayarlar; Satın Alma → Ayarlar; Ürün → Ayarlar; Cari → Kimlik, Organizasyon, Ayarlar; Kimlik → Denetim
  (kaynak: her `.csproj` dosyasındaki `ProjectReference`).
- **Kural açığı (iki yer):** dolaylı referans üzerinden başka modülün Domain türleri kullanılıyor ve test bunu
  yakalamıyor: `Mod/Finance/Infrastructure/FinanceSalesOfficialInvoiceReadPortV1.cs:4`
  (`using …Sales.Domain.Invoices`) ve `Mod/Manufacturing/Application/ManufacturingApiV1.cs`,
  `ManufacturingPlanLifecycleV1.cs` (`…Inventory.Domain`). Tabloya yazma değil, tür kullanımıdır; yine de
  "modül sınırı sözleşmede korunur" ilkesine aykırıdır.

### 5.2 Bağımlılık matrisi (okuyan/çağıran → sahip)

Mekanizma: **P** = uygulama sözleşmesine doğrudan proje referansı; **B** = API katmanındaki bağdaştırıcı
(modüller birbirini görmez, `Api/…Runtime/` ve `Api/Endpoints/*Host*.cs` bağlar); **Oİ** = ortak işlem (iki
modül aynı veritabanı işleminde, her biri kendi tablosuna yazar); **O** = olay kutusu. Yön her satırda
"okuyan ← sahip" şeklindedir; Oİ'de çağıran, sahibin katılımcısını çağırır.

| Okuyan / çağıran | Sahip | Mekanizma | Ne için | Kanıt |
|---|---|---|---|---|
| Üretim | Stok | P + B + **Oİ** | Plan tahsisi, girdi çıkışı, iade, çıktı, düzeltme, maliyet üretimi; rulo ve maliyet okumaları | `Api/ManufacturingRuntime/*Host.cs` |
| Üretim | Satış | B | Onaylı sipariş satırı, fason hizmet siparişi, sipariş kuru | `Api/Endpoints/ManufacturingSalesLinkedSupplyHostPorts.cs`, `ManufacturingSubcontractOrderHostPorts.cs` |
| Üretim | Muhasebe | P + B | Yeniden açmada dönem açık mı | `ManufacturingReopeningAccountingPeriodStatusPortV1.cs` |
| Üretim | Ürün, Organizasyon, Cari, Ayarlar | P + B | Teknik ölçüler, konum/iş merkezi, müşteri, tolerans ve parametre profili | `ManufacturingOutputSelectionReader.cs`, `ManufacturingSettingsScrapCostPolicyReader.cs` |
| Stok | Üretim | B | Etiket izlemede üretim soy ağacı | `Api/Endpoints/InventoryLabelTraceHostSources.cs` |
| Stok | Satın Alma | B + **O** | Gelecek tedarik, eksik uyarısı, mal kabul maliyeti, izlemede tedarikçi; alış faturası kesinleşti olayı | `Api/InventoryRuntime/PurchasingOwnerInventoryReceiptCostSource.cs`, `TrainCompletionHostComposition.cs:95` |
| Stok | Satış | P | Gelecek talep ve eksik talep kaynağı | `Mod/Inventory/Infrastructure/Persistence/InventoryFutureDemandSource.cs` |
| Stok | Lojistik | B | Transfer çıkış belgesi kanıtı, izlemede müşteri | `Api/LogisticsRuntime/InventoryTransferOutgoingDocumentProofAdapter.cs` |
| Stok | Ürün, Organizasyon, Cari, Ayarlar | B | Ürün, kg/birim, konum, mal sahibi adı, birim ve transfer ayarı | `Api/Endpoints/Inventory*Host*.cs` |
| Lojistik | Stok | B + **Oİ** | Sevk, müşteri iadesi, fason teslim, transfer çıkışı stok hareketi | `Api/LogisticsRuntime/LogisticsRealizationTransactionRunner.cs` |
| Lojistik | Satış, Satın Alma | P | Onaylı sipariş satırı, operasyon iş listeleri | `Mod/Logistics/Application/` |
| Lojistik | Üretim | B | Fason siparişin üretim tamamlanma bilgisi | `Api/Endpoints/ManufacturingSubcontractCompletionHostPort.cs` |
| Lojistik | Cari, Organizasyon, Belgeler, Ayarlar | B | Müşteri hesabı, transfer referansı, irsaliye belgesi, kantar ayarı | `Api/LogisticsRuntime/` |
| Satış | Lojistik | P (Lojistik uygular) + B + **O** | İrsaliye, faturalanabilir sevk, teslimat, iade; sevk olayı ile risk istisnası kullanımı | `Mod/Logistics/Infrastructure/`, `Api/SalesRuntime/SalesRiskExceptionUsageInboxHost.cs` |
| Satış | Stok | P (Stok uygular) + B | Sipariş tahsis okuması, marj maliyeti, gelecek tahsis bırakma | `Mod/Inventory/Infrastructure/Persistence/InventorySalesOrderAllocationReadPort.cs` |
| Satış | Üretim, Satın Alma | B | Siparişe bağlı tedarik (üretim planı/emri, alış siparişi) | `ManufacturingSalesLinkedSupplyHostPorts.cs`, `PurchasingSalesLinkedSupplyHostPorts.cs` |
| Satış | Finans | P (Finans uygular) + B | Risk okuması, resmî fatura okuması, faturanın Finans'a kabulü ve iptali | `Mod/Finance/Infrastructure/FinanceSalesOfficialInvoiceReadPortV1.cs` |
| Satış | Ürün, Cari, Kimlik, Ayarlar | P + B | Ürün ve fiyat, müşteri hesabı ve iletişim, kullanıcı adı, iskonto limiti | `Api/Endpoints/SalesPartyDisplayHost.cs`, `SalesUserDisplayHost.cs` |
| Satın Alma | Stok | B + **Oİ** | Mal kabul, kabul ters kaydı, tedarikçi iadesi | `Api/PurchasingRuntime/PurchaseGoodsReceiptHost.cs`, `PurchaseReceiptReversalHost*.cs`, `PurchaseSupplierReturnHost.cs` |
| Satın Alma | Finans | B | Alış faturasının Finans'a kabulü | `Api/PurchasingRuntime/PurchaseInvoiceFinanceAdmissionHost.cs` |
| Satın Alma | Satış, Ürün, Cari, Belgeler, Organizasyon, Ayarlar | B | Talebin satış kaynağı, ürün adayı, tedarikçi, e-fatura XML'i, konum, politika | `Api/PurchasingRuntime/`, `Api/Endpoints/Purchasing*Host*.cs` |
| Finans | Satış, Satın Alma | P + B | Fatura açık kalem kabul yetkisi ve iptali | `Api/Endpoints/FinanceEndpoints*.cs` bağdaştırıcıları |
| Finans | Muhasebe | B | Dönem durumu, dönem devri | `Api/FinanceRuntime/FinanceCreditAccountingPeriodPortV1.cs`, `FinancePeriodCarryForwardPortV1.cs` |
| Finans | Cari, Belgeler, Ayarlar | B | Hesap, kredi veren, kanıt belgesi, kur ve teminat politikası | `Api/FinanceRuntime/` |
| Muhasebe | Satış, Satın Alma | B + **O** | Fatura satırları; fatura kesinleşti ve iptal edildi olayları | `Api/AccountingRuntime/AccountingInvoiceJournalHost.cs`, `TrainCompletionHostComposition.cs` (101–105) |
| Muhasebe | Stok | B + **O** | Maliyet kaynağı ve soy okuması; alış farkı ve üretim maliyet farkı olayları | `Api/AccountingRuntime/AccountingInventoryCostSourceReaderV1.cs` |
| Muhasebe | Finans | P + **O** | Kredi hareketi, masraf ödemesi, kasa transferi, kasa döviz olayları | `TrainCompletionHostComposition.cs` (99, 107) |
| Muhasebe | Ürün, Ayarlar | B + P | Fatura satırı ürün sınıfı, tutar profili | `Api/AccountingRuntime/AccountingInvoiceLineCogsSource.cs` |
| Belgeler | Cari, Finans, Stok, Lojistik, Üretim, Satın Alma, Satış | B | Belgenin bağlanacağı kaydın var olduğunu ve okunabildiğini sorar | `Api/DocumentsRuntime/*DocumentOwnerLinkPort*.cs` |
| Ürün | Cari, Belgeler, Ayarlar | B + P | Fiyat listesi ve paket standardı müşterisi, yükleme sınırı, ürün türü | `Api/ProductRuntime/` |
| Cari | Kimlik, Organizasyon, Ayarlar | P | Kullanıcı, konum işleticisi, e-belge profili | `Mod/Counterparty/Infrastructure/` |
| Ayarlar | Kimlik, Organizasyon, Stok, Lojistik, Ürün, Finans | B | Kişi dizini, iş merkezi, transfer deposu, ürün türü, politika referansı | `Api/SettingsRuntime/`, `Api/Endpoints/Settings*Host.cs` |
| İş merkezi (Work) | Stok, Satın Alma, Satış, Finans, Kimlik | B + **O** | Onay kuyruğu okumaları; tahsis bırakıldı ve tedarik yeniden açıldı bildirimleri | `Api/WorkRuntime/` |
| Organizasyon ↔ Kimlik | karşılıklı | **O** | Şirket açıldı → ilk yönetici atandı | `Api/OrganizationRuntime/CompanySetupMessagingHost.cs` |
| Bütün iş modülleri | Denetim, Idempotency | P (altyapı) + **Oİ** | Denetim kaydı ve komut sonucu iş kaydıyla aynı işlemde | izin listesinin 18 kenarı |

Bu tablo `.csproj` referanslarından, `Api/` altındaki bağdaştırıcı sınıflarının uyguladığı arayüzlerden ve
olay kayıtlarından çıkarıldı; en yoğun bağlar gösterildi, tek satırlık yardımcı okumaların tamamı değildir.

### 5.3 Olaylar ve tüketiciler

**Dağıtıcı kapalı.** Olayları kaynaktan tüketiciye taşıyan arka plan işi `Work:OutboxDispatcher:Enabled` doğru
olmadıkça çalışmaz; açıldığında beş sayısal ayar da (parti boyu, deneme sayısı, bekleme süreleri) elle
verilmek zorundadır (`Mod/Work/Infrastructure/Outbox/WorkOutboxDispatcherOptions.cs`). `Api/appsettings.json`
ve `appsettings.Development.json` içinde `Work` bölümü hiç yok; `infra/` ve `.github/` altında da bu anahtar
geçmiyor. Ayrıca satış faturası muhasebe kaydı için ikinci bir kapı var:
`Accounting:InvoiceJournal:InboxAdmissionEnabled` (`Api/AccountingRuntime/AccountingInvoiceJournalHost.cs:57`),
o da ayarlanmamış. İkisi de proje kurallarında insan kapısıdır.

Kayıtlı kaynaklar ve tüketiciler (`Api/Shell/TrainCompletionHostComposition.cs`,
`Api/LogisticsRuntime/LogisticsRiskGateHost.cs`, `Api/OrganizationRuntime/CompanySetupMessagingHost.cs`,
`Api/WorkRuntime/WorkNotificationHostV1.cs`):

| Kaynak | Olay | Tüketici | Sonuç |
|---|---|---|---|
| `sales` | `sales.invoice.finalized.v1` | Muhasebe `accounting.invoice-journal`; Muhasebe `accounting.inventory-cost-proposals` | Satış fişi ve satılan malın maliyeti önerisi |
| `sales` | `sales.invoice.cancelled.v1` | Muhasebe `accounting.sales-invoice-cancellation` | İptal fişi |
| `sales` | `sales.return-invoice.finalized.v1` | **tüketici yok** | — |
| `purchasing` | `purchasing.invoice.finalized.v1` | Muhasebe `accounting.purchase-invoice-journal`; Stok `inventory.purchase-invoice-cost` | Alış fişi; stok maliyetinin faturayla güncellenmesi |
| `purchasing` | `purchasing.order-line.revived.v1` | İş merkezi bildirimi | Bildirim |
| `inventory.outbox.v1` | `inventory.purchase-invoice-cost-difference.proposed.v1` | Muhasebe `accounting.inventory-cost-proposals` | Fark önerisi |
| `inventory.outbox.v1` | `inventory.manufacturing-cost-difference.proposed.v1` | Muhasebe `accounting.manufacturing-cost-difference` | Üretim maliyet farkı önerisi |
| `inventory.outbox.v1` | `inventory.future-allocation.released.v1` | İş merkezi bildirimi | Bildirim |
| `finance.outbox.v1` | `finance.credit-movement.recorded.v1` | Muhasebe `accounting.credit-movement-proposal` | Kredi fişi önerisi |
| `finance.outbox.v1` | masraf ödemesi, kasa transferi, kasa döviz, kasa döviz defter değeri (4 olay) | Muhasebe `accounting.finance-journal-source` | Fiş kaynağı |
| `logistics` | `logistics.definite-shipment.recorded.v1`, `.cancelled.v1` | Satış `sales.risk-exception-usage` | Risk istisnası kullanımının işlenmesi |
| `organization.company-setup` | `organization.company.opened.v1` | Kimlik `identity.company-opened` | Yeni şirkete ilk yönetici |
| `identity.company-setup` | `identity.company.first_admin_assigned.v1` | Organizasyon `organization.company-first-admin` | Kurulum tamamlanması |

**Yazılan ama yayımlanmayan olaylar** (modül kendi olay tablosuna yazıyor; dağıtıcıya kayıtlı kaynak bunları
vermiyor ya da hiç kaynak yok):
- Stok: `inventory.definite-shipment.recorded.v1`, `inventory.goods-receipt.reversed.v1`,
  `inventory.supplier-return-exit.recorded.v1`, `inventory.customer-return.recorded.v1`,
  `inventory.subcontract-delivery.recorded.v1`, `inventory.future-allocation.created.v1` ve `.converted.v1`
  (kaynak yalnız üç türü süzüyor: `Mod/Inventory/Infrastructure/Persistence/InventoryOutboxEventSource.cs` 39–43).
- Cari: `counterparty.party.deactivated.v1`, `counterparty.party.merged.v1` — Cari için kayıtlı kaynak yok
  (`IWorkOutboxEventSourceV1` uygulayan sınıf listesinde Cari bulunmuyor).
- Kimlik: `identity.permission.changed.v1`, `identity.founder.bootstrapped.v1` — kaynak yalnız "ilk yönetici
  atandı" olayını veriyor.
- Satın Alma: `purchasing.order-line.closed.v1` yazılıyor; kaynak tür süzmüyor ama kayıtta yalnız iki tür
  bildirilmiş. Dağıtıcının bildirilmemiş türü nasıl ele aldığı doğrulanamadı (bkz. 8).
- İş merkezi: `work.desired-state.applied.v2` kendi tablosuna yazılıyor; dış tüketicisi görülmedi.

**Yayımlayanı olmayan tüketici:** bulunmadı; kayıtlı 13 tüketicinin her aboneliğinin karşısında kayıtlı bir
kaynak ve bildirilen olay türü var.

**Olay kutusu olmayan modüller:** Üretim, Ürün, Belgeler, Ayarlar, Denetim. Üretim bütün etkisini ortak işlemle
verir; "üretim tamamlandı" gibi bir olay yoktur, bu yüzden Satış ya da İş merkezi üretim bitişinden haberdar
olmaz.

**Kodda eskimiş açıklama:** `Api/Program.cs:497–498` satış fişi tüketicisinin "Satış kendi kaynağını
kaydedene kadar" dağıtıcıya bağlanmadığını söylüyor; kaynak `TrainCompletionHostComposition.cs:92` içinde
kayıtlı.

### 5.4 "Başka modülün tablosuna yazmama" kuralı

İhlal bulunmadı. Yapılan kontroller:
- Modül klasörlerinde başka modülün `…DbContext` türünü kullanan dosya araması: 16 modülün hepsinde 0 sonuç.
- API katmanında iki modülün veritabanı bağlamını birlikte kullanıp yazan dosya: 0 (tek çoklu kullanım şema
  uyumluluk sağlık kontrolü, salt okunur: `Api/Startup/DatabaseSchemaCompatibilityHealthCheck.cs`).
  Yazma yapan beş sunucu sınıfı yalnız kendi modülünün bağlamına yazıyor (dördü Üretim, biri Satın Alma).
- Modüllerde başka şemanın tablosuna `FROM/JOIN/INTO/UPDATE/DELETE` ile ham SQL: 0 sonuç. API katmanındaki tek
  ham SQL kilit alma komutudur (`sp_getapplock`, `Api/Endpoints/InventoryFutureAllocationHostPorts.cs`).
- Ortak işlemlerde karşı modül kendi katılımcı sınıfıyla kendi tablosuna yazar; çağıran modül yalnız bağlantıyı
  ve işlemi paylaşır.

### 5.5 Boşluklar

1. Olay dağıtıcısı kapalı; olayla kurulan bütün bağlar (fatura → muhasebe fişi, fatura → stok maliyeti, maliyet
   farkı → muhasebe, sevk → risk kullanımı, şirket açılışı → ilk yönetici, bildirimler) çalışmıyor.
2. Müşteri iade faturası olayının tüketicisi yok; iade muhasebe fişine olay yoluyla dönüşmüyor.
3. Cari pasifleştirme ve birleştirme olayları kimseye ulaşmıyor; diğer modüllerin eski cariyle açık kayıtları
   için uyarı ya da görev oluşmuyor.
4. İzin listesi testi Application referanslarını ve dolaylı Domain kullanımını denetlemiyor (5.1).
5. Üretimden çıkan bir olay yok; üretim bitişi başka modüle haber verilmiyor.
6. Şirketler arası (grup içi) bağlantı mekanizması yok.

## 6. Master §29 kabul senaryolarından bu zincirlere düşenler

"Koşulabilir" değerlendirmesi kodun varlığına göredir; test sonucu bu çalışmada alınmadı. Ortak engel (her
satır için geçerli, tekrar yazılmadı): ilgili izinler hiçbir başlangıç rolünde yok
(`Api/Seed/identity-seed.json`: 5 rol, yalnız organizasyon, kimlik ve dört cari izni).

| Senaryo | Konu | Bugün koşulabilir mi | Engel / not |
|---|---|---|---|
| AT-029 | Fiziksel mevcut ile mal sahibi ayrı | Evet (kod var) | — |
| AT-030 | Müşteri malı serbest stok gibi satılamaz | Evet (kod var) | `InventorySalesOwnershipGuardV1` |
| AT-031 | Fasoncudaki şirket malı emanet durumunda görünür | Kısmen | Mal sahibi/elinde tutan ayrımı var; giden fason zinciri yok (2.7) |
| AT-032–034 | Tahsis serbest miktarı düşürür, fazla tahsis olmaz, bırakınca geri döner | Evet (kod var) | — |
| AT-035 | Kalite bloğu serbest miktardan düşer | Kısmen | Bloklu miktar düşülüyor; bloğu koyan/kaldıran komut yok (2.2-a) |
| AT-036 | Yüklemeye tutulan miktar başka siparişe gitmez | Evet (kod var) | Sevk tahsis kimliği ve sürümüyle yapılır |
| AT-038 | Etiket bölme/birleştirme soy ağacını korur | Evet (kod var) | `LabelTransformationPanel`, `InventoryStore.Genealogy.cs` |
| AT-039 | Lot/rulo/ısı/MTC bağı girişten çıkışa izlenir | Kısmen | MTC alanı yok; çıktıda döküm numarası elle girilir (2.2-d) |
| AT-040 | Serbest miktarın her bileşeni kaynağa açılır | Evet (kod var) | Etiket ayrıntısı ve hareket zaman çizgisi |
| AT-041 | Kaynağı hazır olmayan plan hazır olamaz | Evet (kod var) | Onay tahsis edemezse reddeder |
| AT-042 | Fiilî tüketim tahsisi ve mevcudu aşamaz | Evet (kod var) | — |
| AT-043–047 | Boy kesme, dilme, boru/profil, boya, soğuk hadde dengeleri | Evet (kod var) | Motorlar `Mod/Manufacturing/Domain/Engines/`; altın örnek sonuçları koordinatörde |
| AT-048 | Tolerans dışı fark onaysız kapanamaz | Evet (kod var) | İstisna onay izni hiçbir rolde yok |
| AT-049 | Yeni tolerans/formül sürümü geçmişi değiştirmez | Evet (kod var) | Onayda parametre anlık görüntüsü saklanır |
| AT-050 | Çıktı etiketi bütün girdilere geri izlenir | Evet (kod var) | `OutputLineageEdges` |
| AT-051 | Kalite karantinasındaki çıktı sevk edilemez | Kapsam dışı | URT-D14 ile üretim kapsamından çıkarıldı |
| AT-052 | Düzeltme silmez, ters zincir kurar | Evet (kod var) | 2.5 |
| AT-054 | Yüklenen etiketler tahsis ve planla uzlaşır | Evet (kod var) | Plan üstü sevk ayrı onaya gider |
| AT-056 | OCR/AI çıkarımı izlenir | Hayır | OCR ve AI çıkarımı yok |
| AT-057 | Aynı belge özeti mükerrer uyarısı üretir | Kısmen | Özet eşleşmesi var; "benzer anahtarlar" yok |
| AT-059 | AI kalıcı değişikliği onaysız uygulamaz | Hayır (sınanacak şey yok) | AI kapalı, yazma aracı yok |
| AT-061, AT-062 | Düğmesiz önizleme; eksik girdide eski sonuç kalmaz | Evet (kod var) | `PlanPreviewPanel.tsx`, `/plans/preview` |
| AT-063 | Stok satırından Üretim Planla, Tahsis Revize, Yükleme Talimatı | Evet (kod var) | `stockRowActions.ts` |
| AT-065 | Tek belge bir kez saklanır, ekranlar aynı dosyayı gösterir | Kısmen | Bağlama var; Muhasebe için belge bağı bağdaştırıcısı görülmedi |
| AT-009, AT-012, AT-013 | Mükerrer VKN, cari birleştirme, pasif cari | Evet (kod var) | Birleştirme/pasifleştirme sonrası diğer modüllere haber gitmiyor (5.5-3) |

## 7. Yapılacaklar

Öncelik: P0 = üç firmanın test ortamında kullanmaya başlaması için şart; P1 = hedefin tamamı için gerekli;
P2 = sonra. Büyüklük: K = gün, O = 1–2 hafta, B = daha fazla.

| # | İş | Neden | Öncelik | Büyüklük | İlgili modüller |
|---|---|---|---|---|---|
| 1 | Üretim, Stok, Lojistik ve Belgeler izinlerini taşıyan iş rollerini tanımlamak ve kullanıcılara atanabilir kılmak | Bugün hiçbir rol bu zincirin tek bir adımını yapamıyor; rol atama insan kapısıdır | P0 | O | Kimlik, bütün modüller |
| 2 | Test ortamında olay dağıtıcısını açmak (beş ayar değeri ve satış fişi kabul anahtarı kararıyla) | Fatura → muhasebe, fatura → stok maliyeti, maliyet farkı, sevk → risk ve şirket kurulumu olayla bağlı; kapalıyken modüller bütün olarak çalışmıyor. Karar: `NEEDS-SINAN` (değerler) | P0 | K | İş merkezi, Muhasebe, Stok, Satış, Finans |
| 3 | Çıktı formundaki "Bloklu" seçeneğini kaldırmak ya da bloğu kaldıran bir komut ve ekran eklemek | URT-D14'e göre üretimde karantina yok; bugün "Bloklu" seçilen mal satılamaz ve çözmenin yolu yok | P0 | K | Üretim, Stok |
| 4 | Plan–satış siparişi bağı için ekran | Uç var, ekran yok; müşteri malı planı bu bağ olmadan ilerlemiyor | P0 | K | Üretim, Satış |
| 5 | Müşteri malının günlük kabulü (karşı taraf sahipli stok girişi) | Fason iş yapan şirket için müşteri malı bugün yalnız açılış sayımıyla girilebiliyor | P0 | O | Stok, Lojistik, Satış |
| 6 | Çıktının bağlı satış siparişine ayrılmasını kolaylaştırmak (öneri ya da tek adım) | Her etiket elle ayrılıyor; siparişe üretimde hata ve gecikme kaynağı | P1 | O | Üretim, Stok, Satış |
| 7 | Çıktıda döküm/rulo numarasını girdi rulosundan önermek | Elle yeniden giriş etiket bilgisini bozabilir | P1 | K | Üretim, Stok |
| 8 | İzlemede kapanmış alış siparişinin tedarikçisini göstermek | Geriye izleme en çok eski partilerde gerekir; bugün "bilinmiyor" dönüyor | P1 | K | Stok, Satın Alma |
| 9 | İrsaliye/fatura ve döküm numarasından başlayan izleme (geri çağırma listesi) | Müşteri şikâyetinde çıkış noktası belge ya da dökümdür, etiket değil | P1 | O | Stok, Lojistik, Satış |
| 10 | Müşteri iade faturası olayına Muhasebe tüketicisi | Olay yayımlanıyor, karşılayan yok; iade fişi olayla oluşmuyor | P1 | O | Satış, Muhasebe |
| 11 | Cari pasifleştirme/birleştirme olaylarını yayımlamak ve açık kayıt uyarısı üretmek | §8.5'in son halkası; eski cariyle açık sipariş, tahsis ve alacak görünmez kalıyor | P1 | O | Cari, İş merkezi, Satış, Finans |
| 12 | Giden fason zinciri (malın fasoncuya gidişi, işlenmesi, dönüşü, hizmet faturası eşleşmesi) | Satın Alma'daki fason alışı stok hareketi üretmiyor | P1 | B | Satın Alma, Stok, Lojistik, Üretim |
| 13 | Şirketler arası (GDC–GSP–GIN) üretim ve mal hareketi kuralı ve mekanizması | Kodda hiç karşılığı yok; üç şirket birlikte kullanınca ilk hafta gerekir. Önce karar gerekir | P1 | B | Organizasyon, Satış, Satın Alma, Stok, Muhasebe |
| 14 | Üretim tüketimi/çıktısı için muhasebe kaydı gereksinimini netleştirmek | Bugün yalnız maliyet farkı önerisi var; hammadde → yarı mamul → mamul hesap geçişi görülmedi | P1 | O | Muhasebe, Stok, Üretim |
| 15 | Bağımlılık testini Application referanslarını ve dolaylı Domain kullanımını kapsayacak biçimde genişletmek; iki dolaylı kullanımı sözleşmeye taşımak | 23 kenar ve 2 Domain kullanımı denetim dışında | P1 | K | Mimari testler, Finans, Üretim |
| 16 | Yayımlanmayan olaylar için karar: yayımla ya da yazmayı bırak | 13 olay türü yazılıp bekliyor; tablo büyür, niyet belirsiz | P2 | K | Stok, Kimlik, Satın Alma, Cari |
| 17 | Belge alma kanalları (e-posta, tarama), OCR, sınıflandırma ve AI taslağı | §8.4'ün orta halkaları yok | P2 | B | Belgeler, AI |
| 18 | Sicil kaynağı tarama ve fark önerisi | §8.5'in baş halkaları yok; dış kaynak ve sözleşme kararı gerekir | P2 | B | Cari, AI |
| 19 | Malzeme sertifikası (MTC) bağı | AT-039'un MTC kısmı; alan bilinçli olarak yok | P2 | O | Stok, Belgeler, Satın Alma |
| 20 | Üretim README'sini ve `Program.cs` açıklamasını güncellemek | README "21 uç, 9 izin" diyor; kodda 48 uç eşlemesi ve 17 izin adı var | P2 | K | Üretim |

## 8. Doğrulanamayanlar

- Hiçbir test ya da derleme koşulmadı; "kod var" ifadeleri çalıştığı anlamına gelmez. Seviye 2 koordinatörün
  test sonuçlarından eklenmelidir.
- Olay dağıtıcısının başka bir ortam ayarıyla (ortam değişkeni, sunucudaki ayar dosyası) açılıp açılmadığı
  depodan görülemez; depo içinde açan bir ayar yok.
- Dağıtıcının, kaynağın bildirmediği bir olay türünü (ör. `purchasing.order-line.closed.v1`) nasıl ele aldığı
  okunmadı.
- Üretim için ayrı muhasebe fişinin (hammadde → yarı mamul → mamul) bilinçli olarak mı bırakıldığı;
  ilgili karar metni aranmadı.
- Alış mal kabulünde mal sahibinin her zaman şirket olup olmadığı: sahip bilgisi istemciden alınmıyor, Satın
  Alma'nın kendi kaydından geliyor (`PurchasingService.GoodsReceipt.cs:85`); hangi durumda karşı taraf sahipli
  kabul üretebildiği izlenmedi.
- Satış–Finans ve Satın Alma–Finans fatura kabulünün aynı veritabanı işleminde olup olmadığı doğrulanmadı;
  matriste "bağdaştırıcı" olarak gösterildi.
- Giden fason için depolar arası transferin fasoncu konumuna kullanılabilirliği (konum işleticisi karşı taraf
  olabiliyor) uçtan uca izlenmedi.
- Geliştirme kimlik kipinde (`DevelopmentBootstrap`) kullanıcılara hangi izinlerin verildiği ve üretim
  ekranlarının bu kipte açılıp açılmadığı sınanmadı.
- 5.2 matrisi kısmen otomatik çıkarımla hazırlandı (bağdaştırıcı sınıfın uyguladığı arayüz ve kullandığı modül
  ad alanları); çok modül kullanan büyük dosyalar elendi, bu yüzden bazı tekil okuma bağları tabloda olmayabilir.
- Master §29'daki altın örnek (golden) değerlerin motor çıktılarıyla eşleştiği kod okunarak doğrulanamaz.
