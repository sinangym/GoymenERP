/**
 * KLN-D54 (STK-S37) component cases — the structural profile weight table screen. COMPONENT/DOM EVIDENCE ONLY: `fetch` is
 * stubbed with synthetic answers; the real API and SQL are the Product unit tests and the price list relational acceptance tests.
 * What these cases establish: no permission shows nothing, each failed read says what failed, no table says "kaynak yok", the
 * current table shows its version, source and rows, the file is read line by line and refused with the line number, the import
 * sends the company, the expected version, the source label, the reason and the rows (never the file) with one Idempotency-Key
 * kept for a retry after an unknown outcome, a version conflict reloads, and the routes are pinned to the canonical OpenAPI.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ProfileWeightTablePanel } from './ProfileWeightTablePanel'
import {
  PROFILE_WEIGHT_CURRENT_PATH,
  PROFILE_WEIGHT_PATH,
  PROFILE_WEIGHT_PROBLEM,
  decodeProfileWeightTable,
  parseProfileWeightFile,
  parseWeight,
} from './profileWeightApi'
import { PARSE_REFUSAL_TEXT, PROFILE_WEIGHT_TEXT, parseRefusalMessage } from './profileWeightText'

const COMPANY = 'aaaaaaaa-0000-4000-8000-000000000001'
const OTHER_COMPANY = '00000000-0000-4000-8000-000000000009'

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

const NONE = {
  companyId: COMPANY, version: '0', sourceLabel: null, reason: null, importedAtUtc: null, importedBy: null, rows: [],
}

function table(overrides: Record<string, unknown> = {}) {
  return {
    companyId: COMPANY, version: '2', sourceLabel: 'SENTETIK katalog 2026', reason: 'SENTETIK ilk yükleme',
    importedAtUtc: '2026-11-15T08:00:00Z', importedBy: 'u-1',
    rows: [{ series: 'HEA', size: '100', kgPerMetre: 16.7 }, { series: 'IPE', size: '200', kgPerMetre: 22.4 }],
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

const FILE = 'series;size;kgPerMetre\nIPE;200;22.4\n\nHEA;100;16.7\n'

function textFile(text: string) {
  return new File([text], 'agirliklar.txt', { type: 'text/plain' })
}

describe('structural profile weight table panel (KLN-D54)', () => {
  it('shows nothing without the permission (403) and says "kaynak yok" while no table is imported', async () => {
    install(() => new Response(null, { status: 403 }))
    const { container } = renderPanel()
    await waitFor(() => expect(container.querySelector('section')).toBeNull())
    cleanup()
    vi.unstubAllGlobals()

    const sent = install(() => json(NONE))
    renderPanel()
    expect(await screen.findByTestId('profile-weight-none')).toBeTruthy()
    expect(screen.getByText(PROFILE_WEIGHT_TEXT.none)).toBeTruthy()
    expect(sent[0].url).toMatch(new RegExp(`${PROFILE_WEIGHT_CURRENT_PATH}$`))
    expect(sent[0].headers['X-Company-Id']).toBe(COMPANY)
    expect(sent.every((request) => request.method === 'GET')).toBe(true)
  })

  it('says what failed: an ended session, an unwired provider, an off-contract answer', async () => {
    const cases: readonly [Response, string, string][] = [
      [new Response(null, { status: 401 }), 'profile-weight-unauthenticated', PROFILE_WEIGHT_TEXT.unauthenticated],
      [new Response(null, { status: 503 }), 'profile-weight-unavailable', PROFILE_WEIGHT_TEXT.unavailable],
      [json(table({ companyId: OTHER_COMPANY })), 'profile-weight-failed', PROFILE_WEIGHT_TEXT.failed],
    ]
    for (const [response, testId, text] of cases) {
      install(() => response)
      renderPanel()
      expect((await screen.findByTestId(testId)).textContent).toContain(text)
      expect(screen.queryByTestId('profile-weight-none')).toBeNull()
      cleanup()
      vi.unstubAllGlobals()
    }
  })

  it('shows the current version with its source, reason and rows, never rounded', async () => {
    install(() => json(table({ rows: [{ series: 'HEM', size: '1000', kgPerMetre: 349.123456 }] })))
    renderPanel()
    expect((await screen.findByTestId('profile-weight-header')).textContent).toContain('SENTETIK katalog 2026')
    expect(screen.getByText('SENTETIK ilk yükleme')).toBeTruthy()
    expect(screen.getByTestId('profile-weight-rows').textContent).toContain('349.123456')
  })

  it('refuses a malformed file with its line number and sends nothing', async () => {
    const user = userEvent.setup()
    const sent = install(() => json(NONE))
    renderPanel()

    const file = await screen.findByLabelText(PROFILE_WEIGHT_TEXT.fileLabel)
    await user.upload(file, textFile('series;size;kgPerMetre\nIPE;200;22.4\nW;200;22.4\n'))
    expect(await screen.findByText(parseRefusalMessage('series', 3))).toBeTruthy()
    await user.type(screen.getByRole('textbox', { name: PROFILE_WEIGHT_TEXT.sourceInputLabel }), 'SENTETIK katalog')
    await user.type(screen.getByRole('textbox', { name: PROFILE_WEIGHT_TEXT.reasonLabel }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: PROFILE_WEIGHT_TEXT.submit }))
    expect(await screen.findByText(PROFILE_WEIGHT_TEXT.fileRequired)).toBeTruthy()
    expect(sent.some((request) => request.method === 'POST')).toBe(false)
  })

  it('imports the rows with the version, the source, the reason and one key, kept for a retry after an unknown outcome', async () => {
    const user = userEvent.setup()
    let reads = 0
    const sent = install((method, attempt) => {
      if (method === 'GET') {
        reads += 1
        return json(reads === 1 ? NONE : table({ version: '1' }))
      }
      return attempt === 1 ? new Response(null, { status: 502 }) : new Response(null, { status: 204 })
    })
    renderPanel()

    await user.upload(await screen.findByLabelText(PROFILE_WEIGHT_TEXT.fileLabel), textFile(FILE))
    expect(await screen.findByText(PROFILE_WEIGHT_TEXT.parsedRows(2))).toBeTruthy()
    await user.click(screen.getByRole('button', { name: PROFILE_WEIGHT_TEXT.submit }))
    expect(await screen.findByText(PROFILE_WEIGHT_TEXT.sourceRequired)).toBeTruthy()
    await user.type(screen.getByRole('textbox', { name: PROFILE_WEIGHT_TEXT.sourceInputLabel }), '  SENTETIK katalog  ')
    await user.click(screen.getByRole('button', { name: PROFILE_WEIGHT_TEXT.submit }))
    expect(await screen.findByText(PROFILE_WEIGHT_TEXT.reasonRequired)).toBeTruthy()
    expect(sent.some((request) => request.method === 'POST')).toBe(false)

    await user.type(screen.getByRole('textbox', { name: PROFILE_WEIGHT_TEXT.reasonLabel }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: PROFILE_WEIGHT_TEXT.submit }))
    expect(await screen.findByText(PROFILE_WEIGHT_TEXT.unknownOutcome)).toBeTruthy()
    await user.click(screen.getByRole('button', { name: PROFILE_WEIGHT_TEXT.retrySame }))
    expect(await screen.findByText(PROFILE_WEIGHT_TEXT.imported)).toBeTruthy()
    await waitFor(() => expect(reads).toBe(2))

    const writes = sent.filter((request) => request.method === 'POST')
    expect(writes).toHaveLength(2)
    expect(writes[0].url).toMatch(new RegExp(`${PROFILE_WEIGHT_PATH}$`))
    expect(writes[0].body).toEqual({
      expectedVersion: '0',
      sourceLabel: 'SENTETIK katalog',
      reason: 'SENTETIK',
      rows: [{ series: 'IPE', size: '200', kgPerMetre: 22.4 }, { series: 'HEA', size: '100', kgPerMetre: 16.7 }],
    })
    expect(writes[0].headers['X-Company-Id']).toBe(COMPANY)
    expect(writes[0].headers['Idempotency-Key']).toMatch(/.+/)
    expect(writes[1].headers['Idempotency-Key']).toBe(writes[0].headers['Idempotency-Key'])
    expect(writes[1].body).toEqual(writes[0].body)
  })

  it('reloads after a version conflict and shows the server refusal of a row', async () => {
    const user = userEvent.setup()
    let reads = 0
    install((method, attempt) => {
      if (method === 'GET') {
        reads += 1
        return json(reads === 1 ? table() : table({ version: '3' }))
      }
      return attempt === 1
        ? json({ code: PROFILE_WEIGHT_PROBLEM.rowsInvalid, detail: '2. satır: SENTETIK' }, 400)
        : json({ code: PROFILE_WEIGHT_PROBLEM.versionConflict }, 409)
    })
    renderPanel()

    await user.upload(await screen.findByLabelText(PROFILE_WEIGHT_TEXT.fileLabel), textFile(FILE))
    await user.type(screen.getByRole('textbox', { name: PROFILE_WEIGHT_TEXT.sourceInputLabel }), 'SENTETIK katalog')
    await user.type(screen.getByRole('textbox', { name: PROFILE_WEIGHT_TEXT.reasonLabel }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: PROFILE_WEIGHT_TEXT.submit }))
    expect(await screen.findByText(`${PROFILE_WEIGHT_TEXT.rowsRefused} 2. satır: SENTETIK`)).toBeTruthy()
    await user.click(screen.getByRole('button', { name: PROFILE_WEIGHT_TEXT.submit }))
    expect(await screen.findByText(PROFILE_WEIGHT_TEXT.conflict)).toBeTruthy()
    await waitFor(() => expect(reads).toBe(2))
  })
})

describe('structural profile weight table decoder and file', () => {
  it('fails closed on an off-contract read', () => {
    expect(decodeProfileWeightTable(table(), COMPANY)?.rows).toHaveLength(2)
    expect(decodeProfileWeightTable(NONE, COMPANY)?.version).toBe('0')
    expect(decodeProfileWeightTable(table({ companyId: OTHER_COMPANY }), COMPANY)).toBeNull()
    expect(decodeProfileWeightTable(table({ extra: 1 }), COMPANY)).toBeNull()
    expect(decodeProfileWeightTable(table({ version: '01' }), COMPANY)).toBeNull()
    expect(decodeProfileWeightTable(table({ rows: [] }), COMPANY)).toBeNull()
    expect(decodeProfileWeightTable(table({ sourceLabel: null }), COMPANY)).toBeNull()
    expect(decodeProfileWeightTable({ ...NONE, sourceLabel: 'SENTETIK' }, COMPANY)).toBeNull()
    expect(decodeProfileWeightTable({ ...NONE, rows: table().rows }, COMPANY)).toBeNull()
    expect(decodeProfileWeightTable(table({ rows: [{ series: 'W', size: '200', kgPerMetre: 1 }] }), COMPANY)).toBeNull()
    expect(decodeProfileWeightTable(table({ rows: [{ series: 'IPE', size: '200', kgPerMetre: 0 }] }), COMPANY)).toBeNull()
    expect(decodeProfileWeightTable(table({ rows: [{ series: 'IPE', size: '200', kgPerMetre: '22.4' }] }), COMPANY)).toBeNull()
    expect(decodeProfileWeightTable(table({ rows: [{ series: 'IPE', size: '200' }] }), COMPANY)).toBeNull()
  })

  it('reads the file line by line, never rounding, and refuses each malformed line with its number', () => {
    expect(parseProfileWeightFile(`﻿${FILE.replace(/\n/g, '\r\n')}`)).toEqual({
      ok: true,
      rows: [{ series: 'IPE', size: '200', kgPerMetre: 22.4 }, { series: 'HEA', size: '100', kgPerMetre: 16.7 }],
    })
    const header = 'series;size;kgPerMetre\n'
    expect(parseProfileWeightFile('seri;olcu;kg\nIPE;200;22.4')).toEqual({ ok: false, reason: 'header', line: 1 })
    expect(parseProfileWeightFile(header)).toEqual({ ok: false, reason: 'empty', line: null })
    expect(parseProfileWeightFile(`${header}IPE;200`)).toEqual({ ok: false, reason: 'columns', line: 2 })
    expect(parseProfileWeightFile(`${header}ipe;200;22.4`)).toEqual({ ok: false, reason: 'series', line: 2 })
    expect(parseProfileWeightFile(`${header}IPE;200a;22.4`)).toEqual({ ok: false, reason: 'size', line: 2 })
    expect(parseProfileWeightFile(`${header}IPE;200;22,4`)).toEqual({ ok: false, reason: 'weight', line: 2 })
    expect(parseProfileWeightFile(`${header}IPE;200;22.4\nIPE;200;22.4`)).toEqual({ ok: false, reason: 'duplicate', line: 3 })
    const many = Array.from({ length: 2001 }, (_, index) => `IPE;${index + 1};1`).join('\n')
    expect(parseProfileWeightFile(`${header}${many}`)).toEqual({ ok: false, reason: 'tooMany', line: null })
    expect(parseWeight('22.123456')).toBe(22.123456)
    expect(parseWeight('22.1234567')).toBeNull()
    expect(parseWeight('0')).toBeNull()
    expect(parseWeight('1e3')).toBeNull()
    expect(parseRefusalMessage('weight', 4)).toBe(`4. satır: ${PARSE_REFUSAL_TEXT.weight}`)
  })
})

describe('structural profile weight table client ↔ canonical OpenAPI', () => {
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

  it('calls the declared routes with exactly the declared members and knows only declared problem codes', () => {
    const read = document.paths[`/api/v1${PROFILE_WEIGHT_CURRENT_PATH}`]?.get
    expect(read?.responses?.['200']?.content?.['application/json']?.schema?.$ref).toBe(
      '#/components/schemas/ProductProfileWeightTableViewV1',
    )
    expect(Object.keys(document.components.schemas.ProductProfileWeightTableViewV1?.properties ?? {}).sort()).toEqual([
      'companyId', 'importedAtUtc', 'importedBy', 'reason', 'rows', 'sourceLabel', 'version',
    ])
    const write = document.paths[`/api/v1${PROFILE_WEIGHT_PATH}`]?.post
    const reference = write?.requestBody?.content?.['application/json']?.schema?.$ref ?? ''
    const schema = document.components.schemas[reference.replace('#/components/schemas/', '')]
    expect(Object.keys(schema?.properties ?? {}).sort()).toEqual(['expectedVersion', 'reason', 'rows', 'sourceLabel'])
    const declared = new Set(
      Object.values(write?.responses ?? {}).flatMap((response) => response['x-erp-problem-codes'] ?? []),
    )
    for (const code of Object.values(PROFILE_WEIGHT_PROBLEM)) expect(declared.has(code), code).toBe(true)
  })
})
