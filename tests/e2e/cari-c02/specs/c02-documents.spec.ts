import { expect, test, type Page } from '@playwright/test'
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

const LEGAL_NAME = `SENTETIK C02 belge ${testedCommit.slice(0, 12)}`
const FILE_NAME = `SENTETIK Sözleşme ${testedCommit.slice(0, 8)}.pdf`
const PDF = Buffer.from(`%PDF-1.4\n% SENTETIK browser-suite:c02 ${testedCommit}\n%%EOF\n`, 'utf8')
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

test.beforeAll(() => {
  expect(testedCommit).toMatch(/^[0-9a-f]{40}$/)
})

test.describe.configure({ mode: 'serial' })

let partyId = ''
let documentId = ''

test('C02-D1 a document uploaded on the Cari card is listed after close and reopen and downloads byte for byte', async ({ page }) => {
  await signInAs(page, CREATOR_ACTOR)
  await page.goto('/cari/yeni')
  await page.getByLabel(KIND_LABEL).selectOption({ label: LEGAL_PERSON_KIND })
  await page.getByLabel(new RegExp(`^${LEGAL_NAME_LABEL}`)).fill(LEGAL_NAME)
  const created = page.waitForResponse((r) => r.request().method() === 'POST' && new URL(r.url()).pathname === '/api/v1/counterparties')
  await page.getByRole('button', { name: SUBMIT_LABEL }).click()
  const createdResponse = await created
  expect(createdResponse.status()).toBe(201)
  partyId = String(((await createdResponse.json()) as Record<string, unknown>).id)
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
  const saved = await download.createReadStream()
  const chunks: Buffer[] = []
  for await (const chunk of saved) chunks.push(Buffer.from(chunk as Buffer))
  expect(Buffer.concat(chunks).equals(PDF)).toBe(true)
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
  await panel.getByLabel(FILE_LABEL).setInputFiles({ name: `Tekrar ${FILE_NAME}`, mimeType: 'application/pdf', buffer: PDF })
  const uploaded = page.waitForResponse((r) => r.request().method() === 'POST' && new URL(r.url()).pathname === '/api/v1/documents')
  await panel.getByRole('button', { name: UPLOAD_LABEL, exact: true }).click()
  expect((await uploaded).status()).toBe(201)

  const warning = panel.locator('[data-documents-state="duplicate"]')
  await expect(warning).toContainText(DUPLICATE_TITLE)
  await expect(warning.locator(`[data-duplicate-id="${documentId}"]`)).toContainText(FILE_NAME)
  await expect(panel.locator('tbody tr')).toHaveCount(3)
})

test('C02-D3 another company\'s fully authorized actor gets 404 and an actor without the document names gets 403', async ({ page }) => {
  expect(documentId, 'C02-D1 must have stored a document').not.toBe('')
  const content = `${apiBaseUrl}/api/v1/documents/${documentId}/content`
  const list = `${apiBaseUrl}/api/v1/documents?ownerKind=counterparty.party&ownerId=${partyId}`

  // The same names, in company B: A's document is not found, and A's card is not B's record.
  const other = { [DEVELOPMENT_USER_HEADER]: OTHER_COMPANY_ACTOR, 'X-Company-Id': COMPANY_B }
  expect((await page.request.get(content, { headers: other })).status()).toBe(404)
  expect((await page.request.get(list, { headers: other })).status()).toBe(404)

  // No document name at all, in company A: refused before anything is read.
  const noName = { [DEVELOPMENT_USER_HEADER]: ACCESS_ADMIN_ACTOR, 'X-Company-Id': COMPANY_A }
  expect((await page.request.get(content, { headers: noName })).status()).toBe(403)
  expect((await page.request.get(list, { headers: noName })).status()).toBe(403)

  // And the permitted actor still downloads it: the refusals above were about THEM, not the document.
  const creator = { [DEVELOPMENT_USER_HEADER]: CREATOR_ACTOR, 'X-Company-Id': COMPANY_A }
  const allowed = await page.request.get(content, { headers: creator })
  expect(allowed.status()).toBe(200)
  expect(Buffer.from(await allowed.body()).equals(PDF)).toBe(true)
  expect(allowed.headers()['x-content-type-options']).toBe('nosniff')
})
