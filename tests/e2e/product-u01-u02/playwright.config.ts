import path from 'node:path'
import { defineConfig } from '@playwright/test'
// Importing ./paths establishes (runner) or verifies (workers) the run identity before
// anything else here is evaluated; a refused run throws before the web server or browser.
import { here, frontend, origin, runDir, runId, viteCacheDir, evidenceRoot, coldStart, claimId } from './paths'

export default defineConfig({
  testDir: path.join(here, 'specs'), testMatch: /u-.*\.spec\.ts$/,
  fullyParallel: false, workers: 1, retries: 0, forbidOnly: true,
  timeout: 90_000, expect: { timeout: 12_000 },
  globalSetup: path.join(here, 'global-setup.ts'),
  outputDir: path.join(runDir, 'playwright-output'),
  preserveOutput: 'always',
  reporter: [['list'], [path.join(here, 'safe-reporter.ts')]],
  use: {
    browserName: 'chromium', baseURL: origin, viewport: { width: 1280, height: 800 },
    locale: 'tr-TR', trace: 'off', video: 'off', screenshot: 'only-on-failure', serviceWorkers: 'block',
  },
  webServer: {
    command: 'node "' + path.join(frontend, 'node_modules/vite/bin/vite.js') + '" --config "' + path.join(here, 'vite.profile.ts') + '" --configLoader runner',
    cwd: frontend, url: origin, reuseExistingServer: false, timeout: 180_000,
    stdout: 'pipe', stderr: 'pipe',
    env: { CARI010_EVIDENCE_DIR: evidenceRoot, CARI010_RUN_ID: runId, CARI010_VITE_CACHE_DIR: viteCacheDir, CARI010_COLD_START: coldStart ? '1' : '0', CARI010_RUN_CLAIM: claimId },
  },
})
