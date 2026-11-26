import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { ShellProvider } from '../shell/ShellProvider'
import { AppShell } from '../shell/AppShell'
import { COMPANY_A, installFetch, json } from '../settings/testHarness'
import { workRoutes } from './workRoutes'
import { shellRoutes } from '../shell/router'
import { ACCOUNTING_CAPABILITY, ACCOUNTING_ROUTE } from '../accounting/capabilities'
import { NOTIFICATION_TEXT as T } from './notificationText'

afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })
function mount(path = '/bildirimlerim', realShell = false) {
  const router = createMemoryRouter(realShell ? shellRoutes : [{ path: '/', element: <AppShell />, children: workRoutes }], { initialEntries: [path] })
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
  render(<ShellProvider queryClient={queryClient}><RouterProvider router={router} /></ShellProvider>)
}
const item = { id: 'bbbbbbbb-0000-4000-8000-000000000001', companyId: COMPANY_A, kind: 'allocation-released', salesOrderId: 'cccccccc-0000-4000-8000-000000000001', salesOrderLineId: 'dddddddd-0000-4000-8000-000000000001', sourceEntityId: 'eeeeeeee-0000-4000-8000-000000000001', sourceVersion: '2', reasonCode: 'acceptance-link-failed', reasonText: 'SENTETIK ayrım bırakıldı', occurredAtUtc: '2026-11-17T00:00:00Z', recordedAtUtc: '2026-11-17T00:00:01Z', passivatedAtUtc: null, purchaseOrderId: null, purchaseOrderLineId: null }
describe('personal notification inbox', () => {
  it.each([1, 2] as const)('links cost kind %s only to its existing authorized prepare route', async costKind => {
    const capability = costKind === 1 ? ACCOUNTING_CAPABILITY.invoicedShipmentPrepare : ACCOUNTING_CAPABILITY.purchaseCostPrepare
    const path = costKind === 1 ? ACCOUNTING_ROUTE.invoicedShipmentPrepare : ACCOUNTING_ROUTE.purchaseCostPrepare
    const stale = { ...item, kind: 'accounting-cost-proposal-stale', costKind, salesOrderId: null, salesOrderLineId: null,
      sourceVersion: '3', reasonCode: 'inventory_cost_preparation_stale', reasonText: 'Bayatladı' }
    installFetch({ capabilities: [capability], respond: url => url.includes('/work/notifications?')
      ? json({ companyId: COMPANY_A, items: [stale], hasMore: false, nextCursor: null }) : undefined })
    mount()
    expect((await screen.findByRole('link', { name: T.openCost })).getAttribute('href')).toBe(`${path}?proposalId=${stale.sourceEntityId}`)
  })
  it.each([ACCOUNTING_ROUTE.invoicedShipmentPrepare, ACCOUNTING_ROUTE.purchaseCostPrepare])('does not fetch owner data for an ungranted deep link %s', async path => {
    const calls = installFetch({ capabilities: [] })
    mount(`${path}?proposalId=${item.sourceEntityId}`, true)
    await waitFor(() => expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
    expect(calls.some(call => call.url.includes('/accounting/'))).toBe(false)
  })
  it('shows a stale cost proposal beside 99 old records with an Accounting next step and no guessed owner link', async () => {
    const stale = { ...item, kind: 'accounting-cost-proposal-stale', costKind: 1, salesOrderId: null, salesOrderLineId: null,
      sourceVersion: '3', reasonCode: 'inventory_cost_preparation_stale', reasonText: 'Bayatladı' }
    const items = [...Array.from({ length: 99 }, (_, index) => ({ ...item,
      id: `bbbbbbbb-0000-4000-8000-${(index + 2).toString().padStart(12, '0')}` })), stale]
    const calls = installFetch({ capabilities: ['sales.drafts.detail', 'purchasing.orders.detail'], respond: (url) => url.includes('/work/notifications?')
      ? json({ companyId: COMPANY_A, items, hasMore: true, nextCursor: stale.id }) : undefined })
    mount()
    expect(await screen.findByText(T.staleCost)).toBeTruthy()
    expect(screen.getByText(T.staleCostNextStep)).toBeTruthy()
    expect(screen.getByText(`${T.costProposal}: ${stale.sourceEntityId}`)).toBeTruthy()
    expect(screen.getAllByText(item.reasonText)).toHaveLength(99)
    const staleRow = screen.getByText(T.staleCost).closest('tr')!
    expect(staleRow.querySelector('a')).toBeNull()
    expect(staleRow.textContent).not.toContain(T.revived)
    expect(calls.filter(call => call.url.includes('/work/notifications?')).every(call => call.headers['X-Company-Id'] === COMPANY_A)).toBe(true)
    expect(calls.some(call => /\/api\/v1\/(?:accounting|sales|purchasing)\//.test(call.url))).toBe(false)
    expect(screen.getByRole('button', { name: T.more })).toBeTruthy()
  })
  it('shows a revived purchase source without granting access to the purchase order', async () => {
    const purchase = { ...item, kind: 'purchase-supply-revived', salesOrderId: null, salesOrderLineId: null,
      purchaseOrderId: item.salesOrderId, purchaseOrderLineId: item.salesOrderLineId, reasonText: 'SENTETIK tedarik yeniden açıldı' }
    installFetch({ capabilities: [], respond: (url) => url.includes('/work/notifications?')
      ? json({ companyId: COMPANY_A, items: [purchase], hasMore: false, nextCursor: null }) : undefined })
    mount()
    expect(await screen.findByText(T.revived)).toBeTruthy()
    expect(screen.getByText(purchase.reasonText)).toBeTruthy()
    expect(screen.queryByRole('link', { name: T.order })).toBeNull()
  })
  it('renders server reasons and passive history without adding a sales record permission', async () => {
    const calls = installFetch({ capabilities: [], respond: (url) => url.includes('/work/notifications?')
      ? json({ companyId: COMPANY_A, items: [{ ...item, passivatedAtUtc: url.includes('includePassive=true') ? '2027-02-15T00:00:00Z' : null }], hasMore: false, nextCursor: null }) : undefined })
    mount()
    expect(await screen.findByText(item.reasonText)).toBeTruthy()
    expect(screen.queryByRole('link', { name: T.order })).toBeNull()
    await userEvent.setup().click(screen.getByLabelText(T.includePassive))
    expect(await screen.findByText(T.passive)).toBeTruthy()
    expect(calls.filter((call) => call.url.includes('/work/notifications?')).every((call) => call.headers['X-Company-Id'] === COMPANY_A)).toBe(true)
    expect(calls.some((call) => call.url.includes('recipient'))).toBe(false)
  })
  it('rejects a foreign successful body and renders no foreign reason', async () => {
    // A body that is coherent for ANOTHER company (envelope and row agree): only the company fence can refuse it.
    const foreign = 'ffffffff-0000-4000-8000-000000000001'
    const calls = installFetch({ capabilities: [], respond: (url) => url.includes('/work/notifications?')
      ? json({ companyId: foreign, items: [{ ...item, companyId: foreign }], hasMore: false, nextCursor: null }) : undefined })
    mount()
    await screen.findByRole('heading', { name: T.heading })
    await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    expect(calls.filter((call) => call.url.includes('/work/notifications?')).map((call) => call.headers['X-Company-Id'])).toEqual([COMPANY_A])
    expect(screen.queryByText(item.reasonText)).toBeNull()
    expect(screen.queryByRole('table')).toBeNull()
  })
})
