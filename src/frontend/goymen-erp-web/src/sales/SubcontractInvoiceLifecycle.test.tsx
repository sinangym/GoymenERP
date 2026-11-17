import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { SubcontractInvoiceLifecycle } from './SubcontractInvoiceLifecycle'
import { SUBCONTRACT_LIFECYCLE_TEXT as L } from './SubcontractLifecycleText'
import { INVOICE_FINALIZATION_TEXT as T } from './invoiceText'
import { plannedInstalments } from './invoiceTestData'
import { subcontractInvoiceDetail, SUBCONTRACT_INVOICE_ID, SUBCONTRACT_ORDER_ID } from './subcontractInvoiceTestData'
import { resetPendingPoolAttempts } from './pendingPoolAttempts'

const companyId = '11111111-1111-4111-8111-111111111111'
const auth = vi.hoisted(() => ({ capture: vi.fn(() => ({ token: 1 })), report: vi.fn() }))
const dispatch = vi.hoisted(() => vi.fn())
const enabled = vi.hoisted(() => ({ capabilities: ['sales.invoices.write'] }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => auth }))
vi.mock('../shell/shellContext', async original => ({
  ...await original<typeof import('../shell/shellContext')>(),
  useShellState: () => ({ sessionEpoch: 1, routeEntryGeneration: 0, requestFenceGeneration: 0,
    committedContextRevision: 1, activeContext: { scopeMode: 'single', companyId: '11111111-1111-4111-8111-111111111111',
      userId: 'SENTETIK-user', capabilityGeneration: '1', capabilities: enabled.capabilities } }),
  useShellDispatch: () => dispatch,
}))
vi.mock('../shell/shellState', async original => ({
  ...await original<typeof import('../shell/shellState')>(),
  hasCapability: (_state: unknown, capability: string) => enabled.capabilities.includes(capability),
}))

const body = (state = 'Draft', version = '1') => subcontractInvoiceDetail({ state, version,
  lifecycle: { decision: null, instalments: plannedInstalments(), exchangeRate: null, functionalTotals: null, match: null, finalization: null } })
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status,
  headers: { 'Content-Type': status === 200 ? 'application/json' : 'application/problem+json' } })
const listing = () => ({ salesOrderId: SUBCONTRACT_ORDER_ID, items: [{ id: SUBCONTRACT_INVOICE_ID,
  documentNo: 'SFH-2026-0003', state: 'Draft', version: '1', businessDate: '2026-11-14', createdAtUtc: '2026-11-14T09:30:00Z' }], nextCursor: null })
async function mountAndOpen() {
  const mounted = render(<SubcontractInvoiceLifecycle salesOrderId={SUBCONTRACT_ORDER_ID} initialInvoiceId={SUBCONTRACT_INVOICE_ID} />)
  fireEvent.click(await screen.findByRole('button', { name: L.open }))
  return mounted
}
beforeEach(() => { enabled.capabilities = ['sales.invoices.write']; auth.report.mockClear() })
afterEach(() => { cleanup(); resetPendingPoolAttempts(); vi.unstubAllGlobals() })

describe('SC34 lifecycle screen and held command', () => {
  it('selects a real order invoice by document metadata and pages with the server cursor, without a technical id textbox', async () => {
    enabled.capabilities = ['sales.invoices.read']
    const urls: string[] = []
    const secondId = 'f5000000-0000-4000-8000-000000000008'
    vi.stubGlobal('fetch', vi.fn(async (url: string) => {
      urls.push(url)
      if (url.includes('cursor=SENTETIK-next')) return response({ ...listing(),
        items: [{ ...listing().items[0], id: secondId, documentNo: 'SFH-2026-0002' }] })
      return response({ ...listing(), nextCursor: 'SENTETIK-next' })
    }))
    render(<SubcontractInvoiceLifecycle salesOrderId={SUBCONTRACT_ORDER_ID} />)
    expect((await screen.findByRole('combobox', { name: L.invoice }) as HTMLSelectElement).value).toBe(SUBCONTRACT_INVOICE_ID)
    expect(screen.getByRole('option').textContent).toContain('SFH-2026-0003')
    expect(screen.queryByRole('textbox')).toBeNull()
    expect(urls[0]).toContain(`/by-order/${SUBCONTRACT_ORDER_ID}?pageSize=50`)
    fireEvent.click(screen.getByRole('button', { name: L.nextPage }))
    await screen.findByRole('option', { name: /SFH-2026-0002/ })
    expect((screen.getByRole('combobox', { name: L.invoice }) as HTMLSelectElement).value).toBe(secondId)
    expect(urls[1]).toContain('cursor=SENTETIK-next')
    fireEvent.click(screen.getByRole('button', { name: L.firstPage }))
    await screen.findByRole('option', { name: /SFH-2026-0003/ })
  })

  it('shows an empty order list and rejects metadata from another order before allowing an invoice to open', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => response({ ...listing(), items: [] })))
    const first = render(<SubcontractInvoiceLifecycle salesOrderId={SUBCONTRACT_ORDER_ID} />)
    await screen.findByText(L.emptyList)
    expect(screen.queryByRole('button', { name: L.open })).toBeNull()
    first.unmount()
    vi.stubGlobal('fetch', vi.fn(async () => response({ ...listing(), salesOrderId: companyId })))
    render(<SubcontractInvoiceLifecycle salesOrderId={SUBCONTRACT_ORDER_ID} />)
    await waitFor(() => expect(screen.queryByText(/Yükleniyor/)).toBeNull())
    expect(screen.queryByRole('combobox')).toBeNull()
    expect(screen.queryByRole('button', { name: L.open })).toBeNull()
  })

  it('keeps the same unknown command across unmount and retries its exact company, version and key', async () => {
    const posts: RequestInit[] = []
    vi.stubGlobal('fetch', vi.fn(async (url: string, init: RequestInit = {}) => {
      if (url.includes('/by-order/')) return response(listing())
      if (init.method !== 'POST') return response(body())
      posts.push(init)
      return posts.length === 1 ? response({ status: 503, code: 'service_unavailable' }, 503) : response(body('PendingApproval', '2'))
    }))
    const first = await mountAndOpen()
    fireEvent.click(await screen.findByRole('button', { name: L.actions.submit }))
    await screen.findByRole('button', { name: T.retry })
    first.unmount()
    await mountAndOpen()
    fireEvent.click(await screen.findByRole('button', { name: T.retry }))
    await waitFor(() => expect(posts).toHaveLength(2))
    const headers = posts.map(item => new Headers(item.headers))
    expect(headers[0].get('Idempotency-Key')).toBe(headers[1].get('Idempotency-Key'))
    expect(headers[1].get('X-Company-Id')).toBe(companyId)
    expect(posts[0].body).toBe(posts[1].body)
    expect(JSON.parse(posts[1].body as string)).toEqual({ expectedVersion: '1' })
  })

  it('shows a readable invoice without making controls available to a reader', async () => {
    enabled.capabilities = ['sales.invoices.read']
    vi.stubGlobal('fetch', vi.fn(async (url: string) => response(url.includes('/by-order/') ? listing() : body())))
    await mountAndOpen()
    await screen.findByText(L.noControls)
    expect(screen.queryByRole('button', { name: L.actions.submit })).toBeNull()
    expect(screen.queryByRole('button', { name: L.actions.cancel })).toBeNull()
  })

  it('fences a denied source read and never exposes commands for a malformed or foreign-order body', async () => {
    vi.stubGlobal('fetch', vi.fn(async (url: string) => url.includes('/by-order/') ? response(listing())
      : response({ status: 403, code: 'forbidden' }, 403)))
    const first = await mountAndOpen()
    await waitFor(() => expect(auth.report).toHaveBeenCalledWith(403, { token: 1 }))
    expect(screen.queryByRole('button', { name: L.actions.submit })).toBeNull()
    first.unmount()
    for (const value of [{ ...body(), salesOrderId: companyId }, { ...body(), version: 7 }]) {
      vi.stubGlobal('fetch', vi.fn(async (url: string) => response(url.includes('/by-order/') ? listing() : value)))
      const panel = await mountAndOpen()
      await waitFor(() => expect(screen.queryByText(/Yükleniyor/)).toBeNull())
      expect(screen.queryByRole('button', { name: L.actions.submit })).toBeNull()
      panel.unmount()
    }
  })
})
