# CARI-UI-001 — Cari Kartlar ve Cari 360 İlk Uçtan Uca Dilim

- Sürüm: `1.5`
- Tarih: `2026-08-30`
- İş sahibi: `CHATGPT-BUSINESS/CARI`
- Ortak shell/capability/route/query-cache sahibi: `CODEX-PLATFORM`
- Organization şirket dizini sahibi: `CODEX-PLATFORM/ORGANIZATION`
- İncelenen ana dal: `main@75c783ba1ca0ab78432398f57c67e12b8c3177d2`
- Contract durumu: **ACCEPTED**
- Implementation: **BLOCKED**
- Production activation: **NO-GO**
- Event delivery: `synchronous_transaction`
- Takip: Issue `#78`

## 1. Amaç ve kullanıcı sonucu

Bu sözleşme ilk görünür Cari dikey diliminin iş, güvenlik, route, paging, hata ve erişilebilirlik sınırını tanımlar. Tek başına production UI açmaz.

### 1.1 Normal-runtime declaration doğrusu ve hedefi

Bugünkü normal `CounterpartyShellCapabilityProvider` hem `cari.parties.list` hem `cari.parties.detail` için `SingleOrAuthorizedMultiCompanyRead + ImplementationAvailable=false` yayımlar. JSON'daki `routes[]` ve `capabilities[]` alanları bu current-runtime gerçeğini korur. Testing ortamındaki disposable C-01 override'ının `true` olması normal-runtime aktivasyonu değildir.

| Route | Current normal runtime | Kabul edilen aktivasyon hedefi | Bugünkü kapı |
|---|---|---|---|
| `cari.list` / C-01A | `SingleOrAuthorizedMultiCompanyRead + false` | `SingleCompany + company-wide + true` | Issue #146 kalan 3 acceptance boşluğu + C-03 bağımsız activation + Issue #199 + Issue #204 normal-provider profili + native Back/Forward + beşli seri |
| `cari.detail` / C-03 | `SingleOrAuthorizedMultiCompanyRead + false` | `SingleCompany + company-wide + true` | Issue #148 + normal-origin + ayrı SQL/browser/a11y + beşli seri |

Route'lar bağımsız açılır. Owner source bu PR merge edildiğinde hazırdır. PR #192 birleştiği için list ve detail activation implementation başlangıç uygunluğu `true`dır. Issue #148'in kalan bulguları C-03 candidate'ında kapanır; detail runtime GO'yu kanıt tamamlanana dek bloke eder, implementation başlangıcını değil. Bundan ayrı iki route'un da `runtimeActivationGo=false` gerçeği kalır. Liste kanıtı detail'i açmaz; C-01B/group ve group detail renderer/implementation kapalı kalır. `ImplementationAvailable=true` yalnız normal/pre-production implementation availability demektir; global production deployment/cutover `NO-GO` ve genel implementation readiness `BLOCKED` kalır. Exact kanıt kapısı bu sözleşmenin §13'ünde ve `ACIK_KARARLAR.md` içinde tanımlıdır.

İlk dilim:

1. `C-01 Cari Kartlar`,
2. `C-02 Yeni Cari` ekranının mevcut backend tarafından desteklenen create alt kümesi,
3. `C-03 Cari 360` çekirdek özeti,
4. `CARI-LINK-WORKSPACE-001` ile mevcut global aktif Party’yi başka Göymen şirketine bağlama

ile sınırlıdır.

`C-04…C-12` ayrı kanonik ekran/workspace olarak korunur. Bakiye, açık kalem, risk, limit, adres, kişi, banka, vergi, sicil, belge, sipariş, stok, tahsis, CRM ve AI sonucu owner backend gerçeği olmadan mock, sıfır veya sahte KPI olarak gösterilmez.

## 2. Current-main doğrusu

Aşağıdaki contract/feature dilimleri ana dala alınmıştır:

- PR `#106` / kaynak PR `#77`: global aktif Party’yi hedef şirkete bağlayan company-account link dilimi;
- PR `#107` / kaynak PR `#97`: shared `401/403/500/503` ProblemDetails sözleşmesi;
- PR `#108` / kaynak PR `#102`: capability, route registry ve feature-mount contract bundle’ı.

PR #108 generic contract'ı yayımlamıştır. Güncel main'de normal server-backed provider staged declaration'ları vardır; list/create/link/detail declaration'larının dördü de `ImplementationAvailable=false` kalır. PR #169 merkezî business-read `401/403` sınırını, PR #183 uzak C-01 `10/10` gerçek-browser kanıtını, PR #188 kalıcı SAB browser koşucusu ve `4/4`, `skip=0` artifact kapısını, PR #189 C-03 UI'yı birleştirmiştir. PR #191 CARI-LINK no-op concurrency acceptance'ını deterministik yapmıştır; bu kanıt C-01/C-03 normal-runtime aktivasyonuna kredi vermez. PR #192 final head `c7410fa0151118e9f9976ae6bd1b5f4e06bb5c82`, coordinator kayıtlı `5 × 218/218` ve uzak `7/7` ile merge SHA `68aa15c81c049330e5ff08bf75b25a9ec6217f82` olarak birleşmiştir. PR #196 final head `7e284c7833e2a3130adc84d9c53efbdfc08f08ce`, merge `3fceea9e191907f370bf9e240b3b80cb8974b2d6` ve run `33721831760` ile disposable override altında yalnız Forward davranışını `1/1`, `skip=0` kanıtlamıştır; normal-provider activation kredisi yoktur. Issue #146 owner denetimi `4 PASS / 2 PARTIAL / 1 FAIL` sonucuyla açık kalmıştır; `C01-AC-002/005/007` mevcut kimliklerinde kapanmadan C-01 runtime GO yoktur. Issue #199'un üç P1'i ve native Back de açıktır. PR #197/#198 altyapı doğrulamalarını taşır; #198 yalnız task-manifest parser hardening'idir ve capability gerçeğini değiştirmez. PR #201 C-02/company-link mutation response boundary'sini, PR #206 company-link source-authority sınırını sertleştirmiştir; ikisi de UI/capability activation kredisi vermez. İncelenen current main `75c783ba1ca0ab78432398f57c67e12b8c3177d2` olmuştur.

Company-link davranışı:

- hedef şirket yalnız server-resolved aktif `CompanyContext`;
- yalnız global `Party.IsActive=true`;
- pasif fakat izinli kaynak company-account kimlik/görünürlük kanıtı olabilir, yeniden aktifleştirilmez;
- `already_linked` ve `idempotency_conflict` stable Cari-owned code;
- same/different-key yarışları, replay, wrong-scope, CompanyId override, rollback ve audit/idempotency cardinality gerçek SQL ile kanıtlıdır.

Bu mevcut backend davranışı UI yetkisi veya görünürlüğü üretmez; capability, route ve browser kabul kapıları ayrıca geçer.

## 3. Sahiplik ve tek-yazar sınırı

### `CODEX-PLATFORM`

- root App ve ortak shell,
- router ve route registry,
- server-backed capability provider,
- shared query/cache ve CompanyContext generation fence,
- global navigasyon, tokenlar, global CSS ve ortak bileşenler,
- Playwright, visual ve accessibility harness,
- platform-owned `401/403/500/503` hata zarfı.

### `CODEX-PLATFORM/ORGANIZATION`

- yetkili şirket dizini,
- şirket kimliği → şirket kodu/hukuki ad projection’ı,
- projection sürümü, authorization ve cache invalidation sözleşmesi.

### `CHATGPT-BUSINESS/CARI`

- Cari feature route declaration’ları,
- Cari API istemcisi ve response mapping,
- C-01, C-02 create alt kümesi, C-03 ve link workspace,
- Cari filtre/form/state/fixture’ları,
- Issues `#93`, `#94`, `#95` production uygulamaları,
- Cari domain `400/404/409` anlamları,
- Cari component, gerçek SQL ve browser kabulü.

Shell runtime handoff tamamlanana kadar ChatGPT-Cari `App.tsx`, global CSS/router/query/capability client ve ortak navigasyonda read-only’dir.

## 4. Organization şirket görüntüleme projection’ı

Cari UI ham UUID’yi şirket etiketi yapmaz ve sabit şirket adı uydurmaz.

| Alan | Değer |
|---|---|
| Contract | `ORGANIZATION-COMPANY-DIRECTORY-V1` |
| Owner | `CODEX-PLATFORM/ORGANIZATION` |
| Mevcut transport | `GET /api/v1/organization/companies` |
| Context | Kimliği doğrulanmış actor’ın yetkili şirket seti; `X-Company-Id` yok |
| Response version adayı | `organization.company-summary.v1` |
| Alanlar | `id`, `code`, `legalName`, `isPlaceholder` |
| Join | `CompanyPartyAccount.companyId == CompanySummary.id` |

Cache yalnız actor/session/yetkili-company-seti revision’ına bağlı private process-memory snapshot olabilir. Login/logout, kullanıcı veya yetkili şirket seti değişimi, policy revision, malformed response ve kesin authorization kaybında temizlenir. Seçili şirket değişimi bütün company-scoped Cari sorgularını generation fence ile geçersizleştirir.

Projection yoksa şirket adı uydurulmaz, ham UUID gösterilmez ve link hedefi seçilemez.

## 5. Route registry metadata

Static route’lar parameter route’tan önce çözülür.

Shell-facing canonical screen kimliği `C-02`’dir. `create-subset`, ekran kimliğinin parçası veya alias değildir; route’un kapsam varyantı metadata’sıdır.

| Route ID | Screen | Path | Context | Güvenli dönüş |
|---|---|---|---|---|
| `cari.list` | `C-01` | `/cari` | single veya explicit authorized multi-company read | `dashboard` |
| `cari.create` | `C-02` (`scopeVariant=create-subset`) | `/cari/yeni` | single-company | `cari.list` |
| `cari.company-link` | `CARI-LINK-WORKSPACE-001` | `/cari/sirkete-bagla` | single-target-company | `cari.list` |
| `cari.detail` | `C-03` | `/cari/:partyId` | single veya explicit authorized multi-company read | `cari.list` |

Deep-link, refresh, back/forward ve yeni sekme her seferinde taze capability ve backend authorization ister. Raw return URL kabul edilmez; yalnız accepted internal route identity kullanılır. Eski context Party etiketi title, breadcrumb, recent veya favorite içinde taşınmaz.

## 6. Capability matrisi

| Capability | API | Permission | Scope |
|---|---|---|---|
| `cari.parties.list` | `GET /api/v1/counterparties` | `counterparty.parties.read` | şirket veya explicit authorized multi-company read |
| `cari.parties.detail` | `GET /api/v1/counterparties/{partyId}` | `counterparty.parties.read` | şirket veya explicit authorized multi-company read |
| `cari.parties.create` | `POST /api/v1/counterparties` | `counterparty.parties.create` | company-wide single-company |
| `cari.accounts.status.update` | `PUT .../company-account/active-state` | `counterparty.accounts.status.update` | company-wide single-company |
| `cari.accounts.link` | `POST .../{partyId}/company-account` | `counterparty.accounts.create` + `counterparty.parties.read` | ikisi de hedef şirkette company-wide |

UI capability'yi rol adından, JWT'den, localStorage'dan, route isminden veya `/identity/me` cevabından türetmez. Unknown, malformed veya stale capability fail-closed olur. Kontrol görünürlüğü authorization değildir; backend her çağrıda yeniden yetkilendirir.

### 6.1 Normal-runtime açılış hedefi

Her route ayrı activation candidate taşır. C-01A candidate, kabul edilmiş PR #192 merge SHA'sını içeren işlem-anı main'den oluşturulur ve head'i bu merge SHA'sının descendant'ı olur. Kanıtın çalışabilmesi için yalnız list provider/current-runtime alanlarını aşağıdaki hedefe değiştirir; #184 ile açılmış route-local/top-level implementation-start alanlarını `true/[]` olarak korur, `runtimeActivationGo=true` ve route-local runtime-GO blocker dizisini `[]` yapar. Detail runtime alanları candidate tarafından değiştirilmez ve pinli base değeri aynen korunur: reviewed main'de `ImplementationAvailable=false`tır; C-03 daha önce kendi bağımsız kapısıyla açılmışsa kabul edilmiş base değeri C-01 kanıtı sayılmadan korunur. C-03 ayrı candidate'ında yalnız detail'i değiştirir; candidate aynı head'de #148'i ve kendi kanıt kapısını kapattıktan sonra merge edilir. Candidate source tree production'a deploy edilmez; aynı combined exact head browser/CI/review kapılarını geçmeden main'e merge edilmez:

- C-01A: `cari.list + C-01 + /cari + cari.parties.list + SingleCompany + CompanyWide + counterparty.parties.read + ImplementationAvailable=true`.
- C-03: `cari.detail + C-03 + /cari/:partyId + cari.parties.detail + SingleCompany + CompanyWide + counterparty.parties.read + ImplementationAvailable=true`.
- C-01B/group ve group detail kapalıdır; create/link declaration'ları bu geçişte `false` kalır.

## 7. Read model ve redaction

Party alanları: `id`, `code`, `kind`, `legalName`, `tradeName`, `isActive`, `version`.

Şirket hesabı alanları: `id`, `companyId`, `roles`, `isActive`, `version`.

`externalIntegrationCode`, dedicated read permission ve redaction owner kabul edilene kadar UI mapping’ine alınmaz. Global Party aktifliği ile şirket hesabı aktifliği ayrıdır. Cari kodu salt okunurdur.

## 8. `CARI-READ-PAGE-001` — accepted observable contract

Owner: Issue `#94`, karar: `issuecomment-5470043284`.

- `q`: opsiyonel, Unicode dış whitespace trim + NFC, en çok `200`;
- `pageSize`: default `50`, maximum `200`, minimum `1`;
- varsayılanlar: `roleMatch=any`, `partyState=active`, `accountState=all`, `sort=code`, `direction=asc`, `includeCount=false`;
- cursor azami yaşı `30 dakika`; session/policy/permission/yetkili-company-seti revision değişimi daha erken invalid eder;
- arama code/legalName/tradeName contains;
- Türkçe case-insensitive ve accent-sensitive; accent folding yok;
- her sort `PartyId` unique tie-breaker taşır;
- keyset/authenticated opaque cursor zorunlu; offset paging ve browser’a tüm listeyi yükleme yasaktır;
- browser fan-out/client union yasaktır;
- invalid/tampered/expired/scope-mismatch cursor: `400/code=invalid_cursor`, partial data yok;
- `includeCount=true` yalnız authorization/context/filter sonrası distinct Party sayısını döndürür.

## 9. `CARI-LINK-CANDIDATE-001` — accepted observable contract

Owner: Issue `#95`, karar: `issuecomment-5470044664`.

```http
GET /api/v1/counterparties/company-account-link-candidates
X-Company-Id: <server tarafından doğrulanan hedef şirket>
```

- hedefte company-wide `counterparty.accounts.create` + `counterparty.parties.read`;
- Party global aktif;
- en az bir actor-authorized source account üzerinden görünür;
- hedefe zaten bağlı değil;
- yalnız actor-visible source account özetleri;
- source account pasifliği tek başına onboarding engeli değil;
- page default `25`, maximum `100`, minimum `1`;
- Issue #94 arama/cursor semantiği yeniden kullanılır;
- invalid cursor: `400/code=invalid_cursor`, partial candidate yok;
- PartyId hint fresh candidate response olmadan authority değildir;
- POST bütün şartları yeniden doğrular.

## 10. Idempotency ve replay

Yeni Party create, account active-state update ve company-link tekil `Idempotency-Key` ister:

- tek field-value,
- Trim,
- `1..128` karakter,
- missing/empty/multiple/comma-joined/oversized fail-closed.

Her kullanıcı-onaylı logical submit tek key kullanır. Timeout/unknown result yalnız aynı payload ve aynı key ile kontrollü retry edilir. Yeni key veya company switch pending sonucu aşamaz.

Replay header’ları:

```text
Idempotency-Replayed: true
Idempotency-Original-Correlation-Id: <ilk correlation>
```

Replay duplicate business effect, navigation veya toast üretmez.

## 11. Hata sahipliği

### Cari domain — Issue `#93`

| Code | HTTP | Main durumu |
|---|---:|---|
| `validation_failed` | 400 | C-01 read PR #141 ile main'de |
| `command_metadata_invalid` | 400 | PR #127 ile main'de |
| `invalid_cursor` | 400 | PR #141/#174 ile main'de |
| `not_found` | 404 | C-03 PR #170/#187 ile main'de |
| `already_linked` | 409 | PR #106 ile main’de |
| `version_conflict` | 409 | PR #127 ile main'de |
| `idempotency_conflict` | 409 | PR #106 ile main’de |

`not_found` missing/foreign/hidden/global-inactive ayrımını sızdırmaz. UI branching yalnız `code` üzerinden yapılır; localized metin parse edilmez.

### Shared platform — PR `#107` ile main’de

| Code | HTTP |
|---|---:|
| `authentication_required` | 401 |
| `forbidden` | 403 |
| `internal_error` | 500 |
| `service_unavailable` | 503 |

Cari bunları yeniden tanımlamaz. Unknown/malformed code veri sızdırmayan generic unexpected-error state’e gider. ProblemDetails plain text render edilir; `innerHTML` yoktur.

## 12. UI state, ekran ve erişilebilirlik

Loading, no-data, filter-empty, partial/stale, 401, 403, non-disclosing 404, domain 409, replay, timeout/unknown write, 500, 502/503/504, malformed, offline ve stale-company response ayrı state’lerdir.

- `C-01`: server arama/filtre, yoğun liste, masaüstü drawer/mobil tam detay; para/risk/mock KPI yok.
- `C-02`: Party türü, hukuki ad, opsiyonel ticari ad, roller ve shell’den salt okunur aktif şirket.
- `C-03`: Cari kimliği/ad/tür, global Party durumu ve yalnız yetkili şirket hesapları.
- `C-04…C-12`: ayrı kanonik route/workspace; owner kapısı olmadan boş sekme yok.

Kabul; klavye görevleri, görünür focus/skip-link, semantic sort + `aria-sort`, validation summary focus, drawer/modal focus yönetimi, live-region, %200 zoom, %400 reflow, reduced motion, rengin tek sinyal olmaması ve mobil parity’yi kapsar.

## 13. Tamamlanan ve normal-runtime açılış kapıları

Tamamlanan:

- [x] PR #106 company-link backend merge.
- [x] PR #107 shared platform error merge.
- [x] PR #108 generic capability/route/mount contract bundle merge.
- [x] PR #127 stable Cari ProblemDetails producer'ları.
- [x] PR #141 C-01 list read API merge'i.
- [x] PR #170 C-03 lossless detail contract ve PR #187 C-03 server-scope fix merge'i.
- [x] PR #174 company-link candidate read API merge'i.
- [x] PR #183 `B-C01-001…010` uzak gerçek-browser `10/10`, `skip=0` kanıtı.
- [x] PR #185/#186 company-deactivation read fence/race kanıtı.
- [x] PR #169 merkezî business-read `401/403` sınırı merge'i.
- [x] PR #188 kalıcı SAB-006…009 browser/JUnit/HTML artifact kapısı.
- [x] PR #189 C-03 UI merge'i.
- [x] PR #191 CARI-LINK no-op concurrency acceptance; normal-runtime aktivasyon kanıtı değildir.
- [x] Issues #93/#94/#95 observable iş sözleşmeleri kabul edildi.
- [x] PR #192 route-entry freshness final head `c7410fa0…`, coordinator kayıtlı beş ardışık tam `218/218`, görünür uzak `7/7` ve merge `68aa15c8…`. Beş ayrı uzak run kimliği yayımlanmadı; taze Codex exact-head incelemesi ile gerçek-browser Back/Forward kanıtı waive edildi.
- [x] PR #196 disposable Forward davranışı: head `7e284c78…`, merge `3fceea9…`, run `33721831760`, `1/1`, `skip=0`; normal-provider activation kanıtı değildir.
- [x] PR #201 C-02/company-link mutation response boundary hardening; activation kredisi değildir.
- [x] PR #206 company-link company-wide source-authority hardening; activation kredisi değildir.
- [ ] Issue #199 activation evidence hardening: fail-closed exact JUnit/source, ilk response'tan cache yaşı, crash-safe restore ve preimage eşitliği.
- [ ] Issue #204 disposable-key-free normal-provider activation profili: API `overrideKeysPresent=[]` ve post-DI exact provider origin/config attestation.

PR #192 ile production `staleTime=30_000` altında başarılı giriş, `401`, `403/404`, malformed, offline/network, `5xx`, retry ve generation/fence sonuçlarını kapsayan route-entry jsdom/mutasyon suite'i birleşmiştir. PR #183 uzak gerçek-browser `B-C01-001…010` `10/10` ve aynı runner'da `SAB-006…009` `4/4`, `skip=0` da tamamlanmış önkoşuldur. Bunlar normal provider'ı tek başına açmaz.

C-01A candidate yalnız LIST'i `SingleCompany + ImplementationAvailable=true` yapar ve bağımsız C-03 activation'ı taşıyan pinli base'deki detail gerçeğini değiştirmez. Exact head'de gerçek Chromium + API/SQL ile `success → leave → server-side read grant revoke → Back/Forward` iki native direction case'i çalışır. İlk başarılı business response'tan dönüşe ölçülen `elapsedMilliseconds` değeri `30000`'den küçük olur; taze capability çağrısı grantı kaldırmış; revoke sonrası business GET `0`; önceden kurulan `MutationObserver`, DOM ve accessibility tree eski satırı hiç görmemiş olmalıdır. Issue #199'un exact JUnit/source, timing ve crash-safe/exact-equality restore kapıları aynı candidate'ta kapanır. Issue #204'ün disposable-key-free profili olmadan normal-provider beşli seri başlayamaz.

PR #196 yalnız Forward'ı kurabilmiştir; native Back yapısal C-03 bağımlılığıdır. C-03 C-01 implementation başlangıcını engellemez; evidence identity pinini, beşli seri başlangıcını ve runtime GO'yu engeller. Pinli base #148 kapanışı + bağımsız C-03 normal-provider merge'i + post-main yeşili ve production row-link/route topolojisini taşır; LIST kapalıyken gerçek erişilebilirlik iddia etmez. Candidate exact iki history/direction vakası taşır; route-local manifest ayrıca mevcut `CASE-C03-009@list-available` dalıyla `3/3` olur. Back: shell → production Cari click'i → ilk başarılı response/satır → production row click ile detail → revoke → `page.goBack()` ile `/cari`. Forward: shell → production Cari click'i → ilk başarılı response/satır → `page.goBack()` ile shell → revoke → `page.goForward()` ile `/cari`. İlk bootstrap sonrası `page.goto`, `pushState/replaceState`, direct History API, test-invoked router ve test-only route/link yasaktır.

Issue #146'daki “iki istek de tekrarlar” şartı fail-closed dallarıyla uygulanır: grant korunmuş dönüşte taze capability ve business request'in tekrarı PR #192'nin merge edilmiş suite'inde kanıtlanır; grant kaldırılmış gerçek-browser dönüşünde taze capability reddinden sonra business GET'in tekrar etmemesi zorunludur. Forced business `403` ikinci bir aktivasyon varyantı değildir; business `401/403` sınırı SAB kümesinde kanıtlanmıştır.

Bu route-entry alt kümesi Issue #146'nın tamamlandığı anlamına gelmez. [Owner denetimi](https://github.com/sinangym/GoymenERP/issues/146#issuecomment-5521747972) `4 PASS / 2 PARTIAL / 1 FAIL` sonucuyla issue'yu açık bırakır: `C01-AC-002` active/inactive/all filtre matrisi ve inactive-row yokluğunu, `C01-AC-005` `invalid_cursor` sonrası cache yerine yeni cursor'sız page-1 GET'i, `C01-AC-007` ise bütün filtre/continuation için keyboard-visible-focus, mobil/%200 görev tamamlama ve exactly-once live announcement kanıtını mevcut kimliklerde tamamlar. Üçü kapanmadan C-01 runtime GO verilmez.

Normal-provider origin capability listesi veya registry hash'inden çıkarılmaz. Current normal `sha256:3Giw…` ve disposable `sha256:SXjo…` yalnız bugünkü declaration kümelerini ayırır; revision provider türü/source channel içermez. Origin'in tek kaynağı running API process'inin DI sonrası resolved provider count ve exact type'ıdır: tek exact `CounterpartyShellCapabilityProvider` `normal`, tek exact `C01E2eShellCapabilityProvider` `c01-disposable`; eksik/çoklu/bilinmeyen fail-closed. Workflow/artifact etiketi veya `Configure` authority/revision dönüşü origin üretemez. Process-emitted attestation count/type/source channel/process-instance ile aynı process'in registry revision'ını bağlar. Empty override envelope normal provider'ı korur; API process `IConfiguration` görünümünde beş disposable key yoktur; bu yokluk job/runner ortamını kapsamaz. API container ID, image ID/digest ve OCI revision kaydedilir; revision exact head'dir. HTTP `generation` origin kanıtı değildir.

Artifact `(base SHA, head SHA, tested tree SHA, workflow/test SHA, run ID, attempt, API container ID, image ID/digest, OCI revision, process-instance ID, resolved provider count/type, provider origin, registry revision, API overrideKeysPresent=[], capability tuple, browser request correlations, server request bindings)` ile attestation/config/endpoint ve browser/request/cache/DOM/SQL sonuçlarını aynı container/process'e atomik bağlar. Her browser capability/business request correlation'ı aynı attested API process'inin structured request kaydında server-attached container/process-instance ile exact bir kez eşleşir; client origin etiketi otorite değildir. Origin omit/spoof/disposable, disposable-selected+normal-label, eksik/çoklu/bilinmeyen provider, farklı container/process, eksik/çift/eşleşmeyen correlation, normal attestation+disposable browser traffic, API override veya image/head drift kırmızıdır. PR #192 serisi ile #196 disposable run mahsup edilmez. Hem C-03 hem C-01 kendi immutable `(base/head/tested-tree/workflow-test-source/OCI-revision/provider-origin/registry-revision)` kimliğinde beş ayrı full remote run üretir. Run ID/attempt, API container ID, image ID/digest ve process-instance ID run'a özgü atomik bağdır; dolu olmalı, run'lar arasında değişebilir ve streak kimliği diye eşitlenmez. Her run normal origin, farklı run ID, `attempt=1`, retry/quarantine kapalı ve required `skipped/cancelled/timeout=0` ile ardışık yeşildir. C-01 exact Back+Forward ile mevcut `CASE-C03-009@list-available` dallarını (`3/3`); C-03 `CASE-C03-001…008 + CASE-C03-009@list-unavailable` manifestini (`9/9`) ve tam SQL/browser/a11y kümesini çalıştırır; suffix'ler yeni CASE kimliği değildir. Failure veya invariant identity/origin/revision değişimi seriyi sıfırlar; selective rerun sayılmaz. C-03 candidate'ın ilk evidence-bearing head'i provider detail declaration'ı ile owner detail current-runtime/context/scope, implementation-start `true/[]`, proposed `runtimeActivationGo=true` ve blocker `[]` alanlarını exact eşler; list/create/link'i pinli base değerinde tutar; aynı head #148 bulgularını kapatır. Candidate provider/owner-GO/attestation/tests aynı first evidence-bearing SHA'dadır; fresh CI/container/UI-golden, iki exact-head K3 ve merge-tree equality gerekir; toggle-after-evidence yoktur.

### 13.1 Aktivasyon sonrası Issue #78 C-02 + company-link kapısı

#78 implementation'ı C-01A normal-runtime activation merge'i ve post-main yeşili görülmeden başlamaz. Yeni kimlik üretilmez: `CARI_C02_LINK_ACCEPTANCE_v1.md` içindeki mevcut `C02-AC-001…014`, `CLINK-AC-001…020`, `UX-AC-001…004` toplam `38/38` vaka; mevcut `AT-001/003/005/006/008/010/013` ve `DEC-CAR-001…003`, `DEC-TEC-012/013`, `DEC-TST-001/002` bağları kullanılır. VKN/TCKN/duplicate motoru alt kapsam dışıdır; `C02-AC-014` bu yokluğu kanıtlayan zorunlu negatif vaka olarak `38/38` içinde geçer, yalnız global `AT-009` açık kalır.

İlk kanıt-bearing combined exact head, iki gerçek route/component'i, generated client/runtime decoder'ları, test/workflow kaynağını ve `cari.parties.create` ile `cari.accounts.link` normal declaration'larında `SingleCompany + CompanyWide + ImplementationAvailable=true` değerini birlikte taşır. Aynı head'de owner JSON'un iki authoritative current-runtime kaydı provider ile eşleşir; iki route için `activationImplementationStartEligible=true`, route-local ve üst `activationImplementationStartBlockedBy=[]`, `runtimeActivationGo=true`, `runtimeActivationGoBlockedBy=[]` ve üst C-02/link kapısı için `state=unmerged-activation-evidence-candidate`, `runtimeActivationGo=true` bulunur. Reviewed main'de availability/GO alanları `false`, üst başlangıç blocker'ları dolu ve kapı frozen kalır; kanıttan sonra ayrı toggle commit'i yoktur. Server-resolved active CompanyContext tek şirkettir; create yalnız `kind`, `legalName`, opsiyonel `tradeName`, `roles` alır ve Cari kodunu server üretir. Company-link fresh candidate + hedefte iki company-wide permission ister; Party global aktif, en az bir actor-authorized source account üzerinden görünür ve hedefte henüz bağlı değildir. Source roles otomatik kopyalanmaz, reason alanı yoktur, PartyId yalnız fresh candidate ile doğrulanan hint'tir.

Gerçek SQL/API kanıtı create için `Party +1 / target account +1 / party-created audit +1 / create CommandExecution +1`; link için `Party 0 / target account +1 / source 0 / account-created audit +1 / account-create CommandExecution +1` cardinality'sini ve replay/race/unknown-outcome sonuçlarını gösterir. Gerçek Chromium; route/capability/permission/non-disclosure, dirty company switch/stale response, double-submit/replay, malformed/5xx/offline, keyboard/focus/live-region ve responsive/reflow akışlarını kanıtlar. Başarılı create ve link mevcut sözleşmede target-scoped C-03'e bir kez gider; bu nedenle C-03 normal runtime `true` olmadan veya owner başarı hedefini açıkça revize etmeden full-stack GO yoktur. C-01'e sessiz redirect yoktur.

#78, C-01/C-03 registry authority attestation'ını aynen yeniden kullanır: origin çalışan API'nin post-DI `count=1` + exact `GoymenERP.Modules.Counterparty.Infrastructure.Shell.CounterpartyShellCapabilityProvider` tipinden türetilir; API `IConfiguration` disposable activation anahtarlarını içermez; revision/`Configure`/client-workflow-artifact etiketi otorite değildir. Origin/config/registry/endpoint ile her browser capability/business request'i aynı attested API container/process'ine server-side correlation ile exact bir kez bağlanır; normal attestation + disposable trafik kırmızıdır. Aynı immutable `(base main SHA, candidate head SHA, tested integration tree SHA, workflow/test source SHA, OCI revision=head, resolved provider count/type, provider origin, registry revision)` kimliğinde beş ardışık tam uzak koşum gerekir: farklı run ID, `attempt=1`, `providerOrigin=normal`, `38/38`, retry/quarantine kapalı, required `skipped/cancelled/timeout=0`, bütün işler `completed/success`. API container ID, image ID/digest ve process-instance ID koşuma özgü dolu bağlardır, koşumlar arasında değişebilir. Non-success veya invariant kimlik/origin değişikliği seriyi sıfırlar; selective rerun sayılmaz. Aynı exact head tam CI/container/UI-golden ve iki bağımsız K3 `P0=0/P1=0` alır; kanıttan sonra toggle commit'i yoktur ve merge tree test edilmiş tree ile aynı olmalıdır. Account status update, group modları ve production cutover kapalıdır.

Bu contract'ın merge edilmesi normal provider'ı değiştirmez veya production activation'ı açmaz. Liste ve detail birbirinden bağımsızdır; create/link kapısı tanımlı fakat C-01A activation sonrasına kadar donmuştur. Human UAT ve production cutover ayrıca kapalıdır.
