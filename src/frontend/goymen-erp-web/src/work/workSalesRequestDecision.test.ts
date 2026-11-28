import {describe,it,expect,afterEach,vi} from 'vitest'
import {COMPANY_A,COMPANY_B,json} from '../settings/testHarness'
import {decodeRequestDetail} from '../sales/poolContract'
import {decodeWorkDecisionPreview,isWorkDecisionRequest,sendWorkDecision,isWorkPackageEffectiveDate,workDecisionReasonLimit} from './workDecisionApi'
import {decodeWorkSalesRequestPreview,salesRequestWorkCommand,SALES_REQUEST_WORK_KIND as kind} from './workSalesRequestDecision'
import {salesRequestHuman as human,salesRequestFixture as fixture,salesRequestId as id} from './salesRequestDecisionFixture'
const decode=(v:unknown)=>decodeWorkSalesRequestPreview(v,COMPANY_A,id,isWorkPackageEffectiveDate)
function put(root:object,path:readonly string[],value:unknown){let o=root as Record<string,unknown>;for(const k of path.slice(0,-1)){const next=o[k];if(next===null||typeof next!=='object')throw Error('fixture');o=next as Record<string,unknown>}o[path.at(-1)!]=value}
afterEach(()=>vi.unstubAllGlobals())
describe('Sales request owner decoder reused by narrow Work preview',()=>{
 it.each(['Submitted','Approved','Rejected'] as const)('preserves all3/5/10/11 native fields and exact raw text %s',state=>{
  const h=human(COMPANY_A,state),raw=JSON.stringify(h);expect(decode(h)).toEqual(h);expect(JSON.stringify(h)).toBe(raw)
  expect(Object.keys(h)).toHaveLength(3);expect(Object.keys(h.detail)).toHaveLength(5);expect(Object.keys(h.detail.request)).toHaveLength(10);expect(Object.keys(h.detail.lines[0]!)).toHaveLength(11)
  expect(h.detail.lines[0]!.quantity).toBe('0001.2500');expect(h.detail.lines[0]!.productVersion).toBe('9007199254740993')
  expect(decodeRequestDetail(h.detail,id)).toEqual(h.detail);expect(decodeWorkDecisionPreview(fixture(COMPANY_A,state),COMPANY_A,kind,id)).not.toBeNull()
 })
 it('matches uppercase bookmarked GUID context without rewriting the payload',()=>{const h=human(COMPANY_A);expect(decodeWorkSalesRequestPreview(h,COMPANY_A.toUpperCase(),id.toUpperCase(),isWorkPackageEffectiveDate)).toEqual(h)})
 it.each(['1','9007199254740993','9223372036854775807'])('retains full Int64 header text %s',version=>expect(decode(human(COMPANY_A,'Submitted',version))?.detail.request.version).toBe(version))
 it.each([1,'0','01','1\n','9223372036854775808'])('rejects noncanonical header version %s',v=>{const h=human(COMPANY_A);put(h,['detail','request','version'],v);expect(decode(h)).toBeNull()})
 it.each([
  [['detail','request','companyId'],COMPANY_B],[['detail','request','id'],COMPANY_B],[['detail','request','lineCount'],2],
  [['detail','request','createdAtUtc'],'2026-02-30T08:00:00Z'],[['detail','request','createdAtUtc'],'2026-01-01T24:00:00Z'],
  [['detail','lines','0','quantity'],1.25],[['detail','lines','0','lineNo'],2147483648],[['detail','lines','0','productVersion'],9007199254740992],
  [['detail','lines','0','unitPrice'],'1'],[['approvalSupported'],false],[['rejectionSupported'],false],[['detail','decision'],{decidedBy:'maker'}],
  [['sourceFactsDigest'],'a'.repeat(64)],[['detail','originalDecisionReceipt'],{}],
 ] as const)('refuses malformed/private/foreign native facts %j',(path,value)=>{const h=human(COMPANY_A);put(h,path,value);expect(decode(h)).toBeNull()})
 it.each(['rules','references','documentLabel','sourceVersion','pending','approvalSupported','rejectionSupported'] as const)('binds outer %s before rendering',field=>{const v=fixture(COMPANY_A);put(v,[field],field==='rules'||field==='references'?[]:field==='pending'||field==='approvalSupported'||field==='rejectionSupported'?false:'wrong');expect(decodeWorkDecisionPreview(v,COMPANY_A,kind,id)).toBeNull()})
 it('keeps approved reject-only distinct from the Submitted queue and preserves raw mandatory reason',()=>{
  const h=human(COMPANY_A,'Approved');expect(salesRequestWorkCommand(h,true,'',null)).toBeNull()
  const reason=' '+ 'r'.repeat(3998)+' ';expect(salesRequestWorkCommand(h,false,reason,null)).toEqual({expectedVersion:'2',approve:false,reason,delegationId:null})
  expect(workDecisionReasonLimit(kind)).toBe(4000);expect(isWorkDecisionRequest({expectedVersion:'2',approve:false,reason,delegationId:null},kind)).toBe(true)
 })
 it.each(['','  ','r'.repeat(4001)])('refuses native invalid rejection text',reason=>expect(salesRequestWorkCommand(human(COMPANY_A),false,reason,null)).toBeNull())
 it('does not add SoD, chronology, trimmed reason or control restrictions absent from native',()=>{
  const h=human(COMPANY_A,'Approved');put(h,['detail','decision','decidedAtUtc'],'2020-01-01T01:00:00Z');expect(decode(h)).not.toBeNull()
  expect(salesRequestWorkCommand(h,false,'  raw\nreason  ',null)?.reason).toBe('  raw\nreason  ')
 })
 it.each([true,false])('requires incrementable version for both native actions approve=%s',approve=>{
  const h=human(COMPANY_A,'Submitted','9223372036854775807');expect(decode(h)).not.toBeNull();expect(salesRequestWorkCommand(h,approve,'reason',null)).toBeNull()
  expect(isWorkDecisionRequest({expectedVersion:'1',approve,reason:approve?null:'reason',delegationId:null,organizationCompanyTaxId:{}},kind)).toBe(false)
 })
 it('consumes original Approved replay only, never later native current Rejected as the approval answer',async()=>{
  const body={expectedVersion:'1',approve:true,reason:null,delegationId:null},result={companyId:COMPANY_A,kind,itemId:id,state:'Approved',replayed:true,originalCorrelationId:'original-sales-request'}
  const reply=()=>json(result,200,{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':result.originalCorrelationId})
  const fetcher=vi.fn().mockImplementation(reply);vi.stubGlobal('fetch',fetcher)
  expect(await sendWorkDecision(COMPANY_A,kind,id,body,'SENTETIK-key')).toMatchObject({type:'applied',data:{state:'Approved'}})
  result.state='Rejected';expect(await sendWorkDecision(COMPANY_A,kind,id,body,'SENTETIK-key')).toEqual({type:'unknown',reason:'unexpected'})
 })
})
