/**
 * DEC-FIN-016 wire decoders — fail closed on every shape the server does not send. SENTETIK fixtures.
 */

import { describe, expect, it } from 'vitest'
import {
  decodeCustomerInstrument,
  decodeCustomerInstrumentCommandResult,
  decodeCustomerInstrumentDetail,
  decodeCustomerInstrumentPage,
} from './customerInstrumentContract'
import {
  INSTRUMENT_BANK,
  INSTRUMENT_COMPANY,
  INSTRUMENT_CURRENCY,
  INSTRUMENT_SOURCE,
  instrumentBody,
} from './customerInstrumentFixtures'

describe('customer instrument decoders', () => {
  it('decodes the server shape and binds it to the verified company', () => {
    const decoded = decodeCustomerInstrument(instrumentBody(), INSTRUMENT_COMPANY)
    expect(decoded.ok && decoded.value.amount).toEqual({
      disclosure: 'Visible', money: { amount: '1000.00', currencyId: INSTRUMENT_CURRENCY },
    })
    expect(decodeCustomerInstrument(instrumentBody(), 'bbbbbbbb-0000-4000-8000-000000000002').ok).toBe(false)
  })

  it('refuses a redacted amount with digits, an unknown member, a cheque without a bank and disagreeing state facts', () => {
    const refused = [
      instrumentBody({ amount: { disclosure: 'Redacted', money: { amount: '1000.00', currencyId: INSTRUMENT_CURRENCY } } }),
      instrumentBody({ amount: { disclosure: 'Visible', money: { amount: '1000.00', currencyId: INSTRUMENT_BANK } } }),
      { ...instrumentBody(), extra: true },
      instrumentBody({ bankName: null }),
      instrumentBody({ status: 'Collected', treasuryAccountId: INSTRUMENT_BANK }),
      instrumentBody({ status: 'AtBankForCollection' }),
      instrumentBody({ collectionSourceId: INSTRUMENT_SOURCE }),
      instrumentBody({ status: 'Endorsed' }),
      instrumentBody({ dueOn: '2026-02-30' }),
    ]
    for (const body of refused) expect(decodeCustomerInstrument(body, INSTRUMENT_COMPANY).ok).toBe(false)
    expect(decodeCustomerInstrument(instrumentBody({
      kind: 'PromissoryNote', bankName: null, status: 'Protested',
    }), INSTRUMENT_COMPANY).ok).toBe(true)
    expect(decodeCustomerInstrument(instrumentBody({
      status: 'Collected', treasuryAccountId: INSTRUMENT_BANK, collectionSourceId: INSTRUMENT_SOURCE,
      amount: { disclosure: 'Redacted', money: null },
    }), INSTRUMENT_COMPANY).ok).toBe(true)
  })

  it('decodes a detail with its movements and only known next actions', () => {
    const detail = {
      instrument: instrumentBody(),
      movements: [{
        version: '1', fromStatus: null, toStatus: 'InPortfolio', occurredOn: '2026-09-20', treasuryAccountId: null,
        collectionSourceId: null, note: null, recordedBy: 'SENTETIK-clerk', recordedAtUtc: '2026-09-20T09:00:00+00:00',
      }],
      availableActions: ['DepositForCollection', 'Collect'],
    }
    const decoded = decodeCustomerInstrumentDetail(detail, INSTRUMENT_COMPANY)
    expect(decoded.ok && decoded.value.availableActions).toEqual(['DepositForCollection', 'Collect'])
    expect(decodeCustomerInstrumentDetail({ ...detail, availableActions: ['Endorse'] }, INSTRUMENT_COMPANY).ok).toBe(false)
    expect(decodeCustomerInstrumentPage({ companyId: INSTRUMENT_COMPANY, items: [instrumentBody()], nextCursor: null },
      INSTRUMENT_COMPANY).ok).toBe(true)
  })

  it('a command result carries a resource only when it succeeded', () => {
    const common = { isReplay: false, originalCorrelationId: 'SENTETIK-1', issues: [] }
    expect(decodeCustomerInstrumentCommandResult({ outcome: 'Succeeded', resource: instrumentBody(), ...common },
      INSTRUMENT_COMPANY).ok).toBe(true)
    expect(decodeCustomerInstrumentCommandResult({ outcome: 'Succeeded', resource: null, ...common },
      INSTRUMENT_COMPANY).ok).toBe(false)
    expect(decodeCustomerInstrumentCommandResult({
      outcome: 'ValidationFailed', resource: null, ...common, issues: [{ field: 'serialNumber', code: 'instrument_duplicate' }],
    }, INSTRUMENT_COMPANY).ok).toBe(true)
    expect(decodeCustomerInstrumentCommandResult({ outcome: 'Denied', resource: instrumentBody(), ...common },
      INSTRUMENT_COMPANY).ok).toBe(false)
    expect(decodeCustomerInstrumentCommandResult({ outcome: 'Succeeded', resource: instrumentBody(), ...common,
      isReplay: true, originalCorrelationId: '' }, INSTRUMENT_COMPANY).ok).toBe(false)
  })
})
