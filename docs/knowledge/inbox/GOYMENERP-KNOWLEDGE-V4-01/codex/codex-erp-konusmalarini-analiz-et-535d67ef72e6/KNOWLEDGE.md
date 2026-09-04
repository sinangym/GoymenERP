# Göymen ERP konuşma bilgisi — codex-erp-konusmalarini-analiz-et-535d67ef72e6

Bu paket, “ERP konuşmalarını analiz et” Codex konuşmasında görünür olan kullanıcı kararlarını, gereksinimleri, arşiv kararlarını ve güncel `main@188c792900e35d3e2ed6a9ba4734dc9ad596fc97` kanıtını normatif olmayan aday bilgi olarak taşır. Karar sicilini, Master v3'ü, backlog'u veya uygulama davranışını değiştirmez.

## Ürün hedefi

Tek, hızlı ve güvenilir Göymen ERP; kullanıcı kolaylığına dayalı ortak lacivert kabuk, hukuki şirket izolasyonu, modüler monolit, merkezî belge kasası, gerçek SQL kanıtı ve her sayfada kontrollü AI ile şirketin satış, satınalma, ürün/hizmet, cari, stok/tahsis, beş üretim motoru, lojistik, muhasebe, finans ve raporlama yükünü taşımayı hedefler. StokERP runtime bağımlılığı değil, doğrulanması gereken donör bilgi kaynağıdır.

## Değişmez sınırlar

- CompanyId güvenlik sınırıdır; grup görünümü yalnız yetkili şirketleri kapsar.
- AI doğrudan DB/ham SQL yazmaz, formül veya onay otoritesi değildir; taslak, önizleme ve ERP komutu sınırında çalışır.
- Aynı fiziksel, ticari veya finansal olay farklı ekranlar için kopyalanmaz; tek sahiplik ve farklı bağlamsal görünüm kullanılır.
- Stok, para, üretim ve kesin belge kayıtları silinmez; ters hareket/revizyon ve immutable audit korunur.
- Mock/InMemory kanıtı gerçek SQL veya browser kanıtı değildir; exact-SHA ve fresh CI zorunludur.

## Ana iş kapsamı

Paket; ürün/fiyat/paket, sipariş/irsaliye/fatura, stok/tahsis/etiket, boy kesme-dilme-boru/profil-boyama-soğuk hadde üretimi, cari/açık kalem/risk, finans/nakit/muhasebe, belge/PDF/e-posta/WhatsApp paylaşımı, OCR/AI, kişisel rapor taslakları, hızlı arama, güvenlik ve migrasyon kurallarını atomik kayıtlarla kapsar.

## Kaynak kapsamı

243 normalize kayıt ve 38 ilan edilmiş ek indekslendi; 37 ek erişilebilir ve hashli, 1 ek erişilemez durumdadır. Altı tam arşiv konuşma/karar/prototip/görsel kaynaklarını içerir. İlk mesajdaki altı canlı ChatGPT paylaşım sayfası araç üzerinden okunamadığı için arşivler kaynak kabul edildi ve paket `visible_only / partial` kaldı.
