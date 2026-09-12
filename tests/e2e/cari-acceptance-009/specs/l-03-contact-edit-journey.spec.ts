/**
 * L-03 — C-06: the selected company-account's operational contact and address, edited
 * for real, through the whole life of one record.
 *
 * SOURCE CONTRACT, pinned: Backend #236 `9df7f40e3aefc5b02ef42a378c0d1aa800a2fe29` —
 * `docs/features/CARI_CONTACT_INFORMATION_BACKEND_HANDOFF_v1.md` with the DTO and the
 * checked-in OpenAPI. Same shape as the earlier `a6efdc0`; that commit additionally
 * refuses raw control characters before trimming, and it is the dependency a real
 * acceptance binds to. Shared test intent: issue #66 comment `5624159337`, relayed to
 * Backend as the joint acceptance plan. Nothing here invents a field, a limit, an
 * endpoint or a count.
 *
 * THE SEQUENCE AND THE NUMBERS, so the environment's typed verifier binds to them
 * rather than inferring them. The fixture starts at v1 with an EMPTY profile:
 *
 *   U1  phone + extension + profile e-mail, two addresses (TR delivery, DE
 *       operational), two contact people — the first with a phone channel, the second
 *       with an e-mail channel. Its reply is WITHHELD after the server commits; the
 *       same-key retry replays it.                                        → v2
 *   U2  clear phone and extension together (after the client's own refusal of a
 *       phone-only clear, which sends nothing).                           → v3
 *   U3  deactivate the TR delivery address.                               → v4
 *   U4  update that SAME passive address's line1. An upsert does not reactivate, so it
 *       must come back still passive.                                     → v5
 *   U5  reactivate it explicitly.                                         → v6
 *   --  a second tab opened BEFORE U1 submits an e-mail on expectedVersion 1 and is
 *       refused with 409 version_conflict: a durable command record, audit 0.
 *   U6  that same tab refreshes to v6, the user reviews, and the same intended e-mail
 *       is saved under a NEW key on expectedVersion 6.                    → v7
 *
 *   8 PUTs · 6 successful logical updates · 6 audit events · 7 durable unique keys
 *   (6 Succeeded 204 + 1 VersionConflict 409) · 0 new Party · 0 new account ·
 *   0 writes in company B.
 *
 *   `contactPuts` is the MAIN page's 6 (U1 original, U1 replay, U2, U3, U4, U5);
 *   `staleContactPuts` is the SECOND page's 2 (the 409 and U6). `seq` is page-local in
 *   each projection; the SQL side joins on key/company/actor/payload/outcome/response.
 *
 * ALSO MEASURED HERE: the real in-app return and reopen with keyboard focus, a company
 * switch with an unsaved draft (the real dirty warning, and nothing carried across),
 * and a READ-ONLY actor who is offered no editor and whose deep link writes nothing.
 *
 * NOTHING IS MOCKED. The one route interception withholds the DELIVERY of a real
 * request after `route.fetch` has already sent it to the real API and its real answer
 * has been read. The read-only context rewrites ONE request header — the development
 * identity — and nothing else: the requests are real and the server's answers are its
 * own. There is no `route.fulfill` anywhere in this lane.
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
import { expect, test, type BrowserContext, type Locator, type Page } from '@playwright/test'
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
const SAVE_BUTTON = /Bilgileri kaydet|Gönderiliyor/

/**
 * The read-only actor, as the API owner named it: company A's ORGANIZATION_OPERATOR,
 * given `counterparty.parties.read` inside the Testing acceptance profile and NOT the
 * basic edit permission. `company-a-viewer` is deliberately not used — it is
 * site-scoped and its GET is a 403, which would prove nothing about a reader.
 */
const READ_ONLY_ACTOR = 'company-a-user'

/** The values this journey writes. Prefixed so every row is attributable to this run. */
const PHONE = '+90 212 555 00 01'
const EXTENSION = '123'
const EMAIL_U1 = `${PREFIX.toLowerCase()}-profil1@example.test`
const EMAIL_STALE = `${PREFIX.toLowerCase()}-profil2@example.test`
const ADDR_TR_LINE1 = `${PREFIX} Teslim Yolu 1`
const ADDR_TR_LINE1_UPDATED = `${PREFIX} Teslim Yolu 1A`
const ADDR_DE_LINE1 = `${PREFIX} Operational 2`
const PERSON_1 = `${PREFIX} Operasyon Muhatabı`
const PERSON_2 = `${PREFIX} Muhasebe Muhatabı`

/** Set by the test so the afterEach hook can reach the recorders of the run that failed. */
let activeRecorder: LiveRecorder | null = null
let activeStaleRecorder: LiveRecorder | null = null

function contactPuts(net: LiveRecorder): LiveRecord[] {
  return net.records.filter((r) => r.method === 'PUT' && CONTACT_PATH_RE.test(r.path))
}

function contactGets(net: LiveRecorder): LiveRecord[] {
  return net.records.filter((r) => r.method === 'GET' && CONTACT_PATH_RE.test(r.path))
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

interface ContactBody {
  readonly version?: string
  readonly information?: {
    readonly phone?: string | null
    readonly phoneExtension?: string | null
    readonly email?: string | null
    readonly addresses?: { id: string; isActive: boolean; line1: string; purpose: string; countryCode: string }[]
    readonly contacts?: { id: string; displayName: string; channels?: { id: string; kind: string }[] }[]
  }
}

/**
 * The newest contact read that ACTUALLY ANSWERED after `sinceTick`, waited for with a
 * bound.
 *
 * The recorder's body copy is a second round trip of its own, so the DOM can be ready
 * while the capture is still in flight. Reading `records` at that moment produced
 * `undefined` and then a version of `null` — a measurement that silently described a
 * read nobody had. DOM-ready is not recorder-ready, and if the capture never lands this
 * fails loudly instead of reporting an absence as a value.
 */
async function awaitContactRead(page: Page, net: LiveRecorder, sinceTick: number, label: string): Promise<ContactBody> {
  const deadline = Date.now() + 30_000
  for (;;) {
    const answered = contactGets(net).filter((r) => r.requestTick > sinceTick && isCompletedRead(r, CONTRACT))
    const newest = answered.at(-1)
    if (newest !== undefined) return (newest.responseBody ?? {}) as ContactBody
    if (Date.now() > deadline) {
      const seen = contactGets(net)
        .filter((r) => r.requestTick > sinceTick)
        .map((r) => ({ seq: r.seq, status: r.status, transferOutcome: r.transferOutcome, bodyState: r.bodyState, ack: r.contractAck }))
      throw new Error(`${label}: no completed contact read landed in the recorder: ${JSON.stringify(seen)}`)
    }
    await page.waitForTimeout(100)
  }
}

function field(page: Page, id: string): Locator {
  return page.locator(`#${id}`)
}

function saveButton(page: Page): Locator {
  return page.getByRole('button', { name: SAVE_BUTTON })
}

/** The 0-based index of the address row whose line1 currently reads `line1`. */
async function addressIndexFor(page: Page, line1: string): Promise<number> {
  for (const row of await page.locator('[data-row-index]').all()) {
    const input = row.locator('input[id$="-line1"]')
    if ((await input.count()) === 0) continue
    if ((await input.inputValue()) === line1) return Number(await row.getAttribute('data-row-index'))
  }
  throw new Error(`no address row currently shows line1 ${JSON.stringify(line1)}`)
}

/** Save, wait for the confirmation, and return the version the FRESH read carried. */
async function saveAndRead(page: Page, net: LiveRecorder, label: string): Promise<ContactBody> {
  const before = net.currentTick()
  await saveButton(page).click()
  await expect(page.getByText(SAVED_TEXT), `${label}: the save must be confirmed`).toBeVisible({ timeout: 60_000 })
  return await awaitContactRead(page, net, before, label)
}

/**
 * A company switch, waited for HONESTLY.
 *
 * `settleSince`'s `minimumCompleted` counts completed PAGE reads — items/hasMore/
 * nextCursor — which is the right condition on a list route and the wrong one here: the
 * editor's own read is a single record, and a 404 or a route closed by capability
 * completes no page at all. Requiring one made a correct outcome time out after 15
 * seconds and report a false failure. What is required is that every request this action
 * issued reached a terminal state; what it MEANT is then read from the records.
 */
async function switchCompany(page: Page, net: LiveRecorder, companyId: string, discardDraft: boolean): Promise<void> {
  const beforeAction = net.currentTick()
  const picker = page.getByRole('combobox', { name: 'Şirket', exact: true })
  await picker.selectOption(companyId)
  const dialog = page.getByRole('dialog')
  if (discardDraft) {
    await expect(dialog, 'an unsaved draft must be defended by a real warning').toBeVisible({ timeout: 30_000 })
    await dialog.getByRole('button', { name: 'Değişiklikleri bırak' }).click()
  } else if (await dialog.count()) {
    await dialog.getByRole('button', { name: 'Değişiklikleri bırak' }).click()
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
// Playwright requires this first argument to BE an object destructuring pattern and
// rejects the file at load time otherwise — see the same note in L-01/L-02.
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

test('L-03 C-06: six real updates v1→v7 across two tabs — withheld reply and same-key replay, a stranded extension refused locally, deactivate → passive upsert → reactivate, a real stale-version conflict resolved under a new fence, then reopen by keyboard, a defended draft on company change, and a read-only actor who is offered nothing', async ({
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
   * Exactly ONE delivery is withheld in the whole journey: U1, on the main page. Its
   * success is 204 with no body, so the target says so — the default expectation is a
   * 201 carrying an identity, and a legitimate 204 measured against that would be
   * labelled an unexpected upstream.
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
  const listed =
    (
      net.records.find((r) => r.method === 'GET' && r.path === '/api/v1/counterparties' && isCompletedRead(r, CONTRACT))
        ?.responseBody as { items?: { id?: string; code?: string }[] } | null
    )?.items ?? []
  expect(
    listed.length,
    'this journey EDITS a counterparty the environment already holds and creates none; company A listed no editable record',
  ).toBeGreaterThan(0)
  const partyId = String(listed[0].id)
  const partyCode = String(listed[0].code)

  // ---- 1. card → editor, by the application's own navigation ----------------------
  await page.getByRole('row', { name: new RegExp(partyCode) }).getByRole('link').first().click()
  await expect(page.locator('[data-section="account"]')).toBeVisible({ timeout: 30_000 })
  const editLink = page.getByRole('link', { name: 'İletişim ve adres bilgilerini düzenle' })
  await expect(
    editLink,
    'the card must offer the editor: without the server-granted capability this journey cannot run',
  ).toBeVisible({ timeout: 30_000 })
  const beforeEditor = net.currentTick()
  await editLink.click()
  await expect(page).toHaveURL(new RegExp(`/cari/${partyId}/bilgiler$`), { timeout: 30_000 })
  await expect(field(page, 'cari-contact-phone')).toBeVisible({ timeout: 30_000 })
  const v1 = await awaitContactRead(page, net, beforeEditor, 'editor seed')
  expect(v1.version, 'the fixture profile starts at version 1').toBe('1')
  await lShot(page, SLUG, '01-editor-open-v1')

  /**
   * The second tab, opened NOW — before U1 — so the conflict later is genuinely a stale
   * fence and not something arranged after the fact. Its own recorder is attached before
   * its first request, and its records stay in their own projection: mixing them into the
   * main page's would make both counts unreadable.
   */
  const stale = await context.newPage()
  const staleNet = LiveRecorder.attach(stale)
  activeStaleRecorder = staleNet
  await stale.goto(`/cari/${partyId}/bilgiler`)
  await expect(field(stale, 'cari-contact-phone')).toBeVisible({ timeout: 60_000 })
  const staleSeed = await awaitContactRead(stale, staleNet, 0, 'stale tab seed')
  expect(staleSeed.version, 'the second tab is seeded on the same version 1').toBe('1')

  // ---- 2. U1, with a real lost reply ------------------------------------------------
  await field(page, 'cari-contact-phone').fill(PHONE)
  await field(page, 'cari-contact-phoneExtension').fill(EXTENSION)
  await field(page, 'cari-contact-email').fill(EMAIL_U1)

  await page.getByRole('button', { name: 'Adres ekle' }).click()
  await field(page, 'cari-address-0-purpose').selectOption('delivery')
  await field(page, 'cari-address-0-countryCode').fill('TR')
  await field(page, 'cari-address-0-line1').fill(ADDR_TR_LINE1)
  await field(page, 'cari-address-0-city').fill('Kocaeli')

  await page.getByRole('button', { name: 'Adres ekle' }).click()
  await field(page, 'cari-address-1-purpose').selectOption('operational')
  // A country that is not TR, and no default was offered: the form must not assume one.
  await field(page, 'cari-address-1-countryCode').fill('DE')
  await field(page, 'cari-address-1-line1').fill(ADDR_DE_LINE1)

  await page.getByRole('button', { name: 'İrtibat kişisi ekle' }).click()
  await field(page, 'cari-contact-row-0-displayName').fill(PERSON_1)
  await field(page, 'cari-contact-row-0-purpose').fill('Operasyon')
  await page.getByRole('button', { name: 'Kanal ekle' }).first().click()
  await field(page, 'cari-contact-row-0-channel-0-kind').selectOption('phone')
  await field(page, 'cari-contact-row-0-channel-0-value').fill('0532 000 00 01')

  await page.getByRole('button', { name: 'İrtibat kişisi ekle' }).click()
  await field(page, 'cari-contact-row-1-displayName').fill(PERSON_2)
  await field(page, 'cari-contact-row-1-purpose').fill('Muhasebe')
  await page.getByRole('button', { name: 'Kanal ekle' }).last().click()
  await field(page, 'cari-contact-row-1-channel-0-kind').selectOption('email')
  await field(page, 'cari-contact-row-1-channel-0-value').fill(`${PREFIX.toLowerCase()}-muhasebe@example.test`)

  await saveButton(page).click()

  const unknown = page.locator(UNKNOWN_NOTICE)
  await expect(unknown, 'a withheld reply must leave the outcome UNKNOWN on screen').toBeVisible({ timeout: 60_000 })
  await expect(unknown.getByRole('button', { name: RETRY_BUTTON })).toBeVisible()
  // The record may exist. The screen must not say it was saved.
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
  const u1Key = withheld[0].idempotencyKey
  expect(u1Key).not.toBeNull()

  const beforeRetry = net.currentTick()
  await unknown.getByRole('button', { name: RETRY_BUTTON }).click()
  await expect(page.getByText(SAVED_TEXT), 'the same-key retry must replay the real write').toBeVisible({ timeout: 60_000 })
  const v2 = await awaitContactRead(page, net, beforeRetry, 'after U1')
  expect(v2.version, 'U1 is one logical update, replay included').toBe('2')

  const afterU1 = contactPuts(net)
  expect(afterU1.length, 'U1 is one withheld send plus one same-key retry').toBe(2)
  expect(afterU1[1].idempotencyKey, 'the retry must carry the SAME key, never a new one').toBe(u1Key)
  expect(
    JSON.stringify(afterU1[1].requestBody),
    'the retry must carry the identical payload — the client-minted child ids included',
  ).toBe(JSON.stringify(afterU1[0].requestBody))
  expect(afterU1[1].status).toBe(204)
  expect(
    (afterU1[1].responseHeaders['idempotency-replayed'] ?? '').toLowerCase(),
    'the server itself must say this was a replay, not a second effect',
  ).toBe('true')

  const u1Sent = afterU1[0].requestBody as { expectedVersion?: unknown; changes?: Record<string, unknown> }
  expect(u1Sent.expectedVersion, 'U1 is fenced on the version the editor read').toBe('1')
  expect(Object.keys(u1Sent.changes ?? {}).sort()).toEqual(['addresses', 'contacts', 'email', 'phone', 'phoneExtension'])
  expect(v2.information?.phone).toBe(PHONE)
  expect(v2.information?.phoneExtension).toBe(EXTENSION)
  expect(v2.information?.email).toBe(EMAIL_U1)
  expect((v2.information?.addresses ?? []).length).toBeGreaterThanOrEqual(2)
  expect((v2.information?.contacts ?? []).length).toBeGreaterThanOrEqual(2)
  await lShot(page, SLUG, '03-u1-saved-v2')

  // ---- 3. U2: clearing a phone may not strand its extension ------------------------
  const putsBeforeRefusal = contactPuts(net).length
  await field(page, 'cari-contact-phone').fill('')
  await saveButton(page).click()
  await expect(
    page.getByText(/Dahili numara yalnız bir telefonla/),
    'the contract forbids a phone-only clear that leaves a retained extension; the refusal must be visible',
  ).toBeVisible({ timeout: 30_000 })
  expect(contactPuts(net).length, 'a client-side refusal sends nothing and mints no key').toBe(putsBeforeRefusal)

  await field(page, 'cari-contact-phoneExtension').fill('')
  const v3 = await saveAndRead(page, net, 'U2')
  expect(v3.version).toBe('3')
  expect(v3.information?.phone).toBeNull()
  expect(v3.information?.phoneExtension).toBeNull()
  const u2 = contactPuts(net).at(-1)!
  expect(u2.requestBody).toEqual({ expectedVersion: '2', changes: { phone: null, phoneExtension: null } })
  expect(u2.idempotencyKey, 'a new logical attempt gets its own key').not.toBe(u1Key)

  // ---- 4. U3: deactivate the TR delivery address ------------------------------------
  const trIndex = await addressIndexFor(page, ADDR_TR_LINE1)
  const trRow = page.locator(`[data-row-index="${trIndex}"]`)
  await trRow.getByRole('button', { name: 'Pasife al' }).click()
  await expect(trRow.getByText('Kaydedildiğinde pasife alınacak.')).toBeVisible()
  // A status operation carries the identity and the operation, and nothing else — so
  // while one is marked, the row's own fields are closed rather than silently dropped.
  await expect(field(page, `cari-address-${trIndex}-line1`)).toBeDisabled()
  const v4 = await saveAndRead(page, net, 'U3')
  expect(v4.version).toBe('4')
  const u3 = contactPuts(net).at(-1)!
  const trId = ((u3.requestBody as { changes?: { addresses?: { id: string }[] } }).changes?.addresses ?? [])[0]?.id
  expect(u3.requestBody).toEqual({
    expectedVersion: '3',
    changes: { addresses: [{ id: trId, operation: 'deactivate' }] },
  })
  expect((v4.information?.addresses ?? []).find((a) => a.id === trId)?.isActive).toBe(false)
  await lShot(page, SLUG, '04-u3-deactivated-v4')

  // ---- 5. U4: update the SAME passive address; an upsert does not reactivate --------
  const passiveIndex = await addressIndexFor(page, ADDR_TR_LINE1)
  await field(page, `cari-address-${passiveIndex}-line1`).fill(ADDR_TR_LINE1_UPDATED)
  const v5 = await saveAndRead(page, net, 'U4')
  expect(v5.version).toBe('5')
  expect(contactPuts(net).at(-1)!.requestBody).toEqual({
    expectedVersion: '4',
    changes: { addresses: [{ id: trId, operation: 'upsert', line1: ADDR_TR_LINE1_UPDATED }] },
  })
  const updatedRow = (v5.information?.addresses ?? []).find((a) => a.id === trId)
  expect(updatedRow?.line1).toBe(ADDR_TR_LINE1_UPDATED)
  expect(
    updatedRow?.isActive,
    'the contract is explicit that an upsert of an inactive child does not reactivate it',
  ).toBe(false)

  // ---- 6. U5: reactivate, explicitly -----------------------------------------------
  const reactivateIndex = await addressIndexFor(page, ADDR_TR_LINE1_UPDATED)
  await page
    .locator(`[data-row-index="${reactivateIndex}"]`)
    .getByRole('button', { name: 'Yeniden etkinleştir' })
    .click()
  const v6 = await saveAndRead(page, net, 'U5')
  expect(v6.version).toBe('6')
  expect(contactPuts(net).at(-1)!.requestBody).toEqual({
    expectedVersion: '5',
    changes: { addresses: [{ id: trId, operation: 'reactivate' }] },
  })
  expect((v6.information?.addresses ?? []).find((a) => a.id === trId)?.isActive).toBe(true)
  expect(contactPuts(net).length, 'the main page carries six PUTs: U1 + replay, U2, U3, U4, U5').toBe(6)
  await lShot(page, SLUG, '05-u5-reactivated-v6')

  // ---- 7. the second tab: a real stale fence, then the same edit under a new one ----
  await field(stale, 'cari-contact-email').fill(EMAIL_STALE)
  await saveButton(stale).click()
  const conflict = stale.locator('[data-failure="version-conflict"]')
  await expect(
    conflict,
    'this tab was seeded on version 1 and the record is now at 6: the fence is genuinely stale',
  ).toBeVisible({ timeout: 60_000 })
  expect(contactPuts(staleNet).length, 'a definite conflict is not retried under the same key').toBe(1)
  const conflictPut = contactPuts(staleNet)[0]
  expect(conflictPut.status).toBe(409)
  expect(conflictPut.requestBody).toEqual({ expectedVersion: '1', changes: { email: EMAIL_STALE } })
  // The draft survives the refusal — the user's own words are still on screen.
  await expect(field(stale, 'cari-contact-email')).toHaveValue(EMAIL_STALE)
  await lShot(stale, SLUG, '06-version-conflict')

  const beforeRefresh = staleNet.currentTick()
  await stale.getByRole('button', { name: 'Kaydı yenile' }).click()
  const refreshed = await awaitContactRead(stale, staleNet, beforeRefresh, 'stale refresh')
  expect(refreshed.version, 'the refresh must adopt the version the record actually has').toBe('6')
  await expect(
    stale.getByText('Kayıt yenilendi'),
    'the user is told the record moved, and their draft was kept for review',
  ).toBeVisible({ timeout: 30_000 })
  // Reviewed: what the server now holds is U1's e-mail, and the draft still asks for the
  // other one. Saving is the user's decision, taken on the NEW fence.
  await expect(field(stale, 'cari-contact-email')).toHaveValue(EMAIL_STALE)
  const v7 = await saveAndRead(stale, staleNet, 'U6')
  expect(v7.version).toBe('7')
  expect(v7.information?.email).toBe(EMAIL_STALE)
  const u6 = contactPuts(staleNet).at(-1)!
  expect(u6.requestBody).toEqual({ expectedVersion: '6', changes: { email: EMAIL_STALE } })
  expect(u6.idempotencyKey, 'the retried intent is a NEW attempt with its own key').not.toBe(conflictPut.idempotencyKey)
  expect(contactPuts(staleNet).length, 'the second tab carries two PUTs: the 409 and U6').toBe(2)
  await lShot(stale, SLUG, '07-u6-saved-v7')
  await stale.close()

  // ---- 8. the real return, reopening the record, and the keyboard ------------------
  await page.getByRole('link', { name: /Cari karta dön/ }).click()
  await expect(page.locator('[data-section="identity"]')).toBeVisible({ timeout: 30_000 })
  await page.getByRole('link', { name: /Cari Kartlar listesine dön/ }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Cari Kartlar' })).toBeVisible({ timeout: 30_000 })
  await page.getByRole('row', { name: new RegExp(partyCode) }).getByRole('link').first().click()
  await expect(page.locator('[data-section="account"]')).toBeVisible({ timeout: 30_000 })

  /**
   * Reached by KEYBOARD, not by a click: the editor has to be operable without a mouse,
   * and "the link exists" is not that. Tabbing is bounded — if the link is not reachable
   * the loop ends and the assertion says so.
   */
  let tabs = 0
  let reachedByKeyboard = false
  while (tabs < 60) {
    await page.keyboard.press('Tab')
    tabs += 1
    const focusedHref = await page.evaluate(() => (document.activeElement as HTMLAnchorElement | null)?.getAttribute('href') ?? null)
    if (focusedHref === `/cari/${partyId}/bilgiler`) {
      reachedByKeyboard = true
      break
    }
  }
  expect(reachedByKeyboard, `the editor link must be reachable from the card by keyboard (tried ${tabs} tab stops)`).toBe(true)
  const beforeReopen = net.currentTick()
  await page.keyboard.press('Enter')
  await expect(field(page, 'cari-contact-phone')).toBeVisible({ timeout: 30_000 })
  const reopened = await awaitContactRead(page, net, beforeReopen, 'reopen')
  expect(reopened.version, 'the reopened editor reads the record as it now stands').toBe('7')
  expect(reopened.information?.phone).toBeNull()
  expect(reopened.information?.email).toBe(EMAIL_STALE)
  expect((reopened.information?.addresses ?? []).some((a) => a.line1 === ADDR_TR_LINE1_UPDATED)).toBe(true)
  expect((reopened.information?.addresses ?? []).some((a) => a.line1 === ADDR_DE_LINE1)).toBe(true)
  expect((reopened.information?.contacts ?? []).some((c) => c.displayName === PERSON_1)).toBe(true)
  expect((reopened.information?.contacts ?? []).some((c) => c.displayName === PERSON_2)).toBe(true)
  await lShot(page, SLUG, '08-reopened-v7')

  // ---- 9. an unsaved draft is defended when the company changes --------------------
  await field(page, 'cari-contact-website').fill('https://example.test/terk-edilecek')
  const putsBeforeB = contactPuts(net).length
  await switchCompany(page, net, B, true)
  await page.goto(`/cari/${partyId}/bilgiler`)
  await page.waitForLoadState('networkidle', { timeout: 30_000 }).catch(() => {})
  const bEditorOpen = (await field(page, 'cari-contact-website').count()) > 0
  const bWebsite = bEditorOpen ? await field(page, 'cari-contact-website').inputValue() : null
  expect(bWebsite, "the draft prepared in company A must not appear in company B").not.toBe(
    'https://example.test/terk-edilecek',
  )
  const bLines: string[] = []
  for (const input of await page.locator('input[id^="cari-address-"][id$="-line1"]').all()) {
    bLines.push(await input.inputValue())
  }
  expect(bLines.includes(ADDR_TR_LINE1_UPDATED), "company A's address must never be rendered under company B").toBe(false)
  expect(contactPuts(net).filter((r) => r.companyId === B), 'this journey writes nothing in company B').toEqual([])
  expect(contactPuts(net).length).toBe(putsBeforeB)
  await lShot(page, SLUG, '09-company-b')

  // ---- 10. a READ-ONLY actor is offered nothing and writes nothing -----------------
  /**
   * The application bakes `VITE_DEV_USER` into its bundle at dev-server start, so a
   * browser context cannot become somebody else by itself. This context rewrites ONE
   * request header — the development identity — on the API calls it makes, and nothing
   * else: every request is real and every answer is the server's own. The actor is the
   * one the API owner named: company A's operator, given the Cari read permission in the
   * Testing profile and NOT the basic edit permission.
   */
  const readOnly: BrowserContext = await browser.newContext()
  const readOnlyPage = await readOnly.newPage()
  const readOnlyNet = LiveRecorder.attach(readOnlyPage)
  await readOnlyPage.addInitScript((id: string) => {
    window.localStorage.setItem('goymen-company-id', id)
  }, A)
  await readOnlyPage.route('**/api/v1/**', async (route) => {
    await route.continue({ headers: { ...route.request().headers(), 'x-development-user': READ_ONLY_ACTOR } })
  })
  await readOnlyPage.goto('/cari')
  await expect(readOnlyPage.getByRole('heading', { level: 1, name: 'Cari Kartlar' })).toBeVisible({ timeout: 60_000 })
  await readOnlyPage.getByRole('row', { name: new RegExp(partyCode) }).getByRole('link').first().click()
  await expect(readOnlyPage.locator('[data-section="account"]')).toBeVisible({ timeout: 30_000 })
  // The action is not offered: a capability the server did not grant is not advertised.
  await expect(
    readOnlyPage.getByRole('link', { name: 'İletişim ve adres bilgilerini düzenle' }),
    'a reader must not be offered an editor whose Save they cannot use',
  ).toHaveCount(0)
  // And the deep link is indistinguishable from an unknown path, and calls nothing.
  await readOnlyPage.goto(`/cari/${partyId}/bilgiler`)
  await expect(readOnlyPage.locator('[data-state="not-found"]')).toBeVisible({ timeout: 30_000 })
  expect(await field(readOnlyPage, 'cari-contact-phone').count()).toBe(0)
  const readOnlyPuts = contactPuts(readOnlyNet)
  expect(readOnlyPuts, 'a read-only actor writes nothing').toEqual([])
  await lShot(readOnlyPage, SLUG, '10-read-only-deep-link')
  const readOnlyRecords = readOnlyNet.records.map((r) => ({
    seq: r.seq,
    method: r.method,
    path: r.path,
    companyId: r.companyId,
    status: r.status,
    devIdentityHeader: r.devIdentityHeader,
  }))
  const readOnlyBarrier = await readOnlyNet.quiesce({ settleMs: 1000, timeoutMs: 30_000 })
  await readOnly.close()

  // ---- 11. the barrier, and the evidence BEFORE the verdict ------------------------
  const barrier = await net.quiesce({ settleMs: 1500, timeoutMs: 45_000 })
  let evidenceError: unknown = null
  try {
    writeLJson('measurement', `${SLUG}-network.json`, {
      phase: 'measurement',
      slug: SLUG,
      runId: manifest.runId,
      instanceId: manifest.instanceId,
      sourceContract: 'Backend #236 9df7f40e3aefc5b02ef42a378c0d1aa800a2fe29',
      jointTestIntent: 'issue #66 comment 5624159337',
      partyId,
      partyCode,
      companyA: A,
      companyB: B,
      readOnlyActor: READ_ONLY_ACTOR,
      versions: { start: '1', afterU1: '2', afterU2: '3', afterU3: '4', afterU4: '5', afterU5: '6', afterU6: '7' },
      expected: {
        puts: 8,
        successfulLogicalUpdates: 6,
        auditEvents: 6,
        durableUniqueKeys: 7,
        durableOutcomes: { succeeded204: 6, versionConflict409: 1 },
        newParties: 0,
        newAccounts: 0,
        companyBWrites: 0,
      },
      /** MAIN page: U1 original, U1 replay, U2, U3, U4, U5. `seq` is page-local. */
      contactPuts: contactPuts(net).map(putProjection),
      /** SECOND page: the stale 409 and U6. Same projection; `seq` is page-local. */
      staleContactPuts: contactPuts(staleNet).map(putProjection),
      contactGets: contactGets(net).map((r) => ({
        seq: r.seq,
        companyId: r.companyId,
        status: r.status,
        completed: isCompletedRead(r, CONTRACT),
        version: (r.responseBody as ContactBody | null)?.version ?? null,
      })),
      staleContactGets: contactGets(staleNet).map((r) => ({
        seq: r.seq,
        companyId: r.companyId,
        status: r.status,
        completed: isCompletedRead(r, CONTRACT),
        version: (r.responseBody as ContactBody | null)?.version ?? null,
      })),
      withheld: withholder.withheld,
      passedThrough: withholder.passedThrough,
      keyboardTabStopsToEditor: tabs,
      companyBEditorOpened: bEditorOpen,
      companyBAddressLines: bLines,
      readOnly: { actor: READ_ONLY_ACTOR, records: readOnlyRecords, barrier: readOnlyBarrier },
      barrier,
      observedEvents: net.observedEvents(),
      staleObservedEvents: staleNet.observedEvents(),
      boundaries: [
        'No Party and no company account was created: ExpectedNewParties is 0 for this journey.',
        'Six successful logical updates against one account in company A, v1 to v7. The database effect is measured by the environment snapshot, not by this browser.',
        'The withheld exchange was a real 204 from the real API; its delivery to the browser was cut after the server had already committed. Nothing was mocked and no response was ever synthesised.',
        'The read-only context rewrites ONLY the X-Development-User header on its own API requests; the requests and the answers are real. It is NOT a production identity-provider session and no revocation was performed.',
        'A session revocation is NOT measured anywhere in this journey: the isolated Testing host authenticates by X-Development-User and offers no sign-out.',
        'The company-B step records what was observed. It does not claim B holds or lacks an account for this Party; it asserts only that no value or draft from A appeared there and that nothing was written.',
        'seq is page-local in each projection. Joining the browser and the SQL evidence is done on key/company/actor/payload/outcome/response, not on seq.',
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
