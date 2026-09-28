import { randomUUID } from 'node:crypto'
import { expect, test, type Page, type Request as BrowserRequest, type Response } from '@playwright/test'

/**
 * U02-DUP — the §10.2 duplicate warning on the REAL API and UI of the u02 disposable stack. Nothing is mocked.
 *
 * Decided rules (spec PRODUCT-U01-U02-JIT-001 §7/§10.2, README "Same name is not a duplicate hard block"):
 *  - a code/alias exact collision is a hard block — the server mints the code; the database refuses a second
 *    holder (SQL facts, not this journey);
 *  - a name match only WARNS and lets the person compare: it never blocks a save or an approval and never merges.
 * The server answers ONE rule, `exactName`: same name after trim, under the name column's Turkish
 * case-insensitive, accent-sensitive collation. No similarity/technical rule exists (NEEDS-SINAN), so none is
 * asserted.
 *
 * Journey (this run's cards only, every name carries the run tag):
 *  1. The authorized actor's real create arranges card A plus two controls that must NOT match: a longer name
 *     (not a substring rule) and one differing only by an accent (ü/u).
 *  2. In the browser the authorized actor saves Draft B, typed with other letter cases (İ/i, Ü/ü): the save is
 *     201 (never blocked) and the saved screen lists exactly A, with a link.
 *  3. The link opens A, whose own warning lists B. Closing and reopening B asks the server again and lists A.
 *     B's edit page warns for its stored name.
 *  4. The disposable activator opens B (A listed). A third same-named card C is created meanwhile. The first
 *     press re-reads the warning, sends NO approval and shows the fresh list (A and C); the second press
 *     approves (200). B reopened is Active and shows no warning; A lists B (Aktif) and C.
 *  5. The unauthorized actor's direct preview is 403 and discloses no name.
 */

type Seed = {
  readonly companyId: string
  readonly codeGroupId: string
  readonly authorizedActor: string
  readonly unauthorizedActor: string
  readonly activatorActor: string
}

function required(name: string): string {
  const value = process.env[name]?.trim()
  if (!value) throw new Error(`${name} is not set; the u02 duplicate journey runs only inside the disposable stack.`)
  return value
}

function seed(): Seed {
  const raw = JSON.parse(required('BROWSER_E2E_U02_SEED')) as Record<string, unknown>
  const text = (key: keyof Seed): string => {
    const value = raw[key]
    if (typeof value !== 'string' || value.trim() === '') throw new Error(`BROWSER_E2E_U02_SEED.${key} is missing.`)
    return value
  }
  const value: Seed = {
    companyId: text('companyId'),
    codeGroupId: text('codeGroupId'),
    authorizedActor: text('authorizedActor'),
    unauthorizedActor: text('unauthorizedActor'),
    activatorActor: text('activatorActor'),
  }
  if (!/^u02-[a-z0-9-]+$/.test(value.activatorActor)) throw new Error('activatorActor is not a disposable u02 actor.')
  return value
}

const DEVELOPMENT_USER_HEADER = 'X-Development-User'
const PRODUCTS_PATH = '/api/v1/products'
const DUPLICATES_PATH = `${PRODUCTS_PATH}/duplicate-candidates`

// The screen's own texts (src/product/productText.ts), asserted verbatim.
const FOUND_TITLE = 'Bu adla kayıtlı başka ürün kartları var'
const CHANGED_BEFORE_APPROVAL =
  'Aynı adlı kartların listesi siz bakarken değişti. Güncel listeyi inceleyin; onaylamak için yeniden basın.'
const ACTIVATE_BUTTON = 'Ürünü aktifleştir'
const ACTIVATE_SUCCESS = 'Ürün aktifleştirildi.'

interface Core {
  readonly id: string
  readonly code: string
  readonly name: string
  readonly lifecycle: string
  readonly version: string
}

interface Candidates {
  readonly rule: string
  readonly name: string
  readonly exactName: Core[]
  readonly hasMore: boolean
}

function apiHeaders(actor: string, companyId: string): Record<string, string> {
  return { [DEVELOPMENT_USER_HEADER]: actor, 'X-Company-Id': companyId }
}

async function createDraft(page: Page, apiBaseUrl: string, s: Seed, name: string, key: string): Promise<Core> {
  const response = await page.request.post(`${apiBaseUrl}${PRODUCTS_PATH}`, {
    headers: { ...apiHeaders(s.authorizedActor, s.companyId), 'Content-Type': 'application/json', 'Idempotency-Key': key },
    data: { name, groupId: s.codeGroupId },
  })
  expect(response.status(), `the real create of ${name}`).toBe(201)
  return ((await response.json()) as { card: { product: Core } }).card.product
}

function isDuplicateRead(response: Response, excludeProductId: string): boolean {
  const url = new URL(response.url())
  return response.request().method() === 'GET' && url.pathname === DUPLICATES_PATH
    && url.searchParams.get('excludeProductId') === excludeProductId
}

async function candidatesOf(response: Response): Promise<Candidates> {
  expect(response.status(), 'the real duplicate preview').toBe(200)
  const body = (await response.json()) as Candidates
  expect(body.rule).toBe('exactName')
  return body
}

/** Closes whatever is open and opens `path` fresh; the next duplicate read for `productId` is returned. */
async function openFresh(page: Page, path: string, productId: string): Promise<Candidates> {
  await page.goto('about:blank')
  const read = page.waitForResponse((response) => isDuplicateRead(response, productId))
  await page.goto(path)
  return candidatesOf(await read)
}

function warningList(page: Page) {
  return page.getByTestId('product-duplicate-candidates')
}

test('U02-DUP a same-named Draft is saved with a warning that links the other cards, reopens consistently, and approval re-reads the warning before it sends', async ({ page }) => {
  test.setTimeout(180_000)
  const s = seed()
  const apiBaseUrl = required('BROWSER_E2E_API_BASE_URL').replace(/\/$/, '')
  const runTag = randomUUID().slice(0, 8)
  const nameA = `SENTETİK u02 mükerrer ${runTag}`
  const typedB = `sentetik U02 MÜKERRER ${runTag}`

  const a = await test.step('arrange A and two cards the exact-name rule must not match', async () => {
    const created = await createDraft(page, apiBaseUrl, s, nameA, `u02-dup-a-${runTag}`)
    await createDraft(page, apiBaseUrl, s, `${nameA} 2`, `u02-dup-longer-${runTag}`)
    await createDraft(page, apiBaseUrl, s, `SENTETİK u02 mukerrer ${runTag}`, `u02-dup-accent-${runTag}`)
    return created
  })

  const b = await test.step('the authorized actor saves B (other letter cases): 201, and the saved screen links A', async () => {
    // Enter the form the way U02-FIX-01 does (list → "Yeni ürün") and wait for the real code-group read, so the
    // form is filled only after the shell has settled and cannot be remounted under the typed values.
    const groupsAnswer = page.waitForResponse((response) =>
      new URL(response.url()).pathname === `${PRODUCTS_PATH}/code-groups` && response.request().method() === 'GET')
    await page.goto('/urun')
    await expect(page.getByRole('heading', { level: 1, name: 'Ürünler' })).toBeVisible({ timeout: 30_000 })
    await page.getByRole('main').getByRole('link', { name: 'Yeni ürün', exact: true }).click()
    await expect(page.getByRole('heading', { level: 1, name: 'Yeni ürün' })).toBeVisible({ timeout: 30_000 })
    expect((await groupsAnswer).status(), 'the real code-group read').toBe(200)
    await page.getByLabel('Ürün adı').fill(typedB)
    const options = page.waitForResponse((response) => new URL(response.url()).pathname === `${PRODUCTS_PATH}/reference-options`)
    await page.getByLabel('Kod grubu').selectOption(s.codeGroupId)
    expect((await options).status()).toBe(200)
    await expect(page.getByLabel('Ürün adı')).toHaveValue(typedB)
    await expect(page.getByLabel('Kod grubu')).toHaveValue(s.codeGroupId)
    const created = page.waitForResponse((response) =>
      new URL(response.url()).pathname === PRODUCTS_PATH && response.request().method() === 'POST')
    const read = page.waitForResponse((response) => new URL(response.url()).pathname === DUPLICATES_PATH)
    await page.getByRole('button', { name: 'Taslak olarak kaydet' }).click()
    const createdResponse = await created
    expect(createdResponse.status(), 'a name match never blocks the save').toBe(201)
    const product = ((await createdResponse.json()) as { card: { product: Core } }).card.product
    await expect(page.getByRole('heading', { level: 1, name: 'Taslak kaydedildi' })).toBeVisible()

    const candidates = await candidatesOf(await read)
    expect(new URL((await read).url()).searchParams.get('excludeProductId')).toBe(product.id)
    expect(candidates.exactName.map((item) => item.id), 'only A: not the longer name, not the other accent').toEqual([a.id])
    await expect(page.getByText(FOUND_TITLE)).toBeVisible()
    await expect(warningList(page).getByRole('link')).toHaveCount(1)
    await expect(warningList(page).getByRole('link', { name: a.code, exact: true })).toHaveAttribute('href', `/urun/${a.id}`)
    return product
  })

  await test.step('the link opens A, whose warning lists B; reopening B asks again; B edit warns', async () => {
    const read = page.waitForResponse((response) => isDuplicateRead(response, a.id))
    await warningList(page).getByRole('link', { name: a.code, exact: true }).click()
    await expect(page.getByRole('heading', { level: 1, name: nameA })).toBeVisible()
    expect((await candidatesOf(await read)).exactName.map((item) => item.id)).toEqual([b.id])
    await expect(warningList(page).getByRole('link', { name: b.code, exact: true })).toBeVisible()

    const reopened = await openFresh(page, `/urun/${b.id}`, b.id)
    expect(reopened.exactName.map((item) => item.id)).toEqual([a.id])
    await expect(warningList(page).getByRole('link', { name: a.code, exact: true })).toBeVisible()

    const edit = await openFresh(page, `/urun/${b.id}/duzenle`, b.id)
    expect(edit.exactName.map((item) => item.id)).toEqual([a.id])
    await expect(warningList(page).getByRole('link', { name: a.code, exact: true })).toBeVisible()
  })

  await test.step('approval re-reads the warning: a changed list sends nothing; the second press approves', async () => {
    await page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: s.activatorActor })
    const opened = await openFresh(page, `/urun/${b.id}`, b.id)
    expect(opened.exactName.map((item) => item.id)).toEqual([a.id])
    const c = await createDraft(page, apiBaseUrl, s, nameA.toLocaleLowerCase('tr-TR'), `u02-dup-c-${runTag}`)

    const activations: string[] = []
    const watch = (request: BrowserRequest) => {
      if (request.method() === 'POST' && new URL(request.url()).pathname === `${PRODUCTS_PATH}/${b.id}/activation`) {
        activations.push(request.url())
      }
    }
    page.on('request', watch)
    try {
      const reRead = page.waitForResponse((response) => isDuplicateRead(response, b.id))
      await page.getByRole('button', { name: ACTIVATE_BUTTON, exact: true }).click()
      const fresh = await candidatesOf(await reRead)
      expect(fresh.exactName.map((item) => item.id).sort()).toEqual([a.id, c.id].sort())
      await expect(page.getByText(CHANGED_BEFORE_APPROVAL)).toBeVisible()
      await expect(warningList(page).getByRole('link', { name: c.code, exact: true })).toBeVisible()
      expect(activations, 'no approval is sent on the strength of a list no longer on screen').toEqual([])

      const approved = page.waitForResponse((response) =>
        response.request().method() === 'POST' && new URL(response.url()).pathname === `${PRODUCTS_PATH}/${b.id}/activation`)
      await page.getByRole('button', { name: ACTIVATE_BUTTON, exact: true }).click()
      expect((await approved).status(), 'the warning never blocks the approval').toBe(200)
      await expect(page.getByText(ACTIVATE_SUCCESS)).toBeVisible()
      expect(activations).toHaveLength(1)
    } finally {
      page.off('request', watch)
    }

    await page.goto('about:blank')
    await page.goto(`/urun/${b.id}`)
    await expect(page.locator('span[data-lifecycle]')).toHaveAttribute('data-lifecycle', 'Active')
    await expect(page.getByTestId('product-duplicate-check')).toHaveCount(0)

    const fromA = await openFresh(page, `/urun/${a.id}`, a.id)
    expect(fromA.exactName.map((item) => item.id).sort()).toEqual([b.id, c.id].sort())
    expect(fromA.exactName.find((item) => item.id === b.id)?.lifecycle).toBe('Active')
    await expect(warningList(page)).toContainText('Aktif')
  })

  await test.step('the unauthorized actor learns nothing from the preview', async () => {
    const denied = await page.request.get(`${apiBaseUrl}${DUPLICATES_PATH}?name=${encodeURIComponent(nameA)}`, {
      headers: apiHeaders(s.unauthorizedActor, s.companyId),
    })
    expect(denied.status(), 'Product authority is checked before the name is compared').toBe(403)
    const body = await denied.text()
    expect(body).not.toContain(a.code)
    expect(body).not.toContain(runTag)
  })
})
