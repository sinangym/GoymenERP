/**
 * AT-009 — the VKN/TCKN block of the Cari card's identity section (user decision 2026-09-30).
 *
 * What the user can do here, and nothing more:
 *  - READ the party's number (VKN or TCKN) as the server stored it; "Girilmemiş" when there is none.
 *  - ENTER the first number. The client refuses a number whose check digits fail before any request exists; the
 *    server refuses it again on its own. Before saving, the same company's other cards carrying the number are
 *    looked up and shown as a WARNING; the user may still save ("Uyarıya rağmen kaydet").
 *  - REQUEST A CHANGE of an existing number, with a reason. It is never applied here: it waits until a DIFFERENT
 *    person with the approve permission decides it. The proposer sees that it is pending and is offered no decision.
 *  - DECIDE (approve / reject) someone else's pending request.
 *
 * What is offered comes from the read's `viewer` block, measured by the server for this actor and company; the server
 * re-authorizes every command anyway. Every command carries one Idempotency-Key per attempt: an answer the client
 * could not read keeps the SAME key and body for an explicit retry, and a definite answer ends the attempt. After any
 * applied command the card is re-read — a 204 carries no body, so nothing is painted from what was sent.
 */

import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router'
import { beginAttempt, generateAttemptKey, retryAttempt, settleAttempt } from './commandAttempt'
import type { CommandOutcome } from './commandApi'
import type { ContractContext } from './clientContract'
import {
  KIND_LABEL,
  TAX_IDENTITY_TEXT as T,
  TAX_PROBLEM_CODE,
  checkTaxIdentity,
  taxProblemCodeOf,
  type TaxChange,
  type TaxDuplicate,
  type TaxIdentityView,
} from './taxIdentity'
import {
  checkTaxIdentityDuplicates,
  decideTaxIdentityChange,
  enterTaxIdentity,
  fetchTaxIdentity,
  requestTaxIdentityChange,
} from './taxIdentityApi'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { useShellDispatch } from '../shell/shellContext'
import {
  recallTaxIdentityAttempt,
  rememberTaxIdentityAttempt,
  taxIdentityBindingForSnapshot,
  type FrozenTaxIdentityAttempt,
  type TaxIdentityAttemptState,
  type TaxIdentitySender,
} from './pendingTaxIdentityAttempt'
import styles from './CariTaxIdentityPanel.module.css'

interface Props {
  readonly partyId: string
  readonly companyId: string
  readonly namespace: string
  readonly contractContext: ContractContext | null
}

interface Notice {
  readonly tone: 'ok' | 'error'
  readonly text: string
  readonly retry?: () => void
}

type Sender = TaxIdentitySender
type DuplicateProbe = (number: string) => ReturnType<typeof checkTaxIdentityDuplicates>

const REFETCH_CODES = new Set<string>([
  TAX_PROBLEM_CODE.versionConflict,
  TAX_PROBLEM_CODE.changeStale,
  TAX_PROBLEM_CODE.changeNotPending,
  TAX_PROBLEM_CODE.changePending,
  TAX_PROBLEM_CODE.requiresApproval,
  TAX_PROBLEM_CODE.notSet,
])

function refusalText(status: number, body: unknown): string {
  const code = taxProblemCodeOf(body)
  if (code !== null && code in T.refusal) return T.refusal[code]
  if (status === 403) return T.forbidden
  return T.genericRefusal
}

export function CariTaxIdentityPanel({ partyId, companyId, namespace, contractContext }: Props) {
  const queryClient = useQueryClient()
  const boundary = useAuthBoundary()
  const shellDispatch = useShellDispatch()
  const attemptBinding = useMemo(
    () => taxIdentityBindingForSnapshot(boundary.capture(), partyId, companyId, contractContext?.origin ?? null),
    [boundary, companyId, contractContext?.origin, partyId],
  )
  const binding = attemptBinding?.key ?? JSON.stringify([namespace, partyId, companyId, contractContext?.origin ?? null])
  const bindingRef = useRef(binding)
  useEffect(() => {
    bindingRef.current = binding
  }, [binding])
  const queryKey = [namespace, 'counterparty-tax-identity', partyId, companyId]
  const query = useQuery({
    queryKey,
    queryFn: async ({ signal }) => {
      const security = boundary.capture()
      const outcome = await fetchTaxIdentity(partyId, companyId, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, security)
      }
      return outcome
    },
  })
  const [attempt, setAttempt] = useState<TaxIdentityAttemptState>(
    () => recallTaxIdentityAttempt(attemptBinding) ?? { phase: 'idle' },
  )
  const attemptRef = useRef(attempt)
  useEffect(() => {
    attemptRef.current = attempt
  }, [attempt])
  const unresolved = attempt.phase === 'submitting' || attempt.phase === 'unknown' || attempt.phase === 'abandoned'
  const dirtyScope = `cari.tax-identity:${partyId}`
  useEffect(() => {
    shellDispatch({ type: unresolved ? 'DIRTY_SET' : 'DIRTY_CLEAR', scope: dirtyScope })
  }, [dirtyScope, shellDispatch, unresolved])
  useEffect(
    () => () => shellDispatch({ type: 'DIRTY_CLEAR', scope: dirtyScope }),
    [dirtyScope, shellDispatch],
  )
  useEffect(() => {
    if (attempt.phase !== 'completed') return
    rememberTaxIdentityAttempt(attemptBinding, attempt)
    const idle: TaxIdentityAttemptState = { phase: 'idle' }
    attemptRef.current = idle
    setAttempt(idle)
  }, [attempt, attemptBinding])
  const [notice, setNotice] = useState<Notice | null>(null)
  const [formKey, setFormKey] = useState(0)

  const refresh = () => void queryClient.invalidateQueries({ queryKey })

  const probeDuplicates: DuplicateProbe = async (number) => {
    const requestBinding = binding
    const security = boundary.capture()
    const outcome = await checkTaxIdentityDuplicates(partyId, companyId, number)
    if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
      boundary.report(outcome.status, security)
    }
    return bindingRef.current === requestBinding ? outcome : { type: 'aborted' }
  }

  const dispatchAttempt = async (inFlight: TaxIdentityAttemptState): Promise<boolean> => {
    if (inFlight.phase !== 'submitting') return false
    const frozen = inFlight.snapshot.payload
    attemptRef.current = inFlight
    rememberTaxIdentityAttempt(attemptBinding, inFlight)
    setAttempt(inFlight)
    setNotice(null)
    const security = boundary.capture()
    const currentContract = contractContext?.origin === frozen.contract.origin ? contractContext : null
    const outcome: CommandOutcome<null> = currentContract === null
      ? { type: 'contract-not-ready' }
      : await frozen.send({ companyId: frozen.companyId, idempotencyKey: frozen.key, contract: currentContract })
    const next = settleAttempt(inFlight, outcome)
    rememberTaxIdentityAttempt(attemptBinding, next, false)
    if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
      boundary.report(outcome.status, security)
    }
    if (bindingRef.current !== frozen.binding) return false
    attemptRef.current = next
    setAttempt(next)
    if (next.phase === 'completed') {
      setNotice({ tone: 'ok', text: frozen.okText })
      setFormKey((value) => value + 1)
      refresh()
      return true
    }
    if (next.phase === 'unknown') {
      setNotice({ tone: 'error', text: T.unknownOutcome, retry: () => void retryCurrentAttempt() })
      return false
    }
    if (next.phase === 'failed') {
      const idle: TaxIdentityAttemptState = { phase: 'idle' }
      attemptRef.current = idle
      setAttempt(idle)
      if (outcome.type === 'http') {
        setNotice({ tone: 'error', text: refusalText(outcome.status, outcome.body) })
        const code = taxProblemCodeOf(outcome.body)
        if (code !== null && REFETCH_CODES.has(code)) refresh()
        return false
      }
      setNotice({ tone: 'error', text: outcome.type === 'contract-not-ready' ? T.contractNotReady : T.genericRefusal })
    }
    return false
  }

  const retryCurrentAttempt = async (): Promise<boolean> => {
    const retry = retryAttempt(attemptRef.current)
    return retry.phase === 'submitting' ? dispatchAttempt(retry) : false
  }

  /** One command attempt: an unresolved key/body/context cannot be replaced by a changed form. */
  const run = async (fingerprint: string, send: Sender, okText: string): Promise<boolean> => {
    if (contractContext === null) {
      setNotice({ tone: 'error', text: T.contractNotReady })
      return false
    }
    const current = attemptRef.current
    const currentFrozen = current.phase === 'idle' ? null : current.snapshot.payload
    if (currentFrozen !== null && currentFrozen.fingerprint !== fingerprint) {
      setNotice({ tone: 'error', text: T.unknownOutcome, retry: () => void retryCurrentAttempt() })
      return false
    }
    if (current.phase === 'unknown' || current.phase === 'abandoned') return retryCurrentAttempt()
    const frozen: FrozenTaxIdentityAttempt = {
      fingerprint,
      key: generateAttemptKey(),
      binding,
      companyId,
      contract: contractContext,
      send,
      okText,
    }
    return dispatchAttempt(
      beginAttempt({
        key: frozen.key,
        operation: `counterparty.tax-identity:${fingerprint.split(':', 1)[0]}`,
        binding: frozen.binding,
        payload: frozen,
      }),
    )
  }

  const outcome = query.data
  if (query.isPending) {
    return (
      <div className={styles.panel} data-testid="cari-tax-identity" data-state="loading">
        <h3 className={styles.title}>{T.heading}</h3>
        <p className={styles.muted}>{T.loading}</p>
      </div>
    )
  }
  if (outcome === undefined || outcome.type !== 'ok') {
    return (
      <div className={styles.panel} data-testid="cari-tax-identity" data-state="error">
        <h3 className={styles.title}>{T.heading}</h3>
        <p className={styles.error} role="alert">
          {outcome?.type === 'http' && outcome.status === 403 ? T.forbidden : T.loadFailed}
        </p>
        <div className={styles.actions}>
          <button type="button" className={styles.button} onClick={() => void query.refetch()}>
            {T.retry}
          </button>
        </div>
      </div>
    )
  }

  const view = outcome.data
  const pending = view.pendingChange
  const retainedAttempt = attempt.phase === 'idle' ? null : attempt.snapshot.payload
  const visibleNotice = notice ?? (unresolved && retainedAttempt !== null
    ? {
        tone: 'error' as const,
        text: T.unknownOutcome,
        retry: () => void retryCurrentAttempt(),
      }
    : null)
  return (
    <div className={styles.panel} data-testid="cari-tax-identity" data-state="ready">
      <h3 className={styles.title}>{T.heading}</h3>
      <p>
        <span className={styles.muted}>{T.factLabel}: </span>
        {view.taxIdentityNumber === null || view.kind === null ? (
          <span data-tax-identity="none">{T.none}</span>
        ) : (
          <>
            <span className={styles.value} data-tax-identity={view.taxIdentityNumber}>
              {view.taxIdentityNumber}
            </span>{' '}
            <span className={styles.muted}>({KIND_LABEL[view.kind]})</span>
          </>
        )}
      </p>

      {view.duplicates.length > 0 ? (
        <DuplicateWarning title={T.duplicateTitle} duplicates={view.duplicates} state="tax-identity-duplicate" />
      ) : null}

      {view.lastDecidedChange !== null && pending === null ? <LastDecision change={view.lastDecidedChange} /> : null}

      {pending !== null ? (
        <PendingChange
          key={`${pending.id}:${formKey}`}
          view={view}
          change={pending}
          busy={attempt.phase === 'submitting'}
          locked={unresolved}
          onDecide={(decision, note) =>
            run(
              `decide:${pending.id}:${decision}:${note ?? ''}:${view.version}`,
              (context) => decideTaxIdentityChange(context, partyId, view.version, pending.id, decision, note),
              decision === 'approve' ? T.approved : T.rejected,
            )
          }
        />
      ) : null}

      {view.viewer.canUpdate && pending === null ? (
        view.taxIdentityNumber === null ? (
          <NumberForm
            key={`enter:${formKey}`}
            mode="enter"
            busy={attempt.phase === 'submitting'}
            locked={unresolved}
            partyId={partyId}
            probeDuplicates={probeDuplicates}
            onSend={(number) =>
              run(
                `enter:${number}:${view.version}`,
                (context) => enterTaxIdentity(context, partyId, view.version, number),
                T.saved,
              )
            }
          />
        ) : (
          <ChangeRequest
            key={`request:${formKey}`}
            busy={attempt.phase === 'submitting'}
            locked={unresolved}
            partyId={partyId}
            probeDuplicates={probeDuplicates}
            onSend={(number, reason) =>
              run(
                `request:${number}:${reason}:${view.version}`,
                (context) => requestTaxIdentityChange(context, partyId, view.version, number, reason),
                T.requested,
              )
            }
          />
        )
      ) : null}

      {visibleNotice !== null ? (
        <div role={visibleNotice.tone === 'error' ? 'alert' : 'status'} data-state={`tax-identity-notice-${visibleNotice.tone}`}>
          <p className={visibleNotice.tone === 'error' ? styles.error : styles.ok}>{visibleNotice.text}</p>
          {visibleNotice.retry !== undefined ? (
            <div className={styles.actions}>
              <button
                type="button"
                className={styles.button}
                disabled={attempt.phase === 'submitting'}
                onClick={visibleNotice.retry}
              >
                {T.retrySame}
              </button>
            </div>
          ) : null}
        </div>
      ) : null}
    </div>
  )
}

function DuplicateWarning({
  title,
  duplicates,
  state,
}: {
  readonly title: string
  readonly duplicates: readonly TaxDuplicate[]
  readonly state: string
}) {
  return (
    <div className={styles.warning} role="status" data-state={state}>
      <p>
        <strong>{title}</strong>
      </p>
      <p>{T.duplicateDetail}</p>
      <ul>
        {duplicates.map((duplicate) => (
          <li key={duplicate.partyId}>
            <Link to={`/cari/${encodeURIComponent(duplicate.partyId)}`}>
              {duplicate.code} — {duplicate.legalName}
            </Link>{' '}
            {duplicate.accountIsActive ? null : <span>{T.inactiveAccount}</span>}
          </li>
        ))}
      </ul>
    </div>
  )
}

function LastDecision({ change }: { readonly change: TaxChange }) {
  return (
    <p className={styles.muted} data-state={`tax-identity-last-${change.status}`}>
      {change.status === 'approved' ? T.lastDecisionApproved : T.lastDecisionRejected}:{' '}
      {change.currentTaxIdentityNumber} → {change.proposedTaxIdentityNumber}
      {change.decisionNote !== null ? ` (${change.decisionNote})` : ''}
    </p>
  )
}

function PendingChange({
  view,
  change,
  busy,
  locked,
  onDecide,
}: {
  readonly view: TaxIdentityView
  readonly change: TaxChange
  readonly busy: boolean
  readonly locked: boolean
  readonly onDecide: (decision: 'approve' | 'reject', note: string | null) => Promise<boolean>
}) {
  const [note, setNote] = useState('')
  const noteId = `tax-identity-note-${change.id}`
  const mayDecide = view.viewer.canApprove && !view.viewer.isRequesterOfPending
  const trimmed = note.trim()
  return (
    <div className={styles.pending} data-state="tax-identity-pending">
      <strong>{T.pendingTitle}</strong>
      <dl>
        <dt>{T.pendingCurrent}</dt>
        <dd>{change.currentTaxIdentityNumber}</dd>
        <dt>{T.pendingProposed}</dt>
        <dd data-proposed-tax-identity={change.proposedTaxIdentityNumber}>
          {change.proposedTaxIdentityNumber} ({KIND_LABEL[change.proposedKind]})
        </dd>
        <dt>{T.pendingReason}</dt>
        <dd>{change.reason}</dd>
        <dt>{T.pendingRequestedAt}</dt>
        <dd>{change.requestedAtUtc}</dd>
      </dl>
      {change.proposedDuplicates.length > 0 ? (
        <DuplicateWarning
          title={T.proposedDuplicateTitle}
          duplicates={change.proposedDuplicates}
          state="tax-identity-proposed-duplicate"
        />
      ) : null}
      {view.viewer.isRequesterOfPending ? (
        <p className={styles.muted} data-state="tax-identity-own-request">
          {T.pendingBySelf}
        </p>
      ) : mayDecide ? (
        <>
          <p className={styles.muted}>{T.pendingByOther}</p>
          <div className={styles.form}>
            <label htmlFor={noteId}>{T.noteLabel}</label>
            <textarea id={noteId} value={note} maxLength={1000} rows={2} onChange={(e) => setNote(e.target.value)} />
          </div>
          <div className={styles.actions}>
            <button
              type="button"
              className={styles.button}
              disabled={busy || locked}
              onClick={() => void onDecide('approve', trimmed.length > 0 ? trimmed : null)}
            >
              {T.approve}
            </button>
            <button
              type="button"
              className={styles.button}
              disabled={busy || locked}
              onClick={() => void onDecide('reject', trimmed.length > 0 ? trimmed : null)}
            >
              {T.reject}
            </button>
          </div>
        </>
      ) : (
        <p className={styles.muted}>{T.pendingNoApprover}</p>
      )}
    </div>
  )
}

/**
 * The number field with the client check and the duplicate warning in front of the send. A warning never blocks: it
 * asks for one more, explicit click. A failed look-up is said out loud and asks for the same click.
 */
function useCheckedNumber(probeDuplicates: DuplicateProbe) {
  const [text, setText] = useState('')
  const [fieldError, setFieldError] = useState<string | null>(null)
  const [warning, setWarning] = useState<{
    readonly number: string
    readonly duplicates: readonly TaxDuplicate[] | null
  } | null>(null)
  const [checking, setChecking] = useState(false)

  /** Returns the number when it may be sent now, or null when the user must act first. */
  const prepare = async (): Promise<string | null> => {
    const check = checkTaxIdentity(text)
    if (!check.ok) {
      setFieldError(check.message)
      setWarning(null)
      return null
    }
    setFieldError(null)
    if (warning !== null && warning.number === check.value) return check.value
    setChecking(true)
    const outcome = await probeDuplicates(check.value).finally(() => setChecking(false))
    if (outcome.type === 'ok') {
      if (outcome.data.duplicates.length === 0) return check.value
      setWarning({ number: check.value, duplicates: outcome.data.duplicates })
      return null
    }
    if (outcome.type === 'http' && outcome.code === TAX_PROBLEM_CODE.invalid) {
      setFieldError(T.refusal[TAX_PROBLEM_CODE.invalid])
      return null
    }
    setWarning({ number: check.value, duplicates: null })
    return null
  }

  const onChange = (value: string) => {
    setText(value)
    setFieldError(null)
    setWarning(null)
  }

  return { text, onChange, fieldError, warning, checking, prepare }
}

function NumberForm({
  mode,
  busy,
  locked,
  partyId,
  probeDuplicates,
  onSend,
}: {
  readonly mode: 'enter'
  readonly busy: boolean
  readonly locked: boolean
  readonly partyId: string
  readonly probeDuplicates: DuplicateProbe
  readonly onSend: (number: string) => Promise<boolean>
}) {
  const field = useCheckedNumber(probeDuplicates)
  const inputId = `tax-identity-${mode}-${partyId}`
  const submit = async (event: FormEvent) => {
    event.preventDefault()
    const number = await field.prepare()
    if (number !== null) await onSend(number)
  }
  return (
    <form className={styles.form} onSubmit={(event) => void submit(event)} noValidate>
      <label htmlFor={inputId}>{T.inputLabel}</label>
      <input
        id={inputId}
        inputMode="numeric"
        autoComplete="off"
        maxLength={32}
        value={field.text}
        aria-invalid={field.fieldError !== null}
        aria-describedby={field.fieldError !== null ? `${inputId}-error` : undefined}
        onChange={(e) => field.onChange(e.target.value)}
      />
      {field.fieldError !== null ? (
        <p id={`${inputId}-error`} className={styles.error} role="alert">
          {field.fieldError}
        </p>
      ) : null}
      <CheckWarning warning={field.warning} />
      <div className={styles.actions}>
        <button type="submit" className={styles.button} disabled={busy || locked || field.checking}>
          {field.checking ? T.checking : busy ? T.sending : field.warning !== null ? T.saveAnyway : T.save}
        </button>
      </div>
    </form>
  )
}

function ChangeRequest({
  busy,
  locked,
  partyId,
  probeDuplicates,
  onSend,
}: {
  readonly busy: boolean
  readonly locked: boolean
  readonly partyId: string
  readonly probeDuplicates: DuplicateProbe
  readonly onSend: (number: string, reason: string) => Promise<boolean>
}) {
  const [open, setOpen] = useState(false)
  const [reason, setReason] = useState('')
  const [reasonError, setReasonError] = useState<string | null>(null)
  const field = useCheckedNumber(probeDuplicates)
  const inputId = `tax-identity-request-${partyId}`
  const reasonId = `${inputId}-reason`
  if (!open) {
    return (
      <div className={styles.actions}>
        <p className={styles.muted}>{T.changeNote}</p>
        <button type="button" className={styles.button} onClick={() => setOpen(true)}>
          {T.requestChange}
        </button>
      </div>
    )
  }
  const submit = async (event: FormEvent) => {
    event.preventDefault()
    const trimmed = reason.trim()
    if (trimmed.length === 0 || trimmed.length > 1000) {
      setReasonError(T.reasonRequired)
      return
    }
    setReasonError(null)
    const number = await field.prepare()
    if (number !== null) await onSend(number, trimmed)
  }
  return (
    <form className={styles.form} onSubmit={(event) => void submit(event)} noValidate>
      <p className={styles.muted}>{T.changeNote}</p>
      <label htmlFor={inputId}>{T.proposedLabel}</label>
      <input
        id={inputId}
        inputMode="numeric"
        autoComplete="off"
        maxLength={32}
        value={field.text}
        aria-invalid={field.fieldError !== null}
        onChange={(e) => field.onChange(e.target.value)}
      />
      {field.fieldError !== null ? (
        <p className={styles.error} role="alert">
          {field.fieldError}
        </p>
      ) : null}
      <label htmlFor={reasonId}>{T.reasonLabel}</label>
      <textarea
        id={reasonId}
        value={reason}
        rows={2}
        maxLength={1000}
        aria-invalid={reasonError !== null}
        onChange={(e) => {
          setReason(e.target.value)
          setReasonError(null)
        }}
      />
      {reasonError !== null ? (
        <p className={styles.error} role="alert">
          {reasonError}
        </p>
      ) : null}
      <CheckWarning warning={field.warning} />
      <div className={styles.actions}>
        <button type="submit" className={styles.button} disabled={busy || locked || field.checking}>
          {field.checking ? T.checking : busy ? T.sending : field.warning !== null ? T.sendAnyway : T.sendRequest}
        </button>
        <button type="button" className={styles.button} disabled={busy || locked} onClick={() => setOpen(false)}>
          {T.cancel}
        </button>
      </div>
    </form>
  )
}

function CheckWarning({
  warning,
}: {
  readonly warning: { readonly number: string; readonly duplicates: readonly TaxDuplicate[] | null } | null
}) {
  if (warning === null) return null
  if (warning.duplicates === null) {
    return (
      <div className={styles.warning} role="status" data-state="tax-identity-check-failed">
        <p>{CHECK_FAILED}</p>
      </div>
    )
  }
  return (
    <DuplicateWarning title={T.duplicateTitle} duplicates={warning.duplicates} state="tax-identity-duplicate-before-save" />
  )
}

const CHECK_FAILED =
  'Mükerrer kontrolü yapılamadı. Numarayı yine de kaydedebilirsiniz; kayıttan sonra kart uyarıyı gösterir.'
