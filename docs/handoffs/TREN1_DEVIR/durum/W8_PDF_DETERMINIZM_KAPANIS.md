# W8 — Barkod PDF determinizm kapanışı

Taban: `203d0536b0a02532c2d771d855ba6baeaeff9187`.
Kapsam: ilk tam PDF birim koşumunda aynı istek için farklı bayt üreten iki barkod testi.

İlk hata kanıtı korunur: `Repeated_rendering_of_the_same_transport_and_identity_has_identical_vault_content` (ilk fark 1413) ve `A_corporate_barcode_request_keeps_the_exact_same_content_on_a_lost_answer_retry` (ilk fark 2144). Mevcut `Assert.Equal(byte[], byte[])` beklentileri değişmedi.

## Kök neden ve dar düzeltme

Projede kullanılan paket **PDFsharp 6.2.4**; repo provenance'ının upstream commit'i `9c3863a71b12067ff36e68a92c1a1f260114e2d1`. Bu sürümde [PdfFontDescriptorCache](https://github.com/empira/PDFsharp/blob/9c3863a71b12067ff36e68a92c1a1f260114e2d1/src/foundation/src/PDFsharp/src/PdfSharp/Pdf.Advanced/PdfFontDescriptorCache.cs) descriptor oluştururken koşulsuz subset adı üretir. [PdfFontDescriptor](https://github.com/empira/PDFsharp/blob/9c3863a71b12067ff36e68a92c1a1f260114e2d1/src/foundation/src/PDFsharp/src/PdfSharp/Pdf.Advanced/PdfFontDescriptor.cs) bu altı harfi `Guid.NewGuid()` ile üretir. [Resmî font gömme açıklamasındaki](https://docs.pdfsharp.net/PDFsharp/Topics/Fonts/Font-Embedding.html) `EmbedCompleteFontFile` seçimi font dosyasının tamamını korur; bu sürümün rastgele descriptor adını kaldırmaz.

`BarcodeLabelPdfGenerator` zaten istek tarihini ve teknik PDF kimliklerini sabitler. Yalnız kendi sayfasındaki tek, tam gömülen Noto Sans fontunun Type0 `/BaseFont`, CID `/BaseFont` ve descriptor `/FontName` adları kaydetmeden önce aynı gerçek tam font adına çevrilir. Üç alanın özgün adları ve font türleri önce doğrulanır. Font programı, glyph eşlemesi, ToUnicode, çizim içerikleri, barkod ve ithal logo/antet form kaynakları değiştirilmez. Bayt dizisinde regex/replace yapılmaz; global font ayarı, yeni paket veya ortam değişikliği yoktur.

Dayanak: DEC-DOC-004/DEC-DOC-010'un sabit teknik kimlik, gömülü Noto Sans ve sürümlü onaylı asset sınırı; aynı anahtarın kaybolan yanıt tekrarında aynı içerik olması. Gerçek kurumsal asset onayı veya UAT tamamlanmış sayılmaz.

## Regresyon kaynağı ve doğrulama

- İki ilk başarısız testin exact-byte eşitliği aynen korunur.
- Araya başka istek üretimi girse de aynı istek aynı baytları verir; tam gömülü font baytları vendored dosyayla aynıdır ve üç font adı/ToUnicode bağı korunur.
- Metin/font içeren gerçek PDF antet formunun özgün font adı ve Türkçe metni korunur; tekrar üretim yine bayt düzeyinde eşittir.
- `git diff --check`: exit 0.
- Kaynak checkpoint'i hazırlanırken derleme/test **NOT_RUN**; ağır süreç izni bekleniyor. SQL, browser, CI ve bağımsız inceleme bu çalışmanın kanıtı değildir.

## İkinci kaynak sınırı — catalog XMP kimlikleri

Sonraki donuk taban `1b55f5e109148f8d7dcc8d394481087d00f31993` üzerindeki gerçek
PDF koşusu 81 testte 77 başarılı, 4 başarısız, 0 skip verdi. İki özgün exact-byte
testi ve iki yeni font regresyonu kırmızı kaldı; ilk font etiketi farkı giderilmişti.
Bu ilk başarısız TRX ve native exit1 kanıtı korunur, önceki NOT_RUN kaydı başarıya
çevrilmez.

PDFsharp **aynı 6.2.4 upstream commit'inde**
[PdfMetadata.GenerateXmp](https://github.com/empira/PDFsharp/blob/9c3863a71b12067ff36e68a92c1a1f260114e2d1/src/foundation/src/PDFsharp/src/PdfSharp/Pdf/PdfMetadata.cs)
iki UUID'yi `Guid.NewGuid` ile üretir.
[PdfDocument.PrepareForSave](https://github.com/empira/PDFsharp/blob/9c3863a71b12067ff36e68a92c1a1f260114e2d1/src/foundation/src/PDFsharp/src/PdfSharp/Pdf/PdfDocument.cs)
catalog `/Metadata` nesnesini her Save sırasında yeniden kurar. Public DocumentEvents
yalnız page/graphics olayları sağlar; bu sürümde bu iki değeri sonradan bağlayacak
public save/metadata customization hook'u bulunmadı. Kaynaklar gerçek sürümün
sabit upstream commit'inden okundu; yeni paket, kütüphane patch'i veya reflection yoktur.

İki ham sentetik PDF 293354'er byte idi; farklı 57 byte'ın tamamı yalnız catalog XMP
`xmpMM:DocumentID` ve `xmpMM:InstanceID` UUID değerleri içindeydi. Font, sayfa,
trailer ve tarih farkı yoktu. Bu ham çift ve dış inceleme çıktısı korunur; üretim
belgesi veya müşteri verisi kullanılmadı.

Dar düzeltme, Save sonrası **yalnız belgenin kendi typed catalog metadata stream'ini**
mevcut TechnicalId'ye bağlar. `PdfInternals.GetObjectID` ile gerçek indirect object
kimliği, plain XML type/subtype, filtersiz stream, tekil byte eşleşmesi, object/stream
başlangıç-bitiş sınırları ve Length doğrulanır. Tekil xmpMM bölümündeki iki alanın
her biri tam 36 karakterlik geçerli UUID olmalıdır; uyuşmazlıkta belge üretilmez.
Yalnız bu iki eşit uzunluklu değer yazılır; tüm PDF'de regex/global replace yoktur.
İthal form metadata/font/içerik stream'leri, document Info tarihleri, iki trailer ID,
xref ve dosya uzunluğu bu işlemle değişmez. İlk font düzeltmesi aynen korunur.

Ham ilk çıktı ile düzeltilmiş çıktı yine 293354 byte'tır; değişen 60 byte yalnız
iki UUID'nin toplam 72 byte aralığındadır; xref ve trailer birebir aynıdır.
Düzeltilmiş aynı istek çiftinin tüm byte'ları eşittir. Yeni regresyon, yalnız
TechnicalId değiştiğinde iki XMP ve iki trailer kimlik aralığı dışında **her byte'ın**
ve dolayısıyla offsetlerin aynı kaldığını doğrular. Eski byte-equality ve font/
Türkçe/antet kontrolleri silinmedi veya gevşetilmedi. Geçici ham-PDF yakalama kodu
son test kaynağından çıkarıldı.

## Bu dar kaynak için gerçek yerel sonuç

- Locked restore exit0; final PDF test-projesi build exit0, 0 uyarı/0 hata.
- `dotnet test tests/GoymenERP.DocumentGeneration.Pdf.Tests/GoymenERP.DocumentGeneration.Pdf.Tests.csproj --no-build --no-restore`:
  **82/82 başarılı, 0 başarısız, 0 skip**, native exit0; özgün 81 + bir yeni regresyon.
- Her ağır süreç öncesi boş fiziksel RAM en az2GiB idi; tek PDF-only .NET süreci çalıştı.
  İlk tanı probe'u exit1 ve ilk erişilemeyen ObjectID API denemesinin build exit1
  kayıtları korundu; public PdfInternals API düzeltmesinden sonra yeni native log/TRX
  alındı. Hata çıktıları son başarılı dosyalarla ezilmedi.
- `git diff --check`: exit0. Son birleşik head'de recovery/pin ve ilgili kontroller
  tekrar bağlanmalıdır; bu sonuç tüm tren kabulü veya bağımsız exact-head K3 imzası
  değildir. SQL, Node, browser ve CI bu dar işte **NOT_RUN**; production ve kurumsal
  asset aktivasyonu değişmedi.
