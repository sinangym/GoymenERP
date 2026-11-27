import { afterEach,expect,it,vi } from 'vitest'
import { decodeMorningSummary,fetchMorningSummary,MORNING_SUMMARY_PATH } from './workMorningSummaryApi'
const company='aaaaaaaa-0000-4000-8000-000000000001',other='bbbbbbbb-0000-4000-8000-000000000002',digest='cccccccc-0000-4000-8000-000000000001'
const summary={companyId:company,digestId:digest,slotUtc:'2026-11-27T06:00:00Z',scheduleVersion:'settings-v1',counts:[{kind:'task-assigned',count:3}]}
afterEach(()=>vi.unstubAllGlobals())
it('accepts exact server counts and source references, including the native 100-row bound, without a derived total',()=>{
 expect(decodeMorningSummary(summary,company)).toEqual(summary)
 expect(decodeMorningSummary({...summary,counts:[]},company)?.counts).toEqual([])
 expect(decodeMorningSummary({...summary,counts:[{kind:'task-assigned',count:100}]},company)?.counts[0]?.count).toBe(100)
 expect(decodeMorningSummary({...summary,counts:[{kind:'task-assigned',count:99},{kind:'allocation-released',count:1}]},company)).not.toBeNull()
})
it.each([{companyId:other},{companyId:company+'\n'},{digestId:'00000000-0000-0000-0000-000000000000'},{digestId:digest+'\n'},
 {slotUtc:'2026-02-30T06:00:00Z'},{slotUtc:'2026-11-27T06:00:00Z\n'},{slotUtc:'2026-11-27T06:00:00+03:00'},
 {scheduleVersion:''},{scheduleVersion:'x'.repeat(129)},{counts:null},{extra:true},
 ...[0,-1,1.5,'3',101,Number.NaN,Number.POSITIVE_INFINITY].map(count=>({counts:[{kind:'task-assigned',count}]})),
 {counts:[{kind:'future-kind',count:1}]},{counts:[{kind:'mention',count:1}]},{counts:[{kind:'task-assigned',count:1,extra:true}]},
 {counts:[{kind:'task-assigned',count:1},{kind:'task-assigned',count:2}]},{counts:[{kind:'task-assigned',count:60},{kind:'allocation-released',count:41}]}])('rejects malformed, foreign, partial or truncated summary shapes %#',delta=>{
 expect(decodeMorningSummary({...summary,...delta},company)).toBeNull()
})
it.each(['companyId','digestId','slotUtc','scheduleVersion','counts'])('requires every wire field: %s',field=>{
 const value:Record<string,unknown>={...summary};delete value[field];expect(decodeMorningSummary(value,company)).toBeNull()
})
it('sends the committed company as a header with no query/body and keeps 204 distinct from zero counts',async()=>{
 const fetch=vi.fn(async()=>new Response(null,{status:204}));vi.stubGlobal('fetch',fetch)
 expect(await fetchMorningSummary(company)).toEqual({type:'ok-single',data:null})
 expect(fetch).toHaveBeenCalledWith(expect.stringContaining(MORNING_SUMMARY_PATH),expect.objectContaining({headers:expect.objectContaining({'X-Company-Id':company})}))
 const [url,init]=fetch.mock.calls[0]! as unknown as [string,RequestInit];expect(url).not.toContain('?');expect(init.body).toBeUndefined();expect(init.method).toBeUndefined()
})
it.each([200,206,403,503])('preserves malformed/null or refusal outcomes without inventing counts: %s',async status=>{
 vi.stubGlobal('fetch',vi.fn(async()=>new Response(status===200?'null':'{}',{status})))
 const result=await fetchMorningSummary(company)
 expect(result.type).toBe(status===200?'malformed':'http')
})
