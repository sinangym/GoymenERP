/**
 * L-02-DETAIL in the real shell: the shares shown as exact text, "Yayımla" only for a Draft and a
 * create holder, the record-load form only on an Issued instruction, one load id per snapshot kept
 * on retry, a replay shown as a replay, the detail re-read after a transition and after a refusal
 * that moved the record (stale version, cap, block, duplicate), a code-less 403 shown as a refusal.
 * Component evidence, NOT_ENV.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { INSTRUCTION_DETAIL_TEXT as T, PROBLEM_CODE_TEXT } from './logisticsText'
import { CUSTOMER_RETURN_CAPABILITY } from './customerReturnApi'
import type { CustomerReturnResponseV1, CustomerReturnViewV1, RequestCustomerReturnV1 } from './customerReturnContract'
import {
  FORBIDDEN_MONEY_WORDS,
  ALL_LOGISTICS,
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

function installCustomerReturnScreen(capabilities: readonly string[], handlers: {
  readonly onRead?: (fallback: CustomerReturnViewV1) => Response | Promise<Response>
  readonly onRequest?: (fallback: CustomerReturnViewV1, request: RequestCustomerReturnV1) => Response | Promise<Response>
} = {}) {
  const companyId = '00000000-0000-4000-8000-000000000041'
  const instructionId = '00000000-0000-4000-8000-000000000042'
  const lineId = '00000000-0000-4000-8000-000000000043'
  const sourceOrderId = '00000000-0000-4000-8000-000000000044'
  const sourceOrderLineId = '00000000-0000-4000-8000-000000000045'
  const unitId = '00000000-0000-4000-8000-000000000046'
  const line = instructionLine({
    lineId,
    sourceOrderId,
    sourceOrderLineId,
    unitId,
    delivered: '1',
    shipped: '1',
    shippedNotDelivered: '0',
  })
  const delivery = {
    shareId: '00000000-0000-4000-8000-000000000051',
    lineId,
    kind: 'Delivered',
    quantity: '1',
    measurement: null,
    sourceShipmentId: '00000000-0000-4000-8000-000000000052',
    recordedAtUtc: '2026-10-04T08:00:00Z',
  }
  const detail = instructionDetail({ id: instructionId, companyId, status: 'Issued', version: '3' }, [line], [delivery])
  installFetch({ capabilities, onDetail: () => json(detail) })
  const shellFetch = globalThis.fetch
  const customerReturn: CustomerReturnViewV1 = {
    customerReturnId: '00000000-0000-4000-8000-000000000053',
    companyId,
    loadingInstructionId: instructionId,
    loadingInstructionLineId: lineId,
    sourceDeliveryId: delivery.shareId,
    sourceShipmentId: delivery.sourceShipmentId,
    sourceOrderId,
    sourceOrderLineId,
    unitId,
    quantity: '0.5',
    sourceDeliveredQuantity: '1',
    status: 'Pending',
    version: '1',
    reason: 'SENTETIK müşteri iadesi',
    requestedBy: 'requester-a',
    occurredAtUtc: '2026-10-04T08:00:00Z',
    requestedAtUtc: '2026-10-04T08:01:00Z',
    decidedBy: null,
    decidedAtUtc: null,
    decisionNote: null,
    inventoryMovementId: null,
    originalLabelId: null,
    originalShipmentMovementId: null,
  }
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(typeof input === 'string' ? input : input instanceof URL ? input.href : input.url, 'http://test')
    if (url.pathname.endsWith('/identity/me')) {
      return json({
        userId: 'customer-return-user',
        displayName: 'Müşteri iadesi kullanıcısı',
        email: null,
        allowedCompanyIds: [companyId],
      })
    }
    if (url.pathname.endsWith('/organization/companies')) {
      return json([{ id: companyId, code: 'GDC', legalName: 'Göymen A', isPlaceholder: true }])
    }
    if (url.pathname === `/api/v1/logistics/orders/${sourceOrderId}/customer-returns`) {
      return handlers.onRead?.(customerReturn) ?? json([customerReturn])
    }
    if (
      init?.method === 'POST'
      && url.pathname === `/api/v1/logistics/instructions/${instructionId}/lines/${lineId}/customer-returns`
      && handlers.onRequest
    ) {
      return handlers.onRequest(customerReturn, JSON.parse(String(init.body)) as RequestCustomerReturnV1)
    }
    return shellFetch(input, init)
  }))
  return { customerReturn, path: `/lojistik/yukleme-talimatlari/${instructionId}` }
}

describe('L-02-DETAIL', () => {
  it('fails closed at the real shell boundary when the customer-return read capability is absent', async () => {
    const setup = installCustomerReturnScreen(ALL_LOGISTICS)
    renderLogisticsApp(setup.path)

    await screen.findByText('Bu kayıt için kullanılabilir iade bilgisi bulunamadı.')
    expect(screen.queryByText('SENTETIK müşteri iadesi')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Talep oluştur' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Onayla' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Reddet' })).toBeNull()
  })

  it.each([
    [CUSTOMER_RETURN_CAPABILITY.request, 'Talep oluştur', ['Onayla', 'Reddet']],
    [CUSTOMER_RETURN_CAPABILITY.approve, 'Onayla', ['Talep oluştur', 'Reddet']],
    [CUSTOMER_RETURN_CAPABILITY.reject, 'Reddet', ['Talep oluştur', 'Onayla']],
  ] as const)('maps the published %s shell capability to only its own control', async (capability, visible, hidden) => {
    const setup = installCustomerReturnScreen([...ALL_LOGISTICS, CUSTOMER_RETURN_CAPABILITY.read, capability])
    renderLogisticsApp(setup.path)

    await screen.findByText('SENTETIK müşteri iadesi')
    expect(screen.getByRole('button', { name: visible })).not.toBeNull()
    for (const label of hidden) expect(screen.queryByRole('button', { name: label })).toBeNull()
  })

  it('keeps request completion visible after the matching fresh read unlocks the next partial return', async () => {
    let current: CustomerReturnViewV1 | null = null
    let reads = 0
    let posts = 0
    const second = deferred<Response>()
    const setup = installCustomerReturnScreen([
      ...ALL_LOGISTICS,
      CUSTOMER_RETURN_CAPABILITY.read,
      CUSTOMER_RETURN_CAPABILITY.request,
    ], {
      onRead: (fallback) => {
        reads += 1
        return json([current ?? fallback])
      },
      onRequest: (fallback, request) => {
        posts += 1
        current = {
          ...fallback,
          customerReturnId: request.customerReturnId,
          quantity: request.quantity,
          reason: request.reason,
          occurredAtUtc: request.occurredAtUtc,
        }
        const response: CustomerReturnResponseV1 = {
          return: current,
          isReplay: false,
          originalCorrelationId: `customer-return-${posts}`,
        }
        return posts === 1 ? json(response, 201) : second.promise
      },
    })
    renderLogisticsApp(setup.path)

    fireEvent.change(await screen.findByLabelText('İade miktarı'), { target: { value: '0.25' } })
    fireEvent.change(screen.getByLabelText('İade nedeni'), { target: { value: 'İlk kısmi iade' } })
    fireEvent.change(screen.getByLabelText('İade zamanı'), { target: { value: '2026-10-04T09:00' } })
    const submit = screen.getByRole('button', { name: 'Talep oluştur' }) as HTMLButtonElement
    await userEvent.click(submit)

    await screen.findByText('İşlem kaydedildi.')
    await waitFor(() => expect(reads).toBeGreaterThanOrEqual(2))
    expect(submit.disabled).toBe(false)

    await userEvent.click(submit)
    await waitFor(() => {
      expect(posts).toBe(2)
      expect(screen.queryByText('İşlem kaydedildi.')).toBeNull()
    })
  })

  it('keeps the completed command visible and locked when its fresh read fails', async () => {
    let reads = 0
    const setup = installCustomerReturnScreen([
      ...ALL_LOGISTICS,
      CUSTOMER_RETURN_CAPABILITY.read,
      CUSTOMER_RETURN_CAPABILITY.request,
    ], {
      onRead: (fallback) => {
        reads += 1
        return reads === 1 ? json([fallback]) : json({ title: 'read failed' }, 500)
      },
      onRequest: (fallback, request) => json({
        return: {
          ...fallback,
          customerReturnId: request.customerReturnId,
          quantity: request.quantity,
          reason: request.reason,
          occurredAtUtc: request.occurredAtUtc,
        },
        isReplay: false,
        originalCorrelationId: 'customer-return-failed-fresh-read',
      } satisfies CustomerReturnResponseV1, 201),
    })
    renderLogisticsApp(setup.path)

    fireEvent.change(await screen.findByLabelText('İade miktarı'), { target: { value: '0.25' } })
    fireEvent.change(screen.getByLabelText('İade nedeni'), { target: { value: 'Fresh read başarısız' } })
    fireEvent.change(screen.getByLabelText('İade zamanı'), { target: { value: '2026-10-04T09:00' } })
    await userEvent.click(screen.getByRole('button', { name: 'Talep oluştur' }))

    await waitFor(() => expect(reads).toBeGreaterThanOrEqual(2))
    expect(screen.getByText('İşlem kaydedildi.')).not.toBeNull()
    expect(screen.queryByRole('button', { name: 'Talep oluştur' })).toBeNull()
  })

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

  it('the SAME quantity and measurement submitted again after a completion is a second load: two POSTs, two keys, two load ids, both recorded', async () => {
    // The key and the load id belong to the ATTEMPT, never to the payload: two genuinely equal loads (an ordinary thing
    // for a loader to do) must not collapse into one key or one load id — that would be one stock event lost.
    let reads = 0
    const harness = installFetch({
      onDetail: () => {
        reads += 1
        return json(issued())
      },
      onLoad: (_, call) => loadAnswer(call),
    })
    renderLogisticsApp(path)
    const quantity = await screen.findByLabelText(T.load.quantity)
    await userEvent.type(quantity, '2,5')
    await userEvent.click(screen.getByTestId('logistics-load-button'))
    await waitFor(() => expect(screen.getByTestId('logistics-load-notice').textContent).toBe(T.command.completed))
    await waitFor(() => expect(reads).toBeGreaterThanOrEqual(2))
    await userEvent.clear(quantity)
    await userEvent.type(quantity, '2,5')
    await userEvent.click(screen.getByTestId('logistics-load-button'))
    await waitFor(() => expect(harness.logisticsCalls().filter((call) => call.method === 'POST')).toHaveLength(2))
    await waitFor(() => expect(reads).toBeGreaterThanOrEqual(3))
    expect(screen.getByTestId('logistics-load-notice').textContent).toBe(T.command.completed)
    const posts = harness.logisticsCalls().filter((call) => call.method === 'POST')
    const bodies = posts.map((post) => JSON.parse(post.body ?? '{}') as { loadId: string; quantity: string; measurement: string })
    expect(bodies.map((body) => [body.quantity, body.measurement])).toEqual([['2.5', 'Measured'], ['2.5', 'Measured']])
    expect(posts[0].idempotencyKey).not.toBe(posts[1].idempotencyKey)
    expect(bodies[0].loadId).not.toBe(bodies[1].loadId)
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
