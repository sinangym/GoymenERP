# Kritik Analiz — Frontend Paket Aday Envanteri (minimum staged)

- Tarih: 2026-08-31
- Protokol: `GOYMEN_ERP_OTONOM_TESLIM_PLANI_v2` (v2.1) §9 + `ERP-000K_SHELL_CONTRACT_v1.md` §19
- Bağlı: Issue #122 (`W0-CICD-001` OpenAPI/generated-client), Issue #128 (SHELL-PILOT-01A Codex), Issue #129 (SHELL-PILOT-01B Claude)
- İncelenen main: current (branch senkron)
- Analizi yapan: `CLAUDE-FRONTEND` — dosya `docs/delivery/CRITICAL_ANALYSIS/frontend-packages/claude.md` (v2.1 ajan-ayrık; Codex ayrı `codex.md`)
- Durum: **REV.3 — ChatGPT+Codex 5×P1 BLOCKER ele alındı; CODEX-PLATFORM exact-version supply-chain review bekliyor (Issue #122)**

---

## Revizyon günlüğü

| Rev | Değişiklik |
|---|---|
| 0 | 12 kategori stack önerisi (head `00e8a0c`) |
| 1–2 | ChatGPT `BUSINESS-NONBLOCKING`; Issue #122 cross-lane bağımlılığı |
| 3 | ChatGPT+Codex 5×P1: **(1)** "stack önerisi" → **aday envanter**; paket kararı Codex exact-version supply-chain review + Issue #122. **(2)** **minimum staged**: yalnız Stage 0 (SHELL-PILOT-01B için router + data-fetch + CSS) analiz ediliyor; tablo/form/headless-a11y/virtualization/ikon **ertelendi** — ihtiyaç doğduğunda ayrı analiz. **(3)** dış olgusal iddialar (maintenance/bundle/"yaygın"/lisans) `[CODEX-VERIFY]` işaretli — bu doküman doğrulamıyor. **(4)** abartılı mimari iddialar kaldırıldı (router loader ≠ auth; `removeQueries`+`AbortController` ≠ fence — Issue #124/#119; TanStack Table + React Aria a11y kompozisyonu otomatik değil; Zod ≠ ikinci OpenAPI gerçeği; WebKit ≠ tam Safari/iOS kabulü). **(5)** ajan-ayrık yola taşındı; `AUTONOMOUS_PROGRESS.md`'den el çekildi. |

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

### 3.1 Router

| Aday | Lisans `[CODEX-VERIFY]` | Not |
|---|---|---|
| React Router (library/data mode) | MIT `[CV]` | `loader`/`action` server-reauth noktasıyla uyumlu; **loader backend authorization DEĞİL** — endpoint reauth authoritative kalır. Büyük ekosistem `[CV]`. |
| TanStack Router | MIT `[CV]` | Daha güçlü tip güvenliği; ekosistem/saha kanıtı daha az `[CV]`. |

Açık nokta (review): olgunluk (React Router) vs tip güvenliği (TanStack Router).

### 3.2 Server-state / cache

| Aday | Lisans `[CV]` | Not |
|---|---|---|
| TanStack Query | MIT `[CV]` | `queryKey[0] = contextKey` namespace ile `ERP-000K` §13 INV-NS eşlenebilir. **`removeQueries` + `AbortController` tek başına stale-response fence DEĞİL** — `requestFenceGeneration` kontrolü şart (bkz. fence analizi §A.2). |
| SWR | MIT `[CV]` | Mutation/invalidation kontrolü daha zayıf. |
| kütüphanesiz (fetch + kendi cache) | — | Fence/namespace kendi kodumuzda; en az bağımlılık, en çok kod. |

Açık nokta (review): TanStack Query mi, minimal kendi katman mı — fence/namespace her ikisinde de **bizim** kurgumuz.

### 3.3 Styling

- **CSS Modules + CSS custom properties (token)** — Vite yerleşik, sıfır runtime, yeni bağımlılık yok. Master §26 tokenları `:root`.
- Tailwind / runtime CSS-in-JS: reddedildi (config/purge karmaşası; React 19 + perf; SSR-yok).
- Açık nokta yok.

### 3.4 Stage 0 shell client-state

- `SHELL-PILOT-01B` için ayrı state kütüphanesi **gerekmeyebilir**: React context + reducer (sıfır paket) `activeContext`/`pendingTarget`/`dirtyScopes` için yeterli olabilir.
- Zustand vb. yalnız kanıtlı ihtiyaçta, ayrı analiz. (ChatGPT #118 review notu: "React/context/local reducer sıfır-paket baseline geçerli.")

## 4. Stage 0 — OpenAPI client (aday; karar Issue #122)

| Aday | Lisans `[CV]` | Not |
|---|---|---|
| openapi-typescript + openapi-fetch | MIT `[CV]` | Yalnız `.d.ts` tip + ince tipli fetch. Codegen büyük client üretmez → contract-drift diff küçük/deterministik. Generated klasör elle düzenlenmez. |
| orval | MIT `[CV]` | Query hook üretir; çıktı büyük, drift diff gürültülü. |

Issue #122 §Codex sahipliği: generator determinizmi, timestamp/absolute-path yokluğu, React 19/TS 6/Vite 8 uyumu, lisans dosyaları, transitive risk, desteklenmeyen-versiyon davranışı — hepsi **Codex doğrulaması**. Claude yalnız pinned generator'ın tüketicisi + wrapper (transport/safe-error-map/cancellation/request-generation; **DTO uydurmaz**).

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

## 7. Karar akışı

```text
bu aday envanter
→ Codex exact-version supply-chain review (Issue #122)  [açık noktalarda katıl/karşı-öneri]
→ yakınsa: ADR-frontend-stack (Stage 0 kilidi)
→ SHELL-PILOT-01B foundation PR (Claude: pinned + lockfile + token + router iskeleti + fetch/fence katmanı + ilk smoke E2E)
→ Stage 1+ kategoriler ilgili dalgada ayrı analiz
```

Lane yazarı merge etmez (v2.1 §1). Coordinator kapısı + 2× exact-head K3 review.
