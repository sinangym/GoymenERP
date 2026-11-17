import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { decodeFinanceDocumentNumber } from './documentNumber'
import { FinanceDocumentNumberFacts } from './FinanceDocumentNumberFacts'

describe('finance recorded number proof', () => {
  it('keeps an opaque recorded number and lossless Int64 format version without producing either on the client', () => {
    expect(decodeFinanceDocumentNumber({ number: 'MAK-2026-000001', formatVersion: '9007199254740993' }))
      .toEqual({ number: 'MAK-2026-000001', formatVersion: '9007199254740993' })
    expect(decodeFinanceDocumentNumber(undefined)).toBeUndefined()
    for (const value of [null, 'MAK-2026-000001', {}, { number: 'MAK-2026-000001', formatVersion: 1 },
      { number: 'MAK-2026-000001', formatVersion: '01' }, { number: 'MAK-2026-000001', formatVersion: '9223372036854775808' },
      { number: 'bank-reference', formatVersion: '1' }, { number: 'MAK-2026-000001', formatVersion: '1', seed: true }])
      expect(decodeFinanceDocumentNumber(value)).toBeNull()
  })
  it('distinguishes a draft awaiting record from a legacy source without retrospectively inventing a number', () => {
    const view = render(<dl><FinanceDocumentNumberFacts kind="receipt" number={undefined} recorded={false} /></dl>)
    expect(screen.getByText('Kesin kayıtta ayrılır.')).toBeInTheDocument()
    view.rerender(<dl><FinanceDocumentNumberFacts kind="voucher" number={undefined} /></dl>)
    expect(screen.getByText('Bu eski kayıtta belge numarası bulunmuyor.')).toBeInTheDocument()
    view.rerender(<dl><FinanceDocumentNumberFacts kind="voucher" number={{ number: 'DEK-2026-000001', formatVersion: '3' }} /></dl>)
    expect(screen.getByText('Dekont numarası')).toBeInTheDocument()
    expect(screen.getByText('DEK-2026-000001')).toBeInTheDocument()
    expect(screen.getByText('3')).toBeInTheDocument()
  })
})
