/**
 * SHELL-AUTH-BOUNDARY-001 browser acceptance — Issue #165, the four cases jsdom
 * cannot reach.
 *
 * Every fault here is injected at the RESPONSE stage. That is the whole point: a
 * superseded request in jsdom is aborted, so nothing is delivered and therefore
 * nothing is rejected. An abort is a safe outcome, not evidence. Here the real
 * response exists, the context moves, and only then is the response delivered.
 */

import { expect, test, type Page, type Route } from '@playwright/test'

const GDC = '10000000-0000-0000-0000-000000000001'
const GSP = '10000000-0000-0000-0000-000000000002'

/** The row the fixture puts first under GDC. */
const GDC_ROW = 'CAR-000001'
/** Present only under GSP. */
const GSP_ROW = 'CAR-000054'

const TERMINATED = '[data-state="unauthenticated"]'

/**
 * Assert a CENTRAL session termination, not a local one.
 *
 * `[data-state="unauthenticated"]` alone is not enough and I proved it: run the
 * suite against a build WITHOUT the boundary and that selector still matches,
 * because the list page renders its own local unauthenticated block inside a
 * perfectly healthy shell. The discriminator is that a central termination
 * replaces the WHOLE shell — no nav, no company picker, no table.
 */
async function expectCentrallyTerminated(page: Page): Promise<void> {
  await expect(page.locator(TERMINATED)).toBeVisible({ timeout: 30_000 })
  await expect(
    page.getByRole('link', { name: 'Cari Kartlar' }),
    'the shell nav survived, so this was a LOCAL failure, not a central termination',
  ).toHaveCount(0)
  await expect(
    page.getByRole('combobox', { name: 'Şirket', exact: true }),
    'the company picker survived, so the shell was never torn down',
  ).toHaveCount(0)
  await expect(page.getByRole('table')).toHaveCount(0)
}

function rows(page: Page) {
  return page.locator('table tbody tr')
}

async function openList(page: Page): Promise<void> {
  await page.goto('/cari')
  await expect(page.getByRole('heading', { level: 1, name: 'Cari Kartlar' })).toBeVisible({
    timeout: 30_000,
  })
  await expect(rows(page)).toHaveCount(50, { timeout: 30_000 })
}

/**
 * Hold the next business read and answer it with `status` only when released.
 *
 * The request is genuinely issued and the client genuinely waits; the caller can
 * move the context in between and then deliver a response that is stale by the
 * time it lands.
 */
async function holdNextBusinessRead(
  page: Page,
  status: 401 | 403,
): Promise<{
  ready: Promise<void>
  release: () => void
  /** True only once the PAGE observed the response — not merely that fulfil resolved. */
  wasReceivedByPage: () => boolean
  /** The exact URL of the held read, for request-level assertions. */
  url: () => string | null
}> {
  let release: () => void = () => {}
  const held = new Promise<void>((resolve) => {
    release = resolve
  })
  let markReady: () => void = () => {}
  const ready = new Promise<void>((resolve) => {
    markReady = resolve
  })

  // DELIVERY IS OBSERVED FROM THE PAGE SIDE.
  //
  // An earlier version reported delivery from whether `route.fulfill` resolved.
  // Codex was right that this proves nothing about the client: fulfil resolving
  // says the interceptor wrote a response, not that the production queryFn read
  // its status and reported it. The response event is the page's own account of
  // having received it.
  let receivedByPage = false
  let heldUrl: string | null = null
  page.on('response', (response) => {
    if (heldUrl !== null && response.url() === heldUrl && response.status() === status) {
      receivedByPage = true
    }
  })

  let used = false
  await page.route('**/api/v1/counterparties*', async (route: Route) => {
    if (used) return route.fallback()
    used = true
    heldUrl = route.request().url()
    markReady()
    await held
    await route.fulfill({
      status,
      contentType: 'application/problem+json',
      body: JSON.stringify({
        type: 'about:blank',
        title: status === 401 ? 'authentication_required' : 'forbidden',
        status,
        code: status === 401 ? 'authentication_required' : 'forbidden',
      }),
    })
  })
  return {
    ready,
    release: () => release(),
    wasReceivedByPage: () => receivedByPage,
    url: () => heldUrl,
  }
}

/** Make the server answer identity as a different actor from now on. */
async function serverAuthenticatesDifferentActor(page: Page, userId: string): Promise<void> {
  await page.route('**/api/v1/identity/me', async (route: Route) => {
    const response = await route.fetch()
    const body = (await response.json()) as Record<string, unknown>
    return route.fulfill({
      response,
      body: JSON.stringify({ ...body, userId, displayName: 'Başka Kullanıcı' }),
      contentType: 'application/json',
    })
  })
}

test.describe.configure({ mode: 'serial' })

test('SAB-006 a refetch 401 ends the session exactly like a first-page 401', async ({ page }) => {
  await openList(page)

  // The parity claim is about the BOUNDARY, not about the trigger: a 401 that
  // arrives on a refetch of an already-successful list must reach the same central
  // decision as one that arrives on the very first read.
  let used = false
  await page.route('**/api/v1/counterparties*', async (route: Route) => {
    if (used) return route.fallback()
    used = true
    return route.fulfill({
      status: 401,
      contentType: 'application/problem+json',
      body: JSON.stringify({ status: 401, code: 'authentication_required' }),
    })
  })

  // A real refetch trigger, not a synthetic dispatch: sorting re-issues the read.
  await page.locator('th').filter({ hasText: 'Kod' }).first().getByRole('button').click()

  await expectCentrallyTerminated(page)
  expect(await page.locator('body').innerText()).not.toContain(GDC_ROW)
})

test('SAB-007 a terminated session cannot be resurfaced by leaving and returning', async ({
  page,
}) => {
  // CLIENT-SIDE navigation only, and the distinction matters. An earlier version
  // used page.goto, which is a full reload: the app re-bootstraps from scratch and
  // legitimately recovers, because the dev identity still authenticates. That
  // proved nothing about the cache. The condition under which stale data could
  // resurface is the one where the shell keeps living — a pushState route change
  // and a popstate — so the route is entered by clicking the nav link.
  await page.goto('/')
  await page.getByRole('link', { name: 'Cari Kartlar' }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Cari Kartlar' })).toBeVisible({
    timeout: 30_000,
  })
  await expect(rows(page)).toHaveCount(50, { timeout: 30_000 })

  let used = false
  await page.route('**/api/v1/counterparties*', async (route: Route) => {
    if (used) return route.fallback()
    used = true
    return route.fulfill({
      status: 401,
      contentType: 'application/problem+json',
      body: JSON.stringify({ status: 401, code: 'authentication_required' }),
    })
  })
  await page.locator('th').filter({ hasText: 'Kod' }).first().getByRole('button').click()
  await expectCentrallyTerminated(page)

  // Browser back / forward over the pushState entry: popstate, not a reload, so
  // the shell and its QueryClient are the same instances throughout.
  await page.goBack()
  await expectCentrallyTerminated(page)
  expect(await page.locator('body').innerText()).not.toContain(GDC_ROW)
  await page.goForward()
  await expectCentrallyTerminated(page)
  expect(await page.locator('body').innerText()).not.toContain(GDC_ROW)

  // Window focus, which is a real refetch trigger for TanStack Query.
  await page.evaluate(() => {
    window.dispatchEvent(new Event('blur'))
    window.dispatchEvent(new Event('focus'))
    document.dispatchEvent(new Event('visibilitychange'))
  })
  await page.waitForTimeout(1500)
  await expectCentrallyTerminated(page)
  expect(await page.locator('body').innerText()).not.toContain(GDC_ROW)
})

test('SAB-008 an actor change ABORTS the in-flight read, so no stale response is delivered', async ({
  page,
  context,
}) => {
  test.setTimeout(150_000)

  // WHAT THIS PROVES, AND WHAT IT DOES NOT. This is PREVENTION, not rejection,
  // and the difference is the whole point of this suite — so it is named in the
  // title rather than buried.
  //
  // I got here by being wrong twice, and both corrections came from measurement.
  // First I claimed the actor could not change inside one page lifetime. Codex
  // refuted it and I reproduced the refutation: ShellProvider sets only
  // `refetchOnWindowFocus: false`, and TanStack's `refetchOnReconnect` defaults to
  // TRUE, so once staleTime has elapsed an offline→online transition refetches
  // identity in place (identity call count 2 → 3). The actor CAN change.
  //
  // Then the delivered-stale version failed at the delivery check. Isolating the
  // three variables one at a time gave the answer:
  //
  //   hold, wait, release                    → received
  //   hold, offline→online, release          → received   (the toggle is innocent)
  //   hold, actor changes, release           → net::ERR_ABORTED
  //
  // The chain, verified in source: `actorMismatch` is a SYNCHRONOUS render gate in
  // AppShell, so an actor change unmounts CariListPage in the same paint; TanStack
  // cancels the query when its last observer goes; the queryFn's AbortSignal fires;
  // and `cari/api.ts` classifies an aborted read as `fence-stale` rather than
  // reporting it to the boundary at all.
  //
  // So in a browser the departed actor's response is never DELIVERED. That is a
  // real security property and it is what this case pins. It is NOT evidence that
  // a delivered stale response would be rejected — nothing was delivered. That
  // invariant is proved where it is decidable, in
  // `src/shell/authBoundaryLiveActor.test.tsx`, against the real provider and hook,
  // mutation-checked three ways.
  //
  // If the abort ever stops happening, this goes red and the delivered-stale
  // browser case becomes both necessary and writable.
  await openList(page)

  let abortedError: string | null = null
  let heldUrl: string | null = null
  page.on('requestfailed', (request) => {
    if (heldUrl !== null && request.url() === heldUrl) {
      abortedError = request.failure()?.errorText ?? 'unknown'
    }
  })

  const gate = await holdNextBusinessRead(page, 401)
  await page.locator('th').filter({ hasText: 'Kod' }).first().getByRole('button').click()
  await gate.ready
  heldUrl = gate.url()

  // The server now authenticates somebody else, and a reconnect makes the shell
  // observe it in place. staleTime must elapse first or the refetch is skipped.
  await serverAuthenticatesDifferentActor(page, '20000000-0000-0000-0000-000000000009')
  await page.waitForTimeout(31_000)
  await context.setOffline(true)
  await page.waitForTimeout(600)
  await context.setOffline(false)

  await expect(
    page.getByText('Başka Kullanıcı'),
    'the actor never changed, so this case exercised nothing',
  ).toBeVisible({ timeout: 30_000 })

  // Release the departed actor's 401. It cannot arrive: the read is already gone.
  gate.release()
  await page.waitForTimeout(2500)

  expect(abortedError, 'the in-flight read survived the actor change').toBe('net::ERR_ABORTED')
  expect(
    gate.wasReceivedByPage(),
    'the stale response WAS delivered — this is now a rejection case, not a prevention one',
  ).toBe(false)

  // And the successor session is intact.
  await expect(page.locator(TERMINATED)).toHaveCount(0)
  await expect(page.getByText('Başka Kullanıcı')).toBeVisible()
})

test('SAB-009 a company switch cancels the old read, so no stale 403 reaches the successor', async ({
  page,
}) => {
  await openList(page)

  // A business read bound to GDC is issued and its 403 is withheld.
  let abortedError: string | null = null
  let heldUrl: string | null = null
  page.on('requestfailed', (request) => {
    if (heldUrl !== null && request.url() === heldUrl) {
      abortedError = request.failure()?.errorText ?? 'unknown'
    }
  })
  const gate = await holdNextBusinessRead(page, 403)
  await page.locator('th').filter({ hasText: 'Kod' }).first().getByRole('button').click()
  await gate.ready
  heldUrl = gate.url()

  // Commit GSP while GDC's 403 is still in flight.
  const picker = page.getByRole('combobox', { name: 'Şirket', exact: true })
  await picker.selectOption(GSP)
  await expect(rows(page)).toHaveCount(2, { timeout: 30_000 })
  const gspCodes = await rows(page).locator('td:first-child').allInnerTexts()
  expect(gspCodes).toContain(GSP_ROW)

  // Deliver the departed company's 403.
  // Release the departed company's 403. Measured: the switch cancels the read, so
  // it cannot arrive — same shape as SAB-008 and as B-C01-003. Prevention, and the
  // title says so.
  gate.release()
  await page.waitForTimeout(2500)
  expect(
    abortedError,
    'the departed company read survived the switch — this is now a delivery case',
  ).toBe('net::ERR_ABORTED')
  expect(
    gate.wasReceivedByPage(),
    'the stale 403 WAS delivered, so this case must assert rejection, not prevention',
  ).toBe(false)

  // GSP's capability must survive: the gated screen is still mounted, still
  // showing GSP's own rows, and the nav entry is still there.
  await expect(
    page.getByRole('table'),
    'a stale 403 for the departed company revoked the new capability',
  ).toBeVisible()
  expect(await rows(page).locator('td:first-child').allInnerTexts()).toEqual(gspCodes)
  await expect(page.getByRole('link', { name: 'Cari Kartlar' })).toBeVisible()
  await expect(picker).toHaveValue(GSP)
  await expect(page.locator(TERMINATED)).toHaveCount(0)
})
