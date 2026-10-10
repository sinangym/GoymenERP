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
const { preparerActor, readerActor, companyId, deliveryDraftId, scheduleDraftId, realPartyId } = env.seed
const DEVELOPMENT_USER_HEADER = 'X-Development-User'
const SALES_READ_CONTRACT_HEADER = 'x-erp-sales-read-contract'
const SALES_READ_CONTRACT = 'record-party-display-v1'
const COUNTERPARTY_CONTRACT_HEADER = 'X-ERP-Counterparty-Contract'
const COUNTERPARTY_CONTRACT_VERSION = 'optional-trade-roles-v1'
const DRAFTS_PATH = '/api/v1/sales/drafts'
const DETAIL_PATH = `${DRAFTS_PATH}/${deliveryDraftId}`
const CARI_DETAIL_CAPABILITY = 'cari.parties.detail'
const LONG_LEGAL_NAME =
  'Teslimat müşterisi KARADENİZ ÇELİK DÖKÜM MAKİNA İMALAT SANAYİ VE DIŞ TİCARET ANONİM ŞİRKETİ GEBZE ÜRETİM VE LOJİSTİK TESİSLERİ'

interface PartyDisplay {
  readonly partyId: string
  readonly code: string
  readonly legalName: string
  readonly tradeName: string | null
}

interface DetailLine {
  readonly lineNo: number
  readonly requestedDeliveryDate: string | null
  readonly priceDisclosure: 'Visible' | 'Withheld'
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
const isDetailReadFor = (response: Response, draftId: string) =>
  response.request().method() === 'GET' && new URL(response.url()).pathname === `${DRAFTS_PATH}/${draftId}`

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

test('S04-GOYDS-01 Sipariş 360 keeps long Cari, due date and withheld price distinct at every accepted width', async ({ page }) => {
  await signInAs(page, readerActor)
  await page.setViewportSize({ width: 1440, height: 900 })
  const detailAnswer = page.waitForResponse(isDetailRead)
  await page.goto(`/satis/siparisler/${deliveryDraftId}`)
  const response = await detailAnswer
  expect(response.status()).toBe(200)
  expect(await response.headerValue(SALES_READ_CONTRACT_HEADER)).toBe(SALES_READ_CONTRACT)
  const detail = await response.json() as { readonly partyDisplay: PartyDisplay; readonly lines: readonly DetailLine[] }
  expect(detail.partyDisplay.legalName).toContain(LONG_LEGAL_NAME)
  expect(detail.lines.length).toBeGreaterThan(0)
  const line = detail.lines[0]
  expect(line.requestedDeliveryDate, 'the untouched reference draft has no invented due date').toBeNull()
  expect(line.priceDisclosure, 'the read-only actor receives no commercial value').toBe('Withheld')

  const party = page.getByTestId('sales-draft-party')
  const facts = page.getByTestId(`sales-line-${line.lineNo}-facts`)
  const due = facts.locator('[data-layout-field="delivery-date"]')
  const price = facts.locator('[data-layout-field="price"]')
  await expect(page.getByTestId('sales-order-summary')).toBeVisible()
  await expect(page.getByTestId('sales-order-status-facts')).toBeVisible()
  await expect(party).toHaveText(partyText(detail.partyDisplay))
  await expect(due).toContainText('Bilinmiyor')
  await expect(price).toContainText('Fiyat bu yetkiyle görüntülenmez')

  const expectStableLayout = async (width: number, height: number) => {
    await page.setViewportSize({ width, height })
    await expect(party).toBeVisible()
    await expect(due).toBeVisible()
    await expect(price).toBeVisible()
    const geometry = await page.evaluate(({ lineNo }) => {
      const dueElement = document.querySelector(`[data-testid="sales-line-${lineNo}-facts"] [data-layout-field="delivery-date"]`)
      const priceElement = document.querySelector(`[data-testid="sales-line-${lineNo}-facts"] [data-layout-field="price"]`)
      if (!(dueElement instanceof HTMLElement) || !(priceElement instanceof HTMLElement)) return null
      const dueRect = dueElement.getBoundingClientRect()
      const priceRect = priceElement.getBoundingClientRect()
      const overlaps = dueRect.left < priceRect.right && dueRect.right > priceRect.left
        && dueRect.top < priceRect.bottom && dueRect.bottom > priceRect.top
      return {
        pageFits: document.documentElement.scrollWidth <= document.documentElement.clientWidth,
        dueFits: dueRect.left >= 0 && dueRect.right <= document.documentElement.clientWidth,
        priceFits: priceRect.left >= 0 && priceRect.right <= document.documentElement.clientWidth,
        overlaps,
      }
    }, { lineNo: line.lineNo })
    expect(geometry).toEqual({ pageFits: true, dueFits: true, priceFits: true, overlaps: false })
  }

  await expectStableLayout(1440, 900)
  // A 1366-wide host with its real 350px right panel open leaves this exact page viewport.
  await expectStableLayout(1016, 768)
  for (const width of [390, 320]) {
    await expectStableLayout(width, 844)
    await page.getByRole('button', { name: 'Menü', exact: true }).click()
    await expect(page.getByRole('dialog', { name: 'Menü' })).toBeVisible()
    await page.keyboard.press('Escape')
    await expect(page.getByRole('dialog', { name: 'Menü' })).toHaveCount(0)
  }

  // The earlier DEC-SAL-003 journey leaves this separate real order Approved with a recorded line due date.
  // It is the source-backed record for due-date and delivery-progress layout; the untouched reference draft above is not.
  await page.setViewportSize({ width: 1440, height: 900 })
  const scheduledAnswer = page.waitForResponse((candidate) => isDetailReadFor(candidate, scheduleDraftId))
  await page.goto(`/satis/siparisler/${scheduleDraftId}`)
  const scheduledResponse = await scheduledAnswer
  expect(scheduledResponse.status()).toBe(200)
  expect(await scheduledResponse.headerValue(SALES_READ_CONTRACT_HEADER)).toBe(SALES_READ_CONTRACT)
  const scheduled = await scheduledResponse.json() as {
    readonly draft: { readonly approvalState: string }
    readonly lines: readonly DetailLine[]
  }
  expect(scheduled.draft.approvalState).toBe('Approved')
  expect(scheduled.lines).toHaveLength(1)
  expect(scheduled.lines[0].requestedDeliveryDate).toBe('2026-10-20')
  expect(scheduled.lines[0].priceDisclosure).toBe('Withheld')
  const scheduledFacts = page.getByTestId(`sales-line-${scheduled.lines[0].lineNo}-facts`)
  await expect(scheduledFacts.locator('[data-layout-field="delivery-date"]')).toContainText('20.10.2026')
  await expect(scheduledFacts.locator('[data-layout-field="price"]')).toContainText('Fiyat bu yetkiyle görüntülenmez')
  await expect(page.getByTestId('sales-delivery-progress')).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
})
