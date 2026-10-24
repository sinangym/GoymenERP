/**
 * O-04 — the operator of an external location on the location card (O02), against the real server.
 *
 * SOURCE CONTRACT: PR #795 `GET /organization/locations/{id}/operator` (organization.locations.read) and the card's
 * `LocationOperatorSection`. This spec only passes once #795 is on the branch under test; before that the read is the
 * catch-all 404 and the first case fails on its status, which is the honest signal.
 *
 * MEASURED: the seeded operator actor (read + status, no `organization.locations.write`) sees the operator section on
 * an external location, with the server's own answer, and is offered no change control and sends no candidate search
 * and no write; an internal location has no section and sends no operator read; the card does not scroll sideways at
 * 320 px.
 *
 * NOT MEASURED (BLOCKED, not PASS): the assignment journey. No seeded role carries `organization.locations.write` and
 * no seeded Cari party is a supplier operator candidate, so the write path is covered by the component cases only
 * (`src/organization/LocationOperator.test.tsx`). Next step: a manifest actor with the write grant and a synthetic
 * supplier party in the fixture — owner: the Organization fixture/seed owner.
 *
 * NOTHING IS MOCKED: no interception at all; the default development identity is the seeded operator actor.
 */
import { expect, test } from '@playwright/test'
import { ORG_LIST_PATH, cardHeading, locationCardPath, pageItems } from '../src/journey.js'
import { DETAIL_RE, LOCATIONS_RE, LiveRecorder, shot } from '../src/recorder.js'

const GROUP = 'o-04-location-operator'
/** Seeded external site `SITE-EXT`; every location code there starts with this prefix (organization-seed.json). */
const EXTERNAL_PREFIX = 'EXT-'
const INTERNAL_CODE = 'OSM-H0'
const OPERATOR_RE = /^\/api\/v1\/organization\/locations\/[0-9a-fA-F-]+\/operator$/
const CANDIDATES_RE = /^\/api\/v1\/organization\/locations\/[0-9a-fA-F-]+\/operator-candidates$/

async function seededLocations(net: LiveRecorder) {
  const read = await net.waitForNth('the location page read', LOCATIONS_RE, 'GET', 0)
  expect(read.status).toBe(200)
  return pageItems(read.responseBody)
}

test.describe('O-04 location operator', () => {
  test('an external location shows the server operator to a reader, with no change control and no write', async ({
    page,
  }) => {
    const net = LiveRecorder.attach(page)
    await page.goto(ORG_LIST_PATH)
    const external = (await seededLocations(net)).find((row) => row.code.startsWith(EXTERNAL_PREFIX))
    expect(external, 'the seed must hold an external location').toBeTruthy()

    await page.goto(locationCardPath(external!.id))
    const detail = await net.waitForNth('the external card read', DETAIL_RE, 'GET', 0)
    expect(detail.status).toBe(200)
    const operator = await net.waitForNth('the operator read', OPERATOR_RE, 'GET', 0)
    expect(operator.status).toBe(200)
    const body = operator.responseBody as { locationId: string; operator: null | { code: string } }
    expect(body.locationId.toLowerCase()).toBe(external!.id.toLowerCase())

    const region = page.getByRole('region', { name: 'İşletici' })
    await expect(cardHeading(page)).toBeVisible()
    await expect(region).toBeVisible()
    if (body.operator === null) {
      await expect(region.getByText('Atanmış işletici yok.')).toBeVisible()
    } else {
      await expect(region.getByText(body.operator.code, { exact: true })).toBeVisible()
    }
    // A reader is offered nothing to change, and not showing it is not the guarantee: nothing was sent.
    await expect(region.getByRole('button')).toHaveCount(0)
    expect(net.issued(CANDIDATES_RE)).toHaveLength(0)
    expect(net.issued(OPERATOR_RE, 'PUT')).toHaveLength(0)

    await page.setViewportSize({ width: 320, height: 800 })
    await expect
      .poll(() => page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth))
      .toBeLessThanOrEqual(0)

    await shot(page, GROUP, '01-external-operator-reader')
    await net.write(GROUP, 'external-reader.json')
  })

  test('an internal location has no operator section and sends no operator read', async ({ page }) => {
    const net = LiveRecorder.attach(page)
    await page.goto(ORG_LIST_PATH)
    const internal = (await seededLocations(net)).find((row) => row.code === INTERNAL_CODE)
    expect(internal, `${INTERNAL_CODE} must be seeded`).toBeTruthy()

    await page.goto(locationCardPath(internal!.id))
    const detail = await net.waitForNth('the internal card read', DETAIL_RE, 'GET', 0)
    expect(detail.status).toBe(200)
    await expect(cardHeading(page)).toBeVisible()
    await expect(page.getByRole('region', { name: 'İşletici' })).toHaveCount(0)
    expect(net.issued(OPERATOR_RE)).toHaveLength(0)

    await net.write(GROUP, 'internal.json')
  })
})
