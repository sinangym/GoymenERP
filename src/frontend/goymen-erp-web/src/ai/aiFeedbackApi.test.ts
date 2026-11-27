import { afterEach,describe,expect,it,vi } from 'vitest'
import { decodeAiFeedbackView,decodeAiFeedbackReceipt,isAiFeedbackRequest,AI_FEEDBACK_REASONS } from './aiFeedbackContract'
import { fetchAiFeedback,sendAiFeedback } from './aiFeedbackApi'
const company='aaaaaaaa-0000-4000-8000-000000000001',id='cccccccc-0000-4000-8000-000000000001'
const request={expectedVersion:'0',kind:'Accepted',reasonCode:null} as const
const view={answerId:id,version:'0',kind:null,reasonCode:null}
const receipt={answerId:id,version:'1',kind:'Accepted',reasonCode:null,originalCorrelationId:'original',replayed:false}
afterEach(()=>vi.unstubAllGlobals())
describe('C9 content-free feedback contract',()=>{
 it('keeps zero/null initial state and distinguishes an unreviewed answer from recorded feedback',()=>{
  expect(decodeAiFeedbackView(view,id)).toEqual(view)
  expect(decodeAiFeedbackView({...view,version:'1',kind:'Accepted'},id)).not.toBeNull()
  for(const delta of [{answerId:company},{answerId:id+'\n'},{version:'0\n'},{version:'01'},{version:0},{kind:'Future'},{reasonCode:'other'},{extra:true}]) expect(decodeAiFeedbackView({...view,...delta},id)).toBeNull()
  expect(decodeAiFeedbackView({...view,version:'1'},id)).toBeNull()
 })
 it.each(['Accepted','Corrected','Rejected','Reverted'] as const)('accepts only fixed reason codes for %s without free text',kind=>{
  expect(isAiFeedbackRequest({...request,kind})).toBe(kind==='Accepted')
  for(const reasonCode of AI_FEEDBACK_REASONS) expect(isAiFeedbackRequest({...request,kind,reasonCode})).toBe(true)
  for(const delta of [{reasonCode:'Özel serbest metin'},{reasonCode:'other\n'},{expectedVersion:'9223372036854775807'},{expectedVersion:'0\n'},{expectedVersion:'-1'},{expectedVersion:'00'},{answerId:id},{question:'İçerik'},{kind:'Future'}]) expect(isAiFeedbackRequest({...request,kind,...delta})).toBe(false)
 })
 it('binds immutable receipts to the submitted body and increment, including an older replay after a newer GET',()=>{
  expect(decodeAiFeedbackReceipt(receipt,id,request)).toEqual(receipt)
  expect(decodeAiFeedbackReceipt({...receipt,replayed:true},id,request)?.version).toBe('1')
  for(const delta of [{answerId:company},{version:'2'},{version:1},{kind:'Corrected'},{reasonCode:'other'},{originalCorrelationId:'original\n'},{replayed:'true'},{answerText:'İçerik'}])expect(decodeAiFeedbackReceipt({...receipt,...delta},id,request)).toBeNull()
 })
 it('sends exact company/header/key/body and refuses malformed identities or disagreeing replay headers',async()=>{
  const fetch=vi.fn(async(_input:RequestInfo|URL,_init?:RequestInit)=>new Response(JSON.stringify(receipt),{status:200}));vi.stubGlobal('fetch',fetch)
  expect((await sendAiFeedback(company,id,request,'same-key')).type).toBe('applied')
  const init=fetch.mock.calls[0]![1]!
  expect(JSON.parse(init.body as string)).toEqual(request);expect(init.headers).toMatchObject({'X-Company-Id':company,'Idempotency-Key':'same-key'})
  fetch.mockResolvedValue(new Response(JSON.stringify({...receipt,replayed:true}),{status:200}))
  expect((await sendAiFeedback(company,id,request,'same-key')).type).toBe('unknown')
  fetch.mockResolvedValue(new Response(JSON.stringify({...receipt,replayed:true}),{status:200,headers:{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'}}))
  expect((await sendAiFeedback(company,id,request,'same-key')).type).toBe('applied')
  fetch.mockResolvedValue(new Response(JSON.stringify({...receipt,answerId:company}),{status:200}))
  expect((await sendAiFeedback(company,id,request,'same-key')).type).toBe('unknown')
  fetch.mockClear();expect((await sendAiFeedback(company,id+'\n',request,'same-key')).type).toBe('unknown');expect(fetch).not.toHaveBeenCalled()
 })
 it.each([403,404,409,503])('keeps native status %i separate from an applied receipt and does not retry',async status=>{
  const fetch=vi.fn(async()=>new Response('{}',{status}));vi.stubGlobal('fetch',fetch)
  expect((await sendAiFeedback(company,id,request,'key')).type).toBe(status===503?'unknown':'refused');expect(fetch).toHaveBeenCalledTimes(1)
 })
 it('uses the actual answer path/company and rejects foreign GET identity',async()=>{
  const fetch=vi.fn(async()=>new Response(JSON.stringify({...view,answerId:company}),{status:200}));vi.stubGlobal('fetch',fetch)
  expect((await fetchAiFeedback(company,id)).type).toBe('malformed')
  expect(fetch).toHaveBeenCalledWith(expect.stringContaining(`/ai/answers/${id}/feedback`),expect.objectContaining({headers:expect.objectContaining({'X-Company-Id':company})}))
  fetch.mockClear();expect((await fetchAiFeedback(company,id+'\n')).type).toBe('malformed');expect(fetch).not.toHaveBeenCalled()
 })
})
