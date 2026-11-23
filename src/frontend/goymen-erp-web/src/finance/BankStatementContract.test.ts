// SENTETIK: WP12 decoder evidence (exact keys, fail-closed).
import { describe, expect, it } from 'vitest'
import { decodeCommandResult, decodeDetail, decodeImport, decodeImportList, decodeMatchResult } from './BankStatementContract'

import { company, confirmedMatch, detailBody, parsedImport } from './bankStatementFixtures'
const importId = parsedImport.id
const match = confirmedMatch.id
const account = parsedImport.bankAccountId

describe('bank statement decoders', () => {
  it('accepts the exact detail and refuses an extra, missing or mistyped field', () => {
    expect(decodeDetail(detailBody, company, importId)?.lines.length).toBe(2)
    expect(decodeDetail({ ...detailBody, extra: 1 }, company, importId)).toBeNull()
    expect(decodeDetail({ ...detailBody, import: { ...parsedImport, companyId: account } }, company, importId)).toBeNull()
    expect(decodeDetail(detailBody, company, account)).toBeNull()
    expect(decodeDetail({ ...detailBody, suggestedCount: 2 }, company, importId)).toBeNull()
    const [first] = detailBody.lines
    expect(decodeDetail({ ...detailBody, lines: [{ ...first, amount: 100 }] }, company, importId)).toBeNull()
    expect(decodeDetail({ ...detailBody, lines: [{ ...first, suggestion: { ...first.suggestion, candidate: null } }] }, company, importId)).toBeNull()
  })

  it('a confirmed line carries its active match for the same line and record', () => {
    const [first] = detailBody.lines
    const confirmed = { ...first, suggestion: { status: 'Confirmed', manualReason: null, candidate: first.suggestion.candidate, match: confirmedMatch } }
    expect(decodeDetail({ ...detailBody, suggestedCount: 0, manualCount: 2, lines: [confirmed, detailBody.lines[1]] }, company, importId)).not.toBeNull()
    expect(decodeDetail({ ...detailBody, suggestedCount: 0, manualCount: 2,
      lines: [{ ...confirmed, suggestion: { ...confirmed.suggestion, match: { ...confirmedMatch, lineId: match } } }, detailBody.lines[1]] }, company, importId)).toBeNull()
  })

  it('a reserved import has no parse facts and a CSV import names its bank key', () => {
    const reserved = { ...parsedImport, version: '1', state: 'Reserved', documentId: null, documentVersion: null, documentSha256: null,
      lineCount: 0, firstTransactionOn: null, lastTransactionOn: null, parsedBy: null, parsedAtUtc: null }
    expect(decodeImport(reserved, company)).not.toBeNull()
    expect(decodeImport({ ...reserved, lineCount: 1 }, company)).toBeNull()
    expect(decodeImport({ ...reserved, format: 'Csv' }, company)).toBeNull()
    expect(decodeImportList({ companyId: company, imports: [reserved, reserved] }, company)).toBeNull()
  })

  it('command results agree with their outcome', () => {
    expect(decodeCommandResult({ outcome: 'Succeeded', resource: parsedImport, code: null, issues: [], replayed: false }, company)).not.toBeNull()
    expect(decodeCommandResult({ outcome: 'Succeeded', resource: null, code: null, issues: [], replayed: false }, company)).toBeNull()
    expect(decodeCommandResult({ outcome: 'ValidationFailed', resource: null, code: 'finance_bank_statement_file_invalid',
      issues: [{ lineNumber: 4, field: 'amount', code: 'amount_invalid' }], replayed: false }, company)?.issues).toHaveLength(1)
    expect(decodeMatchResult({ outcome: 'Succeeded', resource: confirmedMatch, code: null, replayed: true }, company)?.replayed).toBe(true)
    expect(decodeMatchResult({ outcome: 'Succeeded', resource: { ...confirmedMatch, counterpartyCheck: 'Mismatch' }, code: null, replayed: false }, company)).toBeNull()
  })
})
