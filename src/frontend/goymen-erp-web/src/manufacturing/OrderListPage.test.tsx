/**
 * MFG-02 order list — component/DOM tests through the real shell, gate and fence, over synthetic
 * fixtures. NOT real-API, real-SQL or browser acceptance (the `mfg` browser suite covers the round
 * trip), and never reported as such.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import {
  LIST_CAPABILITIES,
  ORDER,
  installFetch,
  json,
  orderList,
  orderSummary,
  problem,
  renderAt,
  requestsFor,
  type RecordedRequest,
} from './testHarness'
import { MANUFACTURING_CAPABILITY } from './capabilities'

const PATH = '/uretim/emirler'
const SECOND = 'd0000000-0000-4000-8000-00000000000b'

function stub(
  options: { pages?: Array<() => Response>; capabilities?: readonly string[] } = {},
): { recorded: RecordedRequest[] } {
  const pages = options.pages ?? [() => json(orderList())]
  let index = 0
  const recorded = installFetch({
    capabilities: options.capabilities ?? LIST_CAPABILITIES,
    respond: (url) => {
      if (!url.includes('/manufacturing/orders')) return undefined
      const respond = pages[Math.min(index, pages.length - 1)]
      index += 1
      return respond()
    },
  })
  return { recorded }
}

const lists = (recorded: RecordedRequest[]) => requestsFor(recorded, '/manufacturing/orders', 'GET')

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('order list', () => {
  it('shows each order as the server summarised it, and no balance or label it does not carry', async () => {
    stub({
      pages: [
        () =>
          json(
            orderList([
              orderSummary({ target: { basis: 'GoodOutputMass', kg: '900' } }),
              orderSummary({
                orderId: SECOND,
                status: 'Closed',
                version: 6,
                plannedRollCount: 1,
                plannedInputKg: '1000.125',
                closedAtUtc: '2026-09-19T10:00:00.000Z',
              }),
            ]),
          ),
      ],
    })
    renderAt(PATH)

    const open = await screen.findByRole('row', { name: new RegExp(ORDER.slice(-8)) })
    expect(within(open).getByText('Açık')).toBeTruthy()
    expect(within(open).getByText('2000')).toBeTruthy()
    expect(within(open).getByText('İyi ürün kütlesi: 900 kg')).toBeTruthy()

    const closed = await screen.findByRole('row', { name: new RegExp(SECOND.slice(-8)) })
    expect(within(closed).getByText('Kapalı')).toBeTruthy()
    // The kilogram text is the server's, lossless and unrounded.
    expect(within(closed).getByText('1000.125')).toBeTruthy()
    expect(within(closed).getByText('Hedef yok')).toBeTruthy()

    // The list carries no balance, no difference and no label; the id is a labelled technical reference.
    expect(screen.queryByText(/Kütle farkı/)).toBeNull()
    expect(screen.queryByText(ORDER)).toBeNull()
    expect(screen.getByText(/etiketleri ürün\/etiket okumasıyla gelecek/)).toBeTruthy()
  })

  it('links one order for a holder of the order screen’s own capability', async () => {
    stub()
    renderAt(PATH)
    const link = await screen.findByRole('link', { name: new RegExp(ORDER.slice(-8)) })
    expect(link.getAttribute('href')).toBe(`/uretim/emir/${ORDER}`)
  })

  it('shows the row without a link when only the list is granted', async () => {
    stub({ capabilities: [MANUFACTURING_CAPABILITY.orderList] })
    renderAt(PATH)
    await screen.findByRole('row', { name: new RegExp(ORDER.slice(-8)) })
    expect(screen.queryByRole('link', { name: new RegExp(ORDER.slice(-8)) })).toBeNull()
  })

  it('shows exactly one in-list create action only for the plan-create capability', async () => {
    stub({ capabilities: [...LIST_CAPABILITIES, MANUFACTURING_CAPABILITY.planCreate] })
    renderAt(PATH)
    await screen.findByRole('row', { name: new RegExp(ORDER.slice(-8)) })
    const actions = screen.getAllByRole('link', { name: 'Yeni üretim planı' })
    expect(actions).toHaveLength(1)
    expect(actions[0].getAttribute('href')).toBe('/uretim/plan/yeni')
  })

  it('draws no create action when the plan-create capability is absent', async () => {
    stub()
    renderAt(PATH)
    await screen.findByRole('row', { name: new RegExp(ORDER.slice(-8)) })
    expect(screen.queryByRole('link', { name: 'Yeni üretim planı' })).toBeNull()
  })

  it('sends the server’s own cursor back unchanged and appends the next page once', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({
      pages: [
        () => json(orderList([orderSummary()], 'CURSOR-1')),
        () => json(orderList([orderSummary({ orderId: SECOND })], null)),
      ],
    })
    renderAt(PATH)
    await screen.findByRole('row', { name: new RegExp(ORDER.slice(-8)) })

    await user.click(screen.getByRole('button', { name: 'Sonraki sayfayı yükle' }))
    await screen.findByRole('row', { name: new RegExp(SECOND.slice(-8)) })
    expect(await screen.findByText('Liste sonu.')).toBeTruthy()

    const second = lists(recorded)[1]
    expect(second.url).toContain('cursor=CURSOR-1')
    // The first row is still there, once.
    expect(screen.getAllByRole('row', { name: new RegExp(ORDER.slice(-8)) })).toHaveLength(1)
  })

  it('keeps the rows already shown when the next page fails, and retries the SAME cursor', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({
      pages: [
        () => json(orderList([orderSummary()], 'CURSOR-1')),
        () => problem(503, { code: 'service_unavailable' }),
        () => json(orderList([orderSummary({ orderId: SECOND })], null)),
      ],
    })
    renderAt(PATH)
    await screen.findByRole('row', { name: new RegExp(ORDER.slice(-8)) })
    await user.click(screen.getByRole('button', { name: 'Sonraki sayfayı yükle' }))

    expect(await screen.findByText('Sonraki sayfa okunamadı; yukarıdaki satırlar yerinde kaldı.')).toBeTruthy()
    expect(screen.getByRole('row', { name: new RegExp(ORDER.slice(-8)) })).toBeTruthy()

    await user.click(screen.getByRole('button', { name: 'Tekrar dene' }))
    await screen.findByRole('row', { name: new RegExp(SECOND.slice(-8)) })
    const [, failed, retried] = lists(recorded)
    expect(retried.url).toBe(failed.url)
  })

  it('asks the server for the status it was given and starts the list again', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({
      pages: [() => json(orderList([orderSummary()], 'CURSOR-1')), () => json(orderList([], null))],
    })
    renderAt(PATH)
    await screen.findByRole('row', { name: new RegExp(ORDER.slice(-8)) })

    await user.selectOptions(screen.getByLabelText('Durum'), 'Closed')
    await waitFor(() => expect(lists(recorded).length).toBeGreaterThan(1))
    const filtered = lists(recorded)[1]
    expect(filtered.url).toContain('status=Closed')
    // A filter restarts the list: the previous page's cursor is not carried over.
    expect(filtered.url).not.toContain('cursor=')
  })

  it('says so when the company has no order', async () => {
    stub({ pages: [() => json(orderList([], null))] })
    renderAt(PATH)
    expect(await screen.findByText('Bu görünümde henüz kayıt yok.')).toBeTruthy()
  })

  it('shows no table at all when the answer does not match the contract', async () => {
    stub({ pages: [() => json({ orders: [{ orderId: 'not-a-guid' }], nextCursor: null })] })
    renderAt(PATH)
    // Fail closed: a body that does not match the contract is not half-shown. The block is matched by its
    // own state attribute, because its title text is rendered beside the subject in one paragraph.
    await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    expect(screen.queryByRole('table')).toBeNull()
  })
})
