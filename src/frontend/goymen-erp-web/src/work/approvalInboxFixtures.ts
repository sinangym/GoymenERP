/** D-05 test fixtures shared by the Work approval tests (synthetic; not a test file itself). */
import { FINANCE_OFFICIAL_INVOICE_IMPORT_KIND } from './approvalInboxContract'

// The #639 wire (WorkApprovalEndpointTests): a Finance pending batch projected as a Work item. Synthetic.
export const COMPANY = '10000000-0000-0000-0000-000000000001'
export const APPROVAL = '11111111-1111-4111-8111-111111111111'
export const item = {
  kind: FINANCE_OFFICIAL_INVOICE_IMPORT_KIND, approvalId: APPROVAL, companyId: COMPANY, status: 'Pending', version: '1',
  batchVersion: 'invoice-v1', checksum: 'a'.repeat(64), templateVersion: 'finance-official-invoice-installment-csv.v1',
  itemCount: 1, submittedBy: 'maker', submittedAtUtc: '2026-09-30T12:00:00Z',
} as const
const row = {
  openItemId: '33333333-3333-4333-8333-333333333333', sourceDocumentId: '44444444-4444-4444-8444-444444444444',
  sourceInstallmentId: '55555555-5555-4555-8555-555555555555', sourceVersion: 'invoice-v1',
  documentKind: 'official-invoice-installment', partyId: '66666666-6666-4666-8666-666666666666',
  companyAccountId: '77777777-7777-4777-8777-777777777777', currencyId: '22222222-2222-2222-2222-222222222222',
  amount: '1250.50', dueOn: '2026-10-31',
} as const
export const detail = {
  item,
  preview: {
    templateVersion: item.templateVersion, sourceSha256: item.checksum, isValid: true,
    batch: { batchId: APPROVAL, companyId: COMPANY, batchVersion: item.batchVersion, batchChecksum: item.checksum, rows: [row] },
    issues: [],
  },
} as const
