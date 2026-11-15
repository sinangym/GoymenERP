/**
 * ORG-05 (KIM-S23, KIM-D28, DEC-ORG-005) — the active company's VKN and its maker-checker change flow.
 *
 * The read is `GET /organization/company/tax-id`; the two writes are the change request (preparer) and the decision
 * (another person). After every accepted command the view is re-read: the screen never predicts the new VKN, the
 * request's status or the next version.
 *
 * THE RULES THAT SHAPE THIS FILE
 *
 *  - Remounted per committed context and route entry, so one company's VKN is never shown under another's label;
 *    the decoder also refuses an answer about any company other than the active one.
 *  - The route stands on `organization.context.read`, but the VKN itself is answered only to a holder of one of the
 *    two tax-id names: anyone else sees the server's 403 as the forbidden block, never an empty card.
 *  - What the caller may do is the server's `viewer` statement, rendered and never derived. The preparer of the
 *    pending request never sees approve/reject; the server still refuses a self-decision (409).
 *  - ONE KEY PER SUBMITTED SNAPSHOT. A definite refusal closes the attempt; an UNKNOWN outcome keeps the key and the
 *    frozen command, so the only ways forward are the same-key resend or re-reading the view.
 *  - Typed work is dirty work: the shell warns before a company switch discards it.
 */

import { useCallback, useEffect, useId, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { fetchCompanyTaxId } from './api'
import type { CommandUnknown } from './commandApi'
import { ORGANIZATION_CAPABILITY } from './capabilities'
import {
  COMPANY_TAX_ID_LIMITS,
  TAX_ID_CHANGE_STATUS,
  TAX_ID_DECISION,
  isValidVknInput,
  type CompanyTaxId,
  type TaxIdChange,
  type TaxIdDecision,
} from './companyTaxIdContract'
import {
  sendCompanyTaxIdChangeRequest,
  sendCompanyTaxIdDecision,
  type CompanyTaxIdFailure,
  type DecideTaxIdChangeCommand,
  type RequestTaxIdChangeCommand,
} from './companyTaxIdApi'
import {
  COMPANY_TAX_ID_TEXT,
  taxIdDoneNotice,
  taxIdFailureText,
  taxIdRetryRefusedText,
  taxIdStatusLabel,
} from './companyTaxIdText'
import { conflictVersionsText, unknownText } from './organizationText'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useShellDispatch, useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import formStyles from './MasterRecordForm.module.css'
import styles from './CompanyTaxIdPage.module.css'

const REQUEST_DIRTY_SCOPE = 'organization.company.tax-id.request'
const DECISION_DIRTY_SCOPE = 'organization.company.tax-id.decision'

export function CompanyTaxIdPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return (
    <CompanyTaxIdScreen key={`${namespace ?? 'no-context'}::${shell.routeEntryGeneration}`} namespace={namespace} />
  )
}

type Done = { readonly kind: 'requested' | 'decided'; readonly replayed: boolean }

function CompanyTaxIdScreen({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const entryGeneration = shell.routeEntryGeneration
  const activeCompanyId = shell.activeContext?.companyId ?? null
  const scopeMode = shell.activeContext?.scopeMode ?? 'single'
  const singleCompanyContext = scopeMode === 'single' && activeCompanyId !== null
  const canOpen = hasCapability(shell, ORGANIZATION_CAPABILITY.companyTaxId)
  const [done, setDone] = useState<Done | null>(null)

  const query = useQuery({
    queryKey: [namespace ?? 'no-context', entryGeneration, 'organization-company-tax-id'],
    enabled: namespace !== null && singleCompanyContext && canOpen,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchCompanyTaxId(activeCompanyId ?? '', signal)
      // Only 401 goes to the boundary. A 403 here is the EXPECTED answer to a context.read holder without either
      // tax-id name, not a sign that the capability snapshot is stale: re-fencing on it would loop.
      if (outcome.type === 'http' && outcome.status === 401) {
        boundary.report(outcome.status, snapshot)
      }
      return outcome
    },
  })
  const refetch = query.refetch
  const reload = useCallback(async () => {
    await refetch()
  }, [refetch])
  const settled = useCallback(
    async (next: Done) => {
      await refetch()
      setDone(next)
    },
    [refetch],
  )

  if (!singleCompanyContext) {
    return (
      <Frame>
        <StateBlock state={{ kind: 'no-authorized-company' }} subject={COMPANY_TAX_ID_TEXT.contextSubject} />
      </Frame>
    )
  }

  if (namespace === null || !canOpen) {
    return (
      <Frame>
        <StateBlock state={{ kind: 'not-found' }} subject={COMPANY_TAX_ID_TEXT.subject} />
      </Frame>
    )
  }

  const state = (
    query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })
  ) as RemoteViewState<CompanyTaxId>

  if (state.kind !== 'ready') {
    return (
      <Frame>
        <StateBlock state={state} subject={COMPANY_TAX_ID_TEXT.subject} onRetry={() => void query.refetch()} />
      </Frame>
    )
  }

  const view = state.data
  const companyId = activeCompanyId ?? ''
  const doneNotice = done === null ? null : taxIdDoneNotice(done.kind, done.replayed)
  return (
    <Frame>
      {doneNotice === null ? null : (
        <div className={formStyles.noticeOk} role="status" data-outcome={doneNotice.outcome}>
          <p className={formStyles.noticeTitle}>{doneNotice.title}</p>
          <p>{doneNotice.detail}</p>
        </div>
      )}
      <TaxIdCard view={view} />
      {view.pending === null ? (
        view.viewer.canRequest ? (
          <RequestLauncher
            view={view}
            companyId={companyId}
            onOpen={() => setDone(null)}
            onDone={(replayed) => settled({ kind: 'requested', replayed })}
            onReload={reload}
          />
        ) : null
      ) : (
        <PendingPanel
          view={view}
          pending={view.pending}
          companyId={companyId}
          onDone={(replayed) => settled({ kind: 'decided', replayed })}
          onReload={reload}
        />
      )}
      {view.lastDecided === null ? null : <LastDecided change={view.lastDecided} />}
    </Frame>
  )
}

function Frame({ children }: { readonly children: React.ReactNode }) {
  return (
    <section aria-labelledby="org-tax-id-heading" className={styles.page}>
      <h1 id="org-tax-id-heading">{COMPANY_TAX_ID_TEXT.heading}</h1>
      <p className={styles.lead}>{COMPANY_TAX_ID_TEXT.lead}</p>
      {children}
    </section>
  )
}

function TaxIdCard({ view }: { readonly view: CompanyTaxId }) {
  return (
    <>
      <dl className={styles.card} data-company={view.companyId}>
        <div className={styles.field}>
          <dt>{COMPANY_TAX_ID_TEXT.code}</dt>
          <dd>{view.code}</dd>
        </div>
        <div className={styles.field}>
          <dt>{COMPANY_TAX_ID_TEXT.legalName}</dt>
          <dd>{view.legalName}</dd>
        </div>
        <div className={styles.field}>
          <dt>{COMPANY_TAX_ID_TEXT.vkn}</dt>
          <dd data-vkn={view.vkn ?? ''}>
            {view.vkn === null ? (
              <span className={styles.muted}>{COMPANY_TAX_ID_TEXT.noVkn}</span>
            ) : (
              <span className={styles.vkn}>{view.vkn}</span>
            )}
          </dd>
        </div>
      </dl>
      {view.isPlaceholder ? <p className={styles.muted}>{COMPANY_TAX_ID_TEXT.placeholderNote}</p> : null}
    </>
  )
}

function statusBadgeClass(change: TaxIdChange): string {
  if (change.status === TAX_ID_CHANGE_STATUS.approved) return styles.badgeApproved
  if (change.status === TAX_ID_CHANGE_STATUS.rejected) return styles.badgeRejected
  return styles.badgePending
}

function ChangeFacts({ change }: { readonly change: TaxIdChange }) {
  const decided = change.status !== TAX_ID_CHANGE_STATUS.pending
  return (
    <dl className={styles.card} data-change={change.id} data-status={change.status}>
      <div className={styles.field}>
        <dt>{COMPANY_TAX_ID_TEXT.status}</dt>
        <dd>
          {/* Status is never colour alone: the word is the signal and the colour is redundant. */}
          <span className={statusBadgeClass(change)}>{taxIdStatusLabel(change.status)}</span>
        </dd>
      </div>
      <div className={styles.field}>
        <dt>{COMPANY_TAX_ID_TEXT.currentVkn}</dt>
        <dd className={styles.vkn}>{change.currentVkn ?? COMPANY_TAX_ID_TEXT.none}</dd>
      </div>
      <div className={styles.field}>
        <dt>{COMPANY_TAX_ID_TEXT.proposedVkn}</dt>
        <dd className={styles.vkn}>{change.proposedVkn}</dd>
      </div>
      <div className={styles.field}>
        <dt>{COMPANY_TAX_ID_TEXT.reason}</dt>
        <dd>{change.reason}</dd>
      </div>
      <div className={styles.field}>
        <dt>{COMPANY_TAX_ID_TEXT.requestedBy}</dt>
        <dd>{change.requestedBy}</dd>
      </div>
      <div className={styles.field}>
        <dt>{COMPANY_TAX_ID_TEXT.requestedAt}</dt>
        <dd>
          <time dateTime={change.requestedAtUtc}>{change.requestedAtUtc}</time>
        </dd>
      </div>
      {decided ? (
        <>
          <div className={styles.field}>
            <dt>{COMPANY_TAX_ID_TEXT.decidedBy}</dt>
            <dd>{change.decidedBy ?? COMPANY_TAX_ID_TEXT.none}</dd>
          </div>
          <div className={styles.field}>
            <dt>{COMPANY_TAX_ID_TEXT.decidedAt}</dt>
            <dd>
              {change.decidedAtUtc === null ? (
                COMPANY_TAX_ID_TEXT.none
              ) : (
                <time dateTime={change.decidedAtUtc}>{change.decidedAtUtc}</time>
              )}
            </dd>
          </div>
          <div className={styles.field}>
            <dt>{COMPANY_TAX_ID_TEXT.decisionNote}</dt>
            <dd>{change.decisionNote ?? COMPANY_TAX_ID_TEXT.none}</dd>
          </div>
        </>
      ) : null}
    </dl>
  )
}

function LastDecided({ change }: { readonly change: TaxIdChange }) {
  const headingId = useId()
  return (
    <section aria-labelledby={headingId} className={styles.page} data-section="tax-id-last-decided">
      <h2 id={headingId} className={styles.subheading}>
        {COMPANY_TAX_ID_TEXT.lastDecidedHeading}
      </h2>
      <ChangeFacts change={change} />
    </section>
  )
}

/* ---------------------------------------------------------------- one command attempt ---------------------------- */

type SendResult =
  | { readonly type: 'done'; readonly replayed: boolean }
  | { readonly type: 'failed'; readonly failure: CompanyTaxIdFailure; readonly replayed: boolean }
  | { readonly type: 'unknown'; readonly unknown: CommandUnknown }

type Phase<C> =
  | { readonly kind: 'editing' }
  | { readonly kind: 'submitting'; readonly key: string; readonly command: C }
  | {
      readonly kind: 'unknown'
      readonly key: string
      readonly command: C
      readonly unknown: CommandUnknown
      readonly lastRefusal?: CompanyTaxIdFailure
    }
  | { readonly kind: 'failed'; readonly failure: CompanyTaxIdFailure }

/** One key per snapshot. A UUID is inside the server's 1..128 canonical length and has no comma. */
function newIdempotencyKey(): string {
  const webCrypto = globalThis.crypto
  if (typeof webCrypto?.randomUUID === 'function') return webCrypto.randomUUID()
  return `org-tax-id-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 12)}`
}

function useCommandAttempt<C>(
  dirtyScope: string,
  typedDirty: boolean,
  sendCommand: (key: string, command: C) => Promise<SendResult>,
  onDone: (replayed: boolean) => Promise<void>,
) {
  const shellDispatch = useShellDispatch()
  const boundary = useAuthBoundary()
  const [phase, setPhase] = useState<Phase<C>>({ kind: 'editing' })
  const noticeRef = useRef<HTMLDivElement>(null)
  const busy = phase.kind === 'submitting'
  const frozen = busy || phase.kind === 'unknown'
  const dirty = frozen || typedDirty

  useEffect(() => {
    shellDispatch({ type: dirty ? 'DIRTY_SET' : 'DIRTY_CLEAR', scope: dirtyScope })
  }, [dirty, dirtyScope, shellDispatch])
  useEffect(() => () => shellDispatch({ type: 'DIRTY_CLEAR', scope: dirtyScope }), [dirtyScope, shellDispatch])
  useEffect(() => {
    if (phase.kind === 'failed' || phase.kind === 'unknown') noticeRef.current?.focus()
  }, [phase])

  const send = useCallback(
    async (key: string, command: C, previous?: Phase<C>) => {
      setPhase({ kind: 'submitting', key, command })
      const security = boundary.capture()
      const outcome = await sendCommand(key, command)
      if (outcome.type === 'failed') {
        if (outcome.failure.kind === 'unauthenticated') boundary.report(401, security)
        if (outcome.failure.kind === 'forbidden') boundary.report(403, security)
      }
      if (outcome.type === 'done') {
        setPhase({ kind: 'editing' })
        shellDispatch({ type: 'DIRTY_CLEAR', scope: dirtyScope })
        await onDone(outcome.replayed)
        return
      }
      if (outcome.type === 'unknown') {
        setPhase({ kind: 'unknown', key, command, unknown: outcome.unknown })
        return
      }
      // A non-replayed refusal of a RESEND is about that request, not the first one: the attempt stays unknown.
      if (previous?.kind === 'unknown' && !outcome.replayed) {
        setPhase({ ...previous, lastRefusal: outcome.failure })
        return
      }
      setPhase({ kind: 'failed', failure: outcome.failure })
    },
    [boundary, dirtyScope, onDone, sendCommand, shellDispatch],
  )

  const start = useCallback(
    (command: C) => {
      if (frozen) return
      void send(newIdempotencyKey(), command)
    },
    [frozen, send],
  )
  const resend = useCallback(() => {
    if (phase.kind !== 'unknown') return
    void send(phase.key, phase.command, phase)
  }, [phase, send])
  const closeRefusal = useCallback(() => setPhase({ kind: 'editing' }), [])

  return { phase, busy, frozen, noticeRef, start, resend, closeRefusal }
}

function AttemptNotice<C>({
  phase,
  noticeRef,
  onResend,
  onReload,
}: {
  readonly phase: Phase<C>
  readonly noticeRef: React.RefObject<HTMLDivElement | null>
  readonly onResend: () => void
  readonly onReload: () => void
}) {
  if (phase.kind === 'failed') {
    const { title, detail } = taxIdFailureText(phase.failure)
    return (
      <div ref={noticeRef} tabIndex={-1} className={formStyles.noticeError} role="alert" data-outcome={phase.failure.kind}>
        <p className={formStyles.noticeTitle}>{title}</p>
        <p>{detail}</p>
        {phase.failure.kind === 'version-conflict' ? (
          <p className={formStyles.conflict}>
            {conflictVersionsText(
              phase.failure.conflict.expectedVersion,
              phase.failure.conflict.currentVersion,
              phase.failure.conflictIsCurrent,
            )}
          </p>
        ) : null}
        <button type="button" className={formStyles.secondary} onClick={onReload}>
          {COMPANY_TAX_ID_TEXT.reload}
        </button>
      </div>
    )
  }
  if (phase.kind !== 'unknown') return null
  const { title, detail } = unknownText(phase.unknown)
  return (
    <div ref={noticeRef} tabIndex={-1} className={formStyles.noticeWarn} role="alert" data-outcome="unknown">
      <p className={formStyles.noticeTitle}>{title}</p>
      <p>{detail}</p>
      {phase.lastRefusal === undefined ? null : (
        <p data-refusal={phase.lastRefusal.kind}>{taxIdRetryRefusedText(phase.lastRefusal)}</p>
      )}
      <div className={formStyles.actions}>
        {/* Same snapshot, same key — the server replays rather than writing twice. */}
        <button type="button" className={formStyles.primary} onClick={onResend}>
          {COMPANY_TAX_ID_TEXT.resend}
        </button>
        {/* Re-reads the view ONLY; the unresolved key and command are kept. */}
        <button type="button" className={formStyles.secondary} onClick={onReload}>
          {COMPANY_TAX_ID_TEXT.reload}
        </button>
      </div>
    </div>
  )
}

function FieldError({ id, text }: { readonly id: string; readonly text: string | undefined }) {
  if (text === undefined) return null
  return (
    <p id={id} className={formStyles.fieldError} role="alert">
      {text}
    </p>
  )
}

/* ---------------------------------------------------------------- the change request ----------------------------- */

function RequestLauncher({
  view,
  companyId,
  onOpen,
  onDone,
  onReload,
}: {
  readonly view: CompanyTaxId
  readonly companyId: string
  readonly onOpen: () => void
  readonly onDone: (replayed: boolean) => Promise<void>
  readonly onReload: () => Promise<void>
}) {
  const [open, setOpen] = useState(false)
  if (!open) {
    return (
      <section aria-label={COMPANY_TAX_ID_TEXT.openRequest} className={formStyles.section}>
        <button
          type="button"
          className={`${formStyles.secondary} ${formStyles.toggle}`}
          onClick={() => {
            onOpen()
            setOpen(true)
          }}
        >
          {COMPANY_TAX_ID_TEXT.openRequest}
        </button>
      </section>
    )
  }
  return (
    <RequestForm
      view={view}
      companyId={companyId}
      onDone={async (replayed) => {
        await onDone(replayed)
        setOpen(false)
      }}
      onReload={onReload}
      onCancel={() => setOpen(false)}
    />
  )
}

type RequestErrors = Partial<Record<'vkn' | 'reason', string>>

function RequestForm({
  view,
  companyId,
  onDone,
  onReload,
  onCancel,
}: {
  readonly view: CompanyTaxId
  readonly companyId: string
  readonly onDone: (replayed: boolean) => Promise<void>
  readonly onReload: () => Promise<void>
  readonly onCancel: () => void
}) {
  const baseId = useId()
  const [vkn, setVkn] = useState('')
  const [reason, setReason] = useState('')
  const [errors, setErrors] = useState<RequestErrors>({})
  const sendCommand = useCallback(
    async (key: string, command: RequestTaxIdChangeCommand): Promise<SendResult> => {
      const outcome = await sendCompanyTaxIdChangeRequest(companyId, key, command)
      return outcome.type === 'created' ? { type: 'done', replayed: outcome.replayed } : outcome
    },
    [companyId],
  )
  const attempt = useCommandAttempt(
    REQUEST_DIRTY_SCOPE,
    vkn.trim().length > 0 || reason.trim().length > 0,
    sendCommand,
    onDone,
  )
  const { phase, frozen, busy } = attempt

  const submit = (event: React.FormEvent) => {
    event.preventDefault()
    if (frozen) return
    const next: RequestErrors = {}
    const proposed = vkn.trim()
    if (!isValidVknInput(proposed)) next.vkn = COMPANY_TAX_ID_TEXT.vknInvalid
    else if (proposed === view.vkn) next.vkn = COMPANY_TAX_ID_TEXT.vknSame
    const trimmedReason = reason.trim()
    if (trimmedReason.length === 0) next.reason = COMPANY_TAX_ID_TEXT.reasonRequired
    else if (trimmedReason.length > COMPANY_TAX_ID_LIMITS.reason) next.reason = COMPANY_TAX_ID_TEXT.reasonTooLong
    setErrors(next)
    // A submit refused here mints NO key: nothing was attempted.
    if (Object.keys(next).length > 0) return
    // The version READ, never a predicted one.
    attempt.start({ expectedVersion: view.version, proposedVkn: proposed, reason })
  }

  const reloadAfterRefusal = () => {
    if (phase.kind === 'failed') attempt.closeRefusal()
    void onReload()
  }

  const fieldId = (field: string) => `${baseId}-${field}`
  return (
    <section className={formStyles.section} aria-labelledby={fieldId('heading')} data-section="tax-id-request">
      <h2 id={fieldId('heading')} className={formStyles.heading}>
        {COMPANY_TAX_ID_TEXT.requestHeading}
      </h2>
      <p className={formStyles.help} id={fieldId('help')}>
        {phase.kind === 'unknown' ? COMPANY_TAX_ID_TEXT.frozenHelp : COMPANY_TAX_ID_TEXT.requestHelp}
      </p>
      <AttemptNotice
        phase={phase}
        noticeRef={attempt.noticeRef}
        onResend={attempt.resend}
        onReload={reloadAfterRefusal}
      />
      <form className={formStyles.form} onSubmit={submit} noValidate aria-describedby={fieldId('help')}>
        <div className={formStyles.field}>
          <label className={formStyles.label} htmlFor={fieldId('vkn')}>
            {COMPANY_TAX_ID_TEXT.proposedVkn}
          </label>
          <input
            id={fieldId('vkn')}
            className={formStyles.input}
            value={vkn}
            inputMode="numeric"
            autoComplete="off"
            readOnly={frozen}
            aria-invalid={errors.vkn !== undefined}
            aria-describedby={errors.vkn === undefined ? fieldId('vkn-help') : fieldId('vkn-error')}
            onChange={(event) => setVkn(event.target.value)}
          />
          <p id={fieldId('vkn-help')} className={formStyles.help}>
            {COMPANY_TAX_ID_TEXT.proposedVknHelp}
          </p>
          <FieldError id={fieldId('vkn-error')} text={errors.vkn} />
        </div>
        <div className={`${formStyles.field} ${formStyles.wide}`}>
          <label className={formStyles.label} htmlFor={fieldId('reason')}>
            {COMPANY_TAX_ID_TEXT.reason}
          </label>
          <textarea
            id={fieldId('reason')}
            className={formStyles.textarea}
            value={reason}
            rows={2}
            maxLength={COMPANY_TAX_ID_LIMITS.reason}
            readOnly={frozen}
            aria-invalid={errors.reason !== undefined}
            aria-describedby={errors.reason === undefined ? undefined : fieldId('reason-error')}
            onChange={(event) => setReason(event.target.value)}
          />
          <FieldError id={fieldId('reason-error')} text={errors.reason} />
        </div>
        <div className={`${formStyles.actions} ${formStyles.wide}`}>
          {phase.kind === 'unknown' ? null : (
            <button type="submit" className={formStyles.primary} disabled={busy}>
              {COMPANY_TAX_ID_TEXT.submitRequest}
            </button>
          )}
          <button type="button" className={formStyles.secondary} onClick={onCancel} disabled={frozen}>
            {COMPANY_TAX_ID_TEXT.cancel}
          </button>
        </div>
      </form>
    </section>
  )
}

/* ---------------------------------------------------------------- the pending request and its decision ----------- */

function PendingPanel({
  view,
  pending,
  companyId,
  onDone,
  onReload,
}: {
  readonly view: CompanyTaxId
  readonly pending: TaxIdChange
  readonly companyId: string
  readonly onDone: (replayed: boolean) => Promise<void>
  readonly onReload: () => Promise<void>
}) {
  const headingId = useId()
  const { viewer } = view
  return (
    <section aria-labelledby={headingId} className={styles.page} data-section="tax-id-pending">
      <h2 id={headingId} className={styles.subheading}>
        {COMPANY_TAX_ID_TEXT.pendingHeading}
      </h2>
      <ChangeFacts change={pending} />
      {viewer.isRequesterOfPending ? (
        <p className={styles.muted} data-statement="own-request">
          {COMPANY_TAX_ID_TEXT.ownRequest}
        </p>
      ) : viewer.canApprove ? (
        <DecisionForm
          key={pending.id}
          view={view}
          pending={pending}
          companyId={companyId}
          onDone={onDone}
          onReload={onReload}
        />
      ) : (
        <p className={styles.muted} data-statement="awaiting-approver">
          {COMPANY_TAX_ID_TEXT.awaitingApprover}
        </p>
      )}
      {viewer.canRequest ? (
        <p className={styles.muted}>{COMPANY_TAX_ID_TEXT.requestNotAllowedWhilePending}</p>
      ) : null}
    </section>
  )
}

function DecisionForm({
  view,
  pending,
  companyId,
  onDone,
  onReload,
}: {
  readonly view: CompanyTaxId
  readonly pending: TaxIdChange
  readonly companyId: string
  readonly onDone: (replayed: boolean) => Promise<void>
  readonly onReload: () => Promise<void>
}) {
  const baseId = useId()
  const [note, setNote] = useState('')
  const [noteError, setNoteError] = useState<string | undefined>(undefined)
  const sendCommand = useCallback(
    async (key: string, command: DecideTaxIdChangeCommand): Promise<SendResult> => {
      const outcome = await sendCompanyTaxIdDecision(companyId, key, command)
      return outcome.type === 'decided' ? { type: 'done', replayed: outcome.replayed } : outcome
    },
    [companyId],
  )
  const attempt = useCommandAttempt(DECISION_DIRTY_SCOPE, note.trim().length > 0, sendCommand, onDone)
  const { phase, frozen, busy } = attempt

  const decide = (decision: TaxIdDecision) => {
    if (frozen) return
    if (note.trim().length > COMPANY_TAX_ID_LIMITS.note) {
      setNoteError(COMPANY_TAX_ID_TEXT.noteTooLong)
      return
    }
    setNoteError(undefined)
    attempt.start({ requestId: pending.id, expectedVersion: view.version, decision, note })
  }

  const reloadAfterRefusal = () => {
    if (phase.kind === 'failed') attempt.closeRefusal()
    void onReload()
  }

  const fieldId = (field: string) => `${baseId}-${field}`
  return (
    <section className={formStyles.section} aria-labelledby={fieldId('heading')} data-section="tax-id-decision">
      <h3 id={fieldId('heading')} className={formStyles.heading}>
        {COMPANY_TAX_ID_TEXT.decisionHeading}
      </h3>
      <p className={formStyles.help} id={fieldId('help')}>
        {phase.kind === 'unknown' ? COMPANY_TAX_ID_TEXT.frozenHelp : COMPANY_TAX_ID_TEXT.decisionHelp}
      </p>
      <AttemptNotice
        phase={phase}
        noticeRef={attempt.noticeRef}
        onResend={attempt.resend}
        onReload={reloadAfterRefusal}
      />
      <div className={formStyles.field}>
        <label className={formStyles.label} htmlFor={fieldId('note')}>
          {COMPANY_TAX_ID_TEXT.note}
        </label>
        <textarea
          id={fieldId('note')}
          className={formStyles.textarea}
          value={note}
          rows={2}
          maxLength={COMPANY_TAX_ID_LIMITS.note}
          readOnly={frozen}
          aria-invalid={noteError !== undefined}
          aria-describedby={noteError === undefined ? undefined : fieldId('note-error')}
          onChange={(event) => setNote(event.target.value)}
        />
        <FieldError id={fieldId('note-error')} text={noteError} />
      </div>
      {phase.kind === 'unknown' ? null : (
        <div className={formStyles.actions}>
          <button
            type="button"
            className={formStyles.primary}
            disabled={busy}
            onClick={() => decide(TAX_ID_DECISION.approve)}
          >
            {COMPANY_TAX_ID_TEXT.approve}
          </button>
          <button
            type="button"
            className={formStyles.secondary}
            disabled={busy}
            onClick={() => decide(TAX_ID_DECISION.reject)}
          >
            {COMPANY_TAX_ID_TEXT.reject}
          </button>
        </div>
      )}
    </section>
  )
}
