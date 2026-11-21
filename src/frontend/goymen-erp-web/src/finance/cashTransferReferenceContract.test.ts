import { describe, expect, it } from 'vitest'
import { decodeCashTransferPreviewResult, decodeCashTransferTlReference } from './cashTransferContract'

const COMPANY = '11111111-1111-4111-8111-111111111111'
const USD = '22222222-2222-4222-8222-222222222222'
const TRY = '33333333-3333-4333-8333-333333333333'
const PROFILE = { profileId: '44444444-4444-4444-8444-444444444444', version: '1' }
const reference = (amount: string) => ({ currencyId: TRY, amount, sourceVersion: 'finance.cash-transfer.tl-reference.v1' })
const preparation = {
  kind: 'CurrencyExchange', sourceKind: 'Bank', sourceAccountId: '55555555-5555-4555-8555-555555555555',
  sourceMoney: { amount: '100.00', currencyId: USD }, sourceProfile: PROFILE,
  targetKind: 'Cash', targetAccountId: '66666666-6666-4666-8666-666666666666',
  targetMoney: { amount: '3300.00', currencyId: TRY }, targetProfile: PROFILE,
  rate: null, occurredOn: '2026-11-17', valueOn: null, reference: null, description: null,
}
const sourceComparison = { currencyId: USD, sellingRate: '34', rateDay: preparation.occurredOn, source: 'Tcmb',
  recordId: '77777777-7777-4777-8777-777777777777', recordVersion: '1', bulletinDate: '2026-11-16', provenance: 'SENTETIK' }
const targetComparison = { currencyId: TRY, sellingRate: '1', rateDay: preparation.occurredOn, source: 'FunctionalCurrency',
  recordId: null, recordVersion: null, bulletinDate: null, provenance: null }
const exchange = () => ({ companyId: COMPANY, occurredOn: preparation.occurredOn,
  sourceMoney: preparation.sourceMoney, targetMoney: preparation.targetMoney, sourceProfile: PROFILE, targetProfile: PROFILE,
  transactionRate: '33.00', exactTransactionRate: { numerator: '33', denominator: '1' },
  comparisonRate: '34.00', exactComparisonRate: { numerator: '34', denominator: '1' }, rateDeviation: '-1.00',
  sourceComparison, targetComparison })
const preview = (snapshot: unknown) => decodeCashTransferPreviewResult({ outcome: 'Succeeded', preparation, issues: [], exchange: snapshot })

describe('Q10 exact recorded comparison TL references', () => {
  it('displays the two server values without recomputing money or turning the deviation into an Accounting value', () => {
    const decoded = preview({ ...exchange(), sourceReferenceTl: reference('3400'), targetReferenceTl: reference('3300') })
    expect(decoded.ok).toBe(true)
    if (decoded.ok && decoded.value.outcome === 'Succeeded') {
      expect(decoded.value.exchange?.sourceReferenceTl?.amount).toBe('3400')
      expect(decoded.value.exchange?.targetReferenceTl?.amount).toBe('3300')
      expect(decoded.value.exchange?.rateDeviation).toBe('-1.00')
    }
  })
  it('preserves missing legacy values and refuses half-proofs or malformed provenance', () => {
    const legacy = preview(exchange())
    expect(legacy.ok).toBe(true)
    if (legacy.ok && legacy.value.outcome === 'Succeeded') expect(legacy.value.exchange?.sourceReferenceTl).toBeUndefined()
    expect(preview({ ...exchange(), sourceReferenceTl: reference('3400') }).ok).toBe(false)
    expect(preview({ ...exchange(), sourceReferenceTl: null, targetReferenceTl: null }).ok).toBe(false)
    for (const bad of [{ ...reference('3400'), sourceVersion: 'client' }, { ...reference('3400'), amount: '3400.00' },
      { ...reference('3400'), currencyId: USD }, { ...reference('3400'), bookValue: true }])
      expect(preview({ ...exchange(), sourceReferenceTl: bad, targetReferenceTl: reference('3300') }).ok).toBe(false)
  })
  it('keeps exact long and small finite text instead of parsing a JavaScript monetary number', () => {
    for (const amount of ['999999999999999999000000000000000000', '0.' + '0'.repeat(37) + '1'])
      expect(decodeCashTransferTlReference(reference(amount))?.amount).toBe(amount)
    for (const amount of ['0', '-1', '1e2', '01', '1.00', '1,2']) expect(decodeCashTransferTlReference(reference(amount))).toBeNull()
  })
})
