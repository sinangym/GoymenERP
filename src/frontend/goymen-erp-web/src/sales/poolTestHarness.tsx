/**
 * TEST SUPPORT ONLY — the S-01 pool screens in the REAL shell (`AppShell`, `ShellProvider`, `CapabilityRoute`) around
 * `salesRoutes`, under a TEST capability map (./poolFixtures.ts; the backend publishes no id yet — NEEDS-CAPABILITY).
 */

import { render } from '@testing-library/react'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { QueryClient } from '@tanstack/react-query'
import { ShellProvider } from '../shell/ShellProvider'
import { AppShell } from '../shell/AppShell'
import { salesRoutes } from './salesRoutes'
import { SalesPoolCapabilityContext, type SalesPoolCapabilities } from './poolCapabilities'
import { SalesContactInformationSourceContext, SalesCurrencySourceContext, SalesReferenceSourceContext } from './referenceSource'
import { controlledContactInformation, controlledCurrencies, controlledSource } from './testHarness'
import { TEST_POOL } from './poolFixtures'

export function renderPool(initialPath: string, pool: SalesPoolCapabilities = TEST_POOL) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0, refetchOnWindowFocus: false } } })
  const router = createMemoryRouter([{ path: '/', element: <AppShell />, children: salesRoutes }], { initialEntries: [initialPath] })
  return {
    ...render(
      <SalesPoolCapabilityContext.Provider value={pool}>
        <SalesReferenceSourceContext.Provider value={controlledSource}>
          <SalesCurrencySourceContext.Provider value={controlledCurrencies}>
            <SalesContactInformationSourceContext.Provider value={controlledContactInformation}>
              <ShellProvider queryClient={queryClient}>
                <RouterProvider router={router} />
              </ShellProvider>
            </SalesContactInformationSourceContext.Provider>
          </SalesCurrencySourceContext.Provider>
        </SalesReferenceSourceContext.Provider>
      </SalesPoolCapabilityContext.Provider>,
    ),
    router,
  }
}
