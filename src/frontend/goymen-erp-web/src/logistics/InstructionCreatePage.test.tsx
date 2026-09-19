/**
 * L-03 in the real shell: the customer and the lines come only from the APPROVED Sales drafts, the
 * picker shows no money word, the quantity goes out as canonical TEXT, one key per snapshot with the
 * same key on a retry after a lost reply, a replay shown as a replay, the 409 notices, and a closed
 * route without the grant. Component evidence, NOT_ENV.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { LOGISTICS_CAPABILITY } from './capabilities'
import { INSTRUCTION_CREATE_TEXT as T, PROBLEM_CODE_TEXT } from './logisticsText'
import {
  ALL_LOGISTICS,
  DRAFT,
  DRAFT_LINE,
  FORBIDDEN_MONEY_WORDS,
  KG,
  approvedDraftDetail,
  approvedDraftRow,
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
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const salesReads = {
  onSalesList: () => json({ items: [approvedDraftRow()], nextCursor: null }),
  onSalesDetail: () => json(approvedDraftDetail()),
}

/** The create answer that describes the request the harness recorded (line id and quantity text as sent). */
function answering(call: RecordedCall, isReplay = false): Response {
  const body = JSON.parse(call.body ?? '{}') as { lines: { lineId: string; quantity: string }[]; effectiveOn: string }
  const line = instructionLine({ lineId: body.lines[0].lineId, instructed: body.lines[0].quantity, remainingToLoad: body.lines[0].quantity })
  return json(mutation(instructionDetail({ effectiveOn: body.effectiveOn }, [line]), isReplay), 201, {
    Location: '/api/v1/logistics/loading-instructions/cccccccc-cccc-cccc-cccc-cccccccccccc',
    ...(isReplay ? { 'Idempotency-Replayed': 'true' } : {}),
  })
}

async function pickTheLine() {
  await userEvent.selectOptions(await screen.findByLabelText(T.field.customer), `${approvedDraftRow().partyId}|${approvedDraftRow().companyAccountId}`)
  const picker = await screen.findByTestId('logistics-line-picker')
  const row = within(picker).getByTestId('logistics-pick-SO-2026-0001-1')
  await userEvent.click(within(row).getByRole('checkbox'))
  return within(row).getByRole('textbox')
}

describe('L-03 Yeni Yükleme Talimatı', () => {
  it('offers the customer and the line from the approved drafts, shows no money word, and sends the canonical quantity text', async () => {
    const harness = installFetch({ ...salesReads, onCreate: (_, call) => answering(call) })
    renderLogisticsApp('/lojistik/yukleme-talimatlari/yeni')
    const quantity = await pickTheLine()
    for (const word of FORBIDDEN_MONEY_WORDS) expect(document.body.textContent).not.toContain(word)
    expect(screen.getByText('Sentetik bant')).toBeTruthy()
    await userEvent.type(quantity, '4,500')
    await userEvent.click(screen.getByTestId('logistics-create-button'))
    await screen.findByTestId('logistics-created')
    const create = harness.logisticsCalls().find((call) => call.method === 'POST')
    expect(create).toBeDefined()
    const body = JSON.parse(create!.body ?? '{}')
    expect(body.customerPartyId).toBe(approvedDraftRow().partyId)
    expect(body.lines[0]).toMatchObject({ sourceOrderId: DRAFT, sourceOrderLineId: DRAFT_LINE, unitId: KG, quantity: '4.5' })
    expect(body.lines[0].productId).toBeUndefined()
    expect(create!.idempotencyKey).toMatch(/^[0-9a-f]{32}$/)
    expect(create!.body).not.toMatch(/price|unitPrice|currency/)
  })

  it('a lost reply keeps the key: the retry resends the same snapshot under the same key and a replay is shown as a replay', async () => {
    const first = deferred<Response>()
    const harness = installFetch({
      ...salesReads,
      onCreate: (attempt, call) => (attempt === 1 ? first.promise : answering(call, true)),
    })
    renderLogisticsApp('/lojistik/yukleme-talimatlari/yeni')
    const quantity = await pickTheLine()
    await userEvent.type(quantity, '4.5')
    await userEvent.click(screen.getByTestId('logistics-create-button'))
    await screen.findByText(T.attempt.submitting)
    first.reject(new TypeError('Failed to fetch'))
    await screen.findByText(T.attempt.unknown)
    await userEvent.click(screen.getByRole('button', { name: T.attempt.retry }))
    await screen.findByTestId('logistics-created-replay')
    const posts = harness.logisticsCalls().filter((call) => call.method === 'POST')
    expect(posts).toHaveLength(2)
    expect(posts[0].idempotencyKey).toBe(posts[1].idempotencyKey)
    expect(posts[0].body).toBe(posts[1].body)
  })

  it.each([
    ['account_state', 409, `${T.attempt.conflict} ${PROBLEM_CODE_TEXT.account_state}`],
    ['order_line_state', 409, `${T.attempt.conflict} ${PROBLEM_CODE_TEXT.order_line_state}`],
    ['unsupported_unit', 422, `${T.attempt.validation} ${PROBLEM_CODE_TEXT.unsupported_unit}`],
  ])('a %s refusal is shown with its reason and nothing is created', async (code, status, message) => {
    installFetch({ ...salesReads, onCreate: () => json({ status, title: 'x', code, correlationId: 'c', errors: [] }, status) })
    renderLogisticsApp('/lojistik/yukleme-talimatlari/yeni')
    const quantity = await pickTheLine()
    await userEvent.type(quantity, '4.5')
    await userEvent.click(screen.getByTestId('logistics-create-button'))
    await waitFor(() => expect(screen.getByTestId('logistics-create-notice').textContent).toBe(message))
    expect(screen.queryByTestId('logistics-created')).toBeNull()
  })

  it('a code-less 403 and a 503 are shown honestly: refused, or unknown with the key kept', async () => {
    installFetch({ ...salesReads, onCreate: () => json({ status: 403, title: 'Forbidden' }, 403) })
    renderLogisticsApp('/lojistik/yukleme-talimatlari/yeni')
    await userEvent.type(await pickTheLine(), '1')
    await userEvent.click(screen.getByTestId('logistics-create-button'))
    await waitFor(() => expect(screen.getByTestId('logistics-create-notice').textContent).toBe(T.attempt.forbidden))
    cleanup()
    vi.unstubAllGlobals()
    installFetch({ ...salesReads, onCreate: () => json({ status: 503, title: 'Service Unavailable', code: 'service_unavailable' }, 503) })
    renderLogisticsApp('/lojistik/yukleme-talimatlari/yeni')
    await userEvent.type(await pickTheLine(), '1')
    await userEvent.click(screen.getByTestId('logistics-create-button'))
    await screen.findByText(T.attempt.unknown)
    expect(screen.getByRole('button', { name: T.attempt.retry })).toBeTruthy()
  })

  it('no approved draft means no customer to pick and no send; an ambiguous quantity is refused before any send', async () => {
    const harness = installFetch({ onSalesList: () => json({ items: [], nextCursor: null }) })
    renderLogisticsApp('/lojistik/yukleme-talimatlari/yeni')
    await screen.findByTestId('logistics-no-approved')
    expect((screen.getByTestId('logistics-create-button') as HTMLButtonElement).disabled).toBe(true)
    cleanup()
    vi.unstubAllGlobals()
    const second = installFetch({ ...salesReads })
    renderLogisticsApp('/lojistik/yukleme-talimatlari/yeni')
    await userEvent.type(await pickTheLine(), '1.000')
    await userEvent.click(screen.getByTestId('logistics-create-button'))
    await screen.findByText(T.validation.quantityAmbiguous)
    expect(second.logisticsCalls().filter((call) => call.method === 'POST')).toHaveLength(0)
    expect(harness.logisticsCalls().filter((call) => call.method === 'POST')).toHaveLength(0)
  })

  it('the route is closed without the create grant, and the Sales reads are not even attempted', async () => {
    const harness = installFetch({ capabilities: ALL_LOGISTICS.filter((id) => id !== LOGISTICS_CAPABILITY.instructionsCreate) })
    renderLogisticsApp('/lojistik/yukleme-talimatlari/yeni')
    await waitFor(() => expect(harness.calls.some((call) => call.url.includes('/shell/capabilities'))).toBe(true))
    await waitFor(() => expect(screen.queryByRole('heading', { name: T.heading })).toBeNull())
    expect(harness.calls.filter((call) => call.url.includes('/sales/'))).toHaveLength(0)
  })
})
