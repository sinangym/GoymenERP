/**
 * The dirty-scope confirmation (ERP-000K §13.1–13.2, spec CARI_C02_LINK §4.2).
 *
 * Shown while the reducer is in `confirming-switch`: a form or workspace has
 * registered a dirty scope and the user asked for another company. Nothing has
 * been switched yet — the verified context is intact underneath — and nothing
 * will be until the user answers. "Kal" keeps the company and the form exactly as
 * they are; "Değişiklikleri bırak" clears every dirty scope and begins the
 * switch, which remounts the feature under the new context.
 *
 * Modal semantics by hand: focus lands on the safe choice ("Kal"), Escape means
 * stay, Tab cycles inside the two actions, and the shell returns focus to the
 * company picker (stay) or the main region (discard) when this closes (§13.8).
 */

import { useEffect, useRef, type KeyboardEvent } from 'react'
import styles from './SwitchConfirmDialog.module.css'

const SWITCH_CONFIRM_TEXT = {
  title: 'Kaydedilmemiş değişiklikler var',
  detail: (targetCompanyLabel: string | null) =>
    targetCompanyLabel === null
      ? 'Şirket değiştirirseniz bu ekrandaki değişiklikler ve bekleyen işlem bırakılır.'
      : `${targetCompanyLabel} şirketine geçerseniz bu ekrandaki değişiklikler ve bekleyen işlem bırakılır.`,
  stay: 'Kal',
  discard: 'Değişiklikleri bırak',
} as const

export function SwitchConfirmDialog({
  targetCompanyLabel,
  onStay,
  onDiscard,
}: {
  readonly targetCompanyLabel: string | null
  readonly onStay: () => void
  readonly onDiscard: () => void
}) {
  const stayRef = useRef<HTMLButtonElement>(null)
  const discardRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    stayRef.current?.focus()
  }, [])

  function onKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key === 'Escape') {
      event.preventDefault()
      onStay()
      return
    }
    if (event.key !== 'Tab') return
    const first = stayRef.current
    const last = discardRef.current
    if (first === null || last === null) return
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault()
      last.focus()
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault()
      first.focus()
    }
  }

  return (
    <div className={styles.backdrop}>
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby="switch-confirm-title"
        aria-describedby="switch-confirm-detail"
        className={styles.dialog}
        onKeyDown={onKeyDown}
      >
        <h2 id="switch-confirm-title" className={styles.title}>
          {SWITCH_CONFIRM_TEXT.title}
        </h2>
        <p id="switch-confirm-detail" className={styles.detail}>
          {SWITCH_CONFIRM_TEXT.detail(targetCompanyLabel)}
        </p>
        <div className={styles.actions}>
          <button ref={stayRef} type="button" className={styles.primary} onClick={onStay}>
            {SWITCH_CONFIRM_TEXT.stay}
          </button>
          <button ref={discardRef} type="button" className={styles.secondary} onClick={onDiscard}>
            {SWITCH_CONFIRM_TEXT.discard}
          </button>
        </div>
      </div>
    </div>
  )
}
