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
]
