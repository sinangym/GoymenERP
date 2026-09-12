import path from 'node:path'
import { mkdirSync, writeFileSync } from 'node:fs'
import type { Plugin } from '../../../src/frontend/goymen-erp-web/node_modules/vite/dist/node/index'
import base from '../../../src/frontend/goymen-erp-web/vite.config'
import { here, repo, frontend, runDir, runId, viteCacheDir, coldStart, role, origin, uiPort, lane, liveBinding } from './paths'

const configLoadedAtUtc = new Date().toISOString()
/**
 * Where `/api` goes. T lane: a closed loopback port, so a request that escaped the
 * browser-side interception fails visibly instead of reaching anything. L lane: the API
 * origin of the gate-verified binding this web server JOINED (`./paths` re-hashed the
 * manifest) — never an ambient variable.
 */
const apiTarget = lane === 'L' ? (liveBinding as NonNullable<typeof liveBinding>).apiBaseUrl : 'http://127.0.0.1:9'

const ownership: Plugin = {
  name: 'urun-owned-loopback-service',
  configureServer(server) {
    server.httpServer?.once('listening', () => {
      mkdirSync(path.join(runDir, 'service'), { recursive: true })
      writeFileSync(path.join(runDir, 'service', 'ui-' + process.pid + '.json'), JSON.stringify({
        task_id: 'U-01-U-02', lane, runId, role, pid: process.pid, configLoadedAtUtc, listeningAtUtc: new Date().toISOString(),
        startupMs: Date.now() - Date.parse(configLoadedAtUtc), origin, sourceRoot: here,
        cacheDir: viteCacheDir, coldStart, controlledTOnly: lane === 'T', apiProxyTarget: lane === 'L' ? apiTarget : null,
        // The snapshot this server serves against — the same hashes every evidence record carries.
        liveBinding: liveBinding === null ? null : { manifestSha256: liveBinding.manifestSha256, acceptedContractSha256: liveBinding.acceptedContractSha256, sourceBindingSha256: liveBinding.sourceBindingSha256, contractId: liveBinding.contractId, resourceRunId: liveBinding.resourceRunId, resourceInstanceId: liveBinding.resourceInstanceId },
      }, null, 2) + '\n')
    })
  },
}
/**
 * The bundle's development user, in the L lane ONLY, and from the joined binding only.
 *
 * `envPrefix` below deliberately blocks every ambient `VITE_*` variable from reaching
 * `import.meta.env` — which is right for the T lane and was WRONG for the L lane's
 * actor: the first live run (`urun-l-da7ad01-01`) served a bundle whose
 * `import.meta.env.VITE_DEV_USER` was undefined, so the app fell back to its default
 * development user, the server published no Product capability for it and the list
 * route rendered the neutral block. The actor is therefore compiled in with `define`
 * from the hash-checked snapshot, not read from the environment — an ambient
 * `VITE_DEV_USER` still cannot redirect it.
 */
const compiledDevUser = liveBinding === null ? {} : { 'import.meta.env.VITE_DEV_USER': JSON.stringify(liveBinding.actor) }

/** The production Vite config with a test-only root, entry and cache; no production module is transformed. */
export default {
  ...base,
  root: here,
  envDir: false,
  envPrefix: 'URUN_NO_PUBLIC_ENV_',
  define: { ...((base as { define?: Record<string, unknown> }).define ?? {}), ...compiledDevUser },
  cacheDir: viteCacheDir,
  plugins: [...(base.plugins ?? []), ownership],
  resolve: {
    alias: Object.fromEntries(['react', 'react-dom', 'react-router', '@tanstack/react-query'].map(
      (name) => [name, path.join(frontend, 'node_modules', name)],
    )),
    dedupe: ['react', 'react-dom', 'react-router', '@tanstack/react-query'],
  },
  server: {
    host: '127.0.0.1', port: uiPort, strictPort: true, fs: { allow: [repo] },
    // Same-origin: the browser only ever talks to this loopback origin; `/api` is
    // forwarded server-side, so no cross-origin request — and no CORS policy — is involved.
    proxy: { '/api': { target: apiTarget, changeOrigin: true } },
  },
}
