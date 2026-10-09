/**
 * D-05 "Onaylarım" routes, mounted by the shell router before the catch-all. Both are gated on the NATIVE Finance
 * approve capability the server already publishes (`finance.official-invoice-import.approve`): the inbox adds no
 * permission of its own, so whoever may not approve these Finance records sees the same neutral not-found block as
 * for any unknown page — and nothing is read. The server re-authorizes every read regardless.
 */

import type { RouteObject } from 'react-router'
import { CapabilityRoute } from '../shell/CapabilityRoute'
import { ApprovalInboxDetailPage, ApprovalInboxPage } from './ApprovalInboxPages'
import { WORK_APPROVALS_CAPABILITY, WORK_ROUTE } from './workText'

export const workRoutes: RouteObject[] = [
  {
    path: WORK_ROUTE.approvals.slice(1),
    element: <CapabilityRoute capability={WORK_APPROVALS_CAPABILITY}><ApprovalInboxPage /></CapabilityRoute>,
  },
  {
    path: WORK_ROUTE.approvalDetail.slice(1),
    element: <CapabilityRoute capability={WORK_APPROVALS_CAPABILITY}><ApprovalInboxDetailPage /></CapabilityRoute>,
  },
]
