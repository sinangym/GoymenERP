/**
 * T-07 — capability gating and revocation in a REAL browser (G-SUPPORT-004 row 5;
 * G-002 check 5; UX-AC-004). Controlled responses. Requests are COUNTED: a gated
 * screen that is closed must never attempt the read or the write it is not
 * entitled to.
 */
import { expect, test } from '@playwright/test'
import { A_ID, ALL_CAPS, B_ID, LEGAL_NAME, PROBLEM } from '../src/fixtures.js'
import { TLane, problem, shot, writeJsonEvidence } from '../src/tHarness.js'

const LIST_ONLY = ['cari.parties.list', 'cari.parties.detail']

test('T-07a without the create and link grants the routes close non-disclosingly, have no nav entry and issue zero counterparty requests', async ({ page }, testInfo) => {
  const slug = 't07a-ungranted-routes'
  const lane = await TLane.install(page, { preferCompany: A_ID, capabilities: () => LIST_ONLY })
  await page.goto('/')
  await expect(page.getByRole('link', { name: 'Cari Kartlar' })).toBeVisible({ timeout: 60_000 })
  await expect(page.getByRole('link', { name: 'Yeni Cari' })).toHaveCount(0)
  await expect(page.getByRole('link', { name: 'Cariyi Şirkete Bağla' })).toHaveCount(0)

  await page.goto('/cari/yeni')
  await expect(page.locator('[data-state="not-found"]')).toBeVisible({ timeout: 60_000 })
  await expect(page.getByRole('textbox')).toHaveCount(0)
  await page.goto('/cari/sirkete-bagla')
  await expect(page.locator('[data-state="not-found"]')).toBeVisible({ timeout: 60_000 })
  await expect(page.getByRole('radio')).toHaveCount(0)
  const body = await page.locator('body').innerText()
  expect(body).not.toContain('Yeni Cari')
  expect(body).not.toContain('Cariyi Şirkete Bağla')
  expect(lane.creates()).toHaveLength(0)
  expect(lane.links()).toHaveLength(0)
  expect(lane.candidateReads(), 'a closed link route must not read candidates').toHaveLength(0)
  await shot(page, 'T', slug, 'closed-non-disclosing')
  writeJsonEvidence('T', 'measurements', `${slug}.json`, {
    capabilityReads: lane.capabilityReads().length,
    counterpartyRequests: lane.creates().length + lane.links().length + lane.candidateReads().length,
  })
  await lane.finish(testInfo, slug)
})

test('T-07b a grant revoked between route entries closes the route on re-entry; a 403 on the command withholds the form', async ({ page }, testInfo) => {
  const slug = 't07b-revoke-on-reentry-and-403'
  let capabilityReads = 0
  let revoked = false
  const lane = await TLane.install(page, {
    preferCompany: A_ID,
    capabilities: () => {
      capabilityReads += 1
      return revoked ? LIST_ONLY : ALL_CAPS
    },
    create: () => problem(403, PROBLEM.forbidden),
  })
  await page.goto('/cari/yeni')
  await expect(page.getByRole('heading', { level: 1, name: 'Yeni Cari' })).toBeVisible({ timeout: 60_000 })
  await expect(page.getByRole('link', { name: 'Yeni Cari' })).toBeVisible()

  // The command is refused with 403: the form and what was typed are withheld; the
  // session is not ended.
  await page.getByLabel('Taraf türü').selectOption('1')
  await page.getByLabel('Hukuki ad').fill(LEGAL_NAME)
  await page.getByRole('checkbox', { name: 'Müşteri' }).check()
  await page.getByRole('button', { name: 'Cari oluştur' }).click()
  await expect(page.locator('[data-state="forbidden"], [data-state="not-found"]')).toBeVisible()
  await expect(page.getByRole('textbox')).toHaveCount(0)
  await expect(page.getByRole('button', { name: /Cari oluştur/ })).toHaveCount(0)
  expect(await page.locator('body').innerText()).not.toContain('Oturum sonlandı')
  expect(lane.creates()).toHaveLength(1)
  await shot(page, 'T', slug, 'command-403-withheld')

  // The grant is revoked server-side; the next route entry re-authorizes and closes.
  revoked = true
  await page.goto('/')
  await page.goto('/cari/sirkete-bagla')
  await expect(page.locator('[data-state="not-found"]')).toBeVisible({ timeout: 60_000 })
  await expect(page.getByRole('radio')).toHaveCount(0)
  expect(lane.candidateReads(), 'no candidate read after revocation').toHaveLength(0)
  expect(capabilityReads).toBeGreaterThanOrEqual(2)
  await shot(page, 'T', slug, 'revoked-on-reentry')
  writeJsonEvidence('T', 'measurements', `${slug}.json`, {
    capabilityReads,
    createRequests: lane.creates().length,
    createStatus: lane.creates()[0]?.status ?? null,
    candidateReadsAfterRevoke: lane.candidateReads().length,
    companyHeaderOnCommand: lane.creates()[0]?.companyId === A_ID,
    otherCompanyHeadersSeen: lane.records.filter((r) => r.companyId !== null && r.companyId !== A_ID && r.companyId !== B_ID).length,
  })
  await lane.finish(testInfo, slug)
})
