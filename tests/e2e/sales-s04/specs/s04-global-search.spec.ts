/**
 * Sales s04 — AT-002 global search against the REAL stack (`browser:s04:ci`): the shell's search box, the grouped results
 * page and the owner screens, with the suite's EXISTING actors and seed (no actor, role or grant is added for this file).
 *
 * Real Chromium, the real web build of the exact head, the real API and SQL Server, the `normal` capability profile.
 * NOTHING IS MOCKED OR INTERCEPTED: the only thing the suite adds to the browser is the actor's identity header.
 *
 * What it uses from the stage-2 seed (tests/GoymenERP.BrowserSuiteFixtureTool/Suites/S04):
 *   - `deliveryDraftId`: the second draft, only ever REOPENED (never mutated) — its document number is read from the real
 *     Sales detail route, so nothing is guessed;
 *   - `realPartyId`: the REAL Cari party with a GDC account — its code and legal name are read from the real Cari detail;
 *   - `preparerActor`: holds sales.drafts.read AND counterparty.parties.read in GDC (the SALES-A2a precedent), so the
 *     Cari and Sales kinds are searched; no actor here holds the global Product authority, so the Product kind never
 *     appears;
 *   - `readerActor`: holds only sales.drafts.read, so the Cari kind is absent from its answers (AT-002: an unreadable
 *     kind has no group, hit or "more").
 *
 * Depends on the shell mount (the coordinator's step): `<GlobalSearchBox />` in the AppShell toolbar and `...searchRoutes`
 * in the router, plus the host's `AddGlobalSearch()` / `MapGlobalSearchEndpoints()`.
 *
 *   S04-GS1 the preparer types the draft's number into the shell box → grouped results (the URL carries the term) → opens
 *           the order → Back returns to the same results → a reload shows the same; the answer carries no price or count
 *   S04-GS2 the preparer searches the REAL party's name → the Cari group links the Cari detail; "more"-less, Product-less
 *   S04-GS3 the reader's answer for the same name has no Cari group; its order search still answers the Sales group
 */

import { expect, test, type Page, type Response } from '@playwright/test'
import { readEnvironment } from '../src/env.js'

const env = readEnvironment()
const { preparerActor, readerActor, companyId, deliveryDraftId, realPartyId } = env.seed
const DEVELOPMENT_USER_HEADER = 'X-Development-User'
const SEARCH_PATH = '/api/v1/search'
const COUNTERPARTY_CONTRACT_HEADER = 'X-ERP-Counterparty-Contract'
const COUNTERPARTY_CONTRACT_VERSION = 'optional-trade-roles-v1'
const PARTY_KIND = 'counterparty.party'
const PRODUCT_KIND = 'product.item'
const SALES_KIND = 'sales.order'
const PRICE_WORDS = ['price', 'amount', 'discount', 'currency', 'payment', 'total', 'count', 'cost']

interface SearchReading {
  readonly query: string
  readonly groups: readonly { readonly kind: string; readonly items: readonly { readonly id: string; readonly code: string }[]; readonly hasMore: boolean }[]
}

async function signInAs(page: Page, actor: string): Promise<void> {
  await page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: actor })
}

async function readDocumentNo(page: Page): Promise<string> {
  const response = await page.request.get(`${env.apiBaseUrl}/api/v1/sales/drafts/${deliveryDraftId}`, {
    headers: { [DEVELOPMENT_USER_HEADER]: preparerActor, 'X-Company-Id': companyId },
  })
  expect(response.status(), 'the real Sales detail answers the preparer').toBe(200)
  const documentNo = ((await response.json()) as { draft: { documentNo: string } }).draft.documentNo
  expect(documentNo.length).toBeGreaterThan(2)
  return documentNo
}

async function readParty(page: Page): Promise<{ readonly code: string; readonly legalName: string }> {
  const response = await page.request.get(`${env.apiBaseUrl}/api/v1/counterparties/${realPartyId}`, {
    headers: {
      [DEVELOPMENT_USER_HEADER]: preparerActor,
      'X-Company-Id': companyId,
      [COUNTERPARTY_CONTRACT_HEADER]: COUNTERPARTY_CONTRACT_VERSION,
    },
  })
  expect(response.status(), 'the real Cari detail answers the preparer').toBe(200)
  return (await response.json()) as { code: string; legalName: string }
}

function isSearch(response: Response, term: string): boolean {
  const url = new URL(response.url())
  return url.pathname === SEARCH_PATH && url.searchParams.get('q') === term && response.request().method() === 'GET'
}

async function searchAnswer(response: Response): Promise<SearchReading> {
  expect(response.status()).toBe(200)
  const body = (await response.json()) as SearchReading
  const text = JSON.stringify(body).toLowerCase()
  for (const word of PRICE_WORDS) expect(text, `the search answer carries no "${word}" member`).not.toContain(`"${word}`)
  return body
}

test.describe.configure({ mode: 'serial' })

test('S04-GS1 box → grouped results → open the order → Back returns to the same results', async ({ page }) => {
  const documentNo = await readDocumentNo(page)
  await signInAs(page, preparerActor)
  await page.goto('/')

  const box = page.getByRole('search', { name: 'Genel arama' }).first()
  await box.getByLabel('Genel arama').fill(documentNo)
  const first = page.waitForResponse((response) => isSearch(response, documentNo))
  await box.getByRole('button', { name: 'Genel aramada ara' }).click()
  const answer = await searchAnswer(await first)
  await expect(page).toHaveURL(new RegExp(`/arama\\?q=${encodeURIComponent(documentNo)}$`))

  expect(answer.groups.map((group) => group.kind)).not.toContain(PRODUCT_KIND)
  const sales = answer.groups.find((group) => group.kind === SALES_KIND)
  expect(sales?.items.map((item) => item.id)).toContain(deliveryDraftId)

  const salesGroup = page.getByTestId(`global-search-group-${SALES_KIND}`)
  const result = salesGroup.getByRole('link', { name: new RegExp(documentNo) })
  await expect(result).toHaveAttribute('href', `/satis/siparisler/${deliveryDraftId}`)
  await result.click()
  await expect(page).toHaveURL(new RegExp(`/satis/siparisler/${deliveryDraftId}$`))

  const again = page.waitForResponse((response) => isSearch(response, documentNo))
  await page.goBack()
  await expect(page).toHaveURL(new RegExp(`/arama\\?q=${encodeURIComponent(documentNo)}$`))
  await searchAnswer(await again)
  await expect(page.getByTestId(`global-search-group-${SALES_KIND}`).getByRole('link', { name: new RegExp(documentNo) })).toBeVisible()
  await expect(page.getByLabel('Aranan metin')).toHaveValue(documentNo)

  const reload = page.waitForResponse((response) => isSearch(response, documentNo))
  await page.reload()
  await searchAnswer(await reload)
  await expect(page.getByTestId(`global-search-group-${SALES_KIND}`).getByRole('link', { name: new RegExp(documentNo) })).toBeVisible()
  await expect(page.getByTestId(`global-search-group-${PRODUCT_KIND}`)).toHaveCount(0)
})

test('S04-GS2 the preparer finds the REAL Cari party and opens its detail', async ({ page }) => {
  const party = await readParty(page)
  await signInAs(page, preparerActor)
  const response = page.waitForResponse((candidate) => isSearch(candidate, party.legalName))
  await page.goto(`/arama?q=${encodeURIComponent(party.legalName)}`)
  const answer = await searchAnswer(await response)
  const cari = answer.groups.find((group) => group.kind === PARTY_KIND)
  expect(cari?.items.map((item) => item.id)).toContain(realPartyId)
  expect(answer.groups.map((group) => group.kind)).not.toContain(PRODUCT_KIND)

  const link = page.getByTestId(`global-search-group-${PARTY_KIND}`).getByRole('link', { name: new RegExp(party.code) })
  await expect(link).toHaveAttribute('href', `/cari/${realPartyId}`)
  await link.click()
  await expect(page).toHaveURL(new RegExp(`/cari/${realPartyId}$`))
  await page.goBack()
  await expect(page.getByTestId(`global-search-group-${PARTY_KIND}`).getByRole('link', { name: new RegExp(party.code) })).toBeVisible()
})

test('S04-GS3 the reader, without the Cari read, gets no Cari group — its order search still answers', async ({ page }) => {
  const party = await readParty(page)
  const documentNo = await readDocumentNo(page)
  await signInAs(page, readerActor)

  const byName = page.waitForResponse((candidate) => isSearch(candidate, party.legalName))
  await page.goto(`/arama?q=${encodeURIComponent(party.legalName)}`)
  const nameAnswer = await searchAnswer(await byName)
  expect(nameAnswer.groups.map((group) => group.kind)).not.toContain(PARTY_KIND)
  expect(JSON.stringify(nameAnswer)).not.toContain(realPartyId)
  await expect(page.getByTestId(`global-search-group-${PARTY_KIND}`)).toHaveCount(0)

  const byNumber = page.waitForResponse((candidate) => isSearch(candidate, documentNo))
  await page.goto(`/arama?q=${encodeURIComponent(documentNo)}`)
  const numberAnswer = await searchAnswer(await byNumber)
  expect(numberAnswer.groups.map((group) => group.kind)).toEqual([SALES_KIND])
  expect(numberAnswer.groups[0].items.map((item) => item.id)).toContain(deliveryDraftId)
})
