/**
 * Cari command transport — what actually goes on the wire and what comes back
 * as which outcome class. G-002 checks 4, 5, 6, 7 and 10 at the transport
 * layer: one `X-Company-Id` from the caller's context, one caller-supplied
 * `Idempotency-Key`, exactly the contract body fields, exact-201 success,
 * replay headers, and a malformed 201 that is NOT success.
 */

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  buildLinkCandidateQuery,
  createCounterparty,
  fetchLinkCandidates,
  linkCompanyAccount,
  type CreateCounterpartyPayload,
} from './commandApi'
import { PROBLEM_CODE } from './contract'
import {
  API_BASE,
  COUNTERPARTY_CONTRACT_HEADER,
  COUNTERPARTY_CONTRACT_VERSION,
  recordContractRead,
  resetContractReadiness,
} from './clientContract'
import {
  A_ID,
  B_ID,
  PARTY,
  PARTY_2,
  ACC_B,
  candidate,
  candidatePage,
  json,
  jsonWithoutAck,
  NO_ACK_MARKER,
  linkedView,
  mutationView,
  problem,
} from './commandPageHarness'

interface Captured {
  readonly url: string
  readonly init: RequestInit | undefined
}

function stubFetch(respond: (url: string, init?: RequestInit) => Response | Promise<Response>): Captured[] {
  const captured: Captured[] = []
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString()
      captured.push({ url, init })
      const response = await respond(url, init)
      /**
       * A compatible server acknowledges every exchange it accepts, and these cases
       * were written before the protocol existed. Modelling the acknowledging server
       * here keeps them about what they are about; the cases that need a SILENT
       * server say so explicitly and strip it.
       */
      if (
        !response.headers.has(COUNTERPARTY_CONTRACT_HEADER) &&
        !response.headers.has(NO_ACK_MARKER)
      ) {
        response.headers.set(COUNTERPARTY_CONTRACT_HEADER, COUNTERPARTY_CONTRACT_VERSION)
      }
      return response
    }),
  )
  return captured
}

function headersOf(call: Captured): Record<string, string> {
  return (call.init?.headers as Record<string, string> | undefined) ?? {}
}

function bodyOf(call: Captured): unknown {
  return JSON.parse(call.init?.body as string)
}

const KEY = 'f'.repeat(32)
/**
 * Every command now carries the context its write permission was earned in. The
 * transport refuses to send without it, which is the point.
 */
const CONTRACT_A = { origin: API_BASE, namespace: 'actor-a single company-a gen-1' }
const CONTRACT_B = { origin: API_BASE, namespace: 'actor-a single company-b gen-1' }
const CONTEXT = { companyId: A_ID, idempotencyKey: KEY, contract: CONTRACT_A }

/** Stand in for the fresh, validated, acknowledged GET that opens the write gate. */
const CONTRACT_B_GATE = CONTRACT_B
const TARGET_FOR_GATE = { companyId: B_ID, idempotencyKey: KEY, contract: CONTRACT_B }

function openWriteGate(contract = CONTRACT_A) {
  recordContractRead(contract, {
    status: 200,
    bodyValidated: true,
    ack: COUNTERPARTY_CONTRACT_VERSION,
  })
}
const PAYLOAD: CreateCounterpartyPayload = { kind: 1, legalName: 'Örnek Metal Sanayi ve Ticaret A.Ş.', tradeName: null, roles: 3 }

beforeEach(() => {
  vi.stubGlobal('navigator', { onLine: true })
  // These cases are about the command transport, not about the gate; the gate has
  // its own cases below and in contractTransport.test.ts.
  resetContractReadiness()
  // ONE slot, on purpose: readiness is bound to a single current context, so the
  // link cases open their own target-company gate rather than sharing this one.
  openWriteGate()
})
afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
  resetContractReadiness()
})

describe('createCounterparty — POST /api/v1/counterparties', () => {
  it('sends exactly the four contract fields under ONE X-Company-Id and the caller key', async () => {
    const calls = stubFetch(() => json(mutationView(), 201))
    // Whatever else a caller's object carries never reaches the wire.
    const overloaded = { ...PAYLOAD, companyId: B_ID, code: 'CAR-999', reason: 'x' } as CreateCounterpartyPayload
    const outcome = await createCounterparty(CONTEXT, overloaded)

    expect(outcome.type).toBe('created')
    expect(calls).toHaveLength(1)
    expect(calls[0].url).toBe('/api/v1/counterparties')
    expect(calls[0].init?.method).toBe('POST')
    const headers = headersOf(calls[0])
    expect(headers['X-Company-Id']).toBe(A_ID)
    expect(Object.keys(headers).filter((h) => h.toLowerCase() === 'x-company-id')).toHaveLength(1)
    expect(headers['Idempotency-Key']).toBe(KEY)
    expect(headers['Content-Type']).toBe('application/json')
    expect(bodyOf(calls[0])).toEqual({ kind: 1, legalName: PAYLOAD.legalName, tradeName: null, roles: 3 })
    expect(Object.keys(bodyOf(calls[0]) as object)).toEqual(['kind', 'legalName', 'tradeName', 'roles'])
    expect(calls[0].url).not.toContain(A_ID)
  })

  it('a 201 decodes to the verified record; replay headers are surfaced, not hidden', async () => {
    stubFetch(() => json(mutationView(), 201))
    const first = await createCounterparty(CONTEXT, PAYLOAD)
    expect(first).toMatchObject({ type: 'created', replayed: false, originalCorrelationId: null })
    expect(first.type === 'created' && first.data.code).toBe('CAR-000001')
    expect(first.type === 'created' && first.data.version).toBe('1')

    stubFetch(() =>
      json(mutationView(), 201, { 'Idempotency-Replayed': 'TRUE', 'Idempotency-Original-Correlation-Id': ' corr-0001 ' }),
    )
    const replay = await createCounterparty(CONTEXT, PAYLOAD)
    expect(replay).toMatchObject({ type: 'created', replayed: true, originalCorrelationId: 'corr-0001' })
  })

  it('exactly 201 is success: a 200 or 202 with a perfect body is reported as an HTTP outcome', async () => {
    stubFetch(() => json(mutationView(), 200))
    expect(await createCounterparty(CONTEXT, PAYLOAD)).toMatchObject({ type: 'http', status: 200, code: null })
    stubFetch(() => json(mutationView(), 202))
    expect(await createCounterparty(CONTEXT, PAYLOAD)).toMatchObject({ type: 'http', status: 202 })
  })

  it('a 201 whose body fails the mutation decoder is `malformed`, never `created`', async () => {
    // foreign account
    stubFetch(() => json(linkedView(), 201))
    expect(await createCounterparty(CONTEXT, PAYLOAD)).toEqual({ type: 'malformed' })
    // numeric version token
    stubFetch(() => json(mutationView({ version: 1 }), 201))
    expect(await createCounterparty(CONTEXT, PAYLOAD)).toEqual({ type: 'malformed' })
    // not JSON at all
    stubFetch(() => new Response('<html>', { status: 201, headers: { 'Content-Type': 'text/html' } }))
    expect(await createCounterparty(CONTEXT, PAYLOAD)).toEqual({ type: 'malformed' })
  })

  it('no response at all is offline / network-error / aborted — nothing is known about the server side', async () => {
    stubFetch(() => {
      throw new TypeError('Failed to fetch')
    })
    expect(await createCounterparty(CONTEXT, PAYLOAD)).toEqual({ type: 'network-error' })

    vi.stubGlobal('navigator', { onLine: false })
    stubFetch(() => {
      throw new TypeError('Failed to fetch')
    })
    expect(await createCounterparty(CONTEXT, PAYLOAD)).toEqual({ type: 'offline' })

    vi.stubGlobal('navigator', { onLine: true })
    const controller = new AbortController()
    controller.abort()
    stubFetch(() => {
      throw new DOMException('The operation was aborted.', 'AbortError')
    })
    expect(await createCounterparty(CONTEXT, PAYLOAD, controller.signal)).toEqual({ type: 'aborted' })
  })

  it('a definite rejection carries its stable code — or null when the body has none', async () => {
    stubFetch(() => problem(400, PROBLEM_CODE.validationFailed))
    expect(await createCounterparty(CONTEXT, PAYLOAD)).toMatchObject({ type: 'http', status: 400, code: 'validation_failed' })
    stubFetch(() => new Response(null, { status: 400 }))
    expect(await createCounterparty(CONTEXT, PAYLOAD)).toMatchObject({ type: 'http', status: 400, code: null })
    stubFetch(() => problem(409, PROBLEM_CODE.idempotencyConflict))
    expect(await createCounterparty(CONTEXT, PAYLOAD)).toMatchObject({ type: 'http', status: 409, code: 'idempotency_conflict' })
    // An unknown code string is not promoted to a code the UI would branch on.
    stubFetch(() => problem(400, 'made_up_code'))
    expect(await createCounterparty(CONTEXT, PAYLOAD)).toMatchObject({ type: 'http', status: 400, code: null })
  })
})

describe('linkCompanyAccount — POST /api/v1/counterparties/{partyId}/company-account', () => {
  const TARGET = { companyId: B_ID, idempotencyKey: KEY, contract: CONTRACT_B }

  /**
   * The link writes into the TARGET company, so the write gate must have been opened
   * there. Readiness earned in company A does not open a write to company B — the
   * contract says so, and the single slot makes it structural.
   */
  beforeEach(() => {
    openWriteGate(CONTRACT_B)
  })

  it('carries roles only, under the TARGET company, and binds the 201 to the path Party', async () => {
    const calls = stubFetch(() => json(linkedView(), 201))
    const outcome = await linkCompanyAccount(TARGET, PARTY, { roles: 2, reason: 'no', companyId: A_ID } as never)
    expect(outcome.type).toBe('created')
    expect(outcome.type === 'created' && outcome.data.account.companyId).toBe(B_ID)
    expect(outcome.type === 'created' && outcome.data.account.id).toBe(ACC_B)
    expect(calls[0].url).toBe(`/api/v1/counterparties/${PARTY}/company-account`)
    expect(calls[0].init?.method).toBe('POST')
    expect(bodyOf(calls[0])).toEqual({ roles: 2 })
    expect(headersOf(calls[0])['X-Company-Id']).toBe(B_ID)
    expect(headersOf(calls[0])['Idempotency-Key']).toBe(KEY)
  })

  it('a 201 about a different Party, or still carrying the source account, is malformed', async () => {
    stubFetch(() => json(linkedView(2, { id: PARTY_2 }), 201))
    expect(await linkCompanyAccount(TARGET, PARTY, { roles: 2 })).toEqual({ type: 'malformed' })
    // the A (source) account instead of the new B account
    stubFetch(() => json(mutationView(), 201))
    expect(await linkCompanyAccount(TARGET, PARTY, { roles: 2 })).toEqual({ type: 'malformed' })
  })

  it('already_linked and not_found come back as definite HTTP outcomes with their codes', async () => {
    stubFetch(() => problem(409, PROBLEM_CODE.alreadyLinked))
    expect(await linkCompanyAccount(TARGET, PARTY, { roles: 2 })).toMatchObject({ type: 'http', status: 409, code: 'already_linked' })
    stubFetch(() => problem(404, PROBLEM_CODE.notFound))
    expect(await linkCompanyAccount(TARGET, PARTY, { roles: 2 })).toMatchObject({ type: 'http', status: 404, code: 'not_found' })
  })
})

describe('candidate query — only q / pageSize / cursor exist (spec §6.3)', () => {
  it('serializes nothing else, trims q and drops a blank q', () => {
    expect(buildLinkCandidateQuery({})).toBe('')
    expect(buildLinkCandidateQuery({ q: '   ' })).toBe('')
    expect(buildLinkCandidateQuery({ q: '  Örnek  ' })).toBe(`?q=${encodeURIComponent('Örnek')}`)
    expect(buildLinkCandidateQuery({ pageSize: 100, cursor: 'c1' })).toBe('?pageSize=100&cursor=c1')
    const serialized = buildLinkCandidateQuery({ q: 'a', pageSize: 25, cursor: 'x' })
    expect([...new URLSearchParams(serialized.slice(1)).keys()]).toEqual(['q', 'pageSize', 'cursor'])
  })
})

describe('fetchLinkCandidates — GET /api/v1/counterparties/company-account-link-candidates', () => {
  it('is a plain read under the TARGET company: no key, no body, no content-type', async () => {
    const calls = stubFetch(() => json(candidatePage([candidate()])))
    const outcome = await fetchLinkCandidates(B_ID, {})
    expect(outcome.type).toBe('ok-single')
    expect(outcome.type === 'ok-single' && outcome.data.items[0].visibleSourceAccounts[0].companyId).toBe(A_ID)
    expect(calls[0].url).toBe('/api/v1/counterparties/company-account-link-candidates')
    expect(calls[0].init?.method).toBeUndefined()
    expect(calls[0].init?.body).toBeUndefined()
    const headers = headersOf(calls[0])
    expect(headers['X-Company-Id']).toBe(B_ID)
    expect(headers['Idempotency-Key']).toBeUndefined()
    expect(headers['Content-Type']).toBeUndefined()
  })

  it('passes q / cursor through untouched and never adds a parameter of its own', async () => {
    const calls = stubFetch(() => json(candidatePage([])))
    await fetchLinkCandidates(B_ID, { q: 'Örnek', cursor: 'c1' })
    const params = new URL(calls[0].url, 'http://test').searchParams
    expect([...params.keys()]).toEqual(['q', 'cursor'])
    expect(params.get('q')).toBe('Örnek')
    expect(params.get('cursor')).toBe('c1')
  })

  it('a 200 that fails the candidate decoder is malformed; non-200 keeps its status and body', async () => {
    stubFetch(() => json(candidatePage([candidate()], { totalCount: 1 })))
    expect(await fetchLinkCandidates(B_ID, {})).toEqual({ type: 'malformed' })
    stubFetch(() => json(candidatePage([candidate({ companyAccounts: [] })])))
    expect(await fetchLinkCandidates(B_ID, {})).toEqual({ type: 'malformed' })
    stubFetch(() => problem(403, PROBLEM_CODE.forbidden))
    expect(await fetchLinkCandidates(B_ID, {})).toMatchObject({ type: 'http', status: 403 })
    const invalidCursor = await (stubFetch(() => problem(400, PROBLEM_CODE.invalidCursor)), fetchLinkCandidates(B_ID, { cursor: 'x' }))
    expect(invalidCursor).toMatchObject({ type: 'http', status: 400, body: { code: 'invalid_cursor' } })
  })

  it('no response is network-error / offline; an abort is fence-stale', async () => {
    stubFetch(() => {
      throw new TypeError('Failed to fetch')
    })
    expect(await fetchLinkCandidates(B_ID, {})).toEqual({ type: 'network-error' })
    const controller = new AbortController()
    controller.abort()
    stubFetch(() => {
      throw new DOMException('aborted', 'AbortError')
    })
    expect(await fetchLinkCandidates(B_ID, {}, controller.signal)).toEqual({ type: 'fence-stale' })
  })
})

describe('the write gate (spec §4.2)', () => {
  it('sends NOTHING when compatibility has not been proven in this context', async () => {
    // An old node answers a GET with 200 and a valid body and never acknowledges.
    // Posting to it is how a role-less record reaches a server that cannot represent
    // one, so the refusal happens before the request exists.
    resetContractReadiness()
    const calls = stubFetch(() => json(mutationView(), 201))
    const outcome = await createCounterparty(CONTEXT, PAYLOAD)

    expect(outcome).toEqual({ type: 'contract-not-ready' })
    expect(calls).toHaveLength(0)
  })

  it('does not spend readiness earned in ANOTHER company', async () => {
    // Readiness in A never opens a write to B: the link writes into the target
    // company and must be proven there.
    resetContractReadiness()
    openWriteGate(CONTRACT_A)
    const calls = stubFetch(() => json(linkedView(), 201))
    const outcome = await linkCompanyAccount(TARGET_FOR_GATE, PARTY, { roles: 0 })

    expect(outcome).toEqual({ type: 'contract-not-ready' })
    expect(calls).toHaveLength(0)
  })

  it('declares the contract on the command itself, exactly once', async () => {
    openWriteGate()
    const calls = stubFetch(() => json(mutationView(), 201))
    await createCounterparty(CONTEXT, PAYLOAD)

    const sent = calls[0].init?.headers as Record<string, string>
    expect(sent[COUNTERPARTY_CONTRACT_HEADER]).toBe(COUNTERPARTY_CONTRACT_VERSION)
  })

  it('a 201 the server did not acknowledge is UNKNOWN, never created', async () => {
    // The acknowledgment is written before the handler runs, so a genuine 201 from a
    // compatible node always carries it. Without it we cannot tell which node
    // answered — and "created" is the one reading that must not happen.
    openWriteGate()
    stubFetch(() => jsonWithoutAck(mutationView(), 201))
    const outcome = await createCounterparty(CONTEXT, PAYLOAD)

    expect(outcome).toEqual({ type: 'unacknowledged', status: 201 })
  })

  it('omits roles entirely when no classification was chosen', async () => {
    openWriteGate()
    const calls = stubFetch(() => json(mutationView(), 201))
    await createCounterparty(CONTEXT, { ...PAYLOAD, roles: 0 })

    expect(calls[0].init?.body).toBe(
      JSON.stringify({ kind: PAYLOAD.kind, legalName: PAYLOAD.legalName, tradeName: PAYLOAD.tradeName }),
    )
  })

  it('sends `{}` for a roleless link — not an absent body', async () => {
    openWriteGate(CONTRACT_B_GATE)
    const calls = stubFetch(() => json(linkedView(), 201))
    await linkCompanyAccount(TARGET_FOR_GATE, PARTY, { roles: 0 })

    expect(calls[0].init?.body).toBe('{}')
  })
})
