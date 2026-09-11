/**
 * T-02 — CARI-LINK cursor recovery and cache authority in a REAL browser
 * (G-SUPPORT-004 row 2; closes C-005-P2-001 and C-DESKTOP-002-P2-001 at browser
 * level). Controlled responses; every candidate GET the browser issues is recorded,
 * so "fresh read" means a request that actually went out, never a cache hit.
 *
 * Counting starts from a BASELINE taken once the first page is on screen: under the
 * Vite dev server React StrictMode double-invokes effects, so the very first read
 * can be issued twice (one of them cancelled). Responders therefore never key on a
 * request ordinal; they key on the phase the scenario is in.
 */
import { expect, test, type Page } from '@playwright/test'
import { B_ID, PARTY_2, PROBLEM, ROLE, candidate, candidatePage } from '../src/fixtures.js'
import { TLane, json, problem, shot, writeJsonEvidence } from '../src/tHarness.js'

const ROUTE = '/cari/sirkete-bagla'
const P2 = candidate({ id: PARTY_2, code: 'CAR-000002' })
const P2_LINKED = {
  id: PARTY_2,
  code: 'CAR-000002',
  kind: 1,
  legalName: 'Örnek Metal Sanayi ve Ticaret A.Ş.',
  tradeName: 'Örnek Metal',
  isActive: true,
  version: '1',
  companyAccounts: [{ id: 'dddddddd-2222-2222-2222-222222222222', companyId: B_ID, roles: ROLE.Supplier, isActive: true, version: '1' }],
}

async function openWorkspace(page: Page): Promise<void> {
  await page.goto(ROUTE)
  await expect(page.getByRole('heading', { level: 1, name: 'Cariyi Şirkete Bağla' })).toBeVisible({ timeout: 60_000 })
}

function selectionState(page: Page) {
  return page.locator('[data-selection]').getAttribute('data-selection')
}

test('T-02a invalid_cursor: the chain is dropped, a FRESH cursorless GET recovers, "yenilendi" only after it lands, the old choice is no authority', async ({ page }, testInfo) => {
  const slug = 't02a-invalid-cursor-recovery'
  let brokenCursorSeen = false
  const lane = await TLane.install(page, {
    preferCompany: B_ID,
    candidates: ({ url }) => {
      if (url.searchParams.get('cursor') === 'c1') {
        brokenCursorSeen = true
        return problem(400, PROBLEM.invalidCursor)
      }
      // Before the break: the page the choice is made from. After it: the recovery page.
      return brokenCursorSeen
        ? json(candidatePage([P2]))
        : json(candidatePage([candidate()], { hasMore: true, nextCursor: 'c1' }))
    },
  })
  await openWorkspace(page)
  await expect(page.getByRole('row', { name: /CAR-000001/ })).toBeVisible()
  const base = lane.candidateReads().length
  await page.getByRole('radio', { name: /CAR-000001/ }).check()
  await page.getByRole('checkbox', { name: 'Tedarikçi' }).check()
  await expect.poll(() => selectionState(page)).toBe('fresh')

  await page.getByRole('button', { name: 'Daha fazla aday yükle' }).click()
  // The continuation answered 400 invalid_cursor; the recovery is a REQUEST.
  await expect.poll(() => lane.candidateReads().slice(base).map((r) => r.cursor)).toEqual(['c1', null])
  await expect(page.getByRole('row', { name: /CAR-000002/ })).toBeVisible()
  await expect(page.getByText('Aday listesi yenilendi; ilk sayfaya dönüldü.')).toBeVisible()
  // The old chain and its choice are gone; the fresh page is the only authority.
  await expect(page.getByRole('row', { name: /CAR-000001/ })).toHaveCount(0)
  await expect.poll(() => selectionState(page)).toBe('none')
  await page.getByRole('button', { name: 'Şirkete bağla' }).click()
  await expect(page.locator('#cari-link-summary')).toBeVisible()
  expect(lane.links(), 'no link may be sent without a fresh selection').toHaveLength(0)
  await shot(page, 'T', slug, 'recovered-first-page')
  const reads = lane.candidateReads()
  expect(reads.every((r) => r.companyId === B_ID)).toBe(true)
  expect(reads.slice(base)[0].status).toBe(400)
  writeJsonEvidence('T', 'measurements', `${slug}.json`, {
    baselineReadsBeforeScenario: base,
    scenarioReads: reads.slice(base).map((r) => ({ cursor: r.cursor, status: r.status, delivered: r.delivered })),
    announcementAfterRecovery: true,
    linkRequests: lane.links().length,
  })
  await lane.finish(testInfo, slug)
})

test('T-02b malformed continuation: selection suspended, recovery is a fresh GET, and paging onto the same cursor again is a fresh GET too (no 30 s cache reuse)', async ({ page }, testInfo) => {
  const slug = 't02b-malformed-continuation-cache'
  let c1Reads = 0
  const lane = await TLane.install(page, {
    preferCompany: B_ID,
    capabilities: () => ['cari.parties.list', 'cari.parties.create', 'cari.accounts.link'],
    candidates: ({ url }) => {
      if (url.searchParams.get('cursor') === 'c1') {
        c1Reads += 1
        // 1: a count leaks (malformed); 2: the server has recovered.
        return c1Reads === 1 ? json(candidatePage([P2], { totalCount: 1 })) : json(candidatePage([P2]))
      }
      return json(candidatePage([candidate()], { hasMore: true, nextCursor: 'c1' }))
    },
    link: ({ url }) => (url.pathname.includes(PARTY_2) ? json(P2_LINKED, 201) : problem(404, PROBLEM.notFound)),
  })
  await openWorkspace(page)
  await expect(page.getByRole('row', { name: /CAR-000001/ })).toBeVisible()
  const base = lane.candidateReads().length
  await page.getByRole('radio', { name: /CAR-000001/ }).check()
  await page.getByRole('checkbox', { name: 'Tedarikçi' }).check()
  await expect.poll(() => selectionState(page)).toBe('fresh')

  // A malformed continuation hides the table and SUSPENDS the choice.
  await page.getByRole('button', { name: 'Daha fazla aday yükle' }).click()
  await expect(page.locator('[data-state="malformed-response"]')).toBeVisible()
  await expect(page.getByRole('radio')).toHaveCount(0)
  await expect.poll(() => selectionState(page)).toBe('suspended')
  await expect(page.getByText('Seçim güncel aday listesiyle henüz doğrulanmadı.')).toBeVisible()
  await page.getByRole('button', { name: 'Şirkete bağla' }).click()
  await expect(page.locator('#cari-link-summary')).toBeVisible()
  expect(lane.links(), 'no POST while the selection is suspended').toHaveLength(0)
  await shot(page, 'T', slug, 'suspended-after-malformed')

  // Recovery = a fresh cursorless REQUEST, not a cache hit.
  await page.locator('[data-state="malformed-response"]').getByRole('button', { name: 'Tekrar dene' }).click()
  await expect(page.getByRole('row', { name: /CAR-000001/ })).toBeVisible()
  await expect.poll(() => lane.candidateReads().slice(base).map((r) => r.cursor)).toEqual(['c1', null])
  await expect.poll(() => selectionState(page)).toBe('fresh')

  // Paging onto the SAME cursor again is a request (the malformed page held fresh by
  // the production 30 s staleTime is not shown again without one).
  await page.getByRole('button', { name: 'Daha fazla aday yükle' }).click()
  await expect(page.getByRole('row', { name: /CAR-000002/ })).toBeVisible()
  await expect.poll(() => lane.candidateReads().slice(base).map((r) => r.cursor)).toEqual(['c1', null, 'c1'])
  await expect(page.locator('[data-state="malformed-response"]')).toHaveCount(0)

  // A Party from that fresh read links: one POST, roles only, under B.
  await page.getByRole('radio', { name: /CAR-000002/ }).check()
  await expect.poll(() => selectionState(page)).toBe('fresh')
  await page.getByRole('button', { name: 'Şirkete bağla' }).click()
  await expect(page.locator('[data-outcome="linked"]')).toBeVisible()
  expect(lane.links()).toHaveLength(1)
  const link = lane.links()[0]
  expect(link.path).toBe(`/api/v1/counterparties/${PARTY_2}/company-account`)
  expect(link.companyId).toBe(B_ID)
  expect(link.body).toEqual({ roles: ROLE.Supplier })
  expect(link.idempotencyKey).toMatch(/^[0-9a-f]{32}$/)
  await shot(page, 'T', slug, 'linked-after-fresh-reads')
  writeJsonEvidence('T', 'measurements', `${slug}.json`, {
    baselineReadsBeforeScenario: base,
    scenarioReads: lane.candidateReads().slice(base).map((r) => ({ cursor: r.cursor, status: r.status, delivered: r.delivered })),
    linkRequest: { path: link.path, companyId: link.companyId, body: link.body, keyShape: 'hex32' },
  })
  await lane.finish(testInfo, slug)
})
