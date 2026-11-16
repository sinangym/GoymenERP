/**
 * B-08 / BS-10 (STK-S12) component cases — the company's package standard panel. COMPONENT/DOM EVIDENCE ONLY: `fetch` is stubbed
 * with synthetic answers; the real API and SQL are the Product unit tests and the relational acceptance tests. What these cases
 * establish: a forbidden read shows nothing, a missing table or entry says "kaynak yok" (NoSource) and guesses nothing, each
 * failed read says what failed, an off-contract answer is a failed read, the import sends the company, one Idempotency-Key and the
 * exact body (whole .xlsx in base64, source label, reason, expected version), the edit sets or removes this product's entry with a
 * reason and the expected version, a key is kept for a retry after an unknown outcome and dropped after a definite answer, the
 * input is checked before sending, and the routes and bodies are pinned to the canonical OpenAPI document.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { PackageImportPanel } from './PackageImportPanel'
import {
  PACKAGE_STANDARD_CURRENT_PATH,
  PACKAGE_STANDARD_IMPORTS_PATH,
  PACKAGE_STANDARD_PROBLEM,
  bytesToBase64,
  checkPackageFile,
  decodePackageStandardTable,
  packageStandardProductPath,
  parsePieces,
} from './packageImportApi'
import { PACKAGE_STANDARD_TEXT } from './packageImportText'

const COMPANY = 'aaaaaaaa-0000-4000-8000-000000000001'
const OTHER_COMPANY = '00000000-0000-4000-8000-000000000009'
const PRODUCT = '73000000-0000-4000-8000-000000000001'
const OTHER_PRODUCT = '73000000-0000-4000-8000-000000000002'
const SHA = 'cd'.repeat(32)
const XLSX_BYTES = new Uint8Array([0x50, 0x4b, 0x03, 0x04, 0x14, 0x00])

interface Sent {
  readonly url: string
  readonly method: string
  readonly headers: Record<string, string>
  readonly body: unknown
}

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

function install(respond: (method: string, attempt: number) => Response): Sent[] {
  const sent: Sent[] = []
  let writes = 0
  vi.stubGlobal('fetch', async (url: string, init?: RequestInit) => {
    const method = init?.method ?? 'GET'
    sent.push({
      url,
      method,
      headers: (init?.headers ?? {}) as Record<string, string>,
      body: typeof init?.body === 'string' ? JSON.parse(init.body) : null,
    })
    if (method !== 'GET') writes += 1
    return respond(method, writes)
  })
  return sent
}

const EMPTY = {
  companyId: COMPANY, version: '0', sourceKind: null, sourceLabel: null, fileName: null, fileSha256: null,
  createdAtUtc: null, createdBy: null, entries: [],
}

const ENTRY = { productId: PRODUCT, productCode: 'SNT-001', productName: 'SENTETIK URUN', piecesPerPackage: 12 }
const OTHER_ENTRY = { productId: OTHER_PRODUCT, productCode: 'SNT-002', productName: 'SENTETIK IKI', piecesPerPackage: 40 }

function imported(overrides: Record<string, unknown> = {}) {
  return {
    companyId: COMPANY, version: '2', sourceKind: 'Import', sourceLabel: 'SENTETIK LISTE', fileName: 'paket.xlsx',
    fileSha256: SHA, createdAtUtc: '2026-11-15T08:00:00Z', createdBy: 'u-1', entries: [ENTRY, OTHER_ENTRY], ...overrides,
  }
}

function edited(overrides: Record<string, unknown> = {}) {
  return imported({ version: '3', sourceKind: 'Edit', sourceLabel: null, fileName: null, fileSha256: null, ...overrides })
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

function renderPanel() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <PackageImportPanel companyId={COMPANY} productId={PRODUCT} queryNamespace="test" />
    </QueryClientProvider>,
  )
}

function xlsxFile(bytes: Uint8Array<ArrayBuffer> = XLSX_BYTES, name = 'paket.xlsx') {
  return new File([bytes], name, { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' })
}

async function openImport(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('button', { name: PACKAGE_STANDARD_TEXT.openImport }))
  return screen.getByRole('form', { name: PACKAGE_STANDARD_TEXT.formLabel })
}

async function openEdit(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('button', { name: PACKAGE_STANDARD_TEXT.openEdit }))
  return screen.getByRole('form', { name: PACKAGE_STANDARD_TEXT.editFormLabel })
}

function fileInput(): HTMLInputElement {
  const input = screen.getByLabelText(PACKAGE_STANDARD_TEXT.fileLabel)
  if (!(input instanceof HTMLInputElement)) throw new Error('file input missing')
  return input
}

describe('company package standard panel (B-08, BS-10)', () => {
  it('shows nothing without the permission (403) and says NoSource while no table exists', async () => {
    const sent = install(() => new Response(null, { status: 403 }))
    const { container } = renderPanel()
    await waitFor(() => expect(sent.length).toBe(1))
    await waitFor(() => expect(container.textContent).toBe(''))
    expect(sent[0]?.url).toBe(`/api/v1${PACKAGE_STANDARD_CURRENT_PATH}`)
    expect(sent[0]?.headers['X-Company-Id']).toBe(COMPANY)
    cleanup()
    vi.unstubAllGlobals()

    install(() => json(EMPTY))
    renderPanel()
    expect((await screen.findByTestId('package-standard-empty')).textContent).toBe(PACKAGE_STANDARD_TEXT.empty)
    expect(screen.getByTestId('package-standard-product').textContent).toBe(PACKAGE_STANDARD_TEXT.productNoSource)
    expect(screen.queryByTestId('package-standard-rows')).toBeNull()
  })

  it('shows the current table with its version, source and entries, and this product\'s value', async () => {
    install(() => json(imported()))
    renderPanel()
    const header = await screen.findByTestId('package-standard-header')
    expect(header.textContent).toContain('2')
    expect(header.textContent).toContain('SENTETIK LISTE')
    expect(header.textContent).toContain('paket.xlsx')
    const rows = screen.getByTestId('package-standard-rows')
    expect(rows.querySelectorAll('tbody tr')).toHaveLength(2)
    expect(rows.textContent).toContain('SNT-002')
    expect(screen.getByTestId('package-standard-product').textContent).toBe(`${PACKAGE_STANDARD_TEXT.productValue} 12`)
  })

  it('says NoSource for this product when the table holds no entry for it, and shows an edit version without a file', async () => {
    install(() => json(edited({ entries: [OTHER_ENTRY] })))
    renderPanel()
    expect((await screen.findByTestId('package-standard-product')).textContent).toBe(PACKAGE_STANDARD_TEXT.productNoSource)
    const header = screen.getByTestId('package-standard-header')
    expect(header.textContent).toContain(PACKAGE_STANDARD_TEXT.noValue)
    cleanup()
    vi.unstubAllGlobals()

    install(() => json(edited({ entries: [] })))
    renderPanel()
    expect(await screen.findByTestId('package-standard-no-entries')).toBeTruthy()
  })

  it('says what failed for an ended session, an unwired provider, a failed or an off-contract read', async () => {
    install(() => new Response(null, { status: 401 }))
    renderPanel()
    expect(await screen.findByTestId('package-standard-unauthenticated')).toBeTruthy()
    expect(screen.getByText(PACKAGE_STANDARD_TEXT.unauthenticated)).toBeTruthy()
    cleanup()
    vi.unstubAllGlobals()

    install(() => json({ code: PACKAGE_STANDARD_PROBLEM.providerUnavailable }, 503))
    renderPanel()
    expect(await screen.findByTestId('package-standard-unavailable')).toBeTruthy()
    expect(screen.getByText(PACKAGE_STANDARD_TEXT.unavailable)).toBeTruthy()
    cleanup()
    vi.unstubAllGlobals()

    install(() => json(imported({ companyId: OTHER_COMPANY })))
    renderPanel()
    expect(await screen.findByTestId('package-standard-failed')).toBeTruthy()
    expect(screen.getByText(PACKAGE_STANDARD_TEXT.failed)).toBeTruthy()
    expect(screen.queryByText(PACKAGE_STANDARD_TEXT.empty)).toBeNull()
  })

  it('imports the whole file with one key, the source label, reason and expected version, and reloads the table', async () => {
    const user = userEvent.setup()
    let reads = 0
    const sent = install((method) => {
      if (method === 'GET') {
        reads += 1
        return json(reads === 1 ? EMPTY : imported({ version: '1' }))
      }
      return new Response(null, { status: 204 })
    })
    renderPanel()
    await openImport(user)
    await user.upload(fileInput(), xlsxFile())
    await user.type(screen.getByRole('textbox', { name: PACKAGE_STANDARD_TEXT.sourceLabelLabel }), '  SENTETIK LISTE ')
    await user.type(screen.getByRole('textbox', { name: PACKAGE_STANDARD_TEXT.reasonLabel }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: PACKAGE_STANDARD_TEXT.submit }))

    expect(await screen.findByText(PACKAGE_STANDARD_TEXT.imported)).toBeTruthy()
    const posts = sent.filter((request) => request.method === 'POST')
    expect(posts).toHaveLength(1)
    expect(posts[0]?.url).toBe(`/api/v1${PACKAGE_STANDARD_IMPORTS_PATH}`)
    expect(posts[0]?.headers['X-Company-Id']).toBe(COMPANY)
    expect(posts[0]?.headers['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/)
    expect(posts[0]?.body).toEqual({
      expectedVersion: '0',
      sourceLabel: 'SENTETIK LISTE',
      reason: 'SENTETIK',
      fileName: 'paket.xlsx',
      contentBase64: bytesToBase64(XLSX_BYTES),
    })
    expect(await screen.findByTestId('package-standard-rows')).toBeTruthy()
  })

  it('keeps the key for a retry after an unknown outcome and says when the server replayed it', async () => {
    const user = userEvent.setup()
    const sent = install((method, attempt) => {
      if (method === 'GET') return json(EMPTY)
      return attempt === 1
        ? new Response(null, { status: 502 })
        : new Response(null, { status: 204, headers: { 'Idempotency-Replayed': 'true' } })
    })
    renderPanel()
    await openImport(user)
    await user.upload(fileInput(), xlsxFile())
    await user.type(screen.getByRole('textbox', { name: PACKAGE_STANDARD_TEXT.sourceLabelLabel }), 'SENTETIK LISTE')
    await user.type(screen.getByRole('textbox', { name: PACKAGE_STANDARD_TEXT.reasonLabel }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: PACKAGE_STANDARD_TEXT.submit }))
    expect(await screen.findByText(PACKAGE_STANDARD_TEXT.unknownOutcome)).toBeTruthy()

    await user.click(screen.getByRole('button', { name: PACKAGE_STANDARD_TEXT.retrySame }))
    expect(await screen.findByText(PACKAGE_STANDARD_TEXT.replayed)).toBeTruthy()
    const keys = sent.filter((request) => request.method === 'POST').map((request) => request.headers['Idempotency-Key'])
    expect(keys).toHaveLength(2)
    expect(keys[0]).toBe(keys[1])
  })

  it('shows a refused file with the server\'s row detail, drops the key, and reads a version conflict as a reload', async () => {
    const user = userEvent.setup()
    const sent = install((method, attempt) => {
      if (method === 'GET') return json(EMPTY)
      if (attempt === 1) {
        return json({ code: PACKAGE_STANDARD_PROBLEM.fileInvalid, detail: "Satır 2: 'X' kodlu ürün kartı bulunamadı." }, 400)
      }
      return json({ code: PACKAGE_STANDARD_PROBLEM.versionConflict }, 409)
    })
    renderPanel()
    await openImport(user)
    await user.upload(fileInput(), xlsxFile())
    await user.type(screen.getByRole('textbox', { name: PACKAGE_STANDARD_TEXT.sourceLabelLabel }), 'SENTETIK LISTE')
    await user.type(screen.getByRole('textbox', { name: PACKAGE_STANDARD_TEXT.reasonLabel }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: PACKAGE_STANDARD_TEXT.submit }))
    expect(
      await screen.findByText(`${PACKAGE_STANDARD_TEXT.fileInvalid} Satır 2: 'X' kodlu ürün kartı bulunamadı.`),
    ).toBeTruthy()
    expect(screen.queryByRole('button', { name: PACKAGE_STANDARD_TEXT.retrySame })).toBeNull()

    await user.click(screen.getByRole('button', { name: PACKAGE_STANDARD_TEXT.submit }))
    expect(await screen.findByText(PACKAGE_STANDARD_TEXT.conflict)).toBeTruthy()
    const keys = sent.filter((request) => request.method === 'POST').map((request) => request.headers['Idempotency-Key'])
    expect(new Set(keys).size).toBe(2)
  })

  it('checks the file and the mandatory source label and reason before sending anything', async () => {
    const user = userEvent.setup({ applyAccept: false })
    const sent = install(() => json(EMPTY))
    renderPanel()
    await openImport(user)
    await user.click(screen.getByRole('button', { name: PACKAGE_STANDARD_TEXT.submit }))
    expect(await screen.findByText(PACKAGE_STANDARD_TEXT.fileRequired)).toBeTruthy()

    await user.upload(fileInput(), xlsxFile(XLSX_BYTES, 'paket.csv'))
    await user.click(screen.getByRole('button', { name: PACKAGE_STANDARD_TEXT.submit }))
    expect(await screen.findByText(PACKAGE_STANDARD_TEXT.fileNotXlsx)).toBeTruthy()

    await user.upload(fileInput(), xlsxFile(new Uint8Array()))
    await user.click(screen.getByRole('button', { name: PACKAGE_STANDARD_TEXT.submit }))
    expect(await screen.findByText(PACKAGE_STANDARD_TEXT.fileEmpty)).toBeTruthy()

    await user.upload(fileInput(), xlsxFile())
    await user.type(screen.getByRole('textbox', { name: PACKAGE_STANDARD_TEXT.sourceLabelLabel }), '   ')
    await user.click(screen.getByRole('button', { name: PACKAGE_STANDARD_TEXT.submit }))
    expect(await screen.findByText(PACKAGE_STANDARD_TEXT.sourceLabelRequired)).toBeTruthy()

    await user.type(screen.getByRole('textbox', { name: PACKAGE_STANDARD_TEXT.sourceLabelLabel }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: PACKAGE_STANDARD_TEXT.submit }))
    expect(await screen.findByText(PACKAGE_STANDARD_TEXT.reasonRequired)).toBeTruthy()
    expect(sent.filter((request) => request.method === 'POST')).toHaveLength(0)
  })

  it('sets this product\'s entry with a reason and the expected version, and removes it when left empty', async () => {
    const user = userEvent.setup()
    const sent = install((method) => (method === 'GET' ? json(imported()) : new Response(null, { status: 204 })))
    renderPanel()
    await openEdit(user)
    const pieces = screen.getByRole('textbox', { name: PACKAGE_STANDARD_TEXT.piecesLabel })
    expect((pieces as HTMLInputElement).value).toBe('12')
    await user.clear(pieces)
    await user.type(pieces, '24')
    await user.type(screen.getByRole('textbox', { name: PACKAGE_STANDARD_TEXT.reasonLabel }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: PACKAGE_STANDARD_TEXT.saveEntry }))
    expect(await screen.findByText(PACKAGE_STANDARD_TEXT.saved)).toBeTruthy()

    await openEdit(user)
    await user.clear(screen.getByRole('textbox', { name: PACKAGE_STANDARD_TEXT.piecesLabel }))
    await user.type(screen.getByRole('textbox', { name: PACKAGE_STANDARD_TEXT.reasonLabel }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: PACKAGE_STANDARD_TEXT.saveEntry }))
    await waitFor(() => expect(sent.filter((request) => request.method === 'PUT')).toHaveLength(2))

    const puts = sent.filter((request) => request.method === 'PUT')
    expect(puts[0]?.url).toBe(`/api/v1${packageStandardProductPath(PRODUCT)}`)
    expect(puts[0]?.headers['X-Company-Id']).toBe(COMPANY)
    expect(puts[0]?.body).toEqual({ expectedVersion: '2', piecesPerPackage: 24, reason: 'SENTETIK' })
    expect(puts[1]?.body).toEqual({ expectedVersion: '2', piecesPerPackage: null, reason: 'SENTETIK' })
    expect(puts[0]?.headers['Idempotency-Key']).not.toBe(puts[1]?.headers['Idempotency-Key'])
  })

  it('checks the pieces and the reason before an edit and names a missing card (code-less 404)', async () => {
    const user = userEvent.setup()
    const sent = install((method) => (method === 'GET' ? json(EMPTY) : json({ title: 'Ürün bulunamadı.' }, 404)))
    renderPanel()
    await openEdit(user)
    const pieces = screen.getByRole('textbox', { name: PACKAGE_STANDARD_TEXT.piecesLabel })
    await user.type(pieces, '0')
    await user.click(screen.getByRole('button', { name: PACKAGE_STANDARD_TEXT.saveEntry }))
    expect(await screen.findByText(PACKAGE_STANDARD_TEXT.piecesInvalid)).toBeTruthy()

    await user.clear(pieces)
    await user.type(pieces, '6')
    await user.click(screen.getByRole('button', { name: PACKAGE_STANDARD_TEXT.saveEntry }))
    expect(await screen.findByText(PACKAGE_STANDARD_TEXT.reasonRequired)).toBeTruthy()
    expect(sent.filter((request) => request.method === 'PUT')).toHaveLength(0)

    await user.type(screen.getByRole('textbox', { name: PACKAGE_STANDARD_TEXT.reasonLabel }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: PACKAGE_STANDARD_TEXT.saveEntry }))
    expect(await screen.findByText(PACKAGE_STANDARD_TEXT.productMissing)).toBeTruthy()
  })

  it('decodes only the declared table shape', () => {
    expect(decodePackageStandardTable(EMPTY, COMPANY)).toEqual({ version: '0', source: null, entries: [] })
    expect(decodePackageStandardTable(imported(), COMPANY)?.entries).toHaveLength(2)
    expect(decodePackageStandardTable(edited({ entries: [] }), COMPANY)?.entries).toHaveLength(0)
    expect(decodePackageStandardTable(imported(), OTHER_COMPANY)).toBeNull()
    expect(decodePackageStandardTable({ ...EMPTY, entries: [ENTRY] }, COMPANY)).toBeNull()
    expect(decodePackageStandardTable({ ...EMPTY, sourceKind: 'Import' }, COMPANY)).toBeNull()
    expect(decodePackageStandardTable(imported({ entries: [] }), COMPANY)).toBeNull()
    expect(decodePackageStandardTable(imported({ version: 2 }), COMPANY)).toBeNull()
    expect(decodePackageStandardTable(imported({ version: '02' }), COMPANY)).toBeNull()
    expect(decodePackageStandardTable(imported({ sourceKind: 'Seed' }), COMPANY)).toBeNull()
    expect(decodePackageStandardTable(imported({ sourceLabel: ' ' }), COMPANY)).toBeNull()
    expect(decodePackageStandardTable(imported({ fileSha256: 'xyz' }), COMPANY)).toBeNull()
    expect(decodePackageStandardTable(edited({ fileName: 'paket.xlsx' }), COMPANY)).toBeNull()
    expect(decodePackageStandardTable({ ...imported(), extra: 1 }, COMPANY)).toBeNull()
    const { createdBy: _dropped, ...missing } = imported()
    expect(decodePackageStandardTable(missing, COMPANY)).toBeNull()
    expect(decodePackageStandardTable(imported({ entries: [ENTRY, ENTRY] }), COMPANY)).toBeNull()
    expect(decodePackageStandardTable(imported({ entries: [{ ...ENTRY, piecesPerPackage: 0 }] }), COMPANY)).toBeNull()
    expect(decodePackageStandardTable(imported({ entries: [{ ...ENTRY, piecesPerPackage: 1.5 }] }), COMPANY)).toBeNull()
    expect(decodePackageStandardTable(imported({ entries: [{ ...ENTRY, productId: 'x' }] }), COMPANY)).toBeNull()
    expect(decodePackageStandardTable(imported({ entries: [{ ...ENTRY, extra: 1 }] }), COMPANY)).toBeNull()
  })

  it('checks a file name, size and a typed value only to help the person', () => {
    expect(checkPackageFile({ name: 'a.XLSX', size: 1 })).toBe('ok')
    expect(checkPackageFile({ name: 'a.xls', size: 1 })).toBe('not-xlsx')
    expect(checkPackageFile({ name: 'a.xlsx', size: 0 })).toBe('empty')
    expect(checkPackageFile({ name: 'a.xlsx', size: 2_097_153 })).toBe('too-large')
    expect(checkPackageFile({ name: `${'a'.repeat(251)}.xlsx`, size: 1 })).toBe('name-too-long')
    expect(parsePieces(' 12 ')).toBe(12)
    expect(parsePieces('0')).toBeNull()
    expect(parsePieces('012')).toBeNull()
    expect(parsePieces('1.5')).toBeNull()
    expect(parsePieces('-3')).toBeNull()
    expect(parsePieces('99999999999')).toBeNull()
  })
})

describe('company package standard client ↔ canonical OpenAPI', () => {
  const nodeProcess = (globalThis as unknown as {
    readonly process: { readonly cwd: () => string; readonly getBuiltinModule: (name: string) => unknown }
  }).process
  const fs = nodeProcess.getBuiltinModule('node:fs') as { readFileSync(path: string, encoding: 'utf8'): string }
  const path = nodeProcess.getBuiltinModule('node:path') as { resolve(...segments: string[]): string }
  const document = JSON.parse(
    fs.readFileSync(path.resolve(nodeProcess.cwd(), '../../../contracts/openapi/goymen-erp-v1.json'), 'utf8'),
  ) as {
    readonly paths: Record<
      string,
      Record<
        string,
        {
          readonly requestBody?: { readonly content?: Record<string, { readonly schema?: { readonly $ref?: string } }> }
          readonly responses?: Record<
            string,
            {
              readonly 'x-erp-problem-codes'?: readonly string[]
              readonly content?: Record<string, { readonly schema?: { readonly $ref?: string } }>
            }
          >
        }
      >
    >
    readonly components: { readonly schemas: Record<string, { readonly properties?: Record<string, unknown> }> }
  }
  const currentRoute = `/api/v1${PACKAGE_STANDARD_CURRENT_PATH}`
  const importRoute = `/api/v1${PACKAGE_STANDARD_IMPORTS_PATH}`
  const editRoute = '/api/v1/product-package-standards/products/{productId}'

  function requestMembers(route: string, method: 'post' | 'put'): string[] {
    const reference = document.paths[route]?.[method]?.requestBody?.content?.['application/json']?.schema?.$ref ?? ''
    const schema = document.components.schemas[reference.replace('#/components/schemas/', '')]
    return Object.keys(schema?.properties ?? {}).sort()
  }

  it('calls the declared routes and sends exactly the declared members', () => {
    expect(document.paths[currentRoute]?.get).toBeDefined()
    expect(requestMembers(importRoute, 'post')).toEqual([
      'contentBase64', 'expectedVersion', 'fileName', 'reason', 'sourceLabel',
    ])
    expect(requestMembers(editRoute, 'put')).toEqual(['expectedVersion', 'piecesPerPackage', 'reason'])
  })

  it('decodes exactly the declared read members and knows only declared problem codes', () => {
    expect(document.paths[currentRoute]?.get?.responses?.['200']?.content?.['application/json']?.schema?.$ref).toBe(
      '#/components/schemas/ProductPackageStandardTableViewV2',
    )
    expect(Object.keys(document.components.schemas.ProductPackageStandardTableViewV2?.properties ?? {}).sort()).toEqual([
      'companyId', 'createdAtUtc', 'createdBy', 'entries', 'fileName', 'fileSha256', 'sourceKind', 'sourceLabel', 'version',
    ])
    expect(Object.keys(document.components.schemas.ProductPackageStandardEntryV2?.properties ?? {}).sort()).toEqual([
      'piecesPerPackage', 'productCode', 'productId', 'productName',
    ])
    const declared = new Set(
      [document.paths[currentRoute] ?? {}, document.paths[importRoute] ?? {}, document.paths[editRoute] ?? {}]
        .flatMap((operations) => Object.values(operations))
        .flatMap((operation) => Object.values(operation.responses ?? {}))
        .flatMap((response) => response['x-erp-problem-codes'] ?? []),
    )
    for (const code of Object.values(PACKAGE_STANDARD_PROBLEM)) expect(declared.has(code), code).toBe(true)
  })
})
