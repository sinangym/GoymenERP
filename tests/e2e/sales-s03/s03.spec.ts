import { expect, test } from '@playwright/test'

/**
 * Sales s03 v1 — fail-closed browser evidence for the order-draft screens (#260) on the C-01
 * disposable stack. Today no shell capability grants a Sales screen (the provider is staged and
 * no grant exists), so the real app must close every Sales route onto the neutral not-found block
 * and must not read the draft API. Nothing is mocked or intercepted. v2 (BROWSER-CI stage 2, with
 * the shell mount and a synthetic disposable grant) replaces these two cases with the real
 * create → replay → detail path.
 */

const unauthorizedBaseUrl = process.env.BROWSER_E2E_UNAUTHORIZED_BASE_URL?.trim() ?? ''
const salesRoutes = ['/satis/siparisler', '/satis/siparisler/yeni', '/satis/siparisler/00000000-0000-4000-8000-000000000001']

for (const route of salesRoutes) {
  test(`S03-V1-01 the authorized profile is closed out of ${route} and reads no draft`, async ({ page }) => {
    let draftReads = 0
    page.on('request', (request) => {
      if (new URL(request.url()).pathname.startsWith('/api/v1/sales/drafts')) draftReads += 1
    })

    await page.goto(route)

    await expect(page.locator('[data-state="not-found"]')).toBeVisible({ timeout: 30_000 })
    await expect(page.getByRole('table')).toHaveCount(0)
    expect(draftReads, 'an ungranted actor must not read the draft API').toBe(0)
  })
}

test('S03-V1-02 the unauthorized profile is closed out of the Sales list and reads no draft', async ({ page }) => {
  expect(unauthorizedBaseUrl, 'BROWSER_E2E_UNAUTHORIZED_BASE_URL is not set').not.toBe('')
  let draftReads = 0
  page.on('request', (request) => {
    if (new URL(request.url()).pathname.startsWith('/api/v1/sales/drafts')) draftReads += 1
  })

  await page.goto(new URL('/satis/siparisler', unauthorizedBaseUrl).toString())

  await expect(page.locator('[data-state="not-found"]')).toBeVisible({ timeout: 30_000 })
  expect(draftReads).toBe(0)
})
