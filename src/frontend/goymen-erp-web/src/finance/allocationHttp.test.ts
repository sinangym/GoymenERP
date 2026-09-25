import { afterEach, describe, expect, it, vi } from 'vitest'
import { sendFinanceAllocationCommand } from './allocationHttp'
import type { FinanceAllocationWireSnapshot } from './allocationHttp'
import type { FinanceAllocation } from './allocationContract'

const COMPANY_ID = '11111111-1111-1111-1111-111111111111'
const ALLOCATION_ID = '22222222-2222-2222-2222-222222222222'
const SOURCE_ID = '33333333-3333-3333-3333-333333333333'
const OPEN_ITEM_ID = '44444444-4444-4444-4444-444444444444'
const CURRENCY_ID = '55555555-5555-5555-5555-555555555555'
const CREATED_BY = '66666666-6666-6666-6666-666666666666'
const RELEASED_BY = '77777777-7777-7777-7777-777777777777'

const snapshot: FinanceAllocationWireSnapshot = {
  key: 'allocation-key-1',
  binding: {
    companyId: COMPANY_ID,
    actorId: 'finance-user',
    sessionEpoch: 1,
  },
  request: {
    operation: 'allocation-release',
    body: {
      allocationId: ALLOCATION_ID,
      expectedAllocationVersion: 'version-1',
      expectedOpenItemVersion: 'item-version-1',
      reason: 'Yanlis dagitim',
    },
  },
}

function allocation(status: 'Active' | 'Released', version: string): FinanceAllocation {
  return {
    id: ALLOCATION_ID,
    companyId: COMPANY_ID,
    sourceId: SOURCE_ID,
    openItemId: OPEN_ITEM_ID,
    currencyId: CURRENCY_ID,
    amount: '125.50',
    status,
    version,
    reason: 'Fatura tahsilati',
    createdBy: CREATED_BY,
    createdAtUtc: '2026-09-25T09:00:00Z',
    releasedBy: status === 'Released' ? RELEASED_BY : null,
    releasedAtUtc: status === 'Released' ? '2026-09-25T09:15:00Z' : null,
    releaseReason: status === 'Released' ? 'Yanlis dagitim' : null,
  }
}

function commandResult(
  outcome: string,
  resource: unknown = null,
  replayed = false,
): Record<string, unknown> {
  return {
    outcome,
    resource,
    isReplay: replayed,
    originalCorrelationId: replayed ? 'allocation-original-correlation' : '',
    issues: [],
  }
}

function response(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('sendFinanceAllocationCommand', () => {
  it('posts release to the canonical route with the supplied idempotency key and company', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      response(200, commandResult('Succeeded', allocation('Released', '2'))),
    )
    vi.stubGlobal('fetch', fetchMock)

    const result = await sendFinanceAllocationCommand(snapshot)

    expect(result).toMatchObject({
      kind: 'settlement',
      settlement: {
        type: 'succeeded',
        replayed: false,
        resource: {
          id: ALLOCATION_ID,
          sourceId: SOURCE_ID,
          openItemId: OPEN_ITEM_ID,
          status: 'Released',
        },
      },
    })
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(fetchMock).toHaveBeenCalledWith(
      `/api/v1/finance/allocations/${ALLOCATION_ID}/release`,
      expect.objectContaining({
        method: 'POST',
        headers: expect.objectContaining({
          'X-Company-Id': COMPANY_ID,
          'Idempotency-Key': snapshot.key,
        }),
        body: JSON.stringify(snapshot.request.body),
      }),
    )
  })

  it('posts create to the canonical collection route', async () => {
    const createSnapshot: FinanceAllocationWireSnapshot = {
      ...snapshot,
      request: {
        operation: 'allocation-create',
        body: {
          allocationId: ALLOCATION_ID,
          sourceId: SOURCE_ID,
          openItemId: OPEN_ITEM_ID,
          amount: '125.50',
          expectedSourceVersion: 'source-version-1',
          expectedOpenItemVersion: 'item-version-1',
          reason: 'Fatura tahsilati',
        },
      },
    }
    const fetchMock = vi.fn().mockResolvedValue(
      response(200, commandResult('Succeeded', allocation('Active', '1'))),
    )
    vi.stubGlobal('fetch', fetchMock)

    const result = await sendFinanceAllocationCommand(createSnapshot)

    expect(result).toMatchObject({
      kind: 'settlement',
      settlement: {
        type: 'succeeded',
        replayed: false,
        resource: {
          id: ALLOCATION_ID,
          sourceId: SOURCE_ID,
          openItemId: OPEN_ITEM_ID,
          status: 'Active',
        },
      },
    })
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/v1/finance/allocations',
      expect.objectContaining({ body: JSON.stringify(createSnapshot.request.body) }),
    )
  })

  it.each([401, 403] as const)('keeps authorization status %s separate from domain settlement', async status => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status })))
    await expect(sendFinanceAllocationCommand(snapshot)).resolves.toEqual({ kind: 'authorization', status })
  })

  it.each([400, 415] as const)('keeps protocol rejection status %s separate from domain settlement', async status => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status })))
    await expect(sendFinanceAllocationCommand(snapshot)).resolves.toEqual({ kind: 'http-rejection', status })
  })

  it('classifies a transport failure as unknown without changing the attempt', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('network unavailable')))
    const result = await sendFinanceAllocationCommand(snapshot)
    expect(result).toMatchObject({ kind: 'settlement', settlement: { type: 'unknown' } })
  })

  it('preserves a received 503 as definite unavailable', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 503 })))
    const result = await sendFinanceAllocationCommand(snapshot)
    expect(result).toMatchObject({
      kind: 'settlement',
      settlement: { type: 'unknown', reason: 'unavailable' },
    })
  })

  it('keeps an abort during body read unresolved and preserves the attempt for reconciliation', async () => {
    const controller = new AbortController()
    const abortingResponse = {
      status: 200,
      json: vi.fn().mockImplementation(async () => {
        controller.abort()
        throw new DOMException('Body read aborted', 'AbortError')
      }),
    } as unknown as Response
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(abortingResponse)
      .mockResolvedValueOnce(response(200, commandResult('Succeeded', allocation('Released', '2'), true)))
    vi.stubGlobal('fetch', fetchMock)

    const firstResult = await sendFinanceAllocationCommand(snapshot, controller.signal)
    const secondResult = await sendFinanceAllocationCommand(snapshot)

    expect(firstResult).toMatchObject({
      kind: 'settlement',
      settlement: { type: 'unknown', reason: 'no-reply' },
    })
    expect(secondResult).toMatchObject({
      kind: 'settlement',
      settlement: { type: 'succeeded', replayed: true },
    })
    const first = fetchMock.mock.calls[0]?.[1] as RequestInit
    const second = fetchMock.mock.calls[1]?.[1] as RequestInit
    expect(first.headers).toEqual(second.headers)
    expect(first.body).toBe(second.body)
  })

  it.each([
    ['AbortError', new DOMException('Response body stream aborted', 'AbortError')],
    ['TypeError', new TypeError('Response body stream failed')],
  ])('keeps a body-read %s unresolved without aborting the caller signal', async (_name, error) => {
    const controller = new AbortController()
    const interruptedResponse = {
      status: 200,
      json: vi.fn().mockRejectedValue(error),
    } as unknown as Response
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(interruptedResponse)
      .mockResolvedValueOnce(response(200, commandResult('Succeeded', allocation('Released', '2'), true)))
    vi.stubGlobal('fetch', fetchMock)

    const firstResult = await sendFinanceAllocationCommand(snapshot, controller.signal)
    const secondResult = await sendFinanceAllocationCommand(snapshot, controller.signal)

    expect(controller.signal.aborted).toBe(false)
    expect(firstResult).toMatchObject({
      kind: 'settlement',
      settlement: { type: 'unknown', reason: 'no-reply' },
    })
    expect(secondResult).toMatchObject({
      kind: 'settlement',
      settlement: {
        type: 'succeeded',
        replayed: true,
        resource: {
          id: ALLOCATION_ID,
          sourceId: SOURCE_ID,
          openItemId: OPEN_ITEM_ID,
          status: 'Released',
        },
      },
    })
    const first = fetchMock.mock.calls[0]?.[1] as RequestInit
    const second = fetchMock.mock.calls[1]?.[1] as RequestInit
    expect(first.headers).toEqual(second.headers)
    expect(first.body).toBe(second.body)
  })

  it('keeps fully received invalid JSON terminally malformed', async () => {
    const invalidJsonResponse = {
      status: 200,
      json: vi.fn().mockRejectedValue(new SyntaxError('Unexpected token')),
    } as unknown as Response
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(invalidJsonResponse))

    const result = await sendFinanceAllocationCommand(snapshot)

    expect(result).toMatchObject({
      kind: 'settlement',
      settlement: { type: 'unknown', reason: 'malformed' },
    })
  })

  it('rejects status and command-outcome disagreement as malformed', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response(200, commandResult('VersionConflict'))))
    const result = await sendFinanceAllocationCommand(snapshot)
    expect(result).toMatchObject({ kind: 'settlement', settlement: { type: 'unknown', reason: 'malformed' } })
  })

  it('rejects a valid allocation envelope for another company as malformed', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response(200, commandResult('Succeeded', {
      ...allocation('Active', '1'),
      companyId: '88888888-8888-8888-8888-888888888888',
    }))))
    const result = await sendFinanceAllocationCommand(snapshot)
    expect(result).toMatchObject({ kind: 'settlement', settlement: { type: 'unknown', reason: 'malformed' } })
  })

  it('rejects a valid allocation envelope for another allocation as malformed', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response(200, commandResult('Succeeded', {
      ...allocation('Released', '2'),
      id: '99999999-9999-9999-9999-999999999999',
    }))))
    const result = await sendFinanceAllocationCommand(snapshot)
    expect(result).toMatchObject({ kind: 'settlement', settlement: { type: 'unknown', reason: 'malformed' } })
  })

  it('rejects the historical incomplete success resource as malformed', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response(200, commandResult('Succeeded', {
      allocationId: ALLOCATION_ID,
      version: 'version-2',
      state: 'Released',
    }))))
    const result = await sendFinanceAllocationCommand(snapshot)
    expect(result).toMatchObject({ kind: 'settlement', settlement: { type: 'unknown', reason: 'malformed' } })
  })

  it('passes a coherent version conflict to the command classifier', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response(409, commandResult('VersionConflict'))))
    const result = await sendFinanceAllocationCommand(snapshot)
    expect(result).toMatchObject({ kind: 'settlement', settlement: { type: 'refused' } })
  })

  it('passes validation failure issues to the command classifier', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response(422, {
      ...commandResult('ValidationFailed'),
      issues: [{ field: 'amount', code: 'finance_allocation_amount_exceeds_open_item' }],
    })))
    const result = await sendFinanceAllocationCommand(snapshot)
    expect(result).toMatchObject({ kind: 'settlement', settlement: { type: 'refused' } })
  })

  it('does not send a request for an unbound company id', async () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    const result = await sendFinanceAllocationCommand({
      ...snapshot,
      binding: { ...snapshot.binding, companyId: '00000000-0000-0000-0000-000000000000' },
    })
    expect(fetchMock).not.toHaveBeenCalled()
    expect(result).toMatchObject({ kind: 'settlement', settlement: { type: 'unknown', reason: 'malformed' } })
  })

  it('does not send a release request for an unbound allocation id', async () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    const result = await sendFinanceAllocationCommand({
      ...snapshot,
      request: {
        operation: 'allocation-release',
        body: {
          allocationId: 'not-a-guid',
          expectedAllocationVersion: 'version-1',
          expectedOpenItemVersion: 'item-version-1',
          reason: 'Yanlis dagitim',
        },
      },
    })
    expect(fetchMock).not.toHaveBeenCalled()
    expect(result).toMatchObject({ kind: 'settlement', settlement: { type: 'unknown', reason: 'malformed' } })
  })

  it('preserves the supplied key and payload when the caller explicitly retries', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(response(503, {}))
      .mockResolvedValueOnce(response(200, commandResult('Succeeded', allocation('Released', '2'), true)))
    vi.stubGlobal('fetch', fetchMock)

    const firstResult = await sendFinanceAllocationCommand(snapshot)
    const secondResult = await sendFinanceAllocationCommand(snapshot)

    expect(firstResult).toMatchObject({
      kind: 'settlement',
      settlement: { type: 'unknown', reason: 'unavailable' },
    })
    expect(secondResult).toMatchObject({
      kind: 'settlement',
      settlement: {
        type: 'succeeded',
        replayed: true,
        resource: {
          id: ALLOCATION_ID,
          sourceId: SOURCE_ID,
          openItemId: OPEN_ITEM_ID,
          status: 'Released',
        },
      },
    })
    const first = fetchMock.mock.calls[0]?.[1] as RequestInit
    const second = fetchMock.mock.calls[1]?.[1] as RequestInit
    expect(first.headers).toEqual(second.headers)
    expect(first.body).toBe(second.body)
  })
})
