/**
 * The Organization route table, spread into `shell/router.tsx` as `...organizationRoutes`.
 *
 * It lives HERE, in the lane that owns these screens, so the shared router gains one
 * import and one spread instead of a block that has to be re-reviewed every time a
 * screen changes. That is the shape the shell owner asked for, and it is why ORG-03
 * below needs no router change at all.
 *
 * ROUTE ORDER IS LOAD-BEARING and is fixed here, not left to the consumer: the static
 * `lokasyon` segment sits in the path ahead of `:locationId`, so no static segment can
 * ever be read as a record id. That is the #172 P1-2 lesson from `/cari/yeni`. The
 * work-center path is a third static sibling and cannot collide with either.
 *
 * Every route is wrapped in `<CapabilityRoute>`: an ungranted or unresolved capability
 * mounts nothing and renders the same neutral not-found block any unknown path gets.
 */

import type { RouteObject } from 'react-router'
import { CapabilityRoute } from '../shell/CapabilityRoute'
import type { CapabilityId } from '../shell/capabilities'
import {
  ORGANIZATION_CAPABILITY,
  ORGANIZATION_MOUNTED_ROUTE,
} from './capabilities'
import { OrganizationSettingsPage } from './OrganizationSettingsPage'
import { LocationDetailPage } from './LocationDetailPage'
import { WorkCenterListPage } from './WorkCenterListPage'

export const organizationRoutes: RouteObject[] = [
  {
    path: ORGANIZATION_MOUNTED_ROUTE[ORGANIZATION_CAPABILITY.settingsList].slice(1),
    element: (
      <CapabilityRoute capability={ORGANIZATION_CAPABILITY.settingsList}>
        <OrganizationSettingsPage />
      </CapabilityRoute>
    ),
  },
  {
    path: ORGANIZATION_MOUNTED_ROUTE[ORGANIZATION_CAPABILITY.workCenterList].slice(1),
    element: (
      /*
       * The cast is TEMPORARY and is the same one the ORG-01/ORG-02 routes carried
       * before the shell mirrored their ids: `CapabilityRoute` types its prop as the
       * shell's `CapabilityId` union, and `organization.work-centers.list` is not in
       * that union until the shell owner applies WORKCENTER_SHELL_MOUNT_HANDOFF.md.
       *
       * It widens NOTHING at runtime. `hasCapability` compares the string against the
       * server's snapshot either way, so an ungranted id closes this route exactly like
       * any other. This lane removes the cast once the shell declares the id.
       */
      <CapabilityRoute capability={ORGANIZATION_CAPABILITY.workCenterList as CapabilityId}>
        <WorkCenterListPage />
      </CapabilityRoute>
    ),
  },
  {
    path: ORGANIZATION_MOUNTED_ROUTE[ORGANIZATION_CAPABILITY.locationDetail].slice(1),
    element: (
      <CapabilityRoute capability={ORGANIZATION_CAPABILITY.locationDetail}>
        <LocationDetailPage />
      </CapabilityRoute>
    ),
  },
]
