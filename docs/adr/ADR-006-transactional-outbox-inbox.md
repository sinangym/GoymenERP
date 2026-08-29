# ADR-006 — Transactional Outbox/Inbox ve Güvenilir Mesaj Teslimi

- Tarih: 2026-08-30
- Görev: `ERP-000F`
- ADR kayıt listesindeki yüksek seviye yön: **Kabul**
- Bu fiziksel ayrıntı sözleşmesinin durumu: **Öneri / exact-SHA review gerekli**
- Implementation: **NO-GO / BLOCKED**
- Production activation: **NO-GO**
- Açık kapılar: `OPEN-022`, `OPEN-023`, `OPEN-024`, `OPEN-025`
- İlgili mevcut kararlar: `DEC-GOV-002`, `DEC-TEC-001`, `DEC-TEC-008`,
  `DEC-TEC-009`, `DEC-TST-002`, `ADR-002`, `ADR-004`, `ADR-009`, `ADR-011`

## 1. Durum ayrımı

`docs/architecture/ADR_REGISTER.md`, ADR-006 için transactional outbox/inbox yönünü
`Kabul` olarak kaydeder. Bu dosya o yüksek seviye kararı düşürmez veya yeniden açmaz.
Kabul edilmiş yön şudur:

1. business transaction ile dış etki niyeti aynı yerel transaction'da kalıcılaşır;
2. teslim semantiği at-least-once'dır;
3. consumer inbox/dedup ile aynı semantic etkiyi ikinci kez üretmez;
4. transport, event schema ve operasyon politikası ayrı sözleşmelerle sürümlenir.

Bu fiziksel ADR; uygulanabilir güven, tenant, transaction, causation, dispatcher ve
consumer ayrıntılarını önerir. Ayrıntıların exact-SHA review'u ve dört açık karar
kapanmadan `ADR-006` manifest blocker'ı kapanmış sayılmaz. Yalnız ADR kayıt listesindeki
`Kabul` kelimesi feature kodu, migration, worker, test substrate veya pilot event yetkisi
vermez.

## 2. Bağlam

Business kaydı commit edildikten sonra doğrudan dış sisteme mesaj gönderilirse iş kaydı
kalıcı olduğu hâlde mesaj kaybolabilir. Mesaj transaction commit olmadan gönderilirse bu
kez dış etki oluşup business transaction rollback olabilir. Ağ timeout'u veya worker
çökmesi, dış hedefin mesajı alıp almadığının bilinmediği bir sonuç da üretebilir.

Göymen ERP; CompanyId izolasyonu, append-only audit, server-side correlation ve kalıcı
idempotency/concurrency temeline sahiptir. Ancak bugün:

- workload/service principal için gerçek ve iptal edilebilir bir kimlik sözleşmesi yoktur;
- kullanıcıya ait şirket listesini worker tenant keşfi gibi kullanmak güvenli değildir;
- HTTP `X-Causation-Id` değeri doğrulansa da integration event'in canonical nedeni
  sayılamaz;
- iki ayrı `DbContext.SaveChanges` çağrısı aynı fiziksel transaction kanıtı değildir;
- transport, pilot event, ordering, retention ve operasyon değerleri seçilmemiştir.

Bu ADR eksikleri gizlemez ve kaynaklarda olmayan provider, paket, topoloji, süre, sayı,
eşik, retry, retention, event veya pilot seçimi yapmaz.

## 3. Değerlendirilen seçenekler

### 3.1 Commit sonrasında doğrudan publish

Commit ile publish arasında kayıp penceresi bıraktığı için reddedilir.

### 3.2 Commit öncesinde publish

Rollback sonrasında hayalet dış etki üretebildiği için reddedilir.

### 3.3 Dağıtık transaction veya exactly-once iddiası

Harici hedeflerin aynı transaction'a katıldığını ve transport tesliminin business
etkisini tekilleştirdiğini varsaydığı için reddedilir.

### 3.4 Transactional outbox + at-least-once + inbox/dedup

Kabul edilmiş yüksek seviye yöndür. Duplicate teslim hata saklama yöntemi değil,
consumer contract ile güvenli ve test edilen normal bir durumdur.

## 4. Sahiplik ve modül sınırı

- Producer domain/Application contract; event adı, payload, veri sınıfı, oluşma ve
  oluşmama koşulları ile schema uyumluluğunun sahibidir.
- Platform messaging sınırı; ortak envelope, outbox/inbox semantic contract,
  dispatcher/consumer portları ve operasyonel kanıtın sahibidir.
- Consumer domain/Application contract; kabul edilen event/schema sürümü ve oluşturulan
  business etkisinin sahibidir.
- Audit satırı integration event değildir. Gerekli event producer tarafından açıkça
  tanımlanır.
- Bir modül başka modül tablosuna doğrudan yazmaz.
- Yeni bir `*.Infrastructure -> Messaging.Infrastructure` kenarı bu ADR ile otomatik
  izinli olmaz. Exact kenar, gerekçe, migration ownership, makine-okunur allowlist ve
  architecture guard aynı review değişikliğinde bulunmalıdır.
- Cari veya ChatGPT-owned başka bir modül `OPEN-025` ile açıkça seçilmeden pilot producer
  sayılmaz.

## 5. Mesaj kimliği, envelope ve bütünlük

Ortak envelope en az aşağıdaki semantic alanları taşır:

- server-generated değişmez `MessageId`;
- ayrı `EnvelopeSchemaVersion` ve `EventSchemaVersion`;
- kayıtlı `SourceSystem`, `ProducerModule` ve `EventType`;
- zorunlu, boş olmayan `CompanyId`;
- server-controlled UTC oluşma zamanı;
- `CorrelationId`;
- server-authoritative `CausationKind` ve `CausationId`;
- producer contract'ının minimum, sürümlü payload'ı;
- persisted payload ve immutable envelope üzerinden yeniden hesaplanabilir,
  sürümlü canonicalization/hash recipe kimliği ve bütünlük özeti;
- gerekiyorsa sıfır veya daha fazla kaynak audit referansı ve yalnız hash biçimindeki
  idempotency referansı.

Canonicalization ve hash davranışı deterministik, sürümlü ve persisted-row üzerinden
yeniden üretilebilir olmalıdır. Exact algoritma veya paket bu ADR taslağında seçilmez.
Seçim; Türkçe/Unicode, property sırası, omitted/null, decimal/para/miktar hassasiyeti,
geçersiz JSON ve cross-runtime golden fixture ile review edilir. Hash authenticity veya
authorization sağlamaz; kayıtlı producer/source ve permission doğrulaması ayrıca yapılır.

Message identity, source, company, type/version, payload ve hash alanları insert sonrasında
normal writer veya doğrudan SQL ile değiştirilemez. Lease, attempt ve completion gibi
operasyon alanları yalnız dar compare-and-swap sözleşmesiyle değişir.

Audit ilişkisi tek `AuditEventId` varsaymaz. Bir business transaction birden fazla audit
olayı ve birden fazla mesaj üretebilir; bir mesaj da producer, approval, consumer ve
reconciliation zincirindeki birden fazla audit olayına bağlanabilir. Exact ilişki modeli
ayrı, tenant-safe ve immutable referans sözleşmesiyle seçilir; audit satırı payload'a
kopyalanmaz.

## 6. Canonical causation otoritesi

İstemci `X-Causation-Id` değeri HTTP gözlemlenebilirliği için doğrulanmış trace metadata
olabilir; event kimliği, yetki veya canonical mesaj nedeni değildir.

- ERP-000E ile bağlı kök idempotent command event üretiyorsa:
  `CausationKind = CommandExecution` ve `CausationId`, server tarafından kalıcılaştırılmış
  `CommandExecution.Id` olur.
- Bir mesaj consumer tarafından yeni downstream message üretiyorsa:
  `CausationKind = Message` ve `CausationId`, parent `MessageId` olur.
- Scheduled `System` veya authenticated `Integration` producer için canonical cause türü
  ve server-generated kalıcı execution kimliği pilot contract'ta açıkça tanımlanır;
  user/header değeri varsayılmaz.

Canonical cause bulunamıyorsa event yazımı fail-closed olur. Correlation zinciri korunur;
retry yeni business correlation veya yeni semantic MessageId üretmez.

## 7. Producer transaction sınırı

Producer contract'ın ilan ettiği business mutation, gerekli audit, varsa ERP-000E
command result ve outbox insert'i aynı fiziksel SQL transaction'ında commit veya rollback
olur. Transaction commit olmadan transport çağrısı yapılmaz.

Aynı connection ve aynı `DbTransaction` içine güvenilir biçimde enlist edilmemiş iki ayrı
`DbContext.SaveChanges` çağrısı atomiklik kanıtı değildir. Implementation başlamadan:

1. transaction participation seam'i ve sahibi ADR-006/M sözleşmesinde seçilir;
2. runtime; aktif transaction, connection, CompanyContext ve enlistment uyuşmazlığını
   fail-closed reddeder;
3. outbox insert failure injection, business/audit/command result değişikliklerinin
   tamamını rollback ettiğini gerçek SQL Server'da kanıtlar;
4. migration history, schema ownership ve uygulama sırası ERP-000M ile;
5. migration/contract/test quality gate'i ERP-000O ile kilitlenir.

Producer command replay'i veya aynı-key yarışı ikinci event/outbox üretemez. Validation,
authorization, conflict, no-change ve failure sonuçlarının event üretip üretmediği generic
messaging katmanı tarafından tahmin edilmez; `OPEN-025` producer contract'ında açıkça yazılır.

## 8. Workload kimliği ve tenant keşfi

`ActorType=System` veya `ActorType=Integration` etiketi tek başına kimlik/yetki değildir.
Dispatcher ve consumer:

- yerel ERP yetki kaynağında provision edilmiş, aktif/pasif/iptal edilebilir workload
  principal ile çalışır;
- kullanıcı claim'i, kullanıcı oturumu veya geniş admin rolünü taklit etmez;
- yalnız kendi dar messaging/consumer capability ve permission kapsamına sahiptir;
- credential kaynağını payload/header'dan almaz;
- principal veya permission kaynağı erişilemezse fail-closed olur.

Company discovery; kullanıcı `AllowedCompanyIds` listesini kopyalamaz. Trusted Organization
Application contract, workload principal'ın o anda erişebildiği aktif şirketleri verir.
Her şirket için ayrı immutable CompanyContext/DI scope kurulur; context'siz veya
header-supplied CompanyId ile claim yoktur. `IgnoreQueryFilters` ve global tenant bypass
normal scheduler yolu değildir.

Şirket ve principal durumu discovery'den sonra değişebilir. Bu nedenle yeni claim'den ve
her geri alınamaz dış send/consumer business etkisinden hemen önce current principal,
permission, Company ve source-to-tenant mapping yeniden doğrulanır. Revoked/inactive veya
identity/Organization unavailable ise yeni dış etki yoktur; kayıt kaybolmadan görünür
pending/quarantine/reconciliation durumunda kalır. Exact credential/provisioning,
permission code, scope ve revoke işletimi `OPEN-024` kapanmadan uydurulmaz.

Inbound kaynağın envelope içindeki CompanyId beyanı trusted tenant mapping değildir.
Source authentication ve source-to-tenant mapping kurulmadan tenant-scoped quarantine,
tenant audit veya başka bir CompanyId kapsamlı persistence yazılmaz. Bu aşamada yalnız
payloadsız/redacted security telemetry üretilebilir. Repository'de genel kalıcı security
event kanalı ayrıca kabul edilmediyse varmış gibi durable olay iddia edilmez. Trusted
mapping sonrasında fail-closed tenant quarantine/audit current verified CompanyId ile
yazılabilir; beyan edilen CompanyId yine authority olmaz.

## 9. Dispatcher, lease ve belirsiz gönderim

- Delivery semantiği at-least-once'dır; global exactly-once veya sıra iddiası yoktur.
- Claim gerçek SQL Server'da atomiktir. Aynı mesajın aynı anda tek geçerli owner/fencing
  token'ı olabilir.
- Renew/finalize/retry/terminal yazmaları owner + fencing token + expected rowversion ile
  compare-and-swap yapar. Eski owner yeni state'i ilerletemez veya geriletemez.
- Lease expiry durmuş eski process'in başlamış dış çağrısını geri alamaz.
- Send kabul edilmiş fakat ack/state commit olmamışsa sonuç `uncertain`dır.
- `uncertain` sonuç kör biçimde retry edilmez. Adapter hedef idempotency/dedup kanıtı,
  aynı MessageId ile güvenli status lookup veya yetkili reconciliation/manual confirmation
  contract'ından biri olmadan yeni dış etki başlatılmaz.
- Payload/MessageId retry sırasında değişmez; bilinçli yeni business etkisi yeni yetkili
  command ve yeni MessageId ister.
- Lease, timeout, retry, backoff, jitter, batch, parallelism, circuit breaker,
  backpressure, max attempt, poison/dead-letter, re-drive ve alarm değerleri
  `OPEN-024` kapanmadan seçilmez.

Lease/availability/attempt zamanları payload veya client clock'tan alınmaz. State geçişi ve
lease karşılaştırması için trusted server-side UTC kullanılır; DB transaction içinde
atomik zaman karşılaştırması gerekiyorsa provider-specific exact yöntem ERP-000M review'unda
seçilir. Testler production clock'u değiştirmeden injectable/controlled clock kullanır.

Principal/Company revalidation ile dış send arasında sıfır-zamanlı atomiklik iddia edilmez.
Revalidation sonrasında revoke olup başlamış bir send geri alınamaz. Bu yarışın sonucu ack
ile kesinleşmemişse `uncertain` sınırına girer; revoked principal ile yeni send başlatılmaz,
kör retry yapılmaz ve kabul edilmiş reconciliation yolu beklenir.

Gerçek transport adapter'ı ayrıca `OPEN-016` ve ilgili erişim/güvenlik sözleşmesi
kapanmadan production'a açılmaz.

## 10. Consumer ve inbox transaction sınırı

- Inbox dedup anahtarı en az `(CompanyId, ConsumerName, MessageId)` semantic kapsamındadır.
- `ConsumerName` kayıtlı ve değişmez machine code'dur; header veya kullanıcı metni değildir.
- Aynı anahtar farklı source/type/version/payload/envelope hash ile gelirse duplicate değil,
  fail-closed integrity violation'dır.
- Inbox claim/completion, consumer business mutation, gerekli audit ve downstream outbox
  aynı fiziksel SQL transaction'ında commit veya rollback olur.
- Consumer transaction içinden geri alınamaz dış çağrı yapılmaz; böyle etkiler downstream
  outbox'a yazılır.
- Concurrent duplicate delivery yalnız bir contract-declared business etki üretir.
- Kaynak doğrulaması, source-to-tenant mapping, CompanyId/ABAC, hedef business sahipliği,
  event/schema allowlist ve workload permission handler çalışmadan önce yeniden doğrulanır.
- Otomatik worker audit'i original kullanıcıyı taklit etmez; original cause referans olarak
  korunur.

Doğrulanmamış inbound source için handler, inbox veya tenant quarantine yazılmaz. Ancak
source authentication ve trusted CompanyId mapping başarılı olduktan sonra verified tenant
context'i içinde quarantine/inbox değerlendirmesi yapılabilir.

## 11. Audit, idempotency, AI ve veri sınırı

- Business mutation ve yetkili manuel replay/reconciliation immutable audit üretir.
- Lease/attempt/retry normalde teknik telemetry'dir; her deneme yeni business audit değildir.
- ERP-000E command idempotency ile inbox dedup farklı sahiplik ve saklama sınırlarıdır.
- Manual re-drive processed inbox'ı silmez/resetlemez ve payload'ı yerinde değiştirmez.
- AI outbox/inbox tablosuna doğrudan yazamaz; publish, ack, retry, purge veya replay
  yetkisi kazanamaz.
- Payload minimum veri taşır; secret, token, credential, ham idempotency key veya gereksiz
  şirket/kişi verisi mesaj, dead-letter, log, metric ve trace'e konmaz.

## 12. Açık kararlar ve güvenli varsayılan

| ID | Kapanacak alan | Açıkken fail-closed davranış |
|---|---|---|
| `OPEN-022` | Retention/partition/archive/legal hold/purge | Cleanup/purge yoktur. |
| `OPEN-023` | Ordering scope/key/gap/out-of-order | Sıra garantisi ve ordering-dependent consumer yoktur. |
| `OPEN-024` | Workload identity ve dispatcher/consumer operasyon politikası | Değer/rol/eşik/otomatik replay yok; dış etki başlamaz. |
| `OPEN-025` | İlk producer/cause/event v1/data class/consumer/effect | Production event veya pilot seçimi yoktur. |

`OPEN-016` gerçek transport adapter'ının ayrıca production kapısıdır. `OPEN-021` yalnız
ERP-000E command execution retention'ıdır; messaging kayıtlarını kapsamaz.

## 13. Migration, rollback ve recovery

Exact schema, tablo, index, trigger, migration history ve provider davranışı ERP-000M
kararı olmadan kilitlenmez. Implementation:

- temiz DB ve mevcut DB upgrade yolunu;
- idempotent migration scriptini ve tekrar uygulamayı;
- tenant/unique/immutable/CAS constraint'lerini;
- migration ordering ve compatibility manifestini;
- pending, leased, uncertain, retry, dead-letter ve processed kanıtı kaybetmeyen
  forward-fix stratejisini

gerçek SQL Server'da doğrular. Production mesajı oluştuktan sonra tablo drop normal binary
rollback değildir. Backup/restore ve dış etki high-water reconciliation ERP-000N'nin
ayrı kabul kapısıdır.

## 14. Sonuçlar

Olumlu sonuç; business commit ile publish niyeti arasındaki kayıp penceresinin kapanması,
duplicate teslimin görünür/testli olması ve CompanyId/correlation/causation/audit zincirinin
kurulmasıdır.

Maliyet; kalıcı kuyruk büyümesi, migration ve worker işletimi, duplicate/uncertain dış etki
reconciliation'ı, contract versioning ve gözlemlenebilirlik gereksinimidir. Bu maliyetler
gizlenmez veya magic number ile kapatılmaz.

## 15. Kodlama ve kabul kapısı

Aşağıdakiler tamamlanmadan feature code, fixture/substrate code, migration, worker,
adapter, endpoint veya pilot event geliştirilmez:

1. Bu fiziksel ADR exact-SHA mimari/security review ile kabul edilir.
2. ERP-000A/B/C/D/E/M/O `done` ve main'dedir.
3. `OPEN-022`, `OPEN-023`, `OPEN-024`, `OPEN-025` karar sahibi kaydıyla kapanır.
4. Task-local `ERP-000F-AC-001..018` planı onaylanır.
5. Gerekli cross-module edge ve migration ownership exact allowlist/guard ile kilitlenir.

Readiness PR'ı yalnız dokümantasyon ve governance truth üretir; implementation veya
production onayı değildir.
