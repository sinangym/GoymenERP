import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { defineConfig, devices } from '@playwright/test'

/**
 * D-05 Sales → Onaylarım acceptance (`work-approvals`): the composed Work inbox against a REAL isolated stack built
 * from the exact head — real API, real SQL, the real web build — with the seed of `src/env.ts`. Read only: the suite
 * never approves anything; it proves the inbox, the record binding and the hand-off to each owner's own screen.
 *
 * The stack, seed, grants and any workflow selector belong to the acceptance environment's actual owner; this file
 * adds none of them. Retries 0 and forbidOnly; traces, screenshots and video off (the artifact-privacy decision).
 */
function required(name: string): string {
  const value = process.env[name]?.trim()
  if (!value) throw new Error(`${name} is not set; the work-approvals suite runs only inside an isolated acceptance stack.`)
  return value
}

const here = path.dirname(fileURLToPath(import.meta.url))
const artifactsDir = required('BROWSER_E2E_ARTIFACTS_DIR')

export default defineConfig({
  testDir: path.join(here, 'specs'),
  testMatch: /(^|[\\/])work-approvals\.spec\.ts$/,
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: true,
  timeout: 120_000,
  expect: { timeout: 15_000 },
  outputDir: path.join(artifactsDir, 'work-approvals-playwright-output'),
  reporter: [['list'], ['junit', { outputFile: required('BROWSER_E2E_JUNIT_FILE') }]],
  use: {
    ...devices['Desktop Chrome'],
    browserName: 'chromium',
    baseURL: required('BROWSER_E2E_AUTHORIZED_BASE_URL'),
    viewport: { width: 1440, height: 900 },
    locale: 'tr-TR',
    trace: 'off',
    screenshot: 'off',
    video: 'off',
  },
})
