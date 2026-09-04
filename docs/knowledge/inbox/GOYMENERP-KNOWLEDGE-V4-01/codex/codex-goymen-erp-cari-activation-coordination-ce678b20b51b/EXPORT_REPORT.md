# Kaynak dışa aktarım raporu

- Run: `GOYMENERP-KNOWLEDGE-V4-01`
- Provider: `codex`
- Mode: `SOURCE_EXPORT_GIT`
- Source ID: `codex-goymen-erp-cari-activation-coordination-ce678b20b51b`
- Conversation ID: `codex-derived-584a1e09c8b19787d1369b7c`
- Coverage: `visible_only`
- Status: `partial`
- Repository base: `188c792900e35d3e2ed6a9ba4734dc9ad596fc97`
- Normalize kayıt: 35
- Ek: 0
- Çözülmemiş çatışma: 0
- Redaksiyon: 0

## Erişim sınırı

1. Önceki asistan yanıtlarının tamamı ham metin olarak görünür değildi; yalnız görünür kullanıcı mesajları ve açıkça sağlanan konuşma kesitleri işlendi.
2. Platform kararlı bir konuşma kimliği sağlamadı; conversation_id sağlayıcı, görünür başlık ve ilk proje mesajından deterministik olarak türetildi.
3. Diğer Codex, Claude ve ChatGPT konuşmaları bu oturumdan erişilebilir değildir.

## Tekilleştirme ve sınıflandırma

Tekrarlanan talimatlar tek atomik kayıtta birleştirildi ve birleşik kaynak konumları korundu. Kullanıcının konuşmaya yapıştırdığı başka ajan raporları otomatik kullanıcı kararı yapılmadı; kanıtı canlı repository ile doğrulananlar `SOURCE_VERIFIED`, yalnız raporlananlar `PROPOSED`, açık kullanıcı kısıtları `USER_REQUIREMENT` veya `USER_REJECTION` olarak sınıflandırıldı.

## Gizlilik incelemesi

Paket içerikleri otomatik yüksek güvenli credential/PII taramasından ve elle gözden geçirmeden geçirildi; gerçek bağlantı bilgisi, erişim değeri, e-posta, telefon, TCKN veya IBAN yazılmadı. Kişi adı yerine işlevsel “ürün sahibi” ifadesi kullanıldı. Otomatik tarama bütün kişisel veri türlerinin yokluğunu matematiksel olarak kanıtlayamaz; bu rapor elle incelemenin kapsamını açıklar.

## Snapshot üretimi

Kaynak snapshot, görünür proje mesajlarını sıra/rol/locator/redakte içerikle ve kullanılan repository tabanını ayrı kayıtla canonical UTF-8 JSON Lines olarak temsil eder. Operasyon promptu snapshot girdisine alınmadı. Snapshot SHA-256: `ce678b20b51b614be3e7d2565ef9b9c975f22e9e54743d501a8971489c238bee`.
