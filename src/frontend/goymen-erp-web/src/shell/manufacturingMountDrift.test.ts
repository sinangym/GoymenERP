/**
 * The shell mounts exactly the Manufacturing capability ids that lane declares as ROUTES.
 *
 * Two files, two single writers: `src/manufacturing/capabilities.ts` belongs to the Manufacturing
 * lane, `src/shell/capabilities.ts` to the shared-integration owner. If one side renames without the
 * other, the shell quietly stops mounting a screen the server has granted.
 *
 * WHAT THIS IS NOT: backend parity or availability. Manufacturing's provider declares both ids
 * STAGED (MFG-UI D1), so `hasCapability` answers false and `CapabilityRoute` closes the route onto
 * the neutral not-found block in every normal host. The backend pins its own ids against this lane's
 * `capabilities.ts` in `ManufacturingShellCapabilityProviderContractTests`.
 */

import { describe, expect, it } from 'vitest'
import { matchPath } from 'react-router'
import { CAPABILITY, CAPABILITY_ROUTE, CAPABILITY_NAV_LABEL } from './capabilities'
import {
  MANUFACTURING_CAPABILITY,
  MANUFACTURING_ROUTE,
  MANUFACTURING_NAV_LABEL,
  orderDetailPath,
} from '../manufacturing/capabilities'

describe('shell ↔ Manufacturing lane capability ids', () => {
  it('declares the same route id the lane does', () => {
    expect(CAPABILITY.manufacturingOrdersDetail).toBe(MANUFACTURING_CAPABILITY.orderDetail)
  })

  it('mounts every ROUTE the lane declares, and not the facts control', () => {
    const laneRouteIds = new Set(Object.keys(MANUFACTURING_ROUTE))
    const shellManufacturingIds = new Set(
      (Object.values(CAPABILITY) as string[]).filter((id) => id.startsWith('manufacturing.')),
    )
    expect([...shellManufacturingIds].sort()).toEqual([...laneRouteIds].sort())
    expect(shellManufacturingIds.has(MANUFACTURING_CAPABILITY.orderFacts)).toBe(false)
    expect(MANUFACTURING_ROUTE[MANUFACTURING_CAPABILITY.orderFacts]).toBeUndefined()
  })

  it('agrees with the lane on the route and the nav label, value by value', () => {
    expect(CAPABILITY_ROUTE[CAPABILITY.manufacturingOrdersDetail]).toBe(
      MANUFACTURING_ROUTE[MANUFACTURING_CAPABILITY.orderDetail],
    )
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.manufacturingOrdersDetail]).toBe(
      MANUFACTURING_NAV_LABEL[MANUFACTURING_CAPABILITY.orderDetail],
    )
    // No nav entry: an order is opened by its id.
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.manufacturingOrdersDetail]).toBe('')
  })

  it('matches the lane’s own link helper against the pattern the shell mounts', () => {
    const orderId = '7b1c0f2e-0000-4a00-8000-000000000002'
    const match = matchPath(CAPABILITY_ROUTE[CAPABILITY.manufacturingOrdersDetail], orderDetailPath(orderId))
    expect(match).not.toBeNull()
    expect(match?.params.orderId).toBe(orderId)
  })
})
