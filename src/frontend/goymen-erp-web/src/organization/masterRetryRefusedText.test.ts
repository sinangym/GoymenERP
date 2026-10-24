import { describe, expect, it } from 'vitest'
import { masterFailureText, masterRetryRefusedText } from './organizationText'

describe('masterRetryRefusedText (BLG-11)', () => {
  it('names the refusal of the retry and never says the first request was not applied', () => {
    const text = masterRetryRefusedText({ kind: 'forbidden' })
    expect(text).toBe(
      `Son deneme sunucu tarafından reddedildi (${masterFailureText({ kind: 'forbidden' }).title}). Bu, ilk isteğin uygulanmadığı anlamına gelmez.`,
    )
    expect(text).not.toContain('uygulanmadı.')
  })

  it('uses the code-in-use title for a taken code', () => {
    expect(masterRetryRefusedText({ kind: 'code-in-use' })).toContain(masterFailureText({ kind: 'code-in-use' }).title)
  })
})
