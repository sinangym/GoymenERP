# Sinan Kararları — 2026-11-02 (fatura kesme yetkisi; çapraz dövizde risk çevrimi)

Bu belge önceki karar dosyalarını silmez; SAT-D202 ve SAT-D203'ü ekler. Persona kartındaki son açık hücre grubunu
(**NS-11**, fatura kesme) ve EK31 SAT-D200'ün açık bıraktığı çapraz döviz durumunu cevaplar. SAT-D142, EK28 SAT-D186/D187,
EK29 SAT-D191, EK31 SAT-D200 ve SAT-D47/D53/D54/D80 geçerlidir.

**Bilinçli değişiklikler (Sinan'ın açık cevaplarıyla):**
- **EK31 SAT-D200'ün uygulama ayrıntısı:** "limit para birimi ile tutarın para birimi farklı iki dövizse çapraz kur bu
  kararla açılmaz; o tutar 'hesaplanamadı' kalır" ifadesi yerine çevrim **TL üzerinden** yapılır (SAT-D203).

**Kaynak:** Sinan'ın 2 Kasım 2026'da Claude Code cloud oturumunda (oturum `session_01SPADrzecggx3apH2gfNW7b`) verdiği
cevaplar: bir tur, iki soru. Sinan'a her seçeneğin adı **ve açıklaması** birlikte gösterildi.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Faturayı kim kesebilsin (e-Fatura/e-Arşiv düzenleme)? Fatura dalgasına başlamadan bu hücrenin dolması gerekiyor. | Muhasebe keser (Önerilen) | "Muhasebe faturayı hazırlar ve keser; Muhasebe Müdürü, Genel Müdür ve Admin de kesebilir. Satış faturayı görür ama kesemez." (seçilmeyenler: "Satış hazırlar, Muhasebe keser" — "Satış Uzmanı fatura taslağını oluşturur, Muhasebe kontrol edip keser (hazırlayan ≠ kesen)."; "Satış da kesebilsin" — "Satış Uzmanı ve Satış Müdürü de doğrudan fatura kesebilir.") |
| 2 | Tutar ile müşterinin risk limiti iki farklı dövizdeyse (ör. tutar USD, limit EUR) risk nasıl hesaplansın? | TL üzerinden çevir (Önerilen) | "USD tutar önce TCMB kuruyla TL'ye, sonra TL EUR kuruna bölünerek EUR'ya çevrilir (ikisi de kontrol günü kuru). Kurlardan biri yoksa 'hesaplanamadı'." (seçilmeyenler: "Hesaplanamadı kalsın" — "Böyle durumda risk hesaplanmaz, sipariş risk onayına düşer. Nadir bir durumsa yeterli."; "Böyle sipariş olmasın" — "Sipariş para birimi müşterinin limit para birimiyle ya da TL ile aynı olmak zorunda.") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D202 | Fatura kesme | Faturayı **Muhasebe** hazırlar ve keser; **Muhasebe Müdürü, Genel Müdür ve Admin** de kesebilir. **Satış** (Satış Uzmanı, Satış Müdürü) faturayı **görür ama kesemez** (soru 1). Bu, persona kartındaki NS-11 hücre grubunu cevaplar. Uygulama ayrıntısı (geri alınabilir): "kesme" SAT-D142/API-32'deki ERP fatura taslağını hazırlama ve kesme adımlarını kapsar; Satış'ın "görür" hakkı mevcut satış/fatura okuma sınırını aşmaz. Diğer modül müdürleri (Satın Alma, Üretim, Stok, Lojistik, Finans) bu kararla fatura kesme almaz. EK28 SAT-D186'nın "Muhasebe Müdürü onaylayana kadar fatura kesilemez" kapısı aynen kalır. e-Belge sağlayıcısına gönderim ve Critical sayılan adlar insan kapısındadır | Persona kartı NS-11, fatura dalgası (API-32…37) |
| SAT-D203 | Çapraz dövizde risk | Tutar ile kredi limiti farklı iki dövizdeyse (ör. tutar USD, limit EUR) tutar önce **TCMB kuruyla TL'ye**, sonra TL **limit dövizinin kuruna bölünerek** limit para birimine çevrilir; **iki kur da kontrol günü kurudur** (SAT-D47/SAT-D54) (soru 2). Kurlardan biri çözülemezse limit para birimindeki toplam hesaplanamaz ve risk sonucu "hesaplanamadı" kalır (SAT-D80/SAT-D47; tutarı atlayıp kalanla karar verilmez). Uygulama ayrıntısı (geri alınabilir): yuvarlama SAT-D11'deki tek yuvarlama kuralına uyar; ara TL değeri yuvarlanmadan taşınır | Sales risk (API-06/API-07) |

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings etkinleştirme, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`, dış kaynak ve e-Belge bağlantısının açılması) değiştirmez. Persona
  tanımı karardır; gerçek kişilere atama insan kapısındadır.
