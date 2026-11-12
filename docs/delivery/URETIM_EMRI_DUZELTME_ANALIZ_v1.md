# Üretim emrinde düzeltme ve iptal — analiz ve yol haritası v1 (F-009, URT-D28, URT-D30…URT-D34)

Bu belge Sinan'ın 12 Kasım 2026 F-009 kararını (URT-D28,
`docs/decisions/SINAN_KARARLARI_2026-11-12_URETIM_EMRI_DUZELTME.md`) bugünkü kodla karşılaştırır. Sinan'ın istediği
gibi uygulamadan önce yazılmıştır. Sinan'ın takip cevapları (N1–N5, URT-D30…URT-D34) da bu belgeye işlenmiştir. Kod gerçekleri `origin/main` `cf0ae197f` üzerinden okunmuştur. Kod bu belge merge
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

## 3. "Satır" neye karşılık gelir (URT-D30)

Sinan'ın cevabı (N1): "Plan bazında üretime girecek veya üretimden çıkacak her ürün satırı." Satır girdi rulosu
değildir. Planın **girdi ürün satırı** ve **çıktı ürün satırıdır**. Kilit ve düzeltme sınırı, o satıra bağlı
gerçekleşmelerin toplamı üzerinden uygulanır. Rulo ve etiket bu satırın altındaki ayrıntıdır.

Bugünkü koddaki karşılığı:

- **Girdi ürün satırı:** `PlannedInput` (plan girdi satırı). Bugünkü modelde her satır tek bir ana ruloya bağlıdır
  (`parentRollId`). Bu yüzden girdi satırının gerçekleşmesi = o satırın rulosuna ait çıkış ve iade toplamıdır. Kütle
  dengesi rulo başına tutulmaya devam eder.
- **Çıktı ürün satırı:** Planın çıktı satırları (`SlittingSpec.Lines` içindeki `SlitLine` ve boy kesimdeki
  `CutToLengthLine`). Çıktı satırının gerçekleşmesi = o satıra bağlı çıktı beyanlarının (etiketlerin) net kg toplamıdır.
- **Boşluk:** Bugün `OutputDeclaration` plan çıktı satırına bağlı değildir. Yalnız ana ruloyu ve çıktı sınıfını taşır.
  Bu yüzden D2, çıktı beyanına plan çıktı satırı referansını ekler. Eski beyanlarda bu alan boştur. Boş referanslı
  beyan "satırsız" sayılır ve düzeltmesi ret döner (fail-closed); yeni beyanlar referansla yazılır. **Sonuç:** D2'den
  önce beyan almış bir emir, satırsız beyanı geri alınamadığı için net çıktısı sıfıra inemez ve **iptal edilemez**
  (fail-closed, §4.7). Bu emirler bugünkü kapanış kurallarıyla kapatılır. Eski beyanlara geriye dönük satır atamak
  ayrı bir karardır, bu belgede yoktur.

`ASSUMPTION-URT-F009-1` (geri alınabilir): İyi ürün beyanı plandaki çıktı satırına bağlanır. Planda satırı olmayan
sınıflar (2. kalite, kullanılabilir artık, hurda, işlem kaybı) emir içinde **sınıf başına birer çıktı satırı** sayılır.
Bu, satır sınırının hesaplanması için bir uygulama ayrıntısıdır; başka bir gruplama istenirse yalnız toplama kuralı
değişir, olgular değişmez.

Satırı "güncellemek" o satırın çıktısını (veya girdisini) ters kayıtla azaltmak, gerekirse doğru miktarla yeniden
beyan etmektir. Satırı "silmek" o satırın bütün çıktılarını geri almak ve girdisini iade etmektir. Ters kayıt
fiziksel olarak yine etiket üzerinde yapılır; satır yalnız sınırın hangi toplam üzerinden hesaplandığını belirler.

## 4. Sorulara cevaplar

### 4.1 Sevk edilen miktar nereden bulunur

Sevk Lojistik'in kaydıdır ama stok etkisi Stok'ta `logistics.definite-shipment` kaynaklı `Issue` hareketi olarak
durur. Üretim başka modülün tablosunu okumaz. Bu yüzden **Stok'un sahip olduğu yeni bir sürümlü okuma portu**
gerekir: `IInventoryLabelDispositionReadPortV1` (öneri adı). Bir çıktı etiketi için şunları döner:

- etiketin üretimden geldiği kg (çıktı `Receipt`),
- kesin sevkle çıkan kg (müşteri iadesi düşülmeden; iade ayrı alan),
- başka bir üretim emrine çıkışla tüketilen kg,
- transferle gönderilen kg,
- satışa veya başka bir işe ayrılmış kg (aktif ayırma),
- kalite blokajındaki kg (Stok `Block` / `Release` hareketleri, pozisyonun `Blocked` kısmı). Bloklu kg serbest
  sayılmaz; düzeltme ayrı bir ret koduyla reddedilir. Blokajı kaldırmak Stok'un komutudur,
- etiketin çıktının ilk pozisyonunda hâlâ serbest ve ayrılmamış duran kg,
- okuma durumu (`Value / Unreadable`). Okunamazsa düzeltme reddedilir (fail-closed).

Port Stok'un işidir (Stok şeridi, K3: çapraz modül okuma). Üretim yalnız tüketir. İstek Stok oturumuna iletilir.

### 4.2 Satırın alt sınırı

Bir çıktı etiketinden geri alınabilecek kg = **ilk pozisyonunda serbest ve ayrılmamış duran kg**. Sevk edilmiş,
başka emre girdi olarak verilmiş, transfer edilmiş veya satışa ayrılmış kg geri alınamaz (URT-D31). Satırın alt sınırı
= o satıra bağlı çıktı etiketlerindeki bu kilitli kg toplamıdır (URT-D30). Kilitli kg'ı serbest bırakmak düzeltmeden
önce ayrı bir iştir: ayırma bırakılır, transfer geri alınır, kalite blokajı kaldırılır. **Yazar okuması (geri
alınabilir):** bu işler kendi modüllerinin komutlarıyla yapılır; Üretim bunları kendisi geri almaz. Port okunamazsa
düzeltme reddedilir.

### 4.3 Sevk edilmemiş çıktının geri alınması

1. Üretim yeni bir **çıktı ters olgusu** yazar: kimlik, geri aldığı çıktı beyanının kimliği, kg, gerekçe, kullanıcı,
   zaman. Eski beyan durur. Net çıktı = beyan − ters. **Bir beyana bağlı ters olguların toplamı o beyanın kg'ını
   aşamaz**; aşan istek domain'de ret döner (aynı beyana art arda iki düzeltme de bu sınırla korunur).
2. Aynı işlemde Stok, çıktı etiketinden **ters hareket** yazar (yeni hareket türü; kaynak türü
   `manufacturing.output-reversal`, eski `Receipt` hareketine bağlı). Kg, etiketin serbest kg'ını aşarsa Stok reddeder.
3. Doğru miktar yeniden beyan edilecekse bu, bugünkü çıktı beyanı komutuyla yeni bir etiketle yapılır. Aynı etiketi
   yeniden kullanmak istenirse bu ayrı bir karardır, ilk dilimde yoktur.
4. Hurda da etiketle stoğa girdiği için (URT-D8) aynı yolla geri alınır. İşlem kaybı stokta değildir. Onun ters olgusu
   yalnız Üretim'dedir.
5. Yarı mamul ölçümü bir anlık ölçümdür. Düzeltmesi yeni bir ölçümle yapılır; ters olguya gerek yoktur.

### 4.4 Hammaddenin eski miktarına dönmesi

Bugünkü iade yolu (`ReturnInventoryInputAsync`, Stok `Return` hareketi, aynı etiket ve pozisyon) bu işi yapar. Sıra
şudur: önce rulonun çıktıları geri alınır, sonra girdi iade edilir. **Yazar okuması (geri alınabilir):** iade sınırı "çıkış − iade − net çıktı − yarı
mamul" olacak şekilde sıkılaştırılır (§2 boşluğu). Böylece çıktısı duran bir rulonun girdisi iade edilemez.

### 4.5 Etiket stokta değilse ret

Çıktı etiketinin serbest kg'ı istenen ters kg'ından azsa komut ret döner. Ret kodu sebebi söyler (sevk edildi, başka
emre çıkıldı, transfer edildi, ayrıldı, kalite blokajında, okunamadı). Kodlar Üretim'in problem kodlarına eklenir. Stok'un ret kodları
eşlenir.

### 4.6 Emrin tamamlanma koşulu

Değişmez: bugünkü kapanış (`CloseOrderAsync`) net çıktılarla çalışır. Kütle dengesi ve fire üst sınırı ters olgular
düşüldükten sonra değerlendirilir. **Kapalı emirde düzeltme ve iptal reddedilir** (F-009 kabul satırı).

**Yeniden açma (URT-D32):** Yetkili kişi kapalı emri gerekçeyle yeniden açabilir. Kurallar:

1. Yeni komut `ReopenOrder`: gerekçe zorunludur; kullanıcı, zaman ve gerekçe denetim kaydına düşer. Emir `Open`'a döner.
   Önceki kapanış kaydı silinmez; yeniden açma ayrı bir olgudur.
2. Yeni yetki adı (öneri `manufacturing.orders.reopen`) yalnız kayıt olarak eklenir; grant insan kapısıdır.
3. **Muhasebe dönemi kapanmışsa** emir yeniden açılamaz. Dönem durumunun sahibi Muhasebe'dir. Yeni port açılmaz;
   Üretim main'deki `IAccountingPeriodStatusPortV1.GetStatusAsync(DateOnly on, …)`'u kullanır. **Yazar okuması (geri
   alınabilir):** sorulan tarih emrin kapanış tarihidir (kapanış olgusunun zamanı, şirket saat dilimiyle gün). Cevap
   eşlemesi:
   - `Open` → yeniden açma devam eder.
   - `NotOpen` → "muhasebe dönemi kapalı" ret kodu.
   - `None` (o tarih için dönem tanımlı değil) → ayrı ret kodu; açılabilir sayılmaz.
   - `Denied` (aktörün `accounting.periods.read` yetkisi yok) → ayrı ret kodu. **"Dönem kapalı" diye gösterilmez.**
   - Port kayıtlı değil veya okunamıyor → kodsuz 503 (fail-closed).

   Üretim kullanıcısına `accounting.periods.read` verilmesi grant kararıdır (insan kapısı).
4. Yeniden açılan emirde düzeltme bu belgedeki ters kayıt yoluyla yapılır, sonra emir yeniden kapatılır.
5. Kapanışta hesaplanmış üretim alış maliyeti ortalamaya girmiş olabilir (§4.8). Sinan'ın 11. tur m.2 kararı
   ([`6820601088`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6820601088)): geri alınan çıktı
   kendi giriş birim maliyetiyle ters hareketle stoktan çıkar, kalan stoğun ortalaması yeniden hesaplanır, geçmiş
   geriye dönük düzeltilmez. Bu kural Stok'undur; karar kimliği Stok + Üretim için `NEEDS-SPEC`'tir. Stok bu ters
   hareketi portunda sunana kadar maliyeti ortalamaya girmiş emrin yeniden açılması reddedilir (fail-closed).

### 4.7 Hiç sevk yokken iptal

Ön koşullar: emir açık; hiçbir çıktı etiketinde kilitli kg yok; her çıktı geri alınmış (net çıktı 0); her girdi iade
edilmiş (net çıkış 0); yarı mamul 0. Bu durumda **İptal** komutu emri `Cancelled` durumuna geçirir, gerekçe ve kullanıcı
kaydedilir, plan ayırmasının kalanı serbest bırakılır. İptal hiçbir şeyi otomatik geri almaz. Kullanıcı önce satırları
geri alır, sonra iptal eder. Bu, Sinan'ın "hammadde eski miktarına ulaşınca o üretim emri silinebilir" sırasıdır. Emir
ve olguları silinmez. Ekranda iptal edilmiş emir ayrı süzgeçle görünür.

**Planın durumu (URT-D33):** Emir iptal edilince plan **Onaylı**'ya döner. Aynı plandan yeniden üretim emri açılabilir.
Planın emirle bağı silinmez; iptal edilen emir planın geçmişinde görünür. Yeni emir yeni bir emir kimliğidir.

**URT-D1 ile uzlaşma:** URT-D1 "tek plan = tek emir" der; URT-D33 bunu iptal edilmiş emir için daraltır. Bir planın
aynı anda en çok bir **iptal edilmemiş** emri olur. Bugün bu kural tam tekil indeksle (`ManufacturingDbContext.cs:508`,
`(CompanyId, PlanId)` unique) ve `PlanOrderExists` ret koduyla (`ProductionPlan.cs`, `ProductionOrder.cs`,
`ManufacturingStore.cs`) tutulur. D5 bunu iptal edilmemiş emirle sınırlı **filtreli tekil indekse** çevirir
(`[Status] <> Cancelled`), `CK_Orders_Enums` kontrolünü `Cancelled`'ı kapsayacak şekilde genişletir ve `PlanOrderExists`
kontrolünü yalnız iptal edilmemiş emre bakacak hale getirir. Migration'ın Down'ı forward-fix'tir: iptal edilmiş emir
varken geri alınmaz. Aynı plandan aynı anda iki emir açma yarışı gerçek SQL testiyle kanıtlanır. K3'tür.

**Yeni emirde girdi ayırması (URT-D34, 20. tur m.1):** Plan Onaylı'ya döndükten sonra yeni emir açılırken stok yeniden
kontrol edilir ve Stok'un plan ayırma portu üzerinden yeniden ayrılır. Eski emrin ayırmasının kalanı iptalde `OrderCancelled`
sebebiyle bırakılmıştır. Stok yetmezse emir açılmaz ve eksik girdiler (ürün, istenen kg, ayrılabilen kg) cevapta açıkça
gösterilir. Port kayıtlı değil veya okunamıyorsa emir açılmaz (fail-closed, kodsuz 503). Cross-module sözleşme ve
stok kuralı olduğu için K3'tür.

### 4.8 Ortalama maliyete etkisi (SAT-D305 / SAT-D307 / SAT-D308)

- Üretim alış maliyeti yalnız **kapalı** emirde hesaplanır (Stok cevabı 2). Düzeltme ve iptal yalnız **açık** emirde
  yapılır (yeniden açılan emir için §4.6 madde 5). Bu yüzden geri alınan çıktı henüz bir maliyetle ortalamaya girmemiştir. Ters hareketin ortalamaya etkisi
  yoktur. Üretim alışını ortalamaya hangi anda ve hangi tutarla aldığı Stok'un kararıdır; Stok bunu ters hareket
  diliminde doğrular.
- Girdi iadesi bugünkü kurala bağlıdır: iade, bağlı olduğu çıkışın damgalı maliyetiyle girer (Stok cevabı 3,
  `OPEN-INV-AVGCOST-RETURN`).
- Hesaplayıcı (SAT-D307; anahtar kapalıyken SAT-D308) net çıktıyı ve net iadeyi kullanır. Ters olgular girdi listesine net olarak yansır.

## 5. Yol haritası (dilim sırası)

| Dilim | Şerit | İçerik | K3 noktası |
|---|---|---|---|
| D0 | Üretim | Bu belge ve karar dosyası (docs PR) | — |
| D1 | Stok | Ters hareket türü (`manufacturing.output-reversal`) ve `IInventoryLabelDispositionReadPortV1` | Migration (hareket türü kontrolü), çapraz modül port |
| D2 | Üretim | Domain: çıktı ters olgusu, net çıktı, kütle dengesinde ters olgu, kapalı emirde ret, çıktı beyanına plan çıktı satırı referansı (§3) | Migration (yeni tablo), forward-fix Down |
| D3 | Üretim | Uygulama + host + uç: API-25 çıktı düzeltmesi; istekte emrin `ExpectedVersion`'ı; `manufacturing.facts.correct` yalnız kayıt | Komut iskeleti, yetki adı, idempotency, Stok katılımcısıyla tek işlem |
| D4 | Üretim | İade sınırının net çıktıyla sıkılaştırılması (§4.4) | Kural değişikliği (para/stok) |
| D5 | Üretim | `OrderStatus.Cancelled`, iptal komutu (istekte emrin `ExpectedVersion`'ı), `manufacturing.orders.cancel` yalnız kayıt, ayırma serbest bırakma sebebi `OrderCancelled`, planın Onaylı'ya dönmesi (URT-D33), filtreli tekil indeks ve `PlanOrderExists` değişikliği, yeni emirde yeniden ayırma (URT-D34, §4.7) | Migration (durum kontrolü genişler, filtreli tekil indeks, forward-fix Down), yarış testi, Stok ayırma portu sebep listesi |
| D6 | Üretim | Ekran: UI-14 düzeltme formu, iptal düğmesi, ret kodlarının Türkçe metinleri | — |
| D7 | Üretim + Satış | İptal edilen emrin satış bağı okumasında görünmesi (MOD-03) | Satış'ın okuma tüketicisi Satış'ındır |
| D8 | Üretim | Yeniden açma: `ReopenOrder` komutu (istekte emrin `ExpectedVersion`'ı), gerekçe ve denetim kaydı, `manufacturing.orders.reopen` yalnız kayıt, Muhasebe'nin `IAccountingPeriodStatusPortV1` tüketimi (§4.6) | Migration (yeniden açma olgusu), yetki adı, çapraz modül port |

Sıra gerekçesi: D2 ve D3, D1'in ters hareketi ve okuma portu olmadan stok tutarlılığını koruyamaz. D1 bitmeden D2
domain olarak yazılabilir, ama D3 uç olarak açılmaz. D5, D2–D4'e dayanır, çünkü iptalin ön koşulu net çıktının ve
net çıkışın sıfır olmasıdır. D8, D2–D3'e ve Muhasebe'nin main'deki dönem portuna dayanır; port kayıtlı değilse veya okunamıyorsa D8 fail-closed kalır.

## 6. Başka şeritlere aktarılanlar

- **Stok:** D1 (ters hareket türü + etiket durum okuma portu; portta kalite blokajındaki kg alanı ve ret kodu), D5'teki
  ayırma serbest bırakma sebebi `OrderCancelled` (Stok'un `InventoryManufacturingPlanReleaseReasonV1` listesine) ve
  yeni emirde yeniden ayırmanın eksik girdi cevabı (URT-D34).
- **Satış:** D7 tüketicisi. Üretim yalnız emir durumunu okuma portunda yayınlar.
- **Stok (ek):** D1 portu satışa ayrılmış kg'ı da döner (URT-D31). Yeniden açılan emirde çıktının ters hareketi ve
  ortalamanın yeniden hesabı (§4.6 madde 5, 11. tur m.2; karar kimliği `NEEDS-SPEC`).
- **Muhasebe:** Yeni iş yok. Dönem durumu main'deki `IAccountingPeriodStatusPortV1`'den okunur (D8, URT-D32).
  `accounting.periods.read` grant'i insan kapısıdır.
- **Lojistik:** Yeni iş yok. Sevk bilgisi Stok hareketlerinden okunur.

## 7. Kapsam dışı

- Aynı etiketi yeniden kullanma, toplu düzeltme.
- Kilitli kg'ı serbest bırakan işler (ayırmayı bırakma, transferi geri alma): kendi modüllerinin komutlarıdır.
- Kapasite ve takvimle termin hesabı (URT-D29: termin elle girilir).
- Grant, rol, Critical, production etkinleştirme.

## 8. Termin (URT-D29) için öneri

Termin elle girilen bir tarih alanıdır. Öneri: planda isteğe bağlı `RequestedDueDate` (tarih, saat yok). Plan
onayında emre kopyalanır, açık emirde düzeltilebilir. Takvim ve kapasite hesabı yoktur. Bu, D-dizisinden bağımsız
küçük bir dilimdir (migration: plan ve emirde birer nullable sütun). Migration olduğu için K3'tür. Alanın plan mı emir mi olduğu geri alınabilir bir
uygulama ayrıntısıdır.

## 9. Sinan'a sorular (Issue #66) — cevaplandı

| # | Soru | Cevap | Karar |
|---|---|---|---|
| N1 | "Satır" bir girdi rulosunun gerçekleştirmesi mi? | Hayır. Planın girdi ürün satırı ve çıktı ürün satırı ([`6819908312`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6819908312) m.1) | URT-D30, §3 |
| N2 | Sevk dışında başka emre çıkılmış, transfer edilmiş veya satışa ayrılmış çıktı da kilitli sayılsın mı? | Evet, hepsi kilitli; önce o iş geri alınır ([`6819835966`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6819835966) m.2) | URT-D31, §4.2 |
| N3 | Kapalı emirde düzeltme gerekirse emir yeniden açılabilsin mi? | Yetkili kişi gerekçeyle açabilir; muhasebe dönemi kapalıysa açılamaz ([`6819835966`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6819835966) m.3) | URT-D32, §4.6 |
| N4 | İptal edilen emrin planı ne olsun? | Plan Onaylı'ya döner, yeniden emir açılabilir ([`6819640816`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6819640816) m.2) | URT-D33, §4.7 |
| N5 | İptalden sonra yeni emirde girdi nasıl ayrılsın? | Yeni emirde yeniden ayrılır; stok yetmezse emir açılmaz, eksik girdiler gösterilir ([`6822303233`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6822303233) m.1) | URT-D34, §4.7 |
