import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { LabelPrintPanel } from './LabelPrintPanel'
import { generateLabelPdf } from './labelPrintApi'
import { LABEL_PRINT_TEXT as T } from './labelPrintText'

const boundary = vi.hoisted(() => ({ capture: vi.fn(() => 'SENTETIK-capture'), report: vi.fn() }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => boundary }))
vi.mock('./labelPrintApi', () => ({ generateLabelPdf: vi.fn() }))
const generate = vi.mocked(generateLabelPdf)
const props = { labelId: 'SENTETIK-label', companyId: 'SENTETIK-company', actorId: 'SENTETIK-actor', namespace: 'SENTETIK-context' }

beforeEach(() => { sessionStorage.clear(); vi.clearAllMocks() })
afterEach(() => cleanup())

describe('etiket PDF paneli', () => {
  it('503 sonrası aynı zamanı ve anahtarı yeniden gönderir', async () => {
    generate.mockResolvedValueOnce({ type: 'unknown', reason: 'service-unavailable' })
      .mockResolvedValueOnce({ type: 'failed', status: 422, code: 'inventory_label_pdf_barcode_unsupported' })
    render(<LabelPrintPanel {...props} />)
    fireEvent.click(screen.getByRole('button', { name: T.generate }))
    await screen.findByRole('button', { name: T.retry })
    fireEvent.click(screen.getByRole('button', { name: T.retry }))
    await screen.findByText(T.unsupported)
    expect(generate.mock.calls[0]).toEqual(generate.mock.calls[1])
    expect(sessionStorage.length).toBe(0)
  })

  it('403 yetki sınırına bildirilir ve kayıt oluşturuldu mesajı gösterilmez', async () => {
    generate.mockResolvedValueOnce({ type: 'failed', status: 403, code: null })
    render(<LabelPrintPanel {...props} />)
    fireEvent.click(screen.getByRole('button', { name: T.generate }))
    await waitFor(() => expect(boundary.report).toHaveBeenCalledWith(403, 'SENTETIK-capture'))
    expect(screen.queryByText(T.stored)).toBeNull()
  })
})
