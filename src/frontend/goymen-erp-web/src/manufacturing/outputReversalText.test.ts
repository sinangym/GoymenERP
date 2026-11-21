import { describe, expect, it } from 'vitest'
import { failureText } from './manufacturingText'

// F-009 D6 (URT-D28, URT-D30): the output reversal's domain refusals are told by their stable code, never guessed.
describe('output reversal refusal texts', () => {
  it.each([
    ['manufacturing_output_not_declared', 'beyan edilmedi'],
    ['manufacturing_output_line_missing', 'plan çıktı satırına bağlı değil'],
    ['manufacturing_output_line_not_in_plan', 'plan çıktı satırlarından biri değil'],
    ['manufacturing_output_reversal_already_recorded', 'zaten kaydedildi'],
    ['manufacturing_output_reversal_exceeds_declared', 'kalan net kütlesini aşıyor'],
  ])('tells %s in Turkish', (code, fragment) => {
    const text = failureText({ kind: 'rule', code })
    expect(text).toContain(fragment)
    expect(text).not.toContain(code)
  })

  it('keeps an unknown reversal code generic with the code', () => {
    expect(failureText({ kind: 'rule', code: 'manufacturing_output_reversal_unknown' })).toBe(
      'İşlem reddedildi (manufacturing_output_reversal_unknown).',
    )
  })
})
