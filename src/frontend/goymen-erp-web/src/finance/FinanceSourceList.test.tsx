/**
 * Source list — cursor paging, redaction, FS0 name join. Component evidence only.
 */

import { describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { FinanceSourceList } from './FinanceSourceList'
import { AMOUNT_REDACTED, BANK_NAME_UNAVAILABLE, NEXT_PAGE, OPEN_SOURCE, PREVIOUS_PAGE } from './financeText'
import { EXAMPLE_SOURCE, NEGATIVE_EXAMPLE } from './contractExamples'
import { COMPANY_A, COMPANY_B, NO_REPLY, bankPage, found, recordingPort, renderFinance, shellFor, sourceSummary } from './testHarness'
import { FINANCE_CAPABILITY } from './capabilities'

const SECOND = 'dddddddd-0000-4000-8000-000000000004'

describe('FinanceSourceList', () => {
  it('renders rows from the page, joins the bank name, pages by the server cursor', async () => {
    const onOpen = vi.fn()
    const { port, reads } = recordingPort((method, scope, request) => {
      if (method === 'listBankAccounts') return found(bankPage(scope.companyId))
      const cursor = (request as { cursor: string | null }).cursor
      return cursor === null
        ? found({ companyId: scope.companyId, items: [sourceSummary(scope.companyId)], nextCursor: 'c-2' })
        : found({ companyId: scope.companyId, items: [sourceSummary(scope.companyId, { id: SECOND, sourceNumber: 'SENTETIK-2', stage: 'Recorded', amount: NEGATIVE_EXAMPLE.redactedAmount })], nextCursor: null })
    })
    renderFinance(<FinanceSourceList onOpen={onOpen} />, { port })

    const row = (await screen.findByRole('rowheader', { name: 'SENTETIK-DRAFT-1' })).closest('tr') as HTMLElement
    within(row).getByText('Taslak')
    within(row).getByText('1.000')
    within(row).getByText('SENTETIK Banka Hesabı')
    fireEvent.click(within(row).getByRole('button', { name: `${OPEN_SOURCE}: SENTETIK-DRAFT-1` }))
    expect(onOpen).toHaveBeenCalledWith(EXAMPLE_SOURCE)

    fireEvent.click(screen.getByRole('button', { name: NEXT_PAGE }))
    const second = (await screen.findByRole('rowheader', { name: 'SENTETIK-2' })).closest('tr') as HTMLElement
    within(second).getByText(AMOUNT_REDACTED)
    expect(second.textContent).not.toMatch(/1\.000/)
    expect(reads.filter((read) => read.method === 'listSources').map((read) => read.request)).toEqual([{ cursor: null }, { cursor: 'c-2' }])
    screen.getByRole('button', { name: PREVIOUS_PAGE })
  })

  it('a failed bank-name read keeps the rows and says the name is unavailable', async () => {
    const { port } = recordingPort((method, scope) =>
      method === 'listBankAccounts' ? NO_REPLY : found({ companyId: scope.companyId, items: [sourceSummary(scope.companyId)], nextCursor: null }),
    )
    renderFinance(<FinanceSourceList onOpen={() => {}} />, { port })
    await screen.findByRole('rowheader', { name: 'SENTETIK-DRAFT-1' })
    await waitFor(() => screen.getByText(BANK_NAME_UNAVAILABLE))
  })

  it('an empty first page is the empty state; a page for another company is malformed', async () => {
    const { port } = recordingPort((method, scope) =>
      method === 'listBankAccounts' ? found(bankPage(scope.companyId)) : found({ companyId: scope.companyId, items: [], nextCursor: null }),
    )
    const first = renderFinance(<FinanceSourceList onOpen={() => {}} />, { port })
    await waitFor(() => expect(first.container.querySelector('[data-state="empty-no-data"]')).not.toBeNull())
    first.unmount()

    const foreign = recordingPort((method) =>
      method === 'listBankAccounts' ? found(bankPage(COMPANY_A)) : found({ companyId: COMPANY_B, items: [], nextCursor: null }),
    )
    const second = renderFinance(<FinanceSourceList onOpen={() => {}} />, { port: foreign.port })
    await waitFor(() => expect(second.container.querySelector('[data-state="malformed-response"]')).not.toBeNull())
  })

  // Business 5745076917: no technical id as final presentation.
  it('shows the currency CODE only from the real candidate read, the labelled reference otherwise, and never an id as the source number', async () => {
    const currencyId = '22222222-2222-2222-2222-222222222222'
    const idAsNumber = sourceSummary(COMPANY_A, { id: SECOND, sourceNumber: SECOND })
    const responder = (method: string, scope: { companyId: string }) => {
      if (method === 'listBankAccounts') return found(bankPage(scope.companyId))
      if (method === 'bankAccountCurrencyCandidates') {
        return found({ companyId: scope.companyId, items: [{ currencyId, code: 'TRY', displayLabel: 'SENTETIK Lira' }] })
      }
      return found({ companyId: scope.companyId, items: [idAsNumber], nextCursor: null })
    }

    const proposer = recordingPort(responder)
    const first = renderFinance(<FinanceSourceList onOpen={() => {}} />, {
      port: proposer.port,
      shell: shellFor(COMPANY_A, 1, [FINANCE_CAPABILITY.bankAccountsPropose]),
    })
    const row = (await screen.findByRole('rowheader', { name: `Numara verilmedi · ref. …${SECOND.slice(-8)}` })).closest('tr') as HTMLElement
    await waitFor(() => within(row).getByText('TRY'))
    expect(row.textContent).not.toContain(SECOND)
    first.unmount()

    const reader = recordingPort(responder)
    renderFinance(<FinanceSourceList onOpen={() => {}} />, { port: reader.port })
    const other = (await screen.findByRole('rowheader', { name: `Numara verilmedi · ref. …${SECOND.slice(-8)}` })).closest('tr') as HTMLElement
    within(other).getByText('Para birimi kimliği …2222')
    expect(reader.reads.map((read) => read.method)).not.toContain('bankAccountCurrencyCandidates')
  })
})
