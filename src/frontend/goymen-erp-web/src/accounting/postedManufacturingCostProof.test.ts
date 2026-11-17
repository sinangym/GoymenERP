import { describe, expect, it } from 'vitest'
import { postedManufacturingCostShare } from './postedManufacturingCostProof'
import { decodeInventoryCost } from './inventoryCostApi'
import { decodeCogsProposal } from './invoiceLineCogsApi'

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
  })
})
