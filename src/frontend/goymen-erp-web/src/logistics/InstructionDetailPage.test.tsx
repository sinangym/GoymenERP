/**
 * L-02-DETAIL in the real shell: the shares shown as exact text, "Yayımla" only for a Draft and a
 * create holder, the record-load form only on an Issued instruction, one load id per snapshot kept
 * on retry, a replay shown as a replay, the detail re-read after a transition and after a refusal
 * that moved the record (stale version, cap, block, duplicate), a code-less 403 shown as a refusal.
 * Component evidence, NOT_ENV.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { INSTRUCTION_DETAIL_TEXT as T, PROBLEM_CODE_TEXT } from './logisticsText'
import {
  FORBIDDEN_MONEY_WORDS,
  INSTRUCTION,
  LINE,
  READ_ONLY_LOGISTICS,
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

const path = `/lojistik/yukleme-talimatlari/${INSTRUCTION}`
const issued = (lines = [instructionLine()], shares: Record<string, unknown>[] = [], version = '3') =>
  instructionDetail({ status: 'Issued', version }, lines, shares)

function loadAnswer(call: RecordedCall, isReplay = false): Response {
  const body = JSON.parse(call.body ?? '{}') as { loadId: string; quantity: string; measurement: string }
  const share = { shareId: body.loadId, lineId: LINE, kind: 'Loaded', quantity: body.quantity, measurement: body.measurement, sourceShipmentId: null, recordedAtUtc: '2026-09-19T09:10:00Z' }
  const line = instructionLine({ loaded: body.quantity, remainingToLoad: '2', loadedNotShipped: body.quantity })
  return json(mutation(issued([line], [share], '4'), isReplay), 200, isReplay ? { 'Idempotency-Replayed': 'true' } : {})
}

describe('L-02-DETAIL', () => {
  it('shows the facts and the shares as text, no money word; a Draft offers "Yayımla" and no load form', async () => {
    installFetch({ onDetail: () => json(instructionDetail()) })
    renderLogisticsApp(path)
    await screen.findByTestId('logistics-instruction-id')
    expect(screen.getByTestId('logistics-instruction-status').textContent).toBe('Taslak')
    expect(screen.getByTestId('logistics-line-1-instructed').textContent).toBe('4.5')
    expect(screen.getByTestId('logistics-shares-empty')).toBeTruthy()
    expect(screen.getByTestId('logistics-issue-button')).toBeTruthy()
    expect(screen.getByTestId('logistics-load-draft-only')).toBeTruthy()
    for (const word of FORBIDDEN_MONEY_WORDS) expect(document.body.textContent).not.toContain(word)
  })

  it('issues the Draft with the expected version under one key and re-reads the detail', async () => {
    let reads = 0
    const harness = installFetch({
      onDetail: () => json(++reads === 1 ? instructionDetail() : issued()),
      onIssue: () => json(mutation(issued()), 200),
    })
    renderLogisticsApp(path)
    await userEvent.click(await screen.findByTestId('logistics-issue-button'))
    await waitFor(() => expect(screen.getByTestId('logistics-issue-notice').textContent).toBe(T.command.completed))
    const issue = harness.logisticsCalls().find((call) => call.method === 'POST')
    expect(JSON.parse(issue!.body ?? '{}')).toEqual({ expectedVersion: '2' })
    expect(issue!.idempotencyKey).toMatch(/^[0-9a-f]{32}$/)
    await waitFor(() => expect(screen.getByTestId('logistics-instruction-status').textContent).toBe('Yayımlandı'))
  })

  it('records a load with the quantity text as typed, one load id kept across a lost-reply retry, replay shown as replay', async () => {
    const first = deferred<Response>()
    let loads = 0
    const harness = installFetch({
      onDetail: () => json(issued()),
      onLoad: (_, call) => (++loads === 1 ? first.promise : loadAnswer(call, true)),
    })
    renderLogisticsApp(path)
    await userEvent.type(await screen.findByLabelText(T.load.quantity), '2,5')
    await userEvent.click(screen.getByTestId('logistics-load-button'))
    await screen.findByText(T.command.submitting)
    first.reject(new TypeError('Failed to fetch'))
    await screen.findByText(T.command.unknown)
    await userEvent.click(screen.getByRole('button', { name: T.command.retry }))
    await waitFor(() => expect(screen.getByTestId('logistics-load-notice').textContent).toBe(T.command.replayed))
    const posts = harness.logisticsCalls().filter((call) => call.method === 'POST')
    expect(posts).toHaveLength(2)
    expect(posts[0].idempotencyKey).toBe(posts[1].idempotencyKey)
    expect(posts[0].body).toBe(posts[1].body)
    expect(JSON.parse(posts[0].body ?? '{}')).toMatchObject({ expectedVersion: '3', quantity: '2.5', measurement: 'Measured' })
    expect(posts[0].url).toBe(`/api/v1/logistics/loading-instructions/${INSTRUCTION}/lines/${LINE}/loads`)
  })

  it('a second load after a completed one is a NEW attempt under a new key and a new load id; its refusal replaces the success notice', async () => {
    // l04 run 35481530144 at dc41a2d2: after the first load the section stayed `completed` and the next submit was silently
    // blocked, so the screen kept "İşlem kaydedildi." while nothing was sent. Two consecutive loads on one screen: two
    // POSTs with different keys and load ids; the second's cap refusal is what the notice shows, never the earlier success.
    let loads = 0
    let reads = 0
    const harness = installFetch({
      onDetail: () => {
        reads += 1
        return json(issued())
      },
      onLoad: (_, call) =>
        ++loads === 1
          ? loadAnswer(call)
          : json({ status: 409, title: 'Conflict', code: 'logistics_load_exceeds_instructed', correlationId: 'c', errors: [] }, 409),
    })
    renderLogisticsApp(path)
    const quantity = await screen.findByLabelText(T.load.quantity)
    await userEvent.type(quantity, '2,5')
    await userEvent.click(screen.getByTestId('logistics-load-button'))
    await waitFor(() => expect(screen.getByTestId('logistics-load-notice').textContent).toBe(T.command.completed))
    // Property 1: the completed notice survives the re-read the completion caused (the section is keyed by record).
    await waitFor(() => expect(reads).toBeGreaterThanOrEqual(2))
    expect(screen.getByTestId('logistics-load-notice').textContent).toBe(T.command.completed)
    // Property 2: the next submit is a fresh attempt whose outcome replaces that notice.
    await userEvent.clear(quantity)
    await userEvent.type(quantity, '3')
    await userEvent.click(screen.getByTestId('logistics-load-button'))
    await waitFor(() =>
      expect(screen.getByTestId('logistics-load-notice').textContent).toBe(`${T.command.conflict} ${PROBLEM_CODE_TEXT['logistics_load_exceeds_instructed' as keyof typeof PROBLEM_CODE_TEXT]}`),
    )
    expect(screen.queryByText(T.command.completed)).toBeNull()
    const posts = harness.logisticsCalls().filter((call) => call.method === 'POST')
    expect(posts).toHaveLength(2)
    expect(posts[0].idempotencyKey).not.toBe(posts[1].idempotencyKey)
    const [firstBody, secondBody] = posts.map((post) => JSON.parse(post.body ?? '{}') as { loadId: string; quantity: string })
    expect([firstBody.quantity, secondBody.quantity]).toEqual(['2.5', '3'])
    expect(firstBody.loadId).not.toBe(secondBody.loadId)
  })

  it.each([
    ['logistics_load_exceeds_instructed', true],
    ['logistics_line_blocked', true],
    ['version_conflict', true],
    ['logistics_duplicate_share_id', true],
    ['logistics_instruction_state', true],
  ])('a 409 %s is shown with its reason and the detail is re-read', async (code, reread) => {
    let reads = 0
    installFetch({
      onDetail: () => {
        reads += 1
        return json(issued())
      },
      onLoad: () => json({ status: 409, title: 'Conflict', code, correlationId: 'c', errors: [] }, 409),
    })
    renderLogisticsApp(path)
    await userEvent.type(await screen.findByLabelText(T.load.quantity), '9')
    await userEvent.click(screen.getByTestId('logistics-load-button'))
    await waitFor(() =>
      expect(screen.getByTestId('logistics-load-notice').textContent).toBe(`${T.command.conflict} ${PROBLEM_CODE_TEXT[code as keyof typeof PROBLEM_CODE_TEXT]}`),
    )
    if (reread) await waitFor(() => expect(reads).toBeGreaterThanOrEqual(2))
  })

  it('a code-less 403 on the load is shown as "no permission" and nothing is recorded; a reader has no "Yayımla"', async () => {
    let reads = 0
    installFetch({
      capabilities: READ_ONLY_LOGISTICS,
      onDetail: () => {
        reads += 1
        return json(issued())
      },
      onLoad: () => json({ status: 403, title: 'Forbidden' }, 403),
    })
    renderLogisticsApp(path)
    await userEvent.type(await screen.findByLabelText(T.load.quantity), '1')
    expect(screen.queryByTestId('logistics-issue-button')).toBeNull()
    await userEvent.click(screen.getByTestId('logistics-load-button'))
    await waitFor(() => expect(screen.getByTestId('logistics-load-notice').textContent).toBe(T.command.forbidden))
    expect(reads).toBe(1)
  })

  it('a blocked line cannot be chosen for a load', async () => {
    installFetch({ onDetail: () => json(issued([instructionLine(), instructionLine({ lineId: 'dddddddd-dddd-dddd-dddd-ddddddddddd2', lineNo: 2, isBlocked: true })])) })
    renderLogisticsApp(path)
    await screen.findByTestId('logistics-line-2-blocked')
    const select = screen.getByLabelText(T.load.line) as HTMLSelectElement
    expect(select.options[1].disabled).toBe(true)
  })
})
