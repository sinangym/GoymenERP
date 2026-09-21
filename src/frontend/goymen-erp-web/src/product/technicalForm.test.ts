import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { decodeReferenceOptions } from './contract'
import { referenceOptions, FAMILY_FLAT, GROUP_SAC, installFetch, renderProductApp, json,
  productCard, productCore, mutation, references } from './testHarness'
import { PRODUCT_CAPABILITY } from './capabilities'
import { resetPendingAttempts } from './pendingAttempt'
import { technicalDraft, technicalWrite } from './technicalForm'
import { validateCreateForm, EMPTY_CREATE_FORM } from './createForm'

export const TEMPLATE = '81000000-0000-4000-8000-000000000001'
export const KG = '81000000-0000-4000-8000-000000000002'
export const PIECE = '81000000-0000-4000-8000-000000000003'
export function technicalOptionsWire() {
  const field = { role: null, required: true, dictionary: null, allowsCandidate: false,
    appliesWhenField: null, appliesWhenCode: null, parentField: null }
  return referenceOptions({
    templates: [{ id: TEMPLATE, version: '1', displayName: 'Sentetik sac şablonu', fields: [
      { ...field, key: 'thickness', kind: 'measureMillimetre' },
      { ...field, key: 'coating', kind: 'dictionaryReference', dictionary: 'coatings' },
    ] }],
    familyFacts: [{ id: FAMILY_FLAT, code: 'TEST', displayName: 'Sentetik aile', cins: 'Metal', tur: 'Sac', tip: 'Düz', templateId: TEMPLATE, templateVersion: '1' }],
    dictionaries: [{ name: 'coatings', entries: [
      { code: 'NONE', displayName: 'Kaplamasız', status: 'approved', parentCode: null, isExplicitNone: true, massGm2: null },
      { code: 'NEW', displayName: 'Yeni kaplama', status: 'candidate', parentCode: null, isExplicitNone: false, massGm2: 125.5 },
    ] }],
    units: [{ id: KG, displayName: 'Kilogram', symbol: 'kg', isKilogram: true },
      { id: PIECE, displayName: 'Adet', symbol: 'adet', isKilogram: false }],
  })
}

describe('released technical fields', () => {
  const options = decodeReferenceOptions(technicalOptionsWire(), GROUP_SAC)!
  it('decodes actual field kinds, explicit none, candidates and Settings unit identities', () => {
    expect(options.technical?.templates[0].fields).toHaveLength(2)
    expect(options.technical?.dictionaries[0].entries[0].isExplicitNone).toBe(true)
    expect(options.technical?.units[0].id).toBe(KG)
  })
  it('rejects unknown field kinds and duplicate unit identities', () => {
    const raw = technicalOptionsWire()
    expect(decodeReferenceOptions({ ...raw, units: [{ id: KG, displayName: 'kg', symbol: 'kg', isKilogram: true },
      { id: KG, displayName: 'kg', symbol: 'kg', isKilogram: true }] }, GROUP_SAC)).toBeNull()
    expect(decodeReferenceOptions({ ...raw, templates: [{ id: TEMPLATE, version: '1', displayName: 'Test', fields: [{ key: 'x', kind: 'unknown' }] }] }, GROUP_SAC)).toBeNull()
  })
  it('rejects missing family labels and missing or malformed nullable decimal mass', () => {
    const missingCode = technicalOptionsWire()
    delete (missingCode.familyFacts[0] as unknown as Record<string, unknown>).code
    expect(decodeReferenceOptions(missingCode, GROUP_SAC)).toBeNull()

    const missingDisplayName = technicalOptionsWire()
    delete (missingDisplayName.familyFacts[0] as unknown as Record<string, unknown>).displayName
    expect(decodeReferenceOptions(missingDisplayName, GROUP_SAC)).toBeNull()

    const missingMass = technicalOptionsWire()
    delete (missingMass.dictionaries[0].entries[0] as unknown as Record<string, unknown>).massGm2
    expect(decodeReferenceOptions(missingMass, GROUP_SAC)).toBeNull()

    for (const malformed of ['01.5', '1e3', '', Number.NaN, Number.POSITIVE_INFINITY]) {
      const raw = technicalOptionsWire()
      ;(raw.dictionaries[0].entries[0] as unknown as Record<string, unknown>).massGm2 = malformed
      expect(decodeReferenceOptions(raw, GROUP_SAC)).toBeNull()
    }

    const losslessDecimal = technicalOptionsWire()
    ;(losslessDecimal.dictionaries[0].entries[0] as unknown as Record<string, unknown>).massGm2 = '125.50'
    expect(decodeReferenceOptions(losslessDecimal, GROUP_SAC)).not.toBeNull()
  })
  it('writes explicit none, measured attributes and independently selected units into the POST payload', () => {
    const result = validateCreateForm({ ...EMPTY_CREATE_FORM, name: 'Sentetik ürün', groupId: GROUP_SAC, familyId: FAMILY_FLAT,
      technical: { ...technicalDraft(), attributes: { thickness: '1,250', coating: 'NONE' }, attributesChanged: true,
        stock: KG, stockChanged: true, sales: PIECE, salesChanged: true } }, [GROUP_SAC], [], [FAMILY_FLAT], options)
    expect(result.ok && result.payload.references).toEqual({ templateId: TEMPLATE, templateVersion: '1',
      attributes: { thickness: 1.25, coating: 'NONE' }, units: { stock: KG, sales: PIECE } })
  })
  it('refuses a candidate in a required field and a non-kg stock unit', () => {
    const result = technicalWrite({ ...technicalDraft(), attributes: { coating: 'NEW' }, attributesChanged: true,
      stock: PIECE, stockChanged: true }, options, FAMILY_FLAT)
    expect(result.ok).toBe(false)
    if (!result.ok) expect(Object.keys(result.errors)).toEqual(['references.attributes.coating', 'references.units.stock'])
  })
  it('preserves untouched references when only a unit is changed', () => {
    const result = technicalWrite({ ...technicalDraft(), attributes: { historicUnknown: 'old' }, sales: PIECE, salesChanged: true }, options, FAMILY_FLAT)
    expect(result).toEqual({ ok: true, references: { units: { sales: PIECE } } })
  })
  it('never erases unknown attributes while editing another attribute', () => {
    expect(technicalWrite({ ...technicalDraft(), attributes: { historicUnknown: 'old', thickness: '2' }, attributesChanged: true }, options, FAMILY_FLAT).ok).toBe(false)
  })
  it('refuses unavailable releases and overprecise measures', () => {
    const draft = { ...technicalDraft(), attributes: { thickness: '1.2345' }, attributesChanged: true }
    expect(technicalWrite(draft, options, FAMILY_FLAT).ok).toBe(false)
    expect(technicalWrite(draft, null, FAMILY_FLAT).ok).toBe(false)
    expect(technicalWrite({ ...draft, attributes: { thickness: '1' }, sourceTemplateId: TEMPLATE,
      sourceTemplateVersion: '2' }, options, FAMILY_FLAT).ok).toBe(false)
  })
})

afterEach(() => { cleanup(); resetPendingAttempts(); vi.unstubAllGlobals(); vi.restoreAllMocks() })

it('renders the released fields on the normal create screen and sends them in POST', async () => {
  const { calls } = installFetch({ capabilities: [PRODUCT_CAPABILITY.productsCreate],
    onOptions: () => json(technicalOptionsWire()) })
  renderProductApp('/urun/yeni')
  await screen.findByRole('heading', { name: 'Yeni ürün' })
  await userEvent.type(screen.getByLabelText(/Ürün adı/), 'Sentetik teknik ürün')
  await screen.findByRole('option', { name: /Sac \(SAC\)/ })
  await userEvent.selectOptions(screen.getByLabelText(/Kod grubu/), GROUP_SAC)
  await screen.findByRole('option', { name: 'Soğuk hadde' })
  await userEvent.selectOptions(screen.getByLabelText('Ürün ailesi', { exact: false }), FAMILY_FLAT)
  await userEvent.type(screen.getByLabelText(/thickness/), '1.25')
  await userEvent.selectOptions(screen.getByLabelText(/coating/), 'NONE')
  expect((screen.getByRole('option', { name: 'Yeni kaplama (Aday)' }) as HTMLOptionElement).disabled).toBe(true)
  await userEvent.selectOptions(screen.getByLabelText('Stok birimi (kg)'), KG)
  await userEvent.selectOptions(screen.getByLabelText('Satış birimi'), PIECE)
  await userEvent.click(screen.getByRole('button', { name: 'Taslak olarak kaydet' }))
  await screen.findByRole('heading', { name: 'Taslak kaydedildi' })
  const post = calls.find(c => c.method === 'POST')!
  expect(post.idempotencyKey).toBeTruthy()
  expect(JSON.parse(post.body!).references).toEqual({ templateId: TEMPLATE, templateVersion: '1',
    attributes: { thickness: 1.25, coating: 'NONE' }, units: { stock: KG, sales: PIECE } })
})

it('edits the loaded technical value with the read version, then reopens server-resolved names', async () => {
  const original = productCard({ product: productCore({ familyId: FAMILY_FLAT }),
    references: references({ templateId: TEMPLATE, templateVersion: '1', attributes: { thickness: 1.25, coating: 'NONE' } }) })
  const id = String(original.product.id)
  let saved = original
  const { calls } = installFetch({ capabilities: [PRODUCT_CAPABILITY.productsEdit, PRODUCT_CAPABILITY.productsDetail],
    onOptions: () => json(technicalOptionsWire()), onCard: () => json(saved),
    onEdit: (_attempt, body) => {
      const patch = JSON.parse(body!)
      saved = productCard({ product: productCore({ familyId: FAMILY_FLAT, version: '8', labels: {
        group: 'Sac', class: null, family: 'Soğuk hadde', primaryStockUnit: null, template: 'Sentetik sac şablonu',
        familyFacts: { cins: 'Metal', tur: 'Sac', tip: 'Düz' }, attributes: { coating: 'Kaplamasız' },
      } }), references: { ...original.references, ...patch.changes.references } })
      return json(mutation({ card: saved }))
    } })
  const app = renderProductApp(`/urun/${id}/duzenle`)
  const thickness = await screen.findByLabelText(/thickness/)
  await userEvent.clear(thickness)
  await userEvent.type(thickness, '2.5')
  await userEvent.type(screen.getByLabelText('Değişiklik gerekçesi'), 'Teknik ölçü düzeltildi')
  await userEvent.click(screen.getByRole('button', { name: 'Değişiklikleri kaydet' }))
  await waitFor(() => expect(calls.some(c => c.method === 'PATCH')).toBe(true))
  const patch = JSON.parse(calls.find(c => c.method === 'PATCH')!.body!)
  expect(patch.expectedVersion).toBe('7')
  expect(patch.changes.references.attributes).toEqual({ thickness: 2.5, coating: 'NONE' })
  await waitFor(() => expect(screen.queryByText('Değişiklikler kaydedildi')).toBeTruthy())
  await app.router.navigate(`/urun/${id}`)
  await screen.findByText('Kaplamasız')
  expect(screen.getByText('2.5')).toBeTruthy()
  expect(screen.getByText('Metal')).toBeTruthy()
})
