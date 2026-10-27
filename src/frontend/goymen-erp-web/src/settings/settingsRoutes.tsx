/**
 * The Settings route table, spread into `shell/router.tsx` (before the catch-all). The one entry is wrapped in
 * `CapabilityRoute`: an ungranted capability mounts nothing and renders the same neutral not-found block any unknown
 * path gets, and calls nothing.
 */

import type { RouteObject } from 'react-router'
import { CapabilityRoute } from '../shell/CapabilityRoute'
import { SETTINGS_CAPABILITY, SETTINGS_ROUTE } from './capabilities'
import { SalesDiscountLimitsPage } from './SalesDiscountLimitsPage'

export const settingsRoutes: RouteObject[] = [
  {
    path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.salesDiscountLimits].slice(1),
    element: (
      <CapabilityRoute capability={SETTINGS_CAPABILITY.salesDiscountLimits}>
        <SalesDiscountLimitsPage />
      </CapabilityRoute>
    ),
  },
]
