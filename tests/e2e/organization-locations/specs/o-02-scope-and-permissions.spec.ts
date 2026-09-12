/**
 * O-02 — what each actor may see and do, measured against the real server.
 *
 * SOURCE CONTRACT: the merged endpoints' `RequireAuthorization` attributes, the seeded
 * roles in `identity-seed.json` (`ORGANIZATION_READER` / `ORGANIZATION_OPERATOR`), the
 * #246 capability mapping and the fixture manifest's actor table.
 *
 * WHY THE BROWSER ADDS SOMETHING HERE. The component tests can prove the screen does
 * not RENDER a write surface. They cannot prove the server refuses one, nor that the
 * shell's capability snapshot and the server's permissions agree. Both of those are
 * measured here — and the distinction matters, because "did not display it" and "cannot
 * do it" are different guarantees and only the second is authorization.
 *
 * NOTHING IS MOCKED. The only interception rewrites ONE request header — the
 * development identity — to run the same real application as another real seeded actor.
 * Every status and body is the server's own; there is no `route.fulfill` here.
 *
 * NOT MEASURED: identity-provider revocation (the Development bootstrap host
 * authenticates by header, so a real revocation cannot be produced), and database rows.
 */
import { expect, test, type BrowserContext } from '@playwright/test'
import {
  ORG_LIST_PATH,
  actionTrigger,
  cardHeading,
  listHeading,
  locationCardPath,
  locationLink,
  pageItems,
} from '../src/journey.js'
import { LiveRecorder, shot, writeEvidence } from '../src/recorder.js'
import { readOrganizationManifest } from '../src/manifest.js'

const manifest = readOrganizationManifest()
const GROUP = 'o-02-scope-and-permissions'
/** Fixture expectations: the viewer is scoped to SITE-OSM, which holds these eight. */
const VIEWER_SITE_CODE = 'SITE-OSM'
const VIEWER_LOCATION_COUNT = 8
/** In SITE-GAZ, i.e. outside the viewer's scope. */
const HIDDEN_CODE = 'GAZ-DEP'

/** Runs the real app as another seeded actor by rewriting only the identity header. */
async function asActor(context: BrowserContext, actor: string): Promise<void> {
  await context.route('**/api/v1/**', async (route) => {
    await route.continue({
      headers: { ...route.request().headers(), 'x-development-user': actor },
    })
  })
}

test.describe('O-02 scope and permissions', () => {
  test('the site-scoped viewer sees only its own facility and is offered no write surface', async ({
    page,
    context,
  }) => {
    await asActor(context, manifest.viewerActor)
    const net = await LiveRecorder.attach(page)

    await page.goto(ORG_LIST_PATH)
    await expect(listHeading(page)).toBeVisible()

    const sites = net.siteReads()
    expect(sites.length).toBeGreaterThan(0)
    expect(sites[0].status).toBe(200)
    const siteBodies = sites.at(-1)!.responseBody as Array<{ code: string }>
    expect(siteBodies.map((s) => s.code)).toEqual([VIEWER_SITE_CODE])

    const locations = pageItems(net.locationPageReads().at(-1)!.responseBody)
    expect(locations).toHaveLength(VIEWER_LOCATION_COUNT)
    expect(locations.some((l) => l.code === HIDDEN_CODE)).toBe(false)

    // The card opens; the reasoned-change control does not exist for a reader.
    await locationLink(page, locations[0].code).click()
    await expect(cardHeading(page)).toBeVisible()
    await expect(actionTrigger(page)).toHaveCount(0)
    await expect(page.getByLabel('Gerekçe')).toHaveCount(0)
    // Not showing it is not the guarantee; not sending it is.
    expect(net.activeStatePuts()).toHaveLength(0)

    await shot(page, GROUP, '01-viewer-card')
    net.write(GROUP, 'viewer.json')
  })

  test('a location outside the viewer scope is non-disclosing on a direct link', async ({
    page,
    context,
  }) => {
    /**
     * The deep link is the interesting case: the row was never rendered, so only a
     * direct navigation can reach it. A 404 that leaks the id would tell the viewer the
     * record exists — which is the disclosure the non-disclosure rule forbids.
     */
    const operator = await context.browser()!.newContext()
    const lookup = await operator.newPage()
    const lookupNet = await LiveRecorder.attach(lookup)
    await lookup.goto(ORG_LIST_PATH)
    await expect(listHeading(lookup)).toBeVisible()
    const hidden = pageItems(lookupNet.locationPageReads().at(-1)!.responseBody).find(
      (row) => row.code === HIDDEN_CODE,
    )
    expect(hidden, `${HIDDEN_CODE} must exist for the operator`).toBeTruthy()
    await operator.close()

    await asActor(context, manifest.viewerActor)
    const net = await LiveRecorder.attach(page)
    await page.goto(locationCardPath(hidden!.id))

    const detail = net.locationDetailReads()
    expect(detail.length).toBeGreaterThan(0)
    expect(detail.at(-1)!.status).toBe(404)
    const body = JSON.stringify(detail.at(-1)!.responseBody ?? {})
    expect(body).not.toContain(hidden!.id)
    expect(net.activeStatePuts()).toHaveLength(0)

    await shot(page, GROUP, '02-hidden-deep-link')
    net.write(GROUP, 'hidden-deep-link.json')
  })

  test('an actor in the other company reads nothing of company A', async ({ page, context }) => {
    await asActor(context, manifest.foreignActor)
    const net = await LiveRecorder.attach(page)

    await page.goto(ORG_LIST_PATH)
    await expect(listHeading(page)).toBeVisible()

    const locations = pageItems(net.locationPageReads().at(-1)!.responseBody)
    // The fixture seeds no topology for the second company.
    expect(locations).toHaveLength(0)
    expect(net.activeStatePuts()).toHaveLength(0)

    await shot(page, GROUP, '03-foreign-company')
    net.write(GROUP, 'foreign.json')
  })

  test('without the optional sites capability the screen issues ZERO /sites requests', async ({
    page,
    context,
  }) => {
    /**
     * The #246 mapping makes `organization.sites.read` an OPTIONAL control capability:
     * the ORG-01 route stands on `organization.locations.read` alone, and when the sites
     * capability is absent the request is not issued at all.
     *
     * The fixture package marks this actor variant NOT_PREPARED (PR #244 `a2a3cfc8`), and
     * all three prepared actors hold the sites permission — so this case SKIPS rather
     * than pretending. It is recorded as not-measured; it is never reported as covered
     * by this seed, nor by the SQL test's permission reduction.
     */
    test.skip(
      manifest.locationsOnlyActor === undefined,
      'locations-only actor variant is NOT_PREPARED in the fixture package; this case is not measured',
    )
    await asActor(context, manifest.locationsOnlyActor!)
    const net = await LiveRecorder.attach(page)

    await page.goto(ORG_LIST_PATH)
    await expect(listHeading(page)).toBeVisible()

    // The entitled half of the screen works...
    const locations = pageItems(net.locationPageReads().at(-1)!.responseBody)
    expect(locations.length).toBeGreaterThan(0)
    // ...and the facility read was never attempted.
    expect(net.siteReads()).toHaveLength(0)

    await shot(page, GROUP, '04-locations-only')
    net.write(GROUP, 'locations-only.json')
  })

  test.afterAll(async () => {
    writeEvidence(GROUP, 'coverage.json', {
      locationsOnlyActorPrepared: manifest.locationsOnlyActor !== undefined,
      note:
        manifest.locationsOnlyActor === undefined
          ? 'The sites-capability-absent case was SKIPPED: the fixture package marks that actor variant NOT_PREPARED. It is not covered by this run.'
          : 'The sites-capability-absent case ran against a prepared locations-only actor.',
    })
  })
})
