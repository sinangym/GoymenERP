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

  it('an unknown outcome is not settled by a later response in an unready context', () => {
    // Including a compatibility 409: on a re-issue that is consistent both with "the
    // first attempt landed" and with "it never did".
    expect(settlesUnknownOutcome(CTX, res(ACK))).toBe(false)
  })

  it('is settled only by an acknowledged response in a context that is itself ready', () => {
    recordContractRead(CTX, { status: 200, bodyValidated: true, ack: COUNTERPARTY_CONTRACT_VERSION })
    expect(settlesUnknownOutcome(CTX, res(ACK))).toBe(true)
    // Ready context, but this response did not acknowledge: still unknown.
    expect(settlesUnknownOutcome(CTX, res({}))).toBe(false)
    // Acknowledged response, but readiness belongs to another context: still unknown.
    expect(settlesUnknownOutcome(OTHER_COMPANY, res(ACK))).toBe(false)
  })
})
