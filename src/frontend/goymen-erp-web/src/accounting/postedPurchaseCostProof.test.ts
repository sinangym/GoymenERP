import { describe, expect, it } from 'vitest'
import { postedPurchaseCostShare } from './postedPurchaseCostProof'
import { decodeInventoryCost } from './inventoryCostApi'
import { COMPANY_A, COMPANY_B, PREPARER, APPROVER, ACCOUNT_DEBIT, ACCOUNT_CREDIT, JOURNAL } from './testHarness'

const id = 'c0633000-0000-4000-8000-000000000001'
const line = 'c0633000-0000-4000-8000-000000000002'
const at = '2026-11-17T09:00:00Z'
const ratio = { numerator: '1', denominator: '100' }
const zero = { numerator: '0', denominator: '1' }
function source() {
  const approval = { companyId: COMPANY_A, sourceEventId: id, invoiceLineId: line, proposalId: id, proposalVersion: 3,
    journalId: JOURNAL, status: 'Posted', signedAmount: '0.01', currencyId: id, moneyProfileId: id, moneyProfileVersion: 1, moneyScale: 2,
    cogsAccountId: ACCOUNT_DEBIT, counterAccountId: ACCOUNT_CREDIT, accountingDate: '2026-11-17', preparedBy: PREPARER,
    approvedBy: APPROVER, approvedAtUtc: at, sourceProofSha256: 'a'.repeat(64), fingerprint: 'b'.repeat(64),
    recordedRounding: { currencyId: id, moneyProfileId: line, moneyProfileVersion: 2, moneyScale: 2,
      rounding: { ruleVersion: 'accounting.inventory-cost-component-total.v1', roundedSignedAmount: '0.01', exactSignedAmount: ratio,
        exactSignedResidue: zero, exactSignedAmountText: '0.01', exactSignedResidueText: '0' } } }
  return { approval, shipmentId: id, movementId: id, movementLineNumber: 1, productId: id, physicalQuantityKg: 1,
    signedAmount: '0.01', exactSignedAmount: ratio, exactSignedResidue: zero, fingerprint: 'c'.repeat(64),
    candidates: [{ companyId: COMPANY_A, sourceEventId: id, invoiceLineId: line, acceptanceId: id, receiptMovementId: line,
      rootLabelId: line, productId: id, invoicedRootShare: { numerator: '1', denominator: '1' }, invoicedNetQuantityKg: { numerator: '1', denominator: '1' },
      exactTlAmount: ratio, sourceProofSha256: approval.sourceProofSha256, physicalSourceSha256: 'd'.repeat(64), fingerprint: 'e'.repeat(64),
      shipment: { shipmentId: id, movementId: id, movementLineNumber: 1, loadingInstructionLineId: line, productId: id, labelId: id,
        physicalQuantityKg: 1, rootShare: { numerator: '1', denominator: '1' }, netRootQuantityKg: { numerator: '1', denominator: '1' },
        recordedAtUtc: at, provenOriginalSequence: null, returns: [], physicalProofJson: '{"synthetic":true}' } }] }
}

describe('actual first purchase posted share proof', () => {
  it('keeps actual purchase identities and refuses foreign, self-approved, mismatched or missing evidence', () => {
    const value = source()
    expect(postedPurchaseCostShare(value, COMPANY_A, id)).toBe(true)
    expect(postedPurchaseCostShare(value, COMPANY_B, id)).toBe(false)
    expect(postedPurchaseCostShare({ ...value, approval: { ...value.approval, approvedBy: PREPARER } }, COMPANY_A, id)).toBe(false)
    expect(postedPurchaseCostShare({ ...value, approval: { ...value.approval, journalId: null } }, COMPANY_A, id)).toBe(false)
    expect(postedPurchaseCostShare({ ...value, candidates: [] }, COMPANY_A, id)).toBe(false)
    expect(postedPurchaseCostShare({ ...value, candidates: [value.candidates[0], value.candidates[0]] }, COMPANY_A, id)).toBe(false)
    expect(postedPurchaseCostShare({ ...value, candidates: [{ ...value.candidates[0], invoiceLineId: id }] }, COMPANY_A, id)).toBe(false)
  })
  it('shows a linked purchase cost without a new proposal journal and keeps zero approval distinct from missing evidence', () => {
    const value = source()
    const body = { proposalId: id, companyId: COMPANY_A, eventId: id, kind: 1, invoiceId: id, invoiceVersion: 1, admissionId: id,
      invoiceDate: '2026-11-17', officialInvoiceNumber: 'SENTETIK', version: 1, status: 'AlreadyAccounted', sourceOccurredAtUtc: at,
      exactTlAmount: '0.01', component: { componentKey: `invoice-line:${line}`, productId: id, invoiceLineId: line, invoiceLineNumber: 1,
        locationId: null, signedTlAmount: ratio, sourceProofSha256: 'a'.repeat(64), alreadyPostedPurchaseComponents: [value] },
      preparation: null, approvedBy: null, approvedAtUtc: null, journalId: null, cancellationId: null, reversalProposal: null }
    expect(decodeInventoryCost(body, COMPANY_A, 1)?.component.alreadyPostedPurchaseComponents?.[0].approval.journalId).toBe(JOURNAL)
    expect(decodeInventoryCost({ ...body, component: { ...body.component, alreadyPostedPurchaseComponents: undefined } }, COMPANY_A, 1)).toBeNull()
    const rounded = { ...value, signedAmount: '0', approval: { ...value.approval, status: 'RoundedNoCost', journalId: null, signedAmount: '0',
      recordedRounding: { ...value.approval.recordedRounding, rounding: { ...value.approval.recordedRounding.rounding, roundedSignedAmount: '0' } } } }
    expect(postedPurchaseCostShare(rounded, COMPANY_A, id)).toBe(true)
    expect(postedPurchaseCostShare({ ...rounded, approval: { ...rounded.approval, journalId: JOURNAL } }, COMPANY_A, id)).toBe(false)
  })
})
