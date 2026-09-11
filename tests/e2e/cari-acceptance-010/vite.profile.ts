import path from 'node:path'
import { mkdirSync, writeFileSync } from 'node:fs'
import type { Plugin } from '../../../src/frontend/goymen-erp-web/node_modules/vite/dist/node/index'
import base from '../../../src/frontend/goymen-erp-web/vite.config'
import { here, repo, frontend, runDir, runId, viteCacheDir, coldStart, role } from './paths'
const configLoadedAtUtc = new Date().toISOString()
const ownership: Plugin = {
  name: 'c010-owned-loopback-service',
  configureServer(server) {
    server.httpServer?.once('listening', () => {
      mkdirSync(path.join(runDir, 'service'), { recursive: true })
      writeFileSync(path.join(runDir, 'service', 'ui-' + process.pid + '.json'), JSON.stringify({
        task_id: 'C-DESKTOP-010', runId, role, pid: process.pid, configLoadedAtUtc, listeningAtUtc: new Date().toISOString(),
        startupMs: Date.now() - Date.parse(configLoadedAtUtc), origin: 'http://127.0.0.1:15174', sourceRoot: here,
        cacheDir: viteCacheDir, coldStart, controlledTOnly: true,
      }, null, 2) + '\n')
    })
  },
}
export default {
  ...base,
  root: here,
  envDir: false,
  envPrefix: 'C010_NO_PUBLIC_ENV_',
  cacheDir: viteCacheDir,
  plugins: [...(base.plugins ?? []), ownership],
  resolve: {
    alias: Object.fromEntries(['react', 'react-dom', 'react-router', '@tanstack/react-query'].map(
      (name) => [name, path.join(frontend, 'node_modules', name)],
    )),
    dedupe: ['react', 'react-dom', 'react-router', '@tanstack/react-query'],
  },
  server: { host: '127.0.0.1', port: 15174, strictPort: true, proxy: {}, fs: { allow: [repo] } },
}
