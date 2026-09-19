/**
 * Arranges an open production order through the REAL Manufacturing API — the same authorized
 * routes, authority fence, idempotency record and audit a user's commands take — as the seed's
 * recorder. Nothing is written to SQL directly and nothing is fulfilled locally.
 *
 * Every id is new per case, and the parent roll is a synthetic label identity (MFG-API has no
 * Inventory link in this slice), so cases never share state.
 */

import { randomUUID } from 'node:crypto'
import { expect, request, type APIRequestContext } from '@playwright/test'
import type { MfgEnvironment } from './env.js'

export interface ArrangedOrder {
  readonly orderId: string
  readonly rollId: string
  readonly version: number
}

export async function apiAs(env: MfgEnvironment, actor: string): Promise<APIRequestContext> {
  return request.newContext({
    baseURL: env.apiBaseUrl,
    extraHTTPHeaders: {
      'X-Development-User': actor,
      'X-Company-Id': env.seed.companyId,
      'X-Correlation-Id': `mfg-ci-${randomUUID()}`,
    },
  })
}

async function post(api: APIRequestContext, path: string, body: unknown, status: number): Promise<Record<string, unknown>> {
  const response = await api.post(`/api/v1/manufacturing${path}`, {
    data: body,
    headers: { 'Idempotency-Key': randomUUID() },
  })
  expect(response.status(), `${path}: ${await response.text()}`).toBe(status)
  return (await response.json()) as Record<string, unknown>
}

/** An approved slitting plan of one 1000 kg parent roll and its open order. */
export async function arrangeOpenOrder(env: MfgEnvironment): Promise<ArrangedOrder> {
  const api = await apiAs(env, env.seed.recorderActor)
  try {
    const rollId = randomUUID()
    const planId = randomUUID()
    await post(api, '/plans', {
      planId,
      workCenterId: env.seed.workCenterId,
      inputs: [{ parentRollId: rollId, plannedKg: '1000' }],
      target: null,
    }, 201)
    await post(api, `/plans/${planId}/approval`, { expectedVersion: 1 }, 200)
    const orderId = randomUUID()
    const order = await post(api, '/orders', { orderId, planId }, 201)
    return { orderId, rollId, version: order.version as number }
  } finally {
    await api.dispose()
  }
}

/** Another writer's input issue on the same order: moves its version on under the page. */
export async function issueThroughApi(env: MfgEnvironment, arranged: ArrangedOrder, expectedVersion: number, kg: string) {
  const api = await apiAs(env, env.seed.recorderActor)
  try {
    return await post(api, `/orders/${arranged.orderId}/input-issues`, {
      factId: randomUUID(),
      expectedVersion,
      parentRollId: arranged.rollId,
      kg,
      occurredAtUtc: new Date().toISOString(),
    }, 200)
  } finally {
    await api.dispose()
  }
}

/** The order as the server reads it now, for post-conditions the screen does not show. */
export async function readOrder(env: MfgEnvironment, orderId: string): Promise<Record<string, unknown>> {
  const api = await apiAs(env, env.seed.recorderActor)
  try {
    const response = await api.get(`/api/v1/manufacturing/orders/${orderId}`)
    expect(response.status()).toBe(200)
    return (await response.json()) as Record<string, unknown>
  } finally {
    await api.dispose()
  }
}
