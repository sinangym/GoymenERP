import { expect, test } from '../src/test'
import { B_ID, linkedView } from '../../cari-acceptance-009/src/fixtures'
import { Lane, json } from '../src/harness'
import { openLink, choose, submit, noSuccess } from '../src/ui'

for (const processed of [false, true]) {
  test('ME-004 Link unknown ' + (processed ? 'processed replay' : 'unprocessed retry') + ': same immutable key/path/roles, explicit user retry and one controlled result', async ({ page }, info) => {
    let calls = 0
    let logicalRecords = 0
    const lane = await Lane.install(page, {
      slug: 'link-unknown-' + (processed ? 'processed' : 'unprocessed'), company: B_ID,
      link: ({ roles }) => {
        calls += 1
        if (calls === 1) { logicalRecords = processed ? 1 : 0; return { abort: true } }
        if (!processed) logicalRecords += 1
        return json(linkedView(roles ?? 2), 201, processed ? {
          'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'c010-controlled-replay',
        } : {})
      },
    })
    await openLink(page)
    await choose(page)
    await submit(page).click()
    const unknown = page.locator('[data-attempt="unknown"]')
    await expect(unknown).toBeVisible()
    await expect(page.getByRole('checkbox', { name: 'Tedarikçi' })).toBeDisabled()
    await expect(page.getByRole('radio', { name: /CAR-000001/ })).toBeDisabled()
    await expect(page.getByRole('searchbox')).toBeDisabled()
    await noSuccess(page)
    await page.waitForTimeout(700)
    expect(lane.of('link')).toHaveLength(1)
    const beforeRetryRecords = logicalRecords
    // Exercise the abandoned-snapshot rule once; the other branch uses direct retry.
    if (processed) {
      await unknown.getByRole('button', { name: 'İşlemi bırak', exact: true }).click()
      await expect(page.getByRole('checkbox', { name: 'Tedarikçi' })).toBeEnabled()
      await submit(page).click()
      expect(lane.of('link')).toHaveLength(1)
      await page.getByRole('button', { name: 'Önceki işlemi yeniden dene', exact: true }).click()
    } else await unknown.getByRole('button', { name: 'Aynı işlemi yeniden dene' }).click()
    await expect(page.locator('[data-outcome="linked"]')).toHaveCount(1)
    await expect.poll(() => lane.of('link').length).toBe(2)
    const [first, second] = lane.of('link')
    expect(first.key).toMatch(/^[0-9a-f]{32}$/)
    expect(second.key).toBe(first.key)
    expect(second.path).toBe(first.path)
    expect(second.roles).toBe(first.roles)
    expect(first.payloadKeys).toEqual(['roles'])
    expect(second.payloadKeys).toEqual(['roles'])
    expect(second.payloadSha256).toBe(first.payloadSha256)
    expect(second.roles).toBe(2)
    expect(second.company).toBe(B_ID)
    await expect.poll(() => second.replayedHeader).toBe(processed)
    await expect.poll(() => second.originalCorrelationPresent).toBe(processed)
    expect(logicalRecords).toBe(1)
    await expect(page.getByRole('status').getByText('Cari CAR-000001 hedef şirkete bağlandı.', { exact: true })).toHaveCount(1)
    await page.waitForTimeout(500)
    expect(lane.of('link')).toHaveLength(2)
    await expect(page.locator('[data-outcome="linked"]')).toHaveCount(1)
    expect(lane.externalBlocked).toBe(0)
    await lane.finish(info, { me: ['ME-004'], processedFixture: processed, beforeRetryRecords,
      controlledLogicalRecords: logicalRecords, replayHeadersSent: processed, noAutomaticRetry: true,
      sameKeyPathRoles: true, exactPayloadBytesEqual: true, completions: 1, persistentServerReplayClaimed: false })
  })
}
