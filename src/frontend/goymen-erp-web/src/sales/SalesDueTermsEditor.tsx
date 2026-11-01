/**
 * DEC-SAL-008 / SAT-D123 / SAT-D127 / SAT-D129: the due terms ("vade") editor, shared by the S-04 header revision and the S-03
 * create form. A plan is null ("vade yok") or one or more instalments, each with its OWN due type — "nokta vade" (order date +
 * N days) or "sevk vadesi" (each shipment's date + N days) — its days and its share. Mixing the two types across instalments
 * ("karma vade") is one plan. The editor keeps the typed text; `buildDueTerms` reads it when the form is sent. It computes no
 * due date and adds no shares: both are the server's (SAT-D86).
 */
import { DUE_BASES, DUE_LIMITS, type DueBasis, type InstalmentRow } from './headerRevision'
import { HEADER_REVISION_TEXT as T } from './salesText'
import { Button } from '../ui/Button'
import styles from './SalesPages.module.css'

export function SalesDueTermsEditor({
  rows,
  onChange,
  nextKey,
  disabled = false,
  testId,
  error = null,
  errorId,
}: {
  /** null: no due terms ("vade yok"). */
  readonly rows: readonly InstalmentRow[] | null
  readonly onChange: (rows: readonly InstalmentRow[] | null) => void
  /** Mints a stable React key for a new row. */
  readonly nextKey: () => string
  readonly disabled?: boolean
  readonly testId?: string
  /** A form-level message about the plan (shown under the legend, described by the fieldset). */
  readonly error?: string | null
  readonly errorId?: string
}) {
  const blank = (): InstalmentRow => ({ key: nextKey(), basis: 'OrderDate', daysAfter: '', sharePercent: '' })
  const setRow = (rowKey: string, patch: Partial<InstalmentRow>) =>
    onChange(rows?.map((row) => (row.key === rowKey ? { ...row, ...patch } : row)) ?? null)
  return (
    <fieldset className={styles.line} disabled={disabled} data-testid={testId}
      aria-describedby={error !== null && errorId !== undefined ? errorId : undefined}>
      <legend>{T.dueTerms}</legend>
      <p className={styles.muted}>{T.dueTermsHint}</p>
      <p className={styles.muted}>{T.mixedDueTermsHint}</p>
      {error === null ? null : <p id={errorId} role="alert" className={styles.notice}>{error}</p>}
      {rows === null ? (
        <>
          <p className={styles.muted}>{T.noDueTerms}</p>
          <Button type="button" variant="secondary" onClick={() => onChange([blank()])}>{T.addDueTerms}</Button>
        </>
      ) : (
        <>
          {rows.map((row, index) => (
            <div key={row.key} role="group" aria-label={T.instalment(index + 1)}>
              <label htmlFor={`${row.key}-basis`}>{T.basis}</label>
              <select id={`${row.key}-basis`} value={row.basis}
                onChange={(event) => setRow(row.key, { basis: event.target.value as DueBasis })}>
                {DUE_BASES.map((basis) => <option key={basis} value={basis}>{T.basisName[basis]}</option>)}
              </select>
              <label htmlFor={`${row.key}-days`}>{T.daysAfter}</label>
              <input id={`${row.key}-days`} inputMode="numeric" value={row.daysAfter}
                onChange={(event) => setRow(row.key, { daysAfter: event.target.value })} />
              <label htmlFor={`${row.key}-share`}>{T.sharePercent}</label>
              <input id={`${row.key}-share`} inputMode="decimal" value={row.sharePercent}
                onChange={(event) => setRow(row.key, { sharePercent: event.target.value })} />
              <Button type="button" variant="ghost" className={styles.linkButton}
                onClick={() => onChange(rows.filter((candidate) => candidate.key !== row.key))}>
                {T.removeInstalment(index + 1)}
              </Button>
            </div>
          ))}
          <Button type="button" variant="secondary" disabled={rows.length >= DUE_LIMITS.maximumInstalments}
            onClick={() => onChange([...rows, blank()])}>
            {T.addInstalment}
          </Button>
          <Button type="button" variant="ghost" className={styles.linkButton} onClick={() => onChange(null)}>
            {T.removeDueTerms}
          </Button>
        </>
      )}
    </fieldset>
  )
}
