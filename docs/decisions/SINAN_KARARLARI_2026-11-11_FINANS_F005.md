# Sinan Kararları — 2026-11-11 (F-005 resmî satış faturası elle giriş yolu; 2. tur Finans cevapları: geçmiş tarihli eksi bakiye, açılış dönemi, döviz kasası ve bozdurma, Masraf kalemi)

Bu belge önceki karar dosyalarını silmez. FNS-D17…FNS-D25'i ekler. FNS-D1…FNS-D16 geçerli kalır. Ayrıca şunlar
geçerli kalır:

- `SINAN_KARARLARI_2026-11-11_BEKLEYEN_SORULAR.md` BS-4: birincil yol muhasebenin kontrollü dosya aktarımı, yedek yol
  elle giriş + ikinci kişi onayı (maker≠approver).
- Issue #66 yorumu
  [`6808848950`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6808848950), madde 4: pilotta elle
  eşleştirme yapılır. Entegratörde kesilen faturanın ETTN/numarası ve PDF'i ERP'ye girilir; eşleşince fatura
  kesinleşir ve alacak oluşur. Entegratör/Mikro bağlantısı sonraki adımdır.

Bu belge, o iki kararın elle giriş yolunu uygulamak için gereken dört ayrıntıyı (FNS-D17…FNS-D20) ve Sinan'ın aynı gün
verdiği 2. tur Finans cevaplarını (FNS-D21…FNS-D25) karara bağlar. `KARAR_SICILI` satırı eklemez.

**Kaynak:** Sinan, cevapları 11 Kasım 2026'da Finans şeridinin Claude cloud oturumunda
(`session_015Usc2qRaBDjAUVC69G8xez`) `AskUserQuestion` ile verdi. Dört sorunun dördünde de soranın "(Önerilen)" diye
işaretlediği seçeneği seçti. Aynı turda "F-005 için kodlamaya başlayayım mı?" sorusunu "Başla, karar PR'ı da aç
(Önerilen)" diye cevapladı. "(Önerilen)" işaretleri soranındır (Claude). Bu kaynak FNS-D17…FNS-D20'yi bağlar.

**Kaynak (2. tur):** Sinan'ın 11 Kasım 2026 `AskUserQuestion` cevapları; koordinatör Issue #66 yorumu
[`6809898906`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6809898906) ("SINAN-KARARI 2026-11-11
(2. tur) — modül soruları", 2026-11-11T23:41:09Z) olarak kaydetti. Finans maddeleri 3–7 aşağıda aynen alıntılanır. Bu
kaynak FNS-D21…FNS-D25'i bağlar. Yorumun kendisi de belirttiği gibi bu cevaplar `OPEN-*` kapatma veya grant/rol
yetkisi değildir.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | F-005 pilotu: satış faturasını kesinleştirmek için elle girişte hangi alanlar zorunlu olsun? (Satış'ın eşleştirme adımı bugün KDV ve tevkifatı zorunlu istiyor; #66'daki pilot kararı yalnız ETTN/numara + PDF diyor.) | **Tam veri (Önerilen)** | "Numara, ETTN, cari, döviz, ödenecek tutar, TL karşılığı, KDV, tevkifat + PDF. Mevcut eşleştirme ve 0,05 TL tolerans kontrolü olduğu gibi çalışır." Seçilmeyen seçenek: **Yalnız ETTN/numara + PDF**: "Tutarlar taslaktan kabul edilir; eşleştirme kontrolü pilotta tutarları karşılaştırmaz. Satış tarafında kural değişikliği gerekir." |
| 2 | Elle girilen resmî fatura PDF'i nereye bağlansın? | **Finans resmî kaydına (Önerilen)** | "PDF, Finans'taki resmî satış faturası kaydına bağlanır; Satış faturası bu kayda kimlikle ulaşır. Satış tablolarına dokunulmaz." Seçilmeyen seçenek: **Satış faturasına**: "PDF doğrudan Satış faturasına bağlanır; Belge kasasına 'sales.invoice' türü eklenir, Satış şeridiyle ortak iş gerekir." |
| 3 | Aynı satış faturası için Finans'ın mevcut CSV taksit içe aktarması (FIN-F03) ile Satış kesinleşmesi iki ayrı alacak açabiliyor. Nasıl engellensin? | **Finans'ta tekil kontrol (Önerilen)** | "Alacak açılırken aynı kaynak belge/numara/ETTN için ikinci açık kalem reddedilir; iki yol da açık kalır." Seçilmeyen seçenek: **CSV'yi satış faturasına kapat**: "FIN-F03 CSV yalnız satış dışı belgeler için kullanılır; satış faturası alacağı yalnız kesinleşmeyle doğar." |
| 4 | Onaylanmış bir resmî fatura kaydı yanlış çıkarsa ne yapılsın? | **Şimdilik kapsam dışı (Önerilen)** | "Pilotta onaylı kayıt değiştirilemez; düzeltme/iptal yolu ayrı bir dilimde karara bağlanır (NEEDS-SINAN olarak #66'ya yazılır)." Seçilmeyen seçenek: **Ters kayıt + yeni kayıt**: "Onaylı kayıt iki kişiyle iptal edilir (eşleşmemişse), yerine yeni kayıt girilir. Eşleşmiş faturada iptal yasak." |

## 2. tur Finans cevapları (Issue #66, 6809898906, aynen)

| # | Cevap |
|---|---|
| 3 | "Pilotta resmî faturayı elle eşleştirme: ETTN/no + PDF + matrah/KDV/tevkifat girilir, ikinci kişi onaylar (BS-4 fallback aynen geçerli)." |
| 4 | "Geçmiş tarihli tahsilat/ödeme: kasa/banka eksi bakiye kontrolü yalnız kayıt anındaki güncel bakiyeyle yapılır. Geçmiş günlerde eksiye düşme reddedilmez, raporda uyarı olarak görünür." |
| 5 | "Açılış bakiyesi (kasa/banka/cari): yalnız Ayarlar'da "açılış dönemi" açıkken girilir. Finans Uzmanı girer, Finans Müdürü onaylar; dönem kapanınca ekran görünmez." |
| 6 | "TL/USD/EUR kasalar ve kasa/banka arası döviz bozdurma: ikisi de olacak; bozdurma tek işlemle, kurla kaydedilir; ayarlanabilir." |
| 7 | "Cariye bağlı olmayan ödemeler (vergi, SGK, maaş, kira, banka masrafı, kredi faizi): şimdilik tek bir "Masraf" kalemi + zorunlu açıklama ile girilir. Masraf yapısı projenin sonunda şekillenince düzeltilecek; geçiş yolu açık tutulur." |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| FNS-D17 | Elle girişte zorunlu alanlar | Elle girilen resmî satış faturası kaydı şu alanların **hepsini** taşır: resmî numara, ETTN, müşteri cari kimliği, para birimi, ödenecek tutar, TL karşılığı (ödenecek tutar), KDV TL karşılığı, tevkifat TL karşılığı ve PDF. Biri eksikse kayıt reddedilir. Satış'ın mevcut eşleştirme kuralları ve toleransı değişmez. #66 madde 4'teki "ETTN/numara + PDF" ifadesi bu alanlarla birlikte uygulanır; BS-4'ün dosya aktarımı alan listesiyle (numara, ETTN, matrah, KDV, tevkifat) uyumludur. | Finans resmî satış faturası kaydı (F-005 D1); `ISalesOfficialInvoiceReadPortV1` host adaptörü (D3) |
| FNS-D18 | PDF'in bağlandığı yer | PDF, Finans'taki resmî satış faturası kaydına bağlanır. Satış faturası bu kayda yalnız resmî numara/ETTN ile ve okuma portu üzerinden ulaşır. Satış tablolarına ve Belge kasasının Satış türlerine dokunulmaz. | Finans resmî kayıt + Belge kasası bağı (Finans sahipli); D2 (Satış belge türü) plandan çıkar |
| FNS-D19 | Çift alacak | FIN-F03 CSV içe aktarması ve Satış kesinleşmesi birlikte açık kalır. Finans, alacak açılırken aynı şirket için aynı kaynak belgeyi, resmî numarayı veya ETTN'yi taşıyan ikinci bir **açık** alacak kalemini reddeder. | Finans açık kalem kabulü (F-005 D5) |
| FNS-D20 | Onaylı kaydın düzeltilmesi | Pilotta onaylı resmî fatura kaydı değiştirilemez ve iptal edilemez. Düzeltme/iptal yolu ayrı bir dilimde karara bağlanır (`NEEDS-SINAN`, Issue #66). | Finans resmî kayıt (D1): onaylı kayıtta değiştirme/iptal komutu yoktur |
| FNS-D21 | Elle eşleştirmede matrah ve ikinci kişi (2. tur m.3) | 2. tur madde 3, FNS-D17'yi teyit eder ve **matrahı** zorunlu alanlara ekler: elle girilen kayıt FNS-D17'deki alanlara ek olarak matrahın TL karşılığını da taşır; eksikse kayıt reddedilir. Kaydı giren onaylayamaz; ikinci kişi onaylar (BS-4 maker≠approver aynen). Matrah için yeni karşılaştırma kuralı veya tolerans seçilmedi (Sınırlar). | Finans resmî satış faturası kaydı (F-005 D1: alan + doğrulama); D3 host adaptörü |
| FNS-D22 | Geçmiş tarihli kayıtta eksi bakiye (2. tur m.4) | FNS-D11'in eksi bakiye kontrolü, geçmiş tarihli tahsilat/ödemede de yalnız **kayıt anındaki güncel bakiyeyle** yapılır. İşlemin tarihindeki veya sonraki geçmiş günlerde bakiyenin eksiye düşmesi kaydı reddetmez; raporda uyarı olarak görünür. Bu, `FINANS_HAZIRLIK_PLANI_v1.md` R-4'teki "sonraki her gün" (fail-closed) okumasının yerine geçer. KMH/BCH limit istisnası (FNS-D4, FNS-D11) aynen kalır. Bugünkü her-gün kontrolünün gevşetilmesi ayrı bir uygulama dilimidir. | Finans kasa/banka kayıt komutları (API-25/31/32/40) bakiye kontrolü; kasa/banka raporu uyarısı |
| FNS-D23 | Açılış bakiyesi (2. tur m.5) | FNS-D12 yorumu teyit edilir: kasa, banka ve cari açılış bakiyesi yalnız Ayarlar'daki "açılış dönemi" açıkken girilir. Finans Uzmanı girer, Finans Müdürü onaylar (maker≠approver); dönem kapanınca açılış ekranı görünmez. | Finans açılış kaydı (MIG-02, F8); Ayarlar "açılış dönemi" anahtarı (Ayarlar şeridi) |
| FNS-D24 | Döviz kasası ve bozdurma (2. tur m.6) | TL, USD ve EUR kasaları olur. Kasa/banka arasında döviz bozdurma **tek işlemle** ve kurla kaydedilir (iki ayrı hareket girilmez). İkisi de ayarlanabilir. `ASSUMPTION-FIN-CASH` ve `ASSUMPTION-FIN-FX-TRANSFER` bu kararla Sinan kararına bağlanır. | Finans kasa master (API-30); virman/döviz alış-satış (API-32/34) |
| FNS-D25 | Cariye bağlı olmayan ödeme (2. tur m.7) | Vergi, SGK, maaş, kira, banka masrafı, kredi faizi gibi cariye bağlı olmayan ödemeler şimdilik **tek bir "Masraf" kalemiyle** ve **zorunlu açıklamayla** girilir. Masraf yapısı kurulunca bu kayıtların yeni yapıya geçirilebileceği yol açık tutulur. Uygulama ayrıntısı (geri alınabilir): "Masraf" kalemi metin olarak değil bir kimlikle saklanır. | Finans masraf ödemesi/iadesi (API-25); FNS-D9 kart harcamasının masraf türü alanı |

## Sınırlar

- Elle girilen KDV, tevkifat ve TL tutarları için Satış'ın mevcut eşleştirme toleransı dışında yeni bir tolerans
  seçilmedi. Satış'ın kuralı (FNS-D17) aynen uygulanır.
- Kim girer, kim onaylar: `finance.official-sales-invoices.record` ve `.approve` yetkileri **kayıt edilir, kimseye
  verilmez**. Persona/rol ataması insan kapısıdır (`NEEDS-SINAN`).
- PDF saklama süresi ve imha bu belgenin kapsamında değildir. Belge kasasının mevcut saklama politikası geçerlidir;
  ayrı politika gerekirse `NEEDS-SINAN`.
- Dosya aktarımı yolu (BS-4 birincil yol; CSV v2 ve F-028/B-04c Muhasebe şeridi devri) bu belgenin kapsamında
  değildir.
- FNS-D21: matrahın Satış taslağıyla nasıl karşılaştırılacağı (kural, tolerans) seçilmedi. Satış'ın mevcut eşleştirme
  kuralı değişmez; matrah kayıtta taşınır ve gösterilir. Karşılaştırma gerekirse ayrı karar (`NEEDS-SINAN`).
- FNS-D22: raporda uyarının biçimi ve hangi raporda görüneceği uygulama ayrıntısıdır (geri alınabilir). Kayıt anındaki
  kontrolün eşzamanlı kayıtlara karşı veritabanı tarafında korunması (FNS-D11) değişmez.
- FNS-D23: "açılış dönemi" anahtarının sahibi Ayarlar modülüdür; Finans yalnız okur. Kim Finans Uzmanı/Müdürü olur
  (persona → grant) insan kapısıdır.
- FNS-D24: kur kaynağı, kur farkı ve bozdurmanın muhasebe karşılığı bu belgede seçilmedi; mevcut kur kararları
  (Ayarlar kur kayıtları) geçerlidir. "Ayarlanabilir" ifadesinin hangi ayarlar olduğu uygulama PR'ında `OPEN-*`/
  `NEEDS-SINAN` olarak işaretlenir; para davranışı tahminle etkinleştirilmez.
- FNS-D25: ayrı masraf yapısı (madde 8: masraf tipleri, tek düzen hesap planı ve mizan uyumu, Alış/Satış kullanımı)
  `NEEDS-SPEC`tir (ChatGPT şeridi). `FINANS_HAZIRLIK_PLANI_v1.md` R-5 ve FNS-D9'daki "iki seviyeli masraf türü" o
  yapı gelene kadar tek "Masraf" kalemiyle karşılanır. Geçiş (eski kayıtların yeni türlere taşınması) ayrı dilimdir.
- Onay bekleyen kaydın girene ait geri çekme yolu bu belgede karara bağlanmadı. D1 bu komutu açmaz; gerekirse
  ayrı kararla eklenir.
- Deployment, environment, secret, Settings'te etkinleştirme, grant/rol/Critical, production aktivasyonu, `OPEN-*`
  kapatma ve `.claude/settings.json` kapıları değişmez.
