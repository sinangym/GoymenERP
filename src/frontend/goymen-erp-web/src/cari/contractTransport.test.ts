/**
 * The handshake as the TRANSPORT actually performs it.
 *
 * `clientContract.test.ts` pins the rules in isolation. These cases prove the two
 * readers are wired to them: that the declaration goes out, that the acknowledgment is
 * read off the real response, and that readiness follows the decoder's verdict rather
 * than the status line.
 */

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fetchCounterparties } from './api'
import { fetchCounterpartyDetail } from './detailApi'
import {
  API_BASE,
  COUNTERPARTY_CONTRACT_HEADER,
  COUNTERPARTY_CONTRACT_VERSION,
  isCommandContractReady,
  resetContractReadiness,
  type ContractContext,
} from './clientContract'
import { PARTY_ROLE_FLAG } from './contract'

const COMPANY = 'aaaaaaaa-0000-0000-0000-000000000001'
const PARTY = '11111111-1111-1111-1111-111111111111'
const ACCOUNT = 'cccccccc-1111-1111-1111-111111111111'
const CTX: ContractContext = { origin: API_BASE, namespace: 'actor-a single company-a gen-1' }

function listBody() {
  return {
    items: [
      {
        id: PARTY,
        code: 'C-0001',
        kind: 1,
        legalName: 'Alfa Metal Ltd. Şti.',
        tradeName: null,
        isActive: true,
        version: 1,
        companyAccounts: [
          {
            id: ACCOUNT,
            companyId: COMPANY,
            roles: PARTY_ROLE_FLAG.Customer,
            isActive: true,
            version: 1,
          },
        ],
      },
    ],
    nextCursor: null,
    hasMore: false,
    totalCount: null,
    countScope: null,
  }
}

function detailBody() {
  return {
    id: PARTY,
    code: 'C-0001',
    kind: 1,
    legalName: 'Alfa Metal Ltd. Şti.',
    tradeName: null,
    isActive: true,
    version: '1',
    companyAccounts: [
      { id: ACCOUNT, companyId: COMPANY, roles: 0, isActive: true, version: '1' },
    ],
  }
}

/** `acknowledged: false` is exactly how an OLD node answers: 200, no echo. */
function respond(body: unknown, { status = 200, acknowledged = true } = {}) {
  const headers: Record<string, string> = { 'Content-Type': 'application/json' }
  if (acknowledged) headers[COUNTERPARTY_CONTRACT_HEADER] = COUNTERPARTY_CONTRACT_VERSION
  return new Response(JSON.stringify(body), { status, headers })
}

let sentHeaders: Record<string, string>[] = []

function installFetch(make: () => Response) {
  sentHeaders = []
  vi.stubGlobal(
    'fetch',
    vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
      sentHeaders.push((init?.headers as Record<string, string> | undefined) ?? {})
      return make()
    }),
  )
}

beforeEach(() => {
  resetContractReadiness()
  vi.stubGlobal('navigator', { onLine: true })
})
afterEach(() => {
  vi.unstubAllGlobals()
  resetContractReadiness()
})

describe('the list read', () => {
  it('sends the declaration exactly once', async () => {
    installFetch(() => respond(listBody()))
    await fetchCounterparties({ mode: 'single', companyId: COMPANY }, {}, undefined, CTX)

    expect(sentHeaders).toHaveLength(1)
    expect(sentHeaders[0][COUNTERPARTY_CONTRACT_HEADER]).toBe(COUNTERPARTY_CONTRACT_VERSION)
  })

  it('opens the gate on an acknowledged, validated 200', async () => {
    installFetch(() => respond(listBody()))
    const outcome = await fetchCounterparties(
      { mode: 'single', companyId: COMPANY },
      {},
      undefined,
      CTX,
    )

    expect(outcome.type).toBe('ok-single')
    expect(isCommandContractReady(CTX)).toBe(true)
  })

  it('leaves the gate shut against an OLD node that answers 200 without the echo', async () => {
    installFetch(() => respond(listBody(), { acknowledged: false }))
    const outcome = await fetchCounterparties(
      { mode: 'single', companyId: COMPANY },
      {},
      undefined,
      CTX,
    )

    // The read itself is unaffected — an old node still serves the list.
    expect(outcome.type).toBe('ok-single')
    // What must not happen is a command going to that node.
    expect(isCommandContractReady(CTX)).toBe(false)
  })

  it('leaves the gate shut when the body fails validation, acknowledged or not', async () => {
    installFetch(() => respond({ items: 'not-an-array' }))
    const outcome = await fetchCounterparties(
      { mode: 'single', companyId: COMPANY },
      {},
      undefined,
      CTX,
    )

    expect(outcome.type).toBe('malformed')
    expect(isCommandContractReady(CTX)).toBe(false)
  })

  it('REVOKES an earlier grant when the next read in the same context fails', async () => {
    installFetch(() => respond(listBody()))
    await fetchCounterparties({ mode: 'single', companyId: COMPANY }, {}, undefined, CTX)
    expect(isCommandContractReady(CTX)).toBe(true)

    installFetch(() => respond({ code: 'forbidden' }, { status: 403, acknowledged: false }))
    await fetchCounterparties({ mode: 'single', companyId: COMPANY }, {}, undefined, CTX)
    expect(isCommandContractReady(CTX)).toBe(false)
  })

  it('grants nothing when the caller passes no context', async () => {
    installFetch(() => respond(listBody()))
    await fetchCounterparties({ mode: 'single', companyId: COMPANY }, {}, undefined, null)
    expect(isCommandContractReady(CTX)).toBe(false)
  })

  it('never sends role=0, and omits role entirely when nothing is filtered', async () => {
    installFetch(() => respond(listBody()))
    await fetchCounterparties({ mode: 'single', companyId: COMPANY }, {}, undefined, CTX)

    const url = (vi.mocked(fetch).mock.calls[0][0] as string).toString()
    expect(url).not.toContain('role=')
  })
})

describe('the detail read', () => {
  it('sends the declaration and opens the gate on an acknowledged, validated 200', async () => {
    installFetch(() => respond(detailBody()))
    const outcome = await fetchCounterpartyDetail(PARTY, COMPANY, undefined, CTX)

    expect(sentHeaders[0][COUNTERPARTY_CONTRACT_HEADER]).toBe(COUNTERPARTY_CONTRACT_VERSION)
    expect(outcome.type).toBe('ok-single')
    expect(isCommandContractReady(CTX)).toBe(true)
  })

  it('leaves the gate shut against an old node', async () => {
    installFetch(() => respond(detailBody(), { acknowledged: false }))
    const outcome = await fetchCounterpartyDetail(PARTY, COMPANY, undefined, CTX)

    expect(outcome.type).toBe('ok-single')
    expect(isCommandContractReady(CTX)).toBe(false)
  })

  it('leaves the gate shut on a 409 contract refusal that carries the echo', async () => {
    // The filter writes the acknowledgment before the handler runs, so a refusal can
    // carry it. Presence on an error is not readiness.
    installFetch(() => respond({ code: 'client_contract_required' }, { status: 409 }))
    const outcome = await fetchCounterpartyDetail(PARTY, COMPANY, undefined, CTX)

    expect(outcome.type).toBe('http')
    expect(isCommandContractReady(CTX)).toBe(false)
  })
})

describe('a transport failure must not leave the gate open', () => {
  /** Throwing from fetch is how a real network failure reaches the client. */
  function installFailingFetch(error: unknown) {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => {
        throw error
      }),
    )
  }

  async function grant() {
    installFetch(() => respond(listBody()))
    await fetchCounterparties({ mode: 'single', companyId: COMPANY }, {}, undefined, CTX)
    expect(isCommandContractReady(CTX)).toBe(true)
  }

  it('REVOKES readiness when the next read in the same context fails on the network', async () => {
    // Before this, the early transport return skipped the recording entirely, so a
    // grant earned seconds ago outlived the connection it was earned on — and the
    // command gate stayed open against a server we could no longer reach.
    await grant()

    installFailingFetch(new TypeError('Failed to fetch'))
    const outcome = await fetchCounterparties(
      { mode: 'single', companyId: COMPANY },
      {},
      undefined,
      CTX,
    )

    expect(outcome.type).toBe('network-error')
    expect(isCommandContractReady(CTX)).toBe(false)
  })

  it('REVOKES readiness when the next read in the same context finds us offline', async () => {
    await grant()

    vi.stubGlobal('navigator', { onLine: false })
    installFailingFetch(new TypeError('Failed to fetch'))
    const outcome = await fetchCounterparties(
      { mode: 'single', companyId: COMPANY },
      {},
      undefined,
      CTX,
    )

    expect(outcome.type).toBe('offline')
    expect(isCommandContractReady(CTX)).toBe(false)
  })

  it('does the same on the detail read', async () => {
    installFetch(() => respond(detailBody()))
    await fetchCounterpartyDetail(PARTY, COMPANY, undefined, CTX)
    expect(isCommandContractReady(CTX)).toBe(true)

    installFailingFetch(new TypeError('Failed to fetch'))
    await fetchCounterpartyDetail(PARTY, COMPANY, undefined, CTX)
    expect(isCommandContractReady(CTX)).toBe(false)
  })

  it('an ABORTED read changes nothing — it is our own cancellation, not the server', async () => {
    // The shell cancels in-flight reads when the context moves. That cancellation is
    // evidence about us, not about the server, and it must not be read as a failure.
    await grant()

    const controller = new AbortController()
    controller.abort()
    installFailingFetch(new DOMException('Aborted', 'AbortError'))
    const outcome = await fetchCounterparties(
      { mode: 'single', companyId: COMPANY },
      {},
      controller.signal,
      CTX,
    )

    expect(outcome.type).toBe('fence-stale')
    expect(isCommandContractReady(CTX)).toBe(true)
  })

  it('a failed read from an OLD context must not revoke the NEW context', async () => {
    // The single slot is the hazard: a late failure carrying the previous namespace
    // would otherwise overwrite the grant the user is actually working under.
    const OLD: ContractContext = { origin: API_BASE, namespace: 'actor-a single company-a gen-1' }
    const NEW: ContractContext = { origin: API_BASE, namespace: 'actor-a single company-b gen-2' }

    installFetch(() => respond(listBody()))
    await fetchCounterparties({ mode: 'single', companyId: COMPANY }, {}, undefined, NEW)
    expect(isCommandContractReady(NEW)).toBe(true)

    installFailingFetch(new TypeError('Failed to fetch'))
    await fetchCounterparties({ mode: 'single', companyId: COMPANY }, {}, undefined, OLD)

    expect(isCommandContractReady(NEW)).toBe(true)
    expect(isCommandContractReady(OLD)).toBe(false)
  })

  it('a late HTTP failure from an OLD context must not revoke the NEW context either', async () => {
    const OLD: ContractContext = { origin: API_BASE, namespace: 'actor-a single company-a gen-1' }
    const NEW: ContractContext = { origin: API_BASE, namespace: 'actor-a single company-b gen-2' }

    installFetch(() => respond(listBody()))
    await fetchCounterparties({ mode: 'single', companyId: COMPANY }, {}, undefined, NEW)

    installFetch(() => respond({ code: 'forbidden' }, { status: 403, acknowledged: false }))
    await fetchCounterparties({ mode: 'single', companyId: COMPANY }, {}, undefined, OLD)

    expect(isCommandContractReady(NEW)).toBe(true)
  })
})
