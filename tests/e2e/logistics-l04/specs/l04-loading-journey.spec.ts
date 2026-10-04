/**
 * Logistics l04 — LOG-4b user-flow acceptance against the REAL stack (`browser:l04:ci`).
 *
 * Real Chromium, the real web build of the exact head, the real API and SQL Server. NOTHING IS MOCKED: the
 * only things the suite adds to the browser are the actor's identity header (Playwright's extra headers,
 * the s03/s04 way; the API still authorizes the header itself) and, in ONE case, the withholding of a real
 * answer's delivery (the mfg unknown-outcome pattern: the server records, the page learns nothing).
 *
 * FIXTURE SCOPE (the stage-2 seed, tests/GoymenERP.BrowserSuiteFixtureTool/Suites/L04, and nothing else):
 *   - ONE Cari customer opened through the real Counterparty API by the bootstrap admin;
 *   - ONE Sales draft written by the gated Sales fixture writer with the REAL customer ids and the REAL
 *     Settings kilogram unit, then SUBMITTED and APPROVED through the real Sales API by two Sales actors;
 *   - ONE loading instruction (two lines on that approved line) created and ISSUED through the real Logistics
 *     API by the preparer, whose second line was then BLOCKED by the gated Logistics fixture writer — the only
 *     state no API produces in 4a (D2's block source arrives in LOG-5).
 * Everything below is real UI + real API; the positive path creates ANOTHER instruction from a real selection
 * of the approved line in the normal screens (by its visible label, never by a fixture id). The picker reads
 * the PRICE-FREE Logistics route GET /api/v1/logistics/loading-instructions/approved-order-lines under the
 * create grant: the preparer holds NO Sales grant (Sinan §4 #66 5738113623, deny-first) and every Sales
 * draft route answers it 403. The customer select shows the Cari NAME the read carries (`customerName`,
 * Business 5745076917); the positive path selects the customer by that visible text, never by an id.
 *
 * The cases are one serial journey, in file order:
 *   L04-01 preparer: list → new → picks the customer and the approved line in the UI, quantity text "4.5"
 *          kept verbatim; the first answer is withheld, the same snapshot is re-sent under the same key and
 *          answered as a replay; the detail is the real read; no price text on any screen
 *   L04-02 preparer issues it (Draft → Issued)
 *   L04-03 loader: a load within the remainder; a load over it (409 cap); a stale second tab (409 version,
 *          re-read); the same load id under a new key (409 duplicate)
 *   L04-04 loader on the seeded instruction: the blocked line refuses (409), the open line takes a load
 *   L04-05 loader: a definite shipment (withheld answer, reopen, replay) and a partial delivery; AT-024: the Sales
 *          order's S-04 "Teslimat durumu" read by a Sales reader shows nothing delivered before, the delivery after
 *          reopen, and the same value after the delivery is replayed under its key (never counted twice)
 *   L04-06 reader: list and detail open, no create route, a load is refused 403 and nothing is recorded;
 *          the preparer: Sales draft routes 403 while the price-free read answers the approved line with
 *          no price key anywhere;
 *          the preparer under the other company: the API answers nothing of GDC's instruction and publishes no
 *          Logistics screen there
 *   L04-07 AT-053 loader: the manual weighbridge record typed in the detail (two weighings + direction); reopening shows
 *          the server's net and the signed loaded-vs-weighed difference as facts (no tolerance verdict, OPEN-004) on
 *          the instruction whose delivery continues below; the same key replays with one row; a second record is 409;
 *          the reader is 403; one audit on the Web channel; no Inventory movement
 *   L04-08 AT-025 operations actor: "Operasyon işleri" — the sales section shows the seeded approved sales line and never the
 *          seeded purchase order, the purchase section the purchase line (by the supplier's Cari name) and never the sales
 *          order; each row opens its OWNER screen (P-03-DETAIL, S-04); back keeps the section and the search; reopen shows
 *          the same row; no price text or key; the reader holds neither section (routes closed, reads 403); nothing under
 *          the other company
 *   L04-09 AT-055 loader: a dispatch-note DRAFT prepared in the detail from L04-05's definite shipment; reopening shows it with
 *          the shipped quantity (2) as its one line and the shipment no longer pickable; the same key replays with one draft;
 *          a second draft for the same shipment is 409 logistics_shipment_already_dispatched; the reader is 403; one audit on
 *          the Web channel; no Inventory movement; the Sales reader's S-04 lists the draft (state Taslak) and its link opens
 *          the instruction at the draft; then the loader approves it (series number IRS-nnnnnn), records its e-İrsaliye number,
 *          cancels it with a reason (the reader is 403), prepares a NEW draft from the released shipment, and a reload shows
 *          both notes with their states and the cancelled note's numbers
 */

import { existsSync, readFileSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { expect, test, type Page, type Response } from '@playwright/test'
import sql from 'mssql'
import { readEnvironment } from '../src/env.js'

const env = readEnvironment()
// The seed exports GUIDs and actor ids only (validate_browser_acceptance.seed_export); the customer's Cari name is the
// seed's deterministic label (L04BrowserSeed.CustomerName), which the seed verified the price-free read answers.
const customerName = 'SENTETIK browser-suite:l04 Yükleme müşterisi'
const {
  preparerActor,
  loaderActor,
  readerActor,
  companyId,
  otherCompanyId,
  draftId,
  approvedLineId,
  instructionId,
  blockedLineId,
  openLineId,
  shipmentAllocationId,
  shipmentLabelId,
  shipmentReceiptAcceptanceId,
  shipmentReceiptMovementId,
} = env.seed
const INSTRUCTIONS_PATH = '/api/v1/logistics/loading-instructions'
const APPROVED_LINES_PATH = `${INSTRUCTIONS_PATH}/approved-order-lines`
const SALES_DRAFTS_PATH = '/api/v1/sales/drafts'
const PRICE_KEYS = ['unitPrice', 'discountPercent', 'paymentTerms', 'priceUnitId', 'currencyId']
const LIST_ROUTE = '/lojistik/yukleme-talimatlari'
const CREATE_ROUTE = `${LIST_ROUTE}/yeni`
const DEVELOPMENT_USER_HEADER = 'X-Development-User'
const DOCUMENT_NO = 'SENTETIK browser-suite:l04 SO-0001'
// AT-024: the Sales side of the same order. The seed's Sales approver (infra/browser-suites/l04.override.yml,
// BROWSER_L04_SALES_APPROVER: sales.orders.approve + sales.drafts.read, no create, no price) reads S-04; the actor id is
// the override's deterministic subject (seed_export carries only the three Logistics actors), the customerName way.
const SALES_READER_ACTOR = 'l04-sales-approver'
const SALES_DETAIL_ROUTE = `/satis/siparisler/${draftId}`
// The seeded approved draft has ONE line of "6" kg (L04BrowserSeed); only L04-05 ships (2) and delivers (1.5) on it.
const ORDERED = '6'
const PRODUCT_NAME = 'SENTETIK browser-suite:purchase-to-delivery candidate'
// Business 5743412166 (Sevkiyatçı rule): no price or finance word on these screens, in any language the app uses.
const PRICE_WORDS = ['₺', 'TRY', 'fiyat', 'Fiyat', 'price', 'Price', 'unitPrice', 'tutar', 'Tutar', 'iskonto', 'İskonto', 'para birimi']

interface LineReading {
  readonly lineId: string
  readonly lineNo: number
  readonly instructed: string
  readonly loaded: string
  readonly remainingToLoad: string
  readonly isBlocked: boolean
}

interface InstructionReading {
  readonly instruction: { readonly id: string; readonly status: string; readonly version: string; readonly lineCount: number }
  readonly lines: readonly LineReading[]
  readonly shares: readonly {
    readonly shareId: string
    readonly lineId: string
    readonly kind: string
    readonly quantity: string
    readonly sourceShipmentId: string | null
  }[]
}

/** The instruction the browser created in L04-01; every later case runs on it. */
let createdId = ''

async function signInAs(page: Page, actor: string): Promise<void> {
  await page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: actor })
}

/**
 * The stable mark of the instruction L04-01 created: its OWN id, written by L04-01 the moment the real API answered
 * it and read back by a later case after Playwright restarted the worker (module state is gone then, the file is
 * not). The file is named after the seeded instruction, i.e. after this disposable database, so a file left by
 * another run cannot be mistaken for this run's; and a wrong id fails loudly at the detail read (200 + the same id),
 * never silently. This replaces "the row that is not the seeded one" (ORG INFO-9 on #358): that recovery was an
 * assumption about the list holding exactly two rows, and a third instruction would have made it pick the wrong row.
 */
const CREATED_ID_FILE = join(tmpdir(), `goymen-l04-created-${instructionId}.id`)

function rememberCreatedId(id: string): void {
  writeFileSync(CREATED_ID_FILE, id, 'utf8')
}

/**
 * A MISSING file is a loud failure naming the path and the case that should have written it — L04-01 aborting
 * before its write, another tmpdir, a rename. There is deliberately NO other way of finding the instruction here:
 * a fallback to the list would quietly restore the two-row assumption this helper exists to remove.
 */
function recallCreatedId(): string {
  expect(existsSync(CREATED_ID_FILE), `L04-01 should have written the created instruction's id to ${CREATED_ID_FILE}; the file is missing`).toBe(true)
  const id = readFileSync(CREATED_ID_FILE, 'utf8').trim()
  expect(id, `the id file ${CREATED_ID_FILE} written by L04-01 is empty`).not.toBe('')
  return id
}

/** The instruction as the real API answers it to `actor` under `company`. */
async function readInstruction(page: Page, actor: string, id: string, company = companyId): Promise<InstructionReading> {
  const response = await page.request.get(`${env.apiBaseUrl}${INSTRUCTIONS_PATH}/${id}`, {
    headers: { [DEVELOPMENT_USER_HEADER]: actor, 'X-Company-Id': company },
  })
  expect(response.status(), `the real detail route answers ${actor}`).toBe(200)
  const reading = (await response.json()) as InstructionReading
  expect(reading.instruction.id).toBe(id)
  return reading
}

/** Collects the browser's own answers on one command route (POST) whose path matches `pattern`. */
function watchCommand(page: Page, pattern: RegExp): Response[] {
  const answers: Response[] = []
  page.on('response', (response) => {
    const request = response.request()
    if (request.method() === 'POST' && pattern.test(new URL(response.url()).pathname)) answers.push(response)
  })
  return answers
}

/** An instruction id a case needs; '' means an earlier case did not record it (a failure, or a worker restart after one). */
function requireInstructionId(id: string, where: string): string {
  if (id === '') throw new Error(`${where}: no instruction id — L04-01 did not record createdId in this worker (an earlier failure or a worker restart); refusing instead of navigating to the list or waiting on a row that cannot exist`)
  return id
}

/** The list row of one instruction; refuses an empty id at once. */
function instructionRow(page: Page, id: string) {
  return page.getByTestId(`logistics-row-${requireInstructionId(id, 'instructionRow')}`)
}

async function openDetail(page: Page, id: string): Promise<void> {
  await page.goto(`${LIST_ROUTE}/${requireInstructionId(id, 'openDetail')}`)
  await expect(page.getByRole('heading', { level: 1, name: /Yükleme talimatı/ })).toBeVisible({ timeout: 30_000 })
  await expect(page.getByTestId('logistics-instruction-status')).toBeVisible()
}

const status = (page: Page) => page.getByTestId('logistics-instruction-status')
const version = (page: Page) => page.getByTestId('logistics-instruction-version')
const issueNotice = (page: Page) => page.getByTestId('logistics-issue-notice')
const loadNotice = (page: Page) => page.getByTestId('logistics-load-notice')
const createNotice = (page: Page) => page.getByTestId('logistics-create-notice')
const loadForm = (page: Page) => page.getByTestId('logistics-load')
const issueButton = (page: Page) => page.getByTestId('logistics-issue-button')

/** Every key of a JSON value, at any depth. */
function keysOf(value: unknown, into: Set<string> = new Set()): Set<string> {
  if (Array.isArray(value)) for (const item of value) keysOf(item, into)
  else if (typeof value === 'object' && value !== null) {
    for (const [key, inner] of Object.entries(value)) {
      into.add(key)
      keysOf(inner, into)
    }
  }
  return into
}

async function expectNoPriceText(page: Page): Promise<void> {
  const text = await page.locator('main').innerText()
  for (const word of PRICE_WORDS) expect(text, `no price or finance word on the screen: "${word}"`).not.toContain(word)
}

/**
 * AT-024: opens the Sales order's S-04 detail as the Sales reader in its OWN page and returns the "Teslimat durumu" row of
 * its one line (line 1) once the real delivery-progress read answered 200. Every call is a fresh route entry (the section
 * reads afresh on every open); `reload` additionally reloads the page and reads again — close → reopen.
 */
async function openSalesProgress(sales: Page, reload = false): Promise<void> {
  const answered = sales.waitForResponse((response) => response.request().method() === 'GET'
    && new URL(response.url()).pathname === `${SALES_DRAFTS_PATH}/${draftId}/delivery-progress`, { timeout: 30_000 })
  if (reload) await sales.reload()
  else await sales.goto(SALES_DETAIL_ROUTE)
  await expect(sales.getByRole('heading', { level: 1, name: DOCUMENT_NO })).toBeVisible({ timeout: 30_000 })
  expect((await answered).status(), 'the real delivery-progress read answers the Sales reader').toBe(200)
  await expect(sales.getByTestId('sales-delivery-progress-line-1')).toBeVisible({ timeout: 30_000 })
}

/** The exact text of the five quantities of line 1 in S-04 "Teslimat durumu" (never parsed into numbers). */
async function expectSalesProgress(sales: Page, expected: {
  readonly shipped: string; readonly delivered: string; readonly remaining: string; readonly excess: string
}): Promise<void> {
  const row = sales.getByTestId('sales-delivery-progress-line-1')
  await expect(row.locator('td').first()).toHaveText(ORDERED)
  await expect(sales.getByTestId('sales-delivery-progress-line-1-shipped')).toHaveText(expected.shipped)
  await expect(sales.getByTestId('sales-delivery-progress-line-1-delivered')).toHaveText(expected.delivered)
  await expect(sales.getByTestId('sales-delivery-progress-line-1-remaining')).toHaveText(expected.remaining)
  await expect(sales.getByTestId('sales-delivery-progress-line-1-excess')).toHaveText(expected.excess)
}

/** Fills and submits one load share on the line `lineId` through the ONE load form; returns the browser's answers on the loads route. */
async function recordLoad(page: Page, lineId: string, quantity: string, measurement = 'Tartıldı'): Promise<Response[]> {
  const answers = watchCommand(page, /\/api\/v1\/logistics\/loading-instructions\/[^/]+\/lines\/[^/]+\/loads$/)
  const form = loadForm(page)
  await form.getByLabel('Satır').selectOption(lineId)
  await form.getByLabel('Yüklenen miktar').fill(quantity)
  await form.getByLabel('Ölçüm').selectOption({ label: measurement })
  await form.getByTestId('logistics-load-button').click()
  return answers
}

test('L04-01 the preparer creates an instruction from a real selection of the approved line; a withheld answer is re-sent under the same key as a replay; no price on any screen', async ({ page }) => {
  await signInAs(page, preparerActor)

  await page.goto(LIST_ROUTE)
  await expect(page.getByRole('heading', { level: 1, name: 'Yükleme talimatları' })).toBeVisible({ timeout: 30_000 })
  await expect(instructionRow(page, instructionId)).toBeVisible()
  await expectNoPriceText(page)
  // The shell's nav entry (the central mount) and the list screen's own link share the name: the journey clicks the
  // screen's, inside the main region (first l04 run 35478322479: strict-mode violation, two matches).
  await page.getByRole('main').getByRole('link', { name: 'Yeni yükleme talimatı' }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Yeni yükleme talimatı' })).toBeVisible({ timeout: 30_000 })

  // The customer and the line come from the price-free Logistics read of this company's approved lines, picked by their
  // visible labels: the customer by its Cari NAME (Business 5745076917), the line by document number + product name.
  const customer = page.getByLabel('Müşteri (onaylı siparişlerden)')
  await expect(customer.locator('option', { hasText: customerName })).toHaveCount(1, { timeout: 30_000 })
  await customer.selectOption({ label: customerName })
  const lineRow = page.getByTestId(`logistics-pick-${DOCUMENT_NO}-1`)
  await expect(lineRow).toBeVisible()
  await expect(lineRow).toContainText(PRODUCT_NAME)
  await lineRow.getByRole('checkbox', { name: `${DOCUMENT_NO} / 1 / ${PRODUCT_NAME}` }).check()
  await lineRow.getByRole('textbox', { name: /Talimat miktarı/ }).fill('4.5')
  await expectNoPriceText(page)

  // Withhold the delivery of the first real answer: the server creates the instruction, the page learns nothing.
  let withheld: number | null = null
  await page.route(`**${INSTRUCTIONS_PATH}`, async (route) => {
    if (route.request().method() !== 'POST' || withheld !== null) return route.fallback()
    const response = await route.fetch({ headers: { ...route.request().headers(), 'x-development-user': preparerActor } })
    withheld = response.status()
    await route.abort('failed')
  })
  const creates = watchCommand(page, new RegExp(`^${INSTRUCTIONS_PATH}$`))
  await page.getByTestId('logistics-create-button').click()
  await expect(createNotice(page)).toContainText('sonucu alınamadı')
  expect(withheld, 'the withheld first send was recorded by the real API').toBe(201)

  // The same snapshot, the same key: the real API answers the replay; the screen says so.
  const resend = page.waitForResponse((response) => response.request().method() === 'POST' && new URL(response.url()).pathname === INSTRUCTIONS_PATH)
  await page.getByRole('button', { name: 'Aynı işlemi tekrar dene' }).click()
  const replay = await resend
  expect(replay.status()).toBe(201)
  expect(replay.headers()['idempotency-replayed']).toBe('true')
  await expect(page.getByTestId('logistics-created')).toBeVisible()
  await expect(page.getByTestId('logistics-created-replay')).toContainText('daha önce kaydedilmişti')
  const body = (await replay.json()) as { detail: InstructionReading; isReplay: boolean }
  expect(body.isReplay).toBe(true)
  createdId = body.detail.instruction.id
  rememberCreatedId(createdId)
  expect(creates.filter((answer) => answer.headers()['idempotency-replayed'] !== 'true').length, 'the page received no non-replay create answer').toBe(0)

  // Exactly ONE instruction was created: the real detail, with the quantity text kept verbatim.
  const created = await readInstruction(page, preparerActor, createdId)
  expect([created.instruction.status, created.instruction.version, created.instruction.lineCount]).toEqual(['Draft', '2', 1])
  expect([created.lines[0].instructed, created.lines[0].remainingToLoad, created.lines[0].loaded]).toEqual(['4.5', '4.5', '0'])

  await expect(page.getByTestId('logistics-created-id')).toHaveText(createdId)
  await page.getByRole('button', { name: 'Talimatı aç' }).click()
  await expect(status(page)).toHaveText('Taslak')
  await expect(version(page)).toHaveText('2')
  await expect(page.getByTestId('logistics-line-1')).toContainText('4.5')
  await expectNoPriceText(page)

  // Reopen by URL: the real read, not a rendered memory.
  await openDetail(page, createdId)
  await expect(status(page)).toHaveText('Taslak')
  await expect(version(page)).toHaveText('2')
  await expect(page.getByTestId('logistics-line-1')).toContainText('4.5')
})

test('L04-02 the preparer issues the instruction (Draft → Issued)', async ({ page }) => {
  await signInAs(page, preparerActor)
  const issues = watchCommand(page, /\/api\/v1\/logistics\/loading-instructions\/[^/]+\/issue$/)
  await openDetail(page, createdId)
  await expect(status(page)).toHaveText('Taslak')
  await expect(page.getByTestId('logistics-load-draft-only')).toBeVisible()
  await issueButton(page).click()
  await expect(issueNotice(page)).toHaveText('İşlem kaydedildi.')
  await expect(status(page)).toHaveText('Yayımlandı')
  await expect(version(page)).toHaveText('3')
  expect(issues.map((answer) => answer.status())).toEqual([200])
  await expect(issueButton(page)).toHaveCount(0)

  const issued = await readInstruction(page, preparerActor, createdId)
  expect([issued.instruction.status, issued.instruction.version]).toEqual(['Issued', '3'])
})

test('L04-03 the loader records within the remainder; over it is refused; a stale tab is refused and re-reads; the same load id under a new key is a duplicate', async ({ page, context }) => {
  await signInAs(page, loaderActor)
  await openDetail(page, createdId)
  await expect(status(page)).toHaveText('Yayımlandı')
  await expect(issueButton(page)).toHaveCount(0)

  const lineId = (await readInstruction(page, loaderActor, createdId)).lines[0].lineId

  // Within the remainder: 2.5 of 4.5.
  const first = await recordLoad(page, lineId, '2.5')
  await expect(loadNotice(page)).toHaveText('İşlem kaydedildi.')
  await expect(page.getByTestId('logistics-line-1-loaded')).toHaveText('2.5')
  // The remainder is the server's own computed text (4.5 − 2.5 in the calculator's decimal arithmetic answers "2.0", not a
  // canonical "2"; the screen shows the server's text verbatim): assert the screen against the API's answer, not a literal.
  const afterFirst = await readInstruction(page, loaderActor, createdId)
  expect(Number(afterFirst.lines[0].remainingToLoad)).toBe(2)
  await expect(page.getByTestId('logistics-line-1-remaining')).toHaveText(afterFirst.lines[0].remainingToLoad)
  expect(first.map((answer) => answer.status())).toEqual([200])
  await expect(version(page)).toHaveText('4')

  // Over the remainder (3 > 2): the §13 cap, terminal 409, nothing changed.
  const over = await recordLoad(page, lineId, '3')
  await expect(loadNotice(page)).toContainText('talimat kalanını aşıyor')
  expect(over.map((answer) => answer.status())).toEqual([409])
  expect(((await over[0].json()) as { code?: unknown }).code).toBe('logistics_load_exceeds_instructed')
  await expect(page.getByTestId('logistics-line-1-loaded')).toHaveText('2.5')
  await expect(version(page)).toHaveText('4')

  // A stale second tab: tab A records 1 (version 4 → 5); tab B still holds version 4 and is refused, then re-reads.
  const stale = await context.newPage()
  await signInAs(stale, loaderActor)
  await openDetail(stale, createdId)
  await expect(stale.getByTestId('logistics-instruction-version')).toHaveText('4')
  const tabA = await recordLoad(page, lineId, '1')
  await expect(loadNotice(page)).toHaveText('İşlem kaydedildi.')
  await expect(version(page)).toHaveText('5')
  expect(tabA.map((answer) => answer.status())).toEqual([200])

  const tabB = await recordLoad(stale, lineId, '0.5')
  await expect(loadNotice(stale)).toContainText('Talimat siz okuduktan sonra değişti')
  expect(tabB.map((answer) => answer.status())).toEqual([409])
  expect(((await tabB[0].json()) as { code?: unknown }).code).toBe('version_conflict')
  await expect(stale.getByTestId('logistics-instruction-version')).toHaveText('5')
  await expect(stale.getByTestId('logistics-line-1-loaded')).toHaveText('3.5')
  await stale.close()

  const after = await readInstruction(page, loaderActor, createdId)
  // The quantities are the server's own decimal text at the inputs' scale (the same rule as the remainder above: 4.5 − 3.5
  // answers "1.0", never a canonical "1"); compare them by value, never by a hand-written literal. Version and count stay exact.
  expect([after.instruction.version, after.shares.length]).toEqual(['5', 2])
  expect(Number(after.lines[0].loaded)).toBe(3.5)
  expect(Number(after.lines[0].remainingToLoad)).toBe(1)

  // The same load id under a NEW key is the same physical load twice: refused as a duplicate share, nothing recorded.
  const duplicate = await page.request.post(`${env.apiBaseUrl}${INSTRUCTIONS_PATH}/${createdId}/lines/${after.lines[0].lineId}/loads`, {
    headers: {
      [DEVELOPMENT_USER_HEADER]: loaderActor,
      'X-Company-Id': companyId,
      'X-Correlation-Id': `browser-suite-l04-duplicate-${Date.now()}`,
      'Idempotency-Key': `browser-suite:l04:duplicate:${Date.now()}`,
      'Content-Type': 'application/json',
    },
    data: { expectedVersion: '5', loadId: after.shares[1].shareId, quantity: '0.5', measurement: 'Measured' },
  })
  expect(duplicate.status()).toBe(409)
  expect(((await duplicate.json()) as { code?: unknown }).code).toBe('logistics_duplicate_share_id')
  const unchanged = await readInstruction(page, loaderActor, createdId)
  expect([unchanged.instruction.version, unchanged.shares.length]).toEqual(['5', 2])
})

test('L04-04 on the seeded instruction the blocked line refuses a load and the open line takes one', async ({ page }) => {
  await signInAs(page, loaderActor)
  const before = await readInstruction(page, loaderActor, instructionId)
  const blocked = before.lines.find((line) => line.lineId === blockedLineId) as LineReading
  const open = before.lines.find((line) => line.lineId === openLineId) as LineReading
  expect([before.instruction.status, blocked.isBlocked, open.isBlocked]).toEqual(['Issued', true, false])

  await openDetail(page, instructionId)
  await expect(page.getByTestId(`logistics-line-${blocked.lineNo}-blocked`)).toBeVisible()
  await expect(page.getByTestId(`logistics-line-${open.lineNo}-blocked`)).toHaveCount(0)
  // The blocked line cannot even be chosen in the form (fail-closed on the screen, D2)…
  await expect(loadForm(page).getByLabel('Satır').locator(`option[value="${blockedLineId}"]`)).toBeDisabled()
  // …and the real route refuses it as the terminal logistics_line_blocked (the server's own rule, same actor, own key).
  const refused = await page.request.post(`${env.apiBaseUrl}${INSTRUCTIONS_PATH}/${instructionId}/lines/${blockedLineId}/loads`, {
    headers: {
      [DEVELOPMENT_USER_HEADER]: loaderActor,
      'X-Company-Id': companyId,
      'X-Correlation-Id': `browser-suite-l04-blocked-${Date.now()}`,
      'Idempotency-Key': `browser-suite:l04:blocked:${Date.now()}`,
      'Content-Type': 'application/json',
    },
    data: { expectedVersion: before.instruction.version, loadId: crypto.randomUUID(), quantity: '1', measurement: 'Measured' },
  })
  expect(refused.status()).toBe(409)
  expect(((await refused.json()) as { code?: unknown }).code).toBe('logistics_line_blocked')

  const recorded = await recordLoad(page, openLineId, '1')
  await expect(loadNotice(page)).toHaveText('İşlem kaydedildi.')
  expect(recorded.map((answer) => answer.status())).toEqual([200])
  await expect(page.getByTestId(`logistics-line-${open.lineNo}-loaded`)).toHaveText('1')

  const after = await readInstruction(page, loaderActor, instructionId)
  expect(after.lines.find((line) => line.lineId === blockedLineId)?.loaded).toBe('0')
  expect(after.lines.find((line) => line.lineId === openLineId)?.loaded).toBe('1')
})

test('L04-05 the purchase-receipt label crosses allocation, one stock exit, shipment and delivery without a second Inventory effect (AT-024)', async ({ page }) => {
  await signInAs(page, loaderActor)
  if (createdId === '') createdId = recallCreatedId()
  await openDetail(page, createdId)
  const before = await readInstruction(page, loaderActor, createdId)
  const line = before.lines[0]
  expect(Number(line.loaded)).toBe(3.5)

  const allocationPath = `/api/v1/inventory/physical-allocations?salesOrderId=${draftId}&salesOrderLineId=${approvedLineId}&limit=100`
  const allocationBefore = await page.request.get(`${env.apiBaseUrl}${allocationPath}`, {
    headers: { [DEVELOPMENT_USER_HEADER]: loaderActor, 'X-Company-Id': companyId },
  })
  expect(allocationBefore.status()).toBe(200)
  expect(await allocationBefore.json()).toMatchObject({ items: [{ allocation: {
    allocationId: shipmentAllocationId, labelId: shipmentLabelId, status: 'Active', version: '1', remainingKg: '2',
  } }] })

  const pool = await new sql.ConnectionPool(env.sqlConnection).connect()
  try {
    const effects = async (movementId?: string) => {
      const result = await pool.request()
        .input('companyId', sql.UniqueIdentifier, companyId)
        .input('labelId', sql.UniqueIdentifier, shipmentLabelId)
        .input('allocationId', sql.UniqueIdentifier, shipmentAllocationId)
        .input('acceptanceId', sql.UniqueIdentifier, shipmentReceiptAcceptanceId)
        .input('receiptMovementId', sql.UniqueIdentifier, shipmentReceiptMovementId)
        .input('movementId', sql.UniqueIdentifier, movementId ?? '00000000-0000-0000-0000-000000000000')
        .query(`
          SELECT
            (SELECT COUNT(*) FROM purchasing.PurchaseAcceptanceShares
               WHERE CompanyId=@companyId AND AcceptanceId=@acceptanceId) AS receiptAcceptance,
            (SELECT COUNT(*) FROM inventory.Movements
               WHERE CompanyId=@companyId AND Id=@receiptMovementId
                 AND SourceKind=N'purchasing.goods-acceptance' AND SourceId=@acceptanceId) AS receiptMovement,
            (SELECT COUNT(DISTINCT movement.Id)
               FROM inventory.Movements movement
               INNER JOIN inventory.MovementLines movementLine
                 ON movementLine.CompanyId=movement.CompanyId AND movementLine.MovementId=movement.Id
              WHERE movement.CompanyId=@companyId AND movement.SourceKind=N'inventory.opening-count'
                AND movementLine.LabelId=@labelId) AS openingMovements,
            (SELECT COUNT(*) FROM inventory.Allocations WHERE CompanyId=@companyId AND Id=@allocationId AND LabelId=@labelId) AS allocations,
            (SELECT COUNT(*) FROM inventory.Allocations WHERE CompanyId=@companyId AND Id=@allocationId
               AND Status=2 AND Version=2 AND RemainingKg=0) AS consumed,
            (SELECT COUNT(DISTINCT movement.Id)
               FROM inventory.Movements movement
               INNER JOIN inventory.MovementLines movementLine
                 ON movementLine.CompanyId=movement.CompanyId AND movementLine.MovementId=movement.Id
              WHERE movement.CompanyId=@companyId AND movementLine.LabelId=@labelId) AS movements,
            (SELECT COUNT(*) FROM inventory.Movements WHERE CompanyId=@companyId AND Id=@movementId) AS shipmentMovement`)
      return result.recordset[0] as {
        receiptAcceptance: number; receiptMovement: number; openingMovements: number
        allocations: number; consumed: number; movements: number; shipmentMovement: number
      }
    }

    const receiptOnly = {
      receiptAcceptance: 1, receiptMovement: 1, openingMovements: 0,
      allocations: 1, consumed: 0, movements: 1, shipmentMovement: 0,
    }
    expect(await effects()).toEqual(receiptOnly)

    const shipmentPath = `${INSTRUCTIONS_PATH}/${createdId}/lines/${line.lineId}/shipments`
    const attempts: { readonly key: string | null; readonly body: string | null }[] = []
    page.on('request', (request) => {
      if (request.method() === 'POST' && new URL(request.url()).pathname === shipmentPath) {
        attempts.push({ key: request.headers()['idempotency-key'] ?? null, body: request.postData() })
      }
    })
    let withheldStatus: number | null = null
    await page.route(`**${shipmentPath}`, async (route) => {
      if (withheldStatus !== null) return route.fallback()
      const response = await route.fetch({ headers: { ...route.request().headers(), 'x-development-user': loaderActor } })
      withheldStatus = response.status()
      await route.abort('failed')
    })

    const shipment = page.getByTestId('shipment-1')
    await expect(shipment).toBeVisible()
    await shipment.locator('#shipment-1-allocation').selectOption(shipmentAllocationId)
    await shipment.locator('#shipment-1-quantity').fill('2')
    await shipment.getByTestId('shipment-1-submit').click()
    await expect(page.getByTestId('shipment-1-notice')).toContainText('sonucu alınamadı')
    expect(withheldStatus).toBe(200)
    expect(attempts).toHaveLength(1)
    const shipmentBody = JSON.parse(attempts[0].body ?? '{}') as { shipmentId: string; movementId: string }

    const recorded = await readInstruction(page, loaderActor, createdId)
    const shipped = recorded.shares.find((share) => share.kind === 'Shipped' && share.shareId === shipmentBody.shipmentId)
    expect(shipped).toMatchObject({ lineId: line.lineId, quantity: '2', sourceShipmentId: null })
    const shippedEffects = {
      ...receiptOnly, consumed: 1, movements: 2, shipmentMovement: 1,
    }
    expect(await effects(shipmentBody.movementId)).toEqual(shippedEffects)

    await page.getByRole('link', { name: '← Yükleme talimatları', exact: true }).click()
    await expect(page.getByRole('heading', { level: 1, name: 'Yükleme talimatları', exact: true })).toBeVisible()
    const detailRead = page.waitForResponse((response) => response.request().method() === 'GET'
      && new URL(response.url()).pathname === `${INSTRUCTIONS_PATH}/${createdId}`)
    await instructionRow(page, createdId).getByRole('link', { name: createdId, exact: true }).click()
    expect((await detailRead).status()).toBe(200)
    const replayAnswer = page.waitForResponse((response) => response.request().method() === 'POST'
      && new URL(response.url()).pathname === shipmentPath)
    await page.getByRole('button', { name: 'Aynı işlemi tekrar dene' }).click()
    const replay = await replayAnswer
    expect(replay.status()).toBe(200)
    expect(replay.headers()['idempotency-replayed']).toBe('true')
    expect(attempts).toHaveLength(2)
    expect(attempts[1]).toEqual(attempts[0])
    expect(await effects(shipmentBody.movementId)).toEqual(shippedEffects)

    // AT-024, before the delivery: the Sales reader's S-04 shows the definite shipment and nothing delivered yet.
    const sales = await page.context().newPage()
    await signInAs(sales, SALES_READER_ACTOR)
    await openSalesProgress(sales)
    await expectSalesProgress(sales, { shipped: '2', delivered: '0', remaining: ORDERED, excess: '0' })

    await page.goto(LIST_ROUTE)
    await openDetail(page, createdId)
    const inventoryCalls: string[] = []
    page.on('request', (request) => {
      if (new URL(request.url()).pathname.startsWith('/api/v1/inventory/')) inventoryCalls.push(request.url())
    })
    const delivery = page.getByTestId('delivery-1')
    await delivery.locator('#delivery-1-source').selectOption(shipmentBody.shipmentId)
    await delivery.locator('#delivery-1-quantity').fill('1.5')
    const deliveryAnswer = page.waitForResponse((response) => response.request().method() === 'POST'
      && new URL(response.url()).pathname.endsWith(`/lines/${line.lineId}/deliveries`))
    await delivery.getByTestId('delivery-1-submit').click()
    const delivered = await deliveryAnswer
    expect(delivered.status()).toBe(200)
    expect((await delivered.request().postDataJSON()) as { sourceShipmentId: string }).toMatchObject({ sourceShipmentId: shipmentBody.shipmentId })
    expect(inventoryCalls).toEqual([])
    expect(await effects(shipmentBody.movementId)).toEqual(shippedEffects)

    await page.goto(LIST_ROUTE)
    await openDetail(page, createdId)
    const final = await readInstruction(page, loaderActor, createdId)
    expect(final.shares.find((share) => share.kind === 'Delivered')).toMatchObject({
      lineId: line.lineId, quantity: '1.5', sourceShipmentId: shipmentBody.shipmentId,
    })

    // AT-024, after the delivery: the order opened again shows it (read afresh from Logistics), and so does a reload.
    const deliveredAfter = { shipped: '2', delivered: '1.5', remaining: '4.5', excess: '0' }
    await openSalesProgress(sales)
    await expectSalesProgress(sales, deliveredAfter)
    await openSalesProgress(sales, true)
    await expectSalesProgress(sales, deliveredAfter)

    // The SAME delivery again, exactly as the browser sent it (same key, same body) through the real route: the stored
    // answer is replayed, no second share is appended, and the Sales order still shows 1.5 — never 3.
    const deliveryRequest = delivered.request()
    const deliveryKey = deliveryRequest.headers()['idempotency-key']
    expect(deliveryKey, 'the browser sent the delivery under an Idempotency-Key').toBeTruthy()
    const replayedDelivery = await page.request.post(`${env.apiBaseUrl}${new URL(delivered.url()).pathname}`, {
      headers: {
        [DEVELOPMENT_USER_HEADER]: loaderActor, 'X-Company-Id': companyId,
        'Idempotency-Key': deliveryKey, 'Content-Type': 'application/json',
      },
      data: deliveryRequest.postData() ?? '',
    })
    expect(replayedDelivery.status()).toBe(200)
    expect(replayedDelivery.headers()['idempotency-replayed']).toBe('true')
    expect(await effects(shipmentBody.movementId)).toEqual(shippedEffects)
    const afterReplay = await readInstruction(page, loaderActor, createdId)
    expect(afterReplay.shares.filter((share) => share.kind === 'Delivered')).toHaveLength(1)
    expect(afterReplay.instruction.version).toBe(final.instruction.version)
    await openSalesProgress(sales)
    await expectSalesProgress(sales, deliveredAfter)
    await openSalesProgress(sales, true)
    await expectSalesProgress(sales, deliveredAfter)
    await sales.close()
  } finally {
    await pool.close()
  }
})

test('L04-06 the reader reads and cannot create or record; the preparer has nothing under the other company', async ({ page }) => {
  await signInAs(page, readerActor)
  // Playwright restarts the worker after a failed case, which drops the module-level `createdId`: recover the id L04-01
  // wrote down (the stable mark), then prove it is a real instruction of this company before asserting on it.
  if (createdId === '') createdId = recallCreatedId()
  expect(createdId).not.toBe(instructionId)
  await readInstruction(page, readerActor, createdId)
  await page.goto(LIST_ROUTE)
  await expect(page.getByRole('heading', { level: 1, name: 'Yükleme talimatları' })).toBeVisible({ timeout: 30_000 })
  await expect(instructionRow(page, createdId)).toBeVisible()
  await expect(page.getByRole('link', { name: 'Yeni yükleme talimatı' })).toHaveCount(0)

  // The create route is closed onto the neutral not-found block: no form, no read of the Sales sources.
  const salesReads: string[] = []
  page.on('request', (request) => {
    if (new URL(request.url()).pathname.startsWith('/api/v1/sales/')) salesReads.push(request.url())
  })
  await page.goto(CREATE_ROUTE)
  await expect(page.locator('[data-state="not-found"]')).toBeVisible({ timeout: 30_000 })
  await expect(page.getByRole('heading', { level: 1, name: 'Yeni yükleme talimatı' })).toHaveCount(0)
  expect(salesReads).toEqual([])

  // The detail opens; the load form is a server-measured command: the reader's send is refused 403, nothing recorded.
  const before = await readInstruction(page, readerActor, createdId)
  await openDetail(page, createdId)
  await expect(status(page)).toHaveText('Yayımlandı')
  await expect(issueButton(page)).toHaveCount(0)
  const refused = await recordLoad(page, before.lines[0].lineId, '0.5')
  await expect(loadNotice(page)).toContainText('yetkiniz yok')
  expect(refused.map((answer) => answer.status())).toEqual([403])
  const after = await readInstruction(page, readerActor, createdId)
  expect([after.instruction.version, after.shares.length]).toEqual([before.instruction.version, before.shares.length])

  // The preparer holds NO Sales grant (deny-first): the Sales draft routes refuse it, while the price-free Logistics read
  // still answers the seeded approved line — with no price key anywhere in the JSON — and the picker still offers it.
  const salesHeaders = { [DEVELOPMENT_USER_HEADER]: preparerActor, 'X-Company-Id': companyId }
  expect((await page.request.get(`${env.apiBaseUrl}${SALES_DRAFTS_PATH}/${draftId}`, { headers: salesHeaders })).status()).toBe(403)
  expect((await page.request.get(`${env.apiBaseUrl}${SALES_DRAFTS_PATH}?approvalState=Approved`, { headers: salesHeaders })).status()).toBe(403)
  const priceFree = await page.request.get(`${env.apiBaseUrl}${APPROVED_LINES_PATH}`, { headers: salesHeaders })
  expect(priceFree.status()).toBe(200)
  const approvedLines = (await priceFree.json()) as unknown
  const serialized = JSON.stringify(approvedLines)
  expect(serialized).toContain(DOCUMENT_NO)
  expect(serialized.toLowerCase()).toContain(approvedLineId.toLowerCase())
  const keys = [...keysOf(approvedLines)]
  for (const key of PRICE_KEYS) expect(keys, `no price key in the price-free read: ${key}`).not.toContain(key)
  expect(keys, 'the price-free read names the customer (Business 5745076917)').toContain('customerName')
  expect(serialized).toContain(customerName)
  await signInAs(page, preparerActor)
  await page.goto(CREATE_ROUTE)
  await expect(page.getByRole('heading', { level: 1, name: 'Yeni yükleme talimatı' })).toBeVisible({ timeout: 30_000 })
  await expect(page.getByLabel('Müşteri (onaylı siparişlerden)').locator('option', { hasText: customerName })).toHaveCount(1, { timeout: 30_000 })

  // The preparer under the other company holds no Logistics grant there: the real API answers nothing of GDC's
  // instruction (the tenant fence, code-less 403/404), and the shell publishes no Logistics screen for that company.
  const elsewhere = await page.request.get(`${env.apiBaseUrl}${INSTRUCTIONS_PATH}/${createdId}`, {
    headers: { [DEVELOPMENT_USER_HEADER]: preparerActor, 'X-Company-Id': otherCompanyId },
  })
  expect([403, 404]).toContain(elsewhere.status())
  const capabilities = await page.request.get(`${env.apiBaseUrl}/api/v1/shell/capabilities`, {
    headers: { [DEVELOPMENT_USER_HEADER]: preparerActor, 'X-Company-Id': otherCompanyId },
  })
  if (capabilities.status() === 200) {
    const snapshot = (await capabilities.json()) as { capabilities?: unknown }
    const published = Array.isArray(snapshot.capabilities) ? (snapshot.capabilities as string[]) : []
    expect(published.filter((id) => id.startsWith('logistics.'))).toEqual([])
  } else {
    expect([403, 404]).toContain(capabilities.status())
  }
})

test('L04-07 AT-053: the loader records the manual weighbridge record once; reopening shows net and the signed loaded-vs-weighed difference as facts beside shipment and delivery; the same key replays with no second record; a second record and the reader are refused; Inventory is not written', async ({ page }) => {
  await signInAs(page, loaderActor)
  if (createdId === '') createdId = recallCreatedId()
  await openDetail(page, createdId)
  const before = await readInstruction(page, loaderActor, createdId)
  expect(before.instruction.status).toBe('Issued')
  // The kilograms the weighing is compared with are the server's own loaded text (3.5 after L04-03), read, never assumed.
  const loaded = before.lines.reduce((sum, line) => sum + Number(line.loaded), 0)
  expect(loaded).toBe(3.5)
  await expect(page.getByTestId('logistics-weighing-none')).toBeVisible()

  const pool = await new sql.ConnectionPool(env.sqlConnection).connect()
  try {
    const counts = async () => {
      const result = await pool.request()
        .input('companyId', sql.UniqueIdentifier, companyId)
        .input('instructionId', sql.UniqueIdentifier, createdId)
        .input('entityId', sql.NVarChar, createdId)
        .query(`
          SELECT
            (SELECT COUNT(*) FROM [logistics].[VehicleWeighings] WHERE [CompanyId]=@companyId AND [InstructionId]=@instructionId) AS weighings,
            (SELECT COUNT(*) FROM [inventory].[Movements] WHERE [CompanyId]=@companyId) AS movements,
            (SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CompanyId]=@companyId AND [EntityId]=@entityId
               AND [EventType]=N'logistics.loading-instruction.weighing-recorded.v1' AND [SourceChannel]=N'Web') AS audits`)
      return result.recordset[0] as { weighings: number; movements: number; audits: number }
    }
    const start = await counts()
    expect([start.weighings, start.audits]).toEqual([0, 0])

    // The person types the two weighings off the ticket in the order taken; the server calculates the rest.
    const form = page.getByTestId('logistics-weighing-form')
    await form.locator('#logistics-weighing-direction').selectOption('TareFirst')
    await form.locator('#logistics-weighing-first').fill('14000')
    await form.locator('#logistics-weighing-second').fill('14003,2')
    const answer = page.waitForResponse((response) => response.request().method() === 'POST'
      && new URL(response.url()).pathname === `${INSTRUCTIONS_PATH}/${createdId}/weighings`)
    await page.getByTestId('logistics-weighing-button').click()
    const recorded = await answer
    expect(recorded.status()).toBe(200)
    const sent = recorded.request().postDataJSON() as Record<string, unknown>
    expect(Object.keys(sent).sort()).toEqual(['direction', 'expectedVersion', 'firstWeighingKg', 'secondWeighingKg', 'weighingId'])
    expect(sent).toMatchObject({ direction: 'TareFirst', firstWeighingKg: '14000', secondWeighingKg: '14003.2' })
    await expect(page.getByTestId('logistics-weighing-notice')).toHaveText('İşlem kaydedildi.')
    await expect(page.getByTestId('logistics-weighing-button')).toHaveCount(0)

    // Close and reopen: the record is on the instruction, the difference keeps its sign, and no verdict word exists.
    await page.goto(LIST_ROUTE)
    await openDetail(page, createdId)
    await expect(page.getByTestId('logistics-weighing-net')).toHaveText('3.2')
    await expect(page.getByTestId('logistics-weighing-loaded')).toHaveText('3.5')
    await expect(page.getByTestId('logistics-weighing-difference')).toHaveText('-0.3')
    await expect(page.getByTestId('logistics-weighing-tare')).toHaveText('14000')
    await expect(page.getByTestId('logistics-weighing-gross')).toHaveText('14003.2')
    for (const word of ['tolerans dışı', 'Tolerans dışı', 'kabul edildi', ...PRICE_WORDS]) {
      await expect(page.getByTestId('logistics-weighing')).not.toContainText(word)
    }
    // The next step reads the same instruction: the delivery section of this detail is where it continues.
    await expect(page.getByTestId('delivery-1')).toBeVisible()
    const reopened = await page.request.get(`${env.apiBaseUrl}${INSTRUCTIONS_PATH}/${createdId}`, {
      headers: { [DEVELOPMENT_USER_HEADER]: loaderActor, 'X-Company-Id': companyId },
    })
    const reading = (await reopened.json()) as { instruction: { version: string }; weighing: Record<string, unknown> | null }
    expect(reading.weighing).toMatchObject({ weighingId: sent.weighingId, netKg: '3.2', loadedKgAtWeighing: '3.5', differenceKg: '-0.3' })

    // The same key and body again through the real route: the stored answer, never a second record.
    const key = recorded.request().headers()['idempotency-key']
    expect(key, 'the browser sent the weighing under an Idempotency-Key').toBeTruthy()
    const replay = await page.request.post(`${env.apiBaseUrl}${INSTRUCTIONS_PATH}/${createdId}/weighings`, {
      headers: { [DEVELOPMENT_USER_HEADER]: loaderActor, 'X-Company-Id': companyId, 'Idempotency-Key': key, 'Content-Type': 'application/json' },
      data: recorded.request().postData() ?? '',
    })
    expect(replay.status()).toBe(200)
    expect(replay.headers()['idempotency-replayed']).toBe('true')

    // A second record under a new key is refused (its correction is not decided); nothing changes.
    const second = await page.request.post(`${env.apiBaseUrl}${INSTRUCTIONS_PATH}/${createdId}/weighings`, {
      headers: {
        [DEVELOPMENT_USER_HEADER]: loaderActor, 'X-Company-Id': companyId, 'Content-Type': 'application/json',
        'X-Correlation-Id': `browser-suite-l04-weighing-2-${Date.now()}`, 'Idempotency-Key': `browser-suite:l04:weighing-2:${Date.now()}`,
      },
      data: { expectedVersion: reading.instruction.version, weighingId: crypto.randomUUID(), direction: 'TareFirst', firstWeighingKg: '14000', secondWeighingKg: '14004' },
    })
    expect(second.status()).toBe(409)
    expect(((await second.json()) as { code?: unknown }).code).toBe('logistics_weighing_already_recorded')

    // The reader holds no weighing grant: refused 403 at the fence.
    const byReader = await page.request.post(`${env.apiBaseUrl}${INSTRUCTIONS_PATH}/${createdId}/weighings`, {
      headers: {
        [DEVELOPMENT_USER_HEADER]: readerActor, 'X-Company-Id': companyId, 'Content-Type': 'application/json',
        'X-Correlation-Id': `browser-suite-l04-weighing-reader-${Date.now()}`, 'Idempotency-Key': `browser-suite:l04:weighing-reader:${Date.now()}`,
      },
      data: { expectedVersion: reading.instruction.version, weighingId: crypto.randomUUID(), direction: 'TareFirst', firstWeighingKg: '1', secondWeighingKg: '2' },
    })
    expect(byReader.status()).toBe(403)

    const end = await counts()
    expect(end).toEqual({ weighings: 1, movements: start.movements, audits: 1 })
    const after = await readInstruction(page, loaderActor, createdId)
    expect(after.instruction.version).toBe(reading.instruction.version)
  } finally {
    await pool.close()
  }
})

// AT-025: the operations work list. The actor and the purchase order are the seed's deterministic values
// (L04BrowserSeed.OperationsActor / PurchaseOrderId / PurchaseLineId / SupplierName; seed_export carries only the three
// Logistics actors, the SALES_READER_ACTOR way). The operations actor holds the two section names, the Cari party read
// (names) and the two owner screens' reads (sales.drafts.read, purchasing.orders.read) — no price name.
const OPERATIONS_ACTOR = 'l04-operations'
const PURCHASE_ORDER_ID = 'e7000000-0000-4000-8000-000000000401'
const PURCHASE_LINE_ID = 'e7100000-0000-4000-8000-000000000401'
const SUPPLIER_NAME = 'SENTETIK browser-suite:l04 Operasyon tedarikçisi'
const SALES_WORK_ROUTE = '/lojistik/operasyon-isleri/satis'
const PURCHASE_WORK_ROUTE = '/lojistik/operasyon-isleri/satinalma'
const SALES_WORK_PATH = '/api/v1/logistics/operations-work/sales'
const PURCHASE_WORK_PATH = '/api/v1/logistics/operations-work/purchase'

test('L04-08 AT-025: the operations work list keeps sales and purchase work in separate sections; a row opens its owner screen; back keeps the section and the search; reopen shows the same; no price; the reader sees neither section', async ({ page }) => {
  await signInAs(page, OPERATIONS_ACTOR)

  // The sales section: the seeded approved sales line (by its document number), never the purchase order.
  const salesRead = page.waitForResponse((response) => response.request().method() === 'GET'
    && new URL(response.url()).pathname === SALES_WORK_PATH, { timeout: 30_000 })
  await page.goto(SALES_WORK_ROUTE)
  await expect(page.getByRole('heading', { level: 1, name: 'Operasyon işleri' })).toBeVisible({ timeout: 30_000 })
  const salesAnswer = await salesRead
  expect(salesAnswer.status()).toBe(200)
  const salesBody = (await salesAnswer.json()) as { section: string }
  expect(salesBody.section).toBe('Sales')
  expect(JSON.stringify(salesBody).toLowerCase()).not.toContain(PURCHASE_ORDER_ID)
  const salesRow = page.getByTestId(`operations-sales-row-${approvedLineId}`)
  await expect(salesRow).toBeVisible({ timeout: 30_000 })
  await expect(salesRow).toContainText(DOCUMENT_NO)
  await expect(salesRow).toContainText(customerName)
  await expect(page.getByTestId('operations-sales-table')).not.toContainText(SUPPLIER_NAME)
  await expect(page.getByTestId(`operations-purchase-row-${PURCHASE_LINE_ID}`)).toHaveCount(0)
  await expectNoPriceText(page)
  for (const key of PRICE_KEYS) expect([...keysOf(salesBody)], `no price key in the sales section: ${key}`).not.toContain(key)

  // The purchase section through the section switcher: the seeded purchase line (by the supplier's Cari name), never the sales order.
  const purchaseRead = page.waitForResponse((response) => response.request().method() === 'GET'
    && new URL(response.url()).pathname === PURCHASE_WORK_PATH, { timeout: 30_000 })
  await page.getByTestId('operations-tab-purchase').click()
  await expect(page).toHaveURL(new RegExp(`${PURCHASE_WORK_ROUTE}$`))
  const purchaseAnswer = await purchaseRead
  expect(purchaseAnswer.status()).toBe(200)
  const purchaseBody = (await purchaseAnswer.json()) as { section: string }
  expect(purchaseBody.section).toBe('Purchase')
  expect(JSON.stringify(purchaseBody).toLowerCase()).not.toContain(draftId.toLowerCase())
  const purchaseRow = page.getByTestId(`operations-purchase-row-${PURCHASE_LINE_ID}`)
  await expect(purchaseRow).toBeVisible({ timeout: 30_000 })
  await expect(purchaseRow).toContainText(SUPPLIER_NAME)
  // The same seeded 7 kg PO line remains after its real 2 kg receipt, with exactly 5 kg open.
  await expect(purchaseRow.locator('td').last()).toHaveText('5')
  await expect(page.getByTestId('operations-purchase-table')).not.toContainText(DOCUMENT_NO)
  await expect(page.getByTestId(`operations-sales-row-${approvedLineId}`)).toHaveCount(0)
  await expectNoPriceText(page)
  for (const key of PRICE_KEYS) expect([...keysOf(purchaseBody)], `no price key in the purchase section: ${key}`).not.toContain(key)

  // Search, then open the row's OWNER screen (P-03-DETAIL), then back: the same section and search, and the same row.
  await page.getByLabel('Gösterilen satırlarda ara').fill('Operasyon tedarik')
  await expect(page).toHaveURL(/\?ara=Operasyon/)
  await purchaseRow.getByRole('link').click()
  await expect(page).toHaveURL(new RegExp(`/satinalma/siparisler/${PURCHASE_ORDER_ID}$`))
  await expect(page.getByRole('heading', { level: 1, name: 'Satın alma siparişi' })).toBeVisible({ timeout: 30_000 })
  await page.goBack()
  await expect(page).toHaveURL(new RegExp(`${PURCHASE_WORK_ROUTE}\\?ara=Operasyon`))
  await expect(page.getByLabel('Gösterilen satırlarda ara')).toHaveValue('Operasyon tedarik')
  await expect(page.getByTestId('operations-tab-purchase')).toHaveAttribute('aria-current', 'page')
  await expect(page.getByTestId(`operations-purchase-row-${PURCHASE_LINE_ID}`)).toContainText(SUPPLIER_NAME, { timeout: 30_000 })

  // The sales row opens the Sales order's S-04; back returns to the sales section; a fresh open shows the same row.
  await page.getByTestId('operations-tab-sales').click()
  await expect(page).toHaveURL(new RegExp(`${SALES_WORK_ROUTE}$`))
  await page.getByTestId(`operations-sales-row-${approvedLineId}`).getByRole('link', { name: DOCUMENT_NO }).click()
  await expect(page).toHaveURL(new RegExp(`/satis/siparisler/${draftId}$`))
  await expect(page.getByRole('heading', { level: 1, name: DOCUMENT_NO })).toBeVisible({ timeout: 30_000 })
  await page.goBack()
  await expect(page).toHaveURL(new RegExp(`${SALES_WORK_ROUTE}$`))
  await expect(page.getByTestId(`operations-sales-row-${approvedLineId}`)).toBeVisible({ timeout: 30_000 })
  await page.reload()
  await expect(page.getByTestId(`operations-sales-row-${approvedLineId}`)).toContainText(DOCUMENT_NO, { timeout: 30_000 })

  // The reader holds neither section name: both routes close onto the neutral not-found block and both reads answer 403.
  await signInAs(page, readerActor)
  for (const route of [SALES_WORK_ROUTE, PURCHASE_WORK_ROUTE]) {
    await page.goto(route)
    await expect(page.locator('[data-state="not-found"]')).toBeVisible({ timeout: 30_000 })
    await expect(page.getByRole('heading', { level: 1, name: 'Operasyon işleri' })).toHaveCount(0)
  }

  const readerHeaders = { [DEVELOPMENT_USER_HEADER]: readerActor, 'X-Company-Id': companyId }
  expect((await page.request.get(`${env.apiBaseUrl}${SALES_WORK_PATH}`, { headers: readerHeaders })).status()).toBe(403)
  expect((await page.request.get(`${env.apiBaseUrl}${PURCHASE_WORK_PATH}`, { headers: readerHeaders })).status()).toBe(403)

  // Under the other company the operations actor holds nothing: the reads answer nothing of GDC's work.
  const elsewhere = { [DEVELOPMENT_USER_HEADER]: OPERATIONS_ACTOR, 'X-Company-Id': otherCompanyId }
  for (const path of [SALES_WORK_PATH, PURCHASE_WORK_PATH]) {
    const answer = await page.request.get(`${env.apiBaseUrl}${path}`, { headers: elsewhere })
    expect([403, 404]).toContain(answer.status())
  }
})

test('L04-09 AT-055: the loader drafts a dispatch note from the definite shipment; reopening shows it with the shipped quantity; the same key replays with no second draft; a second draft for the same shipment and the reader are refused; the Sales order links to it; Inventory is not written', async ({ page }) => {
  await signInAs(page, loaderActor)
  if (createdId === '') createdId = recallCreatedId()
  await openDetail(page, createdId)
  const before = await readInstruction(page, loaderActor, createdId)
  // L04-05 recorded exactly one definite shipment of 2 kg on line 1: the dispatch note documents that shipment only.
  const shipments = before.shares.filter((share) => share.kind === 'Shipped')
  expect(shipments).toHaveLength(1)
  const shipment = shipments[0]
  expect(shipment.quantity).toBe('2')
  await expect(page.getByTestId('logistics-dispatch-notes-none')).toBeVisible()

  const pool = await new sql.ConnectionPool(env.sqlConnection).connect()
  try {
    const counts = async () => {
      const result = await pool.request()
        .input('companyId', sql.UniqueIdentifier, companyId)
        .input('instructionId', sql.UniqueIdentifier, createdId)
        .input('entityId', sql.NVarChar, createdId)
        .query(`
          SELECT
            (SELECT COUNT(*) FROM [logistics].[DispatchNotes] WHERE [CompanyId]=@companyId AND [InstructionId]=@instructionId) AS notes,
            (SELECT COUNT(*) FROM [logistics].[DispatchNoteLines] WHERE [CompanyId]=@companyId AND [InstructionId]=@instructionId) AS lines,
            (SELECT COUNT(*) FROM [inventory].[Movements] WHERE [CompanyId]=@companyId) AS movements,
            (SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CompanyId]=@companyId AND [EntityId]=@entityId
               AND [EventType]=N'logistics.loading-instruction.dispatch-note-drafted.v1' AND [SourceChannel]=N'Web') AS audits`)
      return result.recordset[0] as { notes: number; lines: number; movements: number; audits: number }
    }
    const start = await counts()
    expect([start.notes, start.lines, start.audits]).toEqual([0, 0, 0])

    // The person picks the shipment in the detail and prepares the draft; nothing else is typed.
    const draftPath = `${INSTRUCTIONS_PATH}/${createdId}/dispatch-notes`
    await page.getByTestId(`logistics-dispatch-note-shipment-${shipment.shareId}`).getByRole('checkbox').check()
    const answer = page.waitForResponse((response) => response.request().method() === 'POST' && new URL(response.url()).pathname === draftPath)
    await page.getByTestId('logistics-dispatch-note-button').click()
    const drafted = await answer
    expect(drafted.status()).toBe(200)
    const sent = drafted.request().postDataJSON() as { dispatchNoteId: string; shipmentIds: string[]; expectedVersion: string }
    expect(Object.keys(sent).sort()).toEqual(['dispatchNoteId', 'expectedVersion', 'shipmentIds'])
    expect(sent).toMatchObject({ expectedVersion: before.instruction.version, shipmentIds: [shipment.shareId] })
    await expect(page.getByTestId('logistics-dispatch-note-notice')).toHaveText('İşlem kaydedildi.')

    // Close and reopen: the draft is on the instruction with ONE line — the shipment's shipped quantity — and the shipment
    // can no longer be picked (AT-055). No number, final state or print control exists on the screen.
    await page.goto(LIST_ROUTE)
    await openDetail(page, createdId)
    const block = page.getByTestId(`logistics-dispatch-note-${sent.dispatchNoteId}`)
    await expect(block).toBeVisible()
    await expect(block.getByTestId('logistics-dispatch-note-status')).toHaveText('Taslak')
    await expect(block.getByTestId('logistics-dispatch-note-line-1-quantity')).toHaveText('2')
    await expect(block.getByTestId('logistics-dispatch-note-line-2')).toHaveCount(0)
    await expect(page.getByTestId(`logistics-dispatch-note-shipment-${shipment.shareId}`).getByRole('checkbox')).toBeDisabled()
    await expect(page.getByTestId('logistics-dispatch-notes-undecided')).toBeVisible()
    for (const word of PRICE_WORDS) await expect(page.getByTestId('logistics-dispatch-notes')).not.toContainText(word)

    // The same key and body again through the real route: the stored answer, never a second draft.
    const key = drafted.request().headers()['idempotency-key']
    expect(key, 'the browser sent the draft under an Idempotency-Key').toBeTruthy()
    const replay = await page.request.post(`${env.apiBaseUrl}${draftPath}`, {
      headers: { [DEVELOPMENT_USER_HEADER]: loaderActor, 'X-Company-Id': companyId, 'Idempotency-Key': key, 'Content-Type': 'application/json' },
      data: drafted.request().postData() ?? '',
    })
    expect(replay.status()).toBe(200)
    expect(replay.headers()['idempotency-replayed']).toBe('true')
    const reading = await readInstruction(page, loaderActor, createdId)

    // AT-055: a second draft for the SAME shipment under a new key is refused; the first draft stays as it is.
    const second = await page.request.post(`${env.apiBaseUrl}${draftPath}`, {
      headers: {
        [DEVELOPMENT_USER_HEADER]: loaderActor, 'X-Company-Id': companyId, 'Content-Type': 'application/json',
        'X-Correlation-Id': `browser-suite-l04-dispatch-2-${Date.now()}`, 'Idempotency-Key': `browser-suite:l04:dispatch-2:${Date.now()}`,
      },
      data: { expectedVersion: reading.instruction.version, dispatchNoteId: crypto.randomUUID(), shipmentIds: [shipment.shareId] },
    })
    expect(second.status()).toBe(409)
    expect(((await second.json()) as { code?: unknown }).code).toBe('logistics_shipment_already_dispatched')

    // The reader holds no dispatch-note grant: refused 403 at the fence.
    const byReader = await page.request.post(`${env.apiBaseUrl}${draftPath}`, {
      headers: {
        [DEVELOPMENT_USER_HEADER]: readerActor, 'X-Company-Id': companyId, 'Content-Type': 'application/json',
        'X-Correlation-Id': `browser-suite-l04-dispatch-reader-${Date.now()}`, 'Idempotency-Key': `browser-suite:l04:dispatch-reader:${Date.now()}`,
      },
      data: { expectedVersion: reading.instruction.version, dispatchNoteId: crypto.randomUUID(), shipmentIds: [shipment.shareId] },
    })
    expect(byReader.status()).toBe(403)

    const end = await counts()
    expect(end).toEqual({ notes: 1, lines: 1, movements: start.movements, audits: 1 })
    expect((await readInstruction(page, loaderActor, createdId)).instruction.version).toBe(reading.instruction.version)

    // The Sales order view (S-04, under "Teslimat durumu") lists the draft and links to it; the link opens the
    // instruction's detail at the draft.
    const sales = await page.context().newPage()
    await signInAs(sales, SALES_READER_ACTOR)
    await openSalesProgress(sales)
    const link = sales.getByTestId(`sales-dispatch-note-link-${sent.dispatchNoteId}`)
    await expect(link).toBeVisible({ timeout: 30_000 })
    await expect(sales.getByTestId(`sales-dispatch-note-${sent.dispatchNoteId}`)).toContainText('Satır 1: 2')
    await expect(sales.getByTestId(`sales-dispatch-note-status-${sent.dispatchNoteId}`)).toHaveText('Taslak')
    await link.click()
    await expect(sales).toHaveURL(new RegExp(`/lojistik/yukleme-talimatlari/${createdId}#irsaliye-${sent.dispatchNoteId}$`))
    await expect(sales.getByTestId(`logistics-dispatch-note-${sent.dispatchNoteId}`)).toBeVisible({ timeout: 30_000 })
    await sales.close()

    // Lifecycle (user decision 2026-09-30): the loader approves the draft in the detail — the server assigns the company series
    // number — then records the hand-entered e-İrsaliye number, then cancels it with a reason. The cancelled note stays visible
    // with its number, and its shipment is released: a NEW draft is prepared from the same shipment (correction = cancel + new).
    const notePath = `${draftPath}/${sent.dispatchNoteId}`
    const panel = page.getByTestId(`logistics-dispatch-note-lifecycle-${sent.dispatchNoteId}`)
    const approveAnswer = page.waitForResponse((response) => response.request().method() === 'POST'
      && new URL(response.url()).pathname === `${notePath}/approve`)
    await panel.getByTestId('logistics-dispatch-note-approve').click()
    const approved = await approveAnswer
    expect(approved.status()).toBe(200)
    expect(approved.request().postDataJSON()).toEqual({ expectedVersion: reading.instruction.version })
    await expect(block.getByTestId('logistics-dispatch-note-status')).toHaveText('Onaylandı')
    await expect(block.getByTestId('logistics-dispatch-note-number')).toHaveText(/^IRS-\d{6}$/)
    const erpNumber = (await block.getByTestId('logistics-dispatch-note-number').textContent()) ?? ''

    // A per-run e-İrsaliye number (GİB form: 3 characters + 13 digits), unique per company.
    const eNumber = `GYM${String(Date.now()).padStart(13, '0').slice(-13)}`
    const eAnswer = page.waitForResponse((response) => response.request().method() === 'POST'
      && new URL(response.url()).pathname === `${notePath}/e-dispatch-number`)
    await panel.getByTestId('logistics-dispatch-note-e-number-input').fill(eNumber.toLowerCase())
    await panel.getByTestId('logistics-dispatch-note-e-number-save').click()
    const eSaved = await eAnswer
    expect(eSaved.status()).toBe(200)
    expect((eSaved.request().postDataJSON() as { eDispatchNumber: string }).eDispatchNumber).toBe(eNumber)
    await expect(block.getByTestId('logistics-dispatch-note-e-number')).toHaveText(eNumber)

    // The reader holds neither lifecycle grant.
    const readerApprove = await page.request.post(`${env.apiBaseUrl}${notePath}/cancel`, {
      headers: {
        [DEVELOPMENT_USER_HEADER]: readerActor, 'X-Company-Id': companyId, 'Content-Type': 'application/json',
        'X-Correlation-Id': `browser-suite-l04-dispatch-cancel-reader-${Date.now()}`, 'Idempotency-Key': `browser-suite:l04:dispatch-cancel-reader:${Date.now()}`,
      },
      data: { expectedVersion: (await readInstruction(page, loaderActor, createdId)).instruction.version, reason: 'SENTETIK reader' },
    })
    expect(readerApprove.status()).toBe(403)

    const cancelAnswer = page.waitForResponse((response) => response.request().method() === 'POST'
      && new URL(response.url()).pathname === `${notePath}/cancel`)
    await panel.getByTestId('logistics-dispatch-note-cancel').click()
    await panel.getByTestId('logistics-dispatch-note-cancel-reason-input').fill('SENTETIK düzeltme: yanlış araç')
    await panel.getByTestId('logistics-dispatch-note-cancel-confirm').click()
    expect((await cancelAnswer).status()).toBe(200)
    await expect(block.getByTestId('logistics-dispatch-note-status')).toHaveText('İptal edildi')
    await expect(block.getByTestId('logistics-dispatch-note-number')).toHaveText(erpNumber)
    await expect(block.getByTestId('logistics-dispatch-note-cancel-reason')).toHaveText('SENTETIK düzeltme: yanlış araç')

    // Released: the shipment is pickable again and a new draft documents it.
    const pick = page.getByTestId(`logistics-dispatch-note-shipment-${shipment.shareId}`).getByRole('checkbox')
    await expect(pick).toBeEnabled()
    await pick.check()
    const redraftAnswer = page.waitForResponse((response) => response.request().method() === 'POST' && new URL(response.url()).pathname === draftPath)
    await page.getByTestId('logistics-dispatch-note-button').click()
    const redrafted = await redraftAnswer
    expect(redrafted.status()).toBe(200)
    const resent = redrafted.request().postDataJSON() as { dispatchNoteId: string; shipmentIds: string[] }
    expect(resent.dispatchNoteId).not.toBe(sent.dispatchNoteId)
    expect(resent.shipmentIds).toEqual([shipment.shareId])

    // Reopen: both notes are visible with their states; the cancelled one keeps its number and e-İrsaliye number.
    await page.reload()
    await openDetail(page, createdId)
    const cancelledBlock = page.getByTestId(`logistics-dispatch-note-${sent.dispatchNoteId}`)
    await expect(cancelledBlock.getByTestId('logistics-dispatch-note-status')).toHaveText('İptal edildi')
    await expect(cancelledBlock.getByTestId('logistics-dispatch-note-number')).toHaveText(erpNumber)
    await expect(cancelledBlock.getByTestId('logistics-dispatch-note-e-number')).toHaveText(eNumber)
    const newBlock = page.getByTestId(`logistics-dispatch-note-${resent.dispatchNoteId}`)
    await expect(newBlock.getByTestId('logistics-dispatch-note-status')).toHaveText('Taslak')
    await expect(newBlock.getByTestId('logistics-dispatch-note-line-1-quantity')).toHaveText('2')
    await expect(page.getByTestId(`logistics-dispatch-note-shipment-${shipment.shareId}`).getByRole('checkbox')).toBeDisabled()

    const final = await counts()
    expect([final.notes, final.lines, final.movements, final.audits]).toEqual([2, 2, start.movements, 2])
  } finally {
    await pool.close()
  }
})

test('L04-10 a delivered share returns to its original label once, by a different decision actor; replay and reject add no stock effect', async ({ page }) => {
  if (createdId === '') createdId = recallCreatedId()
  const before = await readInstruction(page, preparerActor, createdId)
  const delivered = before.shares.find((share) => share.kind === 'Delivered')
  expect(delivered).toBeDefined()
  if (delivered === undefined) throw new Error('L04-10 requires L04-05 delivered share')
  const line = before.lines.find((candidate) => candidate.lineId === delivered.lineId)
  expect(line).toBeDefined()
  if (line === undefined) throw new Error('L04-10 requires the delivered instruction line')

  const movementCount = async (pool: sql.ConnectionPool) => {
    const result = await pool.request()
      .input('companyId', sql.UniqueIdentifier, companyId)
      .input('labelId', sql.UniqueIdentifier, shipmentLabelId)
      .query(\`
        SELECT COUNT(DISTINCT movement.Id) AS movements
          FROM inventory.Movements movement
          INNER JOIN inventory.MovementLines movementLine
            ON movementLine.CompanyId=movement.CompanyId AND movementLine.MovementId=movement.Id
         WHERE movement.CompanyId=@companyId AND movementLine.LabelId=@labelId\`)
    return Number(result.recordset[0].movements)
  }

  const pool = await new sql.ConnectionPool(env.sqlConnection).connect()
  try {
    const movementBaseline = await movementCount(pool)
    await signInAs(page, preparerActor)
    await openDetail(page, createdId)
    const panel = page.locator('section[aria-label="Müşteri iadesi"]')
    await expect(panel).toContainText('Bu teslimat için müşteri iadesi bulunmuyor.')

    const requestPath = \`/api/v1/logistics/instructions/\${createdId}/lines/\${line.lineId}/customer-returns\`
    const requestAnswer = page.waitForResponse((response) => response.request().method() === 'POST'
      && new URL(response.url()).pathname === requestPath)
    await panel.getByLabel('İade miktarı').fill('0.5')
    await panel.getByLabel('İade nedeni').fill('SENTETIK müşteri ambalaj iadesi')
    await panel.getByLabel('İade zamanı').fill(new Date(Date.now() - 60_000).toISOString().slice(0, 16))
    await panel.getByRole('button', { name: 'Talep oluştur' }).click()
    const requestedAnswer = await requestAnswer
    expect(requestedAnswer.status()).toBe(201)
    const requested = await requestedAnswer.json() as {
      return: { customerReturnId: string; status: string; sourceDeliveryId: string; inventoryMovementId: null }
    }
    expect(requested.return).toMatchObject({
      status: 'Pending', sourceDeliveryId: delivered.shareId, inventoryMovementId: null,
    })
    expect(await movementCount(pool)).toBe(movementBaseline)

    await page.goto('/logistics')
    await openDetail(page, createdId)
    await expect(page.locator('section[aria-label="Müşteri iadesi"]')).toContainText('Bekliyor')
    const pendingListAnswer = await page.request.get(
      \`\${env.apiBaseUrl}/api/v1/logistics/orders/\${line.sourceOrderId}/customer-returns\`,
      { headers: { [DEVELOPMENT_USER_HEADER]: preparerActor, 'X-Company-Id': companyId } },
    )
    expect(pendingListAnswer.status()).toBe(200)
    expect(await pendingListAnswer.json()).toEqual(expect.arrayContaining([
      expect.objectContaining({ customerReturnId: requested.return.customerReturnId, status: 'Pending' }),
    ]))

    await signInAs(page, loaderActor)
    await openDetail(page, createdId)
    const decisionPanel = page.locator('section[aria-label="Müşteri iadesi"]')
    await expect(decisionPanel).toContainText('Bekliyor')
    const approvePath = \`/api/v1/logistics/customer-returns/\${requested.return.customerReturnId}/approve\`
    const approveRequest = page.waitForRequest((request) => request.method() === 'POST'
      && new URL(request.url()).pathname === approvePath)
    const approveAnswer = page.waitForResponse((response) => response.request().method() === 'POST'
      && new URL(response.url()).pathname === approvePath)
    await decisionPanel.getByLabel('Karar notu').fill('SENTETIK farklı aktör kabulü')
    await decisionPanel.getByRole('button', { name: 'Onayla' }).click()
    const [approvedRequest, approvedAnswer] = await Promise.all([approveRequest, approveAnswer])
    expect(approvedAnswer.status()).toBe(200)
    const approved = await approvedAnswer.json() as {
      return: { customerReturnId: string; status: string; inventoryMovementId: string; originalLabelId: string }
    }
    expect(approved.return).toMatchObject({
      customerReturnId: requested.return.customerReturnId, status: 'Approved', originalLabelId: shipmentLabelId,
    })
    expect(await movementCount(pool)).toBe(movementBaseline + 1)

    const replayKey = approvedRequest.headers()['idempotency-key']
    const replayBody = approvedRequest.postData()
    expect(replayKey).toBeTruthy()
    expect(replayBody).toBeTruthy()
    const replay = await page.request.post(\`\${env.apiBaseUrl}\${approvePath}\`, {
      headers: {
        [DEVELOPMENT_USER_HEADER]: loaderActor,
        'X-Company-Id': companyId,
        'Idempotency-Key': replayKey as string,
        'Content-Type': 'application/json',
      },
      data: replayBody as string,
    })
    expect(replay.status()).toBe(200)
    expect(replay.headers()['idempotency-replayed']).toBe('true')
    expect(await replay.json()).toEqual(expect.objectContaining({ isReplay: true, return: approved.return }))
    expect(await movementCount(pool)).toBe(movementBaseline + 1)

    const freshApproved = await page.request.get(
      \`\${env.apiBaseUrl}/api/v1/logistics/customer-returns/\${requested.return.customerReturnId}\`,
      { headers: { [DEVELOPMENT_USER_HEADER]: loaderActor, 'X-Company-Id': companyId } },
    )
    expect(freshApproved.status()).toBe(200)
    expect(await freshApproved.json()).toMatchObject({ status: 'Approved', originalLabelId: shipmentLabelId })
    await page.reload()
    await openDetail(page, createdId)
    await expect(page.locator('section[aria-label="Müşteri iadesi"]')).toContainText('Onaylandı')

    const rejectedReturnId = crypto.randomUUID()
    const rejectRequest = await page.request.post(\`\${env.apiBaseUrl}\${requestPath}\`, {
      headers: {
        [DEVELOPMENT_USER_HEADER]: preparerActor,
        'X-Company-Id': companyId,
        'X-Correlation-Id': \`browser-suite-l04-return-reject-request-\${Date.now()}\`,
        'Idempotency-Key': \`browser-suite:l04:return-reject-request:\${Date.now()}\`,
        'Content-Type': 'application/json',
      },
      data: {
        customerReturnId: rejectedReturnId,
        sourceDeliveryId: delivered.shareId,
        expectedInstructionVersion: (await readInstruction(page, preparerActor, createdId)).instruction.version,
        quantity: '0.25',
        reason: 'SENTETIK reddedilecek müşteri iadesi',
        occurredAtUtc: new Date(Date.now() - 30_000).toISOString(),
      },
    })
    expect(rejectRequest.status()).toBe(201)
    const rejected = await page.request.post(
      \`\${env.apiBaseUrl}/api/v1/logistics/customer-returns/\${rejectedReturnId}/reject\`,
      {
        headers: {
          [DEVELOPMENT_USER_HEADER]: loaderActor,
          'X-Company-Id': companyId,
          'X-Correlation-Id': \`browser-suite-l04-return-reject-\${Date.now()}\`,
          'Idempotency-Key': \`browser-suite:l04:return-reject:\${Date.now()}\`,
          'Content-Type': 'application/json',
        },
        data: { expectedVersion: '1', note: 'SENTETIK farklı aktör reddi' },
      },
    )
    expect(rejected.status()).toBe(200)
    expect(await rejected.json()).toMatchObject({
      return: { customerReturnId: rejectedReturnId, status: 'Rejected', inventoryMovementId: null, originalLabelId: null },
    })
    expect(await movementCount(pool)).toBe(movementBaseline + 1)
  } finally {
    await pool.close()
  }
})

