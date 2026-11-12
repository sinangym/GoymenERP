# Üretim emrinde düzeltme ve iptal — analiz ve yol haritası v1 (F-009, URT-D28)

Bu belge Sinan'ın 12 Kasım 2026 F-009 kararını (URT-D28,
`docs/decisions/SINAN_KARARLARI_2026-11-12_URETIM_EMRI_DUZELTME.md`) bugünkü kodla karşılaştırır. Sinan'ın istediği
gibi uygulamadan önce yazılmıştır. Kod gerçekleri `origin/main` `58b96aa28` üzerinden okunmuştur. Kod bu belge merge
edilmeden başlamaz.

## 1. Kararın kurallara çevrilmesi

| Kural | URT-D28 maddesi | Anlamı |
|---|---|---|
| R1 Sevk kilidi | (a) | Bir satırın çıktısından sevk edilen kg geri alınamaz. Satırın çıktısı bu miktarın altına inemez. |
| R2 Serbest kısmın düzeltilmesi | (b) | Sevk edilmemiş ve stokta hâlâ duran çıktı kg'ı ters kayıtla geri alınabilir, gerekirse doğru miktarla yeniden beyan edilir. |
| R3 Satırın tamamen geri alınması | (b) | Satırda hiç sevk yoksa satırın bütün çıktıları ters kayıtla geri alınır, sonra hammadde iadeyle eski miktarına döner. |
| R4 Tamamlama | (c) | Düzeltmeler bitince emir bugünkü kapanış kurallarıyla kapatılır (kütle dengesi, fire üst sınırı, tolerans). |
| R5 İptal | (d) | Emirde hiç sevk yoksa ve bütün çıktılar geri alınıp bütün hammadde iade edildiyse emir **İptal** durumuna geçer. |
| R6 Silme yok | uygulama | Hiçbir olgu ve stok hareketi fiziksel olarak silinmez. Geri alma, eskisine bağlı yeni bir ters olgu ve ters stok hareketidir (AT-052, Stok T13). |

## 2. Bugünkü kod (kanıt)

- **Emir durumu:** `…Manufacturing.Domain/Orders/OrderStatus.cs` yalnız `Open`, `Closed`. İptal durumu yalnız planda var
  (`Plans/ProductionPlan.cs` `PlanStatus.Cancelled`).
- **Emir olguları:** `ProductionOrder` girdi çıkışı (`InputIssue`), iade (`InputReturn`), çıktı beyanı
  (`OutputDeclaration`: iyi ürün, 2. kalite, kullanılabilir artık, hurda, işlem kaybı) ve yarı mamul ölçümü tutar. Her
  toplam bu olgulardan türetilir. Kütle dengesi rulo başınadır (`MassBalance.cs`).
- **Düzeltme ve iptal yok:** Hiçbir olgu için ters olgu, hiçbir emir için iptal komutu yok. Plan dosyası bunu API-25
  (`POST orders/{id}/facts/{factId}/correction`, `manufacturing.facts.correct`, AT-052, K3) olarak listeler, yapılmadı.
- **Stok tarafı:** `StockMovementKind` = Receipt, Issue, Block, Release, Return, CountAdjustment, Scrap,
  TransferDispatch, TransferAccept. **Ters hareket türü yok** (Stok README: "reversal (T13) bu dilimde değil").
  Üretim çıktısı yeni bir etiket ve `Receipt` hareketi oluşturur (`manufacturing.output-declaration`). Girdi iadesi
  aynı etikete `Return` hareketi yazar.
- **Etiket durumu:** Etiket yaşam döngüsü enum'u yok. "Sevk edildi", "tüketildi" ve "transfer edildi" yalnız
  hareketlerden ve pozisyon miktarından türetilebilir. Ayırma `StockAllocation` (`Active/Consumed/Released`).
- **Sevk:** Lojistik kesin sevki, Stok'a `logistics.definite-shipment` kaynaklı `Issue` hareketi yazar
  (`InventoryDefiniteShipmentParticipant.cs`). Satış okuma portları sipariş satırı başınadır, etiket taşımaz. **"Bu
  etiketten kaç kg sevk edildi" sorusunu cevaplayan sürümlü bir port yok.** Stok içinde emsal var:
  `InventoryCustomerReturnParticipant` aynı kaynak türüyle eşleşir.
- **Açık bir boşluk:** `ProductionOrder.ReturnUnusedInput` iadeyi yalnız çıkış yapılan kütleyle sınırlar, beyan
  edilmiş çıktıyı hesaba katmaz. Çıktısı beyan edilmiş bir rulonun girdisi iade edilebilir. Kapanış bunu açıklanamayan
  fark olarak reddeder ama iade anında ret yoktur. R3 sırası bu kuralı gerektirir (§5 D4).

## 3. "Satır" neye karşılık gelir

Sinan'ın sözünde satır hem tüketimi hem çıktıyı taşır. Bizim modelde bu, **bir girdi rulosunun gerçekleştirmesidir**:
o ruloya ait çıkış, iade ve o rulodan beyan edilmiş çıktılar (kütle dengesi de bu birimde tutulur). Satırı
"güncellemek" o rulonun çıktısını ters kayıtla azaltmak, gerekirse doğru miktarla yeniden beyan etmektir. Satırı
"silmek" o rulonun bütün çıktılarını geri almak ve girdisini iade etmektir. Bu okuma §9 N1'de Sinan'a teyit için
sorulur. Teyit gelene kadar ilk dilimler bu okumayla yazılır, çünkü bir girdi rulosunun silinmesi dışında başka bir
okuma veri modelini değiştirmez.

## 4. Sorulara cevaplar

### 4.1 Sevk edilen miktar nereden bulunur

Sevk Lojistik'in kaydıdır ama stok etkisi Stok'ta `logistics.definite-shipment` kaynaklı `Issue` hareketi olarak
durur. Üretim başka modülün tablosunu okumaz. Bu yüzden **Stok'un sahip olduğu yeni bir sürümlü okuma portu**
gerekir: `IInventoryLabelDispositionReadPortV1` (öneri adı). Bir çıktı etiketi için şunları döner:

- etiketin üretimden geldiği kg (çıktı `Receipt`),
- kesin sevkle çıkan kg (müşteri iadesi düşülmeden; iade ayrı alan),
- başka bir üretim emrine çıkışla tüketilen kg,
- transferle gönderilen kg,
- etiketin çıktının ilk pozisyonunda hâlâ serbest ve ayrılmamış duran kg,
- okuma durumu (`Value / Unreadable`). Okunamazsa düzeltme reddedilir (fail-closed).

Port Stok'un işidir (Stok şeridi, K3: çapraz modül okuma). Üretim yalnız tüketir. İstek Stok oturumuna iletilir.

### 4.2 Satırın alt sınırı

Bir çıktı etiketinden geri alınabilecek kg = **ilk pozisyonunda serbest ve ayrılmamış duran kg**. Sevk edilmiş,
başka emre çıkılmış, transfer edilmiş veya ayrılmış kg geri alınamaz. Satırın alt sınırı = o rulonun çıktı
etiketlerindeki bu kilitli kg toplamıdır. Sinan yalnız sevki saydı. Başka emre çıkılmış ve transfer edilmiş kg
fiziksel olarak da artık o pozisyonda olmadığı için geri alınamaz. Ayrılmış kg için önce ayırma serbest bırakılmalıdır.
Bu ayrım §9 N2'de teyit için sorulur. Kod o zamana kadar fail-closed kalır: kilitli sayar.

### 4.3 Sevk edilmemiş çıktının geri alınması

1. Üretim yeni bir **çıktı ters olgusu** yazar: kimlik, geri aldığı çıktı beyanının kimliği, kg, gerekçe, kullanıcı,
   zaman. Eski beyan durur. Net çıktı = beyan − ters.
2. Aynı işlemde Stok, çıktı etiketinden **ters hareket** yazar (yeni hareket türü; kaynak türü
   `manufacturing.output-reversal`, eski `Receipt` hareketine bağlı). Kg, etiketin serbest kg'ını aşarsa Stok reddeder.
3. Doğru miktar yeniden beyan edilecekse bu, bugünkü çıktı beyanı komutuyla yeni bir etiketle yapılır. Aynı etiketi
   yeniden kullanmak istenirse bu ayrı bir karardır, ilk dilimde yoktur.
4. Hurda da etiketle stoğa girdiği için (URT-D8) aynı yolla geri alınır. İşlem kaybı stokta değildir. Onun ters olgusu
   yalnız Üretim'dedir.
5. Yarı mamul ölçümü bir anlık ölçümdür. Düzeltmesi yeni bir ölçümle yapılır; ters olguya gerek yoktur.

### 4.4 Hammaddenin eski miktarına dönmesi

Bugünkü iade yolu (`ReturnInventoryInputAsync`, Stok `Return` hareketi, aynı etiket ve pozisyon) bu işi yapar. Sıra
şudur: önce rulonun çıktıları geri alınır, sonra girdi iade edilir. İade sınırı "çıkış − iade − net çıktı − yarı
mamul" olacak şekilde sıkılaştırılır (§2 boşluğu). Böylece çıktısı duran bir rulonun girdisi iade edilemez.

### 4.5 Etiket stokta değilse ret

Çıktı etiketinin serbest kg'ı istenen ters kg'ından azsa komut ret döner. Ret kodu sebebi söyler (sevk edildi, başka
emre çıkıldı, transfer edildi, ayrıldı, okunamadı). Kodlar Üretim'in problem kodlarına eklenir. Stok'un ret kodları
eşlenir.

### 4.6 Emrin tamamlanma koşulu

Değişmez: bugünkü kapanış (`CloseOrderAsync`) net çıktılarla çalışır. Kütle dengesi ve fire üst sınırı ters olgular
düşüldükten sonra değerlendirilir. **Kapalı emirde düzeltme ve iptal reddedilir** (F-009 kabul satırı). Kapalı emrin
yeniden açılması bu kararın içinde değildir (§9 N3).

### 4.7 Hiç sevk yokken iptal

Ön koşullar: emir açık; hiçbir çıktı etiketinde kilitli kg yok; her çıktı geri alınmış (net çıktı 0); her girdi iade
edilmiş (net çıkış 0); yarı mamul 0. Bu durumda **İptal** komutu emri `Cancelled` durumuna geçirir, gerekçe ve kullanıcı
kaydedilir, plan ayırmasının kalanı serbest bırakılır. İptal hiçbir şeyi otomatik geri almaz. Kullanıcı önce satırları
geri alır, sonra iptal eder. Bu, Sinan'ın "hammadde eski miktarına ulaşınca o üretim emri silinebilir" sırasıdır. Emir
ve olguları silinmez. Ekranda iptal edilmiş emir ayrı süzgeçle görünür. İptal edilen emrin planına ne olacağı §9
N4'tür.

### 4.8 Ortalama maliyete etkisi (SAT-D305 / SAT-D307)

- Üretim alış maliyeti yalnız **kapalı** emirde hesaplanır (Stok cevabı 2). Düzeltme ve iptal yalnız **açık** emirde
  yapılır. Bu yüzden geri alınan çıktı henüz bir maliyetle ortalamaya girmemiştir. Ters hareketin ortalamaya etkisi
  yoktur. Üretim alışını ortalamaya hangi anda ve hangi tutarla aldığı Stok'un kararıdır; Stok bunu ters hareket
  diliminde doğrular.
- Girdi iadesi bugünkü kurala bağlıdır: iade, bağlı olduğu çıkışın damgalı maliyetiyle girer (Stok cevabı 3,
  `OPEN-INV-AVGCOST-RETURN`).
- Hesaplayıcı (SAT-D307) net çıktıyı ve net iadeyi kullanır. Ters olgular girdi listesine net olarak yansır.

## 5. Yol haritası (dilim sırası)

| Dilim | Şerit | İçerik | K3 noktası |
|---|---|---|---|
| D0 | Üretim | Bu belge ve karar dosyası (docs PR) | — |
| D1 | Stok | Ters hareket türü (`manufacturing.output-reversal`) ve `IInventoryLabelDispositionReadPortV1` | Migration (hareket türü kontrolü), çapraz modül port |
| D2 | Üretim | Domain: çıktı ters olgusu, net çıktı, kütle dengesinde ters olgu, kapalı emirde ret | Migration (yeni tablo), forward-fix Down |
| D3 | Üretim | Uygulama + host + uç: API-25 çıktı düzeltmesi; `manufacturing.facts.correct` yalnız kayıt | Komut iskeleti, yetki adı, idempotency, Stok katılımcısıyla tek işlem |
| D4 | Üretim | İade sınırının net çıktıyla sıkılaştırılması (§4.4) | Kural değişikliği (para/stok) |
| D5 | Üretim | `OrderStatus.Cancelled`, iptal komutu, `manufacturing.orders.cancel` yalnız kayıt, ayırma serbest bırakma sebebi `OrderCancelled` | Migration (durum kontrolü genişler), Stok ayırma portu sebep listesi |
| D6 | Üretim | Ekran: UI-14 düzeltme formu, iptal düğmesi, ret kodlarının Türkçe metinleri | — |
| D7 | Üretim + Satış | İptal edilen emrin satış bağı okumasında görünmesi (MOD-03) | Satış'ın okuma tüketicisi Satış'ındır |

Sıra gerekçesi: D2 ve D3, D1'in ters hareketi ve okuma portu olmadan stok tutarlılığını koruyamaz. D1 bitmeden D2
domain olarak yazılabilir, ama D3 uç olarak açılmaz. D5, D2–D4'e dayanır, çünkü iptalin ön koşulu net çıktının ve
net çıkışın sıfır olmasıdır.

## 6. Başka şeritlere aktarılanlar

- **Stok:** D1 (ters hareket türü + etiket durum okuma portu), D5'teki ayırma serbest bırakma sebebi.
- **Satış:** D7 tüketicisi. Üretim yalnız emir durumunu okuma portunda yayınlar.
- **Lojistik:** Yeni iş yok. Sevk bilgisi Stok hareketlerinden okunur.

## 7. Kapsam dışı

- Kapalı emri yeniden açma, aynı etiketi yeniden kullanma, toplu düzeltme.
- Kapasite ve takvimle termin hesabı (URT-D29: termin elle girilir).
- Grant, rol, Critical, production etkinleştirme.

## 8. Termin (URT-D29) için öneri

Termin elle girilen bir tarih alanıdır. Öneri: planda isteğe bağlı `RequestedDueDate` (tarih, saat yok). Plan
onayında emre kopyalanır, açık emirde düzeltilebilir. Takvim ve kapasite hesabı yoktur. Bu, D-dizisinden bağımsız
küçük bir dilimdir (migration: plan ve emirde birer nullable sütun). Alanın plan mı emir mi olduğu geri alınabilir bir
uygulama ayrıntısıdır.

## 9. Sinan'a sorular (NEEDS-SINAN, Issue #66)

| # | Soru | Varsayılan (cevap gelene kadar) |
|---|---|---|
| N1 | "Satır" bir girdi rulosunun gerçekleştirmesi mi (o rulonun çıktıları ve iadesi)? | Evet, bu okumayla yazılır |
| N2 | Sevk dışında başka emre çıkılmış, transfer edilmiş veya satışa ayrılmış çıktı da kilitli sayılsın mı? | Kilitli sayılır (fail-closed) |
| N3 | Kapalı (tamamlanmış) emirde düzeltme gerekirse emir yeniden açılabilsin mi? | Hayır, kapalı emirde ret |
| N4 | İptal edilen emrin planı ne olsun: yeniden emir açılabilir (Onaylı'ya dönsün) mü, plan da iptal mi olsun? | Varsayılan yok: D5 iptal komutu bu cevap gelmeden açılmaz |
