/**
 * S-01 Talep/Teklif Havuzu — "Talepler": the sales requests of the committed company, pinned to
 * `GET /api/v1/sales/requests?pageSize&cursor&state` (API-20, #824). Newest first, as the server orders.
 *
 * The columns are what `SalesRequestSummaryView` carries and nothing more. The state filter is the server's exact
 * `state`. Rows of ONE customer can be selected and taken into a new quote (No 163: a quote covers several requests of the
 * same customer); a rejected request is not selectable (the server refuses it as well). The selection is a check of the
 * chosen rows, never a filter of the page.
 */

import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { fetchRequestPage } from './poolApi'
import { REQUEST_STATE, type RequestState, type SalesRequestSummary } from './poolContract'
import { POOL_TEXT, REQUEST_STATE_TEXT } from './poolText'
import { SALES_POOL_ROUTE, quoteCreatePathFor, salesRequestScreenPath, useSalesPoolCapability } from './poolCapabilities'
import { PoolContinuation, PoolPartyCell, PoolTabs } from './SalesPoolShared'
import { usePoolPages } from './usePoolPages'
import { formatBusinessDate, formatInstant } from './format'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { Button } from '../ui/Button'
import { DataTable, type DataTableColumn } from '../ui/DataTable'
import { Field } from '../ui/Field'
import { StatusIndicator } from '../ui/StatusIndicator'
import { REQUEST_TONE } from './poolView'
import styles from './SalesPool.module.css'

const T = POOL_TEXT.requestList
const C = POOL_TEXT.common

export function SalesRequestListPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  // Remounted per context: rows, cursors and the selection of one company never survive into another.
  return <RequestList key={namespace ?? 'no-context'} namespace={namespace} />
}

function RequestList({ namespace }: { readonly namespace: string | null }) {
  const navigate = useNavigate()
  const canOpen = useSalesPoolCapability('requestsDetail')
  const canCreate = useSalesPoolCapability('requestsCreate')
  const canQuote = useSalesPoolCapability('quotesCreate')
  const [filter, setFilter] = useState<RequestState | null>(null)
  const [selected, setSelected] = useState<ReadonlyMap<string, SalesRequestSummary>>(new Map())
  const list = usePoolPages(namespace, 'sales-requests', fetchRequestPage, filter)

  const chosen = [...selected.values()]
  const parties = new Set(chosen.map((row) => `${row.partyId}|${row.companyAccountId}`))
  const mixed = parties.size > 1

  const header = (
    <header className={styles.header}>
      <div className={styles.titleRow}>
        <h1 id="pool-requests-heading" className={styles.title}>
          {T.heading}
        </h1>
        {canCreate ? (
          <Link className={styles.primaryLink} to={SALES_POOL_ROUTE.requestCreate}>
            {T.create}
          </Link>
        ) : null}
      </div>
      <PoolTabs />
      <div className={styles.toolbar}>
        <Field id="pool-request-filter" label={T.filter} className={styles.filter}>
          {(control) => (
            <select
              {...control}
              className={styles.control}
              value={filter ?? ''}
              onChange={(event) => {
                const value = event.target.value
                setSelected(new Map())
                list.resetContinuation()
                setFilter(value === '' ? null : (value as RequestState))
              }}
            >
              <option value="">{C.filterAll}</option>
              {Object.values(REQUEST_STATE).map((state) => (
                <option key={state} value={state}>
                  {REQUEST_STATE_TEXT[state]}
                </option>
              ))}
            </select>
          )}
        </Field>
        {canQuote ? (
          <div className={styles.actions}>
            <Button
              type="button"
              variant="primary"
              disabled={chosen.length === 0 || mixed}
              onClick={() => navigate(quoteCreatePathFor(chosen.map((row) => row.id)))}
            >
              {T.quoteFromSelected}
            </Button>
            {chosen.length === 0 ? null : (
              <p role="status" className={styles.muted} data-testid="pool-request-selection">
                {mixed ? T.mixedParties : T.selectedCount(chosen.length)}
              </p>
            )}
          </div>
        ) : null}
      </div>
    </header>
  )

  if (list.rows === null) {
    return (
      <section className={styles.page} aria-labelledby="pool-requests-heading">
        {header}
        <StateBlock state={list.firstState ?? { kind: 'initial-loading' }} subject="satış talepleri" onRetry={list.retryFirst} />
      </section>
    )
  }
  if (list.rows.length === 0) {
    return (
      <section className={styles.page} aria-labelledby="pool-requests-heading">
        {header}
        <StateBlock state={{ kind: filter === null ? 'empty-no-data' : 'empty-filtered' }} subject="satış talepleri" />
      </section>
    )
  }

  const toggle = (row: SalesRequestSummary, on: boolean) =>
    setSelected((current) => {
      const next = new Map(current)
      if (on) next.set(row.id, row)
      else next.delete(row.id)
      return next
    })

  const columns: DataTableColumn<SalesRequestSummary>[] = [
    {
      key: 'documentNo',
      header: C.documentNo,
      pin: 'start',
      cell: (row) => (canOpen ? <Link className={styles.link} to={salesRequestScreenPath(row.id)}>{row.documentNo}</Link> : row.documentNo),
    },
    { key: 'party', header: C.party, cell: (row) => <PoolPartyCell partyId={row.partyId} /> },
    { key: 'businessDate', header: C.businessDate, cell: (row) => formatBusinessDate(row.businessDate) },
    {
      key: 'state',
      header: C.state,
      cell: (row) => (
        <StatusIndicator tone={REQUEST_TONE[row.state]}>
          <span data-testid={`pool-request-state-${row.id}`}>{REQUEST_STATE_TEXT[row.state]}</span>
        </StatusIndicator>
      ),
    },
    { key: 'lineCount', header: C.lineCount, cell: (row) => row.lineCount, align: 'end' },
    { key: 'createdAt', header: C.createdAt, cell: (row) => formatInstant(row.createdAtUtc) },
  ]
  if (canQuote) {
    columns.push({
      key: 'select',
      header: T.selectColumn,
      pin: 'end',
      cell: (row) => (
        <input
          type="checkbox"
          aria-label={T.select(row.documentNo)}
          checked={selected.has(row.id)}
          disabled={row.state === REQUEST_STATE.rejected}
          onChange={(event) => toggle(row, event.target.checked)}
        />
      ),
    })
  }

  return (
    <section className={styles.page} aria-labelledby="pool-requests-heading">
      {header}
      <DataTable
        label={T.caption}
        columns={columns}
        rows={list.rows}
        rowKey={(row) => row.id}
        rowAttributes={(row) => ({ 'data-request-id': row.id })}
      />
      <PoolContinuation list={list} />
    </section>
  )
}
