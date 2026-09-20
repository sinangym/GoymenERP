/**
 * Arranges a second or third period through the REAL Accounting API — the same authorized route, #276 fence,
 * ADR-011 record and audit the M-07 screen's propose takes — as the seed's preparer, and reads a period back for
 * post-conditions the screen does not show. Nothing is written to SQL directly and nothing is fulfilled locally.
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
