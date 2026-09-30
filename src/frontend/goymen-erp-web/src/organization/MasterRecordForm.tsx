/**
 * The master edit form: create or edit one site, location or work centre (code and name, plus the one structural
 * choice a create makes — a site's type, a location's site, a work centre's location and kind).
 *
 * THE RULES THAT SHAPE THIS FILE (the active-state dialog's, ORG-02):
 *
 *  - ONE KEY PER SUBMITTED SNAPSHOT. A submit mints a key and freezes the exact command it was minted for. A definite
 *    refusal (400/403/404/409/422) closes that attempt: the fields stay as typed and the next submit is a new command
 *    with a new key. An UNKNOWN outcome (no response, 5xx, an answer off the contract) keeps the key and the frozen
 *    command: the fields cannot be edited and the only ways forward are the same-key resend (the server replays
 *    rather than writing twice) or re-reading the record. Editing would mint a second command.
 *  - A 204 IS NOT A STATE and a 201 is only the new record's id: after either, the caller re-reads the record and the
 *    lists behind it; this form never predicts the saved values or the next version.
 *  - A 401/403 goes to the shared auth boundary, captured before the request, like every other Organization call.
 *  - Typed work is dirty work: the shell warns before a company switch discards it.
 */

import { useCallback, useEffect, useId, useRef, useState } from 'react'
import { sendMasterCommand, type MasterCommand, type MasterFailure, type MasterOutcome } from './masterDataApi'
import type { CreatedRecord } from './contract'
import type { CommandUnknown } from './commandApi'
import {
  MASTER_TEXT,
  REASON_MAX_LENGTH,
  REASON_REQUIRED,
  REASON_TOO_LONG,
  conflictVersionsText,
  masterFailureText,
  unknownText,
} from './organizationText'
import { useShellDispatch } from '../shell/shellContext'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import styles from './MasterRecordForm.module.css'

export interface MasterFormValues {
  readonly code: string
  readonly name: string
  /** The structural choice of a create (site type, parent id, work-centre kind); '' when the form has none. */
  readonly choice: string
  /** A second structural choice (a new work centre's kind); '' when the form has none. */
  readonly secondChoice: string
  readonly reason: string
}

export interface MasterChoice {
  readonly label: string
  readonly options: readonly { readonly value: string; readonly label: string }[]
  /** Why the list cannot be offered (unreadable, too long); the form then refuses to submit. */
  readonly unavailable?: string
  readonly help?: string
}

type FieldName = 'code' | 'name' | 'choice' | 'secondChoice' | 'reason'

type Phase =
  | { readonly kind: 'editing' }
  | { readonly kind: 'submitting'; readonly key: string; readonly command: MasterCommand }
  | {
      readonly kind: 'unknown'
      readonly key: string
      readonly command: MasterCommand
      readonly unknown: CommandUnknown
      readonly lastRefusal?: MasterFailure
    }
  | { readonly kind: 'failed'; readonly failure: MasterFailure }

/** One key per snapshot. A UUID is inside the server's 1..128 canonical length and has no comma. */
function newIdempotencyKey(): string {
  const webCrypto = globalThis.crypto
  if (typeof webCrypto?.randomUUID === 'function') return webCrypto.randomUUID()
  return `org-master-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 12)}`
}

function isText(value: string, max: number): boolean {
  const trimmed = value.trim()
  // Control characters are refused by the server as well; the form says so before minting a key.
  // eslint-disable-next-line no-control-regex
  return trimmed.length > 0 && trimmed.length <= max && !/[\u0000-\u001f\u007f]/.test(trimmed)
}

export function MasterRecordForm({
  heading,
  companyId,
  dirtyScope,
  initial,
  choice,
  secondChoice,
  note,
  buildCommand,
  onCreated,
  onUpdated,
  onReload,
  onCancel,
}: {
  readonly heading: string
  readonly companyId: string
  /** The shell dirty-scope id for this form (one per form kind). */
  readonly dirtyScope: string
  readonly initial: { readonly code: string; readonly name: string; readonly choice?: string }
  readonly choice?: MasterChoice
  readonly secondChoice?: MasterChoice
  readonly note?: string
  /** Builds the command from the values AT SUBMIT TIME, so an edit always carries the freshly read version. */
  readonly buildCommand: (values: MasterFormValues) => MasterCommand
  readonly onCreated?: (record: CreatedRecord, replayed: boolean) => Promise<void> | void
  readonly onUpdated?: (replayed: boolean) => Promise<void> | void
  /** Re-reads the record and the lists behind it. Never touches an unresolved attempt. */
  readonly onReload: () => Promise<void> | void
  readonly onCancel: () => void
}) {
  const shellDispatch = useShellDispatch()
  const boundary = useAuthBoundary()
  const baseId = useId()
  const [code, setCode] = useState(initial.code)
  const [name, setName] = useState(initial.name)
  const [picked, setPicked] = useState(initial.choice ?? '')
  const [picked2, setPicked2] = useState('')
  const [reason, setReason] = useState('')
  const [errors, setErrors] = useState<Partial<Record<FieldName, string>>>({})
  const [phase, setPhase] = useState<Phase>({ kind: 'editing' })
  const noticeRef = useRef<HTMLDivElement>(null)
  const codeRef = useRef<HTMLInputElement>(null)

  const busy = phase.kind === 'submitting'
  const frozen = phase.kind === 'unknown' || busy

  const dirty =
    frozen ||
    code !== initial.code ||
    name !== initial.name ||
    picked !== (initial.choice ?? '') ||
    picked2 !== '' ||
    reason.trim().length > 0
  useEffect(() => {
    shellDispatch({ type: dirty ? 'DIRTY_SET' : 'DIRTY_CLEAR', scope: dirtyScope })
  }, [dirty, dirtyScope, shellDispatch])
  useEffect(() => () => shellDispatch({ type: 'DIRTY_CLEAR', scope: dirtyScope }), [dirtyScope, shellDispatch])

  useEffect(() => {
    codeRef.current?.focus()
  }, [])

  useEffect(() => {
    if (phase.kind === 'failed' || phase.kind === 'unknown') noticeRef.current?.focus()
  }, [phase])

  const settle = useCallback(
    async (outcome: MasterOutcome, key: string, command: MasterCommand, previous?: Phase) => {
      if (outcome.type === 'created') {
        setPhase({ kind: 'editing' })
        shellDispatch({ type: 'DIRTY_CLEAR', scope: dirtyScope })
        await onCreated?.(outcome.record, outcome.replayed)
        return
      }
      if (outcome.type === 'updated') {
        setPhase({ kind: 'editing' })
        shellDispatch({ type: 'DIRTY_CLEAR', scope: dirtyScope })
        await onUpdated?.(outcome.replayed)
        return
      }
      if (outcome.type === 'unknown') {
        setPhase({ kind: 'unknown', key, command, unknown: outcome.unknown })
        return
      }
      /**
       * A refusal on a RESEND that is not the stored answer (a 401/403 at the gate, say) is about THIS request and
       * says nothing about whether the first one was recorded: the attempt stays unknown and the refusal is shown.
       * A replayed refusal IS the original command's own answer and closes it.
       */
      if (previous?.kind === 'unknown' && !outcome.replayed) {
        setPhase({ ...previous, lastRefusal: outcome.failure })
        return
      }
      setPhase({ kind: 'failed', failure: outcome.failure })
      if (outcome.failure.kind === 'code-in-use') {
        setErrors({ code: MASTER_TEXT.codeInUseDetail })
      }
    },
    [dirtyScope, onCreated, onUpdated, shellDispatch],
  )

  const send = useCallback(
    async (key: string, command: MasterCommand, previous?: Phase) => {
      setPhase({ kind: 'submitting', key, command })
      const security = boundary.capture()
      const outcome = await sendMasterCommand(companyId, key, command)
      if (outcome.type === 'failed') {
        if (outcome.failure.kind === 'unauthenticated') boundary.report(401, security)
        if (outcome.failure.kind === 'forbidden') boundary.report(403, security)
      }
      await settle(outcome, key, command, previous)
    },
    [boundary, companyId, settle],
  )

  const submit = useCallback(
    (event: React.FormEvent) => {
      event.preventDefault()
      if (frozen) return
      const next: typeof errors = {}
      if (!isText(code, MASTER_TEXT.codeMax)) next.code = MASTER_TEXT.codeRequired
      if (!isText(name, MASTER_TEXT.nameMax)) next.name = MASTER_TEXT.nameRequired
      if (choice !== undefined && (choice.unavailable !== undefined || picked === '')) {
        next.choice = choice.unavailable ?? MASTER_TEXT.parentRequired
      }
      if (secondChoice !== undefined && picked2 === '') next.secondChoice = MASTER_TEXT.typeRequired
      if (reason.trim().length === 0) next.reason = REASON_REQUIRED
      else if (reason.trim().length > REASON_MAX_LENGTH) next.reason = REASON_TOO_LONG
      setErrors(next)
      // A submit refused here mints NO key: nothing was attempted, so there is nothing to be uncertain about.
      if (Object.keys(next).length > 0) return
      const command = buildCommand({ code, name, choice: picked, secondChoice: picked2, reason })
      void send(newIdempotencyKey(), command)
    },
    [buildCommand, choice, code, frozen, name, picked, picked2, reason, secondChoice, send],
  )

  const resend = useCallback(() => {
    if (phase.kind !== 'unknown') return
    void send(phase.key, phase.command, phase)
  }, [phase, send])

  const reloadAfterRefusal = useCallback(async () => {
    setPhase({ kind: 'editing' })
    await onReload()
  }, [onReload])

  const fieldId = (field: string) => `${baseId}-${field}`
  const describedBy = (field: FieldName, help?: string) =>
    [help, errors[field] === undefined ? undefined : fieldId(`${field}-error`)].filter(Boolean).join(' ') || undefined

  return (
    <section className={styles.section} aria-labelledby={fieldId('heading')}>
      <h2 id={fieldId('heading')} className={styles.heading}>
        {heading}
      </h2>
      <p className={styles.help} id={fieldId('help')}>
        {frozen && phase.kind === 'unknown' ? MASTER_TEXT.frozenHelp : MASTER_TEXT.help}
      </p>
      {note === undefined ? null : <p className={styles.help}>{note}</p>}

      <Notice phase={phase} noticeRef={noticeRef} onResend={resend} onReloadRecord={onReload} onReloadAfterRefusal={reloadAfterRefusal} />

      <form className={styles.form} onSubmit={submit} noValidate aria-describedby={fieldId('help')}>
        <div className={styles.field}>
          <label className={styles.label} htmlFor={fieldId('code')}>
            {MASTER_TEXT.code}
          </label>
          <input
            id={fieldId('code')}
            ref={codeRef}
            className={styles.input}
            value={code}
            maxLength={MASTER_TEXT.codeMax}
            readOnly={frozen}
            aria-invalid={errors.code !== undefined}
            aria-describedby={describedBy('code')}
            onChange={(event) => setCode(event.target.value)}
          />
          <FieldError id={fieldId('code-error')} text={errors.code} />
        </div>

        <div className={styles.field}>
          <label className={styles.label} htmlFor={fieldId('name')}>
            {MASTER_TEXT.name}
          </label>
          <input
            id={fieldId('name')}
            className={styles.input}
            value={name}
            maxLength={MASTER_TEXT.nameMax}
            readOnly={frozen}
            aria-invalid={errors.name !== undefined}
            aria-describedby={describedBy('name')}
            onChange={(event) => setName(event.target.value)}
          />
          <FieldError id={fieldId('name-error')} text={errors.name} />
        </div>

        {choice === undefined ? null : (
          <div className={styles.field}>
            <label className={styles.label} htmlFor={fieldId('choice')}>
              {choice.label}
            </label>
            <select
              id={fieldId('choice')}
              className={styles.select}
              value={picked}
              disabled={frozen || choice.unavailable !== undefined}
              aria-invalid={errors.choice !== undefined}
              aria-describedby={describedBy('choice', choice.help === undefined ? undefined : fieldId('choice-help'))}
              onChange={(event) => setPicked(event.target.value)}
            >
              <option value="">{MASTER_TEXT.choose}</option>
              {choice.options.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
            {choice.help === undefined ? null : (
              <p id={fieldId('choice-help')} className={styles.help}>
                {choice.help}
              </p>
            )}
            {choice.unavailable === undefined ? null : <p className={styles.help}>{choice.unavailable}</p>}
            <FieldError id={fieldId('choice-error')} text={errors.choice} />
          </div>
        )}

        {secondChoice === undefined ? null : (
          <div className={styles.field}>
            <label className={styles.label} htmlFor={fieldId('secondChoice')}>
              {secondChoice.label}
            </label>
            <select
              id={fieldId('secondChoice')}
              className={styles.select}
              value={picked2}
              disabled={frozen}
              aria-invalid={errors.secondChoice !== undefined}
              aria-describedby={describedBy('secondChoice')}
              onChange={(event) => setPicked2(event.target.value)}
            >
              <option value="">{MASTER_TEXT.choose}</option>
              {secondChoice.options.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
            <FieldError id={fieldId('secondChoice-error')} text={errors.secondChoice} />
          </div>
        )}

        <div className={`${styles.field} ${styles.wide}`}>
          <label className={styles.label} htmlFor={fieldId('reason')}>
            {MASTER_TEXT.reason}
          </label>
          <textarea
            id={fieldId('reason')}
            className={styles.textarea}
            value={reason}
            rows={2}
            maxLength={REASON_MAX_LENGTH}
            readOnly={frozen}
            aria-invalid={errors.reason !== undefined}
            aria-describedby={describedBy('reason')}
            onChange={(event) => setReason(event.target.value)}
          />
          <FieldError id={fieldId('reason-error')} text={errors.reason} />
        </div>

        <div className={styles.actions}>
          {phase.kind === 'unknown' ? null : (
            <button type="submit" className={styles.primary} disabled={busy}>
              {MASTER_TEXT.save}
            </button>
          )}
          <button type="button" className={styles.secondary} onClick={onCancel} disabled={frozen}>
            {MASTER_TEXT.cancel}
          </button>
        </div>
      </form>
    </section>
  )
}

/**
 * The closed state of a master form: the button that opens it and, after an accepted command, the notice that says
 * the command was accepted — never what the record now is (the re-read card says that).
 */
export function MasterFormLauncher({
  label,
  saved,
  onOpen,
}: {
  readonly label: string
  readonly saved: null | { readonly title: string; readonly detail: string; readonly outcome: string }
  readonly onOpen: () => void
}) {
  return (
    <section aria-label={label} className={styles.section}>
      {saved === null ? null : (
        <div className={styles.noticeOk} role="status" data-outcome={saved.outcome}>
          <p className={styles.noticeTitle}>{saved.title}</p>
          <p>{saved.detail}</p>
        </div>
      )}
      <button type="button" className={`${styles.secondary} ${styles.toggle}`} onClick={onOpen}>
        {label}
      </button>
    </section>
  )
}

function FieldError({ id, text }: { readonly id: string; readonly text: string | undefined }) {
  if (text === undefined) return null
  return (
    <p id={id} className={styles.fieldError} role="alert">
      {text}
    </p>
  )
}

function Notice({
  phase,
  noticeRef,
  onResend,
  onReloadRecord,
  onReloadAfterRefusal,
}: {
  readonly phase: Phase
  readonly noticeRef: React.RefObject<HTMLDivElement | null>
  readonly onResend: () => void
  readonly onReloadRecord: () => Promise<void> | void
  readonly onReloadAfterRefusal: () => Promise<void>
}) {
  if (phase.kind === 'failed') {
    const { title, detail } = masterFailureText(phase.failure)
    return (
      <div ref={noticeRef} tabIndex={-1} className={styles.noticeError} role="alert" data-outcome={phase.failure.kind}>
        <p className={styles.noticeTitle}>{title}</p>
        <p>{detail}</p>
        {phase.failure.kind === 'version-conflict' ? (
          <>
            <p className={styles.conflict}>
              {conflictVersionsText(
                phase.failure.conflict.expectedVersion,
                phase.failure.conflict.currentVersion,
                phase.failure.conflictIsCurrent,
              )}
            </p>
            {/* Closes the attempt and re-reads the record, KEEPING what the user typed. */}
            <button type="button" className={styles.secondary} onClick={() => void onReloadAfterRefusal()}>
              {MASTER_TEXT.reload}
            </button>
          </>
        ) : null}
      </div>
    )
  }

  if (phase.kind !== 'unknown') return null
  const { title, detail } = unknownText(phase.unknown)
  return (
    <div ref={noticeRef} tabIndex={-1} className={styles.noticeWarn} role="alert" data-outcome="unknown">
      <p className={styles.noticeTitle}>{title}</p>
      <p>{detail}</p>
      {phase.lastRefusal === undefined ? null : (
        <p data-refusal={phase.lastRefusal.kind}>
          {`Son deneme sunucu tarafından reddedildi (${masterFailureText(phase.lastRefusal).title}). Bu, ilk isteğin uygulanmadığı anlamına gelmez.`}
        </p>
      )}
      <div className={styles.actions}>
        {/* Same snapshot, same key — the server replays rather than writing twice. */}
        <button type="button" className={styles.primary} onClick={onResend}>
          {MASTER_TEXT.resend}
        </button>
        {/* Re-reads the record ONLY; the unresolved key and command are kept. */}
        <button type="button" className={styles.secondary} onClick={() => void onReloadRecord()}>
          {MASTER_TEXT.reload}
        </button>
      </div>
    </div>
  )
}
