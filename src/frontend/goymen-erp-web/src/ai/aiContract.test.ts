import { describe, expect, it } from 'vitest'
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
    })
    expect(answer?.toolResults.map((result) => result.outcome)).toEqual(['succeeded', 'not_found'])
    expect(decodeAiAskAnswer({ outcome: 'answered', toolResults: [{ ...RESULT, source: { ...SOURCE, asOfUtc: '2026-11-15T08:00:00Z' } }] })).not.toBeNull()
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
    expect(decodeAiAskAnswer({ outcome: 'answered', toolResults: [result] })).toBeNull()
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
