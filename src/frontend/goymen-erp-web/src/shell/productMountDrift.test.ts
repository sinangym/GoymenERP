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
import { CAPABILITY, CAPABILITY_ROUTE, CAPABILITY_NAV_LABEL } from './capabilities'
import { PRODUCT_CAPABILITY, PRODUCT_CAPABILITY_ROUTE, PRODUCT_CAPABILITY_NAV_LABEL } from '../product/capabilities'

describe('shell ↔ Product lane capability ids', () => {
  it('declares the same four ids the Product lane does', () => {
    expect(CAPABILITY.productItemsList).toBe(PRODUCT_CAPABILITY.productsList)
    expect(CAPABILITY.productItemsDetail).toBe(PRODUCT_CAPABILITY.productsDetail)
    expect(CAPABILITY.productItemsCreate).toBe(PRODUCT_CAPABILITY.productsCreate)
    expect(CAPABILITY.productItemsEdit).toBe(PRODUCT_CAPABILITY.productsEdit)
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

  it('gives the 360 and the editor no nav entry', () => {
    // Both are reached from a record, not from the sidebar, so `NAV_CAPABILITIES` gains
    // exactly two Product entries when the routes are mounted — not four.
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.productItemsDetail]).toBe('')
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.productItemsEdit]).toBe('')
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.productItemsList]).toBe('Ürünler')
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.productItemsCreate]).toBe('Yeni ürün')
  })
})
