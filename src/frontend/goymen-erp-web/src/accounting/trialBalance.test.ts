import { afterEach, describe, expect, it, vi } from 'vitest'
import { fetchTrialBalance } from './trialBalanceApi'
import { decodeTrialBalanceReport, type TrialBalanceQuery } from './trialBalanceContract'

const COMPANY = '10000000-0000-4000-8000-000000000001'
const CURRENCY = '20000000-0000-4000-8000-000000000001'
const ACCOUNT = '30000000-0000-4000-8000-000000000001'
const query: TrialBalanceQuery = {
  fromAccountingDate: '2026-09-01',
  throughAccountingDate: '2026-09-30',
  currencyId: CURRENCY,
}

const report = () => ({
  ...query,
  lines: [{ accountId: ACCOUNT, currencyId: CURRENCY, debit: '125', credit: '100', balanceSide: 'Debit', balance: '25' }],
  currencyTotals: [{ currencyId: CURRENCY, debit: '125', credit: '100', isBalanced: false }],
})

afterEach(() => vi.unstubAllGlobals())

describe('M-06 trial-balance public consumer', () => {
  it('decodes the exact date, currency, account and lossless money contract', () => {
    expect(decodeTrialBalanceReport(report(), query)).toEqual(report())
    expect(decodeTrialBalanceReport({ ...report(), throughAccountingDate: '2026-10-01' }, query)).toBeNull()
    expect(decodeTrialBalanceReport({ ...report(), lines: [{ ...report().lines[0], debit: 125 }] }, query)).toBeNull()
    expect(decodeTrialBalanceReport({ ...report(), lines: [...report().lines, ...report().lines] }, query)).toBeNull()
    expect(decodeTrialBalanceReport({ ...report(), currencyTotals: [] }, query)).toBeNull()
  })

  it('sends the exact inclusive query and company header, accepting only exact 200', async () => {
    const fetch = vi.fn(async () => new Response(JSON.stringify(report()), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }))
    vi.stubGlobal('fetch', fetch)

    const result = await fetchTrialBalance(COMPANY, query)

    expect(result.type).toBe('ok-single')
    const [url, init] = fetch.mock.calls[0]
    expect(String(url)).toContain('/accounting/trial-balance?fromAccountingDate=2026-09-01&throughAccountingDate=2026-09-30')
    expect(String(url)).toContain(`currencyId=${CURRENCY}`)
    expect(new Headers(init?.headers).get('X-Company-Id')).toBe(COMPANY)
  })

  it('fails closed for malformed success and preserves HTTP problems', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ ...report(), lines: [] }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })))
    expect((await fetchTrialBalance(COMPANY, query)).type).toBe('malformed')

    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ code: 'forbidden' }), {
      status: 403,
      headers: { 'Content-Type': 'application/json' },
    })))
    expect(await fetchTrialBalance(COMPANY, query)).toMatchObject({ type: 'http', status: 403 })
  })
})
