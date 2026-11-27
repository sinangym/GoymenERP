import { decodeNormalJournalDraft, type NormalJournalDraft } from '../accounting/contract'
import { isBoundUuid, isPositiveInt64Text } from '../finance/contract'
import { hasExactKeys } from '../finance/wireShape'
import { transferDispatchUtc } from '../inventory/transferDispatchApi'

export const NORMAL_JOURNAL_WORK_KIND = 'accounting.normal-journal-draft.v1'
export const NORMAL_JOURNAL_WORK_CAPABILITY = 'accounting.normal-journal-draft.work-decision'
export type WorkNormalJournalDraft = Omit<NormalJournalDraft, 'version'> & { readonly version: string }
export interface WorkNormalJournalPreview {
  readonly draft: WorkNormalJournalDraft
  readonly sourceFactsDigest: string
  readonly previewDigest: string
}
const object = (v: unknown): v is Record<string,unknown> => v !== null && typeof v === 'object' && !Array.isArray(v)
const guid = (v: unknown): v is string => typeof v === 'string' && v.length === 36 && isBoundUuid(v)
const same = (v: unknown, id: string) => typeof v === 'string' && v.toLowerCase() === id.toLowerCase()
const digest = (v: unknown) => typeof v === 'string' && v.length === 64 && /^[0-9A-F]{64}$/.test(v)
export const normalJournalVersion = (v: unknown): string | null => typeof v === 'string' && v === v.trim()
  && isPositiveInt64Text(v) ? v : null

/** Native fields/line decoder is shared; only Work's lossless version and full DateOnly representation differ. */
export function validNormalJournalPreview(value: Record<string,unknown>, company: string, id: string,
  dateValidator: (v: unknown) => v is string, amountValidator: (v: unknown) => boolean): boolean {
  const human = value.normalJournalDraft
  if (!object(human) || !hasExactKeys(human,['draft','sourceFactsDigest','previewDigest'])
    || !digest(human.sourceFactsDigest) || !digest(human.previewDigest)) return false
  const draft = decodeNormalJournalDraft(human.draft,company,normalJournalVersion,dateValidator)
  if (draft === null || !guid(draft.draftId) || !same(draft.draftId,id) || draft.version !== value.sourceVersion
    || value.pending !== (draft.status === 'Draft') || value.approvalReasonRequired !== false || value.rejectionSupported !== false
    || !guid(draft.preparedByUserId) || !guid(draft.sourceId) || !guid(draft.currencyId)
    || draft.approvedByUserId !== null && !guid(draft.approvedByUserId) || draft.periodId !== null && !guid(draft.periodId)
    || draft.postedJournalId !== null && !guid(draft.postedJournalId)
    || !transferDispatchUtc(draft.preparedAtUtc) || draft.preparedAtUtc !== draft.preparedAtUtc.trim()
    || draft.approvedAtUtc !== null && (!transferDispatchUtc(draft.approvedAtUtc) || draft.approvedAtUtc !== draft.approvedAtUtc.trim())
    || typeof draft.accountPlanSourceSha256 !== 'string' || draft.accountPlanSourceSha256.length !== 64
    || !draft.lines.every(line => guid(line.accountId) && line.amount === line.amount.trim() && amountValidator(line.amount))) return false
  if (!Array.isArray(value.rules) || !Array.isArray(value.references) || value.references.length !== 2
    || value.rules.length < 1 || value.rules.length > 2) return false
  const rule = value.rules[0], credit = value.rules.length === 2
  if (!object(rule) || rule.ruleCode !== 'accounting.normal-journal-draft-approval-required'
    || !value.rules.every(row => object(row) && row.threshold === null && row.actual === null && row.deviation === null && row.unit === null)
    || credit && (!object(value.rules[1]) || value.rules[1].ruleCode !== 'accounting.credit-journal-maker-checker')) return false
  const source = value.references[0], currency = value.references[1]
  return object(source) && object(currency) && source.role === (credit ? 'credit-movement-proposal' : 'source-record')
    && same(source.recordId,draft.sourceId) && source.link === null && currency.role === 'currency'
    && same(currency.recordId,draft.currencyId) && currency.link === null
}

