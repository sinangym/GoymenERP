# Platform Group Authorization Scope Contract — DRAFT v1

- Durum: `DRAFT / IMPLEMENTATION-NEUTRAL / HUMAN SECURITY + IDENTITY + PRODUCT REVIEW REQUIRED`
- Production durumu: `IMPLEMENTATION BLOCKED / NO-GO`
- Taban: `main@f7aef5990e3bb60c13296de0bf420761ea48df9d`
- Sözleşme sahibi: `CODEX-PLATFORM`
- Ürün kararı sahibi: Product Owner / ChatGPT-BUSINESS
- Önkoşul: merged PR #103 group-shared master boundary
- Tüketilen ürün kararları:
  - Issue #98 `PRD-SCOPE-001=A`, `KILITLI`
  - Issue #99 `PRD-PERM-001=A`, `KILITLI`
  - Issue #100 v2 `PRD-GROUP-SHARED-001`

## 1. Amaç

Göymen ERP'nin mevcut yetkilendirme modeli bir kullanıcının permission kararını seçili
`CompanyId` içinde çözer. Group-shared Product ise hiçbir şirketin tenant kaydı değildir;
selected `CompanyContext` Product sahibini belirlemez. Bu nedenle mevcut company-scoped
permission servisini bir şirket seçerek, iki şirketi dolaşarak veya boş/magic CompanyId
vererek kullanmak güvenli bir group authorization modeli üretmez.

Bu sözleşmenin amacı, group-shared Product okuma ve değiştirme yetkisinin **hangi güvenlik
değişmezlerini** sağlaması gerektiğini implementation seçmeden tanımlamaktır. Sözleşme;
kimlik doğrulama, role/permission semantiği, group eligibility, selected-company ayrımı,
non-disclosure, cache/replay reauthorization, self-escalation ve adversarial kabul
kapılarını bir araya getirir.

Bu dosya bir schema, permission adı, role assignment, endpoint veya migration değildir.
Merge edilmesi Product API/runtime geliştirmesini ya da production aktivasyonunu açmaz.

## 2. Current-main gerçekliği

`main@f7aef5990e3bb60c13296de0bf420761ea48df9d` üzerinde:

1. `AccessScopeType` yalnız `Company`, `Site`, `Location`, `WorkCenter` değerlerini taşır.
2. Her `RoleAssignment` boş olmayan bir `CompanyId` taşır.
3. `Company` scope kimliği assignment `CompanyId` ile SQL constraint düzeyinde eşleşir.
4. `IErpAuthorizationService` permission kararını zorunlu `companyId` girdisiyle verir.
5. İstek cache anahtarı `(UserId, CompanyId, Permission)` bileşimidir.
6. `PermissionScope` yalnız company-wide, site, location ve work-center görünümünü taşır.
7. HTTP permission handler, resolved `ICompanyContext` yoksa permission'ı başarılı saymaz.
8. `HttpCurrentUser.AllowedCompanyIds`, yerel Identity atamalarından üretilen şirket erişim
   claim'lerini taşır; group eligibility taşımaz.
9. Dış OIDC/JWT içindeki ERP user/company claim'leri temizlenir; yerel Identity sonucu
   server tarafından yeniden üretilir.
10. Audit zarfı ve kalıcı idempotency deposu bugün `CompanyId` zorunlu ve company-keyed'dir.
11. Role ve role-permission tanımları globaldir; kullanıcı role assignment'ları ise zorunlu
    `CompanyId` ile company-scoped'dur. Bir permission'ın `SYSTEM_ADMINISTRATOR` rolüne
    eklenmesi, o role ait company assignment'larından bağımsız group eligibility üretmez.

Bu mevcut davranış company-scoped işlemler için korunmalıdır. Group authorization eklemek
amacıyla current-main güvenlik değişmezleri gevşetilemez.

## 3. Kapsam

Bu DRAFT aşağıdakileri kapsar:

- group-shared master için authentication ve authorization ayrımı;
- action permission ile group eligibility'nin ayrı, zorunlu girdiler olması;
- exact `SYSTEM_ADMINISTRATOR` ilk-pilot grantee semantiği;
- selected CompanyContext, originating company ve group scope ayrımı;
- default-deny ve bütün veri yollarında non-disclosure;
- read, write, replay, cache ve authorization-change revalidation;
- self-escalation ve görev ayrılığı sınırı;
- company-scoped child projection izolasyonu;
- audit, idempotency, concurrency ve event bağımlılıkları;
- implementation öncesi negatif ve yarış kabul matrisi.

## 4. Kapsam dışı

Bu DRAFT:

- `Group`, `GroupId`, membership, assignment veya policy tablosu tasarlamaz;
- mevcut `AccessScopeType` enum'una değer eklemez;
- permission string'i, registry/manifest veya capability adı üretmez;
- rol seed'i, role-permission grant'i veya user-role assignment yapmaz;
- fresh/existing-install reconciliation payload'ı hazırlamaz;
- mevcut Identity schema, migration, DbContext veya constraint'i değiştirmez;
- API route, header, claim, cookie, token veya error code seçmez;
- cache ürünü, TTL, invalidation broker'ı veya distributed topology seçmez;
- generic approval engine veya onay eşiği seçmez;
- global audit/idempotency persistence çözümünü seçmez;
- Product, Service, Price, Package veya Cari runtime davranışı kodlamaz;
- kanonik `DEC-*`, `ADR-*`, `OPEN-*`, `AT-*` veya task kimliği üretmez;
- ChatGPT-CARI branch/dosyalarına, PR #77'ye veya Cari domainine yazma yetkisi üretmez.

## 5. Normatif kaynaklar ve öncelik

| Kaynak | Tüketilen sınır |
|---|---|
| Issue #98 exact owner kararı [`issuecomment-5469061415`](https://github.com/sinangym/GoymenERP/issues/98#issuecomment-5469061415) | Product tek group-shared UUID/kod/karttır; selected company owner değildir; company ayrımı child/işlemsel kayıtta başlar. |
| Issue #99 exact owner kararı [`issuecomment-5469937658`](https://github.com/sinangym/GoymenERP/issues/99#issuecomment-5469937658) | İlk pilot Product/Service master read ve write yalnız exact mevcut `SYSTEM_ADMINISTRATOR`; diğer roller default-deny; yeni rol/assignment/AI authority yok; exact permission string ve group temsil biçimi açık. |
| Issue #100 v2 | `PRD-GROUP-SHARED-001`, Service ayrımı ve PR #77'nin dar merge-train/UI-shell ilişkisi. |
| Merged PR #103 | Group-shared master, company child, selected-company, global audit/idempotency ve portability güven sınırı. |
| `ADR-002` | Company-scoped kayıtlarda zorunlu CompanyContext, query/write guard ve non-disclosure. |
| `ADR-004` | OIDC authentication ile yerel ERP RBAC+ABAC ayrımı; dış claim yetki değildir; default-deny. |
| `ADR-009` | Canonical append-only audit, server-authoritative provenance ve atomiklik. |
| `ADR-011` | Actor/payload-bound idempotency, replay öncesi reauthorization ve optimistic concurrency. |
| `DEC-GOV-002/003` | Grup görünümü tenant sınırını kaldırmaz; şirket verisi taşınabilir ve ayrıştırılabilir kalır. |

Çelişkide exact son Product Owner kararı iş semantiğini, kabul edilmiş platform ADR'leri
güvenlik uygulamasını belirler. Issue yorum sırası tek başına karar üstünlüğü üretmez.
Bu sözleşme açık teknik kararı kapatamaz.

## 6. Terimler

### 6.1 Action permission

Bir aktörün Product üzerinde belirli bir eylem sınıfını yapabilmesini ifade eden, exact adı
henüz Product-owned versioned manifest ile seçilmemiş izin semantiğidir. Role code veya UI
butonu tek başına action permission değildir.

### 6.2 Group eligibility

Aktörün group-shared subject'e erişiminin server-owned ve authoritative kaynaktan
çözüldüğünü gösteren ayrı güvenlik kararıdır. Company assignment, iki şirkete erişim,
selected company, dış token grubu veya rol kodu tek başına group eligibility değildir.

### 6.3 Group authorization decision

Aktif yerel aktör, action permission, accepted grantee semantiği, authoritative group
eligibility, policy/security revision ve istenen subject/action birlikte doğrulandıktan
sonra üretilen kısa ömürlü server kararıdır. Kararın fiziksel veri modeli bu DRAFT'ta
seçilmez.

### 6.4 Selected CompanyContext

Company-scoped child projection ve company-owned komutların tenant sınırıdır. Product
master sahipliği veya group eligibility kaynağı değildir.

### 6.5 Originating company

Bir group-shared komutun hangi server-doğrulanmış company görünümünden başlatıldığını
gösterebilecek causation bilgisidir. Owner, partition key veya authorization shortcut
değildir. Zorunlu olup olmayacağı ayrı açık karardır.

### 6.6 Policy/security revision

Role, permission, group eligibility veya ilgili authorization politikasında kararın
geçerliliğini etkileyen değişikliği temsil eden semantik fence'tir. Exact token, kolon,
counter veya storage mekanizması bu DRAFT'ta seçilmez.

## 7. Kilitli ürün ve grantee sınırı

1. Product group-shared masterdır; company başına Product kopyası yoktur.
2. İlk pilotta Product master read/list/detail ve kabul edilmiş write sınıflarının tek
   grantee role semantiği exact mevcut `SYSTEM_ADMINISTRATOR`dır.
3. Diğer canonical, custom ve gelecekteki roller default-deny kalır.
4. Yeni `PRODUCT_MASTER_*` rolü veya otomatik assignment üretilmez.
5. AI kendi başına role, action permission veya group eligibility kazanmaz.
6. Product Price permission'ları bu karardan türetilmez.
7. Company/plant-scoped package, price, tax/accounting, stock, label, lot ve transaction
   kayıtları kendi permission + CompanyContext + ABAC + CompanyId sınırını korur.
8. Manual visible-code exception/alias administration sonraki business-role genişlemesinde
   dahi `SYSTEM_ADMINISTRATOR` dışına taşınamaz.
9. Operational business user'a Product write verilmeden önce ayrı editor/approver ve SoD
   Product Owner kararı zorunludur.
10. Product için kilitli group-shared semantik Service identity/scope/lifecycle davranışını
    otomatik belirlemez. Issue #99'daki Service pilot grantee sınırı, Service'in ayrı scope
    sözleşmesi kabul edilmeden runtime group authority üretmez.

Exact role code kabul edilmiş grantee semantiğidir; **role-code string kontrolü tek başına
authorization değildir**. Kabul edilmiş server-owned grantee/role binding, action permission
ve kabul edilmiş group eligibility birlikte fail-closed doğrulanmalıdır. Bu binding'in
fiziksel carrier'ı bu DRAFT'ta seçilmez. Bir veya birden fazla company-scoped assignment
group eligibility için hiçbir zaman yeterli değildir ve eligibility kaynağı olamaz. Kabul
edilmiş action için ek company assignment önkoşulu gerekip gerekmediği ise ayrı bir açık
karardır.

## 8. Authentication ve local actor

Group authorization değerlendirmesi başlamadan önce:

1. dış token imzası, issuer, audience ve süre doğrulanır;
2. `(issuer, subject)` aktif yerel ERP user'a çözülür;
3. dış token içindeki ERP user/company/role/group/scope claim'leri authority sayılmaz;
4. local user pasif, eşleşmemiş veya belirsizse sonuç deny olur;
5. current session ve actor identity server-authoritative olarak sabitlenir;
6. impersonation/delegation varsa ayrı kabul edilmiş sözleşme olmadan group access yoktur.

OIDC provider grubu, e-posta domaini, istemci claim'i veya JWT role claim'i Product group
eligibility üretmez.

## 9. Authorization karar değişmezi

Bir Product group action ancak bütün zorunlu girdiler aynı anda geçerliyse authorize edilir:

- authenticated ve aktif local actor;
- known ve accepted action permission;
- Issue #99 grantee semantiği;
- aktif, süresi dolmamış ve server-owned role/permission gerçeği;
- authoritative group eligibility;
- güncel policy/security revision;
- subject/action/resource sınıfının accepted contract'a uygunluğu;
- selected/originating company kullanılıyorsa onun güncel company access'i;
- company child projection isteniyorsa ayrıca child permission ve ABAC kapsamı;
- gerekli reauthorization, concurrency ve accepted approval/SoD sınırı.

Herhangi bir girdi eksik, unknown, çelişkili, stale veya birden fazla anlam taşıyorsa karar
deny olur. Partial success veya “en geniş kapsamı seç” davranışı yoktur.

Kararın server-side kanıtı en az actor, action, subject sınıfı, grantee/eligibility kaynağı,
etkili revision, varsa originating/selected company ve karar zamanını semantik olarak
taşımalıdır. Exact DTO, kolon veya token biçimi açık karardır.

## 10. Group eligibility değişmezleri

Kabul edilecek model şunları sağlamalıdır:

- eligibility server-owned authoritative kaynaktan okunur;
- explicit kayıt/karar yoksa deny;
- company assignment veya iki company claim'inin birleşimi eligibility değildir;
- `all`, wildcard, `Guid.Empty`, magic company veya config flag eligibility değildir;
- client body/query/header/cookie/route eligibility seçemez;
- role code collision veya yalnız display name eşleşmesi kabul edilmez;
- active/inactive ve valid-from/until semantiği fail-closed'dur;
- eligibility scope/policy değişimi revision/fence üretir;
- actor çıkarıldığında eski request cache, token, response veya replay access vermez;
- future company membership değişimi sessizce group authorization genişletmez;
- bir group kapsamından erişim, başka group veya şirket child verisine taşınmaz.

Root group/shared-scope identity, ayrı authoritative eligibility/membership lifecycle ve
exact persistence owner'ı açık karardır. Tek/birden fazla company assignment yalnız bu ayrı
eligibility yanında ek önkoşul veya originating-company context'i olarak gerekip gerekmediği
bakımından açık kalır; hiçbir durumda eligibility yerine geçmez.

## 11. Selected-company ayrımı

### 11.1 Product-only görünüm

Group-shared Product masterı okumak için selected company'yi Product owner gibi kullanmak
yasaktır. Accepted route/context modeli selected company istemiyorsa fake CompanyContext
oluşturulamaz. İstiyorsa selected company yalnız causation veya child projection sınırıdır.

### 11.2 Company child projection

Product ile birlikte fiyat, paket, stok veya kullanım bilgisi istenirse:

1. group Product read ayrı authorize edilir;
2. selected company server tarafından doğrulanır;
3. child owner permission ve ABAC scope ayrı authorize edilir;
4. child query mevcut CompanyId global filter/write guard sınırında kalır;
5. unauthorized child alanı, count, badge, null-shape, timing veya error farkıyla sızmaz;
6. group access başka şirket child verilerini union etmez.

Group Product read izni company price/package/stock/audit read izni değildir.

### 11.3 Context değişimi

Şirket veya group görünümü değiştiğinde pending request, optimistic UI, cache ve stale
response generation fence ile düşürülmelidir. Bir context'te authorize edilen child
projection diğer context'e taşınamaz.

## 12. Bütün veri yollarında aynı karar

Group authorization yalnız menü veya endpoint attribute'u değildir. Aynı karar en az şu
yollarda server tarafında uygulanır:

- list ve detail;
- doğrudan UUID/kod/alias;
- search/autocomplete/suggestion;
- count, aggregate ve dashboard;
- history/version;
- export/import preview;
- audit veya security evidence;
- mutation, retry ve idempotent replay;
- event/integration projection;
- AI tool discovery, preview ve execution;
- cache hit ve background materialization.

Bir yolun başka yoldan daha gevşek authorization kullanması kabul edilmez.

## 13. Non-disclosure

Yetkisiz, eligibility'si eksik veya scope'u stale aktör:

- Product UUID/kod/alias varlığını doğrulayamaz;
- hit/count/total farkından kayıt varlığı çıkaramaz;
- timing, cache hit, etag/version veya validation sırasından bilgi alamaz;
- başka şirketin child fiyat/paket/stok/kullanım/audit varlığını öğrenemez;
- eski idempotency sonucunu veya original correlation kimliğini göremez;
- export hash/object-key/fingerprint içinden unauthorized subject çıkaramaz;
- farklı 403/404/409/validation detaylarından foreign scope bilgisi çıkaramaz.

Exact HTTP status/error code platform hata ve Product domain sözleşmesiyle seçilir. Bu
DRAFT yeni code üretmez; eşdeğer unauthorized yolların stabil ve non-disclosing olmasını
zorunlu tutar.

## 14. Read ve list davranışı

1. Query server-side authorize ve project edilir; client-side company fan-out yoktur.
2. Authorization filtresi pagination/count'tan sonra uygulanamaz.
3. Unknown group eligibility boş başarı gibi gizlenerek policy hatasını normalleştirmez;
   accepted non-disclosing error/empty semantiği ayrıca kilitlenir.
4. Sort/search/index/cache group scope ve culture sınırını korur.
5. Company child projection kendi tenant query'sinden çıkar; Product tablosuna child
   özetleri kopyalanarak security shortcut yapılmaz.
6. Group read audit/telemetry politikası ayrı kabul edilmeden hassas actor/subject bilgisi
   loglanmaz.

## 15. Write ve commit-time reauthorization

Product group mutation için en az şu sıra korunur:

1. local actor/session;
2. known action permission;
3. accepted `SYSTEM_ADMINISTRATOR` pilot grantee semantiği;
4. authoritative group eligibility ve current revision;
5. gerekiyorsa selected/originating company access;
6. canonical request ve global idempotency scope;
7. expected version / optimistic concurrency;
8. Product invariant ve accepted impact preview;
9. commit-time permission + eligibility + revision reauthorization;
10. atomic business + global-subject audit + terminal idempotency result;
11. kabul edilirse transactional outbox;
12. scope-safe response.

Role, permission, eligibility veya policy revision validation ile commit arasında değişirse
stale decision uygulanmaz. Exact transaction/fence mekanizması audit/idempotency/persistence
ADR'leriyle seçilir.

Issue #99 ilk admin pilotunda generic ikinci Product approver zorunlu kılmaz. Bu, group
authorization policy/eligibility yönetiminin self-approval korumasını kaldırmaz.

## 16. Authorization değişikliği ve self-escalation

Group eligibility, group policy veya Product permission etkisini değiştiren işlem ayrı bir
kritik security command'dır. Product business command'i değildir.

Kabul edilecek yönetim akışı:

- current actor ve mevcut etkili authorization'ı yeniden doğrular;
- hedef actor/scope/action setini explicit ve server-side çözer;
- actor'ın kendisine yeni/yüksek group access vermesini fail-closed reddeder;
- kabul edilmiş policy approval gerektiriyorsa talep eden, hedef ve onaylayan ayrımını
  server-side zorunlu tutar; gerektirmiyorsa sahte approver üretmez;
- accepted reason/approval gereksinimini ayrı policy kaynağından alır ve caller'ın bu
  gereksinimi düşürmesine izin vermez;
- expected version ve idempotency uygular;
- before/after, actor, policy revision ve causation'ı; approval uygulanıyorsa ayrıca
  approver/SoD kanıtını append-only audit eder;
- aynı transaction sınırında tek terminal sonuç üretir;
- role assignment, permission grant ve eligibility değişimini gizli yan etki olarak
  birleştirmez;
- bulk değişiklikte preview/diff ve partial-failure politikasını açık contract olmadan
  seçmez.

Kabul edilmiş system reconciliation/bootstrap yolu insan talep/onay akışından ayrıdır.
Runtime requester/target/approver üçlüsü uyduramaz; bunun yerine accepted dar system
authority, reconciliation planı ve evidence sözleşmesini kanıtlamalıdır.

Exact approval engine, admin role matrisi, request endpoint'i ve emergency/break-glass
politikası açık karardır.

## 17. Request cache ve stale karar

Current request cache `(UserId, CompanyId, Permission)` group authorization için aynen
kullanılamaz. Kabul edilecek group cache semantiği en az şu girdilere bağlanmalıdır:

- local user/session/security identity;
- action permission;
- group/shared-scope identity;
- grantee, role, permission ve eligibility revision;
- subject/resource sınıfı;
- selected/originating company kullanılıyorsa company identity/access revision;
- company child projection permission/ABAC revision;
- Product lifecycle/version ve culture/query shape.

Cache hit authorization'ı atlamaz; decision source ve revision geçerli değilse fail-closed
olur. Process, distributed veya browser cache ürünü/TTL'si bu DRAFT'ta seçilmez. Client
cache authoritative scope kararı taşıyamaz.

## 18. Idempotency ve replay

ADR-011'in `(CompanyId, CommandName, KeyHash)` partition'ı global Product komutuna aynen
uygulanamaz. Group command için kabul edilecek partition/context ayrı karardır.

Her replay öncesinde:

- local actor aktifliği;
- action permission;
- grantee semantiği;
- group eligibility ve current revision;
- varsa originating/selected company access'i;
- subject/resource görünürlüğü

yeniden doğrulanır. Yetkisi kaldırılan aktör eski terminal sonucu, response gövdesi,
original correlation, child projection veya existence bilgisini okuyamaz.

Aynı key farklı actor/payload/group context/expected version ile geldiğinde accepted
conflict semantiği uygulanır; eski response açıklanmaz. Cross-company header değiştirerek
aynı global komut ikinci kez yürütülemez.

## 19. Audit ve evidence

Current audit zarfına arbitrary owner `CompanyId` yazılamaz. Group authorization kararı ve
değişikliği için kabul edilecek global-subject audit extension en az şu semantiği taşır:

- actor ve actor type;
- action ve subject sınıfı;
- effective grantee/permission/eligibility kaynağı;
- policy/security revision;
- decision outcome ve güvenli reason category;
- varsa selected/originating company causation;
- authorization change için canonical before/after ve policy gerektiriyorsa approver/SoD
  kanıtı; system reconciliation ise accepted system authority/evidence;
- correlation/causation;
- AI/source channel provenance;
- canonical integrity hash;
- idempotency özeti ve terminal outcome ilişkisi.

Audit read permission, retention, global-vs-company görünürlüğü ve persistence modeli açık
karardır. Denied-attempt security event ile business audit ayrımı gözlemlenebilirlik
sözleşmesiyle seçilir. PII veya permission detayı normal log'a dökülemez.

## 20. Company-scoped kayıt güvenliği

Group authorization aşağıdakileri değiştirmez:

- tenant entity `CompanyId` zorunluluğu;
- request-scoped immutable CompanyContext;
- EF global filters ve write guard;
- composite FK/unique/index şirket sınırı;
- company-scoped audit read;
- company-keyed idempotency gereken tenant command'ler;
- source document/operation tarafından çözülen CompanyId;
- başka modül tablosuna doğrudan yazma yasağı.

Product group access sahibi kullanıcı company A fiyatını/stokunu okuyabiliyorsa bu, company
B child kaydına erişim üretmez. Product referansı şirketler arası stok transfer yetkisi
değildir.

## 21. AI, integration ve system yolları

- AI model çıktısı action permission veya group eligibility değildir.
- AI tool registry, accepted server capability olmadan Product tool'u gösteremez/çağıramaz.
- Preview/onay bağlamı commit-time reauthorization yerine geçmez.
- Integration identity, service account veya background worker explicit accepted actor ve
  group authorization contract olmadan global Product access alamaz.
- System/migration authority user eligibility değildir; dışarıdan forge edilemez, dar
  allowlist ve bounded lifetime ile fail-closed olmalıdır.
- Environment flag, startup seed, config veya emergency script normal authorization bypass
  üretmez.
- Fresh/existing-install reconciliation yalnız accepted permission manifestini exact role
  ID/code eşleşmesine add-only uygular; group eligibility veya user assignment yaratamaz.
- Production reconciliation invocation/evidence Issues #62/#63 kapılarında kalır.

## 22. Adversarial kabul matrisi

| Senaryo | Zorunlu sonuç |
|---|---|
| Aktif local actor + accepted server-owned exact grantee binding + known action + authoritative group eligibility + current revision + kabul edilmiş diğer önkoşullar | Yalnız istenen group-safe Product eylemi izinli olur; company child, Price, Package veya Stock yetkisi türemez. |
| Unauthenticated, unmapped veya inactive local user | Group Product etkisi ve görünürlüğü `0`. |
| Forged OIDC role/group/company/permission claim | Local Identity dışında authority oluşmaz. |
| Unknown Product permission | Fail-closed; role code veya UI fallback yok. |
| Server-owned grantee/role binding inactive veya süresi dolmuş | Group Product etkisi ve görünürlüğü `0`; eski cache/session kararı kullanılmaz. |
| Group eligibility inactive veya süresi dolmuş | Company hakları değişmez; group Product etkisi ve görünürlüğü `0`. |
| `SYSTEM_ADMINISTRATOR` role code var, group eligibility yok | Deny; Product varlığı/count açıklanmaz. |
| Group eligibility var, accepted action permission yok | Deny; partial access yok. |
| Custom role veya benzer display name | Default-deny; exact grantee semantiği aşılmaz. |
| Company A assignment var, B yok | Bu durumdan group eligibility türetilmez; açık karar yoksa deny. |
| A ve B company assignment birlikte var | İki company erişimi group authorization sayılmaz. |
| Group eligibility kaldırılır, company hakları aktif kalır | Group Product eylemi deny olur; company hakları değişmez ve eligibility geri türetilmez. |
| Company access kaldırılır, group eligibility aktif kalır | Group eligibility değişmez; kaldırılan şirketin child projection'ı deny olur. Group-safe Product eylemi yalnız kabul edilmiş ek company önkoşulu varsa etkilenir. |
| Company assignment süresi dolar, group eligibility aktiftir | Assignment eligibility'yi silmez veya üretmez; kabul edilmiş ek company önkoşulu/child access gerekiyorsa yalnız o yol deny olur. |
| Empty/magic/wildcard CompanyId veya `all` scope | Reddedilir; tenant/global guard bypass olmaz. |
| Selected company değiştirilir | Product owner değişmez; pending child projection/cache stale olur. |
| Group Product read + unauthorized company child | Product-safe alanlar dışında price/package/stock/count/existence sızıntısı `0`. |
| Direct UUID/kod, list, search, count, export yolları | Aynı authorization ve non-disclosure sonucu. |
| Permission/eligibility request ortasında kaldırılır | Commit/replay/cache öncesi reauthorization reddeder. |
| Permission/eligibility revoke ile mutation commit yarışır | Kabul edilmiş transaction/fence doğrusal noktası tek sonuç üretir; revoke etkili olduktan sonra stale request/cache commit edemez. |
| Revoke sonrası re-grant ile eski idempotency key yeniden sunulur | Açık revision-binding/replay kararı olmadan eski authorization sonucu replay edilmez; fail-closed. |
| Stale policy/security revision | Decision kullanılmaz; fail-closed refresh gerekir. |
| Actor kendi group scope'unu yükseltir/onaylar | Mutation etkisi `0`; security evidence üretilir. |
| Aynı scope change key'i iki kez | Tek terminal etki; ikinci assignment/audit yok. |
| Aynı expected version ile iki scope change | Tek yetkili kazanan; diğeri conflict; silent overwrite yok. |
| Authorization audit insert başarısız | Scope/policy/permission değişikliği rollback. |
| Idempotency result insert başarısız | Authorization değişikliği ve audit rollback. |
| Eski group command sonucu farklı company header ile replay | İkinci business etkisi yok; foreign origin/child response sızmaz. |
| Scope kaldırıldıktan sonra eski replay/cache/token | Product, response ve original correlation görünürlüğü `0`. |
| Aynı key + aynı actor/payload/group context/current revision | Tek business etkisi ve tek terminal audit/idempotency sonucu; scope-safe önceki sonuç güvenli replay edilir. |
| Aynı key + farklı actor/payload/group context veya stale revision | İkinci business etkisi yok; önceki sonuç/gövde/existence açıklanmaz ve accepted conflict/fail-closed sonucu uygulanır. |
| Başka session'da eski authorization/cache kararı sunulur | Yeni session/actor/security revision yeniden doğrulanmadan karar veya response kullanılamaz. |
| Product Price veya company package eylemi | Ayrı owner permission + CompanyContext olmadan deny. |
| AI veya integration direct write | Accepted capability + reauthorization + audit/idempotency yoksa etki `0`. |
| HTTP/JWT/config ile system authority üretme | Fail-closed; normal guard'lar atlanmaz. |
| Client correlation/causation değeri taşır | Yalnız trace metadata olur; actor, permission, eligibility veya group authority kanıtı sayılmaz. |

Implementation kabulü yalnız unit test değildir. Architecture guard, HTTP integration,
gerçek SQL Server constraint/race/rollback, cache/replay, migration ve security review
kanıtları gerekir. InMemory provider relational/yarış kanıtı değildir.
Deny-all implementation bu matrisi geçemez; izinli satır accepted modelin gerçek olumlu
kararını ve tek etkili güvenli replay'ini kanıtlamalıdır.

## 23. Açık teknik kararlar

Bu DRAFT aşağıdakileri görünür tutar ve kapatmaz:

1. Root group/shared-scope identity ve company membership lifecycle.
2. Group eligibility persistence owner'ı ve representation modeli.
3. Exact `SYSTEM_ADMINISTRATOR` grantee semantiğinin group eligibility ile nasıl bağlanacağı.
4. Ayrı authoritative group eligibility yanında tek/birden fazla company assignment'ın ek
   önkoşul veya originating-company context'i olarak gerekip gerekmediği.
5. Product-owned versioned permission manifest ve exact permission string'leri.
6. Group authorization decision API/application contract ve policy revision biçimi.
7. Selected/originating company'nin Product read/write için zorunlu olup olmadığı.
8. Group scope change approval/SoD ve emergency/break-glass politikası.
9. Global-subject audit persistence, read permission ve retention.
10. Global command idempotency partition ve replay context'i.
11. Revoke → re-grant sonrasında eski idempotency key'in policy/security revision'a
    bağlanması ve replay sonucu.
12. Cache technology, invalidation topology, TTL ve performance targetları.
13. Stable route/error/capability adları.
14. Authorization security event, metric ve alert politikası.
15. Migration/backfill/rollback veya forward-fix planı.
16. Future group/company membership değişikliğinin mevcut Product erişimine etkisi.
17. Service identity/scope/lifecycle; Product group semantiğinden türetilemez.
18. Product Price ve company-scoped package permission sözleşmeleri.
19. Production activation ve reconciliation invocation/evidence.

## 24. Bağımlılık ve kapılar

| Kapı | Durum | Sonuç |
|---|---|---|
| PR #103 group-shared master contract | `MERGED` | Bu scope sözleşmesinin güvenlik tabanı. |
| Issue #98/#99 repository decision record | `OPEN / OWNER TRUTH-SYNC` | Runtime permission/schema işi bloklu. |
| Issue #99 grantee semantiği | `KILITLI / CONSUMED` | System Administrator-only pilot yeniden açılmaz; exact carrier açık. |
| Product permission manifest + naming/security review | `OPEN` | Permission registry/seed/reconciliation bloklu. |
| Group identity/membership/eligibility representation | `OPEN` | Identity schema/migration ve runtime group decision bloklu. |
| Global-subject audit extension | `OPEN` | Authorization/Product mutation bloklu. |
| Global idempotency scope | `OPEN` | Authorization/Product mutation replay bloklu. |
| Product persistence/migration | `OPEN` | Product schema/API bloklu. |
| Issues #62/#63 | `OPEN / HUMAN + RUNNER GATES` | Production reconciliation activation bloklu. |
| Issue #87 | Runner çalışıyor; enforcement/reviewer alt kapıları açık | Exact-head CI ve kritik insan review zorunlu. |

## 25. Yasak kısa yollar

- `AccessScopeType.Group` ekleyip mevcut company assignment modelini yeterli saymak;
- current `IErpAuthorizationService` çağrısına fake/empty/arbitrary CompanyId vermek;
- iki company permission sorgusunu OR/union ederek group access üretmek;
- selected company'yi Product owner veya idempotency partition yapmak;
- `SYSTEM_ADMINISTRATOR` string kontrolünü authorization saymak;
- dış OIDC/JWT group/role claim'ini local group eligibility yapmak;
- wildcard, config flag veya environment ile group scope açmak;
- role permission seed'ini group membership/assignment yerine kullanmak;
- Product permission manifest kabul edilmeden permission string eklemek;
- normal API startup'ta reconciliation/activation çalıştırmak;
- group access ile company child filtrelerini veya permission'larını atlamak;
- client-side fan-out, count union veya browser cache ile group görünümü kurmak;
- arbitrary CompanyId ile global audit/idempotency kaydı üretmek;
- stale cache/replay sonucunu authorization kanıtı saymak;
- Product group kararından Service veya Price davranışı türetmek;
- bu DRAFT'ı schema/API/production GO olarak göstermek.

## 26. Review ve definition of ready

Bu sözleşme implementation girdisine ancak aşağıdakilerle dönüşebilir:

1. Product Owner, #98/#99 kararlarını repository karar siciline exact biçimde taşır.
2. Product/Identity owners versioned permission manifest ve naming review'u kabul eder.
3. Security/Identity review, group identity/membership/eligibility ve policy revision
   modelini açık ADR/contract ile kabul eder.
4. Group grantee semantiğinin current company-scoped role assignments ile exact ilişkisi
   Product Owner + Security tarafından kapatılır.
5. Audit owner global-subject audit/write/read/retention modelini kabul eder.
6. Idempotency owner global partition/replay context'ini kabul eder.
7. Data/migration owner persistence/backfill/rollback planını gerçek SQL kabulüyle bağlar.
8. Platform error/capability owner stable non-disclosing contract'ı kabul eder.
9. Task-local adversarial matris kanonik acceptance planına insan review'uyla bağlanır.
10. Exact-head standard CI, real SQL, container ve UI golden gerçek runner adımlarıyla geçer.
11. Product Owner/Product review; #98/#99 ürün gerçeğini, Service/Price kapsam dışını, Cari
    sahiplik sınırını ve PR #77 ilişkisinin doğru temsil edildiğini doğrular.
12. Kritik sözleşme exact-head üzerinde ayrı Security, Identity ve Product insan review
    kapılarında kabul edilir.

Bu kapılar tamamlanana kadar:

```text
CONTRACT: DRAFT
GROUP ELIGIBILITY MODEL: OPEN
PERMISSION MANIFEST: OPEN
IDENTITY SCHEMA/MIGRATION: BLOCKED
PRODUCT SCHEMA/API: BLOCKED
PRODUCTION READ/WRITE: NO-GO
PRODUCTION RECONCILIATION: NO-GO
```
