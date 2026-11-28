import {afterEach,describe,expect,it,vi} from 'vitest'
import {decodeWorkDecisionPreview,isWorkDecisionRequest,isWorkPackageEffectiveDate,exactNativeClrAmount,sendWorkDecision,workDecisionReasonLimit,WORK_DECISION_KINDS} from './workDecisionApi'
import {SALES_OVER_DELIVERY_REQUEST_WORK_KIND as kind,decodeWorkSalesOverDeliveryRequestPreview as human, salesOverDeliveryRequestTarget,salesOverDeliveryRequestWorkCommand} from './workSalesOverDeliveryRequestDecision'
import {salesOverDeliveryRequestFixture as fixture,salesOverDeliveryRequestHuman,salesOverDeliveryRequestId as id} from './salesOverDeliveryRequestDecisionFixture'
import {COMPANY_A,COMPANY_B,json,problem} from '../settings/testHarness'
import {decodeApprovalRequest} from '../sales/approvalRequests'
afterEach(()=>vi.unstubAllGlobals())
const decode=(v:unknown)=>decodeWorkDecisionPreview(v,COMPANY_A,kind,id)
const record=(v:unknown)=>v as Record<string,unknown>
describe('numbered over-delivery strict native human/Work transport',()=>{
 it('publishes exactly one paired purpose beside all existing60 kinds',()=>{
  expect(WORK_DECISION_KINDS).toHaveLength(61);expect(WORK_DECISION_KINDS.filter(x=>x===kind)).toEqual([kind])
 })
 it('keeps full5/18/11/8 exact shapes, raw quantity strings and independent request/order versions',()=>{
  const v=fixture(COMPANY_A),before=JSON.stringify(v),d=decode(v);expect(d).not.toBeNull();expect(JSON.stringify(v)).toBe(before)
  const h=d!.salesOverDeliveryRequest!;expect(Object.keys(h)).toHaveLength(5);expect(Object.keys(h.request)).toHaveLength(18);expect(Object.keys(h.order)).toHaveLength(11);expect(Object.keys(h.order.line!)).toHaveLength(8)
  expect(h.order.line!.currentQuantity).toBe('0001.2500');expect(h.request.requestedAtUtc).toBe('2026-11-28T09:00:00.0000001Z');expect(h.request.version).not.toBe(h.order.version)
  expect(salesOverDeliveryRequestTarget(h)).toEqual({requestId:id,orderId:h.order.id,requestNo:3,subjectRevision:2,lineId:h.request.lineId,requestVersion:h.request.version})
  expect(decodeWorkDecisionPreview(v,COMPANY_A.toUpperCase(),kind,id.toUpperCase())).not.toBeNull()
 })
 it.each(['Approved','Rejected'] as const)('preserves terminal %s full facts without enabling another decision',status=>{
  const v=fixture(COMPANY_A,'9223372036854775807',status);expect(decode(v)).not.toBeNull();expect(v.approvalSupported).toBe(false);expect(v.rejectionSupported).toBe(false)
  expect(salesOverDeliveryRequestWorkCommand(v.salesOverDeliveryRequest!,true,'',null)).toBeNull()
 })
 it.each(['root-extra','human-extra','request-extra','order-extra','line-extra','missing','kind','id','order-id','line-id','company','status','pending','approval','rejection','version-number','version-zero','version-pad','version-overflow','order-version-number','requestNo-overflow','revision-overflow','lineNo-overflow','subject-revision','utc-control','utc-date','note','maker','decision','quantity-LF','quantity-CR','quantity-overflow','quantity-scale','quantity-zero','threshold','actual','unit','reference','caption','foreign-human'])('rejects malformed/foreign %s',fault=>{
  const v=structuredClone(fixture(COMPANY_A)) as unknown as Record<string,unknown>,h=record(v.salesOverDeliveryRequest),r=record(h.request),o=record(h.order),l=record(o.line)
  if(fault==='root-extra')v.unexpected=true;if(fault==='human-extra')h.previewDigest='a'.repeat(64);if(fault==='request-extra')r.originalOverDeliveryReceipt={};if(fault==='order-extra')o.unitPrice='9';if(fault==='line-extra')l.price='9'
  if(fault==='missing')delete r.quantity;if(fault==='kind')r.kind='QuoteExpiredConversion';if(fault==='id')h.requestId=COMPANY_B;if(fault==='order-id')o.id=COMPANY_B;if(fault==='line-id')l.id=COMPANY_B;if(fault==='company')v.companyId=COMPANY_B
  if(fault==='status')r.status='Stale';if(fault==='pending')v.pending=false;if(fault==='approval')v.approvalSupported=false;if(fault==='rejection')v.rejectionSupported=false
  if(fault==='version-number')r.version=1;if(fault==='version-zero')r.version='0';if(fault==='version-pad')r.version='01';if(fault==='version-overflow')r.version='9223372036854775808';if(fault==='order-version-number')o.version=1
  if(fault==='requestNo-overflow')r.requestNo=2147483648;if(fault==='revision-overflow')o.revision=2147483648;if(fault==='lineNo-overflow')l.lineNo=2147483648;if(fault==='subject-revision')r.subjectRevision='Rev.2147483648'
  if(fault==='utc-control')r.requestedAtUtc=String(r.requestedAtUtc)+'\n';if(fault==='utc-date')r.requestedAtUtc='2026-02-30T09:00:00Z';if(fault==='note')r.note='not a native over-delivery fact';if(fault==='maker')r.requestedBy='';if(fault==='decision')r.decidedBy=COMPANY_B
  if(fault==='quantity-LF')r.quantity='0.03\n';if(fault==='quantity-CR')r.quantity='0.03\r';if(fault==='quantity-overflow')r.quantity='79228162514264337593543950336';if(fault==='quantity-scale')r.quantity='0.'+'0'.repeat(28)+'1';if(fault==='quantity-zero')r.quantity='0'
  if(fault==='threshold')record((v.rules as unknown[])[0]).threshold='2';if(fault==='actual')record((v.rules as unknown[])[0]).actual='2';if(fault==='unit')record((v.rules as unknown[])[0]).unit='TRY'
  if(fault==='reference')record((v.references as unknown[])[0]).recordId=COMPANY_B;if(fault==='caption')v.documentLabel='other';if(fault==='foreign-human')v.salesDiscount={}
  expect(decode(v)).toBeNull()
 })
 it('does not infer a current-line presence, new revision eligibility or a tolerance calculation',()=>{
  const v=fixture(COMPANY_A);const h=v.salesOverDeliveryRequest!;const changed={...h,order:{...h.order,revision:0,state:'Closed' as const,line:null}}
  expect(human(changed,COMPANY_A,id,isWorkPackageEffectiveDate,exactNativeClrAmount)).not.toBeNull()
 })
 it('keeps the older native numeric-revision decoder unchanged while Work accepts the actual Rev.N owner shape',()=>{
  const h=salesOverDeliveryRequestHuman();expect(decodeApprovalRequest(h.request)).toBeNull()
  expect(decodeApprovalRequest({...h.request,subjectRevision:'2'})).not.toBeNull()
  expect(human(h,COMPANY_A,id,isWorkPackageEffectiveDate,exactNativeClrAmount)).not.toBeNull()
 })
 it('freezes native expected REQUEST version and literal rejection reason in exactly four fields',()=>{
  const h=salesOverDeliveryRequestHuman(),reason='  SENTETIK ret  ',approve=salesOverDeliveryRequestWorkCommand(h,true,reason,null),reject=salesOverDeliveryRequestWorkCommand(h,false,reason,null)
  expect(approve).toEqual({expectedVersion:h.request.version,approve:true,reason:null,delegationId:null});expect(reject).toEqual({...approve,approve:false,reason})
  expect(isWorkDecisionRequest(approve,kind)).toBe(true);expect(isWorkDecisionRequest(reject,kind)).toBe(true);expect(workDecisionReasonLimit(kind)).toBe(4000)
  expect(isWorkDecisionRequest({...reject,reason:'x'.repeat(4000)},kind)).toBe(true);expect(isWorkDecisionRequest({...reject,reason:' '+'x'.repeat(4000)+' '},kind)).toBe(false)
 })
 it.each(['zero','padded','max','blank-reject','approve-reason','extra-target','foreign-selection'])('does not fetch malformed command %s',async fault=>{
  const fetch=vi.fn();vi.stubGlobal('fetch',fetch);const q:Record<string,unknown>={expectedVersion:'1',approve:true,reason:null,delegationId:null}
  if(fault==='zero')q.expectedVersion='0';if(fault==='padded')q.expectedVersion='01';if(fault==='max')q.expectedVersion='9223372036854775807';if(fault==='blank-reject'){q.approve=false;q.reason=' \t '}
  if(fault==='approve-reason')q.reason='SENTETIK';if(fault==='extra-target')q.orderId=COMPANY_B;if(fault==='foreign-selection')q.salesPriceFloor={lines:[]}
  expect(isWorkDecisionRequest(q,kind)).toBe(false);expect(await sendWorkDecision(COMPANY_A,kind,id,q as never,'SENTETIK')).toEqual({type:'unknown',reason:'unexpected'});expect(fetch).not.toHaveBeenCalled()
 })
 it.each([true,false])('sends only the Work native decision approve=%s with unchanged raw body/key and accepts original replay',async approve=>{
  const q=salesOverDeliveryRequestWorkCommand(salesOverDeliveryRequestHuman(),approve,'  SENTETIK ret  ',null)!,fetch=vi.fn().mockResolvedValue(json({companyId:COMPANY_A,kind,itemId:id,state:approve?'Approved':'Rejected',replayed:true,originalCorrelationId:'SENTETIK-original'},200,{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK-original'}));vi.stubGlobal('fetch',fetch)
  expect((await sendWorkDecision(COMPANY_A,kind,id,q,'SENTETIK-key')).type).toBe('applied');const [url,init]=fetch.mock.calls[0]!
  expect(url).toContain(`/work/decisions/${kind}/${id}`);expect(JSON.parse(init.body)).toEqual(q);expect(init.headers['Idempotency-Key']).toBe('SENTETIK-key');expect(Object.keys(q)).toHaveLength(4)
 })
 it.each([400,403,404,409,422,503])('preserves native refusal/unknown status %s without retrying or requesting human history',async status=>{
  const fetch=vi.fn().mockResolvedValue(problem(status));vi.stubGlobal('fetch',fetch)
  expect((await sendWorkDecision(COMPANY_A,kind,id,{expectedVersion:'1',approve:true,reason:null,delegationId:null},'SENTETIK')).type).toBe(status===503?'unknown':'refused');expect(fetch).toHaveBeenCalledTimes(1)
 })
})
