/**
 * UI-70b S-10 — one customer return invoice as `GET /api/v1/sales/customer-returns/{id}` describes it (API-40), with its
 * three commands: "Onaya gönder" (Draft), "Onayla" (Onay bekliyor) and "Kesinleştir" (Onaylandı) — SAT-D290/SAT-D294 — and
 * the invoice's two terminal decisions (SAT-D290 (2, 3)): "Reddet" (Onay bekliyor, the approver's capability) and "İptal et"
 * (Taslak or Onay bekliyor, the preparer's capability), each with an optional reason checked against the server's bound.
 *
 * Every amount is the server's text (SAT-D241/SAT-D292: the deterministic backend calculator); the screen never computes
 * or rounds. Each command is offered only in its source state and under its own published capability; the server re-judges
 * every one (the preparer/submitter cannot approve — 409 — and finalization fails closed with 503 while no Finance port is
 * registered, SAT-D294). After a recorded command, or a 409, the return is re-read.
 */
import { useId, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import {
  cancelCustomerReturn,
  fetchCustomerReturn,
  rejectCustomerReturn,
  sendCustomerReturnCommand,
  type CustomerReturnCommand,
  type CustomerReturnDecision,
  type CustomerReturnDecisionBody,
  type CustomerReturnMutation,
} from './customerReturnApi'
import {
  CUSTOMER_RETURN_REASON_MAX,
  type CustomerReturnAmounts,
  type CustomerReturnState,
  type CustomerReturnView,
} from './customerReturnContract'
import { CUSTOMER_RETURN_STATE_TEXT, CUSTOMER_RETURN_SUBJECT, CUSTOMER_RETURN_TEXT, CUSTOMER_RETURN_TONE } from './customerReturnText'
import { customerReturnNotice } from './customerReturnNotice'
import {
  SALES_CAPABILITY,
  SALES_CUSTOMER_RETURN_APPROVE_CAPABILITY,
  SALES_CUSTOMER_RETURN_FINALIZE_CAPABILITY,
  SALES_CUSTOMER_RETURN_SUBMIT_CAPABILITY,
  SALES_ROUTE,
} from './capabilities'
import { usePoolAttempt } from './poolAttempt'
import { PoolAttemptNotice, PoolPartyCell } from './SalesPoolShared'
import { formatBusinessDate, formatInstant } from './format'
import { classifyOutcome } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import { StatusIndicator } from '../ui/StatusIndicator'
import styles from './SalesPool.module.css'

const T = CUSTOMER_RETURN_TEXT.detail
const C = CUSTOMER_RETURN_TEXT.common

export function SalesCustomerReturnDetailPage() {
  const shell = useShellState()
  const { returnId } = useParams()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const boundary = useAuthBoundary()
  const queryClient = useQueryClient()
  const canList = hasCapability(shell, SALES_CAPABILITY.customerReturnsList)

  const queryKey = [namespace ?? 'no-context', shell.routeEntryGeneration, 'sales-customer-return', returnId ?? '']
  const query = useQuery({
    queryKey,
    enabled: namespace !== null && companyId !== null && (returnId ?? '').length > 0,
    gcTime: 0,
    staleTime: 0,
    retry: false,
    refetchOnWindowFocus: false,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchCustomerReturn(returnId as string, companyId as string, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })

  const backLink = canList ? (
    <Link className={styles.link} to={SALES_ROUTE.customerReturns}>
      ← {T.back}
    </Link>
  ) : null

  if (companyId === null) {
    return (
      <section className={styles.page} aria-label={CUSTOMER_RETURN_SUBJECT.detail}>
        {backLink}
        <p role="status">{C.singleCompanyOnly}</p>
      </section>
    )
  }
  const outcome = query.data
  if (outcome === undefined || outcome.type !== 'ok-single') {
    return (
      <section className={styles.page} aria-label={CUSTOMER_RETURN_SUBJECT.detail}>
        {backLink}
        <StateBlock
          state={outcome === undefined ? { kind: 'initial-loading' } : classifyOutcome(outcome)}
          subject={CUSTOMER_RETURN_SUBJECT.detail}
          onRetry={() => void query.refetch()}
        />
      </section>
    )
  }
  return (
    <ReturnView
      key={`${namespace ?? ''}:${outcome.data.id}`}
      view={outcome.data}
      backLink={backLink}
      onReread={() => void queryClient.invalidateQueries({ queryKey })}
    />
  )
}

function ReturnView({
  view,
  backLink,
  onReread,
}: {
  readonly view: CustomerReturnView
  readonly backLink: React.ReactNode
  readonly onReread: () => void
}) {
  return (
    <section className={styles.page} aria-labelledby="customer-return-heading">
      {backLink}
      <header className={styles.header}>
        <div className={styles.titleRow}>
          <h1 id="customer-return-heading" className={styles.title}>
            {T.heading(view.customerDocumentNo)}
          </h1>
          <StatusIndicator tone={CUSTOMER_RETURN_TONE[view.state]}>
            <span data-testid="customer-return-state">{CUSTOMER_RETURN_STATE_TEXT[view.state]}</span>
          </StatusIndicator>
        </div>
      </header>

      <section className={styles.section} aria-labelledby="customer-return-facts-heading">
        <h2 id="customer-return-facts-heading" className={styles.subtitle}>
          {T.facts}
        </h2>
        <dl className={styles.facts}>
          <div><dt>{C.originalInvoice}</dt><dd>{view.originalDocumentNo}</dd></div>
          <div><dt>{C.customerDocumentNo}</dt><dd>{view.customerDocumentNo}</dd></div>
          <div><dt>{C.customerDocumentDate}</dt><dd>{formatBusinessDate(view.customerDocumentDate)}</dd></div>
          <div><dt>{C.party}</dt><dd><PoolPartyCell partyId={view.partyId} /></dd></div>
          <div><dt>{T.companyAccount}</dt><dd className={styles.identifier}>{view.companyAccountId}</dd></div>
          <div><dt>{C.currency}</dt><dd className={styles.identifier}>{view.currencyId}</dd></div>
          <div>
            <dt>{C.shipment}</dt>
            <dd className={view.shipmentId === null ? undefined : styles.identifier}>{view.shipmentId ?? C.shipmentNone}</dd>
          </div>
          {view.rate === null ? null : <div><dt>{T.rate}</dt><dd className={styles.numeric}>{view.rate}</dd></div>}
          <div><dt>{C.needsReview}</dt><dd data-testid="customer-return-needs-review">{view.needsReview ? C.yes : C.no}</dd></div>
          <div><dt>{T.version}</dt><dd className={styles.identifier}>{view.version}</dd></div>
        </dl>
      </section>

      <section className={styles.section} aria-labelledby="customer-return-decisions-heading">
        <h2 id="customer-return-decisions-heading" className={styles.subtitle}>
          {T.decisions}
        </h2>
        <dl className={styles.facts} data-testid="customer-return-decisions">
          <div><dt>{T.createdBy}</dt><dd>{T.at(view.createdBy, formatInstant(view.createdAtUtc))}</dd></div>
          {view.submittedBy === null || view.submittedAtUtc === null ? null : (
            <div><dt>{T.submittedBy}</dt><dd>{T.at(view.submittedBy, formatInstant(view.submittedAtUtc))}</dd></div>
          )}
          {view.approvedBy === null || view.approvedAtUtc === null ? null : (
            <div><dt>{T.approvedBy}</dt><dd>{T.at(view.approvedBy, formatInstant(view.approvedAtUtc))}</dd></div>
          )}
          {view.finalizedBy === null || view.finalizedAtUtc === null ? null : (
            <div><dt>{T.finalizedBy}</dt><dd>{T.at(view.finalizedBy, formatInstant(view.finalizedAtUtc))}</dd></div>
          )}
          {view.rejectedBy === null || view.rejectedAtUtc === null ? null : (
            <>
              <div><dt>{T.rejectedBy}</dt><dd>{T.at(view.rejectedBy, formatInstant(view.rejectedAtUtc))}</dd></div>
              <div>
                <dt>{T.rejectionReason}</dt>
                <dd data-testid="customer-return-rejection-reason">{view.rejectionReason ?? T.reasonNone}</dd>
              </div>
            </>
          )}
          {view.cancelledBy === null || view.cancelledAtUtc === null ? null : (
            <>
              <div><dt>{T.cancelledBy}</dt><dd>{T.at(view.cancelledBy, formatInstant(view.cancelledAtUtc))}</dd></div>
              <div>
                <dt>{T.cancellationReason}</dt>
                <dd data-testid="customer-return-cancellation-reason">{view.cancellationReason ?? T.reasonNone}</dd>
              </div>
            </>
          )}
        </dl>
        <ReturnCommands view={view} onReread={onReread} />
      </section>

      <Totals id="customer-return-totals" heading={T.totals} amounts={view.totals} currencyId={view.currencyId} />
      {view.functionalTotals === null || view.functionalCurrencyId === null ? null : (
        <Totals
          id="customer-return-functional-totals"
          heading={T.functionalTotals}
          amounts={view.functionalTotals}
          currencyId={view.functionalCurrencyId}
        />
      )}

      <ReturnLines view={view} />
    </section>
  )
}

function Totals({
  id,
  heading,
  amounts,
  currencyId,
}: {
  readonly id: string
  readonly heading: string
  readonly amounts: CustomerReturnAmounts
  readonly currencyId: string
}) {
  const rows: readonly (readonly [string, string])[] = [
    [T.net, amounts.netAmount],
    [T.vat, amounts.vatAmount],
    [T.withheld, amounts.withheldVatAmount],
    [T.lineTotal, amounts.lineTotalAmount],
    [T.derivedPayable, amounts.derivedPayableAmount],
    [T.drift, amounts.driftAmount],
  ]
  return (
    <section className={styles.section} aria-labelledby={`${id}-heading`}>
      <h2 id={`${id}-heading`} className={styles.subtitle}>
        {heading} <span className={styles.identifier}>({currencyId})</span>
      </h2>
      <dl className={styles.facts} data-testid={id}>
        {rows.map(([label, value]) => (
          <div key={label}>
            <dt>{label}</dt>
            <dd className={styles.numeric}>{value}</dd>
          </div>
        ))}
      </dl>
    </section>
  )
}

function ReturnLines({ view }: { readonly view: CustomerReturnView }) {
  return (
    <section className={styles.section} aria-labelledby="customer-return-lines-heading">
      <h2 id="customer-return-lines-heading" className={styles.subtitle}>
        {T.lines}
      </h2>
      <div className={styles.scroll}>
        <table aria-labelledby="customer-return-lines-heading">
          <caption className={styles.muted}>{T.linesCaption}</caption>
          <thead>
            <tr>
              <th scope="col">{T.lineNo}</th>
              <th scope="col">{T.originalLineNo}</th>
              <th scope="col">{T.product}</th>
              <th scope="col" className={styles.numeric}>{T.quantity}</th>
              <th scope="col">{T.unit}</th>
              <th scope="col" className={styles.numeric}>{T.netUnitPrice}</th>
              <th scope="col" className={styles.numeric}>{T.vatPercent}</th>
              <th scope="col" className={styles.numeric}>{T.lineTotal}</th>
              <th scope="col">{T.flags}</th>
            </tr>
          </thead>
          <tbody>
            {view.lines.map((line) => {
              const flags: string[] = []
              if (line.isLastReturn) flags.push(T.lastReturn)
              if (line.needsReview) flags.push(C.needsReview)
              return (
                <tr key={line.lineNo} data-testid={`customer-return-line-${line.lineNo}`}>
                  <td>{line.lineNo}</td>
                  <td>{line.originalLineNo}</td>
                  <td className={styles.identifier}>{line.productId}</td>
                  <td className={styles.numeric}>{line.quantity}</td>
                  <td className={styles.identifier}>{line.unitId}</td>
                  <td className={styles.numeric}>{line.netUnitPrice}</td>
                  <td className={styles.numeric}>{line.vatPercent}</td>
                  <td className={styles.numeric}>{line.amounts.lineTotalAmount}</td>
                  <td>{flags.length === 0 ? '—' : flags.join(' · ')}</td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
    </section>
  )
}

/** The attempt binding scope of a command: one per return, so a key never crosses records. */
const commandScope = (returnId: string) => `sales.customer-return.command:${returnId}`

const COMMAND_SOURCE: Readonly<Record<CustomerReturnCommand, CustomerReturnState>> = {
  submission: 'Draft',
  approval: 'PendingApproval',
  finalization: 'Approved',
}

const COMMAND_CAPABILITY: Readonly<Record<CustomerReturnCommand, string>> = {
  submission: SALES_CUSTOMER_RETURN_SUBMIT_CAPABILITY,
  approval: SALES_CUSTOMER_RETURN_APPROVE_CAPABILITY,
  finalization: SALES_CUSTOMER_RETURN_FINALIZE_CAPABILITY,
}

const COMMAND_LABEL: Readonly<Record<CustomerReturnCommand, string>> = {
  submission: T.submit,
  approval: T.approve,
  finalization: T.finalize,
}

/** SAT-D290 (2, 3): rejection by the approver from PendingApproval; cancellation by the preparer before approval. */
const DECISION_SOURCES: Readonly<Record<CustomerReturnDecision, readonly CustomerReturnState[]>> = {
  rejection: ['PendingApproval'],
  cancellation: ['Draft', 'PendingApproval'],
}

/** The published controls: rejection beside `sales.returns.approve`, cancellation beside `sales.returns.write` (submission's). */
const DECISION_CAPABILITY: Readonly<Record<CustomerReturnDecision, string>> = {
  rejection: SALES_CUSTOMER_RETURN_APPROVE_CAPABILITY,
  cancellation: SALES_CUSTOMER_RETURN_SUBMIT_CAPABILITY,
}

const DECISION_LABEL: Readonly<Record<CustomerReturnDecision, string>> = {
  rejection: T.reject,
  cancellation: T.cancel,
}

type ReturnAction = CustomerReturnCommand | CustomerReturnDecision

const ACTIONS: readonly ReturnAction[] = ['submission', 'approval', 'finalization', 'rejection', 'cancellation']

const actionOf = (path: string): ReturnAction => ACTIONS.find((action) => path.endsWith(`/${action}`)) ?? 'submission'

const isDecision = (action: ReturnAction): action is CustomerReturnDecision => action === 'rejection' || action === 'cancellation'

/** The action whose target state a recorded answer is in (the transport accepts only the target state). */
const actionFor = (state: CustomerReturnState): ReturnAction =>
  state === 'Finalized'
    ? 'finalization'
    : state === 'Approved'
      ? 'approval'
      : state === 'Rejected'
        ? 'rejection'
        : state === 'Cancelled'
          ? 'cancellation'
          : 'submission'

function ReturnCommands({ view, onReread }: { readonly view: CustomerReturnView; readonly onReread: () => void }) {
  const shell = useShellState()
  const reasonId = useId()
  const hintId = useId()
  // The code of the last 5xx answer (the attempt machine keeps none): it says why a finalization stayed unresolved.
  const [serviceCode, setServiceCode] = useState<string | null>(null)
  const [reasonFor, setReasonFor] = useState<CustomerReturnDecision | null>(null)
  const [reason, setReason] = useState('')
  const [localError, setLocalError] = useState<string | null>(null)
  // Every action posts the version it read; only a decision sends its reason (the steps refuse a reason member).
  const attempt = usePoolAttempt<CustomerReturnDecisionBody, CustomerReturnMutation>(
    commandScope(view.id),
    async (snapshot) => {
      const action = actionOf(snapshot.path)
      const context = { idempotencyKey: snapshot.key, companyId: snapshot.companyId }
      const outcome = action === 'rejection'
        ? await rejectCustomerReturn(view.id, snapshot.payload, context)
        : action === 'cancellation'
          ? await cancelCustomerReturn(view.id, snapshot.payload, context)
          : await sendCustomerReturnCommand(action, view.id, { expectedVersion: snapshot.payload.expectedVersion }, context)
      setServiceCode(outcome.type === 'http' && outcome.status >= 500 ? (outcome.problem?.code ?? null) : null)
      return outcome
    },
    (next) => {
      if (next.phase === 'completed') {
        setReasonFor(null)
        setReason('')
      }
      if (next.phase === 'completed' || (next.phase === 'failed' && next.failure === 'conflict')) onReread()
    },
  )

  const offered = (Object.keys(COMMAND_SOURCE) as CustomerReturnCommand[]).filter(
    (command) => view.state === COMMAND_SOURCE[command] && hasCapability(shell, COMMAND_CAPABILITY[command]),
  )
  const decisions = (Object.keys(DECISION_SOURCES) as CustomerReturnDecision[]).filter(
    (decision) => DECISION_SOURCES[decision].includes(view.state) && hasCapability(shell, DECISION_CAPABILITY[decision]),
  )
  const notice = customerReturnNotice(attempt.state, {
    noun: T.noun,
    onRecord: true,
    blockedChange: attempt.blockedChange,
    completed: (result) => T.completed[actionFor(result.view.state)],
    serviceCode,
  })
  if (offered.length === 0 && decisions.length === 0 && notice === null) return null
  const formOpen = reasonFor !== null && decisions.includes(reasonFor)

  function send(action: ReturnAction, reasonText: string | null) {
    // A recorded earlier command closed its own attempt; the next command is a new one (an unresolved one is kept).
    attempt.reset()
    attempt.submit(`${view.id}/${action}`, { expectedVersion: view.version, reason: isDecision(action) ? reasonText : null })
  }

  function startStep(command: CustomerReturnCommand) {
    setReasonFor(null)
    setLocalError(null)
    send(command, null)
  }

  function openDecision(decision: CustomerReturnDecision) {
    setLocalError(null)
    setReasonFor(decision)
  }

  function confirmDecision() {
    if (reasonFor === null) return
    const trimmed = reason.trim()
    // The server measures the trimmed text; whitespace only is no reason.
    if (trimmed.length > CUSTOMER_RETURN_REASON_MAX) {
      setLocalError(T.reasonTooLong(CUSTOMER_RETURN_REASON_MAX))
      return
    }
    setLocalError(null)
    send(reasonFor, trimmed.length === 0 ? null : trimmed)
  }

  return (
    <div className={styles.page} role="group" aria-label={T.commands} data-testid="customer-return-commands">
      <PoolAttemptNotice attempt={attempt} notice={notice} testId="customer-return-notice" />
      {offered.includes('approval') || decisions.includes('rejection') ? <p className={styles.muted}>{T.approveHint}</p> : null}
      {decisions.includes('rejection') ? <p className={styles.muted}>{T.rejectNote}</p> : null}
      {decisions.includes('cancellation') ? <p className={styles.muted}>{T.cancelNote}</p> : null}
      {offered.length === 0 && decisions.length === 0 ? null : (
        <div className={styles.actions}>
          {offered.map((command) => (
            <Button key={command} type="button" variant="primary" disabled={attempt.frozen} onClick={() => startStep(command)}>
              {COMMAND_LABEL[command]}
            </Button>
          ))}
          {decisions.map((decision) => (
            <Button
              key={decision}
              type="button"
              variant="secondary"
              disabled={attempt.frozen}
              aria-pressed={reasonFor === decision && formOpen}
              onClick={() => openDecision(decision)}
              data-testid={`customer-return-${decision}`}
            >
              {DECISION_LABEL[decision]}
            </Button>
          ))}
        </div>
      )}
      {formOpen ? (
        <div className={styles.actions} data-testid="customer-return-reason-form">
          {localError === null ? null : (
            <p role="alert" className={styles.notice} data-testid="customer-return-reason-error">{localError}</p>
          )}
          <label htmlFor={reasonId}>{T.reasonLabel}</label>
          <textarea
            id={reasonId}
            aria-describedby={hintId}
            value={reason}
            disabled={attempt.frozen}
            onChange={(event) => setReason(event.target.value)}
            data-testid="customer-return-reason"
          />
          <span id={hintId} className={styles.muted}>{T.reasonHint(CUSTOMER_RETURN_REASON_MAX)}</span>
          <Button
            type="button"
            variant="primary"
            disabled={attempt.frozen}
            onClick={confirmDecision}
            data-testid="customer-return-reason-confirm"
          >
            {reasonFor === 'rejection' ? T.rejectConfirm : T.cancelConfirm}
          </Button>
          <Button type="button" variant="ghost" disabled={attempt.frozen} onClick={() => setReasonFor(null)}>
            {T.reasonBack}
          </Button>
        </div>
      ) : null}
    </div>
  )
}
