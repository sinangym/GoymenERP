import {afterEach,describe,expect,it,vi} from 'vitest'
import {decodeOfficialCancellation,decodeCancellationCandidates,sendOfficialCancellation} from './officialInvoiceCancellationApi'
import {officialCancellationFixture,OFFICIAL_TEST_COMPANY as company,OFFICIAL_TEST_INVOICE as invoice,cancellationCandidatesFixture} from './officialInvoiceCancellationTestData'
afterEach(()=>vi.unstubAllGlobals())
describe('SAT-SC34 official cancellation source binding',()=>{
 it('accepts exact trusted evidence receipt and never treats missing or cross-company proof as success',()=>{
  const body=officialCancellationFixture();expect(decodeOfficialCancellation(body,company,invoice)).not.toBeNull()
  for(const bad of [{...body,evidence:null},{...body,companyId:invoice},{...body,admissionId:''},{...body,kind:'ManualDeclaration'},{...body,extra:true},{...body,evidence:{...body.evidence,invoiceId:company}},{...body,evidence:{...body.evidence,sourcePayloadSha256:'not-proof'}}])expect(decodeOfficialCancellation(bad,company,invoice)).toBeNull()
 })
 it('pages only exact price-free actual selection metadata',()=>{
  const page=cancellationCandidatesFixture();expect(decodeCancellationCandidates(page,'Normal')).not.toBeNull()
  expect(decodeCancellationCandidates({...page,items:[{...page.items[0],unitPrice:'1'}]},'Normal')).toBeNull()
  expect(decodeCancellationCandidates(page,'Subcontract')).toBeNull();expect(decodeCancellationCandidates({...page,nextCursor:invoice},'Normal')).toBeNull()
 })
 it('posts only reason and expected version; external evidence never comes from the client',async()=>{
  const seen:RequestInit[]=[];vi.stubGlobal('fetch',vi.fn(async(_url:string,init:RequestInit)=>{seen.push(init);return new Response(JSON.stringify(officialCancellationFixture()),{status:200,headers:{'Content-Type':'application/json'}})}))
  expect((await sendOfficialCancellation({action:'record',kind:'Normal',invoiceId:invoice,expectedVersion:'4',reason:'SENTETIK'},{companyId:company,idempotencyKey:'SENTETIK-key'})).type).toBe('succeeded')
  expect(JSON.parse(seen[0].body as string)).toEqual({expectedVersion:'4',reason:'SENTETIK'});expect(new Headers(seen[0].headers).get('X-Company-Id')).toBe(company)
 })
 it('retains exact immutable cancellation identity and explicit allocation confirmation on finance retry',async()=>{
  const seen:RequestInit[]=[];const body={...officialCancellationFixture(),deliveryVersion:1,financeStatus:'AllocationConfirmationRequired'}
  vi.stubGlobal('fetch',vi.fn(async(_url:string,init:RequestInit)=>{seen.push(init);return new Response(JSON.stringify(body),{status:200,headers:{'Content-Type':'application/json'}})}))
  expect((await sendOfficialCancellation({action:'finance-retry',cancellationId:body.cancellationId,expectedDeliveryVersion:0,releaseActiveAllocations:false},{companyId:company,idempotencyKey:'SENTETIK-retry'})).type).toBe('succeeded')
  expect(JSON.parse(seen[0].body as string)).toEqual({expectedDeliveryVersion:0,releaseActiveAllocations:false})
 })
})
