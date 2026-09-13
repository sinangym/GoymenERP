import { describe, expect, it } from 'vitest'
import {
  addLine,
  emptyOrderDraftForm,
  isOrderDraftPristine,
  lineFieldId,
  ORDER_FIELD,
  removeLine,
  setLineProduct,
  validateOrderDraft,
  type OrderDraftFormValues,
  type OrderDraftOptions,
} from './orderDraftForm'
import { ORDER_DRAFT_TEXT } from './salesText'

// Stand-ins for the spec's `<placeholder>` values. Not fixtures of any environment.
const PARTY = '3f2a9c10-5b7e-4d21-9a0c-6e8f1b2c3d41'
const ACCOUNT_A = '3f2a9c10-5b7e-4d21-9a0c-6e8f1b2c3d42'
const PASSIVE_ACCOUNT = '3f2a9c10-5b7e-4d21-9a0c-6e8f1b2c3d43'
const CURRENCY = '3f2a9c10-5b7e-4d21-9a0c-6e8f1b2c3d44'
const PRODUCT = '3f2a9c10-5b7e-4d21-9a0c-6e8f1b2c3d45'
const DRAFT_PRODUCT = '3f2a9c10-5b7e-4d21-9a0c-6e8f1b2c3d46'
const KG = '3f2a9c10-5b7e-4d21-9a0c-6e8f1b2c3d47'
const PIECE = '3f2a9c10-5b7e-4d21-9a0c-6e8f1b2c3d48'
const OTHER_PRODUCT = '3f2a9c10-5b7e-4d21-9a0c-6e8f1b2c3d49'

const OPTIONS: OrderDraftOptions = {
  accounts: [{ partyId: PARTY, companyAccountId: ACCOUNT_A }],
  currencyIds: [CURRENCY],
  productIds: [PRODUCT, OTHER_PRODUCT],
  unitIdsByProduct: { [PRODUCT]: [KG], [OTHER_PRODUCT]: [PIECE] },
}

function filled(over: Partial<OrderDraftFormValues> = {}): OrderDraftFormValues {
  return {
    partyId: PARTY,
    companyAccountId: ACCOUNT_A,
    businessDate: '2026-09-13',
    currencyId: CURRENCY,
    lines: [
      {
        key: 'l1',
        productId: PRODUCT,
        quantityText: '1000',
        orderUnitId: KG,
        requestedDeliveryDate: '2026-09-20',
      },
    ],
    ...over,
  }
}

describe('validateOrderDraft — the spec §3 logical example', () => {
  it('produces exactly the example body, and nothing the server owns', () => {
    const result = validateOrderDraft(filled(), OPTIONS)
    expect(result.ok).toBe(true)
    if (!result.ok) return
    // Deep equality on the whole object: an extra member (companyId, actor, version,
    // status, number, revision) fails this just like a wrong value does.
    expect(result.payload).toEqual({
      kind: 'Order',
      partyId: PARTY,
      companyAccountId: ACCOUNT_A,
      businessDate: '2026-09-13',
      currencyId: CURRENCY,
      lines: [
        { productId: PRODUCT, quantity: '1000', orderUnitId: KG, requestedDeliveryDate: '2026-09-20' },
      ],
    })
  })

  it('sends an unknown due date as null, never as today', () => {
    const form = filled()
    const result = validateOrderDraft(
      { ...form, lines: [{ ...form.lines[0], requestedDeliveryDate: '' }] },
      OPTIONS,
    )
    expect(result.ok).toBe(true)
    if (result.ok) expect(result.payload.lines[0].requestedDeliveryDate).toBeNull()
  })

  it('sends the quantity as canonical decimal text', () => {
    const form = filled()
    const result = validateOrderDraft(
      { ...form, lines: [{ ...form.lines[0], quantityText: '1000,50' }] },
      OPTIONS,
    )
    expect(result.ok).toBe(true)
    if (result.ok) expect(result.payload.lines[0].quantity).toBe('1000.5')
  })
})

describe('validateOrderDraft — references are chosen, never assumed', () => {
  it('refuses an account the projection does not offer (passive, or another company)', () => {
    const result = validateOrderDraft(filled({ companyAccountId: PASSIVE_ACCOUNT }), OPTIONS)
    expect(result.ok).toBe(false)
    if (!result.ok) {
      expect(result.errors[ORDER_FIELD.customer]).toBe(ORDER_DRAFT_TEXT.validation.customerNotOffered)
    }
  })

  it('refuses a party paired with an account that is not offered for it', () => {
    // The party IS offered, but only with another account: the pair is what is chosen.
    const result = validateOrderDraft(filled(), {
      ...OPTIONS,
      accounts: [{ partyId: PARTY, companyAccountId: PASSIVE_ACCOUNT }],
    })
    expect(result.ok).toBe(false)
    if (!result.ok) {
      expect(result.errors[ORDER_FIELD.customer]).toBe(ORDER_DRAFT_TEXT.validation.customerNotOffered)
    }
  })

  it('refuses a product the owner does not report as usable (a Draft, for one)', () => {
    const form = filled()
    const result = validateOrderDraft(
      { ...form, lines: [{ ...form.lines[0], productId: DRAFT_PRODUCT }] },
      OPTIONS,
    )
    expect(result.ok).toBe(false)
    if (!result.ok) {
      expect(result.errors[lineFieldId('l1', 'productId')]).toBe(
        ORDER_DRAFT_TEXT.validation.productNotOffered,
      )
      // One message per cause: the unit is not judged against a product nobody offered.
      expect(result.errors[lineFieldId('l1', 'orderUnitId')]).toBeUndefined()
    }
  })

  it('refuses a unit the chosen product does not offer, and never assumes kg', () => {
    const form = filled()
    const result = validateOrderDraft(
      { ...form, lines: [{ ...form.lines[0], orderUnitId: PIECE }] },
      OPTIONS,
    )
    expect(result.ok).toBe(false)
    if (!result.ok) {
      expect(result.errors[lineFieldId('l1', 'orderUnitId')]).toBe(
        ORDER_DRAFT_TEXT.validation.orderUnitNotOffered,
      )
    }
  })

  it('treats an empty projection as nothing offered, not as anything allowed', () => {
    const empty: OrderDraftOptions = { accounts: [], currencyIds: [], productIds: [], unitIdsByProduct: {} }
    const result = validateOrderDraft(filled(), empty)
    expect(result.ok).toBe(false)
    if (!result.ok) {
      expect(Object.keys(result.errors)).toEqual([
        ORDER_FIELD.customer,
        ORDER_FIELD.currency,
        lineFieldId('l1', 'productId'),
      ])
    }
  })
})

describe('validateOrderDraft — required fields and dates', () => {
  it('lists every missing field, header first and then line by line', () => {
    const result = validateOrderDraft(emptyOrderDraftForm('l1'), OPTIONS)
    expect(result.ok).toBe(false)
    if (!result.ok) {
      expect(Object.keys(result.errors)).toEqual([
        ORDER_FIELD.customer,
        ORDER_FIELD.businessDate,
        ORDER_FIELD.currency,
        lineFieldId('l1', 'productId'),
        lineFieldId('l1', 'quantity'),
        lineFieldId('l1', 'orderUnitId'),
      ])
    }
  })

  it('needs at least one line', () => {
    const result = validateOrderDraft(filled({ lines: [] }), OPTIONS)
    expect(result.ok).toBe(false)
    if (!result.ok) expect(result.errors[ORDER_FIELD.lines]).toBe(ORDER_DRAFT_TEXT.validation.linesRequired)
  })

  it('refuses dates that are not on the calendar', () => {
    const form = filled({ businessDate: '2026-02-29' })
    const result = validateOrderDraft(
      { ...form, lines: [{ ...form.lines[0], requestedDeliveryDate: '2026-04-31' }] },
      OPTIONS,
    )
    expect(result.ok).toBe(false)
    if (!result.ok) {
      expect(result.errors[ORDER_FIELD.businessDate]).toBe(ORDER_DRAFT_TEXT.validation.dateInvalid)
      expect(result.errors[lineFieldId('l1', 'requestedDeliveryDate')]).toBe(
        ORDER_DRAFT_TEXT.validation.dateInvalid,
      )
    }
  })

  it('surfaces the ambiguous thousands separator instead of guessing', () => {
    const form = filled()
    const result = validateOrderDraft(
      { ...form, lines: [{ ...form.lines[0], quantityText: '1.000' }] },
      OPTIONS,
    )
    expect(result.ok).toBe(false)
    if (!result.ok) {
      expect(result.errors[lineFieldId('l1', 'quantity')]).toBe(
        ORDER_DRAFT_TEXT.validation.quantityAmbiguous,
      )
    }
  })
})

describe('line editing', () => {
  it('keeps each line identity stable across add, edit and remove', () => {
    let form = addLine(filled(), 'l2')
    form = setLineProduct(form, 'l2', OTHER_PRODUCT)
    form = removeLine(form, 'l1')
    expect(form.lines.map((line) => line.key)).toEqual(['l2'])
    expect(form.lines[0].productId).toBe(OTHER_PRODUCT)
  })

  it('clears the unit when the product changes, and only then', () => {
    const changed = setLineProduct(filled(), 'l1', OTHER_PRODUCT)
    expect(changed.lines[0].orderUnitId).toBe('')
    const same = setLineProduct(filled(), 'l1', PRODUCT)
    expect(same.lines[0].orderUnitId).toBe(KG)
  })

  it('is pristine only while nothing was typed or chosen', () => {
    expect(isOrderDraftPristine(emptyOrderDraftForm('l1'))).toBe(true)
    expect(isOrderDraftPristine(addLine(emptyOrderDraftForm('l1'), 'l2'))).toBe(true)
    const typed = emptyOrderDraftForm('l1')
    expect(
      isOrderDraftPristine({ ...typed, lines: [{ ...typed.lines[0], quantityText: '5' }] }),
    ).toBe(false)
    expect(isOrderDraftPristine({ ...typed, businessDate: '2026-09-13' })).toBe(false)
  })
})
