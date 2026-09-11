/**
 * The one JSX-needing piece of the C-02 / CARI-LINK test harness: mount the
 * REAL shell (provider + memory router + gated routes) at a path. Kept in its
 * own module so ./commandPageHarness.ts can stay a plain `.ts` fixture file.
 * Test-only; not used by production code.
 */

import { render } from '@testing-library/react'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { ShellProvider } from '../shell/ShellProvider'
import { shellRoutes } from '../shell/router'

export function renderAt(
  path: string,
  options: {
    /**
     * Keep pages the way PRODUCTION does. The default client keeps nothing once a
     * key loses its observer (gcTime 0), so every key switch is a real request —
     * which hides any bug where a RETAINED page stands in for a fresh read. With
     * `retainCache` the client uses the production query defaults from
     * ../shell/ShellProvider.tsx: 30 s staleTime, default (5 min) gcTime.
     */
    readonly retainCache?: boolean
  } = {},
) {
  const router = createMemoryRouter(shellRoutes, { initialEntries: [path] })
  const queries = options.retainCache
    ? { retry: false, staleTime: 30_000, refetchOnWindowFocus: false }
    : { retry: false, gcTime: 0 }
  const queryClient = new QueryClient({ defaultOptions: { queries } })
  render(
    <ShellProvider queryClient={queryClient}>
      <RouterProvider router={router} />
    </ShellProvider>,
  )
  return { queryClient, router }
}
