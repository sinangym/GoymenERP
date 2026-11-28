import {IDENTITY_ACCESS_GRANT_WORK_KIND as kind,type WorkIdentityAccessGrantPreview} from './workIdentityAccessGrantDecision'
export const identityAccessGrantId='d4580000-0000-4000-8000-000000000058'
export const identityAccessGrantImpact='A'.repeat(64)
export function identityAccessGrantHuman(version='9007199254740993',status:WorkIdentityAccessGrantPreview['request']['status']='Pending'):WorkIdentityAccessGrantPreview {
 return {request:{requestId:identityAccessGrantId,requestedBy:{userId:'d4580000-0000-4000-8000-000000000001',displayName:'SENTETIK requester'},
  target:{userId:'d4580000-0000-4000-8000-000000000002',displayName:'SENTETIK target'},roleId:'d4580000-0000-4000-8000-000000000003',
  roleCode:'SENTETIK_ROLE',roleName:'SENTETIK role',status,requestedAtUtc:'2026-11-28T01:00:00.0000001+00:00',validFromUtc:'2026-11-28T01:00:00.0000001+00:00',
  validUntilUtc:'2026-11-28T01:00:00.0000002+00:00',reason:'SENTETIK full requester reason',version},requiresSecondApproval:true,targetActive:true,roleActive:true,
  currentPermissions:['cari.parties.read','identity.access.approve'],currentRoleIsCritical:true,expectedImpactDigest:identityAccessGrantImpact}
}
export function identityAccessGrantFixture(company:string,version?:string,status?:WorkIdentityAccessGrantPreview['request']['status']) {
 const h=identityAccessGrantHuman(version,status),r=h.request
 return {companyId:company,kind,itemId:r.requestId,documentLabel:r.roleName,sourceVersion:r.version,pending:r.status==='Pending',approvalReasonRequired:false,
  approvalSupported:r.status==='Pending'&&h.targetActive&&h.roleActive&&r.version!=='9223372036854775807',rejectionSupported:r.status==='Pending',identityAccessGrant:h,
  references:[{role:'target-user',label:r.target.displayName,recordId:r.target.userId,link:`/ayarlar/kullanicilar-ve-yetkiler?kullanici=${r.target.userId}`},
   {role:'role',label:r.roleName,recordId:r.roleId,link:`/ayarlar/kullanicilar-ve-yetkiler/roller/${r.roleId}`}],
  rules:['identity.access-grant-approval-required',...(h.requiresSecondApproval?['identity.access-grant-second-approval']:[]),...(r.validUntilUtc!==null?['identity.access-grant-time-boxed']:[])].map(ruleCode=>({ruleCode,threshold:null,actual:null,deviation:null,unit:null}))}
}
