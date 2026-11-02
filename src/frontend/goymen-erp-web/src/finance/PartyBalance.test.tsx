/**
 * F1b / API-04: the party account's all-time balance summary (cari bakiye özeti) — the decoder's fail-closed rules and
 * the card above the ledger: one row per currency with the server's totals, balance and open receivable / payable.
 * Component evidence only (SENTETIK).
 */

import { describe, expect, it } from 'vitest'
import { waitFor, within } from '@testing-library/react'
import { PartyLedger } from './PartyLedger'
import { decodePartyBalance } from './partyLedgerContract'
import { LEDGER_ACCOUNT, LEDGER_PARTY, LEDGER_TRY, LEDGER_USD, ledgerBody } from './partyLedgerFixtures'
import { COMPANY_A, found, recordingPort, renderFinance } from './testHarness'

const visible = (amount: string, currencyId: string) => ({ disclosure: 'Visible', money: { amount, currencyId } })
const redacted = { disclosure: 'Redacted', money: null }

function currency(currencyId: string, patch: Record<string, unknown> = {}) {
  return {
    currencyId,
    debitTotal: visible('1000.00', currencyId),
    creditTotal: visible('400.00', currencyId),
    balance: { side: 'Debit', amount: visible('600.00', currencyId) },
    openReceivable: visible('1000.00', currencyId),
    openPayable: visible('400.00', currencyId),
    ...patch,
  }
}

function summary(currencies: unknown[] = [currency(LEDGER_TRY)], companyId = COMPANY_A) {
  return { companyId, partyId: LEDGER_PARTY, companyAccountId: LEDGER_ACCOUNT, currencies }
}

describe('party balance decoder', () => {
  it('accepts the server shape, a fully withheld summary and an empty account', () => {
    expect(decodePartyBalance(summary(), COMPANY_A)).toEqual({ ok: true, value: summary() })
    const withheld = currency(LEDGER_USD, {
      debitTotal: redacted, creditTotal: redacted, balance: { side: null, amount: redacted },
      openReceivable: redacted, openPayable: redacted,
    })
    expect(decodePartyBalance(summary([withheld]), COMPANY_A).ok).toBe(true)
    expect(decodePartyBalance(summary([]), COMPANY_A).ok).toBe(true)
  })

  it.each([
    ['another company', summary(undefined, 'bbbbbbbb-0000-4000-8000-000000000002')],
    ['an extra field', { ...summary(), net: '600.00' }],
    ['a duplicated currency', summary([currency(LEDGER_TRY), currency(LEDGER_TRY)])],
    ['a balance side that does not fit the amount', summary([currency(LEDGER_TRY, { balance: { side: 'Zero', amount: visible('600.00', LEDGER_TRY) } })])],
    ['mixed disclosures', summary([currency(LEDGER_TRY, { openPayable: redacted })])],
    ['money in another currency', summary([currency(LEDGER_TRY, { openReceivable: visible('1.00', LEDGER_USD) })])],
    ['a negative open amount', summary([currency(LEDGER_TRY, { openPayable: visible('-1.00', LEDGER_TRY) })])],
  ])('refuses %s', (_name, body) => {
    expect(decodePartyBalance(body, COMPANY_A).ok).toBe(false)
  })
})

describe('PartyLedger balance summary', () => {
  it('shows the server totals, balance and open receivable / payable above the ledger', async () => {
    const { port, reads } = recordingPort((method) =>
      method === 'partyBalance' ? found(summary()) : found(ledgerBody()))
    const { container } = renderFinance(
      <PartyLedger partyId={LEDGER_PARTY} companyAccountId={LEDGER_ACCOUNT}
        range={{ from: '2026-09-01', to: '2026-09-30' }} onRange={() => undefined} />,
      { port, router: true },
    )
    await waitFor(() => expect(container.querySelector('[data-balance-currency]')).toBeTruthy())
    expect(reads.find((read) => read.method === 'partyBalance')?.request)
      .toEqual({ partyId: LEDGER_PARTY, companyAccountId: LEDGER_ACCOUNT })
    const row = container.querySelector(`[data-balance-currency="${LEDGER_TRY}"]`) as HTMLElement
    expect(within(row).getAllByRole('cell').map((cell) => cell.textContent))
      .toEqual(['1.000,00', '400,00', '600,00 (B)', '1.000,00', '400,00'])
  })
})
