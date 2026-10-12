/**
 * B-C01-001 … B-C01-010 against the real API, real SQL Server and a real browser.
 *
 * The expected values are not invented here. Every one of them was measured in an
 * earlier manual run against this same stack and the canonical `C01-E2E-GOLDEN-V1`
 * fixture, and recorded on Issue #146 — 54 parties, 55 accounts, GDC
 * active-visible 52, GSP active-visible 2. If the fixture ever changes, these
 * numbers must change with it and the fixture evidence check will say so first.
 *
 * Case titles MUST start with the bare case id: the JUnit report is parsed for
 * exactly ten unique `B-C01-\d{3}` prefixes.
 */

import { expect, test, type Page } from '@playwright/test'
import { readEnv } from '../src/contract.js'
import { NetworkRecorder } from '../src/networkRecorder.js'

const env = readEnv()

const GDC = '10000000-0000-0000-0000-000000000001'
const GSP = '10000000-0000-0000-0000-000000000002'

/** Discriminating records, verified against the real API in both directions. */
const FIRST_ROW = { code: 'CAR-000001', name: 'Örnek Metal Sanayi ve Ticaret A.Ş.' }
const GSP_ONLY = 'CAR-000054'
const PAGE_TWO = ['CAR-000051', 'CAR-000052']
const GDC_ACTIVE_VISIBLE = 52
const GSP_ACTIVE_VISIBLE = 2
const SPLIT_STATE_ROW = 'CAR-000002'
const GLOBAL_INACTIVE_ROW = 'CAR-000053'
/** `q=Örnek` matches a legal name in two GDC parties and nothing else (`C01-E2E-GOLDEN-V1`). */
const SEARCH_TERM = 'Örnek'
const SEARCH_MATCHES = ['CAR-000001', 'CAR-000003']
/** Active GDC natural persons: the odd codes CAR-000003 … CAR-000051 plus CAR-000006. */
const GDC_ACTIVE_NATURAL_PERSONS = 25
const ADDRESS_MATCHES = ['CAR-000001', 'CAR-000051']

/** CARI-DS-01: the table is the layout from 768 px up; below that the same rows are one labelled card each. */
const PHONE_MAX_WIDTH = 767

const UUID_RE = /[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}/

async function openList(page: Page): Promise<void> {
  await page.goto('/cari')
  await expect(page.getByRole('heading', { level: 1, name: 'Cari Kartlar' })).toBeVisible()
  await expect(page.getByRole('table')).toBeVisible()
}

async function openAllFilters(page: Page): Promise<void> {
  const toggle = page.getByRole('button', { name: 'Tüm filtreler', exact: true })
  await toggle.focus()
  await page.keyboard.press('Enter')
  await expect(page.getByLabel('İl', { exact: true })).toBeVisible()
  await expect(page.getByLabel('İlçe', { exact: true })).toBeVisible()
  await expect(page.getByLabel('Adres türü', { exact: true })).toBeVisible()
}

function rows(page: Page) {
  return page.locator('table tbody tr')
}

async function codesOf(page: Page): Promise<string[]> {
  return rows(page).locator('td:first-child').allInnerTexts()
}

test.describe.configure({ mode: 'serial' })

test('B-C01-001 the authorized first page is the fixture, and only the fixture', async ({
  page,
}, testInfo) => {
  const net = await NetworkRecorder.attach(page)
  await testInfo.attach('c01-browser-version', {
    body: Buffer.from(page.context().browser()?.version() ?? '', 'utf8'),
    contentType: 'text/plain',
  })

  // The nav entry is checked from the shell root, before the deep link: reaching
  // /cari directly would prove the route works but say nothing about whether the
  // module is discoverable.
  await page.goto('/')
  await expect(page.getByRole('link', { name: 'Cari Kartlar' })).toBeVisible({ timeout: 20_000 })
  await openList(page)

  await expect(rows(page)).toHaveCount(50)
  const first = rows(page).first()
  await expect(first.locator('td').nth(0)).toHaveText(FIRST_ROW.code)
  await expect(first.locator('td').nth(1)).toContainText(FIRST_ROW.name)
  // Combined role flags render as one cell, not a row per role.
  await expect(first.locator('td').nth(3)).toHaveText(/Müşteri/)
  await expect(first.locator('td').nth(3)).toHaveText(/Tedarikçi/)

  // The GSP-only party must be absent under GDC. This is the isolation claim the
  // whole screen rests on, so it is asserted on the accessible text, not on a
  // filtered query the page itself performed.
  const body = (await page.locator('body').innerText()).trim()
  expect(body).not.toContain(GSP_ONLY)

  // No raw identifiers and no integration code may reach the rendered page.
  expect(UUID_RE.test(body)).toBe(false)
  expect(body.toLowerCase()).not.toContain('externalintegrationcode')
  expect(body).not.toContain('filtered-authorized-visible-parties')

  // The fixture's authorized profile holds the LIST grant only (verify_c01_e2e_api.py pins it to exactly
  // `cari.parties.list`). Opening a record and creating one are separate grants, so neither may be offered: the
  // code is plain text, never a link the route would refuse, and there is no "Yeni Cari" action.
  await expect(page.getByRole('main').getByRole('link', { name: 'Yeni Cari' })).toHaveCount(0)
  await expect(first.locator('td').nth(0).getByRole('link')).toHaveCount(0)
  await expect(page.getByRole('main').locator('[data-row-link]')).toHaveCount(0)

  await net.detach(testInfo)
})

test('B-C01-002 the unauthorized profile is not told the screen exists', async ({
  browser,
}, testInfo) => {
  // A separate context on the unauthorized web profile: the API half (empty
  // capabilities, 403) and the UI half (no nav entry, deep link closed) must be
  // observed in ONE run. The earlier attempt reported them from two different
  // runs, which is why this case was classified NOT_COVERED rather than passed.
  const context = await browser.newContext({ baseURL: env.unauthorizedBaseUrl })
  const page = await context.newPage()
  const net = await NetworkRecorder.attach(page)

  // Capability-zero means the read is never ATTEMPTED, not that it is attempted
  // and refused. Counting the requests is the only way to tell those apart.
  let counterpartyRequests = 0
  page.on('request', (request) => {
    if (request.url().includes('/api/v1/counterparties')) counterpartyRequests += 1
  })

  await page.goto('/')
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible({ timeout: 20_000 })
  await expect(page.getByRole('link', { name: 'Cari Kartlar' })).toHaveCount(0)

  await page.goto('/cari')
  // Non-disclosing: the gated route must close, and must not say the record or
  // the module exists.
  await expect(page.locator('[data-state="not-found"]')).toBeVisible({ timeout: 20_000 })
  await expect(page.getByRole('table')).toHaveCount(0)
  const body = await page.locator('body').innerText()
  expect(body).not.toContain(FIRST_ROW.code)
  expect(
    counterpartyRequests,
    'the gated screen issued a counterparty read it was never entitled to attempt',
  ).toBe(0)

  await net.detach(testInfo)
  await context.close()
})

test('B-C01-003 a company switch cancels the old read, so it cannot leak', async ({ page }, testInfo) => {
  const net = await NetworkRecorder.attach(page)
  await openList(page)
  await expect(rows(page)).toHaveCount(50)

  // PREVENTION, NOT REJECTION — and the title says so, because I had this wrong.
  //
  // Two corrections got this case to the truth. The first version slept before
  // route.continue(), which delays the REQUEST, so the response landed in a
  // context that had never moved (#173 P1-2). The second version fetched the real
  // response and withheld it, and reported success from whether `route.fulfill`
  // resolved. Measured on the running app, that signal is worthless:
  //
  //     fulfilOk=true  pageReceived=false  aborted=net::ERR_ABORTED
  //
  // The company switch CANCELS the in-flight read, so the stale response is never
  // delivered at all. Fulfil resolving says only that the interceptor wrote bytes
  // nobody was listening for. Codex's post-merge audit warned that SAB-009 could
  // pass vacuously for exactly this reason; the warning generalises here.
  //
  // What this case now pins is the real, observable property: an old company's
  // read does not survive the switch. Whether a DELIVERED stale response would be
  // rejected is a different claim, it is not provable in this browser path, and it
  // is proved in the reducer tests instead.
  const gdcRead = await net.holdNextCounterpartyResponse()
  await page.getByRole('button', { name: 'Daha fazla yükle' }).click()
  // Wait until the response genuinely EXISTS and is parked. Without this latch the
  // release could fire before the handler even reached route.fetch(), delivering
  // into a context that had not moved yet — the case would pass having proved
  // nothing (#173 post-merge audit, point 3).
  await gdcRead.ready

  const picker = page.getByRole('combobox', { name: 'Şirket', exact: true })
  await picker.selectOption(GSP)

  // GSP is committed and painted while GDC's response is still held.
  await expect(rows(page)).toHaveCount(GSP_ACTIVE_VISIBLE, { timeout: 30_000 })
  const duringSwitch = await codesOf(page)
  expect(duringSwitch).toContain(GSP_ONLY)
  expect(duringSwitch, 'a GDC row survived the switch').not.toContain(FIRST_ROW.code)
  expect(await page.locator('body').innerText()).not.toContain(FIRST_ROW.name)

  // Release the old company's response. It cannot arrive: the read is already gone.
  gdcRead.release()
  await page.waitForTimeout(1500)
  expect(
    gdcRead.abortError(),
    'the old company read survived the switch — this is now a delivery case, not a prevention one',
  ).toBe('net::ERR_ABORTED')
  expect(
    gdcRead.wasDelivered(),
    'the stale response WAS delivered, so this case must assert rejection, not prevention',
  ).toBe(false)

  // And nothing from the departed company is on screen either way.
  const afterStale = await codesOf(page)
  expect(afterStale, 'a GDC row appeared after the switch').not.toContain(FIRST_ROW.code)
  expect(afterStale).toEqual(duringSwitch)
  expect(await page.locator('body').innerText()).not.toContain(FIRST_ROW.name)
  await expect(picker).toHaveValue(GSP)

  await picker.selectOption(GDC)
  await expect(rows(page)).toHaveCount(50, { timeout: 30_000 })
  expect(await codesOf(page)).not.toContain(GSP_ONLY)

  await net.detach(testInfo)
})

test('B-C01-004 sorting, search and filters are done by the server, and focus survives sorting', async ({
  page,
}, testInfo) => {
  const net = await NetworkRecorder.attach(page)
  const initialRead = page.waitForRequest((request) =>
    new URL(request.url()).pathname === '/api/v1/counterparties')
  await openList(page)
  const initialHeaders = await (await initialRead).allHeaders()
  const actor = initialHeaders['x-development-user']
  const company = initialHeaders['x-company-id']
  expect(actor, 'the real address-filter read must reuse the active actor').toBeTruthy()
  expect(company, 'the real address-filter read must reuse the selected company').toBe(GDC)

  const codeHeader = page.locator('th').filter({ hasText: 'Kod' }).first()
  await expect(codeHeader).toHaveAttribute('aria-sort', 'ascending')

  const sortButton = codeHeader.getByRole('button')
  await sortButton.focus()
  await page.keyboard.press('Enter')

  await expect(codeHeader).toHaveAttribute('aria-sort', 'descending', { timeout: 20_000 })
  // The claim is that the SERVER re-sorted, not that the header label changed:
  // descending by code puts the highest visible GDC code first.
  await expect(rows(page).first().locator('td').nth(0)).toHaveText('CAR-000052')
  // Focus must stay on the control the user actuated, across a full re-render.
  await expect(sortButton).toBeFocused()

  // Back to ascending, so every expectation below reads in fixture order.
  await page.keyboard.press('Enter')
  await expect(codeHeader).toHaveAttribute('aria-sort', 'ascending', { timeout: 20_000 })
  await expect(rows(page).first().locator('td').nth(0)).toHaveText(FIRST_ROW.code)

  // SEARCH is the server's. Typing alone issues the read (no submit), and the rows are exactly what the real
  // query returns for the term — a client-side filter over page one could not produce this set and this count.
  const search = page.getByRole('main').getByRole('searchbox', { name: 'Ara', exact: true })
  const searched = page.waitForRequest((request) => {
    const url = new URL(request.url())
    return url.pathname === '/api/v1/counterparties' && url.searchParams.get('q') === SEARCH_TERM
  })
  await search.fill(SEARCH_TERM)
  const searchRequest = new URL((await searched).url())
  expect(searchRequest.searchParams.get('cursor'), 'a new search must start from the first page').toBeNull()
  await expect(rows(page)).toHaveCount(SEARCH_MATCHES.length, { timeout: 20_000 })
  expect(await codesOf(page)).toEqual(SEARCH_MATCHES)
  await search.fill('')
  await expect(rows(page)).toHaveCount(50, { timeout: 20_000 })

  // FILTERS are the server's too, each one a labelled control. Every expected set is the fixture's own truth.
  const main = page.getByRole('main')
  await main.getByLabel('Tür', { exact: true }).selectOption('NaturalPerson')
  await expect(rows(page)).toHaveCount(GDC_ACTIVE_NATURAL_PERSONS, { timeout: 20_000 })
  for (const kind of await rows(page).locator('td:nth-child(3)').allInnerTexts()) {
    expect(kind).toBe('Gerçek kişi')
  }
  await main.getByLabel('Tür', { exact: true }).selectOption('')
  await expect(rows(page)).toHaveCount(50, { timeout: 20_000 })

  // The globally passive party is the ONE row the default filter hides; asking for passive parties shows it alone.
  await main.getByLabel('Taraf durumu', { exact: true }).selectOption('inactive')
  await expect(rows(page)).toHaveCount(1, { timeout: 20_000 })
  expect(await codesOf(page)).toEqual([GLOBAL_INACTIVE_ROW])
  await expect(rows(page).first().locator('td[data-party-state]')).toHaveAttribute('data-party-state', 'inactive')
  await main.getByLabel('Taraf durumu', { exact: true }).selectOption('active')

  // The company-account state is a separate fact with its own filter: one active party is passive in GDC.
  await main.getByLabel('Şirket hesabı durumu', { exact: true }).selectOption('inactive')
  await expect(rows(page)).toHaveCount(1, { timeout: 20_000 })
  expect(await codesOf(page)).toEqual([SPLIT_STATE_ROW])

  // Clearing restores the untouched list and hands the keyboard to the search box.
  await main.getByRole('button', { name: 'Filtreleri temizle' }).click()
  await expect(rows(page)).toHaveCount(50, { timeout: 20_000 })
  await expect(search).toBeFocused()

  // Address filters are additive API contract in this backend slice. Use the same real
  // actor/company headers the running page emitted; no mocked response or extra grant.
  // CAR-000051 is outside the unfiltered first 50 records, so finding it on the second
  // filtered page proves the server applied the address predicate before pagination.
  const addressUrl = new URL('/api/v1/counterparties', env.apiBaseUrl)
  addressUrl.searchParams.append('addressCity', 'İSTANBUL')
  addressUrl.searchParams.append('addressDistrict', 'Çankaya')
  addressUrl.searchParams.append('addressType', 'delivery')
  addressUrl.searchParams.append('sort', 'code')
  addressUrl.searchParams.append('direction', 'asc')
  addressUrl.searchParams.append('pageSize', '1')
  addressUrl.searchParams.append('includeCount', 'true')
  const apiHeaders = { 'X-Development-User': actor!, 'X-Company-Id': company! }
  const firstAddressPage = await page.context().request.get(addressUrl.toString(), { headers: apiHeaders })
  expect(firstAddressPage.status()).toBe(200)
  const firstAddressBody = await firstAddressPage.json()
  expect(firstAddressBody.totalCount).toBe(2)
  expect(firstAddressBody.items.map((item: { code: string }) => item.code)).toEqual(['CAR-000001'])
  expect(firstAddressBody.hasMore).toBe(true)
  expect(firstAddressBody.nextCursor).toBeTruthy()

  addressUrl.searchParams.set('cursor', firstAddressBody.nextCursor)
  const secondAddressPage = await page.context().request.get(addressUrl.toString(), { headers: apiHeaders })
  expect(secondAddressPage.status()).toBe(200)
  const secondAddressBody = await secondAddressPage.json()
  expect(secondAddressBody.items.map((item: { code: string }) => item.code)).toEqual(['CAR-000051'])
  expect(secondAddressBody.hasMore).toBe(false)

  const sameRowUrl = new URL('/api/v1/counterparties', env.apiBaseUrl)
  sameRowUrl.searchParams.append('addressCity', 'İstanbul')
  sameRowUrl.searchParams.append('addressDistrict', 'Çankaya')
  sameRowUrl.searchParams.append('addressType', 'billing')
  const sameRowResponse = await page.context().request.get(sameRowUrl.toString(), { headers: apiHeaders })
  expect(sameRowResponse.status()).toBe(200)
  expect((await sameRowResponse.json()).items).toEqual([])

  const roleUrl = new URL('/api/v1/counterparties', env.apiBaseUrl)
  roleUrl.searchParams.append('addressCity', 'İstanbul')
  roleUrl.searchParams.append('addressDistrict', 'Çankaya')
  roleUrl.searchParams.append('addressType', 'delivery')
  roleUrl.searchParams.append('role', 'Customer')
  roleUrl.searchParams.append('role', 'Supplier')
  roleUrl.searchParams.append('roleMatch', 'all')
  const roleResponse = await page.context().request.get(roleUrl.toString(), { headers: apiHeaders })
  expect(roleResponse.status()).toBe(200)
  expect((await roleResponse.json()).items.map((item: { code: string }) => item.code)).toEqual(['CAR-000001'])

  // The accepted UI must drive that same server query. The filter values are
  // typed rather than populated from page one: presenting the first page as the
  // complete city/district vocabulary would make authorized records on later
  // pages unreachable.
  await openAllFilters(page)
  const city = main.getByLabel('İl', { exact: true })
  const district = main.getByLabel('İlçe', { exact: true })
  const addressType = main.getByLabel('Adres türü', { exact: true })

  await city.fill(' İstanbul ')
  await district.fill(' Çankaya ')
  const deliveryRead = page.waitForRequest((request) => {
    const url = new URL(request.url())
    return (
      url.pathname === '/api/v1/counterparties' &&
      url.searchParams.get('addressCity') === 'İstanbul' &&
      url.searchParams.get('addressDistrict') === 'Çankaya' &&
      url.searchParams.get('addressType') === 'delivery'
    )
  })
  await addressType.selectOption('delivery')
  const deliveryUrl = new URL((await deliveryRead).url())
  expect(deliveryUrl.searchParams.get('cursor'), 'an address-filter change must reset the cursor').toBeNull()
  await expect(rows(page)).toHaveCount(ADDRESS_MATCHES.length, { timeout: 20_000 })
  expect(await codesOf(page)).toEqual(ADDRESS_MATCHES)
  expect(await codesOf(page), 'the server-side address filter lost the record beyond page one').toContain('CAR-000051')

  // CAR-000004 owns an İstanbul/Kadıköy billing row and a separate
  // Ankara/Çankaya delivery row. A query that combines İstanbul + Çankaya +
  // billing must therefore be empty; matching the three facts across different
  // address rows would be a false positive.
  const splitRowRead = page.waitForRequest((request) => {
    const url = new URL(request.url())
    return (
      url.pathname === '/api/v1/counterparties' &&
      url.searchParams.get('addressCity') === 'İstanbul' &&
      url.searchParams.get('addressDistrict') === 'Çankaya' &&
      url.searchParams.get('addressType') === 'billing'
    )
  })
  await addressType.selectOption('billing')
  await splitRowRead
  await expect(rows(page)).toHaveCount(0, { timeout: 20_000 })
  await expect(page.locator('[data-state="empty-filtered"]')).toBeVisible()
  await expect(page.getByText('Filtreye uyan kayıt bulunamadı.')).toBeVisible()

  // Address, title, historical role and state filters remain one server query.
  // CAR-000001 is the one fixture record satisfying all of them.
  await addressType.selectOption('delivery')
  await search.fill(SEARCH_TERM)
  const legacy = main.locator('details').filter({ hasText: 'Geçmiş sınıflandırma' })
  await legacy.locator('summary').click()
  await legacy.getByLabel('Müşteri', { exact: true }).check()
  await legacy.getByLabel('Tedarikçi', { exact: true }).check()
  const combinedRead = page.waitForRequest((request) => {
    const url = new URL(request.url())
    return (
      url.pathname === '/api/v1/counterparties' &&
      url.searchParams.get('q') === SEARCH_TERM &&
      url.searchParams.getAll('role').join(',') === 'Customer,Supplier' &&
      url.searchParams.get('roleMatch') === 'all' &&
      url.searchParams.get('partyState') === 'active' &&
      url.searchParams.get('addressCity') === 'İstanbul' &&
      url.searchParams.get('addressDistrict') === 'Çankaya' &&
      url.searchParams.get('addressType') === 'delivery'
    )
  })
  await main.getByLabel('İşaret eşleşmesi', { exact: true }).selectOption('all')
  expect(new URL((await combinedRead).url()).searchParams.get('cursor')).toBeNull()
  await expect(rows(page)).toHaveCount(1, { timeout: 20_000 })
  expect(await codesOf(page)).toEqual([FIRST_ROW.code])

  // Leaving through the SPA and using browser Back restores the saved view in
  // the same actor/company namespace. No query from a different namespace is
  // replayed, and the visible values remain the ones the user chose.
  await page.getByRole('link', { name: 'Ana sayfa', exact: true }).click()
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
  const restoredRead = page.waitForRequest((request) => {
    const url = new URL(request.url())
    return (
      url.pathname === '/api/v1/counterparties' &&
      url.searchParams.get('q') === SEARCH_TERM &&
      url.searchParams.get('addressCity') === 'İstanbul' &&
      url.searchParams.get('addressDistrict') === 'Çankaya' &&
      url.searchParams.get('addressType') === 'delivery'
    )
  })
  await page.goBack()
  await restoredRead
  await expect(page.getByRole('heading', { level: 1, name: 'Cari Kartlar' })).toBeVisible()
  await expect(page.getByRole('searchbox', { name: 'Ara', exact: true })).toHaveValue(SEARCH_TERM)
  await expect(page.getByText('İl: İstanbul', { exact: true })).toBeVisible()
  await expect(page.getByText('İlçe: Çankaya', { exact: true })).toBeVisible()
  await expect(page.getByText('Adres türü: Teslimat', { exact: true })).toBeVisible()
  await expect(rows(page)).toHaveCount(1)

  await net.detach(testInfo)
})

test('B-C01-005 the page reflows without a horizontal scrollbar', async ({ page }, testInfo) => {
  const net = await NetworkRecorder.attach(page)
  await openList(page)
  await openAllFilters(page)

  // The canonical evidence matrix remains 1600/768/390/640/320. The 1440 and
  // 1366 checks are additional real-use widths walked inside the same case; they
  // do not create extra B-C01 identities or weaken the established matrix.
  for (const size of [
    '1600x900',
    '1440x900',
    '1366x768',
    '768x1024',
    '390x844',
    '640x900',
    '320x900',
  ]) {
    const [width, height] = size.split('x').map(Number)
    await page.setViewportSize({ width, height })

    if (width > PHONE_MAX_WIDTH) {
      await expect(rows(page).first()).toBeVisible()
      // The table may scroll, but only inside its own labelled region, and it must
      // keep table semantics while doing so.
      const display = await page.locator('table').evaluate((el) => getComputedStyle(el).display)
      expect(display, `table lost table semantics at ${size}`).toBe('table')
      await expect(page.getByRole('region', { name: 'Cari listesi tablosu' })).toBeVisible()
    } else {
      // CARI-DS-01: at phone width the same rows are one labelled card each, not a sideways-scrolling table.
      const cards = page.getByRole('main').getByRole('list', { name: 'Cari listesi', exact: true })
      await expect(cards, `no Cari card list at ${size}`).toBeVisible()
      await expect(page.getByRole('table')).toHaveCount(0)
      await expect(page.getByRole('region', { name: 'Cari listesi tablosu' })).toHaveCount(0)
      // The rows did not change with the layout: still the verified first page, in the same order.
      await expect(cards.getByRole('listitem')).toHaveCount(50)
      const firstCard = cards.getByRole('listitem').first()
      await expect(firstCard).toBeVisible()
      await expect(firstCard).toContainText(FIRST_ROW.code)
      await expect(firstCard).toContainText(FIRST_ROW.name)
      // Every fact on the card is labelled, so it reads without the table's column headers.
      expect(await firstCard.locator('dt').allInnerTexts()).toEqual([
        'Taraf durumu',
        'Şirket hesabı durumu',
        'Geçmiş ticari sınıflandırma',
      ])
      // The name is shown whole: not cut, not pushed past the card's edge.
      const name = firstCard.getByText(FIRST_ROW.name, { exact: true })
      const clipped = await name.evaluate((el) => {
        const box = el.getBoundingClientRect()
        const style = getComputedStyle(el)
        return (
          el.scrollWidth > el.clientWidth + 1 ||
          style.textOverflow === 'ellipsis' ||
          box.left < 0 ||
          box.right > document.documentElement.clientWidth + 0.5
        )
      })
      expect(clipped, `the legal name is cut at ${size}`).toBe(false)
    }

    // WCAG 1.4.10: the PAGE must not scroll horizontally. A previous version of
    // this screen passed jsdom and failed exactly here.
    const overflows = await page.evaluate(
      () => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1,
    )
    expect(overflows, `page scrolls horizontally at ${size}`).toBe(false)
    await expect(page.getByRole('button', { name: 'Tüm filtreler', exact: true })).toBeVisible()
    await expect(page.getByLabel('İl', { exact: true })).toBeVisible()
    await expect(page.getByLabel('İlçe', { exact: true })).toBeVisible()
    await expect(page.getByLabel('Adres türü', { exact: true })).toBeVisible()
  }

  await page.setViewportSize({ width: 1600, height: 900 })
  await net.detach(testInfo)
})

test('B-C01-006 the accessible structure is the one screen readers need', async ({
  page,
}, testInfo) => {
  const net = await NetworkRecorder.attach(page)
  await openList(page)

  await expect(page.getByRole('heading', { level: 1 })).toHaveCount(1)
  await expect(page.getByRole('navigation')).toHaveCount(1)
  await expect(page.locator('th[scope="col"]')).toHaveCount(6)
  await expect(page.getByRole('status')).toHaveAttribute('aria-live', 'polite')
  await expect(page.getByRole('region', { name: 'Cari listesi tablosu' })).toBeVisible()
  // The list's own filter search lives in <main>; the shell toolbar carries the global search (AT-002) as a second,
  // separately named search landmark.
  await expect(page.getByRole('main').getByRole('search')).toBeVisible()
  // A settled table must say so; a stuck aria-busy makes a screen reader wait
  // forever for a load that already finished.
  await expect(page.getByRole('table')).toHaveAttribute('aria-busy', 'false')
  // Internal capability names are not user-facing vocabulary.
  const shown = await page.locator('body').innerText()
  expect(shown).not.toContain('filtered-authorized-visible-parties')
  expect(shown.toLowerCase()).not.toContain('capability')

  await net.detach(testInfo)
})

test('B-C01-007 continuation appends the rest exactly once', async ({ page }, testInfo) => {
  const net = await NetworkRecorder.attach(page)
  await openList(page)
  await expect(rows(page)).toHaveCount(50)

  await page.getByRole('button', { name: 'Daha fazla yükle' }).click()

  await expect(rows(page)).toHaveCount(GDC_ACTIVE_VISIBLE, { timeout: 30_000 })
  const codes = await codesOf(page)
  for (const code of PAGE_TWO) expect(codes).toContain(code)
  // No duplicates: an append that re-added page one would still reach 52 by
  // accident on a different fixture, so uniqueness is asserted directly.
  expect(new Set(codes).size).toBe(codes.length)
  // hasMore is false now, so the control must be gone rather than merely disabled.
  await expect(page.getByRole('button', { name: 'Daha fazla yükle' })).toHaveCount(0)

  await net.detach(testInfo)
})

test('B-C01-008 a failed continuation keeps the verified rows and offers a retry', async ({
  page,
}, testInfo) => {
  const net = await NetworkRecorder.attach(page)
  await openList(page)
  const main = page.getByRole('main')
  await main.getByLabel('Sayfa boyutu', { exact: true }).selectOption('10')
  await main.getByLabel('Tür', { exact: true }).selectOption('NaturalPerson')
  await expect(rows(page)).toHaveCount(10)

  const continuationRequests: URL[] = []
  page.on('request', (request) => {
    const url = new URL(request.url())
    if (url.pathname === '/api/v1/counterparties' && url.searchParams.has('cursor')) {
      continuationRequests.push(url)
    }
  })

  const alert = page.getByRole('alert')
  const retry = alert.getByRole('button')

  // Once a continuation has failed, the screen replaces "Daha fazla yükle" with a
  // scoped retry — the load-more control is GONE, not merely disabled. That is the
  // shape DEC-CAR-004 asks for (explicit user retry, no automatic resend), so every
  // fault after the first is driven through the retry button.
  const used500 = await net.failNextCounterpartyRead('injected-500')
  await page.getByRole('button', { name: 'Daha fazla yükle' }).click()
  await expect.poll(used500, { timeout: 20_000 }).toBe(true)
  await expect(alert, 'no continuation error after injected-500').toBeVisible({ timeout: 30_000 })
  await expect(rows(page), 'rows were dropped on injected-500').toHaveCount(10)
  await expect(page.getByRole('button', { name: 'Daha fazla yükle' })).toHaveCount(0)

  for (const fault of ['injected-503', 'injected-abort'] as const) {
    const used = await net.failNextCounterpartyRead(fault)
    await retry.click()
    // The alert from the PREVIOUS fault is still on screen, so a bare toBeVisible
    // would be satisfied by it and the new fault would never be observed (#173
    // post-merge audit, point 4). Waiting for the injector to have been CONSUMED
    // is the signal that does not depend on catching a transient UI state.
    await expect.poll(used, { timeout: 20_000 }).toBe(true)
    await expect(retry, `the retry never settled after ${fault}`).toBeEnabled({ timeout: 30_000 })
    await expect(alert, `no continuation error after ${fault}`).toBeVisible()
    await expect(rows(page), `rows were dropped on ${fault}`).toHaveCount(10)
  }

  // A dropped connection is a transport fault, not a status code.
  const usedDrop = await net.dropNextCounterpartyRead()
  await retry.click()
  await expect.poll(usedDrop, { timeout: 20_000 }).toBe(true)
  await expect(alert).toBeVisible({ timeout: 30_000 })
  await expect(rows(page)).toHaveCount(10)

  // Four failures, and the verified rows never moved. The successful retry then
  // appends the rest exactly once — proof that each failed attempt re-issued the
  // same cursor rather than advancing it.
  // The recovery is OBSERVED, not asserted in advance: the recorder only writes
  // retry-200 when a real 200 actually arrives for this read.
  net.expectRetrySuccess(1)
  await retry.click()
  await expect(rows(page)).toHaveCount(20, { timeout: 30_000 })
  expect(new Set(await codesOf(page)).size).toBe(20)

  expect(continuationRequests.length, 'the filtered continuation and its retries were not observed').toBe(5)
  const cursors = continuationRequests.map((url) => url.searchParams.get('cursor'))
  expect(new Set(cursors).size, 'a retry advanced or replaced the failed cursor').toBe(1)
  for (const request of continuationRequests) {
    expect(request.searchParams.get('kind')).toBe('NaturalPerson')
    expect(request.searchParams.get('pageSize')).toBe('10')
    expect(request.searchParams.get('partyState')).toBe('active')
  }

  await net.detach(testInfo)
})

test('B-C01-009 a tampered cursor is refused with a stable code', async ({ page }, testInfo) => {
  const net = await NetworkRecorder.attach(page)
  await openList(page)

  // The APPLICATION's own continuation request is intercepted and its real cursor
  // is corrupted in flight, then forwarded to the real server.
  //
  // The obvious alternative — issuing a raw page.request.get — was tried and is
  // wrong: it does not carry the headers the client attaches, so the server
  // answers 401 and the case proves nothing about cursor validation. Rewriting
  // the app's own request keeps every header, so what comes back is the server
  // refusing a tampered token from an otherwise legitimate caller.
  let tamperedOnce = false
  await page.route('**/api/v1/counterparties*', async (route) => {
    const url = new URL(route.request().url())
    const cursor = url.searchParams.get('cursor')
    if (tamperedOnce || !cursor) return route.fallback()
    tamperedOnce = true
    url.searchParams.set('cursor', `${cursor.slice(0, -4)}ZZZZ`)
    return route.continue({ url: url.toString() })
  })

  const refused = page.waitForResponse(
    (r) => r.url().includes('/counterparties') && r.status() === 400,
    { timeout: 30_000 },
  )
  await page.getByRole('button', { name: 'Daha fazla yükle' }).click()
  const response = await refused
  expect(tamperedOnce, 'no cursor request was intercepted').toBe(true)

  const problem = (await response.json()) as { code?: string; detail?: string }
  // The stable machine code is the contract; the localized detail is not, and the
  // UI must not branch on it.
  expect(problem.code).toBe('invalid_cursor')

  // A refused cursor cannot be retried — resending it would fail forever — so the
  // screen recovers to the first page and SAYS SO in the live region rather than
  // leaving the user on a dead-end control.
  //
  // My first version of this assertion expected the load-more button to disappear.
  // That was invented; the case definition says nothing about it, and the measured
  // behaviour is better than what I assumed. Asserting the announcement pins the
  // recovery that actually exists.
  await expect(page.getByRole('status')).toHaveText('Liste güncellendi; ilk sayfaya dönüldü.', {
    timeout: 20_000,
  })
  await expect(rows(page)).toHaveCount(50)

  await net.detach(testInfo)
})

test('B-C01-010 party state and account state are separate facts', async ({ page }, testInfo) => {
  const net = await NetworkRecorder.attach(page)
  await openList(page)

  const row = rows(page).filter({ hasText: SPLIT_STATE_ROW }).first()
  await expect(row).toBeVisible()
  // Global Party state and the state INSIDE this company are different columns;
  // collapsing them would hide a party that is active overall but closed here.
  await expect(row.locator('td[data-party-state]')).toHaveAttribute('data-party-state', 'active')
  await expect(row.locator('td[data-account-state]')).toHaveAttribute(
    'data-account-state',
    'inactive',
  )

  // A globally inactive party is hidden by the default filter.
  expect(await codesOf(page)).not.toContain(GLOBAL_INACTIVE_ROW)

  await net.detach(testInfo)
})
