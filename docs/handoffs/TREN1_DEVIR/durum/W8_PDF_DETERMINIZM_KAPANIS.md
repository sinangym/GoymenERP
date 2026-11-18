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
