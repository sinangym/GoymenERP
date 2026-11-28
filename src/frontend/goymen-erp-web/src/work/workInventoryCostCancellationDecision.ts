import { hasExactKeys } from '../finance/wireShape'
import { isBoundUuid } from '../finance/contract'
import { transferDispatchUtc } from '../inventory/transferDispatchApi'
import { inventoryCostWorkLong, validInventoryCostRetainedProposal, type WorkInventoryCostRetainedProposal, type WorkInventoryCostCancellation, type WorkInventoryCostCancellationComponent } from './workInventoryCostDecision'

export const INVENTORY_COST_CANCELLATION_WORK_CAPABILITY = 'accounting.inventory-cost-cancellation.work-decision' as const
export const INVENTORY_COST_CANCELLATION_WORK_KIND = 'accounting.inventory-cost-cancellation.v1' as const
export const INVENTORY_COST_CANCELLATION_WORK_TEXT = {
  title:'Kayıtlı maliyet paylarının iptali', original:'İptal öncesi özgün maliyet önerisi', prepared:'Onaya hazırlanan bileşen iptali', current:'Bu hazırlığın kayıtlı sonucu', source:'Kaynak faturanın iptal kaydı',
  readOnly:'Özgün öneri, iptal hazırlığı ve faturanın iptal bilgileri kayıtlı değerleriyle gösterilir.',
  retained:'Önceki maliyet payı iptalinin sonucu doğrulanmalı. Aynı istek, iki sürüm ve anahtar korunur; güncel kayıt önceki kararın kanıtı değildir.',
  sourceFields:{companyId:'Şirket',cancellationId:'İptal kaydı',documentKind:'Kaynak belge türü',sourceDocumentId:'Kaynak belge',invoiceId:'Fatura',finalizedVersion:'Kesinleşme sürümü',cancelledBy:'İptal eden',cancelledAtUtc:'İptal zamanı',reason:'Fatura iptal gerekçesi',recordedAtUtc:'Kayıt zamanı'},
  originalVersion:'Özgün öneri sürümü',cancellationVersion:'İptal hazırlığı sürümü',
  salesInvoice:'Satış faturası',
} as const
export interface WorkInventoryCostSourceCancellation {
  readonly companyId:string; readonly cancellationId:string; readonly documentKind:'sales-invoice'; readonly sourceDocumentId:string; readonly invoiceId:string
  readonly finalizedVersion:string; readonly cancelledBy:string; readonly cancelledAtUtc:string; readonly reason:string; readonly recordedAtUtc:string
}
export interface WorkInventoryCostCancellationPreview {
  readonly originalProposal:WorkInventoryCostRetainedProposal; readonly preparedCancellation:WorkInventoryCostCancellation; readonly cancellation:WorkInventoryCostCancellation
  readonly preparedRecordedAtUtc:string; readonly sourceCancellation:WorkInventoryCostSourceCancellation
}
export interface WorkInventoryCostCancellationRequest {
  readonly expectedVersion:string; readonly approve:true; readonly reason:null; readonly delegationId:string|null
  readonly inventoryCostCancellation:{readonly expectedProposalVersion:string}
}
const rec=(v:unknown):v is Record<string,unknown>=>typeof v==='object'&&v!==null&&!Array.isArray(v)
const guid=(v:unknown):v is string=>typeof v==='string'&&v.length===36&&isBoundUuid(v)
const same=(a:unknown,b:unknown)=>guid(a)&&guid(b)&&a.toLowerCase()===b.toLowerCase()
const text=(v:unknown):v is string=>typeof v==='string'&&v.length>0&&v===v.trim()
const utc=(v:unknown):v is string=>transferDispatchUtc(v)&&v===v.trim()&&/^\d{4}-\d{2}-\d{2}T(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(v)
const money=(v:unknown):v is string=>typeof v==='string'&&v===v.trim()&&/^-?(0|[1-9][0-9]*)(\.[0-9]{0,27}[1-9])?$/.test(v)&&v!=='-0'
type Check=(v:unknown)=>boolean
const shape=(v:unknown,required:Readonly<Record<string,Check>>,optional:Readonly<Record<string,Check>>={}):v is Record<string,unknown>=>rec(v)
  &&hasExactKeys(v,Object.keys(required),Object.keys(optional))&&Object.entries(required).every(([k,test])=>test(v[k]))
  &&Object.entries(optional).every(([k,test])=>!Object.hasOwn(v,k)||test(v[k]))
const long:Check=inventoryCostWorkLong, zeroLong:Check=v=>inventoryCostWorkLong(v,0)
const uniqueGuid=(rows:readonly Record<string,unknown>[],key:string)=>new Set(rows.map(row=>{const v=row[key];return typeof v==='string'?v.toLowerCase():v})).size===rows.length
function component(value:unknown,approved:boolean,nativeAmountValidator:(v:unknown,positive:boolean)=>boolean):value is WorkInventoryCostCancellationComponent {
  const amount:Check=v=>money(v)&&nativeAmountValidator(v.startsWith('-')?v.slice(1):v,false)
  if(!shape(value,{origin:v=>v==='Known'||v==='Manufacturing'||v==='Purchase',journalSourceId:guid,postingPurpose:text,currencyId:guid,journalTotal:amount,signedAmount:amount,cogsAccountId:guid,counterAccountId:guid},{sourceJournalId:guid,reversingJournalId:guid})
    ||same(value.cogsAccountId,value.counterAccountId)||typeof value.journalTotal!=='string'||value.journalTotal.startsWith('-')||typeof value.signedAmount!=='string'
    ||value.sourceJournalId===undefined&&(value.origin!=='Purchase'||value.signedAmount!=='0'||value.journalTotal!=='0')
    ||value.origin==='Known'&&(value.signedAmount==='0'||value.signedAmount.startsWith('-'))||value.origin==='Purchase'&&value.signedAmount.startsWith('-'))return false
  return approved&&value.signedAmount!=='0'?guid(value.sourceJournalId)&&guid(value.reversingJournalId)&&!same(value.sourceJournalId,value.reversingJournalId):value.reversingJournalId===undefined
}
function cancellation(value:unknown,company:string,id:string,date:(v:unknown)=>v is string,nativeAmountValidator:(v:unknown,positive:boolean)=>boolean):value is WorkInventoryCostCancellation {
  const validDate:Check=v=>date(v)&&v!=='0001-01-01'
  if(!shape(value,{id:guid,companyId:guid,inventoryCostProposalId:guid,cancellationId:guid,version:long,status:v=>v==='Prepared'||v==='Approved',invoiceLineRevision:zeroLong,periodId:guid,accountingDate:validDate,transactionDate:validDate,reason:v=>text(v)&&v.length<=1000,preparedBy:guid,preparedAtUtc:utc,components:Array.isArray,moneyProfiles:Array.isArray},{approvedBy:guid,approvedAtUtc:utc})
    ||!same(value.companyId,company)||!same(value.inventoryCostProposalId,id))return false
  const approved=value.status==='Approved'
  if(approved?!guid(value.approvedBy)||same(value.approvedBy,value.preparedBy)||!utc(value.approvedAtUtc):value.approvedBy!==undefined||value.approvedAtUtc!==undefined)return false
  if(!Array.isArray(value.components)||value.components.length===0||!value.components.every(v=>component(v,approved,nativeAmountValidator))
    ||!Array.isArray(value.moneyProfiles)||value.moneyProfiles.length===0||!value.moneyProfiles.every(v=>shape(v,{currencyId:guid,profileId:guid,version:long,scale:x=>typeof x==='number'&&Number.isInteger(x)&&x>=0&&x<=28}))
    ||!uniqueGuid(value.moneyProfiles,'currencyId'))return false
  const profiles=value.moneyProfiles
  return value.components.every(v=>profiles.some(p=>rec(p)&&same(p.currencyId,v.currencyId)))
    &&new Set(value.components.flatMap(v=>v.reversingJournalId?[v.reversingJournalId.toLowerCase()]:[])).size===value.components.filter(v=>v.reversingJournalId!==undefined).length
}
const frozenFields=['id','companyId','inventoryCostProposalId','cancellationId','invoiceLineRevision','periodId','accountingDate','transactionDate','reason','preparedBy','preparedAtUtc'] as const
function samePreparation(before:WorkInventoryCostCancellation,after:WorkInventoryCostCancellation) {
  return frozenFields.every(k=>before[k]===after[k])&&before.moneyProfiles.length===after.moneyProfiles.length
    &&before.moneyProfiles.every((p,i)=>{const q=after.moneyProfiles[i];return q!==undefined&&p.currencyId===q.currencyId&&p.profileId===q.profileId&&p.version===q.version&&p.scale===q.scale})
    &&before.components.length===after.components.length&&before.components.every((p,i)=>{const q=after.components[i];return q!==undefined
      &&(['origin','sourceJournalId','journalSourceId','postingPurpose','currencyId','journalTotal','signedAmount','cogsAccountId','counterAccountId'] as const).every(k=>p[k]===q[k])})
}
export function validInventoryCostCancellationPreview(value:Record<string,unknown>,company:string,id:string,date:(v:unknown)=>v is string,nativeAmountValidator:(v:unknown,positive:boolean)=>boolean):value is Record<string,unknown>&{readonly inventoryCostCancellation:WorkInventoryCostCancellationPreview} {
  const h=value.inventoryCostCancellation
  if(value.kind!==INVENTORY_COST_CANCELLATION_WORK_KIND||!same(value.companyId,company)||!same(value.itemId,id)||typeof value.pending!=='boolean'
    ||value.approvalReasonRequired!==false||value.rejectionSupported!==false
    ||!shape(h,{originalProposal:()=>true,preparedCancellation:()=>true,cancellation:()=>true,preparedRecordedAtUtc:utc,sourceCancellation:()=>true})
    ||!validInventoryCostRetainedProposal(h.originalProposal,company,id,date)||h.originalProposal.kind!==1
    ||h.originalProposal.cancellationId!==undefined||h.originalProposal.componentCancellation!==undefined||h.originalProposal.reversalProposal!==undefined
    ||!cancellation(h.preparedCancellation,company,id,date,nativeAmountValidator)||!cancellation(h.cancellation,company,id,date,nativeAmountValidator))return false
  const p=h.originalProposal,before=h.preparedCancellation,current=h.cancellation,s=h.sourceCancellation
  if(before.status!=='Prepared'||before.approvedBy!==undefined||before.approvedAtUtc!==undefined||value.sourceVersion!==before.version||!samePreparation(before,current))return false
  if(current.status==='Prepared'?current.version!==before.version:!inventoryCostWorkLong(before.version)||before.version==='9223372036854775807'||BigInt(current.version)!==BigInt(before.version)+1n||value.pending!==false)return false
  if(!shape(s,{companyId:guid,cancellationId:guid,documentKind:v=>v==='sales-invoice',sourceDocumentId:guid,invoiceId:guid,finalizedVersion:long,cancelledBy:guid,cancelledAtUtc:utc,reason:v=>typeof v==='string',recordedAtUtc:utc})
    ||!same(s.companyId,company)||!same(s.cancellationId,before.cancellationId)||!same(s.sourceDocumentId,p.admissionId)||!same(s.invoiceId,p.invoiceId)||s.finalizedVersion!==p.invoiceVersion)return false
  const short=(v:string)=>v.toLowerCase().slice(0,8)
  if(value.documentLabel!==short(id)||!Array.isArray(value.rules)||value.rules.length!==1||!Array.isArray(value.references)||value.references.length!==3)return false
  const rule=value.rules[0],refs=value.references
  if(!rec(rule)||!hasExactKeys(rule,['ruleCode','threshold','actual','deviation','unit'])||rule.ruleCode!=='accounting.inventory-cost-cancellation-approval-required'
    ||['threshold','actual','deviation','unit'].some(k=>rule[k]!==null))return false
  return [[id,'inventory-cost-proposal'],[before.cancellationId,'invoice-cancellation'],[before.periodId,'period']].every(([rid,role],i)=>rec(refs[i])&&typeof rid==='string'
    &&hasExactKeys(refs[i],['role','label','recordId','link'])&&refs[i].role===role&&same(refs[i].recordId,rid)&&refs[i].label===short(rid)&&refs[i].link===null)
}
export function validInventoryCostCancellationRequest(value:unknown):value is WorkInventoryCostCancellationRequest {
  return shape(value,{expectedVersion:v=>inventoryCostWorkLong(v)&&v!=='9223372036854775807',approve:v=>v===true,reason:v=>v===null,delegationId:v=>v===null||guid(v),inventoryCostCancellation:v=>shape(v,{expectedProposalVersion:long})})
}
export function inventoryCostCancellationRequest(human:WorkInventoryCostCancellationPreview,pending:boolean,delegationId:string|null):WorkInventoryCostCancellationRequest|null {
  const b=human.preparedCancellation,c=human.cancellation
  if(!pending||c.status!=='Prepared'||c.version!==b.version)return null
  const request={expectedVersion:b.version,approve:true,reason:null,delegationId,inventoryCostCancellation:Object.freeze({expectedProposalVersion:human.originalProposal.version})} as const
  return validInventoryCostCancellationRequest(request)?Object.freeze(request):null
}
