/**
 * Arranges a second or third period through the REAL Accounting API — the same authorized route, #276 fence,
 * ADR-011 record and audit the M-07 screen's propose takes — as the seed's preparer, and reads a period, the journals
 * and the trial balance back for post-conditions and for the server truth a screen is compared with. Nothing is written
 * to SQL directly and nothing is fulfilled locally.
 */

import { randomUUID } from 'node:crypto'
import { expect, request, type APIRequestContext } from '@playwright/test'
import type { Acc01Environment } from './env.js'

export async function apiAs(env: Acc01Environment, actor: string, companyId: string): Promise<APIRequestContext> {
  return request.newContext({
    baseURL: env.apiBaseUrl,
    extraHTTPHeaders: {
      'X-Development-User': actor,
      'X-Company-Id': companyId,
      'X-Correlation-Id': `acc01-ci-${randomUUID()}`,
    },
  })
}

/** Proposes a period of GDC as the preparer; answers its id. */
export async function proposeThroughApi(env: Acc01Environment, period: { startsOn: string; endsOn: string }): Promise<string> {
  const api = await apiAs(env, env.seed.preparerActor, env.seed.companyId)
  try {
    const periodId = randomUUID()
    const response = await api.post('/api/v1/accounting/periods', {
      data: { periodId, ...period },
      headers: { 'Idempotency-Key': `acc01-arrange-${periodId}` },
    })
    expect(response.status(), `propose: ${await response.text()}`).toBe(201)
    return periodId
  } finally {
    await api.dispose()
  }
}

export interface JournalRead {
  readonly journalId: string
  readonly kind: string
  readonly transactionDate: string
  readonly accountingDate: string
  readonly valueDate: string | null
  readonly periodId: string
  readonly totalDebit: string
  readonly totalCredit: string
  readonly lineCount: number
}

export interface JournalDetailRead {
  readonly journal: JournalRead
  readonly lines: readonly { readonly lineNumber: number; readonly accountId: string; readonly debit: string; readonly credit: string }[]
}

export interface TrialBalanceRead {
  readonly lines: readonly { readonly accountId: string; readonly currencyId: string; readonly debit: string; readonly credit: string }[]
  readonly currencyTotals: readonly { readonly currencyId: string; readonly debit: string; readonly credit: string; readonly isBalanced: boolean }[]
}

async function readJson<T>(env: Acc01Environment, actor: string, path: string): Promise<T> {
  const api = await apiAs(env, actor, env.seed.companyId)
  try {
    const response = await api.get(path)
    expect(response.status(), `read ${path}: ${await response.text()}`).toBe(200)
    return (await response.json()) as T
  } finally {
    await api.dispose()
  }
}

/** GDC's journals as the server lists them now (the seed installs exactly one, its synthetic posted journal). */
export async function readJournals(env: Acc01Environment, actor: string): Promise<readonly JournalRead[]> {
  return (await readJson<{ items: JournalRead[] }>(env, actor, '/api/v1/accounting/journals')).items
}

/** One GDC journal and its lines, as the server reads it now. */
export function readJournal(env: Acc01Environment, actor: string, journalId: string): Promise<JournalDetailRead> {
  return readJson<JournalDetailRead>(env, actor, `/api/v1/accounting/journals/${journalId}`)
}

/** GDC's trial balance over an accounting-date range, as the server computes it now. */
export function readTrialBalance(
  env: Acc01Environment,
  actor: string,
  range: { fromAccountingDate: string; throughAccountingDate: string },
): Promise<TrialBalanceRead> {
  return readJson<TrialBalanceRead>(env, actor, `/api/v1/accounting/trial-balance?${new URLSearchParams(range).toString()}`)
}

/** One GDC period as the server reads it now. */
export async function readPeriod(env: Acc01Environment, actor: string, periodId: string): Promise<{ status: string; version: number }> {
  const api = await apiAs(env, actor, env.seed.companyId)
  try {
    const response = await api.get(`/api/v1/accounting/periods/${periodId}`)
    expect(response.status(), `read: ${await response.text()}`).toBe(200)
    return (await response.json()) as { status: string; version: number }
  } finally {
    await api.dispose()
  }
}
