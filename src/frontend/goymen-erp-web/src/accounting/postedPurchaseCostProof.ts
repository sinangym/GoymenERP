import { isDateOnly } from '../finance/dateOnly'

export interface PostedPurchaseCostShare {
  readonly approval: { readonly companyId: string; readonly sourceEventId: string; readonly invoiceLineId: string
    readonly proposalId: string; readonly proposalVersion: number; readonly journalId: string | null; readonly status: 'Posted' | 'RoundedNoCost'
    readonly signedAmount: string; readonly currencyId: string; readonly moneyProfileId: string; readonly moneyProfileVersion: number
    readonly moneyScale: number; readonly cogsAccountId: string; readonly counterAccountId: string; readonly accountingDate: string
    readonly preparedBy: string; readonly approvedBy: string; readonly approvedAtUtc: string; readonly sourceProofSha256: string; readonly fingerprint: string }
  readonly shipmentId: string; readonly movementId: string; readonly movementLineNumber: number; readonly productId: string
  readonly physicalQuantityKg: number; readonly signedAmount: string; readonly fingerprint: string
}
const rec = (v: unknown): v is Record<string, unknown> => v !== null && typeof v === 'object' && !Array.isArray(v)
const guid = (v: unknown): v is string => typeof v === 'string' && /^[a-f0-9]{8}(-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i.test(v) && !/^0{8}(-0{4}){3}-0{12}$/.test(v)
const same = (v: unknown, expected: unknown) => guid(v) && guid(expected) && v.toLowerCase() === expected.toLowerCase()
const hash = (v: unknown) => typeof v === 'string' && /^[a-f0-9]{64}$/i.test(v)
const integer = (v: unknown) => Number.isSafeInteger(v) && (v as number) > 0
const decimal = (v: unknown): v is string => typeof v === 'string' && /^(0|[1-9][0-9]*)(\.[0-9]{0,27}[1-9])?$/.test(v)
const utc = (v: unknown) => typeof v === 'string' && /(?:Z|\+00:00)$/.test(v) && Number.isFinite(Date.parse(v))
const ratio = (v: unknown) => rec(v) && typeof v.numerator === 'string' && /^-?(0|[1-9][0-9]*)$/.test(v.numerator)
  && v.numerator !== '-0' && typeof v.denominator === 'string' && /^[1-9][0-9]*$/.test(v.denominator)
const scale = (v: unknown) => Number.isSafeInteger(v) && (v as number) >= 0 && (v as number) <= 28

// Identity/shape gate only: source exact amounts, sums and remainder allocation are never recalculated in the UI.
export function postedPurchaseCostShare(v: unknown, company: string, product: string): v is PostedPurchaseCostShare {
  if (!rec(v) || !rec(v.approval)) return false
  const a = v.approval
  if (!same(a.companyId, company) || !['sourceEventId', 'invoiceLineId', 'proposalId', 'currencyId', 'moneyProfileId', 'cogsAccountId', 'counterAccountId', 'preparedBy', 'approvedBy'].every(k => guid(a[k]))
    || !integer(a.proposalVersion) || (a.proposalVersion as number) < 3 || !integer(a.moneyProfileVersion) || !scale(a.moneyScale)
    || !decimal(a.signedAmount) || same(a.cogsAccountId, a.counterAccountId) || same(a.preparedBy, a.approvedBy)
    || typeof a.accountingDate !== 'string' || !isDateOnly(a.accountingDate) || !utc(a.approvedAtUtc)
    || !hash(a.sourceProofSha256) || !hash(a.fingerprint)
    || (a.status === 'Posted' ? !guid(a.journalId) || a.signedAmount === '0'
      : a.status !== 'RoundedNoCost' || a.journalId !== null || a.signedAmount !== '0' || v.signedAmount !== '0')) return false
  const recorded = a.recordedRounding
  if (!rec(recorded) || !same(recorded.currencyId, a.currencyId) || !guid(recorded.moneyProfileId) || !integer(recorded.moneyProfileVersion)
    || !scale(recorded.moneyScale) || !rec(recorded.rounding) || recorded.rounding.ruleVersion !== 'accounting.inventory-cost-component-total.v1'
    || recorded.rounding.roundedSignedAmount !== a.signedAmount || !ratio(recorded.rounding.exactSignedAmount)
    || !ratio(recorded.rounding.exactSignedResidue)) return false
  if (!guid(v.shipmentId) || !guid(v.movementId) || !integer(v.movementLineNumber) || !same(v.productId, product)
    || typeof v.physicalQuantityKg !== 'number' || !Number.isFinite(v.physicalQuantityKg) || v.physicalQuantityKg <= 0
    || !decimal(v.signedAmount) || !ratio(v.exactSignedAmount) || !ratio(v.exactSignedResidue) || !hash(v.fingerprint)
    || !Array.isArray(v.candidates) || v.candidates.length === 0 || v.candidates.length > 10000) return false
  const identities = new Set<string>()
  for (const c of v.candidates) {
    if (!rec(c) || !same(c.companyId, company) || !same(c.sourceEventId, a.sourceEventId) || !same(c.invoiceLineId, a.invoiceLineId)
      || !same(c.productId, product) || !guid(c.acceptanceId) || !guid(c.receiptMovementId) || !guid(c.rootLabelId)
      || !ratio(c.invoicedRootShare) || !ratio(c.invoicedNetQuantityKg) || !ratio(c.exactTlAmount)
      || c.sourceProofSha256 !== a.sourceProofSha256 || !hash(c.physicalSourceSha256) || !hash(c.fingerprint) || !rec(c.shipment)) return false
    const s = c.shipment
    if (!same(s.shipmentId, v.shipmentId) || !same(s.movementId, v.movementId) || s.movementLineNumber !== v.movementLineNumber
      || !same(s.productId, product) || s.physicalQuantityKg !== v.physicalQuantityKg || !guid(s.loadingInstructionLineId) || !guid(s.labelId)
      || !ratio(s.rootShare) || !ratio(s.netRootQuantityKg) || !utc(s.recordedAtUtc)
      || s.provenOriginalSequence !== null && !integer(s.provenOriginalSequence)
      || !Array.isArray(s.returns) || s.returns.length > 65536 || typeof s.physicalProofJson !== 'string' || s.physicalProofJson.length === 0) return false
    const key = c.acceptanceId.toLowerCase()
    if (identities.has(key)) return false
    identities.add(key)
    const returnIds = new Set<string>()
    for (const r of s.returns) {
      if (!rec(r) || !guid(r.returnId) || !guid(r.movementId) || !integer(r.movementLineNumber) || !same(r.sourceIssueMovementId, v.movementId)
        || typeof r.quantityKg !== 'number' || !Number.isFinite(r.quantityKg) || r.quantityKg <= 0 || !utc(r.recordedAtUtc)
        || returnIds.has(r.returnId.toLowerCase())) return false
      returnIds.add(r.returnId.toLowerCase())
    }
  }
  return true
}
