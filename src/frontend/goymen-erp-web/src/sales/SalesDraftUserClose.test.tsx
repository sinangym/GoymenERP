/**
 * SAT-D32 / SAT-D168 / SAT-D215 on S-04: "Siparişi kapat" is offered only to an actor holding the `sales.orders.close` control
 * AND only when the server's status read answers `readyToClose`; the command carries the expected version only (no reason);
 * a not-ready answer (409) is shown in Turkish and the order is read again; a closed order shows the user closure's record
 * ("kullanıcı kapattı"), and a force-closed one keeps its own record. Component evidence, NOT_ENV: the API answers are the
 * harness's. The ready status is a TEST SEAM: on main the owner-less axes are NoSource, so the server never answers ready
 * (and the strict status decoder refuses `readyToClose: true` beside them) — `fetchDraftStatus` is wrapped to flip the flag.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import detailResponseRaw from './examples/detail-response.json?raw'
import { decodeUserClosure } from './contract'
import { SALES_CLOSE_CAPABILITY, SALES_FORCE_CLOSE_CAPABILITY } from './capabilities'
import { FORCE_CLOSE_TEXT, ORDER_DETAIL_TEXT, PROBLEM_CODE_TEXT, USER_CLOSE_TEXT as T } from './salesText'
import { ALL_SALES, COMPANY_A, installFetch, json, renderSalesApp } from './testHarness'

const statusSeam = vi.hoisted(() => ({ ready: false }))

vi.mock('./api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./api')>()
  return {
    ...actual,
    fetchDraftStatus: async (...args: Parameters<typeof actual.fetchDraftStatus>) => {
      const outcome = await actual.fetchDraftStatus(...args)
      return statusSeam.ready && outcome.type === 'ok-single' ? { ...outcome, data: { ...outcome.data, readyToClose: true } } : outcome
    },
  }
})

const DRAFT = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const WITH_CLOSE = [...ALL_SALES, SALES_CLOSE_CAPABILITY]

type Body = Record<string, any>

function detail(state = 'Draft', version = '4'): Body {
  const body = JSON.parse(detailResponseRaw) as Body
  body.draft.approvalState = 'Approved'
  body.draft.state = state
  body.draft.version = version
  body.approval = {
    submittedBy: 'satis-1',
    submittedAtUtc: '2026-09-19T09:00:00Z',
    decidedBy: 'onay-1',
    decidedAtUtc: '2026-09-19T09:05:00Z',
    rejectionReason: null,
  }
  return body
}

const USER_RECORD = { userClosure: { revision: 'Rev.0', closedBy: 'satis-uzman', closedAtUtc: '2026-11-03T10:00:00Z' } }

afterEach(() => {
  cleanup()
  statusSeam.ready = false
  try {
    window.localStorage.clear()
  } catch {
    // no storage here
  }
  vi.unstubAllGlobals()
})

const closeCalls = <C extends { readonly url: string; readonly method: string }>(calls: readonly C[]) =>
  calls.filter((call) => call.url.endsWith('/close') && call.method === 'POST')
const statusCalls = <C extends { readonly url: string }>(calls: readonly C[]) => calls.filter((call) => call.url.endsWith('/status'))

describe('S-04 user closure of a "kapanmaya hazır" order (SAT-D32)', () => {
  it('offers "Siparişi kapat" on a ready order, sends the version only, and reads the order Kapalı with the user record', async () => {
    statusSeam.ready = true
    let closed = false
    const harness = installFetch({
      capabilities: WITH_CLOSE,
      onDetail: () => json(closed ? detail('Closed', '5') : detail()),
      onUserClose: () => {
        closed = true
        return json({ detail: detail('Closed', '5'), isReplay: false, originalCorrelationId: 'c-1' })
      },
      onUserClosure: () => json(USER_RECORD),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)

    const submit = await screen.findByTestId('sales-user-close-submit')
    expect(submit.textContent).toBe(T.submit)
    expect(screen.getByRole('heading', { name: T.heading })).toBeTruthy()
    // No reason field (SAT-D215).
    expect(screen.queryByRole('textbox', { name: /neden/i })).toBeNull()
    fireEvent.click(submit)

    await screen.findByTestId('sales-user-close-record')
    const [sent] = closeCalls(harness.calls)
    expect(sent.companyId).toBe(COMPANY_A)
    expect(sent.idempotencyKey).toMatch(/.+/)
    expect(JSON.parse(sent.body as string)).toEqual({ expectedVersion: '4' })
    await waitFor(() => expect(screen.getByTestId('sales-draft-lifecycle-state').textContent).toBe(ORDER_DETAIL_TEXT.stateClosed))
    await waitFor(() => expect(screen.getByTestId('sales-user-close-by').textContent).toBe('satis-uzman'))
    expect(screen.getByRole('heading', { name: T.recordHeading })).toBeTruthy()
    expect(screen.queryByTestId('sales-user-close')).toBeNull()
    expect(screen.queryByTestId('sales-force-close-record')).toBeNull()
  })

  it('offers nothing while the server says the order is not ready, even to a holder of the control', async () => {
    const harness = installFetch({ capabilities: WITH_CLOSE, onDetail: () => json(detail()) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByTestId('sales-draft-lifecycle-state')
    await waitFor(() => expect(statusCalls(harness.calls).length).toBeGreaterThan(0))
    expect(screen.queryByTestId('sales-user-close')).toBeNull()
    expect(screen.queryByTestId('sales-user-close-submit')).toBeNull()
  })

  it('is not offered without the close control, even on a ready order; force-close never confers it', async () => {
    statusSeam.ready = true
    installFetch({ capabilities: [...ALL_SALES, SALES_FORCE_CLOSE_CAPABILITY], onDetail: () => json(detail()) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByTestId('sales-force-close')
    expect(screen.queryByTestId('sales-user-close')).toBeNull()
  })

  it('shows the server\'s not-ready refusal in Turkish and reads the order again', async () => {
    statusSeam.ready = true
    let details = 0
    const harness = installFetch({
      capabilities: WITH_CLOSE,
      onDetail: () => {
        details += 1
        return json(detail())
      },
      onUserClose: () => json({
        status: 409, code: 'sales_order_not_ready_to_close',
        errors: [{ field: 'readyToClose', code: 'sales_order_not_ready_to_close' }],
      }, 409),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    fireEvent.click(await screen.findByTestId('sales-user-close-submit'))

    await waitFor(() => expect(screen.getByTestId('sales-user-close-notice').textContent)
      .toContain(PROBLEM_CODE_TEXT.sales_order_not_ready_to_close))
    expect(screen.getByTestId('sales-user-close-notice').getAttribute('role')).toBe('alert')
    expect(screen.getByTestId('sales-user-close-notice').textContent).toContain(T.conflict)
    await waitFor(() => expect(details).toBeGreaterThanOrEqual(2))
    expect(closeCalls(harness.calls)).toHaveLength(1)
  })

  it('shows a version conflict and a forbidden answer in its own words', async () => {
    statusSeam.ready = true
    installFetch({
      capabilities: WITH_CLOSE,
      onDetail: () => json(detail()),
      onUserClose: () => json({ status: 409, code: 'version_conflict', errors: [{ field: 'expectedVersion', code: 'version_conflict' }] }, 409),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    fireEvent.click(await screen.findByTestId('sales-user-close-submit'))
    await waitFor(() => expect(screen.getByTestId('sales-user-close-notice').textContent).toContain(T.versionConflict))
  })

  it('shows the status read failure instead of an action, with a retry', async () => {
    installFetch({
      capabilities: WITH_CLOSE,
      onDetail: () => json(detail()),
      onStatus: () => json({ unexpected: true }),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    const section = await screen.findByTestId('sales-user-close')
    expect(section.textContent).toContain(T.statusSubject)
    expect(within(section).getByRole('button', { name: 'Tekrar dene' })).toBeTruthy()
    expect(screen.queryByTestId('sales-user-close-submit')).toBeNull()
  })

  it('does not label the record "kullanıcı kapattı" while it cannot be read', async () => {
    installFetch({
      capabilities: WITH_CLOSE,
      onDetail: () => json(detail('Closed', '5')),
      onUserClosure: () => json({ status: 503, code: 'service_unavailable' }, 503),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    const section = await screen.findByTestId('sales-user-close-record')
    await waitFor(() => expect(within(section).getByRole('button', { name: 'Tekrar dene' })).toBeTruthy())
    expect(within(section).getByRole('heading', { level: 2 }).textContent).toBe(T.recordPendingHeading)
    expect(section.textContent).not.toContain(T.recordHeading)
    expect(screen.queryByTestId('sales-user-close-by')).toBeNull()
  })

  it('keeps a force-closed order\'s own record when the user closure is empty', async () => {
    installFetch({
      capabilities: WITH_CLOSE,
      onDetail: () => json(detail('Closed', '5')),
      onForceClosure: () => json({
        forceClosure: {
          revision: 'Rev.0', closedBy: 'satis-mudur', closedAtUtc: '2026-10-25T10:00:00Z', reason: 'Kalan istenmiyor',
          impactToken: 'b'.repeat(64),
          sources: ['inventory.active-allocations', 'purchasing.linked-orders', 'manufacturing.linked-orders',
            'logistics.loading-without-dispatch-note', 'invoicing.uninvoiced-dispatch-notes', 'finance.unmatched-advances']
            .map((source) => ({ source, status: 'NoSource', blocks: false })),
          lines: [{ lineId: '66666666-6666-6666-6666-666666666666', lineNo: 1, ordered: '1000', shipped: '600', closedRemainder: '400' }],
        },
      }),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByTestId('sales-force-close-record')
    expect(screen.getByRole('heading', { name: FORCE_CLOSE_TEXT.recordHeading })).toBeTruthy()
    expect(screen.queryByTestId('sales-user-close-record')).toBeNull()
  })
})

describe('user-closure contract decoder', () => {
  it('reads the recorded user closure exactly, and null when there is none', () => {
    expect(decodeUserClosure({ userClosure: null })).toEqual({ userClosure: null })
    expect(decodeUserClosure(USER_RECORD)).toEqual(USER_RECORD)
    expect(decodeUserClosure({ userClosure: { ...USER_RECORD.userClosure, reason: 'x' } })).toBeNull()
    expect(decodeUserClosure({ userClosure: { ...USER_RECORD.userClosure, revision: '0' } })).toBeNull()
    expect(decodeUserClosure({ userClosure: { ...USER_RECORD.userClosure, closedBy: ' ' } })).toBeNull()
    expect(decodeUserClosure({})).toBeNull()
  })
})
