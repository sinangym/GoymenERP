/**
 * The Finance route table, owned by the Finance lane, for the shell router's single writer to
 * spread in — never written into `src/shell/**` from here (SHARED_MOUNT_HANDOFF.md).
 *
 * Each route is behind the shared `CapabilityRoute` gate with the backend provider's id, and inside
 * `FinancePortProvider` with the HTTP adapter. The declarations are PUBLISHED since FIN-UI-1c (after
 * the FIN-01 browser acceptance); a route still opens only for an actor holding its own Finance
 * permission, and otherwise closes onto the neutral not-found block and calls nothing.
 */

import type { ReactNode } from 'react'
import type { RouteObject } from 'react-router'
import { CapabilityRoute } from '../shell/CapabilityRoute'
import type { CapabilityId } from '../shell/capabilities'
import { BankAccountProposePage } from './BankAccountProposePage'
import { BankAccountsPage } from './BankAccountsPage'
import { FINANCE_CAPABILITY, FINANCE_ROUTE, type FinanceCapabilityId } from './capabilities'
import { FinancePortProvider } from './financePort'
import { createHttpFinancePort } from './httpPort'
import { ReceiptDetailPage, ReceiptDraftPage, ReceiptListPage } from './ReceiptPages'

const port = createHttpFinancePort()

/**
 * `CapabilityRoute` types its prop as the shell's `CapabilityId` union, which holds the Finance ids
 * only once the shell owner adds them. The cast widens a compile-time union only: `hasCapability`
 * compares plain strings against the server's list, so an unpublished id still fails closed.
 */
function gate(capability: FinanceCapabilityId, children: ReactNode) {
  return (
    <CapabilityRoute capability={capability as unknown as CapabilityId}>
      <FinancePortProvider port={port}>{children}</FinancePortProvider>
    </CapabilityRoute>
  )
}

export const financeRoutes: RouteObject[] = [
  { path: FINANCE_ROUTE.receiptsList.slice(1), element: gate(FINANCE_CAPABILITY.receiptsList, <ReceiptListPage />) },
  // Static `yeni` before `:sourceId`, so it is never read as a source id.
  { path: FINANCE_ROUTE.receiptsDraft.slice(1), element: gate(FINANCE_CAPABILITY.receiptsDraft, <ReceiptDraftPage />) },
  { path: FINANCE_ROUTE.receiptsDetail.slice(1), element: gate(FINANCE_CAPABILITY.receiptsDetail, <ReceiptDetailPage />) },
  { path: FINANCE_ROUTE.bankAccountsList.slice(1), element: gate(FINANCE_CAPABILITY.bankAccountsList, <BankAccountsPage />) },
  {
    path: FINANCE_ROUTE.bankAccountsPropose.slice(1),
    element: gate(FINANCE_CAPABILITY.bankAccountsPropose, <BankAccountProposePage />),
  },
]
