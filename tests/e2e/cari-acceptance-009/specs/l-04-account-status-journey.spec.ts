/**
 * L-04 — C-03: the SELECTED company account deactivated for a reason, reactivated for a
 * separate reason, through one real record — Business §14.2 "Aktif/pasif yap"
 * (PR #232 comment 5647422648), prepared per the handover in PR #250 comment 5648104588.
 *
 * SOURCE CONTRACT, pinned: `PUT /api/v1/counterparties/{partyId}/company-account/active-state`
 * (`SetCounterpartyCompanyAccountActiveStateV1`) at `main@fc14554d` — body
 * `{ isActive, expectedVersion, reason }`, success EXACTLY 204 with no body, terminal
 * failures 404 `not_found` / 409 `version_conflict`, the fence being the ACCOUNT's
 * version carried as the decimal string the schema's `[integer, string]` accepts.
 * Nothing here invents a field, a limit, an endpoint or a count.
 *
 * STATUS: HAZIRLANDI / NOT_RUN. This is SOURCE, written against the checked-in
 * contract and the fixture Business described; it has not been executed. It runs only
 * on a real environment Backend prepares (manifest, fixture, instance), in the same
 * exact frontend/backend/fixture/schema composition, and its typed verifier binds to
 * the projections named below. No mock stands in for the API anywhere in this file.
 *
 * THE FIXTURE Business named: one Party P; its company-A account roles 0, ACTIVE,
 * version 7, with a populated contact profile; its company-B account roles 0, active,
 * version 1. The status-authorized actor is the manifest's, the read-only actor is a
 * separate one. This is NOT the db61/L-03 environment or its data.
 *
 * THE SEQUENCE AND THE NUMBERS, so the environment's verifier binds to them rather than
 * inferring them:
 *
 *   K1  MAIN page: "Bu şirkette pasife al" with reason R1 on fence 7. The reply is
 *       WITHHELD after the server commits (a real 204, its delivery cut). The attempt
 *       is UNKNOWN on screen, its key frozen; the main page does NOT retry yet. → v8
 *   KS  SECOND tab, opened on the card BEFORE K1 with its window opened on fence 7:
 *       "Pasife al" with reason R2 → 409 version_conflict (durable, audit 0). "Kaydı
 *       yenile" re-reads → v8, PASSIVE — the window is re-seated on the current record
 *       with the reason kept, and its intent has become "aktifleştir".
 *       The user closes it (reason discarded on purpose), goes to the LIST — the row
 *       shows the account passive — and reopens the card by KEYBOARD.
 *   K2  SECOND tab: "Bu şirkette aktifleştir" with reason R2 on fence 8. Its 204 is
 *       delivered; the read it owes is WITHHELD (a real, acknowledged 200 whose
 *       delivery is cut, spec-locally — a GET carries no Idempotency-Key for the
 *       harness withholder to target), so the card leaves `ready`, says "saved, view
 *       not refreshed" over the network-error block and offers the READ's own retry;
 *       that read lands v9 ACTIVE and the result is adopted.                 → v9
 *   K1' MAIN page: the explicit same-key retry of K1 — a fresh read first (v9), then the
 *       SAME key and body. The server hands back the STORED 204 as a replay
 *       (`Idempotency-Replayed: true`); a SEPARATE fresh read after it still says
 *       ACTIVE v9, so the card reports a PAST command's result and the CURRENT state,
 *       and claims no "left the active list". The old key does NOT re-deactivate the
 *       account. The three exchanges are proven in NETWORK order: pre-read transfer
 *       end < replay PUT issue, and post-read issue > replay PUT transfer end.
 *
 *   The counts that decide are taken from the FINAL recorders after every barrier:
 *   main 2, second tab 2, total 4, company B 0, read-only actor 0.
 *
 *   4 PUTs · 2 successful logical updates (v7→v8 deactivate, v8→v9 activate) ·
 *   2 active-state audit events · 3 durable unique keys (K1 Succeeded 204, KS
 *   VersionConflict 409, K2 Succeeded 204) · 1 replay of K1 (no new effect) ·
 *   0 re-deactivations · 0 NoChange audits · 0 new Party · 0 new account ·
 *   0 writes in company B · 0 writes by the read-only actor.
 *
 *   `accountStatusPuts` is the MAIN page's 2 (K1 original, K1 replay);
 *   `staleAccountStatusPuts` is the SECOND page's 2 (KS 409, K2). `seq` is page-local in
 *   each projection; the SQL side joins on key/company/actor/payload/outcome/response.
 *
 * ALSO MEASURED HERE: the real dirty-context defence (a typed reason, a company switch,
 * nothing carried into B and nothing written there), and a READ-ONLY actor who is
 * offered no status action and writes nothing.
 *
 * NOTHING IS MOCKED. Two deliveries are withheld, once each, on different pages: K1's
 * PUT on the main page and the post-K2 GET on the second tab — both after `route.fetch`
 * has sent the real request and read the real answer. The read-only context rewrites
 * ONE request header — the development identity — and nothing else.
 *
 * WHAT THIS IS NOT:
 *  - not a session revocation (the isolated Testing host authenticates by
 *    `X-Development-User` and offers no sign-out) — NOT_MEASURED;
 *  - not a server-side 403 for a status attempt by a reader: the action is never
 *    offered, so no PUT is made from the browser — Backend's own HTTP test is that
 *    evidence — NOT_MEASURED here;
 *  - not a NoChange same-state command; the journey never asks for the state the
 *    record already has;
 *  - not a re-run of L-01, L-02, L-03 or any closed C006 step. It shares their harness
 *    and nothing else, and the L-03 child-lifecycle result is not an account result.
 */
import { expect, test, type APIResponse, type BrowserContext, type Locator, type Page } from '@playwright/test'
import {
  DeliveryWithholder,
  LiveRecorder,
  evidenceErrorText,
  failurePriority,
  isCompletedRead,
  lShot,
  readManifest,
  writeLJson,
  type LiveRecord,
} from '../src/lHarness.js'

const manifest = readManifest()
const A = manifest.companyAId
const B = manifest.companyBId
const CONTRACT = manifest.clientContractVersion
const PREFIX = manifest.browserPrefix
const SLUG = 'l04-account-status-journey'
const ACTIVE_STATE_PATH_RE = /^\/api\/v1\/counterparties\/[^/]+\/company-account\/active-state$/
const DETAIL_PATH_RE = /^\/api\/v1\/counterparties\/[^/]+$/

const UNKNOWN_NOTICE = '[data-attempt="unknown"]'
const RETRY_BUTTON = 'Aynı işlemi yeniden dene'
const DEACTIVATE_ACTION = 'Bu şirkette pasife al'
const ACTIVATE_ACTION = 'Bu şirkette aktifleştir'
const DEACTIVATE_SUBMIT = 'Pasife al'
const ACTIVATE_SUBMIT = 'Aktifleştir'
const REFRESH_RECORD = 'Kaydı yenile'
/** The shared state block's own retry — the READ's, on the recovery branch. */
const READ_RETRY = 'Tekrar dene'
const SAVED_DEACTIVATED = 'Şirket hesabı pasife alındı'
const SAVED_ACTIVATED = 'Şirket hesabı aktifleştirildi'
const SUPERSEDED_TITLE = 'Geçmiş işlemin sonucu bildirildi'
const REFRESHED_NOTE = /Kayıt güncel sürümle yenilendi/
const DISCARD_AND_CLOSE = 'Gerekçeyi bırak ve kapat'

/**
 * The read-only actor, as the API owner named it for L-03: company A's
 * ORGANIZATION_OPERATOR, given `counterparty.parties.read` inside the Testing acceptance
 * profile and NOT `counterparty.accounts.status.update`. Backend's fixture pin for this
 * journey may name a different reader; the constant is the one place to change.
 */
const READ_ONLY_ACTOR = 'company-a-user'

/** The reasons this journey writes. Prefixed so every audit row is attributable to this run. */
const REASON_K1 = `${PREFIX} — bu şirket hesabındaki çalışmaya ara verildi.`
const REASON_K2 = `${PREFIX} — çalışma yeniden başladı.`
const REASON_ABANDONED = `${PREFIX} — terk edilecek gerekçe`

/** Set by the test so the afterEach hook can reach the recorders of the run that failed. */
let activeRecorder: LiveRecorder | null = null
let activeStaleRecorder: LiveRecorder | null = null

function accountStatusPuts(net: LiveRecorder): LiveRecord[] {
  return net.records.filter((r) => r.method === 'PUT' && ACTIVE_STATE_PATH_RE.test(r.path))
}

function detailGets(net: LiveRecorder, partyId: string): LiveRecord[] {
  return net.records.filter((r) => r.method === 'GET' && DETAIL_PATH_RE.test(r.path) && r.path.endsWith(`/${partyId}`))
}

/** One PUT, in the projection both pages use and the SQL side joins on. */
function putProjection(record: LiveRecord) {
  return {
    seq: record.seq,
    companyId: record.companyId,
    idempotencyKey: record.idempotencyKey,
    status: record.status,
    replayed: record.responseHeaders['idempotency-replayed'] ?? null,
    problemCode: (record.responseBody as { code?: unknown } | null)?.code ?? null,
    contractDeclaration: record.contractDeclaration,
    contractAck: record.contractAck,
    requestBody: record.requestBody,
  }
}

interface DetailBody {
  readonly id?: string
  readonly companyAccounts?: readonly { id?: string; companyId?: string; isActive?: boolean; version?: string }[]
}

/** The company-A account as ONE completed detail read carried it. */
function accountOf(body: DetailBody, companyId: string): { version: string | null; isActive: boolean | null } {
  const account = (body.companyAccounts ?? []).find((a) => a.companyId === companyId)
  return { version: account?.version ?? null, isActive: account?.isActive ?? null }
}

/**
 * The newest detail read that ACTUALLY ANSWERED after `sinceTick`, waited for with a
 * bound — DOM-ready is not recorder-ready (the L-03 lesson), and a capture that never
 * lands fails loudly instead of reporting an absence as a value.
 */
async function awaitDetailRead(
  page: Page,
  net: LiveRecorder,
  partyId: string,
  sinceTick: number,
  label: string,
): Promise<{ version: string | null; isActive: boolean | null }> {
  const deadline = Date.now() + 30_000
  for (;;) {
    const answered = detailGets(net, partyId).filter((r) => r.requestTick > sinceTick && isCompletedRead(r, CONTRACT))
    const newest = answered.at(-1)
    if (newest !== undefined) return accountOf((newest.responseBody ?? {}) as DetailBody, A)
    if (Date.now() > deadline) {
      const seen = detailGets(net, partyId)
        .filter((r) => r.requestTick > sinceTick)
        .map((r) => ({ seq: r.seq, status: r.status, transferOutcome: r.transferOutcome, bodyState: r.bodyState, ack: r.contractAck }))
      throw new Error(`${label}: no completed detail read landed in the recorder: ${JSON.stringify(seen)}`)
    }
    await page.waitForTimeout(100)
  }
}

/** One record matching `where`, waited for with a bound; the recorder is polled, never assumed. */
async function awaitRecord(
  page: Page,
  net: LiveRecorder,
  where: (r: LiveRecord) => boolean,
  label: string,
  timeoutMs = 30_000,
): Promise<LiveRecord> {
  const deadline = Date.now() + timeoutMs
  for (;;) {
    const found = net.records.filter(where).at(-1)
    if (found !== undefined) return found
    if (Date.now() > deadline) throw new Error(`${label}: no matching record landed in the recorder within ${timeoutMs} ms`)
    await page.waitForTimeout(100)
  }
}

/**
 * A withheld READ, spec-local: the harness's `DeliveryWithholder` selects its target by
 * the request's Idempotency-Key, which a detail GET does not carry, so it can never
 * target one (Dirac L-04 finding 1). This does for ONE detail GET exactly what the
 * harness does for a command: the real request goes to the real API via `route.fetch`
 * (no retry, no redirect), its real status, acknowledgment and body are READ, the loss
 * is reported only as a withheld success when the upstream answer is a 200 acknowledged
 * under the contract and carries THIS party with the company-A account — and only then
 * is the delivery cut with `route.abort`. Nothing is fulfilled or synthesised.
 */
interface WithheldRead {
  readonly marker: 'upstream_success_then_delivery_abort' | 'upstream_unexpected_result' | 'upstream_not_reached'
  readonly label: string
  readonly at: string
  readonly method: string
  readonly path: string
  readonly companyId: string | null
  readonly idempotencyKey: null
  readonly contractDeclaration: string | null
  readonly upstreamStatus: number
  readonly contractAck: string | null
  readonly partyId: string | null
  readonly account: { version: string | null; isActive: boolean | null } | null
  readonly deliveredToBrowser: false
  readonly browserOutcome: 'aborted-by-test'
  readonly upstreamError: string | null
}

async function withholdOneDetailRead(
  page: Page,
  partyId: string,
  companyId: string,
  label: string,
): Promise<{ readonly withheld: WithheldRead[]; readonly passedThrough: { at: string; method: string; path: string }[] }> {
  const state = { done: false, withheld: [] as WithheldRead[], passedThrough: [] as { at: string; method: string; path: string }[] }
  const targetPath = `/api/v1/counterparties/${partyId}`
  await page.route(
    (url) => url.pathname === targetPath,
    async (route) => {
      const request = route.request()
      const method = request.method().toUpperCase()
      const headers = request.headers()
      const company = headers['x-company-id'] ?? null
      if (state.done || method !== 'GET' || company !== companyId) {
        state.passedThrough.push({ at: new Date().toISOString(), method, path: targetPath })
        await route.continue()
        return
      }
      state.done = true
      const base = {
        label,
        at: new Date().toISOString(),
        method,
        path: targetPath,
        companyId: company,
        idempotencyKey: null,
        contractDeclaration: headers['x-erp-counterparty-contract'] ?? null,
        deliveredToBrowser: false as const,
        browserOutcome: 'aborted-by-test' as const,
      }
      let upstream: APIResponse
      try {
        upstream = await route.fetch({ maxRetries: 0, maxRedirects: 0 })
      } catch (error) {
        state.withheld.push({
          ...base,
          marker: 'upstream_not_reached',
          upstreamStatus: 0,
          contractAck: null,
          partyId: null,
          account: null,
          upstreamError: String((error as Error)?.message ?? error).slice(0, 200),
        })
        await route.abort('failed')
        return
      }
      const status = upstream.status()
      const ack = upstream.headers()['x-erp-counterparty-contract'] ?? null
      let body: DetailBody | null = null
      try {
        body = (await upstream.json()) as DetailBody
      } catch {
        body = null
      }
      const answeredParty = typeof body?.id === 'string' ? body.id : null
      const account = body === null ? null : accountOf(body, companyId)
      const expected = status === 200 && ack === CONTRACT && answeredParty === partyId && account?.version !== null
      state.withheld.push({
        ...base,
        marker: expected ? 'upstream_success_then_delivery_abort' : 'upstream_unexpected_result',
        upstreamStatus: status,
        contractAck: ack,
        partyId: answeredParty,
        account,
        upstreamError: expected
          ? null
          : `upstream answered ${status} with ack ${String(ack)} for party ${String(answeredParty)}; the expected 200 + acknowledgment + this party's company account was not seen`,
      })
      // Only the delivery is cut. The server has already answered.
      await route.abort('failed')
    },
  )
  return state
}

function statusDialog(page: Page): Locator {
  return page.locator('[role="dialog"][data-intent]')
}

function reasonField(page: Page): Locator {
  return page.locator('#cari-account-status-reason')
}

function cardAccountState(page: Page): Locator {
  return page.locator('[data-section="account"] [data-account-state]')
}

/** Open the card's status window and type the reason; nothing is sent yet. */
async function openStatusWindow(page: Page, action: string, reason: string): Promise<void> {
  await page.getByRole('button', { name: action }).click({ timeout: 30_000 })
  await expect(statusDialog(page)).toBeVisible({ timeout: 30_000 })
  await reasonField(page).fill(reason, { timeout: 30_000 })
}

/**
 * Open the card from the LIST by KEYBOARD: Tab to the row link and Enter. Bounded — if
 * the link is not reachable the loop ends and the assertion says so.
 */
async function openCardByKeyboard(page: Page, partyId: string): Promise<number> {
  let tabs = 0
  let reached = false
  while (tabs < 80) {
    await page.keyboard.press('Tab')
    tabs += 1
    const focusedHref = await page.evaluate(
      () => (document.activeElement as HTMLAnchorElement | null)?.getAttribute('href') ?? null,
    )
    if (focusedHref === `/cari/${partyId}`) {
      reached = true
      break
    }
  }
  expect(reached, `the card link must be reachable from the list by keyboard (tried ${tabs} tab stops)`).toBe(true)
  await page.keyboard.press('Enter')
  await expect(page.locator('[data-section="account"]')).toBeVisible({ timeout: 30_000 })
  return tabs
}

/**
 * What the CURRENT company's card actually resolved to — a real ANSWER, observed before
 * anything is asserted about it: a finished detail transfer with a status and the screen
 * that status implies, or — where the server granted no capability — the neutral block
 * and no detail read at all.
 */
async function awaitCardTerminal(
  page: Page,
  net: LiveRecorder,
  partyId: string,
  companyId: string,
  sinceTick: number,
  label: string,
): Promise<{ readonly status: number | null; readonly closedByCapability: boolean; readonly cardRendered: boolean }> {
  const deadline = Date.now() + 45_000
  for (;;) {
    const answered = detailGets(net, partyId)
      .filter((r) => r.requestTick > sinceTick && r.companyId === companyId && r.transferOutcome === 'finished' && r.status !== null)
      .at(-1)
    const cardRendered = (await page.locator('[data-section="account"]').count()) > 0
    const stateBlock = (await page.locator('[data-state]').count()) > 0
    const neutralBlock = (await page.locator('[data-state="not-found"]').count()) > 0
    if (net.outstanding().length === 0) {
      if (answered !== undefined && answered.status === 200 && cardRendered) {
        return { status: 200, closedByCapability: false, cardRendered: true }
      }
      if (answered !== undefined && answered.status !== 200 && stateBlock) {
        return { status: answered.status, closedByCapability: false, cardRendered: false }
      }
      if (answered === undefined && neutralBlock) {
        return { status: null, closedByCapability: true, cardRendered: false }
      }
    }
    if (Date.now() > deadline) {
      throw new Error(
        `${label}: no terminal outcome for company ${companyId}: outstanding=${JSON.stringify(net.outstanding())} cardRendered=${cardRendered} stateBlock=${stateBlock}`,
      )
    }
    await page.waitForTimeout(100)
  }
}

/** A company switch, waited for HONESTLY: every request it issued reached a terminal state. */
async function switchCompany(page: Page, net: LiveRecorder, companyId: string, discardDraft: boolean): Promise<void> {
  const beforeAction = net.currentTick()
  const picker = page.getByRole('combobox', { name: 'Şirket', exact: true })
  await picker.selectOption(companyId)
  // The SHELL's switch confirmation, not the status window: the status window carries
  // `data-intent`, the shell dialog does not.
  const dialog = page.locator('[role="dialog"]:not([data-intent])')
  /**
   * A REAL click, bounded. No `force`: if the shell's question is painted under the
   * feature window, a user cannot answer it and neither may this journey — the first
   * real run (c006-7f12ed40ff9a) found exactly that and, with no action timeout, spent
   * its whole 600 s budget retrying the click. Thirty seconds says the same thing honestly.
   */
  if (discardDraft) {
    await expect(dialog.first(), 'an unsaved reason must be defended by a real warning').toBeVisible({ timeout: 30_000 })
    await dialog.first().getByRole('button', { name: 'Değişiklikleri bırak' }).click({ timeout: 30_000 })
  } else if (await dialog.count()) {
    await dialog.first().getByRole('button', { name: 'Değişiklikleri bırak' }).click({ timeout: 30_000 })
  }
  await expect(picker).toHaveValue(companyId, { timeout: 30_000 })
  const settled = await net.settleSince(beforeAction, { timeoutMs: 20_000, contractVersion: CONTRACT })
  expect(
    settled.outstanding,
    `the reads this switch issued must reach a terminal state: ${JSON.stringify(settled.outstanding)}`,
  ).toEqual([])
  expect(settled.settled).toBe(true)
}

/** Evidence survives failure here too; a diagnostic that cannot be written stays silent. */
// oxlint-disable-next-line no-empty-pattern -- required by the Playwright hook signature
test.afterEach(async ({}, testInfo) => {
  if (testInfo.status === 'passed') return
  try {
    writeLJson('pending-state', `${SLUG}-outstanding.json`, {
      testStatus: testInfo.status ?? null,
      testError: testInfo.error?.message ?? null,
      main:
        activeRecorder === null
          ? null
          : { observedEvents: activeRecorder.observedEvents(), outstanding: activeRecorder.outstanding(), records: activeRecorder.records },
      stale:
        activeStaleRecorder === null
          ? null
          : { observedEvents: activeStaleRecorder.observedEvents(), outstanding: activeStaleRecorder.outstanding(), records: activeStaleRecorder.records },
      meaning:
        'RAW RECORDER STATE ONLY, captured after a failed run from an afterEach hook so a failure anywhere in the journey still leaves the request/capture state on disk. NOT the full record: the measurement file and the withheld-upstream entries are written by the main path, and their absence here does not mean they were preserved.',
    })
  } catch {
    // Deliberately swallowed: the run already has a real failure and it owns the report.
  }
})

test('L-04 C-03: the selected company account deactivated for a reason and reactivated for another, v7→v9 across two tabs — a withheld 204 held unknown, a real stale-fence 409 refreshed with the reason kept, the passive account seen in the list and the card reopened by keyboard, a withheld post-204 read recovered by a read, the old key replayed AFTER reactivation without re-deactivating, a defended reason on company change, and a read-only actor offered nothing', async ({
  page,
  context,
  browser,
}, testInfo) => {
  test.setTimeout(600_000)
  expect(testInfo.retry).toBe(0)

  await page.addInitScript((id: string) => {
    if (window.localStorage.getItem('goymen-company-id') === null) window.localStorage.setItem('goymen-company-id', id)
  }, A)
  const net = LiveRecorder.attach(page)
  activeRecorder = net

  /**
   * The main page withholds exactly ONE delivery: K1's PUT. Its success is a body-less
   * 204, so the target says so.
   */
  const withholder = await DeliveryWithholder.install(page, [
    {
      label: 'account-status-k1',
      method: 'PUT',
      companyId: A,
      match: (p) => ACTIVE_STATE_PATH_RE.test(p),
      expectStatus: 204,
      requireIdentity: false,
    },
  ])

  // ---- 0. the target, taken from the authorized list -------------------------------
  await page.goto('/cari')
  await expect(page.getByRole('heading', { level: 1, name: 'Cari Kartlar' })).toBeVisible({ timeout: 60_000 })
  const listRead = await net.settleSince(0, {
    timeoutMs: 20_000,
    expectCompleted: (r) => r.companyId === A && r.method === 'GET' && r.path === '/api/v1/counterparties',
    minimumCompleted: 1,
    contractVersion: CONTRACT,
  })
  expect(listRead.completedExpected, 'the list must answer before this journey picks a target').toBe(true)
  const listed =
    (
      net.records.find((r) => r.method === 'GET' && r.path === '/api/v1/counterparties' && isCompletedRead(r, CONTRACT))
        ?.responseBody as { items?: { id?: string; code?: string }[] } | null
    )?.items ?? []
  expect(
    listed.length,
    'this journey changes the state of an account the environment already holds and creates none; company A listed no record',
  ).toBeGreaterThan(0)
  const partyId = String(listed[0].id)
  const partyCode = String(listed[0].code)

  // ---- 1. the card, at the fixture's version 7, ACTIVE ------------------------------
  const beforeCard = net.currentTick()
  await page.getByRole('row', { name: new RegExp(partyCode) }).getByRole('link').first().click()
  await expect(page.locator('[data-section="account"]')).toBeVisible({ timeout: 30_000 })
  const v7 = await awaitDetailRead(page, net, partyId, beforeCard, 'card seed')
  expect(v7.version, 'the fixture account starts at version 7').toBe('7')
  expect(v7.isActive, 'the fixture account starts ACTIVE').toBe(true)
  await expect(cardAccountState(page)).toHaveAttribute('data-account-state', 'active')
  const deactivate = page.getByRole('button', { name: DEACTIVATE_ACTION })
  await expect(
    deactivate,
    'the card must offer the action: without the server-granted status control this journey cannot run',
  ).toBeVisible({ timeout: 30_000 })
  await expect(deactivate).toBeEnabled()
  await lShot(page, SLUG, '01-card-v7-active')

  /**
   * The second tab, opened NOW — before K1 — with its status window opened on fence 7,
   * so the conflict later is genuinely a stale fence and not something arranged after
   * the fact. Its records stay in their own projection.
   */
  const stale = await context.newPage()
  const staleNet = LiveRecorder.attach(stale)
  activeStaleRecorder = staleNet
  await stale.goto(`/cari/${partyId}`)
  await expect(stale.locator('[data-section="account"]')).toBeVisible({ timeout: 60_000 })
  const staleSeed = await awaitDetailRead(stale, staleNet, partyId, 0, 'stale tab seed')
  expect(staleSeed.version, 'the second tab is seeded on the same version 7').toBe('7')
  await openStatusWindow(stale, DEACTIVATE_ACTION, REASON_K2)

  // ---- 2. K1: deactivate with a reason, the reply withheld -------------------------
  await openStatusWindow(page, DEACTIVATE_ACTION, REASON_K1)
  await expect(statusDialog(page)).toHaveAttribute('data-intent', 'deactivate')
  await statusDialog(page).getByRole('button', { name: DEACTIVATE_SUBMIT }).click()

  const unknown = page.locator(UNKNOWN_NOTICE)
  await expect(unknown, 'a withheld reply must leave the outcome UNKNOWN on screen').toBeVisible({ timeout: 60_000 })
  await expect(unknown.getByRole('button', { name: RETRY_BUTTON })).toBeVisible()
  // The change may have been applied. The screen must not say it was.
  await expect(page.getByText(SAVED_DEACTIVATED)).toHaveCount(0)
  await expect(cardAccountState(page), 'the OLD read stays on screen and is not called fresh').toHaveAttribute(
    'data-account-state',
    'active',
  )
  await lShot(page, SLUG, '02-k1-unknown-after-withheld')

  const withheld = withholder.of('account-status-k1')
  expect(withheld, 'exactly one delivery is withheld on the main page').toHaveLength(1)
  expect(
    withheld[0].marker,
    `the withheld exchange must be a real upstream success before its delivery was cut: ${String(withheld[0].upstreamError)}`,
  ).toBe('upstream_success_then_delivery_abort')
  expect(withheld[0].upstreamStatus).toBe(204)
  expect(withheld[0].contractAck).toBe(CONTRACT)
  const k1Key = withheld[0].idempotencyKey
  expect(k1Key).not.toBeNull()
  const k1Sent = accountStatusPuts(net)[0]
  expect(k1Sent.requestBody, 'K1 is exactly the contract body on fence 7').toEqual({
    isActive: false,
    reason: REASON_K1,
    expectedVersion: '7',
  })

  // ---- 3. the second tab: a real stale fence, refreshed with the reason kept -------
  await statusDialog(stale).getByRole('button', { name: DEACTIVATE_SUBMIT }).click()
  const conflict = stale.locator('[data-failure="version-conflict"]')
  await expect(
    conflict,
    'this window was opened on version 7 and the record is now at 8: the fence is genuinely stale',
  ).toBeVisible({ timeout: 60_000 })
  expect(accountStatusPuts(staleNet).length, 'a definite conflict is not retried under the same key').toBe(1)
  const conflictPut = accountStatusPuts(staleNet)[0]
  expect(conflictPut.status).toBe(409)
  expect(conflictPut.requestBody).toEqual({ isActive: false, reason: REASON_K2, expectedVersion: '7' })
  expect(
    (conflictPut.responseHeaders['idempotency-replayed'] ?? '').toLowerCase(),
    'a first-run conflict is not a replay',
  ).not.toBe('true')
  // The reason survives the refusal — the user's own words are still on screen.
  await expect(reasonField(stale)).toHaveValue(REASON_K2)
  await lShot(stale, SLUG, '03-stale-version-conflict')

  const beforeRefresh = staleNet.currentTick()
  await conflict.getByRole('button', { name: REFRESH_RECORD }).click()
  const refreshed = await awaitDetailRead(stale, staleNet, partyId, beforeRefresh, 'stale refresh')
  expect(refreshed.version, 'the refresh must adopt the version the record actually has').toBe('8')
  expect(refreshed.isActive, 'K1 was applied by the server even though its reply never reached the main page').toBe(false)
  await expect(stale.getByText(REFRESHED_NOTE), 'the user is told the record moved and their reason was kept').toBeVisible({
    timeout: 30_000,
  })
  await expect(reasonField(stale)).toHaveValue(REASON_K2)
  // The window now shows what the record IS: passive — so the intent it offers is activation.
  await expect(statusDialog(stale)).toHaveAttribute('data-intent', 'activate')
  await expect(cardAccountState(stale), 'the fresh read is what the card shows').toHaveAttribute('data-account-state', 'inactive')
  await lShot(stale, SLUG, '04-stale-refreshed-v8-passive')

  // ---- 4. the list shows the passive account; the card is reopened by keyboard -----
  // Closing the window with a typed reason asks first; the reason is discarded on purpose.
  await stale.keyboard.press('Escape')
  await stale.getByRole('button', { name: DISCARD_AND_CLOSE }).click()
  await expect(statusDialog(stale)).toHaveCount(0)
  await stale.getByRole('link', { name: /Cari Kartlar listesine dön/ }).click()
  await expect(stale.getByRole('heading', { level: 1, name: 'Cari Kartlar' })).toBeVisible({ timeout: 30_000 })
  const row = stale.getByRole('row', { name: new RegExp(partyCode) })
  await expect(row, 'the passive account is still LISTED — passive is a state, not a deletion').toBeVisible({ timeout: 30_000 })
  await expect(row.locator('td[data-account-state]'), 'the list shows the company account passive').toHaveAttribute(
    'data-account-state',
    'inactive',
  )
  await lShot(stale, SLUG, '05-list-passive-row')
  const beforeReopen = staleNet.currentTick()
  const tabsToCard = await openCardByKeyboard(stale, partyId)
  const reopened = await awaitDetailRead(stale, staleNet, partyId, beforeReopen, 'reopen by keyboard')
  expect(reopened.version).toBe('8')
  expect(reopened.isActive).toBe(false)
  await expect(stale.getByRole('button', { name: ACTIVATE_ACTION })).toBeEnabled({ timeout: 30_000 })

  // ---- 5. K2: reactivate for a separate reason; the read it owes is withheld ------
  /**
   * The second tab withholds exactly ONE delivery: the detail GET the 204 triggers. The
   * command itself is delivered normally; what the browser loses is the READ — the case
   * where the change is applied and the screen must say "saved, view not refreshed" and
   * offer the READ's own retry, never a second PUT. The withholding is spec-local (see
   * `withholdOneDetailRead`): a GET carries no Idempotency-Key, so the harness's
   * command withholder cannot target it.
   */
  const staleWithholder = await withholdOneDetailRead(stale, partyId, A, 'post-k2-detail-read')
  await openStatusWindow(stale, ACTIVATE_ACTION, REASON_K2)
  await expect(statusDialog(stale)).toHaveAttribute('data-intent', 'activate')
  const beforeK2 = staleNet.currentTick()
  await statusDialog(stale).getByRole('button', { name: ACTIVATE_SUBMIT }).click()

  /**
   * The terminal event FIRST: the recorder must have seen the post-204 GET FAIL (its
   * delivery was cut), and the withholder must have read a real, acknowledged 200 for
   * this party before cutting it. Only then is the screen asked what it shows — a
   * pending or paused view caught mid-flight would make this a race.
   */
  const lostRead = await awaitRecord(
    stale,
    staleNet,
    (r) => r.method === 'GET' && DETAIL_PATH_RE.test(r.path) && r.path.endsWith(`/${partyId}`) && r.requestTick > beforeK2 && r.transferOutcome === 'failed',
    'the withheld post-K2 read (requestfailed)',
    60_000,
  )
  expect(lostRead.status, 'a cut delivery has no status in the browser').toBeNull()
  const withheldRead = staleWithholder.withheld
  expect(withheldRead, 'exactly one read delivery is withheld on the second tab').toHaveLength(1)
  expect(
    withheldRead[0].marker,
    `the withheld read must be a real, acknowledged upstream success before its delivery was cut: ${String(withheldRead[0].upstreamError)}`,
  ).toBe('upstream_success_then_delivery_abort')
  expect(withheldRead[0].upstreamStatus).toBe(200)
  expect(withheldRead[0].contractAck).toBe(CONTRACT)
  expect(withheldRead[0].partyId).toBe(partyId)
  // The answer the browser never received already carried the applied state: v9, ACTIVE.
  expect(withheldRead[0].account?.version, 'K2 was applied before the read was lost').toBe('9')
  expect(withheldRead[0].account?.isActive).toBe(true)
  expect(accountStatusPuts(staleNet).length, 'K2 is one PUT; the lost READ is never answered with a second PUT').toBe(2)
  const k2 = accountStatusPuts(staleNet)[1]
  expect(k2.status).toBe(204)
  expect(k2.requestBody).toEqual({ isActive: true, reason: REASON_K2, expectedVersion: '8' })
  expect(k2.idempotencyKey, 'a new intent is a NEW attempt with its own key').not.toBe(conflictPut.idempotencyKey)

  // The application's REAL branch for a failed post-204 read: the card leaves `ready`,
  // says "saved, view not refreshed", and the retry it offers is the read's own.
  const readRecovery = stale.locator('[data-state="read-recovery"]')
  await expect(readRecovery, 'applied, and the read it owes failed: said so, not "saved"').toBeVisible({ timeout: 60_000 })
  await expect(stale.locator('[data-state="network-error"]'), 'the failed read is reported as what it was').toBeVisible({
    timeout: 30_000,
  })
  await expect(stale.getByText(SAVED_ACTIVATED)).toHaveCount(0)
  expect(await stale.getByRole('button', { name: ACTIVATE_ACTION }).count(), 'no action is mounted on the recovery branch').toBe(0)
  await lShot(stale, SLUG, '06-k2-applied-read-lost')

  const beforeReadAgain = staleNet.currentTick()
  await stale.getByRole('button', { name: READ_RETRY }).click()
  const v9 = await awaitDetailRead(stale, staleNet, partyId, beforeReadAgain, 'read retry after K2')
  expect(v9.version, 'K2 is one logical update').toBe('9')
  expect(v9.isActive).toBe(true)
  const savedActivated = stale.locator('[data-state="account-status-saved"]')
  await expect(savedActivated).toBeVisible({ timeout: 30_000 })
  await expect(savedActivated).toHaveAttribute('data-agrees', 'yes')
  await expect(savedActivated.getByText(SAVED_ACTIVATED)).toBeVisible()
  await expect(cardAccountState(stale)).toHaveAttribute('data-account-state', 'active')
  await lShot(stale, SLUG, '07-k2-adopted-v9-active')
  /**
   * The barrier FIRST, the count AFTER it: a PUT that started after an early count and
   * finished inside the quiet window would have entered the record while every later
   * assertion still passed. The count that decides is the one taken once the page has
   * gone quiet (Dirac L-04 finding 3).
   */
  const staleBarrier = await staleNet.quiesce({ settleMs: 1500, timeoutMs: 45_000 })
  expect(accountStatusPuts(staleNet).length, 'AFTER the barrier: the second tab carries exactly two PUTs, the 409 and K2').toBe(2)
  const stalePutProjection = accountStatusPuts(staleNet).map(putProjection)
  const staleGetProjection = detailGets(staleNet, partyId).map((r) => ({
    seq: r.seq,
    companyId: r.companyId,
    status: r.status,
    completed: isCompletedRead(r, CONTRACT),
    account: accountOf((r.responseBody ?? {}) as DetailBody, A),
  }))
  const staleObserved = staleNet.observedEvents()
  await stale.close()

  // ---- 6. K1': the old key replayed AFTER the reactivation ------------------------
  const beforeK1Retry = net.currentTick()
  await unknown.getByRole('button', { name: RETRY_BUTTON }).click()
  const supersededSaved = page.locator('[data-state="account-status-saved"]')
  await expect(supersededSaved, 'the same-key retry must resolve the attempt').toBeVisible({ timeout: 60_000 })
  const afterK1 = accountStatusPuts(net)
  expect(afterK1.length, 'the main page carries K1 and its one same-key retry').toBe(2)
  expect(afterK1[1].idempotencyKey, 'the retry must carry the SAME key, never a new one').toBe(k1Key)
  expect(JSON.stringify(afterK1[1].requestBody), 'the retry must carry the identical payload').toBe(
    JSON.stringify(afterK1[0].requestBody),
  )
  expect(afterK1[1].status).toBe(204)
  expect(
    (afterK1[1].responseHeaders['idempotency-replayed'] ?? '').toLowerCase(),
    'the server itself must say this was a replay of the stored result, not a second effect',
  ).toBe('true')

  /**
   * THREE exchanges, in NETWORK order (Dirac L-04 finding 4): the fresh read that
   * licenses the retry must have FINISHED its transfer before the replay PUT was
   * issued, and a SEPARATE read must have been issued after the replay's transfer
   * finished. Ordered on `transferTick`/`requestTick` — never on the recorder's own
   * capture tick, which belongs to the recorder. Both reads must be completed
   * (acknowledged, body captured) before their content is read.
   */
  /**
   * A body-less 204 has NO `requestfinished` in this recorder: Chromium closes the
   * empty body stream after the headers and Playwright reports the exchange as
   * `requestfailed` / `net::ERR_ABORTED` — every 204 of L-03 (seq 17–25 of its network
   * capture) and both 204s of the first real L-04 run (c006-5b7b30940e9a) look exactly
   * like that, headers, status, acknowledgment and replay marker all present. So the
   * PUT's terminal transfer is its `transferTick` under EITHER outcome, and what makes
   * it a real answer is the 204 + acknowledgment read from its headers — which the
   * assertions above already required. Requiring `finished` here made the first real run
   * time out AFTER the server and the screen had done exactly what this step measures.
   */
  const replayPut = await awaitRecord(
    page,
    net,
    (r) => r.seq === afterK1[1].seq && r.transferTick !== null && r.status === 204 && r.contractAck === CONTRACT,
    'the replay PUT’s transfer end',
  )
  const isCompletedDetailRead = (r: LiveRecord) =>
    r.method === 'GET' && DETAIL_PATH_RE.test(r.path) && r.path.endsWith(`/${partyId}`) && r.companyId === A && isCompletedRead(r, CONTRACT)
  const preReplayRead = await awaitRecord(
    page,
    net,
    (r) => isCompletedDetailRead(r) && r.requestTick > beforeK1Retry && r.transferTick !== null && r.transferTick < replayPut.requestTick,
    'the pre-replay read (finished before the PUT was issued)',
  )
  const postReplayRead = await awaitRecord(
    page,
    net,
    (r) => isCompletedDetailRead(r) && r.requestTick > (replayPut.transferTick ?? Number.POSITIVE_INFINITY),
    'the post-replay read (issued after the replay answered)',
  )
  expect(preReplayRead.seq, 'two DIFFERENT reads bracket the replay').not.toBe(postReplayRead.seq)
  expect(preReplayRead.transferTick!, 'pre-replay read: transfer ended before the PUT went out').toBeLessThan(replayPut.requestTick)
  expect(postReplayRead.requestTick, 'post-replay read: issued only after the replay answered').toBeGreaterThan(replayPut.transferTick!)
  const preReplay = accountOf((preReplayRead.responseBody ?? {}) as DetailBody, A)
  const afterReplay = accountOf((postReplayRead.responseBody ?? {}) as DetailBody, A)
  expect(preReplay.version, 'the licensing read already saw K2’s state').toBe('9')
  expect(afterReplay.version, 'the old key re-deactivates NOTHING: the record stays where K2 left it').toBe('9')
  expect(afterReplay.isActive).toBe(true)
  const k1ReplayOrdering = {
    preRead: { seq: preReplayRead.seq, requestTick: preReplayRead.requestTick, transferTick: preReplayRead.transferTick, account: preReplay },
    put: { seq: replayPut.seq, requestTick: replayPut.requestTick, transferTick: replayPut.transferTick, status: replayPut.status },
    postRead: { seq: postReplayRead.seq, requestTick: postReplayRead.requestTick, transferTick: postReplayRead.transferTick, account: afterReplay },
  }
  // The result is the fresh read's, not the old intent's.
  await expect(supersededSaved).toHaveAttribute('data-agrees', 'no')
  await expect(supersededSaved.getByText(SUPERSEDED_TITLE)).toBeVisible()
  await expect(supersededSaved.getByText(SAVED_DEACTIVATED)).toHaveCount(0)
  await expect(supersededSaved.getByText(/yalnız aktif hesaplar süzülüyorsa/)).toHaveCount(0)
  await expect(cardAccountState(page)).toHaveAttribute('data-account-state', 'active')
  await lShot(page, SLUG, '08-k1-replayed-superseded-v9-active')

  // ---- 7. a typed reason is defended when the company changes ----------------------
  await openStatusWindow(page, DEACTIVATE_ACTION, REASON_ABANDONED)
  const putsBeforeB = accountStatusPuts(net).length
  const beforeB = net.currentTick()
  await switchCompany(page, net, B, true)
  const bTerminal = await awaitCardTerminal(page, net, partyId, B, beforeB, 'company B card')
  expect(await statusDialog(page).count(), 'no window and no reason from company A survives into company B').toBe(0)
  const bDialogReason = (await reasonField(page).count()) > 0 ? await reasonField(page).inputValue() : null
  expect(bDialogReason).not.toBe(REASON_ABANDONED)
  expect(accountStatusPuts(net).filter((r) => r.companyId === B), 'this journey writes nothing in company B').toEqual([])
  expect(accountStatusPuts(net).length).toBe(putsBeforeB)
  await lShot(page, SLUG, '09-company-b')

  // ---- 8. a READ-ONLY actor is offered nothing and writes nothing -----------------
  const readOnly: BrowserContext = await browser.newContext()
  const readOnlyPage = await readOnly.newPage()
  const readOnlyNet = LiveRecorder.attach(readOnlyPage)
  await readOnlyPage.addInitScript((id: string) => {
    window.localStorage.setItem('goymen-company-id', id)
  }, A)
  await readOnlyPage.route('**/api/v1/**', async (route) => {
    await route.continue({ headers: { ...route.request().headers(), 'x-development-user': READ_ONLY_ACTOR } })
  })
  await readOnlyPage.goto(`/cari/${partyId}`)
  await expect(readOnlyPage.locator('[data-section="account"]'), 'a reader may READ the card').toBeVisible({ timeout: 60_000 })
  await expect(cardAccountState(readOnlyPage)).toHaveAttribute('data-account-state', 'active')
  // The action is not offered: a control the server did not grant is not advertised.
  await expect(readOnlyPage.getByRole('button', { name: DEACTIVATE_ACTION })).toHaveCount(0)
  await expect(readOnlyPage.getByRole('button', { name: ACTIVATE_ACTION })).toHaveCount(0)
  expect(accountStatusPuts(readOnlyNet), 'a read-only actor writes nothing').toEqual([])
  await lShot(readOnlyPage, SLUG, '10-read-only-card')
  const readOnlyBarrier = await readOnlyNet.quiesce({ settleMs: 1000, timeoutMs: 30_000 })
  expect(accountStatusPuts(readOnlyNet), 'AFTER the barrier: the read-only actor wrote nothing').toEqual([])
  const readOnlyRecords = readOnlyNet.records.map((r) => ({
    seq: r.seq,
    method: r.method,
    path: r.path,
    companyId: r.companyId,
    status: r.status,
    devIdentityHeader: r.devIdentityHeader,
  }))
  await readOnly.close()

  // ---- 9. the barrier, and the evidence BEFORE the verdict ------------------------
  const barrier = await net.quiesce({ settleMs: 1500, timeoutMs: 45_000 })
  /**
   * The counts that DECIDE, taken from the final recorders after every barrier: a
   * write that landed inside a quiet window is in these numbers, and would fail them.
   * Asserted after the evidence write below, so a wrong count still leaves its evidence.
   */
  const finalCounts = {
    main: accountStatusPuts(net).length,
    mainInCompanyB: accountStatusPuts(net).filter((r) => r.companyId === B).length,
    stale: accountStatusPuts(staleNet).length,
    readOnly: accountStatusPuts(readOnlyNet).length,
    total: accountStatusPuts(net).length + accountStatusPuts(staleNet).length + accountStatusPuts(readOnlyNet).length,
  }
  let evidenceError: unknown = null
  try {
    writeLJson('measurement', `${SLUG}-network.json`, {
      phase: 'measurement',
      slug: SLUG,
      runId: manifest.runId,
      instanceId: manifest.instanceId,
      sourceContract: 'SetCounterpartyCompanyAccountActiveStateV1 at main@fc14554d1e1e1e495c43d3ad4aa4a5757ea3a578',
      businessAcceptance: 'PR #232 comment 5647422648',
      handover: 'PR #250 comment 5648104588',
      partyId,
      partyCode,
      companyA: A,
      companyB: B,
      readOnlyActor: READ_ONLY_ACTOR,
      versions: { start: '7', afterK1: '8', afterK2: '9', afterK1Replay: '9' },
      expected: {
        puts: 4,
        successfulLogicalUpdates: 2,
        auditEvents: 2,
        noChangeAudits: 0,
        durableUniqueKeys: 3,
        durableOutcomes: { succeeded204: 2, versionConflict409: 1 },
        replays: 1,
        reDeactivations: 0,
        newParties: 0,
        newAccounts: 0,
        companyBWrites: 0,
        readOnlyWrites: 0,
      },
      /** MAIN page: K1 original (withheld), K1 replay. `seq` is page-local. */
      accountStatusPuts: accountStatusPuts(net).map(putProjection),
      /** SECOND page: the stale 409 and K2. Same projection; `seq` is page-local. */
      staleAccountStatusPuts: stalePutProjection,
      detailGets: detailGets(net, partyId).map((r) => ({
        seq: r.seq,
        companyId: r.companyId,
        status: r.status,
        completed: isCompletedRead(r, CONTRACT),
        account: accountOf((r.responseBody ?? {}) as DetailBody, A),
      })),
      staleDetailGets: staleGetProjection,
      withheld: { main: withholder.withheld, staleRead: staleWithholder.withheld },
      passedThrough: { main: withholder.passedThrough, staleRead: staleWithholder.passedThrough },
      k1ReplayOrdering,
      finalCounts,
      keyboardTabStopsToCard: tabsToCard,
      companyBTerminal: bTerminal,
      readOnly: { actor: READ_ONLY_ACTOR, records: readOnlyRecords, barrier: readOnlyBarrier },
      barriers: { main: barrier, stale: staleBarrier, readOnly: readOnlyBarrier },
      barrier,
      observedEvents: net.observedEvents(),
      staleObservedEvents: staleObserved,
      boundaries: [
        'No Party and no company account was created: ExpectedNewParties and ExpectedNewAccounts are 0 for this journey.',
        'Two successful logical state changes against ONE account in company A, v7 to v9: K1 deactivate, K2 activate. The database effect (rows, audits, durable command results) is measured by the environment snapshot, not by this browser.',
        'Only account.isActive of the selected company account changed. The shared Party, its other-company account, its contact profile and its roles are expected unchanged; that expectation is verified by the snapshot, not asserted here.',
        'The replay of K1 after K2 is a STORED 204 handed back by the server (Idempotency-Replayed). It re-deactivated nothing; the fresh read at v9 ACTIVE is the proof the browser has, and the audit/durable counts are the proof the snapshot has.',
        'No NoChange command was issued: the journey never asked for the state the record already had. The NoChange new-key audit semantics are Backend-side and are not the L-03 no-op audit-0 rule.',
        'Two deliveries were withheld, once each: K1\'s PUT on the main page and the post-K2 detail GET on the second tab. Both were real requests whose real answers were read before delivery was cut. Nothing was mocked and no response was ever synthesised.',
        'The read-only context rewrites ONLY the X-Development-User header on its own API requests; the requests and the answers are real. No status PUT was attempted by it, so a server-side 403 for a reader is NOT measured here — Backend\'s own HTTP test is that evidence.',
        'A session revocation is NOT measured anywhere in this journey: the isolated Testing host authenticates by X-Development-User and offers no sign-out.',
        'The company-B step records what was observed. It does not claim B holds or lacks an account for this Party; it asserts only that no window or reason from A appeared there and that nothing was written.',
        'seq is page-local in each projection. Joining the browser and the SQL evidence is done on key/company/actor/payload/outcome/response, not on seq.',
        'Every browser context this journey opened has its own quiescent barrier, and all three are part of the verdict.',
        'Closed C006, L-01, L-02 and L-03 evidence is not reused for this journey and is not re-run by it; the L-03 child-lifecycle result is not an account-lifecycle result.',
      ],
      finalTestResult: 'NOT_DECIDED_AT_WRITE_TIME',
      statusAtWrite: testInfo.status ?? null,
    })
  } catch (error) {
    evidenceError = error
  }
  await net.finish(testInfo, SLUG)

  const allBarriers = [
    { name: 'main', barrier },
    { name: 'stale tab', barrier: staleBarrier },
    { name: 'read-only', barrier: readOnlyBarrier },
  ] as const
  const priority = failurePriority(
    allBarriers.every((entry) => entry.barrier.accepted),
    evidenceError,
  )
  const secondary =
    priority.secondary === 'evidence' ? ` — SECONDARY: the evidence write also failed: ${evidenceErrorText(evidenceError)}` : ''
  for (const entry of allBarriers) {
    expect(
      entry.barrier.outstanding,
      `${entry.name}: the browser did not go quiet; ${entry.barrier.outstanding.length} request(s) unaccounted for: ${JSON.stringify(entry.barrier.outstanding, null, 1)}${secondary}`,
    ).toEqual([])
  }
  for (const entry of allBarriers) {
    expect(
      entry.barrier.accepted,
      `${entry.name}: the barrier was not accepted (timedOut=${entry.barrier.timedOut}, quietWindowHeld=${entry.barrier.quietWindowHeld}, markedBusy=${entry.barrier.markedBusyAtDecision})${secondary}`,
    ).toBe(true)
  }
  if (priority.primary === 'evidence') {
    throw new Error(`the barrier held but the mandatory evidence write failed: ${evidenceErrorText(evidenceError)}`)
  }

  // The FINAL counts, after every barrier and after the evidence is on disk.
  expect(finalCounts.main, 'final: the main page issued exactly K1 and its replay').toBe(2)
  expect(finalCounts.stale, 'final: the second tab issued exactly the 409 and K2').toBe(2)
  expect(finalCounts.total, 'final: four status PUTs in the whole journey, no more').toBe(4)
  expect(finalCounts.mainInCompanyB, 'final: nothing was written in company B').toBe(0)
  expect(finalCounts.readOnly, 'final: the read-only actor wrote nothing').toBe(0)
})
