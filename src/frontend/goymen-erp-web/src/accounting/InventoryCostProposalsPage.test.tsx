import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router'
import { InventoryCostProposalsPage } from './InventoryCostProposalsPage'
import { INVENTORY_COST_TEXT as T } from './inventoryCostText'
import { reversalProposal } from './testHarness'

const state = vi.hoisted(() => ({ company: 'e0431000-0000-4000-8000-000000000001', actor: 'e0431000-0000-4000-8000-000000000002',
  capabilityGeneration: 'SENTETIK-cap-1', committedContextRevision: 1, sessionEpoch: 0 }))
const maker = 'e0431000-0000-4000-8000-000000000003'
const source = 'e0431000-0000-4000-8000-000000000004'
const cogs = 'e0431000-0000-4000-8000-000000000621'; const counter = 'e0431000-0000-4000-8000-000000000153'
vi.mock('../shell/shellContext', () => ({ useShellState: () => ({ phase: 'idle', routeEntryGeneration: 1,
  sessionEpoch: state.sessionEpoch, committedContextRevision: state.committedContextRevision,
  activeContext: { companyId: state.company, userId: state.actor, scopeMode: 'single', capabilityGeneration: state.capabilityGeneration } }) }))
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
const mount = (canApprove = true, canPrepare = false, canProposeReversal = false) => {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
  const content = (approve: boolean, prepare: boolean, reverse: boolean) => <MemoryRouter><QueryClientProvider client={client}>
    <InventoryCostProposalsPage kind={1} canPrepare={prepare} canApprove={approve} canProposeReversal={reverse} /></QueryClientProvider></MemoryRouter>
  const view = render(content(canApprove, canPrepare, canProposeReversal))
  return { ...view, updateAuthority: (approve: boolean, prepare = false, reverse = false) => view.rerender(content(approve, prepare, reverse)) }
}
afterEach(() => { cleanup(); vi.unstubAllGlobals(); state.actor = 'e0431000-0000-4000-8000-000000000002'
  state.company = 'e0431000-0000-4000-8000-000000000001'; state.capabilityGeneration = 'SENTETIK-cap-1'
  state.committedContextRevision = 1; state.sessionEpoch = 0 })

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
  it('allows an approver to inspect and approve without fetching preparation-only options', async () => {
    const paths: string[] = []
    vi.stubGlobal('fetch', vi.fn(async (url: string) => { paths.push(url); return json({ companyId: state.company, items: [row()] }) }))
    mount(); fireEvent.click(await screen.findByRole('button', { name: T.choose }))
    await screen.findByRole('button', { name: T.approve })
    expect(paths).toHaveLength(1); expect(paths.some(path => path.endsWith('/options'))).toBe(false)
    expect(screen.queryByRole('combobox', { name: T.counter })).toBeNull()
  })
  it('loads actual open-period options for a reversal-only user without a preparation or approval grant', async () => {
    const paths: string[] = []
    vi.stubGlobal('fetch', vi.fn(async (url: string) => {
      paths.push(url)
      return url.endsWith('/options') ? json({ companyId: state.company, accounts: [],
        openPeriods: [{ periodId: source, startsOn: '2026-11-01', endsOn: '2026-11-30' }] })
        : json({ companyId: state.company, items: [row({ status: 'CancellationReversalPending', version: 3,
          approvedBy: state.actor, approvedAtUtc: '2026-11-17T09:02:00Z', journalId: source,
          currentPostedJournalId: source, cancellationId: source })] })
    }))
    mount(false, false, true); fireEvent.click(await screen.findByRole('button', { name: T.choose }))
    await screen.findByRole('button', { name: T.reverse })
    expect(paths.filter(path => path.endsWith('/options'))).toHaveLength(1)
    expect(screen.queryByRole('button', { name: T.approve })).toBeNull()
    expect(screen.queryByRole('button', { name: T.prepare })).toBeNull()
    expect(screen.queryByRole('combobox', { name: T.counter })).toBeNull()
    expect(screen.getByRole('combobox', { name: T.period })).toBeDefined()
  })
  it('blocks an approval when the returned source display contradicts the frozen preparation', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json({ companyId: state.company, items: [row({ exactTlAmount: '999999.01' })] })))
    mount(); await screen.findByText(`Beklenmeyen yanıt — ${T.subject}`)
    expect(screen.queryByText('999999.01')).toBeNull()
    expect(screen.queryByRole('button', { name: T.approve })).toBeNull()
  })
  it('does not link an unrelated journal reversal proposal from a malformed cancellation response', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json({ companyId: state.company, items: [row({
      status: 'CancellationReversalPending', version: 3, approvedBy: state.actor, approvedAtUtc: '2026-11-17T09:02:00Z',
      journalId: source, currentPostedJournalId: counter, cancellationId: source,
      reversalProposal: reversalProposal({ companyId: state.company, sourceJournalId: cogs }) })] })))
    mount(false, false, true); await screen.findByText(`Beklenmeyen yanıt — ${T.subject}`)
    expect(screen.queryByRole('link', { name: T.reversalApproval })).toBeNull()
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
  it('keeps the same unknown command across real capability and context revisions when approval permission is lost and returns', async () => {
    state.actor = 'e0431000-0000-4000-8000-000000000011'
    const writes: RequestInit[] = []
    vi.stubGlobal('fetch', vi.fn(async (_url: string, init?: RequestInit) => {
      if (init?.method === 'POST') { writes.push(init); return writes.length === 1 ? json({}, 503) : json(row({ status: 'Posted',
        version: 3, approvedBy: state.actor, approvedAtUtc: '2026-11-17T09:02:00Z', journalId: source })) }
      return json({ companyId: state.company, items: [row()] })
    }))
    const view = mount(); fireEvent.click(await screen.findByRole('button', { name: T.choose }))
    fireEvent.click(await screen.findByRole('button', { name: T.approve })); await screen.findByText(T.unknown)
    state.capabilityGeneration = 'SENTETIK-cap-revoked'; state.committedContextRevision++
    view.updateAuthority(false)
    const blocked = screen.getByRole('button', { name: T.resend }) as HTMLButtonElement
    expect(blocked.disabled).toBe(true); fireEvent.click(blocked); expect(writes).toHaveLength(1)
    state.capabilityGeneration = 'SENTETIK-cap-restored'; state.committedContextRevision++
    view.updateAuthority(true); fireEvent.click(screen.getByRole('button', { name: T.resend }))
    await screen.findByText(T.done); expect(writes).toHaveLength(2)
    expect(writes[0].headers).toEqual(writes[1].headers); expect(writes[0].body).toBe(writes[1].body)
  })
  it.each(['actor', 'company', 'session'] as const)('does not expose another %s pending command', async changed => {
    state.actor = changed === 'actor' ? 'e0431000-0000-4000-8000-000000000018'
      : changed === 'company' ? 'e0431000-0000-4000-8000-000000000019' : 'e0431000-0000-4000-8000-000000000020'
    const originalCompany = state.company; const originalActor = state.actor
    const writes: RequestInit[] = []
    vi.stubGlobal('fetch', vi.fn(async (_url: string, init?: RequestInit) => {
      if (init?.method === 'POST') { writes.push(init); return writes.length === 1 ? json({}, 503) : json(row({ status: 'Posted',
        version: 3, approvedBy: state.actor, approvedAtUtc: '2026-11-17T09:02:00Z', journalId: source })) }
      return json({ companyId: state.company, items: [row()] })
    }))
    const view = mount(); fireEvent.click(await screen.findByRole('button', { name: T.choose }))
    fireEvent.click(await screen.findByRole('button', { name: T.approve })); await screen.findByText(T.unknown)
    if (changed === 'actor') state.actor = 'e0431000-0000-4000-8000-000000000016'
    else if (changed === 'company') state.company = 'e0431000-0000-4000-8000-000000000017'
    else state.sessionEpoch++
    state.capabilityGeneration = 'SENTETIK-cap-other'; state.committedContextRevision++
    view.updateAuthority(true); await screen.findByRole('button', { name: T.choose })
    expect(screen.queryByText(T.unknown)).toBeNull(); expect(screen.queryByRole('button', { name: T.resend })).toBeNull()
    expect(writes).toHaveLength(1)
    // A new session stays isolated. Returning to the original actor/company within the same session can
    // resume its exact attempt; an authority refresh has no right to replace the unknown command's key.
    if (changed === 'session') return
    state.actor = originalActor; state.company = originalCompany
    state.capabilityGeneration = 'SENTETIK-cap-back'; state.committedContextRevision++
    view.updateAuthority(true); fireEvent.click(await screen.findByRole('button', { name: T.resend }))
    await screen.findByText(T.done); expect(writes).toHaveLength(2)
    expect(writes[0].headers).toEqual(writes[1].headers); expect(writes[0].body).toBe(writes[1].body)
  })
  it.each(['actor', 'company'] as const)('ignores a late successful command after the %s context changes', async changed => {
    state.actor = 'e0431000-0000-4000-8000-000000000012'
    const originalCompany = state.company; const originalActor = state.actor
    const original = row({ officialInvoiceNumber: 'SENTETIK-OLD-CONTEXT' })
    let finish!: (value: Response) => void; let reads = 0; let writes = 0
    vi.stubGlobal('fetch', vi.fn(async (_url: string, init?: RequestInit) => {
      if (init?.method === 'POST') { writes++; return new Promise<Response>(resolve => { finish = resolve }) }
      reads++; return json({ companyId: state.company, items: reads === 1 ? [original] : [] })
    }))
    const view = mount(); fireEvent.click(await screen.findByRole('button', { name: T.choose }))
    fireEvent.click(await screen.findByRole('button', { name: T.approve })); await waitFor(() => expect(writes).toBe(1))
    if (changed === 'actor') state.actor = 'e0431000-0000-4000-8000-000000000013'
    else state.company = 'e0431000-0000-4000-8000-000000000014'
    view.updateAuthority(true); await screen.findByText(T.empty)
    await act(async () => finish(json({ ...original, companyId: originalCompany, status: 'Posted', version: 3,
      approvedBy: originalActor, approvedAtUtc: '2026-11-17T09:02:00Z', journalId: source })))
    expect(screen.queryByText(T.done)).toBeNull(); expect(screen.queryByText(T.unknown)).toBeNull()
    expect(screen.queryByText('SENTETIK-OLD-CONTEXT')).toBeNull(); expect(writes).toBe(1)
  })
})
