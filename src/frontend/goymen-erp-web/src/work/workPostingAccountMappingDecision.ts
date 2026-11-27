import type { PostingMapping, PostingMappingRow } from '../accounting/postingMappingApi'
import { isBoundUuid } from '../finance/contract'
import { hasExactKeys } from '../finance/wireShape'
import { transferDispatchUtc } from '../inventory/transferDispatchApi'
import { catalogVersion, catalogIncrementable } from './workApprovedSourceCatalogDecision'

// The paired Work purpose preserves the native mapping activation authority and identity.
export const POSTING_MAPPING_PURPOSE = {
  kind:'accounting.posting-mapping.v1', capabilityId:'accounting.posting-mapping.work-decision',
  routePath:'/onaylarim/accounting.posting-mapping.v1/:approvalId',
} as const
export const POSTING_MAPPING_KIND = POSTING_MAPPING_PURPOSE.kind
export type WorkPostingMapping = Omit<PostingMapping,'mappingVersion'|'version'> & {readonly mappingVersion:string;readonly version:string}
export interface WorkMappingPlanReference {readonly sourceVersion:string;readonly sourceSha256:string}
export interface WorkMappingAccount {readonly accountId:string;readonly code:string;readonly name:string}
export interface WorkPostingAccountMappingPreview {
  readonly mapping:WorkPostingMapping; readonly preparedAccountPlan:WorkMappingPlanReference
  readonly currentActiveAccountPlan:{readonly companyId:string;readonly reference:WorkMappingPlanReference;readonly accounts:readonly WorkMappingAccount[]}
  readonly expectedVersion:string;readonly expectedActiveMappingVersion:string;readonly sourceFactsDigest:string;readonly previewDigest:string
}
const object=(v:unknown):v is Record<string,unknown>=>v!==null&&typeof v==='object'&&!Array.isArray(v)
const guid=(v:unknown):v is string=>typeof v==='string'&&v.length===36&&isBoundUuid(v)
const same=(a:unknown,b:string)=>typeof a==='string'&&a.toLowerCase()===b.toLowerCase()
const text=(v:unknown):v is string=>typeof v==='string'&&v.trim()!==''&&v===v.trim()
const sha=(v:unknown):v is string=>typeof v==='string'&&v.length===64&&/^[0-9a-fA-F]{64}$/.test(v)
const digest=(v:unknown)=>sha(v)&&v===v.toUpperCase()
const utc=(v:unknown):v is string=>transferDispatchUtc(v)&&v===v.trim()
  &&!/^0001-01-01T00:00:00(?:\.0{1,7})?(?:Z|\+00:00)$/.test(v)
  &&Number(v.slice(11,13))<24&&Number(v.slice(14,16))<60&&Number(v.slice(17,19))<60
const selector=(v:unknown)=>v===null||typeof v==='string'&&v.length>=1&&v.length<=64&&/^[A-Za-z0-9][A-Za-z0-9._-]*$/.test(v)&&v===v.trim()
const keys:Readonly<Record<string,readonly string[]>>={
  'sales-invoice':['receivable','revenue','vat-output','withholding-receivable'],
  'purchase-invoice':['payable','inventory','vat-input','withholding-payable','expense'],
  'cost-of-goods-sold':['trade-goods','manufactured'],
}
export const mappingVersion=catalogVersion
export const mappingIncrementable=catalogIncrementable
// The UUIDv8 carrier is reversible transport, never a second native identity or a numeric conversion.
export function postingMappingWorkId(version:string):string|null {
  if(!mappingVersion(version))return null
  const h=BigInt(version).toString(16).padStart(16,'0')
  return `ac52${h.slice(0,4)}-${h.slice(4,8)}-8${h.slice(8,11)}-8${h.slice(11,14)}-${h.slice(14)}0000000000`
}
export function postingMappingNativeId(id:unknown):string|null {
  if(!guid(id))return null
  const n=id.toLowerCase().replaceAll('-','')
  if(n.slice(0,4)!=='ac52'||n[12]!=='8'||n[16]!=='8'||n.slice(22)!=='0000000000')return null
  const h=n.slice(4,12)+n.slice(13,16)+n.slice(17,22),v=BigInt('0x'+h).toString()
  return mappingVersion(v)&&postingMappingWorkId(v)===id.toLowerCase()?v:null
}
function validRows(rows:unknown):rows is readonly PostingMappingRow[] {
  if(!Array.isArray(rows)||rows.length===0||rows.length>1000)return false
  const seen=new Set<string>()
  for(const row of rows){
    if(!object(row)||!hasExactKeys(row,['purpose','mappingKey','itemType','linePurpose','isExport','accountId','accountCode','vatRateCode'])
      ||!text(row.purpose)||!text(row.mappingKey)||!keys[row.purpose]?.includes(row.mappingKey)||!guid(row.accountId)||!text(row.accountCode)
      ||!selector(row.itemType)||!selector(row.linePurpose)||!selector(row.vatRateCode)||row.isExport!==null&&typeof row.isExport!=='boolean'
      ||row.purpose==='cost-of-goods-sold'&&[row.itemType,row.linePurpose,row.vatRateCode,row.isExport].some(v=>v!==null)
      ||row.vatRateCode!==null&&!['vat-output','vat-input'].includes(row.mappingKey))return false
    const key=JSON.stringify([row.purpose,row.mappingKey,row.itemType,row.linePurpose,row.isExport,row.vatRateCode])
    if(seen.has(key))return false
    seen.add(key)
  }
  return true
}
const planRef=(v:unknown):v is WorkMappingPlanReference=>object(v)&&hasExactKeys(v,['sourceVersion','sourceSha256'])&&text(v.sourceVersion)&&sha(v.sourceSha256)
export function validPostingMappingPreview(value:Record<string,unknown>,company:string,id:string,date:(v:unknown)=>v is string):boolean {
  const h=value.postingAccountMapping,nativeId=postingMappingNativeId(id)
  if(nativeId===null||!object(h)||!hasExactKeys(h,['mapping','preparedAccountPlan','currentActiveAccountPlan','expectedVersion','expectedActiveMappingVersion','sourceFactsDigest','previewDigest'])
    ||!object(h.mapping)||!mappingVersion(h.expectedVersion)||!mappingVersion(h.expectedActiveMappingVersion,false)
    ||!digest(h.sourceFactsDigest)||!digest(h.previewDigest)||!planRef(h.preparedAccountPlan))return false
  const m=h.mapping,p=h.currentActiveAccountPlan
  if(!hasExactKeys(m,['companyId','mappingVersion','effectiveFrom','status','version','accountPlanSourceVersion','accountPlanSourceSha256','preparedByUserId','preparedAtUtc','activatedByUserId','activatedAtUtc','rows'])
    ||!guid(m.companyId)||!same(m.companyId,company)||m.mappingVersion!==nativeId||!mappingVersion(m.version)||m.version!==h.expectedVersion||m.version!==value.sourceVersion
    ||!date(m.effectiveFrom)||m.effectiveFrom==='0001-01-01'||!text(m.accountPlanSourceVersion)||!sha(m.accountPlanSourceSha256)
    ||h.preparedAccountPlan.sourceVersion!==m.accountPlanSourceVersion||h.preparedAccountPlan.sourceSha256!==m.accountPlanSourceSha256
    ||!guid(m.preparedByUserId)||!utc(m.preparedAtUtc)||!validRows(m.rows)
    ||m.status!=='Prepared'&&m.status!=='Active'&&m.status!=='Superseded'||value.pending!==(m.status==='Prepared')
    ||value.approvalReasonRequired!==false||value.rejectionSupported!==false)return false
  if(m.status==='Prepared'?m.version!=='1'||m.activatedByUserId!==null||m.activatedAtUtc!==null
    :m.version!==(m.status==='Active'?'2':'3')||!guid(m.activatedByUserId)||same(m.activatedByUserId,m.preparedByUserId)||!utc(m.activatedAtUtc))return false
  if((m.status==='Active')!==(h.expectedActiveMappingVersion===nativeId)||!object(p)||!hasExactKeys(p,['companyId','reference','accounts'])
    ||!guid(p.companyId)||!same(p.companyId,company)||!planRef(p.reference)||!Array.isArray(p.accounts)||p.accounts.length===0)return false
  const ids=new Set<string>(),codes=new Set<string>()
  for(const a of p.accounts){
    if(!object(a)||!hasExactKeys(a,['accountId','code','name'])||!guid(a.accountId)||!text(a.code)||typeof a.name!=='string'||a.name.trim()===''||ids.has(a.accountId.toLowerCase())||codes.has(a.code))return false
    ids.add(a.accountId.toLowerCase());codes.add(a.code)
  }
  if(value.documentLabel!==nativeId||!Array.isArray(value.rules)||value.rules.length!==1||!object(value.rules[0])
    ||value.rules[0].ruleCode!=='accounting.posting-mapping-activation-required'||[value.rules[0].threshold,value.rules[0].actual,value.rules[0].deviation,value.rules[0].unit].some(v=>v!==null)
    ||!Array.isArray(value.references)||value.references.length!==1)return false
  const r=value.references[0]
  return object(r)&&r.role==='posting-account-mapping-version'&&same(r.recordId,id)&&r.label===nativeId&&r.link==='/muhasebe/hesap-eslemeleri'
}
