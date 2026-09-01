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
  release: () => void
  wasRequested: () => boolean
  wasDelivered: () => boolean
}> {
  let release: () => void = () => {}
  const held = new Promise<void>((resolve) => {
    release = resolve
  })
  let requested = false
  let delivered = false
  let used = false
  await page.route('**/api/v1/counterparties*', async (route: Route) => {
    if (used) return route.fallback()
    used = true
    requested = true
    await held
    try {
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
      // Only true if the response really reached the page. If the request had been
      // torn down — by a reload, or by the client abandoning it — fulfil throws and
      // the case would be passing because nothing was delivered, which is the
      // failure mode this whole suite exists to avoid.
      delivered = true
    } catch {
      delivered = false
    }
  })
  return {
    release: () => release(),
    wasRequested: () => requested,
    wasDelivered: () => delivered,
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

test('SAB-008 the stale-actor window is not reachable in a browser, and this is why', async ({
  page,
}) => {
  // HONEST SCOPE. I could not produce SAB-008 in a browser, and rather than dress
  // a vacuous pass as evidence, this case pins the REASON — which is a real
  // regression guard.
  //
  // What I tried first: hold actor A's business 401, make the server answer
  // identity as somebody else, fire window focus so the shell observes it, then
  // deliver A's 401. It passed. It passed for the wrong reason: measured, the
  // focus produced ZERO identity requests, so the shell never left actor A, A's
  // 401 was CURRENT, and terminating the session was correct behaviour. The
  // earlier version of this case used page.reload() instead — which tears the page
  // down and takes the held request with it, so the 401 was never delivered at
  // all. Both are the abort-instead-of-delivery trap this suite exists to avoid.
  //
  // The root cause is a deliberate app decision: ShellProvider sets
  // `refetchOnWindowFocus: false`, and the identity query has no other refetch
  // trigger. The authenticated actor therefore cannot change inside one page
  // lifetime, and a page load destroys any in-flight read. There is no browser
  // path that delivers a departed actor's response into a live successor session.
  //
  // The invariant itself IS proved, at the level where it is decidable:
  // `src/shell/authBoundaryLiveActor.test.tsx` drives the real ShellProvider, the
  // real orchestrator and the real useAuthBoundary, reads the actor from the live
  // identity cache through production code, and is mutation-checked three ways
  // (wrong query key, always-null, constant-wrong-actor).
  //
  // If someone later enables focus refetching, the window opens and this test goes
  // red — which is exactly when the browser case becomes both necessary and
  // writable.
  await openList(page)

  let identityRequests = 0
  page.on('request', (request) => {
    if (request.url().includes('/api/v1/identity/me')) identityRequests += 1
  })

  await page.evaluate(() => {
    window.dispatchEvent(new Event('blur'))
    window.dispatchEvent(new Event('focus'))
    document.dispatchEvent(new Event('visibilitychange'))
  })
  await page.waitForTimeout(2500)

  expect(
    identityRequests,
    'identity now refetches without a reload: the stale-actor window is reachable in ' +
      'the browser and SAB-008 must be written as a real delivered-stale case',
  ).toBe(0)
})

test.skip('SAB-008-BROWSER stale actor A 401 vs live actor B', async ({ page }) => {
  // Kept as the shape the case must take once identity can refetch in place.
  // Skipped, not deleted, and never counted as evidence.
  const gate = await holdNextBusinessRead(page, 401)
  await page.locator('th').filter({ hasText: 'Kod' }).first().getByRole('button').click()
  await expect.poll(() => gate.wasRequested(), { timeout: 20_000 }).toBe(true)

  // The server now authenticates somebody else, and the shell observes it through
  // a real focus-driven identity refetch.
  //
  // NOT page.reload(): a reload tears the page down, which takes the held request
  // with it. The 401 would then never be delivered and this case would pass
  // vacuously — the abort-instead-of-delivery trap. Keeping the page alive is what
  // makes the delivery real, and `wasDelivered` below is the check on that.
  await serverAuthenticatesDifferentActor(page, '20000000-0000-0000-0000-000000000009')
  await page.evaluate(() => {
    window.dispatchEvent(new Event('blur'))
    window.dispatchEvent(new Event('focus'))
  })
  await page.waitForTimeout(2000)

  // Deliver actor A's 401 now. It is not prevented; it arrives.
  gate.release()
  await page.waitForTimeout(2500)
  expect(
    gate.wasDelivered(),
    'the stale 401 was never delivered, so this case proved nothing',
  ).toBe(true)

  // It must be discarded: the session is not terminated by a departed actor.
  await expect(
    page.locator(TERMINATED),
    'a stale 401 for the departed actor terminated the session',
  ).toHaveCount(0)
})

test('SAB-009 a stale 403 for the departed company cannot revoke the new company', async ({
  page,
}) => {
  await openList(page)

  // A business read bound to GDC is issued and its 403 is withheld.
  const gate = await holdNextBusinessRead(page, 403)
  await page.locator('th').filter({ hasText: 'Kod' }).first().getByRole('button').click()
  await expect.poll(() => gate.wasRequested(), { timeout: 20_000 }).toBe(true)

  // Commit GSP while GDC's 403 is still in flight.
  const picker = page.getByRole('combobox', { name: 'Şirket', exact: true })
  await picker.selectOption(GSP)
  await expect(rows(page)).toHaveCount(2, { timeout: 30_000 })
  const gspCodes = await rows(page).locator('td:first-child').allInnerTexts()
  expect(gspCodes).toContain(GSP_ROW)

  // Deliver the departed company's 403.
  gate.release()
  await page.waitForTimeout(2500)
  expect(
    gate.wasDelivered(),
    'the stale 403 was never delivered, so this case proved nothing',
  ).toBe(true)

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
