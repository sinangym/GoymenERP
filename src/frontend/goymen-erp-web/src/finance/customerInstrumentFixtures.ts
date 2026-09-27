/**
 * Test-only SENTETIK fixtures of the DEC-FIN-016 portfolio wire shapes (component and decoder evidence only).
 */

import { EXAMPLE_BANK, EXAMPLE_COMPANY, EXAMPLE_CURRENCY } from './contractExamples'

/** The harness's company, currency and bank account, so the portfolio joins the same SENTETIK bank read. */
export const INSTRUMENT_COMPANY = EXAMPLE_COMPANY
export const INSTRUMENT_ID = 'c1000000-0000-4000-8000-000000000001'
export const INSTRUMENT_CURRENCY = EXAMPLE_CURRENCY
export const INSTRUMENT_BANK = EXAMPLE_BANK
export const INSTRUMENT_SOURCE = 'c4000000-0000-4000-8000-000000000004'

export function instrumentBody(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    id: INSTRUMENT_ID,
    companyId: INSTRUMENT_COMPANY,
    version: '1',
    kind: 'Cheque',
    status: 'InPortfolio',
    partyId: 'c5000000-0000-4000-8000-000000000005',
    companyAccountId: 'c6000000-0000-4000-8000-000000000006',
    serialNumber: 'SENTETIK-CEK-1',
    drawer: 'SENTETIK Keşideci A.Ş.',
    bankName: 'SENTETIK Banka',
    branchName: null,
    dueOn: '2026-10-15',
    receivedOn: '2026-09-20',
    currencyId: INSTRUMENT_CURRENCY,
    amount: { disclosure: 'Visible', money: { amount: '1000.00', currencyId: INSTRUMENT_CURRENCY } },
    description: null,
    treasuryAccountId: null,
    statusChangedOn: '2026-09-20',
    collectionSourceId: null,
    receivedBy: 'SENTETIK-clerk',
    receivedAtUtc: '2026-09-20T09:00:00+00:00',
    ...overrides,
  }
}

export function movementBody(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    version: '1',
    fromStatus: null,
    toStatus: 'InPortfolio',
    occurredOn: '2026-09-20',
    treasuryAccountId: null,
    collectionSourceId: null,
    note: null,
    recordedBy: 'SENTETIK-clerk',
    recordedAtUtc: '2026-09-20T09:00:00+00:00',
    ...overrides,
  }
}

export function detailBody(
  instrument: Record<string, unknown> = instrumentBody(),
  availableActions: readonly string[] = ['DepositForCollection', 'Dishonour', 'ReturnToCustomer', 'Collect'],
  movements: readonly Record<string, unknown>[] = [movementBody()],
): Record<string, unknown> {
  return { instrument, movements, availableActions }
}

type Bucket = { readonly count: number; readonly amount: string | null }

/** A risk figures body: `null` amount = redacted. Every other bucket is an empty visible zero. */
export function riskFigures(buckets: Partial<Record<string, Bucket>> = {}, currencyId = INSTRUMENT_CURRENCY) {
  const names = [
    'inPortfolioNotDue', 'inPortfolioOverdue', 'atBankForCollection', 'dishonoured', 'returnedToCustomer', 'collected',
    'openExposure',
  ]
  const redacted = Object.values(buckets).some((bucket) => bucket?.amount === null)
  return Object.fromEntries(names.map((name) => {
    const bucket = buckets[name] ?? { count: 0, amount: redacted ? null : '0' }
    return [name, {
      count: bucket.count,
      amount: bucket.amount === null
        ? { disclosure: 'Redacted', money: null }
        : { disclosure: 'Visible', money: { amount: bucket.amount, currencyId } },
    }]
  }))
}

export function riskBody(
  figures: Record<string, unknown>,
  party: { readonly partyId: string; readonly companyAccountId: string } | null = null,
  asOf = '2026-10-01',
): Record<string, unknown> {
  const partyId = party?.partyId ?? 'c5000000-0000-4000-8000-000000000005'
  const companyAccountId = party?.companyAccountId ?? 'c6000000-0000-4000-8000-000000000006'
  return {
    companyId: INSTRUMENT_COMPANY,
    asOf,
    partyId: party?.partyId ?? null,
    companyAccountId: party?.companyAccountId ?? null,
    customers: [{ partyId, companyAccountId, currencyId: INSTRUMENT_CURRENCY, figures }],
    totals: [{ currencyId: INSTRUMENT_CURRENCY, figures }],
  }
}
