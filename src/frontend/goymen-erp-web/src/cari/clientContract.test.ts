/**
 * The client-contract handshake, pinned against the exact backend source
 * (PR #230 `080e7a1` + its accepted follow-up `880cae8`).
 *
 * These cases exist because every one of them is a way a client could talk itself into
 * writing to a server that never agreed to the contract.
 */

import { afterEach, describe, expect, it } from 'vitest'
import {
  COUNTERPARTY_CONTRACT_HEADER,
  COUNTERPARTY_CONTRACT_VERSION,
  commandAckOutcome,
  contractAckOf,
  contractContextKey,
  contractRequestHeader,
  isCommandContractReady,
  recordContractRead,
  resetContractReadiness,
  mayRetrySameKey,
  sameCommandAttempt,
  settlesUnknownOutcome,
  type ContractContext,
} from './clientContract'

const CTX: ContractContext = { origin: '/api/v1', namespace: 'actor-a single company-a gen-1 rev-1' }
const OTHER_COMPANY: ContractContext = {
  origin: '/api/v1',
  namespace: 'actor-a single company-b gen-1 rev-1',
}
const OTHER_ORIGIN: ContractContext = { origin: 'https://other.example', namespace: CTX.namespace }

function res(headers: Record<string, string> = {}): Pick<Response, 'headers'> {
  return { headers: new Headers(headers) }
}

const ACK = { [COUNTERPARTY_CONTRACT_HEADER]: COUNTERPARTY_CONTRACT_VERSION }

afterEach(() => {
  resetContractReadiness()
})

describe('the declaration this client sends', () => {
  it('is the exact single value the server pins', () => {
    expect(contractRequestHeader()).toEqual({
      'X-ERP-Counterparty-Contract': 'optional-trade-roles-v1',
    })
  })

  it('hands out a fresh object, so it cannot be accumulated into a duplicate', () => {
    // The server refuses an empty, duplicated or combined declaration with
    // `client_contract_unsupported`. A shared mutable object is how a caller ends up
    // building one by accident.
    const first = contractRequestHeader()
    first[COUNTERPARTY_CONTRACT_HEADER] = 'tampered'
    expect(contractRequestHeader()[COUNTERPARTY_CONTRACT_HEADER]).toBe(
      COUNTERPARTY_CONTRACT_VERSION,
    )
  })
})

describe('reading the acknowledgment', () => {
  it('accepts only the exact value', () => {
    expect(contractAckOf(res(ACK))).toBe(COUNTERPARTY_CONTRACT_VERSION)
    expect(contractAckOf(res({}))).toBeNull()
    expect(contractAckOf(res({ [COUNTERPARTY_CONTRACT_HEADER]: 'optional-trade-roles-v2' }))).toBeNull()
    expect(contractAckOf(res({ [COUNTERPARTY_CONTRACT_HEADER]: 'Optional-Trade-Roles-V1' }))).toBeNull()
    // Surrounding whitespace is NOT a distinguishing case: the Fetch spec strips
    // leading and trailing HTTP whitespace from header values, so ' v1' and 'v1'
    // are the same value by the time any client can look at them. Measured, not
    // assumed — asserting a rejection here would have been asserting against the
    // platform.
    expect(contractAckOf(res({ [COUNTERPARTY_CONTRACT_HEADER]: ' optional-trade-roles-v1 ' }))).toBe(
      COUNTERPARTY_CONTRACT_VERSION,
    )
    expect(contractAckOf(res({ [COUNTERPARTY_CONTRACT_HEADER]: 'optional-trade-roles-v1x' }))).toBeNull()
  })

  it('treats a repeated acknowledgment as absent', () => {
    // `Headers` folds repeats into a comma-joined value. The server promises exactly
    // one; two is not a stronger yes.
    const headers = new Headers()
    headers.append(COUNTERPARTY_CONTRACT_HEADER, COUNTERPARTY_CONTRACT_VERSION)
    headers.append(COUNTERPARTY_CONTRACT_HEADER, COUNTERPARTY_CONTRACT_VERSION)
    expect(contractAckOf({ headers })).toBeNull()
  })
})

describe('what may open the command gate', () => {
  it('is closed before anything has been read', () => {
    expect(isCommandContractReady(CTX)).toBe(false)
    expect(isCommandContractReady(null)).toBe(false)
  })

  it('opens on a successful, body-validated, acknowledged read', () => {
    recordContractRead(CTX, { status: 200, bodyValidated: true, ack: COUNTERPARTY_CONTRACT_VERSION })
    expect(isCommandContractReady(CTX)).toBe(true)
  })

  it('stays closed for an OLD server: 200, validated, but no acknowledgment', () => {
    // The whole point of the handshake. An old server ignores the request header and
    // answers normally; the client must send no command to it.
    recordContractRead(CTX, { status: 200, bodyValidated: true, ack: null })
    expect(isCommandContractReady(CTX)).toBe(false)
  })

  it('stays closed when the body failed validation, even with the acknowledgment', () => {
    // A server we could not read is not a server we may write to.
    recordContractRead(CTX, { status: 200, bodyValidated: false, ack: COUNTERPARTY_CONTRACT_VERSION })
    expect(isCommandContractReady(CTX)).toBe(false)
  })

  it('stays closed on an ERROR that carries the acknowledgment', () => {
    // The filter writes the ack before the handler runs, so it rides on handler
    // errors too. Presence on an error is not readiness and not success.
    for (const status of [400, 403, 404, 409, 500]) {
      resetContractReadiness()
      recordContractRead(CTX, {
        status,
        bodyValidated: false,
        ack: COUNTERPARTY_CONTRACT_VERSION,
      })
      expect(isCommandContractReady(CTX)).toBe(false)
    }
  })
})

describe('what the gate is bound to', () => {
  it('does not carry across a company change', () => {
    recordContractRead(CTX, { status: 200, bodyValidated: true, ack: COUNTERPARTY_CONTRACT_VERSION })
    expect(isCommandContractReady(OTHER_COMPANY)).toBe(false)
  })

  it('does not carry across an API origin change', () => {
    recordContractRead(CTX, { status: 200, bodyValidated: true, ack: COUNTERPARTY_CONTRACT_VERSION })
    expect(isCommandContractReady(OTHER_ORIGIN)).toBe(false)
  })

  it('does not carry across an actor, generation or revision change', () => {
    recordContractRead(CTX, { status: 200, bodyValidated: true, ack: COUNTERPARTY_CONTRACT_VERSION })
    for (const namespace of [
      'actor-b single company-a gen-1 rev-1',
      'actor-a single company-a gen-2 rev-1',
      'actor-a single company-a gen-1 rev-2',
    ]) {
      expect(isCommandContractReady({ origin: CTX.origin, namespace })).toBe(false)
    }
  })

  it('is REVOKED when a later read in the same context stops acknowledging', () => {
    // Not merely "not renewed". A server that has stopped acknowledging, or started
    // failing, must not leave an earlier yes standing.
    recordContractRead(CTX, { status: 200, bodyValidated: true, ack: COUNTERPARTY_CONTRACT_VERSION })
    expect(isCommandContractReady(CTX)).toBe(true)

    recordContractRead(CTX, { status: 200, bodyValidated: true, ack: null })
    expect(isCommandContractReady(CTX)).toBe(false)
  })

  it('is REVOKED when a later read in the same context fails', () => {
    recordContractRead(CTX, { status: 200, bodyValidated: true, ack: COUNTERPARTY_CONTRACT_VERSION })
    recordContractRead(CTX, { status: 503, bodyValidated: false, ack: null })
    expect(isCommandContractReady(CTX)).toBe(false)
  })

  it('keeps one slot, so a grant for a context the user left is unreachable', () => {
    recordContractRead(CTX, { status: 200, bodyValidated: true, ack: COUNTERPARTY_CONTRACT_VERSION })
    recordContractRead(OTHER_COMPANY, {
      status: 200,
      bodyValidated: true,
      ack: COUNTERPARTY_CONTRACT_VERSION,
    })
    expect(isCommandContractReady(OTHER_COMPANY)).toBe(true)
    expect(isCommandContractReady(CTX)).toBe(false)
  })

  it('records nothing when there is no bound context', () => {
    recordContractRead(null, { status: 200, bodyValidated: true, ack: COUNTERPARTY_CONTRACT_VERSION })
    expect(isCommandContractReady(CTX)).toBe(false)
  })

  it('keys a context by origin AND namespace together', () => {
    expect(contractContextKey(CTX)).not.toBe(contractContextKey(OTHER_ORIGIN))
    expect(contractContextKey(CTX)).not.toBe(contractContextKey(OTHER_COMPANY))
  })
})

describe('what a command response means', () => {
  const ATTEMPT = {
    context: CTX,
    idempotencyKey: 'attempt-key-1',
    payloadFingerprint: 'kind=1|legalName=Alfa|roles=absent',
  }
  const VALIDATED_201 = { kind: 'validated', status: 201 } as const

  const ready = () =>
    recordContractRead(CTX, { status: 200, bodyValidated: true, ack: COUNTERPARTY_CONTRACT_VERSION })

  /** The same attempt reporting back, varying only what is under test. */
  const settlement = (over = {}) => ({
    attempt: ATTEMPT,
    context: CTX,
    response: res(ACK),
    result: VALIDATED_201,
    ...over,
  })

  it('is acknowledged only with the exact header', () => {
    expect(commandAckOutcome(res(ACK))).toBe('acknowledged')
    expect(commandAckOutcome(res({}))).toBe('unknown-outcome')
    expect(commandAckOutcome(res({ [COUNTERPARTY_CONTRACT_HEADER]: 'other' }))).toBe(
      'unknown-outcome',
    )
  })

  it('a missing acknowledgment is UNKNOWN, not a failure', () => {
    // The command may have been applied by a server that did not tell us, or refused
    // by one that never saw it. The body cannot settle which, so nothing is retried
    // and the attempt/context/payload/key are kept as they are.
    expect(commandAckOutcome(res({}))).toBe('unknown-outcome')
  })

  it('settles on a validated 2xx result for the same attempt in a ready context', () => {
    ready()
    expect(settlesUnknownOutcome(ATTEMPT, settlement())).toBe(true)
    expect(
      settlesUnknownOutcome(ATTEMPT, settlement({ result: { kind: 'validated', status: 200 } })),
    ).toBe(true)
  })

  it('does NOT settle on a 500, acknowledgment or not', () => {
    // The declaration header proves the server understood the DECLARATION. It says
    // nothing about whether the command was applied: a 500 can be raised after the
    // write as easily as before it.
    ready()
    expect(
      settlesUnknownOutcome(ATTEMPT, settlement({ result: { kind: 'validated', status: 500 } })),
    ).toBe(false)
    expect(
      settlesUnknownOutcome(ATTEMPT, settlement({ result: { kind: 'undecodable', status: 500 } })),
    ).toBe(false)
  })

  it('does NOT settle when no usable response arrived — timeout, abort, transport', () => {
    ready()
    expect(
      settlesUnknownOutcome(ATTEMPT, settlement({ response: null, result: { kind: 'no-response' } })),
    ).toBe(false)
    // Even if a response object exists, "no usable result" is not an answer.
    expect(settlesUnknownOutcome(ATTEMPT, settlement({ result: { kind: 'no-response' } }))).toBe(
      false,
    )
  })

  it('does NOT settle on a 2xx whose body could not be decoded as the result', () => {
    // "It answered 201" is not the same as "we read back the record it created".
    ready()
    expect(
      settlesUnknownOutcome(ATTEMPT, settlement({ result: { kind: 'undecodable', status: 201 } })),
    ).toBe(false)
  })

  it('does NOT settle on a later compatibility 409', () => {
    // A 409 on a re-issue is consistent both with "the first attempt landed" and with
    // "it never did", so it is not an answer either way.
    ready()
    expect(
      settlesUnknownOutcome(ATTEMPT, settlement({ result: { kind: 'validated', status: 409 } })),
    ).toBe(false)
  })

  it('does NOT settle on any 4xx, however well formed', () => {
    ready()
    for (const status of [400, 401, 403, 404, 415, 422]) {
      expect(
        settlesUnknownOutcome(ATTEMPT, settlement({ result: { kind: 'validated', status } })),
      ).toBe(false)
    }
  })

  it('does NOT settle without the acknowledgment, even on a validated 201', () => {
    ready()
    expect(settlesUnknownOutcome(ATTEMPT, settlement({ response: res({}) }))).toBe(false)
  })

  it('does NOT settle in a context that is not itself ready', () => {
    // No `ready()`: the gate is shut, so nothing observed through it counts.
    expect(settlesUnknownOutcome(ATTEMPT, settlement())).toBe(false)
  })

  it('does NOT settle from a context other than the one the attempt was made in', () => {
    recordContractRead(OTHER_COMPANY, {
      status: 200,
      bodyValidated: true,
      ack: COUNTERPARTY_CONTRACT_VERSION,
    })
    // Ready — but for a different company than the attempt belongs to.
    expect(settlesUnknownOutcome(ATTEMPT, settlement({ context: OTHER_COMPANY }))).toBe(false)
  })

  it('does NOT settle from a DIFFERENT attempt: another key or an edited payload', () => {
    ready()
    expect(
      settlesUnknownOutcome(
        ATTEMPT,
        settlement({ attempt: { ...ATTEMPT, idempotencyKey: 'another-key' } }),
      ),
    ).toBe(false)
    expect(
      settlesUnknownOutcome(
        ATTEMPT,
        settlement({ attempt: { ...ATTEMPT, payloadFingerprint: 'edited-while-pending' } }),
      ),
    ).toBe(false)
    expect(
      settlesUnknownOutcome(
        ATTEMPT,
        settlement({ attempt: { ...ATTEMPT, context: OTHER_COMPANY } }),
      ),
    ).toBe(false)
  })

  it('compares attempts by context, key and payload together', () => {
    expect(sameCommandAttempt(ATTEMPT, { ...ATTEMPT })).toBe(true)
    expect(sameCommandAttempt(ATTEMPT, { ...ATTEMPT, idempotencyKey: 'x' })).toBe(false)
    expect(sameCommandAttempt(ATTEMPT, { ...ATTEMPT, payloadFingerprint: 'x' })).toBe(false)
    expect(sameCommandAttempt(ATTEMPT, { ...ATTEMPT, context: OTHER_COMPANY })).toBe(false)
  })
})

describe('retrying an unknown outcome', () => {
  const ATTEMPT = {
    context: CTX,
    idempotencyKey: 'attempt-key-1',
    payloadFingerprint: 'kind=1|legalName=Alfa|roles=absent',
  }

  it('is refused until readiness is re-established in the attempt own context', () => {
    expect(mayRetrySameKey(ATTEMPT, CTX)).toBe(false)

    recordContractRead(CTX, { status: 200, bodyValidated: true, ack: COUNTERPARTY_CONTRACT_VERSION })
    expect(mayRetrySameKey(ATTEMPT, CTX)).toBe(true)
  })

  it('is refused in any other context, however ready that context is', () => {
    recordContractRead(OTHER_COMPANY, {
      status: 200,
      bodyValidated: true,
      ack: COUNTERPARTY_CONTRACT_VERSION,
    })
    // The attempt belongs to company A. Being ready in company B does not license
    // re-sending it there — and never under a freshly minted key.
    expect(mayRetrySameKey(ATTEMPT, OTHER_COMPANY)).toBe(false)
  })
})
