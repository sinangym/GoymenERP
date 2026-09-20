/**
 * The Accounting route table, ready for the shell owner to spread into `shell/router.tsx` as
 * `...accountingRoutes` (see ./SHARED_MOUNT_HANDOFF.md).
 *
 * Every route is wrapped in `<CapabilityRoute>`: while the server keeps the capability staged, the
 * route mounts nothing and renders the same neutral not-found block any unknown path gets. The static
 * `donemler/yeni` is declared before any parameter route. The approval and the close are controls of
 * the period list, not routes.
 */

import type { ReactNode } from 'react'
import type { RouteObject } from 'react-router'
import { CapabilityRoute } from '../shell/CapabilityRoute'
import type { CapabilityId } from '../shell/capabilities'
import { ACCOUNTING_CAPABILITY, ACCOUNTING_ROUTE, type AccountingCapabilityId } from './capabilities'
import { PeriodsPage } from './PeriodsPage'
import { PeriodProposePage } from './PeriodProposePage'
import { JournalDetailPage, JournalsPage } from './JournalPages'
import { PostingQueuesPage } from './PostingQueuesPage'

/**
 * `CapabilityRoute` types its prop as the shell's `CapabilityId` union, which does not yet contain the
 * Accounting ids — adding them is the shell owner's edit (the handoff). The cast is confined here (the
 * Manufacturing / Product pattern): it widens a compile-time union and changes NO runtime behaviour,
 * because `hasCapability` compares a plain string against the server's published list, so an
 * unpublished id still fails closed.
 */
function gate(capability: AccountingCapabilityId, children: ReactNode) {
  return <CapabilityRoute capability={capability as CapabilityId}>{children}</CapabilityRoute>
}

export const accountingRoutes: RouteObject[] = [
  { path: ACCOUNTING_ROUTE.periodsPropose.slice(1), element: gate(ACCOUNTING_CAPABILITY.periodsPropose, <PeriodProposePage />) },
  { path: ACCOUNTING_ROUTE.periodsList.slice(1), element: gate(ACCOUNTING_CAPABILITY.periodsList, <PeriodsPage />) },
  { path: ACCOUNTING_ROUTE.journalsList.slice(1), element: gate(ACCOUNTING_CAPABILITY.journalsList, <JournalsPage />) },
  {
    path: ACCOUNTING_ROUTE.journalsDetail.slice(1),
    element: gate(ACCOUNTING_CAPABILITY.journalsDetail, <JournalDetailPage />),
  },
  {
    path: ACCOUNTING_ROUTE.postingQueues.slice(1),
    element: gate(ACCOUNTING_CAPABILITY.postingQueues, <PostingQueuesPage />),
  },
]
