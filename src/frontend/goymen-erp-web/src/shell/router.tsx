/**
 * SHELL-PILOT-01 route table. One layout route (the shell) + an index.
 *
 * Route guards / capability-gated mounts / deep-link non-disclosure come with the
 * first business feature route. `loader`/`action` are NOT backend authorization —
 * every request is re-authorized server-side (ADR-013, ERP-000K §10.2).
 */

import { createBrowserRouter, type RouteObject } from 'react-router'
import { AppShell } from './AppShell'
import { ShellHome } from './ShellHome'

export const shellRoutes: RouteObject[] = [
  {
    path: '/',
    element: <AppShell />,
    children: [{ index: true, element: <ShellHome /> }],
  },
]

export const shellRouter = createBrowserRouter(shellRoutes)
