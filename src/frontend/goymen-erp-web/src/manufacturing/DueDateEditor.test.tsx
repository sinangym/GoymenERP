import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MANUFACTURING_CAPABILITY } from './capabilities'
import { dueDateFailureText } from './dueDateText'
import { APPROVER, PLAN, installFetch, json, plan, problem, renderAt, requestsFor } from './testHarness'

const EDIT_CAPABILITIES = [MANUFACTURING_CAPABILITY.planDetail, MANUFACTURING_CAPABILITY.planEdit]

afterEach(() => {
  vi.unstubAllGlobals()
})

function dueDateRequests(recorded: Parameters<typeof requestsFor>[0]) {
  return requestsFor(recorded, `/manufacturing/plans/${PLAN}/due-date`, 'POST')
}

describe('F-010a hand-entered plan termin (URT-D29)', () => {
  it('shows the stored termin to a reader and draws no change control without the edit capability', async () => {
    installFetch({
      capabilities: [MANUFACTURING_CAPABILITY.planDetail],
      respond: (url, init) =>
        url.endsWith(`/manufacturing/plans/${PLAN}`) && init?.method !== 'POST'
          ? json(plan({ requestedDueDate: '2026-12-31', dueDateRevision: 1 }))
          : undefined,
    })
    renderAt(`/uretim/plan/${PLAN}`)

    expect(await screen.findByText('2026-12-31')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Termini değiştir' })).toBeNull()
  })

  it('says a plan without a termin has none', async () => {
    installFetch({
      capabilities: [MANUFACTURING_CAPABILITY.planDetail],
      respond: (url, init) =>
        url.endsWith(`/manufacturing/plans/${PLAN}`) && init?.method !== 'POST' ? json(plan()) : undefined,
    })
    renderAt(`/uretim/plan/${PLAN}`)

    expect(await screen.findByText('Termin girilmedi')).toBeTruthy()
  })

  it('sends the typed day with the read version and termin revision, then shows the re-read plan', async () => {
    const user = userEvent.setup()
    let storedPlan = plan({ version: 2 })
    const recorded = installFetch({
      capabilities: EDIT_CAPABILITIES,
      respond: (url, init) => {
        if (url.endsWith(`/manufacturing/plans/${PLAN}`) && init?.method !== 'POST') return json(storedPlan)
        if (url.endsWith(`/manufacturing/plans/${PLAN}/due-date`) && init?.method === 'POST') {
          storedPlan = plan({ version: 2, requestedDueDate: '2026-12-31', dueDateRevision: 1 })
          return json(storedPlan)
        }
        return undefined
      },
    })
    renderAt(`/uretim/plan/${PLAN}`)

    await user.click(await screen.findByRole('button', { name: 'Termini değiştir' }))
    fireEvent.change(screen.getByLabelText('Termin tarihi'), { target: { value: '2026-12-31' } })
    await user.click(screen.getByRole('button', { name: 'Termini kaydet' }))

    expect(await screen.findByText('Termin kaydedildi ve güncel plan yeniden okundu.')).toBeTruthy()
    expect(screen.getByText('2026-12-31')).toBeTruthy()
    const sent = dueDateRequests(recorded)
    expect(sent).toHaveLength(1)
    expect(sent[0].headers['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/i)
    expect(sent[0].body).toEqual({ expectedVersion: 2, expectedDueDateRevision: 0, requestedDueDate: '2026-12-31' })
  })

  it('removes the termin by sending null', async () => {
    const user = userEvent.setup()
    let storedPlan = plan({ requestedDueDate: '2026-12-31', dueDateRevision: 1 })
    const recorded = installFetch({
      capabilities: EDIT_CAPABILITIES,
      respond: (url, init) => {
        if (url.endsWith(`/manufacturing/plans/${PLAN}`) && init?.method !== 'POST') return json(storedPlan)
        if (url.endsWith(`/manufacturing/plans/${PLAN}/due-date`) && init?.method === 'POST') {
          storedPlan = plan({ requestedDueDate: null, dueDateRevision: 2 })
          return json(storedPlan)
        }
        return undefined
      },
    })
    renderAt(`/uretim/plan/${PLAN}`)

    await user.click(await screen.findByRole('button', { name: 'Termini değiştir' }))
    await user.click(screen.getByRole('button', { name: 'Termini kaldır' }))

    expect(await screen.findByText('Termin kaldırıldı ve güncel plan yeniden okundu.')).toBeTruthy()
    expect(dueDateRequests(recorded)[0].body).toEqual({
      expectedVersion: 1,
      expectedDueDateRevision: 1,
      requestedDueDate: null,
    })
  })

  it('keeps the save closed while the typed termin equals the stored one', async () => {
    const user = userEvent.setup()
    installFetch({
      capabilities: EDIT_CAPABILITIES,
      respond: (url, init) =>
        url.endsWith(`/manufacturing/plans/${PLAN}`) && init?.method !== 'POST'
          ? json(plan({ requestedDueDate: '2026-12-31', dueDateRevision: 1 }))
          : undefined,
    })
    renderAt(`/uretim/plan/${PLAN}`)

    await user.click(await screen.findByRole('button', { name: 'Termini değiştir' }))
    const save = screen.getByRole('button', { name: 'Termini kaydet' }) as HTMLButtonElement
    expect(save.disabled).toBe(true)
    expect(screen.getByText('Girilen termin plandaki terminle aynı.')).toBeTruthy()
  })

  it('re-reads the plan after a moved termin revision and names the conflict', async () => {
    const user = userEvent.setup()
    let storedPlan = plan()
    installFetch({
      capabilities: EDIT_CAPABILITIES,
      respond: (url, init) => {
        if (url.endsWith(`/manufacturing/plans/${PLAN}`) && init?.method !== 'POST') return json(storedPlan)
        if (url.endsWith(`/manufacturing/plans/${PLAN}/due-date`) && init?.method === 'POST') {
          storedPlan = plan({ requestedDueDate: '2027-01-15', dueDateRevision: 1 })
          return problem(409, { code: 'manufacturing_plan_due_date_changed' })
        }
        return undefined
      },
    })
    renderAt(`/uretim/plan/${PLAN}`)

    await user.click(await screen.findByRole('button', { name: 'Termini değiştir' }))
    fireEvent.change(screen.getByLabelText('Termin tarihi'), { target: { value: '2026-12-31' } })
    await user.click(screen.getByRole('button', { name: 'Termini kaydet' }))

    expect(await screen.findByText(new RegExp(
      dueDateFailureText(409, 'manufacturing_plan_due_date_changed').replace(/[.*+?^${}()|[\]\\]/g, '\\$&'),
    ))).toBeTruthy()
    expect(await screen.findByText('2027-01-15')).toBeTruthy()
  })

  it('names a closed order refusal without re-sending', async () => {
    const user = userEvent.setup()
    const recorded = installFetch({
      capabilities: EDIT_CAPABILITIES,
      respond: (url, init) => {
        if (url.endsWith(`/manufacturing/plans/${PLAN}`) && init?.method !== 'POST') return json(plan())
        if (url.endsWith(`/manufacturing/plans/${PLAN}/due-date`) && init?.method === 'POST') {
          return problem(422, { code: 'manufacturing_plan_due_date_order_closed' })
        }
        return undefined
      },
    })
    renderAt(`/uretim/plan/${PLAN}`)

    await user.click(await screen.findByRole('button', { name: 'Termini değiştir' }))
    fireEvent.change(screen.getByLabelText('Termin tarihi'), { target: { value: '2026-12-31' } })
    await user.click(screen.getByRole('button', { name: 'Termini kaydet' }))

    expect(await screen.findByText('Planın üretim emri kapanmış; termin artık değiştirilemez.')).toBeTruthy()
    expect(dueDateRequests(recorded)).toHaveLength(1)
  })

  it('re-sends an unknown termin change with the same key and body', async () => {
    const user = userEvent.setup()
    let attempts = 0
    let storedPlan = plan()
    const recorded = installFetch({
      capabilities: EDIT_CAPABILITIES,
      respond: (url, init) => {
        if (url.endsWith(`/manufacturing/plans/${PLAN}`) && init?.method !== 'POST') return json(storedPlan)
        if (url.endsWith(`/manufacturing/plans/${PLAN}/due-date`) && init?.method === 'POST') {
          attempts += 1
          if (attempts === 1) throw new TypeError('connection lost')
          storedPlan = plan({ requestedDueDate: '2026-12-31', dueDateRevision: 1 })
          return json(storedPlan, 200, {
            'Idempotency-Replayed': 'true',
            'Idempotency-Original-Correlation-Id': 'corr-due-1',
          })
        }
        return undefined
      },
    })
    renderAt(`/uretim/plan/${PLAN}`)

    await user.click(await screen.findByRole('button', { name: 'Termini değiştir' }))
    fireEvent.change(screen.getByLabelText('Termin tarihi'), { target: { value: '2026-12-31' } })
    await user.click(screen.getByRole('button', { name: 'Termini kaydet' }))
    expect(await screen.findByText('Termin değişikliğinin sonucu bilinmiyor. Aynı istek ve anahtarla yeniden gönderin.')).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'Aynı istek ve anahtarla yeniden gönder' }))

    expect(await screen.findByText('Termin kaydedildi ve güncel plan yeniden okundu.')).toBeTruthy()
    const [first, second] = dueDateRequests(recorded)
    expect(second.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])
    expect(second.body).toEqual(first.body)
  })

  it('draws no change control on a cancelled plan', async () => {
    installFetch({
      capabilities: EDIT_CAPABILITIES,
      respond: (url, init) =>
        url.endsWith(`/manufacturing/plans/${PLAN}`) && init?.method !== 'POST'
          ? json(plan({
            status: 'Cancelled',
            version: 2,
            cancellation: { reason: 'Vazgecildi', cancelledByUserId: APPROVER, cancelledAtUtc: '2026-11-12T09:00:00Z' },
          }))
          : undefined,
    })
    renderAt(`/uretim/plan/${PLAN}`)

    expect(await screen.findByText('İptal edilmiş planın termini değiştirilemez.')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Termini değiştir' })).toBeNull()
  })

  it('keeps an approved plan termin: no remove control, an empty day is not sent (URT-D66)', async () => {
    const user = userEvent.setup()
    installFetch({
      capabilities: EDIT_CAPABILITIES,
      respond: (url, init) =>
        url.endsWith(`/manufacturing/plans/${PLAN}`) && init?.method !== 'POST'
          ? json(plan({
            status: 'Approved',
            version: 2,
            approvedByUserId: APPROVER,
            approvedAtUtc: '2026-09-24T09:00:00Z',
            requestedDueDate: '2026-12-31',
            dueDateRevision: 1,
          }))
          : undefined,
    })
    renderAt(`/uretim/plan/${PLAN}`)

    await user.click(await screen.findByRole('button', { name: 'Termini değiştir' }))
    expect(screen.getByText('Onaylı planın termini değiştirilebilir ama kaldırılamaz.')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Termini kaldır' })).toBeNull()
  })

  it('names the past day and the kept termin refusals (URT-D66)', () => {
    expect(dueDateFailureText(422, 'manufacturing_due_date_in_past')).toMatch(/İstanbul/)
    expect(dueDateFailureText(422, 'manufacturing_plan_due_date_required')).toBe(
      'Onaylı planın termini kaldırılamaz; termin kaydedilmedi.',
    )
  })
})
