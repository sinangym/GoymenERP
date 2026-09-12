import path from 'node:path'
import { mkdirSync, writeFileSync } from 'node:fs'
import type { Plugin } from '../../../src/frontend/goymen-erp-web/node_modules/vite/dist/node/index'
import base from '../../../src/frontend/goymen-erp-web/vite.config'
import { here, repo, frontend, runDir, runId, viteCacheDir, coldStart, role, origin } from './paths'

const configLoadedAtUtc = new Date().toISOString()
const ownership: Plugin = {
  name: 'urun-owned-loopback-service',
  configureServer(server) {
    server.httpServer?.once('listening', () => {
      mkdirSync(path.join(runDir, 'service'), { recursive: true })
      writeFileSync(path.join(runDir, 'service', 'ui-' + process.pid + '.json'), JSON.stringify({
        task_id: 'U-01-U-02', runId, role, pid: process.pid, configLoadedAtUtc, listeningAtUtc: new Date().toISOString(),
        startupMs: Date.now() - Date.parse(configLoadedAtUtc), origin, sourceRoot: here,
        cacheDir: viteCacheDir, coldStart, controlledTOnly: true,
      }, null, 2) + '\n')
    })
  },
}
/** The production Vite config with a test-only root, entry and cache; no production module is transformed. */
export default {
  ...base,
  root: here,
  envDir: false,
  envPrefix: 'URUN_NO_PUBLIC_ENV_',
  cacheDir: viteCacheDir,
  plugins: [...(base.plugins ?? []), ownership],
  resolve: {
    alias: Object.fromEntries(['react', 'react-dom', 'react-router', '@tanstack/react-query'].map(
      (name) => [name, path.join(frontend, 'node_modules', name)],
    )),
    dedupe: ['react', 'react-dom', 'react-router', '@tanstack/react-query'],
  },
  server: { host: '127.0.0.1', port: 15175, strictPort: true, proxy: {}, fs: { allow: [repo] } },
}
