# Kritik Analiz — Stage 0 Frontend Paketleri ve OpenAPI Generator Tedarik Zinciri

- Tarih: 2026-08-31
- Katman: `K3 / SUPPLY-CHAIN + CROSS-LANE CONTRACT`
- Analizi yapan: `CODEX-PLATFORM`
- İncelenen `main`: `d3f2b3063b6b6a1b55568980ae139a0f6debd4dc`
- İncelenen Claude analizi: PR #118, exact head `a02755135a05a240c8408a32d57f84146f727fbb`
- Bağlı iş: Issue #122 (`W0-CICD-001`) ve Issue #124 (`SHELL-PILOT-01`)
- Durum: **STAGE-0 ROUTER/QUERY ADR ADAYI HAZIR; OPENAPI GENERATOR KABULÜ HENÜZ NO-GO**

Bu belge paket kurmaz, lockfile değiştirmez, ADR kabul etmez ve frontend davranışı
üretmez. Claude'un aynı klasördeki aday envanterine bağımsız exact-version,
uyumluluk, lisans, güvenlik ve tedarik-zinciri karşı incelemesidir.

## 1. Doğrulanan repo tabanı

`src/frontend/goymen-erp-web/package-lock.json` ve yerel toolchain üzerinde:

| Bileşen | Çözülen sürüm |
|---|---:|
| React / React DOM | `19.2.8` |
| TypeScript | `6.0.3` |
| Vite | `8.2.2` |
| Vitest | `4.1.11` |
| Node.js / npm | `24.16.0` / `11.13.0` |

Repo `npm ci`, `npm audit --audit-level=high`, lint, test ve production build
kapılarını kullanır. Paket kabulü bu kapıları force/override/legacy peer çözümüyle
gevşetemez.

## 2. Stage 0 router kararı

### 2.1 Kabul adayı

`react-router@8.3.1` **ADR kabul adayıdır**:

- exact npm metadata: MIT, Node `>=22.22.0`, React ve React DOM `>=19.2.7`;
- repo tabanı bu engine/peer aralıklarının içindedir;
- tek doğrudan dependency `cookie-es@^3.1.1`dir;
- exact tarball SHA-512 integrity kaydı ve `LICENSE.md` içerir;
- React Router + TanStack Query clean sandbox'ında lockfile v3, install script yok,
  high/critical advisory yok;
- published package ile TypeScript 6/Vite 8 probe'u başarılıdır.

Stage 0'da `react-router-dom`, router devtools veya ek router plugin'i kurulmaz.
Router loader/action hiçbir zaman backend authorization sayılmaz; direct URL, refresh,
history ve action her durumda server authorization'a tabidir.

### 2.2 Reddedilen alternatif

`@tanstack/react-router@1.170.32` Stage 0 için seçilmez:

- code-based graph React Router'dan belirgin biçimde daha geniştir;
- file-based/Vite plugin yaklaşımı yaklaşık 60 yeni lock girdisine kadar çıkar;
- yayımlanmış graph TypeScript 6 tam declaration kontrolünde
  `router-core/dist/esm/ssr/types.d.ts` içindeki `__beforeLoadContext` alanında hata
  üretmiştir;
- bugün gereken shell davranışı bu ek yüzey veya plugin'i zorunlu kılmaz.

Bu seçim ürün rotası, permission veya CompanyId politikası değildir; geri alınabilir
teknik bir Stage 0 kararıdır.

## 3. Stage 0 server-state kararı

`@tanstack/react-query@5.102.8` **ADR kabul adayıdır**:

- MIT; React peer aralığı `^18 || ^19`;
- yalnız `@tanstack/query-core@5.102.8` doğrudan dependency'sini ekler;
- exact tarball SHA-512 integrity kaydı ve `LICENSE` içerir;
- React Router ile clean sandbox toplamında install script ve high/critical advisory yok;
- TypeScript 6/Vite 8 probe'u başarılıdır.

Stage 0'da devtools, persistence veya sessionlar arası cache kurulmaz. TanStack Query
aşağıdaki güvenlik davranışlarının sahibi değildir:

1. `actor/session + company + request epoch` query namespace'e girer.
2. `AbortController` yanında geç dönen cevabı commit etmeyen generation fence zorunludur.
3. Company switch, logout ve permission daralmasında ilgili cache fail-closed temizlenir.
4. Cache/menu görünürlüğü authorization üretmez.
5. PR #132'deki capability generation değeri yalnız opaque equality tokenıdır; süreçler
   arası artan sayaç veya yetki kanıtı gibi yorumlanmaz.

CSS Modules + CSS custom properties için yeni runtime paketi gerekmez.

## 4. OpenAPI generator karşılaştırması

### 4.1 `openapi-typescript@7.13.0` + `openapi-fetch@0.17.0`

Bugünkü repo-root kabulü **NO-GO**:

- `openapi-typescript@7.13.0` peer'i `typescript:^5.x`tir;
- TypeScript `6.0.2/6.0.3` ile temiz exact install `ERESOLVE` ve nonzero dönmüştür;
- `--force`, `--legacy-peer-deps` veya sessiz override kullanılmaz;
- `openapi-fetch@0.17.0`, maintainer'ın 2026 yol haritasında maintenance mode'dadır.

İzole TypeScript `5.9.3` generator workspace'i teknik olarak kurulabildi: 33 paket
girdisi, install script yok ve anlık npm audit high/critical sıfır. `openapi-fetch`
TypeScript 6 runtime sandbox'ı üç paket girdisiyle aynı taramayı geçti. Ancak ikinci
TypeScript hattı, unsupported OpenAPI minor davranışı ve maintenance-mode runtime için
ayrı ADR/gerçek-contract PoC gerekir; bu analiz bunları sessizce kabul etmez.

### 4.2 `@hey-api/openapi-ts`

Karşı-aday olarak incelendi ve **NO-GO**:

- `0.99.0` TypeScript 6 peer aralığını kabul eder;
- clean sandbox 51 paket girdisi ve install script olmadan kurulur;
- buna rağmen `npm audit --audit-level=high` dört high bulgu üretmiştir; zincirde
  `js-yaml` CPU-consumption advisory'leri vardır;
- `0.97.x`e geri dönmek pre-1.0 downgrade ve başka advisory'leri kabul etmek olur.

Yüksek advisory varken generator package/lockfile'a alınamaz.

### 4.3 `orval@8.27.0`

Bugün yalnız **K3 ADVERSARIAL PoC ADAYI**, henüz kabul değildir:

- npm metadata: MIT, Node `>=22.18.0`, optional Prettier `>=3` peer;
- Node 24 / TypeScript 6 ile exact clean install başarılıdır;
- isolated graph 135 paket girdisi üretir; `esbuild` native postinstall yüzeyi vardır;
- anlık `npm audit --audit-level=high` high/critical sıfırdır;
- npm tarball metadata'sında SHA-512 integrity vardır, fakat tarball dosya listesinde
  lisans dosyası yoktur; exact tag upstream MIT lisansı tek başına paket provenance
  kapısını otomatik kapatmaz;
- bütün adapter/plugin paketlerini kökten taşıdığı için yüzeyi minimum değildir;
- yakın geçmişte generated-code injection/RCE ve SSRF/LFI/RFI advisory'leri vardır;
  `8.27.0` düzeltmeleri içerse de generator güvenilmeyen input çalıştırmamalıdır;
- config discovery, hooks, transformer, mutator, mock, watch ve remote input kod/ağ
  yüzeyi açabilir.

Bu nedenle gerçek backend OpenAPI çıkmadan Orval package/lockfile kabulü yapılamaz.

## 5. Orval PoC fail-closed profili

Issue #122'nin gerçek contract'ı üretildikten sonra ayrı K3 PoC şunların tamamını
kanıtlamalıdır:

1. `orval@8.27.0` exact pin, committed lockfile ve tarball integrity kontrolü.
2. Yalnız incelenmiş `esbuild` postinstall allowlist; genel script gevşetmesi yok.
3. Girdi yalnız repo-içi canonical JSON; network kapalı, remote/top-level URL ve external
   `$ref` default-deny.
4. Açık OAS allowlist yalnız `3.0.x`/`3.1.x`; Swagger upgrade veya validation bypass yok.
5. Native `fetch` output; Angular/Axios/Effect/Hono/MCP/Mock/Query/SWR/Zod generatorları
   kapalı.
6. Config auto-discovery, `jiti`, hooks, transformer, mutator, mock, watch ve clean yok.
7. Warning = error; malformed/unsupported schema nonzero.
8. Aynı source/toolchain iki temiz dizinde byte-identical çıktı; Windows/Linux farkı yok.
9. Timestamp, absolute path, runner identity, secret, connection string, SQL veya internal
   exception taraması temiz.
10. Malicious path/server/description/JSDoc/enum/default/header/query ve external-ref
    fixture'ları fail-closed.
11. Generated output TypeScript 6 ile compile olur; React 19/Vite 8 build ve browser smoke
    geçer.
12. Manual generated diff ve backend contract mutation CI'da kırmızıya döner.
13. Üretilen kod testlerden önce import/execute edilmez; statik inceleme kapısı öndedir.
14. Paket license/provenance boşluğu açıkça çözülmeden kabul edilmez.

## 6. Karar ve güvenli sıra

| Alan | Karar |
|---|---|
| Router | `react-router@8.3.1` — **ADR kabul adayı** |
| Server-state | `@tanstack/react-query@5.102.8` — **ADR kabul adayı** |
| Styling | CSS Modules + custom properties — yeni paket yok |
| Client state | React context + reducer; ek store paketi yok |
| OpenAPI generator | **NO-GO; önce backend canonical OpenAPI** |
| Generator PoC | `orval@8.27.0`, yalnız bölüm 5 profiliyle |

Güvenli uygulama sırası:

1. Bu analiz PR #118'in exact-head Claude analiziyle çapraz review edilir.
2. Router/query için yalnız Stage 0 kapsamlı ADR önerisi hazırlanır.
3. Codex ayrı branch'te canonical, deterministic ve secret-free backend OpenAPI üretir.
4. Gerçek contract üzerinde Orval adversarial PoC yürütülür.
5. PoC geçerse generator ADR/package PR'ı açılır; geçmezse package kurulmaz ve TS6
   destekli daha küçük stable aday beklenir.
6. Generated client, contract-drift ve Claude consumer PR'ları tek-yazar sınırıyla ayrı
   tutulur.

## 7. Kanıt özeti

- Clean exact install: React Router + TanStack Query — success; install script `0`;
  npm audit high/critical `0`.
- Clean exact install: TanStack Router alternatifi — success; daha geniş graph; strict
  declaration probe failure.
- Repo-root-equivalent TS6 install: openapi-typescript — `ERESOLVE` nonzero.
- Isolated TS5 openapi-typescript — success; doğrudan kabul değildir.
- Clean exact install: Orval — success; 135 package entry; `esbuild` install script;
  npm audit high/critical `0`.
- Clean exact install: Hey API 0.99 — success; npm audit high `4`; rejected.

`npm audit` yalnız 2026-08-31 registry advisory anlık görüntüsüdür; güvenlik garantisi
değildir. Head/base veya exact package sürümü değişirse bu inceleme stale olur.

## 8. Birincil kaynaklar

- React Router 8.3.1 release/install/license:
  <https://github.com/remix-run/react-router/releases/tag/react-router%408.3.1>,
  <https://reactrouter.com/start/declarative/installation>,
  <https://github.com/remix-run/react-router/blob/main/LICENSE.md>
- TanStack Query install/release/license:
  <https://tanstack.com/query/latest/docs/framework/react/installation>,
  <https://github.com/TanStack/query/releases/tag/release-2026-08-27-1607>,
  <https://github.com/TanStack/query/blob/main/LICENSE>
- openapi-typescript package/release/TS6 issue/2026 roadmap:
  <https://github.com/openapi-ts/openapi-typescript/blob/openapi-typescript%407.13.0/packages/openapi-typescript/package.json>,
  <https://github.com/openapi-ts/openapi-typescript/releases/tag/openapi-typescript%407.13.0>,
  <https://github.com/openapi-ts/openapi-typescript/issues/2723>,
  <https://github.com/openapi-ts/openapi-typescript/discussions/2559>
- Orval 8.27 package/release/license/security:
  <https://github.com/orval-labs/orval/blob/v8.27.0/packages/orval/package.json>,
  <https://github.com/orval-labs/orval/releases/tag/v8.27.0>,
  <https://github.com/orval-labs/orval/blob/v8.27.0/LICENSE>,
  <https://github.com/orval-labs/orval/security/advisories/GHSA-fg9p-mrxr-hvq7>,
  <https://github.com/orval-labs/orval/security/advisories/GHSA-88f2-fpv8-89q2>,
  <https://github.com/orval-labs/orval/security/advisories/GHSA-cxq5-97v7-87j8>
