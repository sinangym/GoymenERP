/**
 * BS-9 component cases — the product card's "Ebat/kalınlık" panel. COMPONENT/DOM EVIDENCE ONLY: `fetch` is stubbed with
 * synthetic answers; the real API and SQL are the Product unit tests and the price list relational acceptance tests. What these
 * cases establish: a forbidden read shows nothing, an unset row says "kaynak yok", each failed read says what failed, the
 * command sends the company, one Idempotency-Key and the exact body with only the kind's measures, keeps the key for a retry
 * after an unknown outcome, never rounds a measure, and the route and body are pinned to the canonical OpenAPI document.
 * URN-D16 (STK-S37R): the kg/m is no input; the panel shows the company table's value with its source and version, says when
 * the table holds none, and reads the refusal of a profile the table does not hold.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ProductDimensionPanel } from './ProductDimensionPanel'
import { DIMENSION_PROBLEM, decodeDimension, dimensionPath, parseMeasure } from './dimensionApi'
import { DIMENSION_TEXT, MEASURE_LABEL, SECTION_KIND_LABEL } from './dimensionText'

const COMPANY = 'aaaaaaaa-0000-4000-8000-000000000001'
const OTHER_COMPANY = '00000000-0000-4000-8000-000000000009'
const PRODUCT = '71000000-0000-4000-8000-000000000001'

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

const EMPTY_MEASURES = {
  thicknessMm: null, widthMm: null, lengthMm: null, heightMm: null, outerDiameterMm: null, wallThicknessMm: null,
}

/** No table value: the three kg/m members are null together. */
const NO_WEIGHT = { kgPerMetre: null, kgPerMetreSourceLabel: null, kgPerMetreTableVersion: null }

const TABLE_WEIGHT = { kgPerMetre: 1.75, kgPerMetreSourceLabel: 'SENTETIK KATALOG', kgPerMetreTableVersion: '3' }

function payload(overrides: Record<string, unknown> = {}) {
  return {
    companyId: COMPANY, productId: PRODUCT, version: '2', sectionKind: 'Sheet', ...EMPTY_MEASURES, ...NO_WEIGHT,
    thicknessMm: 2, widthMm: 1250, lengthMm: 2500, updatedAtUtc: '2026-11-12T08:00:00Z', updatedBy: 'u-1',
    updatedInCompanyId: COMPANY, ...overrides,
  }
}

const UNSET = {
  companyId: COMPANY, productId: PRODUCT, version: '0', sectionKind: null, ...EMPTY_MEASURES, ...NO_WEIGHT,
  updatedAtUtc: null, updatedBy: null, updatedInCompanyId: null,
}

const PROFILE = { ...EMPTY_MEASURES, sectionKind: 'Profile', widthMm: 40, heightMm: 20, wallThicknessMm: 2 }

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

function renderPanel() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <ProductDimensionPanel companyId={COMPANY} productId={PRODUCT} queryNamespace="test" />
    </QueryClientProvider>,
  )
}

describe('product dimension panel (BS-9)', () => {
  it('shows nothing without the permission (403) and says "kaynak yok" for an unset row', async () => {
    install(() => new Response(null, { status: 403 }))
    const { container } = renderPanel()
    await waitFor(() => expect(container.querySelector('section')).toBeNull())
    cleanup()
    vi.unstubAllGlobals()

    const sent = install(() => json(UNSET))
    renderPanel()
    expect(await screen.findByText(DIMENSION_TEXT.unset)).toBeTruthy()
    expect(sent[0].url).toMatch(new RegExp(`/product-dimensions/${PRODUCT}$`))
    expect(sent[0].headers['X-Company-Id']).toBe(COMPANY)
    expect(sent.every((request) => request.method === 'GET')).toBe(true)
  })

  it('says what failed: an ended session, a missing card, an unwired provider, an off-contract answer', async () => {
    const cases: readonly [Response, string, string][] = [
      [new Response(null, { status: 401 }), 'product-dimension-unauthenticated', DIMENSION_TEXT.unauthenticated],
      [new Response(null, { status: 404 }), 'product-dimension-not-found', DIMENSION_TEXT.notFound],
      [new Response(null, { status: 503 }), 'product-dimension-unavailable', DIMENSION_TEXT.unavailable],
      [json(payload({ sectionKind: 'Copper' })), 'product-dimension-failed', DIMENSION_TEXT.failed],
    ]
    for (const [response, testId, text] of cases) {
      install(() => response)
      renderPanel()
      expect((await screen.findByTestId(testId)).textContent).toContain(text)
      expect(screen.queryByText(DIMENSION_TEXT.unset)).toBeNull()
      cleanup()
      vi.unstubAllGlobals()
    }
  })

  it('shows the stored measures and that the row is shared, and when it was last changed in another company', async () => {
    install(() => json(payload({ updatedInCompanyId: OTHER_COMPANY })))
    renderPanel()
    expect(await screen.findByText(SECTION_KIND_LABEL.Sheet)).toBeTruthy()
    expect(screen.getByText(MEASURE_LABEL.thicknessMm)).toBeTruthy()
    expect(screen.getByText('1250')).toBeTruthy()
    expect(screen.queryByText(MEASURE_LABEL.outerDiameterMm)).toBeNull()
    expect(screen.getByText(DIMENSION_TEXT.shared)).toBeTruthy()
    expect(screen.getByText(DIMENSION_TEXT.changedElsewhere)).toBeTruthy()
    // A sheet takes no table kg/m: neither a value nor "kaynak yok" for it.
    expect(screen.queryByText(DIMENSION_TEXT.kgPerMetreLabel)).toBeNull()
  })

  it('shows the table kg/m with its source label and version, and says when the table holds none', async () => {
    install(() => json(payload({ ...PROFILE, ...TABLE_WEIGHT })))
    renderPanel()
    expect((await screen.findByTestId('product-dimension-kg-per-metre')).textContent).toBe('1.75')
    expect(screen.getByText('SENTETIK KATALOG')).toBeTruthy()
    expect(screen.getByText(DIMENSION_TEXT.kgPerMetreTableVersion)).toBeTruthy()
    expect(screen.getByText(DIMENSION_TEXT.weightNote)).toBeTruthy()
    cleanup()
    vi.unstubAllGlobals()

    install(() => json(payload(PROFILE)))
    renderPanel()
    expect((await screen.findByTestId('product-dimension-kg-per-metre-missing')).textContent).toBe(
      DIMENSION_TEXT.kgPerMetreMissing,
    )
    cleanup()
    vi.unstubAllGlobals()

    install(() => json(payload({ ...EMPTY_MEASURES, sectionKind: 'RoundTube', outerDiameterMm: 48.3, wallThicknessMm: 2.6 })))
    renderPanel()
    expect((await screen.findByTestId('product-dimension-kg-per-metre-missing')).textContent).toBe(
      DIMENSION_TEXT.kgPerMetreTubeMissing,
    )
  })

  it('sets a round tube with the version, the reason and one key, kept for a retry after an unknown outcome', async () => {
    const user = userEvent.setup()
    const sent = install((method, attempt) => {
      if (method === 'GET') return json(payload())
      return attempt === 1 ? new Response(null, { status: 500 }) : new Response(null, { status: 204 })
    })
    renderPanel()

    await user.click(await screen.findByRole('button', { name: DIMENSION_TEXT.change }))
    await user.selectOptions(screen.getByRole('combobox', { name: DIMENSION_TEXT.kindLabel }), 'RoundTube')
    await user.type(screen.getByRole('textbox', { name: MEASURE_LABEL.outerDiameterMm }), '48,3')
    await user.type(screen.getByRole('textbox', { name: MEASURE_LABEL.wallThicknessMm }), '2.6')
    await user.type(screen.getByRole('textbox', { name: DIMENSION_TEXT.reasonLabel }), '  SENTETIK  ')
    await user.click(screen.getByRole('button', { name: DIMENSION_TEXT.save }))
    expect(await screen.findByText(DIMENSION_TEXT.unknownOutcome)).toBeTruthy()
    await user.click(screen.getByRole('button', { name: DIMENSION_TEXT.retrySame }))
    expect(await screen.findByText(DIMENSION_TEXT.saved)).toBeTruthy()

    const writes = sent.filter((request) => request.method === 'PUT')
    expect(writes).toHaveLength(2)
    expect(writes[0].url).toMatch(new RegExp(`/product-dimensions/${PRODUCT}$`))
    // The sheet's earlier thickness/width/length are not carried into the tube: only the kind's measures leave.
    expect(writes[0].body).toEqual({
      expectedVersion: '2', sectionKind: 'RoundTube', ...EMPTY_MEASURES, outerDiameterMm: 48.3, wallThicknessMm: 2.6, reason: 'SENTETIK',
    })
    expect(writes[0].headers['X-Company-Id']).toBe(COMPANY)
    expect(writes[0].headers['Idempotency-Key']).toMatch(/.+/)
    expect(writes[1].headers['Idempotency-Key']).toBe(writes[0].headers['Idempotency-Key'])
  })

  it('refuses a missing, over-precise or too thick measure before sending, and reads the missing table source', async () => {
    const user = userEvent.setup()
    const sent = install((method) =>
      method === 'GET' ? json(UNSET) : json({ code: DIMENSION_PROBLEM.weightSourceMissing }, 409))
    renderPanel()

    await user.click(await screen.findByRole('button', { name: DIMENSION_TEXT.change }))
    const kind = screen.getByRole('combobox', { name: DIMENSION_TEXT.kindLabel }) as HTMLSelectElement
    expect(kind.value).toBe('')
    expect((screen.getByRole('button', { name: DIMENSION_TEXT.save }) as HTMLButtonElement).disabled).toBe(true)

    await user.selectOptions(kind, 'Profile')
    // The kg/m is no input: it comes from the company's table only.
    expect(screen.queryByRole('textbox', { name: DIMENSION_TEXT.kgPerMetreLabel })).toBeNull()
    expect(screen.getByText(DIMENSION_TEXT.profileWeightNote)).toBeTruthy()
    await user.type(screen.getByRole('textbox', { name: DIMENSION_TEXT.reasonLabel }), 'SENTETIK')
    await user.type(screen.getByRole('textbox', { name: MEASURE_LABEL.widthMm }), '40')
    await user.type(screen.getByRole('textbox', { name: MEASURE_LABEL.heightMm }), '20')
    await user.click(screen.getByRole('button', { name: DIMENSION_TEXT.save }))
    expect(await screen.findByText(DIMENSION_TEXT.measureMissing)).toBeTruthy()

    await user.type(screen.getByRole('textbox', { name: MEASURE_LABEL.wallThicknessMm }), '2.1234')
    await user.click(screen.getByRole('button', { name: DIMENSION_TEXT.save }))
    expect(await screen.findByText(DIMENSION_TEXT.measureFormat)).toBeTruthy()

    await user.clear(screen.getByRole('textbox', { name: MEASURE_LABEL.wallThicknessMm }))
    await user.type(screen.getByRole('textbox', { name: MEASURE_LABEL.wallThicknessMm }), '10.5')
    await user.click(screen.getByRole('button', { name: DIMENSION_TEXT.save }))
    expect(await screen.findByText(DIMENSION_TEXT.wallTooThick)).toBeTruthy()
    expect(sent.some((request) => request.method === 'PUT')).toBe(false)

    await user.clear(screen.getByRole('textbox', { name: MEASURE_LABEL.wallThicknessMm }))
    await user.type(screen.getByRole('textbox', { name: MEASURE_LABEL.wallThicknessMm }), '2')
    await user.click(screen.getByRole('button', { name: DIMENSION_TEXT.save }))
    expect(await screen.findByText(DIMENSION_TEXT.weightSourceMissing)).toBeTruthy()
    expect(sent.filter((request) => request.method === 'PUT').map((request) => request.body)).toEqual([
      { expectedVersion: '0', sectionKind: 'Profile', ...EMPTY_MEASURES, widthMm: 40, heightMm: 20, wallThicknessMm: 2, reason: 'SENTETIK' },
    ])
  })

  it('reloads after a version conflict', async () => {
    const user = userEvent.setup()
    let reads = 0
    install((method) => {
      if (method === 'GET') {
        reads += 1
        return json(reads === 1 ? payload() : payload({ version: '3', thicknessMm: 3 }))
      }
      return json({ code: DIMENSION_PROBLEM.versionConflict }, 409)
    })
    renderPanel()

    await user.click(await screen.findByRole('button', { name: DIMENSION_TEXT.change }))
    await user.type(screen.getByRole('textbox', { name: DIMENSION_TEXT.reasonLabel }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: DIMENSION_TEXT.save }))
    expect(await screen.findByText(DIMENSION_TEXT.conflict)).toBeTruthy()
    await waitFor(() => expect(reads).toBe(2))
  })
})

describe('product dimension decoder and input', () => {
  it('fails closed on an off-contract read', () => {
    expect(decodeDimension(payload(), COMPANY, PRODUCT)).not.toBeNull()
    expect(decodeDimension(UNSET, COMPANY, PRODUCT)?.sectionKind).toBeNull()
    expect(decodeDimension(payload({ sectionKind: 'Copper' }), COMPANY, PRODUCT)).toBeNull()
    expect(decodeDimension(payload({ version: '0' }), COMPANY, PRODUCT)).toBeNull()
    expect(decodeDimension(payload({ companyId: OTHER_COMPANY }), COMPANY, PRODUCT)).toBeNull()
    expect(decodeDimension(payload({ productId: COMPANY }), COMPANY, PRODUCT)).toBeNull()
    expect(decodeDimension(payload({ densityKgM3: 7850 }), COMPANY, PRODUCT)).toBeNull()
    const { kgPerMetre: _dropped, ...missing } = payload()
    expect(decodeDimension(missing, COMPANY, PRODUCT)).toBeNull()
    const { kgPerMetreSourceLabel: _label, ...missingLabel } = payload()
    expect(decodeDimension(missingLabel, COMPANY, PRODUCT)).toBeNull()
    // The shape: a sheet needs its length and holds no diameter; a value is a positive number.
    expect(decodeDimension(payload({ lengthMm: null }), COMPANY, PRODUCT)).toBeNull()
    expect(decodeDimension(payload({ outerDiameterMm: 10 }), COMPANY, PRODUCT)).toBeNull()
    expect(decodeDimension(payload({ widthMm: '1250' }), COMPANY, PRODUCT)).toBeNull()
    expect(decodeDimension(payload({ widthMm: 0 }), COMPANY, PRODUCT)).toBeNull()
    expect(decodeDimension({ ...UNSET, thicknessMm: 2 }, COMPANY, PRODUCT)).toBeNull()
    // A stored row names the company it was changed in; an unset one names none.
    expect(decodeDimension(payload({ updatedInCompanyId: null }), COMPANY, PRODUCT)).toBeNull()
    expect(decodeDimension({ ...UNSET, updatedInCompanyId: COMPANY }, COMPANY, PRODUCT)).toBeNull()
    expect(decodeDimension(payload({ updatedInCompanyId: OTHER_COMPANY }), COMPANY, PRODUCT)?.changedInAnotherCompany).toBe(true)
    // The table kg/m: all three members null or all three set; only a tube or a profile reads one (a later import may drop the
    // row, so a stored profile can read none).
    const tube = { ...EMPTY_MEASURES, sectionKind: 'RoundTube', outerDiameterMm: 48.3, wallThicknessMm: 2.6 }
    expect(decodeDimension(payload(tube), COMPANY, PRODUCT)?.tableWeight).toBeNull()
    expect(decodeDimension(payload({ ...tube, ...TABLE_WEIGHT, kgPerMetre: 2.93 }), COMPANY, PRODUCT)?.tableWeight).toEqual({
      kgPerMetre: 2.93, sourceLabel: 'SENTETIK KATALOG', tableVersion: '3',
    })
    expect(decodeDimension(payload(PROFILE), COMPANY, PRODUCT)?.tableWeight).toBeNull()
    expect(decodeDimension(payload({ ...PROFILE, ...TABLE_WEIGHT }), COMPANY, PRODUCT)).not.toBeNull()
    expect(decodeDimension(payload({ ...PROFILE, kgPerMetre: 1.75 }), COMPANY, PRODUCT)).toBeNull()
    expect(decodeDimension(payload({ ...PROFILE, ...TABLE_WEIGHT, kgPerMetreSourceLabel: ' ' }), COMPANY, PRODUCT)).toBeNull()
    expect(decodeDimension(payload({ ...PROFILE, ...TABLE_WEIGHT, kgPerMetreTableVersion: '0' }), COMPANY, PRODUCT)).toBeNull()
    expect(decodeDimension(payload({ ...PROFILE, ...TABLE_WEIGHT, kgPerMetreTableVersion: 3 }), COMPANY, PRODUCT)).toBeNull()
    expect(decodeDimension(payload({ ...PROFILE, ...TABLE_WEIGHT, kgPerMetre: 0 }), COMPANY, PRODUCT)).toBeNull()
    expect(decodeDimension(payload(TABLE_WEIGHT), COMPANY, PRODUCT)).toBeNull()
    expect(decodeDimension({ ...UNSET, ...TABLE_WEIGHT }, COMPANY, PRODUCT)).toBeNull()
  })

  it('reads a typed measure without rounding it', () => {
    expect(parseMeasure('thicknessMm', '')).toEqual({ ok: true, value: null })
    expect(parseMeasure('thicknessMm', ' 1,5 ')).toEqual({ ok: true, value: 1.5 })
    expect(parseMeasure('thicknessMm', '1.125')).toEqual({ ok: true, value: 1.125 })
    expect(parseMeasure('thicknessMm', '1.1255')).toEqual({ ok: false })
    expect(parseMeasure('widthMm', '0')).toEqual({ ok: false })
    expect(parseMeasure('widthMm', '-1')).toEqual({ ok: false })
    expect(parseMeasure('widthMm', '1e3')).toEqual({ ok: false })
  })
})

describe('product dimension client ↔ canonical OpenAPI', () => {
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
  const route = `/api/v1${dimensionPath(PRODUCT)}`.replace(PRODUCT, '{productId}')

  it('calls the declared route and sends exactly the declared members', () => {
    expect(document.paths[route]?.get).toBeDefined()
    const reference = document.paths[route]?.put?.requestBody?.content?.['application/json']?.schema?.$ref ?? ''
    const schema = document.components.schemas[reference.replace('#/components/schemas/', '')]
    expect(Object.keys(schema?.properties ?? {}).sort()).toEqual([
      'expectedVersion', 'heightMm', 'lengthMm', 'outerDiameterMm', 'reason', 'sectionKind', 'thicknessMm', 'wallThicknessMm',
      'widthMm',
    ])
  })

  it('decodes exactly the declared read members and knows only declared problem codes', () => {
    const operations = document.paths[route] ?? {}
    expect(operations.get?.responses?.['200']?.content?.['application/json']?.schema?.$ref).toBe(
      '#/components/schemas/ProductDimensionViewV1',
    )
    expect(Object.keys(document.components.schemas.ProductDimensionViewV1?.properties ?? {}).sort()).toEqual([
      'companyId', 'heightMm', 'kgPerMetre', 'kgPerMetreSourceLabel', 'kgPerMetreTableVersion', 'lengthMm', 'outerDiameterMm',
      'productId', 'sectionKind', 'thicknessMm', 'updatedAtUtc', 'updatedBy', 'updatedInCompanyId', 'version', 'wallThicknessMm',
      'widthMm',
    ])
    const declared = new Set(
      Object.values(operations)
        .flatMap((operation) => Object.values(operation.responses ?? {}))
        .flatMap((response) => response['x-erp-problem-codes'] ?? []),
    )
    for (const code of Object.values(DIMENSION_PROBLEM)) expect(declared.has(code), code).toBe(true)
  })
})
