import { decodeTaxIdChange, COMPANY_TAX_ID_LIMITS, type TaxIdChange } from '../organization/companyTaxIdContract'
import { isValidVkn } from '../cari/taxIdentity'
import { isBoundUuid } from '../finance/contract'
import { hasExactKeys } from '../finance/wireShape'
import { catalogVersion, catalogIncrementable } from './workApprovedSourceCatalogDecision'

// Paired human Work purpose. Capability availability is controlled by the server shell.
export const ORGANIZATION_COMPANY_TAX_ID_WORK_CAPABILITY='organization.company-tax-id-change.work-decision'
export const ORGANIZATION_COMPANY_TAX_ID_WORK_KIND='organization.company-tax-id-change.v1'
export interface WorkOrganizationCompanyTaxIdPreview {
 readonly companyId:string;readonly companyVersion:string;readonly companyVkn:string|null
 readonly request:TaxIdChange;readonly companyVersionAtRequest:string;readonly isCurrent:boolean
}
export interface OrganizationCompanyTaxIdDecisionRequest {
 readonly expectedVersion:string;readonly approve:boolean;readonly reason:string|null;readonly delegationId:string|null
}
export const ORGANIZATION_COMPANY_TAX_ID_WORK_TEXT={
 title:'Şirket VKN değişikliği karar bilgileri',readOnly:'Şirketin güncel bilgileri ile talebin hazırlandığı bilgiler ayrı gösterilir. Alanlar salt okunurdur.',
 stale:'Şirket kaydı talepten sonra değişmiş. Bu talep yalnız reddedilebilir.',
 retained:'Önceki kararınız yeniden doğrulanıyor. Karar verdiğiniz şirket sürümü ve notunuz korunuyor; güncel talep bu isteğin yerine kullanılmıyor.',
 company:'Güncel şirket bilgileri',request:'VKN değişiklik talebi',yes:'Evet',no:'Hayır',
 fields:{companyId:'Şirket',companyVersion:'Güncel şirket sürümü',companyVkn:'Şirketin güncel VKN’si',companyVersionAtRequest:'Talebin hazırlandığı şirket sürümü',isCurrent:'Talep güncel',id:'Talep kimliği'}
} as const
const record=(v:unknown):v is Record<string,unknown>=>v!==null&&typeof v==='object'&&!Array.isArray(v)
const guid=(v:unknown):v is string=>typeof v==='string'&&v.length===36&&isBoundUuid(v)
const same=(v:unknown,id:string)=>typeof v==='string'&&v.toLowerCase()===id.toLowerCase()
const controls=/[\u0000-\u001f\u007f-\u009f]/
const vkn=(v:unknown):v is string=>typeof v==='string'&&v.length===10&&/^[0-9]{10}$/.test(v)
const actor=(v:unknown):v is string=>typeof v==='string'&&v.trim()!==''&&v===v.trim()&&v.length<=128
const canonicalReason=(v:unknown):v is string=>typeof v==='string'&&v.trim()!==''&&v===v.trim()&&v.length<=COMPANY_TAX_ID_LIMITS.reason&&!controls.test(v)
// Same native note rule: trim, then at most1000 and no control characters. Empty notes are allowed on both decisions.
export const validCompanyTaxIdDecisionNote=(v:unknown):v is string|null=>v===null||typeof v==='string'&&v.trim().length<=COMPANY_TAX_ID_LIMITS.note&&!controls.test(v.trim())
const canonicalNote=(v:unknown)=>v===null||typeof v==='string'&&v.length>0&&v===v.trim()&&validCompanyTaxIdDecisionNote(v)
// Native decoder validates DateTimeOffset readability; additionally require its actual ISO/calendar wire. No default-time or chronology policy.
const timestamp=(v:unknown)=>typeof v==='string'&&v===v.trim()&&v.slice(0,10)>='0001-01-01'&&Number.isFinite(Date.parse(v.slice(0,10)+'T00:00:00Z'))&&new Date(v.slice(0,10)+'T00:00:00Z').toISOString().slice(0,10)===v.slice(0,10)&&/^\d{4}-\d{2}-\d{2}T(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:\.\d{1,7})?(?:Z|[+-](?:(?:0\d|1[0-3]):[0-5]\d|14:00))$/.test(v)
export function decodeWorkOrganizationCompanyTaxIdPreview(value:unknown,company:string,id:string):WorkOrganizationCompanyTaxIdPreview|null {
 if(!guid(company)||!guid(id)||!record(value)||!hasExactKeys(value,['companyId','companyVersion','companyVkn','request','companyVersionAtRequest','isCurrent'])
  ||!guid(value.companyId)||!same(value.companyId,company)||!catalogVersion(value.companyVersion)||!catalogVersion(value.companyVersionAtRequest)
  ||value.companyVkn!==null&&!vkn(value.companyVkn)||typeof value.isCurrent!=='boolean')return null
 const request=decodeTaxIdChange(value.request)
 if(request===null||!guid(request.id)||!same(request.id,id)||!vkn(request.proposedVkn)||!isValidVkn(request.proposedVkn)
  ||request.currentVkn!==null&&!vkn(request.currentVkn)||!canonicalReason(request.reason)||!actor(request.requestedBy)||!timestamp(request.requestedAtUtc)
  ||request.status!=='pending'&&(!actor(request.decidedBy)||request.decidedBy===request.requestedBy||!timestamp(request.decidedAtUtc)||!canonicalNote(request.decisionNote))
  ||value.isCurrent!==(value.companyVersion===value.companyVersionAtRequest&&value.companyVkn===request.currentVkn))return null
 return value as unknown as WorkOrganizationCompanyTaxIdPreview
}
export const isOrganizationCompanyTaxIdDecisionRequest=(v:unknown):v is OrganizationCompanyTaxIdDecisionRequest=>record(v)
 &&hasExactKeys(v,['expectedVersion','approve','reason','delegationId'])&&catalogVersion(v.expectedVersion)&&typeof v.approve==='boolean'
 &&(!v.approve||catalogIncrementable(v.expectedVersion))&&validCompanyTaxIdDecisionNote(v.reason)&&(v.delegationId===null||guid(v.delegationId))
export function organizationCompanyTaxIdDecisionRequest(human:WorkOrganizationCompanyTaxIdPreview,pending:boolean,approve:boolean,note:string|null,delegationId:string|null):OrganizationCompanyTaxIdDecisionRequest|null {
 if(!pending||human.request.status!=='pending'||approve&&!human.isCurrent)return null
 const result={expectedVersion:human.companyVersion,approve,reason:note===null?null:note.trim()||null,delegationId}
 return isOrganizationCompanyTaxIdDecisionRequest(result)?result:null
}
export function validOrganizationCompanyTaxIdPreview(value:Record<string,unknown>,company:string,id:string):boolean {
 const h=decodeWorkOrganizationCompanyTaxIdPreview(value.organizationCompanyTaxId,company,id)
 if(!hasExactKeys(value,['companyId','kind','itemId','documentLabel','sourceVersion','pending','approvalReasonRequired','references','rules','rejectionSupported','approvalSupported','organizationCompanyTaxId'])||h===null||!same(value.companyId,company)||!same(value.itemId,id)||value.kind!==ORGANIZATION_COMPANY_TAX_ID_WORK_KIND
  ||value.sourceVersion!==h.companyVersion||value.pending!==(h.request.status==='pending')||value.approvalReasonRequired!==false||value.rejectionSupported!==true
  ||value.approvalSupported!==(h.isCurrent&&catalogIncrementable(h.companyVersion))||value.documentLabel!==h.request.proposedVkn
  ||!Array.isArray(value.references)||value.references.length!==0||!Array.isArray(value.rules))return false
 const expected=['organization.company-tax-id-change-approval-required',...(h.request.currentVkn===null?['organization.company-tax-id-first']:[]),
  ...(h.request.status==='pending'&&!h.isCurrent?['organization.company-tax-id-change-stale']:[])]
 const rules=value.rules
 return rules.length===expected.length&&expected.every((code,index)=>{const r=rules[index];return record(r)&&hasExactKeys(r,['ruleCode','threshold','actual','deviation','unit'])
  &&r.ruleCode===code&&r.threshold===null&&r.actual===null&&r.deviation===null&&r.unit===null})
}
