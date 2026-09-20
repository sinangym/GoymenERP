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
 *   L04-05 reader: list and detail open, no create route, a load is refused 403 and nothing is recorded;
 *          the preparer: Sales draft routes 403 while the price-free read answers the approved line with
 *          no price key anywhere;
 *          the preparer under the other company: the API answers nothing of GDC's instruction and publishes no
 *          Logistics screen there
 */

import { existsSync, readFileSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { expect, test, type Page, type Response } from '@playwright/test'
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
} = env.seed
const INSTRUCTIONS_PATH = '/api/v1/logistics/loading-instructions'
const APPROVED_LINES_PATH = `${INSTRUCTIONS_PATH}/approved-order-lines`
const SALES_DRAFTS_PATH = '/api/v1/sales/drafts'
const PRICE_KEYS = ['unitPrice', 'discountPercent', 'paymentTerms', 'priceUnitId', 'currencyId']
const LIST_ROUTE = '/lojistik/yukleme-talimatlari'
const CREATE_ROUTE = `${LIST_ROUTE}/yeni`
const DEVELOPMENT_USER_HEADER = 'X-Development-User'
const DOCUMENT_NO = 'SENTETIK browser-suite:l04 SO-0001'
const PRODUCT_NAME = 'SENTETIK browser-suite:l04 rulo 1,25 × 1240'
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
  readonly shares: readonly { readonly shareId: string; readonly lineId: string; readonly quantity: string }[]
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

function recallCreatedId(): string {
  return existsSync(CREATED_ID_FILE) ? readFileSync(CREATED_ID_FILE, 'utf8').trim() : ''
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

test('L04-05 the reader reads and cannot create or record; the preparer has nothing under the other company', async ({ page }) => {
  await signInAs(page, readerActor)
  // Playwright restarts the worker after a failed case, which drops the module-level `createdId`: recover the id L04-01
  // wrote down (the stable mark), then prove it is a real instruction of this company before asserting on it.
  if (createdId === '') createdId = recallCreatedId()
  expect(createdId, 'L04-01 recorded the created instruction').not.toBe('')
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
