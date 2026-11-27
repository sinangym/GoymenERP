/**
 * D-05 "Onaylarım" routes, mounted by the shell router before the catch-all. Both open for an actor holding ANY of the
 * connected owners' own approve capabilities (Finance `finance.official-invoice-import.approve`, Sales
 * `sales.orders.approve`): the inbox adds no permission of its own, so whoever holds none sees the same neutral
 * not-found block as for any unknown page — and nothing is read. Inside, each owner's records are listed only for that
 * owner's capability. The server re-authorizes every read regardless.
 */

import { WorkManagementPage, WORK_MANAGEMENT_CAPABILITIES, WORK_MANAGEMENT_ROUTE } from './WorkManagementPage'
import type { RouteObject } from 'react-router'
import { WorkDecisionPage } from './WorkDecisionPage'
import { WORK_DECISION_KINDS } from './workDecisionApi'
import { CapabilityRoute } from '../shell/CapabilityRoute'
import { ApprovalInboxDetailPage, ApprovalInboxPage } from './ApprovalInboxPages'
import { WorkQueuePage } from './WorkQueuePage'
import { NotificationsPage } from './NotificationsPage'
import { MyTasksPage } from './MyTasksPage'
import { AssistantMemoryPage } from './AssistantMemoryPage'
import { ASSISTANT_MEMORY_ROUTE } from './assistantMemoryContract'
import { MY_TASKS_ROUTE } from './collaborationContract'
import { NOTIFICATIONS_ROUTE } from './notificationContract'
import { WORK_APPROVALS_CAPABILITIES, WORK_ROUTE } from './workText'
import { WORK_INBOX_CAPABILITIES } from './workInboxKinds'
import { WORK_APPROVAL_FOLLOW_UP_CAPABILITIES, WORK_MY_TASKS_CAPABILITIES } from './capabilities'
import { ApprovalFollowUpPage } from './ApprovalFollowUpPage'
import { APPROVAL_FOLLOW_UP_ROUTE } from './approvalFollowUpContract'

/**
 * W1-A / WP1: the one Work inbox (`/onaylarim/kuyruk`) opens for an actor holding any inbox kind's EXISTING approve
 * capability (`WORK_INBOX_CAPABILITIES`, the native "Onaylarım" kinds included); no capability is new and the server
 * re-authorizes every source.
 */
const WORK_INBOX_ROUTE_CAPABILITIES: readonly string[] = [...new Set([...WORK_APPROVALS_CAPABILITIES, ...WORK_INBOX_CAPABILITIES])]

export const workRoutes: RouteObject[] = [
  { path: WORK_MANAGEMENT_ROUTE.slice(1), element: <CapabilityRoute capability={WORK_MANAGEMENT_CAPABILITIES}><WorkManagementPage /></CapabilityRoute> },
  ...WORK_DECISION_KINDS.map(kind => ({ path: `onaylarim/${kind}/:approvalId`,
    element: <CapabilityRoute capability={[kind === 'manufacturing.plan.v1' ? 'manufacturing.plans.work-decision' : kind === 'purchasing.incoming-invoice-variance.v1' ? 'purchasing.invoice-variance.work-decision' : kind.replace('.v1', '.work-decision')]}><WorkDecisionPage kind={kind} /></CapabilityRoute> })),
  // Personal recipient-only inbox; backend refreshes active membership and accepts no recipient override.
  { path: NOTIFICATIONS_ROUTE.slice(1), element: <NotificationsPage /> },
  // W1-C "Bana atanan": own tasks only; the server accepts no assignee override. WP9: behind Work's own `work.tasks.mine`
  // capability (work.tasks.complete), so an actor without it sees the neutral not-found block and nothing is read.
  {
    path: MY_TASKS_ROUTE.slice(1),
    element: <CapabilityRoute capability={WORK_MY_TASKS_CAPABILITIES}><MyTasksPage /></CapabilityRoute>,
  },
  // WP2 "Onay takibi": escalated and orphaned approvals, behind Work's own `work.approvals.follow-up` capability.
  {
    path: APPROVAL_FOLLOW_UP_ROUTE.slice(1),
    element: <CapabilityRoute capability={WORK_APPROVAL_FOLLOW_UP_CAPABILITIES}><ApprovalFollowUpPage /></CapabilityRoute>,
  },
  // WP-C6 "Asistan hafızam": own memory only (see, edit, delete); the server accepts no owner override and keeps writing
  // closed while the pilot, retention, entry limit or information notice is undecided.
  { path: ASSISTANT_MEMORY_ROUTE.slice(1), element: <AssistantMemoryPage /> },
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
