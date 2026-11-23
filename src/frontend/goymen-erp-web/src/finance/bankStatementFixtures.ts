// SENTETIK WP12 wire fixtures shared by the decoder and screen tests.
export const company = '11111111-1111-4111-8111-111111111111'
const importId = '22222222-2222-4222-8222-222222222222'
const line = '33333333-3333-4333-8333-333333333333'
const record = '44444444-4444-4444-8444-444444444444'
const match = '55555555-5555-4555-8555-555555555555'
const account = '66666666-6666-4666-8666-666666666666'

export const parsedImport = {
  id: importId, companyId: company, version: '2', bankAccountId: account, format: 'Mt940', bankKey: null, state: 'Parsed',
  documentId: record, documentVersion: 1, documentSha256: 'A'.repeat(64), csvMappingProfileId: null, csvMappingProfileVersion: null,
  lineCount: 2, duplicateLineCount: 0, firstTransactionOn: '2026-11-17', lastTransactionOn: '2026-11-18',
  reservedBy: 'SENTETIK', reservedAtUtc: '2026-11-22T10:00:00+00:00', parsedBy: 'SENTETIK', parsedAtUtc: '2026-11-22T10:01:00+00:00',
}
export const confirmedMatch = {
  id: match, companyId: company, importId, lineId: line, kind: 'Receipt', recordId: record, version: '1', state: 'Confirmed',
  counterpartyCheck: 'NotAvailable', confirmedBy: 'SENTETIK', confirmedAtUtc: '2026-11-22T10:02:00+00:00',
  reversalId: null, reversedBy: null, reversedAtUtc: null, reversalReason: null,
}
export const detailBody = {
  import: parsedImport, suggestedCount: 1, manualCount: 1,
  lines: [
    { id: line, lineNumber: 3, transactionOn: '2026-11-17', valueOn: null, direction: 'Inflow', amount: '100.00', balance: '-5.00',
      currencyText: 'TRY', reference: 'R-1', description: 'SENTETIK havale', counterpartyIban: null,
      suggestion: { status: 'Suggested', manualReason: null, match: null,
        candidate: { kind: 'Receipt', recordId: record, occurredOn: '2026-11-17', counterpartyCheck: 'NotAvailable' } } },
    { id: '77777777-7777-4777-8777-777777777777', lineNumber: 4, transactionOn: '2026-11-18', valueOn: '2026-11-18', direction: 'Outflow',
      amount: '3.5', balance: null, currencyText: null, reference: null, description: 'SENTETIK masraf', counterpartyIban: 'TR130006200000000000000002',
      suggestion: { status: 'Manual', manualReason: 'NoExactCandidate', candidate: null, match: null } },
  ],
}
