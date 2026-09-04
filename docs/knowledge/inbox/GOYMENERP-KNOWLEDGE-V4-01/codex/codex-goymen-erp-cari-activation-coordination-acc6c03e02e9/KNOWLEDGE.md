# Göymen ERP konuşma bilgisi — codex-goymen-erp-cari-activation-coordination-acc6c03e02e9

Bu paket, görülebilen Codex konuşma kesitindeki PR #161 karar uzlaştırması ile C-01/C-03 normal-runtime aktivasyon hattını normatif olmayan aday bilgi olarak taşır. Kanonik kararları, Master v3'ü, backlog durumunu veya uygulama davranışını değiştirmez.

## Ana sonuçlar

- Karar doğrusu ile delivery durumu ayrıdır. Aktif açık kararlar `open`, kapanmış fakat dilim izi korunacak kimlikler `resolved_open_history` alanında kalır.
- VKN grup çapında benzersizdir; değiştirme yaşam döngüsünün placeholder, çakışma, yetki ve audit ayrıntıları `OPEN-030` olarak açıktır.
- C-01/C-03 normal runtime hâlâ kapalıdır. Disposable browser kanıtı aktivasyon kanıtı değildir.
- Aktivasyon; gerçek Back/Forward yetki tazelemesi, kararlı beşli seri, normal provider origin ve API yapılandırmasında disposable anahtarların yokluğunu gerektirir.
- Product ve Sales hazırlıkları ilk gerçek dikey dilim aktive olana kadar donmuş kalmalıdır; sonraki uygulama sırası mevcut bağlarla #78 C-02 full-stack dilimidir.
- Cari liste cache davranışında aynı taze anahtara dönüş ağ isteği gerektirmez; `invalid_cursor` kurtarması ise taze sunucu isteği gerektirir.

## Canlı repository doğrulaması

Repository görünürlüğü private olarak doğrulandı. Kaynak taban `main@9f5b9bfdef249b9c8e89c6668f66b6aa55c34602` idi. Bu SHA üzerinde dört Counterparty capability declarationı `ImplementationAvailable=false` durumundadır; backlog 36 dilim, 162 ekran referansı ve 159 benzersiz ekran kimliği içerir. PR/issue durumları yalnız 4 Eylül 2026 ölçümüdür ve sonraki hareketlerde yeniden çözülmelidir.

## Kapsam sınırı

Kullanıcının tam döküm olarak bildirdiği ayrı içerik dosyası bu oturumun dosya yüzeyinde görünmedi; erişilebilen yüklemeler yalnız operasyon talimatının kopyalarıydı ve kaynak dışı bırakıldı. Önceki asistan yanıtlarının tamamı ham biçimde görünür olmadığı için bu paket `partial`dır. Aynı çalıştırmanın diğer Codex, Claude ve ChatGPT kaynak paketlerinin yerine geçmez.
