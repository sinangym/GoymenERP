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
 *
 * The shell DECLARES these ids, which is only the frontend saying it has an
 * implementation for them. Whether one is GRANTED is the server's capability provider
 * (#246, and ORG-03's own declaration) — so a route whose id this caller was not granted
 * still mounts nothing, by design rather than by omission.
 */

import type { RouteObject } from 'react-router'
import { CapabilityRoute } from '../shell/CapabilityRoute'
import { ORGANIZATION_CAPABILITY, ORGANIZATION_ROUTE } from './capabilities'
import { OrganizationSettingsPage } from './OrganizationSettingsPage'
import { LocationDetailPage } from './LocationDetailPage'
import { WorkCenterListPage } from './WorkCenterListPage'

export const organizationRoutes: RouteObject[] = [
  {
    path: ORGANIZATION_ROUTE[ORGANIZATION_CAPABILITY.settingsList].slice(1),
    element: (
      <CapabilityRoute capability={ORGANIZATION_CAPABILITY.settingsList}>
        <OrganizationSettingsPage />
      </CapabilityRoute>
    ),
  },
  {
    // ORG-03. Static, and listed ahead of the parameterized route for the same reason
    // `lokasyon` is: a static segment must never be reachable as a record id. Here the
    // two paths also differ in LENGTH, so they could not collide today — the ordering is
    // kept anyway, because the rule that protects the table is the one that holds when a
    // future path does collide, not the one that happens to be unnecessary now.
    path: ORGANIZATION_ROUTE[ORGANIZATION_CAPABILITY.workCentersList].slice(1),
    element: (
      <CapabilityRoute capability={ORGANIZATION_CAPABILITY.workCentersList}>
        <WorkCenterListPage />
      </CapabilityRoute>
    ),
  },
  {
    path: ORGANIZATION_ROUTE[ORGANIZATION_CAPABILITY.locationDetail].slice(1),
    element: (
      <CapabilityRoute capability={ORGANIZATION_CAPABILITY.locationDetail}>
        <LocationDetailPage />
      </CapabilityRoute>
    ),
  },
]
