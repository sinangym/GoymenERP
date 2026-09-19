import { describe, expect, it } from 'vitest'
import createResponseRaw from './examples/create-response.json?raw'
import detailResponseRaw from './examples/detail-response.json?raw'
import loadResponseRaw from './examples/load-response.json?raw'
import listResponseRaw from './examples/list-response.json?raw'
import errorCapRaw from './examples/error-cap.json?raw'
import errorForbiddenRaw from './examples/error-forbidden.json?raw'
import {
  APPROVED_ORDER_LINE_KEYS,
  decodeApprovedOrderLine,
  decodeApprovedOrderLinePage,
  decodeInstructionDetail,
  decodeInstructionMutation,
  decodeInstructionPage,
  decodeProblem,
  parseQuantityInput,
} from './contract'
import approvedLinesRaw from './examples/approved-order-lines-response.json?raw'
import { approvedOrderLine, INSTRUCTION } from './testHarness'

type Body = Record<string, any>
const parse = (raw: string): Body => JSON.parse(raw) as Body
const tweak = (raw: string, change: (body: Body) => void): Body => {
  const body = parse(raw)
  change(body)
  return body
}

describe('instruction decoders', () => {
  it('reads the pinned detail, mutation, load and list bodies', () => {
    expect(decodeInstructionDetail(parse(detailResponseRaw), INSTRUCTION)).not.toBeNull()
    expect(decodeInstructionMutation(parse(createResponseRaw))?.isReplay).toBe(false)
    const loaded = decodeInstructionMutation(parse(loadResponseRaw))
    expect(loaded?.detail.shares[0]).toMatchObject({ kind: 'Loaded', quantity: '2.5', measurement: 'Measured' })
    expect(loaded?.detail.lines[0].remainingToLoad).toBe('2')
    expect(decodeInstructionPage(parse(listResponseRaw))?.items[0].status).toBe('Draft')
  })

  it('keeps every quantity as text and refuses numbers standing in for them', () => {
    const detail = decodeInstructionDetail(parse(detailResponseRaw), null)
    expect(typeof detail?.lines[0].instructed).toBe('string')
    expect(decodeInstructionDetail(tweak(detailResponseRaw, (b) => (b.lines[0].instructed = 4.5)), null)).toBeNull()
    expect(decodeInstructionDetail(tweak(detailResponseRaw, (b) => (b.instruction.version = 2)), null)).toBeNull()
  })

  it.each([
    ['a missing member', (b: Body) => delete b.lines[0].isBlocked],
    ['an extra member', (b: Body) => (b.instruction.unitPrice = '1')],
    ['an unknown status', (b: Body) => (b.instruction.status = 'Shipped')],
    ['a line count that does not match', (b: Body) => (b.instruction.lineCount = 2)],
    ['a share of a line the instruction does not have', (b: Body) => b.shares.push({
      shareId: 'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee', lineId: 'ffffffff-ffff-ffff-ffff-ffffffffffff', kind: 'Loaded',
      quantity: '1', measurement: 'Measured', sourceShipmentId: null, recordedAtUtc: '2026-09-19T09:10:00Z',
    })],
    ['a load without its measurement', (b: Body) => b.shares.push({
      shareId: 'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee', lineId: b.lines[0].lineId, kind: 'Loaded',
      quantity: '1', measurement: null, sourceShipmentId: null, recordedAtUtc: '2026-09-19T09:10:00Z',
    })],
    ['a zero instructed quantity', (b: Body) => (b.lines[0].instructed = '0')],
    ['an all-zero id', (b: Body) => (b.instruction.customerPartyId = '00000000-0000-0000-0000-000000000000')],
  ])('a body with %s is refused', (_, change) => {
    expect(decodeInstructionDetail(tweak(detailResponseRaw, change), null)).toBeNull()
  })

  it('a detail of another record is not an answer to the one asked', () => {
    expect(decodeInstructionDetail(parse(detailResponseRaw), 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaab')).toBeNull()
  })

  it('reads a problem leniently and a code-less platform 403 as a status only', () => {
    expect(decodeProblem(parse(errorCapRaw), 409)).toMatchObject({ code: 'logistics_load_exceeds_instructed', errors: [{ field: 'quantity' }] })
    expect(decodeProblem(parse(errorForbiddenRaw), 403)).toMatchObject({ code: null, errors: [] })
    expect(decodeProblem(parse(errorCapRaw), 422)).toBeNull()
  })
})

describe('the price-free approved-order-lines read the picker uses (deny-first)', () => {
  it('accepts exactly the sixteen wire members and reads the pinned example', () => {
    expect([...APPROVED_ORDER_LINE_KEYS].sort()).toEqual(Object.keys(approvedOrderLine()).sort())
    const line = decodeApprovedOrderLine(approvedOrderLine())
    expect(line).not.toBeNull()
    expect(Object.keys(line!).sort()).toEqual([...APPROVED_ORDER_LINE_KEYS].sort())
    expect(decodeApprovedOrderLinePage(parse(approvedLinesRaw))?.items[0].quantity).toBe('6')
    expect(decodeApprovedOrderLine(approvedOrderLine({ requestedDeliveryDate: null }))).not.toBeNull()
    expect(decodeApprovedOrderLinePage(parse(approvedLinesRaw))?.items[0].customerName).toBe('SENTETIK Yükleme Müşterisi A.Ş.')
  })

  it.each(['unitPrice', 'priceUnitId', 'discountPercent', 'paymentTerms', 'currencyId', 'price', 'discount', 'payment'])(
    'a body carrying an extra member named %s is REFUSED — the data must not arrive (Sinan §4)',
    (extra) => {
      expect(decodeApprovedOrderLine(approvedOrderLine({ [extra]: '1' }))).toBeNull()
      expect(decodeApprovedOrderLinePage({ items: [approvedOrderLine({ [extra]: null })], nextCursor: null })).toBeNull()
    },
  )

  it.each([
    ['a missing member', (b: Body) => delete b.unitCode],
    ['a missing customer name', (b: Body) => delete b.customerName],
    ['an empty customer name', (b: Body) => (b.customerName = '')],
    ['a numeric quantity', (b: Body) => (b.quantity = 6)],
    ['a zero quantity', (b: Body) => (b.quantity = '0')],
    ['a negative revision', (b: Body) => (b.revision = -1)],
    ['an invalid delivery date', (b: Body) => (b.requestedDeliveryDate = '2026-02-30')],
    ['an all-zero customer id', (b: Body) => (b.customerPartyId = '00000000-0000-0000-0000-000000000000')],
  ])('a line with %s is refused', (_, change) => {
    const body = approvedOrderLine()
    change(body)
    expect(decodeApprovedOrderLine(body)).toBeNull()
  })

  it('a page repeating a line id is refused', () => {
    expect(decodeApprovedOrderLinePage({ items: [approvedOrderLine(), approvedOrderLine()], nextCursor: null })).toBeNull()
  })
})

describe('quantity text input (Ö2, no JS number)', () => {
  it.each([
    ['4,5', '4.5'],
    ['4.5', '4.5'],
    ['0004.500', '4.5'],
    ['1000', '1000'],
    ['000000000000000001.250', '1.25'],
  ])('canonicalises %s to %s without a JS number', (input, expected) => {
    expect(parseQuantityInput(input)).toEqual({ ok: true, value: expected })
  })

  it('refuses more than 15 integer digits (the decimal(18,3) column) before any send', () => {
    expect(parseQuantityInput('1234567890123456')).toEqual({ ok: false, error: 'too-wide' })
  })

  it.each([
    ['', 'required'],
    ['abc', 'invalid'],
    ['1.000', 'ambiguous-separator'],
    ['0', 'not-positive'],
    ['1.2345', 'scale'],
    ['2.9999999999999999999999999999999', 'scale'],
  ])('%s → %s', (input, error) => {
    expect(parseQuantityInput(input)).toEqual({ ok: false, error })
  })
})
