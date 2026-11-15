import { describe, expect, it, vi } from 'vitest'
import { ANTIFORGERY_HEADER, createAntiforgeryFetch } from './antiforgery'

const ORIGIN = 'https://erp.example.invalid'
const TOKEN_URL = `${ORIGIN}/api/v1/identity/session/antiforgery`

function json(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': status >= 400 ? 'application/problem+json' : 'application/json' },
  })
}

function refusal(): Response {
  return json(400, { code: 'antiforgery_token_invalid', title: 'SENTETIK' })
}

interface Call {
  readonly url: string
  readonly method: string
  readonly token: string | null
  readonly body: string | null
}

/** A synthetic API: the token route hands out SENTETIK-token-1, -2…; writes answer what `write` returns. */
function harness(options: {
  readonly tokenAnswer?: (index: number) => Response
  readonly write?: (call: Call, index: number) => Response
} = {}) {
  const calls: Call[] = []
  let tokens = 0
  let writes = 0
  const baseFetch = vi.fn<typeof fetch>(async (input, init) => {
    // Relative inputs resolve against the synthetic page origin, as the browser would.
    const request = new Request(typeof input === 'string' ? new URL(input, ORIGIN).href : input, init)
    const call: Call = {
      url: request.url,
      method: request.method,
      token: request.headers.get(ANTIFORGERY_HEADER),
      body: request.body === null ? null : await request.text(),
    }
    calls.push(call)
    if (call.url === TOKEN_URL) {
      tokens += 1
      return options.tokenAnswer?.(tokens) ?? json(200, { requestToken: `SENTETIK-token-${tokens}` })
    }
    writes += 1
    return options.write?.(call, writes) ?? json(200, { ok: true })
  })
  const wrapped = createAntiforgeryFetch({ apiBase: '/api/v1', origin: ORIGIN, baseFetch })
  return { calls, wrapped, tokenReads: () => calls.filter((call) => call.url === TOKEN_URL).length }
}

describe('KIM-D31 antiforgery fetch', () => {
  it('adds the token to an unsafe API request and reads it once for concurrent writes', async () => {
    const { calls, wrapped, tokenReads } = harness()

    await Promise.all([
      wrapped('/api/v1/sales/drafts', { method: 'POST', body: '{"a":1}' }),
      wrapped('/api/v1/sales/drafts/1', { method: 'put', body: '{"b":2}' }),
      wrapped(new Request(`${ORIGIN}/api/v1/sales/drafts/1`, { method: 'DELETE' })),
    ])

    expect(tokenReads()).toBe(1)
    const writes = calls.filter((call) => call.url !== TOKEN_URL)
    expect(writes).toHaveLength(3)
    expect(writes.every((call) => call.token === 'SENTETIK-token-1')).toBe(true)
    expect(writes.map((call) => call.body)).toEqual(['{"a":1}', '{"b":2}', null])
  })

  it('leaves reads, Bearer requests and other origins untouched', async () => {
    const { calls, wrapped, tokenReads } = harness()

    await wrapped('/api/v1/sales/drafts')
    await wrapped('/api/v1/sales/drafts', { method: 'POST', headers: { Authorization: 'Bearer SENTETIK' } })
    await wrapped('https://other.example.invalid/api/v1/sales/drafts', { method: 'POST' })
    await wrapped('/api/v10/sales', { method: 'POST' })

    expect(tokenReads()).toBe(0)
    expect(calls.every((call) => call.token === null)).toBe(true)
  })

  it('caches a 404 from the token route as "not required" and sends no header', async () => {
    const { calls, wrapped, tokenReads } = harness({ tokenAnswer: () => new Response(null, { status: 404 }) })

    await wrapped('/api/v1/sales/drafts', { method: 'POST' })
    await wrapped('/api/v1/sales/drafts', { method: 'POST' })

    expect(tokenReads()).toBe(1)
    expect(calls.filter((call) => call.url !== TOKEN_URL).every((call) => call.token === null)).toBe(true)
  })

  it('never invents a token: a malformed token answer is not cached and the refusal reaches the caller', async () => {
    const { calls, wrapped, tokenReads } = harness({
      tokenAnswer: () => json(200, { requestToken: '   ', extra: true }),
      write: (call) => (call.token === null ? refusal() : json(200, {})),
    })

    const response = await wrapped('/api/v1/sales/drafts', { method: 'POST' })

    expect(response.status).toBe(400)
    expect(tokenReads()).toBe(2)
    expect(calls.filter((call) => call.url !== TOKEN_URL)).toHaveLength(2)
    expect(calls.every((call) => call.token === null)).toBe(true)
  })

  it('retries once with a fresh token after an antiforgery refusal and keeps the body', async () => {
    const { calls, wrapped, tokenReads } = harness({
      write: (call) => (call.token === 'SENTETIK-token-1' ? refusal() : json(201, {})),
    })

    const response = await wrapped(
      new Request(`${ORIGIN}/api/v1/sales/drafts`, { method: 'POST', body: '{"line":1}' }),
    )

    expect(response.status).toBe(201)
    expect(tokenReads()).toBe(2)
    const writes = calls.filter((call) => call.url !== TOKEN_URL)
    expect(writes.map((call) => call.token)).toEqual(['SENTETIK-token-1', 'SENTETIK-token-2'])
    expect(writes.map((call) => call.body)).toEqual(['{"line":1}', '{"line":1}'])
  })

  it('does not retry a second time or on another 400', async () => {
    const { calls, wrapped } = harness({
      write: (_, index) => (index <= 2 ? refusal() : json(400, { code: 'validation_failed' })),
    })

    expect((await wrapped('/api/v1/sales/drafts', { method: 'POST' })).status).toBe(400)
    expect((await wrapped('/api/v1/sales/drafts', { method: 'POST' })).status).toBe(400)

    expect(calls.filter((call) => call.url !== TOKEN_URL)).toHaveLength(3)
  })

  it('drops the token after a successful login and logout, not after a failed one', async () => {
    const { calls, wrapped, tokenReads } = harness({
      write: (call) => (call.url.endsWith('/logout') && call.token === 'SENTETIK-token-2' ? json(401, {}) : json(200, {})),
    })

    await wrapped('/api/v1/identity/session/login', { method: 'POST', body: '{}' })
    expect(tokenReads()).toBe(1)
    await wrapped('/api/v1/sales/drafts', { method: 'POST' })
    expect(tokenReads()).toBe(2)
    await wrapped('/api/v1/identity/session/logout', { method: 'POST' })
    await wrapped('/api/v1/sales/drafts', { method: 'POST' })

    expect(tokenReads()).toBe(2)
    expect(calls.filter((call) => call.url !== TOKEN_URL).map((call) => call.token)).toEqual([
      'SENTETIK-token-1',
      'SENTETIK-token-2',
      'SENTETIK-token-2',
      'SENTETIK-token-2',
    ])
  })
})
