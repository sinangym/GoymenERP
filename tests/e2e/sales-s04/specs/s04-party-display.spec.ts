/**
 * Sales s04 — SALES-CARI-OPTIN against the REAL stack (`browser:s04:ci`): the record-bound Cari code and name on the Sales
 * list and detail through the Sales-owned opt-in read contract (#626), with the suite's EXISTING actors and seed (no actor,
 * role, grant, fixture or override is added for this file).
 *
 * Real Chromium, the real web build of the exact head, the real API and SQL Server, the `normal` capability profile.
 * NOTHING IS MOCKED OR INTERCEPTED: the only thing the suite adds to the browser is the actor's identity header; requests
 * and answers are only observed.
 *
 * What it uses from the stage-2 seed (tests/GoymenERP.BrowserSuiteFixtureTool/Suites/S04):
 *   - `deliveryDraftId`: the second draft on the REAL Cari party, only ever REOPENED (never mutated);
 *   - `realPartyId`: that REAL Cari party — its code and legal name are read from the real Cari detail by the preparer,
 *     who holds counterparty.parties.read in GDC (the SALES-A2a precedent), so the shown texts are compared with the owner's;
 *   - `readerActor`: holds only sales.drafts.read — it sees the display with NO Cari authority and makes no Cari read;
 *   - `preparerActor`: gets the Cari card link exactly when the shell snapshot the browser received publishes the card's
 *     own detail capability (`cari.parties.detail`); otherwise the same plain text.
 *
 *   S04-PD1 a direct GET without the selector is the legacy contract: no `partyDisplay`, no acknowledgment
 *   S04-PD2 the reader's list and detail send the exact selector, receive the exact acknowledgment, and show the genuine
 *           record-bound Cari code/name — no Cari link, no browser request to the Cari API; Back and reopen keep it
 *   S04-PD3 the preparer's link follows the published Cari detail capability
 */

import { expect, test, type Page, type Request, type Response } from '@playwright/test'
import { readEnvironment } from '../src/env.js'

const env = readEnvironment()
const { preparerActor, readerActor, companyId, deliveryDraftId, realPartyId } = env.seed
const DEVELOPMENT_USER_HEADER = 'X-Development-User'
const SALES_READ_CONTRACT_HEADER = 'x-erp-sales-read-contract'
const SALES_READ_CONTRACT = 'record-party-display-v1'
const COUNTERPARTY_CONTRACT_HEADER = 'X-ERP-Counterparty-Contract'
const COUNTERPARTY_CONTRACT_VERSION = 'optional-trade-roles-v1'
const DRAFTS_PATH = '/api/v1/sales/drafts'
const DETAIL_PATH = `${DRAFTS_PATH}/${deliveryDraftId}`
const CARI_DETAIL_CAPABILITY = 'cari.parties.detail'

interface PartyDisplay {
  readonly partyId: string
  readonly code: string
  readonly legalName: string
  readonly tradeName: string | null
}

async function signInAs(page: Page, actor: string): Promise<void> {
  await page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: actor })
}

/** The Cari owner's own code and legal name of the REAL party, read by the preparer through the real Cari detail. */
async function readOwnerParty(page: Page): Promise<{ readonly code: string; readonly legalName: string; readonly tradeName: string | null }> {
  const response = await page.request.get(`${env.apiBaseUrl}/api/v1/counterparties/${realPartyId}`, {
    headers: {
      [DEVELOPMENT_USER_HEADER]: preparerActor,
      'X-Company-Id': companyId,
      [COUNTERPARTY_CONTRACT_HEADER]: COUNTERPARTY_CONTRACT_VERSION,
    },
  })
  expect(response.status(), 'the real Cari detail answers the preparer').toBe(200)
  const body = (await response.json()) as { code: string; legalName: string; tradeName?: string | null }
  return { code: body.code, legalName: body.legalName, tradeName: body.tradeName ?? null }
}

const partyText = (party: { readonly code: string; readonly legalName: string; readonly tradeName: string | null }) =>
  party.tradeName === null ? `${party.code} — ${party.legalName}` : `${party.code} — ${party.legalName} (${party.tradeName})`

const isListRead = (response: Response) =>
  response.request().method() === 'GET' && new URL(response.url()).pathname === DRAFTS_PATH
const isDetailRead = (response: Response) =>
  response.request().method() === 'GET' && new URL(response.url()).pathname === DETAIL_PATH

/** The UI read sent exactly the selector, and the answer acknowledges exactly it and binds the display to the record. */
async function expectOptedIn(response: Response, kind: 'list' | 'detail'): Promise<PartyDisplay> {
  expect(response.status()).toBe(200)
  expect(await response.request().headerValue(SALES_READ_CONTRACT_HEADER), `the ${kind} read sends the Sales selector`).toBe(SALES_READ_CONTRACT)
  expect(await response.headerValue(SALES_READ_CONTRACT_HEADER), `the ${kind} answer acknowledges the contract`).toBe(SALES_READ_CONTRACT)
  const body = (await response.json()) as {
    items?: { id: string; partyId: string; partyDisplay: PartyDisplay }[]
    draft?: { id: string; partyId: string }
    partyDisplay?: PartyDisplay
  }
  if (kind === 'list') {
    const row = body.items?.find((item) => item.id === deliveryDraftId)
    expect(row, 'the list holds the delivery draft').toBeDefined()
    expect(row!.partyId.toLowerCase()).toBe(realPartyId.toLowerCase())
    expect(row!.partyDisplay.partyId.toLowerCase()).toBe(row!.partyId.toLowerCase())
    for (const item of body.items ?? []) expect(item.partyDisplay.partyId.toLowerCase()).toBe(item.partyId.toLowerCase())
    return row!.partyDisplay
  }
  expect(body.draft?.id).toBe(deliveryDraftId)
  expect(body.partyDisplay?.partyId.toLowerCase()).toBe(realPartyId.toLowerCase())
  expect(Object.keys(body.partyDisplay ?? {}).sort()).toEqual(['code', 'legalName', 'partyId', 'tradeName'])
  return body.partyDisplay!
}

/** Every browser request to the Cari API, recorded from the moment the page is created. */
function recordCariRequests(page: Page): readonly string[] {
  const seen: string[] = []
  page.on('request', (request: Request) => {
    const path = new URL(request.url()).pathname
    if (path.startsWith('/api/v1/counterparties')) seen.push(`${request.method()} ${path}`)
  })
  return seen
}

test.describe.configure({ mode: 'serial' })

test('S04-PD1 a direct GET without the selector stays the legacy contract', async ({ page }) => {
  const headers = { [DEVELOPMENT_USER_HEADER]: readerActor, 'X-Company-Id': companyId }
  const detail = await page.request.get(`${env.apiBaseUrl}${DETAIL_PATH}`, { headers })
  expect(detail.status()).toBe(200)
  expect(detail.headers()[SALES_READ_CONTRACT_HEADER]).toBeUndefined()
  const detailBody = (await detail.json()) as Record<string, unknown> & { draft: Record<string, unknown> }
  expect(Object.hasOwn(detailBody, 'partyDisplay')).toBe(false)
  expect(Object.hasOwn(detailBody.draft, 'partyDisplay')).toBe(false)

  const list = await page.request.get(`${env.apiBaseUrl}${DRAFTS_PATH}?pageSize=100`, { headers })
  expect(list.status()).toBe(200)
  expect(list.headers()[SALES_READ_CONTRACT_HEADER]).toBeUndefined()
  const listBody = (await list.json()) as { items: Record<string, unknown>[] }
  expect(listBody.items.length).toBeGreaterThan(0)
  expect(listBody.items.some((item) => Object.hasOwn(item, 'partyDisplay'))).toBe(false)
})

test('S04-PD2 the reader sees the genuine record-bound Cari on the list and the detail, without Cari authority', async ({ page }) => {
  const owner = await readOwnerParty(page)
  const cariRequests = recordCariRequests(page)
  await signInAs(page, readerActor)

  const listAnswer = page.waitForResponse(isListRead)
  await page.goto('/satis/siparisler')
  const listDisplay = await expectOptedIn(await listAnswer, 'list')
  expect({ code: listDisplay.code, legalName: listDisplay.legalName, tradeName: listDisplay.tradeName }).toEqual(owner)
  const expected = partyText(owner)
  const listCell = page.getByTestId(`sales-row-party-${deliveryDraftId}`)
  await expect(listCell).toHaveText(expected)
  await expect(listCell.getByRole('link')).toHaveCount(0)

  const detailAnswer = page.waitForResponse(isDetailRead)
  await page.locator('tr', { has: listCell }).getByRole('link').first().click()
  const detailDisplay = await expectOptedIn(await detailAnswer, 'detail')
  expect(detailDisplay).toEqual(listDisplay)
  const detailParty = page.getByTestId('sales-draft-party')
  await expect(detailParty).toHaveText(expected)
  await expect(detailParty.getByRole('link')).toHaveCount(0)
  await expect(page.getByText(realPartyId, { exact: true })).toHaveCount(0)

  // Back to the list and reopen: each entry reads afresh through the same contract and shows the same Cari.
  const listAgain = page.waitForResponse(isListRead)
  await page.goBack()
  await expectOptedIn(await listAgain, 'list')
  await expect(page.getByTestId(`sales-row-party-${deliveryDraftId}`)).toHaveText(expected)
  const detailAgain = page.waitForResponse(isDetailRead)
  await page.goForward()
  await expectOptedIn(await detailAgain, 'detail')
  await expect(page.getByTestId('sales-draft-party')).toHaveText(expected)

  expect(cariRequests, 'the reader\'s browser makes no Cari API request for the display').toEqual([])
})

test('S04-PD3 the preparer\'s Cari link follows the published Cari detail capability', async ({ page }) => {
  await signInAs(page, preparerActor)
  const snapshot = page.waitForResponse((response) => new URL(response.url()).pathname === '/api/v1/shell/capabilities')
  const detailAnswer = page.waitForResponse(isDetailRead)
  await page.goto(`/satis/siparisler/${deliveryDraftId}`)
  const capabilities = ((await (await snapshot).json()) as { capabilities: string[] }).capabilities
  const display = await expectOptedIn(await detailAnswer, 'detail')
  const party = page.getByTestId('sales-draft-party')
  await expect(party).toHaveText(partyText(display))
  const published = capabilities.includes(CARI_DETAIL_CAPABILITY)
  test.info().annotations.push({ type: 'cari-detail-capability', description: published ? 'published' : 'not published' })
  if (published) {
    await expect(party.getByRole('link', { name: partyText(display) })).toHaveAttribute('href', `/cari/${display.partyId}`)
  } else {
    await expect(party.getByRole('link')).toHaveCount(0)
  }
})
