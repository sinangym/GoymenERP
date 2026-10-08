import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { DocumentPreview } from './DocumentPreview'
import { OWNER_KIND, type DocumentRow } from './contract'
import { DOCUMENTS_TEXT as T } from './documentsText'

const COMPANY = 'aaaaaaaa-0000-4000-8000-000000000001'

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

    await userEvent.click(screen.getByRole('button', { name: T.preview(document.fileName) }))

    expect(await screen.findByRole('dialog', { name: T.previewTitle(document.fileName) })).toBeTruthy()
    expect(screen.getByRole('img', { name: T.previewAlt(document.fileName) }).getAttribute('src')).toBe('blob:preview')
    expect(fetchMock).toHaveBeenCalledWith(`/api/v1/documents/${document.id}/content`, expect.any(Object))
    await userEvent.click(screen.getByRole('button', { name: T.closePreview }))
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:preview')
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
