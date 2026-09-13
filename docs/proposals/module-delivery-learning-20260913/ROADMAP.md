# Teslim durumu ve düşük tüketimli devam önerisi

Durum: 2026-09-13 danışmanlık önerisi; yeni uygulama/görev başlatma veya kanonik backlog değiştirme yetkisi değil. GitHub main kontrolü: `f6fa07d026403649d6c815b84fd850e7ceeaf092`. Bu inceleme kod/teslim kayıtlarını esas alır; tüm eski oturumlar ve ham artefaktlar yeniden denetlenmez.

## Üç çiftin durumu

| Çift | Backend teslimi | Frontend teslimi | Devam durumu |
|---|---|---|---|
| 1 — Cari | [#252](https://github.com/sinangym/GoymenERP/pull/252) merged; önceki temel backend teslimleri korunur | [#250](https://github.com/sinangym/GoymenERP/pull/250) merged; önceki temel ekranlar korunur | L04 gerçek browser/typed SQL/cleanup teslim edilmiş. Son main CI kontrolü ayrıca; tamamlanmış senaryo yeniden koşulmaz. |
| 2 — Ürün | [#251](https://github.com/sinangym/GoymenERP/pull/251) merged; #245 kaynak devriyle closed, merged değil | [#238](https://github.com/sinangym/GoymenERP/pull/238) merged | U01/U02 dar liste/kart/taslak oluşturma/temel düzenleme teslim edilmiş. |
| 3 — Organizasyon | [#246](https://github.com/sinangym/GoymenERP/pull/246) ve ilgili fixture/lifecycle değişiklikleri merged | [#242](https://github.com/sinangym/GoymenERP/pull/242), [#249](https://github.com/sinangym/GoymenERP/pull/249) merged | Liste/lokasyon detay/durum değiştirme ve dar gerçek kabul/cleanup teslim edilmiş. |

Altı görevin atanmış dar geliştirme işleri ile tüm modüller aynı şey değildir. Ürün nitelik/birim/fiyat/import/lifecycle, Cari'nin geniş banka/vergi/mükerrer/risk yüzeyi veya Organizasyon'un bütün yönetimi bu tabloda tamamlandı sayılmaz. Uygulamada `idle/notLoaded` görülmesi tek başına done kanıtı değildir; yukarıdaki PR ve teslimler dayanak alınmıştır. İncelemenin ilk main CI okumasında CI ve C01 sürüyordu; nihai sonuç görev sahibi tarafından güncellenir.

## Sırada ne olmalı?

Öneri: **Siparişin ilk kullanılabilir akışına hazırlık**. Ama önce kısa gerçek bağımlılık kontrolü: mevcut Ürün durumu siparişte kullanılabilir mi, gereken birim/dönüşüm/fiyat/para sözleşmesi var mı; Cari görünür ve şirkette kullanılabilir mi; gerekli ayarlar/yetkiler uygulanmış mı? Draft Ürün yazılabildiği için ticari siparişe uygun ürün hazır varsayılmaz.

Mevcut [DELIVERY_BACKLOG](../../backlog/DELIVERY_BACKLOG_v1.json) geniş modül bağımlılıkları taşır:

- W2-SATIS -> W1-CARI + W1-URUN.
- W3-STOK -> W1-URUN + W1-ORG.
- W3-URETIM -> W3-TAHSIS + W3-ETIKET + W1-SETTINGS.
- W4-FINANS -> W2-ACIKKALEM; açık kalem de irsaliye/ayar bağımlılıkları taşır.

Dar teslimler bütün W1-CARI/W1-URUN'u done yapmaz. Business mevcut backlog/spec sahibi olarak ilk Sipariş akışının hangi gerçek öncüllere ihtiyaç duyduğunu gösterir. Geniş görev bağımlılığı bunu yürütmeye izin vermiyorsa mevcut yetkiyle uygun ayrıştırma önerisini hazırlar; validator/backlog statüsü sessizce atlanmaz.

| Adım | Tek çıktı | Etkin ekip |
|---|---|---|
| 0 | Mevcut hedefin kalan post-merge sonucu ve kısa kapanışı | Yalnız mevcut koordinatör; yeni failure yoksa geliştiriciler çalışmaz |
| 1 | Sipariş için kısa hazır/eksik bağımlılık listesi ve bir küçük akışın kabulü | Mevcut Business; gerektiğinde tek kaynak incelemesi, kod işi başlatmadan |
| 2a — gerçekten gerekli öncül eksik | Örneğin Ürün kullanılabilirlik/birim sözleşmesinin gereken dar dilimi; mevcut teslimi başarısız ilan etmeden ayrı iş | Yetkilendirilmiş tek Backend/Frontend çifti |
| 2b — öncüller hazır | Sipariş taslağı/listesi/detayı gibi Business'ın kesinleştirdiği ilk akış | Yetkilendirilmiş tek Backend/Frontend çifti |
| 3 | Sonraki gerekli sipariş akışı; ilgili noktada stok/tahsis veya finans entegrasyonu | Aynı çift; bir akış bitmeden yeni modül kampanyası yok |

Finansın daha bağımsız olduğu varsayımı mevcut bağımlılıklarla doğrulanmıyor. Stok ve Üretim için MODULE_BRIEFS hazır tutulur; ekip boş diye başlatılmaz. Yeni modülün uygulaması ayrı kullanıcı/koordinasyon yetkisi gerektirir; bu danışmanlık turunda başlatılmamıştır.

## Oturum ve tüketim düzeni

- **1 aktif geliştirme çifti + 1 ortak koordinatör + 1 tam-zamanında Business.** Business bir akış önde; koordinatör değişiklik/teslim/engel geldiğinde çalışır. Her biri sürekli durum raporu üretmez.
- Bitmiş üç çift, kendi modülünün geri dönülebilir teslim noktası olarak korunur. Silme/branch temizleme veya yeniden tüm projeyi öğrenme görevi yok. Kullanıcı yeni görev açmayı seçerse yalnız bir temiz çift açılır; aksi halde açık yeniden atama ve ayrı worktree ile tek bitmiş çift kullanılır. Aynı işi iki çifte verme.
- İkinci aktif çift ancak hazır ve bağımsız bir çıktı varsa, shared contract/migration/UI/ortam çakışması yoksa ve review darboğazı yaratmıyorsa eklenir. Şimdi üç çifti aynı anda açmak önerilmez.
- Başlangıçta rol promptu + tek modül eki + kaynak bağlantıları; devamda yalnız değişiklik, hata ve sonraki eylem. Bütün sohbeti her tur yeniden okutma. Gerekli güvenlik/sözleşme bağlamını kesme.
- Aynı CI için tek watcher; başarı/engel değişmedikçe tekrar mesaj yok. Model akıl yürütmesini normal CI beklemesi için tüketme. Kullanım modeli/süresi değişse de yeni yetki veya sınırsız kredi varsayma.
- Geniş testler son tamamlanmış aday ve zorunlu CI'da; geliştirme sırasında hedef testlerle erken hata yakala. Finans/yetki/SQL güvenliğini token tasarrufu uğruna erteleme.
- Mevcut GitHub CI belge-only PR'da bile geniş işleri tetikliyor; bu paketin yayımlanması da bunu başlatır. Bu gözlem CI'ı değiştirme yetkisi değildir. Aynı belge düzenlemelerini tek yayımda toplayarak gereksiz koşu azaltılır; gerekirse ileride ayrı, kanıtlı kalite-kapısı incelemesi yapılır.

Tüketim için kesin yüzde/para veya bitiş süresi verilmez: görev bazlı token/ücret ölçümü bu turda yapılmadı. Kazanç, tekrar bağlam okuma, boş izleme, paralel çakışma ve yeniden yapılan kodu azaltmaktan beklenir. İki yeni akış sonunda mevcut kayıtlardan ilk gerçek başarıya kadar süre, tekrar sayısı ve bekleme nedeni karşılaştırılır; yeni telemetri projesi açılmaz.
