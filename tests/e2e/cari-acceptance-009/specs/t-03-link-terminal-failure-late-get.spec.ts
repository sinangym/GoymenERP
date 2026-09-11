/**
 * T-03 — terminal 404 / 409 on the link command with a PRE-failure candidate GET
 * returning late, in a REAL browser (G-SUPPORT-004 row 3; C-DESKTOP-002-P2-002 and
 * the C-DESKTOP-003 evidence gap at browser level).
 *
 * The continuation issued before the failure is HELD at the response stage. What
 * happens to it after the failure is MEASURED (delivered vs. cancelled) and recorded;
 * a cancelled read is never counted as a delivered response. EVERY cursorless read
 * issued after the failure is held until the scenario releases it, so nothing read
 * after the failure can land early by accident. Responders key on the scenario
 * phase, never on request ordinals (dev-mode StrictMode may duplicate the very
 * first read), and counts start from a baseline taken once the page is settled.
 */
import { expect, test, type Page } from '@playwright/test'
import { B_ID, PARTY, PARTY_2, PROBLEM, ROLE, candidate, candidatePage } from '../src/fixtures.js'
import { TLane, held, json, problem, shot, writeJsonEvidence } from '../src/tHarness.js'

const ROUTE = '/cari/sirkete-bagla'
const P2 = candidate({ id: PARTY_2, code: 'CAR-000002' })
const P2_VIEW = {
  id: PARTY_2,
  code: 'CAR-000002',
  kind: 1,
  legalName: 'Örnek Metal Sanayi ve Ticaret A.Ş.',
  tradeName: 'Örnek Metal',
  isActive: true,
  version: '1',
  companyAccounts: [{ id: 'dddddddd-2222-2222-2222-222222222222', companyId: B_ID, roles: ROLE.Supplier, isActive: true, version: '1' }],
}

function selectionState(page: Page) {
  return page.locator('[data-selection]').getAttribute('data-selection')
}

/**
 * The refresh announcement, EXACTLY, inside the status live region. A substring
 * match would also hit the failure notice's own sentence ("… aday listesi
 * yenilendi."), which is not the announcement and is on screen from the failure on.
 */
function announced(page: Page) {
  return page.locator('[role="status"]').getByText('Aday listesi yenilendi.', { exact: true })
}

const CASES = [
  { label: 'not-found', status: 404, code: PROBLEM.notFound, notice: 'Cari bulunamadı' },
  { label: 'already-linked', status: 409, code: PROBLEM.alreadyLinked, notice: 'Zaten bağlı olabilir' },
] as const

for (const testCase of CASES) {
  test(`T-03 ${testCase.label}: rows on screen lose authority until the read ISSUED after the failure lands; a pre-failure GET returning late grants nothing`, async ({ page }, testInfo) => {
    const slug = `t03-terminal-${testCase.label}-late-get`
    const late = held()
    const fresh = held()
    let failureAnswered = false
    let postFailureReadsIssued = 0
    const lane = await TLane.install(page, {
      slug,
      preferCompany: B_ID,
      capabilities: () => ['cari.parties.list', 'cari.parties.create', 'cari.accounts.link'],
      candidates: async ({ url }) => {
        if (url.searchParams.get('cursor') === 'c1') {
          await late.promise
          return json(candidatePage([candidate()]))
        }
        if (!failureAnswered) {
          return json(candidatePage([candidate(), P2], { hasMore: true, nextCursor: 'c1' }))
        }
        // Every post-failure first-page read waits for the scenario to release it.
        postFailureReadsIssued += 1
        await fresh.promise
        return json(candidatePage([P2]))
      },
      link: ({ url }) => {
        if (url.pathname.includes(PARTY_2)) return json(P2_VIEW, 201)
        failureAnswered = true
        return problem(testCase.status, testCase.code)
      },
    })

    await page.goto(ROUTE)
    await expect(page.getByRole('heading', { level: 1, name: 'Cariyi Şirkete Bağla' })).toBeVisible({ timeout: 60_000 })
    await expect(page.getByRole('row', { name: /CAR-000001/ })).toBeVisible()
    const base = lane.candidateReads().length
    await page.getByRole('radio', { name: /CAR-000001/ }).check()
    await page.getByRole('checkbox', { name: 'Tedarikçi' }).check()
    await expect.poll(() => selectionState(page)).toBe('fresh')

    // A continuation of the current query is in flight when the link is sent.
    await page.getByRole('button', { name: 'Daha fazla aday yükle' }).click()
    await expect.poll(() => lane.candidateReads().slice(base).map((r) => r.cursor)).toEqual(['c1'])
    await expect.poll(() => selectionState(page)).toBe('fresh')
    await page.getByRole('button', { name: 'Şirkete bağla' }).click()
    const notice = page.locator(`[data-failure="${testCase.label}"]`)
    await expect(notice).toBeVisible()
    await expect(notice).toContainText(testCase.notice)
    expect(lane.links()).toHaveLength(1)
    expect(lane.links()[0].path).toBe(`/api/v1/counterparties/${PARTY}/company-account`)
    expect(lane.links()[0].status).toBe(testCase.status)

    // The failure issues a post-failure first-page read (held). Selection dropped,
    // nothing announced as refreshed.
    await expect.poll(() => postFailureReadsIssued).toBeGreaterThanOrEqual(1)
    await expect.poll(() => selectionState(page)).toBe('none')
    await expect(announced(page)).toHaveCount(0)
    const oldRowsVisibleDuringRefresh = (await page.getByRole('row', { name: /CAR-000001/ }).count()) > 0
    await shot(page, 'T', slug, 'after-failure-refresh-pending')

    // The PRE-failure continuation returns now — late, after the failure.
    late.release()
    await page.waitForTimeout(800)
    const lateRecord = lane.candidateReads().slice(base)[0]
    const lateOutcome = {
      delivered: lateRecord.delivered,
      status: lateRecord.status,
      pageObservedFailure: lane.failures.find((f) => f.url.includes('company-account-link-candidates'))?.errorText ?? null,
    }
    await expect(announced(page)).toHaveCount(0)
    expect(lane.links(), 'no POST after the late pre-failure read').toHaveLength(1)
    let reselectOutcome: string = 'old rows not on screen during the refresh; nothing to re-select'
    if (oldRowsVisibleDuringRefresh && (await page.getByRole('radio', { name: /CAR-000001/ }).count()) > 0) {
      // Choosing the old Party again (closes the failed attempt) stays SUSPENDED; no POST.
      await page.getByRole('radio', { name: /CAR-000001/ }).check()
      await expect(notice).toHaveCount(0)
      await expect.poll(() => selectionState(page)).toBe('suspended')
      await expect(page.getByText('Seçim güncel aday listesiyle henüz doğrulanmadı.')).toBeVisible()
      await page.getByRole('button', { name: 'Şirkete bağla' }).click()
      await expect(page.locator('#cari-link-summary')).toBeVisible()
      await expect(page.locator('#cari-link-selection-error')).toHaveText('Güncel aday listesinden bir cari seçin.')
      expect(lane.links(), 'no POST on the strength of rows read before the failure').toHaveLength(1)
      reselectOutcome = 'old Party re-selected: suspended, submit refused, no POST'
      await shot(page, 'T', slug, 'reselect-suspended')
    }
    await expect(announced(page)).toHaveCount(0)

    // The read issued AFTER the failure lands: announced now; the old Party is gone;
    // a Party present in THAT read links under a NEW key.
    fresh.release()
    await expect(announced(page)).toBeVisible()
    await expect(page.getByRole('row', { name: /CAR-000001/ })).toHaveCount(0)
    await expect(page.getByRole('row', { name: /CAR-000002/ })).toBeVisible()
    await expect.poll(() => selectionState(page)).toBe('none')
    await page.getByRole('radio', { name: /CAR-000002/ }).check()
    await expect.poll(() => selectionState(page)).toBe('fresh')
    await page.getByRole('button', { name: 'Şirkete bağla' }).click()
    await expect(page.locator('[data-outcome="linked"]')).toBeVisible()
    expect(lane.links()).toHaveLength(2)
    expect(lane.links()[1].path).toBe(`/api/v1/counterparties/${PARTY_2}/company-account`)
    expect(lane.links()[1].companyId).toBe(B_ID)
    expect(lane.links()[1].idempotencyKey).not.toBe(lane.links()[0].idempotencyKey)
    await shot(page, 'T', slug, 'linked-from-post-failure-read')

    writeJsonEvidence('T', 'measurements', `${slug}.json`, {
      terminal: testCase,
      baselineReadsBeforeScenario: base,
      scenarioReadCursors: lane.candidateReads().slice(base).map((r) => r.cursor),
      postFailureFirstPageReadsHeldUntilRelease: postFailureReadsIssued,
      preFailureContinuation: lateOutcome,
      oldRowsVisibleDuringRefresh,
      reselectOutcome,
      announcementBeforePostFailureRead: false,
      postFromPreFailureRows: false,
      linkKeys: lane.links().map((l) => l.idempotencyKey),
      note: 'A cancelled pre-failure read is recorded as cancelled, not as a delivered response.',
    })
    await lane.finish(testInfo, slug)
  })
}
