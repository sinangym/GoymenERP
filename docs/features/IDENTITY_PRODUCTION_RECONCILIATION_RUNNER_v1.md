# Identity Production Reconciliation Runner Readiness v1

- Tarih: 2026-08-29
- Doğrulanan taban: `main@d880044d7802581f8a8298a55d5821607f0896c8`
- Takip kaydı: GitHub Issue `#62`
- Eş kanıt sözleşmesi: `docs/features/IDENTITY_GLOBAL_SECURITY_EVIDENCE_v1.md`
- Ön koşul çekirdek: `docs/features/IDENTITY_OPTION_A_PERMISSION_RECONCILIATION_v1.md`
- Durum: **DRAFT / NO-GO**
- Production activation: **YASAK**

Bu belge bir implementation onayı, fiziksel ADR, deployment runbook'u veya production
aktivasyon kararı değildir. Issue `#62` açık kalır. Aşağıdaki açık insan ve altyapı
girdileri kapanmadan runner kodlanamaz, production veritabanına bağlanamaz ve bu iş
`implementation-ready` sayılamaz.

## 1. Amaç

Mevcut inert Identity rol/yetki uzlaştırma çekirdeğinin production kurulumunda ancak:

- explicit ve ayrı bir upgrade süreciyle,
- değişmez release-plan bağıyla,
- salt okunur preflight ve hash bağlı apply ayrımıyla,
- least-privilege güvenilir workload kimliğiyle,
- permission grant ile global security evidence aynı atomik sınırdayken,
- fail-closed sonuç ve tekrar davranışıyla

çalıştırılabilmesi için readiness sınırını tanımlamak.

İlk consumer yalnız kabul edilmiş `CARI-PERM-001 / Option A` planıdır. Bu belge genel
ama keyfi bir rol/yetki yönetim aracı tasarlamaz.

## 2. Doğrulanmış mevcut gerçek

| Alan | `main@d880044...` üzerindeki durum | Sonuç |
|---|---|---|
| Option A politikası | `SYSTEM_ADMINISTRATOR` / `30000000-0000-0000-0000-000000000003` rolüne yalnız `counterparty.accounts.create` eklenir | Hedef değiştirilemez |
| Existing-install çekirdeği | SQL Server, `Serializable` transaction, `UPDLOCK/HOLDLOCK`, exact rol ID/kod kontrolü, add-only permission ve rollback vardır | Inert çekirdek kullanılabilir, production yolu değildir |
| Tekrar ve yarış | Fresh/legacy/repeat/concurrent/collision/rollback gerçek SQL kabul testleri vardır | Çekirdek davranışı kanıtlıdır |
| API kaydı | `ISystemRolePermissionReconciler` normal API DI container'ına kayıtlıdır | Servisin kayıtlı olması production invocation yetkisi vermez |
| API startup | `src/backend/GoymenERP.Api/Program.cs` yalnız Development/Testing ortamında migrate/seed çağırır; production uzlaştırma çağrısı yoktur | Bu ayrım korunmalıdır |
| Sonuç zarfı | Mevcut sonuç yalnız kontrol edilen rol ve eklenen permission sayısını taşır | Plan, release, approval, actor ve evidence kanıtı yoktur |
| Rol metadata kontrolü | Mevcut çekirdek exact rol ID/kod eşleşmesini kontrol eder; `IsActive`, `IsSystem` ve kanonik ad politikasını zorunlu tutmaz | Kanonik insan kararı kaydedilmeden production apply NO-GO |
| Delivery sözleşmesi | `docs/architecture/ADR_REGISTER.md` içinde ADR-010 özeti vardır; fiziksel ADR-010 ve kabul edilmiş production runner sözleşmesi yoktur | Release/migration sahipliği tahmin edilemez |
| Global evidence | Company bağımsız, immutable security-change evidence modeli yoktur | Grant production'da çalıştırılamaz |

Mevcut `IdentityDataSeeder` veya normal API startup'ı production existing-install
upgrade kanıtı değildir. Fresh seed hizalaması da production verisinin yükseltildiğini
kanıtlamaz.

## 3. Kaynak ve yetki sınırı

Bağlayıcı mevcut kaynaklar:

1. Issue `#43` üzerindeki ürün sahibi Option A kararı.
2. `src/backend/Modules/Counterparty/COUNTERPARTY_ACCOUNT_CREATE_ROLE_POLICY.json`.
3. `docs/features/IDENTITY_OPTION_A_PERMISSION_RECONCILIATION_v1.md`.
4. Issue `#62` ve `#63` içindeki görünür blocker'lar.
5. `AGENTS.md` ve repository governance kuralları.

Bu DRAFT belge:

- yeni ADR kimliği ilan etmez,
- ADR-010'u fiziksel olarak oluşturmaz veya kabul etmiş saymaz,
- runner ürünü, job sistemi, cloud/on-prem topolojisi ya da secret sağlayıcısı seçmez,
- task status yükseltmez,
- Issue `#62` veya `#63`'ü kapatmaz,
- production activation yetkisi vermez.

## 4. Kilitli Option A değişmezleri

İlk production planının izin verilen tek iş etkisi:

| Alan | Exact değer |
|---|---|
| Rol kimliği | `30000000-0000-0000-0000-000000000003` |
| Rol kodu | `SYSTEM_ADMINISTRATOR` |
| Permission | `counterparty.accounts.create` |
| Eksik rol oluşturma | Hayır |
| Yeni rol oluşturma | Hayır |
| Kullanıcı/rol assignment | Hayır |
| Permission silme/revoke | Hayır |
| Custom permission/assignment/scope değişikliği | Hayır |

Runner:

- rol, permission veya environment bilgisini serbest CLI parametresiyle değiştiremez,
- kullanıcı girdisinden arbitrary reconciliation target üretemez,
- unknown permission'ı kabul edemez,
- ID/kod collision veya eksik rolü sessizce onaramaz,
- mevcut custom permission, assignment ve scope kayıtlarını silemez,
- applied grant'i otomatik geri alamaz.

Yeni consumer ancak kendi kabul edilmiş ürün kararı, sürümlü contract'ı, acceptance
matrisi ve insan review'u ile ayrı plan olarak eklenebilir.

## 5. API startup'tan kesin ayrım

Production reconciliation, web API lifecycle'ının parçası değildir.

Zorunlu sınırlar:

- `GoymenERP.Api` startup, health-check, request pipeline veya normal seed akışı
  runner'ı çağırmaz.
- API pod/process/container sayısına bağlı "ilk başlayan çalıştırır" davranışı kurulmaz.
- Production API runtime principal'ına uzlaştırma için ek SQL yazma yetkisi verilmez.
- Runner/executor gelecekte upgrade-only bir host/entrypoint ve ayrı DI composition root
  üzerinden çalışır.
- Upgrade host'un fiziksel türü ve orkestrasyon kanalı açık altyapı kararıdır; bu belge
  console app, migration bundle, CI job, container job veya belirli bir sağlayıcı seçmez.
- Migration ve reconciliation sırası fiziksel ADR-010 ile açıkça kabul edilmeden
  implementation başlamaz.

`ISystemRolePermissionReconciler` servisinin bugün API DI container'ında kayıtlı olması,
onu endpoint, startup veya production job üzerinden çağırma izni değildir. Gelecek
uygulama bu erişimi daraltmalı ve upgrade-only executor'ın normal HTTP yüzeyinden
çözülememesini architecture test ile kanıtlamalıdır.

## 6. Release-bundled plan sözleşmesi

Aşağıdaki alanlar production için önerilen **minimum readiness sözleşmesidir**. Fiziksel
şema, serialization ve signing yöntemi insan review'uyla kabul edilmeden bağlayıcı
implementation detayı sayılmaz.

| Alan | Amaç | Kural |
|---|---|---|
| `PlanId` | Planın sabit kimliği | Release içinde değişmez; Option A consumer'ını açıkça gösterir |
| `PlanSchemaVersion` | Canonical plan biçimi | Pozitif, desteklenen exact sürüm |
| `Targets` | Exact rol/permission listesi | Yalnız release-bundled; runtime override yok |
| `PlanHash` | Canonical plan bütünlüğü | Sürümlü canonical byte dizisinin SHA-256 özeti |
| `ContractHash` | Kabul edilmiş politika artifact bağı | Exact policy artifact/manifest digest'i |
| `ReleaseArtifactDigest` | Çalışan binary/package bağı | Onaylı release manifestinden gelir |
| `EnvironmentIdentity` | Hedef ortamın güvenilir kimliği | CLI serbest metni değil, kabul edilmiş control-plane kaynağı |
| `ExecutionId` | Tekil çalışma kimliği | Duplicate execution fail-closed veya exact replay olarak tanımlanır |
| `CorrelationId` / `CausationId` | İz zinciri | Server/runner tarafından güvenilir biçimde üretilir veya doğrulanır |
| `ApprovalReference` | İnsan/onay kapısı | Kabul edilmiş SoD mekanizmasına ait doğrulanabilir referans |
| `ExpectedPreviewHash` | Preflight-apply bağı | Apply'ın gördüğü exact canonical preflight sonucu |

Canonicalization sözleşmesi şunları açıkça sürümlemelidir:

- alan sırası ve isimleri,
- UTF-8 byte üretimi,
- GUID, zaman ve enum gösterimi,
- JSON nesne/dizi sıralaması,
- duplicate/non-canonical alan reddi,
- uppercase/lowercase digest gösterimi,
- hash'e giren ve girmeyen alanlar.

Raw connection string, token, secret değeri, kullanıcı parolası ve hassas altyapı
detayı plan veya hash report'una girmez. Hash üretiminin deterministik olması secret
değerini loglama gerekçesi değildir.

Plan/signature/attestation trust modeli henüz açıktır. Yalnız hash bulunması artifact'in
kim tarafından üretildiğini kanıtlamaz; production güven iddiası için registry,
signature/attestation ve trust-root kararı ayrıca gerekir.

## 7. Preflight sözleşmesi

Preflight tamamen salt okunur olmalıdır.

Önerilen sıra:

1. Release plan, contract ve artifact digest'i canonical olarak doğrula.
2. Güvenilir environment identity ile hedef environment uyuşmasını doğrula.
3. Trusted workload identity ve bağlantı referansını doğrula; secret değerini gösterme.
4. Hedef DB schema/migration uyumluluğunu salt okunur kontrol et.
5. Exact rol ID ve ordinal rol kodunu yükle.
6. Rolün mevcut metadata'sını ve permission kümesini canonical snapshot'a al.
7. Yalnız eksik permission delta'sını hesapla.
8. Assignment, scope ve custom permission için değişmeyecek alan özetlerini üret.
9. Açık policy veya drift varsa `Rejected/NO-GO` döndür.
10. Canonical preview report ve `PreviewHash` üret.

Preflight:

- `INSERT`, `UPDATE`, `DELETE`, migration, seed veya evidence olayı üretmez,
- lock/transaction davranışıyla production veriyi değiştirmez,
- "uygulanabilir" raporu production grant'in yapıldığı anlamına gelmez,
- plan hash, contract hash, release digest, environment ve gözlenen DB state'i birlikte
  bağlamadan apply token üretmez,
- secret veya raw connection bilgisini report'a koymaz.

Preflight'ten sonra state değişirse apply eski preview'ı kullanarak devam etmez.
Yeni preflight ve yeni insan onayı gerekip gerekmediği kabul edilmiş approval
politikasında açıkça tanımlanmalıdır.

## 8. Apply sözleşmesi

Apply ancak exact `ExpectedPreviewHash` ve geçerli approval reference ile başlar.

Önerilen fail-closed sıra:

1. Plan/contract/release/environment/approval bağlarını tekrar doğrula.
2. Yeni canonical preflight üret ve `ExpectedPreviewHash` ile exact karşılaştır.
3. Drift veya hash farkında permission/evidence yazmadan reddet.
4. SQL Server'da explicit `Serializable` transaction aç.
5. Exact rol satırını `UPDLOCK/HOLDLOCK` ile tekrar yükle.
6. ID/kod ve kabul edilmiş metadata politikasını doğrula.
7. Yalnız eksik Option A permission grant'ini hazırla.
8. Canonical global security evidence zarfını hazırla.
9. Grant ile `Applied` veya `NoOp` evidence insert'ini aynı Identity transaction ve aynı
   `SaveChanges` sınırında kalıcılaştır.
10. Commit sonrası redacted, hash'li execution report üret.

Grant yazılıp evidence yazılamıyorsa transaction rollback olmalıdır. Evidence yazılıp
grant yazılamıyorsa aynı transaction rollback olmalıdır. `NoOp` sonucu da production
çalışmasının kanıtıdır ve immutable evidence üretmelidir.

Current core'da yalnız ID/kod doğrulaması vardır. Inactive rol, `IsSystem=false` veya
kanonik ad drift'i için kabul edilmiş politika bulunmadığından production apply bunları
tahmin ederek geçemez. Güvenli öneri fail-closed'dur; fakat repository'de bu boşluğu
kapatan kanonik karar kimliği henüz bulunmadığından karar sahibi kaydı olmadan
implementation kilitlenemez.

## 9. Sonuç sınıfları ve fail-closed davranış

Exact numeric exit code'lar henüz kilitlenmez; orkestrasyon kanalı seçildikten sonra
sürümlemeli contract'ta belirlenir. Anlamsal sonuçlar en az şunları ayırmalıdır:

| Sonuç | DB etkisi | Deployment davranışı |
|---|---|---|
| `Applied` | Exact grant + aynı transaction'da evidence | Başarı; report hash'i saklanır |
| `NoOp` | Grant yok; aynı transaction'da no-op evidence | Başarı; sessiz geçilmez |
| `Rejected` | Grant yok; evidence store sağlıklıysa rejection evidence | Fail-closed, production adımı durur |
| `Failed` | Grant/evidence transaction rollback | Fail-closed, otomatik success sayılmaz |
| `EvidenceUnavailable` | Grant yok veya rollback; primary evidence garantisi yok | Production activation bloke |

Non-success sınıfları orchestration tarafından başarıya çevrilemez. Retry yalnız:

- hata sınıfı retry-safe ise,
- kabul edilmiş retry/fencing politikası varsa,
- aynı execution/plan hash bağını koruyorsa,
- önceki outcome ve evidence durumu kesin olarak doğrulanabiliyorsa

uygulanabilir. Retry sayısı, timeout, backoff ve fencing değerleri bu belgede
uydurulmaz.

Primary Identity/evidence store tamamen kullanılamıyorsa aynı store'a "failure evidence"
yazılacağı garanti edilemez. Bu durumda grant uygulanmaz, runner ayrı bir
`EvidenceUnavailable` sonucu verir ve production activation durur. Harici durable
secondary evidence kanalı, sağlayıcısı ve teslim garantisi açık altyapı kararıdır.

## 10. Yetki, onay ve secret sınırı

Production runner için:

- insan kullanıcının normal API token'ı yeterli sayılmaz,
- trusted workload identity açıkça doğrulanır,
- uygulama runtime principal'ı ile upgrade principal'ı ayrılır,
- upgrade principal yalnız kabul edilmiş schema/table/procedure işlemlerine en az yetkiyle
  erişir,
- migration owner ile reconciliation executor aynı olmak zorunda değildir,
- approval actor ile executor görev ayrılığı kabul edilmiş politika üzerinden uygulanır,
- break-glass kullanımı ayrı onay, sebep ve evidence gerektirir,
- connection string veya credential yalnız kabul edilmiş secret reference/resolver ile
  alınır,
- raw secret CLI argümanı, plan, log, trace, exception veya report'ta gösterilmez.

Workload identity ürünü, SQL grant listesi, secret store, approval sistemi ve
break-glass mekanizması Issue `#62` için insan/altyapı girdisidir.

## 11. Concurrency, idempotency ve geri dönüş

Runner:

- aynı planın tekrarında duplicate permission üretmez,
- paralel iki caller'da unique constraint ve transaction kilitlerini güvenlik katmanı
  olarak korur,
- `ExecutionId + PlanHash + EnvironmentIdentity` tekrarını görünür biçimde ele alır,
- aynı execution kimliğiyle farklı payload/hash'i kesin reddeder,
- cancellation/timeout sonrası transaction sonucunu kesinleştirmeden retry etmez,
- yarım grant, yarım evidence veya "unknown success" durumunu başarı saymaz.

Rollback, açık transaction'ın rollback edilmesidir. Production'da commit edilmiş bir
permission'ı sessizce kaldırmak rollback değildir. Uygulanmış grant'in kaldırılması ayrı
ürün kararı, risk analizi, onay, evidence ve forward-fix/revoke planı gerektirir.

## 12. Kabul matrisi

Bu matris implementation onayı değildir; implementation-ready olmak için minimum test
kapısını tanımlar.

| ID | Senaryo | Beklenen kanıt |
|---|---|---|
| `ID-RUN-001` | Legacy kurulumda permission eksik preflight | Exact tek delta; DB write ve evidence sıfır; deterministic preview hash |
| `ID-RUN-002` | Permission zaten mevcut preflight | `NoOp` preview; DB write sıfır |
| `ID-RUN-003` | Exact preview ve onayla apply | Tek grant ve tek `Applied` evidence aynı commit |
| `ID-RUN-004` | Aynı plan tekrar apply | Duplicate grant yok; tek yeni `NoOp` execution evidence |
| `ID-RUN-005` | İki paralel exact apply | Toplam tek grant; sonuçlar bir `Applied` ve bir `NoOp`; duplicate yok |
| `ID-RUN-006` | Aynı execution ID + aynı payload | Kabul edilmiş replay politikasıyla deterministik sonuç; ikinci grant yok |
| `ID-RUN-007` | Aynı execution ID + farklı payload/hash | Kesin red; grant yok |
| `ID-RUN-008` | Rol ID/kod collision veya rol eksik | Fail-closed; grant yok; custom state aynı |
| `ID-RUN-009` | Inactive/non-system/name drift | Kanonik insan kararı kaydedilene kadar NO-GO; test kabul edilen politikaya bağlanır |
| `ID-RUN-010` | Plan/contract/release digest uyuşmazlığı | Apply başlamaz; grant yok |
| `ID-RUN-011` | Yanlış environment identity | Apply başlamaz; secret açığa çıkmaz; grant yok |
| `ID-RUN-012` | Preflight sonrası DB drift | Expected preview hash uyuşmaz; grant yok |
| `ID-RUN-013` | Eksik/geçersiz approval reference | Grant yok; fail-closed |
| `ID-RUN-014` | Permission insert hatası | Transaction rollback; evidence/grant yok; retry-safe state |
| `ID-RUN-015` | Evidence insert hatası | Permission grant rollback; yarım state yok |
| `ID-RUN-016` | Cancellation/timeout | Outcome kesinleştirilir; unknown success başarı sayılmaz |
| `ID-RUN-017` | API production startup/restart/scale-out | Runner çağrısı, migration ve grant sıfır |
| `ID-RUN-018` | Normal API runtime principal | Reconciliation/evidence mutation yetkisi yok |
| `ID-RUN-019` | Upgrade principal | Yalnız kabul edilmiş least-privilege yüzey; privilege escalation yok |
| `ID-RUN-020` | Log/report taraması | Raw connection string, token, secret ve hassas değer yok |
| `ID-RUN-021` | Primary evidence store unavailable | Grant yok; distinct NO-GO outcome; production activation durur |
| `ID-RUN-022` | Custom permission/assignment/scope | Pre/post canonical parity; yalnız Option A delta değişir |

Concurrency, rollback, constraint ve atomiklik testleri gerçek SQL Server üzerinde
çalışmalıdır. InMemory/mock kanıtı bunların yerine geçmez.

## 13. Açık insan ve altyapı girdileri

Aşağıdakiler bu DRAFT ile kapanmaz:

| Açık girdi | Neden zorunlu |
|---|---|
| Fiziksel ADR-010 ve release/migration ownership | Migration, runner, rollback ve onay sırasını bağlamak |
| `OPEN-014` gerçek sunucu/ağ/env envanteri | Hedef environment ve erişim modelini doğrulamak |
| Runner/job/upgrade host kanalı | Invocation, isolation ve lifecycle sahipliği |
| Trusted workload identity | Actor/provenance ve yetkisiz çalıştırmayı engellemek |
| Exact least-privilege SQL grant'leri | Runtime/upgrade ayrımını kanıtlamak |
| Secret reference/resolver | Credential'ın güvenli edinimi ve redaction |
| Artifact registry, digest, signature/attestation ve trust root | Release-plan kaynağını doğrulamak |
| Approval/SoD/break-glass mekanizması | İnsan kapısı ve görev ayrılığı |
| Timeout/retry/backoff/fencing/maintenance window | Yarış ve bilinmeyen sonuç riskini yönetmek |
| Inactive/non-system/name drift politikası için kanonik karar kaydı | Production rol uygunluğunu fail-closed tanımlamak |
| Global evidence modelinin fiziksel kararı | Grant + evidence atomikliğini mümkün kılmak |
| Durable secondary failure evidence kanalı | Primary store yokken dürüst kanıt sınırı |
| `OPEN-013`, `OPEN-015`, `OPEN-017` ve restore/telemetry kapıları | Production kimlik, kapasite ve recovery güveni |
| Production activation owner/window | Son insan go/no-go ve change window |

Bu girdiler provider, topoloji, threshold veya secret tahmin edilerek kapatılamaz.

## 14. Güvenli gelecek uygulama sırası

Bu sıra yalnız bağımlılık planıdır; hiçbir adım bu belgeyle başlatılmış sayılmaz:

1. Issue `#62` ve `#63` için açık insan/altyapı kararlarını kaydet.
2. Fiziksel ADR-010 ve delivery/migration ownership sözleşmesini kabul et.
3. Global Identity security evidence modelini ayrı review ile tanımla.
4. Evidence schema/migration ve append-only güvenlik katmanlarını gerçek SQL testleriyle
   uygula.
5. Grant + evidence atomik executor'ını upgrade-only composition root içinde uygula.
6. Release-bundled plan, canonical hash, preflight/apply ve result contract'ını uygula.
7. Local ve gerçek SQL negative/race/rollback acceptance matrisini tamamla.
8. Seçilen altyapı kanalına least-privilege adapter ekle.
9. Staging'de repeat, rollback, restore, secret redaction ve evidence export provası yap.
10. Exact-head security/deployment review ve insan go/no-go sonrası production window
    planla.

## 15. Readiness ve Definition of Done

### Bu DRAFT için

- [x] Mevcut inert çekirdek ile production activation arasındaki sınır yazılıdır.
- [x] API startup'tan ayrım fail-closed olarak tanımlıdır.
- [x] Preflight/apply ve hash bağı için minimum öneri görünürdür.
- [x] Grant + evidence atomikliği zorunlu kapıdır.
- [x] Kabul matrisi ve açık insan/altyapı girdileri ayrılmıştır.
- [x] Provider/topoloji/secret/threshold seçilmemiştir.
- [x] Issue `#62` ve `#63` açık ve NO-GO kalır.

### Implementation-ready olma kapısı

- [ ] Fiziksel ADR-010 kabul edilmiştir.
- [ ] Global security evidence fiziksel sözleşmesi kabul edilmiştir.
- [ ] Inactive/non-system/name drift ve diğer açık ürün/security politikaları kanonik
      insan kararlarıyla kapanmıştır.
- [ ] Runner kanalı, workload identity, SQL least privilege ve secret resolver seçilmiştir.
- [ ] Release artifact trust ve approval/SoD mekanizması kabul edilmiştir.
- [ ] Timeout/retry/fencing ve failure-evidence davranışı kabul edilmiştir.
- [ ] Acceptance test planı insan Identity/security/deployment review'undan geçmiştir.

Bu checklist tamamlanmadan Issue `#62` kapatılamaz, implementation başlatılamaz ve
production activation yapılamaz.
