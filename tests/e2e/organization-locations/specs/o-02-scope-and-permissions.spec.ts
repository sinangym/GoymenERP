/**
 * O-02 — what each actor may see and do, measured against the real server.
 *
 * SOURCE CONTRACT: the merged endpoints' `RequireAuthorization` attributes, the seeded
 * roles in `identity-seed.json` (`ORGANIZATION_READER` / `ORGANIZATION_OPERATOR`), the
 * #246 capability mapping and the fixture manifest's actor table.
 *
 * WHY THE BROWSER ADDS SOMETHING HERE. The component tests can prove the screen does not
 * RENDER a write surface. They cannot prove the server refuses one, nor that the shell's
 * capability snapshot and the server's permissions agree. Both are measured here — and
 * the distinction matters, because "did not display it" and "cannot do it" are different
 * guarantees and only the second is authorization.
 *
 * EVERY ASSERTION WAITS FOR THE EXCHANGE. A heading renders before its data arrives, so
 * each case polls for the COMPLETED record it is about.
 *
 * NOTHING IS MOCKED. The only interception rewrites ONE request header — the development
 * identity — to run the same real application as another real seeded actor. Every status
 * and body is the server's own; there is no `route.fulfill` here.
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
import {
  DETAIL_RE,
  LOCATIONS_RE,
  LiveRecorder,
  SITES_RE,
  attachEvidence,
  shot,
  writeEvidence,
} from '../src/recorder.js'
import { ORG_UI_PORT, readOrganizationManifest } from '../src/manifest.js'

const manifest = readOrganizationManifest()
const GROUP = 'o-02-scope-and-permissions'
/** The one verified origin this lane serves. Contexts created by hand need it explicitly. */
const BASE_URL = `http://127.0.0.1:${ORG_UI_PORT}`
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

let active: LiveRecorder | null = null

test.describe('O-02 scope and permissions', () => {
  test('the site-scoped viewer sees only its own facility and is offered no write surface', async ({
    page,
    context,
  }) => {
    await asActor(context, manifest.viewerActor)
    const net = LiveRecorder.attach(page)
    active = net

    await page.goto(ORG_LIST_PATH)

    const sites = await net.waitForNth('the viewer facility read', SITES_RE, 'GET', 0)
    expect(sites.status).toBe(200)
    expect((sites.responseBody as Array<{ code: string }>).map((s) => s.code)).toEqual([
      VIEWER_SITE_CODE,
    ])

    const page1 = await net.waitForNth('the viewer location page read', LOCATIONS_RE, 'GET', 0)
    expect(page1.status).toBe(200)
    const locations = pageItems(page1.responseBody)
    expect(locations).toHaveLength(VIEWER_LOCATION_COUNT)
    expect(locations.some((l) => l.code === HIDDEN_CODE)).toBe(false)

    await expect(listHeading(page)).toBeVisible()

    // The card opens; the reasoned-change control does not exist for a reader.
    await locationLink(page, locations[0].code).click()
    const detail = await net.waitForNth('the viewer card detail read', DETAIL_RE, 'GET', 0)
    expect(detail.status).toBe(200)
    await expect(cardHeading(page)).toBeVisible()
    await expect(actionTrigger(page)).toHaveCount(0)
    await expect(page.getByLabel('Gerekçe')).toHaveCount(0)
    // Not showing it is not the guarantee; not sending it is.
    expect(net.issued(/active-state$/, 'PUT')).toHaveLength(0)

    await shot(page, GROUP, '01-viewer-card')
    await net.write(GROUP, 'viewer.json')
  })

  test('a location outside the viewer scope is non-disclosing on a direct link', async ({
    page,
    context,
    browser,
  }) => {
    /**
     * The deep link is the interesting case: the row was never rendered for the viewer,
     * so only a direct navigation can reach it. A 404 that leaked the id would tell the
     * viewer the record exists — the disclosure the non-disclosure rule forbids.
     *
     * The id is looked up in a SEPARATE operator context so the viewer interception
     * cannot touch it. That context is created by hand and therefore does NOT inherit
     * the suite's `use.baseURL`; it is given the verified origin explicitly, because a
     * relative navigation there would otherwise have nothing to resolve against.
     */
    const operatorContext = await browser.newContext({ baseURL: BASE_URL })
    const lookup = await operatorContext.newPage()
    const lookupNet = LiveRecorder.attach(lookup)
    await lookup.goto(ORG_LIST_PATH)
    const operatorPage = await lookupNet.waitForNth(
      'the operator location page read',
      LOCATIONS_RE,
      'GET',
      0,
    )
    expect(operatorPage.status).toBe(200)
    const hidden = pageItems(operatorPage.responseBody).find((row) => row.code === HIDDEN_CODE)
    expect(hidden, `${HIDDEN_CODE} must be visible to the operator`).toBeTruthy()
    await operatorContext.close()

    await asActor(context, manifest.viewerActor)
    const net = LiveRecorder.attach(page)
    active = net
    await page.goto(locationCardPath(hidden!.id))

    const detail = await net.waitForNth('the viewer deep-link detail read', DETAIL_RE, 'GET', 0)
    expect(detail.status).toBe(404)
    expect(JSON.stringify(detail.responseBody ?? {})).not.toContain(hidden!.id)
    expect(net.issued(/active-state$/, 'PUT')).toHaveLength(0)

    await shot(page, GROUP, '02-hidden-deep-link')
    await net.write(GROUP, 'hidden-deep-link.json')
  })

  test('an actor in the other company reads nothing of company A', async ({ page, context }) => {
    await asActor(context, manifest.foreignActor)
    const net = LiveRecorder.attach(page)
    active = net

    await page.goto(ORG_LIST_PATH)
    const read = await net.waitForNth('the foreign location page read', LOCATIONS_RE, 'GET', 0)
    expect(read.status).toBe(200)
    // The fixture seeds no topology for the second company.
    expect(pageItems(read.responseBody)).toHaveLength(0)
    await expect(listHeading(page)).toBeVisible()
    expect(net.issued(/active-state$/, 'PUT')).toHaveLength(0)

    await shot(page, GROUP, '03-foreign-company')
    await net.write(GROUP, 'foreign.json')
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
     * The environment owner has PREPARED a locations-only payload, but it is not loaded
     * (`runtimeLoaded=false`) and this case runs against its own reduced-permission seed
     * in a fresh owned database. Prepared input is not an observation: until a manifest
     * binds that actor, this case SKIPS and records that it was not measured. It is never
     * marked observed from a source hash.
     */
    test.skip(
      manifest.locationsOnlyActor === undefined,
      'no locations-only actor is bound in this manifest; the payload may be prepared but this run did not load it',
    )
    await asActor(context, manifest.locationsOnlyActor!)
    const net = LiveRecorder.attach(page)
    active = net

    await page.goto(ORG_LIST_PATH)
    const read = await net.waitForNth('the locations-only page read', LOCATIONS_RE, 'GET', 0)
    expect(read.status).toBe(200)
    // The entitled half of the screen works...
    expect(pageItems(read.responseBody).length).toBeGreaterThan(0)
    await expect(listHeading(page)).toBeVisible()
    // ...and the facility read was never attempted, not merely discarded.
    expect(net.issuedSiteReads()).toHaveLength(0)

    await shot(page, GROUP, '04-locations-only')
    await net.write(GROUP, 'locations-only.json')
  })

  test.afterEach(async ({}, testInfo) => {
    if (active !== null && testInfo.status !== testInfo.expectedStatus) {
      await attachEvidence(testInfo, active, GROUP)
    }
    active = null
  })

  test.afterAll(async () => {
    writeEvidence(GROUP, 'coverage.json', {
      locationsOnlyActorBound: manifest.locationsOnlyActor !== undefined,
      note:
        manifest.locationsOnlyActor === undefined
          ? 'The sites-capability-absent case was SKIPPED: no locations-only actor is bound in this manifest. A prepared payload that was never loaded is not an observation, and this run does not cover that case.'
          : 'The sites-capability-absent case ran against a bound locations-only actor in its own reduced-permission seed.',
    })
  })
})
