/**
 * TEST SUPPORT ONLY — the S-01 pool fixtures (wire bodies shaped exactly like #824/#828's OpenAPI schemas) and the stubbed
 * fetch; ./poolTestHarness.tsx renders the screens in the real shell.
 *
 * The capability ids below are TEST ids (`test.pool.*`), kept apart from the published ones (`SALES_POOL_CAPABILITY_ID`)
 * so each screen test grants exactly the controls it exercises through `SalesPoolCapabilityContext`. Nothing here is a
 * production id; the production map is pinned in ./SalesPoolPages.test.tsx.
 */

import { vi } from 'vitest'
import { SHELL_CAPABILITY_CONTRACT_VERSION } from '../shell/api'
import { SALES_CAPABILITY } from './capabilities'
import type { SalesPoolCapabilities } from './poolCapabilities'
import {
  ACCOUNT,
  COMPANY_A,
  COMPANY_B,
  CURRENCY,
  HARNESS_USER_ID,
  KG,
  PARTY,
  PRODUCT,
  json,
} from './testHarness'

export { ACCOUNT, COMPANY_A, COMPANY_B, CURRENCY, KG, PARTY, PRODUCT, json }

export const TEST_POOL: SalesPoolCapabilities = {
  requestsList: ['test.pool.requests.list'],
  requestsDetail: ['test.pool.requests.detail'],
  requestsCreate: ['test.pool.requests.create'],
  requestsDecide: ['test.pool.requests.decide'],
  quotesList: ['test.pool.quotes.list'],
  quotesDetail: ['test.pool.quotes.detail'],
  quotesCreate: ['test.pool.quotes.create'],
  quotesWrite: ['test.pool.quotes.write'],
  quotesConvert: ['test.pool.quotes.convert'],
  quotesApproveExpired: ['test.pool.quotes.approve-expired'],
}

/** Every test pool id plus the order detail (the conversion opens it). */
export const ALL_POOL: readonly string[] = [...Object.values(TEST_POOL).flat(), SALES_CAPABILITY.draftsDetail]

export const REQUEST_ID = 'a1a1a1a1-0000-4000-8000-000000000001'
export const REQUEST_2 = 'a1a1a1a1-0000-4000-8000-000000000002'
export const QUOTE_ID = 'b2b2b2b2-0000-4000-8000-000000000001'
export const ORDER_ID = 'c3c3c3c3-0000-4000-8000-000000000001'
export const REQUEST_LINE = 'd4d4d4d4-0000-4000-8000-000000000001'
export const QUOTE_LINE = 'e5e5e5e5-0000-4000-8000-000000000001'
export const OTHER_PARTY = '11111111-1111-1111-1111-111111111112'

type Json = Record<string, unknown>

export function requestSummary(overrides: Json = {}): Json {
  return {
    id: REQUEST_ID,
    companyId: COMPANY_A,
    documentNo: 'SR-2026-000001',
    state: 'Submitted',
    version: '1',
    partyId: PARTY,
    companyAccountId: ACCOUNT,
    businessDate: '2026-10-20',
    createdAtUtc: '2026-10-20T08:00:00Z',
    lineCount: 1,
    ...overrides,
  }
}

export function requestDetail(summary: Json = {}, overrides: Json = {}): Json {
  const request = requestSummary(summary)
  const state = request.state
  return {
    request,
    requestedDeliveryDate: null,
    note: null,
    lines: [
      {
        lineId: REQUEST_LINE,
        lineNo: 1,
        productId: PRODUCT,
        productVersion: '7',
        productCode: 'PRF-40X40',
        productName: '40x40 profil',
        orderUnitId: KG,
        orderUnitCode: 'kg',
        quantity: '1500',
        requestedDeliveryDate: '2026-11-01',
        note: null,
      },
    ],
    decision:
      state === 'Submitted'
        ? null
        : { decidedBy: 'onaylayici', decidedAtUtc: '2026-10-21T09:00:00Z', rejectionReason: state === 'Rejected' ? 'Stok yok' : null },
    ...overrides,
  }
}

export const requestMutation = (detail: Json, isReplay = false): Json => ({ detail, isReplay, originalCorrelationId: 'corr-1' })

export function quoteSummary(overrides: Json = {}): Json {
  return {
    id: QUOTE_ID,
    companyId: COMPANY_A,
    documentNo: 'SQ-2026-000001',
    state: 'Open',
    version: '1',
    revision: 'Rev.0',
    partyId: PARTY,
    companyAccountId: ACCOUNT,
    currencyId: CURRENCY,
    businessDate: '2026-10-22',
    validUntil: '2026-11-22',
    createdAtUtc: '2026-10-22T08:00:00Z',
    lineCount: 1,
    convertedDraftId: null,
    ...overrides,
  }
}

export function quoteLine(overrides: Json = {}): Json {
  return {
    lineId: QUOTE_LINE,
    lineNo: 1,
    productId: PRODUCT,
    productVersion: '7',
    productCode: 'PRF-40X40',
    productName: '40x40 profil',
    orderUnitId: KG,
    orderUnitCode: 'kg',
    quantity: '1500',
    unitPrice: '42.5',
    priceUnitId: KG,
    discountPercent: null,
    priceDisclosure: 'Visible',
    requestedDeliveryDate: null,
    note: null,
    ...overrides,
  }
}

export const LOSS_REASON_PRICE = 'f6f6f6f6-0000-4000-8000-000000000001'
export const LOSS_REASON_OTHER = 'f6f6f6f6-0000-4000-8000-000000000002'

/** SAT-D25: a SENTETIK Settings loss reason list (the real content is a Settings record). */
export function lossReasons(overrides: Json = {}): Json {
  return {
    businessDay: '2026-10-23',
    isDefined: true,
    profileId: 'f6f6f6f6-0000-4000-8000-0000000000ff',
    profileVersion: '1',
    reasons: [
      { reasonId: LOSS_REASON_PRICE, code: 'FIYAT', label: 'Fiyat', requiresExplanation: false },
      { reasonId: LOSS_REASON_OTHER, code: 'DIGER', label: 'Diğer', requiresExplanation: true },
    ],
    ...overrides,
  }
}

/** The server's SAT-D24 answer for the quote on `businessDay` (expired = validity date before it). */
export function expiryOf(quote: Json, businessDay = '2026-10-23', isApproved = false): Json {
  const validUntil = quote.validUntil as string | null
  return { businessDay, isExpired: validUntil !== null && validUntil < businessDay, isApproved }
}

export function quoteDetail(summary: Json = {}, overrides: Json = {}): Json {
  const quote = quoteSummary(summary)
  const revisionCount = Number(String(quote.revision).replace('Rev.', '')) + 1
  return {
    quote,
    requestIds: [REQUEST_ID],
    requestedDeliveryDate: null,
    note: null,
    lines: [quoteLine()],
    loss:
      quote.state === 'Lost'
        ? { reason: null, lostBy: 'satis', lostAtUtc: '2026-10-23T08:00:00Z', reasonId: LOSS_REASON_PRICE, reasonCode: 'FIYAT', reasonLabel: 'Fiyat' }
        : null,
    conversion:
      quote.state === 'Converted'
        ? { draftId: quote.convertedDraftId, convertedBy: 'satis', convertedAtUtc: '2026-10-23T08:00:00Z' }
        : null,
    createdBy: 'satis',
    revisions: Array.from({ length: revisionCount }, (_, index) => ({
      revision: `Rev.${index}`,
      validUntil: quote.validUntil,
      revisedBy: index === 0 ? 'satis' : 'revize-eden',
      revisedAtUtc: '2026-10-22T08:00:00Z',
    })),
    expiryApprovals: [],
    expiry: expiryOf(quote),
    ...overrides,
  }
}

export const quoteMutation = (detail: Json, isReplay = false): Json => ({ detail, isReplay, originalCorrelationId: 'corr-2' })

/** The ORDER the conversion answers (`SalesOrderDraftResponse`): the quote's line verbatim under its own line id. */
export function orderMutation(businessDate: string, isReplay = false, lineOverrides: Json = {}): Json {
  return {
    detail: {
      draft: {
        id: ORDER_ID,
        companyId: COMPANY_A,
        documentNo: 'SO-2026-000042',
        kind: 'Order',
        state: 'Draft',
        version: '1',
        revision: 'Rev.0',
        partyId: PARTY,
        companyAccountId: ACCOUNT,
        currencyId: CURRENCY,
        businessDate,
        createdAtUtc: '2026-10-23T08:00:00Z',
        lineCount: 1,
        approvalState: 'Preparing',
      },
      requestedDeliveryDate: null,
      note: null,
      lines: [
        {
          lineId: QUOTE_LINE,
          lineNo: 1,
          productId: PRODUCT,
          productVersion: '7',
          productCode: 'PRF-40X40',
          productName: '40x40 profil',
          orderUnitId: KG,
          quantity: '1500',
          unitPrice: '42.5',
          priceUnitId: KG,
          requestedDeliveryDate: null,
          note: null,
          requestedDeliverySlices: null,
          discountPercent: null,
          priceDisclosure: 'Visible',
          ...lineOverrides,
        },
      ],
      deliveryAddress: null,
      contact: null,
      responsible: null,
      paymentTerms: null,
      sourceStrategy: null,
      package: null,
      approval: { submittedBy: null, submittedAtUtc: null, decidedBy: null, decidedAtUtc: null, rejectionReason: null },
      customerAcceptance: { required: false, acceptedRevision: null, acceptedBy: null, acceptedAtUtc: null, isCurrentRevisionAccepted: true },
      isOwnDraft: true,
      isOwnDecisionRestricted: false,
    },
    isReplay,
    originalCorrelationId: 'corr-3',
  }
}

export interface PoolCall {
  readonly url: string
  readonly method: string
  readonly companyId: string | null
  readonly idempotencyKey: string | null
  readonly body: unknown
}

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

/** Stubs fetch: the shell's own reads, then every `/sales/` call goes to `onSales` (an unhandled one fails the test). */
export function installPoolFetch(onSales: (call: PoolCall) => Response | Promise<Response>, capabilities: readonly string[] = ALL_POOL) {
  const calls: PoolCall[] = []
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString()
      const headers = (init?.headers ?? {}) as Record<string, string>
      if (url.includes('/identity/me')) return json(IDENTITY)
      if (url.includes('/organization/companies')) return json(DIRECTORY)
      if (url.includes('/shell/capabilities')) {
        return json({ contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION, generation: 'gen-1', capabilities })
      }
      const call: PoolCall = {
        url,
        method: (init?.method ?? 'GET').toUpperCase(),
        companyId: headers['X-Company-Id'] ?? null,
        idempotencyKey: headers['Idempotency-Key'] ?? null,
        body: typeof init?.body === 'string' ? JSON.parse(init.body) : null,
      }
      calls.push(call)
      if (url.includes('/sales/')) return onSales(call)
      throw new Error(`unexpected fetch: ${url}`)
    }),
  )
  return { calls, posts: () => calls.filter((call) => call.method === 'POST') }
}
