/**
 * The outcome cell and the refusal detail of one audit row (F-023, F023-5b).
 *
 * A refused attempt is marked with the TEXT "Reddedildi" (never colour alone), its refusal class and problem code in
 * Turkish, and the HTTP status the caller received. Field errors are listed as the server stored them (field name and
 * code); they carry no value the caller typed. An F023-4 summary row reads "N kez reddedildi (pencere: …)".
 *
 * Everything shown here was decoded fail-closed by ./contract.ts; nothing is re-derived from raw JSON in this file.
 */

import type { AuditEvent } from './contract'
import type { AuditRefusal, RefusalDetail } from './auditQueryOutcome'
import { AUDIT_TEXT as T, OUTCOME_LABEL, REFUSAL_CLASS_LABEL, formatInstant, problemLabel } from './auditText'
import styles from './audit.module.css'

function ProblemText({ code }: { readonly code: string }) {
  const label = problemLabel(code)
  return label === null ? (
    <span className={styles.code}>{code}</span>
  ) : (
    <>
      {label} <span className={`${styles.code} ${styles.secondary}`}>({code})</span>
    </>
  )
}

function refusalLine(refusal: AuditRefusal): string {
  return refusal.kind === 'summary'
    ? T.refusalSummary(refusal.count, formatInstant(refusal.windowStartUtc), formatInstant(refusal.windowEndUtc))
    : `${REFUSAL_CLASS_LABEL[refusal.refusalClass]} · ${T.httpStatus(refusal.httpStatus)}`
}

/** The "Sonuç" cell: the outcome label, and for a refused row its class, status and problem code. */
export function AuditOutcomeCell({ item }: { readonly item: AuditEvent }) {
  if (item.outcome === 'Succeeded' || item.refusal === null) {
    return <span data-outcome={item.outcome}>{OUTCOME_LABEL[item.outcome]}</span>
  }
  const refusal = item.refusal
  return (
    <span data-outcome="Refused" data-refusal-kind={refusal.kind}>
      <strong className={styles.refusedLabel}>{OUTCOME_LABEL.Refused}</strong>
      <br />
      <span className={styles.secondary}>{refusalLine(refusal)}</span>
      <br />
      <span className={styles.secondary}>
        <ProblemText code={refusal.problemCode} />
      </span>
    </span>
  )
}

function FieldErrors({ refusal }: { readonly refusal: RefusalDetail }) {
  if (refusal.fieldErrors.length === 0) return <p className={styles.muted}>{T.noFieldErrors}</p>
  return (
    <ul className={styles.fieldErrors} aria-label={T.fieldErrors}>
      {refusal.fieldErrors.map((error, index) => (
        <li key={`${error.field}::${error.code}::${index}`}>
          <span className={styles.code}>{error.field}</span>: <ProblemText code={error.code} />
        </li>
      ))}
    </ul>
  )
}

/** The refusal block inside the row's "Ayrıntı" disclosure. */
export function AuditRefusalDetails({ refusal }: { readonly refusal: AuditRefusal }) {
  return (
    <div className={styles.refusal}>
      <h3>{T.refusalDetail}</h3>
      <dl className={styles.refusalFacts}>
        <dt>{T.refusalClass}</dt>
        <dd>{REFUSAL_CLASS_LABEL[refusal.refusalClass]}</dd>
        <dt>{T.problemCode}</dt>
        <dd>
          <ProblemText code={refusal.problemCode} />
        </dd>
        {refusal.kind === 'detail' ? (
          <>
            <dt>{T.httpStatusLabel}</dt>
            <dd>{refusal.httpStatus}</dd>
          </>
        ) : null}
      </dl>
      {refusal.kind === 'summary' ? (
        <p>{refusalLine(refusal)}</p>
      ) : (
        <>
          <h4>{T.fieldErrors}</h4>
          <FieldErrors refusal={refusal} />
        </>
      )}
    </div>
  )
}
