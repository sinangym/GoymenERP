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
import { CAPABILITY, CAPABILITY_ROUTE } from './capabilities'
import { SALES_CAPABILITY, SALES_POOL_CAPABILITY_ID, SALES_POOL_ROUTE, SALES_ROUTE, salesDetailPath } from '../sales/capabilities'
import { CAPABILITY_NAV_LABEL, NAV_CAPABILITIES, NAV_GROUPS } from './navigation'

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
    // UI-20: the two S-01 pool LISTS are named in the sidebar too (their routes stay the lane's pool gates).
    const laneRoutedIds = [
      ...(Object.values(SALES_CAPABILITY) as string[]).filter((id) => id !== SALES_CAPABILITY.ordersApprove),
      SALES_POOL_CAPABILITY_ID.requestsList,
      SALES_POOL_CAPABILITY_ID.quotesList,
    ]
    expect([...shellSalesIds].sort()).toEqual([...laneRoutedIds].sort())
    expect(Object.keys(CAPABILITY_ROUTE)).not.toContain(SALES_CAPABILITY.ordersApprove)
    expect(Object.keys(CAPABILITY_NAV_LABEL)).not.toContain(SALES_CAPABILITY.ordersApprove)
  })

  it('agrees with the lane on every route, value by value', () => {
    // UI-60 / UI-61 (Sales shell 1.9): the two static lists of the dispatch/invoice flow.
    expect(CAPABILITY.salesDispatchesList).toBe(SALES_CAPABILITY.dispatchesList)
    expect(CAPABILITY.salesInvoiceCandidatesList).toBe(SALES_CAPABILITY.invoiceCandidatesList)
    expect(CAPABILITY_ROUTE[CAPABILITY.salesDispatchesList]).toBe(SALES_ROUTE.dispatches)
    expect(CAPABILITY_ROUTE[CAPABILITY.salesInvoiceCandidatesList]).toBe(SALES_ROUTE.invoiceCandidates)
    // UI-80 (Sales shell 1.11): the reports screen.
    expect(CAPABILITY.salesReportsView).toBe(SALES_CAPABILITY.reportsView)
    expect(CAPABILITY_ROUTE[CAPABILITY.salesReportsView]).toBe(SALES_ROUTE.reports)
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
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.salesDraftsList]).toBe('Satış Siparişleri')
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.salesDraftsCreate]).toBe('')
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.salesDraftsDetail]).toBe('')
    expect(NAV_CAPABILITIES).toContain(CAPABILITY.salesDraftsList)
    expect(NAV_CAPABILITIES).not.toContain(CAPABILITY.salesDraftsCreate)
  })

  it('names S-07 and S-08 under the Sales menu, each drawn only where its own capability is granted', () => {
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.salesDispatchesList]).toBe('Sevkiyat / İrsaliyeler')
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.salesInvoiceCandidatesList]).toBe('Faturaya Hazır')
    expect(NAV_CAPABILITIES).toContain(CAPABILITY.salesDispatchesList)
    expect(NAV_CAPABILITIES).toContain(CAPABILITY.salesInvoiceCandidatesList)
    // Neither static route is ever read as a draft id.
    expect(matchPath(CAPABILITY_ROUTE[CAPABILITY.salesDraftsDetail], SALES_ROUTE.dispatches)).toBeNull()
    expect(matchPath(CAPABILITY_ROUTE[CAPABILITY.salesDraftsDetail], SALES_ROUTE.invoiceCandidates)).toBeNull()
  })

  it('names UI-80 under the Sales menu, drawn only where its own capability is granted', () => {
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.salesReportsView]).toBe('Satış raporları')
    expect(NAV_CAPABILITIES).toContain(CAPABILITY.salesReportsView)
    expect(NAV_GROUPS.find((group) => group.id === 'sales')?.entries).toContain(CAPABILITY.salesReportsView)
    expect(matchPath(CAPABILITY_ROUTE[CAPABILITY.salesDraftsDetail], SALES_ROUTE.reports)).toBeNull()
  })

  it('names the S-01 request and quote pools under the Sales menu, on the lane’s own ids and routes', () => {
    expect(CAPABILITY.salesRequestsList).toBe(SALES_POOL_CAPABILITY_ID.requestsList)
    expect(CAPABILITY.salesQuotesList).toBe(SALES_POOL_CAPABILITY_ID.quotesList)
    expect(CAPABILITY_ROUTE[CAPABILITY.salesRequestsList]).toBe(SALES_POOL_ROUTE.requests)
    expect(CAPABILITY_ROUTE[CAPABILITY.salesQuotesList]).toBe(SALES_POOL_ROUTE.quotes)
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.salesRequestsList]).toBe('Satış talepleri')
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.salesQuotesList]).toBe('Satış teklifleri')
    const sales = NAV_GROUPS.find((group) => group.id === 'sales')?.entries ?? []
    expect(sales).toContain(CAPABILITY.salesRequestsList)
    expect(sales).toContain(CAPABILITY.salesQuotesList)
    // Neither the create nor the detail of a pool gets a sidebar entry: they are reached from the list.
    expect(NAV_CAPABILITIES).not.toContain(SALES_POOL_CAPABILITY_ID.requestsCreate)
    expect(NAV_CAPABILITIES).not.toContain(SALES_POOL_CAPABILITY_ID.quotesCreate)
    expect(matchPath(CAPABILITY_ROUTE[CAPABILITY.salesDraftsDetail], SALES_POOL_ROUTE.requests)).toBeNull()
  })
})
