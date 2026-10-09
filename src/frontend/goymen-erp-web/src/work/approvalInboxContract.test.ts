import { describe, expect, it } from 'vitest'
import { APPROVAL, COMPANY, detail, item } from './approvalInboxFixtures'
import {
  FINANCE_OFFICIAL_INVOICE_IMPORT_KIND,
  decodeApprovalDetail,
  decodeApprovalItem,
  decodeApprovalPage,
  workApprovalPath,
} from './approvalInboxContract'

describe('the Work approval read (#639) under Finance\'s own rules', () => {
  it('decodes the page and the detail of the one connected kind', () => {
    expect(decodeApprovalPage({ companyId: COMPANY, items: [item], nextCursor: null })?.items).toEqual([item])
    expect(decodeApprovalPage({ companyId: COMPANY, items: [], nextCursor: 'opaque' })).toEqual({ companyId: COMPANY, items: [], nextCursor: 'opaque' })
    expect(decodeApprovalDetail(detail, APPROVAL)).toEqual({ item, preview: detail.preview })
  })

  it.each([
    ['another kind', { kind: 'sales.order-approval.v1' }],
    ['an approved state', { status: 'Approved' }],
    ['another version', { version: '2' }],
    ['a checksum that is not a SHA-256', { checksum: 'x' }],
    ['no items', { itemCount: 0 }],
    ['an item count as text', { itemCount: '1' }],
    ['no submitter', { submittedBy: '' }],
    ['a time that is not an instant', { submittedAtUtc: 'yesterday' }],
    ['an empty id', { approvalId: '00000000-0000-0000-0000-000000000000' }],
  ])('refuses an item with %s', (_, change) => {
    expect(decodeApprovalItem({ ...item, ...change })).toBeNull()
  })

  it('refuses unknown members, missing members and a whole page carrying one bad item', () => {
    expect(decodeApprovalItem({ ...item, amount: '1' })).toBeNull()
    const { submittedBy: _submittedBy, ...missing } = item
    expect(decodeApprovalItem(missing)).toBeNull()
    expect(decodeApprovalPage({ companyId: COMPANY, items: [item, { ...item, approvalId: '22222222-2222-4222-8222-222222222222', status: 'Approved' }], nextCursor: null })).toBeNull()
  })

  it('refuses another company\'s item, a repeated item and an empty cursor', () => {
    expect(decodeApprovalPage({ companyId: '99999999-9999-4999-8999-999999999999', items: [item], nextCursor: null })).toBeNull()
    expect(decodeApprovalPage({ companyId: COMPANY, items: [item, item], nextCursor: null })).toBeNull()
    expect(decodeApprovalPage({ companyId: COMPANY, items: [], nextCursor: '' })).toBeNull()
    expect(decodeApprovalPage({ companyId: COMPANY, items: [] })).toBeNull()
  })

  it('refuses a detail of another approval or with a preview that does not describe the item', () => {
    expect(decodeApprovalDetail(detail, '22222222-2222-4222-8222-222222222222')).toBeNull()
    expect(decodeApprovalDetail({ ...detail, preview: { ...detail.preview, sourceSha256: 'b'.repeat(64) } }, APPROVAL)).toBeNull()
    expect(decodeApprovalDetail({ ...detail, item: { ...item, itemCount: 2 } }, APPROVAL)).toBeNull()
    expect(decodeApprovalDetail({ ...detail, preview: { ...detail.preview, isValid: false } }, APPROVAL)).toBeNull()
    expect(decodeApprovalDetail({ ...detail, extra: true }, APPROVAL)).toBeNull()
  })

  it('builds the detail path from the kind and the id', () => {
    expect(workApprovalPath(FINANCE_OFFICIAL_INVOICE_IMPORT_KIND, APPROVAL))
      .toBe(`/work/approvals/finance.official-invoice-import.v1/${APPROVAL}`)
  })
})
