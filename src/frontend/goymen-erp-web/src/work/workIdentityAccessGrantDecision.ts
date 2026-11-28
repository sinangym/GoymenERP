import type {AccessGrantRequest} from '../identity/accessGrantContract'
import {isBoundUuid} from '../finance/contract'
import {hasExactKeys} from '../finance/wireShape'
import {catalogVersion,catalogIncrementable} from './workApprovedSourceCatalogDecision'

// Staged core only; root owns paired capability/route publication.
export const IDENTITY_ACCESS_GRANT_WORK_KIND='identity.access-grant-request.v1'
export const IDENTITY_ACCESS_GRANT_WORK_CAPABILITY='identity.access-grant-request.work-decision'
export type WorkAccessGrantRequest=Omit<AccessGrantRequest,'status'>&{readonly status:'Pending'|'Approved'|'Rejected'|'Withdrawn'}
export interface WorkIdentityAccessGrantPreview {readonly request:WorkAccessGrantRequest;readonly requiresSecondApproval:boolean;readonly targetActive:boolean;
 readonly roleActive:boolean;readonly currentPermissions:readonly string[];readonly currentRoleIsCritical:boolean;readonly expectedImpactDigest:string}
export interface IdentityAccessGrantWorkCommand {readonly expectedVersion:string;readonly approve:boolean;readonly reason:string|null;readonly delegationId:string|null;
 readonly identityAccessGrant:{readonly expectedImpactDigest:string}}
const record=(v:unknown):v is Record<string,unknown>=>v!==null&&typeof v==='object'&&!Array.isArray(v)
const guid=(v:unknown):v is string=>typeof v==='string'&&v.length===36&&isBoundUuid(v)
const same=(v:unknown,id:string)=>typeof v==='string'&&v.toLowerCase()===id.toLowerCase()
const text=(v:unknown):v is string=>typeof v==='string'&&v.trim()!==''
const permissions=(v:unknown):v is readonly string[]=>{
 if(!Array.isArray(v))return false
 let previous:string|undefined
 for(const code of v){if(!text(code)||code!==code.trim()||previous!==undefined&&previous>=code)return false;previous=code}
 return true
}
export const accessGrantImpact=(v:unknown):v is string=>typeof v==='string'&&v.length===64&&/^[0-9A-F]{64}$/.test(v)
export const validAccessGrantDecisionReason=(approve:boolean,v:unknown)=>approve?v===null:typeof v==='string'&&v===v.trim()&&v.length<=64&&/^[a-z][a-z0-9_.-]{0,63}$/.test(v)
// Native DateTimeOffset has 100ns precision and no default-time/chronology restriction. Validate calendar/offset; retain raw text.
const instant=(v:unknown,date:(v:unknown)=>boolean):v is string=>typeof v==='string'&&v===v.trim()&&date(v.slice(0,10))&&Number.isFinite(Date.parse(v))
 &&Date.parse(v)>=Date.parse('0001-01-01T00:00:00Z')&&Date.parse(v)<=Date.parse('9999-12-31T23:59:59.999Z')
 &&/^\d{4}-\d{2}-\d{2}T(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:\.\d{1,7})?(?:Z|[+-](?:(?:0\d|1[0-3]):[0-5]\d|14:00))$/.test(v)
// Technical timestamp comparison only, including sub-millisecond native validity windows; no business-date inference.
const ticks=(v:string)=>BigInt(Date.parse(v))*10000n+BigInt((v.match(/\.(\d{1,7})/)?.[1]??'').padEnd(7,'0').slice(3))
export function decodeWorkIdentityAccessGrantPreview(value:unknown,company:string,id:string,date:(v:unknown)=>boolean):WorkIdentityAccessGrantPreview|null {
 if(!guid(company)||!guid(id)||!record(value)||!hasExactKeys(value,['request','requiresSecondApproval','targetActive','roleActive','currentPermissions','currentRoleIsCritical','expectedImpactDigest'])
  ||!record(value.request)||!['requiresSecondApproval','targetActive','roleActive','currentRoleIsCritical'].every(k=>typeof value[k]==='boolean')
  ||!accessGrantImpact(value.expectedImpactDigest)||!permissions(value.currentPermissions))return null
 const r=value.request
 if(!hasExactKeys(r,['requestId','requestedBy','target','roleId','roleCode','roleName','status','requestedAtUtc','validFromUtc','validUntilUtc','reason','version'])
  ||!guid(r.requestId)||!same(r.requestId,id)||!guid(r.roleId)||!text(r.roleCode)||!text(r.roleName)
  ||!catalogVersion(r.version)||typeof r.status!=='string'||!['Pending','Approved','Rejected','Withdrawn'].includes(r.status)||r.status!=='Pending'&&r.version==='1'
  ||!instant(r.requestedAtUtc,date)||r.validFromUtc!==null&&!instant(r.validFromUtc,date)||r.validUntilUtc!==null&&!instant(r.validUntilUtc,date)
  ||r.reason!==null&&(!text(r.reason)||r.reason!==r.reason.trim()||r.reason.length>500))return null
 for(const p of [r.requestedBy,r.target])if(!record(p)||!hasExactKeys(p,['userId','displayName'])||!guid(p.userId)||!text(p.displayName))return null
 if(r.validUntilUtc!==null&&(r.validFromUtc===null||ticks(r.validUntilUtc as string)<=ticks(r.validFromUtc as string)))return null
 return value as unknown as WorkIdentityAccessGrantPreview
}
export const isIdentityAccessGrantWorkCommand=(v:unknown):v is IdentityAccessGrantWorkCommand=>record(v)
 &&hasExactKeys(v,['expectedVersion','approve','reason','delegationId','identityAccessGrant'])&&catalogIncrementable(v.expectedVersion)
 &&typeof v.approve==='boolean'&&validAccessGrantDecisionReason(v.approve,v.reason)&&(v.delegationId===null||guid(v.delegationId))
 &&record(v.identityAccessGrant)&&hasExactKeys(v.identityAccessGrant,['expectedImpactDigest'])&&accessGrantImpact(v.identityAccessGrant.expectedImpactDigest)
export function identityAccessGrantWorkCommand(h:WorkIdentityAccessGrantPreview,approve:boolean,reason:string,delegationId:string|null):IdentityAccessGrantWorkCommand|null {
 if(h.request.status!=='Pending'||approve&&(!h.targetActive||!h.roleActive))return null
 const body={expectedVersion:h.request.version,approve,reason:approve?null:reason,delegationId,identityAccessGrant:{expectedImpactDigest:h.expectedImpactDigest}}
 return isIdentityAccessGrantWorkCommand(body)?body:null
}
export function validIdentityAccessGrantPreview(value:Record<string,unknown>,company:string,id:string,date:(v:unknown)=>boolean):boolean {
 const h=decodeWorkIdentityAccessGrantPreview(value.identityAccessGrant,company,id,date)
 if(h===null||!hasExactKeys(value,['companyId','kind','itemId','documentLabel','sourceVersion','pending','approvalReasonRequired','references','rules','approvalSupported','rejectionSupported','identityAccessGrant'])
  ||!same(value.companyId,company)||!same(value.itemId,id)||value.kind!==IDENTITY_ACCESS_GRANT_WORK_KIND||value.documentLabel!==h.request.roleName
  ||value.sourceVersion!==h.request.version||value.pending!==(h.request.status==='Pending')||value.approvalReasonRequired!==false
  ||value.rejectionSupported!==value.pending||value.approvalSupported!==(value.pending&&h.targetActive&&h.roleActive&&catalogIncrementable(h.request.version))
  ||!Array.isArray(value.rules)||!Array.isArray(value.references)||value.references.length!==2)return false
 const codes=['identity.access-grant-approval-required',...(h.requiresSecondApproval?['identity.access-grant-second-approval']:[]),...(h.request.validUntilUtc!==null?['identity.access-grant-time-boxed']:[])]
 const rules=value.rules
 if(rules.length!==codes.length||!codes.every((code,index)=>{const r=rules[index];return record(r)&&hasExactKeys(r,['ruleCode','threshold','actual','deviation','unit'])&&r.ruleCode===code&&r.threshold===null&&r.actual===null&&r.deviation===null&&r.unit===null}))return false
 const refs=value.references,req=h.request,link=`/ayarlar/kullanicilar-ve-yetkiler?kullanici=${req.target.userId.toLowerCase()}`
 return ([['target-user',req.target.userId,req.target.displayName,link],['role',req.roleId,req.roleName,`/ayarlar/kullanicilar-ve-yetkiler/roller/${req.roleId.toLowerCase()}`]] as const).every(([role,recordId,label,path],index)=>{
  const r=refs[index];return record(r)&&hasExactKeys(r,['role','label','recordId','link'])&&r.role===role&&same(r.recordId,recordId)&&r.label===label&&typeof r.link==='string'&&r.link.toLowerCase()===path
 })
}
