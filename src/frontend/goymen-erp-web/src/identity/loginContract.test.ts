import { describe, expect, it } from 'vitest'
import { LOGIN_RATE_LIMITED_CODE, classifyLogin, classifyLogout, isLoginRateLimited, isProblem } from './loginContract'

/** SENTETIK — the KIM-D29 problem body as the server sends it. */
function rateLimited(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    type: 'https://example.invalid/problems/login-rate-limited',
    title: 'Too many sign-in attempts',
    status: 429,
    detail: 'Try again later.',
    code: LOGIN_RATE_LIMITED_CODE,
    correlationId: 'corr-1',
    ...overrides,
  }
}

function without(body: Record<string, unknown>, key: string): Record<string, unknown> {
  const copy = { ...body }
  delete copy[key]
  return copy
}

describe('classifyLogin — ID-1F sign-in outcomes', () => {
  it('204 is a sign-in', () => {
    expect(classifyLogin(204, null)).toEqual({ kind: 'signed-in' })
  })

  it('401 is ONE refusal whatever the body says (KIM-D2/D4)', () => {
    expect(classifyLogin(401, null)).toEqual({ kind: 'refused' })
    expect(classifyLogin(401, { code: 'account_locked' })).toEqual({ kind: 'refused' })
    expect(classifyLogin(401, { code: 'unknown_user' })).toEqual({ kind: 'refused' })
    expect(classifyLogin(401, rateLimited({ status: 401 }))).toEqual({ kind: 'refused' })
  })

  it('429 with the login_rate_limited problem is the rate-limit state (KIM-D29)', () => {
    expect(classifyLogin(429, rateLimited())).toEqual({ kind: 'rate-limited' })
    expect(classifyLogin(429, rateLimited({ correlationId: null }))).toEqual({ kind: 'rate-limited' })
    expect(classifyLogin(429, without(rateLimited(), 'correlationId'))).toEqual({ kind: 'rate-limited' })
    expect(classifyLogin(429, rateLimited({ conflict: null }))).toEqual({ kind: 'rate-limited' })
  })

  it('429 without that exact problem fails closed', () => {
    expect(classifyLogin(429, null)).toEqual({ kind: 'unexpected' })
    expect(classifyLogin(429, rateLimited({ code: 'something_else' }))).toEqual({ kind: 'unexpected' })
    expect(classifyLogin(429, rateLimited({ status: 400 }))).toEqual({ kind: 'unexpected' })
  })

  it('maps the other statuses the contract names', () => {
    expect(classifyLogin(404, null)).toEqual({ kind: 'not-enabled' })
    expect(classifyLogin(400, null)).toEqual({ kind: 'request-rejected' })
    expect(classifyLogin(415, null)).toEqual({ kind: 'request-rejected' })
    expect(classifyLogin(500, null)).toEqual({ kind: 'service-unavailable' })
    expect(classifyLogin(503, null)).toEqual({ kind: 'service-unavailable' })
  })

  it('a status the contract does not name is unexpected — never a sign-in', () => {
    for (const status of [200, 201, 302, 403, 409, 422]) {
      expect(classifyLogin(status, null), String(status)).toEqual({ kind: 'unexpected' })
    }
  })
})

describe('isProblem / isLoginRateLimited — strict ErpProblemDetailsContract', () => {
  it('accepts the exact shape', () => {
    expect(isProblem(rateLimited(), 429, LOGIN_RATE_LIMITED_CODE)).toBe(true)
    expect(isLoginRateLimited(429, rateLimited())).toBe(true)
  })

  it.each(['type', 'title', 'status', 'detail', 'code'])('refuses a body missing %s', (key) => {
    expect(isLoginRateLimited(429, without(rateLimited(), key))).toBe(false)
  })

  it('refuses an extra key', () => {
    expect(isLoginRateLimited(429, rateLimited({ retryAfter: 60 }))).toBe(false)
  })

  it('refuses mistyped fields', () => {
    expect(isLoginRateLimited(429, rateLimited({ type: 1 }))).toBe(false)
    expect(isLoginRateLimited(429, rateLimited({ title: null }))).toBe(false)
    expect(isLoginRateLimited(429, rateLimited({ detail: 7 }))).toBe(false)
    expect(isLoginRateLimited(429, rateLimited({ status: '429' }))).toBe(false)
    expect(isLoginRateLimited(429, rateLimited({ correlationId: 5 }))).toBe(false)
    expect(isLoginRateLimited(429, rateLimited({ conflict: { field: 'x' } }))).toBe(false)
  })

  it('refuses non-objects', () => {
    for (const value of [null, undefined, 'text', 429, [rateLimited()]]) {
      expect(isLoginRateLimited(429, value)).toBe(false)
    }
  })

  it('only a 429 status counts', () => {
    expect(isLoginRateLimited(400, rateLimited())).toBe(false)
  })
})

describe('classifyLogout — ID-1F sign-out outcomes', () => {
  it('maps every status the contract names', () => {
    expect(classifyLogout(204)).toEqual({ kind: 'signed-out' })
    expect(classifyLogout(401)).toEqual({ kind: 'already-ended' })
    expect(classifyLogout(404)).toEqual({ kind: 'not-enabled' })
    expect(classifyLogout(400)).toEqual({ kind: 'request-rejected' })
    expect(classifyLogout(403)).toEqual({ kind: 'request-rejected' })
    expect(classifyLogout(500)).toEqual({ kind: 'service-unavailable' })
  })

  it('anything else is unexpected — a sign-out is never assumed', () => {
    for (const status of [200, 302, 409, 429]) {
      expect(classifyLogout(status), String(status)).toEqual({ kind: 'unexpected' })
    }
  })
})
