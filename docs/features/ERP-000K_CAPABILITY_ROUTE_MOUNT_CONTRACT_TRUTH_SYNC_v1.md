# ERP-000K Capability / Route / Feature-Mount — Current-Main Truth-Sync Amendment v1

- Durum: `DRAFT / OPEN / HUMAN UI-SECURITY-ARCHITECTURE REVIEW REQUIRED`
- Bağlı normatif metin: `docs/features/ERP-000K_CAPABILITY_ROUTE_MOUNT_CONTRACT_DRAFT_v1.md`
- Bağlı üst sözleşme: `ERP-000K-SHELL-CONTRACT` v1
- İncelenen current main: `f13d26b1a59089f25c5d44f6679d3928e876079c`
- ERP-000K implementation: `BLOCKED`
- Production activation: `NO-GO`
- Sorumluluk: `CODEX-PLATFORM`

## 1. Bu ekin amacı ve önceliği

Bu dosya, bağlı normatif metnin güvenlik ve sahiplik değişmezlerini yeniden yazmaz. Yalnız
ilk taslağın oluşturulduğu `main@83197c50...` sonrasında değişen canlı GitHub durumunu
current-main ile eşler.

Bu ek aşağıdaki **durum bildiren** cümleleri supersede eder:

1. ana metnin üst bilgisindeki `İncelenen taban` değeri;
2. bölüm 3 `Current-main envanteri` içindeki sonradan değişen fiziksel artifact durumu;
3. bölüm 7.1 içindeki PR #77/#79 kabul ve merge durumu;
4. bölüm 10 içindeki platform-owned hata sözleşmesinin fiziksel mevcudiyeti;
5. bölüm 15 içindeki Issue #87 zero-step ve tek-dosya exact-scope cümleleri.

Bunun dışındaki capability, route registry, feature mount, CompanyContext, non-disclosure,
anti-rollback, cache, safe-return, owner declaration, gerçek SQL ve accessibility
zorunlulukları bağlı normatif metindeki haliyle aynen geçerlidir.

Çelişkide bu ek yalnız canlı durum ve bu exact successor scope için üstündür; iş anlamı,
permission, endpoint, payload, router, package, eşik veya production davranışı üretmez.

## 2. Current-main fiziksel gerçek

`main@f13d26b1a59089f25c5d44f6679d3928e876079c` üzerinde:

- PR #103 ile group-shared Product güvenlik sınırı docs-only contract olarak fiziksel
  durumdadır; Product schema/API/UI veya production write hâlâ yoktur.
- PR #106 ile mevcut global aktif Party'yi başka şirkete bağlayan Cari command'i ve
  module-owned `already_linked` / `idempotency_conflict` kodları fiziksel durumdadır.
- PR #107 ile platform-owned `authentication_required`, `forbidden`, `internal_error` ve
  `service_unavailable` ProblemDetails sözleşmesi fiziksel durumdadır.
- PR #106 ve PR #107 sonrasında fresh new-main backend, frontend, gerçek SQL, container ve
  UI golden koşuları başarıyla tamamlanmıştır.
- `src/frontend/goymen-erp-web` hâlâ production router, shared query/cache, server-backed
  capability provider, feature registry, accepted browser-E2E harness veya owner feature
  mount runtime'ı içermez.
- `App.tsx` bootstrap shell'dir; UI kataloğundaki 159 kanonik ekran production route veya
  menü sayılmaz.
- `GET /api/v1/identity/me` permission/capability authority değildir.
- `GET /api/v1/organization/companies` route/action capability registry değildir.
- `ERP-000K-SHELL-CONTRACT` fiziksel olsa da `OPEN`; ERP-000K implementation `BLOCKED`,
  production `NO-GO` durumundadır.

Bu fiziksel ilerlemeler generic shell implementation başlangıcını kendiliğinden
yetkilendirmez.

## 3. Cari sahiplik ve tüketim truth-sync'i

- PR #77'nin reviewed feature içeriği, non-draft transport PR #106 üzerinden main'e
  alınmış ve new-main acceptance yeşil olmuştur.
- Cari company-link endpointi, izinleri, lifecycle ve domain conflict kodları
  `CHATGPT-BUSINESS/CARI` owner kaynağıdır; shared shell bunları ikinci kez tanımlamaz.
- PR #79 Cari UI owner declaration/vertical-slice contract'ı henüz main'de değildir.
  Accepted successor head main'e girene kadar production registry onu tüketemez.
- PR #44 gibi henüz merge edilmemiş Cari metadata girdileri coordination evidence olabilir;
  accepted repository source yerine geçmez.
- Shared shell, Cari search/page/cursor limitlerini, company-link candidate davranışını,
  Organization projection'ını veya domain code anlamını tahmin etmez.
- Owner declaration accepted ve repository-visible olduğunda generic registry onu strict
  schema/version/parity ile tüketir; ayrı hard-coded Cari envanteri üretmez.

## 4. Platform hata sahipliği truth-sync'i

Platform HTTP error contract artık main'de fiziksel olduğundan generic shell:

- `401/authentication_required`;
- `403/forbidden`;
- `500/internal_error`;
- kabul edilmiş producer'da `503/service_unavailable`

sonuçlarını platform-owned stable code olarak tüketebilir.

Bu fiziksel mevcudiyet:

- capability veya authorization üretmez;
- owner-domain `400/404/409` kodlarını platforma taşımaz;
- Türkçe `title/detail` metnini machine discriminator yapmaz;
- unknown/newer owner code'u tahmin etmeye izin vermez;
- owner renderer ve `(owner, contract version, code)` bütünlüğü ihtiyacını kaldırmaz.

Cari `already_linked` ve `idempotency_conflict` module-owned kalır. Gelecek Cari code seti
Issue #93 owner sınırında ilerler.

## 5. Issue #87 ve exact-head kanıt kuralı

GitHub Actions billing/spending-limit kaynaklı runner başlamama kapısı çözülmüştür. Bununla
birlikte:

- yalnız aynı exact head için gerçek runner adımları bulunan backend, frontend, gerçek SQL,
  container ve UI golden başarıları kabul kanıtıdır;
- eski SHA, eski base veya zero-step koşu yeni head için kullanılamaz;
- branch protection, required checks, bağımsız reviewer ve force-push/delete enforcement
  Issue #87'de açık yönetişim borcu olarak kalır;
- main protected olmadığı sürece expected-head, tek-yazar, fresh review ve manuel squash
  disiplini fail-closed süreç kontrolüdür.

## 6. Bu successor head için exact scope amendment

Bağlı normatif metnin bölüm 15'teki ilk taslak one-file allowlist'i, yalnız bu current-main
successor için aşağıdaki exact iki-dosya scope ile değiştirilir:

1. `docs/features/ERP-000K_CAPABILITY_ROUTE_MOUNT_CONTRACT_DRAFT_v1.md`
2. `docs/features/ERP-000K_CAPABILITY_ROUTE_MOUNT_CONTRACT_TRUTH_SYNC_v1.md`

Birinci dosyanın reviewed normative blob'u değiştirilmez; bu ikinci dosya yalnız durum
truth-sync'i ve exact-scope amendment'ıdır. Production source, React, CSS, package/lockfile,
API, schema/migration, workflow, task manifest, karar sicili, global acceptance matrisi ve
UI katalog dosyaları bu successor scope'ta değişmez.

Aşağıdaki kapılar aynen zorunludur:

- exact iki-dosya allowlist ve `git diff --check`;
- task manifest görev/edge/status/dependency parity;
- karar sicili Markdown/CSV parity;
- UI source/canonical-screen golden ve validator parity;
- global acceptance row parity;
- fresh exact-head backend, frontend, gerçek SQL, container ve UI golden;
- bağımsız UI-security-architecture review'da P0/P1 olmaması;
- Draft/human-review kapısı ve expected-head squash merge;
- merge sonrası fresh new-main doğrulaması.

## 7. Değişmeyen NO-GO

Bu iki-dosyalı contract bundle'ın merge edilmesi dahi şunlara izin vermez:

- capability endpointi veya serialization seçimi;
- permission/scope payload'ını client'a açma kararı;
- router, query/cache, form, grid, icon, browser-E2E veya başka package seçimi;
- production route/menu/action/capability kaydı;
- shared App/router/global CSS veya feature runtime implementation;
- Cari/Product/başka owner iş anlamını platformda hard-code etme;
- mock search, AI, notification, KPI veya business result gösterme;
- ERP-000K statüsünü `ready/done` yapma;
- production activation.

Sonuç:

```text
GENERIC CONTRACT BUNDLE: DRAFT / REVIEWABLE
ERP-000K-SHELL-CONTRACT: OPEN
ERP-000K IMPLEMENTATION: BLOCKED
PRODUCTION: NO-GO
AUTO-MERGE: FORBIDDEN
```
