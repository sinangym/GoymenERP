import {decodeWorkDecisionPreview,isWorkDecisionRequest,sendWorkDecision,WORK_DECISION_KINDS,workDecisionReasonLimit} from './workDecisionApi'
import {afterEach,describe,expect,it,vi} from 'vitest'
import {COMPANY_A,COMPANY_B} from '../settings/testHarness'
import {decodeTaxIdChange} from '../organization/companyTaxIdContract'
import {decodeWorkOrganizationCompanyTaxIdPreview,validOrganizationCompanyTaxIdPreview,isOrganizationCompanyTaxIdDecisionRequest,
 organizationCompanyTaxIdDecisionRequest,validCompanyTaxIdDecisionNote,ORGANIZATION_COMPANY_TAX_ID_WORK_KIND as kind} from './workOrganizationCompanyTaxIdDecision'
import {organizationCompanyTaxIdHuman as human,organizationCompanyTaxIdFixture as fixture,companyTaxIdRequestId as id,companyTaxIdActor as actor} from './organizationCompanyTaxIdDecisionFixture'
const decode=(v:unknown)=>decodeWorkOrganizationCompanyTaxIdPreview(v,COMPANY_A,id)
function put(root:object,path:readonly string[],value:unknown){let p=root as Record<string,unknown>;for(const key of path.slice(0,-1)){const child=p[key];if(child===null||typeof child!=='object')throw new Error('Missing fixture');p=child as Record<string,unknown>}p[path[path.length-1]!]=value}
const max='9223372036854775807'
describe('organization company VKN Work core',()=>{
 it.each([false,true])('preserves all6 and native10 human fields without opaque proof, first=%s',first=>{
  const h=human(COMPANY_A,first),raw=JSON.stringify(h);expect(decode(h)).toEqual(h);expect(JSON.stringify(h)).toBe(raw)
  expect(Object.keys(h)).toHaveLength(6);expect(Object.keys(h.request)).toHaveLength(10)
  expect(validOrganizationCompanyTaxIdPreview(fixture(COMPANY_A,first),COMPANY_A,id)).toBe(true)
  expect(decodeWorkOrganizationCompanyTaxIdPreview(h,COMPANY_A.toUpperCase(),id.toUpperCase())).toEqual(h)
 })
 it.each(['1','9007199254740993',max])('keeps actual company Int64 versions lossless %s',version=>{
  const h=human(COMPANY_A);h.companyVersion=version;h.companyVersionAtRequest=version;expect(decode(h)?.companyVersion).toBe(version)
 })
 it.each([1,9007199254740992,'0','01','+1','1\n','1\r','1\r\n','9223372036854775808',null])('rejects numeric/noncanonical/out-of-range version %s',version=>{
  for(const field of ['companyVersion','companyVersionAtRequest']){const h=human(COMPANY_A);put(h,[field],version);expect(decode(h)).toBeNull()}
 })
 const faults:readonly[readonly string[],unknown][]=[
  [['companyId'],COMPANY_B],[['companyId'],'00000000-0000-0000-0000-000000000000'],[['request','id'],COMPANY_B],
  [['request','reason'],''],[['request','reason'],' untrimmed'],[['request','reason'],'g'.repeat(1001)],[['request','reason'],'line\nbreak'],
  [['request','requestedBy'],''],[['request','requestedBy'],' maker '],[['request','requestedBy'],'m'.repeat(129)],
  [['request','status'],'Pending'],[['request','decidedBy'],actor],[['request','decidedAtUtc'],'2026-11-28T10:00:00Z'],[['request','decisionNote'],'note'],
  [['isCurrent'],null],[['request'],null],[['companyVkn'],'1234567890\n'],[['request','currentVkn'],'1234567890\r'],
 ]
 it.each(faults)('rejects malformed/foreign native field %j=%j',(path,value)=>{const h=human(COMPANY_A);put(h,path,value);expect(decode(h)).toBeNull()})
 it.each(['12345678901','1234567891','9876543217\n','9876543217\r','9876543217\r\n',' 9876543217','9876543217 ','９８７６５４３２１７'])('requires exact native VKN checksum and ASCII shape %s',proposedVkn=>{
  const h=human(COMPANY_A);h.request={...h.request,proposedVkn};expect(decode(h)).toBeNull()
 })
 it('rejects an unchanged request VKN without comparing an approved company current VKN to the old request',()=>{
  const h=human(COMPANY_A);h.request={...h.request,currentVkn:h.request.proposedVkn};h.companyVkn=h.request.proposedVkn
  expect(decode(h)).toBeNull();expect(decodeTaxIdChange(h.request)).toBeNull()
 })
 it('does not invent a new checksum requirement for legacy current VKN',()=>{
  const h=human(COMPANY_A);h.companyVkn='1234567891';h.request={...h.request,currentVkn:h.companyVkn};expect(decode(h)).toEqual(h)
 })
 it.each(['0001-01-01T00:00:00Z','2026-11-28T10:00:00.1234567+00:00','2026-11-28T13:00:00+03:00'])('preserves native dates including default and offsets without chronology rule %s',time=>{
  const h=human(COMPANY_A);h.request={...h.request,requestedAtUtc:time};expect(decode(h)).toEqual(h)
 })
 it.each(['2026-02-30T10:00:00Z','2026-11-28','2026-11-28T10:00:00Z\n','2026-11-28T10:00:00Z\r','2026-11-28T10:00:00Z\r\n','0000-01-01T00:00:00Z','2026-11-28T25:00:00Z'])('rejects nonnative timestamp %s',time=>{
  const h=human(COMPANY_A);h.request={...h.request,requestedAtUtc:time};expect(decode(h)).toBeNull()
 })
 it.each(['falseCurrent','differentVkn','differentVersion'] as const)('rejects a contradictory current flag %s',fault=>{
  const h=human(COMPANY_A)
  if(fault==='falseCurrent')h.isCurrent=false;if(fault==='differentVkn')h.companyVkn='1111111114';if(fault==='differentVersion')h.companyVersion='9007199254740994'
  expect(decode(h)).toBeNull()
 })
 it('retains a rejected request that is still current because native rejection does not update company version',()=>{
  const v=fixture(COMPANY_A);v.pending=false;v.organizationCompanyTaxId.request={...v.organizationCompanyTaxId.request,status:'rejected',decidedBy:actor,decidedAtUtc:'2026-11-28T09:00:00Z',decisionNote:null}
  expect(decode(v.organizationCompanyTaxId)).toEqual(v.organizationCompanyTaxId);expect(validOrganizationCompanyTaxIdPreview(v,COMPANY_A,id)).toBe(true)
 })
 it.each(['approved','rejected'] as const)('retains native lowercase terminal %s and accepts earlier decision clock',status=>{
  const h=human(COMPANY_A);h.companyVersion='9007199254740994';h.companyVkn=status==='approved'?h.request.proposedVkn:h.companyVkn;h.isCurrent=false
  h.request={...h.request,status,decidedBy:actor,decidedAtUtc:'2026-11-28T09:00:00Z',decisionNote:'SENTETIK native decision note'}
  expect(decode(h)).toEqual(h);expect(organizationCompanyTaxIdDecisionRequest(h,false,true,null,null)).toBeNull()
  expect(decodeTaxIdChange(h.request)).toEqual(h.request)
 })
 it.each(['sameMaker','noTime','blankNote','badNote','untrimmedNote'] as const)('rejects contradictory terminal %s',fault=>{
  const h=human(COMPANY_A);h.request={...h.request,status:'rejected',decidedBy:actor,decidedAtUtc:'2026-11-28T09:00:00Z',decisionNote:null}
  if(fault==='sameMaker')h.request={...h.request,decidedBy:h.request.requestedBy};if(fault==='noTime')h.request={...h.request,decidedAtUtc:null}
  if(fault==='blankNote')h.request={...h.request,decisionNote:''};if(fault==='badNote')h.request={...h.request,decisionNote:'a\nb'};if(fault==='untrimmedNote')h.request={...h.request,decisionNote:' note '}
  expect(decode(h)).toBeNull()
 })
 it.each(['companyId','itemId','kind','sourceVersion','pending','approvalReasonRequired','rejectionSupported','approvalSupported','documentLabel'] as const)('rejects snapshot mismatch %s',field=>{
  const v=fixture(COMPANY_A);const bad:Record<typeof field,unknown>={companyId:COMPANY_B,itemId:COMPANY_B,kind:'accounting.source-voucher.v1',sourceVersion:'1',pending:false,approvalReasonRequired:true,rejectionSupported:false,approvalSupported:false,documentLabel:'other'}
  Object.assign(v,{[field]:bad[field]});expect(validOrganizationCompanyTaxIdPreview(v,COMPANY_A,id)).toBe(false)
 })
 it.each(['sourceVoucher','inventoryCostCancellation','approvalReceipt','organizationCompanyTaxIdProof'])('rejects cross-kind/private snapshot member %s',field=>{
  expect(validOrganizationCompanyTaxIdPreview({...fixture(COMPANY_A),[field]:{}},COMPANY_A,id)).toBe(false)
 })
 it.each(['sourceFactsDigest','previewDigest','decisionReceipt','rowVersion'])('rejects unknown private detail field %s',field=>{
  expect(decode({...human(COMPANY_A),[field]:'A'.repeat(64)})).toBeNull();const h=human(COMPANY_A);Object.assign(h.request,{[field]:'opaque'});expect(decode(h)).toBeNull()
 })
 it('matches all three native rules in order for a stale first VKN request and exposes reject only',()=>{
  const v=fixture(COMPANY_A,true);v.sourceVersion='9007199254740994';v.organizationCompanyTaxId.companyVersion=v.sourceVersion;v.organizationCompanyTaxId.isCurrent=false;v.approvalSupported=false
  v.rules.push({ruleCode:'organization.company-tax-id-change-stale',threshold:null,actual:null,deviation:null,unit:null})
  expect(validOrganizationCompanyTaxIdPreview(v,COMPANY_A,id)).toBe(true)
  expect(organizationCompanyTaxIdDecisionRequest(v.organizationCompanyTaxId,true,true,null,null)).toBeNull()
  expect(organizationCompanyTaxIdDecisionRequest(v.organizationCompanyTaxId,true,false,null,null)).toEqual({expectedVersion:v.sourceVersion,approve:false,reason:null,delegationId:null})
  for(const replacement of [v.rules.slice(0,2),[...v.rules].reverse(),[...v.rules,v.rules[0]!],[{...v.rules[0]!,actual:'0'},...v.rules.slice(1)]])expect(validOrganizationCompanyTaxIdPreview({...v,rules:replacement},COMPANY_A,id)).toBe(false)
 })
 it('uses current company version, not a fictitious request row version or preparation version',()=>{
  const h=human(COMPANY_A);h.companyVersion='9007199254740995';h.isCurrent=false
  const r=organizationCompanyTaxIdDecisionRequest(h,true,false,'  SENTETIK note  ',id)
  expect(r).toEqual({expectedVersion:'9007199254740995',approve:false,reason:'SENTETIK note',delegationId:id});expect(Object.keys(r!)).toHaveLength(4)
 })
 it.each([true,false])('keeps optional native note on direction approve=%s',approve=>{
  for(const reason of [null,'','  ','  SENTETIK note  ','n'.repeat(1000)])expect(isOrganizationCompanyTaxIdDecisionRequest({expectedVersion:'1',approve,reason,delegationId:null})).toBe(true)
  for(const reason of ['n'.repeat(1001),'bad\nnote','bad\tnote',123])expect(isOrganizationCompanyTaxIdDecisionRequest({expectedVersion:'1',approve,reason,delegationId:null})).toBe(false)
  expect(organizationCompanyTaxIdDecisionRequest(human(COMPANY_A),true,approve,'   ',null)?.reason).toBeNull()
 })
 it('accepts native trim-before-note-validation without changing a frozen original body',()=>{
  const reason='\n '+'n'.repeat(1000)+' \r\n',r={expectedVersion:'1',approve:false,reason,delegationId:null}
  expect(validCompanyTaxIdDecisionNote(reason)).toBe(true);expect(isOrganizationCompanyTaxIdDecisionRequest(r)).toBe(true);expect(r.reason).toBe(reason)
 })
 it.each(['0','01','1\n','1\r','1\r\n','9223372036854775808',1,null])('rejects invalid frozen command company version %s',expectedVersion=>{
  expect(isOrganizationCompanyTaxIdDecisionRequest({expectedVersion,approve:false,reason:null,delegationId:null})).toBe(false)
 })
 it('allows a max-version rejection but never an overflowing approval',()=>{
  const h=human(COMPANY_A);h.companyVersion=max;h.companyVersionAtRequest=max
  expect(decode(h)).toEqual(h);expect(organizationCompanyTaxIdDecisionRequest(h,true,true,null,null)).toBeNull()
  expect(organizationCompanyTaxIdDecisionRequest(h,true,false,null,null)?.expectedVersion).toBe(max)
  expect(organizationCompanyTaxIdDecisionRequest(h,false,false,null,null)).toBeNull()
 })
 it.each(['inventoryCostCancellation','normalJournalDraft','organizationCompanyTaxId','approvalReceipt'])('rejects a foreign request selector %s',field=>{
  expect(isOrganizationCompanyTaxIdDecisionRequest({expectedVersion:'1',approve:false,reason:null,delegationId:null,[field]:{}})).toBe(false)
 })
 it.each([null,'bad',id+'\n',id+'\r',id+'\r\n','00000000-0000-0000-0000-000000000000'])('validates the optional delegation ID exactly %s',delegationId=>{
  expect(isOrganizationCompanyTaxIdDecisionRequest({expectedVersion:'1',approve:true,reason:null,delegationId})).toBe(delegationId===null)
 })
 it('pins the exact owner kind without fabricating a request-row version',()=>expect(kind).toBe('organization.company-tax-id-change.v1'))
})


afterEach(()=>vi.unstubAllGlobals())
describe('paired company VKN generic Work transport',()=>{
 it('requires the actual12-key wrapper and publishes one purpose without changing native6/native10 facts',()=>{
  const v=fixture(COMPANY_A),raw=JSON.stringify(v)
  expect(decodeWorkDecisionPreview(v,COMPANY_A,kind,id)?.organizationCompanyTaxId).toEqual(v.organizationCompanyTaxId)
  expect(Object.keys(v)).toHaveLength(12);expect(WORK_DECISION_KINDS).toHaveLength(59);expect(WORK_DECISION_KINDS.filter(k=>k===kind)).toEqual([kind])
  expect(workDecisionReasonLimit(kind)).toBe(1000);expect(JSON.stringify(v)).toBe(raw)
 })
 it.each([true,false])('keeps the existing exact four-field optional-note command on approve=%s',approve=>{
  expect(isWorkDecisionRequest({expectedVersion:'9007199254740993',approve,reason:null,delegationId:null},kind)).toBe(true)
  expect(isWorkDecisionRequest({expectedVersion:'9007199254740993',approve,reason:'n'.repeat(1000),delegationId:id},kind)).toBe(true)
  expect(isWorkDecisionRequest({expectedVersion:'9007199254740993',approve,reason:'n'.repeat(1001),delegationId:null},kind)).toBe(false)
 })
 it.each(['normalJournalDraft','inventoryCostCancellation','postingAccountMappingSelection','organizationCompanyTaxId','decisionReceipt'])('rejects a foreign command selector %s',field=>{
  expect(isWorkDecisionRequest({expectedVersion:'1',approve:false,reason:null,delegationId:null,[field]:{}},kind)).toBe(false)
 })
 it.each(['sourceVoucher','inventoryCostCancellation','organizationCompanyTaxIdProof','previewDigest'])('refuses a cross-kind/private GET field %s',field=>{
  expect(decodeWorkDecisionPreview({...fixture(COMPANY_A),[field]:{}},COMPANY_A,kind,id)).toBeNull()
 })
 it('does not couple an original frozen rejection body to the current request or reject maxInt64',()=>{
  const request={expectedVersion:'9223372036854775807',approve:false,reason:null,delegationId:null}
  expect(isWorkDecisionRequest(request,kind)).toBe(true);expect(isWorkDecisionRequest({...request,approve:true},kind)).toBe(false)
  expect(isWorkDecisionRequest({...request,expectedVersion:'9007199254740993',approve:true},kind)).toBe(true)
 })
 it.each([true,false])('requires exact lowercase original state and replay headers, approve=%s',async approve=>{
  const request={expectedVersion:'9007199254740993',approve,reason:'SENTETIK frozen note',delegationId:null},body={companyId:COMPANY_A,kind,itemId:id,state:approve?'approved':'rejected',replayed:true,originalCorrelationId:'SENTETIK original org59'}
  const headers={'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':body.originalCorrelationId}
  const fetcher=vi.fn(async(_url:RequestInfo|URL,_init?:RequestInit)=>new Response(JSON.stringify(body),{status:200,headers}));vi.stubGlobal('fetch',fetcher)
  expect(await sendWorkDecision(COMPANY_A,kind,id,request,'SENTETIK retained org59')).toMatchObject({type:'applied',replayed:true})
  const sent=fetcher.mock.calls[0]![1];expect(JSON.parse(String(sent?.body))).toEqual(request);expect(new Headers(sent?.headers).get('Idempotency-Key')).toBe('SENTETIK retained org59')
  for(const state of ['Approved','Rejected',approve?'rejected':'approved']){fetcher.mockImplementation(async()=>new Response(JSON.stringify({...body,state}),{status:200,headers}));expect((await sendWorkDecision(COMPANY_A,kind,id,request,'SENTETIK retained org59')).type).toBe('unknown')}
  fetcher.mockImplementation(async()=>new Response(JSON.stringify(body),{status:200}));expect((await sendWorkDecision(COMPANY_A,kind,id,request,'SENTETIK retained org59')).type).toBe('unknown')
  fetcher.mockImplementation(async()=>new Response(JSON.stringify({...body,organizationCompanyTaxId:{}}),{status:200,headers}));expect((await sendWorkDecision(COMPANY_A,kind,id,request,'SENTETIK retained org59')).type).toBe('unknown')
 })
})
