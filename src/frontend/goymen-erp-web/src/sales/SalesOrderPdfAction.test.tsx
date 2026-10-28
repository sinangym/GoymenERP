/**
 * MOD-07 — "PDF indir" on the S-04 order detail: the server-rendered PDF is saved under the server's file name; loading,
 * forbidden, not found, changed and failed are told apart; nothing is saved from an answer that is not a PDF. Component
 * evidence, NOT_ENV (the real round trip is the backend's HTTP and SQL Server acceptance).
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import detailResponseRaw from './examples/detail-response.json?raw'
import { SalesOrderPdfAction } from './SalesOrderPdfAction'
import { attachmentFileName, salesDraftPdfPath } from './orderPdf'
import { SALES_ORDER_PDF_TEXT as T } from './salesText'
import { COMPANY_A, deferred, installFetch, json, renderSalesApp } from './testHarness'

const DRAFT = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const FILE_NAME = 'Satış_Siparişi_SO-2026-000123_ABC_Demir_2026-10-27_v01_Onaylı.pdf'
const DISPOSITION = `attachment; filename=Sat__Sipari__SO-2026-000123.pdf; filename*=UTF-8''${encodeURIComponent(FILE_NAME)}`

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

function pdf(headers: Record<string, string> = { 'Content-Disposition': DISPOSITION }) {
  return new Response(new Uint8Array([0x25, 0x50, 0x44, 0x46, 0x2d, 0x31]), {
    status: 200,
    headers: { 'Content-Type': 'application/pdf', ...headers },
  })
}

/** Captures what would be saved: the object URL's blob and the anchor's download name; nothing navigates. */
function captureSaves() {
  const createObjectURL = vi.fn((_blob: Blob) => 'blob:order-pdf')
  const revokeObjectURL = vi.fn()
  vi.stubGlobal('URL', Object.assign(URL, { createObjectURL, revokeObjectURL }))
  const downloads: string[] = []
  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
    downloads.push(this.download)
  })
  return { createObjectURL, downloads }
}

function renderAction() {
  return render(<SalesOrderPdfAction draftId={DRAFT} companyId={COMPANY_A} documentNo="SO-2026-000123" />)
}

describe('MOD-07 sales order PDF action', () => {
  it('is busy while the server renders, then saves the PDF under the server file name and says so', async () => {
    const saves = captureSaves()
    const answer = deferred<Response>()
    const fetchMock = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => answer.promise)
    vi.stubGlobal('fetch', fetchMock)
    renderAction()

    await userEvent.click(screen.getByRole('button', { name: T.download }))

    const busy = screen.getByRole('button', { name: T.preparing })
    expect(busy.hasAttribute('disabled')).toBe(true)
    expect(busy.getAttribute('aria-busy')).toBe('true')
    expect(screen.getByRole('status').textContent).toBe(T.preparing)
    // Exactly the order's PDF route, under the committed company; the screen asks for no variant.
    expect(fetchMock).toHaveBeenCalledTimes(1)
    const [input, init] = fetchMock.mock.calls[0]
    expect(String(input)).toBe(`/api/v1${salesDraftPdfPath(DRAFT)}`)
    expect(String(input)).not.toContain('?')
    expect(init?.headers).toMatchObject({ 'X-Company-Id': COMPANY_A })

    answer.resolve(pdf())

    await waitFor(() => expect(screen.getByRole('status').textContent).toBe(T.saved(FILE_NAME)))
    expect(saves.downloads).toEqual([FILE_NAME])
    expect(saves.createObjectURL).toHaveBeenCalledTimes(1)
    expect(saves.createObjectURL.mock.calls[0][0].size).toBe(6)
    expect(screen.getByRole('button', { name: T.download }).hasAttribute('disabled')).toBe(false)
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it.each([
    [403, T.forbidden, false],
    [404, T.notFound, false],
    [409, T.changed, true],
    [422, T.tooLarge, false],
    [503, T.failed, true],
    [500, T.failed, true],
  ])('a %i is told as its own problem, nothing is saved, and only a passing one offers a retry', async (status, message, retryable) => {
    const saves = captureSaves()
    vi.stubGlobal('fetch', vi.fn(async () => json(status === 409 ? { status, code: 'concurrent_change' } : status === 422 ? { status, code: 'sales_order_pdf_too_large' } : { status }, status)))
    renderAction()

    await userEvent.click(screen.getByRole('button', { name: T.download }))

    const alert = await screen.findByRole('alert')
    expect(alert.textContent).toContain(message)
    expect(within(alert).queryByRole('button', { name: T.retry }) !== null).toBe(retryable)
    expect(saves.createObjectURL).not.toHaveBeenCalled()
    expect(saves.downloads).toEqual([])
  })

  it('a 422 without the too-large code is a plain failure (retry offered), never the too-large text', async () => {
    captureSaves()
    vi.stubGlobal('fetch', vi.fn(async () => json({ status: 422, code: 'something_else' }, 422)))
    renderAction()

    await userEvent.click(screen.getByRole('button', { name: T.download }))

    const alert = await screen.findByRole('alert')
    expect(alert.textContent).toContain(T.failed)
    expect(within(alert).getByRole('button', { name: T.retry })).toBeTruthy()
  })

  it('a changed order is fetched again on retry and then saved', async () => {
    const saves = captureSaves()
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(json({ status: 409, code: 'concurrent_change' }, 409))
      .mockResolvedValueOnce(pdf())
    vi.stubGlobal('fetch', fetchMock)
    renderAction()

    await userEvent.click(screen.getByRole('button', { name: T.download }))
    await userEvent.click(within(await screen.findByRole('alert')).getByRole('button', { name: T.retry }))

    await waitFor(() => expect(saves.downloads).toEqual([FILE_NAME]))
    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('no answer, or a 200 that is not a PDF, is a failure and saves nothing', async () => {
    const saves = captureSaves()
    vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('Failed to fetch') }))
    renderAction()
    await userEvent.click(screen.getByRole('button', { name: T.download }))
    expect((await screen.findByRole('alert')).textContent).toContain(T.failed)
    cleanup()

    vi.stubGlobal('fetch', vi.fn(async () => new Response('<html></html>', { status: 200, headers: { 'Content-Type': 'text/html' } })))
    renderAction()
    await userEvent.click(screen.getByRole('button', { name: T.download }))
    expect((await screen.findByRole('alert')).textContent).toContain(T.failed)
    expect(saves.createObjectURL).not.toHaveBeenCalled()
  })

  it('without a server file name the order number names the file', async () => {
    const saves = captureSaves()
    vi.stubGlobal('fetch', vi.fn(async () => pdf({})))
    renderAction()

    await userEvent.click(screen.getByRole('button', { name: T.download }))

    await waitFor(() => expect(saves.downloads).toEqual(['SO-2026-000123.pdf']))
  })

  it('leaving the page abandons the read: the request is aborted and nothing is saved', async () => {
    const saves = captureSaves()
    let signal: AbortSignal | undefined
    const answer = deferred<Response>()
    vi.stubGlobal('fetch', vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
      signal = init?.signal ?? undefined
      return answer.promise
    }))
    const view = renderAction()

    await userEvent.click(screen.getByRole('button', { name: T.download }))
    view.unmount()
    answer.resolve(pdf())
    await Promise.resolve()

    expect(signal?.aborted).toBe(true)
    expect(saves.createObjectURL).not.toHaveBeenCalled()
  })
})

describe('MOD-07 file name of the attachment', () => {
  it('prefers the RFC 6266 UTF-8 name, falls back to the plain one, and keeps only the last path segment', () => {
    expect(attachmentFileName(DISPOSITION)).toBe(FILE_NAME)
    expect(attachmentFileName('attachment; filename="SO-1.pdf"')).toBe('SO-1.pdf')
    expect(attachmentFileName("attachment; filename*=UTF-8''%E0%A4%A; filename=plain.pdf")).toBe('plain.pdf')
    expect(attachmentFileName("attachment; filename*=UTF-8''..%2F..%2Fevil.pdf")).toBe('evil.pdf')
    expect(attachmentFileName('attachment')).toBeNull()
    expect(attachmentFileName(null)).toBeNull()
  })
})

describe('MOD-07 on the S-04 detail', () => {
  it('the order header offers "PDF indir" for the order read under the active company', async () => {
    const saves = captureSaves()
    const fetch = installFetch({ onDetail: () => json(JSON.parse(detailResponseRaw)), onPdf: () => pdf() })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)

    const header = await screen.findByTestId('sales-order-summary')
    await userEvent.click(within(header).getByRole('button', { name: T.download }))

    await waitFor(() => expect(saves.downloads).toEqual([FILE_NAME]))
    const pdfCall = fetch.calls.find((call) => call.url.endsWith('/pdf'))
    expect(pdfCall?.url).toBe(`/api/v1${salesDraftPdfPath(DRAFT)}`)
    expect(pdfCall?.companyId).toBe(COMPANY_A)
  })
})
