# Sinan Kararları — 2026-11-12 Ürün EK1 (U-08 kantar sapması yönü, katsayının hesapta yuvarlanması, satır kg yuvarlaması)

Bu belge önceki karar dosyalarını silmez; birim çevrim (U-08) için `URN-D7`–`URN-D9` kararlarını ekler.
`SINAN_KARARLARI_2026-11-11_BEKLEYEN_SORULAR.md` içindeki BS-9, BS-13, BS-16 ve BS-17 geçerli kalır; bu belge
BS-13 ve BS-17'yi aşağıda yazılan noktalarda **netleştirir** (BS-13'teki "ara adımlarda yuvarlama yoktur" kuralına
katsayı için bir istisna getirir; BS-17'deki işaretli formülün tolerans kontrolünde mutlak değerle kullanıldığını
yazar). `SINAN_KARARLARI_2026-11-12_URUN.md` (`URN-D1`–`URN-D6`) aynen geçerlidir.

**Kaynak:** Sinan, Issue #66, 2026-11-12, koordinatör oturumunda soruldu; koordinatör kaydı
[6825138604](https://github.com/sinangym/goymenerp/issues/66#issuecomment-6825138604). Sorular Ürün şeridinin
NEEDS-SINAN yorumundandır ([6825004231](https://github.com/sinangym/goymenerp/issues/66#issuecomment-6825004231));
o yorum bu kayıtla kapanır. Seçilen seçenek etiketleri koordinatör kaydında kelimesi kelimesine verilmiştir:
"İki yön de", "Hesapta da 3 ondalık", "En yakın, yarım yukarı". Koordinatör kaydı seçilmeyen seçenekleri
listelemez; aşağıda seçilmeyen yol, NEEDS-SINAN sorusundaki alternatiflerden yazılmıştır.

**Ek kaynak (r2 review'larından sonra):** Sinan ve koordinatör, Issue #66, 2026-11-12, koordinatör kaydı
[6825745714](https://github.com/sinangym/goymenerp/issues/66#issuecomment-6825745714) (#1134 r2 review'ları:
DOCS-A 6825428679, DOCS-B 6825433323 P2-1'). Negatif satır kg için Sinan'ın seçimi kelimesi kelimesine
**"Ayna: −1,235"**. Adetle satılan profilde kg/m ondalığı için Sinan'ın cevabı kelimesi kelimesine **"Eğer fiyatta
daha fazla ondalık gerektirirse daha fazla olsun"**, netleştirme sorusuna **"Sen araştır"**; bunun üzerine kural
Sinan'ın devriyle **koordinatör kararıdır (geri alınabilir)**. Önceki yuvarlama önerisi:
[6806590261](https://github.com/sinangym/goymenerp/issues/66#issuecomment-6806590261); bu öneri BS-13'te Sinan
tarafından onaylandı. URN-D8 bu önerinin yalnız kg/m satırındaki "3 ondalık yalnız gösterimde" kısmının yerini alır;
kg→adet aşağı yuvarlama, belge kg toplamının 2 ondalık gösterilmesi ve %1 kantar toleransı BS-13 üzerinden aynen
geçerlidir.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Kantar sapması mutlak mı, işaretli mi? BS-17 formülü işaretli `(tartılan − teorik) / taban`; #1089 hesabı mutlak `\|tartılan − teorik\|` karşılaştırıyor. | **"İki yön de"** | Az tartım da fazla tartım da tolerans dışıysa uyarı verilir: tolerans kontrolü mutlak değerle yapılır (`\|tartılan − teorik\| / taban`). Ekranda sapma işaretli (+/−) gösterilir. #1089'daki mutlak hesap doğrulanır. Seçilmeyen yol: yalnız tek yönün (ör. yalnız eksik tartımın) uyarı vermesi. |
| 2 | BS-13 "katsayı 3 ondalık" kuralı hesapta mı, yalnız gösterimde mi? Örnek: 0,999 kg ÷ 0,333 = 3 adet; 0,999 ÷ (1/3 tam değer) = 2 adet. | **"Hesapta da 3 ondalık"** | Katsayı hesapta 3 ondalığa yuvarlanmış değeriyle kullanılır; görünen = hesaplanan (örnek: 0,999 kg ÷ 0,333 = 3 adet). #1089'daki tam-değer hesabı bu karara göre değişir. Seçilmeyen yol: hesapta tam değer, 3 ondalık yalnız gösterimde (#1089'un bugünkü davranışı). |
| 3 | Belge satırının kg'si Quantity'ye (3 ondalık) kaydedilirken hangi kuralla yuvarlanır ve bu ayar mı olmalı? | **"En yakın, yarım yukarı"** | Sabit kural: en yakına, tam yarıda yukarı (1,2345 → 1,235). Şirket ayarı açılmaz. Seçilmeyen yollar: aşağı yuvarlama; en yakın-çift (banker); kuralın Ayarlar'dan seçilmesi. |
| 4 | Tam yarıdaki negatif satır kg (ters kayıt, iade, düzeltme) nasıl yuvarlanır? (6825745714) | **"Ayna: −1,235"** | Negatif değer sıfırdan uzağa yuvarlanır (−1,2345 → −1,235); iade/düzeltme asıl satırın aynasıdır. Koordinatör kaydı seçilmeyen seçenekleri listelemez. |
| 5 | Adetle satılan profilde kg/m kaç ondalıkla kullanılır? (6825745714) | **"Eğer fiyatta daha fazla ondalık gerektirirse daha fazla olsun"**; netleştirme sorusuna **"Sen araştır"** | Sinan'ın devriyle koordinatör kararı (geri alınabilir): kg/m dahil her katsayı şirket ayarındaki katsayı ondalığıyla kullanılır, varsayılan 3, gerektiğinde Ayarlar'dan artırılır. Ayrıntı URN-D8'de. |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| URN-D7 | U-08 kantar sapmasının yönü | Tolerans kontrolü **mutlak** sapmayla yapılır: `\|tartılan − teorik\| / taban × 100`, BS-16 sınırıyla (`<=` varsayılan) karşılaştırılır; az tartım ve fazla tartım aynı şekilde tolerans dışı sayılır. Ekranda sapma **işaretli** (+ fazla, − eksik) gösterilir (kayıt yalnız ekranı söyler; belge çıktısında gösterim burada seçilmez). BS-17'deki işaretli formül `(tartılan − teorik) / taban` gösterilen sapmadır; kontrol onun mutlak değerini kullanır. Taban (BS-17) ve sınır (BS-16) Ayarlar'da kalır. | Ürün birim çevrim calculator'ı `WithinWeighbridgeTolerance` (#1089; davranış değişmez, xmldoc zaten mutlak kontrolü anlatır; xmldoc'a URN-D7 referansı eklenir); işaretli sapmayı veren hesap ve ekran gösterimi sonraki dilimde |
| URN-D8 | U-08 katsayının hesapta kullanılması | Birim başına **her** çevrim katsayısı — stok/satış birimi başına kg (terim BS-13'te değil, 6806590261 ve 6825745714'tedir; BS-13 "çevrim katsayısı" der); adet birimli üründe adet başına kg, yani F-A/F-D'nin parça kg sonucu; metre birimli üründe metre başına kg; **profil ve borunun kg/m'si dahil** (F-C sonucu veya ebat/kalınlık tablosu değeri) — **hesapta da** şirket ayarındaki katsayı ondalığıyla (`CoefficientDecimals`; BS-13 varsayılanı 3) yuvarlanmış değeriyle kullanılır. Fiyat hassasiyeti gerektiğinde ondalık Ayarlar'dan artırılır (ör. 4–5). **Görünen = hesaplanan:** kartta görünen katsayı hesapta aynen kullanılır (Sinan seçimi "Hesapta da 3 ondalık", soru 2). **Parça kg = yuvarlanmış kg/m × boy (m); sonuç aynı ondalığa yuvarlanır.** Örnek: 1,2345 kg/m, 6 m → 3 ondalıkta 1,235 × 6 = **7,410**; 4 ondalıkta 1,2345 × 6 = **7,407**. Girdiler (kalınlık, en, boy, kesit, yoğunluk) tam hassasiyette kalır; **yuvarlama yalnız katsayıda bir kez yapılır**: her katsayı üretildiği yerde bir kez yuvarlanır (profilde kg/m bir katsayıdır, parça kg ayrı bir katsayıdır; her biri bir kez) ve yuvarlanmış katsayı birim çevriminde (`UnitsToKg` / `KgToWholeUnits`) yeniden yuvarlanmadan kullanılır. Bu, BS-13'teki "ara adımlarda yuvarlama yoktur" kuralının **katsayı için istisnasıdır**; girdilerde ara yuvarlama yoktur. Kaynak: ondalık sayısı ve uygulama kapsamı (kg/m dahil, parça kg kuralı, girdiler) Sinan'ın devriyle **koordinatör kararıdır, geri alınabilir** (#66 6825745714; önceki öneri 6806590261); hesapta yuvarlanmış katsayının kullanılması Sinan seçimidir (soru 2, 6825138604). Gerekçe (koordinatör kaydı): katalog kg/m genelde 3 ondalıkla verilir ve kullanıcı kartta gördüğü değerle hesaplar; fark parça başına gram düzeyindedir. **Ürün bazlı geçersiz kılma bu dilimde yoktur**; ihtiyaç doğarsa ayrı öneri. Katsayının yuvarlama yönü için **"en yakın, yarım yukarı"** (0,3335 → 0,334) Sinan'ın seçimi değil, **yazar okumasıdır** (Sinan yalnız ondalığın hesapta kullanılmasını seçti) — geri alınabilir uygulama ayrıntısı; farklı bir yön istenirse NEEDS-SINAN. Yön **kodda sabit yazılmaz**: BS-13'e göre Ayarlar'dan okunur (bugün `RoundCoefficient` yönü `UnitConversionSettingsV1.DisplayRounding` ayarından alır); bu yazar okuması o ayarın önerilen varsayılan değeridir. Katsayı yönünün belge toplamı gösteriminden ayrı bir ayara alınması uygulama PR'ının işidir. Pozitif katsayıda "yarım yukarı" ile "sıfırdan uzağa" aynı sonucu verir. | Ürün birim çevrim calculator'ı `ProfileKgPerPiece` (F-D, #1130), `KgToWholeUnits` / `UnitsToKg` ve `RoundCoefficient` (#1089 kodu); ayrı K3 PR'ı, golden testlerle |
| URN-D9 | Belge satırı kg → Quantity yuvarlaması | Belge satırının kg değeri Quantity'ye (3 ondalık) **en yakına, tam yarıda yukarı** yuvarlanarak yazılır (1,2345 → 1,235). **Sabit kuraldır; şirket ayarı açılmaz.** Pozitif değerde bu, sıfırdan uzağa yuvarlamayla (.NET `MidpointRounding.AwayFromZero`) aynıdır; `ToPositiveInfinity` kullanılmaz. Negatif satır kg (ters kayıt, iade, düzeltme) tam yarıda **sıfırdan uzağa** yuvarlanır (−1,2345 → −1,235); ters kayıt asıl kaydın tam aynasıdır. Negatif yön **Sinan kararıdır**: "Ayna: −1,235" (#66 6825745714, soru 4). Bu yuvarlama BS-13'teki belge kg toplamı gösterimini (2 ondalık, Ayarlar) değiştirmez. Kural tek backend calculator'da yazılır; UI/rapor/form aynı sonucu kullanır. | Ürün birim çevrim calculator'ı (yeni satır yuvarlama metodu), belge satırı tüketicileri (Satış, Alış, Stok) |

## Uygulama PR'ı notları (r2/r3 review'ları)

Bu bölüm **uygulama önerisi / yazar okumasıdır**; yeni bir stok, para veya iade politikası seçmez.

- (DOCS-A P3-2) F-A ve F-B'nin girdileri (kalınlık, en, boy, yoğunluk) URN-D8'deki tam hassasiyetli girdi
  listesine dahildir; uygulama PR'ı bunları aynı listede yazar.
- (r2 DOCS-A P3-3, DOCS-B P3-2; r3 DOCS-A 6825953243, DOCS-B 6825972870 P2-1) main'de `UnitsToKg` ve
  `KgToWholeUnits` negatif değeri bugün reddeder; `Quantity<T>` ve Stok'taki kilogram değeri de negatif değeri
  reddeder. Ters yönlü satırlar için öneri:
  - **Tam ters kayıt (storno)** yeniden hesaplanmaz: asıl satırın saklanan miktarı aynen, ters yönle kullanılır.
  - **Kısmi iade ve düzeltme** saklı değerin kopyası olamaz; miktar yeniden hesaplanır ve URN-D9 ile yuvarlanır
    (tam yarıda sıfırdan uzağa; Sinan seçimi "Ayna: −1,235", soru 4). Satış'ta iade satırı zaten orantılı yeniden
    hesaplanır (SAT-D292).
  - Ters yönün **işaretle mi, hareket yönüyle mi** temsil edileceğine uygulama PR'ı ve ilgili modül (Satış, Alış,
    Stok) karar verir; bu belge seçmez.
  - URN-D9 golden'ına −1,2345 → −1,235 eklenir (calculator düzeyinde, negatif girdi kabul eden satır yuvarlama
    metodunda).
- (r3 P3-3) Quantity ölçeği koda `3` diye yazılmaz: `Quantity<T>.KilogramScale` ve Ayarlar'daki ölçek kullanılır.
  URN-D9 yuvarlama modu, `Money`'deki gibi adlı bir sabittir (`MidpointRounding.AwayFromZero`).

## Sınırlar

- URN-D8'deki katsayı yuvarlama **yönü** yazar okumasıdır, Sinan kararı değildir; para, stok, formül veya tolerans
  politikası olarak tahminle kilitlenmiş sayılmaz. Uygulama PR'ı bu etiketi korur.
- URN-D8'deki ondalık sayısı ve kapsamı (kg/m dahil, parça kg = yuvarlanmış kg/m × boy, girdiler tam hassasiyet)
  Sinan'ın devriyle koordinatör kararıdır ve **geri alınabilir** (#66 6825745714). Ürün bazlı katsayı ondalığı
  (geçersiz kılma) bu dilimde seçilmez; gerekirse ayrı öneri.
- URN-D9'daki negatif değer yönü Sinan kararıdır (#66 6825745714); r2'deki "yazar okuması" etiketinin yerine geçer.
- Bu belge kod değişikliği değildir. Calculator ve golden test değişiklikleri ayrı bir K3 PR'ıyla, iki bağımsız
  exact-head review ile gelir.
- İşaretli sapmanın ekranda hangi biçimde (renk, metin, ondalık) gösterileceği frontend dilimidir; burada seçilmez.
- İnsan kapıları değişmez: deployment, environment, secret, Settings → Actions'ta workflow etkinleştirme,
  grant/rol/Critical, production aktivasyonu, `OPEN-*` kapatma ve `.claude/settings.json`.
