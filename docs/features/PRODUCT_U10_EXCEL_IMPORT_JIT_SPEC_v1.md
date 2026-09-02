# Product U-10 Excel Import JIT Specification v1

## 1. Amaç ve kapsam borcu

U-10 güvenli içe aktarma zincirini hazırlar: **mapping → preview → validation/error → duplicate → approval → write**. Kilitli `PRODUCT-PRICE-03` görseli fiyat importuna özeldir; bu paket onu genel Product/Hizmet importu gibi yorumlamaz. Genel kapsam `PRD-IMPORT-001` owner kararıyla pinlenene kadar production import NO_GO'dur.

`PRD-IMPORT-001` kapanışı; U-10'un genel Product, yalnız Price veya iki açık mod olup olmadığını; kabul edilen dosya biçimi ve locale/number/date parse kurallarını; dosya tarama/güvenliği; mapping identity/sürümünü; partial-vs-atomic batch davranışını; fingerprint/replay/rollback politikasını ve ERP-000G kaynak dosya saklama sahibini ayrı ayrı pinlemelidir.

## 2. Fazlar ve yan etki sınırı

| Faz | Çıktı | Kalıcı business write |
|---|---|---|
| Güvenli dosya kabulü | Dosya metadata/tarama sonucu ve batch taslağı | Yok |
| Kolon eşleme | Kaynak→canonical alan mapping taslağı | Yok |
| Parse/normalize | Exact, ambiguous, unmatched satırlar | Yok |
| Validation | Satır/batch hata ve uyarıları | Yok |
| Duplicate/replay | İçerik+niyet eşleşmesi ve önceki sonuç referansı | Yok |
| Preview | Eski-yeni diff ve etki özeti | Yok |
| Approval | Yetkili kullanıcı niyeti ve scope snapshotı | Yok |
| Write | Owner politikasıyla tek, auditli/idempotent uygulama | Yalnız bütün kapılar kapalıysa |

Dosya upload edilmesi, mapping kaydı veya approval ekranının açılması business kayıt yazımı değildir.

## 3. Mapping ve satır kararları

- Kullanıcı kaynak kolonları canonical semantik alanlara eşler; DTO/DB kolon adı bu belgede tanımlanmaz.
- Exact eşleşme, birden çok aday ve eşleşmeyen satır ayrı sınıftır.
- AI eşleşme önerir; kullanıcı kararını veya canonical referansı oluşturmaz.
- Kullanıcı satır için mevcut kaydı seç, satırı atla veya yeni taslak akışına git niyetlerinden birini açıkça seçer.
- Yeni Product gereksinimi U-02 taslağına gider; core lifecycle/kod/izin kapılarını atlamaz.
- Hizmet satırı `SRV-SCOPE-001` kapanmadan aktive edilemez.
- Fiyat satırı mapping sonrası doğrudan revision üretmez; U-06 simülasyon/diff/approval zincirine girer.

## 4. Duplicate, replay ve batch atomicity

Aynı dosya/aynı normalize niyet tekrarı mükerrer kalıcı kayıt üretmez. Dosya adı tek başına kimlik değildir. İçerik digest'i, import türü, server-resolved kapsam, mapping sürümü ve komut niyeti global idempotency owner tarafından tek contract olarak tanımlanmalıdır; bu belge key formatı veya endpoint icat etmez.

Satır bazlı kısmi başarı, tüm-batch atomic veya düzelt-ve-devam seçeneklerinden hangisinin geçerli olduğu açık owner kararıdır. Karar gelene kadar çözülmemiş/hatalı satır bulk Apply'ı bloklar.

## 5. Dosya ve güvenlik

Dosya boyutu/türü, malware taraması, saklama, retention, PII ve Document Vault bağları platform sahibi tarafından pinlenir. Formül/makro yürütülmez. Hücre içeriği komut, HTML veya formül olarak çalıştırılmaz. Download edilen hata raporu spreadsheet injection'a karşı güvenli olmalıdır; exact escaping kuralı güvenlik owner kontratıdır.

Issue #100 `PRD-PERM-001=A` pilotunda U-10 Apply yalnız mevcut `SYSTEM_ADMINISTRATOR` rolü için değerlendirilir; diğer roller default-deny'dır. Exact permission string hâlâ unresolved'dır ve rol görünürlüğü server-side komut yetkisi yerine geçmez.

## 6. UI davranışı

Adımlar: Dosya → Mapping → Önizleme → Hatalar/Duplicate → Onay özeti → Sonuç/Rapor. Her adım loading, gerçek empty, filtered-empty, narrow/unauthorized, parse error, stale, replay, integration unavailable ve success+correlation durumlarını taşır. Kullanıcı geriye döndüğünde mapping taslağı korunur; source file değişirse önceki preview stale olur.

## 7. Audit ve kanıt

Batch kimliği, file content digest referansı, mapping sürümü, karar veren kullanıcı, kapsam snapshotı, satır sonuç özeti, approval ve write korelasyonu değiştirilemez biçimde bağlanır. Raw dosyanın audit içinde kopyalanması varsayılmaz; Document Vault owner kararı beklenir.

## 8. Kabul ve devir

`AT-019` yalnız mapping/preview/approval olmadan write olmaması; `AT-020` yalnız aynı import tekrarında duplicate kalıcı kayıt oluşmaması vakalarına bağlanır. Unsupported/unsafe dosya ve spreadsheet-formula injection ayrıca local acceptance ile zero-write korunur. Exact permission adı, batch key, API, tablo, queue, atomicity, retention, rollback veya migration bu belgede yoktur. `PRD-IMPORT-001`, Document Vault, permission, audit, idempotency, UI, real-browser ve shared CI kapıları kanıtla kapanmadan implementation GO yoktur.
