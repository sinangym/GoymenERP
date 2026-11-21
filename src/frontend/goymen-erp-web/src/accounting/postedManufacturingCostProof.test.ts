import { describe, expect, it } from 'vitest'
import { postedManufacturingCostShare } from './postedManufacturingCostProof'
import { decodeInventoryCost } from './inventoryCostApi'
import { decodeCogsHistory, decodeCogsProposal } from './invoiceLineCogsApi'

const id = (suffix: string) => `c0871000-0000-4000-8000-${suffix.padStart(12, '0')}`
const company = id('1'); const product = id('2'); const line = id('3')
const share = () => ({ companyId: company, sourceEventId: id('4'), outputId: id('5'), generationId: id('6'), generationVersion: '1',
  receiptMovementId: id('7'), journalId: id('8'), currencyId: id('9'), cogsAccountId: id('10'), counterAccountId: id('11'),
  journalTotal: '100.01', signedAmount: '33.34', exactSignedAmount: { numerator: '10001', denominator: '300' },
  signedRoundingResidue: '0', moneyProfileId: id('12'), moneyProfileVersion: 2, moneyScale: 2,
  profileAccountingDate: '2026-11-17', sourcePayloadSha256: 'A'.repeat(64), lineageSha256: 'b'.repeat(64), historySha256: 'c'.repeat(64), fingerprint: 'D'.repeat(64),
  shipment: { shipmentId: id('13'), movementId: id('14'), movementLineNumber: 1, productId: product, physicalQuantityKg: 1, recordedAtUtc: '2026-11-17T09:00:00Z' } })
const proposal = () => ({ proposalId: id('15'), companyId: company, eventId: id('16'), kind: 1, invoiceId: id('17'), invoiceVersion: 3,
  admissionId: id('18'), invoiceDate: '2026-11-17', officialInvoiceNumber: 'SENTETIK-001', version: 1, status: 'AlreadyAccounted',
  sourceOccurredAtUtc: '2026-11-17T09:00:00Z', exactTlAmount: '33.34',
  component: { componentKey: `invoice-line:${line}`, productId: product, invoiceLineId: line, invoiceLineNumber: 1, locationId: null,
    signedTlAmount: { numerator: '1667', denominator: '50' }, sourceProofSha256: 'a'.repeat(64), alreadyPostedComponents: [share()] },
  preparation: null, approvedBy: null, approvedAtUtc: null, journalId: null, cancellationId: null, reversalProposal: null })

describe('existing posted manufacturing attribution proof', () => {
  it('shows the mixed source total separately from its sole new journal amount and rejects a doubled preparation', () => {
    const value = proposal()
    const mixed = { ...value, version: 2, status: 'Prepared', exactTlAmount: '58.465', unpostedTlAmountText: '25.125',
      component: { ...value.component, signedTlAmount: { numerator: '11693', denominator: '200' }, unpostedTlAmount: { numerator: '201', denominator: '8' } },
      preparation: { periodId: id('40'), accountingDate: '2026-11-17', transactionDate: '2026-11-17', currencyId: id('9'),
        signedAmount: '25.13', moneyProfileId: id('12'), moneyProfileVersion: 2, moneyScale: 2, preparedBy: id('41'),
        preparedAtUtc: '2026-11-17T10:00:00Z', reason: 'SENTETIK unposted original cost',
        classification: { classification: 1, accountId: id('10'), origin: 'Product', productVersion: '1', classificationVersion: '1', mappingVersion: 1, invoiceLineRevision: 0 },
        counterAccount: { accountId: id('11'), code: '153', name: 'SENTETIK' }, accountPlanPackageId: id('42'),
        accountPlanPackageVersion: 1, accountPlanCatalogVersion: 1, accountPlanSourceVersion: 'SENTETIK', accountPlanSha256: 'a'.repeat(64),
        rounding: { exactSignedAmount: { numerator: '201', denominator: '8' }, roundedSignedAmount: '25.13', exactSignedResidue: { numerator: '-1', denominator: '200' },
          exactSignedAmountText: '25.125', exactSignedResidueText: '-0.005', ruleVersion: 'accounting.inventory-cost-component-total.v1' } } }
    expect(decodeInventoryCost(mixed, company, 1)?.unpostedTlAmountText).toBe('25.125')
    expect(decodeInventoryCost({ ...mixed, preparation: { ...mixed.preparation, signedAmount: '58.47' } }, company, 1)).toBeNull()
    expect(decodeInventoryCost({ ...mixed, unpostedTlAmountText: '58.465' }, company, 1)).toBeNull()
    expect(decodeInventoryCost({ ...mixed, component: { ...mixed.component, unpostedTlAmount: undefined } }, company, 1)).toBeNull()
    expect(decodeInventoryCost({ ...mixed, component: { ...mixed.component, alreadyPostedComponents: [] } }, company, 1)).toBeNull()
    const posted = { ...mixed, version: 3, status: 'Posted', approvedBy: id('43'), approvedAtUtc: '2026-11-17T11:00:00Z', journalId: id('44') }
    expect(decodeInventoryCost(posted, company, 1)?.journalId).toBe(id('44'))
    expect(decodeInventoryCost({ ...posted, status: 'MixedCancellationPending', cancellationId: id('45') }, company, 1)?.status).toBe('MixedCancellationPending')
    expect(decodeInventoryCost({ ...posted, status: 'AlreadyAccounted' }, company, 1)).toBeNull()
    expect(decodeInventoryCost({ ...posted, status: 'MixedCancellationPending' }, company, 1)).toBeNull()
  })
  it('requires explicit same-issue return evidence while keeping legacy no-return sources', () => {
    const original = share()
    const returned = { returnId: id('70'), movementId: id('71'), movementLineNumber: 1,
      sourceIssueMovementId: original.shipment.movementId, quantityKg: 0.5, recordedAtUtc: '2026-11-17T10:00:00Z' }
    const value = { ...original, shipment: { ...original.shipment, hasReturns: true, returns: [returned] } }
    expect(postedManufacturingCostShare(value, company, product)).toBe(true)
    for (const returns of [undefined, [], [returned, returned], [{ ...returned, sourceIssueMovementId: id('72') }]]) {
      expect(postedManufacturingCostShare({ ...value, shipment: { ...value.shipment, returns } }, company, product)).toBe(false)
    }
    expect(postedManufacturingCostShare(original, company, product)).toBe(true)
    expect(postedManufacturingCostShare({ ...value, shipment: { ...value.shipment, hasReturns: false } }, company, product)).toBe(false)
    expect(postedManufacturingCostShare({ ...value, shipment: { ...value.shipment, movementId: undefined } }, company, product)).toBe(false)
  })
  it('displays the actual posted share without claiming a new approval or journal', () => {
    expect(postedManufacturingCostShare(share(), company, product)).toBe(true)
    expect(decodeInventoryCost(proposal(), company, 1)?.status).toBe('AlreadyAccounted')
    expect(decodeInventoryCost({ ...proposal(), journalId: id('8') }, company, 1)).toBeNull()
    expect(decodeInventoryCost({ ...proposal(), status: 'Posted' }, company, 1)).toBeNull()
  })
  it('refuses missing, repeated, foreign, or incomplete physical evidence', () => {
    const value = proposal()
    for (const alreadyPostedComponents of [[], [share(), share()], [{ ...share(), companyId: id('90') }],
      [{ ...share(), fingerprint: null }], [{ ...share(), shipment: { ...share().shipment, productId: id('91') } }]]) {
      expect(decodeInventoryCost({ ...value, component: { ...value.component, alreadyPostedComponents } }, company, 1)).toBeNull()
    }
  })
  it('rejects an impossible profile date and the same account written in different casing', () => {
    expect(postedManufacturingCostShare({ ...share(), profileAccountingDate: '2026-02-30' }, company)).toBe(false)
    expect(postedManufacturingCostShare({ ...share(), counterAccountId: share().cogsAccountId.toUpperCase() }, company)).toBe(false)
  })
  it('keeps unresolved aggregate cancellation distinct from a successful reversal', () => {
    expect(decodeInventoryCost({ ...proposal(), status: 'AttributedCancellationPending' }, company, 1)).toBeNull()
    expect(decodeInventoryCost({ ...proposal(), status: 'AttributedCancellationPending', cancellationId: id('19') }, company, 1)?.status)
      .toBe('AttributedCancellationPending')
    expect(decodeInventoryCost({ ...proposal(), status: 'CancelledByReversal', cancellationId: id('19') }, company, 1)).toBeNull()
  })
  it('binds each correction to its frozen posted component and current journal identity', () => {
    const invoice = id('17'); const evidence = { source: share(), currentJournalId: id('8'), currentCogsAccountId: id('10'),
      counterAccountId: id('11'), currentJournalTotal: '100.01' }
    const correction = { attributionFingerprint: share().fingerprint, sourceJournalId: id('8'),
      reversingCorrectionJournalId: id('30'), replacementCorrectionJournalId: id('31') }
    const value = { proposalId: id('32'), companyId: company, invoiceId: invoice, invoiceLineId: line, finalizedVersion: 3,
      version: 2, expectedLineVersion: 0, status: 'Approved', classification: 2,
      accountPlan: { packageId: id('33'), packageVersion: 2, catalogVersion: 1, sourceVersion: 'SENTETIK-1', sourceSha256: 'A'.repeat(64),
        account: { accountId: id('34'), code: '620', name: 'SENTETIK new account' } },
      periodId: id('35'), transactionDate: '2026-11-17', accountingDate: '2026-11-17', valueDate: null,
      preparedBy: id('36'), preparedAtUtc: '2026-11-17T09:00:00Z', approvedBy: id('37'), approvedAtUtc: '2026-11-17T10:00:00Z',
      reason: 'SENTETIK correction', postedEvidence: null, reversingCorrectionJournalId: null, replacementCorrectionJournalId: null,
      attributedEvidence: [evidence], attributedCorrections: [correction] }
    expect(decodeCogsProposal(value, company, invoice, line)?.attributedCorrections?.[0].replacementCorrectionJournalId).toBe(id('31'))
    expect(decodeCogsProposal({ ...value, attributedCorrections: [{ ...correction, sourceJournalId: id('99') }] }, company, invoice, line)).toBeNull()
    expect(decodeCogsProposal({ ...value, attributedCorrections: [{ ...correction, attributionFingerprint: 'f'.repeat(64) }] }, company, invoice, line)).toBeNull()
    expect(decodeCogsProposal({ ...value, attributedEvidence: [{ ...evidence, source: { ...share(), companyId: id('99') } }] }, company, invoice, line)).toBeNull()
    const normal = { companyId: company, invoiceId: invoice, invoiceLineId: line, finalizedVersion: 3,
      journalId: id('50'), cogsJournalLineNumber: 1, counterJournalLineNumber: 2, cogsAccountId: id('10'), counterAccountId: id('11'),
      currencyId: id('9'), amount: '25.13', sourceFingerprint: 'A'.repeat(64) }
    const mixed = { ...value, postedEvidence: normal, reversingCorrectionJournalId: id('51'), replacementCorrectionJournalId: id('52') }
    expect(decodeCogsProposal(mixed, company, invoice, line)?.attributedCorrections).toHaveLength(1)
    expect(decodeCogsProposal({ ...mixed, postedEvidence: null }, company, invoice, line)).toBeNull()
    const history = { companyId: company, invoiceId: invoice, invoiceLineId: line, lineVersion: 0, current: null, revisions: [], proposals: [],
      source: { companyId: company, invoiceId: invoice, invoiceLineId: line, finalizedVersion: 3, finalizationAdmissionId: id('18'), productId: product,
        shipmentId: id('13'), dispatchNoteId: id('53'), dispatchNoteLineNo: 1, invoiceLineNo: 1, invoiceDate: '2026-11-17',
        officialInvoiceNumber: 'SENTETIK-001', ettn: id('54'), fingerprint: 'A'.repeat(64) },
      productReference: { companyId: company, productId: product, outcome: 'NotSet', classification: null, productVersion: null, classificationVersion: null },
      postingEvidenceState: 'MixedPostedCogs', postedEvidence: normal, attributedEvidence: [evidence] }
    expect(decodeCogsHistory(history, company, invoice, line)?.postingEvidenceState).toBe('MixedPostedCogs')
    expect(decodeCogsHistory({ ...history, postedEvidence: null }, company, invoice, line)).toBeNull()
    expect(decodeCogsHistory({ ...history, attributedEvidence: undefined }, company, invoice, line)).toBeNull()
    expect(decodeCogsHistory({ ...history, postingEvidenceState: 'PostedCogs' }, company, invoice, line)).toBeNull()
  })
})
