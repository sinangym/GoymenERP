/**
 * SAT-D57 transport — the company's customer group list and the Cari account's group.
 *
 * Same thin hand-written shape as ./taxIdentityApi.ts, consuming the generated contract
 * (`contracts/openapi/goymen-erp-v1.json`, see ./customerGroup.ts):
 *  - READS send the client-contract declaration and the selected company. The list read on the Settings screen is
 *    that screen's own primary read, so it records the contract observation (it is what lets the screen's commands
 *    go out). The account read on the Cari card is a side panel and deliberately does NOT move the readiness slot:
 *    the card's own detail read owns it.
 *  - COMMANDS share the write gate (`isCommandContractReady`), one Idempotency-Key per attempt, the exact-204 rule and
 *    the "no acknowledgment = unknown outcome" rule. The versioned ones go through `putVersionedCommand`, so the
 *    version travels as lossless decimal text.
 */

import {
  API_BASE,
  beginContractRead,
  commandAckOutcome,
  contractAckOf,
  contractRequestHeader,
  isCommandContractReady,
  recordContractRead,
  recordContractReadFailure,
  type ContractContext,
} from './clientContract'
import { putVersionedCommand, type CommandContext, type CommandOutcome, type ReplayableFailures } from './commandApi'
import {
  CUSTOMER_GROUPS_PATH,
  GROUP_PROBLEM_CODE,
  accountCustomerGroupPath,
  customerGroupPath,
  decodeAccountCustomerGroup,
  decodeCustomerGroupList,
  groupProblemCodeOf,
  type AccountCustomerGroup,
  type CustomerGroupList,
} from './customerGroup'

const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED =
  import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

function devHeaders(): Record<string, string> {
  return DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}
}

export type GroupReadOutcome<T> =
  | { readonly type: 'ok'; readonly data: T }
  | { readonly type: 'http'; readonly status: number; readonly code: string | null }
  | { readonly type: 'malformed' }
  | { readonly type: 'offline' }
  | { readonly type: 'network-error' }
  | { readonly type: 'aborted' }

async function getJson<T>(
  path: string,
  companyId: string,
  decode: (raw: unknown) => T | null,
  signal: AbortSignal | undefined,
  contract: ContractContext | null,
): Promise<GroupReadOutcome<T>> {
  beginContractRead(contract)
  let response: Response
  try {
    response = await fetch(`${API_BASE}${path}`, {
      headers: { ...devHeaders(), ...contractRequestHeader(), 'X-Company-Id': companyId },
      signal,
    })
  } catch (error) {
    if (signal?.aborted) {
      recordContractReadFailure(contract, 'aborted')
      return { type: 'aborted' }
    }
    recordContractReadFailure(contract, 'unreachable')
    if (!navigator.onLine) return { type: 'offline' }
    if (error instanceof TypeError) return { type: 'network-error' }
    throw error
  }

  let body: unknown = null
  try {
    body = await response.json()
  } catch {
    if (signal?.aborted) {
      recordContractReadFailure(contract, 'aborted')
      return { type: 'aborted' }
    }
    body = null
  }

  const ack = contractAckOf(response)
  if (response.status !== 200) {
    recordContractRead(contract, { status: response.status, bodyValidated: false, ack })
    return { type: 'http', status: response.status, code: groupProblemCodeOf(body) }
  }
  const data = decode(body)
  recordContractRead(contract, { status: response.status, bodyValidated: data !== null, ack })
  return data === null ? { type: 'malformed' } : { type: 'ok', data }
}

/**
 * The company's group list. `contract` is the Settings screen's own context (its primary read records readiness);
 * the Cari card passes `null` so that its side read never opens or closes the card's write gate.
 */
export function fetchCustomerGroups(
  companyId: string,
  signal?: AbortSignal,
  contract: ContractContext | null = null,
): Promise<GroupReadOutcome<CustomerGroupList>> {
  return getJson(CUSTOMER_GROUPS_PATH, companyId, (raw) => decodeCustomerGroupList(raw, companyId), signal, contract)
}

/** The selected company's account of this party and its group (a side read of the Cari card). */
export function fetchAccountCustomerGroup(
  partyId: string,
  companyId: string,
  signal?: AbortSignal,
): Promise<GroupReadOutcome<AccountCustomerGroup>> {
  return getJson(
    accountCustomerGroupPath(partyId),
    companyId,
    (raw) => decodeAccountCustomerGroup(raw, { partyId, companyId }),
    signal,
    null,
  )
}

const replayable = (entries: ReadonlyArray<readonly [number, readonly string[]]>): ReplayableFailures =>
  new Map(entries.map(([status, codes]) => [status, new Set<string>(codes)]))

/** `CreateCounterpartyCustomerGroupV1` stores 204 and 409 (exists / name taken). */
const CREATE_REPLAYABLE = replayable([[409, [GROUP_PROBLEM_CODE.exists, GROUP_PROBLEM_CODE.nameTaken]]])
const RENAME_REPLAYABLE = replayable([
  [404, [GROUP_PROBLEM_CODE.notFound]],
  [409, [GROUP_PROBLEM_CODE.versionConflict, GROUP_PROBLEM_CODE.nameTaken]],
])
const ACTIVE_STATE_REPLAYABLE = replayable([
  [404, [GROUP_PROBLEM_CODE.notFound]],
  [409, [GROUP_PROBLEM_CODE.versionConflict]],
])
const ASSIGN_REPLAYABLE = replayable([
  [404, [GROUP_PROBLEM_CODE.notFound]],
  [409, [GROUP_PROBLEM_CODE.versionConflict, GROUP_PROBLEM_CODE.inactive]],
  [422, [GROUP_PROBLEM_CODE.groupNotFound]],
])

function isVerifiedReplay(response: Response, body: unknown, table: ReplayableFailures): boolean {
  if ((response.headers.get('Idempotency-Replayed') ?? '').trim().toLowerCase() !== 'true') return false
  if (commandAckOutcome(response) !== 'acknowledged') return false
  const code = groupProblemCodeOf(body)
  return code !== null && (table.get(response.status)?.has(code) ?? false)
}

/**
 * `POST …/customer-groups` with a caller-chosen UUID. Success is EXACTLY an acknowledged 204: the group then exists
 * and the screen re-reads the list. An unanswered or unacknowledged create keeps the same key AND id for a retry.
 */
export async function createCustomerGroup(
  context: CommandContext,
  id: string,
  name: string,
  signal?: AbortSignal,
): Promise<CommandOutcome<null>> {
  if (!isCommandContractReady(context.contract)) return { type: 'contract-not-ready' }
  let response: Response
  try {
    response = await fetch(`${API_BASE}${CUSTOMER_GROUPS_PATH}`, {
      method: 'POST',
      headers: {
        ...devHeaders(),
        ...contractRequestHeader(),
        'Content-Type': 'application/json',
        'X-Company-Id': context.companyId,
        'Idempotency-Key': context.idempotencyKey,
      },
      body: JSON.stringify({ id, name }),
      signal,
    })
  } catch {
    if (signal?.aborted) return { type: 'aborted' }
    if (!navigator.onLine) return { type: 'offline' }
    return { type: 'network-error' }
  }

  if (response.status !== 204) {
    let body: unknown = null
    try {
      body = await response.json()
    } catch {
      body = null
    }
    return {
      type: 'http',
      status: response.status,
      code: null,
      body,
      replayed: isVerifiedReplay(response, body, CREATE_REPLAYABLE),
    }
  }

  if (commandAckOutcome(response) === 'unknown-outcome') return { type: 'unacknowledged', status: response.status }
  const original = response.headers.get('Idempotency-Original-Correlation-Id')
  return {
    type: 'applied',
    data: null,
    replayed: (response.headers.get('Idempotency-Replayed') ?? '').trim().toLowerCase() === 'true',
    originalCorrelationId: original !== null && original.trim().length > 0 ? original.trim() : null,
  }
}

/** Rename; `expectedVersion` is the GROUP version of the list the form was opened on. */
export function renameCustomerGroup(
  context: CommandContext,
  groupId: string,
  expectedVersion: string,
  name: string,
  signal?: AbortSignal,
): Promise<CommandOutcome<null>> {
  return putVersionedCommand(`${customerGroupPath(groupId)}/name`, context, expectedVersion, { name }, signal, RENAME_REPLAYABLE)
}

/** Deactivate (passive) or reactivate; a group is never deleted. */
export function setCustomerGroupActiveState(
  context: CommandContext,
  groupId: string,
  expectedVersion: string,
  isActive: boolean,
  signal?: AbortSignal,
): Promise<CommandOutcome<null>> {
  return putVersionedCommand(
    `${customerGroupPath(groupId)}/active-state`,
    context,
    expectedVersion,
    { isActive },
    signal,
    ACTIVE_STATE_REPLAYABLE,
  )
}

/**
 * Assign the account a group of the selected company, or clear it with `null` (sent explicitly: the contract requires
 * the property). `expectedVersion` is the ACCOUNT version of the read the selector was opened on.
 */
export function assignAccountCustomerGroup(
  context: CommandContext,
  partyId: string,
  expectedVersion: string,
  customerGroupId: string | null,
  signal?: AbortSignal,
): Promise<CommandOutcome<null>> {
  return putVersionedCommand(
    accountCustomerGroupPath(partyId),
    context,
    expectedVersion,
    { customerGroupId },
    signal,
    ASSIGN_REPLAYABLE,
  )
}
