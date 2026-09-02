/**
 * C-01A activation evidence — `success → leave → server-side revoke → Back/Forward`.
 *
 * This is the one functional case #184 §2.1 leaves open for C-01A, and it is
 * written to that contract rather than to what is convenient:
 *
 *  - the history move is NATIVE (`page.goBack()` / `page.goForward()`), because a
 *    synthetic router navigation proves nothing about a real back/forward and is
 *    explicitly excluded as evidence;
 *  - the two directions are one parameterized scenario, each in its own run;
 *  - both stay inside the 30 second cache window, so the return is served from a
 *    cache that is still fresh — expiry-then-refetch is not the case under test;
 *  - the grant is revoked SERVER-SIDE, in `[identity].[RolePermissions]`. Intercepting
 *    `/shell/capabilities` in the browser would test the interceptor, not the
 *    server;
 *  - a `MutationObserver` is installed BEFORE the history move. A final-state
 *    assertion cannot see a row that appeared for one frame and vanished, and one
 *    frame of another company's data is the whole thing being ruled out.
 *
 * WHAT A RUN OF THIS FILE IS NOT. Under the disposable C-01 override this proves
 * the behaviour; it is NOT the activation evidence. #184 requires the scenario to
 * run on an activation-candidate head whose NORMAL provider publishes
 * `cari.parties.list` with `ImplementationAvailable=true`, five consecutive clean
 * remote runs on one immutable identity. That head does not exist yet.
 */

import { expect, test, type Page } from '@playwright/test'
import sql from 'mssql'

const READ_PERMISSION = 'counterparty.parties.read'
const LIST_CAPABILITY = 'cari.parties.list'
/** The row the canonical fixture puts first under GDC. */
const FIXTURE_ROW = 'CAR-000001'

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

interface RevokedGrant {
  readonly restore: () => Promise<void>
  readonly rowsRemoved: number
}

/**
 * Remove the read grant for real, and hand back the undo.
 *
 * `[identity]` is bracketed because `identity` is a reserved word in T-SQL — the
 * unbracketed form fails with "Incorrect syntax near the keyword 'identity'".
 *
 * Scoped to `[identity].[RolePermissions]` on purpose: the workflow verifies after the
 * browser run that the Counterparty fixture, `AuditEvent` and `CommandExecution`
 * are untouched, and this touches none of them.
 */
async function revokeReadGrant(connectionString: string): Promise<RevokedGrant> {
  const pool = await new sql.ConnectionPool(connectionString).connect()
  const removed = await pool
    .request()
    .input('code', sql.NVarChar, READ_PERMISSION)
    .query(
      'SELECT RoleId, PermissionCode FROM [identity].[RolePermissions] WHERE PermissionCode = @code',
    )
  const rows = removed.recordset as { RoleId: string; PermissionCode: string }[]
  if (rows.length === 0) {
    await pool.close()
    throw new Error(
      `no ${READ_PERMISSION} grant found to revoke — the fixture is not what this case assumes`,
    )
  }
  await pool
    .request()
    .input('code', sql.NVarChar, READ_PERMISSION)
    .query('DELETE FROM [identity].[RolePermissions] WHERE PermissionCode = @code')

  return {
    rowsRemoved: rows.length,
    restore: async () => {
      for (const row of rows) {
        await pool
          .request()
          .input('roleId', sql.UniqueIdentifier, row.RoleId)
          .input('code', sql.NVarChar, row.PermissionCode)
          .query(
            'IF NOT EXISTS (SELECT 1 FROM [identity].[RolePermissions] WHERE RoleId = @roleId AND PermissionCode = @code) ' +
              'INSERT INTO [identity].[RolePermissions] (RoleId, PermissionCode) VALUES (@roleId, @code)',
          )
      }
      await pool.close()
    },
  }
}

interface Counters {
  capability: number
  business: number
  capabilityBodies: string[]
}

function countRequests(page: Page): Counters {
  const counters: Counters = { capability: 0, business: 0, capabilityBodies: [] }
  page.on('response', (response) => {
    const url = response.url()
    if (url.includes('/api/v1/shell/capabilities')) {
      counters.capability += 1
      void response
        .text()
        .then((body) => counters.capabilityBodies.push(body))
        .catch(() => {
          /* a body we cannot read is recorded as absent, never as containing the grant */
        })
    }
    if (url.includes('/api/v1/counterparties')) counters.business += 1
  })
  return counters
}

/**
 * Record every appearance of the fixture row from now on, at DOM-mutation
 * granularity, so a single frame is observable.
 */
async function watchForStaleRow(page: Page, row: string): Promise<void> {
  await page.evaluate((needle) => {
    const w = window as unknown as { __staleRowSeen?: boolean }
    w.__staleRowSeen = document.body.textContent?.includes(needle) ?? false
    const observer = new MutationObserver(() => {
      if (document.body.textContent?.includes(needle)) w.__staleRowSeen = true
    })
    observer.observe(document.body, { childList: true, subtree: true, characterData: true })
  }, row)
}

async function staleRowWasSeen(page: Page): Promise<boolean> {
  return page.evaluate(
    () => (window as unknown as { __staleRowSeen?: boolean }).__staleRowSeen === true,
  )
}

/**
 * Return to `/cari` with a NATIVE history move.
 *
 * `forward` is fully expressible: the session goes `/` → `/cari` → back to `/`,
 * and the return is a real `goForward()`.
 *
 * `back` is NOT, and this says so instead of faking it. For Back to be the return
 * move, `/cari` has to sit BEHIND the current entry — which needs a second
 * client-side route reachable from it. The shell has exactly one gated route and
 * no link away from it, so no such entry can be created without
 * `history.pushState`, and a synthetic entry is precisely what #184 excludes.
 *
 * This is a gap in the app's reachable routes, not in the gate. When C-03 is
 * granted, a row link makes `/cari → /cari/:id → Back` a genuine sequence and this
 * helper becomes a two-line change.
 */
async function returnToRoute(page: Page, direction: 'back' | 'forward'): Promise<void> {
  if (direction === 'forward') {
    await page.goForward()
    return
  }
  throw new Error(
    'C01A-HISTORY-REVOKE-back cannot be produced yet: returning to /cari with Back ' +
      'requires a client-side route AFTER it, and the shell has none. Do not satisfy ' +
      'this with history.pushState — #184 excludes synthetic navigation as evidence.',
  )
}

for (const direction of ['back', 'forward'] as const) {
  test(`C01A-HISTORY-REVOKE-${direction} a revoked grant closes the route and issues no read`, async ({
    page,
  }) => {
    test.setTimeout(120_000)
    const connectionString = required('ACTIVATION_E2E_SQL_CONNECTION')

    const counters = countRequests(page)

    // 1. Authorized entry. The session starts with ONE full load, then everything
    //    else is client-side — a second `page.goto` would reload and discard the
    //    warm cache this case exists to interrogate.
    await page.goto('/')
    await page.getByRole('link', { name: 'Cari Kartlar' }).click()
    await expect(page.getByRole('row', { name: new RegExp(FIXTURE_ROW) })).toBeVisible({
      timeout: 40_000,
    })
    expect(counters.business, 'the first entry issued no business read').toBeGreaterThan(0)

    // 2. Leave the route WITHOUT reloading. `page.goto` would be a full load and
    //    would destroy the SPA session and its warm cache — which is the whole
    //    condition under test, so it cannot be used to get off the route.
    await page.goBack()
    await expect(page.getByRole('table')).toHaveCount(0)

    // 3. Revoke server-side while the user is off the route.
    const grant = await revokeReadGrant(connectionString)
    try {
      const businessBeforeReturn = counters.business
      const capabilityBeforeReturn = counters.capability

      // 4. Watch for the stale row BEFORE moving, so a one-frame flash counts.
      await watchForStaleRow(page, FIXTURE_ROW)

      // 5. Native history move, inside the 30s window.
      await returnToRoute(page, direction)

      // 6. The route closes, non-disclosingly.
      await expect(page.locator('[data-state="not-found"]')).toBeVisible({ timeout: 40_000 })
      await page.waitForTimeout(2_000)

      // A fresh capability snapshot was taken, and it does not carry the grant.
      expect(
        counters.capability,
        'the return reused the cached capability snapshot instead of asking again',
      ).toBeGreaterThan(capabilityBeforeReturn)
      const latest = counters.capabilityBodies[counters.capabilityBodies.length - 1] ?? ''
      expect(
        latest.includes(LIST_CAPABILITY),
        'the fresh snapshot still carried the revoked capability',
      ).toBe(false)

      // No business read was attempted after the revoke. "Did not display it" and
      // "did not ask for it" are different guarantees; this is the second.
      expect(
        counters.business - businessBeforeReturn,
        'a counterparty read was issued after the grant was revoked',
      ).toBe(0)

      // And the old row never appeared, not even for one frame.
      expect(
        await staleRowWasSeen(page),
        'the previous entry’s row was painted while the return was unauthorized',
      ).toBe(false)
      expect(await page.locator('body').innerText()).not.toContain(FIXTURE_ROW)
    } finally {
      await grant.restore()
    }
  })
}
