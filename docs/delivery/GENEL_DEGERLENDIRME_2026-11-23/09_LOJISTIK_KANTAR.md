# Lojistik & Kantar (Logistics modülü) — durum ve yapılacaklar

> Kapsam: yükleme talimatı, araç tartımı, fiilî yük, sevkiyat, teslim, irsaliye, müşteri iadesi, fason teslim,
> transfer çıkışı ve belgeleri, operasyon iş listeleri, mal kabulün lojistik tarafı.
> HEAD `ddc6cba`, 23 Kasım 2026. Yalnız kod ve belge okunarak hazırlandı; derleme ve test koşulmadı.
> Bu belge karar veya onay değildir.

## 1. Kısa sonuç

- **Satış sevk zinciri kodda uçtan uca yazılmış:** onaylı sipariş satırından yükleme talimatı açılır, talimat
  yayımlanır, yük kaydedilir, araç iki tartımla tartılır, sevk kesinleştirilir (stok aynı işlemde düşer), teslim
  kaydedilir, irsaliye taslağı hazırlanıp onaylanır, e-İrsaliye numarası elle girilir, PDF alınır. Hepsinin ekranı
  var ve menüye bağlı.
- **Bugün hiçbir kullanıcı bunları yapamaz:** Lojistik'in 22 izninin hiçbiri başlangıç rollerinde yok
  (`identity-seed.json` içinde "logistics" araması 0 sonuç). Rol ve yetki ataması insan kapısıdır.
- **Ekranlar kimlik numarası gösteriyor:** talimat listesinde talimat ve müşteri, ayrıntıda sipariş, ürün ve birim
  uzun teknik kimlik (GUID) olarak görünüyor. Talimatın okunur bir numarası yok. Sahada kullanılamaz.
- **Araç, plaka, şoför, nakliyeci, kapı, navlun, rota hiç yok.** Tartım kaydı hangi araca ait olduğunu bilmiyor.
- **Fason teslim ve plan üstü sevk onayı ekranları yazılmış ama "yayımlanmamış" işaretli;** yetki verilse bile
  menüde çıkmaz.
- **Kantar farkı ayarı bağlanmamış:** %0,5 oranını ve %1/%2/%3 bantlarını Ayarlar'dan okuyan parça yerine
  "okunamadı" cevabı veren geçici parça kayıtlı. Her kantar farkı kaydı onaya düşer, bant hiç değerlendirilmez.
- **Sevk geri alınamıyor.** Yanlış kesinleşen sevk için iptal/ters kayıt komutu yok; tek yol müşteri iadesi.
- **Mal kabul Satın Alma'dadır;** Lojistik yalnız "bekleyen alış satırları" listesini gösterir. Gelen araç için
  kantar kaydı yok; sistem-of-record matrisindeki "hedef" bağ kurulmamış.
- Lojistik → Satış olay akışı (sevk kesinleşti bildirimi) yazılmış ama olay dağıtıcısı kapalı
  (`Work:OutboxDispatcher:Enabled`); açılması insan kapısıdır.

## 2. Hedef: bu modülden ne isteniyor

**Master rapor §7.8:** Akış "sevk edilebilir sipariş → yükleme talimatı → araç/sürücü/nokta → fiilî etiket/miktar →
kantar → fark kontrolü → irsaliye taslağı → onay/kesim". Talimat plandır, stok düşmez. Bir yükleme birden çok sipariş
ve yükleme noktası taşıyabilir; bir irsaliye birden çok uyumlu siparişi kapsayabilir. Plan, fiilî yük, teorik ağırlık
ve kantar farkı ayrı tutulur. Dış fason gidiş/dönüş mülkiyet değiştirmez. Depo transferi iki bacaklıdır. Bağlamsız
"Yeni İrsaliye" ekranı olmaz.

**Master §27.8 ekran kataloğu (12 ekran):** L-01 Sevk Edilebilir Siparişler · L-02 Yükleme Talimatları · L-03
Yeni/Düzenle Talimat · L-04 Araç/Sürücü/Kapı · L-05 Fiilî Yükleme · L-06 Kantar Oturumu · L-07 Sevkiyat Gerçekleşmesi ·
L-08 Satış İrsaliyesi Hazırlığı · L-09 Beklenen Alış/Kapı Kabulü · L-10 Mal Kabulü · L-11 Transfer/Yolda/Hedef Kabul ·
L-12 Teslimat ve Farklar.

**Master §28.12 formüller:** net tartım = |tartım2 − tartım1| ve brüt/dara yönü (WGT-001); kantar farkı = kantar kg −
beklenen kg (WGT-002); mal kabul farkları (GR-001…003).

**Master §29 senaryoları:** kantar neti iki tartım ve yönle tutarlı (AT-053); fiilî yüklenen etiketler tahsis ve
planla uzlaşır (AT-054); aynı sevk iki kez irsaliye/faturaya bağlanamaz (AT-055); satış ve alış işleri operasyon
listesinde karışmaz (AT-025); stok satırından "Yükleme Talimatı" doğru bağlamla açılır (AT-063).

**Bağlayıcı kararlar (en yeni geçerli):**

| Konu | Karar özeti | Kaynak |
|---|---|---|
| Menü | "Stok & Lojistik" tek başlık (6. sıra); stok, tahsis, yükleme, sevkiyat, kantar, mal kabul burada. Master §4'teki ayrı "Lojistik & Kantar" (10. sıra) başlığının yerine geçer. | KLN-D1 |
| Kantar | Ağırlık elle girilir; cihaz bağlantısı kararı yok. Tolerans değerleri kısmen açık. | OPEN-003 (kapandı), OPEN-004 (kısmen) |
| Talimat iptali | Sevk kesinleşmediyse yetkili gerekçeyle iptal eder; tartı silinmez; kesinleşmiş sevkte iptal yok. | LOJ-D6…D8 |
| İptalde tahsis | İptal fiziksel tahsisi bırakmaz; tahsis siparişte kalır, bırakma ayrı işlemdir. | LOJ-D16, LOJ-D18 |
| İndirme tartısı | İptal edilen yüklü talimatta indirme tartısı elle kaydedilir; fark uyarıdır. | LOJ-D17 |
| Fason teslim | Ayrı sevk türü; mal yalnız sahibi cariye gider; satış sayılmaz; bedel teslim tartısı net kg'ına göre Satış'tan faturalanır; tartısız fason teslim kaydedilemez; satışla aynı risk kapısı. | LOJ-D1…D5, D9, D12, D14 |
| Tartı düzeltmesi | Araç çıktıktan sonra gerekçeli düzeltme kaydı; eski değer silinmez. | LOJ-D13 |
| Transfer "yola çıktı" | Çıkış tartısı ya da araç çıkışı anı; depo başına "kantardan geçer" ayarı. | LOJ-D10, LOJ-D11 |
| Olaylar | Lojistik sevk kesinleşmesini ve sevk iptalini olay olarak yayınlar; Satış dinler. | LOJ-D19 |
| Kantar ayarı okunamazsa | Sevk durmaz; sonuç "kontrol edilemedi" saklanır ve listelenir. | LOJ-D20 |
| Kantar farkı | Teorik–kantar bantları (%1/%2/%3) yalnız kaydedilir, sevki durdurmaz; sistem stokundan fazla çıkan kg için tek %0,5 oranı Ayarlar'da tutulur, üstü Satış Müdürü onayı. | SAT-D319, D324, D344 |
| Görev ayrımı | Sevki yapan kişi kendi fazla teslimini onaylayamaz; sevk eden bilinmiyorsa hiçbir fazla teslim onaylanmaz. | SAT-D216, SAT-D222 |
| İrsaliye kg dağılımı | Kesinleşmiş ETTN'li dış belge varsa onun dağılımı esas; yoksa teorik kg oranı, artık en ağır ilk satıra; birden çok irsaliyeden faturaya dağılım taşınır. | SAT-D421, D424, D425 |
| Plan üstü sevk | Plan miktarı değişmez; fazla miktar ayrı istisna kaydıyla, satış müdürü onayıyla. | SAT-D417 (W8) |
| Adet + kg | Adetle satılan üründe sevk adedi ve fiziksel kg birlikte taşınır. | KLN-D119 |

## 3. Bugün var olan

Seviye: 0 = yok, 1 = kod yazılmış (doğrulanmamış), 1-kısmi = yalnız bir bölümü var. Yollar `src/backend/` ve
`src/frontend/goymen-erp-web/src/` altındadır; uç noktaların tamamı `GoymenERP.Api/Endpoints/Logistics*.cs`
dosyalarındadır (46 uç nokta; sözleşmede 43 ayrı `/api/v1/logistics` yolu).

### 3.1 Yükleme talimatı ve fiilî yük

| Yetenek | Arka uç | Ekran | Menü | Test kaynağı | Seviye | Not / kanıt |
|---|---|---|---|---|---|---|
| Sevk edilebilir sipariş satırları listesi (L-01) | Var | Var | Var ("Operasyon işleri — Satış") | Var | 1 | `LogisticsOperationsWorkService.cs`, `logistics/OperationsWorkPage.tsx`. Fiyat göstermez. Ürün kodu ve adı, müşteri adı gösterir. |
| Talimat oluşturma (L-03) | Var | Var | Listeden açılır | Var | 1 | `LogisticsEndpoints.cs:41`, `InstructionCreatePage.tsx`. Tek müşteri, birden çok sipariş satırı. Stok etiketinden başlatma var (`capabilities.ts` `instructionCreateFromLabelPath`). |
| Talimat düzenleme | **Yok** | Yok | — | — | 0 | `ILogisticsInstructionService.cs` içinde güncelleme komutu yok; satır yalnız oluşturmada eklenir. Düzeltme yolu: iptal et, yeniden aç. |
| Talimat yayımlama (taslak → yayımlandı) | Var | Var | — | Var | 1 | `LogisticsEndpoints.cs:173` |
| Talimat listesi ve ayrıntısı (L-02) | Var | Var | Var ("Yükleme Talimatları") | Var | 1 | Liste yalnız duruma göre süzülür; arama yok. Kimlikler GUID olarak görünür (bkz. §5.3). |
| Talimat iptali (gerekçeli) | Var | Var | Ayrıntıda | Var | 1 | `LoadingInstruction.cs:218`, `InstructionCancelCommand.tsx`. Sevk varsa reddeder. |
| Fiilî yük kaydı (L-05) | Var | Var | Ayrıntıda | Var | 1-kısmi | `LogisticsEndpoints.cs:191`. Yük **miktar** olarak girilir; yükleme anında etiket okutulmaz. Etiket bağı sevk kesinleşirken tahsis üzerinden kurulur. |
| Plan üstü yük talebi ve onayı | Var | Var | Var ama **yayımlanmamış** | Var | 1-kısmi | `LogisticsEndpoints.PlanExcess.cs`, `PlanExcessApprovalPage.tsx`. `LogisticsShellCapabilityProvider.cs:76,80` `ImplementationAvailable: false`. |
| Satır blokesi (kalite/sahiplik) | Alan modeli var | — | — | Yalnız test verisi | 1-kısmi | `LoadingInstruction.BlockLine`; blokeyi koyan gerçek bir komut ya da uç nokta yok, yalnız test düzeneği (`Fixtures/LogisticsLineBlockFixtureStore.cs`). |

### 3.2 Araç tartımı (kantar)

| Yetenek | Arka uç | Ekran | Menü | Test kaynağı | Seviye | Not / kanıt |
|---|---|---|---|---|---|---|
| Dolu/boş iki tartım, yön, net ve fark (L-06) | Var | Var | Ayrıntıda | Var | 1 | `Domain/Loading/VehicleWeighing.cs` (`WeighbridgeCalculator`), `WeighingCommand.tsx`. Elle giriş. Talimat başına tek kayıt. Yön tutarsızsa reddeder (AT-053). |
| İndirme tartısı (iptal sonrası) | Var | Var | Ayrıntıda | Var | 1 | `UnloadWeighing.cs`, `UnloadWeighingCommand.tsx`. Kaydı olmayan iptalde "indirme kaydı yok" işareti. |
| Tartı düzeltmesi (gerekçeli) | Yalnız fason teslimde | Yalnız fason teslimde | — | Var | 1-kısmi | Alan modeli genel (`LoadingInstruction.CorrectWeighing`), ama uç nokta yalnız `LogisticsSubcontractDeliveryEndpoints.cs:41`. Satış talimatında düzeltme ucu yok. |
| Satışta kantar farkı kaydı ve onayı | Var | Var | İrsaliye bölümünde | Var | 1-kısmi | `LogisticsWeighingDifferenceService.cs`, `DispatchKilogramsPanel.tsx`. **Ayar okuyucusu sahte:** `LogisticsModuleExtensions.cs:116` `UnavailableLogisticsWeighingDifferencePolicyReadPort`; gerçek uyarlayıcı kayıtlı değil. |
| Kantar cihazı bağlantısı | Yok | Yok | — | — | 0 | `SerialPort`, cihaz sürücüsü araması 0 sonuç. Karar gereği elle giriş. |

### 3.3 Sevkiyat, teslim, irsaliye

| Yetenek | Arka uç | Ekran | Menü | Test kaynağı | Seviye | Not / kanıt |
|---|---|---|---|---|---|---|
| Sevk kesinleştirme (stok çıkışı ile tek işlem) | Var | Var | Ayrıntıda | Var | 1 | `LogisticsEndpoints.cs:353`, `LogisticsRuntime/LogisticsRealizationTransactionRunner.cs`, `RealizationCommands.tsx`. Müşteri risk kapısı, plan sınırı, fiziksel stok ve tahsis denetlenir. |
| Sevk iptali / ters kayıt | **Yok** | Yok | — | — | 0 | `CancelShipment`, `ShipmentCancel`, `ReverseShipment` araması 0 sonuç. `LogisticsDefiniteShipmentOutboxV1.cs`: "The shipment cancel (reversal) command is not decided". |
| Teslim kaydı (kaynak sevke bağlı) | Var | Var | Ayrıntıda | Var | 1-kısmi | `LogisticsEndpoints.cs:389`. Yalnız miktar; eksik/hasar/ret sınıfı yok (`Damage`, `Hasar` araması 0). Teslim tesellüm belgesi yok. |
| İrsaliye taslağı (sevklerden) | Var | Var | Ayrıntıda | Var | 1 | `LoadingInstruction.cs:502`, `DispatchNoteCommand.tsx`. Bir sevk en çok bir geçerli irsaliyede (AT-055). |
| İrsaliye onayı ve sistem numarası (IRS-000001) | Var | Var | Ayrıntıda | Var | 1 | Şirket başına boşluksuz sıra, onayla aynı kayıtta. |
| İrsaliye iptali (gerekçeli) | Var | Var | Ayrıntıda | Var | 1 | Sevkler yeni irsaliye için serbest kalır. |
| e-İrsaliye numarası kaydı | Var | Var | Ayrıntıda | Var | 1 | `LoadingInstruction.cs:637`. Biçim denetimi (3 harf/rakam + 13 rakam), bir kez yazılır, şirket içinde tekil. |
| İrsaliye PDF | Var | Var | İrsaliye bölümünde | Var | 1 | `LogisticsDispatchPdfComposer.cs`, `DispatchNotePdfButton.tsx`. Fiyatsız; ürün kodu/adı, miktar, kg. Yalnız onaylı irsaliye. Plaka/şoför alanı yok. |
| Siparişten irsaliyeleri görme | Var | Var | Satış sipariş ayrıntısı | Var | 1 | `LogisticsEndpoints.cs:424`, `sales/SalesDraftDispatchNotes.tsx`; Satış menüsünde "Sevkiyat / İrsaliyeler". |
| Müşteri iadesi (talep, onay, ret; stok girişi ile) | Var | Var | Talimat ayrıntısında | Var | 1 | `LogisticsCustomerReturnEndpoints.cs`, `LogisticsRuntime/LogisticsCustomerReturnRuntime.cs`, `CustomerReturnPanel.tsx`. Onay, stok iade hareketini aynı işlemde yazar. |

### 3.4 Fason teslim, transfer, mal kabul

| Yetenek | Arka uç | Ekran | Menü | Test kaynağı | Seviye | Not / kanıt |
|---|---|---|---|---|---|---|
| Fason teslim (oluştur, yükle, tart, sevk et, tartı düzelt) | Var | Var | Var ama **yayımlanmamış** | Var | 1-kısmi | `LogisticsSubcontractDeliveryEndpoints.cs`, `SubcontractDeliveryPages.tsx`, `shell/logisticsSubcontractRoutes.tsx`. `LogisticsShellCapabilityProvider.Subcontract.cs:42` `ImplementationAvailable: false` (8 yüzey). |
| Transfer çıkış belgesi (taslak, kesinleştir, iptal) | Var | Var (Stok ekranında) | Stok transferinden | Var | 1 | `LogisticsTransferOutgoingDocumentEndpoints.cs`, `inventory/transferDispatchApi.ts`. |
| Transfer çıkış tartısı / araç çıkışı | Var | Var | Var ("Transfer çıkışları") | Var | 1 | `LogisticsTransferDepartureEndpoints.cs`, `TransferDeparturePage.tsx`. Depo ayarı: `SettingsWarehouseTransferScaleEndpoints.cs`, `settings/warehouseTransferScaleApi.ts`. |
| Transferde e-İrsaliye / GİB gönderim kanıtı | Yalnız sözleşme | — | — | — | 0 | `ILogisticsTransferEDispatchEvidenceSourceV1` için kayıt araması 0 sonuç; bağ "Unknown/NotLinked" kalır. |
| Beklenen alış satırları listesi (L-09) | Var | Var | Var ("Operasyon işleri — Satın alma") | Var | 1-kısmi | Yalnız okuma. Ürün kısa kimlikle görünür (`OperationsWorkPage.tsx:367` `shortRef(row.productId)`). Satırdan kabul işlemine değil sipariş ayrıntısına gider. |
| Mal kabul (L-10) | Satın Alma'da | Satın Alma'da | Satın Alma'da | — | — | `PurchasingEndpoints.GoodsReceiptBatches.cs`. Lojistik'te mal kabul kodu yok. |
| Gelen araç kantarı / kapı kabulü | Yok | Yok | — | — | 0 | Satın Alma modülünde iki tartımlı kantar kaydı araması (`Gross`, `Tare`, `Kantar`) kabul tarafında sonuç vermedi; ölçülen kg doğrudan kabul miktarı olarak girilir. |

### 3.5 Hiç olmayanlar (arananlar)

| Aranan | Sonuç |
|---|---|
| Araç plakası (`Plate`, `Plaka`) | Bütün arka uç modüllerinde 0 dosya |
| Şoför (`Driver`, `Şoför`, `Sürücü`) | Yalnız bir açıklama satırı ("L-04 başka ekrandır"); alan yok |
| Nakliyeci (`Carrier`, `Nakliyeci`) | 0 |
| Navlun (`Freight`, `Navlun`) | 0 |
| Yükleme noktası (`LoadingPoint`) | 0 |
| Rota / durak / çoklu teslimat | Lojistik'te 0 (eşleşmeler ekran yolu "route" kelimesidir) |
| Kapı giriş-çıkış kaydı | Yalnız transferde "araç çıkışı" anı var; genel kapı kaydı yok |
| Teslim tesellüm belgesi (`ProofOfDelivery`, `Tesellüm`) | 0 |
| Barkod okutma (`Barcode`, `Scan`) Lojistik'te | 0 (yalnız stok etiketi PDF'inde barkod basımı var) |
| Araç doldurma / sevk planlama | 0 |

**Test kaynağı (koordinatör için):** `tests/GoymenERP.Logistics.UnitTests` 38 dosya, 334 test; 
`tests/GoymenERP.Api.IntegrationTests` içinde adı Logistics geçen 33 dosya, 153 test (gerçek SQL kabul testleri dahil);
`tools/relational_inventory/logistics.txt` 88 satır; ön yüzde `logistics/` altında 29 test dosyası, 233 test.
Kaynaklar var; koşulmadı.

## 4. Kullanılabilirlik engelleri

1. **Rol ve yetki yok.** `ErpPermissions.cs` içinde 22 Lojistik izni kayıtlı (satır 1128–1221). Başlangıç
   rollerinin beşi de organizasyon/güvenlik rolü; hiçbirinde Lojistik izni yok. Sevkiyatçı, kantar operatörü,
   irsaliye onaylayan, satış müdürü (plan üstü ve kantar farkı onayı) rolleri tanımlanmadan menü boş görünür.
   Sevk kesinleştirme ayrıca Stok'un `inventory.physical-allocation.write` iznini ister.
2. **Yayımlanmamış ekranlar.** Fason teslimin 8 yüzeyi ile plan üstü sevk hazırlama ve onay yüzeyi
   `ImplementationAvailable: false`. `GoymenERP.Api/Shell/ShellCapabilityService.cs:76` yalnız yayımlanmış olanları
   kullanıcıya verir. Menü satırları (`shell/navigation.ts:513,515`) tanımlı ama görünmez.
3. **Kantar farkı ayarı sahte sağlayıcıda.** %0,5 oranı ve bantlar okunamadığı için her kantar farkı kaydı onay
   bekler ve bant "değerlendirilmedi" kalır. Ayarlar modülünde kantar toleransı alanı var
   (`SettingsUnitConversionPoliciesV1.cs` `WeighbridgeTolerancePercent`), ama Lojistik'in okuma kapısına bağlı değil;
   %0,5 oranının Ayarlar'da tutulduğu bir alan bulunamadı.
4. **Olay dağıtıcısı kapalı.** Sevk kesinleşince yazılan olay (`logistics.definite-shipment.recorded.v1`) Satış'a
   ancak `Work:OutboxDispatcher:Enabled` açılırsa ulaşır (`Program.cs:501`). Açılması insan kapısıdır.
5. **Ana veri bağımlılıkları.** Talimat için: onaylı satış siparişi, carinin şirket hesabı, ürünün birim profili,
   fiziksel tahsis (sevk kesinleşirken zorunlu), müşteri kredi limiti (risk kapısı; limit tanımsızsa sevk durur:
   `LogisticsShipmentRiskGateCodesV1.CreditLimitNotSet`). Bunların kurulum durumu ilgili modül raporlarındadır.
6. **Tartım toleransı değerleri kesin değil.** %1/%2/%3 bantları geçici varsayımdır (OPEN-004 kısmen açık).

## 5. Eksikler

### 5.1 Sizin isteklerinize göre

| # | Hedef | Durum |
|---|---|---|
| 1 | Araç / sürücü / kapı (master §7.8 akışı, L-02 "plan, araç, durak", L-04) | Hiç yok. |
| 2 | Bir yükleme birden çok yükleme noktası taşır (§7.8) | Yok. Birden çok sipariş var; birden çok müşteri ve yükleme noktası yok (talimat tek müşterilidir). |
| 3 | Talimat düzenleme (L-03 "Yeni/Düzenle") | Yok; iptal + yeniden açma. |
| 4 | Fiilî yüklemede etiket (L-05, AT-054) | Kısmi. Yük miktar olarak girilir; etiket sevk kesinleşirken tahsisle bağlanır. Yüklenen etiketlerin listesi ve planla uzlaştırma ekranı yok. |
| 5 | Sevk iptali olayı (LOJ-D19) | Olay adı tanımlı, üreten komut yok. |
| 6 | Kantar ayarı okunamadığında "kontrol edilemedi" listesi (LOJ-D20) | Kısmi. Bant "değerlendirilmedi" saklanıyor; sonradan incelenecek ayrı liste bulunamadı. |
| 7 | Satış talimatında tartı düzeltmesi (LOJ-D13) | Yalnız fason teslimde uç nokta var. |
| 8 | Fason teslim (LOJ-D1…D14) | Kod var, yayımlanmamış. |
| 9 | Plan üstü sevk (SAT-D417) | Kod var, yayımlanmamış. |
| 10 | Kantar farkı oranı ve bantları Ayarlar'dan (SAT-D344, SAT-D324) | Bağ yok (sahte sağlayıcı). |
| 11 | Beklenen alış / kapı kabulü ve mal kabulde kantar (L-09, L-10; sistem-of-record matrisi "hedef") | Liste var; kapı ve kantar kaydı yok. |
| 12 | Teslimat ve farklar: eksik, fazla, hasar, ret (L-12) | Yalnız teslim miktarı ve müşteri iadesi var. |
| 13 | Transferde e-İrsaliye/GİB kanıtı (LOJ-D10, LOJ-D11) | Sözleşme var, kaynak yok (kaynak sorusu açık: bkz. §7). |
| 14 | Menüde "Tartım" ve "Mal Kabul" girişleri (§4, KLN-D1) | Ayrı giriş yok; tartım talimat ayrıntısının içinde, mal kabul Satın Alma siparişinin içinde. |

### 5.2 Bir ERP'de olması gerekene göre

- **Araç ve taşıyıcı ana verisi:** plaka, dorse, şoför adı/TC/telefon, nakliye firması (cari bağı). Türkiye'de
  irsaliyede plaka ve şoför bilgisi beklenir; e-İrsaliye dışarıda kesilse de ERP kaydı ile eşleşmesi için gerekir.
- **Navlun / nakliye maliyeti:** sevk başına nakliye bedeli, nakliyeci carisine borç, satışa ya da maliyete
  yansıtma. Alış tarafında "masraf satırı" kararı var (ALS-D23); satış sevki için karşılığı yok.
- **Kantar fişi:** tartım kaydının numaralı, yazdırılabilir fişi; tartıma plaka ve fiş numarası bağlama.
- **Kantar cihazı bağlantısı:** göstergeden ağırlığı otomatik alma. Bugün elle giriş karardır; hata ve suistimal
  riskini azaltmak için sonraki adım.
- **Kapı giriş-çıkış defteri:** aracın tesise giriş ve çıkış saati, bekleme süresi.
- **Sevk planlama:** araç kapasitesine göre yük birleştirme, günlük sevk programı, rota ve çoklu teslimat noktası.
- **Teslim kanıtı:** imzalı irsaliye/tesellüm belgesinin taranıp sevke bağlanması (Belgeler modülüyle).
- **Saha kullanımı:** barkod/QR okutarak yükleme, tablet/el terminali ekranı. Hedef cihaz listesi kararı var
  (DEC-TEC-010); Lojistik ekranlarında buna özel düzen bulunamadı.
- **e-İrsaliye uyum denetimi:** dışarıda kesilen belgenin tarihi, alıcısı ve miktarının ERP irsaliyesiyle
  karşılaştırılması. Bugün yalnız numara biçimi ve tekrarı denetlenir.
- **Sevk raporları:** günlük sevk, bekleyen yük, kantar farkı raporu, dışa aktarım.

### 5.3 Ekran ve kullanım kolaylığı

- **Teknik kimlikler:** talimat listesinde talimat ve müşteri GUID (`InstructionListPage.tsx:160,177`); ayrıntıda
  sipariş, ürün ve birim GUID (`InstructionDetailPage.tsx:499–502`). Talimat numarası alanı sözleşmede yok
  (`LoadingInstructionSummaryView`). Operasyon iş listesi (satış) ise adları doğru gösteriyor; aynı veri talimat
  ekranlarına taşınmamış.
- **Arama ve süzme:** talimat listesinde yalnız durum süzgeci; müşteri, sipariş, tarih, plaka araması yok.
- **Çıktı:** yalnız onaylı irsaliye PDF'i. Yükleme talimatı çıktısı (depoya verilecek kâğıt) ve kantar fişi yok.
- **Dışa aktarım / toplu işlem:** `csv`, `xlsx`, yazdırma araması `logistics/` altında 0 sonuç.
- **Tek sayfada çok iş:** yük, tartım, sevk, teslim, irsaliye, iade aynı ayrıntı sayfasında; master'daki ayrı
  L-05…L-08 çalışma alanları tek ekranda birleşmiş. İşlevsel, ama sahada uzun bir sayfa.
- **Durumlar:** yükleniyor, boş, yetkisiz, çakışma, çevrimdışı durumları ortak bileşenlerle ele alınmış
  (ör. `InstructionListPage.tsx`, `testHarness.tsx` ve ilgili testler); bu yön iyi.
- **e-İrsaliye numarası düzeltilemez:** bir kez yazılır; yanlış girilirse irsaliye iptal edilip yeniden
  hazırlanmalıdır.

## 6. Diğer modüllerle bağlantılar

### 6.1 Lojistik kimden ne alır

| Kimden | Ne | Mekanizma | Kanıt |
|---|---|---|---|
| Satış | Onaylı sipariş satırı, yükleme sınırı, satır durumu | Okuma kapısı (port) | `ISalesOrderLineReferencePortV1`, `LogisticsModuleExtensions.cs` |
| Satış | Müşteri risk kapısı ve tek sevk istisnası | Okuma kapısı | `ISalesShipmentRiskGateReadPortV1` (`SalesModuleExtensions.cs:133`) |
| Cari | Müşteri ve şirket hesabı, ad | Okuma kapısı | `Program.cs:415` `CounterpartyLogisticsPartyAccountPortV1` |
| Stok | Fiziksel tahsis, etiket, stok çıkışı | Ortak veritabanı işlemi | `LogisticsRealizationTransactionRunner.cs` |
| Stok | Transfer planı/sevki | Okuma kapısı | `LogisticsRuntime/InventoryTransferPolicyHostPorts.cs:17` |
| Stok | Serbest fiziksel kg (kantar farkı için) | Okuma kapısı | `LogisticsWeighingDifferenceRuntime.cs` `InventoryWeighingDifferenceStockAdapter` |
| Üretim | Fason sipariş bilgisi | Okuma kapısı | `Program.cs:477` |
| Satın Alma | Açık alış satırları (L-09 listesi) | Okuma kapısı | `LogisticsOperationsWorkService.cs` |
| Ürün | İrsaliye PDF'i için ürün kodu/adı | Okuma kapısı | `Program.cs:293` |
| Ayarlar | Birim profili; depo "kantardan geçer" ayarı | Okuma kapısı | `LoadingUnitVerifier.cs`, `SettingsWarehouseTransferScaleEndpoints.cs` |
| Ayarlar | Kantar farkı oranı ve bantları | **Bağlı değil** | `UnavailableLogisticsWeighingDifferencePolicyReadPort` |

### 6.2 Lojistik kime ne verir

| Kime | Ne | Mekanizma | Kanıt |
|---|---|---|---|
| Stok | Sevk çıkışı hareketi | Lojistik'in sevk komutu, Stok'un kendi yazıcısını aynı işlemde çağırır | `InventoryDefiniteShipmentParticipant.cs:213` (kaynak etiketi `logistics.definite-shipment`) |
| Stok | Müşteri iadesi girişi | Aynı yöntem | `LogisticsCustomerReturnRuntime.cs:136` |
| Satış | Teslim edilen/sevk edilen miktar ve **sevk eden kişi** | Okuma kapısı | `LogisticsSalesOrderDeliveryReadPort.cs` (`ShippedBy`) |
| Satış | Yüklenen ve planlanan miktar (iptal önizlemesi, risk kalanı) | Okuma kapısı | `LogisticsSalesOrderLoadingReadPort.cs`, `LogisticsSalesOrderPlannedQuantityReadPort.cs` |
| Satış | İrsaliye listesi, faturalanabilir sevkler | Okuma kapısı | `LogisticsSalesDispatchReadPorts.cs` |
| Satış | Kantar farkı kaydı ("satış ilerleyebilir mi") | Okuma kapısı | `Endpoints/SalesDispatchWeighingDifferenceHost.cs` |
| Satış | Fason teslim net kg (hizmet faturası için) | Okuma kapısı | `LogisticsSubcontractDeliveryNetWeightReadPort.cs` |
| Satış | Sevk kesinleşti olayı | Olay kutusu (outbox) → Satış gelen kutusu | `Outbox/LogisticsDefiniteShipmentOutboxEventSource.cs`, `SalesLogisticsShipmentInboxConsumer.cs` |
| Muhasebe | Faturalanmış sevk kanıtı (satılan malın maliyeti için) | Okuma kapısı | `LogisticsInvoicedShipmentProofReadPortV1.cs` |
| Stok | Transfer çıkış belgesi kanıtı | Okuma kapısı | `LogisticsRuntime/InventoryTransferOutgoingDocumentProofAdapter.cs` |

**Sevk çıkışını kim, ne zaman, kaç kez üretir:** Stok hareketi yalnız "sevk kesinleştir" komutunda, Lojistik kaydıyla
aynı veritabanı işleminde, Stok'un kendi yazıcısı tarafından bir kez yazılır. Talimat, yük ve tartım stok düşmez.
İrsaliye ve fatura ikinci bir stok hareketi üretmez. Bu, "aynı olay bir kez" kuralına uygundur.

**Sevk eden bilgisi (`ShippedBy`):** Lojistik her sevk payının kaydeden kişisini saklıyor ve Satış'a bildiriyor
(`LogisticsStore.cs:1051`, `LogisticsSalesOrderDeliveryReadPort.cs:126`). Satış fazla teslim onayında ve eksikle
satır kapatmada bu kişiyi denetliyor (`SalesOrderDraft.OverDeliveryApproval.cs:84–90`). SAT-D222'deki "Lojistik
bildirene kadar onay yok" bekleme durumu kod tarafında kapanmış görünüyor; kişi kaydı tutulmadan önce saklanmış eski
paylarda bilgi boş kalır ve o satırlar onaylanamaz.

### 6.3 Kopuk ya da eksik halkalar

1. **Lojistik → Finans/Muhasebe (navlun):** hiç yok. Nakliye bedeli, nakliyeci carisi, masraf kaydı tanımsız.
2. **Lojistik ↔ Satın Alma (kabul):** yalnız okuma listesi. Gelen araç tartımı ve kapı kaydı alış kabulüne bağlı
   değil; kabul kg'ı Satın Alma ekranında elle girilir. Sistem-of-record matrisi bunu "hedef" olarak yazıyor
   (`docs/domain/SISTEM_OF_RECORD_MATRISI.md` satır 12).
3. **Lojistik → Satış (olay):** dağıtıcı kapalıyken Satış, tek sevk istisnasının kullanıldığını öğrenmez. Miktar
   bilgisi okuma kapısından geldiği için sipariş ilerlemesi etkilenmez; etkilenen, istisna tüketimi ve ona bağlı
   sipariş durumudur.
4. **Sevk iptali:** komut olmadığı için Stok'ta ters hareket, Satış'ta iptal olayı ve irsaliye/fatura geri sarımı
   zinciri yok.
5. **Ayarlar → Lojistik (kantar farkı):** yukarıda; sahte sağlayıcı.
6. **e-İrsaliye:** ERP dışında kesiliyor. Bağ yalnız elle girilen numara. Uyumsuzluk yakalama: biçim, tekrar ve
   "bir kez yazılır" kuralı. Belge içeriğiyle (tarih, alıcı, miktar) karşılaştırma yok. Kantar farkı kaydında
   kesinleşmiş belge kanıtı (ETTN + Belgeler modülündeki dosya sürümü ve özeti) saklanabiliyor
   (`DispatchKilogramSnapshot.cs`); bu, kg dağılımı içindir, numara doğrulaması değildir.
7. **Çift kayıt riski:** görülmedi. İrsaliye numarası Lojistik'te, resmi fatura Finans/Satış'ta; sevk miktarı tek
   kaynaktan okunuyor.
8. **Veri sahipliği:** ihlal görülmedi. Lojistik başka modülün tablosuna yazmıyor; stok yazımı Stok'un kendi
   parçası üzerinden. `MODULE_DEPENDENCY_ALLOWLIST.json` içinde Lojistik için tek izinli bağımlılık denetim kaydıdır.

## 7. Açık kararlar

| Konu | Durum | Kullanımı engeller mi |
|---|---|---|
| Kantar tolerans değerleri (%1/%2/%3) nihai onayı (OPEN-004) | Kısmen açık; geçici varsayım | Hayır (fark sevki durdurmuyor) |
| Sevk iptali / ters kayıt kuralı | Karara bağlanmamış (kod notu) | Evet, hatalı sevk düzeltilemez |
| Transferde gerçek GİB gönderim/iptal kanıtının kaynağı (hangi program/entegratör) | Soru sorulmuş (`W8_KARAR_BEKLEYENLER.md`, STK-S8) | Yalnız e-İrsaliye'li transfer iptal penceresini |
| Araç sevkinde kullanılmayan ayırmanın bırakılması (STK-S3) | Cevaplanmış: ayrı komutla; uygulama durumu bu raporda doğrulanmadı | Hayır |
| Mal kabulde ana kaynak: kantar mı irsaliye mi (master §28.12 "P0 kararı") | Kantar elle girilir kararı var (OPEN-003); ürün/işlem bazlı kaynak seçimi ayrıca yazılı değil | Kısmen |
| Navlun, araç/şoför/nakliyeci kapsamı | Karar yok | Hayır (eksik kapsam) |
| Lojistik iş rolleri ve yetki ataması | İnsan kapısı | **Evet** |
| Olay dağıtıcısının açılması | İnsan kapısı | Bütünleşik kullanımda evet |
| %0,5 oranının Ayarlar'daki yeri | Karar var (SAT-D344), alan bulunamadı | Kantar farkı akışını |

## 8. Yapılacaklar (öncelik sırasıyla)

| # | İş | Neden | Öncelik | Büyüklük | Ön koşul |
|---|---|---|---|---|---|
| 1 | Lojistik iş rollerini tanımla ve 22 izni dağıt (sevkiyatçı, kantar, irsaliye onayı, iade kararı, satış müdürü onayları) | Bugün kimse Lojistik ekranı açamaz | P0 | K | Sizin kararınız (insan kapısı) |
| 2 | Talimat ekranlarında okunur bilgi: talimat numarası, müşteri adı, sipariş no, ürün kodu/adı, birim; alış iş listesinde ürün adı | GUID ile sahada çalışılamaz | P0 | O | — |
| 3 | Fason teslim ve plan üstü sevk ekranlarını tarayıcı kabulünden geçirip yayımla | Yetki verilse de menüde çıkmıyor | P0 | K–O | 1 |
| 4 | Olay dağıtıcısını test ortamında aç | Satış sevk istisnası tüketimini görmüyor | P0 | K | Sizin kararınız (insan kapısı) |
| 5 | Kantar farkı oranı ve bantlarını Ayarlar'dan okuyan gerçek bağlantıyı yaz; %0,5 için Ayarlar alanı | Her fark onaya düşüyor, bant değerlendirilmiyor | P0 | O | Ayarlar'da alan |
| 6 | Uçtan uca deneme: sipariş → talimat → yük → tartım → sevk → irsaliye → fatura, üç şirkette | Bütün zincirin birlikte çalıştığı bugüne dek kullanıcı rolüyle denenmedi | P0 | O | 1–5 |
| 7 | Talimata araç plakası, şoför ve nakliyeci alanı; irsaliye PDF'ine ve tartım kaydına yansıt | Master hedefi; tartım hangi araca ait belli değil | P1 | O | Kapsam kararı |
| 8 | Sevk iptali / ters kayıt komutu ve iptal olayı | Hatalı sevk düzeltilemiyor; LOJ-D19'un ikinci yarısı | P1 | B | Kural kararı |
| 9 | Talimat düzenleme (taslakta satır ekle/çıkar/miktar değiştir) | Master L-03 | P1 | O | — |
| 10 | Satış talimatında tartı düzeltmesi ucu ve ekranı | LOJ-D13 yalnız fasonda | P1 | K | — |
| 11 | Gelen araç kantarı ve kapı kabulü; alış kabulüne bağ | Master L-09/L-10; matris "hedef" | P1 | B | Kaynak kararı (kantar/irsaliye) |
| 12 | Fiilî yüklemede etiket seçimi/okutma ve planla uzlaştırma görünümü | AT-054 | P1 | O–B | — |
| 13 | Teslim farkları: eksik, hasar, ret sınıfları; tesellüm belgesi bağlama | Master L-12 | P1 | O | Belgeler modülü |
| 14 | Talimat listesinde arama/süzme; yükleme talimatı çıktısı; kantar fişi | Günlük kullanım | P1 | O | 2 |
| 15 | "Kontrol edilemedi" tartım listesi | LOJ-D20 | P1 | K | 5 |
| 16 | Transfer e-İrsaliye/GİB kanıt kaynağı | LOJ-D10/D11'in e-İrsaliye'li kolu | P1 | O | Kaynak cevabı |
| 17 | Navlun: sevk başına nakliye bedeli, nakliyeci carisi, masraf/muhasebe bağı | ERP gereği; karar yok | P2 | B | Karar |
| 18 | Çoklu müşteri/yükleme noktası, rota, sevk planlama | Master §7.8 "birden çok nokta" | P2 | B | 7 |
| 19 | Kantar cihazı bağlantısı; barkodla yükleme; tablet ekranı | Hata azaltma, saha hızı | P2 | B | Cihaz seçimi |
| 20 | e-İrsaliye içerik karşılaştırması (dış belge ↔ ERP irsaliyesi) | Uyumsuzluk bugün yalnız numarada yakalanır | P2 | O–B | Entegratör kararı |

## 9. Doğrulanamayanlar

- Testlerin ve CI'ın geçip geçmediği (koşulmadı; yalnız kaynakların varlığı sayıldı).
- Yeni rol tanımlayıp Lojistik izinlerini atayan bir yönetim ekranının bugün çalışıp çalışmadığı (Kimlik modülü
  raporunun konusu).
- Yayımlanmamış ekranların (fason teslim, plan üstü sevk) tarayıcıda gerçekten çalışıp çalışmadığı.
- Kantar farkı kaydının zorunlu mu isteğe bağlı mı olduğu ve onay bekleyen kaydın fatura adayını fiilen durdurup
  durdurmadığı; Satış tarafında `SaleMayProceed` okunuyor, etkisi Satış raporunda doğrulanmalı.
- "Araç sevkinde kullanılmayan ayırmayı bırakma" (STK-S3) komutunun bu HEAD'deki uygulama durumu.
- İade onayının Satış'taki müşteri iadesi ekranlarıyla (`sales.customer-returns.*`) aynı kaydı mı, ayrı bir kaydı mı
  yönettiği; iki tarafta da iade ekranı var, çakışma olup olmadığı incelenmedi.
- Satır blokesini (kalite/sahiplik) gerçek kullanımda neyin koyacağı; yalnız test düzeneği bulundu.
- Eski yük paylarında "sevk eden" bilgisinin boş kaldığı kayıt olup olmadığı (veriye bağlı).
- `docs/handoffs/TREN1_DEVIR/durum/W8_stok_lojistik.md` (799 satır) satır satır karşılaştırılmadı; yalnız Lojistik'i
  doğrudan ilgilendiren başlıklar koda karşı okundu.
