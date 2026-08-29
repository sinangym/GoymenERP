# Identity Global Security Evidence Readiness v1

- Tarih: 2026-08-29
- Doğrulanan taban: `main@d880044d7802581f8a8298a55d5821607f0896c8`
- Takip kaydı: GitHub Issue `#63`
- İlk producer adayı: Issue `#62` production reconciliation runner
- İlgili company audit kararı: `docs/adr/ADR-009-audit-and-correlation.md`
- Durum: **DRAFT / NO-GO**
- Production activation: **YASAK**

Bu belge yeni bir ADR, fiziksel veri modeli, migration veya production-ready kanıt
altyapısı değildir. Issue `#63` açık kalır. Buradaki logical model ve kabul matrisi
insan Identity/security/audit review'u ile kabul edilmeden implementation başlatılamaz.

## 1. Amaç

Company bağımsız Identity/control-plane güvenlik değişiklikleri için:

- tenant audit'i zayıflatmayan,
- dummy `CompanyId` veya query-filter bypass kullanmayan,
- grant ile kanıtı atomik bağlayan,
- append-only ve canonical integrity doğrulaması taşıyan,
- normal API erişimine kapalı,
- actor, approval, release, plan ve environment provenance'ını kaybetmeyen

bir global security evidence readiness sınırı tanımlamak.

İlk producer, Option A production reconciliation'dır. Model gelecekteki global rol,
permission ve güvenlik politikası değişikliklerine genişleyebilir; ancak her yeni event
türü ve producer ayrıca kabul edilmelidir.

## 2. Neden mevcut tenant audit yeniden kullanılamaz

`docs/adr/ADR-009-audit-and-correlation.md` ve mevcut Audit kodu şirket kapsamlıdır:

- `AuditEvent` boş olmayan `CompanyId` ister.
- `AuditDbContext` aktif `CompanyContext.CompanyId` üzerinden global query filter uygular.
- Audit okuma `identity.audit.read` için company-wide assignment gerektirir.
- Tenant audit'in anlamı belirli şirket içindeki iş olayını kanıtlamaktır.

Option A sistem rolü grant'i tek bir şirkete ait değildir. Şunlar yasaktır:

- `Guid.Empty` veya dummy `CompanyId` kullanmak,
- rastgele bir şirketi event owner yapmak,
- `IgnoreQueryFilters` ile tenant audit'i global store gibi kullanmak,
- global event'i tüm şirket audit okuyucularına göstermek,
- tenant audit tablosunun constraint veya filter'ını sessizce gevşetmek.

Bu nedenle global Identity/control-plane evidence ayrı sahiplik ve authorization
sınırında olmalıdır. Fiziksel schema/table/DbContext seçimi bu DRAFT ile yapılmaz.

## 3. Doğrulanmış mevcut boşluk

Mevcut `SystemRolePermissionReconciler`:

- role grant ile aynı SQL transaction içinde çalışır,
- fakat global evidence üretmez,
- actor, approval, environment, release veya plan hash taşımaz,
- yalnız `RolesChecked` ve `PermissionsAdded` sonucu döndürür.

Dolayısıyla mevcut çekirdek production'da çalıştırılırsa kim, hangi onay/release/plan ile
hangi environment'ta grant verdiği değişmez bir control-plane olayıyla kanıtlanamaz.
Issue `#63` kapanmadan production grant yasaktır.

## 4. Sahiplik ve güven sınırı

Önerilen logical ownership:

- Sahip modül: Identity veya açıkça ayrılmış global security control-plane sınırı.
- Scope: Global/system; `CompanyId` yoktur.
- Yazma: Yalnız trusted upgrade/security executor.
- Okuma: İlk sürümde public HTTP endpoint yok; default-deny.
- Tenant görünürlüğü: Tenant audit query/service/API üzerinden görünmez.
- Mutation: Normal application code, API runtime principal ve kullanıcı rolleri için yok.
- Dış yayın: Outbox/harici SIEM/export kararı kabul edilene kadar otomatik publication yok.

Bir modülün bu evidence tablosuna doğrudan yazması yasaktır. Producer, kabul edilmiş
Application contract/executor üzerinden canonical draft bırakmalıdır. Fiziksel
cross-module dependency yeni ADR/allowlist kararı olmadan eklenemez.

## 5. Önerilen logical event envelope

Aşağıdaki alanlar production readiness için minimum öneridir. İsim, uzunluk, DB türü,
index ve physical schema bu DRAFT ile kilitlenmez.

### 5.1 Kimlik ve zaman

| Alan | Anlam |
|---|---|
| `EventId` | Tekil immutable event kimliği |
| `SchemaVersion` | Canonical envelope sürümü |
| `OccurredAtUtc` | Güvenilir executor tarafından belirlenen UTC zaman |
| `ExecutionId` | Upgrade/reconciliation çalışması kimliği |
| `EventType` | Sürümlü global security event türü |

### 5.2 Aktör ve provenance

| Alan | Anlam |
|---|---|
| `ActorType` | Workload, deployment actor veya kabul edilmiş system actor sınıfı |
| `ActorId` | Güvenilir workload/actor kimliği |
| `ApprovalReference` | İnsan onayı/SoD kaydı referansı |
| `SourceChannel` | Server tarafından belirlenen upgrade/control-plane kanalı |
| `EnvironmentIdentity` | Doğrulanmış hedef ortam kimliği |
| `ReleaseArtifactDigest` | Çalışan onaylı artifact/manifest digest'i |
| `DeploymentExecutionId` | CI/CD veya change execution referansı |
| `CorrelationId` | Uçtan uca correlation kimliği |
| `CausationId` | Önceki onay/deployment/plan olayının nedensellik bağı |

### 5.3 Plan ve hedef

| Alan | Anlam |
|---|---|
| `PlanId` / `PlanSchemaVersion` | Kabul edilmiş reconciliation planı |
| `PlanHash` | Canonical plan SHA-256 özeti |
| `ContractHash` | Exact politika contract/artifact özeti |
| `PreviewHash` | Apply'a bağlanan canonical preflight sonucu |
| `RoleId` / `RoleCode` | Exact hedef rol |
| `PermissionCodesJson` | Ordinal sıralı, duplicate içermeyen canonical permission listesi |
| `BeforeJson` | Değişiklik öncesi canonical security snapshot |
| `AfterJson` | Değişiklik sonrası canonical security snapshot |

### 5.4 Sonuç ve bütünlük

| Alan | Anlam |
|---|---|
| `Outcome` | `Applied`, `NoOp`, `Rejected` veya `Failed` |
| `ReasonCode` | Sürümlü, makine-okunur neden sınıfı |
| `Reason` | Güvenilir ve redacted insan/sistem açıklaması |
| `AffectedRowCount` | Gerçek business etkisi cardinality'si |
| `ErrorClass` | Exception text/secret yerine redacted hata sınıfı |
| `IntegrityHash` | Kalıcı canonical envelope üzerinden SHA-256 |

Event'e raw connection string, token, credential, secret, stack trace, SQL text,
gereksiz kişisel veri veya serbest hassas payload yazılmaz. Actor/provenance alanları
istemci header'ından güvenilmez biçimde alınamaz.

## 6. Canonicalization ve integrity sözleşmesi

Integrity hash ham request/draft üzerinden değil, **persist edilecek canonical envelope**
üzerinden hesaplanmalıdır. Minimum davranış:

1. Alanlar doğrulanır ve kabul edilmiş normalization uygulanır.
2. Listeler duplicate'siz ve ordinal sıralı hale getirilir.
3. Before/after JSON kabul edilmiş canonical JSON sözleşmesine çevrilir.
4. Optional alanların null/empty semantiği tek biçime getirilir.
5. Sürümlü canonical byte dizisi üretilir.
6. `IntegrityHash` bu byte dizisinin SHA-256 özetidir.
7. DB'den yeniden okunan kalıcı alanlarla aynı hash tekrar üretilebilir.

Canonical alan sırası, encoding, GUID/zaman/enum gösterimi, JSON number/string
normalization ve digest case kuralı fiziksel contract'ta açıkça sürümlenmelidir.
Unknown/duplicate/non-canonical alan sessizce kabul edilmez.

Hash tek başına güçlü bir imza veya privileged DBA'ya karşı bağımsız kanıt değildir.
Mevcut ADR-009 gibi application ve DB mutation koruması sağlar; WORM/off-site anchor,
signature, timestamp, key custody ve trust-root kararı olmadan "DBA dahil değiştirilemez"
iddiası kurulamaz.

## 7. Outcome semantiği ve atomiklik

### 7.1 Applied

- Exact permission grant ve `Applied` evidence aynı Identity SQL transaction ve aynı
  commit sınırında kalıcılaşır.
- Evidence insert başarısızsa grant rollback olur.
- Grant başarısızsa evidence aynı transaction'da commit olmaz.
- `AffectedRowCount` gerçek grant etkisini gösterir.

### 7.2 NoOp

- Permission zaten varsa yeni grant üretilmez.
- Çalışmanın exact plan/release/environment üzerinde gerçekten yapıldığı `NoOp` evidence
  ile aynı transaction sınırında kaydedilir.
- No-op sessiz başarı değildir; execution ve approval provenance korunur.

### 7.3 Rejected

- Plan, hash, approval, environment veya role drift reddinde grant sıfırdır.
- Evidence store sağlıklı ve trusted actor doğrulanmışsa rejection evidence kaydedilir.
- Rejection evidence yazılamazsa olay başarıya çevrilmez; ayrı fail-closed outcome gerekir.
- Pre-database doğrulama reddinin hangi durable kanala yazılacağı açık karardır.

### 7.4 Failed

- Grant/evidence transaction hatasında transaction rollback olur.
- Rollback sonrası aynı transaction içinde failed event commit edilemez.
- Store hâlâ sağlıklıysa ayrı ve açık bir failure-evidence denemesi yapılabilir; bunun
  idempotency ve güvenilirlik sözleşmesi ayrıca kabul edilmelidir.
- Primary store kullanılamıyorsa kendi başarısızlığını aynı store'a yazacağı garanti
  edilemez. Bu gerçek gizlenmez; production activation durur.

Bu semantik "her hata mutlaka aynı DB'ye yazılır" şeklinde fiziksel olarak imkânsız bir
iddia kurmaz. Primary store kesintisinde gereken durable secondary channel, provider ve
delivery garantisi açık altyapı kararıdır.

## 8. Append-only savunma katmanları

Gelecek implementation en az şu katmanları kanıtlamalıdır:

- public create/update/delete endpoint'i yok,
- normal API DI yüzeyinden mutation service çözülemez,
- aggregate/entity üzerinde public mutator yok,
- normal DbContext `SaveChanges*` ile update/delete fail-closed,
- tracked-state guard update/delete'i reddeder,
- SQL Server trigger doğrudan `UPDATE` ve `DELETE` işlemini reddeder,
- event ID/execution uniqueness ve gerekli constraint'ler,
- canonical persisted envelope hash recompute testi,
- least-privilege runtime principal'ın insert/update/delete erişimi yok,
- upgrade executor'ın yalnız kabul edilmiş insert/executor yüzeyine erişimi,
- retention job kabul edilene kadar delete yolu yok.

EF guard veya trigger privileged DBA'ya karşı WORM garantisi değildir. Fiziksel
implementation PR'ı kanıt seviyesini doğru adlandırmalı ve kapsamadığı tehditleri
gizlememelidir.

## 9. Okuma, non-disclosure ve görev ayrılığı

İlk sürüm için güvenli varsayılan:

- public HTTP read endpoint yok,
- `identity.audit.read` global evidence okuma yetkisi sayılmaz,
- company-scoped audit okuyucusu global eventleri göremez,
- normal support/operator rolü event varlığını, sayısını veya hata ayrıntısını
  çıkaramaz,
- export/read permission, approval ve break-glass ayrı açık karardır,
- actor ile evidence okuyucu/approver görev ayrılığı kabul edilmiş politika üzerinden
  uygulanır.

Bu DRAFT yeni permission kodu ilan etmez. Global read permission adı, rol matrisi,
scope ve non-disclosure davranışı insan security kararı gerektirir.

## 10. Retention, export ve dış kanıt

Retention/imha politikası kabul edilmeden global evidence silinmez. Şunlar açıktır:

- saklama süresi ve legal hold,
- yetkili imha ve dört-göz onayı,
- export formatı ve redaction,
- WORM/off-site kopya,
- SIEM/security monitoring yayını,
- cryptographic signing/timestamp,
- key custody, rotation ve trust root,
- DB backup/restore sonrası chain doğrulaması,
- disaster recovery ortamında evidence continuity.

Bu kararlar `OPEN-018`, backup/restore ve delivery/observability işleriyle uyumlu
kapanmalıdır. Şu an herhangi bir provider veya süre seçilmez.

## 11. Kabul matrisi

Bu matris physical schema veya implementation onayı değildir; minimum güvenlik
kanıtını tanımlar.

| ID | Senaryo | Beklenen kanıt |
|---|---|---|
| `ID-EVD-001` | Valid Applied envelope | Exact grant + event aynı commit; persisted hash recompute eşit |
| `ID-EVD-002` | Valid NoOp execution | Grant yok; immutable no-op event ve provenance var |
| `ID-EVD-003` | Evidence insert failure | Permission grant rollback; yarım state yok |
| `ID-EVD-004` | Permission insert failure | Evidence/grant transaction rollback; başarı raporu yok |
| `ID-EVD-005` | Rejected plan/hash/approval | Grant sıfır; store sağlıklıysa redacted rejection evidence |
| `ID-EVD-006` | Primary store unavailable | Grant yok; distinct NO-GO; sahte "evidence yazıldı" iddiası yok |
| `ID-EVD-007` | DB'den persisted-field recompute | SHA-256 exact eşit |
| `ID-EVD-008` | Persisted alanda mutation | Recompute farkı ve integrity doğrulama hatası |
| `ID-EVD-009` | EF tracked update/delete | Fail-closed |
| `ID-EVD-010` | Direct SQL update/delete | Append-only trigger reddi |
| `ID-EVD-011` | Duplicate EventId/ExecutionId | Kabul edilmiş idempotency kuralına göre deterministic red/replay; duplicate event yok |
| `ID-EVD-012` | Aynı execution ID + farklı plan/hash | Kesin red; grant/evidence çakışması yok |
| `ID-EVD-013` | Non-canonical/duplicate permission JSON | Kalıcılaştırmadan red |
| `ID-EVD-014` | Dummy/empty CompanyId denemesi | Global modelde CompanyId kullanılmaz; tenant audit'e event yazılmaz |
| `ID-EVD-015` | Tenant audit reader | Global event, count ve error detail görünmez |
| `ID-EVD-016` | Normal API runtime principal | Global evidence read/mutation yok |
| `ID-EVD-017` | Upgrade principal | Yalnız accepted least-privilege insert/executor yüzeyi |
| `ID-EVD-018` | Concurrent Option A apply | Bir `Applied`, bir `NoOp`; toplam tek grant; iki ayrı execution evidence |
| `ID-EVD-019` | Cancellation/timeout | Transaction sonucu kesin; unknown state başarı sayılmaz |
| `ID-EVD-020` | Log/export redaction | Secret, raw token/connection ve gereksiz kişisel veri yok |
| `ID-EVD-021` | Backup/restore | Event count, canonical hash ve chain/anchor kapsamı kabul edilmiş seviyede doğrulanır |
| `ID-EVD-022` | Retention/WORM/signing iddiası | Yalnız gerçekten uygulanmış ve test edilmiş katmanlar raporlanır |

Atomiklik, trigger, constraint, concurrency ve rollback testleri gerçek SQL Server'da
çalışmalıdır. InMemory/mock kanıtı bunların yerine geçmez.

## 12. Açık insan ve altyapı girdileri

| Açık girdi | Neden zorunlu |
|---|---|
| Sahip modül ve physical schema/DbContext | Migration ve transaction ownership |
| Grant + evidence aynı transaction entegrasyon yolu | Cross-module atomiklik ve dependency sınırı |
| Event type/schema/canonicalization sürümü | Deterministik kalıcılık ve hash recompute |
| Inactive/non-system/name drift için kanonik role metadata kararı | Outcome ve before/after anlamı |
| Trusted actor/workload identity | Actor provenance |
| Approval/SoD/break-glass | Yetkili production değişikliği |
| Global read/export permission ve rol matrisi | Default-deny/non-disclosure |
| Runtime ve upgrade SQL principal grant'leri | Least privilege |
| Retention/legal hold/imha | Append-only yaşam döngüsü |
| WORM/off-site/signing/timestamp/trust root | Privileged tamper iddiasının seviyesi |
| SIEM/secondary durable failure channel | Primary store kesintisinde failure evidence |
| Secret/redaction ve hassas veri sınıflaması | Kanıtın veri sızıntısına dönüşmemesi |
| Backup/restore/DR continuity | Kanıt zincirinin geri yükleme sonrası güveni |
| Fiziksel ADR-010 ve deployment ownership | Release/runner/evidence çalışma sırası |
| Production activation owner/window | Nihai insan go/no-go |

Bu girdiler bu belgeyle kapanmış veya varsayılmış sayılmaz.

## 13. Güvenli gelecek uygulama sırası

1. Issue `#63` açık kararlarını Identity/security/audit insan review'uyla kapat.
2. Fiziksel ADR-010, migration ownership ve runner/evidence transaction sınırını kabul et.
3. Logical envelope/canonicalization ve authorization contract'ını sürümle.
4. Ayrı global evidence schema/migration ve append-only savunmaları uygula.
5. Persisted-envelope hash, mutation, non-disclosure ve real-SQL testlerini tamamla.
6. Option A executor içinde grant + evidence aynı transaction entegrasyonunu uygula.
7. Preflight/apply, rejection/failure ve concurrency acceptance matrisini tamamla.
8. Least-privilege infrastructure adapter ve secret resolver'ı seçilen ortamda doğrula.
9. Backup/restore, redaction, external evidence ve staging provası yap.
10. Exact-head security/audit/deployment review ve insan go/no-go sonrası activation planla.

Bu sıra yeni branch, migration veya production kodu başlatma yetkisi değildir.

## 14. Readiness ve Definition of Done

### Bu DRAFT için

- [x] Tenant audit ile global control-plane evidence ayrılmıştır.
- [x] Dummy CompanyId/filter bypass yasakları görünürdür.
- [x] Minimum logical envelope ve canonical hash sınırı tanımlıdır.
- [x] Applied/NoOp grant + evidence atomikliği zorunlu kapıdır.
- [x] Failure-store dürüstlük sınırı ve secondary channel açığı görünürdür.
- [x] Append-only iddiasının koruduğu/korumadığı tehditler ayrılmıştır.
- [x] Kabul matrisi ve açık insan/altyapı girdileri ayrılmıştır.
- [x] Issue `#63` açık ve NO-GO kalır.

### Implementation-ready olma kapısı

- [ ] Physical owner/schema/migration ve transaction sınırı kabul edilmiştir.
- [ ] Canonical envelope/hash ve outcome semantiği kabul edilmiştir.
- [ ] Trusted actor, approval/SoD ve least-privilege modeli kabul edilmiştir.
- [ ] Global read/export, retention/legal hold ve non-disclosure politikası kabul edilmiştir.
- [ ] WORM/signing/secondary failure evidence için iddia seviyesi ve altyapı kararı vardır.
- [ ] Backup/restore ve disaster continuity acceptance planı kabul edilmiştir.
- [ ] Fiziksel ADR-010 ve production runner entegrasyon sırası kabul edilmiştir.
- [ ] Identity/security/audit insan review'u tamamlanmıştır.

Bu checklist tamamlanmadan Issue `#63` kapatılamaz, implementation başlatılamaz ve
production reconciliation aktive edilemez.
