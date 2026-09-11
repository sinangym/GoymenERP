/**
 * L-01 — the LIVE A→B journey: real browser, real API (C-DESKTOP-006 loopback
 * environment), real SQL Server behind it.
 *
 * G-SUPPORT-004 row 1 (+3 for the terminal pre-checks on B) and G-002 checks 1, 2, 3,
 * 5, 10: A create → A list → A detail; under B the new Party is invisible in the list
 * and its detail closes 404 before the link; a FRESH authorized candidate read offers
 * it; link with no classification → B list/detail; the A account, its roles and its
 * version are unchanged. The database deltas are measured OUTSIDE the browser by the
 * environment's read-only snapshot/verify tooling.
 *
 * BOTH commands go through a real lost reply and a real same-key retry. The first
 * create POST and the first link POST are re-issued to the real API by the test with
 * `route.fetch` — same method, path, company, key and body — and only once the real
 * 201, the real acknowledgment and the real Party/account identity have been SEEN is
 * the browser's delivery of that reply aborted. The server has committed; the browser
 * has an unknown outcome. The user then rechecks, gets a fresh acknowledged GET in the
 * same context, and retries the SAME key, which travels as an ordinary browser request
 * and comes back as a real replay of the same record.
 *
 * NOTHING here is mocked. There is no `route.fulfill`, no synthesised 201, no invented
 * replay header and no fabricated body anywhere in this lane. The withheld reply is
 * recorded as `upstream_success_then_delivery_abort`: the browser did not see a 201 and
 * no natural network fault occurred — the test withheld a reply the server had already
 * committed, which is what a lost reply is.
 *
 * Immutable effect, unchanged by any of the above: +1 Party, +2 company accounts (both
 * roles=0), +1 create audit, +1 link audit, +1 create execution, +1 link execution.
 * The final `control.ps1 -Action verify -ExpectedNewParties 1` stands as it is.
 */
import { expect, test, type Page } from '@playwright/test'
import {
  ActorProbe,
  DeliveryWithholder,
  LiveRecorder,
  isCompletedPageRead,
  isCompletedRead,
  lShot,
  readManifest,
  writeLJson,
  type LiveRecord,
} from '../src/lHarness.js'

const manifest = readManifest()
const A = manifest.companyAId
const B = manifest.companyBId
const PREFIX = manifest.browserPrefix
const CONTRACT = manifest.clientContractVersion
// One journey = one Party; the token keeps a re-run distinguishable in the database.
const RUN_TOKEN = new Date().toISOString().replace(/\D/g, '').slice(8, 14)
const LEGAL_NAME = `${PREFIX} Kabul Metal Sanayi ${RUN_TOKEN} A.Ş.`
const TRADE_NAME = `${PREFIX} Kabul ${RUN_TOKEN}`
const UUID_RE = /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i
/** The classification fact as the detail screen states it for an account with no role. */
const NO_ROLE_DETAIL = 'Ticari rol yok — bu alan zorunlu değil'
const LEGACY_ROLE_LABEL = 'Geçmiş ticari sınıflandırma'
/**
 * A fresh idempotency key per run for the refused writes. A constant would mean a later
 * run re-sending the key of an earlier one, and a replay is not a refusal — the negative
 * would then be passing on the previous run's answer.
 */
const negativeKey = (tag: string): string => `${tag}${RUN_TOKEN}`.padEnd(32, 'a').slice(0, 32)
const UNKNOWN_NOTICE = '[data-attempt="unknown"]'
const RETRY_BUTTON = 'Aynı işlemi yeniden dene'

async function switchCompany(page: Page, companyId: string): Promise<void> {
  const picker = page.getByRole('combobox', { name: 'Şirket', exact: true })
  await picker.selectOption(companyId)
  const dialog = page.getByRole('dialog')
  if (await dialog.count()) await dialog.getByRole('button', { name: 'Değişiklikleri bırak' }).click()
  await expect(picker).toHaveValue(companyId, { timeout: 30_000 })
}

function factValue(page: Page, label: string) {
  return page.locator('dt', { hasText: label }).locator('xpath=following-sibling::dd[1]')
}

/**
 * The ids a completed page read actually carried — evidence independent of the DOM.
 *
 * No search narrowing accompanies this, deliberately. The C006 fixture is a single
 * Party with two accounts after the smoke, so after this journey's create company A
 * lists at most two rows and B offers at most one candidate: the record is always on
 * the first page, and adding a filter step would be scope for a scale this environment
 * does not have.
 */
function payloadIds(record: LiveRecord): string[] {
  const body = record.responseBody as { items?: { id?: string }[] } | null
  return (body?.items ?? []).map((item) => item?.id).filter((id): id is string => typeof id === 'string')
}

/** The in-app return to the list. A distinct name from the nav item, on purpose. */
function backToList(page: Page) {
  return page.getByRole('link', { name: /Cari Kartlar listesine dön/ })
}

/**
 * The fresh, successful, ACKNOWLEDGED reads that were BOTH issued after `afterTick` and
 * had already answered before `post` went out — the recheck the retry stands on.
 *
 * "Answered" means the body FINISHED and parsed into a usable page — not that headers
 * arrived. A 200 whose body was still streaming when the POST went out is not a
 * pre-check, and `isCompletedPageRead` is what draws that line.
 *
 * Both halves matter: a read that started earlier but settled later proves nothing,
 * which is why the recorder stamps a tick at the request and again at the terminal
 * stage.
 * `afterTick` is taken at the retry click, so a read that was already in flight before
 * the user asked for the recheck cannot stand in for it. What this establishes is
 * ordering, not causality: it says a qualifying read had answered first, not that the
 * click is what caused it. How many qualified is recorded rather than reduced to a flag.
 */
function revalidatingRead(reads: LiveRecord[], afterTick: number, post: LiveRecord): LiveRecord[] {
  return reads.filter(
    (r) =>
      isCompletedPageRead(r, CONTRACT) &&
      r.requestTick > afterTick &&
      r.settledTick !== null &&
      r.settledTick < post.requestTick,
  )
}

test('L-01 A create → list → detail; B invisible + detail 404 before link; fresh candidate → link → list → detail; both commands survive a real lost reply and a real same-key retry; A account preserved', async ({ page, request }, testInfo) => {
  test.setTimeout(360_000)
  const slug = 'l01-live-ab-journey'
  // An acceptance case that passes on a second attempt has not shown the behaviour it
  // claims — and a command journey that re-ran would write a second Party.
  expect(testInfo.retry).toBe(0)

  // Seed the preference ONCE. The script runs on every navigation, and the shell itself
  // rewrites the preference on a committed switch — overwriting it on each reload would
  // drag the browser back to A after the switch to B.
  await page.addInitScript((id: string) => {
    if (window.localStorage.getItem('goymen-company-id') === null) window.localStorage.setItem('goymen-company-id', id)
  }, A)
  const net = LiveRecorder.attach(page)
  /**
   * Exactly two deliveries are withheld in the whole journey: the first create POST
   * under A and the first link POST under B. Every other request — the retries
   * included — is passed straight through.
   */
  const withholder = await DeliveryWithholder.install(page, [
    { label: 'create', method: 'POST', companyId: A, match: (p) => p === '/api/v1/counterparties' },
    { label: 'link', method: 'POST', companyId: B, match: (p) => /^\/api\/v1\/counterparties\/[^/]+\/company-account$/.test(p) },
  ])

  // ---- A: create, with a real lost reply ------------------------------------------
  await page.goto('/cari/yeni')
  await expect(page.getByRole('heading', { level: 1, name: 'Yeni Cari' })).toBeVisible({ timeout: 60_000 })
  await expect(page.getByRole('combobox', { name: 'Şirket', exact: true })).toHaveValue(A)
  await page.getByLabel('Taraf türü').selectOption('1')
  await page.getByLabel('Hukuki ad').fill(LEGAL_NAME)
  await page.getByLabel(/^Ticari ad/).fill(TRADE_NAME)
  /**
   * No classification is chosen, and that is the point of this journey now: the
   * 10 September decision removed the mandatory commercial role, so a Cari is created
   * without one. The picker is left untouched rather than cleared, because "untouched"
   * is what an operator who has no classification to give actually does.
   */
  await expect(page.getByRole('checkbox', { name: 'Müşteri' })).not.toBeChecked()
  await expect(page.getByRole('checkbox', { name: 'Tedarikçi' })).not.toBeChecked()
  await lShot(page, slug, '01-A-create-filled')
  await page.getByRole('button', { name: 'Cari oluştur' }).click()

  // The reply is gone. The record is not: the server committed before the test cut the
  // delivery, and the screen says the outcome is unknown rather than guessing either way.
  const unknownAfterCreate = page.locator(UNKNOWN_NOTICE)
  await expect(unknownAfterCreate).toBeVisible({ timeout: 60_000 })
  await expect(unknownAfterCreate.getByRole('button', { name: RETRY_BUTTON })).toBeVisible()
  /**
   * What an unknown outcome must NOT do, measured rather than assumed: no success block,
   * no navigation, and no clearing of the attempt. A screen that quietly moved on would
   * be claiming a result the browser never received.
   */
  const createUnknownUrl = page.url()
  expect(createUnknownUrl).toMatch(/\/cari\/yeni$/)
  await expect(page.locator('[data-outcome]')).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Anladım' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Cari oluştur' })).toBeDisabled()
  await page.waitForTimeout(500)
  await expect(page.locator(UNKNOWN_NOTICE)).toBeVisible()
  await expect(page.locator('[data-outcome]')).toHaveCount(0)
  expect(page.url()).toBe(createUnknownUrl)
  // Counted, not asserted-then-declared: the numbers written to the evidence are the
  // ones the page actually reported at this moment.
  const duringCreateUnknown = {
    url: createUnknownUrl,
    urlUnchanged: page.url() === createUnknownUrl,
    successDomCount: await page.locator('[data-outcome]').count(),
    acknowledgeButtonCount: await page.getByRole('button', { name: 'Anladım' }).count(),
    unknownNoticeCount: await page.locator(UNKNOWN_NOTICE).count(),
    submitDisabled: await page.getByRole('button', { name: 'Cari oluştur' }).isDisabled(),
  }
  await lShot(page, slug, '02-A-create-unknown-outcome')

  const createUpstream = withholder.of('create')
  expect(createUpstream).toHaveLength(1)
  const upstreamCreate = createUpstream[0]
  // The real upstream result, seen by the test before the delivery was cut.
  expect(upstreamCreate.marker).toBe('upstream_success_then_delivery_abort')
  expect(upstreamCreate.upstreamError).toBeNull()
  expect(upstreamCreate.upstreamStatus).toBe(201)
  expect(upstreamCreate.contractAck).toBe(CONTRACT)
  expect(upstreamCreate.contractDeclaration).toBe(CONTRACT)
  expect(upstreamCreate.companyId).toBe(A)
  expect(upstreamCreate.partyId).toMatch(UUID_RE)
  expect(upstreamCreate.partyCode).toMatch(/^CAR-\d{6}$/)
  expect(upstreamCreate.accounts).toHaveLength(1)
  expect(upstreamCreate.accounts[0].companyId).toBe(A)
  expect(upstreamCreate.accounts[0].roles).toBe(0)
  expect(upstreamCreate.deliveredToBrowser).toBe(false)
  const partyId = upstreamCreate.partyId!
  const code = upstreamCreate.partyCode!
  const aAccountVersion = upstreamCreate.accounts[0].version

  // The browser's own view of the same exchange: a request that failed, with no status.
  expect(net.creates()).toHaveLength(1)
  const firstCreate = net.creates()[0]
  expect(firstCreate.status).toBeNull()
  expect(firstCreate.failure).not.toBeNull()
  expect(firstCreate.companyId).toBe(A)
  expect(firstCreate.idempotencyKey).toMatch(/^[0-9a-f]{32}$/)
  // `roles` is OMITTED, not sent as 0: absent and 0 are the same normalized payload
  // (spec §3.1), and absence is what "the user chose none" looks like on the wire.
  expect(firstCreate.requestBody).toEqual({ kind: 1, legalName: LEGAL_NAME, tradeName: TRADE_NAME })
  expect(firstCreate.contractDeclaration).toBe(CONTRACT)
  /**
   * The chain is joined by the ATTEMPT KEY, not only by Party identity: the upstream that
   * committed is the very request the browser lost. And it was the ORIGINAL commit — had
   * the first upstream exchange itself been a replay of a key surviving from an earlier
   * run, this journey would read identically while "+1 Party" was false.
   */
  expect(upstreamCreate.idempotencyKey).toBe(firstCreate.idempotencyKey)
  expect(String(upstreamCreate.replayed ?? '')).not.toMatch(/^true$/i)

  // ---- A: explicit recheck, then the SAME key again --------------------------------
  const tickAtCreateRetry = net.currentTick()
  await unknownAfterCreate.getByRole('button', { name: RETRY_BUTTON }).click()
  /**
   * The command first, the success afterwards — in that order, deliberately.
   *
   * Asserting the navigation first would also pass for a screen that read the record
   * with a GET, concluded it existed and moved on without ever re-sending. Waiting for
   * the second POST and its 201 before looking at the URL excludes that: there is no
   * silent success here that a GET could have produced.
   */
  await expect.poll(() => net.creates().length, { timeout: 60_000 }).toBe(2)
  await expect.poll(() => net.creates()[1].status, { timeout: 60_000 }).toBe(201)
  await expect(page).toHaveURL(new RegExp(`/cari/${partyId}$`, 'i'), { timeout: 60_000 })
  await expect(page.getByRole('heading', { level: 1, name: LEGAL_NAME })).toBeVisible({ timeout: 30_000 })

  const retriedCreate = net.creates()[1]
  // The same attempt, not a new one: same key, same body, same path.
  expect(retriedCreate.idempotencyKey).toBe(firstCreate.idempotencyKey)
  expect(retriedCreate.requestBody).toEqual(firstCreate.requestBody)
  expect(retriedCreate.companyId).toBe(A)
  expect(retriedCreate.contractDeclaration).toBe(CONTRACT)
  expect(retriedCreate.status).toBe(201)
  expect(retriedCreate.contractAck).toBe(CONTRACT)
  // A REAL replay of the record the server already holds — not a second creation.
  expect(String(retriedCreate.responseHeaders['idempotency-replayed'] ?? '')).toMatch(/^true$/i)
  const created = retriedCreate.responseBody as {
    id: string
    code: string
    version: string
    legalName: string
    companyAccounts: { id: string; companyId: string; roles: number; version: string }[]
  }
  expect(created.id).toBe(partyId)
  expect(created.code).toBe(code)
  expect(created.legalName).toBe(LEGAL_NAME)
  expect(created.companyAccounts).toHaveLength(1)
  expect(created.companyAccounts[0].id).toBe(upstreamCreate.accounts[0].id)
  expect(created.companyAccounts[0].roles).toBe(0)
  expect(created.companyAccounts[0].version).toBe(aAccountVersion)

  /**
   * The recheck happened, and it happened BEFORE the resend. A retry that re-sent on the
   * grant the first attempt had earned would satisfy "two POSTs with one key" and still
   * be wrong; what makes this the accepted path is a fresh read that had already come
   * back 200 AND acknowledged when the second POST went out.
   */
  const createRechecks = revalidatingRead(net.listReads().filter((r) => r.companyId === A), tickAtCreateRetry, retriedCreate)
  expect(createRechecks.length, 'a fresh acknowledged read must answer before the create is re-sent').toBeGreaterThanOrEqual(1)
  const createRecheck = createRechecks.at(-1)!

  await expect(factValue(page, 'Cari kodu')).toHaveText(code)
  await expect(factValue(page, LEGACY_ROLE_LABEL)).toHaveText(NO_ROLE_DETAIL)
  await expect(page.locator('dd[data-roles]')).toHaveAttribute('data-roles', 'none')
  await expect.poll(() => net.detailReads(partyId).length).toBeGreaterThanOrEqual(1)
  expect(net.detailReads(partyId)[0].companyId).toBe(A)
  await lShot(page, slug, '03-A-detail-after-retry')

  // ---- A: list, reached the way a user reaches it ----------------------------------
  await backToList(page).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Cari Kartlar' })).toBeVisible({ timeout: 60_000 })
  await expect(page.getByRole('row', { name: new RegExp(code) })).toBeVisible({ timeout: 30_000 })
  await lShot(page, slug, '04-A-list-shows-new-party')

  // ---- B: invisible before the link; detail closes 404 ------------------------------
  await switchCompany(page, B)
  const tickBeforeBListVisit = net.currentTick()
  await page.goto('/cari')
  await expect(page.getByRole('heading', { level: 1, name: 'Cari Kartlar' })).toBeVisible({ timeout: 60_000 })
  await expect(page.getByRole('combobox', { name: 'Şirket', exact: true })).toHaveValue(B)
  /**
   * THIS visit's own completed read decides it.
   *
   * A count taken against an earlier baseline can be satisfied by a read from before the
   * visit, and then `toHaveCount(0)` runs against a table that is still loading — where
   * no row is present for the ordinary reason that nothing has been drawn yet. The read
   * must belong to this visit, must have FINISHED with a usable body, and the screen
   * must be in a terminal state rather than mid-flight.
   */
  const bListVisitReads = () =>
    net.listReads().filter((r) => r.companyId === B && r.requestTick > tickBeforeBListVisit && isCompletedPageRead(r, CONTRACT))
  await expect.poll(() => bListVisitReads().length, { timeout: 60_000 }).toBeGreaterThanOrEqual(1)
  await expect(page.locator('[data-state="initial-loading"]')).toHaveCount(0)
  await expect(page.getByRole('row', { name: new RegExp(code) })).toHaveCount(0)
  const bListBeforeLink = bListVisitReads().at(-1)!
  expect(payloadIds(bListBeforeLink)).not.toContain(partyId)
  await lShot(page, slug, '05-B-list-before-link')
  await page.goto(`/cari/${partyId}`)
  await expect(page.locator('[data-state="not-found"]')).toBeVisible({ timeout: 30_000 })
  await expect.poll(() => net.detailReads(partyId).filter((r) => r.companyId === B).map((r) => r.status)).toContain(404)
  expect(await page.locator('body').innerText()).not.toContain(LEGAL_NAME)
  await lShot(page, slug, '06-B-detail-404-before-link')

  // ---- B: fresh authorized candidate → link, with a real lost reply -----------------
  await page.goto('/cari/sirkete-bagla')
  await expect(page.getByRole('heading', { level: 1, name: 'Cariyi Şirkete Bağla' })).toBeVisible({ timeout: 60_000 })
  const candidateRow = page.getByRole('row', { name: new RegExp(code) })
  await expect(candidateRow).toBeVisible({ timeout: 30_000 })
  // Successful reads only: under the Vite dev server StrictMode duplicates the first
  // read of a mount and cancels one copy, which never carries a status.
  const okCandidateReads = () => net.candidateReads().filter((r) => r.companyId === B && r.status === 200)
  const candidateReadsBeforeLink = okCandidateReads()
  expect(candidateReadsBeforeLink.length).toBeGreaterThanOrEqual(1)
  expect(net.candidateReads().every((r) => r.queryKeys.every((k) => ['q', 'pageSize', 'cursor'].includes(k)))).toBe(true)
  await candidateRow.getByRole('radio').check()
  // A fresh selection and no classification is a complete link.
  await expect(page.getByRole('checkbox', { name: 'Tedarikçi' })).not.toBeChecked()
  await expect.poll(() => page.locator('[data-selection]').getAttribute('data-selection')).toBe('fresh')
  await lShot(page, slug, '07-B-candidate-selected')
  await page.getByRole('button', { name: 'Şirkete bağla' }).click()

  const unknownAfterLink = page.locator(UNKNOWN_NOTICE)
  await expect(unknownAfterLink).toBeVisible({ timeout: 60_000 })
  const linkUnknownUrl = page.url()
  expect(linkUnknownUrl).toMatch(/\/cari\/sirkete-bagla$/)
  await expect(page.getByRole('button', { name: 'Anladım' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Şirkete bağla' })).toBeDisabled()
  await page.waitForTimeout(500)
  await expect(page.locator(UNKNOWN_NOTICE)).toBeVisible()
  expect(page.url()).toBe(linkUnknownUrl)
  // Counted, not asserted-then-declared: the numbers written to the evidence are the
  // ones the page actually reported at this moment.
  const duringLinkUnknown = {
    url: linkUnknownUrl,
    urlUnchanged: page.url() === linkUnknownUrl,
    /**
     * NOT APPLICABLE on this screen for this actor, and recorded as such rather than as
     * a passing zero: the link workspace only renders `[data-outcome]` when the actor
     * cannot open the detail (`CariLinkWorkspacePage.tsx:473`), and this actor can — a
     * success here is a navigation. The URL, attempt and acknowledge checks below are
     * what actually bear weight.
     */
    successDomCount: 'not applicable — success on this screen is a navigation for an actor with detail access',
    acknowledgeButtonCount: await page.getByRole('button', { name: 'Anladım' }).count(),
    unknownNoticeCount: await page.locator(UNKNOWN_NOTICE).count(),
    submitDisabled: await page.getByRole('button', { name: 'Şirkete bağla' }).isDisabled(),
  }
  await lShot(page, slug, '08-B-link-unknown-outcome')

  const linkUpstream = withholder.of('link')
  expect(linkUpstream).toHaveLength(1)
  const upstreamLink = linkUpstream[0]
  expect(upstreamLink.marker).toBe('upstream_success_then_delivery_abort')
  expect(upstreamLink.upstreamError).toBeNull()
  expect(upstreamLink.upstreamStatus).toBe(201)
  expect(upstreamLink.contractAck).toBe(CONTRACT)
  expect(upstreamLink.companyId).toBe(B)
  expect(upstreamLink.path).toBe(`/api/v1/counterparties/${partyId}/company-account`)
  expect(upstreamLink.partyId).toBe(partyId)
  expect(upstreamLink.accounts.some((a) => a.companyId === B && a.roles === 0)).toBe(true)
  expect(upstreamLink.deliveredToBrowser).toBe(false)

  expect(net.links()).toHaveLength(1)
  const firstLink = net.links()[0]
  expect(firstLink.status).toBeNull()
  expect(firstLink.failure).not.toBeNull()
  expect(firstLink.companyId).toBe(B)
  // `{}` is the roleless link request the contract names — not an absent body.
  expect(firstLink.requestBody).toEqual({})
  expect(firstLink.idempotencyKey).toMatch(/^[0-9a-f]{32}$/)
  expect(firstLink.idempotencyKey).not.toBe(firstCreate.idempotencyKey)
  expect(upstreamLink.idempotencyKey).toBe(firstLink.idempotencyKey)
  expect(String(upstreamLink.replayed ?? '')).not.toMatch(/^true$/i)

  /**
   * The hard part. The link is already committed upstream, so the recheck the retry
   * performs may come back with the Party no longer among the candidates. The attempt
   * must survive that: it belongs to the key and the Party it was made for, not to a row
   * that happens to still be on screen. If the retry control were gone, or if the screen
   * treated a successful GET as a successful link, this test fails — and the fixture and
   * the expected counts stay exactly as they are either way.
   */
  await expect(unknownAfterLink.getByRole('button', { name: RETRY_BUTTON })).toBeVisible()
  const tickAtLinkRetry = net.currentTick()
  await unknownAfterLink.getByRole('button', { name: RETRY_BUTTON }).click()
  // Again the command before the success: a screen that saw the Party missing from the
  // fresh candidate read, concluded "already linked" and navigated would pass a
  // URL-first assertion and never re-send. It cannot pass this one.
  await expect.poll(() => net.links().length, { timeout: 60_000 }).toBe(2)
  await expect.poll(() => net.links()[1].status, { timeout: 60_000 }).toBe(201)
  await expect(page).toHaveURL(new RegExp(`/cari/${partyId}$`, 'i'), { timeout: 60_000 })
  await expect(page.getByRole('heading', { level: 1, name: LEGAL_NAME })).toBeVisible({ timeout: 30_000 })

  const retriedLink = net.links()[1]
  expect(retriedLink.idempotencyKey).toBe(firstLink.idempotencyKey)
  expect(retriedLink.requestBody).toEqual({})
  expect(retriedLink.path).toBe(firstLink.path)
  expect(retriedLink.companyId).toBe(B)
  expect(retriedLink.status).toBe(201)
  expect(retriedLink.contractAck).toBe(CONTRACT)
  expect(String(retriedLink.responseHeaders['idempotency-replayed'] ?? '')).toMatch(/^true$/i)
  const linked = retriedLink.responseBody as {
    id: string
    code: string
    version: string
    companyAccounts: { id: string; companyId: string; roles: number; version: string }[]
  }
  expect(linked.id).toBe(partyId)
  expect(linked.code).toBe(code)
  expect(linked.companyAccounts).toHaveLength(1)
  expect(linked.companyAccounts[0].companyId).toBe(B)
  expect(linked.companyAccounts[0].roles).toBe(0)
  expect(linked.companyAccounts[0].id).toBe(upstreamLink.accounts.find((a) => a.companyId === B)!.id)

  const linkRechecks = revalidatingRead(net.candidateReads().filter((r) => r.companyId === B), tickAtLinkRetry, retriedLink)
  expect(linkRechecks.length, 'a fresh acknowledged candidate read must answer before the link is re-sent').toBeGreaterThanOrEqual(1)
  const linkRecheck = linkRechecks.at(-1)!
  /**
   * Whether the committed Party was still listed by that recheck is the environment's
   * business, not this test's — but which of the two happened is recorded, because the
   * interesting case is the one where it had already gone and the attempt held anyway.
   */
  const recheckItems = (linkRecheck.responseBody as { items?: { id?: string }[] } | null)?.items ?? []
  const candidateStillListedAfterCommit = recheckItems.some((item) => item?.id === partyId)

  // Stated, not blank: a counterparty with no commercial classification is an ordinary
  // record, and an empty field would read as "unknown" (ILK_DILIM §7).
  await expect(factValue(page, LEGACY_ROLE_LABEL)).toHaveText(NO_ROLE_DETAIL)
  await lShot(page, slug, '09-B-detail-after-retry')

  // ---- B: the real return — navigation the user performs, and the focus they get ----
  /**
   * `page.goto` proves the route renders; it does not prove a user can get back to the
   * record they were looking at. The return position is only armed by opening the record
   * FROM the list, so the round trip is walked in full: back to the list, into the
   * record by its row, back again — and the row the user left must be what holds focus.
   */
  await backToList(page).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Cari Kartlar' })).toBeVisible({ timeout: 60_000 })
  const bRow = page.getByRole('link', { name: `${code} — ${LEGAL_NAME}` })
  await expect(bRow).toBeVisible({ timeout: 30_000 })
  await lShot(page, slug, '10-B-list-after-link')
  await bRow.click()
  await expect(page).toHaveURL(new RegExp(`/cari/${partyId}$`, 'i'), { timeout: 30_000 })
  await expect(page.getByRole('heading', { level: 1, name: LEGAL_NAME })).toBeVisible({ timeout: 30_000 })
  await backToList(page).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Cari Kartlar' })).toBeVisible({ timeout: 60_000 })
  await expect
    .poll(() => page.evaluate(() => (document.activeElement as HTMLElement | null)?.dataset.rowLink ?? null), { timeout: 30_000 })
    .toBe(partyId)
  await lShot(page, slug, '11-B-return-focus-on-record')

  /**
   * It is no longer a candidate — decided by THIS visit's own read, not by a counter.
   *
   * The previous shape compared the candidate-read count against a baseline captured
   * before the link; the retry's own revalidating read had already pushed it past that,
   * so the poll returned on its first tick and the row assertion then ran against a
   * loading or errored screen, where the row is absent for the wrong reason. A run that
   * navigated away and cancelled the request would have recorded the same "false".
   */
  const tickBeforeFinalCandidateVisit = net.currentTick()
  await page.goto('/cari/sirkete-bagla')
  await expect(page.getByRole('heading', { level: 1, name: 'Cariyi Şirkete Bağla' })).toBeVisible({ timeout: 60_000 })
  const finalCandidateReads = () =>
    net
      .candidateReads()
      .filter((r) => r.companyId === B && r.requestTick > tickBeforeFinalCandidateVisit && isCompletedPageRead(r, CONTRACT))
  await expect.poll(() => finalCandidateReads().length, { timeout: 60_000 }).toBeGreaterThanOrEqual(1)
  const finalCandidateRead = finalCandidateReads().at(-1)!
  // Terminal, not mid-flight and not an error: "Sonuç yok" is a real answer, a spinner
  // and a failure are not.
  await expect(page.locator('[data-state="initial-loading"]')).toHaveCount(0)
  await expect(page.locator('[data-state="service-unavailable"], [data-state="malformed-response"], [data-state="forbidden"], [data-state="unauthenticated"]')).toHaveCount(0)
  await expect(page.getByRole('row', { name: new RegExp(code) })).toHaveCount(0)
  // And the read's own body says so too, independently of what was drawn.
  expect(payloadIds(finalCandidateRead)).not.toContain(partyId)
  await lShot(page, slug, '12-B-candidates-after-link')

  // ---- A: the account, its roles and its version are unchanged ---------------------
  await switchCompany(page, A)
  await page.goto(`/cari/${partyId}`)
  await expect(page.getByRole('heading', { level: 1, name: LEGAL_NAME })).toBeVisible({ timeout: 30_000 })
  await expect(factValue(page, LEGACY_ROLE_LABEL)).toHaveText(NO_ROLE_DETAIL)
  /**
   * COMPLETED reads, not merely answered ones. Polling on status alone is satisfied the
   * instant headers arrive, and dereferencing the body a moment later throws a
   * TypeError on a `null` — losing the "A version preserved" check to a race instead of
   * to a real difference.
   */
  const aDetailReads = () => net.detailReads(partyId).filter((r) => r.companyId === A && isCompletedRead(r, CONTRACT))
  await expect.poll(() => aDetailReads().length, { timeout: 60_000 }).toBeGreaterThanOrEqual(2)
  const aDetailAfter = aDetailReads().at(-1)!.responseBody as {
    companyAccounts: { companyId: string; roles: number; version: string; isActive: boolean }[]
  }
  expect(aDetailAfter.companyAccounts).toHaveLength(1)
  expect(aDetailAfter.companyAccounts[0].companyId).toBe(A)
  expect(aDetailAfter.companyAccounts[0].roles).toBe(0)
  expect(aDetailAfter.companyAccounts[0].version).toBe(aAccountVersion)
  await lShot(page, slug, '13-A-detail-after-link-unchanged')

  /**
   * ---- the other actors, in their own request context ----------------------------
   *
   * The journey actor is never changed. `VITE_DEV_USER` is baked into the served bundle
   * at dev-server start, so no browser context can speak as somebody else — and swapping
   * it would mean re-running the whole journey as a different user. These probes travel
   * on their own APIRequestContext instead, through the same proxy origin, and the
   * recorder never sees them (it listens to page events only), so every assertion about
   * the journey's identity stays true and stays about the journey.
   *
   * None of them may write: a refused write that still created a Party would break
   * ExpectedNewParties 1, and a refusal that names the record it is hiding would leak it.
   * Each refusal must be an AUTHORIZATION refusal — 403, with a problem code that is not
   * a business conflict. A 409 or a 422 means the request reached the logic behind the
   * boundary, which is the opposite of what these probes exist to show.
   */
  const probe = new ActorProbe(request, CONTRACT)
  const viewerList = await probe.send({
    label: 'company-a-viewer reads the A list',
    actor: 'company-a-viewer', method: 'GET', path: '/api/v1/counterparties?pageSize=1', companyId: A,
    mustNotDisclose: [LEGAL_NAME, code, partyId],
  })
  expect(viewerList.status).toBe(403)
  expect(viewerList.disclosedForbiddenText).toBe(false)

  const crossCompanyRead = await probe.send({
    label: 'company-a-user reaches into B',
    actor: 'company-a-user', method: 'GET', path: '/api/v1/counterparties?pageSize=1', companyId: B,
    mustNotDisclose: [LEGAL_NAME, code, partyId],
  })
  expect(crossCompanyRead.status).toBe(403)
  expect(crossCompanyRead.disclosedForbiddenText).toBe(false)

  const crossCompanyWrite = await probe.send({
    label: 'company-a-user writes into B',
    actor: 'company-a-user', method: 'POST', path: `/api/v1/counterparties/${partyId}/company-account`, companyId: B,
    data: {}, idempotencyKey: negativeKey('ff'),
    mustNotDisclose: [LEGAL_NAME, code],
  })
  /**
   * An authorization refusal, not a business conflict.
   *
   * This Party is already linked to B, so a request that got PAST the authorization
   * boundary would come back 409 `already_linked` — which is a 4xx, and under a generic
   * "some 4xx" check would have passed as proof that the boundary held. It proves the
   * opposite: it proves the request was authorized enough to be evaluated. The source
   * tells 403 and 409 apart, so this does too.
   */
  expect(crossCompanyWrite.status).toBe(403)
  expect(crossCompanyWrite.problemCode).not.toBe('already_linked')
  expect(crossCompanyWrite.disclosedForbiddenText).toBe(false)
  expect(crossCompanyWrite.createdPartyId).toBeNull()

  const viewerWrite = await probe.send({
    label: 'company-a-viewer creates under A',
    actor: 'company-a-viewer', method: 'POST', path: '/api/v1/counterparties', companyId: A,
    data: { kind: 1, legalName: `${PREFIX} REDDEDILMELI ${RUN_TOKEN}`, tradeName: `${PREFIX} RED ${RUN_TOKEN}` },
    idempotencyKey: negativeKey('ee'),
    mustNotDisclose: [LEGAL_NAME],
  })
  expect(viewerWrite.status).toBe(403)
  expect(viewerWrite.problemCode).not.toBe('validation_failed')
  expect(viewerWrite.disclosedForbiddenText).toBe(false)
  expect(viewerWrite.createdPartyId).toBeNull()

  // Every browser request carried exactly one company header and the journey identity;
  // no other company and no other actor appeared in the journey itself.
  const apiRecords = net.records.filter((r) => r.path.startsWith('/api/v1/counterparties') || r.path.startsWith('/api/v1/shell/'))
  expect(apiRecords.every((r) => r.companyId === A || r.companyId === B)).toBe(true)
  expect(net.records.every((r) => r.devIdentityHeader === manifest.actor)).toBe(true)

  /**
   * The handshake held for the WHOLE journey, not just the two commands: every Cari
   * exchange declared the contract exactly once, and every successful one came back
   * acknowledged. A single unacknowledged success anywhere in here would mean the write
   * gate had been open on evidence the server never gave.
   */
  const cariRecords = net.records.filter((r) => r.path.startsWith('/api/v1/counterparties'))
  expect(cariRecords.length).toBeGreaterThan(0)
  expect(cariRecords.every((r) => r.contractDeclaration === CONTRACT)).toBe(true)
  expect(cariRecords.filter((r) => r.status === 200 || r.status === 201).every((r) => r.contractAck === CONTRACT)).toBe(true)

  /**
   * The browser half of the snapshot barrier. The environment's post-run snapshot is a
   * multi-query reader, so it only means something once mutations have stopped and
   * nothing is still outstanding here.
   */
  const barrier = await net.quiesce()

  writeLJson('measurements', `${slug}.json`, {
    runId: manifest.runId,
    instanceId: manifest.instanceId,
    apiBaseUrl: manifest.apiBaseUrl,
    fixtureHash: manifest.fixtureHash,
    party: { id: partyId, code, legalName: LEGAL_NAME, version: created.version },
    /**
     * The three states a request in this journey could be in, kept apart on purpose:
     * a reply the server produced and the browser never received; the browser's own
     * failed request; and the ordinary retry that went through untouched.
     */
    upstreamWithheld: withholder.withheld,
    browserAborted: [firstCreate, firstLink].map((r) => ({
      path: r.path, companyId: r.companyId, idempotencyKey: r.idempotencyKey, status: r.status, failure: r.failure,
    })),
    normalRetries: [retriedCreate, retriedLink].map((r) => ({
      path: r.path, companyId: r.companyId, idempotencyKey: r.idempotencyKey, status: r.status,
      replayed: r.responseHeaders['idempotency-replayed'] ?? null,
      // Derived: the withholder logs each command it let through untouched.
      passedThrough: withholder.passedThrough.some((p) => p.idempotencyKey === r.idempotencyKey),
    })),
    passedThroughCommands: withholder.passedThrough,
    recheckBeforeResend: {
      create: { path: createRecheck.path, status: createRecheck.status, ack: createRecheck.contractAck, bodyState: createRecheck.bodyState, outcome: createRecheck.outcome, qualifyingReads: createRechecks.length, settledBeforeResend: createRecheck.settledTick! < retriedCreate.requestTick },
      link: { path: linkRecheck.path, status: linkRecheck.status, ack: linkRecheck.contractAck, bodyState: linkRecheck.bodyState, outcome: linkRecheck.outcome, qualifyingReads: linkRechecks.length, settledBeforeResend: linkRecheck.settledTick! < retriedLink.requestTick },
      note: 'Ordering, not causality: a read that FINISHED (body captured, valid page) after the retry click and before the resend. It is not asserted that the click is what produced it.',
    },
    candidateStillListedAfterCommit,
    /**
     * While each outcome was unknown: no success DOM, no navigation, no clearing of the
     * attempt. Recorded as measured zeros, not as an absence nobody looked for.
     */
    duringUnknown: { create: duringCreateUnknown, link: duringLinkUnknown },
    /** Real application navigation and the focus it restores — no `page.goto` stands in. */
    navigationAndFocus: {
      listReachedBy: 'in-app link "Cari Kartlar listesine dön"',
      recordOpenedBy: 'the list row link',
      returnFocusRowLink: partyId,
      returnFocusMeasured: true,
    },
    cursorAndGeneration: {
      candidateQueryKeys: [...new Set(net.candidateReads().flatMap((r) => r.queryKeys))],
      listQueryKeys: [...new Set(net.listReads().flatMap((r) => r.queryKeys))],
      note: 'Query VALUES are never recorded (keys only). Route-generation is an internal counter; what is recorded here is the observable behaviour it produces.',
    },
    /**
     * What this journey does NOT measure, so nothing downstream reads it as covered:
     * a context change (company switch or session exit) while a real command is already
     * in flight. The existing controlled unknown-state/same-key protections cover that
     * shape and are closed scope; the live sub-type is prepared separately as a
     * read-only source step (l-02) and, for a real session exit, not measured at all.
     */
    notMeasuredHere: [
      'context change while a real command POST is in flight (covered controlled; live sub-type not run here)',
      'real session exit / logout (no sign-out affordance exists with header-based development identity)',
      'an old server binary refusing the contract (controlled no-ack evidence is NOT an old-binary acceptance)',
    ],
    accountA: { roles: aDetailAfter.companyAccounts[0].roles, version: aDetailAfter.companyAccounts[0].version, versionPreserved: true },
    accountB: linked.companyAccounts[0],
    negativeActors: probe.records,
    contract: {
      header: manifest.clientContractHeader,
      version: CONTRACT,
      declaredOnEveryCariExchange: true,
      acknowledgedOnEverySuccess: true,
    },
    bDetailBeforeLink: 404,
    bListBeforeLinkContainedParty: false,
    bCandidateOfferedParty: true,
    bCandidateAfterLinkContainedParty: false,
    returnedToRecordByAppNavigation: true,
    expectedImmutableEffect: '+1 Party / +2 company accounts (roles=0) / +1 create audit / +1 link audit / +1 create execution / +1 link execution',
    /**
     * The order this journey is written to be run in, recorded so the snapshot that
     * brackets it is read against the right thing: L-02 runs FIRST as its own serial
     * invocation (workers 1, retries 0) between an owned source-check/snapshot pair, and
     * L-01 runs after it as the single-Party journey.
     *
     * The intermediate state between this journey's post-commit point and its retry is
     * NOT measured at the database. The run-level delta does not cover it and must not be
     * written as if it did.
     */
    executionOrder: { thisStep: 'L-01', runsAfter: 'L-02 (separate serial invocation)', workers: 1, retries: 0 },
    postCommitToRetryIntervalSqlSnapshot: 'NOT_MEASURED',
    browserBarrier: barrier,
    /**
     * How a snapshot must be read against this journey.
     *
     * Both withheld replies came AFTER a real upstream 201, so the reference point for
     * anything that follows them — the lost delivery, the recheck, the same-key replay —
     * is POSTCOMMIT. What those can show is that they added NO FURTHER effect. None of
     * them may be read as the earlier commit having been rolled back, and the whole
     * journey stays inside the single ExpectedNewParties 1 total.
     *
     * The browser cannot make the effect claim at all: an aborted delivery, a navigation
     * or a context change is not by itself evidence of server effect 0. That is the
     * environment snapshot's measurement, not this recorder's.
     */
    snapshotBoundary: {
      // Derived from what the upstream actually returned, so this cannot say POSTCOMMIT
      // about a journey whose commits were never observed.
      reference:
        withholder.withheld.length === 2 && withholder.withheld.every((w) => w.marker === 'upstream_success_then_delivery_abort')
          ? 'POSTCOMMIT'
          : 'INDETERMINATE',
      withheldMarkers: withholder.withheld.map((w) => ({ label: w.label, marker: w.marker, upstreamStatus: w.upstreamStatus })),
      /** An EXPECTATION for the snapshot to test — not a result this browser measured. */
      expectedAdditionalEffectForSnapshot: 0,
      measuredBy: 'control.ps1 -Action snapshot (before/after, by the environment owner)',
      rollbackClaimed: false,
      totalExpectedNewParties: 1,
      browserCanProveServerEffect: false,
    },
    evidenceMeaning:
      'A withheld delivery is a reply the real server committed and the test did not let the browser receive. The browser never saw a 201 for it, and no natural network fault occurred. Nothing in this lane is mocked.',
    requestCounts: {
      creates: net.creates().length, links: net.links().length, candidateReads: net.candidateReads().length,
      detailReads: net.detailReads().length, listReads: net.listReads().length,
      upstreamWithheld: withholder.withheld.length,
      /**
       * Derived, because a literal preserves nothing. `browserRequestsFailed` is every
       * failed request the page made — which legitimately includes the StrictMode
       * duplicate reads this lane cancels — while the command-level number is the two
       * deliveries this test withheld on purpose.
       */
      browserRequestsFailed: net.records.filter((r) => r.failure !== null).length,
      commandDeliveriesAborted: [firstCreate, firstLink].filter((r) => r.failure !== null).length,
      normalRetries: [retriedCreate, retriedLink].filter((r) => r.status === 201).length,
      pageWritesIssued: net.records.filter((r) => r.method === 'POST').length,
      negativeActorRequests: probe.records.length,
    },
  })
  await net.finish(testInfo, slug, {
    party: { id: partyId, code },
    upstreamWithheld: withholder.withheld,
    negativeActors: probe.records,
  })
})
