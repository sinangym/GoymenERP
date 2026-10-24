import { expect, test, type Page, type Response, type Route } from '@playwright/test'
import sql from 'mssql'
import { readEnvironment, readSqlConnection } from '../src/env'
import { readReceiptEffects, type ReceiptEffects } from '../src/receiptEvidence'

/**
 * PUR01-GR-REFENCE (BLG-01) on the disposable normal stack. NOT_RUN until a BROWSER-CI run records it.
 *
 * The receiver records a goods receipt whose answer is lost, the shell is re-fenced by a 403, and the order is
 * entered again under a NEW capability generation, in the same tab, session and company. The re-entry must find the
 * same unknown attempt, send nothing on its own, and one retry must replay with the same Idempotency-Key and body:
 * exactly one acceptance, lot, label, movement and Inventory command (read-only SQL).
 *
 * Three answers are INJECTED, and only these; every request still reaches the real host except the 403:
 *   1. the first receipt POST is forwarded to the server and its answer dropped (the server records it);
 *   2. the next order-list read is answered 403 (the re-fence trigger);
 *   3. later capability snapshots are the real ones with a suffixed `generation` (a fresh grant, same capabilities).
 * The page is never reloaded: the pending attempt is in-memory by design, so a reload is a different journey.
 */
const env = readEnvironment()
const sqlConnection = readSqlConnection()
const { creatorActor, approverActor, receiverActor, companyId, supplierPartyId, companyAccountId, productId, productVersion,
  productUnitId } = env.seed

const DEVELOPMENT_USER_HEADER = 'X-Development-User'
const ORDERS_PATH = '/api/v1/purchasing/orders'
const CAPABILITIES_PATH = '/api/v1/shell/capabilities'
const LIST_ROUTE = '/satinalma/siparisler'
const EFFECTIVE_ON = '2026-09-18'
const ORDER_LINE_ID = '7b1c0f2e-0000-4a00-8000-000000000301'
const ORDERED = '10'
const ACCEPTED = '3'
const MEASURED_KG = '3.2'
// Deliberately outside the journey spec's `SYNPUR01-LABEL-%` count.
const LABEL_CODE = 'SYNPUR01-REFENCE-1'
const UNKNOWN = 'Sonuç bilinmiyor; kabul kaydedilmiş olabilir. Aynı anahtar ve içerikle yeniden deneyin. Yeni kabul başlatılmadı.'
const REPLAYED = 'Önceki mal kabulü yeniden gösterildi; ikinci stok girişi oluşturulmadı.'
const ONCE: ReceiptEffects = {
  acceptancesOnOrder: 1, acceptance: 1, lots: 1, labelsByIdOrCode: 1, receiptMovements: 1,
  receiptMovementById: 1, labelMovementLines: 1, inventoryCommands: 1,
}

interface SentReceipt { readonly acceptanceId: string; readonly lotId: string; readonly labelId: string; readonly movementId: string }

function apiHeaders(actor: string): Record<string, string> {
  return { [DEVELOPMENT_USER_HEADER]: actor, 'X-Company-Id': companyId }
}

function isCall(response: Response, method: 'GET' | 'POST', path: string): boolean {
  return response.request().method() === method && new URL(response.url()).pathname === path
}

/** Precondition only: one Draft through the real create command, approved through the real approval command. */
async function arrangeApprovedOrder(page: Page): Promise<string> {
  const created = await page.request.post(`${env.apiBaseUrl}${ORDERS_PATH}`, {
    headers: { ...apiHeaders(creatorActor), 'Idempotency-Key': 'browser-suite:pur01:refence-order', 'Content-Type': 'application/json' },
    data: {
      supplierPartyId, companyAccountId, sourceRequestId: null, effectiveOn: EFFECTIVE_ON,
      lines: [{ lineId: ORDER_LINE_ID, productId, expectedProductVersion: productVersion, unitId: productUnitId,
        quantity: ORDERED, sourceRequestLineId: null }],
    },
  })
  expect(created.status()).toBe(201)
  const draft = ((await created.json()) as { readonly detail: { readonly order: { readonly id: string; readonly version: string } } }).detail
  const approval = await page.request.post(`${env.apiBaseUrl}${ORDERS_PATH}/${draft.order.id}/approval`, {
    headers: { ...apiHeaders(approverActor), 'Idempotency-Key': 'browser-suite:pur01:refence-order-approval',
      'Content-Type': 'application/json' },
    data: { expectedVersion: draft.order.version },
  })
  expect(approval.status()).toBe(200)
  return draft.order.id
}

test('PUR01-GR-REFENCE an unknown receipt survives a 403 re-fence and a new capability generation; one retry, one receipt', async ({ page }) => {
  test.setTimeout(180_000)
  const pool = await new sql.ConnectionPool(sqlConnection).connect()
  try {
    const orderId = await arrangeApprovedOrder(page)
    const recordPath = `${ORDERS_PATH}/${orderId}/lines/${ORDER_LINE_ID}/goods-receipts`
    const posts: { key: string | null; body: string | null }[] = []
    let dropNextReceiptAnswer = true
    let forbidNextList = false
    let generationSuffix: string | null = null

    await page.route((url) => url.pathname === recordPath, async (route: Route) => {
      if (route.request().method() !== 'POST') return route.continue()
      posts.push({ key: await route.request().headerValue('Idempotency-Key'), body: route.request().postData() })
      if (!dropNextReceiptAnswer) return route.continue()
      dropNextReceiptAnswer = false
      // (1) The server receives and records it; the browser never sees the answer.
      await route.fetch()
      await route.abort('connectionreset')
    })
    await page.route((url) => url.pathname === ORDERS_PATH, async (route: Route) => {
      if (!forbidNextList || route.request().method() !== 'GET') return route.continue()
      forbidNextList = false
      generationSuffix = '-pur01-refence'
      // (2) The re-fence trigger.
      await route.fulfill({ status: 403, contentType: 'application/json', body: JSON.stringify({ code: 'forbidden' }) })
    })
    await page.route((url) => url.pathname === CAPABILITIES_PATH, async (route: Route) => {
      if (generationSuffix === null) return route.continue()
      // (3) The real snapshot, under a new generation.
      const real = await route.fetch()
      const snapshot = (await real.json()) as { generation: string }
      await route.fulfill({ response: real, json: { ...snapshot, generation: `${snapshot.generation}${generationSuffix}` } })
    })

    await page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: receiverActor })
    await page.goto(LIST_ROUTE)
    await expect(page.getByRole('heading', { level: 1, name: 'Satın alma siparişleri' })).toBeVisible({ timeout: 30_000 })
    await page.getByRole('link', { name: `…${orderId.slice(-8)}` }).first().click()
    await expect(page.getByRole('heading', { level: 1, name: 'Satın alma siparişi' })).toBeVisible({ timeout: 30_000 })

    const panel = page.getByRole('region', { name: 'Mal kabulü', exact: true })
    await expect(panel.getByLabel('Kabul lokasyonu').locator('option').nth(1)).toBeAttached({ timeout: 30_000 })
    await panel.getByLabel('Kabul lokasyonu').selectOption({ index: 1 })
    await panel.getByLabel('Kabul miktarı (sipariş birimi)').fill(ACCEPTED)
    await panel.getByLabel('Tartılan miktar (kg)').fill(MEASURED_KG)
    await panel.getByLabel('Etiket kodu').fill(LABEL_CODE)
    await panel.getByRole('button', { name: 'Mal kabulünü kaydet', exact: true }).click()
    await expect(panel.getByText(UNKNOWN, { exact: true })).toBeVisible({ timeout: 30_000 })
    expect(posts).toHaveLength(1)
    const sent = JSON.parse(posts[0].body ?? '{}') as SentReceipt
    const identity = { companyId, orderId, ...sent, labelCode: LABEL_CODE }
    // The server did record the first send.
    await expect.poll(() => readReceiptEffects(pool, identity), { timeout: 30_000 }).toEqual(ONCE)

    // Back to the list in the same tab (client-side history, no reload): its read is refused, the gate closes.
    forbidNextList = true
    const refused = page.waitForResponse((response) => response.request().method() === 'GET'
      && new URL(response.url()).pathname === ORDERS_PATH && response.status() === 403)
    await page.goBack()
    await refused
    await expect(page.getByRole('heading', { level: 1, name: 'Satın alma siparişleri' })).toHaveCount(0, { timeout: 30_000 })

    // Forward to the order: a fresh entry under the new capability generation.
    const freshSnapshot = page.waitForResponse((response) => isCall(response, 'GET', CAPABILITIES_PATH))
    await page.goForward()
    await freshSnapshot
    await expect(page.getByRole('heading', { level: 1, name: 'Satın alma siparişi' })).toBeVisible({ timeout: 30_000 })
    await expect(panel.getByText(UNKNOWN, { exact: true })).toBeVisible({ timeout: 30_000 })
    await expect(panel.getByLabel('Etiket kodu')).toHaveValue(LABEL_CODE)
    // Recalling is not retrying.
    expect(posts).toHaveLength(1)

    const replay = page.waitForResponse((response) => isCall(response, 'POST', recordPath))
    await panel.getByRole('button', { name: 'Aynı kabulü yeniden gönder', exact: true }).click()
    const replayed = await replay
    expect(replayed.status()).toBe(201)
    expect(replayed.headers()['idempotency-replayed']).toBe('true')
    expect(posts).toHaveLength(2)
    expect(posts[1]).toEqual(posts[0])
    await expect(panel.getByText(REPLAYED)).toBeVisible({ timeout: 30_000 })
    expect(await readReceiptEffects(pool, identity)).toEqual(ONCE)
  } finally {
    await pool.close()
  }
})
