# Platform — Dalga 6 kaynak raporu

## Dilimler

| Dilim | Durum | Kaynak değişikliği |
|---|---|---|
| PLT-S13 | DONE (kaynak) | README'nin eski ERP-000C kapsamı ve “idempotency/audit sonra” anlatımı güncellendi; kabul matrisindeki otomatik test türü ile güncel head kabulü ayrıldı. `70b0cf189`, `eacf650b7`. Tarihsel backlog başlangıç istemi değiştirilmedi; güncel dilim durumu entegrasyonda KALAN_PLAN üzerinden tutulur. |
| W5 mimari Audit sahiplik düzeltmesi | SOURCE_READY | Identity yalnız Audit.Application sözleşmesine başvurur; AuditEvent model eşlemesi ve host interceptor'ı kaldırıldı. Audit owner portu aynı açık SQL transaction'ına katılır. Başarı ve terminal ret audit'i owner üzerinden yazılır; hata/iptal sonunda pending taslaklar temizlenir. `1cf78e1f3`, `90d728800`. |

`1cf78e1f3` mesajındaki PLT-S13 etiketi kapsam sınıflandırma hatasıdır: kod değişikliği
W5_DUZELTME'deki mimari onarımdır; PLT-S13 yalnız belge/durum senkronudur. Commit geçmişi
değiştirilmedi. Mimari dayanak AGENTS modül sahipliği değişmezi ile mevcut KIM-D1/KIM-D7'dir.

## Migration

- EF üretimi: `20261116164010_RemoveIdentityAuditTableMapping`.
- Up ve Down boş: önceden migration dışında tutulan Audit eşlemesi yalnız Identity modelinden çıkarılır.
- Audit tablosu veya verisi yaratılmaz/silinmez; veri kaybı olmadığı için yeni THROW yok.
- Designer ve model snapshot üretici çıktısıyla birlikte commit edildi; snapshot elle yazılmadı.
- Eski Identity→Audit.Infrastructure istisnası bağımlılık listesinden kaldırıldı; yeni istisna eklenmedi.

## Yazılan kabul kaynakları

- `IdentityAccessAssignmentSqlServerRelationalAcceptanceTests.An_Audit_owner_failure_rolls_back_the_assignment_outbox_answer_and_audit_then_the_same_key_can_succeed`.
- `IdentityAccessAssignmentSqlServerRelationalAcceptanceTests.The_transactional_Audit_owner_rejects_a_foreign_company_batch_and_discards_its_pending_drafts`.
- Identity model testi owner mapping'in yokluğunu, EF snapshot uyumunu ve metadata migration'ın boş Up/Down'unu doğrular.
- Önceden var olan Identity gerçek-SQL atama/iptal/ret/replay testleri korunur.

## Doğrulama

API, Identity.UnitTests ve Api.IntegrationTests ön derlemeleri exit 0, sıfır hata/uyarı verdi.
Son şirket-B audit assertion'ı bu ön derlemeden sonra eklendi; birleşik dalga head'i yeniden
derlenmelidir. Birim/mimari/HTTP/gerçek SQL/tarayıcı/Vitest test koşumu ve CI: **NOT_RUN**.
Başarıyla derlemek gerçek SQL atomiklik kabulü değildir.

## Entegrasyon işi ve kalanlar

- Yeni iki SQL fact relational Identity envanterine, yeni migration recovery/release envanterine alınmalı.
- AuditDbContext/IdentityDbContext ve değişen owner kaynak fingerprint'leri üreticiyle güncellenmeli.
- K3: Audit owner transaction katılımı, başarısız yazıda rollback ve terminal ret/replay yeniden incelenmeli.
- W5'in 12 bypass ve 5 context construction kaynağındaki “K3 review bekliyor” kayıtlarına dokunulmadı.
- KLN-D114 bildirim deposu uygulaması bu belge düzeltmesiyle tamamlanmış sayılmaz; 90 günün ayarlanabilirliği
  BELGE-Q4 olarak #1189'da soruldu. Audit'in bildirim yaşam döngüsünden bağımsızlığı korunur.
- Karar/master/OPEN durumu, grant/rol, production ve Finance STOP değiştirilmedi.
