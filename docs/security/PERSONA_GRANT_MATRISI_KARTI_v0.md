# Persona / grant matrisi — kart v0 (öneri, aktivasyon değil)

**Kaynak:** Sinan, 19 Eylül 2026 — Issue #66
[yorum 5738113623](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-5738113623) §4.
**Kart sahibi:** CARİ (merkezi yazar). **Durum:** öneri; hiçbir grant açılmadı.
**Ölçüldüğü ağaç:** `main` @ `1c88e904` — o head'de `ErpPermissions` **45** ad kaydediyordu
(5 Critical, 36 yalnız şirket-geneli). Kartın tablosu o ölçümün ürünüdür ve tarihiyle durur.

**Bugün kayıtlı olan: 48 ad — 5 Critical, 39 yalnız şirket-geneli.** (`ErpPermissions`'ın
`All`/`Critical`/`CompanyWideOnly` kümeleri bu head'de sayıldı; dört şerit bağımsız olarak aynı üç
sayıya vardı. Kartın tablosu yeniden ölçülmedi.)

**Güncelleme (1 Kasım 2026, EK20/EK21):** Sinan'ın
[EK20](../decisions/SINAN_KARARLARI_2026-11-01_SATIS_EK20.md) kararları (SAT-D141…D151) iki yeni persona
(**Satış Uzmanı**, **Muhasebe**) ekledi ve yedi satış/ayar adının personasını söyledi; EK21 (SAT-D153, SAT-D155,
SAT-D159, SAT-D162, SAT-D163; PR #870) risk istisnası, Cari e-belge ve Admin'in iki Muhasebe işinin personasını söyledi. Tabloya iki sütun ve yalnız bu
kararların bağladığı satırlar eklendi (aşağıda "EK20/EK21 eşlemesi"). **Kayıtlı ad sayısı yeniden ölçülmedi;**
bugün `ErpPermissions`'ta tabloda satırı olmayan başka adlar da vardır (ör. teklif/talep, iptal, iskonto istisnası).
Onlar bu güncellemenin kapsamı değildir ve deny-first kuralıyla kimseye önerilmemiş sayılır.

**Tabloda satırı olmayan ÜÇ ad var, ve üçü de burada adıyla yazılıdır** — çünkü bir sayı, sonraki
okuyucuya tablonun tam olduğunu söyler. Eksikliği görünür kılmadan sayıyı güncellemek, karta hiç
dokunmamaktan kötü olurdu:

| Kayıtlı ad | Ne zaman geldi | Tabloda neden yok |
|---|---|---|
| `manufacturing.orders.close` | MFG-ORD, kart ölçüldükten sonra | Sinan'ın §4 metni üretim emrini **kapatma**nın hangi personada olduğunu söylemiyor |
| `sales.orders.price.write` | SALES-P (K3, #66 5748258299) | ~~K3 personayı **söylüyor** ama grant'ı **önermiyor**: *"Satış Destek, ayrı fiyat-yazma izni **verilmişse** … girebilir"* — koşullu bir cümle.~~ **KAPANDI (1 Kasım 2026, SAT-D151):** Sinan koşulu karşıladı — izin Satış Destek, Satış Uzmanı, Müdür ve Admin'e verilir. Satır artık tabloda (EK20/EK21 eşlemesi) |
| `finance.manual-receipt.record` | FIN-T05 | Kart (#66 5743678389 §1) "tek dar yetkili operatör" diyor — bir **kişi sayısı**, bir persona değil |

**Üçü de doldurulmadı, çünkü bir satır eklemek bir yetki önerisidir ve kartın kendi kuralı her satırı
Sinan'ın kendi cümlesine dayandırmaktır.** Ama üçünün eksikliği **aynı sebepten değil**, ve ayrımı
korumak kuralın kendisini korumaktır:

- **İkisinde cümle yok:** `manufacturing.orders.close` için §4 emri kimin kapatacağını hiç söylemiyor;
  `finance.manual-receipt.record` için söylenen şey *"tek dar yetkili operatör"*, yani bir **kişi
  sayısı** — bir persona değil.
- **Birinde cümle VAR ama grant önermiyor:** K3, `sales.orders.price.write` için personayı açıkça
  söylüyor (*"Satış Destek … verilmişse"*) ve tam da **koşullu** bıraktığı için bir `+` yazmak,
  Sinan'ın vermediği bir öneriyi ona atfetmek olurdu.

**1 Kasım 2026 notu:** üçüncü ad (`sales.orders.price.write`) SAT-D151 ile kapandı; yukarıdaki iki ad
(`manufacturing.orders.close`, `finance.manual-receipt.record`) aynı sebeple açık kalır.

Tahminle doldurmak kartı bir öneri olmaktan çıkarıp bir varsayım listesine çevirirdi. **Bunları
kapatacak şey teknik değil:** ilk ikisi için Sinan'ın personayı söylemesi, üçüncüsü için koşulun
karşılandığını — yani izni kime **vermek istediğini** — söylemesi. O gelene kadar üçü de
**önce-reddet** altındadır; tabloda görünmeyen ad kimseye önerilmemiş sayılır, eksiklik hiçbir şey
açmaz.

## Bu kart ne yapar, ne yapmaz

- **Yapar:** Sinan'ın dört personasını, bugün **kayıtlı** teknik izin adlarıyla eşleştirme
  *önerisi* verir; her satırın gerekçesini Sinan'ın kendi cümlesine dayandırır; cümlenin
  belirlemediği her noktayı `NEEDS-SINAN` olarak açık bırakır.
- **Yapmaz:** hiçbir rolü tohumlamaz, hiçbir kullanıcıya atama yapmaz, hiçbir izni etkinleştirmez.
  Kayıt (registration) grant değildir; grant insan kapısıdır (ADR-004, `AI_YETKI_SOZLESMESI.md`).
  Bu kart merge edilse bile çalışan sistemde tek bir yetki değişmez.
- **Yapmaz:** eksik bir izin adı icat etmez. Bir persona için gereken ad yoksa, bu kart onu
  "eksik" diye işaretler ve ilgili modül sahibinin işi olarak bırakır.

Uygulama kuralı Sinan'ın koyduğu gibi **deny-first**: bir ad listede yoksa persona onu **almaz**.

## Personalar, Sinan'ın kendi tanımıyla

İlk dört persona §4'ten (19 Eylül 2026); son ikisi EK20'den (1 Kasım 2026, SAT-D141/D142).

| Persona | Sinan'ın tanımı (özet, §4) |
|---|---|
| **En Yetkili Admin** | Tüm ERP işlevleri; kritik insan kapıları ve production işlemleri yine mevcut güvenlik kurallarına tabi. |
| **Sevkiyatçı** | Alış/satış siparişleri, muhasebe kayıtları, stok, üretim ve operasyonel kayıtları **görebilir**; **fiyatları göremez**, **Finance para hareketleri / banka / tahsilat / ödeme bakiyelerini göremez**. "Görür", create/approve/write vermez; sevkiyatın kendi yükleme/sevk işlemleri dar write izinleriyle verilebilir. |
| **Satış Destek** | Alış ve satış siparişi **girebilir/düzenleyebilir**; sipariş **onaylayamaz**. |
| **Müdür** | Siparişleri **onaylayabilir**. Tek başına tüm modüllerde sınırsız read/write veya Finance yetkisi vermez. |
| **Satış Uzmanı** (SAT-D141) | Ayrı persona; başlangıçta **Satış Destek'in bütün grant'larını** alır: sipariş girer ve düzenler, **onaylayamaz**, sipariş başlığını değiştirir. İleride ayrıştırılabilir (ör. yalnız kendi müşterileri — EK20 açık kalanı). *Uygulama yorumu (geri alınabilir, SAT-D141):* "Satış Destek gibi" kartta Satış Destek'e verilen her izni kapsar, alış siparişi izinleri dahil. EK21'de ayrıca risk istisnasını **talep eder ve onaylar** (kendi talebini onaylayamaz; SAT-D153/D159). |
| **Muhasebe** (SAT-D142) | Satış modülünde **her şeyi görür**: siparişler, fiyatlar ve tutarlar, raporlar, vergi ayarları. **Sipariş yazmaz, onaylamaz.** Şimdi dış sistemde kesilen faturayı ERP sipariş/irsaliyesiyle **eşleştirir**; fatura modülü gelince (API-32…37) faturayı **keser** (ERP fatura taslağı; hazırlayan ≠ onaylayan, SAT-D88). Vergi ayarlarını yalnız **öneri → müdür sınıfı onay** akışıyla değiştirir (SAT-D146); kalem tipini değiştirmez. Cari e-belge durumunu günceller (SAT-D155). |

## Önerilen eşleme

`+` = önerilir · `—` = verilmez (gerekçe aşağıda) · `?` = bu tabloda belirsizdi.
Admin sütunu bilerek boş bırakılmadı: "tüm ERP işlevleri" cümlesi tüm kayıtlı adları kapsar,
ama Critical adlar ayrı insan kapısına tabidir (aşağıda).

**Tablo Sinan'ın 20 Eylül kararlarından ÖNCE hazırlandı ve olduğu gibi bırakıldı** — bir kararın
neyi değiştirdiği, ancak öncesi de görülebilirse okunabilir. Kararların bağladığı satırlar için
aşağıdaki "Sinan'ın cevapları — KARAR" bölümü tabloya üstündür; çeliştiği her yerde karar geçerlidir.
Özellikle: `accounting.journals.read` Sevkiyatçı'ya verilmez ve yerine parasız dar bir okuma gerekir
(K4.1); `accounting.periods.read` Sevkiyatçı'ya verilir, `accounting.posting-queues.read` verilmez
(K4.5); Müdür sütunundaki `?` işaretleri "onayın gerektirdiği okuma evet, genel Finance hayır"
çerçevesiyle doldurulur (K4.2); Satış Destek'in stok/üretim satırları artık salt-okunur değildir —
ayrı plan ve tahsis/rezervasyon komut izinleri eşlenir (K4.4).

**EK20/EK21 eşlemesi (1 Kasım 2026).** *Satış Uzmanı* ve *Muhasebe* sütunları ile `sales.orders.approve`
satırından sonraki satırlar bu tarihte eklendi; eski hücreler değişmedi.
- **Satış Uzmanı** sütunu, var olan her satırda Satış Destek sütununun **birebir kopyasıdır** (SAT-D141 uygulama
  yorumu, geri alınabilir). Yukarıdaki K4 kararlarının Satış Destek için bağladığı her şey Satış Uzmanı için de
  aynen geçerlidir (ör. K4.3 cari oluşturma ayrı izin, K4.4 plan/tahsis).
- **Muhasebe** sütunu: satış okumaları `+` (SAT-D142 "her şeyi görür"); satış yazma ve onay adları kalın `—`
  ("sipariş yazmaz ve onaylamaz"). Kararın **susduğu** satış dışı satırlar `?` — "satışta her şeyi görür" cümlesi
  Accounting, Finance, stok veya üretim okumasını kendiliğinden vermez. Critical adlar her sütunda olduğu gibi `—`.
- *İtalik* satırların izin adı **henüz yoktur**; karar personayı söyledi, adı sahibinin teknik işi (SAT-D146 uygulama
  notu, SAT-D142 eşleştirme dilimi, fatura dalgası API-32). Ad gelene kadar bu satırlar hiçbir şey açmaz. Risk
  istisnasının iki adı `SATIS_HAZIRLIK_PLANI_v1.md` API-07'de planlıdır, manifest'te henüz yoktur.

| İzin adı | Admin | Sevkiyatçı | Satış Destek | Satış Uzmanı | Müdür | Muhasebe |
|---|---|---|---|---|---|---|
| `organization.context.read` | + | + | + | + | + | ? |
| `organization.sites.read` | + | + | + | + | + | ? |
| `organization.locations.read` | + | + | + | + | + | ? |
| `organization.work-centers.read` | + | + | ? | ? | ? | ? |
| `organization.locations.status.update` | + | — | — | — | ? | ? |
| `counterparty.parties.read` | + | + | + | + | + | ? |
| `counterparty.parties.create` | + | — | ? | ? | ? | ? |
| `counterparty.parties.update.basic` | + | — | ? | ? | ? | ? |
| `counterparty.accounts.create` | + | — | ? | ? | ? | ? |
| `counterparty.accounts.status.update` | + | — | — | — | ? | ? |
| `product.operational-selection.read` | + | + | + | + | + | ? |
| `sales.drafts.read` | + | **engelli** | + | + | + | + |
| `sales.drafts.create` | + | — | + | + | ? | **—** |
| `sales.orders.approve` | + | — | **—** | **—** | + | **—** |
| `sales.reports.read` (SAT-D143) | + | + | + | + | + | + |
| `sales.orders.price.read` (SAT-D150) | + | **—** | + | + | + | + |
| `sales.orders.price.write` (SAT-D151) | + | — | + | + | + | **—** |
| `sales.orders.header.write` (SAT-D144) | + | — | + | + | + | **—** |
| `sales.orders.risk-exception.request` (SAT-D159; ad henüz kayıtlı değil, API-07) | + | + | + | + | + | **—** |
| `sales.orders.risk-exception.approve` (SAT-D153; ad henüz kayıtlı değil, API-07) | + | — | **—** | + | + | **—** |
| `settings.tax-master.manage` (SAT-D145) | + | — | — | — | + | **—** |
| `product.item-type.manage` (SAT-D145/D149) | + | — | — | — | + | **—** |
| `counterparty.accounts.e-document.update` (SAT-D155) | + | — | — | — | — | + |
| *vergi ayarı okuma* (SAT-D146; **ad bekliyor**) | + | ? | ? | ? | + | + |
| *vergi ayarı önerme* (SAT-D146, SAT-D163; **ad bekliyor**) | + | — | — | — | — | + |
| *vergi ayarı önerisini onaylama* (SAT-D146; **ad bekliyor**) | + | — | — | — | + | **—** |
| *dış fatura ↔ sipariş/irsaliye eşleştirme* (SAT-D142, SAT-D163; **ad bekliyor**) | + | — | — | — | — | + |
| *fatura kesme = ERP fatura taslağı hazırlama* (SAT-D142, API-32; **fatura dalgası, ad bekliyor**) | ? | ? | ? | ? | ? | + *(gelecek)* |
| `purchasing.orders.read` | + | + | + | + | + | ? |
| `purchasing.orders.create` | + | — | + | + | ? | ? |
| `purchasing.orders.approve` | + | — | **—** | **—** | + | ? |
| `inventory.labels.read` | + | + | ? | ? | ? | ? |
| `inventory.balances.read` | + | + | ? | ? | ? | ? |
| `inventory.opening-stock.record` | + | — | — | — | ? | ? |
| `manufacturing.orders.read` | + | + | ? | ? | ? | ? |
| `manufacturing.plans.write` | + | — | — | — | ? | ? |
| `manufacturing.plans.approve` | + | — | — | — | ? | ? |
| `manufacturing.orders.create` | + | — | — | — | ? | ? |
| `manufacturing.facts.record` | + | — | — | — | ? | ? |
| `logistics.loading-instructions.read` | + | + | ? | ? | ? | ? |
| `logistics.loading-instructions.create` | + | + | — | — | ? | ? |
| `logistics.loads.record` | + | + | — | — | ? | ? |
| `accounting.periods.read` | + | ? | — | — | ? | ? |
| `accounting.journals.read` | + | **?** | — | — | ? | ? |
| `accounting.posting-queues.read` | + | ? | — | — | ? | ? |
| `accounting.periods.propose` | + | — | — | — | ? | ? |
| `accounting.periods.approve` | + | — | — | — | ? | ? |
| `accounting.periods.close` | + | — | — | — | ? | ? |
| `finance.bank-account.read` | + | **—** | — | — | ? | ? |
| `finance.manual-receipt.draft.read` | + | **—** | — | — | ? | ? |
| `finance.manual-receipt.draft.save` | + | **—** | — | — | ? | ? |
| `finance.manual-receipt.amount.read` | + | **—** | — | — | ? | ? |
| `finance.bank-account.propose` (CRITICAL) | insan kapısı | — | — | — | — | — |
| `finance.bank-account.approve` (CRITICAL) | insan kapısı | — | — | — | — | — |
| `identity.profile.read` | + | + | + | + | + | ? |
| `identity.access.request` | + | + | + | + | + | ? |
| `identity.access.approve` (CRITICAL) | insan kapısı | — | — | — | — | — |
| `identity.roles.manage` (CRITICAL) | insan kapısı | — | — | — | — | — |
| `identity.audit.read` (CRITICAL) | insan kapısı | — | — | — | — | — |

### Gerekçeler (yalnız Sinan'ın cümlelerinden)

- **Sevkiyatçı'nın "görür"ü write vermez:** her `create` / `record` / `approve` / `update` adı
  Sevkiyatçı'da `—`. Tek istisna Sinan'ın kendi cümlesi: "sevkiyatın kendi yükleme/sevk işlemleri
  ilgili dar write yetkileriyle verilebilir" → `logistics.loading-instructions.create` ve
  `logistics.loads.record`.
- **Satış Destek onaylayamaz:** `sales.orders.approve` ve `purchasing.orders.approve` kalın `—`.
  Bu iki ad Müdür'ün tanımının tam karşılığı.
- **Finance ayrı tutulur:** "Sevkiyatçı her şeyi görür" banka/tahsilat/ödeme okumalarını bypass
  etmez; dört `finance.*` okuma adı da Sevkiyatçı'da `—`.
- **EK20/EK21 satırları (1 Kasım 2026), yalnız karar metinlerinden:**
  - `sales.reports.read` altı personanın hepsinde `+` (SAT-D143). Sevkiyatçı `sales.orders.price.read` almadığı için
    raporlarda yalnız miktar görür; fiyat sapması raporu ona 403 döner.
  - `sales.orders.price.read` Sevkiyatçı'da kalın `—`: SAT-D150 onu açıkça dışarıda bırakır.
  - `sales.orders.price.write` ve `sales.orders.header.write`: Satış Destek, Satış Uzmanı, Müdür, Admin (SAT-D151,
    SAT-D144). Muhasebe "yazmaz"; Sevkiyatçı'nın "görür"ü write vermez. Başlık değişikliği yeniden onay gerektirirse
    onayı mevcut sipariş onay iznini tutan verir.
  - Risk istisnası **talebi**: Sevkiyatçı, Satış Destek, Satış Uzmanı, Müdür ve Admin (SAT-D159). Muhasebe yazmaz
    → `—`. Sevkiyatçı talebi **sevk/yükleme ekranından** açar; o ekran yalnız "risk limiti aşıldı, sevk duruyor"
    durumunu gösterir, limit, bakiye ve tutar göstermez; talep `sales.drafts.read` gerektirmez (SAT-D162).
    **Onayı**: Satış Uzmanı, Müdür veya Admin (SAT-D153); Satış Destek ve Sevkiyatçı onaylamaz. İsteyen kendi
    talebini onaylayamaz — bu persona değil kayıt kuralıdır.
  - `settings.tax-master.manage` ve `product.item-type.manage`: yalnız Müdür ve Admin, doğrudan geçerli (SAT-D145).
    Muhasebe vergi ayarını yalnız öneri akışıyla değiştirir (SAT-D146) ve kalem tipini değiştirmez. Kalem tipini
    bu iki persona dışında kimin **okuyacağı** sorulmadı (EK20 açık kalanı, NEEDS-SINAN); o zamana kadar yalnız
    `product.item-type.manage` sahipleri okur.
  - `counterparty.accounts.e-document.update`: Muhasebe ve Admin (SAT-D155). "Muhasebe + Müdür + Admin" ve
    "Satış Destek/Uzman + Müdür + Admin" seçenekleri gösterildi ve **seçilmedi**; bu yüzden Müdür, Satış Destek
    ve Satış Uzmanı `—`.
  - SAT-D146 ad bekleyen üç izin: okuma Muhasebe, Müdür, Admin; öneri Muhasebe ve Admin (SAT-D146, SAT-D163); onay
    **yalnız** Müdür ve Admin. Admin'in öneri izni onu onay kuralından muaf tutmaz: **kendi önerisini onaylayamaz**
    (SAT-D163, maker ≠ checker). "Muhasebe müdürü" ve "satış müdürü" unvanları ayrı persona kararı gelene kadar ek
    yetki almaz.
  - Dış fatura eşleştirme: Muhasebe ve Admin (SAT-D142, SAT-D163). Diğer personalar `—` (EK20 "yalnız Muhasebe",
    SAT-D163 yalnız Admin'i ekler). Fatura kesmede yalnız Muhasebe söylendi; diğer personalar `?`.
- **Critical adlar:** `AI_YETKI_SOZLESMESI.md` ve ADR-004 §6 gereği grant'leri ayrı insan kapısıdır;
  bu kart onları hiçbir personaya *dağıtmaz*, Admin'de bile "insan kapısı" olarak işaretler.

## Kartın bulduğu gerçek engeller ve uyarılar

1. **`sales.drafts.read` fiyat taşıyor.** `SalesOrderDraftLineDetail` bugün `UnitPrice`,
   `PriceUnitId` ve `DiscountPercent` alanlarını döndürüyor. Sinan "Sevkiyatçı satış siparişini
   görebilir ama fiyatları göremez" dediği için bu ad Sevkiyatçı'ya **bu haliyle verilemez**;
   matriste `engelli` yazmamın sebebi budur — karar eksikliği değil, okuma şekli.
   *Emsal:* LOG-4b'de onaylı sipariş satırları Logistics'e **fiyatsız** bir okumayla verildi
   (Sinan §4'ün aynı cümlesi). Aynı desen gerekiyor: satış siparişinin fiyatsız bir okuması.
   **Sahibi:** SİPARİŞ (Sales owner) — yeni bir dar ad, bu kartın işi değil.
2. **`purchasing.orders.read` bugün fiyat taşımıyor.** `PurchaseOrderLineView` yalnız miktar
   alanları döndürüyor (`Ordered/Technical/Committed/Accepted/Remaining`), fiyat yok. Bu yüzden
   Sevkiyatçı'ya `+` verilebiliyor. **Uyarı:** alış siparişine fiyat eklendiği gün bu satır
   1. maddeyle aynı duruma düşer; o dilimin sahibi bunu bilerek planlamalı.
3. **`counterparty.parties.read` müşteri adını görmenin tek yolu — ve bugün kimlik düzeyinde.**
   LOG-4b'nin `l04` tarayıcı koşusu bunu canlı gösterdi: yükleme hazırlayan aktör bu izni tutmadığı
   için Cari sahibi müşteriyi yanıtlamadı ve seçici sessizce boş kaldı. Yani Sevkiyatçı şeklindeki
   bir persona müşteri adını görebilmek için bu ada ihtiyaç duyuyor; matriste `+` olmasının sebebi
   budur. Okumanın bugünkü şekli kimlik düzeyinde: `CounterpartyDetailView` yalnız id, kod, tür,
   unvan, ticari ad, aktiflik, sürüm ve şirket hesaplarını döndürüyor — banka, IBAN, vergi, sicil
   veya fiyat alanı yok. **Uyarı, 2. maddedekiyle aynı:** bu görünüme banka/vergi/sicil alanı
   eklendiği gün bu satır da düşer ve Sevkiyatçı için dar bir okuma gerekir.

4. **`accounting.journals.read` para tutarı gösteriyor.** Sinan "muhasebe kayıtlarını görebilir"
   diyor ama aynı cümlede "Finance para hareketlerini göremez" diyor. Muhasebe fişi tutarsız
   okunamaz. Bu gerçek bir belirsizlik; tahminle kapatmıyorum.

## Cari sahibine kalan teknik madde (bu kartın kararı değil)

`counterparty.parties.read` **eksikken** okuma `null` yanıtlıyor; tüketen modül bunu "böyle bir cari
yok"tan ayırt edemiyor. LOG-4b'de zinciri şuydu: `ResolveAuthorizedCompanyIdsForDetailAsync` → `null`
→ Logistics port'u NotFound → ekranda boş liste. Fail-closed olduğu için hiçbir şey sızmıyor ve bugün
yanlış bir davranış yok; ama yetkisi olmayan kullanıcı "yetkiniz yok" yerine sessiz bir boşluk
görüyor. Bu, bu kartın veya bir suite PR'ının içinde değiştirilecek bir şey değil: yetki yolunda
kendi dilimini ve kendi kanıtını hak ediyor. **Sahibi:** CARİ. (SİPARİŞ'in LOG-4b sırasındaki
tespiti; koordinatör 19 Eylül 2026'da Cari maddesi olarak kaydetti.)

## Sinan'ın cevapları — KARAR (20 Eylül 2026, #66 5748258299 K4/K5)

Kartın beş sorusu kapandı. Aşağıdakiler öneri değil **karar**dır; yine de hiçbiri grant değildir —
izin adları sahibinin teknik işi, atama ve production grant insan kapısıdır.

**1. Sevkiyatçı muhasebe tutarı: HAYIR.** Yalnız belge, tarih, durum ve operasyonel muhasebe
bağlantısı görür; borç/alacak ve tutar, fiyat, marj, Finance hareketi ve bakiye görmez.
**Doğrudan teknik sonucu var ve kartın tablosunu değiştirir:** tutar döndüren mevcut
`accounting.journals.read` bu rolün ihtiyacını **karşılamaz**. Sunucudan **parasız (money-free) dar
bir projection ve kendi izni** gerekir; alanı frontend'de gizlemek **yeterli değildir** — bu cümle
kararın parçasıdır, çünkü aksi hâlde biri bunu React'te "çözer". Sahibi: Accounting owner.
Dolayısıyla matristeki `accounting.journals.read` satırı Sevkiyatçı için `—` olarak kalır ve yerine
gelecek dar okuma ayrı bir addır.

**2. Müdür:** sipariş onayı için gereken alış/satış, cari/ürün ve karar özetlerini okur; onayın
fiyat/tutar bağlamı **ilgili dar fiyat okumasıyla** sağlanır. Finance'te ve diğer modüllerde genel
erişim bundan doğmaz; maliyet, marj ve banka okuması sınırsız değildir. Matristeki Müdür `?`
işaretleri bu çerçevede doldurulur: onayın gerektirdiği okuma evet, genel Finance hayır.

**3. Satış Destek ve cari kartı:** başlangıçta **mevcut** cariyi ve şirket hesabını seçer. Cari veya
hesap **oluşturmak ayrı ve dar bir izindir**, sipariş girme yetkisiyle otomatik gelmez.

**4. Satış Destek stok/üretim: READ-ONLY DEĞİL.** Sinan az personelle çalışıldığı için **şimdilik**
Satış Destek'in üretim planı yapabilmesini ve müşteri/sipariş için malzeme tahsis/rezerve
edebilmesini istedi. Gereken stok/arz/üretim okumaları ile **ayrı plan oluştur/düzenle** ve
**tahsis/rezervasyon** komut izinleri eşlenir. Bu cümleden **otomatik çıkmayanlar**: sipariş
onaylama, üretim planı onaylama, fiziksel stok düzeltme, üretim gerçekleşmesi, sevkiyat, para
kaydı. Korunan kural aynen durur: **talep/taslak fiziksel stok bloke etmez; aktif satış tahsisi iç
onaylı siparişe dayanır.** Müşteri bağlamından başlatılan işlem uygun onaylı satıra bağlanır; bu
karar bağsız serbest rezervasyon ya da aşım toleransı **seçmez**. Hak ileride yönetici tarafından
kaldırılabilir; kendiliğinden bir süre sonu **uydurulmaz**.

**5. Sevkiyatçı dönem/queue:** dönem **açık/kapalı durumunu okuyabilir**; posting ve error queue
verilmez, dönem açma/onaylama/kapatma yazmaları verilmez. Yani `accounting.periods.read` evet,
`accounting.posting-queues.read` hayır, üç period yazma adı hayır.

**Kabul ölçütü (Sinan'ın kendi cümlesi):** Satış Destek ayrı plan+tahsis izinleriyle plan ve tahsis
kaydedebilir ama sipariş onayı ile fiziksel gerçekleşme/para yetkisini almaz; bu iki izin
kaldırıldığında sipariş girişi kalır, plan/tahsis komutları reddedilir.

## UX kararı (K5, 20 Eylül 2026) — yetki genişletmez

Kart bu soruyu bilerek açık bırakmıştı: sidebar oluşturma eylemi sunmalı mı? Sinan karar verdi.

**Sidebar modülleri ve çalışma listelerini taşır. Oluşturma, ilgili listenin içinde tek ve yetkili
bir "Yeni …" eylemidir.** Cari, ürün ve sipariş için paralel sidebar-create + liste-create
çoğaltması kaldırılır. Bağlamdan gelen gerçek iş kısayolları ("bu cari için sipariş/tahsilat")
korunur, ama **aynı canonical route**'u kullanır ve bağlamı taşır; ikinci bir kayıt motoru
kurulmaz. Hiçbir yerden ulaşılamayan ekranlar (MFG listesi gibi) modülünden veya listesinden
bağlanır.

Gerekçe Sinan'ın: sidebar sade kalır, kişi önce kayıtlarını görür, aynı adlı iki linkin belirsizliği
biter, tek form korunur.

**Bu bir çalışma biçimi kararıdır; hiçbir rolü ve hiçbir izni genişletmez.** Sidebar'da bir
oluşturma girdisi görmek o eylemi yapma yetkisi anlamına gelmez: girdi yalnız izni olana çizilir ve
izin insan kapısından gelir.

İlişki: `docs/delivery/TEKNIK_TUZAKLAR.md` §8'deki "tek giriş noktası" kuralı bu kararın teknik
tarafıdır ve onunla uyumludur — L-03 düzeltildi, Ürün ve Sipariş taslağı için takip maddeleri açık.

## Sonraki adım

Bu kart merge edilse bile hiçbir şey etkinleşmez. Beş soru artık yanıtlı olduğuna göre sıradaki
işler sahipleriyle şunlardır ve **hiçbiri bu kartın kapsamı değildir**:

1. **Fiyatsız satış okuması** — Sevkiyatçı sipariş görebilsin ama fiyat görmesin (LOG-4b'nin
   fiyatsız okuma emsali). Sahibi: Sales owner.
2. **Parasız muhasebe projection'ı + kendi izni** — K4.1'in doğrudan sonucu; `accounting.journals.read`
   tutar döndürdüğü için bu rolü karşılamıyor, ve bu **sunucu tarafında** çözülür. Sahibi:
   Accounting owner.
3. **Plan oluştur/düzenle ve tahsis/rezervasyon izinleri** — K4.4'ün gerektirdiği yeni komut adları,
   korunan "taslak stok bloke etmez / aktif tahsis iç onaylı siparişe dayanır" kuralıyla birlikte.
   Sahipleri: Manufacturing ve Inventory owner'ları.
4. **Dar fiyat okuması** — Müdür'ün sipariş onayı bağlamı için (K4.2). Sahibi: Sales owner.
5. **Cari/hesap oluşturma için ayrı dar izin** — K4.3. Sahibi: Cari owner.
6. Ardından persona başına rol kodlarının Identity modelinde tasarlanması ve **en sonda** grant'lerin
   insan kapısında açılması.
7. **EK20/EK21 grant PR'ı** (SAT-D141…D146, SAT-D150/D151, SAT-D153/D155/D159): persona/rol tanımı ve izin eşlemesi
   yalnız bu kartın EK20/EK21 satırlarını açar; ad bekleyen satırlar (risk istisnası, SAT-D146 vergi önerisi,
   eşleştirme) adları manifest'e gelince eşlenir. Aynı PR `ErpPermissions` açıklamalarını kararlarla hizalar.

**İnsan kapısı değişmez:** EK20/EK21 grant'ları Sinan'ın somut kararıdır, ama bu kart da, o kararlarla açılacak
eşleme PR'ı da **gerçek bir kullanıcıya rol atamaz** ve **production'da hiçbir şeyi etkinleştirmez**. Gerçek rol
ataması, Critical grant'ler ve production aktivasyonu her zaman insan kapısında kalır (SAT-D110; EK20 ve EK21
"Sınırlar").

İzin adları her durumda sahibinin teknik işidir; bu kart ad önermez ve atama yapmaz.
