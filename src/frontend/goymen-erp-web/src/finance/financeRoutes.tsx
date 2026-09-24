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
import { useNavigate, useParams, useSearchParams, type RouteObject } from 'react-router'
import { CapabilityRoute } from '../shell/CapabilityRoute'
import type { CapabilityId } from '../shell/capabilities'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { hasCapability } from '../shell/shellState'
import { BankAccountProposePage } from './BankAccountProposePage'
import { BankAccountsPage } from './BankAccountsPage'
import {
  FINANCE_CAPABILITY,
  FINANCE_ROUTE,
  manualBankPaymentDetailPath,
  type FinanceCapabilityId,
} from './capabilities'
import { isBoundUuid, isPositiveInt64Text } from './contract'
import { FinancePortProvider } from './financePort'
import { createHttpFinancePort } from './httpPort'
import { ReceiptDetailPage, ReceiptDraftPage, ReceiptListPage } from './ReceiptPages'
import { ReversalApprovalsPage } from './ReversalApprovalsPage'
import { ManualBankPaymentStartPage, type ManualBankPaymentEntryContext } from './ManualBankPaymentStartPage'
import {
  ManualBankPaymentCreatePage,
  ManualBankPaymentDetailPage,
  ManualBankPaymentListPage,
} from './ManualBankPaymentPages'

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

function usePaymentContext() {
  const context = useShellState().activeContext
  if (context?.scopeMode !== 'single' || context.companyId === null) return null
  return { ...context, companyId: context.companyId }
}

function ManualBankPaymentListRoute() {
  const context = usePaymentContext()
  const shell = useShellState()
  const navigate = useNavigate()
  return context === null
    ? <StateBlock state={{ kind: 'not-found' }} subject="giden ödeme listesi" />
    : <ManualBankPaymentListPage companyId={context.companyId}
        onCreate={hasCapability(shell, FINANCE_CAPABILITY.manualBankPaymentsCreate)
          ? () => navigate(FINANCE_ROUTE.manualBankPaymentsCreate)
          : undefined}
        onOpen={(paymentId) => navigate(manualBankPaymentDetailPath(paymentId))} />
}

function ManualBankPaymentCreateRoute() {
  const context = usePaymentContext()
  const navigate = useNavigate()
  const [query] = useSearchParams()
  const one = (name: string) => query.getAll(name).length === 1 ? query.get(name) : null
  const partyId = one('partyId')
  const companyAccountId = one('companyAccountId')
  const treasuryAccountId = one('treasuryAccountId')
  const currencyId = one('currencyId')
  const profileId = one('profileId')
  const profileVersion = one('profileVersion')
  if (context === null) {
    return <StateBlock state={{ kind: 'not-found' }} subject="giden ödeme başlangıç bağlamı" />
  }
  const names = ['partyId', 'companyAccountId', 'treasuryAccountId', 'currencyId', 'profileId', 'profileVersion']
  const hasEntryQuery = names.some((name) => query.has(name))
  if (!hasEntryQuery) {
    const continueWith = (entry: ManualBankPaymentEntryContext) => {
      const params = new URLSearchParams({
        partyId: entry.partyId,
        companyAccountId: entry.companyAccountId,
        treasuryAccountId: entry.treasuryAccountId,
        currencyId: entry.currencyId,
        profileId: entry.profileId,
        profileVersion: entry.profileVersion,
      })
      navigate(`${FINANCE_ROUTE.manualBankPaymentsCreate}?${params.toString()}`)
    }
    return <ManualBankPaymentStartPage companyId={context.companyId} onContinue={continueWith} />
  }
  if (!isBoundUuid(partyId) || !isBoundUuid(companyAccountId)
    || !isBoundUuid(treasuryAccountId) || !isBoundUuid(currencyId) || !isBoundUuid(profileId)
    || !isPositiveInt64Text(profileVersion)) {
    return <StateBlock state={{ kind: 'not-found' }} subject="giden ödeme başlangıç bağlamı" />
  }
  return <ManualBankPaymentCreatePage companyId={context.companyId}
    entry={{ partyId, companyAccountId, treasuryAccountId, currencyId,
      profile: { profileId, version: profileVersion } }}
    onOpen={(paymentId) => navigate(manualBankPaymentDetailPath(paymentId))} />
}

function ManualBankPaymentDetailRoute() {
  const shell = useShellState()
  const context = usePaymentContext()
  const navigate = useNavigate()
  const { paymentId } = useParams<{ paymentId: string }>()
  if (context === null || !isBoundUuid(paymentId)) {
    return <StateBlock state={{ kind: 'not-found' }} subject="giden ödeme" />
  }
  return <ManualBankPaymentDetailPage companyId={context.companyId} actorId={context.userId}
    paymentId={paymentId}
    canApprove={hasCapability(shell, FINANCE_CAPABILITY.manualBankPaymentsApprove)}
    canRecord={hasCapability(shell, FINANCE_CAPABILITY.manualBankPaymentsRecord)}
    onOpen={(id) => navigate(manualBankPaymentDetailPath(id))} />
}

export const financeRoutes: RouteObject[] = [
  { path: FINANCE_ROUTE.receiptsList.slice(1), element: gate(FINANCE_CAPABILITY.receiptsList, <ReceiptListPage />) },
  // Static `yeni` before `:sourceId`, so it is never read as a source id.
  { path: FINANCE_ROUTE.receiptsDraft.slice(1), element: gate(FINANCE_CAPABILITY.receiptsDraft, <ReceiptDraftPage />) },
  { path: FINANCE_ROUTE.receiptsDetail.slice(1), element: gate(FINANCE_CAPABILITY.receiptsDetail, <ReceiptDetailPage />) },
  {
    path: FINANCE_ROUTE.reversalApprovals.slice(1),
    element: gate(FINANCE_CAPABILITY.receiptsReverseApprove, <ReversalApprovalsPage />),
  },
  { path: FINANCE_ROUTE.bankAccountsList.slice(1), element: gate(FINANCE_CAPABILITY.bankAccountsList, <BankAccountsPage />) },
  {
    path: FINANCE_ROUTE.bankAccountsPropose.slice(1),
    element: gate(FINANCE_CAPABILITY.bankAccountsPropose, <BankAccountProposePage />),
  },
  {
    path: FINANCE_ROUTE.manualBankPaymentsList.slice(1),
    element: gate(FINANCE_CAPABILITY.manualBankPaymentsList, <ManualBankPaymentListRoute />),
  },
  // Static create before the parameter route: `yeni` is never a payment id.
  {
    path: FINANCE_ROUTE.manualBankPaymentsCreate.slice(1),
    element: gate(FINANCE_CAPABILITY.manualBankPaymentsCreate, <ManualBankPaymentCreateRoute />),
  },
  {
    path: FINANCE_ROUTE.manualBankPaymentsDetail.slice(1),
    element: gate(FINANCE_CAPABILITY.manualBankPaymentsDetail, <ManualBankPaymentDetailRoute />),
  },
]
