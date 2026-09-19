import { describe, expect, it } from 'vitest'
import createRequestRaw from './examples/create-request.json?raw'
import {
  ORDER_FIELD,
  addLine,
  addSlice,
  browserIds,
  emptyOrderDraftForm,
  formFieldForServerPath,
  isOrderDraftPristine,
  lineFieldId,
  linePlanHint,
  removeLine,
  removeSlice,
  setAccount,
  setLineProduct,
  sliceFieldId,
  updateSlice,
  validateOrderDraft,
  type DeliveryReferenceOptions,
  type OrderDraftFormValues,
  type OrderDraftOptions,
} from './orderDraftForm'
import { ORDER_DRAFT_TEXT } from './salesText'

// The ids of #257's create-request.json. Synthetic schema values, not fixtures.
const PARTY = '11111111-1111-1111-1111-111111111111'
const ACCOUNT = '55555555-5555-5555-5555-555555555555'
const CURRENCY = '33333333-3333-3333-3333-333333333333'
const PRODUCT = '22222222-2222-2222-2222-222222222222'
const KG = '44444444-4444-4444-4444-444444444444'
const LINE = '66666666-6666-6666-6666-666666666666'
const SLICE_1 = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'
const SLICE_2 = 'cccccccc-cccc-cccc-cccc-cccccccccccc'
const OTHER_ACCOUNT = '55555555-5555-5555-5555-555555555556'
const DRAFT_PRODUCT = '22222222-2222-2222-2222-222222222223'
const OTHER_PRODUCT = '22222222-2222-2222-2222-222222222224'
const PIECE = '44444444-4444-4444-4444-444444444445'
// SALES-A2a: the Cari owner's contact information of the example account (synthetic).
const ADDRESS = 'aaaaaaaa-1111-4111-8111-000000000401'
const CONTACT = 'bbbbbbbb-1111-4111-8111-000000000401'
const DELIVERY: DeliveryReferenceOptions = {
  addresses: [{ id: ADDRESS, version: '4', purpose: 'delivery', text: 'Sanayi Cad. 1, Kocaeli, TR' }],
  contacts: [{ id: CONTACT, version: '2', displayName: 'Ayşe Yükleme', jobTitle: null, purpose: 'delivery' }],
}

const OPTIONS: OrderDraftOptions = {
  accounts: [{ partyId: PARTY, companyAccountId: ACCOUNT, label: 'Cari A' }],
  currencies: [{ currencyId: CURRENCY, label: 'TRY' }],
  products: [
    { productId: PRODUCT, productVersion: '7', label: 'P', orderUnits: [{ unitId: KG, label: 'kg' }] },
    { productId: OTHER_PRODUCT, productVersion: '3', label: 'Q', orderUnits: [{ unitId: PIECE, label: 'adet' }] },
  ],
}

/** The form a person fills to produce #257's example request. */
function exampleForm(over: Partial<OrderDraftFormValues> = {}): OrderDraftFormValues {
  return {
    partyId: PARTY,
    companyAccountId: ACCOUNT,
    businessDate: '2026-09-13',
    currencyId: CURRENCY,
    requestedDeliveryDate: '',
    note: '',
    lines: [
      {
        lineId: LINE,
        productId: PRODUCT,
        quantityText: '1000',
        orderUnitId: KG,
        requestedDeliveryDate: '2026-09-20',
        note: '',
        slices: [
          { sliceId: SLICE_1, quantityText: '400', requestedDeliveryDate: '2026-09-20' },
          { sliceId: SLICE_2, quantityText: '600', requestedDeliveryDate: '2026-09-22' },
        ],
      },
    ],
    deliveryAddressId: '',
    contactId: '',
    ...over,
  }
}

function withSlices(quantities: readonly string[]): OrderDraftFormValues {
  const form = exampleForm()
  return {
    ...form,
    lines: [
      {
        ...form.lines[0],
        slices: quantities.map((quantityText, i) => ({
          sliceId: `bbbbbbbb-bbbb-bbbb-bbbb-00000000000${i + 1}`,
          quantityText,
          requestedDeliveryDate: '2026-09-20',
        })),
      },
    ],
  }
}

describe('validateOrderDraft — consumes #257 create-request.json', () => {
  it('produces exactly the example body: every member present, unknowns as null', () => {
    const result = validateOrderDraft(exampleForm(), OPTIONS)
    expect(result.ok).toBe(true)
    if (!result.ok) return
    // Deep equality with the Backend's own example, including explicit nulls and the
    // absence of company, actor, version, state and approval members.
    expect(result.payload).toEqual(JSON.parse(createRequestRaw))
    // …and the same member order, so the serialized snapshot is the example's shape.
    expect(Object.keys(result.payload)).toEqual(Object.keys(JSON.parse(createRequestRaw)))
  })

  it('carries the offered product version as expectedProductVersion', () => {
    const result = validateOrderDraft(exampleForm(), {
      ...OPTIONS,
      products: [{ ...OPTIONS.products[0], productVersion: '8' }],
    })
    expect(result.ok && result.payload.lines[0].expectedProductVersion).toBe('8')
  })

  it('sends notes verbatim, a blank note as null, and never price or discount', () => {
    const form = exampleForm({ note: '  Tır ile  ' })
    const result = validateOrderDraft(
      { ...form, lines: [{ ...form.lines[0], note: '   ', slices: null }] },
      OPTIONS,
    )
    expect(result.ok).toBe(true)
    if (!result.ok) return
    expect(result.payload.note).toBe('  Tır ile  ')
    expect(result.payload.lines[0]).toMatchObject({
      note: null,
      unitPrice: null,
      priceUnitId: null,
      discountPercent: null,
      requestedDeliverySlices: null,
    })
  })
})

describe('delivery slices — the #254 §5 golden in the form', () => {
  it('1000 = 400 + 600 is accepted and leaves 0 unplanned', () => {
    expect(validateOrderDraft(withSlices(['400', '600']), OPTIONS).ok).toBe(true)
    expect(linePlanHint(withSlices(['400', '600']).lines[0])).toEqual({ unplanned: '0' })
  })

  it('400 + 700 is refused on the line before anything is sent', () => {
    const result = validateOrderDraft(withSlices(['400', '700']), OPTIONS)
    expect(result.ok).toBe(false)
    if (!result.ok) {
      expect(result.errors[lineFieldId(LINE, 'slices')]).toBe(ORDER_DRAFT_TEXT.validation.slicesExceedOrder)
    }
    expect(linePlanHint(withSlices(['400', '700']).lines[0])).toEqual({ exceeds: true })
  })

  it('400 + 400 is accepted and shows 200 unplanned — not delivered', () => {
    expect(validateOrderDraft(withSlices(['400', '400']), OPTIONS).ok).toBe(true)
    expect(linePlanHint(withSlices(['400', '400']).lines[0])).toEqual({ unplanned: '200' })
  })

  it('gives every slice the line unit, and requires a real slice date', () => {
    const form = withSlices(['400'])
    const undated = updateSlice(form, LINE, form.lines[0].slices![0].sliceId, { requestedDeliveryDate: '' })
    const result = validateOrderDraft(undated, OPTIONS)
    expect(result.ok).toBe(false)
    if (!result.ok) {
      expect(result.errors[sliceFieldId(LINE, form.lines[0].slices![0].sliceId, 'requestedDeliveryDate')]).toBe(
        ORDER_DRAFT_TEXT.validation.sliceDateRequired,
      )
    }
    const ok = validateOrderDraft(form, OPTIONS)
    expect(ok.ok && ok.payload.lines[0].requestedDeliverySlices?.[0].orderUnitId).toBe(KG)
  })

  it('removing the last slice returns to an unknown plan, not "no planned slices"', () => {
    let form = addSlice(exampleForm({ lines: [{ ...exampleForm().lines[0], slices: null }] }), LINE, SLICE_1)
    expect(form.lines[0].slices).toHaveLength(1)
    form = removeSlice(form, LINE, SLICE_1)
    expect(form.lines[0].slices).toBeNull()
  })
})

describe('references are chosen, never assumed', () => {
  it('refuses an account pair the source does not offer (passive, or another company)', () => {
    const result = validateOrderDraft(exampleForm({ companyAccountId: OTHER_ACCOUNT }), OPTIONS)
    expect(!result.ok && result.errors[ORDER_FIELD.customer]).toBe(ORDER_DRAFT_TEXT.validation.customerNotOffered)
  })

  it('refuses a product the owner did not report usable, without a second unit message', () => {
    const form = exampleForm()
    const result = validateOrderDraft({ ...form, lines: [{ ...form.lines[0], productId: DRAFT_PRODUCT }] }, OPTIONS)
    expect(result.ok).toBe(false)
    if (!result.ok) {
      expect(result.errors[lineFieldId(LINE, 'productId')]).toBe(ORDER_DRAFT_TEXT.validation.productNotOffered)
      expect(result.errors[lineFieldId(LINE, 'orderUnitId')]).toBeUndefined()
    }
  })

  it('refuses a unit the chosen product does not offer, and never assumes kg', () => {
    const form = exampleForm()
    const result = validateOrderDraft({ ...form, lines: [{ ...form.lines[0], orderUnitId: PIECE }] }, OPTIONS)
    expect(!result.ok && result.errors[lineFieldId(LINE, 'orderUnitId')]).toBe(
      ORDER_DRAFT_TEXT.validation.orderUnitNotOffered,
    )
  })

  it('treats an empty source as nothing offered', () => {
    const result = validateOrderDraft(exampleForm(), { accounts: [], currencies: [], products: [] })
    expect(!result.ok && Object.keys(result.errors)).toEqual([
      ORDER_FIELD.customer,
      ORDER_FIELD.currency,
      lineFieldId(LINE, 'productId'),
    ])
  })
})

describe('required fields, dates and bounds', () => {
  it('lists every missing field, header first then line by line', () => {
    const result = validateOrderDraft(emptyOrderDraftForm(LINE), OPTIONS)
    expect(!result.ok && Object.keys(result.errors)).toEqual([
      ORDER_FIELD.customer,
      ORDER_FIELD.businessDate,
      ORDER_FIELD.currency,
      lineFieldId(LINE, 'productId'),
      lineFieldId(LINE, 'quantity'),
      lineFieldId(LINE, 'orderUnitId'),
    ])
  })

  it('needs at least one line', () => {
    const result = validateOrderDraft(exampleForm({ lines: [] }), OPTIONS)
    expect(!result.ok && result.errors[ORDER_FIELD.lines]).toBe(ORDER_DRAFT_TEXT.validation.linesRequired)
  })

  it('refuses off-calendar dates and an over-long note', () => {
    const result = validateOrderDraft(
      exampleForm({ businessDate: '2026-02-29', requestedDeliveryDate: '2026-04-31', note: 'x'.repeat(4001) }),
      OPTIONS,
    )
    expect(result.ok).toBe(false)
    if (!result.ok) {
      expect(result.errors[ORDER_FIELD.businessDate]).toBe(ORDER_DRAFT_TEXT.validation.dateInvalid)
      expect(result.errors[ORDER_FIELD.requestedDeliveryDate]).toBe(ORDER_DRAFT_TEXT.validation.dateInvalid)
      expect(result.errors[ORDER_FIELD.note]).toBe(ORDER_DRAFT_TEXT.validation.noteTooLong)
    }
  })

  it('surfaces the ambiguous thousands separator instead of guessing', () => {
    const form = exampleForm()
    const result = validateOrderDraft({ ...form, lines: [{ ...form.lines[0], quantityText: '1.000' }] }, OPTIONS)
    expect(!result.ok && result.errors[lineFieldId(LINE, 'quantity')]).toBe(ORDER_DRAFT_TEXT.validation.quantityAmbiguous)
  })
})

describe('line editing and server paths', () => {
  it('mints distinct v4 UUIDs without needing a secure context', () => {
    const original = globalThis.crypto.randomUUID
    // Simulate a plain-HTTP host, where randomUUID is absent.
    Object.defineProperty(globalThis.crypto, 'randomUUID', { value: undefined, configurable: true })
    try {
      const ids = new Set(Array.from({ length: 50 }, () => browserIds()))
      expect(ids.size).toBe(50)
      for (const id of ids) expect(id).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/)
    } finally {
      Object.defineProperty(globalThis.crypto, 'randomUUID', { value: original, configurable: true })
    }
  })

  it('keeps line identities stable across add, product change and remove', () => {
    let form = addLine(exampleForm(), 'line-2')
    form = setLineProduct(form, 'line-2', OTHER_PRODUCT)
    form = removeLine(form, LINE)
    expect(form.lines.map((line) => line.lineId)).toEqual(['line-2'])
  })

  it('clears the unit when the product changes, and only then', () => {
    expect(setLineProduct(exampleForm(), LINE, OTHER_PRODUCT).lines[0].orderUnitId).toBe('')
    expect(setLineProduct(exampleForm(), LINE, PRODUCT).lines[0].orderUnitId).toBe(KG)
  })

  it('is pristine only while nothing was typed, chosen or planned', () => {
    expect(isOrderDraftPristine(emptyOrderDraftForm(LINE))).toBe(true)
    expect(isOrderDraftPristine(addSlice(emptyOrderDraftForm(LINE), LINE, SLICE_1))).toBe(false)
    expect(isOrderDraftPristine({ ...emptyOrderDraftForm(LINE), note: 'x' })).toBe(false)
  })

  it('places a server field path on the SENT line, not on the form as it is now', () => {
    const sent = [{ lineId: LINE }, { lineId: 'line-2' }]
    expect(formFieldForServerPath('lines[1].productId', sent)).toBe(lineFieldId('line-2', 'productId'))
    expect(formFieldForServerPath('lines[0].expectedProductVersion', sent)).toBe(lineFieldId(LINE, 'productId'))
    expect(formFieldForServerPath('lines[0].requestedDeliverySlices', sent)).toBe(lineFieldId(LINE, 'slices'))
    expect(formFieldForServerPath('companyAccountId', sent)).toBe(ORDER_FIELD.customer)
    expect(formFieldForServerPath('lines[5].quantity', sent)).toBeNull()
    expect(formFieldForServerPath('idempotencyKey', sent)).toBeNull()
  })
})

describe('SALES-A2a — delivery address and contact are chosen from the owner, and optional', () => {
  it('sends the chosen references with the owner versions, and null when nothing is chosen', () => {
    const chosen = validateOrderDraft(exampleForm({ deliveryAddressId: ADDRESS, contactId: CONTACT }), OPTIONS, DELIVERY)
    expect(chosen.ok).toBe(true)
    if (!chosen.ok) return
    expect(chosen.payload.deliveryAddress).toEqual({ id: ADDRESS, expectedVersion: '4' })
    expect(chosen.payload.contact).toEqual({ id: CONTACT, expectedVersion: '2' })
    // No address text travels: the owner's identity and version only.
    expect(JSON.stringify(chosen.payload)).not.toContain('Sanayi')
    const none = validateOrderDraft(exampleForm(), OPTIONS, DELIVERY)
    expect(none.ok && none.payload.deliveryAddress).toBeNull()
    expect(none.ok && none.payload.contact).toBeNull()
    expect(none.ok && none.payload.responsible).toBeNull()
  })

  it('refuses a reference the owner does not list for the account, or one chosen before the list answered', () => {
    const unknown = validateOrderDraft(exampleForm({ deliveryAddressId: CONTACT, contactId: ADDRESS }), OPTIONS, DELIVERY)
    expect(!unknown.ok && unknown.errors[ORDER_FIELD.deliveryAddress]).toBe(ORDER_DRAFT_TEXT.validation.deliveryAddressNotOffered)
    expect(!unknown.ok && unknown.errors[ORDER_FIELD.contact]).toBe(ORDER_DRAFT_TEXT.validation.contactNotOffered)
    const early = validateOrderDraft(exampleForm({ deliveryAddressId: ADDRESS }), OPTIONS, null)
    expect(!early.ok && Object.keys(early.errors)).toEqual([ORDER_FIELD.deliveryAddress])
  })

  it('choosing another account clears both; the same account keeps them', () => {
    const form = exampleForm({ deliveryAddressId: ADDRESS, contactId: CONTACT })
    expect(setAccount(form, PARTY, ACCOUNT)).toBe(form)
    const other = setAccount(form, PARTY, OTHER_ACCOUNT)
    expect([other.companyAccountId, other.deliveryAddressId, other.contactId]).toEqual([OTHER_ACCOUNT, '', ''])
  })

  it('is not pristine with a chosen reference, and places the server paths on the selects', () => {
    expect(isOrderDraftPristine({ ...emptyOrderDraftForm(LINE), contactId: CONTACT })).toBe(false)
    expect(formFieldForServerPath('deliveryAddress', [])).toBe(ORDER_FIELD.deliveryAddress)
    expect(formFieldForServerPath('contact', [])).toBe(ORDER_FIELD.contact)
  })
})
