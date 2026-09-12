import { afterEach, describe, expect, it, vi } from 'vitest'
import { setLocationActiveState, type CommandContext } from './commandApi'

const CONTEXT: CommandContext = {
  companyId: 'aaaaaaaa-0000-0000-0000-000000000001',
  locationId: '33333333-3333-3333-3333-333333333333',
  idempotencyKey: 'key-1',
}

const PAYLOAD = { isActive: false, expectedVersion: 3, reason: '  Bakım için pasife alma  ' }

interface Call {
  url: string
  init: RequestInit | undefined
}

function stub(responder: () => Response | Promise<Response> | never): Call[] {
  const calls: Call[] = []
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      calls.push({ url: input.toString(), init })
      return responder()
    }),
  )
  return calls
}

function body(calls: Call[]): Record<string, unknown> {
  return JSON.parse(String(calls[0].init?.body))
}

function headers(calls: Call[]): Record<string, string> {
  return (calls[0].init?.headers ?? {}) as Record<string, string>
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('request shape', () => {
  it('sends exactly the three contract fields, with the reason trimmed', async () => {
    const calls = stub(() => new Response(null, { status: 204 }))
    await setLocationActiveState(CONTEXT, PAYLOAD)

    expect(body(calls)).toEqual({
      isActive: false,
      expectedVersion: '3',
      reason: 'Bakım için pasife alma',
    })
    // No company, no id, no predicted version may appear in the body.
    expect(Object.keys(body(calls)).sort()).toEqual(['expectedVersion', 'isActive', 'reason'])
  })

  it('sends expectedVersion as the decimal string the schema permits', async () => {
    const calls = stub(() => new Response(null, { status: 204 }))
    await setLocationActiveState(CONTEXT, PAYLOAD)
    expect(body(calls).expectedVersion).toBe('3')
    expect(String(body(calls).expectedVersion)).toMatch(/^[1-9][0-9]*$/)
  })

  it('PUTs to the active-state route with the company and the caller’s key', async () => {
    const calls = stub(() => new Response(null, { status: 204 }))
    await setLocationActiveState(CONTEXT, PAYLOAD)

    expect(calls[0].init?.method).toBe('PUT')
    expect(calls[0].url).toContain(
      '/organization/locations/33333333-3333-3333-3333-333333333333/active-state',
    )
    expect(headers(calls)['X-Company-Id']).toBe(CONTEXT.companyId)
    expect(headers(calls)['Idempotency-Key']).toBe('key-1')
  })

  it('never sends the Cari client-contract header', async () => {
    // That handshake is a Counterparty compatibility mechanism; this surface has none.
    const calls = stub(() => new Response(null, { status: 204 }))
    await setLocationActiveState(CONTEXT, PAYLOAD)
    expect(Object.keys(headers(calls))).not.toContain('X-ERP-Counterparty-Contract')
  })
})

describe('success', () => {
  it('treats an exact 204 as applied', async () => {
    stub(() => new Response(null, { status: 204 }))
    await expect(setLocationActiveState(CONTEXT, PAYLOAD)).resolves.toEqual({
      type: 'applied',
      replayed: false,
      originalCorrelationId: null,
    })
  })

  it('surfaces a replay with its original correlation id', async () => {
    stub(
      () =>
        new Response(null, {
          status: 204,
          headers: {
            'Idempotency-Replayed': 'true',
            'Idempotency-Original-Correlation-Id': 'corr-original',
          },
        }),
    )
    await expect(setLocationActiveState(CONTEXT, PAYLOAD)).resolves.toEqual({
      type: 'applied',
      replayed: true,
      originalCorrelationId: 'corr-original',
    })
  })

  it.each([200, 202])('treats a %i as UNKNOWN — not success, and not a refusal', async (status) => {
    /**
     * 204 is the only success this contract defines. Reading a 200/202 as applied
     * would assert an effect the server never confirmed; reading it as a refusal would
     * assert the opposite. Neither is ours to claim, so it stays unknown and the key
     * is kept.
     */
    stub(() => new Response(JSON.stringify({}), { status }))
    const outcome = await setLocationActiveState(CONTEXT, PAYLOAD)
    expect(outcome).toMatchObject({
      type: 'unknown',
      unknown: { kind: 'unexpected-success', status },
    })
  })
})

describe('the two different 409s', () => {
  it('classifies a 409 WITH the conflict object as a version conflict', async () => {
    stub(() =>
      new Response(JSON.stringify({ title: 'Kayıt güncellendi.', conflict: { expectedVersion: 3, currentVersion: 5 } }), {
        status: 409,
        headers: { 'Content-Type': 'application/problem+json' },
      }),
    )
    const outcome = await setLocationActiveState(CONTEXT, PAYLOAD)
    expect(outcome).toEqual({
      type: 'failed',
      replayed: false,
      failure: {
        kind: 'version-conflict',
        conflict: { expectedVersion: 3, currentVersion: 5 },
        conflictIsCurrent: true,
      },
    })
  })

  it('classifies a 409 WITHOUT the conflict object as an idempotency conflict', async () => {
    // Same key, different payload. A re-send cannot help and a new key would be a
    // second business command — a different recovery entirely.
    stub(() =>
      new Response(JSON.stringify({ title: 'Idempotency-Key çakışması.', status: 409 }), {
        status: 409,
        headers: { 'Content-Type': 'application/problem+json' },
      }),
    )
    const outcome = await setLocationActiveState(CONTEXT, PAYLOAD)
    expect(outcome).toEqual({
      type: 'failed',
      replayed: false,
      failure: { kind: 'idempotency-conflict' },
    })
  })

  it('does NOT call a 409 an idempotency conflict when its body never parsed', async () => {
    /**
     * The presence/absence of the conflict object is the ONLY discriminator on this
     * code-less surface. If the body did not arrive, that discriminator is unavailable
     * — and "no conflict object" would then be a fact about our parser, not about the
     * server. Naming it an idempotency conflict would tell the user something specific
     * we do not know, so it fails closed into unknown.
     */
    stub(() => new Response('<<not json>>', { status: 409 }))
    const outcome = await setLocationActiveState(CONTEXT, PAYLOAD)
    expect(outcome).toMatchObject({
      type: 'unknown',
      unknown: { kind: 'undecodable', status: 409 },
    })
  })

  it('marks a replayed refusal as the ORIGINAL command’s own outcome', async () => {
    // A replayed terminal answer resolves the attempt; a fresh one on a retry does not.
    stub(() =>
      new Response(JSON.stringify({ title: 'Kayıt bulunamadı.' }), {
        status: 404,
        headers: { 'Idempotency-Replayed': 'true' },
      }),
    )
    const outcome = await setLocationActiveState(CONTEXT, PAYLOAD)
    expect(outcome).toMatchObject({ type: 'failed', replayed: true, failure: { kind: 'not-found' } })
  })

  it('marks a REPLAYED version conflict as not carrying a current version', async () => {
    /**
     * The server replays a command's stored terminal answer verbatim, so a replayed
     * 409 reports the conflict as it was when the command first ran — which can be
     * several versions behind. Verified at PR #241 head 76c1be3, which asserts a
     * replayed 409 still reports the ORIGINAL currentVersion after the record moved
     * on twice. The screen must not call that number "current".
     */
    stub(() =>
      new Response(JSON.stringify({ conflict: { expectedVersion: 3, currentVersion: 4 } }), {
        status: 409,
        headers: {
          'Content-Type': 'application/problem+json',
          'Idempotency-Replayed': 'true',
          'Idempotency-Original-Correlation-Id': 'corr-original',
        },
      }),
    )
    const outcome = await setLocationActiveState(CONTEXT, PAYLOAD)
    expect(outcome).toMatchObject({
      type: 'failed',
      failure: { kind: 'version-conflict', conflictIsCurrent: false },
    })
  })
})

describe('other definite refusals', () => {
  it.each([
    [400, 'validation'],
    [401, 'unauthenticated'],
    [403, 'forbidden'],
    [404, 'not-found'],
    [415, 'unsupported-media'],
  ])('classifies %i by status alone', async (status, kind) => {
    stub(() => new Response(JSON.stringify({ title: 'x', status }), { status }))
    const outcome = await setLocationActiveState(CONTEXT, PAYLOAD)
    expect(outcome).toMatchObject({ type: 'failed', replayed: false, failure: { kind } })
  })

  it('classifies a 400 with an entirely EMPTY body', async () => {
    // The contract allows a malformed query/body 400 to carry no body at all.
    stub(() => new Response(null, { status: 400 }))
    const outcome = await setLocationActiveState(CONTEXT, PAYLOAD)
    expect(outcome).toMatchObject({ type: 'failed', failure: { kind: 'validation' } })
  })

  it('never reads a localized title to decide anything', async () => {
    // The same Turkish title on a 404 must still classify as not-found, and a
    // conflict-shaped title on a 400 must not become a conflict.
    stub(() =>
      new Response(JSON.stringify({ title: 'Kayıt güncellendi.', conflict: { expectedVersion: 1, currentVersion: 2 } }), {
        status: 400,
      }),
    )
    const outcome = await setLocationActiveState(CONTEXT, PAYLOAD)
    expect(outcome).toMatchObject({ type: 'failed', failure: { kind: 'validation' } })
  })

  it('treats an off-contract status as UNKNOWN, not as a refusal', async () => {
    // A response the contract does not describe is not evidence that the command did
    // not apply, so it must not produce a "Değişiklik uygulanmadı" message.
    stub(() => new Response(JSON.stringify({}), { status: 418 }))
    const outcome = await setLocationActiveState(CONTEXT, PAYLOAD)
    expect(outcome).toMatchObject({
      type: 'unknown',
      unknown: { kind: 'undecodable', status: 418 },
    })
  })
})

describe('unknown outcomes', () => {
  it.each([500, 503])('treats %i as UNKNOWN, not as a refusal', async (status) => {
    // The server answered, but not about our command's result. It may have applied.
    stub(() => new Response(JSON.stringify({}), { status }))
    const outcome = await setLocationActiveState(CONTEXT, PAYLOAD)
    expect(outcome).toMatchObject({
      type: 'unknown',
      unknown: { kind: 'service-unavailable', status },
    })
  })

  it('treats a failed fetch as UNKNOWN — a write may still have landed', async () => {
    vi.stubGlobal('navigator', { onLine: true })
    stub(() => {
      throw new TypeError('network')
    })
    const outcome = await setLocationActiveState(CONTEXT, PAYLOAD)
    expect(outcome).toMatchObject({ type: 'unknown', unknown: { kind: 'network-error' } })
  })

  it('reports offline separately when the browser says so', async () => {
    vi.stubGlobal('navigator', { onLine: false })
    stub(() => {
      throw new TypeError('network')
    })
    const outcome = await setLocationActiveState(CONTEXT, PAYLOAD)
    expect(outcome).toMatchObject({ type: 'unknown', unknown: { kind: 'offline' } })
  })

  it('reports our own cancellation as aborted', async () => {
    const controller = new AbortController()
    controller.abort()
    stub(() => {
      throw new DOMException('aborted', 'AbortError')
    })
    const outcome = await setLocationActiveState(CONTEXT, PAYLOAD, controller.signal)
    expect(outcome).toMatchObject({ type: 'unknown', unknown: { kind: 'aborted' } })
  })
})
