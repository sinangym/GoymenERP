/**
 * UI-70b S-10 "Müşteri iade faturaları" — the customer return invoices of the committed company, pinned to
 * `GET /api/v1/sales/customer-returns[?invoiceId]` (API-40, SAT-D288). `?fatura=<invoiceId>` narrows the list to one
 * original invoice on the SERVER (`invoiceId`); the client never filters, sorts or counts rows. The server answers the
 * whole list at once (no cursor).
 *
 * The columns are what `SalesCustomerReturnSummaryView` carries. The amount is the server's text, never recomputed.
 */
import { Link, useSearchParams } from 'react-router'
import { fetchCustomerReturnPage } from './customerReturnApi'
import type { CustomerReturnSummary } from './customerReturnContract'
import {
  CUSTOMER_RETURN_INVOICE_PARAM,
  CUSTOMER_RETURN_STATE_TEXT,
  CUSTOMER_RETURN_SUBJECT,
  CUSTOMER_RETURN_TEXT,
  CUSTOMER_RETURN_TONE,
} from './customerReturnText'
import { SALES_CAPABILITY, SALES_ROUTE, salesCustomerReturnPath } from './capabilities'
import { PoolContinuation, PoolPartyCell } from './SalesPoolShared'
import { usePoolPages } from './usePoolPages'
import { formatBusinessDate, formatInstant } from './format'
import { isReferenceId } from './wirePrimitives'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { DataTable, type DataTableColumn } from '../ui/DataTable'
import { StatusIndicator } from '../ui/StatusIndicator'
import styles from './SalesPool.module.css'

const T = CUSTOMER_RETURN_TEXT.list
const C = CUSTOMER_RETURN_TEXT.common

export function SalesCustomerReturnListPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const [params] = useSearchParams()
  const raw = params.get(CUSTOMER_RETURN_INVOICE_PARAM)
  // A malformed filter is not sent: an id the server would refuse narrows nothing and is shown as no filter.
  const invoiceId = raw !== null && isReferenceId(raw) ? raw : null
  // Remounted per context and filter: rows of one company or one invoice never survive into another.
  return <CustomerReturnList key={`${namespace ?? 'no-context'}|${invoiceId ?? ''}`} namespace={namespace} invoiceId={invoiceId} />
}

function CustomerReturnList({ namespace, invoiceId }: { readonly namespace: string | null; readonly invoiceId: string | null }) {
  const shell = useShellState()
  const canOpen = hasCapability(shell, SALES_CAPABILITY.customerReturnsDetail)
  const canCreate = hasCapability(shell, SALES_CAPABILITY.customerReturnsCreate)
  const list = usePoolPages(namespace, 'sales-customer-returns', fetchCustomerReturnPage, invoiceId)

  const header = (
    <header className={styles.header}>
      <div className={styles.titleRow}>
        <h1 id="customer-returns-heading" className={styles.title}>
          {T.heading}
        </h1>
        {canCreate ? (
          <Link className={styles.primaryLink} to={SALES_ROUTE.customerReturnCreate}>
            {T.create}
          </Link>
        ) : null}
      </div>
      <p className={styles.muted}>{T.intro}</p>
      {invoiceId === null ? null : (
        <div className={styles.actions}>
          <p role="status" className={styles.muted} data-testid="customer-returns-filter">
            {T.filteredBy(invoiceId)}
          </p>
          <Link className={styles.link} to={SALES_ROUTE.customerReturns}>
            {T.clearFilter}
          </Link>
        </div>
      )}
    </header>
  )

  if (list.rows === null) {
    return (
      <section className={styles.page} aria-labelledby="customer-returns-heading">
        {header}
        <StateBlock state={list.firstState ?? { kind: 'initial-loading' }} subject={CUSTOMER_RETURN_SUBJECT.list} onRetry={list.retryFirst} />
      </section>
    )
  }
  if (list.rows.length === 0) {
    return (
      <section className={styles.page} aria-labelledby="customer-returns-heading">
        {header}
        <StateBlock state={{ kind: invoiceId === null ? 'empty-no-data' : 'empty-filtered' }} subject={CUSTOMER_RETURN_SUBJECT.list} />
      </section>
    )
  }

  const columns: DataTableColumn<CustomerReturnSummary>[] = [
    {
      key: 'customerDocumentNo',
      header: C.customerDocumentNo,
      pin: 'start',
      cell: (row) =>
        canOpen ? (
          <Link className={styles.link} to={salesCustomerReturnPath(row.id)}>
            {row.customerDocumentNo}
          </Link>
        ) : (
          row.customerDocumentNo
        ),
    },
    { key: 'customerDocumentDate', header: C.customerDocumentDate, cell: (row) => formatBusinessDate(row.customerDocumentDate) },
    { key: 'originalDocumentNo', header: C.originalInvoice, cell: (row) => row.originalDocumentNo },
    { key: 'party', header: C.party, cell: (row) => <PoolPartyCell partyId={row.partyId} /> },
    {
      key: 'state',
      header: C.state,
      cell: (row) => (
        <StatusIndicator tone={CUSTOMER_RETURN_TONE[row.state]}>
          <span data-testid={`customer-return-state-${row.id}`}>{CUSTOMER_RETURN_STATE_TEXT[row.state]}</span>
        </StatusIndicator>
      ),
    },
    {
      key: 'lineTotalAmount',
      header: C.lineTotal,
      align: 'end',
      cell: (row) => (
        <span className={styles.numeric}>
          {row.lineTotalAmount} <span className={styles.identifier}>{row.currencyId}</span>
        </span>
      ),
    },
    { key: 'shipment', header: T.shipmentColumn, cell: (row) => (row.isShipmentLinked ? C.shipmentLinked : C.shipmentNone) },
    { key: 'needsReview', header: C.needsReview, cell: (row) => (row.needsReview ? C.yes : C.no) },
    { key: 'lineCount', header: C.lineCount, align: 'end', cell: (row) => row.lineCount },
    { key: 'createdAt', header: C.createdAt, cell: (row) => formatInstant(row.createdAtUtc) },
  ]

  return (
    <section className={styles.page} aria-labelledby="customer-returns-heading">
      {header}
      <DataTable
        label={T.caption}
        columns={columns}
        rows={list.rows}
        rowKey={(row) => row.id}
        rowAttributes={(row) => ({ 'data-customer-return-id': row.id })}
      />
      <PoolContinuation list={list} />
    </section>
  )
}
