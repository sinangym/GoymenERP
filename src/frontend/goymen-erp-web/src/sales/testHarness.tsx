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
import type { ContactInformationOptions, CurrencyOption } from './contract'
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
}

export interface HarnessConfig {
  readonly capabilities?: readonly string[]
  readonly onList?: (url: string, companyId: string | null) => Response | Promise<Response>
  readonly onDetail?: (url: string, companyId: string | null) => Response | Promise<Response>
  readonly onCreate?: (attempt: number, call: RecordedCall) => Response | Promise<Response>
  /** SALES-A1: a POST on `/sales/drafts/{id}/(submission|approval|rejection)`. */
  readonly onCommand?: (command: string, call: RecordedCall) => Response | Promise<Response>
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
          capabilities: config.capabilities ?? ALL_SALES,
        })
      }
      if (url.includes('/sales/drafts')) {
        const command = /\/sales\/drafts\/[^/?]+\/(submission|approval|rejection|withdrawal)$/.exec(url)?.[1]
        if (call.method === 'POST' && command !== undefined) {
          if (config.onCommand === undefined) throw new Error(`unexpected ${command}`)
          return config.onCommand(command, call)
        }
        if (call.method === 'POST') {
          creates += 1
          if (config.onCreate === undefined) throw new Error('unexpected create')
          return config.onCreate(creates, call)
        }
        if (/\/sales\/drafts\/[^?]+/.test(url)) {
          if (config.onDetail === undefined) throw new Error('unexpected detail read')
          return config.onDetail(url, call.companyId)
        }
        if (config.onList === undefined) throw new Error('unexpected list read')
        return config.onList(url, call.companyId)
      }
      throw new Error(`unexpected fetch: ${url}`)
    }),
  )
  return { calls, salesCalls: () => calls.filter((c) => c.url.includes('/sales/')) }
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
