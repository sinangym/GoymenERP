/**
 * `replayed` on a NON-SUCCESS outcome is a VERIFIED claim bound to the SELECTED
 * endpoint: the answer must be acknowledged, its body must decode to a stable code,
 * and that status/code must be one THIS endpoint stores as terminal. Everything else
 * reports `false`, and `settleAttempt` then leaves an unresolved attempt unresolved
 * (Dirac #250 rounds 2 and 3).
 *
 * Real transport — the real callers (`createCounterparty`, `linkCompanyAccount`,
 * `setCompanyAccountActiveState`, `updateContactInformation`) — over a stubbed `fetch`.
 * Tables per `CounterpartyEndpoints*.cs` at `main@fc14554d`: create stores only 201;
 * link 404 not_found / 409 already_linked; both versioned updates 404 not_found /
 * 409 version_conflict.
 */

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  createCounterparty,
  linkCompanyAccount,
  setCompanyAccountActiveState,
  type CommandOutcome,
} from './commandApi'
import { updateContactInformation } from './contactInformationApi'
import {
  COUNTERPARTY_CONTRACT_HEADER,
  COUNTERPARTY_CONTRACT_VERSION,
  beginContractRead,
  recordContractRead,
  resetContractReadiness,
} from './clientContract'
import { beginAttempt, retryAttempt, settleAttempt } from './commandAttempt'

const A_ID = 'aaaaaaaa-0000-0000-0000-000000000001'
const PARTY = '11111111-1111-1111-1111-111111111111'
const KEY = 'a'.repeat(32)
const CONTEXT = {
  companyId: A_ID,
  idempotencyKey: KEY,
  contract: { origin: '/api/v1', namespace: 'ns-a' },
}

const ACK = { [COUNTERPARTY_CONTRACT_HEADER]: COUNTERPARTY_CONTRACT_VERSION }
const REPLAY = { 'Idempotency-Replayed': 'true' }
const VERIFIED = { ...ACK, ...REPLAY }

function answer(status: number, body: string | null, headers: Record<string, string>): Response {
  return new Response(body, { status, headers: { 'Content-Type': 'application/json', ...headers } })
}

function problemBody(status: number, code: string): string {
  return JSON.stringify({ type: 'about:blank', title: 'Hata', status, code })
}

/** An acknowledged, replay-marked, well-formed failure — verified iff the endpoint stores it. */
function replayed(status: number, code: string): Response {
  return answer(status, problemBody(status, code), VERIFIED)
}

function serve(response: Response) {
  vi.stubGlobal('fetch', vi.fn(async () => response))
}

const statusPut = () =>
  setCompanyAccountActiveState(CONTEXT, PARTY, '7', { isActive: false, reason: 'r' })
const contactPut = () => updateContactInformation(CONTEXT, PARTY, '7', { phone: '001' })
const create = () => createCounterparty(CONTEXT, { kind: 1, legalName: 'X', tradeName: null, roles: 0 })
const link = () => linkCompanyAccount(CONTEXT, PARTY, { roles: 0 })

async function outcomeOf(call: () => Promise<CommandOutcome<unknown>>, response: Response) {
  serve(response)
  return await call()
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

describe('the common floor — no endpoint believes these', () => {
  it('the marker WITHOUT the acknowledgment proves nothing', async () => {
    const outcome = await outcomeOf(statusPut, answer(409, problemBody(409, 'version_conflict'), REPLAY))
    expect(outcome).toMatchObject({ type: 'http', status: 409, code: 'version_conflict', replayed: false })
  })

  it('a body that does not decode (404, marker, ack)', async () => {
    const outcome = await outcomeOf(statusPut, answer(404, 'not json at all', VERIFIED))
    expect(outcome).toMatchObject({ type: 'http', status: 404, code: null, body: null, replayed: false })
  })

  it('a compatibility-coded 409 is never stored', async () => {
    expect(await outcomeOf(statusPut, replayed(409, 'client_contract_required'))).toMatchObject({ replayed: false })
    expect(await outcomeOf(link, replayed(409, 'client_contract_unsupported'))).toMatchObject({ replayed: false })
  })

  it('an idempotency_conflict is a verdict about a DIFFERENT payload', async () => {
    expect(await outcomeOf(statusPut, replayed(409, 'idempotency_conflict'))).toMatchObject({ replayed: false })
    expect(await outcomeOf(link, replayed(409, 'idempotency_conflict'))).toMatchObject({ replayed: false })
  })

  it('a 500 with the marker', async () => {
    expect(await outcomeOf(statusPut, replayed(500, 'internal_error'))).toMatchObject({ status: 500, replayed: false })
    expect(await outcomeOf(create, replayed(500, 'internal_error'))).toMatchObject({ status: 500, replayed: false })
  })

  it('without the marker nothing is a replay, however terminal the answer', async () => {
    const outcome = await outcomeOf(statusPut, answer(409, problemBody(409, 'version_conflict'), ACK))
    expect(outcome).toMatchObject({ type: 'http', status: 409, code: 'version_conflict', replayed: false })
  })
})

describe('create — stores only its 201: no failure of a create is a replay', () => {
  it('already_linked, not_found and version_conflict replays are all unverified', async () => {
    expect(await outcomeOf(create, replayed(409, 'already_linked'))).toMatchObject({ status: 409, replayed: false })
    expect(await outcomeOf(create, replayed(404, 'not_found'))).toMatchObject({ status: 404, replayed: false })
    expect(await outcomeOf(create, replayed(409, 'version_conflict'))).toMatchObject({ status: 409, replayed: false })
  })
})

describe('link — its own table: 404 not_found, 409 already_linked', () => {
  it('a verified already_linked replay from the REAL link caller', async () => {
    const outcome = await outcomeOf(link, replayed(409, 'already_linked'))
    expect(outcome).toMatchObject({ type: 'http', status: 409, code: 'already_linked', replayed: true })
  })

  it('a verified not_found replay', async () => {
    const outcome = await outcomeOf(link, replayed(404, 'not_found'))
    expect(outcome).toMatchObject({ type: 'http', status: 404, code: 'not_found', replayed: true })
  })

  it('a version_conflict is not something a link stores → unverified', async () => {
    const outcome = await outcomeOf(link, replayed(409, 'version_conflict'))
    expect(outcome).toMatchObject({ type: 'http', status: 409, code: 'version_conflict', replayed: false })
  })
})

describe('the two versioned updates — their own table: 404 not_found, 409 version_conflict', () => {
  it('active-state: verified version_conflict and not_found replays', async () => {
    expect(await outcomeOf(statusPut, replayed(409, 'version_conflict'))).toMatchObject({
      type: 'http',
      status: 409,
      code: 'version_conflict',
      replayed: true,
    })
    expect(await outcomeOf(statusPut, replayed(404, 'not_found'))).toMatchObject({ status: 404, replayed: true })
  })

  it('contact-information: the same pair, through the REAL contact caller', async () => {
    expect(await outcomeOf(contactPut, replayed(409, 'version_conflict'))).toMatchObject({
      status: 409,
      code: 'version_conflict',
      replayed: true,
    })
    expect(await outcomeOf(contactPut, replayed(404, 'not_found'))).toMatchObject({ status: 404, replayed: true })
  })

  it('an already_linked is not something an update stores → unverified, on both PUT callers', async () => {
    expect(await outcomeOf(statusPut, replayed(409, 'already_linked'))).toMatchObject({
      status: 409,
      code: 'already_linked',
      replayed: false,
    })
    expect(await outcomeOf(contactPut, replayed(409, 'already_linked'))).toMatchObject({ status: 409, replayed: false })
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
    const outcome = await outcomeOf(statusPut, answer(409, problemBody(409, 'version_conflict'), REPLAY))
    const settled = settleAttempt(resent(), outcome as CommandOutcome<null>)
    expect(settled.phase).toBe('unknown')
    if (settled.phase === 'unknown') expect(settled.snapshot.key).toBe(KEY)
  })

  it('a malformed replay 404 on the re-send stays unknown', async () => {
    const outcome = await outcomeOf(statusPut, answer(404, '{broken', VERIFIED))
    expect(settleAttempt(resent(), outcome as CommandOutcome<null>).phase).toBe('unknown')
  })

  it('a compatibility-coded replay 409 on the re-send stays unknown', async () => {
    const outcome = await outcomeOf(statusPut, replayed(409, 'client_contract_required'))
    expect(settleAttempt(resent(), outcome as CommandOutcome<null>).phase).toBe('unknown')
  })

  it('a cross-endpoint replay (already_linked on the status PUT) on the re-send stays unknown', async () => {
    const outcome = await outcomeOf(statusPut, replayed(409, 'already_linked'))
    expect(settleAttempt(resent(), outcome as CommandOutcome<null>).phase).toBe('unknown')
  })

  it('a verified replay version_conflict on the re-send is terminal', async () => {
    const outcome = await outcomeOf(statusPut, replayed(409, 'version_conflict'))
    const settled = settleAttempt(resent(), outcome as CommandOutcome<null>)
    expect(settled.phase).toBe('failed')
    if (settled.phase === 'failed') expect(settled.failure).toBe('version-conflict')
  })

  it('a replayed-looking answer whose failure kind is not terminal stays unknown even if a transport said replayed', () => {
    // Defence in depth against a future widening of the transport's verification.
    const settled = settleAttempt(resent(), { type: 'http', status: 415, code: null, body: null, replayed: true })
    expect(settled.phase).toBe('unknown')
  })
})
