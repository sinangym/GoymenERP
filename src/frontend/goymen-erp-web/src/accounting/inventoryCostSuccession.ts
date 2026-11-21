import type { InventoryCostCancellationComponent } from './inventoryCostCancellation'

type Ratio = { readonly numerator: string; readonly denominator: string }
export interface InventoryCostClaimSuccession {
  readonly previousEventId: string; readonly previousInvoiceId: string; readonly previousInvoiceLineId: string
  readonly previousAdmissionId: string; readonly previousFinalizedVersion: number; readonly previousProposalId: string
  readonly previousProposalVersion: number; readonly previousSourceSha256: string; readonly cancellationId: string
  readonly invoiceLineRevision: number
  readonly claims: readonly { readonly previousGeneration: number; readonly previousClaimSha256: string
    readonly physical: { readonly shipmentId: string; readonly movementId: string; readonly movementLineNumber: number
      readonly productId: string; readonly quantityKg: number } }[]
  readonly reversedComponents: readonly InventoryCostCancellationComponent[]
  readonly capturedCostFingerprints: readonly string[]
  readonly originalSourceAmount: Ratio; readonly knownBaseAmount: Ratio; readonly restoredAmount: Ratio
}
const rec = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v)
const guid = (v: unknown): v is string => typeof v === 'string' && /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(v) && !/^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(v)
const hash = (v: unknown): v is string => typeof v === 'string' && /^[a-f0-9]{64}$/i.test(v)
const integer = (v: unknown, min = 1) => Number.isSafeInteger(v) && (v as number) >= min
const ratio = (v: unknown): v is Ratio => rec(v) && typeof v.numerator === 'string' && /^(0|[1-9][0-9]*)$/.test(v.numerator)
  && typeof v.denominator === 'string' && /^[1-9][0-9]*$/.test(v.denominator)
const money = (v: unknown): v is string => typeof v === 'string' && /^-?(0|[1-9][0-9]*)(\.[0-9]{0,27}[1-9])?$/.test(v) && v !== '-0'
const same = (a: unknown, b: unknown) => typeof a === 'string' && typeof b === 'string' && a.toLowerCase() === b.toLowerCase()

/** Validate identities/evidence only. No cost, allocation or rounding is computed in the browser. */
export function decodeInventoryCostSuccession(body: unknown, parent: Record<string, unknown>, component: Record<string, unknown>): InventoryCostClaimSuccession | null {
  if (!rec(body) || !['previousEventId', 'previousInvoiceId', 'previousInvoiceLineId', 'previousAdmissionId', 'previousProposalId', 'cancellationId'].every(k => guid(body[k]))
    || same(body.previousInvoiceId, parent.invoiceId) || same(body.previousEventId, parent.eventId) || same(body.previousAdmissionId, parent.admissionId)
    || !integer(body.previousFinalizedVersion) || !integer(body.previousProposalVersion) || !integer(body.invoiceLineRevision, 0)
    || !hash(body.previousSourceSha256) || !ratio(body.originalSourceAmount) || !ratio(body.knownBaseAmount) || !ratio(body.restoredAmount)
    || !rec(component.signedTlAmount) || body.restoredAmount.numerator !== component.signedTlAmount.numerator
    || body.restoredAmount.denominator !== component.signedTlAmount.denominator
    || component.alreadyPostedComponents != null || component.alreadyPostedPurchaseComponents != null || component.unpostedTlAmount != null
    || !Array.isArray(body.claims) || body.claims.length === 0 || body.claims.length > 10000
    || !Array.isArray(component.salesPhysicalClaims) || component.salesPhysicalClaims.length !== body.claims.length) return null
  const actual = component.salesPhysicalClaims as unknown[]
  if (body.claims.some(p => !rec(p) || !integer(p.previousGeneration) || !hash(p.previousClaimSha256) || !rec(p.physical)
    || !['shipmentId', 'movementId', 'productId'].every(k => guid(p.physical && (p.physical as Record<string, unknown>)[k]))
    || !same(p.physical.productId, component.productId) || !integer(p.physical.movementLineNumber)
    || typeof p.physical.quantityKg !== 'number' || !Number.isFinite(p.physical.quantityKg) || p.physical.quantityKg <= 0
    || !actual.some(a => rec(a) && rec(p.physical) && same(a.shipmentId, p.physical.shipmentId) && same(a.movementId, p.physical.movementId)
      && a.movementLineNumber === p.physical.movementLineNumber && same(a.productId, p.physical.productId) && a.quantityKg === p.physical.quantityKg))) return null
  const claims = body.claims as InventoryCostClaimSuccession['claims']
  if (new Set(claims.map(p => `${p.physical.movementId.toLowerCase()}/${p.physical.movementLineNumber}`)).size !== claims.length
    || !Array.isArray(body.capturedCostFingerprints) || body.capturedCostFingerprints.some(p => !hash(p))
    || new Set(body.capturedCostFingerprints).size !== body.capturedCostFingerprints.length
    || !Array.isArray(body.reversedComponents) || body.reversedComponents.some(p => !rec(p)
      || !hash(p.economicKey) || !hash(p.sourceFingerprint) || !['Known', 'Manufacturing', 'Purchase'].includes(p.origin as string)
      || !['journalSourceId', 'currencyId', 'cogsAccountId', 'counterAccountId'].every(k => guid(p[k]))
      || same(p.cogsAccountId, p.counterAccountId) || typeof p.postingPurpose !== 'string' || !p.postingPurpose.trim()
      || !money(p.signedAmount) || !money(p.journalTotal) || p.journalTotal.startsWith('-')
      || (p.signedAmount !== '0' ? !guid(p.sourceJournalId) || !guid(p.reversingJournalId) || same(p.sourceJournalId, p.reversingJournalId)
        : p.reversingJournalId != null || p.sourceJournalId !== null && !guid(p.sourceJournalId))
      || p.origin !== 'Known' && !(body.capturedCostFingerprints as string[]).includes(p.economicKey as string))) return null
  const parts = body.reversedComponents as InventoryCostClaimSuccession['reversedComponents']
  if (new Set(parts.map(p => p.economicKey)).size !== parts.length) return null
  return body as unknown as InventoryCostClaimSuccession
}
