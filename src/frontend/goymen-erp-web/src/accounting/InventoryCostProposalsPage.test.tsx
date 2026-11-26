import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router'
import { InventoryCostProposalsPage } from './InventoryCostProposalsPage'
import { INVENTORY_COST_TEXT as T } from './inventoryCostText'
import { reversalProposal } from './testHarness'
import { decodeInventoryCost } from './inventoryCostApi'

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
    signedTlAmount: { numerator: '600', denominator: '1' }, sourceProofJson: '{}', sourceProofSha256: 'a'.repeat(64) },
  version: 2, status: 'Prepared', sourceOccurredAtUtc: '2026-11-17T09:00:00Z', exactTlAmount: '600',
  preparation: { periodId: source, accountingDate: '2026-11-17', transactionDate: '2026-11-17', currencyId: source,
    signedAmount: '600', moneyProfileId: source, moneyProfileVersion: 1, moneyScale: 2,
    classification: { classification: 1, accountId: cogs, origin: 'Product', productVersion: '1', classificationVersion: '1', mappingVersion: 1, invoiceLineRevision: 0 },
    counterAccount: { accountId: counter, code: '153', name: 'SENTETIK karşı hesap' },
    accountPlanPackageId: source, accountPlanPackageVersion: 1, accountPlanCatalogVersion: 1, accountPlanSourceVersion: 'SENTETIK-plan',
    accountPlanSha256: 'b'.repeat(64), preparedBy: maker, preparedAtUtc: '2026-11-17T09:01:00Z', reason: 'SENTETIK gerçek kaynak' },
  approvedBy: null, approvedAtUtc: null, journalId: null, cancellationId: null, reversalProposal: null, ...patch })
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
const mount = (canApprove = true, canPrepare = false, canProposeReversal = false, canApproveCancellation = false, path = '/') => {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
  const content = (approve: boolean, prepare: boolean, reverse: boolean) => <MemoryRouter initialEntries={[path]}><QueryClientProvider client={client}>
    <InventoryCostProposalsPage kind={1} canPrepare={prepare} canApprove={approve} canProposeReversal={reverse} canApproveCancellation={canApproveCancellation} /></QueryClientProvider></MemoryRouter>
  const view = render(content(canApprove, canPrepare, canProposeReversal))
  return { ...view, updateAuthority: (approve: boolean, prepare = false, reverse = false) => view.rerender(content(approve, prepare, reverse)) }
}
afterEach(() => { cleanup(); vi.unstubAllGlobals(); state.actor = 'e0431000-0000-4000-8000-000000000002'
  state.company = 'e0431000-0000-4000-8000-000000000001'; state.capabilityGeneration = 'SENTETIK-cap-1'
  state.committedContextRevision = 1; state.sessionEpoch = 0 })

describe('inventory cost human approval flow', () => {
  const stale = () => ({ ...row({ status: 'Stale', version: 3 }), staleClosure: { eventId: counter, preparedVersion: 2, preparedBy: maker,
    closedAtUtc: '2026-11-17T09:02:00Z', reasonCode: 'inventory_cost_preparation_stale', reasonText: 'Bayatladı', correlationId: 'SENTETIK-D40', causationId: null } })
  it('opens a linked immutable stale proposal without offering a fresh command or reading options', async () => {
    const fetch = vi.fn(async () => json({ companyId: state.company, items: [stale()] })); vi.stubGlobal('fetch', fetch)
    mount(true, true, false, false, `/?proposalId=${source}`)
    expect(await screen.findByText(T.staleClosed)).toBeTruthy()
    expect(screen.getByRole('heading', { level: 2, name: /Bayatladı/ })).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.prepare })).toBeNull()
    expect(screen.queryByRole('button', { name: T.approve })).toBeNull()
    expect(fetch.mock.calls).toHaveLength(1)
  })
  it('validates immutable stale closure and renewal lineage without accepting evidence on legacy statuses', () => {
    const value = stale()
    expect(decodeInventoryCost(value, state.company, 1)).not.toBeNull()
    for (const delta of [{ preparedVersion: 1 }, { preparedVersion: 3 }, { preparedBy: state.actor }, { eventId: '' },
      { reasonCode: 'invented' }, { reasonText: 'SENTETIK other' }, { closedAtUtc: 'not-a-date' },
      { closedAtUtc: '2026-11-17T09:02:00+03:00' }, { correlationId: '' }, { extra: true }])
      expect(decodeInventoryCost({ ...value, staleClosure: { ...(value.staleClosure as object), ...delta } }, state.company, 1)).toBeNull()
    for (const patch of [{ staleClosure: null }, { staleClosure: undefined }, { preparation: null }, { cancellationId: counter }])
      expect(decodeInventoryCost({ ...value, ...patch }, state.company, 1)).toBeNull()
    expect(decodeInventoryCost(row({ staleClosure: value.staleClosure }), state.company, 1)).toBeNull()
    const renewal = { previousProposalId: counter, previousClosedVersion: 3, closureEventId: maker }
    expect(decodeInventoryCost(row({ version: 4, renewedFrom: renewal }), state.company, 1)).not.toBeNull()
    for (const delta of [{ previousProposalId: source }, { previousClosedVersion: 4 }, { previousClosedVersion: 2 }, { closureEventId: null }, { extra: true }])
      expect(decodeInventoryCost(row({ version: 4, renewedFrom: { ...renewal, ...delta } }), state.company, 1)).toBeNull()
  })
  it('initially selects a linked proposal only from the existing authorized company list', async () => {
    const fetch = vi.fn(async () => json({ companyId: state.company, items: [row()] })); vi.stubGlobal('fetch', fetch)
    mount(true, false, false, false, `/?proposalId=${source}`)
    expect(await screen.findByRole('heading', { level: 2, name: /SENTETIK-F1/ })).toBeTruthy()
    expect(fetch.mock.calls).toHaveLength(1)
  })
  it.each([source, 'bad-id', `${source}&proposalId=${source}`])('shows an explicit missing linked proposal for %s without a separate record read', async id => {
    const fetch = vi.fn(async () => json({ companyId: state.company, items: [] })); vi.stubGlobal('fetch', fetch)
    mount(true, false, false, false, `/?proposalId=${id}`)
    expect(await screen.findByText(T.linkedProposalMissing)).toBeTruthy()
    expect(fetch.mock.calls).toHaveLength(1)
    expect(screen.queryByRole('heading', { level: 2 })).toBeNull()
  })
  it('refuses a linked proposal returned under another company', async () => {
    const foreign = 'e0431000-0000-4000-8000-000000000099'
    vi.stubGlobal('fetch', vi.fn(async () => json({ companyId: foreign, items: [row({ companyId: foreign })] })))
    mount(true, false, false, false, `/?proposalId=${source}`)
    await screen.findByText(`Beklenmeyen yanıt — ${T.subject}`)
    expect(screen.queryByRole('heading', { level: 2 })).toBeNull()
    expect(screen.queryByRole('button', { name: T.approve })).toBeNull()
  })
  it('lets a different reversal approver confirm actual cost shares without creating an initial SMM proposal', async () => {
    const initial = row(); const journal = 'e0431000-0000-4000-8000-000000000005'
    const componentCancellation = { id: source, companyId: state.company, inventoryCostProposalId: source, cancellationId: source,
      version: 1, status: 'Prepared', invoiceLineRevision: 0, periodId: source, accountingDate: '2026-11-18', transactionDate: '2026-11-18',
      reason: 'SENTETIK pay iptali', preparedBy: maker, preparedAtUtc: '2026-11-18T09:00:00Z', approvedBy: null, approvedAtUtc: null,
      moneyProfiles: [{ currencyId: source, profileId: source, version: 1, scale: 2 }], components: [{ economicKey: 'c'.repeat(64), origin: 'Known',
        sourceJournalId: journal, journalSourceId: source, postingPurpose: `invoice-line-cogs:${source}:2`, currencyId: source,
        journalTotal: '600', signedAmount: '600', cogsAccountId: cogs, counterAccountId: counter, sourceFingerprint: 'a'.repeat(64) }] }
    const value = { ...initial, status: 'ComponentCancellationPrepared', version: 3, approvedBy: state.actor,
      approvedAtUtc: '2026-11-17T09:02:00Z', journalId: journal, cancellationId: source, componentCancellation }
    let sent: RequestInit | undefined
    vi.stubGlobal('fetch', vi.fn(async (_url: RequestInfo | URL, init?: RequestInit) => {
      if (init?.method === 'POST') { sent = init; return json({ ...value, status: 'CancelledByComponentReversal', componentCancellation: {
        ...componentCancellation, version: 2, status: 'Approved', approvedBy: state.actor, approvedAtUtc: '2026-11-18T09:05:00Z',
        components: [{ ...componentCancellation.components[0], reversingJournalId: source }] } }) }
      return json({ companyId: state.company, items: [value] })
    }))
    mount(false, false, false, true); fireEvent.click(await screen.findByRole('button', { name: T.choose }))
    expect(screen.getByRole('table', { name: T.salesTitle })).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.approve })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: T.componentApprove })); await screen.findByText(T.done)
    expect(JSON.parse(String(sent?.body))).toEqual({ expectedVersion: 3, expectedCancellationVersion: 1 })
  })
  it('shows the source exact amount and signed residue separately from its one rounded posting amount', async () => {
    const base = row(); const rounding = { exactSignedAmount: { numerator: '1', denominator: '3' }, roundedSignedAmount: '0.33',
      exactSignedResidue: { numerator: '1', denominator: '300' }, exactSignedAmountText: '1 / 3', exactSignedResidueText: '1 / 300',
      ruleVersion: 'accounting.inventory-cost-component-total.v1' }
    vi.stubGlobal('fetch', vi.fn(async () => json({ companyId: state.company, items: [row({ exactTlAmount: '1 / 3',
      component: { ...base.component, signedTlAmount: rounding.exactSignedAmount }, preparation: { ...base.preparation, signedAmount: '0.33', rounding } })] })))
    mount(); fireEvent.click(await screen.findByRole('button', { name: T.choose }))
    await screen.findByText('0.33'); expect(screen.getByText('1 / 300')).toBeDefined()
    expect(screen.getByRole('button', { name: T.approve })).toBeDefined()
  })
  it('accepts a different approver zero outcome without linking or fabricating a journal', async () => {
    const base = row(); const rounding = { exactSignedAmount: { numerator: '1', denominator: '250' }, roundedSignedAmount: '0',
      exactSignedResidue: { numerator: '1', denominator: '250' }, exactSignedAmountText: '0.004', exactSignedResidueText: '0.004',
      ruleVersion: 'accounting.inventory-cost-component-total.v1' }
    const prepared = row({ exactTlAmount: '0.004', component: { ...base.component, signedTlAmount: rounding.exactSignedAmount },
      preparation: { ...base.preparation, signedAmount: '0', rounding } })
    const approved = { ...prepared, status: 'RoundedNoCost', version: 3, approvedBy: state.actor, approvedAtUtc: '2026-11-17T09:02:00Z' }
    let writes = 0
    vi.stubGlobal('fetch', vi.fn(async (_url: string, init?: RequestInit) => {
      if (init?.method === 'POST') { writes++; return json(approved) }
      return json({ companyId: state.company, items: [writes ? approved : prepared] })
    }))
    mount(); fireEvent.click(await screen.findByRole('button', { name: T.choose }))
    fireEvent.click(await screen.findByRole('button', { name: T.approve }))
    await screen.findByText(T.done); await screen.findByText(T.roundedZero)
    expect(writes).toBe(1); expect(screen.queryByText(T.unknown)).toBeNull()
    expect(screen.queryByRole('button', { name: T.approve })).toBeNull()
    expect(screen.queryByRole('link', { name: T.reversalApproval })).toBeNull()
  })
  it('rejects rounding proof that points to another exact source instead of allowing contradictory prepared totals', async () => {
    const base = row()
    vi.stubGlobal('fetch', vi.fn(async () => json({ companyId: state.company, items: [row({ preparation: { ...base.preparation,
      rounding: { exactSignedAmount: { numerator: '1', denominator: '3' }, roundedSignedAmount: '600',
        exactSignedResidue: { numerator: '1', denominator: '300' }, exactSignedAmountText: '1 / 3', exactSignedResidueText: '1 / 300',
        ruleVersion: 'accounting.inventory-cost-component-total.v1' } } })] })))
    mount(); await screen.findByText(`Beklenmeyen yanıt — ${T.subject}`)
    expect(screen.queryByRole('button', { name: T.approve })).toBeNull()
  })
  it('keeps source rounding and the current journal profile separate while refusing currency or evidence replacement', () => {
    const base = row(); const rounding = { exactSignedAmount: { numerator: '1', denominator: '250' }, roundedSignedAmount: '0',
      exactSignedResidue: { numerator: '1', denominator: '250' }, exactSignedAmountText: '0.004', exactSignedResidueText: '0.004',
      ruleVersion: 'accounting.inventory-cost-component-total.v1' }
    const first = row({ kind: 2, exactTlAmount: '0', component: { ...base.component, componentKey: `purchase-first/${source}/${counter}`,
      invoiceLineId: null, invoiceLineNumber: null, locationId: counter, signedTlAmount: { numerator: '0', denominator: '1' },
      recordedRounding: { currencyId: source, moneyProfileId: source, moneyProfileVersion: 1, moneyScale: 2, rounding } },
      preparation: { ...base.preparation, signedAmount: '0', rounding } })
    expect(decodeInventoryCost(first, state.company, 2)).not.toBeNull()
    const aggregate = { ...first, component: { ...first.component, componentKey: `purchase-first-line/${counter}`, locationId: null } }
    expect(decodeInventoryCost(aggregate, state.company, 2)).not.toBeNull()
    expect(decodeInventoryCost({ ...aggregate, component: { ...aggregate.component, locationId: counter } }, state.company, 2)).toBeNull()
    expect(decodeInventoryCost({ ...aggregate, component: { ...aggregate.component, componentKey: `purchase-first-line/${counter.toUpperCase()}` } }, state.company, 2)).toBeNull()
    expect(decodeInventoryCost({ ...first, preparation: { ...base.preparation, signedAmount: '0', rounding,
      moneyProfileId: counter, moneyProfileVersion: 2, moneyScale: 3 } }, state.company, 2)).not.toBeNull()
    expect(decodeInventoryCost({ ...first, preparation: { ...base.preparation, signedAmount: '0', rounding, currencyId: counter } }, state.company, 2)).toBeNull()
    expect(decodeInventoryCost({ ...first, preparation: { ...base.preparation, signedAmount: '0',
      rounding: { ...rounding, exactSignedResidue: { numerator: '0', denominator: '1' }, exactSignedResidueText: '0' } } }, state.company, 2)).toBeNull()
    expect(decodeInventoryCost({ ...first, status: 'NoCost', preparation: null, version: 1 }, state.company, 2)).toBeNull()
    expect(decodeInventoryCost({ ...first, status: 'RoundedNoCost', version: 3, approvedBy: state.actor,
      approvedAtUtc: '2026-11-17T09:02:00Z' }, state.company, 2)).not.toBeNull()
    expect(decodeInventoryCost({ ...first, status: 'RoundedNoCost', version: 3, approvedBy: maker,
      approvedAtUtc: '2026-11-17T09:02:00Z' }, state.company, 2)).toBeNull()
  })
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
