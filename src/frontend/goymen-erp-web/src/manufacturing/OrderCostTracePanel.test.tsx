/**
 * URT-S19 cost trace panel — component/DOM tests through the real shell, gate and fence, over synthetic fixtures. NOT
 * real-API, real-SQL or browser acceptance.
 *
 * The panel is a control of its own (`manufacturing.orders.cost-trace`): without it nothing is requested. With it, the
 * figures are the server's text; while the cost cannot be computed the panel says why and never shows a 0.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import { MANUFACTURING_CAPABILITY } from './capabilities'
import { availableCost, costTrace } from './costTraceFixture'
import {
  COMPANY_A,
  ORDER,
  READER_CAPABILITIES,
  ROLL_1,
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
const HEADING = 'Maliyet izi'
const TRACE_CAPABILITIES = [MANUFACTURING_CAPABILITY.orderDetail, MANUFACTURING_CAPABILITY.orderCostTrace]

function stub(options: {
  readonly capabilities?: readonly string[]
  readonly trace?: () => Response
}): RecordedRequest[] {
  return installFetch({
    capabilities: options.capabilities ?? TRACE_CAPABILITIES,
    respond: (url) => {
      if (!url.includes(`/manufacturing/orders/${ORDER}`)) return undefined
      if (url.endsWith('/mass-balance')) return json(massBalance())
      if (url.endsWith('/cost-trace')) return (options.trace ?? (() => json(costTrace())))()
      return json(order())
    },
  })
}

const traceReads = (recorded: readonly RecordedRequest[]) =>
  requestsFor(recorded, `/manufacturing/orders/${ORDER}/cost-trace`, 'GET')

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('order cost trace', () => {
  it('shows the raw data as the server sent it and an unavailable cost with its reason, never a zero', async () => {
    const recorded = stub({})
    renderAt(PATH)

    const panel = await screen.findByRole('region', { name: HEADING })
    const lotRow = await within(panel).findByRole('row', { name: new RegExp(ROLL_1) })
    expect(within(lotRow).getByTestId('cost-lot-consumed').textContent).toBe('600.5')
    expect(within(panel).getByTestId('cost-trace-method').textContent).toContain(
      'Ağırlıklı ortalama (hammadde kg maliyeti esas)',
    )
    expect(within(panel).getByRole('row', { name: /İyi ürün/ }).textContent).toContain('590')
    const cost = within(panel).getByTestId('cost-trace-cost')
    expect(cost.getAttribute('data-cost-status')).toBe('unavailable')
    expect(within(cost).getByRole('status').textContent).toBe('Maliyet hesaplanamıyor')
    expect(within(cost).getByTestId('cost-reason').textContent).toContain('Girdi maliyeti kaynağı bağlı değil')
    expect(within(cost).queryByTestId('cost-per-kg')).toBeNull()
    expect(within(panel).getByTestId('cost-trace-timeline').textContent).toContain('Zaman kaydı tutulmuyor')

    const [read] = traceReads(recorded)
    expect(read.headers['X-Company-Id']).toBe(COMPANY_A)
  })

  it('shows the calculator figures only when the server says the cost is available', async () => {
    stub({ trace: () => json(costTrace({ cost: availableCost() })) })
    renderAt(PATH)

    const panel = await screen.findByRole('region', { name: HEADING })
    const cost = await within(panel).findByTestId('cost-trace-cost')
    expect(cost.getAttribute('data-cost-status')).toBe('available')
    expect(within(cost).getByTestId('cost-per-kg').textContent).toBe('31.4159')
    expect(within(cost).getByTestId('raw-material-cost-per-kg').textContent).toBe('30.5')
    expect(within(cost).getByTestId('cost-notes').textContent).toContain('Boya maliyeti dahil değil')
    expect(within(cost).queryByTestId('cost-reason')).toBeNull()
  })

  it('requests nothing and shows no panel without the cost trace control', async () => {
    const recorded = stub({ capabilities: READER_CAPABILITIES })
    renderAt(PATH)

    await screen.findByRole('region', { name: 'Kütle dengesi (GDC-BAL/1)' })
    expect(screen.queryByRole('region', { name: HEADING })).toBeNull()
    expect(traceReads(recorded)).toHaveLength(0)
  })

  it('shows no trace on a 403 and keeps the order on screen', async () => {
    const recorded = stub({ trace: () => problem(403) })
    renderAt(PATH)

    await screen.findByRole('region', { name: 'Kütle dengesi (GDC-BAL/1)' })
    await waitFor(() => expect(traceReads(recorded).length).toBeGreaterThan(0))
    await waitFor(() => expect(screen.queryByTestId('cost-trace-cost')).toBeNull())
    expect(screen.queryByTestId('cost-trace-lots')).toBeNull()
  })

  it.each([
    ['not-found', () => problem(404)],
    ['service-unavailable', () => problem(503)],
    ['malformed-response', () => json({ ...costTrace(), cost: { ...costTrace().cost, costPerKgTl: '0' } })],
  ])('shows the %s state of its own read without hiding the order', async (kind, trace) => {
    stub({ trace })
    renderAt(PATH)

    const panel = await screen.findByRole('region', { name: HEADING })
    await waitFor(() => expect(panel.querySelector(`[data-state="${kind}"]`)).not.toBeNull())
    expect(within(panel).queryByTestId('cost-trace-cost')).toBeNull()
    expect(screen.getByRole('region', { name: 'Kütle dengesi (GDC-BAL/1)' })).toBeTruthy()
  })
})
