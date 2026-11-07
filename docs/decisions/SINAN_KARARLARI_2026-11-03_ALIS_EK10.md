# Sinan Kararları — 2026-11-03 Alış EK10 (No.161 kalan iptali: yetki, tek adım, talep etkisi; ERP-000F ve Work şerit değişikliği)

Kaynak: Sinan Göymen'in 3 Kasım 2026 tarihli Claude cloud oturumunda (Alış şeridi), A1-3 (No.161 kalan iptali,
plan API-10, PR #932) için açık kalan üç soruya verdiği **doğrudan cevaplar** (NEEDS-SINAN sorusu:
https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6597493856; makbuz:
https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6597537540). Sorular, bütün seçenekler ve seçenek
açıklamaları aynen aktarılmıştır; "(Önerilen)" işareti Claude'un soruyu sorarken koyduğu işarettir. Claude yalnız
soruları ve seçenekleri hazırlamıştır; seçimi Sinan yapmıştır. Ayrıca koordinatör oturumunun aynı gün Sinan adına
aktardığı süreç değişikliği (ERP-000F ve Work'ün Kalan Modüller şeridine taşınması) ve daha önce uygulanan
aktarımlar **bağlam** olarak kaydedilmiştir (§0.1); bunlar bu dosyada yeni ALS-D numarası almaz.

Numaralama: ALS-D57…D65 EK9'dadır. Bu dosya ALS-D66…D68'i içerir.

## 0. Yetki ve sınır

- `SINAN_KARARLARI_2026-11-02_ALIS.md` §0 (ALS-D45 ile daraltılmış hâliyle), EK8 §0 ve EK9 §0 (ALS-D57) geçerlidir.
  ALS-D66…D68'de seçilen seçenekler doğrudan kullanıcı cevabıdır.
- **Değişmeyen insan kapıları:** production aktivasyonu, secret/deployment, grant/rol/Critical ve `OPEN-*` kapatma
  insanda kalır (AGENTS.md). ALS-D66 bir izin adının **kime önerildiğini** seçer; gerçek kullanıcıya veya production
  rolüne grant değildir.
- Bu dosya hiçbir `OPEN-*` kimliğini kapatmaz, grant/rol vermez, persona kartını değiştirmez.

### 0.1 Koordinatör aktarımları (bağlam; ALS-D57 kapsamında süreç, yeni iş politikası değildir)

1. **ERP-000F ve Work şerit değişikliği (aynen aktarım, 3 Kasım):** "Sinan kararı (3 Kasım): ERP-000F outbox
   dağıtıcısı ve Work modülü Kalan Modüller şeridine (session_015Usc2pruQTAve57Km32zGP) taşındı. ALS-D44'teki
   dağıtıcı görevi artık senin değil. Planından çıkar, bir sonraki Alış belge PR'ında not düş. Alış tarafında yalnız
   kendi outbox kayıtlarını yazmaya devam edersin." (Kaynak: https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6598020192;
   Kalan Modüller planı #934 K-3.)
   - Bu bir şerit/sahiplik (yürütme biçimi) değişikliğidir; ALS-D57 kapsamında uygulanır. Para, stok, formül, yetki
     veya eşik politikası seçmez.
   - **Açık (karar değil):** Alış için Work onay kutusu adaptörünün (MOD-10, `INativeApprovalReadAdapterV1`) sahibi
     netleşmedi: Alış mı yazar, Kalan Modüller mi? Kalan Modüller planı #934 §2/§3 B-25 "adaptörü tüketici şerit
     yazar; Work'te yeni kaynak türü gerekirse Kalan Modüller" der; bu, Kalan Modüller şeridiyle teyit edilecek.
     #934 §2.8 ve B-25 "adaptörü tüketici şerit yazar" yönüne, yani Alış'a eğilir; teyit edilene kadar soru açık
     kalır. Teyide kadar MOD-10 planda Alış'ta (A6) kalır, Work runtime'ının açılması (B-24) Kalan Modüller'dedir.
   - **Bayat kayıt (Finans şeridine not; bu PR Finans dosyalarını değiştirmez):** `docs/delivery/FINANS_HAZIRLIK_PLANI_v1.md`
     satır 42 ve 336 (MOD-11) ERP-000F sahibini hâlâ "Alış şeridi (ALS-D44)" diye yazar; Finans şeridinin bu
     satırları Kalan Modüller'e (#934 B-24) çevirmesi gerekir.
2. **"Modül bağlantıları" ve "AI araçları" satırları (uygulandı):** her Alış PR açıklamasında bir **"Modül
   bağlantıları"** satırı (plan §14 kalıcı kural) ve bir **"AI araçları"** satırı bulunur (kaynak: AI Merkezi
   şeridinin koordinatör aktarımı, Sinan 3 Kasım; `SINAN_KARARLARI_2026-11-03_AI.md` AI-D4). AI-D4 gereği hassas veri
   dış AI'ya **yalnız maskeli (takma ad)** ve **yalnız yöneticinin Ayarlar'dan açtığı veri türlerinde** gider.
3. **Kalan Modüller planı #934 §3 (bilgi):** Alış'ın ertelenen ihtiyaçları orada izlenir: **B-07** birim çevrimi +
   varsayılan fiyat birimi (Ürün), **B-16** fason rolü + ödeme vadesi (Cari), **B-18** kalite bloğu + kabul v2
   (Stok/Ürün), **B-19** tedarikçiye iade çıkışı (Stok), **B-20** ST-08 "tedarikçide hazır" (Stok), **B-21**
   L-09/L-10 kabul tek kaydı, kantar fişi bağı, iade sevki (Lojistik); ayrıca **B-24** Work runtime + ERP-000F ve
   **B-25** Work kaynak türleri.
4. **Muhasebe 320 fişi (öneri, karar değil):** Kalan Modüller planı #934 §6.1, ERP-000F dağıtıcısı yokken 120/320
   fişi için Muhasebe'nin `IAccountingSourceDocumentAdmissionPortV1` kabul portunu **önerir** ve bunu SAT-D208'in
   Finans için tarif ettiği ara yolun aynası olarak sunar. Bizim okumamızla desen olarak ALS-D64 ile aynıdır (kaynak
   kendi işleminde kaydeder, sonra aynı anahtarla idempotent çağrı). Alış tarafında
   kabul edilmiş bir sözleşme değildir; A5'te Muhasebe ile yazılır. ALS-D59 (320 fişi aynı kaynak olaydan
   Muhasebe'de; Alış fiş yazmaz) aynen.

## 1. Kararlar

| No | Konu | Soru (aynen) | Seçenekler (aynen) | Seçilen (aynen) | Önceki durum | Bağlı kimlik |
|---|---|---|---|---|---|---|
| ALS-D66 | Kalanı kim iptal eder | **"Onaylı bir alış siparişinde kabul edilmeyen kalanı (No.161) kim iptal edebilsin?"** | **"Onaycılar (Önerilen)"** — "Satın Alma Müdürü, Genel Müdür ve Admin iptal eder (ALS-D53 onaycıları). Satın Alma Uzmanı iptal edemez."; **"Uzman da"** — "Siparişi hazırlayan Satın Alma Uzmanı da gerekçeyle kalanı iptal edebilir." | **"Onaycılar (Önerilen)"** | No.161 dosyası rol, action, permission veya grant tanımlamaz; izin adı (`purchasing.orders.cancel`, yeni) yalnız plan API-10'dan gelir ve kimin taşıyacağı yazılı değildi; PR #932'de "kim alacağı NEEDS-SINAN", ad yalnız kayıtlı | No.161 §2, API-10, A1-3, PR #932, ALS-D3, ALS-D53, EK8 §3, PRS-01 |
| ALS-D67 | İkinci onay | **"Kalan iptali ikinci bir onay istesin mi?"** | **"Hayır, tek adım (Önerilen)"** — "İptali yetkili kişi gerekçe yazarak yapar; denetim kaydı tutulur, ayrıca onay gerekmez. Bu PR'da şu an böyle."; **"Evet, iki kişi"** — "Bir kişi iptali önerir, başka bir yetkili onaylar; hazırlayan kendi iptalini onaylayamaz." | **"Hayır, tek adım (Önerilen)"** | PR #932: ikinci onay adımı ve hazırlayan ≠ onaylayan kuralı yok (fail-closed, NEEDS-SINAN) | No.161 §2, API-10, PR #932; SAT-D4 (Satış emsali) |
| ALS-D68 | İptal edilen kalan ve talep ihtiyacı | **"Kalan iptal edilince, o kalan için açılmış satın alma talebi yeniden başka tedarikçiden sipariş açılabilir hale gelsin mi?"** | **"Evet, ihtiyaç geri açılsın (Önerilen)"** — "İptal edilen miktar talepten düşer; aynı talepten başka tedarikçiye yeni sipariş açılabilir. Yeni talep gerekmez."; **"Hayır, yeni talep gerekir"** — "İptal edilen kalan talepte kullanılmış sayılır; başka tedarikçiden almak için yeni bir talep açılır. Bu PR'da şu an böyle." | **"Hayır, yeni talep gerekir"** (önerilen seçeneğe karşı) | PR #932: iptal edilen sipariş talep satırının Σ-payında tam sipariş miktarıyla sayılmaya devam eder (fail-closed, NEEDS-SINAN) | No.161 §1/§2, INV-006, ALS-D2, ALS-D16, API-02, A1-3, PR #932 |

## 2. Etkiler ve yorumlar

Bu bölümdeki **yorum** etiketli maddeler geri alınabilir; karar değildir.

- **ALS-D66 (yetki):** Onaylı siparişin kabul edilmemiş kalanını **Satın Alma Müdürü, Genel Müdür ve Admin** iptal
  eder (ALS-D53'teki sipariş onaycıları). **Satın Alma Uzmanı iptal edemez.** İzin adı `purchasing.orders.cancel`
  (plan API-10); manifestte yalnız **kayıtlıdır**. Persona kartı için öneri: Satın Alma Müdürü `+`, Genel Müdür `+`,
  Admin `+`, Satın Alma Uzmanı `—`. Gerçek kullanıcıya/production rolüne grant ve kartın değişmesi **insan
  kapısındadır** (PRS-01; bu dosya kartı değiştirmez).
  - *Yorum:* Seçenek açıklaması yalnız bu dört personayı sayar. Satış Destek, Satış Uzmanı, Satış Müdürü,
    Sevkiyatçı ve Muhasebe/Muhasebe Müdürü sütunları, `purchasing.orders.approve` satırındaki gibi `—` önerilir
    (EK8 §3: onaycı listesi dışındakiler onaylamaz; iptal de onaycılara bağlandı). Öneri EK8 §3'ü izler: kartta
    `purchasing.orders.approve` satırında Muhasebe `?` iken EK8 §3 Muhasebe'yi `—` önerir, çünkü ALS-D53 onaycıları adıyla
    sayar; aynı gerekçe burada da geçerlidir. Kartın "Müdür (eski)" sütunu (`purchasing.orders.approve` satırında `+`)
    SAT-D187 ile yeni müdür personalarına eşlenir; alış tarafında bu Satın Alma Müdürü'dür (ALS-D53), bu yüzden eski
    sütun ayrı bir iptal yetkisi doğurmaz. Geri alınabilir.
- **ALS-D67 (tek adım):** İptal **tek adımlıdır**: yetkili kişi gerekçe yazarak iptal eder; denetim kaydı (audit)
  tutulur; ikinci onay ve hazırlayan ≠ onaylayan kuralı **yoktur**. PR #932'nin mevcut davranışıdır; etki önizlemesi
  (`cancel-impact`) ve gerekçe zorunluluğu aynen (No.161 §2, API-10).
- **ALS-D68 (talep ihtiyacı geri açılmaz; Sinan önerilene karşı seçti):** İptal edilen kalan talepte **kullanılmış
  sayılır**; talep satırının Σ-payı iptalle azalmaz. İptal edilen miktar için aynı talepten yeni sipariş açılamaz
  (talep satırında siparişe bağlanmamış pay varsa o pay için açılabilir); iptal edilen miktarı başka tedarikçiden
  almak için yeni talep açılır (ALS-D54 talep açanlarla). Σ-pay Onaylı ve İptal edilmiş siparişleri sayar. PR #932'nin mevcut davranışıdır
  (iptal edilen sipariş talep satırının Σ-payında tam sipariş miktarıyla sayılır); "fail-closed, NEEDS-SINAN" notu
  karar olarak kapanır.
  - Kayıt: Claude'un önerdiği seçenek **"Evet, ihtiyaç geri açılsın"** idi; Sinan **"Hayır, yeni talep gerekir"**
    seçti. Seçim doğrudan kullanıcı cevabıdır.
  - *Yorum:* Sipariş tarafında `Alış Kalan = Onaylı − İptal − KabulNet − KesinRed/İade` (INV-006) aynen geçerlidir:
    iptal edilen kalan gelecek arzdan ve sipariş kalanından düşer; yalnız talep satırındaki pay geri açılmaz.
    No.161 §1 (bağlı Onaylı sipariş varken talep reddedilmez/geri çekilmez) yalnız Onaylı siparişi sayar; iptal
    edilmiş sipariş bu engeli kaldırır (PR #932). Geri alınabilir.
- **Kod takibi (bu dosyanın parçası değil):** PR #932'deki ve kaynak kod yorumlarındaki (ör. `ErpPermissions.cs`,
  Purchasing domain/servis yorumları, `DURUM_MAKINELERI.md` A1-3 satırı) No.161 `NEEDS-SINAN` notları sonraki bir kod
  PR'ında ALS-D66…D68'e çevrilir. Bu belge PR'ı kaynak kodu değiştirmez.
- **ERP-000F ve Work (§0.1 madde 1; süreç, ALS-D57):** ALS-D44'teki dağıtıcı görevi Alış'tan çıkar; ERP-000F
  dağıtıcısı ve Work modülü (runtime'ın açılması dahil, #934 B-24) **Kalan Modüller** şeridindedir. Alış **yalnız
  kendi outbox kayıtlarını** yazar (A5-0, MOD-12). ALS-D62/D64 (dağıtıcı gelene kadar Finans kabul portu ayrı adımda
  doğrudan çağrılır; dağıtıcı gelince aynı içerik olay olarak) aynen; dağıtıcının gelişi artık Kalan Modüller'e
  bağlıdır. MOD-10 Work adaptörünün sahibi açıktır (§0.1).

## 3. Değişmeyenler

- No.161 §1/§2 (talep reddi, kalanın **tamamının** iptali; kısmi kabul olsa da), INV-006, API-10 izin adı aynen;
  ALS-D66…D68 yalnız kimin iptal edeceğini, ikinci onay olmadığını ve talep payının geri açılmadığını seçer.
- ALS-D3, ALS-D53 (onaycılar), ALS-D54 (talep açanlar), ALS-D59, ALS-D62, ALS-D64 aynen. ALS-D44'ün Alış outbox'ı
  kısmı aynen; yalnız dağıtıcı görevi Kalan Modüller'e geçti (§0.1, süreç).
- Plan §12, API-01b ve TEK-09 bu dosyayla değişmez. Grant/rol ataması, production aktivasyonu, secret/deployment ve
  `OPEN-*` kapatma insan kapısıdır.
