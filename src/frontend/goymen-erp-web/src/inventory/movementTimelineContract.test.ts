import { describe, expect, it } from 'vitest'
import {
  compareSqlServerUniqueIdentifiers,
  continuesTimeline,
  decodeInventoryMovementTimelinePage,
  isCanonicalKg,
  MOVEMENT_KINDS,
} from './movementTimelineContract'

const COMPANY = '11111111-1111-4111-8111-111111111111'
const LABEL = '22222222-2222-4222-8222-222222222222'
const LOCATION = '33333333-3333-4333-8333-333333333333'
const PRODUCT = '44444444-4444-4444-8444-444444444444'
const SOURCE = '55555555-5555-4555-8555-555555555555'
const SOURCE_LINE = '66666666-6666-4666-8666-666666666666'
const MOVEMENT_A = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'
const MOVEMENT_B = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb'

function line(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    sequence: 1, direction: 'In', labelId: LABEL, labelCode: 'SYN-LBL-1', ownerKind: 'Company', ownerId: COMPANY,
    custodianKind: 'Company', custodianId: COMPANY, locationId: LOCATION, productId: PRODUCT, block: 'Free',
    quantityKg: '12.5', ...overrides,
  }
}

function movement(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    movementId: MOVEMENT_A, kind: 'Receipt', occurredAtUtc: '2026-10-08T09:00:00+00:00',
    sourceKind: 'inventory.opening-count', sourceId: SOURCE, sourceLineId: SOURCE_LINE, sourceGrain: 'Line',
    lines: [line()], ...overrides,
  }
}

function page(items: unknown[], nextCursor: string | null = null): unknown {
  return { items, nextCursor }
}

describe('decodeInventoryMovementTimelinePage', () => {
  it('decodes the exact owner page and keeps every fact as stored', () => {
    const decoded = decodeInventoryMovementTimelinePage(page([movement()], 'opaque.cursor'), 25)
    expect(decoded).toEqual({ items: [movement()], nextCursor: 'opaque.cursor' })
  })

  it('accepts an empty last page and a Z instant', () => {
    expect(decodeInventoryMovementTimelinePage(page([]), 25)).toEqual({ items: [], nextCursor: null })
    expect(decodeInventoryMovementTimelinePage(page([movement({ occurredAtUtc: '2026-10-08T09:00:00.1234567Z' })]), 25))
      .not.toBeNull()
  })

  it.each([
    ['extra page key', { items: [], nextCursor: null, windowFull: false }],
    ['missing nextCursor', { items: [] }],
    ['array root', []],
    ['cursor without items', page([], 'opaque')],
    ['blank cursor', page([movement()], ' ')],
    ['padded cursor', page([movement()], ' opaque')],
    ['numeric cursor', page([movement()], 7 as unknown as string)],
  ])('refuses a malformed page: %s', (_name, body) => {
    expect(decodeInventoryMovementTimelinePage(body, 25)).toBeNull()
  })

  it('refuses a page longer than the limit it was asked for', () => {
    const items = [movement(), movement({ movementId: MOVEMENT_B, occurredAtUtc: '2026-10-08T08:00:00Z' })]
    expect(decodeInventoryMovementTimelinePage(page(items), 1)).toBeNull()
    expect(decodeInventoryMovementTimelinePage(page(items), 2)).not.toBeNull()
  })

  it('refuses a duplicate movement and an older-before-newer order', () => {
    expect(decodeInventoryMovementTimelinePage(page([movement(), movement()]), 25)).toBeNull()
    expect(decodeInventoryMovementTimelinePage(page([
      movement({ occurredAtUtc: '2026-10-08T08:00:00Z' }),
      movement({ movementId: MOVEMENT_B, occurredAtUtc: '2026-10-08T09:00:00Z' }),
    ]), 25)).toBeNull()
  })

  it.each([
    ['extra movement key', movement({ cost: '1' })],
    ['unknown kind', movement({ kind: 'Reversal' })],
    ['lower-case kind', movement({ kind: 'receipt' })],
    ['nil movement id', movement({ movementId: '00000000-0000-0000-0000-000000000000' })],
    ['offset instant', movement({ occurredAtUtc: '2026-10-08T12:00:00+03:00' })],
    ['date-only instant', movement({ occurredAtUtc: '2026-10-08' })],
    ['padded source kind', movement({ sourceKind: ' inventory.opening-count' })],
    ['long source kind', movement({ sourceKind: 'x'.repeat(65) })],
    ['whole grain with a line id', movement({ sourceGrain: 'Whole' })],
    ['line grain without a line id', movement({ sourceLineId: null })],
    ['unknown grain', movement({ sourceGrain: 'Partial' })],
    ['no lines', movement({ lines: [] })],
    ['unsorted line sequence', movement({ lines: [line({ sequence: 2 }), line({ sequence: 1 })] })],
    ['duplicate line sequence', movement({ lines: [line(), line()] })],
  ])('refuses a malformed movement: %s', (_name, item) => {
    expect(decodeInventoryMovementTimelinePage(page([item]), 25)).toBeNull()
  })

  it.each([
    ['extra line key', line({ cost: '1' })],
    ['missing label code', (() => { const value = line(); delete value.labelCode; return value })()],
    ['blank label code', line({ labelCode: ' ' })],
    ['control in label code', line({ labelCode: 'A\u0007B' })],
    ['zero sequence', line({ sequence: 0 })],
    ['string sequence', line({ sequence: '1' })],
    ['unknown direction', line({ direction: 'Both' })],
    ['unknown owner kind', line({ ownerKind: 'Supplier' })],
    ['unknown custodian kind', line({ custodianKind: 'Warehouse' })],
    ['unknown block', line({ block: 'Quarantine' })],
    ['bad label id', line({ labelId: 'label-1' })],
    ['bad product id', line({ productId: '00000000-0000-0000-0000-000000000000' })],
  ])('refuses a malformed line: %s', (_name, item) => {
    expect(decodeInventoryMovementTimelinePage(page([movement({ lines: [item] })]), 25)).toBeNull()
  })

  it.each([
    ['0', false], ['12.50', false], ['012.5', false], ['1.2345', false], ['-1', false], ['1e3', false],
    ['1.', false], ['.5', false], [' 1', false], ['12.5', true], ['0.001', true], ['1000', true],
  ])('canonical kg %s → %s', (value, accepted) => {
    expect(isCanonicalKg(value)).toBe(accepted)
    expect(decodeInventoryMovementTimelinePage(page([movement({ lines: [line({ quantityKg: value })] })]), 25) !== null)
      .toBe(accepted)
  })

  it('refuses a numeric kg', () => {
    expect(decodeInventoryMovementTimelinePage(page([movement({ lines: [line({ quantityKg: 12.5 })] })]), 25)).toBeNull()
  })

  it('accepts every closed kind with its owner line shape and refuses the wrong shape', () => {
    const out = line({ direction: 'Out' })
    const shapes: Record<string, { ok: unknown[]; bad: unknown[] }> = {
      Receipt: { ok: [line()], bad: [out] },
      Return: { ok: [line()], bad: [out] },
      TransferAccept: { ok: [line()], bad: [out] },
      Issue: { ok: [out], bad: [line({ direction: 'Out', block: 'Blocked' })] },
      TransferDispatch: { ok: [out, line({ sequence: 2, direction: 'Out', block: 'Blocked' })], bad: [line()] },
      Scrap: { ok: [line({ direction: 'Out', block: 'Blocked' })], bad: [out, line({ sequence: 2, direction: 'Out' })] },
      CountAdjustment: { ok: [out], bad: [line(), line({ sequence: 2 })] },
      Block: {
        ok: [line({ direction: 'Out' }), line({ sequence: 2, block: 'Blocked' })],
        bad: [line({ direction: 'Out' }), line({ sequence: 2, block: 'Blocked', quantityKg: '1' })],
      },
      Release: {
        ok: [line({ direction: 'Out', block: 'Blocked' }), line({ sequence: 2 })],
        bad: [line({ direction: 'Out', block: 'Blocked' }), line({ sequence: 2, labelCode: 'OTHER' })],
      },
    }
    expect(Object.keys(shapes).sort()).toEqual([...MOVEMENT_KINDS].sort())
    for (const [kind, { ok, bad }] of Object.entries(shapes)) {
      expect(decodeInventoryMovementTimelinePage(page([movement({ kind, lines: ok })]), 25), kind).not.toBeNull()
      expect(decodeInventoryMovementTimelinePage(page([movement({ kind, lines: bad })]), 25), kind).toBeNull()
    }
  })
})

// InventoryMovementTimelineTests.Equal_timestamp_order_uses_sql_server_uniqueidentifier_semantics: at one instant the
// owner answers SQL Server uniqueidentifier DESCENDING, where the last group outweighs the first.
const SQL_FIRST = '00000000-0000-0000-0000-000000000001'
const SQL_SECOND = '01000000-0000-0000-0000-000000000000'
const SAME_AT = '2026-10-07T12:00:00+00:00'

describe('compareSqlServerUniqueIdentifiers', () => {
  it.each([
    // [larger, smaller]: each pair differs in exactly one byte, ranked by SqlGuid's byte significance.
    ['00000000-0000-0000-0000-000000000001', '01000000-0000-0000-0000-000000000000'],
    ['00000000-0000-0000-0000-010000000000', '00000000-0000-0000-0000-000000000001'],
    ['00000000-0000-0000-0001-000000000000', '00000000-0000-0000-0000-000000000000'],
    ['00000000-0000-0000-0100-000000000000', '00000000-0000-0001-0000-000000000000'],
    ['00000000-0000-0001-0000-000000000000', '00000000-0000-0100-0000-000000000000'],
    ['00000000-0000-0100-0000-000000000000', '00000000-0001-0000-0000-000000000000'],
    ['00000000-0001-0000-0000-000000000000', '00000000-0100-0000-0000-000000000000'],
    ['00000000-0100-0000-0000-000000000000', '00000001-0000-0000-0000-000000000000'],
    ['00000001-0000-0000-0000-000000000000', '00000100-0000-0000-0000-000000000000'],
    ['00010000-0000-0000-0000-000000000000', '01000000-0000-0000-0000-000000000000'],
  ])('%s sorts after %s', (larger, smaller) => {
    expect(compareSqlServerUniqueIdentifiers(larger, smaller)).toBe(1)
    expect(compareSqlServerUniqueIdentifiers(smaller, larger)).toBe(-1)
  })

  it('is case-insensitive and zero for the same id', () => {
    expect(compareSqlServerUniqueIdentifiers(MOVEMENT_A, MOVEMENT_A.toUpperCase())).toBe(0)
  })
})

describe('equal-instant order', () => {
  it('accepts the owner order and refuses the reversed order inside one page', () => {
    expect(decodeInventoryMovementTimelinePage(page([
      movement({ movementId: SQL_FIRST, occurredAtUtc: SAME_AT }),
      movement({ movementId: SQL_SECOND, occurredAtUtc: SAME_AT }),
    ]), 25)).not.toBeNull()
    expect(decodeInventoryMovementTimelinePage(page([
      movement({ movementId: SQL_SECOND, occurredAtUtc: SAME_AT }),
      movement({ movementId: SQL_FIRST, occurredAtUtc: SAME_AT }),
    ]), 25)).toBeNull()
  })

  it('compares instants below a millisecond exactly instead of folding them together', () => {
    // .0000002 is newer than .0000001: SQL_SECOND may precede SQL_FIRST only because its instant is later.
    expect(decodeInventoryMovementTimelinePage(page([
      movement({ movementId: SQL_SECOND, occurredAtUtc: '2026-10-07T12:00:00.0000002Z' }),
      movement({ movementId: SQL_FIRST, occurredAtUtc: '2026-10-07T12:00:00.0000001Z' }),
    ]), 25)).not.toBeNull()
    expect(decodeInventoryMovementTimelinePage(page([
      movement({ movementId: SQL_FIRST, occurredAtUtc: '2026-10-07T12:00:00.0000001Z' }),
      movement({ movementId: SQL_SECOND, occurredAtUtc: '2026-10-07T12:00:00.0000002Z' }),
    ]), 25)).toBeNull()
    // The same instant written with Z or +00:00 and with trailing fraction zeros is one instant.
    expect(decodeInventoryMovementTimelinePage(page([
      movement({ movementId: SQL_FIRST, occurredAtUtc: '2026-10-07T12:00:00.5Z' }),
      movement({ movementId: SQL_SECOND, occurredAtUtc: '2026-10-07T12:00:00.5000000+00:00' }),
    ]), 25)).not.toBeNull()
  })

  it('applies the same tie-break across a continuation', () => {
    const shown = decodeInventoryMovementTimelinePage(page([
      movement({ movementId: SQL_FIRST, occurredAtUtc: SAME_AT }),
    ]), 25)!.items
    const next = decodeInventoryMovementTimelinePage(page([
      movement({ movementId: SQL_SECOND, occurredAtUtc: SAME_AT }),
    ]), 25)!.items
    expect(continuesTimeline(shown, next)).toBe(true)
    expect(continuesTimeline(next, shown)).toBe(false)
  })
})

describe('continuesTimeline', () => {
  const first = decodeInventoryMovementTimelinePage(page([movement()]), 25)!.items
  it('accepts an older continuation and refuses a repeat or a newer movement', () => {
    const older = decodeInventoryMovementTimelinePage(page([
      movement({ movementId: MOVEMENT_B, occurredAtUtc: '2026-10-08T08:00:00Z' }),
    ]), 25)!.items
    expect(continuesTimeline(first, older)).toBe(true)
    expect(continuesTimeline(first, first)).toBe(false)
    const newer = decodeInventoryMovementTimelinePage(page([
      movement({ movementId: MOVEMENT_B, occurredAtUtc: '2026-10-08T10:00:00Z' }),
    ]), 25)!.items
    expect(continuesTimeline(first, newer)).toBe(false)
    expect(continuesTimeline(first, [])).toBe(true)
  })
})
