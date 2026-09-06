/**
 * T-04 — dirty company switch: "Kal" / "Değişiklikleri bırak", and the late response
 * of a write issued under the OLD company (G-SUPPORT-004 row 5; G-002 check 9;
 * UX-AC-004 context fence). Real browser, controlled responses.
 */
import { expect, test } from '@playwright/test'
import { A_ID, B_ID, LEGAL_NAME } from '../src/fixtures.js'
import { TLane, activeElement, held, json, shot, writeJsonEvidence } from '../src/tHarness.js'
import { mutationView } from '../src/fixtures.js'

test('T-04 a dirty create form makes the switch ASK; Kal keeps everything; discard opens the new company EMPTY and a late 201 of the old company writes nothing', async ({ page }, testInfo) => {
  const slug = 't04-context-switch-fence'
  const createGate = held()
  const lane = await TLane.install(page, {
    preferCompany: A_ID,
    create: async () => {
      await createGate.promise
      return json(mutationView(), 201)
    },
  })
  await page.goto('/cari/yeni')
  await expect(page.getByRole('heading', { level: 1, name: 'Yeni Cari' })).toBeVisible({ timeout: 60_000 })
  const picker = page.getByRole('combobox', { name: 'Şirket', exact: true })
  await expect(picker).toHaveValue(A_ID)
  const capabilityReadsBefore = lane.capabilityReads().length

  // Dirty form → the switch asks. Focus lands on the safe choice.
  await page.getByLabel('Hukuki ad').fill('Taslak Cari')
  await picker.selectOption(B_ID)
  const dialog = page.getByRole('dialog')
  await expect(dialog).toBeVisible()
  await expect(dialog).toHaveAttribute('aria-modal', 'true')
  await expect(dialog).toContainText('Göymen Sac Profil')
  await expect.poll(async () => (await activeElement(page)).text).toBe('Kal')
  await shot(page, 'T', slug, 'dialog-open')

  // Escape means Kal; Tab cycles inside the two actions.
  await page.keyboard.press('Tab')
  expect((await activeElement(page)).text).toBe('Değişiklikleri bırak')
  await page.keyboard.press('Tab')
  expect((await activeElement(page)).text).toBe('Kal')
  await page.keyboard.press('Escape')
  await expect(dialog).toHaveCount(0)
  await expect(picker).toHaveValue(A_ID)
  await expect(page.getByLabel('Hukuki ad')).toHaveValue('Taslak Cari')
  expect(lane.capabilityReads().filter((r) => r.companyId === B_ID), 'Kal must not read B capabilities').toHaveLength(0)
  expect(lane.capabilityReads().length).toBe(capabilityReadsBefore)

  // Fill a valid form and send; the write is HELD at the transport.
  await page.getByLabel('Taraf türü').selectOption('1')
  await page.getByLabel('Hukuki ad').fill(LEGAL_NAME)
  await page.getByRole('checkbox', { name: 'Müşteri' }).check()
  await page.getByRole('button', { name: 'Cari oluştur' }).click()
  await expect.poll(() => lane.creates().length).toBe(1)
  expect(lane.creates()[0].companyId).toBe(A_ID)
  await expect(page.getByRole('button', { name: 'Gönderiliyor…' })).toBeVisible()

  // Discard while the write is in flight: B is committed, the form is empty.
  await picker.selectOption(B_ID)
  await page.getByRole('dialog').getByRole('button', { name: 'Değişiklikleri bırak' }).click()
  await expect(picker).toHaveValue(B_ID, { timeout: 20_000 })
  await expect(page.getByRole('heading', { level: 1, name: 'Yeni Cari' })).toBeVisible()
  await expect(page.getByLabel('Hukuki ad')).toHaveValue('')
  await expect(page.getByLabel('Taraf türü')).toHaveValue('')
  await expect(page.getByRole('checkbox', { name: 'Müşteri' })).not.toBeChecked()
  expect(lane.capabilityReads().some((r) => r.companyId === B_ID)).toBe(true)
  await shot(page, 'T', slug, 'after-discard-empty-under-B')

  // The OLD company's 201 is released now. Measured: delivered or cancelled — either
  // way nothing of it may reach the new context.
  createGate.release()
  await page.waitForTimeout(1000)
  const createRecord = lane.creates()[0]
  await expect(page).toHaveURL(/\/cari\/yeni$/)
  await expect(page.locator('[data-outcome]')).toHaveCount(0)
  expect(await page.locator('body').innerText()).not.toContain('oluşturuldu')
  expect(lane.detailReads()).toHaveLength(0)
  expect(lane.creates(), 'the discarded attempt is never resent under the new company').toHaveLength(1)
  expect(lane.creates().filter((r) => r.companyId === B_ID)).toHaveLength(0)
  await expect(page.getByLabel('Hukuki ad')).toHaveValue('')
  await expect(picker).toHaveValue(B_ID)

  writeJsonEvidence('T', 'measurements', `${slug}.json`, {
    kalKeptCompanyAndForm: true,
    escapeMeansKal: true,
    discardOpenedNewCompanyEmpty: true,
    oldCompanyWrite: {
      companyId: createRecord.companyId,
      delivered: createRecord.delivered,
      status: createRecord.status,
      pageObservedFailure: lane.failures.find((f) => f.url === '/api/v1/counterparties')?.errorText ?? null,
    },
    createRequestsTotal: lane.creates().length,
    createRequestsUnderB: 0,
  })
  await lane.finish(testInfo, slug)
})
