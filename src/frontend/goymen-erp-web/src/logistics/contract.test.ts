import { describe, expect, it } from 'vitest'
import createResponseRaw from './examples/create-response.json?raw'
import detailResponseRaw from './examples/detail-response.json?raw'
import loadResponseRaw from './examples/load-response.json?raw'
import listResponseRaw from './examples/list-response.json?raw'
import errorCapRaw from './examples/error-cap.json?raw'
import errorForbiddenRaw from './examples/error-forbidden.json?raw'
import {
  APPROVED_ORDER_LINE_KEYS,
  CANCEL_REASON_MAX,
  decodeApprovedOrderLine,
  decodeApprovedOrderLinePage,
  decodeInstructionDetail,
  decodeInstructionMutation,
  decodeInstructionPage,
  decodeProblem,
  parseQuantityInput,
} from './contract'
import approvedLinesRaw from './examples/approved-order-lines-response.json?raw'
import { approvedOrderLine, CANCELLED_INSTRUCTION, INSTRUCTION, weighingRecord } from './testHarness'

type Body = Record<string, any>
const parse = (raw: string): Body => JSON.parse(raw) as Body
const tweak = (raw: string, change: (body: Body) => void): Body => {
  const body = parse(raw)
  change(body)
  return body
}
/** F-020: the pinned detail as a Cancelled instruction — the status with its three stored cancel facts. */
const cancelledWith = (change: (b: Body) => void = () => undefined): Body => tweak(detailResponseRaw, (b) => {
  Object.assign(b.instruction, CANCELLED_INSTRUCTION)
  change(b)
})
const NO_CANCEL_FACTS = { cancelledBy: null, cancelledAtUtc: null, cancelReason: null }

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
    ['a summary without the F-020 cancel members', (b: Body) => {
      delete b.instruction.cancelledBy
      delete b.instruction.cancelledAtUtc
      delete b.instruction.cancelReason
    }],
  ])('a body with %s is refused', (_, change) => {
    expect(decodeInstructionDetail(tweak(detailResponseRaw, change), null)).toBeNull()
  })

  it('AT-053: reads the weighbridge record as the backend wrote it, sign kept, and refuses one it cannot vouch for', () => {
    const issuedWith = (weighing: Body | null) => tweak(detailResponseRaw, (b) => {
      b.instruction.status = 'Issued'
      b.weighing = weighing
    })
    const detail = decodeInstructionDetail(issuedWith(weighingRecord()), INSTRUCTION)
    expect(detail?.weighing).toMatchObject({ direction: 'TareFirst', netKg: '4.3', loadedKgAtWeighing: '4.5', differenceKg: '-0.2' })
    expect(decodeInstructionDetail(issuedWith(null), INSTRUCTION)?.weighing).toBeNull()
    expect(decodeInstructionDetail(parse(detailResponseRaw), INSTRUCTION)?.weighing).toBeNull()
    const refused: [string, Body | null, (b: Body) => void][] = [
      ['a missing weighing member', null, (b) => delete b.weighing],
      ['a record on a Draft', weighingRecord(), (b) => (b.instruction.status = 'Draft')],
      ['a number for the difference', weighingRecord({ differenceKg: -0.2 }), () => undefined],
      ['a tolerance verdict member', { ...weighingRecord(), withinTolerance: true }, () => undefined],
      ['a tare that is not the weighing the direction names', weighingRecord({ direction: 'GrossFirst' }), () => undefined],
      ['an unknown direction', weighingRecord({ direction: 'Sideways' }), () => undefined],
      ['a zero net', weighingRecord({ netKg: '0' }), () => undefined],
    ]
    for (const [name, weighing, change] of refused) {
      const body = issuedWith(weighing)
      change(body)
      expect(decodeInstructionDetail(body, INSTRUCTION), name).toBeNull()
    }
  })

  it('F-020: reads a cancelled instruction in the list and the detail, with its kept loads and weighbridge record', () => {
    const page = decodeInstructionPage(tweak(listResponseRaw, (b) => {
      b.items.push({ ...b.items[0], id: 'cccccccc-cccc-cccc-cccc-ccccccccccc2', ...CANCELLED_INSTRUCTION })
    }))
    expect(page?.items.map((item) => item.status)).toEqual(['Draft', 'Cancelled'])
    // One cancelled row without its cancel facts is not vouched for, and with it the whole page.
    expect(decodeInstructionPage(tweak(listResponseRaw, (b) => {
      b.items.push({ ...b.items[0], id: 'cccccccc-cccc-cccc-cccc-ccccccccccc2', status: 'Cancelled' })
    }))).toBeNull()

    const load = (b: Body): Body => ({
      shareId: 'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeee1', lineId: b.lines[0].lineId, kind: 'Loaded',
      quantity: '1', measurement: 'Measured', sourceShipmentId: null, recordedAtUtc: '2026-09-19T09:10:00Z',
    })
    expect(decodeInstructionDetail(cancelledWith(), INSTRUCTION)?.instruction.status).toBe('Cancelled')
    // LOJ-D7: the weighbridge record and the loads of an instruction cancelled after issue stay on it.
    const kept = decodeInstructionDetail(cancelledWith((b) => {
      b.shares.push(load(b))
      b.weighing = weighingRecord()
    }), INSTRUCTION)
    expect(kept?.weighing?.netKg).toBe('4.3')
    expect(kept?.shares).toHaveLength(1)
    expect(decodeInstructionMutation({ detail: cancelledWith(), isReplay: false, originalCorrelationId: 'corr-1' })?.detail.instruction.status).toBe('Cancelled')
    // LOJ-D8: a recorded shipment refuses the cancel, so a cancelled instruction with one is not vouched for.
    expect(decodeInstructionDetail(cancelledWith((b) => {
      b.shares.push({ ...load(b), kind: 'Shipped', measurement: null })
    }), INSTRUCTION)).toBeNull()
  })

  it('F-020 (LOJ-D6…D8): a Cancelled summary carries who cancelled it, when and why, as stored; any other status carries none', () => {
    const facts = { cancelledBy: 'SENTETIK-iptal-eden', cancelledAtUtc: '2026-09-19T11:15:00Z', cancelReason: 'Müşteri vazgeçti' }
    expect(decodeInstructionDetail(cancelledWith(), INSTRUCTION)?.instruction).toMatchObject({ status: 'Cancelled', ...facts })
    expect(decodeInstructionMutation({ detail: cancelledWith(), isReplay: true, originalCorrelationId: 'corr-1' })?.detail.instruction)
      .toMatchObject(facts)
    expect(decodeInstructionPage(tweak(listResponseRaw, (b) => Object.assign(b.items[0], CANCELLED_INSTRUCTION)))?.items[0])
      .toMatchObject(facts)
    // The server writes a zero-offset instant either way; both spellings are the same UTC instant.
    expect(decodeInstructionDetail(cancelledWith((b) => (b.instruction.cancelledAtUtc = '2026-09-19T11:15:00.1234567+00:00')), INSTRUCTION)
      ?.instruction.cancelledAtUtc).toBe('2026-09-19T11:15:00.1234567+00:00')
    // The limits are the columns': a 128-character canceller and a CANCEL_REASON_MAX-character reason are kept whole.
    expect(decodeInstructionDetail(cancelledWith((b) => {
      b.instruction.cancelledBy = 'u'.repeat(128)
      b.instruction.cancelReason = 'r'.repeat(CANCEL_REASON_MAX)
    }), INSTRUCTION)?.instruction).toMatchObject({ cancelledBy: 'u'.repeat(128), cancelReason: 'r'.repeat(CANCEL_REASON_MAX) })

    expect(decodeInstructionDetail(parse(detailResponseRaw), INSTRUCTION)?.instruction).toMatchObject({ status: 'Draft', ...NO_CANCEL_FACTS })
    expect(decodeInstructionDetail(tweak(detailResponseRaw, (b) => (b.instruction.status = 'Issued')), INSTRUCTION)?.instruction)
      .toMatchObject({ status: 'Issued', ...NO_CANCEL_FACTS })
    expect(decodeInstructionPage(parse(listResponseRaw))?.items[0]).toMatchObject(NO_CANCEL_FACTS)
  })

  it.each([
    ['without its canceller', (b: Body) => (b.instruction.cancelledBy = null)],
    ['without its cancel time', (b: Body) => (b.instruction.cancelledAtUtc = null)],
    ['without its reason', (b: Body) => (b.instruction.cancelReason = null)],
    ['without any of the three facts', (b: Body) => Object.assign(b.instruction, NO_CANCEL_FACTS)],
    ['missing a cancel member', (b: Body) => delete b.instruction.cancelledAtUtc],
    ['with a canceller name beside the id (no name lookup)', (b: Body) => (b.instruction.cancelledByName = 'Ad Soyad')],
    ['with a number for the canceller', (b: Body) => (b.instruction.cancelledBy = 42)],
    ['with an empty canceller', (b: Body) => (b.instruction.cancelledBy = '')],
    ['with a canceller longer than its column', (b: Body) => (b.instruction.cancelledBy = 'u'.repeat(129))],
    ['with a date for the cancel time', (b: Body) => (b.instruction.cancelledAtUtc = '2026-09-19')],
    ['with a cancel time without its offset', (b: Body) => (b.instruction.cancelledAtUtc = '2026-09-19T11:15:00')],
    ['with an impossible cancel time', (b: Body) => (b.instruction.cancelledAtUtc = '2026-13-40T11:15:00Z')],
    ['with a number for the cancel time', (b: Body) => (b.instruction.cancelledAtUtc = 1789816500000)],
    ['with a reason that is not text', (b: Body) => (b.instruction.cancelReason = ['Müşteri vazgeçti'])],
    ['with an empty reason', (b: Body) => (b.instruction.cancelReason = '')],
    ['with a blank reason', (b: Body) => (b.instruction.cancelReason = '   ')],
    ['with an untrimmed reason', (b: Body) => (b.instruction.cancelReason = ' Müşteri vazgeçti ')],
    ['with a reason longer than its column', (b: Body) => (b.instruction.cancelReason = 'r'.repeat(CANCEL_REASON_MAX + 1))],
    ['whose status is Issued', (b: Body) => (b.instruction.status = 'Issued')],
    ['whose status is Draft', (b: Body) => (b.instruction.status = 'Draft')],
    ['whose status is Issued with only a reason left', (b: Body) => Object.assign(b.instruction, { status: 'Issued', cancelledBy: null, cancelledAtUtc: null })],
  ])('F-020: a cancel record %s is refused, in the detail, the mutation answer and the list', (_, change) => {
    const body = cancelledWith(change)
    expect(decodeInstructionDetail(body, INSTRUCTION)).toBeNull()
    expect(decodeInstructionMutation({ detail: body, isReplay: false, originalCorrelationId: 'corr-1' })).toBeNull()
    expect(decodeInstructionPage({ items: [body.instruction], nextCursor: null })).toBeNull()
  })

  it('F-020: a status outside Draft / Issued / Cancelled still fails closed, alone or among good rows', () => {
    for (const status of ['Canceled', 'cancelled', 'CANCELLED', 'Voided', '', null, 3]) {
      expect(decodeInstructionDetail(tweak(detailResponseRaw, (b) => (b.instruction.status = status)), INSTRUCTION), String(status)).toBeNull()
      expect(decodeInstructionPage(tweak(listResponseRaw, (b) => {
        b.items.push({ ...b.items[0], id: 'cccccccc-cccc-cccc-cccc-ccccccccccc2', status })
      })), String(status)).toBeNull()
    }
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
