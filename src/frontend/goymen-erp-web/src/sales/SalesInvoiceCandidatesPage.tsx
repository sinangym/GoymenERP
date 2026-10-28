/**
 * UI-61 / S-08 "Faturaya Hazır": the posted sales shipment shares (lines of approved, not cancelled dispatch notes) grouped by
 * the server along DEC-SAL-006, pinned to `GET /api/v1/sales/invoice-candidates[?companyAccountId]` (API-31, its own
 * `sales.invoices.read`). Sinan's S5 answer (2026-10-28): the default proposal is the largest compatible group per customer; the
 * user splits a group or takes partial quantities on the invoice draft (API-32, a later slice) — not here.
 *
 * Read-only and money-free: no price, amount, VAT or total (API-03 is not on main; the screen never computes money). The group,
 * its unresolved key parts and the per-unit quantity totals are the server's; nothing is regrouped, summed or filtered here.
 * A group whose key parts are not all resolved says so and is never presented as one invoice. No "Fatura Kes" (SAT-D7).
 */

import { useQuery } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router'
import { fetchInvoiceCandidates } from './dispatchApi'
import type { InvoiceCandidateGroup, InvoiceCandidateShare } from './dispatchContract'
import { DISPATCH_TEXT, GROUP_KEY_PART_TEXT } from './dispatchText'
import { SALES_ROUTE } from './capabilities'
import { DispatchCustomerCell, DispatchOrderCell, DispatchTabs } from './SalesDispatchShared'
import { formatInstant } from './format'
import { isReferenceId } from './wirePrimitives'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { DataTable, type DataTableColumn } from '../ui/DataTable'
import styles from './SalesPool.module.css'

const T = DISPATCH_TEXT.candidates
const C = DISPATCH_TEXT.common

/** The query member that narrows the candidates to one customer account (`?cari=<companyAccountId>`). */
export const INVOICE_CANDIDATE_ACCOUNT_PARAM = 'cari'

export function SalesInvoiceCandidatesPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <InvoiceCandidates key={namespace ?? 'no-context'} namespace={namespace} />
}

function InvoiceCandidates({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const [params] = useSearchParams()
  const requested = params.get(INVOICE_CANDIDATE_ACCOUNT_PARAM)
  // A malformed account in the address is never sent: the whole list is shown instead.
  const companyAccountId = requested !== null && isReferenceId(requested) ? requested : null
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null

  const read = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'sales-invoice-candidates', companyAccountId ?? 'all'],
    enabled: namespace !== null && companyId !== null,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchInvoiceCandidates(companyId as string, signal, companyAccountId)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })

  const filtered = companyAccountId !== null
  const header = (
    <header className={styles.header}>
      <div className={styles.titleRow}>
        <h1 id="sales-invoice-candidates-heading" className={styles.title}>
          {T.heading}
        </h1>
      </div>
      <DispatchTabs />
      <p className={styles.muted}>{T.intro}</p>
      <p className={styles.muted}>{C.invoicingNoSource}</p>
      <p className={styles.muted}>{C.noAmounts}</p>
      {filtered ? (
        <p>
          <Link className={styles.link} to={SALES_ROUTE.invoiceCandidates}>
            {T.allCustomers}
          </Link>
        </p>
      ) : null}
    </header>
  )

  const data = read.data?.type === 'ok-single' ? read.data.data : null
  if (data === null) {
    const state: RemoteViewState<unknown> =
      read.data === undefined ? { kind: 'initial-loading' } : (classifyOutcome(read.data) as RemoteViewState<unknown>)
    return (
      <section className={styles.page} aria-labelledby="sales-invoice-candidates-heading">
        {header}
        <StateBlock state={state} subject={T.subject} onRetry={() => void read.refetch()} />
      </section>
    )
  }

  return (
    <section className={styles.page} aria-labelledby="sales-invoice-candidates-heading">
      {header}
      {data.truncated ? (
        <p className={styles.notice} role="status" data-testid="sales-invoice-candidates-truncated">
          {T.truncated}
        </p>
      ) : null}
      {filtered && data.groups.length > 0 ? <p className={styles.muted}>{T.filteredTo(data.groups[0].customer.legalName)}</p> : null}
      {data.groups.length === 0 ? (
        <StateBlock state={{ kind: filtered ? 'empty-filtered' : 'empty-no-data' }} subject={T.subject} />
      ) : (
        data.groups.map((group, index) => <CandidateGroup key={groupKey(group)} group={group} index={index + 1} filtered={filtered} />)
      )}
    </section>
  )
}

const groupKey = (group: InvoiceCandidateGroup) =>
  [group.key.companyAccountId, group.key.currencyId, group.key.paymentTerms?.id ?? '-', group.key.paymentTerms?.version ?? '-'].join('|')

function CandidateGroup({ group, index, filtered }: { readonly group: InvoiceCandidateGroup; readonly index: number; readonly filtered: boolean }) {
  const headingId = `sales-invoice-group-${index}`
  const columns: readonly DataTableColumn<InvoiceCandidateShare>[] = [
    {
      key: 'dispatch',
      header: T.dispatch,
      pin: 'start',
      cell: (share) => `${share.dispatchSystemNumber} / ${share.dispatchLineNo}`,
    },
    { key: 'approvedAt', header: T.approvedAt, cell: (share) => formatInstant(share.approvedAtUtc) },
    {
      key: 'order',
      header: C.order,
      cell: (share) => <DispatchOrderCell orderId={share.salesOrderId} documentNo={share.salesOrderDocumentNo} lineNo={share.salesOrderLineNo} />,
    },
    { key: 'product', header: C.product, cell: (share) => `${share.productCode} ${share.productName}` },
    {
      key: 'quantity',
      header: C.quantity,
      align: 'end',
      cell: (share) => (
        <span className={styles.numeric}>
          {share.quantity} {share.unitCode}
        </span>
      ),
    },
    { key: 'shippedAt', header: C.shippedAt, cell: (share) => formatInstant(share.shippedAtUtc) },
  ]

  return (
    <section className={styles.section} aria-labelledby={headingId} data-testid={`sales-invoice-group-${index}`}>
      <h2 id={headingId} className={styles.subtitle}>
        {T.group(index)}
      </h2>
      <dl className={styles.facts}>
        <dt>{T.customer}</dt>
        <dd>
          <DispatchCustomerCell customer={group.customer} />
          {filtered ? null : (
            <>
              {' '}
              <Link
                className={styles.link}
                to={`${SALES_ROUTE.invoiceCandidates}?${new URLSearchParams({ [INVOICE_CANDIDATE_ACCOUNT_PARAM]: group.key.companyAccountId }).toString()}`}
              >
                {T.onlyCustomer}
              </Link>
            </>
          )}
        </dd>
        <dt>{T.currency}</dt>
        <dd>
          <span className={styles.identifier}>{group.key.currencyId}</span>
        </dd>
        <dt>{T.paymentTerms}</dt>
        <dd>{group.key.paymentTerms === null ? T.paymentTermsNone : <span className={styles.identifier}>{group.key.paymentTerms.id}</span>}</dd>
        <dt>{T.noteCount}</dt>
        <dd>{group.dispatchNoteCount}</dd>
        <dt>{T.totals}</dt>
        <dd>
          <ul aria-label={T.totals}>
            {group.quantityTotals.map((total) => (
              <li key={total.unitId} className={styles.numeric}>
                {total.quantity} {total.unitCode}
              </li>
            ))}
          </ul>
        </dd>
      </dl>
      {group.complete ? (
        <p className={styles.muted}>{T.complete}</p>
      ) : (
        <div className={styles.notice} role="note" data-testid={`sales-invoice-group-${index}-incomplete`}>
          <p>{T.incomplete}</p>
          <ul>
            {group.unresolvedKeyParts.map((part) => (
              <li key={part}>{GROUP_KEY_PART_TEXT[part]}</li>
            ))}
          </ul>
          <p>{T.incompleteHint}</p>
        </div>
      )}
      <DataTable
        label={T.sharesCaption(index)}
        columns={columns}
        rows={group.shares}
        rowKey={(share) => share.shipmentId}
        rowAttributes={(share) => ({ 'data-shipment-id': share.shipmentId })}
      />
    </section>
  )
}
