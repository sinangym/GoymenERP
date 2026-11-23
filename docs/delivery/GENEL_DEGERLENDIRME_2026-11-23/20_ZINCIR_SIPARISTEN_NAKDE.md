# Siparişten nakde zinciri — durum ve yapılacaklar

> Kapsam: master rapor §8.1 zinciri (Talep/Teklif → … → Ticari/Finansal Kapanış), yedi kontrol noktası, iptal ve iade ters
> akışları, fatura → muhasebe fişi yan halkası. Modüllerin iç ayrıntısı değil, **halkalar arasındaki geçişler** incelendi.
> HEAD `ddc6cba`, 23 Kasım 2026. Yalnız kod okundu; derleme ve test koşulmadı. Bu belge karar veya onay değildir.
>
> Yol kısaltmaları: `API/` = `src/backend/GoymenERP.Api/`, `MOD/` = `src/backend/Modules/`,
> `WEB/` = `src/frontend/goymen-erp-web/src/`, `IT/` = `tests/GoymenERP.Api.IntegrationTests/`.
>
> Geçiş türleri: **otomatik** = aynı veritabanı işleminde ya da olay tüketicisiyle kendiliğinden; **port** = sonraki modül
> öncekinin verisini okuyor, kullanıcı ayrı işlem başlatıyor ama veriyi yeniden yazmıyor; **elle** = kullanıcı bilgiyi
> yeniden giriyor; **yok** = geçiş yok.

## 1. Kısa sonuç

Zincir kodda büyük ölçüde kurulmuş: talepten tahsilat dağıtımına kadar on üç halkanın uç noktası da ekranı da var ve
halkalar birbirinin verisini okuyor; kullanıcı hiçbir halkada miktar, fiyat ya da cari bilgisini yeniden yazmıyor. Yine de
**bugün üç firmada baştan sona yürütülemez**, çünkü:

1. **Zinciri yürütecek rol yok.** Başlangıç rollerinin beşi de organizasyon/güvenlik işi; `sales.*`, `logistics.*`,
   `inventory.*`, `finance.*`, `accounting.*` izinlerinin hiçbiri bir rolde değil ve rol oluşturan bir uç yok
   (`API/Seed/identity-seed.json`; `API/Endpoints/IdentityEndpoints*.cs` içinde yalnız `GET /access/roles`).
2. **Zincirin ilk iç kopuşu ikinci sevkiyatta.** Bir müşterinin ilk faturası kesinleştiği andan sonra o müşterinin riski
   "hesaplanamıyor" çıkıyor; sevk kapısı bunu "limit aşıldı" gibi durduruyor. Sonraki her sevkiyat ayrı bir risk istisnası
   onayı istiyor (`MOD/Sales/…Application/SalesDraftService.RiskCheck.cs:551-558`).
3. **Fatura → muhasebe fişi halkası kapalı.** Olay dağıtıcısı ve muhasebe kabul anahtarı varsayılan olarak kapalı; hiçbir
   ayar dosyasında açılmıyor (`appsettings.json` içinde `Work:OutboxDispatcher` ve `Accounting:InvoiceJournal` yok).
4. **Sipariş "Kapat" komutu her zaman reddediliyor**; yalnız "zorla kapat" çalışıyor (kapanış eksenlerinin üçünü besleyen
   kaynak bağlanmamış).
5. **Banka mutabakatı yok.**

Halka sayımı (15 halka): **10 gerçek** (talep/teklif, sipariş, tahsis, yükleme, fiilî sevkiyat, irsaliye, fatura, açık kalem,
tahsilat, dağıtım), **4 kısmi** (risk/onay, kaynak stratejisi, satın alma/üretim bağı, kapanış), **1 yok** (banka
mutabakatı). Yan halka (muhasebe fişi) kodda var, çalışma zamanında kapalı. Ölçüm merdiveninde hepsi en fazla seviye 1
(kod yazılmış); seviye 3'ün ortak engeli rol eksikliğidir.

## 2. Halka halka tablo

| # | Halka | Modül | Komut / uç | Ekran (menü) | Sonrakine geçiş | Taşınan veri | Durum | Kanıt |
|---|---|---|---|---|---|---|---|---|
| 1 | Talep / Teklif | Satış | `POST /sales/requests`, `…/approval`; `POST /sales/quotes`, `…/revision`, `…/conversion` | Satış talepleri, Satış teklifleri | **otomatik** (teklif → sipariş tek kayıtta; bir tekliften bir sipariş) | talep kimlikleri teklife, teklif satırları siparişe | gerçek | `API/Endpoints/SalesEndpoints.Requests.cs`, `…Quotes.cs`; `MOD/Sales/…Application/SalesDraftService.Quotes.cs:224` |
| 2 | Satış siparişi | Satış | `POST /sales/drafts`, `…/submission`, `…/approval`, `…/customer-acceptance`, revizyon uçları | Satış Siparişleri | **port** (onaylı satır; tahsis, yükleme, alış ve üretim bunu okur) | sipariş, satır, ürün, miktar, revizyon | gerçek | `MOD/Sales/…Infrastructure/SalesModuleExtensions.cs:85` (`ISalesOrderLineReferencePortV1`) |
| 3 | Risk / onay | Satış ← Finans | `GET …/risk-check`; onayda yalnız uyarı; durdurma sevk anında; `POST …/risk-exceptions` | Sipariş ayrıntısında risk kartı ve istisna | **port** (Lojistik her kesin sevkte Satış risk kapısını okur) | karar (içinde / aşıldı / hesaplanamıyor) ve istisna | kısmi | `SalesDraftService.cs:1051`; `SalesDraftService.ShipmentRiskGate.cs`; §3.1 |
| 4 | Kaynak stratejisi | Satış | `GET/POST …/source-plan` | Sipariş ayrıntısı, kaynak planı | **yok** (plan başka modülde belge açmıyor; bilgi kaydı) | satır başına stok / üretim / alış / fason payları | kısmi | `SalesDraftService.SourcePlan.cs:8-44` |
| 5 | Tahsis | Stok | `POST /inventory/physical-allocations`, `…/release`, `…/change`; gelecek tahsis `POST /inventory/future-allocations` | Sipariş ayrıntısında tahsis paneli; Stok pozisyonu; gelecek tahsis sayfası | **port** (sevk komutu tahsis kimliğini ister) | etiket, kg, adet, sipariş satırı | gerçek | `API/Endpoints/InventoryPhysicalAllocationHostPorts.cs`; `WEB/sales/SalesDraftAllocationPanel.tsx` |
| 6 | Satın alma veya üretim | Alış, Üretim | Alış talebinde satır kaynağı; `POST /manufacturing/plans/{id}/sales-order-link` | Alış talep/sipariş, Üretim planı | **port** (Satış bağlı açık alış/üretim belgesini okur) — ama bağı **ekrandan kurmak mümkün değil** | sipariş satırı referansı | kısmi | `API/Program.cs:440-447`; `WEB/purchasing/PurchaseRequestCreatePage.tsx:94` (yalnız "elle kaynak"); §3.2 |
| 7 | Yükleme | Lojistik | `POST /logistics/loading-instructions`, `…/issue`, `…/lines/{id}/loads`, `…/weighings` | Yükleme talimatları, Satış operasyon listesi | **port** (talimat onaylı satırı Satış'tan seçer; her yüklemeden önce kalanı yeniden okur) | sipariş satırı, ürün, müşteri | gerçek | `MOD/Logistics/…Application/LogisticsInstructionService.cs:14-28, 754-759` |
| 8 | Fiilî sevkiyat | Lojistik + Stok | `POST …/lines/{id}/shipments`, `…/deliveries` | Talimat ayrıntısı | **otomatik** (sevk kaydı ve stok çıkışı tek işlemde); Satış'a teslim miktarı **port** | sevk miktarı, kg, adet, tahsis | gerçek | `API/LogisticsRuntime/LogisticsRealizationTransactionRunner.cs`; `…Realization.cs:66-72` |
| 9 | İrsaliye | Lojistik | `POST …/dispatch-notes`, `…/approve`, `…/cancel`, `…/e-dispatch-number`; kantar farkı `POST /logistics/weighing-differences` | Talimat ayrıntısı; Satış > Sevkiyat / İrsaliyeler; irsaliye PDF | **port** (Satış onaylı irsaliye satırlarını fatura adayı olarak okur) | kesin sevkler, sistem numarası, kantar kg | gerçek (resmî e-İrsaliye gönderimi yok, numara elle) | `LogisticsInstructionService.DispatchNotes.cs:8-28`; `API/Endpoints/LogisticsDispatchPdfEndpoints.cs` |
| 10 | Fatura | Satış + Finans | `POST /sales/invoices`, `…/submission`, `…/approval`, `…/external-match`, `…/finalization`; resmî fatura `POST /finance/official-sales-invoices` ya da CSV içe aktarma | Faturaya Hazır, Satış faturaları; Finans > Resmî satış faturaları | **otomatik** (kesinleştirme sonrası Finans'a aynı istekle senkron bildirim; başarısızsa "Finans kabulü bekliyor" + yeniden gönder) | cari hesap, para birimi, kur, vadeler, tutarlar | gerçek (resmî fatura dış programda kesilir) | `SalesInvoiceService.cs:14-35`; `SalesInvoiceService.Finalize.cs:9-12`; `API/Shell/SalesFinanceAdmissionHost.cs` |
| 11 | Cari açık kalem | Finans | (10'dan gelir) `GET /finance/open-items`, cari ekstre | Açık kalemler, Cari Hesaplar | **port** (dağıtım açık kalemi seçer) | vade başına bir açık kalem | gerçek | `MOD/Finance/…Infrastructure/FinanceInvoiceOpenItemAdmissionService.cs:15-22` |
| 12 | Tahsilat kaynağı | Finans | `POST /finance/manual-receipts/drafts`, `…/manual-receipts/{id}/record`; çek/senet `POST /finance/customer-instruments`, `…/collection` | Tahsilat Taslakları, Müşteri Çekleri ve Senetleri | **elle ayrı işlem** (tahsilat faturadan türemez; cari ve hesap seçilir) | cari, hazine hesabı, tutar, tarih, banka referansı | gerçek | `MOD/Finance/…Contracts/ManualReceiptV1.cs:28-36`; `API/Endpoints/FinanceEndpoints.cs` |
| 13 | Dağıtım | Finans | `POST /finance/allocations`, `…/release` | Finansal Dağıtımlar | **port** (kullanıcı tahsilatı açık kalemlere dağıtır) | tahsilat ↔ açık kalem tutarı | gerçek | `MOD/Finance/…Infrastructure/FinanceAllocationService.cs`; `WEB/finance/AllocationPages.tsx` |
| 14 | Banka mutabakatı | — | yok | yok | **yok** | — | yok | `Reconcil`, `Mutabakat`, `BankStatement` araması Finans kodunda 0 sonuç; yalnız Ayarlar'da banka CSV eşleme tanımı (`API/Endpoints/SettingsBankCsvMappingEndpoints.cs`) |
| 15 | Ticari / finansal kapanış | Satış, Muhasebe | `POST /sales/drafts/{id}/close` (her zaman 409), `…/force-close`, `…/line-closure`; `POST /accounting/periods/{id}/close` | Sipariş ayrıntısı; Muhasebe Dönemleri | — | — | kısmi | `SalesDraftService.Status.cs:94-102`; §3.6 |
| Y | Fatura → muhasebe fişi | Satış → Muhasebe | olay `sales.invoice.finalized.v1` → taslak fiş | Kaynak fişler, Muhasebe Fişleri | **otomatik ama kapalı** (dağıtıcı ve kabul anahtarı kapalı) | fatura tutarları, tarih, kullanıcı | kod var, çalışmıyor | `API/Shell/TrainCompletionHostComposition.cs:91-105`; `API/AccountingRuntime/AccountingInvoiceJournalHost.cs` |

Test kaynağı: her halkanın birim ve gerçek-SQL testi var (`tools/relational_inventory/sales.txt` 241, `logistics.txt` 88,
`inventory.txt` 367, `finance.txt` 255, `accounting.txt` 151 satır; ön yüzde `WEB/sales` altında 99 test dosyası).
**Zinciri baştan sona tek senaryoda yürüten test yok** (`tests/e2e` altında yalnız modül başına süitler; `IT/` içinde
`OrderToCash`, `EndToEnd`, `Chain` adlı dosya 0 sonuç).

## 3. Sorunlu ya da dikkat isteyen geçişler

### 3.1 Sipariş → risk → sevk (halka 3 ve 8)

Risk hesabı Finans'tan (açık alacak, kredi limiti, teminat), Lojistik'ten (sevk edilmiş, faturalanmamış) ve Satış'ın
kendisinden (onaylı, sevk edilmemiş) okunan rakamlarla her seferinde taze yapılıyor; hiçbir rakam kopyalanmıyor. Sipariş
onayında limit aşımı yalnız **uyarı** olarak kaydediliyor (karar FNS-D27); asıl durdurma kesin sevk kaydında.
Sevk kapısı üç durumda sevki reddediyor: limit aşıldı, müşterinin kredi limiti tanımlı değil, risk hesaplanamadı. Üçünde
de çıkış yolu o sevke özel, başka bir kişinin onayladığı, gün sonuna kadar geçerli tek sevk istisnası
(`MOD/Sales/…Application/SalesRiskExceptionService.cs:13-29`).

İki sonuç:

- **Kredi limiti olmayan müşteriye sevk yapılamaz.** Her müşteri hesabı için Finans'ta limit girilip onaylanmalı.
- **İlk kesin faturadan sonra risk "hesaplanamıyor".** Kesinleşmiş bir faturanın tutarı ya açık alacağa geçmiştir ya da
  henüz Finans'ta işlenmemiştir; hangisi olduğunu yalnız Finans söyleyebilir ve bugünkü okuma (sürüm 1) bunu taşımıyor.
  Kod çift saymamak için iki toplamı da "hesaplanamıyor" yapıyor. Bunu taşıyacak ikinci sürüm arayüzü tanımlı
  (`ISalesPartyRiskReadPortV2`) ama **uygulaması ve kaydı yok** (arama: yalnız arayüz dosyası ve bir yorum). Davranış testle
  sabitlenmiş (`tests/GoymenERP.Sales.UnitTests/SalesDraftServiceTests.RiskUnbilled.cs:56`). Pratikte: bir müşteriye ikinci
  sevkiyattan itibaren her sevk için istisna onayı gerekir.

### 3.2 Kaynak planı → satın alma / üretim (halka 4 ve 6)

Kaynak planı satır miktarını "stok + üretim + alış + fason" olarak böler ve toplamın satıra eşit olmasını zorlar; stok payı
elle girilmez, satırın etkin tahsisinden türetilir. Plan **hiçbir belge açmaz**: alış talebi ve üretim planı ayrı ekranlarda
elle başlatılır. Serbest stok önerisi de "kullanılamıyor" döner (kodda serbest stok okuma bağlantısı yok,
`SalesDraftService.SourcePlan.cs:35-37`).

Arka uçta bağ var: alış talebi satırı bir satış sipariş satırını kaynak gösterebilir ve Satış onaylı satırı doğrular
(`MOD/Purchasing/…Application/PurchasingService.cs:972-978`); üretim planı sipariş satırına bağlanabilir
(`API/Endpoints/ManufacturingEndpoints.cs:536-545`). **Ekranda ise yok:** alış talebi oluşturma sayfası yalnız "elle kaynak"
yazıyor; üretim planı ekranı sipariş bağını yalnız gösteriyor, yazan çağrı yok (`WEB/manufacturing` altında
`sales-order-link` araması 0 sonuç). Alış tarafında ekrandan çalışan tek bağ "gelecek tahsis": henüz gelmemiş alış
siparişi miktarını bir satış satırına ayırma (`WEB/inventory/InventoryFutureAllocationPage.tsx`).

Üretilen ya da satın alınan mal stoka girdikten sonra satış satırına **elle** fiziksel tahsis yapılır; üretim çıktısı
kendiliğinden siparişe ayrılmaz.

### 3.3 Sevkiyat → irsaliye → fatura (halka 8–10)

Bu bölüm zincirin en sağlam kısmı. Kesin sevk kaydı, stok çıkışı ve tahsisin tüketilmesi tek veritabanı işleminde
yapılır. İrsaliye yalnız kesin sevklerden oluşur; bir sevk aynı anda tek irsaliyede olabilir. Fatura yalnız onaylı
irsaliye satırlarından oluşur; bir sevk aynı anda tek etkin faturada olabilir. Miktar ve fiyat yeniden yazılmaz.

Dikkat isteyenler:

- **Kantar farkı kaydı fatura için şart.** Her irsaliyede Lojistik'in kantar farkı kaydı olmadan fatura taslağı "hazır
  değil" döner. Farkı değerlendiren oran ve bantlar Ayarlar'dan okunacak şekilde tasarlanmış ama **Ayarlar'da böyle bir
  kayıt yok**; tek uygulama "okunamadı" cevabı veriyor (`MOD/Logistics/…Application/Contracts/WeighingDifferenceContractsV1.cs:181`;
  Ayarlar modülünde `WeighingDifference` araması 0 sonuç). Sonuç: kantar fazlası olan her irsaliye ikinci kişinin onayına
  düşer; oran girilemediği için hiçbiri "oran içinde" sayılmaz.
- **Resmî fatura dış programda kesilir** (karar SAT-D7). Zincir şöyle işler: Satış'ta fatura hazırlığı → dış programda
  resmî fatura → Finans'ta resmî fatura kaydı (PDF ile elle, ya da CSV içe aktarma; giren ≠ onaylayan) → Satış'ta
  eşleştirme → kesinleştirme. Eşleşmeden kesinleştirme reddedilir. e-Fatura/e-İrsaliye bağlantısı yok (`Integrator`,
  `EInvoiceProvider` araması yalnız belge kasası dosyalarında sonuç verdi).
- **Kesinleştirme sonrası Finans bildirimi ayrı adım.** Fatura önce "Finans kabulü bekliyor" olarak kaydedilir, sonra
  Finans'a bildirilir. Bildirim başarısız olursa fatura bekler ve "yeniden gönder" komutu vardır
  (`…/finance-admission`). Bekleyen fatura açık kalem üretmemiştir; risk hesabında "sevk edilmiş, faturalanmamış" tarafında
  kalır.

### 3.4 Tahsilat → dağıtım (halka 12–13)

Tahsilat faturadan bağımsız kaydedilir (banka havalesi, POS, çek/senet). Kesin kayıt için tahsilat tarihinin açık bir
muhasebe dönemine düşmesi ve kaydeden kişinin dönemi okuma izni olması gerekir (`API/Program.cs:162-193`). Dağıtım ayrı
bir komuttur; tahsilat kendiliğinden en eski açık kaleme kapanmaz. Dağıtılmamış tahsilat risk hesabında açık alacaktan
düşülür.

### 3.5 Fatura → muhasebe fişi (yan halka)

Satış fatura kesinleştiğinde, iade faturası kesinleştiğinde ve resmî iptal kaydedildiğinde kendi olay kutusuna kayıt
yazar. Muhasebe'nin üç tüketicisi dağıtıcıya kayıtlı: taslak satış fişi, fatura iptali ve satılan malın maliyeti önerisi.
Ancak:

- Dağıtıcı `Work:OutboxDispatcher:Enabled` açık değilse hiç başlamaz; açıldığında beş değer (parti boyu, deneme sayısı,
  bekleme süreleri) elle verilmezse uygulama açılmaz (`MOD/Work/…Infrastructure/Outbox/WorkOutboxDispatcherOptions.cs`).
- Muhasebe kabulü `Accounting:InvoiceJournal:InboxAdmissionEnabled` açık değilse her olayı "kullanılamıyor" diye geri
  çevirir (`API/AccountingRuntime/AccountingInvoiceJournalHost.cs:54-94`).
- İki anahtar da `appsettings.json`, `appsettings.Development.json` ve `infra/` altında **hiç geçmiyor**. İkisi de insan
  kapısı olarak işaretli.

Dağıtıcı kapalıyken etkilenen diğer geçişler: satılan malın maliyeti (SMM) önerisi, alış faturası fişi, alış maliyet
farkı, kredi hareketi fişi önerisi, bildirimler, risk istisnasının Satış ekranındaki "kullanıldı" görüntüsü (istisnanın
tek kullanımlık olması Lojistik'in kendi kaydında zorlandığı için güvenlik etkilenmez).

Tahsilatın muhasebe fişi olay ile değil, Muhasebe ekranından kaynak seçilerek elle taslak açılarak oluşur
(`MOD/Accounting/…Infrastructure/FinanceManualReceiptNormalJournalSourceProviderV1.cs`). **İade faturası olayını dinleyen
tüketici yok** (`sales.return-invoice.finalized` araması Satış dışında 0 sonuç).

Not: tüketici dosyasındaki açıklama "henüz dağıtıcıya kayıtlı değil" diyor; kod onu kaydediyor
(`TrainCompletionHostComposition.cs:103`). Açıklama eski.

### 3.6 Kapanış (halka 15)

Karar (SAT-D32) siparişin kendiliğinden kapanmamasını, yedi eksen tamamlanınca "kapanmaya hazır" işareti çıkmasını ve
kullanıcının kapatmasını ister. Yedi eksenin üçü (kaynak, faturalama, finansal kapanış) dışarıdan beslenecek bir bağlantı
bekliyor (`ISalesOrderOwnerAxesSourceV1`); **bu bağlantıyı kaydeden kod yok**. Bu üç eksen hep "kaynak yok" döndüğü için
işaret hiç çıkmaz ve "Kapat" komutu her zaman reddedilir. Fatura ve açık kalem artık kodda olduğu hâlde eksenlere
bağlanmamış. Çalışan yollar: satır kapatma, etki önizlemeli zorla kapatma, iptal. Açık kalemin tahsilatla kapanması ve
muhasebe dönemi kapanışı Finans/Muhasebe tarafında var; "bu sipariş tahsil edildi" bilgisi siparişe geri dönmüyor.

## 4. Kontrol noktaları (master §8.1)

| Kontrol | Nerede zorlanıyor | Eşik nereden | Değerlendirme |
|---|---|---|---|
| Cari / limit / risk onayı | **Sunucuda.** Sipariş onayında uyarı; kesin sevkte durdurma (`LogisticsInstructionService.Realization.cs:66-72, 243-262`); istisna onayı ayrı izin, isteyen onaylayamaz | Kredi limiti ve teminat Finans kaydından; teminat ağırlığı Ayarlar teminat türü oranından | Çalışıyor; §3.1'deki "hesaplanamıyor" sorunu dışında. Limit yoksa sevk yok |
| Fiyat / iskonto / marj yetkisi | **Sunucuda.** Taban fiyat altı ve limit üstü iskonto ayrı onay komutları, onaylayan giren kişi olamaz (`SalesDraftService.DiscountException.cs`, `…/below-floor-exception-approval`) | Taban fiyat Ürün fiyat listesinden; iskonto limiti Ayarlar'da kişi → limit grubu bağından; limit tanımsızsa her iskonto onaya düşer | Fiyat ve iskonto var. **Marj için eşik ya da yetki yok:** maliyet onayda sabitlenip gösteriliyor, marj düşük diye sipariş durmuyor (`MarginFloor`, `MinimumMargin`, `MarginApproval` araması 0 sonuç) |
| Termin ve kaynak uygunluğu | **Kısmen.** Teslim planı revizyonu ve gelecek tahsis sunucuda; "bu termine yetişir mi" denetimi yok | Kaynak planı toplam eşitliği kodda | Termin kaynağa bağlı doğrulanmıyor; stok eksiği uyarısı ayrı bir Stok ekranı (`WEB/inventory/InventoryShortageWarningPage.tsx`) |
| Tahsis yarış ve toplam kontrolü | **Sunucuda ve veritabanında.** Etiket sürümü beklenir; serbest stok ve satırın açık miktarı aşılırsa red (`MOD/Inventory/…Application/InventoryAllocationContractsV1.cs:160, 194`); tahsis tablosunda denetim kısıtları (`InventoryDbContext.cs:685-719`) | Satırın açık miktarı Satış'tan; serbest miktar Stok'tan | Çalışıyor. Gelecek tahsiste satış ve alış satırı kilitleri sırayla alınıyor |
| Plan – fiilî yük – kantar farkı | **Sunucuda.** Plan fazlası ayrı onay (`API/Endpoints/LogisticsEndpoints.PlanExcess.cs`); fazla teslim onayı Satış'ta; kantar iki tartım tutarlılığı; kantar farkı kaydı fatura ön koşulu | Teslim toleransı Ayarlar satış politikasından. **Kantar farkı oranı ve bantları okunamıyor** (§3.3) | Mekanizma var; kantar eşiği girilemiyor, bu yüzden her fazlalık onay istiyor |
| Faturaya hazır uyumluluk grubu | **Sunucuda.** Tek fatura = tek cari hesap, tek para birimi, tek kur, tek vade planı; vergi, fiyat, kur, vade, kantar farkı eksikse "hazır değil" ve bütün nedenler listelenir (`…Contracts/SalesInvoiceContractsV1.cs:248-293`) | Kur Ayarlar kur kaydından (sevk günü); vergi Ayarlar vergi listesinden | Çalışıyor. Resmî belgeyle tutar farkı toleransı kodda sabit 0,05 TL (`SalesModuleExtensions.cs` içinde `SalesInvoiceMatchTolerancePolicy`), Ayarlar bağı ertelenmiş |
| Açık kalem ve para kaynağı dağıtımı | **Sunucuda.** Açık kalem yalnız kesin faturadan; dağıtım ayrı komut, ters kayıt önerisi dağıtım varken engelleniyor (`FinanceAllocationReversalGate.cs`) | — | Çalışıyor; banka hareketiyle eşleştirme yok |

## 5. Ters akışlar

| Akış | Ne yapılıyor | Zincirde geri alınan | Geri alınmayan / eksik |
|---|---|---|---|
| Sipariş iptali | Önce etki önizlemesi; önizleme değişmişse red (`SalesDraftService.Cancellation.cs:12-41`) | Gelecek tahsisler iptalle aynı işlemde serbest kalır | Fiziksel tahsis, yükleme talimatı, onaylı irsaliye, bağlı alış/üretim **engeller**; kullanıcı her birini kendi ekranında çözer. "Eşleşmemiş avans" kategorisi kaynaksız, hiç denetlenmiyor |
| Zorla kapatma / satır kapatma | Kalan miktar kesilir; etki önizlemeli | Kalan talep | Bağlı belgeler elle |
| Yükleme talimatı iptali | İptal + indirme tartısı kaydı | Talimat | Stok hareketi yazılmaz; tartı farkı yalnız uyarı, eşik yok (`LogisticsInstructionService.cs:226-233`) |
| İrsaliye iptali | Neden ile iptal; sevkler yeni irsaliye için serbest | İrsaliye bağı | Kesin sevkin kendisi ve stok çıkışı geri alınmaz; bu ancak müşteri iadesiyle olur |
| Fatura iptali (kesinleşmeden) | `…/cancellation`, yalnız onay öncesi | Sevk payları yeniden faturalanabilir | — |
| Kesinleşmiş faturanın iptali | "Resmî fatura iptal kaydı" ekranı ve komutu var | — | **Çalışmıyor:** iptalin dış sistemde gerçekleştiğini doğrulayan kanıt kaynağı yalnız "kullanılamıyor" uygulamasına sahip; her kayıt 503 döner (`…Contracts/SalesOfficialInvoiceCancellationV1.cs:23-33`, `SalesModuleExtensions.cs:55`). Finans açık kalemini ve muhasebe fişini geri alan kod hazır ama tetiklenemiyor |
| Müşteri iadesi — mal | Lojistik iade kaydı + onay; stok girişi aynı işlemde (`API/LogisticsRuntime/LogisticsCustomerReturnRuntime.cs`) | Stok, sipariş ekranında iade miktarı | Fatura tarafına bağlı değil |
| Müşteri iadesi — fatura | Satış iade faturası (hazırla, onayla, kesinleştir); Finans aynı istekte kabul eder ve seçilen vadelerden düşer (`SalesCustomerReturnService.cs:13-31`) | Açık kalem | Stok hareketi yazmaz; **mal iadesiyle çapraz denetim yok** (biri olmadan diğeri kaydedilebilir). Muhasebe fişi yok (§3.5) |
| Tahsilat ters kaydı | Öneri + ikinci kişi onayı | Tahsilat | Dağıtım varken önce dağıtım serbest bırakılmalı |
| Dağıtım geri alma | `…/allocations/{id}/release` | Açık kalem bakiyesi | — |

Geçmiş hiçbir akışta silinmiyor; iptal ve ters kayıtlar ayrı kayıt olarak duruyor (AT-007 ile uyumlu).

## 6. Tek gerçeklik denetimi

| Olay | Sahibi | Diğer modüller nasıl görüyor | Çift kayıt riski |
|---|---|---|---|
| Sipariş, fiyat, revizyon | Satış | Onaylı satır okuması | Yok |
| Tahsis | Stok | Satış ve Lojistik okur | Yok; yükleme payı stok tutmaz, yalnız tahsis tutar |
| Kesin sevk, teslim, irsaliye, kantar | Lojistik | Satış okur (teslim miktarı, fatura adayı, kantar farkı) | Yok; stok çıkışı aynı işlemde tek hareket; bir sevk tek irsaliyede (filtreli tekil anahtar) |
| Fatura hazırlığı | Satış | — | Bir sevk payı tek etkin faturada (AT-055) |
| Resmî fatura belgesi | Finans (resmî satış faturası kaydı) | Satış numara + ETTN ile eşleştirir | Aynı ticari olayın **iki kaydı** var (Satış hazırlığı ve Finans resmî kaydı). Bilinçli tasarım; eşleştirme bağlıyor |
| Alacak açık kalemi | Finans | Satış risk için okur | **Risk var:** açık kalem iki yoldan üretilebiliyor: (a) Satış fatura kesinleştirmesi, (b) Finans'ta "onaylı fatura taksit kaynağı" CSV içe aktarmasının uygulanması. (b) kendi içinde resmî numara ve ETTN çakışmasını engelliyor ama (a) ile karşılaştırma **yapılmıyor**; kodda bu açıkça ertelenmiş (`FinanceOpenItemSourceAdmissionService.cs:208-213`). Aynı fatura iki yoldan da girilirse alacak iki kez oluşur |
| Tahsilat, dağıtım | Finans | — | Yok |
| Muhasebe fişi | Muhasebe | — | Fatura kimliği + sürümüyle tekilleştirilmiş; dağıtıcı kapalı olduğu için bugün hiç oluşmuyor |
| Müşteri iadesi | Mal: Lojistik + Stok. Fatura: Satış + Finans | Sipariş ekranı ikisini de gösterir | Çift kayıt değil, **kopukluk** riski (§5) |

Kopya veri görülmedi: müşteri adı, ürün adı, teslim miktarı, kg bilgileri sahibinden her okumada alınıyor. Sabitlenen
alanlar (faturadaki kur ve tutarlar, onaydaki maliyet ve adet başına kg) karar gereği anlık görüntü.

## 7. Üç şirket boyutu

- Zincirin her halkası tek şirket içinde çalışır; her kayıt şirket kimliği taşır ve her komut şirket geneli izin ölçer.
- **Şirketler arası sipariş, sevk ya da fatura temsili yok.** Arka uçta `Intercompany`, "şirketler arası", "grup içi"
  araması 0 sonuç. "GSP satıyor, GDC üretip sevk ediyor" durumu bugün iki ayrı zincirle yürütülür: GDC'de GSP'ye satış
  (sipariş → sevk → fatura → alacak) ve GSP'de GDC'den alış (sipariş → mal kabul → fatura → borç), artı GSP'de müşteriye
  satış. Üç belge de elle girilir; biri diğerini doğurmaz ve birbirini doğrulamaz. Doğrudan müşteriye sevk (GDC deposundan
  GSP'nin müşterisine) için ayrı bir temsil görülmedi.
- Bu, kararla uyumlu: 22 Kasım 2026 kararında grup içi işlemin az olduğu, tam otomasyonun **ertelendiği**, yerine şirket
  bazlı grup içi işlem dökümü ve emsal fiyat karşılaştırma raporu istendiği yazılı
  (`docs/decisions/SINAN_KARARLARI_2026-11-22_FIKIR_HAVUZU.md:26, 39`). O rapor da kodda yok.
- Cari kartı grup genelinde tek, cari hesap şirket bazında; bir grup şirketi diğerinde müşteri ya da tedarikçi olarak
  tanımlanabilir. Ürün kartı ortak, stok ve ortalama maliyet şirket bazında.

## 8. Master §29 kabul senaryoları

Hepsi için ortak ön koşul: ilgili izinleri taşıyan rol ve kurulum verisi. Aşağıdaki "koşulabilir" bu ön koşul sağlandığında
(örneğin test verisi yazıcılarıyla) anlamındadır; bugün menüden bir kullanıcıyla koşulamaz.

| Senaryo | Bugün koşulabilir mi | Engel / not |
|---|---|---|
| AT-021 fiyat alt sınırı → onay | Evet | Taban fiyat için Ürün fiyat listesi ve Ürün çalışma zamanı ayarı gerekir |
| AT-022 risk limiti aşımı sessiz serbest bırakılmaz | Evet | Sevkte durdurma var. İlk kesin faturadan sonra sonuç "hesaplanamıyor" (yine durduruyor ama doğru nedeni göstermiyor) |
| AT-023 onaylı siparişte kritik değişiklik onayı düşürür | Evet | Ticari, miktar ve satır revizyonu yeniden onay açıyor (`SalesDraftService.cs:551`) |
| AT-024 kısmi teslimat kalanı | Evet | Teslim toplamı Lojistik'ten okunuyor |
| AT-028 iade kaynak bağını korur | Kısmen | İade faturası kaynak faturaya, mal iadesi talimat satırına bağlı; ikisi birbirine bağlı değil |
| AT-032 / AT-033 / AT-034 tahsis | Evet | — |
| AT-036 yüklemeye tutulan miktar başka siparişe kaynak olamaz | Kısmen | Tutma tahsisle sağlanıyor; yükleme payı ayrıca stok tutmuyor |
| AT-035 / AT-051 kalite bloğu, karantinadaki çıktı sevk edilemez | Hayır | `Quarantine`, `QualityBlock`, `Karantina` araması Stok, Lojistik ve Üretim'de 0 sonuç |
| AT-053 kantar neti | Evet | — |
| AT-054 fiilî yük – tahsis – plan uzlaşması | Kısmen | Sevk tahsisle eşleşmek zorunda; plan fazlası onayı var; kantar eşiği girilemiyor |
| AT-055 aynı sevk iki kez irsaliye/faturaya bağlanamaz | Evet | — |
| AT-058 tahsilat ↔ açık kalem ↔ banka hareketi | Kısmen | Tahsilat ↔ açık kalem var; banka hareketi (ekstre) yok |
| AT-007 iptal geçmişi silmez | Evet | Kesinleşmiş fatura iptali çalışmadığı için o dal koşulamaz |
| AT-004 kendi işlemini onaylayamaz | Evet | Sipariş, iskonto, risk istisnası, fatura, iade, resmî fatura kaydı, kantar farkı onaylarında var |

Bu zincire düşen diğer senaryolar (AT-011 çok para birimli bakiye, AT-063 stok satırından işlem, AT-065/066 belge ve PDF)
bu incelemede doğrulanmadı.

## 9. Yapılacaklar

| # | İş | Neden | Öncelik | Büyüklük | İlgili modüller |
|---|---|---|---|---|---|
| 1 | Zinciri yürütecek iş rollerini tanımla ve izinleri dağıt (satışçı, satış müdürü, depo/sevk, kantar, finans, muhasebe; giren ≠ onaylayan çiftleri) | Bugün zincirin hiçbir izni bir rolde değil; rol oluşturan uç yok. Yetki dağıtımı insan kapısı | P0 | O | Kimlik, tümü |
| 2 | Risk okumasını ikinci sürüme geçir: Finans "işlediğim faturalar" kümesini aynı cevapta versin | İlk kesin faturadan sonra müşterinin riski hesaplanamıyor; her sevk istisna onayı istiyor | P0 | O | Finans, Satış |
| 3 | Test ortamında olay dağıtıcısını ve muhasebe kabul anahtarını aç; beş dağıtıcı değerini belirle | Fatura → taslak fiş, SMM önerisi, bildirim ve alış tarafındaki olaylar çalışmıyor. Açma kararı insan kapısı | P0 | K (karar) + K | İş (Work), Muhasebe, Satış |
| 4 | Sipariş kapanış eksenlerini bağla (kaynak, faturalama, finansal kapanış) | "Kapat" her zaman reddediliyor; sipariş yalnız zorla kapatılabiliyor | P0 | O | Satış, Finans, Stok |
| 5 | Zincir kurulum sırasını tek kontrol listesine bağla ve test ortamında uygula: Ürün çalışma zamanı ayarı, fiyat listesi, vergi listeleri, para profili ve kur, teslim toleransı, iskonto limitleri, cari hesap + kredi limiti, depo ve açılış stoku, banka/kasa hesabı, muhasebe dönemi, hesap eşlemeleri | Her biri eksikse zincir ilgili halkada "kullanılamıyor" ile durur (ör. Ürün ayarı boşken sipariş satırı 503, limit yokken sevk 409, dönem yokken tahsilat kaydı red) | P0 | O | Ayarlar, Ürün, Cari, Finans, Muhasebe, Stok |
| 6 | Uçtan uca tek senaryo testi: talep → … → dağıtım → fiş, üç şirket için | Halkalar ayrı ayrı test edilmiş, zincir bütün olarak hiç koşulmamış | P0 | O | Tümü |
| 7 | Açık kalemin iki giriş yolunu (Satış kesinleştirmesi ve CSV taksit kaynağı) aynı resmî numara/ETTN üzerinden çakıştır | Aynı fatura iki yoldan girilirse alacak iki kez oluşur | P0 | K–O | Finans, Satış |
| 8 | Kantar farkı oranı ve bantlarını Ayarlar'a ekle ve Lojistik'e bağla | Eşik okunamadığı için kantar fazlası olan her irsaliye onay bekliyor | P1 | O | Ayarlar, Lojistik |
| 9 | Kesinleşmiş fatura iptali için kanıt kaynağını sağla (ya da kanıtın nasıl kabul edileceğine karar ver) | Ekran ve komut var, her kayıt 503 | P1 | O (karar gerekir) | Satış, Finans, Muhasebe |
| 10 | Alış talebi ekranına satış kaynağı, üretim planı ekranına sipariş bağı yazma; sipariş ekranından "alış talebi aç / üretim planla" kısayolu | Arka uç bağı var, ekrandan kurulamıyor; kaynak planı bilgi kaydı olarak kalıyor | P1 | O | Alış, Üretim, Satış |
| 11 | Banka mutabakatı: ekstre içe aktarma, tahsilatla eşleştirme | Zincirin 14. halkası yok; Ayarlar'daki CSV eşleme tanımının tüketicisi yok | P1 | B | Finans, Ayarlar |
| 12 | Marj yetkisi: eşik ve onay akışı (önce karar) | Master §8.1 "marj yetkisi" kontrolü kodda yok | P1 | O | Satış, Ayarlar |
| 13 | İade faturası için muhasebe fişi tüketicisi; mal iadesi ↔ iade faturası çapraz uyarısı | İade faturası olayını kimse dinlemiyor; iki iade kaydı birbirinden habersiz | P1 | O | Muhasebe, Satış, Lojistik |
| 14 | İptal etkisine "eşleşmemiş avans / dekont" kaynağını bağla | Karar (DEC-SAL-005) beş kategori sayıyor, biri kaynaksız | P1 | K | Finans, Satış |
| 15 | Kaynak planına serbest stok önerisi; termin – kaynak uygunluk denetimi | Öneri "kullanılamıyor"; termin denetimi yok | P1 | O | Stok, Satış |
| 16 | Kalite bloğu / karantina (sevke kapatma) | AT-035, AT-051 koşulamıyor | P1 | B | Stok, Üretim, Lojistik |
| 17 | Fatura – resmî belge tutar toleransını Ayarlar'a taşı | Bugün kodda sabit | P2 | K | Ayarlar, Satış |
| 18 | Grup içi işlem dökümü ve emsal fiyat raporu | 22 Kasım kararı; şirketler arası tek temsil bu olacak | P2 | O | Raporlar, Satış, Alış |
| 19 | e-İrsaliye / e-Fatura bağlantısı | Bugün numara ve PDF elle; karar gereği dış programda | P2 | B | Lojistik, Finans |

## 10. Doğrulanamayanlar

- Hiçbir test koşulmadı; "gerçek" denen halkalar kodun ve test kaynağının varlığına dayanır, yeşil olduğuna değil.
- Bütün gerçek sahiplerle (Finans limiti, Lojistik payları, Satış fatura kaydı) "limit içinde" sonucunun uçtan uca
  üretildiği bir test görmedim; Satış'ın gerçek-SQL risk kapısı testleri yalnız "limit yok" ve "kaynak yok" dallarını
  kapsıyor (`IT/SalesShipmentRiskGateSqlServerRelationalAcceptanceTests.cs`). Lojistik tarafındaki "limit içinde sevk"
  testinin kapıyı gerçek mi sahte mi bağladığına bakmadım.
- §6'daki çift alacak riski kod açıklamasına ve iki servisin birbirini okumamasına dayanıyor; veritabanında bunu
  engelleyen bir kısıt olup olmadığını taramadım.
- Ürün çalışma zamanı ayarı (`ProductRuntime:IssuerLocalUserId`) boşken sipariş satırının 503 döndüğü koddan okundu;
  test ortamında bu ayarın nasıl verileceği Ürün incelemesinin konusu.
- Tahsilatın çek/senet ve POS dallarının dağıtıma kadar aynı yolu izlediği varsayıldı; yalnız banka havalesi dalı okundu.
- Kur kaynağı (TCMB) varsayılan olarak kapalı görünüyor; dövizli sipariş ve faturada kurun elle girilip girilemediği
  Ayarlar incelemesine bırakıldı.
- Fason hizmet zinciri (müşteri malı, fason teslim, fason hizmet faturası) bu belgede izlenmedi.
- `docs/handoffs/SATIS_MODUL_BAGLANTILARI_v1.md` (2 Kasım) artık eski: orada "yok" görünen fatura → Finans alacak bağı,
  kredi limiti kaynağı, bağlı alış/üretim okuması ve Satış olay kutusu bugün kodda var.
