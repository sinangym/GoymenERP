# Product U-03/U-04 Service JIT Specification v1

## 1. Sonuç ve güvenlik sınırı

U-03 Hizmetler ile U-04 Yeni/Düzenle Hizmet, ürün kartından ayrı **stoksuz ticari kayıt** deneyimidir. Bu belge ekran ve iş davranışını hazırlar; `SRV-SCOPE-001` kapanmadan schema, API, route veya aktivasyon davranışı tanımlamaz. Product'ın grup-paylaşımlı kimlik kararı Hizmet'e kendiliğinden taşınmaz.

## 2. Değişmez ilkeler

1. Hizmet fiziksel stok oluşturmaz; lot, etiket, paket, depo, rezervasyon, tahsis yüzdesi ve fiziksel miktar üretmez.
2. Hizmet satışta, satın almada veya her ikisinde kullanılabilir; exact scope ve lifecycle owner kararıdır.
3. Alt yüklenici hizmeti operasyon profili ve girdi/çıktı referansı taşıyabilir; fiziksel ürün hareketinin sahibi Stok/Üretim akışıdır.
4. Hizmet miktarı ticari hizmet miktarıdır; bağlı fiziksel girdi/çıktı miktarıyla sessizce eşitlenmez.
5. AI açıklama, grup, birim veya operasyon profili önerir; kayıt, onay ya da aktivasyon yapmaz.
6. Görsel kaynaklar yerleşim niyetidir; izin, endpoint, persistence veya status makinesi kanıtı değildir.

## 3. `SRV-SCOPE-001` karar paketi

Aşağıdaki alanlar owner tarafından tek sürümlü kararla pinlenmeden implementation NO_GO'dur:

| Karar | Gerekli kesinlik | Fail-closed davranış |
|---|---|---|
| Kimlik kapsamı | Global mi, şirket mi; referans çözümleme sınırı | Aktivasyon yok |
| Görünür kod | Seri/prefix, uniqueness, alias ve yeniden kullanım | Kod üretimi yok |
| Lifecycle | Görsel aday intent'lerin varlığı, adları, geçişleri ve reactivation semantiği | Hiçbir lifecycle komutu üretilmez |
| Ticari yön | Satın alınabilir/satılabilir kuralları | Kullanım yok |
| İzin | Exact permission manifest kimlikleri | Komutlar kapalı |
| Vergi/muhasebe | Tax/account owner ve varsayılanların sahibi | Muhasebe sonucu türetilmez |
| Operasyon profili | Hangi hizmette zorunlu; sahiplik ve ücret bazı | Aktivasyon bloklu |
| Audit/idempotency | Global platform kontratlarına bağ | Yazma yok |

Pilot role truth `PRD-PERM-001=A` gereği Product/Service read ve ilk faz write yalnız mevcut `SYSTEM_ADMINISTRATOR` için değerlendirilebilir; diğer roller default-deny'dır. Bu pin exact permission string değildir ve `SRV-SCOPE-001` ile `G-PRD-REMAIN-PERM-01` kapılarını kapatmaz.

## 4. U-03 Hizmetler

### 4.1 Liste görünümü

Kilitli kolon niyeti: görünür kod, ad, hizmet grubu, kullanım yönü, ticari birim, aktif fiyat durumu ve lifecycle durumu. Arama/filtre/sıralama server-side kapsam altında çalışır. Gerçek boş durum ile filtre sonucu boşluğu ayrılır. Kullanıcının göremediği hizmetler sonuç, öneri, sayaç veya toplamla sızmaz.

Eylemler: yeni hizmet, detay aç, düzenle, kopya taslağı, fiyat bağlamına git. “Kopyala” yeni kimlik niyetidir; kaynak history veya görünür kodu miras alma davranışı owner kararı olmadan uygulanmaz.

### 4.2 Detay görünümü

| Bölüm | İçerik | Yazma sahibi |
|---|---|---|
| Genel | Kod, ad, grup, açıklama, ticari yön, birim, durum | Hizmet owner kontratı |
| Operasyon | Operasyon türü, sahiplik, ücret bazı, kısa girdi→çıktı ilişkisi | Üretim/operasyon entegrasyonu |
| Fiyat ve iskonto | U-05 fiyat context linki ve özet | Fiyat modülü |
| Geçmiş | Önce/sonra, neden, aktör, korelasyon | Global audit |
| Bağlantılar | Açık sipariş/tedarik/operasyon referansları | İlgili modül |

Detay hiçbir koşulda stok miktarı veya fiziksel uygunluk gerçeği üretmez.

## 5. U-04 Yeni/Düzenle Hizmet

### 5.1 Alan niyetleri

- Ticari kimlik: ad, açıklama, grup, kullanım yönü, kontrollü ticari `UnitId`.
- Koşullu operasyon profili: operasyon türü, sahiplik, ücret bazı, girdi/çıktı referans özeti.
- Lifecycle görsel niyeti: taslak kaydetme, onaya gönderme ve aktivasyon ayrı eylem adaylarıdır; bunların varlığı, exact komutları ve geçişleri owner kararı olmadan üretilmez.
- Etki önizlemesi: sahiplik, ücret bazı veya kullanım yönü değişiyorsa bağlı kayıtlar listelenir; sessiz toplu değişiklik yapılmaz.

### 5.2 Doğrulama

- Stok/lot/etiket/paket/depo alanları forbidden'dır.
- Serbest metinle yeni birim veya vergi kodu oluşturulmaz.
- Operasyon profili gereken hizmette sahiplik ve ücret bazı çözülmeden aktivasyon kapanır.
- Değişiklik stale sürüme dayanıyorsa 409/fark davranışı gerekir; exact taşıma mekanizması platform owner'a aittir.
- Alan hatasında form korunur, erişilebilir özet ve ilk hataya odak sağlanır.

## 6. Durum ve erişilebilirlik matrisi

Her iki ekran: loading, gerçek empty, filtered-empty, unauthorized/narrow, validation, stale/409, save/replay, success+audit/correlation ve AI/integration unavailable durumlarını ayrı taşır. Masaüstü veri yoğunluğu küçük ekranda kart/öncelikli alan düzenine iner; klavye, focus, label/description ve WCAG AA kontratı korunur.

## 7. Kabul ve devir

Bağlayıcı vakalar scenarios JSON'daki U03/U04 serileridir. Service vakaları module-specific canonical AT'ye yapay biçimde bağlanmaz. U-03/U-04 “development-ready” ancak `SRV-SCOPE-001`, exact izin, data owner, audit, idempotency, concurrency, UI ve CI kapıları kanıtla kapandığında denebilir. Bu sürüm yalnız `PREPARATION_READY_IMPLEMENTATION_NO_GO` sonucudur.
