import {describe,it,expect,vi,afterEach} from 'vitest'
import {COMPANY_A,COMPANY_B,installFetch,json,problem} from '../settings/testHarness'
import {IDENTITY_ACCESS_GRANT_WORK_KIND as kind,decodeWorkIdentityAccessGrantPreview,identityAccessGrantWorkCommand,accessGrantImpact} from './workIdentityAccessGrantDecision'
import {decodeWorkDecisionPreview,isWorkPackageEffectiveDate,isWorkDecisionRequest,sendWorkDecision,fetchWorkDecision,WORK_DECISION_KINDS} from './workDecisionApi'
import {identityAccessGrantId as id,identityAccessGrantFixture as fixture,identityAccessGrantHuman as human,identityAccessGrantImpact as token} from './identityAccessGrantDecisionFixture'
afterEach(()=>{vi.unstubAllGlobals();vi.restoreAllMocks()})
const decode=(v:unknown)=>decodeWorkDecisionPreview(v,COMPANY_A,kind,id)
const body=(approve=true)=>({expectedVersion:'9007199254740993',approve,reason:approve?null:'incorrect_request',delegationId:null,identityAccessGrant:{expectedImpactDigest:token}})
describe('Work access grant exact native human and seen-impact binding',()=>{
 it('pairs exactly one purpose and preserves all native12 request fields, full permissions, sub-ms native window and Int64 text',()=>{
  expect(WORK_DECISION_KINDS.filter(v=>v===kind)).toEqual([kind]);expect(WORK_DECISION_KINDS).toHaveLength(63)
  const value=fixture(COMPANY_A),decoded=decode(value);expect(decoded).toEqual(value);expect(decoded?.identityAccessGrant?.request.version).toBe('9007199254740993')
  expect(Object.keys(value.identityAccessGrant.request)).toHaveLength(12);expect(Object.keys(value.identityAccessGrant)).toHaveLength(7)
 })
 it.each(['1','9007199254740993','9223372036854775806','9223372036854775807'])('retains canonical version %s without Number conversion',version=>expect(decode(fixture(COMPANY_A,version))).not.toBeNull())
 it.each(['Approved','Rejected','Withdrawn'] as const)('terminal %s is read-only',status=>{
  const v=fixture(COMPANY_A,'2',status);expect(decode(v)).toEqual(v);expect(identityAccessGrantWorkCommand(v.identityAccessGrant,true,'',null)).toBeNull()
 })
 it.each(['0','01','+1','-1','1.0','1e1','9223372036854775808','1\n','1\r','1\r\n',1,null])('rejects noncanonical/foreign request version %j',version=>{
  const v=fixture(COMPANY_A);expect(decode({...v,sourceVersion:version,identityAccessGrant:{...v.identityAccessGrant,request:{...v.identityAccessGrant.request,version}}})).toBeNull()
 })
 it.each(['\n','\r','\r\n'])('rejects replacement and appended digest suffix %j',suffix=>{
  for(const bad of ['A'.repeat(64-suffix.length)+suffix,'A'.repeat(64)+suffix]){expect(accessGrantImpact(bad)).toBe(false);const v=fixture(COMPANY_A);expect(decode({...v,identityAccessGrant:{...v.identityAccessGrant,expectedImpactDigest:bad}})).toBeNull()}
 })
 it.each([
  {expectedImpactDigest:'a'.repeat(64)},{expectedImpactDigest:null},{expectedImpactDigest:'G'.repeat(64)},
  {currentPermissions:null},{currentPermissions:['x','x']},{currentPermissions:['z','a']},{currentPermissions:['']},{currentPermissions:[' x']},
  {currentPermissions:['x',null]},{currentRoleIsCritical:null},{targetActive:1},{roleActive:null},{requiresSecondApproval:'true'},
  {sourceFactsDigest:token},{previewDigest:token},{appliedImpactDigest:token},{decisionReceipt:{}},
 ])('refuses malformed/private human %j',patch=>{const v=fixture(COMPANY_A);expect(decode({...v,identityAccessGrant:{...v.identityAccessGrant,...patch}})).toBeNull()})
 it.each([
  {status:['Pending']},{status:'Unknown'},{version:'1',status:'Approved'},{requestedAtUtc:'2026-02-30T00:00:00Z'},
  {requestedAtUtc:'2026-01-01T00:00:00+14:01'},{requestedAtUtc:'2026-01-01T00:00:00Z\n'},
  {validFromUtc:null},{validUntilUtc:'2026-11-28T01:00:00.0000001+00:00'},{validUntilUtc:'bad'},
  {reason:' reason '},{reason:'x'.repeat(501)},{requestedBy:null},{target:{userId:id,displayName:'x',extra:true}},
  {roleId:id+'\n'},{roleCode:' '},{roleName:''},{extra:true},
 ])('refuses malformed native request %j',patch=>{const v=fixture(COMPANY_A);expect(decode({...v,identityAccessGrant:{...v.identityAccessGrant,request:{...v.identityAccessGrant.request,...patch}}})).toBeNull()})
 it('accepts native empty permission set and null dates/reason without inventing historical applied effects',()=>{
  const h={...human(),currentPermissions:[],currentRoleIsCritical:false,request:{...human().request,validFromUtc:null,validUntilUtc:null,reason:null}}
  expect(decodeWorkIdentityAccessGrantPreview(h,COMPANY_A,id,isWorkPackageEffectiveDate)).toEqual(h)
 })
 it('preserves UTC-offset equality rules and native default instant without imposing timestamp chronology',()=>{
  const h=human();expect(decodeWorkIdentityAccessGrantPreview({...h,request:{...h.request,requestedAtUtc:'0001-01-01T00:00:00+00:00'}},COMPANY_A,id,isWorkPackageEffectiveDate)).not.toBeNull()
  expect(decodeWorkIdentityAccessGrantPreview({...h,request:{...h.request,validUntilUtc:'2026-11-28T02:00:00.0000001+01:00'}},COMPANY_A,id,isWorkPackageEffectiveDate)).toBeNull()
 })
 it('accepts actual GUID identity regardless of letter case while rejecting malformed suffixes',()=>{
  const v=fixture(COMPANY_A),h=v.identityAccessGrant,r=h.request
  const upper={...v,companyId:COMPANY_A.toUpperCase(),itemId:id.toUpperCase(),identityAccessGrant:{...h,request:{...r,requestId:id.toUpperCase(),roleId:r.roleId.toUpperCase(),requestedBy:{...r.requestedBy,userId:r.requestedBy.userId.toUpperCase()},target:{...r.target,userId:r.target.userId.toUpperCase()}}}}
  expect(decode(upper)).not.toBeNull();expect(decode({...v,itemId:id+'\\n'})).toBeNull()
 })
 it('requires exact company/request/version, complete native rules and references, and support flags',()=>{
  const v=fixture(COMPANY_A)
  for(const patch of [{companyId:COMPANY_B},{itemId:COMPANY_B},{sourceVersion:'1'},{kind:'sales.request.v1'},{pending:false},{approvalReasonRequired:true},{rejectionSupported:false},{approvalSupported:false},{documentLabel:'other'},{references:v.references.slice(1)},{rules:[]},{identityAccessGrant:undefined},{salesRequest:{}}])expect(decode({...v,...patch})).toBeNull()
  expect(decode({...v,references:v.references.map((r,i)=>i?{...r,recordId:COMPANY_B}:r)})).toBeNull()
  expect(decode({...v,references:v.references.map((r,i)=>i?r:{...r,link:'/general-read'})})).toBeNull()
  expect(decode({...v,rules:v.rules.map((r,i)=>i?r:{...r,actual:'1'})})).toBeNull()
 })
 it.each(['targetActive','roleActive'] as const)('inactive %s supports rejection without approval',field=>{
  const v=fixture(COMPANY_A),h={...v.identityAccessGrant,[field]:false};expect(decode({...v,approvalSupported:false,identityAccessGrant:h})).not.toBeNull()
  expect(identityAccessGrantWorkCommand(h,true,'',null)).toBeNull();expect(identityAccessGrantWorkCommand(h,false,'not_required',null)?.approve).toBe(false)
 })
 it('freezes client-seen token for both native directions in exactly5 public fields',()=>{
  for(const approve of [true,false])expect(identityAccessGrantWorkCommand(human(),approve,'incorrect_request',null)).toEqual(body(approve))
  expect(Object.keys(body())).toHaveLength(5);expect(isWorkDecisionRequest(body(),kind)).toBe(true)
  for(const other of [undefined,'sales.request.v1','finance.cash-account.v1'] as const)expect(isWorkDecisionRequest(body(),other)).toBe(false)
 })
 it.each(['','bad code',' Reason','reason ','UPPER','x'.repeat(65),'reason\n','reason\r','reason\r\n'])('rejects noncanonical reject reason %j',reason=>expect(isWorkDecisionRequest({...body(false),reason},kind)).toBe(false))
 it('requires null approval reason and exact token selection; both decisions must increment original request version',()=>{
  expect(isWorkDecisionRequest({...body(false),reason:'a'.repeat(64)},kind)).toBe(true)
  for(const patch of [{reason:'reason'},{identityAccessGrant:undefined},{identityAccessGrant:{expectedImpactDigest:token,extra:true}},{expectedVersion:'9223372036854775807'},{normalJournalDraft:{periodId:id}}])expect(isWorkDecisionRequest({...body(),...patch},kind)).toBe(false)
 })
 it('transport uses the frozen seen token, exact key/body and original replay headers; no owner read is sent by POST',async()=>{
  const calls=installFetch({respond:(url,init)=>url.includes(`/work/decisions/${kind}/${id}`)?init?.method==='POST'?json({companyId:COMPANY_A,kind,itemId:id,state:'Approved',replayed:true,originalCorrelationId:'original-identity'},200,{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original-identity'}):json(fixture(COMPANY_A)):undefined})
  const result=await sendWorkDecision(COMPANY_A,kind,id,body(),'same-key');expect(result.type).toBe('applied');expect(calls.filter(c=>c.method==='POST')[0]?.body).toEqual(body());expect(calls.filter(c=>c.method==='POST')[0]?.headers['Idempotency-Key']).toBe('same-key');expect(calls.some(c=>c.method==='GET')).toBe(false)
 })
 it('version-conflict/forbidden and unbound receipt503 remain distinct; no fabricated original approval',async()=>{
  let status=503;installFetch({respond:url=>url.includes(`/work/decisions/${kind}/${id}`)?problem(status):undefined})
  expect((await sendWorkDecision(COMPANY_A,kind,id,body(),'same-key')).type).toBe('unknown')
  status=409;expect((await sendWorkDecision(COMPANY_A,kind,id,body(),'same-key')).type).toBe('refused')
  status=403;expect(await fetchWorkDecision(COMPANY_A,kind,id)).toMatchObject({type:'http',status:403})
 })
})
