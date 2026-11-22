# Adet/kg Formül Golden v1 — EK1 (SAT-D451–SAT-D460, ALS-D122–ALS-D124)

## Kaynak, bağımsızlık ve kabul sınırı

Bu belge **yeni Sinan kararı eklemez**. `ADET_KG_FORMUL_GOLDEN_V1.md`'nin ekidir ve SAT-D431 yöntemiyle, aşağıdaki
kararların formül golden'ını kaydeder. Ana golden'ın tanımları (TK1–TK9, TA1–TA5, TI1–TI5, TI8), ortak kabul ve etiket
örnekleri (R1, R5, L9, A07, K02/K03) ve gösterim kuralları burada da geçerlidir. **İstisna:** ana golden'daki TI8 farkı
kesin değerlerden tanımlar; bu ekte fark tabanı SAT-D456 ile **kuruş tabanına** geçmiştir (aşağıda "Fark tabanı"). Ana
golden'ın I11 ve I12 satırlarında tutarlar tam kuruş olduğu için değerleri değişmez; ana golden'daki TI8 metninin
hizalanması ayrı bir belge değişikliğinin işidir.

**Kaynak kararlar:** `SINAN_KARARLARI_2026-11-22_WORK_AI_DALGALAR.md` (bu belge yazılırken `claude/nifty-franklin-nk76d5`
@ `cc9efec8cc948645c8a43e2c41a42fc1d51f54ff` dalında, main'de değil): SAT-D451–SAT-D460, ALS-D122–ALS-D124, GOV-D13,
11–23. ve 28. cevaplar. SAT-D460 (SINAN-CEVAP 66) adetli miktar iadesinde maliyet karşılaştırmasını belirler. SAT-D456–SAT-D459, ALS-D123 ve ALS-D124 (SINAN-CEVAP 64), bu ekin ilk taslağındaki NEEDS-SINAN EN1–EN6
sorularına verilen cevaplardır; GOV-D13 bu ekin ayrı dala gönderilip taslak PR açılmasına izin verir (merge yetkisi
vermez). Dayandıkları önceki kararlar: SAT-D436, SAT-D441, SAT-D444, SAT-D445, ALS-D119, ALS-D120,
ALS-D121, SAT-D371, SAT-D392, SAT-D393, SAT-D396, SAT-D408, SAT-D410, SAT-D427 (`SINAN_KARARLARI_2026-11-21_TREN_KAPANIS.md`
ve main'deki dosyalar), SAT-D11, SAT-D261, SAT-D265.

**Bağımsızlık:** Satırlar, uygulamanın yazarı olmayan ayrı bir Claude oturumunda yalnız karar metinlerinden, kesin
rasyonel aritmetikle (Python `fractions.Fraction`; gösterim `decimal` `ROUND_HALF_UP`, yani yarımda sıfırdan uzağa)
hesaplandı. Ara yuvarlama yalnız kararın açıkça yazdığı yerde yapılır. Bu kurallar için yazılmış uygulama kodu, testleri
ve beklenen değerleri okunmadı.

**Kabul sınırı:** Bu belge hiçbir formülü açmaz. Karar dosyasının "Sınırlar"ı gereği SAT-D453–SAT-D455 ve ALS-D122,
golden satırları iki bağımsız DOCS incelemesinden geçip trene girmeden etkinleştirilmez. **Merge sırası:** Dayandığı karar
dosyası henüz main'de olmadığından bu ek, karar dosyasıyla birlikte tren üzerinden main'e girer; ayrıca main'e merge
edilmez. `OPEN-*`, `KARAR_SICILI.md`, `ACIK_KARARLAR.md`, grant/rol/Critical ve production değişmez. "Türetilen" adımlar
karar metninde açıkça yazmayan ve mevcut kurallardan gerekçesiyle çıkarılan adımlardır; karara bağlanmamış noktalar
`NEEDS-SINAN` diye listelenir ve o satırlar hesaplanamaz bırakılır.

**Fark tabanı (SAT-D456):** iade farkı = belgeye yazılan (kuruşa yuvarlanmış) stok çıkış tutarı − belgeye yazılan
(kuruşa yuvarlanmış) iade tutarı.

**Ortak sonuç notu (miktar iadesi):** Bu not yalnız **kg birimli** sipariş satırından miktar iadesi için geçerlidir (E06,
E08). Ortalama okunamazken elle maliyet yolu (SAT-D454) hem kg hem adetli satırda Bölüm 2'deki gibidir (E08, E14). Kg birimli satırda stok çıkışı ile iade faturası arasında fark ≠ 0 olduğunda sonuç ana
golden I11/I12 ile aynıdır: ortalama okunabiliyorsa fark içeren iade Finans spec'i gelene kadar
**reddedilir** (SAT-D371); fark okunamıyorsa yol SAT-D393/D396/D408 farklı kişi onaylı istisnadır; onaylı istisna Stok
`cost_difference_unposted` (409) reddini aşmaz (SAT-D410). Satırlardaki tutarlar bu kuralın karşılaştırdığı değerlerdir.
**Adetli sipariş satırlarından miktar iadesinde (SAT-D455) SAT-D460 geçerlidir:** fark iade farkı olarak kaydedilir, farklı
kişi onayıyla geçer, **reddedilmez** (Bölüm 3; E12, E13, E13a). Bu onayın Stok `cost_difference_unposted` (409)
beklemesiyle (SAT-D410) ilişkisini SAT-D460 yazmaz; golden bu ilişkiyi seçmez (NEEDS-SINAN EN8). Kg ve adetli satır
arasındaki bu ayrım karar metnini harfiyen izler; teyidi NEEDS-SINAN EN9'dur.

---

## 1. SAT-D451 ve SAT-D453 — miktar iadesinde tedarikçiye fatura birim fiyatı

### Formül

1. Net birim fiyat = seçilen alış siparişi satırının birim fiyatından satıra ait **bütün satır iskontoları** (yüzde,
   zincir, tutar) düşülmüş fiyattır (SAT-D458); **vade farkı eklenmez** (SAT-D451, 11. soru). Sinan'ın örneği: 41,25 − %2,5 =
   40,21875 (13. soru).
   **Belge altı iskonto düşülmez** ve satıra dağıtılmaz; belge altı iskontosu olan siparişten miktar iadesi anlaşılır
   hata koduyla durur (SAT-D458).
2. Net birim fiyat 4 ondalığı aşarsa **4 ondalığa, yarımda sıfırdan uzağa** yuvarlanır (SAT-D453).
3. İade tutarı = iade miktarı × yuvarlanmış net birim fiyat (SAT-D453).

### Türetilen adımlar

- **TE1 — İade belgesi satır tutarı:** iade miktarı × net birim fiyat, belge satırı olarak SAT-D11 ile kuruşa bir kez
  yuvarlanır. Gerekçe: SAT-D453 tutarın yuvarlanmış birim fiyatla hesaplanacağını söyler; belge satırı tutarının kuruşa bir
  kez yuvarlanması mevcut satır kuralıdır (SAT-D11). Satırlar bu yuvarlamaya duyarlı olmayacak biçimde değil, onu
  gösterecek biçimde seçildi (E04).
- **TE2 — Satır iskontosunun biçimi (uygulama modelinden):** SAT-D458 satıra ait bütün iskontoların net fiyata
  yansıdığını söyler, ama iskontonun satırda hangi biçimde tutulduğunu söylemez. Ana oturumun aktardığı mevcut alış satır
  modeline göre (uygulama kuralı; karar metninde yazılı değildir, golden yazarı uygulama kodunu okumadı) satırda iskonto
  **ya yüzde ya tutardır, ikisi birlikte olmaz**; satır düzeyinde zincir iskonto tanımlı değildir; **tutar iskontosu birim
  başınadır**: net birim fiyat = birim fiyat − tutar iskontosu (E07a). Bu nedenle zincir iskonto (E07) ve yüzde + tutar
  birlikteliği (E07c) mevcut modelde oluşamaz. Model değişirse bu satırlar yeniden ele alınır. **Dayanak:** iki bağımsız
  DOCS reviewer bu modeli `claude/nifty-franklin-nk76d5` @ `cc9efec8` üzerinde doğruladı (DOCS-REVIEW-2: şema kısıtı
  `CK_PurchaseOrderLines_Price`; DOCS-REVIEW-1: `PurchaseOrderPricing.cs`).

### Golden satırları (kg birimli seçilen sipariş satırı)

| # | Durum | Girdiler | Kesin ara değerler | Gösterilen / sonuç |
|---|---|---|---|---|
| E01 | Sinan'ın örneği, ayırt edici miktar | Sipariş 41,25 TL/kg, %2,5 iskonto; iade 1 000,000 kg | Net 41.25 × 0.975 = 40.21875 → **40,2188**; tutar 1000 × 40.2188 = 40 218,8 | Birim fiyat **40,2188**; iade faturası **40 218,80 TL** (kesin net 40,21875 ile çıkan 40 218,75 **OLMAZ**) |
| E02 | Yarım sınırı, çift basamak | Sipariş 10,01 TL/kg, %7,5 iskonto; iade 100,000 kg | Net 10.01 × 0.925 = 9.25925 (tam yarım) → **9,2593** | Birim fiyat **9,2593** (yarımda çifte (banker's) yuvarlamayla çıkan 9,2592 **OLMAZ**); iade faturası **925,93 TL** |
| E03 | İskontosuz satır | Sipariş 20,50 TL/kg; iade 80,000 kg | Net 20,5 (yuvarlama yok) | Birim fiyat **20,5000**; iade faturası **1 640,00 TL** (= ana golden I11) |
| E04 | Vade farkı hariç (SAT-D451); TE1 kuruş yuvarlaması | Sipariş 41,25 TL/kg, %2,5 iskonto, %3 vade farkı; iade 80,000 kg | Net 40,2188 (vade farkı hariç); 80 × 40.2188 = 3 217,504 → kuruş | Birim fiyat **40,2188** (vade farkı dahil 40.21875 × 1.03 = 41.4253125 → 41,4253 **OLMAZ**); iade faturası **3 217,50 TL** |
| E05 | Net 4 ondalığı aşmıyor | Sipariş 40,00 TL/kg, %2,5 iskonto; iade 50,000 kg | Net 39 | Birim fiyat **39,0000**; iade faturası **1 950,00 TL** |
| E06 | Ortalama okunabilir, fark var | L9 lotundan (ana golden I11, 120,000 kg) **80,000 kg iade**; depo/ürün ortalaması 21,2500; seçilen satır **R5′** (R5'in fiyatı değiştirilmiş varyantı: 21,00 TL/kg, %2,5 iskonto; ana golden'daki R5 20,50 TL/kg ve iskontosuzdur) | Net 20.475 → 20,4750; fatura 80 × 20.475 = 1 638; stok çıkışı 80 × 21.25 = 1 700; fark (SAT-D456) 1 700,00 − 1 638,00 | Birim fiyat **20,4750**; iade faturası **1 638,00 TL**; stok çıkışı **1 700,00 TL**; fark **62,00 TL** → Finans spec'i gelene kadar **reddedilir** (SAT-D371) |
| E07a | Birim başına tutar iskontosu (SAT-D458, TE2) | Sipariş 41,25 TL/kg, tutar iskontosu 0,2500 TL/kg; iade 80,000 kg | Net 41.25 − 0.25 = 41 | Birim fiyat **41,0000**; iade faturası **3 280,00 TL** |

Durur / oluşamaz:

| # | Durum | Sonuç |
|---|---|---|
| E07b | Seçilen siparişte belge altı iskonto var | Miktar iadesi **anlaşılır hata koduyla durur** (SAT-D458); belge altı iskonto satıra dağıtılmaz |
| E07 | Zincir iskonto (%10 + %2,5) | **Mevcut satır modelinde oluşamaz** (TE2: satır düzeyinde zincir iskonto tanımlı değil; uygulama kuralı) |
| E07c | Aynı satırda yüzde iskonto ve tutar iskontosu birlikte | **Mevcut satır modelinde oluşamaz** (TE2: iskonto ya yüzde ya tutardır; uygulama kuralı). Model değişip bu durum oluşursa, karar gelene kadar yol anlaşılır kodla kapalı kalır (karar dosyası "Sınırlar") |

---

## 2. SAT-D454 — ortalama okunamazken elle maliyet

### Formül

1. Ortalama maliyet **okunamıyorsa**, farklı kişinin onayladığı elle maliyet sipariş fiyatından farklı olabilir
   (SAT-D454).
2. Stok çıkışı = iade kg × onaylı elle maliyet/kg; iade faturası = iade miktarı × siparişin net birim fiyatı (SAT-D451/D453).
3. Fark = kuruşa yuvarlanmış stok çıkışı − kuruşa yuvarlanmış iade faturası (SAT-D456); mevcut iade farkı kuralıyla
   (SAT-D392/D393, farklı kişi onayı) kaydedilir.
4. Ortalama okunabiliyorken elle maliyet girilemez (SAT-D371/D410 sürer).

### Golden satırları

| # | Durum | Girdiler | Kesin ara değerler | Gösterilen / sonuç |
|---|---|---|---|---|
| E08 | Elle maliyet sipariş fiyatından farklı | 80,000 kg; ortalama okunamıyor; elle maliyet 22,1000 TL/kg (giren ≠ onaylayan, `purchasing.orders.approve`); sipariş 21,00 TL/kg, %2,5 → 20,4750 | Stok çıkışı 80 × 22.1 = 1 768; fatura 1 638; fark 130 | Stok çıkışı **1 768,00 TL** (elle maliyetle); iade faturası **1 638,00 TL**; fark **130,00 TL** iade farkı olarak; onaylı istisna Stok 409'unu aşmaz (SAT-D410) |
| E09 | Elle maliyet net fiyata eşit | 80,000 kg; ortalama okunamıyor; elle maliyet 20,4750 | Stok çıkışı 1 638; fatura 1 638; fark 0 | Stok çıkışı ve fatura **1 638,00 TL**; fark yok |

Hesaplanamaz / reddedilir:

| # | Durum | Sonuç |
|---|---|---|
| E10 | Ortalama okunabiliyorken elle maliyet girildi | **Reddedilir** (SAT-D454 son cümlesi; SAT-D371/D410) |
| E11 | Elle maliyeti iadeyi giren kişi onaylamak istiyor | **Reddedilir** (SAT-D408) |

---

## 3. SAT-D455 — adet birimli sipariş satırından miktar iadesi

### Formül

1. Hazırlayan **iade adedini** girer.
2. İade belgesi = adet × siparişin net adet fiyatı (SAT-D451; 4 ondalığı aşarsa SAT-D453 ile yuvarlanır).
3. Stoktan çıkan kg ve adet etiketten gelir; kg belgede yalnız bilgidir (SAT-D455).
4. Stok çıkışı = etiketten çıkan kg × depo/ürün ortalaması/kg (SAT-D444) veya, ortalama okunamıyorsa, onaylı elle maliyet
   (SAT-D454).

5. Girilen iade adedi stoktan çıkan etiket adedine **eşit olmalıdır**; farklıysa iade reddedilir (SAT-D459).
6. Fark = kuruşa yuvarlanmış stok çıkışı − kuruşa yuvarlanmış iade belgesi tutarı (SAT-D456).
7. **Maliyet karşılaştırması (SAT-D460):** belge tutarı (adet × net adet fiyatı) iade kg'sine bölünerek stok ortalama kg
   maliyetiyle karşılaştırılır; fark (6. adım) **iade farkı** olarak kaydedilir ve farklı kişi onayıyla (SAT-D392/D393)
   geçer; **fark yüzünden iade reddedilmez**. Bu onayın Stok 409 (SAT-D410) beklemesiyle ilişkisi kararda yazmaz (EN8).
   Kg başına faturalanan değer karşılaştırma bilgisidir; golden onun gösterim
   hassasiyetini seçmez, kesin değerini verir.

### Golden satırları

| # | Durum | Girdiler | Kesin ara değerler | Gösterilen / sonuç |
|---|---|---|---|---|
| E12 | Adetli satır, iskonto, yarım sınırı (SAT-D460) | Sipariş satırı 265,13 TL/adet, %2,5 iskonto; iade 3 adet; etiket 3 adet / 37,461 kg; depo/ürün ortalaması 21,2500 TL/kg | Net 265.13 × 0.975 = 258.50175 (tam yarım) → **258,5018**; belge 3 × 258.5018 = 775,5054 → 775,51; kg başına faturalanan 775.51 / 37.461 = 20.70179653… (ortalama 21,25 ile karşılaştırılır); stok çıkışı 37.461 × 21.25 = 796,04625 → 796,05; fark (SAT-D456) 796,05 − 775,51 | Birim fiyat **258,5018 TL/adet**; iade faturası **775,51 TL** (3 adet; kg 37,461 bilgi); stok çıkışı **796,05 TL**; fark **20,54 TL** iade farkı olarak kaydedilir, farklı kişi onayıyla geçer; **reddedilmez** (SAT-D460); Stok 409 ilişkisi EN8 |
| E13 | Adetli satır, vade farkı hariç | E12 satırında ayrıca %2 vade farkı | Net yine 258,5018 (vade farkı dahil 258.50175 × 1.02 = 263,671785 → 263,6718 **OLMAZ**) | E12 ile aynı: **775,51 TL** fatura, **796,05 TL** çıkış, fark **20,54 TL** iade farkı, farklı kişi onayı (SAT-D460); Stok 409 ilişkisi EN8 |
| E13a | Adetli satır, fatura stok çıkışından büyük (SAT-D460) | Net 300,00 TL/adet; iade 3 adet; etiket 3 adet / 37,461 kg; ortalama 21,2500 | Belge 900,00; kg başına faturalanan 900 / 37.461 = 24.02498598…; stok çıkışı 796,05; fark 796,05 − 900,00 | İade faturası **900,00 TL**; stok çıkışı **796,05 TL**; fark **−103,95 TL** iade farkı, farklı kişi onayıyla geçer; **reddedilmez**; Stok 409 ilişkisi EN8 |
| E13b | Adetli satır, fark kuruş tabanında sıfır (SAT-D460, SAT-D456) | Net 265,35 TL/adet; iade 3 adet; etiket 37,461 kg; ortalama 21,2500 | Belge 796,05; kg başına faturalanan 796.05 / 37.461 = 21.25010010…; stok çıkışı 796,04625 → 796,05; fark 796,05 − 796,05 | Fark **0,00 TL** → iade farkı yok (kesin değerlerden −0,00375 çıkar; kuruş tabanında sıfırdır, fark kaydı **OLMAZ**) |
| E14 | Adetli satır, ortalama okunamıyor (SAT-D454); ayırt edici fark tabanı (SAT-D456) | E12; ortalama okunamıyor; onaylı elle maliyet 20,7000 TL/kg | Stok çıkışı 37.461 × 20.7 = 775,4427 → 775,44; fark 775,44 − 775,51 | Stok çıkışı **775,44 TL**; iade faturası **775,51 TL**; fark **−0,07 TL** (kesin değerlerden 775.4427 − 775.5054 = −0,0627 → −0,06 **OLMAZ**); onaylı istisna Stok 409'unu aşmaz (SAT-D410) |

Reddedilir:

| # | Durum | Sonuç |
|---|---|---|
| E15 | Girilen iade adedi, iade edilen etiketin adediyle tutmuyor (ör. 3 adet girildi, etikette 5 adet var) | **Reddedilir** (SAT-D459); hazırlayan düzeltir |

---

## 4. SAT-D452 — gerçekleşen kg fiyatına yalnız kesinleşmiş faturalar

### Formül

Satış ve alışta gerçekleşme ve gerçek kg fiyatı (SAT-D436, ana golden TK6) hesabına **yalnız kesinleşmiş faturalar** girer;
taslak, iptal ve reddedilmiş faturalar sayılmaz (SAT-D452). Finans'ın reddettiği satış faturası kesinleşmiş sayılmaz ve
hesaba girmez; yeniden gönderilip Finans kabul edince girer (SAT-D457). Teorik kg fiyatı siparişin kendi tutarı ve teorik kg'sinden
hesaplandığı için faturaların durumundan etkilenmez.

### Golden satırları

Ortak sipariş: 100 adet × 250,00 TL = 25 000,00; `k` 12,345 → teorik 1 234,500 kg; teorik kg fiyatı 20,25.
Fatura 1: 40 adet, 10 000,00 TL, kantar 500,160 kg. Fatura 2: 60 adet, 15 000,00 TL, kantar 748,900 kg (ana golden K02/K03).

| # | Durum | Kesin ara değerler | Gösterilen |
|---|---|---|---|
| E16 | Fatura 1 kesinleşmiş, fatura 2 **taslak** | Yalnız fatura 1: 10000/500.16 = 19.99360204… | Gerçekleşme **40/100 adet, 10 000,00 TL**, gerçek kg **500,160**, gerçek kg fiyatı **19,99** (taslak dahil 20,02 **OLMAZ**); teorik **20,25** |
| E17 | İki fatura da kesinleşmiş | 25000/1249.06 = 20.01505131… | **100/100 adet**, gerçek kg fiyatı **20,02** (= K03) |
| E18 | Fatura 2 kesinleştikten sonra **iptal** edildi | Yalnız fatura 1 | **40/100 adet**, **19,99** |
| E19 | Sipariş için yalnız taslak fatura var | Kesinleşmiş fatura yok | Gerçek kg fiyatı **gösterilemez**; teorik **20,25** gösterilir |
| E20 | Alış: kabul tartılmış (489,750 kg), fatura taslak | Kesinleşmiş fatura yok | Gerçek kg fiyatı **gösterilemez**; teorik **18,00** (ana golden K10); fatura kesinleşince 9000/489.75 → **18,38** |

| E21 | Fatura 2 Finans tarafından reddedildi (SAT-D427), yeniden gönderim bekliyor (SAT-D457) | Yalnız fatura 1 | **40/100 adet**, **19,99** |
| E21a | Fatura 2 yeniden gönderildi ve Finans kabul etti (SAT-D457) | 25000/1249.06 | **100/100 adet**, **20,02** |

---

## 5. ALS-D122 — kısmi iadeden sonra kısmi fatura kg payı

### Formül

1. Kısmi iade yapılmışsa faturalanan adede düşen kg = **kabulün kalan kg'si × faturalanan adet ÷ kalan adet** (ALS-D122).
   Kalan kg ve adet **her fatura işlenirken güncel** hesaplanır: kabulün tartılan kg'si ve adedinden o ana kadarki iadeler
   ve önceki faturaların kg ve adedi düşülür (ALS-D123).
2. İade yoksa ALS-D121 aynen geçerlidir (kabulde tartılan kg × faturalanan adet ÷ kabul adedi).
3. kg payı **3 ondalığa, yarımda sıfırdan uzağa** yuvarlanır; kabul tamamen faturalandığında **son fatura kalan kg'yi**
   alır ve toplam, iadeler düşülmüş kabul kg'sine tam eşit olur (ALS-D124).
4. Maliyet/kg = faturalı payın tutarı ÷ kg payı; tutar kesin taşınır (ALS-D120); dövizde TL karşılığı tedarikçi
   faturasının resmî kuruyla (BS-1).

### Golden satırları

Ortak kabul (ana golden A01/A07): 100 adet, kabulde tartılan 1 250,400 kg.

| # | Durum | Girdiler | Kesin ara değerler | Gösterilen |
|---|---|---|---|---|
| E22 | Kısmi iade sonra kısmi fatura, TL | 20 adet iade, tartılmış 249,870 kg → kalan 80 adet / 1 000,530 kg; fatura 40 adet × 250,00 = 10 000,00 TL | kg payı 1000.53 × 40 / 80 = 500,265; 10000/500.265 = 19.98940561… | kg payı **500,265** (ALS-D121 özgün oranıyla 500,160 **OLMAZ**); maliyet/kg gösterim **19,9894** |
| E23 | Aynı, dövizli (A07) | E22; 40 adet × 7,50 USD, resmî kur 34,5678 → 10 370,34 TL | 10370.34/500.265 = 20.72969326… | kg payı **500,265**; faturalı pay **10 370,34 TL**; maliyet/kg gösterim **20,7297** |
| E24 | İade kabulün kendi oranıyla çıktı | 20 adet iade, tartılmış 250,080 kg (= 12,504 × 20) → kalan 80 adet / 1 000,320 kg; fatura 40 adet | 1000.32 × 40 / 80 = 500,16 | kg payı **500,160** (ALS-D121 ile aynı; iki kural bu durumda örtüşür) |
| E25 | İade yok | Fatura 40 adet | ALS-D121: 1250.4 × 40 / 100 = 500,16 | kg payı **500,160** (= ana golden A13) |
| E26 | kg payı 3 ondalıkta bitmiyor (ALS-D124) | 30 adet iade (tartılmış 249,870 kg) sonrası kalan 70 adet / 1 000,530 kg; fatura 40 adet (son değil) | 1000.53 × 40 / 70 = 571.73142857… → 3 ondalık | kg payı **571,731**; kalan 30 adet / 428,799 kg |
| E27 | Faturalar arasında iade (ALS-D123) | Fatura 1: 40 adet (iade yok, ALS-D121) → 500,160; sonra 20 adet iade (249,870 kg, fatura bekleyen paydan); sonra fatura 2: 40 adet | Fatura 2 anında kalan 1250.4 − 249.87 − 500.16 = 500,370 kg / 100 − 20 − 40 = 40 adet; 500.37 × 40 / 40 | Fatura 1 **500,160**; fatura 2 **500,370 kg** (yalnız iadeyi düşen okumayla 1000.53 × 40/80 = 500,265 **OLMAZ**) |
| E28 | Yarım sınırı ve son faturanın kalanı (ALS-D123, ALS-D124) | İade sonrası kalan 3 adet / 1 000,000 kg; üç fatura, her biri 1 adet | F1: 1000/3 = 333.333… → 333,333; F2: kalan 666,667 / 2 = 333.3335 (tam yarım) → 333,334; F3 son: kalan 333,333 | **333,333 / 333,334 / 333,333**; toplam 1 000,000 |
| E29 | İade yokken üç fatura (ALS-D121, ALS-D124) | Kabul 3 adet / 1 000,000 kg, iade yok; üç fatura, her biri 1 adet | F1, F2: 1000 × 1/3 = 333.333… → 333,333 (ALS-D121 özgün oran); F3 son: 1000 − 666,666 = 333,334 | **333,333 / 333,333 / 333,334**; toplam 1 000,000 (E28'den farklı: iade yoksa ALS-D121 aynen geçerlidir) |

---

## Türetilenler

- **TE1:** İade belgesi satır tutarı, SAT-D11 ile kuruşa bir kez yuvarlanır (E01, E04, E07a, E12). SAT-D456 "belgeye
  yazılan (kuruşa yuvarlanmış) iade tutarı" ifadesiyle bu adımı doğrular.
- **TE2:** Satır iskontosunun biçimi uygulama modelinden alınır (karar metninde yazılı değildir): yüzde ya da birim başına
  tutar, ikisi birlikte değil; zincir iskonto yok (E07, E07a, E07c).
- İlk taslaktaki TE3 (fark tabanı) SAT-D456 ile karar metnine dönüştü.

Hiçbiri yeni para/stok/maliyet politikası seçmez. Bir reviewer bir türetmeyi politika seçimi sayarsa ilgili satır
hesaplanamaz kabul edilip `NEEDS-SINAN`'a taşınmalıdır.

## İlk taslağın NEEDS-SINAN noktalarının durumu (SINAN-CEVAP 64)

| No | Konu | Durum | Satır |
|---|---|---|---|
| EN1 | Zincir, tutar ve belge altı iskonto | **SAT-D458:** satır iskontolarının hepsi düşülür; belge altı düşülmez, iade hata koduyla durur | E07a, E07b (E07 mevcut modelde oluşamaz) |
| EN7 | Yüzde ve tutar iskontosu birlikte | Mevcut satır modelinde oluşamaz (TE2; uygulama kuralı) | E07c |
| EN2 | Fark tabanı | **SAT-D456:** kuruşa yuvarlanmış iki tutar | E14 |
| EN3 | Girilen adet ≠ etiket adedi | **SAT-D459:** reddedilir | E15 |
| EN4 | Finans reddi alan fatura | **SAT-D457:** sayılmaz; Finans kabul edince girer | E21, E21a |
| EN5 | kg payının yuvarlanması | **ALS-D124:** 3 ondalık, yarımda sıfırdan uzağa; son fatura kalanı alır | E26, E28, E29 |
| EN6 | "Kalan"ın tanımı | **ALS-D123:** her fatura anında güncel kalan | E27, E28 |

## NEEDS-SINAN

EN7, mevcut satır modelinde oluşamadığı için listeden çıkarıldı (TE2). Açık noktalar (satır değerlerini değiştirmez):

- **EN8 (E12, E13, E13a):** SAT-D460'a göre adetli miktar iadesinde farklı kişi onayıyla geçen iade farkı, Stok'un
  `cost_difference_unposted` (409) beklemesini (SAT-D410) de aşar mı, yoksa iade fark işlenene kadar mı bekler?
- **EN9 (E06 ile E12):** Kg birimli miktar iadesinde ortalama okunabilirken fark içeren iade reddediliyor (SAT-D371,
  ana golden I11/I12); adetli satırda ise onayla geçiyor (SAT-D460). 28. cevaptaki "diğer miktar iadeleri gibi … ret yok"
  ifadesi kg satırının da onayla geçmesinin istendiğini düşündürebilir; bu ayrım isteniyor mu?

## Sınırlar

- Bu belge kod, test, migration, OpenAPI, Ayarlar verisi veya başka karar dosyası değiştirmez; grant açmaz.
- `OPEN-*` kapatılmaz; `KARAR_SICILI.md` ve `ACIK_KARARLAR.md` değişmez. İnsan kapıları (deployment, environment,
  secret, Settings → Actions etkinleştirme, grant/rol/Critical, production aktivasyonu, `OPEN-*` kapatma,
  `.claude/settings.json`) değişmez.
