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
 * browser's own request and then hands it on with `route.fallback()`.
 *
 * FALLBACK, NOT CONTINUE — AND WHY IT IS ASSERTED, NOT ASSUMED. A page route runs BEFORE
 * the context route, and `route.continue()` ends the chain (Playwright 1.62.1
 * `Page._onRoute` returns once a handler reports handled; `continue` reports handled,
 * `fallback` does not). A HOLD that continued would send its read, and every later read
 * it matched, past the identity rewrite as the ambient `VITE_DEV_USER` — the operator,
 * not the bound actor. So `asActor` records every request it rewrote, and each case that
 * uses it asserts that every API answer it received belongs to a rewritten request.
 *
 * ACTORS COME FROM THE MANIFEST. Items 3 and 4 need subjects the normal Development
 * seed does not have; those cases SKIP when the manifest binds none. This package adds
 * no actor, role or grant; the manifest declares each bound test actor's grant and
 * `coverage.json` carries it next to what was observed.
 *
 * COVERAGE IS A CLOSED SET. Each item's `status` is exactly one of MEASURED, SKIPPED,
 * FAILED or NOT_MEASURED; the free text lives in `detail`. A gate reads `status` and never
 * searches text. Business #66 5705730345: items 3 and 4 must each be MEASURED; a green
 * exit with either SKIPPED is not ORG-03 browser acceptance.
 *
 * NOT MEASURED HERE: database rows (the SQL cases own that), audit contents, and — for
 * item 6 — a real 403 or provider failure on this read. Neither can be produced from a
 * browser without a mock or without breaking the environment, so `coverage.json` says
 * so instead of claiming them.
 */
import { expect, test, type BrowserContext, type Page, type Request, type Route } from '@playwright/test'
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
import { UNSETTLED, settleClaimed, type ItemEvidence } from '../src/coverage.js'
import { readOrganizationManifest } from '../src/manifest.js'

const manifest = readOrganizationManifest()
const GROUP = 'o-04-work-center-journey'
/** The screen's own label for the return target, as the card's breadcrumb shows it. */
const BACK_LABEL = 'İş Merkezleri'
const WRITE_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE'])
const API_RE = /^\/api\/v1\//
const CAPABILITIES_RE = /^\/api\/v1\/shell\/capabilities$/
/** `OrganizationShellCapabilityProvider` ids: the ORG-03 list and the location card. */
const LIST_CAPABILITY = 'organization.work-centers.list'
const CARD_CAPABILITY = 'organization.locations.detail'

interface Rewrite {
  readonly request: Request
  readonly method: string
  readonly path: string
  readonly companyId: string | undefined
  outcome: 'pending' | 'sent' | 'not-sent'
  error?: string
}

/** Every request `asActor` rewrote, and every API answer the page received. */
class ActorRewrites {
  readonly entries: Rewrite[] = []
  private readonly answered: Request[] = []

  constructor(readonly actor: string) {}

  watch(page: Page): void {
    page.on('response', (response) => {
      if (API_RE.test(new URL(response.url()).pathname)) this.answered.push(response.request())
    })
  }

  of(request: Request): Rewrite | undefined {
    return this.entries.find((entry) => entry.request === request)
  }

  sent(pattern: RegExp, companyId: string): Rewrite[] {
    return this.entries.filter(
      (entry) => entry.outcome === 'sent' && pattern.test(entry.path) && entry.companyId === companyId,
    )
  }

  /** Answers to requests this rewrite never sent — i.e. answers about some other subject. */
  unrewrittenAnswers(): string[] {
    return this.answered
      .filter((request) => this.of(request) === undefined || this.of(request)!.outcome === 'not-sent')
      .map((request) => `${request.method()} ${new URL(request.url()).pathname}`)
  }
}

/** Runs the real app as another seeded actor by rewriting only the identity header. */
async function asActor(context: BrowserContext, page: Page, actor: string): Promise<ActorRewrites> {
  const rewrites = new ActorRewrites(actor)
  rewrites.watch(page)
  await context.route('**/api/v1/**', async (route) => {
    const request = route.request()
    const entry: Rewrite = {
      request,
      method: request.method(),
      path: new URL(request.url()).pathname,
      companyId: request.headers()['x-company-id'],
      outcome: 'pending',
    }
    rewrites.entries.push(entry)
    try {
      await route.continue({
        headers: { ...request.headers(), 'x-development-user': actor },
      })
      entry.outcome = 'sent'
    } catch (error) {
      // The browser gave the request up before it was sent (a company switch cancels a
      // held read). Recorded instead of thrown: a throw escapes the route chain, and a
      // request that was never sent cannot have produced an answer the case relies on.
      entry.outcome = 'not-sent'
      entry.error = error instanceof Error ? error.message.split('\n')[0] : String(error)
    }
  })
  return rewrites
}

function expectEveryAnswerRewritten(rewrites: ActorRewrites): void {
  expect(
    rewrites.unrewrittenAnswers(),
    `every API answer must belong to a request sent as ${rewrites.actor}`,
  ).toEqual([])
}

function detailPathOf(locationId: string): RegExp {
  return new RegExp(`^/api/v1/organization/locations/${locationId}$`)
}

/** Item 7, from the wire: the whole case issued reads only. */
function expectReadOnly(net: LiveRecorder): void {
  const writes = net.records.filter((record) => WRITE_METHODS.has(record.method))
  expect(writes, 'the ORG-03 journey must issue no write of any kind').toEqual([])
}

/**
 * Every row of the answer is on the page. Negative link counts are asserted only after
 * this: the heading also renders while loading, and `toHaveCount(0)` passes at once on a
 * table that has not been drawn yet.
 */
async function expectRowsRendered(page: Page, rows: readonly WorkCenterRow[]): Promise<void> {
  for (const row of rows) {
    await expect(page.getByRole('row').filter({ hasText: row.code })).toHaveCount(1)
  }
}

/** The capability ids of every snapshot the server answered for this company. */
async function capabilitySnapshots(net: LiveRecorder, companyId: string): Promise<string[][]> {
  const forCompany = (r: LiveRecord) =>
    r.complete && r.status === 200 && CAPABILITIES_RE.test(r.path) && r.requestHeaders['x-company-id'] === companyId
  await net.waitFor(`the capability snapshot for company ${companyId}`, (records) => records.find(forCompany))
  await net.drain()
  return net.records.filter(forCompany).map((record) => {
    const ids = (record.responseBody as { capabilities?: unknown } | undefined)?.capabilities
    return Array.isArray(ids) ? ids.filter((id): id is string => typeof id === 'string') : []
  })
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

type ItemKey = '1-2-7-8' | '3' | '4' | '5' | '6-empty' | '6-forbidden' | '6-provider-unavailable'

const notStarted = (): ItemEvidence => ({
  status: 'NOT_MEASURED',
  detail: 'the case did not start in this run',
  actor: null,
  grant: null,
})

/** What `coverage.json` records about each item, written once at the end. */
const coverage: Record<ItemKey, ItemEvidence> = {
  '1-2-7-8': notStarted(),
  '3': notStarted(),
  '4': notStarted(),
  '5': notStarted(),
  '6-empty': notStarted(),
  '6-forbidden': {
    status: 'NOT_MEASURED',
    detail: 'a real 403 on this read needs a stale capability, which only a mock or an authorization change mid-run can produce',
    actor: null,
    grant: null,
  },
  '6-provider-unavailable': {
    status: 'NOT_MEASURED',
    detail: 'producing it means breaking the owned environment during the run',
    actor: null,
    grant: null,
  },
}

/** The items the running case answers for; `afterEach` settles them against the case's outcome. */
let claimed: readonly ItemKey[] = []

function claim(keys: readonly ItemKey[], actor: string | undefined): void {
  claimed = keys
  for (const key of keys) {
    coverage[key] = {
      status: 'FAILED',
      detail: UNSETTLED,
      actor: actor ?? null,
      grant: actor === undefined ? null : (manifest.actorGrants[actor] ?? null),
    }
  }
}

/** Called only as a case's LAST step, after its evidence files are written. */
function settle(key: ItemKey, status: 'MEASURED' | 'NOT_MEASURED', detail: string, observed?: Record<string, unknown>): void {
  coverage[key] = { ...coverage[key], status, detail, observed }
}

let active: LiveRecorder | null = null

test.describe('O-04 work centers (ORG-03)', () => {
  test('sidebar → list → a row’s card by keyboard → back to the list, reading only', async ({
    page,
    context,
  }) => {
    claim(['1-2-7-8'], manifest.operatorActor)
    // The operator is bound through the same identity rewrite as every other actor, never the
    // ambient `VITE_DEV_USER`: the BROWSER-CI web image bakes its own Development user at build
    // time (the authorized profile's is `group-bootstrap-admin`), so an unrewritten request would
    // run the claimed operator case as somebody else.
    const rewrites = await asActor(context, page, manifest.operatorActor)
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
    // The list is mounted again on return; its rows are drawn before anything is asserted absent.
    await expectRowsRendered(page, rows)

    // Every row without the flag offered no link, and none of their cards was asked for.
    for (const row of rows.filter((candidate) => !candidate.canOpenLocationDetail)) {
      await expect(workCenterLocationLink(page, row.code)).toHaveCount(0)
      expect(net.issued(detailPathOf(row.locationId), 'GET')).toHaveLength(0)
    }
    expectReadOnly(net)
    expectEveryAnswerRewritten(rewrites)
    await net.write(GROUP, 'operator-journey.json')
    settle('1-2-7-8', 'MEASURED', 'operator journey completed', {
      companyId: read.requestHeaders['x-company-id'],
      rows: rows.map((row) => ({ code: row.code, canOpenLocationDetail: row.canOpenLocationDetail })),
      followedRow: openable!.code,
    })
  })

  test('without the card capability the list works, offers no link and asks no card', async ({
    page,
    context,
  }) => {
    claim(['3'], manifest.workCentersOnlyActor)
    test.skip(
      manifest.workCentersOnlyActor === undefined,
      'no work-centers-only actor is bound in this manifest; the normal seed has none',
    )
    const rewrites = await asActor(context, page, manifest.workCentersOnlyActor!)
    const net = LiveRecorder.attach(page)
    active = net

    const read = await listFromSidebar(page, net)
    expect(read.status).toBe(200)
    const companyId = read.requestHeaders['x-company-id']
    // The actor is what item 3 says it is: the list capability, and no card capability.
    const snapshots = await capabilitySnapshots(net, companyId)
    for (const ids of snapshots) {
      expect(ids).toContain(LIST_CAPABILITY)
      expect(ids).not.toContain(CARD_CAPABILITY)
    }
    const rows = workCenterRows(read.responseBody)
    expect(rows.length).toBeGreaterThan(0)
    // The server agrees with the capability: no row may open a card.
    expect(rows.every((row) => row.canOpenLocationDetail === false)).toBe(true)
    await expectRowsRendered(page, rows)
    await expect(anyWorkCenterLocationLink(page)).toHaveCount(0)
    // Item 3: the card read is never ATTEMPTED — counted over issued, not answered.
    expect(net.issued(DETAIL_RE, 'GET')).toHaveLength(0)
    expectReadOnly(net)
    await net.drain()
    expectEveryAnswerRewritten(rewrites)
    await shot(page, GROUP, '04-work-centers-only')
    await net.write(GROUP, 'work-centers-only.json')
    settle('3', 'MEASURED', 'list 200 without the card capability; links 0; card GET issued 0', {
      companyId,
      capabilitySnapshots: snapshots.length,
      rows: rows.map((row) => ({ code: row.code, canOpenLocationDetail: row.canOpenLocationDetail })),
    })
  })

  test('with the card capability, an out-of-scope row offers no link and its card is never asked', async ({
    page,
    context,
  }) => {
    claim(['4'], manifest.rowScopedActor)
    test.skip(
      manifest.rowScopedActor === undefined,
      'no row-scoped actor (work centers on two locations, cards on one) is bound in this manifest; the normal seed has none',
    )
    const rewrites = await asActor(context, page, manifest.rowScopedActor!)
    const net = LiveRecorder.attach(page)
    active = net

    const read = await listFromSidebar(page, net)
    expect(read.status).toBe(200)
    const companyId = read.requestHeaders['x-company-id']
    // The capability is present, so any missing link is the ROW's answer, not the route's.
    const snapshots = await capabilitySnapshots(net, companyId)
    for (const ids of snapshots) {
      expect(ids).toContain(LIST_CAPABILITY)
      expect(ids).toContain(CARD_CAPABILITY)
    }
    const rows = workCenterRows(read.responseBody)
    // The case only means something when the server answered BOTH ways in one list.
    const inScope = rows.filter((row) => row.canOpenLocationDetail)
    const outOfScope = rows.filter((row) => !row.canOpenLocationDetail)
    expect(inScope.length, 'the bound actor must have at least one openable row').toBeGreaterThan(0)
    expect(outOfScope.length, 'the bound actor must have at least one out-of-scope row').toBeGreaterThan(0)

    // Drawn first, positives next, negatives last: a zero count on an undrawn table proves nothing.
    await expectRowsRendered(page, rows)
    for (const row of inScope) {
      await expect(workCenterLocationLink(page, row.code)).toHaveCount(1)
    }
    for (const row of outOfScope) {
      await expect(workCenterLocationLink(page, row.code)).toHaveCount(0)
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
    const neverAsked = outOfScope.filter((candidate) => !inScope.some((i) => i.locationId === candidate.locationId))
    for (const row of neverAsked) {
      expect(net.issued(detailPathOf(row.locationId), 'GET')).toHaveLength(0)
    }
    expectReadOnly(net)
    await net.drain()
    expectEveryAnswerRewritten(rewrites)
    await net.write(GROUP, 'row-scoped.json')
    settle('4', 'MEASURED', 'one list answered both ways; in-scope card 200; out-of-scope links 0 and card GET issued 0', {
      companyId,
      capabilitySnapshots: snapshots.length,
      rows: rows.map((row) => ({ code: row.code, locationId: row.locationId, canOpenLocationDetail: row.canOpenLocationDetail })),
      followedRow: target.code,
      cardStatus: detail.status,
    })
  })

  test('a late answer from company A leaves nothing in company B, and B reads for itself', async ({
    page,
    context,
  }) => {
    claim(['5', '6-empty'], manifest.multiCompanyActor)
    test.skip(
      manifest.multiCompanyActor === undefined,
      'no multi-company actor is bound in this manifest',
    )
    const rewrites = await asActor(context, page, manifest.multiCompanyActor!)
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

    // Hold A's NEXT read in the browser; the server answers it later, unchanged. Every
    // branch hands the request on with `fallback()`, so it still passes the identity rewrite.
    let releaseA: (() => void) | null = null
    let heldRequest: Request | null = null
    await page.route(
      (url) => WORK_CENTERS_RE.test(url.pathname),
      async (route: Route) => {
        if (route.request().headers()['x-company-id'] !== companyA!.id || heldRequest !== null) {
          await route.fallback()
          return
        }
        heldRequest = route.request()
        await new Promise<void>((resolve) => {
          releaseA = resolve
        })
        await route.fallback()
      },
    )
    await page.reload()
    await expect.poll(() => releaseA !== null).toBe(true)

    await picker.selectOption(companyB!.id)
    await expect(picker).toHaveValue(companyB!.id)
    await expect(picker).toBeEnabled()
    const readB = await net.waitFor('company B’s own work-center read', (records) =>
      records.find(
        (r) => r.complete && r.status !== undefined && r.method === 'GET' && WORK_CENTERS_RE.test(r.path) && r.requestHeaders['x-company-id'] === companyB!.id,
      ),
    )
    expect(readB.status).toBe(200)
    await expect
      .poll(() => rewrites.sent(WORK_CENTERS_RE, companyB!.id).length, {
        message: 'B’s read must be sent as the bound actor',
      })
      .toBeGreaterThan(0)
    const rowsB = workCenterRows(readB.responseBody)

    // Now let A's held read go, and give its answer every chance to paint.
    const release = releaseA as unknown as () => void
    release()
    await expect
      .poll(() => rewrites.of(heldRequest!)?.outcome ?? 'pending', {
        message: 'the held read must come back through the identity rewrite',
      })
      .not.toBe('pending')
    const heldOutcome =
      rewrites.of(heldRequest!)!.outcome === 'sent' ? 'continued-to-server' : 'cancelled-by-browser'
    await page.waitForTimeout(1_000)
    await net.drain()

    await expect(picker).toHaveValue(companyB!.id)
    // B's own answer is on the page before anything is asserted absent from it.
    if (rowsB.length > 0) {
      await expectRowsRendered(page, rowsB)
    } else {
      await expect(page.locator('[data-state="empty-no-data"]')).toBeVisible()
    }
    for (const row of rowsA.filter((a) => !rowsB.some((b) => b.id === a.id))) {
      await expect(page.getByText(row.code, { exact: true })).toHaveCount(0)
    }
    if (rowsB.every((row) => !row.canOpenLocationDetail)) {
      await expect(anyWorkCenterLocationLink(page)).toHaveCount(0)
    }
    expectReadOnly(net)
    expectEveryAnswerRewritten(rewrites)
    await shot(page, GROUP, '06-company-b-after-late-a')
    await net.write(GROUP, 'late-company-a.json')
    // Item 6, the part a browser can produce honestly: a real empty company is shown as
    // empty — not as an error, and not as the other company's rows.
    if (rowsB.length === 0) {
      settle('6-empty', 'MEASURED', `empty state shown in ${manifest.companyBCode}`, { companyId: companyB!.id })
    } else {
      settle('6-empty', 'NOT_MEASURED', `${manifest.companyBCode} has work centers in this run`, {
        companyId: companyB!.id,
        rows: rowsB.length,
      })
    }
    settle('5', 'MEASURED', 'A’s held read left nothing in B; B read for itself as the bound actor', {
      companyA: companyA!.id,
      companyB: companyB!.id,
      heldOutcome,
    })
  })

  test.afterEach(async ({}, testInfo) => {
    // A red case turns every item it claimed FAILED, even one it settled before failing.
    settleClaimed(coverage, claimed, {
      status: testInfo.status ?? 'unknown',
      expectedStatus: testInfo.expectedStatus,
      skipReason: testInfo.annotations.find((a) => a.type === 'skip')?.description,
      error: testInfo.error?.message?.split('\n')[0],
    })
    claimed = []
    if (active !== null && testInfo.status !== testInfo.expectedStatus) {
      await attachEvidence(testInfo, active, GROUP)
    }
    active = null
  })

  test.afterAll(async () => {
    writeEvidence(GROUP, 'coverage.json', {
      runId: manifest.runId,
      browserJourneyCommit: manifest.binding.browserJourneyCommit,
      items: coverage,
      // Business #66 5705730345, read from `status` alone.
      itemsThreeAndFourMeasured: coverage['3'].status === 'MEASURED' && coverage['4'].status === 'MEASURED',
      boundActors: {
        operatorActor: manifest.operatorActor,
        multiCompanyActor: manifest.multiCompanyActor ?? null,
        workCentersOnlyActor: manifest.workCentersOnlyActor ?? null,
        rowScopedActor: manifest.rowScopedActor ?? null,
      },
    })
  })
})
