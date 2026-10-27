/**
 * S-01 Talep/Teklif Havuzu — "Teklifler": the sales quotes of the committed company, pinned to
 * `GET /api/v1/sales/quotes?pageSize&cursor&state` (API-22, #828). Newest first, as the server orders.
 *
 * Only what `SalesQuoteSummaryView` carries: the lifecycle (Açık / Siparişe dönüştü / Kaybedildi), the commercial revision
 * and the validity date as stored — the validity is information, not an expiry this screen enforces (no expiry policy is
 * decided). A converted quote names its one order (No 162), linked to the order detail only with that screen's own
 * capability.
 */

import { useState } from 'react'
import { Link } from 'react-router'
import { fetchQuotePage } from './poolApi'
import { QUOTE_STATE, type QuoteState, type SalesQuoteSummary } from './poolContract'
import { POOL_TEXT, QUOTE_STATE_TEXT } from './poolText'
import { SALES_POOL_ROUTE, salesQuoteScreenPath, useSalesPoolCapability } from './poolCapabilities'
import { SALES_CAPABILITY, salesDetailPath } from './capabilities'
import { PoolContinuation, PoolPartyCell, PoolTabs } from './SalesPoolShared'
import { usePoolPages } from './usePoolPages'
import { formatBusinessDate, formatInstant } from './format'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { DataTable, type DataTableColumn } from '../ui/DataTable'
import { Field } from '../ui/Field'
import { StatusIndicator } from '../ui/StatusIndicator'
import { QUOTE_TONE } from './poolView'
import styles from './SalesPool.module.css'

const T = POOL_TEXT.quoteList
const C = POOL_TEXT.common

export function SalesQuoteListPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <QuoteList key={namespace ?? 'no-context'} namespace={namespace} />
}

function QuoteList({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const canOpen = useSalesPoolCapability('quotesDetail')
  const canCreate = useSalesPoolCapability('quotesCreate')
  const canOpenOrder = hasCapability(shell, SALES_CAPABILITY.draftsDetail)
  const [filter, setFilter] = useState<QuoteState | null>(null)
  const list = usePoolPages(namespace, 'sales-quotes', fetchQuotePage, filter)

  const header = (
    <header className={styles.header}>
      <div className={styles.titleRow}>
        <h1 id="pool-quotes-heading" className={styles.title}>
          {T.heading}
        </h1>
        {canCreate ? (
          <Link className={styles.primaryLink} to={SALES_POOL_ROUTE.quoteCreate}>
            {T.create}
          </Link>
        ) : null}
      </div>
      <PoolTabs />
      <div className={styles.toolbar}>
        <Field id="pool-quote-filter" label={T.filter} className={styles.filter}>
          {(control) => (
            <select
              {...control}
              className={styles.control}
              value={filter ?? ''}
              onChange={(event) => {
                const value = event.target.value
                list.resetContinuation()
                setFilter(value === '' ? null : (value as QuoteState))
              }}
            >
              <option value="">{C.filterAll}</option>
              {Object.values(QUOTE_STATE).map((state) => (
                <option key={state} value={state}>
                  {QUOTE_STATE_TEXT[state]}
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
      <section className={styles.page} aria-labelledby="pool-quotes-heading">
        {header}
        <StateBlock state={list.firstState ?? { kind: 'initial-loading' }} subject="satış teklifleri" onRetry={list.retryFirst} />
      </section>
    )
  }
  if (list.rows.length === 0) {
    return (
      <section className={styles.page} aria-labelledby="pool-quotes-heading">
        {header}
        <StateBlock state={{ kind: filter === null ? 'empty-no-data' : 'empty-filtered' }} subject="satış teklifleri" />
      </section>
    )
  }

  const orderCell = (row: SalesQuoteSummary) => {
    if (row.convertedDraftId === null) return C.none
    return canOpenOrder ? (
      <Link className={styles.link} to={salesDetailPath(row.convertedDraftId)}>
        {T.openOrder}
      </Link>
    ) : (
      T.orderCreated
    )
  }

  const columns: readonly DataTableColumn<SalesQuoteSummary>[] = [
    {
      key: 'documentNo',
      header: C.documentNo,
      pin: 'start',
      cell: (row) => (canOpen ? <Link className={styles.link} to={salesQuoteScreenPath(row.id)}>{row.documentNo}</Link> : row.documentNo),
    },
    { key: 'revision', header: T.revision, cell: (row) => row.revision },
    {
      key: 'state',
      header: C.state,
      cell: (row) => (
        <StatusIndicator tone={QUOTE_TONE[row.state]}>
          <span data-testid={`pool-quote-state-${row.id}`}>{QUOTE_STATE_TEXT[row.state]}</span>
        </StatusIndicator>
      ),
    },
    { key: 'party', header: C.party, cell: (row) => <PoolPartyCell partyId={row.partyId} /> },
    { key: 'businessDate', header: C.businessDate, cell: (row) => formatBusinessDate(row.businessDate) },
    { key: 'validUntil', header: T.validUntil, cell: (row) => (row.validUntil === null ? C.unknown : formatBusinessDate(row.validUntil)) },
    { key: 'lineCount', header: C.lineCount, cell: (row) => row.lineCount, align: 'end' },
    { key: 'createdAt', header: C.createdAt, cell: (row) => formatInstant(row.createdAtUtc) },
    { key: 'order', header: T.order, cell: orderCell, pin: 'end' },
  ]

  return (
    <section className={styles.page} aria-labelledby="pool-quotes-heading">
      {header}
      <DataTable label={T.caption} columns={columns} rows={list.rows} rowKey={(row) => row.id} rowAttributes={(row) => ({ 'data-quote-id': row.id })} />
      <PoolContinuation list={list} />
    </section>
  )
}
