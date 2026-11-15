/**
 * S-11 (SAT-D28, NS-13, SAT-D346) transport — the sales contracts (paths in `salesContract.ts`).
 *
 * `X-Company-Id` is the company frozen with the attempt and `Idempotency-Key` the attempt's key. The view carries no `isReplay`:
 * a replay is said ONLY by the `Idempotency-Replayed` header, and a replay must name its original correlation while a first answer
 * must not — anything else, a body the strict decoder refuses, or a body in the wrong state is `malformed`, so the caller keeps
 * the key (the write may have happened).
 */
import { headers, readFailure, request } from './api'
import { CREATED_STATUS, STATE_COMMAND_STATUS, decodeProblem } from './contract'
import type { OrderCommandOutcome } from './orderAttempt'
import {
  SALES_CONTRACTS_PATH,
  SALES_CONTRACT_STATE,
  decodeSalesContract,
  decodeSalesContractList,
  salesContractDecisionPath,
  salesContractPath,
  type CreateSalesContractBody,
  type SalesContract,
  type SalesContractApprovalBody,
  type SalesContractRejectionBody,
} from './salesContract'
import type { ViewOutcome } from '../shell/uiState'

export async function fetchSalesContracts(companyId: string, signal?: AbortSignal): Promise<ViewOutcome<readonly SalesContract[]>> {
  const raw = await request(SALES_CONTRACTS_PATH, { headers: headers(companyId) }, signal)
  if ('failure' in raw) return readFailure(raw.failure)
  if (raw.status !== 200) return { type: 'http', status: raw.status, body: raw.body }
  const items = decodeSalesContractList(raw.body)
  return items === null ? { type: 'malformed' } : { type: 'ok-single', data: items }
}

export async function fetchSalesContract(contractId: string, companyId: string, signal?: AbortSignal): Promise<ViewOutcome<SalesContract>> {
  const raw = await request(salesContractPath(contractId), { headers: headers(companyId) }, signal)
  if ('failure' in raw) return readFailure(raw.failure)
  if (raw.status !== 200) return { type: 'http', status: raw.status, body: raw.body }
  const view = decodeSalesContract(raw.body, contractId)
  return view === null ? { type: 'malformed' } : { type: 'ok-single', data: view }
}

/** A recorded answer and whether it was the replay of an earlier send (the header, not the body). */
export interface SalesContractMutation {
  readonly contract: SalesContract
  readonly isReplay: boolean
}

export interface SalesContractCommandContext {
  readonly idempotencyKey: string
  /** The company frozen with the attempt, not whatever the shell holds at retry time. */
  readonly companyId: string
}

type Answer = Awaited<ReturnType<typeof request>>

function interpret(
  raw: Answer,
  expectedStatus: number,
  accept: (body: unknown) => SalesContract | null,
): OrderCommandOutcome<SalesContractMutation> {
  if ('failure' in raw) {
    if (raw.failure === 'aborted') return { type: 'aborted' }
    if (raw.failure === 'offline') return { type: 'offline' }
    return { type: 'network-error' }
  }
  if (raw.status !== expectedStatus) {
    if (raw.status >= 200 && raw.status < 300) return { type: 'malformed' }
    return { type: 'http', status: raw.status, problem: decodeProblem(raw.body, raw.status) }
  }
  const contract = accept(raw.body)
  // A replay names its original correlation; a first answer does not.
  if (contract === null || raw.replayed !== (raw.replayCorrelationId !== null)) return { type: 'malformed' }
  return { type: 'succeeded', data: { contract, isReplay: raw.replayed } }
}

function post(path: string, body: unknown, context: SalesContractCommandContext, signal?: AbortSignal) {
  return request(
    path,
    {
      method: 'POST',
      headers: { ...headers(context.companyId), 'Content-Type': 'application/json', 'Idempotency-Key': context.idempotencyKey },
      body: JSON.stringify(body),
    },
    signal,
  )
}

const sameId = (left: string, right: string) => left.toLowerCase() === right.toLowerCase()

/**
 * Records a contract (201). The answer is believed only when it names the sent party, account, currency, validity, plan length
 * and rule count, and lands Active (no approval needed) or pending discount approval — which of the two is the server's decision.
 */
export async function createSalesContract(
  payload: CreateSalesContractBody,
  context: SalesContractCommandContext,
  signal?: AbortSignal,
): Promise<OrderCommandOutcome<SalesContractMutation>> {
  const body: CreateSalesContractBody = {
    partyId: payload.partyId,
    companyAccountId: payload.companyAccountId,
    currencyId: payload.currencyId,
    validFrom: payload.validFrom,
    validThrough: payload.validThrough,
    installments: payload.installments.map((item) => ({ dueDate: item.dueDate, amount: item.amount })),
    discountRules: payload.discountRules.map((rule) => ({ kind: rule.kind, label: rule.label, ratePercent: rule.ratePercent })),
    clauses: payload.clauses,
  }
  const raw = await post(SALES_CONTRACTS_PATH, body, context, signal)
  return interpret(raw, CREATED_STATUS, (value) => {
    const contract = decodeSalesContract(value)
    return contract !== null
      && contract.version === '1'
      && (contract.state === SALES_CONTRACT_STATE.active || contract.state === SALES_CONTRACT_STATE.pending)
      && sameId(contract.partyId, payload.partyId)
      && sameId(contract.companyAccountId, payload.companyAccountId)
      && sameId(contract.currencyId, payload.currencyId)
      && contract.validFrom === payload.validFrom
      && contract.validThrough === payload.validThrough
      && contract.installments.length === payload.installments.length
      && contract.discountRules.length === payload.discountRules.length
      ? contract
      : null
  })
}

/** NS-13: approves the pending discount at the version read; the 200 must be this contract, Active and decided. */
export async function approveSalesContractDiscount(
  contractId: string,
  payload: SalesContractApprovalBody,
  context: SalesContractCommandContext,
  signal?: AbortSignal,
): Promise<OrderCommandOutcome<SalesContractMutation>> {
  const raw = await post(salesContractDecisionPath(contractId, 'discount-approval'), { expectedVersion: payload.expectedVersion },
    context, signal)
  return interpret(raw, STATE_COMMAND_STATUS, (value) => {
    const contract = decodeSalesContract(value, contractId)
    return contract !== null && contract.state === SALES_CONTRACT_STATE.active && contract.decidedBy !== null ? contract : null
  })
}

/** NS-13: rejects the pending discount with its reason at the version read; the 200 must be this contract, rejected. */
export async function rejectSalesContractDiscount(
  contractId: string,
  payload: SalesContractRejectionBody,
  context: SalesContractCommandContext,
  signal?: AbortSignal,
): Promise<OrderCommandOutcome<SalesContractMutation>> {
  const raw = await post(salesContractDecisionPath(contractId, 'discount-rejection'),
    { expectedVersion: payload.expectedVersion, reason: payload.reason }, context, signal)
  return interpret(raw, STATE_COMMAND_STATUS, (value) => {
    const contract = decodeSalesContract(value, contractId)
    return contract !== null && contract.state === SALES_CONTRACT_STATE.rejected ? contract : null
  })
}
