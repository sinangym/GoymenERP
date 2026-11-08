/**
 * API-09 / API-36 (PR #986): the S-04 "Faturalar" tab — the order's invoices (`GET …/drafts/{id}/invoices`, under the draft
 * read) — and, per invoice, the finalization panel (`GET /sales/invoices/{id}`, under the invoice read and the price read).
 *
 *  - Every amount, date, percentage and instalment is the server's text, in the server's order. The screen never adds, splits,
 *    rounds or compares an amount, a tolerance or a threshold (AGENTS.md: "Fiyat/formül/toleransı ekran içinde yeniden yazma").
 *  - The Finance stage (bekliyor / gönderildi / kabul / red) is read from the server's ledger facts only (`financeStage`).
 *  - The finalize / re-send controls show only when the server's state allows them AND the caller may use them. The shell
 *    publishes no capability for `sales.invoices.finalize` yet, so the routed tab mounts the panel with `canControl` false and
 *    says so neutrally (NEEDS-SINAN). No new capability, permission or grant is introduced here.
 *  - Open policies are said neutrally, never decided: the dead end after Finance's authority refusal (`refused:denied`) and the
 *    automatic background re-send (NEEDS-SINAN).
 *  - 401/403 go to the shell's auth boundary; the screen does not change the session or the authority itself.
 */
import { useId, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { SALES_CAPABILITY } from './capabilities'
import { fetchDraftInvoices, fetchSalesInvoice } from './invoiceApi'
import {
  FINANCE_STAGE,
  INVOICE_FINALIZATION_KIND,
  financeStage,
  type DraftInvoices,
  type FinanceStage,
  type InvoiceFinalization,
  type SalesInvoiceDetail,
} from './invoiceFinalizationContract'
import {
  invoiceFinalizationNotice,
  offeredInvoiceCommand,
  useInvoiceFinalization,
  type InvoiceFinalizationController,
} from './invoiceFinalization'
import { isOutcomeStillUnknown } from './orderAttempt'
import { formatBusinessDate, formatInstant } from './format'
import {
  FINANCE_STAGE_TEXT,
  INVOICE_FINALIZATION_TEXT as T,
  INVOICE_STATE_TEXT,
  ORDER_INVOICES_TEXT as L,
} from './invoiceText'
import { useShellState } from '../shell/shellContext'
import { hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type ViewOutcome } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import { StatusIndicator, type StatusTone } from '../ui/StatusIndicator'
import styles from './SalesPages.module.css'

const STAGE_TONE: Record<FinanceStage, StatusTone> = {
  pending: 'info',
  'sent-waiting': 'warning',
  accepted: 'success',
  refused: 'danger',
}

const queryDefaults = {
  gcTime: 0,
  staleTime: 0,
  retry: false,
  refetchOnWindowFocus: false,
  placeholderData: () => undefined,
} as const

/** The tab's container: one API-09 read per open, keyed by the context namespace and the route entry. */
export function SalesOrderInvoices({
  draftId,
  companyId,
  namespace,
  entryGeneration,
}: {
  readonly draftId: string
  readonly companyId: string
  readonly namespace: string
  readonly entryGeneration: number
}) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const headingId = useId()
  const [openInvoiceId, setOpenInvoiceId] = useState<string | null>(null)
  const query = useQuery({
    ...queryDefaults,
    queryKey: [namespace, entryGeneration, 'sales-draft-invoices', draftId],
    queryFn: async ({ signal }): Promise<ViewOutcome<DraftInvoices>> => {
      const snapshot = boundary.capture()
      const outcome = await fetchDraftInvoices(draftId, companyId, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return signal.aborted ? { type: 'fence-stale' } : outcome
    },
  })
  const canReadDetail = hasCapability(shell, SALES_CAPABILITY.invoiceCandidatesList)
  const outcome = query.data
  let body
  if (outcome?.type === 'ok-single' && outcome.data.items.length > 0) {
    const invoices = outcome.data
    body = (
      <>
        <div className={styles.tableWrap}>
          <table className={styles.table} data-testid="sales-order-invoices-table">
            <caption className={styles.caption}>{L.caption}</caption>
            <thead><tr>
              <th scope="col">{L.documentNo}</th><th scope="col">{L.state}</th><th scope="col">{L.businessDate}</th>
              <th scope="col">{L.lineCount}</th><th scope="col">{L.createdAt}</th>
              {canReadDetail ? <th scope="col">{L.action}</th> : null}
            </tr></thead>
            <tbody>{invoices.items.map((item) => (
              <tr key={item.id} data-testid={`sales-order-invoice-${item.id}`}>
                <th scope="row">{item.documentNo}</th>
                <td>{INVOICE_STATE_TEXT[item.state]}</td>
                <td>{formatBusinessDate(item.businessDate)}</td>
                <td className={styles.numericValue}>{L.lineCountValue(item.invoiceLineCount, item.orderLineCount)}</td>
                <td>{formatInstant(item.createdAtUtc)}</td>
                {canReadDetail ? (
                  <td>
                    <Button
                      type="button"
                      variant="secondary"
                      aria-label={L.openLabel(item.documentNo)}
                      aria-pressed={openInvoiceId === item.id}
                      onClick={() => setOpenInvoiceId(openInvoiceId === item.id ? null : item.id)}
                      data-testid={`sales-order-invoice-open-${item.id}`}
                    >
                      {openInvoiceId === item.id ? L.close : L.open}
                    </Button>
                  </td>
                ) : null}
              </tr>
            ))}</tbody>
          </table>
        </div>
        {invoices.hasMore ? <p className={styles.muted} data-testid="sales-order-invoices-more">{L.hasMore}</p> : null}
        {canReadDetail ? null : <p className={styles.muted} data-testid="sales-order-invoices-no-detail">{L.noDetailRead}</p>}
        {openInvoiceId !== null && canReadDetail && invoices.items.some((item) => item.id === openInvoiceId) ? (
          <SalesInvoiceFinalizationPanel
            key={`invoice:${namespace}:${entryGeneration}:${openInvoiceId}`}
            invoiceId={openInvoiceId}
            companyId={companyId}
            namespace={namespace}
            entryGeneration={entryGeneration}
            canControl={false}
            onRecorded={() => void query.refetch()}
          />
        ) : null}
      </>
    )
  } else {
    const state = outcome === undefined
      ? { kind: 'initial-loading' as const }
      : outcome.type === 'ok-single' ? { kind: 'empty-no-data' as const } : classifyOutcome(outcome)
    body = state.kind === 'empty-no-data'
      ? <p className={styles.muted} role="status" data-testid="sales-order-invoices-empty">{L.empty}</p>
      : <StateBlock state={state} subject={L.subject} onRetry={() => void query.refetch()} />
  }
  return (
    <section className={styles.section} aria-labelledby={headingId} data-testid="sales-order-invoices">
      <h2 id={headingId}>{L.heading}</h2>
      {body}
    </section>
  )
}

/** One invoice's finalization: the read, the facts, the instalment plan and (only when allowed) the controls. */
export function SalesInvoiceFinalizationPanel({
  invoiceId,
  companyId,
  namespace,
  entryGeneration,
  canControl,
  onRecorded,
}: {
  readonly invoiceId: string
  readonly companyId: string
  readonly namespace: string
  readonly entryGeneration: number
  /** The caller may use the finalize / re-send controls (a shell capability); the server's state decides the rest. */
  readonly canControl: boolean
  /** Called after a recorded command or a conflict, so the list is read again too. */
  readonly onRecorded?: () => void
}) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const queryClient = useQueryClient()
  const queryKey = [namespace, entryGeneration, 'sales-invoice', invoiceId]
  const query = useQuery({
    ...queryDefaults,
    queryKey,
    queryFn: async ({ signal }): Promise<ViewOutcome<SalesInvoiceDetail>> => {
      const snapshot = boundary.capture()
      const outcome = await fetchSalesInvoice(invoiceId, companyId, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return signal.aborted ? { type: 'fence-stale' } : outcome
    },
  })
  const controller = useInvoiceFinalization({
    shell,
    invoiceId,
    companyId,
    onRecorded: () => {
      void queryClient.invalidateQueries({ queryKey })
      onRecorded?.()
    },
  })
  const headingId = useId()
  const outcome = query.data
  if (outcome?.type !== 'ok-single') {
    return (
      <section className={styles.section} data-testid="sales-invoice-finalization">
        <StateBlock
          state={outcome === undefined ? { kind: 'initial-loading' } : classifyOutcome(outcome)}
          subject={T.subject}
          onRetry={() => void query.refetch()}
        />
        <AttemptNotice controller={controller} />
      </section>
    )
  }
  const detail = outcome.data
  return (
    <section className={styles.section} aria-labelledby={headingId} data-testid="sales-invoice-finalization">
      <h3 id={headingId}>{T.heading(detail.documentNo)}</h3>
      <p className={styles.muted}>{T.description}</p>
      <dl className={styles.facts} aria-label={T.facts}>
        <div><dt>{T.state}</dt><dd data-testid="sales-invoice-state">{INVOICE_STATE_TEXT[detail.state]}</dd></div>
        <div><dt>{T.version}</dt><dd>{detail.version}</dd></div>
        <div><dt>{T.businessDate}</dt><dd>{formatBusinessDate(detail.businessDate)}</dd></div>
        <div><dt>{T.payable}</dt><dd className={styles.numericValue} data-testid="sales-invoice-payable">{detail.totals.payableAmount}</dd></div>
        <div><dt>{T.createdBy}</dt><dd>{detail.createdBy}</dd></div>
        {detail.paymentAllocationWarning === null ? null : (
          <div><dt>{T.paymentAllocationWarning}</dt><dd>{detail.paymentAllocationWarning}</dd></div>
        )}
      </dl>
      <MatchFacts detail={detail} />
      <FinalizationFacts finalization={detail.finalization} />
      <InstalmentTable detail={detail} />
      <AttemptNotice controller={controller} />
      <Controls detail={detail} canControl={canControl} controller={controller} />
    </section>
  )
}

function MatchFacts({ detail }: { readonly detail: SalesInvoiceDetail }) {
  const match = detail.match
  return (
    <>
      <h4>{T.matchHeading}</h4>
      {match === null ? (
        <p className={styles.muted} data-testid="sales-invoice-match-missing">{T.matchMissing}</p>
      ) : (
        <dl className={styles.facts} data-testid="sales-invoice-match">
          <div><dt>{T.officialInvoiceNo}</dt><dd>{match.officialInvoiceNo}</dd></div>
          <div><dt>{T.ettn}</dt><dd>{match.ettn}</dd></div>
          <div><dt>{T.invoiceAmount}</dt><dd className={styles.numericValue}>{match.invoiceAmount}</dd></div>
          <div><dt>{T.officialAmount}</dt><dd className={styles.numericValue}>{match.officialAmount}</dd></div>
          <div><dt>{T.difference}</dt><dd className={styles.numericValue} data-testid="sales-invoice-match-difference">{match.difference}</dd></div>
          <div><dt>{T.tolerance}</dt><dd className={styles.numericValue}>{match.tolerance}</dd></div>
          <div><dt>{T.matchedBy}</dt><dd>{match.matchedBy}</dd></div>
          <div><dt>{T.matchedAt}</dt><dd>{formatInstant(match.matchedAtUtc)}</dd></div>
        </dl>
      )}
    </>
  )
}

function FinalizationFacts({ finalization }: { readonly finalization: InvoiceFinalization | null }) {
  if (finalization === null) {
    return (
      <>
        <h4>{T.finalizationHeading}</h4>
        <p className={styles.muted} data-testid="sales-invoice-not-finalized">{T.notFinalized}</p>
      </>
    )
  }
  const stage = financeStage(finalization)
  const denied = stage === FINANCE_STAGE.refused && finalization.lastOutcomeParsed?.reason === 'denied'
  return (
    <>
      <h4>{T.finalizationHeading}</h4>
      <p>
        <StatusIndicator tone={STAGE_TONE[stage]}>
          <span data-testid="sales-invoice-finance-stage">{T.financeStage}: {FINANCE_STAGE_TEXT[stage]}</span>
        </StatusIndicator>
      </p>
      <dl className={styles.facts} data-testid="sales-invoice-finalization-facts">
        <div><dt>{T.finalizedBy}</dt><dd>{finalization.finalizedBy}</dd></div>
        <div><dt>{T.finalizedAt}</dt><dd>{formatInstant(finalization.finalizedAtUtc)}</dd></div>
        <div><dt>{T.finalizedVersion}</dt><dd>{finalization.finalizedVersion}</dd></div>
        {finalization.admittedAtUtc === null ? null : (
          <div><dt>{T.admittedAt}</dt><dd>{formatInstant(finalization.admittedAtUtc)}</dd></div>
        )}
        <div><dt>{T.attemptCount}</dt><dd data-testid="sales-invoice-attempt-count">{finalization.attemptCount}</dd></div>
        <div>
          <dt>{T.lastAttemptAt}</dt>
          <dd>{finalization.lastAttemptAtUtc === null ? T.noAttempt : formatInstant(finalization.lastAttemptAtUtc)}</dd>
        </div>
        {finalization.lastOutcome === null ? null : (
          <div><dt>{T.lastOutcome}</dt><dd data-testid="sales-invoice-last-outcome"><code>{finalization.lastOutcome}</code></dd></div>
        )}
        <div><dt>{T.accounting}</dt><dd data-testid="sales-invoice-accounting">{T.accountingNoSource}</dd></div>
      </dl>
      {denied ? <p className={styles.notice} data-testid="sales-invoice-denied-note">{T.deniedNote}</p> : null}
      {stage === FINANCE_STAGE.pending || stage === FINANCE_STAGE.sentWaiting ? (
        <p className={styles.muted} data-testid="sales-invoice-redrive-note">{T.redriveNote}</p>
      ) : null}
    </>
  )
}

/** The final plan once finalized (with Finance's open item), else the planned one — verbatim, in the server's order. */
function InstalmentTable({ detail }: { readonly detail: SalesInvoiceDetail }) {
  const final = detail.finalization
  if (final === null ? detail.instalments.length === 0 : final.instalments.length === 0) {
    return <p className={styles.muted} data-testid="sales-invoice-instalments-empty">{T.instalmentsEmpty}</p>
  }
  return (
    <div className={styles.tableWrap}>
      <table className={styles.table} data-testid="sales-invoice-instalments">
        <caption className={styles.caption}>{final === null ? T.instalmentsPlannedCaption : T.instalmentsFinalCaption}</caption>
        {final === null ? (
          <>
            <thead><tr>
              <th scope="col">{T.sequenceNo}</th><th scope="col">{T.dueDate}</th>
              <th scope="col">{T.sharePercent}</th><th scope="col">{T.amount}</th>
            </tr></thead>
            <tbody>{detail.instalments.map((row) => (
              <tr key={row.sequenceNo} data-testid={`sales-invoice-instalment-${row.sequenceNo}`}>
                <th scope="row">{row.sequenceNo}</th>
                <td>{formatBusinessDate(row.dueDate)}</td>
                <td className={styles.numericValue}>{row.sharePercent}</td>
                <td className={styles.numericValue}>{row.amount}</td>
              </tr>
            ))}</tbody>
          </>
        ) : (
          <>
            <thead><tr>
              <th scope="col">{T.sequenceNo}</th><th scope="col">{T.dueDate}</th><th scope="col">{T.amount}</th>
              <th scope="col">{T.functionalAmount}</th><th scope="col">{T.openItemId}</th>
            </tr></thead>
            <tbody>{final.instalments.map((row) => (
              <tr key={row.installmentId} data-testid={`sales-invoice-instalment-${row.sequenceNo}`}>
                <th scope="row">{row.sequenceNo}</th>
                <td>{formatBusinessDate(row.dueDate)}</td>
                <td className={styles.numericValue}>{row.amount}</td>
                <td className={styles.numericValue}>{row.functionalAmount ?? T.functionalMissing}</td>
                <td><code>{row.openItemId}</code></td>
              </tr>
            ))}</tbody>
          </>
        )}
      </table>
    </div>
  )
}

function AttemptNotice({ controller }: { readonly controller: InvoiceFinalizationController }) {
  const { attempt } = controller
  const notice = invoiceFinalizationNotice(attempt)
  return (
    <>
      {notice === null ? null : (
        <p role={attempt.phase === 'failed' ? 'alert' : 'status'} className={styles.notice} data-testid="sales-invoice-finalization-notice">
          {notice}
        </p>
      )}
      {isOutcomeStillUnknown(attempt) && attempt.phase !== 'submitting' ? (
        <div className={styles.actions}>
          <Button type="button" variant="secondary" onClick={controller.retry} data-testid="sales-invoice-finalization-retry">{T.retry}</Button>
          <Button type="button" variant="ghost" className={styles.linkButton} onClick={controller.leave}>{T.abandon}</Button>
        </div>
      ) : null}
    </>
  )
}

function Controls({
  detail,
  canControl,
  controller,
}: {
  readonly detail: SalesInvoiceDetail
  readonly canControl: boolean
  readonly controller: InvoiceFinalizationController
}) {
  const offered = offeredInvoiceCommand(detail)
  if (offered === null) return null
  if (!canControl) return <p className={styles.muted} data-testid="sales-invoice-controls-unavailable">{T.controlsUnavailable}</p>
  const { attempt } = controller
  const frozen = attempt.phase === 'submitting' || isOutcomeStillUnknown(attempt)
  const finalize = offered === INVOICE_FINALIZATION_KIND.finalize
  return (
    <div className={styles.section}>
      <p className={styles.muted}>{finalize ? T.finalizeNote : T.resendNote}</p>
      <div className={styles.actions}>
        <Button
          type="button"
          variant="primary"
          disabled={frozen}
          onClick={() => controller.submit(offered, detail)}
          data-testid={finalize ? 'sales-invoice-finalize' : 'sales-invoice-resend'}
        >
          {finalize ? T.finalize : T.resend}
        </Button>
      </div>
    </div>
  )
}
