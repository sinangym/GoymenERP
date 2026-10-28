/**
 * UI-60 / S-07 "Sevkiyat / İrsaliyeler": the company's sales-direction dispatch notes, pinned to
 * `GET /api/v1/sales/dispatches?pageSize&cursor&state` (API-30, `sales.drafts.read`). Newest first, as the server orders; the
 * state filter is the server's; paging follows the owner's cursor and never sorts, filters or counts on the client.
 *
 * Read-only (AT-025: purchase receipts and transfer documents never appear here). Each note shows its lines — the Sales order
 * line, product and the exactly recorded shipped quantity in the order unit — and no price or amount. The invoicing state is
 * "not tracked yet" (no invoice owner), never a guessed "faturalanmadı".
 */

import { useState } from 'react'
import { fetchDispatchPage } from './dispatchApi'
import { DISPATCH_STATE, type DispatchState, type DispatchSummary } from './dispatchContract'
import { DISPATCH_STATE_TEXT, DISPATCH_TEXT } from './dispatchText'
import { DispatchCustomerCell, DispatchOrderCell, DispatchTabs } from './SalesDispatchShared'
import { PoolContinuation } from './SalesPoolShared'
import { usePoolPages } from './usePoolPages'
import { formatInstant } from './format'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { DataTable, type DataTableColumn } from '../ui/DataTable'
import { Field } from '../ui/Field'
import { StatusIndicator } from '../ui/StatusIndicator'
import styles from './SalesPool.module.css'

const T = DISPATCH_TEXT.dispatches
const C = DISPATCH_TEXT.common

const STATE_TONE = { Draft: 'neutral', Approved: 'success', Cancelled: 'danger' } as const

export function SalesDispatchListPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <DispatchList key={namespace ?? 'no-context'} namespace={namespace} />
}

function DispatchList({ namespace }: { readonly namespace: string | null }) {
  const [filter, setFilter] = useState<DispatchState | null>(null)
  const list = usePoolPages(namespace, 'sales-dispatches', fetchDispatchPage, filter)

  const header = (
    <header className={styles.header}>
      <div className={styles.titleRow}>
        <h1 id="sales-dispatches-heading" className={styles.title}>
          {T.heading}
        </h1>
      </div>
      <DispatchTabs />
      <p className={styles.muted}>{T.intro}</p>
      <p className={styles.muted}>{C.invoicingNoSource}</p>
      <div className={styles.toolbar}>
        <Field id="sales-dispatch-filter" label={T.filter} className={styles.filter}>
          {(control) => (
            <select
              {...control}
              className={styles.control}
              value={filter ?? ''}
              onChange={(event) => {
                const value = event.target.value
                list.resetContinuation()
                setFilter(value === '' ? null : (value as DispatchState))
              }}
            >
              <option value="">{C.filterAll}</option>
              {Object.values(DISPATCH_STATE).map((state) => (
                <option key={state} value={state}>
                  {DISPATCH_STATE_TEXT[state]}
                </option>
              ))}
            </select>
          )}
        </Field>
      </div>
    </header>
  )

  if (list.rows === null) {
    return (
      <section className={styles.page} aria-labelledby="sales-dispatches-heading">
        {header}
        <StateBlock state={list.firstState ?? { kind: 'initial-loading' }} subject={T.subject} onRetry={list.retryFirst} />
      </section>
    )
  }
  if (list.rows.length === 0) {
    return (
      <section className={styles.page} aria-labelledby="sales-dispatches-heading">
        {header}
        <StateBlock state={{ kind: filter === null ? 'empty-no-data' : 'empty-filtered' }} subject={T.subject} />
      </section>
    )
  }

  const columns: readonly DataTableColumn<DispatchSummary>[] = [
    {
      key: 'number',
      header: T.number,
      pin: 'start',
      cell: (row) => row.systemNumber ?? <span className={styles.muted}>{T.draftNumber}</span>,
    },
    {
      key: 'state',
      header: T.state,
      cell: (row) => (
        <StatusIndicator tone={STATE_TONE[row.state]}>
          <span data-testid={`sales-dispatch-state-${row.id}`}>{DISPATCH_STATE_TEXT[row.state]}</span>
        </StatusIndicator>
      ),
    },
    { key: 'eDispatchNumber', header: T.eDispatchNumber, cell: (row) => row.eDispatchNumber ?? C.none },
    { key: 'customer', header: C.customer, cell: (row) => <DispatchCustomerCell customer={row.customer} /> },
    {
      key: 'lines',
      header: T.lines,
      cell: (row) => (
        <ul className={styles.facts} aria-label={`${T.lines}: ${row.systemNumber ?? T.draftNumber}`}>
          {row.lines.map((line) => (
            <li key={line.lineNo}>
              <DispatchOrderCell orderId={line.salesOrderId} documentNo={line.salesOrderDocumentNo} lineNo={line.salesOrderLineNo} />{' '}
              — {line.productCode} {line.productName} —{' '}
              <span className={styles.numeric}>
                {line.quantity} {line.unitCode}
              </span>{' '}
              <span className={styles.muted}>({formatInstant(line.shippedAtUtc)})</span>
            </li>
          ))}
        </ul>
      ),
    },
    { key: 'createdAt', header: T.createdAt, cell: (row) => formatInstant(row.createdAtUtc) },
    { key: 'approvedAt', header: T.approvedAt, cell: (row) => (row.approvedAtUtc === null ? C.none : formatInstant(row.approvedAtUtc)) },
  ]

  return (
    <section className={styles.page} aria-labelledby="sales-dispatches-heading">
      {header}
      <DataTable label={T.caption} columns={columns} rows={list.rows} rowKey={(row) => row.id} rowAttributes={(row) => ({ 'data-dispatch-id': row.id })} />
      <PoolContinuation list={list} />
    </section>
  )
}
