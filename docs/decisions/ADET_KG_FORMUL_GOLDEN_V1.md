# Adet/kg Formül Golden v1 (SAT-D436, ALS-D119, SAT-D438, SAT-D433)

## Kaynak, bağımsızlık ve kabul sınırı

Bu belge **yeni Sinan kararı eklemez**. SAT-D265 (5) ve SAT-D431 yöntemiyle, adet/kg paketinin
(`docs/delivery/ADET_KG_PAKETI_v1.md`, AK-3/AK-5/AK-6/AK-8) dört formül golden'ını kaydeder.

**Kaynak kararlar:**

- `SINAN_KARARLARI_2026-11-21_TREN_KAPANIS.md` (bu belge yazılırken `claude/nifty-franklin-nk76d5` dalında, main'de
  değil): SAT-D433, SAT-D434, SAT-D435, SAT-D436, SAT-D437, SAT-D438, ALS-D119, KLN-D119, URT-D112 ve 14–26. cevaplar;
  bu belgenin ilk sürümündeki NEEDS-SINAN N1–N9 için sorulan 27–35. cevaplar ve SAT-D439, SAT-D440, SAT-D441, SAT-D442,
  SAT-D443, ALS-D120, ALS-D121 ile "Sınırlar"daki belge düzeyi kg fiyatı cümlesi.
- Issue #1189 aktarımları: SINAN-CEVAP 51
  ([7069053648](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-7069053648)), SINAN-CEVAP 52
  ([7069251238](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-7069251238)), K3-B2 kaynak payı cevabı
  ([7036983182](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-7036983182): "Kaynak payı açıkça
  seçilsin"), SINAN-CEVAP 53 ([7070761130](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-7070761130):
  N1–N9 cevapları), SINAN-CEVAP 54 ([7070882941](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-7070882941): SAT-D444 ve SAT-D442 eşitlik sırası). Bağlantılar **ajan aktarımıdır**, insanın doğrudan GitHub yorumu değildir.
- main'deki kilitli kurallar: `SAT_S12_SATIS_MARJI_FORMUL_GOLDEN_V1.md` (G01–G24, D01–D23, T1–T4); SAT-D11, SAT-D259,
  SAT-D261, SAT-D262, SAT-D264, SAT-D265 (EK51/EK52); SAT-D295–SAT-D299 (EK59); SAT-D301, SAT-D306
  (`…_2026-11-11_STOK_MALIYET_VE_TRANSFER.md`); SAT-D371, SAT-D392, SAT-D393, SAT-D396, SAT-D408, SAT-D410; BS-1, BS-9,
  BS-13, BS-16, BS-17 (`…_2026-11-11_BEKLEYEN_SORULAR.md`); URN-D7, URN-D8, URN-D9, URN-D13–URN-D16; KLN-D85–KLN-D88;
  KIM-D45–KIM-D47; URT-D51; SAT-D409 (yalnız emsal olarak anılır).

**Bağımsızlık:** Satırlar, uygulamanın yazarı olmayan ayrı bir Claude oturumunda yalnız yukarıdaki karar metinlerinden,
kesin rasyonel aritmetikle (Python `fractions.Fraction`; gösterim `decimal` `ROUND_HALF_UP`, yani pozitif ve negatif
sayıda yarımda sıfırdan uzağa) hesaplandı. **Ara yuvarlama yoktur**; yuvarlama yalnız aşağıda adı geçen yerlerde bir kez
yapılır. Bu kurallar için yazılmış uygulama kodu, testleri ve beklenen değerleri okunmadı (`claude/adet-kg-*` dallarına
bakılmadı). Proje Unit/SQL/HTTP/Vitest/browser/CI koşumu kapsam dışıdır.

**Kabul sınırı:** Bu belgenin varlığı hiçbir formülü açmaz. İki bağımsız exact-head DOCS review ve main'e alınma gerekir;
uygulama ancak ondan sonra ve bu satırlara eşit çıktığında açılır (SAT-D265 (5), SAT-D431). Son uygulama K3 kapısı ayrıca
sürer. `OPEN-*`, `KARAR_SICILI.md`, `ACIK_KARARLAR.md`, grant/rol/Critical ve production değişmez. "Türetilen" diye
işaretli her adım karar metninde açıkça yazmayan ve mevcut kilitli kurallardan gerekçesiyle çıkarılan adımdır;
gerçekten karara bağlanmamış noktalar `NEEDS-SINAN` diye listelenir ve o satırlar **hesaplanamaz** bırakılır. Para,
stok veya maliyet politikası seçilmez.

**Sürüm notu (aynı PR):** İkinci head'de, ilk head'deki N1 ve N3–N9 Sinan'ın cevaplarına (SINAN-CEVAP 53) göre
hesaplandı veya kapsam dışı işaretlendi; N2 "Bunu bilmiyorum" cevabıyla açık kaldı. Üçüncü head'de SINAN-CEVAP 54
(36–37. sorular) işlendi: miktar iadesinin maliyeti ve fiyatı için SAT-D444 (ikinci head'deki TI6 türetmesinin yerine
geçer) ve SAT-D442'nin eşitlik sırası (N10 kapandı). Yeni kararların metni yalnız karar dosyasından ve #1189
aktarımından okundu; uygulama kodu okunmadı.

**Ortak gösterim:** Para ve kg fiyatı 0,01; maliyet/kg (iç hassasiyet) 0,0001; yüzde 0,01; hepsi yarımda sıfırdan
uzağa (SAT-D261, SAT-D265 (2)–(4), SAT-D436). Neden adları (ör. `TheoreticalKgUnavailable`) açıklayıcıdır; sözleşme
adı uygulamanındır, golden yalnız "gösterilemez/hesaplanamaz" sonucunu ve sıfır/tahmini değer yazılmamasını sabitler.

---

## 1. SAT-D436 — kg fiyatı göstergesi (satış ve alış; sipariş ve fatura)

### Formül

1. **Teorik kg/birim** (`k`): sipariş onayında belgeye kaydedilen değer (SAT-D437, KIM-D47); kart sonradan değişse de
   belgedeki değer kullanılır. Kg birimli satırda dönüşüm yoktur.
2. **Teorik kg** = ticari miktar × `k` (SAT-D437).
3. **Teorik kg fiyatı** = tutar ÷ teorik kg (kesin).
4. **Gerçek kg fiyatı** = faturalanan tutar ÷ tartılan kg (satışta kantar, alışta kabul tartımı) (kesin).
5. **Belge düzeyi** = satır tutarlarının toplamı ÷ ilgili kg toplamı (kesin).
6. Gösterim 0,01 TL/kg, yarımda sıfırdan uzağa (SAT-D436, 24. cevap). Bu bir maliyet hesabı değildir; tutarı
   değiştirmez (SAT-D434, SAT-D435).
7. **Dövizli belge:** kg fiyatı belge para biriminde gösterilir (USD belgede USD/kg); kur kullanılmaz (SAT-D439).
8. **Belge düzeyi yalnız bütün satırlar kg'li mal satırıyken gösterilir; satır düzeyi her zaman gösterilir** (karar
   dosyası "Sınırlar", 28. soru açık).
9. **Metre gibi ondalıklı ticari birimler bu trende açılmaz** (SAT-D443); bu bölüm adet ve kg birimli satırları kapsar.

### Türetilen adımlar

- **TK1 — Pay (tutar):** belgedeki **KDV hariç** satır net tutarı (satır iskontosundan sonra, SAT-D11 ile kuruşa bir kez
  yuvarlanmış). Gerekçe: satır adet × adet fiyatıdır (SAT-D434); fiyat listeleri ve satır fiyatları KDV hariçtir
  (URN-D26); para göstergelerinin mevcut tabanı KDV hariç net tutardır (SAT-D259 (2)). KDV dahil tutardan hesap
  K11'de "OLMAZ" diye gösterilir.
- **TK2 — Teorik kg'nin hassasiyeti:** `k`, belgeye kaydedildiği değerle **aynen** kullanılır (URN-D8 "görünen =
  hesaplanan", KLN-D88; varsayılan 3 ondalık, `CoefficientDecimals` Ayarlar'dadır). Satırın teorik kg'si, belge satırı
  kg değeri olarak URN-D9 ile 3 ondalığa (yarımda sıfırdan uzağa) yazılır ve kg fiyatı **bu belge kg'sinden** hesaplanır
  (URN-D9: "UI/rapor/form aynı sonucu kullanır"). Adet satırlarında miktar tam sayı ve `k` 3 ondalıklı olduğundan bu
  yuvarlama değer değiştirmez; yalnız metre gibi ondalıklı miktarlarda etkilidir (K07, SAT-D443 ile bu trende kapsam
  dışı).
- **TK3 — Ara yuvarlama yok:** BS-13'ün "belgedeki kg toplamı 2 ondalık gösterilir" kuralı yalnız kg gösterimidir;
  kg fiyatının paydasına girmez (K09). Kg fiyatı tek bölmeden sonra bir kez yuvarlanır.
- **TK4 — Belge düzeyi:** Σ tutar ÷ Σ kg kesin hesaplanır; satır kg fiyatlarının (yuvarlanmış veya kesin) ortalaması
  değildir (K03, K06). Kg birimli satırın teorik kg'si miktarının kendisidir (K08).
- **TK5 — Eksik kg'de belge düzeyi (artık karar metnidir):** İlk sürümde türetilen "eksik kg'de belge düzeyi gösterilmez"
  adımı, karar dosyasının "Sınırlar" cümlesiyle karşılanır: belge düzeyi kg fiyatı yalnız **bütün satırlar kg'li mal
  satırı** olduğunda gösterilir. Türetilen kalan kısım: kg'li mal satırında ilgili kg (teorik veya gerçek) yoksa o satır
  bu tür için kg'li sayılmaz ve o belge düzeyi gösterilmez (K17). Belge altı iskonto, vade farkı, kg'siz hizmet ve iade
  satırlarının nasıl gireceği açıktır (NEEDS-SINAN N2).
- **TK9 — Dövizli gösterim hassasiyeti:** SAT-D439 kuru kaldırır; 24. cevabın "0,01, yarımda sıfırdan uzağa" kuralı
  belge para biriminin 0,01'ine uygulanır. 2 ondalıklı olmayan para birimleri (JPY, KWD) marj golden'ı D bölümündeki gibi
  kapsanmaz.
- **TK6 — Gerçek kg ve gerçekleşme:** Gerçek kg fiyatının paydası, faturalanan satıra yazılan gerçek kg'dir (SAT-D434:
  gerçek kg faturada bilgi olarak yazılır; alışta kabulde tartılan kg). Sipariş gerçekleşmesi = Σ faturalanan tutar ÷ Σ
  faturalanan satırların gerçek kg'si; sevk edilmiş ama faturalanmamış miktar dahil edilmez ("faturalanan tutar").
  Faturalanan miktarın teorik kg'si = faturalanan miktar × sipariş satırına kaydedilmiş `k` (SAT-D437).
- **TK7 — Onay öncesi:** Kaydedilmiş `k` yoktur; teorik kg fiyatı gösterilmez. Gerekçe: 23. cevapta "her gösterimde
  karttan canlı" seçeneği seçilmedi.
- **TK8 — "Teorik farkı":** golden iki değeri kesin verir: kg farkı = gerçek kg − teorik kg ve işaretli sapma =
  (gerçek − teorik) / teorik × 100 (URN-D7 gösterimi, BS-17 tabanı). Bu bilgi göstergesinin ekran biçimi uygulama
  ayrıntısıdır; TL/kg farkı gösterilecekse iki kesin kg fiyatının farkından bir kez yuvarlanır.

### Golden satırları

Ortak girdiler: satış satırı 100 adet × 250,00 TL = 25 000,00 TL (KDV hariç), `k` = 12,345 kg/adet → teorik kg 1 234,500.

| # | Durum | Girdiler | Kesin ara değerler | Gösterilen |
|---|---|---|---|---|
| K01 | Sipariş satırı, henüz fatura yok | 25 000,00 TL; teorik kg 1 234,500 | 25000/1234.5 = 20.25111381… | Teorik **20,25 TL/kg**; gerçek: gösterilemez (faturalanmamış, TK6) |
| K02 | Kısmi faturalama: 40 adet faturalandı | Fatura tutarı 40 × 250,00 = 10 000,00; teorik kg 40 × 12,345 = 493,800; kantar 500,160 kg | Teorik 10000/493.8 = 20.25111381…; gerçek 10000/500.16 = 19.99360204…; kg farkı +6,360; sapma +1.28797083…% | Fatura satırı teorik **20,25**, gerçek **19,99 TL/kg**. Sipariş gerçekleşmesi: 40/100 adet, 10 000,00 TL, gerçek kg 500,160, gerçek kg fiyatı **19,99**, fark +6,360 kg (sapma kesin +1.28797083…%, TK8); sipariş teorik kg fiyatı K01 gibi 20,25 |
| K03 | Kalan 60 adet ikinci faturada | 15 000,00 TL; kantar 748,900 kg | Fatura 2 gerçek 15000/748.9 = 20.02937641…; sipariş Σ 25000/(500.160+748.900=1249.060) = 20.01505131…; fark +14,560 kg, +1.17942486…% | Fatura 2 gerçek **20,03**; sipariş gerçekleşmesi 100/100 adet, gerçek kg fiyatı **20,02** (satır kg fiyatlarının ortalaması (19,99+20,03)/2 = 20,01 **OLMAZ**, TK4) |
| K04 | Kantar sapması (SAT-D435) | 100 adet tek faturada; kantar 1 209,810 kg | Tutar **25 000,00 sabit**; gerçek 25000/1209.81 = 20.66440184…; sapma −2% | Tutar 25 000,00 (değişmez); teorik **20,25**, gerçek **20,66 TL/kg** |
| K05 | Yarım sınırı | 10 adet × 123,45 = 1 234,50; `k` 10,000 → 100,000 kg | 1234.50/100 = 12.345 (tam yarım) | **12,35 TL/kg** (yarımda çifte yuvarlamayla çıkan 12,34 **OLMAZ**) |
| K06 | Belge düzeyi, iki adet satırı | S1 = K01 (25 000,00; 1 234,500 kg); S2 = 50 adet × 80,00 = 4 000,00, `k` 4,321 → 216,050 kg | S2 4000/216.05 = 18.51423281…; belge 29000/1450.55 = 19.99241666… | S1 20,25; S2 **18,51**; belge teorik **19,99 TL/kg** (satır ortalaması 19,38 **OLMAZ**) |
| K07 | **Kapsam dışı (SAT-D443: metre bu trende açılmaz).** Yalnız TK2'nin ileride metre açılırsa sınanacak okumasını gösterir; bu trende golden değildir | 12,5 m × 100,08 = 1 251,00; `k` 1,235 kg/m | Kesin kg 15.4375 → belge satırı kg (URN-D9) 15,438; 1251/15.438 = 81.03381267… (15,4375 ile 81.03643724…) | Bu trende gösterilmez (metre satırı açılmaz) |
| K08 | Belge düzeyi, adet + kg satırı | S1 = K01; S2 = 500,000 kg × 21,00 = 10 500,00 (kg birimli; teorik kg = 500,000) | 35500/1734.5 = 20.46699336… | Belge teorik **20,47 TL/kg** |
| K09 | 2 ondalık kg gösterimi paydaya girmez (TK3) | 1 adet × 1 234,57; `k` 100,005 | 1234.57/100.005 = 12.34508274…; (2 ondalığa yuvarlanmış 100,01 ile 12.34446555…) | **12,35 TL/kg** (12,34 **OLMAZ**) |
| K10 | Alış siparişi ve faturası | 200 adet × 45,00 = 9 000,00; `k` 2,500 → 500,000 kg; kabulde tartılan 489,750 kg; fatura 200 adet 9 000,00 | Teorik 9000/500 = 18; gerçek 9000/489.75 = 18.37672281… | Teorik **18,00**, gerçek **18,38 TL/kg** |
| K11 | KDV hariç taban (TK1) | K01, KDV %20 → KDV dahil 30 000,00 | 25000/1234.5 = 20.25111381… (30000/1234.5 = 24.30133657…) | **20,25 TL/kg** (24,30 **OLMAZ**) |
| K12 | Satır iskontosu | 100 adet × 250,00, %10 satır iskontosu → 22 500,00 | 22500/1234.5 = 18.22600243… | **18,23 TL/kg** |
| K16 | Dövizli belge (SAT-D439) | 10 adet × 100,00 USD = 1 000,00 USD; `k` 12,345 → 123,450 kg; siparişte USD/TRY 34,5678 dondurulmuş | 1000/123.45 = 8.10044552… (kur kullanılmaz) | **8,10 USD/kg** (kurla çevrilmiş 1000×34.5678/123.45 → 280,01 TL/kg **OLMAZ**) |
| K19 | Dövizli faturada gerçek kg fiyatı (SAT-D439) | Fatura 4 adet × 100,00 USD = 400,00 USD; teorik kg 49,380; kantar 50,016 kg; fatura kuru ne olursa olsun | Teorik 400/49.38 = 8.10044552…; gerçek 400/50.016 = 7.99744082… | Teorik **8,10**, gerçek **8,00 USD/kg** |

Gösterilemez (sıfır veya tahmini değer yazılmaz; sıfıra bölme yapılmaz):

| # | Durum | Sonuç |
|---|---|---|
| K13 | Onay öncesi taslak veya kart kg kaynağı olmayan satır: kaydedilmiş `k` yok (TK7, SAT-D437) | Teorik kg fiyatı gösterilemez (`TheoreticalKgUnavailable`) |
| K14 | Faturalanmış satırda gerçek kg yazılmamış (tartım yok) | Gerçek kg fiyatı gösterilemez (`WeighedKgUnavailable`); tutar ve teorik değer etkilenmez |
| K15 | Teorik veya gerçek kg = 0 | İlgili kg fiyatı gösterilemez (`ZeroKg`) |
| K17 | Belge düzeyi; satırlardan biri kg'li mal satırı değil veya ilgili kg'si yok ("Sınırlar", TK5) | Belge düzeyi gösterilemez; bütün satırların satır düzeyi gösterilir |
| K18 | Belge altı iskonto, vade farkı, kg'siz hizmet satırı (nakliye) veya iade satırı olan belge | Belge düzeyi **hesaplanamaz — NEEDS-SINAN N2** (28. soru "Bunu bilmiyorum"); satır düzeyi gösterilir |

---

## 2. ALS-D119 — alıştan gelen stok maliyeti

### Formül

1. **Giriş maliyeti/kg** = ödenen tutar ÷ kabulde tartılan gerçek kg (ALS-D119); SAT-D298'in teorik kg (kart katsayısı)
   paydasının yerine geçer.
2. İç hassasiyet 4 ondalık, yarımda sıfırdan uzağa (SAT-D265 (2)); ALS-D120 ile bu yuvarlama yalnız ortalamanın sonucunda
   yapılır, giriş maliyeti/kg'nin 4 ondalıklı değeri gösterimdir.
3. Hareketli ortalamaya giriş (SAT-D256, SAT-D257 depo ve `CompanyId` kapsamı): yeni ortalama = (Q₀ × A₀ + giriş
   tutarı) ÷ (Q₀ + q). Giriş tutarı **kesin** taşınır; giriş maliyeti/kg ara adımda yuvarlanmaz, 4 ondalık iç hassasiyet
   yalnız sonuçta (ortalamada) uygulanır (ALS-D120). Giriş maliyeti/kg'nin 4 ondalıklı değeri yalnız gösterimdir.
4. **Kısmi fatura:** faturalanan adede düşen kg = kabulde tartılan kg × faturalanan adet ÷ kabul adedi (ALS-D121; TL ve
   dövizli kabul).
5. Alış iadesi (SAT-D306): çıkış, iade edilen asıl kabul satırının maliyet/kg'siyle; kalan stoğun ortalaması yeniden
   hesaplanır.

### Türetilen adımlar

- **TA1 — Tutar mevcut zincirden gelir, yalnız payda değişir:** ALS-D119 metni "SAT-D298'in teorik kg kısmının yerine
  geçer" der; tutar kaynağı değişmez. Ödenen tutar = kabul edilen ticari miktar × alış sipariş satırı birim fiyatı
  (SAT-D295 (1)); dövizli satırda TL karşılığı tedarikçi faturasındaki resmî kurla, fatura gelene kadar `Unavailable`
  (BS-1). Navlun/gümrük girmez (SAT-D302).
- **TA2 — Döviz çevriminde ara yuvarlama yok** (marj golden'ı T1 emsali); satırdaki sayılar 2 ondalıkta tam çıkacak
  biçimde seçildi, iki okuma aynı sonucu verir.
- **TA3 — Tartılan kg yoksa veya sıfırsa** maliyet hesaplanmaz ve ortalamaya girmez (SAT-D298 (3)'ün "katsayı yoksa
  NoSource, tahmini katsayı yok" kuralının benzeri; teorik kg yedek olarak kullanılmaz). Sıfır tutar SAT-D297 ile
  `Unreadable` (`ZeroCostReceipt`).
- **TA4 — Kantar toleransı yalnız uyarıdır:** sapma = |tartılan − teorik| / teorik × 100, sınır dahil `<=` %1
  (BS-9, BS-16, BS-17, URN-D7). Tolerans dışı olsa da maliyet tartılan kg'den hesaplanır; teorik kg'ye geri dönülmez.
- **TA5 — İade çıkışının kg'si** iade edilen etiketin tartılmış kg'sidir; çıkış tutarı = iade kg × kabul maliyeti/kg
  (SAT-D306 (1)), kesin. ALS-D120 giriş maliyeti/kg'yi ara adımda yuvarlamadığı için kabul maliyeti/kg kabulün kesin
  oranıdır (tutar ÷ tartılan kg). A10'da bu oran ile 4 ondalıklı gösterim değeri ve havuz değerinin Q × A₄ ya da kesin
  toplam olarak tutulması aynı gösterilen sonucu verir; satır bu seçimlere duyarlı değildir.

### Golden satırları

Ortak kabul R1: 100 adet × 250,00 TL = 25 000,00 TL, kart `k` 12,345 → teorik 1 234,500 kg; kabulde tartılan 1 250,400 kg.

| # | Durum | Girdiler | Kesin ara değerler | Gösterilen / sonuç |
|---|---|---|---|---|
| A01 | Temel (R1) | 25 000,00 TL; 1 250,400 kg | 25000/1250.4 = 19.99360204… | **19,9936 TL/kg** (SAT-D298 teorik paydası 25000/1234.5 → 20,2511 **OLMAZ**) |
| A02 | Yarım sınırı | 246,89 TL; 200,000 kg | 246.89/200 = 1.23445 (tam yarım) | **1,2345 TL/kg** (çifte yuvarlamayla 1,2344 **OLMAZ**) |
| A03 | Tartılan = teorik | 25 000,00; 1 234,500 kg | 20.25111381… | **20,2511** (yalnız bu durumda eski kuralla aynı) |
| A04 | Teorik ile gerçek farkı, tolerans dışı (TA4) | R1 | Sapma (1250.4−1234.5)/1234.5×100 = +1.28797083…% > %1 | Uyarı; maliyet yine **19,9936** |
| A05 | Tolerans sınırı tam %1 (BS-16) | 25 000,00; tartılan 1 246,845 = 1 234,5 × 1,01 | Sapma tam %1 → kabul (`<=`); 25000/1246.845 = 20.05060773… | Uyarı yok; **20,0506** |
| A06 | Ortalama havuzuna giriş (ALS-D120) | Depo havuzu 500,000 kg @ 20,1234; giriş R1 25 000,00 TL / 1 250,400 kg | (500×20.1234 + 25000)/1750.4 = 20.03067870… | Yeni ortalama **20,0307** |
| A07 | Dövizli alış, fatura geldi (BS-1) | 100 adet × 7,50 USD = 750,00 USD; tedarikçi faturası resmî kuru 34,5678; tartılan 1 250,400 kg | TL 750×34.5678 = 25 925,85; 25925.85/1250.4 = 20.73404510… | **20,7340 TL/kg** |
| A08 | Dövizli alış, fatura henüz yok | A07, fatura yok | — | TL maliyet **Unavailable** (BS-1); geçici kur yok; ortalamaya girmez |
| A09 | Kısmi fatura, TL sipariş (ALS-D121) | R1; tedarikçi faturası önce 40 adet (10 000,00 TL, sipariş fiyatıyla aynı) | Faturalanan kg payı 1250.4×40/100 = 500,160; maliyet tutarı sipariş satırından (TA1): 10000/500.16 = 19.99360204… | Faturalı pay 500,160 kg; kabulün tamamı **19,9936**; kısmi fatura maliyeti değiştirmez |
| A10 | Tedarikçi iadesi (SAT-D306) | A06 sonrası havuz 1 750,400 kg @ 20,0307; R1'den 20 adetlik etiket iade, tartılmış 249,870 kg | Çıkış 249.87×25000/1250.4 = 4 995,80134357… (4 ondalıklı 19,9936 ile 4 995,800832); yeni ortalama (1750.4×20.0307 − 4995.80134357…)/1500.53 = 20.03687759… (diğer okumalar 20.03685274… / 20.03687793…) | Çıkış tutarı gösterim **4 995,80 TL**; kalan ortalama **20,0369** |
| A13 | Dövizli kabulün kısmi faturası (ALS-D121, BS-1, ALS-D120) | A07 kabulü (100 adet, 1 250,400 kg, 7,50 USD/adet); önce 40 adet faturalandı, resmî kur 34,5678 | Faturalı kg payı 1250.4×40/100 = 500,160; TL tutar 40×7.50×34.5678 = 10 370,34; 10370.34/500.16 = 20.73404510… | Faturalı pay **500,160 kg / 10 370,34 TL** (gösterim 20,7340 TL/kg); bekleyen 60 adet / 750,240 kg TL maliyeti **Unavailable** (BS-1); bekleyen payın havuza etkisi mevcut Unavailable kuralınındır, bu golden seçmez |
| A14 | Kesin tutar taşınır (ALS-D120) | Havuz 2,000 kg @ 10,0000; giriş 1 000,01 TL / 7,000 kg | (2×10 + 1000.01)/9 = 113.33444444… (giriş maliyeti 142,8586'ya yuvarlanıp girilseydi 113.33446666… → 113,3345) | Yeni ortalama **113,3344** (113,3345 **OLMAZ**) |

Hesaplanamaz:

| # | Durum | Sonuç |
|---|---|---|
| A11 | Kabulde tartım yok veya tartılan kg 0 (TA3) | Maliyet hesaplanamaz, ortalamaya girmez; teorik kg yedeği yok |
| A12 | Sıfır tutarlı (bedelsiz) giriş | `Unreadable` (`ZeroCostReceipt`, SAT-D297); ortalamaya girmez |

---

## 3. SAT-D438 — adetli satırda marj

### Formül (marj golden'ının G01–G15 adımlarına tek dönüşüm eklenir)

1. NetSatış: G tanımındaki gibi (KDV hariç, iskonto sonrası, vade farkı öncesi, kuruşa bir kez yuvarlanmış; SAT-D11).
   Dövizli satışta NetSatış_TL = NetSatış_döviz × dondurulmuş sipariş kuru (SAT-D426, marj golden'ı T1–T4).
2. Maliyet/kg (`c`): sipariş anında Stok portundan okunan ve satıra anlık görüntü olarak yazılan TL değer (SAT-D262 (4),
   SAT-D264), ≤ 4 ondalık (G16/G17 bu değere uygulanır).
3. **Maliyet/birim = `c` × satıra kaydedilen teorik kg/birim `k`** (SAT-D438, SAT-D437).
4. Maliyet = maliyet/birim × miktar (kesin). Marj = NetSatış − Maliyet (kesin). Yüzde = Marj × 100 / NetSatış (kesin,
   yuvarlanmamış marjdan; SAT-D265 (1)).
5. Gösterim: tutar 0,01 TL, yüzde 0,01; NetSatış ≤ 0 ise yüzde kullanılamıyor (NetSaleNotPositive).

### Türetilen adımlar

- **TM1 — Yuvarlama yeri:** Maliyet/birim **yuvarlanmaz**; yuvarlama yalnız gösterimde bir kez yapılır. Gerekçe:
  SAT-D261 (2) "yeni bir yuvarlama kuralı açılmaz"; SAT-D265 yalnız gösterimde (3) ve ortalamanın iç hassasiyetinde
  (2) yuvarlama tanımlar; BS-13 "ara adımlarda yuvarlama yoktur"; marj golden'ı "Marj (kesin)" ve T1 aynı yolu izler.
  G16'nın "≤ 4 ondalık" sınırı porttan gelen `c`'ye uygulanır; `c × k` 4 ondalığı aşabilir ve bu CostInvalid değildir.
  M02, ara 4 ondalık yuvarlamanın yanlış sonucunu "OLMAZ" diye gösterir.
- **TM2 — Gerçek kg marja girmez:** SAT-D438 "belgeye kaydedilen teorik kg/adet" der; kantar sapması tutarı değiştirmez
  (SAT-D435) ve maliyet de değişmez (M07).
- **TM3 — Kg birimli satır:** dönüşüm yoktur; G01–G15 aynen (M04).
- **TM4 — Kayıtlı değer:** kart sonradan değişse de kayıtlı `k` kullanılır (SAT-D437, KIM-D47) (M10); `k`'nin ondalık
  sayısı şirket ayarına göre 3'ten fazla olabilir ve aynen kullanılır (URN-D8) (M09).

### Golden satırları

Ortak girdiler: `c` = 19,9936 TL/kg (A01), `k` = 12,345 kg/adet → maliyet/adet 19.9936 × 12.345 = 246,820992 (kesin).

| # | Durum | NetSatış | `c` | `k` | Miktar | Maliyet (kesin) | Marj (kesin) | Tutar | Yüzde (kesin → gösterilen) |
|---|---|---|---|---|---|---|---|---|---|
| M01 | Temel adet satırı | 25 000,00 | 19,9936 | 12,345 | 100 | 24 682,0992 | 317,9008 | **317,90** | 1.2716032 → **1,27** |
| M02 | Ayırt edici: maliyet/adet yuvarlanmaz (TM1) | 250 000,00 | 19,9936 | 12,345 | 1 000 | 246 820,992 | 3 179,008 | **3 179,01** (maliyet/adet 246,8210'a yuvarlanırsa çıkan 3 179,00 **OLMAZ**) | 1.2716032 → **1,27** |
| M03 | Yarım sınırı | 10,00 | 3,3300 | 1,500 | 1 | 4,995 | 5,005 | **5,01** | 50.05 → **50,05** |
| M04 | Kg birimli satır (TM3) | 21 500,00 | 20,1234 | — (kg) | 1 000,000 kg | 20 123,4 | 1 376,6 | **1 376,60** | 6.40279069… → **6,40** |
| M05 | **Kapsam dışı (SAT-D443: metre bu trende açılmaz)**; bilgi için | 1 680,00 (12 m × 140,00) | 20,1234 | 1,235 kg/m | 12 | 298,228788 | 1 381,771212 | bu trende gösterilmez | (82.24828642…) |
| M06 | NetSatış ≤ 0 | 0,00 | 19,9936 | 12,345 | 2 | 493,641984 | −493,641984 | **−493,64** | kullanılamıyor (NetSaleNotPositive) |
| M07 | Kantar sapması marjı değiştirmez (TM2) | 25 000,00; kantar 1 209,810 kg | 19,9936 | 12,345 | 100 | 24 682,0992 | 317,9008 | **317,90** | **1,27** (= M01) |
| M08 | Dövizli adet satırı | 100 adet × 10,00 USD = 1 000,00 USD; dondurulmuş kur 34,5678 → 34 567,80 TL (kesin) | 19,9936 | 12,345 | 100 | 24 682,0992 | 9 885,7008 | **9 885,70** | 28.59800392… → **28,60** |
| M09 | `k` 4 ondalıklı (Ayarlar `CoefficientDecimals` = 4) | 2 500,00 | 19,9936 | 12,3457 | 10 | 2 468,3498752 | 31,6501248 | **31,65** | 1.266004992 → **1,27** |
| M10 | Kart onaydan sonra 12,400'e değişti (TM4) | 25 000,00 | 19,9936 | kayıtlı **12,345** | 100 | 24 682,0992 | 317,9008 | **317,90** | **1,27** (= M01) |

Hesaplanamaz (tutar ve yüzde yok; sıfır/tahmini değer yazılmaz):

| # | Durum | Sonuç |
|---|---|---|
| M11 | Satıra teorik kg/birim kaydedilmemiş (onay öncesi önizleme, kart kaynağı yok, eski satır) | `UnitConversionUndecided` (G19 bu durum için sürer) |
| M12 | `c` = 5,00001 (> 4 ondalık) | `CostInvalid` (G16) |
| M13 | `c` = 0 | Maliyet okunamıyor (G18 sonraki kullanıcı cevabı) |
| M14 | Maliyet portu NoSource/Unreadable/ProducedItem/MixedSourceItem | İlgili neden (G21 ve onun geçiş notu) |
| M15 | Metre satırında miktar × `k` 3 ondalığı aşıyor: 12,5 m × 1,235 = 15,4375 kg; NetSatış 1 750,00; `c` 20,1234 | **Kapsam dışı (SAT-D443)**: metre bu trende açılmaz. Metre açılırken ayrı karar gerekir; iki okuma 1 439,3450125 → 1 439,35 (birim başına) ve 1 439,3349508 → 1 439,33 (URN-D9 belge kg'si) olarak kayıtlıdır |

---

## 4. SAT-D433 — bölünmüş/birleştirilmiş etiketli tedarikçi iadesi

### Formül

1. **Soy ağacı payı:** etiketin kg'sinin her alış kabulüne kadar izlenen payı (SAT-D433, 26. cevap).
2. **Kabul başına iade kg'si** = iade kg × kabulün soy ağacı payı.
3. Her kabul içinde faturalı / fatura bekleyen payı **hazırlayan açıkça seçer** (K3-B2, SAT-D433); sistem tahminle pay
   atamaz.
4. **Maliyet tutarı** = Σ (kabul başına iade kg'si × o kabulün maliyet/kg'si) (SAT-D306 (1), ALS-D119); kalan stoğun
   ortalaması yeniden hesaplanır (SAT-D306 (2)).
5. **Dağıtım artığı:** kabul başına kg 3 ondalığa indirildiğinde kalan 0,001 kg'lık artık **en büyük paya**, eşitlikte
   kabul sırasına göre ilkine yazılır (SAT-D440).
6. **Adet dağıtımı (adetli etiket, KLN-D119):** iade adedi kabullere orantılı dağıtılırken her pay **aşağı** tam sayıya
   yuvarlanır, kalan adet en büyük paya yazılır; en büyük paylar eşitse kabul sırasına göre ilkine; kg dağıtımı ayrıca
   orantılı kalır (SAT-D442, 37. cevap).
7. **Miktar iadesi (SAT-D441, SAT-D444):** etiketin soyuna sınır konmaz; soyunda o tedarikçinin kabulü olmayan (başka
   tedarikçi, üretim, açılış, müşteri iadesi) etiketten de iade yapılabilir. İade, hazırlayanın seçtiği o tedarikçinin
   kabulünden (açık miktarı kadar) düşülür. Stok çıkışı o depo/ürünün **ortalama kg maliyetiyle** yapılır; iadenin
   tedarikçiye **fatura birim fiyatı, iadenin düşüldüğü seçilen alış siparişinin birim fiyatıdır**; aradaki fark mevcut
   iade farkı kuralıyla (SAT-D392/D393, farklı kişi onayı) kaydedilir (SAT-D444).
8. Fark kontrolü: iade maliyeti ilgili siparişteki ilgili faturanın birim maliyetiyle karşılaştırılır (SAT-D392); fark
   varsa iade reddedilir (SAT-D371); fark okunamazsa SAT-D393/D396/D408 istisna yolu, onaylı istisna Stok 409'unu aşmaz
   (SAT-D410).

### Türetilen adımlar

- **TI1 — Payın hesabı:** pay kesin kesirdir. Bölmede alt etiketler üst etiketin paylarını aynen taşır; birleştirmede
  sonuç etiketin payı, kaynak etiketlerin kg'leriyle ağırlıklı ortalamadır. Gerekçe: "soy ağacı payına orantılı"
  (SAT-D433) ifadesinin tek kg-korunumlu okuması; mevcut soy ağacı kayıtları kenar başına net kg taşır.
- **TI2 — Kesin hesap, tek gösterim:** kabul başına kg ve maliyet tutarı kesin hesaplanır; maliyet tutarı gösterimde
  0,01 TL'ye bir kez yuvarlanır (SAT-D261). Stok hareket değerinin saklama hassasiyeti mevcut defter kuralınındır, bu
  golden onu seçmez.
- **TI3 — Kabul sınırı:** bir kabule düşen iade kg'si o kabulden alınan − önceki iadeleri aşarsa iade **reddedilir**
  (SAT-D306 (a) okuması); aşan kg başka kabule kaydırılmaz (kaydırma "orantılı" kuralını bozar). Hazırlayanın faturalı /
  bekleyen seçimi de o payın kalanını aşamaz.
- **TI4 — Dövizli kabulde bekleyen pay:** fatura gelmeden TL maliyet `Unavailable` olduğundan (BS-1) bekleyen paydan
  seçilen kg'nin maliyeti, dolayısıyla iadenin toplam maliyet tutarı `Unavailable` okunur (SAT-D306 (b)); kısmi toplam
  gösterilmez. Devam yolu SAT-D393/D396/D408/D410'dur.
- **TI5 — TL kabulde faturalı ve bekleyen pay aynı maliyet/kg'yi taşır** (TA1: tutar sipariş satırından); seçim maliyet
  tutarını değiştirmez, yalnız SAT-D392 karşılaştırmasının hangi faturayla yapılacağını belirler.
- **TI6 — (ikinci head'deki türetme kaldırıldı):** "Etiketin kendi maliyeti" artık karar metnidir: o depo/ürünün
  ortalama kg maliyeti (SAT-D444). Ortalama, iade anındaki depo/ürün hareketli ortalamasıdır (SAT-D256, SAT-D257);
  golden onu girdi olarak alır.
- **TI7 — Karışık soy (X payı + X dışı pay):** X'e iade edilen kg'nin X kabullerinden gelen soy payı SAT-D433 ile o X
  kabullerine orantılı dağıtılır ve SAT-D306 (1) gereği o kabulün maliyet/kg'siyle çıkar; X dışı pay SAT-D441/SAT-D444
  ile hazırlayanın seçtiği X kabulünden düşülür, depo/ürün ortalamasıyla çıkar ve seçilen siparişin birim fiyatıyla
  faturalanır. Gerekçe: iki kararın kapsamları birbirini dışlar (SAT-D433 "etiketin içindeki her alış kabulü";
  SAT-D441 "soyunda o tedarikçinin kabulü olmayan" kısım); 32. cevap "kesin bir sınır koymayalım" der. Bir reviewer
  bu bileşimi politika seçimi sayarsa I12 hesaplanamaz sayılmalıdır.
- **TI8 — Fark (miktar iadesi):** fark = iade kg × (depo/ürün ortalaması/kg − seçilen alış siparişinin birim fiyatı/kg),
  kesin; işaret yönü ve kaydı mevcut kuraldır (SAT-D371/D392/D393/D396/D408/D410). SAT-D371'in "Finans spec'i gelene
  kadar fark içeren iade reddedilir" şartı bu belgeyle değişmez. Satırlarda seçilen sipariş kg birimlidir; adet
  birimli seçilen siparişte iade belgesinin adet miktarı bu golden'da satır olarak verilmez.

### Golden satırları

Ortak kabuller (tedarikçi X, aynı depo, TL; kabul sırası R1, R2, R3): R1 11 996,16 TL / 600,000 kg = 19,9936 TL/kg;
R2 8 576,00 / 400,000 = 21,4400; R3 2 812,50 / 150,000 = 18,7500 (oranlar tam; ALS-D120 okumasına duyarlı değildir).
Soy ağacı: L1 (R1'den 600,000 kg) + L2 (R2'den 400,000 kg) → birleştirme L3 1 000,000 kg (R1 3/5, R2 2/5); L3 bölme →
L4 300,000 kg + L5 700,000 kg; L5 bölme → L6 350,000 kg + …; L6 + L7 (R3'ten 150,000 kg) → birleştirme L8 500,000 kg.

| # | Durum | Girdiler | Kesin ara değerler | Gösterilen / sonuç |
|---|---|---|---|---|
| I01 | Birleştir-böl etiketinin tamamı | L4 300,000 kg iade (R1 3/5, R2 2/5) | R1 180 kg × 19.9936 = 3 598,848; R2 120 kg × 21.44 = 2 572,8; toplam 6 171,648 | R1 **180,000 kg**, R2 **120,000 kg**; maliyet **6 171,65 TL** |
| I02 | Etiketin bir kısmı | L4'ten 250,000 kg | R1 150 × 19.9936 = 2 999,04; R2 100 × 21.44 = 2 144; toplam 5 143,04 | R1 **150,000**, R2 **100,000 kg**; **5 143,04 TL** |
| I03 | İki kat soy ağacı, üç kabul | L8 (R1 350×3/5/500 = 21/50, R2 7/25, R3 150/500 = 3/10); 100,000 kg iade | R1 42 × 19.9936 = 839,7312; R2 28 × 21.44 = 600,32; R3 30 × 18.75 = 562,5; toplam 2 002,5512 | R1 **42,000**, R2 **28,000**, R3 **30,000 kg**; **2 002,55 TL** |
| I04 | Kabul içinde faturalı/bekleyen seçimi, TL (TI5) | I01; R1 kabulünde faturalı 240,000 kg, bekleyen 360,000 kg kalan; hazırlayan R1'in 180 kg'sini 100 faturalı + 80 bekleyen seçer; R2'nin 120 kg'si tamamı faturalı | Maliyet/kg her iki payda 19,9936 | Dağılım ve maliyet I01 ile aynı: **6 171,65 TL**; pay kayıtları R1-faturalı 100, R1-bekleyen 80, R2-faturalı 120 kg |
| I05 | Ortalamanın yeniden hesabı (SAT-D306 (2)) | Depo havuzu 2 000,000 kg @ 20,5000; I01 iadesi | (2000×20.5 − 6171.648)/1700 = 20.48726588… | Kalan 1 700,000 kg, ortalama **20,4873** |
| I06 | Dövizli R2, faturalı paydan seçim (BS-1) | I01; R2 dövizli, faturalı payın TL maliyet/kg'si fatura resmî kuruyla 21,4400; hazırlayan R2'nin 120 kg'sini faturalı paydan seçer | I01 ile aynı | **6 171,65 TL** |
| I07 | Kabul sınırı aşılıyor (TI3) | I01; R1'den alınan 600 kg'nin 500 kg'si daha önce başka etiketlerle iade edilmiş (kalan 100 kg); R1 payı 180 kg | 180 > 100 | **Reddedilir**; R2'ye kaydırılmaz |
| I08 | Fatura fiyatı siparişten farklı (SAT-D392/D371) | I01; R1'in faturası 12 300,00 TL / 600,000 kg | Fatura maliyet/kg 12300/600 = 20,5 ≠ 19,9936; R1 payı 180 kg için fark 180×(20.5−19.9936) = 91,152 | Fark var → **reddedilir** (SAT-D371, Finans spec'i gelene kadar) |
| I09 | Dağıtım artığı (SAT-D440) | Etiket = R1 100 kg + R2 200 kg birleştirmesi (paylar 1/3, 2/3); 100,000 kg iade | Kesin 33,333… / 66,666…; aşağı indirilmiş 33,333 + 66,666 = 99,999; artık 0,001 kg en büyük paya (R2) | R1 **33,333**, R2 **66,667 kg**; maliyet 33.333×19.9936 + 66.667×21.44 = 2 095,7871488 → **2 095,79 TL** |
| I10 | Adet dağıtımı (SAT-D442) | L8 adetli; 10 adet / 100,000 kg iade; paylar R1 21/50, R2 7/25, R3 3/10 | Adet kesin 4,2 / 2,8 / 3; aşağı 4 / 2 / 3 = 9; kalan 1 adet en büyük paya (R1); kg I03 gibi orantılı | Adet R1 **5**, R2 **2**, R3 **3**; kg R1 **42,000**, R2 **28,000**, R3 **30,000** |
| I11 | Miktar iadesi, soyunda X yok (SAT-D441, SAT-D444, TI8) | L9 120,000 kg, soyu %100 tedarikçi Y'nin R4 kabulü; depo/ürün ortalaması 21,2500 TL/kg; X'e 80,000 kg iade; hazırlayan X'in R5 kabulünü seçer (R5'in alış siparişi kg birimli, birim fiyat 20,50 TL/kg, açık 300,000 kg) | Stok çıkışı 80×21.25 = 1 700; tedarikçiye fatura 80×20.50 = 1 640; fark 60 | R5'ten **80,000 kg** düşülür; stok çıkışı **1 700,00 TL** (ortalamayla); iade faturası **1 640,00 TL**; fark **60,00 TL** mevcut iade farkı kuralına gider (soy ağacındaki R4 maliyeti kullanılmaz) |
| I12 | Karışık soy, X payı + üretim payı (SAT-D433 + SAT-D441/D444, TI7) | L10 200,000 kg: %50 R1 (X; sipariş birim fiyatı R1 maliyetine eşit, 19,9936 TL/kg), %50 üretim çıktısı P; depo/ürün ortalaması 21,2500; X'e 100,000 kg iade; hazırlayan X dışı pay için R5'i seçer | R1 50 kg: çıkış 50×19.9936 = 999,68, fatura 999,68, fark 0; R5 50 kg: çıkış 50×21.25 = 1 062,5, fatura 50×20.50 = 1 025, fark 37,5; toplam çıkış 2 062,18, fatura 2 024,68 | R1 **50,000**, R5 **50,000 kg**; stok çıkışı **2 062,18 TL**; iade faturası **2 024,68 TL**; fark **37,50 TL** mevcut iade farkı kuralına gider |
| I15 | Adet artığında eşit en büyük paylar (SAT-D442, 37. cevap) | Etiket = R1 50 kg + R2 50 kg (kabul sırası R1, R2; paylar 1/2, 1/2); adetli; 5 adet / 50,000 kg iade | Adet kesin 2,5 / 2,5; aşağı 2 / 2 = 4; kalan 1 adet, eşit en büyük paylardan kabul sırasına göre ilki (R1); kg 25 / 25 | Adet R1 **3**, R2 **2**; kg R1 **25,000**, R2 **25,000** |

Hesaplanamaz:

| # | Durum | Sonuç |
|---|---|---|
| I13 | Dövizli R2'den bekleyen pay seçildi (TI4) | Toplam iade maliyeti `Unavailable`; kısmi toplam gösterilmez; SAT-D393 istisna yolu (onay `purchasing.orders.approve`, giren ≠ onaylayan, Stok 409 aşılmaz) |
| I14 | Seçilen X kabulünün açık miktarı yetmiyor (SAT-D441 "açık miktarı kadar", TI3) | Reddedilir; başka kabule kaydırılmaz |

---

## Türetilenlerin özeti

TK1–TK9 (kg fiyatı), TA1–TA5 (alış maliyeti), TM1–TM4 (adetli marj), TI1–TI5, TI7, TI8 (iade soy ağacı payı ve miktar iadesi; TI6 SAT-D444 ile karar metnine dönüştü)
yukarıda gerekçesiyle yazılıdır. Hiçbiri yeni para/stok/maliyet politikası seçmez; her biri adı geçen kilitli kuralın veya
kararın metninden çıkar. Bir reviewer bir türetmeyi politika seçimi sayarsa ilgili satır hesaplanamaz kabul edilip
`NEEDS-SINAN`'a taşınmalıdır; golden bu belgeyle o satırı kilitlemiş sayılmaz.

## Önceki NEEDS-SINAN noktalarının durumu (SINAN-CEVAP 53 ve 54)

| No | Konu | Durum | Etkilenen satır |
|---|---|---|---|
| N1 | Dövizli belgede kg fiyatı | **Cevaplandı — SAT-D439:** belge para biriminde, kur yok | K16, K19 hesaplandı |
| N2 | Belge altı iskonto, vade farkı, kg'siz hizmet ve iade satırları | **Açık** (28. soru "Bunu bilmiyorum"); karar gelene kadar belge düzeyi yalnız bütün satırlar kg'li mal satırıyken gösterilir | K18 hesaplanamaz kalır |
| N3 | Metre satırında 3 ondalığı aşan kg | **SAT-D443:** metre bu trende açılmaz; ayrı karar | K07, M05, M15 kapsam dışı |
| N4 | Ortalamaya giriş: yuvarlanmış maliyet/kg mi, kesin tutar mı | **Cevaplandı — ALS-D120:** kesin tutar | A06, A14 |
| N5 | Kısmi faturada kg payı | **Cevaplandı — ALS-D121:** kabul kg × faturalanan adet ÷ kabul adedi | A09, A13 |
| N6 | Soy ağacı dağıtım artığı | **Cevaplandı — SAT-D440:** en büyük paya, eşitlikte kabul sırasına göre ilkine | I09 |
| N7 | Soyunda farklı tedarikçi | **Cevaplandı — SAT-D441:** sınır yok; seçilen X kabulünden düşülür, etiket maliyetiyle çıkış, fark mevcut kuralla | I11 |
| N8 | Soyunda alış dışı pay | **Cevaplandı — SAT-D441** (33. cevap 32. ile aynı) | I12 |
| N9 | Adetli etiketten iadede adet dağıtımı | **Cevaplandı — SAT-D442:** aşağı yuvarla, kalan adet en büyük paya | I10 |
| N10 | Adet artığında eşit en büyük paylar | **Cevaplandı — SAT-D442 (37. cevap):** kabul sırasına göre ilkine | I15 |
| (TI6) | Miktar iadesinde "etiketin kendi maliyeti" ve iade fiyatı | **Cevaplandı — SAT-D444:** stok çıkışı depo/ürün ortalamasıyla, fatura seçilen siparişin birim fiyatıyla | I11, I12 |

## NEEDS-SINAN (karara bağlanmamış; satırlar hesaplanamaz bırakıldı)

- **N2 (K18):** Belge düzeyi kg fiyatına belge altı iskonto, vade farkı, kg'si olmayan hizmet satırları (nakliye vb.) ve
  iade satırları nasıl girsin? (28. soru "Bunu bilmiyorum"; karar dosyası "Sınırlar".) Satır düzeyi her zaman gösterilir.

## Sınırlar

- Bu belge kod, test, migration, OpenAPI, Ayarlar verisi veya başka karar dosyası değiştirmez; grant açmaz.
- URT-D112 (üretim çıktısında adet) ve KLN-D119 (etikette adet) için formül golden'ı istenmedi; sapma eşiği URT-D51 (5)
  ile açık kalır.
- Karışık kaynaklı kalem marjı açık kalır (`SINAN_KARARLARI_2026-11-21_TREN_KAPANIS.md` "Sınırlar").
- `OPEN-*` kapatılmaz; `KARAR_SICILI.md` ve `ACIK_KARARLAR.md` değişmez. İnsan kapıları (deployment, environment,
  secret, Settings → Actions etkinleştirme, grant/rol/Critical, production aktivasyonu, `OPEN-*` kapatma,
  `.claude/settings.json`) değişmez.
