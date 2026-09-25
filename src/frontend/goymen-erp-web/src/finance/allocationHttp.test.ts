import { afterEach, describe, expect, it, vi } from 'vitest'
import { sendFinanceAllocationCommand } from './allocationHttp'
import type { FinanceAllocationWireSnapshot } from './allocationHttp'

const COMPANY_ID = '11111111-1111-1111-1111-111111111111'
const ALLOCATION_ID = '22222222-2222-2222-2222-222222222222'

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

function commandResult(outcome: string, resource: unknown = null): Record<string, unknown> {
  return {
    outcome,
    resource,
    isReplay: false,
    originalCorrelationId: '',
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
    const fetchMock = vi.fn().mockResolvedValue(response(200, commandResult('Succeeded', {
      allocationId: ALLOCATION_ID,
      version: 'version-2',
      state: 'Released',
    })))
    vi.stubGlobal('fetch', fetchMock)

    const result = await sendFinanceAllocationCommand(snapshot)

    expect(result.kind).toBe('settlement')
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
          sourceId: '33333333-3333-3333-3333-333333333333',
          openItemId: '44444444-4444-4444-4444-444444444444',
          amount: '125.50',
          expectedSourceVersion: 'source-version-1',
          expectedOpenItemVersion: 'item-version-1',
          reason: 'Fatura tahsilati',
        },
      },
    }
    const fetchMock = vi.fn().mockResolvedValue(response(200, commandResult('Succeeded', {
      allocationId: ALLOCATION_ID,
      version: 'version-1',
      state: 'Active',
    })))
    vi.stubGlobal('fetch', fetchMock)

    await sendFinanceAllocationCommand(createSnapshot)

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

  it('classifies a server failure as unknown', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 503 })))
    const result = await sendFinanceAllocationCommand(snapshot)
    expect(result).toMatchObject({ kind: 'settlement', settlement: { type: 'unknown' } })
  })

  it('rejects status and command-outcome disagreement as malformed', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response(200, commandResult('VersionConflict'))))
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
      .mockResolvedValueOnce(response(200, commandResult('Succeeded', {
        allocationId: ALLOCATION_ID,
        version: 'version-2',
        state: 'Released',
      })))
    vi.stubGlobal('fetch', fetchMock)

    await sendFinanceAllocationCommand(snapshot)
    await sendFinanceAllocationCommand(snapshot)

    const first = fetchMock.mock.calls[0]?.[1] as RequestInit
    const second = fetchMock.mock.calls[1]?.[1] as RequestInit
    expect(first.headers).toEqual(second.headers)
    expect(first.body).toBe(second.body)
  })
})
