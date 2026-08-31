# Codex Stage 0 Supply-Chain Evidence

- Snapshot: `2026-08-31`
- Node.js: `v24.16.0`
- npm: `11.13.0`
- Scope: PR #135 analysis evidence only
- Package/ADR/runtime adoption authority: **none**

Each child directory preserves the exact `package.json` and npm lockfile v3 used
for the clean-sandbox measurement. The SHA-256 values are listed in
`../../codex.md` and can be verified with:

```powershell
Get-ChildItem -Recurse -File package.json,package-lock.json |
  Get-FileHash -Algorithm SHA256
```

Reproduction must happen in a new expendable directory, never in the product
frontend. Copy one evidence directory there, then run:

```powershell
npm.cmd ci --ignore-scripts
npm.cmd audit --json
```

`--ignore-scripts` is the first fail-closed inspection pass. The lockfile
`hasInstallScript` inventory shows only `orval/node_modules/esbuild`. Executing
that reviewed postinstall, or any generator, requires a separate isolated K3 PoC;
these evidence files do not authorize it.

The Hey API audit stdout is canonically preserved as
`hey-api/npm-audit-2026-08-31.json` with SHA-256:

```text
27545dc94cbecfed9f6404b738a40abe516e8d6c91d6dc5196140b2a09e6298d
```

It records four high findings, both advisory IDs, affected ranges, dependency
nodes and the npm-proposed `0.97.0` semver-major fallback. A future adoption
review must create fresh audit JSON/SBOM; this dated snapshot is not a security
guarantee.

Exact strict TypeScript declaration probes:

```powershell
# react-router-query; expected exit 0
npm.cmd install --no-save --package-lock=false --ignore-scripts `
  @types/react@19.2.18 @types/react-dom@19.2.4
node_modules\.bin\tsc.cmd --noEmit --strict true --skipLibCheck false `
  --module NodeNext --moduleResolution NodeNext --target ES2022 `
  --jsx react-jsx --lib ES2022,DOM,DOM.Iterable --types react,react-dom `
  node_modules\react-router\dist\development\index.d.ts `
  node_modules\@tanstack\react-query\build\modern\index.d.ts

# tanstack-router-query; expected exit 2 / TS2339
node_modules\.bin\tsc.cmd --noEmit --strict true --skipLibCheck false `
  --module NodeNext --moduleResolution NodeNext --target ES2022 `
  node_modules\@tanstack\router-core\dist\esm\ssr\types.d.ts
```

The `openapi-typescript@7.13.0` TypeScript 6 rejection is reproduced in a fresh
empty directory with:

```powershell
npm.cmd init -y
npm.cmd install --save-exact openapi-typescript@7.13.0 `
  openapi-fetch@0.17.0 typescript@6.0.2
```

Expected result is nonzero `ERESOLVE`: found TypeScript `6.0.2`, while
`openapi-typescript@7.13.0` declares peer TypeScript `^5.x`.
