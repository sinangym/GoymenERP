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
 * | `listCustomerInstruments` / `customerInstrumentDetail` | `GET /finance/customer-instruments[/{id}]` (DEC-FIN-016) |
 * | `customerInstrumentRisk` | `GET /finance/customer-instruments/risk?asOf=[&partyId&companyAccountId]` (read-only) |
 * | `partyLedger` | `GET /finance/party-ledger?partyId&companyAccountId&from&to` (read-only; 400 / 422 as refusals) |
 * | `receiveCustomerInstrument` | `POST /finance/customer-instruments` with `Idempotency-Key` (DEC-FIN-016) |
 * | `changeCustomerInstrumentStatus` | `POST …/{id}/status-changes` with `Idempotency-Key` (DEC-FIN-016) |
 * | `collectCustomerInstrument` | `POST …/{id}/collection` with `Idempotency-Key` (DEC-FIN-016, the money effect) |
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
 * `finance_period_not_open` for a closed period, `finance_period_rule_unavailable` when it could not decide.
 * FIN-T06 uses the explicit persistent proposal and checker-decision routes; the legacy direct-Reverse action remains
 * unpublished and is not a fallback URL.
 *
 * The company is only the verified shell company in `X-Company-Id`; it is never put into a body.
 */

import { API_BASE, devHeaders, fetchDraftContext } from './http'
import type { FinanceReceiptPort, FinanceScope, PortReply } from './port'

export const BANK_ACCOUNTS_PATH = '/finance/bank-accounts'
export const MANUAL_RECEIPTS_PATH = '/finance/manual-receipts'
export const DRAFTS_PATH = '/finance/manual-receipts/drafts'
export const REVERSAL_PROPOSALS_PATH = '/finance/manual-receipts/reversal-proposals'
export const BANK_ACCOUNT_MASTER_PATH = '/finance/bank-accounts/master'
export const BANK_ACCOUNT_CURRENCY_CANDIDATES_PATH = '/finance/bank-accounts/currency-candidates'
export const MANUAL_BANK_PAYMENTS_PATH = '/finance/manual-bank-payments'
/** DEC-FIN-016: the customer cheque / note portfolio (receive, status changes, collection, list, detail). */
export const CUSTOMER_INSTRUMENTS_PATH = '/finance/customer-instruments'
/** The first-release party ledger (cari hesap ekstresi), read-only. */
export const PARTY_LEDGER_PATH = '/finance/party-ledger'
/** The ledger's stated refusal: more rows than one answer carries (narrow the range). */
export const PARTY_LEDGER_TOO_MANY_CODE = 'finance_party_ledger_too_many_entries'

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
 * The party ledger read: a found ledger and the platform outcomes as `read` restates them, plus the two refusals the
 * server states by status — 400 (a malformed range) and 422 `finance_party_ledger_too_many_entries` — handed on as the
 * ledger decoder's refusal marker, so the screen can say which one it was. Any other 422 is not a ledger answer.
 */
async function readPartyLedger(url: string, scope: FinanceScope, signal: AbortSignal): Promise<PortReply> {
  let response: Response
  try {
    response = await fetch(url, {
      headers: { ...devHeaders(), 'X-Company-Id': scope.companyId, Accept: 'application/json' },
      signal,
    })
  } catch {
    return { kind: 'no-reply' }
  }

  const refusal = (partyLedgerRefusal: 'Invalid' | 'TooManyEntries'): PortReply =>
    ({ kind: 'body', body: { outcome: 'Found', resource: { partyLedgerRefusal } } })
  if (response.status === 400) return refusal('Invalid')
  if (response.status === 422) {
    let code: unknown
    try {
      code = ((await response.json()) as { readonly code?: unknown } | null)?.code
    } catch {
      return signal.aborted ? { kind: 'no-reply' } : { kind: 'body', body: undefined }
    }
    return code === PARTY_LEDGER_TOO_MANY_CODE ? refusal('TooManyEntries') : { kind: 'body', body: undefined }
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
    listManualBankPayments: (scope, request, signal) => {
      const params = new URLSearchParams()
      if (request.cursor !== null) params.set('cursor', request.cursor)
      const query = params.toString()
      return read(`${API_BASE}${MANUAL_BANK_PAYMENTS_PATH}${query === '' ? '' : `?${query}`}`, scope, signal)
    },
    manualBankPaymentDetail: (scope, paymentId, signal) =>
      read(`${API_BASE}${MANUAL_BANK_PAYMENTS_PATH}/${encodeURIComponent(paymentId)}`, scope, signal),
    saveManualBankPaymentDraft: (scope, idempotencyKey, request) =>
      command(`${API_BASE}${MANUAL_BANK_PAYMENTS_PATH}`, scope, idempotencyKey, request),
    approveManualBankPayment: (scope, idempotencyKey, request) =>
      command(
        `${API_BASE}${MANUAL_BANK_PAYMENTS_PATH}/${encodeURIComponent(request.paymentId)}/approval`,
        scope,
        idempotencyKey,
        request,
      ),
    recordManualBankPayment: (scope, idempotencyKey, request) =>
      command(
        `${API_BASE}${MANUAL_BANK_PAYMENTS_PATH}/${encodeURIComponent(request.paymentId)}/record`,
        scope,
        idempotencyKey,
        request,
      ),

    listCustomerInstruments: (scope, request, signal) => {
      const params = new URLSearchParams()
      if (request.cursor !== null) params.set('cursor', request.cursor)
      const query = params.toString()
      return read(`${API_BASE}${CUSTOMER_INSTRUMENTS_PATH}${query === '' ? '' : `?${query}`}`, scope, signal)
    },
    customerInstrumentRisk: (scope, request, signal) => {
      const params = new URLSearchParams({ asOf: request.asOf })
      if (request.partyId !== null && request.companyAccountId !== null) {
        params.set('partyId', request.partyId)
        params.set('companyAccountId', request.companyAccountId)
      }
      return read(`${API_BASE}${CUSTOMER_INSTRUMENTS_PATH}/risk?${params.toString()}`, scope, signal)
    },
    partyLedger: (scope, request, signal) => {
      const params = new URLSearchParams({
        partyId: request.partyId,
        companyAccountId: request.companyAccountId,
        from: request.from,
        to: request.to,
      })
      return readPartyLedger(`${API_BASE}${PARTY_LEDGER_PATH}?${params.toString()}`, scope, signal)
    },
    customerInstrumentDetail: (scope, instrumentId, signal) =>
      read(`${API_BASE}${CUSTOMER_INSTRUMENTS_PATH}/${encodeURIComponent(instrumentId)}`, scope, signal),
    receiveCustomerInstrument: (scope, idempotencyKey, request) =>
      command(`${API_BASE}${CUSTOMER_INSTRUMENTS_PATH}`, scope, idempotencyKey, request),
    changeCustomerInstrumentStatus: (scope, idempotencyKey, request) =>
      command(
        `${API_BASE}${CUSTOMER_INSTRUMENTS_PATH}/${encodeURIComponent(request.instrumentId)}/status-changes`,
        scope,
        idempotencyKey,
        request,
      ),
    collectCustomerInstrument: (scope, idempotencyKey, request) =>
      command(
        `${API_BASE}${CUSTOMER_INSTRUMENTS_PATH}/${encodeURIComponent(request.instrumentId)}/collection`,
        scope,
        idempotencyKey,
        request,
      ),

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
    proposeReversal: (scope, idempotencyKey, request) =>
      command(
        `${API_BASE}${MANUAL_RECEIPTS_PATH}/${encodeURIComponent(request.sourceId)}/reversal-proposals`,
        scope,
        idempotencyKey,
        request,
      ),
    approveReversal: (scope, idempotencyKey, request) =>
      command(
        `${API_BASE}${REVERSAL_PROPOSALS_PATH}/${encodeURIComponent(request.proposalId)}/approval`,
        scope,
        idempotencyKey,
        request,
      ),
    rejectReversal: (scope, idempotencyKey, request) =>
      command(
        `${API_BASE}${REVERSAL_PROPOSALS_PATH}/${encodeURIComponent(request.proposalId)}/rejection`,
        scope,
        idempotencyKey,
        request,
      ),
    pendingReversals: (scope, signal) => read(`${API_BASE}${REVERSAL_PROPOSALS_PATH}/pending`, scope, signal),
    reversalProposal: (scope, proposalId, signal) =>
      read(`${API_BASE}${REVERSAL_PROPOSALS_PATH}/${encodeURIComponent(proposalId)}`, scope, signal),

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
