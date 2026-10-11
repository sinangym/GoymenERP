import { expect, test, type APIRequestContext, type Download, type Page } from '@playwright/test'
import { ACCESS_ADMIN_ACTOR, CREATOR_ACTOR, OTHER_COMPANY_ACTOR, readEnvironment } from '../src/env'

/**
 * AT-065 / AT-057 (`browser:c02:ci`, document vault core) — a document on the Cari card in a real browser against the
 * disposable stack built from the exact head: real API, real SQL, the development object store and EICAR-only
 * scanner the Testing host registers (never a production scanner; every version it clears names it).
 *
 * THE JOURNEY IS THE CRITERION: on a card the creator made, upload a PDF → close and reopen the card → the document is
 * listed → download it and get the SAME bytes under the same name → upload the same file again and be warned with the
 * earlier document (AT-057 warns, never refuses). Then the fences, each with the variable isolated as C02-03 does:
 * the other-company actor holds the SAME document and Cari names in company B and still gets 404 for A's document; the
 * access administrator (no document name at all) gets 403 before anything is read.
 *
 * ACTOR NEEDS (infra/browser-suites/c02.override.yml, central): c02-creator and c02-other-company additionally hold
 * `documents.files.read` + `documents.files.upload` in their own company; the suite's closed permission list in
 * tools/validate_browser_acceptance.py gains the two names. c02-access-admin is unchanged.
 *
 * IT WRITES (a Party, three documents and their audit events): the suite is already on the writing arm.
 */

const { apiBaseUrl, testedCommit } = readEnvironment()

const DEVELOPMENT_USER_HEADER = 'X-Development-User'
const COMPANY_A = '10000000-0000-0000-0000-000000000001'
const COMPANY_B = '10000000-0000-0000-0000-000000000002'

/** Exact strings from src/cari/cariCommandText.ts and src/documents/documentsText.ts. */
const KIND_LABEL = 'Taraf türü'
const LEGAL_NAME_LABEL = 'Hukuki ad'
const SUBMIT_LABEL = 'Cari oluştur'
const LEGAL_PERSON_KIND = 'Tüzel kişi'
const DOCUMENTS_HEADING = 'Belgeler'
const FILE_LABEL = 'Dosya seç'
const UPLOAD_LABEL = 'Yükle'
const DUPLICATE_TITLE = 'Aynı dosya bu şirkette daha önce yüklenmiş'
const VERSION_FILE_LABEL = 'Yeni sürüm dosyası'
const APPEND_VERSION_LABEL = 'Sürümü ekle'

const LEGAL_NAME = `SENTETIK C02 belge ${testedCommit.slice(0, 12)}`
const FILE_NAME = `SENTETIK Sözleşme ${testedCommit.slice(0, 8)}.pdf`
const PDF = Buffer.from(`%PDF-1.4\n% SENTETIK browser-suite:c02 ${testedCommit}\n%%EOF\n`, 'utf8')
const PDF_V2 = Buffer.from(`%PDF-1.4\n% SENTETIK browser-suite:c02 version-2 ${testedCommit}\n%%EOF\n`, 'utf8')
const PDF_V3 = Buffer.from(`%PDF-1.4\n% SENTETIK browser-suite:c02 version-3 ${testedCommit}\n%%EOF\n`, 'utf8')
const DUPLICATE_PDF = Buffer.from(`%PDF-1.4\n% SENTETIK browser-suite:c02 duplicate ${testedCommit}\n%%EOF\n`, 'utf8')
const EICAR_PDF = Buffer.from(
  '%PDF-1.4\nX5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*\n%%EOF\n',
  'utf8',
)
const PREVIEW_FILE_NAME = `SENTETIK Tarama ${testedCommit.slice(0, 8)}.png`
const PNG = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=',
  'base64',
)

async function signInAs(page: Page, actor: string): Promise<void> {
  await page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: actor })
}

function documentsPanel(page: Page) {
  return page.locator('section[data-section="documents"]')
}

function actorHeaders(actor = CREATOR_ACTOR, companyId = COMPANY_A): Record<string, string> {
  return { [DEVELOPMENT_USER_HEADER]: actor, 'X-Company-Id': companyId }
}

async function appendVersion(
  request: APIRequestContext,
  id: string,
  expectedCurrentVersion: number,
  buffer: Buffer,
  name: string,
  key: string,
) {
  return request.post(`${apiBaseUrl}/api/v1/documents/${id}/versions`, {
    headers: { ...actorHeaders(), 'Idempotency-Key': key },
    multipart: {
      expectedCurrentVersion: String(expectedCurrentVersion),
      file: { name, mimeType: 'application/pdf', buffer },
    },
  })
}

async function downloadBytes(download: Download): Promise<Buffer> {
  const stream = await download.createReadStream()
  const chunks: Buffer[] = []
  for await (const chunk of stream) chunks.push(Buffer.from(chunk as Buffer))
  return Buffer.concat(chunks)
}

async function createParty(page: Page, suffix: string): Promise<string> {
  await page.goto('/cari/yeni')
  await page.getByLabel(KIND_LABEL).selectOption({ label: LEGAL_PERSON_KIND })
  await page.getByLabel(new RegExp(`^${LEGAL_NAME_LABEL}`)).fill(`${LEGAL_NAME} ${suffix}`)
  const created = page.waitForResponse((r) => r.request().method() === 'POST' && new URL(r.url()).pathname === '/api/v1/counterparties')
  await page.getByRole('button', { name: SUBMIT_LABEL }).click()
  const response = await created
  expect(response.status()).toBe(201)
  return String(((await response.json()) as Record<string, unknown>).id)
}

test.beforeAll(() => {
  expect(testedCommit).toMatch(/^[0-9a-f]{40}$/)
})

test.describe.configure({ mode: 'serial' })

let partyId = ''
let documentId = ''

test('C02-D1 a document uploaded on the Cari card is listed after close and reopen and downloads byte for byte', async ({ page }) => {
  await signInAs(page, CREATOR_ACTOR)
  partyId = await createParty(page, 'ana')
  await expect(page).toHaveURL(new RegExp(`/cari/${partyId}$`))

  const panel = documentsPanel(page)
  await expect(panel.getByRole('heading', { name: DOCUMENTS_HEADING })).toBeVisible()
  await panel.getByLabel(FILE_LABEL).setInputFiles({ name: FILE_NAME, mimeType: 'application/pdf', buffer: PDF })
  const uploaded = page.waitForResponse((r) => r.request().method() === 'POST' && new URL(r.url()).pathname === '/api/v1/documents')
  await panel.getByRole('button', { name: UPLOAD_LABEL, exact: true }).click()
  const uploadResponse = await uploaded
  expect(uploadResponse.status()).toBe(201)
  const body = (await uploadResponse.json()) as { document: { id: string; scanState: string; downloadable: boolean }; duplicates: unknown[] }
  documentId = body.document.id
  expect(body.document.scanState).toBe('Clean')
  expect(body.duplicates).toEqual([])
  await expect(panel.locator('[data-documents-state="stored"]')).toContainText(FILE_NAME)

  // Close and reopen: leave the app, come back by URL; the list is the server's.
  await page.goto('/')
  await page.goto(`/cari/${partyId}`)
  const reopened = documentsPanel(page)
  const row = reopened.locator(`tr[data-document-id="${documentId}"]`)
  await expect(row).toBeVisible()
  await expect(row.getByRole('rowheader')).toHaveText(FILE_NAME)

  const downloadPromise = page.waitForEvent('download')
  await row.getByRole('button', { name: `${FILE_NAME} dosyasını indir` }).click()
  const download = await downloadPromise
  expect(download.suggestedFilename()).toBe(FILE_NAME)
  expect((await downloadBytes(download)).equals(PDF)).toBe(true)
})

test('C02-D1V the same version attempt survives a lost answer, replays once and reopens history 2 then 1 with distinct bytes', async ({ page }) => {
  expect(documentId, 'C02-D1 must have stored a document').not.toBe('')
  await signInAs(page, CREATOR_ACTOR)
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto(`/cari/${partyId}`)
  const panel = documentsPanel(page)
  const row = panel.locator(`tr[data-document-id="${documentId}"]`)
  await row.locator(`[data-document-versions-toggle="${documentId}"]`).click()
  const history = panel.locator(`section[data-document-versions="${documentId}"]`)
  await expect(history.locator('tr[data-version-no="1"]')).toBeVisible()

  let calls = 0
  const keys: string[] = []
  const versionPath = `/api/v1/documents/${documentId}/versions`
  await page.route((url) => url.pathname === versionPath, async (route) => {
    if (route.request().method() !== 'POST') {
      await route.continue()
      return
    }
    calls += 1
    keys.push(route.request().headers()['idempotency-key'] ?? '')
    if (calls === 1) {
      const stored = await route.fetch()
      expect(stored.status()).toBe(201)
      await route.abort('connectionreset')
      return
    }
    await route.continue()
  })

  await history.getByLabel(VERSION_FILE_LABEL).setInputFiles({ name: 'SENTETIK Sözleşme-v2.pdf', mimeType: 'application/pdf', buffer: PDF_V2 })
  await history.getByRole('button', { name: APPEND_VERSION_LABEL }).click()
  await expect(history.locator('[data-version-state="unknown"]')).toBeVisible()
  const replayResponse = page.waitForResponse((r) => r.request().method() === 'POST' && new URL(r.url()).pathname === versionPath)
  await history.getByRole('button', { name: 'Aynı sürüm denemesini tekrarla' }).click()
  const replay = await replayResponse
  expect(replay.status()).toBe(201)
  expect(replay.headers()['idempotency-replayed']).toBe('true')
  expect(calls).toBe(2)
  expect(keys[0]).toMatch(/^[0-9a-f-]{36}$/)
  expect(keys[1]).toBe(keys[0])
  await expect(history.locator('[data-version-state="replayed"]')).toBeVisible()
  await expect(history.locator('tr[data-version-no]')).toHaveCount(2)
  await expect(history.locator('tr[data-version-no]').nth(0)).toHaveAttribute('data-version-no', '2')
  await expect(history.locator('tr[data-version-no]').nth(1)).toHaveAttribute('data-version-no', '1')

  const v2Download = page.waitForEvent('download')
  await history.getByRole('button', { name: 'SENTETIK Sözleşme-v2.pdf sürüm 2 içeriğini indir' }).click()
  expect((await downloadBytes(await v2Download)).equals(PDF_V2)).toBe(true)
  const v1Download = page.waitForEvent('download')
  await history.getByRole('button', { name: `${FILE_NAME} sürüm 1 içeriğini indir` }).click()
  expect((await downloadBytes(await v1Download)).equals(PDF)).toBe(true)

  await page.goto('/')
  await page.goto(`/cari/${partyId}`)
  const reopened = documentsPanel(page)
  await reopened.locator(`[data-document-versions-toggle="${documentId}"]`).click()
  const reopenedHistory = reopened.locator(`section[data-document-versions="${documentId}"]`)
  await expect(reopenedHistory.locator('tr[data-version-no]').nth(0)).toHaveAttribute('data-version-no', '2')
  await expect(reopenedHistory.locator('tr[data-version-no]').nth(1)).toHaveAttribute('data-version-no', '1')
})

test('C02-D1R a stale expected version and scanner rejection create no extra version and refresh the real history', async ({ page }) => {
  expect(documentId, 'C02-D1 must have stored a document').not.toBe('')
  await signInAs(page, CREATOR_ACTOR)
  await page.goto(`/cari/${partyId}`)
  const panel = documentsPanel(page)
  await panel.locator(`[data-document-versions-toggle="${documentId}"]`).click()
  const history = panel.locator(`section[data-document-versions="${documentId}"]`)
  await expect(history.locator('tr[data-version-no="2"]')).toBeVisible()

  const concurrent = await appendVersion(page.request, documentId, 2, PDF_V3, 'SENTETIK Sözleşme-v3.pdf', `SENTETIK-c02-v3-${testedCommit.slice(0, 12)}`)
  expect(concurrent.status()).toBe(201)

  await history.getByLabel(VERSION_FILE_LABEL).setInputFiles({ name: 'SENTETIK stale.pdf', mimeType: 'application/pdf', buffer: PDF })
  const staleResponse = page.waitForResponse((r) => r.request().method() === 'POST' && new URL(r.url()).pathname.endsWith(`/${documentId}/versions`))
  await history.getByRole('button', { name: APPEND_VERSION_LABEL }).click()
  expect((await staleResponse).status()).toBe(409)
  await expect(history.locator('[data-version-state="refused"]')).toContainText('başka bir sürüm eklendi')
  await expect(history.locator('tr[data-version-no="3"]')).toBeVisible()
  await expect(history.locator('tr[data-version-no]')).toHaveCount(3)

  await history.getByLabel(VERSION_FILE_LABEL).setInputFiles({ name: 'SENTETIK tarama-reddi.pdf', mimeType: 'application/pdf', buffer: EICAR_PDF })
  const rejectedResponse = page.waitForResponse((r) => r.request().method() === 'POST' && new URL(r.url()).pathname.endsWith(`/${documentId}/versions`))
  await history.getByRole('button', { name: APPEND_VERSION_LABEL }).click()
  expect((await rejectedResponse).status()).toBe(422)
  await expect(history.locator('[data-version-state="refused"]')).toContainText('zararlı içerik buldu')
  await expect(history.locator('tr[data-version-no]')).toHaveCount(3)

  const pageResponse = await page.request.get(`${apiBaseUrl}/api/v1/documents/${documentId}/versions?pageSize=50`, { headers: actorHeaders() })
  expect(pageResponse.status()).toBe(200)
  const body = (await pageResponse.json()) as { versions: Array<{ versionNo: number }> }
  expect(body.versions.map((version) => version.versionNo)).toEqual([3, 2, 1])
})

test('C02-D1H continuation failure keeps fifty real rows, retries the same cursor and remains keyboard reachable at 390px', async ({ page }) => {
  test.setTimeout(240_000)
  expect(documentId, 'C02-D1 must have stored a document').not.toBe('')
  await signInAs(page, CREATOR_ACTOR)
  for (let version = 4; version <= 51; version += 1) {
    const bytes = Buffer.from(`%PDF-1.4\n% SENTETIK C02 version-${version} ${testedCommit}\n%%EOF\n`, 'utf8')
    const response = await appendVersion(
      page.request,
      documentId,
      version - 1,
      bytes,
      `SENTETIK-Sürüm-${String(version).padStart(2, '0')}.pdf`,
      `SENTETIK-c02-v${version}-${testedCommit.slice(0, 8)}`,
    )
    expect(response.status(), `version ${version}`).toBe(201)
  }

  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto(`/cari/${partyId}`)
  const panel = documentsPanel(page)
  const toggle = panel.locator(`[data-document-versions-toggle="${documentId}"]`)
  await toggle.click()
  const history = panel.locator(`section[data-document-versions="${documentId}"]`)
  await expect(history.locator('tr[data-version-no]')).toHaveCount(50)
  const continuationUrls: string[] = []
  let continuationCalls = 0
  await page.route((url) => url.pathname.endsWith(`/${documentId}/versions`) && url.searchParams.has('cursor'), async (route) => {
    continuationCalls += 1
    continuationUrls.push(route.request().url())
    if (continuationCalls === 1) {
      await route.fulfill({ status: 503, contentType: 'application/problem+json', body: '{}' })
      return
    }
    await route.continue()
  })
  await history.getByRole('button', { name: 'Daha eski sürümleri getir' }).click()
  await expect(history.locator('[data-version-state="more-failed"]')).toBeVisible()
  await expect(history.locator('tr[data-version-no]')).toHaveCount(50)
  const continued = page.waitForResponse((r) => r.request().method() === 'GET' && new URL(r.url()).pathname.endsWith(`/${documentId}/versions`) && new URL(r.url()).searchParams.has('cursor'))
  await history.getByRole('button', { name: 'Kaldığı yerden tekrar dene' }).click()
  expect((await continued).status()).toBe(200)
  await expect(history.locator('tr[data-version-no]')).toHaveCount(51)
  await expect(history.locator('tr[data-version-no="1"]')).toBeVisible()
  expect(continuationCalls).toBe(2)
  expect(new URL(continuationUrls[0]).searchParams.get('cursor')).toBe(new URL(continuationUrls[1]).searchParams.get('cursor'))
  await history.getByRole('button', { name: 'Sürümleri kapat' }).click()
  await page.setViewportSize({ width: 390, height: 844 })
  const mobileToggle = panel.locator(`[data-document-versions-toggle="${documentId}"]`)
  await mobileToggle.focus()
  await expect(mobileToggle).toBeFocused()
  await page.keyboard.press('Enter')
  const mobileHistory = panel.locator(`section[data-document-versions="${documentId}"]`)
  const mobileHistoryHeading = mobileHistory.getByRole('heading')
  await expect(mobileHistoryHeading).toContainText('— sürümler')
  await expect(mobileHistoryHeading).toBeFocused()
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
  await expect(mobileHistory.locator('tr[data-version-no="51"]')).toBeVisible()
})

test('C02-D1B a late real history answer cannot cross the owner generation and the limited actor sees no vault controls', async ({ page }) => {
  expect(documentId, 'C02-D1 must have stored a document').not.toBe('')
  await signInAs(page, CREATOR_ACTOR)
  const otherPartyId = await createParty(page, 'ikinci-kayıt')
  await page.goto(`/cari/${partyId}`)
  const firstPanel = documentsPanel(page)

  let release!: () => void
  let fetched!: () => void
  const releaseGate = new Promise<void>((resolve) => {
    release = resolve
  })
  const fetchedGate = new Promise<void>((resolve) => {
    fetched = resolve
  })
  const historyPath = `/api/v1/documents/${documentId}/versions`
  await page.route((url) => url.pathname === historyPath && !url.searchParams.has('cursor'), async (route) => {
    const response = await route.fetch()
    expect(response.status()).toBe(200)
    fetched()
    await releaseGate
    try {
      await route.fulfill({ response })
    } catch {
      // A navigation is allowed to abort the obsolete transport; either way its bytes must not enter the new owner view.
    }
  })
  await firstPanel.locator(`[data-document-versions-toggle="${documentId}"]`).click()
  await fetchedGate
  const navigation = page.goto(`/cari/${otherPartyId}`)
  await page.waitForURL(new RegExp(`/cari/${otherPartyId}$`))
  release()
  await navigation
  const otherPanel = documentsPanel(page)
  await expect(otherPanel.locator(`[data-document-versions="${documentId}"]`)).toHaveCount(0)
  await expect(otherPanel.getByText(FILE_NAME, { exact: true })).toHaveCount(0)
  await signInAs(page, ACCESS_ADMIN_ACTOR)
  await page.goto(`/cari/${partyId}`)
  await expect(documentsPanel(page)).toHaveCount(0)
})

test('C02-D1P a verified PNG opens as an accessible preview and closes back to its trigger', async ({ page }) => {
  expect(partyId, 'C02-D1 must have created the card').not.toBe('')
  await signInAs(page, CREATOR_ACTOR)
  await page.goto(`/cari/${partyId}`)
  const panel = documentsPanel(page)
  await panel.getByLabel(FILE_LABEL).setInputFiles({ name: PREVIEW_FILE_NAME, mimeType: 'image/png', buffer: PNG })
  const uploaded = page.waitForResponse((r) => r.request().method() === 'POST' && new URL(r.url()).pathname === '/api/v1/documents')
  await panel.getByRole('button', { name: UPLOAD_LABEL, exact: true }).click()
  const uploadResponse = await uploaded
  expect(uploadResponse.status()).toBe(201)
  const body = (await uploadResponse.json()) as { document: { id: string; scanState: string; downloadable: boolean } }
  expect(body.document.scanState).toBe('Clean')
  expect(body.document.downloadable).toBe(true)

  const row = panel.locator(`tr[data-document-id="${body.document.id}"]`)
  await expect(row).toBeVisible()
  const trigger = row.getByRole('button', { name: `${PREVIEW_FILE_NAME} dosyasını önizle` })
  await trigger.click()

  const dialog = page.getByRole('dialog', { name: `${PREVIEW_FILE_NAME} önizlemesi` })
  await expect(dialog).toBeVisible()
  const image = dialog.getByRole('img', { name: `${PREVIEW_FILE_NAME} belge önizlemesi` })
  await expect(image).toBeVisible()
  await expect.poll(() => image.evaluate((node: HTMLImageElement) => node.naturalWidth)).toBe(1)
  const close = dialog.getByRole('button', { name: 'Önizlemeyi kapat' })
  await expect(close).toBeFocused()
  await page.keyboard.press('Tab')
  await expect(close).toBeFocused()
  await page.keyboard.press('Shift+Tab')
  await expect(close).toBeFocused()
  await page.keyboard.press('Escape')
  await expect(dialog).toBeHidden()
  await expect(trigger).toBeFocused()
})

test('C02-D2 uploading the same file again stores it and warns with the earlier document (AT-057)', async ({ page }) => {
  expect(partyId, 'C02-D1 must have created the card').not.toBe('')
  await signInAs(page, CREATOR_ACTOR)
  await page.goto(`/cari/${partyId}`)
  const panel = documentsPanel(page)
  const rowsBefore = await panel.locator('tbody tr').count()

  // D1V/H deliberately move the original document through many immutable versions. Duplicate detection concerns the
  // current content of another document, so establish that earlier document here instead of treating D1's historical
  // v1 bytes as the current upload. Both writes still use the real API through the page.
  const earlierName = `SENTETIK Önceki belge ${testedCommit.slice(0, 8)}.pdf`
  await panel.getByLabel(FILE_LABEL).setInputFiles({ name: earlierName, mimeType: 'application/pdf', buffer: DUPLICATE_PDF })
  const earlierUpload = page.waitForResponse((r) => r.request().method() === 'POST' && new URL(r.url()).pathname === '/api/v1/documents')
  await panel.getByRole('button', { name: UPLOAD_LABEL, exact: true }).click()
  const earlierResponse = await earlierUpload
  expect(earlierResponse.status()).toBe(201)
  const earlierBody = (await earlierResponse.json()) as { document: { id: string }; duplicates: unknown[] }
  expect(earlierBody.duplicates).toEqual([])

  await panel.getByLabel(FILE_LABEL).setInputFiles({ name: `Tekrar ${earlierName}`, mimeType: 'application/pdf', buffer: DUPLICATE_PDF })
  const uploaded = page.waitForResponse((r) => r.request().method() === 'POST' && new URL(r.url()).pathname === '/api/v1/documents')
  await panel.getByRole('button', { name: UPLOAD_LABEL, exact: true }).click()
  expect((await uploaded).status()).toBe(201)

  const warning = panel.locator('[data-documents-state="duplicate"]')
  await expect(warning).toContainText(DUPLICATE_TITLE)
  await expect(warning.locator(`[data-duplicate-id="${earlierBody.document.id}"]`)).toContainText(earlierName)
  await expect(panel.locator('tbody tr')).toHaveCount(rowsBefore + 2)
})

test('C02-D3 another company\'s fully authorized actor gets 404 and an actor without the document names gets 403', async ({ page }) => {
  expect(documentId, 'C02-D1 must have stored a document').not.toBe('')
  const content = `${apiBaseUrl}/api/v1/documents/${documentId}/content`
  const versions = `${apiBaseUrl}/api/v1/documents/${documentId}/versions`
  const oldVersion = `${versions}/1/content`
  const list = `${apiBaseUrl}/api/v1/documents?ownerKind=counterparty.party&ownerId=${partyId}`

  // The same names, in company B: A's document is not found, and A's card is not B's record.
  const other = { [DEVELOPMENT_USER_HEADER]: OTHER_COMPANY_ACTOR, 'X-Company-Id': COMPANY_B }
  expect((await page.request.get(content, { headers: other })).status()).toBe(404)
  expect((await page.request.get(versions, { headers: other })).status()).toBe(404)
  expect((await page.request.get(oldVersion, { headers: other })).status()).toBe(404)
  expect((await page.request.get(list, { headers: other })).status()).toBe(404)

  // No document name at all, in company A: refused before anything is read.
  const noName = { [DEVELOPMENT_USER_HEADER]: ACCESS_ADMIN_ACTOR, 'X-Company-Id': COMPANY_A }
  expect((await page.request.get(content, { headers: noName })).status()).toBe(403)
  expect((await page.request.get(versions, { headers: noName })).status()).toBe(403)
  expect((await page.request.get(oldVersion, { headers: noName })).status()).toBe(403)
  expect((await page.request.get(list, { headers: noName })).status()).toBe(403)

  // And the permitted actor still downloads it: the refusals above were about THEM, not the document.
  const creator = { [DEVELOPMENT_USER_HEADER]: CREATOR_ACTOR, 'X-Company-Id': COMPANY_A }
  const allowed = await page.request.get(content, { headers: creator })
  expect(allowed.status()).toBe(200)
  expect(Buffer.from(await allowed.body()).equals(PDF)).toBe(true)
  expect(allowed.headers()['x-content-type-options']).toBe('nosniff')
})
