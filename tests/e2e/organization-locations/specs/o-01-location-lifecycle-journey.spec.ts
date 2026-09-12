/**
 * O-01 — the whole authorized path, for real: facilities and paged locations → the
 * card → a justified deactivation → the CURRENT view → back to the page it started on
 * → a justified reactivation on a freshly read version.
 *
 * SOURCE CONTRACT, pinned and not invented here: the merged Organization endpoints and
 * DTOs (`OrganizationEndpoints.cs`, `OrganizationModels.cs`, `OrganizationService.cs`),
 * the third Backend's handover (issue #66 comment `5646385043`), Business's narrow
 * acceptance package (comment `5646417811`) and the fixture manifest
 * `tests/e2e-fixtures/organization-locations/fixture.json`. PR #241 already carries the
 * real SQL lifecycle evidence; this measures what a browser adds to it.
 *
 * WHAT ONLY THE BROWSER CAN SHOW HERE, and why each assertion exists:
 *
 *  - a 204 carries NO body and NO version, so the screen cannot have derived the new
 *    state from the command. We assert the app issued a fresh GET and that the card
 *    reflects THAT — not the state we asked for;
 *  - `version + 1` is never predicted: the second command's `expectedVersion` must equal
 *    the version the app actually READ, and the two are compared from the wire;
 *  - the originating page survives the round trip, which is a real navigation through
 *    the shell's router rather than a component-level claim;
 *  - the list behind the card is refreshed, so the new state is visible where the user
 *    came from.
 *
 * NOTHING IS MOCKED. There is no `route.fulfill` in this file. Every status, body,
 * version and replay header is the server's own.
 *
 * NOT MEASURED HERE: database rows (PR #241 and the environment's own snapshot own
 * that), audit contents, and anything about companies, stock, ownership or capacity.
 */
import { expect, test } from '@playwright/test'
import {
  ORG_LIST_PATH,
  backLink,
  cardHeading,
  cardState,
  listHeading,
  locationLink,
  outcome,
  pageItems,
  submitReasonedChange,
  versionOf,
} from '../src/journey.js'
import { LiveRecorder, attachEvidence, shot } from '../src/recorder.js'
import { readOrganizationManifest } from '../src/manifest.js'

const manifest = readOrganizationManifest()
const GROUP = 'o-01-location-lifecycle'
/** The fixture's mutation selector. Read from the list, never manufactured. */
const TARGET_CODE = 'OSM-H0'
const DEACTIVATE_REASON = `${manifest.runPrefix} bakım için pasife alma`
const REACTIVATE_REASON = `${manifest.runPrefix} bakım tamamlandı`

test.describe('O-01 location lifecycle', () => {
  test('list → card → reasoned deactivate → fresh view → return → reasoned reactivate', async ({
    page,
  }, testInfo) => {
    const net = await LiveRecorder.attach(page)

    // ---- 1. The list, from the live API -------------------------------------------
    await page.goto(ORG_LIST_PATH)
    await expect(listHeading(page)).toBeVisible()
    await expect(locationLink(page, TARGET_CODE)).toBeVisible()

    const firstPage = net.locationPageReads()
    expect(firstPage.length).toBeGreaterThan(0)
    // Offset paging, and nothing from the Cari protocol.
    expect(firstPage[0].queryKeys.sort()).toEqual(['page', 'pageSize'])
    expect(firstPage[0].queryKeys).not.toContain('cursor')
    expect(firstPage[0].requestHeaders['x-company-id']).toBeTruthy()
    const listed = pageItems(firstPage[0].responseBody)
    const target = listed.find((row) => row.code === TARGET_CODE)
    expect(target, `${TARGET_CODE} must be on the first page of this fixture`).toBeTruthy()

    await shot(page, GROUP, '01-list')

    // ---- 2. The card --------------------------------------------------------------
    await locationLink(page, TARGET_CODE).click()
    await expect(cardHeading(page)).toBeVisible()
    expect(await cardState(page)).toBe('active')

    const beforeDetail = net.locationDetailReads()
    expect(beforeDetail.length).toBeGreaterThan(0)
    const versionBefore = versionOf(beforeDetail.at(-1)!.responseBody)

    // ---- 3. The justified deactivation --------------------------------------------
    await submitReasonedChange(page, DEACTIVATE_REASON)
    await expect(page.locator(outcome.applied)).toBeVisible()

    const puts = net.activeStatePuts()
    expect(puts).toHaveLength(1)
    const deactivate = puts[0]
    expect(deactivate.status).toBe(204)
    // The exact three contract fields, the version we READ, and the trimmed reason.
    expect(deactivate.requestBody).toEqual({
      isActive: false,
      expectedVersion: String(versionBefore),
      reason: DEACTIVATE_REASON,
    })
    expect(deactivate.requestHeaders['idempotency-key']).toBeTruthy()
    // A first send is not a replay.
    expect(deactivate.responseHeaders?.['idempotency-replayed']).toBeUndefined()

    // ---- 4. The CURRENT view, which the command could not have supplied ------------
    /**
     * A 204 has no body and no version. So the card below must come from a read the app
     * issued AFTER the command settled — that ordering is the assertion, not a detail.
     */
    const afterCommand = net
      .locationDetailReads()
      .filter((r) => r.issuedAt > deactivate.settledAt!)
    expect(
      afterCommand.length,
      'the screen must re-read the record; a 204 states no resulting version',
    ).toBeGreaterThan(0)
    const fresh = afterCommand.at(-1)!
    expect(fresh.status).toBe(200)
    const versionAfter = versionOf(fresh.responseBody)
    expect(versionAfter).toBe(versionBefore + 1)
    expect(await cardState(page)).toBe('passive')
    await shot(page, GROUP, '02-deactivated')

    // ---- 5. Back to the page the card was opened from ------------------------------
    await backLink(page).click()
    await expect(listHeading(page)).toBeVisible()
    const listAfter = net.locationPageReads().filter((r) => r.issuedAt > deactivate.settledAt!)
    expect(
      listAfter.length,
      'the list behind the card must be refreshed, not served from the pre-change cache',
    ).toBeGreaterThan(0)
    const refreshed = pageItems(listAfter.at(-1)!.responseBody).find((r) => r.code === TARGET_CODE)
    expect(refreshed?.isActive).toBe(false)

    // ---- 6. Reactivate on a FRESHLY read version ----------------------------------
    await locationLink(page, TARGET_CODE).click()
    await expect(cardHeading(page)).toBeVisible()
    expect(await cardState(page)).toBe('passive')
    const reopened = net.locationDetailReads().at(-1)!
    const versionForReactivate = versionOf(reopened.responseBody)

    await submitReasonedChange(page, REACTIVATE_REASON)
    await expect(page.locator(outcome.applied)).toBeVisible()

    const secondPut = net.activeStatePuts().at(-1)!
    expect(net.activeStatePuts()).toHaveLength(2)
    expect(secondPut.status).toBe(204)
    expect(secondPut.requestBody).toEqual({
      isActive: true,
      expectedVersion: String(versionForReactivate),
      reason: REACTIVATE_REASON,
    })
    /**
     * A NEW key: this is a different business command, not a retry of the first. The
     * same key would ask the server to replay the deactivation.
     */
    expect(secondPut.requestHeaders['idempotency-key']).not.toBe(
      deactivate.requestHeaders['idempotency-key'],
    )

    const finalRead = net.locationDetailReads().filter((r) => r.issuedAt > secondPut.settledAt!)
    expect(finalRead.length).toBeGreaterThan(0)
    expect(versionOf(finalRead.at(-1)!.responseBody)).toBe(versionForReactivate + 1)
    expect(await cardState(page)).toBe('active')
    await shot(page, GROUP, '03-reactivated')

    net.write(GROUP, 'network.json')
  })

  test.afterEach(async ({ page }, testInfo) => {
    if (testInfo.status === testInfo.expectedStatus) return
    const net = await LiveRecorder.attach(page)
    await attachEvidence(testInfo, net, GROUP)
  })
})
