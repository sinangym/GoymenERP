import { expect, test, type APIRequestContext, type Browser, type Page } from '@playwright/test'
import { editorEnvironment, type EditorEnvironment } from '../src/env'

/**
 * EK-46 in a real browser against the INSTALLED isolated environment (D-ENVIRONMENT=A, synthetic pack only):
 * the work-centre list at 320 px (LF-08), keyboard reach with a visible focus (LF-06), AT-005 with two sessions on
 * the same work centre (LF-12) and AT-006 for a save lost on the network (LF-13). The editor actor changes only the
 * NAME of a synthetic work centre and every reason reads "EK-46 kabul koşusu"; nothing else is written.
 */

const LIST_ROUTE = '/ayarlar/organizasyon/is-merkezleri'
const API_LIST = '/api/v1/organization/work-centers'
const HEADING = 'İş Merkezleri'
const EDIT_LABEL = 'İş merkezini düzenle'
const REASON = 'EK-46 kabul koşusu'
const DIFF_TABLE = 'Girdiğiniz değerler ile güncel kayıt'
const SAVED = /Değişiklik kaydedildi|Aynı işlem zaten kaydedilmişti/
/** Keeps a run's marker inside the 200-character name limit whatever the seeded name is. */
const NAME_PREFIX_LENGTH = 120
const MAX_TAB_STEPS = 40

interface WorkCentre {
  readonly id: string
  readonly name: string
}

/** A context made from `browser` does not inherit the config's `use`, so the base URL and locale are passed again. */
async function editorPage(browser: Browser, baseURL: string | undefined, environment: EditorEnvironment): Promise<Page> {
  expect(baseURL, 'the isolated acceptance config sets the base URL').toBeTruthy()
  const context = await browser.newContext({
    baseURL,
    locale: 'tr-TR',
    extraHTTPHeaders: { [environment.header]: environment.editorA },
  })
  return context.newPage()
}

async function firstWorkCentre(request: APIRequestContext, environment: EditorEnvironment): Promise<WorkCentre> {
  const response = await request.get(`${environment.apiBaseUrl}${API_LIST}`, {
    headers: { [environment.header]: environment.editorA, 'X-Company-Id': environment.companyA },
  })
  expect(response.status()).toBe(200)
  const rows = (await response.json()) as unknown
  expect(Array.isArray(rows) && rows.length > 0, 'the synthetic pack must seed a work centre in company A').toBe(true)
  const [row] = rows as Record<string, unknown>[]
  expect(typeof row.id === 'string' && typeof row.name === 'string', 'a work-centre row has no id or name').toBe(true)
  return { id: row.id as string, name: row.name as string }
}

function runName(base: string, session: string): string {
  return `${base.slice(0, NAME_PREFIX_LENGTH)} EK46-${session}-${Date.now().toString(36)}`
}

async function openEditor(page: Page, workCentreId: string): Promise<void> {
  await page.goto(`${LIST_ROUTE}/${encodeURIComponent(workCentreId)}`)
  await page.getByRole('button', { name: EDIT_LABEL }).click({ timeout: 30_000 })
  await expect(page.getByLabel('Ad', { exact: true })).toBeVisible()
}

async function typeAndSave(page: Page, name: string): Promise<void> {
  await page.getByLabel('Ad', { exact: true }).fill(name)
  await page.getByLabel('Gerekçe', { exact: true }).fill(REASON)
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click()
}

test('EK46-01 the work-centre list scrolls no page sideways at 320 px', async ({ browser, baseURL }) => {
  const environment = editorEnvironment()
  const page = await editorPage(browser, baseURL, environment)
  await page.setViewportSize({ width: 320, height: 640 })
  await page.goto(LIST_ROUTE)
  await expect(page.getByRole('heading', { level: 1, name: HEADING })).toBeVisible({ timeout: 30_000 })
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)
  expect(overflow, 'the page itself scrolls sideways at 320 px').toBeLessThanOrEqual(0)
  await page.context().close()
})

test('EK46-02 the skip link and the edit action are reachable by keyboard with a visible focus', async ({ browser, baseURL, request }) => {
  const environment = editorEnvironment()
  const workCentre = await firstWorkCentre(request, environment)
  const page = await editorPage(browser, baseURL, environment)
  await page.goto(`${LIST_ROUTE}/${encodeURIComponent(workCentre.id)}`)
  const edit = page.getByRole('button', { name: EDIT_LABEL })
  await expect(edit).toBeVisible({ timeout: 30_000 })

  await page.keyboard.press('Tab')
  await expect(page.getByRole('link', { name: 'Ana içeriğe geç' })).toBeFocused()
  await page.keyboard.press('Enter')
  let reached = false
  for (let step = 0; step < MAX_TAB_STEPS && !reached; step += 1) {
    await page.keyboard.press('Tab')
    reached = await edit.evaluate((element) => element === document.activeElement)
  }
  expect(reached, 'the edit action cannot be reached with Tab from the main content').toBe(true)
  const outline = await edit.evaluate((element) => getComputedStyle(element).outlineStyle)
  expect(outline, 'the focused action draws no outline').not.toBe('none')

  await page.keyboard.press('Enter')
  await expect(page.getByLabel('Kod', { exact: true })).toBeFocused()
  await page.context().close()
})

test('EK46-03 AT-005 the second session sees the difference, keeps its input and saves on the fresh version', async ({ browser, baseURL, request }) => {
  const environment = editorEnvironment()
  const workCentre = await firstWorkCentre(request, environment)
  const first = await editorPage(browser, baseURL, environment)
  const second = await editorPage(browser, baseURL, environment)
  await openEditor(first, workCentre.id)
  await openEditor(second, workCentre.id)

  const firstName = runName(workCentre.name, 'a')
  await typeAndSave(first, firstName)
  await expect(first.getByText(SAVED)).toBeVisible({ timeout: 30_000 })

  const secondName = runName(workCentre.name, 'b')
  await typeAndSave(second, secondName)
  const notice = second.getByRole('alert').filter({ hasText: 'Kayıt bu sırada değişti' })
  await expect(notice).toBeVisible({ timeout: 30_000 })
  await expect(second.getByLabel('Ad', { exact: true })).toHaveValue(secondName)
  await notice.getByRole('button', { name: 'Güncel kaydı yükle' }).click()

  const diff = second.getByRole('table', { name: DIFF_TABLE })
  await expect(diff).toBeVisible({ timeout: 30_000 })
  await expect(diff.getByRole('row').nth(1).getByRole('cell')).toHaveText(['Ad', secondName, firstName])
  await expect(second.getByLabel('Ad', { exact: true })).toHaveValue(secondName)
  await expect(second.getByLabel('Gerekçe', { exact: true })).toHaveValue(REASON)

  await second.getByRole('button', { name: 'Kaydet', exact: true }).click()
  await expect(second.getByText(SAVED)).toBeVisible({ timeout: 30_000 })
  await expect(second.getByRole('table', { name: DIFF_TABLE })).toHaveCount(0)
  await first.context().close()
  await second.context().close()
})

test('EK46-04 AT-006 a save lost on the network keeps the input and resends with the same key', async ({ browser, baseURL, request }) => {
  const environment = editorEnvironment()
  const workCentre = await firstWorkCentre(request, environment)
  const page = await editorPage(browser, baseURL, environment)
  await openEditor(page, workCentre.id)
  const keys: string[] = []
  let dropNext = true
  await page.route(`**${API_LIST}/${encodeURIComponent(workCentre.id)}`, async (route) => {
    const sent = route.request()
    if (sent.method() !== 'PUT') return route.fallback()
    keys.push(sent.headers()['idempotency-key'] ?? '')
    if (dropNext) {
      dropNext = false
      return route.abort('connectionreset')
    }
    return route.fallback()
  })

  const name = runName(workCentre.name, 'c')
  await typeAndSave(page, name)
  const unknown = page.locator('[data-outcome="unknown"]')
  await expect(unknown).toBeVisible({ timeout: 30_000 })
  const field = page.getByLabel('Ad', { exact: true })
  await expect(field).toHaveValue(name)
  await expect(field).toHaveAttribute('readonly', '')
  await unknown.getByRole('button', { name: 'Aynı işlem anahtarıyla tekrar gönder' }).click()
  await expect(page.getByText(SAVED)).toBeVisible({ timeout: 30_000 })
  expect(keys).toHaveLength(2)
  expect(keys[0], 'the first attempt carried no idempotency key').not.toBe('')
  expect(keys[1], 'the resend minted a new key').toBe(keys[0])
  await page.context().close()
})
