import { describe, expect, it } from 'vitest'
import {
  PRODUCT_CONTRACT_STATUS,
  assertedRoles,
  currentVersionOf,
  decodeCodeGroupList,
  decodeProductCard,
  decodeProductMutation,
  decodeProductPage,
  isComplete,
  knownLifecycle,
  productFieldErrorsOf,
  productProblemCodeOf,
  unevaluatedRoles,
} from './contract'

const PRODUCT_ID = '11111111-1111-1111-1111-111111111111'
const OTHER_ID = '44444444-4444-4444-4444-444444444444'
const GROUP_ID = 'aaaaaaa1-0000-0000-0000-000000000001'
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

function core(over: Record<string, unknown> = {}) {
  return {
    id: PRODUCT_ID,
    code: 'SAC-42',
    name: 'Siyah sac',
    groupId: GROUP_ID,
    classId: null,
    familyId: null,
    roles: roles(),
    lifecycle: 'Draft',
    version: '7',
    createdAtUtc: '2026-01-01T00:00:00+00:00',
    updatedAtUtc: '2026-09-12T07:00:00+00:00',
    labels: { group: 'Sac', class: null, family: null, primaryStockUnit: 'kg' },
    completeness: { evaluation: 'Incomplete', missingFields: ['references.units.stock'] },
    ...over,
  }
}

function references(over: Record<string, unknown> = {}) {
  return {
    templateId: null,
    templateVersion: null,
    attributes: null,
    units: { stock: null, purchase: null, sales: null, price: null, production: null, package: null },
    trackingPolicyId: null,
    ...over,
  }
}

function card(over: Record<string, unknown> = {}) {
  return { product: core(), references: references(), ...over }
}

describe('contract provenance', () => {
  it('declares itself pinned to a published backend source', () => {
    // The marker tells a later reader these names came from the backend contract,
    // not from a business-spec proposal. Losing it has to be a deliberate edit.
    expect(PRODUCT_CONTRACT_STATUS).toBe('PINNED')
  })
})

describe('decodeProductPage', () => {
  it('decodes a well-formed page', () => {
    const page = decodeProductPage({ items: [core()], nextCursor: null })
    expect(page?.items).toHaveLength(1)
    expect(page?.items[0].code).toBe('SAC-42')
    expect(page?.nextCursor).toBeNull()
  })

  it('requires the nextCursor property rather than treating absence as null', () => {
    // An absent property is drift; normalising it would silently accept a server
    // that stopped publishing the only "there is more" signal the page has.
    expect(decodeProductPage({ items: [] })).toBeNull()
  })

  it('rejects a nil or malformed id, and a JS-number version', () => {
    expect(decodeProductPage({ items: [core({ id: NIL })], nextCursor: null })).toBeNull()
    // A revision past 2^53 cannot survive a double; README: "never a JS Number".
    expect(decodeProductPage({ items: [core({ version: 7 })], nextCursor: null })).toBeNull()
    expect(decodeProductPage({ items: [core({ version: '07' })], nextCursor: null })).toBeNull()
  })

  it('rejects a role whose value contradicts its provenance', () => {
    // "Not-evaluated requires null. Explicit false is preserved."
    const badPending = roles({ productionInput: { value: false, provenance: 'not-evaluated' } })
    expect(decodeProductPage({ items: [core({ roles: badPending })], nextCursor: null })).toBeNull()
    const badManual = roles({ sellable: { value: null, provenance: 'manual' } })
    expect(decodeProductPage({ items: [core({ roles: badManual })], nextCursor: null })).toBeNull()
  })

  it('rejects a missing role rather than reading it as not-evaluated', () => {
    const partial = roles()
    delete (partial as Record<string, unknown>).sellable
    expect(decodeProductPage({ items: [core({ roles: partial })], nextCursor: null })).toBeNull()
  })

  it('carries an unrecognised lifecycle instead of rejecting the whole page', () => {
    /**
     * The vocabulary is unchanged — only `Draft/Active/Inactive` are known. What
     * changes is the cost: one odd row must not hide every valid row behind
     * "Beklenmeyen yanıt", which tells the operator nothing.
     */
    const page = decodeProductPage({
      items: [core(), core({ id: OTHER_ID, code: 'BORU-7', lifecycle: 'draft' })],
      nextCursor: null,
    })
    expect(page?.items).toHaveLength(2)
    expect(page?.items[0].lifecycle).toEqual({ known: true, value: 'Draft' })
    expect(page?.items[1].lifecycle).toEqual({ known: false, raw: 'draft' })
    expect(knownLifecycle(page!.items[1].lifecycle)).toBeNull()
  })

  it('still rejects a missing, blank or unprintable lifecycle', () => {
    // Unfamiliar and MALFORMED are different: a control or bidi character means the
    // field is broken, not that it names a state this build has not learned.
    expect(decodeProductPage({ items: [core({ lifecycle: '  ' })], nextCursor: null })).toBeNull()
    expect(decodeProductPage({ items: [core({ lifecycle: null })], nextCursor: null })).toBeNull()
    const bidi = 'Draf' + String.fromCharCode(0x202e) + 't'
    expect(decodeProductPage({ items: [core({ lifecycle: bidi })], nextCursor: null })).toBeNull()
  })

  it('rejects a spoofable code but not a human-written name', () => {
    /**
     * A right-to-left override inside a verbatim-printed value makes the RENDERED
     * text differ from the STORED one: `SAC-42` can be read as `SAC-24`. That is a
     * record-identity confusion. A NAME is text a person wrote — guarding it would
     * make this client stricter than the server's own validator, so a name the
     * server accepted would become a record this screen refuses to show.
     */
    const rlo = String.fromCharCode(0x202e)
    expect(decodeProductPage({ items: [core({ code: 'SAC' + rlo + '-42' })], nextCursor: null })).toBeNull()
    expect(
      decodeProductPage({ items: [core({ name: 'Siyah' + rlo + ' sac' })], nextCursor: null }),
    ).not.toBeNull()
  })

  it('rejects a duplicated id — one product is never two rows', () => {
    expect(decodeProductPage({ items: [core(), core()], nextCursor: null })).toBeNull()
  })

  it('rejects a timestamp with no zone', () => {
    expect(
      decodeProductPage({ items: [core({ updatedAtUtc: '2026-09-12T07:00:00' })], nextCursor: null }),
    ).toBeNull()
  })
})

describe('decodeProductCard', () => {
  it('decodes a well-formed card', () => {
    const decoded = decodeProductCard(card(), PRODUCT_ID)
    expect(decoded?.product.code).toBe('SAC-42')
    expect(decoded?.references.units.stock).toBeNull()
    expect(decoded?.product.completeness.evaluation).toBe('Incomplete')
  })

  it('rejects a card about a different product', () => {
    // A well-formed 200 about another record would have the user looking at one
    // record believing it is another — and any edit aimed at the wrong revision.
    expect(decodeProductCard(card(), OTHER_ID)).toBeNull()
  })

  it('normalises an absent references block rather than rejecting it', () => {
    // Every `ProductReferences` member is optional in the source, so "nothing
    // assigned" is an ordinary state — not drift.
    const decoded = decodeProductCard(card({ references: null }), PRODUCT_ID)
    expect(decoded?.references.units.package).toBeNull()
    expect(decoded?.references.templateId).toBeNull()
  })

  it('rejects a template id without its pinned version, and vice versa', () => {
    /**
     * The source pairs them at every write (`template_pair_required`), so a read
     * carrying one without the other describes a state the domain does not allow —
     * and a version we cannot tell what pins is worse than none.
     */
    expect(
      decodeProductCard(
        card({ references: references({ templateId: OTHER_ID, templateVersion: null }) }),
        PRODUCT_ID,
      ),
    ).toBeNull()
    expect(
      decodeProductCard(
        card({ references: references({ templateId: null, templateVersion: 'v3' }) }),
        PRODUCT_ID,
      ),
    ).toBeNull()
  })

  it('rejects a unit reference that is not a bound uuid', () => {
    const badUnits = references({
      units: { stock: 'kg', purchase: null, sales: null, price: null, production: null, package: null },
    })
    expect(decodeProductCard(card({ references: badUnits }), PRODUCT_ID)).toBeNull()
  })

  it('carries an unknown completeness evaluation without calling it complete', () => {
    // README: "NotEvaluated must not be rendered as complete or activatable." The
    // same restraint applies to a token this build does not know.
    const decoded = decodeProductCard(
      card({ product: core({ completeness: { evaluation: 'Partial', missingFields: [] } }) }),
      PRODUCT_ID,
    )
    expect(decoded?.product.completeness.evaluation).toBeNull()
    expect(decoded?.product.completeness.rawEvaluation).toBe('Partial')
    expect(isComplete(decoded!.product.completeness)).toBe(false)
  })

  it('does not treat NotEvaluated as complete', () => {
    const decoded = decodeProductCard(
      card({ product: core({ completeness: { evaluation: 'NotEvaluated', missingFields: [] } }) }),
      PRODUCT_ID,
    )
    expect(isComplete(decoded!.product.completeness)).toBe(false)
  })
})

describe('decodeProductMutation', () => {
  it('decodes a mutation and reads isReplay from the BODY', () => {
    // `isReplay` is a response field in this contract, not a header.
    const decoded = decodeProductMutation({ card: card(), isReplay: true }, null, 'Draft')
    expect(decoded?.isReplay).toBe(true)
    expect(decoded?.card.product.code).toBe('SAC-42')
  })

  it('rejects a create result that is not a Draft', () => {
    /**
     * A new record is always born Draft and activation is a separate command with
     * its own audit record. Reporting an `Active` create as an ordinary success
     * would hide an activation from the person responsible for it.
     */
    const active = card({ product: core({ lifecycle: 'Active' }) })
    expect(decodeProductMutation({ card: active, isReplay: false }, null, 'Draft')).toBeNull()
  })

  it('rejects an edit result about a different product', () => {
    expect(decodeProductMutation({ card: card(), isReplay: false }, OTHER_ID, 'Draft')).toBeNull()
  })

  it('rejects a missing isReplay flag', () => {
    expect(decodeProductMutation({ card: card() }, null, 'Draft')).toBeNull()
  })
})

describe('decodeCodeGroupList', () => {
  it('decodes a well-formed list', () => {
    const groups = decodeCodeGroupList([{ id: GROUP_ID, name: 'Sac', prefix: 'SAC' }])
    expect(groups).toHaveLength(1)
    expect(groups?.[0].prefix).toBe('SAC')
  })

  it('rejects a non-canonical prefix', () => {
    /**
     * README pins the format: uppercase ASCII [A-Z0-9], 1..32. Offering a group the
     * server would then reject at create time is a dead end presented as a choice —
     * `ÇELİK` is explicitly named as unsupported and owner-correctable.
     */
    expect(decodeCodeGroupList([{ id: GROUP_ID, name: 'Çelik', prefix: 'ÇELİK' }])).toBeNull()
    expect(decodeCodeGroupList([{ id: GROUP_ID, name: 'Sac', prefix: 'sac' }])).toBeNull()
    expect(decodeCodeGroupList([{ id: GROUP_ID, name: 'Sac', prefix: 'SAC-1' }])).toBeNull()
  })

  it('accepts a Unicode display name', () => {
    // The name is human text and stays Unicode; only the PREFIX is ASCII-bound.
    expect(decodeCodeGroupList([{ id: GROUP_ID, name: 'Çelik sac', prefix: 'SAC' }])).not.toBeNull()
  })

  it('rejects a duplicate id', () => {
    const twice = [
      { id: GROUP_ID, name: 'Sac', prefix: 'SAC' },
      { id: GROUP_ID, name: 'Sac 2', prefix: 'SC2' },
    ]
    expect(decodeCodeGroupList(twice)).toBeNull()
  })
})

describe('role helpers', () => {
  it('separates asserted roles from unevaluated ones', () => {
    const decoded = decodeProductPage({ items: [core()], nextCursor: null })
    const r = decoded!.items[0].roles
    expect(assertedRoles(r)).toEqual(['purchasable', 'productionOutput'])
    // `sellable` is a determined NO and must not appear as unevaluated.
    expect(unevaluatedRoles(r)).toEqual(['productionInput'])
  })
})

describe('problem codes', () => {
  it('reads the contract’s own codes', () => {
    expect(productProblemCodeOf({ code: 'version_conflict' })).toBe('version_conflict')
    expect(productProblemCodeOf({ code: 'lifecycle_conflict' })).toBe('lifecycle_conflict')
    expect(productProblemCodeOf({ code: 'invalid_cursor' })).toBe('invalid_cursor')
  })

  it('returns null for an unknown code so the caller falls back to the status', () => {
    expect(productProblemCodeOf({ code: 'product_something_new' })).toBeNull()
    expect(productProblemCodeOf(null)).toBeNull()
  })
})

describe('currentVersionOf', () => {
  it('reads the current version from a version_conflict body', () => {
    expect(currentVersionOf({ code: 'version_conflict', currentVersion: '9' })).toBe('9')
  })

  it('returns null when the server did not say, or said it badly', () => {
    // A refusal we cannot read is still a refusal; the screen asks for a reload
    // rather than guessing a number.
    expect(currentVersionOf({ code: 'version_conflict' })).toBeNull()
    expect(currentVersionOf({ currentVersion: 9 })).toBeNull()
  })
})

describe('productFieldErrorsOf', () => {
  it('reads the {field, code} list the contract sends', () => {
    const body = { errors: [{ field: 'name', code: 'invalid_name' }] }
    expect(productFieldErrorsOf(body)).toEqual([{ field: 'name', code: 'invalid_name' }])
  })

  it('keeps an entry whose code this build does not know', () => {
    // The screen maps it to a general sentence; losing the FIELD binding would be
    // worse than losing message precision.
    const body = { errors: [{ field: 'groupId', code: 'brand_new_code' }] }
    expect(productFieldErrorsOf(body)).toEqual([{ field: 'groupId', code: 'brand_new_code' }])
  })

  it('skips malformed entries and a non-array errors member', () => {
    const body = { errors: [{ field: 'name' }, null, { code: 'x' }, { field: 'ok', code: 'c' }] }
    expect(productFieldErrorsOf(body)).toEqual([{ field: 'ok', code: 'c' }])
    expect(productFieldErrorsOf({ errors: 'nope' })).toEqual([])
  })
})
