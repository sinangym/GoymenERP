import { expect, test, type Locator, type Page, type Request, type Response as PageResponse } from '@playwright/test'
import { EDITOR_ACTOR, GDC_COMPANY_ID, READER_ACTOR, WORK_CENTERS_ONLY_ACTOR, readEnvironment } from '../src/env'

/**
 * ORG-03 (`browser:org03:ci`) — the work-centre list, in a real browser against the disposable stack
 * built from the exact head: real API, real SQL, the `normal` capability profile, the authorized web.
 *
 * WHAT THIS SUITE IS FOR. The slice's claim is a BROWSER fact and cannot be shown anywhere else: a user
 * holding `organization.work-centers.read` and NOT `organization.locations.read` sees the İş Merkezleri
 * entry in the sidebar and opens ORG-03 from it. Unit and component layers can show the screen renders;
 * only this layer shows the ENTRY EXISTS for that user — and that ORG-01, the screen a link would have
 * lived inside, does not open for them at all. That pair is why ORG-03 is its own sidebar entry (K5).
 *
 * The ORG-03/ORG-04 read cases write nothing: everything they read is in the built-in organization seed (five
 * work centres across four locations of the GDC company), so the suite needs no fixture and no seed envelope.
 * The ORG master-edit cases (ORG-ME-*, at the end of this file) WRITE through the real screens as the disposable
 * org03-editor; they are declared last because the read cases count the seeded work centres exactly.
 *
 * WHAT IT DOES NOT COVER, so nobody reads its green as more than it is: the MIXED case — some rows
 * openable, some not — is absent, because the browser-suite contract refuses a narrowly-scoped actor
 * (see infra/browser-suites/org03.override.yml). That is the case `canOpenLocationDetail` exists to
 * discriminate, so browser evidence for the field itself remains owed. Covered here are the two ends:
 * all rows closed, and a row that opens.
 *
 * ALSO STILL OWED, for the ORG-04 card: real-browser evidence of the COMPANY-SWITCH stale fence — that a
 * late answer for the company the user just left never renders under the new one, and that the card's
 * filtered way back is dropped when the committed context changes. Neither org03 actor can switch company
 * (each holds an assignment in GDC alone — infra/browser-suites/org03.override.yml), so no case here
 * exercises it. The only evidence is the
 * component layer (`WorkCenterDetailPage.test.tsx`, synthetic fetch fixtures), which is not browser
 * acceptance; this suite's green says nothing about it.
 *
 * The ORG-04 flow is walked with the KEYBOARD as well as the pointer (ORG04-03): Tab to the row's card
 * link, Enter to open, Tab to the card's back link, Enter to return.
 */

const { testedCommit, apiBaseUrl } = readEnvironment()

const WORK_CENTRE_LIST_ROUTE = '/ayarlar/organizasyon/is-merkezleri'
const LOCATION_LIST_ROUTE = '/ayarlar/organizasyon'
const NAV_LABEL = 'İş Merkezleri'
const DEVELOPMENT_USER_HEADER = 'X-Development-User'
// The built-in GDC organization operator. The disposable editor creates master data, while this
// existing public persona holds the separately authorized location lifecycle command.
const LOCATION_STATUS_ACTOR = 'company-a-user'

/** The five seeded work centres of GDC, by code — the whole authorized set for a company-wide reader. */
const SEEDED_WORK_CENTRE_CODES = ['WC-PAINT', 'WC-TUBE', 'WC-SLIT', 'WC-CR', 'WC-CUT'] as const

/** The sentence the screen shows in place of the link when the server says the card is out of reach. */
const CLOSED_LOCATION_TEXT = 'Lokasyon kartı bu yetkiyle açılamıyor'

/** The location link's own name, so a count never includes ORG-04's work-centre card links. */
const LOCATION_LINK = 'Lokasyon kartını aç'

/** ORG-04: each row's code opens its own work-centre card, on the same grant as the list. */
const CARD_LINK = /^İş merkezi kartını aç: /
const WORK_CENTRE_CARD_API = /^\/api\/v1\/organization\/work-centers\/[0-9a-f-]{36}$/

async function signInAs(page: Page, actor: string): Promise<void> {
  await page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: actor })
}

/** One row of `GET /organization/work-centers`, as far as these cases read it. */
interface WorkCentreRow {
  readonly id: string
  readonly locationId: string
  readonly code: string
  readonly name: string
  readonly isActive: boolean
  readonly canOpenLocationDetail: boolean
}

function isWorkCentreListRead(response: PageResponse): boolean {
  return response.request().method() === 'GET'
    && new URL(response.url()).pathname === '/api/v1/organization/work-centers'
}

/** The seeded set as the server answered it — the source a case derives its row from, never a typed code. */
async function readListAnswer(answer: Promise<PageResponse>): Promise<readonly WorkCentreRow[]> {
  const response = await answer
  expect(response.status()).toBe(200)
  const rows = await response.json() as readonly WorkCentreRow[]
  expect(Array.isArray(rows)).toBe(true)
  expect(rows.map((row) => row.code).sort()).toEqual([...SEEDED_WORK_CENTRE_CODES].sort())
  return rows
}

/**
 * Moves focus forward with Tab, one awaited key press at a time, until `target` holds it. No sleep and no
 * timing: each press is a real keyboard event and each check reads `document.activeElement`. The bound only
 * turns "never reachable by Tab" into a named failure instead of an endless loop.
 */
async function tabTo(page: Page, target: Locator, limit = 100): Promise<void> {
  await expect(target).toBeVisible()
  for (let presses = 0; presses < limit; presses += 1) {
    if (await target.evaluate((element) => element === document.activeElement)) break
    await page.keyboard.press('Tab')
  }
  await expect(target, `reachable with Tab within ${limit} presses`).toBeFocused()
}

/** The sidebar entry, scoped to the nav landmark so it can never be confused with the screen's own heading. */
function navEntry(page: Page) {
  return page.getByRole('navigation', { name: 'Modül navigasyonu' }).getByRole('link', { name: NAV_LABEL })
}

test.beforeAll(() => {
  // The run is bound to the head it was built from; a suite that cannot say which commit it tested is
  // not evidence about any commit.
  expect(testedCommit).toMatch(/^[0-9a-f]{40}$/)
})

test('ORG03-01 a work-centre-only reader reaches the screen FROM THE SIDEBAR, and every row says the location card is closed', async ({ page }) => {
  await signInAs(page, WORK_CENTERS_ONLY_ACTOR)
  await page.goto('/')

  // The entry point itself is the claim. Navigating straight to the route would prove the route opens
  // and say nothing about whether this user could ever have found it.
  const entry = navEntry(page)
  await expect(entry).toBeVisible({ timeout: 30_000 })
  await expect(entry).toHaveAttribute('href', WORK_CENTRE_LIST_ROUTE)
  await entry.click()

  await expect(page.getByRole('heading', { level: 1, name: NAV_LABEL })).toBeVisible({ timeout: 30_000 })
  const table = page.getByRole('table')
  await expect(table).toBeVisible()
  for (const code of SEEDED_WORK_CENTRE_CODES) {
    await expect(table.getByText(code, { exact: true }), `seeded work centre ${code} is listed`).toBeVisible()
  }

  // EVERY row is closed for this actor, and it is asserted as a COUNT, not as "at least one": a single
  // closed row would also be true if the others wrongly linked.
  await expect(table.getByText(CLOSED_LOCATION_TEXT)).toHaveCount(SEEDED_WORK_CENTRE_CODES.length)
  // And the screen draws no location link at all — the inverse of the same fact, so a renamed sentence
  // cannot make the first assertion pass vacuously. (ORG-04's card links are the row's own record, on the
  // same work-centre grant, so they are counted apart.)
  await expect(table.getByRole('link', { name: LOCATION_LINK })).toHaveCount(0)
  await expect(table.getByRole('link', { name: CARD_LINK })).toHaveCount(SEEDED_WORK_CENTRE_CODES.length)
  // As strict as the pre-ORG-04 "no link at all": the table's links, counted WITHOUT a name, are exactly the
  // card links. A location link under any name — a renamed one included — would make this count larger.
  await expect(table.getByRole('link')).toHaveCount(SEEDED_WORK_CENTRE_CODES.length)
})

test('ORG03-02 the same reader has NO location list to reach it from: ORG-01 is neither in the sidebar nor openable', async ({ page }) => {
  await signInAs(page, WORK_CENTERS_ONLY_ACTOR)
  await page.goto('/')
  await expect(navEntry(page)).toBeVisible({ timeout: 30_000 })

  // This is the other half of K5 and the reason ORG-03 is not a link inside ORG-01: for this user that
  // screen does not exist. An entry point inside a screen they cannot open is no entry point.
  await expect(
    page.getByRole('navigation', { name: 'Modül navigasyonu' }).getByRole('link', { name: 'Organizasyon' }),
  ).toHaveCount(0)

  await page.goto(LOCATION_LIST_ROUTE)
  // Neutral not-found, exactly as an unknown path gets: an ungranted route must not be distinguishable
  // from one that does not exist.
  await expect(page.getByRole('heading', { level: 1, name: 'Organizasyon' })).toHaveCount(0)
  await expect(page.getByRole('alert')).toBeVisible({ timeout: 30_000 })
})

test('ORG03-03 a reader holding the location name as well gets the link, and it opens the location card', async ({ page }) => {
  await signInAs(page, READER_ACTOR)
  await page.goto('/')

  const entry = navEntry(page)
  await expect(entry).toBeVisible({ timeout: 30_000 })
  await entry.click()
  const table = page.getByRole('table')
  await expect(table).toBeVisible({ timeout: 30_000 })

  // The other end of the pair: with the location name, every row links and none carries the closed
  // sentence. Both directions again, so neither can pass by the screen simply rendering nothing.
  await expect(table.getByText(CLOSED_LOCATION_TEXT)).toHaveCount(0)
  await expect(table.getByRole('link', { name: LOCATION_LINK })).toHaveCount(SEEDED_WORK_CENTRE_CODES.length)

  await table.getByRole('link', { name: LOCATION_LINK }).first().click()
  // ORG-02 opened for real — the link is not merely present, it resolves through the real API.
  await expect(page).toHaveURL(/\/ayarlar\/organizasyon\/lokasyon\/[0-9a-f-]{36}$/, { timeout: 30_000 })
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible({ timeout: 30_000 })

  const back = page.getByRole('navigation', { name: 'Geri' }).getByRole('link', { name: /İş Merkezleri/ })
  await expect(back).toHaveAttribute('href', WORK_CENTRE_LIST_ROUTE)
  await back.click()
  await expect(page).toHaveURL(new RegExp(`${WORK_CENTRE_LIST_ROUTE}$`))
  await expect(page.getByRole('table')).toBeVisible()
})


test('ORG03-04 location card opens the exact authorized work-center subset and safely restores/clears it', async ({ page }) => {
  await signInAs(page, READER_ACTOR)
  await page.goto('/')
  const entry = navEntry(page)
  await expect(entry).toBeVisible({ timeout: 30_000 })
  const listAnswer = page.waitForResponse((response) => response.request().method() === 'GET'
    && new URL(response.url()).pathname === '/api/v1/organization/work-centers')
  await entry.click()
  const initial = await listAnswer
  expect(initial.status()).toBe(200)
  const rows = await initial.json() as readonly {
    readonly id: string; readonly locationId: string; readonly code: string; readonly canOpenLocationDetail: boolean
  }[]
  expect(Array.isArray(rows)).toBe(true)
  expect(rows).toHaveLength(SEEDED_WORK_CENTRE_CODES.length)
  const selected = rows.find((row) => row.canOpenLocationDetail)
  expect(selected).toBeDefined()
  if (!selected) throw new Error('Existing ORG03 reader has no openable seeded location')
  const locationId = selected.locationId
  const expected = rows.filter((row) => row.locationId === locationId)
  expect(expected.length).toBeGreaterThan(0)
  expect(expected.length).toBeLessThan(rows.length)
  const locationRoute = `${LOCATION_LIST_ROUTE}/lokasyon/${locationId}`
  const filteredRoute = `${WORK_CENTRE_LIST_ROUTE}?locationId=${locationId}`
  await page.getByRole('row').filter({ has: page.getByText(selected.code, { exact: true }) })
    .getByRole('link', { name: 'Lokasyon kartını aç' }).click()
  await expect(page).toHaveURL(new RegExp(`${locationRoute}$`))
  const action = page.getByRole('link', { name: 'Bu lokasyondaki iş merkezleri', exact: true })
  await expect(action).toHaveAttribute('href', filteredRoute)
  const filteredAnswer = page.waitForResponse((response) => response.request().method() === 'GET'
    && new URL(response.url()).pathname === '/api/v1/organization/work-centers')
  await action.click()
  const read = await filteredAnswer
  expect(read.status()).toBe(200)
  expect(new URL(read.url()).search).toBe('') // The filter is not an invented server API.
  const currentRows = await read.json() as typeof rows
  expect(currentRows.map((row) => row.id).sort()).toEqual(rows.map((row) => row.id).sort())
  const table = page.getByRole('table')
  await expect(table.getByRole('row')).toHaveCount(expected.length + 1)
  for (const row of rows) {
    await expect(table.getByText(row.code, { exact: true })).toHaveCount(row.locationId === locationId ? 1 : 0)
  }
  await table.getByRole('link', { name: 'Lokasyon kartını aç' }).first().click()
  await expect(page).toHaveURL(new RegExp(`${locationRoute}$`))
  const back = page.getByRole('navigation', { name: 'Geri' }).getByRole('link', { name: /İş Merkezleri/ })
  await expect(back).toHaveAttribute('href', filteredRoute)
  await back.click()
  await expect(page.getByRole('table').getByRole('row')).toHaveCount(expected.length + 1)
  await page.getByRole('link', { name: 'Filtreyi temizle', exact: true }).click()
  await expect(page).toHaveURL(new RegExp(`${WORK_CENTRE_LIST_ROUTE}$`))
  await expect(page.getByRole('table').getByRole('row')).toHaveCount(rows.length + 1)

  const probes: string[] = []
  page.on('request', (request) => {
    if (new URL(request.url()).pathname === '/api/v1/organization/work-centers') probes.push(request.url())
  })
  await page.goto(`${WORK_CENTRE_LIST_ROUTE}?locationId=${locationId}&locationId=${locationId}`)
  await expect(page.getByText('Beklenmeyen yanıt — lokasyon filtresi', { exact: true })).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
  expect(probes).toEqual([])
})

test('ORG04-01 a work-centre-only reader opens a row card, sees its record and a closed location, and returns', async ({ page }) => {
  await signInAs(page, WORK_CENTERS_ONLY_ACTOR)
  const locationProbes: string[] = []
  page.on('request', (request) => {
    if (new URL(request.url()).pathname.startsWith('/api/v1/organization/locations')) locationProbes.push(request.url())
  })
  await page.goto('/')
  const entry = navEntry(page)
  await expect(entry).toBeVisible({ timeout: 30_000 })
  const listAnswer = page.waitForResponse((response) => response.request().method() === 'GET'
    && new URL(response.url()).pathname === '/api/v1/organization/work-centers')
  await entry.click()
  const rows = await (await listAnswer).json() as readonly {
    readonly id: string; readonly code: string; readonly name: string; readonly isActive: boolean
    readonly canOpenLocationDetail: boolean
  }[]
  const selected = rows.find((row) => row.code === 'WC-SLIT')
  expect(selected).toBeDefined()
  if (!selected) throw new Error('The seeded WC-SLIT work centre is missing')

  const cardAnswer = page.waitForResponse((response) => response.request().method() === 'GET'
    && WORK_CENTRE_CARD_API.test(new URL(response.url()).pathname))
  await page.getByRole('table').getByRole('link', { name: `İş merkezi kartını aç: ${selected.code}` }).click()
  const card = await cardAnswer
  expect(card.status()).toBe(200)
  expect(new URL(card.url()).pathname).toBe(`/api/v1/organization/work-centers/${selected.id}`)
  const body = await card.json() as Record<string, unknown>
  // The card is the listed row plus its version, read by the same scope predicate.
  expect(body).toMatchObject({
    id: selected.id,
    code: selected.code,
    name: selected.name,
    isActive: selected.isActive,
    canOpenLocationDetail: false,
  })
  expect(typeof body.version).toBe('number')

  await expect(page).toHaveURL(new RegExp(`${WORK_CENTRE_LIST_ROUTE}/${selected.id}$`))
  await expect(page.getByRole('heading', { level: 1, name: 'İş merkezi' })).toBeVisible({ timeout: 30_000 })
  await expect(page.getByText(selected.code, { exact: true })).toBeVisible()
  await expect(page.getByText(CLOSED_LOCATION_TEXT)).toBeVisible()
  await expect(page.getByRole('link', { name: LOCATION_LINK })).toHaveCount(0)
  const record = page.locator(`[data-work-center="${selected.id}"]`)
  await expect(record).toBeVisible()
  // The record draws no link of any name: a closed location is a sentence, not a renamed link.
  await expect(record.getByRole('link')).toHaveCount(0)
  // No probe: the card never asks for a location it was told it cannot open.
  expect(locationProbes).toEqual([])

  const back = page.getByRole('navigation', { name: 'Geri' }).getByRole('link', { name: /İş Merkezleri/ })
  await expect(back).toHaveAttribute('href', WORK_CENTRE_LIST_ROUTE)
  await back.click()
  await expect(page).toHaveURL(new RegExp(`${WORK_CENTRE_LIST_ROUTE}$`))
  await expect(page.getByRole('table')).toBeVisible()
  await expect(page.locator('[data-work-center]')).toHaveCount(0)

  // Close and REOPEN: a fresh route entry is a fresh read of the same record, and the card it draws is that
  // record again — its own id and its own name, not whatever the first visit left behind.
  const reopenAnswer = page.waitForResponse((response) => response.request().method() === 'GET'
    && WORK_CENTRE_CARD_API.test(new URL(response.url()).pathname))
  await page.getByRole('table').getByRole('link', { name: `İş merkezi kartını aç: ${selected.code}` }).click()
  const reopened = await reopenAnswer
  expect(reopened.status()).toBe(200)
  expect(new URL(reopened.url()).pathname).toBe(`/api/v1/organization/work-centers/${selected.id}`)
  await expect(page).toHaveURL(new RegExp(`${WORK_CENTRE_LIST_ROUTE}/${selected.id}$`))
  const again = page.locator(`[data-work-center="${selected.id}"]`)
  await expect(again).toBeVisible({ timeout: 30_000 })
  await expect(again.getByText(selected.name, { exact: true })).toBeVisible()
  await expect(again.getByText(selected.code, { exact: true })).toBeVisible()
  await expect(page.locator('[data-work-center]')).toHaveCount(1)
  expect(locationProbes).toEqual([])
})

test('ORG04-02 a reader with the location read goes list -> card -> location card -> back to the work-centre list', async ({ page }) => {
  await signInAs(page, READER_ACTOR)
  await page.goto('/')
  const entry = navEntry(page)
  await expect(entry).toBeVisible({ timeout: 30_000 })
  const listAnswer = page.waitForResponse(isWorkCentreListRead)
  await entry.click()
  // The row comes from the server's own answer for this actor, not from a code typed here: the first row
  // the server says opens its location card.
  const rows = await readListAnswer(listAnswer)
  const selected = rows.find((row) => row.canOpenLocationDetail)
  expect(selected, 'the reader has a seeded row whose location card opens').toBeDefined()
  if (!selected) throw new Error('The ORG03 reader has no openable seeded location')
  const locationRoute = `${LOCATION_LIST_ROUTE}/lokasyon/${selected.locationId}`
  const table = page.getByRole('table')
  await expect(table).toBeVisible({ timeout: 30_000 })

  await table.getByRole('link', { name: `İş merkezi kartını aç: ${selected.code}` }).click()
  await expect(page).toHaveURL(new RegExp(`${WORK_CENTRE_LIST_ROUTE}/${selected.id}$`))
  await expect(page.getByRole('heading', { level: 1, name: 'İş merkezi' })).toBeVisible({ timeout: 30_000 })
  await expect(page.locator(`[data-work-center="${selected.id}"]`)).toBeVisible()
  const location = page.getByRole('link', { name: LOCATION_LINK })
  // Exactly this record's location, not "some location".
  await expect(location).toHaveAttribute('href', locationRoute)
  const locationAnswer = page.waitForResponse((response) => response.request().method() === 'GET'
    && /^\/api\/v1\/organization\/locations\/[0-9a-f-]{36}$/.test(new URL(response.url()).pathname))
  await location.click()
  const locationRead = await locationAnswer
  expect(locationRead.status()).toBe(200)
  expect(new URL(locationRead.url()).pathname).toBe(`/api/v1/organization/locations/${selected.locationId}`)
  await expect(page).toHaveURL(new RegExp(`${locationRoute}$`), { timeout: 30_000 })

  // The location card's own back link (#434) returns to the work-centre list the journey started from.
  const back = page.getByRole('navigation', { name: 'Geri' }).getByRole('link', { name: /İş Merkezleri/ })
  await expect(back).toHaveAttribute('href', WORK_CENTRE_LIST_ROUTE)
  await back.click()
  await expect(page).toHaveURL(new RegExp(`${WORK_CENTRE_LIST_ROUTE}$`))
  await expect(page.getByRole('table')).toBeVisible()
})

test('ORG04-03 the ORG-04 flow by keyboard alone: Tab to a row\'s card, Enter to open it, Tab to the back link, Enter to return', async ({ page }) => {
  await signInAs(page, WORK_CENTERS_ONLY_ACTOR)
  await page.goto('/')
  const entry = navEntry(page)
  await expect(entry).toBeVisible({ timeout: 30_000 })

  // The sidebar entry, by keyboard too: the list is reached without a pointer.
  const listAnswer = page.waitForResponse(isWorkCentreListRead)
  await tabTo(page, entry)
  await page.keyboard.press('Enter')
  const rows = await readListAnswer(listAnswer)
  await expect(page).toHaveURL(new RegExp(`${WORK_CENTRE_LIST_ROUTE}$`))
  const table = page.getByRole('table')
  await expect(table).toBeVisible({ timeout: 30_000 })

  // The LAST row the server lists, so reaching it means Tab walked every earlier row's link, not just one.
  const selected = rows[rows.length - 1]
  const cardLink = table.getByRole('link', { name: `İş merkezi kartını aç: ${selected.code}` })
  await tabTo(page, cardLink)

  const cardAnswer = page.waitForResponse((response) => response.request().method() === 'GET'
    && WORK_CENTRE_CARD_API.test(new URL(response.url()).pathname))
  await page.keyboard.press('Enter')
  const card = await cardAnswer
  expect(card.status()).toBe(200)
  expect(new URL(card.url()).pathname).toBe(`/api/v1/organization/work-centers/${selected.id}`)
  await expect(page).toHaveURL(new RegExp(`${WORK_CENTRE_LIST_ROUTE}/${selected.id}$`))
  const record = page.locator(`[data-work-center="${selected.id}"]`)
  await expect(record).toBeVisible({ timeout: 30_000 })
  await expect(record.getByText(selected.name, { exact: true })).toBeVisible()

  // The way back is reachable by keyboard and leads where its href says.
  const back = page.getByRole('navigation', { name: 'Geri' }).getByRole('link', { name: /İş Merkezleri/ })
  await expect(back).toHaveAttribute('href', WORK_CENTRE_LIST_ROUTE)
  await tabTo(page, back)
  await page.keyboard.press('Enter')
  await expect(page).toHaveURL(new RegExp(`${WORK_CENTRE_LIST_ROUTE}$`))
  await expect(page.getByRole('table')).toBeVisible({ timeout: 30_000 })
  await expect(page.locator('[data-work-center]')).toHaveCount(0)
})

// ── ORG master edit: create and edit sites, locations and work centres ─────────────────────────────────────
// These cases WRITE, so they run last: every read case above counts the five seeded work centres exactly.
// Only org03-editor holds the three writes (infra/browser-suites/org03.override.yml, user decision 2026-09-27).

const SITES_API = '/api/v1/organization/sites'
const LOCATIONS_API = '/api/v1/organization/locations'
const WORK_CENTERS_API = '/api/v1/organization/work-centers'

const NEW_SITE = { code: 'ORG03-ME-SITE', name: 'SENTETIK org03 düzenleme deposu', edited: 'SENTETIK org03 düzenlenmiş depo' }
const NEW_LOCATION = {
  code: 'ORG03-ME-LOC',
  name: 'SENTETIK org03 lokasyon',
  editedCode: 'ORG03-ME-LOC2',
  editedName: 'SENTETIK org03 lokasyon (düzenlendi)',
}
const NEW_WORK_CENTRE = { code: 'ORG03-ME-WC', name: 'SENTETIK org03 iş merkezi', editedName: 'SENTETIK org03 iş merkezi (düzenlendi)' }
const UNKNOWN_SITE = {
  code: 'ORG03-ME-LOSS',
  name: 'SENTETIK org03 yanıt kaybı tesisi',
  reason: 'SENTETIK org03: kaydedilen yanıt ulaşmadı',
}
const LIFECYCLE_SITE = {
  code: `ORG03-LC-${testedCommit.slice(0, 8).toUpperCase()}`,
  name: 'SENTETIK org03 yaşam döngüsü tesisi',
}
const LIFECYCLE_LOCATION = {
  code: `ORG03-LC-LOC-${testedCommit.slice(0, 6).toUpperCase()}`,
  name: 'SENTETIK org03 yaşam döngüsü lokasyonu',
  deactivateReason: 'SENTETIK org03: lokasyonu geçici olarak pasife alma kabulü',
  reactivateReason: 'SENTETIK org03: lokasyonu yeniden aktifleştirme kabulü',
}

function apiHeaders(actor: string): Record<string, string> {
  return { [DEVELOPMENT_USER_HEADER]: actor, 'X-Company-Id': GDC_COMPANY_ID }
}

function commandHeaders(actor: string, key: string): Record<string, string> {
  return { ...apiHeaders(actor), 'Content-Type': 'application/json', 'Idempotency-Key': key }
}

function isCall(response: PageResponse, method: string, path: string): boolean {
  return response.request().method() === method && new URL(response.url()).pathname === path
}

interface MasterChoiceValue {
  readonly label: string
  readonly option: string | RegExp
}

/** The form region is named by its heading; every field is reached by its visible label. */
async function fillMasterForm(form: Locator, values: {
  readonly code?: string
  readonly name?: string
  readonly choice?: MasterChoiceValue
  readonly secondChoice?: MasterChoiceValue
  readonly reason: string
}): Promise<void> {
  await expect(form).toBeVisible({ timeout: 30_000 })
  if (values.code !== undefined) await form.getByLabel('Kod', { exact: true }).fill(values.code)
  if (values.name !== undefined) await form.getByLabel('Ad', { exact: true }).fill(values.name)
  for (const choice of [values.choice, values.secondChoice]) {
    if (choice === undefined) continue
    const select = form.getByLabel(choice.label, { exact: true })
    const options = await select.locator('option').allTextContents()
    const label = options.find((text) => typeof choice.option === 'string' ? text === choice.option : choice.option.test(text))
    expect(label, `option ${String(choice.option)} is offered by ${choice.label}`).toBeDefined()
    await select.selectOption({ label: label ?? '' })
  }
  await form.getByLabel('Gerekçe', { exact: true }).fill(values.reason)
}

test('ORG-ME-01 the editor creates a site, a location under it and a work centre there, edits each, and every card reopens with the stored values', async ({ page }) => {
  await signInAs(page, EDITOR_ACTOR)

  // ── Site: create on ORG-01, then edit its name. ──────────────────────────────────────────────────
  await page.goto(LOCATION_LIST_ROUTE)
  await expect(page.getByRole('heading', { level: 1, name: 'Organizasyon' })).toBeVisible({ timeout: 30_000 })
  await page.getByRole('button', { name: 'Yeni tesis', exact: true }).click()
  const siteForm = page.getByRole('region', { name: 'Yeni tesis' })
  await fillMasterForm(siteForm, {
    code: NEW_SITE.code,
    name: NEW_SITE.name,
    choice: { label: 'Tesis türü', option: 'Depo' },
    reason: 'SENTETIK org03: tesis oluşturma',
  })
  const siteCreated = page.waitForResponse((response) => isCall(response, 'POST', SITES_API))
  await siteForm.getByRole('button', { name: 'Kaydet', exact: true }).click()
  const siteAnswer = await siteCreated
  expect(siteAnswer.status()).toBe(201)
  const site = await siteAnswer.json() as { readonly id: string; readonly version: number }
  expect(site.id).toMatch(/^[0-9a-f-]{36}$/)
  await expect(page.getByRole('status').filter({ hasText: 'Kayıt oluşturuldu' })).toBeVisible({ timeout: 30_000 })
  await expect(page.getByText(NEW_SITE.name, { exact: true })).toBeVisible()

  await page.getByRole('button', { name: `Tesisi düzenle: ${NEW_SITE.code}`, exact: true }).click()
  const siteEdit = page.getByRole('region', { name: `Tesisi düzenle: ${NEW_SITE.code}` })
  await fillMasterForm(siteEdit, { name: NEW_SITE.edited, reason: 'SENTETIK org03: tesis adı düzeltme' })
  const siteUpdated = page.waitForResponse((response) => isCall(response, 'PUT', `${SITES_API}/${site.id}`))
  await siteEdit.getByRole('button', { name: 'Kaydet', exact: true }).click()
  expect((await siteUpdated).status()).toBe(204)
  await expect(page.getByRole('status').filter({ hasText: 'Değişiklik kaydedildi' })).toBeVisible({ timeout: 30_000 })

  // Close and reopen: a fresh page load reads the stored site, not the form's memory.
  await page.goto('/')
  await page.goto(LOCATION_LIST_ROUTE)
  await expect(page.getByText(NEW_SITE.edited, { exact: true })).toBeVisible({ timeout: 30_000 })
  await expect(page.getByText(NEW_SITE.name, { exact: true })).toHaveCount(0)

  // ── Location: create under the new site; the app opens its card; edit code and name there. ────────
  await page.getByRole('button', { name: 'Yeni lokasyon', exact: true }).click()
  const locationForm = page.getByRole('region', { name: 'Yeni lokasyon' })
  await fillMasterForm(locationForm, {
    code: NEW_LOCATION.code,
    name: NEW_LOCATION.name,
    choice: { label: 'Tesis', option: new RegExp(`^${NEW_SITE.code} — `) },
    reason: 'SENTETIK org03: lokasyon oluşturma',
  })
  const locationCreated = page.waitForResponse((response) => isCall(response, 'POST', LOCATIONS_API))
  await locationForm.getByRole('button', { name: 'Kaydet', exact: true }).click()
  const locationAnswer = await locationCreated
  expect(locationAnswer.status()).toBe(201)
  const location = await locationAnswer.json() as { readonly id: string; readonly version: number }
  await expect(page).toHaveURL(new RegExp(`/ayarlar/organizasyon/lokasyon/${location.id}$`), { timeout: 30_000 })
  await expect(page.getByRole('heading', { level: 1, name: 'Lokasyon' })).toBeVisible({ timeout: 30_000 })
  await expect(page.getByText(NEW_LOCATION.code, { exact: true })).toBeVisible({ timeout: 30_000 })
  // A warehouse site's location is internal: the external flag came from the site's type, not from the form.
  const stored = await page.request.get(`${apiBaseUrl}${LOCATIONS_API}/${location.id}`, { headers: apiHeaders(EDITOR_ACTOR) })
  expect(stored.status()).toBe(200)
  expect(await stored.json()).toMatchObject({
    id: location.id, siteId: site.id, code: NEW_LOCATION.code, isExternal: false, isActive: true,
  })

  await page.getByRole('button', { name: 'Lokasyonu düzenle', exact: true }).click()
  const locationEdit = page.getByRole('region', { name: 'Lokasyonu düzenle' })
  await fillMasterForm(locationEdit, {
    code: NEW_LOCATION.editedCode, name: NEW_LOCATION.editedName, reason: 'SENTETIK org03: lokasyon düzeltme',
  })
  const locationUpdated = page.waitForResponse((response) => isCall(response, 'PUT', `${LOCATIONS_API}/${location.id}`))
  await locationEdit.getByRole('button', { name: 'Kaydet', exact: true }).click()
  expect((await locationUpdated).status()).toBe(204)
  await expect(page.getByRole('status').filter({ hasText: 'Değişiklik kaydedildi' })).toBeVisible({ timeout: 30_000 })

  await page.goto('/')
  await page.goto(`/ayarlar/organizasyon/lokasyon/${location.id}`)
  await expect(page.getByText(NEW_LOCATION.editedCode, { exact: true })).toBeVisible({ timeout: 30_000 })
  await expect(page.getByText(NEW_LOCATION.editedName, { exact: true })).toBeVisible()
  await expect(page.getByText(NEW_LOCATION.code, { exact: true })).toHaveCount(0)

  // ── Work centre: the NEXT reader of the new location is the work-centre form's location picker. ───
  await page.goto(WORK_CENTRE_LIST_ROUTE)
  await expect(page.getByRole('table')).toBeVisible({ timeout: 30_000 })
  await page.getByRole('button', { name: 'Yeni iş merkezi', exact: true }).click()
  const wcForm = page.getByRole('region', { name: 'Yeni iş merkezi' })
  await fillMasterForm(wcForm, {
    code: NEW_WORK_CENTRE.code,
    name: NEW_WORK_CENTRE.name,
    choice: { label: 'Lokasyon', option: `${NEW_LOCATION.editedCode} — ${NEW_LOCATION.editedName}` },
    secondChoice: { label: 'İş merkezi türü', option: 'Dilme' },
    reason: 'SENTETIK org03: iş merkezi oluşturma',
  })
  const wcCreated = page.waitForResponse((response) => isCall(response, 'POST', WORK_CENTERS_API))
  await wcForm.getByRole('button', { name: 'Kaydet', exact: true }).click()
  const wcAnswer = await wcCreated
  expect(wcAnswer.status()).toBe(201)
  const workCentre = await wcAnswer.json() as { readonly id: string; readonly version: number }
  await expect(page).toHaveURL(new RegExp(`${WORK_CENTRE_LIST_ROUTE}/${workCentre.id}$`), { timeout: 30_000 })
  const card = page.locator(`[data-work-center="${workCentre.id}"]`)
  await expect(card).toBeVisible({ timeout: 30_000 })
  await expect(card.getByText(NEW_WORK_CENTRE.name, { exact: true })).toBeVisible()

  await page.getByRole('button', { name: 'İş merkezini düzenle', exact: true }).click()
  const wcEdit = page.getByRole('region', { name: 'İş merkezini düzenle' })
  await fillMasterForm(wcEdit, { name: NEW_WORK_CENTRE.editedName, reason: 'SENTETIK org03: iş merkezi adı düzeltme' })
  const wcUpdated = page.waitForResponse((response) => isCall(response, 'PUT', `${WORK_CENTERS_API}/${workCentre.id}`))
  await wcEdit.getByRole('button', { name: 'Kaydet', exact: true }).click()
  expect((await wcUpdated).status()).toBe(204)

  await page.goto('/')
  await page.goto(`${WORK_CENTRE_LIST_ROUTE}/${workCentre.id}`)
  const reopened = page.locator(`[data-work-center="${workCentre.id}"]`)
  await expect(reopened).toBeVisible({ timeout: 30_000 })
  await expect(reopened.getByText(NEW_WORK_CENTRE.editedName, { exact: true })).toBeVisible()
  await expect(reopened.getByText('Dilme', { exact: true })).toBeVisible()

  // The list every reader sees now carries the new row, at the edited location.
  const list = await page.request.get(`${apiBaseUrl}${WORK_CENTERS_API}`, { headers: apiHeaders(READER_ACTOR) })
  expect(list.status()).toBe(200)
  const rows = await list.json() as readonly WorkCentreRow[]
  expect(rows.filter((row) => row.id === workCentre.id)).toEqual([
    expect.objectContaining({
      id: workCentre.id, locationId: location.id, code: NEW_WORK_CENTRE.code, name: NEW_WORK_CENTRE.editedName,
    }),
  ])
})

test('ORG-ME-05 a newly created location persists its reasoned passive and active lifecycle through fresh reads and reopen', async ({ page }) => {
  await signInAs(page, EDITOR_ACTOR)

  // Create the owning site through the real screen. Its commit-bound code keeps this
  // record distinct from every other ORG03 master-edit acceptance row.
  await page.goto(LOCATION_LIST_ROUTE)
  await expect(page.getByRole('heading', { level: 1, name: 'Organizasyon' })).toBeVisible({ timeout: 30_000 })
  await page.getByRole('button', { name: 'Yeni tesis', exact: true }).click()
  const siteForm = page.getByRole('region', { name: 'Yeni tesis' })
  await fillMasterForm(siteForm, {
    code: LIFECYCLE_SITE.code,
    name: LIFECYCLE_SITE.name,
    choice: { label: 'Tesis türü', option: 'Depo' },
    reason: 'SENTETIK org03: yaşam döngüsü tesisi oluşturma',
  })
  const siteCreated = page.waitForResponse((response) => isCall(response, 'POST', SITES_API))
  await siteForm.getByRole('button', { name: 'Kaydet', exact: true }).click()
  const siteAnswer = await siteCreated
  expect(siteAnswer.status()).toBe(201)
  const site = await siteAnswer.json() as { readonly id: string; readonly version: number }

  // Create the location through its real form. The resulting card and every later state
  // are read from the server; no expected version is manufactured by this test.
  await page.getByRole('button', { name: 'Yeni lokasyon', exact: true }).click()
  const locationForm = page.getByRole('region', { name: 'Yeni lokasyon' })
  await fillMasterForm(locationForm, {
    code: LIFECYCLE_LOCATION.code,
    name: LIFECYCLE_LOCATION.name,
    choice: { label: 'Tesis', option: new RegExp(`^${LIFECYCLE_SITE.code} — `) },
    reason: 'SENTETIK org03: yaşam döngüsü lokasyonu oluşturma',
  })
  const locationCreated = page.waitForResponse((response) => isCall(response, 'POST', LOCATIONS_API))
  await locationForm.getByRole('button', { name: 'Kaydet', exact: true }).click()
  const locationAnswer = await locationCreated
  expect(locationAnswer.status()).toBe(201)
  const created = await locationAnswer.json() as { readonly id: string; readonly version: number }
  const cardRoute = `${LOCATION_LIST_ROUTE}/lokasyon/${created.id}`
  const cardApi = `${LOCATIONS_API}/${created.id}`
  const activeStateApi = `${cardApi}/active-state`
  await expect(page).toHaveURL(new RegExp(`${cardRoute}$`), { timeout: 30_000 })
  await expect(page.locator('[data-state="active"]')).toHaveText('Aktif')

  const initialRead = await page.request.get(`${apiBaseUrl}${cardApi}`, { headers: apiHeaders(EDITOR_ACTOR) })
  expect(initialRead.status()).toBe(200)
  const initial = await initialRead.json() as { readonly id: string; readonly version: number; readonly isActive: boolean }
  expect(initial).toMatchObject({ id: created.id, version: created.version, isActive: true })

  // The editor's write grant deliberately does not confer the separate lifecycle authority.
  // Reopen the stored card as the existing organization operator before exercising that command.
  await signInAs(page, LOCATION_STATUS_ACTOR)
  await page.goto(cardRoute)
  await expect(page.locator('[data-state="active"]')).toHaveText('Aktif')

  // Deactivate from the card, with the canonical modal and its own justification.
  await page.getByRole('button', { name: 'Pasife al', exact: true }).click()
  const deactivateDialog = page.getByRole('dialog', { name: 'Lokasyonu pasife al' })
  await deactivateDialog.getByLabel('Gerekçe', { exact: true }).fill(LIFECYCLE_LOCATION.deactivateReason)
  const deactivated = page.waitForResponse((response) => isCall(response, 'PUT', activeStateApi))
  await deactivateDialog.getByRole('button', { name: 'Pasife al', exact: true }).click()
  const deactivateAnswer = await deactivated
  expect(deactivateAnswer.status()).toBe(204)
  expect(deactivateAnswer.request().postDataJSON()).toEqual({
    isActive: false,
    expectedVersion: initial.version,
    reason: LIFECYCLE_LOCATION.deactivateReason,
  })
  await expect(page.locator('[data-outcome="applied"]')).toContainText('Değişiklik kaydedildi', { timeout: 30_000 })
  await expect(page.locator('[data-state="passive"]')).toHaveText('Pasif')

  const passiveRead = await page.request.get(`${apiBaseUrl}${cardApi}`, { headers: apiHeaders(LOCATION_STATUS_ACTOR) })
  expect(passiveRead.status()).toBe(200)
  const passive = await passiveRead.json() as { readonly id: string; readonly version: number; readonly isActive: boolean }
  expect(passive).toMatchObject({ id: created.id, isActive: false })
  expect(passive.version).toBeGreaterThan(initial.version)

  // Leave the card, return through the list and reopen the exact stored record. This is
  // a fresh route/list/card journey, so the passive badge cannot come from modal state.
  await page.goto('/')
  await page.goto(LOCATION_LIST_ROUTE)
  const storedRow = page.getByRole('row').filter({ has: page.getByRole('link', { name: LIFECYCLE_LOCATION.code, exact: true }) })
  await expect(storedRow.getByText('Pasif', { exact: true })).toBeVisible({ timeout: 30_000 })
  await storedRow.getByRole('link', { name: LIFECYCLE_LOCATION.code, exact: true }).click()
  await expect(page).toHaveURL(new RegExp(`${cardRoute}$`), { timeout: 30_000 })
  await expect(page.locator('[data-state="passive"]')).toHaveText('Pasif')

  // Reactivate with a distinct reason and the version from the passive fresh read.
  await page.getByRole('button', { name: 'Aktifleştir', exact: true }).click()
  const reactivateDialog = page.getByRole('dialog', { name: 'Lokasyonu aktifleştir' })
  await reactivateDialog.getByLabel('Gerekçe', { exact: true }).fill(LIFECYCLE_LOCATION.reactivateReason)
  const reactivated = page.waitForResponse((response) => isCall(response, 'PUT', activeStateApi))
  await reactivateDialog.getByRole('button', { name: 'Aktifleştir', exact: true }).click()
  const reactivateAnswer = await reactivated
  expect(reactivateAnswer.status()).toBe(204)
  expect(reactivateAnswer.request().postDataJSON()).toEqual({
    isActive: true,
    expectedVersion: passive.version,
    reason: LIFECYCLE_LOCATION.reactivateReason,
  })
  await expect(page.locator('[data-outcome="applied"]')).toContainText('Değişiklik kaydedildi', { timeout: 30_000 })
  await expect(page.locator('[data-state="active"]')).toHaveText('Aktif')

  const activeRead = await page.request.get(`${apiBaseUrl}${cardApi}`, { headers: apiHeaders(LOCATION_STATUS_ACTOR) })
  expect(activeRead.status()).toBe(200)
  const active = await activeRead.json() as { readonly id: string; readonly version: number; readonly isActive: boolean }
  expect(active).toMatchObject({ id: created.id, isActive: true })
  expect(active.version).toBeGreaterThan(passive.version)

  // The paged owner list contains exactly one committed record for this unique code.
  const locations = await page.request.get(`${apiBaseUrl}${LOCATIONS_API}?page=1&pageSize=100`, {
    headers: apiHeaders(LOCATION_STATUS_ACTOR),
  })
  expect(locations.status()).toBe(200)
  const matches = ((await locations.json() as { readonly items: readonly {
    readonly id: string; readonly siteId: string; readonly code: string; readonly name: string
    readonly isActive: boolean; readonly version: number
  }[] }).items).filter((location) => location.code === LIFECYCLE_LOCATION.code)
  expect(matches).toEqual([expect.objectContaining({
    id: created.id,
    siteId: site.id,
    code: LIFECYCLE_LOCATION.code,
    name: LIFECYCLE_LOCATION.name,
    isActive: true,
    version: active.version,
  })])
})

test('ORG-ME-04 a committed site create whose answer is lost stays frozen, replays the same command, and reopens once', async ({ page }) => {
  await signInAs(page, EDITOR_ACTOR)

  let firstRequest: Request | null = null
  let firstStatus: number | null = null
  let committed: { readonly id: string; readonly version: number } | null = null
  await page.route(`**${SITES_API}`, async (route) => {
    if (route.request().method() !== 'POST' || firstRequest !== null) return route.fallback()
    firstRequest = route.request()
    const response = await route.fetch({
      headers: { ...route.request().headers(), [DEVELOPMENT_USER_HEADER]: EDITOR_ACTOR },
    })
    firstStatus = response.status()
    committed = await response.json() as { readonly id: string; readonly version: number }
    await route.abort('failed')
  })

  await page.goto(LOCATION_LIST_ROUTE)
  await expect(page.getByRole('heading', { level: 1, name: 'Organizasyon' })).toBeVisible({ timeout: 30_000 })
  await page.getByRole('button', { name: 'Yeni tesis', exact: true }).click()
  const form = page.getByRole('region', { name: 'Yeni tesis' })
  await fillMasterForm(form, {
    code: UNKNOWN_SITE.code,
    name: UNKNOWN_SITE.name,
    choice: { label: 'Tesis türü', option: 'Depo' },
    reason: UNKNOWN_SITE.reason,
  })
  await form.getByRole('button', { name: 'Kaydet', exact: true }).click()

  const unknown = form.locator('[data-outcome="unknown"]')
  await expect(unknown).toBeVisible({ timeout: 30_000 })
  await expect(unknown).toContainText('Sunucuya ulaşılamadı — sonuç bilinmiyor')
  expect(firstStatus).toBe(201)
  expect(committed).toMatchObject({ id: expect.stringMatching(/^[0-9a-f-]{36}$/), version: expect.any(Number) })
  const first = firstRequest as Request | null
  expect(first).not.toBeNull()
  const firstHeaders = first!.headers()
  const firstKey = firstHeaders['idempotency-key']
  const firstBody = first!.postDataJSON()
  expect(firstHeaders['x-company-id']).toBe(GDC_COMPANY_ID)
  expect(firstKey).toMatch(/.+/)
  expect(firstBody).toEqual({
    code: UNKNOWN_SITE.code,
    name: UNKNOWN_SITE.name,
    type: 'Warehouse',
    reason: UNKNOWN_SITE.reason,
  })

  // The browser cannot know whether the 201 was committed, so the exact submitted snapshot stays immutable.
  await expect(form.getByLabel('Kod', { exact: true })).toHaveValue(UNKNOWN_SITE.code)
  await expect(form.getByLabel('Kod', { exact: true })).toHaveAttribute('readonly', '')
  await expect(form.getByLabel('Ad', { exact: true })).toHaveValue(UNKNOWN_SITE.name)
  await expect(form.getByLabel('Ad', { exact: true })).toHaveAttribute('readonly', '')
  await expect(form.getByLabel('Tesis türü', { exact: true })).toBeDisabled()
  await expect(form.getByLabel('Gerekçe', { exact: true })).toHaveValue(UNKNOWN_SITE.reason)
  await expect(form.getByLabel('Gerekçe', { exact: true })).toHaveAttribute('readonly', '')
  await expect(form.getByRole('button', { name: 'Kaydet', exact: true })).toHaveCount(0)

  const replayAnswer = page.waitForResponse((response) => isCall(response, 'POST', SITES_API))
  await form.getByRole('button', { name: 'Aynı işlem anahtarıyla tekrar gönder', exact: true }).click()
  const replay = await replayAnswer
  expect(replay.status()).toBe(201)
  expect(replay.headers()['idempotency-replayed']).toBe('true')
  expect(replay.request().headers()['x-company-id']).toBe(firstHeaders['x-company-id'])
  expect(replay.request().headers()['idempotency-key']).toBe(firstKey)
  expect(replay.request().postDataJSON()).toEqual(firstBody)
  expect(await replay.json()).toEqual(committed)
  await expect(page.getByRole('status').filter({ hasText: 'Aynı işlem zaten kaydedilmişti' })).toBeVisible({ timeout: 30_000 })

  const fresh = await page.request.get(`${apiBaseUrl}${SITES_API}`, { headers: apiHeaders(EDITOR_ACTOR) })
  expect(fresh.status()).toBe(200)
  const matching = (await fresh.json() as readonly {
    readonly id: string; readonly code: string; readonly name: string; readonly version: number
  }[]).filter((site) => site.code === UNKNOWN_SITE.code)
  expect(matching).toEqual([
    expect.objectContaining({ id: committed!.id, name: UNKNOWN_SITE.name, version: committed!.version }),
  ])

  // Close and reopen through a fresh list read; the edit form must be populated from the single stored record.
  await page.goto('/')
  await page.goto(LOCATION_LIST_ROUTE)
  await expect(page.getByText(UNKNOWN_SITE.name, { exact: true })).toBeVisible({ timeout: 30_000 })
  await page.getByRole('button', { name: `Tesisi düzenle: ${UNKNOWN_SITE.code}`, exact: true }).click()
  const reopened = page.getByRole('region', { name: `Tesisi düzenle: ${UNKNOWN_SITE.code}` })
  await expect(reopened.getByLabel('Kod', { exact: true })).toHaveValue(UNKNOWN_SITE.code)
  await expect(reopened.getByLabel('Ad', { exact: true })).toHaveValue(UNKNOWN_SITE.name)
})

test('ORG-ME-02 a replayed create applies once, another body under the same key is refused, and a stale edit is a 409 that changes nothing', async ({ page }) => {
  const key = `org03-me-replay-${testedCommit.slice(0, 12)}`
  const body = { code: 'ORG03-ME-RPL', name: 'SENTETIK org03 tekrar', type: 'Warehouse', reason: 'SENTETIK org03: tekrar denemesi' }

  const first = await page.request.post(`${apiBaseUrl}${SITES_API}`, { headers: commandHeaders(EDITOR_ACTOR, key), data: body })
  expect(first.status()).toBe(201)
  const created = await first.json() as { readonly id: string; readonly version: number }

  const replay = await page.request.post(`${apiBaseUrl}${SITES_API}`, { headers: commandHeaders(EDITOR_ACTOR, key), data: body })
  expect(replay.status()).toBe(201)
  expect(replay.headers()['idempotency-replayed']).toBe('true')
  expect(await replay.json()).toEqual(created)

  const otherBody = await page.request.post(`${apiBaseUrl}${SITES_API}`, {
    headers: commandHeaders(EDITOR_ACTOR, key), data: { ...body, name: 'SENTETIK org03 başka gövde' },
  })
  expect(otherBody.status()).toBe(409)

  const sites = await page.request.get(`${apiBaseUrl}${SITES_API}`, { headers: apiHeaders(EDITOR_ACTOR) })
  expect(sites.status()).toBe(200)
  const matching = (await sites.json() as readonly { readonly id: string; readonly code: string; readonly name: string }[])
    .filter((row) => row.code === body.code)
  expect(matching).toEqual([expect.objectContaining({ id: created.id, name: body.name })])

  // The same code again under a NEW key is the owner's 422, not a second site.
  const duplicate = await page.request.post(`${apiBaseUrl}${SITES_API}`, {
    headers: commandHeaders(EDITOR_ACTOR, `${key}-dup`), data: body,
  })
  expect(duplicate.status()).toBe(422)
  expect((await duplicate.json() as { readonly code?: string }).code).toBe('organization_code_in_use')

  const edit = { code: body.code, name: 'SENTETIK org03 tekrar (1)', expectedVersion: created.version, reason: 'SENTETIK org03: ilk düzeltme' }
  const firstEdit = await page.request.put(`${apiBaseUrl}${SITES_API}/${created.id}`, {
    headers: commandHeaders(EDITOR_ACTOR, `${key}-edit-1`), data: edit,
  })
  expect(firstEdit.status()).toBe(204)
  const stale = await page.request.put(`${apiBaseUrl}${SITES_API}/${created.id}`, {
    headers: commandHeaders(EDITOR_ACTOR, `${key}-edit-2`), data: { ...edit, name: 'SENTETIK org03 tekrar (bayat)' },
  })
  expect(stale.status()).toBe(409)
  const after = await page.request.get(`${apiBaseUrl}${SITES_API}`, { headers: apiHeaders(EDITOR_ACTOR) })
  const current = (await after.json() as readonly { readonly id: string; readonly name: string }[]).find((row) => row.id === created.id)
  expect(current?.name).toBe(edit.name)
})

test('ORG-ME-03 a reader without the writes is offered no create or edit control, and the API refuses its command', async ({ page }) => {
  await signInAs(page, READER_ACTOR)
  await page.goto(WORK_CENTRE_LIST_ROUTE)
  await expect(page.getByRole('table')).toBeVisible({ timeout: 30_000 })
  await expect(page.getByRole('button', { name: 'Yeni iş merkezi', exact: true })).toHaveCount(0)

  await page.goto(LOCATION_LIST_ROUTE)
  await expect(page.getByRole('heading', { level: 1, name: 'Organizasyon' })).toBeVisible({ timeout: 30_000 })
  await expect(page.getByRole('button', { name: 'Yeni lokasyon', exact: true })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Yeni tesis', exact: true })).toHaveCount(0)
  await expect(page.getByRole('button', { name: /^Tesisi düzenle/ })).toHaveCount(0)

  const refused = await page.request.post(`${apiBaseUrl}${SITES_API}`, {
    headers: commandHeaders(READER_ACTOR, `org03-me-denied-${testedCommit.slice(0, 12)}`),
    data: { code: 'ORG03-ME-DENIED', name: 'SENTETIK org03 yetkisiz', type: 'Warehouse', reason: 'SENTETIK org03: yetkisiz deneme' },
  })
  expect(refused.status()).toBe(403)
  const sites = await page.request.get(`${apiBaseUrl}${SITES_API}`, { headers: apiHeaders(EDITOR_ACTOR) })
  expect((await sites.json() as readonly { readonly code: string }[]).some((row) => row.code === 'ORG03-ME-DENIED')).toBe(false)
})
