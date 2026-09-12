/**
 * Warm-up, and the run's own identity record.
 *
 * A cold Vite dev server compiles and optimizes on the first navigation, which can
 * exceed a test's timeout and make the FIRST case fail for a reason that has nothing to
 * do with the application. One real navigation here removes that, and recording the
 * browser build plus the bound tuple means the evidence says which code this run
 * actually served rather than which code was merged.
 */
import { mkdirSync, writeFileSync } from 'node:fs'
import path from 'node:path'
import { chromium, type FullConfig } from '@playwright/test'
import { readOrganizationManifest } from './src/manifest.js'
import { EVIDENCE_ROOT } from './src/recorder.js'

export default async function globalSetup(config: FullConfig): Promise<void> {
  // Re-read here so a missing tuple stops the run before a browser is launched.
  const manifest = readOrganizationManifest()
  const baseURL = config.projects[0]?.use?.baseURL
  if (baseURL === undefined) throw new Error('baseURL is not configured')

  const browser = await chromium.launch()
  const page = await browser.newPage()
  const startedAt = Date.now()
  await page.goto(`${baseURL}/`, { waitUntil: 'domcontentloaded', timeout: 180_000 })
  await page
    .locator('nav, [role="status"], [data-state], h1')
    .first()
    .waitFor({ state: 'visible', timeout: 180_000 })
  const warmUpMs = Date.now() - startedAt

  mkdirSync(EVIDENCE_ROOT, { recursive: true })
  writeFileSync(
    path.join(EVIDENCE_ROOT, 'run.json'),
    `${JSON.stringify(
      {
        browserName: 'chromium',
        browserVersion: browser.version(),
        baseURL,
        warmUpMs,
        warmedAtUtc: new Date().toISOString(),
        runId: manifest.runId,
        apiBaseUrl: manifest.apiBaseUrl,
        binding: manifest.binding,
        locationsOnlyActorPrepared: manifest.locationsOnlyActor !== undefined,
      },
      null,
      2,
    )}\n`,
    'utf8',
  )
  await browser.close()
}
