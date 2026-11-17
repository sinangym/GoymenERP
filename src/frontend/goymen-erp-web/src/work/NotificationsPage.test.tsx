import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { ShellProvider } from '../shell/ShellProvider'
import { AppShell } from '../shell/AppShell'
import { COMPANY_A, installFetch, json } from '../settings/testHarness'
import { workRoutes } from './workRoutes'
import { NOTIFICATION_TEXT as T } from './notificationText'

afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })
function mount() {
  const router = createMemoryRouter([{ path: '/', element: <AppShell />, children: workRoutes }], { initialEntries: ['/bildirimlerim'] })
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
  render(<ShellProvider queryClient={queryClient}><RouterProvider router={router} /></ShellProvider>)
}
const item = { id: 'bbbbbbbb-0000-4000-8000-000000000001', companyId: COMPANY_A, kind: 'allocation-released', salesOrderId: 'cccccccc-0000-4000-8000-000000000001', salesOrderLineId: 'dddddddd-0000-4000-8000-000000000001', sourceEntityId: 'eeeeeeee-0000-4000-8000-000000000001', sourceVersion: '2', reasonCode: 'acceptance-link-failed', reasonText: 'SENTETIK ayrım bırakıldı', occurredAtUtc: '2026-11-17T00:00:00Z', recordedAtUtc: '2026-11-17T00:00:01Z', passivatedAtUtc: null, purchaseOrderId: null, purchaseOrderLineId: null }
describe('personal notification inbox', () => {
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
    installFetch({ capabilities: [], respond: (url) => url.includes('/work/notifications?')
      ? json({ companyId: item.id, items: [item], hasMore: false, nextCursor: null }) : undefined })
    mount()
    await screen.findByRole('heading', { name: T.heading })
    await waitFor(() => expect(screen.queryByText(item.reasonText)).toBeNull())
    expect(screen.queryByRole('table')).toBeNull()
  })
})
