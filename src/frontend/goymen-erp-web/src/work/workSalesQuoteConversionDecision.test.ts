import {describe,it,expect,afterEach,vi} from 'vitest'
import {COMPANY_A,COMPANY_B,json} from '../settings/testHarness'
import {decodeQuoteDetail} from '../sales/poolContract'
import {decodeApprovalRequest} from '../sales/approvalRequests'
import {WORK_DECISION_KINDS,decodeWorkDecisionPreview,isWorkDecisionRequest,sendWorkDecision,isWorkPackageEffectiveDate,workDecisionReasonLimit} from './workDecisionApi'
import {decodeWorkSalesQuoteConversionPreview,salesQuoteConversionWorkCommand,SALES_QUOTE_CONVERSION_WORK_KIND as kind} from './workSalesQuoteConversionDecision'
import {salesQuoteConversionHuman as human,salesQuoteConversionFixture as fixture,salesQuoteConversionId as id} from './salesQuoteConversionDecisionFixture'
const decode=(v:unknown)=>decodeWorkSalesQuoteConversionPreview(v,COMPANY_A,id,isWorkPackageEffectiveDate)
function put(root:object,path:readonly string[],value:unknown){let o=root as Record<string,unknown>;for(const k of path.slice(0,-1)){const n=o[k];if(n===null||typeof n!=='object')throw Error('fixture');o=n as Record<string,unknown>}o[path.at(-1)!]=value}
afterEach(()=>vi.unstubAllGlobals())
describe('selected quote owner contract and exact frozen target',()=>{
 it.each(['Pending','Approved','Rejected','Stale'] as const)('preserves complete native5/18/13/14/15 shape %s',status=>{
  const h=human(COMPANY_A,status),before=JSON.stringify(h);expect(decode(h)).toEqual(h);expect(JSON.stringify(h)).toBe(before)
  expect(Object.keys(h)).toHaveLength(5);expect(Object.keys(h.request)).toHaveLength(18);expect(Object.keys(h.quote)).toHaveLength(13);expect(Object.keys(h.quote.quote)).toHaveLength(14);expect(Object.keys(h.quote.lines[0]!)).toHaveLength(15)
  expect(decodeQuoteDetail(h.quote,null)).toEqual(h.quote);expect(decodeWorkDecisionPreview(fixture(COMPANY_A,status),COMPANY_A,kind,id)).not.toBeNull()
  expect(h.quote.lines[0]!.quantity).toBe('0001.2500');expect(h.request.requestedAtUtc).toBe('2026-11-28T09:00:00.0000001Z')
 })
 it('retains native general decoder defaults and accepts actual Rev0 only in Work selected request',()=>{
  const h=human(COMPANY_A);expect(decodeApprovalRequest(h.request)).toBeNull();expect(decode(h)).not.toBeNull()
  expect(decodeWorkSalesQuoteConversionPreview(h,COMPANY_A.toUpperCase(),id.toUpperCase(),isWorkPackageEffectiveDate)).toEqual(h)
 })
 it.each(['1','9007199254740993','9223372036854775807'])('retains Int64 text in both axes %s',version=>{
  expect(decode(human(COMPANY_A,'Pending',version,version))).not.toBeNull()
 })
 it.each([
  [['request','subjectRevision'],'0'],[['request','subjectRevision'],'Rev.01'],[['request','subjectRevision'],'Rev.2147483648'],[['request','requestNo'],2147483648],
  [['request','version'],9007199254740992],[['quote','quote','version'],'9223372036854775808'],[['quote','lines','0','productVersion'],'01'],
  [['request','requestedAtUtc'],'2026-02-30T08:00:00Z'],[['quote','quote','createdAtUtc'],'2026-01-01T24:00:00Z'],[['quote','quote','businessDate'],'2026-02-30'],
  [['request','subjectId'],COMPANY_B],[['quote','quote','companyId'],COMPANY_B],[['request','quantity'],'1'],[['request','decidedBy'],'maker'],
  [['quote','lines','0','quantity'],1.25],[['quote','lines','0','unitPrice'],'2'],[['quote','dueTerms'],{instalments:[]}],[['request','status'],['Pending']],
  [['approvalSupported'],false],[['rejectionSupported'],false],[['requestId'],COMPANY_B],[['request','originalDecisionReceipt'],{}],[['sourceFactsDigest'],'a'.repeat(64)],
 ] as const)('rejects malformed or mixed native facts %j',(path,value)=>{const h=human(COMPANY_A);put(h,path,value);expect(decode(h)).toBeNull()})
 it.each(['sourceVersion','documentLabel','pending','approvalSupported','rejectionSupported','rules','references'] as const)('binds outer %s',field=>{
  const f=fixture(COMPANY_A);put(f,[field],field==='rules'||field==='references'?[]:field==='pending'||field==='approvalSupported'||field==='rejectionSupported'?false:'wrong');expect(decodeWorkDecisionPreview(f,COMPANY_A,kind,id)).toBeNull()
 })
 it('shows only native visible prices and preserves raw commercial text',()=>{
  const h=human(COMPANY_A,'Pending',undefined,undefined,true);expect(decode(h)?.quote.lines[0]!.unitPrice).toBe('00042.5000')
  put(h,['quote','dueTermsDisclosure'],'Withheld');expect(decode(h)).toBeNull()
 })
 it('freezes separate action axes and four selected target fields including Rev0',()=>{
  const h=human(COMPANY_A),approve=salesQuoteConversionWorkCommand(h,true,'ignored',null),reason='  raw\nreason  ',reject=salesQuoteConversionWorkCommand(h,false,reason,null)
  expect(approve).toEqual({expectedVersion:'9007199254740993',approve:true,reason:null,delegationId:null,salesQuoteConversion:{quoteId:h.quote.quote.id,requestNo:3,subjectRevision:0,requestVersion:'1'}})
  expect(reject).toEqual({...approve,expectedVersion:'1',approve:false,reason});expect(isWorkDecisionRequest(approve,kind)).toBe(true);expect(isWorkDecisionRequest(reject,kind)).toBe(true);expect(workDecisionReasonLimit(kind)).toBe(4000)
  for(const other of WORK_DECISION_KINDS.filter(k=>k!==kind))expect(isWorkDecisionRequest(approve,other)).toBe(false)
 })
 it('preserves Approved reject-only and independent Max quote vs request increments',()=>{
  const approved=human(COMPANY_A,'Approved');expect(salesQuoteConversionWorkCommand(approved,true,'',null)).toBeNull();expect(salesQuoteConversionWorkCommand(approved,false,'r',null)?.expectedVersion).toBe('1')
  const max=human(COMPANY_A,'Pending','9223372036854775807');expect(decode(max)).not.toBeNull();expect(salesQuoteConversionWorkCommand(max,true,'',null)).toBeNull();expect(salesQuoteConversionWorkCommand(max,false,'r',null)).not.toBeNull()
 })
 it.each(['','  ','r'.repeat(4001)])('refuses invalid raw mandatory reject text',reason=>expect(salesQuoteConversionWorkCommand(human(COMPANY_A),false,reason,null)).toBeNull())
 it.each(['quoteId','requestNo','subjectRevision','requestVersion'] as const)('requires frozen selection %s',field=>{
  const command=salesQuoteConversionWorkCommand(human(COMPANY_A),true,'',null)!,node:Record<string,unknown>={...command.salesQuoteConversion};delete node[field]
  expect(isWorkDecisionRequest({...command,salesQuoteConversion:node},kind)).toBe(false)
 })
 it('only accepts the original Approved receipt as an approval replay, never a later Rejected current state',async()=>{
  const body=salesQuoteConversionWorkCommand(human(COMPANY_A),true,'',null)!,result={companyId:COMPANY_A,kind,itemId:id,state:'Approved',replayed:true,originalCorrelationId:'original-quote'}
  vi.stubGlobal('fetch',vi.fn(()=>json(result,200,{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original-quote'})))
  expect(await sendWorkDecision(COMPANY_A,kind,id,body,'SENTETIK-key')).toMatchObject({type:'applied',data:{state:'Approved'}})
  result.state='Rejected';expect(await sendWorkDecision(COMPANY_A,kind,id,body,'SENTETIK-key')).toEqual({type:'unknown',reason:'unexpected'})
 })
})
