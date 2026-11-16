import { describe, expect, it } from 'vitest'
import {
  decodeInventoryLabelTrace,
  decodeInventoryLabelTraceSearch,
  LABEL_TRACE_MAX_SEARCH_RESULTS,
  normalizeLabelTraceSearchText,
  partyRoleOf,
} from './labelTraceContract'

const ROOT = '22222222-2222-4222-8222-222222222222'
const OUTPUT = '23232323-2323-4323-8323-232323232323'
const TRANSFORMATION = '33333333-3333-4333-8333-333333333333'
const ORDER = '44444444-4444-4444-8444-444444444444'
const SOURCE = '55555555-5555-4555-8555-555555555555'
const RECEIPT = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'
const SHIPMENT = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb'
const SUPPLIER = '66666666-6666-4666-8666-666666666666'
const CUSTOMER = '77777777-7777-4777-8777-777777777777'

function link(overrides: Record<string, unknown> = {}) {
  return {
    transformationId: TRANSFORMATION, kind: 'Manufacturing', occurredAtUtc: '2026-11-02T10:00:00Z',
    sourceLabelId: ROOT, outputLabelId: OUTPUT, sourceQuantityKg: '100', outputQuantityKg: '80',
    productionOrderId: ORDER, consumedKg: '100', consumedShare: '1', shareStatus: 'Recorded',
    ...overrides,
  }
}

function receipt(overrides: Record<string, unknown> = {}) {
  return {
    movementId: RECEIPT, lineNumber: 1, labelId: ROOT, step: 'SupplierReceipt', movementKind: 'Receipt',
    occurredAtUtc: '2026-11-01T08:00:00+00:00', sourceKind: 'purchasing.goods-receipt', sourceId: SOURCE, sourceLineId: null,
    direction: 'In', quantityKg: '100', partyId: SUPPLIER, supplierLabelNumber: 'SUP-0001',
    ...overrides,
  }
}

function shipment(overrides: Record<string, unknown> = {}) {
  return {
    movementId: SHIPMENT, lineNumber: 1, labelId: OUTPUT, step: 'Shipment', movementKind: 'Issue',
    occurredAtUtc: '2026-11-03T08:00:00Z', sourceKind: 'logistics.shipment', sourceId: SOURCE, sourceLineId: null,
    direction: 'Out', quantityKg: '80', partyId: CUSTOMER, supplierLabelNumber: null,
    ...overrides,
  }
}

function trace(overrides: Record<string, unknown> = {}) {
  return {
    labelId: ROOT, labelCode: 'SYN-ROOT', direction: 'forward',
    labels: [{ labelId: ROOT, code: 'SYN-ROOT', depth: 0 }, { labelId: OUTPUT, code: 'SYN-OUT', depth: 1 }],
    links: [link()],
    events: [receipt(), shipment()],
    parties: [
      { partyId: SUPPLIER, role: 'Supplier', code: 'TED-1', legalName: 'Sentetik Tedarik A.Ş.', tradeName: null },
      { partyId: CUSTOMER, role: 'Customer', code: 'MUS-1', legalName: 'Sentetik Müşteri Ltd.', tradeName: 'Müşteri' },
    ],
    productionStatus: 'Answered', partyStatus: 'Answered', partyNameStatus: 'Answered', windowFull: false,
    ...overrides,
  }
}

describe('decodeInventoryLabelTrace', () => {
  it('decodes the owner trace with the root first and the production and party answers passed through', () => {
    const decoded = decodeInventoryLabelTrace(trace(), ROOT, 'forward')
    expect(decoded?.labels.map((item) => item.depth)).toEqual([0, 1])
    expect(decoded?.links[0].consumedShare).toBe('1')
    expect(decoded?.events.map((item) => item.step)).toEqual(['SupplierReceipt', 'Shipment'])
    expect(decoded?.parties).toHaveLength(2)
  })

  it('accepts an unanswered enrichment: no production answer and no parties', () => {
    const decoded = decodeInventoryLabelTrace(trace({
      links: [link({ productionOrderId: null, consumedKg: null, consumedShare: null, shareStatus: null })],
      events: [receipt({ partyId: null }), shipment({ partyId: null })],
      parties: [], productionStatus: 'Unavailable', partyStatus: 'NotAnswered', partyNameStatus: 'NotAnswered',
    }), ROOT, 'forward')
    expect(decoded?.productionStatus).toBe('Unavailable')
    expect(decoded?.parties).toEqual([])
  })

  it.each([
    ['another label', trace({ labelId: OUTPUT }), ROOT, 'forward'],
    ['another direction', trace(), ROOT, 'backward'],
    ['an extra key', { ...trace(), extra: 1 }, ROOT, 'forward'],
    ['a missing key', (({ windowFull: _omit, ...rest }) => rest)(trace()), ROOT, 'forward'],
    ['an unknown status', trace({ partyStatus: 'Maybe' }), ROOT, 'forward'],
    ['no root node', trace({ labels: [] }), ROOT, 'forward'],
    ['a root not first', trace({ labels: [{ labelId: OUTPUT, code: 'SYN-OUT', depth: 1 }, { labelId: ROOT, code: 'SYN-ROOT', depth: 0 }] }), ROOT, 'forward'],
    ['a root code mismatch', trace({ labelCode: 'SYN-OTHER' }), ROOT, 'forward'],
    ['a second depth-zero node', trace({ labels: [{ labelId: ROOT, code: 'SYN-ROOT', depth: 0 }, { labelId: OUTPUT, code: 'SYN-OUT', depth: 0 }] }), ROOT, 'forward'],
    ['a repeated label', trace({ labels: [{ labelId: ROOT, code: 'SYN-ROOT', depth: 0 }, { labelId: ROOT.toUpperCase(), code: 'SYN-ROOT', depth: 1 }] }), ROOT, 'forward'],
    ['a link to an unwalked label', trace({ links: [link({ outputLabelId: SOURCE })] }), ROOT, 'forward'],
    ['a repeated link', trace({ links: [link(), link()] }), ROOT, 'forward'],
    ['a production answer on a split', trace({ links: [link({ kind: 'Split' })] }), ROOT, 'forward'],
    ['a partial production answer', trace({ links: [link({ consumedKg: null })] }), ROOT, 'forward'],
    ['a share without an order', trace({ links: [link({ productionOrderId: null, consumedKg: null, shareStatus: null })] }), ROOT, 'forward'],
    ['a share above one', trace({ links: [link({ consumedShare: '1.5' })] }), ROOT, 'forward'],
    ['a non-canonical kg', trace({ events: [receipt({ quantityKg: '100.0' }), shipment()] }), ROOT, 'forward'],
    ['a local instant', trace({ events: [receipt({ occurredAtUtc: '2026-11-01T08:00:00' }), shipment()] }), ROOT, 'forward'],
    ['an event on an unwalked label', trace({ events: [receipt({ labelId: SOURCE }), shipment()] }), ROOT, 'forward'],
    ['a repeated event line', trace({ events: [receipt(), receipt()], parties: [] }), ROOT, 'forward'],
    ['a party on a production step', trace({ events: [receipt(), shipment({ step: 'ProductionIssue' })] }), ROOT, 'forward'],
    ['a supplier number on a shipment', trace({ events: [receipt(), shipment({ supplierLabelNumber: 'SUP-1' })] }), ROOT, 'forward'],
    ['a party no event names', trace({ events: [receipt(), shipment({ partyId: null })] }), ROOT, 'forward'],
    ['a party under the wrong role', trace({ parties: [{ partyId: SUPPLIER, role: 'Customer', code: 'TED-1', legalName: 'X', tradeName: null }] }), ROOT, 'forward'],
  ] as const)('fails closed on %s', (_name, body, labelId, direction) => {
    expect(decodeInventoryLabelTrace(body, labelId, direction)).toBeNull()
  })
})

describe('decodeInventoryLabelTraceSearch', () => {
  const item = (overrides: Record<string, unknown> = {}) => ({
    labelId: ROOT, code: 'SYN-ROOT', supplierLabelNumber: null, matchedBy: 'LabelCode', ...overrides,
  })

  it('decodes matches by our code and by the supplier label number', () => {
    const decoded = decodeInventoryLabelTraceSearch({
      items: [item(), item({ labelId: OUTPUT, supplierLabelNumber: 'SUP-0001', matchedBy: 'SupplierLabelNumber' })],
      windowFull: true, supplierSearchStatus: 'Answered',
    })
    expect(decoded?.items.map((entry) => entry.matchedBy)).toEqual(['LabelCode', 'SupplierLabelNumber'])
    expect(decoded?.windowFull).toBe(true)
  })

  it.each([
    ['an extra key', { items: [], windowFull: false, supplierSearchStatus: 'Answered', more: 1 }],
    ['an unknown status', { items: [], windowFull: false, supplierSearchStatus: 'Later' }],
    ['a supplier match without a number', { items: [item({ matchedBy: 'SupplierLabelNumber' })], windowFull: false, supplierSearchStatus: 'Answered' }],
    ['a repeated label', { items: [item(), item({ labelId: ROOT.toUpperCase() })], windowFull: false, supplierSearchStatus: 'Answered' }],
    ['more than the window', {
      items: Array.from({ length: LABEL_TRACE_MAX_SEARCH_RESULTS + 1 }, (_value, index) =>
        item({ labelId: `00000000-0000-4000-8000-${String(index + 1).padStart(12, '0')}` })),
      windowFull: true, supplierSearchStatus: 'Answered',
    }],
  ] as const)('fails closed on %s', (_name, body) => {
    expect(decodeInventoryLabelTraceSearch(body)).toBeNull()
  })
})

describe('label trace helpers', () => {
  it('normalizes the search text with the owner rule', () => {
    expect(normalizeLabelTraceSearchText('  SUP-1 ')).toBe('SUP-1')
    expect(normalizeLabelTraceSearchText(' a ')).toBeNull()
    expect(normalizeLabelTraceSearchText('x'.repeat(65))).toBeNull()
    expect(normalizeLabelTraceSearchText('ab\u0007c')).toBeNull()
  })

  it('names a party role only for a receipt or a shipment', () => {
    expect(partyRoleOf('SupplierReceipt')).toBe('Supplier')
    expect(partyRoleOf('Shipment')).toBe('Customer')
    expect(partyRoleOf('ProductionOutput')).toBeNull()
  })
})
