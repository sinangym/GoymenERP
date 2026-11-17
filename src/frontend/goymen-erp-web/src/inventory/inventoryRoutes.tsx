import type { ReactNode } from 'react'
import type { RouteObject } from 'react-router'
import { CapabilityRoute } from '../shell/CapabilityRoute'
import type { CapabilityId } from '../shell/capabilities'
import {
  INVENTORY_LABEL_TRACE_ROUTE,
  INVENTORY_OPENING_CAPABILITY,
  INVENTORY_MOVEMENT_TIMELINE_ROUTE,
  INVENTORY_OPENING_ROUTE,
  INVENTORY_SHORTAGE_WARNINGS_ROUTE,
  INVENTORY_NEGATIVE_STOCK_ATTEMPTS_ROUTE,
  INVENTORY_STOCK_POSITIONS_CAPABILITY,
  INVENTORY_STOCK_POSITIONS_ROUTE,
  INVENTORY_TRANSFERS_ROUTE,
  INVENTORY_FUTURE_ALLOCATION_ROUTE,
  INVENTORY_FUTURE_ALLOCATION_READ_CAPABILITY,
} from './capabilities'
import { InventoryLabelTracePage } from './InventoryLabelTracePage'
import { InventoryMovementTimelinePage } from './InventoryMovementTimelinePage'
import { InventoryOpeningCreatePage } from './InventoryOpeningCreatePage'
import { InventoryOpeningDetailPage } from './InventoryOpeningDetailPage'
import { InventoryOpeningHomePage } from './InventoryOpeningHomePage'
import { InventoryShortageWarningPage } from './InventoryShortageWarningPage'
import { NegativeStockAttemptPage } from './NegativeStockAttemptPage'
import { InventoryLabelDetailPage } from './InventoryLabelDetailPage'
import { InventoryStockPositionPage } from './InventoryStockPositionPage'
import { InventoryTransferPage } from './InventoryTransferPage'
import { InventoryFutureAllocationPage } from './InventoryFutureAllocationPage'
import { INVENTORY_LABEL_DETAIL_ROUTE } from './labelDetailRoute'

function gate(children: ReactNode) {
  return <CapabilityRoute capability={INVENTORY_OPENING_CAPABILITY as CapabilityId}>{children}</CapabilityRoute>
}

export const inventoryRoutes: RouteObject[] = [
  { path: INVENTORY_NEGATIVE_STOCK_ATTEMPTS_ROUTE.slice(1), element: <CapabilityRoute capability={INVENTORY_STOCK_POSITIONS_CAPABILITY as CapabilityId}>
    <NegativeStockAttemptPage /></CapabilityRoute> },
  { path: INVENTORY_FUTURE_ALLOCATION_ROUTE.slice(1), element: <CapabilityRoute capability={INVENTORY_FUTURE_ALLOCATION_READ_CAPABILITY as CapabilityId}>
    <InventoryFutureAllocationPage /></CapabilityRoute> },
  { path: INVENTORY_TRANSFERS_ROUTE.slice(1), element: <CapabilityRoute capability={INVENTORY_STOCK_POSITIONS_CAPABILITY as CapabilityId}>
    <InventoryTransferPage /></CapabilityRoute> },
  {
    path: INVENTORY_STOCK_POSITIONS_ROUTE.slice(1),
    element: (
      <CapabilityRoute capability={INVENTORY_STOCK_POSITIONS_CAPABILITY as CapabilityId}>
        <InventoryStockPositionPage />
      </CapabilityRoute>
    ),
  },
  {
    path: INVENTORY_LABEL_DETAIL_ROUTE.slice(1),
    element: (
      <CapabilityRoute capability={INVENTORY_STOCK_POSITIONS_CAPABILITY as CapabilityId}>
        <InventoryLabelDetailPage />
      </CapabilityRoute>
    ),
  },
  {
    path: INVENTORY_MOVEMENT_TIMELINE_ROUTE.slice(1),
    element: (
      <CapabilityRoute capability={INVENTORY_STOCK_POSITIONS_CAPABILITY as CapabilityId}>
        <InventoryMovementTimelinePage />
      </CapabilityRoute>
    ),
  },
  {
    path: INVENTORY_SHORTAGE_WARNINGS_ROUTE.slice(1),
    element: (
      <CapabilityRoute capability={INVENTORY_STOCK_POSITIONS_CAPABILITY as CapabilityId}>
        <InventoryShortageWarningPage />
      </CapabilityRoute>
    ),
  },
  {
    path: INVENTORY_LABEL_TRACE_ROUTE.slice(1),
    element: (
      <CapabilityRoute capability={INVENTORY_STOCK_POSITIONS_CAPABILITY as CapabilityId}>
        <InventoryLabelTracePage />
      </CapabilityRoute>
    ),
  },
  { path: INVENTORY_OPENING_ROUTE.create.slice(1), element: gate(<InventoryOpeningCreatePage />) },
  { path: INVENTORY_OPENING_ROUTE.home.slice(1), element: gate(<InventoryOpeningHomePage />) },
  { path: INVENTORY_OPENING_ROUTE.detail.slice(1), element: gate(<InventoryOpeningDetailPage />) },
]
