# Adet/kg Formül Golden v1 — EK1 (SAT-D451, SAT-D452, SAT-D453, SAT-D454, SAT-D455, ALS-D122)

## Kaynak, bağımsızlık ve kabul sınırı

Bu belge **yeni Sinan kararı eklemez**. `ADET_KG_FORMUL_GOLDEN_V1.md`'nin ekidir ve SAT-D431 yöntemiyle, aşağıdaki
kararların formül golden'ını kaydeder. Ana golden'ın tanımları (TK1–TK9, TA1–TA5, TI1–TI5, TI8), ortak kabul ve etiket
örnekleri (R1, R5, L9, A07, K02/K03) ve gösterim kuralları burada da geçerlidir.

**Kaynak kararlar:** `SINAN_KARARLARI_2026-11-22_WORK_AI_DALGALAR.md` (bu belge yazılırken `claude/nifty-franklin-nk76d5`
@ `f12fed4b8bb1f3e6861647dfb17ea9a2f0dd472c` dalında, main'de değil): SAT-D451, SAT-D452, SAT-D453, SAT-D454, SAT-D455,
ALS-D122 ve 11–16. cevaplar. Dayandıkları önceki kararlar: SAT-D436, SAT-D441, SAT-D444, SAT-D445, ALS-D119, ALS-D120,
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

**Ortak sonuç notu (miktar iadesi):** Bu ekteki miktar iadesi satırlarında stok çıkışı ile iade faturası arasında fark
≠ 0 olduğunda sonuç ana golden I11/I12 ile aynıdır: ortalama okunabiliyorsa fark içeren iade Finans spec'i gelene kadar
**reddedilir** (SAT-D371); fark okunamıyorsa yol SAT-D393/D396/D408 farklı kişi onaylı istisnadır; onaylı istisna Stok
`cost_difference_unposted` (409) reddini aşmaz (SAT-D410). Satırlardaki tutarlar bu kuralın karşılaştırdığı değerlerdir.

---

## 1. SAT-D451 ve SAT-D453 — miktar iadesinde tedarikçiye fatura birim fiyatı

### Formül

1. Net birim fiyat = seçilen alış siparişi satırının birim fiyatı × (1 − iskonto oranı); **vade farkı eklenmez**
   (SAT-D451). Sinan'ın örneği: 41,25 − %2,5 = 40,21875 (13. soru).
2. Net birim fiyat 4 ondalığı aşarsa **4 ondalığa, yarımda sıfırdan uzağa** yuvarlanır (SAT-D453).
3. İade tutarı = iade miktarı × yuvarlanmış net birim fiyat (SAT-D453).

### Türetilen adımlar

- **TE1 — İade belgesi satır tutarı:** iade miktarı × net birim fiyat, belge satırı olarak SAT-D11 ile kuruşa bir kez
  yuvarlanır. Gerekçe: SAT-D453 tutarın yuvarlanmış birim fiyatla hesaplanacağını söyler; belge satırı tutarının kuruşa bir
  kez yuvarlanması mevcut satır kuralıdır (SAT-D11). Satırlar bu yuvarlamaya duyarlı olmayacak biçimde değil, onu
  gösterecek biçimde seçildi (E04).
- **TE2 — Tek satır iskontosu:** formül, satırda tek bir yüzde iskonto olduğu durumu kapsar (13. sorudaki örnek). Zincir
  iskonto (ör. %10 + %5), tutar olarak satır iskontosu ve belge altı iskontonun net birim fiyata nasıl yansıyacağı
  kararda yazmaz → NEEDS-SINAN EN1.

### Golden satırları (kg birimli seçilen sipariş satırı)

| # | Durum | Girdiler | Kesin ara değerler | Gösterilen / sonuç |
|---|---|---|---|---|
| E01 | Sinan'ın örneği, ayırt edici miktar | Sipariş 41,25 TL/kg, %2,5 iskonto; iade 1 000,000 kg | Net 41.25 × 0.975 = 40.21875 → **40,2188**; tutar 1000 × 40.2188 = 40 218,8 | Birim fiyat **40,2188**; iade faturası **40 218,80 TL** (kesin net 40,21875 ile çıkan 40 218,75 **OLMAZ**) |
| E02 | Yarım sınırı, çift basamak | Sipariş 10,01 TL/kg, %7,5 iskonto; iade 100,000 kg | Net 10.01 × 0.925 = 9.25925 (tam yarım) → **9,2593** | Birim fiyat **9,2593** (çifte yuvarlamayla 9,2592 **OLMAZ**); iade faturası **925,93 TL** |
| E03 | İskontosuz satır | Sipariş 20,50 TL/kg; iade 80,000 kg | Net 20,5 (yuvarlama yok) | Birim fiyat **20,5000**; iade faturası **1 640,00 TL** (= ana golden I11) |
| E04 | Vade farkı hariç (SAT-D451); TE1 kuruş yuvarlaması | Sipariş 41,25 TL/kg, %2,5 iskonto, %3 vade farkı; iade 80,000 kg | Net 40,2188 (vade farkı hariç); 80 × 40.2188 = 3 217,504 → kuruş | Birim fiyat **40,2188** (vade farkı dahil 40.21875 × 1.03 = 41.4253125 → 41,4253 **OLMAZ**); iade faturası **3 217,50 TL** |
| E05 | Net 4 ondalığı aşmıyor | Sipariş 40,00 TL/kg, %2,5 iskonto; iade 50,000 kg | Net 39 | Birim fiyat **39,0000**; iade faturası **1 950,00 TL** |
| E06 | Ortalama okunabilir, fark var | L9 (ana golden I11) 80,000 kg, depo/ürün ortalaması 21,2500; seçilen R5 satırı 21,00 TL/kg, %2,5 iskonto | Net 20.475 → 20,4750; fatura 80 × 20.475 = 1 638; stok çıkışı 80 × 21.25 = 1 700; fark 62 | Birim fiyat **20,4750**; iade faturası **1 638,00 TL**; stok çıkışı **1 700,00 TL**; fark **62,00 TL** → Finans spec'i gelene kadar **reddedilir** (SAT-D371) |

Hesaplanamaz:

| # | Durum | Sonuç |
|---|---|---|
| E07 | Seçilen sipariş satırında zincir iskonto (%10 + %5), tutar iskontosu veya belge altı iskonto var (TE2) | **NEEDS-SINAN EN1** |

---

## 2. SAT-D454 — ortalama okunamazken elle maliyet

### Formül

1. Ortalama maliyet **okunamıyorsa**, farklı kişinin onayladığı elle maliyet sipariş fiyatından farklı olabilir
   (SAT-D454).
2. Stok çıkışı = iade kg × onaylı elle maliyet/kg; iade faturası = iade miktarı × siparişin net birim fiyatı (SAT-D451/D453).
3. Fark = stok çıkışı − iade faturası; mevcut iade farkı kuralıyla (SAT-D392/D393, farklı kişi onayı) kaydedilir.
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

### Türetilen adım

- **TE3 — Fark tabanı:** fark = stok çıkışı − iade belgesi satır tutarı. Stok çıkışı tutarının saklama hassasiyeti ve
  farkın kuruşa yuvarlanmış belge tutarından mı, kesin tutardan mı alınacağı kararda yazmaz; E12 ve E13 bu iki okumanın
  aynı gösterilen farkı verdiği sayılarla seçildi. İki okumanın ayrıştığı durum NEEDS-SINAN EN2'dir.

### Golden satırları

| # | Durum | Girdiler | Kesin ara değerler | Gösterilen / sonuç |
|---|---|---|---|---|
| E12 | Adetli satır, iskonto, yarım sınırı | Sipariş satırı 265,13 TL/adet, %2,5 iskonto; iade 3 adet; etiket 3 adet / 37,461 kg; depo/ürün ortalaması 21,2500 TL/kg | Net 265.13 × 0.975 = 258.50175 (tam yarım) → **258,5018**; belge 3 × 258.5018 = 775,5054 → 775,51; stok çıkışı 37.461 × 21.25 = 796,04625; fark 20,54085 (kuruş tabanıyla 20,53625) | Birim fiyat **258,5018 TL/adet**; iade faturası **775,51 TL** (3 adet; kg 37,461 bilgi); stok çıkışı **796,05 TL**; fark **20,54 TL** → Finans spec'i gelene kadar **reddedilir** (SAT-D371) |
| E13 | Adetli satır, vade farkı hariç | E12 satırında ayrıca %2 vade farkı | Net yine 258,5018 (vade farkı dahil 258.50175 × 1.02 = 263,671785 → 263,6718 **OLMAZ**) | E12 ile aynı: **775,51 TL** fatura, **796,05 TL** çıkış, fark **20,54 TL** |
| E14 | Adetli satır, ortalama okunamıyor (SAT-D454) | E12; ortalama okunamıyor; onaylı elle maliyet 20,7000 TL/kg | Stok çıkışı 37.461 × 20.7 = 775,4427; fark 775.4427 − 775.5054 = −0,0627 (kuruş tabanıyla −0,0673) | Stok çıkışı **775,44 TL**; iade faturası **775,51 TL**; fark **−0,06 TL** (kuruş tabanında −0,07) → iki okuma ayrışır: **hesaplanamaz — NEEDS-SINAN EN2** |

Hesaplanamaz:

| # | Durum | Sonuç |
|---|---|---|
| E15 | Girilen iade adedi, iade edilen etiketin adediyle tutmuyor (ör. 3 adet girildi, etikette 5 adet var) | **NEEDS-SINAN EN3** (etiketin önce bölünmesi mi gerekir, kg nasıl belirlenir) |

---

## 4. SAT-D452 — gerçekleşen kg fiyatına yalnız kesinleşmiş faturalar

### Formül

Satış ve alışta gerçekleşme ve gerçek kg fiyatı (SAT-D436, ana golden TK6) hesabına **yalnız kesinleşmiş faturalar** girer;
taslak, iptal ve reddedilmiş faturalar sayılmaz (SAT-D452). Teorik kg fiyatı siparişin kendi tutarı ve teorik kg'sinden
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

Hesaplanamaz:

| # | Durum | Sonuç |
|---|---|---|
| E21 | Kesinleşmiş satış faturası Finans tarafından yetki gerekçesiyle reddedildi (SAT-D427) ve yeniden gönderimi bekliyor | SAT-D452'deki "reddedilmiş fatura"nın bu Finans reddini kapsayıp kapsamadığı yazmaz (SAT-D427'de fatura değişmez, kesinleşmiş kalır) → **NEEDS-SINAN EN4** |

---

## 5. ALS-D122 — kısmi iadeden sonra kısmi fatura kg payı

### Formül

1. Kısmi iade yapılmışsa faturalanan adede düşen kg = **kabulün kalan kg'si × faturalanan adet ÷ kalan adet** (ALS-D122).
2. İade yoksa ALS-D121 aynen geçerlidir (kabulde tartılan kg × faturalanan adet ÷ kabul adedi).
3. Maliyet/kg = faturalı payın tutarı ÷ kg payı; tutar kesin taşınır (ALS-D120); dövizde TL karşılığı tedarikçi
   faturasının resmî kuruyla (BS-1).

### Golden satırları

Ortak kabul (ana golden A01/A07): 100 adet, kabulde tartılan 1 250,400 kg.

| # | Durum | Girdiler | Kesin ara değerler | Gösterilen |
|---|---|---|---|---|
| E22 | Kısmi iade sonra kısmi fatura, TL | 20 adet iade, tartılmış 249,870 kg → kalan 80 adet / 1 000,530 kg; fatura 40 adet × 250,00 = 10 000,00 TL | kg payı 1000.53 × 40 / 80 = 500,265; 10000/500.265 = 19.98940561… | kg payı **500,265** (ALS-D121 özgün oranıyla 500,160 **OLMAZ**); maliyet/kg gösterim **19,9894** |
| E23 | Aynı, dövizli (A07) | E22; 40 adet × 7,50 USD, resmî kur 34,5678 → 10 370,34 TL | 10370.34/500.265 = 20.72969326… | kg payı **500,265**; faturalı pay **10 370,34 TL**; maliyet/kg gösterim **20,7297** |
| E24 | İade kabulün kendi oranıyla çıktı | 20 adet iade, tartılmış 250,080 kg (= 12,504 × 20) → kalan 80 adet / 1 000,320 kg; fatura 40 adet | 1000.32 × 40 / 80 = 500,16 | kg payı **500,160** (ALS-D121 ile aynı; iki kural bu durumda örtüşür) |
| E25 | İade yok | Fatura 40 adet | ALS-D121: 1250.4 × 40 / 100 = 500,16 | kg payı **500,160** (= ana golden A13) |

Hesaplanamaz:

| # | Durum | Sonuç |
|---|---|---|
| E26 | kg payı 3 ondalıkta bitmiyor: kalan 70 adet / 1 000,530 kg, fatura 40 adet → 571,73142857… kg | Payın yuvarlanıp yuvarlanmayacağı (3 ondalık, URN-D9 benzeri) ve artığın kalan paya nasıl yazılacağı kararda yok → **NEEDS-SINAN EN5** |
| E27 | Faturalar arasında iade: önce fatura 1 (40 adet, kg payı 500,160), sonra 20 adet iade (249,870 kg, fatura bekleyen paydan seçildi), sonra fatura 2 (40 adet) | "Kalan" iadeden sonra kalan kabul miktarı mı (1 000,530 kg / 80 adet → 500,265), yoksa faturalanmamış ve iade edilmemiş kalan mı (500,370 kg / 40 adet → 500,370)? → **NEEDS-SINAN EN6** |

---

## Türetilenler

- **TE1:** İade belgesi satır tutarı, SAT-D11 ile kuruşa bir kez yuvarlanır (E01, E04, E12).
- **TE2:** SAT-D451/D453 formülü tek yüzde satır iskontosunu kapsar; diğer iskonto biçimleri EN1.
- **TE3:** Fark = stok çıkışı − iade belgesi satır tutarı; satırlar iki tabanın aynı gösterilen farkı verdiği sayılarla
  seçildi, ayrışma EN2.

Hiçbiri yeni para/stok/maliyet politikası seçmez. Bir reviewer bir türetmeyi politika seçimi sayarsa ilgili satır
hesaplanamaz kabul edilip `NEEDS-SINAN`'a taşınmalıdır.

## NEEDS-SINAN

- **EN1 (E07):** Seçilen sipariş satırında zincir iskonto (%10 + %5), tutar olarak iskonto veya belge altı iskonto varsa
  miktar iadesinin net birim fiyatı nasıl hesaplansın?
- **EN2 (E14):** Miktar iadesinde fark, kuruşa yuvarlanmış iade belgesi tutarından mı, kesin tutardan (miktar × 4 ondalıklı
  net fiyat) mı hesaplansın; stok çıkışı tutarı hangi hassasiyetle tutulsun?
- **EN3 (E15):** Adetli satırdan miktar iadesinde girilen adet, iade edilen etiketin adediyle tutmuyorsa ne olsun (etiket
  önce bölünsün mü; kg nasıl belirlensin)?
- **EN4 (E21):** SAT-D427 Finans yetki reddi alan, kesinleşmiş ve yeniden gönderim bekleyen fatura, SAT-D452'deki
  "reddedilmiş fatura" sayılsın mı?
- **EN5 (E26):** ALS-D122/ALS-D121 kg payı 3 ondalıkta bitmezse yuvarlansın mı; artık kalan paya nasıl yazılsın?
- **EN6 (E27):** ALS-D122'deki "kalan", faturalar arasında iade olduğunda iade sonrası kalan kabul miktarı mı, yoksa
  faturalanmamış ve iade edilmemiş kalan mı?

## Sınırlar

- Bu belge kod, test, migration, OpenAPI, Ayarlar verisi veya başka karar dosyası değiştirmez; grant açmaz.
- `OPEN-*` kapatılmaz; `KARAR_SICILI.md` ve `ACIK_KARARLAR.md` değişmez. İnsan kapıları (deployment, environment,
  secret, Settings → Actions etkinleştirme, grant/rol/Critical, production aktivasyonu, `OPEN-*` kapatma,
  `.claude/settings.json`) değişmez.
