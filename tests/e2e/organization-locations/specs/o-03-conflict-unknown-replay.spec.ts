/**
 * O-03 — the three outcomes a command can have that are NOT a clean success: a stale
 * version, a lost reply, and a server-side replay of a key that was already used.
 *
 * SOURCE CONTRACT: the merged endpoint's idempotency handling, the third Backend's
 * handover (issue #66 comment `5646385043`) and PR #241's SQL lifecycle evidence —
 * version conflicts carry `conflict:{expectedVersion,currentVersion}`, an idempotency
 * conflict carries no such object, a replay repeats the stored terminal answer with
 * `Idempotency-Replayed: true`, and a replayed 409's numbers can be historical.
 *
 * WHY THIS CANNOT BE DONE IN JSDOM, which is the whole justification for the case:
 *
 *  - the 409 must come from a real optimistic-concurrency check on a real row, not from
 *    a status we chose;
 *  - the replay must be the SERVER deciding it has seen this key before. A synthesised
 *    204 with a replay header proves our parser, not the contract;
 *  - the unknown outcome is produced by withholding the browser's DELIVERY of a real
 *    answer after the server has already committed — so the command genuinely is
 *    recorded and the client genuinely does not know. A fabricated 503 is a different
 *    situation with the same shape, and it is the one the key does not exist for.
 *
 * EVERY ASSERTION WAITS FOR THE EXCHANGE, never for a notice. A receipt renders before
 * the fresh read behind it finishes, so each step polls for the completed record.
 *
 * NOTHING IS FULFILLED. `DeliveryWithholder` calls `route.fetch` and then aborts the
 * delivery; every status, body and header in this file is the server's own.
 */
import { expect, test } from '@playwright/test'
import {
  ORG_LIST_PATH,
  cardHeading,
  cardState,
  dialog,
  listHeading,
  locationCardPath,
  locationLink,
  outcome,
  submitReasonedChange,
  versionOf,
} from '../src/journey.js'
import {
  ACTIVE_STATE_RE,
  DETAIL_RE,
  LOCATIONS_RE,
  DeliveryWithholder,
  LiveRecorder,
  attachEvidence,
  shot,
} from '../src/recorder.js'
import { readOrganizationManifest } from '../src/manifest.js'

const manifest = readOrganizationManifest()
const GROUP = 'o-03-conflict-unknown-replay'
const TARGET_CODE = 'OSM-H0'
const RESEND_BUTTON = /Aynı işlem anahtarıyla tekrar gönder/
const RELOAD_BUTTON = 'Güncel kaydı yükle'

let active: LiveRecorder | null = null

/** Opens the list and the target card, and returns the card's completed detail read. */
async function openCard(page: import('@playwright/test').Page, net: LiveRecorder) {
  await page.goto(ORG_LIST_PATH)
  await net.waitForNth('the list page read', LOCATIONS_RE, 'GET', 0)
  await expect(listHeading(page)).toBeVisible()
  await locationLink(page, TARGET_CODE).click()
  const detail = await net.waitForNth('the card detail read', DETAIL_RE, 'GET', 0)
  expect(detail.status).toBe(200)
  await expect(cardHeading(page)).toBeVisible()
  return detail
}

test.describe('O-03 conflict, unknown and replay', () => {
  test('a stale version is refused 409 and the justification draft survives the recovery', async ({
    page,
    context,
  }) => {
    const net = LiveRecorder.attach(page)
    active = net
    const detail = await openCard(page, net)
    const staleVersion = versionOf(detail.responseBody)
    const locationId = detail.path.split('/').at(-1)!

    /**
     * A SECOND real tab moves the record on, exactly as another user would. The first tab
     * is then holding a version the server has superseded — the only honest way to reach
     * a version conflict.
     */
    const other = await context.newPage()
    const otherNet = LiveRecorder.attach(other)
    await other.goto(locationCardPath(locationId))
    await otherNet.waitForNth('the second tab detail read', DETAIL_RE, 'GET', 0)
    await expect(cardHeading(other)).toBeVisible()
    await submitReasonedChange(other, `${manifest.runPrefix} diğer sekme değişikliği`)
    const otherPut = await otherNet.waitForNth(
      'the second tab command',
      ACTIVE_STATE_RE,
      'PUT',
      0,
    )
    expect(otherPut.status).toBe(204)
    await other.close()

    // The first tab submits on the version it read before the other tab moved it.
    const reason = `${manifest.runPrefix} eski sürümle gönderim`
    await submitReasonedChange(page, reason)
    const conflict = await net.waitForNth('the stale command', ACTIVE_STATE_RE, 'PUT', 0)

    expect(conflict.status).toBe(409)
    expect(conflict.requestBody).toMatchObject({ expectedVersion: String(staleVersion) })
    // The version discriminator, from the server's own body.
    const body = conflict.responseBody as {
      conflict?: { expectedVersion: number; currentVersion: number }
    }
    expect(body.conflict?.expectedVersion).toBe(staleVersion)
    expect(body.conflict?.currentVersion).toBeGreaterThan(staleVersion)
    // A first-time conflict is not a replay, so its numbers are current.
    expect(conflict.responseHeaders?.['idempotency-replayed']).toBeUndefined()
    await expect(page.locator(outcome.versionConflict)).toBeVisible()

    // The recommended recovery must not cost the user their text.
    await page.getByRole('button', { name: RELOAD_BUTTON }).click()
    await expect(dialog(page).getByLabel('Gerekçe')).toHaveValue(reason)

    await shot(page, GROUP, '01-version-conflict')
    await net.write(GROUP, 'version-conflict.json')
  })

  test('a lost reply is UNKNOWN, and the same key is replayed rather than written twice', async ({
    page,
  }) => {
    const net = LiveRecorder.attach(page)
    active = net
    const withholder = new DeliveryWithholder(page, ACTIVE_STATE_RE)
    await withholder.arm()

    const detail = await openCard(page, net)
    const versionBefore = versionOf(detail.responseBody)
    const stateBefore = await cardState(page)

    await submitReasonedChange(page, `${manifest.runPrefix} teslimi kaybolan komut`)

    // The server COMMITTED it; only the browser's delivery was dropped.
    await withholder.waitForWithheld()
    expect(withholder.withheldStatus).toBe(204)
    const originalKey = withholder.withheldKey
    expect(originalKey).toBeTruthy()
    await expect(page.locator(outcome.unknown)).toBeVisible()

    /**
     * The screen must not claim the change failed, and must not offer an ordinary
     * submit: editing the justification would change the payload fingerprint and mint a
     * SECOND key for a command that is already recorded.
     */
    await expect(dialog(page).getByLabel('Gerekçe')).toHaveAttribute('readonly', '')
    await expect(dialog(page).getByRole('button', { name: /Pasife al|Aktifleştir/ })).toHaveCount(0)
    await shot(page, GROUP, '02-unknown')

    // The one way forward: the SAME key, which the server replays.
    await page.getByRole('button', { name: RESEND_BUTTON }).click()
    const replay = await net.waitForNth('the same-key resend', ACTIVE_STATE_RE, 'PUT', 1)

    expect(replay.requestHeaders['idempotency-key']).toBe(originalKey)
    expect(replay.status).toBe(204)
    // The SERVER says this is a replay. That is the assertion; we do not infer it.
    expect(replay.responseHeaders?.['idempotency-replayed']).toBe('true')
    expect(replay.responseHeaders?.['idempotency-original-correlation-id']).toBeTruthy()

    /**
     * Exactly ONE logical change, despite two requests: the version moved by one, not
     * two. That is what the key bought.
     */
    const fresh = await net.waitForAfter(
      'a detail read issued after the replay arrived',
      DETAIL_RE,
      'GET',
      replay.receivedAt!,
    )
    expect(versionOf(fresh.responseBody)).toBe(versionBefore + 1)
    await expect(page.locator(outcome.replayed)).toBeVisible()
    await expect.poll(() => cardState(page)).not.toBe(stateBefore)

    await shot(page, GROUP, '03-replayed')
    await net.write(GROUP, 'unknown-replay.json')
  })

  test('a delayed replay of an older command is history, not the current state', async ({
    page,
    context,
  }) => {
    /**
     * The fact PR #241 pins and the screen must respect: replaying a command that
     * deactivated a record, AFTER someone reactivated it, still answers 204 — the server
     * is repeating that command's terminal answer, not describing the row. So the notice
     * may say "already recorded" while the CARD shows what a fresh read says.
     */
    const net = LiveRecorder.attach(page)
    active = net
    const withholder = new DeliveryWithholder(page, ACTIVE_STATE_RE)
    await withholder.arm()

    const detail = await openCard(page, net)
    const locationId = detail.path.split('/').at(-1)!
    const startState = await cardState(page)

    // Command A: committed server-side, reply withheld → unresolved.
    await submitReasonedChange(page, `${manifest.runPrefix} gecikmeli replay komutu`)
    await withholder.waitForWithheld()
    expect(withholder.withheldStatus).toBe(204)
    await expect(page.locator(outcome.unknown)).toBeVisible()

    // Command B, from another real tab, puts the record BACK to where it started.
    const other = await context.newPage()
    const otherNet = LiveRecorder.attach(other)
    await other.goto(locationCardPath(locationId))
    await otherNet.waitForNth('the second tab detail read', DETAIL_RE, 'GET', 0)
    await expect(cardHeading(other)).toBeVisible()
    await submitReasonedChange(other, `${manifest.runPrefix} geri alma`)
    const undo = await otherNet.waitForNth('the undo command', ACTIVE_STATE_RE, 'PUT', 0)
    expect(undo.status).toBe(204)
    await other.close()

    // Now command A is retried under its own key: a DELAYED replay.
    await page.getByRole('button', { name: RESEND_BUTTON }).click()
    const replay = await net.waitForNth('the delayed replay', ACTIVE_STATE_RE, 'PUT', 1)
    expect(replay.status).toBe(204)
    expect(replay.responseHeaders?.['idempotency-replayed']).toBe('true')

    /**
     * The card follows the FRESH read, not the replayed receipt: the record is back at
     * its starting state, so that is what the user must see.
     */
    const fresh = await net.waitForAfter(
      'a detail read issued after the delayed replay arrived',
      DETAIL_RE,
      'GET',
      replay.receivedAt!,
    )
    expect(fresh.status).toBe(200)
    await expect(page.locator(outcome.replayed)).toBeVisible()
    await expect.poll(() => cardState(page)).toBe(startState)

    await shot(page, GROUP, '04-delayed-replay')
    await net.write(GROUP, 'delayed-replay.json')
  })

  test.afterEach(async ({}, testInfo) => {
    if (active !== null && testInfo.status !== testInfo.expectedStatus) {
      await attachEvidence(testInfo, active, GROUP)
    }
    active = null
  })
})
