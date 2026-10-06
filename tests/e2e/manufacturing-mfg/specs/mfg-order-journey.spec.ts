/**
 * MFG — the production order screen against the REAL stack (`browser:mfg:ci`).
 *
 * Real Chromium, the real web build of the exact head, the real API and SQL Server with the
 * approved kilogram profile the real Settings provider answers. The order is arranged through the
 * real Manufacturing API as the seeded recorder (./src/arrange.ts); every fact on the screen goes
 * through the same routes. NOTHING IS FULFILLED LOCALLY: the only interceptions are the
 * Development identity rewrite and, in the unknown-outcome case, withholding the browser's
 * DELIVERY of a real answer the server really gave (the org03 pattern).
 *
 * IDENTITY. The web image bakes its own Development user at build time, so every case binds its
 * actor by rewriting the identity header on each API request and asserts every answer it relied on
 * belongs to a rewritten request.
 */

import {
  expect,
  test,
  type BrowserContext,
  type Page,
  type Request,
  type Response,
  type Route,
} from '@playwright/test'
import {
  apiAs,
  arrangeOpenOrder,
  declareThroughApi,
  issueThroughApi,
  readMassBalance,
  readOrder,
} from '../src/arrange.js'
import { MFG_PROCESSES, readEnvironment, type MfgProcess } from '../src/env.js'

const env = readEnvironment()
const FACT_RE = /\/api\/v1\/manufacturing\/orders\/[^/]+\/(input-issues|input-returns|wip-measurements|outputs)$/
const ORDER_RE = /\/api\/v1\/manufacturing\/orders\/[^/]+$/
const BALANCE_RE = /\/api\/v1\/manufacturing\/orders\/[^/]+\/mass-balance$/
const CLOSE_RE = /\/api\/v1\/manufacturing\/orders\/[^/]+\/close$/
const LIST_RE = /\/api\/v1\/manufacturing\/orders(\?|$)/
const OUTPUT_PRODUCTS_RE = /\/api\/v1\/manufacturing\/references\/output-products\?/
const OUTPUT_LOCATIONS_RE = /\/api\/v1\/manufacturing\/references\/output-locations\?/

// The screen's own texts (src/manufacturing/manufacturingText.ts), asserted verbatim.
const OUTPUT_FACT_KIND = 'Üretim çıktısı'
const ISSUED_OWNERSHIP_HEADING = 'Girdi çıkışından gelen sahiplik (değiştirilemez):'
const THIS_COMPANY = 'Bu şirket'
/** Both refusals the screen shows while the issue trace has not proved one complete position. */
const OWNERSHIP_REFUSAL_RE = /sahipliği doğrulanmadan çıktı gönderilemez|hareket geçmişi bu görünümde kısaltılmış/

/** An Inventory movement line as the label detail read carries it (the fields this suite checks). */
interface IssuedLine {
  readonly direction: string
  readonly ownerKind: string
  readonly ownerId: string
  readonly custodianKind: string
  readonly custodianId: string
}

interface LabelMovement {
  readonly kind: string
  readonly sourceKind: string
  readonly sourceId: string
  readonly sourceLineId: string | null
  readonly sourceGrain: string
  readonly lines: readonly IssuedLine[]
}

/** Binds `actor` to every API request of this context and records which requests it rewrote. */
async function asActor(
  context: BrowserContext,
  actor: string,
  rewritten = new Set<Request>(),
): Promise<Set<Request>> {
  await context.route('**/api/v1/**', async (route: Route) => {
    rewritten.add(route.request())
    await route.fallback({ headers: { ...route.request().headers(), 'x-development-user': actor } })
  })
  return rewritten
}

/** Keeps one route installed while a multi-actor journey changes its bound Development identity. */
async function bindActors(context: BrowserContext, initialActor: string) {
  const rewritten = new Set<Request>()
  let actor = initialActor
  await context.route('**/api/v1/**', async (route: Route) => {
    rewritten.add(route.request())
    await route.fallback({ headers: { ...route.request().headers(), 'x-development-user': actor } })
  })
  return {
    rewritten,
    switchTo(nextActor: string) {
      actor = nextActor
    },
  }
}

interface WorkCenterAnswer {
  readonly id: string
  readonly kind: MfgProcess
}

async function readCanonicalWorkCenters(): Promise<ReadonlyMap<MfgProcess, WorkCenterAnswer>> {
  const api = await apiAs(env, env.seed.recorderActor)
  try {
    const response = await api.get('/api/v1/organization/work-centers')
    expect(response.status()).toBe(200)
    const body = await response.json() as unknown
    expect(Array.isArray(body)).toBe(true)

    const result = new Map<MfgProcess, WorkCenterAnswer>()
    for (const process of MFG_PROCESSES) {
      const row = (body as unknown[]).find((candidate) =>
        typeof candidate === 'object' && candidate !== null
          && (candidate as Record<string, unknown>).kind === process)
      if (typeof row !== 'object' || row === null) {
        throw new Error(`fixture must publish one ${process} work center`)
      }
      const id = (row as Record<string, unknown>).id
      if (typeof id !== 'string' || !/^[0-9a-f-]{36}$/.test(id)) {
        throw new Error(`fixture ${process} work center must publish a canonical id`)
      }
      result.set(process, { id, kind: process })
    }
    return result
  } finally {
    await api.dispose()
  }
}

function watchAnswers(page: Page): Request[] {
  const answered: Request[] = []
  page.on('requestfinished', (request) => {
    if (new URL(request.url()).pathname.startsWith('/api/v1/')) answered.push(request)
  })
  return answered
}

function expectEveryAnswerRewritten(answered: readonly Request[], rewritten: ReadonlySet<Request>) {
  const foreign = answered.filter((request) => !rewritten.has(request)).map((request) => request.url())
  expect(foreign, 'every API answer must belong to a request sent as the bound actor').toEqual([])
}

async function openOrder(page: Page, orderId: string) {
  await page.goto(`/uretim/emir/${orderId}`)
  await expect(page.getByRole('heading', { name: 'Üretim emri' })).toBeVisible()
}

/** The roll's row in the roll-balance table (the GDC-BAL/1 panel has a row per roll too). */
function rollRow(page: Page, rollId: string) {
  return page.getByRole('region', { name: 'Ana rulo dengesi (kg)', exact: true }).getByRole('row', { name: new RegExp(rollId) })
}

/** The roll's row in the GDC-BAL/1 panel (BAL slice 2). */
function balanceRow(page: Page, rollId: string) {
  return page.getByRole('region', { name: 'Kütle dengesi (GDC-BAL/1)', exact: true }).getByRole('row', { name: new RegExp(rollId) })
}

/** The roll's "Çıkan" cell: data cells are Planlanan, Çıkan, İade, … after the row header. */
function issuedCell(page: Page, rollId: string) {
  return rollRow(page, rollId).getByRole('cell').nth(1)
}

/** The roll's "İade" cell, immediately after "Çıkan". */
function returnedCell(page: Page, rollId: string) {
  return rollRow(page, rollId).getByRole('cell').nth(2)
}

function kgField(page: Page) {
  return page.getByRole('textbox', { name: 'Kütle (kg)', exact: true })
}

async function fillIssue(page: Page, rollId: string, kg: string) {
  // Exact names: a label query is a substring match by default, and "Ana rulo" also names the
  // balances section ("Ana rulo dengesi (kg)").
  await page.getByRole('radio', { name: 'Girdi çıkışı', exact: true }).check()
  await page.getByRole('combobox', { name: 'Ana rulo', exact: true }).selectOption(rollId)
  await kgField(page).fill(kg)
  await page.getByLabel('Zaman', { exact: true }).fill('2026-09-18T09:30')
}

function isLabelDetailRead(response: Response, rollId: string): boolean {
  return response.request().method() === 'GET'
    && new URL(response.url()).pathname === `/api/v1/inventory/labels/${rollId}/detail`
}

/** The page's own reads of the parent roll's Inventory label detail, in arrival order. */
function watchLabelReads(page: Page, rollId: string): { readonly reads: Response[]; stop: () => void } {
  const reads: Response[] = []
  const watch = (response: Response) => {
    if (isLabelDetailRead(response, rollId)) reads.push(response)
  }
  page.on('response', watch)
  return { reads, stop: () => page.off('response', watch) }
}

/**
 * This order's input-issue trace in a label detail read the page itself received: the Out lines of its
 * `manufacturing.input-issue` Issue entries. The read must be complete (not truncated), or the screen could not
 * derive an output ownership from it.
 */
async function issueTraceOf(read: Response, orderId: string): Promise<IssuedLine[]> {
  expect(read.status(), 'the parent label detail read').toBe(200)
  const body = await read.json() as {
    movementTimeline: { windowFull: boolean; entries: LabelMovement[] }
  }
  expect(body.movementTimeline.windowFull, 'the issue trace must be complete').toBe(false)
  return body.movementTimeline.entries
    .filter((entry) => entry.kind === 'Issue' && entry.sourceKind === 'manufacturing.input-issue'
      && entry.sourceId === orderId && entry.sourceGrain === 'Line' && entry.sourceLineId !== null)
    .flatMap((entry) => entry.lines.filter((line) => line.direction === 'Out'))
}

/**
 * The output's owner and custodian are derived from the order's own input-issue trace, shown read-only, and only
 * then is the output submittable. The server trace must name this company as owner AND custodian, and the screen
 * must show exactly that, with no refusal. Nothing on the screen is typed or predicted.
 */
async function expectIssuedCompanyOwnership(page: Page, trace: readonly IssuedLine[], companyId: string) {
  expect(trace.length, 'the order has an input issue on this roll').toBeGreaterThan(0)
  for (const line of trace) {
    expect(line).toMatchObject({
      ownerKind: 'Company', ownerId: companyId, custodianKind: 'Company', custodianId: companyId,
    })
  }

  const ownership = page.getByTestId('output-issued-ownership')
  await expect(ownership).toBeVisible()
  await expect(ownership).toContainText(ISSUED_OWNERSHIP_HEADING)
  for (const party of ['owner', 'custodian'] as const) {
    const selector = `[data-party="${party}"]`
    const value = ownership.locator(selector)
    await expect(value).toHaveText(companyId)
    await expect(value).toHaveAttribute('data-kind', 'Company')
    // `has` is evaluated relative to each <dd>, so it takes the bare selector, not the chained locator.
    await expect(ownership.locator('dd').filter({ has: page.locator(selector) })).toContainText(THIS_COMPANY)
  }
  await expect(page.getByText(OWNERSHIP_REFUSAL_RE)).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Kaydet', exact: true })).toBeEnabled()
}

async function fillWip(page: Page, rollId: string, kg: string) {
  await page.getByRole('radio', { name: 'WIP ölçümü', exact: true }).check()
  await page.getByRole('combobox', { name: 'Ana rulo', exact: true }).selectOption(rollId)
  await kgField(page).fill(kg)
  await page.getByLabel('Zaman', { exact: true }).fill('2026-09-18T09:30')
}

test.describe('MFG order screen (real API, real SQL)', () => {
  test('each canonical process reaches an Inventory-backed realization and authorized close', async ({ page, context }) => {
    const actors = await bindActors(context, env.seed.recorderActor)
    const { rewritten } = actors
    const answered = watchAnswers(page)
    const workCenters = await readCanonicalWorkCenters()

    for (const process of MFG_PROCESSES) {
      const workCenter = workCenters.get(process)!
      const arranged = await arrangeOpenOrder(env, env.seed.rolls.process[process], workCenter.id)

      await openOrder(page, arranged.orderId)
      await expect(page.getByText(process, { exact: true })).toBeVisible()
      // A partial issue of the company's own parent roll. The output's owner and custodian are then derived
      // from this order's own input-issue trace on the roll (read from Inventory after the issue), not chosen.
      await fillIssue(page, arranged.rollId, '500')
      const issuePost = page.waitForRequest((request) =>
        request.url().endsWith(`/orders/${arranged.orderId}/input-issues`) && request.method() === 'POST')
      const issueResponse = page.waitForResponse((response) =>
        response.url().endsWith(`/orders/${arranged.orderId}/input-issues`) && response.request().method() === 'POST')
      // The issue form reads no label; the roll's label is read after the recorded issue (and again for the output),
      // so the latest read carries the issue's trace. Armed before the click so no read can be missed.
      const labelReads = watchLabelReads(page, arranged.rollId)
      await page.getByRole('button', { name: 'Kaydet', exact: true }).click()
      expect((await issueResponse).status()).toBe(200)
      expect((await issuePost).postDataJSON()).toMatchObject({
        expectedVersion: arranged.version,
        parentRollId: arranged.rollId,
        kg: '500',
      })
      await expect(page.getByText('Kaydedildi', { exact: true })).toBeVisible()

      const products = page.waitForResponse((response) =>
        OUTPUT_PRODUCTS_RE.test(response.url()) && response.request().method() === 'GET')
      const locations = page.waitForResponse((response) =>
        OUTPUT_LOCATIONS_RE.test(response.url()) && response.request().method() === 'GET')
      await page.getByRole('radio', { name: OUTPUT_FACT_KIND, exact: true }).check()
      await page.getByRole('combobox', { name: 'Ana rulo', exact: true }).selectOption(arranged.rollId)
      await expect(page.getByTestId('parent-label-state')).toBeVisible()
      // The ownership shown is the one in the page's own latest post-issue read of the roll's label.
      await expect(page.getByTestId('output-issued-ownership')).toBeVisible()
      labelReads.stop()
      expect(labelReads.reads.length, 'the roll label is re-read after the issue').toBeGreaterThan(0)
      await expectIssuedCompanyOwnership(page,
        await issueTraceOf(labelReads.reads[labelReads.reads.length - 1], arranged.orderId), env.seed.companyId)
      await page.getByLabel('Zaman', { exact: true }).fill('2026-09-25T09:30')
      expect((await products).status()).toBe(200)
      expect((await locations).status()).toBe(200)
      await page.getByRole('combobox', { name: 'Çıktı sınıfı', exact: true }).selectOption('GoodProduct')
      await page.getByRole('combobox', { name: 'Ürün', exact: true }).selectOption(env.seed.output.productId)
      await page.getByRole('combobox', { name: 'Lokasyon', exact: true }).selectOption(env.seed.output.locationId)
      await page.getByLabel('Yeni etiket kodu', { exact: true })
        .fill(`SYN-MFG-${process.toUpperCase()}-${arranged.orderId.replaceAll('-', '')}`)
      await page.getByRole('combobox', { name: 'İlk stok durumu', exact: true })
        .selectOption(env.seed.output.initialBlock)
      await kgField(page).fill('500')

      const outputPost = page.waitForRequest((request) =>
        request.url().endsWith(`/orders/${arranged.orderId}/outputs`) && request.method() === 'POST')
      const outputResponse = page.waitForResponse((response) =>
        response.url().endsWith(`/orders/${arranged.orderId}/outputs`) && response.request().method() === 'POST')
      await page.getByRole('button', { name: 'Kaydet', exact: true }).click()
      expect((await outputResponse).status()).toBe(200)
      expect((await outputPost).postDataJSON()).toMatchObject({
        expectedVersion: 2,
        parentRollId: arranged.rollId,
        outputClass: 'GoodProduct',
        productId: env.seed.output.productId,
        // Exactly the derived ownership the screen showed: the company's own, in its own custody.
        ownerKind: 'Company',
        ownerId: env.seed.companyId,
        custodianKind: 'Company',
        custodianId: env.seed.companyId,
        locationId: env.seed.output.locationId,
        kg: '500',
      })
      await expect(page.getByText('Kaydedildi', { exact: true })).toBeVisible()

      actors.switchTo(env.seed.closerActor)
      await openOrder(page, arranged.orderId)
      await expect(page.getByText(process, { exact: true })).toBeVisible()
      const closeResponse = page.waitForResponse((response) =>
        response.url().endsWith(`/orders/${arranged.orderId}/close`) && response.request().method() === 'POST')
      await page.getByRole('button', { name: 'Emri kapat', exact: true }).click()
      await page.getByRole('button', { name: 'Evet, emri kapat', exact: true }).click()
      expect((await closeResponse).status()).toBe(200)
      await expect(page.getByText('Emir kapatıldı', { exact: true })).toBeVisible()

      const stored = await readOrder(env, arranged.orderId)
      expect(stored).toMatchObject({
        orderId: arranged.orderId,
        workCenterId: workCenter.id,
        process,
        status: 'Closed',
        version: 4,
        rolls: [{
          parentRollId: arranged.rollId,
          issuedKg: '500',
          outputsKg: { GoodProduct: '500' },
          unexplainedKg: '0',
          isFullyAccounted: true,
        }],
      })

      actors.switchTo(env.seed.recorderActor)
    }
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('a recorder issues input, reopens it, and returns unused stock with the fresh Inventory label version', async ({ page, context }) => {
    const arranged = await arrangeOpenOrder(env, env.seed.rolls.issue)
    const rewritten = await asActor(context, env.seed.recorderActor)
    const answered = watchAnswers(page)

    await openOrder(page, arranged.orderId)
    await expect(rollRow(page, arranged.rollId)).toContainText('1000')
    await fillIssue(page, arranged.rollId, '250.5')
    const post = page.waitForRequest((request) => FACT_RE.test(request.url()) && request.method() === 'POST')
    const reread = page.waitForResponse((response) => ORDER_RE.test(response.url()) && response.request().method() === 'GET')
    const rebalanced = page.waitForResponse(
      (response) => BALANCE_RE.test(response.url()) && response.request().method() === 'GET' && response.url().includes(arranged.orderId),
    )
    await page.getByRole('button', { name: 'Kaydet', exact: true }).click()

    const sent = await post
    expect(sent.headers()['idempotency-key']).toMatch(/^[0-9a-f-]{36}$/)
    expect(sent.postDataJSON()).toMatchObject({ parentRollId: arranged.rollId, kg: '250.5', expectedVersion: arranged.version })
    expect((await reread).status()).toBe(200)
    await expect(page.getByText('Kaydedildi', { exact: true })).toBeVisible()
    await expect(issuedCell(page, arranged.rollId)).toHaveText('250.5')

    // GDC-BAL/1 (BAL slice 2): the panel reads its own GET again after the fact, and shows the server's BAL-003:
    // 250.5 issued, nothing declared → 250.5 unaccounted. BAL-004 / BAL-006 are the engine's Unavailable, no number.
    expect((await rebalanced).status()).toBe(200)
    await expect(balanceRow(page, arranged.rollId).getByTestId('mass-difference')).toHaveText('250.5')
    await expect(balanceRow(page, arranged.rollId).getByTestId('scrap-percent')).toContainText('Hesaplanamıyor')
    await expect(balanceRow(page, arranged.rollId).getByTestId('actual-unit-kg')).toContainText('gerçek adet')
    const balance = await readMassBalance(env, arranged.orderId)
    const line = (balance.rolls as Array<{ difference: { differenceKg: string }; scrapPercent: { outcome: string } }>)[0]
    expect(line.difference.differenceKg).toBe('250.5')
    expect(line.scrapPercent.outcome).toBe('Unavailable')

    const stored = await readOrder(env, arranged.orderId)
    expect((stored.rolls as Array<{ issuedKg: string }>)[0].issuedKg).toBe('250.5')

    // Reopen the same order before returning stock. The browser must read the current Inventory
    // label and carry that exact version; it cannot reuse the plan snapshot or infer a version.
    await openOrder(page, arranged.orderId)
    await page.getByRole('radio', { name: 'Kullanılmayan girdi iadesi', exact: true }).check()
    const labelBeforeReturn = page.waitForResponse((response) =>
      response.url().endsWith(`/api/v1/inventory/labels/${arranged.rollId}/detail`)
      && response.request().method() === 'GET')
    await page.getByRole('combobox', { name: 'Ana rulo', exact: true }).selectOption(arranged.rollId)
    const labelResponse = await labelBeforeReturn
    expect(labelResponse.status()).toBe(200)
    const label = await labelResponse.json() as {
      labelVersion: string
      balances: { physicalKg: string }
    }
    expect(label.labelVersion).toMatch(/^[1-9][0-9]*$/)
    expect(label.balances.physicalKg).toBe('749.5')
    await kgField(page).fill('50')
    await page.getByLabel('Zaman', { exact: true }).fill('2026-09-18T10:00')

    const returnPost = page.waitForRequest((request) =>
      request.url().endsWith(`/orders/${arranged.orderId}/input-returns`) && request.method() === 'POST')
    await page.getByRole('button', { name: 'Kaydet', exact: true }).click()
    const returned = await returnPost
    const returnBody = returned.postDataJSON() as Record<string, unknown>
    expect(returnBody).toMatchObject({
      expectedVersion: 2,
      parentRollId: arranged.rollId,
      expectedParentLabelVersion: label.labelVersion,
      kg: '50',
    })
    expect(returnBody.returnId).toMatch(/^[0-9a-f-]{36}$/)
    expect(returnBody.movementId).toMatch(/^[0-9a-f-]{36}$/)
    expect(returnBody.movementId).not.toBe(returnBody.returnId)
    await expect(page.getByText('Kaydedildi', { exact: true })).toBeVisible()

    // A fresh browser read and a direct owner read must agree on both sides of the atomic host.
    await openOrder(page, arranged.orderId)
    await expect(issuedCell(page, arranged.rollId)).toHaveText('250.5')
    await expect(returnedCell(page, arranged.rollId)).toHaveText('50')
    const returnedOrder = await readOrder(env, arranged.orderId)
    expect((returnedOrder.rolls as Array<{ returnedKg: string; netIssuedKg: string }>)[0])
      .toMatchObject({ returnedKg: '50', netIssuedKg: '200.5' })
    const api = await apiAs(env, env.seed.recorderActor)
    try {
      const currentLabelResponse = await api.get(`/api/v1/inventory/labels/${arranged.rollId}/detail`)
      const currentLabelText = await currentLabelResponse.text()
      expect(currentLabelResponse.status(), currentLabelText).toBe(200)
      const currentLabel = JSON.parse(currentLabelText) as {
        labelVersion: string
        balances: { physicalKg: string }
      }
      expect(BigInt(currentLabel.labelVersion)).toBeGreaterThan(BigInt(label.labelVersion))
      expect(currentLabel.balances.physicalKg).toBe('799.5')
    } finally {
      await api.dispose()
    }
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('a recorder selects owner-backed references and stores one company-owned output and Inventory label', async ({ page, context }) => {
    const arranged = await arrangeOpenOrder(env, env.seed.rolls.outputUi)
    const issued = await issueThroughApi(env, arranged, arranged.version, '100')
    const rewritten = await asActor(context, env.seed.recorderActor)
    const answered = watchAnswers(page)
    const products = page.waitForResponse((response) =>
      OUTPUT_PRODUCTS_RE.test(response.url()) && response.request().method() === 'GET')
    const locations = page.waitForResponse((response) =>
      OUTPUT_LOCATIONS_RE.test(response.url()) && response.request().method() === 'GET')

    await openOrder(page, arranged.orderId)
    await page.getByRole('radio', { name: OUTPUT_FACT_KIND, exact: true }).check()
    // No optimistic ownership: before the roll's Inventory trace is read, nothing is shown and nothing can be sent.
    await expect(page.getByTestId('output-issued-ownership')).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Kaydet', exact: true })).toBeDisabled()
    const labelRead = page.waitForResponse((response) => isLabelDetailRead(response, arranged.rollId))
    await page.getByRole('combobox', { name: 'Ana rulo', exact: true }).selectOption(arranged.rollId)
    await expectIssuedCompanyOwnership(page, await issueTraceOf(await labelRead, arranged.orderId), env.seed.companyId)
    await page.getByLabel('Zaman', { exact: true }).fill('2026-09-25T09:30')
    expect((await products).status()).toBe(200)
    expect((await locations).status()).toBe(200)

    await page.getByRole('combobox', { name: 'Çıktı sınıfı', exact: true }).selectOption('GoodProduct')
    await page.getByRole('combobox', { name: 'Ürün', exact: true }).selectOption(env.seed.output.productId)
    await page.getByRole('combobox', { name: 'Lokasyon', exact: true }).selectOption(env.seed.output.locationId)
    await page.getByLabel('Yeni etiket kodu', { exact: true })
      .fill(`SYN-MFG-UI-OUT-${arranged.orderId.replaceAll('-', '')}`)
    await page.getByRole('combobox', { name: 'İlk stok durumu', exact: true })
      .selectOption(env.seed.output.initialBlock)
    await kgField(page).fill('40')

    const post = page.waitForRequest((request) =>
      request.url().endsWith(`/orders/${arranged.orderId}/outputs`) && request.method() === 'POST')
    await page.getByRole('button', { name: 'Kaydet', exact: true }).click()
    const sent = await post
    const body = sent.postDataJSON() as Record<string, unknown>
    expect(body).toMatchObject({
      expectedVersion: issued.version,
      parentRollId: arranged.rollId,
      outputClass: 'GoodProduct',
      productId: env.seed.output.productId,
      expectedProductVersion: env.seed.output.productVersion,
      ownerKind: 'Company',
      ownerId: env.seed.companyId,
      custodianKind: 'Company',
      custodianId: env.seed.companyId,
      locationId: env.seed.output.locationId,
      initialBlock: env.seed.output.initialBlock,
      kg: '40',
    })
    const ids = ['outputId', 'movementId', 'lotId', 'labelId'].map((name) => body[name] as string)
    expect(new Set(ids).size).toBe(4)
    expect(body.expectedParentLabelVersion).toMatch(/^[1-9][0-9]*$/)
    await expect(page.getByText('Kaydedildi', { exact: true })).toBeVisible()
    await expect(rollRow(page, arranged.rollId)).toContainText('40')

    const api = await apiAs(env, env.seed.recorderActor)
    try {
      const labelResponse = await api.get(`/api/v1/inventory/labels/${body.labelId as string}/detail`)
      const labelText = await labelResponse.text()
      expect(labelResponse.status(), labelText).toBe(200)
      const label = JSON.parse(labelText) as {
        identity: { labelId: string; productId: string }
        balances: { physicalKg: string; positions: Array<Record<string, unknown>> }
      }
      expect(label.identity).toMatchObject({ labelId: body.labelId, productId: env.seed.output.productId })
      expect(label.balances.physicalKg).toBe('40')
      expect(label.balances.positions).toContainEqual(expect.objectContaining({
        ownerKind: 'Company', ownerId: env.seed.companyId,
        custodianKind: 'Company', custodianId: env.seed.companyId,
        locationId: env.seed.output.locationId, block: env.seed.output.initialBlock, quantityKg: '40',
      }))
    } finally {
      await api.dispose()
    }

    const stored = await readOrder(env, arranged.orderId)
    expect((stored.rolls as Array<{ outputsKg: Record<string, string> }>)[0].outputsKg.GoodProduct).toBe('40')
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('customer-owned input replays once, keeps its Cari owner through output, and returns unused stock', async ({ page, context }) => {
    const arranged = {
      orderId: env.seed.customerOwned.orderId,
      rollId: env.seed.customerOwned.roll.labelId,
      version: 1,
    }
    const rewritten = await asActor(context, env.seed.recorderActor)
    const answered = watchAnswers(page)
    const before = await readOrder(env, arranged.orderId)
    const beforeRoll = (before.rolls as Array<{
      parentRollId: string
      commercialContext: {
        partyId: string
        companyPartyAccountId: string
        partyVersion: string
        accountVersion: string
      }
    }>)[0]
    expect(beforeRoll.parentRollId).toBe(arranged.rollId)
    expect(beforeRoll.commercialContext).toMatchObject({
      partyId: expect.stringMatching(/^[0-9a-f-]{36}$/),
      companyPartyAccountId: expect.stringMatching(/^[0-9a-f-]{36}$/),
      partyVersion: expect.stringMatching(/^.+$/),
      accountVersion: expect.stringMatching(/^.+$/),
    })
    expect(beforeRoll.commercialContext.partyId).not.toBe(env.seed.companyId)

    // The first real issue answer is withheld from the page. Retrying sends the same command and Inventory moves
    // the customer's roll only once; the replayed Manufacturing answer refreshes the order from server truth.
    let withheld: number | null = null
    await page.route(FACT_RE, async (route) => {
      if (withheld !== null) return route.fallback()
      const response = await route.fetch({
        headers: { ...route.request().headers(), 'x-development-user': env.seed.recorderActor },
      })
      withheld = response.status()
      await route.abort('failed')
    })
    await openOrder(page, arranged.orderId)
    await fillIssue(page, arranged.rollId, '500')
    await page.getByRole('button', { name: 'Kaydet', exact: true }).click()
    await expect(page.getByText('Sonuç bilinmiyor')).toBeVisible()
    expect(withheld).toBe(200)
    await page.getByRole('button', { name: 'Aynı olguyu yeniden gönder', exact: true }).click()
    await expect(page.getByText('Daha önce kaydedilmiş')).toBeVisible()
    const issuedOrder = await readOrder(env, arranged.orderId)
    expect((issuedOrder.rolls as Array<{ issuedKg: string }>)[0].issuedKg).toBe('500')

    await openOrder(page, arranged.orderId)
    const products = page.waitForResponse((response) =>
      OUTPUT_PRODUCTS_RE.test(response.url()) && response.request().method() === 'GET')
    const locations = page.waitForResponse((response) =>
      OUTPUT_LOCATIONS_RE.test(response.url()) && response.request().method() === 'GET')
    await page.getByRole('radio', { name: OUTPUT_FACT_KIND, exact: true }).check()
    const labelRead = page.waitForResponse((response) => isLabelDetailRead(response, arranged.rollId))
    await page.getByRole('combobox', { name: 'Ana rulo', exact: true }).selectOption(arranged.rollId)
    const trace = await issueTraceOf(await labelRead, arranged.orderId)
    expect(trace).toHaveLength(1)
    expect(trace[0]).toMatchObject({
      ownerKind: 'Counterparty',
      ownerId: beforeRoll.commercialContext.partyId,
      custodianKind: 'Company',
      custodianId: env.seed.companyId,
    })
    const ownership = page.getByTestId('output-issued-ownership')
    await expect(ownership.locator('[data-party="owner"]')).toHaveText(beforeRoll.commercialContext.partyId)
    await expect(ownership.locator('[data-party="owner"]')).toHaveAttribute('data-kind', 'Counterparty')
    await expect(ownership.locator('[data-party="custodian"]')).toHaveText(env.seed.companyId)
    await expect(ownership.locator('[data-party="custodian"]')).toHaveAttribute('data-kind', 'Company')
    await expect(ownership).toContainText('Cari')
    await expect(ownership).toContainText(THIS_COMPANY)
    await page.getByLabel('Zaman', { exact: true }).fill('2026-09-25T10:00')
    expect((await products).status()).toBe(200)
    expect((await locations).status()).toBe(200)
    await page.getByRole('combobox', { name: 'Çıktı sınıfı', exact: true }).selectOption('GoodProduct')
    await page.getByRole('combobox', { name: 'Ürün', exact: true }).selectOption(env.seed.output.productId)
    await page.getByRole('combobox', { name: 'Lokasyon', exact: true }).selectOption(env.seed.output.locationId)
    await page.getByLabel('Yeni etiket kodu', { exact: true })
      .fill(`SYN-MFG-FASON-OUT-${arranged.orderId.replaceAll('-', '')}`)
    await page.getByRole('combobox', { name: 'İlk stok durumu', exact: true })
      .selectOption(env.seed.output.initialBlock)
    await kgField(page).fill('300')
    const outputPost = page.waitForRequest((request) =>
      request.url().endsWith(`/orders/${arranged.orderId}/outputs`) && request.method() === 'POST')
    const outputResponse = page.waitForResponse((response) =>
      response.url().endsWith(`/orders/${arranged.orderId}/outputs`) && response.request().method() === 'POST')
    await page.getByRole('button', { name: 'Kaydet', exact: true }).click()
    expect((await outputResponse).status()).toBe(200)
    const outputBody = (await outputPost).postDataJSON() as Record<string, unknown>
    expect(outputBody).toMatchObject({
      ownerKind: 'Counterparty',
      ownerId: beforeRoll.commercialContext.partyId,
      custodianKind: 'Company',
      custodianId: env.seed.companyId,
      kg: '300',
    })

    const afterOutput = await readOrder(env, arranged.orderId)
    await openOrder(page, arranged.orderId)
    await page.getByRole('radio', { name: 'Kullanılmayan girdi iadesi', exact: true }).check()
    const labelBeforeReturn = page.waitForResponse((response) => isLabelDetailRead(response, arranged.rollId))
    await page.getByRole('combobox', { name: 'Ana rulo', exact: true }).selectOption(arranged.rollId)
    const label = await (await labelBeforeReturn).json() as { labelVersion: string }
    await kgField(page).fill('200')
    await page.getByLabel('Zaman', { exact: true }).fill('2026-09-25T10:30')
    const returnPost = page.waitForRequest((request) =>
      request.url().endsWith(`/orders/${arranged.orderId}/input-returns`) && request.method() === 'POST')
    const returnResponse = page.waitForResponse((response) =>
      response.url().endsWith(`/orders/${arranged.orderId}/input-returns`) && response.request().method() === 'POST')
    await page.getByRole('button', { name: 'Kaydet', exact: true }).click()
    expect((await returnResponse).status()).toBe(200)
    expect((await returnPost).postDataJSON()).toMatchObject({
      expectedVersion: afterOutput.version,
      parentRollId: arranged.rollId,
      expectedParentLabelVersion: label.labelVersion,
      kg: '200',
    })

    await openOrder(page, arranged.orderId)
    const stored = await readOrder(env, arranged.orderId)
    expect((stored.rolls as Array<Record<string, unknown>>)[0]).toMatchObject({
      parentRollId: arranged.rollId,
      issuedKg: '500',
      returnedKg: '200',
      netIssuedKg: '300',
      outputsKg: { GoodProduct: '300' },
      unexplainedKg: '0',
      isFullyAccounted: true,
      commercialContext: beforeRoll.commercialContext,
    })
    await expect(issuedCell(page, arranged.rollId)).toHaveText('500')
    await expect(returnedCell(page, arranged.rollId)).toHaveText('200')
    const api = await apiAs(env, env.seed.recorderActor)
    try {
      const outputLabelResponse = await api.get(`/api/v1/inventory/labels/${outputBody.labelId as string}/detail`)
      const outputLabelText = await outputLabelResponse.text()
      expect(outputLabelResponse.status(), outputLabelText).toBe(200)
      const outputLabel = JSON.parse(outputLabelText) as {
        balances: { physicalKg: string; positions: Array<Record<string, unknown>> }
      }
      expect(outputLabel.balances.physicalKg).toBe('300')
      expect(outputLabel.balances.positions).toContainEqual(expect.objectContaining({
        ownerKind: 'Counterparty', ownerId: beforeRoll.commercialContext.partyId,
        custodianKind: 'Company', custodianId: env.seed.companyId,
        locationId: env.seed.output.locationId, quantityKg: '300',
      }))
    } finally {
      await api.dispose()
    }
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('an answer the browser never received is re-sent with the same key and recorded once', async ({ page, context }) => {
    const arranged = await arrangeOpenOrder(env, env.seed.rolls.replay)
    const rewritten = await asActor(context, env.seed.recorderActor)
    const answered = watchAnswers(page)
    // Withhold the DELIVERY of the first real answer: the server records the fact, the page learns nothing.
    let withheld: number | null = null
    await page.route(FACT_RE, async (route) => {
      if (withheld !== null) return route.fallback()
      const response = await route.fetch({
        headers: { ...route.request().headers(), 'x-development-user': env.seed.recorderActor },
      })
      withheld = response.status()
      await route.abort('failed')
    })

    await openOrder(page, arranged.orderId)
    await fillIssue(page, arranged.rollId, '100')
    await page.getByRole('button', { name: 'Kaydet', exact: true }).click()
    await expect(page.getByText('Sonuç bilinmiyor')).toBeVisible()
    expect(withheld).toBe(200)
    await expect(kgField(page)).toBeDisabled()

    const resend = page.waitForRequest((request) => FACT_RE.test(request.url()) && request.method() === 'POST')
    await page.getByRole('button', { name: 'Aynı olguyu yeniden gönder', exact: true }).click()
    await resend
    await expect(page.getByText('Daha önce kaydedilmiş')).toBeVisible()

    // Recorded ONCE: 100 kg issued, not 200.
    const stored = await readOrder(env, arranged.orderId)
    expect((stored.rolls as Array<{ issuedKg: string }>)[0].issuedKg).toBe('100')
    await expect(issuedCell(page, arranged.rollId)).toHaveText('100')
    // The withheld request went out through route.fetch with the actor header set explicitly; every
    // answer the PAGE received came through the identity rewrite.
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('a write that another writer overtook is a conflict, re-read, and a new command at the new version', async ({ page, context }) => {
    const arranged = await arrangeOpenOrder(env, env.seed.rolls.conflict)
    const rewritten = await asActor(context, env.seed.recorderActor)
    const answered = watchAnswers(page)

    await openOrder(page, arranged.orderId)
    await fillWip(page, arranged.rollId, '5')
    // Another writer issues this roll through the real API; the page still holds the old order version.
    const moved = await issueThroughApi(env, arranged, arranged.version, '10', '2026-09-18T09:25:00.000Z')
    const posts: Request[] = []
    page.on('request', (request) => {
      if (FACT_RE.test(request.url()) && request.method() === 'POST') posts.push(request)
    })
    await page.getByRole('button', { name: 'Kaydet', exact: true }).click()
    await expect(page.getByText(/Emir siz formu doldururken değişti/)).toBeVisible()
    await expect(issuedCell(page, arranged.rollId)).toHaveText('10')

    await page.getByRole('button', { name: 'Kaydet', exact: true }).click()
    await expect(page.getByText('Kaydedildi', { exact: true })).toBeVisible()
    await expect(issuedCell(page, arranged.rollId)).toHaveText('10')
    expect(posts).toHaveLength(2)
    expect(posts[1].postDataJSON()).toMatchObject({ expectedVersion: moved.version })
    expect(posts[1].headers()['idempotency-key']).not.toBe(posts[0].headers()['idempotency-key'])

    const stored = await readOrder(env, arranged.orderId)
    expect((stored.rolls as Array<{ issuedKg: string; workInProgressKg: string }>)[0]).toMatchObject({
      issuedKg: '10',
      workInProgressKg: '5',
    })
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('a reader sees the balances and no form, and sends no command', async ({ page, context }) => {
    // The order the suite fixture seeded through the real API (Suites/MFG): the seed path, end to end.
    const arranged = { orderId: env.seed.orderId, rollId: env.seed.rollId }
    const rewritten = await asActor(context, env.seed.readerActor)
    const answered = watchAnswers(page)
    const posts: Request[] = []
    page.on('request', (request) => {
      if (request.method() !== 'GET' && new URL(request.url()).pathname.startsWith('/api/v1/manufacturing')) posts.push(request)
    })

    await openOrder(page, arranged.orderId)
    await expect(rollRow(page, arranged.rollId)).toContainText('1000')
    await expect(page.getByRole('heading', { name: 'Gerçekleşme kaydı' })).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Kaydet', exact: true })).toHaveCount(0)
    // The read grant alone opens the GDC-BAL/1 panel too: the same order, read through the close's calculator.
    await expect(balanceRow(page, arranged.rollId).getByTestId('scrap-percent')).toContainText('Hesaplanamıyor')
    expect(posts).toEqual([])
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('the list opens one order, and the order it opens is the one the list named', async ({ page, context }) => {
    const arranged = await arrangeOpenOrder(env, env.seed.rolls.list)
    const rewritten = await asActor(context, env.seed.recorderActor)
    const answered = watchAnswers(page)

    const listed = page.waitForResponse((response) => LIST_RE.test(response.url()) && response.request().method() === 'GET')
    await page.goto('/uretim/emirler')
    await expect(page.getByRole('heading', { name: 'Üretim emirleri' })).toBeVisible()
    expect((await listed).status()).toBe(200)

    const row = page.getByRole('row', { name: new RegExp(arranged.orderId.slice(-8)) })
    await expect(row).toContainText('Açık')
    await expect(row).toContainText('1000')
    // The list carries the stored summary only: no balance column, and no invented label.
    await expect(page.getByRole('region', { name: 'Kütle dengesi (GDC-BAL/1)' })).toHaveCount(0)

    await row.getByRole('link').click()
    await expect(page.getByRole('heading', { name: 'Üretim emri' })).toBeVisible()
    expect(new URL(page.url()).pathname).toBe(`/uretim/emir/${arranged.orderId}`)
    await expect(rollRow(page, arranged.rollId)).toContainText('1000')
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('a closer declares fire, then closes an out-of-tolerance order with a reasoned exception', async ({ page, context }) => {
    // Policy-backed close through the real stack: 1000 issued, 600 declared → explicit fire is still required;
    // the remaining 400 is declared as Scrap → the disposable 5% profile requires the separately authorized,
    // reasoned exception. The production Settings catalog stays empty.
    const arranged = await arrangeOpenOrder(env, env.seed.rolls.close)
    const issued = await issueThroughApi(env, arranged, arranged.version, '1000')
    const partial = await declareThroughApi(env, arranged, issued.version as number, 'GoodProduct', '600')
    const rewritten = await asActor(context, env.seed.closerActor)
    const answered = watchAnswers(page)

    await openOrder(page, arranged.orderId)
    // The closer records no fact: the close is its own authority.
    await expect(page.getByRole('heading', { name: 'Gerçekleşme kaydı' })).toHaveCount(0)
    await expect(page.getByRole('heading', { name: 'Emri kapat' })).toBeVisible()

    const refusedPost = page.waitForRequest((request) => CLOSE_RE.test(request.url()) && request.method() === 'POST')
    await page.getByRole('button', { name: 'Emri kapat', exact: true }).click()
    await page.getByRole('button', { name: 'Evet, emri kapat', exact: true }).click()
    const sent = await refusedPost
    expect(sent.postDataJSON()).toMatchObject({ expectedVersion: partial.version, exceptionReason: null })
    expect(sent.headers()['idempotency-key']).toMatch(/^[0-9a-f-]{36}$/)
    await expect(page.getByTestId('close-refusal')).toContainText('açıklanamayan pozitif fark')
    expect((await readOrder(env, arranged.orderId)).status).toBe('Open')

    // The missing 400 kg is declared through the real API. The normal close then receives the server's
    // approval-required result; only the dedicated capability exposes the reasoned follow-up.
    const balanced = await declareThroughApi(env, arranged, partial.version as number, 'Scrap', '400')
    await page.reload()
    await expect(page.getByRole('heading', { name: 'Emri kapat' })).toBeVisible()
    const approvalPost = page.waitForRequest((request) => CLOSE_RE.test(request.url()) && request.method() === 'POST')
    await page.getByRole('button', { name: 'Emri kapat', exact: true }).click()
    await page.getByRole('button', { name: 'Evet, emri kapat', exact: true }).click()
    const approvalRequest = await approvalPost
    expect(approvalRequest.postDataJSON()).toMatchObject({ expectedVersion: balanced.version, exceptionReason: null })
    await expect(page.getByTestId('close-exception-approval')).toBeVisible()

    const reason = 'Gerçek fire kaydı teknik olarak doğrulandı.'
    await page.getByLabel('Onay gerekçesi').fill(reason)
    const acceptedPost = page.waitForRequest((request) => CLOSE_RE.test(request.url()) && request.method() === 'POST'
      && request.postDataJSON()?.exceptionReason === reason)
    await page.getByRole('button', { name: 'Gerekçeyle onayla ve kapat' }).click()
    const acceptedRequest = await acceptedPost
    expect(acceptedRequest.postDataJSON()).toMatchObject({ expectedVersion: balanced.version, exceptionReason: reason })
    expect(acceptedRequest.headers()['idempotency-key']).not.toBe(approvalRequest.headers()['idempotency-key'])
    await expect(page.getByText('Emir kapatıldı', { exact: true })).toBeVisible()
    await expect(page.getByTestId('order-already-closed')).toBeVisible()

    const stored = await readOrder(env, arranged.orderId)
    expect(stored.status).toBe('Closed')
    expect(stored.closedAtUtc).not.toBeNull()
    expectEveryAnswerRewritten(answered, rewritten)
  })
})
