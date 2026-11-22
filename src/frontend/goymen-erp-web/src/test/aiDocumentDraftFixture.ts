/** SENTETIK WP-B3 document draft response used by the AI panel tests. */
export const draftField = (code: string, value: string | null, status = 'verified', reasonCode: string | null = null, source = 'structured-file') => ({
  code,
  value,
  status,
  reasonCode,
  source,
})

export function draftBody(overrides: Record<string, unknown> = {}) {
  return {
    draftKind: 'incoming-invoice',
    readingTier: 'structured-file',
    source: { sha256: 'a'.repeat(64), mediaType: 'application/xml', byteLength: 120 },
    quarantineFindings: [],
    documentNumber: draftField('documentNumber', 'SENTETIK-1'),
    documentDate: draftField('documentDate', '2026-11-20'),
    currency: draftField('currency', 'TRY'),
    counterparty: {
      taxId: draftField('counterpartyTaxId', '9990001119', 'unverified', 'counterparty_match_unavailable'),
      name: draftField('counterpartyName', null, 'unverified', 'missing'),
      matchedPartyId: null,
    },
    lines: [
      {
        lineNumber: 1,
        productCode: draftField('productCode', 'KUTU-40'),
        description: draftField('description', 'SENTETIK Kutu profil 40x40x2'),
        quantity: draftField('quantity', '10'),
        unit: draftField('unit', 'C62'),
        unitPrice: draftField('unitPrice', null, 'unverified', 'missing'),
        lineAmount: draftField('lineAmount', '1000'),
        section: draftField('section', '40x40'),
        wallThickness: draftField('wallThickness', '25', 'invalid', 'wall_thickness_exceeds_section'),
        length: draftField('length', null, 'unverified', null),
        matchedProductId: 'dddddddd-0000-4000-8000-0000000000b3',
        matchedProductVersion: 'pv-3',
      },
    ],
    payableAmount: draftField('payableAmount', '1000'),
    openIn: { ownerModule: 'Purchasing', screenCode: 'purchasing.invoices.import' },
    createsRecord: false,
    ...overrides,
  }
}
