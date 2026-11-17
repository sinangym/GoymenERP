import type { DocumentList } from '../documents/contract'
import type { ImportPreview, ImportPreviewRequest, ImportReservationResult, SourceInvoice } from './invoiceImportContract'
export const importTestId = (n: number) => `00000000-0000-0000-0000-${String(n).padStart(12, '0')}`
const id = importTestId
export const importReservationFixture = (): ImportReservationResult => ({ reservation: { id: id(1), companyId: id(2), version: '1',
  reservedBy: 'synthetic-actor', reservedAtUtc: '2026-11-17T09:00:00Z' }, isReplay: false, originalCorrelationId: 'synthetic-correlation' })
export const importRequestFixture = (): ImportPreviewRequest => ({ expectedVersion: '1', documentId: id(3), documentVersion: 2,
  documentSha256: 'a'.repeat(64), options: { format: 1, delimiter: ';', decimalSeparator: '.', dateFormat: 'yyyy-MM-dd' } })
export const importInvoiceFixture = (): SourceInvoice => ({ number: 'SENTETIK-2026-0001', ettn: id(4), invoiceDate: '2026-11-17', invoiceType: 'SATIS', currency: 'USD',
  supplierIdentities: [{ scheme: 'VKN', value: '0123456789' }], buyerIdentities: [{ scheme: 'VKN', value: '0987654321' }],
  totals: { LineExtensionAmount: { value: '10.123456789123456789', currency: 'USD' }, TaxExclusiveAmount: { value: '10.123456789123456789', currency: 'USD' },
    TaxInclusiveAmount: { value: '11.123456789123456789', currency: 'USD' }, PayableAmount: { value: '11.123456789123456789', currency: 'USD' } },
  taxes: [{ code: 'SOURCE-CODE', percent: '10', amount: { value: '1', currency: 'USD' }, taxableAmount: { value: '10', currency: 'USD' }, isWithholding: false }],
  adjustments: [{ isCharge: true, amount: { value: '0.12', currency: 'USD' }, reason: 'Kaynak masrafı' }],
  rates: [{ purpose: 'TaxExchangeRate', sourceCurrency: 'USD', targetCurrency: 'TRY', rate: '35.123456', sourceBaseRate: '1', targetBaseRate: null, operator: null, date: '2026-11-17' }],
  payments: [{ source: 'PaymentTerms', dueOn: '2026-12-17', installmentDueOn: null, amount: { value: '11.123456789123456789', currency: 'USD' } }],
  lines: [{ id: '001', sellerCode: '0007', buyerCode: null, description: 'Sentetik kaynak malzeme', unitCode: 'KGM', quantity: '0.123456789123456789',
    net: { value: '10.123456789123456789', currency: 'USD' }, taxes: [], adjustments: [] }],
})
export const importPreviewFixture = (): ImportPreview => ({ importId: id(1), companyId: id(2), version: '1', documentId: id(3), documentVersion: 2,
  documentSha256: 'a'.repeat(64), invoice: importInvoiceFixture(), issues: [] })
export const importDocumentsFixture = (): DocumentList => ({ items: [{ id: id(3), companyId: id(2), ownerKind: 'purchasing.invoice-import', ownerId: id(1), versionNo: 2,
  fileName: 'sentetik.xml', contentType: 'application/xml', sizeBytes: 1024, sha256: 'a'.repeat(64), scanState: 'Clean', downloadable: true,
  uploadedBy: 'synthetic-actor', uploadedAtUtc: '2026-11-17T09:00:00Z' }], canUpload: true, canLink: false,
  limits: { maximumBytes: 10485760, extensions: ['.xml', '.xlsx', '.csv'], contentTypes: ['application/xml', 'text/csv', 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'] } })
