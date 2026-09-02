# ERP-000G Belge Kasası — Dar Acceptance Taslağı v1

> Durum: `TEST_DESIGN_PARTIAL / IMPLEMENTATION_NO_GO`
> Kaynak: `main@68aa15c81c049330e5ff08bf75b25a9ec6217f82`
> Kanonik kapanış: `AT-057=NOT_CLAIMED`

Bu acceptance yalnız önerilen upload → quarantine/validation → status diliminin zorunlu davranışlarını hazırlar. Exact API, permission/scope ve production ingress politikası açık olduğu için vakalar implementation-ready değildir.

## 1. Kanıt koşulları

Uygulama aşamasında her vaka şu kanıtları birlikte taşımalıdır:

- exact base/head/tested-tree SHA,
- gerçek SQL Server ve owner'ın kabul edeceği disposable object-store adapter'ı; S3-compatible seçim ancak teknik karar kabul edilirse,
- iki şirket ve güncel authorization'ı farklı aktörler,
- güvenli malware test imzası ile timeout/unavailable fixture'ı,
- fault injection sonrası istemci/SQL/object/audit/retry-recovery durumu,
- log ve response redaction kontrolü.

Mock/InMemory tek başına promotion kanıtı değildir. Test MIME, uzantı ve boyut değerleri `TEST_ONLY_POLICY` olarak etiketlenir.

## 2. Dar vaka matrisi

| Vaka | Taslak beklenti | Durum / kaynak |
|---|---|---|
| `CASE-DOC-001` | Geçersiz correlation/causation yokken oturumsuz upload/status, eksik/geçersiz CompanyContext seçim metadata'sına rağmen `401`; business SQL/object/hash lookup yok | DRAFT — ADR-004, Platform HTTP contract |
| `CASE-DOC-002` | Authenticated aktörde eksik/geçersiz/çoklu zorunlu company context `400`; business lookup yok | DRAFT — ADR-002 |
| `CASE-DOC-003` | Şirket üyeliği veya endpoint permission reddi `403`; idempotency/hash lookup'tan önce | BLOCKED — `G-DOC-AUTH-01` |
| `CASE-DOC-004` | Yetki sonrası valid-missing, foreign-company ve target-ABAC-scope-hidden kimlik aynı `404` body/header, business-audit=0 ve redacted telemetry outcome'unu replay/original-correlation headers olmadan verir | BLOCKED — `G-DOC-AUTH-01` |
| `CASE-DOC-005` | Kabul edilen byte önce `RECEIVED/QUARANTINED/VALIDATING`; erken `VALIDATED_CLEAN_PENDING_DOCUMENT` yok | BLOCKED — `G-DOC-INGRESS-01` |
| `CASE-DOC-006` | Server exact-byte hash/length hesaplar; yanlış client hash hiçbir karşılaştırmanın veya sonucun otoritesi değildir | DRAFT — DEC-DOC-002/009 |
| `CASE-DOC-007` | Uzantı, beyan edilen MIME ve gerçek içerik uyuşmazlığı fail-closed | BLOCKED — `G-DOC-INGRESS-01` |
| `CASE-DOC-008` | Malware test imzası `REJECTED`; scanner timeout/unavailable temiz-pending sonucu değildir | BLOCKED — `G-DOC-INGRESS-01` |
| `CASE-DOC-009` | Beş temiz-pending önkoşulu tek tek eksiltilir; hiçbir varyant `VALIDATED_CLEAN_PENDING_DOCUMENT` olmaz | DRAFT — DEC-DOC-009 |
| `CASE-DOC-010` | Object write'ın başlamadığı ve object oluşmadığı kesin kanıtlıysa güvenli failure; policy-safe retry | BLOCKED — `G-DOC-RECON-01` |
| `CASE-DOC-011` | Object başarılı + SQL commit kesin hata: kullanılabilir status kimliği veya başarı yok; key/hash sızmadan durable anchor↔failure-evidence bağı ve recovery sonrası tam-bir-kez canonical audit | BLOCKED — `G-DOC-RECON-01` |
| `CASE-DOC-012` | SQL pending başarılı + object kesin hata: authoritative `RECONCILIATION_REQUIRED`; belge/link yok, retry ikinci etki üretmez | BLOCKED — `G-DOC-RECON-01` |
| `CASE-DOC-013` | Server-computed ingest hash/length ile persisted-object readback veya provider integrity kanıtı validation sırasında uyuşmazsa temiz-pending oluşmaz; tamper-evident güvenlik/reconciliation kanıtı oluşur, business audit yalnız authoritative state transition commit ederse ADR-009'a göre oluşur | BLOCKED — `G-DOC-RECON-01` |
| `CASE-DOC-014` | Paralel same-byte yüklemede bu dilim global/cross-company hash lookup, reuse veya dedup yapmaz; ayrı fiziksel karantina yazımları şirket/mantıksal doğruluğu etkilemez | DRAFT — DEC-DOC-001/002 |
| `CASE-DOC-015` | Aynı namespace+actor+fingerprint seri replay kalıcı exact `ResponseJson/Outcome` sonucunu `Idempotency-Replayed: true` ve `Idempotency-Original-Correlation-Id` ile döndürür; ikinci iş etkisi yok | BLOCKED — `G-DOC-IDEMP-01` |
| `CASE-DOC-016` | Aynı namespace içinde path identity, canonical payload/metadata, server hash veya byte-length bileşenlerinden biri farklıysa ya da yetkili actor farklıysa `409`; önceki sonuç yok | BLOCKED — `G-DOC-IDEMP-01` |
| `CASE-DOC-017` | Authorization'ı geçemeyen actor aynı key'i denese bile lookup öncesi `403`; namespace oracle'ı, key reservation veya `CommandExecution` yok | BLOCKED — `G-DOC-AUTH-01` |
| `CASE-DOC-018` | Replay/status öncesi company/endpoint permission kalktıysa `403`; target ABAC kapsamı kalktıysa header-sız `404`; eski sonuç açıklanmaz | BLOCKED — `G-DOC-AUTH-01` |
| `CASE-DOC-019` | SQL state, audit ve completed idempotency sonucu aynı SQL transaction'da; object etkisi hariç | DRAFT — ADR-009/011 |
| `CASE-DOC-020` | Başarılı/no-change accepted state transition ve accepted reconciliation için ADR-009 tam audit zarfı; permission, before/after+version, causation, IP/UA, approval, idempotency hash, schema/outcome/integrity var; auth/context/permission/scope/invalid/not-found/conflict yollarında business audit=0; raw key/byte/object secret/foreign metadata yok | DRAFT — ADR-009, DEC-DOC-009 |
| `CASE-DOC-021` | Delete/purge/GC/legal-hold kaldırma yüzeyi yok; kilitli en-yüksek-yetki sınırı korunur | DRAFT — `OPEN-018 (KISMEN)` |
| `CASE-DOC-022` | Real SQL/object-store veya beş eksenli fault kanıtı eksikse promotion `BLOCKED` | DRAFT — AGENTS |
| `CASE-DOC-023` | Status valid-missing/foreign/scope-hidden varyantlarında body/header, business-audit=0 ve redacted telemetry outcome'u implementation invariant'ı olarak eşdeğer; ölçüm ve kabul edilmiş zaman bütçesi yoksa promotion `BLOCKED` | BLOCKED — `G-DOC-NOORACLE-01` |
| `CASE-DOC-024` | Aynı namespace+actor+fingerprint paralel çağrılarda tam bir completed sonuç vardır; bütün yetkili eş-fingerprint çağrılar winner'ın exact kalıcı sonucunu iki replay header'ıyla alır, ikinci business/quarantine object veya SQL etkisi yoktur; transient/`500` persisted/replayed değildir | BLOCKED — `G-DOC-IDEMP-01` |
| `CASE-DOC-025` | Object write kısmi/outcome-unknown ise private quarantine üzerinde SQL transaction'dan bağımsız durable discovery anchor; owner/trigger/recovery ve anchor↔audit bağı pinli değilse ilerlemez | BLOCKED — `G-DOC-RECON-01` |
| `CASE-DOC-026` | Aynı raw key, farklı `CompanyId` veya farklı sürümlü `CommandName` altında bağımsız namespace'tir; cross-namespace replay/conflict/existence açıklaması yok | BLOCKED — `G-DOC-IDEMP-01` |
| `CASE-DOC-027` | SQL commit outcome-unknown ise güncel authorization/context sonrası persisted state ve `CommandExecution` yeniden okunur; sonuç çözülmeden blind retry, ikinci object/SQL etkisi, kullanılabilir status kimliği veya success iddiası yok | BLOCKED — `G-DOC-RECON-01` |
| `CASE-DOC-028` | Malware scan/finalize outcome-unknown ayrı fault'tur; fail-open/temiz-pending yok, accepted recovery sonucu olmadan aynı denemede devam yok | BLOCKED — `G-DOC-RECON-01` |
| `CASE-DOC-029` | Güncel yürütücü crash olursa yöntemden bağımsız olarak en fazla bir sonraki güncel yürütücü vardır; stale finalize reddedilir, bounded recovery sonsuz pending bırakmaz ve ikinci etki üretmez | BLOCKED — `G-DOC-IDEMP-01` |
| `CASE-DOC-030` | Authorization/fingerprint öncesi kalıcı business/quarantine etkisi yoktur; seçilen teknik yöntemin bütün geçici kaynakları her terminal/recovery yolunda temizlediği ve ikinci etki üretmediği kanıtlanır | BLOCKED — `G-DOC-IDEMP-01` |
| `CASE-DOC-031` | Geçersiz correlation/causation metadata, anonymous/authenticated ve CompanyContext varyantlarından önce canonical `400/correlation_metadata_invalid`; hostile değer echo edilmez ve business etki yok | DRAFT — Platform HTTP contract |
| `CASE-DOC-032` | Upload'da eksik, boş, whitespace-only, multiple veya comma-joined `Idempotency-Key` fail-closed; business/fingerprint/object etkisi, key reservation ve `CommandExecution` yok | BLOCKED — `G-DOC-IDEMP-01` |
| `CASE-DOC-033` | Trim edilmiş key'in uppercase SHA-256 özeti namespace'e girer; çevre whitespace aynı digest/replay'i, case farkı ise farklı digest'i üretir; ham key DB/log/audit'e girmez | BLOCKED — `G-DOC-IDEMP-01` |

## 3. Sonraki dilimlere ertelenen kabul

Bu paket aşağıdakileri çalıştırılabilir kabul saymaz:

- Yetkili same-company duplicate warning ve yetkisiz/cross-company no-oracle.
- Sonraki duplicate/dedup dilimindeki same-company ve cross-company hit/miss davranış/zaman yan-kanal ölçümü; mevcut ilk dilimde global/cross-company lookup veya reuse yoktur.
- Temiz-pending sonrasında object corruption algılanırsa uygulanacak downgrade/quarantine/recovery durumu.
- “Benzer anahtar” tanımı ve kullanıcı seçimi.
- Logical document/version/record-link oluşturma.
- Signed download, export/share, OCR/AI.
- Retention, delete, purge, GC ve legal hold.

`AT-057`, `G-DOC-DUPKEY-01`, `G-DOC-NOORACLE-01` ve gerekli link/authorization kararları kapanmadan `NOT_CLAIMED` kalır. Fiziksel dedup doğruluk önkoşulu değildir ve başka şirket varlığını açıklayamaz.

## 4. Geçiş ölçütü

Bu iki dokümanın review edilmesi implementation GO değildir. Bir sonraki teknik handoff için önce:

1. `G-DOC-AUTH-01`: mevcut katalogdan exact upload/status permission ve scope,
2. `G-DOC-INGRESS-01`: production allowlist, boyut, scanner ve timeout davranışı,
3. `G-DOC-IDEMP-01`: tek/non-empty key, trim→uppercase SHA-256 türetimi, pre-fingerprint kalıcı etki=0, tek güncel yürütücü, stale-finalize reddi, bounded crash recovery, cleanup ve double-effect=0 sonuçları; exact yöntem Codex proposal'ı,
4. `G-DOC-RECON-01`: unknown/orphan/pending için durable owner, trigger, retry, failure-evidence↔audit bağı, recovery/cleanup, istemci sonucu ve operasyonel gözlem,
5. current-main üzerinde yeniden kaynak uzlaştırması,
6. exact failure/status API önerisi için bağımsız P0/P1 review

gereklidir. Bunlar tamamlanmadan schema, migration, API veya production davranışı başlatılmaz. Body/header, business-audit=0 ve redacted telemetry eşdeğerliği uygulama invariant'ıdır; `G-DOC-NOORACLE-01` zaman bütçesi kapanmadan promotion olmaz.
