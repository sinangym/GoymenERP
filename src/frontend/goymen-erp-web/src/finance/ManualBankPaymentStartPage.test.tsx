import { fireEvent, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { FinanceReceiptPort } from './port'
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
    draftContext: async (_scope, request) => contextStatusCode === undefined
      ? contextOk(draftContextBody(COMPANY_A, request, { bankAccount: bankAccount(COMPANY_A, { id: BANK }) }))
      : contextStatus(contextStatusCode),
  } as FinanceReceiptPort
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
    await waitFor(() => expect(next).toBeEnabled())
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
    expect(screen.getByRole('button', { name: 'Ödeme formuna devam et' })).toBeDisabled()
    expect(onContinue).not.toHaveBeenCalled()
  })
})
