/**
 * The shell mounts exactly the Manufacturing capability ids that lane declares as ROUTES.
 *
 * Two files, two single writers: `src/manufacturing/capabilities.ts` belongs to the Manufacturing
 * lane, `src/shell/capabilities.ts` to the shared-integration owner. If one side renames without the
 * other, the shell quietly stops mounting a screen the server has granted.
 *
 * WHAT THIS IS NOT: backend parity or a grant. The provider publishes the routed ids
 * (`ImplementationAvailable: true`, MFG-UI-F #349; the list joins them in MFG-ORD), but the registry
 * still publishes each only to an actor holding its permissions, so `CapabilityRoute` closes the
 * route onto the neutral not-found block for anyone else. The backend pins its own ids against this
 * lane's `capabilities.ts` in `ManufacturingShellCapabilityProviderContractTests`.
 */

import { describe, expect, it } from 'vitest'
import { matchPath, matchRoutes } from 'react-router'
import { CAPABILITY, CAPABILITY_ROUTE, CAPABILITY_NAV_LABEL } from './capabilities'
import {
  MANUFACTURING_CAPABILITY,
  MANUFACTURING_ROUTE,
  MANUFACTURING_NAV_LABEL,
  orderDetailPath,
  planDetailPath,
} from '../manufacturing/capabilities'

describe('shell ↔ Manufacturing lane capability ids', () => {
  it('declares the same route ids the lane does', () => {
    expect(CAPABILITY.manufacturingOrdersDetail).toBe(MANUFACTURING_CAPABILITY.orderDetail)
    expect(CAPABILITY.manufacturingOrdersList).toBe(MANUFACTURING_CAPABILITY.orderList)
    expect(CAPABILITY.manufacturingPlansCreate).toBe(MANUFACTURING_CAPABILITY.planCreate)
    expect(CAPABILITY.manufacturingPlansDetail).toBe(MANUFACTURING_CAPABILITY.planDetail)
  })

  it('mounts every ROUTE the lane declares, and neither control', () => {
    const laneRouteIds = new Set(Object.keys(MANUFACTURING_ROUTE))
    const shellManufacturingIds = new Set(
      (Object.values(CAPABILITY) as string[]).filter((id) => id.startsWith('manufacturing.')),
    )
    expect([...shellManufacturingIds].sort()).toEqual([...laneRouteIds].sort())
    // The fact forms and the close are controls of the order screen, not routes: a control in this
    // table would mount a screen that does not exist.
    for (const control of [MANUFACTURING_CAPABILITY.orderFacts, MANUFACTURING_CAPABILITY.orderClose]) {
      expect(shellManufacturingIds.has(control)).toBe(false)
      expect(MANUFACTURING_ROUTE[control]).toBeUndefined()
    }
  })

  it('agrees with the lane on the route and the nav label, value by value', () => {
    expect(CAPABILITY_ROUTE[CAPABILITY.manufacturingOrdersDetail]).toBe(
      MANUFACTURING_ROUTE[MANUFACTURING_CAPABILITY.orderDetail],
    )
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.manufacturingOrdersDetail]).toBe(
      MANUFACTURING_NAV_LABEL[MANUFACTURING_CAPABILITY.orderDetail],
    )
    // No nav entry: an order is opened from the list or by its id.
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.manufacturingOrdersDetail]).toBe('')

    expect(CAPABILITY_ROUTE[CAPABILITY.manufacturingOrdersList]).toBe(
      MANUFACTURING_ROUTE[MANUFACTURING_CAPABILITY.orderList],
    )
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.manufacturingOrdersList]).toBe(
      MANUFACTURING_NAV_LABEL[MANUFACTURING_CAPABILITY.orderList],
    )
    // The list is the module's one sidebar entry.
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.manufacturingOrdersList]).toBe('Üretim emirleri')

    expect(CAPABILITY_ROUTE[CAPABILITY.manufacturingPlansCreate]).toBe(
      MANUFACTURING_ROUTE[MANUFACTURING_CAPABILITY.planCreate],
    )
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.manufacturingPlansCreate]).toBe('')
    expect(CAPABILITY_ROUTE[CAPABILITY.manufacturingPlansDetail]).toBe(
      MANUFACTURING_ROUTE[MANUFACTURING_CAPABILITY.planDetail],
    )
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.manufacturingPlansDetail]).toBe('')
  })

  it('never reads the static plan-create path as a plan id', () => {
    const matches = matchRoutes(
      [
        { path: CAPABILITY_ROUTE[CAPABILITY.manufacturingPlansCreate] },
        { path: CAPABILITY_ROUTE[CAPABILITY.manufacturingPlansDetail] },
      ],
      CAPABILITY_ROUTE[CAPABILITY.manufacturingPlansCreate],
    )
    const selected = matches?.at(-1)
    expect(selected?.route.path).toBe(CAPABILITY_ROUTE[CAPABILITY.manufacturingPlansCreate])
    expect(selected?.params.planId).toBeUndefined()
    const planId = '7b1c0f2e-0000-4a00-8000-000000000003'
    expect(matchPath(CAPABILITY_ROUTE[CAPABILITY.manufacturingPlansDetail], planDetailPath(planId))?.params.planId)
      .toBe(planId)
  })

  it('never reads the static list path as an order id', () => {
    // Both are `/uretim/...`; the parameter route must not swallow the list, and the list's own
    // pattern must not match a detail path (the #172 P1-2 rule).
    expect(
      matchPath(
        CAPABILITY_ROUTE[CAPABILITY.manufacturingOrdersDetail],
        CAPABILITY_ROUTE[CAPABILITY.manufacturingOrdersList],
      ),
    ).toBeNull()
    expect(
      matchPath(
        CAPABILITY_ROUTE[CAPABILITY.manufacturingOrdersList],
        orderDetailPath('7b1c0f2e-0000-4a00-8000-000000000002'),
      ),
    ).toBeNull()
  })

  it('matches the lane’s own link helper against the pattern the shell mounts', () => {
    const orderId = '7b1c0f2e-0000-4a00-8000-000000000002'
    const match = matchPath(CAPABILITY_ROUTE[CAPABILITY.manufacturingOrdersDetail], orderDetailPath(orderId))
    expect(match).not.toBeNull()
    expect(match?.params.orderId).toBe(orderId)
  })
})
