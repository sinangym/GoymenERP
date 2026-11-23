/**
 * FNS-D30 / SAT-D310 / spec §S3 on S-04: the hold notice in the risk exception section names why the shipment is held from the
 * risk read (over the limit, "limit tanımlı değil", the risk could not be read) without an amount (SAT-D162), keeps loading,
 * forbidden, 503, stale and malformed reads category-free, and offers the limit shortcut only on a missing limit and only with a
 * credit-limit control: approve → one step, propose → a proposal only, none → hidden. Every identity is synthetic.
 */
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import type { RiskCheck, RiskFigure } from './riskCheck'
import { RiskExceptionHoldNotice, type CreditLimitShortcut } from './RiskExceptionHoldNotice'
import { RISK_EXCEPTION_TEXT } from './riskText'
import type { ViewOutcome } from '../shell/uiState'

const T = RISK_EXCEPTION_TEXT.hold
const S = RISK_EXCEPTION_TEXT.shortcut
const PARTY = '11111111-1111-4111-8111-111111111111'
const CARI = `/cari/${PARTY}`
const SYNTHETIC_AMOUNT = '98765.43'

const figure = (kind: RiskFigure['kind'], state: RiskFigure['state']): RiskFigure => ({
  kind, source: 'synthetic', state, taxBasis: state === 'Available' ? 'IncludingTax' : null,
  amounts: state === 'Available' ? [{ currencyId: 'synthetic-currency', amount: SYNTHETIC_AMOUNT }] : null, reasonCode: null,
})
const check = (result: RiskCheck['result'], limitState: RiskFigure['state'], reasons: RiskCheck['reasons'] = []): RiskCheck => ({
  draftVersion: '4', partyId: PARTY, companyAccountId: '22222222-2222-4222-8222-222222222222', result, reasons,
  amountDisclosure: 'Visible',
  inLocalCurrency: { currencyId: 'synthetic-currency', grossRisk: SYNTHETIC_AMOUNT, grossRiskWithThisOrder: SYNTHETIC_AMOUNT },
  inLimitCurrency: null,
  components: [figure('OpenReceivables', 'Available')],
  thisOrder: figure('ThisOrder', 'Available'),
  creditLimit: figure('CreditLimit', limitState),
  collateral: figure('Collateral', 'NoSource'),
})
const ok = (data: RiskCheck): ViewOutcome<RiskCheck> => ({ type: 'ok-single', data })

function show(outcome: ViewOutcome<RiskCheck> | undefined, shortcut: CreditLimitShortcut = 'none', onRetry = () => undefined) {
  return render(
    <MemoryRouter>
      <RiskExceptionHoldNotice outcome={outcome} shortcut={shortcut} cariHref={CARI} onRetry={onRetry} />
    </MemoryRouter>,
  )
}

afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
})

describe('S-04 risk hold notice (FNS-D30)', () => {
  it('names each hold category in its own words and never an amount', () => {
    const cases: readonly [RiskCheck, keyof typeof T.category][] = [
      [check('Over', 'Available'), 'OverLimit'],
      [check('Unavailable', 'NoSource'), 'CreditLimitNotSet'],
      [check('Unavailable', 'Unavailable', [{ component: 'ShippedNotInvoiced', code: 'shipment_provider_unavailable' }]), 'SourceUnavailable'],
    ]
    for (const [data, category] of cases) {
      const { container, unmount } = show(ok(data), 'approve')
      const text = screen.getByTestId('sales-risk-hold-category')
      expect(text.getAttribute('data-category')).toBe(category)
      expect(text.textContent).toBe(T.category[category])
      // SAT-D162: the hold names no amount even when the read carries one.
      expect(container.textContent).not.toContain(SYNTHETIC_AMOUNT)
      unmount()
    }
  })

  it('says a missing limit is neither unlimited nor zero', () => {
    show(ok(check('Unavailable', 'NoSource')))
    expect(screen.getByTestId('sales-risk-hold-category').textContent).toContain('limitsiz ya da sıfır limit sayılmaz')
  })

  it('needs no exception within the limit and offers no shortcut', () => {
    show(ok(check('Within', 'Available')), 'approve')
    expect(screen.getByTestId('sales-risk-hold-within').textContent).toBe(T.within)
    expect(screen.queryByTestId('sales-risk-hold-category')).toBeNull()
    expect(screen.queryByTestId('sales-risk-hold-shortcut')).toBeNull()
  })

  it('offers the approver holding limit approval a one-step shortcut to the Cari card', () => {
    show(ok(check('Unavailable', 'NoSource')), 'approve')
    const link = screen.getByRole('link', { name: S.defineLimit })
    expect(link.getAttribute('href')).toBe(CARI)
    const shortcut = screen.getByTestId('sales-risk-hold-shortcut')
    expect(shortcut.getAttribute('data-shortcut')).toBe('approve')
    expect(shortcut.textContent).toContain(S.defineLimitHint)
    expect(shortcut.textContent).toContain(S.optional)
    expect(screen.queryByRole('link', { name: S.proposeLimit })).toBeNull()
  })

  it('offers the approver holding only limit propose a proposal shortcut', () => {
    show(ok(check('Unavailable', 'NoSource')), 'propose')
    expect(screen.getByRole('link', { name: S.proposeLimit }).getAttribute('href')).toBe(CARI)
    expect(screen.getByTestId('sales-risk-hold-shortcut').textContent).toContain(S.proposeLimitHint)
    expect(screen.queryByRole('link', { name: S.defineLimit })).toBeNull()
  })

  it('hides the shortcut without a credit-limit control, and on any other category', () => {
    show(ok(check('Unavailable', 'NoSource')), 'none')
    expect(screen.queryByTestId('sales-risk-hold-shortcut')).toBeNull()
    cleanup()
    show(ok(check('Over', 'Available')), 'approve')
    expect(screen.queryByTestId('sales-risk-hold-shortcut')).toBeNull()
    cleanup()
    show(ok(check('Unavailable', 'Unavailable')), 'approve')
    expect(screen.getByTestId('sales-risk-hold-category').getAttribute('data-category')).toBe('SourceUnavailable')
    expect(screen.queryByTestId('sales-risk-hold-shortcut')).toBeNull()
  })

  it('shows loading while the risk is read, with no category', () => {
    show(undefined, 'approve')
    expect(screen.getByTestId('sales-risk-hold-loading').textContent).toBe(T.loading)
    expect(screen.getByTestId('sales-risk-hold').getAttribute('aria-busy')).toBe('true')
    expect(screen.queryByTestId('sales-risk-hold-category')).toBeNull()
  })

  it('keeps forbidden, 503, stale, offline and malformed reads category-free and the request open', () => {
    const cases: readonly [ViewOutcome<RiskCheck>, string][] = [
      [{ type: 'http', status: 403 }, T.forbidden],
      [{ type: 'http', status: 503 }, T.serviceUnavailable],
      [{ type: 'fence-stale' }, T.stale],
      [{ type: 'offline' }, T.unreadable],
      [{ type: 'malformed' }, T.unreadable],
    ]
    for (const [outcome, text] of cases) {
      const { unmount } = show(outcome, 'approve')
      expect(screen.getByTestId('sales-risk-hold-unreadable').textContent).toBe(text)
      expect(screen.queryByTestId('sales-risk-hold-category')).toBeNull()
      expect(screen.queryByTestId('sales-risk-hold-shortcut')).toBeNull()
      unmount()
    }
  })

  it('re-reads the risk on retry, and offers no retry on a forbidden read', () => {
    const onRetry = vi.fn()
    show({ type: 'http', status: 503 }, 'none', onRetry)
    fireEvent.click(screen.getByTestId('sales-risk-hold-retry'))
    expect(onRetry).toHaveBeenCalledTimes(1)
    cleanup()
    show({ type: 'http', status: 403 }, 'none', onRetry)
    expect(screen.queryByTestId('sales-risk-hold-retry')).toBeNull()
  })
})
