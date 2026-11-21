import { screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { PartyLedger } from './PartyLedger'
import { decodePartyLedgerAnswer } from './partyLedgerContract'
import { IDS, LEDGER_ACCOUNT, LEDGER_PARTY, LEDGER_TRY, ledgerBody } from './partyLedgerFixtures'
import { LEDGER_NO_CARRY_FORWARD, LEDGER_OPENING_BALANCE, LEDGER_PERIOD_CARRY } from './partyLedgerText'
import { COMPANY_A, found, recordingPort, renderFinance } from './testHarness'

// Synthetic server answers; no financial arithmetic is performed in the UI or in this fixture.
function carryBody(redacted = false) {
  const amount = (value: string) => redacted
    ? { disclosure: 'Redacted', money: null }
    : { disclosure: 'Visible', money: { amount: value, currencyId: LEDGER_TRY } }
  const balance = (value: string) => ({ side: redacted ? null : 'Credit', amount: amount(value) })
  return {
    companyId: COMPANY_A, partyId: LEDGER_PARTY, companyAccountId: LEDGER_ACCOUNT,
    from: '2026-12-01', to: '2026-12-31', carryForward: 'PeriodClosureVoucher',
    periodCarry: { voucherId: IDS.document, sourcePeriodId: IDS.payment, sourceEndsOn: '2026-11-30',
      targetPeriodId: String(IDS.collection), targetStartsOn: '2026-12-01', sourceHash: 'a'.repeat(64) },
    currencies: [{ currencyId: LEDGER_TRY, entries: [], openingBalance: balance('100.00'),
      debitTotal: amount('0.00'), creditTotal: amount('0.00'), closingBalance: balance('100.00') }],
  }
}

describe('period carry ledger contract', () => {
  it('preserves the legacy shape and accepts proof-bound carry without synthetic movement rows', () => {
    expect(decodePartyLedgerAnswer(ledgerBody(), COMPANY_A).ok).toBe(true)
    const answer = decodePartyLedgerAnswer(carryBody(), COMPANY_A)
    expect(answer.ok).toBe(true)
    if (answer.ok && answer.value.kind === 'ledger') {
      expect(answer.value.ledger.currencies[0].entries).toEqual([])
      expect(answer.value.ledger.currencies[0].openingBalance?.amount.money?.amount).toBe('100.00')
      expect(answer.value.ledger.periodCarry?.sourceHash).toBe('a'.repeat(64))
    }
    expect(decodePartyLedgerAnswer(carryBody(true), COMPANY_A).ok).toBe(true)
  })

  it('requires the immutable proof and matching opening/closing on a carry-only currency', () => {
    const missing = { ...carryBody(), periodCarry: undefined }
    expect(decodePartyLedgerAnswer(missing, COMPANY_A).ok).toBe(false)
    const badHash = carryBody(); badHash.periodCarry.sourceHash = 'not-a-hash'
    expect(decodePartyLedgerAnswer(badHash, COMPANY_A).ok).toBe(false)
    const future = carryBody(); future.periodCarry.targetStartsOn = '2027-01-01'
    expect(decodePartyLedgerAnswer(future, COMPANY_A).ok).toBe(false)
    const crossed = carryBody(); crossed.periodCarry.targetPeriodId = crossed.periodCarry.sourcePeriodId
    expect(decodePartyLedgerAnswer(crossed, COMPANY_A).ok).toBe(false)
    const changed = carryBody(); changed.currencies[0].closingBalance.amount.money!.amount = '99.00'
    expect(decodePartyLedgerAnswer(changed, COMPANY_A).ok).toBe(false)
    const fakeRange = carryBody(); fakeRange.currencies[0].creditTotal.money!.amount = '100.00'
    expect(decodePartyLedgerAnswer(fakeRange, COMPANY_A).ok).toBe(false)
    const legacy = { ...carryBody(), carryForward: 'NoneOpen018' }
    expect(decodePartyLedgerAnswer(legacy, COMPANY_A).ok).toBe(false)
  })

  it('refuses carry disclosures that leak digits or balance side into a redacted result', () => {
    const sided = carryBody(true); sided.currencies[0].openingBalance.side = 'Credit'
    expect(decodePartyLedgerAnswer(sided, COMPANY_A).ok).toBe(false)
    const mixed = carryBody(true)
    mixed.currencies[0].openingBalance = carryBody().currencies[0].openingBalance
    expect(decodePartyLedgerAnswer(mixed, COMPANY_A).ok).toBe(false)
    const otherCompany = carryBody(); otherCompany.companyId = IDS.payment
    expect(decodePartyLedgerAnswer(otherCompany, COMPANY_A).ok).toBe(false)
  })
})

describe('period carry ledger screen', () => {
  it('shows the actual carry opening separately and creates no source movement row', async () => {
    const { port } = recordingPort(() => found(carryBody()))
    const { container } = renderFinance(<PartyLedger partyId={LEDGER_PARTY} companyAccountId={LEDGER_ACCOUNT}
      range={{ from: '2026-12-01', to: '2026-12-31' }} onRange={vi.fn()} />, { port, router: true })
    await waitFor(() => expect(screen.getByText(LEDGER_PERIOD_CARRY)).toBeTruthy())
    expect(screen.getByText(LEDGER_OPENING_BALANCE)).toBeTruthy()
    expect(screen.queryByText(LEDGER_NO_CARRY_FORWARD)).toBeNull()
    expect(container.querySelectorAll('[data-ledger-kind]')).toHaveLength(0)
    expect(container.querySelectorAll('[data-ledger-opening]')).toHaveLength(1)
  })

  it('shows the carry boundary with no amount or side when the backend withholds amounts', async () => {
    const { port } = recordingPort(() => found(carryBody(true)))
    const { container } = renderFinance(<PartyLedger partyId={LEDGER_PARTY} companyAccountId={LEDGER_ACCOUNT}
      range={{ from: '2026-12-01', to: '2026-12-31' }} onRange={vi.fn()} />, { port, router: true })
    await waitFor(() => expect(screen.getByText(LEDGER_OPENING_BALANCE)).toBeTruthy())
    const table = container.querySelector('table[data-ledger-currency]')!
    expect(table.textContent).not.toContain('100,00')
    expect(table.textContent).not.toContain('(A)')
    expect(table.textContent).not.toContain('(B)')
  })
})
