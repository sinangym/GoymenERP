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
  /**
   * SAT-D8 / API-10: GET `/sales/drafts/{id}/customer-returns`; default: no approved return on any line of the detail this
   * harness last served for that draft — the owner's real zeros (a 404 when it served none).
   */
  readonly onCustomerReturns?: (url: string, companyId: string | null) => Response | Promise<Response>
  /**
   * DEC-SAL-001 / API-08: GET `/sales/drafts/{id}/status`; default: the seven axes of the detail this harness last served
   * for that draft — nothing shipped, no tolerance asked, the three owner-less axes `NoSource` (a 404 when it served none).
   */
  readonly onStatus?: (url: string, companyId: string | null) => Response | Promise<Response>
  /**
   * API-03: GET `/sales/drafts/{id}/totals`; default: the totals of the detail this harness last served for that draft as the
   * fake server's own fixture (`totalsOf`) — tax `NoSource` (no VAT master), an unpriced line `Unpriced` (a 404 when it served none).
   */
  readonly onTotals?: (url: string, companyId: string | null) => Response | Promise<Response>
  /**
   * MOD-01 / API-06: GET `/sales/drafts/{id}/risk-check`; default: today's answer for the detail this harness last served for
   * that draft (`riskCheckOf`) — fail-closed `Unavailable` (no invoices, no VAT-included totals, no limit master), amounts
   * withheld exactly when a line's price is (a 404 when it served none).
   */
  readonly onRiskCheck?: (url: string, companyId: string | null) => Response | Promise<Response>
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
  /** API-04: GET `/sales/drafts/{id}/header` (the S-04 summary and the header-revision form); default: `headerOf` the served detail. */
  readonly onHeader?: (url: string, companyId: string | null) => Response | Promise<Response>
  /** API-04: a POST on `/sales/drafts/{id}/header-revision`. */
  readonly onHeaderRevision?: (call: RecordedCall) => Response | Promise<Response>
  /** SAT-D27: GET `/sales/references/sales-owners`; default: nobody listed for the bound company. */
  readonly onSalesOwners?: (url: string, companyId: string | null) => Response | Promise<Response>
  /** AT-055: GET `/logistics/dispatch-notes?salesOrderId=…` (read only when the shell holds the Logistics instruction detail). */
  readonly onDispatchNotes?: (url: string, companyId: string | null) => Response | Promise<Response>
  /** K3 / AT-018: a POST on `/sales/drafts/{id}/commercial-revision`. */
  readonly onCommercialRevision?: (call: RecordedCall) => Response | Promise<Response>
  /** Reasoned maker-checker approval of the current below-floor price snapshots. */
  readonly onPriceFloorExceptionApproval?: (call: RecordedCall) => Response | Promise<Response>
  /** AT-018: GET `/sales/drafts/{id}/line-pricing`; default: 404 (the pricing is shown as unreadable). */
  readonly onLinePricing?: (url: string, companyId: string | null) => Response | Promise<Response>
  /** UI-11: GET `/documents?ownerKind=sales.order&ownerId=…` (read only once the "Sözleşme / Belgeler" tab is opened). */
  readonly onDocuments?: (url: string, companyId: string | null) => Response | Promise<Response>
  /** MOD-07: GET `/sales/drafts/{id}/pdf` (read only when the user asks for the PDF). */
  readonly onPdf?: (call: RecordedCall) => Response | Promise<Response>
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
        if (call.method === 'GET' && /\/sales\/drafts\/[^/?]+\/pdf$/.test(url)) {
          if (config.onPdf === undefined) throw new Error('unexpected order PDF read')
          return config.onPdf(call)
        }
        if (call.method === 'GET' && /\/sales\/drafts\/[^/?]+\/line-pricing$/.test(url)) {
          return config.onLinePricing?.(url, call.companyId) ?? json({ status: 404 }, 404)
        }
        if (call.method === 'GET' && /\/sales\/drafts\/[^/?]+\/lines\/[^/?]+\/price-references$/.test(url)) {
          if (config.onPriceReferences === undefined) throw new Error('unexpected price-references read')
          return config.onPriceReferences(url, call.companyId)
        }
        if (call.method === 'GET' && /\/sales\/drafts\/[^/?]+\/header$/.test(url)) {
          // API-04: the S-04 summary reads the header on every open; default: the served detail's header, no due terms.
          return config.onHeader?.(url, call.companyId) ?? headerOf(details.get(url.replace(/\/header$/, '')))
        }
        if (call.method === 'POST' && /\/sales\/drafts\/[^/?]+\/header-revision$/.test(url)) {
          if (config.onHeaderRevision === undefined) throw new Error('unexpected header revision')
          return config.onHeaderRevision(call)
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
        if (call.method === 'GET' && /\/sales\/drafts\/[^/?]+\/customer-returns$/.test(url)) {
          return config.onCustomerReturns?.(url, call.companyId) ?? nothingReturned(details.get(url.replace(/\/customer-returns$/, '')))
        }
        if (call.method === 'GET' && /\/sales\/drafts\/[^/?]+\/status$/.test(url)) {
          return config.onStatus?.(url, call.companyId) ?? statusOf(details.get(url.replace(/\/status$/, '')))
        }
        if (call.method === 'GET' && /\/sales\/drafts\/[^/?]+\/risk-check$/.test(url)) {
          return config.onRiskCheck?.(url, call.companyId) ?? riskCheckOf(details.get(url.replace(/\/risk-check$/, '')))
        }
        if (call.method === 'GET' && /\/sales\/drafts\/[^/?]+\/totals$/.test(url)) {
          return config.onTotals?.(url, call.companyId) ?? totalsOf(details.get(url.replace(/\/totals$/, '')))
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
      if (call.method === 'GET' && url.includes('/sales/references/sales-owners')) {
        return config.onSalesOwners?.(url, call.companyId) ?? json({ companyId: call.companyId, owners: [], hasMore: false })
      }
      if (call.method === 'GET' && url.includes('/documents?')) {
        if (config.onDocuments === undefined) throw new Error('unexpected documents read')
        return config.onDocuments(url, call.companyId)
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

/** SAT-D8 / API-10: the default returns — every line of the served detail, nothing approved as returned. */
function nothingReturned(detail: unknown): Response {
  const body = detail as { draft?: { version?: unknown }; lines?: { lineId: string; lineNo: number; orderUnitId: string; quantity: string }[] } | undefined
  if (typeof body?.draft?.version !== 'string' || !Array.isArray(body.lines)) return json({ status: 404 }, 404)
  return json({
    draftVersion: body.draft.version,
    lines: body.lines.map((line) => ({
      lineId: line.lineId, lineNo: line.lineNo, orderUnitId: line.orderUnitId, ordered: line.quantity, approvedReturned: '0',
    })),
  })
}

/**
 * DEC-SAL-001 / API-08: the default status — the served detail's own lifecycle (`Active` once approved or revised, SAT-D32),
 * approval and acceptance; every line `NotStarted` against the current revision; source, invoicing and financial closure
 * `NoSource`; never ready to close.
 */
export function statusOf(detail: unknown): Response {
  const body = detail as {
    draft?: { version?: unknown; revision?: string; state?: string; approvalState?: string }
    customerAcceptance?: { required?: boolean; isCurrentRevisionAccepted?: boolean; acceptedRevision?: string | null }
    lines?: { lineId: string; lineNo: number; orderUnitId: string; quantity: string }[]
  } | undefined
  if (typeof body?.draft?.version !== 'string' || !Array.isArray(body.lines) || body.customerAcceptance === undefined) {
    return json({ status: 404 }, 404)
  }
  const { draft, customerAcceptance: acceptance } = body
  const everApproved = draft.approvalState === 'Approved' || draft.revision !== 'Rev.0'
  return json({
    draftVersion: draft.version,
    revision: draft.revision,
    lifecycle: { state: draft.state === 'Draft' && everApproved ? 'Active' : draft.state },
    internalApproval: { state: draft.approvalState },
    customerAcceptance: {
      state: !acceptance.required ? 'NotRequired' : acceptance.isCurrentRevisionAccepted ? 'Accepted' : 'Pending',
      acceptedRevision: acceptance.acceptedRevision ?? null,
    },
    source: { state: 'NoSource' },
    realization: {
      state: 'NotStarted',
      warning: false,
      basisRevision: draft.revision,
      tolerance: null,
      lines: body.lines.map((line) => ({
        lineId: line.lineId, lineNo: line.lineNo, orderUnitId: line.orderUnitId, ordered: line.quantity, shipped: '0',
        delivered: '0', state: 'NotStarted', direction: 'Under', deviation: line.quantity, band: null, warning: false,
        approvedOverDelivery: false, closedRemainder: null, approvedReturned: '0',
      })),
    },
    invoicing: { state: 'NoSource', ...invoicingGateOf(draft.approvalState, draft.revision, acceptance) },
    financialClosure: { state: 'NoSource' },
    readyToClose: false,
  })
}

/** SAT-D103: the gate the server answers for the detail (the backend's `InvoicingGate`). */
function invoicingGateOf(
  approvalState: string | undefined,
  revision: string | undefined,
  acceptance: { required?: boolean; isCurrentRevisionAccepted?: boolean; acceptedRevision?: string | null },
): { gate: string; priceBasisRevision: string | null } {
  const revised = revision !== undefined && revision !== 'Rev.0'
  const waiting = { gate: 'AwaitingCustomerAcceptance', priceBasisRevision: null }
  if (approvalState === 'Approved') {
    return acceptance.required && !acceptance.isCurrentRevisionAccepted ? waiting : { gate: 'Open', priceBasisRevision: revision ?? null }
  }
  if (revised && (approvalState === 'Preparing' || approvalState === 'PendingApproval')) return { gate: 'AwaitingReapproval', priceBasisRevision: null }
  if (revised && approvalState === 'Rejected') {
    const last = `Rev.${Number(revision.slice(4)) - 1}`
    return acceptance.required && acceptance.acceptedRevision !== last ? waiting : { gate: 'Open', priceBasisRevision: last }
  }
  return { gate: 'NotApproved', priceBasisRevision: null }
}

/** Exact decimal text → (digits, scale) for the fake server's fixture amounts; never a JS number. */
function exactDecimal(text: string): { readonly digits: bigint; readonly scale: number } {
  const [whole, fraction = ''] = text.split('.')
  return { digits: BigInt(`${whole}${fraction}`), scale: fraction.length }
}

/**
 * The FAKE SERVER's fixture amount for a priced line in its order unit: quantity × price × (100 − discount %) / 100, rounded
 * half away from zero to 2 decimals. Test data only — the screen never computes an amount; this stands in for the backend.
 */
function fixtureAmount(quantity: string, price: string, discountPercent: string | null): { readonly net: string; readonly amount: string } {
  const q = exactDecimal(quantity)
  const p = exactDecimal(price)
  const d = exactDecimal(discountPercent ?? '0')
  // net unit price = p × (100 − d) / 100, at scale p.scale + d.scale + 2
  const netDigits = p.digits * (100n * 10n ** BigInt(d.scale) - d.digits)
  const netScale = p.scale + d.scale + 2
  const product = q.digits * netDigits
  const scale = q.scale + netScale
  const divisor = 10n ** BigInt(scale - 2)
  const rounded = (product * 2n + divisor) / (divisor * 2n)
  const text = (digits: bigint, at: number) => {
    const padded = digits.toString().padStart(at + 1, '0')
    return at === 0 ? padded : `${padded.slice(0, -at)}.${padded.slice(-at)}`
  }
  const net = text(netDigits, netScale).replace(/\.?0+$/, '')
  return { net: net === '' ? '0' : net, amount: text(rounded, 2) }
}

/**
 * API-03: the default totals — every line of the served detail; a priced line in its order unit `Computed` (the fixture
 * amount), an unpriced one `Unpriced`; tax `NoSource` (no VAT master exists). A detail with withheld prices is 403.
 */
export function totalsOf(detail: unknown): Response {
  const body = detail as {
    draft?: { version?: unknown; revision?: string; currencyId?: string }
    lines?: { lineId: string; lineNo: number; orderUnitId: string; quantity: string; unitPrice: string | null; priceUnitId: string | null;
      discountPercent: string | null; priceDisclosure: string }[]
  } | undefined
  if (typeof body?.draft?.version !== 'string' || !Array.isArray(body.lines)) return json({ status: 404 }, 404)
  if (body.lines.some((line) => line.priceDisclosure !== 'Visible')) return json({ status: 403 }, 403)
  const lines = body.lines.map((line) => {
    const priced = line.unitPrice !== null && line.priceUnitId === line.orderUnitId
    const fixture = priced ? fixtureAmount(line.quantity, line.unitPrice as string, line.discountPercent) : null
    return {
      lineId: line.lineId, lineNo: line.lineNo, orderUnitId: line.orderUnitId, quantity: line.quantity,
      state: line.unitPrice === null ? 'Unpriced' : priced ? 'Computed' : 'PriceUnitNotOrderUnit',
      netUnitPrice: fixture?.net ?? null, netAmount: fixture?.amount ?? null, taxState: priced ? 'NoSource' : 'NotComputed',
      vatPercent: null, vatAmount: null, withholdingShare: null, withheldVatAmount: null, grossAmount: null, payableAmount: null,
    }
  })
  const unpriced = lines.filter((line) => line.state === 'Unpriced').length
  const unconvertible = lines.filter((line) => line.state === 'PriceUnitNotOrderUnit').length
  const complete = lines.length > 0 && unpriced === 0 && unconvertible === 0
  const cents = lines.reduce((sum, line) => sum + (line.netAmount === null ? 0n : BigInt(line.netAmount.replace('.', ''))), 0n)
  const net = cents.toString().padStart(3, '0')
  return json({
    draftVersion: body.draft.version,
    revision: body.draft.revision,
    currencyId: body.draft.currencyId,
    amountScale: 2,
    taxState: 'NoSource',
    totals: {
      state: complete ? 'Computed' : 'Incomplete', unpricedLineCount: unpriced, priceUnitNotOrderUnitLineCount: unconvertible,
      amountOutOfRangeLineCount: 0,
      netAmount: complete ? `${net.slice(0, -2)}.${net.slice(-2)}` : null, taxState: complete ? 'NoSource' : 'NotComputed',
      vatAmount: null, withheldVatAmount: null, grossAmount: null, payableAmount: null,
    },
    lines,
  })
}

/** API-04: the fake server's revisable header for a served detail — no sales owner, no due terms (withheld with its prices). */
export function headerOf(detail: unknown): Response {
  const body = detail as {
    draft?: { version?: unknown; revision?: string; partyId?: string; companyAccountId?: string; currencyId?: string }
    deliveryAddress?: unknown
    contact?: unknown
    lines?: { priceDisclosure: string }[]
  } | undefined
  if (typeof body?.draft?.version !== 'string' || !Array.isArray(body.lines)) return json({ status: 404 }, 404)
  const visible = body.lines.every((line) => line.priceDisclosure === 'Visible')
  return json({
    draftVersion: body.draft.version, revision: body.draft.revision, partyId: body.draft.partyId,
    companyAccountId: body.draft.companyAccountId, currencyId: body.draft.currencyId,
    deliveryAddress: body.deliveryAddress ?? null, contact: body.contact ?? null, salesOwnerUserId: null, salesOwnerDisplayName: null,
    dueTermsDisclosure: visible ? 'Visible' : 'Withheld', dueTerms: null, customerOrCurrencyRevisionPending: false,
  })
}

/**
 * MOD-01 / API-06: the fake server's risk answer for a served detail, as main answers it today — open receivables a recorded
 * zero, shipped-not-invoiced without a source, the remainder a recorded zero, this order and the limit not answerable — so the
 * result is fail-closed `Unavailable`. Amounts are withheld exactly when a line's price is.
 */
export function riskCheckOf(detail: unknown): Response {
  const body = detail as {
    draft?: { version?: unknown; partyId?: string; companyAccountId?: string }
    lines?: { priceDisclosure: string }[]
  } | undefined
  if (typeof body?.draft?.version !== 'string' || !Array.isArray(body.lines)) return json({ status: 404 }, 404)
  const visible = body.lines.every((line) => line.priceDisclosure === 'Visible')
  const figure = (kind: string, source: string, state: string, reasonCode: string | null) => ({
    kind, source, state, taxBasis: state === 'Available' ? 'IncludingTax' : null,
    amounts: state === 'Available' && visible ? [] : null, reasonCode,
  })
  return json({
    draftVersion: body.draft.version,
    partyId: body.draft.partyId,
    companyAccountId: body.draft.companyAccountId,
    result: 'Unavailable',
    reasons: [
      { component: 'ShippedNotInvoiced', code: 'invoicing_owner_missing' },
      { component: 'CreditLimit', code: 'finance_credit_limit_owner_missing' },
    ],
    amountDisclosure: visible ? 'Visible' : 'Withheld',
    inLocalCurrency: null,
    inLimitCurrency: null,
    components: [
      figure('OpenReceivables', 'finance.open-items', 'Available', null),
      figure('ShippedNotInvoiced', 'sales.invoicing', 'NoSource', 'invoicing_owner_missing'),
      figure('ApprovedUnshippedRemainder', 'sales.approved-order-remainders', 'Available', null),
    ],
    thisOrder: figure('ThisOrder', 'sales.this-order', 'Available', null),
    creditLimit: figure('CreditLimit', 'finance.party-credit-limit', 'NoSource', 'finance_credit_limit_owner_missing'),
    collateral: figure('Collateral', 'finance.party-collateral', 'NoSource', 'finance_collateral_owner_missing'),
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
