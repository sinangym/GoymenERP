import { afterEach, describe, expect, it, vi } from 'vitest'
import { decodeWorkDecisionPreview, exactNativeClrAmount, isWorkDecisionRequest, isWorkPackageEffectiveDate, sendWorkDecision, WORK_DECISION_KINDS } from './workDecisionApi'
import { inventoryCostFixture, inventoryCostGuid as g } from './inventoryCostDecisionFixture'
import { validInventoryCostPreview, validInventoryCostRetainedProposal } from './workInventoryCostDecision'
import { inventoryCostCancellationFixture as fixture, cancellationWorkId as id } from './inventoryCostCancellationDecisionFixture'
import { INVENTORY_COST_CANCELLATION_WORK_KIND as kind, inventoryCostCancellationRequest, validInventoryCostCancellationPreview, validInventoryCostCancellationRequest } from './workInventoryCostCancellationDecision'

const company='ac560000-0000-4000-8000-000000000001',other='ac560000-0000-4000-8000-000000000002'
const valid=(v:Record<string,unknown>)=>validInventoryCostCancellationPreview(v,company,id,isWorkPackageEffectiveDate,exactNativeClrAmount)
const object=(v:unknown):v is Record<string,unknown>=>typeof v==='object'&&v!==null
function put(target:unknown,path:readonly(string|number)[],value:unknown) {
 let parent=target
 for(const key of path.slice(0,-1)){if(!object(parent))throw new Error('Missing fixture parent');parent=parent[key]}
 if(!object(parent))throw new Error('Missing fixture target');parent[path[path.length-1]!]=value
}
describe('inventory cost cancellation Work core',()=>{
 it.each(['79228162514264337593543950336','99999999999999999999999999999','0.00000000000000000000000000001'])('rejects nonrepresentable CLR cancellation amounts %s',amount=>{
  for(const field of ['signedAmount','journalTotal']){const v=fixture(company);for(const section of ['preparedCancellation','cancellation'])put(v,['inventoryCostCancellation',section,'components',0,field],amount);expect(valid(v)).toBe(false)}
 })
 it.each(['79228162514264337593543950335','0.0000000000000000000000000001'])('preserves exact CLR boundary text %s',amount=>{
  const v=fixture(company);for(const section of ['preparedCancellation','cancellation'])for(const field of ['signedAmount','journalTotal'])put(v,['inventoryCostCancellation',section,'components',0,field],amount)
  expect(valid(v)).toBe(true);expect(v.inventoryCostCancellation.preparedCancellation.components[0]!.signedAmount).toBe(amount)
 })
 it('keeps a representable negative manufacturing amount and rejects its overflow without changing its sign',()=>{
  const v=fixture(company);expect(valid(v)).toBe(true);expect(v.inventoryCostCancellation.preparedCancellation.components[1]!.signedAmount).toBe('-0.01')
  for(const section of ['preparedCancellation','cancellation'])put(v,['inventoryCostCancellation',section,'components',1,'signedAmount'],'-79228162514264337593543950336');expect(valid(v)).toBe(false)
 })
 it.each(['AwaitingPreparation','NoCost','AlreadyAccounted'] as const)('preserves genuine unprepared N1 %s without fabricating a preparation',status=>{
  const v=fixture(company,'Prepared',status),before=JSON.stringify(v),p=v.inventoryCostCancellation.originalProposal
  expect(p.preparation).toBeUndefined();expect(valid(v)).toBe(true);expect(p.version).toBe('1');expect(v.sourceVersion).toBe('7')
  expect(Object.keys(v.inventoryCostCancellation)).toHaveLength(5);expect(Object.keys(v.inventoryCostCancellation.sourceCancellation)).toHaveLength(10)
  expect(JSON.stringify(v)).toBe(before);expect(validInventoryCostCancellationPreview(v,company.toUpperCase(),id.toUpperCase(),isWorkPackageEffectiveDate,exactNativeClrAmount)).toBe(true)
  expect(validInventoryCostRetainedProposal(p,company,id,isWorkPackageEffectiveDate)).toBe(true)
 })
 it.each(['Prepared','Posted','RoundedNoCost','Stale'] as const)('reuses the exact prepared/terminal original proposal validator for %s',status=>{
  const v=fixture(company,'Prepared',status,'9007199254740993','7');expect(valid(v)).toBe(true)
  expect(v.inventoryCostCancellation.originalProposal.preparation).toBeDefined()
  put(v,['inventoryCostCancellation','originalProposal','preparation','moneyProfileVersion'],'9223372036854775808');expect(valid(v)).toBe(false)
 })
 it('does not confuse retained unprepared N1 with an impossible prepared N1',()=>{
  expect(valid(fixture(company,'Prepared','Prepared','7','1'))).toBe(false)
 })
 it.each(['accounting.invoiced-shipment-cost.v1','accounting.purchase-cost-difference.v1'] as const)('keeps existing55 %s approval preparation/version/status rules unchanged',kind=>{
  const v=inventoryCostFixture(company,kind);expect(validInventoryCostPreview(v,company,id,kind,isWorkPackageEffectiveDate)).toBe(true)
  Reflect.deleteProperty(v.inventoryCost.proposal,'preparation');expect(validInventoryCostPreview(v,company,id,kind,isWorkPackageEffectiveDate)).toBe(false)
  const n1=inventoryCostFixture(company,kind);Object.assign(n1.inventoryCost.proposal,{version:'1',status:'AwaitingPreparation'});n1.sourceVersion='1'
  expect(validInventoryCostPreview(n1,company,id,kind,isWorkPackageEffectiveDate)).toBe(false)
 })
 it('retains full rows, source legal reason, raw signed amounts and native UTC ticks without calculating',()=>{
  const v=fixture(company),h=v.inventoryCostCancellation,base=h.preparedCancellation.components[0]!
  const components=Array.from({length:13},(_,i)=>({...base,journalSourceId:g(100+i),sourceJournalId:g(120+i)}))
  Object.assign(h.preparedCancellation,{components});Object.assign(h.cancellation,{components:structuredClone(components)})
  expect(valid(v)).toBe(true);expect(h.cancellation.components).toHaveLength(13)
  expect(h.sourceCancellation.reason.length).toBeGreaterThan(1000);expect(h.sourceCancellation.reason).toContain('\n')
  expect(h.preparedRecordedAtUtc).toBe('2026-11-28T10:00:00.0000001Z');expect(h.preparedCancellation.moneyProfiles[0]?.version).toBe('9223372036854775807')
  const signed=fixture(company);expect(valid(signed)).toBe(true);expect(signed.inventoryCostCancellation.preparedCancellation.components[1]?.signedAmount).toBe('-0.01')
 })
 it('keeps original N unchanged while current M advances once and permits a terminal MaxValue human result',()=>{
  const v=fixture(company,'Approved','Prepared','9223372036854775806','9007199254740993'),h=v.inventoryCostCancellation
  expect(valid(v)).toBe(true);expect(h.cancellation.version).toBe('9223372036854775807');expect(h.originalProposal.version).toBe('9007199254740993')
  expect(v.sourceVersion).toBe('9223372036854775806');expect(inventoryCostCancellationRequest(h,v.pending,null)).toBeNull()
  Object.assign(h.cancellation,{version:'9223372036854775806'});expect(valid(v)).toBe(false)
 })
 it.each(['kind','company','item','caption','rule','actual','ref-id','ref-role','ref-label','ref-link','source-version','reason-flag','reject-flag'])('refuses source/formatter binding mismatch %s',fault=>{
  const v=fixture(company)
  if(fault==='kind')Object.assign(v,{kind:'accounting.invoiced-shipment-cost.v1'})
  if(fault==='company')v.companyId=other;if(fault==='item')v.itemId=g(90);if(fault==='caption')v.documentLabel='other'
  if(fault==='rule')v.rules[0]!.ruleCode='other';if(fault==='actual')Object.assign(v.rules[0]!,{actual:'0'})
  if(fault==='ref-id')v.references[0]!.recordId=g(90);if(fault==='ref-role')v.references[1]!.role='other';if(fault==='ref-label')v.references[2]!.label='other';if(fault==='ref-link')Object.assign(v.references[1]!,{link:'/native'})
  if(fault==='source-version')v.sourceVersion='8';if(fault==='reason-flag')v.approvalReasonRequired=true;if(fault==='reject-flag')v.rejectionSupported=true
  expect(valid(v)).toBe(false)
 })
 it.each(['companyId','cancellationId','sourceDocumentId','invoiceId','finalizedVersion','documentKind'])('binds source cancellation %s to retained original facts',key=>{
  const v=fixture(company);Object.assign(v.inventoryCostCancellation.sourceCancellation,{[key]:key==='finalizedVersion'?'2':key==='documentKind'?'purchase-invoice':other});expect(valid(v)).toBe(false)
 })
 it.each(['id','companyId','inventoryCostProposalId','cancellationId','invoiceLineRevision','periodId','accountingDate','transactionDate','reason','preparedBy','preparedAtUtc','profile','component'])('never substitutes another preparation/current fact %s',key=>{
  const v=fixture(company),c=v.inventoryCostCancellation.cancellation
  if(key==='profile')Object.assign(c.moneyProfiles[0]!,{version:'2'});else if(key==='component')Object.assign(c.components[0]!,{signedAmount:'100.02'})
  else Object.assign(c,{[key]:key==='invoiceLineRevision'?'2':key==='accountingDate'||key==='transactionDate'?'2026-11-29':key==='reason'?'other':key==='preparedAtUtc'?'2026-11-28T11:00:00Z':other})
  expect(valid(v)).toBe(false)
 })
 it.each(['wrapper','original','preparation','original-component','prepared','component','profile','source'])('rejects unknown/private properties at %s',branch=>{
  const v=fixture(company,'Prepared','Prepared','7','7'),h=v.inventoryCostCancellation
  const target=branch==='wrapper'?h:branch==='original'?h.originalProposal:branch==='preparation'?h.originalProposal.preparation:branch==='original-component'?h.originalProposal.component:branch==='prepared'?h.preparedCancellation:branch==='component'?h.preparedCancellation.components[0]:branch==='profile'?h.preparedCancellation.moneyProfiles[0]:h.sourceCancellation
  Object.assign(target!,{sourceProofJson:'SENTETIK private'});expect(valid(v)).toBe(false)
 })
 it.each(['0','01','+1','1.0','1\n','9223372036854775808',9007199254740992,null])('refuses malformed or lossy version %j in all independent layers',raw=>{
  for(const path of [['originalProposal','version'],['originalProposal','invoiceVersion'],['preparedCancellation','version'],['preparedCancellation','invoiceLineRevision'],['preparedCancellation','moneyProfiles',0,'version'],['cancellation','version'],['sourceCancellation','finalizedVersion']] as const){
   const v=fixture(company);put(v.inventoryCostCancellation,path,raw);expect(valid(v),path.join('.')).toBe(false)
  }
 })
 it.each(['2026-02-30','0001-01-01','2026-01-01T24:00:00Z','2026-02-30T08:00:00Z'])('refuses normalized/default date and time %s',raw=>{
  const v=fixture(company);Object.assign(v.inventoryCostCancellation,{preparedRecordedAtUtc:raw});expect(valid(v)).toBe(false)
  const date=fixture(company);Object.assign(date.inventoryCostCancellation.preparedCancellation,{accountingDate:raw});expect(valid(date)).toBe(false)
 })
 it('retains native Int32 and closed nested types through the shared full-tree validator',()=>{
  const v=fixture(company);put(v.inventoryCostCancellation.originalProposal,['component','invoiceLineNumber'],2147483647);expect(valid(v)).toBe(true)
  put(v.inventoryCostCancellation.originalProposal,['component','invoiceLineNumber'],2147483648);expect(valid(v)).toBe(false)
  const q=fixture(company);put(q.inventoryCostCancellation.originalProposal,['component','alreadyPostedComponents',0,'shipment','hasReturns'],['false']);expect(valid(q)).toBe(false)
 })
 it('requires actual approved reversal IDs only on nonzero components and retains zero acknowledgements without a fabricated journal',()=>{
  const v=fixture(company,'Approved');expect(valid(v)).toBe(true)
  expect(v.inventoryCostCancellation.cancellation.components[2]?.reversingJournalId).toBeUndefined()
  Object.assign(v.inventoryCostCancellation.cancellation.components[2]!,{reversingJournalId:g(99)});expect(valid(v)).toBe(false)
  const q=fixture(company,'Approved');Reflect.deleteProperty(q.inventoryCostCancellation.cancellation.components[0]!,'reversingJournalId');expect(valid(q)).toBe(false)
  const r=fixture(company);Object.assign(r.inventoryCostCancellation.preparedCancellation.components[0]!,{reversingJournalId:g(99)});expect(valid(r)).toBe(false)
 })
 it('requires profiles per currency, native scale and an independent checker',()=>{
  const zero=fixture(company);Object.assign(zero.inventoryCostCancellation.preparedCancellation,{invoiceLineRevision:'0'});Object.assign(zero.inventoryCostCancellation.cancellation,{invoiceLineRevision:'0'});expect(valid(zero)).toBe(true)
  const v=fixture(company);Object.assign(v.inventoryCostCancellation.preparedCancellation.moneyProfiles[0]!,{currencyId:g(90)});expect(valid(v)).toBe(false)
  const q=fixture(company);Object.assign(q.inventoryCostCancellation.preparedCancellation.moneyProfiles[0]!,{scale:29});expect(valid(q)).toBe(false)
  const r=fixture(company,'Approved');Object.assign(r.inventoryCostCancellation.cancellation,{approvedBy:r.inventoryCostCancellation.preparedCancellation.preparedBy});expect(valid(r)).toBe(false)
 })
 it('freezes exactly M and N with no human facts and no implicit N increment',()=>{
  const v=fixture(company,'Prepared','Prepared','9007199254740993','9223372036854775807'),h=v.inventoryCostCancellation
  expect(valid(v)).toBe(true);const request=inventoryCostCancellationRequest(h,true,g(80))
  expect(request).toEqual({expectedVersion:'9007199254740993',approve:true,reason:null,delegationId:g(80),inventoryCostCancellation:{expectedProposalVersion:'9223372036854775807'}})
  expect(Object.isFrozen(request)).toBe(true);expect(Object.isFrozen(request?.inventoryCostCancellation)).toBe(true)
  Object.assign(h.originalProposal,{version:'2'});Object.assign(h.preparedCancellation,{version:'3'});Object.assign(h.cancellation,{version:'3'})
  expect(request?.expectedVersion).toBe('9007199254740993');expect(request?.inventoryCostCancellation.expectedProposalVersion).toBe('9223372036854775807')
  expect(JSON.stringify(request)).not.toContain('proposalId');expect(JSON.stringify(request)).not.toContain('100.01');expect(JSON.stringify(request)).not.toContain('SENTETIK')
 })
 it.each(['max','not-pending','approved'])('does not create a new command for %s',fault=>{
  const v=fixture(company,fault==='approved'?'Approved':'Prepared','AwaitingPreparation',fault==='max'?'9223372036854775807':'7')
  expect(valid(v)).toBe(true);expect(inventoryCostCancellationRequest(v.inventoryCostCancellation,fault==='not-pending'?false:v.pending,null)).toBeNull()
 })
 it.each([{approve:false},{reason:'reason'},{expectedVersion:'9223372036854775807'},{inventoryCostCancellation:{expectedProposalVersion:'0'}},{inventoryCostCancellation:{expectedProposalVersion:9007199254740992}},{inventoryCostCancellation:{expectedProposalVersion:'1',extra:true}},{delegationId:'00000000-0000-0000-0000-000000000000'},{otherOwnerSelection:{}}])('keeps the exact approve-only five-field command %j',patch=>{
  const request={expectedVersion:'1',approve:true,reason:null,delegationId:null,inventoryCostCancellation:{expectedProposalVersion:'1'}}
  expect(validInventoryCostCancellationRequest(request)).toBe(true);expect(validInventoryCostCancellationRequest({...request,...patch})).toBe(false)
 })
})

afterEach(()=>vi.unstubAllGlobals())
describe('published cancellation Work transport',()=>{
 it('decodes the actual five-field human wrapper and binds uppercase route GUIDs without rewriting raw facts',()=>{
  const v=fixture(company),raw=JSON.stringify(v)
  expect(decodeWorkDecisionPreview(v,company.toUpperCase(),kind,id.toUpperCase())?.inventoryCostCancellation).toEqual(v.inventoryCostCancellation)
  expect(JSON.stringify(v)).toBe(raw);expect(WORK_DECISION_KINDS.filter(value=>value===kind)).toEqual([kind]);expect(WORK_DECISION_KINDS).toHaveLength(54)
 })
 it.each(WORK_DECISION_KINDS.filter(value=>value!==kind))('rejects a foreign cancellation selection on %s',otherKind=>{
  expect(isWorkDecisionRequest({expectedVersion:'7',approve:true,reason:null,delegationId:null,inventoryCostCancellation:{expectedProposalVersion:'1'}},otherKind)).toBe(false)
 })
 it.each(['inventoryCost','sourceProof','approvalReceipt','sourceFactsDigest'])('rejects an unexpected cross-kind/private wrapper %s',field=>{
  expect(decodeWorkDecisionPreview({...fixture(company),[field]:{}},company,kind,id)).toBeNull()
 })
 it('sends only M/N and the four shared fields, accepting the genuine original terminal receipt and replay metadata',async()=>{
  const request=inventoryCostCancellationRequest(fixture(company).inventoryCostCancellation,true,null)!
  const response={companyId:company,kind,itemId:id,state:'CancelledByComponentReversal',replayed:true,originalCorrelationId:'SENTETIK original'}
  const fetcher=vi.fn(async(_url:RequestInfo|URL,_init?:RequestInit)=>new Response(JSON.stringify(response),{status:200,headers:{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK original'}}));vi.stubGlobal('fetch',fetcher)
  expect(await sendWorkDecision(company,kind,id,request,'SENTETIK same key')).toMatchObject({type:'applied',replayed:true,data:{state:'CancelledByComponentReversal',originalCorrelationId:'SENTETIK original'}})
  expect(JSON.parse(String(fetcher.mock.calls[0]?.[1]?.body))).toEqual(request);expect(Object.keys(request)).toHaveLength(5)
  for(const state of ['Approved','Posted','RoundedNoCost','Stale','Cancelled']){fetcher.mockImplementation(async()=>new Response(JSON.stringify({...response,state,replayed:false}),{status:200}));expect((await sendWorkDecision(company,kind,id,request,'SENTETIK same key')).type).toBe('unknown')}
  fetcher.mockImplementation(async()=>new Response(JSON.stringify({...response,extra:true}),{status:200,headers:{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK original'}}));expect((await sendWorkDecision(company,kind,id,request,'SENTETIK same key')).type).toBe('unknown')
 })
 it.each([{approve:false},{reason:'not allowed'},{inventoryCostCancellation:undefined},{inventoryCostCancellation:{expectedProposalVersion:'0'}},{postingAccountMappingSelection:{expectedActiveMappingVersion:'1'}}])('refuses invalid command before fetch %j',patch=>{
  const request=inventoryCostCancellationRequest(fixture(company).inventoryCostCancellation,true,null)!
  expect(isWorkDecisionRequest({...request,...patch},kind)).toBe(false)
 })
})
