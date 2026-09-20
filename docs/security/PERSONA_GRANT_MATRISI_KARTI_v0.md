# Persona / grant matrisi — kart v0 (öneri, aktivasyon değil)

**Kaynak:** Sinan, 19 Eylül 2026 — Issue #66
[yorum 5738113623](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-5738113623) §4.
**Kart sahibi:** CARİ (merkezi yazar). **Durum:** öneri; hiçbir grant açılmadı.
**Ölçüldüğü ağaç:** `main` @ `1c88e904` — o head'de `ErpPermissions` **45** ad kaydediyordu
(5 Critical, 36 yalnız şirket-geneli). Kartın tablosu o ölçümün ürünüdür ve tarihiyle durur.

**Bugün kayıtlı olan: 48 ad — 5 Critical, 39 yalnız şirket-geneli.** (`ErpPermissions`'ın
`All`/`Critical`/`CompanyWideOnly` kümeleri bu head'de sayıldı; dört şerit bağımsız olarak aynı üç
sayıya vardı. Kartın tablosu yeniden ölçülmedi.)

**Tabloda satırı olmayan ÜÇ ad var, ve üçü de burada adıyla yazılıdır** — çünkü bir sayı, sonraki
okuyucuya tablonun tam olduğunu söyler. Eksikliği görünür kılmadan sayıyı güncellemek, karta hiç
dokunmamaktan kötü olurdu:

| Kayıtlı ad | Ne zaman geldi | Tabloda neden yok |
|---|---|---|
| `manufacturing.orders.close` | MFG-ORD, kart ölçüldükten sonra | Sinan'ın §4 metni üretim emrini **kapatma**nın hangi personada olduğunu söylemiyor |
| `sales.orders.price.write` | SALES-P (K3, #66 5748258299) | K3 fiyat yazmayı **ayrı bir izin** yapıyor ama hangi personanın taşıyacağını söylemiyor; kart K4.4'te Satış Destek'e tahsis/rezervasyon verirken **fiyat** demiyor |
| `finance.manual-receipt.record` | FIN-T05 | Kart (#66 5743678389 §1) "tek dar yetkili operatör" diyor — bir **kişi sayısı**, bir persona değil |

**Üçü de doldurulmadı, çünkü bir satır eklemek bir yetki önerisidir ve kartın kendi kuralı her satırı
Sinan'ın kendi cümlesine dayandırmaktır.** Üçünde de o cümle yok; tahminle doldurmak kartı bir
öneri olmaktan çıkarıp bir varsayım listesine çevirirdi. **Bunları kapatacak şey teknik değil:**
Sinan'ın her biri için hangi personanın taşıyacağını söylemesi. O gelene kadar üçü de **önce-reddet**
altındadır — tabloda görünmeyen ad kimseye önerilmemiş sayılır, yani eksiklik hiçbir şey açmaz.

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

## Dört persona, Sinan'ın kendi tanımıyla

| Persona | Sinan'ın tanımı (özet, §4) |
|---|---|
| **En Yetkili Admin** | Tüm ERP işlevleri; kritik insan kapıları ve production işlemleri yine mevcut güvenlik kurallarına tabi. |
| **Sevkiyatçı** | Alış/satış siparişleri, muhasebe kayıtları, stok, üretim ve operasyonel kayıtları **görebilir**; **fiyatları göremez**, **Finance para hareketleri / banka / tahsilat / ödeme bakiyelerini göremez**. "Görür", create/approve/write vermez; sevkiyatın kendi yükleme/sevk işlemleri dar write izinleriyle verilebilir. |
| **Satış Destek** | Alış ve satış siparişi **girebilir/düzenleyebilir**; sipariş **onaylayamaz**. |
| **Müdür** | Siparişleri **onaylayabilir**. Tek başına tüm modüllerde sınırsız read/write veya Finance yetkisi vermez. |

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

| İzin adı | Admin | Sevkiyatçı | Satış Destek | Müdür |
|---|---|---|---|---|
| `organization.context.read` | + | + | + | + |
| `organization.sites.read` | + | + | + | + |
| `organization.locations.read` | + | + | + | + |
| `organization.work-centers.read` | + | + | ? | ? |
| `organization.locations.status.update` | + | — | — | ? |
| `counterparty.parties.read` | + | + | + | + |
| `counterparty.parties.create` | + | — | ? | ? |
| `counterparty.parties.update.basic` | + | — | ? | ? |
| `counterparty.accounts.create` | + | — | ? | ? |
| `counterparty.accounts.status.update` | + | — | — | ? |
| `product.operational-selection.read` | + | + | + | + |
| `sales.drafts.read` | + | **engelli** | + | + |
| `sales.drafts.create` | + | — | + | ? |
| `sales.orders.approve` | + | — | **—** | + |
| `purchasing.orders.read` | + | + | + | + |
| `purchasing.orders.create` | + | — | + | ? |
| `purchasing.orders.approve` | + | — | **—** | + |
| `inventory.labels.read` | + | + | ? | ? |
| `inventory.balances.read` | + | + | ? | ? |
| `inventory.opening-stock.record` | + | — | — | ? |
| `manufacturing.orders.read` | + | + | ? | ? |
| `manufacturing.plans.write` | + | — | — | ? |
| `manufacturing.plans.approve` | + | — | — | ? |
| `manufacturing.orders.create` | + | — | — | ? |
| `manufacturing.facts.record` | + | — | — | ? |
| `logistics.loading-instructions.read` | + | + | ? | ? |
| `logistics.loading-instructions.create` | + | + | — | ? |
| `logistics.loads.record` | + | + | — | ? |
| `accounting.periods.read` | + | ? | — | ? |
| `accounting.journals.read` | + | **?** | — | ? |
| `accounting.posting-queues.read` | + | ? | — | ? |
| `accounting.periods.propose` | + | — | — | ? |
| `accounting.periods.approve` | + | — | — | ? |
| `accounting.periods.close` | + | — | — | ? |
| `finance.bank-account.read` | + | **—** | — | ? |
| `finance.manual-receipt.draft.read` | + | **—** | — | ? |
| `finance.manual-receipt.draft.save` | + | **—** | — | ? |
| `finance.manual-receipt.amount.read` | + | **—** | — | ? |
| `finance.bank-account.propose` (CRITICAL) | insan kapısı | — | — | — |
| `finance.bank-account.approve` (CRITICAL) | insan kapısı | — | — | — |
| `identity.profile.read` | + | + | + | + |
| `identity.access.request` | + | + | + | + |
| `identity.access.approve` (CRITICAL) | insan kapısı | — | — | — |
| `identity.roles.manage` (CRITICAL) | insan kapısı | — | — | — |
| `identity.audit.read` (CRITICAL) | insan kapısı | — | — | — |

### Gerekçeler (yalnız Sinan'ın cümlelerinden)

- **Sevkiyatçı'nın "görür"ü write vermez:** her `create` / `record` / `approve` / `update` adı
  Sevkiyatçı'da `—`. Tek istisna Sinan'ın kendi cümlesi: "sevkiyatın kendi yükleme/sevk işlemleri
  ilgili dar write yetkileriyle verilebilir" → `logistics.loading-instructions.create` ve
  `logistics.loads.record`.
- **Satış Destek onaylayamaz:** `sales.orders.approve` ve `purchasing.orders.approve` kalın `—`.
  Bu iki ad Müdür'ün tanımının tam karşılığı.
- **Finance ayrı tutulur:** "Sevkiyatçı her şeyi görür" banka/tahsilat/ödeme okumalarını bypass
  etmez; dört `finance.*` okuma adı da Sevkiyatçı'da `—`.
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

İzin adları her durumda sahibinin teknik işidir; bu kart ad önermez ve atama yapmaz.
