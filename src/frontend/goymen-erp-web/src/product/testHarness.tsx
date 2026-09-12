/**
 * DOM test harness for the Product screens.
 *
 * Mounts the REAL shell (`AppShell`, `ShellProvider`, `CapabilityRoute`) around the
 * Product routes, so every test goes through the same bootstrap, capability
 * resolution and entry gate the running app does. It does NOT build a second shell
 * or a second router — the Product lane reuses the shared ones and supplies only
 * its own route table.
 *
 * The fixtures below mirror the PUBLISHED wire shapes (`ProductPage`,
 * `ProductCardView`, `ProductMutationResponse`, `ProductCodeGroup`). They are
 * component evidence, not API evidence: no Product service, host mapping or SQL
 * exists, so nothing here proves a live endpoint.
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
export const GROUP_SAC = 'aaaaaaa1-0000-0000-0000-000000000001'
export const CLASS_FLAT = 'cccccccc-0000-0000-0000-000000000001'
export const FAMILY_FLAT = 'bbbbbbb1-0000-0000-0000-000000000001'

/** `ProductReferenceOptions` — classes/families plus the unavailable-catalogue list. */
export function referenceOptions(over: Record<string, unknown> = {}) {
  return {
    groupId: GROUP_SAC,
    classId: null,
    classes: [{ id: CLASS_FLAT, displayName: 'Yassı ürün' }],
    families: [{ id: FAMILY_FLAT, displayName: 'Soğuk hadde' }],
    units: [],
    trackingPolicies: [],
    templates: [],
    unavailableCatalogs: [],
    ...over,
  }
}

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

export function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

/** `ProductCodeGroup[]` — the only reference read the contract publishes. */
export function codeGroups() {
  return [{ id: GROUP_SAC, name: 'Sac', prefix: 'SAC' }]
}

/** `ProductRolesView`: every key present, each a tagged `{value, provenance}`. */
export function roles(over: Record<string, unknown> = {}) {
  return {
    purchasable: { value: true, provenance: 'manual' },
    sellable: { value: false, provenance: 'manual' },
    productionInput: { value: null, provenance: 'not-evaluated' },
    productionOutput: { value: true, provenance: 'derived' },
    ...over,
  }
}

/** `ProductDraftView` — the product core, shared by list items and the card. */
export function productCore(over: Record<string, unknown> = {}) {
  return {
    id: PRODUCT_1,
    code: 'SAC-42',
    name: 'Siyah sac',
    groupId: GROUP_SAC,
    classId: null,
    familyId: null,
    roles: roles(),
    lifecycle: 'Draft',
    version: '7',
    createdAtUtc: '2026-01-01T00:00:00+00:00',
    updatedAtUtc: '2026-09-12T07:00:00+00:00',
    labels: { group: 'Sac', class: null, family: null, primaryStockUnit: 'kg' },
    completeness: { evaluation: 'Incomplete', missingFields: ['references.units.stock'] },
    ...over,
  }
}

/** `ProductPage`. */
export function productPage(over: Record<string, unknown> = {}) {
  return { items: [productCore()], nextCursor: null, ...over }
}

/** `ProductReferences`. */
export function references(over: Record<string, unknown> = {}) {
  return {
    templateId: null,
    templateVersion: null,
    attributes: null,
    units: { stock: null, purchase: null, sales: null, price: null, production: null, package: null },
    trackingPolicyId: null,
    ...over,
  }
}

/**
 * `ProductCardView`. The card-level `completeness` is a compatibility projection of
 * `product.completeness` in the source, so the fixture emits it too — and the
 * decoder deliberately reads only the product one.
 */
export function productCard(over: Record<string, unknown> = {}) {
  const product = (over.product ?? productCore()) as Record<string, unknown>
  return {
    product,
    references: references(),
    completeness: product.completeness,
    ...over,
  }
}

/** `ProductMutationResponse`. */
export function mutation(over: Record<string, unknown> = {}) {
  return { card: productCard(), isReplay: false, ...over }
}

export interface RecordedCall {
  readonly url: string
  readonly method: string
  readonly companyId: string | null
  readonly idempotencyKey: string | null
  readonly body: string | null
}

export interface HarnessConfig {
  readonly capabilities?: readonly string[]
  /**
   * The capability generation the server answers with. A re-entry after a real
   * 403 legitimately receives a NEW one; a test that wants to prove recovery
   * across that change re-installs the mock with a different value mid-test.
   */
  readonly capabilityGeneration?: string
  /** The shell's company-directory read; failing it removes the whole outlet. */
  readonly onDirectory?: () => Response
  readonly onList?: (url: string) => Response
  readonly onCard?: (url: string) => Response
  readonly onGroups?: () => Response
  readonly onOptions?: (url: string) => Response
  readonly onCreate?: (attempt: number, body: string | null) => Response | Promise<Response>
  readonly onEdit?: (attempt: number, body: string | null) => Response | Promise<Response>
}

/**
 * Installs a fetch mock covering the shell bootstrap plus the Product endpoints.
 * Returns the recorded calls so a test can assert what actually left the client —
 * notably that no `X-Company-Id` is ever attached to a Product request.
 */
export function installFetch(config: HarnessConfig = {}) {
  const calls: RecordedCall[] = []
  let createAttempts = 0
  let editAttempts = 0

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
    if (url.includes('/organization/companies')) return config.onDirectory?.() ?? json(DIRECTORY)
    if (url.includes('/shell/capabilities')) {
      return json({
        contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION,
        generation: config.capabilityGeneration ?? 'gen-1',
        capabilities:
          config.capabilities ?? [
            PRODUCT_CAPABILITY.productsList,
            PRODUCT_CAPABILITY.productsDetail,
          ],
      })
    }
    if (url.includes('/products/code-groups')) {
      return config.onGroups?.() ?? json(codeGroups())
    }
    if (url.includes('/products/reference-options')) {
      return config.onOptions?.(url) ?? json(referenceOptions())
    }
    if (url.includes('/products')) {
      if (method === 'PATCH') {
        editAttempts += 1
        return (
          (await config.onEdit?.(editAttempts, body)) ??
          json(mutation({ card: productCard({ product: productCore({ version: '8' }) }) }))
        )
      }
      if (method === 'POST') {
        createAttempts += 1
        return (await config.onCreate?.(createAttempts, body)) ?? json(mutation(), 201)
      }
      // `/products/{id}` (and `/products/{id}/draft`) have a segment after
      // `products`; `/products?x` does not.
      if (/\/products\/[^?]+/.test(url)) return config.onCard?.(url) ?? json(productCard())
      return config.onList?.(url) ?? json(productPage())
    }
    throw new Error(`unexpected fetch: ${url}`)
  })

  vi.stubGlobal('fetch', fetchMock)
  return { calls }
}

/**
 * Renders the shell with the Product routes mounted, at `initialPath`.
 *
 * The router is returned so a test can drive a navigation the SCREEN did not
 * initiate — browser Back, a sidebar link — which is the class of exit the local
 * Cancel handler never saw. The shell's own nav has no Product entry here because
 * mounting it is the shell owner's delta, so the router is the honest stand-in.
 */
export function renderProductApp(
  initialPath: string,
): RenderResult & { readonly router: ReturnType<typeof createMemoryRouter> } {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: 0, refetchOnWindowFocus: false } },
  })
  const router = createMemoryRouter(
    [{ path: '/', element: <AppShell />, children: productRoutes }],
    { initialEntries: [initialPath] },
  )
  return {
    ...render(
      <ShellProvider queryClient={queryClient}>
        <RouterProvider router={router} />
      </ShellProvider>,
    ),
    router,
  }
}
