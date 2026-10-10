import { describe, expect, it } from 'vitest'
import { decodeInventoryStockPositions } from './contract'
import { decodeStockDisplayPage, decodeStockLocationDisplayPage, decodeStockOwnerDisplayPage } from './stockDisplayContract'

/**
 * Fixtures are the #597 producer's own values (contract pin d821e6b8):
 * - the row and Product display of `InventoryStockPositionHttpAcceptanceTests.Positions()/ProductDisplays()`
 *   ("ROLL-1", quantities 10/2/8/1/7, Product "P-1 Galvaniz bant" version 7, 1.5 × 1250 mm), serialized the way the
 *   host does it (`thicknessMm`/`widthMm` are JSON numbers, `nextCursor` present and null on an exhausted page);
 * - the Product owner's rejections from `ProductStockDisplayReferenceService` (`productId`/`invalid_reference`,
 *   identity and technical null) and `ProductStockDisplayReferenceTests` (template mismatch: identity kept,
 *   technical null, `references.templateId`/`family_template_mismatch`).
 */
const COMPANY = '10000000-0000-4000-8000-0000000000a1'
const LABEL = '22222222-2222-4222-8222-222222222222'
const LOT = '33333333-3333-4333-8333-333333333333'
const LOCATION = '44444444-4444-4444-8444-444444444444'
const PRODUCT = '55555555-5555-4555-8555-555555555555'
const FAMILY = '66666666-6666-4666-8666-666666666666'
const TEMPLATE = '77777777-7777-4777-8777-777777777777'

function productDisplay() {
  return {
    productId: PRODUCT,
    expectedProductVersion: '7',
    identity: { productId: PRODUCT, productVersion: '7', code: 'P-1', name: 'Galvaniz bant' },
    technical: {
      family: { familyId: FAMILY, familyCode: 'coil', sourceRevision: '#66/stock-display' },
      template: { templateId: TEMPLATE, templateVersion: '1', templateCode: 'coil-v1', sourceRevision: '#66/stock-display' },
      thicknessMm: 1.5,
      widthMm: 1250,
    },
    errors: [] as { field: string; code: string }[],
  }
}

function row(overrides: Record<string, unknown> = {}) {
  return {
    companyId: COMPANY, labelId: LABEL, labelVersion: '3', labelCode: 'ROLL-1', productId: PRODUCT,
    productVersion: '7', classification: 'Standard',
    lot: { id: LOT, heatNumber: 'HEAT-1', coilNumber: 'COIL-1' }, heatNumber: 'HEAT-1', coilNumber: 'COIL-1',
    documentStatus: 'Complete', ownerKind: 'Company', ownerId: COMPANY, custodianKind: 'Company', custodianId: COMPANY,
    locationId: LOCATION, physicalKg: '10', freePhysicalKg: '8', blockedPhysicalKg: '2', activeAllocationKg: '1',
    availableKg: '7', productDisplay: productDisplay(), ...overrides,
  }
}

function page(overrides: Record<string, unknown> = {}) {
  return { items: [row()], searchWindowFull: false, nextCursor: null, ...overrides }
}

/** The previously published (no-header) shape of the same row: 19 keys, no Product display. */
function legacyRow() {
  const { productVersion: _version, classification: _classification, productDisplay: _display, ...legacy } = row()
  return legacy
}

describe('decodeStockDisplayPage — #597 opt-in page', () => {
  it('decodes the producer fixture exactly, keeping every quantity as the wire text', () => {
    const decoded = decodeStockDisplayPage(page())
    expect(decoded).not.toBeNull()
    expect(decoded!.nextCursor).toBeNull()
    expect(decoded!.searchWindowFull).toBe(false)
    const item = decoded!.items[0]
    expect([item.physicalKg, item.blockedPhysicalKg, item.freePhysicalKg, item.activeAllocationKg, item.availableKg])
      .toEqual(['10', '2', '8', '1', '7'])
    expect(item.productDisplay.identity).toEqual({ productId: PRODUCT, productVersion: '7', code: 'P-1', name: 'Galvaniz bant' })
    // The nominal measures arrive as JSON numbers and are kept as their exact decimal text.
    expect(item.productDisplay.technical).toMatchObject({ thicknessMm: '1.5', widthMm: '1250' })
    expect(item.classification).toBe('Standard')
  })

  it('accepts a page whose nextCursor is a server continuation, or absent (optional in the published schema)', () => {
    expect(decodeStockDisplayPage(page({ nextCursor: 'opaque.cursor-1', searchWindowFull: true }))?.nextCursor)
      .toBe('opaque.cursor-1')
    const { nextCursor: _cursor, ...withoutCursor } = page()
    expect(decodeStockDisplayPage(withoutCursor)?.nextCursor).toBeNull()
    expect(decodeStockDisplayPage(page({ nextCursor: '' }))).toBeNull()
    expect(decodeStockDisplayPage(page({ nextCursor: 'x'.repeat(4097) }))).toBeNull()
    expect(decodeStockDisplayPage(page({ nextCursor: 12 }))).toBeNull()
  })

  it('decodes an empty page (the host never asks Product for one)', () => {
    expect(decodeStockDisplayPage({ items: [], searchWindowFull: false, nextCursor: null }))
      .toEqual({ items: [], searchWindowFull: false, nextCursor: null })
  })

  it('keeps a Product with no identity or technical block null — the owner’s errors explain it, nothing is filled in', () => {
    const unknown = { ...productDisplay(), identity: null, technical: null, errors: [{ field: 'productId', code: 'invalid_reference' }] }
    const decoded = decodeStockDisplayPage(page({ items: [row({ productDisplay: unknown })] }))
    expect(decoded?.items[0].productDisplay.identity).toBeNull()
    expect(decoded?.items[0].productDisplay.technical).toBeNull()
    expect(decoded?.items[0].productDisplay.errors).toEqual([{ field: 'productId', code: 'invalid_reference' }])

    const templateMismatch = {
      ...productDisplay(), technical: null, errors: [{ field: 'references.templateId', code: 'family_template_mismatch' }],
    }
    const kept = decodeStockDisplayPage(page({ items: [row({ productDisplay: templateMismatch })] }))
    expect(kept?.items[0].productDisplay.identity?.code).toBe('P-1')
    expect(kept?.items[0].productDisplay.technical).toBeNull()
  })

  it('binds the Product display to the row’s exact Product and version', () => {
    const otherProduct = '99999999-9999-4999-8999-999999999999'
    expect(decodeStockDisplayPage(page({ items: [row({ productDisplay: { ...productDisplay(), productId: otherProduct } })] }))).toBeNull()
    expect(decodeStockDisplayPage(page({ items: [row({ productDisplay: { ...productDisplay(), expectedProductVersion: '6' } })] }))).toBeNull()
    expect(decodeStockDisplayPage(page({ items: [row({
      productDisplay: { ...productDisplay(), identity: { ...productDisplay().identity, productVersion: '8' } },
    })] }))).toBeNull()
    expect(decodeStockDisplayPage(page({ items: [row({
      productDisplay: { ...productDisplay(), identity: { ...productDisplay().identity, productId: otherProduct } },
    })] }))).toBeNull()
    // A non-empty opt-in row always carries its version; without one it cannot be bound and is refused.
    expect(decodeStockDisplayPage(page({ items: [row({ productVersion: null })] }))).toBeNull()
  })

  it.each([
    ['Standard'], ['SecondQuality'], ['Scrap'], [null],
  ])('accepts classification %s', (classification) => {
    expect(decodeStockDisplayPage(page({ items: [row({ classification })] }))?.items[0].classification).toBe(classification)
  })

  it('refuses a classification outside the Inventory owner’s closed set', () => {
    expect(decodeStockDisplayPage(page({ items: [row({ classification: 'standard' })] }))).toBeNull()
    expect(decodeStockDisplayPage(page({ items: [row({ classification: 'Hurda' })] }))).toBeNull()
  })

  it.each([
    ['Company', 'Company'], ['Counterparty', 'Company'], ['Counterparty', 'Counterparty'],
  ])('accepts owner %s held by %s', (ownerKind, custodianKind) => {
    const decoded = decodeStockDisplayPage(page({ items: [row({ ownerKind, custodianKind })] }))
    expect(decoded?.items[0].ownerKind).toBe(ownerKind)
    expect(decoded?.items[0].custodianKind).toBe(custodianKind)
  })

  it('refuses an owner or holder kind the owner does not publish', () => {
    expect(decodeStockDisplayPage(page({ items: [row({ ownerKind: 'company' })] }))).toBeNull()
    expect(decodeStockDisplayPage(page({ items: [row({ custodianKind: 'Supplier' })] }))).toBeNull()
  })

  it('keeps the #588 identities exact: physical = blocked + free-physical, free-physical = allocated + available', () => {
    expect(decodeStockDisplayPage(page({ items: [row({
      physicalKg: '18450.251', blockedPhysicalKg: '0.001', freePhysicalKg: '18450.25', activeAllocationKg: '450.25', availableKg: '18000',
    })] }))?.items[0].availableKg).toBe('18000')
    // Physical off by one gram.
    expect(decodeStockDisplayPage(page({ items: [row({ physicalKg: '10.001' })] }))).toBeNull()
    // Available reported where free-physical belongs: the mix-up #588 exists to prevent.
    expect(decodeStockDisplayPage(page({ items: [row({ freePhysicalKg: '7', availableKg: '7' })] }))).toBeNull()
  })

  it('refuses non-canonical, signed or over-precise kilograms', () => {
    for (const bad of ['-1', '10.0', '010', '1e1', '1.2345', ' 10', '']) {
      expect(decodeStockDisplayPage(page({ items: [row({ physicalKg: bad })] })), bad).toBeNull()
    }
  })

  it('accepts the nominal millimetres as a pattern string and refuses an exponent or non-finite form', () => {
    const asText = { ...productDisplay(), technical: { ...productDisplay().technical, thicknessMm: '1.50', widthMm: '1250' } }
    expect(decodeStockDisplayPage(page({ items: [row({ productDisplay: asText })] }))?.items[0].productDisplay.technical?.thicknessMm)
      .toBe('1.50')
    for (const bad of [1e21, Number.NaN, '1e3', '+1', '']) {
      const technical = { ...productDisplay().technical, widthMm: bad }
      expect(decodeStockDisplayPage(page({ items: [row({ productDisplay: { ...productDisplay(), technical } })] })), String(bad)).toBeNull()
    }
  })

  it('refuses unknown or missing keys at every level', () => {
    expect(decodeStockDisplayPage({ ...page(), total: 1 })).toBeNull()
    expect(decodeStockDisplayPage({ items: [row()], nextCursor: null })).toBeNull()
    expect(decodeStockDisplayPage(page({ items: [{ ...row(), locationName: 'Hol-3' }] }))).toBeNull()
    const { lot: _lot, ...withoutLot } = row()
    expect(decodeStockDisplayPage(page({ items: [withoutLot] }))).toBeNull()
    expect(decodeStockDisplayPage(page({ items: [row({ lot: { id: LOT, heatNumber: null, coilNumber: null, extra: 1 } })] }))).toBeNull()
    expect(decodeStockDisplayPage(page({ items: [row({ productDisplay: { ...productDisplay(), locationDisplay: null } })] }))).toBeNull()
    expect(decodeStockDisplayPage(page({ items: [row({
      productDisplay: { ...productDisplay(), errors: [{ field: 'productId', code: 'invalid_reference', message: 'x' }] },
    })] }))).toBeNull()
  })

  it('refuses the same label twice on one page', () => {
    expect(decodeStockDisplayPage(page({ items: [row(), row()] }))).toBeNull()
  })

  it('does not accept the previously published shape, and the legacy decoder does not accept this one', () => {
    // Old-response compatibility: the legacy reader keeps reading the old shape for its other screens …
    expect(decodeInventoryStockPositions({ items: [legacyRow()], searchWindowFull: false })?.items[0].labelCode).toBe('ROLL-1')
    // … and neither decoder loosens to the other's keys, so a response is read by the mode that was requested.
    expect(decodeStockDisplayPage({ items: [legacyRow()], searchWindowFull: false })).toBeNull()
    expect(decodeInventoryStockPositions(page())).toBeNull()
  })
})

describe('decodeStockDisplayPage — every guard can fail on its own', () => {
  it('keeps the SECOND #588 identity on its own: free-physical = allocated + available', () => {
    // physical = blocked + free holds (10 = 2 + 8); only free = allocated + available is broken (8 ≠ 1 + 8).
    expect(decodeStockDisplayPage(page({ items: [row({ availableKg: '8' })] }))).toBeNull()
    expect(decodeStockDisplayPage(page({ items: [row({ activeAllocationKg: '2' })] }))).toBeNull()
  })

  it.each([
    ['physicalKg'], ['blockedPhysicalKg'], ['freePhysicalKg'], ['activeAllocationKg'], ['availableKg'],
  ])('refuses a non-canonical %s even where the identities would still add up', (field) => {
    // Each bad form keeps the field's own value, so only the canonical-text check can refuse it.
    const value = String(row()[field as keyof ReturnType<typeof row>])
    for (const bad of [`${value}.0`, `0${value}`, `+${value}`, `${value} `, `${value}e0`]) {
      expect(decodeStockDisplayPage(page({ items: [row({ [field]: bad })] })), `${field}=${bad}`).toBeNull()
    }
  })

  it('refuses a negative Kullanılabilir balanced by over-allocation, and a consistent four-decimal row', () => {
    expect(decodeStockDisplayPage(page({ items: [row({ activeAllocationKg: '9', availableKg: '-1' })] }))).toBeNull()
    expect(decodeStockDisplayPage(page({ items: [row({
      physicalKg: '0.0002', blockedPhysicalKg: '0.0001', freePhysicalKg: '0.0001', activeAllocationKg: '0', availableKg: '0.0001',
    })] }))).toBeNull()
  })

  it('adds quantities exactly beyond Number precision (no floating-point sums)', () => {
    const big = { physicalKg: '9007199254740993', blockedPhysicalKg: '0', freePhysicalKg: '9007199254740993', activeAllocationKg: '0', availableKg: '9007199254740993' }
    expect(decodeStockDisplayPage(page({ items: [row(big)] }))?.items[0].availableKg).toBe('9007199254740993')
    // As Numbers these would be equal (9007199254740992); exactly they are not.
    expect(decodeStockDisplayPage(page({ items: [row({ ...big, blockedPhysicalKg: '1' })] }))).toBeNull()
  })

  it('refuses an extra or missing key inside identity, technical, family and template', () => {
    const display = productDisplay()
    const variants = [
      { ...display, identity: { ...display.identity, currentName: 'x' } },
      { ...display, identity: { productId: PRODUCT, productVersion: '7', code: 'P-1' } },
      { ...display, technical: { ...display.technical, lengthMm: 1 } },
      { ...display, technical: { family: display.technical.family, template: display.technical.template, thicknessMm: 1.5 } },
      { ...display, technical: { ...display.technical, family: { ...display.technical.family, name: 'Bobin' } } },
      { ...display, technical: { ...display.technical, family: { familyId: FAMILY, familyCode: 'coil' } } },
      { ...display, technical: { ...display.technical, template: { ...display.technical.template, extra: 1 } } },
      { ...display, technical: { ...display.technical, template: { templateId: TEMPLATE, templateVersion: '1', templateCode: 'coil-v1' } } },
    ]
    for (const [index, productDisplay] of variants.entries()) {
      expect(decodeStockDisplayPage(page({ items: [row({ productDisplay })] })), String(index)).toBeNull()
    }
  })

  it.each([
    ['a nil label id', { labelId: '00000000-0000-0000-0000-000000000000' }],
    ['a label version 0', { labelVersion: '0' }],
    ['a label version beyond int64', { labelVersion: '9223372036854775808' }],
    ['an empty heat number', { heatNumber: '' }],
    ['an empty document status', { documentStatus: '' }],
    ['a blank Product version', { productVersion: ' ' }],
    ['a non-GUID lot id', { lot: { id: 'lot-1', heatNumber: 'HEAT-1', coilNumber: 'COIL-1' } }],
  ])('refuses a row with %s', (_name, overrides) => {
    expect(decodeStockDisplayPage(page({ items: [row(overrides)] }))).toBeNull()
  })

  it.each([
    ['an empty Product code', { identity: { productId: PRODUCT, productVersion: '7', code: '', name: 'Galvaniz bant' } }],
    ['an empty Product name', { identity: { productId: PRODUCT, productVersion: '7', code: 'P-1', name: ' ' } }],
    ['errors that are not a list', { errors: null }],
    ['a non-GUID family id', { technical: { ...productDisplay().technical, family: { familyId: 'coil', familyCode: 'coil', sourceRevision: 'r1' } } }],
  ])('refuses a Product display with %s', (_name, overrides) => {
    expect(decodeStockDisplayPage(page({ items: [row({ productDisplay: { ...productDisplay(), ...overrides } })] }))).toBeNull()
  })

  it('keeps a row whose stored Product version the Product owner could not read (its own invalid_version result)', () => {
    const unreadable = {
      productId: PRODUCT, expectedProductVersion: 'v7', identity: null, technical: null,
      errors: [{ field: 'expectedProductVersion', code: 'invalid_version' }],
    }
    const decoded = decodeStockDisplayPage(page({ items: [row({ productVersion: 'v7', productDisplay: unreadable })] }))
    expect(decoded?.items[0].productDisplay.identity).toBeNull()
    expect(decoded?.items[0].productVersion).toBe('v7')
    // Still bound: the display must answer for that exact stored text.
    expect(decodeStockDisplayPage(page({ items: [row({ productVersion: 'v7', productDisplay: { ...unreadable, expectedProductVersion: '7' } })] }))).toBeNull()
  })
})

/**
 * STOCK-LOCATION-2: the #607 `2.0` page (Product + Location display). Every `1.0` member and rule is unchanged; each row
 * adds the required, non-null `locationDisplay` (`OrganizationLocationDisplayV1`) bound to the row's own `locationId`.
 */
describe('decodeStockLocationDisplayPage — #607 2.0 page', () => {
  const display = (overrides: Record<string, unknown> = {}) =>
    ({ locationId: LOCATION, locationCode: 'HOL-1', locationName: 'Hol 1 · Boy kesme', ...overrides })
  const row2 = (overrides: Record<string, unknown> = {}) => ({ ...row(), locationDisplay: display(), ...overrides })
  const page2 = (items: readonly unknown[] = [row2()], extra: Record<string, unknown> = {}) =>
    ({ items, searchWindowFull: false, nextCursor: null, ...extra })

  it('decodes the 2.0 row: every 1.0 member as the 1.0 decoder reads it, plus the bound location display', () => {
    const decoded = decodeStockLocationDisplayPage(page2())
    const { locationDisplay, ...rest } = decoded!.items[0]
    expect(rest).toEqual(decodeStockDisplayPage(page())!.items[0])
    expect(locationDisplay).toEqual(display())
    expect(decodeStockLocationDisplayPage(page2([], { nextCursor: 'opaque' }))?.nextCursor).toBe('opaque')
    const { nextCursor: _cursor, ...noCursor } = page2()
    expect(decodeStockLocationDisplayPage(noCursor)?.nextCursor).toBeNull()
  })

  it('keeps a long Turkish name and a long code exactly as published', () => {
    const name = 'Çağlayan Öğütücü İşletmesi — Hol 2 · Boy kesme ve dilme hattı, güney kapı yanı'
    const decoded = decodeStockLocationDisplayPage(page2([row2({ locationDisplay: display({ locationCode: 'TS-B-HOL-02-BK-0001', locationName: name }) })]))
    expect(decoded?.items[0].locationDisplay).toEqual({ locationId: LOCATION, locationCode: 'TS-B-HOL-02-BK-0001', locationName: name })
  })

  it('accepts the nested id in another letter case, still bound to the row', () => {
    expect(decodeStockLocationDisplayPage(page2([row2({ locationDisplay: display({ locationId: LOCATION.toUpperCase() }) })]))).not.toBeNull()
  })

  it.each([
    ['a missing location display', (r: Record<string, unknown>) => { delete r.locationDisplay }],
    ['a null location display', (r: Record<string, unknown>) => { r.locationDisplay = null }],
    ['a display of another location', (r: Record<string, unknown>) => { r.locationDisplay = display({ locationId: '99999999-9999-4999-8999-999999999999' }) }],
    ['a null code', (r: Record<string, unknown>) => { r.locationDisplay = display({ locationCode: null }) }],
    ['a null name', (r: Record<string, unknown>) => { r.locationDisplay = display({ locationName: null }) }],
    ['a blank code', (r: Record<string, unknown>) => { r.locationDisplay = display({ locationCode: '  ' }) }],
    ['a blank name', (r: Record<string, unknown>) => { r.locationDisplay = display({ locationName: '' }) }],
    ['a code that is not text', (r: Record<string, unknown>) => { r.locationDisplay = display({ locationCode: 12 }) }],
    ['a nested id that is not a GUID', (r: Record<string, unknown>) => { r.locationDisplay = display({ locationId: 'HOL-1' }) }],
    ['an unknown member in the display', (r: Record<string, unknown>) => { r.locationDisplay = display({ siteId: LOCATION }) }],
    ['a missing member in the display', (r: Record<string, unknown>) => { const { locationName: _n, ...rest } = display(); r.locationDisplay = rest }],
    ['an unknown row member', (r: Record<string, unknown>) => { r.locationName = 'Hol 1' }],
  ])('refuses the whole page for %s', (_, change) => {
    const bad = row2()
    change(bad as Record<string, unknown>)
    expect(decodeStockLocationDisplayPage(page2([row2({ labelId: '22222222-2222-4222-8222-222222222223' }), bad]))).toBeNull()
  })

  it('applies every 1.0 rule to a 2.0 row (a broken quantity identity, a foreign Product display)', () => {
    expect(decodeStockLocationDisplayPage(page2([row2({ availableKg: '6' })]))).toBeNull()
    expect(decodeStockLocationDisplayPage(page2([row2({ productDisplay: { ...productDisplay(), expectedProductVersion: '8' } })]))).toBeNull()
  })

  it('keeps the two shapes apart: 2.0 never accepts a 1.0 row, 1.0 never accepts a 2.0 row', () => {
    expect(decodeStockLocationDisplayPage(page())).toBeNull()
    expect(decodeStockDisplayPage(page2())).toBeNull()
    expect(decodeStockDisplayPage(page())).not.toBeNull()
  })
})

/**
 * STOCK-OWNER-DISPLAY-3: the `3.0` page (#664, `X-ERP-Inventory-Stock-Display: 3.0`). Every `2.0` member stays as it
 * is; a `3.0` row adds the required-nullable `ownerDisplay` — the Cari owner's identity bound to the row's own
 * `ownerId`, and null exactly when the owner is a Company.
 */
describe('decodeStockOwnerDisplayPage — #664 3.0 page', () => {
  const COUNTERPARTY = '88888888-8888-4888-8888-888888888888'
  const LONG_LEGAL = 'KARADENİZ ÇELİK DÖKÜM MAKİNA İMALAT SANAYİ VE DIŞ TİCARET ANONİM ŞİRKETİ GEBZE ŞUBESİ'
  const location = () => ({ locationId: LOCATION, locationCode: 'HOL-1', locationName: 'Hol 1 · Boy kesme' })
  const owner = (overrides: Record<string, unknown> = {}) => ({
    partyId: COUNTERPARTY, code: 'CAR-000042', legalName: LONG_LEGAL, tradeName: 'Karadeniz Döküm', ...overrides,
  })
  /** A Company-owned `3.0` row: the `2.0` row plus a null owner display. */
  const companyRow = (overrides: Record<string, unknown> = {}) => ({ ...row(), locationDisplay: location(), ownerDisplay: null, ...overrides })
  /** A Cari-owned `3.0` row with its bound identity. */
  const cariRow = (overrides: Record<string, unknown> = {}) => companyRow({
    labelId: '22222222-2222-4222-8222-222222222223', ownerKind: 'Counterparty', ownerId: COUNTERPARTY, ownerDisplay: owner(), ...overrides,
  })
  const page3 = (items: unknown[] = [companyRow()], overrides: Record<string, unknown> = {}) =>
    ({ items, searchWindowFull: false, nextCursor: null, ...overrides })

  it('decodes the 3.0 row: every 2.0 member as the 2.0 decoder reads it, plus the owner display', () => {
    const decoded = decodeStockOwnerDisplayPage(page3([companyRow(), cariRow()]))
    expect(decoded?.items.map((item) => item.ownerDisplay)).toEqual([null, owner()])
    const { ownerDisplay: _o, ...rest } = decoded!.items[0]
    const { ownerDisplay: _raw, ...wire } = companyRow()
    expect(rest).toEqual(decodeStockLocationDisplayPage({ items: [wire], searchWindowFull: false, nextCursor: null })!.items[0])
    expect(decodeStockOwnerDisplayPage(page3([], { nextCursor: 'opaque' }))?.nextCursor).toBe('opaque')
    const { nextCursor: _c, ...noCursor } = page3()
    expect(decodeStockOwnerDisplayPage(noCursor)?.nextCursor).toBeNull()
  })

  it('keeps a long Turkish legal name, the code and the trade name exactly as published — nothing trimmed', () => {
    const padded = owner({ code: ' CAR-000042 ', legalName: ` ${LONG_LEGAL} `, tradeName: ' Karadeniz ' })
    expect(decodeStockOwnerDisplayPage(page3([cariRow({ ownerDisplay: padded })]))?.items[0].ownerDisplay).toEqual(padded)
  })

  it('accepts a null trade name and any string the Cari owner publishes for it', () => {
    for (const tradeName of [null, '', '   ', LONG_LEGAL]) {
      expect(decodeStockOwnerDisplayPage(page3([cariRow({ ownerDisplay: owner({ tradeName }) })]))?.items[0].ownerDisplay?.tradeName).toBe(tradeName)
    }
  })

  it('accepts the party id in another letter case, still bound to the row', () => {
    expect(decodeStockOwnerDisplayPage(page3([cariRow({ ownerDisplay: owner({ partyId: COUNTERPARTY.toUpperCase() }) })]))).not.toBeNull()
  })

  it.each([
    ['a missing ownerDisplay key (the 2.0 shape)', (r: Record<string, unknown>) => { delete r.ownerDisplay }],
    ['an undefined ownerDisplay', (r: Record<string, unknown>) => { r.ownerDisplay = undefined }],
    ['a Cari owner with a null display', (r: Record<string, unknown>) => { r.ownerDisplay = null }],
    ['a display of another party', (r: Record<string, unknown>) => { r.ownerDisplay = owner({ partyId: '99999999-9999-4999-8999-999999999999' }) }],
    ['a display naming the company', (r: Record<string, unknown>) => { r.ownerDisplay = owner({ partyId: COMPANY }) }],
    ['a nil party id', (r: Record<string, unknown>) => { r.ownerDisplay = owner({ partyId: '00000000-0000-0000-0000-000000000000' }) }],
    ['a party id that is not a GUID', (r: Record<string, unknown>) => { r.ownerDisplay = owner({ partyId: 'CAR-000042' }) }],
    ['a null code', (r: Record<string, unknown>) => { r.ownerDisplay = owner({ code: null }) }],
    ['a blank code', (r: Record<string, unknown>) => { r.ownerDisplay = owner({ code: '  ' }) }],
    ['a code that is not text', (r: Record<string, unknown>) => { r.ownerDisplay = owner({ code: 42 }) }],
    ['a null legal name', (r: Record<string, unknown>) => { r.ownerDisplay = owner({ legalName: null }) }],
    ['a blank legal name', (r: Record<string, unknown>) => { r.ownerDisplay = owner({ legalName: '' }) }],
    ['a trade name that is not text', (r: Record<string, unknown>) => { r.ownerDisplay = owner({ tradeName: 7 }) }],
    ['a missing trade name', (r: Record<string, unknown>) => { const { tradeName: _t, ...rest } = owner(); r.ownerDisplay = rest }],
    ['an unknown member in the display', (r: Record<string, unknown>) => { r.ownerDisplay = owner({ taxNumber: '1' }) }],
    ['a display that is an array', (r: Record<string, unknown>) => { r.ownerDisplay = [owner()] }],
    ['a display that is text', (r: Record<string, unknown>) => { r.ownerDisplay = 'CAR-000042' }],
  ])('refuses the whole page for a Cari row with %s', (_, mutate) => {
    const bad: Record<string, unknown> = cariRow()
    mutate(bad)
    expect(decodeStockOwnerDisplayPage(page3([bad]))).toBeNull()
    // One bad row refuses the page: a good row beside it is not shown either.
    expect(decodeStockOwnerDisplayPage(page3([companyRow(), bad]))).toBeNull()
  })

  it.each([
    ['a Cari identity', owner()],
    ['an identity naming the company itself', owner({ partyId: COMPANY })],
    ['an empty object', {}],
    ['text', 'Şirket malı'],
  ])('refuses a Company row carrying %s: a company owner has no display', (_, ownerDisplay) => {
    expect(decodeStockOwnerDisplayPage(page3([companyRow({ ownerDisplay })]))).toBeNull()
  })

  it('applies every 2.0 and 1.0 rule to a 3.0 row', () => {
    expect(decodeStockOwnerDisplayPage(page3([companyRow({ availableKg: '6' })]))).toBeNull()
    expect(decodeStockOwnerDisplayPage(page3([companyRow({ locationDisplay: null })]))).toBeNull()
    expect(decodeStockOwnerDisplayPage(page3([companyRow({ locationDisplay: { ...location(), locationId: PRODUCT } })]))).toBeNull()
    expect(decodeStockOwnerDisplayPage(page3([companyRow({ productDisplay: { ...productDisplay(), expectedProductVersion: '8' } })]))).toBeNull()
    expect(decodeStockOwnerDisplayPage(page3([companyRow({ extra: true })]))).toBeNull()
    expect(decodeStockOwnerDisplayPage(page3([companyRow(), companyRow()]))).toBeNull()
  })

  it('keeps the three shapes apart: no decoder accepts another version\'s row', () => {
    const v3 = page3([companyRow()])
    const { ownerDisplay: _o, ...row2 } = companyRow()
    const v2 = { items: [row2], searchWindowFull: false, nextCursor: null }
    expect(decodeStockOwnerDisplayPage(v2)).toBeNull()
    expect(decodeStockOwnerDisplayPage(page())).toBeNull()
    expect(decodeStockLocationDisplayPage(v3)).toBeNull()
    expect(decodeStockDisplayPage(v3)).toBeNull()
    // The older decoders still read their own shapes, unchanged.
    expect(decodeStockLocationDisplayPage(v2)).not.toBeNull()
    expect(decodeStockDisplayPage(page())).not.toBeNull()
  })
})
