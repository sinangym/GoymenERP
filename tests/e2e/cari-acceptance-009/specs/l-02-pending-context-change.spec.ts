/**
 * L-02 — CRO-AC-010 source step: a company/context change DURING a pending preparation
 * read, and what the late answer is not allowed to do afterwards.
 *
 * WHY THIS IS A SEPARATE, READ-ONLY STEP. The main journey (L-01) measures a context
 * change nowhere: its two commands are already dispatched when their replies are
 * withheld, so the reference point there is POSTCOMMIT and the only thing a later
 * transition could show is "no FURTHER effect". This step takes the other half — the
 * one the Backend boundary note admits without an extra Party — and keeps it strictly
 * before any dispatch: a pending GET, a company switch while it is in flight, and the
 * old answer arriving into a context that has moved on.
 *
 * Nothing here writes. No POST is issued in either context, so the environment's
 * before/after snapshot around this step must be equal on every counter and every row,
 * and `ExpectedNewParties 1` for the whole run is untouched.
 *
 * WHAT THIS IS NOT, stated so nothing downstream reads it as more:
 *  - Not a session revocation. The isolated Testing host authenticates by
 *    `X-Development-User` (DevelopmentBootstrap), and there is no sign-out affordance;
 *    a real production identity-provider session revocation cannot be produced here and
 *    is NOT measured. Only the company/route-context transition is.
 *  - Not a claim of server effect 0. An aborted delivery, a navigation or a context
 *    change proves nothing about the database on its own; the snapshot measures that.
 *  - Not an old-binary or old-client acceptance of any kind.
 *
 * NOTHING is mocked. The one `page.route` here DELAYS a real request and then lets it
 * complete against the real API — no status, body or header is ever synthesised.
 */
import { expect, test, type Page } from '@playwright/test'
import { LiveRecorder, isCompletedRead, lShot, readManifest, writeLJson } from '../src/lHarness.js'

const manifest = readManifest()
const A = manifest.companyAId
const B = manifest.companyBId
const CONTRACT = manifest.clientContractVersion
const CANDIDATES = '/api/v1/counterparties/company-account-link-candidates'

async function switchCompany(page: Page, companyId: string): Promise<void> {
  const picker = page.getByRole('combobox', { name: 'Şirket', exact: true })
  await picker.selectOption(companyId)
  const dialog = page.getByRole('dialog')
  if (await dialog.count()) await dialog.getByRole('button', { name: 'Değişiklikleri bırak' }).click()
  await expect(picker).toHaveValue(companyId, { timeout: 30_000 })
}

/**
 * The selection marker, read without waiting.
 *
 * `actionTimeout` is 0 in this lane, so a bare `getAttribute` on an element that is not
 * there blocks until the whole test times out. If company B renders the workspace closed
 * there is no marker to read, and "absent" is the honest answer.
 */
async function selectionState(page: Page): Promise<string | null> {
  const marker = page.locator('[data-selection]')
  if ((await marker.count()) === 0) return null
  return marker.first().getAttribute('data-selection')
}

test('L-02 CRO-AC-010: a company change while a preparation read is pending — the late answer does not open the new context, and nothing is written', async ({ browser }, testInfo) => {
  test.setTimeout(240_000)
  const slug = 'l02-pending-context-change'
  expect(testInfo.retry).toBe(0)

  /**
   * A separate, explicitly opened browser context.
   *
   * The journey actor is NOT changed: `VITE_DEV_USER` is baked into the bundle the one
   * dev server serves, so this context speaks as the same synthetic actor L-01 uses.
   * That is the point — what moves here is the company and the route context, not the
   * identity, and changing the identity would mean serving a different bundle to the
   * whole run.
   */
  // The opening instant of the bracket the environment owner can snapshot against.
  const stepStartedAtUtc = new Date().toISOString()
  const context = await browser.newContext({ locale: 'tr-TR', viewport: { width: 1280, height: 800 } })
  const page = await context.newPage()
  await page.addInitScript((id: string) => {
    if (window.localStorage.getItem('goymen-company-id') === null) window.localStorage.setItem('goymen-company-id', id)
  }, A)
  const net = LiveRecorder.attach(page)

  try {
    /**
     * Hold ONE candidate read open, then let it finish for real.
     *
     * `route.continue()` is simply deferred — the request reaches the live API with its
     * own headers and comes back with the server's own answer. A pending request is not
     * something a mock could stand in for here: the whole question is what the real
     * answer does when it lands late.
     */
    let holdNext = false
    let heldPath: string | null = null
    let release = (): void => {}
    const held = new Promise<void>((resolve) => {
      release = resolve
    })
    await page.route(
      (url) => url.pathname === CANDIDATES,
      async (route) => {
        if (!holdNext) {
          await route.continue()
          return
        }
        holdNext = false
        heldPath = new URL(route.request().url()).pathname
        await held
        await route.continue()
      },
    )

    // ---- under A: a real, authorized preparation read and a fresh selection --------
    await page.goto('/cari/sirkete-bagla')
    await expect(page.getByRole('heading', { level: 1, name: 'Cariyi Şirkete Bağla' })).toBeVisible({ timeout: 60_000 })
    await expect(page.getByRole('combobox', { name: 'Şirket', exact: true })).toHaveValue(A)
    await expect.poll(() => net.candidateReads().filter((r) => r.companyId === A && r.status === 200).length, { timeout: 30_000 }).toBeGreaterThanOrEqual(1)
    /**
     * A selection makes the strongest version of this case — authority granted by a read
     * in the previous context, which must not survive the move. Whether company A has any
     * candidate at all is the fixture's business, not this step's, so the selection is
     * taken when one is offered and the fact that it was is recorded either way. The
     * pending-read half below does not depend on it.
     */
    const selectionMeasured = (await page.getByRole('radio').count()) > 0
    if (selectionMeasured) {
      await page.getByRole('radio').first().check()
      await expect.poll(() => selectionState(page)).toBe('fresh')
    }
    const readsBeforeHold = net.candidateReads().filter((r) => r.status === 200).length
    await lShot(page, slug, '01-A-selection-fresh')

    // ---- arm the hold, then make the screen read again -----------------------------
    holdNext = true
    const search = page.getByRole('searchbox').first()
    await search.fill('CARI')
    await page.getByRole('button', { name: 'Ara', exact: true }).click()
    /**
     * The read is out and has not been answered: that is the pending state this measures.
     *
     * Identified by its `q` key rather than by "the most recent unanswered read": under
     * the dev server StrictMode duplicates a mount's first read and cancels one copy, and
     * a copy that has not yet been marked failed looks pending too. Only the search read
     * carries `q`, so this is the one that was held and no other.
     */
    const heldPending = () => net.candidateReads().filter((r) => r.queryKeys.includes('q') && r.status === null && r.failure === null)
    await expect.poll(() => heldPending().length, { timeout: 30_000 }).toBeGreaterThanOrEqual(1)
    const pending = heldPending().at(-1)!
    expect(heldPath, 'the route handler must have held a candidate read').toBe(CANDIDATES)
    expect(pending.path).toBe(CANDIDATES)
    expect(pending.companyId).toBe(A)
    expect(pending.contractDeclaration).toBe(CONTRACT)
    await lShot(page, slug, '02-A-read-pending')

    /**
     * The state at the instant BEFORE the transition, captured rather than assumed.
     *
     * Committing the search already suspends the selection in company A: a new query key
     * with `keepPreviousData` makes the rows non-authoritative, so `data-selection`
     * becomes "suspended" twenty lines before any company changes
     * (`CariLinkWorkspacePage.tsx:231-239,697`). Recording "fresh before the switch"
     * would then credit the company change with something the pending search had already
     * done.
     */
    const selectionBeforeSwitch = await selectionState(page)

    // ---- the context moves while that read is still in flight ----------------------
    const switchTick = net.currentTick()
    await switchCompany(page, B)
    await expect(page.getByRole('combobox', { name: 'Şirket', exact: true })).toHaveValue(B)
    /**
     * The selection does not survive the move. It was authority granted by a read in the
     * previous context, and the new context has not read anything yet.
     */
    // The new context presents no fresh selection. Whether the switch or the pending
    // search produced that is decided by the pair of readings, not by this assertion.
    await expect.poll(() => selectionState(page)).not.toBe('fresh')
    const selectionAfterSwitch = await selectionState(page)
    await lShot(page, slug, '03-B-after-switch')

    /**
     * ---- the held request is released, and what happens to it is MEASURED -----------
     *
     * It is not going to be a late 200, and demanding one would be demanding that the
     * application behave worse. The workspace is keyed on the context namespace
     * (`CariLinkWorkspacePage.tsx:83`), so the switch unmounts it; the query's `queryFn`
     * consumed the abort signal and passed it to the real fetch, so query-core cancels
     * the read when its last observer goes. The browser therefore aborts this request
     * while it is still paused in the route handler.
     *
     * That IS the fence working. So the outcome is measured and named — delivered, or
     * cancelled — exactly as the sibling t-04 does for the same transition, and an abort
     * is never written down as a late delivery. Everything the step must establish
     * (POST 0, no previous-context content in the new one, the terminal state, the
     * snapshot bracket) holds either way.
     */
    release()
    await expect
      .poll(() => net.records.find((r) => r.seq === pending.seq)?.outcome, { timeout: 30_000 })
      .not.toBe('pending')
    const late = net.records.find((r) => r.seq === pending.seq)!
    expect(late.companyId).toBe(A)
    const heldDelivered = late.outcome === 'finished' && late.bodyState === 'complete'
    const heldOutcome = heldDelivered
      ? isCompletedRead(late, CONTRACT)
        ? 'delivered-complete-acknowledged'
        : 'delivered-complete-other'
      : late.outcome === 'failed'
        ? 'cancelled-by-application'
        : 'delivered-without-body'
    // A cancelled request carries no answer at all — and must never be presented as one.
    if (!heldDelivered) expect(late.responseBody).toBeNull()
    const firstBRead = net.candidateReads().find((r) => r.companyId === B)
    const settledAfterContextChange =
      firstBRead !== undefined && late.settledTick !== null ? late.settledTick > firstBRead.requestTick : null

    // Give it every chance to do something it should not.
    await page.waitForTimeout(1500)
    await expect(page.getByRole('combobox', { name: 'Şirket', exact: true })).toHaveValue(B)
    await expect.poll(() => selectionState(page)).not.toBe('fresh')
    await expect(page.locator('[data-outcome]')).toHaveCount(0)
    await expect(page.locator('[data-attempt]')).toHaveCount(0)
    /**
     * The real question, measured instead of declared: did the previous company's rows
     * reach this screen? Every code the late A answer carried is looked for in the page.
     * Command markers alone prove nothing here — nothing was ever submitted, so they are
     * trivially absent.
     */
    const lateItems = ((late.responseBody as { items?: { id?: string; code?: string }[] } | null)?.items ?? []).filter(
      (item): item is { id?: string; code: string } => typeof item?.code === 'string',
    )
    const bodyText = await page.locator('body').innerText()
    const leakedCodes = lateItems.filter((item) => bodyText.includes(item.code)).map((item) => item.code)
    expect(leakedCodes, 'a row from the previous context must not be rendered into this one').toEqual([])
    /**
     * The leak check only means something when there was an answer to leak. When the
     * application cancelled the read there is no body, `lateItems` is empty and the
     * assertion above is vacuous — so which of the two happened is recorded, and the
     * check is not counted as evidence in the cancelled case.
     */
    const leakCheckApplicable = heldDelivered && lateItems.length > 0
    /**
     * The write gate in the new context stands on the new context's own reads. Either it
     * is shut, or a B-scoped acknowledged read has answered since the switch — never on
     * the strength of the answer that just arrived for A.
     */
    const submit = page.getByRole('button', { name: 'Şirkete bağla', exact: true })
    const submitEnabled = await submit.isEnabled()
    /**
     * Readiness in the new context may only rest on the new context's OWN reads — reads
     * issued after the switch, not merely after the held one went out.
     *
     * What is asserted is the gate: either it is shut, or a B-scoped acknowledged read
     * answered in this context. What is NOT asserted, because this step cannot establish
     * it, is which of the two produced the state; both halves are recorded so nobody has
     * to infer it from a flag.
     */
    const bReadsAfterSwitch = net
      .candidateReads()
      .filter((r) => r.companyId === B && r.status === 200 && r.contractAck === CONTRACT && r.requestTick > switchTick)
    expect(submitEnabled === false || bReadsAfterSwitch.length > 0).toBe(true)
    await lShot(page, slug, '04-B-late-answer-granted-nothing')

    // The new context reached a terminal state of its own — not still loading, which is
    // when "no row of A is here" would be true for the wrong reason.
    await expect(page.locator('[data-state="initial-loading"]')).toHaveCount(0)

    // ---- nothing was written, in either context ------------------------------------
    expect(net.creates()).toHaveLength(0)
    expect(net.links()).toHaveLength(0)
    expect(net.records.filter((r) => r.method === 'POST')).toHaveLength(0)
    expect(net.records.every((r) => r.devIdentityHeader === manifest.actor)).toBe(true)
    expect(net.records.every((r) => r.companyId === null || r.companyId === A || r.companyId === B)).toBe(true)

    const barrier = await net.quiesce()

    writeLJson('measurements', `${slug}.json`, {
      runId: manifest.runId,
      instanceId: manifest.instanceId,
      apiBaseUrl: manifest.apiBaseUrl,
      fixtureHash: manifest.fixtureHash,
      case: 'CRO-AC-010 pending READ + company/context change',
      separateBrowserContext: true,
      mainActorUnchanged: manifest.actor,
      pendingRead: {
        path: heldPath,
        companyId: pending.companyId,
        queryKeys: pending.queryKeys,
        declaredContract: pending.contractDeclaration,
        heldWhileContextChanged: true,
      },
      heldRequest: {
        /**
         * The measured outcome, named. The expected one in this application is
         * `cancelled-by-application`: the switch unmounts the namespace-keyed workspace
         * and query-core aborts the read whose signal was consumed. That cancellation is
         * the fence doing its job — it is recorded as a cancellation, never as a late
         * delivery.
         */
        outcome: heldOutcome,
        transferOutcome: late.outcome,
        bodyState: late.bodyState,
        failure: late.failure,
        status: late.status,
        ack: late.contractAck,
        settledAfterContextChange,
      },
      lateAnswer: {
        companyId: late.companyId,
        /** Only stated where the sub-case actually ran. */
        grantedSelection: selectionMeasured ? false : 'not measured — company A offered no candidate to select',
        /** Measured only when there was an answer to leak; vacuous otherwise, and said so. */
        leakCheckApplicable,
        rowsCarried: lateItems.length,
        rowsRenderedIntoNewContext: leakedCodes.length,
        /**
         * The gate's state and the new context's own reads, side by side. Which one
         * produced the state is NOT established by this step, so it is not claimed.
         */
        writeGate: { submitEnabled, bScopedAcknowledgedReadsAfterSwitch: bReadsAfterSwitch.length, causalityEstablished: false },
      },
      selection: {
        measured: selectionMeasured,
        /** Read at the instant before the transition, not assumed from an earlier one. */
        afterSelecting: selectionMeasured ? 'fresh' : 'no candidate offered under A; selection sub-case not measured',
        beforeSwitch: selectionBeforeSwitch,
        suspendedByPendingSearchBeforeSwitch: selectionBeforeSwitch === 'suspended',
        droppedByTheSwitch: selectionBeforeSwitch === 'fresh' && selectionAfterSwitch !== 'fresh',
        afterSwitch: selectionAfterSwitch, afterHeldSettled: await selectionState(page),
      },
      cursorAndGeneration: {
        candidateQueryKeys: [...new Set(net.candidateReads().flatMap((r) => r.queryKeys))],
        readsBeforeHold,
        bScopedAcknowledgedReadsAfterSwitch: bReadsAfterSwitch.length,
        note: 'Query VALUES are never recorded (keys only). Route-generation is internal state; what is recorded is the observable behaviour it produces — the previous context’s selection and readiness do not carry over.',
      },
      /**
       * Read-only, before any dispatch. The environment's before/after snapshot around
       * this step must therefore be equal on every counter and every row.
       */
      snapshotBoundary: {
        reference: 'PRE_DISPATCH',
        postsIssued: 0,
        /**
         * This step is written to run FIRST, as its own serial invocation (workers 1,
         * retries 0), between the environment owner's source-check and snapshot pair —
         * so the bracket below contains this step and nothing else. L-01's single-Party
         * journey runs afterwards.
         */
        executionOrder: { thisStep: 'L-02', runsBefore: 'L-01', separateSerialInvocation: true, workers: 1, retries: 0 },
        expectedEffectInWiderInvocationInterval: 0,
        /**
         * A per-step delta needs a bracket, and only the environment owner can take one.
         * These two instants are the bracket this step offers; the run-level
         * ExpectedNewParties 1 cannot isolate this step's contribution on its own.
         */
        stepStartedAtUtc,
        stepEndedAtUtc: new Date().toISOString(),
        expectedSnapshotDeltaBetweenThoseInstants: 'zero on all six Effects counters and on Rows',
        browserCanProveServerEffect: false,
        note: 'Browser quiescence and a POST count of zero are what this step can state. Whether the database moved between the two instants above is the environment snapshot’s measurement, not this record’s.',
      },
      browserBarrier: barrier,
      notMeasuredHere: [
        'real session exit / identity-provider session revocation (DevelopmentBootstrap is not evidence of it, and no sign-out affordance exists)',
        'context change while a real command POST is already dispatched (that reference is POSTCOMMIT and belongs to L-01)',
        'legacy-protocol / unsupported-declaration 409 negatives (bound by the environment owner’s own measurement, not by this browser)',
        'any old server binary behaviour: none exists in this single-node environment',
      ],
    })
    await net.finish(testInfo, slug, { case: 'CRO-AC-010 pending read + context change', postsIssued: 0, browserBarrier: barrier })
  } finally {
    await context.close()
  }
})
