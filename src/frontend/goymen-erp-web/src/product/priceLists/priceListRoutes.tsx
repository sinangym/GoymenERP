/**
 * U-05 routes, spread into the Product route table (./../productRoutes.tsx) so the shell mounts them with the other
 * Product screens. Each route is gated on its OWN capability through the shared `CapabilityRoute` (fail closed until
 * the server publishes the id). `/fiyat-listeleri/yeni` is declared before `/fiyat-listeleri/:priceListId`.
 */

import type { ReactNode } from 'react'
import type { RouteObject } from 'react-router'
import { CapabilityRoute } from '../../shell/CapabilityRoute'
import type { CapabilityId } from '../../shell/capabilities'
import { PRICE_LIST_CAPABILITY, PRICE_LIST_ROUTE, type PriceListCapabilityId } from './capabilities'
import { PriceListCreatePage } from './PriceListCreatePage'
import { PriceListDetailPage } from './PriceListDetailPage'
import { PriceListListPage } from './PriceListListPage'

/** The shell's `CapabilityId` union does not list these ids yet (the central step); the cast widens a type only. */
function gate(capability: PriceListCapabilityId, children: ReactNode) {
  return <CapabilityRoute capability={capability as CapabilityId}>{children}</CapabilityRoute>
}

export const priceListRoutes: RouteObject[] = [
  { path: PRICE_LIST_ROUTE.list.slice(1), element: gate(PRICE_LIST_CAPABILITY.list, <PriceListListPage />) },
  { path: PRICE_LIST_ROUTE.create.slice(1), element: gate(PRICE_LIST_CAPABILITY.create, <PriceListCreatePage />) },
  { path: PRICE_LIST_ROUTE.detail.slice(1), element: gate(PRICE_LIST_CAPABILITY.detail, <PriceListDetailPage />) },
]
