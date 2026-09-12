import { describe, expect, it } from 'vitest'
import {
  PRODUCT_CONTRACT_STATUS,
  PRODUCT_COUNT_SCOPE,
  assertedRoles,
  decodeProductDetail,
  decodeProductDraftCreated,
  decodeProductPage,
  knownLifecycle,
  productFieldErrorsOf,
  productProblemCodeOf,
  unevaluatedRoles,
} from './contract'

const PRODUCT_ID = '11111111-1111-1111-1111-111111111111'
const TEMPLATE_ID = '22222222-2222-2222-2222-222222222222'
const OTHER_ID = '44444444-4444-4444-4444-444444444444'
const NIL = '00000000-0000-0000-0000-000000000000'

function roles(over: Record<string, unknown> = {}) {
  return {
    purchasable: { value: true, provenance: 'manual' },
    sellable: { value: false, provenance: 'manual' },
    productionInput: { value: null, provenance: 'not-evaluated' },
    productionOutput: { value: true, provenance: 'derived' },
    ...over,
  }
}

function completeness(over: Record<string, unknown> = {}) {
  return { template: 'complete', unit: 'incomplete', package: 'unknown', ...over }
}

function item(over: Record<string, unknown> = {}) {
  return {
    id: PRODUCT_ID,
    code: 'SAC-42',
    name: 'Siyah sac',
    classification: { class: 'Yassı ürün', group: 'Sac', family: 'Soğuk hadde' },
    roles: roles(),
    lifecycle: 'Active',
    primaryStockUnit: 'kg',
    completeness: completeness(),
    match: { reason: 'code', matchedAlias: null },
    lastChangedAt: '2026-09-12T07:00:00Z',
    version: '7',
    ...over,
  }
}

function page(over: Record<string, unknown> = {}) {
  return {
    items: [item()],
    nextCursor: null,
    hasMore: false,
    totalCount: null,
    countScope: null,
    ...over,
  }
}

function detail(over: Record<string, unknown> = {}) {
  return {
    id: PRODUCT_ID,
    code: 'SAC-42',
    name: 'Siyah sac',
    classification: { class: 'Yassı ürün', group: 'Sac', family: null },
    roles: roles(),
    lifecycle: 'Active',
    completeness: completeness(),
    version: '7',
    aliases: [{ code: 'ESKI-1', retiredAt: '2026-01-02T00:00:00Z' }],
    template: { id: TEMPLATE_ID, name: 'Yassı ürün şablonu', version: 'v3' },
    unitRoles: {
      stock: 'kg',
      purchase: 'kg',
      sales: 'kg',
      price: 'kg',
      production: null,
      package: null,
    },
    trackingPolicy: 'Parti bazlı',
    createdAt: '2026-01-01T00:00:00Z',
    createdBy: 'sinan',
    updatedAt: '2026-09-12T07:00:00Z',
    updatedBy: 'sinan',
    lastActivatedAt: '2026-02-01T00:00:00Z',
    lastDeactivatedAt: null,
    deactivationReason: null,
    availableSections: ['template', 'units', 'aliasHistory'],
    ...over,
  }
}

describe('contract status marker', () => {
  /**
   * The marker is what tells a later reader that these names came from the business
   * spec and not from a merged API. Dropping it when the real DTOs land has to be a
   * deliberate edit, not something that happens by accident during a rename.
   */
  it('still declares the contract as proposed', () => {
    expect(PRODUCT_CONTRACT_STATUS).toBe('PROPOSED_JIT')
  })
})

describe('decodeProductPage', () => {
  it('decodes a well-formed page', () => {
    const decoded = decodeProductPage(page())
    expect(decoded?.items).toHaveLength(1)
    expect(decoded?.items[0].code).toBe('SAC-42')
    expect(decoded?.items[0].version).toBe('7')
  })

  it('rejects a nil or malformed product id', () => {
    expect(decodeProductPage(page({ items: [item({ id: NIL })] }))).toBeNull()
    expect(decodeProductPage(page({ items: [item({ id: 'not-a-uuid' })] }))).toBeNull()
  })

  it('rejects a version that is a number rather than a lossless string', () => {
    // A revision past 2^53 cannot survive a double; a rounded concurrency token is
    // worse than a rejected response.
    expect(decodeProductPage(page({ items: [item({ version: 7 })] }))).toBeNull()
    expect(decodeProductPage(page({ items: [item({ version: '07' })] }))).toBeNull()
  })

  it('carries an unrecognised lifecycle instead of rejecting the whole page', () => {
    /**
     * The vocabulary is unchanged — `Archived` and `draft` are both still NOT one of
     * the three states. What changed is the cost: one odd row used to hide every
     * valid row behind "Beklenmeyen yanıt". #237 currently emits `"draft"`, so this
     * is the difference between an empty list and a list that names the problem.
     */
    const decoded = decodeProductPage(
      page({ items: [item(), item({ id: OTHER_ID, code: 'BORU-7', lifecycle: 'draft' })] }),
    )
    expect(decoded?.items).toHaveLength(2)
    expect(decoded?.items[0].lifecycle).toEqual({ known: true, value: 'Active' })
    // Carried verbatim, never coerced into `Draft`.
    expect(decoded?.items[1].lifecycle).toEqual({ known: false, raw: 'draft' })
    expect(knownLifecycle(decoded!.items[1].lifecycle)).toBeNull()
  })

  it('rejects an unprintable lifecycle token — malformed is not merely unfamiliar', () => {
    /**
     * A bidi override inside a token printed verbatim can make it RENDER as a
     * different word than the one stored, so the operator reads a status the server
     * never sent. Length is already bounded at 64 by the guard; this closes what a
     * bound cannot. Any sane vocabulary is printable, so nothing real lands here.
     */
    const bidi = 'Inac' + String.fromCharCode(0x202e) + 'tive'
    expect(decodeProductPage(page({ items: [item({ lifecycle: bidi })] }))).toBeNull()
    expect(
      decodeProductPage(page({ items: [item({ lifecycle: 'dr' + String.fromCharCode(0) + 'aft' })] })),
    ).toBeNull()
    expect(
      decodeProductPage(page({ items: [item({ lifecycle: 'dra' + String.fromCharCode(0x200b) + 'ft' })] })),
    ).toBeNull()
    // ...and an ordinary unfamiliar token is still carried, not rejected.
    expect(decodeProductPage(page({ items: [item({ lifecycle: 'archived' })] }))).not.toBeNull()
  })

  it('rejects a spoofable code, alias or unit but NOT a human-written name', () => {
    /**
     * Where the line falls. A bidi override inside a value printed verbatim makes
     * the RENDERED text differ from the STORED one, so `SAC-42` can be read as
     * `SAC-24` — a record-identity confusion, worse than a status one.
     *
     * It applies to tokens the SERVER mints (code, alias, unit, pinned version) and
     * NOT to text a PERSON wrote. Guarding a product name would make this client
     * stricter than the server's own validator: a name the server accepted would
     * become a record this screen refuses to show, which is the worse failure.
     */
    const rlo = String.fromCharCode(0x202e)
    expect(decodeProductPage(page({ items: [item({ code: 'SAC' + rlo + '-42' })] }))).toBeNull()
    expect(decodeProductPage(page({ items: [item({ primaryStockUnit: 'k' + rlo + 'g' })] }))).toBeNull()
    expect(
      decodeProductPage(
        page({ items: [item({ match: { reason: 'alias', matchedAlias: 'ESK' + rlo + 'I-1' } })] }),
      ),
    ).toBeNull()

    // ...and the human-written fields are left alone.
    const decoded = decodeProductPage(page({ items: [item({ name: 'Siyah' + rlo + ' sac' })] }))
    expect(decoded?.items).toHaveLength(1)
  })

  it('rejects a lifecycle token longer than the bound', () => {
    expect(decodeProductPage(page({ items: [item({ lifecycle: 'x'.repeat(65) })] }))).toBeNull()
    expect(decodeProductPage(page({ items: [item({ lifecycle: 'x'.repeat(64) })] }))).not.toBeNull()
  })

  it('still rejects a missing or blank lifecycle', () => {
    // An absent field is a different fact from an unfamiliar one: there is nothing
    // to carry and nothing to show.
    expect(decodeProductPage(page({ items: [item({ lifecycle: '  ' })] }))).toBeNull()
    expect(decodeProductPage(page({ items: [item({ lifecycle: null })] }))).toBeNull()
    expect(decodeProductPage(page({ items: [item({ lifecycle: 3 })] }))).toBeNull()
  })

  it('rejects a role whose value contradicts its provenance', () => {
    // not-evaluated must carry null...
    expect(
      decodeProductPage(
        page({ items: [item({ roles: roles({ productionInput: { value: false, provenance: 'not-evaluated' } }) })] }),
      ),
    ).toBeNull()
    // ...and a determined role must carry a boolean.
    expect(
      decodeProductPage(
        page({ items: [item({ roles: roles({ sellable: { value: null, provenance: 'manual' } }) })] }),
      ),
    ).toBeNull()
  })

  it('rejects a missing role rather than reading it as not-evaluated', () => {
    const partial = roles()
    delete (partial as Record<string, unknown>).sellable
    expect(decodeProductPage(page({ items: [item({ roles: partial })] }))).toBeNull()
  })

  it('rejects an alias match that does not name the alias it matched', () => {
    expect(
      decodeProductPage(page({ items: [item({ match: { reason: 'alias', matchedAlias: null } })] })),
    ).toBeNull()
    // ...and a non-alias match that carries one anyway.
    expect(
      decodeProductPage(page({ items: [item({ match: { reason: 'code', matchedAlias: 'ESKI-1' } })] })),
    ).toBeNull()
  })

  it('accepts an alias match that names the retired code', () => {
    const decoded = decodeProductPage(
      page({ items: [item({ match: { reason: 'alias', matchedAlias: 'ESKI-1' } })] }),
    )
    expect(decoded?.items[0].match).toEqual({ reason: 'alias', matchedAlias: 'ESKI-1' })
  })

  it('rejects a duplicated product id — one product is never two rows', () => {
    expect(decodeProductPage(page({ items: [item(), item()] }))).toBeNull()
  })

  it('rejects a count that was not asked for', () => {
    expect(
      decodeProductPage(page({ totalCount: '12', countScope: PRODUCT_COUNT_SCOPE }), false),
    ).toBeNull()
    expect(
      decodeProductPage(page({ totalCount: '12', countScope: PRODUCT_COUNT_SCOPE }), true),
    ).not.toBeNull()
  })

  it('rejects a count without its scope, and an unknown scope', () => {
    expect(decodeProductPage(page({ totalCount: '12', countScope: null }), true)).toBeNull()
    expect(decodeProductPage(page({ totalCount: '12', countScope: 'all-products' }), true)).toBeNull()
  })

  it('rejects an absent count property rather than normalising it to null', () => {
    const body = page() as Record<string, unknown>
    delete body.totalCount
    expect(decodeProductPage(body)).toBeNull()
  })

  it('rejects a hasMore/nextCursor pair that disagrees', () => {
    expect(decodeProductPage(page({ hasMore: true, nextCursor: null }))).toBeNull()
    expect(decodeProductPage(page({ hasMore: false, nextCursor: 'abc' }))).toBeNull()
  })

  it('rejects a timestamp with no zone', () => {
    expect(decodeProductPage(page({ items: [item({ lastChangedAt: '2026-09-12T07:00:00' })] }))).toBeNull()
  })
})

describe('decodeProductDetail', () => {
  it('decodes a well-formed detail', () => {
    const decoded = decodeProductDetail(detail(), PRODUCT_ID)
    expect(decoded?.aliases).toHaveLength(1)
    expect(decoded?.template?.version).toBe('v3')
    expect(decoded?.unitRoles.production).toBeNull()
  })

  it('rejects a response about a different product', () => {
    // A well-formed 200 about another record would have the user looking at one
    // product believing it is another.
    expect(decodeProductDetail(detail(), '33333333-3333-3333-3333-333333333333')).toBeNull()
  })

  it('rejects an alias that equals the live code', () => {
    expect(
      decodeProductDetail(detail({ aliases: [{ code: 'SAC-42', retiredAt: '2026-01-02T00:00:00Z' }] }), PRODUCT_ID),
    ).toBeNull()
  })

  it('rejects a duplicated alias — a retired code is not reusable', () => {
    const twice = [
      { code: 'ESKI-1', retiredAt: '2026-01-02T00:00:00Z' },
      { code: 'ESKI-1', retiredAt: '2026-03-02T00:00:00Z' },
    ]
    expect(decodeProductDetail(detail({ aliases: twice }), PRODUCT_ID)).toBeNull()
  })

  it('rejects a Draft that claims it was activated', () => {
    expect(
      decodeProductDetail(detail({ lifecycle: 'Draft', lastActivatedAt: '2026-02-01T00:00:00Z' }), PRODUCT_ID),
    ).toBeNull()
  })

  it('rejects a spoofable alias code or pinned template version on the detail', () => {
    const rlo = String.fromCharCode(0x202e)
    expect(
      decodeProductDetail(
        detail({ aliases: [{ code: 'ESK' + rlo + 'I-1', retiredAt: '2026-01-02T00:00:00Z' }] }),
        PRODUCT_ID,
      ),
    ).toBeNull()
    expect(
      decodeProductDetail(
        detail({ template: { id: TEMPLATE_ID, name: 'Şablon', version: 'v' + rlo + '3' } }),
        PRODUCT_ID,
      ),
    ).toBeNull()
    // A template NAME is human text and is not guarded.
    expect(
      decodeProductDetail(
        detail({ template: { id: TEMPLATE_ID, name: 'Şab' + rlo + 'lon', version: 'v3' } }),
        PRODUCT_ID,
      ),
    ).not.toBeNull()
  })

  it('skips the consistency checks for a status it does not understand', () => {
    /**
     * Both checks ask whether two statements about a KNOWN state agree. We cannot
     * judge whether a deactivation reason belongs to a status we do not understand,
     * so the pair is carried as sent rather than given an invented verdict.
     */
    const decoded = decodeProductDetail(
      detail({ lifecycle: 'archived', deactivationReason: 'arşivlendi' }),
      PRODUCT_ID,
    )
    expect(decoded?.lifecycle).toEqual({ known: false, raw: 'archived' })
    expect(decoded?.deactivationReason).toBe('arşivlendi')
  })

  it('rejects a non-Inactive product carrying a deactivation reason', () => {
    expect(decodeProductDetail(detail({ deactivationReason: 'hatalı' }), PRODUCT_ID)).toBeNull()
  })

  it('accepts an Inactive product with its reason', () => {
    const decoded = decodeProductDetail(
      detail({
        lifecycle: 'Inactive',
        deactivationReason: 'Üretimden kaldırıldı',
        lastDeactivatedAt: '2026-08-01T00:00:00Z',
      }),
      PRODUCT_ID,
    )
    expect(decoded?.deactivationReason).toBe('Üretimden kaldırıldı')
  })

  it('rejects an unknown or duplicated available section', () => {
    expect(decodeProductDetail(detail({ availableSections: ['pricing'] }), PRODUCT_ID)).toBeNull()
    expect(
      decodeProductDetail(detail({ availableSections: ['units', 'units'] }), PRODUCT_ID),
    ).toBeNull()
  })

  it('rejects a missing unit role rather than defaulting it', () => {
    const partial = { stock: 'kg', purchase: 'kg', sales: 'kg', price: 'kg', production: null }
    expect(decodeProductDetail(detail({ unitRoles: partial }), PRODUCT_ID)).toBeNull()
  })

  it('accepts a null template but rejects a malformed one', () => {
    expect(decodeProductDetail(detail({ template: null }), PRODUCT_ID)?.template).toBeNull()
    expect(decodeProductDetail(detail({ template: { id: TEMPLATE_ID, name: 'X' } }), PRODUCT_ID)).toBeNull()
  })
})

describe('decodeProductDraftCreated', () => {
  const created = {
    id: PRODUCT_ID,
    code: 'SAC-42',
    name: 'Siyah sac',
    lifecycle: 'Draft',
    version: '1',
  }

  it('decodes a Draft creation', () => {
    expect(decodeProductDraftCreated(created)?.code).toBe('SAC-42')
  })

  it('rejects a 201 that says the product was created Active', () => {
    /**
     * §6.1/§6.2: a new record is always born Draft and activation is a separate
     * command with its own audit record. Reporting an `Active` 201 as an ordinary
     * success would hide an activation from the person responsible for it.
     */
    expect(decodeProductDraftCreated({ ...created, lifecycle: 'Active' })).toBeNull()
  })

  it('rejects a missing or non-lossless version', () => {
    expect(decodeProductDraftCreated({ ...created, version: 1 })).toBeNull()
    expect(decodeProductDraftCreated({ ...created, version: '0' })).toBeNull()
  })
})

describe('role helpers', () => {
  it('separates asserted roles from unevaluated ones', () => {
    const decoded = decodeProductPage(page())
    const decodedRoles = decoded!.items[0].roles
    expect(assertedRoles(decodedRoles)).toEqual(['purchasable', 'productionOutput'])
    // `sellable` is a determined NO and must not appear as unevaluated.
    expect(unevaluatedRoles(decodedRoles)).toEqual(['productionInput'])
  })
})

describe('problem codes', () => {
  it('reads a known platform or product code', () => {
    expect(productProblemCodeOf({ code: 'forbidden' })).toBe('forbidden')
    expect(productProblemCodeOf({ code: 'invalid_cursor' })).toBe('invalid_cursor')
  })

  it('returns null for an unknown code so the caller falls back to the status', () => {
    // The Product codes are proposed, so a server code this build has never heard
    // of is expected rather than exceptional.
    expect(productProblemCodeOf({ code: 'product_something_new' })).toBeNull()
    expect(productProblemCodeOf(null)).toBeNull()
  })
})

describe('productFieldErrorsOf', () => {
  const known = ['name', 'productClass']

  it('keeps only errors for fields the form renders', () => {
    const body = { errors: { name: 'Zorunlu', secretField: 'x' } }
    expect(productFieldErrorsOf(body, known)).toEqual({ name: 'Zorunlu' })
  })

  it('reads the first message when the server sends an array', () => {
    const body = { errors: { productClass: ['', 'Sınıf gerekli'] } }
    expect(productFieldErrorsOf(body, known)).toEqual({ productClass: 'Sınıf gerekli' })
  })

  it('returns nothing for a body with no errors object', () => {
    expect(productFieldErrorsOf({ detail: 'x' }, known)).toEqual({})
  })
})
