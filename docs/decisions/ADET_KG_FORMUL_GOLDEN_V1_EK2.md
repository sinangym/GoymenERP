# Adet/kg Formül Golden v1 — EK2 (SAT-D465–SAT-D467: belge düzeyi kg fiyatı)

## Kaynak, bağımsızlık ve kabul sınırı

Bu belge **yeni Sinan kararı eklemez**. `ADET_KG_FORMUL_GOLDEN_V1.md`'nin ikinci ekidir ve SAT-D431 yöntemiyle SAT-D465'in
formül golden'ını kaydeder. Ana golden'ın kg fiyatı tanımları (Bölüm 1: TK1–TK9, K01–K19) ve gösterim kuralları burada da
geçerlidir; SAT-D465 ana golden'daki **N2**'yi cevaplar ve "Sınırlar"daki fail-closed ara kuralın ("belge düzeyi yalnız
bütün satırlar kg'li mal satırıyken gösterilir") kg'siz hizmet satırı için yerine geçer.

**Kaynak karar:** `SINAN_KARARLARI_2026-11-22_EK_SORULAR.md` (bu belge yazılırken `claude/nifty-franklin-nk76d5` @
`0308f5415` ve öncesi, main'de değil), E-09, E-13–E-15 soruları, SAT-D465, SAT-D466, SAT-D467 ve GOV-D14 (bu ekin
`claude/adet-kg-golden-ek2` dalına gönderilme izni; merge yetkisi vermez). SAT-D465: "Belge düzeyi kg fiyatı = net mal
tutarı / net kg. Belge altı iskonto mal satırlarına tutarla oransal dağıtılıp düşülür; vade farkı ve kg'siz hizmet satırları
dahil edilmez; iade satırlarının tutarı ve kg'si düşülür." SAT-D466 ve SAT-D467, bu ekin ilk taslağındaki NEEDS-SINAN
EB1 ve EB2'yi cevaplar. Dayandığı önceki kararlar: SAT-D436, SAT-D437, SAT-D439,
SAT-D452, SAT-D457 (kesinleşmiş faturalar), SAT-D11, SAT-D76, SAT-D407 (belge altı iskonto dağıtımında kuruş ve artık),
URN-D9 (belge satırı kg'si).

**Bağımsızlık:** Satırlar, uygulamanın yazarı olmayan ayrı bir Claude oturumunda yalnız karar metinlerinden, kesin rasyonel
aritmetikle (Python `fractions.Fraction`; gösterim `decimal` `ROUND_HALF_UP`, yani yarımda sıfırdan uzağa) hesaplandı. Ara
yuvarlama yalnız kararın açıkça yazdığı yerde yapılır. Uygulama kodu, testleri ve beklenen değerleri okunmadı.

**Kabul sınırı:** Bu belge hiçbir formülü açmaz; SAT-D465 gereği bağımsız golden iki DOCS incelemesiyle onaylanmadan
gösterim açılmaz. **Merge sırası:** Dayandığı karar dosyası henüz main'de olmadığından bu ek, karar dosyası ve ana golden'la
birlikte tren üzerinden main'e girer; ayrıca main'e merge edilmez. `OPEN-*`, `KARAR_SICILI.md`, `ACIK_KARARLAR.md`,
grant/rol/Critical ve production değişmez. "Türetilen" adımlar karar metninde açıkça yazmayan ve mevcut kurallardan
gerekçesiyle çıkarılan adımlardır; karara bağlanmamış noktalar `NEEDS-SINAN` diye listelenir ve o satırlar açık bırakılır.

---

## Formül

1. **Net mal tutarı** = Σ mal satırı tutarı (KDV hariç, satır iskontosu sonrası, **vade farkı hariç**) − belge altı
   iskonto − Σ iade satırı tutarı (SAT-D465). Kg'siz hizmet satırları (nakliye vb.) dahil edilmez.
2. **Net kg** = Σ mal satırı kg'si − Σ iade satırı kg'si. Teorik göstergede teorik kg (miktar × belgeye kaydedilen kg/birim,
   SAT-D437; URN-D9 ile 3 ondalık belge satırı kg'si), gerçek göstergede faturalanan satırlara yazılan gerçek kg
   (SAT-D436; ana golden TK6).
3. **Belge düzeyi kg fiyatı** = net mal tutarı ÷ net kg (kesin), gösterim 0,01 yarımda sıfırdan uzağa (SAT-D436). Dövizli
   belgede belge para biriminde (SAT-D439). Gerçek kg fiyatına yalnız kesinleşmiş ve Finans'ın reddetmediği faturalar
   girer (SAT-D452, SAT-D457).
4. **Net kg ≤ 0** ise belge düzeyi kg fiyatı **gösterilmez** (sıfıra veya negatif kg'ye bölme yapılmaz; sıfır/tahmini
   değer yazılmaz). **Net kg > 0 ama net mal tutarı ≤ 0** ise de gösterilmez ("hesaplanamaz"; SAT-D466).
5. **Belge altı iskonto dağıtım tabanı:** yalnız pozitif mal satırları; iade satırı tabana girmez ve pay almaz (SAT-D467).
   Belge düzeyi değer bundan etkilenmez (TB1).

## Türetilen adımlar

- **TB1 — Belge altı iskontonun tamamı mal tutarından düşülür:** SAT-D465 iskontoyu yalnız mal satırlarına dağıtır; kg'siz
  hizmet satırı pay almaz. Bu yüzden belge düzeyinde dağıtılan payların toplamı belge altı iskonto tutarına eşittir ve
  belge düzeyi değer dağıtımın kuruş yuvarlamasından ve artığın yerinden **bağımsızdır**. Satır payları bilgi için SAT-D407
  ile (her pay kuruşa, artık tutarı en büyük satıra, eşitlikte ilk satıra) gösterilir; bu, belge altı iskonto dağıtımının
  mevcut kuralıdır (SAT-D403/D407) ve SAT-D465'in "tutarla oransal" ifadesiyle aynı tabanı kullanır (B03, B08).
- **TB2 — Vade farkı hariç satır tutarı:** satırın vade farkı hariç tutarı = miktar × iskontolu birim fiyat, belge satırı
  olarak kuruşa bir kez (SAT-D11; net birim fiyat yuvarlanmaz, SAT-D76). Satırlar bu tutarın kuruşta tam çıktığı sayılarla
  seçildi (B04, B08); vade farkı dahil belge satır tutarından hesap "OLMAZ" diye gösterilir.
- **TB3 — Kg'li mal satırında kg yoksa:** SAT-D465 kg'siz **hizmet** satırını dışlar; kg'si olması gereken bir mal
  satırında ilgili kg (teorik veya gerçek) yoksa belge düzeyi gösterilmez (ana golden TK5; kısmi kg toplamı tahmini değer
  olur) (B13).

## Golden satırları — satış

Ortak girdiler (ana golden K01/K06): S1 = 100 adet × 250,00 TL = 25 000,00 TL, `k` 12,345 → teorik 1 234,500 kg.

| # | Durum | Girdiler | Kesin ara değerler | Gösterilen |
|---|---|---|---|---|
| B01 | Tek satır | S1 | 25000 / 1234.5 = 20.25111381… | **20,25 TL/kg** (= K01) |
| B02 | Çok satır | S1 + S2 (50 adet × 80,00 = 4 000,00; `k` 4,321 → 216,050 kg) | 29000 / 1450.55 = 19.99241666… | **19,99 TL/kg** (= K06; satır kg fiyatlarının ortalaması **OLMAZ**) |
| B03 | Belge altı iskonto, oransal dağıtım ve kuruş artığı (TB1) | S1, S2, S3 (kg birimli 50,000 kg × 20,00 = 1 000,00); belge altı iskonto 100,00 TL | Paylar kesin 83,333… / 13,333… / 3,333…; kuruşa 83,33 / 13,33 / 3,33 = 99,99; artık 0,01 en büyük satıra (S1) → **83,34 / 13,33 / 3,33**; net mal 30000 − 100 = 29 900; net kg 1 500,550; 29900 / 1500.55 = 19.92602712… | **19,93 TL/kg** (artığın yerinden bağımsız) |
| B04 | Vade farkı hariç (TB2) | S1, satırda %2 vade farkı (belge satır tutarı vade dahil 25 500,00) | Mal tutarı vade hariç 25 000,00; 25000 / 1234.5 | **20,25 TL/kg** (vade dahil 25500 / 1234.5 → 20,66 **OLMAZ**) |
| B05 | Kg'siz hizmet satırı hariç | S1 + nakliye hizmet satırı 750,00 TL (kg yok) | Hizmet dışlanır: 25000 / 1234.5 | **20,25 TL/kg** (25750 / 1234.5 → 20,86 **OLMAZ**; hizmet satırı artık belge düzeyini gizlemez) |
| B06 | İade satırı düşümü | Fatura: S1 (gerçek kg 1 250,400) + iade satırı 10 adet × 240,00 = 2 400,00 (teorik 123,450 kg; tartılan 125,000 kg) | Net mal 25000 − 2400 = 22 600; teorik net kg 1234.5 − 123.45 = 1 111,050 → 20.34111876…; gerçek net kg 1250.4 − 125 = 1 125,400 → 20.08174871… | Teorik **20,34**, gerçek **20,08 TL/kg** |
| B07 | Belge altı iskonto kg'siz hizmet satırı olan belgede | S1 + nakliye 750,00; belge altı iskonto 257,50 | İskontonun tamamı S1'e (TB1): 25000 − 257.5 = 24 742,5; 24742.5 / 1234.5 = 20.04252733… | **20,04 TL/kg** (iskonto hizmet satırıyla birlikte oransal dağıtılsaydı S1 payı 250,00 → 24750 / 1234.5 = 20,05 **OLMAZ**) |
| B08 | Bütün kurallar birlikte | S1 (vade farkı %2; belgede 25 500,00) + kg birimli S4 500,000 kg × 21,00 = 10 500,00 + nakliye 750,00 + belge altı iskonto 355,00 + iade satırı 10 adet × 240,00 (123,450 kg) | Belge altı paylar (TB1): 355 × 25000/35500 = 250,00; 355 × 10500/35500 = 105,00; net mal 25000 + 10500 − 355 − 2400 = 32 745; net kg 1234.5 + 500 − 123.45 = 1 611,050; 32745 / 1611.05 = 20.32525371… | **20,33 TL/kg** |
| B09 | Adetli ürün kg'si | S1 (adetli; `k` kaydı 12,345) + S2 (adetli; `k` 4,321) | Teorik kg = adet × `k`: 1 234,500 + 216,050 | = B02: **19,99 TL/kg** |
| B17 | İade satırı ve belge altı iskonto birlikte (SAT-D467) | B08 belgesi: S1 25 000,00, S4 10 500,00, iade satırı 2 400,00, belge altı iskonto 355,00 | Dağıtım tabanı yalnız pozitif mal satırları: 25000 + 10500 = 35 500; paylar 355 × 25000/35500 = 250,00 ve 355 × 10500/35500 = 105,00; iade satırı payı 0 | Satır payları **S1 250,00 / S4 105,00 / iade 0,00**; belge düzeyi **20,33 TL/kg** (= B08; iade satırı tabana girseydi taban 37 900 olur, paylar değişirdi, OLMAZ) |
| B10 | Dövizli belge (SAT-D439) | 10 adet × 100,00 USD = 1 000,00 USD; `k` 12,345 → 123,450 kg | 1000 / 123.45 = 8.10044552… (kur yok) | **8,10 USD/kg** |

## Golden satırları — alış

| # | Durum | Girdiler | Kesin ara değerler | Gösterilen |
|---|---|---|---|---|
| B11 | Alış faturası, belge altı iskonto | 200 adet × 45,00 = 9 000,00; `k` 2,500 → teorik 500,000 kg; kabulde tartılan 489,750 kg; belge altı iskonto 90,00 | Net mal 8 910; teorik 8910 / 500 = 17,82; gerçek 8910 / 489.75 = 18.19295558… | Teorik **17,82**, gerçek **18,19 TL/kg** (ana golden K10'un iskontolu hali) |
| B12 | Alış faturası, tedarikçiye iade satırı | B11 faturasında iade satırı 20 adet × 45,00 = 900,00 (teorik 50,000 kg; tartılan 49,870 kg); belge altı iskonto yok | Net mal 9000 − 900 = 8 100; teorik net kg 450,000 → 18; gerçek net kg 489.75 − 49.87 = 439,880 → 18.41411294… | Teorik **18,00**, gerçek **18,41 TL/kg** |

## Gösterilmez / açık

| # | Durum | Sonuç |
|---|---|---|
| B13 | Kg'li mal satırlarından birinin ilgili kg'si yok (TB3) | Belge düzeyi **gösterilmez**; satır düzeyi diğer satırlarda gösterilir |
| B14 | Net kg = 0: S1 (1 234,500 kg) + iade satırı 100 adet (1 234,500 kg) | **Gösterilmez** (formül 4) |
| B15 | Net kg < 0: iade satırlarının kg'si mal satırlarınınkini aşıyor | **Gösterilmez** (formül 4) |
| B16 | Net kg > 0 ama net mal tutarı ≤ 0: S1 (25 000,00; 1 234,500 kg) + iade satırı 50 adet × 520,00 = 26 000,00 (50 × 12,345 = 617,250 kg) → net mal −1 000,00, net kg 617,250 | **Gösterilmez** ("hesaplanamaz"; SAT-D466). Mekanik −1000 / 617.25 değeri yazılmaz |

## Türetilenler

- **TB1:** Belge altı iskontonun tamamı mal tutarından düşülür; satır payları bilgi için SAT-D407 ile, taban SAT-D467'ye
  göre yalnız pozitif mal satırları (B03, B07, B08, B17).
- **TB2:** Vade farkı hariç satır tutarı = miktar × iskontolu birim fiyat, kuruşa bir kez (B04, B08).
- **TB3:** Kg'si olması gereken mal satırında kg yoksa belge düzeyi gösterilmez (B13).

Hiçbiri yeni para/stok/maliyet politikası seçmez. Bir reviewer bir türetmeyi politika seçimi sayarsa ilgili satır
hesaplanamaz kabul edilip `NEEDS-SINAN`'a taşınmalıdır.

## NEEDS-SINAN

Açık nokta yoktur. İlk taslaktaki EB1 SAT-D466 ile (net mal tutarı ≤ 0 → gösterilmez; B16), EB2 SAT-D467 ile (iade satırı
dağıtım tabanına girmez; B17) cevaplandı.

## Sınırlar

- Bu belge kod, test, migration, OpenAPI, Ayarlar verisi veya başka karar dosyası değiştirmez; grant açmaz.
- Satır düzeyi kg fiyatı ana golden Bölüm 1'deki gibidir (satır tutarı ÷ satır kg'si); SAT-D465 yalnız belge düzeyini
  belirler. Belge altı iskontonun satır düzeyi kg fiyatına yansıyıp yansımadığı bu belgede seçilmez.
- `OPEN-*` kapatılmaz; `KARAR_SICILI.md` ve `ACIK_KARARLAR.md` değişmez. İnsan kapıları (deployment, environment,
  secret, Settings → Actions etkinleştirme, grant/rol/Critical, production aktivasyonu, `OPEN-*` kapatma,
  `.claude/settings.json`) değişmez.
