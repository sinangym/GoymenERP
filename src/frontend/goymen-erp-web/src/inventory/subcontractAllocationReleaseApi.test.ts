import {afterEach,describe,expect,it,vi} from 'vitest'
import {decodeSubcontractRelease,decodeSubcontractReleaseCandidates,sendSubcontractRelease} from './subcontractAllocationReleaseApi'
import {RELEASE_ALLOCATION as id,RELEASE_COMPANY as company,releaseCandidateFixture,releaseResultFixture} from './subcontractAllocationReleaseTestData'
afterEach(()=>vi.unstubAllGlobals())
describe('explicit unused subcontract release owner contract',()=>{
 it('retains the authoritative twenty released and eighty consumed without deriving a new quantity',()=>{
  const result=decodeSubcontractRelease(releaseResultFixture(),company);expect(result?.releasedKg).toBe('20');expect(result?.previouslyConsumedKg).toBe('80')
  expect(decodeSubcontractRelease({...releaseResultFixture(),previouslyConsumedKg:'0'},company)).not.toBeNull()
 })
 it.each(['company','kind','status','duplicate','unknown'])('fails closed for malformed or foreign candidate %s',kind=>{
  const value=releaseCandidateFixture()
  if(kind==='company')value.items[0].allocation.companyId=id
  if(kind==='kind')value.items[0].allocation.demandKind='sales.order-line'
  if(kind==='status')value.items[0].allocation.status='Consumed'
  if(kind==='duplicate')value.items.push({...value.items[0]})
  expect(decodeSubcontractReleaseCandidates(kind==='unknown'?{...value,price:'1'}:value,company)).toBeNull()
 })
 it('posts only expected version and explicit reason with the immutable key',async()=>{
  const fetcher=vi.fn(async()=>new Response(JSON.stringify(releaseResultFixture()),{status:200,headers:{'Content-Type':'application/json'}}));vi.stubGlobal('fetch',fetcher)
  expect((await sendSubcontractRelease({allocationId:id,expectedAllocationVersion:'2',reason:'SENTETIK explicit'},{companyId:company,idempotencyKey:'SENTETIK-key'})).type).toBe('succeeded')
  const [url,init]=fetcher.mock.calls[0] as unknown as [string,RequestInit];expect(url).toContain(`/subcontract-allocations/${id}/release-unused`)
  expect(JSON.parse(init.body as string)).toEqual({expectedAllocationVersion:'2',reason:'SENTETIK explicit'});expect(new Headers(init.headers).get('Idempotency-Key')).toBe('SENTETIK-key')
 })
})
