import { describe, expect, it } from 'vitest';

import { OWNER_KIND, decodeDocumentList } from './contract';

const COMPANY = 'aaaaaaaa-0000-4000-8000-000000000001';
const ORDER = 'bbbbbbbb-0000-4000-8000-000000000002';
const OTHER = 'cccccccc-0000-4000-8000-000000000003';
const LIMITS = {
  maximumBytes: 20_971_520,
  extensions: ['.pdf', '.png', '.jpg', '.jpeg'],
  contentTypes: ['application/pdf', 'image/png', 'image/jpeg'],
};

function purchaseOrderDocument(over: Record<string, unknown> = {}) {
  return {
    id: 'dddddddd-0000-4000-8000-000000000004',
    companyId: COMPANY,
    ownerKind: OWNER_KIND.purchasingPurchaseOrder,
    ownerId: ORDER,
    versionNo: 1,
    fileName: 'Kantar-fisi.png',
    contentType: 'image/png',
    sizeBytes: 68,
    sha256: 'a'.repeat(64),
    scanState: 'Clean',
    downloadable: true,
    uploadedBy: 'pur01-document-writer',
    uploadedAtUtc: '2026-10-09T09:00:00Z',
    ...over,
  };
}

describe('document owner contract', () => {
  it('pins the canonical counterparty, inventory label and purchase-order identities', () => {
    expect(OWNER_KIND).toEqual({
      counterpartyParty: 'counterparty.party',
      inventoryMaterialLabel: 'inventory.material-label',
      purchasingPurchaseOrder: 'purchasing.purchase-order',
    });
  });

  it('accepts purchase-order rows only for the exact company, owner kind and owner id', () => {
    const response = { items: [purchaseOrderDocument()], canUpload: true, limits: LIMITS };
    expect(decodeDocumentList(response, COMPANY, OWNER_KIND.purchasingPurchaseOrder, ORDER)?.items).toHaveLength(1);
    expect(decodeDocumentList(response, OTHER, OWNER_KIND.purchasingPurchaseOrder, ORDER)).toBeNull();
    expect(decodeDocumentList(response, COMPANY, OWNER_KIND.purchasingPurchaseOrder, OTHER)).toBeNull();
    expect(decodeDocumentList({ ...response, items: [purchaseOrderDocument({ ownerKind: 'counterparty.party' })] },
      COMPANY, OWNER_KIND.purchasingPurchaseOrder, ORDER)).toBeNull();
  });
});
