import { describe, expect, it } from 'vitest'
import { decodeInventoryCostSuccession } from './inventoryCostSuccession'
import { keyProbes } from './exactKeysTestSupport'

const id = (n: number) => `ca000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const physical = { shipmentId: id(1), movementId: id(2), movementLineNumber: 1, productId: id(3), quantityKg: 1 }
const ratio = { numerator: '1667', denominator: '50' }
const parent = { invoiceId: id(4), eventId: id(5), admissionId: id(6) }
const component = { productId: id(3), signedTlAmount: ratio, salesPhysicalClaims: [physical] }
const proof = () => ({ previousEventId: id(7), previousInvoiceId: id(8), previousInvoiceLineId: id(9),
  previousAdmissionId: id(10), previousFinalizedVersion: 3, previousProposalId: id(11), previousProposalVersion: 1,
  previousSourceSha256: 'a'.repeat(64), cancellationId: id(12), invoiceLineRevision: 0,
  claims: [{ physical, previousGeneration: 2, previousClaimSha256: 'b'.repeat(64) }],
  capturedCostFingerprints: ['c'.repeat(64)], originalSourceAmount: ratio, knownBaseAmount: { numerator: '0', denominator: '1' }, restoredAmount: ratio,
  reversedComponents: [{ economicKey: 'c'.repeat(64), origin: 'Manufacturing', sourceJournalId: id(13), reversingJournalId: id(14),
    journalSourceId: id(15), currencyId: id(16), cogsAccountId: id(17), counterAccountId: id(18), postingPurpose: 'SENTETIK-source',
    signedAmount: '33.34', journalTotal: '100.01', sourceFingerprint: 'd'.repeat(64) }] })

describe('reinvoice actual economic claim succession proof', () => {
  it('preserves the released generation and exact restored amount without recalculating the cost', () => {
    expect(decodeInventoryCostSuccession(proof(), parent, component)).toEqual(proof())
  })
  it('refuses foreign physical identity, duplicate claims, wrong restored amount and incomplete reversal proof', () => {
    const value = proof()
    for (const invalid of [
      { ...value, previousInvoiceId: parent.invoiceId }, { ...value, previousEventId: parent.eventId },
      { ...value, claims: [{ ...value.claims[0], physical: { ...physical, movementId: id(99) } }] },
      { ...value, claims: [value.claims[0], value.claims[0]] },
      { ...value, claims: [{ ...value.claims[0], previousGeneration: 0 }] },
      { ...value, restoredAmount: { numerator: '1668', denominator: '50' } },
      { ...value, reversedComponents: [{ ...value.reversedComponents[0], reversingJournalId: null }] },
      { ...value, capturedCostFingerprints: [] },
    ]) expect(decodeInventoryCostSuccession(invalid, parent, component)).toBeNull()
    expect(decodeInventoryCostSuccession(value, parent, { ...component, alreadyPostedComponents: [{}] })).toBeNull()
  })
  it('retains an actual zero approval witness but rejects a fabricated zero reversal journal', () => {
    const zero = { numerator: '0', denominator: '1' }
    const value = { ...proof(), originalSourceAmount: zero, restoredAmount: zero,
      reversedComponents: [{ ...proof().reversedComponents[0], signedAmount: '0', journalTotal: '0', sourceJournalId: null, reversingJournalId: null }] }
    expect(decodeInventoryCostSuccession(value, parent, { ...component, signedTlAmount: zero })).not.toBeNull()
    expect(decodeInventoryCostSuccession({ ...value, reversedComponents: [{ ...value.reversedComponents[0], reversingJournalId: id(14) }] },
      parent, { ...component, signedTlAmount: zero })).toBeNull()
  })
})
describe('claim succession exact key sets', () => {
  it('rejects an unknown or missing key on the proof, a claim, its physical identity, a ratio and a reversed component', () => {
    expect(decodeInventoryCostSuccession(proof(), parent, component)).not.toBeNull()
    for (const [label, body] of keyProbes(proof(), [[[], 'invoiceLineRevision'], [['claims', 0], 'previousGeneration'],
      [['claims', 0, 'physical'], 'quantityKg'], [['restoredAmount'], 'denominator'], [['knownBaseAmount'], 'numerator'],
      [['reversedComponents', 0], 'postingPurpose']]))
      expect(decodeInventoryCostSuccession(body, parent, component), label).toBeNull()
  })
})
