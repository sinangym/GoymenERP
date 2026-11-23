/**
 * URT-S22 order lineage panel — component/DOM tests through the real shell, gate and fence, over synthetic fixtures.
 * NOT real-API, real-SQL or browser acceptance.
 *
 * The panel lives under the order read the screen already needs (KLN-D44: no control of its own) and reads nothing
 * until it is opened. Once open: "Geri iz" shows every output with ALL its inputs, their kg and kg shares; "İleri iz"
 * every input with the outputs it went into; the shares are the server's text, unrounded; not-calculated shares show
 * the reason, never a 0.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import {
  DOWNSTREAM_ORDER,
  INPUT_LABEL_A,
  INPUT_LABEL_B,
  OUTPUT_1,
  OUTPUT_2,
  UPSTREAM_ORDER,
  inputShare,
  lineageOutput,
  orderLineage,
} from './lineageFixture'
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
const HEADING = 'Soy ağacı'
const SHOW = 'Soy ağacını göster'

function stub(options: { readonly lineage?: () => Response } = {}): RecordedRequest[] {
  return installFetch({
    capabilities: READER_CAPABILITIES,
    respond: (url) => {
      if (!url.includes(`/manufacturing/orders/${ORDER}`)) return undefined
      if (url.endsWith('/mass-balance')) return json(massBalance())
      if (url.endsWith('/lineage')) return (options.lineage ?? (() => json(orderLineage())))()
      return json(order())
    },
  })
}

const lineageReads = (recorded: readonly RecordedRequest[]) =>
  requestsFor(recorded, `/manufacturing/orders/${ORDER}/lineage`, 'GET')

async function openPanel() {
  const panel = await screen.findByRole('region', { name: HEADING })
  await userEvent.click(within(panel).getByRole('button', { name: SHOW }))
  return panel
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('order lineage', () => {
  it('reads nothing until it is opened, then reads it under the active company', async () => {
    const recorded = stub()
    renderAt(PATH)

    const panel = await screen.findByRole('region', { name: HEADING })
    await screen.findByRole('region', { name: 'Kütle dengesi (GDC-BAL/1)' })
    const toggle = within(panel).getByRole('button', { name: SHOW })
    expect(toggle.getAttribute('aria-expanded')).toBe('false')
    expect(lineageReads(recorded)).toHaveLength(0)

    await userEvent.click(toggle)

    await within(panel).findByTestId('order-lineage-body')
    expect(within(panel).getByRole('button', { name: 'Soy ağacını gizle' }).getAttribute('aria-expanded')).toBe('true')
    const [read] = lineageReads(recorded)
    expect(read.headers['X-Company-Id']).toBe(COMPANY_A)
  })

  it('shows the backward trace: every output with all its inputs, kg and shares as the server sent them', async () => {
    stub()
    renderAt(PATH)

    const panel = await openPanel()
    const backward = await within(panel).findByRole('tabpanel', { name: 'Geri iz' })
    const first = backward.querySelector(`[data-lineage-output="${OUTPUT_1}"]`) as HTMLElement
    expect(first).not.toBeNull()
    const lineA = first.querySelector(`[data-lineage-input="${INPUT_LABEL_A}"]`) as HTMLElement
    const lineB = first.querySelector(`[data-lineage-input="${INPUT_LABEL_B}"]`) as HTMLElement
    expect(within(lineA).getByTestId('lineage-consumed-kg').textContent).toBe('75')
    expect(within(lineA).getByTestId('lineage-share').textContent).toBe('0.75')
    expect(within(lineB).getByTestId('lineage-share').textContent).toBe('0.25')
    expect(within(first).getByTestId('lineage-total-kg').textContent).toBe('100')
    expect(within(first).getByTestId('lineage-share-status').textContent).toBe('Pay hesaplandı')
    expect(backward.querySelector(`[data-lineage-output="${OUTPUT_2}"]`)).not.toBeNull()
    expect(within(backward).getByTestId('order-lineage-upstream').textContent).toContain(UPSTREAM_ORDER)
    expect(within(panel).queryByTestId('order-lineage-truncated')).toBeNull()
  })

  it('shows the forward trace: each input with every output it went into, by keyboard too', async () => {
    stub()
    renderAt(PATH)

    const panel = await openPanel()
    const backwardTab = await within(panel).findByRole('tab', { name: 'Geri iz' })
    expect(backwardTab.getAttribute('aria-selected')).toBe('true')
    backwardTab.focus()
    await userEvent.keyboard('{ArrowRight}')

    const forwardTab = within(panel).getByRole('tab', { name: 'İleri iz' })
    expect(forwardTab.getAttribute('aria-selected')).toBe('true')
    expect(document.activeElement).toBe(forwardTab)
    const forward = within(panel).getByRole('tabpanel', { name: 'İleri iz' })
    const inputA = forward.querySelector(`tbody[data-lineage-input="${INPUT_LABEL_A}"]`) as HTMLElement
    expect(inputA).not.toBeNull()
    const intoTwo = inputA.querySelector(`[data-lineage-output="${OUTPUT_2}"]`) as HTMLElement
    expect(within(intoTwo).getByTestId('lineage-consumed-kg').textContent).toBe('40')
    expect(within(intoTwo).getByTestId('lineage-share').textContent).toBe('1')
    expect(inputA.querySelector(`[data-lineage-output="${OUTPUT_1}"]`)).not.toBeNull()
    expect(within(forward).getByTestId('order-lineage-downstream').textContent).toContain(DOWNSTREAM_ORDER)
    // A hidden panel has no accessible name; follow the named tab's actual controls association.
    const backwardPanel = document.getElementById(backwardTab.getAttribute('aria-controls')!)
    expect(backwardPanel?.getAttribute('role')).toBe('tabpanel')
    expect(backwardPanel?.hidden).toBe(true)
  })

  it('shows the reason and no share while the shares are not calculated, and says when the list was cut', async () => {
    const unreadable = lineageOutput({
      shareStatus: 'unreadable',
      shareCode: 'lineage_input_without_consumption',
      totalConsumedKg: null,
      inputs: [inputShare(INPUT_LABEL_A, '75', null)],
    })
    stub({ lineage: () => json(orderLineage({ outputs: [unreadable], upstream: [], downstream: [], truncated: true })) })
    renderAt(PATH)

    const panel = await openPanel()
    const backward = await within(panel).findByRole('tabpanel', { name: 'Geri iz' })
    expect(within(backward).getByTestId('lineage-share-status').textContent).toContain(
      'Bir girdi etiketinin tüketilen kg değeri yok',
    )
    expect(within(backward).getByTestId('lineage-share').textContent).toBe('—')
    expect(within(backward).getByTestId('lineage-total-kg').textContent).toBe('—')
    expect(within(panel).getByTestId('order-lineage-truncated').textContent).toContain('tamamı değil')
    expect(within(backward).getByTestId('order-lineage-upstream').textContent).toContain('başka bir emir kaydı yok')
  })

  it('shows the empty state for an order without lineage', async () => {
    stub({ lineage: () => json(orderLineage({ outputs: [], upstream: [], downstream: [] })) })
    renderAt(PATH)

    const panel = await openPanel()
    await waitFor(() => expect(panel.querySelector('[data-state="empty-no-data"]')).not.toBeNull())
    expect(within(panel).queryByRole('tablist')).toBeNull()
  })

  it.each([
    ['not-found', () => problem(404)],
    ['service-unavailable', () => problem(503)],
    ['malformed-response', () => json({ ...orderLineage(), calculatorVersion: 2 })],
  ])('shows the %s state of its own read without hiding the order', async (kind, lineage) => {
    stub({ lineage })
    renderAt(PATH)

    const panel = await openPanel()
    await waitFor(() => expect(panel.querySelector(`[data-state="${kind}"]`)).not.toBeNull())
    expect(within(panel).queryByRole('tablist')).toBeNull()
    expect(screen.getByRole('region', { name: 'Kütle dengesi (GDC-BAL/1)' })).toBeTruthy()
  })
})
