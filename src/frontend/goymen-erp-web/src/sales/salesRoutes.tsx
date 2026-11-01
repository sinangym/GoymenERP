/**
 * The Sales route table, owned by the Sales lane, for the shell router's single
 * writer to spread in — never written into `src/shell/**` from here.
 *
 * Each route reuses the shared `CapabilityRoute` entry gate. The backend shell provider
 * (`SalesShellCapabilityProvider`) publishes the three route ids; an actor without the
 * grant gets the neutral not-found block and no request is sent.
 */

import type { ReactNode } from 'react'
import type { RouteObject } from 'react-router'
import { CapabilityRoute } from '../shell/CapabilityRoute'
import type { CapabilityId } from '../shell/capabilities'
import { SALES_CAPABILITY, SALES_ROUTE } from './capabilities'
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
import { SalesDispatchListPage } from './SalesDispatchListPage'
import { SalesInvoiceCandidatesPage } from './SalesInvoiceCandidatesPage'
import { SalesReportsPage } from './SalesReportsPage'

/** The shell's `CapabilityId` union carries the three Sales route ids, so no cast is needed. */
function gate(capability: CapabilityId, children: ReactNode) {
  return <CapabilityRoute capability={capability}>{children}</CapabilityRoute>
}

export const salesRoutes: RouteObject[] = [
  { path: SALES_ROUTE.list.slice(1), element: gate(SALES_CAPABILITY.draftsList, <SalesDraftListPage />) },
  // Static `yeni` before `:draftId`, so it is never read as a record id.
  // The create form reads its options from the real Sales reference routes (S03): the context's
  // default source is the real one; a test or an isolated screen provides its own.
  { path: SALES_ROUTE.create.slice(1), element: gate(SALES_CAPABILITY.draftsCreate, <SalesDraftCreatePage />) },
  { path: SALES_ROUTE.detail.slice(1), element: gate(SALES_CAPABILITY.draftsDetail, <SalesDraftDetailPage />) },
  // S-01 Talep/Teklif Havuzu (UI-20). Each gate holds the id the backend publishes for its screen (Sales shell 1.8,
  // ./poolCapabilities.ts); an actor the shell does not grant it meets the neutral not-found block.
  // Static `yeni` before the parameter routes.
  { path: SALES_POOL_ROUTE.requests.slice(1), element: <SalesPoolRoute capability="requestsList"><SalesRequestListPage /></SalesPoolRoute> },
  { path: SALES_POOL_ROUTE.requestCreate.slice(1), element: <SalesPoolRoute capability="requestsCreate"><SalesRequestCreatePage /></SalesPoolRoute> },
  { path: SALES_POOL_ROUTE.requestDetail.slice(1), element: <SalesPoolRoute capability="requestsDetail"><SalesRequestDetailPage /></SalesPoolRoute> },
  { path: SALES_POOL_ROUTE.quotes.slice(1), element: <SalesPoolRoute capability="quotesList"><SalesQuoteListPage /></SalesPoolRoute> },
  { path: SALES_POOL_ROUTE.quoteCreate.slice(1), element: <SalesPoolRoute capability="quotesCreate"><SalesQuoteCreatePage /></SalesPoolRoute> },
  { path: SALES_POOL_ROUTE.quoteDetail.slice(1), element: <SalesPoolRoute capability="quotesDetail"><SalesQuoteDetailPage /></SalesPoolRoute> },
  // UI-60 / UI-61 (Sales shell 1.9): S-07 Sevkiyat / İrsaliyeler and S-08 Faturaya Hazır, each on its own published id.
  { path: SALES_ROUTE.dispatches.slice(1), element: gate(SALES_CAPABILITY.dispatchesList, <SalesDispatchListPage />) },
  { path: SALES_ROUTE.invoiceCandidates.slice(1), element: gate(SALES_CAPABILITY.invoiceCandidatesList, <SalesInvoiceCandidatesPage />) },
  // UI-80 (Sales shell 1.11): the Sales reports screen on its own published id.
  { path: SALES_ROUTE.reports.slice(1), element: gate(SALES_CAPABILITY.reportsView, <SalesReportsPage />) },
]
