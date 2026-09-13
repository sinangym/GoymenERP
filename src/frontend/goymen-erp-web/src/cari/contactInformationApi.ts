/**
 * Operational contact/address transport — `GET` and `PUT`
 * `/api/v1/counterparties/{partyId}/contact-information`.
 *
 * Same thin hand-written shape as ./detailApi.ts and ./commandApi.ts. The runtime
 * decoding in ./contactInformationContract.ts is what survives the generated-client
 * migration (Issue #122).
 *
 * WHAT THE GET IS FOR, twice over. It is the card's read, and it is also the exchange
 * that opens the write gate for this company: a command may only go out where a
 * fresh, successful, body-validated, ACKNOWLEDGED read has been seen for this origin,
 * actor, session, company and generation. The GET is also the only way to learn what
 * the record now says after a 204 — the update's own response carries no body.
 *
 * A 404 here is non-disclosing and means "no account for this Party in the selected
 * company, as far as you are concerned". It is never turned into an empty card: an
 * empty contact block would be a claim that the counterparty has no address, and this
 * client is not entitled to make it.
 */

import {
  API_BASE,
  contractAckOf,
  contractRequestHeader,
  beginContractRead,
  recordContractRead,
  recordContractReadFailure,
  type ContractContext,
} from './clientContract'
import {
  VERSIONED_UPDATE_REPLAYABLE_FAILURES,
  putVersionedCommand,
  type CommandContext,
  type CommandOutcome,
} from './commandApi'
import {
  contactInformationPath,
  decodeContactInformation,
  type ContactInformationView,
} from './contactInformationContract'
import type { ViewOutcome } from '../shell/uiState'

const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED =
  import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

function devHeaders(): Record<string, string> {
  return DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}
}

/** Read the selected company's operational contact block for one Party. */
export async function fetchContactInformation(
  partyId: string,
  activeCompanyId: string,
  signal?: AbortSignal,
  contractContext?: ContractContext | null,
): Promise<ViewOutcome<ContactInformationView>> {
  const headers: Record<string, string> = {
    ...devHeaders(),
    ...contractRequestHeader(),
    'X-Company-Id': activeCompanyId,
  }

  // Mark this context current BEFORE the request goes out, so a response that lands
  // after the client has moved on is recognisable as late — including a successful one.
  beginContractRead(contractContext ?? null)

  let response: Response
  try {
    response = await fetch(`${API_BASE}${contactInformationPath(partyId)}`, { headers, signal })
  } catch (error) {
    if (signal?.aborted) {
      recordContractReadFailure(contractContext ?? null, 'aborted')
      return { type: 'fence-stale' }
    }
    recordContractReadFailure(contractContext ?? null, 'unreachable')
    if (!navigator.onLine) return { type: 'offline' }
    if (error instanceof TypeError) return { type: 'network-error' }
    throw error
  }

  let body: unknown = null
  try {
    body = await response.json()
  } catch {
    // The headers arrived, so the transport-level abort branch above never ran — but
    // our own cancellation can still cut the body mid-stream. That is not an
    // unreadable body; see detailApi.ts.
    if (signal?.aborted) {
      recordContractReadFailure(contractContext ?? null, 'aborted')
      return { type: 'fence-stale' }
    }
    body = null
  }

  const ack = contractAckOf(response)

  // Exact 200 only. The whole 2xx range is not success.
  if (response.status !== 200) {
    recordContractRead(contractContext ?? null, { status: response.status, bodyValidated: false, ack })
    return { type: 'http', status: response.status, body }
  }

  const view = decodeContactInformation(body, { partyId, companyId: activeCompanyId })
  recordContractRead(contractContext ?? null, {
    status: response.status,
    bodyValidated: view !== null,
    ack,
  })
  if (view === null) return { type: 'malformed' }
  return { type: 'ok-single', data: view }
}

/**
 * Apply the basic contact/address update.
 *
 * `expectedVersion` is the ACCOUNT version from the read this edit was opened on —
 * the single fence for the whole graph, children included; there is no per-child
 * expected version. `changes` is built by ./contactEditModel.ts, so every property in
 * it is one the contract declares and every absent one is a deliberate retain.
 *
 * Success is 204 with no body. Nothing about the record's new state comes from here:
 * the caller re-reads.
 */
export function updateContactInformation(
  context: CommandContext,
  partyId: string,
  expectedVersion: string,
  changes: Readonly<Record<string, unknown>>,
  signal?: AbortSignal,
): Promise<CommandOutcome<null>> {
  return putVersionedCommand(
    contactInformationPath(partyId),
    context,
    expectedVersion,
    { changes },
    signal,
    // The contact-information update stores 204 / 404 / 409 (version_conflict) — its own
    // table, named here rather than inherited (`CounterpartyEndpoints.ContactInformation.cs`).
    VERSIONED_UPDATE_REPLAYABLE_FAILURES,
  )
}
