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
import { DOCUMENTS_TEXT as T, formatSize, refusalText, versionRefusalText } from './documentsText'

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
    expect(within(rows[0]).queryByRole('button', { name: T.preview('Sözleşme.pdf') })).toBeNull()
    // An unscanned latest version offers no download or preview; its history is still reachable.
    expect(within(rows[1]).getAllByRole('button').map((b) => b.getAttribute('aria-label'))).toEqual([T.versionsShow('Tarama.png')])
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

/**
 * DOC-VERSION-UI-01 (#677 consumer), component evidence (NOT_ENV): a row opens its own history newest first and pages it
 * with the server's cursor; a chosen clean version downloads its own bytes; a new version is one frozen attempt against
 * the document's versions route, never the upload route; refusals and an unknown outcome keep their meaning; and a
 * change of company, owner or entry generation closes the history and drops what arrives late.
 */
describe('document versions (DOC-VERSION-UI-01)', () => {
  const DOC = row().id
  const HISTORY = `/api/v1/documents/${DOC}/versions?pageSize=50`
  const APPEND = `/api/v1/documents/${DOC}/versions`

  function version(versionNo: number, over: Record<string, unknown> = {}) {
    return row({ versionNo, fileName: `Sözleşme-v${versionNo}.pdf`, sizeBytes: 1000 + versionNo, ...over })
  }

  function history(versionNos: readonly number[], nextCursor: string | null = null) {
    return { documentId: DOC, versions: versionNos.map((n) => version(n)), nextCursor }
  }

  function range(from: number, to: number): number[] {
    return Array.from({ length: from - to + 1 }, (_, index) => from - index)
  }

  const isHistory = (call: Call) => call.method === 'GET' && call.url.startsWith(`${APPEND}?`)
  const isAppend = (call: Call) => call.method === 'POST' && call.url === APPEND
  const list = (canUpload: boolean, versionNo = 1) => json({ items: [row({ versionNo })], canUpload, limits: LIMITS })

  async function openVersions() {
    await userEvent.click(await screen.findByRole('button', { name: T.versionsShow('Sözleşme.pdf') }))
    return screen.getByRole('region', { name: T.versionsHeading('Sözleşme.pdf') })
  }

  function versionRows(region: HTMLElement): string[] {
    return [...region.querySelectorAll('tr[data-version-no]')].map((r) => r.getAttribute('data-version-no') ?? '')
  }

  it('opens one document history newest first under the active company and offers a download only for a clean version', async () => {
    const calls = installFetch((call) =>
      isHistory(call)
        ? json({ documentId: DOC, versions: [version(2, { scanState: 'NotScanned', downloadable: false }), version(1)], nextCursor: null })
        : list(false, 2),
    )
    renderPanel()

    const region = await openVersions()
    const table = await within(region).findByRole('table', { name: T.versionsCaption('Sözleşme.pdf') })
    expect(versionRows(region)).toEqual(['2', '1'])
    expect(within(table).getAllByRole('rowheader').map((h) => h.textContent)).toEqual([T.versionNo(2), T.versionNo(1)])
    const read = calls.filter(isHistory)
    expect(read).toHaveLength(1)
    expect(read[0].url).toBe(HISTORY)
    expect(read[0].headers['X-Company-Id']).toBe(COMPANY)
    // The unscanned version is listed but cannot be downloaded; the clean one can.
    expect(within(region).queryByRole('button', { name: T.versionDownload('Sözleşme-v2.pdf', 2) })).toBeNull()
    expect(within(region).getByRole('button', { name: T.versionDownload('Sözleşme-v1.pdf', 1) })).toBeTruthy()
    expect(within(region).getByText(T.versionsEnd)).toBeTruthy()
    // Every cell names its column for the phone card layout.
    expect([...region.querySelectorAll('tr[data-version-no="1"] td')].map((cell) => cell.getAttribute('data-label'))).toEqual([
      T.columnFile, T.columnType, T.columnSize, T.columnUploaded, T.columnState, T.columnAction,
    ])
    // No append without the server's canUpload; the toggle states that it is open and closes it again.
    expect(within(region).queryByLabelText(T.appendFileLabel)).toBeNull()
    const toggle = screen.getByRole('button', { name: T.versionsHide('Sözleşme.pdf') })
    expect(toggle.getAttribute('aria-expanded')).toBe('true')
    expect(toggle.getAttribute('aria-controls')).toBe(region.id)
    await userEvent.click(within(region).getByRole('button', { name: T.versionsClose }))
    expect(screen.queryByRole('region', { name: T.versionsHeading('Sözleşme.pdf') })).toBeNull()
    expect(document.activeElement).toBe(screen.getByRole('button', { name: T.versionsShow('Sözleşme.pdf') }))
  })

  it('pages older versions with the server cursor, keeps the shown rows when a continuation fails and retries the same cursor', async () => {
    let continuations = 0
    const calls = installFetch((call) => {
      if (!isHistory(call)) return list(false, 60)
      if (call.url === HISTORY) return json(history(range(60, 11), '11'))
      continuations++
      return continuations === 1 ? json({ title: 'x' }, 503) : json(history(range(10, 1)))
    })
    renderPanel()

    const region = await openVersions()
    await userEvent.click(await within(region).findByRole('button', { name: T.versionsMore }))
    expect(await within(region).findByText(T.versionsMoreFailed)).toBeTruthy()
    expect(versionRows(region)).toEqual(range(60, 11).map(String))

    await userEvent.click(within(region).getByRole('button', { name: T.versionsMoreAgain }))
    await waitFor(() => expect(versionRows(region)).toEqual(range(60, 1).map(String)))
    expect(within(region).getByText(T.versionsEnd)).toBeTruthy()
    expect(within(region).queryByText(T.versionsMoreFailed)).toBeNull()
    expect(calls.filter(isHistory).map((c) => c.url)).toEqual([HISTORY, `${HISTORY}&cursor=11`, `${HISTORY}&cursor=11`])
  })

  it.each([
    ['a continuation that overlaps the shown rows', history(range(11, 2))],
    ['a first page of another document', { ...history([2, 1]), documentId: OTHER_COMPANY }],
  ])('refuses %s as a failed read', async (_name, answer) => {
    installFetch((call) => {
      if (!isHistory(call)) return list(false, 60)
      if (call.url !== HISTORY) return json(answer)
      return 'documentId' in answer && answer.documentId !== DOC ? json(answer) : json(history(range(60, 11), '11'))
    })
    renderPanel()

    const region = await openVersions()
    if ('documentId' in answer && answer.documentId !== DOC) {
      expect(await within(region).findByText(T.versionsFailed)).toBeTruthy()
      expect(versionRows(region)).toEqual([])
      return
    }
    await userEvent.click(await within(region).findByRole('button', { name: T.versionsMore }))
    expect(await within(region).findByText(T.versionsMoreFailed)).toBeTruthy()
    expect(versionRows(region)).toHaveLength(50)
  })

  it.each([403, 404])('shows no history and no append when the history answers %i', async (status) => {
    installFetch((call) => (isHistory(call) ? json({ title: 'x' }, status) : list(true)))
    renderPanel()

    const region = await openVersions()
    expect(await within(region).findByText(T.versionsGone)).toBeTruthy()
    expect(versionRows(region)).toEqual([])
    expect(within(region).queryByLabelText(T.appendFileLabel)).toBeNull()
    expect(within(region).queryByRole('button', { name: T.versionsAgain })).toBeNull()
  })

  it('reads a failed first page again on request', async () => {
    let reads = 0
    installFetch((call) => {
      if (!isHistory(call)) return list(false)
      reads++
      if (reads === 1) throw new TypeError('network down')
      return json(history([1]))
    })
    renderPanel()

    const region = await openVersions()
    await userEvent.click(await within(region).findByRole('button', { name: T.versionsAgain }))
    await waitFor(() => expect(versionRows(region)).toEqual(['1']))
  })

  it('adds a new version to the same document on the versions route, then shows history 2,1 and the re-read list', async () => {
    let stored = false
    const calls = installFetch((call) => {
      if (isAppend(call)) {
        stored = true
        return json({ document: version(2, { fileName: 'Sözleşme-yeni.pdf', sizeBytes: 4096 }), isReplay: false, originalCorrelationId: 'c-9' }, 201)
      }
      if (isHistory(call)) return json(history(stored ? [2, 1] : [1]))
      return list(true, stored ? 2 : 1)
    })
    renderPanel()

    const region = await openVersions()
    const input = await within(region).findByLabelText(T.appendFileLabel)
    await waitFor(() => expect((input as HTMLInputElement).disabled).toBe(false))
    expect(within(region).getByText(new RegExp(T.appendNote(2).replace(/[.]/g, '\\.')))).toBeTruthy()
    await userEvent.upload(input, pdf('Sözleşme-yeni.pdf', 4096))
    await userEvent.click(within(region).getByRole('button', { name: T.append }))

    expect(await within(region).findByText(T.appended('Sözleşme-yeni.pdf', 2))).toBeTruthy()
    const posts = calls.filter((c) => c.method === 'POST')
    // One request, to this document's versions — never the upload route, so no new document is created.
    expect(posts.map((c) => c.url)).toEqual([APPEND])
    expect(posts[0].headers['X-Company-Id']).toBe(COMPANY)
    expect(posts[0].headers['Idempotency-Key']).toMatch(/^key-/)
    const form = posts[0].body as FormData
    expect([...form.keys()].sort()).toEqual(['expectedCurrentVersion', 'file'])
    expect(form.get('expectedCurrentVersion')).toBe('1')
    expect((form.get('file') as File).name).toBe('Sözleşme-yeni.pdf')
    await waitFor(() => expect(versionRows(region)).toEqual(['2', '1']))
    // The owner's list is read again as well.
    await waitFor(() => expect(calls.filter((c) => c.url.startsWith('/api/v1/documents?')).length).toBeGreaterThanOrEqual(2))
  })

  it.each([
    ['a lost answer', () => { throw new TypeError('network down') }],
    ['a 503', () => json({ title: 'x' }, 503)],
    ['a 201 that is not the next version', () => json({ document: row({ versionNo: 3, sizeBytes: 2048 }), isReplay: false, originalCorrelationId: 'c' }, 201)],
    ['a 201 of another company', () => json({ document: row({ versionNo: 2, sizeBytes: 2048, companyId: OTHER_COMPANY }), isReplay: false, originalCorrelationId: 'c' }, 201)],
    ['a replay flag without its header', () => json({ document: row({ versionNo: 2, sizeBytes: 2048 }), isReplay: true, originalCorrelationId: 'c' }, 201)],
  ] as const)('keeps the frozen attempt after %s and repeats it unchanged, reporting the replay', async (_name, first) => {
    let appends = 0
    const calls = installFetch((call) => {
      if (isAppend(call)) {
        appends++
        if (appends === 1) return first()
        return json({ document: row({ versionNo: 2, sizeBytes: 2048 }), isReplay: true, originalCorrelationId: 'c-1' }, 201, { 'Idempotency-Replayed': 'true' })
      }
      // The history moves on while the outcome is unknown; the retry must not follow it.
      if (isHistory(call)) return json(history(appends > 0 ? [2, 1] : [1]))
      return list(true)
    })
    renderPanel()

    const region = await openVersions()
    const input = (await within(region).findByLabelText(T.appendFileLabel)) as HTMLInputElement
    await waitFor(() => expect(input.disabled).toBe(false))
    await userEvent.upload(input, pdf('Sözleşme.pdf', 2048))
    await userEvent.click(within(region).getByRole('button', { name: T.append }))
    expect(await within(region).findByText(T.appendUnknownTitle)).toBeTruthy()
    // Pinned: no other file can replace the attempt while its outcome is unknown.
    expect(input.disabled).toBe(true)

    await userEvent.click(within(region).getByRole('button', { name: T.appendRetry }))
    expect(await within(region).findByText(T.appendReplayed)).toBeTruthy()
    const posts = calls.filter(isAppend)
    expect(posts).toHaveLength(2)
    expect(posts[1].headers['Idempotency-Key']).toBe(posts[0].headers['Idempotency-Key'])
    expect(posts[1].headers['X-Company-Id']).toBe(COMPANY)
    expect((posts[1].body as FormData).get('expectedCurrentVersion')).toBe('1')
    const files = posts.map((c) => (c.body as FormData).get('file') as File)
    expect(files.map((file) => `${file.name}|${file.size}`)).toEqual(['Sözleşme.pdf|2048', 'Sözleşme.pdf|2048'])
  })

  it.each([
    [409, 'concurrent_change', true],
    [409, 'same_key_payload_mismatch', false],
    [422, 'document_content_rejected', false],
    [422, 'document_content_mismatch', false],
    [403, null, false],
    [404, null, false],
  ] as const)('explains a %i %s refusal, adds no row and re-reads the history only when it was stale', async (status, code, rereads) => {
    const calls = installFetch((call) => {
      if (isAppend(call)) return json(code === null ? { title: 'x' } : { code, correlationId: 'c' }, status)
      return isHistory(call) ? json(history([1])) : list(true)
    })
    renderPanel()

    const region = await openVersions()
    const input = (await within(region).findByLabelText(T.appendFileLabel)) as HTMLInputElement
    await waitFor(() => expect(input.disabled).toBe(false))
    await userEvent.upload(input, pdf())
    await userEvent.click(within(region).getByRole('button', { name: T.append }))

    expect(await within(region).findByText(versionRefusalText(status, code))).toBeTruthy()
    await waitFor(() => expect(versionRows(region)).toEqual(['1']))
    expect(calls.filter(isAppend)).toHaveLength(1)
    expect(calls.filter(isHistory)).toHaveLength(rereads ? 2 : 1)
    // A refusal is definite: nothing is pinned and nothing can be re-sent without choosing a file again.
    expect((within(region).getByRole('button', { name: T.append }) as HTMLButtonElement).disabled).toBe(true)
    expect(within(region).queryByRole('button', { name: T.appendRetry })).toBeNull()
  })

  it('sends nothing for a file the limits do not allow', async () => {
    const calls = installFetch((call) => (isHistory(call) ? json(history([1])) : list(true)))
    renderPanel()

    const region = await openVersions()
    const input = (await within(region).findByLabelText(T.appendFileLabel)) as HTMLInputElement
    await waitFor(() => expect(input.disabled).toBe(false))
    await userEvent.upload(input, new File(['MZ'], 'kurulum.exe', { type: 'application/octet-stream' }), { applyAccept: false })
    expect(await within(region).findByText(T.typeLocal)).toBeTruthy()
    expect((within(region).getByRole('button', { name: T.append }) as HTMLButtonElement).disabled).toBe(true)
    expect(calls.filter((c) => c.method === 'POST')).toHaveLength(0)
  })

  it('downloads each chosen version from its own content route under its own name, and saves nothing that is not that version', async () => {
    const createObjectURL = vi.fn((blob: Blob) => `blob:${blob.size}`)
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL, revokeObjectURL: vi.fn() }))
    const clicked: string[] = []
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      clicked.push(`${this.download}|${this.href}`)
    })
    let third = 0
    const calls = installFetch((call) => {
      if (call.url === `${APPEND}/1/content`) return new Response(new Uint8Array(1001), { status: 200 })
      if (call.url === `${APPEND}/2/content`) return new Response(new Uint8Array(1002), { status: 200 })
      if (call.url === `${APPEND}/3/content`) {
        third++
        // First the wrong length for version 3, then a scan refusal.
        return third === 1 ? new Response(new Uint8Array(7), { status: 200 }) : json({ code: 'document_not_scanned', correlationId: 'c' }, 409)
      }
      return isHistory(call) ? json(history([3, 2, 1])) : list(false, 3)
    })
    renderPanel()

    const region = await openVersions()
    await userEvent.click(await within(region).findByRole('button', { name: T.versionDownload('Sözleşme-v1.pdf', 1) }))
    await waitFor(() => expect(clicked).toEqual(['Sözleşme-v1.pdf|blob:1001']))
    await userEvent.click(within(region).getByRole('button', { name: T.versionDownload('Sözleşme-v2.pdf', 2) }))
    await waitFor(() => expect(clicked).toEqual(['Sözleşme-v1.pdf|blob:1001', 'Sözleşme-v2.pdf|blob:1002']))

    await userEvent.click(within(region).getByRole('button', { name: T.versionDownload('Sözleşme-v3.pdf', 3) }))
    expect(await within(region).findByText(T.versionDownloadRefused)).toBeTruthy()
    await userEvent.click(within(region).getByRole('button', { name: T.versionDownload('Sözleşme-v3.pdf', 3) }))
    expect(await within(region).findByText(T.versionNotScanned)).toBeTruthy()
    expect(clicked).toHaveLength(2)
    const reads = calls.filter((c) => c.url.endsWith('/content'))
    expect(reads.every((c) => c.headers['X-Company-Id'] === COMPANY)).toBe(true)
    // The latest-version route of the document is not what a chosen version reads.
    expect(reads.some((c) => c.url === `/api/v1/documents/${DOC}/content`)).toBe(false)
  })

  it('closes the history when the company and entry generation change and drops the answers that arrive late', async () => {
    const createObjectURL = vi.fn(() => 'blob:late')
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL, revokeObjectURL: vi.fn() }))
    let releaseContent: (response: Response) => void = () => undefined
    let releaseAppend: (response: Response) => void = () => undefined
    const calls = installFetch((call) => {
      const company = call.headers['X-Company-Id']
      if (call.url.endsWith('/content')) return new Promise<Response>((resolve) => (releaseContent = resolve))
      if (isAppend(call)) return new Promise<Response>((resolve) => (releaseAppend = resolve))
      if (isHistory(call)) return json(history([1]))
      return json({ items: [row({ companyId: company })], canUpload: true, limits: LIMITS })
    })
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
    const panel = (companyId: string, generation: number) => (
      <QueryClientProvider client={queryClient}>
        <DocumentsPanel ownerKind={OWNER_KIND.counterpartyParty} ownerId={PARTY} companyId={companyId} namespace={`ns-${companyId}`} entryGeneration={generation} />
      </QueryClientProvider>
    )
    const view = render(panel(COMPANY, 1))

    const region = await openVersions()
    const input = (await within(region).findByLabelText(T.appendFileLabel)) as HTMLInputElement
    await waitFor(() => expect(input.disabled).toBe(false))
    await userEvent.click(await within(region).findByRole('button', { name: T.versionDownload('Sözleşme-v1.pdf', 1) }))
    await userEvent.upload(input, pdf())
    await userEvent.click(within(region).getByRole('button', { name: T.append }))
    await waitFor(() => expect(calls.filter(isAppend)).toHaveLength(1))

    view.rerender(panel(OTHER_COMPANY, 2))
    await screen.findByRole('button', { name: T.versionsShow('Sözleşme.pdf') })
    // The other company's panel starts closed: no history, no version row, no append state of the first company.
    expect(document.querySelector('[data-document-versions]')).toBeNull()
    expect(calls.filter(isHistory).every((c) => c.headers['X-Company-Id'] === COMPANY)).toBe(true)

    releaseAppend(json({ document: row({ versionNo: 2, sizeBytes: 2048 }), isReplay: false, originalCorrelationId: 'c' }, 201))
    releaseContent(new Response(new Uint8Array(1001), { status: 200 }))
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(document.querySelector('[data-version-state]')).toBeNull()
    expect(document.querySelector('[data-document-versions]')).toBeNull()
    expect(createObjectURL).not.toHaveBeenCalled()

    // Opened again under the new company, the history is read under that company only.
    await openVersions()
    await waitFor(() => expect(calls.filter(isHistory).at(-1)?.headers['X-Company-Id']).toBe(OTHER_COMPANY))
  })

  it('does not save a version download that answers after the history was closed', async () => {
    const createObjectURL = vi.fn(() => 'blob:late')
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL, revokeObjectURL: vi.fn() }))
    let release: (response: Response) => void = () => undefined
    installFetch((call) => {
      if (call.url.endsWith('/content')) return new Promise<Response>((resolve) => (release = resolve))
      return isHistory(call) ? json(history([1])) : list(false)
    })
    renderPanel()

    const region = await openVersions()
    await userEvent.click(await within(region).findByRole('button', { name: T.versionDownload('Sözleşme-v1.pdf', 1) }))
    await userEvent.click(within(region).getByRole('button', { name: T.versionsClose }))
    release(new Response(new Uint8Array(1001), { status: 200 }))
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(createObjectURL).not.toHaveBeenCalled()
  })
})
