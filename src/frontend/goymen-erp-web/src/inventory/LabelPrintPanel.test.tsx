import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { LabelPrintPanel } from './LabelPrintPanel'
import { fetchLabelPdfTemplate, generateLabelPdf } from './labelPrintApi'
import { LABEL_PRINT_TEXT as T } from './labelPrintText'

const boundary = vi.hoisted(() => ({ capture: vi.fn(() => 'SENTETIK-capture'), report: vi.fn() }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => boundary }))
vi.mock('./labelPrintApi', () => ({ generateLabelPdf: vi.fn(), fetchLabelPdfTemplate: vi.fn() }))
const generate = vi.mocked(generateLabelPdf)
const props = { labelId: 'SENTETIK-label', companyId: 'SENTETIK-company', actorId: 'SENTETIK-actor', namespace: 'SENTETIK-context' }

beforeEach(() => { sessionStorage.clear(); vi.clearAllMocks(); vi.mocked(fetchLabelPdfTemplate).mockResolvedValue({ type: 'ok-single', data: { publicationId: null, publicationVersion: null } }) })
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
  it('kayıp yanıtta seçili yayımı ve sürümü aynen korur, güncel yayımı yeniden seçmez', async () => {
    const choice = { publicationId: '58000000-0000-4000-8000-000000000001', publicationVersion: '7' }
    vi.mocked(fetchLabelPdfTemplate).mockResolvedValueOnce({ type: 'ok-single', data: choice })
    generate.mockResolvedValueOnce({ type: 'unknown', reason: 'service-unavailable' }).mockResolvedValueOnce({ type: 'failed', status: 409, code: null })
    render(<LabelPrintPanel {...props} />)
    fireEvent.click(screen.getByRole('button', { name: T.generate }))
    await screen.findByRole('button', { name: T.retry })
    fireEvent.click(screen.getByRole('button', { name: T.retry }))
    await waitFor(() => expect(generate).toHaveBeenCalledTimes(2))
    expect(generate.mock.calls[0]).toEqual(generate.mock.calls[1]); expect(generate.mock.calls[1][4]).toEqual(choice)
    expect(fetchLabelPdfTemplate).toHaveBeenCalledTimes(1)
  })
  it('bozuk şablon yanıtında PDF komutunu başlatmaz', async () => {
    vi.mocked(fetchLabelPdfTemplate).mockResolvedValueOnce({ type: 'malformed' })
    render(<LabelPrintPanel {...props} />)
    fireEvent.click(screen.getByRole('button', { name: T.generate }))
    await waitFor(() => expect(fetchLabelPdfTemplate).toHaveBeenCalledTimes(1))
    expect(generate).not.toHaveBeenCalled(); expect(sessionStorage.length).toBe(0)
  })
  it('saklanan denemeyi yalnız tam alan kümesiyle geri çağırır', () => {
    const storageKey = `goymen.inventory.label-pdf.v1:${props.namespace}:${props.actorId}:${props.companyId}:${props.labelId}`
    const template = { publicationId: '58000000-0000-4000-8000-000000000001', publicationVersion: '7' }
    const stored = { key: 'SENTETIK-key', requestedAtUtc: '2026-11-20T08:00:00.000Z', template }
    const recalled = (value: unknown) => {
      sessionStorage.setItem(storageKey, JSON.stringify(value))
      render(<LabelPrintPanel {...props} />)
      const retry = screen.queryByRole('button', { name: T.retry }) !== null
      cleanup()
      return retry
    }
    expect(recalled(stored)).toBe(true)
    expect(recalled({ key: stored.key, requestedAtUtc: stored.requestedAtUtc })).toBe(true)
    expect(recalled({ ...stored, extra: 1 })).toBe(false)
    expect(recalled({ key: stored.key, requestedAtUtc: stored.requestedAtUtc, extra: 1 })).toBe(false)
    expect(recalled({ key: stored.key, template })).toBe(false)
    expect(recalled({ requestedAtUtc: stored.requestedAtUtc, template })).toBe(false)
    expect(recalled({ ...stored, template: { ...template, extra: 1 } })).toBe(false)
    expect(recalled({ ...stored, template: { publicationId: template.publicationId } })).toBe(false)
  })
})
