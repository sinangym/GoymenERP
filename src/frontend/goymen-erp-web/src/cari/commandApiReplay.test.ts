/**
 * `replayed` on a NON-SUCCESS outcome is a VERIFIED claim, not a header: the answer
 * must be acknowledged, its body must decode to a stable code, and that status/code
 * must be one the endpoint stores as terminal. Everything else reports `false`, and
 * `settleAttempt` then leaves an unresolved attempt unresolved (Dirac #250 round 2, #1).
 *
 * Real transport (`putVersionedCommand` / `postCommand`) over a stubbed `fetch`.
 */

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createCounterparty, putVersionedCommand, type CommandOutcome } from './commandApi'
import {
  COUNTERPARTY_CONTRACT_HEADER,
  COUNTERPARTY_CONTRACT_VERSION,
  beginContractRead,
  recordContractRead,
  resetContractReadiness,
} from './clientContract'
import { beginAttempt, retryAttempt, settleAttempt } from './commandAttempt'

const A_ID = 'aaaaaaaa-0000-0000-0000-000000000001'
const KEY = 'a'.repeat(32)
const PATH = '/counterparties/11111111-1111-1111-1111-111111111111/company-account/active-state'
const CONTEXT = {
  companyId: A_ID,
  idempotencyKey: KEY,
  contract: { origin: '/api/v1', namespace: 'ns-a' },
}

const ACK = { [COUNTERPARTY_CONTRACT_HEADER]: COUNTERPARTY_CONTRACT_VERSION }
const REPLAY = { 'Idempotency-Replayed': 'true' }

function answer(status: number, body: string | null, headers: Record<string, string>): Response {
  return new Response(body, { status, headers: { 'Content-Type': 'application/json', ...headers } })
}

function problemBody(code: string): string {
  return JSON.stringify({ type: 'about:blank', title: 'Hata', status: 409, code })
}

async function put(response: Response): Promise<CommandOutcome<null>> {
  vi.stubGlobal('fetch', vi.fn(async () => response))
  return await putVersionedCommand(PATH, CONTEXT, '7', { isActive: false, reason: 'r' })
}

beforeEach(() => {
  resetContractReadiness()
  vi.stubGlobal('navigator', { onLine: true })
  // The write gate: a fresh, validated, acknowledged read in THIS context.
  beginContractRead(CONTEXT.contract)
  recordContractRead(CONTEXT.contract, { status: 200, bodyValidated: true, ack: COUNTERPARTY_CONTRACT_VERSION })
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
  resetContractReadiness()
})

describe('verifiedReplay on the PUT transport', () => {
  it('acknowledged, decoded, terminal version_conflict 409 with the marker → replayed', async () => {
    const outcome = await put(answer(409, problemBody('version_conflict'), { ...ACK, ...REPLAY }))
    expect(outcome).toMatchObject({ type: 'http', status: 409, code: 'version_conflict', replayed: true })
  })

  it('acknowledged, decoded, terminal not_found 404 with the marker → replayed', async () => {
    const body = JSON.stringify({ type: 'about:blank', title: 'Yok', status: 404, code: 'not_found' })
    const outcome = await put(answer(404, body, { ...ACK, ...REPLAY }))
    expect(outcome).toMatchObject({ type: 'http', status: 404, code: 'not_found', replayed: true })
  })

  it('the marker WITHOUT the acknowledgment proves nothing → not replayed', async () => {
    const outcome = await put(answer(409, problemBody('version_conflict'), REPLAY))
    expect(outcome).toMatchObject({ type: 'http', status: 409, code: 'version_conflict', replayed: false })
  })

  it('a body that does not decode (404, marker, ack) → not replayed', async () => {
    const outcome = await put(answer(404, 'not json at all', { ...ACK, ...REPLAY }))
    expect(outcome).toMatchObject({ type: 'http', status: 404, code: null, body: null, replayed: false })
  })

  it('a compatibility-coded 409 can never be a stored replay → not replayed', async () => {
    const outcome = await put(answer(409, problemBody('client_contract_required'), { ...ACK, ...REPLAY }))
    expect(outcome).toMatchObject({ type: 'http', status: 409, code: 'client_contract_required', replayed: false })
  })

  it('an idempotency_conflict is a verdict about a DIFFERENT payload → not replayed', async () => {
    const outcome = await put(answer(409, problemBody('idempotency_conflict'), { ...ACK, ...REPLAY }))
    expect(outcome).toMatchObject({ type: 'http', status: 409, replayed: false })
  })

  it('a 500 with the marker → not replayed', async () => {
    const body = JSON.stringify({ type: 'about:blank', title: 'Hata', status: 500, code: 'internal_error' })
    const outcome = await put(answer(500, body, { ...ACK, ...REPLAY }))
    expect(outcome).toMatchObject({ type: 'http', status: 500, replayed: false })
  })

  it('without the marker nothing is a replay, however terminal the answer', async () => {
    const outcome = await put(answer(409, problemBody('version_conflict'), ACK))
    expect(outcome).toMatchObject({ type: 'http', status: 409, code: 'version_conflict', replayed: false })
  })
})

describe('verifiedReplay on the POST transport (create / link share it)', () => {
  it('acknowledged already_linked 409 with the marker → replayed; compatibility → not', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => answer(409, problemBody('already_linked'), { ...ACK, ...REPLAY })))
    const linked = await createCounterparty(CONTEXT, { kind: 1, legalName: 'X', tradeName: null, roles: 0 })
    expect(linked).toMatchObject({ type: 'http', status: 409, code: 'already_linked', replayed: true })

    vi.stubGlobal('fetch', vi.fn(async () => answer(409, problemBody('client_contract_unsupported'), { ...ACK, ...REPLAY })))
    const compat = await createCounterparty(CONTEXT, { kind: 1, legalName: 'X', tradeName: null, roles: 0 })
    expect(compat).toMatchObject({ type: 'http', status: 409, replayed: false })
  })
})

describe('settleAttempt only closes on a VERIFIED replay of a terminal failure', () => {
  const snapshot = {
    key: KEY,
    operation: 'counterparty.account-status:p',
    binding: 'b',
    payload: { expectedVersion: '7', isActive: false, reason: 'r' },
  }
  function resent() {
    const unknown = settleAttempt(beginAttempt<typeof snapshot.payload, null>(snapshot), {
      type: 'http',
      status: 500,
      code: null,
      body: null,
    })
    return retryAttempt(unknown)
  }

  it('an unacknowledged replay 409 on the re-send stays unknown, key kept', async () => {
    const outcome = await put(answer(409, problemBody('version_conflict'), REPLAY))
    const settled = settleAttempt(resent(), outcome)
    expect(settled.phase).toBe('unknown')
    if (settled.phase === 'unknown') expect(settled.snapshot.key).toBe(KEY)
  })

  it('a malformed replay 404 on the re-send stays unknown', async () => {
    const outcome = await put(answer(404, '{broken', { ...ACK, ...REPLAY }))
    expect(settleAttempt(resent(), outcome).phase).toBe('unknown')
  })

  it('a compatibility-coded replay 409 on the re-send stays unknown', async () => {
    const outcome = await put(answer(409, problemBody('client_contract_required'), { ...ACK, ...REPLAY }))
    expect(settleAttempt(resent(), outcome).phase).toBe('unknown')
  })

  it('a verified replay version_conflict on the re-send is terminal', async () => {
    const outcome = await put(answer(409, problemBody('version_conflict'), { ...ACK, ...REPLAY }))
    const settled = settleAttempt(resent(), outcome)
    expect(settled.phase).toBe('failed')
    if (settled.phase === 'failed') expect(settled.failure).toBe('version-conflict')
  })

  it('a replayed-looking answer whose failure kind is not terminal stays unknown even if a transport said replayed', () => {
    // Defence in depth against a future widening of the transport's verification.
    const settled = settleAttempt(resent(), { type: 'http', status: 415, code: null, body: null, replayed: true })
    expect(settled.phase).toBe('unknown')
  })
})
