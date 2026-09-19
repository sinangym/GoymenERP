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
import { CariCreatePage } from '../cari/CariCreatePage'
import { CariLinkWorkspacePage } from '../cari/CariLinkWorkspacePage'
import { CariDetailPage } from '../cari/CariDetailPage'
import { CariContactEditPage } from '../cari/CariContactEditPage'
import { productRoutes } from '../product/productRoutes'
import { organizationRoutes } from '../organization/organizationRoutes'
import { salesRoutes } from '../sales/salesRoutes'
import { manufacturingRoutes } from '../manufacturing/manufacturingRoutes'
import { financeRoutes } from '../finance/financeRoutes'
import { logisticsRoutes } from '../logistics/logisticsRoutes'
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
         * STATIC paths are matched BEFORE the `/cari/:partyId` param route, so
         * `yeni` and `sirkete-bagla` can never be read as a party id — the
         * detail screen would otherwise mount and issue a read for a record that
         * cannot exist (Codex #172 P1-2). Each is gated on its OWN capability:
         * an ungranted create or link route closes onto the same neutral
         * not-found block any unknown path gets, and calls nothing.
         */
        path: CAPABILITY_ROUTE[CAPABILITY.cariPartiesCreate].slice(1),
        element: (
          <CapabilityRoute capability={CAPABILITY.cariPartiesCreate}>
            <CariCreatePage />
          </CapabilityRoute>
        ),
      },
      {
        path: CAPABILITY_ROUTE[CAPABILITY.cariAccountsLink].slice(1),
        element: (
          <CapabilityRoute capability={CAPABILITY.cariAccountsLink}>
            <CariLinkWorkspacePage />
          </CapabilityRoute>
        ),
      },
      {
        /**
         * C-06 — the operational contact/address editor, on its own capability and
         * BEFORE the `/cari/:partyId` param route for the same reason `yeni` is: a
         * deeper static segment must never be read as a party id.
         *
         * A separate grant from the detail, like the detail is from the list (#182):
         * holding the read must not imply holding the write, and gating the route is
         * what spares the user an editor whose Save they may not use. The API still
         * re-authorizes `counterparty.parties.read` + `counterparty.parties.update.basic`
         * on every request — a capability is UI visibility, never authorization.
         */
        path: CAPABILITY_ROUTE[CAPABILITY.cariAccountsContactUpdate].slice(1),
        element: (
          <CapabilityRoute capability={CAPABILITY.cariAccountsContactUpdate}>
            <CariContactEditPage />
          </CapabilityRoute>
        ),
      },
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
      /**
       * The Product lane's own routes, declared in `src/product/productRoutes.tsx` and
       * mounted here by the shared-integration owner.
       *
       * Declared BEFORE the catch-all by convention, not because react-router needs it: the
       * pinned version (8.3.1) ranks routes by score, not declaration order. A static
       * segment outscores `path: '*'`, which also carries a splat penalty, so `/urun` wins
       * wherever it sits; order only breaks ties between equal scores. No render can tell
       * the two orders apart, so `featureRouteAccess.test.tsx` pins this order
       * structurally and proves reachability separately by rendering the routes. Each entry is
       * gated on its own capability exactly like the Cari ones, and that file declares
       * `/urun/yeni` and `/urun/:productId/duzenle` ahead of `/urun/:productId`, so no
       * static segment is ever read as a product id. The create and edit entries also
       * carry a pending-attempt observer OUTSIDE their gate; it renders nothing, fetches
       * nothing and discloses nothing, so an ungranted route still shows only the
       * neutral not-found block.
       */
      ...productRoutes,
      /**
       * The Organization lane's own routes, declared in
       * `src/organization/organizationRoutes.tsx` and mounted here by the
       * shared-integration owner.
       *
       * Declared BEFORE the catch-all by the same convention as the Product routes above:
       * react-router 8.3.1 ranks by score, so the catch-all cannot shadow these wherever they
       * sit. `featureRouteAccess.test.tsx` renders them for reachability and pins the order
       * structurally. Each entry is
       * gated on its own capability exactly like the Cari ones, and that file fixes the
       * segment order itself — the card's static `lokasyon` segment sits ahead of
       * `:locationId`, so no static segment is read as a record id.
       */
      ...organizationRoutes,
      /**
       * The Sales lane's own routes, declared in `src/sales/salesRoutes.tsx` (list, `yeni`, then
       * `:draftId`) and mounted here by the shared-integration owner (S03 handoff), before the
       * catch-all. Each is wrapped in `CapabilityRoute`: an unpublished capability reads nothing.
       */
      ...salesRoutes,
      /**
       * The Manufacturing lane's own routes, declared in
       * `src/manufacturing/manufacturingRoutes.tsx` and mounted here by the shared-integration
       * owner (MFG-UI handoff). Each is wrapped in `CapabilityRoute`: the capability is published
       * (MFG-UI-F), so a route opens only for an actor the registry grants it; for anyone else it
       * renders the neutral not-found block and reads nothing.
       */
      ...manufacturingRoutes,
      /**
       * The Finance lane's own routes, declared in `src/finance/financeRoutes.tsx` (receipt list, `yeni`,
       * `:sourceId`; bank-account list and `yeni`) and mounted here by the shared-integration owner
       * (FIN-UI-1b handoff), before the catch-all. Each is wrapped in `CapabilityRoute`: the declarations
       * are published (FIN-UI-1c), so a route opens only for an actor the registry grants it; for anyone
       * else it closes onto the neutral not-found block.
       */
      ...financeRoutes,
      /**
       * The Logistics lane's own routes, declared in `src/logistics/logisticsRoutes.tsx` (list, `yeni`, then
       * `:instructionId`) and mounted here by the shared-integration owner (LOG-4b handoff), before the
       * catch-all. Each is wrapped in `CapabilityRoute`: while the declarations are staged (until the l04
       * suite), every Logistics route closes onto the neutral not-found block and reads nothing.
       */
      ...logisticsRoutes,
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
