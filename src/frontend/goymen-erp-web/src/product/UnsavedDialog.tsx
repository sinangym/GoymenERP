/**
 * The §10.4 unsaved guard, shared by the create and edit forms.
 *
 * One component rather than a copy in each screen: the two had identical focus
 * trapping and identical Escape handling, and a guard that behaves differently on
 * two pages is one the user learns to distrust on both.
 *
 * Escape means STAY. The destructive option must never be what a stray key press
 * picks, and focus starts on the safe button for the same reason.
 */

import { useEffect, useRef } from 'react'
import styles from './ProductCreatePage.module.css'

export function UnsavedDialog({
  title,
  detail,
  stayLabel,
  leaveLabel,
  onStay,
  onLeave,
}: {
  readonly title: string
  readonly detail: string
  readonly stayLabel: string
  readonly leaveLabel: string
  readonly onStay: () => void
  readonly onLeave: () => void
}) {
  const dialogRef = useRef<HTMLDivElement>(null)
  const stayRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    stayRef.current?.focus()
  }, [])

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        event.preventDefault()
        onStay()
        return
      }
      if (event.key !== 'Tab') return
      // A minimal focus trap: the dialog has exactly two controls, so cycling
      // between them is the whole loop.
      const focusable = dialogRef.current?.querySelectorAll<HTMLElement>('button')
      if (focusable === undefined || focusable.length === 0) return
      const first = focusable[0]
      const last = focusable[focusable.length - 1]
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault()
        last.focus()
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault()
        first.focus()
      }
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [onStay])

  return (
    <div className={styles.dialogBackdrop}>
      <div
        className={styles.dialog}
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="unsaved-title"
        aria-describedby="unsaved-detail"
        ref={dialogRef}
      >
        <p id="unsaved-title" className={styles.errorSummaryTitle}>
          {title}
        </p>
        <p id="unsaved-detail">{detail}</p>
        <div className={styles.actions}>
          <button type="button" className={styles.primaryAction} ref={stayRef} onClick={onStay}>
            {stayLabel}
          </button>
          <button type="button" className={styles.secondaryAction} onClick={onLeave}>
            {leaveLabel}
          </button>
        </div>
      </div>
    </div>
  )
}
