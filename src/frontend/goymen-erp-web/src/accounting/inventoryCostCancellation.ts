import { isDateOnly } from '../finance/dateOnly'

export interface InventoryCostCancellationComponent {
  readonly economicKey: string; readonly origin: 'Known' | 'Manufacturing' | 'Purchase'
  readonly sourceJournalId: string | null; readonly journalSourceId: string; readonly postingPurpose: string
  readonly currencyId: string; readonly journalTotal: string; readonly signedAmount: string
  readonly cogsAccountId: string; readonly counterAccountId: string; readonly sourceFingerprint: string
  readonly reversingJournalId?: string
}
export interface InventoryCostCancellation {
  readonly id: string; readonly companyId: string; readonly inventoryCostProposalId: string; readonly cancellationId: string
  readonly version: number; readonly status: 'Prepared' | 'Approved'; readonly invoiceLineRevision: number
  readonly periodId: string; readonly accountingDate: string; readonly transactionDate: string; readonly reason: string
  readonly preparedBy: string; readonly preparedAtUtc: string; readonly approvedBy: string | null; readonly approvedAtUtc: string | null
  readonly components: readonly InventoryCostCancellationComponent[]
  readonly moneyProfiles: readonly { readonly currencyId: string; readonly profileId: string; readonly version: number; readonly scale: number }[]
}
const rec = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v)
const guid = (v: unknown): v is string => typeof v === 'string' && /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(v) && v !== '00000000-0000-0000-0000-000000000000'
const same = (a: string, b: string) => a.toLowerCase() === b.toLowerCase()
const hash = (v: unknown) => typeof v === 'string' && /^[a-f0-9]{64}$/i.test(v)
const text = (v: unknown): v is string => typeof v === 'string' && v.trim() === v && v.length > 0
const decimal = (v: unknown): v is string => typeof v === 'string' && /^-?(0|[1-9][0-9]*)(\.[0-9]{0,27}[1-9])?$/.test(v) && v !== '-0'
const utc = (v: unknown) => typeof v === 'string' && /(?:Z|\+00:00)$/.test(v) && Number.isFinite(Date.parse(v))

export function decodeInventoryCostCancellation(body: unknown, company: string, proposal: string, cancellation: string,
  sourceProofSha256: string): InventoryCostCancellation | null {
  if (!rec(body) || !['id', 'companyId', 'inventoryCostProposalId', 'cancellationId', 'periodId', 'preparedBy'].every(k => guid(body[k]))
    || !same(body.companyId as string, company) || !same(body.inventoryCostProposalId as string, proposal)
    || !same(body.cancellationId as string, cancellation) || !Number.isSafeInteger(body.version) || (body.version as number) < 1
    || !Number.isSafeInteger(body.invoiceLineRevision) || (body.invoiceLineRevision as number) < 0
    || typeof body.accountingDate !== 'string' || !isDateOnly(body.accountingDate)
    || typeof body.transactionDate !== 'string' || !isDateOnly(body.transactionDate) || !text(body.reason) || !utc(body.preparedAtUtc)
    || !['Prepared', 'Approved'].includes(body.status as string)) return null
  const approved = body.status === 'Approved'
  if (approved ? !guid(body.approvedBy) || same(body.approvedBy, body.preparedBy as string) || !utc(body.approvedAtUtc) || (body.version as number) < 2
    : body.approvedBy !== null || body.approvedAtUtc !== null) return null
  if (!Array.isArray(body.moneyProfiles) || body.moneyProfiles.length === 0 || body.moneyProfiles.some(p => !rec(p)
    || !guid(p.currencyId) || !guid(p.profileId) || !Number.isSafeInteger(p.version) || (p.version as number) < 1
    || !Number.isSafeInteger(p.scale) || (p.scale as number) < 0 || (p.scale as number) > 28)
    || new Set(body.moneyProfiles.map(p => p.currencyId.toLowerCase())).size !== body.moneyProfiles.length) return null
  const profiles = body.moneyProfiles as { currencyId: string }[]
  if (!Array.isArray(body.components) || body.components.length === 0 || body.components.some(p => !rec(p)
    || !hash(p.economicKey) || !hash(p.sourceFingerprint) || !['Known', 'Manufacturing', 'Purchase'].includes(p.origin as string)
    || !guid(p.journalSourceId) || !guid(p.currencyId) || !guid(p.cogsAccountId) || !guid(p.counterAccountId)
    || same(p.cogsAccountId, p.counterAccountId) || !text(p.postingPurpose) || !decimal(p.signedAmount) || !decimal(p.journalTotal)
    || p.journalTotal.startsWith('-') || p.sourceJournalId !== null && !guid(p.sourceJournalId)
    || p.sourceJournalId === null && (p.origin !== 'Purchase' || p.signedAmount !== '0' || p.journalTotal !== '0')
    || p.origin === 'Known' && (p.sourceFingerprint !== sourceProofSha256 || p.signedAmount === '0' || p.signedAmount.startsWith('-'))
    || p.origin === 'Purchase' && p.signedAmount.startsWith('-')
    || !profiles.some(profile => same(profile.currencyId, p.currencyId as string))
    || (approved && p.signedAmount !== '0' ? !guid(p.reversingJournalId) || same(p.reversingJournalId, p.sourceJournalId as string)
      : p.reversingJournalId !== null && p.reversingJournalId !== undefined))) return null
  if (new Set(body.components.map(p => p.economicKey)).size !== body.components.length) return null
  const reversals = body.components.flatMap(p => p.reversingJournalId ? [p.reversingJournalId.toLowerCase()] : [])
  return new Set(reversals).size === reversals.length ? body as unknown as InventoryCostCancellation : null
}
