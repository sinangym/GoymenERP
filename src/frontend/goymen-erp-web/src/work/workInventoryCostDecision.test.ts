import {afterEach,describe,expect,it,vi} from 'vitest'
import {inventoryCostFixture as fixture,successionCostFixture,firstPurchaseCostFixture,cancellationCostFixture,inventoryCostId as id,inventoryCostGuid} from './inventoryCostDecisionFixture'
import {isInventoryCostWorkKind,inventoryCostWorkLong} from './workInventoryCostDecision'
import {decodeWorkDecisionPreview,isWorkDecisionRequest,isWorkDecisionKind,sendWorkDecision} from './workDecisionApi'
import {decodeInventoryCost,costRatio,roundingProof} from '../accounting/inventoryCostApi'
const company='aaaaaaaa-0000-4000-8000-000000000001',other='bbbbbbbb-0000-4000-8000-000000000001'
const kinds=['accounting.invoiced-shipment-cost.v1','accounting.purchase-cost-difference.v1'] as const
const req={expectedVersion:'7',approve:true,reason:null,delegationId:null}
const object=(v:unknown):v is Record<string,unknown>=>typeof v==='object'&&v!==null
function put(target:unknown,path:readonly (string|number)[],value:unknown){let parent=target
 for(const key of path.slice(0,-1)){if(!object(parent))throw new Error('Fixture parent absent');parent=parent[key]}
 if(!object(parent))throw new Error('Fixture target absent');parent[path[path.length-1]!]=value
}
const decode=(v:ReturnType<typeof fixture>)=>decodeWorkDecisionPreview(v,company,v.kind,id)
afterEach(()=>vi.unstubAllGlobals())
describe('inventory cost Work closed human whitelist',()=>{
 it.each(kinds)('binds %s to company/id/native kind and retains all raw business facts without mutation',kind=>{
  const v=fixture(company,kind),before=JSON.stringify(v),h=decode(v)?.inventoryCost
  expect(h).toEqual(v.inventoryCost);expect(Object.keys(h!)).toHaveLength(2)
  expect(h?.proposal.invoiceVersion).toBe('9007199254740993');expect(h?.proposal.preparation.moneyProfileVersion).toBe('9223372036854775807')
  expect(h?.proposal.exactTlAmount).toBe('100.01');expect(h?.proposal.preparation.reason).toBe('SENTETIK maliyet hazırlama gerekçesi')
  expect(JSON.stringify(v)).toBe(before);expect(decodeWorkDecisionPreview(v,company.toUpperCase(),kind,id.toUpperCase())).not.toBeNull()
  expect(isWorkDecisionKind(kind)).toBe(true);expect(isInventoryCostWorkKind(kind)).toBe(true)
  expect(decodeWorkDecisionPreview(v,other,kind,id)).toBeNull();expect(decodeWorkDecisionPreview(v,company,kind,inventoryCostGuid(99))).toBeNull()
  expect(decodeWorkDecisionPreview(v,company,kind===kinds[0]?kinds[1]:kinds[0],id)).toBeNull()
  expect(decodeInventoryCost(v.inventoryCost.proposal,company,kind===kinds[0]?1:2)).toBeNull() // No fabricated native proof payload.
 })
 it('keeps all manufacturing/purchase rows, nullable omission, exact fractions and UTC ticks',()=>{
  const v=fixture(company),c=v.inventoryCost.proposal.component,m=c.alreadyPostedComponents![0]!
  Object.assign(c,{alreadyPostedComponents:Array.from({length:13},()=>structuredClone(m))})
  const h=decode(v)?.inventoryCost;expect(h?.proposal.component.alreadyPostedComponents).toHaveLength(13)
  expect(h?.proposal.component.alreadyPostedComponents?.[0].shipment.netRootQuantityKg).toEqual({numerator:'1',denominator:'3'})
  expect(h?.proposal.component.alreadyPostedPurchaseComponents?.[0].approval.recordedRounding.moneyProfileVersion).toBe('9223372036854775807')
  expect(h?.preparedRecordedAtUtc).toBe('2026-11-28T10:00:00.0000001Z')
  Reflect.deleteProperty(v.inventoryCost.proposal,'officialInvoiceNumber');v.documentLabel=id.slice(0,8);v.references[0]!.label=v.inventoryCost.proposal.invoiceId.slice(0,8)
  expect(decode(v)).not.toBeNull();Object.assign(v.inventoryCost.proposal,{officialInvoiceNumber:null});expect(decode(v)).toBeNull()
 })
 it('preserves full native calendar boundaries and signed purchase differences without calculations',()=>{
  const v=fixture(company,kinds[1]),p=v.inventoryCost.proposal
  Object.assign(p,{invoiceDate:'0001-01-02',exactTlAmount:'-100.01'});Object.assign(p.component,{signedTlAmount:{numerator:'-10001',denominator:'100'}})
  Object.assign(p.preparation,{accountingDate:'9999-12-31',transactionDate:'0099-01-01',signedAmount:'-100.01',preparedAtUtc:'0001-01-01T00:00:00.0000001Z'})
  v.rules[0]!.actual='-100.01';expect(decode(v)?.inventoryCost).toEqual(v.inventoryCost)
 })
 it.each(['caption','referenceId','referenceLabel','rule','amount','unit'])('rejects mismatched source formatter binding %s',fault=>{
  const v=fixture(company)
  if(fault==='caption')v.documentLabel='SENTETIK different'
  if(fault==='referenceId')v.references[1]!.recordId=inventoryCostGuid(99)
  if(fault==='referenceLabel')v.references[1]!.label='SENTETIK wrong period'
  if(fault==='rule')v.rules[0]!.ruleCode='accounting.purchase-cost-difference-approval-required'
  if(fault==='amount')v.rules[0]!.actual='100.02'
  if(fault==='unit')v.rules[0]!.unit='EUR'
  expect(decode(v)).toBeNull()
 })
 it.each(['Posted','RoundedNoCost','Stale'] as const)('reads native terminal %s without converting it into pending approval',state=>{
  const v=fixture(company,kinds[0],state,'9223372036854775807');expect(decode(v)?.inventoryCost?.proposal.status).toBe(state)
  expect(isWorkDecisionRequest({...req,expectedVersion:v.sourceVersion},kinds[0])).toBe(false)
  Object.assign(v,{pending:true});expect(decode(v)).toBeNull()
 })
 it('allows a source-cancelled prepared human snapshot read-only and retains renewal/succession/physical generations',()=>{
  const v=successionCostFixture(company),p=v.inventoryCost.proposal
  expect(decode(v)?.inventoryCost?.proposal.component.claimSuccession?.claims[0].previousGeneration).toBe('9223372036854775807')
  v.pending=false;expect(decode(v)?.pending).toBe(false)
  Object.assign(p.component.claimSuccession!.claims[0].physical,{quantityKg:2});expect(p.component.salesPhysicalClaims![0]!.quantityKg).toBe(1);expect(decode(v)).toBeNull()
 })
 it('keeps source and preparation rounding profiles separate without recomputing either',()=>{
  const v=firstPurchaseCostFixture(company)
  expect(decode(v)?.inventoryCost?.proposal.component.recordedRounding?.moneyProfileVersion).toBe('9007199254740993')
  expect(decode(v)?.inventoryCost?.proposal.preparation.moneyProfileVersion).toBe('9223372036854775807')
  Object.assign(v.inventoryCost.proposal.preparation.rounding!,{roundedSignedAmount:'101'});expect(decode(v)).toBeNull()
 })
 it('retains the cancellation whitelist/profile long versions and refuses wrong parent/company/raw economic key',()=>{
  const v=cancellationCostFixture(company),c=v.inventoryCost.proposal.componentCancellation!
  expect(decode(v)?.inventoryCost?.proposal.componentCancellation).toEqual(c)
  for(const patch of [{companyId:other},{inventoryCostProposalId:inventoryCostGuid(88)},{cancellationId:inventoryCostGuid(89)},{version:9007199254740992},{invoiceLineRevision:'-1'}]){
   const raw=cancellationCostFixture(company);Object.assign(raw.inventoryCost.proposal.componentCancellation!,patch);expect(decode(raw)).toBeNull()
  }
  Object.assign(c.components[0]!,{economicKey:'A'.repeat(64)});expect(decode(v)).toBeNull()
 })
 it.each(['previousFinalizedVersion','previousProposalVersion','invoiceLineRevision','previousGeneration','profileVersion','cancellationVersion'])('checks retained succession/cancellation long %s without Number conversion',field=>{
  const v=cancellationCostFixture(company),p=v.inventoryCost.proposal
  const target=field==='previousGeneration'?p.component.claimSuccession!.claims[0]:field==='profileVersion'?p.componentCancellation!.moneyProfiles[0]:field==='cancellationVersion'?p.componentCancellation:p.component.claimSuccession
  Object.assign(target!,{[field==='profileVersion'||field==='cancellationVersion'?'version':field]:'9223372036854775808'});expect(decode(v)).toBeNull()
 })
 it.each(['SourceProofJson','physicalProofJson','fingerprint','economicKey','sourceFactsDigest','previewDigest','approvalReceipt','unexpected'])('rejects private/unknown nested property %s',key=>{
  const v=fixture(company);Object.assign(v.inventoryCost.proposal.component,{[key]:'A'.repeat(64)});expect(decode(v)).toBeNull()
 })
 it.each(['wrapper','preparation','manufacturing','shipment','purchase','approval','candidate','return','succession','generation','reversed'])('rejects an extra property at every retained branch: %s',branch=>{
  const v=branch==='succession'||branch==='generation'||branch==='reversed'?successionCostFixture(company):fixture(company),c=v.inventoryCost.proposal.component
  const target=branch==='wrapper'?v.inventoryCost:branch==='preparation'?v.inventoryCost.proposal.preparation:branch==='manufacturing'?c.alreadyPostedComponents![0]:branch==='shipment'?c.alreadyPostedComponents![0]!.shipment:
   branch==='purchase'?c.alreadyPostedPurchaseComponents![0]:branch==='approval'?c.alreadyPostedPurchaseComponents![0]!.approval:branch==='candidate'?c.alreadyPostedPurchaseComponents![0]!.candidates[0]:branch==='return'?c.alreadyPostedPurchaseComponents![0]!.candidates[0]!.shipment.returns[0]:branch==='generation'?c.claimSuccession!.claims[0]:branch==='reversed'?c.claimSuccession!.reversedComponents[0]:c.claimSuccession
  Object.assign(target!,{unknown:'SENTETIK private'});expect(decode(v)).toBeNull()
 })
 it.each(['2','9007199254740993','9223372036854775807'])('keeps exact all nested native long representation %s',long=>{
  const v=fixture(company),p=v.inventoryCost.proposal,c=p.component
  Object.assign(p,{invoiceVersion:long});Object.assign(p.preparation,{moneyProfileVersion:long,accountPlanPackageVersion:long,accountPlanCatalogVersion:long})
  Object.assign(p.preparation.classification,{mappingVersion:long});Object.assign(c.alreadyPostedComponents![0]!,{moneyProfileVersion:long})
  Object.assign(c.alreadyPostedComponents![0]!.shipment,{provenOriginalSequence:long});expect(decode(v)).not.toBeNull()
 })
 it.each(['01','+2','2.0','2\n','9223372036854775808',9007199254740992,null])('rejects lossy/noncanonical nested long %j',value=>{
  for(const path of [['invoiceVersion'],['preparation','moneyProfileVersion'],['preparation','classification','mappingVersion'],['component','alreadyPostedComponents',0,'moneyProfileVersion'],['component','alreadyPostedComponents',0,'shipment','provenOriginalSequence'],['component','alreadyPostedPurchaseComponents',0,'approval','proposalVersion'],['component','alreadyPostedPurchaseComponents',0,'approval','recordedRounding','moneyProfileVersion'],['component','alreadyPostedPurchaseComponents',0,'candidates',0,'shipment','provenOriginalSequence']] as const){
   const v=fixture(company);put(v.inventoryCost.proposal,path,value);expect(decode(v),path.join('.')).toBeNull()
  }
 })
 it.each(['invoiceLineNumber','manufacturingShipment','manufacturingReturn','purchase','purchaseShipment','purchaseReturn'])('enforces native Int32 line boundary in %s',branch=>{
  const path=branch==='invoiceLineNumber'?['invoiceLineNumber']:branch==='manufacturingShipment'?['alreadyPostedComponents',0,'shipment','movementLineNumber']:branch==='manufacturingReturn'?['alreadyPostedComponents',0,'shipment','returns',0,'movementLineNumber']:
   branch==='purchase'?['alreadyPostedPurchaseComponents',0,'movementLineNumber']:branch==='purchaseShipment'?['alreadyPostedPurchaseComponents',0,'candidates',0,'shipment','movementLineNumber']:['alreadyPostedPurchaseComponents',0,'candidates',0,'shipment','returns',0,'movementLineNumber']
  const v=fixture(company);put(v.inventoryCost.proposal.component,path,2147483648);expect(decode(v)).toBeNull()
  // Purchase share and its candidate share one exact movement line identity.
  put(v.inventoryCost.proposal.component,path,2147483647)
  if(branch==='purchase')put(v.inventoryCost.proposal.component,['alreadyPostedPurchaseComponents',0,'candidates',0,'shipment','movementLineNumber'],2147483647)
  if(branch==='purchaseShipment')put(v.inventoryCost.proposal.component,['alreadyPostedPurchaseComponents',0,'movementLineNumber'],2147483647)
  expect(decode(v)).not.toBeNull()
 })
 it('accepts retained false+empty returns but never a nonempty false branch or contradictory receipt relationships',()=>{
  const v=fixture(company),s=v.inventoryCost.proposal.component.alreadyPostedComponents![0]!.shipment
  Object.assign(s,{hasReturns:false,returns:[]});expect(decode(v)).not.toBeNull()
  Object.assign(s,{returns:[{returnId:inventoryCostGuid(90),movementId:inventoryCostGuid(91),movementLineNumber:1,sourceIssueMovementId:s.movementId,quantityKg:1,recordedAtUtc:s.recordedAtUtc}]});expect(decode(v)).toBeNull()
  const q=fixture(company),candidate=q.inventoryCost.proposal.component.alreadyPostedPurchaseComponents![0]!.candidates[0]!
  Object.assign(candidate,{companyId:other});expect(decode(q)).toBeNull()
 })
 it.each([{candidate:null},{candidate:{}},{candidate:'not a candidate'},{candidate:[]}])('returns malformed, without throwing, for malformed nested row %j',({candidate})=>{
  const v=fixture(company);Object.assign(v.inventoryCost.proposal.component.alreadyPostedPurchaseComponents![0]!,{candidates:[candidate]})
  expect(decode(v)).toBeNull()
 })
 it.each(['2026-02-30','2026-01-01T24:00:00Z','2026-02-30T08:00:00Z'])('rejects calendar normalization %s',raw=>{
  const v=fixture(company);Object.assign(v.inventoryCost,{preparedRecordedAtUtc:raw});expect(decode(v)).toBeNull()
 })
 it('preserves native pure validator defaults and does not coerce amounts or protocol versions',()=>{
  expect(costRatio({numerator:'10001',denominator:'100'})).toBe(true);expect(inventoryCostWorkLong('9007199254740993')).toBe(true)
  expect(inventoryCostWorkLong(9007199254740992)).toBe(false)
  expect(roundingProof(fixture(company).inventoryCost.proposal.preparation.rounding)).toBe(true)
 })
 it.each(['1','0','07','+7','7.0','7\n','9223372036854775807'])('refuses unsafe action version %s',expectedVersion=>{
  for(const kind of kinds)expect(isWorkDecisionRequest({...req,expectedVersion},kind)).toBe(false)
 })
 it.each(kinds)('sends only four fields and accepts authentic success states but never Stale/Approved 200 for %s',async kind=>{
  const f=vi.fn(async(_url:RequestInfo|URL,_init?:RequestInit)=>new Response(JSON.stringify({companyId:company,kind,itemId:id,state:'Posted',replayed:true,originalCorrelationId:'SENTETIK original'}),{status:200,headers:{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK original'}}));vi.stubGlobal('fetch',f)
  expect((await sendWorkDecision(company,kind,id,req,'SENTETIK original key')).type).toBe('applied')
  expect(JSON.parse(String(f.mock.calls[0]?.[1]?.body))).toEqual(req)
  for(const state of ['RoundedNoCost','Stale','Approved']){f.mockImplementation(async()=>new Response(JSON.stringify({companyId:company,kind,itemId:id,state,replayed:false,originalCorrelationId:'SENTETIK fresh'}),{status:200}));expect((await sendWorkDecision(company,kind,id,req,'SENTETIK key')).type).toBe(state==='RoundedNoCost'?'applied':'unknown')}
  f.mockImplementation(async()=>new Response(JSON.stringify({title:'Conflict',status:409}),{status:409,headers:{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK original'}}))
  expect(await sendWorkDecision(company,kind,id,req,'SENTETIK key')).toMatchObject({type:'refused',status:409,replayed:true})
  expect(isWorkDecisionRequest({...req,reason:'reason'},kind)).toBe(false);expect(isWorkDecisionRequest({...req,approve:false},kind)).toBe(false)
  expect(isWorkDecisionRequest({...req,approvedSourceCatalogSelection:{expectedActiveCatalogVersion:'0'}},kind)).toBe(false)
 })
})
