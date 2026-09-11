import path from 'node:path'
import { defineConfig } from '@playwright/test'
// Importing ./paths establishes (runner) or verifies (workers) the run identity before
// anything else in this file is evaluated; a refused run throws here, before the web server,
// the browser or any test exists.
import { here, frontend, origin, runDir, runId, viteCacheDir, evidenceRoot, coldStart, claimId } from './paths'
export default defineConfig({
  testDir: path.join(here, 'specs'), testMatch: /t-.*\.spec\.ts$/,
  fullyParallel: false, workers: 1, retries: 0, forbidOnly: true,
  timeout: 90_000, expect: { timeout: 12_000 },
  // One real navigation before any test: cold-start dependency optimization and first
  // transforms never land inside a test's own timeout, and their cost is recorded.
  globalSetup: path.join(here, 'global-setup.ts'),
  // Playwright removes outputDir when a run starts; that is harmless only because the run
  // directory was claimed fresh by this run (an existing run directory is refused), so no
  // earlier run's failure artifacts can be inside it. preserveOutput keeps this run's own
  // artifacts after passing tests; it does not prevent the start-up removal.
  outputDir: path.join(runDir, 'playwright-output'),
  preserveOutput: 'always',
  reporter: [['list'], [path.join(here, 'safe-reporter.ts')]],
  use: {
    browserName: 'chromium', baseURL: origin, viewport: { width: 1280, height: 800 },
    locale: 'tr-TR', trace: 'off', video: 'off', screenshot: 'only-on-failure', serviceWorkers: 'block',
  },
  webServer: {
    command: 'node "' + path.join(frontend, 'node_modules/vite/bin/vite.js') + '" --config "' + path.join(here, 'vite.profile.ts') + '" --configLoader runner',
    // 180 s: the cold Vite start (dependency optimization included) happens inside this
    // window; C010/C011 used 120 s. The cost itself is measured by the warm-up record.
    cwd: frontend, url: origin, reuseExistingServer: false, timeout: 180_000,
    stdout: 'pipe', stderr: 'pipe',
    // The verified identity is handed to the web server explicitly (the runner's environment
    // is inherited as well); the web server joins the claimed run and never claims itself.
    env: { CARI010_EVIDENCE_DIR: evidenceRoot, CARI010_RUN_ID: runId, CARI010_VITE_CACHE_DIR: viteCacheDir, CARI010_COLD_START: coldStart ? '1' : '0', CARI010_RUN_CLAIM: claimId },
  },
})
