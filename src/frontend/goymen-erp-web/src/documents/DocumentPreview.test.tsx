import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { DocumentPreview } from './DocumentPreview'
import { OWNER_KIND, type DocumentRow } from './contract'
import { DOCUMENTS_TEXT as T } from './documentsText'

const COMPANY = 'aaaaaaaa-0000-4000-8000-000000000001'
const OTHER_COMPANY = 'bbbbbbbb-0000-4000-8000-000000000002'

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => {
    resolve = done
  })
  return { promise, resolve }
}

function row(contentType: string): DocumentRow {
  return {
    id: 'd0000000-0000-4000-8000-000000000001',
    companyId: COMPANY,
    ownerKind: OWNER_KIND.counterpartyParty,
    ownerId: '11111111-1111-4111-8111-111111111111',
    versionNo: 1,
    fileName: contentType === 'image/jpeg' ? 'Fotoğraf.jpg' : 'Tarama.png',
    contentType,
    sizeBytes: 4,
    sha256: 'a'.repeat(64),
    scanState: 'Clean',
    downloadable: true,
    uploadedBy: 'company-a-user',
    uploadedAtUtc: '2026-10-08T09:00:00Z',
  }
}

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('B-02 original document image preview', () => {
  it.each(['image/png', 'image/jpeg'])('renders only a verified %s response under the active company fence', async (contentType) => {
    const createObjectURL = vi.fn(() => 'blob:preview')
    const revokeObjectURL = vi.fn()
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL, revokeObjectURL }))
    const fetchMock = vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
      expect(init?.headers).toMatchObject({ 'X-Company-Id': COMPANY })
      return new Response(new Uint8Array([1, 2, 3, 4]), { status: 200, headers: { 'Content-Type': contentType } })
    })
    vi.stubGlobal('fetch', fetchMock)
    const document = row(contentType)
    render(<DocumentPreview row={document} companyId={COMPANY} />)

    const trigger = screen.getByRole('button', { name: T.preview(document.fileName) })
    await userEvent.click(trigger)

    const dialog = await screen.findByRole('dialog', { name: T.previewTitle(document.fileName) })
    const close = screen.getByRole('button', { name: T.closePreview })
    expect(document.activeElement).toBe(close)
    expect(screen.getByRole('img', { name: T.previewAlt(document.fileName) }).getAttribute('src')).toBe('blob:preview')
    expect(fetchMock).toHaveBeenCalledWith(`/api/v1/documents/${document.id}/content`, expect.any(Object))
    await userEvent.tab()
    expect(document.activeElement).toBe(close)
    await userEvent.tab({ shift: true })
    expect(document.activeElement).toBe(close)
    expect(dialog.getAttribute('aria-modal')).toBe('true')
    await userEvent.keyboard('{Escape}')
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(document.activeElement).toBe(trigger)
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:preview')
  })

  it('keeps a delayed request alive through loading renders and reaches a stable ready URL', async () => {
    const answer = deferred<Response>()
    const createObjectURL = vi.fn(() => 'blob:delayed')
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL, revokeObjectURL: vi.fn() }))
    vi.stubGlobal('fetch', vi.fn(() => answer.promise))
    const document = row('image/png')
    render(<DocumentPreview row={document} companyId={COMPANY} />)

    await userEvent.click(screen.getByRole('button', { name: T.preview(document.fileName) }))
    expect(screen.getByRole('button', { name: T.preview(document.fileName) }).textContent).toBe(T.previewing)
    answer.resolve(new Response(new Uint8Array([1]), { status: 200, headers: { 'Content-Type': 'image/png' } }))

    expect(await screen.findByRole('img', { name: T.previewAlt(document.fileName) })).toBeTruthy()
    expect(createObjectURL).toHaveBeenCalledTimes(1)
    expect(screen.getByRole('img').getAttribute('src')).toBe('blob:delayed')
  })

  it('revokes each ready URL on close or unmount and replaces it on reopen', async () => {
    const createObjectURL = vi.fn().mockReturnValueOnce('blob:first').mockReturnValueOnce('blob:second')
    const revokeObjectURL = vi.fn()
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL, revokeObjectURL }))
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response(new Uint8Array([1]), { status: 200, headers: { 'Content-Type': 'image/png' } })),
    )
    const document = row('image/png')
    const view = render(<DocumentPreview row={document} companyId={COMPANY} />)
    const trigger = screen.getByRole('button', { name: T.preview(document.fileName) })

    await userEvent.click(trigger)
    expect((await screen.findByRole('img')).getAttribute('src')).toBe('blob:first')
    await userEvent.click(screen.getByRole('button', { name: T.closePreview }))
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:first')
    await userEvent.click(trigger)
    expect((await screen.findByRole('img')).getAttribute('src')).toBe('blob:second')
    view.unmount()
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:second')
  })

  it('aborts a stale company-context request and ignores its delayed response', async () => {
    const answer = deferred<Response>()
    let signal: AbortSignal | undefined
    const createObjectURL = vi.fn(() => 'blob:stale')
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL, revokeObjectURL: vi.fn() }))
    vi.stubGlobal(
      'fetch',
      vi.fn((_input: RequestInfo | URL, init?: RequestInit) => {
        signal = init?.signal ?? undefined
        return answer.promise
      }),
    )
    const document = row('image/png')
    const view = render(<DocumentPreview row={document} companyId={COMPANY} />)

    await userEvent.click(screen.getByRole('button', { name: T.preview(document.fileName) }))
    view.rerender(<DocumentPreview row={document} companyId={OTHER_COMPANY} />)
    expect(signal?.aborted).toBe(true)
    answer.resolve(new Response(new Uint8Array([1]), { status: 200, headers: { 'Content-Type': 'image/png' } }))
    await Promise.resolve()

    expect(screen.queryByRole('img')).toBeNull()
    expect(createObjectURL).not.toHaveBeenCalled()
  })

  it.each(['application/pdf', 'text/html', 'image/svg+xml', 'application/octet-stream'])(
    'never offers an inline preview for %s',
    (contentType) => {
      const fetchMock = vi.fn()
      vi.stubGlobal('fetch', fetchMock)
      render(<DocumentPreview row={row(contentType)} companyId={COMPANY} />)
      expect(screen.queryByRole('button')).toBeNull()
      expect(screen.queryByRole('img')).toBeNull()
      expect(fetchMock).not.toHaveBeenCalled()
    },
  )

  it('refuses a response whose bytes are labelled as a different or unsafe media type', async () => {
    const createObjectURL = vi.fn(() => 'blob:unsafe')
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL, revokeObjectURL: vi.fn() }))
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response('<svg/>', { status: 200, headers: { 'Content-Type': 'image/svg+xml' } })),
    )
    const document = row('image/png')
    render(<DocumentPreview row={document} companyId={COMPANY} />)

    await userEvent.click(screen.getByRole('button', { name: T.preview(document.fileName) }))

    expect(await screen.findByText(T.previewTypeRefused)).toBeTruthy()
    expect(screen.queryByRole('img')).toBeNull()
    expect(createObjectURL).not.toHaveBeenCalled()
  })

  it('keeps a refused content response non-disclosing and terminal', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response(JSON.stringify({ code: 'document_access_denied' }), { status: 403, headers: { 'Content-Type': 'application/json' } })),
    )
    const document = row('image/jpeg')
    render(<DocumentPreview row={document} companyId={COMPANY} />)

    await userEvent.click(screen.getByRole('button', { name: T.preview(document.fileName) }))

    expect(await screen.findByText(T.previewRefused)).toBeTruthy()
    await waitFor(() => expect(screen.queryByRole('img')).toBeNull())
  })
})
