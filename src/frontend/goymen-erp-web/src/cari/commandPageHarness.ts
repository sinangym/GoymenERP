/**
 * Test-only harness for the C-02 create and CARI-LINK workspace component
 * tests. Mounts the REAL shell (provider, router, capability gate, boundary) and
 * stubs `fetch` with synthetic fixtures — no business database, no secret, no
 * real host. Not used by production code (same status as ../shell/fixtures.ts).
 *
 * Every request is RECORDED with its method, headers and parsed body, because
 * "did not display it" and "did not send it" are different guarantees and only
 * the second one is authorization.
 */

import { vi } from 'vitest'
import { waitFor } from '@testing-library/react'
import { SHELL_CAPABILITY_CONTRACT_VERSION } from '../shell/api'
import { CAPABILITY } from '../shell/capabilities'
import { PARTY_ROLE_FLAG } from './contract'
import { CARI_CONTROL_CAPABILITY } from './accountStatusContract'

export const A_ID = 'aaaaaaaa-0000-0000-0000-000000000001'
export const B_ID = 'bbbbbbbb-0000-0000-0000-000000000002'
export const PARTY = '11111111-1111-1111-1111-111111111111'
export const PARTY_2 = '22222222-2222-2222-2222-222222222222'
export const ACC_A = 'cccccccc-1111-1111-1111-111111111111'
export const ACC_B = 'dddddddd-2222-2222-2222-222222222222'
export const ACC_A2 = 'eeeeeeee-2222-2222-2222-222222222222'

export const IDENTITY = {
  userId: '99999999-9999-9999-9999-999999999999',
  displayName: 'Kullanıcı A',
  email: null,
  allowedCompanyIds: [A_ID, B_ID],
}

export const DIRECTORY = [
  { id: A_ID, code: 'GDC', legalName: 'Göymen Demir Çelik', isPlaceholder: false },
  { id: B_ID, code: 'GSP', legalName: 'Göymen Sac Profil', isPlaceholder: false },
]

export const LEGAL_NAME = 'Örnek Metal Sanayi ve Ticaret A.Ş.'
export const TRADE_NAME = 'Örnek Metal'

/**
 * A response from a COMPATIBLE server.
 *
 * The acknowledgment is included by default because that is what the server these
 * cases describe actually does: the compatibility filter echoes the declaration on
 * every exchange it accepts. A case that needs a SILENT server — an old node — says
 * so by passing the header explicitly as an empty value via `headers`.
 */
export function json(body: unknown, status = 200, headers: Record<string, string> = {}): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: {
      'Content-Type': 'application/json',
      'X-ERP-Counterparty-Contract': 'optional-trade-roles-v1',
      ...headers,
    },
  })
}

/**
 * The marker that says "this response is deliberately silent".
 *
 * A harness that adds the acknowledgment by default needs a way to express its
 * absence; without one, the old-node case cannot be written at all.
 */
export const NO_ACK_MARKER = 'X-Test-Silent-Server'

/** The same body from an OLD node: no acknowledgment, so no write may follow. */
export function jsonWithoutAck(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', [NO_ACK_MARKER]: 'true' },
  })
}

/** A stable-code ProblemDetails body, as the server sends it. */
export function problem(status: number, code: string | null, title = 'Hata'): Response {
  const body: Record<string, unknown> = { type: 'about:blank', title, status, detail: 'Yerel ayrıntı metni' }
  if (code !== null) body.code = code
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  })
}

/** `CounterpartyMutationView` / `CounterpartyDetailView` shape (string versions). */
export function mutationView(over: Record<string, unknown> = {}) {
  return {
    id: PARTY,
    code: 'CAR-000001',
    kind: 1,
    legalName: LEGAL_NAME,
    tradeName: TRADE_NAME,
    isActive: true,
    version: '1',
    companyAccounts: [
      {
        id: ACC_A,
        companyId: A_ID,
        roles: PARTY_ROLE_FLAG.Customer | PARTY_ROLE_FLAG.Supplier,
        isActive: true,
        version: '1',
      },
    ],
    ...over,
  }
}

/** The same Party linked into B: same id/code, only the B account. */
export function linkedView(roles: number = PARTY_ROLE_FLAG.Supplier, over: Record<string, unknown> = {}) {
  return mutationView({
    companyAccounts: [{ id: ACC_B, companyId: B_ID, roles, isActive: true, version: '1' }],
    ...over,
  })
}

/**
 * `CounterpartyContactInformationView` — the account-scoped operational block.
 * Shape and example values from the pinned Backend handoff
 * (`CARI_CONTACT_INFORMATION_BACKEND_HANDOFF_v1.md` at `9df7f40`).
 */
export function contactView(
  information: Record<string, unknown> = {},
  over: Record<string, unknown> = {},
) {
  return {
    partyId: PARTY,
    accountId: ACC_A,
    companyId: A_ID,
    version: '1',
    isActive: true,
    information: {
      phone: null,
      phoneExtension: null,
      email: null,
      website: null,
      addresses: [],
      contacts: [],
      ...information,
    },
    ...over,
  }
}

export function contactAddress(over: Record<string, unknown> = {}) {
  return {
    id: 'a1111111-1111-4111-8111-111111111111',
    version: '1',
    isActive: true,
    purpose: 'billing',
    countryCode: 'TR',
    line1: 'Merkez Cadde 1',
    line2: null,
    city: 'İstanbul',
    district: 'Şişli',
    postalCode: null,
    ...over,
  }
}

export function contactChannel(over: Record<string, unknown> = {}) {
  return {
    id: 'd1111111-1111-4111-8111-111111111111',
    version: '1',
    isActive: true,
    kind: 'phone',
    value: '0532 000 00 00',
    extension: null,
    ...over,
  }
}

export function contactPerson(over: Record<string, unknown> = {}) {
  return {
    id: 'c1111111-1111-4111-8111-111111111111',
    version: '1',
    isActive: true,
    displayName: 'Ayşe Yılmaz',
    jobTitle: null,
    purpose: 'Operations',
    channels: [contactChannel()],
    ...over,
  }
}

/** `CounterpartyLinkCandidateItem` shape — visibleSourceAccounts, no Party version. */
export function candidate(over: Record<string, unknown> = {}) {
  return {
    id: PARTY,
    code: 'CAR-000001',
    kind: 1,
    legalName: LEGAL_NAME,
    tradeName: TRADE_NAME,
    isActive: true,
    visibleSourceAccounts: [
      {
        id: ACC_A,
        companyId: A_ID,
        roles: PARTY_ROLE_FLAG.Customer | PARTY_ROLE_FLAG.Supplier,
        isActive: true,
        version: '1',
      },
    ],
    ...over,
  }
}

export function candidatePage(items: unknown[], over: Record<string, unknown> = {}) {
  return { items, nextCursor: null, hasMore: false, ...over }
}

export interface RecordedCall {
  readonly url: string
  readonly method: string
  readonly headers: Record<string, string>
  readonly body: unknown
  readonly companyId: string | null
  readonly idempotencyKey: string | null
  readonly signal: AbortSignal | undefined
}

type Responder = (call: RecordedCall) => Response | Promise<Response>

export interface HarnessConfig {
  /** Granted capabilities, per company. Default: every Cari capability. */
  capabilities?: readonly string[] | ((companyId: string | null) => readonly string[])
  create?: Responder
  link?: Responder
  candidates?: Responder
  /**
   * The plain list read — which is also the create form's compatibility probe.
   * Configurable because "what the probe found" is now a behaviour worth testing:
   * an old node, a 403, a 503 and a malformed body must each produce a different
   * screen, not one shared "incompatible client" verdict.
   */
  counterparties?: Responder
  detail?: Responder
  /** The account-scoped operational contact block: `GET`/`PUT` the C-06 editor uses. */
  contact?: Responder
  /** The selected account's reasoned active-state `PUT` the C-03 status action uses. */
  activeState?: Responder
}

export const ALL_CARI_CAPABILITIES: readonly string[] = [
  CAPABILITY.cariPartiesList,
  CAPABILITY.cariPartiesCreate,
  CAPABILITY.cariAccountsLink,
  CAPABILITY.cariPartiesDetail,
  CAPABILITY.cariAccountsContactUpdate,
  // The status CONTROL — proposed, never published by the server today. Granted here by
  // default so the action's behaviour can be tested; the "not granted" cases say so.
  CARI_CONTROL_CAPABILITY.accountStatusUpdate,
]

/** A compatible server's 204: no body, the acknowledgment, optionally a replay marker. */
export function noContent(headers: Record<string, string> = {}): Response {
  return new Response(null, {
    status: 204,
    headers: { 'X-ERP-Counterparty-Contract': 'optional-trade-roles-v1', ...headers },
  })
}

export const CREATE_RE = /\/counterparties$/
export const LINK_RE = /\/counterparties\/[^/?]+\/company-account$/
export const CANDIDATES_RE = /\/counterparties\/company-account-link-candidates(\?|$)/
/** A single Party read; the candidates collection path is excluded explicitly. */
export const DETAIL_RE = /\/counterparties\/(?!company-account-link-candidates)[^/?]+$/
export const CONTACT_RE = /\/counterparties\/[^/?]+\/contact-information$/
export const ACTIVE_STATE_RE = /\/counterparties\/[^/?]+\/company-account\/active-state$/

export function installFetch(config: HarnessConfig = {}) {
  const calls: RecordedCall[] = []
  const resolveCapabilities = (companyId: string | null): readonly string[] => {
    const value = config.capabilities ?? ALL_CARI_CAPABILITIES
    return typeof value === 'function' ? value(companyId) : value
  }

  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString()
      const method = (init?.method ?? 'GET').toUpperCase()
      const headers = (init?.headers as Record<string, string> | undefined) ?? {}
      let body: unknown = null
      if (typeof init?.body === 'string') {
        try {
          body = JSON.parse(init.body)
        } catch {
          body = init.body
        }
      }
      const call: RecordedCall = {
        url,
        method,
        headers,
        body,
        companyId: headers['X-Company-Id'] ?? null,
        idempotencyKey: headers['Idempotency-Key'] ?? null,
        signal: init?.signal ?? undefined,
      }
      calls.push(call)

      if (url.includes('/identity/me')) return json(IDENTITY)
      if (url.includes('/organization/companies')) return json(DIRECTORY)
      if (url.includes('/shell/capabilities')) {
        return json({
          contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION,
          generation: `gen-${call.companyId?.slice(-1) ?? 'x'}`,
          capabilities: resolveCapabilities(call.companyId),
        })
      }
      // Order matters: the candidates path would otherwise match the detail pattern.
      if (CANDIDATES_RE.test(url)) {
        return (config.candidates ?? (() => json(candidatePage([candidate()]))))(call)
      }
      if (method === 'POST' && LINK_RE.test(url)) {
        return (config.link ?? (() => json(linkedView(), 201)))(call)
      }
      if (method === 'POST' && CREATE_RE.test(url)) {
        return (config.create ?? (() => json(mutationView(), 201)))(call)
      }
      // Deeper than the link path, so it is matched first; a 204 by default, because
      // that is what a compatible server answers an applied status change with.
      if (method === 'PUT' && ACTIVE_STATE_RE.test(url)) {
        return (config.activeState ?? (() => noContent()))(call)
      }
      // Before the detail pattern for the same reason the candidates path is: a deeper
      // segment must not be read as a party id.
      if (CONTACT_RE.test(url)) {
        return (config.contact ?? (() => json(contactView())))(call)
      }
      if (method === 'GET' && DETAIL_RE.test(url)) {
        return (config.detail ?? (() => json(mutationView())))(call)
      }
      if (url.includes('/counterparties')) {
        return (
          config.counterparties ??
          (() => json({ items: [], nextCursor: null, hasMore: false, totalCount: null, countScope: null }))
        )(call)
      }
      throw new Error(`unexpected fetch: ${method} ${url}`)
    }),
  )

  return {
    calls,
    creates: () => calls.filter((c) => c.method === 'POST' && CREATE_RE.test(c.url)),
    links: () => calls.filter((c) => c.method === 'POST' && LINK_RE.test(c.url)),
    candidateReads: () => calls.filter((c) => c.method === 'GET' && CANDIDATES_RE.test(c.url)),
    detailReads: () => calls.filter((c) => c.method === 'GET' && DETAIL_RE.test(c.url)),
    contactReads: () => calls.filter((c) => c.method === 'GET' && CONTACT_RE.test(c.url)),
    contactUpdates: () => calls.filter((c) => c.method === 'PUT' && CONTACT_RE.test(c.url)),
    activeStateUpdates: () => calls.filter((c) => c.method === 'PUT' && ACTIVE_STATE_RE.test(c.url)),
    capabilityReads: () => calls.filter((c) => c.url.includes('/shell/capabilities')),
  }
}

export { renderAt } from './commandPageRender'

/** Start the shell in company B (the link workspace's usual target). */
export function preferCompany(companyId: string) {
  window.localStorage.setItem('goymen-company-id', companyId)
}

/**
 * Four sequential round-trips (identity, directory, capability, feature) before
 * anything renders; the default 1s wait is not a fair budget under parallel load.
 * Same reasoning as CariDetailPage.test.tsx.
 */
export const CHAIN = { timeout: 5000 } as const

export function stateBlock(kind: string): HTMLElement | null {
  return document.querySelector<HTMLElement>(`[data-state="${kind}"]`)
}

export async function findStateBlock(kind: string): Promise<HTMLElement> {
  return await waitFor(() => {
    const node = stateBlock(kind)
    if (node === null) throw new Error(`state block "${kind}" not rendered`)
    return node
  }, CHAIN)
}

/** A deferred response the test releases by hand — delivered, not prevented. */
export function held(): { promise: Promise<Response>; release: (response: Response) => void } {
  let release: (response: Response) => void = () => {}
  const promise = new Promise<Response>((resolve) => {
    release = resolve
  })
  return { promise, release }
}

export function settle(ms = 50): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}
