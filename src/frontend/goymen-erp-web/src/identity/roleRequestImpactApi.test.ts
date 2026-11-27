import { afterEach,expect,it,vi } from 'vitest'
import { fetchRoleRequestImpact } from './api'
const company='10000000-0000-4000-8000-000000000001',user='20000000-0000-4000-8000-000000000001',role='30000000-0000-4000-8000-000000000001'
const impact={companyId:company,userId:user,roleId:role,currentPermissionCodes:[],requestedRolePermissionCodes:[],addedPermissionCodes:[],alreadyHeldCodes:[],roleHasCriticalPermissions:false,hasSegregationOfDutiesConflict:null,expandsScope:null}
afterEach(()=>vi.unstubAllGlobals())
it('uses only the typed two-id POST read body and current company header, never a command key or query override',async()=>{
 const fetch=vi.fn<(input:RequestInfo|URL,init?:RequestInit)=>Promise<Response>>(async()=>new Response(JSON.stringify(impact),{status:200}));vi.stubGlobal('fetch',fetch)
 expect(await fetchRoleRequestImpact(company,user,role)).toEqual({type:'ok-single',data:impact})
 const [url,init]=fetch.mock.calls[0]!
 expect(url).toBe('/api/v1/identity/access/role-request-impact');expect(init?.method).toBe('POST');expect(JSON.parse(init!.body as string)).toEqual({userId:user,roleId:role})
 const headers=new Headers(init?.headers);expect(headers.get('X-Company-Id')).toBe(company);expect(headers.get('Content-Type')).toBe('application/json');expect(headers.has('Idempotency-Key')).toBe(false)
})
it.each([403,404,503])('preserves the native refusal %i without inventing zero permissions',async status=>{
 vi.stubGlobal('fetch',vi.fn(async()=>new Response('{}',{status})));expect(await fetchRoleRequestImpact(company,user,role)).toMatchObject({type:'http',status})
})
it.each([{companyId:role},{userId:role},{roleId:user},{roleHasCriticalPermissions:undefined},{extra:true}])('hides a foreign or malformed owner response %j',async delta=>{
 vi.stubGlobal('fetch',vi.fn(async()=>new Response(JSON.stringify({...impact,...delta}),{status:200})));expect(await fetchRoleRequestImpact(company,user,role)).toEqual({type:'malformed'})
})
it('aborts the source read instead of releasing a late cancelled result',async()=>{
 const abort=new AbortController();abort.abort();vi.stubGlobal('fetch',vi.fn(async()=>{throw new DOMException('Aborted','AbortError')}))
 expect(await fetchRoleRequestImpact(company,user,role,abort.signal)).toEqual({type:'fence-stale'})
})
