/**
 * The Organization route table, ready for the shell owner to spread into
 * `shell/router.tsx` as `...organizationRoutes`.
 *
 * It lives HERE, in the lane that owns these screens, so the shared router gains one
 * import and one spread instead of a block that has to be re-reviewed every time a
 * screen changes. That is the shape the shell owner asked for.
 *
 * ROUTE ORDER IS LOAD-BEARING and is fixed here, not left to the consumer: the static
 * `lokasyon` segment sits in the path ahead of `:locationId`, so no static segment can
 * ever be read as a record id. That is the #172 P1-2 lesson from `/cari/yeni`.
 *
 * Every route is wrapped in `<CapabilityRoute>`: an ungranted or unresolved capability
 * mounts nothing and renders the same neutral not-found block any unknown path gets.
 * Since the server publishes no `organization.*` capability yet, that is the state
 * these routes are in today — by design, not by omission.
 */

import type { RouteObject } from 'react-router'
import { CapabilityRoute } from '../shell/CapabilityRoute'
import type { CapabilityId } from '../shell/capabilities'
import { ORGANIZATION_CAPABILITY, ORGANIZATION_ROUTE } from './capabilities'
import { OrganizationSettingsPage } from './OrganizationSettingsPage'
import { LocationDetailPage } from './LocationDetailPage'

/**
 * TEMPORARY CAST — remove when the shell owner's `capabilities.ts` delta lands.
 *
 * `CapabilityId` is a union built from the ids declared in `shell/capabilities.ts`.
 * The Organization ids are not in it yet (that file is the shell owner's, and their
 * delta is the first of the two layers). The moment those ids are declared there, the
 * union contains them and this cast becomes unnecessary — at which point it is
 * deleted, not kept. The Ürün lane hit exactly this ordering and resolved it the same
 * way.
 *
 * The cast changes no behaviour: `CapabilityRoute` compares the value against the
 * SERVER's snapshot, so an id the server has not granted still mounts nothing.
 */
function capability(id: string): CapabilityId {
  return id as CapabilityId
}

export const organizationRoutes: RouteObject[] = [
  {
    path: ORGANIZATION_ROUTE[ORGANIZATION_CAPABILITY.settingsList].slice(1),
    element: (
      <CapabilityRoute capability={capability(ORGANIZATION_CAPABILITY.settingsList)}>
        <OrganizationSettingsPage />
      </CapabilityRoute>
    ),
  },
  {
    path: ORGANIZATION_ROUTE[ORGANIZATION_CAPABILITY.locationDetail].slice(1),
    element: (
      <CapabilityRoute capability={capability(ORGANIZATION_CAPABILITY.locationDetail)}>
        <LocationDetailPage />
      </CapabilityRoute>
    ),
  },
]
