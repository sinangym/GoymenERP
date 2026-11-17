/**
 * URT-S17 order cost panel — component/DOM tests through the real shell, gate and fence, over synthetic fixtures. NOT
 * real-API, real-SQL or browser acceptance.
 *
 * The panel lives under the cost trace control (`manufacturing.orders.cost-trace`): without it nothing is requested.
 * With it, the figures are the server's recorded text; an open order's cost is pending (SAT-D317 (1)) and every answer
 * without figures says why, never a 0.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import { MANUFACTURING_CAPABILITY } from './capabilities'
import { costTrace } from './costTraceFixture'
import { COST_LABEL_1, COST_SCRAP_LABEL, availableOrderCost, orderCost } from './orderCostFixture'
import {
  COMPANY_A,
  ORDER,
  READER_CAPABILITIES,
  installFetch,
  json,
  massBalance,
  order,
  problem,
  renderAt,
  requestsFor,
  type RecordedRequest,
} from './testHarness'

const PATH = `/uretim/emir/${ORDER}`
const HEADING = 'Emir maliyeti'
const MASS_BALANCE_HEADING = 'Kütle dengesi (GDC-BAL/1)'
const COST_CAPABILITIES = [MANUFACTURING_CAPABILITY.orderDetail, MANUFACTURING_CAPABILITY.orderCostTrace]

function stub(options: {
  readonly capabilities?: readonly string[]
  readonly cost?: () => Response
}): RecordedRequest[] {
  return installFetch({
    capabilities: options.capabilities ?? COST_CAPABILITIES,
    respond: (url) => {
      if (!url.includes(`/manufacturing/orders/${ORDER}`)) return undefined
      if (url.endsWith('/mass-balance')) return json(massBalance())
      if (url.endsWith('/cost-trace')) return json(costTrace())
      if (url.endsWith('/production-cost')) return (options.cost ?? (() => json(orderCost())))()
      return json(order())
    },
  })
}

const costReads = (recorded: readonly RecordedRequest[]) =>
  requestsFor(recorded, `/manufacturing/orders/${ORDER}/production-cost`, 'GET')

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('order cost', () => {
  it('displays an exact weighted entry rate as its server fraction without a rounded conversion', async () => {
    const original = availableOrderCost()
    const line = { issueMovementId: 'b0000000-0000-4000-8000-000000000002', currencyId: 'c0000000-0000-4000-8000-000000000001',
      foreignUnitCostPerKg: '2', exchangeRate: null, exchangeRateRatio: { numerator: '100', denominator: '3' },
      tlUnitCostPerKg: '10', keptKilograms: '2', foreignAmount: '4' }
    stub({ cost: () => json({ ...original, currencyLines: [line] }) })
    renderAt(PATH)
    const panel = await screen.findByRole('region', { name: HEADING })
    expect(await within(panel).findByText('100/3')).toBeTruthy()
    expect(within(panel).queryByText('33.3333')).toBeNull()
  })

  it('says the cost cannot be computed while no cost source is bound, never a zero', async () => {
    const recorded = stub({})
    renderAt(PATH)

    const panel = await screen.findByRole('region', { name: HEADING })
    const body = await within(panel).findByTestId('order-cost-body')
    expect(body.getAttribute('data-cost-status')).toBe('unavailable')
    expect(within(body).getByRole('status').textContent).toBe('Maliyet hesaplanamıyor')
    expect(within(body).getByTestId('order-cost-reason').textContent).toContain('Girdi maliyeti kaynağı bağlı değil')
    expect(within(body).queryByTestId('order-cost-per-kg')).toBeNull()
    expect(within(body).queryByTestId('order-cost-lines')).toBeNull()

    const [read] = costReads(recorded)
    expect(read.headers['X-Company-Id']).toBe(COMPANY_A)
  })

  it('says an open order has no receipt cost yet (SAT-D317 (1))', async () => {
    stub({ cost: () => json(orderCost({ status: 'pending', reasonCode: 'order_not_closed' })) })
    renderAt(PATH)

    const panel = await screen.findByRole('region', { name: HEADING })
    const body = await within(panel).findByTestId('order-cost-body')
    expect(body.getAttribute('data-cost-status')).toBe('pending')
    expect(within(body).getByRole('status').textContent).toBe('Maliyet bekliyor — kesinleşmedi')
    expect(within(body).getByTestId('order-cost-reason').textContent).toContain('Emir kapanmadan maliyet kesinleşmez')
    expect(within(body).queryByTestId('order-cost-per-kg')).toBeNull()
  })

  it('names the issues an unreadable cost is waiting for', async () => {
    const issue = 'b0000000-0000-4000-8000-000000000002'
    stub({
      cost: () =>
        json(
          orderCost({
            status: 'unreadable',
            reasonCode: 'input_exchange_rate_missing',
            issueReasons: [{ issueMovementId: issue, reasonCode: 'input_exchange_rate_missing' }],
          }),
        ),
    })
    renderAt(PATH)

    const panel = await screen.findByRole('region', { name: HEADING })
    const reasons = await within(panel).findByTestId('order-cost-issue-reasons')
    const row = within(reasons).getByRole('row', { name: new RegExp(issue) })
    expect(row.textContent).toContain('kuru yok')
  })

  it('shows the recorded figures as the server sent them when the cost is available', async () => {
    stub({ cost: () => json(availableOrderCost()) })
    renderAt(PATH)

    const panel = await screen.findByRole('region', { name: HEADING })
    const body = await within(panel).findByTestId('order-cost-body')
    expect(body.getAttribute('data-cost-status')).toBe('available')
    expect(within(body).getByTestId('order-cost-per-kg').textContent).toBe('12.3457')
    expect(within(body).getByTestId('order-raw-material-cost-per-kg').textContent).toBe('12.1')
    expect(within(body).getByTestId('order-cost-input-total').textContent).toBe('1000')
    expect(within(body).getByTestId('order-cost-distributed-total').textContent).toBe('1000')
    expect(within(body).getByTestId('order-cost-remainder').textContent).toBe('-0.01')
    expect(within(body).getByTestId('order-cost-remainder-line').textContent).toBe(COST_LABEL_1)
    expect(within(body).queryByTestId('order-cost-reason')).toBeNull()

    const lines = within(body).getByTestId('order-cost-lines')
    const carrier = within(lines).getByRole('row', { name: new RegExp(COST_LABEL_1) })
    expect(within(carrier).getByTestId('order-cost-line-amount').textContent).toBe('740.74')
    expect(within(carrier).getByTestId('order-cost-line-remainder').textContent).toBe('-0.01')
    const scrap = within(lines).getByRole('row', { name: new RegExp(COST_SCRAP_LABEL) })
    expect(within(scrap).getByTestId('order-cost-line-amount').textContent).toContain(
      'Hurda etiketinin maliyeti için karar yok',
    )
    expect(within(body).getByTestId('order-cost-currency-lines').textContent).toContain('35.25')
    expect(within(body).getByTestId('order-cost-notes').textContent).toContain('Boya maliyeti dahil değil')
  })

  it('requests nothing and shows no panel without the cost trace control', async () => {
    const recorded = stub({ capabilities: READER_CAPABILITIES })
    renderAt(PATH)

    await screen.findByRole('region', { name: MASS_BALANCE_HEADING })
    expect(screen.queryByRole('region', { name: HEADING })).toBeNull()
    expect(costReads(recorded)).toHaveLength(0)
  })

  it('shows no cost on a 403 and keeps the order on screen', async () => {
    const recorded = stub({ cost: () => problem(403) })
    renderAt(PATH)

    await screen.findByRole('region', { name: MASS_BALANCE_HEADING })
    await waitFor(() => expect(costReads(recorded).length).toBeGreaterThan(0))
    await waitFor(() => expect(screen.queryByTestId('order-cost-body')).toBeNull())
  })

  it.each([
    ['not-found', () => problem(404)],
    ['service-unavailable', () => problem(503)],
    ['malformed-response', () => json(orderCost({ costPerKgTl: '0' }))],
  ])('shows the %s state of its own read without hiding the order', async (kind, cost) => {
    stub({ cost })
    renderAt(PATH)

    const panel = await screen.findByRole('region', { name: HEADING })
    await waitFor(() => expect(panel.querySelector(`[data-state="${kind}"]`)).not.toBeNull())
    expect(within(panel).queryByTestId('order-cost-body')).toBeNull()
    expect(screen.getByRole('region', { name: MASS_BALANCE_HEADING })).toBeTruthy()
  })
})
