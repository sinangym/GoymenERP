# ERP-000G Belge Kasası — Dar JIT Taslağı v1

> Durum: `JIT_DRAFT / TEST_DESIGN_PARTIAL / IMPLEMENTATION_NO_GO`
> Kaynak: `main@68aa15c81c049330e5ff08bf75b25a9ec6217f82`
> Sahiplik: yalnız ChatGPT iş kuralı ve acceptance hazırlığı

Bu belge `ERP-000G / W0-DOCVAULT` için sıradaki küçük davranışın sınırını hazırlar. Schema, migration, endpoint, permission, provider, scanner, worker veya production aktivasyonu yetkisi vermez.

## 1. Bağlayıcı gerçek

- `DEC-DOC-001`: model `PhysicalBlob → LogicalDocument → DocumentVersion → RecordLink` katmanlarıdır.
- `DEC-DOC-002`: aynı byte fiziksel olarak deduplicate **edilebilir**; bu bir zorunlu global tek-blob garantisi değildir. Şirket, mantıksal belge, metadata, sürüm ve erişim bağlamları birleşmez.
- `DEC-DOC-003`: byte veya belge sürümü overwrite edilmez; değişiklik yeni sürümdür.
- `DEC-DOC-009`: allowlist, gerçek içerik/MIME doğrulaması, malware taraması, kısa imzalı erişim ve hassas export yetkisi zorunludur.
- `ADR-002/004`, `DEC-TEC-013` ve platform HTTP error contract: authentication ve şirket/permission/scope kontrolü business, hash, idempotency ve object lookup'tan önce yapılır; başka şirket veya görünmeyen kayıt için existence oracle yoktur.
- `ADR-009/011`: audit/correlation zorunludur; idempotency namespace tam olarak `(CompanyId, CommandName, IdempotencyKeyHash)` olur ve replay öncesi yeniden yetkilendirme yapılır.
- `OPEN-018` durumu `KISMEN`dir: fiziksel silmeyi yalnız en yüksek yetkili kullanıcı yapabilir kararı kilitlidir; süre, legal hold, onay/audit ve teknik purge yöntemi açık olduğu için delete/purge/GC bu dilimde kapalıdır.
- Kanonik görev yalnız `ERP-000A` bağımlılığı taşır. `ERP-000F`, ancak async scan/finalize/reconciliation seçilirse sonraki teknik bağımlılıktır; bu ilk taslağın global blocker'ı değildir.

Kaynak kayıtları: `AGENTS.md`, `task_manifest.json`, `DELIVERY_BACKLOG_v1.json`, `KARAR_SICILI.md`, `ACIK_KARARLAR.md`, `ACCEPTANCE_70.md`, `PLATFORM_HTTP_ERROR_CONTRACT_v1.md`, ADR-002/004/009/011 ve [Issue #66 exact handoff](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-5514680192). Owner kararı ve kaynaklar implementasyon öncesi canlı main üzerinde yeniden okunur.

## 2. Önerilen ilk dilim

İlk olası uygulama dilimi yalnız şunları içerir:

1. yetkili şirkette byte yükleme kabulü,
2. byte'ın karantinada tutulması,
3. sunucu hash/length, gerçek içerik-MIME ve malware sonucu doğrulaması,
4. güvenli upload durumunun yeniden yetkilendirilerek okunması.

Bu dilim dahi `G-DOC-AUTH-01`, `G-DOC-INGRESS-01`, `G-DOC-IDEMP-01` ve `G-DOC-RECON-01` kapanmadan uygulamaya başlayamaz. `G-DOC-NOORACLE-01` içindeki davranış invariant'ları uygulamada zorunlu, ölçülmüş zaman bütçesi ise promotion blocker'ıdır.

İlk dilimin dışında:

- duplicate aday gösterimi ve `AT-057`,
- `LogicalDocument`, `DocumentVersion` veya `RecordLink` üretimi,
- owner-modül bağlantısı,
- download, signed access, export/share,
- OCR/AI tüketimi,
- delete, purge, GC ve legal-hold işlemleri.

## 3. Şirket ve erişim sınırı

- Kullanıcıya erişilebilir upload, logical document, version ve link kayıtları `CompanyId` ile sınırlandırılır.
- `PhysicalBlob`, hash, object key veya storage URI authorization nesnesi değildir.
- Response, log ve audit; raw byte, server hash, object key/URI, credential veya yabancı şirket metadata'sı döndürmez.
- Platformun correlation/causation metadata sınırı daha erkendir: geçersiz değer canonical `400/correlation_metadata_invalid` üretir. Bunun dışında oturumsuz required-context çağrı, eksik/geçersiz CompanyContext seçim metadata'sına rağmen `401`; authenticated aktörde eksik/geçersiz zorunlu şirket bağlamı `400/company_context_invalid`; şirket üyeliği veya endpoint permission reddi `403` olur.
- Güncel target ABAC kapsamı dışında, foreign-company veya valid-missing upload kimliği aynı non-disclosing `404` body/header şeklini verir; replay ve original-correlation header'ları bulunmaz. Üçünde de business audit kardinalitesi sıfır, redacted telemetry outcome'u eşdeğerdir. Bu davranış uygulama invariant'ıdır. Zaman eşdeğerliği ve kabul edilmiş bütçe `G-DOC-NOORACLE-01` promotion kanıtını bekler. Yeni problem code icat edilmez.
- Status ve replay her çağrıda güncel authentication, company access, permission ve scope kontrolünden geçer.

Exact permission ve Company/Site/Location scope matrisi mevcut katalog üzerinden owner kararıyla pinlenir; bu taslak ad üretmez.

## 4. Güvenlik durumu

Önerilen kavramsal akış:

```text
RECEIVED → QUARANTINED → VALIDATING → VALIDATED_CLEAN_PENDING_DOCUMENT
                                  ↘ REJECTED
RECEIVED / QUARANTINED / VALIDATING → RECONCILIATION_REQUIRED
```

`VALIDATED_CLEAN_PENDING_DOCUMENT` yalnız karantina byte'ının teknik güvenlik sonucudur; kullanıcıya açılmış belge, version, link veya download anlamına gelmez. Bu durum için aynı exact byte üzerinde şu beş kanıt birlikte zorunludur:

1. server-computed exact-byte hash,
2. server-computed byte length,
3. gerçek içerik ve MIME doğrulaması,
4. kesin `clean` malware sonucu,
5. SQL ve object durumunun tutarlı olması.

Herhangi biri eksikse `VALIDATED_CLEAN_PENDING_DOCUMENT` yasaktır. `unknown`, timeout, scanner unavailable, checksum mismatch veya finalize belirsizliği fail-closed'dur. `REJECTED` ya da `RECONCILIATION_REQUIRED` durumundan aynı deneme içinde doğrudan temiz-pending geçişi yoktur.

İstemci hash'i, uzantı ve `Content-Type` yalnız untrusted hint'tir. Production MIME/uzantı/boyut ve scanner-timeout politikası açık karardır; test fixture değeri production politikası değildir.

## 5. SQL–object ayrık hata sınırı

SQL ve object store atomik kabul edilmez. Her fault-injection kanıtı ayrı ayrı istemci sonucunu, SQL durumunu, object durumunu, audit sonucunu ve retry/recovery davranışını göstermelidir.

| Hata | Zorunlu sonuç |
|---|---|
| Object write kesin başlamadı ve object oluşmadığı kanıtlı | Kullanılabilir kayıt yok; güvenli hata, policy-safe retry |
| Object write kısmi veya sonucu belirsiz | Private quarantine üzerinde SQL transaction'dan bağımsız, kalıcı discovery anchor; `RECONCILIATION_REQUIRED` |
| Object başarılı, SQL commit başarısız | Object key/hash sızmadan kalıcı orphan-discovery/reconciliation anchor'ı |
| SQL pending başarılı, object başarısız | `RECONCILIATION_REQUIRED`; belge/link yok |
| Scan/finalize belirsiz | Fail-open yok; `RECONCILIATION_REQUIRED` |
| Validation sırasında checksum mismatch | Sunucunun ingest exact-byte hash/length değeri persisted-object readback veya provider integrity kanıtıyla uyuşmazsa temiz-pending oluşmaz; güvenlik audit'i ve reconciliation |
| Paralel aynı-byte upload | Bu dilimde global/cross-company hash lookup, reuse veya dedup yoktur; ayrı fiziksel karantina yazımları doğruluğu etkilemez |

`RECONCILIATION_REQUIRED`, authoritative SQL satırı yoksa yalnız iç operasyon durumudur; istemciye kullanılabilir upload/status kimliği veya başarı iddiası üretmez. `G-DOC-RECON-01`; durable discovery anchor'ın owner'ını, tetikleyicisini, object-put/SQL-commit outcome-unknown yeniden okuma ve retry sonucunu, recovery/cleanup sorumlusunu, istemci failure/opsiyonel opaque-attempt-receipt sözleşmesini, anchor ile tam audit arasındaki tamper-evident bağı ve operasyonel gözlemi pinler. Recovery canonical audit'i tam bir kez üretir; durable failure evidence veya çözülmüş sonuç yokken blind retry, ikinci object/SQL etkisi ve success yoktur. Async recovery yöntemi seçilirse `ERP-000F` ayrıca kabul edilmelidir; bu taslak outbox/worker varmış gibi davranmaz.

## 6. İdempotency ve audit

- Namespace: `(CompanyId, CommandName, IdempotencyKeyHash)`.
- İlk-dilim upload kritik komuttur ve tam bir adet, trim sonrası boş olmayan `Idempotency-Key` ister. Eksik, boş/whitespace, multiple veya comma-joined değer business/fingerprint/object işleminden önce fail-closed reddedilir; exact module error code teknik öneride review edilir.
- `IdempotencyKeyHash`, trim edilmiş key'in büyük harf SHA-256 özetidir. Ham key saklanmaz veya loglanmaz; trim dışında case folding ya da başka sessiz canonicalization yapılmaz.
- Fingerprint; sürümlü `CommandName`, path identity, canonical business payload/metadata, server-computed hash ve byte length'i kapsar. İstemci hash'i fingerprint otoritesi değildir.
- Aynı namespace + aynı yetkili actor + aynı fingerprint, kalıcı `ResponseJson/Outcome` sonucunu zorunlu olarak birebir replay eder; response `Idempotency-Replayed: true` ve `Idempotency-Original-Correlation-Id` taşır.
- Aynı namespace + actor + fingerprint paralel yarışında tam olarak bir completed sonuç vardır; bütün yetkili eş-fingerprint çağrılar winner'ın kalıcı exact sonucunu aynı iki replay header'ıyla alır ve ikinci business/quarantine object veya SQL etkisi oluşmaz. ADR-011'in mevcut tek-kazanan sınırı korunur; upload'a özgü koordinasyon yöntemi burada seçilmez. Transient/`500` sonuç completed `CommandExecution` olarak saklanmaz veya replay edilmez.
- Aynı namespace + farklı fingerprint veya güncel authorization'ı geçen farklı actor `409` verir; önceki sonuç açıklanmaz.
- Farklı `CompanyId` veya farklı sürümlü `CommandName`, raw key aynı olsa bile farklı namespace'tir; birbirinin kaydını replay/conflict etmez.
- Şirket/endpoint permission kontrolünü geçemeyen actor idempotency lookup'tan önce `403` alır; target ABAC kapsamı dışındaki kimlik ise non-disclosing `404` alır. İkisinde de namespace veya önceki sonuç açıklanmaz.
- Authentication, CompanyContext, permission veya target-scope reddi key ayırmaz ve `CommandExecution` üretmez.
- `G-DOC-IDEMP-01` yöntem değil sonucu pinler: authorization ve fingerprint tamamlanmadan kalıcı business/quarantine etkisi yoktur; paralel veya crash yolunda en fazla bir güncel yürütücü vardır; stale finalization reddedilir; çözüm sınırlı sürede recovery'ye döner ve sonsuz in-progress bırakmaz; geçici kaynaklar temizlenir; ikinci etki sıfırdır. Request-local staging, claim, lease veya fencing kullanılıp kullanılmayacağı Codex'in dar teknik öneri ve review konusudur; kaynak kararı gibi dayatılmaz.
- SQL durum değişimi, audit ve completed idempotency sonucu aynı SQL transaction sınırındadır; object etkisi bu transaction'a dahilmiş gibi gösterilmez.
- Başarılı/no-change kabul edilmiş state transition ile accepted reconciliation business audit ürettiğinde ADR-009'un tam zarfını taşır; özellikle company, actor/role, permission code, action/entity, old/new version ve before/after, UTC zaman, correlation/causation, source/reason, IP/user-agent, approval context, idempotency hash, schema version, outcome ve integrity hash korunur. Authentication/CompanyContext/permission/scope reddi, invalid request, non-disclosing not-found ve conflict business audit üretmez; gerekli güvenlik/operasyon kanıtı audit diye yeniden adlandırılmaz. Secret, raw key veya byte içermez.

## 7. Açık kararlar

| Yerel kapı | İlk dilime etkisi |
|---|---|
| `G-DOC-AUTH-01` | Exact upload/status permission ve company/scope matrisi gelmeden NO_GO |
| `G-DOC-INGRESS-01` | Production allowlist, boyut, scanner ve timeout davranışı gelmeden NO_GO |
| `G-DOC-IDEMP-01` | Tek/non-empty key, trim→uppercase SHA-256 türetimi, pre-fingerprint kalıcı etki=0, tek güncel yürütücü, stale-finalize reddi, bounded crash recovery, cleanup ve double-effect=0 sonuçları pinlenmeden NO_GO; yöntem Codex proposal'ıdır |
| `G-DOC-DUPKEY-01` | Duplicate/benzer-anahtar davranışı ve `AT-057` sonraki dilimde |
| `G-DOC-NOORACLE-01` | Body/header, business-audit=0 ve redacted telemetry eşdeğerliği uygulama invariant'ıdır; ilk status ve sonraki duplicate lookup için ölçülmüş zaman eşdeğerliği + kabul edilmiş yan-kanal bütçesi promotion blocker'ıdır |
| `G-DOC-RECON-01` | Unknown/orphan/pending için durable anchor owner-trigger-retry-recovery/cleanup, istemci sonucu, anchor↔audit bağı ve operasyonel gözlem gerekir |
| `G-DOC-LINK-01` | Pilot owner/belge türü ve target authorization gelmeden link yok |
| `G-DOC-ACCESS-01` | Gizlilik, download/export, TTL/revoke gelmeden erişim URL'si yok |
| `OPEN-018 (KISMEN)` | Kilitli yüksek-yetki sınırı korunur; retention/purge/legal-hold kapalı |
| `ERP-000F (koşullu)` | Yalnız async finalize/reconcile seçilirse gerekir |

NEEDS-SINAN: ERP-000G ilk pilot owner/belge türü ile upload ve status için mevcut permission + Company/Site/Location scope matrisi nedir?

## 8. Handoff

- `implementationGo=false`
- `testDesign=PARTIAL`
- `AT-057=NOT_CLAIMED`
- `promotion=BLOCKED`
- Codex schema/API/migration'a başlamaz; önce `G-DOC-AUTH-01`, `G-DOC-INGRESS-01`, `G-DOC-IDEMP-01` ve `G-DOC-RECON-01` girdileri alınır, ardından exact route/DTO/failure sözleşmesi dar bir teknik öneri olarak review edilir. `G-DOC-NOORACLE-01` zaman kanıtı kapanmadan promotion yoktur.
