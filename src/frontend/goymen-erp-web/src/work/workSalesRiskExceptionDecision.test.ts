import {afterEach,describe,expect,it,vi} from 'vitest'
import {decodeWorkDecisionPreview,isWorkDecisionRequest,isWorkPackageEffectiveDate,sendWorkDecision,workDecisionReasonLimit,WORK_DECISION_KINDS} from './workDecisionApi'
import {SALES_RISK_EXCEPTION_WORK_KIND as kind,decodeWorkSalesRiskExceptionPreview as human,salesRiskExceptionWorkCommand} from './workSalesRiskExceptionDecision'
import {salesRiskExceptionFixture as fixture,salesRiskExceptionHuman,salesRiskExceptionId as id} from './salesRiskExceptionDecisionFixture'
import {COMPANY_A,COMPANY_B,json} from '../settings/testHarness'
import {decodeRiskException} from '../sales/riskException'
afterEach(()=>vi.unstubAllGlobals())
const decode=(v:unknown)=>decodeWorkDecisionPreview(v,COMPANY_A,kind,id)
const record=(v:unknown)=>v as Record<string,unknown>
describe('Risk owner-bound full10/13 human and exact native four-field protocol',()=>{
 it('publishes exactly one Risk purpose beside all existing60 kinds',()=>{expect(WORK_DECISION_KINDS).toHaveLength(63);expect(WORK_DECISION_KINDS.filter(k=>k===kind)).toEqual([kind])})
 it('preserves all native fields,100ns UTC, rawreason and independent lossless order/exception versions',()=>{
  const v=fixture(COMPANY_A),before=JSON.stringify(v),d=decode(v);expect(d).not.toBeNull();expect(JSON.stringify(v)).toBe(before)
  expect(Object.keys(d!.salesRiskException!)).toHaveLength(10);expect(Object.keys(d!.salesRiskException!.exception)).toHaveLength(13)
  expect(d!.sourceVersion).toBe('9007199254740993');expect(d!.salesRiskException!.orderVersion).toBe('9007199254740995');expect(d!.salesRiskException!.exception.reason).toBe('  SENTETIK özgün risk gerekçesi  ');expect(d!.salesRiskException!.exception.requestedAtUtc).toBe('2026-11-28T09:00:00.0000001Z')
  expect(decodeWorkDecisionPreview(v,COMPANY_A.toUpperCase(),kind,id.toUpperCase())).not.toBeNull()
 })
 it.each(['Approved','Rejected','Withdrawn'] as const)('keeps terminal %s authoritative facts at maximum versions without another action',state=>{
  const v=fixture(COMPANY_A,'9223372036854775807',state);Object.assign(v.salesRiskException!,{orderVersion:'9223372036854775807'})
  expect(decode(v)).not.toBeNull();expect(salesRiskExceptionWorkCommand(v.salesRiskException!,true,'',null)).toBeNull();expect(salesRiskExceptionWorkCommand(v.salesRiskException!,false,'ret',null)).toBeNull()
 })
 it('retains a Requested MAX human with both action flags false and refuses a new incrementing command',()=>{
  const v=fixture(COMPANY_A,'9223372036854775807');expect(decode(v)).not.toBeNull();expect(v.pending).toBe(true);expect(v.approvalSupported).toBe(false);expect(v.rejectionSupported).toBe(false)
  expect(isWorkDecisionRequest({expectedVersion:v.sourceVersion,approve:true,reason:null,delegationId:null},kind)).toBe(false)
 })
 it.each(['Closed','Cancelled'] as const)('keeps reject independent of %s order eligibility',state=>{
  const v=fixture(COMPANY_A,'1','Requested',true);Object.assign(v.salesRiskException!,{orderState:state});expect(decode(v)).not.toBeNull();expect(salesRiskExceptionWorkCommand(v.salesRiskException!,true,'',null)).toBeNull();expect(salesRiskExceptionWorkCommand(v.salesRiskException!,false,'  ret  ',null)).not.toBeNull()
 })
 it('accepts the native approved-revision eligibility and current validity flag without doing a date calculation',()=>{
  const v=fixture(COMPANY_A);Object.assign(v.salesRiskException!,{orderApprovalState:'Rejected',hasApprovedRevision:true});expect(decode(v)).not.toBeNull()
  const terminal=fixture(COMPANY_A,'2','Approved');Object.assign(terminal.salesRiskException!.exception,{isValidToday:false,validOn:'0001-01-01'});expect(decode(terminal)).not.toBeNull()
 })
 it('leaves the legacy fixed1/2 decoder unchanged while Work accepts retained Int64 versions',()=>{
  const h=salesRiskExceptionHuman(COMPANY_A);expect(decodeRiskException(h.exception,h.exception.orderId)).toBeNull();expect(decodeRiskException({...h.exception,version:'1'},h.exception.orderId)).not.toBeNull();expect(human(h,COMPANY_A,id,isWorkPackageEffectiveDate)).not.toBeNull()
 })
 it.each(['root-extra','human-extra','exception-extra','missing','human-id','human-company','company','state','version-number','version-pad','version-overflow','order-version-number','order-state','order-approval','order-revision','exceptionNo-zero','exceptionNo-overflow','order-id','instruction-id','reason','reason-long','actor','actor-long','utc-date','utc-time','utc-LF','decision','validity','today','pending','approval','rejection','source-version','threshold','actual','deviation','unit','reference','reference-link','caption','foreign-human'])('rejects malformed/foreign %s',fault=>{
  const v=structuredClone(fixture(COMPANY_A)) as unknown as Record<string,unknown>,h=record(v.salesRiskException),r=record(h.exception)
  if(fault==='root-extra')v.unexpected=true;if(fault==='human-extra')h.previewDigest='a'.repeat(64);if(fault==='exception-extra')r.originalDecisionReceipt={};if(fault==='missing')delete r.loadingInstructionId
  if(fault==='human-id')h.exceptionId=COMPANY_B;if(fault==='human-company')h.companyId=COMPANY_B;if(fault==='company')v.companyId=COMPANY_B;if(fault==='state')r.state='Stale'
  if(fault==='version-number')r.version=1;if(fault==='version-pad')r.version='01';if(fault==='version-overflow')r.version='9223372036854775808';if(fault==='order-version-number')h.orderVersion=1;if(fault==='order-state')h.orderState='Posted';if(fault==='order-approval')h.orderApprovalState='Unknown';if(fault==='order-revision')h.hasApprovedRevision='true'
  if(fault==='exceptionNo-zero')r.exceptionNo=0;if(fault==='exceptionNo-overflow')r.exceptionNo=2147483648;if(fault==='order-id')r.orderId='not-guid';if(fault==='instruction-id')r.loadingInstructionId=COMPANY_A.slice(1)
  if(fault==='reason')r.reason=' ';if(fault==='reason-long')r.reason='x'.repeat(4001);if(fault==='actor')r.requestedBy='';if(fault==='actor-long')r.requestedBy='x'.repeat(129)
  if(fault==='utc-date')r.requestedAtUtc='2026-02-30T09:00:00Z';if(fault==='utc-time')r.requestedAtUtc='2026-01-01T24:00:00Z';if(fault==='utc-LF')r.requestedAtUtc=String(r.requestedAtUtc)+'\n';if(fault==='decision')r.decidedBy=COMPANY_B;if(fault==='validity')r.validOn='2026-11-28';if(fault==='today')r.isValidToday=true
  if(fault==='pending')v.pending=false;if(fault==='approval')v.approvalSupported=false;if(fault==='rejection')h.rejectionSupported=false;if(fault==='source-version')v.sourceVersion='1'
  if(['threshold','actual','deviation','unit'].includes(fault))record((v.rules as unknown[])[0])[fault]='1';if(fault==='reference')record((v.references as unknown[])[0]).recordId=COMPANY_B;if(fault==='reference-link')record((v.references as unknown[])[0]).link='/satis/siparisler/'+COMPANY_B;if(fault==='caption')v.documentLabel='other';if(fault==='foreign-human')v.salesDiscount={}
  expect(decode(v)).toBeNull()
 })
 it.each(['self','no-decision','reason','date','version'] as const)('refuses inconsistent approved native decision %s',fault=>{
  const v=fixture(COMPANY_A,'2','Approved'),r=v.salesRiskException!.exception
  Object.assign(r,fault==='self'?{decidedBy:r.requestedBy}:fault==='no-decision'?{decidedAtUtc:null}:fault==='reason'?{rejectionReason:'ret'}:fault==='date'?{validOn:'2026-02-30'}:{version:'1'})
  expect(decode(v)).toBeNull()
 })
 it('freezes exception version/rawreason in exact four fields without native target IDs or any amount',()=>{
  const h=salesRiskExceptionHuman(COMPANY_A),reason='  SENTETIK ret\n  ',approve=salesRiskExceptionWorkCommand(h,true,reason,null),reject=salesRiskExceptionWorkCommand(h,false,reason,null)
  expect(approve).toEqual({expectedVersion:h.exception.version,approve:true,reason:null,delegationId:null});expect(reject).toEqual({...approve,approve:false,reason});expect(isWorkDecisionRequest(reject,kind)).toBe(true);expect(workDecisionReasonLimit(kind)).toBe(4000)
  for(const body of [{...reject,reason:'x'.repeat(4001)},{...reject,reason:' '},{...approve,reason:'note'},{...approve,orderId:h.exception.orderId},{...approve,salesDiscount:{lines:[]}},{...approve,expectedVersion:'01'}])expect(isWorkDecisionRequest(body,kind)).toBe(false)
  expect(isWorkDecisionRequest({...reject,reason:'x'.repeat(4000)},kind)).toBe(true)
 })
 it.each([true,false])('accepts only the genuine original generic result state/correlation for approve=%s',async approve=>{
  const body={expectedVersion:'1',approve,reason:approve?null:'  ret  ',delegationId:null},original={companyId:COMPANY_A,kind,itemId:id,state:approve?'Approved':'Rejected',replayed:true,originalCorrelationId:'SENTETIK-risk-original'},headers={'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK-risk-original'}
  vi.stubGlobal('fetch',vi.fn(async()=>json(original,200,headers)));expect((await sendWorkDecision(COMPANY_A,kind,id,body,'key')).type).toBe('applied')
  for(const invalid of [{...original,state:'Requested'},{...original,state:approve?'Rejected':'Approved'},{...original,originalDecisionReceipt:{}},{...original,kind:'sales.order-draft.v1'}]){vi.stubGlobal('fetch',vi.fn(async()=>json(invalid,200,headers)));expect((await sendWorkDecision(COMPANY_A,kind,id,body,'key')).type).toBe('unknown')}
 })
})
