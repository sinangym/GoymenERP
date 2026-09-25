/**
 * Arranges an open production order through the REAL Manufacturing API — the same authorized
 * routes, authority fence, idempotency record and audit a user's commands take — as the seed's
 * recorder. Nothing is written to SQL directly and nothing is fulfilled locally.
 *
 * Every plan and order id is new per case. The parent roll is the fixture's real Inventory label;
 * each plan re-reads its current candidate/version before saving, so no stale or invented version is used.
 */

import { randomUUID } from 'node:crypto'
import { expect, request, type APIRequestContext } from '@playwright/test'
import type { MfgEnvironment, MfgRollSeed } from './env.js'

export interface ArrangedOrder {
  readonly orderId: string
  readonly rollId: string
  readonly version: number
}

interface ParentRollCandidate {
  readonly labelId: string
  readonly labelVersion: string
  readonly draftSelectable: boolean
}

interface ParentLabelDetail {
  readonly labelVersion: string
  readonly identity: {
    readonly productId: string
    readonly requiredDocuments: readonly string[]
  }
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

export async function readParentRollCandidate(
  env: MfgEnvironment,
  roll: MfgRollSeed,
  actor = env.seed.recorderActor,
): Promise<ParentRollCandidate> {
  const api = await apiAs(env, actor)
  try {
    const response = await api.get('/api/v1/inventory/references/parent-rolls', {
      params: { q: roll.labelCode, limit: 25 },
    })
    const text = await response.text()
    expect(response.status(), text).toBe(200)
    const body = JSON.parse(text) as { items?: ParentRollCandidate[] }
    const candidate = body.items?.find((item) => item.labelId === roll.labelId)
    expect(candidate, 'fixture parent roll must be returned by the real Inventory candidate API').toBeDefined()
    expect(candidate?.draftSelectable).toBe(true)
    expect(candidate?.labelVersion).toMatch(/^[1-9][0-9]*$/)
    return candidate as ParentRollCandidate
  } finally {
    await api.dispose()
  }
}

/** An approved slitting plan of one 1000 kg parent roll and its open order. */
export async function arrangeOpenOrder(env: MfgEnvironment, roll: MfgRollSeed): Promise<ArrangedOrder> {
  const api = await apiAs(env, env.seed.recorderActor)
  try {
    const candidate = await readParentRollCandidate(env, roll)
    const rollId = candidate.labelId
    const planId = randomUUID()
    await post(api, '/plans', {
      planId,
      workCenterId: env.seed.workCenterId,
      inputs: [{ parentRollId: rollId, plannedKg: '1000', expectedLabelVersion: candidate.labelVersion }],
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
      issueId: randomUUID(),
      movementId: randomUUID(),
      expectedVersion,
      parentRollId: arranged.rollId,
      kg,
      occurredAtUtc: new Date().toISOString(),
    }, 200)
  } finally {
    await api.dispose()
  }
}

/**
 * Declares an output through the real API (MFG-ORD): the close case needs an order whose roll
 * accounts for part, then all, of what it issued — the decided rule, arranged through the product's
 * own routes and never in SQL.
 */
export async function declareThroughApi(
  env: MfgEnvironment,
  arranged: ArrangedOrder,
  expectedVersion: number,
  outputClass: string,
  kg: string,
): Promise<Record<string, unknown>> {
  const api = await apiAs(env, env.seed.recorderActor)
  try {
    const detailResponse = await api.get(`/api/v1/inventory/labels/${arranged.rollId}/detail`)
    const detailText = await detailResponse.text()
    expect(detailResponse.status(), detailText).toBe(200)
    const detail = JSON.parse(detailText) as ParentLabelDetail
    expect(detail.labelVersion).toMatch(/^[1-9][0-9]*$/)
    expect(detail.identity.productId).toBe(env.seed.output.productId)
    expect(detail.identity.requiredDocuments).toEqual(env.seed.output.requiredDocuments)
    const outputId = randomUUID()
    const movementId = randomUUID()
    const lotId = randomUUID()
    const labelId = randomUUID()
    return await post(api, `/orders/${arranged.orderId}/outputs`, {
      outputId,
      expectedVersion,
      parentRollId: arranged.rollId,
      expectedParentLabelVersion: detail.labelVersion,
      outputClass,
      productId: env.seed.output.productId,
      expectedProductVersion: env.seed.output.productVersion,
      lotId,
      labelId,
      labelCode: `SYN-MFG-OUT-${labelId.replaceAll('-', '')}`,
      heatNumber: null,
      coilNumber: null,
      requiredDocuments: env.seed.output.requiredDocuments,
      ownerKind: env.seed.output.ownerKind,
      ownerId: env.seed.output.ownerId,
      custodianKind: env.seed.output.custodianKind,
      custodianId: env.seed.output.custodianId,
      locationId: env.seed.output.locationId,
      initialBlock: env.seed.output.initialBlock,
      movementId,
      kg,
      occurredAtUtc: new Date().toISOString(),
    }, 200)
  } finally {
    await api.dispose()
  }
}

/** The order's GDC-BAL/1 read (BAL slice 2) as the server answers it now: the panel's own source. */
export async function readMassBalance(env: MfgEnvironment, orderId: string): Promise<Record<string, unknown>> {
  const api = await apiAs(env, env.seed.recorderActor)
  try {
    const response = await api.get(`/api/v1/manufacturing/orders/${orderId}/mass-balance`)
    expect(response.status()).toBe(200)
    return (await response.json()) as Record<string, unknown>
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
