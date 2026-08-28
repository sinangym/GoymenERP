# ERP-000G Readiness Raporu

- Tarih: 2026-08-28
- Taban: `main` / `9f08ff16447eca89682d0a084e2341b9c2b433c2`
- Dal: `codex/erp-000g-spec-gate`
- Sonuç: **Implementation NO-GO; spec/ADR/acceptance review GO**
- Production kodu/package/migration/object-store/worker: **Yok**
- Bağlayıcı öneri: `docs/adr/ADR-005-document-vault-object-storage.md`

## Sonuç

ERP-000G task manifestte `ready`, ADR register'da ADR-005 `Kabul` görünmesine rağmen
fiziksel ADR-005, ayrıntılı feature spec ve uygulanabilir acceptance planı güncel `main`de
yoktur. Mevcut tek task cümlesi; tenant-safe physical dedup, SQL–object-store crash
pencereleri, quarantine/malware yaşam döngüsü, signed access ve restore davranışını
kodlamaya yetecek kesinlikte değildir.

Bu dalda oluşturulan ADR-005 yalnız review önerisidir. Provider/package seçmez, bucket veya
schema oluşturmaz, pilot belge türü/metadata/link sahibi uydurmaz ve ERP-000G kodlama
yetkisi vermez.

## Kanonik olarak doğrulanan yön

- Belge Kasası tek gerçeklik sahibidir; modüller güvenli document reference taşır.
- Model `Blob -> Mantıksal Belge -> Belge Sürümü -> Kayıt Bağlantısı` katmanlarını ayırır.
- Aynı byte fiziksel olarak dedup edilebilir; farklı şirket ve iş bağlamları ayrı mantıksal
  belge/erişim kapsamı taşır.
- Belge sürümü yerinde overwrite edilmez; yeni version ve geçmiş görünürlüğü korunur.
- S3 uyumlu nesne deposunda byte, SQL Server'da metadata/hash başlangıç yönüdür.
- MIME doğrulama, malware tarama, aktarımda/depoda şifreleme, kısa imzalı erişim ve hassas
  export authorization zorunludur.
- Silme/retention/legal hold ayrı, yetkili ve auditli politikadır.
- DB ve belge deposu checksum manifestiyle birlikte restore ve şirket ayrıştırma kanıtına
  girmelidir.

## Readiness boşluk matrisi

| Kapı | Güncel durum | Risk | Implementation durumu |
|---|---|---|---|
| Fiziksel ADR-005 | Register `Kabul`, dosya yoktu | Onaysız ayrıntıların kabul edilmiş sanılması | Bu dalda `Öneri`; insan review'u bekliyor |
| Feature spec / DoR | Yalnız task/epic tek cümlesi | Owner, scope, state, auth, API ve acceptance eksik | Bloklu |
| Dependencies | Manifest yalnız ERP-000A | CompanyId/auth/audit/idempotency ve async sınır atlanabilir | B/C/D/E ve seçilirse F düzeltmesi gerekli |
| Tenant dedup | Shared physical blob yönü var | Hash existence oracle, cross-company metadata/URL sızıntısı, yanlış shared delete | `OPEN-026` adayı + security acceptance gerekli |
| Blob identity/encryption | Provider-agnostic yön var | ETag/hash karışması, tahmin edilebilir key, key/export belirsizliği | `OPEN-026` adayı bloklu |
| SQL–object atomikliği | Tanımlı değil | Orphan object, missing byte, yarım finalize, duplicate version/link | `OPEN-029` adayı + ADR failure matrix gerekli |
| Security lifecycle | Yalnız business version state'i var | Malware/scan-error dosyasının clean sayılması veya erişime açılması | `OPEN-027` adayı bloklu |
| Signed access/export | Yüksek düzey zorunluluk var | Stale permission, uzun URL, header/cache leak, tenant bypass | `OPEN-028` adayı bloklu |
| İlk pilot contract | Belge türü/link owner seçilmemiş | Business metadata/permission uydurulması | `OPEN-029` adayı bloklu |
| Retention/legal hold | `OPEN-018` açık | Shared blob veya geçmiş sürümün erken silinmesi | Purge/delete disabled kalmalı |
| Acceptance | AT-057/AT-065 genel ve `Taslak/TBD` | SQL/S3, crash, malware, concurrency ve tenant kanıtı yok | Task-local plan gerekli |
| Backup/restore/export | Yüksek düzey yön var | DB-object drift veya başka tenant metadata sızıntısı | Gerçek restore/export acceptance gerekli |

## P0/P1 değerlendirmesi

Bu hazırlık anında production kodu olmadığı için doğrulanmış P0 veri kaybı yoktur. Ancak
aşağıdaki P1 readiness blockerları kapanmadan kod başlatılamaz:

1. `ready/Kabul` durum drift'i ve fiziksel ADR/spec/acceptance yokluğu;
2. B/C/D/E ve async tasarımda F dependency kapısının manifestte görünmemesi;
3. shared physical dedup'ın tenant existence oracle ve ref/delete sınırının tanımsızlığı;
4. SQL metadata ile object-store side effect'i arasında crash-safe idempotent orchestration
   ve reconciliation sözleşmesinin yokluğu;
5. malware/MIME/quarantine/scan-error/clean yaşam döngüsünün ve fail-closed erişimin
   tanımsızlığı;
6. signed access, export permission, expiry/revocation ve audit sözleşmesinin eksikliği;
7. gerçek SQL Server + gerçek S3-compatible engine acceptance kanıtının olmaması.

## Açık karar adayları

Bu kimlikler bu Draft dalında kanonik `ACIK_KARARLAR.md` kaydına öneri olarak alınmıştır;
karar sahibi review'u ve merge olmadan kapanmış sayılmaz:

### OPEN-026 — Shared blob dedup, encryption ve lifecycle sınırı

Physical dedup scope'u; content-hash algorithm/version; object-key türetimi; encryption/
key scope ve rotation; tenant reference modeli; shared blob delete; şirket export/restore
ve başka tenant'a existence bilgisi vermeyen response davranışı seçilmelidir.

Güvenli geçici davranış: hash/object key dışarı açılmaz, cross-tenant match kullanıcıya
gösterilmez, production cross-company physical dedup kapalıdır, shared physical blob
silinmez ve automatic purge yoktur.

### OPEN-027 — Upload validation, malware ve quarantine politikası

MIME/extension allowlist'i; dosya/request/page/archive limitleri; encrypted/password/macro/
nested archive davranışı; scanner engine, signature update, timeout/retry, rescan ve manual
release yetkisi seçilmelidir.

Güvenli geçici davranış: clean kanıtı olmayan dosya preview/download/link/OCR/AI/export için
kapalıdır; scanner outage fail-closed'dur.

### OPEN-028 — Signed access, preview, download ve hassas export

Proxy/presigned yaklaşımı; URL/capability TTL; tek object/operation kapsamı; range/cache/
Content-Disposition; permission daralması ve acil revocation; download/export izinleri,
görev ayrılığı ve audit seçilmelidir.

Güvenli geçici davranış: bucket private, kalıcı URL yok, her issuance server-side
CompanyId/permission/resource doğrulamalıdır; hassas export kapalıdır.

### OPEN-029 — İlk pilot contract ve DB–object-store orchestration

Producer/owner module, document type, zorunlu metadata, veri sınıfı, version/emission
kuralları, business/security lifecycle, link target contract'ı, kullanıcı/System actor,
permission sahibi, staged upload/finalize/reconciliation akışı ve synchronous/async sınırı
seçilmelidir. Async yol seçilirse ERP-000F bağımlılığı ayrıca açılır.

Güvenli geçici davranış: satın alma, finans, OCR veya başka bir gerçek belge türü pilot diye
tahmin edilmez; production automatic finalize, logical document/link veya worker
oluşturulmaz. Orphan/quarantine object erişime açılmaz ve purge edilmez.

## Zorunlu acceptance kapsamı

Task-local acceptance planı en az şunları gerçek SQL Server ve gerçek S3-compatible engine
üzerinde ayrı Given/When/Then senaryolarıyla kanıtlamalıdır:

- aynı byte'ın aynı ve farklı şirkette paralel upload/dedup yarışı;
- tek physical blob mümkünken ayrı CompanyId logical document/version/link ve sıfır
  cross-tenant hash/metadata/count/error/signed-URL sızıntısı;
- bir şirket referansı kalktığında başka şirketin byte'ının silinmemesi;
- object-success/DB-fail, DB-session/object-missing, scan-success/finalize-fail,
  corrupt/missing object ve idempotent retry/orphan reconciliation;
- extension/MIME mismatch, malware, scanner timeout/unavailable, archive bomb,
  password/nested archive ve path traversal dosya adı;
- clean olmadan preview/download/link/OCR/AI/export reddi;
- signed capability issue/redeem/expiry/revocation, permission daralması, safe filename/
  content-disposition ve hassas export audit'i;
- concurrent version/active-version yarışı, replay ve same-key/different-payload conflict;
- immutable byte/hash/version ve normal writer/direct-object overwrite reddi;
- idempotent DB migration + object-store policy manifesti;
- DB+object version/checksum restore, orphan/missing reconciliation ve şirket export
  paketinde başka tenant metadata'sı olmaması;
- architecture dependency guard ile başka modül table/object key erişiminin reddi.

AT-056 OCR/AI çıkarımı ERP-000G core acceptance değildir. AI/OCR scope'a alınırsa ayrıca
`OPEN-020`, veri sınıfı, preview/approval ve no-direct-write sözleşmesi gerekir.

## Dependency ve mimari kapı

- ERP-000G task dependency en az ERP-000B, ERP-000C, ERP-000D ve ERP-000E'yi açıkça
  taşımalıdır.
- Async scan/finalize/reconcile veya dış effect için ERP-000F/outbox seçilirse ERP-000F
  kabul edilmeden ilgili implementation başlatılmaz.
- Documents başka modül tablosuna veya object bucket'ına doğrudan erişmez; link ownership
  ve authorization Application contract üzerinden doğrulanır.
- Yeni cross-module Infrastructure edge ancak açık ADR + exact allowlist + architecture
  test ile aynı değişiklikte kabul edilebilir.

## Coding gate

ERP-000G implementation aşağıdakilerin tamamı gerçekleşmeden **NO-GO / BLOCKED** kalır:

1. ADR-005 `Öneri`den insan/ChatGPT review'uyla `Kabul`e geçer.
2. Feature spec Definition of Ready'nin tüm başlıklarını doldurur; uygulanmayan başlıklar
   gerekçeli N/A olur.
3. `OPEN-026`…`OPEN-029` karar sahibi tarafından kapanır; `OPEN-018` açıkken purge/delete
   disabled kalır.
4. Manifest status/dependency gerçeği düzeltilir.
5. Task-local acceptance senaryoları ve gerçek SQL+S3 test tasarımı kabul edilir.
6. İlk pilot document/version/link/data-class/permission contract'ı yazılı kabul edilir.
7. Gerekli dependency edge varsa ADR/allowlist/architecture guard atomik tanımlanır.

Bu kapılar açılmadan package/provider seçimi, test fixture/substrate kodu, object-store
container/bucket, schema/migration, upload API, worker, signed URL veya pilot entegrasyon
oluşturulmaz.

## Bu dalda tamamlanan güvenli hazırlık

Bu spec-only dalda, başka feature branch üzerine zincir kurmadan:

1. tam ERP-000G feature spec;
2. `ERP-000G-AC-*` gibi task-local kimlikli ayrıntılı acceptance planı;
3. `OPEN-026`…`OPEN-029` adaylarının kanonik açık karar kaydı;
4. ADR register, platform task listesi ve task manifest readiness/dependency düzeltmeleri

hazırlanmıştır. Paket Draft PR'da mimari, security ve acceptance review'una sunulmalıdır.
Global acceptance ID'leri açık PR'larla çakışacak biçimde tahmin edilmemelidir.

## Paralel Draft PR ve merge sırası kapısı

Bu dalın tabanı olan `main`, ERP-000E'yi dokümanlarda hâlâ `in_review`, ERP-000F'yi ise
`ready` gösteren tarihsel snapshot'tır. Draft PR #10 bu gerçeği ERP-000E `done` ve
ERP-000F `blocked` olarak düzeltir; bu dal yalnız ERP-000G'nin `blocked` gerçeğini ekler.

PR #10 ile bu dal aşağıdaki dört ortak dosyayı değiştirir:

- `docs/architecture/ADR_REGISTER.md`;
- `docs/backlog/task_manifest.json`;
- `docs/decisions/ACIK_KARARLAR.md`;
- `docs/features/ERP-000_PLATFORM_TASKS.md`.

Bu nedenle PR #10 merge edilmeden ERP-000G dalı merge edilmez. PR #10 merge edildikten
sonra bu dal güncel `main` ile senkronize edilir ve dört dosyada hem ERP-000E/ERP-000F
gerçeğinin hem ERP-000G kapılarının korunduğu yeniden doğrulanır. Conflict veya semantik
tutarsızlık sessizce çözülmez; insan review'una taşınır. Bu kapı Draft PR açıklamasında da
merge blocker olarak görünür kalmalıdır.

PR #8 governance dalı ERP-000G feature/spec bağımlılığı değildir. Yeni çalışma güncel
`main` tabanında kalmalı; unmerged governance veya feature branch üzerine bağımlı zincir
kurulmamalıdır.
