# ADR-008 — AI Entegrasyon Mimarisi: Responses API, Structured Outputs ve Şemalı Tools

- **Durum:** Kabul
- **Tarih:** 2026-08-27
- **Sahip:** Sinan Göymen
- **Kapsam:** Göymen ERP AI Gateway, sayfa AI'ları, OCR sonrası yapılandırma, AI Rapor Oluşturucu ve gelecekteki çok adımlı ajan akışları

## Bağlam

Göymen ERP'de AI yardımcıdır; tek gerçeklik sahibi, hesap motoru veya yetki/onay mercii değildir. AI veritabanına doğrudan bağlanamaz ve ham SQL üzerinden ERP verisi sorgulayamaz. AI entegrasyonu sürümlü tool şemaları, CompanyContext, RBAC/ABAC, önizleme, onay ve audit zincirini korumalıdır.

## Karar

1. OpenAI entegrasyonunda düşük seviye varsayılan API **Responses API** olacaktır.
2. Modelden ERP komut/taslak argümanı veya semantik rapor tanımı alınırken **Structured Outputs / JSON Schema** kullanılacaktır.
3. Function tools `strict` şema yaklaşımıyla tanımlanacak; model yalnız izin verilen sürümlü tool sözleşmelerini çağırabilecektir.
4. Çok adımlı agent loop, guardrail, handoff, session, tracing veya tool orkestrasyonu gerektiğinde **OpenAI Agents SDK** kullanılabilir. Kısa ve deterministik akışlarda zorunlu değildir.
5. **ChatKit** yalnız kullanıcı arayüzü geliştirmeyi hızlandırması halinde opsiyoneldir. ERP'nin yetki, tool gateway, audit, state machine ve veri sahipliği ChatKit'e devredilmez. Agent Builder bağımlı entegrasyon kullanılmaz.
6. **Agent Builder kullanılmaz.** Kalıcı ERP agent/workflow mantığı repository içindeki kod, şema, prompt/policy sürümü ve testlerle yönetilir.
7. Model adları domain kararına sabitlenmez. Desteklenen model/sürüm çevre ve policy konfigürasyonudur; değişiklik eval/golden test kapısından geçer.
8. AI Rapor Oluşturucu SQL değil `ReportDefinitionDraft` üretir. Semantic query compiler doğrulanmış tanımı parametrik sorguya dönüştürür.

## Güvenlik değişmezleri

- Tool çağrısı öncesi ve uygulama öncesi kullanıcı + CompanyContext + kayıt kapsamı tekrar doğrulanır.
- AI doğrudan DB connection string veya repository secret göremez.
- Write tool sonucu idempotent ERP command üzerinden uygulanır.
- Yüksek etkili yazmalar insan onayı olmadan uygulanmaz.
- Structured output şemaya uysa bile değer doğruluğu deterministik ERP servisleri tarafından kontrol edilir.
- AI audit envelope; model, prompt, tool schema sürümü, veri kapsamı, çağrı, sonuç ve kullanıcı kararını taşır.

## Gerekçe ve güncel OpenAI durumu

- OpenAI API dokümantasyonu Responses API'yi güncel ana API yolu olarak sunar ve JSON Schema tabanlı Structured Outputs'u destekler.
- OpenAI Agents SDK, Responses API'yi varsayılan model yolu olarak kullanır ve tool, guardrail, handoff, session ve tracing için daha yüksek seviye runtime sağlar.
- OpenAI'nin 3 Haziran 2026 güncellemesine göre Agent Builder kapatılma sürecindedir ve 30 Kasım 2026 sonrasında platformda kullanılamayacaktır; kalıcı workflow'lar için Agents SDK önerilmektedir. Bu nedenle Göymen ERP Agent Builder'a bağımlı olmayacaktır.

## Resmî kaynaklar

- https://platform.openai.com/docs/quickstart/make-your-first-api-request
- https://platform.openai.com/docs/api-reference/responses
- https://openai.github.io/openai-agents-python/
- https://openai.com/index/introducing-agentkit/
- https://openai.com/index/introducing-structured-outputs-in-the-api/

## Sonuçlar

**Olumlu:** ERP tool sözleşmeleri test edilebilir ve sağlayıcı/model değişimine daha dayanıklı olur; ham SQL riski azalır; yetki/audit merkezi kalır.

**Maliyet:** Semantic field catalog, JSON Schema sürümleme, tool contract testleri ve model eval seti geliştirilmelidir.
