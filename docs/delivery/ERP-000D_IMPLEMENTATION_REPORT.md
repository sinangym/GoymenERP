# ERP-000D Uygulama Teslim Raporu

- Tarih: 2026-08-27
- Dal: `codex/erp-000d-audit-correlation`
- Taban: `main` / `fe87fc4f995a4dc4fb2e38b1399e96c95a2d09cd`
- Durum: İki review blocker'ı kapatıldı ve yerel kabul doğrulaması tamamlandı;
  uzak CI ve son re-review bekleniyor
- Bağlayıcı ADR: `docs/adr/ADR-009-audit-and-correlation.md`

## Sonuç

ERP-000D kapsamında `Platform.Audit` modülü, istek kapsamlı correlation/causation
bağlamı ve ilk uçtan uca kritik komut audit entegrasyonu tamamlandı. Audit kaydı teknik
log olarak değil; actor + company + role/permission + before/after + reason + approval
alanı + AI involvement + source channel + istemci + nedensellik taşıyan sürümlü iş
kanıtı olarak modellenmiştir.

Lokasyon aktif/pasif değişikliği ile audit insert aynı EF `SaveChanges` ve aynı SQL
transaction'ındadır. Audit insert veritabanında reddedildiğinde lokasyon update'i de
rollback olur. Bu davranış gerçek SQL Server üzerinde hata enjeksiyonuyla kanıtlandı.

Draft PR #2 raporlama deltası bu dala alınmadı ve değiştirilmedi. ERP-000E kalıcı
idempotency/concurrency ile ERP-000F outbox/inbox kapsamı da bu göreve karıştırılmadı.

## Tamamlanan platform kapsamı

- Audit Domain/Application/Infrastructure proje sınırları ve `audit` SQL şeması.
- Ayrı `AuditDbContext`, migrations history ve `InitialAudit` migration'ı.
- İş modüllerinin tabloya doğrudan yazmak yerine kullandığı `IAuditEventCollector`.
- Kritik iş değişikliği ve audit insert'i aynı transaction'a ekleyen
  `AuditSaveChangesInterceptor`.
- Başarısız/cancel edilmiş save sonrasında bekleyen audit entity'sini tracker'dan
  ayıran güvenli cleanup.
- Uygulama ve SQL katmanında append-only koruma.
- Şirket, zaman, correlation ve entity erişim yolları için indeksler.
- JSON geçerliliği, boş CompanyId ve schema version SQL check constraint'leri.
- SHA-256 idempotency key özeti ve tüm kanıt zarfı için SHA-256 integrity hash.
- Draft değerlerini önce kalıcı biçime canonicalize eden, hash'i aynı zarf üzerinden
  üreten ve DB satırından yeniden hesaplanabilir kılan integrity sözleşmesi.
- Server tarafından kurulan değişmez `CorrelationContext` ve response correlation
  header'ı.
- Yalnız Company kapsamlı `identity.audit.read` + `CompanyContext` korumalı audit okuma
  endpoint'i; Site/Location/WorkCenter kapsamlı izin fail-closed reddedilir.
- Yüksek hacim için toplam saymayan, indeks uyumlu cursor/keyset sayfalama.
- Etkili RBAC rol kodlarının permission scope ile birlikte snapshot olarak taşınması.
- AT-003 kabul matrisinin otomatik kanıtlarla güncellenmesi.
- CI backend kapısına idempotent audit migration script üretiminin eklenmesi.

## Audit olay sözleşmesi

Her kalıcı olay şu alan gruplarını taşır:

| Alan grubu | İçerik |
|---|---|
| Olay | `Id`, `EventType`, `Operation`, `Outcome`, `OccurredAtUtc`, `SchemaVersion` |
| Kayıt | `EntityType`, `EntityId`, `EntityVersionBefore`, `EntityVersionAfter` |
| Tenant | zorunlu `CompanyId` |
| Aktör/yetki | `ActorType`, yerel `ActorId`, etkili `RoleCodesJson`, `PermissionCode` |
| Değişiklik | açık `BeforeJson`, `AfterJson`, zorunlu `Reason` |
| Onay | nullable `ApprovalContextJson` |
| AI | `AiInvolvement` ve nullable `AiContextJson` |
| İstemci | güvenilir `SourceChannel`, IP ve User-Agent |
| Nedensellik | `CorrelationId`, nullable `CausationId` |
| Kanıt | ham anahtar yerine `IdempotencyKeyHash`, olay `IntegrityHash` |

`AiInvolvement` başlangıç sözlüğü `None/Assisted/Proposed/Executed`; source channel
sözlüğü `Web/Mobile/Import/Integration/AiAssisted/System` olarak kilitlidir. Mevcut HTTP
akışı source channel değerini server tarafında `Web` üretir. İstemci AI veya kanal
seviyesini kendi header'ıyla yükseltemez.

## İlk dikey entegrasyon

`PUT /api/v1/organization/locations/{id}/active-state` artık:

1. doğrulanmış yerel kullanıcı ve seçili şirket,
2. `organization.locations.status.update` permission + ABAC scope,
3. beklenen kayıt sürümü,
4. mevcut `Idempotency-Key` zorunluluğu,
5. güvenli correlation/causation bağlamı,
6. 1–1000 karakter zorunlu gerekçe,
7. atomik before/after audit olayı

olmadan çalışmaz. Request contract'ına `Reason` eklendi. Mevcut frontend bu mutation
endpoint'ini çağırmadığı için UI geriye uyumluluk kırığı oluşmadı; ilerideki form bu
gerekçeyi açık kullanıcı alanı olarak sunmalıdır.

State gerçekten değişiyorsa `Activate/Deactivate`, aynı değer tekrar isteniyorsa
`NoChange` audit operasyonu üretilir. Yetkisiz, başka şirket kaydı, bulunamayan kayıt,
version conflict, eksik gerekçe veya geçersiz correlation kalıcı başarılı iş olayı
üretmez.

## Değişmezlik ve şirket güvenliği

- Public audit mutation endpoint'i yoktur.
- `AuditDbContext.SaveChanges*` normal uygulama yazmasını koşulsuz reddeder.
- EF interceptor tracked audit `Modified/Deleted` durumunu reddeder.
- SQL trigger doğrudan `UPDATE/DELETE` işlemlerini hata `51000` ile reddeder.
- Audit okuma permission kontrolünü hem endpoint hem query service uygular.
- `identity.audit.read` merkezi authorization katmanında Company kapsamı zorunlu bir
  permission'dır; dar kapsamlı rol HTTP katmanında `403` alır.
- EF global query filter seçili `CompanyId` değerini her audit sorgusunda zorunlu tutar.
- A şirketinde audit okuyan grup yöneticisi, B şirketi correlation kimliğini bilse dahi
  sonuç veya toplam bilgisi alamaz.
- Ham idempotency anahtarı audit tablosuna yazılmaz.
- Integrity hash ham draft'tan değil; trim edilmiş zorunlu/opsiyonel alanlar, tekrarsız
  ve sıralı rol kodları ile kalıcılaştırılan canonical zarftan hesaplanır. Bu nedenle
  SQL Server'dan okunan satır aynı SHA-256 değerini yeniden üretir.

SQL sysadmin teknik olarak trigger'ı devre dışı bırakabilir. Off-site/WORM arşiv,
legal hold, imzalı/zaman damgalı dış kanıt ve retention/imha politikası bu kabulün
dışındadır; `OPEN-018` ve backup/restore çalışmasıyla production öncesi tamamlanmalıdır.

## Migration ve rollback

- Migration: `20260827160529_InitialAudit`.
- Ayrı history: `[audit].[__EFMigrationsHistory]`.
- CI artefaktı: idempotent `audit-migration.sql`.
- `Down` boş geliştirme/test veritabanında tabloyu kaldırabilir.
- Production'da ilk audit olayından sonra tablo düşüren rollback uygulanmaz. Uygulama
  binary'si geri alınsa dahi audit şeması korunur; uyumsuzluk forward-fix migration ile
  çözülür.

## Doğrulama sonucu

- Release backend testleri: toplam **42/42** geçti.
  - Audit unit: 5/5.
  - Identity unit: 7/7.
  - Organization unit: 10/10.
  - HTTP/claims/audit integration: 19/19.
  - Gerçek Docker SQL Server relational acceptance: 1/1.
- SQL kabulünde migration/seed, audit insert hata enjeksiyonu ile atomik rollback,
  başarılı update+audit, no-change audit, SQL `51000` update/delete reddi, rol/izin
  snapshot'ı, canonical kalıcı zarftan integrity hash yeniden hesaplama, dar kapsamlı
  audit okuyucuya `403`, A/B audit izolasyonu ve gerçek SQL keyset sayfalama doğrulandı.
- Organization, Identity ve Audit idempotent migration scriptleri üretildi.
- `dotnet format --verify-no-changes` ve `git diff --check` geçti.
- NuGet vulnerability taraması: bilinen açık yok.
- Frontend: lint geçti, 2/2 test geçti, production build geçti; npm açık yok.
- Docker Compose model doğrulaması ile güncel API/web image build geçti.

## Bilinçli kapsam dışı ve sonraki kapı

- ERP-000E: kalıcı idempotency key store, duplicate response replay ve genel concurrency
  sözleşmesi. ERP-000D yalnız mevcut header zorunluluğunu ve anahtar hash kanıtını taşır.
- ERP-000F: transactional outbox/inbox, retry ve dedup.
- Gerçek approval motoru: alan hazırdır, mevcut lokasyon komutunda approval yoktur.
- Gerçek AI Gateway: alan ve allowlist hazırdır; mevcut olay `AiInvolvement=None` taşır.
- Yetkisiz/başarısız denemelerin kalıcı güvenlik olayı deposu ve gelişmiş trace/metric
  ERP-000L gözlemlenebilirlik sözleşmesiyle ele alınmalıdır.
- WORM/harici audit arşivi, legal hold ve retention politikası.
- Sentetik büyük audit hacmi p95/indeks PoC'si ERP-000M performans kapısında ölçülmelidir.

ERP-000D review ve uzak CI geçmeden task `done` yapılmamalı ve ERP-000E'ye
başlanmamalıdır.
