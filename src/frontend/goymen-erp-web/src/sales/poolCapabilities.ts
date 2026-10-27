/**
 * S-01 "Satış Talebi / Teklif Havuzu" — the routes of the pool and the capabilities that open them.
 *
 * NEEDS-CAPABILITY. The backend shell provider (`SalesShellCapabilityProvider`, contract 1.6 on #828) publishes NO
 * capability for the sales request or the sales quote screens, and this lane does not invent one: every list below is
 * EMPTY, so `CapabilityRoute` (which opens a list only when one of its ids is granted, and an empty list never) closes
 * every pool route onto the neutral not-found block in the running app, and every pool control stays hidden. The server
 * still re-authorizes every request (`sales.requests.*`, `sales.quotes.*`, granted to no role today).
 *
 * When the central owner publishes the ids, they are written HERE (one place), the backend contract test pins them, and
 * the screens open for an actor the shell grants them to — nothing else changes. Tests provide their own map through
 * `SalesPoolCapabilityContext`; the production default is this empty one.
 */

import { createContext, useContext } from 'react'
import { useShellState } from '../shell/shellContext'
import { hasCapability } from '../shell/shellState'

export type SalesPoolCapabilityKey =
  /** S-01 requests list (`sales.requests.read` on the server). */
  | 'requestsList'
  /** S-01 request detail (`sales.requests.read`). */
  | 'requestsDetail'
  /** S-01 new request (`sales.requests.write`). */
  | 'requestsCreate'
  /** Control on the request detail: approve / reject (`sales.requests.approve`). */
  | 'requestsDecide'
  /** S-01 quotes list (`sales.quotes.read`). */
  | 'quotesList'
  /** S-01 quote detail (`sales.quotes.read`). */
  | 'quotesDetail'
  /** S-01 new quote (`sales.quotes.write`). */
  | 'quotesCreate'
  /** Control on the quote detail: revision and loss (`sales.quotes.write`). */
  | 'quotesWrite'
  /** Control on the quote detail: "Siparişe dönüştür" (`sales.quotes.write` + `sales.drafts.create` on the server). */
  | 'quotesConvert'

export type SalesPoolCapabilities = Readonly<Record<SalesPoolCapabilityKey, readonly string[]>>

/** Production: nothing is published, so nothing opens. */
export const SALES_POOL_CAPABILITY: SalesPoolCapabilities = {
  requestsList: [],
  requestsDetail: [],
  requestsCreate: [],
  requestsDecide: [],
  quotesList: [],
  quotesDetail: [],
  quotesCreate: [],
  quotesWrite: [],
  quotesConvert: [],
}

export const SalesPoolCapabilityContext = createContext<SalesPoolCapabilities>(SALES_POOL_CAPABILITY)

/** Whether the shell grants any id of the key; an empty list (unpublished) is never granted. */
export function useSalesPoolCapability(key: SalesPoolCapabilityKey): boolean {
  const shell = useShellState()
  const ids = useContext(SalesPoolCapabilityContext)[key]
  return ids.some((id) => hasCapability(shell, id))
}

export const SALES_POOL_ROUTE = {
  requests: '/satis/talepler',
  // Static `yeni` before the parameter route: it must never be read as a record id.
  requestCreate: '/satis/talepler/yeni',
  requestDetail: '/satis/talepler/:requestId',
  quotes: '/satis/teklifler',
  quoteCreate: '/satis/teklifler/yeni',
  quoteDetail: '/satis/teklifler/:quoteId',
} as const

export const salesRequestScreenPath = (requestId: string) => `${SALES_POOL_ROUTE.requests}/${encodeURIComponent(requestId)}`
export const salesQuoteScreenPath = (quoteId: string) => `${SALES_POOL_ROUTE.quotes}/${encodeURIComponent(quoteId)}`

/** The query member that carries the requests a new quote covers (`?talep=<id>&talep=<id>`). */
export const QUOTE_FROM_REQUEST_PARAM = 'talep'

export function quoteCreatePathFor(requestIds: readonly string[]): string {
  const params = new URLSearchParams()
  for (const id of requestIds) params.append(QUOTE_FROM_REQUEST_PARAM, id)
  const query = params.toString()
  return query === '' ? SALES_POOL_ROUTE.quoteCreate : `${SALES_POOL_ROUTE.quoteCreate}?${query}`
}
