/**
 * The Product route table, owned by the Product lane and spread into the shell
 * router by its single writer.
 *
 * WHY IT LIVES HERE. `src/shell/router.tsx` has one writer and PR #235 is editing
 * it. Exporting the routes as data means the shell owner's change is one import
 * plus one spread — see ./SHARED_MOUNT_HANDOFF.md — instead of forty lines of
 * Product markup landing in someone else's file.
 *
 * The shared `CapabilityRoute` is REUSED, not reimplemented. It carries the entry
 * gate that closes the revisit hole Codex found on #146/#148 (a revoked grant, a
 * Back within the stale window, and cached rows painted with no capability call in
 * between). A second, thinner gate beside it is how the two drift apart, and the
 * thinner one is always the one that is wrong.
 */

import type { ReactNode } from 'react'
import type { RouteObject } from 'react-router'
import { CapabilityRoute } from '../shell/CapabilityRoute'
import type { CapabilityId } from '../shell/capabilities'
import {
  ActivationPendingAttemptScope,
  EditPendingAttemptScope,
  PendingAttemptDiscardObserver,
} from './PendingAttemptDiscardObserver'
import { createScope } from './pendingAttempt'
import { PRODUCT_CAPABILITY, PRODUCT_CAPABILITY_ROUTE, type ProductCapabilityId } from './capabilities'
import { ProductListPage } from './ProductListPage'
import { ProductDetailPage } from './ProductDetailPage'
import { ProductCreatePage } from './ProductCreatePage'
import { ProductEditPage } from './ProductEditPage'
import { priceListRoutes } from './priceLists/priceListRoutes'

/**
 * `CapabilityRoute` types its prop as the shell's `CapabilityId` union, which does
 * not yet contain the Product ids — adding them is the shell owner's edit, not
 * this lane's.
 *
 * The cast is confined to this one function ON PURPOSE. It widens a compile-time
 * union and changes NO runtime behaviour: `hasCapability` already takes a plain
 * string and compares against the server's published list, so an id the server
 * never published still fails closed exactly as it does today. The handoff's step 1
 * adds the three ids to `CAPABILITY`, after which this cast can be deleted without
 * touching anything else.
 */
function gate(capability: ProductCapabilityId, children: ReactNode) {
  return <CapabilityRoute capability={capability as CapabilityId}>{children}</CapabilityRoute>
}

/**
 * Child routes for the shell's layout route. Paths are relative (no leading
 * slash), matching how the Cari routes are declared.
 *
 * ORDER MATTERS. `/urun/yeni` is declared BEFORE `/urun/:productId` so the static
 * path can never be matched as a product id. React Router ranks static segments
 * above dynamic ones, so this is belt and braces — but the Cari slice shipped the
 * bug this prevents (#172 P1-2): the create URL mounted the detail screen, which
 * then issued a read for a record that cannot exist.
 */
export const productRoutes: RouteObject[] = [
  {
    path: PRODUCT_CAPABILITY_ROUTE[PRODUCT_CAPABILITY.productsList].slice(1),
    element: gate(PRODUCT_CAPABILITY.productsList, <ProductListPage />),
  },
  {
    path: PRODUCT_CAPABILITY_ROUTE[PRODUCT_CAPABILITY.productsCreate].slice(1),
    /**
     * The write screens carry a pending-attempt observer OUTSIDE the gate — see
     * `PendingAttemptDiscardObserver`. It must outlive the form, because the moment
     * it has to act is the moment the form has already been unmounted.
     */
    element: (
      <PendingAttemptDiscardObserver scope={createScope()}>
        {gate(PRODUCT_CAPABILITY.productsCreate, <ProductCreatePage />)}
      </PendingAttemptDiscardObserver>
    ),
  },
  {
    /**
     * The edit route is declared BEFORE the bare `:productId` for the same reason
     * `yeni` is: `/urun/<id>/duzenle` must never be read as a product id. It is
     * also gated on its own capability — reading a card and rewriting it are
     * different authorities.
     */
    path: PRODUCT_CAPABILITY_ROUTE[PRODUCT_CAPABILITY.productsEdit].slice(1),
    element: (
      <EditPendingAttemptScope>
        {gate(PRODUCT_CAPABILITY.productsEdit, <ProductEditPage />)}
      </EditPendingAttemptScope>
    ),
  },
  {
    /**
     * Gated on its OWN capability. Holding the list must not imply holding the
     * 360: a list-only snapshot that could open the detail and call its API is
     * the frontend widening a grant the server kept separate.
     */
    path: PRODUCT_CAPABILITY_ROUTE[PRODUCT_CAPABILITY.productsDetail].slice(1),
    element: (
      <ActivationPendingAttemptScope>
        {gate(PRODUCT_CAPABILITY.productsDetail, <ProductDetailPage />)}
      </ActivationPendingAttemptScope>
    ),
  },
  /**
   * AT-018 U-05 price lists (company-scoped, their own capability ids and routes under `/fiyat-listeleri`, see
   * ./priceLists/capabilities.ts). Spread here so the shell mounts them with no shell edit.
   */
  ...priceListRoutes,
]
