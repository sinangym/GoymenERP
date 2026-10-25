/**
 * Delegation No 30 / SAT-D12 on S-04: "Siparişi kapat" reads its preview only when asked, behind the
 * `sales.orders.force-close` control; each category says whether it blocks (an uninvoiced dispatch note is a warning only);
 * per line the server's closed remainder is shown; only a closable preview offers the command, which needs a reason and
 * carries the preview's token; a recorded closure re-reads the order, which then reads "Kapalı" with its recorded closure and
 * offers no other command. Component evidence, NOT_ENV: the API answers are the harness's.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import detailResponseRaw from './examples/detail-response.json?raw'
import { decodeDraftDetail, decodeForceCloseImpact, decodeForceClosure, type DraftDetail } from './contract'
import { SALES_CANCEL_CAPABILITY, SALES_FORCE_CLOSE_CAPABILITY } from './capabilities'
import { FORCE_CLOSE_TEXT as T, ORDER_DETAIL_TEXT, PROBLEM_CODE_TEXT } from './salesText'
import { ALL_SALES, COMPANY_A, installFetch, json, renderSalesApp } from './testHarness'

const DRAFT = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const LINE = '66666666-6666-6666-6666-666666666666'
const KG = '44444444-4444-4444-4444-444444444444'
const TOKEN = 'b'.repeat(64)
const WITH_FORCE_CLOSE = [...ALL_SALES, SALES_FORCE_CLOSE_CAPABILITY]

type Body = Record<string, any>

const APPROVED = {
  submittedBy: 'satis-1',
  submittedAtUtc: '2026-09-19T09:00:00Z',
  decidedBy: 'onay-1',
  decidedAtUtc: '2026-09-19T09:05:00Z',
  rejectionReason: null,
}

function detail(state = 'Draft', version = '4'): Body {
  const body = JSON.parse(detailResponseRaw) as Body
  body.draft.approvalState = 'Approved'
  body.draft.state = state
  body.draft.version = version
  body.approval = APPROVED
  return body
}

const SOURCES = [
  'inventory.active-allocations', 'purchasing.linked-orders', 'manufacturing.linked-orders',
  'logistics.loading-without-dispatch-note', 'invoicing.uninvoiced-dispatch-notes', 'finance.unmatched-advances',
]

/** The server's own blocking rule (the decoder refuses a body that contradicts it). */
const blocks = (source: string, status: string) =>
  status === 'Unreadable' || (status === 'Present' && (source === SOURCES[0] || source === SOURCES[3]))

function impact(options: { allocations?: string; loading?: string; invoicing?: string; closable?: boolean } = {}): Body {
  const allocations = options.allocations ?? 'None'
  const loading = options.loading ?? 'None'
  const invoicing = options.invoicing ?? 'None'
  const statusOf = (index: number) => (index === 0 ? allocations : index === 3 ? loading : index === 4 ? invoicing : 'NoSource')
  const sources = SOURCES.map((source, index) => ({ source, status: statusOf(index), blocks: blocks(source, statusOf(index)) }))
  const loadingNull = loading === 'Unreadable'
  const allocationNull = allocations === 'Unreadable'
  return {
    draftVersion: '4',
    revision: 'Rev.0',
    closable: options.closable ?? !sources.some((entry) => entry.blocks),
    impactToken: TOKEN,
    sources,
    lines: [{
      lineId: LINE, lineNo: 1, orderUnitId: KG, ordered: '1000',
      activeAllocations: allocationNull ? null : 0, allocatedKg: allocationNull ? null : '0',
      loadingInstructionLines: loadingNull ? null : 1, instructed: loadingNull ? null : '600', loaded: loadingNull ? null : '600',
      shipped: loadingNull ? null : '600', delivered: loadingNull ? null : '400',
      closedRemainder: loadingNull ? null : '400',
    }],
  }
}

const RECORD = {
  forceClosure: {
    revision: 'Rev.0', closedBy: 'satis-mudur', closedAtUtc: '2026-10-25T10:00:00Z', reason: 'Müşteri kalanı istemiyor',
    impactToken: TOKEN,
    sources: SOURCES.map((source, index) => {
      const status = index === 0 || index === 3 ? 'None' : index === 4 ? 'Present' : 'NoSource'
      return { source, status, blocks: false }
    }),
    lines: [{ lineId: LINE, lineNo: 1, ordered: '1000', shipped: '600', closedRemainder: '400' }],
  },
}

afterEach(() => {
  cleanup()
  try {
    window.localStorage.clear()
  } catch {
    // no storage here
  }
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const impactCalls = <C extends { readonly url: string }>(calls: readonly C[]) => calls.filter((call) => call.url.endsWith('/force-close-impact'))
const closeCalls = <C extends { readonly url: string; readonly method: string }>(calls: readonly C[]) =>
  calls.filter((call) => call.url.endsWith('/force-close') && call.method === 'POST')

describe('S-04 force-close of an approved order (No 30, SAT-D12)', () => {
  it('reads the preview only when asked, warns on an uninvoiced dispatch note without blocking, closes and reopens Kapalı', async () => {
    let closed = false
    const harness = installFetch({
      capabilities: WITH_FORCE_CLOSE,
      onDetail: () => json(closed ? detail('Closed', '5') : detail()),
      onForceCloseImpact: () => json(impact({ invoicing: 'Present' })),
      onForceClose: () => {
        closed = true
        return json({ detail: detail('Closed', '5'), isReplay: false, originalCorrelationId: 'c-1' })
      },
      onForceClosure: () => json(RECORD),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)

    const section = await screen.findByTestId('sales-force-close')
    expect(impactCalls(harness.calls)).toHaveLength(0)
    expect(within(section).queryByTestId('sales-force-close-submit')).toBeNull()

    fireEvent.click(within(section).getByTestId('sales-force-close-open'))
    await within(section).findByTestId('sales-force-close-impact')
    expect(impactCalls(harness.calls)[0]).toMatchObject({ method: 'GET', companyId: COMPANY_A, idempotencyKey: null, body: null })
    const invoicing = screen.getByTestId('sales-force-close-source-invoicing.uninvoiced-dispatch-notes')
    expect(invoicing.getAttribute('data-blocks')).toBe('false')
    expect(invoicing.textContent).toContain(T.warns)
    expect(screen.getByTestId('sales-force-close-warning').textContent).toBe(T.warning)
    // The remainder is the server's number (ordered − shipped), never computed on the screen.
    expect(screen.getByTestId('sales-force-close-line-1-remainder').textContent).toBe('400')

    fireEvent.click(screen.getByTestId('sales-force-close-submit'))
    expect(await screen.findByText(T.reasonRequired)).toBeTruthy()
    expect(closeCalls(harness.calls)).toHaveLength(0)

    fireEvent.change(screen.getByLabelText(T.reasonLabel), { target: { value: '  Müşteri kalanı istemiyor  ' } })
    fireEvent.click(screen.getByTestId('sales-force-close-submit'))

    await screen.findByTestId('sales-force-close-record')
    const [sent] = closeCalls(harness.calls)
    expect(sent.companyId).toBe(COMPANY_A)
    expect(sent.idempotencyKey).toMatch(/.+/)
    expect(JSON.parse(sent.body as string)).toEqual({ expectedVersion: '4', reason: 'Müşteri kalanı istemiyor', impactToken: TOKEN })
    await waitFor(() => expect(screen.getByTestId('sales-draft-lifecycle-state').textContent).toBe(ORDER_DETAIL_TEXT.stateClosed))
    expect(screen.getByTestId('sales-draft-approval-state').textContent).toBe('Onaylı')
    await waitFor(() => expect(screen.getByTestId('sales-force-close-reason-text').textContent).toBe('Müşteri kalanı istemiyor'))
    expect(screen.getByTestId('sales-force-close-record-line-1').textContent).toContain('400')
    expect(screen.queryByTestId('sales-force-close')).toBeNull()
  })

  it('shows what blocks the closure and offers no command while an allocation or open loading exists', async () => {
    const harness = installFetch({
      capabilities: WITH_FORCE_CLOSE,
      onDetail: () => json(detail()),
      onForceCloseImpact: () => json(impact({ allocations: 'Present', loading: 'Present' })),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)

    fireEvent.click(await screen.findByTestId('sales-force-close-open'))
    await screen.findByTestId('sales-force-close-blocked')
    expect(screen.getByTestId('sales-force-close-source-inventory.active-allocations').getAttribute('data-blocks')).toBe('true')
    expect(screen.getByTestId('sales-force-close-source-logistics.loading-without-dispatch-note').textContent).toContain(T.blocks)
    expect(screen.queryByTestId('sales-force-close-submit')).toBeNull()
    expect(closeCalls(harness.calls)).toHaveLength(0)
  })

  it('never shows an unreadable owner as 0 and offers no command', async () => {
    installFetch({
      capabilities: WITH_FORCE_CLOSE,
      onDetail: () => json(detail()),
      onForceCloseImpact: () => json(impact({ loading: 'Unreadable' })),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)

    fireEvent.click(await screen.findByTestId('sales-force-close-open'))
    await screen.findByTestId('sales-force-close-blocked')
    expect(screen.getByTestId('sales-force-close-line-1-remainder').textContent).toBe(ORDER_DETAIL_TEXT.cancellation.unreadable)
    expect(screen.queryByTestId('sales-force-close-submit')).toBeNull()
  })

  it('reads the preview again when the impact changed after the user saw it', async () => {
    let reads = 0
    const harness = installFetch({
      capabilities: WITH_FORCE_CLOSE,
      onDetail: () => json(detail()),
      onForceCloseImpact: () => {
        reads += 1
        return json(reads === 1 ? impact() : impact({ allocations: 'Present' }))
      },
      onForceClose: () => json({
        status: 409, code: 'sales_force_close_impact_changed', errors: [{ field: 'impactToken', code: 'sales_force_close_impact_changed' }],
      }, 409),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)

    fireEvent.click(await screen.findByTestId('sales-force-close-open'))
    fireEvent.change(await screen.findByLabelText(T.reasonLabel), { target: { value: 'neden' } })
    fireEvent.click(screen.getByTestId('sales-force-close-submit'))

    await waitFor(() => expect(screen.getByTestId('sales-force-close-notice').textContent)
      .toContain(PROBLEM_CODE_TEXT.sales_force_close_impact_changed))
    await screen.findByTestId('sales-force-close-blocked')
    expect(impactCalls(harness.calls).length).toBeGreaterThanOrEqual(2)
    expect(closeCalls(harness.calls)).toHaveLength(1)
  })

  it('is not offered without the force-close control, nor on a closed order, where the cancellation is not offered either', async () => {
    installFetch({ capabilities: [...ALL_SALES, SALES_CANCEL_CAPABILITY], onDetail: () => json(detail()) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByTestId('sales-cancellation')
    expect(screen.queryByTestId('sales-force-close')).toBeNull()
    cleanup()

    installFetch({
      capabilities: [...WITH_FORCE_CLOSE, SALES_CANCEL_CAPABILITY],
      onDetail: () => json(detail('Closed', '5')),
      onForceClosure: () => json(RECORD),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByTestId('sales-force-close-record')
    expect(screen.queryByTestId('sales-force-close')).toBeNull()
    expect(screen.queryByTestId('sales-cancellation')).toBeNull()
  })
})

describe('force-close contract decoders', () => {
  const decoded = (): DraftDetail => decodeDraftDetail(detail(), DRAFT)!

  it('reads a closed order as Closed beside Approved, and refuses Closed beside any other approval state', () => {
    expect(decodeDraftDetail(detail('Closed', '5'), DRAFT)?.draft.state).toBe('Closed')
    const unapproved = detail('Closed', '5')
    unapproved.draft.approvalState = 'Preparing'
    expect(decodeDraftDetail(unapproved, DRAFT)).toBeNull()
  })

  it('refuses a preview whose blocks flag contradicts the rule, or that is closable beside a blocking source', () => {
    expect(decodeForceCloseImpact(impact({ invoicing: 'Present' }), decoded())).not.toBeNull()
    const lying = impact({ invoicing: 'Present' })
    lying.sources[4].blocks = true
    expect(decodeForceCloseImpact(lying, decoded())).toBeNull()
    expect(decodeForceCloseImpact(impact({ allocations: 'Present', closable: true }), decoded())).toBeNull()
    const stale = impact()
    stale.draftVersion = '3'
    expect(decodeForceCloseImpact(stale, decoded())).toBeNull()
  })

  it('reads the recorded closure exactly, and null when there is none', () => {
    expect(decodeForceClosure({ forceClosure: null })).toEqual({ forceClosure: null })
    expect(decodeForceClosure(RECORD)?.forceClosure?.lines[0].closedRemainder).toBe('400')
    expect(decodeForceClosure({ forceClosure: { ...RECORD.forceClosure, extra: 1 } })).toBeNull()
  })
})
