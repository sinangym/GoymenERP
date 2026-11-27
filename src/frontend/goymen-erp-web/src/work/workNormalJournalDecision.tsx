import { decodeNormalJournalDraft, type NormalJournalDraft } from '../accounting/contract'
import { NORMAL_JOURNAL_TEXT as N } from '../accounting/accountingText'
import { isBoundUuid, isPositiveInt64Text } from '../finance/contract'
import { hasExactKeys } from '../finance/wireShape'
import { transferDispatchUtc } from '../inventory/transferDispatchApi'
import { WORK_DECISION_TEXT as T } from './workDecisionText'

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

export function WorkNormalJournalFacts({ human }: { readonly human: WorkNormalJournalPreview }) {
  const d = human.draft
  return <section aria-label={T.normalJournalProposal}><h3>{T.normalJournalProposal}</h3><dl>
    <dt>{T.normalJournalId}</dt><dd>{d.draftId}</dd><dt>{T.importCompany}</dt><dd>{d.companyId}</dd>
    <dt>{N.status}</dt><dd>{d.status === 'Draft' ? T.normalJournalDraft : T.normalJournalApproved}</dd>
    <dt>{N.version}</dt><dd>{d.version}</dd><dt>{N.sourceRef}</dt><dd>{d.sourceId}</dd>
    <dt>{N.postingRule}</dt><dd>{d.postingPurpose} · {d.postingRuleVersion}</dd>
    <dt>{T.normalJournalAccountPlan}</dt><dd>{d.accountPlanSourceVersion}</dd><dt>{T.importCurrency}</dt><dd>{d.currencyId}</dd>
    <dt>{N.transactionDate}</dt><dd>{d.transactionDate}</dd><dt>{N.accountingDate}</dt><dd>{d.accountingDate}</dd>
    <dt>{N.valueDate}</dt><dd>{d.valueDate ?? T.packageAbsent}</dd>
    <dt>{T.packagePreparedBy}</dt><dd>{d.preparedByUserId}</dd><dt>{T.packagePreparedAt}</dt><dd>{d.preparedAtUtc}</dd>
    <dt>{T.normalJournalApprover}</dt><dd>{d.approvedByUserId ?? T.packageAbsent}</dd><dt>{T.normalJournalApprovedAt}</dt><dd>{d.approvedAtUtc ?? T.packageAbsent}</dd>
    <dt>{N.period}</dt><dd>{d.periodId ?? T.packageAbsent}</dd><dt>{T.normalJournalPosted}</dt><dd>{d.postedJournalId ?? T.packageAbsent}</dd>
  </dl><table><thead><tr><th>{N.lineNumber}</th><th>{T.normalJournalMapping}</th><th>{N.lineAccount}</th><th>{N.lineSide}</th><th>{N.amount}</th></tr></thead>
    <tbody>{d.lines.map(line => <tr key={line.lineNumber}><td>{line.lineNumber}</td><td>{line.mappingKey}</td><td>{line.accountId}</td><td>{line.side === 'Debit' ? N.debit : N.credit}</td><td>{line.amount}</td></tr>)}</tbody>
  </table></section>
}
