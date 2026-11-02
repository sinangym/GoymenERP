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
(**Satış Uzmanı**, **Muhasebe**) ekledi ve altı kayıtlı satış/ayar adının (`sales.reports.read`,
`sales.orders.price.read`, `sales.orders.price.write`, `sales.orders.header.write`, `settings.tax-master.manage`,
`product.item-type.manage`) personasını söyledi; [EK21](../decisions/SINAN_KARARLARI_2026-11-01_SATIS_EK21.md)
(SAT-D153, SAT-D155, SAT-D159, SAT-D162, SAT-D163; #870, main @ `c89d22e9`) risk istisnası, Cari e-belge ve Admin'in iki Muhasebe işinin personasını söyledi;
[EK22](../decisions/SINAN_KARARLARI_2026-11-01_SATIS_EK22.md) (SAT-D164) Muhasebe'nin dört temel okumasını söyledi.
Tabloya iki sütun ve yalnız bu kararların bağladığı satırlar eklendi (aşağıda "EK20/EK21 eşlemesi"). **Kayıtlı ad sayısı yeniden ölçülmedi;**
bugün `ErpPermissions`'ta tabloda satırı olmayan başka adlar da vardır (ör. teklif/talep, iptal, iskonto istisnası).
Onlar bu güncellemenin kapsamı değildir ve deny-first kuralıyla kimseye önerilmemiş sayılır.

**Güncelleme (2 Kasım 2026, EK24–EK28 — müdür ayrımı):** Sinan'ın
[EK28](../decisions/SINAN_KARARLARI_2026-11-02_SATIS_EK28.md) kararları (SAT-D186…D190) tek **Müdür** personasını
**Satış Müdürü**, **Muhasebe Müdürü** ve **Genel Müdür**'e ayırdı; öteki modül müdürleri (Satın Alma Müdürü, Üretim
Müdürü …) kendi şeritlerinde tanımlanır ve o zamana kadar onların hücrelerini **Genel Müdür + Admin** tutar (SAT-D187).
Tablodaki eski Müdür sütunu silinmedi, **"Müdür (eski)"** adıyla iz olarak durur; her hücresi SAT-D188 sınıflandırmasıyla üç
yeni sütuna dağıtıldı ve değişen her hücre karar numarasını taşır. EK24 (SAT-D168, SAT-D169), EK25 (SAT-D179), EK27
(SAT-D185) ve EK28 (SAT-D186, SAT-D189) satırları ile eski Satış kararlarının (SAT-D1, D4, D24/D44, D37, D65, D99, D110)
satırı olmayan Müdür hücreleri eklendi. Sınıfı açık olmayan hücreler tahminle doldurulmadı: **`NEEDS-SINAN`** (NS-n) ile
işaretlidir ve kartın sonunda listelenir. Ayrıntı: aşağıda "EK28 eşlemesi".

**Tabloda satırı olmayan ÜÇ ad vardı (1 Kasım 2026'dan beri İKİ: `sales.orders.price.write` SAT-D151 ile kapandı ve tabloya girdi), ve üçü de burada adıyla yazılıdır** — çünkü bir sayı, sonraki
okuyucuya tablonun tam olduğunu söyler. Eksikliği görünür kılmadan sayıyı güncellemek, karta hiç
dokunmamaktan kötü olurdu:

| Kayıtlı ad | Ne zaman geldi | Tabloda neden yok |
|---|---|---|
| `manufacturing.orders.close` | MFG-ORD, kart ölçüldükten sonra | Sinan'ın §4 metni üretim emrini **kapatma**nın hangi personada olduğunu söylemiyor |
| `sales.orders.price.write` | SALES-P (K3, #66 5748258299) | ~~K3 personayı **söylüyor** ama grant'ı **önermiyor**: *"Satış Destek, ayrı fiyat-yazma izni **verilmişse** … girebilir"* — koşullu bir cümle.~~ **KAPANDI (1 Kasım 2026, SAT-D151):** Sinan koşulu karşıladı — izin Satış Destek, Satış Uzmanı, Müdür ve Admin'e verilir (2 Kasım 2026: Müdür → Satış Müdürü ve Genel Müdür, SAT-D188). Satır artık tabloda (EK20/EK21 eşlemesi) |
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

İlk dört persona §4'ten (19 Eylül 2026); Satış Uzmanı ve Muhasebe EK20'den (1 Kasım 2026, SAT-D141/D142); üç müdür
personası EK28'den (2 Kasım 2026, SAT-D187…D190). §4'teki tek **Müdür** satırı tarihî iz olarak üstü çizili durur.

**Bir kişiye birden fazla müdürlük verilebilir (SAT-D187).** Persona bir yetki paketidir, kişi değildir: aynı kişi hem
Satış Müdürü hem Muhasebe Müdürü olabilir ve iki paketin birleşimini alır. Bu, kayıt düzeyindeki kuralları gevşetmez —
hazırlayan/isteyen kendi kaydını onaylayamaz (P6, SAT-D37, SAT-D153, SAT-D163, SAT-D186); bu kural personaya değil kayda
bağlıdır.

| Persona | Sinan'ın tanımı (özet, §4) |
|---|---|
| **En Yetkili Admin** | Tüm ERP işlevleri; kritik insan kapıları ve production işlemleri yine mevcut güvenlik kurallarına tabi. Müdürlerin yaptığı her şeyi yapabilir (SAT-D187, EK28 soru 2 ve 4); Genel Müdür'ün kapsamı dışındaki sistem yönetimi yetkileri yalnız Admin'dedir (SAT-D190). |
| **Sevkiyatçı** | Alış/satış siparişleri, muhasebe kayıtları, stok, üretim ve operasyonel kayıtları **görebilir**; **fiyatları göremez**, **Finance para hareketleri / banka / tahsilat / ödeme bakiyelerini göremez**. "Görür", create/approve/write vermez; sevkiyatın kendi yükleme/sevk işlemleri dar write izinleriyle verilebilir. |
| **Satış Destek** | Alış ve satış siparişi **girebilir/düzenleyebilir**; sipariş **onaylayamaz**. |
| ~~**Müdür**~~ *(19 Eylül §4; 2 Kasım 2026'da SAT-D187 ile ayrıldı — tabloda "Müdür (eski)" sütunu)* | ~~Siparişleri **onaylayabilir**. Tek başına tüm modüllerde sınırsız read/write veya Finance yetkisi vermez.~~ Bu tanımın yerine aşağıdaki üç müdür personası ve modül müdürü yer tutucuları geçer. K4.2'nin sınırı ("onayın gerektirdiği okuma evet, genel Finance hayır") her müdür personası için aynen geçerlidir. |
| **Satış Uzmanı** (SAT-D141) | Ayrı persona; başlangıçta **Satış Destek'in bütün grant'larını** alır: sipariş girer ve düzenler, **onaylayamaz**, sipariş başlığını değiştirir. İleride ayrıştırılabilir (ör. yalnız kendi müşterileri — EK20 açık kalanı). *Uygulama yorumu (geri alınabilir, SAT-D141):* "Satış Destek gibi" kartta Satış Destek'e verilen her izni kapsar, alış siparişi izinleri dahil. EK21'de ayrıca risk istisnasını **talep eder ve onaylar** (kendi talebini onaylayamaz; SAT-D153/D159). |
| **Muhasebe** (SAT-D142) | Satış modülünde **her şeyi görür**: siparişler, fiyatlar ve tutarlar, raporlar, vergi ayarları. **Sipariş yazmaz, onaylamaz.** Şimdi dış sistemde kesilen faturayı ERP sipariş/irsaliyesiyle **eşleştirir**; fatura modülü gelince (API-32…37) faturayı **keser** (ERP fatura taslağı; hazırlayan ≠ onaylayan, SAT-D88). Vergi ayarlarını yalnız **öneri → onay** akışıyla değiştirir (SAT-D146; onaylayan 2 Kasım 2026'dan beri Muhasebe Müdürü, Genel Müdür veya Admin — SAT-D188); kalem tipini değiştirmez. Cari e-belge durumunu günceller (SAT-D155). |
| **Satış Müdürü** (SAT-D187, SAT-D188) | Eski Müdür'ün **satış** yetkilerini alır: satış siparişi onayı (SAT-D6), taban fiyat ve iskonto istisnası onayı (SAT-D1/D2/D37), miktar revizyonunun yeniden onayı (SAT-D3), iptal (SAT-D4), süresi geçmiş teklif onayı (SAT-D24/D44), fiyat ve başlık yazma (SAT-D151, SAT-D144), risk istisnası talebi ve onayı (SAT-D159, SAT-D153), fazla teslim onayı ve kapatma (SAT-D168), kaynak planı (SAT-D179), satış ayarları (SAT-D189). "Müdür" iskonto limit grubu **Satış Müdürü** grubudur (SAT-D188, soru 7). Okumaları Muhasebe Müdürü ile ortaktır. Vergi/muhasebe yetkisi **almaz**. |
| **Muhasebe Müdürü** (SAT-D187, SAT-D188) | Eski Müdür'ün **vergi/muhasebe** yetkilerini alır: vergi ayarlarını ve kalem tipini doğrudan değiştirme (SAT-D145/D149; SAT-D148/D158/D181 değer ve liste girişi bunun içindedir), Muhasebe'nin vergi ayarı önerisini onaylama (SAT-D146), kur düzeltmesi (SAT-D169), belge tarihinden önceki sevklerde istisna onayı (SAT-D186). Okumaları Satış Müdürü ile ortaktır. Satış yetkisi **almaz**. Muhasebe personasının (SAT-D142) yetkilerini kendiliğinden almaz; aynı kişiye iki persona birlikte verilebilir. |
| **Genel Müdür** (SAT-D187, SAT-D190) | **Bütün modüllerde** müdürlerin yaptığı onay ve değişiklikleri yapabilir; her müdür hücresinde yetkilidir (SAT-D188, soru 9). Admin'in **sistem yönetimi** yetkileri (kullanıcı, rol, sistem ayarı) bu kapsamda **değildir** (SAT-D190). Öteki modül müdürleri tanımlanana kadar o modüllerin müdür hücrelerini Admin ile birlikte tutar (SAT-D187). |
| *Satın Alma Müdürü, Üretim Müdürü … (yer tutucu)* (SAT-D187) | Her modülün kendi müdürü, yetkisi kendi modülünde kalır. Bu personalar **o modüllerin karar dosyalarında** tanımlanır (Alış ve Üretim şeritleri; ALS-D3 alış siparişi onayı ve ALS-D6 kur düzeltmesinin güncellenmesi Alış şeridinin işidir). Karta girene kadar sütunları yoktur; hücreleri **Genel Müdür + Admin**'dedir. |

## Önerilen eşleme

`+` = önerilir · `—` = verilmez (gerekçe aşağıda) · `?` = bu tabloda belirsizdi. · **NS-n** = `NEEDS-SINAN`, müdür ayrımında sınıfı açık olmayan hücre (kartın sonundaki listede).
Admin sütunu bilerek boş bırakılmadı: "tüm ERP işlevleri" cümlesi tüm kayıtlı adları kapsar,
ama Critical adlar ayrı insan kapısına tabidir (aşağıda).

**Tablo Sinan'ın 20 Eylül kararlarından ÖNCE hazırlandı ve olduğu gibi bırakıldı** — bir kararın
neyi değiştirdiği, ancak öncesi de görülebilirse okunabilir. Kararların bağladığı satırlar için
aşağıdaki "Sinan'ın cevapları — KARAR" bölümü tabloya üstündür; çeliştiği her yerde karar geçerlidir.
Özellikle: `accounting.journals.read` Sevkiyatçı'ya verilmez ve yerine parasız dar bir okuma gerekir
(K4.1); `accounting.periods.read` Sevkiyatçı'ya verilir, `accounting.posting-queues.read` verilmez
(K4.5); Müdür sütunundaki (2 Kasım 2026'dan beri "Müdür (eski)"; K4.2 her müdür personası için aynen geçerlidir) `?` işaretleri "onayın gerektirdiği okuma evet, genel Finance hayır"
çerçevesiyle doldurulur (K4.2); Satış Destek'in stok/üretim satırları artık salt-okunur değildir —
ayrı plan ve tahsis/rezervasyon komut izinleri eşlenir (K4.4).

**EK20/EK21 eşlemesi (1 Kasım 2026).** *Satış Uzmanı* ve *Muhasebe* sütunları ile `sales.orders.approve` ve
`purchasing.orders.read` satırları **arasına** giren on dört satır (karar numarası veya "ad bekliyor" ile işaretli)
bu tarihte eklendi. `purchasing.*` ve sonrasındaki satırlar eskidir; eski dört sütunun hücreleri değişmedi.
- **Satış Uzmanı** sütunu, var olan her satırda Satış Destek sütununun **birebir kopyasıdır** (SAT-D141 uygulama
  yorumu, geri alınabilir). Yukarıdaki K4 kararlarının Satış Destek için bağladığı her şey Satış Uzmanı için de
  aynen geçerlidir (ör. K4.3 cari oluşturma ayrı izin, K4.4 plan/tahsis).
- **Muhasebe** sütunu: satış okumaları `+` (SAT-D142 "her şeyi görür"); satış yazma ve onay adları kalın `—`
  ("sipariş yazmaz ve onaylamaz"). Dört temel okuma `+` (SAT-D164, yalnız okuma): `organization.context.read`,
  `identity.profile.read`, `counterparty.parties.read`, `product.operational-selection.read` — bunlar olmadan
  "her şeyi görür" ve SAT-D155 e-belge komutu (ek olarak `counterparty.parties.read` ister) çalışmaz. Accounting ve
  Finance **okumaları** `—`: SAT-D164'te "ek olarak muhasebe/finans okumaları" seçeneği gösterildi ve Sinan onu
  **seçmedi** (kartın "gösterildi, seçilmedi → `—`" kuralı, SAT-D155 ile aynı). Kararın **susduğu** diğer satış dışı
  satırlar (stok, üretim, lojistik, alış, organizasyon yazmaları, Accounting/Finance yazmaları) `?`. Critical adlar
  her sütunda olduğu gibi `—`.
- *İtalik* satırların izin adı **henüz yoktur**; karar personayı söyledi, adı sahibinin teknik işi (SAT-D146 uygulama
  notu, SAT-D142 eşleştirme dilimi, fatura dalgası API-32). Ad gelene kadar bu satırlar hiçbir şey açmaz. Risk
  istisnasının iki adı `SATIS_HAZIRLIK_PLANI_v1.md` API-07'de planlıdır, manifest'te henüz yoktur.

**EK28 eşlemesi (2 Kasım 2026, SAT-D186…D190; EK24–EK27 satırlarıyla).** Eski **Müdür** sütunu "Müdür (eski)" adıyla
**değiştirilmeden** durur; yalnız izdir ve hiçbir şey önermez. Yerine gelen üç sütun SAT-D188'in sınıflandırmasıyla
doldurulur; değişen her hücre karar numarasını taşır:
- **Satış** yetkileri (onay, iptal, istisna, fiyat/başlık yazma, risk istisnası, kapatma, kaynak planı, satış ayarları)
  → **Satış Müdürü**; Muhasebe Müdürü bu hücrelerde `—` *(SAT-D188)*.
- **Vergi/muhasebe** yetkileri (vergi ayarı ve kalem tipi, vergi önerisi onayı, kur düzeltmesi, istisna onayı)
  → **Muhasebe Müdürü**; Satış Müdürü bu hücrelerde `—` *(SAT-D188)*. EK28'in iki **daraltması** kalın `—` ile
  gösterilir: vergi önerisi onayını Satış Müdürü vermez; kur düzeltmesini Satış Müdürü yapmaz (soru 8'de "İki müdür de"
  gösterildi ve seçilmedi).
- Eski Müdür'ün `+` **okumaları** → **iki müdüre de** `+` *(SAT-D188 okuma)*.
- **Alış** onayı (`purchasing.orders.approve`, ALS-D3) → Satın Alma Müdürü; o persona Alış şeridinde tanımlanana kadar
  hücre **Genel Müdür + Admin**'dedir (SAT-D187); Satış ve Muhasebe Müdürü `—`.
- **Genel Müdür** her müdür hücresinde yetkilidir *(SAT-D188/D190)*: bir hücre herhangi bir müdürde `+` ise Genel
  Müdür'de de `+`. Eski Müdür'ün **almadığı** bir şeyi (ör. SAT-D155 e-belge, SAT-D65 limit düzenleme) Genel Müdür de
  almaz; Admin'in Critical/sistem yönetimi adları kapsam dışıdır (SAT-D190).
- Eski Müdür hücresi `?` olan ve satış/muhasebe sınıfına açıkça girmeyen her satır (organizasyon, cari oluşturma,
  stok, üretim, lojistik, muhasebe dönemi/fişi, Finance, alış siparişi girişi) üç sütunda da `?` **NS-n** olarak
  işaretlidir; tahminle doldurulmadı. Liste: kartın sonunda "NEEDS-SINAN — müdür ayrımı".
- **Yeni satırlar** (bu tarihte eklendi): eski Satış kararlarının tabloda satırı olmayan Müdür hücreleri
  (SAT-D1, SAT-D2/D37/D67, SAT-D4, SAT-D24/D44, SAT-D65/D69, SAT-D99, SAT-D110) ile EK24–EK28'in persona söyleyen
  kararları (SAT-D168, SAT-D169, SAT-D179, SAT-D186, SAT-D189). Bu satırlarda müdür dışı sütunlar yalnız kararın
  kendi metnindendir: kararın saydığı kişi `+`, kararın saydığı kümenin dışında kalan `—` (deny-first), kararın hiç
  değinmediği persona `?`. Bu satırlar da hiçbir şey açmaz; izin adları sahibinin teknik işidir ve atama insan
  kapısıdır.

| İzin adı | Admin | Sevkiyatçı | Satış Destek | Satış Uzmanı | Müdür (eski) | Muhasebe | Satış Müdürü | Muhasebe Müdürü | Genel Müdür |
|---|---|---|---|---|---|---|---|---|---|
| `organization.context.read` | + | + | + | + | + | + *(SAT-D164)* | + *(SAT-D188 okuma)* | + *(SAT-D188 okuma)* | + *(SAT-D188/D190)* |
| `organization.sites.read` | + | + | + | + | + | ? | + *(SAT-D188 okuma)* | + *(SAT-D188 okuma)* | + *(SAT-D188/D190)* |
| `organization.locations.read` | + | + | + | + | + | ? | + *(SAT-D188 okuma)* | + *(SAT-D188 okuma)* | + *(SAT-D188/D190)* |
| `organization.work-centers.read` | + | + | ? | ? | ? | ? | ? **NS-3** | ? **NS-3** | ? **NS-3** |
| `organization.locations.status.update` | + | — | — | — | ? | ? | ? **NS-3** | ? **NS-3** | ? **NS-3** |
| `counterparty.parties.read` | + | + | + | + | + | + *(SAT-D164)* | + *(SAT-D188 okuma)* | + *(SAT-D188 okuma)* | + *(SAT-D188/D190)* |
| `counterparty.parties.create` | + | — | ? | ? | ? | ? | ? **NS-4** | ? **NS-4** | ? **NS-4** |
| `counterparty.parties.update.basic` | + | — | ? | ? | ? | ? | ? **NS-4** | ? **NS-4** | ? **NS-4** |
| `counterparty.accounts.create` | + | — | ? | ? | ? | ? | ? **NS-4** | ? **NS-4** | ? **NS-4** |
| `counterparty.accounts.status.update` | + | — | — | — | ? | ? | ? **NS-4** | ? **NS-4** | ? **NS-4** |
| `product.operational-selection.read` | + | + | + | + | + | + *(SAT-D164)* | + *(SAT-D188 okuma)* | + *(SAT-D188 okuma)* | + *(SAT-D188/D190)* |
| `sales.drafts.read` | + | **engelli** | + | + | + | + | + *(SAT-D188 okuma)* | + *(SAT-D188 okuma)* | + *(SAT-D188/D190)* |
| `sales.drafts.create` | + | — | + | + | ? | **—** | ? **NS-12** *(SAT-D188 satış)* | — *(SAT-D188 satış)* | ? **NS-12** |
| `sales.orders.approve` | + | — | **—** | **—** | + | **—** | + *(SAT-D6, SAT-D188)* | **—** *(SAT-D188)* | + *(SAT-D188/D190)* |
| `sales.reports.read` (SAT-D143) | + | + | + | + | + | + | + *(SAT-D143, SAT-D188)* | + *(SAT-D143, SAT-D188)* | + *(SAT-D188/D190)* |
| `sales.orders.price.read` (SAT-D150) | + | **—** | + | + | + | + | + *(SAT-D150, SAT-D188)* | + *(SAT-D150, SAT-D188)* | + *(SAT-D188/D190)* |
| `sales.orders.price.write` (SAT-D151) | + | — | + | + | + | **—** | + *(SAT-D151, SAT-D188)* | — *(SAT-D188)* | + *(SAT-D188/D190)* |
| `sales.orders.header.write` (SAT-D144) | + | — | + | + | + | **—** | + *(SAT-D144, SAT-D185, SAT-D188)* | — *(SAT-D188)* | + *(SAT-D188/D190)* |
| `sales.orders.risk-exception.request` (SAT-D159; ad henüz kayıtlı değil, API-07) | + | + | + | + | + | ? | + *(SAT-D159, SAT-D188)* | — *(SAT-D188)* | + *(SAT-D188/D190)* |
| `sales.orders.risk-exception.approve` (SAT-D153; ad henüz kayıtlı değil, API-07) | + | — | **—** | + | + | **—** | + *(SAT-D153, SAT-D188)* | **—** *(SAT-D188)* | + *(SAT-D188/D190)* |
| `sales.orders.price-floor-exception.approve` (SAT-D1) | + | — | **—** | **—** | + *(SAT-D1)* | **—** | + *(SAT-D1, SAT-D188)* | — *(SAT-D188)* | + *(SAT-D188/D190)* |
| `sales.orders.discount-exception.approve` (SAT-D2/D37/D67) | + | — | **—** | **—** | + *(SAT-D37)* | **—** | + *(SAT-D37, SAT-D188)* | — *(SAT-D188)* | + *(SAT-D188/D190)* |
| `sales.orders.cancel` (SAT-D4) | + | — | ? | ? | + *(SAT-D4)* | **—** | + *(SAT-D4, SAT-D188)* | — *(SAT-D188)* | + *(SAT-D188/D190)* |
| `sales.quotes.expired-conversion.approve` (SAT-D24/D44) | + | — | ? | ? | + *(SAT-D24 "yönetici")* | **—** | + *(SAT-D24/D44, SAT-D188)* | — *(SAT-D188)* | + *(SAT-D188/D190)* |
| `sales.invoices.read` (SAT-D110) | + | ? | + *(SAT-D110)* | + *(SAT-D110, SAT-D141 kopya)* | + *(SAT-D110)* | + *(SAT-D110)* | + *(SAT-D110, SAT-D188 okuma)* | + *(SAT-D110, SAT-D188 okuma)* | + *(SAT-D188/D190)* |
| `settings.sales-discount-limits.manage` (SAT-D65/D69) | + *(SAT-D69)* | — | **—** *(SAT-D65)* | **—** *(SAT-D65, SAT-D141 kopya)* | **—** *(SAT-D65)* | ? | **—** *(SAT-D65)* | **—** *(SAT-D65)* | **—** *(SAT-D65)* |
| *fazla teslim onayı, eksik teslimle satır kapatma, sipariş kapatma* (SAT-D168; **ad bekliyor**) | + *(SAT-D168)* | — | — *(SAT-D168)* | + *(SAT-D168)* | + *(SAT-D168)* | — *(SAT-D168)* | + *(SAT-D168, SAT-D188)* | — *(SAT-D188)* | + *(SAT-D188/D190)* |
| `sales.orders.source-plan.write` (SAT-D179; ad henüz kayıtlı değil, API-11) | + *(SAT-D179)* | — *(SAT-D179)* | + *(SAT-D179)* | + *(SAT-D179)* | + *(SAT-D179)* | — *(SAT-D179)* | + *(SAT-D179, SAT-D188)* | — *(SAT-D188)* | + *(SAT-D188/D190)* |
| *satış ayarları: teklif kayıp nedenleri, varsayılan teklif geçerlilik günü, teslim toleransı yüzdeleri* (SAT-D189; **ad bekliyor**) | + *(SAT-D189)* | — | — | **—** *(SAT-D189)* | + *(EK28 soru 3)* | — | + *(SAT-D189)* | **—** *(SAT-D189)* | + *(SAT-D189)* |
| *"faturalanmış, sevk edilmemiş" bakiyeyi görme ve yönetme* (SAT-D99; **ad bekliyor**) | + *(SAT-D187)* | ? | ? *(SAT-D99 "satışçı")* | ? *(SAT-D99 "satışçı")* | + *(SAT-D99)* | + *(SAT-D99 yorum)* | ? **NS-1** | ? **NS-1** | + *(SAT-D188/D190)* |
| `settings.tax-master.manage` (SAT-D145) | + | — | — | — | + | **—** | **—** *(SAT-D188)* | + *(SAT-D145, SAT-D188)* | + *(SAT-D188/D190)* |
| `product.item-type.manage` (SAT-D145/D149) | + | — | — | — | + | **—** | **—** *(SAT-D188)* | + *(SAT-D145/D149, SAT-D188)* | + *(SAT-D188/D190)* |
| `counterparty.accounts.e-document.update` (SAT-D155) | + | — | — | — | — | + | — *(SAT-D155)* | — *(SAT-D155)* | — *(SAT-D155)* |
| *vergi ayarı okuma* (SAT-D146; **ad bekliyor**) | + | ? | ? | ? | + | + | + *(SAT-D188 okuma)* | + *(SAT-D188 okuma)* | + *(SAT-D188/D190)* |
| *vergi ayarı önerme* (SAT-D146, SAT-D163; **ad bekliyor**) | + | — | — | — | — | + | — *(SAT-D146)* | — *(SAT-D146)* | — *(SAT-D146)* |
| *vergi ayarı önerisini onaylama* (SAT-D146; **ad bekliyor**) | + | — | — | — | + | **—** | **—** *(SAT-D188)* | + *(SAT-D146, SAT-D188)* | + *(SAT-D188/D190)* |
| *kur düzeltmesi (gerekçeli, audit'li)* (SAT-D169; **ad bekliyor**; alıştaki karşılığı ALS-D6) | + *(SAT-D169)* | — | — | — | + *(SAT-D169 "Müdürler")* | + *(SAT-D169)* | **—** *(SAT-D188, soru 8)* | + *(SAT-D169, SAT-D188)* | + *(SAT-D188/D190)* |
| *belge tarihinden önceki sevklerde KDV istisnası onayı* (SAT-D186; **ad bekliyor**) | + *(SAT-D186)* | — | — | — | yok *(EK28 öncesi)* | — *(SAT-D186)* | — *(SAT-D186, SAT-D188)* | + *(SAT-D186, SAT-D188)* | + *(SAT-D186)* |
| *dış fatura ↔ sipariş/irsaliye eşleştirme* (SAT-D142, SAT-D163; **ad bekliyor**) | + | — | — | — | — | + | — *(SAT-D142/D163)* | — *(SAT-D142/D163)* | — *(SAT-D142/D163)* |
| *fatura kesme = ERP fatura taslağı hazırlama* (SAT-D142, API-32; **fatura dalgası, ad bekliyor**) | ? *(karar yok)* | ? | ? | ? | ? | + *(gelecek)* | ? **NS-11** | ? **NS-11** | ? **NS-11** |
| `purchasing.orders.read` | + | + | + | + | + | ? | + *(SAT-D188 okuma)* | + *(SAT-D188 okuma)* | + *(SAT-D188/D190)* |
| `purchasing.orders.create` | + | — | + | + | ? | ? | ? **NS-5** | ? **NS-5** | ? **NS-5** |
| `purchasing.orders.approve` | + | — | **—** | **—** | + | ? | — *(SAT-D187/D188 alış)* | — *(SAT-D187/D188 alış)* | + *(SAT-D187 ara dönem, SAT-D188; ALS-D3)* |
| `inventory.labels.read` | + | + | ? | ? | ? | ? | ? **NS-6** | ? **NS-6** | ? **NS-6** |
| `inventory.balances.read` | + | + | ? | ? | ? | ? | ? **NS-6** | ? **NS-6** | ? **NS-6** |
| `inventory.opening-stock.record` | + | — | — | — | ? | ? | ? **NS-6** | ? **NS-6** | ? **NS-6** |
| `manufacturing.orders.read` | + | + | ? | ? | ? | ? | ? **NS-7** | ? **NS-7** | ? **NS-7** |
| `manufacturing.plans.write` | + | — | — | — | ? | ? | ? **NS-7** | ? **NS-7** | ? **NS-7** |
| `manufacturing.plans.approve` | + | — | — | — | ? | ? | ? **NS-7** | ? **NS-7** | ? **NS-7** |
| `manufacturing.orders.create` | + | — | — | — | ? | ? | ? **NS-7** | ? **NS-7** | ? **NS-7** |
| `manufacturing.facts.record` | + | — | — | — | ? | ? | ? **NS-7** | ? **NS-7** | ? **NS-7** |
| `logistics.loading-instructions.read` | + | + | ? | ? | ? | ? | ? **NS-8** | ? **NS-8** | ? **NS-8** |
| `logistics.loading-instructions.create` | + | + | — | — | ? | ? | ? **NS-8** | ? **NS-8** | ? **NS-8** |
| `logistics.loads.record` | + | + | — | — | ? | ? | ? **NS-8** | ? **NS-8** | ? **NS-8** |
| `accounting.periods.read` | + | ? | — | — | ? | — *(SAT-D164)* | ? **NS-9** | ? **NS-9** | ? **NS-9** |
| `accounting.journals.read` | + | **?** | — | — | ? | — *(SAT-D164)* | ? **NS-9** | ? **NS-9** | ? **NS-9** |
| `accounting.posting-queues.read` | + | ? | — | — | ? | — *(SAT-D164)* | ? **NS-9** | ? **NS-9** | ? **NS-9** |
| `accounting.periods.propose` | + | — | — | — | ? | ? | ? **NS-9** | ? **NS-9** | ? **NS-9** |
| `accounting.periods.approve` | + | — | — | — | ? | ? | ? **NS-9** | ? **NS-9** | ? **NS-9** |
| `accounting.periods.close` | + | — | — | — | ? | ? | ? **NS-9** | ? **NS-9** | ? **NS-9** |
| `finance.bank-account.read` | + | **—** | — | — | ? | — *(SAT-D164)* | ? **NS-10** | ? **NS-10** | ? **NS-10** |
| `finance.manual-receipt.draft.read` | + | **—** | — | — | ? | — *(SAT-D164)* | ? **NS-10** | ? **NS-10** | ? **NS-10** |
| `finance.manual-receipt.draft.save` | + | **—** | — | — | ? | ? | ? **NS-10** | ? **NS-10** | ? **NS-10** |
| `finance.manual-receipt.amount.read` | + | **—** | — | — | ? | — *(SAT-D164)* | ? **NS-10** | ? **NS-10** | ? **NS-10** |
| `finance.bank-account.propose` (CRITICAL) | insan kapısı | — | — | — | — | — | — *(Critical)* | — *(Critical)* | — *(Critical)* |
| `finance.bank-account.approve` (CRITICAL) | insan kapısı | — | — | — | — | — | — *(Critical)* | — *(Critical)* | — *(Critical)* |
| `identity.profile.read` | + | + | + | + | + | + *(SAT-D164)* | + *(SAT-D188 okuma)* | + *(SAT-D188 okuma)* | + *(SAT-D188/D190)* |
| `identity.access.request` | + | + | + | + | + | ? | ? **NS-2** | ? **NS-2** | ? **NS-2** |
| `identity.access.approve` (CRITICAL) | insan kapısı | — | — | — | — | — | — *(Critical)* | — *(Critical)* | — *(Critical)* |
| `identity.roles.manage` (CRITICAL) | insan kapısı | — | — | — | — | — | — *(Critical)* | — *(Critical)* | — *(Critical)* |
| `identity.audit.read` (CRITICAL) | insan kapısı | — | — | — | — | — | — *(Critical)* | — *(Critical)* | — *(Critical)* |

### Gerekçeler (yalnız Sinan'ın cümlelerinden)

- **Sevkiyatçı'nın "görür"ü write vermez:** her `create` / `record` / `approve` / `update` adı
  Sevkiyatçı'da `—`. Tek istisna Sinan'ın kendi cümlesi: "sevkiyatın kendi yükleme/sevk işlemleri
  ilgili dar write yetkileriyle verilebilir" → `logistics.loading-instructions.create` ve
  `logistics.loads.record`.
- **Satış Destek onaylayamaz:** `sales.orders.approve` ve `purchasing.orders.approve` kalın `—`.
  Bu iki ad Müdür'ün tanımının tam karşılığı. *(2 Kasım 2026: satış onayı Satış Müdürü'nün, alış onayı Satın Alma
  Müdürü'nün — ara dönemde Genel Müdür + Admin; SAT-D187/D188.)*
- **Finance ayrı tutulur:** "Sevkiyatçı her şeyi görür" banka/tahsilat/ödeme okumalarını bypass
  etmez; dört `finance.*` okuma adı da Sevkiyatçı'da `—`.
- **EK20/EK21 satırları (1 Kasım 2026), yalnız karar metinlerinden:**
  - `sales.reports.read` altı personanın hepsinde `+` (SAT-D143). Sevkiyatçı `sales.orders.price.read` almadığı için
    raporlarda yalnız miktar görür; fiyat sapması raporu ona 403 döner.
  - `sales.orders.price.read` Sevkiyatçı'da kalın `—`: SAT-D150 onu açıkça dışarıda bırakır.
  - `sales.orders.price.write` ve `sales.orders.header.write`: Satış Destek, Satış Uzmanı, Müdür, Admin (SAT-D151,
    SAT-D144); 2 Kasım 2026'dan beri Müdür'ün yerine Satış Müdürü ve Genel Müdür (SAT-D188). Muhasebe "yazmaz"; Sevkiyatçı'nın "görür"ü write vermez. Başlık değişikliği yeniden onay gerektirirse
    onayı mevcut sipariş onay iznini tutan verir.
  - Risk istisnası **talebi**: Sevkiyatçı, Satış Destek, Satış Uzmanı, Müdür ve Admin (SAT-D159; Müdür → Satış Müdürü ve
    Genel Müdür, SAT-D188). Muhasebe bu soruda
    hiç seçenek olarak gösterilmedi → `?` (SAT-D142 "sipariş yazmaz" bir risk istisnası talebini açıkça kapsamıyor).
    Sevkiyatçı talebi **sevk/yükleme ekranından** açar; o ekran yalnız "risk limiti aşıldı, sevk duruyor"
    durumunu gösterir, limit, bakiye ve tutar göstermez; talep `sales.drafts.read` gerektirmez (SAT-D162).
    Yükleme ekranının bu talep için kullanacağı Lojistik okuma adı **henüz belirlenmedi**; sahibinin (Logistics owner)
    teknik işidir ve ad gelene kadar kartta satırı yoktur.
    **Onayı**: Satış Uzmanı, Müdür veya Admin (SAT-D153; Müdür → Satış Müdürü ve Genel Müdür, SAT-D188); Satış Destek ve Sevkiyatçı onaylamaz. İsteyen kendi
    talebini onaylayamaz — bu persona değil kayıt kuralıdır.
  - `settings.tax-master.manage` ve `product.item-type.manage`: yalnız Müdür ve Admin, doğrudan geçerli (SAT-D145); 2 Kasım 2026'dan
    beri Müdür'ün yerine **Muhasebe Müdürü** ve Genel Müdür (SAT-D188 muhasebe sınıfı).
    Muhasebe vergi ayarını yalnız öneri akışıyla değiştirir (SAT-D146) ve kalem tipini değiştirmez. Kalem tipini
    bu iki persona dışında kimin **okuyacağı** sorulmadı (EK20 açık kalanı, NEEDS-SINAN); o zamana kadar yalnız
    `product.item-type.manage` sahipleri okur.
  - `counterparty.accounts.e-document.update`: Muhasebe ve Admin (SAT-D155). "Muhasebe + Müdür + Admin" ve
    "Satış Destek/Uzman + Müdür + Admin" seçenekleri gösterildi ve **seçilmedi**; bu yüzden Müdür, Satış Destek
    ve Satış Uzmanı `—`.
  - SAT-D146 ad bekleyen üç izin: okuma Muhasebe, Müdür, Admin; öneri Muhasebe ve Admin (SAT-D146, SAT-D163); onay
    **yalnız** Müdür ve Admin (2 Kasım 2026'dan beri okuma iki müdüre de; onay **Muhasebe Müdürü**, Genel Müdür ve Admin —
    Satış Müdürü onaylamaz, SAT-D188). Admin'in öneri izni onu onay kuralından muaf tutmaz: **kendi önerisini onaylayamaz**
    (SAT-D163, maker ≠ checker). ~~"Muhasebe müdürü" ve "satış müdürü" unvanları ayrı persona kararı gelene kadar ek
    yetki almaz.~~ **Kapandı (2 Kasım 2026, SAT-D187/D188):** ikisi ayrı personadır; EK20'nin "müdür sınıfı = Müdür +
    Admin" ara okuması kalktı.
  - Dış fatura eşleştirme: Muhasebe ve Admin (SAT-D142, SAT-D163). Diğer personalar `—` (EK20 "yalnız Muhasebe",
    SAT-D163 yalnız Admin'i ekler). Fatura kesmede yalnız Muhasebe söylendi; diğer personalar `?`.
    **Admin dahil:** SAT-D163 Admin'e yalnız eşleştirmeyi ve vergi önerisini ekledi; fatura kesmede Admin için karar
    yoktur (hücre `? (karar yok)`).
- **EK24–EK28 satırları ve müdür ayrımı (2 Kasım 2026), yalnız karar metinlerinden:**
  - **Satış onayları Satış Müdürü'ne** (SAT-D188): `sales.orders.approve` (SAT-D6; SAT-D3'teki miktar revizyonunun
    yeniden onayı ve SAT-D14'teki "onaylayan persona aynıdır" da bu izindir; SAT-D174 eşik kapalı), taban fiyat
    (SAT-D1) ve iskonto istisnası (SAT-D2/D37) onayı, iptal (SAT-D4), süresi geçmiş teklif onayı (SAT-D24/D44).
    SAT-D44'ün "hazırlayan ≠ onaylayan" istisnası yalnız süresi geçmiş teklif onayı içindir; öteki onaylarda P6 aynen.
  - **İskonto limit grubu (SAT-D61…D63, SAT-D66, SAT-D67, SAT-D117):** "Müdür" limit grubu **Satış Müdürü**
    grubudur (SAT-D188, soru 7). Başlangıç limiti %100 (SAT-D62), ileride "bağlı grup + ek puan" (örnek: Satış Müdürü =
    Satıcı + 3; SAT-D117 pasif grupta da çalışır). Kişi → grup bağı şirket bazında tutulabilir, varsayılan üç şirkette
    ortak (SAT-D66). Satış Müdürü'nün kendi limitini aşan iskontosu **başka** bir onay yetkilisini bekler — başka bir
    Satış Müdürü, Genel Müdür veya Admin (SAT-D67, SAT-D188). Limit grubu bir **Ayarlar verisidir**, persona değildir:
    gruba bağlanmak yetki açmaz, yalnız onaysız iskonto sınırını belirler. Limitleri ve grupları düzenleme
    (`settings.sales-discount-limits.manage`) SAT-D65'teki gibi Yönetim Kurulu ve Admin'dedir (SAT-D69); hiçbir müdür
    personası almaz (SAT-D189 "SAT-D65'teki gibi kalır"). "Yönetim Kurulu" personasının karta eklenmesi insan kapısıdır.
  - **SAT-D168** fazla teslim onayı, eksik teslimle satır kapatma ve sipariş kapatma: Satış Uzmanı, Satış Müdürü,
    Genel Müdür, Admin. İzin adları uygulama PR'ında eklenir; kart mevcut bir adı (ör. `sales.orders.force-close`)
    bu karara **eşlemez**.
  - **SAT-D169** kur düzeltmesi: **Muhasebe, Muhasebe Müdürü, Genel Müdür, Admin** (SAT-D188, soru 8); Satış
    Müdürü kalın `—`. Düzeltme yalnız sonraki belgelere yansır (SAT-D172). Alıştaki karşılığı ALS-D6 ("Satış'taki
    gibi") yalnız çapraz referanstır; ALS-D6'nın yeni rollere göre güncellenmesi Alış şeridinin işidir (EK28 açık kalanı).
  - **SAT-D179** kaynak planı (`sales.orders.source-plan.write`, API-11, manifest'te henüz yok): Satış Destek, Satış
    Uzmanı, Satış Müdürü, Genel Müdür, Admin.
  - **SAT-D185** alıcı belgesinin sonradan girilmesi **yeni izin açmaz**: `sales.orders.header.write` ile yapılır
    (satırda işaretli). Revizyon açılmaz; audit'li.
  - **SAT-D186** belge tarihinden önceki sevklerde KDV istisnası onayı: **Muhasebe Müdürü, Genel Müdür, Admin**.
    Onayı isteyen onaylayamaz; onaylanana kadar sipariş alınır ve sevk çıkar ama o sevklerin faturası kesilmez
    (fail-closed). Genel hukuki soru mali müşavirde açıktır (SAT-D148).
  - **SAT-D189** satış ayarları (teklif kayıp nedenleri, varsayılan geçerlilik günü, teslim toleransı yüzdeleri):
    **Satış Müdürü, Genel Müdür, Admin**; doğrudan geçerli ve audit'li. "Yalnız Admin", "Satış Uzmanı da" (soru 3) ve
    "İki müdür de" (soru 5) gösterildi ve seçilmedi → Satış Uzmanı ve Muhasebe Müdürü kalın `—`.
  - **SAT-D110** `sales.invoices.read`: Muhasebe, Satış Destek, eski Müdür; Müdür'ün okuması iki müdüre de geçer
    (SAT-D188 okuma). Satış Uzmanı SAT-D141 kopya yorumuyla `+`. Sevkiyatçı karardan geçmez → `?`.
  - **SAT-D99** "faturalanmış, sevk edilmemiş" bakiye: kararın "Müdür"ü SAT-D188 sınıflarından hiçbirine açıkça
    girmez (satış mı, muhasebe mi, ikisi mi?) → **NS-1**. Genel Müdür ve Admin her durumda yetkilidir (SAT-D187/D188).
  - **SAT-D148/D158/D181** (onaylı kod ve tutar, alıcı durumu listesi, KDV/tevkifat oran listeleri) değer girişi
    `settings.tax-master.manage` içindedir → Muhasebe Müdürü, Genel Müdür, Admin (SAT-D188 muhasebe sınıfı).
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
*(2 Kasım 2026: tek Müdür SAT-D187 ile Satış Müdürü, Muhasebe Müdürü, Genel Müdür ve modül müdürlerine ayrıldı; bu
sınır her biri için aynen geçerlidir ve hiçbirine genel Finance erişimi açmaz.)*

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
4. **Dar fiyat okuması** — Müdür'ün (2 Kasım 2026'dan beri Satış Müdürü ve Genel Müdür, SAT-D188) sipariş onayı
   bağlamı için (K4.2). Sahibi: Sales owner.
5. **Cari/hesap oluşturma için ayrı dar izin** — K4.3. Sahibi: Cari owner.
6. Ardından persona başına rol kodlarının Identity modelinde tasarlanması ve **en sonda** grant'lerin
   insan kapısında açılması.
7. **EK20…EK28 grant PR'ı** (SAT-D141…D146, SAT-D150/D151, SAT-D153/D155/D159, SAT-D163, SAT-D164; müdür ayrımı
   SAT-D186…D190 ve EK24–EK27 satırları): persona/rol tanımı ve izin eşlemesi yalnız bu kartta **karar numarasıyla
   işaretli her hücre**yi açar — eski satırlardaki SAT-D164 ve SAT-D188 hücreleri dahil; "Müdür (eski)" sütunu, `?`
   ve **NS-n** hücreleri hiçbir şey açmaz. Ad bekleyen satırlar (risk istisnası, SAT-D146 vergi önerisi, eşleştirme,
   SAT-D168/D169/D186/D189/D99, `sales.orders.source-plan.write`) adları manifest'e gelince eşlenir. Aynı PR
   `ErpPermissions` açıklamalarını kararlarla hizalar.

**İnsan kapısı değişmez:** EK20/EK21 ve EK28 persona kararları Sinan'ın somut kararıdır, ama bu kart da, o kararlarla açılacak
eşleme PR'ı da **gerçek bir kullanıcıya rol atamaz** ve **production'da hiçbir şeyi etkinleştirmez**. Gerçek rol
ataması, Critical grant'ler ve production aktivasyonu her zaman insan kapısında kalır (SAT-D110; EK20, EK21 ve EK28
"Sınırlar"). Bir kişiye birden fazla müdürlük verilmesi (SAT-D187) de gerçek bir atamadır ve aynı insan kapısındadır.

İzin adları her durumda sahibinin teknik işidir; bu kart ad önermez ve atama yapmaz.

## NEEDS-SINAN — müdür ayrımı (2 Kasım 2026, SAT-D188 son cümlesi)

SAT-D188 "bir hücre bu sınıflara açıkça girmiyorsa persona kartı PR'ında NEEDS-SINAN yazılır" diyor. Aşağıdaki hücreler
satış, muhasebe, okuma veya alış onayı sınıfına **açıkça** girmiyor; tabloda üç müdür sütununda `? NS-n` olarak durur ve
**hiçbir şey açmaz** (deny-first). Hepsinde, hangi müdür alırsa alsın, Genel Müdür ve Admin de yetkilidir (SAT-D188); öteki
modül müdürleri tanımlanana kadar o modülün müdür hücresini Genel Müdür + Admin tutar (SAT-D187). Soru her satırda aynıdır:
**bu hücre hangi müdürün (Satış, Muhasebe, ilgili modül müdürü) yetkisidir — yoksa hiçbirinin mi?**

| NS | Hücre(ler) | Eski Müdür | Neden sınıfa girmiyor |
|---|---|---|---|
| NS-1 | *"faturalanmış, sevk edilmemiş" bakiyeyi görme ve yönetme* (SAT-D99) | `+` | SAT-D188'in kendi örneği. Bakiye hem satışın (sevk) hem muhasebenin (fatura, iade faturası) konusu; "görür ve yönetir" okuma + yazma birlikte |
| NS-2 | `identity.access.request` | `+` | Okuma değil, onay/değişiklik değil; Identity modülünün temel talebi. Muhasebe sütunu da `?` |
| NS-3 | `organization.work-centers.read`, `organization.locations.status.update` | `?` | Organizasyon modülü; müdür rolü tanımlanmadı. Eski hücre zaten belirsizdi |
| NS-4 | `counterparty.parties.create`, `counterparty.parties.update.basic`, `counterparty.accounts.create`, `counterparty.accounts.status.update` | `?` | Cari oluşturma/değiştirme: satışın mı, muhasebenin mi, ayrı bir Cari sorumlusunun mu? K4.3 yalnız Satış Destek için "ayrı dar izin" der |
| NS-5 | `purchasing.orders.create` | `?` | Alış modülü; Satın Alma Müdürü Alış şeridinde tanımlanacak (SAT-D187, ALS-D3). Alış **onayı** sınıflıdır (Satın Alma Müdürü; ara dönemde Genel Müdür + Admin) |
| NS-6 | `inventory.labels.read`, `inventory.balances.read`, `inventory.opening-stock.record` | `?` | Stok modülü; müdür rolü tanımlanmadı |
| NS-7 | `manufacturing.orders.read`, `manufacturing.plans.write`, `manufacturing.plans.approve`, `manufacturing.orders.create`, `manufacturing.facts.record` | `?` | Üretim modülü; Üretim Müdürü Üretim şeridinde tanımlanacak (SAT-D187) |
| NS-8 | `logistics.loading-instructions.read`, `logistics.loading-instructions.create`, `logistics.loads.record` | `?` | Lojistik modülü; müdür rolü tanımlanmadı |
| NS-9 | `accounting.periods.read`, `accounting.journals.read`, `accounting.posting-queues.read`, `accounting.periods.propose`, `accounting.periods.approve`, `accounting.periods.close` | `?` | Muhasebe **dönemi/fişi**: SAT-D188'in muhasebe sınıfı yalnız vergi/kur/istisna kararlarını sayar; Accounting modülünün dönem ve fiş işleri Muhasebe Müdürü'ne mi geçer, sorulmadı |
| NS-10 | `finance.bank-account.read`, `finance.manual-receipt.draft.read`, `finance.manual-receipt.draft.save`, `finance.manual-receipt.amount.read` | `?` | Finance modülü; K4.2 "genel Finance hayır". Finance'ın müdürü (Muhasebe Müdürü mü, ayrı bir Finans Müdürü mü) sorulmadı |
| NS-11 | *fatura kesme = ERP fatura taslağı hazırlama* (SAT-D142, API-32) | `?` | Fatura dalgası; yalnız Muhasebe söylendi, hiçbir müdür ve Admin için karar yok |
| NS-12 | `sales.drafts.create` (Satış Müdürü ve Genel Müdür) | `?` | Satış sınıfıdır (Muhasebe Müdürü `—`), ama eski Müdür hücresi zaten belirsizdi: müdürün kendisi sipariş taslağı girebilir mi? |

Bu liste persona kararı ister; `OPEN-*` kapatmaz ve hiçbir izni açmaz. Cevaplar gelince ilgili hücreler karar numarasıyla
doldurulur.
