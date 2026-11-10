/**
 * UI-63 / S-09 "Satış faturaları": the company's Sales invoices, pinned to `GET /api/v1/sales/invoices?pageSize&cursor` (API-32,
 * `sales.invoices.read` only; Sales shell 1.20 `sales.invoices.list`). The server orders the list
 * (newest first) and offers no filter and no sort; paging follows the owner's cursor and the client never sorts, filters or
 * counts (LF-01).
 *
 * SAT-D238 (EK48): a user without `sales.orders.price.read` sees the list with the amounts WITHHELD by the server
 * (`amountDisclosure: Withheld`, both amounts null); each such cell says "Gizli" — never 0, never "—".
 *
 * A row's document number opens the existing invoice detail — `SalesInvoiceFinalizationPanel` (API-36, the same read under the
 * same two names) — below the list, addressed by `?fatura=<id>` so the selection survives a reload and the back button. The
 * panel is mounted with `canControl` false: no `sales.invoices.finalize` control is published yet, exactly as on the order's
 * "Faturalar" tab. A `fatura` value that is not a reference id is ignored.
 *
 * The detail read needs the price name too, so a WITHHELD row's document number is plain text with a hint and opens nothing; a
 * `fatura` value naming a loaded withheld row shows the same hint instead of the panel (no read that the server would refuse). The
 * panel therefore waits for the first page, so a deep link is checked against the loaded rows before any detail read.
 */

import { useSearchParams } from 'react-router'
import { fetchSalesInvoicePage } from './invoiceListApi'
import type { SalesInvoiceSummary } from './invoiceListContract'
import { INVOICE_LIST_TEXT } from './invoiceListText'
import { INVOICE_STATE_TEXT } from './invoiceText'
import { SalesInvoiceFinalizationPanel } from './SalesInvoiceFinalization'
import { PoolContinuation, PoolPartyCell } from './SalesPoolShared'
import { usePoolPages } from './usePoolPages'
import { formatBusinessDate, formatInstant } from './format'
import { isReferenceId } from './wirePrimitives'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { Button } from '../ui/Button'
import { DataTable, type DataTableColumn } from '../ui/DataTable'
import styles from './SalesPool.module.css'

const T = INVOICE_LIST_TEXT
const SELECTED_PARAM = 'fatura'

export function SalesInvoiceListPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <InvoiceList key={namespace ?? 'no-context'} namespace={namespace} />
}

function InvoiceList({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const list = usePoolPages(namespace, 'sales-invoices', fetchSalesInvoicePage, null)
  const [params, setParams] = useSearchParams()
  const requested = params.get(SELECTED_PARAM)
  const selectedId = isReferenceId(requested) ? requested : null
  const selectedWithheld =
    selectedId !== null && (list.rows ?? []).some((row) => row.id === selectedId && row.amountDisclosure === 'Withheld')

  const select = (invoiceId: string | null) =>
    setParams(
      (current) => {
        const next = new URLSearchParams(current)
        if (invoiceId === null) next.delete(SELECTED_PARAM)
        else next.set(SELECTED_PARAM, invoiceId)
        return next
      },
      { replace: false },
    )

  const header = (
    <header className={styles.header}>
      <div className={styles.titleRow}>
        <h1 id="sales-invoices-heading" className={styles.title}>
          {T.heading}
        </h1>
      </div>
      <p className={styles.muted}>{T.intro}</p>
    </header>
  )

  const detail = selectedWithheld ? (
    <section className={styles.section} aria-label={T.detailHeading} data-testid="sales-invoices-detail-withheld">
      <p className={styles.muted}>{T.detailWithheld}</p>
      <div className={styles.actions}>
        <Button type="button" variant="secondary" onClick={() => select(null)}>
          {T.close}
        </Button>
      </div>
    </section>
  ) : selectedId !== null && list.rows !== null && namespace !== null && companyId !== null ? (
      <section className={styles.section} aria-label={T.detailHeading} data-testid="sales-invoices-detail">
        <div className={styles.actions}>
          <Button type="button" variant="secondary" onClick={() => select(null)}>
            {T.close}
          </Button>
        </div>
        <SalesInvoiceFinalizationPanel
          key={`invoice:${namespace}:${shell.routeEntryGeneration}:${selectedId}`}
          invoiceId={selectedId}
          companyId={companyId}
          namespace={namespace}
          entryGeneration={shell.routeEntryGeneration}
          canControl={false}
          onRecorded={list.retryFirst}
        />
      </section>
    ) : null

  if (list.rows === null || list.rows.length === 0) {
    return (
      <section className={styles.page} aria-labelledby="sales-invoices-heading">
        {header}
        <StateBlock
          state={list.rows === null ? (list.firstState ?? { kind: 'initial-loading' }) : { kind: 'empty-no-data' }}
          subject={T.subject}
          onRetry={list.retryFirst}
        />
        {detail}
      </section>
    )
  }

  const columns: readonly DataTableColumn<SalesInvoiceSummary>[] = [
    {
      key: 'documentNo',
      header: T.documentNo,
      pin: 'start',
      cell: (row) =>
        row.amountDisclosure === 'Withheld' ? (
          <span data-testid={`sales-invoices-document-withheld-${row.id}`} title={T.detailWithheld}>
            {row.documentNo}
          </span>
        ) : (
          <Button
            type="button"
            variant="ghost"
            size="sm"
            aria-label={T.openLabel(row.documentNo)}
            aria-pressed={selectedId === row.id}
            onClick={() => select(selectedId === row.id ? null : row.id)}
          >
            {row.documentNo}
          </Button>
        ),
    },
    {
      key: 'state',
      header: T.state,
      cell: (row) => <span data-testid={`sales-invoices-state-${row.id}`}>{INVOICE_STATE_TEXT[row.state]}</span>,
    },
    { key: 'businessDate', header: T.businessDate, cell: (row) => formatBusinessDate(row.businessDate) },
    { key: 'customer', header: T.customer, cell: (row) => <PoolPartyCell partyId={row.partyId} /> },
    {
      key: 'payable',
      header: T.payable,
      align: 'end',
      cell: (row) => (row.amountDisclosure === 'Withheld' ? <WithheldAmount id={`sales-invoices-payable-withheld-${row.id}`} /> : (
        <span className={styles.numeric}>{row.payableAmount}</span>
      )),
    },
    {
      key: 'functionalPayable',
      header: T.functionalPayable,
      align: 'end',
      cell: (row) => (row.amountDisclosure === 'Withheld' ? <WithheldAmount id={`sales-invoices-functional-withheld-${row.id}`} /> : (
        <span className={styles.numeric}>{row.functionalPayableAmount ?? T.none}</span>
      )),
    },
    { key: 'lineCount', header: T.lineCount, align: 'end', cell: (row) => <span className={styles.numeric}>{row.lineCount}</span> },
    { key: 'createdAt', header: T.createdAt, cell: (row) => formatInstant(row.createdAtUtc) },
  ]

  return (
    <section className={styles.page} aria-labelledby="sales-invoices-heading">
      {header}
      <DataTable
        label={T.caption}
        columns={columns}
        rows={list.rows}
        rowKey={(row) => row.id}
        rowAttributes={(row) => ({ 'data-invoice-id': row.id })}
      />
      <PoolContinuation list={list} />
      {detail}
    </section>
  )
}

/** SAT-D238: the server withheld this amount from the user; it is "Gizli", not a value. */
function WithheldAmount({ id }: { readonly id: string }) {
  return (
    <span data-testid={id} aria-label={INVOICE_LIST_TEXT.withheldLabel} title={INVOICE_LIST_TEXT.withheldLabel}>
      {INVOICE_LIST_TEXT.withheld}
    </span>
  )
}
