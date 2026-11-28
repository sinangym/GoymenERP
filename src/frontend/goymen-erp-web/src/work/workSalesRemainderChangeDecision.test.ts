import {afterEach,describe,expect,it,vi} from 'vitest'
import {decodeWorkDecisionPreview,isWorkDecisionRequest,isWorkPackageEffectiveDate,exactNativeClrAmount,sendWorkDecision,workDecisionReasonLimit,WORK_DECISION_KINDS} from './workDecisionApi'
import {SALES_REMAINDER_CHANGE_WORK_KIND as kind,decodeWorkSalesRemainderChangePreview as human,salesRemainderChangeWorkCommand} from './workSalesRemainderChangeDecision'
import {salesRemainderChangeFixture as fixture,salesRemainderChangeHuman,salesRemainderChangeId as id} from './salesRemainderChangeDecisionFixture'
import {COMPANY_A,COMPANY_B,json,problem} from '../settings/testHarness'
import {decodeRemainderChanges} from '../sales/remainderChange'
afterEach(()=>vi.unstubAllGlobals())
const decode=(v:unknown)=>decodeWorkDecisionPreview(v,COMPANY_A,kind,id)
const record=(v:unknown)=>v as Record<string,unknown>
describe('remainder native quantity-only Work profile',()=>{
 it('publishes one remainder purpose beside all existing61 kinds',()=>{expect(WORK_DECISION_KINDS).toHaveLength(64);expect(WORK_DECISION_KINDS.filter(k=>k===kind)).toEqual([kind])})
 it('preserves complete10/19/9 facts and raw text without mutation, calculations or a numeric version',()=>{
  const v=fixture(COMPANY_A),before=JSON.stringify(v),d=decode(v);expect(d).not.toBeNull();expect(JSON.stringify(v)).toBe(before)
  const h=d!.salesRemainderChange!;expect(Object.keys(h)).toHaveLength(10);expect(Object.keys(h.change)).toHaveLength(19);expect(Object.keys(h.line)).toHaveLength(9)
  expect(h.version).toBe('9007199254740993');expect(h.change.requestedQuantity).toBe('0001.2500');expect(h.change.requestedAtUtc).toBe('2026-11-28T09:00:00.0000001Z')
  expect(decodeWorkDecisionPreview(v,COMPANY_A.toUpperCase(),kind,id.toUpperCase())).not.toBeNull()
 })
 it.each(['Approved','Rejected'] as const)('keeps terminal%s and does not impose a new global requester maker ban',state=>{
  const v=fixture(COMPANY_A,'9',state);expect(v.salesRemainderChange!.change.decidedBy).toBe(v.salesRemainderChange!.change.requestedBy);expect(decode(v)).not.toBeNull()
  expect(salesRemainderChangeWorkCommand(v.salesRemainderChange!,true,'',null)).toBeNull()
 })
 it('keeps Cancel nullable requested quantity and zero decision floor lossless without computing equality',()=>{
  const v=fixture(COMPANY_A,'9','Approved');const h=v.salesRemainderChange!;const changed={...h,change:{...h.change,kind:'Cancel' as const,requestedQuantity:null,resultQuantity:'0',loadedAtDecision:'0'}}
  expect(human(changed,COMPANY_A,id,isWorkPackageEffectiveDate,exactNativeClrAmount)).not.toBeNull()
 })
 it.each(['root-extra','human-extra','change-extra','line-extra','missing','draft-id','line-id','lineNo','company','state','pending','approval','rejection','version-number','version-zero','version-pad','version-overflow','changeNo-overflow','revision-overflow','lineNo-overflow','change-revision','utc-control','utc-date','reason','requester','decision','quantity-LF','quantity-CR','quantity-overflow','quantity-scale','quantity-zero','cancel-quantity','threshold','actual','unit','reference','line-reference','caption','foreign-human'])('rejects malformed or crossbound %s',fault=>{
  const v=structuredClone(fixture(COMPANY_A)) as unknown as Record<string,unknown>,h=record(v.salesRemainderChange),r=record(h.change),l=record(h.line)
  if(fault==='root-extra')v.unexpected=true;if(fault==='human-extra')h.previewDigest='a'.repeat(64);if(fault==='change-extra')r.originalRemainderDecisionReceipt={};if(fault==='line-extra')l.unitPrice='9';if(fault==='missing')delete r.loadedAtRequest
  if(fault==='draft-id')h.draftId=COMPANY_B;if(fault==='line-id')l.id=COMPANY_B;if(fault==='lineNo')l.lineNo=1;if(fault==='company')v.companyId=COMPANY_B
  if(fault==='state')r.state='Stale';if(fault==='pending')v.pending=false;if(fault==='approval')v.approvalSupported=false;if(fault==='rejection')v.rejectionSupported=false
  if(fault==='version-number')h.version=1;if(fault==='version-zero')h.version='0';if(fault==='version-pad')h.version='01';if(fault==='version-overflow')h.version='9223372036854775808'
  if(fault==='changeNo-overflow')r.changeNo=2147483648;if(fault==='revision-overflow')h.revision=2147483648;if(fault==='lineNo-overflow')r.lineNo=l.lineNo=2147483648;if(fault==='change-revision')r.revision='Rev.2147483648'
  if(fault==='utc-control')r.requestedAtUtc=String(r.requestedAtUtc)+'\n';if(fault==='utc-date')r.requestedAtUtc='2026-02-30T09:00:00Z';if(fault==='reason')r.reason='';if(fault==='requester')r.requestedBy='';if(fault==='decision')r.decidedBy=COMPANY_B
  if(fault==='quantity-LF')r.requestedQuantity='1.25\n';if(fault==='quantity-CR')r.requestedQuantity='1.25\r';if(fault==='quantity-overflow')r.requestedQuantity='79228162514264337593543950336';if(fault==='quantity-scale')r.requestedQuantity='0.'+'0'.repeat(28)+'1';if(fault==='quantity-zero')r.requestedQuantity='0';if(fault==='cancel-quantity')r.kind='Cancel'
  if(fault==='threshold')record((v.rules as unknown[])[0]).threshold='2';if(fault==='actual')record((v.rules as unknown[])[0]).actual='2';if(fault==='unit')record((v.rules as unknown[])[0]).unit='KG'
  if(fault==='reference')record((v.references as unknown[])[0]).recordId=COMPANY_B;if(fault==='line-reference')record((v.references as unknown[])[1]).recordId=COMPANY_B;if(fault==='caption')v.documentLabel='other';if(fault==='foreign-human')v.salesDiscount={}
  expect(decode(v)).toBeNull()
 })
 it('does not reuse or change the legacy decoder arithmetic profile for a Work decision',()=>{
  expect(decodeRemainderChanges({draftVersion:'9',floorStatus:'Unreadable',lines:[],items:[]})).not.toBeNull()
  const h=salesRemainderChangeHuman();expect(human({...h,change:{...h.change,loadedAtRequest:'0',shippedAtRequest:'100',deliveredAtRequest:'200'}},COMPANY_A,id,isWorkPackageEffectiveDate,exactNativeClrAmount)).not.toBeNull() // Owner, not UI, owns eligibility.
 })
 it.each([null,'','   ','  SENTETIK ret  ','x'.repeat(4000)])('accepts native optional rejection reason%s without trimming',reason=>{
  const body={expectedVersion:'6',approve:false,reason,delegationId:null};expect(isWorkDecisionRequest(body,kind)).toBe(true);expect(workDecisionReasonLimit(kind)).toBe(4000)
 })
 it.each(['zero','padded','max','long-reject','approve-reason','extra-target','foreign-selection'])('does not fetch malformed command%s',async fault=>{
  const fetch=vi.fn();vi.stubGlobal('fetch',fetch);const q:Record<string,unknown>={expectedVersion:'6',approve:true,reason:null,delegationId:null}
  if(fault==='zero')q.expectedVersion='0';if(fault==='padded')q.expectedVersion='06';if(fault==='max')q.expectedVersion='9223372036854775807';if(fault==='long-reject'){q.approve=false;q.reason=' '+'x'.repeat(4000)+' '}
  if(fault==='approve-reason')q.reason='';if(fault==='extra-target')q.changeNo=4;if(fault==='foreign-selection')q.salesPriceFloor={lines:[]}
  expect(isWorkDecisionRequest(q,kind)).toBe(false);expect(await sendWorkDecision(COMPANY_A,kind,id,q as never,'SENTETIK')).toEqual({type:'unknown',reason:'unexpected'});expect(fetch).not.toHaveBeenCalled()
 })
 it.each([true,false])('sends only the four frozen fields and original replay approve%s',async approve=>{
  const q=salesRemainderChangeWorkCommand(salesRemainderChangeHuman(),approve,'  SENTETIK ret  ',null)!,fetch=vi.fn().mockResolvedValue(json({companyId:COMPANY_A,kind,itemId:id,state:approve?'Approved':'Rejected',replayed:true,originalCorrelationId:'SENTETIK-original'},200,{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK-original'}));vi.stubGlobal('fetch',fetch)
  expect((await sendWorkDecision(COMPANY_A,kind,id,q,'SENTETIK-key')).type).toBe('applied');const [url,init]=fetch.mock.calls[0]!
  expect(url).toContain(`/work/decisions/${kind}/${id}`);expect(JSON.parse(init.body)).toEqual(q);expect(init.headers['Idempotency-Key']).toBe('SENTETIK-key');expect(Object.keys(q)).toHaveLength(4)
 })
 it('keeps a current draft state masquerading as an original change decision unknown',async()=>{
  vi.stubGlobal('fetch',vi.fn().mockResolvedValue(json({companyId:COMPANY_A,kind,itemId:id,state:'Preparing',replayed:true,originalCorrelationId:'SENTETIK'},200,{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK'})))
  expect((await sendWorkDecision(COMPANY_A,kind,id,{expectedVersion:'6',approve:true,reason:null,delegationId:null},'SENTETIK')).type).toBe('unknown')
 })
 it.each([400,403,404,409,422,503])('preserves native refusal/unknown status%s with no extra read',async status=>{
  const fetch=vi.fn().mockResolvedValue(problem(status));vi.stubGlobal('fetch',fetch);expect((await sendWorkDecision(COMPANY_A,kind,id,{expectedVersion:'6',approve:true,reason:null,delegationId:null},'SENTETIK')).type).toBe(status===503?'unknown':'refused');expect(fetch).toHaveBeenCalledTimes(1)
 })
})
