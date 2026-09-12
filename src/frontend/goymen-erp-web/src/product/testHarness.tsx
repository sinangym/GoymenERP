/**
 * DOM test harness for the Product screens.
 *
 * Mounts the REAL shell (`AppShell`, `ShellProvider`, `CapabilityRoute`) around the
 * Product routes, so every test goes through the same bootstrap, capability
 * resolution and entry gate the running app does. It does NOT build a second shell
 * or a second router — the Product lane reuses the shared ones and only supplies
 * its own route table (./productRoutes.tsx).
 *
 * The Cari page tests import `shellRoutes` directly because their routes are
 * already in it. The Product routes are not, and mounting them is the shell
 * owner's handoff step (./SHARED_MOUNT_HANDOFF.md), so this harness composes the
 * same layout route here instead.
 */

import { render, type RenderResult } from '@testing-library/react'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { QueryClient } from '@tanstack/react-query'
import { vi } from 'vitest'
import { ShellProvider } from '../shell/ShellProvider'
import { AppShell } from '../shell/AppShell'
import { SHELL_CAPABILITY_CONTRACT_VERSION } from '../shell/api'
import { productRoutes } from './productRoutes'
import { PRODUCT_CAPABILITY } from './capabilities'

export const COMPANY_A = 'aaaaaaaa-0000-0000-0000-000000000001'
export const COMPANY_B = 'bbbbbbbb-0000-0000-0000-000000000002'
export const PRODUCT_1 = '11111111-1111-1111-1111-111111111111'
export const PRODUCT_2 = '22222222-2222-2222-2222-222222222222'

const IDENTITY = {
  userId: '99999999-9999-9999-9999-999999999999',
  displayName: 'Kullanıcı A',
  email: null,
  allowedCompanyIds: [COMPANY_A, COMPANY_B],
}

const DIRECTORY = [
  { id: COMPANY_A, code: 'GDC', legalName: 'Göymen A', isPlaceholder: true },
  { id: COMPANY_B, code: 'GSP', legalName: 'Göymen B', isPlaceholder: true },
]

export function json(body: unknown, status = 200, headers: Record<string, string> = {}): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', ...headers },
  })
}

/** A list row with every field the contract declares, overridable per test. */
export function productItem(over: Record<string, unknown> = {}) {
  return {
    id: PRODUCT_1,
    code: 'SAC-42',
    name: 'Siyah sac',
    classification: { class: 'Yassı ürün', group: 'Sac', family: 'Soğuk hadde' },
    roles: {
      purchasable: { value: true, provenance: 'manual' },
      sellable: { value: false, provenance: 'manual' },
      productionInput: { value: null, provenance: 'not-evaluated' },
      productionOutput: { value: true, provenance: 'derived' },
    },
    lifecycle: 'Active',
    primaryStockUnit: 'kg',
    completeness: { template: 'complete', unit: 'incomplete', package: 'unknown' },
    match: { reason: 'code', matchedAlias: null },
    lastChangedAt: '2026-09-12T07:00:00Z',
    version: '7',
    ...over,
  }
}

export function productPage(over: Record<string, unknown> = {}) {
  return {
    items: [productItem()],
    nextCursor: null,
    hasMore: false,
    totalCount: null,
    countScope: null,
    ...over,
  }
}

export function productDetail(over: Record<string, unknown> = {}) {
  return {
    id: PRODUCT_1,
    code: 'SAC-42',
    name: 'Siyah sac',
    classification: { class: 'Yassı ürün', group: 'Sac', family: null },
    roles: productItem().roles,
    lifecycle: 'Active',
    completeness: { template: 'complete', unit: 'incomplete', package: 'unknown' },
    version: '7',
    aliases: [{ code: 'ESKI-1', retiredAt: '2026-01-02T00:00:00Z' }],
    template: {
      id: '33333333-3333-3333-3333-333333333333',
      name: 'Yassı ürün şablonu',
      version: 'v3',
    },
    unitRoles: {
      stock: 'kg',
      purchase: 'kg',
      sales: 'kg',
      price: 'kg',
      production: null,
      package: null,
    },
    trackingPolicy: 'Parti bazlı',
    createdAt: '2026-01-01T00:00:00Z',
    createdBy: 'sinan',
    updatedAt: '2026-09-12T07:00:00Z',
    updatedBy: 'sinan',
    lastActivatedAt: '2026-02-01T00:00:00Z',
    lastDeactivatedAt: null,
    deactivationReason: null,
    availableSections: ['template', 'units', 'aliasHistory'],
    ...over,
  }
}

export interface RecordedCall {
  readonly url: string
  readonly method: string
  readonly companyId: string | null
  readonly idempotencyKey: string | null
  readonly body: string | null
}

export interface HarnessConfig {
  /** Capabilities the server publishes. Defaults to list + detail. */
  readonly capabilities?: readonly string[]
  /** Answers `GET /products…`. */
  readonly onList?: (url: string) => Response
  /** Answers `GET /products/{id}`. */
  readonly onDetail?: (url: string) => Response
  /** Answers `POST /products`. Receives the call index, so a retry can differ. */
  readonly onCreate?: (attempt: number, body: string | null) => Response | Promise<Response>
}

/**
 * Installs a fetch mock covering the shell bootstrap plus the Product endpoints.
 * Returns the recorded calls so a test can assert what actually left the client —
 * notably that no `X-Company-Id` is ever attached to a Product request.
 */
export function installFetch(config: HarnessConfig = {}) {
  const calls: RecordedCall[] = []
  let createAttempts = 0

  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = typeof input === 'string' ? input : input.toString()
    const headers = (init?.headers ?? {}) as Record<string, string>
    const method = (init?.method ?? 'GET').toUpperCase()
    const body = typeof init?.body === 'string' ? init.body : null
    calls.push({
      url,
      method,
      companyId: headers['X-Company-Id'] ?? null,
      idempotencyKey: headers['Idempotency-Key'] ?? null,
      body,
    })

    if (url.includes('/identity/me')) return json(IDENTITY)
    if (url.includes('/organization/companies')) return json(DIRECTORY)
    if (url.includes('/shell/capabilities')) {
      return json({
        contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION,
        generation: 'gen-1',
        capabilities:
          config.capabilities ?? [
            PRODUCT_CAPABILITY.productsList,
            PRODUCT_CAPABILITY.productsDetail,
          ],
      })
    }
    if (url.includes('/products')) {
      if (method === 'POST') {
        createAttempts += 1
        return (
          (await config.onCreate?.(createAttempts, body)) ??
          json({ id: PRODUCT_1, code: 'SAC-42', name: 'Siyah sac', lifecycle: 'Draft', version: '1' }, 201)
        )
      }
      // `/products/{id}` has a segment after `products`; `/products?x` does not.
      const isDetail = /\/products\/[^?]+/.test(url)
      if (isDetail) return config.onDetail?.(url) ?? json(productDetail())
      return config.onList?.(url) ?? json(productPage())
    }
    throw new Error(`unexpected fetch: ${url}`)
  })

  vi.stubGlobal('fetch', fetchMock)
  return { calls }
}

/** Renders the shell with the Product routes mounted, at `initialPath`. */
export function renderProductApp(initialPath: string): RenderResult {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: 0, refetchOnWindowFocus: false } },
  })
  const router = createMemoryRouter(
    [{ path: '/', element: <AppShell />, children: productRoutes }],
    { initialEntries: [initialPath] },
  )
  return render(
    <ShellProvider queryClient={queryClient}>
      <RouterProvider router={router} />
    </ShellProvider>,
  )
}
