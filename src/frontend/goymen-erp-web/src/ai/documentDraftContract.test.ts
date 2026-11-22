import { describe, expect, it } from 'vitest'
import { decodeDocumentDraft, draftInvalidFields, draftUnreadableReasons, mediaTypeOf } from './documentDraftContract'
import { draftBody, draftField as field } from '../test/aiDocumentDraftFixture'

describe('belgeden taslak sözleşmesi', () => {
  it('sözleşmedeki taslağı aynen çözer', () => {
    const draft = decodeDocumentDraft(draftBody())
    expect(draft).not.toBeNull()
    expect(draft?.lines[0]?.wallThickness.status).toBe('invalid')
    expect(draft?.createsRecord).toBe(false)
  })

  it.each([
    ['kayıt oluşturduğunu söyleyen cevap', { createsRecord: true }],
    ['bilinmeyen üye (alıcı)', { recipient: 'SENTETIK' }],
    ['bilinmeyen taslak türü', { draftKind: 'payment' }],
    ['bilinmeyen sahip ekranı', { openIn: { ownerModule: 'Finance', screenCode: 'finance.payments.release' } }],
    ['nedeni olmayan hatalı alan', { currency: field('currency', 'XX', 'invalid', null) }],
    ['nedeni olan doğrulanmış alan', { currency: field('currency', 'TRY', 'verified', 'missing') }],
    ['yanlış alan kodu', { currency: field('documentDate', 'TRY') }],
    ['bozuk kaynak özeti', { source: { sha256: 'xyz', mediaType: 'application/xml', byteLength: 1 } }],
    ['sırasız satır numarası', { lines: [{ ...draftBody().lines[0], lineNumber: 2 }] }],
  ])('%s kabul edilmez (fail-closed)', (_name, overrides) => {
    expect(decodeDocumentDraft(draftBody(overrides))).toBeNull()
  })

  it('problem alanlarından yalnız bilinen adlar ve kodlar okunur', () => {
    expect(draftInvalidFields({ invalidFields: ['content', 'SENTETIK<script>'] })).toEqual(['content'])
    expect(draftUnreadableReasons({ invalidFields: ['ocr_unavailable', 'Bad Value'] })).toEqual(['ocr_unavailable'])
    expect(draftInvalidFields(null)).toEqual([])
  })

  it('dosya türü bildirilen türden veya uzantıdan çıkarılır; bilinmeyen reddedilir', () => {
    expect(mediaTypeOf({ name: 'fatura.XML', type: '' })).toBe('application/xml')
    expect(mediaTypeOf({ name: 'liste.csv', type: 'text/csv' })).toBe('text/csv')
    expect(mediaTypeOf({ name: 'tarama.jpg', type: '' })).toBe('image/jpeg')
    expect(mediaTypeOf({ name: 'program.exe', type: 'application/x-msdownload' })).toBeNull()
  })
})
