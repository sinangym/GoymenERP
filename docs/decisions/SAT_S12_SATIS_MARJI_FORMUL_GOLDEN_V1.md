# SAT-S12 Satış Marjı Formül Golden v1

## Kaynak ve kabul sınırı

Bu belge yeni Sinan kararı eklemez; SAT-D265(5) için bağımsız formül golden adayını kaydeder. Kullanıcı, bağımsız incelemenin raporunu ve aşağıdaki kopyalanabilir golden metnini çalışma sohbetine iletti. Aktarımın kaynak kaydı: [Issue #1189 / 6960758947](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6960758947). Bu bağlantı **ajan aktarımıdır**, bağımsız insan GitHub yazarlığı veya DOCS/K3 review imzası değildir. İnceleyen oturumun kimliği burada bağımsız olarak doğrulanmış sayılmaz.

İncelenen kaynak: `codex/tren-1-satis-kapanis-w8`, exact head `e20f0ad14a44ecfe927060dbe0a2ff7e3eda9c8c`. İnceleme formül çekirdeği için şartlı **APPROVE**, P0/P1 yok; açılışı engelleyen **P2-1** bildirir: Satış çağrısının fonksiyonel para birimini maliyetin kendi para biriminden seçmesi TL kuralını geçersiz kılar. Yazarın `3aa5b4c58` düzeltmesi ayrı tren kaynağıdır; bu golden incelemesi yeni uygulama head'ini onaylamaz.

Kaynak karar dosyaları: `SINAN_KARARLARI_2026-11-09_SATIS_EK51.md`, `SINAN_KARARLARI_2026-11-10_SATIS_EK52.md` ve daha yeni ilgili satış kararları. Formül golden metni aşağıya iletildiği biçimiyle aktarılmıştır; yazar testlerinden türetilmemiştir. İnceleme kesin rasyonel aritmetik kullandığını bildirir. Proje Unit/SQL/HTTP/Vitest/browser/CI koşumu bu incelemenin kapsamında değildir.

Bu dosyanın varlığı formülü açmaz. İki bağımsız exact-head DOCS review, main'e alınma ve P2-1'in giderilmesi gerekir. Son uygulama K3 kapısı ayrıca sürer. Eski karar dosyası, grant/rol/Critical, production, OPEN veya Finance STOP değiştirilmez.

Kullanıcı bu aday için iki ayrı bağımsız Claude oturumuyla golden/DOCS incelemesini kabul etti: [6961293621](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6961293621), doğrudan çalışma sohbeti cevabının açık ajan aktarımı. Bu, SAT-D265(5)'teki inceleyen oturum şartına yalnız bu aday için dar istisnadır; iki bağımsız exact-head imzası, kaynak/aritmetik doğrulaması ve main koşulu korunur. Son uygulama K3 veya başka formül adayları için genel kural değişmez; yazar kendi işini onaylamaz veya merge etmez.

## İletilen golden metni

Kaynak: SAT-D259, D261, D262, D263, D265(1-4), D275, D295, D297, D298; OPEN-INV-AVGCOST-FX.
Kapsam: TL, maliyet birimi = sipariş birimi, miktar > 0.
Tanım: NetSatış = iskonto sonrası, vade farkı öncesi, KDV hariç satır tutarı; kuruşa
bir kez half-away-from-zero yuvarlanmış satır tutarıdır (SAT-D11).
Maliyet = birim alış maliyeti (≤ 4 ondalık) × miktar; vade farkı/ek maliyet eklenmez.
Marj = NetSatış − Maliyet (kesin). Yüzde = Marj × 100 / NetSatış (kesin, yuvarlanmamış Marj'dan).
Gösterim: tutar 0,01; yüzde 0,01; ikisi de sıfırdan uzağa (negatif dahil).
NetSatış ≤ 0: tutar gösterilir, yüzde "kullanılamıyor" (NetSaleNotPositive).

| # | NetSatış | BirimMaliyet | Miktar | Marj (kesin) | Yüzde (kesin) | Tutar | Yüzde |
|---|---|---|---|---|---|---|---|
| G01 | 1000.00 | 7.1234 | 100 | 287.66 | 28.766 | 287.66 | 28.77 |
| G02 | 100.00 | 120.5 | 1 | -20.5 | -20.5 | -20.50 | -20.50 |
| G03 | 0.00 | 5 | 2 | -10 | — | -10.00 | kullanılamıyor (NetSaleNotPositive) |
| G04 | -50.00 | 1 | 1 | -51 | — | -51.00 | kullanılamıyor (NetSaleNotPositive) |
| G05 | 10.00 | 4.995 | 1 | 5.005 | 50.05 | 5.01 | 50.05 |
| G06 | 10.00 | 10.005 | 1 | -0.005 | -0.05 | -0.01 | -0.05 |
| G07 | 1000.00 | 999.95 | 1 | 0.05 | 0.005 | 0.05 | 0.01 |
| G08 | 1000.00 | 1000.05 | 1 | -0.05 | -0.005 | -0.05 | -0.01 |
| G09 | 10.00 | 3.3335 | 1 | 6.6665 | 66.665 | 6.67 | 66.67 (yuvarlanmış tutardan 66.70 OLMAZ) |
| G10 | 10.00 | 3.3325 | 1 | 6.6675 | 66.675 | 6.67 | 66.68 (66.70 OLMAZ) |
| G11 | 900.00 (fiyat 100, %10 isk., 10 kg; vade farkı yok / %5 / 2.5 TL) | 60 | 10 | 300 | 33.333… | 300.00 | 33.33 (üç durumda aynı) |
| G12 | 30.02 (10.005 × 3 = 30.015 → 30.02) | 10 | 3 | 0.02 | 0.0666… | 0.02 | 0.07 |
| G13 | 1500.00 | 12.3456 | 2.5 | 1469.136 | 97.9424 | 1469.14 | 97.94 |
| G14 | 300.00 | 0.3333 | 3 | 299.0001 | 99.6667 | 299.00 | 99.67 |
| G15 | 100.00 | 5.00010 | 1 | 94.9999 | 94.9999 | 95.00 | 95.00 |

Kullanılamıyor (tutar ve yüzde yok; sıfır/tahmini değer yazılmaz):

- G16 maliyet 5.00001 (> 4 ondalık) → CostInvalid
- G17 maliyet -0.0001 → CostInvalid
- G19 maliyet birimi ≠ sipariş birimi → UnitConversionUndecided
- G20 satış veya maliyet para birimi ≠ TL → CurrencyConversionUndecided (OPEN-INV-AVGCOST-FX)
- G21 maliyet SourceMissing/Unreadable/NoSource/ProducedItem/MixedSourceItem → ilgili neden
- G22 NetSatış yok → SalesAmountUnavailable (maliyet nedeninden önce)
- G23 NetSatış 1.00, maliyet 999999999999999.9999, miktar 10^14 → AmountOutOfRange
- G24 NetSatış 0.01, maliyet 999999999999999.9999, miktar 10^10 → yüzde taşar → AmountOutOfRange (tüm marj)

Açık (NEEDS-SINAN; bu golden politika seçmez):

- G18 birim maliyet 0: mekanik sonuç 100.00 / 100.00; SAT-D297 gereği porttan gelmemeli,
  Satış katmanında reddi kararla kapanmadı.
- Kg dışı birimde Stok tarafı çift 4-ondalık yuvarlama; bu golden yalnız aynı birimi kapsar.
- Yalnız yüzde taştığında tutarın da düşmesi bugünkü davranıştır; karar kapsamı dışında.

## Uygulama okumasının sınırları

**G20 ve tarihsel OPEN atfının düzeltmesi:** Kaynak listesindeki ve iletilen G20 satırındaki `OPEN-INV-AVGCOST-FX` yeni/açık bir maliyet kararı değildir. `SINAN_KARARLARI_2026-11-11_BEKLEYEN_SORULAR.md` **BS-1** ve `SINAN_KARARLARI_2026-11-11_SATIS_EK59_ORTALAMA_MALIYET.md` durum tablosu bu kur kaynağını daha önce karara bağlamıştır: TL ortalama maliyet **tedarikçi faturasındaki resmî kurla** hesaplanır; fatura gelene kadar TL maliyet **Unavailable** okunur, geçici kur veya düzeltme hareketi üretilmez. G20'nin maliyet kısmı bugün gerçek Stok portundan TL değer ya da Unavailable okunmasıyla değerlendirilir. Golden'daki eski OPEN parantezi bu kararı yeniden açmaz. **Satış tutarının para birimi TL dışı olduğunda marjın nasıl ele alınacağı ayrı, karara bağlanmamış sınırdır; BS-1 veya bu OPEN kimliğine dayanarak satış tutarı çevrilmez.** Bu belge mevcut kararı aktarır; açık karar sicilini değiştirmez veya kendi yetkisiyle OPEN kapatmaz.

**G21 geçiş durumu:** `ProducedItem` / `MixedSourceItem` koşulsuz ve süresiz yasak değildir. `SINAN_KARARLARI_2026-11-15_MALIYET_VE_RISK.md` **URT-D67**, üretilen maliyetin URT-S14 ile hesaplandığı anda ortalamaya ve marj kaynağına katılmasını belirler; karma kaynak için **SAT-D305(1)** tek hareketli ortalama kuralı geçerlidir. İletilen G21 yalnız gerçek kaynağın henüz okunamadığı durumda ilgili nedenin aktarımını kaydeder; mevcut ve kanıtlı TL maliyetini sırf ürün üretildi/karma diye reddetmeye dayanak değildir.

**G12 kesin yüzde:** İletilen tablodaki `0.0666…`, devirli `0.06666…` anlamına gelmez. Kesin kesir `100/1501`, ondalık açılım `0.0666222518…`; gösterim `0.07` değişmez.

**G18 sonraki kullanıcı cevabı:** Yukarıdaki iletilen inceleme snapshot'ında sıfır maliyet açık bırakılmıştı. Kullanıcı daha sonra “Marjı kullanılamıyor göster (Önerilen)” dedi: [6960913297](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6960913297), açık ajan aktarımı. Satış'a porttan veya eski snapshot'tan sıfır birim maliyet gelirse maliyet okunamıyor gösterilir; sipariş onayı engellenmez. G18'in mekanik yüzde hesabı artık uygulamada %100 gösterme yetkisi değildir. Bu yeni cevabın uygulama değişikliği ayrı tren kaynağıdır; ilk inceleme onu onaylamaz.

G22 ve G24 hesaplayıcının incelenen kaynak davranışını kaydeder; tüm uygulama katmanlarının neden önceliği veya erişilebilir miktar sınırları hakkında yeni karar değildir. Yalnız yüzde taşmasında tutarın da düşmesi ve kg dışı birimde Stok ara yuvarlaması bu golden ile karara bağlanmaz. Satış döviz tutarının marj için çevrilmesi veya ilk alış maliyetinin muhasebeleştirme politikası türetilmez.

Formülün açılması ayrı uygulama değişikliğidir; bu belgeyle `MarginGoldenAccepted` değiştirilmez. Main'e alınma, runtime kabulü, gerçek sağlayıcı, SQL, browser veya CI başarısı anlamına gelmez.

## SAT-D426 ek golden'ı: dövizli satışta marj

### Kaynak, bağımsızlık ve kabul sınırı

Bu bölüm yeni Sinan kararı eklemez; SAT-D426 için SAT-D265 (5) gereği istenen golden satırlarını kaydeder.
Kaynak kararlar: SAT-D259, SAT-D261, SAT-D262, SAT-D264, SAT-D265 (EK51/EK52); BS-1
(`SINAN_KARARLARI_2026-11-11_BEKLEYEN_SORULAR.md`); SAT-D306; SAT-D426 ve aynı belgenin 9. cevabı
(`SINAN_KARARLARI_2026-11-21_TREN_KAPANIS.md`, bu bölüm yazılırken `claude/tren-1` dalında, main'de değil);
SINAN-CEVAP 49 ([Issue #1189 / 7065185254](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-7065185254))
ve SINAN-CEVAP 50 ([Issue #1189 / 7067213965](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-7067213965)).
SINAN-CEVAP 50'nin (2) "kur bulunamazsa onay engellenmesin" ve (3) "kur, ilk başarılı onayda donsun" cevapları bu
bölümde SAT-D429 ve SAT-D430 diye anılır; o kimliklerin karar dosyası satırı bu bölüm yazılırken depoda yoktu, metin
yorumdaki cevaptır. Bağlantılar **ajan aktarımıdır**, bağımsız insan GitHub yazarlığı değildir.

Bağımsızlık: Bu satırlar, uygulamanın yazarı olmayan ayrı bir Claude oturumunda (SINAN-CEVAP 50 (1)) yalnız yukarıdaki
karar metinlerinden ve bu belgenin kilitli adımlarından, kesin ondalık aritmetikle (ara yuvarlama yok; yüzde kesin
kesirden) hesaplandı. Uygulama kodu, uygulama testleri ve uygulamanın beklenen değerleri okunmadı. Proje
Unit/SQL/HTTP/Vitest/browser/CI koşumu kapsam dışıdır.

Bu bölümün varlığı döviz dönüşümünü açmaz: iki bağımsız exact-head DOCS review ve main'e alınma gerekir; son uygulama
K3 kapısı ayrıca sürer. `MarginGoldenAccepted`, `OPEN-*`, `KARAR_SICILI.md`, `ACIK_KARARLAR.md`, grant/rol/Critical ve
production değişmez.

### G20'nin SAT-D426 ile değişen kısmı

- **Satış para birimi TL dışı:** G20'nin "satış … para birimi ≠ TL → CurrencyConversionUndecided" kısmı SAT-D426 ile
  **değişir**. Satış tutarı, siparişin onayında dondurulmuş kurla TL'ye çevrilir ve marj TL hesaplanır (SAT-D259 (2)
  korunur). Bu, yukarıdaki "Uygulama okumasının sınırları" bölümünde satış para birimi için yazılan "karara bağlanmamış
  sınır; satış tutarı çevrilmez" cümlesinin yerine geçer (o cümle SAT-D426'dan önceki durumu kaydeder; metni
  korunmuştur).
- **Kur yoksa veya kullanılamazsa:** Onay öncesi önizleme (SAT-D267), kayıtlı kuru olmayan eski onaylı sipariş ve kurun
  okunamadığı onay (onay engellenmez, SAT-D429) için marj **hesaplanamaz** kalır: CurrencyConversionUndecided. Geçici kur
  üretilmez, sıfır/tahmini değer yazılmaz.
- **Maliyet para birimi TL dışı: durum değişmez.** Maliyet BS-1'e göre Stok portundan TL değer ya da Unavailable
  okunur; Satış maliyeti çevirmez (SAT-D262 (3)). Porttan TL dışı para birimli bir maliyet gelirse G20 aynen sürer:
  CurrencyConversionUndecided.
- **TL satış:** Çevrim yoktur; siparişte bir kur kayıtlı olsa bile yok sayılır, sonuç G01–G15 ile aynıdır.

### Formül (G01–G15 adımlarına tek adım eklenir)

1. NetSatış_döviz = G-tanımındaki net satır tutarı, **satış para biriminde** kuruşa (o para biriminin 0,01'ine) bir kez
   half-away-from-zero yuvarlanmış (SAT-D11; değişmez).
2. NetSatış_TL = NetSatış_döviz × Kur_dondurulmuş (**kesin, yuvarlanmaz**). Kur, `<satış para birimi>/TRY` çiftinde
   1 birim dövizin TL karşılığıdır.
3. Maliyet_TL = TL birim maliyet (≤ 4 ondalık, SAT-D265 (2)) × miktar (kesin).
4. Marj = NetSatış_TL − Maliyet_TL (kesin). Yüzde = Marj × 100 / NetSatış_TL (kesin, yuvarlanmamış Marj'dan;
   SAT-D265 (1)).
5. Gösterim: tutar 0,01 TL, yüzde 0,01; ikisi de sıfırdan uzağa (negatif dahil) (SAT-D261, SAT-D265 (3)–(4)).
   NetSatış_TL ≤ 0 ise tutar gösterilir, yüzde kullanılamıyor (NetSaleNotPositive).

**Türetilen adımlar (karar metni açıkça yazmadığı için; yeni politika seçilmeden mevcut kurallardan):**

- **T1 — Çevrimde ara yuvarlama yok.** SAT-D261 (2) "yeni bir yuvarlama kuralı açılmaz" der; SAT-D265 yalnız
  gösterimde (3) yuvarlama tanımlar ve yüzdeyi yuvarlanmamış tutardan (1) ister; bu belge "Marj (kesin)" der. Bu yüzden
  NetSatış_TL kuruşa yuvarlanmaz. Faturanın TL karşılığındaki satır yuvarlaması (SAT-D223) muhasebe tutarı içindir ve
  SAT-D426 "Sınırlar" faturanın kurunu/hesabını siparişin marjına taşımaz. D04 ve D05, kuruşa yuvarlanmış TL ara
  adımının yanlış sonucunu "OLMAZ" diye gösterir.
- **T2 — Kur hassasiyeti.** Kur, siparişe dondurulmuş değeriyle **aynen** kullanılır; golden kur için yuvarlama veya
  basamak sınırı uygulamaz (4 ve 8 ondalıklı satırlar). Kurun kaynağı, günü ve geçerliliği Ayarlar kur politikasınındır
  (OPEN-012 KISMEN; DEC-FIN-010); golden onu girdi kabul eder. Gün sütunu açıklayıcıdır, hesaba girmez.
- **T3 — Kur çifti uyumu.** Kur çiftinin taban para birimi satış para birimiyle aynı ve karşı para birimi TRY değilse
  kur o satış için TL karşılığı vermez; marj hesaplanamaz (fail-closed, SAT-D426 "geçici kur üretilmez"). Çapraz kur
  türetilmez.
- **T4 — Kur tektir.** Kur, çözülebildiği ilk onayda donar (SAT-D430) ve sonra değişmez (9. cevap); revizyon onayı
  ilk dondurulan kuru kullanır (SAT-D426 "Sınırlar").

### Golden satırları

Sütunlar: Net döviz = NetSatış_döviz; Kur = dondurulmuş kur (çift, değer, kur günü / onay günü);
NetSatış TL (kesin); Marj (kesin); Tutar = gösterilen marj; Yüzde = gösterilen yüzde.

| # | Satış p.b. | Net döviz | Kur (çift, değer, gün) | Miktar | TL birim maliyet | NetSatış TL (kesin) | Marj (kesin) | Tutar | Yüzde |
|---|---|---|---|---|---|---|---|---|---|
| D01 | USD | 1000.00 | USD/TRY 34.5678 (kur 2026-11-19 / onay 2026-11-20) | 100 | 300.1234 | 34567.80 | 4555.46 | 4555.46 | 13.18 |
| D02 | EUR | 2500.00 | EUR/TRY 37.12345678 (8 ondalık; 2026-11-19 / 2026-11-20) | 50 | 1500.5 | 92808.64195 | 17783.64195 | 17783.64 | 19.16 |
| D03 | USD | 10.00 | USD/TRY 34.1235 (2026-11-19 / 2026-11-20) | 1 | 341.2300 | 341.235 | 0.005 | 0.01 | 0.00 |
| D04 | USD | 10.00 | USD/TRY 34.1235 (2026-11-19 / 2026-11-20) | 1 | 0.0050 | 341.235 | 341.23 | 341.23 (TL önce kuruşa yuvarlanırsa çıkan 341.24 OLMAZ) | 100.00 |
| D05 | USD | 100.00 | USD/TRY 34.12345650 (8 ondalık; 2026-11-19 / 2026-11-20) | 1 | 3412.3407 | 3412.34565 | 0.00495 | 0.00 (0.01 OLMAZ) | 0.00 |
| D06 | USD | 100.00 | USD/TRY 34.12345650 (8 ondalık; 2026-11-19 / 2026-11-20) | 1 | 3412.3406 | 3412.34565 | 0.00505 | 0.01 | 0.00 |
| D07 | USD | 100.00 | USD/TRY 34.0000 (2026-11-19 / 2026-11-20) | 1 | 3399.83 | 3400.00 | 0.17 | 0.17 | 0.01 (kesin 0.005) |
| D08 | EUR | 500.00 | EUR/TRY 37.4512 (2026-11-19 / 2026-11-20) | 20 | 1000.0001 | 18725.60 | -1274.402 | -1274.40 | -6.81 |
| D09 | USD | 0.00 | USD/TRY 34.5678 (2026-11-19 / 2026-11-20) | 2 | 5 | 0.00 | -10 | -10.00 | kullanılamıyor (NetSaleNotPositive) |
| D10 | USD | -50.00 | USD/TRY 34.5678 (2026-11-19 / 2026-11-20) | 1 | 1 | -1728.39 | -1729.39 | -1729.39 | kullanılamıyor (NetSaleNotPositive) |
| D11 | USD | 12345678.91 | USD/TRY 34.56789012 (8 ondalık; 2026-11-19 / 2026-11-20) | 250000 | 1234.5678 | 426764072.0176813692 | 118122122.0176813692 | 118122122.02 | 27.68 |
| D12 | USD | 3.00 | USD/TRY 34.56789012 (8 ondalık; 2026-11-19 / 2026-11-20) | 1 | 100 | 103.70367036 | 3.70367036 | 3.70 | 3.57 |
| D13 | TL | 1000.00 | siparişte USD/TRY 34.5678 kayıtlı — **yok sayılır** | 100 | 7.1234 | 1000.00 | 287.66 | 287.66 | 28.77 (= G01) |
| D14 | TL | 1000.00 | kur yok | 100 | 7.1234 | 1000.00 | 287.66 | 287.66 | 28.77 (= G01) |
| D15 | USD | 1000.00 | İlk onay 2026-11-16'da kur okunamadı (onay sürdü, kur yok); revizyon onayı 2026-11-20'de USD/TRY 34.6000 (kur 2026-11-19) okundu ve **o an dondu** (SAT-D430) | 100 | 300.1234 | 34600.00 | 4587.66 | 4587.66 | 13.26 |
| D16 | USD | 1000.00 | İlk onay 2026-11-20'de USD/TRY 34.5678 (kur 2026-11-19) dondu; revizyon onayı 2026-11-25'te Ayarlar kuru 35.1000 olsa da **34.5678 kullanılır** (T4) | 100 | 300.1234 | 34567.80 | 4555.46 | 4555.46 | 13.18 |

Hesaplanamaz (tutar ve yüzde yok; sıfır/tahmini değer veya geçici kur yazılmaz):

| # | Satış p.b. | Net döviz | Kur durumu | Miktar | TL birim maliyet | Sonuç |
|---|---|---|---|---|---|---|
| D17 | USD | 1000.00 | Onay öncesi önizleme (SAT-D267): kur henüz dondurulmamış | 100 | 300.1234 | CurrencyConversionUndecided |
| D18 | USD | 1000.00 | SAT-D426'dan önce onaylanmış, kayıtlı kuru olmayan eski sipariş | 100 | 300.1234 | CurrencyConversionUndecided |
| D19 | USD | 1000.00 | Onay günü kur okunamadı; onay engellenmedi (SAT-D429), kur dondurulmadı, sonra başarılı onay yok | 100 | 300.1234 | CurrencyConversionUndecided |
| D20 | USD | 1000.00 | Dondurulmuş kur EUR/TRY 37.4512 — taban para birimi satışla uyuşmuyor (T3) | 100 | 300.1234 | CurrencyConversionUndecided |
| D21 | USD | 1000.00 | Dondurulmuş kur USD/EUR 0.9234 — karşı para birimi TRY değil (T3) | 100 | 300.1234 | CurrencyConversionUndecided |
| D22 | USD | 1000.00 | USD/TRY 34.5678 dondurulmuş; maliyet porttan **USD** para birimiyle (8.6500) geldi — G20 maliyet kısmı aynen | 100 | (USD 8.6500) | CurrencyConversionUndecided |
| D23 | TL | 1000.00 | Kur yok sayılır; maliyet porttan **EUR** para birimiyle geldi — G20 maliyet kısmı aynen | 100 | (EUR 7.1234) | CurrencyConversionUndecided |

### Kesin yüzde kesirleri (yeniden hesap için)

D01 455546/34567.80 → 13.17833359…; D02 17783.64195×100/92808.64195 → 19.16162285…; D03 0.5/341.235 → 0.00146526…;
D04 34123/341.235 → 99.99853473…; D05 0.495/3412.34565 → 0.00014506…; D06 0.505/3412.34565 → 0.00014799…;
D07 17/3400 = 0.005 (tam yarım → 0.01); D08 -127440.2/18725.60 → -6.80566710…; D11 → 27.67855350…;
D12 370.367036/103.70367036 → 3.57139756…; D15 458766/34600 → 13.25913294…; D16 = D01.

### Bu bölümün açık bıraktıkları

- Kur değeri ≤ 0 veya Ayarlar kur politikasının geçersiz saydığı bir kur için satır verilmez; kurun geçerliliği Ayarlar
  kur politikasınındır (OPEN-012 KISMEN). Golden bunu seçmez.
- Neden önceliği (ör. kur yokluğu ile maliyet nedeninin ya da SalesAmountUnavailable'ın aynı satırda birlikte olması)
  için yeni sıra tanımlanmaz; her hesaplanamaz satır tek neden taşır. G22'nin kaydettiği öncelik aynen geçerlidir.
- Büyük tutarlarda AmountOutOfRange eşiği G23/G24'teki gibi hesaplayıcının sınırıdır; D11 bu sınırın içinde kabul
  edilir, yeni eşik tanımlanmaz.
- Satış para birimi TL dışıyken net tutarın satış para birimindeki ölçeği (ör. JPY 0, KWD 3 basamak) bu satırlarda
  kapsanmaz; satırlar 2 basamaklı USD/EUR içindir.
