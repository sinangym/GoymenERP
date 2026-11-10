/**
 * UI-65 "Kontrol Merkezi" (Satış hazırlık planı v1 §3.3 API-37; SAT-D95, SAT-D86, SAT-D99, SAT-D208, SAT-D229): the read-only
 * list of sales follow-ups that need a person, pinned to `GET /api/v1/sales/control-center` (its own `sales.invoices.read`).
 *
 * Mount: the backend publishes no own shell id for this screen yet, so the route stands on the S-08 id
 * (`sales.invoice-candidates.list`), which the shell publishes only beside the very same `sales.invoices.read` the endpoint
 * measures. No capability, permission or grant is introduced. The API re-measures every call; a 403 stays its own state.
 *
 * Every row, its severity, its age in days and the grace days are the server's. The kind filter (`?tur=`) narrows the answer
 * already on screen and never changes what is asked; the server's order is kept. Nothing is written, no amount is shown.
 * An unavailable check says so — never an empty list. Each order an entry names (SAT-D246 `salesOrderIds`; an invoice may span
 * several) gets its own link, in the server's order.
 */

import { Fragment } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router'
import { fetchControlCenter } from './controlCenterApi'
import {
  isControlCenterKind,
  type ControlCenter,
  type ControlCenterItem,
  type ControlCenterKind,
  type ControlCenterSeverity,
} from './controlCenterContract'
import {
  CONTROL_CENTER_KIND_HINT,
  CONTROL_CENTER_KIND_TEXT,
  CONTROL_CENTER_SEVERITY_TEXT,
  CONTROL_CENTER_STATE_TEXT,
  CONTROL_CENTER_TEXT as T,
} from './controlCenterText'
import { SALES_CAPABILITY, SALES_ROUTE, salesDetailPath } from './capabilities'
import { DispatchTabs } from './SalesDispatchShared'
import { INVOICE_CANDIDATE_ACCOUNT_PARAM } from './SalesInvoiceCandidatesPage'
import { formatBusinessDate } from './format'
import { CAPABILITY, CAPABILITY_ROUTE } from '../shell/capabilities'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { DataTable, type DataTableColumn } from '../ui/DataTable'
import { StatusIndicator, type StatusTone } from '../ui/StatusIndicator'
import styles from './SalesPool.module.css'

/** The address member that narrows the list on screen to one check kind (`?tur=<kind>`). */
export const CONTROL_CENTER_KIND_PARAM = 'tur'

const SEVERITY_TONE: Readonly<Record<ControlCenterSeverity, StatusTone>> = {
  Critical: 'danger',
  ApprovalRequired: 'warning',
  WithinTolerance: 'info',
  Info: 'neutral',
}

export function SalesControlCenterPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <ControlCenterView key={namespace ?? 'no-context'} namespace={namespace} />
}

function ControlCenterView({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const [params] = useSearchParams()
  const requested = params.get(CONTROL_CENTER_KIND_PARAM)
  // An unknown kind in the address narrows nothing: the whole list is shown.
  const kindFilter = requested !== null && isControlCenterKind(requested) ? requested : null
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null

  const read = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'sales-control-center'],
    enabled: namespace !== null && companyId !== null,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchControlCenter(companyId as string, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })

  const header = (
    <header className={styles.header}>
      <div className={styles.titleRow}>
        <h1 id="sales-control-center-heading" className={styles.title}>
          {T.heading}
        </h1>
      </div>
      <DispatchTabs />
      <p className={styles.muted}>{T.intro}</p>
      <p className={styles.muted}>{T.invoiceDetailHint}</p>
      <p className={styles.muted}>{T.noAmounts}</p>
    </header>
  )

  const data = read.data?.type === 'ok-single' ? read.data.data : null
  if (data === null) {
    const state: RemoteViewState<unknown> =
      read.data === undefined ? { kind: 'initial-loading' } : (classifyOutcome(read.data) as RemoteViewState<unknown>)
    return (
      <section className={styles.page} aria-labelledby="sales-control-center-heading">
        {header}
        <StateBlock state={state} subject={T.subject} onRetry={() => void read.refetch()} />
      </section>
    )
  }

  const items = kindFilter === null ? data.items : data.items.filter((item) => item.kind === kindFilter)
  return (
    <section className={styles.page} aria-labelledby="sales-control-center-heading">
      {header}
      <dl className={styles.facts} aria-label={T.facts}>
        <dt>{T.businessDate}</dt>
        <dd>{formatBusinessDate(data.businessDate)}</dd>
        <dt>{T.graceDays}</dt>
        <dd data-testid="sales-control-center-grace-days">{T.graceDaysValue(data.uninvoicedDispatchGraceDays)}</dd>
      </dl>
      {data.checks.some((check) => check.truncated) ? (
        <p className={styles.notice} role="status" data-testid="sales-control-center-truncated">
          {T.truncatedNotice}
        </p>
      ) : null}
      {data.checks.some((check) => check.state === 'Unavailable') ? (
        <p className={styles.notice} role="status" data-testid="sales-control-center-unavailable">
          {T.unavailableNotice}
        </p>
      ) : null}
      <Checks data={data} />
      <section className={styles.section} aria-labelledby="sales-control-center-items-heading">
        <h2 id="sales-control-center-items-heading" className={styles.subtitle}>
          {T.itemsHeading}
        </h2>
        {kindFilter !== null ? (
          <p className={styles.muted}>
            {T.filteredTo(CONTROL_CENTER_KIND_TEXT[kindFilter])}{' '}
            <Link className={styles.link} to={SALES_ROUTE.controlCenter}>
              {T.allKinds}
            </Link>
          </p>
        ) : null}
        {items.length === 0 ? (
          <StateBlock state={{ kind: kindFilter !== null ? 'empty-filtered' : 'empty-no-data' }} subject={T.subject} />
        ) : (
          <Items items={items} />
        )}
      </section>
    </section>
  )
}

const kindPath = (kind: ControlCenterKind) =>
  `${SALES_ROUTE.controlCenter}?${new URLSearchParams({ [CONTROL_CENTER_KIND_PARAM]: kind }).toString()}`

function Checks({ data }: { readonly data: ControlCenter }) {
  const columns: readonly DataTableColumn<ControlCenter['checks'][number]>[] = [
    {
      key: 'kind',
      header: T.check,
      pin: 'start',
      cell: (check) => (
        <>
          <span>{CONTROL_CENTER_KIND_TEXT[check.kind]}</span>
          <br />
          <span className={styles.muted}>{CONTROL_CENTER_KIND_HINT[check.kind](data.uninvoicedDispatchGraceDays)}</span>
        </>
      ),
    },
    {
      key: 'state',
      header: T.checkState,
      cell: (check) => (
        <StatusIndicator tone={check.state === 'Unavailable' ? 'warning' : check.state === 'NoSource' ? 'info' : 'neutral'}>
          <span data-testid={`sales-control-center-check-${check.kind}-state`}>{CONTROL_CENTER_STATE_TEXT[check.state]}</span>
        </StatusIndicator>
      ),
    },
    {
      key: 'items',
      header: T.checkItems,
      align: 'end',
      // Counting the rows the server listed under the check; an unavailable check shows no number.
      cell: (check) =>
        check.state === 'Unavailable' ? (
          T.notTruncated
        ) : (
          <span className={styles.numeric} data-testid={`sales-control-center-check-${check.kind}-count`}>
            {data.items.filter((item) => item.kind === check.kind).length}
          </span>
        ),
    },
    {
      key: 'truncated',
      header: T.checkTruncated,
      cell: (check) => (check.truncated ? <StatusIndicator tone="warning">{T.truncated}</StatusIndicator> : T.notTruncated),
    },
    {
      key: 'action',
      header: T.links,
      cell: (check) =>
        data.items.some((item) => item.kind === check.kind) ? (
          <Link className={styles.link} to={kindPath(check.kind)} aria-label={T.showOnlyLabel(CONTROL_CENTER_KIND_TEXT[check.kind])}>
            {T.showOnly}
          </Link>
        ) : (
          T.notTruncated
        ),
    },
  ]
  return (
    <section className={styles.section} aria-labelledby="sales-control-center-checks-heading">
      <h2 id="sales-control-center-checks-heading" className={styles.subtitle}>
        {T.checksHeading}
      </h2>
      <DataTable label={T.checksCaption} columns={columns} rows={data.checks} rowKey={(check) => check.kind} />
    </section>
  )
}

const itemKey = (item: ControlCenterItem) => `${item.kind}|${item.shipmentId ?? item.invoiceId ?? item.reference}`

function Items({ items }: { readonly items: readonly ControlCenterItem[] }) {
  const shell = useShellState()
  const canOpenOrder = hasCapability(shell, SALES_CAPABILITY.draftsDetail)
  const canOpenCari = hasCapability(shell, CAPABILITY.cariPartiesDetail)
  const columns: readonly DataTableColumn<ControlCenterItem>[] = [
    {
      key: 'severity',
      header: T.severity,
      pin: 'start',
      cell: (item) => <StatusIndicator tone={SEVERITY_TONE[item.severity]}>{CONTROL_CENTER_SEVERITY_TEXT[item.severity]}</StatusIndicator>,
    },
    { key: 'kind', header: T.kind, cell: (item) => CONTROL_CENTER_KIND_TEXT[item.kind] },
    { key: 'reference', header: T.reference, cell: (item) => item.reference },
    { key: 'referenceDate', header: T.referenceDate, cell: (item) => formatBusinessDate(item.referenceDate) },
    { key: 'ageDays', header: T.ageDays, align: 'end', cell: (item) => <span className={styles.numeric}>{item.ageDays}</span> },
    { key: 'count', header: T.count, align: 'end', cell: (item) => <span className={styles.numeric}>{item.count}</span> },
    {
      key: 'customer',
      header: T.customer,
      cell: (item) =>
        canOpenCari ? (
          <Link className={styles.link} to={CAPABILITY_ROUTE[CAPABILITY.cariPartiesDetail].replace(':partyId', encodeURIComponent(item.partyId))}>
            {T.openCari}
          </Link>
        ) : (
          <span className={styles.identifier} title={T.accountIdentifier}>
            {item.companyAccountId}
          </span>
        ),
    },
    {
      key: 'links',
      header: T.links,
      cell: (item) => (
        <>
          {canOpenOrder
            ? item.salesOrderIds.map((orderId, index) => (
                <Fragment key={orderId}>
                  {index > 0 ? ' ' : null}
                  <Link className={styles.link} to={salesDetailPath(orderId)}>
                    {item.salesOrderIds.length === 1 ? T.openOrder : T.openOrderNth(index + 1, item.salesOrderIds.length)}
                  </Link>
                </Fragment>
              ))
            : null}
          {item.kind === 'UninvoicedDispatchOverdue' ? (
            <>
              {' '}
              <Link
                className={styles.link}
                to={`${SALES_ROUTE.invoiceCandidates}?${new URLSearchParams({ [INVOICE_CANDIDATE_ACCOUNT_PARAM]: item.companyAccountId }).toString()}`}
              >
                {T.openCandidates}
              </Link>
            </>
          ) : null}
        </>
      ),
    },
  ]
  return (
    <DataTable
      label={T.itemsCaption}
      columns={columns}
      rows={items}
      rowKey={itemKey}
      rowAttributes={(item) => ({ 'data-kind': item.kind, 'data-reference': item.reference })}
    />
  )
}
