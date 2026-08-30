# CARI-UI-001 — Cari Kartlar ve Cari 360 İlk Uçtan Uca Dilim

- Sürüm: `1.4`
- Tarih: `2026-08-30`
- İş sahibi: `CHATGPT-BUSINESS/CARI`
- Ortak shell/capability/route/query-cache sahibi: `CODEX-PLATFORM`
- Organization şirket dizini sahibi: `CODEX-PLATFORM/ORGANIZATION`
- İncelenen ana dal: `main@f13d26b1a59089f25c5d44f6679d3928e876079c`
- Contract durumu: **ACCEPTED**
- Implementation: **BLOCKED**
- Production activation: **NO-GO**
- Event delivery: `synchronous_transaction`
- Takip: Issue `#78`

## 1. Amaç ve kullanıcı sonucu

Bu sözleşme ilk görünür Cari dikey diliminin iş ve güvenlik sınırını tanımlar. Tek başına production UI açmaz.

İlk dilim:

1. `C-01 Cari Kartlar`,
2. `C-02 Yeni Cari` ekranının mevcut backend tarafından desteklenen create alt kümesi,
3. `C-03 Cari 360` çekirdek özeti,
4. `CARI-LINK-WORKSPACE-001` ile mevcut global aktif Party’yi başka Göymen şirketine bağlama

ile sınırlıdır.

`C-04…C-12` ayrı kanonik ekran/workspace olarak korunur. Bakiye, açık kalem, risk, limit, adres, kişi, banka, vergi, sicil, belge, sipariş, stok, tahsis, CRM ve AI sonucu owner backend gerçeği olmadan mock, sıfır veya sahte KPI olarak gösterilmez.

## 2. Current-main doğrusu

Aşağıdaki backend/platform ön koşulları ana dala alınmıştır:

- PR `#106` / kaynak PR `#77`: global aktif Party’yi hedef şirkete bağlayan company-account link dilimi;
- PR `#107` / kaynak PR `#97`: shared `401/403/500/503` ProblemDetails sözleşmesi;
- ana dal CI `33326894439` ve UI golden `33326894409`: runner-backed **SUCCESS**.

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

Shell handoff tamamlanana kadar ChatGPT-Cari `App.tsx`, global CSS/router/query/capability client ve ortak navigasyonda read-only’dir.

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

| Route ID | Screen | Path | Context | Güvenli dönüş |
|---|---|---|---|---|
| `cari.list` | `C-01` | `/cari` | single veya explicit authorized multi-company read | `dashboard` |
| `cari.create` | `C-02:create-subset` | `/cari/yeni` | single-company | `cari.list` |
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

UI capability’yi rol adından, JWT’den, localStorage’dan, route isminden veya `/identity/me` cevabından türetmez. Unknown, malformed veya stale capability fail-closed olur. Kontrol görünürlüğü authorization değildir; backend her çağrıda yeniden yetkilendirir.

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
| `validation_failed` | 400 | producer/test pending |
| `command_metadata_invalid` | 400 | producer/test pending |
| `invalid_cursor` | 400 | Issues #94/#95 ile pending |
| `not_found` | 404 | normalization pending |
| `already_linked` | 409 | PR #106 ile main’de |
| `version_conflict` | 409 | normalization pending |
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

## 13. Tamamlanan ve açık kapılar

Tamamlanan:

- [x] PR #106 company-link backend merge.
- [x] PR #107 shared platform error merge.
- [x] Main runner-backed CI + real SQL + container + UI golden green.
- [x] Issues #93/#94/#95 observable iş sözleşmeleri kabul edildi.

Açık ve UI implementation’ı bloke edenler:

1. PR #102 veya successor generic mount contract’ın current-main kabulü.
2. ERP-000K runtime feature mount, route registry, query/cache ve CompanyContext fence.
3. Server-backed capability provider.
4. Issue #93 kalan producer/test implementasyonu.
5. Issue #94 cursor-paged read API implementasyonu.
6. Issue #95 target-company candidate API implementasyonu.
7. `ORGANIZATION-COMPANY-DIRECTORY-V1` owner projection/version kabulü.
8. Frontend single-writer handoff.
9. Playwright/visual/accessibility ve açık UAT kabulü.

Bu contract’ın merge edilmesi UI’yı veya production activation’ı açmaz. İlk görünür Cari dilimi ancak bütün açık kapılar, gerçek SQL ve browser acceptance ile tamamlanır.
