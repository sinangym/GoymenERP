import {afterEach,describe,expect,it,vi} from 'vitest'
import {postingMappingFixture as fixture} from './postingAccountMappingDecisionFixture'
import {POSTING_MAPPING_KIND as kind,postingMappingWorkId,postingMappingNativeId} from './workPostingAccountMappingDecision'
import {decodeWorkDecisionPreview,isWorkDecisionKind,isWorkDecisionRequest,sendWorkDecision,WORK_DECISION_KINDS} from './workDecisionApi'
const company='aaaaaaaa-0000-4000-8000-000000000001'
const request={expectedVersion:'1',approve:true,reason:null,delegationId:null,postingAccountMappingSelection:{expectedActiveMappingVersion:'0'}}
afterEach(()=>vi.unstubAllGlobals())
describe('staged native posting mapping purpose',()=>{
 it.each(['1','9007199254740993','9223372036854775807'])('roundtrips exact positive native Int64 identity %s through the server UUIDv8 layout',v=>{
  const id=postingMappingWorkId(v);expect(id).not.toBeNull();expect(postingMappingNativeId(id)).toBe(v);expect(postingMappingNativeId(id!.toUpperCase())).toBe(v)
 })
 it('uses the exact known server carrier without hashing or rounding',()=>{
  expect(postingMappingWorkId('1')).toBe('ac520000-0000-8000-8000-010000000000')
  expect(postingMappingWorkId('9223372036854775807')).toBe('ac527fff-ffff-8fff-8fff-ff0000000000')
 })
 it.each(['0','-1','01','+1','1\n','9223372036854775808'])('rejects noncanonical/out of range native identity %j',v=>expect(postingMappingWorkId(v)).toBeNull())
 it.each(['bad','prefix','version','variant','tail','zero','negative'])('rejects invalid custom route carrier %s',fault=>{
  const id=postingMappingWorkId('1')!
  const bad=fault==='bad'?id+'\n':fault==='prefix'?'bc52'+id.slice(4):fault==='version'?id.slice(0,14)+'4'+id.slice(15):fault==='variant'?id.slice(0,19)+'9'+id.slice(20):fault==='tail'?id.slice(0,-1)+'1':fault==='zero'?'ac520000-0000-8000-8000-000000000000':'ac528000-0000-8000-8000-000000000000'
  expect(postingMappingNativeId(bad)).toBeNull()
 })
 it('retains all seven details, twelve native headers/eight row fields and the independent current plan with its paired purpose',()=>{
  const v=fixture(company),id=v.itemId
  expect(decodeWorkDecisionPreview(v,company,kind,id)?.postingAccountMapping).toEqual(v.postingAccountMapping)
  expect(Object.keys(v.postingAccountMapping)).toHaveLength(7);expect(Object.keys(v.postingAccountMapping.mapping)).toHaveLength(12);expect(Object.keys(v.postingAccountMapping.mapping.rows[0]!)).toHaveLength(8)
  expect(isWorkDecisionKind(kind)).toBe(true);expect(WORK_DECISION_KINDS).toHaveLength(54);expect(WORK_DECISION_KINDS.filter(k=>k===kind)).toEqual([kind])
  expect(decodeWorkDecisionPreview(v,company.toUpperCase(),kind,id.toUpperCase())).not.toBeNull()
  expect(v.postingAccountMapping.currentActiveAccountPlan.accounts[0]!.accountId).not.toBe(v.postingAccountMapping.mapping.rows[0]!.accountId)
 })
 it.each(['Active','Superseded'] as const)('accepts actual native terminal %s while retaining separate identity and row version',status=>{
  const v=fixture(company,'9223372036854775807','9223372036854775807',status)
  if(status==='Superseded')Object.assign(v.postingAccountMapping,{expectedActiveMappingVersion:'0'})
  expect(decodeWorkDecisionPreview(v,company,kind,v.itemId)).not.toBeNull()
 })
 it.each(['0','9007199254740993','9223372036854775807'])('freezes active identity %s without increment restriction',v=>{
  expect(isWorkDecisionRequest({...request,postingAccountMappingSelection:{expectedActiveMappingVersion:v}},kind)).toBe(true)
 })
 it.each(['0','01','+1','1\n','9223372036854775807','9223372036854775808'])('rejects nonincrementable command row version %j',v=>expect(isWorkDecisionRequest({...request,expectedVersion:v},kind)).toBe(false))
 it.each(['-1','00','+0','0\n','9223372036854775808'])('rejects invalid active identity %j',v=>expect(isWorkDecisionRequest({...request,postingAccountMappingSelection:{expectedActiveMappingVersion:v}},kind)).toBe(false))
 it('preserves exact five command fields and cross-kind exclusivity',()=>{
  expect(isWorkDecisionRequest(request,kind)).toBe(true)
  for(const extra of [{approve:false},{reason:''},{postingAccountMappingSelection:null},{postingAccountMappingSelection:{}},{postingAccountMappingSelection:{expectedActiveMappingVersion:'0',sourceFactsDigest:'A'.repeat(64)}},{approvedSourceCatalogSelection:{expectedActiveCatalogVersion:'0'}},{delegationId:'00000000-0000-0000-0000-000000000000'}])expect(isWorkDecisionRequest({...request,...extra},kind)).toBe(false)
  for(const other of WORK_DECISION_KINDS.filter(other=>other!==kind))expect(isWorkDecisionRequest(request,other)).toBe(false)
 })
 it.each(['missing','private','cross','foreign','identity','row-version','number','status','pending','activation','same-maker','default-date','bad-date','default-utc','time','plan-bind','foreign-plan','empty-plan','duplicate-plan-id','duplicate-plan-code','empty-name','rows-empty','rows-max','row-extra','row-missing','duplicate-row','unknown-purpose','wrong-key','selector','selector-long','cogs','vat','code','ref','caption','rule','active-identity'])('fails closed on malformed/native-incompatible human %s',fault=>{
  const v=fixture(company),h=v.postingAccountMapping,m=h.mapping;let raw:unknown=v
  if(fault==='missing')raw={...v,postingAccountMapping:undefined}
  else if(fault==='private')Object.assign(m,{activationReceipt:{}})
  else if(fault==='cross')raw={...v,approvedSourceCatalog:{}}
  else if(fault==='foreign')Object.assign(m,{companyId:v.itemId})
  else if(fault==='identity')Object.assign(m,{mappingVersion:'1'})
  else if(fault==='row-version')Object.assign(m,{version:'2'})
  else if(fault==='number')Object.assign(m,{mappingVersion:9007199254740992})
  else if(fault==='status')Object.assign(m,{status:['Prepared']})
  else if(fault==='pending')v.pending=false
  else if(fault==='activation')Object.assign(m,{activatedAtUtc:'2026-11-28T00:00:00Z'})
  else if(fault==='same-maker'){Object.assign(m,{status:'Active',version:'2',activatedByUserId:m.preparedByUserId,activatedAtUtc:m.preparedAtUtc});Object.assign(h,{expectedVersion:'2',expectedActiveMappingVersion:m.mappingVersion});v.sourceVersion='2';v.pending=false}
  else if(fault==='default-date')Object.assign(m,{effectiveFrom:'0001-01-01'})
  else if(fault==='bad-date')Object.assign(m,{effectiveFrom:'2026-02-30'})
  else if(fault==='default-utc')Object.assign(m,{preparedAtUtc:'0001-01-01T00:00:00.0000000Z'})
  else if(fault==='time')Object.assign(m,{preparedAtUtc:'2026-11-28T24:00:00Z'})
  else if(fault==='plan-bind')Object.assign(h.preparedAccountPlan,{sourceVersion:'other'})
  else if(fault==='foreign-plan')Object.assign(h.currentActiveAccountPlan,{companyId:v.itemId})
  else if(fault==='empty-plan')Object.assign(h.currentActiveAccountPlan,{accounts:[]})
  else if(fault==='duplicate-plan-id')Object.assign(h.currentActiveAccountPlan.accounts[1]!,{accountId:h.currentActiveAccountPlan.accounts[0]!.accountId})
  else if(fault==='duplicate-plan-code')Object.assign(h.currentActiveAccountPlan.accounts[1]!,{code:h.currentActiveAccountPlan.accounts[0]!.code})
  else if(fault==='empty-name')Object.assign(h.currentActiveAccountPlan.accounts[0]!,{name:' '})
  else if(fault==='rows-empty')Object.assign(m,{rows:[]})
  else if(fault==='rows-max')Object.assign(m,{rows:Array.from({length:1001},(_,i)=>({...m.rows[0]!,itemType:`Type${i}`}))})
  else if(fault==='row-extra')Object.assign(m.rows[0]!,{lineNumber:1})
  else if(fault==='row-missing'){const rest={...m.rows[0]!};Reflect.deleteProperty(rest,'vatRateCode');Object.assign(m,{rows:[rest]})}
  else if(fault==='duplicate-row')Object.assign(m,{rows:[m.rows[0],m.rows[0]]})
  else if(fault==='unknown-purpose')Object.assign(m.rows[0]!,{purpose:'unknown'})
  else if(fault==='wrong-key')Object.assign(m.rows[0]!,{mappingKey:'revenue'})
  else if(fault==='selector')Object.assign(m.rows[0]!,{itemType:' A '})
  else if(fault==='selector-long')Object.assign(m.rows[0]!,{linePurpose:'A'.repeat(65)})
  else if(fault==='cogs')Object.assign(m.rows[0]!,{purpose:'cost-of-goods-sold',mappingKey:'trade-goods'})
  else if(fault==='vat')Object.assign(m.rows[0]!,{mappingKey:'inventory'})
  else if(fault==='code')Object.assign(m.rows[0]!,{accountCode:' code '})
  else if(fault==='ref')v.references[0]!.recordId=company
  else if(fault==='caption')v.documentLabel='rounded'
  else if(fault==='rule')v.rules[0]!.ruleCode='wrong'
  else Object.assign(h,{expectedActiveMappingVersion:m.mappingVersion})
  expect(decodeWorkDecisionPreview(raw,company,kind,v.itemId)).toBeNull()
 })
 it.each(['sourceFactsDigest','previewDigest'] as const)('accepts only exact uppercase64 %s',field=>{
  for(const value of ['a'.repeat(64),'A'.repeat(64)+'\n','A'.repeat(63)+'\n','A'.repeat(63)+'\r','A'.repeat(62)+'\r\n']){
   const v=fixture(company);Object.assign(v.postingAccountMapping,{[field]:value});expect(decodeWorkDecisionPreview(v,company,kind,v.itemId)).toBeNull()
  }
 })
 it('preserves source SHA case, exact account codes and native UTC precision without chronology inventions',()=>{
  const v=fixture(company,'9007199254740993','0','Superseded');Object.assign(v.postingAccountMapping.mapping,{accountPlanSourceSha256:'aB'.repeat(32)})
  Object.assign(v.postingAccountMapping.preparedAccountPlan,{sourceSha256:'aB'.repeat(32)})
  expect(decodeWorkDecisionPreview(v,company,kind,v.itemId)).not.toBeNull()
 })
 it('retains native selector maxima, whitespace in nonblank current account names and all 1000 rows',()=>{
  const v=fixture(company);Object.assign(v.postingAccountMapping.mapping,{rows:Array.from({length:1000},(_,i)=>({...v.postingAccountMapping.mapping.rows[0]!,itemType:`A${i}`,linePurpose:'A'.repeat(64)}))})
  Object.assign(v.postingAccountMapping.currentActiveAccountPlan.accounts[0]!,{name:' Current raw name '})
  expect(decodeWorkDecisionPreview(v,company,kind,v.itemId)).not.toBeNull()
 })
 it('accepts only exact native Active receipt and replay headers, never current Superseded or private proof',async()=>{
  const v=fixture(company),body={companyId:company,kind,itemId:v.itemId,state:'Active',replayed:true,originalCorrelationId:'SENTETIK original'}
  const response=(extra:object={})=>new Response(JSON.stringify({...body,...extra}),{status:200,headers:{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK original'}})
  const fetch=vi.fn().mockResolvedValue(response());vi.stubGlobal('fetch',fetch)
  expect((await sendWorkDecision(company,kind,v.itemId,request,'same-key')).type).toBe('applied');expect(JSON.parse(String(fetch.mock.calls[0]![1].body))).toEqual(request)
  for(const delta of [{state:'Approved'},{state:'Superseded'},{companyId:v.itemId},{itemId:company},{replayed:false},{originalCorrelationId:'other'},{postingAccountMapping:{}}]){
   fetch.mockResolvedValue(response(delta));expect((await sendWorkDecision(company,kind,v.itemId,request,'same-key')).type).toBe('unknown')
  }
 })
})
