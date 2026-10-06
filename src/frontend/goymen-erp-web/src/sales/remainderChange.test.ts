import { describe, expect, it } from 'vitest'
import { decodeRemainderChanges } from './remainderChange'

const lineId = '11111111-1111-4111-8111-111111111111'
const unitId = '22222222-2222-4222-8222-222222222222'

const valid = () => ({
  draftVersion: '6',
  floorStatus: 'Readable',
  lines: [{ lineId, lineNo: 1, orderUnitId: unitId, quantity: '3', remainder: 'Revised', loaded: '2', shipped: '2', delivered: '1', minimum: '2' }],
  items: [{
    changeNo: 1, revision: 'Rev.0', lineId, lineNo: 1, kind: 'Revise', state: 'Approved', orderedQuantity: '5',
    requestedQuantity: '3', reason: 'Müşteri kalan miktarı değiştirdi', requestedBy: 'sales-preparer',
    requestedAtUtc: '2026-09-30T10:00:00Z', loadedAtRequest: '2', shippedAtRequest: '2', deliveredAtRequest: '1',
    decidedBy: 'sales-approver', decidedAtUtc: '2026-09-30T10:05:00Z', decisionReason: null as string | null,
    resultQuantity: '3', loadedAtDecision: '2',
  }],
})

describe('remainder change decoder invariants', () => {
  it('maps the canonical minimum wire field to the exact UI floor', () => {
    const raw = valid()
    expect(Object.keys(raw.lines[0]).sort()).toEqual([
      'lineId', 'lineNo', 'orderUnitId', 'quantity', 'remainder', 'loaded', 'shipped', 'delivered', 'minimum',
    ].sort())
    const decoded = decodeRemainderChanges(raw)
    expect(decoded?.lines[0]).toMatchObject({ loaded: '2', shipped: '2', delivered: '1', minimumQuantity: '2' })
  })

  it('refuses the legacy minimumQuantity wire alias', () => {
    const body = valid()
    const line = body.lines[0] as Record<string, unknown>
    line.minimumQuantity = line.minimum
    delete line.minimum
    expect(decodeRemainderChanges(body)).toBeNull()
  })

  it('refuses a readable line with the canonical minimum field missing', () => {
    const body = valid()
    delete (body.lines[0] as Record<string, unknown>).minimum
    expect(decodeRemainderChanges(body)).toBeNull()
  })

  it.each([
    ['approved result', (body: ReturnType<typeof valid>) => { body.items[0].resultQuantity = null as unknown as string }],
    ['approved floor', (body: ReturnType<typeof valid>) => { body.items[0].loadedAtDecision = null as unknown as string }],
    ['approved reason', (body: ReturnType<typeof valid>) => { body.items[0].decisionReason = 'olmamalı' }],
    ['rejected result', (body: ReturnType<typeof valid>) => { body.items[0].state = 'Rejected'; body.items[0].resultQuantity = '3' }],
    ['rejected floor', (body: ReturnType<typeof valid>) => { body.items[0].state = 'Rejected'; body.items[0].resultQuantity = null as unknown as string; body.items[0].loadedAtDecision = '2' }],
  ])('refuses an incoherent %s decision', (_name, corrupt) => {
    const body = valid()
    corrupt(body)
    expect(decodeRemainderChanges(body)).toBeNull()
  })

  it.each(['loaded', 'shipped', 'delivered', 'minimum'] as const)(
    'refuses readable lines with null %s',
    (field) => {
      const body = valid()
      body.lines[0][field] = null as unknown as string
      expect(decodeRemainderChanges(body)).toBeNull()
    },
  )

  it.each([
    ['delivered above shipped', { loaded: '2', shipped: '1', delivered: '1.1', minimum: '2' }],
    ['shipped above loaded', { loaded: '2', shipped: '2.1', delivered: '1', minimum: '2' }],
    ['minimum different from loaded', { loaded: '2', shipped: '2', delivered: '1', minimum: '2.1' }],
  ])('refuses %s', (_name, floor) => {
    const body = valid()
    Object.assign(body.lines[0], floor)
    expect(decodeRemainderChanges(body)).toBeNull()
  })
})
