import {afterEach, describe, expect, it} from 'vitest'
import {cleanup, render, screen, within} from '@testing-library/react'
import {COMPANY_A} from '../settings/testHarness'
import {WorkSalesContractDiscountFacts} from './WorkSalesContractDiscountFacts'
import {SALES_CONTRACT_DISCOUNT_WORK_TEXT as T} from './workSalesContractDiscountText'
import {salesContractDiscountHuman as human} from './salesContractDiscountDecisionFixture'
afterEach(cleanup)
describe('Contract authorized full native human renderer', () => {
  it('renders all24 native fields, flags and complete installment/rule tables without inputs or private proof', () => {
    const h = human(COMPANY_A), c = h.contract; render(<WorkSalesContractDiscountFacts human={h}/>); const region = screen.getByRole('region', {name: T.title})
    for (const value of [h.companyId, c.id, String(c.contractNo), c.partyId, c.companyAccountId, c.currencyId, c.version, c.validFrom, c.validThrough!, c.plannedTotalAmount!, c.clauses!, c.createdBy, c.createdAtUtc]) expect(region.textContent).toContain(value)
    for (const label of Object.values(T.fields)) expect(within(region).getByText(label, {exact: true})).toBeTruthy()
    expect(within(region).getAllByRole('table')).toHaveLength(2); expect(within(region).getAllByRole('row')).toHaveLength(6)
    for (const value of [...c.installments.flatMap(x => [x.dueDate, x.amount!]), ...c.discountRules.flatMap(x => [x.label, x.ratePercent!])]) expect(region.textContent).toContain(value)
    for (const role of ['button', 'textbox', 'checkbox', 'combobox'] as const) expect(within(region).queryByRole(role)).toBeNull()
    for (const forbidden of ['sourceFactsDigest', 'previewDigest', 'originalDecisionReceipt', 'payloadHash', 'auditIntegrityHash']) expect(region.textContent).not.toContain(forbidden)
  })
  it('redacts all numeric price slots without removing the rows or their native terms', () => {
    const h = human(COMPANY_A, '1', 'PendingDiscountApproval', false); render(<WorkSalesContractDiscountFacts human={h}/>); const region = screen.getByRole('region', {name: T.title})
    expect(within(region).getAllByText(T.withheld)).toHaveLength(7)
    for (const price of ['1500.00', '1500.25', '3000.25']) expect(region.textContent).not.toContain(price)
    expect(within(region).getAllByRole('row')).toHaveLength(6); expect(region.textContent).toContain(h.contract.clauses!)
  })
  it.each(['Active', 'DiscountRejected'] as const)('shows the actual terminal %s decision and server current-day flag without creating an action', state => {
    const h = human(COMPANY_A, '9223372036854775807', state); render(<WorkSalesContractDiscountFacts human={h}/>); const region = screen.getByRole('region', {name: T.title})
    for (const value of [T.states[state], h.contract.decidedBy!, h.contract.decidedAtUtc!, h.contract.version]) expect(region.textContent).toContain(value)
    if (state === 'DiscountRejected') expect(region.textContent).toContain(h.contract.rejectionReason!)
    expect(within(region).queryByRole('button')).toBeNull()
  })
})
