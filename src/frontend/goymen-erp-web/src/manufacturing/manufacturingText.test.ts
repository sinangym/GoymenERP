import { describe, expect, it } from 'vitest'
import { failureText } from './manufacturingText'

describe('failureText', () => {
  it('names the unconsumed-input bound of a return apart from the issued bound (F-009 D4)', () => {
    expect(failureText({ kind: 'rule', code: 'manufacturing_return_exceeds_unconsumed' })).toBe(
      'İade, net çıktı ve yarı mamul düşüldükten sonra kalan girdi kütlesini aşamaz. Önce çıktıyı düzeltin veya ' +
        'yarı mamulü yeniden ölçün.',
    )
    expect(failureText({ kind: 'rule', code: 'manufacturing_return_exceeds_issued' })).toBe(
      'İade, bu rulodan çıkan kütleyi aşamaz.',
    )
  })
})
