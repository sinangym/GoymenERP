/**
 * T-06 — wire payload types, metadata 400 and decoder rejections in a REAL browser
 * (G-SUPPORT-004 rows 4 and 7; G-002 checks 4 and 10). Controlled responses.
 */
import { expect, test, type Page } from '@playwright/test'
import { A_ID, B_ID, LEGAL_NAME, PROBLEM, candidate, candidatePage, linkedView, mutationView } from '../src/fixtures.js'
import { TLane, json, problem, shot, writeJsonEvidence } from '../src/tHarness.js'

async function openCreate(page: Page): Promise<void> {
  await page.goto('/cari/yeni')
  await expect(page.getByRole('heading', { level: 1, name: 'Yeni Cari' })).toBeVisible({ timeout: 60_000 })
}

test('T-06a the create body carries exactly the four fields with NUMERIC kind and roles; a 400 command_metadata_invalid is shown by code and the form is preserved', async ({ page }, testInfo) => {
  const slug = 't06a-payload-metadata-400'
  const lane = await TLane.install(page, {
    preferCompany: A_ID,
    create: () => problem(400, PROBLEM.commandMetadataInvalid),
  })
  await openCreate(page)
  await page.getByLabel('Taraf türü').selectOption('2')
  await page.getByLabel('Hukuki ad').fill(`  ${LEGAL_NAME}  `)
  await page.getByLabel(/^Ticari ad/).fill('   ')
  await page.getByRole('checkbox', { name: 'Fason' }).check()
  await page.getByRole('checkbox', { name: 'Hizmet' }).check()
  await page.getByRole('button', { name: 'Cari oluştur' }).click()

  const notice = page.locator('[data-failure="metadata"]')
  await expect(notice).toBeVisible()
  await expect(notice).toContainText('İşlem bilgisi geçersiz')
  expect(lane.creates()).toHaveLength(1)
  const request = lane.creates()[0]
  expect(request.body).toEqual({ kind: 2, legalName: LEGAL_NAME, tradeName: null, roles: 12 })
  expect(Object.keys(request.body as Record<string, unknown>)).toEqual(['kind', 'legalName', 'tradeName', 'roles'])
  expect(request.companyId).toBe(A_ID)
  expect(request.idempotencyKey).toMatch(/^[0-9a-f]{32}$/)
  expect(request.devIdentityHeader, 'dev bootstrap identity header present under Vite dev').toBe(true)
  // The form is preserved; the localized detail text is not shown; nothing is resent.
  await expect(page.getByLabel('Hukuki ad')).toHaveValue(`  ${LEGAL_NAME}  `)
  expect(await page.locator('body').innerText()).not.toContain('Yerel ayrıntı metni')
  await page.waitForTimeout(500)
  expect(lane.creates()).toHaveLength(1)
  await shot(page, 'T', slug, 'metadata-400')
  writeJsonEvidence('T', 'measurements', `${slug}.json`, {
    body: request.body,
    bodyKeyOrder: Object.keys(request.body as Record<string, unknown>),
    kindType: typeof (request.body as Record<string, unknown>).kind,
    rolesType: typeof (request.body as Record<string, unknown>).roles,
    status: request.status,
  })
  await lane.finish(testInfo, slug)
})

test('T-06b decoder: a 201 that names a foreign account is NOT success; a non-201 2xx is unknown; a candidate page with a count is malformed', async ({ page }, testInfo) => {
  const slug = 't06b-decoder-rejections'
  let creates = 0
  const lane = await TLane.install(page, {
    preferCompany: A_ID,
    capabilities: () => ['cari.parties.list', 'cari.parties.create', 'cari.accounts.link'],
    create: () => {
      creates += 1
      // 1: a well-formed 201 about the WRONG company (B account under an A command).
      // 2: a 200 with a perfect body — not in the contract for a POST.
      return creates === 1 ? json(linkedView(), 201) : json(mutationView(), 200)
    },
    candidates: () => json(candidatePage([candidate()], { totalCount: 1 })),
  })
  await openCreate(page)
  await page.getByLabel('Taraf türü').selectOption('1')
  await page.getByLabel('Hukuki ad').fill(LEGAL_NAME)
  await page.getByRole('checkbox', { name: 'Müşteri' }).check()
  await page.getByRole('button', { name: 'Cari oluştur' }).click()
  const unknown = page.locator('[data-attempt="unknown"]')
  await expect(unknown).toBeVisible()
  await expect(unknown).toContainText('Sunucu yanıtı beklenen biçimde değil.')
  await expect(page).toHaveURL(/\/cari\/yeni$/)
  await expect(page.locator('[data-outcome]')).toHaveCount(0)
  expect(await page.locator('body').innerText()).not.toContain(B_ID)
  await shot(page, 'T', slug, 'foreign-account-201-unknown')

  // Same key retry meets a 200: still unknown, still one logical attempt.
  await unknown.getByRole('button', { name: 'Aynı işlemi yeniden dene' }).click()
  await expect.poll(() => lane.creates().length).toBe(2)
  expect(lane.creates()[1].idempotencyKey).toBe(lane.creates()[0].idempotencyKey)
  await expect(page.locator('[data-attempt="unknown"]')).toBeVisible()
  await expect(page.locator('[data-outcome]')).toHaveCount(0)

  // A candidate page that counts is rejected whole.
  await page.goto('/cari/sirkete-bagla')
  await expect(page.getByRole('heading', { level: 1, name: 'Cariyi Şirkete Bağla' })).toBeVisible({ timeout: 60_000 })
  await expect(page.locator('[data-state="malformed-response"]')).toBeVisible()
  await expect(page.getByRole('radio')).toHaveCount(0)
  expect(await page.locator('body').innerText()).not.toContain(LEGAL_NAME)
  await shot(page, 'T', slug, 'candidate-count-malformed')
  writeJsonEvidence('T', 'measurements', `${slug}.json`, {
    createStatuses: lane.creates().map((r) => r.status),
    sameKeyOnRetry: lane.creates()[1].idempotencyKey === lane.creates()[0].idempotencyKey,
    successShown: false,
    candidatePageWithCountRejected: true,
  })
  await lane.finish(testInfo, slug)
})
