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
import { CAPABILITY, CAPABILITY_ROUTE, RESERVED_CARI_PATHS } from './capabilities'
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
      ...RESERVED_CARI_PATHS.map((path) => ({
        /**
         * Declared by the server, not implemented here — and matched BEFORE the
         * param route so `/cari/:partyId` cannot swallow them.
         *
         * Without this, `/cari/yeni` is read as a party id: the detail screen
         * mounts and issues a read for a record that cannot exist. Codex caught it
         * on #172 (P1-2). The neutral block is the same one any unknown path gets,
         * so a reserved-but-unbuilt path discloses nothing about what exists.
         */
        path,
        element: <StateBlock state={{ kind: 'not-found' }} subject="sayfa" />,
      })),
      {
        /**
         * C-03 detail, gated on its OWN capability.
         *
         * An earlier version mounted this under `cari.parties.list`, on the
         * grounds that the registry published no separate detail capability and
         * inventing one would be the frontend granting itself authority the
         * backend never declared. That reasoning was right at the time and the
         * fact has since changed: #182 publishes `cari.parties.detail` for this
         * exact path, held `ImplementationAvailable=false`.
         *
         * Leaving it under the list capability would now mean a list-only
         * snapshot could mount C-03 and call the detail API — the frontend
         * widening a grant the server deliberately kept separate.
         */
        path: CAPABILITY_ROUTE[CAPABILITY.cariPartiesDetail].slice(1),
        element: (
          <CapabilityRoute capability={CAPABILITY.cariPartiesDetail}>
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
