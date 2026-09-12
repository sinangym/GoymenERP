import path from 'node:path'
import { defineConfig } from '@playwright/test'
// Importing ./paths binds the L lane to a gate-verified snapshot and establishes
// (runner) or verifies (workers) the run identity before anything else here is
// evaluated; a refused tuple or run throws before the web server or browser exists.
import { here, frontend, origin, runDir, runId, viteCacheDir, evidenceRoot, coldStart, claimId, lane, liveBinding } from './paths'
import { BINDING_ENV, bindingEnvValue } from './manifest-validator'

const webServerEnv: Record<string, string> = {
  CARI010_EVIDENCE_DIR: evidenceRoot, CARI010_RUN_ID: runId, CARI010_VITE_CACHE_DIR: viteCacheDir,
  CARI010_COLD_START: coldStart ? '1' : '0', CARI010_RUN_CLAIM: claimId, URUN_LANE: lane,
}
if (liveBinding !== null) {
  // The web server and every worker JOIN this binding as members: they re-hash the
  // files and re-derive from the SAME inputs, so the inputs travel with the binding.
  webServerEnv[BINDING_ENV] = bindingEnvValue(liveBinding)
  webServerEnv.URUN_ENVIRONMENT_MANIFEST = process.env.URUN_ENVIRONMENT_MANIFEST as string
  webServerEnv.URUN_ACCEPTED_CONTRACT_SHA256 = process.env.URUN_ACCEPTED_CONTRACT_SHA256 as string
  webServerEnv.VITE_DEV_USER = liveBinding.actor
  process.env[BINDING_ENV] = webServerEnv[BINDING_ENV]
}

export default defineConfig({
  testDir: path.join(here, 'specs'),
  // Anchored to the file name, as 009 learned to: an unanchored `l-` also matches inside words.
  testMatch: lane === 'L' ? /(^|[\\/])l-[^\\/]*\.spec\.ts$/ : /(^|[\\/])u-[^\\/]*\.spec\.ts$/,
  fullyParallel: false, workers: 1, retries: 0, forbidOnly: true,
  timeout: lane === 'L' ? 120_000 : 90_000, expect: { timeout: lane === 'L' ? 15_000 : 12_000 },
  globalSetup: path.join(here, 'global-setup.ts'),
  outputDir: path.join(runDir, 'playwright-output'),
  preserveOutput: 'always',
  reporter: [['list'], [path.join(here, 'safe-reporter.ts')]],
  use: {
    browserName: 'chromium', baseURL: origin, viewport: { width: 1280, height: 800 },
    locale: 'tr-TR', video: 'off', screenshot: 'only-on-failure', serviceWorkers: 'block',
    // T: nothing but synthetic data, traces stay off as in 010. L: the trace is evidence
    // of a real exchange with synthetic, prefix-named records and no secret; kept as 009 does.
    trace: lane === 'L' ? 'on' : 'off',
  },
  webServer: {
    command: 'node "' + path.join(frontend, 'node_modules/vite/bin/vite.js') + '" --config "' + path.join(here, 'vite.profile.ts') + '" --configLoader runner',
    cwd: frontend, url: origin, reuseExistingServer: false, timeout: 180_000,
    stdout: 'pipe', stderr: 'pipe',
    env: webServerEnv,
  },
})
