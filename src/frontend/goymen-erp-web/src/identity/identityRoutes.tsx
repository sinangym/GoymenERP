/**
 * The Identity access-view route table, ready for the shell owner to spread into `shell/router.tsx` as
 * `...identityRoutes` (before the catch-all), with `IDENTITY_CAPABILITY` / `IDENTITY_ROUTE` / `IDENTITY_NAV_LABEL`
 * mirrored into `shell/capabilities.ts` and the list's nav entry drawn by `AppShell`.
 *
 * The static list path precedes the parameterized role card; the two differ in length, and the rule is kept anyway
 * so a future static segment is never read as a role id. Each entry is wrapped in `CapabilityRoute`: an ungranted
 * capability mounts nothing and renders the same neutral not-found block any unknown path gets.
 */

import type { ReactNode } from 'react'
import type { RouteObject } from 'react-router'
import { CapabilityRoute } from '../shell/CapabilityRoute'
import { IDENTITY_CAPABILITY, IDENTITY_ROUTE, type IdentityCapabilityId } from './capabilities'
import { IdentityAccessPage } from './IdentityAccessPage'
import { IdentityRoleDetailPage } from './IdentityRoleDetailPage'

function gate(capability: IdentityCapabilityId, children: ReactNode) {
  return <CapabilityRoute capability={capability}>{children}</CapabilityRoute>
}

export const identityRoutes: RouteObject[] = [
  {
    path: IDENTITY_ROUTE[IDENTITY_CAPABILITY.accessList].slice(1),
    element: gate(IDENTITY_CAPABILITY.accessList, <IdentityAccessPage />),
  },
  {
    path: IDENTITY_ROUTE[IDENTITY_CAPABILITY.roleDetail].slice(1),
    element: gate(IDENTITY_CAPABILITY.roleDetail, <IdentityRoleDetailPage />),
  },
]
