/**
 * F-04 Banka Hesapları — list, approval control and propose form, with real pending promises.
 * Component evidence; SENTETIK/NOT_ENV; no API/SQL/browser.
 */

import { describe, expect, it } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import type { ReactNode } from 'react'
import { BankAccountProposePage } from './BankAccountProposePage'
import { BankAccountsPage } from './BankAccountsPage'
import {
  APPROVE,
  APPROVED_NOTICE,
  APPROVE_CONFIRM,
  BANK_OWN_PROPOSAL,
  BANK_PROPOSE_LINK,
  BANK_STATE_APPROVED,
  BANK_STATE_PENDING,
  PROPOSED_NOTICE,
  PROPOSE_CURRENCY,
  PROPOSE_CURRENCY_PLACEHOLDER,
  PROPOSE_ERROR,
  PROPOSE_NAME,
  PROPOSE_NO_CANDIDATES,
  PROPOSE_SUBMIT,
  currencyFallback,
} from './bankAccountText'
import { FINANCE_CAPABILITY } from './capabilities'
import { RESEND_SAME, UNKNOWN_TITLE } from './financeText'
import {
  COMPANY_A,
  NO_REPLY,
  bankAccount,
  body,
  found,
  recordingPort,
  renderFinance,
  shellFor,
  type ApprovalCall,
  type ReadResponder,
} from './testHarness'

const PENDING = 'aaaaaaaa-0000-4000-8000-0000000000b1'
const OWN = 'aaaaaaaa-0000-4000-8000-0000000000b2'
const APPROVED = 'aaaaaaaa-0000-4000-8000-0000000000b3'
const TRY = 'cccccccc-0000-4000-8000-0000000000c1'
const USD = 'cccccccc-0000-4000-8000-0000000000c2'

const account = (id: string, name: string, active: boolean, version = '1') =>
  bankAccount(COMPANY_A, { id, currencyId: TRY, displayName: name, isActive: active, isUsableForReceipt: active, version })

const MASTER = {
  companyId: COMPANY_A,
  items: [
    { account: account(APPROVED, 'SENTETIK Onaylı', true, '2'), isOwnProposal: false },
    { account: account(OWN, 'SENTETIK Benim', false), isOwnProposal: true },
    { account: account(PENDING, 'SENTETIK Bekleyen', false), isOwnProposal: false },
  ],
}

const CANDIDATES = {
  companyId: COMPANY_A,
  items: [
    { currencyId: USD, code: 'USD', displayLabel: 'SENTETIK Dolar' },
    { currencyId: TRY, code: 'TRY', displayLabel: 'SENTETIK Lira' },
  ],
}

function responder(master: unknown = MASTER, candidates: unknown = CANDIDATES): ReadResponder {
  return (method) => {
    if (method === 'listBankAccountMaster') return found(master)
    if (method === 'bankAccountCurrencyCandidates') return found(candidates)
    return NO_REPLY
  }
}

const routed = (ui: ReactNode) => <MemoryRouter>{ui}</MemoryRouter>
const row = (name: string) => screen.getByRole('row', { name: new RegExp(name) })

describe('F-04 list', () => {
  it('shows each account with its state and offers the approval only on another person\'s proposal', async () => {
    const { port, reads } = recordingPort(responder())
    renderFinance(routed(<BankAccountsPage />), {
      port,
      shell: shellFor(COMPANY_A, 1, [FINANCE_CAPABILITY.bankAccountsList, FINANCE_CAPABILITY.bankAccountsApprove]),
    })

    await screen.findByText('SENTETIK Bekleyen')
    expect(within(row('SENTETIK Onaylı')).getByText(BANK_STATE_APPROVED)).toBeTruthy()
    expect(within(row('SENTETIK Benim')).getByText(BANK_OWN_PROPOSAL)).toBeTruthy()
    expect(within(row('SENTETIK Benim')).queryByRole('button', { name: new RegExp(APPROVE) })).toBeNull()
    expect(within(row('SENTETIK Bekleyen')).getByText(BANK_STATE_PENDING)).toBeTruthy()
    expect(within(row('SENTETIK Bekleyen')).getByRole('button', { name: `${APPROVE}: SENTETIK Bekleyen` })).toBeTruthy()
    // Not a proposer: no candidate read, no propose link, the currency's identity instead of a guessed code.
    expect(reads.map((read) => read.method)).not.toContain('bankAccountCurrencyCandidates')
    expect(screen.queryByText(BANK_PROPOSE_LINK)).toBeNull()
    expect(within(row('SENTETIK Onaylı')).getByText(currencyFallback(TRY))).toBeTruthy()
  })

  it('without the approval control published, no row offers it', async () => {
    const { port } = recordingPort(responder())
    renderFinance(routed(<BankAccountsPage />), { port, shell: shellFor(COMPANY_A, 1, [FINANCE_CAPABILITY.bankAccountsList]) })
    await screen.findByText('SENTETIK Bekleyen')
    expect(screen.queryByRole('button', { name: new RegExp(APPROVE) })).toBeNull()
  })

  it('a proposer sees the codes from Settings\' candidates and the propose link', async () => {
    const { port } = recordingPort(responder())
    renderFinance(routed(<BankAccountsPage />), {
      port,
      shell: shellFor(COMPANY_A, 1, [FINANCE_CAPABILITY.bankAccountsList, FINANCE_CAPABILITY.bankAccountsPropose]),
    })
    await waitFor(() => expect(within(row('SENTETIK Onaylı')).getByText('TRY')).toBeTruthy())
    expect(screen.getByText(BANK_PROPOSE_LINK)).toBeTruthy()
  })

  it('an approval sends the row\'s version once, then re-reads the list', async () => {
    const { port, commands, reads } = recordingPort(responder())
    renderFinance(routed(<BankAccountsPage />), {
      port,
      shell: shellFor(COMPANY_A, 1, [FINANCE_CAPABILITY.bankAccountsList, FINANCE_CAPABILITY.bankAccountsApprove]),
    })
    fireEvent.click(await screen.findByRole('button', { name: `${APPROVE}: SENTETIK Bekleyen` }))
    fireEvent.click(screen.getByRole('button', { name: APPROVE_CONFIRM }))

    expect(commands).toHaveLength(1)
    expect(commands[0].method).toBe('approveBankAccount')
    expect(commands[0].request as ApprovalCall).toEqual({ accountId: PENDING, body: { expectedVersion: '1' } })
    const listReads = () => reads.filter((read) => read.method === 'listBankAccountMaster').length
    const before = listReads()
    await commands[0].resolve(
      body({ outcome: 'Succeeded', resource: account(PENDING, 'SENTETIK Bekleyen', true, '2'), isReplay: false, originalCorrelationId: 'SENTETIK-1', issues: [] }),
    )
    expect(await screen.findByText(APPROVED_NOTICE)).toBeTruthy()
    await waitFor(() => expect(listReads()).toBe(before + 1))
    expect(commands).toHaveLength(1)
  })

  it('an unknown approval is frozen and re-sent only with the same key', async () => {
    const { port, commands } = recordingPort(responder())
    renderFinance(routed(<BankAccountsPage />), {
      port,
      shell: shellFor(COMPANY_A, 1, [FINANCE_CAPABILITY.bankAccountsList, FINANCE_CAPABILITY.bankAccountsApprove]),
    })
    fireEvent.click(await screen.findByRole('button', { name: `${APPROVE}: SENTETIK Bekleyen` }))
    fireEvent.click(screen.getByRole('button', { name: APPROVE_CONFIRM }))
    await commands[0].resolve(NO_REPLY)

    expect(await screen.findByText(UNKNOWN_TITLE)).toBeTruthy()
    expect((screen.getByRole('button', { name: `${APPROVE}: SENTETIK Bekleyen` }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: RESEND_SAME }))
    expect(commands).toHaveLength(2)
    expect(commands[1].key).toBe(commands[0].key)
  })
})

describe('F-04 propose', () => {
  it('offers Settings\' candidates as given with nothing preselected, and sends exactly the proposal', async () => {
    const { port, commands } = recordingPort(responder())
    renderFinance(routed(<BankAccountProposePage />), { port })

    const select = (await screen.findByLabelText(PROPOSE_CURRENCY)) as HTMLSelectElement
    expect(select.value).toBe('')
    expect(Array.from(select.options).map((option) => option.textContent)).toEqual([
      PROPOSE_CURRENCY_PLACEHOLDER,
      'USD — SENTETIK Dolar',
      'TRY — SENTETIK Lira',
    ])

    // Nothing chosen: refused locally, nothing sent.
    fireEvent.change(screen.getByLabelText(PROPOSE_NAME), { target: { value: 'SENTETIK Ana' } })
    fireEvent.click(screen.getByRole('button', { name: PROPOSE_SUBMIT }))
    expect(screen.getByText(PROPOSE_ERROR.currencyRequired)).toBeTruthy()
    expect(commands).toHaveLength(0)

    fireEvent.change(select, { target: { value: TRY } })
    fireEvent.click(screen.getByRole('button', { name: PROPOSE_SUBMIT }))
    expect(commands).toHaveLength(1)
    expect(commands[0].method).toBe('proposeBankAccount')
    expect(commands[0].request).toEqual({ displayName: 'SENTETIK Ana', currencyId: TRY })

    await commands[0].resolve(
      body({ outcome: 'Succeeded', resource: account(PENDING, 'SENTETIK Ana', false), isReplay: false, originalCorrelationId: 'SENTETIK-1', issues: [] }),
    )
    expect(await screen.findByText(PROPOSED_NOTICE)).toBeTruthy()
    // The form is closed: a second proposal cannot be sent from it.
    expect(screen.queryByRole('button', { name: PROPOSE_SUBMIT })).toBeNull()
  })

  it('refuses a name with surrounding spaces before sending', async () => {
    const { port, commands } = recordingPort(responder())
    renderFinance(routed(<BankAccountProposePage />), { port })
    fireEvent.change(await screen.findByLabelText(PROPOSE_NAME), { target: { value: ' SENTETIK ' } })
    fireEvent.change(screen.getByLabelText(PROPOSE_CURRENCY), { target: { value: TRY } })
    fireEvent.click(screen.getByRole('button', { name: PROPOSE_SUBMIT }))
    expect(screen.getByText(PROPOSE_ERROR.nameSpaces)).toBeTruthy()
    expect(commands).toHaveLength(0)
  })

  it('says so when the company has no currency candidates', async () => {
    const { port } = recordingPort(responder(MASTER, { companyId: COMPANY_A, items: [] }))
    renderFinance(routed(<BankAccountProposePage />), { port })
    expect(await screen.findByText(PROPOSE_NO_CANDIDATES)).toBeTruthy()
    expect(screen.queryByRole('button', { name: PROPOSE_SUBMIT })).toBeNull()
  })
})
