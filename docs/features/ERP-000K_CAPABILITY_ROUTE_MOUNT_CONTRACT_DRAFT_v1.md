# ERP-000K — Capability, Route ve Feature-Mount Contract Taslağı v1

- Durum: `DRAFT / OPEN / HUMAN REVIEW REQUIRED`
- Bağlı üst sözleşme: `ERP-000K-SHELL-CONTRACT` v1
- ERP-000K implementation: `BLOCKED`
- Production activation: `NO-GO`
- İncelenen taban: `main@83197c50f515db959638fadd7cc11ad6eb7a4fc8`
- Sorumluluk: `CODEX-PLATFORM` generic shell zarfı; feature içeriği ilgili owner modülde
- Contract kapanışı: `NONE`

## 1. Amaç

Bu taslak, ortak React kabuğunun capability, route ve feature-mount sınırlarını owner
modüllerin iş anlamını sahiplenmeden review edilebilir hale getirir. Aşağıdaki üç soruyu
birbirinden ayırır:

1. Sunucu, belirli kullanıcı ve güncel bağlam için bir UI yeteneğinin kullanılabilirliğini
   hangi güvenlik değişmezleriyle bildirir?
2. Ortak shell, yalnız kabul edilmiş feature route tanımlarını hangi generic kurallarla
   kaydeder ve açar?
3. Feature owner ile shared shell owner arasındaki mount ve dosya sahipliği nasıl tek
   yazarlı kalır?

Bu dosya exact endpoint, JSON şekli, router/query/cache/component paketi, business route,
permission adı, rol, cache anahtarı, timeout, breakpoint, eşik veya production davranışı
seçmez. Yeni kanonik `OPEN-*`, `AT-*`, `DEC-*`, ADR, ekran veya görev kimliği üretmez.

## 2. Bağlayıcı kaynak ve sınır

Kaynak önceliği kök `AGENTS.md` ile aynıdır. Bu taslak özellikle şunları uygular:

- `ERP-000K-SHELL-CONTRACT` v1 bölüm 8–10: server-backed capability, route registry ve
  feature-mount sınırı;
- aynı sözleşme bölüm 13–17: CompanyContext fence, güvenli hata/session davranışı ve
  owner feature extension sınırları;
- aynı sözleşme bölüm 20–21: contract kapanış kapıları ile şimdilik izinli docs-only iş;
- `DEC-GOV-002/004/007`: grup görünümü tenant sınırını kaldırmaz; eksik feature kapalıdır;
  inert menü gösterilmez;
- `DEC-ORG-001`: Company, Site, Location ve WorkCenter ayrı kapsam kavramlarıdır;
- `DEC-TEC-002/005/009/010/011`: React/TypeScript, backend reauthorization,
  correlation, cihaz/tarayıcı kabulü ve package/TCO inceleme sınırı;
- `DEC-TST-001/002`: ekranın açılması kabul değildir; security, accessibility ve gerçek
  kullanıcı akışı kanıtı gerekir;
- Master v3 bölüm 5, 6 ve 26 ile `docs/ui/UI_DESIGN_SYSTEM.md`.

Bu taslak üst sözleşmeyi kapatmaz veya genişletmez. Çelişkide kanonik karar sicili,
Master, kabul edilmiş feature/ADR ve daha yeni kullanıcı kararı geçerlidir.

## 3. Current-main envanteri

İncelenen tabanda doğrulanan mevcut durum:

- `src/frontend/goymen-erp-web/package.json` yalnız React ve ReactDOM runtime
  dependency'lerine sahiptir; router, shared query/cache, form, grid veya browser-E2E
  paketi yoktur.
- `src/frontend/goymen-erp-web/src/App.tsx` tek bootstrap shell'dir. Yalnız
  `#dashboard` bağlantısı gerçektir; diğer modül adları etkileşimsiz metindir.
- Aynı bootstrap, yetkili şirket listesini yükler; seçilen şirket kimliğini localStorage
  başlangıç tercihi olarak saklar ve organization isteklerinde `X-Company-Id` yollar.
- `AbortController` kullanılır; ancak request-generation fence, company-scoped shared
  cache, dirty form, pending write veya permission-revocation lifecycle'ı yoktur.
- `src/frontend/goymen-erp-web/src/api/organizationApi.ts`, hataları yalnız HTTP status
  metniyle yükseltir; sürümlü ortak UI error contract tüketmez.
- `GET /api/v1/identity/me`, `UserId`, `DisplayName`, `Email` ve
  `AllowedCompanyIds` döndürür. Permission, ABAC scope, capability veya feature
  availability kaynağı değildir.
- `GET /api/v1/organization/companies`, kullanıcının yetkili şirket özetlerini döndürür;
  route/action capability registry değildir.
- Organization'ın company-scoped endpointleri `RequireCompanyContext` ve backend
  permission ile korunur. Bu koruma client görünürlüğüyle ikame edilemez.
- UI kataloğundaki 159 kanonik ekran kaydı, 159 production route veya menü öğesi demek
  değildir.
- ERP-000K manifestte `blocked`; üst `ERP-000K-SHELL-CONTRACT` fiziksel olarak vardır
  fakat `OPEN`, implementation `BLOCKED`, production `NO-GO` durumundadır.

Bu envanter yalnız geriye düşmeme tabanıdır; implementation veya package seçimi değildir.

## 4. Terimler ve otorite sınırı

### 4.1 Capability

Capability, sunucunun güncel kullanıcı, session ve açık bağlam için bir UI yeteneğinin
sunulabilir olduğunu bildiren, sürümlü ve fail-closed bir availability kararıdır.

Capability:

- backend authorization yerine geçmez;
- client tarafından rol/JWT metni, menü, HTTP probing veya hata mesajından türetilmez;
- iş kaydının okunabileceğini veya değiştirilebileceğini tek başına garanti etmez;
- permission, ABAC scope, SoD, expected version, approval veya domain kuralı üretmez;
- eksik, unknown, malformed, stale ya da yanlış bağlama aitse kapalı sayılır.

### 4.2 Route declaration

Route declaration, kabul edilmiş bir feature ekranına deep-link ve shell davranışı veren
owner-supplied kayıttır. Route'un iş anlamı ve permission gereksinimi feature owner'a;
registry şeması, collision kontrolü ve generic navigation güvenliği shell owner'a aittir.

### 4.3 Feature mount

Feature mount, owner feature'ın kabul edilmiş route ve capability girdileriyle ortak shell
içinde görünür hale geldiği entegrasyon sınırıdır. Mount olmak, feature'ın backend
authorization veya acceptance kapılarını geçtiği anlamına gelmez; bu kanıtlar ayrıca
gerekir.

### 4.4 Availability ve authorization ayrımı

```text
server-backed availability
→ shell görünürlük ve navigation kararı
→ route loader/action sırasında güncel backend authorization
→ owner API/domain validation
```

Bu zincirin hiçbir adımı bir sonraki adımı atlayamaz. Menüde gizlemek, disabled control
veya client route guard güvenlik sınırı değildir.

## 5. Generic capability zarfı değişmezleri

Exact serialization daha sonra kabul edilecek olmakla birlikte capability çözümü en az şu
semantiği kanıtlamalıdır:

- response contract ve policy sürümü;
- server tarafından doğrulanmış actor/session bağı;
- tek şirket, açık yetkili çok-şirket okuma veya context-free sınıfından yalnız biri;
- tek şirketliyse aktif CompanyContext bağı;
- gerekiyorsa Site/Location/WorkCenter kapsamının owner permission contract'ına bağlılığı;
- owner feature/contract sürümü ve implementation availability;
- capability'nin güncellik/yenileme sınırı;
- server-derived revision/generation ile eski veya sırası bozulmuş cevabı reddedebilme;
- unknown veya desteklenmeyen capability için fail-closed sonuç;
- kararın hangi server-owned registry/policy sürümünden üretildiğine güvenli referans.

Zarf business label, menu sırası, sayı, KPI, record existence veya yabancı tenant bilgisi
sızdırmamalıdır. Permission isimlerinin ya da scope içeriğinin client'a nasıl ve ne kadar
açılacağı ayrıca security review ister; bu taslak payload biçimi seçmez.

`context-free` genel bir kaçış yolu değildir. Yalnız kabul edilmiş server-owned platform/
system route allowlist'i bu sınıfı kullanabilir. Tenant veya business verisi; CompanyId,
record kimliği, count, recent/favorite ya da company-scoped cache taşıyan owner route
`context-free` ilan edilemez. Feature owner'ın kendi declaration'ıyla bu sınıfa geçmesi
validator tarafından reddedilir; tanımsız route default olarak context gerektirir.

### 5.1 Capability kaynağı olmayan girdiler

Şunların hiçbiri capability kaynağı değildir:

- localStorage/sessionStorage;
- feature'ın frontend bundle içinde bulunması;
- kullanıcı rol adının client'ta görülmesi;
- prototipteki menü veya route;
- HTTP 200/403/404 deneyerek feature keşfi;
- eski capability response'u, favorite, recent, cache veya açık browser sekmesi;
- model/AI çıktısı;
- development identity/header veya environment badge.

### 5.2 Yeniden doğrulama ve geçersizleştirme

Eski capability en az şu olaylarda yeniden kullanılamaz:

- login, logout, kullanıcı veya external identity değişimi;
- session yenileme, expiry veya token/claim dönüşümü;
- CompanyContext ya da açık multi-company-read context değişimi;
- permission, role assignment, ABAC scope, SoD veya feature availability değişimi;
- route/owner contract ya da capability policy sürümü değişimi;
- background dönüşü, direct refresh veya güvenli freshness sınırının aşılması;
- malformed/partial response veya capability service arızası.

Client yalnız en güncel isteğin, güncel actor/session/context ve beklenen policy/registry
generation'ının cevabını uygular. Yeni bir kararı eski revision ile geri alma, geç gelen
response'u yeni context'e uygulama veya aynı context içindeki grant/revoke yarışında eski
grant'i yeniden canlandırma reddedilir. Exact revision ve taşıma mekanizması açık karardır;
anti-rollback ve request-generation fence kabul zorunluluğudur.

Geçersiz capability ile feature açık bırakılmaz. Denied, unregistered, unknown veya hiç
uygulanmamış feature; yetkisiz kullanıcıya birbirinden ayırt edilebilir menu, route, title,
count ya da hata üretmez. Yalnız daha önce server tarafından bu session/context için
yetkilendirilmiş bir feature'ın geçici çalışma arızası, veri veya permission ayrıntısı
sızdırmayan generic unavailable state gösterebilir. Normal ERP shell'i güvenli çalışır;
sessiz geniş yetki fallback'i yoktur.

Capability/session/scope kaybında protected mount kaldırılır; actor/session/context
kapsamlı hassas view ve cache temizlenir; devam eden read cevapları generation fence ile
düşürülür. Browser history, title, breadcrumb, telemetry ve fallback eski hassas veriyi
taşımaz. Sonucu bilinmeyen pending write körlemesine yeniden gönderilmez; ERP-000E'nin
idempotency/result-reconciliation sınırı kullanılır.

Kesin logout, kullanıcı/session değişimi, CompanyContext değişimi veya doğrulanmış revoke
durumunda unmount ve hassas state purge zorunludur. Geçici capability-service arızasında
yeni load/action ve hassas render fail-closed kalır; aynı actor/session/context'e bağlı
dirty form state'i veri kaybını önlemek için yalnız process-memory içinde, görünmez ve
işlem üretemez halde tutulabilir. Taze server capability ve route/API reauthorization
başarılı olmadan yeniden mount edilmez; bağ değişirse bu volatile state de temizlenir.

### 5.3 Transport ve storage sınırı

Capability envelope yetkiyle ilişkili hassas security metadata'dır. Browser persistent
storage'a (`localStorage`, `sessionStorage`, IndexedDB veya Service Worker Cache Storage)
yazılamaz; public/CDN/shared proxy cache'te tutulamaz ve başka kullanıcı/session/context'e
aktarılmaz. Client yalnız current in-memory actor/session/context generation'ına bağlı
snapshot'ı kullanabilir ve kesin bağ değişiminde temizler.

HTTP taşıma private ve paylaşılmaz olmalıdır. Exact header seti ayrı security review'da
seçilir; `no-store`/`private`/doğru `Vary` veya aynı güvenlik sonucunu kanıtlayan eşdeğer
contract olmadan capability response production'a açılamaz. ETag, conditional request,
service worker, browser cache veya intermediary optimizasyonu eski allow kararını replay
edemez. Denied, unknown ve unregistered ayrımı response body/status/cardinality, timing,
cacheability, validator/ETag veya redirect davranışından gözlenemez.

## 6. Generic route registry zarfı

Bir owner route, production registry'ye alınmadan önce aşağıdaki kavramsal alanların
kararlaştırılmış ve doğrulanabilir olması gerekir. Bunlar exact JSON/TypeScript tipi
değildir:

| Alan grubu | Zorunlu anlam |
|---|---|
| Kimlik | Stable feature/route identity; varsa mevcut kanonik screen/workspace kimliği. |
| Owner | Owner modül, fiziksel owner contract path/version ve implementation availability. |
| Navigation | Kabul edilmiş path/deep-link kalıbı, page type ve shell layout ihtiyacı. |
| Context | `single-company`, açık `authorized-multi-company-read` veya yalnız merkezi platform allowlist'li `context-free` sınıfı. |
| Capability | View, action ve extension için ayrı owner-supplied capability referansları ve güncel server-backed çözümleme gereği. |
| Authorization | Loader/action ve her owner API çağrısında server reauthorization sınırı. |
| Failure | 401/403/non-disclosing 404/409/service-unavailable owner/platform ayrımı. |
| State | Query/cache namespace sorumluluğu ve company/user/session invalidation beklentisi. |
| Wayfinding | Breadcrumb, güvenli geri dönüş, direct refresh ve browser history davranışı. |
| Extensions | Search, AI, notification, drawer/modal/action bağı varsa owner contract referansı. |

Registry:

- missing veya duplicate stable identity'yi reddeder;
- seçilecek router'ın kabul edilmiş canonicalization kurallarında path collision ve
  static/parameter route belirsizliğini fail-closed reddeder;
- encoded/decoded, Unicode, case, slash, optional/wildcard, redirect ve route precedence
  eşdeğerlikleri package review'unda açıkça kilitlenmeden registry aktive edilmez;
- unknown alan, unsupported contract version veya owner uyuşmazlığını sessizce yutmaz;
- uygulanmamış veya capability'siz feature için inert menu/route üretmez;
- 159 ekranı otomatik route ya da ana menü öğesine dönüştürmez;
- prototype hash route'unu production route saymaz;
- menu görünürlüğünden permission veya record existence çıkarmaya izin vermez;
- bütün declaration setini tek doğrulanmış snapshot olarak kabul veya reddeder; kısmi
  registry aktivasyonu yapmaz;
- view capability'sinden create/update/approve/export veya extension capability'si
  türetmez; her etkili action kendi owner ve backend contract'ına bağlıdır.

Breadcrumb veya geri dönüş hedefi ham kullanıcı URL'si değildir; accepted registry
identity'sinden çözülür ve yeniden authorize edilir. External navigation ayrı kabul edilmiş
allowlist/policy olmadan açılmaz; return target açık yönlendirme yüzeyi olamaz.

Exact path, route sırası, serialization, router paketi ve lazy-loading/bundle stratejisi bu
taslakta seçilmez.

## 7. Owner declaration ve opaque handoff

Her feature owner yalnız kendi iş alanı için şunları sağlar:

- kabul edilmiş canonical screen/workspace eşlemesi;
- business route ve deep-link gereksinimi;
- business permission/capability anlamı ve gereken scope;
- owner API, domain loading/action ve error/conflict semantiği;
- loading/empty/unauthorized/validation/conflict/success durumları;
- feature acceptance ve fixture referansları;
- owner contract/version ve implementation availability.

Her owner/contract version için yalnız bir accepted, repository-visible declaration kaynağı
olabilir. Shell bu kaynağı strict tüketir veya strict parity ile doğrular; business route,
permission, error ya da limitleri ikinci bir platform listesinde kopyalamaz. Declaration
path/schema/code-generation biçimi açık karardır, fakat tek-kaynak ve exact-version
bütünlüğü zorunludur.

Shell owner bu alanların business anlamını yeniden yazmaz. Generic validator yalnız
şema/sürüm, uniqueness, canonical collision, owner, context sınıfı, capability varlığı ve
gerekli handoff referanslarının bütünlüğünü kontrol eder. Feature source'un global App,
router, registry, query client veya mount collection'ına doğrudan import/self-registration
ile yazması architecture guard tarafından reddedilir; production mount yalnız shared
composition root ve doğrulanmış registry snapshot'ından oluşur.

Pozitif ve güncel capability alınmadan feature module top-level side effect, network
request, telemetry, background timer/worker veya business state üretmez. Bundle içinde
kodun bulunması çalıştırma yetkisi değildir; exact import/lazy-loading tekniği package ve
build review'unda seçilir.

Owner girdisi kabul edilmemiş veya merge edilmemiş branch'teyse production registry onu
tüketmez. PR numarası ve branch adı coordination evidence olabilir; kanonik contract
yerine geçmez.

### 7.1 Cari sahiplik sınırı

- Cari route, permission, paging, company-link ve error semantiği ChatGPT-CARI owner
  hattındadır.
- PR #44, #77 ve #79 current review girdileridir; hiçbiri bu taslakla accepted veya
  merged sayılmaz.
- Shared shell, Cari path, permission, limit, cursor, error code ya da Organization
  projection davranışı tahmin etmez.
- Cari declaration ileride accepted main kaynağı olduğunda generic registry onu strict
  test/validation ile tüketebilir; ayrı hard-coded ikinci Cari envanteri üretmez.
- Cari feature source, `docs/features/CARI_*` ve ChatGPT-owned branch'ler bu çalışma
  kapsamında değiştirilmez.

## 8. Feature-mount sorumluluk matrisi

| Konu | Shared shell owner | Feature owner |
|---|---|---|
| Root App ve shell layout | Sahip | Tüketir |
| Router/registry generic altyapısı | Sahip | Owner declaration sağlar |
| Capability zarfı/çözümleme protokolü | Generic sınırı sahiplenir | Business capability anlamını sağlar |
| Query/cache CompanyContext fence | Generic lifecycle'ı sahiplenir | Feature query/action anahtar girdilerini sağlar |
| Canonical screen ve route anlamı | Doğrular, yeniden yorumlamaz | Sahip |
| Business permission/ABAC/SoD | Uygulamaz, yalnız server sonucunu tüketir | Backend owner contract'ı sahiplenir |
| Domain loading/empty/error/conflict | Generic render slotu sağlar | Semantiği ve acceptance'ı sahiplenir |
| Shared components/a11y/browser harness | Sahip | Contract'a göre kullanır/test eder |
| API/domain authorization | Yerine geçmez | Backend owner uygular |
| Shared App/router/global CSS/query client yazması | Tek yazar | Ayrı production writer olamaz |

Mount kaydı eksik, unavailable, stale veya owner contract'ıyla uyumsuzsa route açılmaz.
Feature bundle'ın bulunması tek başına availability değildir.

## 9. CompanyContext ve state fence

Capability, route ve mount katmanı şu kuralları birlikte korur:

1. Tek-company route, server doğrulamadan aktif CompanyContext kabul etmez.
2. Client path/query/body/localStorage içindeki CompanyId kapsam genişletemez.
3. Açık authorized-multi-company-read davranışı ayrı owner contract ister; client fan-out
   veya veri union'u değildir.
4. Group-read sonucu write yetkisi üretmez; write açık tek CompanyContext'e geçmeden
   uygulanmaz.
5. Company switch; capability, route loader, cache, drawer/modal, favorite/recent ve
   background refresh'i yeniden doğrular.
6. Eski context'in geç yanıtı yeni context state'ine uygulanmaz.
7. Dirty form, pending write veya onay preview'i sessizce başka şirkete taşınmaz.
8. Logout/kullanıcı değişimi, önceki actor'a ait capability ve view state'ini yok sayar.
9. Daralan permission/scope açık route'u ve görünür action'ı yeniden doğrular.
10. Cache anahtarının exact formatı bu taslakta seçilmez; kullanıcı/session/context
    izolasyonu ve invalidation davranışı acceptance ile kanıtlanır.

## 10. Hata, session ve non-disclosure sınırı

| Durum | Generic shell davranışı |
|---|---|
| Capability eksik/unknown/malformed/stale | Feature kapalı; geniş fallback veya probing yok. |
| Capability service unavailable | Yetkisiz kullanıcıya feature keşfi yaptırmadan fail-closed; yalnız önceden güvenli biçimde yetkilendirilmiş mount generic unavailable state gösterebilir. |
| `401` | Session/login akışı; hassas state login yüzeyine sızmaz. |
| `403` | Forbidden; gerçek empty veya 404 metninden permission tahmini yok. |
| Non-disclosing `404` | Yabancı kayıt/tenant varlığı title, breadcrumb, count veya toast ile sızmaz. |
| `409` | Stable owner error code/contract tüketilir; Türkçe metinden conflict türü tahmin edilmez. |
| Owner route geçici runtime arızası | Yalnız zaten yetkili mount'ta veri sızdırmayan generic fallback; uygulanmamış feature menu/route üretmez. |
| Beklenmeyen/malformed cevap | Güvenli route/page fallback ve server-derived correlation reference. |

Platform; shared authentication/authorization/unhandled/service-boundary error zarfını,
feature owner ise domain validation/not-found/conflict anlamını sahiplenir. Hangi exact
kodların hangi contract'ta yer alacağı ayrı kabul edilir; bu taslak yeni error code üretmez.
Her stable code tek owner-qualified, sürümlü ve repository-visible kaynaktan gelir;
cross-owner collision veya kopya literal parity'si reddedilir. Unknown code Türkçe
title/detail üzerinden sınıflandırılmaz; veri sızdırmayan generic unexpected-error
fallback'ine gider ve güvenli correlation ile tanılanır. Shell yalnız kabul edilmiş
platform-owned shared kodları generic olarak yorumlar; domain kodu owner/version bağıyla
opaque taşınır ve anlamı owner renderer/contract dışında yeniden sınıflandırılmaz. Exact
namespace/prefix açık karardır; `(owner, contract version, code)` uniqueness ve parity
fail-closed zorunluluktur.

## 11. Search, AI, notification ve dönem slotları

- ERP-000I kabul edilmeden global search mount edilmez ve mock sonuç gösterilmez.
- ERP-000J kabul edilmeden AI mount internal extension point'tir; görünür prompt,
  attachment, cevap veya `Uygula` davranışı yoktur.
- Kabul edilmiş feed/event owner olmadan notification badge/count/link mount edilmez.
- Kabul edilmiş dönem context owner'ı olmadan görünür/işlevsel dönem seçici yoktur.
- Extension capability, base route permission'ını veya owner API authorization'ını
  genişletemez.
- AI/search/notification arızası normal ERP görevini gereksiz yere bloke etmez; ancak
  ilgili extension unavailable olur.

## 12. Taslak acceptance planı

Aşağıdaki maddeler task-local plan girdisidir; global `AT-*` veya tamamlanma kanıtı
değildir:

- Missing, unknown, duplicate, malformed ve unsupported-version capability fail-closed.
- Denied/unknown/unregistered capability raw endpoint body/status/cardinality/timing,
  cache/ETag veya redirect üzerinden ayırt edilemez.
- Context-free sınıfı merkezi allowlist dışında ve tenant/business veri taşıyan route'ta
  reddedilir.
- Capability A actor/session/company bağından B bağında kullanılamaz.
- Eski/tekrarlanan veya sırası bozulmuş capability revision'ı daha yeni kararı geri alamaz;
  grant/revoke yarışı eski grant'i canlandırmaz.
- Company/permission/scope değişimi açık route, action, cache ve extension'ı yeniden
  doğrular.
- Direct URL, refresh, back/forward, yeni sekme, favorite ve recent aynı backend
  authorization sonucunu üretir.
- Encoded/decoded, Unicode, case, slash, optional/wildcard, redirect, static/parameter ve
  precedence collision'ları accepted router canonicalization'ında reddedilir; registry
  bütünüyle aktive olur veya bütünüyle reddedilir.
- Owner declaration eksik veya unavailable olduğunda menu/route/action oluşmaz.
- Feature direct import/self-registration ile shared registry veya mount kapısını
  atlayamaz.
- Pozitif capability öncesinde feature module network, telemetry, timer/worker veya başka
  top-level side effect üretmez.
- Client rol/JWT/localStorage/bundle varlığından capability türetemez.
- Dar scope count/title/breadcrumb/empty/error üzerinden geniş şirket verisi sızdırmaz.
- Yavaş eski-company response'u yeni CompanyContext'e uygulanmaz.
- Pending write ve dirty form company switch ile duplicate write veya veri kaybı üretmez.
- `401/403/404/409/service unavailable/malformed` durumları birbirine çevrilmez.
- Error türü Türkçe title/detail metninden tahmin edilmez.
- Cari gibi owner modül declaration'ı tek kanonik kaynaktan strict tüketilir; shell ikinci
  business envanteri hard-code etmez.
- View capability action/approve/export veya extension capability'si üretmez; her işlem
  backend owner contract'ında yeniden authorize edilir.
- `view=allowed` iken create/update/approve/export capability'lerinin ayrı ayrı denied
  kalabildiği negatif senaryo kanıtlanır.
- Duplicate/cross-owner error-code declaration bütün registry snapshot'ını reddeder;
  yalnız valid registry sonrasında runtime'da gelen unknown/newer code generic güvenli
  fallback'e gider. Owner kaynağı dışında ikinci error-code listesi kabul edilmez.
- Breadcrumb/back/return hedefi yalnız accepted internal registry identity'sinden çözülür;
  scheme-relative, encoded absolute, backslash, `javascript:`/`data:` ve diğer
  raw/external open-redirect biçimleri reddedilir.
- Capability kapalıyken search/AI/notification/dönem mock veya inert çalışan control
  üretmez.
- Capability envelope browser persistent storage veya shared/intermediary cache'e
  yazılmaz; aynı browser/proxy üzerinde ardışık A→B kullanıcı/session geçişinde A'nın
  allow/state/validator bilgisi B'ye uygulanmaz.
- Keyboard, focus, deep-link ve responsive navigation accepted device/browser
  matrisinde kanıtlanır.

Gerçek test türleri unit/contract/API/browser/accessibility katmanlarını içerir. Production
capability kararı persisted Identity/Organization permission, role assignment, ABAC scope,
CompanyId veya registry girdisi tüketiyorsa gerçek SQL Server API/contract acceptance
zorunludur; mock/InMemory yalnız ek kanıttır. En az A actor/company kararının B bağında
yeniden kullanılamaması, dar scope'un company-wide'a yükselmemesi, inactive/expired grant,
grant/revoke/reconciliation yarışı, foreign identifier ve non-disclosure gerçek SQL'de
kanıtlanır. Exact fixture, package ve ürün eşikleri ayrı review'da seçilir.

## 13. Açık tasarım kararları

Bu taslak aşağıdakileri kapatmaz:

1. Capability endpointi, envelope serialization'ı, sürümleme ve freshness mekanizması.
2. Client'a açılacak permission/scope ayrıntısının veri minimizasyonu.
3. Route registry serialization'ı, router ve lazy-loading/bundle stratejisi.
4. Shared query/cache, form, component, icon ve browser-E2E paketleri.
5. Cache namespace/invalidasyon uygulaması ve offline/PWA desteğinin kapsamı.
6. Capability değişiklik bildirimi, polling/refresh ve permission-revocation gecikme
   sözleşmesi.
7. Single-company ile authorized-multi-company-read için exact server contract'ları.
8. Period context owner'ı ve görünürlük koşulu.
9. Owner declaration'ların repository path, schema ve code-generation/validation modeli.
10. Error code sahiplik matrisi ve shared/platform ile domain contract birleşimi.

Bu maddeler mevcut kabul edilmiş kimliklerle sahipli karara bağlanmadan implementation
başlamaz. Bu dosya yeni kanonik karar kimliği tahsis etmez.

## 14. Şimdilik yasak değişiklikler

Bu çalışma kapsamında şunlar yapılmaz:

- React, CSS, router, query/cache, component, API veya backend implementation;
- package/lockfile, test harness, CI/workflow, container veya deployment değişikliği;
- task manifest, karar sicili, acceptance matrisi, Master veya UI catalog/card/asset
  değişikliği;
- canonical screen/route/menu/permission/capability/error code kaydı;
- Cari production source, `docs/features/CARI_*` veya ChatGPT-owned branch yazması;
- group mode için magic CompanyId veya client-side union;
- mock search/AI/notification/KPI/badge/route davranışı;
- ERP-000K statüsünü `ready/done` yapma ya da üst contract'ı kapatma.

## 15. Bu Draft için kabul kapısı

- diff yalnız `docs/features/ERP-000K_CAPABILITY_ROUTE_MOUNT_CONTRACT_DRAFT_v1.md`
  olmalıdır;
- current-main SHA ve source envanteri doğrulanmalıdır;
- yeni kanonik task/decision/ADR/OPEN/AT/screen/route/permission/capability kimliği ilan
  edilmemelidir;
- task manifest 15 görev / 49 edge ve bütün status/dependency/blocker değerleri
  değişmeden kalmalıdır;
- karar sicili Markdown/CSV 107/107 parity korunmalıdır;
- UI golden 72 kaynak / 159 ekran ve 24/24 validator testi korunmalıdır;
- global acceptance matrisi 70 satır ve mevcut kanıtlarıyla değişmeden kalmalıdır;
- `git diff --check`, task manifest, bootstrap ve UI validator paketleri geçmelidir;
- exact-head bağımsız review'da açık P0/P1 bulunmamalıdır;
- Issue #87 nedeniyle runner başlamadan biten zero-step workflow kabul sayılmamalıdır;
- PR Draft / human review / NO-MERGE kalmalıdır.

Bu kapının geçmesi yalnız generic contract taslağını review edilebilir yapar. ERP-000K
implementation, package seçimi, owner feature mount'u veya production activation için GO
vermez.
