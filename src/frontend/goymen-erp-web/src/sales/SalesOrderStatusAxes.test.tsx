/**
 * DEC-SAL-001 / API-08 / UI-15 on S-04: the "Sipariş durumu" card shows the seven axes exactly as the server answers them,
 * each as a named list item whose state is a word (never a colour alone); "kapanmaya hazır" with the reason it is false;
 * the realization's tolerance band and the SAT-D29…D33 marks; and per line the measured progress. Loading, 503 (with its
 * support code), 404, 401/403, malformed and foreign answers show no axis at all. Component evidence, NOT_ENV: the API
 * answers here are the harness's.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import detailResponseRaw from './examples/detail-response.json?raw'
import statusResponseRaw from './examples/status-response.json?raw'
import { SALES_CAPABILITY } from './capabilities'
import { decodeDraftDetail, type DraftDetail } from './contract'
import { decodeOrderStatus } from './orderStatus'
import { APPROVAL_STATE_TEXT, ORDER_STATUS_TEXT as T } from './salesText'
import { OrderStatusCard } from './SalesOrderStatusAxes'
import { ALL_SALES, COMPANY_A, installFetch, json, renderSalesApp } from './testHarness'

const DRAFT = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const LINE_2 = '66666666-6666-4666-8666-666666666667'
const TON = '77777777-7777-4777-8777-777777777777'
const PATH = `/satis/siparisler/${DRAFT}`

type Body = Record<string, any>

const APPROVED = {
  submittedBy: 'satis-1', submittedAtUtc: '2026-09-19T09:00:00Z', decidedBy: 'onay-1', decidedAtUtc: '2026-09-19T09:05:00Z',
  rejectionReason: null,
}
const NOT_YET = { submittedBy: null, submittedAtUtc: null, decidedBy: null, decidedAtUtc: null, rejectionReason: null }

/** Two lines: the first names its unit ("KG"); the second's unit is not named by the order (shown as its stored id). */
function detail(approvalState = 'Approved', revision = 'Rev.0'): Body {
  const body = JSON.parse(detailResponseRaw) as Body
  body.draft = { ...body.draft, companyId: COMPANY_A, approvalState, revision, version: '4', lineCount: 2 }
  body.approval = approvalState === 'Approved' ? APPROVED
    : approvalState === 'PendingApproval' ? { ...NOT_YET, submittedBy: 'satis-1', submittedAtUtc: '2026-09-20T09:00:00Z' } : NOT_YET
  const base = body.lines[0]
  body.lines = [
    { ...base, orderUnitCode: 'KG' },
    { ...base, lineId: LINE_2, lineNo: 2, productCode: 'URUN-2', productName: 'İkinci ürün', orderUnitId: TON, quantity: '25.5',
      requestedDeliveryDate: null, requestedDeliverySlices: [] },
  ]
  return body
}

/** The pinned example (line 1 completed in the warn band) plus line 2 not started. */
function status(): Body {
  const body = JSON.parse(statusResponseRaw) as Body
  body.realization.lines.push({
    lineId: LINE_2, lineNo: 2, orderUnitId: TON, ordered: '25.5', shipped: '0', delivered: '0', state: 'NotStarted',
    direction: 'Under', deviation: '25.5', band: null, warning: false, approvedOverDelivery: false, closedRemainder: null,
    approvedReturned: '0',
  })
  body.realization.state = 'Partial'
  return body
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

const statusCalls = <C extends { readonly url: string }>(calls: readonly C[]) => calls.filter((call) => call.url.endsWith('/status'))
const card = () => screen.findByRole('region', { name: T.heading })
/** The card once its axes are on screen (the read answered and decoded). */
async function loaded() {
  const section = await card()
  await within(section).findByRole('list', { name: T.axesLabel })
  return section
}

describe('S-04 order status axes (DEC-SAL-001 / API-08 / UI-15)', () => {
  it('shows the seven axes as named list items, through one company-bound read-only GET', async () => {
    const harness = installFetch({ onDetail: () => json(detail()), onStatus: () => json(status()) })
    renderSalesApp(PATH)

    const section = await card()
    const list = await within(section).findByRole('list', { name: T.axesLabel })
    const names = within(list).getAllByRole('listitem').map((item) => item.getAttribute('data-testid'))
    expect(names).toEqual([
      'sales-status-axis-lifecycle', 'sales-status-axis-internal-approval', 'sales-status-axis-customer-acceptance',
      'sales-status-axis-source', 'sales-status-axis-realization', 'sales-status-axis-invoicing', 'sales-status-axis-financial-closure',
    ])
    // Each axis is named "label state" — the state is a word, not a colour.
    expect(within(list).getByRole('listitem', { name: `${T.axis.lifecycle} ${T.lifecycle.Active}` })).toBeTruthy()
    expect(within(list).getByRole('listitem', { name: `${T.axis.internalApproval} ${APPROVAL_STATE_TEXT.Approved}` })).toBeTruthy()
    expect(within(list).getByRole('listitem', { name: `${T.axis.customerAcceptance} ${T.acceptance.NotRequired}` })).toBeTruthy()
    for (const axis of ['source', 'invoicing', 'financialClosure'] as const) {
      const item = within(list).getByRole('listitem', { name: `${T.axis[axis]} ${T.ownerless.NoSource}` })
      expect(item.textContent).toContain(T.ownerlessNote[axis])
    }
    // SAT-D29: the realization axis reads "Kısmi" here and carries the warning mark of its completed line.
    expect(within(list).getByRole('listitem', { name: `${T.axis.realization} ${T.realization.Partial} ${T.warningMark}` })).toBeTruthy()

    const reads = statusCalls(harness.calls)
    expect(reads).toHaveLength(1)
    expect(reads[0]).toMatchObject({ method: 'GET', companyId: COMPANY_A, idempotencyKey: null, body: null })
    expect(reads[0].url).toContain(`/sales/drafts/${DRAFT}/status`)
    expect(section.getAttribute('aria-busy')).toBe('false')
  })

  it('shows "kapanmaya hazır: Hayır" and says why: the owner-less axes have no source yet (SAT-D32)', async () => {
    installFetch({ onDetail: () => json(detail()), onStatus: () => json(status()) })
    renderSalesApp(PATH)

    await loaded()
    expect(screen.getByTestId('sales-status-ready-value').textContent).toBe(T.readyToClose.no)
    expect(screen.getByTestId('sales-status-ready-note').textContent)
      .toBe(T.readyToClose.noSourceNote(`${T.axis.source}, ${T.axis.invoicing}, ${T.axis.financialClosure}`))
  })

  it('shows the tolerance band it was judged with and "Tamamlandı + uyarı işareti" on the warn-band line (SAT-D29)', async () => {
    installFetch({ onDetail: () => json(detail()), onStatus: () => json(status()) })
    renderSalesApp(PATH)

    await loaded()
    expect(screen.getByTestId('sales-status-basis').textContent).toBe('Rev.0')
    expect(screen.getByTestId('sales-status-tolerance').textContent).toBe(T.toleranceBand('1', '2', '3'))
    expect(screen.getByTestId('sales-status-tolerance-version').textContent).toBe('3')
    expect(screen.getByTestId('sales-status-tolerance-window').textContent).toBe(`01.01.2026 – ${T.toleranceOpenEnded}`)
    expect(screen.getByTestId('sales-status-tolerance-evaluated-on').textContent).toBe('13.09.2026')
    expect(screen.getByTestId('sales-status-note-warning').textContent).toBe(T.warningNote)

    expect(screen.getByTestId('sales-status-line-1-state-text').textContent).toBe(T.realization.Completed)
    const mark = screen.getByTestId('sales-status-line-1-state-warning')
    expect(mark.textContent).toContain(T.warningMark)
    // The glyph is for the eye only; the word carries the meaning.
    expect(mark.querySelector('[aria-hidden="true"]')?.textContent).toBe('⚠')
    expect(screen.getByTestId('sales-status-line-1-deviation').textContent).toBe(T.deviation.Over('15'))
    expect(screen.getByTestId('sales-status-line-1-band').textContent).toBe(T.band.Warn)
  })

  it('shows per line the measured progress in the order unit, returns apart (SAT-D33), in a captioned table', async () => {
    installFetch({ onDetail: () => json(detail()), onStatus: () => json(status()) })
    renderSalesApp(PATH)

    const section = await loaded()
    const table = within(section).getByRole('table', { name: T.linesCaption })
    expect(within(table).getAllByRole('columnheader').map((cell) => cell.textContent)).toEqual([
      T.column.lineNo, T.column.product, T.column.ordered, T.column.shipped, T.column.delivered, T.column.deviation,
      T.column.band, T.column.state, T.column.closedRemainder, T.column.approvedReturned, T.column.unit,
    ])
    expect(within(table).getAllByRole('rowheader').map((cell) => cell.textContent)).toEqual(['1', '2'])
    expect(screen.getByTestId('sales-status-line-1-ordered').textContent).toBe('1000')
    expect(screen.getByTestId('sales-status-line-1-shipped').textContent).toBe('1015')
    expect(screen.getByTestId('sales-status-line-1-delivered').textContent).toBe('1010')
    expect(screen.getByTestId('sales-status-line-1-returned').textContent).toBe('2.5')
    expect(screen.getByTestId('sales-status-line-1-unit').textContent).toBe('KG')
    expect(screen.getByTestId('sales-status-line-2-unit').textContent).toBe(TON)
    expect(screen.getByTestId('sales-status-line-2-deviation').textContent).toBe(T.deviation.Under('25.5'))
    expect(screen.getByTestId('sales-status-line-2-band').textContent).toBe(T.notMeasured)
    expect(screen.getByTestId('sales-status-line-2-state-text').textContent).toBe(T.realization.NotStarted)
    expect(within(table).getByText('URUN-2 — İkinci ürün')).toBeTruthy()
    expect(within(section).getByText(T.returnsNote)).toBeTruthy()
  })

  it('shows an approval-band over-delivery as "Onay bekliyor", and an approved one as "Tamamlandı + onaylı fazla teslim" (SAT-D30)', async () => {
    const awaiting = status()
    Object.assign(awaiting.realization.lines[0], { state: 'AwaitingApproval', shipped: '1025', deviation: '25', band: 'Approval', warning: false })
    Object.assign(awaiting.realization, { state: 'AwaitingApproval', warning: false })
    installFetch({ onDetail: () => json(detail()), onStatus: () => json(awaiting) })
    renderSalesApp(PATH)

    await loaded()
    expect(screen.getByTestId('sales-status-realization-text').textContent).toBe(T.realization.AwaitingApproval)
    expect(screen.getByTestId('sales-status-note-awaiting-approval').textContent).toBe(T.awaitingApprovalNote)
    expect(screen.queryByTestId('sales-status-realization-warning')).toBeNull()

    cleanup()
    vi.unstubAllGlobals()
    const approved = status()
    Object.assign(approved.realization.lines[0], { shipped: '1025', deviation: '25', band: 'Approval', warning: false, approvedOverDelivery: true })
    approved.realization.warning = false
    installFetch({ onDetail: () => json(detail()), onStatus: () => json(approved) })
    renderSalesApp(PATH)

    await loaded()
    expect(screen.getByTestId('sales-status-line-1-state-text').textContent).toBe(T.realization.Completed)
    expect(screen.getByTestId('sales-status-line-1-state-approved-over').textContent).toContain(T.approvedOverDeliveryMark)
  })

  it('says no approved tolerance exists on the order date and judges nothing (PolicyUnavailable, SAT-D5)', async () => {
    const policy = status()
    Object.assign(policy.realization, { state: 'PolicyUnavailable', warning: false, tolerance: null })
    Object.assign(policy.realization.lines[0], { state: 'PolicyUnavailable', direction: null, deviation: null, band: null, warning: false })
    installFetch({ onDetail: () => json(detail()), onStatus: () => json(policy) })
    renderSalesApp(PATH)

    await loaded()
    expect(screen.getByRole('listitem', { name: `${T.axis.realization} ${T.realization.PolicyUnavailable}` })).toBeTruthy()
    expect(screen.getByTestId('sales-status-note-policy-unavailable').textContent).toBe(T.policyUnavailable('13.09.2026'))
    expect(screen.getByTestId('sales-status-tolerance').textContent).toBe(T.realization.PolicyUnavailable)
    // The facts stay visible; nothing is judged.
    expect(screen.getByTestId('sales-status-line-1-shipped').textContent).toBe('1015')
    expect(screen.getByTestId('sales-status-line-1-deviation').textContent).toBe(T.notMeasured)
    expect(screen.getByTestId('sales-status-line-1-band').textContent).toBe(T.notMeasured)
  })

  it('explains PendingReapproval: the ordered quantity is shown but there is no approved basis, so nothing is measured', async () => {
    const pending = status()
    pending.revision = 'Rev.1'
    pending.internalApproval.state = 'PendingApproval'
    Object.assign(pending.realization, { state: 'PendingReapproval', warning: false, basisRevision: null, tolerance: null })
    for (const line of pending.realization.lines) {
      Object.assign(line, {
        state: 'PendingReapproval', shipped: null, delivered: null, direction: null, deviation: null, band: null,
        warning: false, approvedOverDelivery: false, closedRemainder: null, approvedReturned: null,
      })
    }
    installFetch({ onDetail: () => json(detail('PendingApproval', 'Rev.1')), onStatus: () => json(pending) })
    renderSalesApp(PATH)

    await loaded()
    expect(screen.getByRole('listitem', { name: `${T.axis.realization} ${T.realization.PendingReapproval}` })).toBeTruthy()
    expect(screen.getByTestId('sales-status-note-pending-reapproval').textContent).toBe(T.pendingReapproval)
    expect(screen.getByTestId('sales-status-basis').textContent).toBe(T.notApplicable)
    expect(screen.getByTestId('sales-status-tolerance').textContent).toBe(T.notMeasured)
    expect(screen.getByTestId('sales-status-line-1-ordered').textContent).toBe('1000')
    expect(screen.getByTestId('sales-status-line-1-shipped').textContent).toBe(T.notMeasured)
    expect(screen.getByTestId('sales-status-line-1-returned').textContent).toBe(T.notMeasured)
  })

  it('says the realization is measured against the last approved revision while a new one awaits re-approval', async () => {
    const earlier = status()
    earlier.revision = 'Rev.1'
    earlier.internalApproval.state = 'PendingApproval'
    earlier.realization.lines[1].ordered = '20'
    earlier.realization.lines[1].deviation = '20'
    installFetch({ onDetail: () => json(detail('PendingApproval', 'Rev.1')), onStatus: () => json(earlier) })
    renderSalesApp(PATH)

    await loaded()
    expect(screen.getByTestId('sales-status-note-basis-earlier').textContent).toBe(T.basisEarlier('Rev.0', 'Rev.1'))
    expect(screen.getByTestId('sales-status-line-2-ordered').textContent).toBe('20')
  })

  it('shows no quantity for an unreadable delivery owner, never zeros', async () => {
    const unreadable = status()
    Object.assign(unreadable.realization, { state: 'Unreadable', warning: false, tolerance: null })
    for (const line of unreadable.realization.lines) {
      Object.assign(line, {
        state: 'Unreadable', shipped: null, delivered: null, direction: null, deviation: null, band: null,
        warning: false, approvedOverDelivery: false, closedRemainder: null, approvedReturned: null,
      })
    }
    installFetch({ onDetail: () => json(detail()), onStatus: () => json(unreadable) })
    renderSalesApp(PATH)

    await loaded()
    expect(screen.getByTestId('sales-status-note-unreadable').textContent).toBe(T.realizationUnreadable)
    expect(screen.getByTestId('sales-status-line-1-shipped').textContent).toBe(T.notRead)
    expect(screen.getByTestId('sales-status-line-1-returned').textContent).toBe(T.notRead)
  })

  it('is read for a never-approved draft too: Taslak, Hazırlanıyor, nothing started (harness default)', async () => {
    installFetch({ onDetail: () => json(detail('Preparing')) })
    renderSalesApp(PATH)

    const section = await card()
    await within(section).findByRole('list', { name: T.axesLabel })
    expect(within(section).getByRole('listitem', { name: `${T.axis.lifecycle} ${T.lifecycle.Draft}` })).toBeTruthy()
    expect(within(section).getByRole('listitem', { name: `${T.axis.internalApproval} ${APPROVAL_STATE_TEXT.Preparing}` })).toBeTruthy()
    expect(within(section).getByRole('listitem', { name: `${T.axis.realization} ${T.realization.NotStarted}` })).toBeTruthy()
    expect(screen.getByTestId('sales-status-tolerance').textContent).toBe(T.toleranceNotNeeded)
  })

  it('shows a polite loading status while the axes are read, then the answer', async () => {
    let release: (response: Response) => void = () => undefined
    installFetch({ onDetail: () => json(detail()), onStatus: () => new Promise<Response>((resolve) => { release = resolve }) })
    renderSalesApp(PATH)

    const section = await card()
    const loading = await within(section).findByRole('status')
    expect(loading.getAttribute('data-state')).toBe('initial-loading')
    expect(loading.getAttribute('aria-live')).toBe('polite')
    expect(section.getAttribute('aria-busy')).toBe('true')
    expect(within(section).queryByRole('list', { name: T.axesLabel })).toBeNull()

    release(json(status()))
    await within(section).findByRole('list', { name: T.axesLabel })
    expect(within(section).queryByRole('status')).toBeNull()
  })

  it('on a 503 shows no axis, says the status service cannot answer with its support code, and retries the same read-only URL', async () => {
    let failing = true
    const harness = installFetch({
      onDetail: () => json(detail()),
      onStatus: () => (failing
        ? json({ status: 503, title: 'Service Unavailable', code: 'service_unavailable', correlationId: 'corr-503' }, 503)
        : json(status())),
    })
    renderSalesApp(PATH)

    const section = await card()
    const alert = await within(section).findByRole('alert')
    expect(alert.getAttribute('data-state')).toBe('service-unavailable')
    expect(within(section).getByTestId('sales-status-unreadable').textContent).toBe(T.serviceUnavailable)
    expect(within(section).getByTestId('sales-status-support-code').textContent).toBe(T.supportCode('corr-503'))
    expect(within(section).queryByRole('list', { name: T.axesLabel })).toBeNull()
    expect(within(section).queryByTestId('sales-status-ready')).toBeNull()

    failing = false
    within(alert).getByRole('button').click()
    await within(section).findByRole('list', { name: T.axesLabel })
    const reads = statusCalls(harness.calls)
    expect(reads).toHaveLength(2)
    expect(new Set(reads.map((call) => `${call.method} ${call.url}`)).size).toBe(1)
  })

  it('on a code-less 503 still names the outage, without a support code', async () => {
    installFetch({ onDetail: () => json(detail()), onStatus: () => json({ status: 503 }, 503) })
    renderSalesApp(PATH)

    const section = await card()
    expect((await within(section).findByRole('alert')).getAttribute('data-state')).toBe('service-unavailable')
    expect(within(section).getByTestId('sales-status-unreadable').textContent).toBe(T.serviceUnavailable)
    expect(within(section).queryByTestId('sales-status-support-code')).toBeNull()
  })

  it('fails closed on 404: no axis, no retry', async () => {
    installFetch({ onDetail: () => json(detail()), onStatus: () => json({ status: 404 }, 404) })
    renderSalesApp(PATH)

    const section = await card()
    const alert = await within(section).findByRole('alert')
    expect(alert.getAttribute('data-state')).toBe('not-found')
    expect(alert.textContent).toContain(`Bulunamadı — ${T.subject}`)
    expect(within(alert).queryByRole('button')).toBeNull()
    expect(within(section).getByTestId('sales-status-unreadable').textContent).toBe(T.unreadable)
    expect(within(section).queryByRole('list', { name: T.axesLabel })).toBeNull()
  })

  it.each([401, 403])('routes a %i through the real auth boundary: the protected detail closes and no axis is left', async (code) => {
    installFetch({ onDetail: () => json(detail()), onStatus: () => json({ status: code }, code) })
    renderSalesApp(PATH)

    expect(await screen.findByText(code === 401 ? 'Oturum sonlandı — oturum' : 'Bulunamadı — sayfa')).toBeTruthy()
    expect(screen.queryByTestId('sales-status-axes')).toBeNull()
    expect(screen.queryByTestId('sales-draft-version')).toBeNull()
  })

  it('refuses an answer that does not describe exactly this detail and shows it as unexpected, never as axes', async () => {
    const foreign = status()
    foreign.draftVersion = '3'
    installFetch({ onDetail: () => json(detail()), onStatus: () => json(foreign) })
    renderSalesApp(PATH)

    const section = await card()
    const alert = await within(section).findByRole('alert')
    expect(alert.getAttribute('data-state')).toBe('malformed-response')
    expect(within(section).queryByRole('list', { name: T.axesLabel })).toBeNull()
    expect(within(section).queryByTestId('sales-status-lines')).toBeNull()
  })

  it('refuses "ready to close" beside axes that have no source: a contradiction is malformed, not a green flag', async () => {
    const contradictory = status()
    contradictory.readyToClose = true
    installFetch({ onDetail: () => json(detail()), onStatus: () => json(contradictory) })
    renderSalesApp(PATH)

    const section = await card()
    expect((await within(section).findByRole('alert')).getAttribute('data-state')).toBe('malformed-response')
    expect(within(section).queryByTestId('sales-status-ready')).toBeNull()
  })

  it('is not read without the S-04 detail capability (API-08 publishes none of its own)', async () => {
    const harness = installFetch({
      capabilities: ALL_SALES.filter((id) => id !== SALES_CAPABILITY.draftsDetail),
      onDetail: () => json(detail()),
    })
    renderSalesApp(PATH)
    await screen.findByRole('alert')
    expect(screen.queryByTestId('sales-status-axes')).toBeNull()
    expect(statusCalls(harness.calls)).toHaveLength(0)
  })

  it('reads afresh when the order is opened again', async () => {
    let realization = 'Partial'
    const harness = installFetch({
      onDetail: () => json(detail()),
      onStatus: () => {
        const body = status()
        if (realization === 'Completed') {
          Object.assign(body.realization.lines[1], { shipped: '25.5', delivered: '25.5', state: 'Completed', direction: 'None', deviation: '0', band: 'Within' })
          body.realization.state = 'Completed'
        }
        return json(body)
      },
    })
    renderSalesApp(PATH)
    await screen.findByRole('listitem', { name: `${T.axis.realization} ${T.realization.Partial} ${T.warningMark}` })

    cleanup()
    realization = 'Completed'
    renderSalesApp(PATH)
    await screen.findByRole('listitem', { name: `${T.axis.realization} ${T.realization.Completed} ${T.warningMark}` })
    expect(screen.getByTestId('sales-status-line-2-deviation').textContent).toBe(T.deviation.None)
    expect(statusCalls(harness.calls)).toHaveLength(2)
  })
})

describe('OrderStatusCard outcomes', () => {
  const draftDetail = decodeDraftDetail(detail(), DRAFT) as DraftDetail

  it('shows the forbidden state on its own (a 403 the auth boundary would otherwise close the route on)', () => {
    render(<OrderStatusCard detail={draftDetail} outcome={{ type: 'http', status: 403, body: { status: 403 } }} onRetry={() => undefined} />)

    const section = screen.getByRole('region', { name: T.heading })
    const alert = within(section).getByRole('alert')
    expect(alert.getAttribute('data-state')).toBe('forbidden')
    expect(alert.textContent).toContain(`Yetkiniz yok — ${T.subject}`)
    expect(within(alert).queryByRole('button')).toBeNull()
    expect(within(section).queryByRole('list', { name: T.axesLabel })).toBeNull()
  })

  it('says there is no line to measure when the order has none (empty)', () => {
    const decoded = decodeOrderStatus(status(), draftDetail)
    expect(decoded).not.toBeNull()
    if (decoded === null) return
    const empty = { ...decoded, realization: { ...decoded.realization, state: 'NotStarted' as const, warning: false, tolerance: null, lines: [] } }
    render(<OrderStatusCard detail={{ ...draftDetail, lines: [] }} outcome={{ type: 'ok-single', data: empty }} onRetry={() => undefined} />)

    expect(screen.getByTestId('sales-status-empty').textContent).toBe(T.empty)
    expect(screen.queryByTestId('sales-status-lines')).toBeNull()
    expect(screen.getByRole('list', { name: T.axesLabel })).toBeTruthy()
  })

  it('shows "ready to close: Evet" with the user-action note when the server says every axis is complete', () => {
    const decoded = decodeOrderStatus(status(), draftDetail)
    if (decoded === null) throw new Error('example does not decode')
    render(<OrderStatusCard detail={draftDetail} outcome={{ type: 'ok-single', data: { ...decoded, readyToClose: true } }} onRetry={() => undefined} />)

    expect(screen.getByTestId('sales-status-ready-value').textContent).toBe(T.readyToClose.yes)
    expect(screen.getByTestId('sales-status-ready-note').textContent).toBe(T.readyToClose.yesNote)
  })

  it.each([
    [{ type: 'offline' as const }, 'offline'],
    [{ type: 'network-error' as const }, 'network-error'],
    [{ type: 'malformed' as const }, 'malformed-response'],
    [{ type: 'http' as const, status: 500, body: null }, 'service-unavailable'],
  ])('fails closed on %o without any axis', (outcome, kind) => {
    render(<OrderStatusCard detail={draftDetail} outcome={outcome} onRetry={() => undefined} />)
    expect(screen.getByRole('alert').getAttribute('data-state')).toBe(kind)
    expect(screen.getByTestId('sales-status-unreadable').textContent).toBe(T.unreadable)
    expect(screen.queryByRole('list', { name: T.axesLabel })).toBeNull()
  })
})

describe('the detail page', () => {
  it('waits for the detail before the status read: no status call while the detail is unread', async () => {
    const harness = installFetch({ onDetail: () => json({ status: 404 }, 404) })
    renderSalesApp(PATH)
    await screen.findByRole('alert')
    await waitFor(() => expect(statusCalls(harness.calls)).toHaveLength(0))
  })
})
