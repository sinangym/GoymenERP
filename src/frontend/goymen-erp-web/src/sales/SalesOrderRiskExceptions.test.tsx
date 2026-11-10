/**
 * API-07 (EK21 SAT-D153…D162) on S-04: the single-shipment risk exceptions panel lists the order's exceptions (company-bound
 * GET under the draft read), offers the request only with `sales.orders.risk-exception.request` and the decision only on a
 * pending exception with `sales.orders.risk-exception.approve`, sends exactly one key per attempt and the same key again on an
 * unknown outcome, names the server's refusal (a self-decision included) and refreshes on success or a conflict. Loading,
 * empty, 403, 503 (with its support code), offline and malformed answers show no exception. Component evidence, NOT_ENV: the
 * API answers here are the harness's. Every identity is synthetic.
 */
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import detailResponseRaw from './examples/detail-response.json?raw'
import {
  SALES_RISK_EXCEPTION_APPROVE_CAPABILITY,
  SALES_RISK_EXCEPTION_REQUEST_CAPABILITY,
} from './capabilities'
import { REPLAY_HEADER } from './contract'
import { RISK_EXCEPTION_TEXT as T } from './riskText'
import { OrderRiskExceptions } from './SalesOrderRiskExceptions'
import { ALL_SALES, COMPANY_A, installFetch, json, renderSalesApp } from './testHarness'

const DRAFT = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const SHIPMENT = 'e1e1e1e1-e1e1-4e1e-8e1e-e1e1e1e1e1e1'
const PATH = `/satis/siparisler/${DRAFT}`
const REQUESTER = [...ALL_SALES, SALES_RISK_EXCEPTION_REQUEST_CAPABILITY]
const APPROVER = [...ALL_SALES, SALES_RISK_EXCEPTION_APPROVE_CAPABILITY]

type Body = Record<string, any>

function detail(): Body {
  const body = JSON.parse(detailResponseRaw) as Body
  body.draft = { ...body.draft, companyId: COMPANY_A, version: '4' }
  return body
}

const requested = (overrides: Body = {}): Body => ({
  orderId: DRAFT, exceptionNo: 1, loadingInstructionId: SHIPMENT, state: 'Requested', version: '1',
  reason: 'SENTETIK müşteri ödeme sözü verdi', requestedBy: 'synthetic-shipper', requestedAtUtc: '2026-11-10T07:00:00Z',
  decidedBy: null, decidedAtUtc: null, rejectionReason: null, validOn: null, isValidToday: false, ...overrides,
})
const approved = (overrides: Body = {}): Body => requested({
  state: 'Approved', version: '2', decidedBy: 'synthetic-manager', decidedAtUtc: '2026-11-10T08:00:00Z', validOn: '2026-11-10',
  isValidToday: true, ...overrides,
})
const rejected = (overrides: Body = {}): Body => requested({
  state: 'Rejected', version: '2', decidedBy: 'synthetic-manager', decidedAtUtc: '2026-11-10T08:00:00Z',
  rejectionReason: 'SENTETIK risk çok yüksek', ...overrides,
})
const problem = (status: number, code: string | null, correlationId = 'corr-risk-exception-1') =>
  json({ status, title: 'refused', ...(code === null ? {} : { code }), correlationId }, status)

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const panel = () => screen.findByRole('region', { name: T.heading })
const exceptionCalls = <C extends { readonly url: string }>(calls: readonly C[]) => calls.filter((call) => call.url.includes('/risk-exceptions'))

describe('S-04 single-shipment risk exceptions (API-07)', () => {
  it('lists the order’s exceptions newest first through one company-bound GET, with validity as the server states it', async () => {
    let served = 0
    const harness = installFetch({
      onDetail: () => json(detail()),
      onRiskExceptions: () => { served += 1; return json({ items: [rejected({ exceptionNo: 3 }), approved({ exceptionNo: 2, isValidToday: false }), approved()] }) },
    })
    renderSalesApp(PATH)
    const section = await panel()
    await within(section).findByTestId('sales-risk-exception-3')

    expect(within(section).getByTestId('sales-risk-exception-3-state').textContent).toBe(T.state.Rejected)
    expect(within(section).getByTestId('sales-risk-exception-3-decided').textContent).toContain('SENTETIK risk çok yüksek')
    expect(within(section).getByTestId('sales-risk-exception-2-validity').textContent).toBe(T.lapsed('2026-11-10'))
    expect(within(section).getByTestId('sales-risk-exception-1-validity').textContent).toBe(T.validToday('2026-11-10'))
    // Without either action capability neither the form nor a decision is offered.
    expect(within(section).queryByTestId('sales-risk-exception-request')).toBeNull()
    expect(within(section).queryByRole('button', { name: T.approve })).toBeNull()
    const reads = exceptionCalls(harness.calls)
    expect(reads).toEqual([expect.objectContaining({ method: 'GET', companyId: COMPANY_A, idempotencyKey: null })])
    expect(reads[0].url).toMatch(new RegExp(`/sales/drafts/${DRAFT}/risk-exceptions$`))
    expect(served).toBe(1)
  })

  it('says so when the order has no exception', async () => {
    installFetch({ onDetail: () => json(detail()) })
    renderSalesApp(PATH)
    expect((await within(await panel()).findByTestId('sales-risk-exception-empty')).textContent).toBe(T.empty)
  })

  it('is not mounted, and never read, without the S-04 detail capability', async () => {
    const harness = installFetch({ onDetail: () => json(detail()), capabilities: [] })
    renderSalesApp(PATH)
    await waitFor(() => expect(harness.calls.some((call) => call.url.includes('/shell/capabilities'))).toBe(true))
    expect(screen.queryByRole('region', { name: T.heading })).toBeNull()
    expect(exceptionCalls(harness.calls)).toEqual([])
  })

  it('requests one exception with one key, the trimmed body, and refreshes the list', async () => {
    let lists = 0
    const harness = installFetch({
      capabilities: REQUESTER,
      onDetail: () => json(detail()),
      onRiskExceptions: () => { lists += 1; return json({ items: lists === 1 ? [] : [requested()] }) },
      onRiskExceptionCommand: () => json(requested(), 201),
    })
    renderSalesApp(PATH)
    const section = await panel()
    const form = await within(section).findByTestId('sales-risk-exception-request')
    fireEvent.change(within(form).getByLabelText(T.shipment), { target: { value: ` ${SHIPMENT} ` } })
    fireEvent.change(within(form).getByLabelText(T.reason), { target: { value: '  SENTETIK müşteri ödeme sözü verdi ' } })
    fireEvent.click(within(form).getByRole('button', { name: T.request }))

    expect((await within(section).findByText(T.requested)).getAttribute('role')).toBe('status')
    await within(section).findByTestId('sales-risk-exception-1')
    const posts = exceptionCalls(harness.calls).filter((call) => call.method === 'POST')
    expect(posts).toHaveLength(1)
    expect(posts[0].url).toMatch(new RegExp(`/sales/drafts/${DRAFT}/risk-exceptions$`))
    expect(posts[0].companyId).toBe(COMPANY_A)
    expect(posts[0].idempotencyKey).toMatch(/^[0-9a-f]{32}$/)
    expect(JSON.parse(posts[0].body as string)).toEqual({ loadingInstructionId: SHIPMENT, reason: 'SENTETIK müşteri ödeme sözü verdi' })
  })

  it('refuses a request without a valid shipment id or a reason before sending anything', async () => {
    const harness = installFetch({ capabilities: REQUESTER, onDetail: () => json(detail()) })
    renderSalesApp(PATH)
    const form = await within(await panel()).findByTestId('sales-risk-exception-request')
    fireEvent.change(within(form).getByLabelText(T.shipment), { target: { value: 'not-a-shipment' } })
    fireEvent.click(within(form).getByRole('button', { name: T.request }))
    expect(await screen.findByText(T.invalidShipment)).toBeTruthy()
    fireEvent.change(within(form).getByLabelText(T.shipment), { target: { value: SHIPMENT } })
    fireEvent.click(within(form).getByRole('button', { name: T.request }))
    expect(await screen.findByText(T.invalidReason)).toBeTruthy()
    expect(exceptionCalls(harness.calls).filter((call) => call.method === 'POST')).toEqual([])
  })

  it('retries an unknown outcome with the same key and body, then shows the replayed result', async () => {
    let attempts = 0
    const harness = installFetch({
      capabilities: REQUESTER,
      onDetail: () => json(detail()),
      onRiskExceptionCommand: () => {
        attempts += 1
        if (attempts === 1) throw new TypeError('network down')
        return json(requested(), 201, { [REPLAY_HEADER]: 'true' })
      },
    })
    renderSalesApp(PATH)
    const section = await panel()
    const form = await within(section).findByTestId('sales-risk-exception-request')
    fireEvent.change(within(form).getByLabelText(T.shipment), { target: { value: SHIPMENT } })
    fireEvent.change(within(form).getByLabelText(T.reason), { target: { value: 'SENTETIK müşteri ödeme sözü verdi' } })
    fireEvent.click(within(form).getByRole('button', { name: T.request }))

    expect(await within(section).findByText(T.unknown)).toBeTruthy()
    expect((within(form).getByRole('button', { name: T.request }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(within(section).getByTestId('sales-risk-exception-retry'))
    expect(await within(section).findByText(T.requested + T.replayed)).toBeTruthy()

    const posts = exceptionCalls(harness.calls).filter((call) => call.method === 'POST')
    expect(posts).toHaveLength(2)
    expect(posts[1].idempotencyKey).toBe(posts[0].idempotencyKey)
    expect(posts[1].body).toBe(posts[0].body)
  })

  it('names a duplicate request and refreshes the list after the 409', async () => {
    let lists = 0
    installFetch({
      capabilities: REQUESTER,
      onDetail: () => json(detail()),
      onRiskExceptions: () => { lists += 1; return json({ items: [] }) },
      onRiskExceptionCommand: () => problem(409, 'sales_risk_exception_duplicate'),
    })
    renderSalesApp(PATH)
    const section = await panel()
    const form = await within(section).findByTestId('sales-risk-exception-request')
    fireEvent.change(within(form).getByLabelText(T.shipment), { target: { value: SHIPMENT } })
    fireEvent.change(within(form).getByLabelText(T.reason), { target: { value: 'r' } })
    fireEvent.click(within(form).getByRole('button', { name: T.request }))

    const notice = await within(section).findByTestId('sales-risk-exception-notice')
    expect(notice.textContent).toBe(`${T.problem.sales_risk_exception_duplicate} · ${T.supportCode('corr-risk-exception-1')}`)
    await waitFor(() => expect(lists).toBe(2))
    expect(within(section).queryByTestId('sales-risk-exception-retry')).toBeNull()
  })

  it('approves a pending exception at its own version and shows it valid today', async () => {
    let lists = 0
    const harness = installFetch({
      capabilities: APPROVER,
      onDetail: () => json(detail()),
      onRiskExceptions: () => { lists += 1; return json({ items: [lists === 1 ? requested() : approved()] }) },
      onRiskExceptionCommand: () => json(approved()),
    })
    renderSalesApp(PATH)
    const section = await panel()
    const decision = await within(section).findByTestId('sales-risk-exception-1-decision')
    expect(within(section).queryByTestId('sales-risk-exception-request')).toBeNull()
    fireEvent.click(within(decision).getByRole('button', { name: T.approve }))

    expect(await within(section).findByText(T.approved)).toBeTruthy()
    expect((await within(section).findByTestId('sales-risk-exception-1-validity')).textContent).toBe(T.validToday('2026-11-10'))
    expect(within(section).queryByTestId('sales-risk-exception-1-decision')).toBeNull()
    const post = exceptionCalls(harness.calls).find((call) => call.method === 'POST')
    expect(post?.url).toMatch(new RegExp(`/sales/drafts/${DRAFT}/risk-exceptions/1/approval$`))
    expect(JSON.parse(post?.body as string)).toEqual({ expectedVersion: '1', reason: null })
  })

  it('needs a rejection reason, and names a self-decision refusal (SAT-D153)', async () => {
    const harness = installFetch({
      capabilities: APPROVER,
      onDetail: () => json(detail()),
      onRiskExceptions: () => json({ items: [requested()] }),
      onRiskExceptionCommand: () => problem(409, 'sales_risk_exception_self_decision'),
    })
    renderSalesApp(PATH)
    const section = await panel()
    const decision = await within(section).findByTestId('sales-risk-exception-1-decision')
    fireEvent.click(within(decision).getByRole('button', { name: T.reject }))
    expect(await within(section).findByText(T.invalidRejection)).toBeTruthy()
    expect(exceptionCalls(harness.calls).filter((call) => call.method === 'POST')).toEqual([])

    fireEvent.change(within(decision).getByLabelText(T.rejectionReason), { target: { value: ' SENTETIK risk çok yüksek ' } })
    fireEvent.click(within(decision).getByRole('button', { name: T.reject }))
    expect((await within(section).findByTestId('sales-risk-exception-notice')).textContent)
      .toContain(T.problem.sales_risk_exception_self_decision)
    const post = exceptionCalls(harness.calls).find((call) => call.method === 'POST')
    expect(post?.url).toMatch(/\/risk-exceptions\/1\/rejection$/)
    expect(JSON.parse(post?.body as string)).toEqual({ expectedVersion: '1', reason: 'SENTETIK risk çok yüksek' })
  })

  it('treats an answer for another exception or state as an unknown outcome, never as done', async () => {
    installFetch({
      capabilities: APPROVER,
      onDetail: () => json(detail()),
      onRiskExceptions: () => json({ items: [requested()] }),
      onRiskExceptionCommand: () => json(rejected()),
    })
    renderSalesApp(PATH)
    const section = await panel()
    fireEvent.click(within(await within(section).findByTestId('sales-risk-exception-1-decision')).getByRole('button', { name: T.approve }))
    expect(await within(section).findByText(T.unknown)).toBeTruthy()
    expect(within(section).getByTestId('sales-risk-exception-retry')).toBeTruthy()
  })
})

describe('risk exceptions panel outcomes', () => {
  const send = vi.fn()
  const show = (outcome: Parameters<typeof OrderRiskExceptions>[0]['outcome']) =>
    render(<OrderRiskExceptions outcome={outcome} canRequest canDecide send={send} onRecorded={() => undefined} />)

  it('is busy while loading and offers no action', () => {
    show(undefined)
    const section = screen.getByRole('region', { name: T.heading })
    expect(section.getAttribute('aria-busy')).toBe('true')
    expect(within(section).queryByTestId('sales-risk-exception-request')).toBeNull()
  })

  it('says a forbidden read in words and shows nothing else', () => {
    show({ type: 'http', status: 403, body: null })
    expect(screen.getByTestId('sales-risk-exception-forbidden').textContent).toBe(T.forbidden)
    expect(screen.queryByTestId('sales-risk-exception-request')).toBeNull()
  })

  it('names a 503 with its support code, never assuming an exception is valid', () => {
    show({ type: 'http', status: 503, body: { status: 503, title: 'unavailable', correlationId: 'corr-503' } })
    expect(screen.getByTestId('sales-risk-exception-unreadable').textContent).toBe(T.serviceUnavailable)
    expect(screen.getByTestId('sales-risk-exception-support-code').textContent).toBe(T.supportCode('corr-503'))
  })

  it.each([
    ['malformed', { type: 'malformed' as const }],
    ['offline', { type: 'offline' as const }],
    ['a conflict', { type: 'http' as const, status: 409, body: null }],
  ])('shows %s as unreadable with no exception and no action', (_, outcome) => {
    show(outcome)
    expect(screen.getByTestId('sales-risk-exception-unreadable').textContent).toBe(T.unreadable)
    expect(screen.queryByTestId('sales-risk-exception-request')).toBeNull()
    expect(screen.queryByRole('button', { name: T.approve })).toBeNull()
  })
})
