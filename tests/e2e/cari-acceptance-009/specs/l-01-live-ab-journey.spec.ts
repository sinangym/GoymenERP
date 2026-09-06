/**
 * L-01 — the LIVE A→B journey: real browser, real API (C-DESKTOP-006 loopback
 * environment), real SQL Server behind it. No route.fulfill, no mock.
 *
 * G-SUPPORT-004 row 1 (+3 for the terminal pre-checks on B) and G-002 checks 1, 2,
 * 3, 5, 10: A create → A list → A detail; under B the new Party is invisible in the
 * list and its detail closes 404 before the link; a FRESH authorized candidate read
 * offers it; link with roles only → B list/detail; the A account, its roles and its
 * version are unchanged. The database deltas are measured OUTSIDE the browser by the
 * environment's read-only snapshot/verify tooling (evidence/L/db-*.json).
 */
import { expect, test, type Page } from '@playwright/test'
import { LiveRecorder, lShot, readManifest, writeLJson } from '../src/lHarness.js'

const manifest = readManifest()
const A = manifest.companyAId
const B = manifest.companyBId
const PREFIX = manifest.browserDataPrefix
// One journey = one Party; the token keeps a re-run distinguishable in the database.
const RUN_TOKEN = new Date().toISOString().replace(/\D/g, '').slice(8, 14)
const LEGAL_NAME = `${PREFIX} Kabul Metal Sanayi ${RUN_TOKEN} A.Ş.`
const TRADE_NAME = `${PREFIX} Kabul ${RUN_TOKEN}`
const UUID_RE = /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i

async function switchCompany(page: Page, companyId: string): Promise<void> {
  const picker = page.getByRole('combobox', { name: 'Şirket', exact: true })
  await picker.selectOption(companyId)
  const dialog = page.getByRole('dialog')
  if (await dialog.count()) await dialog.getByRole('button', { name: 'Değişiklikleri bırak' }).click()
  await expect(picker).toHaveValue(companyId, { timeout: 30_000 })
}

function factValue(page: Page, label: string) {
  return page.locator('dt', { hasText: label }).locator('xpath=following-sibling::dd[1]')
}

test('L-01 A create → list → detail; B invisible + detail 404 before link; B fresh candidate → link → list → detail; A account preserved', async ({ page }, testInfo) => {
  test.setTimeout(240_000)
  const slug = 'l01-live-ab-journey'
  // Seed the preference ONCE. The script runs on every navigation, and the shell
  // itself rewrites the preference on a committed switch — overwriting it on each
  // reload would drag the browser back to A after the switch to B.
  await page.addInitScript((id: string) => {
    if (window.localStorage.getItem('goymen-company-id') === null) window.localStorage.setItem('goymen-company-id', id)
  }, A)
  const net = LiveRecorder.attach(page)

  // ---- A: create --------------------------------------------------------------
  await page.goto('/cari/yeni')
  await expect(page.getByRole('heading', { level: 1, name: 'Yeni Cari' })).toBeVisible({ timeout: 60_000 })
  await expect(page.getByRole('combobox', { name: 'Şirket', exact: true })).toHaveValue(A)
  await page.getByLabel('Taraf türü').selectOption('1')
  await page.getByLabel('Hukuki ad').fill(LEGAL_NAME)
  await page.getByLabel(/^Ticari ad/).fill(TRADE_NAME)
  await page.getByRole('checkbox', { name: 'Müşteri' }).check()
  await page.getByRole('checkbox', { name: 'Tedarikçi' }).check()
  await lShot(page, slug, '01-A-create-filled')
  await page.getByRole('button', { name: 'Cari oluştur' }).click()

  // The verified id opens Cari 360 under A.
  await expect(page).toHaveURL(new RegExp(`/cari/${UUID_RE.source}$`, 'i'), { timeout: 30_000 })
  await expect(page.getByRole('heading', { level: 1, name: LEGAL_NAME })).toBeVisible({ timeout: 30_000 })
  await expect.poll(() => net.creates().length).toBe(1)
  const create = net.creates()[0]
  expect(create.status).toBe(201)
  expect(create.companyId).toBe(A)
  expect(create.idempotencyKey).toMatch(/^[0-9a-f]{32}$/)
  expect(create.requestBody).toEqual({ kind: 1, legalName: LEGAL_NAME, tradeName: TRADE_NAME, roles: 3 })
  const created = create.responseBody as {
    id: string
    code: string
    version: string
    legalName: string
    companyAccounts: { id: string; companyId: string; roles: number; version: string }[]
  }
  expect(created.legalName).toBe(LEGAL_NAME)
  expect(created.code).toMatch(/^CAR-\d{6}$/)
  expect(typeof created.version).toBe('string')
  expect(created.companyAccounts).toHaveLength(1)
  expect(created.companyAccounts[0].companyId).toBe(A)
  expect(created.companyAccounts[0].roles).toBe(3)
  const partyId = created.id
  const code = created.code
  await expect(factValue(page, 'Cari kodu')).toHaveText(code)
  await expect(factValue(page, 'Bu şirketteki roller')).toHaveText('Müşteri, Tedarikçi')
  await expect.poll(() => net.detailReads(partyId).length).toBeGreaterThanOrEqual(1)
  expect(net.detailReads(partyId)[0].companyId).toBe(A)
  await lShot(page, slug, '02-A-detail-after-create')

  // ---- A: list --------------------------------------------------------------------
  await page.goto('/cari')
  await expect(page.getByRole('heading', { level: 1, name: 'Cari Kartlar' })).toBeVisible({ timeout: 60_000 })
  await expect(page.getByRole('row', { name: new RegExp(code) })).toBeVisible({ timeout: 30_000 })
  await lShot(page, slug, '03-A-list-shows-new-party')

  // ---- B: invisible before the link; detail closes 404 ------------------------------
  await switchCompany(page, B)
  await page.goto('/cari')
  await expect(page.getByRole('heading', { level: 1, name: 'Cari Kartlar' })).toBeVisible({ timeout: 60_000 })
  await expect(page.getByRole('combobox', { name: 'Şirket', exact: true })).toHaveValue(B)
  await expect.poll(() => net.listReads().filter((r) => r.companyId === B && r.status !== null).length).toBeGreaterThanOrEqual(1)
  await expect(page.getByRole('row', { name: new RegExp(code) })).toHaveCount(0)
  await lShot(page, slug, '04-B-list-before-link')
  await page.goto(`/cari/${partyId}`)
  await expect(page.locator('[data-state="not-found"]')).toBeVisible({ timeout: 30_000 })
  await expect.poll(() => net.detailReads(partyId).filter((r) => r.companyId === B).map((r) => r.status)).toContain(404)
  expect(await page.locator('body').innerText()).not.toContain(LEGAL_NAME)
  await lShot(page, slug, '05-B-detail-404-before-link')

  // ---- B: fresh authorized candidate → link (roles only) -------------------------------
  await page.goto('/cari/sirkete-bagla')
  await expect(page.getByRole('heading', { level: 1, name: 'Cariyi Şirkete Bağla' })).toBeVisible({ timeout: 60_000 })
  const candidateRow = page.getByRole('row', { name: new RegExp(code) })
  await expect(candidateRow).toBeVisible({ timeout: 30_000 })
  // Successful reads only: under the Vite dev server StrictMode duplicates the first
  // read of a mount and cancels one copy, which never carries a status.
  const okCandidateReads = () => net.candidateReads().filter((r) => r.companyId === B && r.status === 200)
  const candidateReadsBeforeLink = okCandidateReads()
  expect(candidateReadsBeforeLink.length).toBeGreaterThanOrEqual(1)
  expect(net.candidateReads().every((r) => r.queryKeys.every((k) => ['q', 'pageSize', 'cursor'].includes(k)))).toBe(true)
  await candidateRow.getByRole('radio').check()
  await page.getByRole('checkbox', { name: 'Tedarikçi' }).check()
  await expect.poll(() => page.locator('[data-selection]').getAttribute('data-selection')).toBe('fresh')
  await lShot(page, slug, '06-B-candidate-selected')
  await page.getByRole('button', { name: 'Şirkete bağla' }).click()
  await expect(page).toHaveURL(new RegExp(`/cari/${partyId}$`, 'i'), { timeout: 30_000 })
  await expect(page.getByRole('heading', { level: 1, name: LEGAL_NAME })).toBeVisible({ timeout: 30_000 })
  await expect(factValue(page, 'Bu şirketteki roller')).toHaveText('Tedarikçi')
  await expect.poll(() => net.links().length).toBe(1)
  const link = net.links()[0]
  expect(link.status).toBe(201)
  expect(link.companyId).toBe(B)
  expect(link.path).toBe(`/api/v1/counterparties/${partyId}/company-account`)
  expect(link.requestBody).toEqual({ roles: 2 })
  expect(link.idempotencyKey).toMatch(/^[0-9a-f]{32}$/)
  expect(link.idempotencyKey).not.toBe(create.idempotencyKey)
  const linked = link.responseBody as { id: string; code: string; version: string; companyAccounts: { companyId: string; roles: number; version: string }[] }
  expect(linked.id).toBe(partyId)
  expect(linked.code).toBe(code)
  expect(linked.companyAccounts).toHaveLength(1)
  expect(linked.companyAccounts[0].companyId).toBe(B)
  expect(linked.companyAccounts[0].roles).toBe(2)
  await lShot(page, slug, '07-B-detail-after-link')

  // ---- B: list shows it; it is no longer a candidate -------------------------------------
  await page.goto('/cari')
  await expect(page.getByRole('row', { name: new RegExp(code) })).toBeVisible({ timeout: 30_000 })
  await lShot(page, slug, '08-B-list-after-link')
  await page.goto('/cari/sirkete-bagla')
  await expect(page.getByRole('heading', { level: 1, name: 'Cariyi Şirkete Bağla' })).toBeVisible({ timeout: 60_000 })
  await expect.poll(() => okCandidateReads().length).toBeGreaterThan(candidateReadsBeforeLink.length)
  await expect(page.getByRole('row', { name: new RegExp(code) })).toHaveCount(0)
  await lShot(page, slug, '09-B-candidates-after-link')

  // ---- A: the account, its roles and its version are unchanged ------------------------------
  await switchCompany(page, A)
  await page.goto(`/cari/${partyId}`)
  await expect(page.getByRole('heading', { level: 1, name: LEGAL_NAME })).toBeVisible({ timeout: 30_000 })
  await expect(factValue(page, 'Bu şirketteki roller')).toHaveText('Müşteri, Tedarikçi')
  await expect.poll(() => net.detailReads(partyId).filter((r) => r.companyId === A && r.status === 200).length).toBeGreaterThanOrEqual(2)
  const aDetailAfter = net.detailReads(partyId).filter((r) => r.companyId === A && r.status === 200).at(-1)!.responseBody as {
    companyAccounts: { companyId: string; roles: number; version: string; isActive: boolean }[]
  }
  expect(aDetailAfter.companyAccounts).toHaveLength(1)
  expect(aDetailAfter.companyAccounts[0].companyId).toBe(A)
  expect(aDetailAfter.companyAccounts[0].roles).toBe(3)
  expect(aDetailAfter.companyAccounts[0].version).toBe(created.companyAccounts[0].version)
  await lShot(page, slug, '10-A-detail-after-link-unchanged')

  // Every request carried exactly one company header and the bootstrap identity; no other company appeared.
  const apiRecords = net.records.filter((r) => r.path.startsWith('/api/v1/counterparties') || r.path.startsWith('/api/v1/shell/'))
  expect(apiRecords.every((r) => r.companyId === A || r.companyId === B)).toBe(true)
  expect(net.records.every((r) => r.devIdentityHeader === manifest.syntheticActorIds.browser)).toBe(true)

  writeLJson('measurements', `${slug}.json`, {
    runId: manifest.runId,
    apiBaseUrl: manifest.apiBaseUrl,
    backendHead: manifest.backendHead,
    party: { id: partyId, code, legalName: LEGAL_NAME, version: created.version },
    create: { status: create.status, companyId: create.companyId, idempotencyKey: create.idempotencyKey, body: create.requestBody, replayed: create.responseHeaders['idempotency-replayed'] ?? null },
    link: { status: link.status, companyId: link.companyId, idempotencyKey: link.idempotencyKey, body: link.requestBody, replayed: link.responseHeaders['idempotency-replayed'] ?? null },
    accountA: { before: created.companyAccounts[0], after: aDetailAfter.companyAccounts[0] },
    accountB: linked.companyAccounts[0],
    bDetailBeforeLink: 404,
    bListBeforeLinkContainedParty: false,
    bCandidateOfferedParty: true,
    bCandidateAfterLinkContainedParty: false,
    requestCounts: { creates: net.creates().length, links: net.links().length, candidateReads: net.candidateReads().length, detailReads: net.detailReads().length, listReads: net.listReads().length },
  })
  await net.finish(testInfo, slug, { party: { id: partyId, code } })
})
