/**
 * S-01 "Satış Talebi / Teklif Havuzu" — the routes of the pool and the capabilities that open them.
 *
 * PUBLISHED. The backend shell provider (`SalesShellCapabilityProvider`, Sales shell contract 1.8) publishes one id per
 * pool screen and control; they are declared once, in `./capabilities.ts` (`SALES_POOL_CAPABILITY_ID`, pinned there by the
 * backend contract test), and mapped here onto the pool's keys. `CapabilityRoute` opens a route only when the shell grants
 * its id, so an actor without the request / quote names still meets the neutral not-found block, and the server
 * re-authorizes every request (`sales.requests.*`, `sales.quotes.*`, granted to no role today — a human gate). Tests
 * provide their own map through `SalesPoolCapabilityContext`; the production default is the published one.
 */

import { createContext, useContext } from 'react'
import { useShellState } from '../shell/shellContext'
import { hasCapability } from '../shell/shellState'
import { SALES_POOL_CAPABILITY_ID, SALES_POOL_ROUTE } from './capabilities'

export { SALES_POOL_ROUTE }

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

/** Production: each key opens on exactly the one id the backend publishes for it (Sales shell contract 1.8). */
export const SALES_POOL_CAPABILITY: SalesPoolCapabilities = {
  requestsList: [SALES_POOL_CAPABILITY_ID.requestsList],
  requestsDetail: [SALES_POOL_CAPABILITY_ID.requestsDetail],
  requestsCreate: [SALES_POOL_CAPABILITY_ID.requestsCreate],
  requestsDecide: [SALES_POOL_CAPABILITY_ID.requestsDecide],
  quotesList: [SALES_POOL_CAPABILITY_ID.quotesList],
  quotesDetail: [SALES_POOL_CAPABILITY_ID.quotesDetail],
  quotesCreate: [SALES_POOL_CAPABILITY_ID.quotesCreate],
  quotesWrite: [SALES_POOL_CAPABILITY_ID.quotesWrite],
  quotesConvert: [SALES_POOL_CAPABILITY_ID.quotesConvert],
}

export const SalesPoolCapabilityContext = createContext<SalesPoolCapabilities>(SALES_POOL_CAPABILITY)

/** Whether the shell grants any id of the key; an empty list (unpublished) is never granted. */
export function useSalesPoolCapability(key: SalesPoolCapabilityKey): boolean {
  const shell = useShellState()
  const ids = useContext(SalesPoolCapabilityContext)[key]
  return ids.some((id) => hasCapability(shell, id))
}

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
