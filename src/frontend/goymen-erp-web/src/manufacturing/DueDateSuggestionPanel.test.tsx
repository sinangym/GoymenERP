import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MANUFACTURING_CAPABILITY } from './capabilities'
import { DUE_DATE_SUGGESTION_TEXT, dueDateSuggestionReasonText } from './dueDateSuggestionText'
import { suggestedDueDate, unavailableDueDate } from './dueDateSuggestionFixture'
import { COMPANY_A, PLAN, installFetch, json, plan, problem, renderAt, requestsFor } from './testHarness'

const EDIT_CAPABILITIES = [MANUFACTURING_CAPABILITY.planDetail, MANUFACTURING_CAPABILITY.planEdit]
const SUGGESTION_PATH = `/manufacturing/plans/${PLAN}/due-date-suggestion`

afterEach(() => {
  vi.unstubAllGlobals()
})

function respondWith(suggestion: () => Response, storedPlan: () => unknown = () => plan()) {
  return (url: string, init?: RequestInit) => {
    if (url.endsWith(`/manufacturing/plans/${PLAN}`) && init?.method !== 'POST') return json(storedPlan())
    if (url.endsWith(SUGGESTION_PATH) && init?.method !== 'POST') return suggestion()
    return undefined
  }
}

async function openTerminForm(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('button', { name: 'Termini değiştir' }))
}

describe('F-010b termin suggestion (URT-S10)', () => {
  it('reads nothing until the user asks for a suggestion', async () => {
    const user = userEvent.setup()
    const recorded = installFetch({
      capabilities: EDIT_CAPABILITIES,
      respond: respondWith(() => json(suggestedDueDate())),
    })
    renderAt(`/uretim/plan/${PLAN}`)

    await openTerminForm(user)

    expect(screen.getByRole('button', { name: DUE_DATE_SUGGESTION_TEXT.request })).toBeTruthy()
    expect(requestsFor(recorded, SUGGESTION_PATH)).toHaveLength(0)
  })

  it('shows the server suggestion with its inputs and only fills the field when used; the save sends it', async () => {
    const user = userEvent.setup()
    let storedPlan = plan()
    const recorded = installFetch({
      capabilities: EDIT_CAPABILITIES,
      respond: (url, init) => {
        if (url.endsWith(`/manufacturing/plans/${PLAN}/due-date`) && init?.method === 'POST') {
          storedPlan = plan({ requestedDueDate: '2026-09-22', dueDateRevision: 1 })
          return json(storedPlan)
        }
        return respondWith(() => json(suggestedDueDate()), () => storedPlan)(url, init)
      },
    })
    renderAt(`/uretim/plan/${PLAN}`)

    await openTerminForm(user)
    await user.click(screen.getByRole('button', { name: DUE_DATE_SUGGESTION_TEXT.request }))

    expect((await screen.findByTestId('due-date-suggestion-day')).textContent).toContain('2026-09-22')
    expect(screen.getByText(/100 kg\/saat/)).toBeTruthy()
    expect(screen.getByText('2026-09-22 11:00')).toBeTruthy()
    // URT-D86/URT-D89: the queued work and the margin are the server's figures, shown as they came.
    expect(screen.getByTestId('due-date-suggestion-queue').textContent).toBe('2 plan · 400.5 kg')
    expect(screen.getByTestId('due-date-suggestion-margin').textContent).toBe('1.5 saat · 1 iş günü')
    const reads = requestsFor(recorded, SUGGESTION_PATH, 'GET')
    expect(reads).toHaveLength(1)
    expect(reads[0].headers['X-Company-Id']).toBe(COMPANY_A)
    // Nothing is written by asking.
    expect(requestsFor(recorded, `/manufacturing/plans/${PLAN}/due-date`, 'POST')).toHaveLength(0)

    await user.click(screen.getByRole('button', { name: DUE_DATE_SUGGESTION_TEXT.use }))

    expect((screen.getByLabelText('Termin tarihi') as HTMLInputElement).value).toBe('2026-09-22')
    expect(screen.getByText(DUE_DATE_SUGGESTION_TEXT.used)).toBeTruthy()
    expect(requestsFor(recorded, `/manufacturing/plans/${PLAN}/due-date`, 'POST')).toHaveLength(0)

    await user.click(screen.getByRole('button', { name: 'Termini kaydet' }))

    expect(await screen.findByText('Termin kaydedildi ve güncel plan yeniden okundu.')).toBeTruthy()
    const sent = requestsFor(recorded, `/manufacturing/plans/${PLAN}/due-date`, 'POST')
    expect(sent).toHaveLength(1)
    expect(sent[0].body).toEqual({ expectedVersion: 1, expectedDueDateRevision: 0, requestedDueDate: '2026-09-22' })
  })

  it('names the reason of an unavailable answer and offers nothing to take', async () => {
    const user = userEvent.setup()
    installFetch({
      capabilities: EDIT_CAPABILITIES,
      respond: respondWith(() => json(unavailableDueDate('capacity_not_configured'))),
    })
    renderAt(`/uretim/plan/${PLAN}`)

    await openTerminForm(user)
    await user.click(screen.getByRole('button', { name: DUE_DATE_SUGGESTION_TEXT.request }))

    expect((await screen.findByTestId('due-date-suggestion-reason')).textContent)
      .toBe(dueDateSuggestionReasonText('capacity_not_configured'))
    expect(screen.queryByRole('button', { name: DUE_DATE_SUGGESTION_TEXT.use })).toBeNull()
  })

  it('shows a suggestion for another plan version as stale and offers nothing to take', async () => {
    const user = userEvent.setup()
    installFetch({
      capabilities: EDIT_CAPABILITIES,
      respond: respondWith(() => json(suggestedDueDate({ planVersion: 2 }))),
    })
    renderAt(`/uretim/plan/${PLAN}`)

    await openTerminForm(user)
    await user.click(screen.getByRole('button', { name: DUE_DATE_SUGGESTION_TEXT.request }))

    expect(await screen.findByText(DUE_DATE_SUGGESTION_TEXT.stalePlan)).toBeTruthy()
    expect(screen.queryByRole('button', { name: DUE_DATE_SUGGESTION_TEXT.use })).toBeNull()
  })

  it('fails closed on a body that is not the contract', async () => {
    const user = userEvent.setup()
    installFetch({
      capabilities: EDIT_CAPABILITIES,
      respond: respondWith(() => json({ ...suggestedDueDate(), extra: 1 })),
    })
    renderAt(`/uretim/plan/${PLAN}`)

    await openTerminForm(user)
    await user.click(screen.getByRole('button', { name: DUE_DATE_SUGGESTION_TEXT.request }))

    expect(await screen.findByText('Beklenmeyen yanıt')).toBeTruthy()
    expect(screen.queryByRole('button', { name: DUE_DATE_SUGGESTION_TEXT.use })).toBeNull()
  })

  it('offers a retry after a 503 and shows the suggestion the retry reads', async () => {
    const user = userEvent.setup()
    let calls = 0
    const recorded = installFetch({
      capabilities: EDIT_CAPABILITIES,
      respond: respondWith(() => {
        calls += 1
        return calls === 1 ? problem(503) : json(suggestedDueDate())
      }),
    })
    renderAt(`/uretim/plan/${PLAN}`)

    await openTerminForm(user)
    await user.click(screen.getByRole('button', { name: DUE_DATE_SUGGESTION_TEXT.request }))

    expect(await screen.findByText('Servis geçici olarak kullanılamıyor')).toBeTruthy()
    await user.click(screen.getByRole('button', { name: /tekrar dene/i }))

    expect((await screen.findByTestId('due-date-suggestion-day')).textContent).toContain('2026-09-22')
    expect(requestsFor(recorded, SUGGESTION_PATH, 'GET')).toHaveLength(2)
  })

  it('says a plan unknown in this company is not found', async () => {
    const user = userEvent.setup()
    installFetch({
      capabilities: EDIT_CAPABILITIES,
      respond: respondWith(() => problem(404)),
    })
    renderAt(`/uretim/plan/${PLAN}`)

    await openTerminForm(user)
    await user.click(screen.getByRole('button', { name: DUE_DATE_SUGGESTION_TEXT.request }))

    expect(await screen.findByText('Bulunamadı')).toBeTruthy()
  })
})
