import { describe, expect, it } from 'vitest'
import { matchRoutes } from 'react-router'
import { PAINT_MEMBERSHIP_CAPABILITY, PAINT_MEMBERSHIP_ROUTE } from '../product/paintMemberships/contract'
import { productRoutes } from '../product/productRoutes'
import { CAPABILITY, CAPABILITY_ROUTE } from './capabilities'
import { NAV_CAPABILITIES, NAV_GROUPS } from './navigation'

describe('paint membership owner mount', () => {
  it('exposes one Product route and navigation entry using the owner capability', () => {
    expect(CAPABILITY.productPaintMemberships).toBe(PAINT_MEMBERSHIP_CAPABILITY)
    expect(CAPABILITY_ROUTE[CAPABILITY.productPaintMemberships]).toBe(PAINT_MEMBERSHIP_ROUTE)
    expect(matchRoutes(productRoutes, PAINT_MEMBERSHIP_ROUTE)?.at(-1)?.route.path).toBe(PAINT_MEMBERSHIP_ROUTE.slice(1))
    expect(NAV_CAPABILITIES.filter((id) => id === PAINT_MEMBERSHIP_CAPABILITY)).toHaveLength(1)
    expect(NAV_GROUPS.find((group) => group.id === 'product')?.entries).toContain(PAINT_MEMBERSHIP_CAPABILITY)
  })
})
