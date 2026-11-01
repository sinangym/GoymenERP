/**
 * MOD-01 / API-06 / UI-15 on S-04: the "Müşteri riski" card shows the server's result, limit, gross risk ("kullanılan") and
 * gross risk with this order verbatim, names the remaining limit as not served (never derived), shows each part with its
 * state and reason, and hides every amount from a caller without the price read (SAT-D150) while the result and reasons stay.
 * Loading, empty, 403, 503 (with its support code), 404 and malformed or foreign answers show no result and no amount.
 * Component evidence, NOT_ENV: the API answers here are the harness's.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import detailResponseRaw from './examples/detail-response.json?raw'
import riskResponseRaw from './examples/risk-check-response.json?raw'
import { SALES_CAPABILITY } from './capabilities'
import { decodeDraftDetail, type DraftDetail } from './contract'
import { decodeRiskCheck } from './riskCheck'
import { RISK_CHECK_TEXT as T, riskReasonText } from './riskText'
import { OrderRiskCard } from './SalesOrderRiskCard'
import { ALL_SALES, COMPANY_A, installFetch, json, renderSalesApp } from './testHarness'

const DRAFT = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const PARTY = '11111111-1111-1111-1111-111111111111'
const ACCOUNT = '55555555-5555-5555-5555-555555555555'
const TL = '9e60dcf0-f4bd-5bca-b464-f9a2db98e31a'
const PATH = `/satis/siparisler/${DRAFT}`

type Body = Record<string, any>

/** The pinned detail at version 4 in company A, its line's price disclosed or withheld. */
function detail(disclosure: 'Visible' | 'Withheld' = 'Visible'): Body {
  const body = JSON.parse(detailResponseRaw) as Body
  body.draft = { ...body.draft, companyId: COMPANY_A, version: '4' }
  body.lines = body.lines.map((line: Body) => ({ ...line, priceDisclosure: disclosure }))
  return body
}

/** The pinned example (today's fail-closed answer) bound to that detail. */
function risk(overrides: Body = {}): Body {
  return { ...(JSON.parse(riskResponseRaw) as Body), draftVersion: '4', ...overrides }
}

/** A decided answer: limit 10 000 TL, gross risk 7 500, with this order 12 000 → over the limit. */
function over(): Body {
  const body = risk({ result: 'Over', reasons: [] })
  const available = (kind: string, source: string, amount: string | null) => ({
    kind, source, state: 'Available', taxBasis: kind === 'CreditLimit' ? null : 'IncludingTax',
    amounts: amount === null ? [] : [{ currencyId: TL, amount }], reasonCode: null,
  })
  body.components = [
    available('OpenReceivables', 'finance.open-items', '7500.00'),
    available('ShippedNotInvoiced', 'sales.invoicing', null),
    available('ApprovedUnshippedRemainder', 'sales.approved-order-remainders', null),
  ]
  body.thisOrder = available('ThisOrder', 'sales.this-order', '4500.00')
  body.creditLimit = available('CreditLimit', 'finance.party-credit-limit', '10000.00')
  body.inLimitCurrency = { currencyId: TL, grossRisk: '7500.00', grossRiskWithThisOrder: '12000.00' }
  body.inLocalCurrency = { currencyId: TL, grossRisk: '7500.00', grossRiskWithThisOrder: '12000.00' }
  return body
}

/** The same answer as the server sends it without the price read: every amount null. */
function withheld(body: Body): Body {
  const strip = (figure: Body) => ({ ...figure, amounts: null })
  return {
    ...body,
    amountDisclosure: 'Withheld',
    components: body.components.map(strip),
    thisOrder: strip(body.thisOrder),
    creditLimit: strip(body.creditLimit),
    collateral: strip(body.collateral),
    inLimitCurrency: body.inLimitCurrency === null ? null : { ...body.inLimitCurrency, grossRisk: null, grossRiskWithThisOrder: null },
    inLocalCurrency: body.inLocalCurrency === null ? null : { ...body.inLocalCurrency, grossRisk: null, grossRiskWithThisOrder: null },
  }
}

const decodedDetail = (body: Body = detail()) => decodeDraftDetail(body, DRAFT) as DraftDetail
const named = (id: string) => (id === TL ? 'TRY' : id)

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const riskCalls = <C extends { readonly url: string }>(calls: readonly C[]) => calls.filter((call) => call.url.endsWith('/risk-check'))
const card = () => screen.findByRole('region', { name: T.heading })
async function loaded() {
  const section = await card()
  await within(section).findByTestId('sales-risk-result')
  return section
}

describe('S-04 customer risk card (API-06 / UI-15)', () => {
  it('shows today’s fail-closed answer with every reason, through one company-bound read-only GET', async () => {
    const harness = installFetch({ onDetail: () => json(detail()), onRiskCheck: () => json(risk()) })
    renderSalesApp(PATH)

    const section = await loaded()
    expect(within(section).getByTestId('sales-risk-result').getAttribute('data-result')).toBe('Unavailable')
    expect(within(section).getByTestId('sales-risk-result').textContent).toContain(T.result.Unavailable)
    expect(within(section).getByTestId('sales-risk-result-note').textContent).toBe(T.resultNote.Unavailable)
    // No limit, so no limit currency and no limit-currency total — the words, never zero.
    expect(within(section).getByTestId('sales-risk-credit-limit').textContent).toBe(T.notAvailable)
    expect(within(section).getByTestId('sales-risk-used').textContent).toBe(T.notAvailable)
    expect(within(section).getByTestId('sales-risk-limit-currency').textContent).toBe(T.limitCurrencyUnknown)
    // The remaining limit is not in the read and is never derived on the screen.
    expect(within(section).getByTestId('sales-risk-available').textContent).toBe(T.availableNotServed)
    // Each part: an answered amount verbatim, a recorded zero in words, a missing source with its reason.
    expect(within(section).getByTestId('sales-risk-figure-OpenReceivables-amount').textContent).toBe(`1250.00 ${TL}`)
    expect(within(section).getByTestId('sales-risk-figure-ApprovedUnshippedRemainder-amount').textContent).toBe(T.recordedZero)
    expect(within(section).getByTestId('sales-risk-figure-ShippedNotInvoiced-amount').textContent).toBe(T.notAvailable)
    expect(within(section).getByTestId('sales-risk-figure-ShippedNotInvoiced-reason').textContent)
      .toBe(riskReasonText('invoicing_owner_missing'))
    const reasons = within(within(section).getByTestId('sales-risk-reasons')).getAllByRole('listitem')
    expect(reasons.map((item) => item.getAttribute('data-code')))
      .toEqual(['invoicing_owner_missing', 'vat_inclusive_amount_unavailable', 'finance_credit_limit_owner_missing'])
    expect(within(section).queryByTestId('sales-risk-withheld')).toBeNull()

    const reads = riskCalls(harness.calls)
    expect(reads).toHaveLength(1)
    expect(reads[0]).toMatchObject({ method: 'GET', companyId: COMPANY_A, idempotencyKey: null, body: null })
    expect(reads[0].url).toContain(`/sales/drafts/${DRAFT}/risk-check`)
    expect(section.getAttribute('aria-busy')).toBe('false')
  })

  it('warns when the order takes the customer over the limit, with the limit, used and used-with-order amounts verbatim', () => {
    const decoded = decodeRiskCheck(over(), decodedDetail())
    expect(decoded).not.toBeNull()
    render(<OrderRiskCard outcome={{ type: 'ok-single', data: decoded! }} currencyName={named} onRetry={() => undefined} />)
    const result = screen.getByTestId('sales-risk-result')
    expect(result.getAttribute('data-result')).toBe('Over')
    expect(result.getAttribute('role')).toBe('alert')
    expect(screen.getByTestId('sales-risk-credit-limit').textContent).toBe('10000.00 TRY')
    expect(screen.getByTestId('sales-risk-used').textContent).toBe('7500.00 TRY')
    expect(screen.getByTestId('sales-risk-used-with-order').textContent).toBe('12000.00 TRY')
    expect(screen.getByTestId('sales-risk-limit-currency').textContent).toBe('TRY')
    expect(screen.getByTestId('sales-risk-available').textContent).toBe(T.availableNotServed)
    expect(screen.queryByTestId('sales-risk-reasons')).toBeNull()
  })

  it('SAT-D150: a caller without the price read sees the result and reasons but no amount at all', async () => {
    installFetch({ onDetail: () => json(detail('Withheld')), onRiskCheck: () => json(withheld(over())) })
    renderSalesApp(PATH)

    const section = await loaded()
    expect(within(section).getByTestId('sales-risk-result').getAttribute('data-result')).toBe('Over')
    expect(within(section).getByTestId('sales-risk-withheld').textContent).toBe(T.withheld)
    for (const id of ['sales-risk-credit-limit', 'sales-risk-used', 'sales-risk-used-with-order', 'sales-risk-local-used']) {
      expect(within(section).getByTestId(id).textContent).toBe(T.hidden)
    }
    expect(within(section).getByTestId('sales-risk-figure-OpenReceivables-amount').textContent).toBe(T.hidden)
    expect(section.textContent).not.toMatch(/[0-9]{3,}\.[0-9]{2}/)
  })

  it('hides amounts when the detail withholds prices even if an answer claims to disclose them', () => {
    const decoded = decodeRiskCheck(over(), decodedDetail())!
    render(<OrderRiskCard outcome={{ type: 'ok-single', data: decoded }} currencyName={named} pricesWithheld onRetry={() => undefined} />)
    expect(screen.getByTestId('sales-risk-used').textContent).toBe(T.hidden)
    expect(screen.getByTestId('sales-risk-withheld')).toBeTruthy()
  })

  it('refuses a withheld answer that still carries an amount, a decided answer with reasons, and a foreign answer', () => {
    const leak = withheld(over())
    leak.components[0] = { ...leak.components[0], amounts: [{ currencyId: TL, amount: '7500.00' }] }
    expect(decodeRiskCheck(leak, decodedDetail())).toBeNull()
    const leakTotal = withheld(over())
    leakTotal.inLimitCurrency.grossRisk = '7500.00'
    expect(decodeRiskCheck(leakTotal, decodedDetail())).toBeNull()
    expect(decodeRiskCheck(over(), decodedDetail())).not.toBeNull()
    expect(decodeRiskCheck({ ...over(), reasons: [{ component: 'Check', code: 'tax_basis_mismatch' }] }, decodedDetail())).toBeNull()
    expect(decodeRiskCheck({ ...risk(), reasons: [] }, decodedDetail())).toBeNull()
    expect(decodeRiskCheck({ ...risk(), draftVersion: '3' }, decodedDetail())).toBeNull()
    expect(decodeRiskCheck({ ...risk(), partyId: ACCOUNT }, decodedDetail())).toBeNull()
    expect(decodeRiskCheck({ ...risk(), companyAccountId: PARTY }, decodedDetail())).toBeNull()
    expect(decodeRiskCheck({ ...risk(), extra: true }, decodedDetail())).toBeNull()
    expect(decodeRiskCheck({ ...risk(), result: 'Maybe' }, decodedDetail())).toBeNull()
    expect(decodeRiskCheck({ ...risk(), components: risk().components.slice(1) }, decodedDetail())).toBeNull()
    const negative = over()
    negative.creditLimit.amounts = [{ currencyId: TL, amount: '-1' }]
    expect(decodeRiskCheck(negative, decodedDetail())).toBeNull()
    const noSourceWithAmount = risk()
    noSourceWithAmount.creditLimit = { ...noSourceWithAmount.creditLimit, amounts: [] }
    expect(decodeRiskCheck(noSourceWithAmount, decodedDetail())).toBeNull()
  })

  it('shows a malformed answer as unexpected, never as a result', async () => {
    installFetch({ onDetail: () => json(detail()), onRiskCheck: () => json({ ...risk(), draftVersion: '3' }) })
    renderSalesApp(PATH)
    const section = await card()
    const alert = await within(section).findByRole('alert')
    expect(alert.getAttribute('data-state')).toBe('malformed-response')
    expect(within(section).queryByTestId('sales-risk-result')).toBeNull()
    expect(within(section).getByTestId('sales-risk-unreadable').textContent).toBe(T.unreadable)
  })

  it('shows a polite loading status while the check is read, then the answer', async () => {
    let release: (response: Response) => void = () => undefined
    installFetch({ onDetail: () => json(detail()), onRiskCheck: () => new Promise<Response>((resolve) => { release = resolve }) })
    renderSalesApp(PATH)

    const section = await card()
    const loading = await within(section).findByRole('status')
    expect(loading.getAttribute('data-state')).toBe('initial-loading')
    expect(section.getAttribute('aria-busy')).toBe('true')
    expect(within(section).queryByTestId('sales-risk-result')).toBeNull()
    release(json(risk()))
    await within(section).findByTestId('sales-risk-result')
  })

  it('says so when no part of the risk has answered yet (the empty state)', () => {
    const body = risk()
    body.components = body.components.map((figure: Body) => figure.state === 'Available'
      ? { ...figure, state: 'Unavailable', taxBasis: null, amounts: null, reasonCode: 'party_risk_provider_unavailable' }
      : figure)
    body.reasons = [...body.reasons, { component: 'OpenReceivables', code: 'party_risk_provider_unavailable' }]
    const decoded = decodeRiskCheck(body, decodedDetail())!
    expect(decoded).not.toBeNull()
    render(<OrderRiskCard outcome={{ type: 'ok-single', data: decoded }} currencyName={named} onRetry={() => undefined} />)
    expect(screen.getByTestId('sales-risk-empty').textContent).toBe(T.noSourceYet)
    expect(screen.getByTestId('sales-risk-local-used').textContent).toBe(T.notAvailable)
  })

  it('on a 503 shows no result, names the outage with its support code, and retries the same read-only URL', async () => {
    let failing = true
    const harness = installFetch({
      onDetail: () => json(detail()),
      onRiskCheck: () => (failing
        ? json({ status: 503, title: 'Service Unavailable', code: 'service_unavailable', correlationId: 'corr-503' }, 503)
        : json(risk())),
    })
    renderSalesApp(PATH)

    const section = await card()
    const alert = await within(section).findByRole('alert')
    expect(alert.getAttribute('data-state')).toBe('service-unavailable')
    expect(within(section).getByTestId('sales-risk-unreadable').textContent).toBe(T.serviceUnavailable)
    expect(within(section).getByTestId('sales-risk-support-code').textContent).toBe(T.supportCode('corr-503'))
    expect(within(section).queryByTestId('sales-risk-result')).toBeNull()

    failing = false
    within(alert).getByRole('button').click()
    await within(section).findByTestId('sales-risk-result')
    const reads = riskCalls(harness.calls)
    expect(reads).toHaveLength(2)
    expect(new Set(reads.map((call) => `${call.method} ${call.url}`)).size).toBe(1)
  })

  it('shows the forbidden state on its own, with no result and no amount', () => {
    render(<OrderRiskCard outcome={{ type: 'http', status: 403 }} currencyName={named} onRetry={() => undefined} />)
    expect(screen.getByTestId('sales-risk-forbidden').textContent).toBe(T.forbidden)
    expect(screen.queryByTestId('sales-risk-result')).toBeNull()
  })

  it.each([401, 403])('routes a %i read through the real auth boundary: the protected detail closes and no result is left', async (code) => {
    installFetch({ onDetail: () => json(detail()), onRiskCheck: () => json({ status: code }, code) })
    renderSalesApp(PATH)
    expect(await screen.findByText(code === 401 ? 'Oturum sonlandı — oturum' : 'Bulunamadı — sayfa')).toBeTruthy()
    expect(screen.queryByTestId('sales-risk')).toBeNull()
  })

  it('fails closed on 404: no result, no retry', async () => {
    installFetch({ onDetail: () => json(detail()), onRiskCheck: () => json({ status: 404 }, 404) })
    renderSalesApp(PATH)
    const section = await card()
    const alert = await within(section).findByRole('alert')
    expect(alert.getAttribute('data-state')).toBe('not-found')
    expect(within(alert).queryByRole('button')).toBeNull()
  })

  it('is not read without the S-04 detail capability (API-06 publishes none of its own)', async () => {
    const harness = installFetch({
      capabilities: ALL_SALES.filter((capability) => capability !== SALES_CAPABILITY.draftsDetail),
      onDetail: () => json(detail()),
    })
    renderSalesApp(PATH)
    await waitFor(() => expect(harness.calls.some((call) => call.url.includes('/shell/capabilities'))).toBe(true))
    expect(screen.queryByTestId('sales-risk')).toBeNull()
    expect(riskCalls(harness.calls)).toHaveLength(0)
  })

  it('reads the harness default (today’s fail-closed answer) for a detail without an explicit risk answer', async () => {
    installFetch({ onDetail: () => json(detail()) })
    renderSalesApp(PATH)
    const section = await loaded()
    expect(within(section).getByTestId('sales-risk-result').getAttribute('data-result')).toBe('Unavailable')
  })
})
