/**
 * BS-9 component cases — the product card's "Ebat/kalınlık" panel. COMPONENT/DOM EVIDENCE ONLY: `fetch` is stubbed with
 * synthetic answers; the real API and SQL are the Product unit tests and the price list relational acceptance tests. What these
 * cases establish: a forbidden read shows nothing, an unset row says "kaynak yok", each failed read says what failed, the
 * command sends the company, one Idempotency-Key and the exact body with only the kind's measures, keeps the key for a retry
 * after an unknown outcome, never rounds a measure, and the route and body are pinned to the canonical OpenAPI document.
 * KLN-D48/D54 (STK-S37): the new section families keep only their own fields, a structural profile is set by series and size
 * with no typed kg/m and shows the table weight with its source and version, or "kaynak yok" — never an estimate.
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
  meshSpacingMm: null, kgPerMetre: null,
}

const EMPTY_TEXTS = { profileSeries: null, profileSize: null, surfaceFinish: null }

const NO_WEIGHT = { standardKgPerMetre: null, kgPerMetreSource: null, weightTableVersion: null, weightTableSourceLabel: null }

function payload(overrides: Record<string, unknown> = {}) {
  return {
    companyId: COMPANY, productId: PRODUCT, version: '2', sectionKind: 'Sheet', ...EMPTY_MEASURES, ...EMPTY_TEXTS, ...NO_WEIGHT,
    thicknessMm: 2, widthMm: 1250, lengthMm: 2500, updatedAtUtc: '2026-11-12T08:00:00Z', updatedBy: 'u-1',
    updatedInCompanyId: COMPANY, ...overrides,
  }
}

const UNSET = {
  companyId: COMPANY, productId: PRODUCT, version: '0', sectionKind: null, ...EMPTY_MEASURES, ...EMPTY_TEXTS, ...NO_WEIGHT,
  updatedAtUtc: null, updatedBy: null, updatedInCompanyId: null,
}

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
      expectedVersion: '2', sectionKind: 'RoundTube', ...EMPTY_MEASURES, outerDiameterMm: 48.3, wallThicknessMm: 2.6, ...EMPTY_TEXTS,
      reason: 'SENTETIK',
    })
    expect(writes[0].headers['X-Company-Id']).toBe(COMPANY)
    expect(writes[0].headers['Idempotency-Key']).toMatch(/.+/)
    expect(writes[1].headers['Idempotency-Key']).toBe(writes[0].headers['Idempotency-Key'])
  })

  it('refuses a missing, over-precise or too thick measure before sending, and reads the server refusal', async () => {
    const user = userEvent.setup()
    const sent = install((method) =>
      method === 'GET' ? json(UNSET) : json({ code: DIMENSION_PROBLEM.measureInvalid }, 400))
    renderPanel()

    await user.click(await screen.findByRole('button', { name: DIMENSION_TEXT.change }))
    const kind = screen.getByRole('combobox', { name: DIMENSION_TEXT.kindLabel }) as HTMLSelectElement
    expect(kind.value).toBe('')
    expect((screen.getByRole('button', { name: DIMENSION_TEXT.save }) as HTMLButtonElement).disabled).toBe(true)

    await user.selectOptions(kind, 'Profile')
    await user.type(screen.getByRole('textbox', { name: DIMENSION_TEXT.reasonLabel }), 'SENTETIK')
    await user.type(screen.getByRole('textbox', { name: MEASURE_LABEL.widthMm }), '40')
    await user.type(screen.getByRole('textbox', { name: MEASURE_LABEL.heightMm }), '20')
    await user.type(screen.getByRole('textbox', { name: MEASURE_LABEL.wallThicknessMm }), '2')
    await user.click(screen.getByRole('button', { name: DIMENSION_TEXT.save }))
    expect(await screen.findByText(DIMENSION_TEXT.measureMissing)).toBeTruthy()

    await user.type(screen.getByRole('textbox', { name: MEASURE_LABEL.kgPerMetre }), '1.1234567')
    await user.click(screen.getByRole('button', { name: DIMENSION_TEXT.save }))
    expect(await screen.findByText(DIMENSION_TEXT.measureFormat)).toBeTruthy()

    await user.clear(screen.getByRole('textbox', { name: MEASURE_LABEL.kgPerMetre }))
    await user.type(screen.getByRole('textbox', { name: MEASURE_LABEL.kgPerMetre }), '1.75')
    await user.clear(screen.getByRole('textbox', { name: MEASURE_LABEL.wallThicknessMm }))
    await user.type(screen.getByRole('textbox', { name: MEASURE_LABEL.wallThicknessMm }), '10.5')
    await user.click(screen.getByRole('button', { name: DIMENSION_TEXT.save }))
    expect(await screen.findByText(DIMENSION_TEXT.wallTooThick)).toBeTruthy()
    expect(sent.some((request) => request.method === 'PUT')).toBe(false)

    await user.clear(screen.getByRole('textbox', { name: MEASURE_LABEL.wallThicknessMm }))
    await user.type(screen.getByRole('textbox', { name: MEASURE_LABEL.wallThicknessMm }), '2')
    await user.click(screen.getByRole('button', { name: DIMENSION_TEXT.save }))
    expect(await screen.findByText(DIMENSION_TEXT.measureInvalid)).toBeTruthy()
    expect(sent.filter((request) => request.method === 'PUT').map((request) => request.body)).toEqual([
      {
        expectedVersion: '0', sectionKind: 'Profile', ...EMPTY_MEASURES, widthMm: 40, heightMm: 20, wallThicknessMm: 2, kgPerMetre: 1.75,
        ...EMPTY_TEXTS, reason: 'SENTETIK',
      },
    ])
  })

  it('shows a structural profile with its table weight, source and version, or "kaynak yok" without a table row (KLN-D54)', async () => {
    const structural = {
      ...EMPTY_MEASURES, sectionKind: 'StructuralProfile', lengthMm: 12000, profileSeries: 'IPE', profileSize: '200',
    }
    install(() => json(payload({
      ...structural, standardKgPerMetre: 22.4, kgPerMetreSource: 'StandardTable', weightTableVersion: '3',
      weightTableSourceLabel: 'SENTETIK katalog',
    })))
    renderPanel()
    expect(await screen.findByText(SECTION_KIND_LABEL.StructuralProfile)).toBeTruthy()
    expect(screen.getByText('IPE 200')).toBeTruthy()
    expect(screen.getByTestId('product-dimension-weight-standard').textContent).toContain('22.4')
    expect(screen.getByText('SENTETIK katalog')).toBeTruthy()
    expect(screen.getByText(DIMENSION_TEXT.standardKgTable)).toBeTruthy()
    expect(screen.queryByTestId('product-dimension-weight-no-source')).toBeNull()
    cleanup()
    vi.unstubAllGlobals()

    install(() => json(payload({ ...structural, kgPerMetreSource: 'NoSource' })))
    renderPanel()
    expect((await screen.findByTestId('product-dimension-weight-no-source')).textContent).toContain(DIMENSION_TEXT.standardKgNoSource)
    expect(screen.queryByTestId('product-dimension-weight-standard')).toBeNull()
  })

  it('sets a structural profile by series and size with no typed kg/m, and refuses a malformed size before sending', async () => {
    const user = userEvent.setup()
    const sent = install((method) => (method === 'GET' ? json(UNSET) : new Response(null, { status: 204 })))
    renderPanel()

    await user.click(await screen.findByRole('button', { name: DIMENSION_TEXT.change }))
    await user.selectOptions(screen.getByRole('combobox', { name: DIMENSION_TEXT.kindLabel }), 'StructuralProfile')
    expect(screen.queryByRole('textbox', { name: MEASURE_LABEL.kgPerMetre })).toBeNull()
    expect(screen.getByText(DIMENSION_TEXT.standardKgNote)).toBeTruthy()
    await user.selectOptions(screen.getByRole('combobox', { name: DIMENSION_TEXT.profileSeriesLabel }), 'HEA')
    await user.type(screen.getByRole('textbox', { name: DIMENSION_TEXT.profileSizeLabel }), '100a')
    await user.type(screen.getByRole('textbox', { name: MEASURE_LABEL.lengthMm }), '12000')
    await user.type(screen.getByRole('textbox', { name: DIMENSION_TEXT.reasonLabel }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: DIMENSION_TEXT.save }))
    expect(await screen.findByText(DIMENSION_TEXT.profileInvalid)).toBeTruthy()
    expect(sent.some((request) => request.method === 'PUT')).toBe(false)

    await user.clear(screen.getByRole('textbox', { name: DIMENSION_TEXT.profileSizeLabel }))
    await user.type(screen.getByRole('textbox', { name: DIMENSION_TEXT.profileSizeLabel }), '100A')
    await user.click(screen.getByRole('button', { name: DIMENSION_TEXT.save }))
    expect(await screen.findByText(DIMENSION_TEXT.saved)).toBeTruthy()
    expect(sent.filter((request) => request.method === 'PUT').map((request) => request.body)).toEqual([
      {
        expectedVersion: '0', sectionKind: 'StructuralProfile', ...EMPTY_MEASURES, lengthMm: 12000, ...EMPTY_TEXTS,
        profileSeries: 'HEA', profileSize: '100A', reason: 'SENTETIK',
      },
    ])
  })

  it('requires the surface of a painted ERW profile and refuses a wire not thinner than the mesh spacing', async () => {
    const user = userEvent.setup()
    const sent = install((method) => (method === 'GET' ? json(UNSET) : new Response(null, { status: 204 })))
    renderPanel()

    await user.click(await screen.findByRole('button', { name: DIMENSION_TEXT.change }))
    const kind = screen.getByRole('combobox', { name: DIMENSION_TEXT.kindLabel })
    await user.selectOptions(kind, 'WeldedMesh')
    await user.type(screen.getByRole('textbox', { name: MEASURE_LABEL.widthMm }), '2150')
    await user.type(screen.getByRole('textbox', { name: MEASURE_LABEL.lengthMm }), '5000')
    await user.type(screen.getByRole('textbox', { name: 'Tel çapı (mm)' }), '8')
    await user.type(screen.getByRole('textbox', { name: MEASURE_LABEL.meshSpacingMm }), '8')
    await user.type(screen.getByRole('textbox', { name: DIMENSION_TEXT.reasonLabel }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: DIMENSION_TEXT.save }))
    expect(await screen.findByText(DIMENSION_TEXT.wireTooThick)).toBeTruthy()

    await user.selectOptions(kind, 'PaintedErwProfile')
    await user.type(screen.getByRole('textbox', { name: MEASURE_LABEL.heightMm }), '40')
    await user.type(screen.getByRole('textbox', { name: MEASURE_LABEL.wallThicknessMm }), '2')
    await user.type(screen.getByRole('textbox', { name: MEASURE_LABEL.kgPerMetre }), '3.45')
    await user.clear(screen.getByRole('textbox', { name: MEASURE_LABEL.widthMm }))
    await user.type(screen.getByRole('textbox', { name: MEASURE_LABEL.widthMm }), '60')
    await user.click(screen.getByRole('button', { name: DIMENSION_TEXT.save }))
    expect(await screen.findByText(DIMENSION_TEXT.measureMissing)).toBeTruthy()
    expect(sent.some((request) => request.method === 'PUT')).toBe(false)

    await user.type(screen.getByRole('textbox', { name: DIMENSION_TEXT.surfaceFinishLabel }), 'RAL 7016 boyalı')
    await user.click(screen.getByRole('button', { name: DIMENSION_TEXT.save }))
    expect(await screen.findByText(DIMENSION_TEXT.saved)).toBeTruthy()
    // The mesh's wire diameter, spacing and length do not leave with the profile.
    expect(sent.filter((request) => request.method === 'PUT').map((request) => request.body)).toEqual([
      {
        expectedVersion: '0', sectionKind: 'PaintedErwProfile', ...EMPTY_MEASURES, widthMm: 60, heightMm: 40, wallThicknessMm: 2,
        kgPerMetre: 3.45, ...EMPTY_TEXTS, surfaceFinish: 'RAL 7016 boyalı', reason: 'SENTETIK',
      },
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
    // A round tube's kg/m is optional, a profile's is required.
    const tube = { ...EMPTY_MEASURES, sectionKind: 'RoundTube', outerDiameterMm: 48.3, wallThicknessMm: 2.6 }
    expect(decodeDimension(payload(tube), COMPANY, PRODUCT)).not.toBeNull()
    expect(decodeDimension(payload({ ...tube, kgPerMetre: 2.93, kgPerMetreSource: 'Card' }), COMPANY, PRODUCT)).not.toBeNull()
    // A card value names its source "Card"; without one no source is named.
    expect(decodeDimension(payload({ ...tube, kgPerMetre: 2.93 }), COMPANY, PRODUCT)).toBeNull()
    expect(decodeDimension(payload({ ...tube, kgPerMetreSource: 'Card' }), COMPANY, PRODUCT)).toBeNull()
    const profile = { ...EMPTY_MEASURES, sectionKind: 'Profile', widthMm: 40, heightMm: 20, wallThicknessMm: 2 }
    expect(decodeDimension(payload(profile), COMPANY, PRODUCT)).toBeNull()
    expect(decodeDimension(payload({ ...profile, kgPerMetre: 1.75, kgPerMetreSource: 'Card' }), COMPANY, PRODUCT)).not.toBeNull()
  })

  it('fails closed on a new family row off its shape and on a structural profile weight without its source (KLN-D48/D54)', () => {
    const structural = {
      ...EMPTY_MEASURES, sectionKind: 'StructuralProfile', lengthMm: 12000, profileSeries: 'IPE', profileSize: '200',
    }
    const standard = { standardKgPerMetre: 22.4, kgPerMetreSource: 'StandardTable', weightTableVersion: '3', weightTableSourceLabel: 'SENTETIK katalog' }
    expect(decodeDimension(payload({ ...structural, ...standard }), COMPANY, PRODUCT)?.weight).toEqual({
      standardKgPerMetre: 22.4, source: 'StandardTable', tableVersion: '3', tableSourceLabel: 'SENTETIK katalog',
    })
    expect(decodeDimension(payload({ ...structural, kgPerMetreSource: 'NoSource' }), COMPANY, PRODUCT)?.weight.source).toBe('NoSource')
    // A structural profile never holds a typed kg/m, always names a source, and a table value names its table.
    expect(decodeDimension(payload({ ...structural, ...standard, kgPerMetre: 22.4 }), COMPANY, PRODUCT)).toBeNull()
    expect(decodeDimension(payload(structural), COMPANY, PRODUCT)).toBeNull()
    expect(decodeDimension(payload({ ...structural, ...standard, weightTableVersion: null }), COMPANY, PRODUCT)).toBeNull()
    expect(decodeDimension(payload({ ...structural, kgPerMetreSource: 'NoSource', standardKgPerMetre: 22.4 }), COMPANY, PRODUCT)).toBeNull()
    expect(decodeDimension(payload({ ...structural, ...standard, profileSeries: 'W' }), COMPANY, PRODUCT)).toBeNull()
    expect(decodeDimension(payload({ ...structural, ...standard, profileSize: '200 ' }), COMPANY, PRODUCT)).toBeNull()
    expect(decodeDimension(payload({ ...structural, ...standard, profileSize: null }), COMPANY, PRODUCT)).toBeNull()
    // A sheet names no table and no structural designation.
    expect(decodeDimension(payload(standard), COMPANY, PRODUCT)).toBeNull()
    expect(decodeDimension(payload({ profileSeries: 'IPE' }), COMPANY, PRODUCT)).toBeNull()
    // Welded mesh needs its spacing; a painted ERW tube needs its surface; a plain ERW tube holds none.
    const mesh = { ...EMPTY_MEASURES, sectionKind: 'WeldedMesh', widthMm: 2150, lengthMm: 5000, outerDiameterMm: 5 }
    expect(decodeDimension(payload({ ...mesh, meshSpacingMm: 150 }), COMPANY, PRODUCT)).not.toBeNull()
    expect(decodeDimension(payload(mesh), COMPANY, PRODUCT)).toBeNull()
    const erw = { ...EMPTY_MEASURES, sectionKind: 'ErwTube', outerDiameterMm: 60.3, wallThicknessMm: 2 }
    expect(decodeDimension(payload(erw), COMPANY, PRODUCT)).not.toBeNull()
    expect(decodeDimension(payload({ ...erw, surfaceFinish: 'RAL 7016' }), COMPANY, PRODUCT)).toBeNull()
    expect(decodeDimension(payload({ ...erw, sectionKind: 'PaintedErwTube' }), COMPANY, PRODUCT)).toBeNull()
    expect(decodeDimension(payload({ ...erw, sectionKind: 'PaintedErwTube', surfaceFinish: 'RAL 7016' }), COMPANY, PRODUCT)).not.toBeNull()
    expect(decodeDimension(payload({ ...erw, sectionKind: 'PaintedErwTube', surfaceFinish: ' RAL 7016' }), COMPANY, PRODUCT)).toBeNull()
  })

  it('reads a typed measure without rounding it', () => {
    expect(parseMeasure('thicknessMm', '')).toEqual({ ok: true, value: null })
    expect(parseMeasure('thicknessMm', ' 1,5 ')).toEqual({ ok: true, value: 1.5 })
    expect(parseMeasure('thicknessMm', '1.125')).toEqual({ ok: true, value: 1.125 })
    expect(parseMeasure('thicknessMm', '1.1255')).toEqual({ ok: false })
    expect(parseMeasure('kgPerMetre', '1.123456')).toEqual({ ok: true, value: 1.123456 })
    expect(parseMeasure('kgPerMetre', '1.1234567')).toEqual({ ok: false })
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
      'expectedVersion', 'heightMm', 'kgPerMetre', 'lengthMm', 'meshSpacingMm', 'outerDiameterMm', 'profileSeries', 'profileSize',
      'reason', 'sectionKind', 'surfaceFinish', 'thicknessMm', 'wallThicknessMm', 'widthMm',
    ])
  })

  it('decodes exactly the declared read members and knows only declared problem codes', () => {
    const operations = document.paths[route] ?? {}
    expect(operations.get?.responses?.['200']?.content?.['application/json']?.schema?.$ref).toBe(
      '#/components/schemas/ProductDimensionViewV1',
    )
    expect(Object.keys(document.components.schemas.ProductDimensionViewV1?.properties ?? {}).sort()).toEqual([
      'companyId', 'heightMm', 'kgPerMetre', 'kgPerMetreSource', 'lengthMm', 'meshSpacingMm', 'outerDiameterMm', 'productId',
      'profileSeries', 'profileSize', 'sectionKind', 'standardKgPerMetre', 'surfaceFinish', 'thicknessMm', 'updatedAtUtc',
      'updatedBy', 'updatedInCompanyId', 'version', 'wallThicknessMm', 'weightTableSourceLabel', 'weightTableVersion', 'widthMm',
    ])
    const declared = new Set(
      Object.values(operations)
        .flatMap((operation) => Object.values(operation.responses ?? {}))
        .flatMap((response) => response['x-erp-problem-codes'] ?? []),
    )
    for (const code of Object.values(DIMENSION_PROBLEM)) expect(declared.has(code), code).toBe(true)
  })
})
