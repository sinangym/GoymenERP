# Göymen ERP UI tasarım sistemi

Bu belge, repo Master bölüm 5, 6 ve 26 ile doğrulanmış ekran referanslarının ortak görsel dilini tek yerde toplar. Tokenlar marka ve WCAG 2.2 AA doğrulaması tamamlanana kadar başlangıç değeridir.

## 1. Tasarım karakteri

- Sade ve anlaşılır; yüksek veri yoğunluğunu saklamadan katmanlı gösterir.
- Fazla yumuşak veya dekoratif değildir; kurumsal, net ve kontrollüdür.
- Ana yön koyu lacivert sabit menü, beyaz global araç çubuğu ve açık nötr çalışma yüzeyidir.
- Tek birincil vurgu mavisi kullanılır; semantik renkler yalnız durum anlamı taşır.
- Kritik işlemde hız kadar izlenebilirlik, önizleme ve geri alınabilirlik önemlidir.

## 2. Başlangıç tokenları

| Token | Değer | Kullanım |
|---|---|---|
| `nav-bg` | `#071F3D` | Ana sol menü |
| `nav-bg-deep` | `#04172E` | Menü alt katmanı/derinlik |
| `primary` | `#0B6FE8` | Ana işlem, aktif seçim |
| `primary-pressed` | `#075BC4` | Basılı/aktif durum |
| `page-bg` | `#F5F7FA` | Ana çalışma yüzeyi |
| `surface` | `#FFFFFF` | Kart, panel, modal |
| `text` | `#0F172A` | Birincil metin |
| `muted` | `#64748B` | İkincil açıklama |
| `border` | `#E2E8F0` | İnce ayırıcı |
| `success` | `#15803D` | Başarı/uygun/gerçekleşen |
| `warning` | `#D97706` | Yaklaşan görev/orta risk |
| `danger` | `#DC2626` | Engelleyici hata/kritik risk |
| `info` | `#0284C7` | Bilgi/bağlantı |

Köşe yarıçapı çoğunlukla `8–12 px` aralığındadır. Ağır gölge, geniş degrade ve renk kalabalığı kullanılmaz. Renk tek başına anlam taşımaz; metin, ikon ve durum etiketi birlikte verilir.

## 3. Ortak uygulama kabuğu

Masaüstünde sıra:

1. Sabit koyu lacivert sol menü.
2. Beyaz global araç çubuğu: şirket bağlamı, global arama, `+ Yeni`, bildirim, yardım ve kullanıcı.
3. Sayfa başlığı, açıklama ve ana işlem.
4. Bağlamsal filtre/KPI alanı.
5. Ana liste, detay veya karar workspace’i.
6. Gerektiğinde sağdan açılan `320–380 px` AI/detay paneli.

Şirket bağlamı backend yetkisinin yerine geçmez. Görsel şirket seçimi yalnız aktif CompanyContext’i gösterir; erişim her API çağrısında server-side doğrulanır.

## 4. Sayfa türleri

| Tür | Kullanım | Temel yapı |
|---|---|---|
| Dashboard/kontrol merkezi | Rol bazlı özet ve istisna | KPI → dikkat gerektirenler → günlük işler → risk tablosu |
| Liste + hızlı detay | Günlük tarama ve seçim | Arama/filtre → yoğun tablo → sağ drawer |
| 360 detay | Tek kaydın bütün ilişkileri | Kimlik/özet → sabit sekmeler → zaman çizgisi/bağlı kayıtlar |
| Rehberli form | Yeni/düzenle | Bölümlü form → doğrulama → etki önizleme → onay |
| Tam ekran workspace | Yoğun veya çok adımlı işlem | Bağlam → kaynak → giriş → hesap/önizleme → kontrol → onay |
| Karşılaştırma/önizleme | Mükerrerlik, revizyon, fiyat, tahsis | Eski/yeni veya kaynak/hedef → fark → risk → kullanıcı kararı |

Modal yalnız kısa ve tek odaklı işlem içindir. Çok satırlı, hesaplamalı, belge eşleştirmeli veya zincir etkili işlem tam ekran workspace olur.

## 5. Liste ve tablo standardı

- Global ve sayfa içi arama hızlı, klavye erişilebilir ve veri büyüklüğüne uygun server-side çalışmalıdır.
- Kaydedilmiş görünüm, kolon seçimi, sıralama, filtre ve sayfalama kullanıcı bazında saklanabilir.
- Satıra tıklama detay açar; checkbox seçimiyle karıştırılmaz.
- Toplamlar filtre kapsamını açıkça belirtir; dar yetkili kullanıcıya şirket geneli toplam sızdırılmaz.
- Durumlar kısa rozet; miktar, para birimi ve kaynak türü ayrı alanlardır.
- Kritik farklar metin + ikon + renk ile gösterilir.
- Büyük veri setinde sanallaştırma/keyset veya uygun cursor tabanlı sayfalama değerlendirilir; tüm satırlar tarayıcıya yüklenmez.

## 6. Form ve işlem standardı

- Alanlar iş akışına göre gruplanır; teknik veri modeli ekrana olduğu gibi dökülmez.
- Zorunlu, salt okunur, kaynaktan gelen ve AI önerisi olan alanlar ayırt edilir.
- Hesaplanan alanlar mümkün olduğunda giriş değiştikçe anlık güncellenir; kullanıcı ayrıca “Hesapla” düğmesine basmaz.
- Anlık önizleme backend deterministik hesaplayıcısının sonucunu gösterir; LLM formül çalıştırmaz.
- Zincir etkili kayıtta hangi modül/kayıtların değişeceği kaydetmeden önce gösterilir.
- Kritik değişiklik gerekçe, yetki, onay ve audit gerektirir.
- Kesinleşmiş iş kaydı sessizce silinmez veya üstüne yazılmaz; revizyon/ters kayıt modeli uygulanır.

## 7. AI standardı

AI her sayfada bağlamsal olabilir, fakat ERP’nin üstünde değildir.

- Okur, açıklar, önerir, eşleştirir ve taslak/önizleme hazırlar.
- Kullanıcının Company/Site/Location/WorkCenter ve permission kapsamının dışındaki veriyi okuyamaz.
- Fiyat, formül, kur, stok veya muhasebe gerçeği uyduramaz.
- Öneri kartı en az amaç, kaynak, güven, etki, risk, gereken yetki/onay ve `Uygula` öncesi farkı gösterir.
- Kritik zincir: `AI önerir → kullanıcı önizler → kullanıcı onaylar → ERP uygular → audit kaydeder`.
- AI paneli kapatılabilir; ana iş akışı AI olmadan da tamamlanabilir.
- AI katılımı audit olayında model/politika/tool/source channel bilgisiyle işaretlenir.

## 8. Durumlar

Her ekran şu durumları tasarlar:

- Yükleniyor/skeleton.
- Gerçek veri yok.
- Filtre sonucu boş.
- Yetkisiz veya dar kapsamlı.
- Kısmi/güncel olmayan veri.
- Doğrulama hatası.
- Eşzamanlı değişiklik çatışması.
- Kaydediliyor/tekrar deneniyor/idempotent replay.
- Başarılı sonuç ve audit/correlation referansı.
- Entegrasyon veya AI servisi geçici olarak kullanılamıyor.

## 9. Responsive ve erişilebilirlik

- `1440 px+`: tam sol menü, çok kolon, sabit/çekmece AI.
- `1024–1439 px`: daraltılabilir menü, yeniden akan kartlar.
- `768–1023 px`: iki kolon, overlay menü/AI.
- `<768 px`: tek kolon, tam ekran workspace, kontrollü yatay tablo veya kart alternatifi.
- Klavye ile gezinme, görünür focus, anlamlı label/aria, yeterli kontrast ve renk dışı durum işareti zorunludur.
- Yalnız `min-width` ile telefon desteği verilmiş sayılmaz; gerçek görev akışı mobilde ayrıca kabul edilir.

## 10. Dil, tarih, sayı ve dosya adı

- Kullanıcıya görünen metinler Türkçe karakterleri doğru kullanır ve UTF-8 saklanır.
- Tarih/saat, para ve miktar sunumu şirket/kullanıcı yereline göre; veri saklama ISO/UTC sözleşmesine göre yapılır.
- Birim etiketi değerden ayrılmaz; `kg`, `ton`, `adet`, `m`, para birimi açık görünür.
- Sistemce oluşturulan PDF/Excel dosya adları anlaşılır Türkçe metin ve değişmez tanımlayıcı taşımalıdır. Örnek: `Satış_Siparişi_SP-2026-1842_Rev-02_2026-08-27.pdf`.
- İşletim sistemi uyumluluğu için dosya adında `<>:"/\\|?*` kullanılmaz; Türkçe içerik belgenin başlık/metadata alanlarında eksiksiz korunur.

## 11. Referansların üretime taşınmaması gereken yönleri

- Prototipteki mock veri production seed değildir.
- Inline `onclick`, kontrolsüz `innerHTML`, yalnız istemci tarafı yetki ve sahte upload/AI davranışı kopyalanmaz.
- Görselde görünen toplam veya renk iş kuralı değildir; iş kuralı kabul edilmiş dokümandan gelir.
- Pixel benzerliği accessibility, performans, responsive ve güvenlik gereksinimlerini geçersiz kılamaz.
