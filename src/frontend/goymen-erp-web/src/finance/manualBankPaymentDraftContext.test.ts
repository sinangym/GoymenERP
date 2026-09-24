import { describe, expect, it } from 'vitest'
import { decodeManualBankPaymentDraftContext } from './manualBankPaymentDraftContext'

const companyId = '10000000-0000-0000-0000-000000000001'
const request = { treasuryAccountId: '30000000-0000-0000-0000-000000000003',
  partyId: '40000000-0000-0000-0000-000000000004',
  companyAccountId: '50000000-0000-0000-0000-000000000005', occurredOn: '2026-09-24' } as const
const normal = {
  companyId, occurredOn: request.occurredOn,
  treasuryAccount: { id: request.treasuryAccountId, companyId,
    currencyId: '60000000-0000-0000-0000-000000000006', isActive: true, isUsableForPayment: true,
    displayName: 'Ana odeme hesabi', version: '7' },
  currency: { currencyId: '60000000-0000-0000-0000-000000000006', displayCode: 'TRY',
    profile: { reference: { profileId: '70000000-0000-0000-0000-000000000007', version: '9' },
      scale: 2, effectiveFrom: '2026-01-01', effectiveThrough: null } },
  party: { partyId: request.partyId, companyAccountId: request.companyAccountId, code: 'SUP-004',
    legalName: 'Tedarikci Dort A.S.', tradeName: null, partyVersion: 'PARTY-3',
    companyAccountVersion: 'ACCOUNT-5' },
}

describe('manual bank payment draft context', () => {
  it('accepts the exact company-bound outgoing-payment context', () => {
    expect(decodeManualBankPaymentDraftContext(normal, companyId, request)).toEqual({ ok: true, value: normal })
  })
  it.each([
    ['unknown member', { ...normal, unexpected: true }],
    ['foreign company', { ...normal, companyId: '90000000-0000-0000-0000-000000000009' }],
    ['receipt-only account', { ...normal, treasuryAccount: { ...normal.treasuryAccount, isUsableForPayment: false } }],
    ['other account', { ...normal, treasuryAccount: { ...normal.treasuryAccount,
      id: '90000000-0000-0000-0000-000000000009' } }],
    ['other currency', { ...normal, currency: { ...normal.currency,
      currencyId: '90000000-0000-0000-0000-000000000009' } }],
    ['profile not effective', { ...normal, currency: { ...normal.currency,
      profile: { ...normal.currency.profile, effectiveFrom: '2026-09-25' } } }],
    ['missing nullable member', { ...normal, party: { ...normal.party, tradeName: undefined } }],
  ])('refuses %s', (_name, body) => {
    expect(decodeManualBankPaymentDraftContext(body, companyId, request)).toEqual({ ok: false })
  })
})
