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
import { CariDetailPage } from '../cari/CariDetailPage'
import { StateBlock } from './StateBlock'

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
      {
        /**
         * C-03 detail, mounted under the SAME capability as the list.
         *
         * The server authorizes both reads with one permission
         * (`counterparty.parties.read`) and the registry publishes no
         * `cari.parties.detail`. Inventing one here would be the frontend
         * granting itself authority the backend never declared — the exact thing
         * DEC-GOV-007 exists to stop.
         *
         * If a separately grantable detail is wanted, it has to come from the
         * provider first, not from this table.
         */
        path: 'cari/:partyId',
        element: (
          <CapabilityRoute capability={CAPABILITY.cariPartiesList}>
            <CariDetailPage />
          </CapabilityRoute>
        ),
      },
      {
        /**
         * Catch-all. Without it react-router renders its own error boundary —
         * "Unexpected Application Error! 404 Not Found" — outside the shell.
         *
         * That is a DISCLOSURE, not a cosmetic gap. A capability-gated route the
         * caller is not entitled to renders the neutral not-found block INSIDE the
         * shell; a genuinely unknown path rendered a raw framework page. The two
         * being distinguishable tells the caller which one they hit, which is
         * exactly what ERP-000K §10.2 non-disclosure forbids: a denied module and
         * a nonexistent page must be indistinguishable.
         *
         * Found running B-C01-002-UI against the real stack with an unauthorized
         * actor (Issue #146).
         */
        path: '*',
        element: <StateBlock state={{ kind: 'not-found' }} subject="sayfa" />,
      },
    ],
  },
]

export const shellRouter = createBrowserRouter(shellRoutes)
