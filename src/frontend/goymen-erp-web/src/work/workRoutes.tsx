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
import { NotificationsPage } from './NotificationsPage'
import { MyTasksPage } from './MyTasksPage'
import { MY_TASKS_ROUTE } from './collaborationContract'
import { NOTIFICATIONS_ROUTE } from './notificationContract'
import { WORK_APPROVALS_CAPABILITIES, WORK_ROUTE } from './workText'
import { WORK_INBOX_CAPABILITIES } from './workInboxKinds'

/**
 * W1-A / WP1: the one Work inbox (`/onaylarim/kuyruk`) opens for an actor holding any inbox kind's EXISTING approve
 * capability (`WORK_INBOX_CAPABILITIES`, the native "Onaylarım" kinds included); no capability is new and the server
 * re-authorizes every source.
 */
const WORK_INBOX_ROUTE_CAPABILITIES: readonly string[] = [...new Set([...WORK_APPROVALS_CAPABILITIES, ...WORK_INBOX_CAPABILITIES])]

export const workRoutes: RouteObject[] = [
  // Personal recipient-only inbox; backend refreshes active membership and accepts no recipient override.
  { path: NOTIFICATIONS_ROUTE.slice(1), element: <NotificationsPage /> },
  // W1-C "Bana atanan": own tasks only, like the personal inbox; the server accepts no assignee override.
  { path: MY_TASKS_ROUTE.slice(1), element: <MyTasksPage /> },
  // W1-A / WP1 (D5): the one Work inbox, behind the owners' approve capabilities (declared before the detail route).
  {
    path: WORK_ROUTE.queue.slice(1),
    element: <CapabilityRoute capability={WORK_INBOX_ROUTE_CAPABILITIES}><WorkQueuePage /></CapabilityRoute>,
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
