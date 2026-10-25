import { describe, expect, it } from 'vitest';

import {
  OWNER_KIND,
  VERSION_PAGE_SIZE,
  decodeDocumentList,
  decodeDocumentVersionPage,
  decodeDocumentVersionWrite,
  versionContentPath,
  versionHistoryPath,
  versionsPath,
} from './contract';

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
  it('pins the canonical counterparty, inventory label, purchase-order and sales-order identities', () => {
    expect(OWNER_KIND).toEqual({
      counterpartyParty: 'counterparty.party',
      inventoryMaterialLabel: 'inventory.material-label',
      purchasingPurchaseOrder: 'purchasing.purchase-order',
      salesOrder: 'sales.order',
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

describe('document version contract (#677)', () => {
  const DOC = purchaseOrderDocument().id;
  const bound = { companyId: COMPANY, ownerKind: OWNER_KIND.purchasingPurchaseOrder, ownerId: ORDER, documentId: DOC };
  const version = (versionNo: number, over: Record<string, unknown> = {}) => purchaseOrderDocument({ versionNo, ...over });
  const page = (versionNos: readonly number[], nextCursor: string | null, over: Record<string, unknown> = {}) => ({
    documentId: DOC,
    versions: versionNos.map((n) => version(n)),
    nextCursor,
    ...over,
  });

  it('pins the version routes and the explicit default page size', () => {
    expect(VERSION_PAGE_SIZE).toBe(50);
    expect(versionsPath(DOC)).toBe(`/documents/${DOC}/versions`);
    expect(versionHistoryPath(DOC, 50, null)).toBe(`/documents/${DOC}/versions?pageSize=50`);
    expect(versionHistoryPath(DOC, 50, '11')).toBe(`/documents/${DOC}/versions?pageSize=50&cursor=11`);
    expect(versionContentPath(DOC, 3)).toBe(`/documents/${DOC}/versions/3/content`);
  });

  it('accepts a newest-first page of the asked document and keeps the server cursor as received', () => {
    const first = decodeDocumentVersionPage(page([3, 2], '2'), { ...bound, pageSize: 2, cursor: null });
    expect(first?.versions.map((v) => v.versionNo)).toEqual([3, 2]);
    expect(first?.nextCursor).toBe('2');
    expect(decodeDocumentVersionPage(page([1], null), { ...bound, pageSize: 2, cursor: '2' })?.nextCursor).toBeNull();
    // An exhausted continuation may be empty; a first page may not.
    expect(decodeDocumentVersionPage(page([], null), { ...bound, pageSize: 2, cursor: '2' })?.versions).toEqual([]);
    expect(decodeDocumentVersionPage(page([], null), { ...bound, pageSize: 2, cursor: null })).toBeNull();
  });

  it.each([
    ['a missing nextCursor', { documentId: DOC, versions: [version(2), version(1)] }, null],
    ['another document id', page([2, 1], null, { documentId: OTHER }), null],
    ['a row of another document', { ...page([2], null), versions: [version(2, { id: OTHER })] }, null],
    ['a row of another company', { ...page([2], null), versions: [version(2, { companyId: OTHER })] }, null],
    ['a row of another owner', { ...page([2], null), versions: [version(2, { ownerId: OTHER })] }, null],
    ['a row of another owner kind', { ...page([2], null), versions: [version(2, { ownerKind: 'counterparty.party' })] }, null],
    ['oldest first', page([1, 2], null), null],
    ['a repeated version', page([2, 2], null), null],
    ['more rows than asked', page([3, 2, 1], null), null],
    ['a numeric cursor', page([3, 2], null, { nextCursor: 2 }), null],
    ['a cursor that is not the last row', page([3, 2], '3'), null],
    ['a cursor on a short page', page([3], '3'), null],
    ['a cursor after version 1', page([2, 1], '1'), null],
    ['a cursor with an empty page', page([], '2'), '5'],
    ['a row at the asked cursor', page([5, 4], null), '5'],
    ['a row above the asked cursor', page([6, 4], null), '5'],
    ['a downloadable unscanned version', { ...page([2], null), versions: [version(2, { scanState: 'NotScanned' })] }, null],
  ] as const)('refuses %s', (_name, answer, cursor) => {
    expect(decodeDocumentVersionPage(answer, { ...bound, pageSize: 2, cursor })).toBeNull();
  });

  it('accepts an append answer only for the same document, company, owner, next version and sent size', () => {
    const sent = { ...bound, expectedCurrentVersion: 1, sizeBytes: 68 };
    const answer = { document: version(2), isReplay: false, originalCorrelationId: 'c-1' };
    expect(decodeDocumentVersionWrite(answer, sent)?.document.versionNo).toBe(2);
    expect(decodeDocumentVersionWrite({ ...answer, isReplay: true }, sent)?.isReplay).toBe(true);
    expect(decodeDocumentVersionWrite(answer, { ...sent, expectedCurrentVersion: 2 })).toBeNull();
    expect(decodeDocumentVersionWrite({ ...answer, document: version(1) }, sent)).toBeNull();
    expect(decodeDocumentVersionWrite(answer, { ...sent, sizeBytes: 67 })).toBeNull();
    expect(decodeDocumentVersionWrite(answer, { ...sent, documentId: OTHER })).toBeNull();
    expect(decodeDocumentVersionWrite(answer, { ...sent, companyId: OTHER })).toBeNull();
    expect(decodeDocumentVersionWrite(answer, { ...sent, ownerId: OTHER })).toBeNull();
    expect(decodeDocumentVersionWrite({ document: version(2), originalCorrelationId: 'c-1' }, sent)).toBeNull();
    expect(decodeDocumentVersionWrite({ document: version(2), isReplay: false, originalCorrelationId: '' }, sent)).toBeNull();
    expect(decodeDocumentVersionWrite({ document: null, isReplay: false, originalCorrelationId: 'c-1' }, sent)).toBeNull();
  });
});
