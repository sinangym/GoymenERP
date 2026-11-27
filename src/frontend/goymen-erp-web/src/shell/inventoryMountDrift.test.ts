import { describe, expect, it } from 'vitest'
import { matchPath, matchRoutes } from 'react-router'
import {
  INVENTORY_LABEL_TRACE_ROUTE,
  INVENTORY_MOVEMENT_TIMELINE_ROUTE,
  INVENTORY_OPENING_CAPABILITY,
  INVENTORY_OPENING_ROUTE,
  INVENTORY_SHORTAGE_WARNINGS_ROUTE,
  INVENTORY_STOCK_POSITIONS_CAPABILITY,
  INVENTORY_STOCK_POSITIONS_ROUTE,
  INVENTORY_FUTURE_ALLOCATION_ROUTE,
  INVENTORY_TRANSFER_PLANNING_ROUTE,
  INVENTORY_TRANSFER_PLANNING_CAPABILITY,
  INVENTORY_TRANSFER_ACTION_ROUTE,
  inventoryFutureAllocationPath,
} from '../inventory/capabilities'
import { workRoutes } from '../work/workRoutes'
import { inventoryRoutes } from '../inventory/inventoryRoutes'
import { INVENTORY_LABEL_DETAIL_ROUTE } from '../inventory/labelDetailRoute'
import { NAV_CAPABILITIES } from './AppShell'
import { CAPABILITY, CAPABILITY_NAV_LABEL, CAPABILITY_ROUTE } from './capabilities'
import { NAV_GROUPS } from './navigation'

describe('shell ↔ Inventory opening-stock contract', () => {
  it('mounts the owner routes under one existing capability and one navigation entry', () => {
    expect(CAPABILITY.inventoryOpeningStockRecord).toBe(INVENTORY_OPENING_CAPABILITY)
    expect(CAPABILITY_ROUTE[CAPABILITY.inventoryOpeningStockRecord]).toBe(INVENTORY_OPENING_ROUTE.home)
    // NAV-OPENING-01: one entry, under Ayarlar, as "Açılış stokları"; the route and capability are unchanged.
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.inventoryOpeningStockRecord]).toBe('Açılış stokları')
    expect(NAV_GROUPS.filter((group) => group.entries.includes(INVENTORY_OPENING_CAPABILITY)).map((group) => group.id))
      .toEqual(['settings'])
    expect(NAV_GROUPS.find((group) => group.id === 'stock-logistics')?.entries).not.toContain(INVENTORY_OPENING_CAPABILITY)
    expect(NAV_CAPABILITIES.filter((item) => item === INVENTORY_OPENING_CAPABILITY)).toHaveLength(1)
    expect(CAPABILITY.inventoryStockPositionsList).toBe(INVENTORY_STOCK_POSITIONS_CAPABILITY)
    expect(CAPABILITY_ROUTE[CAPABILITY.inventoryStockPositionsList]).toBe(INVENTORY_STOCK_POSITIONS_ROUTE)
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.inventoryStockPositionsList]).toBe('Stok / Malzeme Pozisyonu')
    expect(NAV_CAPABILITIES.filter((item) => item === INVENTORY_STOCK_POSITIONS_CAPABILITY)).toHaveLength(1)
    expect(inventoryRoutes).toHaveLength(16)
    const futurePath = inventoryFutureAllocationPath('order-1', 'line-2')
    expect(matchRoutes(inventoryRoutes, futurePath)?.at(-1)?.route.path)
      .toBe(INVENTORY_FUTURE_ALLOCATION_ROUTE.slice(1))
    expect(matchRoutes(inventoryRoutes, futurePath)?.at(-1)?.params)
      .toMatchObject({ salesOrderId: 'order-1', salesOrderLineId: 'line-2' })
    // ST-04 company timeline: an owner route reached from a label detail under the SAME stock-positions
    // capability. It adds no capability, no sidebar entry and no CAPABILITY_ROUTE mapping of its own.
    expect(matchRoutes(inventoryRoutes, INVENTORY_MOVEMENT_TIMELINE_ROUTE)?.at(-1)?.route.path).toBe(
      INVENTORY_MOVEMENT_TIMELINE_ROUTE.slice(1),
    )
    expect(Object.values(CAPABILITY_ROUTE)).not.toContain(INVENTORY_MOVEMENT_TIMELINE_ROUTE)
    // KLN-D42 shortage warning: reached from the stock positions page under the SAME capability; no own entry.
    expect(matchRoutes(inventoryRoutes, INVENTORY_SHORTAGE_WARNINGS_ROUTE)?.at(-1)?.route.path).toBe(
      INVENTORY_SHORTAGE_WARNINGS_ROUTE.slice(1),
    )
    expect(Object.values(CAPABILITY_ROUTE)).not.toContain(INVENTORY_SHORTAGE_WARNINGS_ROUTE)
    // STK-S33 label trace (KLN-D44): reached from a label detail under the SAME capability; no own entry or name.
    expect(matchRoutes(inventoryRoutes, INVENTORY_LABEL_TRACE_ROUTE)?.at(-1)?.route.path).toBe(
      INVENTORY_LABEL_TRACE_ROUTE.slice(1),
    )
    expect(Object.values(CAPABILITY_ROUTE)).not.toContain(INVENTORY_LABEL_TRACE_ROUTE)
    expect(matchRoutes(inventoryRoutes, '/stok/etiket/hareketler')?.at(-1)?.route.path).toBe(
      INVENTORY_LABEL_DETAIL_ROUTE.slice(1),
    )
    expect(matchRoutes(inventoryRoutes, INVENTORY_STOCK_POSITIONS_ROUTE)?.at(-1)?.route.path).toBe(
      INVENTORY_STOCK_POSITIONS_ROUTE.slice(1),
    )
    expect(matchRoutes(inventoryRoutes, '/stok/etiket/label-1')?.at(-1)?.route.path).toBe(
      INVENTORY_LABEL_DETAIL_ROUTE.slice(1),
    )
  })

  it('keeps static create before detail and never reads yeni as a count id', () => {
    const matches = matchRoutes(inventoryRoutes, INVENTORY_OPENING_ROUTE.create)
    expect(matches?.at(-1)?.route.path).toBe(INVENTORY_OPENING_ROUTE.create.slice(1))
    expect(matches?.at(-1)?.params.countId).toBeUndefined()
    expect(matchPath(INVENTORY_OPENING_ROUTE.detail, '/stok/acilis/COUNT-1')?.params.countId).toBe('COUNT-1')
  })
  it('mounts transfer planning with the existing write purpose independently from stock reports', () => {
    expect(CAPABILITY.inventoryTransferPlanning).toBe(INVENTORY_TRANSFER_PLANNING_CAPABILITY)
    expect(CAPABILITY_ROUTE[CAPABILITY.inventoryTransferPlanning]).toBe(INVENTORY_TRANSFER_PLANNING_ROUTE)
    const route = matchRoutes(inventoryRoutes, INVENTORY_TRANSFER_PLANNING_ROUTE)?.at(-1)?.route
    expect(route?.path).toBe(INVENTORY_TRANSFER_PLANNING_ROUTE.slice(1))
    expect(NAV_CAPABILITIES.filter(item => item === INVENTORY_TRANSFER_PLANNING_CAPABILITY)).toHaveLength(1)
    expect(NAV_GROUPS.find(group => group.id === 'stock-logistics')?.entries).toContain(INVENTORY_TRANSFER_PLANNING_CAPABILITY)
  })
  it('mounts every receipt decision purpose under its own advertised action authority', () => {
    for (const [capability, path] of Object.entries(INVENTORY_TRANSFER_ACTION_ROUTE)) {
      expect(CAPABILITY_ROUTE[capability as keyof typeof CAPABILITY_ROUTE]).toBe(path)
      const route = matchRoutes(inventoryRoutes, path)?.at(-1)?.route
      expect(route?.path).toBe(path.slice(1))
      expect((route?.element as { props?: { capability?: string } }).props?.capability).toBe(capability)
      expect(NAV_CAPABILITIES.filter(item => item === capability)).toHaveLength(1)
      expect(NAV_GROUPS.find(group => group.id === 'stock-logistics')?.entries).toContain(capability)
    }
  })
})


it('mounts explicit unused subcontract release independently of stock display capabilities', () => {
  const capability = CAPABILITY.inventorySubcontractRelease
  expect(CAPABILITY_ROUTE[capability]).toBe('/stok/fason-ayirma-birak')
  expect(matchRoutes(inventoryRoutes, CAPABILITY_ROUTE[capability])?.at(-1)?.route.path).toBe('stok/fason-ayirma-birak')
  expect(NAV_CAPABILITIES).toContain(capability)
  expect(CAPABILITY_NAV_LABEL[capability]).toBe('Kullanılmayan fason ayırmayı bırak')
})


it('registers the existing native opening-cost purpose literal and its separate Work mount exactly', () => {
 // InventoryShellCapabilityProvider's existing declaration; no new permission or native inventory route.
 expect(CAPABILITY.inventoryOpeningCostWorkDecision).toBe('inventory.opening-cost.work-decision')
 expect(CAPABILITY_ROUTE[CAPABILITY.inventoryOpeningCostWorkDecision]).toBe('/onaylarim/inventory.opening-cost.v1/:approvalId')
 expect(CAPABILITY_NAV_LABEL[CAPABILITY.inventoryOpeningCostWorkDecision]).toBe('')
 expect(matchRoutes(workRoutes,'/onaylarim/inventory.opening-cost.v1/cccccccc-0000-4000-8000-000000000001')?.at(-1)?.route.path)
  .toBe('onaylarim/inventory.opening-cost.v1/:approvalId')
 expect(inventoryRoutes).toHaveLength(16)
})
