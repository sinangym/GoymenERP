/**
 * The audit log route table (UI-K02), spread into `shell/router.tsx` as `...auditRoutes` before the catch-all, with
 * `AUDIT_CAPABILITY` / `AUDIT_ROUTE` / `AUDIT_NAV_LABEL` mirrored into `shell/capabilities.ts` and the nav entry drawn
 * by `AppShell` under Ayarlar › Sistem Yönetimi. The entry is wrapped in `CapabilityRoute`: an ungranted capability
 * mounts nothing and renders the same neutral not-found block any unknown path gets.
 */

import type { RouteObject } from 'react-router'
import { CapabilityRoute } from '../shell/CapabilityRoute'
import { AUDIT_CAPABILITY, AUDIT_ROUTE } from './capabilities'
import { AuditLogPage } from './AuditLogPage'

export const auditRoutes: RouteObject[] = [
  {
    path: AUDIT_ROUTE[AUDIT_CAPABILITY.list].slice(1),
    element: (
      <CapabilityRoute capability={AUDIT_CAPABILITY.list}>
        <AuditLogPage />
      </CapabilityRoute>
    ),
  },
]
