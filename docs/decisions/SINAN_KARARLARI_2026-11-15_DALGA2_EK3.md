# Sinan Kararları — 2026-11-15 (Dalga 2 ek 3: üretilen belgenin kasaya yazılması, sistem PDF'i taraması, AI paneli ve AI sınırları)

Bu belge önceki karar dosyalarını silmez; KLN-D59, KLN-D60, AI-D8 ve AI-D9 kararlarını ve ASSUMPTION-AI-POLICYREF-1
varsayımını ekler. DEC-DOC-009 (dosya beyaz listesi, MIME doğrulama, malware taraması, imzalı kısa erişim), AI-D1…AI-D7
(özellikle AI-D3/AI-D4 veri sınıfları, AI-D6 insan kapısı) ve KLN-D51 (gözlemlenebilirlik değerleri Ayarlar'dan
değişir) aynen geçerlidir.

**Kaynak:** Sinan, 15 Kasım 2026, bu Claude Code oturumunda soru penceresiyle cevapladı (SINAN-CEVAP 24). Sorular
Platform takımının dalga 2 raporundaki NEEDS-SINAN maddelerinden geldi. "(Önerilen)" işaretleri soranındır. Dört
sorunun dördünde önerilen seçenek seçildi.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 24.1 | Sistem bir belgeyi kendisi üretip (örn. satış siparişi PDF'i, etiket) dosya kasasına kaydederken hangi yetki aransın? Şu an geçici olarak "dosya yükleme" yetkisi isteniyor. | **Üreten işlemin yetkisi yeter (Önerilen)** | "PDF'i üreten komutun (örn. sipariş PDF'i alma) kendi yetkisi yeterlidir; ayrıca dosya yükleme yetkisi aranmaz. Kayıt, işlemi yapan kullanıcı adına denetime yazılır." Seçilmeyenler: "Dosya yükleme yetkisi de gerekli" — üreten komutun yetkisine ek olarak kullanıcının dosya yükleme yetkisi de olmalı (bugünkü geçici uygulama); "Ayrı 'belge üretme' yetkisi" — yeni ayrı bir izin açılır, tek tek kişilere verilebilir. |
| 24.2 | Sistemin kendi ürettiği PDF'ler de, kullanıcının yüklediği dosyalar gibi virüs taramasından geçsin mi? Bugün geçiyor; tarama servisi yoksa canlıda PDF kaydedilemiyor (503). | **Taramasız, sistem işaretli (Önerilen)** | "Sistemin kendi ürettiği PDF taranmaz; 'sistem üretimi' olarak işaretlenir, içerik özeti (hash) saklanır, tür/boyut kontrolü sürer. Kullanıcı yüklemeleri taranmaya devam eder." Seçilmeyen: "Hepsi taransın" — bugünkü gibi, üretilen PDF de taranır; tarama servisi yoksa PDF kaydedilmez/indirilemez. |
| 24.3 | AI paneli (`ai.panel.use`) kimlerde olsun? (AI canlıda yine kapalı başlar; açılması ayrıca Sinan'ın kararıdır, AI-D6.) | **Önce Admin + Genel Müdür (Önerilen)** | "Başlangıçta yalnız Admin ve Genel Müdür kullanır; denendikten sonra diğer personalara verilir. Kişi bazında da verilebilir. Şirket çapında geçerli bir izindir." Seçilmeyenler: "Bütün iş personaları" — herkes kullanır, AI yalnız kullanıcının zaten görebildiği veriye erişir; "Yöneticiler (tüm müdürler)" — Genel Müdür, Admin ve bütün müdür personaları. |
| 24.4 | AI için başlangıç sınırları ne olsun? (Ayarlardan değişir; boş kalırsa AI hiç çalışmaz.) | **Dengeli paket (Önerilen)** | "Soru en fazla 2.000 karakter; tek istekte en fazla 5 araç çağrısı; araç argümanı en fazla 16 KB; dış AI'a en fazla 'Şirket içi' sınıfı veri (hassas veri yalnız maskeli, AI-D3/D4)." Seçilmeyenler: "Sıkı paket" — soru 1.000 karakter, 3 araç çağrısı, 8 KB, dış AI'a yalnız "Genel" sınıfı veri; "Geniş paket" — soru 4.000 karakter, 10 araç çağrısı, 32 KB, dış AI'a "Şirket içi" sınıfı veri. |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| KLN-D59 | Sistemin ürettiği belgenin kasaya yazılma yetkisi (24.1) | (1) Bir iş komutu belge üretip (sipariş PDF'i, etiket, rapor çıktısı) belge kasasına yazdığında **yalnız üreten komutun kendi iş yetkisi** aranır; `documents.files.upload` ayrıca aranmaz. (2) Kasaya yazma, işlemi başlatan kullanıcı adına denetime yazılır (kim, hangi komut, hangi belge). (3) Bu kural yalnız sistemin ürettiği belge içindir; kullanıcının dışarıdan yüklediği dosya `documents.files.upload` ister. (4) Yeni izin açılmaz. Üretici portunun (`IGeneratedDocumentStorePortV1`) çağıranın yetkisini nasıl taşıdığı **uygulama ayrıntısıdır (geri alınabilir)**; port, çağıran modülün yetki kontrolünü geçmiş komut dışında çağrılamaz. | Platform belge kasası üretilen belge portu (PLT-S3/S16/S17), satış siparişi PDF'i, STK-S26 etiketi |
| KLN-D60 | Sistemin ürettiği PDF'in malware taraması (24.2; DEC-DOC-009'un kapsamını netleştirir) | (1) Sistemin **kendi ürettiği** PDF malware taramasından geçmez; kayıt "sistem üretimi" olarak işaretlenir. (2) İçerik özeti (SHA-256) saklanır; tür (beyaz liste/MIME) ve boyut kontrolü sürer; imzalı kısa erişim kuralı aynen geçerlidir. (3) Kullanıcının yüklediği her dosya DEC-DOC-009'a göre taranmaya devam eder; tarama servisi yoksa yükleme fail-closed reddedilir. (4) "Sistem üretimi" işareti yalnız sunucu içindeki üretici yoldan konabilir; istemci veya yükleme isteği bu işareti koyamaz. (5) DEC-DOC-009 sicil satırı değişmez; bu karar, daha yeni kullanıcı kararı olarak, taramanın dışarıdan gelen dosyalar için olduğunu netleştirir. | Platform belge kasası (tarama atlama yalnız üretici yol), Documents kayıt modeli |
| AI-D8 | `ai.panel.use` persona dağılımı (24.3) | (1) İzin adı `ai.panel.use`, **şirket çapında** geçerli bir izindir. (2) Başlangıç persona dağılımı: **Admin + Genel Müdür**. Denendikten sonra diğer personalara genişletmek ayrı Sinan kararıdır; kişi bazında grant mümkündür. (3) İzin, AI'ın kullanıcının zaten göremediği veriye erişmesine yol açmaz (AI-D1). (4) Gerçek grant insan kapısındadır; AI'ın canlıda açılması AI-D6'ya göre ayrıca Sinan'ın kararıdır (`Ai:Enabled=false` başlar). | AI paneli izin kaydı, persona grant listesi, AI paneli ekranı |
| AI-D9 | AI çalışma sınırları başlangıç değerleri (24.4) | Başlangıç değerleri: soru uzunluğu en fazla **2.000 karakter**; tek istekte en fazla **5 araç çağrısı**; tek araç argümanı en fazla **16 KB**; dış AI'a gidebilecek en yüksek veri sınıfı **"Şirket içi"** (hassas veri yalnız AI-D4'teki maskeleme ve veri türü izniyle; "Çok gizli" ve kişisel veri AI-D4'teki gibi gitmez). Değerler Ayarlar'dan değişir; değer yoksa veya geçersizse AI fail-closed çalışmaz. Sınırı aşan istek reddedilir. Ret mesajının metni, ayar anahtarlarının adı ve ayarın yeniden başlatmayla mı canlı mı uygulandığı **uygulama ayrıntısıdır (geri alınabilir)**. | AI runtime seçenekleri, Ayarlar ekranı |

## Varsayım (geri alınabilir)

| No | Konu | Varsayım |
|---|---|---|
| ASSUMPTION-AI-POLICYREF-1 | Grup E (AI) olaylarındaki `PolicyRef` değeri | `PolicyRef`, isteğin değerlendirildiği AI politika ayarının sürüm kimliğini taşır (örn. yayınlanmış ayar sürümü); politika henüz yayınlanmadıysa sabit `"default-deny"` yazılır. Değer yalnız izlenebilirlik içindir; para/stok/yetki etkisi yoktur ve sürümlü sözleşme testinde sabitlenir. Sinan veya bir tüketici itiraz ederse değiştirilir. |

## Uygulama notu (karar değildir)

- KLN-D51'e göre gözlemlenebilirlik değerleri Ayarlar'dan değişir. Bugünkü kodda değerler uygulama yapılandırmasından
  okunur ve yeniden başlatmayla uygulanır; tipli Ayarlar sağlayıcısı ayrı bir dilimde bağlanır. Bu not karar seçmez,
  KLN-D51'i değiştirmez.

## Sınırlar

- Bu belge yeni rol açmaz ve hiçbir kişiye izin vermez. `ai.panel.use` persona dağılımı yukarıda yazıldığı gibidir;
  gerçek grant insan kapısındadır.
- Dış AI'ın canlıda açılması, sağlayıcı seçimi, API anahtarı ve veri türü izinleri AI-D5/AI-D6/AI-D7'deki gibi
  Sinan'dadır; bu belge bunları açmaz.
- İnsan kapıları değişmez: deployment, environment, secret, Settings → Actions etkinleştirme, grant/rol/Critical,
  production aktivasyonu, `OPEN-*` kapatma ve `.claude/settings.json`.
