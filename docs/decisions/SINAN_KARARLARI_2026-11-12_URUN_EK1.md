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

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Kantar sapması mutlak mı, işaretli mi? BS-17 formülü işaretli `(tartılan − teorik) / taban`; #1089 hesabı mutlak `\|tartılan − teorik\|` karşılaştırıyor. | **"İki yön de"** | Az tartım da fazla tartım da tolerans dışıysa uyarı verilir: tolerans kontrolü mutlak değerle yapılır (`\|tartılan − teorik\| / taban`). Ekranda sapma işaretli (+/−) gösterilir. #1089'daki mutlak hesap doğrulanır. Seçilmeyen yol: yalnız tek yönün (ör. yalnız eksik tartımın) uyarı vermesi. |
| 2 | BS-13 "katsayı 3 ondalık" kuralı hesapta mı, yalnız gösterimde mi? Örnek: 0,999 kg ÷ 0,333 = 3 adet; 0,999 ÷ (1/3 tam değer) = 2 adet. | **"Hesapta da 3 ondalık"** | Katsayı hesapta 3 ondalığa yuvarlanmış değeriyle kullanılır; görünen = hesaplanan (örnek: 0,999 kg ÷ 0,333 = 3 adet). #1089'daki tam-değer hesabı bu karara göre değişir. Seçilmeyen yol: hesapta tam değer, 3 ondalık yalnız gösterimde (#1089'un bugünkü davranışı). |
| 3 | Belge satırının kg'si Quantity'ye (3 ondalık) kaydedilirken hangi kuralla yuvarlanır ve bu ayar mı olmalı? | **"En yakın, yarım yukarı"** | Sabit kural: en yakına, tam yarıda yukarı (1,2345 → 1,235). Şirket ayarı açılmaz. Seçilmeyen yollar: aşağı yuvarlama; en yakın-çift (banker); kuralın Ayarlar'dan seçilmesi. |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| URN-D7 | U-08 kantar sapmasının yönü | Tolerans kontrolü **mutlak** sapmayla yapılır: `\|tartılan − teorik\| / taban × 100`, BS-16 sınırıyla (`<=` varsayılan) karşılaştırılır; az tartım ve fazla tartım aynı şekilde tolerans dışı sayılır. Ekranda ve belgede sapma **işaretli** (+ fazla, − eksik) gösterilir. BS-17'deki işaretli formül `(tartılan − teorik) / taban` gösterilen sapmadır; kontrol onun mutlak değerini kullanır. Taban (BS-17) ve sınır (BS-16) Ayarlar'da kalır. | Ürün birim çevrim calculator'ı `WithinWeighbridgeTolerance` (#1089; davranış değişmez, xmldoc hizalanır); işaretli sapmayı veren hesap ve ekran gösterimi sonraki dilimde |
| URN-D8 | U-08 katsayının hesapta kullanılması | Çevrim katsayısı (adet başına kg) **hesapta da** Ayarlar'daki katsayı ondalığına (BS-13 varsayılanı 3) yuvarlanmış değeriyle kullanılır; ekranda görünen katsayı ile hesaplanan aynıdır. Bu, BS-13'teki "ara adımlarda yuvarlama yoktur" kuralının **katsayı için istisnasıdır**; diğer ara adımlar (kesit, kg/m, parça kg) yuvarlanmadan kalır. Katsayının yuvarlama yönü için **"en yakın, yarım yukarı"** (0,3335 → 0,334) Sinan'ın seçimi değil, **yazar okumasıdır** (koordinatör kaydı aynen böyle etiketler; Sinan yalnız 3 ondalığın hesapta kullanılmasını seçti) — geri alınabilir uygulama ayrıntısı; farklı bir yön istenirse NEEDS-SINAN. Ondalık sayısı Ayarlar'dan değişebilir (BS-13). | Ürün birim çevrim calculator'ı `KgToWholeUnits` / `UnitsToKg` ve `RoundCoefficient` (#1089 kodu); ayrı K3 PR'ı, golden testlerle |
| URN-D9 | Belge satırı kg → Quantity yuvarlaması | Belge satırının kg değeri Quantity'ye (3 ondalık) **en yakına, tam yarıda yukarı** yuvarlanarak yazılır (1,2345 → 1,235). **Sabit kuraldır; şirket ayarı açılmaz.** Bu yuvarlama BS-13'teki belge kg toplamı gösterimini (2 ondalık, Ayarlar) değiştirmez. Kural tek backend calculator'da yazılır; UI/rapor/form aynı sonucu kullanır. | Ürün birim çevrim calculator'ı (yeni satır yuvarlama metodu), belge satırı tüketicileri (Satış, Alış, Stok) |

## Sınırlar

- URN-D8'deki katsayı yuvarlama **yönü** yazar okumasıdır, Sinan kararı değildir; para, stok, formül veya
  tolerans politikası olarak tahminle kilitlenmiş sayılmaz. Uygulama PR'ı bu etiketi korur.
- Bu belge kod değişikliği değildir. Calculator ve golden test değişiklikleri ayrı bir K3 PR'ıyla, iki bağımsız
  exact-head review ile gelir.
- İşaretli sapmanın ekranda hangi biçimde (renk, metin, ondalık) gösterileceği frontend dilimidir; burada seçilmez.
- İnsan kapıları değişmez: deployment, environment, secret, Settings → Actions'ta workflow etkinleştirme,
  grant/rol/Critical, production aktivasyonu, `OPEN-*` kapatma ve `.claude/settings.json`.
