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
 * NOTHING IS FULFILLED. `DeliveryWithholder` calls `route.fetch` and then aborts the
 * delivery; every status, body and header in this file is the server's own.
 */
import { expect, test } from '@playwright/test'
import {
  ORG_LIST_PATH,
  actionTrigger,
  cardHeading,
  cardState,
  dialog,
  listHeading,
  locationCardPath,
  locationLink,
  outcome,
  pageItems,
  submitReasonedChange,
  versionOf,
} from '../src/journey.js'
import { DeliveryWithholder, LiveRecorder, shot } from '../src/recorder.js'
import { readOrganizationManifest } from '../src/manifest.js'

const manifest = readOrganizationManifest()
const GROUP = 'o-03-conflict-unknown-replay'
const TARGET_CODE = 'OSM-H0'
const ACTIVE_STATE_RE = /\/api\/v1\/organization\/locations\/[^/]+\/active-state$/
const RESEND_BUTTON = /Aynı işlem anahtarıyla tekrar gönder/
const RELOAD_BUTTON = 'Güncel kaydı yükle'

test.describe('O-03 conflict, unknown and replay', () => {
  test('a stale version is refused 409 and the justification draft survives the recovery', async ({
    page,
    context,
  }) => {
    const net = await LiveRecorder.attach(page)
    await page.goto(ORG_LIST_PATH)
    await expect(listHeading(page)).toBeVisible()
    await locationLink(page, TARGET_CODE).click()
    await expect(cardHeading(page)).toBeVisible()

    const staleVersion = versionOf(net.locationDetailReads().at(-1)!.responseBody)
    const locationId = net.locationDetailReads().at(-1)!.path.split('/').at(-1)!

    /**
     * A SECOND real tab moves the record on, exactly as another user would. The first
     * tab is now holding a version the server has superseded — which is the only honest
     * way to reach a version conflict.
     */
    const other = await context.newPage()
    const otherNet = await LiveRecorder.attach(other)
    await other.goto(locationCardPath(locationId))
    await expect(cardHeading(other)).toBeVisible()
    await submitReasonedChange(other, `${manifest.runPrefix} diğer sekme değişikliği`)
    await expect(other.locator(outcome.applied)).toBeVisible()
    expect(otherNet.activeStatePuts().at(-1)!.status).toBe(204)
    await other.close()

    // The first tab submits on the version it read before the other tab moved it.
    const reason = `${manifest.runPrefix} eski sürümle gönderim`
    await submitReasonedChange(page, reason)

    const conflict = net.activeStatePuts().at(-1)!
    expect(conflict.status).toBe(409)
    expect(conflict.requestBody).toMatchObject({ expectedVersion: String(staleVersion) })
    // The version discriminator, from the server's own body.
    const body = conflict.responseBody as { conflict?: { expectedVersion: number; currentVersion: number } }
    expect(body.conflict?.expectedVersion).toBe(staleVersion)
    expect(body.conflict?.currentVersion).toBeGreaterThan(staleVersion)
    // A first-time conflict is not a replay, so its numbers are current.
    expect(conflict.responseHeaders?.['idempotency-replayed']).toBeUndefined()
    await expect(page.locator(outcome.versionConflict)).toBeVisible()

    // The recommended recovery must not cost the user their text.
    await page.getByRole('button', { name: RELOAD_BUTTON }).click()
    await expect(dialog(page).getByLabel('Gerekçe')).toHaveValue(reason)

    await shot(page, GROUP, '01-version-conflict')
    net.write(GROUP, 'version-conflict.json')
  })

  test('a lost reply is UNKNOWN, and the same key is replayed rather than written twice', async ({
    page,
  }) => {
    const net = await LiveRecorder.attach(page)
    const withholder = new DeliveryWithholder(page, ACTIVE_STATE_RE)
    await withholder.arm()

    await page.goto(ORG_LIST_PATH)
    await expect(listHeading(page)).toBeVisible()
    await locationLink(page, TARGET_CODE).click()
    await expect(cardHeading(page)).toBeVisible()
    const stateBefore = await cardState(page)
    const versionBefore = versionOf(net.locationDetailReads().at(-1)!.responseBody)

    await submitReasonedChange(page, `${manifest.runPrefix} teslimi kaybolan komut`)

    // The server COMMITTED it; only the browser's delivery was dropped.
    await expect(page.locator(outcome.unknown)).toBeVisible()
    expect(withholder.withheldStatus).toBe(204)
    const originalKey = withholder.withheldKey
    expect(originalKey).toBeTruthy()

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
    await expect(page.locator(outcome.replayed)).toBeVisible()

    const puts = net.activeStatePuts()
    expect(puts.length).toBeGreaterThanOrEqual(2)
    const replay = puts.at(-1)!
    expect(replay.requestHeaders['idempotency-key']).toBe(originalKey)
    expect(replay.status).toBe(204)
    // The SERVER says this is a replay. That is the assertion; we do not infer it.
    expect(replay.responseHeaders?.['idempotency-replayed']).toBe('true')
    expect(replay.responseHeaders?.['idempotency-original-correlation-id']).toBeTruthy()

    /**
     * Exactly ONE logical change, despite two requests: the version moved by one, not
     * two. This is what the key bought.
     */
    const fresh = net.locationDetailReads().filter((r) => r.issuedAt > replay.settledAt!)
    expect(fresh.length).toBeGreaterThan(0)
    expect(versionOf(fresh.at(-1)!.responseBody)).toBe(versionBefore + 1)
    expect(await cardState(page)).not.toBe(stateBefore)

    await shot(page, GROUP, '03-replayed')
    net.write(GROUP, 'unknown-replay.json')
  })

  test('a delayed replay of an older command is history, not the current state', async ({
    page,
  }) => {
    /**
     * The fact PR #241 pins and the screen must respect: replaying a command that
     * deactivated a record, AFTER someone reactivated it, still answers 204 — because
     * the server is repeating that command's terminal answer, not describing the row.
     * So the notice may say "already recorded" and the CARD must still show what a fresh
     * read says.
     */
    const net = await LiveRecorder.attach(page)
    const withholder = new DeliveryWithholder(page, ACTIVE_STATE_RE)
    await withholder.arm()

    await page.goto(ORG_LIST_PATH)
    await expect(listHeading(page)).toBeVisible()
    await locationLink(page, TARGET_CODE).click()
    await expect(cardHeading(page)).toBeVisible()
    const startState = await cardState(page)

    // Command A: committed server-side, reply withheld → unresolved.
    await submitReasonedChange(page, `${manifest.runPrefix} gecikmeli replay komutu`)
    await expect(page.locator(outcome.unknown)).toBeVisible()
    expect(withholder.withheldStatus).toBe(204)

    // Command B, from another real tab, puts the record BACK to where it started.
    const other = await page.context().newPage()
    const otherNet = await LiveRecorder.attach(other)
    const locationId = net.locationDetailReads().at(-1)!.path.split('/').at(-1)!
    await other.goto(locationCardPath(locationId))
    await expect(cardHeading(other)).toBeVisible()
    await submitReasonedChange(other, `${manifest.runPrefix} geri alma`)
    await expect(other.locator(outcome.applied)).toBeVisible()
    expect(otherNet.activeStatePuts().at(-1)!.status).toBe(204)
    await other.close()

    // Now command A is retried under its own key: a DELAYED replay.
    await page.getByRole('button', { name: RESEND_BUTTON }).click()
    await expect(page.locator(outcome.replayed)).toBeVisible()
    const replay = net.activeStatePuts().at(-1)!
    expect(replay.status).toBe(204)
    expect(replay.responseHeaders?.['idempotency-replayed']).toBe('true')

    /**
     * The card follows the FRESH read, not the replayed receipt: the record is back at
     * its starting state, so that is what the user must see.
     */
    const fresh = net.locationDetailReads().filter((r) => r.issuedAt > replay.settledAt!)
    expect(fresh.length).toBeGreaterThan(0)
    expect(await cardState(page)).toBe(startState)

    await shot(page, GROUP, '04-delayed-replay')
    net.write(GROUP, 'delayed-replay.json')
  })

  test.afterEach(async ({ page }) => {
    // Leave nothing half-open; the environment owner does the real cleanup.
    await expect(actionTrigger(page).or(listHeading(page)).first()).toBeVisible({ timeout: 5_000 })
  })
})
