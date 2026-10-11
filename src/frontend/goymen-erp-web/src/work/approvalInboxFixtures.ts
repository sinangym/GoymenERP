/** D-05 test fixtures shared by the Work approval tests (synthetic; not a test file itself). */
import {
  FINANCE_OFFICIAL_INVOICE_IMPORT_KIND,
  INVENTORY_STOCK_COUNT_KIND,
  INVENTORY_STOCK_SCRAP_KIND,
  SALES_ORDER_DRAFT_KIND,
} from './approvalInboxContract'

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

// The #645 wire (WorkApprovalEndpointTests): a Sales order draft awaiting internal approval. Synthetic.
export const DRAFT = 'aaaaaaaa-1111-4111-8111-000000000645'
export const salesItem = {
  kind: SALES_ORDER_DRAFT_KIND, approvalId: DRAFT, companyId: COMPANY, status: 'PendingApproval', version: '3',
  revision: 'Rev.0', documentNo: 'SO-2026-000123', isOwnDraft: false, submittedBy: 'satis-hazirlayan',
  submittedAtUtc: '2026-10-09T09:15:00Z',
} as const
export const salesDetail = { item: salesItem } as const

// The #667 wire (WorkApprovalEndpointTests): a stock-count difference and a stock scrap awaiting their decision. Synthetic.
export const COUNT = 'cccccccc-1111-4111-8111-000000000667'
export const SCRAP = 'dddddddd-1111-4111-8111-000000000667'
export const LABEL = 'eeeeeeee-1111-4111-8111-000000000667'
export const SCRAP_LABEL = 'eeeeeeee-2222-4222-8222-000000000667'
export const PRODUCT = 'ffffffff-1111-4111-8111-000000000667'
export const LOCATION = 'abababab-1111-4111-8111-000000000667'
export const COUNTERPARTY = 'cdcdcdcd-1111-4111-8111-000000000667'
export const countItem = {
  kind: INVENTORY_STOCK_COUNT_KIND, approvalId: COUNT, companyId: COMPANY, status: 'PendingApproval', version: '4',
  labelId: LABEL, productId: PRODUCT, locationId: LOCATION, differenceKg: '-3.25',
  submittedBy: 'stok-sayici', submittedAtUtc: '2026-10-10T07:00:00+00:00', labelVersion: '4',
  ownerKind: 'Company', ownerId: COMPANY, custodianKind: 'Company', custodianId: COMPANY, block: 'Free',
  bookKg: '1250.5', countedKg: '1247.25', reason: 'Sayımda rulo ucu eksik çıktı; tartı fişi ekte.',
} as const
export const countDetail = { item: countItem } as const
export const scrapItem = {
  kind: INVENTORY_STOCK_SCRAP_KIND, approvalId: SCRAP, companyId: COMPANY, status: 'PendingApproval', version: '7',
  labelId: SCRAP_LABEL, productId: PRODUCT, locationId: LOCATION, scrapKg: '6',
  submittedBy: 'hurda-kaydeden', submittedAtUtc: '2026-10-10T08:00:00Z', labelVersion: '7',
  ownerKind: 'Counterparty', ownerId: COUNTERPARTY, custodianKind: 'Company', custodianId: COMPANY, block: 'Blocked',
  bookKg: '100', remainingKg: '94', reason: 'Kenar ezilmesi; müşteri onayıyla hurdaya ayrıldı.',
} as const
export const scrapDetail = { item: scrapItem } as const
