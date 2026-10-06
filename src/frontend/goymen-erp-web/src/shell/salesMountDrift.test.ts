/**
 * The shell mounts exactly the Sales capability ids and routes that lane declares.
 *
 * Two files, two single writers: `src/sales/capabilities.ts` belongs to the Sales lane (SİPARİŞ),
 * `src/shell/capabilities.ts` to the shared-integration owner. A rename on one side alone would make
 * the shell quietly stop mounting a screen the server has granted.
 *
 * WHAT THIS IS NOT: backend parity or availability. Whether a Sales screen opens is the server's
 * published snapshot (S03-c); an unpublished id keeps `CapabilityRoute` on the neutral not-found block.
 */

import { describe, expect, it } from 'vitest'
import { matchPath } from 'react-router'
import { CAPABILITY, CAPABILITY_ROUTE, CAPABILITY_NAV_LABEL } from './capabilities'
import { SALES_CAPABILITY, SALES_ROUTE, salesDetailPath } from '../sales/capabilities'
import { NAV_CAPABILITIES } from './AppShell'

describe('shell ↔ Sales lane capability ids', () => {
  it('declares the same three ids the lane does', () => {
    expect(CAPABILITY.salesDraftsList).toBe(SALES_CAPABILITY.draftsList)
    expect(CAPABILITY.salesDraftsDetail).toBe(SALES_CAPABILITY.draftsDetail)
    expect(CAPABILITY.salesDraftsCreate).toBe(SALES_CAPABILITY.draftsCreate)
  })

  it('mounts every routed Sales id the lane declares, and nothing else under sales.; the approve control is no route', () => {
    const shellSalesIds = (Object.values(CAPABILITY) as string[]).filter((id) => id.startsWith('sales.'))
    // SALES-A1: `sales.orders.approve` is a control of the S-04 detail (the provider's ParentCapabilityId),
    // checked inside the detail with hasCapability, so it belongs in no route registry (the Finance pattern).
    const laneRoutedIds = (Object.values(SALES_CAPABILITY) as string[]).filter(
      (id) => id !== SALES_CAPABILITY.ordersApprove,
    )
    expect([...shellSalesIds].sort()).toEqual([...laneRoutedIds].sort())
    expect(Object.keys(CAPABILITY_ROUTE)).not.toContain(SALES_CAPABILITY.ordersApprove)
    expect(Object.keys(CAPABILITY_NAV_LABEL)).not.toContain(SALES_CAPABILITY.ordersApprove)
  })

  it('agrees with the lane on every route, value by value', () => {
    expect(CAPABILITY_ROUTE[CAPABILITY.salesDraftsList]).toBe(SALES_ROUTE.list)
    expect(CAPABILITY_ROUTE[CAPABILITY.salesDraftsCreate]).toBe(SALES_ROUTE.create)
    expect(CAPABILITY_ROUTE[CAPABILITY.salesDraftsDetail]).toBe(SALES_ROUTE.detail)
  })

  it('matches the lane’s own link helper, and the static create route is never a draft id', () => {
    const draftId = '7b1c0f2e-0000-4a00-8000-000000000003'
    const match = matchPath(CAPABILITY_ROUTE[CAPABILITY.salesDraftsDetail], salesDetailPath(draftId))
    expect(match?.params.draftId).toBe(draftId)
    // The list pattern swallows neither the create route nor a draft.
    expect(matchPath(CAPABILITY_ROUTE[CAPABILITY.salesDraftsList], SALES_ROUTE.create)).toBeNull()
    expect(matchPath(CAPABILITY_ROUTE[CAPABILITY.salesDraftsList], salesDetailPath(draftId))).toBeNull()
  })

  it('keeps one Sales work-list nav entry while create stays mounted behind the list action', () => {
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.salesDraftsList]).toBe('Sipariş Taslakları')
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.salesDraftsCreate]).toBe('')
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.salesDraftsDetail]).toBe('')
    expect(NAV_CAPABILITIES).toContain(CAPABILITY.salesDraftsList)
    expect(NAV_CAPABILITIES).not.toContain(CAPABILITY.salesDraftsCreate)
  })
})
