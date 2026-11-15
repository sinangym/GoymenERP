import { describe, expect, it } from 'vitest'

import { DOCUMENT_PROBLEM } from './contract'
import { DOCUMENTS_TEXT, refusalText, typeLabel } from './documentsText'

describe('documentsText — B-26 / FNS-D52 file kinds (SENTETIK)', () => {
  it.each([
    ['application/pdf', 'PDF'],
    ['image/png', 'PNG'],
    ['image/jpeg', 'JPEG'],
    ['application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', 'XLSX'],
    ['text/plain', 'MT940'],
    ['text/csv', 'CSV'],
  ])('labels the canonical type %s as %s', (contentType, label) => {
    expect(typeLabel(contentType)).toBe(label)
  })

  it('shows an unknown type as the server sent it', () => {
    expect(typeLabel('application/octet-stream')).toBe('application/octet-stream')
  })

  it('does not promise a fixed kind list when a type is refused; the record lists its own kinds', () => {
    const refused = refusalText(422, DOCUMENT_PROBLEM.typeNotAllowed)
    expect(refused).not.toMatch(/PDF|PNG|JPEG/)
    expect(DOCUMENTS_TEXT.typeLocal).not.toMatch(/PDF|PNG|JPEG/)
    expect(DOCUMENTS_TEXT.allowed(['.pdf', '.xlsx', '.sta', '.csv'], 20_971_520)).toContain('PDF, XLSX, STA, CSV')
  })
})
