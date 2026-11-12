# Üretim emrinde düzeltme ve iptal — analiz ve yol haritası v1 (F-009, URT-D28, URT-D30…URT-D38)

Bu belge Sinan'ın 12 Kasım 2026 F-009 kararını (URT-D28,
`docs/decisions/SINAN_KARARLARI_2026-11-12_URETIM_EMRI_DUZELTME.md`) bugünkü kodla karşılaştırır. Sinan'ın istediği
gibi uygulamadan önce yazılmıştır. Sinan'ın takip cevapları (N1–N11, URT-D30…URT-D38) da bu belgeye işlenmiştir. Kod gerçekleri `origin/main` `cf0ae197f` üzerinden okunmuştur. Kod bu belge merge
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
   - `NotOpen` → nötr "muhasebe dönemi açık değil" ret kodu. Port belgesine göre `NotOpen` önerilmiş (onaylanmamış)
     veya kapalı dönemi kapsar; ret metni portun `Status` alanına göre `Proposed` ile `Closed`'u ayırır. Sinan'ın kuralı
     yalnız kapalı dönemi söyler; `Proposed` ve `None`'da ret fail-closed yönde daraltmadır.
   - `None` (o tarih için dönem tanımlı değil) → ayrı ret kodu; açılabilir sayılmaz.
   - `Denied` (aktörün `accounting.periods.read` yetkisi yok) → ayrı ret kodu. **"Dönem kapalı" diye gösterilmez.**
   - Port kayıtlı değil veya okunamıyor → kodsuz 503 (fail-closed).

   Üretim kullanıcısına `accounting.periods.read` verilmesi grant kararıdır (insan kapısı).
4. Yeniden açılan emirde düzeltme bu belgedeki ters kayıt yoluyla yapılır, sonra emir yeniden kapatılır.
5. Kapanışta hesaplanmış üretim alış maliyeti ortalamaya girmiş olabilir (§4.8). Sinan'ın 11. tur m.2 kararı
   ([`6820601088`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6820601088)): geri alınan çıktı
   kendi giriş birim maliyetiyle ters hareketle stoktan çıkar, kalan stoğun ortalaması yeniden hesaplanır, geçmiş
   geriye dönük düzeltilmez. 22. tur m.2 ([`6822949341`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6822949341)) aynı kuralı yeniden açılan emir için teyit etti; iki kaynak birlikte URT-D36'dır: ters hareket emrin
   kendi giriş maliyetiyle yapılır, ortalama bu girişin etkisi çıkarılarak yeniden hesaplanır (Stok calculator'ı). Stok bu ters
   hareketi portunda sunana kadar maliyeti ortalamaya girmiş emrin yeniden açılması reddedilir (fail-closed). Bu D8
   diliminin **geçici** kuralıdır, kalıcı bir "yeniden açılmaz" kuralı değildir; port gelince URT-D32 ve URT-D36
   uygulanır. URT-D8 hurda/fire kararıdır ve bu kuralla ilgisi yoktur (koordinatör düzeltmesi
   [`6823484848`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6823484848)).

### 4.7 Hiç sevk yokken iptal

Ön koşullar: emir açık; hiçbir çıktı etiketinde kilitli kg yok; her çıktı geri alınmış (net çıktı 0); her girdi iade
edilmiş (net çıkış 0); yarı mamul 0. Bu durumda **İptal** komutu emri `Cancelled` durumuna geçirir, gerekçe ve kullanıcı
kaydedilir, plan ayırmasının kalanı serbest bırakılır. İptal hiçbir şeyi otomatik geri almaz. Kullanıcı önce satırları
geri alır, sonra iptal eder. Bu, Sinan'ın "hammadde eski miktarına ulaşınca o üretim emri silinebilir" sırasıdır. Emir
ve olguları silinmez. Ekranda iptal edilmiş emir ayrı süzgeçle görünür.

**Planın durumu (URT-D33):** Emir iptal edilince plan **Onaylı**'ya döner. Aynı plandan yeniden üretim emri açılabilir.
Yeni emir yeni bir emir kimliğidir. Bugün `ProductionPlan.OrderId` tek bir `Guid?` tutar ve `RecordOrder` dolu değeri
`PlanOrderExists` ile reddeder (`ProductionPlan.cs:181`, `:548-562`). D5'te bu alan **etkin (iptal edilmemiş) emri**
gösterir: iptalde boşaltılır, yeni emirde yeni kimlikle dolar. Planın emir geçmişi bu alandan değil, emir kayıtlarının
`Orders.PlanId` sütunundan okunur; iptal edilen emir orada kalır ve silinmez. Bu, iptal edilmemiş emirle sınırlı tekil
indeksle aynı kuralı tutar (aşağıda): planın en çok bir etkin emri olur, alan da en çok bir emir gösterir.

**URT-D1 ile uzlaşma:** URT-D1 "tek plan = tek emir" der; URT-D33 bunu iptal edilmiş emir için daraltır. Bir planın
aynı anda en çok bir **iptal edilmemiş** emri olur. Bugün bu kural tam tekil indeksle (`ManufacturingDbContext.cs:508`,
`(CompanyId, PlanId)` unique) ve `PlanOrderExists` ret koduyla (`ProductionPlan.cs`, `ProductionOrder.cs`,
`ManufacturingStore.cs`) tutulur. D5 bunu iptal edilmemiş emirle sınırlı **filtreli tekil indekse** çevirir
(`[Status] <> Cancelled`), `CK_Orders_Enums` kontrolünü `Cancelled`'ı kapsayacak şekilde genişletir ve `PlanOrderExists`
kontrolünü yalnız iptal edilmemiş emre bakacak hale getirir. `CK_Orders_Close` (`ManufacturingDbContext.cs:496-498`)
bugün yalnız `Status IN (1, 2)` dallarını sabitler; aynı migration `Cancelled` için iptal eden kullanıcı ve iptal zamanı
dalını ekler. Migration'ın Down'ı forward-fix'tir: iptal edilmiş emir
varken geri alınmaz. Aynı plandan aynı anda iki emir açma yarışı gerçek SQL testiyle kanıtlanır. K3'tür.

**Ara durum (D5 notu):** İptal ile yeni emir arasında plan "Onaylı ama etkin ayırması yok" durumundadır. Bugün Onaylı
ayrılmış demektir (Manufacturing README U3-a). D5'te plan iptalinin `PlanCancelled` serbest bırakması bu durumda
bırakılacak kalan bulmamalı ve hata vermemelidir; plan görünümü (API-14) bu durumu ayırmalıdır. D5/D6 kabulünde ayrıca
URT-D34'ün "eksik girdiler açıkça gösterilir" kısmı Stok'un Free kg portuyla sağlanır. D5 bu port ve Stok'un
`OrderCancelled=3` dilimi main'e girmeden başlamaz (§5); bu yüzden eksik girdiyi göstermeyen, yalnız çıplak 409 dönen
bir ara dönem yoktur. Yetersizlikte emir açılmaz (all-or-nothing, kısmi ayırma yok). Etiket seçimi (URT-D35), farklı
etiket (URT-D37), sahiplik (URT-D38), kısmi dilme ve yeniden onayda ayırma anı (26. tur
[`6823809221`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6823809221) m.1/m.2, N10/N11) iş sorusu olarak cevaplandı. Farklı etiketin uygulaması (Onaylı plandan
yeniden onaya geçiş) ayrı bir K3 dilimidir (§5 D5b); main'deki kısıt o dilim gelene kadar yerinde kalır (§4.7 (e)).

**Yeni emirde girdi ayırması (URT-D34, 20. tur m.1; ayırma anı 26. tur m.2):** Plan Onaylı'ya döndükten sonra yeni
emirden önce stok yeniden kontrol edilir ve Stok'un plan ayırma portu üzerinden yeniden ayrılır. Ayırma anı yeniden
onaydır (aşağıda). Eski emrin ayırmasının kalanı iptalde `OrderCancelled`
sebebiyle bırakılmıştır. Ayrılacak etiketleri her girdi ürünü için kullanıcı seçer (URT-D35); sistem FIFO veya plan etiketiyle
seçmez. Plandakinden farklı etiket seçilirse plan yeni etikete göre yeniden hesaplanır ve yeniden onaya düşer; uymuyorsa
seçim reddedilir (URT-D37). **Koordinatör okuması ([`6823809221`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6823809221) m.2, geri alınabilir):** iptal sonrası
her yeni emir, girdiler değişse de değişmese de planın yeniden onayından geçer. Ayırma bu adımda URT-D21 yolundan,
yeni plan sürümünden türeyen anahtarla yapılır (N11). Emir açılışı **hiçbir durumda** ayırmaz; yeniden onaylanan plan
sürümünün etkin ayırmasını kullanır, yoksa reddedilir. Girdi değişmediyse yeniden onay yeniden hesap yapmaz, yalnız
yeniden ayırır. İkinci bir ayırma yolu açılmaz. Seçilen etiketin sahipliği plan girdisininkiyle aynı olmalıdır: şirket malı yerine şirket
malı, müşteri malı yerine aynı müşterinin malı; farklıysa reddedilir (URT-D38; karşılaştırma `CommercialContext.PartyId`
ile, §4.7 (e)). Seçilen etiketlerin serbest kg'ı yetmezse yeniden onay reddedilir, emir açılmaz ve eksik girdiler (ürün, istenen kg, ayrılabilen kg)
cevapta açıkça gösterilir; kısmi ayırma yoktur. Port kayıtlı değil veya okunamıyorsa emir açılmaz (fail-closed, kodsuz 503). Cross-module sözleşme ve
stok kuralı olduğu için K3'tür.

**Free kg okumasının rolü:** Karar kaynağı her zaman Stok'un ayırma komutudur (all-or-nothing). Free kg okuma portu
yalnız etiket seçim listesini ve ret cevabındaki eksik girdi listesini doldurur. Okuma ile ayırma arasında zaman farkı
(TOCTOU) vardır: okuma "yeterli" derken komut 409 dönerse emir yine açılmaz. Okuma başarısızsa ret ayrıntısız kalır;
emir yine açılmaz.

**Bugünkü ayırma kimlikleriyle çakışma (D5 ve D8 K3 noktası, B review `6822977122` P2-1):** Main'de aynı plan için
ikinci bir ayırma veya ikinci bir serbest bırakma üç yerde takılır. Kod fail-closed kalır, veri bozulmaz; ama URT-D34 bu
değişiklikler olmadan uygulanamaz:

1. Serbest bırakmanın idempotency anahtarı plan + sebep başınadır (`ManufacturingPlanAllocationHost.cs:117`,
   `manufacturing-plan-release:{company}:{plan}:{reason}`). Aynı planda ikinci emir iptal edilince aynı anahtar gelir:
   Stok replay döner (host `Incomplete` sayar) veya 409 `IdempotencyConflict` döner. D8'de yeniden açılıp yeniden kapanan
   emir `OrderClosed` anahtarını ikinci kez kullanır.
2. Ayırmanın idempotency anahtarı plan sürümünden türer (`ManufacturingPlanAllocationHost.cs:176`). Plana emir
   bağlanırken plan sürümü ilerlemez (`ProductionPlan.RecordOrder`). "Plan Onaylı'ya döner" adımı sürümü ilerletmezse
   yeni emirdeki ayırma ilk onayın anahtarına düşer.
3. Üretim'in ayırma kaydı plan girdisi başına tektir: `PlanInputAllocations` PK = `(CompanyId, PlanId, ParentRollId)`
   (`ManufacturingDbContext.cs:449`). Girdi çıkışı bu kaydı okur (`FindPlanInputAllocationAsync`,
   `ManufacturingStore.cs:860-869`); kayıt emir 1'in tükettiği eski ayırmayı gösterir ve Stok `Active` olmayan ayırmayı
   tüketmeyi reddeder. Satırı üzerine yazmak URT-D3'ün etiket sürümü izini siler.

D5 için gerekenler (yazar okuması, geri alınabilir teknik ayrıntı; Stok politikası seçmez):

- (a) Ayırma kimliği plan sürümünden türemeye devam eder (`:176`): 26. tur m.2 ile ayırma yalnız (yeniden) onayda
  yapıldığı için yeniden onay plan sürümünü bir ilerletir ve her ayırma kuşağı yeni sürümün anahtarını alır. Serbest
  bırakma kimliği **emir (ayırma kuşağı) başına** olur: anahtar emir kimliğini veya ayırmanın plan sürümünü içerir;
  hangisinin seçildiği D5 PR'ında açıkça yazılır. D8'in `OrderClosed` serbest bırakması da kuşak anahtarına geçer.
- (b) `PlanInputAllocations` için şema değişikliği: emir/kuşak kimliği anahtara girer, eski satırlar silinmez veya
  üzerine yazılmaz (URT-D3 izi korunur). Girdi çıkışı yalnız etkin emrin ayırmasını okur. Migration, mevcut satırları
  ilk emirlerine bağlayan backfill ve forward-fix Down (birden çok kuşak varken geri alınmaz) gerekir.
- (c) URT-D35/URT-D37 bu şemayı da etkiler. Bugünkü plan girdisi ana etiket anahtarlıdır (`PlannedInput.ParentRollId`,
  `ExpectedLabelVersion`, `TechnicalSnapshot`, `PlannedMass`, `CommercialContext`; `PlannedInput.cs:9-60`) ve plan, o bobinin eni ve
  teknik değerleriyle hesaplanmıştır (ör. `SlittingSpecRequestV1.NetUsableWidthMm` planlanan ana bobin genişliğini
  aşamaz). Bu yüzden başka etiket bir ayırma ayrıntısı değildir: URT-D37'ye göre plan girdisi yeni etikete geçer, plan
  hesap motoruyla yeniden hesaplanır (en, fire, kütle dengesi) ve yeniden onaya düşer; ayırma yeniden onayda yeni
  etiketle yapılır (N11). Değişen alanlar `ParentRollId`, `ExpectedLabelVersion`, `TechnicalSnapshot` ve `PlannedMass`'tır;
  `CommercialContext` değişmez (URT-D38). Eski plan girdisi değerleri silinmez: main'deki taslak revizyon tarihçesi
  (`PlanRevisions` / `PlanRevisionInputs`, MFG-PLAN-EDIT, `ManufacturingDbContext.cs:22-24`) deseniyle yeni revizyon
  eklenir, plan satırı ilk içeriğini korur; URT-D3 etiket izi korunur. İlk emirde URT-D21/URT-D3 aynen
  sürer (yazar okuması): plan onayında planı hazırlayan kullanıcının planda seçtiği etiket ayrılır.
- (d) Yeniden onay (Üretim yazımı) ile Stok ayırması tek işlemde yapılır (bugünkü `ApproveAndAllocateAsync` /
  `RunAsync` katılımcı deseni, `ManufacturingApiV1.cs:1255-1258`). Emir açılışı Stok'a ayırma komutu göndermez; yalnız
  yeniden onaylanan sürümün etkin ayırmasını doğrular (26. tur m.2).
- (d2) **D5'in yeniden onay adımı (yazar okuması, geri alınabilir):** D5 minimal yeniden onay adımını kendisi getirir;
  D5b'ye kadar fail-closed bekletmez. Kapsam: iptal sonrası Onaylı ama etkin ayırması olmayan planda, girdileri
  **değişmeden**, URT-D21 onay yolunun yeniden çalıştırılması (URT-D2 hazırlayan ≠ onaylayan, DEC-PRDCT-008 snapshot,
  plan sürümü +1, aynı etiketlerle all-or-nothing ayırma). Plan durumu Onaylı kalır (URT-D33); yeniden hesap ve girdi
  değişimi yoktur. Farklı etiket seçimi (URT-D37) D5b'ye kadar reddedilir. D5b bu adımı genişletir (revizyon, yeniden
  hesap); ikinci bir yol açmaz.
- (e) URT-D37/URT-D38 K3 noktaları: `PlannedInput` etiket alanlarının değişimi ve izi; motor üzerinden yeniden hesap;
  yeniden onay; kütle dengesi; motorun reddettiği seçimde ret; sahiplik kontrolü backend'de (Stok etiket okumasından,
  okunamazsa ret).
  - **Uygulama ayrı K3 dilimidir (§5 D5b).** Main'de yalnız taslak plan revize edilir (`ProductionPlan.RequireRevisable`,
    `ProductionPlan.cs:761-775`, `PlanNotDraft`) ve dilme ayrıntılı plan `manufacturing_plan_slitting_spec_fixed` ile
    kilitlidir (:772, AT-044). D5b şunları getirir: Onaylı → yeniden onay geçişi; `PlanRevisions` tarihçesi (eski değerler
    (c)'deki gibi); kısıtın kaldırılması; HamKg ≤ etiketin güncel bakiyesi (N10: planlanan kg etiket bakiyesini aşamaz,
    kalan aynı etikette düşmüş bakiyeyle kalır, yeni etiket basılmaz; bakiye Stok portundan okunur, aşan plan reddedilir,
    okunamazsa ret). Kısıt D5b main'e gelene kadar yerinde kalır.
  - **Yeniden onayda ayırma (N11).** URT-D21 yolu kullanılır: anahtar yeni plan sürümünden türer
    (`manufacturing-plan-allocation:{company}:{plan}:{version}`, `ManufacturingPlanAllocationHost.cs:176`); emir
    açılışı ayrıca ayırmaz. `OrderCancelled` ile serbest kalan değişmemiş girdiler de yeniden onayda tekrar ayrılır.
    URT-D2 (hazırlayan ≠ onaylayan) ve DEC-PRDCT-008 ayar snapshot'ı yeniden onayda da uygulanır.
  - **Sahiplik anahtarı (URT-D38).** Karşılaştırma `CommercialContext.PartyId` iledir, `CompanyPartyAccountId` ile değil.
    `CommercialContext` null olan girdi şirket malıdır ve müşteri malı etiket reddedilir; dolu girdi için etiketin sahibi
    aynı `PartyId` olmalıdır. Main'de Stok'un plan ayırması bunu zaten uygular (URT-D22, `OwnedAsPlanned`,
    `InventoryManufacturingRealizationParticipant.PlanAllocations.cs:284-299`; Üretim `CustomerPartyId =
    CommercialContext.PartyId` gönderir, `ManufacturingPlanAllocationHost.cs:52-60`). Yeni Stok alanı veya okuması
    gerekmez; Üretim'in ekran ve komut kontrolü yalnız erken ret içindir.
  - **Null girdinin dayanağı.** Null = şirket malı okuması yeni politika değildir: main'deki host null'ı `Company` olarak
    gönderir ve Stok `OwnedAsPlanned` şirket sahibi ister; null girdinin şirket malı oluşu ilk onayda Stok tarafından
    doğrulanmıştır. `PlannedInput.cs:55-58` docstring'i ("not interpreted here as company-owned") domain katmanı içindir;
    uygulama katmanı bugün null'u şirket malı sayar. Bu gerilim D5 PR'ında docstring'e not düşülerek kapanır. U3-a
    öncesinden kalan, ayırmasız onaylanmış (legacy) planda iptal sonrası yeniden onay fail-closed reddedilir.
  - **Stok talep satırı (S1/S2).** Stok'un plan ayırma sözleşmesi plan girdisini ana etiketle anahtarlar
    (`DemandLineId = parent.Label.Id`, `InventoryManufacturingRealizationParticipant.PlanAllocations.cs:94`; etkin
    ayırma tekilliği `UX_Allocations_ActivePlanInput` (CompanyId, DemandId, DemandLineId), `InventoryDbContext.cs:500-503`).
    Plan girdisi yeni etikete geçince talep satırı da değişir. Stok cevabı
    ([#66 `6823794908`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6823794908)): S1 — `DemandLineId =
    parent.Label.Id` kalır; eski ayırma `OrderCancelled` ile serbest kalır, yeni etiket yeni `DemandLineId` alır, V1
    yeterlidir. S2 — yeni alan gerekmez; sahiplik `IInventoryLabelDetailsV1.GetAsync` → `Balances.Positions[]`
    (`OwnerKind`, `OwnerId`, `LabelVersion`) içinden okunur; 503/409/404/401/403, pozisyon yokluğu veya birden çok sahip
    ret demektir (fail-closed). Counterparty'nin müşteri olduğunun doğrulanması Ortaklar/Satış portunun işidir (§6).
  - **D5b K3 listesine bırakılanlar (#1116'nın Üretim K3'üne bıraktığı noktalar; bu belge seçim yapmaz):** (1) HamKg üst
    sınırı hangi bakiyedir (`PhysicalKg` / `FreeKg` / `BlockedKg`) ve birden çok pozisyonlu etikette hangi pozisyon
    sayılır; (2) kısmi dilmede kalan kg için plan ayırması nasıl davranır; (3) tüketim katılımcısında kısmi tüketim nasıl
    işler.
  - **AT-044 genel kuraldır.** 26. tur m.1 dilme motorunun tabanıdır (README `:156`, `:252`, NEEDS-BUSINESS
    `5741547443`); yalnız URT-D37 yolu için değildir. `manufacturing_plan_slitting_spec_fixed` kaldırılınca taslak plan
    revizyonu (MFG-PLAN-EDIT) da açılır; HamKg ≤ bakiye kuralı ilk plan kaydında, taslak revizyonda ve onayda da
    uygulanır. Kuralın sahibi D5b'dir (yazar okuması, geri alınabilir): D5b PR'ı kuralı bu üç yolda birlikte getirir ve
    Manufacturing README `:156`/`:252` satırlarını ve NEEDS-BUSINESS durumunu günceller.
- (f) Testler: aynı planda art arda iki iptal (her kuşak tam bir kez serbest bırakılır), iptal → yeni emir → girdi
  çıkışı yeni ayırmayı tüketir, D8'de yeniden açılıp yeniden kapanan emir, gerçek SQL ile. D5b için ayrıca: yeniden onay
  ve emir açılışı aynı girdiyi iki kez ayıramaz (çift ayırma; `UX_Allocations_ActivePlanInput`); iptal ile yeniden onay
  arasında etiketin başka talebe ayrılması ve yeniden onayın tutarlı reddi (aradaki boşluk); HamKg güncel bakiyeyi aşan
  planın reddi; müşteri malı ↔ şirket malı ve farklı `PartyId` reddi. D5 için (yalnız D5 main'deyken, D5b yokken):
  değişmemiş girdiyle iptal → yeniden onay → emir açılışı; emir açılışı Stok'a ayırma göndermez ve çift ayırma olmaz;
  yeniden onaysız emir açılışı reddedilir; farklı etiket seçimi reddedilir; legacy ayırmasız planın yeniden onayı
  reddedilir.

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
| D5 | Üretim | `OrderStatus.Cancelled`, iptal komutu (istekte emrin `ExpectedVersion`'ı), `manufacturing.orders.cancel` yalnız kayıt, ayırma serbest bırakma sebebi `OrderCancelled`, planın Onaylı'ya dönmesi (URT-D33), filtreli tekil indeks ve `PlanOrderExists` değişikliği, yeni emirde yeniden ayırma (URT-D34/URT-D35, §4.7): ayırma, D5'in getirdiği minimal yeniden onay adımında (girdi değişmeden, URT-D21 yolu, plan sürümü +1) yeniden onay ile Stok ayırması tek işlemde yapılır; emir açılışı ayırmaz (26. tur m.2, §4.7 (d), (d2)); aynı sahiplik kontrolü (URT-D38) | Migration (durum kontrolü genişler, filtreli tekil indeks, forward-fix Down), yarış testi, Stok ayırma portu sebep listesi; ayırma/serbest bırakma kimliği emir (kuşak) başına; `PlanInputAllocations` şema değişikliği + backfill + forward-fix Down; `CK_Orders_Close` iptal dalı; aynı planda art arda iki iptal testi; sahiplik kontrolü (§4.7 (e)) |
| D5b | Üretim | Farklı etikette planın yeniden hesabı ve yeniden onayı (URT-D37, N10/N11), D5'in yeniden onay adımını genişleterek: Onaylı → yeniden onay geçişi, `PlanRevisions` tarihçesi, `manufacturing_plan_slitting_spec_fixed` kısıtının kaldırılması, HamKg ≤ etiketin güncel bakiyesi (ilk kayıt, taslak revizyon ve onayda da; AT-044, README güncellemesi), yeniden onayda URT-D21 yolundan ayırma | `PlannedInput` `ParentRollId`/`ExpectedLabelVersion`/`TechnicalSnapshot`/`PlannedMass` değişimi ve izi, motor üzerinden yeniden hesap, kütle dengesi, URT-D2 ve DEC-PRDCT-008 snapshot'ı yeniden onayda, Stok bakiye okuması (fail-closed), çift ayırma ve aradaki boşluk testleri; #1116'dan bırakılan üç nokta (üst sınır bakiyesi ve pozisyon, kalan kg, kısmi tüketim) (§4.7 (e), (f)); kısıt bu dilim main'e gelene kadar yerinde kalır |
| D6 | Üretim | Ekran: UI-14 düzeltme formu, iptal düğmesi, ret kodlarının Türkçe metinleri | — |
| D7 | Üretim + Satış | İptal edilen emrin satış bağı okumasında görünmesi (MOD-03) | Satış'ın okuma tüketicisi Satış'ındır |
| D8 | Üretim | Yeniden açma: `ReopenOrder` komutu (istekte emrin `ExpectedVersion`'ı), gerekçe ve denetim kaydı, `manufacturing.orders.reopen` yalnız kayıt, Muhasebe'nin `IAccountingPeriodStatusPortV1` tüketimi (§4.6) | Migration (yeniden açma olgusu), yetki adı, çapraz modül port; `OrderClosed` serbest bırakma anahtarının emir/kuşak başına olması (yeniden açılıp kapanan emir, §4.7) |

Sıra gerekçesi: D2 ve D3, D1'in ters hareketi ve okuma portu olmadan stok tutarlılığını koruyamaz. D1 bitmeden D2
domain olarak yazılabilir, ama D3 uç olarak açılmaz. D5, D2–D4'e dayanır, çünkü iptalin ön koşulu net çıktının ve
net çıkışın sıfır olmasıdır. D5'in iki dış bağımlılığı da vardır (§6, §4.7): Stok'un `OrderCancelled=3` K3 dilimi main'e girmeden ve Stok'un Free kg okuma
portu gelmeden D5 başlamaz. D5 yeniden onay adımına bağlıdır ve bu adımı minimal haliyle (girdi değişmeden) kendisi getirir; ikinci bir ayırma yolu açılmaz. D5b, D5'e (iptal ve yeniden emir yolu) ve Stok'un etiket bakiye okumasına dayanır; gelene kadar farklı etiket seçimi reddedilir ve `manufacturing_plan_slitting_spec_fixed` kısıtı yerinde kalır. D8, D2–D3'e ve Muhasebe'nin main'deki dönem portuna dayanır; port kayıtlı değilse veya okunamıyorsa D8 fail-closed kalır.

## 6. Başka şeritlere aktarılanlar

- **Stok:** D1 (ters hareket türü + etiket durum okuma portu; portta kalite blokajındaki kg alanı ve ret kodu), D5'teki
  ayırma serbest bırakma sebebi `OrderCancelled` (Stok'un `InventoryManufacturingPlanReleaseReasonV1` listesine) ve
  yeni emirde yeniden ayırmanın eksik girdi cevabı (URT-D34). URT-D37 ile Stok'un plan ayırma sözleşmesi etkilenir:
  sözleşme plan girdisini ana etiketle anahtarlar (`DemandLineId = parent.Label.Id`); yeni emirde plan girdisi başka
  etikete geçince talep satırı değişir. Stok'a iletildi; cevap [#66 `6823794908`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6823794908): yeni etiket
  yeni `DemandLineId` alır, eski ayırma `OrderCancelled` ile serbest kalır, V1 yeterlidir. URT-D38 için yeni okuma
  gerekmez: Stok'un plan ayırması sahipliği zaten `OwnedAsPlanned` ile denetler (§4.7 (e)); erken ret için sahiplik
  `IInventoryLabelDetailsV1` `Balances.Positions[]` (`OwnerKind`, `OwnerId`) içinden okunur, okunamazsa veya pozisyon
  yoksa/birden çok sahip varsa ret (fail-closed). D5b'nin bakiye okuması da aynı porttandır.
- **Satış:** D7 tüketicisi. Üretim yalnız emir durumunu okuma portunda yayınlar.
- **Stok (ek):** D1 portu satışa ayrılmış kg'ı da döner (URT-D31). Yeniden açılan emirde çıktının ters hareketi ve
  ortalamanın yeniden hesabı Stok'un kodudur. Kural 11. tur m.2 ile karara bağlıdır ve 22. tur m.2 ile teyit edilmiştir
  (URT-D36, §4.6 madde 5). Açık olan yalnız Stok'un uygulama spec'i/golden örneği ve Stok tarafındaki karar kimliğidir.
- **Muhasebe:** Yeni iş yok. Dönem durumu main'deki `IAccountingPeriodStatusPortV1`'den okunur (D8, URT-D32).
  `accounting.periods.read` grant'i insan kapısıdır.
- **Lojistik:** Yeni iş yok. Sevk bilgisi Stok hareketlerinden okunur.

**Stok cevabı (2026-11-12, Stok şeridi oturumu `session_015Usc2pruQTAve57Km32zGP`, makbuz [#66 `6823091345`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6823091345); kod değişmedi, hepsi ST-F009-D1 kapsamında):**

- D1: Kalite blokajındaki kg, D1 portunda ayrı ve salt okunur bir alan olur. Hangi kg'ın bloklu sayılacağını Stok
  belirler.
- D5: main'de serbest bırakma sebebi olarak yalnız `PlanCancelled=1` ve `OrderClosed=2` var. `OrderCancelled=3` ve
  `manufacturing.order-cancelled` kodu V1'e ek değer olarak Stok'un K3 diliminde gelir. Üretim'in D5 dilimi bu değer
  main'e girmeden başlamaz.
- URT-D34: Bugünkü ayırma komutu etiket bazlıdır ve ya hepsini ayırır ya hiçbirini. Yetersizlikte tek bir 409
  (`inventory_manufacturing_allocation_insufficient_free_stock`) döner ve satır ayrıntısı vermez. Eksik girdileri
  göstermek için Stok yan etkisiz yeni bir okuma portu önerir: etiket/ürün başına ayrılabilir (Free) kg (örnek ad
  `IInventoryManufacturingPlanAvailabilityReadPortV1`). Üretim bu portu ayırmadan önce okur; okuma karar kaynağı değildir, yalnız
  seçim listesini ve eksik girdi listesini doldurur (TOCTOU, §4.7). Komut değişmez ve fail-closed kalır. Etiketleri kullanıcı seçer (URT-D35, 22. tur m.1); okuma portu
  seçim listesini ve eksik kg'ı gösterir. Seçilen etiketler yetmezse emir açılmaz; kısmi ayırma yoktur (URT-D34).
- Yeniden açılan emirde çıktı, girişteki kendi maliyetiyle ters harekete alınır ve ortalama bu girişin etkisi
  çıkarılarak yeniden hesaplanır (URT-D36, 22. tur m.2). Hesap Stok'un calculator'ıdır.

*Yazar notu:* bu listenin önceki sürümündeki "D8'in 'maliyeti ortalamaya girmiş emir yeniden açılmaz' kuralı
geçerlidir" cümlesi Stok makbuzunda yoktu ve yanlış atıftı; çıkarıldı. Doğrusu §4.6 madde 5'teki D8 diliminin geçici
fail-closed kuralıdır (koordinatör düzeltmesi
[`6823484848`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6823484848)).

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
| N6 | Emir açılırken girdi ürünü hangi etiketlerden ayrılır? | "Etiket önemli, kullanıcı kendisi seçer" ([`6822949341`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6822949341) m.1; soru [`6822790075`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6822790075)) | URT-D35, §6 |
| N7 | Yeniden açılan emrin çıktısı hangi maliyetle ters harekete alınır? | "Emrin kendi maliyeti (Önerilen)" ([`6822949341`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6822949341) m.2) | URT-D36, §4.6, §6 |
| N8 | İptal sonrası yeni emirde planlanandan farklı etiket seçilirse ne olsun? | "Plan yeniden hesaplanır (Önerilen)" ([`6823484848`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6823484848) m.1) | URT-D37, §4.7 |
| N9 | Yeni emirde seçilen etiketin sahipliği farklı olabilir mi? | "Aynı sahiplik zorunlu (Önerilen)" ([`6823484848`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6823484848) m.2) | URT-D38, §4.7 |
| N10 | Dilmede ana bobin kısmen dilinebilir mi? (AT-044) | "Kısmi dilinir, kalan aynı etikette (Önerilen)" ([`6823809221`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6823809221) m.1) | URT-D37 uygulama sınırı, §4.7 (e), §5 D5b |
| N11 | İptal sonrası yeniden onayda ayırma ne zaman? | "Yeniden onayda ayrılır (Önerilen)" ([`6823809221`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6823809221) m.2) | URT-D37 uygulama sınırı, §4.7 (e), §5 D5b |
