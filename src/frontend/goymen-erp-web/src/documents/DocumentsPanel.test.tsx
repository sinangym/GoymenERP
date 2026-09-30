/**
 * AT-065 / AT-057 document panel, component evidence (NOT_ENV): the list appears only when the server answers it, the
 * upload is offered only under `canUpload`, one Idempotency-Key per chosen file survives a lost answer, the duplicate
 * warning names the earlier documents, refusals are explained by their stable codes, and only a clean version is
 * downloadable — as an attachment under its own name.
 */

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { DocumentsPanel } from './DocumentsPanel'
import { OWNER_KIND, decodeDocumentList, decodeDocumentUpload } from './contract'
import { DOCUMENTS_TEXT as T, formatSize, refusalText } from './documentsText'

const COMPANY = 'aaaaaaaa-0000-4000-8000-000000000001'
const OTHER_COMPANY = 'bbbbbbbb-0000-4000-8000-000000000002'
const PARTY = '11111111-1111-4111-8111-111111111111'
const SHA = 'a'.repeat(64)
const LIMITS = { maximumBytes: 20971520, extensions: ['.pdf', '.png', '.jpg', '.jpeg'], contentTypes: ['application/pdf', 'image/png', 'image/jpeg'] }

function row(over: Record<string, unknown> = {}) {
  return {
    id: 'd0000000-0000-4000-8000-000000000001',
    companyId: COMPANY,
    ownerKind: OWNER_KIND.counterpartyParty,
    ownerId: PARTY,
    versionNo: 1,
    fileName: 'Sözleşme.pdf',
    contentType: 'application/pdf',
    sizeBytes: 2048,
    sha256: SHA,
    scanState: 'Clean',
    downloadable: true,
    uploadedBy: 'company-a-user',
    uploadedAtUtc: '2026-09-30T09:00:00+00:00',
    ...over,
  }
}

function json(body: unknown, status = 200, headers: Record<string, string> = {}): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } })
}

interface Call {
  readonly url: string
  readonly method: string
  readonly headers: Record<string, string>
  readonly body: BodyInit | null | undefined
}

function installFetch(handler: (call: Call) => Response | Promise<Response>) {
  const calls: Call[] = []
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const call = {
        url: typeof input === 'string' ? input : input.toString(),
        method: init?.method ?? 'GET',
        headers: (init?.headers ?? {}) as Record<string, string>,
        body: init?.body,
      }
      calls.push(call)
      return handler(call)
    }),
  )
  return calls
}

function renderPanel() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
  const view = render(
    <QueryClientProvider client={queryClient}>
      <DocumentsPanel ownerKind={OWNER_KIND.counterpartyParty} ownerId={PARTY} companyId={COMPANY} namespace="ns" entryGeneration={1} />
    </QueryClientProvider>,
  )
  return { queryClient, ...view }
}

function pdf(name = 'Sözleşme.pdf', size = 2048): File {
  return new File([new Uint8Array(size)], name, { type: 'application/pdf' })
}

beforeEach(() => {
  vi.stubGlobal('crypto', { randomUUID: vi.fn(() => `key-${Math.random().toString(36).slice(2)}`) })
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('AT-065 document panel', () => {
  it('lists the documents newest first and offers a download only for a clean version', async () => {
    installFetch(() =>
      json({
        items: [row(), row({ id: 'd0000000-0000-4000-8000-000000000002', fileName: 'Tarama.png', contentType: 'image/png', scanState: 'NotScanned', downloadable: false })],
        canUpload: false,
        limits: LIMITS,
      }),
    )
    renderPanel()

    const table = await screen.findByRole('table', { name: T.caption })
    const rows = within(table).getAllByRole('row').slice(1)
    expect(rows.map((r) => within(r).getByRole('rowheader').textContent)).toEqual(['Sözleşme.pdf', 'Tarama.png'])
    expect(within(rows[0]).getByRole('button', { name: T.download('Sözleşme.pdf') })).toBeTruthy()
    expect(within(rows[1]).queryByRole('button')).toBeNull()
    expect(within(rows[1]).getByText(T.scanPending)).toBeTruthy()
    expect(within(rows[0]).getByText(formatSize(2048))).toBeTruthy()
    // No upload without the server's canUpload.
    expect(screen.queryByLabelText(T.fileLabel)).toBeNull()
  })

  it.each([403, 404])('renders nothing at all when the list answers %i', async (status) => {
    const calls = installFetch(() => json({ title: 'x' }, status))
    const { container } = renderPanel()
    await waitFor(() => expect(calls).toHaveLength(1))
    await waitFor(() => expect(container.innerHTML).toBe(''))
  })

  it('reads the list under the active company with the owner as query, and refuses another company\'s row as malformed', async () => {
    const calls = installFetch(() => json({ items: [row({ companyId: OTHER_COMPANY })], canUpload: true, limits: LIMITS }))
    renderPanel()

    expect(await screen.findByText(T.readFailed)).toBeTruthy()
    expect(calls[0].url).toBe(`/api/v1/documents?ownerKind=counterparty.party&ownerId=${PARTY}`)
    expect(calls[0].headers['X-Company-Id']).toBe(COMPANY)
    expect(screen.queryByRole('table')).toBeNull()
  })

  it('uploads the chosen file once with one key and the company, then shows it after the re-read', async () => {
    let listReads = 0
    const calls = installFetch((call) => {
      if (call.method === 'POST') return json({ document: row({ sizeBytes: 2048 }), duplicates: [], isReplay: false, originalCorrelationId: 'c-1' }, 201)
      listReads++
      return json({ items: listReads === 1 ? [] : [row()], canUpload: true, limits: LIMITS })
    })
    renderPanel()

    expect(await screen.findByText(T.none)).toBeTruthy()
    await userEvent.upload(screen.getByLabelText(T.fileLabel), pdf())
    await userEvent.click(screen.getByRole('button', { name: T.upload }))

    expect(await screen.findByText(T.uploaded('Sözleşme.pdf'))).toBeTruthy()
    const post = calls.filter((c) => c.method === 'POST')
    expect(post).toHaveLength(1)
    expect(post[0].url).toBe('/api/v1/documents')
    expect(post[0].headers['X-Company-Id']).toBe(COMPANY)
    expect(post[0].headers['Idempotency-Key']).toMatch(/^key-/)
    const form = post[0].body as FormData
    expect(form.get('ownerKind')).toBe('counterparty.party')
    expect(form.get('ownerId')).toBe(PARTY)
    expect((form.get('file') as File).name).toBe('Sözleşme.pdf')
    // The list is read again and now holds the document.
    expect(await screen.findByRole('rowheader', { name: 'Sözleşme.pdf' })).toBeTruthy()
  })

  it('warns with the earlier documents when the same file was already uploaded (AT-057)', async () => {
    installFetch((call) =>
      call.method === 'POST'
        ? json(
            {
              document: row({ id: 'd0000000-0000-4000-8000-000000000009', fileName: 'Tekrar.pdf' }),
              duplicates: [{ documentId: row().id, fileName: 'Sözleşme.pdf', ownerKind: 'counterparty.party', ownerId: PARTY, uploadedAtUtc: '2026-09-29T08:00:00Z' }],
              isReplay: false,
              originalCorrelationId: 'c-2',
            },
            201,
          )
        : json({ items: [row()], canUpload: true, limits: LIMITS }),
    )
    renderPanel()

    await userEvent.upload(await screen.findByLabelText(T.fileLabel), pdf('Tekrar.pdf'))
    await userEvent.click(screen.getByRole('button', { name: T.upload }))

    const warning = await screen.findByText(T.duplicateTitle)
    const block = warning.closest('[data-documents-state="duplicate"]') as HTMLElement
    expect(within(block).getByText(/^Sözleşme\.pdf — /)).toBeTruthy()
    expect(block.querySelector('[data-duplicate-id]')?.getAttribute('data-duplicate-id')).toBe(row().id)
  })

  it('keeps the same key and file for a retry after a lost answer and reports the replay', async () => {
    let posts = 0
    const calls = installFetch((call) => {
      if (call.method !== 'POST') return json({ items: [], canUpload: true, limits: LIMITS })
      posts++
      if (posts === 1) throw new TypeError('network down')
      return json({ document: row(), duplicates: [], isReplay: true, originalCorrelationId: 'c-1' }, 201, { 'Idempotency-Replayed': 'true' })
    })
    renderPanel()

    await userEvent.upload(await screen.findByLabelText(T.fileLabel), pdf())
    await userEvent.click(screen.getByRole('button', { name: T.upload }))
    expect(await screen.findByText(T.unknownTitle)).toBeTruthy()
    // The file is pinned while the outcome is unknown.
    expect((screen.getByLabelText(T.fileLabel) as HTMLInputElement).disabled).toBe(true)

    await userEvent.click(screen.getByRole('button', { name: T.retry }))
    expect(await screen.findByText(T.replayed)).toBeTruthy()
    const keys = calls.filter((c) => c.method === 'POST').map((c) => c.headers['Idempotency-Key'])
    expect(keys).toHaveLength(2)
    expect(keys[0]).toBe(keys[1])
  })

  it('explains a refusal by its code and sends nothing for a type the list does not allow', async () => {
    const calls = installFetch((call) =>
      call.method === 'POST'
        ? json({ code: 'document_content_mismatch', correlationId: 'c' }, 422)
        : json({ items: [], canUpload: true, limits: LIMITS }),
    )
    renderPanel()

    const input = await screen.findByLabelText(T.fileLabel)
    await userEvent.upload(input, new File(['MZ'], 'kurulum.exe', { type: 'application/octet-stream' }), { applyAccept: false })
    expect(await screen.findByText(T.typeLocal)).toBeTruthy()
    expect((screen.getByRole('button', { name: T.upload }) as HTMLButtonElement).disabled).toBe(true)

    await userEvent.upload(input, pdf('fatura.pdf'))
    await userEvent.click(screen.getByRole('button', { name: T.upload }))
    expect(await screen.findByText(refusalText(422, 'document_content_mismatch'))).toBeTruthy()
    expect(calls.filter((c) => c.method === 'POST')).toHaveLength(1)
  })

  it('saves a download as an attachment under its own name and explains a version that was not scanned', async () => {
    const createObjectURL = vi.fn(() => 'blob:doc')
    const revokeObjectURL = vi.fn()
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL, revokeObjectURL }))
    const clicked: string[] = []
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      clicked.push(`${this.download}|${this.href}`)
    })
    let downloads = 0
    installFetch((call) => {
      if (call.url.endsWith('/content')) {
        downloads++
        return downloads === 1
          ? new Response(new Uint8Array([37, 80, 68, 70]), { status: 200, headers: { 'Content-Type': 'application/pdf' } })
          : json({ code: 'document_not_scanned', correlationId: 'c' }, 409)
      }
      return json({ items: [row()], canUpload: false, limits: LIMITS })
    })
    renderPanel()

    await userEvent.click(await screen.findByRole('button', { name: T.download('Sözleşme.pdf') }))
    await waitFor(() => expect(clicked).toEqual(['Sözleşme.pdf|blob:doc']))
    expect(createObjectURL).toHaveBeenCalledTimes(1)

    await userEvent.click(screen.getByRole('button', { name: T.download('Sözleşme.pdf') }))
    expect(await screen.findByText(T.notScanned)).toBeTruthy()
  })
})

describe('document contract decoders', () => {
  it('refuses a row that claims a download for a version that was not scanned', () => {
    expect(decodeDocumentList({ items: [row({ scanState: 'NotScanned', downloadable: true })], canUpload: false, limits: LIMITS }, COMPANY,
      OWNER_KIND.counterpartyParty, PARTY)).toBeNull()
    expect(decodeDocumentList({ items: [row({ ownerId: OTHER_COMPANY })], canUpload: false, limits: LIMITS }, COMPANY, OWNER_KIND.counterpartyParty,
      PARTY)).toBeNull()
    expect(decodeDocumentList({ items: [row()], canUpload: 'yes', limits: LIMITS }, COMPANY, OWNER_KIND.counterpartyParty, PARTY)).toBeNull()
  })

  it('accepts an upload answer only for the company, owner and size that were sent', () => {
    const answer = { document: row(), duplicates: [], isReplay: false, originalCorrelationId: 'c' }
    const sent = { companyId: COMPANY, ownerKind: OWNER_KIND.counterpartyParty, ownerId: PARTY, sizeBytes: 2048 }
    expect(decodeDocumentUpload(answer, sent)?.document.id).toBe(row().id)
    expect(decodeDocumentUpload(answer, { ...sent, sizeBytes: 2047 })).toBeNull()
    expect(decodeDocumentUpload(answer, { ...sent, companyId: OTHER_COMPANY })).toBeNull()
    expect(decodeDocumentUpload({ ...answer, duplicates: [{ documentId: row().id, fileName: 'x', ownerKind: 'counterparty.party', ownerId: PARTY,
      uploadedAtUtc: '2026-09-30T09:00:00Z' }] }, sent)).toBeNull()
  })
})
