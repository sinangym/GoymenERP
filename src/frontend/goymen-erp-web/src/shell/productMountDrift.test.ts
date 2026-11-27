import { matchRoutes } from 'react-router'
import { workRoutes } from '../work/workRoutes'
import { WORK_INBOX_KINDS } from '../work/workInboxKinds'
import { PRICE_LIST_WORK_CAPABILITY, PRICE_LIST_WORK_ROUTE, PACKAGE_STANDARD_WORK_CAPABILITY, PACKAGE_STANDARD_WORK_ROUTE } from '../product/priceLists/capabilities'
/**
 * The shell mounts exactly the capability ids the Product lane declares.
 *
 * The two lists exist in two files because the lanes have a single-writer boundary:
 * `src/product/capabilities.ts` belongs to `CLAUDE-FRONTEND-URUN`, and
 * `src/shell/capabilities.ts` belongs to the shared-integration owner. If one side is
 * renamed without the other, the shell quietly stops mounting a feature the server has
 * granted — a silent loss, and a hard one to diagnose from the symptom.
 *
 * WHAT THIS IS NOT: backend parity. On main the backend registry
 * (`ShellCapabilityRegistry.cs`) pins the same four `product.items.*` ids to the same four
 * routes and grants them from the global Product authority snapshot — but that pin lives
 * in C# and is enforced there. This file pins the two FRONTEND lanes to each other and
 * nothing more, and must not be counted as the server-side check.
 */

import { describe, expect, it } from 'vitest'
import {
  CAPABILITY,
  CAPABILITY_ROUTE,
  CAPABILITY_NAV_LABEL,
  PRODUCT_ACTIVATE_CAPABILITY as SHELL_PRODUCT_ACTIVATE_CAPABILITY,
} from './capabilities'
import {
  PRODUCT_ACTIVATE_CAPABILITY,
  PRODUCT_CAPABILITY,
  PRODUCT_CAPABILITY_ROUTE,
  PRODUCT_CAPABILITY_NAV_LABEL,
} from '../product/capabilities'
import { NAV_CAPABILITIES } from './AppShell'

describe('shell ↔ Product lane capability ids', () => {
  it('declares the same four ids the Product lane does', () => {
    expect(CAPABILITY.productItemsList).toBe(PRODUCT_CAPABILITY.productsList)
    expect(CAPABILITY.productItemsDetail).toBe(PRODUCT_CAPABILITY.productsDetail)
    expect(CAPABILITY.productItemsCreate).toBe(PRODUCT_CAPABILITY.productsCreate)
    expect(CAPABILITY.productItemsEdit).toBe(PRODUCT_CAPABILITY.productsEdit)
  })

  it('pins activation as an action-only detail control without mounting another route or nav item', () => {
    expect(SHELL_PRODUCT_ACTIVATE_CAPABILITY).toBe(PRODUCT_ACTIVATE_CAPABILITY)
    expect(Object.values(CAPABILITY)).not.toContain(SHELL_PRODUCT_ACTIVATE_CAPABILITY)
    expect(Object.keys(CAPABILITY_ROUTE)).not.toContain(SHELL_PRODUCT_ACTIVATE_CAPABILITY)
    expect(NAV_CAPABILITIES).not.toContain(SHELL_PRODUCT_ACTIVATE_CAPABILITY)
  })

  it('mounts every id the Product lane declares, and no more', () => {
    // Set equality in both directions: a lane id the shell never mounts is a feature the
    // server could grant and nobody would show, and a shell id the lane does not declare
    // is a route pointing at nothing.
    const laneValues = new Set(Object.values(PRODUCT_CAPABILITY) as string[])
    const shellProductValues = new Set(
      (Object.values(CAPABILITY) as string[]).filter((id) => id.startsWith('product.')),
    )
    expect([...shellProductValues].sort()).toEqual([...laneValues].sort())
  })

  it('agrees with the lane on every route and nav label, value by value', () => {
    // `Record<CapabilityId, string>` already makes a MISSING entry a type error; this pins
    // the VALUES, which the type cannot, and it reads them from the lane rather than
    // restating them here — a literal copied into both files drifts silently.
    for (const [shellId, laneId] of [
      [CAPABILITY.productItemsList, PRODUCT_CAPABILITY.productsList],
      [CAPABILITY.productItemsDetail, PRODUCT_CAPABILITY.productsDetail],
      [CAPABILITY.productItemsCreate, PRODUCT_CAPABILITY.productsCreate],
      [CAPABILITY.productItemsEdit, PRODUCT_CAPABILITY.productsEdit],
    ] as const) {
      expect(CAPABILITY_ROUTE[shellId]).toBe(PRODUCT_CAPABILITY_ROUTE[laneId])
      expect(CAPABILITY_NAV_LABEL[shellId]).toBe(PRODUCT_CAPABILITY_NAV_LABEL[laneId])
    }
  })

  it('keeps the editor a deeper path than the 360, so neither can match the other', () => {
    /**
     * `/urun/:productId/duzenle` must never be readable as a product id — the #172 P1-2
     * lesson the Cari routes already carry.
     *
     * What is asserted is the PATHS, because that is what makes them unambiguous: the
     * editor has one more segment than the 360, so no URL can match both. An earlier
     * version of this case compared `Object.keys(PRODUCT_CAPABILITY_ROUTE)` instead and
     * failed — correctly: that record lists the 360 before the editor, and a lookup
     * record's key order carries no routing meaning at all. Mount order lives in
     * `productRoutes`, and where it genuinely matters — ahead of the router's `path: '*'`
     * catch-all — it is the shell router's own concern, not this file's.
     */
    const edit = CAPABILITY_ROUTE[CAPABILITY.productItemsEdit]
    const detail = CAPABILITY_ROUTE[CAPABILITY.productItemsDetail]
    expect(edit).toBe('/urun/:productId/duzenle')
    expect(detail).toBe('/urun/:productId')
    expect(edit.startsWith(`${detail}/`)).toBe(true)
    expect(edit.split('/').length).toBe(detail.split('/').length + 1)
  })

  it('keeps Product creation inside the list instead of adding a second nav entry', () => {
    // K5 leaves only the list in the sidebar. Create remains a separately authorised,
    // mounted route reached by the list action; detail and edit remain record actions.
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.productItemsDetail]).toBe('')
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.productItemsEdit]).toBe('')
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.productItemsList]).toBe('Ürünler')
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.productItemsCreate]).toBe('')
    expect(NAV_CAPABILITIES).toContain(CAPABILITY.productItemsList)
    expect(NAV_CAPABILITIES).not.toContain(CAPABILITY.productItemsCreate)
    expect(NAV_CAPABILITIES).not.toContain(CAPABILITY.productItemsDetail)
    expect(NAV_CAPABILITIES).not.toContain(CAPABILITY.productItemsEdit)
  })
})

it('mounts the exact company-scoped package decision beside unchanged global Product master routes',()=>{
 const capability=CAPABILITY.productPackageStandardWorkDecision
 expect(capability).toBe(PACKAGE_STANDARD_WORK_CAPABILITY);expect(capability).toBe('price-lists.package-standard-version.work-decision')
 expect(CAPABILITY_ROUTE[capability]).toBe(PACKAGE_STANDARD_WORK_ROUTE);expect(CAPABILITY_NAV_LABEL[capability]).toBe('')
 expect(matchRoutes(workRoutes,'/onaylarim/product.package-standard-version.v1/cccccccc-0000-4000-8000-000000000001')?.at(-1)?.route.path).toBe('onaylarim/product.package-standard-version.v1/:approvalId')
 expect(WORK_INBOX_KINDS.filter(row=>row.kind==='product.package-standard-version.v1')).toEqual([{kind:'product.package-standard-version.v1',module:'product',capability}])
 expect(CAPABILITY_ROUTE[CAPABILITY.productItemsList]).toBe('/urun');expect(CAPABILITY_ROUTE[CAPABILITY.productItemsDetail]).toBe('/urun/:productId')
 expect((Object.values(CAPABILITY) as string[]).filter(value=>value.startsWith('product.')).sort()).toEqual(Object.values(PRODUCT_CAPABILITY).sort())
})

it('mounts exact price publication purpose beside unchanged native and global Product routes',()=>{
 const capability=CAPABILITY.productPriceListWorkDecision
 expect(capability).toBe(PRICE_LIST_WORK_CAPABILITY);expect(CAPABILITY_ROUTE[capability]).toBe(PRICE_LIST_WORK_ROUTE);expect(CAPABILITY_NAV_LABEL[capability]).toBe('')
 expect(matchRoutes(workRoutes,'/onaylarim/product.price-list-version.v1/cccccccc-0000-4000-8000-000000000001')?.at(-1)?.route.path).toBe('onaylarim/product.price-list-version.v1/:approvalId')
 expect(WORK_INBOX_KINDS.filter(row=>row.kind==='product.price-list-version.v1')).toEqual([{kind:'product.price-list-version.v1',module:'product',capability}])
 expect(CAPABILITY_ROUTE[CAPABILITY.productItemsList]).toBe('/urun')
 expect((Object.values(CAPABILITY) as string[]).filter(value=>value.startsWith('product.')).sort()).toEqual(Object.values(PRODUCT_CAPABILITY).sort())
})
