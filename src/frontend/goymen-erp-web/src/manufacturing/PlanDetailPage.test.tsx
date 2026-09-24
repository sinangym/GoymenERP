import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MANUFACTURING_CAPABILITY, orderDetailPath } from './capabilities'
import {
  APPROVER,
  COMPANY_A,
  PLAN,
  installFetch,
  json,
  massBalance,
  order,
  plan,
  renderAt,
  requestsFor,
} from './testHarness'

const LIFECYCLE_CAPABILITIES = [
  MANUFACTURING_CAPABILITY.planDetail,
  MANUFACTURING_CAPABILITY.planApprove,
  MANUFACTURING_CAPABILITY.planOrderCreate,
  MANUFACTURING_CAPABILITY.orderDetail,
]

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('plan approval and order creation', () => {
  it('approves the read version, re-reads the plan, creates one order and opens its fresh detail', async () => {
    const user = userEvent.setup()
    let storedPlan = plan()
    let storedOrder: ReturnType<typeof order> | null = null
    const recorded = installFetch({
      capabilities: LIFECYCLE_CAPABILITIES,
      respond: (url, init) => {
        if (url.endsWith(`/manufacturing/plans/${PLAN}`) && init?.method !== 'POST') return json(storedPlan)
        if (url.endsWith(`/manufacturing/plans/${PLAN}/approval`) && init?.method === 'POST') {
          storedPlan = plan({
            status: 'Approved',
            version: 2,
            approvedByUserId: APPROVER,
            approvedAtUtc: '2026-09-24T09:00:00Z',
          })
          return json(storedPlan)
        }
        if (url.endsWith('/manufacturing/orders') && init?.method === 'POST') {
          const request = JSON.parse(init.body as string) as { orderId: string; planId: string }
          storedOrder = order({ orderId: request.orderId, planId: request.planId })
          return json(storedOrder, 201, { Location: `/api/v1/manufacturing/orders/${request.orderId}` })
        }
        if (storedOrder !== null && url.endsWith(`/manufacturing/orders/${storedOrder.orderId}`)) {
          return json(storedOrder)
        }
        if (storedOrder !== null && url.endsWith(`/manufacturing/orders/${storedOrder.orderId}/mass-balance`)) {
          return json(massBalance({ orderId: storedOrder.orderId }))
        }
        return undefined
      },
    })
    const { router } = renderAt(`/uretim/plan/${PLAN}`)

    await user.click(await screen.findByRole('button', { name: 'Planı onayla' }))
    expect(await screen.findByText('Plan onaylandı ve güncel kayıt yeniden okundu.')).toBeTruthy()
    expect(screen.getByText('Onaylı')).toBeTruthy()

    const approval = requestsFor(recorded, `/manufacturing/plans/${PLAN}/approval`, 'POST')
    expect(approval).toHaveLength(1)
    expect(approval[0].headers).toMatchObject({ 'X-Company-Id': COMPANY_A })
    expect(approval[0].headers['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/i)
    expect(approval[0].body).toEqual({ expectedVersion: 1 })
    expect(requestsFor(recorded, `/manufacturing/plans/${PLAN}`, 'GET')).toHaveLength(2)

    await user.click(screen.getByRole('button', { name: 'Üretim emri oluştur' }))
    await waitFor(() => expect(router.state.location.pathname).toMatch(/^\/uretim\/emir\/[0-9a-f-]{36}$/i))

    const create = requestsFor(recorded, '/manufacturing/orders', 'POST')
    expect(create).toHaveLength(1)
    expect(create[0].headers).toMatchObject({ 'X-Company-Id': COMPANY_A })
    expect(create[0].headers['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/i)
    expect(create[0].body).toMatchObject({ planId: PLAN })
    expect(router.state.location.pathname).toBe(orderDetailPath((create[0].body as { orderId: string }).orderId))
  })

  it('re-sends an unknown approval with the same key, body and read version', async () => {
    const user = userEvent.setup()
    let approvals = 0
    let storedPlan = plan()
    const recorded = installFetch({
      capabilities: [MANUFACTURING_CAPABILITY.planDetail, MANUFACTURING_CAPABILITY.planApprove],
      respond: (url, init) => {
        if (url.endsWith(`/manufacturing/plans/${PLAN}`) && init?.method !== 'POST') return json(storedPlan)
        if (url.endsWith(`/manufacturing/plans/${PLAN}/approval`) && init?.method === 'POST') {
          approvals += 1
          if (approvals === 1) throw new TypeError('connection lost')
          storedPlan = plan({
            status: 'Approved',
            version: 2,
            approvedByUserId: APPROVER,
            approvedAtUtc: '2026-09-24T09:00:00Z',
          })
          return json(storedPlan, 200, {
            'Idempotency-Replayed': 'true',
            'Idempotency-Original-Correlation-Id': 'corr-1',
          })
        }
        return undefined
      },
    })
    renderAt(`/uretim/plan/${PLAN}`)

    await user.click(await screen.findByRole('button', { name: 'Planı onayla' }))
    await user.click(await screen.findByRole('button', { name: 'Aynı istek ve anahtarla yeniden gönder' }))
    expect(await screen.findByText('Plan onaylandı ve güncel kayıt yeniden okundu.')).toBeTruthy()

    const [first, second] = requestsFor(recorded, `/manufacturing/plans/${PLAN}/approval`, 'POST')
    expect(second.body).toEqual(first.body)
    expect(second.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])
  })

  it('re-sends an unknown order create with the same order id and key', async () => {
    const user = userEvent.setup()
    let creates = 0
    const approved = plan({
      status: 'Approved',
      version: 2,
      approvedByUserId: APPROVER,
      approvedAtUtc: '2026-09-24T09:00:00Z',
    })
    const recorded = installFetch({
      capabilities: [MANUFACTURING_CAPABILITY.planDetail, MANUFACTURING_CAPABILITY.planOrderCreate],
      respond: (url, init) => {
        if (url.endsWith(`/manufacturing/plans/${PLAN}`)) return json(approved)
        if (url.endsWith('/manufacturing/orders') && init?.method === 'POST') {
          creates += 1
          if (creates === 1) throw new TypeError('connection lost')
          const request = JSON.parse(init.body as string) as { orderId: string; planId: string }
          return json(order({ orderId: request.orderId, planId: request.planId }), 201, {
            Location: `/api/v1/manufacturing/orders/${request.orderId}`,
            'Idempotency-Replayed': 'true',
            'Idempotency-Original-Correlation-Id': 'corr-2',
          })
        }
        return undefined
      },
    })
    renderAt(`/uretim/plan/${PLAN}`)

    await user.click(await screen.findByRole('button', { name: 'Üretim emri oluştur' }))
    await user.click(await screen.findByRole('button', { name: 'Aynı istek ve anahtarla yeniden gönder' }))
    expect(await screen.findByText(/Önceki emir oluşturma sonucu yeniden gösterildi/)).toBeTruthy()

    const [first, second] = requestsFor(recorded, '/manufacturing/orders', 'POST')
    expect(second.body).toEqual(first.body)
    expect(second.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])
  })

  it('draws neither command when the server publishes only plan read', async () => {
    const recorded = installFetch({
      capabilities: [MANUFACTURING_CAPABILITY.planDetail],
      respond: (url) => url.endsWith(`/manufacturing/plans/${PLAN}`) ? json(plan()) : undefined,
    })
    renderAt(`/uretim/plan/${PLAN}`)

    await screen.findByRole('heading', { name: 'Plan yeniden açıldı' })
    expect(screen.queryByRole('button', { name: 'Planı onayla' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Üretim emri oluştur' })).toBeNull()
    expect(requestsFor(recorded, '/approval', 'POST')).toHaveLength(0)
    expect(requestsFor(recorded, '/manufacturing/orders', 'POST')).toHaveLength(0)
  })
})
