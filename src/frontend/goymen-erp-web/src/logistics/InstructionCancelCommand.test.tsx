/**
 * F-020 on L-02-DETAIL in the real shell: "Talimatı iptal et" with a required reason sent trimmed under one Idempotency-Key
 * (kept across a lost-reply retry), the re-read showing the instruction Cancelled with its kept weighing marked cancelled;
 * a recorded shipment refusing the cancel (409 `logistics_instruction_shipped`, re-read) and the screen not offering it once a
 * shipment shows; a code-less 403 shown as "no permission". Component evidence, NOT_ENV.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import {
  INSTRUCTION_CANCEL_TEXT as T,
  INSTRUCTION_DETAIL_TEXT,
  PROBLEM_CODE_TEXT,
  WEIGHING_TEXT,
} from './logisticsText'
import { resetPendingInstructionAttempts } from './pendingInstructionAttempts'
import {
  CANCELLED_INSTRUCTION,
  FORBIDDEN_MONEY_WORDS,
  INSTRUCTION,
  LINE,
  deferred,
  installFetch,
  instructionDetail,
  instructionLine,
  json,
  mutation,
  renderLogisticsApp,
  weighingRecord,
} from './testHarness'

afterEach(() => {
  cleanup()
  resetPendingInstructionAttempts()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const path = `/lojistik/yukleme-talimatlari/${INSTRUCTION}`
const CANCEL_URL = `/api/v1/logistics/loading-instructions/${INSTRUCTION}/cancel`
const REASON = 'Araç gelmedi'
const loadedShare = { shareId: '00000000-0000-4000-8000-000000000071', lineId: LINE, kind: 'Loaded', quantity: '4.5',
  measurement: 'Measured', sourceShipmentId: null, recordedAtUtc: '2026-09-19T09:10:00Z', recordedBy: 'SENTETIK-operator' }
const shippedShare = { shareId: '00000000-0000-4000-8000-000000000072', lineId: LINE, kind: 'Shipped', quantity: '2',
  measurement: null, sourceShipmentId: null, recordedAtUtc: '2026-09-19T09:30:00Z', recordedBy: 'SENTETIK-operator' }
const loadedLine = instructionLine({ loaded: '4.5', remainingToLoad: '0', loadedNotShipped: '4.5' })
const shippedLine = instructionLine({ loaded: '4.5', remainingToLoad: '0', shipped: '2', loadedNotShipped: '2.5', shippedNotDelivered: '2' })

/** Issued, loaded and weighed; nothing shipped yet: cancellable. */
const issued = () => instructionDetail({ status: 'Issued', version: '4' }, [loadedLine], [loadedShare], weighingRecord())
/** The same instruction after the cancel: its share and weighing are kept. */
const cancelled = (reason = REASON) =>
  instructionDetail({ ...CANCELLED_INSTRUCTION, cancelReason: reason, version: '5' }, [loadedLine], [loadedShare], weighingRecord())
/** A shipment was recorded meanwhile: no longer cancellable. */
const shipped = () => instructionDetail({ status: 'Issued', version: '5' }, [shippedLine], [loadedShare, shippedShare], weighingRecord())

async function openAndType(reason: string) {
  await userEvent.click(await screen.findByTestId('logistics-instruction-cancel-open'))
  const input = screen.getByLabelText(T.reasonLabel)
  await userEvent.clear(input)
  if (reason.length > 0) await userEvent.type(input, reason)
}

describe('F-020 instruction cancellation on L-02-DETAIL', () => {
  it('requires a reason, sends it trimmed with one key, and the re-read shows the cancelled instruction with its weighing marked', async () => {
    let reads = 0
    const harness = installFetch({
      onDetail: () => json(++reads === 1 ? issued() : cancelled()),
      onCancel: () => json(mutation(cancelled())),
    })
    renderLogisticsApp(path)
    const section = await screen.findByTestId('logistics-instruction-cancel')
    expect(within(section).getByText(T.note)).toBeTruthy()
    expect(within(section).getByTestId('logistics-instruction-cancel-undecided').textContent).toBe(T.undecided)
    expect(screen.queryByTestId('logistics-weighing-cancelled')).toBeNull()

    // An empty (or blank) reason is refused on the field and nothing is sent.
    await openAndType('   ')
    await userEvent.click(screen.getByTestId('logistics-instruction-cancel-confirm'))
    await screen.findByText(T.reasonRequired)
    expect(screen.getByLabelText(T.reasonLabel).getAttribute('aria-invalid')).toBe('true')
    expect(harness.logisticsCalls().filter((call) => call.method === 'POST')).toHaveLength(0)

    await userEvent.type(screen.getByLabelText(T.reasonLabel), `  ${REASON}  `)
    await userEvent.click(screen.getByTestId('logistics-instruction-cancel-confirm'))
    await waitFor(() => expect(screen.getByTestId('logistics-instruction-cancel-notice').textContent).toBe(INSTRUCTION_DETAIL_TEXT.command.completed))

    const posts = harness.logisticsCalls().filter((call) => call.method === 'POST')
    expect(posts).toHaveLength(1)
    expect(posts[0].url).toBe(CANCEL_URL)
    expect(posts[0].idempotencyKey).toMatch(/^[0-9a-f]{32}$/)
    // Only the version and the trimmed reason travel; the released quantity is the server's.
    expect(JSON.parse(posts[0].body ?? '{}')).toEqual({ expectedVersion: '4', reason: REASON })

    // The re-read: Cancelled, the stored reason, the share and the weighing kept, the weighing marked cancelled, no command.
    await waitFor(() => expect(screen.getByTestId('logistics-instruction-status').textContent).toBe('İptal edildi'))
    expect(screen.getByTestId('logistics-instruction-cancel-reason').textContent).toBe(REASON)
    expect(screen.getByTestId('logistics-share-00000000-0000-4000-8000-000000000071')).toBeTruthy()
    expect(screen.getByTestId('logistics-weighing-net').textContent).toBe('4.3')
    expect(screen.getByTestId('logistics-weighing-cancelled').textContent).toContain(WEIGHING_TEXT.cancelledMark)
    expect(screen.getByTestId('logistics-weighing-cancelled').textContent).toContain(WEIGHING_TEXT.cancelledNote)
    // The notice of the command that cancelled it survives the re-read; nothing more is offered.
    expect(screen.getByTestId('logistics-instruction-cancel-notice').textContent).toBe(INSTRUCTION_DETAIL_TEXT.command.completed)
    expect(screen.queryByTestId('logistics-instruction-cancel-open')).toBeNull()
    expect(screen.queryByTestId('logistics-instruction-cancel-form')).toBeNull()
    const record = screen.getByRole('article', { name: INSTRUCTION_DETAIL_TEXT.heading })
    expect(record.querySelectorAll('button, form, input, select, textarea')).toHaveLength(0)
    for (const word of FORBIDDEN_MONEY_WORDS) expect(document.body.textContent).not.toContain(word)
  })

  it('a lost reply is retried with the same key and body, the form frozen meanwhile', async () => {
    const first = deferred<Response>()
    let sends = 0
    let reads = 0
    const harness = installFetch({
      onDetail: () => json(++reads === 1 ? issued() : cancelled()),
      onCancel: () => (++sends === 1 ? first.promise : json(mutation(cancelled(), true), 200, { 'Idempotency-Replayed': 'true' })),
    })
    renderLogisticsApp(path)
    await openAndType(REASON)
    await userEvent.click(screen.getByTestId('logistics-instruction-cancel-confirm'))
    await screen.findByText(INSTRUCTION_DETAIL_TEXT.command.submitting)
    first.reject(new TypeError('Failed to fetch'))
    await screen.findByText(INSTRUCTION_DETAIL_TEXT.command.unknown)
    // While the outcome is unknown the reason cannot be changed into a second, different cancel.
    expect((screen.getByLabelText(T.reasonLabel) as HTMLTextAreaElement).disabled).toBe(true)
    await userEvent.click(screen.getByRole('button', { name: INSTRUCTION_DETAIL_TEXT.command.retry }))
    await waitFor(() => expect(screen.getByTestId('logistics-instruction-cancel-notice').textContent).toBe(INSTRUCTION_DETAIL_TEXT.command.replayed))

    const posts = harness.logisticsCalls().filter((call) => call.method === 'POST')
    expect(posts).toHaveLength(2)
    expect(posts[0].idempotencyKey).toBe(posts[1].idempotencyKey)
    expect(posts[0].body).toBe(posts[1].body)
    await waitFor(() => expect(screen.getByTestId('logistics-instruction-status').textContent).toBe('İptal edildi'))
  })

  it('a recorded shipment refuses the cancel (409, re-read) and the screen then says why instead of offering it', async () => {
    let reads = 0
    installFetch({
      onDetail: () => json(++reads === 1 ? issued() : shipped()),
      onCancel: () => json({ status: 409, title: 'Conflict', code: 'logistics_instruction_shipped', correlationId: 'c', errors: [] }, 409),
    })
    renderLogisticsApp(path)
    await openAndType(REASON)
    await userEvent.click(screen.getByTestId('logistics-instruction-cancel-confirm'))
    await waitFor(() => expect(screen.getByTestId('logistics-instruction-cancel-notice').textContent).toBe(
      `${INSTRUCTION_DETAIL_TEXT.command.conflict} ${PROBLEM_CODE_TEXT['logistics_instruction_shipped' as keyof typeof PROBLEM_CODE_TEXT]}`))
    // The refusal says the record moved: the detail is re-read and shows the shipment that exists.
    await waitFor(() => expect(screen.getByTestId('logistics-instruction-cancel-shipped').textContent).toBe(T.shipped))
    expect(reads).toBeGreaterThanOrEqual(2)
    expect(screen.getByTestId('logistics-instruction-status').textContent).not.toBe('İptal edildi')
    expect(screen.queryByTestId('logistics-instruction-cancel-open')).toBeNull()
    expect(screen.queryByTestId('logistics-instruction-cancel-form')).toBeNull()
  })

  it('a code-less 403 is shown as "no permission" and nothing is recorded', async () => {
    let reads = 0
    installFetch({
      onDetail: () => {
        reads += 1
        return json(issued())
      },
      onCancel: () => json({ status: 403, title: 'Forbidden' }, 403),
    })
    renderLogisticsApp(path)
    await openAndType(REASON)
    await userEvent.click(screen.getByTestId('logistics-instruction-cancel-confirm'))
    await waitFor(() => expect(screen.getByTestId('logistics-instruction-cancel-notice').textContent).toBe(INSTRUCTION_DETAIL_TEXT.command.forbidden))
    expect(reads).toBe(1)
    expect(screen.queryByTestId('logistics-weighing-cancelled')).toBeNull()
    expect(screen.getByTestId('logistics-instruction-status').textContent).not.toBe('İptal edildi')
  })

  it('a Draft offers the cancel; an instruction with a recorded shipment offers none and says why', async () => {
    installFetch({ onDetail: () => json(instructionDetail()) })
    renderLogisticsApp(path)
    expect((await screen.findByTestId('logistics-instruction-cancel-open')).textContent).toBe(T.cancel)
    cleanup()
    vi.unstubAllGlobals()

    const harness = installFetch({ onDetail: () => json(shipped()) })
    renderLogisticsApp(path)
    expect((await screen.findByTestId('logistics-instruction-cancel-shipped')).textContent).toBe(T.shipped)
    expect(screen.queryByTestId('logistics-instruction-cancel-open')).toBeNull()
    expect(screen.queryByTestId('logistics-instruction-cancel-form')).toBeNull()
    expect(harness.logisticsCalls().every((call) => call.method === 'GET')).toBe(true)
    for (const word of FORBIDDEN_MONEY_WORDS) expect(document.body.textContent).not.toContain(word)
  })
})
