/**
 * The Manufacturing route table, ready for the shell owner to spread into
 * `shell/router.tsx` as `...manufacturingRoutes` (see ./SHARED_MOUNT_HANDOFF.md).
 *
 * Wrapped in `<CapabilityRoute>`: the capability is published (MFG-UI-F #349), but the
 * registry grants it only to an actor holding its permission; for anyone else the route
 * mounts nothing and renders the same neutral not-found block any unknown path gets.
 */

import type { ReactNode } from 'react'
import type { RouteObject } from 'react-router'
import { CapabilityRoute } from '../shell/CapabilityRoute'
import type { CapabilityId } from '../shell/capabilities'
import {
  MANUFACTURING_CAPABILITY,
  MANUFACTURING_ROUTE,
  type ManufacturingCapabilityId,
} from './capabilities'
import { OrderDetailPage } from './OrderDetailPage'

/**
 * `CapabilityRoute` types its prop as the shell's `CapabilityId` union, which does not
 * yet contain the Manufacturing ids — adding them is the shell owner's edit (the
 * handoff), not this lane's. The cast is confined here, the Product lane's pattern: it
 * widens a compile-time union and changes NO runtime behaviour, because `hasCapability`
 * compares a plain string against the server's published list, so an unpublished id
 * still fails closed. Once the shell declares the ids the cast can go.
 */
function gate(capability: ManufacturingCapabilityId, children: ReactNode) {
  return <CapabilityRoute capability={capability as CapabilityId}>{children}</CapabilityRoute>
}

export const manufacturingRoutes: RouteObject[] = [
  {
    path: MANUFACTURING_ROUTE[MANUFACTURING_CAPABILITY.orderDetail].slice(1),
    element: gate(MANUFACTURING_CAPABILITY.orderDetail, <OrderDetailPage />),
  },
]
