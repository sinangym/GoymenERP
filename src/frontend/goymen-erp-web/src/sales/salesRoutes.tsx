/**
 * The Sales route table, owned by the Sales lane, for the shell router's single
 * writer to spread in — never written into `src/shell/**` from here.
 *
 * Each route reuses the shared `CapabilityRoute` entry gate. The ids are PROPOSED and
 * unpublished (see ./capabilities.ts), so in the running app every route closes onto
 * the neutral not-found block and calls nothing.
 */

import type { ReactNode } from 'react'
import type { RouteObject } from 'react-router'
import { CapabilityRoute } from '../shell/CapabilityRoute'
import type { CapabilityId } from '../shell/capabilities'
import { SALES_CAPABILITY, SALES_ROUTE, type SalesCapabilityId } from './capabilities'
import { SalesDraftListPage } from './SalesDraftListPage'
import { SalesDraftCreatePage } from './SalesDraftCreatePage'
import { SalesDraftDetailPage } from './SalesDraftDetailPage'
import { SalesPoolRoute } from './SalesPoolRoute'
import { SALES_POOL_ROUTE } from './poolCapabilities'
import { SalesRequestListPage } from './SalesRequestListPage'
import { SalesRequestCreatePage } from './SalesRequestCreatePage'
import { SalesRequestDetailPage } from './SalesRequestDetailPage'
import { SalesQuoteListPage } from './SalesQuoteListPage'
import { SalesQuoteCreatePage } from './SalesQuoteCreatePage'
import { SalesQuoteDetailPage } from './SalesQuoteDetailPage'

/**
 * `CapabilityRoute` types its prop as the shell's `CapabilityId` union, which cannot
 * contain the Sales ids until the shell owner adds them. The cast widens a compile-time
 * union only: `hasCapability` compares plain strings against the server's list, so an
 * unpublished id still fails closed.
 */
function gate(capability: SalesCapabilityId, children: ReactNode) {
  return <CapabilityRoute capability={capability as unknown as CapabilityId}>{children}</CapabilityRoute>
}

export const salesRoutes: RouteObject[] = [
  { path: SALES_ROUTE.list.slice(1), element: gate(SALES_CAPABILITY.draftsList, <SalesDraftListPage />) },
  // Static `yeni` before `:draftId`, so it is never read as a record id.
  // The create form reads its options from the real Sales reference routes (S03): the context's
  // default source is the real one; a test or an isolated screen provides its own.
  { path: SALES_ROUTE.create.slice(1), element: gate(SALES_CAPABILITY.draftsCreate, <SalesDraftCreatePage />) },
  { path: SALES_ROUTE.detail.slice(1), element: gate(SALES_CAPABILITY.draftsDetail, <SalesDraftDetailPage />) },
  // S-01 Talep/Teklif Havuzu (UI-20). NEEDS-CAPABILITY: the backend publishes no id for these screens yet, so each
  // gate holds an EMPTY list (./poolCapabilities.ts) and closes onto the neutral not-found block — no id is invented.
  // Static `yeni` before the parameter routes.
  { path: SALES_POOL_ROUTE.requests.slice(1), element: <SalesPoolRoute capability="requestsList"><SalesRequestListPage /></SalesPoolRoute> },
  { path: SALES_POOL_ROUTE.requestCreate.slice(1), element: <SalesPoolRoute capability="requestsCreate"><SalesRequestCreatePage /></SalesPoolRoute> },
  { path: SALES_POOL_ROUTE.requestDetail.slice(1), element: <SalesPoolRoute capability="requestsDetail"><SalesRequestDetailPage /></SalesPoolRoute> },
  { path: SALES_POOL_ROUTE.quotes.slice(1), element: <SalesPoolRoute capability="quotesList"><SalesQuoteListPage /></SalesPoolRoute> },
  { path: SALES_POOL_ROUTE.quoteCreate.slice(1), element: <SalesPoolRoute capability="quotesCreate"><SalesQuoteCreatePage /></SalesPoolRoute> },
  { path: SALES_POOL_ROUTE.quoteDetail.slice(1), element: <SalesPoolRoute capability="quotesDetail"><SalesQuoteDetailPage /></SalesPoolRoute> },
]
