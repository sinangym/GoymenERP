// SENTETIK: FNS-D121 decoder and TR mod-97 evidence.
import { describe, expect, it } from 'vitest'
import { decodePartyIbanCommandResult, decodePartyIbanList, groupIban, isIbanShape, normalizeIban } from './partyIban'

const company = 'aaaaaaaa-0000-4000-8000-000000000001'
const party = '11111111-1111-4111-8111-111111111111'
const ibanRow = { id: '22222222-2222-4222-8222-222222222222', companyId: company, partyId: party, iban: 'TR130006200000000000000002',
  bankName: 'SENTETIK Banka', description: null, isActive: true, version: '1', addedBy: 'SENTETIK', addedAtUtc: '2026-11-23T08:00:00+00:00',
  deactivatedBy: null, deactivatedAtUtc: null, deactivationReason: null }

describe('cari IBAN contract', () => {
  it('validates the ISO 13616 shape (FNS-D122, TR keeps 24 digits) and normalizes grouping', () => {
    expect(isIbanShape(normalizeIban('tr13 0006 2000 0000 0000 0000 02'))).toBe(true)
    expect(isIbanShape('TR130006200000000000000003')).toBe(false)
    expect(isIbanShape('TR1300062000000000000000A2')).toBe(false)
    expect(isIbanShape(normalizeIban('DE89 3704 0044 0532 0130 00'))).toBe(true)
    expect(isIbanShape('GB82WEST12345698765432')).toBe(true)
    expect(isIbanShape('NL91ABNA0417164300')).toBe(true)
    expect(isIbanShape('DE88370400440532013000')).toBe(false)
    expect(isIbanShape('TR13')).toBe(false)
    expect(groupIban('TR130006200000000000000002')).toBe('TR13 0006 2000 0000 0000 0000 02')
  })

  it('decodes the exact list and refuses another company, an extra key or an inconsistent passive row', () => {
    expect(decodePartyIbanList({ companyId: company, partyId: party, ibans: [ibanRow] }, company, party)?.ibans).toHaveLength(1)
    expect(decodePartyIbanList({ companyId: party, partyId: party, ibans: [ibanRow] }, company, party)).toBeNull()
    expect(decodePartyIbanList({ companyId: company, partyId: party, ibans: [{ ...ibanRow, extra: 1 }] }, company, party)).toBeNull()
    expect(decodePartyIbanList({ companyId: company, partyId: party, ibans: [{ ...ibanRow, isActive: false }] }, company, party)).toBeNull()
    expect(decodePartyIbanList({ companyId: company, partyId: party, ibans: [{ ...ibanRow, iban: 'TR130006200000000000000003' }] }, company, party)).toBeNull()
  })

  it('a command result agrees with its outcome', () => {
    expect(decodePartyIbanCommandResult({ outcome: 'Succeeded', resource: ibanRow, code: null, replayed: false }, company, party)).not.toBeNull()
    expect(decodePartyIbanCommandResult({ outcome: 'Succeeded', resource: null, code: null, replayed: false }, company, party)).toBeNull()
    expect(decodePartyIbanCommandResult({ outcome: 'VersionConflict', resource: null, code: 'counterparty_party_iban_already_listed',
      replayed: false }, company, party)?.code).toBe('counterparty_party_iban_already_listed')
  })
})
