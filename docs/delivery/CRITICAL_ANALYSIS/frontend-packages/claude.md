# Kritik Analiz — Frontend Paket Aday Envanteri (minimum staged)

- Tarih: 2026-08-31
- Protokol: `GOYMEN_ERP_OTONOM_TESLIM_PLANI_v2` (v2.1) §9 + `ERP-000K_SHELL_CONTRACT_v1.md` §19
- Bağlı: Issue #122 (`W0-CICD-001` OpenAPI/generated-client), Issue #128 (SHELL-PILOT-01A Codex), Issue #129 (SHELL-PILOT-01B Claude)
- İncelenen main: `d3f2b30` (PR #132 SHELL-PILOT-01A backend merge oldu)
- Analizi yapan: `CLAUDE-FRONTEND` — dosya `docs/delivery/CRITICAL_ANALYSIS/frontend-packages/claude.md` (v2.1 ajan-ayrık; Codex `codex.md` = PR #135)
- Durum: **REV.4 — Codex `codex.md` (PR #135) supply-chain review'u ile YAKINSANDI; Stage 0 router/query ADR adayı hazır; OpenAPI generator NO-GO**

---

## Revizyon günlüğü

| Rev | Değişiklik |
|---|---|
| 0 | 12 kategori stack önerisi (head `00e8a0c`) |
| 1–2 | ChatGPT `BUSINESS-NONBLOCKING`; Issue #122 cross-lane bağımlılığı |
| 3 | ChatGPT+Codex 5×P1: **(1)** "stack önerisi" → **aday envanter**; paket kararı Codex exact-version supply-chain review + Issue #122. **(2)** **minimum staged**: yalnız Stage 0 (SHELL-PILOT-01B için router + data-fetch + CSS) analiz ediliyor; tablo/form/headless-a11y/virtualization/ikon **ertelendi** — ihtiyaç doğduğunda ayrı analiz. **(3)** dış olgusal iddialar (maintenance/bundle/"yaygın"/lisans) `[CODEX-VERIFY]` işaretli — bu doküman doğrulamıyor. **(4)** abartılı mimari iddialar kaldırıldı (router loader ≠ auth; `removeQueries`+`AbortController` ≠ fence — Issue #124/#119; TanStack Table + React Aria a11y kompozisyonu otomatik değil; Zod ≠ ikinci OpenAPI gerçeği; WebKit ≠ tam Safari/iOS kabulü). **(5)** ajan-ayrık yola taşındı; `AUTONOMOUS_PROGRESS.md`'den el çekildi. |
| 4 | Codex `codex.md` (PR #135, `main@d3f2b30`) supply-chain review'u ile yakınsandı: **router = `react-router@8.3.1`** (exact pin, MIT, tek dep `cookie-es`, TS6/Vite8 probe OK), **server-state = `@tanstack/react-query@5.102.8`** (exact pin, MIT, tek dep `query-core`), **client-state = React context+reducer** (yeni store paketi yok), **CSS Modules** (yeni runtime yok). TanStack Router Stage 0 için **reddedildi** (TS6 declaration hatası + ~60 lock girdisi). **OpenAPI generator = NO-GO**: `openapi-typescript@7.13.0` TS6 `ERESOLVE` + `openapi-fetch` maintenance-mode; `@hey-api/openapi-ts` 4 high advisory. Sıradaki generator adımı: canonical backend OpenAPI'den **sonra** `orval@8.27.0` yalnız `codex.md` §5 14-madde fail-close PoC profiliyle. |

---

## 1. Bu dokümanın sınırı

- **Paket KURMAZ, versiyon SEÇMEZ, lisans/güvenlik GERÇEĞİ İDDİA ETMEZ.**
- Paket kurma yetkisi: (a) `CODEX-PLATFORM` exact-version primary-source + supply-chain review (Issue #122), (b) yakınsarsa `ADR-frontend-stack`, (c) sonra `SHELL-PILOT-01B` foundation PR (Claude, pinned + lockfile).
- Bu doküman yalnız **aday envanter** + `DEC-TEC-*` kısıt eşlemesi + `[CODEX-VERIFY]` doğrulama listesi sağlar.
- Generated client biçimi Issue #122'nin kararıdır; §4 yalnız aday.

## 2. Bağlayıcı kısıtlar

| Kaynak | Kısıt |
|---|---|
| `DEC-TEC-002` | React + TypeScript |
| `DEC-TEC-011` | **Ücretli grid/raporlama bileşeni varsayılan DEĞİL**; açık kaynak/kurumsal TCO; istisna açık gerekçe + 3 yıllık TCO |
| `DEC-TEC-006` | Tam çevrimdışı yok; SSR/PWA gerekmiyor |
| `DEC-TEC-010` | Chrome, Edge, Safari, Firefox; Windows/macOS/iOS/Android/tablet/el terminali/kiosk |
| `ERP-000K` §13/§15/§19 | CompanyContext fence (bkz. `shell-companycontext-fence/claude.md`); WCAG 2.2 AA; deterministik test + upgrade/rollback |
| Issue #122 | pinlenmiş OpenAPI generator → `src/frontend/goymen-erp-web/src/api/generated/`; contract-drift CI kırmızı = build hatası; elle DTO yasak |
| Bugünkü kurulum | React 19, Vite 8, TypeScript 6, Vitest 4, oxlint (`package.json`) |

## 3. Stage 0 — SHELL-PILOT-01B minimum (analiz ediliyor)

`SHELL-PILOT-01B` (Issue #129) için **yalnızca** şu üç kategori gerekli. Fazlası bu dilimde eklenmez.

### 3.1 Router — `react-router@8.3.1` (ADR kabul adayı — Codex #135 doğruladı)

- MIT; Node `>=22.22.0`; React/ReactDOM peer `>=19.2.7` — repo tabanı içinde.
- Tek doğrudan dependency `cookie-es`; tarball SHA-512 + `LICENSE.md`; clean sandbox: install script yok, high/critical advisory yok; TS6/Vite8 probe OK.
- Stage 0'da `react-router-dom`, devtools, router plugin **kurulmaz**.
- `loader`/`action` **backend authorization DEĞİL** — direct URL/refresh/history/action her durumda server reauth'a tabi.
- `@tanstack/react-router@1.170.32` reddedildi (Stage 0): TS6 `router-core` declaration hatası + ~60 yeni lock girdisi.

### 3.2 Server-state — `@tanstack/react-query@5.102.8` (ADR kabul adayı — Codex #135 doğruladı)

- MIT; React peer `^18 || ^19`; tek doğrudan dependency `@tanstack/query-core`; tarball SHA-512 + `LICENSE`; clean sandbox temiz; TS6/Vite8 probe OK.
- Stage 0'da devtools, persistence, sessionlar-arası cache **kurulmaz**.
- **Sahibi olmadığı güvenlik davranışları (shell'in kurgusu — PR #133):** (1) namespace'e `actor/session + company + requestFenceGeneration`; (2) geç-dönen-cevabı-commit-etmeyen generation fence (`AbortController` yanında); (3) company-switch/logout/permission-daralmasında fail-closed cache temizliği; (4) cache/menü görünürlüğü ≠ authorization; (5) PR #132 `capabilityGeneration` yalnız **opak equality token**.
- Eşleme: fence → `meta.fenceGeneration` + `onSettled`/`onSuccess` guard; namespace → `queryKey[0] = contextKey`.
- `removeQueries` + `AbortController` **tek başına** stale-response fence DEĞİL — `requestFenceGeneration` kontrolü şart (fence analizi §A.2; PR #133 `contextFence.ts`).

### 3.3 Client state — React context + reducer (yeni paket yok)

- PR #133 `src/shell/shellState.ts` saf reducer olarak kanıtlandı (`activeContext`/`pendingTarget`/`dirtyScopes`/iki sayaç, 17 test). Zustand vb. yalnız kanıtlı ihtiyaçta, ayrı analiz.

### 3.4 Styling — CSS Modules + CSS custom properties

- Vite yerleşik, sıfır runtime, yeni bağımlılık yok. Master §26 tokenları `:root`. Tailwind / runtime CSS-in-JS reddedildi.

## 4. OpenAPI client — **NO-GO** (Codex #135)

| Aday | Sonuç (Codex `codex.md` §4) |
|---|---|
| `openapi-typescript@7.13.0` + `openapi-fetch@0.17.0` | **NO-GO**: openapi-typescript peer `typescript:^5.x` → TS6 `ERESOLVE` nonzero; `openapi-fetch` maintainer 2026 yol haritasında maintenance-mode. `--force`/`--legacy-peer-deps` kullanılmaz. |
| `@hey-api/openapi-ts@0.99.0` | **NO-GO**: `npm audit --audit-level=high` 4 high bulgu (`js-yaml` CPU-consumption zinciri). |
| `orval@8.27.0` | Yalnız **K3 ADVERSARIAL PoC ADAYI** — canonical backend OpenAPI çıktıktan sonra, `codex.md` §5 14-madde fail-close profiliyle (network kapalı, external `$ref` default-deny, OAS `3.0.x/3.1.x` allowlist, byte-identical çıktı, malicious fixture'lar, statik inceleme testlerden önce, license/provenance boşluğu açık çözüm). |

Güvenli sıra: Codex canonical backend OpenAPI üretir → gerçek contract üzerinde Orval adversarial PoC → geçerse generator ADR/package PR; geçmezse TS6-destekli daha küçük stable aday beklenir. Generated client / contract-drift / Claude consumer PR'ları tek-yazar sınırıyla ayrı.

## 5. Ertelenen kategoriler (Stage 1+ — bu dilimde analiz EDİLMİYOR)

Aşağıdakiler ilgili dalga bir ekranda fiilen gerektirdiğinde, kendi kritik analizinde ele alınır:

| Kategori | Tetikleyen dalga | `DEC` kısıtı |
|---|---|---|
| Veri tablosu (headless) | W1-CARI C-01 liste | `DEC-TEC-011` — ücretli grid yasak; headless (ör. TanStack Table) + kendi hücre bileşenleri aday |
| Form + validation | W1-CARI C-02 form | Zod kullanılırsa **yalnız transport/UI-niyet doğrular; server kısıtları + generated tipler authoritative** |
| Headless bileşen + a11y | W0-SHELL ortak bileşenler | React Aria vs Radix — **TanStack Table + React Aria a11y kompozisyonu otomatik uyumlu değil; DOM/keyboard modeli kanıtlanmalı** |
| Row virtualization | W3 stok/üretim büyük listeler | — |
| İkon | ortak toolbar | ISC/MIT aday; ihtiyaç doğunca |
| Browser E2E aracı | `SHELL-PILOT-01B` kabul | Playwright aday (WebKit motoru **yararlı kanıt ama tam Safari/iOS kabulü değil**); Cypress aday; #122/#129 kapanınca seçilir |

## 6. `[CODEX-VERIFY]` doğrulama listesi (Issue #122)

Her Stage 0 adayı için Codex primary-source ile doğrular:

1. Exact son stabil versiyon + release kadansı (son 90 gün aktif mi).
2. Lisans dosyası (MIT/Apache-2.0/ISC iddiası doğru mu; ücretli tier yok mu).
3. Bilinen kritik/yüksek CVE.
4. Transitive bağımlılık yüzeyi + `npm audit --audit-level=high` (README'de mevcut).
5. React 19 + TypeScript 6 + Vite 8 fiili uyumu (`tsc -b` + build kanıtı).
6. Bootstrap/supply-chain preflight (`tools/validate_dotnet_bootstrap_integrity` benzeri frontend kontrolü) zayıflamıyor.
7. Deterministik generator çıktısı (openapi-typescript): timestamp/machine-path/random-order yok.
8. Upgrade/rollback: tek revert ile geri alınabilir; lockfile commit'li.

## 7. Karar akışı (REV.4 — yakınsandı)

```text
bu envanter + Codex codex.md (PR #135)  ── YAKINSADI ──▶
  Stage 0 ADR-frontend-stack önerisi (react-router@8.3.1 + @tanstack/react-query@5.102.8
    + CSS Modules + React context/reducer; router/query pin, geri alınabilir teknik karar)
  → ADR kabul → SHELL-PILOT-01B foundation PR (Claude: pinned + lockfile + token + router iskeleti
    + PR #133 fence/reducer'ı wire + ilk smoke E2E)

OpenAPI generator (ayrı hat):
  Codex canonical backend OpenAPI (Issue #122) → orval@8.27.0 adversarial PoC (codex.md §5 profili)
  → geçerse generator ADR/package PR → generated client (Codex) + contract-drift (Codex) + consumer (Claude), tek-yazar ayrı

Stage 1+ (tablo/form/headless-a11y/virtualization/ikon): ilgili dalgada ayrı kritik analiz
```

Lane yazarı merge etmez (v2.1 §1). Coordinator kapısı + 2× exact-head K3 review. `codex.md` ↔ bu dosya çapraz review: CLAUDE ONAY (PR #135 yorumu), ChatGPT business review bekliyor.
