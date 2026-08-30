# Kritik Analiz — Frontend Paket Seçimi

- Tarih: 2026-08-31
- Protokol: `GOYMEN_ERP_OTONOM_TESLIM_PLANI_v2.md` §9 + `ERP-000K_SHELL_CONTRACT_v1.md` §19
- İncelenen main: `e9c48605260e52a2905609dc377e14e9dbb68582`
- Analizi yapan: `CLAUDE-FRONTEND`
- Durum: **ADVERSARIAL REVIEW BEKLİYOR — reviewer: CODEX-PLATFORM**
- Sonuç: yakınsarsa `ADR-frontend-stack` önerisi açılır (numara Sinan/ChatGPT atar); paketler `SHELL-PILOT-01` ilk PR'ında eklenir.

---

## 0. Neden bu analiz

Bugünkü `src/frontend/goymen-erp-web` yalnız React 19 + ReactDOM + Vite 8 + Vitest 4 + oxlint içerir. Router, server-state, form, tablo, headless bileşen, browser-E2E ve OpenAPI client yoktur. `ERP-000K_SHELL_CONTRACT` §19 bu seçimlerin implementation'dan önce lisans/güvenlik/TCO/uyum review'undan geçmesini şart koşar. Bu dosya o review'un girdisidir; paket **eklemez**.

## 1. Bağlayıcı kısıtlar (kaynak taraması)

| Kaynak | Kısıt |
|---|---|
| `DEC-TEC-002` | React + TypeScript; ASP.NET Core .NET 10 backend; ortak şemalı API |
| `DEC-TEC-011` | **Ücretli grid/raporlama bileşeni varsayılan tercih DEĞİL**; açık kaynak/kurumsal TCO karşılaştırması; istisna açık gerekçe + 3 yıllık TCO ile |
| `DEC-TEC-006` | Tam çevrimdışı ERP yok; saha taslak/outbox var |
| `DEC-TEC-010` | Chrome, Edge, Safari, Firefox; Windows/macOS/iOS/Android/tablet/el terminali/kiosk |
| `ERP-000K` §13 | Company-switch'te request-generation/cache/stale-response fence; contextler arası cache paylaşılmaz |
| `ERP-000K` §15 | WCAG 2.2 AA; breakpoint `<768 / 768-1023 / 1024-1439 / 1440-1599 / 1600+`; `1600px` altı sağ panel drawer; keyboard + ekran okuyucu |
| `ERP-000K` §19 | SSR/PWA gerektirmeyen bugünkü mimari; deterministik test + upgrade/rollback |
| `GOYMEN_ERP_OTONOM_TESLIM_PLANI_v2` §9 | Pinlenmiş OpenAPI generator → `src/frontend/goymen-erp-web/src/api/generated/`; contract-drift CI kırmızı = build hatası |
| `AGENTS.md` | Secrets commit edilmez; her public API sürümlü contract testi |
| Master §26 | Koyu lacivert token seti; yüksek veri yoğunluğu; kurumsal karakter |

## 2. Değerlendirme eksenleri (§19)

1. Güncel resmî destek ve maintenance kadansı
2. Güvenlik / bilinen açık / supply-chain yüzeyi
3. Lisans + ücretli istisna + 3 yıllık TCO
4. React 19 / TypeScript 6 / Vite 8 uyumu
5. SSR/PWA gerektirmeyen mimariye uyum
6. Bundle / performans / accessibility
7. Deterministik test + upgrade/rollback

## 3. Kategori kararları

### 3.1 Router — **React Router v7 (library/data mode)**

| Aday | Lisans | Değerlendirme |
|---|---|---|
| **React Router v7** | MIT | En büyük ekosistem, olgun, Remix ekibi aktif. `loader`/`action` modeli `ERP-000K` §10.2 "her açılışta backend reauthorization" ile birebir örtüşür. Framework modu kullanılmaz; salt library modu (SSR yok). Type-safety orta (route param tipleri manuel/generate). |
| TanStack Router | MIT | Uçtan uca tip güvenliği daha iyi; ekosistem daha genç, kurumsal saha kanıtı daha az; migration maliyeti ileride yüksek. |
| Wouter / kendi router | MIT | ~160 ekran + deep-link + guard + nested layout için yetersiz. |

**Öneri:** React Router v7 library mode. **Trade-off / review noktası:** tip güvenliği TanStack'te daha iyi; ekosistem/saha kanıtı React Router'da. Kurumsal ERP + uzun ömür için olgunluk ağır basar.

### 3.2 Server-state / query cache — **TanStack Query v5**

- MIT, de-facto standart, React 19 uyumlu.
- `queryKey` company/context namespace'i → `ERP-000K` §13 company-switch invalidation ve stale-response fence doğrudan bu mekanizmayla çözülür (`queryClient.removeQueries` + `AbortController` + request-generation guard).
- Alternatif RTK Query: Redux bağımlılığı getirir, cache lifecycle kontrolü bu senaryo için daha dolaylı. SWR: daha az kontrol (mutation/invalidation zayıf).
- **Öneri:** TanStack Query v5. Review noktası yok — bu seçim netleşmiş sektör standardı.

### 3.3 Shell client-state — **Zustand**

- MIT, ~1KB, minimal boilerplate. Shell-seviyesi state: aktif `CompanyContext`, capability cache handle, layout (drawer açık/kapalı, nav daraltılmış), breadcrumb.
- Business/server verisi **Query'de** kalır; Zustand yalnız türetilemeyen UI state.
- Alternatif Redux Toolkit: ERP ölçeğinde savunulabilir ama shell için fazla tören; feature'lar kendi local state'ini yönetir. Context: company-switch'te geniş yeniden render riski.
- **Öneri:** Zustand. Review noktası: Redux Toolkit tercih edilirse audit/devtools/time-travel avantajı; karşılığında boilerplate. Şu an Zustand yeterli, gerekirse feature bazında yükseltilir.

### 3.4 Form + validation — **React Hook Form + Zod**

| Aday | Lisans | Değerlendirme |
|---|---|---|
| **React Hook Form + Zod** | MIT | RHF standart, kontrolsüz input performansı yüksek (büyük sipariş/plan formları). Zod şeması OpenAPI'den üretilen tiplerle hizalanır; `ERP-000K` §12 validation-error/field-summary bağı net. |
| TanStack Form + Valibot/Zod | MIT | Daha iyi tip güvenliği, Query/Router/Table ile aynı ekosistem; daha genç, saha kanıtı az. |
| Formik | MIT | Maintenance yavaşladı; kontrollü render performansı zayıf. |

**Öneri:** React Hook Form + Zod. **Trade-off / review noktası:** ekosistem tekliği için TanStack Form; olgunluk/performans kanıtı için RHF. RHF öneriliyor.

### 3.5 Headless bileşen + a11y — **React Aria Components (Adobe)**

| Aday | Lisans | Değerlendirme |
|---|---|---|
| **React Aria Components** | Apache-2.0 | En geniş WCAG 2.2 kapsamı (combobox, listbox, table, grid, date picker, dialog, tooltip, drag-drop). `ERP-000K` §15 AA + keyboard + SR + `1600px` drawer + focus trap gereksinimini en az kendi kodla karşılar. Adobe uzun-vade maintenance. Bundle orta; tree-shake edilir. |
| Radix UI Primitives | MIT | Daha hafif, çok kullanılan; ama tablo/grid/date-picker/combobox kapsamı dar → bu bileşenleri kendimiz a11y ile yazarız (risk). |
| Base UI (MUI) | MIT | Çok genç (2025 GA öncesi hareketli API); erken bağlanma riski. |
| Headless UI | MIT | Kapsam çok dar (menu/dialog/listbox/switch). |

**Öneri:** React Aria Components. **Trade-off / review noktası:** Radix daha küçük bundle + daha basit API; React Aria daha tam a11y + daha büyük bundle. AA zorunluluğu ve ~160 ekranın tekrar eden combobox/table/date-picker ihtiyacı React Aria'yı haklı çıkarır. **Bu, review'daki en önemli karar.**

### 3.6 Veri tablosu — **TanStack Table v8 (headless)**

- MIT, headless (kendi markup + React Aria `Table` ile a11y). `DEC-TEC-011` **tam uyum**: sıfır lisans maliyeti.
- Server-side sort/filter/pagination/keyset cursor (`ERP-000K` §11.1) ile birlikte; client-side yalnız görünüm.
- Ücretli AG Grid / MUI DataGrid Pro **reddedildi** (`DEC-TEC-011`): 3 yıllık TCO ~ dev-seat başına yıllık lisans; headless + kendi hücre bileşenleri ile karşılanabilir ihtiyaç.
- **Öneri:** TanStack Table v8 + TanStack Virtual (satır virtualization, MIT). Review noktası yok — `DEC-TEC-011` bunu zorunlu kılar.

### 3.7 Styling — **CSS Modules + CSS custom properties (design token)**

- Vite yerel desteği, **sıfır runtime**, yeni bağımlılık yok. Master §26 tokenları `:root` CSS değişkenleri.
- Tailwind **reddedildi**: purge/config karmaşası, uzun-vade opinion churn, uzun Türkçe metin + yüksek data density'de utility-soup okunabilirlik riski.
- Runtime CSS-in-JS (styled-components/emotion) **reddedildi**: React 19 + performans + SSR-yokken gereksiz.
- vanilla-extract: iyi aday (tip-güvenli, sıfır runtime) ama build karmaşası + öğrenme; CSS Modules yeterli ve "boring".
- **Öneri:** CSS Modules + token dosyası. Review noktası: vanilla-extract tip güvenliği isteniyorsa alternatif; CSS Modules öneriliyor.

### 3.8 İkonlar — **Lucide React**

- ISC lisans, tree-shakeable, tutarlı 24px grid, kurumsal görünüm. Alternatif Heroicons (MIT, dar set). **Öneri:** Lucide.

### 3.9 OpenAPI client — **openapi-typescript + openapi-fetch**

| Aday | Lisans | Değerlendirme |
|---|---|---|
| **openapi-typescript + openapi-fetch** | MIT | `openapi.json` → sadece `.d.ts` tipler + ~1KB tipli fetch wrapper. Codegen büyük client class üretmez → contract-drift diff'i küçük ve deterministik (`GOYMEN_ERP_OTONOM_TESLIM_PLANI_v2` §9). `src/api/generated/` yalnız tip dosyası. |
| orval | MIT | TanStack Query hook'ları üretir; çıktı büyük, drift diff gürültülü, "magic" fazla. |
| @hey-api/openapi-ts | MIT | Olgunlaşıyor; çıktı orta. |
| NSwag (C# tarafı) | — | Backend'in OpenAPI üretimi Codex'te; frontend tüketimi ayrı. |

**Öneri:** openapi-typescript (tip üretimi, CI'da) + openapi-fetch (runtime). Query ile elle sarılır (ince, açık). Review noktası: orval hook üretimini tercih ederse daha az boilerplate; drift-check basitliği için openapi-typescript öneriliyor.

### 3.10 Browser E2E — **Playwright**

- Apache-2.0, Microsoft aktif maintenance. **WebKit motoru** → `DEC-TEC-010` Safari kapsamı gerçek (Cypress'te WebKit deneysel). Multi-tab/multi-origin (company-switch, deep-link, SSO redirect) Playwright'ta birinci sınıf. Trace viewer + deterministik auto-wait.
- `@axe-core/playwright` ile a11y taraması aynı harness'te.
- Cypress **reddedildi**: multi-origin/multi-tab zayıf, WebKit zayıf.
- **Öneri:** Playwright + `@axe-core/playwright`. Test kökü: `tests/e2e/` (Claude sahibi, `GOYMEN_ERP_OTONOM_TESLIM_PLANI_v2` §4).

### 3.11 Component test — **Vitest + Testing Library**

- Vitest 4 zaten var. `@testing-library/react` + `@testing-library/user-event` + `@testing-library/jest-dom` + `vitest-axe`.
- Vitest **browser mode** (Playwright provider) gerçek DOM'da component testi için; jsdom fallback.
- **Öneri:** yukarıdaki set. Review noktası yok.

### 3.12 Para/sayı/tarih gösterimi — **Intl native (+ big.js yalnız gerekirse)**

- Para **matematiği backend** (deterministik formül hesaplayıcı, `DEC-PRDCT-004`). Frontend yalnız **gösterir**: `Intl.NumberFormat('tr-TR', {style:'currency', currency})`. `AT-011` (TRY/USD/EUR tek sayıya netleşmez) → her bakiye kendi para birimiyle ayrı render.
- Client-side aritmetik kaçınılmazsa (örn. satır toplamı önizleme): `big.js` (~3KB, MIT). Ama öncelik backend preview (`DEC-PRDCT-005` düğmesiz önizleme).
- i18n: UI Türkçe; string'ler tipli katalog modülünde (`src/i18n/tr.ts`), tam i18n kütüphanesi ertelenir (`DEC` yok, `ASSUMPTION` gerekmez — genişleme noktası bırakılır).
- **Öneri:** Intl native + tipli string katalog. `big.js` yalnız kanıtlı ihtiyaçta.

## 4. Özet stack

| Katman | Seçim | Lisans |
|---|---|---|
| Router | React Router v7 (library mode) | MIT |
| Server-state | TanStack Query v5 | MIT |
| Shell client-state | Zustand | MIT |
| Form + validation | React Hook Form + Zod | MIT |
| Headless + a11y | React Aria Components | Apache-2.0 |
| Tablo | TanStack Table v8 + TanStack Virtual | MIT |
| Styling | CSS Modules + CSS token | — (yerleşik) |
| İkon | Lucide React | ISC |
| OpenAPI client | openapi-typescript + openapi-fetch | MIT |
| Browser E2E | Playwright + @axe-core/playwright | Apache-2.0 |
| Component test | Vitest browser mode + Testing Library + vitest-axe | MIT |
| Para/tarih | Intl native (+ big.js gerekirse) | — / MIT |

## 5. Ekseni bazında toplu değerlendirme

1. **Maintenance:** hepsi son 90 günde aktif release; React Router / Query / Table / Playwright haftalık-aylık kadans.
2. **Güvenlik/supply-chain:** hepsi çok yüksek indirme, permissive lisans, bilinen kritik açık yok. Pin + lockfile commit + CI `npm audit --audit-level=high` (README'de mevcut) + `npm ls` derinlik kontrolü. Transitive yüzey en büyük Playwright'ta (yalnız devDependency, runtime bundle'a girmez).
3. **Lisans/TCO:** **sıfır ücretli lisans.** `DEC-TEC-011` tam uyum; AG Grid/MUI Pro reddi 3 yıllık dev-seat lisans maliyetini elimine eder.
4. **React 19 / TS 6 / Vite 8:** hepsi React 19 ve Vite 8 destekliyor; TS 6 (yeni) için Query/Router/RHF sorunsuz, React Aria ve TanStack Table tip tanımları TS 6 ile doğrulanmalı (review'da `tsc -b` kanıtı).
5. **SSR/PWA yok:** hiçbiri SSR zorlamıyor; React Router library mode, Query client-only. `DEC-TEC-006` outbox/offline-taslak ileride Query persist + kendi kuyruk katmanıyla eklenebilir (şimdi değil).
6. **Bundle/perf/a11y:** tahmini ilk yük React+RR+Query+Zustand ~55-70KB gz; React Aria + Table lazy/route-split. a11y React Aria ile güçlü taban. `ERP-000K` §15 breakpoint'leri CSS token + container query ile.
7. **Deterministik test + rollback:** exact pin; her paket ayrı PR'da eklenmez — tek "shell foundation" PR'ında hepsi + lockfile + `tsc` + lint + build + ilk smoke E2E yeşil. Rollback = tek revert.

## 6. Review'da netleşmesi gereken 3 nokta

1. **Router:** React Router v7 (olgunluk) vs TanStack Router (tip güvenliği). Öneri: React Router.
2. **Headless/a11y:** React Aria Components (tam a11y, +bundle) vs Radix (hafif, dar kapsam → daha çok kendi a11y kodu). Öneri: React Aria. **En kritik.**
3. **Form:** React Hook Form (olgunluk) vs TanStack Form (ekosistem tekliği). Öneri: RHF.

Diğer kategorilerde `DEC-TEC-011` veya sektör standardı tek yönlü sonuç veriyor.

## 7. Codex adversarial review isteği

Şablon (`GOYMEN_ERP_OTONOM_TESLIM_PLANI_v2` §8):

```text
REVIEWED HEAD: <bu PR head SHA>
KATMAN: K3 — doğru | yanlış
SOURCE OF TRUTH: DEC-TEC-002/006/010/011, ERP-000K §13/§15/§19, OTONOM_TESLIM_PLANI §9
LİSANS DOĞRULAMA: her paket lisansı fiilen doğrulandı mı | <sapma>
SUPPLY-CHAIN: transitive yüzey / bilinen açık / pin stratejisi kabul | <bulgu>
DEC-TEC-011 UYUMU: ücretli bileşen yok — teyit | <ihlal>
TS6/REACT19/VITE8 UYUM RİSKİ: yok | <paket + kanıt gereği>
3 AÇIK NOKTA (router/a11y/form): öneriye KATIL | KARŞI ÖNERİ + gerekçe
KARAR: HANDOFF-ACCEPTED | BLOCKER
```

Yakınsarsa: `ADR-frontend-stack` önerisi + `SHELL-PILOT-01` foundation PR (paketler + lockfile + token dosyası + router iskeleti + Query provider + ilk smoke E2E).
