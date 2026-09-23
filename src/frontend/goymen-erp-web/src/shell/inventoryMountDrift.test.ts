import { describe, expect, it } from 'vitest'
import { matchPath, matchRoutes } from 'react-router'
import { INVENTORY_OPENING_CAPABILITY, INVENTORY_OPENING_ROUTE } from '../inventory/capabilities'
import { inventoryRoutes } from '../inventory/inventoryRoutes'
import { NAV_CAPABILITIES } from './AppShell'
import { CAPABILITY, CAPABILITY_NAV_LABEL, CAPABILITY_ROUTE } from './capabilities'

describe('shell ↔ Inventory opening-stock contract', () => {
  it('mounts the owner routes under one existing capability and one navigation entry', () => {
    expect(CAPABILITY.inventoryOpeningStockRecord).toBe(INVENTORY_OPENING_CAPABILITY)
    expect(CAPABILITY_ROUTE[CAPABILITY.inventoryOpeningStockRecord]).toBe(INVENTORY_OPENING_ROUTE.home)
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.inventoryOpeningStockRecord]).toBe('Stok Açılışı')
    expect(NAV_CAPABILITIES.filter((item) => item === INVENTORY_OPENING_CAPABILITY)).toHaveLength(1)
    expect(inventoryRoutes).toHaveLength(3)
  })

  it('keeps static create before detail and never reads yeni as a count id', () => {
    const matches = matchRoutes(inventoryRoutes, INVENTORY_OPENING_ROUTE.create)
    expect(matches?.at(-1)?.route.path).toBe(INVENTORY_OPENING_ROUTE.create.slice(1))
    expect(matches?.at(-1)?.params.countId).toBeUndefined()
    expect(matchPath(INVENTORY_OPENING_ROUTE.detail, '/stok/acilis/COUNT-1')?.params.countId).toBe('COUNT-1')
  })
})
