/**
 * Warm-up: the first page load of a cold Vite dev server includes dependency
 * optimization and on-demand transforms, which can exceed a test's own timeout and
 * make the FIRST acceptance case fail for a reason that has nothing to do with the
 * application. One real navigation here, before any test, also records the exact
 * browser build that will run the suite.
 */
import { mkdirSync, writeFileSync } from 'node:fs'
import path from 'node:path'
import { chromium, type FullConfig } from '@playwright/test'

export default async function globalSetup(config: FullConfig): Promise<void> {
  const baseURL = config.projects[0]?.use?.baseURL ?? 'http://127.0.0.1:15173'
  const browser = await chromium.launch()
  const page = await browser.newPage()
  const startedAt = Date.now()
  await page.goto(`${baseURL}/`, { waitUntil: 'domcontentloaded', timeout: 180_000 })
  // Any rendered shell surface is enough: the modules are compiled and cached.
  await page.locator('nav, [role="status"], [data-state], h1').first().waitFor({ state: 'visible', timeout: 180_000 })
  const warmUpMs = Date.now() - startedAt
  const evidenceDir = process.env.CARI009_EVIDENCE_DIR
  if (evidenceDir) {
    const lane = (process.env.CARI009_LANE ?? 'T').toUpperCase()
    const dir = path.join(evidenceDir, lane)
    mkdirSync(dir, { recursive: true })
    writeFileSync(
      path.join(dir, 'browser.json'),
      `${JSON.stringify(
        {
          browserName: 'chromium',
          browserVersion: browser.version(),
          playwrightBaseURL: baseURL,
          warmUpMs,
          warmedAtUtc: new Date().toISOString(),
        },
        null,
        2,
      )}\n`,
      'utf8',
    )
  }
  await browser.close()
}
