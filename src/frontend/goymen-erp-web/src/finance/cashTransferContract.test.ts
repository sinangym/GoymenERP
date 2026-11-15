import { describe, expect, it } from 'vitest'
import {
  decodeCashTransferAccountContext,
  decodeCashTransferCommandResult,
  decodeCashTransferFor,
  decodeCashTransferPage,
  decodeCashTransferPreparation,
  decodeCashTransferPreviewResult,
  decodeCashTransferView,
  isRateText,
} from './cashTransferContract'
import { samePreparation } from './cashTransferHttp'

// SENTETIK decoder fixtures: shapes only, no real company, account or person.
const COMPANY = '11111111-1111-4111-8111-111111111111'
const OTHER = '99999999-9999-4999-8999-999999999999'
const TRANSFER = '33333333-3333-4333-8333-333333333333'
const SOURCE = '44444444-4444-4444-8444-444444444444'
const TARGET = '55555555-5555-4555-8555-555555555555'
const TRY = '22222222-2222-4222-8222-222222222222'
const EUR = '77777777-7777-4777-8777-777777777777'
const PROFILE = '66666666-6666-4666-8666-666666666666'

const sameCurrency = (patch: Record<string, unknown> = {}) => ({
  kind: 'SameCurrency',
  sourceKind: 'Bank',
  sourceAccountId: SOURCE,
  sourceMoney: { amount: '1000.00', currencyId: TRY },
  sourceProfile: { profileId: PROFILE, version: '3' },
  targetKind: 'Cash',
  targetAccountId: TARGET,
  targetMoney: { amount: '1000.00', currencyId: TRY },
  targetProfile: { profileId: PROFILE, version: '3' },
  rate: null,
  occurredOn: '2026-11-10',
  valueOn: null,
  reference: null,
  description: 'SENTETIK kasaya çekiş',
  ...patch,
})

const exchange = (patch: Record<string, unknown> = {}) => sameCurrency({
  kind: 'CurrencyExchange',
  targetKind: 'Bank',
  sourceMoney: { amount: '100.00', currencyId: EUR },
  targetMoney: { amount: '3650.00', currencyId: TRY },
  rate: '36.5',
  ...patch,
})

const view = (patch: Record<string, unknown> = {}) => ({
  id: TRANSFER,
  companyId: COMPANY,
  preparation: sameCurrency(),
  recordedBy: 'treasurer-a',
  recordedAtUtc: '2026-11-10T09:00:00+00:00',
  transferConfirmed: true,
  version: '1',
  ...patch,
})

describe('cash transfer preparation decoder', () => {
  it('accepts a same-currency transfer and an exchange as sent', () => {
    expect(decodeCashTransferPreparation(sameCurrency())).toEqual(sameCurrency())
    expect(decodeCashTransferPreparation(exchange())).toEqual(exchange())
  })

  it('refuses unknown or missing keys', () => {
    expect(decodeCashTransferPreparation({ ...sameCurrency(), extra: 1 })).toBeNull()
    const { rate: _rate, ...withoutRate } = sameCurrency()
    expect(decodeCashTransferPreparation(withoutRate)).toBeNull()
  })

  it('refuses the same account on both legs', () => {
    expect(decodeCashTransferPreparation(sameCurrency({ targetKind: 'Bank', targetAccountId: SOURCE }))).toBeNull()
  })

  it('allows the same id on legs of different kinds', () => {
    expect(decodeCashTransferPreparation(sameCurrency({ targetKind: 'Cash', targetAccountId: SOURCE }))).not.toBeNull()
  })

  it('refuses a same-currency transfer with two currencies, two amounts or a rate', () => {
    expect(decodeCashTransferPreparation(sameCurrency({ targetMoney: { amount: '1000.00', currencyId: EUR } }))).toBeNull()
    expect(decodeCashTransferPreparation(sameCurrency({ targetMoney: { amount: '1000', currencyId: TRY } }))).toBeNull()
    expect(decodeCashTransferPreparation(sameCurrency({ rate: '1' }))).toBeNull()
  })

  it('refuses an exchange in one currency or without a rate', () => {
    expect(decodeCashTransferPreparation(exchange({ targetMoney: { amount: '100.00', currencyId: EUR } }))).toBeNull()
    expect(decodeCashTransferPreparation(exchange({ rate: null }))).toBeNull()
  })

  it('refuses a value date before the occurred date and over-long texts', () => {
    expect(decodeCashTransferPreparation(sameCurrency({ valueOn: '2026-11-09' }))).toBeNull()
    expect(decodeCashTransferPreparation(sameCurrency({ valueOn: '2026-11-10' }))).not.toBeNull()
    expect(decodeCashTransferPreparation(sameCurrency({ description: 'x'.repeat(1001) }))).toBeNull()
    expect(decodeCashTransferPreparation(sameCurrency({ reference: '   ' }))).toBeNull()
  })

  it('refuses an unknown kind or account kind', () => {
    expect(decodeCashTransferPreparation(sameCurrency({ kind: 'Exchange' }))).toBeNull()
    expect(decodeCashTransferPreparation(sameCurrency({ sourceKind: 'Card' }))).toBeNull()
  })
})

describe('cash transfer rate grammar', () => {
  it('takes a positive decimal within 18 integer and 10 fractional digits', () => {
    expect(isRateText('36.5')).toBe(true)
    expect(isRateText('0.0000000001')).toBe(true)
    expect(isRateText('123456789012345678')).toBe(true)
  })

  it('refuses zero, a sign, a comma, too many digits and non-strings', () => {
    expect(isRateText('0')).toBe(false)
    expect(isRateText('-1')).toBe(false)
    expect(isRateText('36,5')).toBe(false)
    expect(isRateText('0.00000000001')).toBe(false)
    expect(isRateText('1234567890123456789')).toBe(false)
    expect(isRateText(36.5)).toBe(false)
  })
})

describe('cash transfer view and page decoders', () => {
  it('accepts a confirmed transfer for the active company', () => {
    expect(decodeCashTransferView(view(), COMPANY).ok).toBe(true)
  })

  it('refuses another company, an unconfirmed transfer or a missing recorder', () => {
    expect(decodeCashTransferView(view({ companyId: OTHER }), COMPANY).ok).toBe(false)
    expect(decodeCashTransferView(view({ transferConfirmed: false }), COMPANY).ok).toBe(false)
    expect(decodeCashTransferView(view({ recordedBy: '' }), COMPANY).ok).toBe(false)
  })

  it('answers the detail only for the transfer asked for', () => {
    expect(decodeCashTransferFor(view(), COMPANY, TRANSFER)).not.toBeNull()
    expect(decodeCashTransferFor(view(), COMPANY, SOURCE)).toBeNull()
  })

  it('refuses a page for another company, a duplicate row or an empty cursor', () => {
    expect(decodeCashTransferPage({ companyId: COMPANY, items: [view()], nextCursor: 'c2' }, COMPANY)?.items).toHaveLength(1)
    expect(decodeCashTransferPage({ companyId: OTHER, items: [], nextCursor: null }, COMPANY)).toBeNull()
    expect(decodeCashTransferPage({ companyId: COMPANY, items: [view(), view()], nextCursor: null }, COMPANY)).toBeNull()
    expect(decodeCashTransferPage({ companyId: COMPANY, items: [], nextCursor: '' }, COMPANY)).toBeNull()
  })
})

describe('cash transfer command and preview results', () => {
  it('takes a success with its resource and a refusal without one', () => {
    const success = decodeCashTransferCommandResult(
      { outcome: 'Succeeded', resource: view(), isReplay: false, originalCorrelationId: 'corr-1', issues: [] }, COMPANY)
    expect(success.ok).toBe(true)
    const refusal = decodeCashTransferCommandResult({
      outcome: 'ValidationFailed', resource: null, isReplay: false, originalCorrelationId: 'corr-1',
      issues: [{ field: 'sourceAccountId', code: 'finance_treasury_balance_insufficient' }],
    }, COMPANY)
    expect(refusal.ok).toBe(true)
  })

  it('refuses a refusal carrying a resource and a replay without its correlation', () => {
    expect(decodeCashTransferCommandResult(
      { outcome: 'VersionConflict', resource: view(), isReplay: false, originalCorrelationId: 'c', issues: [] }, COMPANY).ok)
      .toBe(false)
    expect(decodeCashTransferCommandResult(
      { outcome: 'Succeeded', resource: view(), isReplay: true, originalCorrelationId: '', issues: [] }, COMPANY).ok)
      .toBe(false)
  })

  it('takes a preview success without issues and a refusal with at least one', () => {
    expect(decodeCashTransferPreviewResult({ outcome: 'Succeeded', preparation: exchange(), issues: [] }).ok).toBe(true)
    expect(decodeCashTransferPreviewResult({
      outcome: 'ValidationFailed', preparation: null, issues: [{ field: 'rate', code: 'finance_exchange_rate_rule_unavailable' }],
    }).ok).toBe(true)
    expect(decodeCashTransferPreviewResult({ outcome: 'ValidationFailed', preparation: null, issues: [] }).ok).toBe(false)
    expect(decodeCashTransferPreviewResult({
      outcome: 'Succeeded', preparation: exchange(), issues: [{ field: 'rate', code: 'x' }],
    }).ok).toBe(false)
    expect(decodeCashTransferPreviewResult({ outcome: 'Denied', preparation: null, issues: [] }).ok).toBe(false)
  })
})

describe('cash transfer account context', () => {
  const request = { accountKind: 'Bank', accountId: SOURCE, occurredOn: '2026-11-10' } as const
  const context = (patch: Record<string, unknown> = {}) => ({
    companyId: COMPANY, accountKind: 'Bank', accountId: SOURCE, currencyId: TRY,
    profile: { profileId: PROFILE, version: '3' }, occurredOn: '2026-11-10', ...patch,
  })

  it('answers exactly what was asked', () => {
    expect(decodeCashTransferAccountContext(context(), COMPANY, request)?.currencyId).toBe(TRY)
  })

  it('refuses another company, account kind, account or date', () => {
    expect(decodeCashTransferAccountContext(context({ companyId: OTHER }), COMPANY, request)).toBeNull()
    expect(decodeCashTransferAccountContext(context({ accountKind: 'Cash' }), COMPANY, request)).toBeNull()
    expect(decodeCashTransferAccountContext(context({ accountId: TARGET }), COMPANY, request)).toBeNull()
    expect(decodeCashTransferAccountContext(context({ occurredOn: '2026-11-11' }), COMPANY, request)).toBeNull()
  })
})

describe('samePreparation', () => {
  it('compares text for text, independent of key order', () => {
    const a = decodeCashTransferPreparation(exchange())
    const reordered = decodeCashTransferPreparation(Object.fromEntries(Object.entries(exchange()).reverse()))
    expect(a).not.toBeNull()
    expect(samePreparation(a!, reordered!)).toBe(true)
    expect(samePreparation(a!, decodeCashTransferPreparation(exchange({ rate: '36.50' }))!)).toBe(false)
  })
})
