import {afterEach,describe,expect,it,vi} from 'vitest'
import {decodeSourceVoucher} from '../accounting/sourceVoucherApi'
import {isWorkPackageEffectiveDate as date,decodeWorkDecisionPreview,isWorkDecisionRequest,sendWorkDecision,WORK_DECISION_KINDS} from './workDecisionApi'
import {COMPANY_A,COMPANY_B} from '../settings/testHarness'
import {decodeWorkSourceVoucherPreview,validSourceVoucherPreview,isSourceVoucherDecisionRequest} from './workSourceVoucherDecision'
import {sourceVoucherHuman,sourceVoucherFixture,sourceVoucherReversalHuman,sourceVoucherId as id,sourceVoucherActor as actor} from './sourceVoucherDecisionFixture'
// This fixture callback certifies only its explicit literals. Exact CLR representability remains the shared adapter's callback, not a new money engine.
const amount=(v:unknown)=>typeof v==='string'&&['120','121','1.23','-120','0','1.0000','79228162514264337593543950335','0.0000000000000000000000000001'].includes(v)
const decode=(v:unknown)=>decodeWorkSourceVoucherPreview(v,COMPANY_A,id,date,amount)
function put(root:unknown,path:readonly (string|number)[],value:unknown){let parent=root
 const object=(v:unknown):v is Record<string,unknown>=>v!==null&&typeof v==='object'
 for(const key of path.slice(0,-1)){if(!object(parent))throw new Error('Fixture path');parent=parent[key]}
 if(!object(parent))throw new Error('Fixture target');parent[path[path.length-1]!]=value}


describe('staged source voucher Work decoder',()=>{
 it.each([true,false])('keeps all 25/9 human fields and exact three text long values with periodOpen=%s',period=>{
  const h=sourceVoucherHuman(COMPANY_A,period);expect(decode(h)).toEqual(h);expect(decode(h)?.voucher.lines).toHaveLength(137)
  expect(validSourceVoucherPreview(sourceVoucherFixture(COMPANY_A,period),COMPANY_A,id,date,amount)).toBe(true)
  expect(decode(h)?.voucher.finalizedVersion).toBe('9007199254740993');expect(decode(h)?.voucher.mappingVersion).toBe('9223372036854775807')
  expect(decode(h)?.voucher.exchangeRate).toBe('1.0000')
 })
 it.each(['finalizedVersion','mappingVersion'] as const)('does not round max Int64 %s',field=>{
  const h=sourceVoucherHuman(COMPANY_A);h.voucher={...h.voucher,[field]:'9223372036854775807'};expect(decode(h)).toEqual(h)
 })
 it.each([0,1,9007199254740992,'0','01','+1','1\n','1\r','1\r\n','9223372036854775808',null])('rejects noncanonical or numeric long %s',value=>{
  for(const field of ['version','mappingVersion','finalizedVersion']){const h=sourceVoucherHuman(COMPANY_A);put(h,['voucher',field],value);expect(decode(h)).toBeNull()}
 })
 it.each(['120\n','120\r','120\r\n',' 120','120 ','1e2','01','120.0','-1.2300','-0','1,20',null,120])('rejects noncanonical owner amount %s',value=>{
  for(const path of [['voucher','functionalPayable'],['voucher','lines',0,'functionalAmount']] as const){const h=sourceVoucherHuman(COMPANY_A);put(h,path,value);expect(decode(h)).toBeNull()}
 })
 it.each(['-120','-1.23','0','79228162514264337593543950335','0.0000000000000000000000000001'])('preserves owner decimal literal without summing %s',value=>{
  const h=sourceVoucherHuman(COMPANY_A);put(h,['voucher','functionalPayable'],value);put(h,['voucher','lines',0,'functionalAmount'],value);expect(decode(h)).toEqual(h)
 })
 it('passes signed amount magnitude to the existing native validator while preserving the negative wire string',()=>{
  const h=sourceVoucherHuman(COMPANY_A);h.voucher={...h.voucher,functionalPayable:'-1.23',lines:h.voucher.lines.map(line=>({...line,functionalAmount:'-1.23'}))}
  const seen:unknown[]=[];expect(decodeWorkSourceVoucherPreview(h,COMPANY_A,id,date,v=>{seen.push(v);return v==='1.23'||v==='1.0000'})).toEqual(h)
  expect(seen).not.toContain('-1.23');expect(decode(h)?.voucher.functionalPayable).toBe('-1.23')
 })
 it('binds canonical negative payable to the exact owner rule, preserving raw text',()=>{
  const v=sourceVoucherFixture(COMPANY_A);v.sourceVoucher.voucher={...v.sourceVoucher.voucher,functionalPayable:'-120'};v.rules[0]!.actual='-120'
  expect(validSourceVoucherPreview(v,COMPANY_A,id,date,amount)).toBe(true);v.rules[0]!.actual='120';expect(validSourceVoucherPreview(v,COMPANY_A,id,date,amount)).toBe(false)
 })
 it('requires the native amount callback rather than silently certifying shape as representability',()=>{
  const h=sourceVoucherHuman(COMPANY_A);expect(decodeWorkSourceVoucherPreview(h,COMPANY_A,id,date,()=>false)).toBeNull()
 })
 it.each(['id','companyId','sourceDocumentId','invoiceId','sourceActorId','partyId','documentCurrencyId'])('rejects foreign/malformed identity %s',field=>{
  const h=sourceVoucherHuman(COMPANY_A);put(h,['voucher',field],field==='companyId'?COMPANY_B:id+'\n');expect(decode(h)).toBeNull()
 })
 it.each(['createdAtUtc','approvedAtUtc'])('rejects UTC/calendar/suffix faults %s',field=>{
  for(const bad of ['2026-02-30T09:00:00Z','2026-11-28T24:00:00Z','2026-11-28T09:00:00+03:00','2026-11-28T09:00:00Z\n']){
   const h=sourceVoucherHuman(COMPANY_A);put(h,field==='createdAtUtc'?[field]:['voucher',field],bad);expect(decode(h)).toBeNull()}
 })
 it.each(['0001-01-01','0001-01-02','9999-12-31'])('keeps actual DateOnly calendar without new chronology %s',day=>{
  const h=sourceVoucherHuman(COMPANY_A);h.voucher={...h.voucher,documentDate:day,dueDate:day};expect(decode(h)).toEqual(h)
 })
 it.each(['2026-02-30','2026-11-28Z','2026-11-28\n','0000-01-01'])('rejects non-DateOnly %s',day=>{
  const h=sourceVoucherHuman(COMPANY_A);h.voucher={...h.voucher,documentDate:day};expect(decode(h)).toBeNull()
 })
 it('preserves native optional omitted/null fields and nullable blank captions without historical account-name inference',()=>{
  const h=sourceVoucherHuman(COMPANY_A);h.voucher={...h.voucher,officialInvoiceNo:' ',cancellationId:null,reversalProposal:null,
   lines:h.voucher.lines.map(line=>({...line,accountCode:'',accountName:null,vatRateCode:''}))};expect(decode(h)).toEqual(h)
  const raw=structuredClone(h);for(const line of raw.voucher.lines){Reflect.deleteProperty(line,'accountCode');Reflect.deleteProperty(line,'accountName')}
  Reflect.deleteProperty(raw.voucher,'cancellationId');Reflect.deleteProperty(raw.voucher,'reversalProposal');expect(decode(raw)).toEqual(raw)
 })
 it.each(['duplicate','zero','overInt32','null','unknown','blankMapping','empty'])('refuses incomplete or non-native line %s',fault=>{
  const h=sourceVoucherHuman(COMPANY_A);if(fault==='empty')h.voucher={...h.voucher,lines:[]};else if(fault==='duplicate')h.voucher={...h.voucher,lines:[h.voucher.lines[0]!,h.voucher.lines[0]!]};
  else {const tail:readonly string[]=fault==='unknown'?['extra']:fault==='blankMapping'?['mappingKey']:fault==='null'?[]:['lineNumber'];put(h,['voucher','lines',0,...tail],fault==='zero'?0:fault==='overInt32'?2147483648:fault==='null'?null:fault==='blankMapping'?' ':true)}
  expect(decode(h)).toBeNull()
 })
 it.each(['lineNumber','sourceLineNumber'] as const)('keeps the full native Int32 bound for %s',field=>{
  const h=sourceVoucherHuman(COMPANY_A);put(h,['voucher','lines',0,field],2147483647);expect(decode(h)).toEqual(h)
  put(h,['voucher','lines',0,field],2147483648);expect(decode(h)).toBeNull()
 })
 it('displays completed maximum Int64 version losslessly without changing the original command version one',()=>{
  const h=sourceVoucherHuman(COMPANY_A);h.voucher={...h.voucher,status:'Posted',version:'9223372036854775807',periodId:id,journalId:id,approvedBy:actor,approvedAtUtc:'2026-11-28T08:00:00Z'}
  expect(decode(h)?.voucher.version).toBe('9223372036854775807');expect(isSourceVoucherDecisionRequest({expectedVersion:h.voucher.version,approve:true,reason:null,delegationId:null})).toBe(false)
 })
 it.each(['approvalReceipt','sourceFactsDigest','previewDigest','SourceJson','unexpected'])('rejects private and unknown fields at every public level %s',key=>{
  for(const path of [[],['voucher'],['voucher','lines',0]] as const){const h=sourceVoucherHuman(COMPANY_A);put(h,[...path,key],'SENTETIK private');expect(decode(h)).toBeNull()}
 })
 it.each(['pending','status','period','cancelled','sourceVersion','reason','reject','caption','refs','rule','ruleUnit','ruleAmount','outerCompany','outerId','kind'])('binds complete human facts to Work snapshot %s',fault=>{
  const v=sourceVoucherFixture(COMPANY_A)
  const changes:Record<string,readonly [readonly (string|number)[],unknown]>={pending:[['pending'],false],status:[['sourceVoucher','voucher','status'],'invented'],period:[['sourceVoucher','periodOpen'],false],
   cancelled:[['sourceVoucher','sourceCancelled'],true],sourceVersion:[['sourceVersion'],'2'],reason:[['approvalReasonRequired'],true],reject:[['rejectionSupported'],true],caption:[['documentLabel'],'other'],
   refs:[['references',0,'recordId'],id],rule:[['rules',0,'ruleCode'],'other'],ruleUnit:[['rules',0,'unit'],'TRY'],ruleAmount:[['rules',0,'actual'],'121'],outerCompany:[['companyId'],COMPANY_B],outerId:[['itemId'],actor],kind:[['kind'],'accounting.normal-journal-draft.v1']}
  const change=changes[fault]!;put(v,change[0],change[1]);expect(validSourceVoucherPreview(v,COMPANY_A,id,date,amount)).toBe(false)
 })
 it('accepts current Posted2 and cancelled original-payment presentation without deriving a new command version',()=>{
  const h=sourceVoucherHuman(COMPANY_A);h.voucher={...h.voucher,status:'Posted',version:'2',journalId:id,periodId:id,approvedBy:actor,approvedAtUtc:'2026-11-28T08:00:00Z'}
  expect(decode(h)).toEqual(h);h.sourceCancelled=true;h.periodOpen=false;h.voucher={...h.voucher,status:'CancellationReversalPending',cancellationId:id,cancellationReason:'SENTETIK full cancellation reason'}
  expect(decode(h)).toEqual(h)
 })
 it.each([true,false])('retains full public16 reversal and numeric Int32 version with approved=%s and no invented chronology',approved=>{
  const h=sourceVoucherReversalHuman(COMPANY_A,approved);expect(decode(h)).toEqual(h)
  expect(decode(h)?.voucher.reversalProposal?.version).toBe(2147483647);expect(h.voucher.cancellationReason!.length).toBeGreaterThan(2048)
 })
 it.each(['source','company','versionText','versionOverflow','guidSuffix','instantSuffix','extra','missingCancellation','status'])('refuses unbound native reversal %s',fault=>{
  const h=sourceVoucherReversalHuman(COMPANY_A)
  const changes:Record<string,readonly [readonly (string|number)[],unknown]>={source:[['voucher','reversalProposal','sourceJournalId'],actor],company:[['voucher','reversalProposal','companyId'],COMPANY_B],
   versionText:[['voucher','reversalProposal','version'],'2'],versionOverflow:[['voucher','reversalProposal','version'],2147483648],guidSuffix:[['voucher','reversalProposal','proposedBy'],actor+'\n'],
   instantSuffix:[['voucher','reversalProposal','decidedAtUtc'],'2026-11-28T10:00:00Z\n'],extra:[['voucher','reversalProposal','approvalReceipt'],{}],missingCancellation:[['voucher','cancellationId'],null],status:[['voucher','reversalProposal','status'],'Pending']}
  const change=changes[fault]!;put(h,change[0],change[1]);expect(decode(h)).toBeNull()
 })
 it('keeps native numeric decoder defaults and does not coerce Work text into a fabricated native number',()=>{
  const h=sourceVoucherHuman(COMPANY_A);expect(decodeSourceVoucher(h.voucher,COMPANY_A)).toBeNull()
  const native={...h.voucher,finalizedVersion:4,version:1,mappingVersion:1,documentDate:'2026-11-28',dueDate:null};expect(decodeSourceVoucher(native,COMPANY_A)).toEqual(native)
  expect(decodeSourceVoucher({...native,version:9007199254740992},COMPANY_A)).toBeNull()
 })
 it.each(['2',2,'01',null])('rejects a substituted original request version %s',version=>expect(isSourceVoucherDecisionRequest({expectedVersion:version,approve:true,reason:null,delegationId:null})).toBe(false))
 it('accepts only the exact frozen four-field approval body',()=>{
  const r={expectedVersion:'1',approve:true,reason:null,delegationId:null};expect(isSourceVoucherDecisionRequest(r)).toBe(true)
  for(const extra of ['inventoryCostCancellation','normalJournalDraft','sourceVoucher','approvalReceipt'])expect(isSourceVoucherDecisionRequest({...r,[extra]:{}})).toBe(false)
  expect(isSourceVoucherDecisionRequest({...r,approve:false})).toBe(false);expect(isSourceVoucherDecisionRequest({...r,reason:''})).toBe(false)
 })
})


afterEach(()=>vi.unstubAllGlobals())
describe('paired source voucher shared Work transport',()=>{
 const kind='accounting.source-voucher.v1',request={expectedVersion:'1',approve:true,reason:null,delegationId:null} as const
 it('publishes the actual closed human wrapper and exactly one new kind with all other native facts unchanged',()=>{
  const value=sourceVoucherFixture(COMPANY_A),before=JSON.stringify(value)
  expect(decodeWorkDecisionPreview(value,COMPANY_A.toUpperCase(),kind,id.toUpperCase())?.sourceVoucher).toEqual(value.sourceVoucher)
  expect(JSON.stringify(value)).toBe(before);expect(WORK_DECISION_KINDS).toHaveLength(53);expect(WORK_DECISION_KINDS.filter(k=>k===kind)).toEqual([kind])
 })
 it.each(['79228162514264337593543950335','-79228162514264337593543950335','0.0000000000000000000000000001','-0.0000000000000000000000000001'])('uses the existing native CLR callback losslessly at %s',raw=>{
  const value=sourceVoucherFixture(COMPANY_A);value.sourceVoucher.voucher={...value.sourceVoucher.voucher,functionalPayable:raw,lines:value.sourceVoucher.voucher.lines.map(line=>({...line,functionalAmount:raw}))};value.rules[0]!.actual=raw
  expect(decodeWorkDecisionPreview(value,COMPANY_A,kind,id)?.sourceVoucher?.voucher.functionalPayable).toBe(raw)
 })
 it.each(['79228162514264337593543950336','-79228162514264337593543950336','0.00000000000000000000000000001','-0.00000000000000000000000000001','-1.2300'])('refuses nonrepresentable or noncanonical native GET amount %s',raw=>{
  const value=sourceVoucherFixture(COMPANY_A);value.sourceVoucher.voucher={...value.sourceVoucher.voucher,functionalPayable:raw};value.rules[0]!.actual=raw
  expect(decodeWorkDecisionPreview(value,COMPANY_A,kind,id)).toBeNull()
 })
 it.each(['inventoryCostCancellation','inventoryCost','approvalReceipt','sourceFactsDigest'])('rejects a cross-kind or private top-level preview field %s',field=>{
  expect(decodeWorkDecisionPreview({...sourceVoucherFixture(COMPANY_A),[field]:{}},COMPANY_A,kind,id)).toBeNull()
 })
 it.each([{approve:false},{reason:''},{expectedVersion:'2'},{inventoryCostCancellation:{expectedProposalVersion:'1'}},{normalJournalDraft:{periodId:id}}])('rejects an altered source voucher command before fetch %j',delta=>{
  expect(isWorkDecisionRequest({...request,...delta},kind)).toBe(false)
 })
 it('requires the exact Posted original result and actual replay metadata for the four-field native approval',async()=>{
  const body={companyId:COMPANY_A,kind,itemId:id,state:'Posted',replayed:true,originalCorrelationId:'SENTETIK original source voucher'}
  const fetcher=vi.fn(async(_url:RequestInfo|URL,_init?:RequestInit)=>new Response(JSON.stringify(body),{status:200,headers:{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':body.originalCorrelationId}}));vi.stubGlobal('fetch',fetcher)
  expect(await sendWorkDecision(COMPANY_A,kind,id,request,'SENTETIK retained key')).toMatchObject({type:'applied',replayed:true})
  const sent=fetcher.mock.calls[0]![1];expect(JSON.parse(String(sent?.body))).toEqual(request);expect(new Headers(sent?.headers).get('Idempotency-Key')).toBe('SENTETIK retained key')
  for(const state of ['Approved','Cancelled','CancellationReversalPending','CancelledByReversal']){fetcher.mockImplementation(async()=>new Response(JSON.stringify({...body,state}),{status:200,headers:{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':body.originalCorrelationId}}));expect((await sendWorkDecision(COMPANY_A,kind,id,request,'SENTETIK retained key')).type).toBe('unknown')}
  fetcher.mockImplementation(async()=>new Response(JSON.stringify(body),{status:200}));expect((await sendWorkDecision(COMPANY_A,kind,id,request,'SENTETIK retained key')).type).toBe('unknown')
  fetcher.mockImplementation(async()=>new Response(JSON.stringify({...body,sourceVoucher:{}}),{status:200,headers:{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':body.originalCorrelationId}}));expect((await sendWorkDecision(COMPANY_A,kind,id,request,'SENTETIK retained key')).type).toBe('unknown')
 })
})
