/**
 * The audit route table, ready for the shell owner to spread into `shell/router.tsx` as `...auditRoutes` (before the
 * catch-all), with `AUDIT_CAPABILITY` / `AUDIT_ROUTE` / `AUDIT_NAV_LABEL` mirrored into `shell/capabilities.ts` and the
 * nav entry drawn by `AppShell` — once the backend publishes the capability (./capabilities.ts).
 *
 * The capability is passed as a one-element list because it is not in the shell registry yet; `CapabilityRoute` treats
 * that exactly like a registry id (same fresh entry check, same neutral block when ungranted).
 */

import type { RouteObject } from 'react-router'
import { CapabilityRoute } from '../shell/CapabilityRoute'
import { AUDIT_CAPABILITY, AUDIT_ROUTE } from './capabilities'
import { AuditEventsPage } from './AuditEventsPage'

export const auditRoutes: RouteObject[] = [
  {
    path: AUDIT_ROUTE[AUDIT_CAPABILITY.eventsList].slice(1),
    element: (
      <CapabilityRoute capability={[AUDIT_CAPABILITY.eventsList]}>
        <AuditEventsPage />
      </CapabilityRoute>
    ),
  },
]
