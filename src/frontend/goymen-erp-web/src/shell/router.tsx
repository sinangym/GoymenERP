/**
 * SHELL-PILOT-01 route table: one layout route (the shell) + an index + the feature
 * routes the frontend has an implementation for.
 *
 * A feature route is always wrapped in <CapabilityRoute> — it mounts only when the
 * server-published capability is present. `loader`/`action` are NOT backend
 * authorization; every request is re-authorized server-side (ADR-013, ERP-000K §10.2).
 */

import { createBrowserRouter, type RouteObject } from 'react-router'
import { AppShell } from './AppShell'
import { ShellHome } from './ShellHome'
import { CapabilityRoute } from './CapabilityRoute'
import { CAPABILITY, CAPABILITY_ROUTE } from './capabilities'
import { CariListPage } from '../cari/CariListPage'

export const shellRoutes: RouteObject[] = [
  {
    path: '/',
    element: <AppShell />,
    children: [
      { index: true, element: <ShellHome /> },
      {
        path: CAPABILITY_ROUTE[CAPABILITY.cariPartiesList].slice(1),
        element: (
          <CapabilityRoute capability={CAPABILITY.cariPartiesList}>
            <CariListPage />
          </CapabilityRoute>
        ),
      },
    ],
  },
]

export const shellRouter = createBrowserRouter(shellRoutes)
