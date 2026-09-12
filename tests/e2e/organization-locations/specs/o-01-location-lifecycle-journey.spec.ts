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
 * EVERY ASSERTION WAITS FOR THE EXCHANGE, NOT FOR PAINT. The list heading renders before
 * its data lands, and the success notice renders before the fresh read that follows a
 * 204 has finished — so asserting straight after either would be timing-dependent and
 * would fail on ordinary real-API latency. Each step below polls for the COMPLETED
 * record (status plus body) it is actually about.
 *
 * WHAT ONLY THE REAL STACK SHOWS HERE:
 *  - a 204 carries NO body and NO version, so the card cannot have come from the
 *    command. We wait for a detail read ISSUED AFTER the command settled and assert the
 *    card against that;
 *  - `version + 1` is never predicted: the second command's `expectedVersion` equals the
 *    version actually READ, compared from the wire;
 *  - the originating page survives a real router round trip, and the list behind the
 *    card is refreshed rather than served from the pre-change cache.
 *
 * NOTHING IS MOCKED. There is no `route.fulfill` in this file.
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
import {
  ACTIVE_STATE_RE,
  DETAIL_RE,
  LOCATIONS_RE,
  LiveRecorder,
  attachEvidence,
  shot,
} from '../src/recorder.js'
import { readOrganizationManifest } from '../src/manifest.js'

const manifest = readOrganizationManifest()
const GROUP = 'o-01-location-lifecycle'
/** The fixture's mutation selector. Read from the list, never manufactured. */
const TARGET_CODE = 'OSM-H0'
const DEACTIVATE_REASON = `${manifest.runPrefix} bakım için pasife alma`
const REACTIVATE_REASON = `${manifest.runPrefix} bakım tamamlandı`

/** The recorder of the case currently running, so a failure attaches ITS traffic. */
let active: LiveRecorder | null = null

test.describe('O-01 location lifecycle', () => {
  test('list → card → reasoned deactivate → fresh view → return → reasoned reactivate', async ({
    page,
  }) => {
    const net = LiveRecorder.attach(page)
    active = net

    // ---- 1. The list, from the live API -------------------------------------------
    await page.goto(ORG_LIST_PATH)
    const firstPage = await net.waitForNth('the first location page read', LOCATIONS_RE, 'GET', 0)
    expect(firstPage.status).toBe(200)
    // Offset paging, and nothing from the Cari protocol.
    expect(firstPage.queryKeys.sort()).toEqual(['page', 'pageSize'])
    expect(firstPage.queryKeys).not.toContain('cursor')
    expect(firstPage.requestHeaders['x-company-id']).toBeTruthy()
    const listed = pageItems(firstPage.responseBody)
    const target = listed.find((row) => row.code === TARGET_CODE)
    expect(target, `${TARGET_CODE} must be on the first page of this fixture`).toBeTruthy()

    await expect(listHeading(page)).toBeVisible()
    await expect(locationLink(page, TARGET_CODE)).toBeVisible()
    await shot(page, GROUP, '01-list')

    // ---- 2. The card --------------------------------------------------------------
    await locationLink(page, TARGET_CODE).click()
    const beforeDetail = await net.waitForNth('the card detail read', DETAIL_RE, 'GET', 0)
    expect(beforeDetail.status).toBe(200)
    const versionBefore = versionOf(beforeDetail.responseBody)
    await expect(cardHeading(page)).toBeVisible()
    expect(await cardState(page)).toBe('active')

    // ---- 3. The justified deactivation --------------------------------------------
    await submitReasonedChange(page, DEACTIVATE_REASON)
    const deactivate = await net.waitForNth('the deactivate command', ACTIVE_STATE_RE, 'PUT', 0)

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
     * A 204 has no body and no version, so the card must come from a read the app issued
     * AFTER the command settled. That ordering is the assertion; the success notice is
     * only a receipt and appears before this read finishes.
     */
    const fresh = await net.waitForAfter(
      'a detail read issued after the command settled',
      DETAIL_RE,
      'GET',
      deactivate.settledAt!,
    )
    expect(fresh.status).toBe(200)
    const versionAfter = versionOf(fresh.responseBody)
    expect(versionAfter).toBe(versionBefore + 1)
    await expect(page.locator(outcome.applied)).toBeVisible()
    await expect.poll(() => cardState(page)).toBe('passive')
    await shot(page, GROUP, '02-deactivated')

    // ---- 5. Back to the page the card was opened from ------------------------------
    await backLink(page).click()
    const listAfter = await net.waitForAfter(
      'a list read issued after the command, i.e. not the pre-change cache',
      LOCATIONS_RE,
      'GET',
      deactivate.settledAt!,
    )
    expect(listAfter.status).toBe(200)
    await expect(listHeading(page)).toBeVisible()
    const refreshed = pageItems(listAfter.responseBody).find((r) => r.code === TARGET_CODE)
    expect(refreshed?.isActive).toBe(false)

    // ---- 6. Reactivate on a FRESHLY read version ----------------------------------
    await locationLink(page, TARGET_CODE).click()
    const reopened = await net.waitForAfter(
      'the reopened card detail read',
      DETAIL_RE,
      'GET',
      listAfter.settledAt!,
    )
    expect(reopened.status).toBe(200)
    const versionForReactivate = versionOf(reopened.responseBody)
    await expect(cardHeading(page)).toBeVisible()
    expect(await cardState(page)).toBe('passive')

    await submitReasonedChange(page, REACTIVATE_REASON)
    const secondPut = await net.waitForNth('the reactivate command', ACTIVE_STATE_RE, 'PUT', 1)

    expect(secondPut.status).toBe(204)
    expect(secondPut.requestBody).toEqual({
      isActive: true,
      expectedVersion: String(versionForReactivate),
      reason: REACTIVATE_REASON,
    })
    /**
     * A NEW key: this is a different business command, not a retry. The same key would
     * ask the server to replay the deactivation instead.
     */
    expect(secondPut.requestHeaders['idempotency-key']).not.toBe(
      deactivate.requestHeaders['idempotency-key'],
    )

    const finalRead = await net.waitForAfter(
      'a detail read issued after the reactivation settled',
      DETAIL_RE,
      'GET',
      secondPut.settledAt!,
    )
    expect(versionOf(finalRead.responseBody)).toBe(versionForReactivate + 1)
    await expect(page.locator(outcome.applied)).toBeVisible()
    await expect.poll(() => cardState(page)).toBe('active')
    await shot(page, GROUP, '03-reactivated')

    await net.write(GROUP, 'network.json')
  })

  test.afterEach(async ({}, testInfo) => {
    // The recorder that OBSERVED this case, not a new one.
    if (active !== null && testInfo.status !== testInfo.expectedStatus) {
      await attachEvidence(testInfo, active, GROUP)
    }
    active = null
  })
})
