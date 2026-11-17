/**
 * F2 "Fason hizmet faturası taslağı hazırla", SENTETIK consumer tests (NOT_ENV): the shell hooks are mocked and the panel is
 * mounted as the order detail mounts it on a subcontract service order with `sales.invoices.write`. Proves: the body names the
 * order, the instruction and the weighing only; ids are checked locally and nothing is sent for a bad one; a 409
 * `invoice_not_ready` lists the server's reasons verbatim and keeps the inputs open; a created draft shows the server's net kg,
 * billed kg and amounts verbatim; an unknown outcome keeps the SAME key and freezes the inputs; 401/403 go to the auth boundary.
 */
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { SubcontractInvoiceCreate } from './SubcontractInvoiceCreate'
import { SUBCONTRACT_INVOICE_TEXT as S, subcontractInvoiceFieldText, subcontractInvoiceReasonText } from './SubcontractInvoiceText'
import { INVOICE_FINALIZATION_TEXT as T } from './invoiceText'
import { resetPendingPoolAttempts } from './pendingPoolAttempts'
import {
  LOADING_INSTRUCTION_ID,
  SUBCONTRACT_INVOICE_NO,
  SUBCONTRACT_ORDER_ID,
  WEIGHING_ID,
  subcontractInvoiceDetail,
} from './subcontractInvoiceTestData'

const boundary = vi.hoisted(() => ({ capture: vi.fn(() => ({ token: 1 })), report: vi.fn() }))
const dispatch = vi.hoisted(() => vi.fn())
const shell = vi.hoisted(() => ({
  state: {
    activeContext: null as null | { scopeMode: string; companyId: string | null; userId: string },
    sessionEpoch: 1,
    routeEntryGeneration: 0,
  },
}))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => boundary }))
vi.mock('../shell/shellContext', async (original) => ({
  ...await original<typeof import('../shell/shellContext')>(),
  useShellState: () => shell.state,
  useShellDispatch: () => dispatch,
}))

const COMPANY = '99999999-9999-9999-9999-999999999999'

interface Call {
  readonly url: string
  readonly method: string
  readonly company: string | null
  readonly key: string | null
  readonly body: string | null
}

const json = (body: unknown, status = 200, headers: Record<string, string> = {}) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } })
const problem = (status: number, code: string | null, errors: readonly unknown[] = []) =>
  new Response(JSON.stringify({ status, title: 'Problem', errors, ...(code === null ? {} : { code }) }), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  })

function serve(command: (call: Call, index: number) => Response | Promise<Response>) {
  const calls: Call[] = []
  vi.stubGlobal('fetch', vi.fn(async (url: string, init: RequestInit = {}) => {
    if (url.includes('/by-order/')) return json({ salesOrderId: SUBCONTRACT_ORDER_ID, items: [], nextCursor: null })
    const headers = (init.headers ?? {}) as Record<string, string>
    const call: Call = {
      url,
      method: (init.method ?? 'GET').toUpperCase(),
      company: headers['X-Company-Id'] ?? null,
      key: headers['Idempotency-Key'] ?? null,
      body: typeof init.body === 'string' ? init.body : null,
    }
    calls.push(call)
    if (call.method !== 'POST') throw new Error(`unexpected ${call.method} ${url}`)
    return command(call, calls.length - 1)
  }))
  return calls
}

function mount(onCreated = vi.fn()) {
  render(<SubcontractInvoiceCreate salesOrderId={SUBCONTRACT_ORDER_ID} onCreated={onCreated} />)
  return { onCreated, panel: screen.getByTestId('sales-subcontract-invoice') }
}

function fill(panel: HTMLElement, instruction = LOADING_INSTRUCTION_ID, weighing = WEIGHING_ID) {
  fireEvent.change(within(panel).getByTestId('sales-subcontract-invoice-loadingInstructionId'), { target: { value: instruction } })
  fireEvent.change(within(panel).getByTestId('sales-subcontract-invoice-weighingId'), { target: { value: weighing } })
}

const create = (panel: HTMLElement) => fireEvent.click(within(panel).getByTestId('sales-subcontract-invoice-create'))
const notice = () => screen.findByTestId('sales-subcontract-invoice-notice')

beforeEach(() => {
  shell.state = { activeContext: { scopeMode: 'single', companyId: COMPANY, userId: 'actor-a' }, sessionEpoch: 1, routeEntryGeneration: 0 }
  boundary.capture.mockClear()
  boundary.report.mockClear()
})

afterEach(() => {
  cleanup()
  resetPendingPoolAttempts()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('F2 subcontract service invoice create', () => {
  it('sends the order, the instruction and the weighing only, then shows the server draft verbatim', async () => {
    const calls = serve(() => json(subcontractInvoiceDetail(), 201))
    const { panel, onCreated } = mount()
    fill(panel, ` ${LOADING_INSTRUCTION_ID} `)
    create(panel)

    expect((await notice()).textContent).toContain(S.completed(SUBCONTRACT_INVOICE_NO))
    expect(calls).toHaveLength(1)
    expect(calls[0].url).toBe('/api/v1/sales/subcontract-service-invoices')
    expect(calls[0].company).toBe(COMPANY)
    expect(calls[0].key).toMatch(/\S{8,}/)
    expect(JSON.parse(calls[0].body ?? 'null')).toEqual({
      salesOrderId: SUBCONTRACT_ORDER_ID,
      loadingInstructionId: LOADING_INSTRUCTION_ID,
      weighingId: WEIGHING_ID,
    })
    expect(onCreated).toHaveBeenCalledTimes(1)
    const created = screen.getByTestId('sales-subcontract-invoice-created')
    expect(within(created).getByTestId('sales-subcontract-invoice-document-no').textContent).toBe(SUBCONTRACT_INVOICE_NO)
    expect(within(created).getByTestId('sales-subcontract-invoice-net-kg').textContent).toBe('1234.560')
    expect(within(created).getByTestId('sales-subcontract-invoice-payable').textContent).toBe('1180.40')
    const line = within(created).getByTestId('sales-subcontract-invoice-line-1')
    expect(line.textContent).toContain('1234.560')
    expect(line.textContent).toContain('0.81')
  })

  it('refuses a missing or malformed id locally and sends nothing', () => {
    const calls = serve(() => problem(500, null))
    const { panel } = mount()
    create(panel)
    expect(within(panel).getByTestId('sales-subcontract-invoice-error').textContent).toBe(S.required)
    fill(panel, 'not-a-uuid')
    create(panel)
    expect(within(panel).getByTestId('sales-subcontract-invoice-loadingInstructionId-error').textContent).toBe(S.invalidId)
    expect(within(panel).queryByTestId('sales-subcontract-invoice-weighingId-error')).toBeNull()
    expect(calls).toHaveLength(0)
  })

  it('lists the server reasons of invoice_not_ready verbatim and keeps the inputs open', async () => {
    serve(() => problem(409, 'invoice_not_ready', [
      { field: 'weighingId', code: 'weighing_missing' },
      { field: 'lines[0]', code: 'subcontract_share_policy_pending' },
      { field: 'salesOrderId', code: 'some_future_code' },
    ]))
    const { panel } = mount()
    fill(panel)
    create(panel)
    const shown = await notice()
    expect(within(shown).getByText(S.notReady)).toBeTruthy()
    expect(within(within(shown).getByRole('list', { name: S.notReady })).getAllByRole('listitem').map((item) => item.textContent)).toEqual([
      S.notReadyItem(subcontractInvoiceFieldText('weighingId'), subcontractInvoiceReasonText('weighing_missing')),
      S.notReadyItem(subcontractInvoiceFieldText('lines[0]'), subcontractInvoiceReasonText('subcontract_share_policy_pending')),
      S.notReadyItem(subcontractInvoiceFieldText('salesOrderId'), 'some_future_code'),
    ])
    expect((within(panel).getByTestId('sales-subcontract-invoice-weighingId') as HTMLInputElement).disabled).toBe(false)
    expect(within(panel).queryByTestId('sales-subcontract-invoice-retry')).toBeNull()
  })

  it('asks afresh with a new key after a not-ready refusal', async () => {
    const calls = serve((_call, index) =>
      index === 0 ? problem(409, 'invoice_not_ready', [{ field: 'weighingId', code: 'weighing_missing' }]) : json(subcontractInvoiceDetail(), 201))
    const { panel } = mount()
    fill(panel)
    create(panel)
    await screen.findByRole('list', { name: S.notReady })
    create(panel)
    expect((await screen.findByTestId('sales-subcontract-invoice-created'))).toBeTruthy()
    expect(calls).toHaveLength(2)
    expect(calls[1].key).not.toBe(calls[0].key)
  })

  it('keeps the SAME key and freezes the inputs after an unknown outcome until it is retried', async () => {
    const calls = serve((_call, index) => (index === 0 ? problem(503, null) : json(subcontractInvoiceDetail(), 201)))
    const { panel } = mount()
    fill(panel)
    create(panel)
    expect((await notice()).textContent).toContain(T.unknownServiceUnavailable)
    expect((within(panel).getByTestId('sales-subcontract-invoice-weighingId') as HTMLInputElement).disabled).toBe(true)
    fireEvent.click(within(panel).getByTestId('sales-subcontract-invoice-retry'))
    await screen.findByTestId('sales-subcontract-invoice-created')
    expect(calls).toHaveLength(2)
    expect(calls[1].key).toBe(calls[0].key)
    expect(calls[1].body).toBe(calls[0].body)
  })

  it('leaves an unknown outcome on request', async () => {
    serve(() => problem(503, null))
    const { panel } = mount()
    fill(panel)
    create(panel)
    await within(panel).findByTestId('sales-subcontract-invoice-abandon')
    fireEvent.click(within(panel).getByTestId('sales-subcontract-invoice-abandon'))
    expect((await notice()).textContent).toBe(T.abandoned)
  })

  it.each([401, 403])('reports %i to the auth boundary', async (status) => {
    serve(() => problem(status, null))
    const { panel } = mount()
    fill(panel)
    create(panel)
    await notice()
    await waitFor(() => expect(boundary.report).toHaveBeenCalledWith(status, { token: 1 }))
  })
})
