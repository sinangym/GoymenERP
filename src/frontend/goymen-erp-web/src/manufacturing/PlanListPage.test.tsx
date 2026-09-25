/**
 * MFG-PLAN-LIST plan list — component/DOM tests through the real shell, gate and fence, over synthetic fixtures.
 * NOT real-API, real-SQL or browser acceptance (the `mfg` plan journey covers the round trip), and never reported as
 * such.
 */

import { act } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import {
  COMPANY_A,
  COMPANY_B,
  LIST_CAPABILITIES,
  PLAN,
  PLAN_LIST_CAPABILITIES,
  installFetch,
  json,
  orderList,
  planList,
  planSummary,
  problem,
  renderAt,
  type RecordedRequest,
} from './testHarness'
import { MANUFACTURING_CAPABILITY } from './capabilities'
import { decodePlanList } from './planListContract'

const PATH = '/uretim/planlar'
const SECOND = 'd0000000-0000-4000-8000-00000000000c'
const APPROVED_AT = '2026-09-18T10:00:00.000Z'

function stub(
  options: { pages?: Array<() => Response>; capabilities?: readonly string[] } = {},
): { recorded: RecordedRequest[] } {
  const pages = options.pages ?? [() => json(planList())]
  let index = 0
  const recorded = installFetch({
    capabilities: options.capabilities ?? PLAN_LIST_CAPABILITIES,
    respond: (url) => {
      if (url.includes('/manufacturing/orders')) return json(orderList([], null))
      if (!/\/manufacturing\/plans(\?|$)/.test(url)) return undefined
      const respond = pages[Math.min(index, pages.length - 1)]
      index += 1
      return respond()
    },
  })
  return { recorded }
}

const lists = (recorded: RecordedRequest[]) =>
  recorded.filter((request) => request.method === 'GET' && /\/manufacturing\/plans(\?|$)/.test(request.url))

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('plan list', () => {
  it('shows each plan as the server summarised it, and no engine answer, order or label it does not carry', async () => {
    stub({
      pages: [
        () =>
          json(
            planList([
              planSummary({ target: { basis: 'GoodOutputMass', kg: '900' } }),
              planSummary({
                planId: SECOND,
                status: 'Approved',
                version: 2,
                plannedRollCount: 1,
                plannedInputKg: '1000.125',
                approvedAtUtc: APPROVED_AT,
              }),
            ]),
          ),
      ],
    })
    renderAt(PATH)

    const draft = await screen.findByRole('row', { name: new RegExp(PLAN.slice(-8)) })
    expect(within(draft).getByText('Taslak')).toBeTruthy()
    expect(within(draft).getByText('1500.625')).toBeTruthy()
    expect(within(draft).getByText('İyi ürün kütlesi: 900 kg')).toBeTruthy()
    expect(within(draft).queryByText(/Onay:/)).toBeNull()

    const approved = await screen.findByRole('row', { name: new RegExp(SECOND.slice(-8)) })
    expect(within(approved).getByText('Onaylı')).toBeTruthy()
    // The kilogram text is the server's, lossless and unrounded.
    expect(within(approved).getByText('1000.125')).toBeTruthy()
    expect(within(approved).getByText('Hedef yok')).toBeTruthy()
    expect(within(approved).getByText(/Onay:/)).toBeTruthy()

    // No engine answer, no "converted"/order state, and the id is a labelled technical reference.
    expect(screen.queryByText(/Hesaplanamıyor/)).toBeNull()
    expect(screen.queryByText(/emre dönüştü/i)).toBeNull()
    expect(screen.queryByText(PLAN)).toBeNull()
    expect(screen.getByText(/etiketleri ürün\/etiket okumasıyla gelecek/)).toBeTruthy()
  })

  it('asks the plan list endpoint with the company context and nothing else', async () => {
    const { recorded } = stub()
    renderAt(PATH)
    await screen.findByRole('row', { name: new RegExp(PLAN.slice(-8)) })
    const [first] = lists(recorded)
    expect(first.url).toMatch(/\/api\/v1\/manufacturing\/plans$/)
    expect(first.headers['X-Company-Id']).toBe(COMPANY_A)
    // A read: no write of any kind was sent.
    expect(recorded.filter((request) => request.method !== 'GET')).toHaveLength(0)
  })

  it('links each plan to its existing detail for a holder of the plan detail capability', async () => {
    stub()
    renderAt(PATH)
    const link = await screen.findByRole('link', { name: new RegExp(PLAN.slice(-8)) })
    expect(link.getAttribute('href')).toBe(`/uretim/plan/${PLAN}`)
  })

  it('shows the row without a link when only the list is granted', async () => {
    stub({ capabilities: [MANUFACTURING_CAPABILITY.planList] })
    renderAt(PATH)
    await screen.findByRole('row', { name: new RegExp(PLAN.slice(-8)) })
    expect(screen.queryByRole('link', { name: new RegExp(PLAN.slice(-8)) })).toBeNull()
  })

  it('draws the create and order-list actions only for their own capabilities', async () => {
    stub({
      capabilities: [...PLAN_LIST_CAPABILITIES, MANUFACTURING_CAPABILITY.planCreate, MANUFACTURING_CAPABILITY.orderList],
    })
    renderAt(PATH)
    await screen.findByRole('row', { name: new RegExp(PLAN.slice(-8)) })
    const create = screen.getAllByRole('link', { name: 'Yeni üretim planı' })
    expect(create).toHaveLength(1)
    expect(create[0].getAttribute('href')).toBe('/uretim/plan/yeni')
    // Inside the list's own header: the sidebar has its own "Üretim emirleri" entry.
    const page = screen.getByRole('region', { name: 'Üretim planları' })
    expect(within(page).getByRole('link', { name: 'Üretim emirleri' }).getAttribute('href')).toBe('/uretim/emirler')
  })

  it('draws neither action when their capabilities are absent', async () => {
    stub()
    renderAt(PATH)
    await screen.findByRole('row', { name: new RegExp(PLAN.slice(-8)) })
    expect(screen.queryByRole('link', { name: 'Yeni üretim planı' })).toBeNull()
    const page = screen.getByRole('region', { name: 'Üretim planları' })
    expect(within(page).queryByRole('link', { name: 'Üretim emirleri' })).toBeNull()
  })

  it('does not open for an actor without the plan list capability, and asks nothing', async () => {
    const { recorded } = stub({ capabilities: LIST_CAPABILITIES })
    renderAt(PATH)
    await waitFor(() => expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
    expect(screen.queryByRole('table')).toBeNull()
    expect(lists(recorded)).toHaveLength(0)
  })

  it('sends the server’s own cursor back unchanged and appends the next page once', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({
      pages: [
        () => json(planList([planSummary()], 'CURSOR-1')),
        () => json(planList([planSummary(), planSummary({ planId: SECOND })], null)),
      ],
    })
    renderAt(PATH)
    await screen.findByRole('row', { name: new RegExp(PLAN.slice(-8)) })

    await user.click(screen.getByRole('button', { name: 'Sonraki sayfayı yükle' }))
    await screen.findByRole('row', { name: new RegExp(SECOND.slice(-8)) })
    expect(await screen.findByText('Liste sonu.')).toBeTruthy()

    const second = lists(recorded)[1]
    expect(second.url).toContain('cursor=CURSOR-1')
    // The first row is still there, once, even though the next page repeated it.
    expect(screen.getAllByRole('row', { name: new RegExp(PLAN.slice(-8)) })).toHaveLength(1)
  })

  it('keeps the rows already shown when the next page fails, and retries the SAME cursor', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({
      pages: [
        () => json(planList([planSummary()], 'CURSOR-1')),
        () => problem(503, { code: 'service_unavailable' }),
        () => json(planList([planSummary({ planId: SECOND })], null)),
      ],
    })
    renderAt(PATH)
    await screen.findByRole('row', { name: new RegExp(PLAN.slice(-8)) })
    await user.click(screen.getByRole('button', { name: 'Sonraki sayfayı yükle' }))

    expect(await screen.findByText('Sonraki sayfa okunamadı; yukarıdaki satırlar yerinde kaldı.')).toBeTruthy()
    expect(screen.getByRole('row', { name: new RegExp(PLAN.slice(-8)) })).toBeTruthy()

    await user.click(screen.getByRole('button', { name: 'Tekrar dene' }))
    await screen.findByRole('row', { name: new RegExp(SECOND.slice(-8)) })
    const [, failed, retried] = lists(recorded)
    expect(retried.url).toBe(failed.url)
  })

  it('asks the server for the status it was given and starts the list again', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({
      pages: [() => json(planList([planSummary()], 'CURSOR-1')), () => json(planList([], null))],
    })
    renderAt(PATH)
    await screen.findByRole('row', { name: new RegExp(PLAN.slice(-8)) })

    await user.selectOptions(screen.getByLabelText('Durum'), 'Approved')
    await waitFor(() => expect(lists(recorded).length).toBeGreaterThan(1))
    const filtered = lists(recorded)[1]
    expect(filtered.url).toContain('status=Approved')
    // A filter restarts the list: the previous page's cursor is not carried over.
    expect(filtered.url).not.toContain('cursor=')
    expect(await screen.findByText('Bu görünümde henüz kayıt yok.')).toBeTruthy()
  })

  it('offers exactly the plan statuses, never an order status', async () => {
    stub()
    renderAt(PATH)
    await screen.findByRole('row', { name: new RegExp(PLAN.slice(-8)) })
    const options = within(screen.getByLabelText('Durum')).getAllByRole('option')
    expect(options.map((option) => (option as HTMLOptionElement).value)).toEqual(['', 'Draft', 'Approved'])
  })

  it('says so when the company has no plan', async () => {
    stub({ pages: [() => json(planList([], null))] })
    renderAt(PATH)
    expect(await screen.findByText('Bu görünümde henüz kayıt yok.')).toBeTruthy()
  })

  it.each([
    ['a malformed id', { plans: [{ ...planSummary(), planId: 'not-a-guid' }], nextCursor: null }],
    ['an approved plan without its approval time', { plans: [planSummary({ status: 'Approved' })], nextCursor: null }],
    ['a draft with an approval time', { plans: [planSummary({ approvedAtUtc: APPROVED_AT })], nextCursor: null }],
    ['an order status', { plans: [planSummary({ status: 'Open' })], nextCursor: null }],
    ['a rounded kilogram text', { plans: [planSummary({ plannedInputKg: '1500.60' })], nextCursor: null }],
    ['an extra field', { plans: [{ ...planSummary(), orderId: PLAN }], nextCursor: null }],
    ['a missing field', { plans: [{ planId: PLAN }], nextCursor: null }],
    ['an empty cursor', { plans: [planSummary()], nextCursor: '' }],
    ['a repeated plan', { plans: [planSummary(), planSummary()], nextCursor: null }],
  ])('shows no table at all for %s', async (_case, body) => {
    stub({ pages: [() => json(body)] })
    renderAt(PATH)
    // Fail closed: a body that does not match the contract is not half-shown.
    await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    expect(screen.queryByRole('table')).toBeNull()
  })

  it.each([401, 403])('never exposes rows after the auth boundary receives %s', async (status) => {
    stub({ pages: [() => problem(status)] })
    renderAt(PATH)
    await screen.findByRole('alert')
    expect(screen.queryByRole('table')).toBeNull()
  })

  it('shows a retryable state for a 503 and reads again on retry', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({
      pages: [() => problem(503, { code: 'service_unavailable' }), () => json(planList())],
    })
    renderAt(PATH)
    await user.click(await screen.findByRole('button', { name: 'Tekrar dene' }))
    await screen.findByRole('row', { name: new RegExp(PLAN.slice(-8)) })
    expect(lists(recorded)).toHaveLength(2)
  })

  it('ignores a late company-A answer after switching to company B', async () => {
    installFetch({
      capabilities: PLAN_LIST_CAPABILITIES,
      respond: (url) => (/\/manufacturing\/plans(\?|$)/.test(url) ? json(planList([], null)) : undefined),
    })
    const normalFetch = globalThis.fetch
    let complete: (value: Response) => void = () => {
      throw new Error('Deferred read was not started')
    }
    const delayed = new Promise<Response>((resolve) => {
      complete = resolve
    })
    const calls: string[] = []
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
        const url = typeof input === 'string' ? input : input.toString()
        if (/\/manufacturing\/plans(\?|$)/.test(url)) {
          const company = new Headers(init?.headers).get('X-Company-Id') ?? ''
          calls.push(company)
          if (company === COMPANY_A) return delayed
        }
        return normalFetch(input, init)
      }),
    )
    renderAt(PATH)
    await waitFor(() => expect(calls).toContain(COMPANY_A))
    fireEvent.change(screen.getByRole('combobox', { name: 'Şirket' }), { target: { value: COMPANY_B } })
    await screen.findByText('Bu görünümde henüz kayıt yok.')
    await act(async () => {
      complete(json(planList([planSummary({ planId: SECOND })])))
    })
    expect(calls).toContain(COMPANY_B)
    expect(screen.queryByRole('row', { name: new RegExp(SECOND.slice(-8)) })).toBeNull()
    expect(screen.queryByRole('table')).toBeNull()
  })
})

describe('plan list contract', () => {
  it('decodes both statuses and the end of the list', () => {
    const page = decodePlanList(
      planList([planSummary(), planSummary({ planId: SECOND, status: 'Approved', version: 2, approvedAtUtc: APPROVED_AT })]),
    )
    expect(page?.plans.map((row) => row.status)).toEqual(['Draft', 'Approved'])
    expect(page?.nextCursor).toBeNull()
  })

  it('refuses a body that is not the page', () => {
    expect(decodePlanList(null)).toBeNull()
    expect(decodePlanList([])).toBeNull()
    expect(decodePlanList({ plans: [] })).toBeNull()
    expect(decodePlanList({ orders: [], nextCursor: null })).toBeNull()
    expect(decodePlanList({ plans: [], nextCursor: 5 })).toBeNull()
  })
})
