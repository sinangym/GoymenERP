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
 *
 * Reads: the Backend answers a found resource with 200 and platform problems otherwise, so the
 * adapter restates the status as the #258 `ReadResult` the screens decode — 403 Denied, 404
 * NotFound, 503 Unavailable. Anything else is not a ReadResult and is passed on undecodable.
 *
 * Save: the outcomes a client acts on arrive as the #258 `CommandResult` body (200 Succeeded, 409
 * VersionConflict / IdempotencyConflict, 422 ValidationFailed with its issues) and are passed on as
 * they are. Denied and Unavailable are the platform problems 403 and 503; the adapter restates them
 * as that outcome with no resource, as it does for reads. Anything else (400, 401, 415, 500)
 * carries no command result.
 * No answer at all is `no-reply`, an UNKNOWN outcome the command hook retries with the same key.
 *
 * Record and Reverse have no route (T05; authority is OPEN-009). They answer an explicit local
 * `Unavailable`, never a success and never a request to a guessed URL.
 *
 * The company is only the verified shell company in `X-Company-Id`; it is never put into a body.
 */

import { API_BASE, devHeaders, fetchDraftContext } from './http'
import type { FinanceReceiptPort, FinanceScope, PortReply } from './port'

export const BANK_ACCOUNTS_PATH = '/finance/bank-accounts'
export const MANUAL_RECEIPTS_PATH = '/finance/manual-receipts'
export const DRAFTS_PATH = '/finance/manual-receipts/drafts'

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

    draftSave: async (scope, idempotencyKey, request) => {
      let response: Response
      try {
        response = await fetch(`${API_BASE}${DRAFTS_PATH}`, {
          method: 'POST',
          headers: {
            ...devHeaders(),
            'X-Company-Id': scope.companyId,
            'Idempotency-Key': idempotencyKey,
            'Content-Type': 'application/json',
            Accept: 'application/json',
          },
          body: JSON.stringify(request),
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
    },

    record: () => unpublishedCommand(),
    reverse: () => unpublishedCommand(),
  }
}
