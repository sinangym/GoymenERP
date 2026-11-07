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

**Güncelleme (2 Kasım 2026, EK29):** [EK29](../decisions/SINAN_KARARLARI_2026-11-02_SATIS_EK29.md) (SAT-D191…D195) on iki
NS'den on birini kapattı: eski Müdür'ün satış/muhasebe dışı yetkileri **o modülün müdürüne** gider, rol karta girene kadar
Genel Müdür + Admin'dedir (SAT-D191); cari yönetimi yalnız Muhasebe Müdürü'ndedir (SAT-D192); SAT-D99 bakiyesi iki
müdürdedir (SAT-D193); Satış Müdürü ve Genel Müdür taslak açar (SAT-D194). SAT-D188'in "okumalar iki müdüre de" kuralı
satış ve muhasebe okumalarıyla sınırlandı; öteki modüllerin okumaları o modülün müdürüne gider (EK29 "Bilinçli
değişiklikler"). Açık kalan tek hücre grubu **NS-11** (fatura kesme). *(EK32 SAT-D202 ile kapandı; EK31/EK32
güncellemesi iki yeni grup açtı: NS-13 fatura onayı, NS-14 kur içe aktarma; ikisi de EK33 SAT-D204/D205 ile kapandı.)*

**Güncelleme (2 Kasım 2026, EK30):** [EK30](../decisions/SINAN_KARARLARI_2026-11-02_SATIS_EK30.md) (SAT-D196/D197)
**Finans Uzmanı** (oluşturur) ve **Finans Müdürü** (onaylar) personalarını ekledi; finans artık SAT-D191'in ara dönemine
bağlı değildir. Genel Müdür finansta yalnız **onay ve onayın gerektirdiği okumayı** alır, oluşturamaz (SAT-D197); K4.2
Satış ve Muhasebe Müdürü için aynen kalır. EK29 turundaki dört `finance.*` Genel Müdür `+` hücresi bu yüzden geri
alındı. Ayrıntı: ana tablonun altındaki "Finans personaları" tablosu.

**Güncelleme (2 Kasım 2026, EK31/EK32):** [EK32](../decisions/SINAN_KARARLARI_2026-11-02_SATIS_EK32.md) SAT-D202
**NS-11**'i kapattı: faturayı **Muhasebe** hazırlar ve keser; **Muhasebe Müdürü, Genel Müdür ve Admin** de kesebilir;
**Satış** (Satış Uzmanı, Satış Müdürü) faturayı görür ama kesemez; Satış Destek, Sevkiyatçı, Finans Uzmanı ve Finans
Müdürü kesmez. Fatura taslağı **onayı** (API-33) bu kararın kapsamı dışıdır (SAT-D88/D106 aynen); Muhasebe Müdürü,
Genel Müdür ve Admin'in onay hücresi EK28'den açıkça türetilemediği için **NS-13** olarak açık kalır.
[EK31](../decisions/SINAN_KARARLARI_2026-11-02_SATIS_EK31.md) SAT-D199 kur **okumasını** (Satış Uzmanı, Satış Müdürü,
Muhasebe, Muhasebe Müdürü, Genel Müdür, Admin, Finans Uzmanı, Finans Müdürü) ve SAT-D198 belirlenemeyen arife/yarım
günün **işaretlenmesini** (Muhasebe, Muhasebe Müdürü, Genel Müdür, Admin) söyledi. Kur kaydının üç adı
(`settings.exchange-rates.read`, `.correct`, `.import`) #906'dan (`claude/kur-kaydi`; bu güncellemede main'de henüz
kayıtlı değil) alındı; içe aktarmanın (`.import`) sahibi karara bağlı değildir → **NS-14**. Ayrıntı: aşağıda "EK31/EK32
eşlemesi" ve kartın sonundaki NEEDS-SINAN listesi.

**Güncelleme (2 Kasım 2026, EK33):** [EK33](../decisions/SINAN_KARARLARI_2026-11-02_SATIS_EK33.md) iki açık grubu
kapattı. **NS-13 (SAT-D204):** fatura taslağı onayını (API-33) ve SAT-D106 tolerans farkı onayını (API-35) **Muhasebe,
Muhasebe Müdürü, Genel Müdür ve Admin** verir; taslağı hazırlayan kişi onu onaylayamaz (SAT-D88/D106, kayıt kuralı).
**NS-14 (SAT-D205):** `settings.exchange-rates.import` (elle "şimdi çek") **Muhasebe, Muhasebe Müdürü, Genel Müdür ve
Admin**'dedir — kuru düzeltebilen kişilerle aynı. Kartta açık `NEEDS-SINAN` hücresi kalmadı.

**Güncelleme (7 Kasım 2026, Alış EK8/EK10/EK11 — satın alma personaları):** Alış şeridinin
[EK8](../decisions/SINAN_KARARLARI_2026-11-02_ALIS_EK8.md) kararları (ALS-D53, ALS-D54, ALS-D56) **Satın Alma Uzmanı**
(talep ve sipariş hazırlar, onaylamaz) ve **Satın Alma Müdürü** (talebi ve siparişi onaylar) personalarını tanımladı ve
talep açanları söyledi; [EK10](../decisions/SINAN_KARARLARI_2026-11-03_ALIS_EK10.md) ALS-D66 kalan iptalini
(`purchasing.orders.cancel`) onaycılara verdi; [EK11](../decisions/SINAN_KARARLARI_2026-11-07_ALIS_EK11.md) ALS-D70
alış fiyatı okumasının (`purchasing.orders.price.read`) personalarını söyledi: Satın Alma Uzmanı, Satın Alma Müdürü,
Genel Müdür, Admin, Muhasebe, Muhasebe Müdürü, **Satış Destek** ve **Satış Uzmanı**; Sevkiyatçı görmez. İki persona
yalnız `purchasing.*` adlarında anlam taşıdığı için ana tabloya sütun olarak değil, "Finans personaları" deseniyle ayrı
bir ek tabloyla eklendi (aşağıda "Satın alma personaları"). `purchasing.*` satırlarında bu ek tablo ana tabloya
üstündür; Satın Alma Müdürü tanımlandığı için o satırlardaki "SAT-D187 ara dönem: Satın Alma Müdürü" hücreleri artık
Satın Alma Müdürü + Genel Müdür + Admin'dir (SAT-D188: Genel Müdür her müdür hücresinde yetkilidir). Öneridir; grant
değildir. Kart değişikliği insan kapısı: Sinan "Onaylıyorum, aynı PR", PR #957 yorumu 6706749065.

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
personası EK28'den (2 Kasım 2026, SAT-D187…D190); Finans Uzmanı ve Finans Müdürü EK30'dan (2 Kasım 2026,
SAT-D196/D197). §4'teki tek **Müdür** satırı tarihî iz olarak üstü çizili durur.

**Bir kişiye birden fazla müdürlük verilebilir (SAT-D187).** Persona bir yetki paketidir, kişi değildir: aynı kişi hem
Satış Müdürü hem Muhasebe Müdürü olabilir ve iki paketin birleşimini alır. Bu, kayıt düzeyindeki kuralları gevşetmez —
hazırlayan/isteyen kendi kaydını onaylayamaz (P6, SAT-D37, SAT-D153, SAT-D163, SAT-D186); bu kural personaya değil kayda
bağlıdır.

| Persona | Sinan'ın tanımı (özet, §4) |
|---|---|
| **En Yetkili Admin** | Tüm ERP işlevleri; kritik insan kapıları ve production işlemleri yine mevcut güvenlik kurallarına tabi. Müdürlerin yaptığı her şeyi yapabilir (SAT-D187, EK28 soru 2 ve 4); Genel Müdür'ün kapsamı dışındaki sistem yönetimi yetkileri yalnız Admin'dedir (SAT-D190). |
| **Sevkiyatçı** | Alış/satış siparişleri, muhasebe kayıtları, stok, üretim ve operasyonel kayıtları **görebilir**; **fiyatları göremez**, **Finance para hareketleri / banka / tahsilat / ödeme bakiyelerini göremez**. "Görür", create/approve/write vermez; sevkiyatın kendi yükleme/sevk işlemleri dar write izinleriyle verilebilir. |
| **Satış Destek** | Alış ve satış siparişi **girebilir/düzenleyebilir**; sipariş **onaylayamaz**. |
| ~~**Müdür**~~ *(19 Eylül §4; 2 Kasım 2026'da SAT-D187 ile ayrıldı — tabloda "Müdür (eski)" sütunu)* | ~~Siparişleri **onaylayabilir**. Tek başına tüm modüllerde sınırsız read/write veya Finance yetkisi vermez.~~ Bu tanımın yerine aşağıdaki müdür personaları ve modül müdürü yer tutucuları geçer. K4.2'nin sınırı ("onayın gerektirdiği okuma evet, genel Finance hayır") Satış Müdürü ve Muhasebe Müdürü için aynen geçerlidir; Genel Müdür için yalnız finans **onayı** ve onayın gerektirdiği okuma kadar kalkar (SAT-D197). |
| **Satış Uzmanı** (SAT-D141) | Ayrı persona; başlangıçta **Satış Destek'in bütün grant'larını** alır: sipariş girer ve düzenler, **onaylayamaz**, sipariş başlığını değiştirir. İleride ayrıştırılabilir (ör. yalnız kendi müşterileri — EK20 açık kalanı). *Uygulama yorumu (geri alınabilir, SAT-D141):* "Satış Destek gibi" kartta Satış Destek'e verilen her izni kapsar, alış siparişi izinleri dahil. EK21'de ayrıca risk istisnasını **talep eder ve onaylar** (kendi talebini onaylayamaz; SAT-D153/D159). |
| **Muhasebe** (SAT-D142) | Satış modülünde **her şeyi görür**: siparişler, fiyatlar ve tutarlar, raporlar, vergi ayarları. **Sipariş yazmaz, onaylamaz.** Şimdi dış sistemde kesilen faturayı ERP sipariş/irsaliyesiyle **eşleştirir**; fatura modülü gelince (API-32…37) faturayı **keser** (ERP fatura taslağı; hazırlayan ≠ onaylayan, SAT-D88). Vergi ayarlarını yalnız **öneri → onay** akışıyla değiştirir (SAT-D146; onaylayan 2 Kasım 2026'dan beri Muhasebe Müdürü, Genel Müdür veya Admin — SAT-D188); kalem tipini değiştirmez. Cari e-belge durumunu günceller (SAT-D155). |
| **Satış Müdürü** (SAT-D187, SAT-D188) | Eski Müdür'ün **satış** yetkilerini alır: satış siparişi onayı (SAT-D6), taban fiyat ve iskonto istisnası onayı (SAT-D1/D2/D37), miktar revizyonunun yeniden onayı (SAT-D3), iptal (SAT-D4), süresi geçmiş teklif onayı (SAT-D24/D44), fiyat ve başlık yazma (SAT-D151, SAT-D144), risk istisnası talebi ve onayı (SAT-D159, SAT-D153), fazla teslim onayı ve kapatma (SAT-D168), kaynak planı (SAT-D179), satış ayarları (SAT-D189). "Müdür" iskonto limit grubu **Satış Müdürü** grubudur (SAT-D188, soru 7). Satış siparişi taslağı açar (SAT-D194; açtığını onaylayamaz). "Faturalanmış, sevk edilmemiş" bakiyeyi Muhasebe Müdürü ile birlikte görür ve yönetir (SAT-D193). **Satış ve muhasebe** okumaları Muhasebe Müdürü ile ortaktır (SAT-D188); öteki modüllerin okumalarını **almaz** (SAT-D191). Vergi/muhasebe yetkisi ve cari yönetimi **almaz** (SAT-D188, SAT-D192). |
| **Muhasebe Müdürü** (SAT-D187, SAT-D188) | Eski Müdür'ün **vergi/muhasebe** yetkilerini alır: vergi ayarlarını ve kalem tipini doğrudan değiştirme (SAT-D145/D149; SAT-D148/D158/D181 değer ve liste girişi bunun içindedir), Muhasebe'nin vergi ayarı önerisini onaylama (SAT-D146), kur düzeltmesi (SAT-D169), belge tarihinden önceki sevklerde istisna onayı (SAT-D186). Accounting modülünün müdürüdür: muhasebe dönemi ve yevmiye/fiş yetkileri (SAT-D191). Cari kartı açma/değiştirme ve cari hesabını aktif/pasif yapma **yalnız** Muhasebe Müdürü'ndedir (SAT-D192). SAT-D99 bakiyesini Satış Müdürü ile birlikte görür ve yönetir (SAT-D193). **Satış ve muhasebe** okumaları Satış Müdürü ile ortaktır (SAT-D188); öteki modüllerin okumalarını almaz (SAT-D191). Satış yetkisi **almaz**. Muhasebe personasının (SAT-D142) yetkilerini kendiliğinden almaz; aynı kişiye iki persona birlikte verilebilir. |
| **Genel Müdür** (SAT-D187, SAT-D190) | **Bütün modüllerde** müdürlerin yaptığı onay ve değişiklikleri yapabilir; her müdür hücresinde yetkilidir (SAT-D188, soru 9). Admin'in **sistem yönetimi** yetkileri (kullanıcı, rol, sistem ayarı) bu kapsamda **değildir** (SAT-D190). Öteki modül müdürleri tanımlanana kadar o modüllerin müdür hücrelerini Admin ile birlikte tutar (SAT-D187, SAT-D191). Bu ara dönem hücrelerinin bir kısmı onay değil **operasyonel kayıttır** (`inventory.opening-stock.record`, `manufacturing.facts.record`, `logistics.loads.record`, `logistics.loading-instructions.create`, `organization.locations.status.update`); SAT-D190'ın "onay ve değişiklik" kapsamını aşar ve dayanağı EK29 soru 1'dir (SAT-D191: "rol tanımlanana kadar Genel Müdür ve Admin yapar"). Grant PR'ı bunları bilerek açar. Taslak açabilir (SAT-D194). **Finansta** yalnız onay ve onayın gerektirdiği okumayı alır; finans işlemi oluşturmaz ve banka hesabı okuması gibi genel finans okuması almaz (SAT-D197). |
| **Finans Uzmanı** (SAT-D196) | Finans işlemini **oluşturur**: taslak kaydetme ve oluşturma için gereken okumalar (ör. banka hesabı seçimi, taslak ve tutarı). **Onaylamaz.** Finans dışında yalnız yedi temel izni alır (SAT-D191 temel; yorum, geri alınabilir — bkz. "Finans personaları"); öteki finans dışı her hücrede `—`, **tek istisna** kur okuması `settings.exchange-rates.read` (SAT-D199). Fatura kesmez (SAT-D202). Critical finans adları insan kapısındadır. |
| **Finans Müdürü** (SAT-D196) | Finans işlemini **onaylar**: onay ve onayın gerektirdiği okumalar. **Oluşturmaz**; hazırlayan ≠ onaylayan. Müdür personası olarak yedi temel izni alır (SAT-D191 temel); öteki finans dışı her hücrede `—`, **tek istisna** kur okuması `settings.exchange-rates.read` (SAT-D199). Fatura kesmez (SAT-D202). Bugün kartta Critical olmayan bir finans onay adı olmadığı için finans satırlarında henüz `+` hücresi yoktur. Critical finans adları insan kapısındadır. |
| **Satın Alma Uzmanı** (ALS-D53) | Satın alma talebini ve siparişini **hazırlar**, kendi talebini geri çeker; talebi ve siparişi **onaylamaz**, kalanı iptal etmez (ALS-D66). Talep açar (ALS-D54). Alış fiyatını görür (ALS-D70). Alış dışında yalnız SAT-D191 temel okumaları (EK8 §3, yorum). |
| **Satın Alma Müdürü** (ALS-D53) | Alış modülünün müdürüdür: talebi ve siparişi **onaylar/reddeder** (ALS-D2, ALS-D3), onaylı siparişin kalanını iptal eder (ALS-D66), talep açar (ALS-D54), alış fiyatını görür (ALS-D70). Hazırlayan kendi kaydını onaylayamaz (kayıt kuralı). Alış faturası kurunu **düzeltmez** (ALS-D56). Kendi modülü dışındaki okumayı almaz (SAT-D191). |
| *Üretim, Stok, Lojistik Müdürü … (yer tutucu)* (SAT-D187, SAT-D191) | Her modülün kendi müdürü, yetkisi kendi modülünde kalır. Bu personalar **o modüllerin karar dosyalarında** tanımlanır (Üretim şeridi; URT-D2 plan onayı Üretim müdüründedir). Satın Alma Müdürü artık tanımlıdır: yukarıdaki Satın Alma satırları ve "Satın alma personaları" ek tablosu. Karta girene kadar sütunları yoktur. O modüllerde **eski Müdür'ün `+` hücreleri** ile SAT-D191'in o modülün müdürüne verdiği hücreler — tabloda "SAT-D187 ara dönem: … Müdürü" diye işaretli olanlar — **Genel Müdür + Admin**'dedir. Müdürü olmayan modülün (ör. Organizasyon) hücreleri de Genel Müdür + Admin'de kalır (SAT-D191). Kendi modülü dışındaki okumayı almaz (SAT-D191). |

## Önerilen eşleme

`+` = önerilir · `—` = verilmez (gerekçe aşağıda) · `?` = bu tabloda belirsizdi. · **NS-n** = `NEEDS-SINAN`, müdür ayrımında sınıfı açık olmayan hücre (kartın sonundaki listede; EK29'dan sonra yalnız NS-11; EK32'den sonra NS-13 ve NS-14; EK33'ten sonra hiçbiri).
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
- Eski Müdür'ün `+` **satış ve muhasebe okumaları** → **iki müdüre de** `+` *(SAT-D188 okuma)*; EK29 bu kuralı
  satış/muhasebe okumalarıyla sınırladı.
- **Temel okumalar ve talep** (bütün personalara verilenler: `organization.context/sites/locations.read`,
  `identity.profile.read`, `identity.access.request`, `counterparty.parties.read`, `product.operational-selection.read`)
  → **bütün müdürlere** `+` *(SAT-D191 temel)*.
- **Öteki modüllerin** okuma ve yazmaları (alış, stok, üretim, lojistik, organizasyon yazması) → **o modülün
  müdürü**; rol karta girene kadar Genel Müdür + Admin (operasyonel kayıt hücreleri dahil; dayanak EK29 soru 1); Satış ve Muhasebe Müdürü `—` *(SAT-D191)*. Muhasebe dönemi
  ve yevmiye/fiş adları Accounting modülüdür → **Muhasebe Müdürü** *(SAT-D191)*. Üretim planı onayı URT-D2 gereği
  Üretim müdüründedir → ara dönem Genel Müdür + Admin. Muhasebe Müdürü dönem **önerme** ve **onaylama**yı birlikte
  alır; P6 (hazırlayan ≠ onaylayan) kayıt düzeyinde aynen geçerlidir: kendi önerisini onaylayamaz.
- **Finance** artık ara dönemde değildir: Finans Uzmanı oluşturur, Finans Müdürü onaylar (SAT-D196); Genel Müdür
  yalnız onay ve onayın gerektirdiği okuma (SAT-D197); Satış ve Muhasebe Müdürü `—` (K4.2). Bkz. "Finans personaları".
- **Cari yönetimi** (`counterparty.parties.create`, `.update.basic`, `counterparty.accounts.create`,
  `.status.update`) → **yalnız Muhasebe Müdürü**; Satış Müdürü kalın `—` *(SAT-D192)*.
- **Alış** onayı (`purchasing.orders.approve`, ALS-D3) → Satın Alma Müdürü; o persona Alış şeridinde tanımlanana kadar
  hücre **Genel Müdür + Admin**'dedir (SAT-D187); Satış ve Muhasebe Müdürü `—`.
- **Genel Müdür** her müdür hücresinde yetkilidir *(SAT-D188/D190)*: bir hücre herhangi bir müdürde `+` ise Genel
  Müdür'de de `+`. Eski Müdür'ün **almadığı** bir şeyi (ör. SAT-D155 e-belge, SAT-D65 limit düzenleme) Genel Müdür de
  almaz; Admin'in Critical/sistem yönetimi adları kapsam dışıdır (SAT-D190).
- İlk turda `? NS-n` işaretlenen hücreler EK29 ile kapandı (yukarıdaki maddeler; kapanış tablosu kartın sonunda).
  Açık kalan tek grup **NS-11** (fatura kesme): üç müdür sütununda `?`, hiçbir şey açmaz (fail-closed).
  *(2 Kasım 2026: NS-11 EK32 SAT-D202 ile kapandı; bkz. "EK31/EK32 eşlemesi".)*
- **Yeni satırlar** (bu tarihte eklendi): eski Satış kararlarının tabloda satırı olmayan Müdür hücreleri
  (SAT-D1, SAT-D2/D37/D67, SAT-D4, SAT-D24/D44, SAT-D65/D69, SAT-D99, SAT-D110) ile EK24–EK28'in persona söyleyen
  kararları (SAT-D168, SAT-D169, SAT-D179, SAT-D186, SAT-D189). Bu satırlarda müdür dışı sütunlar yalnız kararın
  kendi metnindendir: kararın saydığı kişi `+`, kararın saydığı kümenin dışında kalan `—` (deny-first), kararın hiç
  değinmediği persona `?`. Bu satırlar da hiçbir şey açmaz; izin adları sahibinin teknik işidir ve atama insan
  kapısıdır.

**EK31/EK32 eşlemesi (2 Kasım 2026, SAT-D198, SAT-D199, SAT-D202).** Aynı satır kuralı geçerlidir (kararın saydığı
kişi `+`, kararın saydığı kümenin dışında kalan `—`, kararın hiç değinmediği persona `?`). "Müdür (eski)" sütunu EK28
sonrası kararlarda `yok` yazar. Değişen ya da eklenen satırlar:
- **Fatura kesme** (SAT-D202, NS-11 kapandı): Muhasebe, Muhasebe Müdürü, Genel Müdür, Admin `+`; Satış Uzmanı ve Satış
  Müdürü kalın `—` ("görür ama kesemez"); Satış Destek ve Sevkiyatçı `—` (SAT-D202 kapalı liste). Finans Uzmanı ve
  Finans Müdürü `—` (SAT-D202; "Finans personaları" tablosu).
- **Fatura taslağı onayı** (API-33, SAT-D88/D106; yeni satır): Muhasebe `+` (SAT-D88; hazırlayan onaylayamaz, SAT-D106);
  Satış personaları, Satış Destek ve Sevkiyatçı `—` (SAT-D88 yalnız Muhasebe'yi sayar); Muhasebe Müdürü, Genel Müdür ve
  Admin ilk turda **`?` NS-13** idi → **EK33 SAT-D204 ile `+`** (API-35 tolerans farkı onayı da aynı kişilerde).
- **Fatura okuma** (SAT-D202 "Satış görür"; yeni, ad bekliyor): Satış Uzmanı, Satış Müdürü, Muhasebe, Muhasebe Müdürü,
  Genel Müdür, Admin `+`; Satış Destek, Sevkiyatçı ve Finans personaları `?` (karar değinmiyor).
- **Kur okuma** `settings.exchange-rates.read` (SAT-D199; yeni satır): saydığı sekiz persona `+`; Satış Destek ve
  Sevkiyatçı `—` (soru 2'de "Herkes" gösterildi ve seçilmedi).
- **Kur düzeltmesi** `settings.exchange-rates.correct` (SAT-D169/D188): eski "ad bekliyor" satırı #906'daki adla
  güncellendi; hücreler değişmedi. Finans personaları `—` (SAT-D199 açıklaması: "Düzeltme yine yalnız Muhasebe/Muhasebe
  Müdürü/GM/Admin").
- **Kur içe aktarma** `settings.exchange-rates.import` (#906; yeni satır): **hiçbir kararda sahibi yok** → bütün
  sütunlar `?` **NS-14**, Admin dahil (fail-closed). → **EK33 SAT-D205 ile kapandı:** Muhasebe, Muhasebe Müdürü, Genel
  Müdür, Admin `+`; öteki personalar `—` ("Kur görenlerin hepsi" gösterildi ve seçilmedi).
- **Belirlenemeyen arife/yarım günü işaretleme** (SAT-D198; yeni, ad bekliyor): Muhasebe, Muhasebe Müdürü, Genel
  Müdür, Admin `+`; öteki personalar `—` (soru 1'in seçeneği bu dört kişiyi sayar).

*Uygulama ayrıntısı (geri alınabilir) — bu güncellemenin yorumları:*
1. **#906 adları:** üç ad `origin/claude/kur-kaydi` dalındaki `ErpPermissions` ve
   `SettingsExchangeRatePermissionManifestV1`'den alındı; #906 bu güncelleme yazılırken main'de değildi. Ad #906'nın
   review'unda değişirse satır adı izler, hücreler değişmez. Ad main'e gelene kadar satırlar hiçbir şey açmaz.
2. **Fatura kesme adı:** `SATIS_HAZIRLIK_PLANI_v1.md` API-32 planlı adı `sales.invoices.write`'dır (manifest'te yok).
   Satır bu planlı adı yalnız referans olarak yazar; adın kesinleşmesi Sales owner'ın işidir. SAT-D202'nin "kesme"si
   taslağı hazırlama ve onaylanmış taslağı kesme adımlarıdır; onay (API-33) ayrı satırdır.
3. **Fatura onayı (NS-13):** SAT-D88 onayı **Muhasebe** personasına verir, "Müdür"e değil. SAT-D187/D190 Genel Müdür ve
   Admin'e "müdürlerin yaptığı" işleri verir; Muhasebe Müdürü kart gereği Muhasebe personasının yetkilerini
   kendiliğinden almaz (SAT-D187 tablosu). Bu yüzden üç personanın API-33 onayı EK28'den **açıkça türetilemez** ve
   `?` kalır; SAT-D202 de onaylayan kümesini genişletmez. Aynı belirsizlik SAT-D106 tolerans aşımı onayı (API-35) için
   de geçerlidir; bu kart API-35 için ayrı satır açmaz, NS-13'ün cevabı ona da uygulanır mı diye birlikte sorulur.
   **Kapandı (EK33 SAT-D204):** üç persona API-33 ve API-35 onayını alır; API-35 aynı satırla temsil edilir
   (planlı ad `sales.invoices.match` API-35'in eşleştirme komutudur; tolerans onayının adı Sales owner'ın işidir).
4. **Fatura okuma:** API-32 planında `GET invoices` ve `GET invoices/{id}` yazma adına (`sales.invoices.write`)
   bağlıdır; "Satış görür ama kesemez" bu planla karşılanamaz. Ayrı bir fatura okuma adı gerekir; kart ad
   **önermez** (Sales owner'ın işi). Kayıtlı `sales.invoices.read` (SAT-D110) API-31 "faturaya hazır sevkiyatlar"
   okumasıdır, fatura belgesinin okuması değildir; bu satıra eşlenmez. Fatura fiyat/tutar taşıdığı için Sevkiyatçı'nın
   `?` hücresi K4.1/SAT-D150 çerçevesinde değerlendirilir. Muhasebe Müdürü, Genel Müdür ve Admin'in okuma `+`'ı
   SAT-D202'nin onlara verdiği kesme yetkisinden çıkar (kesen, kestiği belgeyi görür); yorumdur, geri alınabilir.
5. **Yarım gün işaretleme:** kişileri kur düzeltmesininkiyle aynıdır, ama ayrı bir eylemdir (günü işaretler, kuru
   değiştirmez). `settings.exchange-rates.correct`'e katılıp katılmayacağı ya da ayrı ad alacağı Settings owner'ın
   teknik işidir; kart birleştirmeyi **önermez**.
6. **Satış Müdürü ve kur düzeltmesi/işaretleme:** SAT-D188 soru 8 ve SAT-D198 kümesi gereği kalın `—`; kur okuması
   (SAT-D199) bunu değiştirmez.

| İzin adı | Admin | Sevkiyatçı | Satış Destek | Satış Uzmanı | Müdür (eski) | Muhasebe | Satış Müdürü | Muhasebe Müdürü | Genel Müdür |
|---|---|---|---|---|---|---|---|---|---|
| `organization.context.read` | + | + | + | + | + | + *(SAT-D164)* | + *(SAT-D191 temel)* | + *(SAT-D191 temel)* | + *(SAT-D191 temel)* |
| `organization.sites.read` | + | + | + | + | + | ? | + *(SAT-D191 temel)* | + *(SAT-D191 temel)* | + *(SAT-D191 temel)* |
| `organization.locations.read` | + | + | + | + | + | ? | + *(SAT-D191 temel)* | + *(SAT-D191 temel)* | + *(SAT-D191 temel)* |
| `organization.work-centers.read` | + | + | ? | ? | ? | ? | — *(SAT-D191)* | — *(SAT-D191)* | + *(SAT-D191, SAT-D187 ara dönem: müdürü yok)* |
| `organization.locations.status.update` | + | — | — | — | ? | ? | — *(SAT-D191)* | — *(SAT-D191)* | + *(SAT-D191, SAT-D187 ara dönem: müdürü yok)* |
| `counterparty.parties.read` | + | + | + | + | + | + *(SAT-D164)* | + *(SAT-D191 temel)* | + *(SAT-D191 temel)* | + *(SAT-D191 temel)* |
| `counterparty.parties.create` | + | — | ? | ? | ? | ? | **—** *(SAT-D192)* | + *(SAT-D192)* | + *(SAT-D192)* |
| `counterparty.parties.update.basic` | + | — | ? | ? | ? | ? | **—** *(SAT-D192)* | + *(SAT-D192)* | + *(SAT-D192)* |
| `counterparty.accounts.create` | + | — | ? | ? | ? | ? | **—** *(SAT-D192)* | + *(SAT-D192)* | + *(SAT-D192)* |
| `counterparty.accounts.status.update` | + | — | — | — | ? | ? | **—** *(SAT-D192)* | + *(SAT-D192)* | + *(SAT-D192)* |
| `product.operational-selection.read` | + | + | + | + | + | + *(SAT-D164)* | + *(SAT-D191 temel)* | + *(SAT-D191 temel)* | + *(SAT-D191 temel)* |
| `sales.drafts.read` | + | **engelli** | + | + | + | + | + *(SAT-D188 okuma)* | + *(SAT-D188 okuma)* | + *(SAT-D188/D190)* |
| `sales.drafts.create` | + | — | + | + | ? | **—** | + *(SAT-D194)* | — *(SAT-D188 satış)* | + *(SAT-D194)* |
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
| `sales.quotes.expired-conversion.approve` (SAT-D24/D44) | + | — | — *(SAT-D24 "yönetici")* | — *(SAT-D24 "yönetici")* | + *(SAT-D24 "yönetici")* | **—** | + *(SAT-D24/D44, SAT-D188)* | — *(SAT-D188)* | + *(SAT-D188/D190)* |
| `sales.invoices.read` (SAT-D110) | + | ? | + *(SAT-D110)* | + *(SAT-D110, SAT-D141 kopya)* | + *(SAT-D110)* | + *(SAT-D110)* | + *(SAT-D110, SAT-D188 okuma)* | + *(SAT-D110, SAT-D188 okuma)* | + *(SAT-D188/D190)* |
| `settings.sales-discount-limits.manage` (SAT-D65/D69) | + *(SAT-D69)* | — | **—** *(SAT-D65)* | **—** *(SAT-D65, SAT-D141 kopya)* | **—** *(SAT-D65)* | — *(SAT-D65 kümesi dışında)* | **—** *(SAT-D65)* | **—** *(SAT-D65)* | **—** *(SAT-D65)* |
| *fazla teslim onayı, eksik teslimle satır kapatma, sipariş kapatma* (SAT-D168; **ad bekliyor**) | + *(SAT-D168)* | — | — *(SAT-D168)* | + *(SAT-D168)* | + *(SAT-D168)* | — *(SAT-D168)* | + *(SAT-D168, SAT-D188)* | — *(SAT-D188)* | + *(SAT-D188/D190)* |
| `sales.orders.source-plan.write` (SAT-D179; ad henüz kayıtlı değil, API-11) | + *(SAT-D179)* | — *(SAT-D179)* | + *(SAT-D179)* | + *(SAT-D179)* | + *(SAT-D179)* | — *(SAT-D179)* | + *(SAT-D179, SAT-D188)* | — *(SAT-D188)* | + *(SAT-D188/D190)* |
| *satış ayarları: teklif kayıp nedenleri, varsayılan teklif geçerlilik günü, teslim toleransı yüzdeleri* (SAT-D189; **ad bekliyor**) | + *(SAT-D189)* | — | — | **—** *(SAT-D189)* | + *(EK28 soru 3)* | — | + *(SAT-D189)* | **—** *(SAT-D189)* | + *(SAT-D189)* |
| *"faturalanmış, sevk edilmemiş" bakiyeyi görme ve yönetme* (SAT-D99; **ad bekliyor**) | + *(SAT-D187)* | ? | ? *(SAT-D99 "satışçı")* | ? *(SAT-D99 "satışçı")* | + *(SAT-D99)* | + *(SAT-D99 yorum)* | + *(SAT-D193)* | + *(SAT-D193)* | + *(SAT-D193)* |
| `settings.tax-master.manage` (SAT-D145) | + | — | — | — | + | **—** | **—** *(SAT-D188)* | + *(SAT-D145, SAT-D188)* | + *(SAT-D188/D190)* |
| `product.item-type.manage` (SAT-D145/D149) | + | — | — | — | + | **—** | **—** *(SAT-D188)* | + *(SAT-D145/D149, SAT-D188)* | + *(SAT-D188/D190)* |
| `counterparty.accounts.e-document.update` (SAT-D155) | + | — | — | — | — | + | — *(SAT-D155)* | — *(SAT-D155)* | — *(SAT-D155)* |
| *vergi ayarı okuma* (SAT-D146; **ad bekliyor**) | + | ? | ? | ? | + | + | + *(SAT-D188 okuma)* | + *(SAT-D188 okuma)* | + *(SAT-D188/D190)* |
| *vergi ayarı önerme* (SAT-D146, SAT-D163; **ad bekliyor**) | + | — | — | — | — | + | — *(SAT-D146)* | — *(SAT-D146)* | — *(SAT-D146)* |
| *vergi ayarı önerisini onaylama* (SAT-D146; **ad bekliyor**) | + | — | — | — | + | **—** | **—** *(SAT-D188)* | + *(SAT-D146, SAT-D188)* | + *(SAT-D188/D190)* |
| `settings.exchange-rates.read` (SAT-D199; ad #906'da, main'de henüz kayıtlı değil) | + *(SAT-D199)* | — *(SAT-D199)* | — *(SAT-D199)* | + *(SAT-D199)* | yok *(EK28 öncesi)* | + *(SAT-D199)* | + *(SAT-D199)* | + *(SAT-D199)* | + *(SAT-D199)* |
| `settings.exchange-rates.correct` = kur düzeltmesi (gerekçeli, audit'li) (SAT-D169; ad #906'da, main'de henüz kayıtlı değil; alıştaki karşılığı ALS-D6) | + *(SAT-D169)* | — | — | — | + *(SAT-D169 "Müdürler")* | + *(SAT-D169)* | **—** *(SAT-D188, soru 8)* | + *(SAT-D169, SAT-D188)* | + *(SAT-D188/D190)* |
| `settings.exchange-rates.import` (SAT-D205; ad #906'da, main'de henüz kayıtlı değil) | + *(SAT-D205)* | — *(SAT-D205)* | — *(SAT-D205)* | — *(SAT-D205)* | yok *(EK28 öncesi)* | + *(SAT-D205)* | **—** *(SAT-D205)* | + *(SAT-D205)* | + *(SAT-D205)* |
| *belirlenemeyen arife/yarım günü tam/yarım gün olarak gerekçeli işaretleme* (SAT-D198; **ad bekliyor**) | + *(SAT-D198)* | — *(SAT-D198)* | — *(SAT-D198)* | — *(SAT-D198)* | yok *(EK28 öncesi)* | + *(SAT-D198)* | **—** *(SAT-D198)* | + *(SAT-D198)* | + *(SAT-D198)* |
| *belge tarihinden önceki sevklerde KDV istisnası onayı* (SAT-D186; **ad bekliyor**) | + *(SAT-D186)* | — | — | — | yok *(EK28 öncesi)* | — *(SAT-D186)* | — *(SAT-D186, SAT-D188)* | + *(SAT-D186, SAT-D188)* | + *(SAT-D186)* |
| *dış fatura ↔ sipariş/irsaliye eşleştirme* (SAT-D142, SAT-D163; **ad bekliyor**) | + | — | — | — | — | + | — *(SAT-D142/D163)* | — *(SAT-D142/D163)* | — *(SAT-D142/D163)* |
| *fatura kesme = ERP fatura taslağı hazırlama ve onaylanmış taslağı kesme* (SAT-D142, SAT-D202, API-32; **fatura dalgası, ad bekliyor** — planlı ad `sales.invoices.write`) | + *(SAT-D202)* | — *(SAT-D202)* | — *(SAT-D202)* | **—** *(SAT-D202 "görür ama kesemez")* | ? | + *(SAT-D142, SAT-D202)* | **—** *(SAT-D202 "görür ama kesemez")* | + *(SAT-D202)* | + *(SAT-D202)* |
| *fatura taslağı onayı ve tolerans farkı onayı* (SAT-D88, SAT-D106, SAT-D204, API-33/API-35; **fatura dalgası, ad bekliyor** — API-33 planlı adı `sales.invoices.approve`) | + *(SAT-D204)* | — *(SAT-D88, SAT-D204)* | — *(SAT-D88, SAT-D204)* | — *(SAT-D88, SAT-D204)* | — *(SAT-D88)* | + *(SAT-D88, SAT-D204; hazırlayan onaylayamaz)* | **—** *(SAT-D204)* | + *(SAT-D204)* | + *(SAT-D204)* |
| *fatura okuma* (SAT-D202 "Satış görür"; **ad bekliyor** — API-32 planında GET yazma adına bağlı) | + *(SAT-D202)* | ? | ? | + *(SAT-D202)* | yok *(EK28 öncesi)* | + *(SAT-D202)* | + *(SAT-D202)* | + *(SAT-D202)* | + *(SAT-D202)* |
| `purchasing.orders.read` (Satın alma personaları ek tablosuna bakınız) | + | + | + | + | + | ? | — *(SAT-D191)* | — *(SAT-D191)* | + *(SAT-D191, SAT-D187 ara dönem: Satın Alma Müdürü)* |
| `purchasing.orders.create` (Satın alma personaları ek tablosuna bakınız) | + | — | + | + | ? | ? | — *(SAT-D191)* | — *(SAT-D191)* | + *(SAT-D191, SAT-D187 ara dönem: Satın Alma Müdürü)* |
| `purchasing.orders.approve` (Satın alma personaları ek tablosuna bakınız) | + | — | **—** | **—** | + | ? | — *(SAT-D187/D188 alış)* | — *(SAT-D187/D188 alış)* | + *(SAT-D187 ara dönem, SAT-D188; ALS-D3)* |
| `inventory.labels.read` | + | + | ? | ? | ? | ? | — *(SAT-D191)* | — *(SAT-D191)* | + *(SAT-D191, SAT-D187 ara dönem: Stok Müdürü)* |
| `inventory.balances.read` | + | + | ? | ? | ? | ? | — *(SAT-D191)* | — *(SAT-D191)* | + *(SAT-D191, SAT-D187 ara dönem: Stok Müdürü)* |
| `inventory.opening-stock.record` | + | — | — | — | ? | ? | — *(SAT-D191)* | — *(SAT-D191)* | + *(SAT-D191, SAT-D187 ara dönem: Stok Müdürü)* |
| `manufacturing.orders.read` | + | + | ? | ? | ? | ? | — *(SAT-D191)* | — *(SAT-D191)* | + *(SAT-D191, SAT-D187 ara dönem: Üretim Müdürü)* |
| `manufacturing.plans.write` | + | — | — | — | ? | ? | — *(SAT-D191)* | — *(SAT-D191)* | + *(SAT-D191, SAT-D187 ara dönem: Üretim Müdürü)* |
| `manufacturing.plans.approve` | + | — | — | — | ? | ? | — *(SAT-D191)* | — *(SAT-D191)* | + *(SAT-D191, URT-D2; SAT-D187 ara dönem: Üretim Müdürü)* |
| `manufacturing.orders.create` | + | — | — | — | ? | ? | — *(SAT-D191)* | — *(SAT-D191)* | + *(SAT-D191, SAT-D187 ara dönem: Üretim Müdürü)* |
| `manufacturing.facts.record` | + | — | — | — | ? | ? | — *(SAT-D191)* | — *(SAT-D191)* | + *(SAT-D191, SAT-D187 ara dönem: Üretim Müdürü)* |
| `logistics.loading-instructions.read` | + | + | ? | ? | ? | ? | — *(SAT-D191)* | — *(SAT-D191)* | + *(SAT-D191, SAT-D187 ara dönem: Lojistik Müdürü)* |
| `logistics.loading-instructions.create` | + | + | — | — | ? | ? | — *(SAT-D191)* | — *(SAT-D191)* | + *(SAT-D191, SAT-D187 ara dönem: Lojistik Müdürü)* |
| `logistics.loads.record` | + | + | — | — | ? | ? | — *(SAT-D191)* | — *(SAT-D191)* | + *(SAT-D191, SAT-D187 ara dönem: Lojistik Müdürü)* |
| `accounting.periods.read` | + | ? | — | — | ? | — *(SAT-D164)* | — *(SAT-D191)* | + *(SAT-D191)* | + *(SAT-D191, SAT-D188/D190)* |
| `accounting.journals.read` | + | **?** | — | — | ? | — *(SAT-D164)* | — *(SAT-D191)* | + *(SAT-D191)* | + *(SAT-D191, SAT-D188/D190)* |
| `accounting.posting-queues.read` | + | ? | — | — | ? | — *(SAT-D164)* | — *(SAT-D191)* | + *(SAT-D191)* | + *(SAT-D191, SAT-D188/D190)* |
| `accounting.periods.propose` | + | — | — | — | ? | ? | — *(SAT-D191)* | + *(SAT-D191; P6: kendi önerisini onaylayamaz)* | + *(SAT-D191, SAT-D188/D190; P6)* |
| `accounting.periods.approve` | + | — | — | — | ? | ? | — *(SAT-D191)* | + *(SAT-D191; P6: kendi önerisini onaylayamaz)* | + *(SAT-D191, SAT-D188/D190; P6)* |
| `accounting.periods.close` | + | — | — | — | ? | ? | — *(SAT-D191)* | + *(SAT-D191)* | + *(SAT-D191, SAT-D188/D190)* |
| `finance.bank-account.read` | + | **—** | — | — | ? | — *(SAT-D164)* | — *(K4.2, SAT-D197)* | — *(K4.2, SAT-D197)* | **—** *(SAT-D197: oluşturma tarafındaki genel okuma)* |
| `finance.manual-receipt.draft.read` | + | **—** | — | — | ? | — *(SAT-D164)* | — *(K4.2, SAT-D197)* | — *(K4.2, SAT-D197)* | — *(SAT-D197: tablodaki hiçbir onay gerektirmez)* |
| `finance.manual-receipt.draft.save` | + | **—** | — | — | ? | ? | — *(K4.2, SAT-D197)* | — *(K4.2, SAT-D197)* | **—** *(SAT-D197: oluşturamaz)* |
| `finance.manual-receipt.amount.read` | + | **—** | — | — | ? | — *(SAT-D164)* | — *(K4.2, SAT-D197)* | — *(K4.2, SAT-D197)* | — *(SAT-D197: tablodaki hiçbir onay gerektirmez)* |
| `finance.bank-account.propose` (CRITICAL) | insan kapısı | — | — | — | — | — | — *(Critical)* | — *(Critical)* | — *(Critical)* |
| `finance.bank-account.approve` (CRITICAL) | insan kapısı | — | — | — | — | — | — *(Critical)* | — *(Critical)* | — *(Critical)* |
| `identity.profile.read` | + | + | + | + | + | + *(SAT-D164)* | + *(SAT-D191 temel)* | + *(SAT-D191 temel)* | + *(SAT-D191 temel)* |
| `identity.access.request` | + | + | + | + | + | ? | + *(SAT-D191 temel)* | + *(SAT-D191 temel)* | + *(SAT-D191 temel)* |
| `identity.access.approve` (CRITICAL) | insan kapısı | — | — | — | — | — | — *(Critical)* | — *(Critical)* | — *(Critical)* |
| `identity.roles.manage` (CRITICAL) | insan kapısı | — | — | — | — | — | — *(Critical)* | — *(Critical)* | — *(Critical)* |
| `identity.audit.read` (CRITICAL) | insan kapısı | — | — | — | — | — | — *(Critical)* | — *(Critical)* | — *(Critical)* |

**Finans personaları (2 Kasım 2026, EK30 SAT-D196/D197).** Finans Uzmanı ve Finans Müdürü yalnız finans adlarında anlam
taşıdığı için ana tabloya sütun olarak değil, bu ek tabloyla eklendi. İkisi finans dışında **yalnız yedi temel izni**
alır (aşağıdaki ilk yedi satır, SAT-D191 temel); ana tablonun öteki bütün finans dışı satırlarında ikisi de **`—`**
(deny-first). **İstisna (2 Kasım 2026, EK31 SAT-D199):** kur okuması `settings.exchange-rates.read` ikisine de `+`;
kur düzeltmesi ve yarım gün işaretlemesi `—` (SAT-D169/D188, SAT-D198), kur içe aktarma `—` (SAT-D205), fatura kesme `—`
(SAT-D202), fatura onayı `—` (SAT-D88, SAT-D204), fatura okuma `?` (karar değinmiyor). Bu satırlar aşağıdaki tablonun sonundadır. Finans Uzmanı **oluşturur** (taslak kaydetme ve oluşturma
için gereken okumalar), Finans Müdürü **onaylar** (onay ve onayın gerektirdiği okumalar); hazırlayan ≠ onaylayan
(SAT-D196). Genel Müdür finans işlemini **onaylayabilir ama oluşturamaz**; ona yalnız onay ve onayın gerektirdiği okuma
verilir (SAT-D197). Satış Müdürü ve Muhasebe Müdürü için K4.2 aynen kalır: genel Finance yok. Admin her iki işi de
yapabilir (SAT-D187, SAT-D196) ama **kendi kaydını onaylayamaz** (P6, kayıt kuralı).

| İzin adı | Admin | Finans Uzmanı | Finans Müdürü | Genel Müdür | Satış Müdürü / Muhasebe Müdürü |
|---|---|---|---|---|---|
| `organization.context.read` | + | + *(SAT-D191 temel — "bütün personalara verilen temel izinler"; SAT-D164 emsali; yorum, geri alınabilir)* | + *(SAT-D191 temel)* | + *(SAT-D191 temel; ana tablo)* | + *(SAT-D191 temel; ana tablo)* |
| `organization.sites.read` | + | + *(SAT-D191 temel — "bütün personalara verilen temel izinler"; SAT-D164 emsali; yorum, geri alınabilir)* | + *(SAT-D191 temel)* | + *(SAT-D191 temel; ana tablo)* | + *(SAT-D191 temel; ana tablo)* |
| `organization.locations.read` | + | + *(SAT-D191 temel — "bütün personalara verilen temel izinler"; SAT-D164 emsali; yorum, geri alınabilir)* | + *(SAT-D191 temel)* | + *(SAT-D191 temel; ana tablo)* | + *(SAT-D191 temel; ana tablo)* |
| `identity.profile.read` | + | + *(SAT-D191 temel — "bütün personalara verilen temel izinler"; SAT-D164 emsali; yorum, geri alınabilir)* | + *(SAT-D191 temel)* | + *(SAT-D191 temel; ana tablo)* | + *(SAT-D191 temel; ana tablo)* |
| `identity.access.request` | + | + *(SAT-D191 temel — "bütün personalara verilen temel izinler"; SAT-D164 emsali; yorum, geri alınabilir)* | + *(SAT-D191 temel)* | + *(SAT-D191 temel; ana tablo)* | + *(SAT-D191 temel; ana tablo)* |
| `counterparty.parties.read` | + | + *(SAT-D191 temel — "bütün personalara verilen temel izinler"; SAT-D164 emsali; yorum, geri alınabilir)* | + *(SAT-D191 temel)* | + *(SAT-D191 temel; ana tablo)* | + *(SAT-D191 temel; ana tablo)* |
| `product.operational-selection.read` | + | + *(SAT-D191 temel — "bütün personalara verilen temel izinler"; SAT-D164 emsali; yorum, geri alınabilir)* | + *(SAT-D191 temel)* | + *(SAT-D191 temel; ana tablo)* | + *(SAT-D191 temel; ana tablo)* |
| `finance.bank-account.read` | + | + *(SAT-D196: taslak kaydetme ekranı bu okumayı ister)* | — *(SAT-D196: tablodaki hiçbir onay gerektirmez)* | **—** *(SAT-D197)* | — *(K4.2)* |
| `finance.manual-receipt.draft.read` | + | + *(SAT-D196)* | — *(SAT-D196: tablodaki hiçbir onay gerektirmez)* | — *(SAT-D197)* | — *(K4.2)* |
| `finance.manual-receipt.draft.save` | + | + *(SAT-D196)* | **—** *(SAT-D196: müdür oluşturmaz)* | **—** *(SAT-D197)* | — *(K4.2)* |
| `finance.manual-receipt.amount.read` | + | + *(SAT-D196: taslağın tutarı)* | — *(SAT-D196: tablodaki hiçbir onay gerektirmez)* | — *(SAT-D197)* | — *(K4.2)* |
| `finance.bank-account.propose` (CRITICAL) | insan kapısı | — *(Critical)* | — *(Critical)* | — *(Critical)* | — *(Critical)* |
| `finance.bank-account.approve` (CRITICAL) | insan kapısı | — *(Critical)* | — *(Critical)* | — *(Critical)* | — *(Critical)* |
| `settings.exchange-rates.read` (SAT-D199; #906) | + *(SAT-D199)* | + *(SAT-D199)* | + *(SAT-D199)* | + *(SAT-D199)* | + *(SAT-D199)* |
| `settings.exchange-rates.correct` (SAT-D169/D188; #906) | + *(SAT-D169)* | — *(SAT-D199 açıklaması, SAT-D188)* | — *(SAT-D199 açıklaması, SAT-D188)* | + *(SAT-D188/D190)* | — (Satış Müdürü, SAT-D188 soru 8) / + (Muhasebe Müdürü, SAT-D169/D188) |
| `settings.exchange-rates.import` (SAT-D205; #906) | + *(SAT-D205)* | — *(SAT-D205)* | — *(SAT-D205)* | + *(SAT-D205)* | — (Satış Müdürü) / + (Muhasebe Müdürü) *(SAT-D205)* |
| *belirlenemeyen arife/yarım günü işaretleme* (SAT-D198; ad bekliyor) | + *(SAT-D198)* | — *(SAT-D198)* | — *(SAT-D198)* | + *(SAT-D198)* | — (Satış Müdürü) / + (Muhasebe Müdürü) *(SAT-D198)* |
| *fatura kesme* (SAT-D202; ad bekliyor) | + *(SAT-D202)* | — *(SAT-D202)* | — *(SAT-D202)* | + *(SAT-D202)* | — (Satış Müdürü) / + (Muhasebe Müdürü) *(SAT-D202)* |
| *fatura taslağı onayı ve tolerans farkı onayı* (SAT-D88, SAT-D106, SAT-D204, API-33/API-35; ad bekliyor) | + *(SAT-D204)* | — *(SAT-D204)* | — *(SAT-D204)* | + *(SAT-D204)* | — (Satış Müdürü) / + (Muhasebe Müdürü) *(SAT-D204)* |
| *fatura okuma* (SAT-D202 "Satış görür"; ad bekliyor) | + *(SAT-D202)* | ? | ? | + *(SAT-D202)* | + *(SAT-D202)* |

- **SAT-D199 istisnası (2 Kasım 2026):** son yedi satır EK31/EK32 eşlemesinin bu tablodaki karşılığıdır; Genel Müdür
  ve Satış/Muhasebe Müdürü hücreleri ana tablodakinin aynısıdır. Finans personalarının kur okuması finansın SAT-D196
  "oluşturma/onay" ayrımından değil, doğrudan SAT-D199'un sayımından gelir; bu yüzden ikisi de `+` alır. *Uygulama
  ayrıntısı (geri alınabilir):* fatura okumasında Finans personaları için `?` yazıldı, `—` değil — SAT-D202 yalnız
  kesmeyi kapalı liste olarak sayar, okumaya değinmez.
- **Temel izinler (ilk yedi satır):** SAT-D191'in uygulama ayrıntısı bunları "bütün personalara verilen temel izinler"
  diye tanımlar ve müdürlere de verir; Finans Müdürü bir müdür personası olduğu için alır. Finans Uzmanı için bu bir
  **yorumdur (geri alınabilir)**: SAT-D191 bunları bütün personalara verilen izinler sayar ve bu izinler olmadan persona
  oturumda şirket bağlamını ve kendi profilini açamaz (Muhasebe için SAT-D164 emsali). Genel Müdür ve Satış/Muhasebe
  Müdürü hücreleri ana tablodakinin aynısıdır.
- Bugün tablodaki dört Critical olmayan finans adının hiçbiri bir **onay** değildir; bu yüzden Finans Müdürü ve Genel
  Müdür bu tabloda hiçbir `+` almaz. Onayın gerektirdiği okuma, onay adı karta eklendiğinde o onayla birlikte
  değerlendirilir (ör. `finance.manual-receipt.reverse.approve`). Critical `finance.bank-account.approve` insan
  kapısında verilirse, onu karşılayan okuma da aynı kapıda değerlendirilir.
- `ErpPermissions`'ta tabloda satırı olmayan başka `finance.*` adları da vardır; bu kart onlara satır **eklemez**. Sonradan
  eklendiklerinde yalnız açıkça **oluşturma** (Finans Uzmanı) ya da **onay** (Finans Müdürü, Genel Müdür) olanlar SAT-D196'ya
  göre dağıtılır; `finance.manual-receipt.record` gibi `*.record` adları (FIN-T05 "tek dar yetkili operatör") ve ne
  oluşturma ne onay olan her ad **NEEDS-SINAN** olur, kendiliğinden Finans Uzmanı'na gitmez.

**Satın alma personaları (7 Kasım 2026, Alış EK8 §3, EK10 §2, EK11 §2).** Satın Alma Uzmanı ve Satın Alma Müdürü
yalnız alış adlarında anlam taşıdığı için bu ek tabloyla eklendi; ana tablonun öteki bütün alış dışı satırlarında ikisi
de yalnız SAT-D191 temel okumalarını alır, öteki her hücre **`—`** (deny-first; EK8 §3). Bu tablo ana tablonun
`purchasing.*` satırlarına üstündür. Kaynak eşleme: EK8 §3 "Kart için not" (ALS-D53, ALS-D54, ALS-D56), EK10 §2
(ALS-D66), EK11 §2 (ALS-D70). Hücrelerdeki *yorum* işaretleri karar dosyalarındaki yorumların aynısıdır (geri alınabilir).

| İzin adı | Admin | Satın Alma Uzmanı | Satın Alma Müdürü | Satış Destek | Satış Uzmanı | Genel Müdür | Muhasebe | Muhasebe Müdürü | Sevkiyatçı | Satış Müdürü |
|---|---|---|---|---|---|---|---|---|---|---|
| `purchasing.requests.create` (ad bekliyor; bugün `purchasing.orders.create` ile ölçülür, ASSUMPTION-PUR4-1) | + | + *(ALS-D54)* | + *(ALS-D54)* | + *(ALS-D54)* | + *(ALS-D54)* | ? *(yorum, SAT-D194 benzetmesi; seçenekte yok)* | — *(ALS-D54)* | — *(ALS-D54)* | — *(EK8 §3)* | — *(EK8 §3: ALS-D54 "Satış" yalnız Satış Destek ve Satış Uzmanı)* |
| `purchasing.requests.withdraw` | + | + *(ALS-D54, ALS-D47)* | + *(ALS-D54, ALS-D47)* | + *(ALS-D54, ALS-D47)* | + *(ALS-D54, ALS-D47)* | ? *(talep açarsa; yukarıdaki satıra bağlı)* | — *(ALS-D54)* | — *(ALS-D54)* | — *(EK8 §3)* | — *(EK8 §3)* |
| `purchasing.requests.approve` | + | — *(ALS-D53)* | + *(ALS-D2, ALS-D53)* | — | — | + *(ALS-D53, SAT-D188)* | — *(ALS-D53)* | — *(ALS-D53)* | — | — *(EK8 §3)* |
| `purchasing.orders.create` | + | + *(ALS-D53)* | + *(yorum: SAT-D191 ara dönem hücresinin geçişi; SAT-D194 benzetmesi)* | + *(kart §4)* | + *(kart, SAT-D141)* | + *(SAT-D188 soru 9)* | ? *(ana tabloda olduğu gibi)* | — *(SAT-D191)* | — | — *(SAT-D191)* |
| `purchasing.orders.read` | + | + *(yorum: hazırlamanın gerektirdiği okuma)* | + *(yorum: SAT-D191 ara dönem hücresinin geçişi)* | + *(kart)* | + *(kart)* | + | ? *(ana tabloda olduğu gibi; price.read ile çift NEEDS-SINAN, aşağıda)* | — *(SAT-D191)* | + *(kart §4; fiyatlar `Withheld`, PRS-02 — aşağıda "engeller" 2. madde)* | — *(SAT-D191)* |
| `purchasing.orders.approve` | + | — *(ALS-D53)* | + *(ALS-D3, ALS-D53)* | **—** | **—** | + *(ALS-D53, SAT-D188)* | — *(ALS-D53)* | — *(ALS-D53)* | — | — *(SAT-D187/D188 alış)* |
| `purchasing.orders.cancel` (A1-3, PR #932) | + *(ALS-D66)* | — *(ALS-D66)* | + *(ALS-D66)* | — *(yorum, EK10 §2)* | — *(yorum, EK10 §2)* | + *(ALS-D66)* | — *(yorum, EK10 §2)* | — *(yorum, EK10 §2)* | — *(yorum, EK10 §2)* | — *(yorum, EK10 §2)* |
| `purchasing.orders.price.read` (A2a-1, PR #944) | + *(ALS-D70)* | + *(ALS-D70)* | + *(ALS-D70)* | + *(ALS-D70)* | + *(ALS-D70)* | + *(ALS-D70)* | + *(ALS-D70)* | + *(ALS-D70)* | **—** *(ALS-D70, PRS-02; kart §4 "fiyatları göremez")* | — *(yorum, EK11 §2: seçenek saymaz)* |
| *alış faturası kur düzeltmesi* (`settings.exchange-rates.correct` veya alışa özgü ad; ad bekliyor) | + *(ALS-D56)* | — *(ALS-D56)* | **—** *(ALS-D56)* | — | — | + *(ALS-D56)* | + *(ALS-D56)* | + *(ALS-D56)* | — | **—** *(SAT-D188 soru 8)* |

- **Satış Destek ve Satış Uzmanı fiyatı görür (ALS-D70):** Sinan önerilen "Satın alma + yönetim + muhasebe" yerine
  "Satış Destek de görsün" seçeneğini seçti; seçenek açıklaması "alış siparişi girebilen Satış Destek ve Satış Uzmanı"
  der. Satış Müdürü sayılmadığı için `—` (yorum; geri alınabilir). Finans Uzmanı / Finans Müdürü bu satırda da `—`
  (kartın finans dışı satır kuralı).
- **Muhasebe: `price.read` `+`, `orders.read` `?` — NEEDS-SINAN.** ALS-D70 Muhasebe'ye fiyat okumasını verir, ama
  fiyat yalnız sipariş detayında açılır ve detaya `purchasing.orders.read` ile ulaşılır (PRS-02 açıklama izni, erişim
  izni değil). `orders.read` olmadan `price.read` işe yaramaz. Muhasebe'nin `purchasing.orders.read` hücresi hiçbir
  kararda bağlanmadı (ana tabloda `?`); bu çift tahminle doldurulmaz, **NEEDS-SINAN** olarak açık kalır ve o zamana
  kadar fiilen hiçbir şey açmaz.
- `purchasing.goods-receipts.record` (depo kabul, PRS-02): EK8 §3'te `?` / sorulmadı; bu tabloya satır eklenmedi ve
  deny-first altında kimseye önerilmemiş sayılır.
- **Gerçek grant insan kapısıdır.** Bu tablodaki adlar manifestte yalnız **kayıtlıdır**; SENTETIK browser-suite
  personaları dışında kimse taşımaz. Bir kişiye hem Satın Alma Müdürü hem başka bir persona verilmesi (SAT-D187) de
  gerçek atamadır ve aynı kapıdadır.

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
    SAT-D163 yalnız Admin'i ekler). ~~Fatura kesmede yalnız Muhasebe söylendi; diğer personalar `?`.
    **Admin dahil:** SAT-D163 Admin'e yalnız eşleştirmeyi ve vergi önerisini ekledi; fatura kesmede Admin için karar
    yoktur (hücre `? (karar yok)`).~~ **Kapandı (2 Kasım 2026, SAT-D202):** Muhasebe, Muhasebe Müdürü, Genel Müdür ve
    Admin keser; Satış görür ama kesemez; öteki personalar kesmez. Onay (API-33) ayrı satırdır; NS-13 EK33 SAT-D204 ile kapandı (Muhasebe, Muhasebe Müdürü, Genel Müdür, Admin).
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
  - **SAT-D99** "faturalanmış, sevk edilmemiş" bakiye: ilk turda NS-1 idi; **Satış Müdürü ve Muhasebe Müdürü** görür
    ve yönetir, Genel Müdür ve Admin de (SAT-D193). Kapatma yolu mali müşavirle belirlenir (SAT-D99).
  - **SAT-D194** `sales.drafts.create`: Satış Müdürü ve Genel Müdür taslak açar; açan onaylayamaz (P6).
  - **SAT-D192** cari yönetimi yalnız Muhasebe Müdürü'ndedir (Genel Müdür ve Admin de); "Satış ve Muhasebe Müdürü"
    seçeneği gösterildi ve seçilmedi → Satış Müdürü kalın `—`.
  - **SAT-D195 fason fiyatı:** fasonda fiyat listesi kullanılmaz; fiyat kg başına elle yazılır ve siparişin normal iç
    onayına gider. **Yeni izin gerekmez:** elle fiyat yazmak `sales.orders.price.write` (SAT-D151) ister, onay
    `sales.orders.approve`'dur; liste olmadığı için ayrıca taban/iskonto istisna onayı yoktur.
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
   *(7 Kasım 2026, A2a-1, PR #944 `6c58f0c8`: alış siparişine fiyat eklendi ve uyarı karşılandı — PRS-02 ile sipariş
   detayı fiyatları yalnız `purchasing.orders.price.read` sahibine `Visible` gösterir, öteki okuyucuya `Withheld`;
   mal kabul okumaları ve arz portları fiyat taşımaz. Sevkiyatçı'nın `purchasing.orders.read` `+` hücresi bu yüzden
   geçerli kalır; fiyat okuması Sevkiyatçı'ya verilmez — EK11 ALS-D70.)*
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
*(2 Kasım 2026: tek Müdür SAT-D187 ile Satış Müdürü, Muhasebe Müdürü, Genel Müdür ve modül müdürlerine ayrıldı. Bu
sınır Satış Müdürü ve Muhasebe Müdürü için aynen geçerlidir. Genel Müdür için yalnız finans **onayı** ve onayın
gerektirdiği okuma kadar kalkar; finans işlemi oluşturma ve banka hesabı okuması gibi genel finans okumaları ona da
verilmez (EK30 SAT-D197). Finans işini Finans Uzmanı ve Finans Müdürü yapar (SAT-D196).)*

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
7. **EK20…EK29 grant PR'ı** (SAT-D141…D146, SAT-D150/D151, SAT-D153/D155/D159, SAT-D163, SAT-D164; müdür ayrımı
   SAT-D186…D190, EK24–EK27 satırları ve EK29 SAT-D191…D194): persona/rol tanımı ve izin eşlemesi yalnız bu kartta **karar numarasıyla
   işaretli her hücre**yi açar — eski satırlardaki SAT-D164, SAT-D188 ve SAT-D191…D194 hücreleri dahil; "Müdür (eski)" sütunu, `?`
   ve **NS-n** hücreleri hiçbir şey açmaz. Ad bekleyen satırlar (risk istisnası, SAT-D146 vergi önerisi, eşleştirme,
   SAT-D168/D169/D186/D189/D99, `sales.orders.source-plan.write`; EK31/EK32'den SAT-D198 işaretleme, SAT-D202 fatura
   kesme, onay (SAT-D204) ve okuma, #906'nın üç `settings.exchange-rates.*` adı) adları manifest'e gelince eşlenir. Aynı PR
   `ErpPermissions` açıklamalarını kararlarla hizalar.

**İnsan kapısı değişmez:** EK20/EK21, EK28 ve EK29 persona kararları Sinan'ın somut kararıdır, ama bu kart da, o kararlarla açılacak
eşleme PR'ı da **gerçek bir kullanıcıya rol atamaz** ve **production'da hiçbir şeyi etkinleştirmez**. Gerçek rol
ataması, Critical grant'ler ve production aktivasyonu her zaman insan kapısında kalır (SAT-D110; EK20, EK21, EK28 ve EK29
"Sınırlar"). Bir kişiye birden fazla müdürlük verilmesi (SAT-D187) de gerçek bir atamadır ve aynı insan kapısındadır.

İzin adları her durumda sahibinin teknik işidir; bu kart ad önermez ve atama yapmaz.

## NEEDS-SINAN — müdür ayrımı (2 Kasım 2026, SAT-D188 son cümlesi; EK29, EK31/EK32 ve EK33 ile güncellendi)

SAT-D188 "bir hücre bu sınıflara açıkça girmiyorsa persona kartı PR'ında NEEDS-SINAN yazılır" diyor. İlk turda on iki hücre
grubu `? NS-n` olarak işaretlendi; EK29 (SAT-D191…D195) on birini, EK32 (SAT-D202) on ikincisini (NS-11) kapattı.
EK31/EK32 güncellemesi iki yeni grup açtı (NS-13, NS-14); EK33 (SAT-D204, SAT-D205) ikisini de kapattı.

**Açık kalan: yok** (2 Kasım 2026, EK33'ten sonra). Kartta `NEEDS-SINAN` hücresi kalmadı; `?` hücreleri (kararın
değinmediği persona) ve ad bekleyen satırlar hiçbir şey açmaz.

**EK33 ile kapananlar** (EK31/EK32 güncellemesinde açılmıştı):

| NS | Hücre | Durum |
|---|---|---|
| NS-13 | *fatura taslağı onayı* (SAT-D88, SAT-D106, API-33; tolerans aşımı onayı API-35 aynı soruyla) — Muhasebe Müdürü, Genel Müdür, Admin | SAT-D88 onayı yalnız **Muhasebe** personasına verir; SAT-D202 onaylayan kümesini genişletmez ve bu üç personanın onayını "EK28 SAT-D187/D190 kurallarıyla kartta değerlendirilir" diye bırakır. SAT-D187/D190 "müdürlerin yaptığı" işleri Genel Müdür ve Admin'e verir; fatura onayı bir müdür işi olarak hiçbir kararda sayılmadı ve Muhasebe Müdürü Muhasebe personasının yetkilerini kendiliğinden almaz. Açıkça türetilemediği için üç hücre `?`, **hiçbir şey açmaz** (fail-closed). Soru: Muhasebe Müdürü, Genel Müdür ve Admin fatura taslağını (ve SAT-D106 tolerans farkını) onaylayabilir mi? Hazırlayan ≠ onaylayan (SAT-D106) her durumda kayıt kuralı olarak kalır. **KAPANDI (EK33 SAT-D204):** Muhasebe, Muhasebe Müdürü, Genel Müdür ve Admin fatura taslağını (API-33) ve tolerans farkını (API-35) onaylar; taslağı hazırlayan kişi onaylayamaz (kayıt kuralı). Satış personaları, Satış Destek, Sevkiyatçı ve Finans personaları `—`. |
| NS-14 | `settings.exchange-rates.import` (#906) — bütün personalar | Kurun TCMB/resmî kaynaktan içe aktarılmasını kimin başlatacağı hiçbir kararda yok (SAT-D169 kurun **otomatik** geldiğini söyler; SAT-D199 okumayı, SAT-D169/D188 düzeltmeyi söyler). Bütün sütunlar `?`, Admin dahil; **hiçbir şey açmaz**. Dış kaynak bağlantısının açılması ayrıca insan kapısıdır (SAT-D198, EK31 "Sınırlar"). Soru: elle içe aktarmayı (otomatik çalışma dışında) kim başlatabilir? **KAPANDI (EK33 SAT-D205):** Muhasebe, Muhasebe Müdürü, Genel Müdür ve Admin — kuru düzeltebilen kişilerle aynı; öteki personalar `—` ("Kur görenlerin hepsi" ve "Yalnız Admin" gösterildi, seçilmedi). Kaynak bağlantısının açılması insan kapısında kalır. |

**EK32 ile kapanan:**

| NS | Hücre | Eski Müdür | Durum |
|---|---|---|---|
| NS-11 | *fatura kesme = ERP fatura taslağı hazırlama* (SAT-D142, API-32) | `?` | Fatura dalgası; yalnız Muhasebe söylendi. Hiçbir müdür, Genel Müdür ve Admin için karar yok; SAT-D191 satış/muhasebe **dışı** yetkileri dağıtır ve faturayı açıkça saymaz. Üç müdür sütunu ve Admin `?` kalır, **hiçbir şey açmaz** (fail-closed, deny-first). Soru: fatura taslağını hangi müdür hazırlar/onaylar, Admin dahil mi? **KAPANDI (SAT-D202):** Muhasebe, Muhasebe Müdürü, Genel Müdür, Admin keser; Satış Uzmanı/Satış Müdürü görür, kesmez; Satış Destek, Sevkiyatçı, Finans personaları kesmez. Onay sorusu NS-13'e taşındı. |

**EK29 ile kapananlar** (hücreler tabloda karar numarasıyla dolduruldu):

| NS | Hücre(ler) | Karar | Sonuç |
|---|---|---|---|
| NS-1 | SAT-D99 "faturalanmış, sevk edilmemiş" bakiye | SAT-D193 | Satış Müdürü + Muhasebe Müdürü (+ Genel Müdür, Admin) |
| NS-2 | `identity.access.request` | SAT-D191 (temel izin) | Bütün müdürler |
| NS-3 | `organization.work-centers.read`, `organization.locations.status.update` | SAT-D191 | Organizasyon modülünün müdürü yok → Genel Müdür + Admin; Satış/Muhasebe Müdürü `—` |
| NS-4 | `counterparty.parties.create`, `.update.basic`, `counterparty.accounts.create`, `.status.update` | SAT-D192 | Yalnız Muhasebe Müdürü (+ Genel Müdür, Admin); Satış Müdürü kalın `—` |
| NS-5 | `purchasing.orders.create` (ve okuma, Review B P2) | SAT-D191 | Satın Alma Müdürü → ara dönem Genel Müdür + Admin; Satış/Muhasebe Müdürü `—` |
| NS-6 | üç `inventory.*` adı | SAT-D191 | Stok Müdürü → ara dönem Genel Müdür + Admin |
| NS-7 | beş `manufacturing.*` adı | SAT-D191, URT-D2 | Üretim Müdürü → ara dönem Genel Müdür + Admin |
| NS-8 | üç `logistics.*` adı | SAT-D191 | Lojistik Müdürü → ara dönem Genel Müdür + Admin |
| NS-9 | altı `accounting.*` dönem/fiş/kuyruk adı | SAT-D191 | Muhasebe Müdürü (+ Genel Müdür, Admin); Satış Müdürü `—` |
| NS-10 | dört Critical olmayan `finance.*` adı | SAT-D196, SAT-D197 (EK30; SAT-D191'in finans ara dönemi geri alındı) | Finans Uzmanı oluşturur (dört adın hepsi); Finans Müdürü ve Genel Müdür yalnız onay ve onayın gerektirdiği okuma — bugün bu dört ad onay olmadığı için ikisinde de `—`; Satış/Muhasebe Müdürü `—` (K4.2) |
| NS-12 | `sales.drafts.create` | SAT-D194 | Satış Müdürü + Genel Müdür; Muhasebe Müdürü `—` |

Bu liste `OPEN-*` kapatmaz ve hiçbir izni açmaz; persona tanımı karardır, atama insan kapısıdır.
