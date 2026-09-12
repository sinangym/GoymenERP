/**
 * L-03 — C-06: the selected company-account's operational contact and address, edited
 * for real. Open the card, edit, save, re-read, return, reopen, and see the same values.
 *
 * SOURCE CONTRACT, pinned: Backend #236 `a6efdc0c9e22dc4531f87bd192def2bb44b46c2c` —
 * `docs/features/CARI_CONTACT_INFORMATION_BACKEND_HANDOFF_v1.md` with the DTO and the
 * checked-in OpenAPI in the same commit. Nothing here invents a field, a limit or an
 * endpoint.
 *
 * WHAT THIS JOURNEY WRITES, exactly, so the environment's typed delta can match it:
 *
 *   - ZERO new Party and ZERO new company account. `ExpectedNewParties` is 0: this
 *     journey edits a counterparty the environment already holds and creates none.
 *   - TWO logical contact-information updates against ONE account in company A:
 *       1. set phone + extension, add two operational addresses, add one contact
 *          person with one phone channel;
 *       2. clear phone and extension together.
 *   - FOUR `PUT` requests carry those two: the first update's reply is WITHHELD from
 *     the browser after the server has already committed, its same-key retry replays
 *     it, the second update goes out once, and one further attempt is refused by the
 *     server with `409 version_conflict` and therefore writes nothing.
 *   - ZERO writes in company B, and zero from any refusal the client makes locally.
 *
 * NOTHING IS MOCKED. The one route interception withholds the DELIVERY of a real
 * request after `route.fetch` has already sent it to the real API and its real answer
 * has been read; no status, header or body is ever synthesised, and there is no
 * `route.fulfill` anywhere in this lane.
 *
 * WHAT THIS IS NOT:
 *  - not a session revocation (the isolated Testing host authenticates by
 *    `X-Development-User`; a real identity-provider revocation cannot be produced here
 *    and is not measured);
 *  - not a claim about rows this journey did not read — the environment's own
 *    before/after snapshot measures the database, not the browser;
 *  - not a re-run of L-01, L-02 or any closed C006 step. It shares their harness and
 *    nothing else.
 */
import { expect, test, type Locator, type Page } from '@playwright/test'
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
const SLUG = 'l03-contact-edit-journey'
const CONTACT_PATH_RE = /^\/api\/v1\/counterparties\/[^/]+\/contact-information$/
const UNKNOWN_NOTICE = '[data-attempt="unknown"]'
const RETRY_BUTTON = 'Aynı işlemi yeniden dene'
const SAVED_TEXT = 'Bilgiler kaydedildi'

/** Set by the test so the afterEach hook can reach the recorder of the run that failed. */
let activeRecorder: LiveRecorder | null = null

function contactPuts(net: LiveRecorder): LiveRecord[] {
  return net.records.filter((r) => r.method === 'PUT' && CONTACT_PATH_RE.test(r.path))
}

function contactGets(net: LiveRecorder): LiveRecord[] {
  return net.records.filter((r) => r.method === 'GET' && CONTACT_PATH_RE.test(r.path))
}

/** The account version a completed contact GET actually carried. Evidence, not the DOM. */
function versionOf(record: LiveRecord): string | null {
  const body = record.responseBody as { version?: unknown } | null
  return typeof body?.version === 'string' ? body.version : null
}

function field(page: Page, id: string): Locator {
  return page.locator(`#${id}`)
}

async function switchCompany(page: Page, net: LiveRecorder, companyId: string): Promise<void> {
  // The interval opens BEFORE the user action, so the reads the action itself issues are
  // inside it (the L-01 barrier lesson: a read abandoned by an immediate navigation can
  // never settle).
  const beforeAction = net.currentTick()
  const picker = page.getByRole('combobox', { name: 'Şirket', exact: true })
  await picker.selectOption(companyId)
  const dialog = page.getByRole('dialog')
  // The editor registers a dirty scope, so a switch with unsaved work asks first. Leaving
  // the draft behind is the point: it must not travel to another company.
  if (await dialog.count()) await dialog.getByRole('button', { name: 'Değişiklikleri bırak' }).click()
  await expect(picker).toHaveValue(companyId, { timeout: 30_000 })
  const settled = await net.settleSince(beforeAction, {
    timeoutMs: 15_000,
    expectCompleted: (r) => r.companyId === companyId && r.path.startsWith('/api/v1/counterparties'),
    minimumCompleted: 1,
    contractVersion: CONTRACT,
  })
  expect(
    settled.completedExpected,
    `the switch to ${companyId} must land a COMPLETED scoped counterparties read before the journey navigates: completed=${JSON.stringify(settled.matched)} incomplete=${JSON.stringify(settled.matchedIncomplete)}`,
  ).toBe(true)
  expect(settled.outstanding, `reads this switch issued must finish: ${JSON.stringify(settled.outstanding)}`).toEqual([])
}

/** Evidence survives failure here too; a diagnostic that cannot be written stays silent. */
// Playwright requires this first argument to BE an object destructuring pattern and
// rejects the file at load time otherwise — see the same note in L-01/L-02.
// oxlint-disable-next-line no-empty-pattern -- required by the Playwright hook signature
test.afterEach(async ({}, testInfo) => {
  if (testInfo.status === 'passed') return
  const recorder = activeRecorder
  if (recorder === null) return
  try {
    writeLJson('pending-state', `${SLUG}-outstanding.json`, {
      testStatus: testInfo.status ?? null,
      testError: testInfo.error?.message ?? null,
      observedEvents: recorder.observedEvents(),
      outstanding: recorder.outstanding(),
      records: recorder.records,
      meaning:
        'RAW RECORDER STATE ONLY, captured after a failed run from an afterEach hook so a failure anywhere in the journey still leaves the request/capture state on disk. NOT the full record: the measurement file and the withheld-upstream entries are written by the main path, and their absence here does not mean they were preserved.',
    })
  } catch {
    // Deliberately swallowed: the run already has a real failure and it owns the report.
  }
})

test('L-03 C-06: edit the account contact/address for real — withheld reply and same-key replay, a stranded extension refused locally, a real stale-version conflict, then return, reopen and read the same values; nothing written in B', async ({
  page,
  context,
}, testInfo) => {
  test.setTimeout(420_000)
  expect(testInfo.retry).toBe(0)

  await page.addInitScript((id: string) => {
    if (window.localStorage.getItem('goymen-company-id') === null) window.localStorage.setItem('goymen-company-id', id)
  }, A)
  const net = LiveRecorder.attach(page)
  activeRecorder = net

  /**
   * Exactly ONE delivery is withheld in the whole journey: the first contact-information
   * PUT under A. Its success is 204 with no body, so the target says so — the default
   * expectation is a 201 carrying an identity, and a legitimate 204 measured against
   * that would be labelled an unexpected upstream.
   */
  const withholder = await DeliveryWithholder.install(page, [
    {
      label: 'contact-update',
      method: 'PUT',
      companyId: A,
      match: (p) => CONTACT_PATH_RE.test(p),
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
  const listed = (
    net.records.find((r) => r.method === 'GET' && r.path === '/api/v1/counterparties' && isCompletedRead(r, CONTRACT))
      ?.responseBody as { items?: { id?: string; code?: string }[] } | null
  )?.items ?? []
  expect(
    listed.length,
    'this journey EDITS a counterparty the environment already holds and creates none; company A listed no editable record',
  ).toBeGreaterThan(0)
  const target = listed[0]
  const partyId = String(target.id)
  const partyCode = String(target.code)

  // ---- 1. card → editor, by the application's own navigation ----------------------
  await page.getByRole('row', { name: new RegExp(partyCode) }).getByRole('link').first().click()
  await expect(page.locator('[data-section="account"]')).toBeVisible({ timeout: 30_000 })
  const editLink = page.getByRole('link', { name: 'İletişim ve adres bilgilerini düzenle' })
  await expect(
    editLink,
    'the card must offer the editor: without the server-granted capability this journey cannot run, and the editor must not be reachable anyway',
  ).toBeVisible({ timeout: 30_000 })
  await editLink.click()
  await expect(page).toHaveURL(new RegExp(`/cari/${partyId}/bilgiler$`), { timeout: 30_000 })
  await expect(field(page, 'cari-contact-phone')).toBeVisible({ timeout: 30_000 })
  const seededVersion = versionOf(contactGets(net).filter((r) => isCompletedRead(r, CONTRACT)).at(-1)!)
  expect(seededVersion, 'the editor must be seeded from a completed, acknowledged contact read').not.toBeNull()
  await lShot(page, SLUG, '01-editor-open')

  // A second editor on the SAME record, opened now, holds the version the first save is
  // about to supersede. That is what makes the later conflict real rather than staged.
  const stale = await context.newPage()
  await stale.goto(`/cari/${partyId}/bilgiler`)
  await expect(field(stale, 'cari-contact-phone')).toBeVisible({ timeout: 60_000 })

  // ---- 2. the first update, with a real lost reply ---------------------------------
  const phone = '+90 212 555 00 01'
  const extension = '123'
  await field(page, 'cari-contact-phone').fill(phone)
  await field(page, 'cari-contact-phoneExtension').fill(extension)

  await page.getByRole('button', { name: 'Adres ekle' }).click()
  await field(page, 'cari-address-0-purpose').selectOption('delivery')
  await field(page, 'cari-address-0-countryCode').fill('TR')
  await field(page, 'cari-address-0-line1').fill(`${PREFIX} Teslim Yolu 1`)
  await field(page, 'cari-address-0-city').fill('Kocaeli')

  await page.getByRole('button', { name: 'Adres ekle' }).click()
  await field(page, 'cari-address-1-purpose').selectOption('operational')
  // A country that is not TR, and no default was offered: the form must not assume one.
  await field(page, 'cari-address-1-countryCode').fill('DE')
  await field(page, 'cari-address-1-line1').fill(`${PREFIX} Operational 2`)

  await page.getByRole('button', { name: 'İrtibat kişisi ekle' }).click()
  await field(page, 'cari-contact-row-0-displayName').fill(`${PREFIX} Operasyon Muhatabı`)
  await field(page, 'cari-contact-row-0-purpose').fill('Operasyon')
  await page.getByRole('button', { name: 'Kanal ekle' }).click()
  await field(page, 'cari-contact-row-0-channel-0-kind').selectOption('phone')
  await field(page, 'cari-contact-row-0-channel-0-value').fill('0532 000 00 01')

  const save = page.getByRole('button', { name: /Bilgileri kaydet|Gönderiliyor/ })
  await save.click()

  const unknown = page.locator(UNKNOWN_NOTICE)
  await expect(unknown, 'a withheld reply must leave the outcome UNKNOWN on screen').toBeVisible({ timeout: 60_000 })
  await expect(unknown.getByRole('button', { name: RETRY_BUTTON })).toBeVisible()
  // The record may exist. The screen must not say it was saved, and it must not offer a
  // second save under a new key.
  await expect(page.getByText(SAVED_TEXT)).toHaveCount(0)
  await lShot(page, SLUG, '02-unknown-after-withheld')

  const withheld = withholder.of('contact-update')
  expect(withheld, 'exactly one delivery is withheld in this journey').toHaveLength(1)
  expect(
    withheld[0].marker,
    `the withheld exchange must be a real upstream success before its delivery was cut: ${String(withheld[0].upstreamError)}`,
  ).toBe('upstream_success_then_delivery_abort')
  expect(withheld[0].upstreamStatus).toBe(204)
  expect(withheld[0].contractAck).toBe(CONTRACT)
  const firstKey = withheld[0].idempotencyKey
  expect(firstKey).not.toBeNull()

  const beforeRetry = net.currentTick()
  await unknown.getByRole('button', { name: RETRY_BUTTON }).click()
  await expect(page.getByText(SAVED_TEXT), 'the same-key retry must replay the real write').toBeVisible({ timeout: 60_000 })

  const puts = contactPuts(net)
  expect(puts.length, 'one withheld send and one same-key retry').toBe(2)
  expect(puts[1].idempotencyKey, 'the retry must carry the SAME key, never a new one').toBe(firstKey)
  expect(
    JSON.stringify(puts[1].requestBody),
    'the retry must carry the identical payload — the client-minted child ids included',
  ).toBe(JSON.stringify(puts[0].requestBody))
  expect(puts[1].requestTick).toBeGreaterThan(beforeRetry)
  expect(puts[1].status, 'the replayed update answers 204').toBe(204)
  expect(
    (puts[1].responseHeaders['idempotency-replayed'] ?? '').toLowerCase(),
    'the server itself must say this was a replay, not a second effect',
  ).toBe('true')

  // The save's own fence, read from the request rather than from the screen.
  const sentBody = puts[0].requestBody as { expectedVersion?: unknown; changes?: Record<string, unknown> }
  expect(sentBody.expectedVersion, 'the update is fenced on the ACCOUNT version the editor read').toBe(seededVersion)
  expect(Object.keys(sentBody.changes ?? {}).sort()).toEqual(['addresses', 'contacts', 'phone', 'phoneExtension'])

  // ---- 3. the re-read is what the screen believes ---------------------------------
  const afterSave = contactGets(net).filter((r) => isCompletedRead(r, CONTRACT) && r.requestTick > puts[1].requestTick)
  expect(afterSave.length, 'a 204 carries no body: the screen must re-read before claiming anything').toBeGreaterThan(0)
  const reread = afterSave.at(-1)!
  const rereadBody = reread.responseBody as {
    version?: string
    information?: { phone?: string | null; phoneExtension?: string | null; addresses?: unknown[]; contacts?: { channels?: unknown[] }[] }
  }
  expect(rereadBody.information?.phone).toBe(phone)
  expect(rereadBody.information?.phoneExtension).toBe(extension)
  expect((rereadBody.information?.addresses ?? []).length).toBeGreaterThanOrEqual(2)
  expect((rereadBody.information?.contacts ?? []).length).toBeGreaterThanOrEqual(1)
  expect((rereadBody.information?.contacts?.[0]?.channels ?? []).length).toBeGreaterThanOrEqual(1)
  const versionAfterFirst = String(rereadBody.version)
  expect(versionAfterFirst).not.toBe(seededVersion)
  await lShot(page, SLUG, '03-saved-and-reread')

  // ---- 4. clearing a phone may not strand its extension ---------------------------
  const putsBeforeRefusal = contactPuts(net).length
  await field(page, 'cari-contact-phone').fill('')
  await save.click()
  await expect(
    page.getByText(/Dahili numara yalnız bir telefonla/),
    'the contract forbids a phone-only clear that leaves a retained extension; the refusal must be visible',
  ).toBeVisible({ timeout: 30_000 })
  expect(
    contactPuts(net).length,
    'a client-side refusal sends nothing and mints no key',
  ).toBe(putsBeforeRefusal)

  await field(page, 'cari-contact-phoneExtension').fill('')
  await save.click()
  await expect(page.getByText(SAVED_TEXT)).toBeVisible({ timeout: 60_000 })
  const clearPut = contactPuts(net).at(-1)!
  expect(clearPut.status).toBe(204)
  expect(clearPut.requestBody).toEqual({
    expectedVersion: versionAfterFirst,
    changes: { phone: null, phoneExtension: null },
  })
  expect(clearPut.idempotencyKey, 'a new logical attempt gets its own key').not.toBe(firstKey)

  // ---- 5. a REAL stale-version conflict from the editor opened earlier -------------
  const staleNet = LiveRecorder.attach(stale)
  await field(stale, 'cari-contact-email').fill(`${PREFIX.toLowerCase()}-stale@example.test`)
  await stale.getByRole('button', { name: /Bilgileri kaydet|Gönderiliyor/ }).click()
  const conflict = stale.locator('[data-failure="version-conflict"]')
  await expect(
    conflict,
    'the second editor was seeded before two committed updates; its fence is genuinely stale',
  ).toBeVisible({ timeout: 60_000 })
  const stalePuts = contactPuts(staleNet)
  expect(stalePuts.length, 'a definite conflict is not retried under the same key').toBe(1)
  expect(stalePuts[0].status).toBe(409)
  // The draft survives the refusal, and the recovery offered is a fresh read.
  await expect(field(stale, 'cari-contact-email')).toHaveValue(`${PREFIX.toLowerCase()}-stale@example.test`)
  await expect(stale.getByRole('button', { name: 'Kaydı yenile' })).toBeVisible()
  await lShot(stale, SLUG, '04-version-conflict')
  await stale.close()

  // ---- 6. the real return, and reopening the record --------------------------------
  await page.getByRole('link', { name: /Cari karta dön/ }).click()
  await expect(page.locator('[data-section="identity"]')).toBeVisible({ timeout: 30_000 })
  await page.getByRole('link', { name: /Cari Kartlar listesine dön/ }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Cari Kartlar' })).toBeVisible({ timeout: 30_000 })
  await page.getByRole('row', { name: new RegExp(partyCode) }).getByRole('link').first().click()
  await page.getByRole('link', { name: 'İletişim ve adres bilgilerini düzenle' }).click()
  await expect(field(page, 'cari-contact-phone')).toBeVisible({ timeout: 30_000 })

  // Reopened from a fresh read: the two addresses and the contact are still there, and
  // the cleared phone is still cleared.
  await expect(field(page, 'cari-contact-phone')).toHaveValue('')
  await expect(field(page, 'cari-contact-phoneExtension')).toHaveValue('')
  await expect(field(page, 'cari-address-0-line1')).toBeVisible()
  await expect(field(page, 'cari-address-1-line1')).toBeVisible()
  await expect(field(page, 'cari-contact-row-0-displayName')).toBeVisible()
  const reopened = contactGets(net).filter((r) => isCompletedRead(r, CONTRACT)).at(-1)!
  const reopenedInfo = (reopened.responseBody as { information?: { addresses?: { line1?: string }[]; contacts?: { displayName?: string }[] } })
    .information
  expect((reopenedInfo?.addresses ?? []).some((a) => a.line1 === `${PREFIX} Teslim Yolu 1`)).toBe(true)
  expect((reopenedInfo?.addresses ?? []).some((a) => a.line1 === `${PREFIX} Operational 2`)).toBe(true)
  expect((reopenedInfo?.contacts ?? []).some((c) => c.displayName === `${PREFIX} Operasyon Muhatabı`)).toBe(true)
  await lShot(page, SLUG, '05-reopened')

  // ---- 7. company B: nothing of A's, and nothing written --------------------------
  const putsBeforeB = contactPuts(net).length
  await switchCompany(page, net, B)
  await page.goto(`/cari/${partyId}/bilgiler`)
  // Either outcome is honest and both are recorded: B may hold its own account for this
  // Party, or the read is a non-disclosing 404, or the route is closed. What must NOT
  // happen is A's values appearing under B's company, or any write.
  await page.waitForLoadState('networkidle', { timeout: 30_000 }).catch(() => {})
  const bPhone = field(page, 'cari-contact-phone')
  const bEditorOpen = (await bPhone.count()) > 0
  const bLineInputs = await page.locator('input[id^="cari-address-"][id$="-line1"]').all()
  const bLines: string[] = []
  for (const input of bLineInputs) bLines.push(await input.inputValue())
  expect(
    bLines.includes(`${PREFIX} Teslim Yolu 1`),
    "company A's address must never be rendered under company B",
  ).toBe(false)
  const bContactPuts = contactPuts(net).filter((r) => r.companyId === B)
  expect(bContactPuts, 'this journey writes nothing in company B').toEqual([])
  expect(contactPuts(net).length).toBe(putsBeforeB)
  await lShot(page, SLUG, '06-company-b')

  // ---- 8. the barrier, and the evidence BEFORE the verdict -------------------------
  const barrier = await net.quiesce({ settleMs: 1500, timeoutMs: 45_000 })
  let evidenceError: unknown = null
  try {
    writeLJson('measurement', `${SLUG}-network.json`, {
      phase: 'measurement',
      slug: SLUG,
      runId: manifest.runId,
      instanceId: manifest.instanceId,
      partyId,
      partyCode,
      companyA: A,
      companyB: B,
      seededVersion,
      versionAfterFirstUpdate: versionAfterFirst,
      logicalUpdates: 2,
      expectedNewParties: 0,
      contactPuts: contactPuts(net).map((r) => ({
        seq: r.seq,
        companyId: r.companyId,
        idempotencyKey: r.idempotencyKey,
        status: r.status,
        replayed: r.responseHeaders['idempotency-replayed'] ?? null,
        contractDeclaration: r.contractDeclaration,
        contractAck: r.contractAck,
        requestBody: r.requestBody,
      })),
      contactGets: contactGets(net).map((r) => ({
        seq: r.seq,
        companyId: r.companyId,
        status: r.status,
        completed: isCompletedRead(r, CONTRACT),
        version: versionOf(r),
      })),
      withheld: withholder.withheld,
      passedThrough: withholder.passedThrough,
      companyBEditorOpened: bEditorOpen,
      companyBAddressLines: bLines,
      barrier,
      observedEvents: net.observedEvents(),
      boundaries: [
        'No Party or company account was created; ExpectedNewParties is 0 for this journey.',
        'Two logical contact-information updates against one account in company A. The database effect is measured by the environment snapshot, not by this browser.',
        'The withheld exchange was a real 204 from the real API; its delivery to the browser was cut after the server had already committed. Nothing was mocked and no response was ever synthesised.',
        'A session revocation is NOT measured: the isolated Testing host authenticates by X-Development-User and offers no sign-out.',
        'The company-B step records what was observed. It does not claim B holds or lacks an account for this Party; it asserts only that no value from A appeared there and that nothing was written.',
        'Closed C006, L-01 and L-02 evidence is not reused for this journey and is not re-run by it.',
      ],
      finalTestResult: 'NOT_DECIDED_AT_WRITE_TIME',
      statusAtWrite: testInfo.status ?? null,
    })
  } catch (error) {
    evidenceError = error
  }
  await net.finish(testInfo, SLUG)

  /**
   * The barrier is the PRIMARY verdict and a failed evidence write is a secondary
   * symptom: reporting them the other way round once hid a real failure behind its own
   * diagnostic.
   */
  const priority = failurePriority(barrier.accepted, evidenceError)
  const secondary =
    priority.secondary === 'evidence' ? ` — SECONDARY: the evidence write also failed: ${evidenceErrorText(evidenceError)}` : ''
  expect(
    barrier.outstanding,
    `the browser did not go quiet; ${barrier.outstanding.length} request(s) unaccounted for: ${JSON.stringify(barrier.outstanding, null, 1)}${secondary}`,
  ).toEqual([])
  expect(
    barrier.accepted,
    `the barrier was not accepted (timedOut=${barrier.timedOut}, quietWindowHeld=${barrier.quietWindowHeld}, markedBusy=${barrier.markedBusyAtDecision})${secondary}`,
  ).toBe(true)
  // The barrier held — so a mandatory evidence write that failed IS the failure.
  if (priority.primary === 'evidence') {
    throw new Error(`the barrier held but the mandatory evidence write failed: ${evidenceErrorText(evidenceError)}`)
  }
})
