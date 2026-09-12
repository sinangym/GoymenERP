/**
 * The reasoned deactivate / reactivate modal for the SELECTED company account (C-03,
 * §14.2 "Aktif/pasif yap"). Presentation only: it owns the textarea and the focus, and
 * nothing about the command — the key, the fence, the outcome and the dirty scope live
 * in `AccountStatusControl`, so that an unresolved attempt survives this window closing.
 *
 * Modal semantics by hand, the way the shell's switch confirmation does it: focus lands
 * in the reason field on open, Tab stays inside the panel, Escape asks the owner to
 * close (the owner decides whether that needs a confirmation), and the backdrop is not
 * a click target — a stray click must not discard a reason or hide an unknown outcome.
 */

import { useEffect, useRef, type KeyboardEvent, type ReactNode } from 'react'
import type { AccountStatusIntent, ReasonProblem } from './accountStatusContract'
import { canonicalReason, REASON_MAX_LENGTH } from './accountStatusContract'
import { CARI_ACCOUNT_STATUS_TEXT as TEXT } from './cariCommandText'
import styles from './AccountStatusDialog.module.css'

export type CloseConfirm = 'unsaved-reason' | 'unresolved' | null

export interface AccountStatusDialogProps {
  readonly intent: AccountStatusIntent
  /** `<code> — <legal name>`, from the record the user is looking at. */
  readonly targetLabel: string
  readonly companyLabel: string
  readonly currentIsActive: boolean
  readonly reason: string
  readonly onReasonChange: (value: string) => void
  readonly showErrors: boolean
  readonly problem: ReasonProblem | null
  /** A request is in flight, or the pre-retry read is running. */
  readonly busy: boolean
  /**
   * The attempt's outcome is UNKNOWN: the payload is frozen and the only ways forward
   * are the notice's same-key retry or an explicit abandon. The form is closed to edits
   * and to a second submit; closing the window is still allowed, with its own question.
   */
  readonly locked: boolean
  /** A read the user asked for is on its way; the window waits, the reason stays. */
  readonly refreshing: boolean
  /** The window was re-seated on a fresh version; the reason was kept for review. */
  readonly refreshedNote: boolean
  /**
   * The owner's own read in this context has been acknowledged. Without it no command
   * may leave the client (the transport would refuse it anyway) — so Save is closed
   * BEFORE the click, not after.
   */
  readonly contractReady: boolean
  /** The record moved underneath this window; sending the old fence is refused. */
  readonly changedSinceOpen: boolean
  /** Move the fence to the version that landed, keeping the reason. */
  readonly onAdoptFresh: () => void
  readonly onSubmit: () => void
  /** Escape or "Vazgeç". The owner answers with `closeConfirm` or by unmounting. */
  readonly onRequestClose: () => void
  readonly closeConfirm: CloseConfirm
  readonly onKeepOpen: () => void
  readonly onConfirmClose: () => void
  /** The attempt notice, rendered inside the panel while the window is open. */
  readonly notice?: ReactNode
  /** Focus target after a refused submit. */
  readonly summaryRef: React.RefObject<HTMLParagraphElement | null>
}

const FOCUSABLE = 'a[href], button, textarea, input, select, [tabindex]:not([tabindex="-1"])'

/**
 * Really focusable: a control disabled through an ancestor `<fieldset disabled>` has
 * no `disabled` attribute of its own, so an attribute selector kept it in the Tab
 * ring and the wrap landed on a field that cannot take keys (Dirac #250 P2-7).
 */
function focusable(node: HTMLElement): boolean {
  if (node.matches(':disabled')) return false
  if (node.closest('fieldset:disabled') !== null) return false
  return !node.hidden
}

export function AccountStatusDialog({
  intent,
  targetLabel,
  companyLabel,
  currentIsActive,
  reason,
  onReasonChange,
  showErrors,
  problem,
  busy,
  locked,
  refreshing,
  refreshedNote,
  contractReady,
  changedSinceOpen,
  onAdoptFresh,
  onSubmit,
  onRequestClose,
  closeConfirm,
  onKeepOpen,
  onConfirmClose,
  notice,
  summaryRef,
}: AccountStatusDialogProps) {
  const panelRef = useRef<HTMLDivElement | null>(null)
  const reasonRef = useRef<HTMLTextAreaElement | null>(null)
  const keepRef = useRef<HTMLButtonElement | null>(null)

  // Focus lands on the one thing the window is for. Once, on open.
  useEffect(() => {
    reasonRef.current?.focus()
  }, [])

  // The confirmation is a question; its safe answer takes the focus when it appears —
  // and when it is answered "keep", the focus goes back to the reason, not to the
  // document: an Escape pressed from outside the panel would otherwise reach nothing.
  const hadConfirmRef = useRef(false)
  useEffect(() => {
    if (closeConfirm !== null) {
      hadConfirmRef.current = true
      keepRef.current?.focus()
    } else if (hadConfirmRef.current) {
      hadConfirmRef.current = false
      // The reason only when it can take keys; while the form is locked it cannot,
      // and the panel itself keeps the window operable.
      const reason = reasonRef.current
      if (reason !== null && focusable(reason)) reason.focus()
      else panelRef.current?.focus()
    }
  }, [closeConfirm])

  // Disabling the fieldset while a send is in flight or the outcome is unknown drops
  // the focus out of the panel; a keyboard user's next Escape would then reach the
  // document and nothing else. The panel itself takes it, so the window stays operable.
  useEffect(() => {
    if (!(busy || locked || refreshing)) return
    const panel = panelRef.current
    if (panel === null) return
    const active = document.activeElement
    // A control that was just disabled can stay reported as active without receiving
    // keys any more — that counts as "focus lost" here, not as "still inside".
    const lost =
      active === null || !panel.contains(active) || !(active instanceof HTMLElement) || !focusable(active)
    if (lost) panel.focus()
  }, [busy, locked, refreshing])

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'Escape') {
      event.preventDefault()
      if (closeConfirm !== null) onKeepOpen()
      else onRequestClose()
      return
    }
    if (event.key !== 'Tab' || panelRef.current === null) return
    const nodes = Array.from(panelRef.current.querySelectorAll<HTMLElement>(FOCUSABLE)).filter(focusable)
    if (nodes.length === 0) {
      event.preventDefault()
      panelRef.current.focus()
      return
    }
    // From the panel itself (or anything not in the ring), Tab enters at the first
    // enabled control and Shift+Tab at the last — never at a disabled one.
    const active = document.activeElement
    if (!(active instanceof HTMLElement) || !nodes.includes(active)) {
      event.preventDefault()
      ;(event.shiftKey ? nodes[nodes.length - 1] : nodes[0]).focus()
      return
    }
    const first = nodes[0]
    const last = nodes[nodes.length - 1]
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault()
      last.focus()
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault()
      first.focus()
    }
  }

  const canonicalLength = canonicalReason(reason).length
  const invalid = showErrors && problem !== null
  const problemText =
    problem === 'required'
      ? TEXT.reasonRequired
      : problem === 'too-long'
        ? TEXT.reasonTooLong(canonicalLength)
        : null
  const titleId = 'cari-account-status-title'
  const reasonId = 'cari-account-status-reason'
  const hintId = 'cari-account-status-reason-hint'
  const errorId = 'cari-account-status-reason-error'
  const submitBlocked = busy || locked || refreshing || !contractReady || changedSinceOpen || closeConfirm !== null

  return (
    <div className={styles.backdrop} data-dialog="account-status">
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        className={styles.panel}
        onKeyDown={onKeyDown}
        data-intent={intent}
        tabIndex={-1}
      >
        <h2 id={titleId}>{TEXT.title[intent]}</h2>
        <p className={styles.scope}>{TEXT.scopeNote}</p>

        <dl className={styles.facts}>
          <div className={styles.fact}>
            <dt>{TEXT.target}</dt>
            <dd>{targetLabel}</dd>
          </div>
          <div className={styles.fact}>
            <dt>{TEXT.company}</dt>
            <dd>{companyLabel}</dd>
          </div>
          <div className={styles.fact}>
            <dt>{TEXT.currentState}</dt>
            <dd data-state={currentIsActive ? 'active' : 'inactive'}>
              {currentIsActive ? TEXT.active : TEXT.inactive}
            </dd>
          </div>
          <div className={styles.fact}>
            <dt>{TEXT.targetState}</dt>
            <dd data-state={intent === 'activate' ? 'active' : 'inactive'} data-target-state>
              {intent === 'activate' ? TEXT.active : TEXT.inactive}
            </dd>
          </div>
        </dl>

        {changedSinceOpen ? (
          <div role="alert" className={styles.blocked} data-state="changed-since-open">
            <p style={{ margin: 0 }}>{busy || locked ? TEXT.changedWhileFrozen : TEXT.changedSinceOpen}</p>
            {/* Not while the attempt is frozen or a retry check runs: the window shows the
                frozen snapshot, and the same-key retry sends exactly that. */}
            {!(busy || locked) ? (
              <p style={{ margin: '8px 0 0' }}>
                <button type="button" className={styles.secondary} onClick={onAdoptFresh}>
                  {TEXT.adoptFresh}
                </button>
              </p>
            ) : null}
          </div>
        ) : null}

        {refreshing ? (
          <p role="status" className={styles.scope} data-state="refreshing">
            {TEXT.refreshing}
          </p>
        ) : null}

        {refreshedNote && !refreshing ? (
          <p role="status" className={styles.confirm} data-state="refreshed">
            {TEXT.refreshedNote}
          </p>
        ) : null}

        {!contractReady ? (
          <p role="alert" className={styles.blocked} data-state="contract-not-ready">
            {TEXT.contractNotReady}
          </p>
        ) : null}

        {invalid ? (
          <p ref={summaryRef} tabIndex={-1} role="alert" className={styles.summary}>
            {TEXT.errorSummary}
          </p>
        ) : null}

        <form
          onSubmit={(event) => {
            event.preventDefault()
            if (!submitBlocked) onSubmit()
          }}
        >
          <fieldset
            disabled={busy || locked || refreshing || closeConfirm !== null}
            style={{ border: 0, padding: 0, margin: 0 }}
          >
            <div className={styles.field}>
              <label htmlFor={reasonId}>{TEXT.reason}</label>
              <textarea
                id={reasonId}
                ref={reasonRef}
                value={reason}
                onChange={(event) => onReasonChange(event.target.value)}
                aria-describedby={invalid ? `${hintId} ${errorId}` : hintId}
                aria-invalid={invalid ? 'true' : undefined}
                aria-required="true"
                autoComplete="off"
                spellCheck={false}
              />
              <p id={hintId} className={styles.hint}>
                {TEXT.reasonHint}
              </p>
              <span className={styles.count} aria-hidden="true">
                {TEXT.reasonCount(canonicalLength)}
              </span>
              {invalid && problemText !== null ? (
                <p id={errorId} className={styles.error}>
                  {problemText}
                </p>
              ) : null}
            </div>
          </fieldset>

          {notice}

          {closeConfirm !== null ? (
            <div role="alertdialog" aria-labelledby="cari-account-status-close-title" className={styles.confirm}>
              <p id="cari-account-status-close-title" className={styles.confirmTitle}>
                {closeConfirm === 'unresolved' ? TEXT.closeUnresolvedTitle : TEXT.closeConfirmTitle}
              </p>
              <p>{closeConfirm === 'unresolved' ? TEXT.closeUnresolvedDetail : TEXT.closeConfirmDetail}</p>
              <div className={styles.actions}>
                <button ref={keepRef} type="button" className={styles.primary} onClick={onKeepOpen}>
                  {closeConfirm === 'unresolved' ? TEXT.closeUnresolvedKeep : TEXT.closeConfirmKeep}
                </button>
                <button type="button" className={styles.secondary} onClick={onConfirmClose}>
                  {closeConfirm === 'unresolved' ? TEXT.closeUnresolvedClose : TEXT.closeConfirmDiscard}
                </button>
              </div>
            </div>
          ) : (
            <div className={styles.actions}>
              <button type="button" className={styles.secondary} onClick={onRequestClose} disabled={busy}>
                {TEXT.cancel}
              </button>
              <button type="submit" className={styles.primary} disabled={submitBlocked} aria-busy={busy}>
                {busy ? TEXT.submitting : TEXT.submit[intent]}
              </button>
            </div>
          )}
        </form>
        <span hidden data-max-length={REASON_MAX_LENGTH} />
      </div>
    </div>
  )
}
