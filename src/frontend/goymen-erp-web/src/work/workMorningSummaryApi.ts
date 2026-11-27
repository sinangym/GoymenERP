import { readJson } from '../settings/discountLimitApi'
import { hasExactKeys } from '../finance/wireShape'
import { isBoundUuid } from '../finance/contract'
import type { ViewOutcome } from '../shell/uiState'
import type { WorkInboxKind } from './collaborationContract'

export const MORNING_SUMMARY_PATH = '/work/morning-summary'
// Existing Work notification classes, not a new notification catalogue or schedule.
export const MORNING_DIGEST_KINDS = ['task-assigned','allocation-released','purchase-supply-revived','accounting-cost-proposal-stale'] as const satisfies readonly WorkInboxKind[]
export type MorningDigestKind = typeof MORNING_DIGEST_KINDS[number]
export interface WorkMorningSummary { readonly companyId: string; readonly digestId: string; readonly slotUtc: string; readonly scheduleVersion: string; readonly counts: readonly { readonly kind: MorningDigestKind; readonly count: number }[] }
const object=(v: unknown): v is Record<string,unknown> => typeof v==='object' && v!==null && !Array.isArray(v)
const uuid=(v: unknown): v is string => typeof v==='string' && v.length===36 && isBoundUuid(v)
export function decodeMorningSummary(v: unknown,company: string): WorkMorningSummary|null {
 if(!object(v) || !hasExactKeys(v,['companyId','digestId','slotUtc','scheduleVersion','counts']) || !uuid(v.companyId) || v.companyId.toLowerCase()!==company.toLowerCase() || !uuid(v.digestId)
  || typeof v.slotUtc!=='string' || v.slotUtc!==v.slotUtc.trim() || v.slotUtc.startsWith('0000') || !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(v.slotUtc)
  || !Number.isFinite(Date.parse(v.slotUtc)) || new Date(v.slotUtc).toISOString().slice(0,19)!==v.slotUtc.slice(0,19)
  || typeof v.scheduleVersion!=='string' || !v.scheduleVersion.trim() || v.scheduleVersion.length>128 || !Array.isArray(v.counts) || v.counts.length>MORNING_DIGEST_KINDS.length)return null
 const kinds=new Set<string>()
 // Validate the native 100-notification page bound only; no total is returned or displayed.
 let remaining=100
 for(const item of v.counts){
  if(!object(item) || !hasExactKeys(item,['kind','count']) || typeof item.kind!=='string' || !(MORNING_DIGEST_KINDS as readonly string[]).includes(item.kind) || kinds.has(item.kind)
   || typeof item.count!=='number' || !Number.isInteger(item.count) || item.count<=0 || item.count>remaining)return null
  kinds.add(item.kind);remaining-=item.count
 }
 return v as unknown as WorkMorningSummary
}
export async function fetchMorningSummary(company: string,signal?: AbortSignal): Promise<ViewOutcome<WorkMorningSummary|null>> {
 const result=await readJson(MORNING_SUMMARY_PATH,company,v=>decodeMorningSummary(v,company),signal)
 return result.type==='http' && result.status===204 ? {type:'ok-single',data:null} : result
}
