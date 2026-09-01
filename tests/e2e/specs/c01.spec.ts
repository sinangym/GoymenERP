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

test.describe.configure({ mode: 'serial' })

test('B-C01-001 the authorized first page is the fixture, and only the fixture', async ({
  page,
}, testInfo) => {
  const net = await NetworkRecorder.attach(page)
  await testInfo.attach('c01-browser-version', {
    body: Buffer.from(page.context().browser()?.version() ?? '', 'utf8'),
    contentType: 'text/plain',
  })

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

  await net.detach(testInfo)
  await context.close()
})

test('B-C01-003 a company switch cannot leak the company we left', async ({ page }, testInfo) => {
  const net = await NetworkRecorder.attach(page)
  await openList(page)
  await expect(rows(page)).toHaveCount(50)

  // Hold the GSP read at the response stage so the switch is observed while the
  // old company's rows are still painted. This is the delivery-not-prevention
  // window that jsdom could never reach.
  await net.delayNextCounterpartyRead(600)

  const picker = page.getByRole('combobox', { name: 'Şirket', exact: true })
  await picker.selectOption(GSP)

  await expect(rows(page)).toHaveCount(GSP_ACTIVE_VISIBLE, { timeout: 30_000 })
  const gspCodes = await codesOf(page)
  expect(gspCodes).toContain(GSP_ONLY)
  // Nothing that belongs only to GDC may survive the switch.
  expect(gspCodes).not.toContain(FIRST_ROW.code)
  expect(await page.locator('body').innerText()).not.toContain(FIRST_ROW.name)

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
  await expect(sortButton).toBeFocused()

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
  context,
}, testInfo) => {
  const net = await NetworkRecorder.attach(page)
  await openList(page)
  await expect(rows(page)).toHaveCount(50)

  const more = page.getByRole('button', { name: 'Daha fazla yükle' })
  const alert = page.getByRole('alert')

  // Each fault is injected at the response stage, once, and the verified first
  // page must survive every one of them.
  for (const fault of ['injected-500', 'injected-503', 'injected-abort'] as const) {
    await net.failNextCounterpartyRead(fault)
    await more.click()
    await expect(alert, `no continuation error after ${fault}`).toBeVisible({ timeout: 30_000 })
    await expect(rows(page), `rows were dropped on ${fault}`).toHaveCount(50)
  }

  // Offline is a transport fault, not a status code, so it is produced by the
  // browser rather than by an injected response.
  await context.setOffline(true)
  net.note('/api/v1/counterparties', 'injected-offline')
  await more.click()
  await expect(alert).toBeVisible({ timeout: 30_000 })
  await expect(rows(page)).toHaveCount(50)
  await context.setOffline(false)

  // The retry is explicit and user-driven; nothing was resent automatically.
  await page.getByRole('alert').getByRole('button').click()
  await expect(rows(page)).toHaveCount(GDC_ACTIVE_VISIBLE, { timeout: 30_000 })
  net.noteRetrySuccess(1)
  expect(new Set(await codesOf(page)).size).toBe(GDC_ACTIVE_VISIBLE)

  await net.detach(testInfo)
})

test('B-C01-009 a tampered cursor is refused with a stable code', async ({ page }, testInfo) => {
  const net = await NetworkRecorder.attach(page)
  await openList(page)

  // The cursor is taken from the REAL first-page response and then corrupted, so
  // this exercises the server's own validation rather than a made-up token.
  const response = await page.request.get(
    `${env.authorizedBaseUrl}/api/v1/counterparties?pageSize=50`,
    { headers: { 'X-Company-Id': GDC } },
  )
  expect(response.status()).toBe(200)
  const body = (await response.json()) as { nextCursor: string | null }
  expect(body.nextCursor, 'the fixture must produce a real cursor').toBeTruthy()
  const tampered = `${body.nextCursor!.slice(0, -4)}ZZZZ`

  const refused = await page.request.get(
    `${env.authorizedBaseUrl}/api/v1/counterparties?pageSize=50&cursor=${encodeURIComponent(tampered)}`,
    { headers: { 'X-Company-Id': GDC } },
  )
  expect(refused.status()).toBe(400)
  const problem = (await refused.json()) as { code?: string; detail?: string }
  // The stable machine code is the contract; the localized detail is not.
  expect(problem.code).toBe('invalid_cursor')
  net.note('/api/v1/counterparties', 'real-400-invalid-cursor')

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
