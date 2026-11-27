import { decodeJournalReversalProposal, type JournalReversalProposal } from '../accounting/journalReversalContract'
import { isBoundUuid } from '../finance/contract'
import { hasExactKeys } from '../finance/wireShape'
import { transferDispatchUtc } from '../inventory/transferDispatchApi'

export const JOURNAL_REVERSAL_WORK_KIND = 'accounting.journal-reversal.v1'
export const JOURNAL_REVERSAL_WORK_CAPABILITY = 'accounting.journal-reversal.work-decision'
export interface WorkJournalReversalPreview {
  readonly proposal: JournalReversalProposal; readonly sourceFactsDigest: string; readonly previewDigest: string
}
export const journalReversalVersion = (v: unknown): v is string => typeof v === 'string' && v === v.trim()
  && /^[1-9][0-9]{0,9}$/.test(v) && BigInt(v) <= 2147483647n
const object = (v: unknown): v is Record<string,unknown> => v !== null && typeof v === 'object' && !Array.isArray(v)
const guid = (v: unknown): v is string => typeof v === 'string' && v.length === 36 && isBoundUuid(v)
const same = (a: unknown,b: string) => typeof a === 'string' && a.toLowerCase() === b.toLowerCase()
const digest = (v: unknown) => typeof v === 'string' && v.length === 64 && /^[0-9A-F]{64}$/.test(v)
const utc = (v: unknown) => typeof v === 'string' && v === v.trim() && transferDispatchUtc(v)

export function validJournalReversalPreview(value: Record<string,unknown>,company: string,id: string,date: (v: unknown) => v is string): boolean {
  const human = value.accountingJournalReversal
  if (!object(human) || !hasExactKeys(human,['proposal','sourceFactsDigest','previewDigest'])
    || !digest(human.sourceFactsDigest) || !digest(human.previewDigest)) return false
  const p = decodeJournalReversalProposal(human.proposal,company,date)
  if (p === null || !guid(p.proposalId) || !guid(p.companyId) || !same(p.proposalId,id) || !guid(p.sourceJournalId) || !guid(p.periodId)
    || !guid(p.proposedBy) || !utc(p.proposedAtUtc) || p.decidedBy !== null && !guid(p.decidedBy)
    || p.decidedAtUtc !== null && !utc(p.decidedAtUtc) || p.reversalJournalId !== null && !guid(p.reversalJournalId)
    || p.status !== 'Pending' && p.status !== 'Approved' || p.decisionReason !== null || p.reason !== p.reason.trim()
    || p.transactionDate === '0001-01-01' || p.accountingDate === '0001-01-01' || p.valueDate === '0001-01-01'
    || p.version > 2147483647 || value.sourceVersion !== String(p.version) || !journalReversalVersion(value.sourceVersion)
    || value.pending !== (p.status === 'Pending') || value.approvalReasonRequired !== false || value.rejectionSupported !== false
    || value.documentLabel !== id.slice(0,8).toLowerCase() || !Array.isArray(value.rules) || value.rules.length !== 1
    || !object(value.rules[0]) || value.rules[0].ruleCode !== 'accounting.journal-reversal-approval-required'
    || [value.rules[0].threshold,value.rules[0].actual,value.rules[0].deviation,value.rules[0].unit].some(v=>v!==null)
    || !Array.isArray(value.references) || value.references.length !== 2) return false
  const journal = value.references[0],period = value.references[1]
  return object(journal) && journal.role === 'journal' && same(journal.recordId,p.sourceJournalId)
    && journal.label === p.sourceJournalId.slice(0,8).toLowerCase() && journal.link === `/muhasebe/fisler/${p.sourceJournalId}`
    && object(period) && period.role === 'period' && same(period.recordId,p.periodId)
    && period.label === p.periodId.slice(0,8).toLowerCase() && period.link === null
}
