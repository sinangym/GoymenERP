import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { CreditAccountsPage } from './CreditAccountsPage'
import { CREDIT_PLAN_HEADER, decodeCreditAccount, parseCreditPlanFile, sendCreditAccount, type CreditGroup } from './creditAccountApi'
import { CreditAccountProposalForm } from './CreditAccountProposalForm'
import { CREDIT_TEXT as T } from './creditAccountText'

const Company = 'cd471111-1111-4111-8111-111111111111'
const Id = 'cd472222-2222-4222-8222-222222222222'
const Currency = 'cd473333-3333-4333-8333-333333333333'
const Party = 'cd474444-4444-4444-8444-444444444444'
const Account = 'cd475555-5555-4555-8555-555555555555'
const fixture = (patch: Record<string, unknown> = {}) => ({ id: Id, companyId: Company, version: '1', group: 'Cash',
  lenderName: 'SENTETIK kurum', contractReference: 'SENTETIK-47', currencyId: Currency, principalAmount: '1000.00', startDate: '2026-11-17',
  status: 'Proposed', isOwnProposal: false, proposedAtUtc: '2026-11-17T10:00:00Z', decidedAtUtc: null, rejectionReason: null,
  installments: [], planTotals: null, profile: { lenderPartyId: Party, lenderCompanyPartyAccountId: Account,
    sourceDocument: { reference: 'SENTETIK', documentDate: '2026-11-16', revision: '1' }, planNature: 'SourceMovements',
    cash: { kind: 'Overdraft', maturityDate: null } }, tolerance: { profileId: Currency, profileVersion: 1, currencyScale: 2, smallestCurrencyUnit: '0.01' }, ...patch })
const boundary = vi.hoisted(() => ({ capture: vi.fn(() => ({})), report: vi.fn() }))
const shell = vi.hoisted(() => ({ generation: 0, capabilities: ['finance.credits.workspace', 'finance.credits.approve'],
  state: { activeContext: { companyId: 'cd471111-1111-4111-8111-111111111111', scopeMode: 'single', userId: 'SENTETIK-checker' }, routeEntryGeneration: 0 } }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => boundary }))
vi.mock('../shell/shellContext', () => ({ useShellState: () => shell.state }))
vi.mock('../shell/shellState', async original => ({ ...await original<typeof import('../shell/shellState')>(),
  contextKey: () => ['SENTETIK', String(shell.generation)], contextKeyString: (value: readonly string[]) => value.join(':'),
  hasCapability: (_state: unknown, capability: string) => shell.capabilities.includes(capability),
}))
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: {
  'Content-Type': 'application/json',
  // Contract: a body replay is announced by the same headers (K3 P2-3).
  ...(typeof body === 'object' && body !== null && ((body as { isReplay?: unknown }).isReplay === true)
    ? { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': String((body as { originalCorrelationId?: unknown }).originalCorrelationId) } : {}),
} })
const mount = () => render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><CreditAccountsPage /></QueryClientProvider>)
beforeEach(() => { shell.generation++; boundary.report.mockClear() })
afterEach(() => { cleanup(); vi.unstubAllGlobals() })

describe('source backed credit account workflow', () => {
  it('transports and displays optional source VAT without filling or converting legacy plan values', async () => {
    const source = `${CREDIT_PLAN_HEADER};kdv\n1;2026-12-17;130.000;100;10;0;0;20.000\n`
    const lines = parseCreditPlanFile(source)!
    expect(lines[0]?.vat).toBe('20.000')
    expect(lines[0]?.bsmv).toBe('0')
    expect(lines[0]?.kkdf).toBe('0')
    expect(parseCreditPlanFile(source.replace('20.000', '-20'))).toBeNull()
    expect(parseCreditPlanFile(source.replace('20.000', '20,00'))).toBeNull()
    expect(parseCreditPlanFile(`${CREDIT_PLAN_HEADER}\n1;2026-12-17;130;100;10;0;0`)?.[0]).not.toHaveProperty('vat')
    const leasing = fixture({ group: 'Leasing', profile: { lenderPartyId: Party, lenderCompanyPartyAccountId: Account,
      sourceDocument: { reference: 'SENTETIK lease', documentDate: '2026-11-16', revision: '1' }, planNature: 'Installments',
      leasing: { assetReference: 'SENTETIK asset', leaseStartDate: '2026-11-17', leaseEndDate: '2027-11-17' } }, installments: lines,
      planTotals: { installment: '130.000', principal: '100', interest: '10', bsmv: '0', kkdf: '0', vat: '20.000' } })
    expect(decodeCreditAccount(leasing, Company)?.planTotals?.vat).toBe('20.000')
    expect(decodeCreditAccount({ ...leasing, installments: [{ ...lines[0], vat: 20 }] }, Company)).toBeNull()
    vi.stubGlobal('fetch', vi.fn(async () => json({ companyId: Company, items: [leasing] })))
    mount(); fireEvent.click(await screen.findByRole('button', { name: 'İncele' }))
    expect(await screen.findByRole('cell', { name: '20.000' })).toBeTruthy()
    expect(screen.getByRole('columnheader', { name: 'Taksit içindeki KDV' })).toBeTruthy()
  })

  it('keeps source amount text and rejects another company, numeric money or a mismatched group profile', () => {
    expect(decodeCreditAccount(fixture(), Company)?.principalAmount).toBe('1000.00')
    expect(decodeCreditAccount(fixture({ companyId: Id }), Company)).toBeNull()
    expect(decodeCreditAccount(fixture({ principalAmount: 1000 }), Company)).toBeNull()
    expect(decodeCreditAccount(fixture({ group: 'Leasing' }), Company)).toBeNull()
    expect(decodeCreditAccount(fixture({ status: 'Approved', decidedAtUtc: null }), Company)).toBeNull()
  })
  it('parses bank plan transport without calculating totals, rounding or filling absent values', () => {
    const plan = parseCreditPlanFile(`${CREDIT_PLAN_HEADER}\n1;2026-12-17;0105.001;100;3;1;1\n`)
    expect(plan?.[0].installment).toBe('0105.001')
    expect(plan?.[0].principal).toBe('100')
    expect(parseCreditPlanFile(`${CREDIT_PLAN_HEADER}\n1;2026-12-17;105;100;3;;1`)).toBeNull()
    expect(parseCreditPlanFile(`${CREDIT_PLAN_HEADER}\n1;2026-12-17;1e3;100;3;1;1`)).toBeNull()
    expect(parseCreditPlanFile(`${CREDIT_PLAN_HEADER}\n2;2026-12-17;105;100;3;1;1`)).toBeNull()
  })
  it('never offers self approval and discloses no approver identity', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json({ companyId: Company, items: [fixture({ isOwnProposal: true })] })))
    mount(); fireEvent.click(await screen.findByRole('button', { name: 'İncele' }))
    expect(screen.getByRole('table', { name: 'Kredi hesapları' })).toBeTruthy()
    expect(screen.getByText('Kendi önerinizi onaylayamaz veya reddedemezsiniz.')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Onayla' })).toBeNull()
  })
  it('retains an unknown command across remounts and retries the exact same key and body', async () => {
    const commands: RequestInit[] = []; let approved = false
    vi.stubGlobal('fetch', vi.fn(async (_url: string, init: RequestInit = {}) => {
      if (init.method !== 'POST') return json({ companyId: Company, items: [fixture()] })
      commands.push(init)
      if (!approved) return json({}, 503)
      return json({ outcome: 'Succeeded', resource: fixture({ status: 'Approved', version: '2', decidedAtUtc: '2026-11-17T11:00:00Z' }),
        isReplay: true, originalCorrelationId: 'SENTETIK', issues: [] })
    }))
    const first = mount(); fireEvent.click(await screen.findByRole('button', { name: 'İncele' })); fireEvent.click(screen.getByRole('button', { name: 'Onayla' }))
    await screen.findByText('Komutun sonucu doğrulanamadı. Form kilitlendi; aynı anahtarla yeniden sorgulayın.')
    first.unmount(); approved = true; mount()
    fireEvent.click(await screen.findByRole('button', { name: 'Aynı isteği yeniden gönder' }))
    await waitFor(() => expect(commands).toHaveLength(2))
    expect(commands[0].body).toEqual(commands[1].body)
    expect((commands[0].headers as Record<string, string>)['Idempotency-Key']).toBe((commands[1].headers as Record<string, string>)['Idempotency-Key'])
    await screen.findByText('İşlem kaydedildi.')
  })
  it('refuses malformed success without creating a new key or claiming refusal', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json({ outcome: 'Succeeded', resource: fixture({ companyId: Id }) })))
    expect(await sendCreditAccount(Company, { operation: 'approve', key: 'same-key', id: Id, request: { expectedVersion: '1' } })).toEqual({ type: 'unknown' })
  })
})

async function proposal(group: CreditGroup) {
  vi.stubGlobal('fetch', vi.fn(async (url: RequestInfo | URL) => String(url).endsWith('/currency-candidates')
    ? json({ companyId: Company, items: [{ currencyId: Currency, code: 'TRY', displayLabel: 'SENTETIK TL' }] })
    : json({ items: [{ id: Party, code: 'SENTETIK', kind: 1, legalName: 'SENTETIK lender', tradeName: null, isActive: true, version: 1,
      companyAccounts: [{ id: Account, companyId: Company, roles: 1, isActive: true, version: 1 }] }], nextCursor: null, hasMore: false, totalCount: null, countScope: null })))
  const send = vi.fn()
  render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
    <CreditAccountProposalForm company={Company} namespace={`SENTETIK-fields-${shell.generation}`} frozen={false} send={send} />
  </QueryClientProvider>)
  fireEvent.change(screen.getByLabelText(T.group), { target: { value: group } })
  fireEvent.change(await screen.findByLabelText(T.lender), { target: { value: Party } })
  await screen.findByRole('option', { name: 'TRY · SENTETIK TL' })
  const fill = (label: string, value: string) => fireEvent.change(screen.getByLabelText(label), { target: { value } })
  fill(T.sourceName, 'SENTETIK lender'); fill(T.contract, 'SENTETIK-contract'); fill(T.currency, Currency)
  fill(T.principal, '1000'); fill(T.start, '2026-11-17'); fill(T.source, 'SENTETIK-source'); fill(T.documentDate, '2026-11-17'); fill(T.revision, '1')
  return { send, fill }
}
function sourcePlan() {
  fireEvent.click(screen.getByRole('button', { name: T.add }))
  const line = within(screen.getByRole('group', { name: `${T.columns[0]} 1` }))
  for (const [label, value] of [[T.columns[1], '2026-12-17'], [T.columns[2], '1000.00000'], [T.columns[3], '1000'],
    [T.columns[4], '0'], [T.columns[5], '0'], [T.columns[6], '0']]) fireEvent.change(line.getByLabelText(label), { target: { value } })
}
describe('approved group profile form fields', () => {
  it('shows documented noncash evidence to the existing authorized reviewer without treating it as a payment or release', async () => {
    const profile = { ...fixture().profile, cash: undefined, planNature: 'SourceMovements', nonCash: {
      kind: 'Guarantee', beneficiaryReference: 'SENTETIK-beneficiary', isIndefinite: true, expiryDate: null, riskAmount: '12000.00001',
      commission: { amount: '12.34567', commissionDate: '2026-11-17', period: 'SENTETIK-period', sourceDocument: { reference: 'SENTETIK-commission', documentDate: '2026-11-16', revision: 'R1' } },
      returnEvidence: { returnedOn: '2026-11-18', sourceDocument: { reference: 'SENTETIK-return', documentDate: '2026-11-18', revision: 'R2' } }
    } }
    vi.stubGlobal('fetch', vi.fn(async () => json({ companyId: Company, items: [fixture({ group: 'NonCash', profile })] })))
    mount(); fireEvent.click(await screen.findByRole('button', { name: T.select }))
    expect(screen.getByText('12000.00001')).toBeTruthy(); expect(screen.getByText('12.34567')).toBeTruthy()
    expect(screen.getByText('SENTETIK-commission · R1')).toBeTruthy(); expect(screen.getByText('SENTETIK-return · R2')).toBeTruthy()
    expect(screen.getByRole('button', { name: T.approve }).hasAttribute('disabled')).toBe(false)
  })
  it.each(['Cash', 'NonCash', 'InstrumentAndFactoring', 'Leasing'] as const)('keeps legacy %s records visible but explains missing required source fields before a new approval', async group => {
    const base = fixture().profile
    const profile = { lenderPartyId: Party, lenderCompanyPartyAccountId: Account, sourceDocument: base.sourceDocument, planNature: 'SourceMovements',
      ...(group === 'Cash' ? { cash: { kind: 'ForeignCurrencyIndexed', maturityDate: null } } : {}),
      ...(group === 'NonCash' ? { nonCash: { kind: 'Guarantee', beneficiaryReference: 'SENTETIK', isIndefinite: true, expiryDate: null } } : {}),
      ...(group === 'InstrumentAndFactoring' ? { instrumentAndFactoring: { kind: 'Factoring', withRecourse: true, instrumentIds: [], receivableDocumentReferences: ['SENTETIK'] } } : {}),
      ...(group === 'Leasing' ? { leasing: { assetReference: 'SENTETIK', leaseStartDate: '2026-11-17', leaseEndDate: '2027-11-17' } } : {}) }
    vi.stubGlobal('fetch', vi.fn(async () => json({ companyId: Company, items: [fixture({ group, profile })] })))
    mount(); fireEvent.click(await screen.findByRole('button', { name: T.select }))
    expect(screen.getByText(T.legacy)).toBeTruthy()
    expect(screen.getByRole('button', { name: T.approve }).hasAttribute('disabled')).toBe(true)
    expect(screen.getByLabelText(T.reason)).toBeTruthy()
  })
  it.each(['Spot', 'Installment', 'ForeignCurrencyIndexed', 'Export', 'Revolving', 'Overdraft', 'CreditCard'])('uses the approved %s plan nature without a conflicting choice or a synthetic installment', async kind => {
    const { send, fill } = await proposal('Cash')
    fill(T.kind, kind)
    expect(screen.queryByLabelText(T.plan)).toBeNull()
    const movements = ['Revolving', 'Overdraft', 'CreditCard'].includes(kind)
    if (['Spot', 'Installment'].includes(kind)) {
      expect(screen.getByRole('button', { name: T.propose }).hasAttribute('disabled')).toBe(true)
      fill(T.maturity, '2026-12-17')
    }
    if (kind === 'ForeignCurrencyIndexed') fill(T.indexCurrency, Currency)
    if (kind === 'Export') fill(T.exportReference, 'SENTETIK-export')
    if (!movements) sourcePlan()
    fireEvent.click(screen.getByRole('button', { name: T.propose }))
    expect(send).toHaveBeenCalledTimes(1)
    const request = send.mock.calls[0][0].request
    expect(request.profile.planNature).toBe(movements ? 'SourceMovements' : 'Installments')
    expect(request.installments).toHaveLength(movements ? 0 : 1)
    if (!movements) expect(request.installments[0].installment).toBe('1000.00000')
    expect(request.profile.cash).toEqual({ kind, maturityDate: ['Spot', 'Installment'].includes(kind) ? '2026-12-17' : null,
      ...(kind === 'ForeignCurrencyIndexed' ? { indexCurrencyId: Currency } : {}), ...(kind === 'Export' ? { exportCommitmentReference: 'SENTETIK-export' } : {}) })
  })
  it('requires documented noncash risk and optional commission/return evidence without making a bank event', async () => {
    const { send, fill } = await proposal('NonCash')
    fill(T.kind, 'Guarantee'); fill(T.beneficiary, 'SENTETIK-beneficiary'); fireEvent.click(screen.getByLabelText(T.indefinite))
    expect(screen.getByRole('button', { name: T.propose }).hasAttribute('disabled')).toBe(true)
    fill(T.riskAmount, '12000.00001'); fill(T.commercialReference, 'SENTETIK-order')
    fireEvent.click(screen.getByLabelText(T.hasCommission)); fireEvent.click(screen.getByLabelText(T.hasReturnEvidence))
    expect(screen.getByRole('button', { name: T.propose }).hasAttribute('disabled')).toBe(true)
    fill(T.commissionAmount, '12.34567'); fill(T.commissionDate, '2026-11-17'); fill(T.commissionPeriod, 'SENTETIK-period')
    fill(T.commissionReference, 'SENTETIK-commission'); fill(T.commissionDocumentDate, '2026-11-16'); fill(T.commissionRevision, 'R1')
    fill(T.returnedOn, '2026-11-18'); fill(T.returnReference, 'SENTETIK-return'); fill(T.returnDocumentDate, '2026-11-18'); fill(T.returnRevision, 'R2')
    fireEvent.click(screen.getByRole('button', { name: T.propose }))
    expect(send).toHaveBeenCalledTimes(1)
    expect(send.mock.calls[0][0].request.profile.nonCash).toEqual({ kind: 'Guarantee', beneficiaryReference: 'SENTETIK-beneficiary', isIndefinite: true,
      expiryDate: null, riskAmount: '12000.00001', relatedCommercialDocumentReference: 'SENTETIK-order',
      commission: { amount: '12.34567', commissionDate: '2026-11-17', period: 'SENTETIK-period', sourceDocument: { reference: 'SENTETIK-commission', documentDate: '2026-11-16', revision: 'R1' } },
      returnEvidence: { returnedOn: '2026-11-18', sourceDocument: { reference: 'SENTETIK-return', documentDate: '2026-11-18', revision: 'R2' } } })
    expect(send.mock.calls[0][0].request.installments).toEqual([])
    expect(send.mock.calls[0][0].request).not.toHaveProperty('bankEffect')
  })
  it('requires source factoring deductions and maturity without inventing withheld amounts or a repayment plan', async () => {
    const { send, fill } = await proposal('InstrumentAndFactoring')
    fill(T.kind, 'Factoring'); fill(T.recourse, 'yes'); fill(T.receivables, 'SENTETIK-invoice'); fill(T.plan, 'SourceMovements')
    expect(screen.getByRole('button', { name: T.propose }).hasAttribute('disabled')).toBe(true)
    fill(T.maturity, '2026-12-17'); fill(T.deductionInterest, '1.23456'); fill(T.deductionCommission, '0'); fill(T.deductionBsmv, '0.00001')
    fireEvent.click(screen.getByRole('button', { name: T.propose }))
    expect(send).toHaveBeenCalledTimes(1)
    expect(send.mock.calls[0][0].request.profile.instrumentAndFactoring).toEqual({ kind: 'Factoring', withRecourse: true, instrumentIds: [],
      receivableDocumentReferences: ['SENTETIK-invoice'], maturityDate: '2026-12-17', deductions: { interest: '1.23456', commission: '0', bsmv: '0.00001' } })
    expect(send.mock.calls[0][0].request.installments).toEqual([])
  })
  it('requires a leasing contract date and imported plan while keeping an absent purchase option omitted', async () => {
    const { send, fill } = await proposal('Leasing')
    fill(T.asset, 'SENTETIK-asset'); fill(T.leaseStart, '2026-11-17'); fill(T.leaseEnd, '2027-11-17')
    sourcePlan()
    expect(screen.getByRole('button', { name: T.propose }).hasAttribute('disabled')).toBe(true)
    fill(T.contractDate, '2026-11-16')
    fireEvent.click(screen.getByRole('button', { name: T.propose }))
    expect(send).toHaveBeenCalledTimes(1)
    expect(send.mock.calls[0][0].request.profile.leasing).toEqual({ assetReference: 'SENTETIK-asset', leaseStartDate: '2026-11-17', leaseEndDate: '2027-11-17', contractDate: '2026-11-16' })
    expect(send.mock.calls[0][0].request.profile.planNature).toBe('Installments')
    expect(send.mock.calls[0][0].request.installments[0]).not.toHaveProperty('vat')
  })
})
