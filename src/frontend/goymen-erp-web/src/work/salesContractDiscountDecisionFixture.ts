import type {WorkSalesContractDiscountPreview} from './workSalesContractDiscountDecision'
import {SALES_CONTRACT_DISCOUNT_WORK_KIND as kind} from './workSalesContractDiscountDecision'
import {salesRequestIncrementable} from './workSalesRequestDecision'

export const salesContractDiscountId = 'f5110000-0000-4000-8000-000000000001'
export const salesContractMaker = 'f5110000-0000-4000-8000-000000000002'
export const salesContractChecker = 'f5110000-0000-4000-8000-000000000003'
export function salesContractDiscountHuman(company: string, version = '9007199254740993',
  state: WorkSalesContractDiscountPreview['contract']['state'] = 'PendingDiscountApproval', visible = true): WorkSalesContractDiscountPreview {
  const deciding = state !== 'PendingDiscountApproval', action = !deciding && salesRequestIncrementable(version)
  return {companyId: company, approvalSupported: action, rejectionSupported: action, contract: {
    id: salesContractDiscountId, contractNo: 11, partyId: 'f5110000-0000-4000-8000-000000000004', companyAccountId: 'f5110000-0000-4000-8000-000000000005', currencyId: 'f5110000-0000-4000-8000-000000000006',
    moneyScale: 2, state, version, validFrom: '0001-01-01', validThrough: '9999-12-31', amountDisclosure: visible ? 'Visible' : 'Withheld',
    installments: [{installmentNo: 1, dueDate: '2026-12-01', amount: visible ? '1500.00' : null}, {installmentNo: 2, dueDate: '2027-01-01', amount: visible ? '1500.25' : null}],
    plannedTotalAmount: visible ? '3000.25' : null, discountRules: [{ruleNo: 1, kind: 'Line', label: 'SENTETIK hacim', ratePercent: visible ? '5' : null}, {ruleNo: 2, kind: 'Total', label: 'SENTETIK toplam', ratePercent: visible ? '2.5' : null}],
    clauses: '  SENTETIK sözleşme maddesi\nİkinci satır  ', discountApprovalRequired: true, discountLimitDefined: false, discountLimitPercent: null,
    isInEffectToday: state === 'Active', createdBy: salesContractMaker, createdAtUtc: '2026-11-28T09:00:00.0000001Z',
    decidedBy: deciding ? salesContractChecker : null, decidedAtUtc: deciding ? '2026-11-28T08:00:00.0000001Z' : null, rejectionReason: state === 'DiscountRejected' ? 'SENTETIK ret' : null,
  }}
}
export function salesContractDiscountFixture(company: string, version = '9007199254740993',
  state: WorkSalesContractDiscountPreview['contract']['state'] = 'PendingDiscountApproval', visible = true) {
  const h = salesContractDiscountHuman(company, version, state, visible)
  return {companyId: company, kind, itemId: salesContractDiscountId, documentLabel: 'Sözleşme #11', sourceVersion: version, pending: state === 'PendingDiscountApproval', approvalReasonRequired: false,
    approvalSupported: h.approvalSupported, rejectionSupported: h.rejectionSupported, salesContractDiscount: h,
    rules: [{ruleCode: 'sales.contract-discount-limit-undefined', threshold: null, actual: null, deviation: null, unit: null}],
    references: [{role: 'sales-contract', label: 'Sözleşme #11', recordId: salesContractDiscountId, link: null}]}
}
