---
contract_id: ERP-000F-OUTBOX-INBOX-CONTRACT
version: 1
contract_status: OPEN
readiness_document_status: ACCEPTABLE
implementation_status: BLOCKED
production_activation: NO-GO
owner: CODEX-PLATFORM
review_class: SECURITY_MESSAGING_CROSS_MODULE_CONTRACT
reviewed_base_sha: 83197c50f515db959638fadd7cc11ad6eb7a4fc8
---

# ERP-000F — Outbox/Inbox Fiziksel Readiness ve Contract v1

## 1. Sonuç

Transactional outbox + at-least-once teslim + inbox/dedup yönü kanonik olarak kabul
edilmiştir. Güncel platform; CompanyContext, default-deny RBAC/ABAC, append-only audit,
server-side correlation ve kalıcı idempotency/concurrency temeline sahiptir.

ERP-000F implementation yine de **BLOCKED**tır. Bunun nedenleri:

1. fiziksel ayrıntılı ADR-006 exact-SHA review bekler;
2. `ERP-000M` migration/provider kanıtı ve `ERP-000O` kalite/yayın kapısı tamamlanmamıştır;
3. `OPEN-022..025` açık kalmıştır;
4. gerçek revocable workload principal ve trusted tenant-discovery contract yoktur;
5. root causation, transaction enlistment ve uncertain-send davranışı feature code için
   uygulanabilir biçimde henüz kanıtlanmamıştır.

Bu belge feature code, migration, worker, endpoint, test fixture/substrate veya pilot event
oluşturmaz. Provider, paket, broker, topoloji, event, pilot, eşik, süre, retry veya retention
değeri seçmez.

## 2. Kaynak ve sahiplik sınırı

Bağlayıcı kaynaklar:

- `AGENTS.md`;
- `DEC-GOV-002`, `DEC-TEC-001`, `DEC-TEC-008`, `DEC-TEC-009`, `DEC-TST-002`;
- `ADR-002`, `ADR-004`, `ADR-009`, `ADR-011`;
- `docs/adr/ADR-006-transactional-outbox-inbox.md`;
- `docs/governance/CODEX_OTONOM_PROJE_ORKESTRATORU_v1.md`;
- `docs/acceptance/ERP-000F_ACCEPTANCE.md`.

Sahiplik:

| Alan | Sahip | ERP-000F sınırı |
|---|---|---|
| Event adı/payload/emission | Producer domain/Application | Messaging tahmin etmez. |
| Ortak envelope/outbox/inbox | Platform messaging | Semantic ve güvenlik contract'ını sağlar. |
| Consumer business effect | Consumer domain/Application | Messaging iş kuralını sahiplenmez. |
| Kimlik/yetki | Identity + server policy | Worker kullanıcı taklidi yapmaz. |
| Aktif şirket ve CompanyContext | Organization | Header/payload tenant kaynağı değildir. |
| Audit/correlation | Platform Audit/BuildingBlocks | Audit event mesajın kendisi değildir. |
| Command replay | ERP-000E Idempotency | Inbox dedup yerine geçmez. |
| Migration/provider suitability | ERP-000M | Bu docs-only iş seçim yapmaz. |
| CI/artifact/migration gates | ERP-000O | F kanıtları kabul edilmiş gate'e eklenir. |
| Pilot Cari producer | ChatGPT-Cari + kullanıcı kararı | `OPEN-025` olmadan seçilemez. |

## 3. Manifest ve DAG sözleşmesi

`ERP-000F`:

- `status = blocked`;
- direct dependencies = `A/B/C/D/E/M/O`;
- blockers = `ADR-006`, `OPEN-022`, `OPEN-023`, `OPEN-024`, `OPEN-025`.

F; N veya L'ye bağımlı değildir. N, F recovery kanıtını; L ise F telemetry/queue
kanıtını tüketir. F, G/H/I/J/K veya Cari modülünü kendi içinde uygulamaz.

## 4. Kapsam

### 4.1 Kapsam içi

- Producer transaction'a bağlı kalıcı outbox niyeti.
- Sürüm kontrollü ortak envelope ve bütünlük kanıtı.
- At-least-once dispatcher; atomik claim, fencing ve crash recovery sözleşmesi.
- Uncertain-send sonucu ve kör retry yasağı.
- Revocable workload identity, trusted company discovery ve fail-closed revalidation.
- CompanyId-scoped inbox/dedup ve consumer transaction atomikliği.
- Correlation/canonical causation, audit ve ERP-000E idempotency ayrımı.
- Adapter-independent transport portu.
- Migration/forward-fix, gerçek SQL race/constraint ve telemetry kabul planı.

### 4.2 Kapsam dışı

- Gerçek broker, e-posta, Mikro, e-belge, WhatsApp veya başka adapter seçimi.
- Host, process, cluster, queue veya deployment topolojisi.
- İlk business event ve Cari/domain producer değişikliği.
- Retry/lease/timeout/batch/parallelism/retention/alert için sayısal değer.
- Event sourcing veya audit tablosunu integration bus saymak.
- PWA/offline client outbox'ı.
- Operatör UI route/SCREEN-ID tasarlamak.
- Production cleanup, replay veya dış mesaj gönderimi.

## 5. Değişmez semantic sözleşme

1. Business commit varsa publish niyeti kaybolmaz; business rollback ise mesaj yoktur.
2. Transport exactly-once değildir; duplicate teslim beklenir ve test edilir.
3. Aynı semantic event aynı MessageId'yi retry/re-drive boyunca korur.
4. Producer/consumer payload veya CompanyId beyanı authorization kaynağı değildir.
5. Event ve envelope sürümleri ayrıdır; eski kayıtlar yerinde yeni anlamla değiştirilmez.
6. Normal writer immutable identity/payload/hash alanlarını update/delete edemez.
7. Consumer transaction içinden geri alınamaz dış etki çağrılmaz.
8. İş etkisi, audit/command result/outbox veya inbox/consumer/downstream-outbox
   atomiklik grupları aynı fiziksel SQL transaction ile kanıtlanır.
9. Bir modül diğer modül tablosuna doğrudan yazmaz.
10. Open policy veya provider seçimi magic number/string olarak kodlanmaz.

## 6. Envelope ve schema contract

Zorunlu semantic alanlar:

- server-generated `MessageId`;
- `EnvelopeSchemaVersion`, `EventSchemaVersion`;
- registered `SourceSystem`, `ProducerModule`, `EventType`;
- server-verified `CompanyId`;
- server-controlled UTC oluşma zamanı;
- `CorrelationId`, `CausationKind`, `CausationId`;
- minimum, sürümlü payload;
- persisted canonicalization/hash recipe kimliği ve yeniden üretilebilir payload/envelope
  integrity kanıtı;
- gerekiyorsa aggregate, sıfır veya daha fazla audit ve hashed-idempotency referansı.

Exact canonicalization/hash algoritması veya package bu readiness işinde seçilmez. Kabul
edilen implementation; Türkçe/Unicode, deterministic property order, omitted/null,
decimal/para/miktar hassasiyeti, invalid JSON ve cross-runtime golden fixture ile recipe'yi
sürümler. Bütünlük hash'i source authenticity veya permission sağlamaz.

Audit bağı tek `AuditEventId` değildir. Transaction, approval, producer, consumer ve
reconciliation zincirleri 1:N veya N:1 ilişki gerektirebilir. Exact tenant-safe immutable
referans modeli implementation review'unda seçilir; audit satırı event payload'ına
kopyalanmaz.

## 7. Server-authoritative causation

| Kaynak | Canonical cause |
|---|---|
| ERP-000E idempotent root command | `CommandExecution` + server persisted `CommandExecution.Id` |
| Parent message consumer'ının downstream message'ı | `Message` + parent `MessageId` |
| Scheduled/System veya authenticated Integration producer | `OPEN-025` içinde tanımlı server-generated durable execution cause |

HTTP `X-Causation-Id`, doğrulanmış observability metadata olabilir; immutable event
causation alanına kör biçimde kopyalanamaz. Canonical cause yoksa event creation
fail-closed olur.

## 8. Producer atomicity ve persistence seam

Producer contract, hangi başarılı sonucu event olarak yaydığını açıkça ilan eder. Generic
platform validation failure, no-change, conflict veya failure için event uydurmaz.

Atomic unit:

```text
contract-declared business mutation
+ required business audit
+ optional ERP-000E CommandExecution result
+ outbox message
= one physical SQL transaction
```

İki ayrı `DbContext.SaveChanges` tek başına yeterli değildir. Implementation review'unda
transaction participation seam, connection/transaction ownership, CompanyContext binding,
migration history ve exact dependency edge seçilir. Runtime; active transaction veya
enlistment uyuşmazlığında yazmayı reddeder. Her parçaya ayrı failure injection uygulanır.

Same-key replay/race ikinci business effect, audit, CommandExecution veya outbox üretmez.

## 9. Workload principal ve tenant yaşam döngüsü

Dispatcher/consumer principal:

- ERP tarafından provision edilmiş ve authenticate edilmiş workload kimliğidir;
- active/inactive/revoked yaşam döngüsüne sahiptir;
- dar permission/capability ve scope kullanır;
- kullanıcı, admin veya original actor claim'ini taklit etmez;
- credential'ı message/header/payload'dan almaz.

Trusted Organization Application contract, current workload'ın erişebildiği aktif
şirketleri verir. Her şirket ayrı immutable CompanyContext scope'unda işlenir. Normal yol
`IgnoreQueryFilters`, context-less query veya global tenant bypass kullanmaz.

Discovery bir authorization snapshot değildir. Principal, permission, Company durumu ve
source-to-tenant mapping:

1. claim öncesi;
2. transport'a yeni geri alınamaz send başlamadan hemen önce;
3. consumer business effect başlamadan hemen önce

yeniden doğrulanır. Identity/Organization unavailable, revoked principal veya inactive
Company yeni dış etkiyi fail-closed durdurur. Kayıt sessizce silinmez ve başka tenant'a
taşınmaz.

Inbound envelope'daki CompanyId trusted mapping değildir. Source authenticate edilip
source-to-tenant mapping doğrulanmadan tenant-scoped quarantine, inbox veya audit yazılmaz.
Yalnız payloadsız/redacted security telemetry mümkündür; ayrıca kabul edilmiş genel durable
security-event channel yoksa kalıcı güvenlik olayı varmış gibi gösterilmez. Mapping sonrası
quarantine current verified tenant context'iyle yazılabilir.

## 10. Dispatcher ve uncertain-send state

Dispatcher gereksinimleri:

- gerçek SQL atomik claim;
- owner + server fencing token + expected rowversion CAS;
- aktif lease'i ikinci worker'ın devralamaması;
- expired lease sonrası yeni token ile reclaim;
- stale owner renew/finalize/failure yazısının reddi;
- her send öncesi persisted integrity ve registration doğrulaması;
- retry sırasında immutable message/payload;
- pending/retry/dead-letter/uncertain kayıtlarının sessiz kaybolmaması.

Transport çağrısının kabul edilip edilmediği bilinmiyorsa `uncertain-send` sınırı oluşur.
Aşağıdakilerden biri contract ile kanıtlanmadan dispatcher yeni dış etkiyi kör retry etmez:

- hedefin MessageId tabanlı idempotency/dedup'ı;
- aynı MessageId için güvenilir status lookup/reconciliation;
- yetkili, auditli manual confirmation/re-drive kararı.

Exact state adları, timeouts, lease/retry/backoff değerleri, re-drive permissions ve
alarmlar `OPEN-024` kapanışında belirlenir.

Lease, available-at ve attempt zamanları client/payload clock'tan alınmaz. Trusted
server-side UTC kullanılır; DB içinde atomik karşılaştırmanın exact yöntemi ERP-000M ile
seçilir. Test harness injectable/controlled clock kullanır fakat production time source'u
değiştirmez.

Send öncesi revalidation ile dış çağrı arasında revoke yarışının fiziksel olarak tamamen
kapanacağı iddia edilmez. Revalidation sonrasında revoke edilen ve başlamış send sonucu
belirsizse uncertain/reconciliation davranışı uygulanır; yeni send ve kör retry yoktur.

## 11. Inbox ve consumer atomicity

Semantic dedup key en az `(CompanyId, ConsumerName, MessageId)`'dir. Aynı key aynı
source/type/version/payload/integrity ile tekrar geldiğinde ikinci effect no-op olur.
Farklı içerik duplicate sayılmaz ve fail-closed integrity incident olur.

Atomic unit:

```text
inbox claim/completion
+ contract-declared consumer mutation
+ required business audit
+ optional downstream outbox
= one physical SQL transaction
```

Handler kayıtlı source/producer/type/version/consumer allowlist, trusted tenant mapping,
current CompanyContext, permission/scope ve hedef business ownership kontrolünden sonra
çalışır. Handler transaction içinden harici irreversible çağrı yapamaz.

Source authentication/trusted tenant mapping başarısızken handler, inbox veya tenant
quarantine oluşturulmaz. Redacted security telemetry başka şirketin varlığını/payload'ını
açıklamaz.

## 12. Operatör, audit ve AI sınırı

- Otomatik lease/attempt/retry teknik telemetry'dir.
- Yetkili manual replay/reconciliation/purge business audit gerektirir.
- Operator başka şirket payload, count, error veya correlation bilgisini göremez.
- Manual replay processed inbox'ı resetlemez veya immutable payload'ı değiştirmez.
- AI messaging persistence'a veya transport'a doğrudan erişemez; yalnız gelecekteki dar,
  permission-controlled query/preview tool'u kullanabilir.
- Ham payload/secret/credential/access token/idempotency key loglanmaz.

## 13. Açık karar paketi

### OPEN-022 — Messaging retention

Saklama, partition, arşiv, legal hold ve yetkili purge kararıdır. Açıkken cleanup yoktur.

### OPEN-023 — Ordering

Ordering scope/key, gap ve geç/out-of-order davranışıdır. Açıkken sıra garantisi yoktur.

### OPEN-024 — Workload ve operations

Workload provisioning/authentication/revocation; lease/fencing; timeout; retry/backoff;
circuit breaker/backpressure; poison/dead-letter; uncertain-send; manual replay/re-drive
permission/approval/audit ve alarm politikasıdır. Değer veya rol uydurulmaz.

### OPEN-025 — Pilot event

Producer sahibi, durable root cause, source command/aggregate, event type, schema v1
payload/data class, emission/no-change/failure koşulları, consumer ve business/external
effect sözleşmesidir. Cari dâhil hiçbir modül otomatik pilot değildir.

## 14. Migration ve recovery contract

ERP-000M exact provider suitability, multi-DbContext migration owner/order/history ve
rollback/forward-fix girdisidir. ERP-000O; clean/upgrade/reapply script, constraint,
container, artifact ve CI kanıtını kapı olarak uygular.

Messaging migration kabulü gelecekte en az:

- clean DB + mevcut DB upgrade + repeat script;
- tenant/unique/integrity/immutable/CAS DB guard;
- same physical transaction failure injection;
- pending/leased/uncertain/retry/dead-letter/processed kanıtını düşürmeyen forward-fix;
- old/new binary-schema-event compatibility

kanıtı ister. Backup/restore high-water ve dış etki reconciliation ERP-000N kapsamıdır.

## 15. Task-local acceptance sahipliği

`docs/acceptance/ERP-000F_ACCEPTANCE.md` içindeki `ERP-000F-AC-001..018` bu task'ın
readiness senaryolarıdır. Yeni kanonik `AT-*` değildir; merkezi
`ACCEPTANCE_MATRIX.csv` değiştirilmez. PR #2'nin `AT-071..078` ve diğer blocker'ları
aynen korunur.

Senaryolar bugün `DRAFT / UNIMPLEMENTED`dır. Feature/test substrate, migration veya
synthetic producer kodu open kararlar kapanmadan yazılmaz. SQL race, constraint,
transaction ve migration için EF InMemory kanıt sayılmaz.

## 16. Definition of Ready

ERP-000F implementation ancak:

1. A/B/C/D/E/M/O `done` ve main'de;
2. fiziksel ADR-006 exact-SHA review ile kabul;
3. OPEN-022..025 karar kaydıyla kapalı;
4. transaction/migration/dependency edge'i exact;
5. pilot event ve workload contract'ı yazılı;
6. AC-001..018 expected davranışı kararsız değer içermeden onaylı

ise başlayabilir.

## 17. Definition of Done

- AC-001..018 gereken unit/contract/gerçek SQL/security/migration/observability
  katmanlarında yeşildir.
- P0/P1 açık bulgu, skip/ignored kritik test veya stale decision yoktur.
- CompanyId, authorization, revocation, causation ve no-blind-retry sınırı kanıtlanır.
- Migration/forward-fix ve duplicate/uncertain external-effect runbook kanıtlıdır.
- Package/vulnerability/architecture/build/container ve fresh exact-head CI yeşildir.
- Kritik Draft PR exact-SHA bağımsız review ve delege/insan gate olmadan merge edilmez.
- Production activation ayrıca OPEN-016, recovery/observability ve production güvenlik
  kapılarını bekler.
