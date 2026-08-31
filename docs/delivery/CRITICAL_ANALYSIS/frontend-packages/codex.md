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
- published declaration'lar TypeScript 6 strict probe'undan geçmiştir; Vite 8
  runtime/build kabulü ayrı package-adoption PR'ında kanıtlanacaktır.

Stage 0'da `react-router-dom`, router devtools veya ek router plugin'i kurulmaz.
Router loader/action hiçbir zaman backend authorization sayılmaz; direct URL, refresh,
history ve action her durumda server authorization'a tabidir.

### 2.2 Reddedilen alternatif

`@tanstack/react-router@1.170.32` Stage 0 için seçilmez:

- code-based graph React Router'dan belirgin biçimde daha geniştir;
- file-based/Vite plugin yaklaşımı ayrıca incelenmesi gereken daha geniş bir
  build-time supply-chain yüzeyi açar;
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
- published declaration'lar TypeScript 6 strict probe'undan geçmiştir; Vite 8
  runtime/build kabulü bu analizle verilmez.

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

## 8. Yeniden üretim defteri

Bu bölüm, yukarıdaki ampirik iddiaların 2026-08-31 anlık görüntüsünü aynı exact
girdilerle yeniden üretmek içindir. Komutlar Windows PowerShell 7 üzerinde
`node --version` = `v24.16.0` ve `npm --version` = `11.13.0` ile çalıştırılmıştır.
Sandbox'lar repo dışında, boş dizinlerden oluşturulmuş; hiçbirinde `--force`,
`--legacy-peer-deps`, override veya audit suppression kullanılmamıştır.

### 8.1 Exact sandbox girdileri ve hashler

Her sandbox'ta yalnız aşağıdaki exact dependency setiyle `npm install
--package-lock-only`, ardından `npm ci`, `npm audit --json` çalıştırıldı. Paket
sayısı lockfile v3 içindeki boş root anahtarı hariç `packages` anahtar sayısıdır.

| Sandbox | Exact dependency seti | Manifest SHA-256 | Lock SHA-256 | Paket / install script / audit |
|---|---|---|---|---|
| router-query | `react@19.2.8`, `react-dom@19.2.8`, `react-router@8.3.1`, `@tanstack/react-query@5.102.8`, `typescript@6.0.2` | `d6cc4c3f83314684d7ac2fe9bb9824eecb1fc532fc2e36217530f9de3124f5fb` | `789451fe7ab51dc2ef6216e5b15d42f9faec899c18a6f2e879223c0293994ac4` | `8 / 0 / 0 high-critical` |
| tanstack-router-query | önceki React/Query seti, router yerine `@tanstack/react-router@1.170.32` | `e2793b3c261ac82b7f03ad1d0137ec27f87a14ee98838aada7b8de6e63a4602c` | `6161b9d20f11117fb05f1e47cceb8700fde35614b377768284436e8513da092b` | `16 / 0 / 0 high-critical` |
| openapi-typescript-isolated | `openapi-typescript@7.13.0`, `typescript@5.9.3` | `ae7cb4d21cc1cdb698d0ab90f35ed79ac41bc378b826bb3d52fe8a12f20a0e93` | `620f1f39fbe7b96ce89e4974205cfb9587b226f9bfac53cae276178114052cb4` | `33 / 0 / 0 high-critical` |
| openapi-fetch-runtime-ts6 | `openapi-fetch@0.17.0`, `typescript@6.0.2` | `cff98718760cb4fa574594a7cf5a603a6ff6b470bc48d258244406d25a7a65c6` | `5f28d7f0d3bab678365b96c513fce23807f92d98b8ac1ba4d8276e389e4d69dc` | `3 / 0 / 0 high-critical` |
| orval | `orval@8.27.0`, `prettier@3.6.2`, `typescript@6.0.2` | `cbbcd3c33e6528a83dee3415b7af970498aa7500935d802f5e78ea3206d41392` | `d4aceee09c11197c6c35669ca2eb5d28f549028b2588ee538fdd9cb4ded4f052` | `135 / 1: node_modules/esbuild / 0 high-critical` |
| hey-api | `@hey-api/openapi-ts@0.99.0`, `typescript@6.0.2` | `cb69bf3dabe7a64e85a425758500d0aec70ca1997ebaba25601deaa0a1034f64` | `c1db21b587a73cba210cf7662c4dd1f3e8630d90d3577cec11ca56ac17cc673b` | `51 / 0 / 4 high` |

Hash ve lock inceleme komutları:

```powershell
Get-FileHash .\package.json -Algorithm SHA256
Get-FileHash .\package-lock.json -Algorithm SHA256
$lock = Get-Content .\package-lock.json -Raw | ConvertFrom-Json -AsHashtable
($lock.packages.Keys | Where-Object { $_ }).Count
$lock.packages.GetEnumerator() |
  Where-Object { $_.Value.hasInstallScript } |
  Select-Object -ExpandProperty Key
npm audit --json
```

Hey API'deki dört high bulgu aynı kök zincirdedir:
`@hey-api/openapi-ts -> @hey-api/json-schema-ref-parser -> js-yaml@4.2.0`
ve `@hey-api/shared` üzerinden taşınır. Audit kayıtları
`GHSA-52cp-r559-cp3m` ile `GHSA-5p4m-2wfm-xmqj` advisories'ini ve
`@hey-api/openapi-ts`, `@hey-api/json-schema-ref-parser`, `@hey-api/shared`,
`js-yaml` dependency yollarını raporlamıştır. Bu nedenle sayı yalnız özet değil,
reddetme kararının exact dependency yoludur.

### 8.2 Compiler/build probe'ları

Router/Query declaration probe'u sandbox lockfile'ını değiştirmeden exact React
type paketlerini ekleyip yayımlanmış declaration girişlerini doğrudan strict
olarak derlemiştir. Exact kapı:

```powershell
npm.cmd install --no-save --package-lock=false --ignore-scripts `
  @types/react@19.2.18 @types/react-dom@19.2.4
node_modules\.bin\tsc.cmd --noEmit --skipLibCheck false `
  --module NodeNext --moduleResolution NodeNext --target ES2022 `
  --jsx react-jsx --lib ES2022,DOM,DOM.Iterable --types react,react-dom `
  node_modules\react-router\dist\development\index.d.ts `
  node_modules\@tanstack\react-query\build\modern\index.d.ts
```

Komut exit `0` dönmüştür. Bu, Vite 8 runtime/build kabulü değildir; o kanıt
package-adoption PR'ına bırakılmıştır. Aynı strict declaration kapısı TanStack
Router alternatifiyle aşağıdaki exact komutta exit `2` dönmüştür:

```powershell
node_modules\.bin\tsc.cmd --noEmit --skipLibCheck false `
  --module NodeNext --moduleResolution NodeNext --target ES2022 `
  node_modules\@tanstack\router-core\dist\esm\ssr\types.d.ts
```

Exact hata:

```text
node_modules/@tanstack/router-core/dist/esm/ssr/types.d.ts(5,24):
error TS2339: Property '__beforeLoadContext' does not exist on type 'MakeRouteMatch'.
```

TypeScript 6 root-equivalent generator deneyi:

```powershell
npm install --save-exact openapi-typescript@7.13.0 `
  openapi-fetch@0.17.0 typescript@6.0.2
```

exit nonzero dönmüş ve şu çözümleme kapısında durmuş; install force edilmemiştir:

```text
npm error code ERESOLVE
npm error ERESOLVE unable to resolve dependency tree
npm error Found: typescript@6.0.2
npm error peer typescript@"^5.x" from openapi-typescript@7.13.0
```

### 8.3 Published tarball provenance

Her exact sürüm için `npm pack <package>@<version> --dry-run --json` çalıştırılmış,
çıktıdaki `integrity` ve `files[].path` alanları incelenmiştir:

| Paket | Registry SHA-512 integrity | Lisans kanıtı |
|---|---|---|
| `react-router@8.3.1` | `sha512-TEOpiO2g0TJHEOJeRVv4amUFun9v1npCKszvcquNvzETUtJ8udV86ah5eFoHT7g26bsBvT6EiIhqulR8eDF++A==` | `LICENSE.md`; 320 tarball dosyası |
| `@tanstack/react-query@5.102.8` | `sha512-TYBea4OuXWD7MhaSHq069TWbFe7rcwWN6kzT7JF0OKi1K6c1gTv2IzD6A6ExJsCMozdkqBWeuIUZmu4KQg0O5A==` | `LICENSE`; 415 tarball dosyası |
| `openapi-typescript@7.13.0` | `sha512-EFP392gcqXS7ntPvbhBzbF8TyBA+baIYEm791Hy5YkjDYKTnk/Tn5OQeKm5BIZvJihpp8Zzr4hzx0Irde1LNGQ==` | `LICENSE`; 105 tarball dosyası |
| `openapi-fetch@0.17.0` | `sha512-PsbZR1wAPcG91eEthKhN+Zn92FMHxv+/faECIwjXdxfTODGSGegYv0sc1Olz+HYPvKOuoXfp+0pA2XVt2cI0Ig==` | `LICENSE`; 14 tarball dosyası |
| `orval@8.27.0` | `sha512-USIGxWy6LuNj3wf217j6EwU14oWWQFVS6ltm45caGkKRV42zrR9rwWxeny3ClOzPBhz2t5+Z/zsVzIP/aZnSVA==` | 10 tarball dosyası; lisans girdisi yok |
| `@hey-api/openapi-ts@0.99.0` | `sha512-SePU/5oEWWkvUBYmvzdYRctseoLuskyhs4ET0RvLIcmzc8yLQoA2R+KtBIQ8bPsoSUB0m4E5SmBnl6aGSA0szQ==` | `LICENSE.md`; 61 tarball dosyası |

Bu defter gelecekteki package kabul PR'ına taşınmış bir audit artefaktı değildir.
Gerçek adoption PR'ı aynı exact girdilerle taze audit JSON, SBOM, lockfile ve CI
artefaktını yeniden üretmek zorundadır.

## 9. Birincil kaynaklar

- React Router 8.3.1 release/install/license:
  <https://github.com/remix-run/react-router/releases/tag/react-router%408.3.1>,
  <https://reactrouter.com/start/declarative/installation>,
  <https://github.com/remix-run/react-router/blob/main/LICENSE.md>
- TanStack Query install/release/license:
  <https://tanstack.com/query/latest/docs/framework/react/installation>,
  <https://github.com/TanStack/query/releases/tag/release-2026-08-27-1607>,
  <https://github.com/TanStack/query/blob/main/LICENSE>
- TanStack Router exact published metadata/repo:
  <https://registry.npmjs.org/@tanstack/react-router/1.170.32>,
  <https://github.com/TanStack/router>
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
- Hey API exact published metadata/package/issues:
  <https://registry.npmjs.org/@hey-api/openapi-ts/0.99.0>,
  <https://github.com/hey-api/openapi-ts>,
  <https://github.com/hey-api/openapi-ts/issues/4252>,
  <https://github.com/advisories/GHSA-52cp-r559-cp3m>,
  <https://github.com/advisories/GHSA-5p4m-2wfm-xmqj>
