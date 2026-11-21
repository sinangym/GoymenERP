/**
 * F2 (SATIS_RISK_SEVK_KAPISI_VE_FASON_FATURA_PLANI_v1 §F2): "Fason hizmet faturası taslağı hazırla" on a subcontract service
 * order. The person names the loading instruction and its delivery weighing; the body carries those two ids and the order only
 * — the net kilograms (LOJ-D9: the fee kilograms; scrap is not billed), the kg price, the tax and every amount are the
 * server's. A weighing that cannot be invoiced now (no weighing, an unresolved source, a non-kg line or already invoiced)
 * comes back as the server's reasons, listed verbatim; the screen evaluates none of them.
 *
 * One Idempotency-Key per submitted snapshot: an unknown outcome keeps the key and freezes the inputs (they survive the panel,
 * ./pendingPoolAttempts.ts) until the same request is retried or left. 401/403 go to the auth boundary.
 */
import { useId, useState } from 'react'
import { formatBusinessDate } from './format'
import { INVOICE_FINALIZATION_TEXT as T } from './invoiceText'
import { orderAttemptBinding } from './orderAttempt'
import { recallPoolAttempt } from './pendingPoolAttempts'
import { usePoolDirty } from './poolAttempt'
import type { CreateSubcontractInvoicePayload } from './SubcontractInvoiceApi'
import {
  isSubcontractInvoiceFormValues,
  subcontractInvoiceNotice,
  subcontractInvoiceScope,
  useSubcontractInvoiceCreate,
  type SubcontractInvoiceFormValues,
} from './SubcontractInvoiceAttempt'
import { SUBCONTRACT_SERVICE_INVOICES_PATH, type SubcontractInvoiceDetail } from './SubcontractInvoiceContract'
import { SUBCONTRACT_INVOICE_TEXT as S } from './SubcontractInvoiceText'
import { SubcontractInvoiceLifecycle } from './SubcontractInvoiceLifecycle'
import { isReferenceId } from './wirePrimitives'
import { useShellState } from '../shell/shellContext'
import { Button } from '../ui/Button'
import styles from './SalesPool.module.css'

const EMPTY: SubcontractInvoiceFormValues = { loadingInstructionId: '', weighingId: '' }

type FieldName = keyof SubcontractInvoiceFormValues

export function SubcontractInvoiceCreate({
  salesOrderId,
  onCreated,
}: {
  readonly salesOrderId: string
  /** A draft was recorded (or replayed): the order's documents may be read again. */
  readonly onCreated?: (detail: SubcontractInvoiceDetail) => void
}) {
  const shell = useShellState()
  const headingId = useId()
  const instructionId = useId()
  const weighingId = useId()
  const hintId = useId()
  const scope = subcontractInvoiceScope(salesOrderId)
  const [values, setValues] = useState<SubcontractInvoiceFormValues>(() => {
    // An unresolved attempt from an earlier visit brings back the ids it was sent from.
    const recalled = recallPoolAttempt<CreateSubcontractInvoicePayload, SubcontractInvoiceDetail, unknown>(
      orderAttemptBinding(shell, scope),
    )?.values
    return isSubcontractInvoiceFormValues(recalled) ? recalled : EMPTY
  })
  const [fieldErrors, setFieldErrors] = useState<Partial<Record<FieldName, string>>>({})
  const [localError, setLocalError] = useState<string | null>(null)
  const attempt = useSubcontractInvoiceCreate(salesOrderId, values, (next) => {
    if (next.phase === 'completed') onCreated?.(next.result)
  })
  usePoolDirty(scope, attempt.frozen)

  const notice = subcontractInvoiceNotice(attempt.state, attempt.blockedChange)
  const locked = attempt.frozen || attempt.state.phase === 'submitting'
  const created = attempt.state.phase === 'completed' ? attempt.state.result : null

  function change(field: FieldName, value: string) {
    setLocalError(null)
    setFieldErrors((current) => ({ ...current, [field]: undefined }))
    setValues((current) => ({ ...current, [field]: value }))
  }

  function create() {
    const instruction = values.loadingInstructionId.trim()
    const weighing = values.weighingId.trim()
    if (instruction.length === 0 || weighing.length === 0) {
      setLocalError(S.required)
      return
    }
    const errors: Partial<Record<FieldName, string>> = {}
    if (!isReferenceId(instruction)) errors.loadingInstructionId = S.invalidId
    if (!isReferenceId(weighing)) errors.weighingId = S.invalidId
    setFieldErrors(errors)
    if (Object.keys(errors).length > 0) return
    setLocalError(null)
    // A finished answer is closed first, so the same or changed ids are asked afresh (a not-ready weighing may be ready now).
    if ((attempt.state.phase === 'failed' && attempt.state.unresolvedSince === undefined) || attempt.state.phase === 'completed') {
      attempt.reset()
    }
    attempt.submit(SUBCONTRACT_SERVICE_INVOICES_PATH, { salesOrderId, loadingInstructionId: instruction, weighingId: weighing })
  }

  const field = (name: FieldName, id: string, label: string) => (
    <div>
      <label htmlFor={id}>{label}</label>
      <input
        id={id}
        className={styles.control}
        value={values[name]}
        onChange={(event) => change(name, event.target.value)}
        autoComplete="off"
        spellCheck={false}
        aria-describedby={hintId}
        aria-invalid={fieldErrors[name] === undefined ? undefined : true}
        data-testid={`sales-subcontract-invoice-${name}`}
      />
      {fieldErrors[name] === undefined ? null : (
        <p role="alert" className={styles.rowError} data-testid={`sales-subcontract-invoice-${name}-error`}>{fieldErrors[name]}</p>
      )}
    </div>
  )

  return (
    <section className={styles.section} aria-labelledby={headingId} data-testid="sales-subcontract-invoice">
      <h3 id={headingId} className={styles.subtitle}>{S.heading}</h3>
      <p className={styles.muted}>{S.intro}</p>
      <fieldset disabled={locked}>
        <legend className={styles.muted} id={hintId}>{S.idHint}</legend>
        <div className={styles.grid}>
          {field('loadingInstructionId', instructionId, S.loadingInstructionLabel)}
          {field('weighingId', weighingId, S.weighingLabel)}
        </div>
      </fieldset>
      {localError === null ? null : (
        <p role="alert" className={styles.notice} data-testid="sales-subcontract-invoice-error">{localError}</p>
      )}
      <div className={styles.actions}>
        <Button type="button" variant="primary" disabled={locked} onClick={create} data-testid="sales-subcontract-invoice-create">
          {S.create}
        </Button>
      </div>
      {notice === null ? null : (
        <div role={notice.role} className={styles.notice} data-testid="sales-subcontract-invoice-notice">
          <p>{notice.message}</p>
          {notice.reasons.length > 0 ? (
            <ul aria-label={S.notReady}>
              {notice.reasons.map((reason, position) => (
                <li key={`${position}:${reason}`}>{reason}</li>
              ))}
            </ul>
          ) : null}
        </div>
      )}
      {attempt.frozen && attempt.state.phase !== 'submitting' ? (
        <div className={styles.actions}>
          <Button type="button" variant="secondary" onClick={attempt.retry} data-testid="sales-subcontract-invoice-retry">
            {T.retry}
          </Button>
          <Button type="button" variant="ghost" onClick={attempt.leave} data-testid="sales-subcontract-invoice-abandon">
            {T.abandon}
          </Button>
        </div>
      ) : null}
      {created === null ? null : <SubcontractInvoiceSummary detail={created} />}
      <SubcontractInvoiceLifecycle key={created?.id ?? 'lookup'} salesOrderId={salesOrderId} initialInvoiceId={created?.id} />
    </section>
  )
}

/** The recorded draft as the server answered it: every figure is the server's text. */
function SubcontractInvoiceSummary({ detail }: { readonly detail: SubcontractInvoiceDetail }) {
  const headingId = useId()
  return (
    <div className={styles.section} role="group" aria-labelledby={headingId} data-testid="sales-subcontract-invoice-created">
      <h4 id={headingId} className={styles.subtitle}>{S.newDraftHeading}</h4>
      <dl className={styles.facts}>
        <div><dt>{S.documentNo}</dt><dd data-testid="sales-subcontract-invoice-document-no">{detail.documentNo}</dd></div>
        <div><dt>{S.state}</dt><dd>{S.draftState}</dd></div>
        <div><dt>{S.businessDate}</dt><dd>{formatBusinessDate(detail.businessDate)}</dd></div>
        <div><dt>{S.netKg}</dt><dd className={styles.numeric} data-testid="sales-subcontract-invoice-net-kg">{detail.netKg}</dd></div>
        <div><dt>{S.weighingRevision}</dt><dd>{detail.weighingRevision}</dd></div>
        <div><dt>{S.netAmount}</dt><dd className={styles.numeric}>{detail.totals.netAmount}</dd></div>
        <div><dt>{S.vatAmount}</dt><dd className={styles.numeric}>{detail.totals.vatAmount}</dd></div>
        <div><dt>{S.withheldVatAmount}</dt><dd className={styles.numeric}>{detail.totals.withheldVatAmount}</dd></div>
        <div><dt>{S.grossAmount}</dt><dd className={styles.numeric}>{detail.totals.grossAmount}</dd></div>
        <div>
          <dt>{S.payableAmount}</dt>
          <dd className={styles.numeric} data-testid="sales-subcontract-invoice-payable">{detail.totals.payableAmount}</dd>
        </div>
      </dl>
      <div className={styles.scroll}>
        <table>
          <caption>{S.linesCaption}</caption>
          <thead>
            <tr>
              <th scope="col">{S.lineNo}</th>
              <th scope="col">{S.quantity}</th>
              <th scope="col">{S.netUnitPrice}</th>
              <th scope="col">{S.vatPercent}</th>
              <th scope="col">{S.netAmount}</th>
              <th scope="col">{S.payableAmount}</th>
            </tr>
          </thead>
          <tbody>
            {detail.lines.map((line) => (
              <tr key={line.lineId} data-testid={`sales-subcontract-invoice-line-${line.lineNo}`}>
                <td>{line.lineNo}</td>
                <td className={styles.numeric}>{line.quantity}</td>
                <td className={styles.numeric}>{line.netUnitPrice}</td>
                <td className={styles.numeric}>{line.vatPercent}</td>
                <td className={styles.numeric}>{line.amounts.netAmount}</td>
                <td className={styles.numeric}>{line.amounts.payableAmount}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}
