/**
 * GOY-DS-001 Field — wires a label, optional help and optional error to one control.
 *
 * It owns only the relationship: a stable id, `<label htmlFor>`, and `aria-describedby`
 * built from help + error (+ any describedby the caller already has). The caller renders
 * the control itself and keeps every policy: input type, validation, conversion,
 * required/min/max and how empty, zero or null values are treated. `aria-invalid` is set
 * only when the caller passes an error; the Field never decides that a value is invalid.
 */

import { useId, type ReactNode } from 'react'
import styles from './primitives.module.css'

export type FieldControlProps = {
  readonly id: string
  readonly 'aria-describedby'?: string
  readonly 'aria-invalid'?: true
}

export function Field({
  label,
  help,
  error,
  id,
  describedBy,
  className,
  children,
}: {
  readonly label: ReactNode
  readonly help?: ReactNode
  readonly error?: ReactNode
  /** Use when the control's id must be known elsewhere; otherwise a unique id is generated. */
  readonly id?: string
  /** Ids the caller already points the control at; kept ahead of help and error. */
  readonly describedBy?: string
  readonly className?: string
  readonly children: (control: FieldControlProps) => ReactNode
}) {
  const generated = useId()
  const controlId = id ?? `field-${generated}`
  const helpId = `${controlId}-help`
  const errorId = `${controlId}-error`
  const hasHelp = help !== undefined && help !== null && help !== false
  const hasError = error !== undefined && error !== null && error !== false
  const describedby = [describedBy, hasHelp ? helpId : null, hasError ? errorId : null].filter(Boolean).join(' ')
  const control: FieldControlProps = {
    id: controlId,
    ...(describedby ? { 'aria-describedby': describedby } : {}),
    ...(hasError ? { 'aria-invalid': true as const } : {}),
  }
  return (
    <div className={[styles.field, className].filter(Boolean).join(' ')}>
      <label className={styles.label} htmlFor={controlId}>
        {label}
      </label>
      {children(control)}
      {hasHelp ? (
        <p className={styles.help} id={helpId}>
          {help}
        </p>
      ) : null}
      {hasError ? (
        <p className={styles.error} id={errorId}>
          {error}
        </p>
      ) : null}
    </div>
  )
}
