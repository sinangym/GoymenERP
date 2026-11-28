import { afterEach, describe, expect, it, vi } from 'vitest'
import { approvedSourceCatalogFixture as fixture } from './approvedSourceCatalogDecisionFixture'
import { APPROVED_SOURCE_CATALOG_KIND as kind } from './workApprovedSourceCatalogDecision'
import { decodeWorkDecisionPreview, isWorkDecisionKind, isWorkDecisionRequest, sendWorkDecision, WORK_DECISION_KINDS } from './workDecisionApi'
const company = 'aaaaaaaa-0000-4000-8000-000000000001',id = 'cccccccc-0000-4000-8000-000000000051'
const request = {expectedVersion:'1',approve:true,reason:null,delegationId:null,approvedSourceCatalogSelection:{expectedActiveCatalogVersion:'0'}}
afterEach(()=>vi.unstubAllGlobals())
describe('approved source catalog Work transport',()=>{
  it('preserves the full six/fifteen header and whole flat package with exactly one paired mounted kind',()=>{
    const value = fixture(company,id)
    expect(decodeWorkDecisionPreview(value,company,kind,id)?.approvedSourceCatalog).toEqual(value.approvedSourceCatalog)
    expect(Object.keys(value.approvedSourceCatalog)).toHaveLength(6);expect(Object.keys(value.approvedSourceCatalog.catalog)).toHaveLength(15)
    expect(value.approvedSourceCatalog.package.accounts).toHaveLength(13);expect(value.approvedSourceCatalog.package.bindings[0]!.mappings).toHaveLength(13)
    expect(isWorkDecisionKind(kind)).toBe(true);expect(WORK_DECISION_KINDS).toHaveLength(56)
    expect(WORK_DECISION_KINDS.filter(k=>String(k)===kind)).toEqual([kind])
    expect(decodeWorkDecisionPreview(value,company.toUpperCase(),kind,id.toUpperCase())).not.toBeNull()
  })
  it.each(['9007199254740993','9223372036854775806'])('freezes both incrementable Int64 strings exactly: %s',version=>{
    const value = fixture(company,id,version,version)
    expect(decodeWorkDecisionPreview(value,company,kind,id)?.approvedSourceCatalog?.expectedActiveCatalogVersion).toBe(version)
    expect(isWorkDecisionRequest({...request,expectedVersion:version,approvedSourceCatalogSelection:{expectedActiveCatalogVersion:version}},kind)).toBe(true)
  })
  it.each(['Active','Superseded'] as const)('renders completed MaxValue facts without treating them as a new incrementable command: %s',status=>{
    const value = fixture(company,id,'9223372036854775807','9223372036854775807',status)
    Object.assign(value.approvedSourceCatalog.catalog,{catalogVersion:'9223372036854775807',activatedAtUtc:'2026-11-01T00:00:00Z'})
    expect(decodeWorkDecisionPreview(value,company,kind,id)).not.toBeNull()
    expect(isWorkDecisionRequest({...request,expectedVersion:value.sourceVersion},kind)).toBe(false)
  })
  it.each(['missing','extra','foreign-company','foreign-package','status-array','numeric-version','numeric-active','max-pending','unsafe','LF-version',
    'partial-activation','default-utc','calendar','time','lower-digest','private-receipt','cross-proof','ref','rule','caption','account-count','empty-accounts',
    'duplicate-account','duplicate-code','empty-name','binding-count','unknown-kind','duplicate-binding','missing-map','duplicate-map','foreign-map','unknown-side','trim-profile'])('refuses malformed or unbound full preview: %s',fault=>{
    const value=fixture(company,id),h=value.approvedSourceCatalog,c=h.catalog;let raw:unknown=value
    if(fault==='missing')raw={...value,approvedSourceCatalog:undefined}
    else if(fault==='extra')raw={...value,approvedSourceCatalog:{...h,actorId:company}}
    else if(fault==='foreign-company')Object.assign(c,{companyId:id})
    else if(fault==='foreign-package')Object.assign(c,{packageId:company})
    else if(fault==='status-array')Object.assign(c,{status:['Prepared']})
    else if(fault==='numeric-version')Object.assign(c,{version:1})
    else if(fault==='numeric-active')Object.assign(h,{expectedActiveCatalogVersion:0})
    else if(fault==='max-pending'){value.sourceVersion='9223372036854775807';Object.assign(c,{version:value.sourceVersion});Object.assign(h,{expectedPackageVersion:value.sourceVersion})}
    else if(fault==='unsafe')Object.assign(h,{expectedActiveCatalogVersion:'9223372036854775808'})
    else if(fault==='LF-version')Object.assign(h,{expectedActiveCatalogVersion:'0\n'})
    else if(fault==='partial-activation')Object.assign(c,{activatedByUserId:company})
    else if(fault==='default-utc')Object.assign(c,{preparedAtUtc:'0001-01-01T00:00:00.0000000Z'})
    else if(fault==='calendar')Object.assign(c,{preparedAtUtc:'2026-02-30T08:00:00Z'})
    else if(fault==='time')Object.assign(c,{preparedAtUtc:'2026-01-01T24:00:00Z'})
    else if(fault==='lower-digest')Object.assign(h,{sourceFactsDigest:'a'.repeat(64)})
    else if(fault==='private-receipt')Object.assign(c,{activationReceipt:{}})
    else if(fault==='cross-proof')raw={...value,accountingJournalReversal:{}}
    else if(fault==='ref')value.references[0]!.recordId=company
    else if(fault==='rule')value.rules[0]!.ruleCode='wrong'
    else if(fault==='caption')value.documentLabel='wrong'
    else if(fault==='account-count')Object.assign(c,{accountCount:12})
    else if(fault==='empty-accounts')Object.assign(h.package,{accounts:[]})
    else if(fault==='duplicate-account')Object.assign(h.package.accounts[1]!,{accountId:h.package.accounts[0]!.accountId})
    else if(fault==='duplicate-code')Object.assign(h.package.accounts[1]!,{code:h.package.accounts[0]!.code})
    else if(fault==='empty-name')Object.assign(h.package.accounts[0]!,{name:' '})
    else if(fault==='binding-count')Object.assign(c,{bindingCount:2})
    else if(fault==='unknown-kind')Object.assign(h.package.bindings[0]!,{sourceKind:'finance.cash-exchange.recorded.v1'})
    else if(fault==='duplicate-binding'){Object.assign(h.package,{bindings:[h.package.bindings[0],h.package.bindings[0]]});Object.assign(c,{bindingCount:2})}
    else if(fault==='missing-map')Object.assign(h.package.bindings[0]!,{mappings:[]})
    else if(fault==='duplicate-map')Object.assign(h.package.bindings[0]!,{mappings:[h.package.bindings[0]!.mappings[0],h.package.bindings[0]!.mappings[0]]})
    else if(fault==='foreign-map')Object.assign(h.package.bindings[0]!.mappings[0]!,{accountId:company})
    else if(fault==='unknown-side')Object.assign(h.package.bindings[0]!.mappings[0]!,{side:'Other'})
    else Object.assign(h.package.bindings[0]!,{ownerProfileVersion:' 1 '})
    expect(decodeWorkDecisionPreview(raw,company,kind,id)).toBeNull()
  })
  it.each(['0','01','+1','1\n','9223372036854775807','9223372036854775808'])('rejects nonincrementable package version %s',version=>{
    expect(isWorkDecisionRequest({...request,expectedVersion:version},kind)).toBe(false)
  })
  it.each(['-1','00','+0','0\n','9223372036854775807','9223372036854775808'])('rejects invalid active version %s',version=>{
    expect(isWorkDecisionRequest({...request,approvedSourceCatalogSelection:{expectedActiveCatalogVersion:version}},kind)).toBe(false)
  })
  it('keeps the five command fields exclusive to the catalog and accepts no proof or reason payload',()=>{
    expect(isWorkDecisionRequest(request,kind)).toBe(true)
    expect(isWorkDecisionRequest({...request,delegationId:id},kind)).toBe(true)
    for(const extra of [{approve:false},{reason:''},{approvedSourceCatalogSelection:null},{approvedSourceCatalogSelection:{}},
      {delegationId:'00000000-0000-0000-0000-000000000000'},{approvedSourceCatalogSelection:{expectedActiveCatalogVersion:'0',previewDigest:'A'.repeat(64)}},{approvedSourceCatalog:{}},{normalJournalDraft:{periodId:id}}])
      expect(isWorkDecisionRequest({...request,...extra},kind)).toBe(false)
    for(const k of WORK_DECISION_KINDS.filter(k=>k!==kind))expect(isWorkDecisionRequest(request,k)).toBe(false)
  })
  it('accepts only the native original Active six-field receipt with matching replay headers',async()=>{
    const body={companyId:company,kind,itemId:id,state:'Active',replayed:true,originalCorrelationId:'SENTETIK original'}
    const response=(extra:object={})=>new Response(JSON.stringify({...body,...extra}),{status:200,headers:{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK original'}})
    const fetch=vi.fn().mockResolvedValue(response());vi.stubGlobal('fetch',fetch)
    expect((await sendWorkDecision(company,kind,id,request,'same-key')).type).toBe('applied')
    expect(JSON.parse(String(fetch.mock.calls[0]![1].body))).toEqual(request)
    for(const delta of [{state:'Approved'},{state:'Superseded'},{companyId:id},{itemId:company},{replayed:false},{originalCorrelationId:'other'},{approvedSourceCatalog:{}}]){
      fetch.mockResolvedValue(response(delta));expect((await sendWorkDecision(company,kind,id,request,'same-key')).type).toBe('unknown')
    }
    fetch.mockResolvedValue(new Response(JSON.stringify(body),{status:200}));expect((await sendWorkDecision(company,kind,id,request,'same-key')).type).toBe('unknown')
  })
})
