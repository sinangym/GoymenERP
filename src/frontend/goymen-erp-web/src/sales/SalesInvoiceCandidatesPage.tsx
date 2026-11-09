/**
 * UI-61 / S-08 "Faturaya Hazır": the posted sales shipment shares (lines of approved, not cancelled dispatch notes) grouped by
 * the server along DEC-SAL-006, pinned to `GET /api/v1/sales/invoice-candidates[?companyAccountId]` (API-31, its own
 * `sales.invoices.read`). Sinan's S5 answer (2026-10-28): the default proposal is the largest compatible group per customer; the
 * user splits a group by choosing some of its shares on the invoice draft create (UI-62 / API-32, `SalesInvoiceDraftCreate`);
 * a partial quantity is a later slice.
 *
 * Money-free: no price, amount, VAT or total (the screen never computes money). The group, its unresolved key parts and the
 * per-unit quantity totals are the server's; nothing is regrouped, summed or filtered here. A group whose key parts are not
 * all resolved says so and offers no draft. The draft create follows the `sales.invoices.write` control id (not published by
 * the shell yet, so it fails closed); the prepared draft opens below with its UI-64 controls. The official invoice is issued in
 * the external program — no "Fatura Kes" (SAT-D7).
 */

import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router'
import { fetchInvoiceCandidates } from './dispatchApi'
import type { InvoiceCandidateGroup, InvoiceCandidateShare } from './dispatchContract'
import { DISPATCH_TEXT, GROUP_KEY_PART_TEXT } from './dispatchText'
import { SALES_INVOICE_APPROVE_CAPABILITY, SALES_INVOICE_WRITE_CAPABILITY, SALES_ROUTE } from './capabilities'
import { INVOICE_DRAFT_CREATE_TEXT as CT } from './invoiceDraftText'
import type { SalesInvoiceDetail } from './invoiceFinalizationContract'
import { SalesInvoiceDraftCreate } from './SalesInvoiceDraftCreate'
import { SalesInvoiceFinalizationPanel } from './SalesInvoiceFinalization'
import { DispatchCustomerCell, DispatchOrderCell, DispatchTabs } from './SalesDispatchShared'
import { formatBusinessDate, formatInstant } from './format'
import { isReferenceId } from './wirePrimitives'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { DataTable, type DataTableColumn } from '../ui/DataTable'
import { StatusIndicator } from '../ui/StatusIndicator'
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
  const canCreate = hasCapability(shell, SALES_INVOICE_WRITE_CAPABILITY)
  const canApprove = hasCapability(shell, SALES_INVOICE_APPROVE_CAPABILITY)
  // The draft this screen prepared: its group leaves the candidates, so the draft is shown at the page level.
  const [created, setCreated] = useState<SalesInvoiceDetail | null>(null)

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

  const createdDraft =
    created === null || namespace === null || companyId === null ? null : (
      <section className={styles.section} aria-labelledby="sales-invoice-created-heading" data-testid="sales-invoice-created">
        <h2 id="sales-invoice-created-heading" className={styles.subtitle}>
          {CT.newDraftHeading}
        </h2>
        <p role="status" className={styles.notice} data-testid="sales-invoice-created-notice">
          {created.isReplay ? CT.replayed(created.documentNo) : CT.completed(created.documentNo)}
        </p>
        <SalesInvoiceFinalizationPanel
          key={`invoice:${namespace}:${shell.routeEntryGeneration}:${created.id}`}
          invoiceId={created.id}
          companyId={companyId}
          namespace={namespace}
          entryGeneration={shell.routeEntryGeneration}
          canControl={false}
          canWrite={canCreate}
          canApprove={canApprove}
          onRecorded={() => void read.refetch()}
        />
      </section>
    )

  const data = read.data?.type === 'ok-single' ? read.data.data : null
  if (data === null) {
    const state: RemoteViewState<unknown> =
      read.data === undefined ? { kind: 'initial-loading' } : (classifyOutcome(read.data) as RemoteViewState<unknown>)
    return (
      <section className={styles.page} aria-labelledby="sales-invoice-candidates-heading">
        {header}
        {createdDraft}
        <StateBlock state={state} subject={T.subject} onRetry={() => void read.refetch()} />
      </section>
    )
  }

  return (
    <section className={styles.page} aria-labelledby="sales-invoice-candidates-heading">
      {header}
      {createdDraft}
      {data.truncated ? (
        <p className={styles.notice} role="status" data-testid="sales-invoice-candidates-truncated">
          {T.truncated}
        </p>
      ) : null}
      {filtered && data.groups.length > 0 ? <p className={styles.muted}>{T.filteredTo(data.groups[0].customer.legalName)}</p> : null}
      {data.groups.length === 0 ? (
        <StateBlock state={{ kind: filtered ? 'empty-filtered' : 'empty-no-data' }} subject={T.subject} />
      ) : (
        data.groups.map((group, index) => (
          <CandidateGroup
            key={groupKey(group)}
            group={group}
            index={index + 1}
            filtered={filtered}
            canCreate={canCreate}
            onCreated={(detail) => {
              setCreated(detail)
              void read.refetch()
            }}
            onConflict={() => void read.refetch()}
          />
        ))
      )}
    </section>
  )
}

const groupKey = (group: InvoiceCandidateGroup) =>
  [
    group.key.companyAccountId,
    group.key.currencyId,
    group.key.exchangeRate.state,
    group.key.exchangeRate.shipmentDate ?? group.key.exchangeRate.bulletinDate ?? '-',
    group.key.exchangeRate.rate ?? '-',
    group.key.dueDate ?? '-',
    group.key.invoiceType ?? '-',
  ].join('|')

function rateText(rate: InvoiceCandidateGroup['key']['exchangeRate']): string {
  if (rate.state === 'NotApplicable') return T.rateNotApplicable
  if (rate.state === 'Resolved') return T.rateResolved(formatBusinessDate(rate.bulletinDate ?? ''), rate.rate ?? '')
  return T.rateUnresolved(formatBusinessDate(rate.shipmentDate ?? ''))
}

function CandidateGroup({
  group,
  index,
  filtered,
  canCreate,
  onCreated,
  onConflict,
}: {
  readonly group: InvoiceCandidateGroup
  readonly index: number
  readonly filtered: boolean
  readonly canCreate: boolean
  readonly onCreated: (detail: SalesInvoiceDetail) => void
  readonly onConflict: () => void
}) {
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
      // SAT-D107: the official e-İrsaliye number is entered later; until then a warning, never a block.
      key: 'eDispatchNumber',
      header: T.eDispatchNumber,
      cell: (share) =>
        share.eDispatchNumber ?? (
          <StatusIndicator tone="warning">
            <span data-testid={`sales-invoice-share-${share.shipmentId}-e-dispatch-missing`}>{T.eDispatchNumberMissing}</span>
          </StatusIndicator>
        ),
    },
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
        <dt>{T.exchangeRate}</dt>
        <dd>{rateText(group.key.exchangeRate)}</dd>
        <dt>{T.dueDate}</dt>
        <dd>{group.key.dueDate === null ? T.sourceMissing : formatBusinessDate(group.key.dueDate)}</dd>
        <dt>{T.invoiceType}</dt>
        <dd>{group.key.invoiceType ?? T.sourceMissing}</dd>
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
      {group.invoiceable ? (
        <p className={styles.muted}>{T.invoiceable}</p>
      ) : (
        <div className={styles.notice} role="note" data-testid={`sales-invoice-group-${index}-incomplete`}>
          <p>
            <strong>{T.notInvoiceable}</strong>
          </p>
          {group.dueDateMissing ? <p data-testid={`sales-invoice-group-${index}-due-date-missing`}>{T.dueDateMissing}</p> : null}
          {group.unresolvedKeyParts.length > 0 ? (
            <>
              <p>{T.incomplete}</p>
              <ul aria-label={T.incomplete}>
                {group.unresolvedKeyParts.map((part) => (
                  <li key={part}>{GROUP_KEY_PART_TEXT[part]}</li>
                ))}
              </ul>
              <p>{T.incompleteHint}</p>
            </>
          ) : null}
        </div>
      )}
      <DataTable
        label={T.sharesCaption(index)}
        columns={columns}
        rows={group.shares}
        rowKey={(share) => share.shipmentId}
        rowAttributes={(share) => ({ 'data-shipment-id': share.shipmentId })}
      />
      {!canCreate ? (
        group.invoiceable ? (
          <p className={styles.muted} data-testid={`sales-invoice-group-${index}-draft-unavailable`}>
            {CT.unavailable}
          </p>
        ) : null
      ) : group.invoiceable ? (
        <SalesInvoiceDraftCreate group={group} groupIdentity={groupKey(group)} index={index} onCreated={onCreated} onConflict={onConflict} />
      ) : (
        <p className={styles.muted} data-testid={`sales-invoice-group-${index}-draft-blocked`}>
          {CT.notInvoiceableHint}
        </p>
      )}
    </section>
  )
}
