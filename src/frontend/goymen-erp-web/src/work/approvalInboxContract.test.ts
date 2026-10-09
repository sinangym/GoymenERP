import { describe, expect, it } from 'vitest'
import { APPROVAL, COMPANY, DRAFT, detail, item, salesDetail, salesItem } from './approvalInboxFixtures'
import {
  FINANCE_OFFICIAL_INVOICE_IMPORT_KIND as FINANCE,
  SALES_ORDER_DRAFT_KIND as SALES,
  decodeApprovalDetail,
  decodeApprovalItem,
  decodeApprovalPage,
  isApprovalKind,
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
