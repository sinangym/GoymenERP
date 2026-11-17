import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { decodeCreditMovement, sendCreditMovement, type CreditMovement, type CreditMovementInput } from './creditMovementApi'
import { CreditMovementsPanel } from './CreditMovementsPanel'
import type { CreditAccount } from './creditAccountApi'
import { decodeCreditPostingProposals } from './CreditAccountingProposalsPanel'

const Company = 'ce691111-1111-4111-8111-111111111111'
const Account = 'ce692222-2222-4222-8222-222222222222'
const Movement = 'ce693333-3333-4333-8333-333333333333'
const Currency = 'ce694444-4444-4444-8444-444444444444'
const input = (): CreditMovementInput => ({ creditAccountId: Account, expectedCreditVersion: '2', kind: 'Drawdown',
  sourceDocument: { reference: 'SENTETIK actual source', documentDate: '2026-11-17', revision: '1' }, transactionDate: '2026-11-17', accountingDate: '2026-11-17',
  components: [{ kind: 'principal', amount: '100.00', functionalAmount: null }], bankEffect: null, replacesOpeningMovementId: null, expectedOpeningMovementVersion: null, reason: 'SENTETIK source' })
const row = (): CreditMovement => ({ id: Movement, companyId: Company, version: '1', status: 'Proposed', source: input(), currencyId: Currency,
  isOwnProposal: false, proposedAtUtc: '2026-11-17T09:00:00Z', decidedAtUtc: null, rejectionReason: null, reversedByMovementId: null, openingPeriodVersion: null,
  moneyProfile: { profileId: Currency, profileVersion: 1, currencyScale: 2, smallestCurrencyUnit: '0.01' } })
const account = { id: Account, companyId: Company, version: '2', status: 'Approved', currencyId: Currency, group: 'Cash' } as CreditAccount
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } })
const boundary = vi.hoisted(() => ({ capture: () => ({}), report: vi.fn() }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => boundary }))
vi.mock('../shell/shellContext', () => ({ useShellState: () => ({ routeEntryGeneration: 0 }) }))
vi.mock('../shell/shellState', () => ({ hasCapability: (_: unknown, name: string) => name === 'finance.credits.approve' }))
afterEach(() => { cleanup(); vi.unstubAllGlobals() })
describe('actual credit movements', () => {
  it('does not present an unreadable accounting proposal as an empty or posted result', () => {
    expect(decodeCreditPostingProposals({ companyId: Company, items: [] }, Company)).toEqual([])
    expect(decodeCreditPostingProposals({ companyId: Account, items: [] }, Company)).toBeNull()
    expect(decodeCreditPostingProposals({ companyId: Company, items: [{ proposalId: Movement, status: 'Posted' }] }, Company)).toBeNull()
  })
  it('keeps an accepted source as waiting and rejects missing bank provenance or duplicate source components', () => {
    const source = { eventId: Movement, companyId: Company, movementId: Movement, creditAccountId: Account, currencyId: Currency,
      kind: 'Drawdown', sourceDocument: input().sourceDocument, transactionDate: '2026-11-17', accountingDate: '2026-11-17',
      components: input().components, bankEffect: null, reversesMovementId: null, approvedBy: 'SENTETIK checker', reason: 'SENTETIK source' }
    const proposal = { proposalId: Movement, companyId: Company, status: 'AwaitingMapping', mappingStatus: 'MappingMissing', mappingVersion: null,
      waitingReasons: ['credit_mapping_details_pending_TK30', 'credit_mapping_missing', 'credit_functional_source_amount_missing'], receivedAtUtc: '2026-11-17T09:00:00Z', source }
    expect(decodeCreditPostingProposals({ companyId: Company, items: [proposal] }, Company)?.[0]?.source.components[0]?.functionalAmount).toBeNull()
    expect(decodeCreditPostingProposals({ companyId: Company, items: [{ ...proposal, source: { ...source, bankEffect: {} } }] }, Company)).toBeNull()
    expect(decodeCreditPostingProposals({ companyId: Company, items: [{ ...proposal, source: { ...source, components: [...source.components, ...source.components] } }] }, Company)).toBeNull()
  })
  it('keeps unknown TL and source text while refusing numeric money, foreign company and wrong account', () => {
    expect(decodeCreditMovement(row(), Company, Account)?.source.components[0]?.functionalAmount).toBeNull()
    expect(decodeCreditMovement({ ...row(), companyId: Account }, Company, Account)).toBeNull()
    expect(decodeCreditMovement(row(), Company, Movement)).toBeNull()
    expect(decodeCreditMovement({ ...row(), source: { ...input(), components: [{ kind: 'principal', amount: 100, functionalAmount: null }] } }, Company, Account)).toBeNull()
  })
  it('matches a successful source response irrespective of JSON member order but never accepts changed amounts', async () => {
    const request = { ...input(), sourceDocument: { revision: '1', reference: 'SENTETIK actual source', documentDate: '2026-11-17' } }
    vi.stubGlobal('fetch', vi.fn(async () => json({ outcome: 'Succeeded', resource: row(), isReplay: false, originalCorrelationId: 'SENTETIK', issues: [] })))
    expect((await sendCreditMovement(Company, Account, { key: 'SENTETIK-key', operation: 'propose', request })).type).toBe('applied')
    expect((await sendCreditMovement(Company, Account, { key: 'SENTETIK-other', operation: 'propose', request: { ...request, components: [{ kind: 'principal', amount: '101.00', functionalAmount: null }] } })).type).toBe('unknown')
  })
  it('retains an unknown approval across remount and a later authorization refusal until the same receipt is resolved', async () => {
    const posted: RequestInit[] = []; let status = 503
    vi.stubGlobal('fetch', vi.fn(async (_: string, init: RequestInit = {}) => {
      if (init.method !== 'POST') return json({ companyId: Company, items: [row()] })
      posted.push(init)
      return status === 200 ? json({ outcome: 'Succeeded', resource: { ...row(), version: '2', status: 'Approved', decidedAtUtc: '2026-11-17T10:00:00Z' }, isReplay: true, originalCorrelationId: 'SENTETIK', issues: [] }) : json({}, status)
    }))
    const namespace = `SENTETIK:${crypto.randomUUID()}`
    const mount = () => render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><CreditMovementsPanel account={account} namespace={namespace} /></QueryClientProvider>)
    const first = mount(); fireEvent.click(await screen.findByRole('button', { name: 'Onayla' }))
    await screen.findByText('Sonuç doğrulanamadı; aynı istek ve anahtarla yeniden sorgulayın.')
    first.unmount(); status = 403; const second = mount()
    fireEvent.click(await screen.findByRole('button', { name: 'Aynı isteği yeniden gönder' }))
    await waitFor(() => { expect(posted).toHaveLength(2); expect((screen.getByRole('button', { name: 'Aynı isteği yeniden gönder' }) as HTMLButtonElement).disabled).toBe(false) })
    second.unmount(); status = 200; mount()
    fireEvent.click(await screen.findByRole('button', { name: 'Aynı isteği yeniden gönder' }))
    await screen.findByText('Hareket kaydedildi.')
    expect(posted).toHaveLength(3); expect(posted[0]?.body).toBe(posted[1]?.body); expect(posted[0]?.body).toBe(posted[2]?.body)
    expect(new Headers(posted[0]?.headers).get('Idempotency-Key')).toBe(new Headers(posted[1]?.headers).get('Idempotency-Key'))
    expect(new Headers(posted[0]?.headers).get('Idempotency-Key')).toBe(new Headers(posted[2]?.headers).get('Idempotency-Key'))
  })
})
