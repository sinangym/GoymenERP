import { expect, test, type Page } from '@playwright/test'
import { readEnvironment } from '../src/env'

const env = readEnvironment()
const {
  creatorActor, approverActor, readerActor, companyId, otherCompanyId, orderId, lineId,
  supplierPartyId, companyAccountId, productId, productVersion, productUnitId,
  productUnitProfileId, productUnitProfileVersion, productCode, productName,
} = env.seed
const ORDERS_PATH = '/api/v1/purchasing/orders'
const REQUESTS_PATH = '/api/v1/purchasing/requests'
const LIST_ROUTE = '/satinalma/siparisler'
const CREATE_ROUTE = `${LIST_ROUTE}/yeni`
const REQUEST_LIST_ROUTE = '/satinalma/talepler'
const REQUEST_CREATE_ROUTE = `${REQUEST_LIST_ROUTE}/yeni`
const DEVELOPMENT_USER_HEADER = 'X-Development-User'

interface OrderSummary {
  readonly id: string
  readonly companyId: string
  readonly status: 'Draft' | 'Approved'
  readonly version: string
  readonly supplierPartyId: string
  readonly companyAccountId: string
  readonly effectiveOn: string
}

interface OrderDetail {
  readonly order: OrderSummary
  readonly lines: ReadonlyArray<{
    readonly lineId: string
    readonly productId: string
    readonly productVersion: string
    readonly unitId: string
    readonly ordered: string
  }>
}

interface ApprovalResponse {
  readonly detail: OrderDetail
  readonly isReplay: boolean
  readonly originalCorrelationId: string
}

interface RequestSummary {
  readonly id: string
  readonly companyId: string
  readonly status: 'Draft' | 'Submitted'
  readonly version: string
  readonly effectiveOn: string
}

interface RequestDetail {
  readonly request: RequestSummary
  readonly lines: ReadonlyArray<{
    readonly lineId: string
    readonly productId: string
    readonly productVersion: string
    readonly unitId: string
    readonly quantity: string
  }>
}

interface RequestResponse {
  readonly detail: RequestDetail
  readonly isReplay: boolean
  readonly originalCorrelationId: string
}

async function signInAs(page: Page, actor: string): Promise<void> {
  await page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: actor })
}

async function readOrder(page: Page, actor: string, targetOrderId = orderId, company = companyId): Promise<OrderDetail> {
  const response = await page.request.get(`${env.apiBaseUrl}${ORDERS_PATH}/${targetOrderId}`, {
    headers: { [DEVELOPMENT_USER_HEADER]: actor, 'X-Company-Id': company },
  })
  expect(response.status()).toBe(200)
  return (await response.json()) as OrderDetail
}

async function readOrders(page: Page, actor: string): Promise<readonly OrderSummary[]> {
  const response = await page.request.get(`${env.apiBaseUrl}${ORDERS_PATH}`, {
    headers: { [DEVELOPMENT_USER_HEADER]: actor, 'X-Company-Id': companyId },
  })
  expect(response.status()).toBe(200)
  const body = (await response.json()) as { readonly items: readonly OrderSummary[]; readonly nextCursor: string | null }
  expect(body.nextCursor).toBeNull()
  return body.items
}

async function readRequest(page: Page, actor: string, requestId: string, company = companyId): Promise<RequestDetail> {
  const response = await page.request.get(`${env.apiBaseUrl}${REQUESTS_PATH}/${requestId}`, {
    headers: { [DEVELOPMENT_USER_HEADER]: actor, 'X-Company-Id': company },
  })
  expect(response.status()).toBe(200)
  return (await response.json()) as RequestDetail
}

async function readRequests(page: Page, actor: string): Promise<readonly RequestSummary[]> {
  const response = await page.request.get(`${env.apiBaseUrl}${REQUESTS_PATH}`, {
    headers: { [DEVELOPMENT_USER_HEADER]: actor, 'X-Company-Id': companyId },
  })
  expect(response.status()).toBe(200)
  const body = (await response.json()) as { readonly items: readonly RequestSummary[]; readonly nextCursor: string | null }
  expect(body.nextCursor).toBeNull()
  return body.items
}

function seededRow(page: Page) {
  return page.getByRole('row').filter({ hasText: `…${orderId.slice(-8)}` })
}


test('P01 saves one real Product-backed request, closes the form, reopens the same record and submits it; fences hold', async ({ page }) => {
  await signInAs(page, creatorActor)
  const before = await readRequests(page, creatorActor)

  await page.goto(REQUEST_LIST_ROUTE)
  await expect(page.getByRole('heading', { level: 1, name: 'Satın alma talepleri' })).toBeVisible({ timeout: 30_000 })
  await page.getByRole('link', { name: 'Yeni talep', exact: true }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Yeni satın alma talebi' })).toBeVisible()

  await page.getByLabel('İş tarihi').fill('2026-09-18')
  await page.getByLabel('Ürün ara').fill(productCode)
  const candidateAnswer = page.waitForResponse((response) => {
    const url = new URL(response.url())
    return response.request().method() === 'GET' && url.pathname === '/api/v1/purchasing/references/products'
  })
  await page.getByRole('button', { name: 'Ara', exact: true }).click()
  const candidates = await candidateAnswer
  expect(candidates.status()).toBe(200)
  expect(await candidates.json()).toMatchObject({
    items: [{
      productId, productVersion, code: productCode, name: productName, unitId: productUnitId,
      profileId: productUnitProfileId, profileVersion: productUnitProfileVersion,
    }],
  })
  await page.getByLabel('Ürün 1').selectOption(productId)
  await page.getByLabel('Miktar').fill('7')

  const createAnswer = page.waitForResponse((response) =>
    response.request().method() === 'POST' && new URL(response.url()).pathname === REQUESTS_PATH)
  await page.getByRole('button', { name: 'Taslak talebi kaydet' }).click()
  const createResponse = await createAnswer
  expect(createResponse.status()).toBe(201)
  const created = (await createResponse.json()) as RequestResponse
  const requestId = created.detail.request.id
  const draftVersion = created.detail.request.version
  expect(createResponse.headers()['location']).toContain(`${REQUESTS_PATH}/${requestId}`)
  expect(created.isReplay).toBe(false)
  expect(/^[1-9]\d*$/.test(draftVersion)).toBe(true)
  expect(created.detail.request).toMatchObject({
    id: requestId, companyId, status: 'Draft', version: draftVersion, effectiveOn: '2026-09-18',
  })
  expect(created.detail.lines).toHaveLength(1)
  expect(created.detail.lines[0]).toMatchObject({
    productId, productVersion, unitId: productUnitId, quantity: '7',
  })
  await expect(page.getByRole('heading', { level: 1, name: 'Satın alma talebi kaydedildi' })).toBeVisible()

  await page.getByRole('link', { name: 'Listeye dön ve formu kapat' }).click()
  const createdRow = page.getByRole('row').filter({ hasText: `…${requestId.slice(-8)}` })
  await expect(createdRow).toContainText('Taslak')
  const afterCreate = await readRequests(page, creatorActor)
  expect(afterCreate).toHaveLength(before.length + 1)
  expect(afterCreate.filter((item) => item.id === requestId)).toHaveLength(1)

  await createdRow.getByRole('link', { name: `…${requestId.slice(-8)}` }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Satın alma talebi' })).toBeVisible()
  await expect(page.getByText(requestId, { exact: true })).toBeVisible()
  const reopened = await readRequest(page, creatorActor, requestId)
  expect(reopened.request).toMatchObject({ id: requestId, status: 'Draft', version: draftVersion })
  expect(reopened.lines[0]).toMatchObject({ productId, productVersion, unitId: productUnitId, quantity: '7' })

  const submissionPath = `${REQUESTS_PATH}/${requestId}/submission`
  const submitAnswer = page.waitForResponse((response) =>
    response.request().method() === 'POST' && new URL(response.url()).pathname === submissionPath)
  await page.getByRole('button', { name: 'Gönder', exact: true }).click()
  const submitResponse = await submitAnswer
  expect(submitResponse.status()).toBe(200)
  const submitted = (await submitResponse.json()) as RequestResponse
  const submittedVersion = (BigInt(draftVersion) + 1n).toString()
  expect([submitted.detail.request.id, submitted.detail.request.status, submitted.detail.request.version])
    .toEqual([requestId, 'Submitted', submittedVersion])
  await expect(page.getByText('Talep gönderildi ve yeniden okundu.')).toBeVisible()
  await expect(page.getByText('Gönderildi', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Gönder', exact: true })).toHaveCount(0)
  const afterSubmit = await readRequest(page, creatorActor, requestId)
  expect([afterSubmit.request.id, afterSubmit.request.status, afterSubmit.request.version])
    .toEqual([requestId, 'Submitted', submittedVersion])

  await signInAs(page, readerActor)
  const readerCapabilities = await page.request.get(`${env.apiBaseUrl}/api/v1/shell/capabilities`, {
    headers: { [DEVELOPMENT_USER_HEADER]: readerActor, 'X-Company-Id': companyId },
  })
  expect(readerCapabilities.status()).toBe(200)
  const snapshot = (await readerCapabilities.json()) as { readonly capabilities?: unknown }
  const published = Array.isArray(snapshot.capabilities) ? snapshot.capabilities as string[] : []
  expect(published).toContain('purchasing.requests.list')
  expect(published).toContain('purchasing.requests.detail')
  expect(published).not.toContain('purchasing.requests.create')
  expect(published).not.toContain('purchasing.requests.submit')

  await page.goto(REQUEST_LIST_ROUTE)
  const readerRow = page.getByRole('row').filter({ hasText: `…${requestId.slice(-8)}` })
  await expect(readerRow).toContainText('Gönderildi')
  await expect(page.getByRole('link', { name: 'Yeni talep', exact: true })).toHaveCount(0)
  await readerRow.getByRole('link', { name: `…${requestId.slice(-8)}` }).click()
  await expect(page.getByText(requestId, { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Gönder', exact: true })).toHaveCount(0)

  const denied = await page.request.post(`${env.apiBaseUrl}${submissionPath}`, {
    headers: {
      [DEVELOPMENT_USER_HEADER]: readerActor, 'X-Company-Id': companyId,
      'Idempotency-Key': 'browser-suite:pur01:reader-submit-denied', 'Content-Type': 'application/json',
    },
    data: { expectedVersion: submittedVersion },
  })
  expect(denied.status()).toBe(403)
  const afterDenied = await readRequest(page, readerActor, requestId)
  expect([afterDenied.request.status, afterDenied.request.version]).toEqual(['Submitted', submittedVersion])

  const elsewhere = await page.request.get(`${env.apiBaseUrl}${REQUESTS_PATH}/${requestId}`, {
    headers: { [DEVELOPMENT_USER_HEADER]: creatorActor, 'X-Company-Id': otherCompanyId },
  })
  expect([403, 404]).toContain(elsewhere.status())

  const linkedOrderCreate = await page.request.post(`${env.apiBaseUrl}${ORDERS_PATH}`, {
    headers: {
      [DEVELOPMENT_USER_HEADER]: creatorActor, 'X-Company-Id': companyId,
      'Idempotency-Key': `browser-suite:pur01:source-order:${requestId}`, 'Content-Type': 'application/json',
    },
    data: {
      supplierPartyId, companyAccountId, sourceRequestId: requestId, effectiveOn: '2026-09-18',
      lines: [{
        lineId: '7b1c0f2e-0000-4a00-8000-000000000098', productId,
        expectedProductVersion: productVersion, unitId: productUnitId, quantity: '5',
        sourceRequestLineId: created.detail.lines[0].lineId,
      }],
    },
  })
  expect(linkedOrderCreate.status()).toBe(201)
  const linkedOrder = (await linkedOrderCreate.json()) as ApprovalResponse
  const linkedOrderId = linkedOrder.detail.order.id
  const linkedOrderVersion = linkedOrder.detail.order.version
  expect(linkedOrder.detail.order).toMatchObject({ id: linkedOrderId, status: 'Draft' })

  const linkedApproval = await page.request.post(`${env.apiBaseUrl}${ORDERS_PATH}/${linkedOrderId}/approval`, {
    headers: {
      [DEVELOPMENT_USER_HEADER]: approverActor, 'X-Company-Id': companyId,
      'Idempotency-Key': `browser-suite:pur01:source-order-approval:${requestId}`,
      'Content-Type': 'application/json',
    },
    data: { expectedVersion: linkedOrderVersion },
  })
  expect(linkedApproval.status()).toBe(200)

  await signInAs(page, approverActor)
  const approverCapabilities = await page.request.get(`${env.apiBaseUrl}/api/v1/shell/capabilities`, {
    headers: { [DEVELOPMENT_USER_HEADER]: approverActor, 'X-Company-Id': companyId },
  })
  expect(approverCapabilities.status()).toBe(200)
  const approverSnapshot = (await approverCapabilities.json()) as { readonly capabilities?: unknown }
  const approverPublished = Array.isArray(approverSnapshot.capabilities)
    ? approverSnapshot.capabilities as string[]
    : []
  expect(approverPublished).not.toContain('purchasing.requests.detail')
  await page.goto(`${LIST_ROUTE}/${linkedOrderId}`)
  await expect(page.getByText(linkedOrderId, { exact: true })).toBeVisible()
  await expect(page.getByText('Onaylandı', { exact: true })).toBeVisible()
  await expect(page.getByRole('link', { name: requestId, exact: true })).toHaveCount(0)
  await expect(page.getByText(requestId, { exact: true })).toBeVisible()

  await signInAs(page, readerActor)
  await page.goto(`${LIST_ROUTE}/${linkedOrderId}`)
  await page.getByRole('link', { name: requestId, exact: true }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Satın alma talebi' })).toBeVisible()
  await expect(page.getByText(requestId, { exact: true })).toBeVisible()
  await page.goBack()
  await expect(page.getByRole('heading', { level: 1, name: 'Satın alma siparişi' })).toBeVisible()
  await expect(page.getByText(linkedOrderId, { exact: true })).toBeVisible()
  await expect(page.getByText('Onaylandı', { exact: true })).toBeVisible()
})


test('P04 creates one real order, closes the form, reopens that record and uses it in approval; create fences hold', async ({ page }) => {
  await signInAs(page, creatorActor)
  const before = await readOrders(page, creatorActor)
  expect(before.filter((item) => item.id === orderId)).toHaveLength(1)

  await page.goto(LIST_ROUTE)
  await expect(page.getByRole('heading', { level: 1, name: 'Satın alma siparişleri' })).toBeVisible({ timeout: 30_000 })
  await page.getByRole('link', { name: 'Yeni', exact: true }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Yeni satın alma siparişi' })).toBeVisible()

  await page.getByLabel('Tedarikçi cari hesabı').selectOption(`${supplierPartyId}|${companyAccountId}`)
  await page.getByLabel('İş tarihi').fill('2026-09-18')
  await page.getByLabel('Ürün ara').fill(productCode)
  const candidateAnswer = page.waitForResponse((response) => {
    const url = new URL(response.url())
    return response.request().method() === 'GET' && url.pathname === '/api/v1/purchasing/references/products'
  })
  await page.getByRole('group', { name: 'Sipariş satırı' }).getByRole('button', { name: 'Ara' }).click()
  const candidates = await candidateAnswer
  expect(candidates.status()).toBe(200)
  expect(await candidates.json()).toMatchObject({
    items: [{
      productId, productVersion, code: productCode, name: productName, unitId: productUnitId,
      profileId: productUnitProfileId, profileVersion: productUnitProfileVersion,
    }],
  })
  await page.getByLabel('Ürün ve alış birimi').selectOption(productId)
  await page.getByLabel('Miktar').fill('5')

  const createAnswer = page.waitForResponse((response) =>
    response.request().method() === 'POST' && new URL(response.url()).pathname === ORDERS_PATH)
  await page.getByRole('button', { name: 'Taslak siparişi kaydet' }).click()
  const createResponse = await createAnswer
  expect(createResponse.status()).toBe(201)
  const created = (await createResponse.json()) as ApprovalResponse
  const createdId = created.detail.order.id
  const createdVersion = created.detail.order.version
  expect(createResponse.headers()['location']).toContain(`${ORDERS_PATH}/${createdId}`)
  expect(created.isReplay).toBe(false)
  expect(/^[1-9]\d*$/.test(createdVersion)).toBe(true)
  expect(created.detail.order).toMatchObject({
    id: createdId, companyId, status: 'Draft', version: createdVersion, supplierPartyId, companyAccountId,
    effectiveOn: '2026-09-18',
  })
  expect(created.detail.lines).toHaveLength(1)
  expect(created.detail.lines[0]).toMatchObject({
    productId, productVersion, unitId: productUnitId, ordered: '5',
  })
  await expect(page.getByRole('heading', { level: 1, name: 'Satın alma siparişi kaydedildi' })).toBeVisible()

  await page.getByRole('link', { name: 'Listeye dön ve formu kapat' }).click()
  const createdRow = page.getByRole('row').filter({ hasText: `…${createdId.slice(-8)}` })
  await expect(createdRow).toContainText('Taslak')
  const afterCreate = await readOrders(page, creatorActor)
  expect(afterCreate).toHaveLength(before.length + 1)
  expect(afterCreate.filter((item) => item.id === createdId)).toHaveLength(1)
  expect(afterCreate.filter((item) => item.id === orderId)).toHaveLength(1)

  await createdRow.getByRole('link', { name: `…${createdId.slice(-8)}` }).click()
  await expect(page.getByText(createdId, { exact: true })).toBeVisible()
  const reopened = await readOrder(page, creatorActor, createdId)
  expect(reopened.order).toMatchObject({ id: createdId, status: 'Draft', version: createdVersion, supplierPartyId, companyAccountId })
  expect(reopened.lines[0]).toMatchObject({ productId, productVersion, unitId: productUnitId, ordered: '5' })

  await signInAs(page, approverActor)
  await page.goto(`${LIST_ROUTE}/${createdId}`)
  await expect(page.getByText(createdId, { exact: true })).toBeVisible()
  const approvalAnswer = page.waitForResponse((response) =>
    response.request().method() === 'POST' && new URL(response.url()).pathname === `${ORDERS_PATH}/${createdId}/approval`)
  await page.getByRole('button', { name: 'Onayla' }).click()
  expect((await approvalAnswer).status()).toBe(200)
  const approved = await readOrder(page, approverActor, createdId)
  expect([approved.order.id, approved.order.status, approved.order.version])
    .toEqual([createdId, 'Approved', (BigInt(createdVersion) + 1n).toString()])
  expect(await readOrders(page, approverActor)).toHaveLength(before.length + 1)

  await signInAs(page, readerActor)
  const readerCapabilities = await page.request.get(`${env.apiBaseUrl}/api/v1/shell/capabilities`, {
    headers: { [DEVELOPMENT_USER_HEADER]: readerActor, 'X-Company-Id': companyId },
  })
  expect(readerCapabilities.status()).toBe(200)
  const readerSnapshot = (await readerCapabilities.json()) as { readonly capabilities?: unknown }
  const readerPublished = Array.isArray(readerSnapshot.capabilities)
    ? readerSnapshot.capabilities as string[]
    : []
  expect(readerPublished).toContain('purchasing.orders.list')
  expect(readerPublished).toContain('purchasing.orders.detail')
  expect(readerPublished).not.toContain('purchasing.orders.create')

  await page.goto(LIST_ROUTE)
  await expect(page.getByRole('heading', { level: 1, name: 'Satın alma siparişleri' })).toBeVisible({ timeout: 30_000 })
  await expect(page.getByRole('link', { name: 'Yeni', exact: true })).toHaveCount(0)
  let deniedUiPosts = 0
  page.on('request', (request) => {
    if (request.method() === 'POST' && new URL(request.url()).pathname === ORDERS_PATH) deniedUiPosts += 1
  })
  await page.goto(CREATE_ROUTE)
  const refusedCreate = page.locator('[data-state="not-found"]')
  await expect(refusedCreate).toBeVisible({ timeout: 30_000 })
  await expect(refusedCreate).toContainText('Bulunamadı')
  await expect(page.getByRole('heading', { level: 1, name: 'Yeni satın alma siparişi' })).toHaveCount(0)
  expect(deniedUiPosts).toBe(0)

  const deniedRequest = {
    supplierPartyId, companyAccountId, sourceRequestId: null, effectiveOn: '2026-09-18',
    lines: [{
      lineId: '7b1c0f2e-0000-4a00-8000-000000000099', productId,
      expectedProductVersion: productVersion, unitId: productUnitId, quantity: '1', sourceRequestLineId: null,
    }],
  }
  const denied = await page.request.post(`${env.apiBaseUrl}${ORDERS_PATH}`, {
    headers: {
      [DEVELOPMENT_USER_HEADER]: readerActor, 'X-Company-Id': companyId,
      'Idempotency-Key': 'browser-suite:pur01:reader-create-denied', 'Content-Type': 'application/json',
    },
    data: deniedRequest,
  })
  expect(denied.status()).toBe(403)
  const elsewhere = await page.request.post(`${env.apiBaseUrl}${ORDERS_PATH}`, {
    headers: {
      [DEVELOPMENT_USER_HEADER]: creatorActor, 'X-Company-Id': otherCompanyId,
      'Idempotency-Key': 'browser-suite:pur01:other-company-create-denied', 'Content-Type': 'application/json',
    },
    data: deniedRequest,
  })
  expect([403, 404]).toContain(elsewhere.status())
  expect(await readOrders(page, readerActor)).toHaveLength(before.length + 1)
})

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
