import { isValidElement } from 'react'
import { describe, expect, it } from 'vitest'
import { inventoryRoutes } from './inventoryRoutes'
import { INVENTORY_NEGATIVE_STOCK_ATTEMPTS_ROUTE, INVENTORY_STOCK_POSITIONS_CAPABILITY, inventoryNegativeStockAttemptPath } from './capabilities'

describe('negative stock attempt route ownership', () => {
  it('uses the existing stock-report capability instead of creating or inheriting a write authority', () => {
    const routes = inventoryRoutes.filter((route) => route.path === INVENTORY_NEGATIVE_STOCK_ATTEMPTS_ROUTE.slice(1))
    expect(routes).toHaveLength(1)
    const element = routes[0].element
    expect(isValidElement(element)).toBe(true)
    if (!isValidElement<{ capability: string }>(element)) throw new Error('Missing module route gate')
    expect(element.props.capability).toBe(INVENTORY_STOCK_POSITIONS_CAPABILITY)
  })
  it('carries only actual optional product and depot identities in its entry path', () => {
    const product = '22222222-2222-4222-8222-222222222222'; const location = '33333333-3333-4333-8333-333333333333'
    expect(inventoryNegativeStockAttemptPath()).toBe(INVENTORY_NEGATIVE_STOCK_ATTEMPTS_ROUTE)
    const path = new URL(inventoryNegativeStockAttemptPath(product, location), 'https://synthetic.invalid')
    expect(path.pathname).toBe(INVENTORY_NEGATIVE_STOCK_ATTEMPTS_ROUTE)
    expect(Object.fromEntries(path.searchParams)).toEqual({ productId: product, locationId: location })
  })
})
