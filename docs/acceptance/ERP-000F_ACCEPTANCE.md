# ERP-000F — Task-local Kabul Planı

- Tarih: 2026-08-30
- Kimlik aralığı: `ERP-000F-AC-001..018`
- Durum: **DRAFT / UNIMPLEMENTED**
- Implementation kapısı: `ADR-006`, `OPEN-022`, `OPEN-023`, `OPEN-024`, `OPEN-025`
- Production activation: **NO-GO**

## 1. Kimlik ve sahiplik kuralı

Bu dosyadaki kimlikler task-local'dır; yeni kanonik `AT-*` satırı değildir.
`docs/acceptance/ACCEPTANCE_MATRIX.csv` ve PR #2 değiştirilmez. Gelecekte merkezi matrise
eşleme gerekirse ayrı sahiplik/reconciliation review'unda yapılır.

Bütün senaryolar bugün taslaktır. Test sınıfı, gerçek SQL Server CI run'ı ve kalıcı kanıt
olmadan `Otomatik`, `Geçti` veya `Kabul` durumuna yükseltilemez. Open kararlar kapanmadan
feature, test fixture/substrate, migration, worker veya synthetic producer kodu yazılmaz.

## 2. Ortak test düzeni

Implementation başladığında fixture en az şunları sağlar:

- A ve B şirketi; aktif/pasif durum geçişleri ve ayrı CompanyContext.
- Aktif, revoked ve permission'ı kaldırılmış workload principal örnekleri.
- İki bağımsız dispatcher worker ve en az iki kayıtlı consumer adı.
- Yalnız test host'unda kayıtlı synthetic producer/consumer contract'ı; production event
  veya Cari pilot seçmez.
- Kontrollü clock ve concurrency barrier.
- Business, audit, CommandExecution, outbox, inbox ve downstream outbox noktalarında ayrı
  failure injection.
- Transport kabulü, rejection, timeout ve sonucu belirsiz bırakma yeteneği olan fake.
- Başlangıç/bitiş exact satır sayısı, transport çağrı sayısı ve negative-effect assertion.
- Structured log/metric/trace'te payload/secret/credential/ham idempotency key sızıntısı
  kontrolü.

Production state transition/lease zamanı trusted server-side UTC'dir; provider-specific
DB karşılaştırması ERP-000M ile seçilir. Testler yalnız injectable/controlled clock ile
ilerletilir ve client/payload zamanı authority sayılmaz.

SQL transaction, unique/foreign key, rowversion, trigger/CAS, race ve migration kanıtında
EF InMemory kabul edilmez. Fixture'lar benzersiz CompanyId/MessageId namespace kullanır;
test cleanup production retention davranışı sayılmaz.

## ERP-000F-AC-001 — Producer transaction atomikliği

**Given:** Contract-declared business mutation, gerekli audit, varsa CommandExecution ve
outbox vardır.

**When:** Her kalıcı noktada ayrı ayrı SQL failure enjekte edilir.

**Then:** Bütün kayıtlar aynı fiziksel SQL transaction'da birlikte commit veya rollback
olur; commit öncesi transport çağrısı sıfırdır. Ayrı `DbContext.SaveChanges` kullanılıyorsa
aynı connection/transaction enlistment kanıtı ayrıca doğrulanır.

**Kanıt:** Gerçek SQL Server transaction/failure-injection.

## ERP-000F-AC-002 — Producer idempotency ve tek outbox

**Given:** Aynı CompanyId, command, actor, key ve canonical payload ile iki paralel istek.

**When:** ERP-000E unique/concurrency sınırında yarışırlar.

**Then:** Tek business effect/audit/CommandExecution/outbox oluşur; kaybeden kalıcı sonucu
replay eder. Aynı key farklı payload/actor için fail-closed conflict olur ve eski mesaj
bilgisi sızmaz.

**Kanıt:** Gerçek SQL parallel barrier + exact row count.

## ERP-000F-AC-003 — Tenant write/query guard

**Given:** A ve B şirket mesajları ile yalnız A kapsamlı context/principal.

**When:** ID, query, count, error, route veya direct writer manipülasyonuyla B kaydı
okunmaya/yazılmaya çalışılır.

**Then:** B payload, varlık, count, error ve correlation bilgisi sızmaz; CompanyId boş,
Guid.Empty veya active context ile uyuşmazsa fail-closed olur.

**Kanıt:** HTTP/Application + gerçek SQL negative security tests.

## ERP-000F-AC-004 — Envelope version, integrity ve immutability

**Given:** Onaylanmış canonicalization/hash recipe ile persisted envelope.

**When:** Yeni context/runtime kaydı okur ve recipe'yi yeniden çalıştırır.

**Then:** Payload/envelope integrity birebir tekrar üretilir. Türkçe/Unicode, property
sırası, omitted/null, decimal hassasiyeti ve invalid JSON golden örnekleri aynı contract
sonucunu verir. Immutable identity/payload/hash alanlarının EF ve direct SQL update/delete
girişimi reddedilir.

**Kanıt:** Contract golden + gerçek SQL persisted-row recompute/DB guard.

## ERP-000F-AC-005 — Server-authoritative causation

**Given 1:** Event üreten ERP-000E root command.

**Then 1:** `CausationKind=CommandExecution` ve cause server persisted
`CommandExecution.Id`dir.

**Given 2:** Parent message'ın ürettiği downstream message.

**Then 2:** `CausationKind=Message` ve cause parent `MessageId`dir.

Client `X-Causation-Id` değiştirilse veya verilmezse canonical event cause değişmez;
authorization/CompanyContext'e etki etmez. Scheduled/System cause, OPEN-025 sözleşmesi
olmadan kabul edilmez.

Aynı transaction birden fazla audit ve mesaj ürettiğinde ilişki tek `AuditEventId`
varsaymaz; 1:N/N:1 tenant-safe immutable referanslar persisted zincirden eksiksiz yeniden
kurulur ve audit içeriği payload'a kopyalanmaz.

**Kanıt:** HTTP/Application contract + persisted chain assertion.

## ERP-000F-AC-006 — Workload principal provision ve revoke

**Given:** Aktif, inactive, revoked ve permission'ı kaldırılmış ayrı workload principal
durumları.

**When:** Dispatcher/consumer authenticate ve authorize olur.

**Then:** Yalnız aktif ve current dar permission/scope'lu principal ilerler. ActorType
etiketi, user/header claim'i veya admin impersonation yetki sağlamaz. Identity kaynağı
unavailable ise yeni dış etki yoktur.

**Kanıt:** Identity/Application security contract + audit/telemetry assertion.

## ERP-000F-AC-007 — Trusted company discovery ve revocation yarışı

**Given:** Workload discovery anında A şirketine yetkili; claim/send/effect öncesinde
principal, permission veya Company inactive/revoked olur.

**When:** Worker sıradaki güven sınırına geçer.

**Then:** Current state yeniden doğrulanır; yeni claim/send/business effect başlamaz.
Context-less, header-supplied CompanyId, user AllowedCompanyIds veya IgnoreQueryFilters
yolu reddedilir. Organization unavailable ise pending kayıt korunur.

Revalidation sonrasında principal/Company revoke olup send başlamışsa çağrının geri
alındığı varsayılmaz. Sonuç kesin değilse AC-010 uncertain/reconciliation sınırına girer;
revoked principal ile yeni send yoktur.

**Kanıt:** Organization Application contract + iki aşamalı race + architecture guard.

## ERP-000F-AC-008 — Çift dispatcher claim ve fencing

**Given:** Tek claim edilebilir outbox ve iki worker.

**When:** İkisi gerçek SQL barrier'da eşzamanlı claim eder.

**Then:** Yalnız biri current owner/fencing token olur. Aktif lease devralınamaz; expired
lease sonrası yeni token oluşur. Eski owner renew/finalize/failure yazamaz.

**Kanıt:** Gerçek SQL concurrency + CAS/rowversion + transport call count.
**Açık expected:** Süre/değer `OPEN-024` kapanınca eklenir.

## ERP-000F-AC-009 — Send sonrası crash ve duplicate delivery

**Given:** Transport mesajı kabul eder; worker dispatched state commit etmeden çöker.

**When:** Mesaj güvenli recovery yoluyla yeniden teslim edilir.

**Then:** Transport duplicate görebilir; ERP-controlled consumer aynı MessageId için tek
business effect/audit/downstream outbox üretir. Exactly-once transport iddiası yapılmaz.

**Kanıt:** Crash injection + gerçek SQL inbox/effect counts.

## ERP-000F-AC-010 — Uncertain-send ve kör retry yasağı

**Given:** Transport timeout/connection loss nedeniyle mesajın hedefte kabul edilip
edilmediği bilinmez.

**When:** Worker aynı kaydı tekrar ele alır.

**Then:** Hedef MessageId idempotency/dedup, güvenilir status reconciliation veya yetkili
manual confirmation contract'ından biri kanıtlanmadan yeni dış etki çağrısı yapılmaz.
Kayıt silinmez; güvenli uncertain/reconciliation kanıtı ve telemetry taşır.

**Kanıt:** Transport fake + send count = 1 until accepted reconciliation path.
**Açık expected:** Exact state/permission `OPEN-024/025` kapanınca eklenir.

## ERP-000F-AC-011 — Concurrent inbox dedup

**Given:** Aynı `(CompanyId, ConsumerName, MessageId)` ve aynı integrity ile iki paralel
teslim.

**When:** Consumer gerçek SQL unique/transaction sınırında yarışır.

**Then:** Tek contract-declared mutation/audit/downstream outbox oluşur; diğeri duplicate
no-op'tur. Aynı key farklı source/type/version/hash ise duplicate değil fail-closed
integrity incident olur.

**Kanıt:** Gerçek SQL unique race + exact effect count.

## ERP-000F-AC-012 — Consumer transaction atomikliği

**Given:** Kayıtlı source/type/version ve doğrulanmış tenant için ilk teslim.

**When:** Inbox, business mutation, audit ve downstream outbox noktalarında ayrı failure
enjekte edilir.

**Then:** Tamamı aynı fiziksel SQL transaction'da commit/rollback olur. Transaction içinde
harici irreversible fake call sayısı sıfırdır; failure sonrası yeniden teslim mümkündür.

**Kanıt:** Gerçek SQL failure-injection + transport call count.

## ERP-000F-AC-013 — Source/type/version/payload fail-closed

**Given:** Unregistered source/producer/consumer, unsupported event/envelope version,
invalid payload veya integrity mismatch varyantları.

**When:** Outbound pre-send veya inbound pre-handler doğrulaması çalışır.

**Then:** Transport/business handler çağrısı yapılmaz; successful inbox/outbox state yoktur;
payload/hassas veri error text'te sızmadan güvenli incident/telemetry oluşur.

Inbound source authentication ve trusted CompanyId mapping kurulmamışsa envelope CompanyId
ile tenant quarantine/inbox/audit yazılmaz. Yalnız payloadsız/redacted telemetry üretilir;
genel durable security-event kanalı ayrıca kabul edilmediyse kalıcı olay varmış gibi kanıt
sunulmaz. Mapping başarılı olduktan sonra verified tenant quarantine değerlendirilebilir.

**Kanıt:** Contract negative set + gerçek SQL + zero-call assertions.

## ERP-000F-AC-014 — Retry, poison, dead-letter ve backpressure

**Given:** `OPEN-024` ile kabul edilmiş error sınıfları ve operasyon politikası.

**When:** Retryable, permanent, pressure ve recovery senaryoları oluşur.

**Then:** Yalnız kabul edilmiş politika uygulanır; tight loop, sessiz silme, payload değişimi
ve yetkisiz replay olmaz. Pending mesaj temel ERP transaction'ını transport'a bağlamaz.

**Kanıt:** Policy unit + gerçek SQL transitions + metric/alarm fixture.
**Açık expected:** Sayı/süre/eşik bugün yazılmaz.

## ERP-000F-AC-015 — Ordering contract

**Given:** `OPEN-023` ile kabul edilmiş ordering scope/key ve gap/out-of-order davranışı.

**When:** Gecikme, duplicate ve parallel delivery uygulanır.

**Then:** Yalnız seçilmiş kapsamın garantisi kanıtlanır; diğer mesajlar için global sıra
iddiası yoktur. Gap/geç mesaj sessiz yanlış business effect üretmez.

**Kanıt:** Deterministic scheduler + gerçek SQL concurrency.
**Açık expected:** OPEN-023 kapanmadan sıra sonucu uydurulmaz.

## ERP-000F-AC-016 — Retention, legal hold ve purge

**Given:** `OPEN-022` ile kabul edilmiş retention/partition/archive/legal-hold/purge
politikası ve farklı operational state'ler.

**When:** Yetkili cleanup/purge işi sınır tarihlerinde çalışır.

**Then:** Pending, active, uncertain, retry, işlenmemiş veya legal-hold kanıtı silinmez;
yalnız uygun terminal kayıtlar actor/reason/company audit'iyle etkilenir. Karar kapanmadan
production cleanup disabled/yoktur.

**Kanıt:** Gerçek SQL boundary + authorization/audit + disabled configuration test.

## ERP-000F-AC-017 — Migration, upgrade ve forward-fix

**Given:** Temiz DB ve A-E platform migration'lı mevcut DB.

**When:** ERP-000M/O ile kabul edilmiş messaging migration/script uygulanır ve yeniden
çalıştırılır.

**Then:** Migration history/order, tenant/unique/integrity/immutable/CAS guard'ları aynı ve
idempotenttir. Forward-fix/compatibility provası pending/leased/uncertain/retry/
dead-letter/processed kanıtını düşürmez; production rollback tablo drop değildir.

**Kanıt:** Gerçek SQL clean+upgrade+reapply + metadata/direct SQL + artifact manifest.

## ERP-000F-AC-018 — Observability, redaction ve pilot contract

**Given:** `OPEN-025` ile kabul edilmiş pilot ve `OPEN-024` ile kabul edilmiş operasyon
politikası altında success/retry/duplicate/revoke/integrity/uncertain/dead-letter olayları.

**When:** Producer, dispatcher ve consumer zinciri çalışır.

**Then:** Correlation/canonical causation, queue depth/age, dispatch/consumer latency,
attempt, lease reclaim, duplicate, integrity, uncertain ve dead-letter kanıtları tenant-safe
log/metric/trace ile tanılanır. Secret, credential, access token, ham idempotency key veya
izin verilmeyen payload görünmez. Pilot event yalnız onaylı owner/schema/data-class/
emission/consumer/effect contract'ını çalıştırır.

**Kanıt:** Structured telemetry/redaction contract + real chain evidence.

## 3. Kanıt matrisi

| Senaryo | Contract/Unit | Gerçek SQL | Race/Crash | Security/Audit | Migration/Telemetry |
|---|---:|---:|---:|---:|---:|
| AC-001 | — | Zorunlu | Failure injection | Audit | — |
| AC-002 | Idempotency | Zorunlu | Zorunlu | Actor/payload | — |
| AC-003 | — | Zorunlu | — | Zorunlu | Safe error |
| AC-004 | Golden | Zorunlu | — | Integrity | — |
| AC-005 | Zorunlu | Zorunlu | — | Zorunlu | Trace |
| AC-006 | Identity | Gerekirse | Revoke | Zorunlu | Telemetry |
| AC-007 | Organization | Zorunlu | Zorunlu | Zorunlu | Architecture |
| AC-008 | Claim/CAS | Zorunlu | Zorunlu | CompanyId | Lease metric |
| AC-009 | Delivery | Zorunlu | Zorunlu | Effect count | Duplicate metric |
| AC-010 | Adapter | Gerekirse | Timeout/crash | Manual gate | Uncertain metric |
| AC-011 | Dedup | Zorunlu | Zorunlu | Integrity | Duplicate metric |
| AC-012 | Handler | Zorunlu | Failure injection | Audit | — |
| AC-013 | Negative | Zorunlu | — | Fail-closed | Incident metric |
| AC-014 | Policy | Zorunlu | Scheduler | Replay auth | Alarm |
| AC-015 | Ordering | Zorunlu | Zorunlu | — | Gap metric |
| AC-016 | Policy | Zorunlu | Boundary | Auth+audit | Cleanup disabled |
| AC-017 | Contract | Zorunlu | — | Kanıt korunur | Zorunlu |
| AC-018 | Redaction | Zorunlu | Chain | Zorunlu | Zorunlu |

## 4. Kabul çıkış kapısı

- Her senaryonun kanıtı exact test sınıfı ve fresh CI run kimliği taşır.
- Race/constraint/transaction/migration kanıtı EF InMemory değildir.
- Failure testinin hedef failure point'e ulaştığı ayrıca doğrulanır.
- P0/P1, stale OPEN/ADR, skipped kritik test veya eski-base CI varken F `done` olmaz.
- Exact-head bağımsız security/architecture review ve kritik merge kapısı geçmeden Ready/
  merge yapılmaz.
- Production activation; `OPEN-016`, identity, recovery, observability ve production
  approval kapıları kapanmadan yapılmaz.
