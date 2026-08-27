# Göymen ERP Master Rapor v3.0 — Faz 0 v1.1 Delta

**Tarih:** 27 Ağustos 2026  
**Durum:** Bağlayıcı ek karar; Master Rapor v3.0'ın tek gerçeklik, yetki, AI, audit ve semantik raporlama ilkelerini değiştirmez, Raporlar alanını ayrıntılandırır.

## R-06 — Rapor Taslaklarım

`R-06 Rapor Taslaklarım`, kullanıcının tekrar kullanmak istediği rapor tanımlarını saklar. Taslak raporun veri sonucunu değil **semantik rapor tanımını** saklar.

- Varsayılan olarak oluşturan kullanıcıya özeldir.
- İsimli, açıklamalı, sürümlü, kopyalanabilir, güncellenebilir, arşivlenebilir ve yeniden çalıştırılabilir.
- Arşivleme fiziksel silme değildir; audit korunur.
- Veri kümesi, alan, ölçü, izinli hesaplanmış ölçü, filtre, gruplama, sıralama, pivot/grafik, kolon görünümü ve çıktı tercihini saklayabilir.
- Canlı sonuç satırları, cari bakiye/stok snapshot'ı veya rapor veri sonucu taslak tanımına gömülmez.
- Her çalıştırmada güncel veri, güncel CompanyContext ve güncel RBAC/ABAC yetkisi uygulanır.
- Eski taslak eski yetkiyi korumaz.
- Paylaşım, zamanlama ve dışa aktarım ayrı izinlerdir.

Önerilen nesneler: `SemanticFieldCatalog`, `ReportDefinition`, `ReportVersion`, `ReportShare`, `ReportSchedule`, `ReportRun`, `ReportArtifact`.

## R-07 — AI ile Rapor Oluşturucu

AI ham SQL üretmez veya çalıştırmaz. Doğal dil isteğini doğrulanmış bir semantik rapor tanımına çevirir.

`Doğal dil → Yetkili SemanticFieldCatalog → Structured ReportDefinitionDraft → Şema/politika doğrulama → Deterministik semantic query compiler → Yetkili sorgu → Önizleme → Kaydet/Paylaş/Zamanla`

AI tanımı veri kümesi, alan/ölçü, filtre, gruplama, sıralama, grafik/pivot, kolon görünümü ve XLSX/PDF çıktı tercihi içerebilir.

Yasaklar:

- Ham SQL veya DB bağlantısı üretip çalıştırmak.
- Semantic catalog dışından alan/ölçü kullanmak.
- Yetkiyi report definition içine kalıcı hak olarak gömmek.
- AI rapor sonucunu iş kuralı/onay yerine kullanmak.

## Paylaşım, zamanlama ve çıktı

- Taslak paylaşımı kullanıcı/rol/izinli ekip kapsamıyla yapılabilir.
- Paylaşım rapor tanımına erişimdir; veri satırı yetkisi değildir.
- Zamanlanmış rapor her çalıştırmada sahip/çalıştırma kimliği, CompanyContext ve güncel yetkiyi yeniden kontrol eder.
- Yetki yoksa artifact üretilmez; görünür hata/görev oluşur.
- XLSX/PDF çıktısı `ReportVersion`, veri kesim zamanı, şirket kapsamı ve filtre özetini metadata olarak taşır.
- Büyük raporlar kuyrukta `ReportRun` olarak hazırlanır; artifact Merkezi Belge Kasasına bağlanır.

## AI mimari kararı

- Ana düşük seviye OpenAI entegrasyonu: **Responses API**.
- Şemalı çıktı/tool argümanları: **Structured Outputs / JSON Schema** ve strict function tools.
- Çok adımlı orkestrasyon/guardrail/handoff/session/tracing gerektiğinde: **Agents SDK**.
- **ChatKit** yalnız opsiyonel UI hızlandırıcısıdır; yetki, tool gateway, state machine ve audit ERP backendinde kalır.
- **Agent Builder kullanılmaz.** Kalıcı workflow kod, şema, prompt/policy sürümü ve testlerle repository içinde tutulur.

## Ekran kataloğu eklentisi

| Kod | Ekran/alan | Ana kapsam |
|---|---|---|
| R-06 | Rapor Taslaklarım | Kullanıcıya özel semantik rapor tanımı, sürüm/kopya/arşiv/yeniden çalıştırma |
| R-07 | AI ile Rapor Oluşturucu | Yetkili semantic catalog üzerinden şemalı rapor tanımı; ham SQL yok |

## Kabul kapısı

`AT-071`–`AT-078` zorunludur. Bu delta `ERP-000A + ERP-000B` başlangıcını bloke etmez. Rapor görevleri CompanyContext, Identity/RBAC+ABAC, Audit, Belge Kasası ve AI Gateway sözleşmeleri üstüne kurulacaktır.
