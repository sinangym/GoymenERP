# Sinan Kararları — 2026-11-11 (F-005 resmî satış faturası elle giriş yolu: zorunlu alanlar, PDF bağı, çift alacak, düzeltme)

Bu belge önceki karar dosyalarını silmez. FNS-D17…FNS-D20'yi ekler. FNS-D1…FNS-D16 geçerli kalır. Ayrıca şunlar
geçerli kalır:

- `SINAN_KARARLARI_2026-11-11_BEKLEYEN_SORULAR.md` BS-4: birincil yol muhasebenin kontrollü dosya aktarımı, yedek yol
  elle giriş + ikinci kişi onayı (maker≠approver).
- Issue #66 yorumu
  [`6808848950`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6808848950), madde 4: pilotta elle
  eşleştirme yapılır. Entegratörde kesilen faturanın ETTN/numarası ve PDF'i ERP'ye girilir; eşleşince fatura
  kesinleşir ve alacak oluşur. Entegratör/Mikro bağlantısı sonraki adımdır.

Bu belge, o iki kararın elle giriş yolunu uygulamak için gereken dört ayrıntıyı karara bağlar. `KARAR_SICILI` satırı
eklemez.

**Kaynak:** Sinan, cevapları 11 Kasım 2026'da Finans şeridinin Claude cloud oturumunda
(`session_015Usc2qRaBDjAUVC69G8xez`) `AskUserQuestion` ile verdi. Dört sorunun dördünde de soranın "(Önerilen)" diye
işaretlediği seçeneği seçti. Aynı turda "F-005 için kodlamaya başlayayım mı?" sorusunu "Başla, karar PR'ı da aç
(Önerilen)" diye cevapladı. "(Önerilen)" işaretleri soranındır (Claude).

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | F-005 pilotu: satış faturasını kesinleştirmek için elle girişte hangi alanlar zorunlu olsun? (Satış'ın eşleştirme adımı bugün KDV ve tevkifatı zorunlu istiyor; #66'daki pilot kararı yalnız ETTN/numara + PDF diyor.) | **Tam veri (Önerilen)** | "Numara, ETTN, cari, döviz, ödenecek tutar, TL karşılığı, KDV, tevkifat + PDF. Mevcut eşleştirme ve 0,05 TL tolerans kontrolü olduğu gibi çalışır." Seçilmeyen seçenek: **Yalnız ETTN/numara + PDF**: "Tutarlar taslaktan kabul edilir; eşleştirme kontrolü pilotta tutarları karşılaştırmaz. Satış tarafında kural değişikliği gerekir." |
| 2 | Elle girilen resmî fatura PDF'i nereye bağlansın? | **Finans resmî kaydına (Önerilen)** | "PDF, Finans'taki resmî satış faturası kaydına bağlanır; Satış faturası bu kayda kimlikle ulaşır. Satış tablolarına dokunulmaz." Seçilmeyen seçenek: **Satış faturasına**: "PDF doğrudan Satış faturasına bağlanır; Belge kasasına 'sales.invoice' türü eklenir, Satış şeridiyle ortak iş gerekir." |
| 3 | Aynı satış faturası için Finans'ın mevcut CSV taksit içe aktarması (FIN-F03) ile Satış kesinleşmesi iki ayrı alacak açabiliyor. Nasıl engellensin? | **Finans'ta tekil kontrol (Önerilen)** | "Alacak açılırken aynı kaynak belge/numara/ETTN için ikinci açık kalem reddedilir; iki yol da açık kalır." Seçilmeyen seçenek: **CSV'yi satış faturasına kapat**: "FIN-F03 CSV yalnız satış dışı belgeler için kullanılır; satış faturası alacağı yalnız kesinleşmeyle doğar." |
| 4 | Onaylanmış bir resmî fatura kaydı yanlış çıkarsa ne yapılsın? | **Şimdilik kapsam dışı (Önerilen)** | "Pilotta onaylı kayıt değiştirilemez; düzeltme/iptal yolu ayrı bir dilimde karara bağlanır (NEEDS-SINAN olarak #66'ya yazılır)." Seçilmeyen seçenek: **Ters kayıt + yeni kayıt**: "Onaylı kayıt iki kişiyle iptal edilir (eşleşmemişse), yerine yeni kayıt girilir. Eşleşmiş faturada iptal yasak." |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| FNS-D17 | Elle girişte zorunlu alanlar | Elle girilen resmî satış faturası kaydı şu alanların **hepsini** taşır: resmî numara, ETTN, müşteri cari kimliği, para birimi, ödenecek tutar, TL karşılığı (ödenecek tutar), KDV TL karşılığı, tevkifat TL karşılığı ve PDF. Biri eksikse kayıt reddedilir. Satış'ın mevcut eşleştirme kuralları ve toleransı değişmez. #66 madde 4'teki "ETTN/numara + PDF" ifadesi bu alanlarla birlikte uygulanır; BS-4'ün dosya aktarımı alan listesiyle (numara, ETTN, matrah, KDV, tevkifat) uyumludur. | Finans resmî satış faturası kaydı (F-005 D1); `ISalesOfficialInvoiceReadPortV1` host adaptörü (D3) |
| FNS-D18 | PDF'in bağlandığı yer | PDF, Finans'taki resmî satış faturası kaydına bağlanır. Satış faturası bu kayda yalnız resmî numara/ETTN ile ve okuma portu üzerinden ulaşır. Satış tablolarına ve Belge kasasının Satış türlerine dokunulmaz. | Finans resmî kayıt + Belge kasası bağı (Finans sahipli); D2 (Satış belge türü) plandan çıkar |
| FNS-D19 | Çift alacak | FIN-F03 CSV içe aktarması ve Satış kesinleşmesi birlikte açık kalır. Finans, alacak açılırken aynı şirket için aynı kaynak belgeyi, resmî numarayı veya ETTN'yi taşıyan ikinci bir **açık** alacak kalemini reddeder. | Finans açık kalem kabulü (F-005 D5) |
| FNS-D20 | Onaylı kaydın düzeltilmesi | Pilotta onaylı resmî fatura kaydı değiştirilemez ve iptal edilemez. Düzeltme/iptal yolu ayrı bir dilimde karara bağlanır (`NEEDS-SINAN`, Issue #66). | Finans resmî kayıt (D1): onaylı kayıtta değiştirme/iptal komutu yoktur |

## Sınırlar

- Elle girilen KDV, tevkifat ve TL tutarları için Satış'ın mevcut eşleştirme toleransı dışında yeni bir tolerans
  seçilmedi. Satış'ın kuralı (FNS-D17) aynen uygulanır.
- Kim girer, kim onaylar: `finance.official-sales-invoices.record` ve `.approve` yetkileri **kayıt edilir, kimseye
  verilmez**. Persona/rol ataması insan kapısıdır (`NEEDS-SINAN`).
- PDF saklama süresi ve imha bu belgenin kapsamında değildir. Belge kasasının mevcut saklama politikası geçerlidir;
  ayrı politika gerekirse `NEEDS-SINAN`.
- Dosya aktarımı yolu (BS-4 birincil yol; CSV v2 ve F-028/B-04c Muhasebe şeridi devri) bu belgenin kapsamında
  değildir.
- Onay bekleyen kaydın girene ait geri çekme yolu bu belgede karara bağlanmadı. D1 bu komutu açmaz; gerekirse
  ayrı kararla eklenir.
- Deployment, environment, secret, Settings'te etkinleştirme, grant/rol/Critical, production aktivasyonu, `OPEN-*`
  kapatma ve `.claude/settings.json` kapıları değişmez.
