import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { CreditAccountsPage } from './CreditAccountsPage'
import { CREDIT_PLAN_HEADER, decodeCreditAccount, parseCreditPlanFile, sendCreditAccount } from './creditAccountApi'

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
