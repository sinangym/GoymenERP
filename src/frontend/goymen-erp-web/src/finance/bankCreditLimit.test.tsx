import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { CreditLimitsPanel } from './CreditLimitsPanel'
import { decodeCreditLimitWorkspace, type CreditLimit, type CreditLimitWorkspace } from './creditLimitApi'
import type { CreditAccount } from './creditAccountApi'

const Company = 'c1741111-1111-4111-8111-111111111111'
const Account = 'c1742222-2222-4222-8222-222222222222'
const Limit = 'c1743333-3333-4333-8333-333333333333'
const Currency = 'c1744444-4444-4444-8444-444444444444'
const account = { id: Account, companyId: Company, version: '2', status: 'Approved', currencyId: Currency, group: 'Cash',
  profile: { lenderPartyId: Account, lenderCompanyPartyAccountId: Company } } as CreditAccount
const row = (): CreditLimit => ({ id: Limit, companyId: Company, lenderPartyId: Account, lenderCompanyPartyAccountId: Company, currencyId: Currency, group: 'Cash', version: '1', status: 'Proposed',
  source: { creditAccountId: Account, expectedCreditVersion: '2', expectedCurrentLimitId: null, expectedCurrentLimitVersion: null, amount: '50.000', sourceDocument: { reference: 'SENTETIK', documentDate: '2026-11-17', revision: '1' }, reason: 'SENTETIK' },
  isOwnProposal: false, proposedAtUtc: '2026-11-17T10:00:00Z', decidedAtUtc: null, rejectionReason: null, supersededById: null })
const workspace = (): CreditLimitWorkspace => ({ companyId: Company, creditAccountId: Account, items: [row()], assessment: { recordedPrincipalNet: '-7.125', exceedsLimit: null, proposedMovements: [] } })
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: {
  'Content-Type': 'application/json',
  // Contract: a body replay is announced by the same headers (K3 P2-3).
  ...(typeof body === 'object' && body !== null && ((body as { isReplay?: unknown }).isReplay === true)
    ? { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': String((body as { originalCorrelationId?: unknown }).originalCorrelationId) } : {}),
} })
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => ({ capture: () => ({}), report: vi.fn() }) }))
vi.mock('../shell/shellContext', () => ({ useShellState: () => ({ routeEntryGeneration: 0 }) }))
vi.mock('../shell/shellState', () => ({ hasCapability: (_: unknown, name: string) => name === 'finance.credits.approve' }))
afterEach(() => { cleanup(); vi.unstubAllGlobals() })

describe('bank group credit limit', () => {
  it('preserves exact signed recorded usage and unknown ceiling and rejects wrong source identity or numeric amounts', () => {
    expect(decodeCreditLimitWorkspace(workspace(), account)?.assessment.recordedPrincipalNet).toBe('-7.125')
    expect(decodeCreditLimitWorkspace({ ...workspace(), companyId: Account }, account)).toBeNull()
    expect(decodeCreditLimitWorkspace({ ...workspace(), items: [{ ...row(), lenderPartyId: Company }] }, account)).toBeNull()
    expect(decodeCreditLimitWorkspace({ ...workspace(), items: [{ ...row(), source: { ...row().source, amount: 50 } }] }, account)).toBeNull()
    expect(decodeCreditLimitWorkspace({ ...workspace(), assessment: { ...workspace().assessment, exceedsLimit: false } }, account)).toBeNull()
  })
  it('shows exceeded advisory without hiding the independent approval action', async () => {
    const current = { ...row(), id: Currency, status: 'Approved', version: '2', decidedAtUtc: '2026-11-17T11:00:00Z' }
    vi.stubGlobal('fetch', vi.fn(async () => json({ ...workspace(), items: [row(), current], assessment: { recordedPrincipalNet: '80.00', exceedsLimit: true, proposedMovements: [] } })))
    render(<QueryClientProvider client={new QueryClient()}><CreditLimitsPanel account={account} namespace="SENTETIK-advisory" refreshVersion={0} /></QueryClientProvider>)
    await screen.findByText('Limit aşımı var. Bu uyarı kaydı veya onayı engellemez.')
    expect((screen.getByRole('button', { name: 'Limiti onayla' }) as HTMLButtonElement).disabled).toBe(false)
  })
  it('keeps an unknown write across remount and a transport refusal then resends the exact command key', async () => {
    const requests: RequestInit[] = []; let status = 503
    vi.stubGlobal('fetch', vi.fn(async (_: RequestInfo | URL, init?: RequestInit) => {
      if (init?.method !== 'POST') return json(workspace())
      requests.push(init)
      return status === 200 ? json({ outcome: 'Succeeded', resource: { ...row(), status: 'Approved', version: '2', decidedAtUtc: '2026-11-17T11:00:00Z' }, isReplay: true, originalCorrelationId: 'SENTETIK', issues: [] }) : json({}, status)
    }))
    const namespace = `SENTETIK:${crypto.randomUUID()}`
    const mount = () => render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><CreditLimitsPanel account={account} namespace={namespace} refreshVersion={0} /></QueryClientProvider>)
    const first = mount(); fireEvent.click(await screen.findByRole('button', { name: 'Limiti onayla' })); await screen.findByText('Sonuç doğrulanamadı; aynı istek ve anahtarla yeniden sorgulayın.')
    first.unmount(); status = 403; const second = mount(); fireEvent.click(await screen.findByRole('button', { name: 'Aynı limit isteğini yeniden gönder' }))
    await waitFor(() => { expect(requests).toHaveLength(2); expect((screen.getByRole('button', { name: 'Aynı limit isteğini yeniden gönder' }) as HTMLButtonElement).disabled).toBe(false) })
    second.unmount(); status = 200; mount(); fireEvent.click(await screen.findByRole('button', { name: 'Aynı limit isteğini yeniden gönder' })); await screen.findByText('Limit işlemi kaydedildi.')
    expect(requests).toHaveLength(3); expect(requests[0]?.body).toBe(requests[2]?.body)
    expect(new Headers(requests[0]?.headers).get('Idempotency-Key')).toBe(new Headers(requests[2]?.headers).get('Idempotency-Key'))
  })
})
