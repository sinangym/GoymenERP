# ChatGPT–Codex Paylaşımlı Yürütme El Kitabı v1.0

**Bağlı plan:** `GOYMEN_ERP_END_TO_END_EXECUTION_PLAN_V1.md`  
**Makine-okunur plan:** `docs/backlog/erp_e2e_workstreams_v1.json`  
**Başlangıç main:** `fed32839a9e0b4258efa5980fddc2846aaf85c49`

---

## 1. Ortak çalışma modeli

ChatGPT ve Codex iki ayrı üretim ajanıdır; birbirlerinin kopyası değildir.

- GitHub ortak hafızadır.
- Her iş exact `main` SHA ile başlar.
- Her aggregate/shared yüzeyde tek writer vardır.
- Diğer ajan read-only reviewer'dır.
- Yeni commit, eski CI ve review kanıtını geçersiz kılar.
- İşin amacı doküman üretmek değil, çalışan kullanıcı sonucu oluşturmaktır.
- Bilinen ekran/iş kararları tekrar analiz edilmez.
- Yalnız para, stok, muhasebe, hukuk, yetki, tolerans, saklama ve cutover gibi D2 konular kullanıcı kararı bekler.
- Her modül dikey dilimler halinde tamamlanır.

---

# BÖLÜM A — CHATGPT-BUSINESS PLANI

## A1. Rol

ChatGPT-BUSINESS aşağıdaki rollerin birleşimidir:

- iş analisti;
- domain tasarımcısı;
- modül backend geliştiricisi;
- modül React geliştiricisi;
- formül ve kabul testi sahibi;
- UX/ekran referansı eşleyicisi;
- veri migrasyon eşleme sahibi;
- Codex platform PR'larının iş doğruluğu reviewer'ı.

Kullanıcı ürün sahibidir. ChatGPT kullanıcı adına geri döndürülemez D2 kararı vermez.

## A2. Yazma sahipliği

ChatGPT'nin ana production yazma alanları:

```text
src/backend/Modules/Counterparty/**
src/backend/Modules/Product/**
src/backend/Modules/Sales/**
src/backend/Modules/Purchasing/**
src/backend/Modules/BusinessPurchases/**
src/backend/Modules/Inventory/**
src/backend/Modules/Allocation/**
src/backend/Modules/Manufacturing/**
src/backend/Modules/Logistics/**
src/backend/Modules/Finance/**
src/backend/Modules/Accounting/**
İlgili module-owned frontend route/workspace/component dosyaları
İlgili module-owned tests/docs/contracts
```

ChatGPT aşağıdaki alanlara ancak açık tek-writer devriyle yazar:

```text
.github/**
Identity generic seeder/reconciler
shared design-system core
shared worker/outbox/search/AI gateway
shared deployment/backup tooling
```

## A3. İlk aktif görevler

### A3.1 PR #60

1. `main` ve PR head canlı okunur.
2. PR #61 merge commit'iyle non-destructive sync yapılır.
3. Diff'in yalnız parser + acceptance kapsamı olduğu doğrulanır.
4. Real SQL boundary testleri çalıştırılır.
5. Full CI ve UI golden tamamlanır.
6. Codex exact-head re-review istenir.
7. P0/P1 yoksa insan security merge kapısına sunulur.
8. Merge sonrası Issue #54/#56 kapanış gerçeği güncellenir.

### A3.2 Issue #53

Ayrı branch:

```text
hotfix/chatgpt/cari-audit-event-tenant-guard-v1
```

Teslimat:

- AuditEvent global query filter;
- active CompanyContext write guard;
- interceptor sonrası eklenen audit için guard;
- A/B gerçek SQL görünürlük testi;
- wrong-company audit insert rollback;
- append-only ve sidecar migration ownership koruması.

### A3.3 PR #64 review

ChatGPT şunları doğrular:

- kanonik kaynak `main`deki Option A contractıdır;
- yalnız SYSTEM_ADMINISTRATOR;
- missing admin rolü oluşturulmaz;
- diğer roller ve assignment'lar değişmez;
- custom permission korunur;
- transaction/concurrency/collision gerçek SQL kanıtı;
- production runner iddiası yok;
- #62/#63 ayrı kalır;
- schema/migration değişikliği yok veya gerekçesi açık;
- PR #36/ChatGPT branch'ine write yok.

Sonuç exact SHA ile PR #64'e yazılır.

### A3.4 PR #31 review

#64 ve #60 sonrası current-main head üzerinde:

- exact beş dosya;
- ilk `dotnet` öncesi preflight;
- tool/restore/build/test/EF/container sınırları;
- NuGet ve response-file koruması;
- Counterparty/iş modülü davranışının değişmemesi;
- full CI ve container evidence

incelenir.

### A3.5 PR #36 finalizasyonu

#64, #60, #31 ve #53 main'e girdikten sonra:

1. current main iki-parent/non-destructive sync;
2. operator role kalıntılarının kaldırılması;
3. duplicate header hunklarının kaldırılması;
4. reason alanının kullanıcı açısından opsiyonel tutulması;
5. permission registry ve admin seed davranışının main'den devralınması;
6. Party visibility'nin yetkili mevcut account üzerinden doğrulanması;
7. route, docs ve tests Option A ile uyumlu hale getirilmesi;
8. gerçek SQL full acceptance;
9. Codex exact-head review;
10. insan merge kapısı.

## A4. Dalga bazlı ChatGPT planı

### W1 — Cari UI ve Ürün/Hizmet

ChatGPT paralel olmayan iki ardışık ana dilim açar:

#### Dilim CARI-UI-001

Kullanıcı sonucu:

> Yetkili kullanıcı Cari listesinden kaydı açar, grup kimliği ve şirket hesaplarını Cari 360'ta görür.

Kapsam:

- route ve permission;
- list filters/search;
- Cari 360 özet ve şirket hesapları;
- create/status/link aksiyonları;
- documents tab placeholder yerine gerçek Document Vault linki;
- loading/empty/error/403/404/conflict;
- Playwright ve visual test.

#### Dilim PRD-CORE-001

Kullanıcı sonucu:

> Yetkili kullanıcı yeni ürün oluşturur; stok, alış, satış ve paket birimlerini tanımlar.

Kapsam:

- Product aggregate;
- taxonomy ve attribute set;
- immutable user code;
- unit conversions;
- package standard v1;
- product list/detail/form;
- SQL constraints ve audit.

#### Dilim PRD-PRICE-001

- liste fiyatı;
- currency/unit/validity;
- cari/ürün kapsamı;
- minimum fiyat ve iskonto contractı;
- preview ve approval connection.

### W2 — Satış ve Satın Alma

#### SAL-ORDER-001

- draft sales order;
- customer and product selection;
- quantity/unit/price/currency;
- line and header delivery date;
- snapshot semantics;
- list/detail/create UI.

#### SAL-APPROVAL-001

- price/discount/margin/risk preview;
- approval/revision;
- change diff;
- immutable previous revision;
- cancellation impact list.

#### PUR-ORDER-001

- supplier purchase order;
- line delivery segments;
- ready/in-transit information;
- approval/revision;
- purchase remaining calculation;
- future supply projection event.

#### OPR-PURCHASE-001

- non-commercial operating purchase request;
- category, cost center and approval;
- no pollution of commercial Product catalog.

### W3 — Stok ve Tahsis

#### INV-LEDGER-001

- StockEvent aggregate;
- opening and goods-receipt event;
- position projection;
- ownership/responsibility/location/trade context;
- physical stock screen.

#### INV-LABEL-001

- label/lot/heat/coil/MTC;
- split/merge events;
- print preview;
- immutable old label history.

#### ALC-SALES-001

- Sales Order demand;
- physical label source;
- atomic allocation;
- partial/release/cancel;
- over-allocation race.

#### INV-GR-001

- purchase order to goods receipt;
- accepted/rejected/net quantity;
- future supply to physical transformation;
- supplier and document links.

#### INV-COUNT-TRANSFER-001

- count and approval;
- quality/legal/system block;
- two-leg transfer;
- external processing ownership preservation.

### W4 — Üretim

Ortak altyapıdan sonra her motor ayrı PR trenidir:

1. `MFG-SLIT-WIND-001`.
2. `MFG-COLD-ROLL-001`.
3. `MFG-TUBE-PROFILE-001`.
4. `MFG-PAINT-001`.
5. `MFG-CUT-TO-LENGTH-001`.
6. `MFG-QUEUE-001`.
7. `MFG-REALIZATION-001`.
8. `MFG-GENEALOGY-001`.

Her motor:

- DoR;
- formül ID/sürüm;
- calculator API;
- preview hash;
- plan DB/API/UI;
- actual realization;
- mass balance;
- stock/genealogy effects;
- real SQL/golden/E2E

taşır.

### W5 — Lojistik

1. Load instruction.
2. Vehicle/driver/stops.
3. Actual label loading.
4. Weighbridge.
5. Shipment/partial shipment.
6. Dispatch note.
7. Invoice-ready grouping.
8. Purchase goods receipt/return alignment.

### W6 — Finans ve Muhasebe

#### FIN-OPENITEM-001

- invoice/open-item model;
- aging;
- original currency/local equivalent;
- no automatic customer/supplier netting.

#### FIN-SOURCE-DISTRIBUTION-001

- bank/cash/dekont financial source;
- many-to-many allocation;
- partial/multiple closure;
- no duplicate movement.

#### FIN-BANK-RECON-001

- bank statement import;
- 1:1, 1:n, n:1, n:n matching;
- unmatched lines;
- reversal.

#### FIN-INSTRUMENTS-001

- check/note;
- POS gross/net/commission;
- DBS collateral versus real receipt;
- company card and advance.

#### FIN-TREASURY-001

- payment package;
- 13-week cash;
- credit facility/drawdown;
- limit/collateral/covenant;
- budget.

#### ACC-CORE-001

- chart of accounts;
- voucher/voucher line;
- operation-to-voucher draft;
- trial balance;
- period lock;
- external accounting reconciliation.

### W7–W8

- semantic report definitions;
- AI tool business contracts;
- migration mappings;
- UAT scripts;
- cutover reconciliation;
- user training data and help content.

## A5. ChatGPT her PR öncesi kontrol listesi

```text
[ ] current main SHA recorded
[ ] no existing PR implements the same slice
[ ] source-of-truth module identified
[ ] user outcome is one sentence
[ ] D2 decisions closed or exact blocker recorded
[ ] data, state, formula and unit rules defined
[ ] CompanyContext and permissions defined
[ ] audit/idempotency/concurrency decided
[ ] UI reference and states defined
[ ] real SQL acceptance cases defined
[ ] Codex read-only boundary declared
```

## A6. ChatGPT review kontrol listesi

Codex PR'ı incelenirken:

```text
[ ] accepted business contract is not reinterpreted
[ ] no new role, permission, status or formula is invented
[ ] no company/data-scope leak
[ ] generic platform code does not own module semantics
[ ] no silent startup/production activation
[ ] negative and real SQL evidence is meaningful
[ ] diff is within declared scope
[ ] current-head CI is fresh
```

---

# BÖLÜM B — CODEX-PLATFORM PLANI

## B1. Rol

Codex aşağıdaki alanların ana teknik yürütücüsüdür:

- platform architecture;
- Identity/auth/security;
- shared frontend shell/design system;
- generic workflow/approval/task;
- documents, workers, search and AI infrastructure;
- CI/CD, observability, migration, backup/recovery;
- SQL/concurrency/security reviewer.

Codex, modül iş anlamını ChatGPT contractı veya `main`deki accepted source olmadan üretmez.

## B2. Yazma sahipliği

Codex ana production alanları:

```text
src/backend/Modules/Identity/**
src/backend/BuildingBlocks/**
src/backend/GoymenERP.Api/Security/**
shared frontend shell/design-system/router/cache/test harness
Document/Audit platform infrastructure where assigned
Worker/Outbox/Inbox/Search/AI/Observability modules
.github/**
tools/**
infra/**
deployment/backup/recovery tooling
platform architecture tests
```

Codex aşağıdaki alanlara production write yapmaz:

```text
ChatGPT-owned active business module branch
Counterparty/Product/Sales/Purchasing/Inventory/Manufacturing/Finance business semantics
formül/tolerans/business status without accepted contract
parallel/chatgpt/**
hotfix/chatgpt/**
```

## B3. İlk aktif görevler

### B3.1 PR #64

- full exact-head CI tamamlanır;
- all changed files and diff reviewed;
- no operator role/new assignment;
- real SQL fresh/existing/no-op/concurrent/collision/rollback evidence;
- PR body and Issue #57 handoff current head ile güncellenir;
- ChatGPT review istenir;
- insan merge onayı olmadan merge edilmez.

### B3.2 PR #60 re-review

ChatGPT current-main sync yaptıktan sonra:

- only two-file scope;
- parser exact-one + trim + 1..128 + comma reject;
- missing case wire-real;
- invalid cases zero effect;
- exact-128 replay;
- current-main full CI

incelenir.

### B3.3 PR #31

#64 ve #60 sonrasında:

- new main sync;
- unchanged five-file security delta;
- isolated preflight;
- restore/build/test/EF/container order;
- current module count and migrations;
- full CI and container;
- ChatGPT review;
- human security merge.

### B3.4 React shell

Branch:

```text
codex/ui-common-shell-v1
```

Dilimler:

1. design tokens and base layout;
2. nav/topbar/company switcher;
3. router/permission/feature registry;
4. grid/form/workspace/drawer primitives;
5. query cache and company-switch invalidation;
6. 401/403/404/error boundary;
7. responsive and accessibility harness;
8. Playwright/visual infrastructure.

ChatGPT, ekran anatomisi ve master report uyumunu review eder.

### B3.5 Minimal Document Vault

Branch treni:

- `codex/document-vault-core-v1`;
- `codex/pdf-turkish-filename-v1`;
- `codex/document-link-ui-v1`.

Minimum:

- blob/object technical ID;
- hash dedup without existence leak;
- version;
- metadata;
- company/permission;
- multi-record links;
- signed/read access abstraction;
- malware/quarantine contract;
- Turkish filename and embedded font golden.

### B3.6 Approval/task engine

- generic request/step/decision;
- amount/currency/risk/tolerance policy inputs;
- no hard-coded Sales or Finance semantics;
- delegation, timeout and escalation;
- immutable preview/hash;
- audit and separation-of-duties hooks;
- work center projection.

## B4. Dalga bazlı Codex planı

### W1

- common shell;
- task center skeleton;
- document vault minimum;
- PDF runtime;
- UI test harness;
- feature flags;
- safe config and route metadata.

### W2

- approval/revision/task infrastructure;
- number-series technical service;
- PDF template pipeline;
- notification event base;
- shared diff/preview component.

### W3

- concurrency/locking performance review;
- label/barcode infrastructure;
- projection rebuild harness;
- allocation stress tests;
- optional outbox minimum if async projection is selected.

### W4

- generic versioned calculator runtime;
- work calendar and scheduling engine;
- realtime production queue;
- print/label runtime;
- benchmark and deterministic test seam.

### W5

- realtime loading updates;
- device/scale adapter contract without vendor guess;
- safe print/export;
- shipment and document integration review.

### W6

- bank file import framework;
- generic matching engine;
- scheduler and job locks;
- payment file/export security;
- finance concurrency/load tests;
- accounting integration adapter base.

### W7

- outbox/inbox production implementation;
- search projections and index;
- AI gateway/tool registry;
- OCR/document processing workers;
- observability/SLO;
- backup/restore;
- release artifacts, SBOM/provenance and deployment promotion.

### W8

- migration runner;
- deterministic batch reports;
- reconciliation tooling;
- cutover automation;
- backup/restore and rollback rehearsal tooling.

## B5. Codex her PR öncesi kontrol listesi

```text
[ ] live main fetched
[ ] accepted module contract exists
[ ] exact writer boundary declared
[ ] no ChatGPT-owned active branch touched
[ ] no business decision invented
[ ] real SQL/performance/security risks identified
[ ] production activation explicitly in or out of scope
[ ] rollback/forward-fix behavior declared
[ ] ChatGPT review target defined
```

## B6. Codex review kontrol listesi

ChatGPT modül PR'ı incelenirken:

```text
[ ] DB constraints enforce critical invariants
[ ] CompanyId and permission are server derived
[ ] no enumeration/existence oracle
[ ] audit/idempotency transaction is atomic
[ ] same-key/different-key/stale-version behavior is deterministic
[ ] real SQL is used for relational behavior
[ ] migrations and model snapshot agree
[ ] API and UI cannot bypass domain service
[ ] performance and query shape are bounded
[ ] no wildcard guard exception or test skip
[ ] exact-head CI is fresh
```

---

# BÖLÜM C — ORTAK İŞ DÖNGÜSÜ

## C1. Her yeni dilimin başlangıcı

1. `git fetch --all --prune`.
2. Current `main` SHA kaydı.
3. Aynı sonucu yapan açık PR/issue taraması.
4. Master report, latest decision ve accepted contract okuması.
5. `SLICE-CONTRACT` oluşturma.
6. Writer/reviewer/readonly boundaries.
7. Branch açma.

## C2. SLICE-CONTRACT

```text
SLICE-CONTRACT-V1
ID:
WORKSTREAM:
USER OUTCOME:
SOURCE OF TRUTH:
IN SCOPE:
OUT OF SCOPE:
DATA/STATE:
FORMULA/UNIT/CURRENCY:
PERMISSION/SCOPE:
AUDIT/REVERSAL:
IDEMPOTENCY/CONCURRENCY:
UI REFERENCE/STATES:
ACCEPTANCE:
D2 DECISIONS:
WRITER:
REVIEWER:
```

## C3. PR treninde dosya paylaşımı

Tek kullanıcı sonucu gerekirse ardışık PR'lara ayrılır:

```text
A. domain + migration
B. API + security + real SQL
C. React workspace + E2E
D. integration/projection + UAT
```

Ancak A merge edildiğinde feature flag kapalı kalır; C/D tamamlanmadan modül tamamlandı denmez.

## C4. Cross-review

- Yazar self-review yapar.
- Diğer ajan exact head'i canlı diff ve CI ile inceler.
- Bulgu ilgili PR'a yazılır.
- Düzeltmeyi branch sahibi yapar.
- Yeni head için review yenilenir.

## C5. Kullanıcıya sunulacak çıktı

Her kabul edilmiş dilim sonunda kullanıcıya:

- ne çalışıyor;
- hangi ekranlar;
- hangi iş zinciri;
- hangi testler;
- açık D2 karar;
- sonraki dikey dilim

tek özet halinde verilir.

---

# BÖLÜM D — CODEX'E VERİLECEK KANONİK TALİMAT

```text
GöymenERP için GitHub tabanlı paylaşımlı yürütme planını uygula.

Önce canlı repository gerçeğini yeniden oku:
- current main
- docs/delivery/GOYMEN_ERP_END_TO_END_EXECUTION_PLAN_V1.md
- docs/delivery/CHATGPT_CODEX_EXECUTION_PLAYBOOK_V1.md
- docs/backlog/erp_e2e_workstreams_v1.json
- Issue #40
- kendi aktif PR/issue'ların

Kurallar:
1. Sohbet özeti yerine exact SHA, diff, CI ve main contractlarını kaynak kabul et.
2. Aynı aggregate/shared dosyada tek writer kuralını koru.
3. ChatGPT-owned parallel/chatgpt/* ve hotfix/chatgpt/* branch'lerine write yapma.
4. İş anlamını, rolü, formülü, statüyü veya para/stok etkisini kabul edilmiş source olmadan üretme.
5. Her iş bir kullanıcı sonucu taşıyan vertical slice olmalı.
6. P0/P1 dışındaki hardening'i ayrı issue'ya taşı; ana iş akışını gereksiz durdurma.
7. UI, API, DB, security, real SQL ve E2E birlikte tamamlanmadan modülü done sayma.
8. Her PR HANDOFF-V1 ve exact-head CI taşımalı.
9. Yeni commit eski review/CI'yı stale yapar.
10. Auto-merge yapma; kritik Identity/CI/schema/production işlerinde insan merge kapısını koru.

Aktif öncelik:
- PR #64'ü exact-head CI ve handoff ile bitir.
- ChatGPT PR #60'ı current main'e sync ettikten sonra read-only re-review yap.
- #64/#60 sonrası PR #31'i current main'e sync edip güvenli merge kapısına getir.
- Sonra W1 ortak React shell/design system/test harness işine başla.
- ChatGPT'nin CARI-UI-001 ve PRD-CORE-001 dilimleriyle contract bazlı paralel ilerle.

Her bulguyu yalnız sohbet içinde bırakma; ilgili PR/issue'ya exact SHA ile yaz.
```

---

# BÖLÜM E — KULLANICI KARAR YÜKÜNÜ AZALTMA

Kullanıcıya yalnız D2 paketleri sunulur. Her D2 paketinde önerilen varsayılan bulunur. Aynı konu tekrar sorulmaz. Karar verildiğinde:

1. kanonik issue/decision kaydı;
2. module contract;
3. implementation;
4. real-SQL/UAT acceptance

zinciri tamamlanır.

Rutin teknik seçimler ajanlar tarafından yapılır ve PR'da görünür bırakılır.
