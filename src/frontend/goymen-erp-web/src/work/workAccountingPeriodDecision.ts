import { decodePeriod, type PeriodView } from '../accounting/contract'
import { decodePeriodClosure, decodePeriodReopening, type PeriodClosure, type PeriodReopening } from '../accounting/periodLifecycleApi'
import { isBoundUuid, isPositiveInt64Text } from '../finance/contract'
import { hasExactKeys } from '../finance/wireShape'
import { transferDispatchUtc } from '../inventory/transferDispatchApi'

export const ACCOUNTING_PERIOD_WORK_CAPABILITY = {
  'accounting.period-opening.v1': 'accounting.period-opening.work-decision',
  'accounting.period-reopening.v1': 'accounting.period-reopening.work-decision',
} as const
export type AccountingPeriodWorkKind = keyof typeof ACCOUNTING_PERIOD_WORK_CAPABILITY
export const isAccountingPeriodWorkKind = (v: string): v is AccountingPeriodWorkKind => Object.hasOwn(ACCOUNTING_PERIOD_WORK_CAPABILITY,v)
export type WorkAccountingPeriod = Omit<PeriodView,'version'> & { readonly version: string }
export interface WorkAccountingPeriodOpening { readonly period: WorkAccountingPeriod; readonly sourceFactsDigest: string; readonly previewDigest: string }
export interface WorkAccountingPeriodReopening extends WorkAccountingPeriodOpening {
  readonly reopening: PeriodReopening<string>; readonly closure: PeriodClosure<string>; readonly laterPeriodClosed: boolean
}
const object = (v: unknown): v is Record<string,unknown> => v !== null && typeof v === 'object' && !Array.isArray(v)
const guid = (v: unknown): v is string => typeof v === 'string' && v.length === 36 && isBoundUuid(v)
const same = (a: unknown,b: string) => typeof a === 'string' && a.toLowerCase() === b.toLowerCase()
export const accountingPeriodVersion = (v: unknown): string | null => typeof v === 'string' && v === v.trim() && isPositiveInt64Text(v) ? v : null
export const accountingPeriodIncrementable = (v: unknown) => accountingPeriodVersion(v) !== null && v !== '9223372036854775807'
const digest = (v: unknown) => typeof v === 'string' && v.length === 64 && /^[0-9A-F]{64}$/.test(v)
const utc = (v: unknown) => typeof v === 'string' && v === v.trim() && transferDispatchUtc(v)
const optionalUtc = (v: unknown) => v === null || utc(v)
const optionalGuid = (v: unknown) => v === null || guid(v)
const label = (p: WorkAccountingPeriod) => `${p.startsOn} / ${p.endsOn}`
const ref = (v: unknown,role: string,id: string,text: string,link: string | null) => object(v) && v.role === role && same(v.recordId,id) && v.label === text && v.link === link

export function validAccountingPeriodPreview(value: Record<string,unknown>,company: string,kind: AccountingPeriodWorkKind,id: string,
  date: (v: unknown) => boolean): boolean {
  const reopen = kind === 'accounting.period-reopening.v1'
  const human = reopen ? value.accountingPeriodReopening : value.accountingPeriodOpening
  if (!object(human) || !hasExactKeys(human,reopen ? ['period','reopening','closure','laterPeriodClosed','sourceFactsDigest','previewDigest'] : ['period','sourceFactsDigest','previewDigest'])
    || !digest(human.sourceFactsDigest) || !digest(human.previewDigest) || value.rejectionSupported !== false || value.approvalReasonRequired !== false) return false
  const p = decodePeriod(human.period,company,{readVersion:accountingPeriodVersion,date})
  if (p === null || !guid(p.periodId) || !guid(p.companyId) || !guid(p.preparedByUserId) || !utc(p.preparedAtUtc)
    || !optionalGuid(p.approvedByUserId) || !optionalGuid(p.closedByUserId) || !optionalUtc(p.approvedAtUtc) || !optionalUtc(p.closedAtUtc)
    || value.documentLabel !== label(p) || !Array.isArray(value.rules) || !Array.isArray(value.references)
    || !value.rules.every(r => object(r) && r.threshold === null && r.actual === null && r.deviation === null && r.unit === null)) return false
  if (!reopen) return same(p.periodId,id) && value.sourceVersion === p.version && value.pending === (p.status === 'Proposed')
    && !p.isOwnProposal && (value.pending !== true || accountingPeriodIncrementable(p.version)) && value.rules.length === 1
    && object(value.rules[0]) && value.rules[0].ruleCode === 'accounting.period-opening-approval-required'
    && value.references.length === 1 && ref(value.references[0],'period',p.periodId,label(p),'/muhasebe/donemler')
  const projection = {readVersion:accountingPeriodVersion,nativeChronology:false} as const
  const r = decodePeriodReopening(human.reopening,company,p.periodId,projection)
  const c = decodePeriodClosure(human.closure,company,p.periodId,projection)
  if (r === null || c === null || !same(r.id,id) || !guid(r.id) || !guid(r.closureId) || !guid(r.proposedBy)
    || !utc(r.proposedAtUtc) || !optionalGuid(r.approvedBy) || !optionalUtc(r.approvedAtUtc)
    || !guid(c.id) || !guid(c.targetPeriodId) || !guid(c.carryVoucherId) || !guid(c.closedBy) || !utc(c.closedAtUtc)
    || !optionalGuid(c.reopeningId) || !optionalGuid(c.reopenedBy) || !optionalUtc(c.reopenedAtUtc) || !digest(c.sourceHash)
    || c.lineCount > 2147483647 || !same(r.closureId,c.id) || r.closedPeriodVersion !== c.closedPeriodVersion
    || BigInt(c.closedPeriodVersion) > BigInt(p.version) || value.sourceVersion !== r.version
    || typeof human.laterPeriodClosed !== 'boolean' || value.approvalSupported !== !human.laterPeriodClosed) return false
  const pending = r.approvedBy === null && c.reopeningId === null && p.status === 'Closed' && c.closedPeriodVersion === p.version
  if (value.pending !== pending || r.version === '2' && (!same(c.reopeningId,r.id) || c.reopenedBy !== r.approvedBy || c.reopenedAtUtc !== r.approvedAtUtc)
    || pending && !accountingPeriodIncrementable(c.closedPeriodVersion)) return false
  return value.rules.length === (human.laterPeriodClosed ? 2 : 1) && object(value.rules[0])
    && value.rules[0].ruleCode === 'accounting.period-reopening-requested'
    && (!human.laterPeriodClosed || object(value.rules[1]) && value.rules[1].ruleCode === 'accounting.period-reopening-later-period-closed')
    && value.references.length === 2 && ref(value.references[0],'period',p.periodId,label(p),'/muhasebe/donemler')
    && ref(value.references[1],'period-closure',c.id,c.id.slice(0,8).toLowerCase(),null)
}
