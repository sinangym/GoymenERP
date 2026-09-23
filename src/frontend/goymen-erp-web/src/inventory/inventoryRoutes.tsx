import type { ReactNode } from 'react'
import type { RouteObject } from 'react-router'
import { CapabilityRoute } from '../shell/CapabilityRoute'
import type { CapabilityId } from '../shell/capabilities'
import { INVENTORY_OPENING_CAPABILITY, INVENTORY_OPENING_ROUTE } from './capabilities'
import { InventoryOpeningCreatePage } from './InventoryOpeningCreatePage'
import { InventoryOpeningDetailPage } from './InventoryOpeningDetailPage'
import { InventoryOpeningHomePage } from './InventoryOpeningHomePage'

function gate(children: ReactNode) {
  return <CapabilityRoute capability={INVENTORY_OPENING_CAPABILITY as CapabilityId}>{children}</CapabilityRoute>
}

export const inventoryRoutes: RouteObject[] = [
  { path: INVENTORY_OPENING_ROUTE.create.slice(1), element: gate(<InventoryOpeningCreatePage />) },
  { path: INVENTORY_OPENING_ROUTE.home.slice(1), element: gate(<InventoryOpeningHomePage />) },
  { path: INVENTORY_OPENING_ROUTE.detail.slice(1), element: gate(<InventoryOpeningDetailPage />) },
]
