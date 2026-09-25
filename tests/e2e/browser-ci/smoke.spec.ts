import { expect, test, type Response } from '@playwright/test'

/**
 * BROWSER-CI plumbing smoke. Proves the selectable workflow's stack end to end — real Chromium,
 * real web build of the head under test, real API, real SQL with the canonical C-01 fixture —
 * and nothing about any feature. Nothing here is mocked or intercepted.
 */

const unauthorizedBaseUrl = process.env.BROWSER_E2E_UNAUTHORIZED_BASE_URL?.trim() ?? ''

test('BROWSER-CI-SMOKE-01 the authorized profile reads the real Cari list through the real API', async ({ page }) => {
  const listAnswers: Response[] = []
  page.on('response', (response) => {
    if (new URL(response.url()).pathname === '/api/v1/counterparties') listAnswers.push(response)
  })

  await page.goto('/cari')

  await expect(page.getByRole('heading', { level: 1, name: 'Cari Kartlar' })).toBeVisible({ timeout: 30_000 })
  await expect(page.locator('table tbody tr').first()).toBeVisible({ timeout: 30_000 })
  expect(listAnswers.length, 'the list was not read from the API').toBeGreaterThan(0)
  expect(listAnswers.every((response) => response.status() === 200), 'a list read was not answered 200').toBe(true)
})

test('BROWSER-CI-SMOKE-02 the unauthorized profile gets the neutral block and no list', async ({ page }) => {
  expect(unauthorizedBaseUrl, 'BROWSER_E2E_UNAUTHORIZED_BASE_URL is not set').not.toBe('')
  let listReads = 0
  page.on('request', (request) => {
    if (new URL(request.url()).pathname === '/api/v1/counterparties') listReads += 1
  })

  await page.goto(new URL('/cari', unauthorizedBaseUrl).toString())

  await expect(page.locator('[data-state="not-found"]')).toBeVisible({ timeout: 30_000 })
  await expect(page.getByRole('table')).toHaveCount(0)
  expect(listReads, 'an ungranted actor must not read the list').toBe(0)
})
