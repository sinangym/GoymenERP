# CARI-UI-001 — Cari Kartlar ve Cari 360 İlk Uçtan Uca Dilim

- Sürüm: `1.3`
- Tarih: `2026-08-30`
- İş sahibi: `CHATGPT-BUSINESS/CARI`
- Ortak kabuk, route registry, query/cache ve server-capability sahibi: `CODEX-PLATFORM`
- Organization şirket dizini sahibi: `CODEX-PLATFORM/ORGANIZATION`
- İncelenen `main`: `83197c50f515db959638fadd7cc11ad6eb7a4fc8`
- Kanonik ilk ekranlar: `C-01`, `C-03`, `C-02` create alt kümesi
- Yardımcı işlem workspace’i: `CARI-LINK-WORKSPACE-001`
- Takip: Issue `#78`
- Contract durumu: **REVIEW_PENDING**
- Implementation: **BLOCKED**
- Production activation: **NO-GO**
- Event delivery: `synchronous_transaction`

## 1. Sonuç ve kapsam

Bu sözleşme ilk görünür Cari dikey dilimini tanımlar; production kodunu başlatmaz. İlk dilim:

1. `C-01 Cari Kartlar`,
2. `C-02 Yeni Cari` ekranının mevcut backend’in desteklediği create alt kümesi,
3. `C-03 Cari 360` çekirdek özeti,
4. mevcut aktif Party’yi başka şirkete bağlayan `CARI-LINK-WORKSPACE-001`

ile sınırlıdır.

`C-04…C-12` C-03 sekmelerine dönüştürülmez. Bakiye, açık kalem, risk, limit, adres, kişi, banka, vergi, sicil, belge, sipariş, stok, tahsis ve takip görevi için owner backend gerçeği olmadan mock, sıfır veya sahte KPI gösterilmez.

Bağlayıcı kaynaklar `KARAR_SICILI`, Master v3, kanonik ekran kataloğu, Cari route kaynakları, UI tasarım sistemi, ERP-000K shell sözleşmesi, canlı Counterparty contract’ları, PR #77 ve Issues #93/#94/#95 owner kararlarıdır. Prototip iş kuralı veya permission kaynağı değildir.

## 2. Tek-writer ve sahiplik

### `CODEX-PLATFORM`

- root App ve ortak shell,
- router ve route registry,
- server-backed capability provider,
- shared query/cache ve CompanyContext generation fence,
- global navigasyon, tokenlar, global CSS ve ortak bileşenler,
- Playwright, visual ve accessibility harness,
- platform-owned `401/403/500/503` ProblemDetails sözleşmesi.

### `CODEX-PLATFORM/ORGANIZATION`

- yetkili şirket dizini ve görüntüleme projection’ı,
- şirket kimliği → şirket kodu/hukuki ad eşlemesi,
- projection sürümü ve actor/session yetkilendirme kapsamı.

### `CHATGPT-BUSINESS/CARI`

- Cari feature route declaration’ları,
- Cari API istemcisi ve güvenli response mapping,
- C-01, C-02 create alt kümesi, C-03 ve link workspace,
- Cari’ye özel filtre, form, state ve fixture’lar,
- `CARI-READ-PAGE-001`,
- `CARI-LINK-CANDIDATE-001`,
- Cari domain `400/404/409` hata anlamları,
- Cari component, gerçek SQL ve browser kabulü.

Shell handoff tamamlanana kadar ChatGPT-Cari `App.tsx`, global CSS/router/query/capability client ve ortak navigasyonda read-only’dir.

## 3. Organization şirket görüntüleme projection’ı

Cari UI şirket adını/kodunu sabit ID tablosundan veya ham UUID’den üretmez.

Task-local bağımlılık sözleşmesi:

| Alan | Değer |
|---|---|
| Contract | `ORGANIZATION-COMPANY-DIRECTORY-V1` |
| Owner | `CODEX-PLATFORM/ORGANIZATION` |
| Mevcut transport | `GET /api/v1/organization/companies` |
| Context | Kimliği doğrulanmış actor’ın yetkili şirket seti; `X-Company-Id` yok |
| Response version | `organization.company-summary.v1` |
| Alanlar | `id`, `code`, `legalName`, `isPlaceholder` |
| Join | `CompanyPartyAccount.companyId == CompanySummary.id` |

Mevcut endpoint fiziksel kaynak olarak kullanılabilir; ancak owner projection sürümü ve cache/authorization davranışı kabul edilmeden bu sözleşme tamamlanmış sayılmaz.

Cache yalnız aynı actor/session/yetkili-şirket-seti revision’ına bağlı private process-memory snapshot olabilir. Login/logout, kullanıcı değişimi, yetkili şirket seti veya policy revision değişimi, malformed response ve kesin authorization kaybında temizlenir. Seçili şirket değişimi bütün company-scoped Cari query’lerini generation fence ile geçersizleştirir; actor-scoped yetkili şirket dizini ise seçili şirkete göre yeniden adlandırılmaz.

Projection yoksa veya geçici olarak kullanılamıyorsa:

- ham CompanyId kullanıcı etiketi olarak gösterilmez,
- şirket adı uydurulmaz,
- link hedefi seçilemez,
- yabancı şirket varlığı sızdırmayan generic unavailable state gösterilir,
- taze authorization/projection gelmeden işleme devam edilmez.

## 4. Route registry metadata

Static route’lar parameter route’tan önce çözülür.

| Route ID | Screen | Path | Öncelik | Context | Breadcrumb | Güvenli dönüş |
|---|---|---|---|---|---|---|
| `cari.list` | `C-01` | `/cari` | static | single veya explicit authorized multi-company read | Cari | dashboard |
| `cari.create` | `C-02:create-subset` | `/cari/yeni` | static-before-parameter | single-company | Cari → Yeni Cari | `cari.list` |
| `cari.company-link` | task-local workspace | `/cari/sirkete-bagla` | static-before-parameter | single-target-company | Cari → Şirkete Bağla | `cari.list` |
| `cari.detail` | `C-03` | `/cari/:partyId` | bütün Cari static route’larından sonra | single veya explicit authorized multi-company read | Cari → yalnız yetkili güncel Party etiketi | `cari.list` |

Deep-link ve refresh her seferinde taze capability ve backend authorization ister. Eski context Party etiketi breadcrumb, title, recent veya favorite içinde taşınmaz. Raw return URL kabul edilmez; yalnız accepted internal route identity kullanılır.

Feature cache namespace en az actor, session, context modu, company/yetkili-set revision, route parametreleri, normalize query ve contract version’a bağlıdır. Exact key formatı ERP-000K implementation kararıdır.

## 5. Capability matrisi

| Capability | API | Permission | Scope |
|---|---|---|---|
| `cari.parties.list` | `GET /api/v1/counterparties` | `counterparty.parties.read` | şirket veya explicit authorized multi-company read |
| `cari.parties.detail` | `GET /api/v1/counterparties/{partyId}` | `counterparty.parties.read` | şirket veya explicit authorized multi-company read |
| `cari.parties.create` | `POST /api/v1/counterparties` | `counterparty.parties.create` | company-wide single-company |
| `cari.accounts.status.update` | `PUT .../company-account/active-state` | `counterparty.accounts.status.update` | company-wide single-company |
| `cari.accounts.link` | `POST .../{partyId}/company-account` | `counterparty.accounts.create` **ve** `counterparty.parties.read` | ikisi de hedef şirkette company-wide |

UI capability’yi rol adından, JWT’den, localStorage’dan, route isminden veya `/identity/me` cevabından türetmez. Unknown, malformed veya stale capability fail-closed olur. Kontrol görünürlüğü authorization değildir; backend her çağrıda yeniden yetkilendirir.

## 6. Read model ve redaction

Party alanları:

- `id`, `code`, `kind`, `legalName`, `tradeName`, `isActive`, `version`.

Şirket hesabı alanları:

- `id`, `companyId`, `roles`, `isActive`, `version`.

`externalIntegrationCode`, dedicated read permission ve redaction owner kabul edilene kadar UI mapping’ine alınmaz. Global Party aktifliği ile şirket hesabı aktifliği ayrıdır. Cari kodu salt okunurdur.

## 7. `CARI-READ-PAGE-001` — accepted observable contract

Owner: `CHATGPT-BUSINESS/CARI`; takip: Issue `#94`; owner decision: `issuecomment-5470043284`.

### Limits/defaults

- `q`: opsiyonel, Unicode dış whitespace trim + NFC, en çok **200**; boşsa omitted.
- `pageSize`: **50 default**, **200 maximum**, minimum 1.
- `roleMatch=any`, `partyState=active`, `accountState=all`, `sort=code`, `direction=asc`, `includeCount=false` varsayılandır.
- Cursor azami yaşı **30 dakika**; session/policy/permission/yetkili-company-set revision değişimi daha erken invalid eder.

### Arama/sıralama

- code/legalName/tradeName contains.
- Türkçe case-insensitive ve accent-sensitive: `i=İ`, `ı=I`, `i!=I`, `İ!=ı`; accent folding yok.
- Exact SQL collation bu sözleşme tarafından seçilmez; gerçek SQL gözlenebilir sonuç kabul kaynağıdır.
- Her sort immutable PartyId unique tie-breaker taşır.
- Keyset/cursor paging zorunludur; offset paging ve bütün listeyi browser’a yükleme yasaktır.

### Context/cursor/count

- `X-Company-Id` varsa yalnız exact yetkili seçili şirket.
- Header yoksa yalnız bu route için server-resolved explicit authorized-multi-company-read.
- Browser fan-out/client union yasaktır.
- Cursor contract version, normalize query/filter/sort, actor/session, context modu, selected CompanyId veya authorized-company-set revision ve expiry’ye bağlıdır.
- malformed/tampered/expired/mismatch: `400/code=invalid_cursor`, partial data yok.
- `includeCount=true`, authorization/context/filter sonrasındaki exact distinct Party sayısıdır; hidden group toplamı yok.
- Uzun ömürlü global DB snapshot garantisi yoktur; concurrent business değişikliklerinde deterministic keyset continuation ve fail-closed scope binding korunur.

Minimum response: `items`, `nextCursor`, `hasMore`; count istenirse `totalCount`, `countScope`.

## 8. `CARI-LINK-CANDIDATE-001` — accepted observable contract

Owner: `CHATGPT-BUSINESS/CARI`; takip: Issue `#95`; owner decision: `issuecomment-5470044664`.

```http
GET /api/v1/counterparties/company-account-link-candidates
X-Company-Id: <server tarafından doğrulanan hedef şirket>
```

### Eligibility/security

- target yalnız resolved CompanyContext; body/query/route override yok,
- hedefte `counterparty.accounts.create` + `counterparty.parties.read` company-wide,
- Party global aktif,
- en az bir izinli source account üzerinden görünür,
- hedefe zaten bağlı değil,
- yalnız actor-visible source account özetleri,
- source account pasifliği tek başına onboarding engeli değil ve otomatik reactivation yok,
- browser fan-out/client union yok.

### Paging/disclosure

- Issue #94 normalizasyon, Turkish search ve authenticated cursor semantics yeniden kullanılır.
- `pageSize`: **25 default**, **100 maximum**, minimum 1.
- Cursor azami yaşı **30 dakika**; target company/actor/session/policy/query mismatch daha erken invalid eder.
- invalid cursor: `400/code=invalid_cursor`, partial candidate yok.
- Dönen alanlar yalnız Party `id/code/kind/legalName/tradeName/isActive` ve actor-visible source account `id/companyId/roles/isActive/version`.
- externalIntegrationCode, banka/vergi/sicil, bakiye, risk, hidden account ve foreign count/timing yok.

Önceki context PartyId yalnız hint’tir; fresh candidate response içinde yeniden görünmeden authority değildir. POST bütün şartları yeniden doğrular; aradaki inactive/link yarışı accepted `not_found` veya `already_linked` ile fail-closed olur.

## 9. Idempotency ve replay

Yeni Party create, account active-state update ve company-link tekil `Idempotency-Key` ister:

- tek field-value,
- Trim,
- 1–128 karakter,
- missing/empty/multiple/comma-joined/oversized fail-closed.

Her kullanıcı-onaylı logical submit tek key kullanır. Timeout veya sonucu bilinmeyen write yalnız aynı payload ve aynı key ile kontrollü retry edilir. Sonuç bilinmezken otomatik yeni key veya company switch ile write fence aşılmaz.

Replay header’ları:

```text
Idempotency-Replayed: true
Idempotency-Original-Correlation-Id: <ilk correlation>
```

Replay duplicate business effect, navigation veya toast üretmez.

## 10. Hata sahipliği

### Cari domain — accepted contract, partial implementation

Owner: Issue #93; owner decision: `issuecomment-5470041578`.

| Code | HTTP | Durum |
|---|---:|---|
| `validation_failed` | 400 | accepted; producer/test queued |
| `command_metadata_invalid` | 400 | accepted; producer/test queued |
| `invalid_cursor` | 400 | accepted; #94/#95 implementation queued |
| `not_found` | 404 | accepted; producer normalization queued |
| `already_linked` | 409 | PR #77’de fiziksel/real-SQL |
| `version_conflict` | 409 | accepted; producer normalization queued |
| `idempotency_conflict` | 409 | PR #77’de fiziksel/real-SQL |

`not_found` missing/foreign/hidden/global-inactive ayrımını sızdırmaz. `version_conflict` güvenli accepted `conflict` extension’ını korur. UI branching yalnız code ile yapılır; locale metni parse edilmez.

### Shared platform — owner `CODEX-PLATFORM`, Issue #96 / PR #97

| Code | HTTP |
|---|---:|
| `authentication_required` | 401 |
| `forbidden` | 403 |
| `internal_error` | 500 |
| `service_unavailable` | 503 |

Cari bu kodları sahiplenmez, yeniden tanımlamaz veya kopya registry üretmez; yalnız kabul edilmiş platform sözleşmesini tüketir. Unknown code güvenli generic unexpected-error state’e gider. ProblemDetails plain text render edilir; `innerHTML` yoktur.

## 11. UI state ve retry

Loading, no-data, filter-empty, partial/stale, 401, 403, non-disclosing 404, domain 409, replay, timeout/unknown write, 500, 502/503/504, malformed, offline ve stale-company response ayrı state’lerdir.

Read geçici hatalarda kontrollü retry sunabilir. Write sonucu bilinmiyorsa yalnız aynı payload/key kullanılabilir. Replay duplicate business effect, navigation veya toast üretmez.

## 12. Ekranlar

### C-01

Başlık ve capability-gated aksiyonlar, company/context göstergesi, server arama/filtre, yoğun liste ve masaüstü drawer/mobil tam detay. Kolonlar yalnız Cari kodu, adlar, tür, görünür roller/hesap sayısı, Party ve seçili hesap durumudur. Para/risk/mock KPI yoktur.

### C-02 create alt kümesi

Party türü, hukuki ad, opsiyonel ticari ad, roller ve shell’den salt okunur aktif şirket. VKN/TCKN/MERSİS/IBAN/banka/adres/kişi yoktur.

### C-03 çekirdek 360

Cari kimliği/ad/tür, global Party durumu ve yalnız yetkili şirket hesapları. Şirket adı/kodu Organization projection’dan gelir. C-04…C-12 owner kapıları kapanmadan boş sekme/mock kart yoktur.

### C-04…C-12

Her biri ayrı kanonik route/workspace olarak korunur: Cari Hesap Takibi, Açık Kalemler, Adres/Kişi/İletişim, Banka/Vergi/Sicil, Risk/Limit/Vade, Mutabakat, Belgeler/Sözleşmeler, Mükerrer/Birleştirme ve İlişki/Takip Görevleri.

## 13. Accessibility ve responsive acceptance

- Klavye ile liste/detail/create/link/status görevleri,
- görünür focus ve skip-link,
- semantic sort header + `aria-sort`,
- validation summary focus,
- modal/drawer focus trap, Escape ve invoker’a dönüş,
- spam üretmeyen live region,
- %200 zoom ve %400 reflow,
- reduced motion,
- rengin tek sinyal olmaması,
- mobil eylem/label parity.

## 14. Başlama kapıları

1. PR #77 merge + green new-main CI.
2. PR #102 veya successor generic mount contract kabulü.
3. ERP-000K feature mount, route registry, query/cache ve CompanyContext runtime fence.
4. Server-backed capability provider.
5. Issue #93 accepted contract’ın kalan error producer/test implementasyonu.
6. Issue #94 accepted read/page contract implementasyonu.
7. Issue #95 accepted candidate contract implementasyonu.
8. `ORGANIZATION-COMPANY-DIRECTORY-V1` owner projection/version kabulü.
9. Frontend single-writer handoff.

## 15. Completion gates

- C-01 single ve authorized multi-company read,
- C-02 create,
- C-03 core 360,
- link workspace,
- permission/CompanyContext fail-closed,
- real SQL ve API contract acceptance,
- Playwright/visual/accessibility,
- responsive task completion,
- service/malformed/unknown-write states,
- runner-backed full CI/container/UI golden,
- exact-head cross review,
- insan/UAT kabulü.

Bu dilim tamamlandığında dahi C-04…C-12 ile finansal, risk, iletişim, belge ve CRM alanları tamamlanmış sayılmaz.
