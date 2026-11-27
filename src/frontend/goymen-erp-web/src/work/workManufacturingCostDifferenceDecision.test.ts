import {afterEach,describe,expect,it,vi} from 'vitest'
import {manufacturingCostFixture as fixture,manufacturingCostId as id} from './manufacturingCostDifferenceDecisionFixture'
import {MANUFACTURING_COST_WORK_KIND as kind} from './workManufacturingCostDifferenceDecision'
import {decodeWorkDecisionPreview,isWorkDecisionRequest,isWorkDecisionKind,sendWorkDecision,WORK_DECISION_KINDS} from './workDecisionApi'
const company='aaaaaaaa-0000-4000-8000-000000000001'
const request={expectedVersion:'9007199254740993',approve:true,reason:null,delegationId:null}
afterEach(()=>vi.unstubAllGlobals())
describe('staged manufacturing cost Work purpose',()=>{
 it('retains exact native detail4/header20/output11/plan7 and leaves all 47 public kinds unchanged',()=>{
  const v=fixture(company),h=v.manufacturingCostDifference
  expect(decodeWorkDecisionPreview(v,company,kind,id)?.manufacturingCostDifference).toEqual(h)
  expect(Object.keys(h)).toHaveLength(4);expect(Object.keys(h.proposal)).toHaveLength(20);expect(Object.keys(h.proposal.outputs[0]!)).toHaveLength(11);expect(Object.keys(h.proposal.accountPlan)).toHaveLength(7)
  expect(h.proposal.outputs).toHaveLength(13);expect(isWorkDecisionKind(kind)).toBe(true);expect(WORK_DECISION_KINDS).toHaveLength(47)
  expect(WORK_DECISION_KINDS.some(k=>String(k)===kind)).toBe(false)
  expect(decodeWorkDecisionPreview(v,company.toUpperCase(),kind,id.toUpperCase())).not.toBeNull()
 })
 it.each(['2','7','9007199254740993','9223372036854775807'])('decodes complete native version %s without narrowing to safe JS number',version=>{
  const v=fixture(company,version);expect(decodeWorkDecisionPreview(v,company,kind,id)?.sourceVersion).toBe(version)
 })
 it('preserves native year-one dates, UTC ticks, nullable catalog/USD and raw rational/money text without chronology invention',()=>{
  const v=fixture(company);Object.assign(v.manufacturingCostDifference,{preparedAtUtc:'0001-01-01T00:00:00.0000000Z'})
  Object.assign(v.manufacturingCostDifference.proposal,{accountingDate:'0001-01-02',transactionDate:'9999-12-31',sourceOccurredAtUtc:'9999-12-31T23:59:59.9999999+00:00'})
  v.manufacturingCostDifference.proposal.accountPlan.catalogVersion=null
  expect(decodeWorkDecisionPreview(v,company,kind,id)?.manufacturingCostDifference).toEqual(v.manufacturingCostDifference)
 })
 it.each(['0','01','+2','2\n','2\r','2\r\n','9223372036854775807','9223372036854775808'])('rejects noncanonical or nonincrementable command version %j',version=>{
  expect(isWorkDecisionRequest({...request,expectedVersion:version},kind)).toBe(false)
 })
 it('requires the four exact command fields, approve/null reason and no proof or foreign selection',()=>{
  expect(isWorkDecisionRequest(request,kind)).toBe(true)
  for(const extra of [{approve:false},{reason:''},{sourceFactsDigest:'A'.repeat(64)},{manufacturingCostDifference:{}},{normalJournalDraft:{periodId:id}},{postingAccountMappingSelection:{expectedActiveMappingVersion:'0'}},{approvedSourceCatalogSelection:{expectedActiveCatalogVersion:'0'}},{delegationId:id+'\n'}])expect(isWorkDecisionRequest({...request,...extra},kind)).toBe(false)
 })
 it.each(['missing','foreign','id','private','cross','numeric','header','outputs','duplicate','output-extra','plan','plan-bind','status','pending','maker','reason','ref','caption','rule','date','utc'])('rejects partial, foreign or invalid native proof %s',fault=>{
  const v=fixture(company),h=v.manufacturingCostDifference,p=h.proposal;let raw:unknown=v
  if(fault==='missing')raw={...v,manufacturingCostDifference:undefined}
  else if(fault==='foreign')Object.assign(p,{companyId:id})
  else if(fault==='id')Object.assign(p,{proposalId:company})
  else if(fault==='private')Object.assign(p,{approvalReceipt:{}})
  else if(fault==='cross')raw={...v,postingAccountMapping:{}}
  else if(fault==='numeric')Object.assign(p,{version:9007199254740992})
  else if(fault==='header')Reflect.deleteProperty(p,'reason')
  else if(fault==='outputs')p.outputs=[]
  else if(fault==='duplicate')p.outputs=[p.outputs[0]!,p.outputs[0]!]
  else if(fault==='output-extra')Object.assign(p.outputs[0]!,{positions:[]})
  else if(fault==='plan')Object.assign(p,{accountPlan:null})
  else if(fault==='plan-bind')p.accountPlan.debit.accountId=id
  else if(fault==='status')Object.assign(p,{status:'AwaitingPreparation'})
  else if(fault==='pending')v.pending=false
  else if(fault==='maker'){Object.assign(p,{status:'Posted',approvedBy:p.preparedBy,journalId:id});v.pending=false}
  else if(fault==='reason')p.reason=' source reason '
  else if(fault==='ref')v.references[0]!.recordId=company
  else if(fault==='caption')v.documentLabel='wrong'
  else if(fault==='rule')v.rules[0]!.actual='601'
  else if(fault==='date')p.accountingDate='2026-02-30'
  else h.preparedAtUtc='2026-11-28T24:00:00Z'
  expect(decodeWorkDecisionPreview(raw,company,kind,id)).toBeNull()
 })
 it.each(['sourceFactsDigest','previewDigest'] as const)('rejects both lowercase and exact-length LF/CR/CRLF digest replacements: %s',field=>{
  for(const ending of ['\n','\r','\r\n']){
   const v=fixture(company);v.manufacturingCostDifference[field]='A'.repeat(64-ending.length)+ending
   expect(decodeWorkDecisionPreview(v,company,kind,id)).toBeNull()
  }
  const v=fixture(company);v.manufacturingCostDifference[field]='a'.repeat(64);expect(decodeWorkDecisionPreview(v,company,kind,id)).toBeNull()
 })
 it.each(['signedTlDifference','generationVersion'] as const)('rejects suffix controls for %s',field=>{
  for(const suffix of ['\n','\r','\r\n']){const v=fixture(company);v.manufacturingCostDifference.proposal[field]+=suffix;expect(decodeWorkDecisionPreview(v,company,kind,id)).toBeNull()}
 })
 it('rejects suffixes in every output raw numeric/ratio field and canonical plan version',()=>{
  for(const field of ['netQuantityKg','remainingQuantityKg','consumedQuantityKg','tlUnitCostPerKg','remainingTlValue','consumedTlDifference','usdUnitCostPerKg','originalTlAmount'] as const)
   for(const suffix of ['\n','\r','\r\n']){const v=fixture(company);Object.assign(v.manufacturingCostDifference.proposal.outputs[0]!,{[field]:'1'+suffix});expect(decodeWorkDecisionPreview(v,company,kind,id)).toBeNull()}
  for(const field of ['packageVersion','catalogVersion'] as const){const v=fixture(company);v.manufacturingCostDifference.proposal.accountPlan[field]='2\n';expect(decodeWorkDecisionPreview(v,company,kind,id)).toBeNull()}
 })
 it.each(['Posted','Approved','Rejected','Prepared'])('accepts only exact native Posted success (%s)',async state=>{
  vi.stubGlobal('fetch',vi.fn(async()=>new Response(JSON.stringify({companyId:company,kind,itemId:id,state,replayed:false,originalCorrelationId:'SENTETIK original'}),{status:200})))
  const result=await sendWorkDecision(company,kind,id,request,'SENTETIK key');expect(result.type).toBe(state==='Posted'?'applied':'unknown')
 })
})
