import { importInvoiceFixture, importRequestFixture, importTestId as id } from './invoiceImportFixtures.testSupport'
import { incomingInvoiceFixture } from './incomingInvoiceFixtures.testSupport'
import type { ImportMappingReferences, ImportRecordSnapshot } from './invoiceImportAdmissionContract'
export const admissionFacts = () => ({ ...importInvoiceFixture(), rates: [], adjustments: [],
  totals: { PayableAmount: { currency: 'USD', value: '12' } },
  payments: [{ dueOn: '2026-12-17', installmentDueOn: null, amount: { currency: 'USD', value: '12' }, source: 'PaymentTerms' }],
  lines: [{ id: '001', sellerCode: '0007', buyerCode: null, description: 'SENTETIK mal', unitCode: 'KGM', quantity: '1', net: { currency: 'USD', value: '10' }, taxes: [], adjustments: [] }] })
export const admissionReferences = (): ImportMappingReferences => ({ companyId: id(2), importId: id(1), documentId: id(3), documentVersion: 2,
  documentSha256: 'a'.repeat(64), invoiceDate: '2026-11-17', units: [{ unitId: id(10), code: 'KGM', profileId: id(11), profileVersion: '3', scale: 3 }],
  currencies: [{ currencyId: id(12), code: 'USD', profileId: id(13), profileVersion: '2', scale: 2 }], payable: '12', functionalPayable: '24', serviceTypes: [], expenseTypes: null })
export const admissionSupplier = () => ({ companyId: id(2), importId: id(1), supplierPartyId: id(14), companyAccountId: id(15), partyVersion: '4', accountVersion: '5' })
export const admissionProducts = () => ({ companyId: id(2), importId: id(1), searchWindowFull: false,
  items: [{ productId: id(16), productVersion: '6', code: 'SYN', name: 'SENTETIK ürün', unitId: id(10), unitCode: 'KGM', profileId: id(11), profileVersion: '3', itemType: 'Steel' as const, itemTypeVersion: '7' }] })
export const admissionSnapshot = (): ImportRecordSnapshot => ({ facts: admissionFacts(), request: { source: importRequestFixture(), expectedInvoiceVersion: '0',
  supplierPartyId: id(14), companyAccountId: id(15), expectedPartyVersion: '4', expectedAccountVersion: '5', currencyId: id(12),
  lines: [{ sourceLineId: '001', lineId: id(17), productId: id(16), expectedProductVersion: '6', unitId: id(10), itemType: 'Steel', linePurpose: 'resale', vatTaxCode: '', expectedItemTypeVersion: '7' }],
  installments: [{ id: id(18), openItemId: id(19), sequence: 1, dueOn: '2026-12-17', amount: '12', functionalAmount: null }] } })
export const admissionResult = (snapshot = admissionSnapshot()) => ({ isReplay: false, originalCorrelationId: 'SENTETIK', financeOutcome: null,
  invoice: { ...incomingInvoiceFixture(), id: id(1), companyId: id(2), supplierPartyId: id(14), companyAccountId: id(15), currencyId: id(12),
    functionalCurrencyId: id(20), officialRate: '2', officialNumber: snapshot.facts.number, ettn: snapshot.facts.ettn, invoiceDate: snapshot.facts.invoiceDate,
    documentId: id(3), documentVersion: '2', functionalAmounts: { net: '20.00', vat: '4.00', withholding: '0.00', payable: '24.00' },
    lines: snapshot.request.lines.map((x, index) => ({ ...incomingInvoiceFixture().lines[0], id: x.lineId, lineNo: index + 1, productId: x.productId,
      unitId: x.unitId, quantity: '1', itemType: x.itemType, linePurpose: x.linePurpose,
      functionalAmounts: { net: '20.00', vat: '4.00', withholding: '0.00', payable: '24.00' } })) } })
