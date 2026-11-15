/**
 * LOJ-D17 on L-02-DETAIL in the real shell: a cancelled instruction with loaded goods is marked "İndirme kaydı yok" and offers
 * the manual unload weighing; the two weighings are sent once with one weighing id kept across a lost-reply retry; the re-read
 * shows the backend's net, the kilograms loaded at the cancel and the signed difference with the backend's warning (no threshold
 * is configured, so every difference warns) and no verdict; a second record is refused and re-read; a direction contradiction is
 * explained on the field; a live instruction or one cancelled before anything was loaded offers no unload weighing.
 * Component evidence, NOT_ENV.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { INSTRUCTION_DETAIL_TEXT, PROBLEM_CODE_TEXT, UNLOAD_WEIGHING_TEXT as T } from './logisticsText'
import { resetPendingInstructionAttempts } from './pendingInstructionAttempts'
import {
  CANCELLED_INSTRUCTION,
  FORBIDDEN_MONEY_WORDS,
  INSTRUCTION,
  deferred,
  installFetch,
  instructionDetail,
  instructionLine,
  json,
  mutation,
  renderLogisticsApp,
  unloadWeighingRecord,
  weighingRecord,
  type RecordedCall,
} from './testHarness'

afterEach(() => {
  cleanup()
  resetPendingInstructionAttempts()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const path = `/lojistik/yukleme-talimatlari/${INSTRUCTION}`
const UNLOAD_URL = `/api/v1/logistics/loading-instructions/${INSTRUCTION}/unload-weighings`
const loadedLine = instructionLine({ loaded: '4.5', remainingToLoad: '0', loadedNotShipped: '4.5' })
/** Loaded, weighed and cancelled; the harness derives "Missing" (no unload weighing) or "Recorded". */
const cancelled = (unloadWeighing: Record<string, unknown> | null = null, version = '5') =>
  instructionDetail({ ...CANCELLED_INSTRUCTION, version }, [loadedLine], [], weighingRecord(), [], unloadWeighing)

function unloadAnswer(call: RecordedCall, isReplay = false): Response {
  const body = JSON.parse(call.body ?? '{}') as { weighingId: string; direction: string; firstWeighingKg: string; secondWeighingKg: string }
  const record = unloadWeighingRecord({ weighingId: body.weighingId, direction: body.direction, firstWeighingKg: body.firstWeighingKg,
    secondWeighingKg: body.secondWeighingKg })
  return json(mutation(cancelled(record, '6'), isReplay), 200, isReplay ? { 'Idempotency-Replayed': 'true' } : {})
}

const VERDICT_WORDS = ['tolerans dışı', 'Tolerans dışı', 'kabul edildi', 'reddedildi', 'uygun değil', 'Uygun']

describe('LOJ-D17 unload weighing after the cancel on L-02-DETAIL', () => {
  it('marks the instruction "İndirme kaydı yok", records the two weighings with one id across a lost reply, and shows the backend facts', async () => {
    const first = deferred<Response>()
    let sends = 0
    let reads = 0
    const harness = installFetch({
      onDetail: () => json(++reads === 1 ? cancelled() : cancelled(unloadWeighingRecord(), '6')),
      onUnloadWeighing: (call) => (++sends === 1 ? first.promise : unloadAnswer(call, true)),
    })
    renderLogisticsApp(path)
    expect((await screen.findByTestId('logistics-unload-missing')).textContent).toContain(T.missing)
    expect(screen.getByTestId('logistics-unload-weighing-none').textContent).toBe(T.none)
    await userEvent.type(screen.getByLabelText(T.first), '15004')
    await userEvent.type(screen.getByLabelText(T.second), '15000')
    await userEvent.click(screen.getByTestId('logistics-unload-weighing-button'))
    await screen.findByText(INSTRUCTION_DETAIL_TEXT.command.submitting)
    first.reject(new TypeError('Failed to fetch'))
    await screen.findByText(INSTRUCTION_DETAIL_TEXT.command.unknown)
    await userEvent.click(screen.getByRole('button', { name: INSTRUCTION_DETAIL_TEXT.command.retry }))
    await waitFor(() => expect(screen.getByTestId('logistics-unload-weighing-notice').textContent).toBe(INSTRUCTION_DETAIL_TEXT.command.replayed))

    const posts = harness.logisticsCalls().filter((call) => call.method === 'POST')
    expect(posts).toHaveLength(2)
    expect(posts[0].url).toBe(UNLOAD_URL)
    expect(posts[0].idempotencyKey).toBe(posts[1].idempotencyKey)
    expect(posts[0].body).toBe(posts[1].body)
    const sent = JSON.parse(posts[0].body ?? '{}') as Record<string, unknown>
    // Only what the person read off the ticket travels: no net, difference, threshold, operator or time.
    expect(Object.keys(sent).sort()).toEqual(['direction', 'expectedVersion', 'firstWeighingKg', 'secondWeighingKg', 'weighingId'])
    expect(sent).toMatchObject({ expectedVersion: '5', direction: 'GrossFirst', firstWeighingKg: '15004', secondWeighingKg: '15000' })

    // The re-read: the backend's record, the sign kept, the warning shown (it never blocks), the mark and the form gone.
    await waitFor(() => expect(screen.getByTestId('logistics-unload-weighing-difference').textContent).toBe('-0.5'))
    expect(screen.getByTestId('logistics-unload-weighing-net').textContent).toBe('4')
    expect(screen.getByTestId('logistics-unload-weighing-loaded').textContent).toBe('4.5')
    expect(screen.getByTestId('logistics-unload-weighing-warning').textContent).toBe(`${T.warning} ${T.noThreshold}`)
    expect(screen.queryByTestId('logistics-unload-missing')).toBeNull()
    expect(screen.queryByTestId('logistics-unload-weighing-button')).toBeNull()
    // The instruction stays cancelled; the unload weighing changes nothing else on it.
    expect(screen.getByTestId('logistics-instruction-status').textContent).toBe('İptal edildi')
    for (const word of [...VERDICT_WORDS, ...FORBIDDEN_MONEY_WORDS]) expect(document.body.textContent).not.toContain(word)
  })

  it('a recorded unload weighing with no difference shows no warning and offers no second record', async () => {
    installFetch({
      onDetail: () => json(cancelled(unloadWeighingRecord({ firstWeighingKg: '15004.5', grossKg: '15004.5', netKg: '4.5', differenceKg: '0',
        differenceWarning: false }), '6')),
    })
    renderLogisticsApp(path)
    expect((await screen.findByTestId('logistics-unload-weighing-difference')).textContent).toBe('0')
    expect(screen.getByTestId('logistics-unload-weighing-tare').textContent).toBe('15000')
    expect(screen.getByTestId('logistics-unload-weighing-gross').textContent).toBe('15004.5')
    expect(screen.queryByTestId('logistics-unload-weighing-warning')).toBeNull()
    expect(screen.queryByTestId('logistics-unload-missing')).toBeNull()
    expect(screen.queryByTestId('logistics-unload-weighing-form')).toBeNull()
  })

  it('a second record is refused and the detail is re-read; a direction contradiction is explained on the field', async () => {
    let reads = 0
    let sends = 0
    installFetch({
      onDetail: () => json(++reads === 1 ? cancelled() : cancelled(unloadWeighingRecord(), '6')),
      onUnloadWeighing: () =>
        ++sends === 1
          ? json({ status: 422, title: 'Unprocessable', code: 'logistics_weighing_direction_inconsistent', correlationId: 'c',
            errors: [{ field: 'secondWeighingKg', code: 'logistics_weighing_direction_inconsistent' }] }, 422)
          : json({ status: 409, title: 'Conflict', code: 'logistics_unload_weighing_already_recorded', correlationId: 'c', errors: [] }, 409),
    })
    renderLogisticsApp(path)
    await userEvent.selectOptions(await screen.findByLabelText(T.direction), 'TareFirst')
    await userEvent.type(screen.getByLabelText(T.first), '15004')
    await userEvent.type(screen.getByLabelText(T.second), '15000')
    await userEvent.click(screen.getByTestId('logistics-unload-weighing-button'))
    await screen.findByText(T.invalidDirection)
    expect(screen.getByLabelText(T.second).getAttribute('aria-invalid')).toBe('true')

    await userEvent.selectOptions(screen.getByLabelText(T.direction), 'GrossFirst')
    await userEvent.click(screen.getByTestId('logistics-unload-weighing-button'))
    await waitFor(() => expect(screen.getByTestId('logistics-unload-weighing-notice').textContent).toBe(
      `${INSTRUCTION_DETAIL_TEXT.command.conflict} ${PROBLEM_CODE_TEXT['logistics_unload_weighing_already_recorded' as keyof typeof PROBLEM_CODE_TEXT]}`))
    // The refusal says the record moved: the detail is re-read and shows the unload weighing that exists.
    await waitFor(() => expect(screen.getByTestId('logistics-unload-weighing-net').textContent).toBe('4'))
    expect(screen.queryByTestId('logistics-unload-weighing-button')).toBeNull()
  })

  it('a live instruction and one cancelled before anything was loaded offer no unload weighing; an unreadable quantity is not sent', async () => {
    installFetch({ onDetail: () => json(instructionDetail({ status: 'Issued', version: '4' }, [loadedLine])) })
    renderLogisticsApp(path)
    await screen.findByTestId('logistics-instruction-id')
    expect(screen.queryByTestId('logistics-unload-weighing')).toBeNull()
    expect(screen.queryByTestId('logistics-unload-weighing-form')).toBeNull()
    cleanup()
    vi.unstubAllGlobals()

    installFetch({ onDetail: () => json(instructionDetail({ ...CANCELLED_INSTRUCTION, version: '3' })) })
    renderLogisticsApp(path)
    await screen.findByTestId('logistics-instruction-id')
    expect(screen.getByTestId('logistics-unload-weighing-none')).toBeTruthy()
    expect(screen.queryByTestId('logistics-unload-missing')).toBeNull()
    expect(screen.queryByTestId('logistics-unload-weighing-form')).toBeNull()
    cleanup()
    vi.unstubAllGlobals()

    const harness = installFetch({ onDetail: () => json(cancelled()) })
    renderLogisticsApp(path)
    await userEvent.type(await screen.findByLabelText(T.first), '15.004')
    await userEvent.type(screen.getByLabelText(T.second), '15000')
    await userEvent.click(screen.getByTestId('logistics-unload-weighing-button'))
    await screen.findByText(INSTRUCTION_DETAIL_TEXT.command.validation)
    expect(harness.logisticsCalls().filter((call) => call.method === 'POST')).toHaveLength(0)
  })
})
