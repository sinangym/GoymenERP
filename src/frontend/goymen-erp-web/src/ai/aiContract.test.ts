import { afterEach, describe, expect, it, vi } from 'vitest'
import { askAi } from './aiApi'
import { decodeAiAskAnswer, decodeAiCapabilities, parseToolArguments, problemCode, problemInvalidFields } from './aiContract'

const TOOL = { toolId: 'test.item-summary.v1', version: '1', ownerModule: 'test', dataClass: 'Internal' }
const SOURCE = {
  targetKind: 'test-item',
  targetId: 'cccccccc-0000-0000-0000-000000000003',
  title: 'Sentetik kaynak',
  sourceVersion: '7',
  asOfUtc: '2026-11-15T08:00:00+00:00',
  dataClass: 'Internal',
}
const RESULT = { toolId: TOOL.toolId, toolVersion: '1', outcome: 'succeeded', contentJson: '{"a":1}', source: SOURCE }
const CITATION = {
  toolId: TOOL.toolId,
  toolVersion: '1',
  targetKind: SOURCE.targetKind,
  targetId: SOURCE.targetId,
  title: SOURCE.title,
  sourceVersion: SOURCE.sourceVersion,
  asOfUtc: SOURCE.asOfUtc,
}
const COMPOSED = { text: '[1] Sentetik kaynak: a: 1;', citations: [CITATION], modelDeployment: 'sentetik-yerel', policyVersion: 'ai.gateway-policy:1' }

describe('Plan AI-3 — AI sözleşme çözücüleri', () => {
  it('hazır yetenek ve kapalı durum kabul edilir', () => {
    expect(decodeAiCapabilities({ availability: 'ready', panelAvailable: true, tools: [TOOL] })?.tools).toHaveLength(1)
    expect(decodeAiCapabilities({ availability: 'disabled', panelAvailable: false, tools: [] })?.availability).toBe('disabled')
  })

  it.each([
    ['fazla alan', { availability: 'ready', panelAvailable: true, tools: [], extra: 1 }],
    ['eksik alan', { availability: 'ready', tools: [] }],
    ['bilinmeyen durum', { availability: 'maybe', panelAvailable: false, tools: [] }],
    ['kapalı runtime ama panel açık', { availability: 'disabled', panelAvailable: true, tools: [] }],
    ['kapalı panelde araç', { availability: 'ready', panelAvailable: false, tools: [TOOL] }],
    ['hazır olmayan runtime’da araç', { availability: 'unavailable', panelAvailable: false, tools: [TOOL] }],
    ['yinelenen araç', { availability: 'ready', panelAvailable: true, tools: [TOOL, TOOL] }],
    ['geçersiz araç kimliği', { availability: 'ready', panelAvailable: true, tools: [{ ...TOOL, toolId: 'Item Summary' }] }],
    ['bilinmeyen veri sınıfı', { availability: 'ready', panelAvailable: true, tools: [{ ...TOOL, dataClass: 'Secret' }] }],
    ['dizi değil', []],
  ])('yetenek çözücü reddeder: %s', (_name, body) => {
    expect(decodeAiCapabilities(body)).toBeNull()
  })

  it('cevap ve bulunamadı sonucu kabul edilir', () => {
    const answer = decodeAiAskAnswer({
      outcome: 'answered',
      toolResults: [RESULT, { toolId: TOOL.toolId, toolVersion: '1', outcome: 'not_found', contentJson: null, source: null }],
      answer: COMPOSED,
    })
    expect(answer?.toolResults.map((result) => result.outcome)).toEqual(['succeeded', 'not_found'])
    expect(answer?.answer?.citations).toHaveLength(1)
    expect(
      decodeAiAskAnswer({
        outcome: 'answered',
        toolResults: [{ ...RESULT, source: { ...SOURCE, asOfUtc: '2026-11-15T08:00:00Z' } }],
        answer: COMPOSED,
      }),
    ).not.toBeNull()
  })

  it('WP-A1: cevap veremem durumu metinsiz kabul edilir', () => {
    const answer = decodeAiAskAnswer({ outcome: 'cannot_answer', toolResults: [], answer: null })
    expect(answer).toEqual({ outcome: 'cannot_answer', toolResults: [], answer: null })
  })

  it.each([
    ['cevaplandı ama metin yok', { outcome: 'answered', toolResults: [RESULT], answer: null }],
    ['cevap veremem ama metin var', { outcome: 'cannot_answer', toolResults: [RESULT], answer: COMPOSED }],
    ['answer alanı eksik', { outcome: 'answered', toolResults: [RESULT] }],
    ['kaynaksız metin', { outcome: 'answered', toolResults: [RESULT], answer: { ...COMPOSED, citations: [] } }],
    ['bırakılmamış kaynağa atıf', { outcome: 'answered', toolResults: [RESULT], answer: { ...COMPOSED, citations: [{ ...CITATION, targetId: 'dddddddd-0000-0000-0000-000000000004' }] } }],
    ['atıfta fazla alan', { outcome: 'answered', toolResults: [RESULT], answer: { ...COMPOSED, citations: [{ ...CITATION, prompt: 'x' }] } }],
    ['metinde fazla alan', { outcome: 'answered', toolResults: [RESULT], answer: { ...COMPOSED, provider: 'x' } }],
    ['bilinmeyen sonuç', { outcome: 'partial', toolResults: [RESULT], answer: null }],
  ])('WP-A1: cevap gövdesi reddedilir: %s', (_name, body) => {
    expect(decodeAiAskAnswer(body)).toBeNull()
  })

  it.each([
    ['bulunamadı içerik taşıyor', { toolId: TOOL.toolId, toolVersion: '1', outcome: 'not_found', contentJson: '{}', source: null }],
    ['bulunamadı kaynak taşıyor', { toolId: TOOL.toolId, toolVersion: '1', outcome: 'not_found', contentJson: null, source: SOURCE }],
    ['kaynak kimliği UUID değil', { ...RESULT, source: { ...SOURCE, targetId: 'SENTETIK-1' } }],
    ['zaman UTC değil', { ...RESULT, source: { ...SOURCE, asOfUtc: '2026-11-15T08:00:00+03:00' } }],
    ['bilinmeyen sonuç', { ...RESULT, outcome: 'partial' }],
    ['fazla alan', { ...RESULT, companyId: 'cccccccc-0000-0000-0000-000000000003' }],
    ['içerik metin değil', { ...RESULT, contentJson: { a: 1 } }],
  ])('cevap çözücü reddeder: %s', (_name, result) => {
    expect(decodeAiAskAnswer({ outcome: 'cannot_answer', toolResults: [result], answer: null })).toBeNull()
  })

  it('problem kodu ve yalnız bilinen geçersiz alanlar okunur', () => {
    expect(problemCode({ code: 'ai_disabled' })).toBe('ai_disabled')
    expect(problemCode(null)).toBeNull()
    expect(problemInvalidFields({ invalidFields: ['tool', 'companyId', 'question'] })).toEqual(['question', 'tool'])
    expect(problemInvalidFields({ invalidFields: 'tool' })).toEqual([])
  })

  it('argüman metni boş, tek JSON nesnesi veya geçersizdir', () => {
    expect(parseToolArguments('   ')).toBeNull()
    expect(parseToolArguments('{"itemId":"SENTETIK-1"}')).toEqual({ itemId: 'SENTETIK-1' })
    expect(parseToolArguments('[1]')).toBe('invalid')
    expect(parseToolArguments('"metin"')).toBe('invalid')
    expect(parseToolArguments('{bozuk')).toBe('invalid')
  })
})


const GENERAL = { outcome:'answered',mode:'general-chat',toolResults:[],
 answer:{text:'SENTETIK genel cevap',grounded:false,citations:[],modelDeployment:'sentetik',policyVersion:'policy:1'} }
afterEach(()=>vi.unstubAllGlobals())
describe('WP-B5 explicit mode wire isolation',()=>{
 it('accepts only general ungrounded empty-tool responses and its explicit cannot-answer shape',()=>{
  expect(decodeAiAskAnswer(GENERAL,'general-chat')).toEqual(GENERAL)
  expect(decodeAiAskAnswer({...GENERAL,outcome:'cannot_answer',answer:null},'general-chat'))
   .toEqual({...GENERAL,outcome:'cannot_answer',answer:null})
  expect(decodeAiAskAnswer(GENERAL)).toBeNull()
  expect(decodeAiAskAnswer({outcome:'answered',toolResults:[RESULT],answer:COMPOSED},'general-chat')).toBeNull()
 })
 it.each([
  ['missing mode',{...GENERAL,mode:undefined}],['wrong mode',{...GENERAL,mode:'erp'}],['future mode',{...GENERAL,mode:'future'}],
  ['ERP results',{...GENERAL,toolResults:[RESULT]}],['ERP citations',{...GENERAL,answer:{...GENERAL.answer,citations:[CITATION]}}],
  ['grounded true',{...GENERAL,answer:{...GENERAL.answer,grounded:true}}],['grounded missing',{...GENERAL,answer:{...GENERAL.answer,grounded:undefined}}],
  ['answer missing',{...GENERAL,answer:null}],['cannot-answer text',{...GENERAL,outcome:'cannot_answer'}],
  ['extra context',{...GENERAL,context:{}}],['extra provider',{...GENERAL,answer:{...GENERAL.answer,provider:'invented'}}],
  ['unknown outcome',{...GENERAL,outcome:'partial'}],
 ])('rejects general %s without releasing ERP material',(_label,body)=>{
  expect(decodeAiAskAnswer(body,'general-chat')).toBeNull()
 })
 it('retains the unchanged ERP four-field request and consumes only its matching response mode',async()=>{
  const fetch=vi.fn(async(_input:RequestInfo|URL,_init?:RequestInit)=>new Response(JSON.stringify({outcome:'answered',toolResults:[RESULT],answer:COMPOSED}),{status:200}))
  vi.stubGlobal('fetch',fetch)
  const request={question:'SENTETIK ERP',toolId:TOOL.toolId,toolVersion:'1',arguments:{id:SOURCE.targetId}}
  expect((await askAi(request,SOURCE.targetId)).type).toBe('ok-single')
  expect(JSON.parse(fetch.mock.calls[0]![1]!.body as string)).toEqual(request)
  expect((fetch.mock.calls[0]![1]!.headers as Record<string,string>)['X-Company-Id']).toBe(SOURCE.targetId)
  fetch.mockImplementation(async()=>new Response(JSON.stringify(GENERAL),{status:200}))
  expect((await askAi(request,SOURCE.targetId)).type).toBe('malformed')
 })
 it('sends only question and general-chat mode on the same endpoint, never owner context or a tool',async()=>{
  const fetch=vi.fn(async(_input:RequestInfo|URL,_init?:RequestInit)=>new Response(JSON.stringify(GENERAL),{status:200}))
  vi.stubGlobal('fetch',fetch)
  const answer=await askAi({question:'SENTETIK genel',mode:'general-chat'},SOURCE.targetId)
  expect(answer.type).toBe('ok-single')
  expect(String(fetch.mock.calls[0]![0])).toMatch(/\/ai\/ask$/)
  expect(JSON.parse(fetch.mock.calls[0]![1]!.body as string)).toEqual({question:'SENTETIK genel',mode:'general-chat'})
  expect((fetch.mock.calls[0]![1]!.headers as Record<string,string>)['X-Company-Id']).toBe(SOURCE.targetId)
  fetch.mockImplementation(async()=>new Response(JSON.stringify({outcome:'answered',toolResults:[RESULT],answer:COMPOSED}),{status:200}))
  expect((await askAi({question:'SENTETIK genel',mode:'general-chat'},SOURCE.targetId)).type).toBe('malformed')
 })
 it('recognizes the native invalid mode field without displaying unknown fields',()=>{
  expect(problemInvalidFields({invalidFields:['mode','question','context']})).toEqual(['question','mode'])
 })
})
