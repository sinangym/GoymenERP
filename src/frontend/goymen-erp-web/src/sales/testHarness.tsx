/**
 * DOM harness for the Sales screens: the REAL shell (`AppShell`, `ShellProvider`,
 * `CapabilityRoute`) around `salesRoutes`, a stubbed fetch, and a controlled selection
 * source. Component evidence only (NOT_ENV): no Sales API, owner provider or grant
 * exists, and nothing here proves one.
 */

import { useContext } from 'react'
import { render } from '@testing-library/react'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { QueryClient } from '@tanstack/react-query'
import { vi } from 'vitest'
import { ShellProvider } from '../shell/ShellProvider'
import { AppShell } from '../shell/AppShell'
import { SHELL_CAPABILITY_CONTRACT_VERSION } from '../shell/api'
import { SALES_CAPABILITY } from './capabilities'
import { salesRoutes } from './salesRoutes'
import {
  SalesContactInformationSourceContext,
  SalesCurrencySourceContext,
  SalesReferenceSourceContext,
  type UseContactInformation,
  type UseCurrencyOptions,
  type UseReferenceOptions,
} from './referenceSource'
import {
  SALES_READ_CONTRACT_HEADER,
  SALES_READ_CONTRACT_PARTY_DISPLAY,
  type ContactInformationOptions,
  type CurrencyOption,
} from './contract'
import type { OrderDraftOptions } from './orderDraftForm'

// Company A is the examples' company, so the Backend's own bodies describe it.
export const COMPANY_A = '99999999-9999-9999-9999-999999999999'
export const COMPANY_B = '88888888-8888-8888-8888-888888888888'
export const PARTY = '11111111-1111-1111-1111-111111111111'
export const ACCOUNT = '55555555-5555-5555-5555-555555555555'
export const CURRENCY = '33333333-3333-3333-3333-333333333333'
export const PRODUCT = '22222222-2222-2222-2222-222222222222'
export const KG = '44444444-4444-4444-4444-444444444444'
/** SALES-A2a: the controlled Cari contact information of the example account, and the currencies list of company A. */
export const ADDRESS = 'aaaaaaaa-1111-4111-8111-000000000401'
export const CONTACT = 'bbbbbbbb-1111-4111-8111-000000000401'
export const MONEY_PROFILE = 'cccccccc-1111-4111-8111-000000000401'
export const UNIT_PROFILE = 'dddddddd-1111-4111-8111-000000000401'
export const ADDRESS_TEXT = 'Sanayi Cad. 1, Kocaeli, TR'
export const CONTACT_NAME = 'Ayşe Yükleme'

export const ALL_SALES = [SALES_CAPABILITY.draftsList, SALES_CAPABILITY.draftsDetail, SALES_CAPABILITY.draftsCreate]
/** SALES-A1: the three screens plus the approve control the shell publishes to an approver. */
export const ALL_SALES_WITH_APPROVE = [...ALL_SALES, SALES_CAPABILITY.ordersApprove]

export const CURRENCIES_A: readonly CurrencyOption[] = [
  { currencyId: CURRENCY, code: 'TRY', profileId: MONEY_PROFILE, profileVersion: '3' },
]

export const CONTACT_INFORMATION_A: ContactInformationOptions = {
  partyId: PARTY,
  companyAccountId: ACCOUNT,
  accountVersion: '2',
  addresses: [{ id: ADDRESS, version: '4', purpose: 'delivery', text: ADDRESS_TEXT }],
  contacts: [{ id: CONTACT, version: '2', displayName: CONTACT_NAME, jobTitle: 'Sevkiyat', purpose: 'delivery' }],
}

/** A second account of company A (SALES-A2a: choosing it clears the delivery selects; it lists no address). */
export const OTHER_ACCOUNT = '55555555-5555-5555-5555-555555555556'

export const OPTIONS_A: OrderDraftOptions = {
  accounts: [
    { partyId: PARTY, companyAccountId: ACCOUNT, label: 'ABC Demir — A hesabı' },
    { partyId: PARTY, companyAccountId: OTHER_ACCOUNT, label: 'ABC Demir — B hesabı' },
  ],
  currencies: CURRENCIES_A.map((c) => ({ currencyId: c.currencyId, label: c.code })),
  products: [{
    productId: PRODUCT,
    productVersion: '7',
    code: 'PRF-40X40',
    name: '40x40 profil',
    label: 'PRF-40X40 — 40x40 profil',
    orderUnits: [{ unitId: KG, code: 'kg', label: 'kg', profileId: UNIT_PROFILE, profileVersion: '3' }],
  }],
}

/** Offers choices for company A only; B has no source, like the running app everywhere. */
export const controlledSource: UseReferenceOptions = (companyId) =>
  companyId === COMPANY_A ? { status: 'ready', options: OPTIONS_A } : { status: 'unavailable' }

/** The currencies list of company A for any real date; idle without a date (the real hook's shape). */
export const controlledCurrencies: UseCurrencyOptions = (companyId, effectiveOn) =>
  companyId === COMPANY_A && effectiveOn !== '' ? { status: 'ready', data: CURRENCIES_A } : { status: 'idle' }

/**
 * SALES-A2a: the header-only source of the running app on main — accounts and currencies real, products
 * `no-list-route` (E1) — so the header selects are shown and the lines / submit are withheld.
 */
export const useHeaderOnlySource: UseReferenceOptions = (companyId, businessDate) => {
  const useCurrencies = useContext(SalesCurrencySourceContext)
  const currencies = useCurrencies(companyId, businessDate)
  return companyId === COMPANY_A
    ? { status: 'unavailable', missing: [{ source: 'products', reason: 'no-list-route' }], header: { accounts: OPTIONS_A.accounts, accountsOffer: { items: OPTIONS_A.accounts, nextCursor: null, loadingMore: false, moreFailed: null, loadMore: () => {} }, currencies } }
    : { status: 'unavailable' }
}

/** The example account's contact information; another account lists nothing; no account chosen → idle. */
export const controlledContactInformation: UseContactInformation = (companyId, partyId, companyAccountId) =>
  partyId === '' || companyAccountId === '' || companyId === ''
    ? { status: 'idle' }
    : companyId === COMPANY_A && partyId === PARTY && companyAccountId === ACCOUNT
      ? { status: 'ready', data: CONTACT_INFORMATION_A }
      : { status: 'ready', data: { partyId, companyAccountId, accountVersion: '1', addresses: [], contacts: [] } }

/** D2: the harness's signed-in actor — a draft "of one's own" is one whose stored creator / submitter is this id. */
export const HARNESS_USER_ID = '77777777-7777-7777-7777-777777777777'

const IDENTITY = {
  userId: HARNESS_USER_ID,
  displayName: 'Satış kullanıcısı',
  email: null,
  allowedCompanyIds: [COMPANY_A, COMPANY_B],
  authenticationSessionBinding: null,
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

export interface RecordedCall {
  readonly url: string
  readonly method: string
  readonly companyId: string | null
  readonly idempotencyKey: string | null
  readonly body: string | null
  /** The Sales read selector the request sent (#626); null when it sent none. */
  readonly readContract: string | null
}

/**
 * SALES-CARI-OPTIN: the Cari texts the harness's server publishes for a Sales record's party (synthetic). Every party id
 * gets them, bound to that id — the decoder's own check is what binds a display to its record.
 */
export const HARNESS_PARTY_DISPLAY = { code: 'CARI-000123', legalName: 'ABC Demir Sanayi ve Ticaret A.Ş.', tradeName: 'ABC Demir' } as const

const isObject = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value)
const displayFor = (partyId: unknown) => ({ partyId, ...HARNESS_PARTY_DISPLAY })

/**
 * The #626 server, around a test's own legacy GET list/detail body: a request that sent the selector gets, on a 200, the
 * record-bound display added (where the body does not already carry one) and the exact acknowledgment — so the existing
 * component tests keep their legacy fixtures. A request without the selector, and any other status, pass unchanged.
 */
async function asPartyDisplayRead(response: Response, kind: 'list' | 'detail', call: RecordedCall): Promise<Response> {
  if (call.readContract !== SALES_READ_CONTRACT_PARTY_DISPLAY || response.status !== 200) return response
  const body: unknown = await response.clone().json().catch(() => undefined)
  let enriched: unknown = body
  if (kind === 'list' && isObject(body) && Array.isArray(body.items)) {
    enriched = {
      ...body,
      items: body.items.map((item: unknown) =>
        isObject(item) && !Object.hasOwn(item, 'partyDisplay') ? { ...item, partyDisplay: displayFor(item.partyId) } : item),
    }
  }
  if (kind === 'detail' && isObject(body) && isObject(body.draft) && !Object.hasOwn(body, 'partyDisplay')) {
    enriched = { ...body, partyDisplay: displayFor(body.draft.partyId) }
  }
  const headers: Record<string, string> = {}
  response.headers.forEach((value, name) => { headers[name] = value })
  return new Response(enriched === undefined ? await response.text() : JSON.stringify(enriched), {
    status: 200,
    headers: { ...headers, [SALES_READ_CONTRACT_HEADER]: SALES_READ_CONTRACT_PARTY_DISPLAY },
  })
}

/** DEC-SAL-003: a valid history holding only schedule 0 (the draft as created, nobody changed it). */
export const ORIGINAL_SCHEDULE = {
  items: [{
    scheduleNo: 0, changedBy: null, changedAtUtc: null, requestedDeliveryDate: null,
    lines: [{ lineId: '00000000-0000-4000-8000-000000000001', lineNo: 1, requestedDeliveryDate: null, slices: [] }],
  }],
}

export interface HarnessConfig {
  readonly capabilities?: readonly string[]
  readonly onList?: (url: string, companyId: string | null) => Response | Promise<Response>
  readonly onDetail?: (url: string, companyId: string | null) => Response | Promise<Response>
  readonly onRevisions?: (url: string, companyId: string | null) => Response | Promise<Response>
  readonly onCreate?: (attempt: number, call: RecordedCall) => Response | Promise<Response>
  /** DEC-SAL-003: GET `/sales/drafts/{id}/delivery-schedules`; default: schedule 0 only (nothing changed). */
  readonly onDeliverySchedules?: (url: string, companyId: string | null) => Response | Promise<Response>
  /**
   * AT-024: GET `/sales/drafts/{id}/delivery-progress`; default: nothing shipped or delivered for the lines of the
   * detail this harness last served for that draft (a 404 when it served none).
   */
  readonly onDeliveryProgress?: (url: string, companyId: string | null) => Response | Promise<Response>
  /**
   * DEC-SAL-005: GET `/sales/drafts/{id}/loading-progress`; default: no loading instruction for the lines of the detail
   * this harness last served for that draft — the owner's real zeros (a 404 when it served none).
   */
  readonly onLoadingProgress?: (url: string, companyId: string | null) => Response | Promise<Response>
  /** DEC-SAL-003: a POST on `/sales/drafts/{id}/delivery-schedule`. */
  readonly onDeliverySchedule?: (call: RecordedCall) => Response | Promise<Response>
  /** SALES-A1: a POST on `/sales/drafts/{id}/(submission|approval|rejection)`. */
  readonly onCommand?: (command: string, call: RecordedCall) => Response | Promise<Response>
  /** DEC-SAL-005: GET `/sales/drafts/{id}/cancellation-impact` (read only when the user asks for the preview). */
  readonly onCancellationImpact?: (url: string, companyId: string | null) => Response | Promise<Response>
  /** No 30: GET `/sales/drafts/{id}/force-close-impact` (read only when the user asks for the preview). */
  readonly onForceCloseImpact?: (url: string, companyId: string | null) => Response | Promise<Response>
  /** No 30: a POST on `/sales/drafts/{id}/force-close`. */
  readonly onForceClose?: (call: RecordedCall) => Response | Promise<Response>
  /** No 30: GET `/sales/drafts/{id}/force-close`; default: no recorded closure. */
  readonly onForceClosure?: (url: string, companyId: string | null) => Response | Promise<Response>
  /** DEC-SAL-005: a POST on `/sales/drafts/{id}/cancellation`. */
  readonly onCancel?: (call: RecordedCall) => Response | Promise<Response>
  /** AT-007: GET `/sales/drafts/{id}/cancellation`; default: no recorded cancellation. */
  readonly onCancellation?: (url: string, companyId: string | null) => Response | Promise<Response>
  /** DEC-SAL-002: a POST on `/sales/drafts/{id}/quantity-revision`. */
  readonly onQuantityRevision?: (call: RecordedCall) => Response | Promise<Response>
  /** AT-055: GET `/logistics/dispatch-notes?salesOrderId=…` (read only when the shell holds the Logistics instruction detail). */
  readonly onDispatchNotes?: (url: string, companyId: string | null) => Response | Promise<Response>
  /** K3 / AT-018: a POST on `/sales/drafts/{id}/commercial-revision`. */
  readonly onCommercialRevision?: (call: RecordedCall) => Response | Promise<Response>
  /** Reasoned maker-checker approval of the current below-floor price snapshots. */
  readonly onPriceFloorExceptionApproval?: (call: RecordedCall) => Response | Promise<Response>
  /** AT-018: GET `/sales/drafts/{id}/line-pricing`; default: 404 (the pricing is shown as unreadable). */
  readonly onLinePricing?: (url: string, companyId: string | null) => Response | Promise<Response>
  /** AT-018: GET `/sales/drafts/{id}/lines/{lineId}/price-references`. */
  readonly onPriceReferences?: (url: string, companyId: string | null) => Response | Promise<Response>
}

export function installFetch(config: HarnessConfig = {}) {
  const calls: RecordedCall[] = []
  let creates = 0
  // The last detail body served per detail URL: the default delivery progress describes exactly those lines.
  const details = new Map<string, unknown>()
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
        readContract: headers[SALES_READ_CONTRACT_HEADER] ?? null,
      }
      calls.push(call)
      if (url.includes('/identity/me')) return json(IDENTITY)
      if (url.includes('/organization/companies')) return json(DIRECTORY)
      if (url.includes('/shell/capabilities')) {
        return json({
          contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION,
          generation: 'gen-1',
          capabilities: config.capabilities ?? ALL_SALES,
        })
      }
      if (url.includes('/sales/drafts')) {
        const command = /\/sales\/drafts\/[^/?]+\/(submission|approval|rejection|withdrawal|customer-acceptance)$/.exec(url)?.[1]
        if (call.method === 'POST' && /\/sales\/drafts\/[^/?]+\/delivery-schedule$/.test(url)) {
          if (config.onDeliverySchedule === undefined) throw new Error('unexpected delivery-schedule update')
          return config.onDeliverySchedule(call)
        }
        if (call.method === 'POST' && /\/sales\/drafts\/[^/?]+\/commercial-revision$/.test(url)) {
          if (config.onCommercialRevision === undefined) throw new Error('unexpected commercial revision')
          return config.onCommercialRevision(call)
        }
        if (call.method === 'POST' && /\/sales\/drafts\/[^/?]+\/below-floor-exception-approval$/.test(url)) {
          if (config.onPriceFloorExceptionApproval === undefined) throw new Error('unexpected price-floor exception approval')
          return config.onPriceFloorExceptionApproval(call)
        }
        if (call.method === 'GET' && /\/sales\/drafts\/[^/?]+\/line-pricing$/.test(url)) {
          return config.onLinePricing?.(url, call.companyId) ?? json({ status: 404 }, 404)
        }
        if (call.method === 'GET' && /\/sales\/drafts\/[^/?]+\/lines\/[^/?]+\/price-references$/.test(url)) {
          if (config.onPriceReferences === undefined) throw new Error('unexpected price-references read')
          return config.onPriceReferences(url, call.companyId)
        }
        if (call.method === 'POST' && /\/sales\/drafts\/[^/?]+\/quantity-revision$/.test(url)) {
          if (config.onQuantityRevision === undefined) throw new Error('unexpected quantity revision')
          return config.onQuantityRevision(call)
        }
        if (call.method === 'GET' && /\/sales\/drafts\/[^/?]+\/force-close-impact$/.test(url)) {
          if (config.onForceCloseImpact === undefined) throw new Error('unexpected force-close-impact read')
          return config.onForceCloseImpact(url, call.companyId)
        }
        if (/\/sales\/drafts\/[^/?]+\/force-close$/.test(url)) {
          if (call.method === 'POST') {
            if (config.onForceClose === undefined) throw new Error('unexpected force-close')
            return config.onForceClose(call)
          }
          return config.onForceClosure?.(url, call.companyId) ?? json({ forceClosure: null })
        }
        if (call.method === 'GET' && /\/sales\/drafts\/[^/?]+\/cancellation-impact$/.test(url)) {
          if (config.onCancellationImpact === undefined) throw new Error('unexpected cancellation-impact read')
          return config.onCancellationImpact(url, call.companyId)
        }
        if (/\/sales\/drafts\/[^/?]+\/cancellation$/.test(url)) {
          if (call.method === 'POST') {
            if (config.onCancel === undefined) throw new Error('unexpected cancellation')
            return config.onCancel(call)
          }
          return config.onCancellation?.(url, call.companyId) ?? json({ cancellation: null })
        }
        if (call.method === 'GET' && /\/sales\/drafts\/[^/?]+\/delivery-progress$/.test(url)) {
          return config.onDeliveryProgress?.(url, call.companyId) ?? nothingDelivered(details.get(url.replace(/\/delivery-progress$/, '')))
        }
        if (call.method === 'GET' && /\/sales\/drafts\/[^/?]+\/loading-progress$/.test(url)) {
          return config.onLoadingProgress?.(url, call.companyId) ?? nothingLoaded(details.get(url.replace(/\/loading-progress$/, '')))
        }
        if (call.method === 'GET' && /\/sales\/drafts\/[^/?]+\/delivery-schedules$/.test(url)) {
          return config.onDeliverySchedules?.(url, call.companyId) ?? json(ORIGINAL_SCHEDULE)
        }
        if (call.method === 'GET' && /\/sales\/drafts\/[^/?]+\/remainder-changes$/.test(url)) {
          return noRemainderChanges(details.get(url.replace(/\/remainder-changes$/, '')))
        }
        if (call.method === 'POST' && command !== undefined) {
          if (config.onCommand === undefined) throw new Error(`unexpected ${command}`)
          return config.onCommand(command, call)
        }
        if (call.method === 'POST') {
          creates += 1
          if (config.onCreate === undefined) throw new Error('unexpected create')
          return config.onCreate(creates, call)
        }
        if (call.method === 'GET' && /\/sales\/drafts\/[^/?]+\/revisions$/.test(url)) {
          return config.onRevisions?.(url, call.companyId) ?? json({ items: [] })
        }
        if (/\/sales\/drafts\/[^?]+/.test(url)) {
          if (config.onDetail === undefined) throw new Error('unexpected detail read')
          const response = await asPartyDisplayRead(await config.onDetail(url, call.companyId), 'detail', call)
          if (response.ok) details.set(url, await response.clone().json().catch(() => undefined))
          return response
        }
        if (config.onList === undefined) throw new Error('unexpected list read')
        return asPartyDisplayRead(await config.onList(url, call.companyId), 'list', call)
      }
      if (call.method === 'GET' && url.includes('/logistics/dispatch-notes?')) {
        if (config.onDispatchNotes === undefined) throw new Error('unexpected dispatch-notes read')
        return config.onDispatchNotes(url, call.companyId)
      }
      throw new Error(`unexpected fetch: ${url}`)
    }),
  )
  return { calls, salesCalls: () => calls.filter((c) => c.url.includes('/sales/')) }
}

/** AT-024: the default progress — every line of the served detail, nothing shipped or delivered, the whole quantity remaining. */
function nothingDelivered(detail: unknown): Response {
  const body = detail as { draft?: { version?: unknown }; lines?: { lineId: string; lineNo: number; orderUnitId: string; quantity: string }[] } | undefined
  if (body?.draft === undefined || !Array.isArray(body.lines)) return json({ status: 404 }, 404)
  return json({
    draftVersion: body.draft.version,
    lines: body.lines.map((line) => ({
      lineId: line.lineId, lineNo: line.lineNo, orderUnitId: line.orderUnitId, ordered: line.quantity,
      shipped: '0', delivered: '0', remaining: line.quantity, excess: '0',
    })),
  })
}

/** DEC-SAL-005: the default loading progress — every line of the served detail, with no loading instruction at all. */
function nothingLoaded(detail: unknown): Response {
  const body = detail as { draft?: { version?: unknown }; lines?: { lineId: string; lineNo: number; orderUnitId: string; quantity: string }[] } | undefined
  if (typeof body?.draft?.version !== 'string' || !Array.isArray(body.lines)) return json({ status: 404 }, 404)
  return json({
    draftVersion: body.draft.version,
    lines: body.lines.map((line) => ({
      lineId: line.lineId, lineNo: line.lineNo, orderUnitId: line.orderUnitId, ordered: line.quantity,
      loadingInstructionLines: 0, instructed: '0', loaded: '0', shipped: '0', delivered: '0',
    })),
  })
}

/** A valid owner-backed empty remainder read for detail tests that are not exercising remainder mutations. */
function noRemainderChanges(detail: unknown): Response {
  const body = detail as { draft?: { version?: unknown }; lines?: { lineId: string; lineNo: number; orderUnitId: string; quantity: string }[] } | undefined
  if (typeof body?.draft?.version !== 'string' || !Array.isArray(body.lines)) return json({ status: 404 }, 404)
  return json({
    draftVersion: body.draft.version,
    floorStatus: 'Readable',
    lines: body.lines.map((line) => ({
      lineId: line.lineId,
      lineNo: line.lineNo,
      orderUnitId: line.orderUnitId,
      quantity: line.quantity,
      remainder: 'Open',
      loaded: '0',
      shipped: '0',
      delivered: '0',
      minimum: '0',
    })),
    items: [],
  })
}

export interface HarnessSources {
  readonly currencies?: UseCurrencyOptions
  readonly contactInformation?: UseContactInformation
}

export function renderSalesApp(
  initialPath: string,
  source: UseReferenceOptions = controlledSource,
  sources: HarnessSources = {},
) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: 0, refetchOnWindowFocus: false } },
  })
  const router = createMemoryRouter([{ path: '/', element: <AppShell />, children: salesRoutes }], {
    initialEntries: [initialPath],
  })
  return {
    ...render(
      <SalesReferenceSourceContext.Provider value={source}>
        <SalesCurrencySourceContext.Provider value={sources.currencies ?? controlledCurrencies}>
          <SalesContactInformationSourceContext.Provider value={sources.contactInformation ?? controlledContactInformation}>
            <ShellProvider queryClient={queryClient}>
              <RouterProvider router={router} />
            </ShellProvider>
          </SalesContactInformationSourceContext.Provider>
        </SalesCurrencySourceContext.Provider>
      </SalesReferenceSourceContext.Provider>,
    ),
    router,
    queryClient,
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
