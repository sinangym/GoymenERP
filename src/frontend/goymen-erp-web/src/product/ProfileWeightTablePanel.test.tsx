/**
 * URN-D13…D16 (STK-S37R) component cases — the company's kg/m table panel. COMPONENT/DOM EVIDENCE ONLY: `fetch` is stubbed
 * with synthetic answers; the real API and SQL are the Product unit tests and the relational acceptance tests. What these cases
 * establish: a forbidden read shows nothing, an empty table says so, each failed read says what failed, an off-contract answer
 * is a failed read, the import sends the company, one Idempotency-Key and the exact body (whole file in base64, source label,
 * reason, expected version), keeps the key for a retry after an unknown outcome, reads the vault's fail-closed 503 as a refusal,
 * checks the file and the mandatory label before sending, and the routes and bodies are pinned to the canonical OpenAPI document.
 * KLN-D87 (STK-SC33-b): a structural profile row (series and size, no measure) is shown and decoded; its shape is fail-closed.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ProfileWeightTablePanel } from './ProfileWeightTablePanel'
import {
  PROFILE_WEIGHT_CURRENT_PATH,
  PROFILE_WEIGHT_PROBLEM,
  PROFILE_WEIGHT_TABLE_PATH,
  bytesToBase64,
  checkFile,
  decodeProfileWeightTable,
} from './profileWeightApi'
import { PROFILE_WEIGHT_TEXT } from './profileWeightText'

const COMPANY = 'aaaaaaaa-0000-4000-8000-000000000001'
const OTHER_COMPANY = '00000000-0000-4000-8000-000000000009'
const DOCUMENT = '72000000-0000-4000-8000-000000000001'
const SHA = 'ab'.repeat(32)
const CSV = 'sectionKind;widthMm;heightMm;outerDiameterMm;wallThicknessMm;kgPerMetre\nProfile;40;20;;2;1.75\n'

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
  companyId: COMPANY, version: '0', sourceLabel: null, fileName: null, fileSha256: null, documentId: null,
  importedAtUtc: null, importedBy: null, rows: [],
}

const PROFILE_ROW = {
  sectionKind: 'Profile', widthMm: 40, heightMm: 20, outerDiameterMm: null, wallThicknessMm: 2, kgPerMetre: 1.75,
  profileSeries: null, profileSize: null,
}
const TUBE_ROW = {
  sectionKind: 'RoundTube', widthMm: null, heightMm: null, outerDiameterMm: 48.3, wallThicknessMm: 2.6, kgPerMetre: 2.93,
  profileSeries: null, profileSize: null,
}
const STRUCTURAL_ROW = {
  sectionKind: 'StructuralProfile', widthMm: null, heightMm: null, outerDiameterMm: null, wallThicknessMm: null,
  kgPerMetre: 22.4, profileSeries: 'IPE', profileSize: '200',
}

function table(overrides: Record<string, unknown> = {}) {
  return {
    companyId: COMPANY, version: '3', sourceLabel: 'SENTETIK KATALOG', fileName: 'agirlik.csv', fileSha256: SHA,
    documentId: DOCUMENT, importedAtUtc: '2026-11-14T08:00:00Z', importedBy: 'u-1', rows: [PROFILE_ROW, TUBE_ROW],
    ...overrides,
  }
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

function renderPanel() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <ProfileWeightTablePanel companyId={COMPANY} queryNamespace="test" />
    </QueryClientProvider>,
  )
}

function csvFile(content = CSV, name = 'agirlik.csv') {
  return new File([content], name, { type: 'text/csv' })
}

async function openForm(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('button', { name: PROFILE_WEIGHT_TEXT.openImport }))
  return screen.getByRole('form', { name: PROFILE_WEIGHT_TEXT.formLabel })
}

function fileInput(): HTMLInputElement {
  const input = screen.getByLabelText(PROFILE_WEIGHT_TEXT.fileLabel)
  if (!(input instanceof HTMLInputElement)) throw new Error('file input missing')
  return input
}

describe('company kg/m table panel (URN-D13…D16)', () => {
  it('shows nothing without the permission (403) and says when no table was imported', async () => {
    const sent = install(() => new Response(null, { status: 403 }))
    const { container } = renderPanel()
    await waitFor(() => expect(sent.length).toBe(1))
    await waitFor(() => expect(container.textContent).toBe(''))
    expect(sent[0]?.url).toBe(`/api/v1${PROFILE_WEIGHT_CURRENT_PATH}`)
    expect(sent[0]?.headers['X-Company-Id']).toBe(COMPANY)
    cleanup()
    vi.unstubAllGlobals()

    install(() => json(EMPTY))
    renderPanel()
    expect((await screen.findByTestId('profile-weight-empty')).textContent).toBe(PROFILE_WEIGHT_TEXT.empty)
    expect(screen.queryByTestId('profile-weight-rows')).toBeNull()
  })

  it('shows the current table with its version, source label, file and rows', async () => {
    install(() => json(table()))
    renderPanel()
    const header = await screen.findByTestId('profile-weight-header')
    expect(header.textContent).toContain('3')
    expect(header.textContent).toContain('SENTETIK KATALOG')
    expect(header.textContent).toContain('agirlik.csv')
    const rows = screen.getByTestId('profile-weight-rows')
    expect(rows.querySelectorAll('tbody tr')).toHaveLength(2)
    expect(rows.textContent).toContain('1.75')
    expect(rows.textContent).toContain('48.3')
    expect(rows.textContent).toContain(PROFILE_WEIGHT_TEXT.noValue)
  })

  it('shows a structural profile row by its series and size, with no measure', async () => {
    install(() => json(table({ rows: [PROFILE_ROW, STRUCTURAL_ROW] })))
    renderPanel()
    const rows = await screen.findByTestId('profile-weight-rows')
    const structural = rows.querySelectorAll('tbody tr')[1]
    expect(structural.textContent).toContain('IPE')
    expect(structural.textContent).toContain('200')
    expect(structural.textContent).toContain('22.4')
    expect(screen.getByText(PROFILE_WEIGHT_TEXT.columnSeries)).toBeTruthy()
    expect(screen.getByText(PROFILE_WEIGHT_TEXT.extendedFileFormat, { exact: false })).toBeTruthy()
  })

  it('says what failed for an ended session, an unwired provider, a failed or an off-contract read', async () => {
    install(() => new Response(null, { status: 401 }))
    renderPanel()
    expect(await screen.findByTestId('profile-weight-unauthenticated')).toBeTruthy()
    expect(screen.getByText(PROFILE_WEIGHT_TEXT.unauthenticated)).toBeTruthy()
    cleanup()
    vi.unstubAllGlobals()

    install(() => json({ code: PROFILE_WEIGHT_PROBLEM.providerUnavailable }, 503))
    renderPanel()
    expect(await screen.findByTestId('profile-weight-unavailable')).toBeTruthy()
    expect(screen.getByText(PROFILE_WEIGHT_TEXT.unavailable)).toBeTruthy()
    cleanup()
    vi.unstubAllGlobals()

    install(() => json(table({ companyId: OTHER_COMPANY })))
    renderPanel()
    expect(await screen.findByTestId('profile-weight-failed')).toBeTruthy()
    expect(screen.getByText(PROFILE_WEIGHT_TEXT.failed)).toBeTruthy()
    expect(screen.queryByText(PROFILE_WEIGHT_TEXT.empty)).toBeNull()
  })

  it('imports the whole file with one key, the source label, reason and expected version, and reloads the table', async () => {
    const user = userEvent.setup()
    let reads = 0
    const sent = install((method) => {
      if (method === 'GET') {
        reads += 1
        return json(reads === 1 ? EMPTY : table({ version: '1' }))
      }
      return new Response(null, { status: 204 })
    })
    renderPanel()
    await openForm(user)
    await user.upload(fileInput(), csvFile())
    await user.type(screen.getByRole('textbox', { name: PROFILE_WEIGHT_TEXT.sourceLabelLabel }), '  SENTETIK KATALOG ')
    await user.type(screen.getByRole('textbox', { name: PROFILE_WEIGHT_TEXT.reasonLabel }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: PROFILE_WEIGHT_TEXT.submit }))

    expect(await screen.findByText(PROFILE_WEIGHT_TEXT.imported)).toBeTruthy()
    const posts = sent.filter((request) => request.method === 'POST')
    expect(posts).toHaveLength(1)
    expect(posts[0]?.url).toBe(`/api/v1${PROFILE_WEIGHT_TABLE_PATH}`)
    expect(posts[0]?.headers['X-Company-Id']).toBe(COMPANY)
    expect(posts[0]?.headers['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/)
    expect(posts[0]?.body).toEqual({
      expectedVersion: '0',
      sourceLabel: 'SENTETIK KATALOG',
      reason: 'SENTETIK',
      fileName: 'agirlik.csv',
      contentBase64: bytesToBase64(new TextEncoder().encode(CSV)),
    })
    expect(await screen.findByTestId('profile-weight-rows')).toBeTruthy()
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
    await openForm(user)
    await user.upload(fileInput(), csvFile())
    await user.type(screen.getByRole('textbox', { name: PROFILE_WEIGHT_TEXT.sourceLabelLabel }), 'SENTETIK KATALOG')
    await user.type(screen.getByRole('textbox', { name: PROFILE_WEIGHT_TEXT.reasonLabel }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: PROFILE_WEIGHT_TEXT.submit }))
    expect(await screen.findByText(PROFILE_WEIGHT_TEXT.unknownOutcome)).toBeTruthy()

    await user.click(screen.getByRole('button', { name: PROFILE_WEIGHT_TEXT.retrySame }))
    expect(await screen.findByText(PROFILE_WEIGHT_TEXT.replayed)).toBeTruthy()
    const keys = sent.filter((request) => request.method === 'POST').map((request) => request.headers['Idempotency-Key'])
    expect(keys).toHaveLength(2)
    expect(keys[0]).toBe(keys[1])
  })

  it('reads the vault fail-closed 503 and a refused file as definite refusals, and a version conflict as a reload', async () => {
    const user = userEvent.setup()
    const sent = install((method, attempt) => {
      if (method === 'GET') return json(EMPTY)
      if (attempt === 1) return json({ code: PROFILE_WEIGHT_PROBLEM.fileVaultUnavailable }, 503)
      if (attempt === 2) {
        return json({ code: PROFILE_WEIGHT_PROBLEM.fileInvalid, detail: 'Satır 2: metre ağırlığı geçersiz.' }, 400)
      }
      return json({ code: PROFILE_WEIGHT_PROBLEM.versionConflict }, 409)
    })
    renderPanel()
    await openForm(user)
    await user.upload(fileInput(), csvFile())
    await user.type(screen.getByRole('textbox', { name: PROFILE_WEIGHT_TEXT.sourceLabelLabel }), 'SENTETIK KATALOG')
    await user.type(screen.getByRole('textbox', { name: PROFILE_WEIGHT_TEXT.reasonLabel }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: PROFILE_WEIGHT_TEXT.submit }))
    expect(await screen.findByText(PROFILE_WEIGHT_TEXT.vaultUnavailable)).toBeTruthy()
    // A definite refusal keeps no key: the next try is a new command.
    expect(screen.queryByRole('button', { name: PROFILE_WEIGHT_TEXT.retrySame })).toBeNull()

    await user.click(screen.getByRole('button', { name: PROFILE_WEIGHT_TEXT.submit }))
    expect(await screen.findByText(`${PROFILE_WEIGHT_TEXT.fileInvalid} Satır 2: metre ağırlığı geçersiz.`)).toBeTruthy()

    await user.click(screen.getByRole('button', { name: PROFILE_WEIGHT_TEXT.submit }))
    expect(await screen.findByText(PROFILE_WEIGHT_TEXT.conflict)).toBeTruthy()
    const keys = sent.filter((request) => request.method === 'POST').map((request) => request.headers['Idempotency-Key'])
    expect(new Set(keys).size).toBe(3)
  })

  it('checks the file and the mandatory source label and reason before sending anything', async () => {
    const user = userEvent.setup({ applyAccept: false })
    const sent = install(() => json(EMPTY))
    renderPanel()
    await openForm(user)
    await user.click(screen.getByRole('button', { name: PROFILE_WEIGHT_TEXT.submit }))
    expect(await screen.findByText(PROFILE_WEIGHT_TEXT.fileRequired)).toBeTruthy()

    await user.upload(fileInput(), csvFile(CSV, 'agirlik.xlsm'))
    await user.click(screen.getByRole('button', { name: PROFILE_WEIGHT_TEXT.submit }))
    expect(await screen.findByText(PROFILE_WEIGHT_TEXT.fileUnsupportedType)).toBeTruthy()

    await user.upload(fileInput(), csvFile(''))
    await user.click(screen.getByRole('button', { name: PROFILE_WEIGHT_TEXT.submit }))
    expect(await screen.findByText(PROFILE_WEIGHT_TEXT.fileEmpty)).toBeTruthy()

    await user.upload(fileInput(), csvFile())
    await user.type(screen.getByRole('textbox', { name: PROFILE_WEIGHT_TEXT.sourceLabelLabel }), '   ')
    await user.click(screen.getByRole('button', { name: PROFILE_WEIGHT_TEXT.submit }))
    expect(await screen.findByText(PROFILE_WEIGHT_TEXT.sourceLabelRequired)).toBeTruthy()

    await user.type(screen.getByRole('textbox', { name: PROFILE_WEIGHT_TEXT.sourceLabelLabel }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: PROFILE_WEIGHT_TEXT.submit }))
    expect(await screen.findByText(PROFILE_WEIGHT_TEXT.reasonRequired)).toBeTruthy()
    expect(sent.filter((request) => request.method === 'POST')).toHaveLength(0)
  })

  it('decodes only the declared table shape', () => {
    expect(decodeProfileWeightTable(EMPTY, COMPANY)).toEqual({ version: '0', imported: null, rows: [] })
    expect(decodeProfileWeightTable(table(), COMPANY)?.rows).toHaveLength(2)
    expect(decodeProfileWeightTable(table(), OTHER_COMPANY)).toBeNull()
    expect(decodeProfileWeightTable({ ...EMPTY, rows: [PROFILE_ROW] }, COMPANY)).toBeNull()
    expect(decodeProfileWeightTable({ ...EMPTY, sourceLabel: 'X' }, COMPANY)).toBeNull()
    expect(decodeProfileWeightTable(table({ rows: [] }), COMPANY)).toBeNull()
    expect(decodeProfileWeightTable(table({ version: 3 }), COMPANY)).toBeNull()
    expect(decodeProfileWeightTable(table({ version: '03' }), COMPANY)).toBeNull()
    expect(decodeProfileWeightTable(table({ sourceLabel: ' ' }), COMPANY)).toBeNull()
    expect(decodeProfileWeightTable(table({ fileSha256: 'xyz' }), COMPANY)).toBeNull()
    expect(decodeProfileWeightTable(table({ documentId: 'not-a-uuid' }), COMPANY)).toBeNull()
    expect(decodeProfileWeightTable({ ...table(), extra: 1 }, COMPANY)).toBeNull()
    const { importedBy: _dropped, ...missing } = table()
    expect(decodeProfileWeightTable(missing, COMPANY)).toBeNull()
    // The row shape follows its kind; a sheet or coil is no table kind; the kg/m is positive.
    expect(decodeProfileWeightTable(table({ rows: [{ ...PROFILE_ROW, outerDiameterMm: 10 }] }), COMPANY)).toBeNull()
    expect(decodeProfileWeightTable(table({ rows: [{ ...PROFILE_ROW, heightMm: null }] }), COMPANY)).toBeNull()
    expect(decodeProfileWeightTable(table({ rows: [{ ...TUBE_ROW, widthMm: 40 }] }), COMPANY)).toBeNull()
    expect(decodeProfileWeightTable(table({ rows: [{ ...PROFILE_ROW, sectionKind: 'Sheet' }] }), COMPANY)).toBeNull()
    expect(decodeProfileWeightTable(table({ rows: [{ ...PROFILE_ROW, kgPerMetre: 0 }] }), COMPANY)).toBeNull()
    expect(decodeProfileWeightTable(table({ rows: [{ ...PROFILE_ROW, extra: 1 }] }), COMPANY)).toBeNull()
    // KLN-D87: a structural profile row holds its series and size and no measure; the other kinds hold neither.
    expect(decodeProfileWeightTable(table({ rows: [STRUCTURAL_ROW] }), COMPANY)?.rows[0]).toEqual(STRUCTURAL_ROW)
    expect(decodeProfileWeightTable(table({ rows: [{ ...STRUCTURAL_ROW, wallThicknessMm: 2 }] }), COMPANY)).toBeNull()
    expect(decodeProfileWeightTable(table({ rows: [{ ...STRUCTURAL_ROW, profileSeries: 'W' }] }), COMPANY)).toBeNull()
    expect(decodeProfileWeightTable(table({ rows: [{ ...STRUCTURAL_ROW, profileSize: null }] }), COMPANY)).toBeNull()
    expect(decodeProfileWeightTable(table({ rows: [{ ...PROFILE_ROW, profileSeries: 'IPE' }] }), COMPANY)).toBeNull()
    expect(decodeProfileWeightTable(table({ rows: [{ ...TUBE_ROW, wallThicknessMm: null }] }), COMPANY)).toBeNull()
  })

  it('checks a file name and size only to help the person', () => {
    expect(checkFile({ name: 'a.CSV', size: 1 })).toBe('ok')
    expect(checkFile({ name: 'a.Xlsx', size: 1 })).toBe('ok')
    expect(checkFile({ name: 'a.txt', size: 1 })).toBe('unsupported-type')
    expect(checkFile({ name: 'a.xlsm', size: 1 })).toBe('unsupported-type')
    expect(checkFile({ name: 'a.xls', size: 1 })).toBe('unsupported-type')
    expect(checkFile({ name: 'a.csv', size: 0 })).toBe('empty')
    expect(checkFile({ name: 'a.xlsx', size: 5_242_880 })).toBe('ok')
    expect(checkFile({ name: 'a.xlsx', size: 5_242_881 })).toBe('too-large')
    expect(checkFile({ name: `${'a'.repeat(252)}.csv`, size: 1 })).toBe('name-too-long')
    expect(bytesToBase64(new TextEncoder().encode('kg;m'))).toBe('a2c7bQ==')
  })
})

describe('company kg/m table client ↔ canonical OpenAPI', () => {
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
  const currentRoute = `/api/v1${PROFILE_WEIGHT_CURRENT_PATH}`
  const importRoute = `/api/v1${PROFILE_WEIGHT_TABLE_PATH}`

  it('calls the declared routes and sends exactly the declared members', () => {
    expect(document.paths[currentRoute]?.get).toBeDefined()
    const reference = document.paths[importRoute]?.post?.requestBody?.content?.['application/json']?.schema?.$ref ?? ''
    const schema = document.components.schemas[reference.replace('#/components/schemas/', '')]
    expect(Object.keys(schema?.properties ?? {}).sort()).toEqual([
      'contentBase64', 'expectedVersion', 'fileName', 'reason', 'sourceLabel',
    ])
  })

  it('decodes exactly the declared read members and knows only declared problem codes', () => {
    expect(document.paths[currentRoute]?.get?.responses?.['200']?.content?.['application/json']?.schema?.$ref).toBe(
      '#/components/schemas/ProductProfileWeightTableViewV1',
    )
    expect(Object.keys(document.components.schemas.ProductProfileWeightTableViewV1?.properties ?? {}).sort()).toEqual([
      'companyId', 'documentId', 'fileName', 'fileSha256', 'importedAtUtc', 'importedBy', 'rows', 'sourceLabel', 'version',
    ])
    expect(Object.keys(document.components.schemas.ProductProfileWeightRowV1?.properties ?? {}).sort()).toEqual([
      'heightMm', 'kgPerMetre', 'outerDiameterMm', 'profileSeries', 'profileSize', 'sectionKind', 'wallThicknessMm', 'widthMm',
    ])
    const declared = new Set(
      [document.paths[currentRoute] ?? {}, document.paths[importRoute] ?? {}]
        .flatMap((operations) => Object.values(operations))
        .flatMap((operation) => Object.values(operation.responses ?? {}))
        .flatMap((response) => response['x-erp-problem-codes'] ?? []),
    )
    for (const code of Object.values(PROFILE_WEIGHT_PROBLEM)) expect(declared.has(code), code).toBe(true)
  })
})
