/**
 * AT-053 on L-02-DETAIL in the real shell: the weighbridge record typed as the person reads the ticket, sent once with
 * one weighing id kept across a lost-reply retry, the backend's net and signed difference shown after the re-read (and
 * after reopening the screen) with no verdict word, a second record refused and re-read, a direction contradiction
 * explained on the field, a Draft offering no form. Component evidence, NOT_ENV.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { INSTRUCTION_DETAIL_TEXT, PROBLEM_CODE_TEXT, WEIGHING_TEXT as T } from './logisticsText'
import {
  FORBIDDEN_MONEY_WORDS,
  INSTRUCTION,
  deferred,
  installFetch,
  instructionDetail,
  instructionLine,
  json,
  mutation,
  renderLogisticsApp,
  weighingRecord,
  type RecordedCall,
} from './testHarness'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const path = `/lojistik/yukleme-talimatlari/${INSTRUCTION}`
const loadedLine = instructionLine({ loaded: '4.5', remainingToLoad: '0', loadedNotShipped: '4.5' })
const issued = (weighing: Record<string, unknown> | null = null, version = '4') =>
  instructionDetail({ status: 'Issued', version }, [loadedLine], [], weighing)

function weighingAnswer(call: RecordedCall, isReplay = false): Response {
  const body = JSON.parse(call.body ?? '{}') as { weighingId: string; direction: string; firstWeighingKg: string; secondWeighingKg: string }
  const record = weighingRecord({ weighingId: body.weighingId, direction: body.direction, firstWeighingKg: body.firstWeighingKg,
    secondWeighingKg: body.secondWeighingKg, tareKg: body.firstWeighingKg, grossKg: body.secondWeighingKg })
  return json(mutation(issued(record, '5'), isReplay), 200, isReplay ? { 'Idempotency-Replayed': 'true' } : {})
}

const VERDICT_WORDS = ['tolerans dışı', 'Tolerans dışı', 'kabul edildi', 'reddedildi', 'uygun değil', 'Uygun']

describe('AT-053 weighbridge record on L-02-DETAIL', () => {
  it('records the two weighings as typed, keeps one weighing id across a lost reply, and shows the backend facts after the re-read', async () => {
    const first = deferred<Response>()
    let weighings = 0
    let reads = 0
    const harness = installFetch({
      onDetail: () => json(++reads === 1 ? issued() : issued(weighingRecord(), '5')),
      onWeighing: (call) => (++weighings === 1 ? first.promise : weighingAnswer(call, true)),
    })
    renderLogisticsApp(path)
    expect((await screen.findByTestId('logistics-weighing-none')).textContent).toBe(T.none)
    await userEvent.type(screen.getByLabelText(T.first), '15000')
    await userEvent.type(screen.getByLabelText(T.second), '15004,3')
    await userEvent.click(screen.getByTestId('logistics-weighing-button'))
    await screen.findByText(INSTRUCTION_DETAIL_TEXT.command.submitting)
    first.reject(new TypeError('Failed to fetch'))
    await screen.findByText(INSTRUCTION_DETAIL_TEXT.command.unknown)
    await userEvent.click(screen.getByRole('button', { name: INSTRUCTION_DETAIL_TEXT.command.retry }))
    await waitFor(() => expect(screen.getByTestId('logistics-weighing-notice').textContent).toBe(INSTRUCTION_DETAIL_TEXT.command.replayed))

    const posts = harness.logisticsCalls().filter((call) => call.method === 'POST')
    expect(posts).toHaveLength(2)
    expect(posts[0].idempotencyKey).toBe(posts[1].idempotencyKey)
    expect(posts[0].body).toBe(posts[1].body)
    expect(posts[0].url).toBe(`/api/v1/logistics/loading-instructions/${INSTRUCTION}/weighings`)
    const sent = JSON.parse(posts[0].body ?? '{}') as Record<string, unknown>
    // The client sends only what the person read off the ticket: no net, difference, tolerance, operator or time.
    expect(Object.keys(sent).sort()).toEqual(['direction', 'expectedVersion', 'firstWeighingKg', 'secondWeighingKg', 'weighingId'])
    expect(sent).toMatchObject({ expectedVersion: '4', direction: 'TareFirst', firstWeighingKg: '15000', secondWeighingKg: '15004.3' })

    // The re-read shows the backend's record; the difference keeps its sign and no verdict is spoken.
    await waitFor(() => expect(screen.getByTestId('logistics-weighing-difference').textContent).toBe('-0.2'))
    expect(screen.getByTestId('logistics-weighing-net').textContent).toBe('4.3')
    expect(screen.getByTestId('logistics-weighing-loaded').textContent).toBe('4.5')
    expect(screen.getByTestId('logistics-weighing-direction').textContent).toBe(T.directionShort.TareFirst)
    expect(screen.queryByTestId('logistics-weighing-button')).toBeNull()
    for (const word of [...VERDICT_WORDS, ...FORBIDDEN_MONEY_WORDS]) expect(document.body.textContent).not.toContain(word)
  })

  it('reopening an instruction that was weighed shows the record to a reader and offers no second record', async () => {
    installFetch({ onDetail: () => json(issued(weighingRecord({ direction: 'GrossFirst', firstWeighingKg: '15004.3', secondWeighingKg: '15000',
      tareKg: '15000', grossKg: '15004.3', differenceKg: '0' }), '5')) })
    renderLogisticsApp(path)
    expect((await screen.findByTestId('logistics-weighing-difference')).textContent).toBe('0')
    expect(screen.getByTestId('logistics-weighing-tare').textContent).toBe('15000')
    expect(screen.getByTestId('logistics-weighing-gross').textContent).toBe('15004.3')
    expect(screen.queryByTestId('logistics-weighing-form')).toBeNull()
  })

  it('a second record is refused and the detail is re-read; a direction contradiction is explained on the field', async () => {
    let reads = 0
    let weighings = 0
    installFetch({
      onDetail: () => json(++reads === 1 ? issued() : issued(weighingRecord(), '5')),
      onWeighing: () =>
        ++weighings === 1
          ? json({ status: 422, title: 'Unprocessable', code: 'logistics_weighing_direction_inconsistent', correlationId: 'c',
            errors: [{ field: 'secondWeighingKg', code: 'logistics_weighing_direction_inconsistent' }] }, 422)
          : json({ status: 409, title: 'Conflict', code: 'logistics_weighing_already_recorded', correlationId: 'c', errors: [] }, 409),
    })
    renderLogisticsApp(path)
    await userEvent.selectOptions(await screen.findByLabelText(T.direction), 'GrossFirst')
    await userEvent.type(screen.getByLabelText(T.first), '15000')
    await userEvent.type(screen.getByLabelText(T.second), '15004')
    await userEvent.click(screen.getByTestId('logistics-weighing-button'))
    await screen.findByText(T.invalidDirection)
    expect(screen.getByLabelText(T.second).getAttribute('aria-invalid')).toBe('true')
    expect(screen.getByTestId('logistics-weighing-notice').textContent).toContain(
      PROBLEM_CODE_TEXT['logistics_weighing_direction_inconsistent' as keyof typeof PROBLEM_CODE_TEXT])

    await userEvent.selectOptions(screen.getByLabelText(T.direction), 'TareFirst')
    await userEvent.click(screen.getByTestId('logistics-weighing-button'))
    await waitFor(() => expect(screen.getByTestId('logistics-weighing-notice').textContent).toBe(
      `${INSTRUCTION_DETAIL_TEXT.command.conflict} ${PROBLEM_CODE_TEXT['logistics_weighing_already_recorded' as keyof typeof PROBLEM_CODE_TEXT]}`))
    // The refusal says the record moved: the detail is re-read and shows the record that exists.
    await waitFor(() => expect(screen.getByTestId('logistics-weighing-net').textContent).toBe('4.3'))
  })

  it('a Draft offers no weighing form, and a quantity the client cannot read is not sent', async () => {
    installFetch({ onDetail: () => json(instructionDetail()) })
    renderLogisticsApp(path)
    expect((await screen.findByTestId('logistics-weighing-issued-only')).textContent).toBe(T.issuedOnly)
    expect(screen.queryByTestId('logistics-weighing-button')).toBeNull()
    cleanup()
    vi.unstubAllGlobals()

    const harness = installFetch({ onDetail: () => json(issued()) })
    renderLogisticsApp(path)
    await userEvent.type(await screen.findByLabelText(T.first), '15.000')
    await userEvent.type(screen.getByLabelText(T.second), '16000')
    await userEvent.click(screen.getByTestId('logistics-weighing-button'))
    await screen.findByText(INSTRUCTION_DETAIL_TEXT.command.validation)
    expect(harness.logisticsCalls().filter((call) => call.method === 'POST')).toHaveLength(0)
  })
})
