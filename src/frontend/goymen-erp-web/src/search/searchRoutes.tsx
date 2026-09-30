/**
 * AT-002 — the global search's route table and its gate, ready for the shell owner to mount (never written into
 * `src/shell/**` from here). The exact shell handoff:
 *
 *   src/shell/router.tsx   — `import { searchRoutes } from '../search/searchRoutes'` and `...searchRoutes,` in the
 *                            layout's children, before the catch-all.
 *   src/shell/AppShell.tsx — `import { GlobalSearchBox } from '../search/GlobalSearchBox'` and `<GlobalSearchBox />`
 *                            in the toolbar, between the company picker `</label>` and the user `<div>`.
 *
 * The search has no capability of its own (no server declaration, no permission): the route opens behind the shared
 * `CapabilityRoute` entry gate on the first searchable owner list the actor holds, so a direct URL, a refresh or Back
 * re-verifies the snapshot exactly as every owner route does; an actor holding none gets the neutral not-found block and
 * reads nothing.
 */

import type { RouteObject } from 'react-router'
import { GlobalSearchPage } from './GlobalSearchPage'
import { SearchRoute } from './SearchRoute'
import { SEARCH_ROUTE } from './searchCapabilities'

export const searchRoutes: RouteObject[] = [
  {
    path: SEARCH_ROUTE.slice(1),
    element: (
      <SearchRoute>
        <GlobalSearchPage />
      </SearchRoute>
    ),
  },
]
