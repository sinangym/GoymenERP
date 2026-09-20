/**
 * The HTTP `FinanceReceiptPort` (FIN-T04b), for the routes the Backend publishes:
 *
 * | Port method | Route |
 * |---|---|
 * | `listBankAccounts` | `GET /finance/bank-accounts` |
 * | `listSources` | `GET /finance/manual-receipts?cursor=` |
 * | `sourceDetail` | `GET /finance/manual-receipts/{sourceId}` |
 * | `draftContext` | `GET /finance/manual-receipts/draft-context` (#261) |
 * | `draftSave` | `POST /finance/manual-receipts/drafts` with `Idempotency-Key` |
 * | `listBankAccountMaster` | `GET /finance/bank-accounts/master` (FIN-UI-1a) |
 * | `bankAccountCurrencyCandidates` | `GET /finance/bank-accounts/currency-candidates` (FIN-UI-1a) |
 * | `proposeBankAccount` | `POST /finance/bank-accounts` with `Idempotency-Key` (FIN-UI-1a) |
 * | `approveBankAccount` | `POST /finance/bank-accounts/{accountId}/approval` with `Idempotency-Key` (FIN-UI-1a) |
 * | `proposeBankAccountIbanChange` | `POST …/{accountId}/iban-change` with `Idempotency-Key` (D4) |
 * | `approveBankAccountIbanChange` | `POST …/{accountId}/iban-change/approval` with `Idempotency-Key` (D4) |
 *
 * Reads: the Backend answers a found resource with 200 and platform problems otherwise, so the
 * adapter restates the status as the #258 `ReadResult` the screens decode — 403 Denied, 404
 * NotFound, 503 Unavailable. Anything else is not a ReadResult and is passed on undecodable.
 *
 * Commands (save, propose, approve): the outcomes a client acts on arrive as the #258 `CommandResult` body (200 Succeeded, 409
 * VersionConflict / IdempotencyConflict, 422 ValidationFailed with its issues) and are passed on as
 * they are. Denied and Unavailable are the platform problems 403 and 503; the adapter restates them
 * as that outcome with no resource, as it does for reads. Anything else (400, 401, 415, 500)
 * carries no command result.
 * No answer at all is `no-reply`, an UNKNOWN outcome the command hook retries with the same key.
 *
 * Record (FIN-T05a): `POST …/manual-receipts/{draftId}/record` with the #258 `RecordRequest`, answered like every
 * command. The server decides the period from `occurredOn` (K1) and answers a definite 422 when it refuses —
 * `finance_period_not_open` for a closed period, `finance_period_rule_unavailable` when it could not decide. The
 * control is not offered yet anyway: no provider declares its capability. Reverse has no route (FIN-T06): it answers an
 * explicit local `Unavailable`, never a success and never a request to a guessed URL.
 *
 * The company is only the verified shell company in `X-Company-Id`; it is never put into a body.
 */

import { API_BASE, devHeaders, fetchDraftContext } from './http'
import type { FinanceReceiptPort, FinanceScope, PortReply } from './port'

export const BANK_ACCOUNTS_PATH = '/finance/bank-accounts'
export const MANUAL_RECEIPTS_PATH = '/finance/manual-receipts'
export const DRAFTS_PATH = '/finance/manual-receipts/drafts'
export const BANK_ACCOUNT_MASTER_PATH = '/finance/bank-accounts/master'
export const BANK_ACCOUNT_CURRENCY_CANDIDATES_PATH = '/finance/bank-accounts/currency-candidates'

const READ_OUTCOME_BY_STATUS: Readonly<Record<number, 'Denied' | 'NotFound' | 'Unavailable'>> = {
  403: 'Denied',
  404: 'NotFound',
  503: 'Unavailable',
}

const COMMAND_RESULT_STATUSES: ReadonlySet<number> = new Set([200, 409, 422])

const COMMAND_OUTCOME_BY_STATUS: Readonly<Record<number, 'Denied' | 'Unavailable'>> = {
  403: 'Denied',
  503: 'Unavailable',
}

/** A command outcome the server states by status alone: no resource, no issues, not a replay. */
function outcomeOnly(outcome: 'Denied' | 'Unavailable'): PortReply {
  return {
    kind: 'body',
    body: { outcome, resource: null, isReplay: false, originalCorrelationId: '', issues: [] },
  }
}

async function read(url: string, scope: FinanceScope, signal: AbortSignal): Promise<PortReply> {
  let response: Response
  try {
    response = await fetch(url, {
      headers: { ...devHeaders(), 'X-Company-Id': scope.companyId, Accept: 'application/json' },
      signal,
    })
  } catch {
    return { kind: 'no-reply' }
  }

  if (response.status === 200) {
    try {
      return { kind: 'body', body: { outcome: 'Found', resource: await response.json() } }
    } catch {
      return signal.aborted ? { kind: 'no-reply' } : { kind: 'body', body: undefined }
    }
  }

  const outcome = READ_OUTCOME_BY_STATUS[response.status]
  return { kind: 'body', body: outcome === undefined ? undefined : { outcome, resource: null } }
}

/**
 * One command POST: the `CommandResult` / `BankAccountCommandResult` body for 200 / 409 / 422, the
 * stated outcome for 403 / 503, nothing usable otherwise, `no-reply` when no answer arrived.
 */
async function command(url: string, scope: FinanceScope, idempotencyKey: string, body: unknown): Promise<PortReply> {
  let response: Response
  try {
    response = await fetch(url, {
      method: 'POST',
      headers: {
        ...devHeaders(),
        'X-Company-Id': scope.companyId,
        'Idempotency-Key': idempotencyKey,
        'Content-Type': 'application/json',
        Accept: 'application/json',
      },
      body: JSON.stringify(body),
    })
  } catch {
    return { kind: 'no-reply' }
  }

  const statedOutcome = COMMAND_OUTCOME_BY_STATUS[response.status]
  if (statedOutcome !== undefined) return outcomeOnly(statedOutcome)
  if (!COMMAND_RESULT_STATUSES.has(response.status)) return { kind: 'body', body: undefined }
  try {
    return { kind: 'body', body: await response.json() }
  } catch {
    return { kind: 'no-reply' }
  }
}

/** A local answer for a command the Backend does not publish: honest `Unavailable`, nothing sent. */
function unpublishedCommand(): Promise<PortReply> {
  return Promise.resolve(outcomeOnly('Unavailable'))
}

export function createHttpFinancePort(): FinanceReceiptPort {
  return {
    listBankAccounts: (scope, signal) => read(`${API_BASE}${BANK_ACCOUNTS_PATH}`, scope, signal),

    listSources: (scope, request, signal) => {
      const params = new URLSearchParams()
      if (request.cursor !== null) params.set('cursor', request.cursor)
      const query = params.toString()
      return read(`${API_BASE}${MANUAL_RECEIPTS_PATH}${query === '' ? '' : `?${query}`}`, scope, signal)
    },

    sourceDetail: (scope, request, signal) =>
      read(`${API_BASE}${MANUAL_RECEIPTS_PATH}/${encodeURIComponent(request.sourceId)}`, scope, signal),

    draftContext: (scope, request, signal) => fetchDraftContext(scope.companyId, request, signal),

    draftSave: (scope, idempotencyKey, request) => command(`${API_BASE}${DRAFTS_PATH}`, scope, idempotencyKey, request),

    record: (scope, idempotencyKey, request) =>
      command(`${API_BASE}${MANUAL_RECEIPTS_PATH}/${encodeURIComponent(request.draftId)}/record`, scope, idempotencyKey, request),
    reverse: () => unpublishedCommand(),

    listBankAccountMaster: (scope, signal) => read(`${API_BASE}${BANK_ACCOUNT_MASTER_PATH}`, scope, signal),

    bankAccountCurrencyCandidates: (scope, signal) =>
      read(`${API_BASE}${BANK_ACCOUNT_CURRENCY_CANDIDATES_PATH}`, scope, signal),

    proposeBankAccount: (scope, idempotencyKey, request) =>
      command(`${API_BASE}${BANK_ACCOUNTS_PATH}`, scope, idempotencyKey, request),

    approveBankAccount: (scope, idempotencyKey, accountId, body) =>
      command(`${API_BASE}${BANK_ACCOUNTS_PATH}/${encodeURIComponent(accountId)}/approval`, scope, idempotencyKey, body),

    proposeBankAccountIbanChange: (scope, idempotencyKey, accountId, body) =>
      command(`${API_BASE}${BANK_ACCOUNTS_PATH}/${encodeURIComponent(accountId)}/iban-change`, scope, idempotencyKey, body),

    approveBankAccountIbanChange: (scope, idempotencyKey, accountId, body) =>
      command(
        `${API_BASE}${BANK_ACCOUNTS_PATH}/${encodeURIComponent(accountId)}/iban-change/approval`,
        scope,
        idempotencyKey,
        body,
      ),
  }
}
