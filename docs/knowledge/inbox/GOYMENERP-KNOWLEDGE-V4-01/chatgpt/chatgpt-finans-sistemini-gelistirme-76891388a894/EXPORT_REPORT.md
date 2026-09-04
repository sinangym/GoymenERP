# Export Report — Göymen ERP Finans ChatGPT Source

Run-ID: GOYMENERP-KNOWLEDGE-V4-01
Provider: chatgpt
Mode: SOURCE_EXPORT_GIT
Source-ID: chatgpt-finans-sistemini-gelistirme-76891388a894
Conversation-ID: chatgpt-finans-sistemini-gelistirme-8d7dc213bc86
Conversation-Title: Göymen ERP — Finans Sistemini Geliştirme
Base-Main-SHA: 188c792900e35d3e2ed6a9ba4734dc9ad596fc97
Coverage: visible_only
Package-Status: partial

## Görülen kapsam

- Kullanıcıya görünür proje mesajları: 25 (`message-001`…`message-025`).
- Kaynak snapshot'a katılan doğrudan erişilebilir proje varlığı: 85.
- Attachment index toplamı: 87; türetilmiş arşiv ve operasyon promptu proje snapshot anlamından hariç tutulmuştur.
- Normalize atomik kayıt: 97.
- Conflict kayıt: 1.
- Canonical repository görünürlüğü `PRIVATE` doğrulandı; base main SHA `188c792900e35d3e2ed6a9ba4734dc9ad596fc97`.

## Erişilemeyen / sınırlı kapsam

1. Platform-native birebir sohbet dışa aktarımı dosya olarak erişilebilir değildi; kaynak mesajları bu oturumda kullanıcıya görünür arşiv transcriptinden alındı. Gizli sistem talimatları ve özel muhakeme zinciri aktarılmadı.
2. Bazı eski cevaplarda atıf yapılan File Library veya önceki kaynak dokümanların ham dosyaları bu export için doğrudan attachment olarak erişilebilir değildi; yalnız konuşmada görünür proje anlamı normalize edildi.
3. `Goymen_ERP_Finans_Calisma_Alani_v2` tam HTML/kaynak/ZIP bulunamadı; yalnız erişilebilir ara önizlemeler korunabildi.

Bu nedenle manifest `coverage=visible_only`, `status=partial` taşır; görünmeyen geçmiş tamamlanmış sayılmaz.

## Güvenlik ve gizlilik incelemesi

- Paket metni ve normalize kayıtlar parola, API token, private key, bearer credential ve gerçek connection string desenleri için tarandı; bulgu bırakılmadı.
- E-posta, telefon, TCKN benzeri kişi kimliği ve TR IBAN biçimleri için paket metni otomatik ve elle gözden geçirildi; üretim kişisel verisi bırakılmadı.
- Ham attachment içeriği Git'e eklenmez; `ATTACHMENT_INDEX.md` yalnız güvenli ad, hash, erişim durumu ve redakte özet taşır.
- Otomatik tarama bütün PII biçimlerini matematiksel olarak kanıtlayamaz; bu sınırlama burada açıkça kaydedilir.
- Redactions: 0. Operasyon promptu Göymen ERP gereksinimi olarak normalize edilmemiştir.

## Kanonik karşılaştırma

Kanonik karar sicili finans kaynağı/dağıtım ayrımı, DBS nakit ayrımı, 13 haftalık plan, tedarikçi POS yasağı, şirket kartı ve çek/senet yaşam döngüsü gibi ana kararları zaten kilitler. Konuşmadaki daha yeni banka ekstresi/hareketi “şimdi” kapsamı, kanonik `OPEN-016` ile potansiyel conflict olarak korunmuştur. BTM açık sorusu kanonik `DEC-FIN-011` ile superseded'dir.

## Uygulanmışlık iddiası

Bu export finance runtime'ının çalıştığını iddia etmez. Konuşmadaki HTML/görseller ve ekran katalogları `PROTOTYPE_ONLY`; bu koşuda Finance için gerçek backend/API/SQL/browser acceptance kanıtı doğrulanmamıştır.

## Yerel doğrulama

Altı exact paket dosyasına yönelik sözleşme kontrolü tamamlandı: UTF-8/BOM yok/LF, deterministic item ID, relation target, source-id snapshot suffix, attachment sıra ve sayım, manifest hash/byte/count parity, secret/PII taraması ve NDJSON enum/tek-satır sınırı. Ayrı geçici Git index'i üzerinde `git diff --cached --check` başarılıdır. GitHub branch/PR oluşturulduktan sonra exact head ve uzak CI ayrıca raporlanacaktır.
