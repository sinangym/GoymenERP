/**
 * T-01 — C-02 create validation summary focus in a REAL browser (UX-AC-001;
 * closes C-005-P2-002 at browser level, G-SUPPORT-004 rows 4 and 8).
 *
 * Controlled responses (T lane): the shell endpoints are answered by the harness;
 * no command is ever sent by a client-invalid submit, and the recording proves it.
 */
import { expect, test } from '@playwright/test'
import { A_ID } from '../src/fixtures.js'
import { TLane, activeElement, shot, writeJsonEvidence } from '../src/tHarness.js'

const SLUG = 't01-create-summary-focus'

test('T-01 a client-invalid create submit focuses the validation SUMMARY, links each field error, and sends nothing', async ({ page }, testInfo) => {
  const lane = await TLane.install(page, { preferCompany: A_ID })
  await page.goto('/cari/yeni')
  await expect(page.getByRole('heading', { level: 1, name: 'Yeni Cari' })).toBeVisible({ timeout: 60_000 })
  await expect(page.getByLabel('Taraf türü')).toBeVisible()

  // Submit the empty form.
  await page.getByRole('button', { name: 'Cari oluştur' }).click()
  const summary = page.locator('#cari-create-summary')
  await expect(summary).toBeVisible()
  await expect(summary).toHaveAttribute('role', 'alert')
  await expect(summary).toHaveAttribute('tabindex', '-1')
  await expect(summary.getByRole('listitem')).toHaveText(['Taraf türü seçin.', 'Hukuki ad zorunludur.', 'En az bir rol seçin.'])

  // The summary — not a field — holds focus (UX-AC-001).
  await expect.poll(async () => (await activeElement(page)).id).toBe('cari-create-summary')
  const focusOnSummary = await activeElement(page)
  await shot(page, 'T', SLUG, 'summary-focused')

  // Field errors stay linked to their own controls.
  const kind = page.getByLabel('Taraf türü')
  await expect(kind).toHaveAttribute('aria-invalid', 'true')
  await expect(kind).toHaveAttribute('aria-describedby', 'cari-create-kind-error')
  await expect(page.locator('#cari-create-kind-error')).toHaveText('Taraf türü seçin.')
  const legalName = page.getByLabel('Hukuki ad')
  await expect(legalName).toHaveAttribute('aria-invalid', 'true')
  await expect(legalName).toHaveAttribute('aria-describedby', 'cari-create-legal-name-error')
  const customer = page.getByRole('checkbox', { name: 'Müşteri' })
  await expect(customer).toHaveAttribute('aria-invalid', 'true')
  await expect(customer).toHaveAttribute('aria-describedby', 'cari-create-roles-error')
  await expect(page.locator('form[aria-describedby="cari-create-summary"]')).toHaveCount(1)

  // Tab from the summary reaches the first control to fix, with a visible focus.
  await page.keyboard.press('Tab')
  const afterTab = await activeElement(page)
  expect(afterTab.id).toBe('cari-create-kind')
  expect(afterTab.focusVisible, 'keyboard focus on the first invalid control must be visible').toBe(true)
  await shot(page, 'T', SLUG, 'tab-to-first-invalid')

  // Correct two fields; the summary re-lists only what is left and takes focus again.
  await kind.selectOption('1')
  await legalName.fill('X')
  await page.getByRole('button', { name: 'Cari oluştur' }).click()
  await expect(summary.getByRole('listitem')).toHaveText(['En az bir rol seçin.'])
  await expect.poll(async () => (await activeElement(page)).id).toBe('cari-create-summary')
  await expect(kind).not.toHaveAttribute('aria-invalid', 'true')
  await expect(legalName).toHaveAttribute('aria-describedby', 'cari-create-legal-name-hint')

  // Nothing was sent: no key was minted, no command reached the wire.
  expect(lane.creates(), 'a client-invalid submit must not POST').toHaveLength(0)

  writeJsonEvidence('T', 'measurements', `${SLUG}.json`, {
    focusOnSummary,
    afterTab,
    listItemsFirstSubmit: 3,
    listItemsSecondSubmit: 1,
    createRequests: lane.creates().length,
  })
  await lane.finish(testInfo, SLUG)
})
