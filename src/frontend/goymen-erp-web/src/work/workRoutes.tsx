/**
 * D-05 "Onaylarım" routes, mounted by the shell router before the catch-all. Both open for an actor holding ANY of the
 * connected owners' own approve capabilities (Finance `finance.official-invoice-import.approve`, Sales
 * `sales.orders.approve`): the inbox adds no permission of its own, so whoever holds none sees the same neutral
 * not-found block as for any unknown page — and nothing is read. Inside, each owner's records are listed only for that
 * owner's capability. The server re-authorizes every read regardless.
 */

import type { RouteObject } from 'react-router'
import { CapabilityRoute } from '../shell/CapabilityRoute'
import { ApprovalInboxDetailPage, ApprovalInboxPage } from './ApprovalInboxPages'
import { WorkQueuePage } from './WorkQueuePage'
import { WORK_APPROVALS_CAPABILITIES, WORK_ROUTE } from './workText'

export const workRoutes: RouteObject[] = [
  // B-25: the one-list Work queue, behind the same owners' approve capabilities (declared before the detail route).
  {
    path: WORK_ROUTE.queue.slice(1),
    element: <CapabilityRoute capability={WORK_APPROVALS_CAPABILITIES}><WorkQueuePage /></CapabilityRoute>,
  },
  {
    path: WORK_ROUTE.approvals.slice(1),
    element: <CapabilityRoute capability={WORK_APPROVALS_CAPABILITIES}><ApprovalInboxPage /></CapabilityRoute>,
  },
  {
    path: WORK_ROUTE.approvalDetail.slice(1),
    element: <CapabilityRoute capability={WORK_APPROVALS_CAPABILITIES}><ApprovalInboxDetailPage /></CapabilityRoute>,
  },
]
