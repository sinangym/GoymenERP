/**
 * BLG-01 through the REAL shell: `ShellProvider`, `AppShell` bootstrap, `CapabilityRoute` entry gate, the auth
 * boundary and the real purchasing route table, with `fetch` stubbed by SENTETIK fixtures.
 *
 * The claim: an unknown goods receipt survives a 403 capability re-fence and a fresh entry under a NEW capability
 * generation (same person, same session, same company). The fresh entry finds the same attempt, sends nothing on its
 * own, and a retry sends the exact same Idempotency-Key and body, so the server records one receipt.
 *
 * NOT evidence of the real API, SQL or browser acceptance (NOT_ENV); the browser step is PUR01 in the E2E suite.
 */

import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter, type RouteObject } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ShellProvider } from '../shell/ShellProvider'
import { AppShell } from '../shell/AppShell'
import { StateBlock } from '../shell/StateBlock'
import { SHELL_CAPABILITY_CONTRACT_VERSION } from '../shell/api'
import { PURCHASING_CAPABILITY, PURCHASING_ROUTE } from './capabilities'
import { purchasingRoutes } from './purchasingRoutes'
import { resetReceiptAttemptsForTests } from './goodsReceiptAttempt'
import { PURCHASE_GOODS_RECEIPT_CAPABILITY, type GoodsReceipt, type GoodsReceiptRequest } from './goodsReceiptContract'
import { GOODS_RECEIPT_TEXT as T } from './goodsReceiptText'

const ACTOR = 'c1000000-0000-4000-8000-000000000001'
const COMPANY = 'c2000000-0000-4000-8000-000000000001'
const ORDER = 'c3000000-0000-4000-8000-000000000001'
const LINE = 'c4000000-0000-4000-8000-000000000001'
const PRODUCT = 'c5000000-0000-4000-8000-000000000001'
const UNIT = 'c6000000-0000-4000-8000-000000000001'
const LOCATION = 'c7000000-0000-4000-8000-000000000001'
const SUPPLIER = 'c8000000-0000-4000-8000-000000000001'
const ACCOUNT = 'c9000000-0000-4000-8000-000000000001'

const CAPABILITIES = [PURCHASING_CAPABILITY.orderList, PURCHASING_CAPABILITY.orderDetail, PURCHASE_GOODS_RECEIPT_CAPABILITY]

function order(lineCount = 1) {
  return {
    id: ORDER, companyId: COMPANY, status: 'Approved', version: '3', supplierPartyId: SUPPLIER,
    companyAccountId: ACCOUNT, sourceRequestId: null, effectiveOn: '2026-09-25',
    createdAtUtc: '2026-09-25T09:00:00Z', updatedAtUtc: '2026-09-25T09:00:00Z', lineCount,
    supplierDisplay: { partyId: SUPPLIER, code: 'SENTETIK-TED', legalName: 'SENTETIK Tedarikçi', tradeName: null },
  }
}

const LINE_VIEW = {
  lineId: LINE, lineNo: 1, kind: 'General', parentLineId: null, sourceRequestLineId: null,
  productId: PRODUCT, productVersion: '7', unitId: UNIT, ordered: '10', technical: '0', committed: '10', accepted: '0', remaining: '10',
}

function recorded(payload: GoodsReceiptRequest): GoodsReceipt {
  return {
    companyId: COMPANY, acceptanceId: payload.acceptanceId, purchaseOrderId: ORDER, purchaseOrderLineId: LINE,
    purchaseOrderVersion: '4', productId: PRODUCT, productVersion: '7', unitId: UNIT, acceptedQuantity: payload.acceptedQuantity,
    remainingQuantity: '8', lotId: payload.lotId, labelId: payload.labelId, labelCode: payload.labelCode, labelVersion: '1',
    movementId: payload.movementId, locationId: payload.locationId, measuredQuantityKg: payload.measuredQuantityKg,
    initialBlock: 'Free', occurredAtUtc: payload.occurredAtUtc,
  }
}

const json = (body: unknown, status = 200, headers: Record<string, string> = {}) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } })

function resetCompanyPreference(): void {
  const store = new Map<string, string>()
  const stub: Storage = {
    get length() { return store.size },
    clear: () => store.clear(),
    getItem: (key: string) => store.get(key) ?? null,
    key: (index: number) => [...store.keys()][index] ?? null,
    removeItem: (key: string) => void store.delete(key),
    setItem: (key: string, value: string) => void store.set(key, String(value)),
  }
  Object.defineProperty(window, 'localStorage', { value: stub, configurable: true, writable: true })
}

/** A synthetic server: one generation per capability snapshot read, one stored receipt, the first POST unanswered. */
function installServer() {
  resetCompanyPreference()
  const server = {
    capabilityReads: 0,
    forbidNextList: false,
    posts: [] as { key: string; body: string }[],
    receipts: [] as GoodsReceipt[],
  }
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = typeof input === 'string' ? input : input.toString()
    const method = init?.method ?? 'GET'
    if (url.endsWith('/identity/me')) {
      return json({ userId: ACTOR, displayName: 'SENTETIK Kullanıcı', email: null, allowedCompanyIds: [COMPANY],
        authenticationSessionBinding: null })
    }
    if (url.endsWith('/organization/companies')) {
      return json([{ id: COMPANY, code: 'GDC', legalName: 'SENTETIK Demir Çelik', isPlaceholder: false }])
    }
    if (url.endsWith('/shell/capabilities')) {
      server.capabilityReads += 1
      return json({ contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION, generation: `pur-cap-gen-${server.capabilityReads}`,
        capabilities: CAPABILITIES })
    }
    if (method === 'POST' && url.endsWith(`/lines/${LINE}/goods-receipts`)) {
      server.posts.push({ key: new Headers(init?.headers).get('Idempotency-Key') ?? '', body: init?.body as string })
      const payload = JSON.parse(init?.body as string) as GoodsReceiptRequest
      if (server.receipts.length === 0) server.receipts.push(recorded(payload))
      // The first answer is lost (the server did record it): the client must treat it as unknown.
      if (server.posts.length === 1) return json({}, 503)
      return json({ receipt: server.receipts[0], isReplay: true, originalCorrelationId: 'original' }, 201,
        { 'Idempotency-Replayed': 'true' })
    }
    if (url.endsWith('goods-receipt-options')) {
      return json({ source: { companyId: COMPANY, purchaseOrderId: ORDER, purchaseOrderLineId: LINE, purchaseOrderVersion: '3',
        productId: PRODUCT, productVersion: '7', unitId: UNIT, effectiveOn: '2026-09-25' },
      locations: [{ locationId: LOCATION, locationCode: 'D-01', locationName: 'Mal Kabul', siteName: 'Fabrika' }],
      searchWindowFull: false })
    }
    if (url.includes('/quantity-comparison')) return json({ code: 'not_found' }, 404)
    if (url.endsWith('/goods-receipts')) return json({ items: server.receipts, searchWindowFull: false })
    if (url.endsWith(`/purchasing/orders/${ORDER}`)) return json({ order: order(), lines: [LINE_VIEW], acceptances: [] })
    if (url.includes('/purchasing/orders')) {
      if (server.forbidNextList) {
        server.forbidNextList = false
        return json({ code: 'forbidden' }, 403)
      }
      return json({ items: [], nextCursor: null })
    }
    return json({ type: 'about:blank', title: 'Hata', status: 404 }, 404)
  }))
  return server
}

function mount(path: string) {
  const routes: RouteObject[] = [{
    path: '/',
    element: <AppShell />,
    children: [...purchasingRoutes, { path: '*', element: <StateBlock state={{ kind: 'not-found' }} subject="sayfa" /> }],
  }]
  const router = createMemoryRouter(routes, { initialEntries: [path] })
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
  render(<ShellProvider queryClient={queryClient}><RouterProvider router={router} /></ShellProvider>)
  return { router, queryClient }
}

const detailPath = PURCHASING_ROUTE[PURCHASING_CAPABILITY.orderDetail].replace(':orderId', ORDER)
const listPath = PURCHASING_ROUTE[PURCHASING_CAPABILITY.orderList]

async function fillAndSave() {
  await screen.findByRole('option', { name: 'Fabrika / D-01 Mal Kabul' }, { timeout: 5000 })
  await userEvent.selectOptions(screen.getByLabelText(T.location), LOCATION)
  await userEvent.type(screen.getByLabelText(T.accepted), '2')
  await userEvent.type(screen.getByLabelText(T.measured), '1,9')
  await userEvent.type(screen.getByLabelText(T.label), 'SENTETIK-RECEIPT-001')
  await userEvent.click(screen.getByRole('button', { name: T.save }))
}

afterEach(() => {
  resetReceiptAttemptsForTests()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

describe('goods receipt attempt through the real shell (BLG-01, NOT_ENV)', () => {
  it('finds an unknown receipt again after a 403 re-fence and a fresh capability generation; one retry, one receipt', async () => {
    const server = installServer()
    const { router, queryClient } = mount(detailPath)
    await fillAndSave()
    await screen.findByText(T.unknown, undefined, { timeout: 5000 })
    expect(server.posts).toHaveLength(1)

    // A purchasing read is refused: the shell strips the capability snapshot and the gated route closes.
    server.forbidNextList = true
    await act(async () => { await router.navigate(listPath) })
    await waitFor(() => expect(screen.queryByText(/Bulunamadı/)).not.toBeNull(), { timeout: 5000 })
    const readsBeforeReentry = server.capabilityReads

    // A fresh entry verifies a NEW snapshot (new generation, same grant): the query namespace changes.
    await act(async () => { await router.navigate(detailPath) })
    await screen.findByText(T.unknown, undefined, { timeout: 5000 })
    expect(server.capabilityReads).toBeGreaterThan(readsBeforeReentry)
    const namespaces = queryClient.getQueryCache().getAll()
      .map((query) => query.queryKey[0])
      .filter((first): first is string => typeof first === 'string' && first.startsWith('['))
    expect(namespaces.length).toBeGreaterThan(0)
    expect(namespaces.every((namespace) => namespace.includes(`pur-cap-gen-${server.capabilityReads}`))).toBe(true)

    // The recalled attempt keeps its facts and sends nothing on its own.
    expect((screen.getByLabelText(T.label) as HTMLInputElement).value).toBe('SENTETIK-RECEIPT-001')
    expect(server.posts).toHaveLength(1)

    await userEvent.click(screen.getByRole('button', { name: T.retry }))
    await waitFor(() => expect(server.posts).toHaveLength(2))
    expect(server.posts[1]).toEqual(server.posts[0])
    expect(server.posts[0].key).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/)
    await screen.findByText(new RegExp(T.replayed))
    expect(server.receipts).toHaveLength(1)
  })
})
