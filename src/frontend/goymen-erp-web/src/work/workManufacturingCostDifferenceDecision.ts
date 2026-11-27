import type { CostDifference, CostOutput } from '../accounting/manufacturingCostDifferenceApi'
import { isBoundUuid } from '../finance/contract'
import { hasExactKeys } from '../finance/wireShape'
import { transferDispatchUtc } from '../inventory/transferDispatchApi'
import { catalogVersion, catalogIncrementable } from './workApprovedSourceCatalogDecision'

// Candidate only. Shell availability and the public mount list remain separately paired by the owner.
export const MANUFACTURING_COST_WORK_PURPOSE = {
  kind:'accounting.manufacturing-cost-difference.v1', capabilityId:'accounting.manufacturing-cost-difference.work-decision',
  routePath:'/onaylarim/accounting.manufacturing-cost-difference.v1/:approvalId',
} as const
export const MANUFACTURING_COST_WORK_KIND = MANUFACTURING_COST_WORK_PURPOSE.kind
export type WorkCostAccountPlan = Omit<NonNullable<CostDifference['accountPlan']>,'packageVersion'|'catalogVersion'> & {
  readonly packageVersion:string;readonly catalogVersion:string|null
}
export type WorkManufacturingCostDifference = Omit<CostDifference,'version'|'accountPlan'|'status'> & {
  readonly version:string;readonly status:'Prepared'|'Posted';readonly accountPlan:WorkCostAccountPlan
}
export interface WorkManufacturingCostDifferencePreview {
  readonly proposal:WorkManufacturingCostDifference;readonly preparedAtUtc:string;readonly sourceFactsDigest:string;readonly previewDigest:string
}
export const costDecisionVersion = catalogVersion
export const costDecisionIncrementable = catalogIncrementable
const object=(v:unknown):v is Record<string,unknown>=>v!==null&&typeof v==='object'&&!Array.isArray(v)
const guid=(v:unknown):v is string=>typeof v==='string'&&v.length===36&&isBoundUuid(v)
const same=(v:unknown,id:string)=>typeof v==='string'&&v.toLowerCase()===id.toLowerCase()
const text=(v:unknown):v is string=>typeof v==='string'&&v.trim()!==''
const sha=(v:unknown):v is string=>typeof v==='string'&&v.length===64&&/^[0-9a-fA-F]{64}$/.test(v)&&v===v.trim()
const digest=(v:unknown)=>sha(v)&&v===v.toUpperCase()
const utc=(v:unknown):v is string=>transferDispatchUtc(v)&&v===v.trim()
  &&/^\d{4}-\d{2}-\d{2}T(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(v)
// Native literal/ratio representations are displayed unchanged, never parsed to floating-point or summed.
const decimal=(v:unknown):v is string=>typeof v==='string'&&v===v.trim()&&/^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?$/.test(v)
const quantity=(v:unknown):v is string=>typeof v==='string'&&v===v.trim()
  &&(/^(?:0|[1-9][0-9]*)(?:\.[0-9]+)?$/.test(v)||/^[1-9][0-9]* \/ [1-9][0-9]*$/.test(v))
const outputKeys=['outputId','receiptMovementId','labelId','netQuantityKg','remainingQuantityKg','consumedQuantityKg','tlUnitCostPerKg',
  'remainingTlValue','consumedTlDifference','usdUnitCostPerKg','originalTlAmount'] as const
function outputs(v:unknown):v is readonly CostOutput[] {
  if(!Array.isArray(v)||v.length===0)return false
  const ids=new Set<string>()
  for(const row of v){
    if(!object(row)||!hasExactKeys(row,outputKeys)||!guid(row.outputId)||!guid(row.receiptMovementId)||!guid(row.labelId)
      ||!quantity(row.netQuantityKg)||!quantity(row.remainingQuantityKg)||!quantity(row.consumedQuantityKg)
      ||!decimal(row.tlUnitCostPerKg)||!decimal(row.remainingTlValue)||!decimal(row.consumedTlDifference)||!decimal(row.originalTlAmount)
      ||row.usdUnitCostPerKg!==null&&!decimal(row.usdUnitCostPerKg)||ids.has(row.outputId.toLowerCase()))return false
    ids.add(row.outputId.toLowerCase())
  }
  return true
}
export function validManufacturingCostDifferencePreview(value:Record<string,unknown>,company:string,id:string,date:(v:unknown)=>v is string):boolean {
  const h=value.manufacturingCostDifference
  if(!object(h)||!hasExactKeys(h,['proposal','preparedAtUtc','sourceFactsDigest','previewDigest'])||!object(h.proposal)
    ||!utc(h.preparedAtUtc)||!digest(h.sourceFactsDigest)||!digest(h.previewDigest))return false
  const p=h.proposal,a=p.accountPlan
  if(!hasExactKeys(p,['proposalId','companyId','manufacturingOrderId','generationId','generationVersion','status','version','signedTlDifference','outputs',
      'sourceOccurredAtUtc','periodId','accountingDate','transactionDate','debitAccountId','creditAccountId','preparedBy','approvedBy','journalId','reason','accountPlan'])
    ||!guid(p.companyId)||!same(p.companyId,company)||!guid(p.proposalId)||!same(p.proposalId,id)||!guid(p.manufacturingOrderId)||!guid(p.generationId)
    ||typeof p.generationVersion!=='string'||p.generationVersion!==p.generationVersion.trim()||!/^[1-9][0-9]*$/.test(p.generationVersion)
    ||!costDecisionVersion(p.version)||BigInt(p.version)<2n||p.version!==value.sourceVersion||!decimal(p.signedTlDifference)||!outputs(p.outputs)
    ||!utc(p.sourceOccurredAtUtc)||!guid(p.periodId)||!date(p.accountingDate)||p.accountingDate==='0001-01-01'
    ||!date(p.transactionDate)||p.transactionDate==='0001-01-01'||!guid(p.debitAccountId)||!guid(p.creditAccountId)||same(p.debitAccountId,p.creditAccountId)
    ||!guid(p.preparedBy)||!text(p.reason)||p.reason!==p.reason.trim()||p.reason.length>1000
    ||p.status!=='Prepared'&&p.status!=='Posted'||value.pending!==(p.status==='Prepared')
    ||value.approvalReasonRequired!==false||value.rejectionSupported!==false
    ||!object(a)||!hasExactKeys(a,['packageId','packageVersion','catalogVersion','sourceVersion','sha256','debit','credit'])
    ||!guid(a.packageId)||!costDecisionVersion(a.packageVersion)||a.catalogVersion!==null&&!costDecisionVersion(a.catalogVersion)
    ||!text(a.sourceVersion)||!sha(a.sha256))return false
  for(const account of [a.debit,a.credit])if(!object(account)||!hasExactKeys(account,['accountId','code','name'])||!guid(account.accountId)||!text(account.code)||!text(account.name))return false
  if(!object(a.debit)||!object(a.credit)||!same(a.debit.accountId,p.debitAccountId)||!same(a.credit.accountId,p.creditAccountId))return false
  if(p.status==='Prepared'?p.approvedBy!==null||p.journalId!==null:!guid(p.approvedBy)||same(p.approvedBy,p.preparedBy)||!guid(p.journalId))return false
  if(value.documentLabel!==id.toLowerCase().slice(0,8)||!Array.isArray(value.rules)||value.rules.length!==1||!object(value.rules[0])
    ||value.rules[0].ruleCode!=='accounting.manufacturing-cost-difference-approval-required'||value.rules[0].threshold!==null
    ||value.rules[0].deviation!==null||value.rules[0].unit!=='TRY'||value.rules[0].actual!==p.signedTlDifference
    ||!Array.isArray(value.references)||value.references.length!==4)return false
  const refs=[['manufacturing-order',p.manufacturingOrderId],['period',p.periodId],['debit-ledger-account',p.debitAccountId],['credit-ledger-account',p.creditAccountId]] as const
  const references=value.references
  return refs.every(([role,recordId],index)=>{const ref=references[index]
    return object(ref)&&ref.role===role&&same(ref.recordId,recordId)&&ref.label===recordId.toLowerCase().slice(0,8)&&ref.link===null})
}
