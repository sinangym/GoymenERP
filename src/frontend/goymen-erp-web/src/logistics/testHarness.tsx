/**
 * DOM harness for the Logistics screens: the REAL shell (`AppShell`, `ShellProvider`,
 * `CapabilityRoute`) around `logisticsRoutes` and a stubbed fetch. Component evidence only
 * (NOT_ENV): the API answers here are the harness's, and nothing here proves a server or a grant.
 */

import { render } from '@testing-library/react'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { QueryClient } from '@tanstack/react-query'
import { vi } from 'vitest'
import { ShellProvider } from '../shell/ShellProvider'
import { AppShell } from '../shell/AppShell'
import { SHELL_CAPABILITY_CONTRACT_VERSION } from '../shell/api'
import { LOGISTICS_CAPABILITY } from './capabilities'
import { logisticsRoutes } from './logisticsRoutes'

export const COMPANY_A = '99999999-9999-9999-9999-999999999999'
export const COMPANY_B = '88888888-8888-8888-8888-888888888888'
export const PARTY = '11111111-1111-1111-1111-111111111111'
export const ACCOUNT = '55555555-5555-5555-5555-555555555555'
export const PRODUCT = '22222222-2222-2222-2222-222222222222'
export const KG = '44444444-4444-4444-4444-444444444444'
export const DRAFT = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
export const DRAFT_LINE = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'
export const INSTRUCTION = 'cccccccc-cccc-cccc-cccc-cccccccccccc'
export const LINE = 'dddddddd-dddd-dddd-dddd-dddddddddddd'

export const ALL_LOGISTICS = [
  LOGISTICS_CAPABILITY.instructionsList,
  LOGISTICS_CAPABILITY.instructionsDetail,
  LOGISTICS_CAPABILITY.instructionsCreate,
]
export const READ_ONLY_LOGISTICS = [LOGISTICS_CAPABILITY.instructionsList, LOGISTICS_CAPABILITY.instructionsDetail]

const IDENTITY = {
  userId: '77777777-7777-7777-7777-777777777777',
  displayName: 'Sevkiyat kullanıcısı',
  email: null,
  allowedCompanyIds: [COMPANY_A, COMPANY_B],
}

const DIRECTORY = [
  { id: COMPANY_A, code: 'GDC', legalName: 'Göymen A', isPlaceholder: true },
  { id: COMPANY_B, code: 'GSP', legalName: 'Göymen B', isPlaceholder: true },
]

export function json(body: unknown, status = 200, headers: Record<string, string> = {}): Response {
  return new Response(typeof body === 'string' ? body : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', ...headers },
  })
}

/* ── Synthetic wire bodies (the LOG-4a shapes) ───────────────────────────── */

export function instructionSummary(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    id: INSTRUCTION,
    companyId: COMPANY_A,
    status: 'Draft',
    version: '2',
    customerPartyId: PARTY,
    companyAccountId: ACCOUNT,
    effectiveOn: '2026-09-19',
    createdAtUtc: '2026-09-19T09:00:00Z',
    updatedAtUtc: '2026-09-19T09:00:00Z',
    lineCount: 1,
    ...overrides,
  }
}

export function instructionLine(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    lineId: LINE,
    lineNo: 1,
    sourceOrderId: DRAFT,
    sourceOrderLineId: DRAFT_LINE,
    sourceOrderRevision: 0,
    productId: PRODUCT,
    unitId: KG,
    instructed: '4.5',
    loaded: '0',
    shipped: '0',
    delivered: '0',
    remainingToLoad: '4.5',
    loadedNotShipped: '0',
    shippedNotDelivered: '0',
    isBlocked: false,
    ...overrides,
  }
}

export function instructionDetail(
  summary: Record<string, unknown> = {},
  lines: readonly Record<string, unknown>[] = [instructionLine()],
  shares: readonly Record<string, unknown>[] = [],
  weighing: Record<string, unknown> | null = null,
): Record<string, unknown> {
  return { instruction: instructionSummary({ lineCount: lines.length, ...summary }), lines, shares, weighing }
}

/** AT-053: a weighbridge record as the backend answers it (its calculation, never the client's). */
export function weighingRecord(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    weighingId: 'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee',
    direction: 'TareFirst',
    firstWeighingKg: '15000',
    secondWeighingKg: '15004.3',
    tareKg: '15000',
    grossKg: '15004.3',
    netKg: '4.3',
    loadedKgAtWeighing: '4.5',
    differenceKg: '-0.2',
    recordedBy: 'SENTETIK-kantar',
    recordedAtUtc: '2026-09-19T09:20:00Z',
    ...overrides,
  }
}

export function mutation(detail: Record<string, unknown>, isReplay = false, originalCorrelationId = 'corr-1'): Record<string, unknown> {
  return { detail, isReplay, originalCorrelationId }
}

/** One APPROVED order line as the price-free Logistics read answers it (LOG-4b): exactly the sixteen wire members. */
export function approvedOrderLine(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    orderId: DRAFT,
    documentNo: 'SO-2026-0001',
    lineId: DRAFT_LINE,
    lineNo: 1,
    customerPartyId: PARTY,
    companyAccountId: ACCOUNT,
    customerName: 'SENTETIK browser-suite:l04 Yükleme müşterisi',
    productId: PRODUCT,
    productVersion: '7',
    productCode: 'GDC-001',
    productName: 'Sentetik bant',
    unitId: KG,
    unitCode: 'kg',
    quantity: '6',
    requestedDeliveryDate: '2026-09-25',
    revision: 0,
    ...overrides,
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
  readonly capabilities?: readonly string[]
  readonly onList?: (url: string, companyId: string | null) => Response | Promise<Response>
  readonly onDetail?: (url: string, companyId: string | null) => Response | Promise<Response>
  readonly onCreate?: (attempt: number, call: RecordedCall) => Response | Promise<Response>
  readonly onIssue?: (call: RecordedCall) => Response | Promise<Response>
  readonly onLoad?: (lineId: string, call: RecordedCall) => Response | Promise<Response>
  readonly onShipment?: (lineId: string, call: RecordedCall) => Response | Promise<Response>
  readonly onDelivery?: (lineId: string, call: RecordedCall) => Response | Promise<Response>
  readonly onWeighing?: (call: RecordedCall) => Response | Promise<Response>
  readonly onAllocations?: (call: RecordedCall) => Response | Promise<Response>
  /** The price-free approved-order-lines read the picker uses. */
  readonly onApprovedLines?: (url: string, companyId: string | null) => Response | Promise<Response>
}

export function installFetch(config: HarnessConfig = {}) {
  const calls: RecordedCall[] = []
  let creates = 0
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString()
      const headers = (init?.headers ?? {}) as Record<string, string>
      const call: RecordedCall = {
        url,
        method: (init?.method ?? 'GET').toUpperCase(),
        companyId: headers['X-Company-Id'] ?? null,
        idempotencyKey: headers['Idempotency-Key'] ?? null,
        body: typeof init?.body === 'string' ? init.body : null,
      }
      calls.push(call)
      if (url.includes('/identity/me')) return json(IDENTITY)
      if (url.includes('/organization/companies')) return json(DIRECTORY)
      if (url.includes('/shell/capabilities')) {
        return json({
          contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION,
          generation: 'gen-1',
          capabilities: config.capabilities ?? ALL_LOGISTICS,
        })
      }
      if (url.includes('/logistics/loading-instructions/approved-order-lines')) {
        if (config.onApprovedLines === undefined) throw new Error('unexpected approved-order-lines read')
        return config.onApprovedLines(url, call.companyId)
      }
      if (url.includes('/inventory/physical-allocations')) {
        if (config.onAllocations === undefined) throw new Error('unexpected allocation read')
        return config.onAllocations(call)
      }
      if (url.includes('/logistics/loading-instructions')) {
        const realization = /\/lines\/([^/?]+)\/(shipments|deliveries)$/.exec(url)
        if (call.method === 'POST' && realization !== null) {
          const handle = realization[2] === 'shipments' ? config.onShipment : config.onDelivery
          if (handle === undefined) throw new Error('unexpected realization')
          return handle(decodeURIComponent(realization[1]), call)
        }
        const load = /\/logistics\/loading-instructions\/[^/?]+\/lines\/([^/?]+)\/loads$/.exec(url)?.[1]
        if (call.method === 'POST' && load !== undefined) {
          if (config.onLoad === undefined) throw new Error('unexpected load')
          return config.onLoad(decodeURIComponent(load), call)
        }
        if (call.method === 'POST' && /\/weighings$/.test(url)) {
          if (config.onWeighing === undefined) throw new Error('unexpected weighing')
          return config.onWeighing(call)
        }
        if (call.method === 'POST' && /\/issue$/.test(url)) {
          if (config.onIssue === undefined) throw new Error('unexpected issue')
          return config.onIssue(call)
        }
        if (call.method === 'POST') {
          creates += 1
          if (config.onCreate === undefined) throw new Error('unexpected create')
          return config.onCreate(creates, call)
        }
        if (/\/logistics\/loading-instructions\/[^?]+/.test(url)) {
          if (config.onDetail === undefined) throw new Error('unexpected detail read')
          return config.onDetail(url, call.companyId)
        }
        if (config.onList === undefined) throw new Error('unexpected list read')
        return config.onList(url, call.companyId)
      }
      throw new Error(`unexpected fetch: ${url}`)
    }),
  )
  return { calls, logisticsCalls: () => calls.filter((c) => c.url.includes('/logistics/')) }
}

export function renderLogisticsApp(initialPath: string) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: 0, refetchOnWindowFocus: false } },
  })
  const router = createMemoryRouter([{ path: '/', element: <AppShell />, children: logisticsRoutes }], {
    initialEntries: [initialPath],
  })
  return {
    ...render(
      <ShellProvider queryClient={queryClient}>
        <RouterProvider router={router} />
      </ShellProvider>,
    ),
    router,
  }
}

/** A promise the test resolves by hand: a real pending request, not a stub that settles at once. */
export function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

/** Every word that must never appear on a Logistics screen (Business 5743412166). */
export const FORBIDDEN_MONEY_WORDS = ['₺', 'fiyat', 'Fiyat', 'price', 'unitPrice', 'iskonto', 'İskonto', 'discount', 'para birimi'] as const
