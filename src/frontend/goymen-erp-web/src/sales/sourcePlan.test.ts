/**
 * API-11 (SAT-D252..SAT-D255): the strict decoder of `GET /sales/drafts/{id}/source-plan`. A read is data only when it has
 * the exact key set and describes exactly the detail it was read for; anything else is malformed (null). SENTETIK data.
 */
import { describe, expect, it } from 'vitest'
import detailResponseRaw from './examples/detail-response.json?raw'
import { decodeDraftDetail, type DraftDetail } from './contract'
import { compareDecimalText, decodeSourcePlan } from './sourcePlan'

const LINE = '66666666-6666-6666-6666-666666666666'
const KG = '44444444-4444-4444-4444-444444444444'

type Body = Record<string, any>

function detail(): DraftDetail {
  const raw = JSON.parse(detailResponseRaw) as Body
  const decoded = decodeDraftDetail(raw, raw.draft.id)
  if (decoded === null) throw new Error('example detail does not decode')
  return decoded
}

const saved = (): Body => ({
  draftVersion: '9007199254740992', lineQuantity: '1000', locked: '200', stock: '300', production: '400', purchasing: '100',
  subcontract: '0', linkedSupplyConfirmed: false, savedBy: 'planlama-1', savedAtUtc: '2026-11-09T08:00:00Z',
})

function line(overrides: Body = {}): Body {
  return {
    lineId: LINE, lineNo: 1, orderUnitId: KG, quantity: '1000', locked: '200', remaining: '800', stockStatus: 'Derived',
    stock: '300', suggestion: 'Unavailable', suggestionReason: 'FreeStockUnavailable', saved: saved(), writable: true,
    lockReason: null, warnings: [], ...overrides,
  }
}

function plan(overrides: Body = {}, lineOverrides: Body = {}): Body {
  return {
    version: '9007199254740993', revision: 'Rev.0', writable: true,
    linkedSupply: [{ kind: 'Production', status: 'None' }, { kind: 'Purchasing', status: 'Present' }],
    lines: [line(lineOverrides)], ...overrides,
  }
}

describe('decodeSourcePlan (API-11)', () => {
  it('decodes a plan of exactly the detail and keeps every quantity as text', () => {
    const decoded = decodeSourcePlan(plan(), detail())
    expect(decoded).not.toBeNull()
    expect(decoded!.lines[0].saved!.production).toBe('400')
    expect(decoded!.lines[0].stock).toBe('300')
    expect(decoded!.linkedSupply.map((entry) => entry.status)).toEqual(['None', 'Present'])
  })

  it('rejects an extra or a missing key at every level', () => {
    expect(decodeSourcePlan({ ...plan(), extra: 1 }, detail())).toBeNull()
    const { revision: _revision, ...noRevision } = plan()
    expect(decodeSourcePlan(noRevision, detail())).toBeNull()
    expect(decodeSourcePlan(plan({}, { extra: true }), detail())).toBeNull()
    const { warnings: _warnings, ...noWarnings } = line()
    expect(decodeSourcePlan(plan({ lines: [noWarnings] }), detail())).toBeNull()
    expect(decodeSourcePlan(plan({}, { saved: { ...saved(), extra: '1' } }), detail())).toBeNull()
    expect(decodeSourcePlan(plan({ linkedSupply: [{ kind: 'Production', status: 'None', extra: 1 }, { kind: 'Purchasing', status: 'None' }] }), detail()))
      .toBeNull()
  })

  it('rejects a plan for another version, revision or line set', () => {
    expect(decodeSourcePlan(plan({ version: '9007199254740992' }), detail())).toBeNull()
    expect(decodeSourcePlan(plan({ revision: 'Rev.1' }), detail())).toBeNull()
    expect(decodeSourcePlan(plan({ lines: [] }), detail())).toBeNull()
    expect(decodeSourcePlan(plan({ lines: [line(), line()] }), detail())).toBeNull()
    expect(decodeSourcePlan(plan({}, { lineId: '66666666-6666-6666-6666-666666666667' }), detail())).toBeNull()
    expect(decodeSourcePlan(plan({}, { lineNo: 2 }), detail())).toBeNull()
    expect(decodeSourcePlan(plan({}, { quantity: '1000.0' }), detail())).toBeNull()
    expect(decodeSourcePlan(plan({}, { orderUnitId: '44444444-4444-4444-4444-444444444445' }), detail())).toBeNull()
  })

  it('rejects a linked-supply list without exactly one entry per kind', () => {
    expect(decodeSourcePlan(plan({ linkedSupply: [{ kind: 'Production', status: 'None' }] }), detail())).toBeNull()
    expect(decodeSourcePlan(plan({ linkedSupply: [{ kind: 'Production', status: 'None' }, { kind: 'Production', status: 'None' }] }), detail()))
      .toBeNull()
    expect(decodeSourcePlan(plan({ linkedSupply: [{ kind: 'Production', status: 'Open' }, { kind: 'Purchasing', status: 'None' }] }), detail()))
      .toBeNull()
  })

  it('binds the stock figure to its status: unreadable carries none, derived always carries one', () => {
    const unreadable = { stockStatus: 'Unreadable', stock: null, writable: false, lockReason: 'AllocationUnreadable' }
    expect(decodeSourcePlan(plan({}, unreadable), detail())).not.toBeNull()
    expect(decodeSourcePlan(plan({}, { ...unreadable, stock: '0' }), detail())).toBeNull()
    expect(decodeSourcePlan(plan({}, { stock: null }), detail())).toBeNull()
  })

  it('binds the NoPlan warning to a missing recorded plan', () => {
    expect(decodeSourcePlan(plan({}, { saved: null, warnings: ['NoPlan'] }), detail())).not.toBeNull()
    expect(decodeSourcePlan(plan({}, { saved: null, warnings: [] }), detail())).toBeNull()
    expect(decodeSourcePlan(plan({}, { warnings: ['NoPlan'] }), detail())).toBeNull()
    expect(decodeSourcePlan(plan({}, { saved: null, warnings: ['NoPlan', 'NoPlan'] }), detail())).toBeNull()
    expect(decodeSourcePlan(plan({}, { saved: null, warnings: ['NoPlan', 'Unknown'] }), detail())).toBeNull()
  })

  it('rejects a writable line on a locked order, a lock without a reason, and a writable line with a reason', () => {
    expect(decodeSourcePlan(plan({ writable: false }), detail())).toBeNull()
    expect(decodeSourcePlan(plan({ writable: false }, { writable: false, lockReason: 'OrderState' }), detail())).not.toBeNull()
    expect(decodeSourcePlan(plan({}, { writable: false, lockReason: null }), detail())).toBeNull()
    expect(decodeSourcePlan(plan({}, { lockReason: 'LineClosed' }), detail())).toBeNull()
    expect(decodeSourcePlan(plan({}, { writable: false, lockReason: 'Unknown' }), detail())).toBeNull()
  })

  it('keeps the locked part and the remainder together and never above the quantity', () => {
    expect(decodeSourcePlan(plan({}, { locked: null, remaining: '800', writable: false, lockReason: 'DeliveryUnreadable' }), detail()))
      .toBeNull()
    expect(decodeSourcePlan(plan({}, { locked: null, remaining: null, writable: false, lockReason: 'DeliveryUnreadable' }), detail()))
      .not.toBeNull()
    expect(decodeSourcePlan(plan({}, { locked: '1000.5', remaining: '0' }), detail())).toBeNull()
    // A writable line needs both facts a save is judged on.
    expect(decodeSourcePlan(plan({}, { locked: null, remaining: null }), detail())).toBeNull()
  })

  it('rejects non-canonical decimals, numbers for quantities and a recorded plan on a delivered remainder', () => {
    expect(decodeSourcePlan(plan({}, { stock: '01' }), detail())).toBeNull()
    expect(decodeSourcePlan(plan({}, { stock: 300 }), detail())).toBeNull()
    expect(decodeSourcePlan(plan({}, { stock: '-1' }), detail())).toBeNull()
    expect(decodeSourcePlan(plan({}, { saved: { ...saved(), locked: '1000' } }), detail())).toBeNull()
    expect(decodeSourcePlan(plan({}, { saved: { ...saved(), savedBy: ' ' } }), detail())).toBeNull()
    expect(decodeSourcePlan(plan({}, { saved: { ...saved(), savedAtUtc: 'yesterday' } }), detail())).toBeNull()
  })

  it('compares decimal text exactly beyond the JS number range', () => {
    expect(compareDecimalText('12345678901234567890.1', '12345678901234567890.10')).toBe(0)
    expect(compareDecimalText('12345678901234567890', '12345678901234567891')).toBe(-1)
    expect(compareDecimalText('2.5', '2.25')).toBe(1)
  })
})
