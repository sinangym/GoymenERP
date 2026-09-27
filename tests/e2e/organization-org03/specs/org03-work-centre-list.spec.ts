import { expect, test, type Locator, type Page, type Response as PageResponse } from '@playwright/test'
import { READER_ACTOR, WORK_CENTERS_ONLY_ACTOR, readEnvironment } from '../src/env'

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
 * READ-ONLY. Nothing here writes: no command, no form, no state change. Everything it reads is in the
 * built-in organization seed (five work centres across four locations of the GDC company), so the suite
 * needs no fixture and no seed envelope.
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

const { testedCommit } = readEnvironment()

const WORK_CENTRE_LIST_ROUTE = '/ayarlar/organizasyon/is-merkezleri'
const LOCATION_LIST_ROUTE = '/ayarlar/organizasyon'
const NAV_LABEL = 'İş Merkezleri'
const DEVELOPMENT_USER_HEADER = 'X-Development-User'

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
