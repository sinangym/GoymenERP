import { describe, expect, it } from 'vitest'
import {
  APPROVAL, COMPANY, COUNT, DRAFT, SCRAP, countDetail, countItem, detail, item, salesDetail, salesItem, scrapDetail, scrapItem,
} from './approvalInboxFixtures'
import {
  FINANCE_OFFICIAL_INVOICE_IMPORT_KIND as FINANCE,
  INVENTORY_STOCK_COUNT_KIND as COUNT_KIND,
  INVENTORY_STOCK_SCRAP_KIND as SCRAP_KIND,
  SALES_ORDER_DRAFT_KIND as SALES,
  decodeApprovalDetail,
  decodeApprovalItem,
  INVENTORY_LABEL_DISPLAY_CONTRACT,
  LABEL_CODE_MAX_LENGTH,
  WORK_APPROVAL_CONTRACT_HEADER,
  decodeApprovalPage,
  isApprovalKind,
  workApprovalContractOf,
  workApprovalPath,
} from './approvalInboxContract'

const OTHER = '22222222-2222-4222-8222-222222222222'

describe('the Work approval read — Finance under Finance\'s own rules (unchanged)', () => {
  it('decodes the page and the detail of the Finance kind', () => {
    expect(decodeApprovalPage({ companyId: COMPANY, items: [item], nextCursor: null }, FINANCE)?.items).toEqual([item])
    expect(decodeApprovalPage({ companyId: COMPANY, items: [], nextCursor: 'opaque' }, FINANCE)).toEqual({ companyId: COMPANY, items: [], nextCursor: 'opaque' })
    expect(decodeApprovalDetail(detail, FINANCE, APPROVAL)).toEqual({ item, preview: detail.preview })
  })

  it.each([
    ['another kind', { kind: 'sales.order-approval.v1' }],
    ['the Sales kind', { kind: SALES }],
    ['an approved state', { status: 'Approved' }],
    ['another version', { version: '2' }],
    ['a checksum that is not a SHA-256', { checksum: 'x' }],
    ['no items', { itemCount: 0 }],
    ['an item count as text', { itemCount: '1' }],
    ['no submitter', { submittedBy: '' }],
    ['a time that is not an instant', { submittedAtUtc: 'yesterday' }],
    ['an empty id', { approvalId: '00000000-0000-0000-0000-000000000000' }],
  ])('refuses a Finance item with %s', (_, change) => {
    expect(decodeApprovalItem({ ...item, ...change }, FINANCE)).toBeNull()
  })

  it('refuses unknown members, missing members and a whole page carrying one bad item', () => {
    expect(decodeApprovalItem({ ...item, amount: '1' }, FINANCE)).toBeNull()
    const { submittedBy: _submittedBy, ...missing } = item
    expect(decodeApprovalItem(missing, FINANCE)).toBeNull()
    expect(decodeApprovalPage({ companyId: COMPANY, items: [item, { ...item, approvalId: OTHER, status: 'Approved' }], nextCursor: null }, FINANCE)).toBeNull()
  })

  it('refuses another company\'s item, a repeated item and an empty cursor', () => {
    expect(decodeApprovalPage({ companyId: '99999999-9999-4999-8999-999999999999', items: [item], nextCursor: null }, FINANCE)).toBeNull()
    expect(decodeApprovalPage({ companyId: COMPANY, items: [item, item], nextCursor: null }, FINANCE)).toBeNull()
    expect(decodeApprovalPage({ companyId: COMPANY, items: [], nextCursor: '' }, FINANCE)).toBeNull()
    expect(decodeApprovalPage({ companyId: COMPANY, items: [] }, FINANCE)).toBeNull()
  })

  it('refuses a detail of another approval or with a preview that does not describe the item', () => {
    expect(decodeApprovalDetail(detail, FINANCE, OTHER)).toBeNull()
    expect(decodeApprovalDetail({ ...detail, preview: { ...detail.preview, sourceSha256: 'b'.repeat(64) } }, FINANCE, APPROVAL)).toBeNull()
    expect(decodeApprovalDetail({ ...detail, item: { ...item, itemCount: 2 } }, FINANCE, APPROVAL)).toBeNull()
    expect(decodeApprovalDetail({ ...detail, preview: { ...detail.preview, isValid: false } }, FINANCE, APPROVAL)).toBeNull()
    expect(decodeApprovalDetail({ ...detail, extra: true }, FINANCE, APPROVAL)).toBeNull()
  })
})

describe('the Work approval read — Sales order drafts awaiting internal approval (#645)', () => {
  it('decodes the page and the item-only detail of the Sales kind', () => {
    expect(decodeApprovalPage({ companyId: COMPANY, items: [salesItem], nextCursor: 'c-1' }, SALES)).toEqual({
      companyId: COMPANY, items: [salesItem], nextCursor: 'c-1',
    })
    expect(decodeApprovalDetail(salesDetail, SALES, DRAFT)).toEqual({ item: salesItem })
    expect(decodeApprovalItem({ ...salesItem, isOwnDraft: true }, SALES)?.isOwnDraft).toBe(true)
  })

  it.each([
    ['the Finance kind', { kind: FINANCE }],
    ['another state', { status: 'Approved' }],
    ['the Finance state', { status: 'Pending' }],
    ['a version that is not a positive integer', { version: '0' }],
    ['a version as a number', { version: 3 }],
    ['a revision outside Rev.n', { revision: 'R1' }],
    ['a blank document number', { documentNo: '  ' }],
    ['isOwnDraft as text', { isOwnDraft: 'false' }],
    ['no submitter', { submittedBy: '' }],
    ['a time that is not an instant', { submittedAtUtc: '09.10.2026' }],
    ['an empty id', { approvalId: '00000000-0000-0000-0000-000000000000' }],
    ['a company that is not an id', { companyId: 'GDC' }],
  ])('refuses a Sales item with %s', (_, change) => {
    expect(decodeApprovalItem({ ...salesItem, ...change }, SALES)).toBeNull()
  })

  it('refuses a Sales item with Finance members, unknown members or a missing member', () => {
    expect(decodeApprovalItem({ ...salesItem, itemCount: 1 }, SALES)).toBeNull()
    expect(decodeApprovalItem({ ...salesItem, amount: '10' }, SALES)).toBeNull()
    const { revision: _revision, ...missing } = salesItem
    expect(decodeApprovalItem(missing, SALES)).toBeNull()
  })

  it('binds a page to the kind and company asked for', () => {
    // A Finance item in a Sales answer (or the reverse) is not an answer to that request.
    expect(decodeApprovalPage({ companyId: COMPANY, items: [salesItem, item], nextCursor: null }, SALES)).toBeNull()
    expect(decodeApprovalPage({ companyId: COMPANY, items: [salesItem], nextCursor: null }, FINANCE)).toBeNull()
    expect(decodeApprovalPage({ companyId: '99999999-9999-4999-8999-999999999999', items: [salesItem], nextCursor: null }, SALES)).toBeNull()
    expect(decodeApprovalPage({ companyId: COMPANY, items: [salesItem, salesItem], nextCursor: null }, SALES)).toBeNull()
  })

  it('refuses a Sales detail with a Finance preview, of another record, or of the Finance kind', () => {
    expect(decodeApprovalDetail({ ...salesDetail, preview: detail.preview }, SALES, DRAFT)).toBeNull()
    expect(decodeApprovalDetail(salesDetail, SALES, OTHER)).toBeNull()
    expect(decodeApprovalDetail(salesDetail, FINANCE, DRAFT)).toBeNull()
    expect(decodeApprovalDetail(detail, SALES, APPROVAL)).toBeNull()
  })
})

describe('paths and kinds', () => {
  it('builds the detail path from the kind and the id, and knows exactly the connected kinds', () => {
    expect(workApprovalPath(FINANCE, APPROVAL)).toBe(`/work/approvals/finance.official-invoice-import.v1/${APPROVAL}`)
    expect(workApprovalPath(SALES, DRAFT)).toBe(`/work/approvals/sales.order-draft.v1/${DRAFT}`)
    expect([FINANCE, SALES].every(isApprovalKind)).toBe(true)
    expect(isApprovalKind('purchasing.order.v1')).toBe(false)
  })
})

/**
 * #667: Inventory stock-count differences and stock scraps awaiting their decision, read through the same Work read.
 * Each is its own kind with its own exact members; the label, its position and its kilograms are judged by the
 * Inventory owner's own count/scrap rules.
 */
describe('the Work approval read — Inventory stock counts and scraps awaiting a decision (#667)', () => {
  const NIL = '00000000-0000-0000-0000-000000000000'

  it('decodes the page and the item-only detail of each Inventory kind, exactly as published', () => {
    expect(decodeApprovalPage({ companyId: COMPANY, items: [countItem], nextCursor: 'c-1' }, COUNT_KIND)).toEqual({
      companyId: COMPANY, items: [countItem], nextCursor: 'c-1',
    })
    expect(decodeApprovalPage({ companyId: COMPANY, items: [scrapItem], nextCursor: null }, SCRAP_KIND)).toEqual({
      companyId: COMPANY, items: [scrapItem], nextCursor: null,
    })
    expect(decodeApprovalDetail(countDetail, COUNT_KIND, COUNT)).toEqual({ item: countItem })
    expect(decodeApprovalDetail(scrapDetail, SCRAP_KIND, SCRAP)).toEqual({ item: scrapItem })
    // Member order on the wire is not part of the contract (#667 9cf4c35c only reordered it).
    const reordered = Object.fromEntries(Object.entries(countItem).reverse())
    expect(decodeApprovalItem(reordered, COUNT_KIND)).toEqual(countItem)
  })

  it('accepts a surplus, a count of zero, a three-decimal quantity and a seven-digit UTC fraction', () => {
    expect(decodeApprovalItem({ ...countItem, countedKg: '1251', differenceKg: '0.5' }, COUNT_KIND)?.differenceKg).toBe('0.5')
    expect(decodeApprovalItem({ ...countItem, countedKg: '0', differenceKg: '-1250.5' }, COUNT_KIND)?.countedKg).toBe('0')
    expect(decodeApprovalItem({ ...countItem, bookKg: '10.125', countedKg: '10', differenceKg: '-0.125' }, COUNT_KIND)).not.toBeNull()
    expect(decodeApprovalItem({ ...countItem, submittedAtUtc: '2026-10-10T07:00:00.1234567+00:00' }, COUNT_KIND)).not.toBeNull()
    expect(decodeApprovalItem({ ...scrapItem, scrapKg: '100', remainingKg: '0' }, SCRAP_KIND)?.remainingKg).toBe('0')
  })

  it.each([
    ['the scrap kind', { kind: SCRAP_KIND }],
    ['the Sales kind', { kind: SALES }],
    ['an unknown kind', { kind: 'inventory.transfer.v1' }],
    ['a decided state', { status: 'Approved' }],
    ['the no-difference state', { status: 'NoDifference' }],
    ['the Finance state', { status: 'Pending' }],
    ['a version that is not the label version', { version: '5' }],
    ['a version of zero', { version: '0', labelVersion: '0' }],
    ['a version as a number', { version: 4 }],
    ['an empty approval id', { approvalId: NIL }],
    ['a label that is not an id', { labelId: 'ROLL-1' }],
    ['an empty product id', { productId: NIL }],
    ['a location that is not an id', { locationId: 'HOL-1' }],
    ['a company that is not an id', { companyId: 'GDC' }],
    ['an owner kind outside the two', { ownerKind: 'Supplier' }],
    ['an owner kind in another case', { ownerKind: 'company' }],
    ['a holder kind outside the two', { custodianKind: '' }],
    ['an empty owner id', { ownerId: NIL }],
    ['a block outside Free/Blocked', { block: 'Quarantine' }],
    ['no difference', { countedKg: '1250.5', differenceKg: '0' }],
    ['a difference that is not counted − book', { differenceKg: '-3' }],
    ['a difference with the wrong sign', { differenceKg: '3.25' }],
    ['a non-canonical quantity', { bookKg: '1250.50' }],
    ['a comma decimal', { countedKg: '1247,25' }],
    ['a four-decimal quantity', { bookKg: '1250.5001', differenceKg: '-3.2501' }],
    ['a negative counted quantity', { countedKg: '-1', differenceKg: '-1251.5' }],
    ['a book of zero', { bookKg: '0', differenceKg: '1247.25' }],
    ['a quantity as a number', { bookKg: 1250.5 }],
    ['a blank reason', { reason: '   ' }],
    ['a null reason', { reason: null }],
    ['no submitter', { submittedBy: '' }],
    ['a time that is not an instant', { submittedAtUtc: '10.10.2026 07:00' }],
    ['a time that is not UTC', { submittedAtUtc: '2026-10-10T10:00:00+03:00' }],
  ])('refuses a stock-count item with %s', (_, change) => {
    expect(decodeApprovalItem({ ...countItem, ...change }, COUNT_KIND)).toBeNull()
    // One bad item refuses the whole page: nothing of it is partly shown.
    expect(decodeApprovalPage({ companyId: COMPANY, items: [{ ...countItem, ...change }], nextCursor: null }, COUNT_KIND)).toBeNull()
  })

  it.each([
    ['the count kind', { kind: COUNT_KIND }],
    ['a decided state', { status: 'Rejected' }],
    ['a version that is not the label version', { labelVersion: '8' }],
    ['a scrap of zero', { scrapKg: '0', remainingKg: '100' }],
    ['a scrap larger than the book', { scrapKg: '101', remainingKg: '0' }],
    ['a remaining that is not book − scrap', { remainingKg: '95' }],
    ['a negative remaining', { scrapKg: '101', remainingKg: '-1' }],
    ['a non-canonical scrap', { scrapKg: '6.0' }],
    ['a block outside Free/Blocked', { block: 'free' }],
    ['an owner kind outside the two', { ownerKind: 'Customer' }],
    ['a blank reason', { reason: '' }],
    ['a time that is not UTC', { submittedAtUtc: '2026-10-10T08:00:00-05:00' }],
    ['an empty label id', { labelId: NIL }],
  ])('refuses a stock-scrap item with %s', (_, change) => {
    expect(decodeApprovalItem({ ...scrapItem, ...change }, SCRAP_KIND)).toBeNull()
    expect(decodeApprovalPage({ companyId: COMPANY, items: [{ ...scrapItem, ...change }], nextCursor: null }, SCRAP_KIND)).toBeNull()
  })

  it('refuses unknown members, missing members and the other kind\'s members', () => {
    expect(decodeApprovalItem({ ...countItem, labelName: 'Rulo' }, COUNT_KIND)).toBeNull()
    expect(decodeApprovalItem({ ...countItem, scrapKg: '6' }, COUNT_KIND)).toBeNull()
    expect(decodeApprovalItem({ ...scrapItem, countedKg: '94' }, SCRAP_KIND)).toBeNull()
    expect(decodeApprovalItem({ ...scrapItem, decision: null }, SCRAP_KIND)).toBeNull()
    for (const member of Object.keys(countItem)) {
      const { [member]: _dropped, ...missing } = countItem as Record<string, unknown>
      expect(decodeApprovalItem(missing, COUNT_KIND), `count without ${member}`).toBeNull()
    }
    for (const member of Object.keys(scrapItem)) {
      const { [member]: _dropped, ...missing } = scrapItem as Record<string, unknown>
      expect(decodeApprovalItem(missing, SCRAP_KIND), `scrap without ${member}`).toBeNull()
    }
  })

  it('binds a page to the kind, the company and one row per record', () => {
    expect(decodeApprovalPage({ companyId: COMPANY, items: [countItem, scrapItem], nextCursor: null }, COUNT_KIND)).toBeNull()
    expect(decodeApprovalPage({ companyId: COMPANY, items: [countItem], nextCursor: null }, SCRAP_KIND)).toBeNull()
    expect(decodeApprovalPage({ companyId: COMPANY, items: [scrapItem], nextCursor: null }, COUNT_KIND)).toBeNull()
    expect(decodeApprovalPage({ companyId: COMPANY, items: [countItem], nextCursor: null }, FINANCE)).toBeNull()
    expect(decodeApprovalPage({ companyId: COMPANY, items: [countItem], nextCursor: null }, SALES)).toBeNull()
    expect(decodeApprovalPage({ companyId: COMPANY, items: [salesItem], nextCursor: null }, COUNT_KIND)).toBeNull()
    expect(decodeApprovalPage({ companyId: '99999999-9999-4999-8999-999999999999', items: [countItem], nextCursor: null }, COUNT_KIND)).toBeNull()
    expect(decodeApprovalPage({ companyId: COMPANY, items: [countItem, countItem], nextCursor: null }, COUNT_KIND)).toBeNull()
    expect(decodeApprovalPage({ companyId: COMPANY, items: [countItem, { ...countItem, approvalId: COUNT.toUpperCase() }], nextCursor: null }, COUNT_KIND)).toBeNull()
  })

  it('refuses a detail of another record or kind, or one that is not exactly { item }', () => {
    expect(decodeApprovalDetail(countDetail, COUNT_KIND, OTHER)).toBeNull()
    expect(decodeApprovalDetail(countDetail, SCRAP_KIND, COUNT)).toBeNull()
    expect(decodeApprovalDetail(scrapDetail, COUNT_KIND, SCRAP)).toBeNull()
    expect(decodeApprovalDetail({ ...countDetail, preview: detail.preview }, COUNT_KIND, COUNT)).toBeNull()
    expect(decodeApprovalDetail({ ...scrapDetail, history: [] }, SCRAP_KIND, SCRAP)).toBeNull()
    expect(decodeApprovalDetail({ item: { ...countItem, differenceKg: '-3' } }, COUNT_KIND, COUNT)).toBeNull()
    expect(decodeApprovalDetail(countItem, COUNT_KIND, COUNT)).toBeNull()
    expect(decodeApprovalDetail(salesDetail, COUNT_KIND, DRAFT)).toBeNull()
    expect(decodeApprovalDetail(countDetail, SALES, COUNT)).toBeNull()
    expect(decodeApprovalDetail(countDetail, FINANCE, COUNT)).toBeNull()
  })

  it('keeps a kind this client does not know closed, whatever the answer looks like', () => {
    const unknown = 'inventory.transfer.v1' as never
    expect(decodeApprovalItem({ ...countItem, kind: 'inventory.transfer.v1' }, unknown)).toBeNull()
    expect(decodeApprovalItem(countItem, unknown)).toBeNull()
    expect(decodeApprovalPage({ companyId: COMPANY, items: [], nextCursor: null }, unknown)).toBeNull()
    expect(decodeApprovalPage({ companyId: COMPANY, items: [countItem], nextCursor: null }, unknown)).toBeNull()
    expect(decodeApprovalDetail(countDetail, unknown, COUNT)).toBeNull()
    expect(decodeApprovalDetail(detail, unknown, APPROVAL)).toBeNull()
    // Prototype members are not kinds.
    expect(decodeApprovalItem(countItem, 'constructor' as never)).toBeNull()
    expect(decodeApprovalItem(countItem, 'toString' as never)).toBeNull()
  })

  it('leaves Finance and Sales exactly as they were', () => {
    expect(decodeApprovalPage({ companyId: COMPANY, items: [item], nextCursor: null }, FINANCE)?.items).toEqual([item])
    expect(decodeApprovalDetail(detail, FINANCE, APPROVAL)).toEqual({ item, preview: detail.preview })
    expect(decodeApprovalDetail(salesDetail, SALES, DRAFT)).toEqual({ item: salesItem })
  })

  it('knows exactly the four connected kinds and builds their detail paths', () => {
    expect([FINANCE, SALES, COUNT_KIND, SCRAP_KIND].every(isApprovalKind)).toBe(true)
    expect(isApprovalKind('inventory.transfer.v1')).toBe(false)
    expect(isApprovalKind('inventory.stock-count.v2')).toBe(false)
    expect(workApprovalPath(COUNT_KIND, COUNT)).toBe(`/work/approvals/inventory.stock-count.v1/${COUNT}`)
    expect(workApprovalPath(SCRAP_KIND, SCRAP)).toBe(`/work/approvals/inventory.stock-scrap.v1/${SCRAP}`)
  })
})

/**
 * #683: the two Inventory kinds are read under the Work approval contract 2.0, which adds the label's readable code.
 * The code is Inventory's own text — required, trimmed, not blank, at most 64 characters — and is display only: the
 * label id stays the record binding. Finance and Sales have no selector and no such member.
 */
describe('the Work approval contract 2.0 — Inventory label code (#683)', () => {
  const legacy = (source: Record<string, unknown>) => {
    const { labelCode: _dropped, ...rest } = source
    return rest
  }

  it('pins the selector and selects 2.0 for exactly the two Inventory kinds', () => {
    expect(WORK_APPROVAL_CONTRACT_HEADER).toBe('X-ERP-Work-Approval-Contract')
    expect(INVENTORY_LABEL_DISPLAY_CONTRACT).toBe('2.0')
    expect(LABEL_CODE_MAX_LENGTH).toBe(64)
    expect(workApprovalContractOf(COUNT_KIND)).toBe('2.0')
    expect(workApprovalContractOf(SCRAP_KIND)).toBe('2.0')
    expect(workApprovalContractOf(FINANCE)).toBeNull()
    expect(workApprovalContractOf(SALES)).toBeNull()
    expect(workApprovalContractOf('inventory.transfer.v1')).toBeNull()
    expect(workApprovalContractOf('inventory.stock-count.v2')).toBeNull()
  })

  it('carries the published code beside the label id, on the item, the page and the detail', () => {
    expect(decodeApprovalItem(countItem, COUNT_KIND)).toMatchObject({ labelId: countItem.labelId, labelCode: 'RULO-2026-000667' })
    expect(decodeApprovalItem(scrapItem, SCRAP_KIND)).toMatchObject({ labelId: scrapItem.labelId, labelCode: 'ŞERİT-İÇ-2026-000668' })
    expect(decodeApprovalPage({ companyId: COMPANY, items: [countItem], nextCursor: null }, COUNT_KIND)?.items[0].labelCode).toBe('RULO-2026-000667')
    const shown = decodeApprovalDetail(scrapDetail, SCRAP_KIND, SCRAP)
    expect(shown !== null && 'labelCode' in shown.item ? shown.item.labelCode : null).toBe('ŞERİT-İÇ-2026-000668')
  })

  it('accepts the owner\'s whole range: one character, inner spaces, Turkish letters and exactly 64 characters', () => {
    for (const labelCode of ['A', 'RULO 12 / B', 'ÇĞİÖŞÜ-çğıöşü', 'X'.repeat(64)]) {
      expect(decodeApprovalItem({ ...countItem, labelCode }, COUNT_KIND)?.labelCode, labelCode).toBe(labelCode)
      expect(decodeApprovalItem({ ...scrapItem, labelCode }, SCRAP_KIND)?.labelCode, labelCode).toBe(labelCode)
    }
  })

  it.each([
    ['empty', ''],
    ['blank', '   '],
    ['led by a space', ' RULO-1'],
    ['followed by a space', 'RULO-1 '],
    ['followed by a line break', 'RULO-1\n'],
    ['65 characters', 'X'.repeat(65)],
    ['null', null],
    ['a number', 667],
    ['a list', ['RULO-1']],
    ['the label id as an object', { id: 'RULO-1' }],
  ])('refuses an Inventory item whose label code is %s, and the page and detail that carry it', (_, labelCode) => {
    expect(decodeApprovalItem({ ...countItem, labelCode }, COUNT_KIND)).toBeNull()
    expect(decodeApprovalItem({ ...scrapItem, labelCode }, SCRAP_KIND)).toBeNull()
    expect(decodeApprovalPage({ companyId: COMPANY, items: [{ ...countItem, labelCode }], nextCursor: null }, COUNT_KIND)).toBeNull()
    expect(decodeApprovalDetail({ item: { ...scrapItem, labelCode } }, SCRAP_KIND, SCRAP)).toBeNull()
  })

  it('refuses the legacy Inventory shape: an item without a label code is not an answer to the 2.0 read', () => {
    expect(decodeApprovalItem(legacy(countItem), COUNT_KIND)).toBeNull()
    expect(decodeApprovalItem(legacy(scrapItem), SCRAP_KIND)).toBeNull()
    expect(decodeApprovalPage({ companyId: COMPANY, items: [legacy(countItem)], nextCursor: null }, COUNT_KIND)).toBeNull()
    // One legacy row among 2.0 rows refuses the whole page.
    expect(decodeApprovalPage({
      companyId: COMPANY, items: [countItem, legacy({ ...countItem, approvalId: OTHER })], nextCursor: null,
    }, COUNT_KIND)).toBeNull()
    expect(decodeApprovalDetail({ item: legacy(countItem) }, COUNT_KIND, COUNT)).toBeNull()
    expect(decodeApprovalDetail({ item: legacy(scrapItem) }, SCRAP_KIND, SCRAP)).toBeNull()
  })

  it('never reads the code from another member, and keeps the label id as the binding', () => {
    // A code that happens to look like an id is still only text; the id members keep their own rules.
    const shown = decodeApprovalItem({ ...countItem, labelCode: countItem.labelId }, COUNT_KIND)
    expect(shown?.labelCode).toBe(countItem.labelId)
    expect(shown?.labelId).toBe(countItem.labelId)
    expect(decodeApprovalItem({ ...countItem, labelId: 'RULO-2026-000667' }, COUNT_KIND)).toBeNull()
  })

  it('gives Finance and Sales no label code: with one, the item is refused as before', () => {
    expect(decodeApprovalItem({ ...item, labelCode: 'RULO-1' }, FINANCE)).toBeNull()
    expect(decodeApprovalItem({ ...salesItem, labelCode: 'RULO-1' }, SALES)).toBeNull()
    expect(decodeApprovalDetail({ item: { ...salesItem, labelCode: 'RULO-1' } }, SALES, DRAFT)).toBeNull()
    expect(decodeApprovalItem(item, FINANCE)).toEqual(item)
    expect(decodeApprovalItem(salesItem, SALES)).toEqual(salesItem)
  })
})
