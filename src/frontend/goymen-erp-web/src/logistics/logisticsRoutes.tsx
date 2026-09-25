/**
 * The Logistics route table, ready for the shell owner to spread into `shell/router.tsx` as
 * `...logisticsRoutes` (see ./SHARED_MOUNT_HANDOFF.md) — never written into `src/shell/**` from here.
 *
 * Each route is behind the shared `CapabilityRoute` gate with the backend provider's id. The
 * declarations are published after the exact-head `l04` staged-profile acceptance (#358); the shell
 * still exposes each route only when the actor holds its required company-scoped permission.
 */

import type { ReactNode } from 'react'
import type { RouteObject } from 'react-router'
import { CapabilityRoute } from '../shell/CapabilityRoute'
import type { CapabilityId } from '../shell/capabilities'
import { LOGISTICS_CAPABILITY, LOGISTICS_ROUTE, type LogisticsCapabilityId } from './capabilities'
import { InstructionListPage } from './InstructionListPage'
import { InstructionCreatePage } from './InstructionCreatePage'
import { InstructionDetailPage } from './InstructionDetailPage'

function gate(capability: LogisticsCapabilityId, children: ReactNode) {
  // The cast goes once the shell owner adds the three ids to `src/shell/capabilities.ts` (the mount handoff).
  return <CapabilityRoute capability={capability as unknown as CapabilityId}>{children}</CapabilityRoute>
}

export const logisticsRoutes: RouteObject[] = [
  { path: LOGISTICS_ROUTE.list.slice(1), element: gate(LOGISTICS_CAPABILITY.instructionsList, <InstructionListPage />) },
  // Static `yeni` before `:instructionId`, so it is never read as a record id.
  { path: LOGISTICS_ROUTE.create.slice(1), element: gate(LOGISTICS_CAPABILITY.instructionsCreate, <InstructionCreatePage />) },
  { path: LOGISTICS_ROUTE.detail.slice(1), element: gate(LOGISTICS_CAPABILITY.instructionsDetail, <InstructionDetailPage />) },
]
