import {returnView, foreignReturnView, RETURN_ID} from '../sales/customerReturnTestData'
import type {CustomerReturnState} from '../sales/customerReturnContract'
import {SALES_CUSTOMER_RETURN_WORK_KIND} from './workSalesCustomerReturnDecision'
export const salesCustomerReturnId = RETURN_ID
export function salesCustomerReturnHuman(state: CustomerReturnState = 'PendingApproval', version = '9007199254740993', foreign = false) {
  const pending = returnView('PendingApproval')
  const current = returnView(state, {version, ...(state === 'Cancelled' ? {submittedBy: pending.submittedBy, submittedAtUtc: pending.submittedAtUtc} : {})}), f = foreignReturnView()
  const customerReturn = foreign ? {...current, rate: f.rate, functionalCurrencyId: f.functionalCurrencyId, functionalScale: f.functionalScale,
    functionalTotals: f.functionalTotals, lines: f.lines} : current
  return {customerReturn, approvalSupported: state === 'PendingApproval', rejectionSupported: state === 'PendingApproval'}
}
export function salesCustomerReturnFixture(company: string, state: CustomerReturnState = 'PendingApproval', version = '9007199254740993') {
  const human = salesCustomerReturnHuman(state, version), c = human.customerReturn
  return {companyId: company, kind: SALES_CUSTOMER_RETURN_WORK_KIND, itemId: salesCustomerReturnId, documentLabel: c.customerDocumentNo,
    sourceVersion: version, pending: state === 'PendingApproval', approvalReasonRequired: false,
    references: [{role: 'sales-customer-return', label: c.customerDocumentNo, recordId: salesCustomerReturnId, link: `/satis/iadeler/${salesCustomerReturnId}`},
      {role: 'sales-invoice', label: c.originalDocumentNo, recordId: c.originalInvoiceId, link: '/satis/faturalar'}],
    rules: [{ruleCode: 'sales.customer-return-approval-required', threshold: null, actual: null, deviation: null, unit: null}],
    approvalSupported: human.approvalSupported, rejectionSupported: human.rejectionSupported, salesCustomerReturn: human}
}
