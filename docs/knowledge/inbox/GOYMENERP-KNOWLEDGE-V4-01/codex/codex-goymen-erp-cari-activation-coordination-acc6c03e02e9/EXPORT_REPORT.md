# Kaynak dışa aktarım raporu

- Run: `GOYMENERP-KNOWLEDGE-V4-01`
- Provider: `codex`
- Mode: `SOURCE_EXPORT_GIT`
- Source ID: `codex-goymen-erp-cari-activation-coordination-acc6c03e02e9`
- Conversation ID: `codex-derived-584a1e09c8b19787d1369b7c`
- Coverage: `partial`
- Status: `partial`
- Repository base: `9f5b9bfdef249b9c8e89c6668f66b6aa55c34602`
- Normalize kayıt: 35
- Ek: 0
- Çözülmemiş çatışma: 0
- Redaksiyon: 0

## Erişim sınırı

1. Kullanıcının tam döküm olarak bildirdiği ayrı konuşma dosyası bu oturumun dosya yüzeyinde görünmedi; erişilebilen metin yüklemeleri yalnız operasyon talimatının kopyalarıydı.
2. Önceki asistan yanıtlarının tamamı ham metin olarak görünür değildi; yalnız görünür kullanıcı mesajları ve görünür bir asistan durum mesajı işlendi.
3. Platform kararlı bir konuşma kimliği sağlamadı; conversation_id sağlayıcı, görünür başlık ve ilk proje mesajından deterministik olarak türetildi.

## Önceki snapshot ilişkisi

Önceki değişmez partial paket `codex-goymen-erp-cari-activation-coordination-ce678b20b51b` (https://github.com/sinangym/GoymenERP/pull/211) değiştirilmedi. Eski paketin gerçek item kimlikleri bilinse de paket güncel main doğrulama grafiğinde bulunmadığı için bu kaynak PR'ındaki `supersedes` veya `related_items` dizilerine yazılmadı; bilinmeyen hedef uydurulmadı ve cross-PR dangling ilişki üretilmedi. Semantik eşleme, iki paket aynı entegrasyon grafiğine alındığında yapılmalıdır.

## Tekilleştirme ve sınıflandırma

Tekrarlanan talimatlar tek atomik kayıtta birleştirildi ve birleşik kaynak konumları korundu. Kullanıcının konuşmaya yapıştırdığı başka ajan raporları otomatik kullanıcı kararı yapılmadı; kanıtı canlı repository ile doğrulananlar `SOURCE_VERIFIED`, yalnız raporlananlar `PROPOSED`, açık kullanıcı kısıtları `USER_REQUIREMENT` veya `USER_REJECTION` olarak sınıflandırıldı.

## Gizlilik incelemesi

Paket içerikleri otomatik yüksek güvenli credential/PII taramasından ve elle gözden geçirmeden geçirildi; gerçek bağlantı bilgisi, erişim değeri, e-posta, telefon, TCKN veya IBAN yazılmadı. Kişi adı yerine işlevsel “ürün sahibi” ifadesi kullanıldı. Otomatik tarama bütün kişisel veri türlerinin yokluğunu matematiksel olarak kanıtlayamaz; bu rapor elle incelemenin kapsamını açıklar.

## Snapshot üretimi

Kaynak snapshot, görünür proje mesajlarını sıra/rol/locator/redakte içerikle ve kullanılan repository tabanını ayrı kayıtla canonical UTF-8 JSON Lines olarak temsil eder. Operasyon promptu snapshot girdisine alınmadı. Snapshot SHA-256: `acc6c03e02e9972a541b16f2e10d4a09dcad97fc419612e5b2313596d7115355`.
