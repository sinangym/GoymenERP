/**
 * T-09 — the status window and the shell's company-switch question, LAYERED right.
 *
 * Found by the first real L-04 run (c006-7f12ed40ff9a): with a reason typed in the
 * status window, changing the company made the shell ask — the dialog mounted and
 * "Değişiklikleri bırak" resolved — but the feature window's backdrop (z-index 40)
 * painted OVER the shell's dialog (z-index 20), so the click was intercepted by the
 * window's own textarea, 1136 times, until the 600 s budget ran out. A real user would
 * have seen the question and been unable to answer it.
 *
 * This case measures the layering in a real Chromium with controlled responses, and
 * measures it the way a user meets it: a HIT TEST at the button's centre must land in
 * the shell's dialog, and the click must be a plain click — no `force`, no dispatched
 * event — that actually commits the switch. It then checks that the discarded window
 * and its reason do not survive into company B and that nothing was written.
 *
 * Not the L lane: no API, no environment, no acceptance claim beyond "the shell's
 * question is on top and answerable".
 */
import { expect, test } from '@playwright/test'
import { A_ID, ACC_A, ALL_CAPS, B_ID, PARTY, mutationView } from '../src/fixtures.js'
import { TLane, activeElement, json, shot, writeJsonEvidence } from '../src/tHarness.js'

const STATUS_CONTROL = 'cari.accounts.status.update'
const DEACTIVATE_ACTION = 'Bu şirkette pasife al'
const REASON = 'T-09 — terk edilecek gerekçe'

test('T-09 a typed reason makes the company switch ASK, the shell question is ON TOP of the status window and answerable with a plain click; the window and the reason do not follow into B; nothing is written', async ({
  page,
}, testInfo) => {
  const slug = 't09-account-status-switch-layering'
  const lane = await TLane.install(page, {
    slug,
    preferCompany: A_ID,
    capabilities: () => [...ALL_CAPS, STATUS_CONTROL],
    // Acknowledged under the client contract: the status action is closed until the
    // card's OWN read is acknowledged (P2-6), so a silent controlled read would leave
    // the button disabled and measure nothing about layering.
    detail: () =>
      json(
        mutationView({
          companyAccounts: [{ id: ACC_A, companyId: A_ID, roles: 0, isActive: true, version: '7' }],
        }),
        200,
        { 'X-ERP-Counterparty-Contract': 'optional-trade-roles-v1' },
      ),
  })

  await page.goto(`/cari/${PARTY}`)
  await expect(page.locator('[data-section="account"]')).toBeVisible({ timeout: 60_000 })
  const picker = page.getByRole('combobox', { name: 'Şirket', exact: true })
  await expect(picker).toHaveValue(A_ID)

  // The status window, with a reason typed: a dirty scope the shell must defend.
  await page.getByRole('button', { name: DEACTIVATE_ACTION }).click({ timeout: 30_000 })
  const statusWindow = page.locator('[role="dialog"][data-intent="deactivate"]')
  await expect(statusWindow).toBeVisible()
  await page.locator('#cari-account-status-reason').fill(REASON, { timeout: 30_000 })
  await shot(page, 'T', slug, '01-window-with-reason')

  await picker.selectOption(B_ID)
  const shellDialog = page.locator('[role="dialog"]:not([data-intent])')
  await expect(shellDialog, 'the shell must ask before discarding a typed reason').toBeVisible({ timeout: 30_000 })
  await expect(shellDialog).toHaveAttribute('aria-modal', 'true')
  // The shell's own focus management still wins: the safe answer holds the focus.
  await expect.poll(async () => (await activeElement(page)).text).toBe('Kal')

  /**
   * THE LAYERING, measured as a hit test: the element at the centre of the shell's
   * "Değişiklikleri bırak" must be that button (or something inside the shell's dialog),
   * not the status window's textarea or backdrop. This is what the real run failed on.
   */
  const discard = shellDialog.getByRole('button', { name: 'Değişiklikleri bırak' })
  const hit = await discard.evaluate((button) => {
    const rect = button.getBoundingClientRect()
    const el = document.elementFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2)
    const inShellDialog = el?.closest('[role="dialog"]:not([data-intent])') !== null
    const inStatusWindow = el?.closest('[data-dialog="account-status"]') !== null
    // The stacking level that actually applies: the nearest ancestor (the dialog
    // included) whose computed z-index is numeric — the panel itself is `auto`, the
    // shell puts the number on its fixed wrapper.
    let node: Element | null = button
    let shellZ: number | null = null
    while (node !== null && shellZ === null) {
      const z = window.getComputedStyle(node).zIndex
      if (z !== 'auto' && z !== '') shellZ = Number(z)
      node = node.parentElement
    }
    return {
      tag: el?.tagName.toLowerCase() ?? null,
      text: (el?.textContent ?? '').trim().slice(0, 40),
      inShellDialog,
      inStatusWindow,
      shellZ,
    }
  })
  const backdropZ = await page.locator('[data-dialog="account-status"]').evaluate((el) => window.getComputedStyle(el).zIndex)
  expect(hit.inStatusWindow, `the status window must not paint over the shell's question (hit: ${JSON.stringify(hit)})`).toBe(false)
  expect(hit.inShellDialog, `the shell's answer must be the element a click reaches (hit: ${JSON.stringify(hit)})`).toBe(true)
  expect(hit.shellZ, 'the shell dialog has a numeric stacking level').not.toBeNull()
  expect(Number(backdropZ), 'the feature window stacks BELOW the shell dialog').toBeLessThan(hit.shellZ ?? Number.NaN)
  await shot(page, 'T', slug, '02-shell-question-on-top')

  // A PLAIN click — no force, no dispatched event — and it commits the switch.
  await discard.click({ timeout: 10_000 })
  await expect(picker).toHaveValue(B_ID, { timeout: 20_000 })
  await expect(shellDialog).toHaveCount(0)
  // The window and the reason do not follow into B.
  await expect(page.locator('[role="dialog"][data-intent]')).toHaveCount(0)
  expect(await page.locator('#cari-account-status-reason').count()).toBe(0)
  // Nothing was written: no status PUT, in either company.
  expect(lane.records.filter((r) => r.method === 'PUT')).toEqual([])
  await shot(page, 'T', slug, '03-under-b-no-window')

  writeJsonEvidence('T', 'measurement', `${slug}.json`, {
    slug,
    hitTest: hit,
    statusBackdropZIndex: backdropZ,
    puts: lane.records.filter((r) => r.method === 'PUT').length,
    boundaries: [
      'Controlled responses in a real Chromium; no API, no environment, no acceptance claim.',
      'Measures only that the shell company-switch question paints above the status window and is answerable with a plain click, and that the discarded window/reason do not reach company B.',
    ],
  })
  await lane.finish(testInfo, slug)
})
