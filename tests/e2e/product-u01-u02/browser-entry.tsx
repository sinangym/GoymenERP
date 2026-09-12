/**
 * Test-only composition for the Product T lane: the UNCHANGED production
 * `ShellProvider`, `AppShell` and route table, plus a passive probe.
 *
 * The Product routes are not yet spread into `src/shell/router.tsx` — that is the
 * shell owner's delta (see `src/product/SHARED_MOUNT_HANDOFF.md`). Until it lands,
 * this entry mounts `productRoutes` into the SAME layout route the shell owns,
 * before its catch-all; once it has landed the injection is skipped (the path is
 * already there), so the entry keeps working and keeps measuring the real router.
 * No second shell, provider, query client or router is built here.
 *
 * The probe reads selected shell fields and the pending-attempt store's PHASE and
 * KEY — never a payload — and offers one driver action, an in-app navigation, so
 * a spec can revisit a route the way a sidebar link would without a page reload
 * (a reload is a legitimate end to in-memory work and would prove nothing).
 */
import { StrictMode, useEffect } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createBrowserRouter, type RouteObject } from 'react-router'
import { ShellProvider } from '../../../src/frontend/goymen-erp-web/src/shell/ShellProvider'
import { shellRoutes } from '../../../src/frontend/goymen-erp-web/src/shell/router'
import { useShellState } from '../../../src/frontend/goymen-erp-web/src/shell/shellContext'
import { contextKey, contextKeyString } from '../../../src/frontend/goymen-erp-web/src/shell/shellState'
import { productRoutes } from '../../../src/frontend/goymen-erp-web/src/product/productRoutes'
import { PRODUCT_CAPABILITY } from '../../../src/frontend/goymen-erp-web/src/product/capabilities'
import {
  attemptBindingFor,
  createScope,
  editScope,
  recallAttempt,
} from '../../../src/frontend/goymen-erp-web/src/product/pendingAttempt'
import '../../../src/frontend/goymen-erp-web/src/index.css'
import '../../../src/frontend/goymen-erp-web/src/App.css'

const client = new QueryClient({
  defaultOptions: { queries: { retry: false, staleTime: 30_000, refetchOnWindowFocus: false } },
})

function withProductRoutes(routes: RouteObject[]): RouteObject[] {
  return routes.map((route) => {
    if (route.path !== '/' || route.children === undefined) return route
    const alreadyMounted = route.children.some((child) => child.path === 'urun')
    if (alreadyMounted) return route
    const catchAll = route.children.findIndex((child) => child.path === '*')
    const children = [...route.children]
    children.splice(catchAll === -1 ? children.length : catchAll, 0, ...productRoutes)
    return { ...route, children }
  })
}

const router = createBrowserRouter(withProductRoutes(shellRoutes))

export interface ProbeState {
  phase: string
  actor: string | null
  company: string | null
  epoch: number
  revision: number
  namespace: string | null
  generation: string | null
  sessionInvalid: boolean
  productCapabilities: string[]
}
export interface PendingProbe {
  phase: string
  key: string
}
declare global {
  interface Window {
    urun: {
      read(): ProbeState
      /** In-app navigation, as a sidebar link would do it; never a reload. */
      navigate(path: string): void
      /** The pending-attempt store, phase and key only: 'create' or an edit's product id. */
      pending(scope: 'create' | { edit: string }): PendingProbe | null
    }
  }
}

function Probe() {
  const state = useShellState()
  useEffect(() => {
    const key = contextKey(state)
    window.urun = {
      read: () => ({
        phase: state.phase,
        actor: state.activeContext?.userId ?? null,
        company: state.activeContext?.companyId ?? null,
        epoch: state.sessionEpoch,
        revision: state.committedContextRevision,
        namespace: key ? contextKeyString(key) : null,
        generation: state.activeContext?.capabilityGeneration ?? null,
        sessionInvalid: state.sessionInvalid,
        productCapabilities: (state.activeContext?.capabilities ?? []).filter((id) =>
          (Object.values(PRODUCT_CAPABILITY) as string[]).includes(id),
        ),
      }),
      navigate: (path) => {
        void router.navigate(path)
      },
      pending: (scope) => {
        const binding = attemptBindingFor(
          state,
          scope === 'create' ? createScope() : editScope(scope.edit),
        )
        const held = recallAttempt<unknown, unknown>(binding)
        if (held === null || held.phase === 'idle') return null
        return { phase: held.phase, key: held.snapshot.key }
      },
    }
  }, [state])
  return null
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ShellProvider queryClient={client}>
      <Probe />
      <RouterProvider router={router} />
    </ShellProvider>
  </StrictMode>,
)
