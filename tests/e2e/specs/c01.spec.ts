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

import { expect, test, type Locator, type Page } from '@playwright/test'
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

const UUID_RE = /[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}/

async function openList(page: Page): Promise<void> {
  await page.goto('/cari')
  await expect(page.getByRole('heading', { level: 1, name: 'Cari Kartlar' })).toBeVisible()
  await expect(page.getByRole('table')).toBeVisible()
}

function rows(page: Page) {
  return page.locator('table tbody tr')
}

async function codesOf(page: Page): Promise<string[]> {
  return rows(page).locator('td:first-child').allInnerTexts()
}

/**
 * Focus that a KEYBOARD user can actually see (#146 C01-AC-007).
 *
 * `toBeFocused` alone is not the claim: an element can hold focus with no
 * indicator at all, which is precisely the failure WCAG 2.4.7 describes. So the
 * element must match `:focus-visible` -- the state the UA enters for keyboard
 * navigation and not for a mouse click -- and something must actually be drawn.
 */
async function expectVisibleFocus(target: Locator, what: string): Promise<void> {
  await expect(target, `${what} did not take focus`).toBeFocused()
  const indicator = await target.evaluate((element) => {
    const style = getComputedStyle(element)
    return {
      focusVisible: element.matches(':focus-visible'),
      outlineStyle: style.outlineStyle,
      boxShadow: style.boxShadow,
    }
  })
  expect(indicator.focusVisible, `${what} is focused but not :focus-visible`).toBe(true)
  expect(
    indicator.outlineStyle !== 'none' || indicator.boxShadow !== 'none',
    `${what} has focus with no visible indicator (outline=${indicator.outlineStyle}, ` +
      `box-shadow=${indicator.boxShadow})`,
  ).toBe(true)
}

/**
 * Count what a screen reader would actually be told (#146 C01-AC-007).
 *
 * Counts DOM MUTATIONS of any live region, not distinct strings, because the
 * failure being ruled out is the same message being announced twice after a
 * re-render, a focus change or a cache update -- a screen reader speaks that
 * second mutation even though the text did not change.
 *
 * One observer callback is one batch, and a batch touching one region counts
 * once: `childList` and `characterData` records for the same update are one
 * announcement, not two.
 */
async function watchAnnouncements(page: Page): Promise<void> {
  await page.evaluate(() => {
    const w = window as unknown as { __announced?: string[] }
    w.__announced = []
    const selector = '[role="status"], [role="alert"], [aria-live]'
    new MutationObserver((records) => {
      const hosts = new Set<Element>()
      for (const record of records) {
        const node = record.target
        const element = node instanceof Element ? node : node.parentElement
        const host = element?.closest(selector)
        if (host) hosts.add(host)
      }
      for (const host of hosts) {
        const text = (host.textContent ?? '').trim()
        if (text !== '') w.__announced!.push(text)
      }
    }).observe(document.body, { childList: true, subtree: true, characterData: true })
  })
}

async function announcedSince(page: Page, from: number): Promise<string[]> {
  const all = await page.evaluate(
    () => (window as unknown as { __announced?: string[] }).__announced ?? [],
  )
  return all.slice(from)
}

async function announcedCount(page: Page): Promise<number> {
  return (await page.evaluate(
    () => (window as unknown as { __announced?: string[] }).__announced ?? [],
  )).length
}

/** Reach a control using nothing but Tab, or fail saying it is unreachable. */
async function tabTo(page: Page, target: Locator, what: string, limit = 40): Promise<void> {
  for (let press = 0; press < limit; press += 1) {
    if (await target.evaluate((element) => element === document.activeElement)) return
    await page.keyboard.press('Tab')
  }
  throw new Error(`${what} was not reachable by keyboard within ${limit} tabs`)
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

test('B-C01-004 sorting is done by the server, and focus survives it', async ({
  page,
}, testInfo) => {
  const net = await NetworkRecorder.attach(page)
  await openList(page)

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
  await expectVisibleFocus(sortButton, 'the sort control')

  // ---- #146 C01-AC-007: the whole filter set, keyboard only ----------------
  //
  // The owner audit called the previous version PARTIAL because it proved one
  // control. Being "in the tab order" is not the claim either: each control has
  // to be REACHABLE by Tab, show a focus indicator a keyboard user can see,
  // produce a real server query when actuated with the keyboard, and still hold
  // focus after the re-render that answer causes.
  const reads: URLSearchParams[] = []
  page.on('request', (request) => {
    if (!request.url().includes('/api/v1/counterparties')) return
    reads.push(new URL(request.url()).searchParams)
  })

  /**
   * Actuate, then wait for the read that CARRIES THE EXPECTED CHANGE.
   *
   * "A new read arrived" is a weaker claim and it raced: the search box issues a
   * read per keystroke, so the newest read at any instant is the one for the
   * prefix typed so far. The real run caught this asserting `q=CAR-0000`
   * against the `CAR-00001` that was typed. Matching on the value removes the
   * race instead of papering over it with a longer wait.
   */
  async function actuate(
    what: string,
    act: () => Promise<void>,
    carries: (params: URLSearchParams) => boolean,
  ): Promise<URLSearchParams> {
    const from = reads.length
    await act()
    let found: URLSearchParams | undefined
    await expect
      .poll(
        () => {
          found = reads.slice(from).find(carries)
          return found !== undefined
        },
        { message: `${what} produced no server read carrying the change`, timeout: 20_000 },
      )
      .toBe(true)
    return found as URLSearchParams
  }

  const QUERY = 'CAR-00001'
  const search = page.getByRole('searchbox', { name: 'Ara' })
  await tabTo(page, search, 'the search box')
  await expectVisibleFocus(search, 'the search box')
  await actuate(
    'typing in search',
    async () => {
      await page.keyboard.type(QUERY)
    },
    (params) => params.get('q') === QUERY,
  )
  await expectVisibleFocus(search, 'the search box after its own result landed')
  await actuate(
    'clearing search',
    async () => {
      // Backspace per character, not Control+A: the select-all did not take in
      // the real browser, so exactly one character was deleted and the box never
      // emptied. Still keyboard-only, and deterministic.
      for (let index = 0; index < QUERY.length; index += 1) {
        await page.keyboard.press('Backspace')
      }
    },
    (params) => (params.get('q') ?? '') === '',
  )

  const kind = page.getByRole('combobox', { name: 'Tür' })
  await tabTo(page, kind, 'the kind filter')
  await expectVisibleFocus(kind, 'the kind filter')
  await actuate(
    'choosing a kind with the keyboard',
    async () => {
      await page.keyboard.press('ArrowDown')
    },
    (params) => (params.get('kind') ?? '') !== '',
  )
  await expectVisibleFocus(kind, 'the kind filter after its result landed')

  // All four roles, each with Space, and the roleMatch control that only exists
  // once two are selected.
  let selected = 1
  for (const role of ['Müşteri', 'Tedarikçi', 'Fason', 'Hizmet']) {
    const box = page.getByRole('checkbox', { name: role })
    await tabTo(page, box, `the ${role} role filter`)
    await expectVisibleFocus(box, `the ${role} role filter`)
    await actuate(
      `selecting ${role} with Space`,
      async () => {
        await page.keyboard.press('Space')
      },
      (params) => params.getAll('role').length === selected,
    )
    await expectVisibleFocus(box, `the ${role} role filter after its result landed`)
    selected += 1
  }

  const roleMatch = page.getByRole('combobox', { name: 'Rol eşleşmesi' })
  await expect(roleMatch, 'roleMatch never appeared with four roles selected').toBeVisible()
  await tabTo(page, roleMatch, 'the role match control')
  await expectVisibleFocus(roleMatch, 'the role match control')
  await actuate(
    'changing role match with the keyboard',
    async () => {
      await page.keyboard.press('ArrowDown')
    },
    (params) => params.get('roleMatch') === 'all',
  )

  for (const [name, key] of [
    ['Taraf durumu', 'partyState'],
    ['Şirket hesabı durumu', 'accountState'],
  ] as const) {
    const control = page.getByRole('combobox', { name })
    await tabTo(page, control, `the ${name} filter`)
    await expectVisibleFocus(control, `the ${name} filter`)
    const was = await control.inputValue()
    await actuate(
      `changing ${name} with the keyboard`,
      async () => {
        await page.keyboard.press('ArrowDown')
      },
      (params) => (params.get(key) ?? '') !== '' && params.get(key) !== was,
    )
    await expectVisibleFocus(control, `the ${name} filter after its result landed`)
  }

  await net.detach(testInfo)
})

test('B-C01-005 the page reflows without a horizontal scrollbar', async ({ page }, testInfo) => {
  const net = await NetworkRecorder.attach(page)
  await openList(page)

  for (const size of ['1600x900', '768x1024', '390x844', '640x900', '320x900']) {
    const [width, height] = size.split('x').map(Number)
    await page.setViewportSize({ width, height })
    await expect(rows(page).first()).toBeVisible()

    // WCAG 1.4.10: the PAGE must not scroll horizontally. A previous version of
    // this screen passed jsdom and failed exactly here.
    const overflows = await page.evaluate(
      () => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1,
    )
    expect(overflows, `page scrolls horizontally at ${size}`).toBe(false)

    // The table may scroll, but only inside its own labelled region, and it must
    // keep table semantics while doing so.
    const display = await page.locator('table').evaluate((el) => getComputedStyle(el).display)
    expect(display, `table lost table semantics at ${size}`).toBe('table')
    await expect(page.getByRole('region', { name: 'Cari listesi tablosu' })).toBeVisible()
  }

  // ---- #146 C01-AC-007: the task, completed at the two hard sizes ----------
  //
  // Measuring overflow says the layout did not break. It does not say the user
  // could still DO anything, which is what the owner audit called PARTIAL. So at
  // mobile and at the 200%-zoom equivalent the whole task runs: search, change a
  // filter, read the result, and come back from the empty result.
  for (const size of ['390x844', '640x900']) {
    const [width, height] = size.split('x').map(Number)
    await page.setViewportSize({ width, height })
    await openList(page)

    const search = page.getByRole('searchbox', { name: 'Ara' })
    await expect(search, `search is unreachable at ${size}`).toBeVisible()
    await search.fill(SPLIT_STATE_ROW)
    await expect
      .poll(async () => (await codesOf(page)).length, { timeout: 20_000 })
      .toBeLessThan(50)
    expect(await codesOf(page), `search lost its result at ${size}`).toContain(SPLIT_STATE_ROW)

    const accountState = page.getByRole('combobox', { name: 'Şirket hesabı durumu' })
    await expect(accountState, `the account-state filter is unreachable at ${size}`).toBeVisible()
    await accountState.selectOption('active')
    // The party's account is passive here, so this must empty the result -- and
    // the screen has to SAY it is empty rather than showing nothing at all.
    //
    // Asserted on the STABLE state attribute, not the Turkish copy: the first
    // version of this line invented a string that does not exist in the app.
    await expect(page.locator('[data-state="empty-filtered"]')).toBeVisible({ timeout: 20_000 })
    await expect(page.getByText('Filtreye uyan kayıt bulunamadı.')).toBeVisible()

    // And the user can get back out of it, which is the half a static overflow
    // check can never see.
    await accountState.selectOption('all')
    await expect
      .poll(async () => (await codesOf(page)).includes(SPLIT_STATE_ROW), { timeout: 20_000 })
      .toBe(true)

    // Nothing was pushed off-screen or buried by another control while doing it.
    for (const control of [search, accountState]) {
      const box = await control.boundingBox()
      expect(box, `a control vanished at ${size}`).not.toBeNull()
      expect(box!.width, `a control collapsed at ${size}`).toBeGreaterThan(0)
      expect(box!.x, `a control sits off the left edge at ${size}`).toBeGreaterThanOrEqual(0)
      expect(box!.x + box!.width, `a control overflows the viewport at ${size}`).toBeLessThanOrEqual(
        width + 1,
      )
    }
    const stillNoOverflow = await page.evaluate(
      () => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1,
    )
    expect(stillNoOverflow, `the page scrolled horizontally mid-task at ${size}`).toBe(false)

    await search.fill('')
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
  await expect(page.getByRole('search')).toBeVisible()
  // A settled table must say so; a stuck aria-busy makes a screen reader wait
  // forever for a load that already finished.
  await expect(page.getByRole('table')).toHaveAttribute('aria-busy', 'false')
  // Internal capability names are not user-facing vocabulary.
  const shown = await page.locator('body').innerText()
  expect(shown).not.toContain('filtered-authorized-visible-parties')
  expect(shown.toLowerCase()).not.toContain('capability')

  // ---- #146 C01-AC-007: the empty-filter transition is announced ONCE ------
  //
  // The presence of a live region is not the claim -- that is asserted above.
  // The claim is what a screen reader is TOLD, and the failure being ruled out
  // is the same message spoken twice because a re-render, a focus change or a
  // cache update touched the region again.
  await watchAnnouncements(page)
  const before = await announcedCount(page)
  await page.getByRole('searchbox', { name: 'Ara' }).fill('ZZZ-NO-SUCH-RECORD')
  await expect(page.locator('[data-state="empty-filtered"]')).toBeVisible({ timeout: 20_000 })
  // Settle: anything spoken after this point would still be a second telling.
  await page.waitForTimeout(1_500)
  const spoken = await announcedSince(page, before)
  expect(
    spoken.length,
    `the empty-filter transition was announced ${spoken.length} times: ${JSON.stringify(spoken)}`,
  ).toBe(1)

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
  await expect(rows(page)).toHaveCount(50)

  const alert = page.getByRole('alert')
  const retry = alert.getByRole('button')

  // Once a continuation has failed, the screen replaces "Daha fazla yükle" with a
  // scoped retry — the load-more control is GONE, not merely disabled. That is the
  // shape DEC-CAR-004 asks for (explicit user retry, no automatic resend), so every
  // fault after the first is driven through the retry button.
  // #146 C01-AC-007: both transitions below are counted, because the failure
  // being ruled out is the same message reaching a screen reader twice.
  await watchAnnouncements(page)
  const beforeError = await announcedCount(page)

  const used500 = await net.failNextCounterpartyRead('injected-500')
  await page.getByRole('button', { name: 'Daha fazla yükle' }).click()
  await expect.poll(used500, { timeout: 20_000 }).toBe(true)
  await expect(alert, 'no continuation error after injected-500').toBeVisible({ timeout: 30_000 })
  await expect(rows(page), 'rows were dropped on injected-500').toHaveCount(50)
  await expect(page.getByRole('button', { name: 'Daha fazla yükle' })).toHaveCount(0)

  await page.waitForTimeout(1_500)
  const errorSpoken = await announcedSince(page, beforeError)
  expect(
    errorSpoken.length,
    `the continuation error was announced ${errorSpoken.length} times: ` +
      JSON.stringify(errorSpoken),
  ).toBe(1)

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
    await expect(rows(page), `rows were dropped on ${fault}`).toHaveCount(50)
  }

  // A dropped connection is a transport fault, not a status code.
  const usedDrop = await net.dropNextCounterpartyRead()
  await retry.click()
  await expect.poll(usedDrop, { timeout: 20_000 }).toBe(true)
  await expect(alert).toBeVisible({ timeout: 30_000 })
  await expect(rows(page)).toHaveCount(50)

  // Four failures, and the verified rows never moved. The successful retry then
  // appends the rest exactly once — proof that each failed attempt re-issued the
  // same cursor rather than advancing it.
  // The recovery is OBSERVED, not asserted in advance: the recorder only writes
  // retry-200 when a real 200 actually arrives for this read.
  const beforeRecovery = await announcedCount(page)
  net.expectRetrySuccess(1)
  await retry.click()
  await expect(rows(page)).toHaveCount(GDC_ACTIVE_VISIBLE, { timeout: 30_000 })
  expect(new Set(await codesOf(page)).size).toBe(GDC_ACTIVE_VISIBLE)

  // A retry that WORKED has to be spoken, exactly once. Before this the only
  // signal was the alert disappearing, which a screen reader does not announce:
  // the user pressed the button and heard nothing at all.
  await page.waitForTimeout(1_500)
  const recoverySpoken = await announcedSince(page, beforeRecovery)
  expect(
    recoverySpoken.length,
    `the successful retry was announced ${recoverySpoken.length} times: ` +
      JSON.stringify(recoverySpoken),
  ).toBe(1)
  expect(recoverySpoken[0]).toContain('kalan kayıtlar yüklendi')

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
  // The ORDINAL sequence, recorded on the request so an attempt that was sent
  // and then aborted still counts. #146 C01-AC-005 is about the ORDER --
  // `page1 200 -> invalid_cursor 400 -> fresh cursorless page1 200` -- because
  // the version this replaces recovered from React Query's cache and issued no
  // third request at all: nothing re-checked that page 1 is still what the
  // server would serve.
  const reads: { cursor: string | null }[] = []
  page.on('request', (request) => {
    if (!request.url().includes('/api/v1/counterparties')) return
    reads.push({ cursor: new URL(request.url()).searchParams.get('cursor') })
  })
  await watchAnnouncements(page)
  const spokenBefore = await announcedCount(page)

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

  // Exactly three reads, in exactly this shape.
  await expect
    .poll(() => reads.length, { timeout: 20_000 })
    .toBe(3)
  expect(reads[0].cursor, 'the first read carried a cursor').toBeNull()
  expect(reads[1].cursor, 'the continuation carried no cursor to tamper with').not.toBeNull()
  expect(
    reads[2].cursor,
    'the recovery replayed a cursor instead of asking for page 1 afresh',
  ).toBeNull()

  // Deliberately NOT asserting a row count here as well: 50 rows would also hold
  // if the recovery had silently re-rendered the cache. The read sequence above
  // is the claim, and a tautological second assertion would only make the case
  // look stronger than it is.

  // #146 C01-AC-007: the recovery is spoken exactly once. The fresh page landing
  // is a re-render of the same region, which is precisely how a second telling
  // gets in.
  await page.waitForTimeout(1_500)
  const spokenAfter = await announcedSince(page, spokenBefore)
  expect(
    spokenAfter.length,
    `the recovery was announced ${spokenAfter.length} times: ${JSON.stringify(spokenAfter)}`,
  ).toBe(1)

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

  // ---- #146 C01-AC-002: the matrix, proven by three REAL server reads --------
  //
  // The two states being separate columns is a rendering claim. That they are
  // separate FILTERS is a server claim, and the owner audit called the previous
  // version PARTIAL for asserting only the first. CAR-000002 is the fixture's
  // only party that is globally active with a passive account in GDC, so it
  // appears, disappears and reappears across the three account-state values --
  // a result client-side filtering of one response could imitate, which is why
  // each step is bound to its own request.
  const reads: { url: string; company: string | null }[] = []
  page.on('request', (request) => {
    if (!request.url().includes('/api/v1/counterparties')) return
    reads.push({ url: request.url(), company: request.headers()['x-company-id'] ?? null })
  })

  const accountState = page.getByRole('combobox', { name: 'Şirket hesabı durumu' })
  const partyState = page.getByRole('combobox', { name: 'Taraf durumu' })
  await expect(partyState).toHaveValue('active')

  /** Change one filter and wait for the read it causes, not for a timeout. */
  async function applyAccountState(value: string): Promise<void> {
    const before = reads.length
    await accountState.selectOption(value)
    await expect.poll(() => reads.length, { timeout: 20_000 }).toBeGreaterThan(before)
    await expect(page.locator('[data-state="initial-loading"]')).toHaveCount(0, {
      timeout: 20_000,
    })
  }

  await applyAccountState('all')
  expect(await codesOf(page), 'accountState=all hid the split-state party').toContain(
    SPLIT_STATE_ROW,
  )

  await applyAccountState('active')
  expect(
    await codesOf(page),
    'accountState=active still showed a party whose account here is passive',
  ).not.toContain(SPLIT_STATE_ROW)

  await applyAccountState('inactive');
  {
    const codes = await codesOf(page)
    expect(codes, 'accountState=inactive lost the only passive account').toContain(SPLIT_STATE_ROW)
    // The fixture has exactly one, so this also proves the server applied the
    // filter rather than the page hiding rows it had already been given.
    expect(codes).toHaveLength(1)
  }

  // Every read was a real, correctly bound, cursorless page-1 request. The
  // company header matters as much as the query: a filter proven under the wrong
  // tenant proves nothing.
  const matrix = reads.slice(-3).map((read) => new URL(read.url).searchParams)
  expect(matrix[0].get('accountState')).toBe('all')
  expect(matrix[1].get('accountState')).toBe('active')
  expect(matrix[2].get('accountState')).toBe('inactive')
  for (const [index, params] of matrix.entries()) {
    expect(params.get('partyState'), 'party state drifted while account state changed').toBe(
      'active',
    )
    // A filter change restarts paging; carrying the old cursor would page
    // through a result set that no longer exists.
    expect(params.get('cursor'), `read ${index} carried a stale cursor`).toBeNull()
  }
  for (const read of reads.slice(-3)) {
    expect(read.company, 'a read was issued without the active company binding').toBe(GDC)
  }

  await net.detach(testInfo)
})
