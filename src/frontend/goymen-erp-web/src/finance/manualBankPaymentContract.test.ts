import { describe, expect, it } from 'vitest'
import {
  decodeManualBankPaymentCommandResult,
  decodeManualBankPaymentPage,
  decodeManualBankPaymentView,
} from './manualBankPaymentContract'

const COMPANY = '11111111-1111-4111-8111-111111111111'

function recorded(overrides: Record<string, unknown> = {}) {
  return {
    id: '22222222-2222-4222-8222-222222222222',
    companyId: COMPANY,
    status: 'Recorded',
    linkedDocument: null,
    preparation: {
      partyId: '33333333-3333-4333-8333-333333333333',
      companyAccountId: '44444444-4444-4444-8444-444444444444',
      treasuryAccountId: '55555555-5555-4555-8555-555555555555',
      money: { amount: '1250.50', currencyId: '66666666-6666-4666-8666-666666666666' },
      profile: { profileId: '77777777-7777-4777-8777-777777777777', version: '9007199254740993' },
      occurredOn: '2026-09-23',
      valueOn: null,
      bankReference: null,
      description: 'SENTETIK giden odeme',
    },
    preparedBy: 'synthetic-maker',
    preparedAtUtc: '2026-09-23T18:00:00Z',
    approvedBy: 'synthetic-checker',
    approvedAtUtc: '2026-09-23T18:10:00Z',
    recordedBy: 'synthetic-final-actor',
    recordedAtUtc: '2026-09-23T18:20:00Z',
    bankResultConfirmed: true,
    version: '3',
    ...overrides,
  }
}

describe('manual bank payment v1 decoder', () => {
  it('accepts A-now recorded payment without a bank reference or document', () => {
    const decoded = decodeManualBankPaymentView(recorded(), COMPANY)

    expect(decoded.ok).toBe(true)
    if (decoded.ok) expect(decoded.value.preparation.bankReference).toBeNull()
  })

  it('accepts a canonical linked document and rejects malformed identity or version', () => {
    const documentId = '88888888-8888-4888-8888-888888888888'
    expect(decodeManualBankPaymentView(recorded({ linkedDocument: { documentId, version: 'owner-v7' } }), COMPANY).ok)
      .toBe(true)
    expect(decodeManualBankPaymentView(recorded({ linkedDocument: { documentId: COMPANY, version: '' } }), COMPANY).ok)
      .toBe(false)
  })

  it('rejects unknown bank outcome and maker-checker collapse', () => {
    expect(decodeManualBankPaymentView(recorded({ bankResultConfirmed: false }), COMPANY).ok).toBe(false)
    expect(decodeManualBankPaymentView(recorded({ approvedBy: 'synthetic-maker' }), COMPANY).ok).toBe(false)
  })

  it('binds page and success resources to the active company', () => {
    expect(decodeManualBankPaymentPage({ companyId: COMPANY, items: [recorded()], nextCursor: null }, COMPANY).ok)
      .toBe(true)
    expect(decodeManualBankPaymentPage({ companyId: COMPANY, items: [recorded({ companyId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa' })], nextCursor: null }, COMPANY).ok)
      .toBe(false)

    const result = {
      outcome: 'Succeeded',
      resource: recorded(),
      isReplay: false,
      originalCorrelationId: 'synthetic-correlation',
      issues: [],
    }
    expect(decodeManualBankPaymentCommandResult(result, COMPANY).ok).toBe(true)
    expect(decodeManualBankPaymentCommandResult({ ...result, resource: null }, COMPANY).ok).toBe(false)
  })

  it('rejects lossy money and invalid lifecycle timestamps', () => {
    const value = recorded()
    value.preparation = { ...value.preparation, money: { ...value.preparation.money, amount: '1e3' } }
    expect(decodeManualBankPaymentView(value, COMPANY).ok).toBe(false)
    expect(decodeManualBankPaymentView(recorded({ status: 'Approved', recordedBy: null }), COMPANY).ok).toBe(false)
  })
})
