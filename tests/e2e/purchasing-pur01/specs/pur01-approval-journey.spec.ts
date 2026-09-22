import { expect, test, type Page } from '@playwright/test'
import { readEnvironment } from '../src/env'

const env = readEnvironment()
const { approverActor, readerActor, companyId, otherCompanyId, orderId, lineId } = env.seed
const ORDERS_PATH = '/api/v1/purchasing/orders'
const LIST_ROUTE = '/satinalma/siparisler'
const DEVELOPMENT_USER_HEADER = 'X-Development-User'

interface OrderSummary {
  readonly id: string
  readonly status: 'Draft' | 'Approved'
  readonly version: string
}

interface OrderDetail {
  readonly order: OrderSummary
  readonly lines: ReadonlyArray<{ readonly lineId: string }>
}

interface ApprovalResponse {
  readonly detail: OrderDetail
  readonly isReplay: boolean
  readonly originalCorrelationId: string
}

async function signInAs(page: Page, actor: string): Promise<void> {
  await page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: actor })
}

async function readOrder(page: Page, actor: string, company = companyId): Promise<OrderDetail> {
  const response = await page.request.get(`${env.apiBaseUrl}${ORDERS_PATH}/${orderId}`, {
    headers: { [DEVELOPMENT_USER_HEADER]: actor, 'X-Company-Id': company },
  })
  expect(response.status()).toBe(200)
  return (await response.json()) as OrderDetail
}

function seededRow(page: Page) {
  return page.getByRole('row').filter({ hasText: `…${orderId.slice(-8)}` })
}

test('PUR01 approves through the real API, reopens the same record and replays the same command; reader and company fences hold', async ({ page }) => {
  await signInAs(page, approverActor)
  const before = await readOrder(page, approverActor)
  expect([before.order.id, before.order.status, before.order.version]).toEqual([orderId, 'Draft', '2'])
  expect(before.lines.map((line) => line.lineId)).toEqual([lineId])

  await page.goto(LIST_ROUTE)
  await expect(page.getByRole('heading', { level: 1, name: 'Satın alma siparişleri' })).toBeVisible({ timeout: 30_000 })
  await expect(seededRow(page)).toContainText('Taslak')
  await seededRow(page).getByRole('link', { name: `…${orderId.slice(-8)}` }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Satın alma siparişi' })).toBeVisible({ timeout: 30_000 })
  await expect(page.getByText(orderId, { exact: true })).toBeVisible()
  await expect(page.getByText('Taslak', { exact: true })).toBeVisible()

  let withheldStatus: number | null = null
  const approvalPath = `${ORDERS_PATH}/${orderId}/approval`
  await page.route(`**${approvalPath}`, async (route) => {
    if (route.request().method() !== 'POST' || withheldStatus !== null) return route.fallback()
    const response = await route.fetch({
      headers: { ...route.request().headers(), [DEVELOPMENT_USER_HEADER.toLowerCase()]: approverActor },
    })
    withheldStatus = response.status()
    await route.abort('failed')
  })

  await page.getByRole('button', { name: 'Onayla' }).click()
  await expect(page.getByText('Sonuç bilinmiyor. Yeni işlem başlatmadan aynı anahtar ve sürümle yeniden gönderin.')).toBeVisible()
  expect(withheldStatus, 'the first approval reached and committed in the real API').toBe(200)

  const committed = await readOrder(page, approverActor)
  const committedVersion = (BigInt(before.order.version) + 1n).toString()
  expect([committed.order.id, committed.order.status, committed.order.version]).toEqual([orderId, 'Approved', committedVersion])

  await page.goto(LIST_ROUTE)
  await expect(seededRow(page)).toContainText('Onaylandı')
  await seededRow(page).getByRole('link', { name: `…${orderId.slice(-8)}` }).click()
  await expect(page.getByText(orderId, { exact: true })).toBeVisible()
  await expect(page.getByText('Onaylandı', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Aynı onayı yeniden gönder' })).toBeVisible()

  const replayAnswer = page.waitForResponse((response) =>
    response.request().method() === 'POST' && new URL(response.url()).pathname === approvalPath)
  await page.getByRole('button', { name: 'Aynı onayı yeniden gönder' }).click()
  const replay = await replayAnswer
  expect(replay.status()).toBe(200)
  expect(replay.headers()['idempotency-replayed']).toBe('true')
  const replayBody = (await replay.json()) as ApprovalResponse
  expect(replayBody.isReplay).toBe(true)
  expect([replayBody.detail.order.id, replayBody.detail.order.status, replayBody.detail.order.version])
    .toEqual([orderId, 'Approved', committedVersion])
  await expect(page.getByText('Önceki onay yanıtı yeniden gösterildi.')).toBeVisible()

  await page.goto(LIST_ROUTE)
  await seededRow(page).getByRole('link', { name: `…${orderId.slice(-8)}` }).click()
  await expect(page.getByText(orderId, { exact: true })).toBeVisible()
  await expect(page.getByText('Onaylandı', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Onayla' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Aynı onayı yeniden gönder' })).toHaveCount(0)
  const afterReplay = await readOrder(page, approverActor)
  expect([afterReplay.order.status, afterReplay.order.version]).toEqual(['Approved', committedVersion])

  await signInAs(page, readerActor)
  await page.goto(LIST_ROUTE)
  await expect(seededRow(page)).toContainText('Onaylandı')
  await seededRow(page).getByRole('link', { name: `…${orderId.slice(-8)}` }).click()
  await expect(page.getByText(orderId, { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Onayla' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Aynı onayı yeniden gönder' })).toHaveCount(0)

  const denied = await page.request.post(`${env.apiBaseUrl}${approvalPath}`, {
    headers: {
      [DEVELOPMENT_USER_HEADER]: readerActor,
      'X-Company-Id': companyId,
      'X-Correlation-Id': 'browser-suite-pur01-reader-denied',
      'Idempotency-Key': 'browser-suite:pur01:reader-denied',
      'Content-Type': 'application/json',
    },
    data: { expectedVersion: committedVersion },
  })
  expect(denied.status()).toBe(403)
  const afterDenied = await readOrder(page, readerActor)
  expect([afterDenied.order.status, afterDenied.order.version]).toEqual(['Approved', committedVersion])

  const elsewhere = await page.request.get(`${env.apiBaseUrl}${ORDERS_PATH}/${orderId}`, {
    headers: { [DEVELOPMENT_USER_HEADER]: approverActor, 'X-Company-Id': otherCompanyId },
  })
  expect([403, 404]).toContain(elsewhere.status())
  const capabilities = await page.request.get(`${env.apiBaseUrl}/api/v1/shell/capabilities`, {
    headers: { [DEVELOPMENT_USER_HEADER]: approverActor, 'X-Company-Id': otherCompanyId },
  })
  if (capabilities.status() === 200) {
    const snapshot = (await capabilities.json()) as { readonly capabilities?: unknown }
    const published = Array.isArray(snapshot.capabilities) ? snapshot.capabilities as string[] : []
    expect(published.filter((id) => id.startsWith('purchasing.'))).toEqual([])
  } else {
    expect([403, 404]).toContain(capabilities.status())
  }
})
