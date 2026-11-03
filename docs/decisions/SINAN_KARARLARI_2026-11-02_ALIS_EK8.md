# Sinan Kararları — 2026-11-02 Alış EK8 (satın alma personaları, talep açanlar, tolerans birimi, kur düzeltme)

Kaynak: Sinan Göymen'in 2 Kasım 2026 tarihli Claude cloud oturumunda ("Proje modülleri alış analizi"), Alış planında
açık kalan persona sorusuna (plan §7 PRS-01, persona kartındaki Satın Alma Müdürü yer tutucusu), N-38'e (kabul
toleransının birimi) ve ALS-D6'nın rol listesine verdiği **doğrudan cevaplar**. Sorular, bütün seçenekler ve seçenek
açıklamaları aynen aktarılmıştır; "(Önerilen)" işareti Claude'un soruyu sorarken koyduğu işarettir. Claude yalnız
soruları ve seçenekleri hazırlamıştır; seçimi Sinan yapmıştır.

Numaralama: ALS-D47 (talebin geri çekilmesi: oluşturan veya gönderen), paralel PR #907 ile eklenecek EK6'dadır;
ALS-D48…D52 EK7'dedir. Bu dosya ALS-D53'ten başlar; EK6 bu dosyanın parçası değildir.

## 0. Yetki ve sınır

- `SINAN_KARARLARI_2026-11-02_ALIS.md` §0 (ALS-D45 ile daraltılmış hâliyle) geçerlidir. Seçilen seçenekler doğrudan
  kullanıcı cevabıdır.
- **Persona eşlemesi grant değildir.** ALS-D53 ve ALS-D54 persona **tanımıdır**: hiçbir gerçek kullanıcıya, üretim
  rolüne veya seed rolüne bu dosyayla bir şey verilmez. Grant ve rol ataması insan kapısında kalır (AGENTS.md
  "Değişmeyen insan kapıları"). Kayıt düzeyindeki kural (hazırlayan/isteyen kendi kaydını onaylayamaz — P6, ALS-D2,
  ALS-D3, SAT-D37) personaya değil kayda bağlıdır ve aynen geçerlidir.
- **Bu dosya persona kartını değiştirmez.** `docs/security/PERSONA_GRANT_MATRISI_KARTI_v0.md` ortak dosyadır; Satın
  Alma sütunlarının eklenmesi plan kalemi **PRS-01**'dir ve kendi review'larıyla ayrı bir PR'da yapılır. Kart için
  hazırlanan eşleme aşağıda §3'te "Kart için not" olarak durur; karar değildir, PRS-01'in girdisidir.
- Bu dosya hiçbir `OPEN-*` kimliğini kapatmaz; tolerans yüzdeleri kod sabiti olmaz, Ayarlar politika kaydında kalır
  (`DEC-SET-001`, ALS-D43). İnsan kapıları (grant/rol, production aktivasyonu, deployment, secret, `OPEN-*` kapatma)
  değişmez.
- **Sürekli talimat (Sinan, 2026-11-02, aynen):** **"Bana soruları sorabilirsin, satış tarafı ile benzer konularda
  verdiğim kararları ordan da alabilirsin"**. Uygulama biçimi: Satış'a benzeyen bir Alış konusunda Satış kararı **benzetmeyle** uygulanabilir;
  ancak her böyle uygulama **geri alınabilir yorum** olarak etiketlenir ve dayandığı SAT-D kaynağını anar. Benzetme
  yeni politika değildir; Alış karar numarası almaz; Sinan aksini söylerse o yorum düşer. Para/stok/formül/yetki
  politikasında benzetme yine fail-closed uygulanır ve grant açmaz.

## 1. Kararlar

| No | Konu | Soru (aynen) | Seçenekler (aynen) | Seçilen (aynen) | Önceki durum | Bağlı kimlik |
|---|---|---|---|---|---|---|
| ALS-D53 | Satın alma personaları | **"Satış'taki gibi Alış için de iki persona tanımlayalım mı? Satın Alma Uzmanı talep ve sipariş hazırlar, onaylayamaz; Satın Alma Müdürü talebi ve siparişi onaylar. Satış'taki kurala göre Genel Müdür ve Admin de onaylayabilir; hazırlayan kendi talebini onaylayamaz."** | **"Evet, ikisi (Önerilen)"** — "Uzman hazırlar ve kendi talebini geri çeker. Müdür, Genel Müdür ve Admin onaylar/reddeder. Satış'taki Müdür ve Uzman ayrımının aynısı."; **"Yalnız Müdür"** — "Ayrı bir Satın Alma Uzmanı yok. Talebi ve siparişi Satış Destek gibi mevcut personalar hazırlar, Satın Alma Müdürü onaylar."; **"Şimdilik yok"** — "Satın alma personası tanımlanmaz. Ara dönemde onayı Genel Müdür ve Admin yapar, persona kartındaki gibi." | **"Evet, ikisi (Önerilen)"** | Persona kartında Satın Alma Müdürü yer tutucu (SAT-D187, SAT-D191); hücreleri ara dönemde Genel Müdür + Admin'de; ALS-D2/D3 "Satın alma müdürü onaylar", "Satın alma uzmanı hazırlar" diyordu ama persona tanımlı değildi | ALS-D2, ALS-D3, ALS-D46, ALS-D47 (PR #907), SAT-D187, SAT-D188, SAT-D190, SAT-D191, plan PRS-01 |
| ALS-D54 | Satın alma talebini kimler açar | **"Satın alma TALEBİNİ (malzeme ihtiyacı bildirimi) kimler açabilsin? Talebi açan kişi kendi talebini geri de çekebilir (ALS-D47)."** | **"Satın alma + Satış (Önerilen)"** — "Satın Alma Uzmanı, Satın Alma Müdürü, Satış Destek ve Satış Uzmanı. Persona kartı Satış Destek'in alış siparişi girebildiğini zaten söylüyor."; **"Yalnız satın alma"** — "Talebi yalnız Satın Alma Uzmanı ve Müdürü açar; diğer bölümler ihtiyacı sözlü ya da başka yoldan iletir."; **"İhtiyacı olan her bölüm"** — "Satın alma ve satışın yanında Üretim, Stok ve Sevkiyat da talep açar. Üretim ve stok taleplerinin otomatik gelmesi zaten planda (A4)." | **"Satın alma + Satış (Önerilen)"** | Talep bugün `purchasing.orders.create` ile açılıyor (ASSUMPTION-PUR4-1: talep ve sipariş aynı adla); talep açanlar ayrıca seçilmemişti | ALS-D46, ALS-D47 (PR #907), ALS-D16 (Üretim talebi, A4), persona kartı §4 Satış Destek, SAT-D141, plan API-01/01b/03 |
| ALS-D55 | Mal kabul toleransının ölçüldüğü miktar | **"Mal kabulde tolerans (%1 uyarı / %2 onay / %3 blok) hangi miktar üzerinden ölçülsün? (Kg, adet ve metre aynı satırda hesaplanabildiği için soruyorum.)"** | **"Satırın giriş birimi (Önerilen)"** — "Sipariş satırı hangi birimde girildiyse (kg, adet veya metre) karşılaştırma o birimde yapılır. Kantar kg'ı ile irsaliye kg'ı arasındaki fark ayrıca OPEN-003 kuralıyla kontrol edilir."; **"Her zaman kg"** — "Adet veya metre girilse bile tolerans çevrilmiş kg üzerinden ölçülür. Kantar sonucu belirleyici olur."; **"Fiyat birimi"** — "Fiyat hangi miktar üzerinden çarpılıyorsa tolerans da o birimde ölçülür; faturaya giden miktarla aynı olur." | **"Satırın giriş birimi (Önerilen)"** | ALS-D43 her birimde aynı yüzdeyi veriyordu; hangi miktar üzerinden ölçüleceği açıktı (EK7 §2 son madde, plan **N-38**) | N-38 (kapanır), ALS-D43, ALS-D51, ALS-D52, ALS-D10/D12, No 40/41/62/63, OPEN-003, plan API-95, API-40, API-11, A3 |
| ALS-D56 | Alış faturası kurunu kim düzeltir | **"Alış faturasının kurunu kim düzeltebilsin? Satış'ta Muhasebe, Muhasebe Müdürü, Genel Müdür ve Admin düzeltiyor (SAT-D188); Satış Müdürü düzeltemiyor."** | **"Satış'la aynı (Önerilen)"** — "Muhasebe, Muhasebe Müdürü, Genel Müdür ve Admin gerekçeyle düzeltir; Satın Alma Müdürü düzeltemez."; **"+ Satın Alma Müdürü"** — "Yukarıdakilere ek olarak Satın Alma Müdürü de alış faturasının kurunu düzeltebilir." | **"Satış'la aynı (Önerilen)"** | ALS-D6 "Satış'taki gibi Muhasebe, Müdür veya Admin gerekçeyle düzeltir (SAT-D169)" diyordu; "Müdür" EK28 ile bölündü ve persona kartı güncellemeyi Alış şeridine bırakmıştı | ALS-D6 (rol listesi güncellenir), ALS-D14, ALS-D15, SAT-D169, SAT-D188 (soru 8), SAT-D190 |

## 2. Etkiler ve yorumlar

Bu bölümdeki **yorum** etiketli maddeler geri alınabilir; karar değildir.

- **ALS-D53:** Satın Alma Uzmanı talep ve sipariş hazırlar, kendi talebini geri çeker (ALS-D46/D47), **onaylamaz**.
  Satın Alma Müdürü talebi ve siparişi onaylar/reddeder. Genel Müdür ve Admin de onaylar (SAT-D188 soru 9, SAT-D190 —
  seçilen seçenek açıklamasında aynen). Persona kartının "Satın Alma … Müdürü (yer tutucu)" satırı Satın Alma
  Müdürü için bu kararla dolar; Satın Alma Uzmanı yeni personadır. Bir kişiye birden çok persona verilebilir
  (SAT-D187); hazırlayan ≠ onaylayan kayıt düzeyinde aynen.
  - *Yorum (SAT-D191 benzetmesi):* Satın Alma Müdürü kendi modülü dışındaki okumayı almaz; yedi temel izni alır
    (SAT-D191 temel). Satın Alma Uzmanı için de aynısı (Finans Uzmanı emsali, SAT-D196). Geri alınabilir.
  - *Yorum (SAT-D187/D191 ara dönem):* Kartta "SAT-D187 ara dönem: Satın Alma Müdürü" işaretli hücreler
    (`purchasing.orders.read`, `purchasing.orders.create`, `purchasing.orders.approve`) PRS-01'de Satın Alma
    Müdürü'ne geçer; Genel Müdür + Admin `+` kalır (SAT-D188 soru 9).
- **ALS-D54:** Talebi Satın Alma Uzmanı, Satın Alma Müdürü, Satış Destek ve Satış Uzmanı açar. Üretim, Stok ve
  Sevkiyat elle talep açmaz ("İhtiyacı olan her bölüm" seçilmedi); Üretim'in talebi A4'te port üzerinden gelir
  (ALS-D16, onay bekler). Talebi açan, onu geri çekebilir (soru metni; ALS-D47).
  - Satış Destek ve Satış Uzmanı bugün kartta `purchasing.orders.create` `+` taşır (persona kartı §4, SAT-D141
    kopyası). ALS-D54 **talep** açmayı söyler; Satış Destek'in alış **siparişi** girmesi kartın §4 tanımından gelir
    ve bu kararla değişmez.
  - *Yorum:* Talep açma bugün `purchasing.orders.create` ile ölçülür (ASSUMPTION-PUR4-1). Talep açmayı ayrı bir ada
    (öneri: `purchasing.requests.create`, **ad bekliyor**) ayırmak ayrı bir teknik dilimdir; ayrılana kadar talep
    açan dört persona sipariş girme adını da taşır. Ayrılınca Satış Destek/Uzmanı'nın sipariş girmesi kart §4'e
    göre korunur.
  - *Yorum (SAT-D194 benzetmesi):* Satış'ta Satış Müdürü ve Genel Müdür taslak açar (SAT-D194). Genel Müdür'ün
    satın alma talebi açması **seçenekte sayılmadı**; kartta `?` kalır (Admin "tüm ERP işlevleri" tanımıyla `+`).
    Sinan'a sorulmadıkça açılmaz.
- **ALS-D55 (N-38 kapanır):** Kabul toleransı (ALS-D43 bantları; No 40/41/62/63 onaycıları) **sipariş satırının giriş
  biriminde** (kg, adet veya metre) ölçülür: kabul miktarı o birimde sipariş miktarıyla karşılaştırılır. Fiyat
  birimi (ALS-D51) ve çevrilmiş kg toleransı belirlemez. Kantar kg'ı ile irsaliye kg'ı arasındaki fark **ayrıca**
  OPEN-003 kuralıyla kontrol edilir; iki kontrol birbirinin yerine geçmez.
  - *Yorum:* Giriş birimi adet veya metre olan satırda kantar kg'ı tolerans bandını değiştirmez; kantar–irsaliye
    farkı kendi kontrolünde görünür. Giriş birimine çevrim gerekirse (ör. kabul kg ile kaydedildi, satır adetle
    girildi) çevrim ortak calculator'da ürün kartı katsayısıyla yapılır (ALS-D51); katsayı yoksa tolerans
    "hesaplanamıyor" olur ve satır kendiliğinden bant içi sayılmaz (fail-closed; tahmini katsayı yok); izlenecek
    yol A3'te mevcut onay kapılarıyla (No 40/41/62/63, ALS-D10/D12) yazılır, yeni bir onay kuralı seçilmez.
- **ALS-D56 (ALS-D6 güncellenir):** ALS-D6'daki "Satış'taki gibi Muhasebe, Müdür veya Admin gerekçeyle düzeltir"
  ifadesinin rol listesi **Muhasebe, Muhasebe Müdürü, Genel Müdür ve Admin** olur (Satış'taki SAT-D169 + SAT-D188
  soru 8 ile aynı). **Satın Alma Müdürü ve Satın Alma Uzmanı alış faturasının kurunu düzeltemez**; Satış Müdürü de
  düzeltemez (SAT-D188). Kur kuralının kendisi (kabul gününden önceki iş günü TCMB kuru, ALS-D14 kontrol kuru, ALS-D15
  kur farkı onayı) değişmez. Düzeltme gerekçeli ve audit'lidir.
  - *Yorum:* Düzeltme adı Satış'la ortak `settings.exchange-rates.correct` (kartta "alıştaki karşılığı ALS-D6"; ad
    #906'da) olabilir veya alışa özgü ayrı bir ad açılabilir; bu teknik seçimdir, rol listesi her iki durumda
    ALS-D56'dır. Ad netleşene kadar alışta kur düzeltmesi açılmaz.

## 3. Kart için not (PRS-01 girdisi; grant değildir, kartı bu dosya değiştirmez)

`+` = önerilir · `—` = verilmez · `?` = kararlarda açık değil (hiçbir şey açmaz). "Ad bekliyor" = izin adı henüz
manifest'te yok.

| İzin adı | Satın Alma Uzmanı | Satın Alma Müdürü | Satış Destek | Satış Uzmanı | Genel Müdür | Admin | Muhasebe / Muhasebe Müdürü | Dayanak |
|---|---|---|---|---|---|---|---|---|
| `purchasing.requests.create` (ad bekliyor; bugün `purchasing.orders.create` ile ölçülür) | + | + | + | + | ? *(yorum, SAT-D194 benzetmesi; seçenekte yok)* | + *(kart "tüm ERP işlevleri", kart Admin satırı)* | — / — *(ALS-D54: seçilen seçenek talep açanları sayar, Muhasebe yok)* | ALS-D54 |
| `purchasing.requests.withdraw` (PR #907 ile ekleniyor) | + | + | + | + | ? *(talep açarsa; yukarıdaki satıra bağlı)* | + *(kart "tüm ERP işlevleri", kart Admin satırı)* | — / — *(ALS-D54; talep açmayan geri çekemez)* | ALS-D54 soru metni, ALS-D47 (PR #907); kayıt düzeyinde yalnız oluşturan/gönderen |
| `purchasing.requests.approve` | — | + | — | — | + | + | — / — *(ALS-D53: seçilen seçenek onaylayanları sayar: Müdür, Genel Müdür, Admin)* | ALS-D2, ALS-D53; SAT-D188 soru 9, SAT-D190 |
| `purchasing.orders.create` | + | + *(yorum: SAT-D191 ara dönem hücresinin geçişi; SAT-D194 benzetmesi)* | + *(kart §4)* | + *(kart, SAT-D141)* | + *(kartta ara dönem; SAT-D188 soru 9)* | + | kartta olduğu gibi (`?` / `—`) — bu turdaki hiçbir cevap kapsamıyor | ALS-D3, ALS-D53, kart §4 |
| `purchasing.orders.read` | + *(yorum: hazırlamanın gerektirdiği okuma)* | + *(yorum: SAT-D191 ara dönem hücresinin geçişi)* | + *(kart)* | + *(kart)* | + | + | kartta olduğu gibi (`?` / `—`) | ALS-D53, kart |
| `purchasing.orders.approve` | — | + | — | — | + | + | — / — *(ALS-D53: seçilen seçenek onaylayanları sayar)* | ALS-D3, ALS-D53; SAT-D187/D188 |
| Alış faturası kur düzeltmesi (`settings.exchange-rates.correct` veya alışa özgü ad, ad bekliyor) | — | — | — | — | + | + | + / + | ALS-D6, ALS-D56; SAT-D169, SAT-D188 |
| `purchasing.goods-receipts.record` | ? | ? | kartta olduğu gibi | kartta olduğu gibi | kartta olduğu gibi | + | kartta olduğu gibi | Bu turda sorulmadı (Depo kabul, PRS-02) |

Muhasebe sütununda kartta `?`, Muhasebe Müdürü sütununda `—` (SAT-D191) olan hücreler, bu turdaki bir cevap onları
açıkça dışarıda bırakmadıkça kartta olduğu gibi kalır; `—` yazılan her Muhasebe hücresi dayandığı kararı anar.

**Satış Müdürü ve Sevkiyatçı** alış yazma ve onay hücrelerinde (`purchasing.orders.create`, `.approve`, talep açma,
geri çekme ve onay) `—` kalır (kart `purchasing.*` satırları, SAT-D191). ALS-D54 seçeneğindeki "Satış", seçenek
açıklamasına göre yalnız **Satış Destek ve Satış Uzmanı**'dır; Satış Müdürü'nü kapsamaz.

**Genel Müdür `?` (talep açma) ne zaman etkili olur:** bu hücre yalnız talep açma `purchasing.requests.create` olarak
`purchasing.orders.create`'ten ayrıldığında (ASSUMPTION-PUR4-1 kaldırıldığında) bir şey ifade eder. Ayrılana kadar
Genel Müdür ve Admin, kartta taşıdıkları `purchasing.orders.create` ile fiilen talep açabilir.

Satın Alma Uzmanı ve Müdürü'nün Alış dışı hücreleri: yalnız SAT-D191 temel okumaları (yorum, yukarıda); öteki her
hücre `—`. Critical adlar her sütunda `—` (insan kapısı).

## 4. Değişmeyenler

- ALS-D2 (talep onayı müdürde), ALS-D3 (tek onay, eşik kapalı), ALS-D46/D47 (geri çekme) aynen; ALS-D53 yalnız
  personaları adlandırır.
- ALS-D43 (tolerans bantları, metre 2 ondalık, adet tam sayı), ALS-D51 (çevrim ürün kartından, fiyat birimi),
  ALS-D52 (fatura gerçekleşen irsaliye miktarıyla) aynen; ALS-D55 yalnız toleransın ölçüldüğü birimi seçer.
- ALS-D6 kur günü kuralı, ALS-D14, ALS-D15, ALS-D41 aynen; ALS-D56 yalnız düzeltme rol listesini günceller.
- Persona kartı bu PR'da değişmez (PRS-01 ayrı PR). Grant/rol ataması insan kapısıdır.
