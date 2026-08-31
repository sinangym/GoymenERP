# ADR-013 — Frontend Stage 0 stack

- Durum: Kabul (Sinan onayı 2026-08-31, `GOYMEN_ERP_HIZLI_TESLIM_v3.md` §9.4)
- Bağlam: `docs/delivery/CRITICAL_ANALYSIS/frontend-packages/claude.md` + `codex.md` (PR #135 merged)
- Kapsam: SHELL-PILOT-01B ve sonraki ekranlar. Geri alınabilir teknik karar; ürün rotası / permission / CompanyId politikası değildir.

## Karar

| Katman | Seçim | Sürüm | Lisans |
|---|---|---|---|
| Router | `react-router` (library/data mode) | `8.3.1` (exact pin) | MIT |
| Server-state | `@tanstack/react-query` | `5.102.8` (exact pin) | MIT |
| Client-state | React `useReducer` + context (`src/shell/shellState.ts`) | — | — |
| Styling | CSS Modules + CSS custom properties (token) | Vite yerleşik | — |
| Component test | `@testing-library/react` + `vitest` (jsdom) | pinned | MIT |

## Gerekçe

- Codex supply-chain review'u (PR #135): `react-router@8.3.1` tek dep `cookie-es`, TS6/Vite8 probe OK; `@tanstack/react-query@5.102.8` tek dep `query-core`; ikisi de clean sandbox, install script yok, high/critical advisory yok.
- `@tanstack/react-router` Stage 0 için reddedildi: TS6 `router-core` declaration hatası + ~60 lock girdisi.
- `loader`/`action` **backend authorization değildir** — her açılış server reauth'a tabi (`ERP-000K` §10.2).
- TanStack Query güvenlik davranışının sahibi değildir: request-fence, context namespace, fail-closed cache temizliği shell'in kurgusudur (`src/shell/contextFence.ts`, `shellState.ts`).

## Kapsam dışı (kendi kararında)

- OpenAPI generator: **NO-GO** (Codex #135). Canonical backend OpenAPI'den sonra `orval` adversarial PoC (`codex.md` §5). O gelene kadar ince elle yazılmış typed client, `HANDOFF-*` işaretli.
- Tablo / form / headless-a11y / virtualization / ikon: ilgili dalgada ayrı ADR.
- Browser E2E aracı (Playwright): `SHELL-PILOT-01B` kabul kapısında seçilir.

## Ücretli bileşen

Yok. `DEC-TEC-011` tam uyum.
