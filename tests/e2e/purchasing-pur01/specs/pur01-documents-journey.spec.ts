import { expect, test, type Page } from '@playwright/test'
import sql from 'mssql'
import { readEnvironment, readSqlConnection } from '../src/env'

const env = readEnvironment()
const sqlConnection = readSqlConnection()
const { creatorActor, readerActor, companyId, otherCompanyId, orderId } = env.seed

const DEVELOPMENT_USER_HEADER = 'X-Development-User'
const OWNER_KIND = 'purchasing.purchase-order'
const DOCUMENTS_PATH = '/api/v1/documents'
const LIST_ROUTE = '/satinalma/siparisler'
const FILE_NAME = 'PUR01-kantar-fisi.png'
const FILE_BYTES = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=',
  'base64',
)

interface DocumentRow {
  readonly id: string
  readonly companyId: string
  readonly ownerKind: string
  readonly ownerId: string
  readonly versionNo: number
  readonly fileName: string
  readonly contentType: string
  readonly sizeBytes: number
  readonly downloadable: boolean
}

interface DocumentUpload {
  readonly document: DocumentRow
  readonly duplicates: readonly unknown[]
  readonly isReplay: boolean
  readonly originalCorrelationId: string
}

interface DocumentList {
  readonly items: readonly DocumentRow[]
  readonly canUpload: boolean
}

function apiHeaders(actor: string, company = companyId): Record<string, string> {
  return { [DEVELOPMENT_USER_HEADER]: actor, 'X-Company-Id': company }
}

function listPath(owner = orderId): string {
  return `${DOCUMENTS_PATH}?ownerKind=${encodeURIComponent(OWNER_KIND)}&ownerId=${owner}`
}

async function capabilitiesOf(page: Page, actor: string): Promise<readonly string[]> {
  const response = await page.request.get(`${env.apiBaseUrl}/api/v1/shell/capabilities`, {
    headers: apiHeaders(actor),
  })
  expect(response.status()).toBe(200)
  const body = await response.json() as { readonly capabilities?: unknown }
  return Array.isArray(body.capabilities) ? body.capabilities as string[] : []
}

async function listDocuments(page: Page, actor: string): Promise<DocumentList> {
  const response = await page.request.get(`${env.apiBaseUrl}${listPath()}`, { headers: apiHeaders(actor) })
  expect(response.status()).toBe(200)
  return await response.json() as DocumentList
}

test('PUR01-DOC uploads once, reopens, previews/downloads and hides the vault from an order-only reader', async ({ page }) => {
  test.setTimeout(240_000)
  // The shell endpoint publishes declared UI capability ids, not raw permission names. Documents authority is
  // measured through the real Documents API and UI below; Purchasing detail is the declared route capability.
  const writerCapabilities = await capabilitiesOf(page, creatorActor)
  expect(writerCapabilities).toContain('purchasing.orders.detail')
  const readerCapabilities = await capabilitiesOf(page, readerActor)
  expect(readerCapabilities).toContain('purchasing.orders.detail')

  await page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: creatorActor })
  const emptyVault = await listDocuments(page, creatorActor)
  expect(emptyVault.canUpload).toBe(true)
  expect(emptyVault.items).toEqual([])
  await page.goto(`${LIST_ROUTE}/${orderId}`)
  await expect(page.getByText(orderId, { exact: true })).toBeVisible({ timeout: 30_000 })
  await expect(page.locator('[data-documents-state="none"]')).toBeVisible()

  let committed: DocumentUpload | null = null
  let firstKey: string | null = null
  let firstCompany: string | null = null
  await page.route(`**${DOCUMENTS_PATH}`, async (route) => {
    if (route.request().method() !== 'POST' || committed !== null) return route.fallback()
    firstKey = await route.request().headerValue('Idempotency-Key')
    firstCompany = await route.request().headerValue('X-Company-Id')
    const response = await route.fetch()
    expect(response.status()).toBe(201)
    committed = await response.json() as DocumentUpload
    await route.abort('failed')
  })

  await page.getByLabel('Dosya seç').setInputFiles({ name: FILE_NAME, mimeType: 'image/png', buffer: FILE_BYTES })
  await page.getByRole('button', { name: 'Yükle', exact: true }).click()
  await expect(page.getByText('Yüklemenin sonucu bilinmiyor', { exact: true })).toBeVisible()
  expect(firstKey).toMatch(/^[0-9a-f-]{36}$/i)
  expect(firstCompany).toBe(companyId)
  expect(committed).not.toBeNull()
  const firstUpload = committed as DocumentUpload | null
  if (firstUpload === null) throw new Error('The real upload committed without a readable response body')

  const replayAnswer = page.waitForResponse((response) =>
    response.request().method() === 'POST' && new URL(response.url()).pathname === DOCUMENTS_PATH)
  await page.getByRole('button', { name: 'Aynı dosyayla tekrar dene', exact: true }).click()
  const replayResponse = await replayAnswer
  expect(replayResponse.status()).toBe(201)
  expect(replayResponse.headers()['idempotency-replayed']).toBe('true')
  expect(await replayResponse.request().headerValue('Idempotency-Key')).toBe(firstKey)
  expect(await replayResponse.request().headerValue('X-Company-Id')).toBe(companyId)
  const replay = await replayResponse.json() as DocumentUpload
  expect(replay.isReplay).toBe(true)
  expect(replay.document).toEqual(firstUpload.document)
  expect(replay.duplicates).toEqual([])
  await expect(page.getByText('Bu yükleme daha önce kaydedilmişti; aynı kayıt gösteriliyor.')).toBeVisible()
  await expect(page.getByRole('rowheader', { name: FILE_NAME })).toBeVisible()

  await page.goto(LIST_ROUTE)
  const orderRow = page.getByRole('row').filter({ hasText: `…${orderId.slice(-8)}` })
  await expect(orderRow).toBeVisible()
  await orderRow.getByRole('link', { name: `…${orderId.slice(-8)}` }).click()
  await expect(page.getByText(orderId, { exact: true })).toBeVisible()
  await expect(page.getByRole('rowheader', { name: FILE_NAME })).toBeVisible()

  await page.getByRole('button', { name: `${FILE_NAME} dosyasını önizle` }).click()
  await expect(page.getByRole('dialog', { name: `${FILE_NAME} önizlemesi` })).toBeVisible()
  await expect(page.getByRole('img', { name: `${FILE_NAME} belge önizlemesi` })).toBeVisible()
  await page.getByRole('button', { name: 'Önizlemeyi kapat', exact: true }).click()

  const download = page.waitForEvent('download')
  await page.getByRole('button', { name: `${FILE_NAME} dosyasını indir` }).click()
  expect((await download).suggestedFilename()).toBe(FILE_NAME)

  const stored = await listDocuments(page, creatorActor)
  expect(stored.canUpload).toBe(true)
  expect(stored.items).toHaveLength(1)
  expect(stored.items[0]).toMatchObject({
    id: replay.document.id,
    companyId,
    ownerKind: OWNER_KIND,
    ownerId: orderId,
    versionNo: 1,
    fileName: FILE_NAME,
    contentType: 'image/png',
    sizeBytes: FILE_BYTES.length,
    downloadable: true,
  })

  const pool = await new sql.ConnectionPool(sqlConnection).connect()
  try {
    const effects = await pool.request()
      .input('companyId', sql.UniqueIdentifier, companyId)
      .input('documentId', sql.UniqueIdentifier, replay.document.id)
      .input('ownerKind', sql.NVarChar(128), OWNER_KIND)
      .input('ownerId', sql.UniqueIdentifier, orderId)
      .input('entityId', sql.NVarChar(64), replay.document.id)
      .query(`
        SELECT
          (SELECT COUNT(*) FROM documents.Documents WHERE CompanyId=@companyId AND Id=@documentId) AS documents,
          (SELECT COUNT(*) FROM documents.DocumentVersions WHERE CompanyId=@companyId AND DocumentId=@documentId) AS versions,
          (SELECT COUNT(*) FROM documents.DocumentLinks WHERE CompanyId=@companyId AND DocumentId=@documentId
            AND OwnerKind=@ownerKind AND OwnerId=@ownerId) AS links,
          (SELECT COUNT(*) FROM documents.DocumentAttempts WHERE CompanyId=@companyId AND DocumentId=@documentId
            AND CommandName=N'documents.upload.v1') AS attempts,
          (SELECT COUNT(*) FROM audit.AuditEvents WHERE CompanyId=@companyId AND EntityId=@entityId
            AND EventType=N'documents.document.uploaded.v1') AS audits`)
    expect(effects.recordset[0]).toEqual({ documents: 1, versions: 1, links: 1, attempts: 1, audits: 1 })
  } finally {
    await pool.close()
  }

  await page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: readerActor })
  const deniedList = page.waitForResponse((response) => {
    const url = new URL(response.url())
    return response.request().method() === 'GET' && url.pathname === DOCUMENTS_PATH
      && url.searchParams.get('ownerKind') === OWNER_KIND && url.searchParams.get('ownerId') === orderId
  })
  await page.goto(`${LIST_ROUTE}/${orderId}`)
  expect((await deniedList).status()).toBe(403)
  await expect(page.getByRole('heading', { level: 2, name: 'Belgeler', exact: true })).toHaveCount(0)

  const foreign = await page.request.get(`${env.apiBaseUrl}${listPath()}`, {
    headers: apiHeaders(creatorActor, otherCompanyId),
  })
  // The creator may read documents in the other company, but this order is not that company's record:
  // DocumentService answers another company's owner as 404 (non-disclosure), never as a readable or refused vault.
  expect(foreign.status()).toBe(404)
  const foreignBody = await foreign.text()
  expect(foreignBody).not.toContain(FILE_NAME)
  expect(foreignBody).not.toContain(companyId)
  const foreignProblem = JSON.parse(foreignBody) as Record<string, unknown>
  for (const leaked of ['items', 'canUpload', 'document', 'fileName', 'ownerKind', 'ownerId']) {
    expect(foreignProblem, `the foreign-company answer carries "${leaked}"`).not.toHaveProperty(leaked)
  }
  const unknownOwner = crypto.randomUUID()
  const unknown = await page.request.get(`${env.apiBaseUrl}${listPath(unknownOwner)}`, {
    headers: apiHeaders(creatorActor),
  })
  expect(unknown.status()).toBe(404)
  const unknownBody = await unknown.text()
  expect(unknownBody).not.toContain(orderId)
  expect(unknownBody).not.toContain(FILE_NAME)
  // An owner that exists in another company and an owner that does not exist answer with the same shape — the
  // same fields, whatever the platform's problem writer names them — so nothing says which of the two it was.
  const unknownProblem = JSON.parse(unknownBody) as Record<string, unknown>
  expect(Object.keys(foreignProblem).sort()).toEqual(Object.keys(unknownProblem).sort())
})
