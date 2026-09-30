/**
 * AT-055 on L-02-DETAIL in the real shell: a dispatch-note DRAFT is made from a definite shipment (lines = that shipment's
 * shipped quantity), sent once with one draft id kept across a lost-reply retry, shown after the re-read and after
 * reopening the screen, a shipment already on a draft cannot be picked again, a second link refused by the server is
 * explained and re-read, a Draft instruction offers no form, and the decoder refuses an answer linking a shipment twice.
 * Component evidence, NOT_ENV.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { decodeInstructionDetail } from './contract'
import { resetPendingInstructionAttempts } from './pendingInstructionAttempts'
import { DISPATCH_NOTE_TEXT as T, INSTRUCTION_DETAIL_TEXT, PROBLEM_CODE_TEXT } from './logisticsText'
import {
  DRAFT,
  DRAFT_LINE,
  FORBIDDEN_MONEY_WORDS,
  INSTRUCTION,
  KG,
  LINE,
  PRODUCT,
  deferred,
  installFetch,
  instructionDetail,
  instructionLine,
  json,
  mutation,
  renderLogisticsApp,
  type RecordedCall,
} from './testHarness'

afterEach(() => {
  cleanup()
  resetPendingInstructionAttempts()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const path = `/lojistik/yukleme-talimatlari/${INSTRUCTION}`
const SHIP_1 = '51515151-5151-4151-8151-515151515151'
const SHIP_2 = '52525252-5252-4252-8252-525252525252'
const NOTE = '6f6f6f6f-6f6f-4f6f-8f6f-6f6f6f6f6f6f'
const shippedLine = instructionLine({ loaded: '4.5', shipped: '4.5', remainingToLoad: '0', loadedNotShipped: '0', shippedNotDelivered: '4.5' })
const shares = [
  { shareId: '50505050-5050-4050-8050-505050505050', lineId: LINE, kind: 'Loaded', quantity: '4.5', measurement: 'Measured', sourceShipmentId: null, recordedAtUtc: '2026-09-19T09:10:00Z' },
  { shareId: SHIP_1, lineId: LINE, kind: 'Shipped', quantity: '3', measurement: null, sourceShipmentId: null, recordedAtUtc: '2026-09-19T09:20:00Z' },
  { shareId: SHIP_2, lineId: LINE, kind: 'Shipped', quantity: '1.5', measurement: null, sourceShipmentId: null, recordedAtUtc: '2026-09-19T09:30:00Z' },
]

const note = (id: string, shipmentIds: readonly string[], state: Record<string, unknown> = {}) => ({
  dispatchNoteId: id,
  status: 'Draft',
  number: null,
  createdBy: 'SENTETIK-sevkiyat',
  createdAtUtc: '2026-09-19T09:40:00Z',
  approvedBy: null,
  approvedAtUtc: null,
  cancelledBy: null,
  cancelledAtUtc: null,
  cancelReason: null,
  eDispatchNumber: null,
  ...state,
  lines: shipmentIds.map((shipmentId, index) => ({
    lineNo: index + 1,
    shipmentId,
    instructionLineId: LINE,
    sourceOrderId: DRAFT,
    sourceOrderLineId: DRAFT_LINE,
    productId: PRODUCT,
    unitId: KG,
    quantity: shipmentId === SHIP_1 ? '3' : '1.5',
  })),
})

const issued = (notes: readonly Record<string, unknown>[] = [], version = '6') =>
  instructionDetail({ status: 'Issued', version }, [shippedLine], shares, null, notes)

function draftAnswer(call: RecordedCall, isReplay = false): Response {
  const body = JSON.parse(call.body ?? '{}') as { dispatchNoteId: string; shipmentIds: string[] }
  return json(mutation(issued([note(body.dispatchNoteId, body.shipmentIds)], '7'), isReplay), 200, isReplay ? { 'Idempotency-Replayed': 'true' } : {})
}

describe('AT-055 dispatch-note draft on L-02-DETAIL', () => {
  it('drafts from one shipment with its shipped quantity, keeps one draft id across a lost reply, and shows the draft after the re-read', async () => {
    const first = deferred<Response>()
    let drafts = 0
    let reads = 0
    let lastDraft: Record<string, unknown> | null = null
    const harness = installFetch({
      onDetail: () => json(++reads === 1 || lastDraft === null ? issued() : issued([lastDraft], '7')),
      onDispatchNote: (call) => {
        const body = JSON.parse(call.body ?? '{}') as { dispatchNoteId: string; shipmentIds: string[] }
        lastDraft = note(body.dispatchNoteId, body.shipmentIds)
        return ++drafts === 1 ? first.promise : draftAnswer(call, true)
      },
    })
    renderLogisticsApp(path)
    expect((await screen.findByTestId('logistics-dispatch-notes-none')).textContent).toBe(T.none)
    expect(screen.getByTestId('logistics-dispatch-notes-undecided').textContent).toBe(T.undecided)
    await userEvent.click(within(screen.getByTestId(`logistics-dispatch-note-shipment-${SHIP_1}`)).getByRole('checkbox'))
    await userEvent.click(screen.getByTestId('logistics-dispatch-note-button'))
    await screen.findByText(INSTRUCTION_DETAIL_TEXT.command.submitting)
    first.reject(new TypeError('Failed to fetch'))
    await screen.findByText(INSTRUCTION_DETAIL_TEXT.command.unknown)
    await userEvent.click(screen.getByRole('button', { name: INSTRUCTION_DETAIL_TEXT.command.retry }))
    await waitFor(() => expect(screen.getByTestId('logistics-dispatch-note-notice').textContent).toBe(INSTRUCTION_DETAIL_TEXT.command.replayed))

    const posts = harness.logisticsCalls().filter((call) => call.method === 'POST')
    expect(posts).toHaveLength(2)
    expect(posts[0].idempotencyKey).toBe(posts[1].idempotencyKey)
    expect(posts[0].body).toBe(posts[1].body)
    expect(posts[0].url).toBe(`/api/v1/logistics/loading-instructions/${INSTRUCTION}/dispatch-notes`)
    const sent = JSON.parse(posts[0].body ?? '{}') as Record<string, unknown>
    // Only the shipments: no quantity, price, number, date or customer.
    expect(Object.keys(sent).sort()).toEqual(['dispatchNoteId', 'expectedVersion', 'shipmentIds'])
    expect(sent).toMatchObject({ expectedVersion: '6', shipmentIds: [SHIP_1] })

    // The re-read shows the draft: one line, the shipment's shipped quantity; the shipment is now linked and cannot be picked.
    const draftId = sent.dispatchNoteId as string
    const block = await screen.findByTestId(`logistics-dispatch-note-${draftId}`)
    expect(block.id).toBe(`irsaliye-${draftId}`)
    expect(within(block).getByTestId('logistics-dispatch-note-status').textContent).toBe(T.draft)
    expect(within(block).getByTestId('logistics-dispatch-note-line-1-quantity').textContent).toBe('3')
    expect(within(block).queryByTestId('logistics-dispatch-note-line-2')).toBeNull()
    const linked = within(screen.getByTestId(`logistics-dispatch-note-shipment-${SHIP_1}`)).getByRole('checkbox') as HTMLInputElement
    expect(linked.disabled).toBe(true)
    expect(screen.getByTestId(`logistics-dispatch-note-shipment-${SHIP_1}`).textContent).toContain(T.linked)
    expect((within(screen.getByTestId(`logistics-dispatch-note-shipment-${SHIP_2}`)).getByRole('checkbox') as HTMLInputElement).disabled).toBe(false)
    for (const word of FORBIDDEN_MONEY_WORDS) expect(document.body.textContent).not.toContain(word)
  })

  it('reopening shows the draft with its lines to a reader; with every shipment linked nothing is offered to pick', async () => {
    installFetch({ onDetail: () => json(issued([note(NOTE, [SHIP_1, SHIP_2])], '7')) })
    renderLogisticsApp(`${path}#irsaliye-${NOTE}`)
    const block = await screen.findByTestId(`logistics-dispatch-note-${NOTE}`)
    expect(within(block).getByTestId('logistics-dispatch-note-line-1-quantity').textContent).toBe('3')
    expect(within(block).getByTestId('logistics-dispatch-note-line-2-quantity').textContent).toBe('1.5')
    expect(within(block).getByTestId('logistics-dispatch-note-reference').textContent).toBe(NOTE)
    expect(screen.getByTestId('logistics-dispatch-note-no-free').textContent).toBe(T.noFreeShipment)
    expect((screen.getByTestId('logistics-dispatch-note-button') as HTMLButtonElement).disabled).toBe(true)
  })

  it('a second link refused by the server is explained and the detail is re-read', async () => {
    let reads = 0
    installFetch({
      // The screen was opened before another user drafted SHIP_1; the server holds the single link.
      onDetail: () => json(++reads === 1 ? issued() : issued([note(NOTE, [SHIP_1])], '7')),
      onDispatchNote: () => json({ status: 409, title: 'Conflict', code: 'logistics_shipment_already_dispatched', correlationId: 'c',
        errors: [{ field: 'shipmentIds', code: 'logistics_shipment_already_dispatched' }] }, 409),
    })
    renderLogisticsApp(path)
    await userEvent.click(within(await screen.findByTestId(`logistics-dispatch-note-shipment-${SHIP_1}`)).getByRole('checkbox'))
    await userEvent.click(screen.getByTestId('logistics-dispatch-note-button'))
    await waitFor(() => expect(screen.getByTestId('logistics-dispatch-note-notice').textContent).toBe(
      `${INSTRUCTION_DETAIL_TEXT.command.conflict} ${PROBLEM_CODE_TEXT['logistics_shipment_already_dispatched' as keyof typeof PROBLEM_CODE_TEXT]}`))
    await screen.findByTestId(`logistics-dispatch-note-${NOTE}`)
    expect((within(screen.getByTestId(`logistics-dispatch-note-shipment-${SHIP_1}`)).getByRole('checkbox') as HTMLInputElement).disabled).toBe(true)
  })

  it('a Draft instruction offers no form, and nothing is sent without a chosen shipment', async () => {
    installFetch({ onDetail: () => json(instructionDetail()) })
    renderLogisticsApp(path)
    expect((await screen.findByTestId('logistics-dispatch-note-issued-only')).textContent).toBe(T.issuedOnly)
    cleanup()
    vi.unstubAllGlobals()

    const harness = installFetch({ onDetail: () => json(issued()) })
    renderLogisticsApp(path)
    await userEvent.click(await screen.findByTestId('logistics-dispatch-note-button'))
    await screen.findByText(T.chooseOne)
    expect(harness.logisticsCalls().filter((call) => call.method === 'POST')).toHaveLength(0)
  })

  it('the decoder refuses a detail that links a shipment twice, names a non-shipment, or changes its quantity', () => {
    expect(decodeInstructionDetail(issued([note(NOTE, [SHIP_1])]), INSTRUCTION)?.dispatchNotes).toHaveLength(1)
    const twice = issued([note(NOTE, [SHIP_1]), note('70707070-7070-4070-8070-707070707070', [SHIP_1])])
    expect(decodeInstructionDetail(twice, INSTRUCTION)).toBeNull()
    expect(decodeInstructionDetail(issued([note(NOTE, [shares[0].shareId])]), INSTRUCTION)).toBeNull()
    const changed = note(NOTE, [SHIP_1])
    expect(decodeInstructionDetail(issued([{ ...changed, lines: [{ ...changed.lines[0], quantity: '2.9' }] }]), INSTRUCTION)).toBeNull()
    expect(decodeInstructionDetail(issued([{ ...changed, status: 'Final' }]), INSTRUCTION)).toBeNull()
    expect(decodeInstructionDetail(issued([{ ...changed, number: 'IRS-1' }]), INSTRUCTION)).toBeNull()
    const { dispatchNotes: _omitted, ...withoutMember } = issued()
    expect(decodeInstructionDetail(withoutMember, INSTRUCTION)).toBeNull()
  })
})

const APPROVED = { status: 'Approved', number: 'IRS-000001', approvedBy: 'SENTETIK-onay', approvedAtUtc: '2026-09-19T09:50:00Z' }
const cancelledFrom = (base: Record<string, unknown>, reason: string) => ({
  ...base, status: 'Cancelled', cancelledBy: 'SENTETIK-iptal', cancelledAtUtc: '2026-09-19T10:00:00Z', cancelReason: reason,
})

describe('AT-055 dispatch-note approval, cancellation and e-İrsaliye number on L-02-DETAIL', () => {
  it('approving a draft sends only the version under its own key and shows the server-assigned number after the re-read', async () => {
    let state: Record<string, unknown> = {}
    const lifecycle: string[] = []
    const harness = installFetch({
      onDetail: () => json(issued([note(NOTE, [SHIP_1], state)], state.status === undefined ? '7' : '8')),
      onDispatchNoteLifecycle: (kind, id) => {
        lifecycle.push(`${kind}:${id}`)
        state = APPROVED
        return json(mutation(issued([note(NOTE, [SHIP_1], state)], '8')))
      },
    })
    renderLogisticsApp(path)
    const panel = await screen.findByTestId(`logistics-dispatch-note-lifecycle-${NOTE}`)
    await userEvent.click(within(panel).getByTestId('logistics-dispatch-note-approve'))
    await waitFor(() => expect(within(screen.getByTestId(`logistics-dispatch-note-${NOTE}`)).getByTestId('logistics-dispatch-note-number').textContent)
      .toBe('IRS-000001'))
    const block = screen.getByTestId(`logistics-dispatch-note-${NOTE}`)
    expect(within(block).getByTestId('logistics-dispatch-note-status').textContent).toBe(T.approved)
    expect(lifecycle).toEqual([`approve:${NOTE}`])
    const post = harness.logisticsCalls().filter((call) => call.method === 'POST')
    expect(post).toHaveLength(1)
    expect(post[0].url).toBe(`/api/v1/logistics/loading-instructions/${INSTRUCTION}/dispatch-notes/${NOTE}/approve`)
    expect(JSON.parse(post[0].body ?? '{}')).toEqual({ expectedVersion: '7' })
    expect(post[0].idempotencyKey).not.toBeNull()
    // An approved note offers no second approval; it offers cancellation and the e-İrsaliye number.
    const approvedPanel = screen.getByTestId(`logistics-dispatch-note-lifecycle-${NOTE}`)
    await waitFor(() => expect(within(approvedPanel).queryByTestId('logistics-dispatch-note-approve')).toBeNull())
    expect(within(approvedPanel).getByTestId('logistics-dispatch-note-cancel')).toBeTruthy()
    expect(within(approvedPanel).getByTestId('logistics-dispatch-note-e-number-form')).toBeTruthy()
  })

  it('an approve answer without a number is not success (the outcome stays unknown)', async () => {
    installFetch({
      onDetail: () => json(issued([note(NOTE, [SHIP_1])], '7')),
      onDispatchNoteLifecycle: () => json(mutation(issued([note(NOTE, [SHIP_1])], '8'))),
    })
    renderLogisticsApp(path)
    await userEvent.click(within(await screen.findByTestId(`logistics-dispatch-note-lifecycle-${NOTE}`)).getByTestId('logistics-dispatch-note-approve'))
    await screen.findByText(INSTRUCTION_DETAIL_TEXT.command.unknown)
  })

  it('cancelling requires a reason, sends it trimmed, keeps the cancelled note visible and frees its shipment for a new draft', async () => {
    let cancelled = false
    const lifecycle: string[] = []
    const harness = installFetch({
      onDetail: () => json(cancelled
        ? issued([cancelledFrom(note(NOTE, [SHIP_1], APPROVED), 'Yanlış araç')], '9')
        : issued([note(NOTE, [SHIP_1], APPROVED)], '8')),
      onDispatchNoteLifecycle: (kind) => {
        lifecycle.push(kind)
        cancelled = true
        return json(mutation(issued([cancelledFrom(note(NOTE, [SHIP_1], APPROVED), 'Yanlış araç')], '9')))
      },
    })
    renderLogisticsApp(path)
    // The approved note holds SHIP_1: it cannot be picked for another draft.
    expect((within(await screen.findByTestId(`logistics-dispatch-note-shipment-${SHIP_1}`)).getByRole('checkbox') as HTMLInputElement).disabled).toBe(true)
    const panel = screen.getByTestId(`logistics-dispatch-note-lifecycle-${NOTE}`)
    await userEvent.click(within(panel).getByTestId('logistics-dispatch-note-cancel'))
    await userEvent.click(within(panel).getByTestId('logistics-dispatch-note-cancel-confirm'))
    await within(panel).findByText(T.cancelReasonRequired)
    expect(harness.logisticsCalls().filter((call) => call.method === 'POST')).toHaveLength(0)
    await userEvent.type(within(panel).getByTestId('logistics-dispatch-note-cancel-reason-input'), '  Yanlış araç  ')
    await userEvent.click(within(panel).getByTestId('logistics-dispatch-note-cancel-confirm'))

    await waitFor(() => expect(within(screen.getByTestId(`logistics-dispatch-note-${NOTE}`)).getByTestId('logistics-dispatch-note-status').textContent)
      .toBe(T.cancelled))
    const block = screen.getByTestId(`logistics-dispatch-note-${NOTE}`)
    expect(within(block).getByTestId('logistics-dispatch-note-number').textContent).toBe('IRS-000001')
    expect(within(block).getByTestId('logistics-dispatch-note-cancel-reason').textContent).toBe('Yanlış araç')
    expect(lifecycle).toEqual(['cancel'])
    const post = harness.logisticsCalls().filter((call) => call.method === 'POST')
    expect(post).toHaveLength(1)
    expect(post[0].url).toBe(`/api/v1/logistics/loading-instructions/${INSTRUCTION}/dispatch-notes/${NOTE}/cancel`)
    expect(JSON.parse(post[0].body ?? '{}')).toEqual({ expectedVersion: '8', reason: 'Yanlış araç' })
    // Released: SHIP_1 is pickable again (correction = cancel + new draft).
    await waitFor(() =>
      expect((within(screen.getByTestId(`logistics-dispatch-note-shipment-${SHIP_1}`)).getByRole('checkbox') as HTMLInputElement).disabled).toBe(false))
  })

  it('the e-İrsaliye number is validated and uppercased before it is sent, and a number in use is explained', async () => {
    const harness = installFetch({
      onDetail: () => json(issued([note(NOTE, [SHIP_1], APPROVED)], '8')),
      onDispatchNoteLifecycle: () =>
        json({ status: 409, title: 'Conflict', code: 'logistics_e_dispatch_number_in_use', correlationId: 'c',
          errors: [{ field: 'eDispatchNumber', code: 'logistics_e_dispatch_number_in_use' }] }, 409),
    })
    renderLogisticsApp(path)
    const panel = await screen.findByTestId(`logistics-dispatch-note-lifecycle-${NOTE}`)
    const input = within(panel).getByTestId('logistics-dispatch-note-e-number-input')
    await userEvent.type(input, 'GYM12')
    await userEvent.click(within(panel).getByTestId('logistics-dispatch-note-e-number-save'))
    await within(panel).findByText(T.eDispatchNumberInvalid)
    expect(harness.logisticsCalls().filter((call) => call.method === 'POST')).toHaveLength(0)
    await userEvent.clear(input)
    await userEvent.type(input, 'gym2026000000001')
    await userEvent.click(within(panel).getByTestId('logistics-dispatch-note-e-number-save'))
    await waitFor(() => expect(screen.getByTestId('logistics-dispatch-note-lifecycle-notice').textContent).toBe(
      `${INSTRUCTION_DETAIL_TEXT.command.conflict} ${PROBLEM_CODE_TEXT['logistics_e_dispatch_number_in_use' as keyof typeof PROBLEM_CODE_TEXT]}`))
    const post = harness.logisticsCalls().filter((call) => call.method === 'POST')
    expect(post[0].url).toBe(`/api/v1/logistics/loading-instructions/${INSTRUCTION}/dispatch-notes/${NOTE}/e-dispatch-number`)
    expect(JSON.parse(post[0].body ?? '{}')).toEqual({ expectedVersion: '8', eDispatchNumber: 'GYM2026000000001' })
  })

  it('reopening shows the states, numbers and the e-İrsaliye number; a cancelled note offers no command', async () => {
    const OTHER = '70707070-7070-4070-8070-707070707070'
    installFetch({
      onDetail: () => json(issued([
        cancelledFrom(note(NOTE, [SHIP_1], { ...APPROVED, eDispatchNumber: 'GYM2026000000001' }), 'Düzeltme'),
        note(OTHER, [SHIP_1], { ...APPROVED, number: 'IRS-000002' }),
      ], '11')),
    })
    renderLogisticsApp(path)
    const first = await screen.findByTestId(`logistics-dispatch-note-${NOTE}`)
    expect(within(first).getByTestId('logistics-dispatch-note-status').textContent).toBe(T.cancelled)
    expect(within(first).getByTestId('logistics-dispatch-note-e-number').textContent).toBe('GYM2026000000001')
    const second = screen.getByTestId(`logistics-dispatch-note-${OTHER}`)
    expect(within(second).getByTestId('logistics-dispatch-note-number').textContent).toBe('IRS-000002')
    expect(screen.queryByTestId(`logistics-dispatch-note-lifecycle-${NOTE}`)).toBeNull()
    expect(screen.getByTestId(`logistics-dispatch-note-lifecycle-${OTHER}`)).toBeTruthy()
  })

  it('the decoder holds the lifecycle shape: a number only once approved, a reason only when cancelled, one live link, unique numbers', () => {
    const OTHER = '70707070-7070-4070-8070-707070707070'
    expect(decodeInstructionDetail(issued([note(NOTE, [SHIP_1], APPROVED)]), INSTRUCTION)?.dispatchNotes[0].number).toBe('IRS-000001')
    expect(decodeInstructionDetail(issued([note(NOTE, [SHIP_1], { status: 'Approved' })]), INSTRUCTION)).toBeNull()
    expect(decodeInstructionDetail(issued([note(NOTE, [SHIP_1], { status: 'Cancelled' })]), INSTRUCTION)).toBeNull()
    expect(decodeInstructionDetail(issued([note(NOTE, [SHIP_1], { cancelReason: 'x' })]), INSTRUCTION)).toBeNull()
    expect(decodeInstructionDetail(issued([note(NOTE, [SHIP_1], { eDispatchNumber: 'GYM2026000000001' })]), INSTRUCTION)).toBeNull()
    expect(decodeInstructionDetail(issued([note(NOTE, [SHIP_1], { ...APPROVED, eDispatchNumber: 'bad' })]), INSTRUCTION)).toBeNull()
    // A cancelled draft (never approved) has no number; its shipment may be on a new live note.
    const released = issued([cancelledFrom(note(NOTE, [SHIP_1]), 'Hata'), note(OTHER, [SHIP_1])])
    expect(decodeInstructionDetail(released, INSTRUCTION)?.dispatchNotes).toHaveLength(2)
    // Two live notes on one shipment, or one ERP number twice, is not an answer.
    expect(decodeInstructionDetail(issued([note(NOTE, [SHIP_1], APPROVED), note(OTHER, [SHIP_1])]), INSTRUCTION)).toBeNull()
    expect(decodeInstructionDetail(issued([cancelledFrom(note(NOTE, [SHIP_1], APPROVED), 'x'), note(OTHER, [SHIP_1], APPROVED)]), INSTRUCTION)).toBeNull()
  })
})
