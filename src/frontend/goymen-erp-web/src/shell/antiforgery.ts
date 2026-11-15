/**
 * KIM-D31: the antiforgery header of the local account session, sent by ONE shared mechanism.
 *
 * Every module calls `fetch` directly, so the header is added by wrapping the global `fetch` once (in `main.tsx`)
 * instead of in each client: no module carries a second copy of the rule. The wrapper only touches unsafe
 * requests (POST, PUT, PATCH, DELETE) under the API base that carry no `Authorization` header — Bearer requests are
 * exempt on the server too.
 *
 * - The request token comes from `GET <api>/identity/session/antiforgery` and is cached; concurrent writes share one
 *   in-flight read.
 * - A 404 from that route means the API does not run the local account mode: no header is needed and the answer is
 *   cached. Any other unexpected answer is not cached and the write goes out without a header, so the server refuses
 *   it (fail closed) — the wrapper never invents a token.
 * - The token is bound to the signed-in user, so a successful login or logout drops it.
 * - A 400 with `antiforgery_token_invalid` (an expired or foreign token) drops the token and retries ONCE with a
 *   fresh one; a second refusal reaches the caller unchanged.
 */

export const ANTIFORGERY_HEADER = 'X-Goymen-Antiforgery'
export const ANTIFORGERY_PROBLEM_CODE = 'antiforgery_token_invalid'

const UNSAFE_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE'])
const TOKEN_PATH = '/identity/session/antiforgery'
const USER_BINDING_PATHS = ['/identity/session/login', '/identity/session/logout']

type TokenState = { readonly kind: 'not-required' } | { readonly kind: 'token'; readonly value: string }

export interface AntiforgeryFetchOptions {
  /** The API base as the clients use it (`VITE_API_BASE_URL` or `/api/v1`). */
  readonly apiBase: string
  /** The page origin the relative API base resolves against. */
  readonly origin: string
  /** The fetch that really sends the request. */
  readonly baseFetch: typeof fetch
}

function trimTrailingSlash(value: string): string {
  return value.endsWith('/') ? value.slice(0, -1) : value
}

function requestUrl(input: RequestInfo | URL): string {
  if (typeof input === 'string') return input
  if (input instanceof URL) return input.href
  return input.url
}

function requestMethod(input: RequestInfo | URL, init: RequestInit | undefined): string {
  const method = init?.method ?? (input instanceof Request ? input.method : 'GET')
  return method.toUpperCase()
}

function requestHeaders(input: RequestInfo | URL, init: RequestInit | undefined): Headers {
  const headers = new Headers(input instanceof Request ? input.headers : undefined)
  if (init?.headers !== undefined) {
    new Headers(init.headers).forEach((value, name) => headers.set(name, value))
  }
  return headers
}

/** The token body is exactly `{ "requestToken": "<non-blank>" }`; anything else is not a token. */
function decodeToken(body: unknown): string | null {
  if (typeof body !== 'object' || body === null || Array.isArray(body)) return null
  const keys = Object.keys(body)
  if (keys.length !== 1 || keys[0] !== 'requestToken') return null
  const token = (body as { readonly requestToken: unknown }).requestToken
  return typeof token === 'string' && token.trim().length > 0 ? token : null
}

async function isAntiforgeryRefusal(response: Response): Promise<boolean> {
  if (response.status !== 400) return false
  try {
    const body: unknown = await response.clone().json()
    return (
      typeof body === 'object' &&
      body !== null &&
      (body as { readonly code?: unknown }).code === ANTIFORGERY_PROBLEM_CODE
    )
  } catch {
    return false
  }
}

export function createAntiforgeryFetch(options: AntiforgeryFetchOptions): typeof fetch {
  const { baseFetch } = options
  const apiRoot = trimTrailingSlash(new URL(options.apiBase, options.origin).href)
  const tokenUrl = `${apiRoot}${TOKEN_PATH}`
  const userBindingUrls = new Set(USER_BINDING_PATHS.map((path) => `${apiRoot}${path}`))

  let state: TokenState | null = null
  let inFlight: Promise<TokenState | null> | null = null

  async function readToken(): Promise<TokenState | null> {
    const response = await baseFetch(tokenUrl, {
      method: 'GET',
      credentials: 'same-origin',
      cache: 'no-store',
      headers: { Accept: 'application/json' },
    })
    if (response.status === 404) return { kind: 'not-required' }
    if (response.status !== 200) return null
    let body: unknown = null
    try {
      body = await response.json()
    } catch {
      body = null
    }
    const token = decodeToken(body)
    return token === null ? null : { kind: 'token', value: token }
  }

  function currentToken(): Promise<TokenState | null> {
    if (state !== null) return Promise.resolve(state)
    if (inFlight === null) {
      const read = readToken().then(
        (result) => {
          if (inFlight === read) {
            state = result
            inFlight = null
          }
          return result
        },
        (error: unknown) => {
          if (inFlight === read) inFlight = null
          throw error
        },
      )
      inFlight = read
    }
    return inFlight
  }

  function dropToken(): void {
    state = null
    inFlight = null
  }

  function isInScope(url: string): boolean {
    let resolved: string
    try {
      resolved = new URL(url, options.origin).href
    } catch {
      return false
    }
    return resolved === apiRoot || resolved.startsWith(`${apiRoot}/`) || resolved.startsWith(`${apiRoot}?`)
  }

  async function send(input: RequestInfo | URL, init: RequestInit | undefined, headers: Headers): Promise<Response> {
    const token = await currentToken()
    const attemptHeaders = new Headers(headers)
    if (token?.kind === 'token') attemptHeaders.set(ANTIFORGERY_HEADER, token.value)
    // A Request input is cloned per attempt so its body is still readable for the one retry.
    const attemptInput = input instanceof Request ? input.clone() : input
    return baseFetch(attemptInput, { ...init, headers: attemptHeaders })
  }

  return async function antiforgeryFetch(input: RequestInfo | URL, init?: RequestInit): Promise<Response> {
    const url = requestUrl(input)
    const method = requestMethod(input, init)
    if (!UNSAFE_METHODS.has(method) || !isInScope(url)) return baseFetch(input, init)

    const headers = requestHeaders(input, init)
    if (headers.has('Authorization')) return baseFetch(input, init)

    let response = await send(input, init, headers)
    if (await isAntiforgeryRefusal(response)) {
      dropToken()
      response = await send(input, init, headers)
    }

    const resolved = new URL(url, options.origin)
    const path = `${resolved.origin}${resolved.pathname}`
    if (response.ok && userBindingUrls.has(trimTrailingSlash(path))) dropToken()
    return response
  }
}

const INSTALLED = Symbol.for('goymen-erp.antiforgery-fetch')

/** Wraps `globalThis.fetch` once; a second call is a no-op. */
export function installAntiforgeryFetch(apiBase: string): void {
  const current = globalThis.fetch as typeof fetch & { [INSTALLED]?: true }
  if (current[INSTALLED]) return
  const wrapped = createAntiforgeryFetch({
    apiBase,
    origin: globalThis.location.origin,
    baseFetch: current.bind(globalThis),
  }) as typeof fetch & { [INSTALLED]?: true }
  wrapped[INSTALLED] = true
  globalThis.fetch = wrapped
}
