# Platform Group-Shared Master Contract — DRAFT v1

- Durum: `DRAFT / IMPLEMENTATION-NEUTRAL / HUMAN ARCHITECTURE + SECURITY REVIEW REQUIRED`
- Production durumu: `IMPLEMENTATION BLOCKED / NO-GO`
- Taban: `main@83197c50f515db959638fadd7cc11ad6eb7a4fc8`
- Sözleşme sahibi: `CODEX-PLATFORM`
- Ürün kararı sahibi: Product Owner / ChatGPT-BUSINESS
- Tüketilen karar: Issue #98 `PRD-SCOPE-001`, seçenek A, `KILITLI` — exact owner
  kararı: `issuecomment-5469061415`
- Tüketilen yetki kararı: Issue #99 `PRD-PERM-001`, seçenek A, `KILITLI` — exact
  owner kararı: `issuecomment-5469937658`
- İlgili epic: Issue #100 `PRD-CORE-001`

## 1. Amaç

Issue #98, iki Göymen şirketinin aynı teknik/ticari ürün için tek `Product` UUID, tek
görünen kod ve tek ürün kartı kullanmasını bağlayıcı ürün kararı olarak kilitlemiştir.
Fiziksel ve ticari kayıtlar ise server tarafından çözülen kendi `CompanyId` değerlerini
taşımaya devam eder.

Mevcut platformun ana güvenlik değişmezi farklıdır: tenant kayıtları zorunlu
`CompanyId`, immutable request-scoped `CompanyContext`, EF global query filter, write
guard ve bileşik foreign key ile korunur. Grup görünümü bu sınırı kaldırmaz.

Bu doküman, kilitli ürün ve pilot yetki kararlarını mevcut tenant güvenliğini sessizce delmeden
uygulanabilir hale getirmek için gereken **platform sınırlarını** tarif eder. Bir Product
şeması, exact permission string/manifest'i, migration, endpoint, router veya deployment seçmez.

## 2. Kapsam

Bu taslak şu konuları kapsar:

1. group-shared master ile company-scoped business/physical record ayrımı;
2. kimlik, yetki, selected-company ve group-read güven sınırı;
3. sorgu, arama, toplam, cache ve bilgi sızıntısı kuralları;
4. write, audit, idempotency, concurrency ve system-write kapıları;
5. tenant kayıtlarının global Product referansı kullanma sınırı;
6. şirket veri paketi için portable-reference ve collision gereksinimleri;
7. event/outbox, AI, ortak shell ve hata sözleşmesi bağımlılıkları;
8. implementation öncesi adversarial kabul matrisi.

## 3. Kapsam dışı

Bu doküman aşağıdakileri yapmaz:

- Product/Service entity veya tablo tasarlamaz;
- `CompanyId` alanını nullable yapmaz;
- mevcut `TenantEntity` sınıfına istisna eklemez;
- keyfî bir şirketi Product sahibi ilan etmez;
- permission string/manifest'i veya yeni rol kodu üretmez ve grant/assignment yapmaz;
- generic maker-checker/approval engine kurmaz veya onay eşiği seçmez;
- kod ön eki, sıra üreticisi veya alias migration'ı seçmez;
- API route, JSON alanı, hata kodu, cache ürünü veya paket seçmez;
- event adı, topic, broker, retry veya retention politikası seçmez;
- şirket export/import fingerprint alanlarını veya taşıma formatını kilitlemez;
- Service kapsamını Product kararından otomatik türetmez;
- kanonik `OPEN-*`, `DEC-*`, `ADR-*`, `AT-*`, task veya ekran kimliği üretmez;
- ChatGPT-CARI sahipliğindeki dosya, branch, domain veya production davranışını değiştirmez.

## 4. Normatif kaynaklar ve öncelik

| Kaynak | Bu taslağın tükettiği sınır |
|---|---|
| Issue #98 exact owner kararı [`issuecomment-5469061415`](https://github.com/sinangym/GoymenERP/issues/98#issuecomment-5469061415) | Tek group-shared Product UUID/kod/kart; company ayrımı fiziksel/ticari kayıtta başlar; global inactive iki şirkette yeni kullanımı kapatır; export aynı UUID'yi korur. |
| Issue #99 exact owner kararı [`issuecomment-5469937658`](https://github.com/sinangym/GoymenERP/issues/99#issuecomment-5469937658) | İlk pilotta Product/Service master read ve write yalnız mevcut `SYSTEM_ADMINISTRATOR`; diğer roller default-deny; yeni rol/assignment/AI yetkisi yok; generic maker-checker MVP'yi bloklamaz; exact permission string'leri ayrı versioned manifest ve naming/security review ister. |
| Issue #100 | Product epic'i schema/migration öncesi açık platform shared-master sözleşmesi ister. |
| `ADR-002` | Tenant kayıtlarında request-scoped CompanyContext, CompanyId query/write guard, non-disclosure ve server-side group read. |
| `ADR-004` | Dış kimlik yalnız authentication'dır; ERP permission ve scope yerel Identity kaynağından gelir; default-deny. |
| `ADR-009` | Append-only canonical audit zarfı, atomiklik, correlation ve şirket kapsamlı okuma. |
| `ADR-011` | Kalıcı idempotency, actor/payload binding, replay reauthorization, atomiklik ve optimistic concurrency. |
| `DEC-GOV-002` | Grup görünümü CompanyId güvenlik sınırını kaldırmaz. |
| `DEC-GOV-003` | Her şirket kendi geçmişi ve bağlı kayıtlarıyla taşınabilir olmalıdır. |
| `DEC-PRD-001…011` | Tek ürün, immutable kimlik, lifecycle, birim/paket/snapshot ve AI simülasyon sınırları. |

Çelişkide ürünün iş anlamını Product Owner kararı, platform güvenlik uygulamasını kabul
edilmiş ADR'ler belirler. Bu iki katmandan biri diğerini sessizce yeniden yorumlayamaz.
Issue yorumlarının kronolojik sırası karar üstünlüğü üretmez; yukarıda sabitlenen exact
Product Owner kararları normatiftir. Sonraki veya eski coordination/review yorumları bu
kararları `OPEN` durumuna döndüremez.

Issue #98 ve #99 bugün GitHub'da `CLOSED / KILITLI` olmakla birlikte exact karar metinleri
henüz repository karar siciline alınmamıştır. Issue #100 gövdesi de eski company-policy,
company-inactive, açık #98/#99 kapıları ve geniş PR #77 kapısını taşımaktadır. Bu taslak
#98/#99 owner yorumlarını kaynak kabul eder; repo karar kaydı ve Issue #100 truth-sync'i
Product Owner/ChatGPT-BUSINESS sahipliğinde tamamlanmadan executable delivery planı veya
schema başlangıç izni üretmez.

## 5. Terimler

### 5.1 Group-shared master

İki hukuki şirkette ortak kullanılan, şirket başına çoğaltılmayan kanonik iş kimliğidir.
Issue #98 kapsamındaki ilk örnek `Product`tır. Group-shared olmak anonim, public veya tüm
kullanıcılara görünür olmak anlamına gelmez.

### 5.2 Company-scoped record

Hukuki şirket sahipliği veya işlemsel/ fiziksel gerçeklik taşıyan kayıttır. Etiket, lot,
paket örneği, stok olayı/pozisyonu, sipariş satırı, mal kabul, üretim çıktısı, sevkiyat,
şirket fiyatı, vergi/muhasebe eşlemesi ve tesis paket standardı bu sınıftadır.

### 5.3 Originating CompanyContext

Global master komutunda kullanılabilecek aday causation girdisidir. Seçilirse komutun
hangi server-doğrulanmış şirket görünümünden başlatıldığını gösterir; **master sahipliği
değildir**. Global mutation'ın CompanyContext, group context veya ayrı platform scope ile
nasıl bağlanacağı henüz açık karardır ve kabul edilene kadar production mutation blokludur.

### 5.4 Group authorization scope

Group-shared masterı okuma veya değiştirme yetkisinin server tarafında çözülen kapsamıdır.
Birden fazla Company assignment'ının istemcide birleştirilmesi değildir. Pilot grantee ve
maker-checker semantiği Issue #99'da kilitlidir; exact permission string/manifest'i ve
group scope temsili açıktır. Group scope ayrıca server-owned, default-deny
bir authorization scope kaynağına bağlı olmalıdır; company assignment veya permission
tek başına group eligibility üretmez. Bu kaynağın ayrı membership kaydı, global
role/scope veya başka bir Identity modeli olması açık karardır.

### 5.5 Portable reference

Şirket veri paketi içinde global masterın yeni kopyasını üretmeden aynı immutable UUID ve
kanonik kimliğe referans veren, sürümlü ve doğrulanabilir tanımlayıcıdır.

## 6. Temel sınıflandırma değişmezi

Her kalıcı business kayıt persistence modeli kurulmadan önce tam olarak bir sınıfa atanır:

1. `GroupSharedMaster`
2. `CompanyScoped`
3. ayrıca açık platform kararı olan system/security metadata

Bir kayıt aynı anda hem group-shared hem tenant entity gibi davranamaz. Aşağıdaki
yaklaşımlar yasaktır:

- Product satırına boş `CompanyId` koyup mevcut tenant guard'ın bunu görmezden gelmesi;
- iki şirkete aynı Product UUID ile iki tenant satırı yazılması;
- Producta gerçekte sahip olmayan keyfî bir `OwnerCompanyId` atanması;
- `Guid.Empty`, magic company, `all`, wildcard veya istemci seçimiyle tenant filtresi
  atlanması;
- `IgnoreQueryFilters` ile normal HTTP group read/write yapılması;
- aynı global Productın client-side fan-out ile iki şirket endpoint'inden birleştirilmesi.

`ADR-002` içindeki CompanyId zorunluluğu company-scoped kayıtlarda aynen kalır. Group-shared
master yeni ve açık bir persistence/authorization sınıfıdır; mevcut tenant sözleşmesinin
gizli istisnası değildir.

## 7. Product Owner kararından gelen değişmezler

Platform uygulaması aşağıdaki semantiği değiştiremez:

1. Aynı ürün için tek immutable Product UUID ve tek kanonik görünen kod vardır.
2. Şirket başına Product/ProductAccount kopyası oluşturulmaz.
3. İlk sürümde Product iki Göymen şirketinde de varsayılan olarak kullanılabilir.
   Ayrı `CompanyProductPolicy` veya company-specific inactive zorunluluğu v1'e eklenmez.
4. Etiket, lot, fiziksel paket örneği, stok, sipariş, kabul, üretim ve sevkiyat kaydı kendi CompanyId
   değerini taşır ve global Producta referans verir.
5. Sipariş kaynaklı fiziksel kayıt şirketini siparişin server-doğrulanmış
   CompanyContext'inden alır.
6. Diğer kaynaklar şirketi kendi server-doğrulanmış belge/operasyon bağlamından alır.
7. İstemci body/query/route/header kombinasyonuyla kaynak şirketi serbestçe override edemez.
8. Ortak Product referansı şirketler arası stok sahipliği veya fiziksel hareket yetkisi
   üretmez. Transfer, emanet ve fason hareketleri ileride ayrı açık sözleşme gerektirir.
9. Global Product `Inactive` olduğunda iki şirkette de yeni sipariş, satın alma, stok ve
   üretim kullanımına kapanır; tarihsel kayıtlar görünür kalır. In-flight işlemlerin exact
   davranışı açık karardır.
10. Fiyat, paket standardı, vergi/muhasebe ve fiziksel stok kayıtları kendi şirket/tesis sahipliğini
   korur; global Product satırında tenant verisine dönüşmez.
11. Şirket veri paketi global Productı aynı UUID/kod ile referanslar; yeni UUID üretmez.

## 8. Kimlik ve yetkilendirme sınırı

### 8.1 Authentication yetki değildir

OIDC/JWT subject, e-posta, dış grup veya token claim'i group-shared Product erişimi
vermez. Kullanıcı önce `ADR-004` uyarınca aktif yerel ERP user olarak provision edilir.

### 8.2 Default-deny

Issue #99'un kilitli pilot matrisi uygulanır:

- Product/Service master read/list/detail ve bütün ilk-pilot write eylemleri yalnız mevcut
  exact `SYSTEM_ADMINISTRATOR` rolüne verilir;
- draft create/edit, activate/deactivate/reactivate, visible-code exception/alias,
  unit/conversion, technical-template, package-standard prepare/manage/approve ve
  bulk-import prepare/validate/apply bu sınırdadır;
- diğer mevcut, custom ve gelecekteki roller default-deny kalır;
- yeni `PRODUCT_MASTER_*` rolü veya otomatik user-role assignment üretilmez;
- AI ayrı read/write/activation yetkisi kazanmaz;
- manual visible-code exception/alias administration, daha sonra business rol kataloğu
  genişlese dahi yalnız exact `SYSTEM_ADMINISTRATOR` yetkisinde kalır;
- herhangi bir operasyon business user'ına Product write verilmeden önce Option-B tarzı
  editor/approver ayrımı ve görev ayrılığı (SoD) ayrı Product Owner kararıyla zorunlu
  olarak kabul edilir;
- price read/manage/simulate/approve ayrı gelecekteki Product Price sözleşmesidir ve bu
  kararla sessizce grant edilmez;
- exact permission string'leri Product-owned versioned manifest ve platform
  naming/security review olmadan registry/seed/reconciliation'a dönüşmez.

Bu listedeki package-standard prepare/manage/approve eylemleri yalnız ilk pilot grantee
semantiğini belirler. Company/plant-scoped paket standardı ve tesis istisnası global Product
grant'ine katlanmaz: kendi accepted exact permission/capability'si, server-resolved
`CompanyContext`, ABAC ve `CompanyId` guard'ı ayrıca geçmelidir. Global Product yetkisi
başka şirketin paket kaydını okuma, değiştirme veya varlığını çıkarma yetkisi üretmez.

Fresh install yalnız exact administrator role kabul edilmiş Product permission bundle'ını
alabilir; existing install yalnız eksik kabul edilmiş izinleri exact ID/code eşleşmesine
add-only, idempotent, duplicate-safe ve fail-closed reconciliation ile ekleyebilir. Eksik
administrator veya ID/code collision reddedilir; custom permission ve assignment korunur.
Production invocation/evidence Issue #62/#63 kapılarından geçmeden çalıştırılmaz.

Unknown permission, eksik scope, pasif user, süresi geçmiş assignment, eksik local user
veya çözülemeyen group scope sıfır erişim üretir.

Kabul edilecek group authorization modeli aşağıdaki değişmezleri korur:

- eligibility yalnız server-owned authoritative scope/policy üzerinden çözülür;
- wildcard, `all tenants`, config flag veya istemci claim'i group scope değildir;
- group scope ile Company assignment ayrı kararlardır ve ikisi de default-deny'dır;
- actor/company group eligibility veya scope/policy değişikliği yalnız kabul edilmiş
  permission + scope kullanan ayrı security command ile yapılır; current scope yeniden
  doğrulanır;
- scope/policy change command self-escalation'ı fail-closed reddeder ve kabul edilmiş
  SoD/approval kuralını, idempotency'yi, optimistic concurrency'yi ve atomik append-only
  audit'i uygular;
- authorization scope/policy revision, cache ve replay reauthorization girdisidir;
- scope dışındaki veya sonradan çıkarılan şirket/user eski cache, token ya da replay ile
  Product göremez.

Exact Group/GroupId şeması, membership/role/scope temsili, permission adı, approval
matrisi ve lifecycle bu taslakta seçilmez.

### 8.3 Selected-company ile group scope ayrımı

- Tek şirket görünümünde selected CompanyContext, kullanıcının request bağlamını ve
  company-scoped child kayıt görünürlüğünü sınırlar.
- Group görünümü magic CompanyId değildir; dedicated server-side read model, Issue #99
  Option A ve kabul edilmiş group scope sözleşmesi gerektirir.
- Bir şirkete atanmış olmak tek başına global Product write veya group read yetkisi vermez.
- İki şirkete ayrı ayrı atanmış olmak da istemcinin iki sonucu birleştirerek group access
  üretmesine izin vermez.
- Global authorization, audit ve idempotency context kararı kabul edilene kadar normal
  kullanıcı global master mutation'ı tamamen blokludur. Originating CompanyContext
  seçilirse yalnız causation kanıtıdır ve owner değildir; seçilmezse de ayrı group/system
  scope açık security kararı olmadan mutation açılamaz.
- Group görünümünden başlayan mutation, explicit action permission ve etkili authorization
  yeniden doğrulanmadan uygulanamaz.

### 8.4 Aynı karar bütün veri yollarında uygulanır

Authorization yalnız UI menüsü veya endpoint attribute'u değildir. Aynı scope kararı:

- liste;
- doğrudan ID;
- arama/autocomplete;
- count/aggregate;
- export;
- history/audit;
- mutation ve replay;
- event veya integration projection

yollarında server tarafında yeniden uygulanır.

## 9. Persistence ve referans sınırı

Bu taslak exact tablo/şema/DbContext seçmez. Implementation tasarımı şu kanıtları
sağlamalıdır:

1. Group-shared master satırı tenant global filter'a yanlışlıkla bağlanmaz.
2. Company-scoped child kayıt mevcut CompanyContext query/write guard'ından kaçamaz.
3. Tenant child kaydın global Product referansı, başka şirket child verisini görünür
   veya yazılabilir yapmaz.
4. Global Product FK'si CompanyId'yi child kayıttan düşürme gerekçesi değildir.
5. Child unique/index/FK kuralları kendi CompanyId'sini korur.
6. Product UUID ve görünen kod group-global unique davranır; exact code lifecycle ayrı
   kabul edilmiş sözleşme olmadan migration'a dönüşmez.
7. Product physical delete normal uygulama yolunda yoktur; lifecycle durumu tarihsel
   referansları korur.
8. Company-scoped snapshot, işlem anındaki Product açıklaması/kodu/birim/paket/fiyat
   bilgisini owner sözleşmesine göre saklar; global master değişimi geçmiş işlemi yeniden
   yorumlamaz.
9. Schema/migration, group-shared sınıfını tenant istisna switch'iyle değil açık model ve
   guard ile gösterir.
10. Normal runtime write ile bootstrap/migration/system write birbirinden ayrılır.

## 10. Query, arama, count ve cache

### 10.1 Server-side projection

Product liste ve araması group-shared master gerçeğini server tarafında çözer. V1 Product
kullanılabilirliği global lifecycle'dan gelir ve Product iki Göymen şirketinde varsayılan
olarak kullanılabilir; ayrı company policy üretilmez. Company-scoped projection yalnız
fiyat, paket standardı, stok ve diğer şirket kapsamlı bağlı gerçekleri ekler. Gelecekteki
company-specific kullanılabilirlik ancak ayrı D2 ürün kararıyla eklenebilir.

### 10.2 Non-disclosure

Yetkisiz kullanıcı:

- UUID veya kod tahminiyle global Product varlığını doğrulayamaz;
- unauthorized hit sayısını toplamdan çıkaramaz;
- başka şirketin fiyat/paket/stok/işlem varlığını rozet, boş alan, timing veya hata
  ayrıntısından anlayamaz;
- group mode kapalıyken hidden navigation/route üzerinden aynı veriye ulaşamaz.

Exact 403/404 ayrımı kabul edilmiş platform hata ve owner domain sözleşmesine bağlanır;
bu taslak yeni error code üretmez.

### 10.3 Cache fence

Cache anahtarı ve invalidation en az şu güvenlik girdilerini dikkate almalıdır:

- local actor/session;
- permission/policy revision;
- selected CompanyContext veya açık group scope;
- query/filter/sort/page/culture;
- Product lifecycle/version;
- authoritative group authorization scope/policy revision;
- company-scoped projection kullanılıyorsa ilgili company ve projection revision.

Browser persistent/shared intermediary cache, actor veya şirketler arasında Product ve
child projection replay edemez. Context/permission değişimi pending request ve stale
response'u generation fence ile düşürür.

## 11. Write ve lifecycle sınırı

Global Product write için en az şu sıra korunur:

1. local actor ve session doğrulama;
2. kabul edilmiş global authorization/audit/idempotency context doğrulama;
3. Issue #99 Option A ile kabul edilmiş action permission/scope;
4. güncel reauthorization ve tanımlıysa impact preview; generic maker-checker ancak ayrı
   sonraki ürün kararıyla;
5. canonical request ve idempotency scope;
6. expected version / optimistic concurrency;
7. domain invariant ve downstream impact validation;
8. atomic business + audit + terminal idempotency result;
9. kabul edilirse transactional outbox;
10. güvenli response ve correlation kanıtı.

Global `Inactive` iki şirketin yeni seçimini kapattığı için etkisi company-local bir durum
değişikliğinden daha geniştir. İlk pilotta activation/deactivation/reactivation yalnız
`SYSTEM_ADMINISTRATOR`, mandatory audit, optimistic concurrency ve tanımlıysa impact
preview ile çalışır; generic ikinci Product approver MVP önkoşulu değildir. AI veya
frontend bu durumu doğrudan değiştiremez.

Pilot sonrasında operational business writer açılması, editor/approver ayrımı ve SoD
kararı kabul edilmeden yapılamaz. Manual visible-code exception/alias administration bu
genişlemede dahi `SYSTEM_ADMINISTRATOR` dışına taşınamaz.

## 12. Audit ve correlation

### 12.1 Semantik gereksinim

Global Product değişikliğinin audit kaydı en az şunları ayırt etmelidir:

- değişen subject'in `GroupSharedMaster` olduğu;
- Product UUID ve önce/sonra version;
- actor, etkili role/permission ve approval bağlamı;
- kabul edilen context modeline göre group scope ve varsa originating CompanyContext;
- varsa originating company'nin Product owner'ı olmadığı;
- önce/sonra canonical snapshot, reason, AI involvement ve source channel;
- correlation/causation ve idempotency key hash;
- canonical envelope integrity hash.

Actor, role/permission, originating context, approval, AI involvement, source channel,
correlation ve causation alanları server-authoritative kaynaktan gelir; body/header bu
provenance'i ilan veya yükseltemez. Yalnız owner sözleşmesince izin verilen kullanıcı
gerekçesi doğrulanmış input olabilir. Zarf hash'ten önce `ADR-009` biçiminde canonicalize
edilir ve kalıcı satırdan yeniden üretilebilir olmalıdır.

### 12.2 Mevcut audit şemasıyla çakışma

`ADR-009` audit zarfında `CompanyId` zorunludur ve audit read company-wide permission ile
seçili şirketten yapılır. Bu alanı global Product ownership gibi yorumlamak yanlıştır.
Schema/migration başlamadan önce audit sözleşmesi şu soruları açıkça kapatmalıdır:

1. varsa originating company ile subject scope nasıl ayrı temsil edilir?
2. global master audit'ini hangi kabul edilmiş permission + scope matrisi okur?
3. iki şirket için aynı audit olayının kopyalanması nasıl engellenir?
4. selected-company audit okuyucusu global değişikliği hangi kuralla görür veya görmez?
5. portability paketinde global master audit referansı nasıl taşınır?

Bu sorular kapanmadan mevcut `AuditEvent.CompanyId` alanına keyfî şirket yazmak veya aynı
olayı iki kez üretmek yasaktır.

### 12.3 Atomiklik

Başarılı/no-change kritik Product mutation, audit insert ve terminal idempotency sonucu
aynı transaction'da commit/rollback olur. Audit insert başarısızsa Product değişmez;
Product write başarısızsa başarılı audit uydurulmaz. Teknik telemetry business audit'in
yerini alamaz.

## 13. Idempotency ve concurrency

### 13.1 Mevcut scope farkı

`ADR-011` command identity'sini `(CompanyId, CommandName, IdempotencyKeyHash)` ile
tanımlar. Bu tenant key'i global Product komutuna selected/origin CompanyId üzerinden
doğrudan taşınırsa, aynı mantıksal master için iki bağımsız başarı üretebilir. Bu nedenle
mevcut key global command scope olarak aynen yeniden kullanılamaz.

Product write başlamadan idempotency sözleşmesi global subject için açık bir command
scope tanımlamalıdır. Bu scope:

- arbitrary owner CompanyId kullanamaz;
- `Guid.Empty/all` kullanamaz;
- iki tenant command result kopyası üretemez;
- actor/payload/version binding'i zayıflatamaz;
- permission ve current scope reauthorization'ı replay öncesinden kaldıramaz;
- kabul edilen request origin/context kanıtını kaybedemez.

Accepted context selected-company origin taşıyorsa cross-origin replay sözleşmesi iki
güvenli modelden birini açıkça seçip kanıtlar:

1. origin binding farklı originating CompanyContext için replay'i fail-closed reddeder;
2. stored response yalnız group-safe alanlardan oluşur ve başka origin'e company-scoped
   child projection, original-context metadata veya original correlation açıklamaz.

Her iki modelde de kalıcı sonucu okumadan önce aktif local user, action permission, group
authorization scope/policy revision ve hedef scope yeniden doğrulanır. Kabul edilmiş command scope bir
originating CompanyContext'e bağlıysa o Company assignment ayrıca yeniden doğrulanır.
Origin taşımayan kabul edilmiş group/system scope ise kendi scope kaynağıyla doğrulanır;
sahte origin veya owner company üretilmez.
Yetki/group authorization scope/Company assignment yürütme ile replay arasında
kaldırılmışsa eski sonuç açılmaz.

Exact storage key, schema alanı veya migration bu taslakta seçilmez.

### 13.2 Yarış davranışı

Kabul testleri en az şu yarışları kapatmalıdır:

- aynı key + aynı actor/payload + iki originating company;
- aynı key + farklı actor;
- aynı key + farklı payload veya expected version;
- farklı key + aynı expected version;
- activate/deactivate yarışları;
- visible code/alias üretim yarışı;
- audit veya idempotency insert failure rollback'i;
- timeout sonrası unknown outcome ve güvenli replay.

Tek global Product version/rowversion tek kazanan üretmelidir. Replay hiçbir zaman ikinci
business, audit veya event etkisi üretmez.

## 14. System, migration ve integration write

Mevcut `OrganizationBootstrapWriteScope` group-shared Product için genel bypass olarak
yeniden kullanılamaz. Her non-user write yolu:

- explicit ve dar system identity;
- izin verilen command/use-case allowlist'i;
- kısa yaşam süresi ve request dışı invocation sınırı;
- aynı domain invariants;
- append-only audit/security evidence;
- idempotency/concurrency;
- dry-run/preview ve failure davranışı;
- production activation için ayrı insan kapısı

taşır. Migration veri backfill'i business activation veya otomatik user assignment
üretemez.

System-write authority yalnız trusted infrastructure sınırından doğmalı, dışarıdan forge
edilememeli, dar allowlist ve bounded lifetime sonrasında fail-closed kapanmalıdır. HTTP,
JWT/claim, AI tool, request header, environment/config flag veya public factory bu
authority'yi üretemez. Authority `IgnoreQueryFilters`, authorization, audit, idempotency
veya concurrency bypass'ı vermez. Bunun disposable capability object, scoped dispatcher
token'ı veya başka bir mekanizma olarak uygulanması bu taslakta seçilmez. Canonical olmayan
invocation, scope taşması ve authority expiry/closure sonrası kullanım negatif
architecture/runtime testleriyle reddedilir.

## 15. Transactional event ve downstream tüketim

Global Product lifecycle veya etkili alan değişikliği satış, satın alma, stok ve üretim
seçicilerini etkileyebilir. Bunun güvenilir yayını ERP-000F kabul edilmeden doğrudan
in-process callback, background fire-and-forget veya best-effort HTTP ile yapılamaz.

Event sözleşmesi kabul edilmeden önce şunlar açıktır:

- global subject scope ve originating company ayrımı;
- hangi şirket/consumer projection'larının etkilendiği;
- event version ve compatibility;
- authorization/non-disclosure;
- duplicate delivery ve inbox dedup;
- inactive Productın pending downstream işlemlere etkisi;
- replay/backfill ve portability ilişkisi.

Bu doküman event adı, broker, topic veya retry eşiği seçmez.

Product lifecycle için cache, UI state, event veya outbox authority değildir. Yeni bir
sipariş, satın alma, stok veya üretim business commit'i, stale `Active` kararına izin
vermeyen kabul edilmiş authoritative consistency/fence protokolü olmadan tamamlanamaz.
Bu protokolün transactional query, database fence, version token/barrier veya başka bir
yöntem olması bu taslakta seçilmez. Exact in-flight iptal/devam politikası açık kalabilir;
fakat bu politika kabul ve yarış testleriyle kapanmadan bağımlı production write açılmaz.

## 16. Şirket veri paketi ve portable reference

Issue #98 uyarınca şirket export'u Productı yeni UUID ile çoğaltmaz. Paket en az şu
semantiği korumalıdır:

1. global Product immutable UUID ve kanonik kodu portable reference olarak bulunur;
2. yalnız seçilen şirketin fiyat/paket/etiket/lot/stok/işlem/snapshot bağlantıları dahil
   edilir;
3. başka şirketin child kaydı, toplamı veya audit detayı pakete sızmaz;
4. import, client-supplied CompanyId ile hedef kapsamı genişletmez;
5. aynı Product yeni UUID veya sessiz duplicate kartla yaratılmaz;
6. UUID/kod/fingerprint collision sessizce overwrite, merge veya re-key yapmaz;
7. global master mutation ile company-scoped child import ayrı authorization ve audit
   etkileridir;
8. restore/import idempotent ve tekrar çalıştırılabilir olur;
9. paketin hash, version, source ve signature/provenance gereksinimi ERP-000N/ilgili kabul
   sözleşmesiyle kanıtlanır.

Exact fingerprint alanları, same-UUID/different-content ve different-UUID/same-code
kararları kabul edilmeden import migration yazılamaz. Güvenli varsayılan fail-closed'dur;
otomatik duplicate veya overwrite değildir.

## 17. AI sınırı

AI:

- Productı açıklayabilir, mükerrer adayı gösterebilir, taslak/simülasyon hazırlayabilir;
- group scope, permission, approval veya CompanyContext üretemez;
- Product lifecycle ya da kod/alias istisnasını doğrudan uygulayamaz;
- company-scoped stok/fiyat/paket verisini yetkisiz group yanıtına taşıyamaz;
- global write için normal preview, impact, permission, approval, idempotency, concurrency,
  audit ve outbox zincirini atlayamaz;
- provider/model cevabını ERP iş doğrusu veya kalıcı master olarak yazamaz.

ERP-000J capability/tool contract kabul edilmeden gerçek AI Product tool'u açılmaz.

## 18. Ortak shell, route ve capability

- Product navigation görünürlüğü backend permission yerine geçmez.
- Group görünümü shell'de magic CompanyId veya client union oluşturmaz.
- Route registry aynı owner/version için tek declaration kaynağı kullanır.
- Direct import/self-registration capability ve mount kapısını aşamaz.
- Selected company/group context değişiminde open form, cache ve pending request güvenli
  biçimde fence edilir.
- Product route/action ayrımı ERP-000K contract kabul edilmeden production'a bağlanmaz.
- Mock stok/fiyat/AI sonucu gerçek Product 360 verisi gibi gösterilmez.

## 19. Hata ve bilgi sızıntısı

Platform-owned 401/403/500/503 ile Product-owned validation/not-found/conflict hataları
ayrı sahiplikte kalır. Kabul edilmiş stable error contract olmadan Türkçe title/detail
machine code yerine kullanılamaz.

Public hata gövdesi veya log:

- foreign CompanyId;
- yetkisiz Product UUID/kod;
- başka şirket fiyat/stok/paket varlığı;
- SQL/constraint/stack/token;
- permission veya role envanteri;
- portable package içeriği

sızdıramaz. Correlation kimliği authorization değildir.

## 20. Adversarial kabul matrisi

Aşağıdaki etiketler bu dokümana özeldir; kanonik global `AT-*` kaydı değildir.

| Senaryo | Beklenen sonuç |
|---|---|
| Kimliği doğrulanmış fakat local ERP user olmayan actor | Group Product liste/ID/write/export etkisi `0`. |
| Bir şirkete erişimi olan fakat Product permission olmayan actor | Product varlığı, count ve child projection sızmaz; write/audit/idempotency etkisi `0`. |
| Yalnız A, yalnız B, iki şirket ve hiçbir şirkete atanmamış actor matrisi | Her kombinasyon accepted group permission/scope ile server-side değerlendirilir; company assignment tek başına global yetki üretmez. |
| Kabul edilmiş group scope dışındaki company/actor veya scope kaldırıldıktan sonra eski token/cache | Product, count, export, audit ve replay görünürlüğü `0`; scope/policy revision stale cevabı düşürür. |
| Yetkisiz/self-escalating group scope change, same-key replay veya aynı-version yarışı | Authorization etkisi ve Product erişimi `0`; tek yetkili kazanan varsa security audit + idempotency sonucu atomiktir. |
| Token/body/query ile sahte group/company claim | Server-local scope değişmez; etki `0`. |
| Producta keyfî OwnerCompanyId verme denemesi | Model/migration/command fail-closed; satır üretilmez. |
| Productı iki şirket satırı olarak çoğaltma | Global unique/identity contract reddeder. |
| Ortak Product referansıyla şirketler arası stok/hareket oluşturma | Global master erişimi ownership veya transfer yetkisi üretmez; kabul edilmiş transfer/emanet/fason sözleşmesi yoksa etki `0`. |
| Group read'i iki tenant endpoint'ini client-side birleştirerek kurma | Architecture/E2E gate reddeder. |
| Authorized Product read + unauthorized company stock/fiyat/paket | Product cevabı foreign child existence veya toplamını açıklamaz. |
| Direct UUID/kod tahmini | Liste, ID, search, count ve timing aynı authorization sınırını uygular. |
| Company/group context hızlı değişimi | Eski response/cache/render yeni context'e düşmez. |
| Global Inactive | İki şirkette yeni sipariş, satın alma, stok ve üretim kullanımı reddedilir; tarihsel snapshot görünür kalır; in-flight davranış açık kararı bekler. |
| Selector/cache Active gösterirken Product eşzamanlı Inactive olur | Kabul edilmiş commit-time consistency/fence stale Active kararını engeller; exact mekanizma açık kalır, UI/cache/outbox otorite sayılmaz. |
| Same-key aynı komut iki originating company'den | Tek global terminal etki; exact replay scope sözleşmesi kanıtlanır. |
| A origin'de yürütülen sonucu B origin'de replay etme | Kabul edilen origin-binding veya group-safe response modeli uygulanır; A child projection/original context/correlation sızmaz. |
| Kabul edilmiş no-origin group/system command replay'i | Accepted group/system scope yeniden doğrulanır; keyfî CompanyId/origin/owner sentezlenmez ve response yalnız group-safe alan taşır. |
| Permission, group authorization scope veya Company assignment yürütme ile replay arasında kaldırılır | Kalıcı sonuç okunmadan reauthorization reddeder; replay/original-correlation açıklanmaz. |
| Same-key farklı actor/payload | Conflict; eski response veya foreign context açıklanmaz. |
| Farklı-key aynı version yarışı | Tek kazanan; diğer terminal version conflict; ikinci business/audit/event yok. |
| Audit insert failure | Product + audit + idempotency + outbox birlikte rollback. |
| Idempotency insert failure | Product + audit + result + outbox birlikte rollback. |
| Outbox insert failure | ERP-000F atomiklik kararı uyarınca başarılı Product sonucu uydurulmaz. |
| Export company A | B child kayıtları/count/audit detayları yok; Product UUID portable reference olarak korunur. |
| Import same UUID collision | Kabul edilmiş fingerprint/collision politikası olmadan fail-closed; re-key/overwrite yok. |
| Same UUID/different content, same code/different UUID, alias, newer target veya farklı group collision | Her sınıf explicit policy olmadan fail-closed; sessiz merge/overwrite/UUID değişimi yok. |
| System/migration invocation | Explicit system scope dışındaki normal bypass reddedilir; user assignment/activation yok. |
| HTTP/JWT/AI/config ile system authority üretme veya expiry/closure sonrası kullanma | Trusted infrastructure boundary fail-closed reddeder; auth/audit/idempotency/query guard atlanmaz. |
| AI draft veya tool çağrısı | Preview/onay/permission/audit zinciri olmadan write etkisi `0`. |
| Forged actor/role/approval/AI/source/correlation provenance | Client alanları audit zarfına güvenilir kaynak olarak girmez; canonical persisted hash server-authoritative değerlerle doğrulanır. |

Gerçek implementation acceptance; unit/architecture/HTTP, gerçek SQL Server, migration,
yarış/failure injection, export/import round-trip, UI accessibility/visual ve container
katmanlarını içerir. In-memory provider tenant/FK/concurrency kanıtı olarak yeterli değildir.

## 21. Açık kararlar

Bu taslak aşağıdaki kararları görünür tutar ve kapatmaz:

1. Issue #98/#99 owner kararlarının repo karar siciline exact ve izlenebilir biçimde alınması.
2. Product-owned versioned permission manifest'i, exact permission string'leri ve platform
   naming/security review'u; Product Price yetki sözleşmesi ayrı kalır.
3. Issue #100 epic gövdesindeki stale company-policy/#98/#99/#77 kapılarının owner tarafından
   truth-sync edilmesi.
4. Group authorization scope'un Identity modelinde nasıl temsil edileceği.
5. Global Product mutation için originating CompanyContext ve varsa platform system scope
   sözleşmesinin exact biçimi.
6. Group-shared master persistence owner'ı, schema/DbContext ve migration sırası.
7. Audit zarfında global subject scope ile originating company ayrımı ve audit read modeli.
8. ADR-011 için global command idempotency scope ve replay semantiği.
9. Product code prefix/sequence/alias lifecycle ve collision kuralları.
10. Portable-reference fingerprint, import collision ve provenance formatı.
11. Global lifecycle event audience/version ve ERP-000F consumer sözleşmesi.
12. Product inactive olurken in-flight sipariş/üretim/stok işlemlerinin exact davranışı.
13. Service identity, code, company scope ve lifecycle kararı.
14. U-01…U-10 unresolved/candidate UI eşlemeleri.
15. Production activation, provider/topology, retention ve performans eşikleri.

## 22. Bağımlılık ve implementation kapıları

| Kapı | Durum | Bu sözleşmedeki sonuç |
|---|---|---|
| Issue #98 `PRD-SCOPE-001` | `KILITLI / CLOSED` | Ürün semantiği tüketildi; yeniden açılmaz. |
| Issue #98/#99 repository decision record | `OPEN / OWNER TRUTH-SYNC` | Exact owner kararları repo siciline alınmadan executable plan/schema yok. |
| Issue #100 epic truth-sync | `OPEN / OWNER TRUTH-SYNC` | Eski company-policy, #98/#99 ve geniş #77 kapıları düzeltilmeden epic uygulama kaynağı değildir. |
| Issue #99 `PRD-PERM-001` | `KILITLI / CLOSED` | Option A pilot grantee/default-deny/maker-checker semantiği tüketildi; exact strings/manifest hâlâ kapıdır. |
| Product permission manifest + naming/security review | `OPEN` | Seed/reconciliation/API authorization implementation bloklu. |
| Group-shared persistence ADR | `OPEN` | Product schema/migration bloklu. |
| Audit global-subject extension | `OPEN` | Product mutation bloklu. |
| Global idempotency scope | `OPEN` | Product mutation bloklu. |
| ERP-000F outbox/inbox | Readiness/karar kapıları açık | Cross-module event production bloklu. |
| ERP-000K shell capability/route | Draft/open contract | Product route/mount bloklu. |
| ERP-000N portability/restore | Readiness/karar kapıları açık | Export/import production kanıtı bloklu. |
| ERP-000M SQL/migration evidence | Implementation blocked | Schema/load/restore kanıtı bloklu. |
| Platform stable error contract | Draft human review | Shared error production bağımlılığı açık. |
| Issue #87 | Runner kullanılabilir; enforcement/reviewer açık | Exact-head CI zorunlu, kritik merge insan kapısında. |

## 23. Yasak kısa yollar

- accepted versioned permission manifest/naming review ve group scope sözleşmesi olmadan
  `SYSTEM_ADMINISTRATOR` dahil herhangi bir role physical grant/reconciliation yazmak;
- CompanyId'yi nullable/global flag ile sessizce geçmek;
- Productı bir şirketin tenant satırı yapıp diğer şirkete paylaşmak;
- şirket başına Product kopyası üretmek;
- mevcut Organization bootstrap bypass'ını Product runtime'a taşımak;
- audit CompanyId'sini owner gibi yorumlamak;
- iki global audit/idempotency/event kopyası üretmek;
- client-side group union, count veya cache kullanmak;
- in-memory testle relational isolation/concurrency kanıtı iddia etmek;
- export/import collision'da otomatik merge, overwrite veya UUID değiştirmek;
- AI'ya permission, scope veya direct write vermek;
- Product kararından Service davranışını otomatik türetmek;
- bu DRAFT'ı implementation GO veya kabul edilmiş ADR gibi göstermek.

## 24. Review ve definition of ready

Bu doküman ancak aşağıdaki incelemelerden sonra bağlayıcı ADR/acceptance girdisine
dönüştürülebilir:

1. Product Owner, Issue #98/#99 semantiğini repository karar siciline exact ve izlenebilir
   biçimde taşır; Issue #100 gövdesini aynı gerçekle truth-sync eder.
2. Product Owner, Issue #98/#99 semantiğinin bu sözleşmede eksiksiz ve yeniden yorumlanmadan
   taşındığını doğrular.
3. Security reviewer, default-deny, non-disclosure, group scope ve system-write sınırını
   kabul eder.
4. Identity/Product owners, exact versioned permission manifest/naming review ve group
   scope modelini kabul eder; Issue #62/#63 invocation/evidence kapılarını korur.
5. Audit/Idempotency owners, global subject/command scope extension'larını kabul eder.
6. Data/migration owner persistence ve portable-reference modelini gerçek SQL/restore
   acceptance ile bağlar.
7. ERP-000F owner event audience/atomiklik/dedup sınırını kabul eder.
8. ERP-000K/UI owner route, mount, context-switch ve cache fence'i kabul eder.
9. Exact-head standard CI, real SQL, container ve UI golden gerçek runner adımlarıyla
   yeşil olur.
10. Kritik architecture/security PR insan tarafından review edilip merge kararı alır.

Bu kapılar tamamlanana kadar sonuç:

```text
CONTRACT: DRAFT
PRODUCT DECISION #98: CONSUMED / LOCKED
PRODUCT PERMISSION #99: CONSUMED / LOCKED
EXACT PERMISSION MANIFEST/NAMING: OPEN
SCHEMA/MIGRATION: BLOCKED
PRODUCTION WRITE: NO-GO
PRODUCTION READ/ROUTE: NO-GO
AUTO-MERGE: FORBIDDEN
```
