import { validCogsProposalWire, validCogsSourceReference, validCogsCorrelatedEvidence, type CogsProposal, type CogsHistory } from '../accounting/invoiceLineCogsApi'
import { validPostedManufacturingCostShare, type CostProofWireValidation } from '../accounting/postedManufacturingCostProof'
import { validPostedPurchaseCostShare } from '../accounting/postedPurchaseCostProof'
import { hasExactKeys } from '../finance/wireShape'
import { isBoundUuid } from '../finance/contract'
import { transferDispatchUtc } from '../inventory/transferDispatchApi'

// Source-ready transport only; published kinds, routes and capabilities are paired separately.
export const STAGED_INVOICE_LINE_COGS_PURPOSE = {
  kind:'accounting.invoice-line-cogs-correction.v1', capabilityId:'accounting.invoice-line-cogs-correction.work-decision',
  routePath:'/onaylarim/accounting.invoice-line-cogs-correction.v1/:approvalId',
} as const
export const INVOICE_LINE_COGS_WORK_KIND = STAGED_INVOICE_LINE_COGS_PURPOSE.kind
export type WorkInvoiceLineCogsProposal = CogsProposal<string>
export type WorkInvoiceLineCogsSource = Omit<CogsHistory['source'],'finalizedVersion'> & {readonly finalizedVersion:string}
export interface WorkInvoiceLineCogsPreview {
  readonly proposal:WorkInvoiceLineCogsProposal; readonly source:WorkInvoiceLineCogsSource; readonly currentLineVersion:string
  readonly sourceFactsDigest:string; readonly previewDigest:string
}
const object=(v:unknown):v is Record<string,unknown>=>typeof v==='object'&&v!==null&&!Array.isArray(v)
const guid=(v:unknown):v is string=>typeof v==='string'&&v.length===36&&isBoundUuid(v)
const same=(v:unknown,id:string)=>typeof v==='string'&&v.toLowerCase()===id.toLowerCase()
const digest=(v:unknown):v is string=>typeof v==='string'&&v.length===64&&/^[0-9A-F]{64}$/.test(v)&&v===v.trim()
export const cogsWorkLong=(v:unknown,minimum=1):v is string=>typeof v==='string'&&v===v.trim()&&/^(0|[1-9][0-9]*)$/.test(v)
  &&v.length<=19&&BigInt(v)>=BigInt(minimum)&&BigInt(v)<=9223372036854775807n
const utc=(v:unknown):v is string=>transferDispatchUtc(v)&&v===v.trim()
  &&/^\d{4}-\d{2}-\d{2}T(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(v)
// Close native regex end anchors at the Work boundary, while preserving every validated raw character.
const hashFields=new Set(['sourceSha256','sourceFingerprint','fingerprint','sourcePayloadSha256','lineageSha256','historySha256','sourceProofSha256','physicalSourceSha256','attributionFingerprint'])
const numberTextFields=new Set(['amount','journalTotal','signedAmount','signedRoundingResidue','currentJournalTotal','roundedSignedAmount','numerator','denominator','generationVersion'])
const int32Fields=new Set(['dispatchNoteLineNo','invoiceLineNo','cogsJournalLineNumber','counterJournalLineNumber','movementLineNumber','moneyScale'])
function completeWire(value:unknown):boolean {
  if(Array.isArray(value))return value.every(completeWire)
  if(!object(value))return true
  return Object.entries(value).every(([key,v])=>{
    if(v!==null&&(key.endsWith('Id')||key==='preparedBy'||key==='approvedBy'||key==='ettn')&&!guid(v))return false
    if(hashFields.has(key)&&(typeof v!=='string'||v.length!==64||v!==v.trim()))return false
    if(numberTextFields.has(key)&&(typeof v!=='string'||v!==v.trim()))return false
    if(int32Fields.has(key)&&(typeof v!=='number'||!Number.isInteger(v)||v<(key==='moneyScale'?0:1)||v>2147483647))return false
    return completeWire(v)
  })
}
export function validInvoiceLineCogsPreview(value:Record<string,unknown>,company:string,id:string,date:(v:unknown)=>v is string):value is Record<string,unknown>&{readonly invoiceLineCogs:WorkInvoiceLineCogsPreview} {
  const human=value.invoiceLineCogs
  if(!object(human)||!hasExactKeys(human,['proposal','source','currentLineVersion','sourceFactsDigest','previewDigest'])
    ||!object(human.proposal)||!object(human.source)||!digest(human.sourceFactsDigest)||!digest(human.previewDigest)
    ||!cogsWorkLong(human.currentLineVersion,0)||!completeWire(human))return false
  const p=human.proposal,s=human.source
  if(!guid(p.invoiceId)||!guid(p.invoiceLineId)||!guid(p.proposalId)||!same(p.proposalId,id))return false
  const validDate=(v:unknown)=>date(v)&&v!=='0001-01-01'
  const wire:CostProofWireValidation={long:cogsWorkLong,date:validDate,utc,allowEmptyNoReturns:true}
  if(!validCogsProposalWire(p,company,p.invoiceId,p.invoiceLineId,{...wire,instant:utc,exactVersion:(v,n)=>v===String(n),
      manufacturingShare:(v,c)=>validPostedManufacturingCostShare(v,c,undefined,wire),
      purchaseShare:(v,c,product)=>validPostedPurchaseCostShare(v,c,product,wire)})
    ||!validCogsSourceReference(s,company,p.invoiceId,p.invoiceLineId,wire)||!validCogsCorrelatedEvidence(p,s)
    ||p.finalizedVersion!==s.finalizedVersion||p.version!==value.sourceVersion
    ||!cogsWorkLong(p.expectedLineVersion,0)||BigInt(human.currentLineVersion)<BigInt(p.expectedLineVersion)
    ||p.status==='Approved'&&(p.expectedLineVersion==='9223372036854775807'||BigInt(human.currentLineVersion)<BigInt(p.expectedLineVersion)+1n)
    ||value.pending!==(p.status==='Prepared'&&human.currentLineVersion===p.expectedLineVersion)
    ||value.approvalReasonRequired!==false||value.rejectionSupported!==false
    ||typeof p.reason!=='string'||p.reason!==p.reason.trim()||p.reason.length===0||p.reason.length>1000
    ||object(p.postedEvidence)&&p.postedEvidence.finalizedVersion!==p.finalizedVersion)return false
  if(value.documentLabel!==id.toLowerCase().slice(0,8)||!Array.isArray(value.rules)||value.rules.length!==1
    ||!object(value.rules[0])||value.rules[0].ruleCode!=='accounting.invoice-line-cogs-correction-approval-required'
    ||[value.rules[0].threshold,value.rules[0].actual,value.rules[0].deviation,value.rules[0].unit].some(v=>v!==null)
    ||!Array.isArray(value.references)||value.references.length!==2)return false
  const refs=[['invoice',p.invoiceId],['invoice-line',p.invoiceLineId]] as const
  const references=value.references
  return refs.every(([role,recordId],index)=>{const ref=references[index]
    return object(ref)&&ref.role===role&&same(ref.recordId,recordId)&&ref.label===recordId.toLowerCase().slice(0,8)&&ref.link===null})
}
