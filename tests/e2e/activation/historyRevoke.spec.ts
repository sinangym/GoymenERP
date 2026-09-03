/**
 * C-01A activation evidence — `success → leave → server-side revoke → Forward`.
 *
 * The one functional case #184 §2.1 leaves open for C-01A, written to that
 * contract rather than to what is convenient.
 *
 * WHY ONLY `Forward`. #184 asks for Back and Forward as one parameterized
 * scenario. Forward is expressible today; Back is not, and the reason is in the
 * app rather than the gate: for Back to be the RETURN move, `/cari` has to sit
 * BEHIND the current history entry, which needs a client-side route reachable
 * from it. The shell has one gated route and no link away from it, so the only way
 * to manufacture that entry is `history.pushState` — the synthetic navigation
 * #184 excludes as evidence.
 *
 * An earlier version shipped a Back case that threw unconditionally. Codex was
 * right that this is worse than not shipping it (#196 P1-2): a test that can never
 * be green makes the five-consecutive-clean-runs series impossible by
 * construction, so it corrupts the very thing it was meant to feed. The gap is
 * recorded on #184 instead, and the case arrives when C-03 gives `/cari` a
 * successor route.
 *
 * WHAT A RUN OF THIS FILE IS NOT. Under the disposable C-01 override this proves
 * the behaviour; it is NOT the activation evidence. #184 requires an
 * activation-candidate head whose NORMAL provider publishes `cari.parties.list`
 * with `ImplementationAvailable=true`, five consecutive clean remote runs on one
 * immutable identity.
 */

import { expect, test, type Page } from '@playwright/test'
import { revokeReadGrant } from './revokeGrant.js'

const LIST_CAPABILITY = 'cari.parties.list'
/** The row the canonical fixture puts first under GDC. */
const FIXTURE_ROW = 'CAR-000001'
/** The window the production QueryClient keeps a read fresh for. */
const STALE_WINDOW_MS = 30_000

function required(name: string): string {
  const value = process.env[name]
  if (!value || !value.trim()) {
    throw new Error(
      `${name} is not set. This case mutates a real grant in a disposable database ` +
        `and has no defaults, so it can never be pointed at something else by accident.`,
    )
  }
  return value.trim()
}

interface CapabilityObservation {
  readonly status: number
  readonly generation: unknown
  readonly capabilities: unknown
  readonly readable: boolean
}

interface AuthorizedRead {
  readonly at: number
  readonly url: string
  readonly status: number
}

interface Traffic {
  /** Counted on REQUEST, not response. */
  businessRequests: string[]
  capability: CapabilityObservation[]
  /**
   * The FIRST successful authorized read, and when it was observed.
   *
   * The stale window starts here, not when the row became visible. Codex traced
   * why that distinction is load-bearing (#199 P1-2): the cache is written when
   * the response lands, so measuring from visibility silently omits the
   * request-to-paint interval. A return could therefore be judged inside the
   * 30s window while the cache it reused was already older than that -- the
   * assertion would hold and prove nothing.
   */
  authorizedRead: AuthorizedRead | null
}

/**
 * Observe both endpoints.
 *
 * Business reads are counted on the `request` event on purpose. Counting them on
 * `response` misses a read that was SENT and then aborted — so "no read after the
 * revoke" could hold while a read had in fact been attempted, which is the exact
 * shape of false green this case exists to rule out (#196 P1-5).
 */
function observe(page: Page): Traffic {
  const traffic: Traffic = { businessRequests: [], capability: [], authorizedRead: null }
  page.on('request', (request) => {
    if (request.url().includes('/api/v1/counterparties')) {
      traffic.businessRequests.push(request.url())
    }
  })
  page.on('response', (response) => {
    if (
      traffic.authorizedRead === null &&
      response.url().includes('/api/v1/counterparties') &&
      response.status() === 200
    ) {
      traffic.authorizedRead = {
        at: Date.now(),
        url: response.url(),
        status: response.status(),
      }
    }
    if (!response.url().includes('/api/v1/shell/capabilities')) return
    const status = response.status()
    void response
      .json()
      .then((body: Record<string, unknown>) => {
        traffic.capability.push({
          status,
          generation: body.generation,
          capabilities: body.capabilities,
          readable: true,
        })
      })
      .catch(() => {
        // An unreadable body is recorded as UNREADABLE, never as "no capability".
        // Treating it as absence would let a 403 or a malformed answer satisfy the
        // assertion that the grant is gone.
        traffic.capability.push({
          status,
          generation: undefined,
          capabilities: undefined,
          readable: false,
        })
      })
  })
  return traffic
}

/** Mark this document so a full reload is detectable rather than assumed. */
async function markSession(page: Page, token: string): Promise<void> {
  await page.evaluate((value) => {
    ;(window as unknown as { __sessionToken?: string }).__sessionToken = value
  }, token)
}

/**
 * Record every appearance of the fixture row from now on, at DOM-mutation
 * granularity, so a single frame is observable.
 */
async function watchForStaleRow(page: Page, row: string): Promise<void> {
  await page.evaluate((needle) => {
    const w = window as unknown as { __staleRowSeen?: boolean }
    w.__staleRowSeen = document.body.textContent?.includes(needle) ?? false
    new MutationObserver(() => {
      if (document.body.textContent?.includes(needle)) w.__staleRowSeen = true
    }).observe(document.body, { childList: true, subtree: true, characterData: true })
  }, row)
}

test('C01A-HISTORY-REVOKE-forward a revoked grant closes the route and issues no read', async ({
  page,
}) => {
  test.setTimeout(120_000)
  const target = {
    connectionString: required('ACTIVATION_E2E_SQL_CONNECTION'),
    database: required('ACTIVATION_E2E_DATABASE'),
    actorId: required('ACTIVATION_E2E_ACTOR'),
  }
  const sessionToken = `session-${Date.now()}`
  const traffic = observe(page)

  // 1. Authorized entry. ONE full load starts the session; everything after is
  //    client-side, because a second `page.goto` would reload and discard the warm
  //    cache this case exists to interrogate.
  await page.goto('/')
  await markSession(page, sessionToken)
  await page.getByRole('link', { name: 'Cari Kartlar' }).click()
  await expect(page.getByRole('row', { name: new RegExp(FIXTURE_ROW) })).toBeVisible({
    timeout: 40_000,
  })
  expect(
    traffic.businessRequests.length,
    'the first entry issued no business read',
  ).toBeGreaterThan(0)

  // The clock starts when the authorized read was SERVED, not when its row
  // appeared. Fail closed if it was never observed: an unmeasured start would
  // make every timing assertion below meaningless rather than merely weaker.
  const authorizedRead = traffic.authorizedRead
  expect(
    authorizedRead,
    'no successful authorized counterparties response was observed',
  ).not.toBeNull()
  const authorizedAt = (authorizedRead as AuthorizedRead).at

  // 2. Leave the route without reloading.
  await page.goBack()
  await expect(page.getByRole('table')).toHaveCount(0)

  // 3. Revoke server-side while the user is off the route.
  const grant = await revokeReadGrant(target)
  try {
    const businessBeforeReturn = traffic.businessRequests.length
    const capabilityBeforeReturn = traffic.capability.length

    // 4. Watch for the stale row BEFORE moving, so a one-frame flash counts.
    await watchForStaleRow(page, FIXTURE_ROW)

    // 5. Native forward, inside the window.
    await page.goForward()
    await expect(page.locator('[data-state="not-found"]')).toBeVisible({ timeout: 40_000 })
    await page.waitForTimeout(1_500)

    const returnAt = Date.now()
    const elapsed = returnAt - authorizedAt

    // Carry the measurement in the report itself, so the gate can re-check it
    // rather than take a green exit code as proof (#199 P1-2). Playwright emits
    // annotations as <property> inside the testcase, which is the one activation
    // artifact that is already allowlisted and privacy-scanned -- verified
    // against @playwright/test 1.62, not assumed.
    const read = authorizedRead as AuthorizedRead
    test.info().annotations.push(
      { type: 'activation.authorizedResponseAt', description: String(authorizedAt) },
      { type: 'activation.returnAt', description: String(returnAt) },
      { type: 'activation.elapsedMs', description: String(elapsed) },
      { type: 'activation.authorizedResponseUrl', description: read.url },
      { type: 'activation.authorizedResponseStatus', description: String(read.status) },
    )
    expect(
      elapsed,
      `the return landed ${elapsed}ms after the authorized read, outside the ` +
        `${STALE_WINDOW_MS}ms window — a refetch would be expected anyway`,
    ).toBeLessThan(STALE_WINDOW_MS)

    // Same document: measured, not assumed. A full reload would clear the token,
    // and would make every other assertion here mean something different.
    expect(
      await page.evaluate(
        () => (window as unknown as { __sessionToken?: string }).__sessionToken,
      ),
      'the SPA session was reloaded',
    ).toBe(sessionToken)

    // A fresh capability snapshot was taken, and it is a VALID one that does not
    // carry the grant. An unreadable or non-200 answer fails here rather than
    // counting as "the capability is gone".
    const fresh = traffic.capability.slice(capabilityBeforeReturn)
    expect(fresh.length, 'the return reused the cached capability snapshot').toBeGreaterThan(0)
    const last = fresh[fresh.length - 1]
    expect(last.readable, 'the fresh capability response could not be parsed').toBe(true)
    expect(last.status, 'the fresh capability response was not a 200').toBe(200)
    expect(
      typeof last.generation === 'string' && last.generation.length > 0,
      'the fresh snapshot carried no generation',
    ).toBe(true)
    expect(Array.isArray(last.capabilities), 'the snapshot carried no capability array').toBe(true)
    expect(
      (last.capabilities as string[]).includes(LIST_CAPABILITY),
      'the fresh snapshot still carried the revoked capability',
    ).toBe(false)

    // No business read was ATTEMPTED after the revoke.
    expect(
      traffic.businessRequests.length - businessBeforeReturn,
      'a counterparty read was issued after the grant was revoked',
    ).toBe(0)

    // And the old row never appeared, not even for one frame.
    expect(
      await page.evaluate(
        () => (window as unknown as { __staleRowSeen?: boolean }).__staleRowSeen === true,
      ),
      'the previous entry row was painted while the return was unauthorized',
    ).toBe(false)
    expect(await page.locator('body').innerText()).not.toContain(FIXTURE_ROW)
  } finally {
    await grant.restore()
  }
})
