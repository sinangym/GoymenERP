import { describe, expect, it } from 'vitest'
import { decodeManualBankPayment, manualBankPaymentRecordRequest } from './manualBankPaymentContract'

const payment = {
  id: '10000000-0000-0000-0000-000000000010',
  companyId: '10000000-0000-0000-0000-000000000001',
  version: '2', stage: 'Approved',
  preparation: {
    partyId: '20000000-0000-0000-0000-000000000001',
    companyAccountId: '30000000-0000-0000-0000-000000000001',
    treasuryAccountId: '40000000-0000-0000-0000-000000000001',
    money: { amount: '125.50', currencyId: '50000000-0000-0000-0000-000000000001' },
    profile: { profileId: '60000000-0000-0000-0000-000000000001', version: '1' },
    occurredOn: '2026-09-24', valueOn: null, bankReference: null, documentId: null,
    description: 'Tedarikci odemesi',
  },
  preparedBy: 'preparer', preparedAtUtc: '2026-09-24T07:59:00+00:00',
  approvedBy: 'approver', approvedAtUtc: '2026-09-24T08:00:00+00:00',
  recordedBy: null, recordedAtUtc: null, moneyEventId: null,
}

describe('manual bank payment contract', () => {
  it('keeps optional evidence null and creates an explicit occurred record request', () => {
    const decoded = decodeManualBankPayment(payment)
    expect(decoded.preparation.bankReference).toBeNull()
    expect(decoded.preparation.documentId).toBeNull()
    expect(manualBankPaymentRecordRequest(decoded, true)).toEqual({
      paymentId: payment.id, expectedVersion: '2', result: 'Occurred',
    })
  })

  it('represents an unknown result explicitly instead of treating it as success', () => {
    const decoded = decodeManualBankPayment(payment)
    expect(manualBankPaymentRecordRequest(decoded, false).result).toBe('Unknown')
  })

  it.each([
    { ...payment, extra: true },
    { ...payment, stage: 'Recorded' },
    { ...payment, version: '02' },
    { ...payment, preparation: { ...payment.preparation, money: { ...payment.preparation.money, amount: 'NaN' } } },
  ])('refuses malformed or internally inconsistent payloads', (candidate) => {
    expect(() => decodeManualBankPayment(candidate)).toThrow()
  })
})
