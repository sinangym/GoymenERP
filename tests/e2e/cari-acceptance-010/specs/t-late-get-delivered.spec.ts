/**
 * ME-002 remaining sub-scope (open after C010): a candidate GET issued BEFORE a terminal
 * link failure (404 not_found / 409 already_linked) is DELIVERED to production after the
 * failure — the abort is ignored for that one request only — and must grant nothing:
 * no authority, no "yenilendi" announcement, no POST. Only the read issued after the
 * failure is authority (§8; C-DESKTOP-002-P2-002). Two pre-failure reads are covered:
 * an in-flight continuation (cursor=c1) and an in-flight cursorless first-page refetch.
 *
 * Measured: production `response.json` consumption of the late 200 (transport events),
 * execution of the production candidate queryFn continuation after delivery (V8 precise
 * coverage), the query-cache entry of the late read before/after delivery (fields only)
 * and the UI authority state. NOT distinguished: whether React Query discarded the
 * cancelled result or the seq barrier refused it — both are recorded, neither is assumed.
 */
import type { Page } from '@playwright/test'
import { expect, test } from '../src/test'
import { B_ID, PARTY_2, PROBLEM, candidate, candidatePage, linkedView } from '../../cari-acceptance-009/src/fixtures'
import { Lane, held, json, problem, insensitiveTransport, transportEvents, CoverageWitness, refetch } from '../src/harness'
import { openLink, choose, selection, submit, noSuccess } from '../src/ui'

const P2 = candidate({ id: PARTY_2, code: 'CAR-000002' })
const announced = (page: Page) => page.locator('[role="status"]').getByText('Aday listesi yenilendi.', { exact: true })
const cache = (page: Page) => page.evaluate(() => window.c010.candidateCache())
// The served source is transformed by Vite (object shorthand/whitespace may be rewritten): match structurally.
const queryFnPoint = { file: 'CariLinkWorkspacePage.tsx', needle: /return\s*\{\s*seq\s*(?:,|:\s*seq\s*,)\s*outcome(?:\s*:\s*outcome)?\s*\}/, label: 'production candidate queryFn continuation' }
const CASES = [
  { label: 'not-found', status: 404, code: PROBLEM.notFound, failure: 'not-found' },
  { label: 'already-linked', status: 409, code: PROBLEM.alreadyLinked, failure: 'already-linked' },
] as const
const VARIANTS = ['continuation', 'first-page-refetch'] as const

for (const variant of VARIANTS) {
  for (const terminal of CASES) {
    test(`ME-002 delivered pre-failure ${variant} GET after terminal ${terminal.label}: production consumes the late 200, it grants nothing; only the read issued after the failure is authority`, async ({ page }, info) => {
      const late = held()
      const fresh = held()
      let stage: 'initial' | 'pre-failure-held' | 'post-failure' = 'initial'
      let postFailureReads = 0
      if (variant === 'continuation') await insensitiveTransport(page, { method: 'GET', fragment: 'cursor=c1' })
      else await insensitiveTransport(page, { method: 'GET', fragment: 'company-account-link-candidates', notFragment: 'cursor=' })
      const lane = await Lane.install(page, {
        slug: `late-get-delivered-${variant}-${terminal.label}`, company: B_ID,
        candidates: async ({ url }) => {
          const cursor = url.searchParams.get('cursor')
          if (variant === 'continuation' && cursor === 'c1') { await late.promise; return json(candidatePage([candidate()]), 200, undefined, 'late-continuation') }
          if (stage === 'post-failure') { postFailureReads += 1; await fresh.promise; return json(candidatePage([P2]), 200, undefined, 'post-failure-first-page') }
          if (variant === 'first-page-refetch' && stage === 'pre-failure-held') { await late.promise; return json(candidatePage([candidate(), P2]), 200, undefined, 'late-first-page') }
          return json(candidatePage([candidate(), P2], variant === 'continuation' ? { hasMore: true, nextCursor: 'c1' } : {}), 200, undefined, 'initial-first-page')
        },
        link: ({ url }) => {
          if (url.pathname.includes(PARTY_2)) return json(linkedView(2, { id: PARTY_2, code: 'CAR-000002' }), 201)
          stage = 'post-failure'
          return problem(terminal.status, terminal.code)
        },
      })
      try {
        await openLink(page)
        await choose(page)
        await expect.poll(() => selection(page)).toBe('fresh')
        const base = lane.of('candidates').length
        if (variant === 'continuation') {
          await page.getByRole('button', { name: 'Daha fazla aday yükle' }).click()
          await expect.poll(() => lane.of('candidates').slice(base).map((r) => r.cursor)).toEqual(['c1'])
        } else {
          stage = 'pre-failure-held'
          await refetch(page, 'candidates')
          await expect.poll(() => lane.of('candidates').slice(base).length).toBe(1)
        }
        const lateRecord = lane.of('candidates').slice(base)[0]
        await expect.poll(() => selection(page)).toBe('fresh')
        const cacheBeforeFailure = await cache(page)

        await submit(page).click()
        await expect(page.locator(`[data-failure="${terminal.failure}"]`)).toBeVisible()
        expect(lane.of('link')).toHaveLength(1)
        expect(lane.of('link')[0].status).toBe(terminal.status)
        await expect.poll(() => postFailureReads).toBeGreaterThanOrEqual(1)
        await expect.poll(() => selection(page)).toBe('none')
        await expect(announced(page)).toHaveCount(0)
        const cacheAfterFailure = await cache(page)

        // The PRE-failure read is delivered now, after the failure, and production consumes it.
        const witness = await CoverageWitness.start(page)
        late.release()
        await expect.poll(async () => (await transportEvents(page)).filter((e) => e.event === 'production-json-resolved' && e.status === 200).length).toBeGreaterThanOrEqual(1)
        await expect.poll(() => lateRecord.browserFinished).toBe(true)
        await page.waitForTimeout(400)
        const coverage = await witness.finish([queryFnPoint])
        expect(coverage[0].count, 'the production queryFn continuation must run for the delivered late read').toBeGreaterThan(0)
        const cacheAfterDelivery = await cache(page)
        const events = await transportEvents(page)

        // Nothing changes on the strength of the late read.
        await expect(announced(page)).toHaveCount(0)
        await expect.poll(() => selection(page)).toBe('none')
        expect(lane.of('link')).toHaveLength(1)
        await noSuccess(page)
        const oldRowVisible = (await page.getByRole('radio', { name: /CAR-000001/ }).count()) > 0
        let reselect = 'old row not on screen while the post-failure read is pending'
        if (oldRowVisible) {
          await page.getByRole('radio', { name: /CAR-000001/ }).check()
          await expect.poll(() => selection(page)).toBe('suspended')
          await submit(page).click()
          await expect(page.locator('#cari-link-selection-error')).toHaveText('Güncel aday listesinden bir cari seçin.')
          expect(lane.of('link'), 'no POST on the strength of rows read before the failure').toHaveLength(1)
          reselect = 'old row re-selected after the delivered late read: suspended, submit refused, no POST'
        }

        // The read issued AFTER the failure lands: announced now; the failed Party is gone; CAR-000002 links under a NEW key.
        fresh.release()
        await expect(announced(page)).toBeVisible()
        await expect(page.getByRole('radio', { name: /CAR-000001/ })).toHaveCount(0)
        await expect(page.getByRole('radio', { name: /CAR-000002/ })).toBeVisible()
        await choose(page, 'CAR-000002')
        await expect.poll(() => selection(page)).toBe('fresh')
        await submit(page).click()
        await expect(page.locator('[data-outcome="linked"]')).toHaveCount(1)
        expect(lane.of('link')).toHaveLength(2)
        expect(lane.of('link')[1].key).not.toBe(lane.of('link')[0].key)
        expect(lane.of('link')[1].path).toContain(PARTY_2)
        expect(lane.externalBlocked).toBe(0)
        expect(lane.of('unexpected')).toHaveLength(0)
        const lateEntry = (entries: Awaited<ReturnType<typeof cache>>) =>
          entries.find((e) => (variant === 'continuation' ? e.cursor === 'c1' : e.cursor === null)) ?? null
        await lane.finish(info, {
          me: ['ME-002'], variant, failure: terminal.label,
          lateRead: { cursor: lateRecord.cursor, status: lateRecord.status, transport: lateRecord.transport, browserFinished: lateRecord.browserFinished, browserCancelled: lateRecord.browserCancelled, fixture: lateRecord.fixture },
          transportEvents: events, productionCoverage: coverage,
          cacheBeforeFailure, cacheAfterFailure, cacheAfterDelivery,
          lateEntryAfterFailure: lateEntry(cacheAfterFailure), lateEntryAfterDelivery: lateEntry(cacheAfterDelivery),
          oldRowVisibleDuringRefresh: oldRowVisible, reselect, postFailureReads,
          measured: 'late 200 consumed by production response.json; production queryFn continuation executed after delivery; authority, announcement and POST unaffected; only the post-failure read restores authority',
          notMeasured: 'whether React Query discarded the cancelled result or the seq barrier refused a committed page is recorded via the cache fields, not asserted; no live API',
        })
      } finally { late.release(); fresh.release() }
    })
  }
}
