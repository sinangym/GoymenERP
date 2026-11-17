import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router'
import { InventoryCostProposalsPage } from './InventoryCostProposalsPage'
import { INVENTORY_COST_TEXT as T } from './inventoryCostText'

const state = vi.hoisted(() => ({ company: 'e0431000-0000-4000-8000-000000000001', actor: 'e0431000-0000-4000-8000-000000000002' }))
const maker = 'e0431000-0000-4000-8000-000000000003'
const source = 'e0431000-0000-4000-8000-000000000004'
const cogs = 'e0431000-0000-4000-8000-000000000621'; const counter = 'e0431000-0000-4000-8000-000000000153'
vi.mock('../shell/shellContext', () => ({ useShellState: () => ({ routeEntryGeneration: 1,
  activeContext: { companyId: state.company, userId: state.actor, scopeMode: 'single' } }) }))
vi.mock('../shell/shellState', () => ({ contextKey: () => `${state.company}:${state.actor}`, contextKeyString: (key: string) => key }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => ({ capture: () => 'SENTETIK', report: vi.fn() }) }))
const row = (patch: Record<string, unknown> = {}) => ({ proposalId: source, companyId: state.company, eventId: source,
  kind: 1, invoiceId: source, invoiceVersion: 2, admissionId: source, invoiceDate: '2026-11-17', officialInvoiceNumber: 'SENTETIK-F1',
  component: { componentKey: `invoice-line:${source}`, productId: source, invoiceLineId: source, invoiceLineNumber: 1, locationId: null,
    signedTlAmount: { numerator: '600', denominator: '1' }, sourceProofSha256: 'a'.repeat(64) },
  version: 2, status: 'Prepared', sourceOccurredAtUtc: '2026-11-17T09:00:00Z', exactTlAmount: '600',
  preparation: { periodId: source, accountingDate: '2026-11-17', transactionDate: '2026-11-17', currencyId: source,
    signedAmount: '600', moneyProfileId: source, moneyProfileVersion: 1, moneyScale: 2,
    classification: { classification: 1, accountId: cogs, origin: 'Product', productVersion: '1', classificationVersion: '1', mappingVersion: 1, invoiceLineRevision: 0 },
    counterAccount: { accountId: counter, code: '153', name: 'SENTETIK karşı hesap' },
    accountPlanPackageId: source, accountPlanPackageVersion: 1, accountPlanCatalogVersion: 1, accountPlanSourceVersion: 'SENTETIK-plan',
    accountPlanSha256: 'b'.repeat(64), preparedBy: maker, preparedAtUtc: '2026-11-17T09:01:00Z', reason: 'SENTETIK gerçek kaynak' },
  approvedBy: null, approvedAtUtc: null, journalId: null, cancellationId: null, reversalProposal: null, ...patch })
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
const mount = (canApprove = true) => render(<MemoryRouter><QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
  <InventoryCostProposalsPage kind={1} canPrepare={false} canApprove={canApprove} canProposeReversal={false} /></QueryClientProvider></MemoryRouter>)
afterEach(() => { cleanup(); vi.unstubAllGlobals(); state.actor = 'e0431000-0000-4000-8000-000000000002' })

describe('inventory cost human approval flow', () => {
  it('shows an actual empty queue and never creates an automatic proposal from the browser', async () => {
    const fetch = vi.fn(async () => json({ companyId: state.company, items: [] })); vi.stubGlobal('fetch', fetch)
    mount(); await screen.findByText(T.empty); expect(screen.queryByRole('button', { name: T.approve })).toBeNull()
    expect(fetch.mock.calls).toHaveLength(1)
  })
  it('does not offer approval to the maker or a read-only user', async () => {
    state.actor = maker; vi.stubGlobal('fetch', vi.fn(async () => json({ companyId: state.company, items: [row()] })))
    const view = mount(); fireEvent.click(await screen.findByRole('button', { name: T.choose }))
    await screen.findByText(T.own); expect(screen.queryByRole('button', { name: T.approve })).toBeNull()
    view.unmount(); state.actor = 'e0431000-0000-4000-8000-000000000002'
    mount(false); fireEvent.click(await screen.findByRole('button', { name: T.choose }))
    expect(screen.queryByRole('button', { name: T.approve })).toBeNull()
  })
  it('freezes an unknown approval across remount and resends the identical command instead of creating another journal', async () => {
    const writes: RequestInit[] = []
    vi.stubGlobal('fetch', vi.fn(async (_url: string, init?: RequestInit) => {
      if (init?.method === 'POST') { writes.push(init); return writes.length === 1 ? json({}, 503) : json(row({ status: 'Posted',
        version: 3, approvedBy: state.actor, approvedAtUtc: '2026-11-17T09:02:00Z', journalId: source })) }
      return json({ companyId: state.company, items: [row()] })
    }))
    const view = mount(); fireEvent.click(await screen.findByRole('button', { name: T.choose }))
    fireEvent.click(await screen.findByRole('button', { name: T.approve })); await screen.findByText(T.unknown)
    expect((screen.getByRole('button', { name: T.refresh }) as HTMLButtonElement).disabled).toBe(true)
    view.unmount(); mount(); fireEvent.click(await screen.findByRole('button', { name: T.resend }))
    await waitFor(() => expect(writes).toHaveLength(2)); await screen.findByText(T.done)
    expect(writes[0].headers).toEqual(writes[1].headers); expect(writes[0].body).toBe(writes[1].body)
  })
})
