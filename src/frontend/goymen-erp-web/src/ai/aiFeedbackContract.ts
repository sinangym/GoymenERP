import { hasExactKeys } from '../finance/wireShape'
import { isBoundUuid, isPositiveInt64Text } from '../finance/contract'
export const AI_FEEDBACK_KINDS = ['Accepted','Corrected','Rejected','Reverted'] as const
export const AI_FEEDBACK_REASONS = ['incorrect_information','incorrect_or_missing_source','incomplete_answer','outdated_information','other'] as const
export type AiFeedbackKind = typeof AI_FEEDBACK_KINDS[number]
export type AiFeedbackReason = typeof AI_FEEDBACK_REASONS[number]
export interface AiFeedbackRequest { readonly expectedVersion: string; readonly kind: AiFeedbackKind; readonly reasonCode: AiFeedbackReason | null }
export interface AiFeedbackView { readonly answerId: string; readonly version: string; readonly kind: AiFeedbackKind | null; readonly reasonCode: AiFeedbackReason | null }
export interface AiFeedbackReceipt extends AiFeedbackView { readonly kind: AiFeedbackKind; readonly originalCorrelationId: string; readonly replayed: boolean }
const record = (v: unknown): v is Record<string,unknown> => typeof v === 'object' && v !== null && !Array.isArray(v)
export const isAiFeedbackAnswerId = (v: unknown): v is string => typeof v === 'string' && v.length === 36 && isBoundUuid(v)
const version = (v: unknown): v is string => typeof v === 'string' && v === v.trim() && (v === '0' || isPositiveInt64Text(v))
const kind = (v: unknown): v is AiFeedbackKind => AI_FEEDBACK_KINDS.some(k => k === v)
const reason = (v: unknown): v is AiFeedbackReason => AI_FEEDBACK_REASONS.some(r => r === v)
const decision = (k: unknown,r: unknown) => kind(k) && (r === null ? k === 'Accepted' : reason(r))
export const aiFeedbackPath = (id: string) => `/ai/answers/${encodeURIComponent(id)}/feedback`
export function isAiFeedbackRequest(v: unknown): v is AiFeedbackRequest {
 return record(v) && hasExactKeys(v,['expectedVersion','kind','reasonCode']) && version(v.expectedVersion)
  && v.expectedVersion !== '9223372036854775807' && decision(v.kind,v.reasonCode)
}
export function decodeAiFeedbackView(v: unknown,id: string): AiFeedbackView | null {
 if (!record(v) || !hasExactKeys(v,['answerId','version','kind','reasonCode']) || !isAiFeedbackAnswerId(v.answerId)
  || v.answerId.toLowerCase() !== id.toLowerCase() || !version(v.version)
  || (v.version === '0' ? v.kind !== null || v.reasonCode !== null : !decision(v.kind,v.reasonCode))) return null
 return v as unknown as AiFeedbackView
}
export function decodeAiFeedbackReceipt(v: unknown,id: string,request: AiFeedbackRequest): AiFeedbackReceipt | null {
 if (!isAiFeedbackRequest(request) || !record(v) || !hasExactKeys(v,['answerId','version','kind','reasonCode','originalCorrelationId','replayed'])
  || !isAiFeedbackAnswerId(v.answerId) || v.answerId.toLowerCase() !== id.toLowerCase()
  || v.version !== (BigInt(request.expectedVersion)+1n).toString() || v.kind !== request.kind || v.reasonCode !== request.reasonCode
  || typeof v.replayed !== 'boolean' || typeof v.originalCorrelationId !== 'string' || v.originalCorrelationId.length === 0
  || v.originalCorrelationId.length > 128 || v.originalCorrelationId !== v.originalCorrelationId.trim()
  || /[\u0000-\u001f\u007f-\u009f]/.test(v.originalCorrelationId)) return null
 return v as unknown as AiFeedbackReceipt
}
