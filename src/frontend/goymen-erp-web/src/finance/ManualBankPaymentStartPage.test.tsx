import { fireEvent, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { DraftContextRequest } from './draftContext'
import type { FinanceReceiptPort, FinanceScope } from './port'
import { ManualBankPaymentStartPage } from './ManualBankPaymentStartPage'
import {
  COMPANY_A,
  bankAccount,
  bankAccountCard,
  contextOk,
  contextStatus,
  draftContextBody,
  found,
  renderFinance,
  shellFor,
} from './testHarness'

const PARTY = '33333333-3333-4333-8333-333333333333'
const COMPANY_ACCOUNT = '44444444-4444-4444-8444-444444444444'
const BANK = '55555555-5555-4555-8555-555555555555'

function installCounterpartyRead() {
  vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({
    items: [{
      id: PARTY,
      code: 'C-100',
      kind: 1,
      legalName: 'SENTETIK Alici',
      tradeName: null,
      isActive: true,
      version: 1,
      companyAccounts: [{ id: COMPANY_ACCOUNT, companyId: COMPANY_A, roles: 1, isActive: true, version: 1 }],
    }],
    nextCursor: null,
    hasMore: false,
    totalCount: null,
    countScope: null,
  }), { status: 200, headers: { 'Content-Type': 'application/json' } })))
}

function startPort(contextStatusCode?: number): FinanceReceiptPort {
  return {
    listBankAccountMaster: async () => found({
      companyId: COMPANY_A,
      items: [{
        account: bankAccount(COMPANY_A, { id: BANK }),
        isOwnProposal: false,
        card: bankAccountCard({ moneyEligibility: 'Eligible', iban: 'TR330006100519786457841326' }),
        isOwnPendingIbanProposal: false,
      }],
    }),
    draftContext: async (_scope: FinanceScope, request: DraftContextRequest) => contextStatusCode === undefined
      ? contextOk(draftContextBody(COMPANY_A, request, { bankAccount: bankAccount(COMPANY_A, { id: BANK }) }))
      : contextStatus(contextStatusCode),
  } as unknown as FinanceReceiptPort
}

function chooseContext() {
  fireEvent.change(screen.getByLabelText('Cari hesabı'), { target: { value: PARTY } })
  fireEvent.change(screen.getByLabelText('Banka hesabı'), { target: { value: BANK } })
  fireEvent.change(screen.getByLabelText('İşlem tarihi'), { target: { value: '2026-09-24' } })
}

afterEach(() => vi.unstubAllGlobals())

describe('manual bank payment start page', () => {
  it('uses authorized options and continues only with the canonical draft context', async () => {
    installCounterpartyRead()
    const onContinue = vi.fn()
    renderFinance(<ManualBankPaymentStartPage companyId={COMPANY_A} onContinue={onContinue} />, {
      port: startPort(),
      shell: shellFor(COMPANY_A),
    })

    await screen.findByRole('option', { name: 'C-100 - SENTETIK Alici' })
    expect(screen.queryByText(PARTY)).toBeNull()
    chooseContext()
    const next = screen.getByRole('button', { name: 'Ödeme formuna devam et' })
    await waitFor(() => expect((next as HTMLButtonElement).disabled).toBe(false))
    fireEvent.click(next)

    expect(onContinue).toHaveBeenCalledWith(expect.objectContaining({
      partyId: PARTY,
      companyAccountId: COMPANY_ACCOUNT,
      treasuryAccountId: BANK,
      currencyId: expect.any(String),
      profileId: expect.any(String),
      profileVersion: expect.stringMatching(/^[1-9][0-9]*$/),
    }))
  })

  it('fails closed when the canonical context read refuses the selection', async () => {
    installCounterpartyRead()
    const onContinue = vi.fn()
    renderFinance(<ManualBankPaymentStartPage companyId={COMPANY_A} onContinue={onContinue} />, {
      port: startPort(404),
      shell: shellFor(COMPANY_A),
    })

    await screen.findByRole('option', { name: 'C-100 - SENTETIK Alici' })
    chooseContext()
    await screen.findByRole('alert')
    expect((screen.getByRole('button', { name: 'Ödeme formuna devam et' }) as HTMLButtonElement).disabled).toBe(true)
    expect(onContinue).not.toHaveBeenCalled()
  })
})

// Native Cari form/attempt behavior is covered by CariQuickCreate.test; these cases exercise the caller's owner read.
vi.mock('../cari/CariQuickCreate', () => ({ CariQuickCreate: ({ initialRoles, onCreated }: { initialRoles: number; onCreated: (record: import('../cari/mutationContract').CounterpartyMutationResult, signal: AbortSignal) => Promise<boolean> }) =>
  <button type="button" onClick={() => void onCreated({
    id: 'aaaabbbb-1111-4111-8111-111111111111', code: 'CAR-NEW', kind: 1, legalName: 'Yeni Cari', tradeName: null, isActive: true, version: '1',
    account: { id: 'aaaabbbb-2222-4222-8222-222222222222', companyId: COMPANY_A, roles: initialRoles, isActive: true, version: '1' },
  }, new AbortController().signal)}>Quick test create</button> }))

describe('inline payment party creation owner selection', () => {
  it.each([true, false])('selects only the active eligible native owner result and preserves bank/date: %s', async eligible => {
    installCounterpartyRead()
    const original = globalThis.fetch
    const owner = vi.fn(async () => new Response(JSON.stringify({ items: eligible ? [{ id: 'aaaabbbb-1111-4111-8111-111111111111', code: 'CAR-NEW', kind: 1, legalName: 'Yeni Cari', tradeName: null, isActive: true, version: 1, companyAccounts: [{ id: 'aaaabbbb-2222-4222-8222-222222222222', companyId: COMPANY_A, roles: 0, isActive: true, version: 1 }] }] : [], nextCursor: null, hasMore: false, totalCount: null, countScope: null }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', vi.fn((input: RequestInfo | URL, init?: RequestInit) => String(input).includes('q=CAR-NEW') ? owner() : original(input, init)))
    renderFinance(<ManualBankPaymentStartPage companyId={COMPANY_A} onContinue={vi.fn()} />, { port: startPort(), shell: shellFor(COMPANY_A) })
    await screen.findByRole('option', { name: 'C-100 - SENTETIK Alici' })
    chooseContext()
    fireEvent.click(screen.getByRole('button', { name: 'Quick test create' }))
    await waitFor(() => expect(owner).toHaveBeenCalledTimes(1))
    await waitFor(() => expect((screen.getByLabelText('Cari hesabı') as HTMLSelectElement).value).toBe(eligible ? 'aaaabbbb-1111-4111-8111-111111111111' : PARTY))
    expect((screen.getByLabelText('Banka hesabı') as HTMLSelectElement).value).toBe(BANK)
    expect((screen.getByLabelText('İşlem tarihi') as HTMLInputElement).value).toBe('2026-09-24')
  })
})
