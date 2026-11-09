import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MANUFACTURING_CAPABILITY } from './capabilities'
import { APPROVER, IDENTITY, PLAN, installFetch, json, plan, problem, renderAt, requestsFor } from './testHarness'

const CAPABILITIES = [
  MANUFACTURING_CAPABILITY.planDetail,
  MANUFACTURING_CAPABILITY.planApprove,
  MANUFACTURING_CAPABILITY.planCancel,
]
const OTHER_PREPARER = 'preparer-other'

afterEach(() => {
  vi.unstubAllGlobals()
})

function isPlanRead(url: string, init?: RequestInit) {
  return url.endsWith(`/manufacturing/plans/${PLAN}`) && init?.method !== 'POST'
}

describe('plan rejection and cancellation', () => {
  it('rejects the read version with a trimmed reason and re-reads the Draft', async () => {
    const user = userEvent.setup()
    let stored = plan({ preparedBy: OTHER_PREPARER })
    const recorded = installFetch({
      capabilities: CAPABILITIES,
      respond: (url, init) => {
        if (isPlanRead(url, init)) return json(stored)
        if (url.endsWith(`/manufacturing/plans/${PLAN}/rejection`) && init?.method === 'POST') {
          stored = plan({
            version: 2,
            preparedBy: OTHER_PREPARER,
            rejections: [{ number: 1, reason: 'Eksik girdi', rejectedByUserId: APPROVER, rejectedAtUtc: '2026-09-24T09:00:00Z', planVersion: 2 }],
          })
          return json(stored)
        }
        return undefined
      },
    })
    renderAt(`/uretim/plan/${PLAN}`)

    await user.click(await screen.findByRole('button', { name: 'Planı reddet' }))
    const confirm = screen.getByRole('button', { name: 'Reddi gönder' }) as HTMLButtonElement
    expect(confirm.disabled).toBe(true)
    await user.type(screen.getByLabelText('Gerekçe'), '  Eksik girdi  ')
    await user.click(confirm)

    expect(await screen.findByText('Plan reddedildi; taslak olarak kaldı ve güncel kayıt yeniden okundu.')).toBeTruthy()
    expect(screen.getByText('1. ret (sürüm 2): Eksik girdi')).toBeTruthy()
    const [request] = requestsFor(recorded, `/manufacturing/plans/${PLAN}/rejection`, 'POST')
    expect(request.body).toEqual({ expectedVersion: 1, reason: 'Eksik girdi' })
    expect(request.headers['Idempotency-Key']).toBeTruthy()
  })

  it('cancels the read version and shows the plan as cancelled', async () => {
    const user = userEvent.setup()
    let stored = plan()
    const recorded = installFetch({
      capabilities: CAPABILITIES,
      respond: (url, init) => {
        if (isPlanRead(url, init)) return json(stored)
        if (url.endsWith(`/manufacturing/plans/${PLAN}/cancellation`) && init?.method === 'POST') {
          stored = plan({
            status: 'Cancelled',
            version: 2,
            cancellation: { reason: 'Vazgecildi', cancelledByUserId: APPROVER, cancelledAtUtc: '2026-09-24T09:00:00Z' },
          })
          return json(stored)
        }
        return undefined
      },
    })
    renderAt(`/uretim/plan/${PLAN}`)

    await user.click(await screen.findByRole('button', { name: 'Planı iptal et' }))
    await user.type(screen.getByLabelText('Gerekçe'), 'Vazgecildi')
    await user.click(screen.getByRole('button', { name: 'İptali gönder' }))

    expect(await screen.findByText('Plan iptal edildi ve güncel kayıt yeniden okundu.')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Planı iptal et' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Planı onayla' })).toBeNull()
    expect(requestsFor(recorded, `/manufacturing/plans/${PLAN}/cancellation`, 'POST')[0].body)
      .toEqual({ expectedVersion: 1, reason: 'Vazgecildi' })
  })

  it('re-sends an unknown rejection with the same key and body', async () => {
    const user = userEvent.setup()
    let calls = 0
    const recorded = installFetch({
      capabilities: CAPABILITIES,
      respond: (url, init) => {
        if (isPlanRead(url, init)) return json(plan({ preparedBy: OTHER_PREPARER }))
        if (url.endsWith('/rejection') && init?.method === 'POST') {
          calls += 1
          if (calls === 1) throw new TypeError('connection lost')
          return problem(422, { code: 'manufacturing_plan_cancelled' })
        }
        return undefined
      },
    })
    renderAt(`/uretim/plan/${PLAN}`)

    await user.click(await screen.findByRole('button', { name: 'Planı reddet' }))
    await user.type(screen.getByLabelText('Gerekçe'), 'Neden')
    await user.click(screen.getByRole('button', { name: 'Reddi gönder' }))
    expect(await screen.findByText(/Plan reddinin sonucu bilinmiyor/)).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'Aynı istek ve anahtarla yeniden gönder' }))

    expect(await screen.findByText('Plan zaten iptal edilmiş; yeni karar verilemez.')).toBeTruthy()
    const [first, second] = requestsFor(recorded, '/rejection', 'POST')
    expect(second.body).toEqual(first.body)
    expect(second.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])
  })

  it('names the order refusal of a cancellation', async () => {
    const user = userEvent.setup()
    const recorded = installFetch({
      capabilities: CAPABILITIES,
      respond: (url, init) => {
        if (isPlanRead(url, init)) return json(plan({ status: 'Approved', version: 2, approvedByUserId: APPROVER, approvedAtUtc: '2026-09-24T09:00:00Z' }))
        if (url.endsWith('/cancellation') && init?.method === 'POST') return problem(422, { code: 'manufacturing_plan_has_order' })
        return undefined
      },
    })
    renderAt(`/uretim/plan/${PLAN}`)

    await user.click(await screen.findByRole('button', { name: 'Planı iptal et' }))
    await user.type(screen.getByLabelText('Gerekçe'), 'Neden')
    await user.click(screen.getByRole('button', { name: 'İptali gönder' }))

    expect(await screen.findByText('Bu plandan üretim emri açıldığı için plan iptal edilemez.')).toBeTruthy()
    expect(requestsFor(recorded, '/cancellation', 'POST')).toHaveLength(1)
  })

  it('refreshes after a 409 on rejection', async () => {
    const user = userEvent.setup()
    let reads = 0
    installFetch({
      capabilities: CAPABILITIES,
      respond: (url, init) => {
        if (isPlanRead(url, init)) {
          reads += 1
          return json(plan({ preparedBy: OTHER_PREPARER, version: reads === 1 ? 1 : 3 }))
        }
        if (url.endsWith('/rejection') && init?.method === 'POST') return problem(409, { code: 'manufacturing_plan_version_conflict' })
        return undefined
      },
    })
    renderAt(`/uretim/plan/${PLAN}`)

    await user.click(await screen.findByRole('button', { name: 'Planı reddet' }))
    await user.type(screen.getByLabelText('Gerekçe'), 'Neden')
    await user.click(screen.getByRole('button', { name: 'Reddi gönder' }))

    expect(await screen.findByText(/Plan reddi uygulanmadı \(manufacturing_plan_version_conflict\)\. Güncel plan yeniden okundu\./)).toBeTruthy()
  })

  it('hides approve and reject for the preparer and says why; cancel stays', async () => {
    const recorded = installFetch({
      capabilities: CAPABILITIES,
      respond: (url, init) => isPlanRead(url, init) ? json(plan({ preparedBy: IDENTITY.userId })) : undefined,
    })
    renderAt(`/uretim/plan/${PLAN}`)

    expect(await screen.findByText(/hazırlayan onaylayamaz veya reddedemez/)).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Planı onayla' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Planı reddet' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Planı iptal et' })).toBeTruthy()
    expect(requestsFor(recorded, '/rejection', 'POST')).toHaveLength(0)
  })

  it('names the server self-approval refusal when the preparer is unknown to the screen', async () => {
    const user = userEvent.setup()
    installFetch({
      capabilities: CAPABILITIES,
      respond: (url, init) => {
        if (isPlanRead(url, init)) return json(plan({ preparedBy: null }))
        if (url.endsWith('/approval') && init?.method === 'POST') return problem(422, { code: 'manufacturing_plan_self_approval' })
        return undefined
      },
    })
    renderAt(`/uretim/plan/${PLAN}`)

    await user.click(await screen.findByRole('button', { name: 'Planı onayla' }))
    expect(await screen.findByText('Planın bu sürümünü siz hazırladınız; onayı başka bir kullanıcı vermelidir.')).toBeTruthy()
  })

  it('draws no cancel control without its capability and no reject without the approval grant', async () => {
    installFetch({
      capabilities: [MANUFACTURING_CAPABILITY.planDetail],
      respond: (url, init) => isPlanRead(url, init) ? json(plan({ preparedBy: OTHER_PREPARER })) : undefined,
    })
    renderAt(`/uretim/plan/${PLAN}`)

    await screen.findByRole('heading', { name: 'Plan yeniden açıldı' })
    expect(screen.queryByRole('button', { name: 'Planı iptal et' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Planı reddet' })).toBeNull()
  })
})
