/**
 * DEC-FIN-016 risk view (user decision 2026-09-27): the debt closes at collection, the risk is tracked separately —
 * decoder fail-closed rules and the block's company / customer views, as-of date and redaction. Component evidence only.
 */

import { describe, expect, it } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { CustomerInstrumentRiskBlock } from './CustomerInstrumentRisk'
import { decodeCustomerInstrumentRisk } from './customerInstrumentContract'
import { INSTRUMENT_COMPANY, riskBody, riskFigures } from './customerInstrumentFixtures'
import { RISK_EMPTY, RISK_TITLE } from './customerInstrumentText'
import { AMOUNT_REDACTED } from './financeText'
import { body, found, recordingPort, renderFinance } from './testHarness'

const PARTY = { partyId: 'c5000000-0000-4000-8000-000000000005', companyAccountId: 'c6000000-0000-4000-8000-000000000006' }
const FIGURES = riskFigures({
  inPortfolioNotDue: { count: 1, amount: '1500.00' },
  dishonoured: { count: 1, amount: '700.00' },
  collected: { count: 2, amount: '2000.00' },
  openExposure: { count: 2, amount: '2200.00' },
})

function bucketRow(table: HTMLElement, bucket: string): HTMLElement {
  return table.querySelector(`[data-risk-bucket="${bucket}"]`) as HTMLElement
}

describe('customer instrument risk decoder', () => {
  it('accepts the server shape and refuses disagreeing counts, foreign companies and a half-named scope', () => {
    expect(decodeCustomerInstrumentRisk(riskBody(FIGURES), INSTRUMENT_COMPANY).ok).toBe(true)
    expect(decodeCustomerInstrumentRisk(riskBody(FIGURES, PARTY), INSTRUMENT_COMPANY).ok).toBe(true)
    expect(decodeCustomerInstrumentRisk(riskBody(riskFigures({ collected: { count: 1, amount: '0' } })),
      INSTRUMENT_COMPANY).ok).toBe(false)
    expect(decodeCustomerInstrumentRisk(riskBody(riskFigures({ collected: { count: 0, amount: '5.00' } })),
      INSTRUMENT_COMPANY).ok).toBe(false)
    expect(decodeCustomerInstrumentRisk(riskBody(FIGURES), 'bbbbbbbb-0000-4000-8000-000000000002').ok).toBe(false)
    expect(decodeCustomerInstrumentRisk({ ...riskBody(FIGURES, PARTY), companyAccountId: null }, INSTRUMENT_COMPANY).ok)
      .toBe(false)
    const foreignLine = riskBody(FIGURES, PARTY)
    ;(foreignLine.customers as Record<string, unknown>[])[0].partyId = 'd1000000-0000-4000-8000-000000000001'
    expect(decodeCustomerInstrumentRisk(foreignLine, INSTRUMENT_COMPANY).ok).toBe(false)
    const redactedWithDigits = riskFigures({ collected: { count: 1, amount: null } })
    ;(redactedWithDigits.openExposure as Record<string, unknown>).amount = { disclosure: 'Redacted', money: { amount: '1', currencyId: 'x' } }
    expect(decodeCustomerInstrumentRisk(riskBody(redactedWithDigits), INSTRUMENT_COMPANY).ok).toBe(false)
  })
})

describe('CustomerInstrumentRiskBlock', () => {
  it('shows the company total and the per-customer lines as of the chosen date', async () => {
    const { port, reads } = recordingPort((method, scope, request) => {
      if (method !== 'customerInstrumentRisk') return found({ companyId: scope.companyId, items: [] })
      const asOf = (request as { asOf: string }).asOf
      return found(riskBody(FIGURES, null, asOf))
    })
    renderFinance(<CustomerInstrumentRiskBlock party={null} />, { port })
    const block = await screen.findByRole('region', { name: RISK_TITLE })
    const total = (await within(block).findByText(/^Şirket toplamı/)).closest('table') as HTMLElement
    expect(within(bucketRow(total, 'inPortfolioNotDue')).getAllByRole('cell').map((cell) => cell.textContent))
      .toEqual(['1', '1.500,00'])
    expect(within(bucketRow(total, 'openExposure')).getAllByRole('cell').map((cell) => cell.textContent))
      .toEqual(['2', '2.200,00'])
    expect(within(bucketRow(total, 'returnedToCustomer')).getAllByRole('cell').map((cell) => cell.textContent))
      .toEqual(['0', '0'])
    within(block).getByRole('rowheader', { name: 'Cari hesap ref. …00000006' })
    // No threshold or warning text: facts only (OPEN-010).
    expect(block.textContent).not.toMatch(/uyarı:|limit aşıld/i)

    const first = reads.filter((read) => read.method === 'customerInstrumentRisk')
    expect(first.at(-1)?.request).toMatchObject({ partyId: null, companyAccountId: null })
    fireEvent.change(within(block).getByLabelText('Tarih itibarıyla'), { target: { value: '2026-12-01' } })
    await waitFor(() => expect(reads.filter((read) => read.method === 'customerInstrumentRisk').at(-1)?.request)
      .toEqual({ asOf: '2026-12-01', partyId: null, companyAccountId: null }))
  })

  it('scopes to one customer account on the customer context, without the per-customer table', async () => {
    const { port, reads } = recordingPort(() => found(riskBody(FIGURES, PARTY)))
    renderFinance(<CustomerInstrumentRiskBlock party={PARTY} />, { port })
    const block = await screen.findByRole('region', { name: RISK_TITLE })
    await within(block).findByText(/^Bu cari/)
    expect(within(block).queryByText('Carilere göre')).toBeNull()
    expect(reads.find((read) => read.method === 'customerInstrumentRisk')?.request)
      .toMatchObject({ partyId: PARTY.partyId, companyAccountId: PARTY.companyAccountId })
  })

  it('without the amount grant shows counts only and no digit of any amount', async () => {
    const redacted = riskFigures({
      inPortfolioNotDue: { count: 1, amount: null },
      openExposure: { count: 1, amount: null },
    })
    const { port } = recordingPort(() => found(riskBody(redacted)))
    const view = renderFinance(<CustomerInstrumentRiskBlock party={null} />, { port })
    const block = await screen.findByRole('region', { name: RISK_TITLE })
    const total = (await within(block).findByText(/^Şirket toplamı/)).closest('table') as HTMLElement
    expect(within(bucketRow(total, 'openExposure')).getAllByRole('cell').map((cell) => cell.textContent))
      .toEqual(['1', AMOUNT_REDACTED])
    expect(within(total).getAllByText(AMOUNT_REDACTED)).toHaveLength(7)
    expect(view.container.querySelectorAll('[data-amount-kind="visible"]')).toHaveLength(0)
  })

  it('is hidden without the read grant and says when there is nothing', async () => {
    const denied = recordingPort(() => body({ outcome: 'Denied', resource: null }))
    const first = renderFinance(<CustomerInstrumentRiskBlock party={null} />, { port: denied.port })
    await waitFor(() => expect(denied.reads.length).toBeGreaterThan(0))
    await waitFor(() => expect(screen.queryByRole('region', { name: RISK_TITLE })).toBeNull())
    first.unmount()

    const empty = recordingPort(() => found({ ...riskBody(FIGURES), customers: [], totals: [] }))
    renderFinance(<CustomerInstrumentRiskBlock party={null} />, { port: empty.port })
    await screen.findByText(RISK_EMPTY)
  })
})
