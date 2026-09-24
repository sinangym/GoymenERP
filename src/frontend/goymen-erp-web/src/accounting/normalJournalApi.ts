import { ACCOUNTING_BASE, devHeaders } from './api'
import {
  decodeNormalJournalDraft,
  type CreateNormalJournalDraftRequest,
  type NormalJournalDraft,
} from './contract'

export type NormalJournalCommandSnapshot =
  | {
      readonly operation: 'create'
      readonly idempotencyKey: string
      readonly request: CreateNormalJournalDraftRequest
    }
  | {
      readonly operation: 'approve'
      readonly idempotencyKey: string
      readonly draftId: string
      readonly expectedVersion: number
      readonly periodId: string
    }

export type NormalJournalFailure =
  | { readonly kind: 'validation'; readonly field: string | null }
  | { readonly kind: 'unauthenticated' }
  | { readonly kind: 'forbidden' }
  | { readonly kind: 'not-found' }
  | { readonly kind: 'version-conflict'; readonly expectedVersion: number | null; readonly currentVersion: number | null }
  | { readonly kind: 'idempotency-conflict' }
  | { readonly kind: 'concurrent-change' }
  | { readonly kind: 'content-conflict' }
  | { readonly kind: 'not-pending' }
  | { readonly kind: 'posting-conflict' }
  | { readonly kind: 'rule'; readonly code: string }
  | { readonly kind: 'unsupported-media' }

export type NormalJournalCommandOutcome =
  | { readonly type: 'applied'; readonly draft: NormalJournalDraft; readonly replayed: boolean }
  | { readonly type: 'failed'; readonly failure: NormalJournalFailure }
  | { readonly type: 'unknown'; readonly reason: 'offline' | 'network' | 'service' | 'malformed' | 'unexpected-success' | 'aborted' }

function text(body: unknown, key: string): string | null {
  if (typeof body !== 'object' || body === null || Array.isArray(body)) return null
  const value = (body as Record<string, unknown>)[key]
  return typeof value === 'string' && value.length > 0 ? value : null
}

function version(body: unknown, key: string): number | null {
  const value = text(body, key)
  if (value === null || !/^[1-9][0-9]{0,15}$/.test(value)) return null
  const parsed = Number(value)
  return Number.isSafeInteger(parsed) ? parsed : null
}

function failure(status: number, body: unknown): NormalJournalFailure | null {
  if (status === 400) return { kind: 'validation', field: text(body, 'field') }
  if (status === 401) return { kind: 'unauthenticated' }
  if (status === 403) return { kind: 'forbidden' }
  if (status === 404) return { kind: 'not-found' }
  if (status === 415) return { kind: 'unsupported-media' }
  const code = text(body, 'code')
  if (status === 409 && code === 'idempotency_conflict') return { kind: 'idempotency-conflict' }
  if (status === 409 && code === 'accounting_concurrent_change') return { kind: 'concurrent-change' }
  if (status === 409 && code === 'accounting_normal_journal_draft_content_conflict') return { kind: 'content-conflict' }
  if (status === 409 && code === 'accounting_normal_journal_draft_not_pending') return { kind: 'not-pending' }
  if (status === 409 && code === 'accounting_normal_journal_draft_posting_conflict') return { kind: 'posting-conflict' }
  if (status === 409 && code === 'accounting_version_conflict') {
    return { kind: 'version-conflict', expectedVersion: version(body, 'expectedVersion'), currentVersion: version(body, 'currentVersion') }
  }
  if (status === 422 && code !== null) return { kind: 'rule', code }
  return null
}

export async function sendNormalJournalCommand(
  companyId: string,
  snapshot: NormalJournalCommandSnapshot,
  signal?: AbortSignal,
): Promise<NormalJournalCommandOutcome> {
  const create = snapshot.operation === 'create'
  const url = create
    ? `${ACCOUNTING_BASE}/normal-journal-drafts`
    : `${ACCOUNTING_BASE}/normal-journal-drafts/${encodeURIComponent(snapshot.draftId)}/approval`
  const body = create
    ? snapshot.request
    : { expectedVersion: snapshot.expectedVersion, periodId: snapshot.periodId }
  let response: Response
  try {
    response = await fetch(url, {
      method: 'POST',
      headers: {
        ...devHeaders(),
        'Content-Type': 'application/json',
        'X-Company-Id': companyId,
        'Idempotency-Key': snapshot.idempotencyKey,
      },
      body: JSON.stringify(body),
      signal,
    })
  } catch {
    if (signal?.aborted) return { type: 'unknown', reason: 'aborted' }
    return { type: 'unknown', reason: navigator.onLine ? 'network' : 'offline' }
  }

  const success = create ? 201 : 200
  let parsed: unknown = null
  try {
    parsed = await response.json()
  } catch {
    parsed = null
  }
  if (response.status === success) {
    const draft = decodeNormalJournalDraft(parsed, companyId)
    return draft === null
      ? { type: 'unknown', reason: 'malformed' }
      : { type: 'applied', draft, replayed: (response.headers.get('Idempotency-Replayed') ?? '').toLowerCase() === 'true' }
  }
  if (response.status >= 200 && response.status < 300) return { type: 'unknown', reason: 'unexpected-success' }
  if (response.status >= 500) return { type: 'unknown', reason: 'service' }
  const refused = failure(response.status, parsed)
  return refused === null ? { type: 'unknown', reason: 'malformed' } : { type: 'failed', failure: refused }
}
