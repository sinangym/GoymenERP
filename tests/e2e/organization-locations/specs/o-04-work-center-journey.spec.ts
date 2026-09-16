/**
 * O-04 — ORG-03: the authorized work-center list → a row's location card → back.
 *
 * SOURCE CONTRACT: Business's ORG-03 browser JIT (#66 5698360588, items 1–8), with
 * item 4 as changed by the row-scope ruling (#66 5700319744); the merged endpoint
 * `GET /api/v1/organization/work-centers` and its seven-field `WorkCenterSummary`
 * (`canOpenLocationDetail` included); `OrganizationShellCapabilityProvider` 1.1; the
 * frontend screen `src/organization/WorkCenterListPage.tsx`; the shell's sidebar entry.
 *
 * WHAT ONLY THE REAL STACK SHOWS HERE. The component tests already prove what the screen
 * does with a given answer. They cannot show that the SERVER's row flag and the SERVER's
 * card read agree — that a row which links is a card that answers 200 and a row which
 * does not link is one the browser never asks for — nor that the shell's sidebar entry
 * reaches the real route under the real capability snapshot.
 *
 * NOTHING IS MOCKED. There is no `route.fulfill` in this file. Two interceptions exist
 * and both leave every status and body to the server: the Development identity header
 * rewrite (the o-02 pattern), and in the company-switch case a HOLD that delays the
 * browser's own request before `route.continue()` sends it on unchanged.
 *
 * ACTORS COME FROM THE MANIFEST. Items 3 and 4 need subjects the normal Development
 * seed does not have; those cases SKIP when the manifest binds none and `coverage.json`
 * records that they were not measured. This package adds no actor, role or grant.
 *
 * NOT MEASURED HERE: database rows (the SQL cases own that), audit contents, and — for
 * item 6 — a real 403 or provider failure on this read. Neither can be produced from a
 * browser without a mock or without breaking the environment, so `coverage.json` says
 * so instead of claiming them.
 */
import { expect, test, type BrowserContext, type Page, type Route } from '@playwright/test'
import {
  WORK_CENTERS_PATH,
  anyWorkCenterLocationLink,
  cardBackLink,
  cardHeading,
  tabTo,
  workCenterLocationLink,
  workCenterRows,
  workCentersHeading,
  workCentersNavLink,
  type WorkCenterRow,
} from '../src/journey.js'
import {
  COMPANIES_RE,
  DETAIL_RE,
  LiveRecorder,
  WORK_CENTERS_RE,
  attachEvidence,
  shot,
  writeEvidence,
  type LiveRecord,
} from '../src/recorder.js'
import { readOrganizationManifest } from '../src/manifest.js'

const manifest = readOrganizationManifest()
const GROUP = 'o-04-work-center-journey'
/** The screen's own label for the return target, as the card's breadcrumb shows it. */
const BACK_LABEL = 'İş Merkezleri'
const WRITE_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE'])

/** Runs the real app as another seeded actor by rewriting only the identity header. */
async function asActor(context: BrowserContext, actor: string): Promise<void> {
  await context.route('**/api/v1/**', async (route) => {
    await route.continue({
      headers: { ...route.request().headers(), 'x-development-user': actor },
    })
  })
}

function detailPathOf(locationId: string): RegExp {
  return new RegExp(`^/api/v1/organization/locations/${locationId}$`)
}

/** Item 7, from the wire: the whole case issued reads only. */
function expectReadOnly(net: LiveRecorder): void {
  const writes = net.records.filter((record) => WRITE_METHODS.has(record.method))
  expect(writes, 'the ORG-03 journey must issue no write of any kind').toEqual([])
}

async function listFromSidebar(page: Page, net: LiveRecorder): Promise<LiveRecord> {
  // Item 1: through the real shell entry, not a typed URL.
  await page.goto('/')
  const entry = workCentersNavLink(page)
  await expect(entry).toBeVisible()
  // Item 8: the accessible name IS the visible text.
  expect((await entry.textContent())?.trim()).toBe('İş Merkezleri')
  await entry.click()
  await expect(page).toHaveURL(new RegExp(`${WORK_CENTERS_PATH}$`))
  const read = await net.waitForNth('the work-center list read', WORK_CENTERS_RE, 'GET', 0)
  await expect(workCentersHeading(page)).toBeVisible()
  return read
}

/** What `coverage.json` records about each item, written once at the end. */
const coverage: Record<string, string> = {}

let active: LiveRecorder | null = null

test.describe('O-04 work centers (ORG-03)', () => {
  test('sidebar → list → a row’s card by keyboard → back to the list, reading only', async ({ page }) => {
    const net = LiveRecorder.attach(page)
    active = net

    const read = await listFromSidebar(page, net)
    expect(read.status).toBe(200)
    expect(read.requestHeaders['x-company-id']).toBeTruthy()
    const rows = workCenterRows(read.responseBody)
    expect(rows.length).toBeGreaterThan(0)
    for (const row of rows) {
      // The seven declared fields, and nothing standing in for scope lists.
      expect(Object.keys(row).sort()).toEqual(
        ['canOpenLocationDetail', 'code', 'id', 'isActive', 'kind', 'locationId', 'name'],
      )
      await expect(page.getByRole('row').filter({ hasText: row.code })).toHaveCount(1)
    }

    // Item 2: a row the server marked openable, reached and followed by keyboard alone.
    const openable = rows.find((row) => row.canOpenLocationDetail)
    expect(openable, 'the operator must be able to open at least one row’s card').toBeTruthy()
    const link = workCenterLocationLink(page, openable!.code)
    await expect(link).toBeVisible()
    // Item 8: the row link's name starts with its visible text.
    const visible = (await link.textContent())?.trim() ?? ''
    expect((await link.getAttribute('aria-label'))?.startsWith(visible)).toBe(true)
    await shot(page, GROUP, '01-list')

    await tabTo(page, link)
    await page.keyboard.press('Enter')
    const detail = await net.waitFor('the card read for the followed row', (records) =>
      records.find(
        (r) => r.complete && r.status !== undefined && r.method === 'GET' && detailPathOf(openable!.locationId).test(r.path),
      ),
    )
    // The row's flag and the card read agree: a link is a card that answers.
    expect(detail.status).toBe(200)
    await expect(cardHeading(page)).toBeVisible()
    await shot(page, GROUP, '02-card')

    // Back, by keyboard, to THIS list — not to another Organization view.
    const back = cardBackLink(page, BACK_LABEL)
    await expect(back).toBeVisible()
    await tabTo(page, back)
    await page.keyboard.press('Enter')
    await expect(page).toHaveURL(new RegExp(`${WORK_CENTERS_PATH}$`))
    await expect(workCentersHeading(page)).toBeVisible()
    await shot(page, GROUP, '03-back-on-list')

    // Every row without the flag offered no link, and none of their cards was asked for.
    for (const row of rows.filter((candidate) => !candidate.canOpenLocationDetail)) {
      await expect(workCenterLocationLink(page, row.code)).toHaveCount(0)
      expect(net.issued(detailPathOf(row.locationId), 'GET')).toHaveLength(0)
    }
    expectReadOnly(net)
    coverage.items1_2_7_8 = 'MEASURED with the operator actor'
    await net.write(GROUP, 'operator-journey.json')
  })

  test('without the card capability the list works, offers no link and asks no card', async ({
    page,
    context,
  }) => {
    test.skip(
      manifest.workCentersOnlyActor === undefined,
      'no work-centers-only actor is bound in this manifest; the normal seed has none',
    )
    await asActor(context, manifest.workCentersOnlyActor!)
    const net = LiveRecorder.attach(page)
    active = net

    const read = await listFromSidebar(page, net)
    expect(read.status).toBe(200)
    const rows = workCenterRows(read.responseBody)
    expect(rows.length).toBeGreaterThan(0)
    // The server agrees with the capability: no row may open a card.
    expect(rows.every((row) => row.canOpenLocationDetail === false)).toBe(true)
    await expect(anyWorkCenterLocationLink(page)).toHaveCount(0)
    // Item 3: the card read is never ATTEMPTED — counted over issued, not answered.
    expect(net.issued(DETAIL_RE, 'GET')).toHaveLength(0)
    expectReadOnly(net)
    coverage.item3 = 'MEASURED with the bound work-centers-only actor'
    await shot(page, GROUP, '04-work-centers-only')
    await net.write(GROUP, 'work-centers-only.json')
  })

  test('with the card capability, an out-of-scope row offers no link and its card is never asked', async ({
    page,
    context,
  }) => {
    test.skip(
      manifest.rowScopedActor === undefined,
      'no row-scoped actor (work centers on two locations, cards on one) is bound in this manifest; the normal seed has none',
    )
    await asActor(context, manifest.rowScopedActor!)
    const net = LiveRecorder.attach(page)
    active = net

    const read = await listFromSidebar(page, net)
    expect(read.status).toBe(200)
    const rows = workCenterRows(read.responseBody)
    // The case only means something when the server answered BOTH ways in one list.
    const inScope = rows.filter((row) => row.canOpenLocationDetail)
    const outOfScope = rows.filter((row) => !row.canOpenLocationDetail)
    expect(inScope.length, 'the bound actor must have at least one openable row').toBeGreaterThan(0)
    expect(outOfScope.length, 'the bound actor must have at least one out-of-scope row').toBeGreaterThan(0)

    for (const row of outOfScope) {
      await expect(workCenterLocationLink(page, row.code)).toHaveCount(0)
    }
    for (const row of inScope) {
      await expect(workCenterLocationLink(page, row.code)).toHaveCount(1)
    }
    await shot(page, GROUP, '05-row-scoped-list')

    // The in-scope link really is a card that answers.
    const target = inScope[0]
    await workCenterLocationLink(page, target.code).click()
    const detail = await net.waitFor('the in-scope card read', (records) =>
      records.find(
        (r) => r.complete && r.status !== undefined && r.method === 'GET' && detailPathOf(target.locationId).test(r.path),
      ),
    )
    expect(detail.status).toBe(200)
    await expect(cardHeading(page)).toBeVisible()

    // Item 4 (#66 5700319744): no out-of-scope card was requested at any point.
    for (const row of outOfScope.filter((candidate) => !inScope.some((i) => i.locationId === candidate.locationId))) {
      expect(net.issued(detailPathOf(row.locationId), 'GET')).toHaveLength(0)
    }
    expectReadOnly(net)
    coverage.item4 = 'MEASURED with the bound row-scoped actor'
    await net.write(GROUP, 'row-scoped.json')
  })

  test('a late answer from company A leaves nothing in company B, and B reads for itself', async ({
    page,
    context,
  }) => {
    test.skip(
      manifest.multiCompanyActor === undefined,
      'no multi-company actor is bound in this manifest',
    )
    await asActor(context, manifest.multiCompanyActor!)
    const net = LiveRecorder.attach(page)
    active = net

    await page.goto('/')
    const directory = await net.waitForNth('the company directory read', COMPANIES_RE, 'GET', 0)
    const companies = directory.responseBody as Array<{ id: string; code: string }>
    const companyA = companies.find((company) => company.code === manifest.companyACode)
    const companyB = companies.find((company) => company.code === manifest.companyBCode)
    expect(companyA, `${manifest.companyACode} must be in the actor's directory`).toBeTruthy()
    expect(companyB, `${manifest.companyBCode} must be in the actor's directory`).toBeTruthy()

    const picker = page.getByLabel('Şirket')
    if ((await picker.inputValue()) !== companyA!.id) {
      await picker.selectOption(companyA!.id)
      await expect(picker).toBeEnabled()
      await expect(picker).toHaveValue(companyA!.id)
    }

    // A's list, in the open: learn what A's rows are before holding the next read.
    await page.goto(WORK_CENTERS_PATH)
    const firstA = await net.waitForNth('company A’s first work-center read', WORK_CENTERS_RE, 'GET', 0)
    expect(firstA.requestHeaders['x-company-id']).toBe(companyA!.id)
    const rowsA: WorkCenterRow[] = workCenterRows(firstA.responseBody)
    expect(rowsA.length, 'company A must have work centers for this case to mean anything').toBeGreaterThan(0)

    // Hold A's NEXT read in the browser; the server answers it later, unchanged.
    let releaseA: (() => Promise<void>) | null = null
    let heldOutcome = 'not-held'
    await page.route(
      (url) => WORK_CENTERS_RE.test(url.pathname),
      async (route: Route) => {
        if (route.request().headers()['x-company-id'] !== companyA!.id || releaseA !== null) {
          await route.continue()
          return
        }
        heldOutcome = 'held'
        await new Promise<void>((resolve) => {
          releaseA = async () => {
            resolve()
          }
        })
        try {
          await route.continue()
          heldOutcome = 'continued-to-server'
        } catch {
          // The browser gave the request up (the switch cancelled it) before it was sent.
          heldOutcome = 'cancelled-by-browser'
        }
      },
    )
    await page.reload()
    await expect.poll(() => heldOutcome).toBe('held')

    await picker.selectOption(companyB!.id)
    await expect(picker).toHaveValue(companyB!.id)
    await expect(picker).toBeEnabled()
    const readB = await net.waitFor('company B’s own work-center read', (records) =>
      records.find(
        (r) => r.complete && r.status !== undefined && r.method === 'GET' && WORK_CENTERS_RE.test(r.path) && r.requestHeaders['x-company-id'] === companyB!.id,
      ),
    )
    expect(readB.status).toBe(200)
    const rowsB = workCenterRows(readB.responseBody)

    // Now let A's held read go, and give its answer every chance to paint.
    await (releaseA as unknown as () => Promise<void>)()
    await expect.poll(() => heldOutcome).not.toBe('held')
    await page.waitForTimeout(1_000)
    await net.drain()

    await expect(picker).toHaveValue(companyB!.id)
    for (const row of rowsA.filter((a) => !rowsB.some((b) => b.id === a.id))) {
      await expect(page.getByText(row.code, { exact: true })).toHaveCount(0)
    }
    if (rowsB.every((row) => !row.canOpenLocationDetail)) {
      await expect(anyWorkCenterLocationLink(page)).toHaveCount(0)
    }
    // Item 6, the part a browser can produce honestly: a real empty company is shown as
    // empty — not as an error, and not as the other company's rows.
    if (rowsB.length === 0) {
      await expect(page.locator('[data-state="empty-no-data"]')).toBeVisible()
      coverage.item6_empty = `MEASURED in ${manifest.companyBCode}`
    }
    expectReadOnly(net)
    coverage.item5 = `MEASURED; the held company-A read ended as: ${heldOutcome}`
    await shot(page, GROUP, '06-company-b-after-late-a')
    await net.write(GROUP, 'late-company-a.json')
  })

  test.afterEach(async ({}, testInfo) => {
    if (active !== null && testInfo.status !== testInfo.expectedStatus) {
      await attachEvidence(testInfo, active, GROUP)
    }
    active = null
  })

  test.afterAll(async () => {
    writeEvidence(GROUP, 'coverage.json', {
      items: {
        '1-2-7-8': coverage.items1_2_7_8 ?? 'NOT MEASURED in this run',
        '3': coverage.item3 ?? 'SKIPPED or failed: no bound work-centers-only actor produced evidence',
        '4': coverage.item4 ?? 'SKIPPED or failed: no bound row-scoped actor produced evidence',
        '5': coverage.item5 ?? 'SKIPPED or failed: no bound multi-company actor produced evidence',
        '6-empty': coverage.item6_empty ?? 'NOT MEASURED in this run',
        '6-forbidden': 'NOT MEASURABLE here: a real 403 on this read needs a stale capability, which only a mock or an authorization change mid-run can produce',
        '6-provider-unavailable': 'NOT MEASURABLE here: producing it means breaking the owned environment during the run',
      },
      boundActors: {
        multiCompanyActor: manifest.multiCompanyActor !== undefined,
        workCentersOnlyActor: manifest.workCentersOnlyActor !== undefined,
        rowScopedActor: manifest.rowScopedActor !== undefined,
      },
    })
  })
})
