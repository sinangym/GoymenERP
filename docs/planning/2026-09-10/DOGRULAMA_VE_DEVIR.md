# Paket doğrulaması ve devir

Tarih: 10 Eylül 2026. Bu dosya yalnız bu planlama tesliminin statik kanıt notudur; kanonik ilerleme/backlog'un yerini almaz.

## Kapsam

- Son kullanıcı karar kaydı, son rapor, 40 paketlik envanter, 51 karar/kabul senaryosu eşlemesi, ilk dar cari teknik şartnamesi ve dört rol promptu.
- Uygulama kaynak kodu, mevcut kanonik sicil/master/task/backlog, governance ve CI değiştirilmedi.
- Gerçek müşteri/stock/finans verisi, portal hesabı, production ve tarihsel veri aktarımı kapsam dışı kaldı.

## Bağımsız belge incelemesi

İki ayrı reviewer, yazmadıkları karar/rapor/envanter/ilk dilim veya prompt dosyalarını çapraz inceledi. P0/P1 bildirmediler. Üç P2 açıklık/eşleme bulgusu giderildi:

1. Uçbaş/kullanılabilir artık kusur örneğinden ayrıldı; otomatik hurda değildir.
2. İlk yetkili iş işleminde şirket hesabı hedefi PKG-06'ya ayrı takip alt dilimi ve kabul kanıtıyla eklendi; dar rol dilimi büyütülmedi.
3. Sicil kontrolünde aktif/pasif bütün cari kapsamı, kimlik/kapsam/eşleşme eksikleri ve Türkiye sicilli yabancı şube ayrımı taşındı.

F01 etiketi de öneri değil, kabul edilmiş karar olarak düzeltildi. Bu incelemeler belge içeriği içindir; gelecekteki K3 implementation exact-head review/SQL/browser kapılarının yerine geçmez.

Karar–test eşlemesi ayrıca envanter yazarı dışında parent tarafından ve mapping yazarı dışında bir reviewer tarafından kontrol edildi. Üç kabul ifadesi düzeltildi: dış portalda yerel inbox'ın tek başına exactly-once garantisi olmadığı; restore'un gerekli bütün ortak master referanslarını kapsadığı; yalnız dış bağlantı kesilip ERP çalışıyorsa bağımsız normal işlerin sürdüğü. Bunlar yeni kullanıcı tercihi değil, mevcut kararın doğru uygulanma sınırlarıdır.

## Çalıştırılan kontroller

Çalışma dizini: `C:\src\GoymenERP-delivery-20260910`. İlk taban: `9f5b9bfdef249b9c8e89c6668f66b6aa55c34602`. Aşağıdaki kontroller bu doküman çalışma ağacında çalıştırıldı; ürün runtime kanıtı değildir.

| Komut | Sonuç |
|---|---|
| `powershell -NoProfile -ExecutionPolicy Bypass -File docs/planning/2026-09-10/Validate-DeliveryPackage.ps1` | PASS: 40 paket / 38 aktif hedef, 36 mevcut dilim, 51 REQ/SCN, 16 son soru kararı, 24 yerel bağlantı; döngüsüz grafik. Yedi bozuk girdi self-test'i beklenildiği gibi reddedildi. |
| `python -I -S -B tools/validate_delivery_backlog.py` | PASS: mevcut 6 dalga, 36 dilim ve 58 bağımlılık doğrulandı; mevcut kapsam eşlemesi değiştirilmedi. |
| `python -I -S -B tools/validate_task_manifest.py` | PASS: mevcut 15 görev, 49 bağımlılık; benzersiz kimlikler ve döngüsüz grafik. |

Son içerik düzeltmelerinden sonra paket doğrulaması yeniden PASS verdi; staged `git diff --cached --check` temizdi. Değişen dokuz dosya yalnız bu karar/planlama paketi kapsamındadır; asıl `C:\src\GoymenERP-codex` çalışma ağacı temiz kaldı. Commit/PR exact HEAD'i GitHub kaydında yer alır. Zorunlu uzak CI bu yerel doküman kontrolleriyle geçmiş sayılmaz; PR kendi gerçek sonucunu taşır.

Ürün unit, SQL, browser, portal veya restore testleri bu doküman turunda **NOT_RUN**; çalıştırılmamış kabul senaryoları JSON'da açıkça aynı durumdadır. İlk cari diliminin 10 CR senaryosu da önerilen kabul paketidir; henüz uygulama testi değildir. Kullanılan Python standart kitaplık doğrulayıcıları ve PowerShell aracı veri/DB yazmaz.

## Devir

Koordinatör bu paket PR'ını ve mevcut cari #227/#228 adaylarını güncel head/owner ile uzlaştırır. İnsan karar incelemesi ve ilgili sahip uzlaştırmasından sonra ilk dar dilim için DoR doğrulanır. ChatGPT iş/kabul, Codex backend, Claude frontend şeritlerinde çalışır; ayrı koordinatör mevcut kurallarla merge eder.

Bu turda yeni sürekli otomasyon veya başka AI oturumu başlatılmamıştır. Hazır promptlar ayrı görevlere verilince, yalnız mevcut yetki ve hazır bağımlılık içinde ilerler. Ürün tamamlandı veya canlıya hazır denmez.
