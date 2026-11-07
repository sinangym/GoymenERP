# Sinan Kararları — 2026-11-07 Alış EK11 (A2a-1 sipariş fiyatı: Ayarlar para kataloğu dar istisnası, fiyatı kim görür, bedelsiz satır, birim fiyat ondalığı)

Kaynak: Sinan Göymen'in 7 Kasım 2026 tarihli Claude cloud oturumunda (Alış şeridi), A2a-1 (sipariş satırı fiyatı ve
iskonto, plan TEK-01 + API-20 fiyat kısmı + PRS-02, PR #944) için sorulan dört soruya verdiği **doğrudan cevaplar**
(makbuz: https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6704170096). Sorular, bütün seçenekler ve
seçenek açıklamaları aynen aktarılmıştır; "(Önerilen)" işareti Claude'un soruyu sorarken koyduğu işarettir. Claude
yalnız soruları ve seçenekleri hazırlamıştır; seçimi Sinan yapmıştır.

Numaralama: ALS-D66…D68 EK10'dadır. Bu dosya ALS-D69…D72'yi içerir.

## 0. Yetki ve sınır

- `SINAN_KARARLARI_2026-11-02_ALIS.md` §0 (ALS-D45 ile daraltılmış hâliyle), EK8 §0, EK9 §0 (ALS-D57) ve EK10 §0
  geçerlidir. ALS-D69…D72'de seçilen seçenekler doğrudan kullanıcı cevabıdır.
- **Değişmeyen insan kapıları:** production aktivasyonu, secret/deployment, Ayarlar'da gerçek profil aktivasyonu,
  grant/rol/Critical ve `OPEN-*` kapatma insanda kalır (AGENTS.md). ALS-D70 bir izin adının **kime önerildiğini**
  seçer; gerçek kullanıcıya veya production rolüne grant değildir. İzin yalnız SENTETIK (test/browser-suite)
  personalarına verilebilir; gerçek grant insan kapısındadır.
- Bu dosya hiçbir `OPEN-*` kimliğini kapatmaz, grant/rol vermez. Persona kartındaki Alış eşlemesi (§2) bir **öneridir**
  (kartın kendi kuralı: "Bu kart merge edilse bile çalışan sistemde tek bir yetki değişmez").
- Kart değişikliği insan kapısı: Sinan "Onaylıyorum, aynı PR", PR #957 yorumu 6706749065.

## 1. Kararlar

| No | Konu | Soru (aynen) | Seçenekler (aynen) | Seçilen (aynen) | Önceki durum | Bağlı kimlik |
|---|---|---|---|---|---|---|
| ALS-D69 | Ayarlar para kataloğunda Alış sipariş kaydı | **"Alış siparişinde fiyat ve para birimi kullanılabilsin diye Ayarlar'daki para profili listesine Alış sipariş kaydı (purchasing.order.v1) eklensin mi? Şu an yok; bu yüzden fiyatlı sipariş açılamıyor, fiyatsız sipariş normal çalışıyor. (Ayarlar ertelenmişti; ALS-D63'te yalnız tolerans/eşik için dar istisna vardı.)"** | **"Evet, dar istisna (Önerilen)"** — "Ayarlar para kataloğuna yalnız Alış sipariş satırı eklenir (GDC: TRY; GSP/GIN ayrı), Satış'taki gibi. ALS-D63 istisnası genişler; Ayarlar'ın geri kalanı ertelenmiş kalır."; **"Hayır, bekle"** — "Ayarlar modülü başlayana kadar Alış siparişinde fiyat girilemez; siparişler fiyatsız açılır ve 'fiyat bekliyor' görünür." | **"Evet, dar istisna (Önerilen)"** | ALS-D58 Ayarlar'da yeni kaydı erteliyordu; ALS-D63 dar istisnası yalnız tolerans/eşik/fatura toleransı kayıtlarını kapsıyordu; PR #944: `purchasing.order.v1` için onaylı profil yok → fiyatlı sipariş 503 (fail-closed), fiyatsız sipariş etkilenmez | ALS-D58, ALS-D63, ALS-D5, ALS-D18, TEK-01, API-20, A2a-1 (#944), A2a-1b |
| ALS-D70 | Alış fiyatını kim görür | **"Alış siparişinde fiyatları kim görebilsin? (Yeni yetki: purchasing.orders.price.read. Depo/sevkiyat fiyat görmemeli — PRS-02.)"** | **"Satın alma + yönetim + muhasebe (Önerilen)"** — "Satın Alma Uzmanı, Satın Alma Müdürü, Genel Müdür, Admin, Muhasebe ve Muhasebe Müdürü görür. Sevkiyatçı görmez. Satış personaları görmez."; **"Satış Destek de görsün"** — "Yukarıdakilere ek olarak alış siparişi girebilen Satış Destek ve Satış Uzmanı da fiyatı görür."; **"Yalnız satın alma + yönetim"** — "Satın Alma Uzmanı, Satın Alma Müdürü, Genel Müdür ve Admin görür; Muhasebe görmez." | **"Satış Destek de görsün"** (önerilen seçeneğe karşı) | PR #944: `purchasing.orders.price.read` yalnız kayıtlı, kimse taşımaz; yoksa fiyatlar `Withheld` (PRS-02). Kart: Sevkiyatçı "fiyatları göremez" | PRS-01, PRS-02, A2a-1 (#944), ALS-D53, ALS-D54, kart §4 (Sevkiyatçı, Satış Destek), SAT-D141, SAT-D142 |
| ALS-D71 | Bedelsiz (0 TL) satır | **"Alış siparişinde bedelsiz (0 TL) satır olabilsin mi? (Örn. numune veya bedelsiz gelen mal.) Şu an fiyat ya boş kalıyor ya da sıfırdan büyük olmak zorunda."** | **"Hayır, şimdilik yok (Önerilen)"** — "Fiyat ya bilinmiyor (boş, 'fiyat bekliyor') ya da sıfırdan büyük. Bedelsiz mal gerekirse sonra ayrı bir işaretle eklenir."; **"Evet, 0 fiyat girilebilsin"** — "Satırda açıkça 0 fiyat girilebilir; satır tutarı 0 olur. Boş fiyattan ayrı tutulur." | **"Evet, 0 fiyat girilebilsin"** (önerilen seçeneğe karşı) | PR #944 ilk hâli: fiyat boş ya da > 0 | ALS-D5, ALS-D18, TEK-01, A2a-1 (#944) |
| ALS-D72 | Birim fiyat ondalığı | **"Alış siparişinde birim fiyat kaç ondalık girilebilsin? Şu an TL'de 2 ondalık (örn. 41,25 TL/kg), Satış'la aynı. Kg fiyatı bazen 4 ondalıklı olabiliyor (örn. 41,2575 TL/kg); bu durumda satır tutarı yine kuruşa yuvarlanır."** | **"4 ondalık (Önerilen)"** — "Birim fiyat en fazla 4 ondalık girilebilir; satır tutarı bir kez kuruşa (2 ondalık) yuvarlanır. Ayarlar'da para profili tanımına birim fiyat ölçeği eklenir (dar istisna kapsamında)."; **"2 ondalık kalsın"** — "Birim fiyat da tutar gibi kuruş hassasiyetinde (2 ondalık); Satış'la tamamen aynı."; **"Satış ne ise o"** — "Alış, Satış'ın birim fiyat ondalık kuralını izler; Satış değişirse Alış da değişir." | **"4 ondalık (Önerilen)"** | PR #944: birim fiyat ondalığı para profilinin tutar ölçeğiyle sınırlı (TRY'de 2); satır tutarı bir kez, yarım yukarı (SAT-D11), net birim fiyat yuvarlanmaz (SAT-D76) | ALS-D69, SAT-D11, SAT-D76, TEK-01, A2a-1b |

## 2. Etkiler ve yorumlar

Bu bölümdeki **yorum** etiketli maddeler geri alınabilir; karar değildir.

- **ALS-D69 (Ayarlar'da dar istisna genişler):** Ayarlar onaylı para kataloğuna **yalnız** Alış siparişi işlem
  anahtarı `purchasing.order.v1` eklenir (GDC: TRY; GSP/GIN şirket profilleri ayrı), Satış'taki para profili
  satırlarının deseniyle. ALS-D63 istisnası bu satırla genişler; Ayarlar'ın geri kalanı (API-98 listeleri dahil)
  ertelenmiş kalır (ALS-D58). Uygulama **A2a-1b** dilimidir (plan §12): A2a-1b ile fiyatlı alış siparişi açılır;
  `PurchasingPricingGuardTests` "henüz profil yok" sabitlemesi bilerek ters döner. Ayarlar'da gerçek ortamda profil
  aktivasyonu insan kapısıdır.
  - *Yorum:* "GDC: TRY; GSP/GIN ayrı" seçenek açıklamasındaki kısaltmadır; GSP/GIN para birimi bu dosyada seçilmez,
    Satış'taki gibi Ayarlar sahibinin kayıt adımında ayrıca yazılır. Geri alınabilir.
- **ALS-D70 (fiyatı görenler; Sinan önerilene karşı seçti):** `purchasing.orders.price.read` kart için şu
  personalara **önerilir**: **Satın Alma Uzmanı, Satın Alma Müdürü, Genel Müdür, Admin, Muhasebe, Muhasebe Müdürü,
  Satış Destek ve Satış Uzmanı**. **Sevkiyatçı görmez** (PRS-02; kart §4 "fiyatları göremez").
  - Kayıt: Claude'un önerdiği seçenek **"Satın alma + yönetim + muhasebe"** idi; Sinan **"Satış Destek de görsün"**
    seçti. Seçim doğrudan kullanıcı cevabıdır.
  - Persona kartı (`docs/security/PERSONA_GRANT_MATRISI_KARTI_v0.md`) "Satın alma personaları" tablosunda Satış
    Destek ve Satış Uzmanı bu satırda `+` olarak listelenir (öneri, grant değildir). Gerçek kullanıcıya/production
    rolüne grant insan kapısındadır; yalnız SENTETIK personalar test için alabilir.
  - *Yorum:* Seçenek açıklaması "Yukarıdakilere ek olarak … Satış Destek ve Satış Uzmanı" der; **Satış Müdürü**
    sayılmaz → `—` (deny-first; Satış Müdürü alış yetkisi almaz, SAT-D191). **Finans Uzmanı / Finans Müdürü**
    sayılmaz → `—` (kartın finans dışı satır kuralı). Geri alınabilir.
- **ALS-D71 (bedelsiz satır; Sinan önerilene karşı seçti):** Genel satırda açıkça **0 fiyat** girilebilir; satır
  tutarı 0 olur. 0 fiyat **bir fiyattır**: satır "fiyat bekliyor" sayılmaz ve **boş fiyattan ayrı** tutulur
  (ALS-D18'in fiyatsız satırı aynen).
  - Kayıt: Claude'un önerdiği seçenek **"Hayır, şimdilik yok"** idi; Sinan **"Evet, 0 fiyat girilebilsin"** seçti.
  - *Yorum (uygulama ayrıntısı, PR #944):* 0 fiyatlı satırda iskonto yalnız yüzde ya da 0 tutar olabilir
    (tutar iskontosu birim fiyatı aşamaz; fail-closed). Ayrı bir "bedelsiz" işareti yoktur. Geri alınabilir.
- **ALS-D72 (4 ondalık):** Alış birim fiyatı en fazla **4 ondalık** girilebilir; satır tutarı (sipariş miktarı × net
  birim fiyat) **bir kez** para profilinin tutar ölçeğine (TRY'de kuruş, 2 ondalık) yuvarlanır. Birim fiyat ölçeği
  Ayarlar para profili tanımına alan olarak eklenir (ALS-D69 dar istisnası kapsamında, A2a-1b); 4 kodda sabit yazılmaz.
  - *Yorum (SAT-D11/D76 benzetmesi, EK8 §0):* tek yuvarlama **yarım yukarı** yapılır (SAT-D11) ve **net birim fiyat
    yuvarlanmaz** (SAT-D76). Bunlar Satış kurallarının Alış'a benzetmesidir; seçenek metni yalnız "bir kez kuruşa"
    der. Geri alınabilir uygulama yorumudur, karar değildir.
  - *Yorum:* Satış'ın birim fiyat ondalığı bu kararla değişmez ("Satış ne ise o" seçilmedi); iki modül ayrı ölçek
    taşıyabilir. Ortak `CommercialLinePriceCalculator` ölçeği parametre olarak alır. Geri alınabilir.
- **Kodun bugünkü durumu (bu dosyanın parçası değil):**
  - PR #944 (merge `6c58f0c8`) — `purchasing.orders.price.read` izni yalnız **kayıtlı** (manifest +
    `ErpPermissions`; şirket geneli, Critical değil); kimse taşımaz, yoksa fiyatlar `Withheld` (ALS-D70'in teknik
    tarafı; sahipler README ve kod yorumlarında kayıtlı). **0 fiyat** kabul edilir: domain `purchasing_price_negative`
    ve migration `AddPurchaseOrderLinePrice` içinde `CHECK [UnitPrice] >= 0` (`CK_PurchaseOrderLines_Price`) — ALS-D71
    kodda karşılanmıştır.
  - **Bekleyen: A2a-1b** (Ayarlar para kataloğu satırı `purchasing.order.v1` + birim fiyat ölçeği alanı) — ALS-D69 ve
    ALS-D72 bu dilimle uygulanır. #944 4 ondalığı sabit kodlamaz; birim fiyat ondalığı bugün para profilinin tutar
    ölçeğiyle sınırlıdır ve profil olmadığı için fiyatlı sipariş bugün 503 ile fail-closed'dır.

## 3. Değişmeyenler

- ALS-D5 (elle, iskontolu fiyat), ALS-D18 (fiyatsız sipariş onaylanır, "fiyat bekliyor"), ALS-D51/D58/D60 (fiyat birimi;
  çevrim portu ertelendi), ALS-D53/D54 (personalar, talep açanlar), ALS-D63 (tolerans/eşik istisnası; yalnız ALS-D69
  ile para kataloğu satırı eklenir), ALS-D66…D68 aynen. SAT-D11/SAT-D76 (Satış yuvarlama kuralları) aynen; Alış'a uygulanmaları §2'deki geri alınabilir yorumdur.
- API-98 Ayarlar listelerinin ALS-D63 istisnasına girip girmediği hâlâ **NEEDS-SINAN**'dır; ALS-D69 yalnız para
  kataloğundaki `purchasing.order.v1` satırını kapsar.
- Grant/rol ataması, Ayarlar'da gerçek profil aktivasyonu, production aktivasyonu, secret/deployment ve `OPEN-*`
  kapatma insan kapısıdır.
