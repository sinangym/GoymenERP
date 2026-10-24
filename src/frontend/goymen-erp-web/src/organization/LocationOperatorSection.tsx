/**
 * O02 — the operator of an external location: who runs it, and the authorized user's reasoned (re)assignment.
 *
 * THE RULES THAT SHAPE THIS FILE (the master edit's and the active-state dialog's, ORG-02):
 *
 *  - Only an EXTERNAL location has an operator (the server refuses the write for an internal one), so the caller mounts
 *    this section for external locations only.
 *  - The display is the server's own read; the screen never derives "who operates it" from anything else, and an
 *    operator the server marks `isCurrentlyEligible: false` is shown with that fact, never hidden.
 *  - Candidates come only from the server's candidate search (bounded pages, opaque cursor, no page size sent). The
 *    screen decides no eligibility of its own.
 *  - ONE KEY PER SUBMITTED SNAPSHOT. A definite refusal closes the attempt and the next submit is a new command with a
 *    new key; an UNKNOWN outcome freezes the choice and keeps the key, so the only ways on are the same-key resend or
 *    re-reading the card. The coded "Cari check unavailable" 503 recorded nothing and also keeps the key.
 *  - A 204 is not a state: after it the card and the operator are re-read; nothing is predicted.
 *  - 401/403 go to the shared auth boundary, captured before the request.
 *
 * Capability: the change control stands on `organization.locations.edit` (the location edit's own capability), because
 * the write and the candidate search require the same `organization.locations.write` permission
 * (ASSUMPTION-O02-OPERATOR-CAPABILITY; a separate capability would be a backend declaration and a K3 shell change).
 */

import { useCallback, useEffect, useId, useRef, useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import type { CommandUnknown } from './commandApi'
import type { LocationSummary } from './contract'
import {
  INVALID_CURSOR,
  fetchLocationOperator,
  fetchOperatorCandidates,
  setLocationOperator,
  type OperatorFailure,
  type OperatorOutcome,
  type SetOperatorPayload,
} from './operatorApi'
import {
  OPERATOR_SEARCH_MAX_LENGTH,
  type LocationOperatorContext,
  type OperatorCandidate,
  type OperatorCandidatePage,
  type OperatorDisplay,
} from './operatorContract'
import {
  CANCEL_LABEL,
  OPERATOR_TEXT,
  REASON_LABEL,
  REASON_MAX_LENGTH,
  REASON_REQUIRED,
  REASON_TOO_LONG,
  conflictVersionsText,
  failureText,
  savedNotice,
  unknownText,
} from './organizationText'
import { classifyOutcome, type RemoteViewState, type ViewOutcome } from '../shell/uiState'
import { useShellDispatch, useShellState } from '../shell/shellContext'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import styles from './LocationOperatorSection.module.css'

const DIRTY_SCOPE = 'organization.location.operator'

function newIdempotencyKey(): string {
  const webCrypto = globalThis.crypto
  if (typeof webCrypto?.randomUUID === 'function') return webCrypto.randomUUID()
  return `org-operator-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 12)}`
}

/** Runs one read through the auth boundary, so a real 401/403 is reported like every other Organization read. */
function useBoundaryRead<T>(queryKey: readonly unknown[], enabled: boolean, read: (signal: AbortSignal) => Promise<ViewOutcome<T>>) {
  const boundary = useAuthBoundary()
  return useQuery({
    queryKey,
    enabled,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await read(signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, snapshot)
      }
      return outcome
    },
  })
}

export function LocationOperatorSection({
  namespace,
  location,
  companyId,
  canAssign,
  onRefresh,
}: {
  readonly namespace: string
  readonly location: LocationSummary
  readonly companyId: string
  readonly canAssign: boolean
  /** Re-reads the location card (its version moves with an assignment). */
  readonly onRefresh: () => Promise<void>
}) {
  const shell = useShellState()
  const [open, setOpen] = useState(false)
  const [saved, setSaved] = useState<null | { readonly replayed: boolean }>(null)
  const query = useBoundaryRead(
    [namespace, shell.routeEntryGeneration, 'organization-location-operator', location.id],
    true,
    (signal) => fetchLocationOperator(companyId, location.id, signal),
  )
  const state = (
    query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })
  ) as RemoteViewState<LocationOperatorContext>
  const refetch = query.refetch

  const reloadAll = useCallback(async () => {
    await Promise.all([onRefresh(), refetch()])
  }, [onRefresh, refetch])

  const current = state.kind === 'ready' ? state.data.operator : null
  const notice = saved === null ? null : savedNotice(saved.replayed)

  return (
    <section aria-labelledby="org-location-operator-heading" className={styles.section} data-section="location-operator">
      <h2 id="org-location-operator-heading">{OPERATOR_TEXT.heading}</h2>
      <p className={styles.muted}>{OPERATOR_TEXT.lead}</p>
      {state.kind === 'ready' ? (
        <OperatorCard operator={state.data.operator} />
      ) : (
        <StateBlock state={state} subject={OPERATOR_TEXT.subject} onRetry={() => void refetch()} />
      )}
      {notice !== null ? (
        <div className={styles.notice} role="status" data-outcome={notice.outcome}>
          <p className={styles.noticeTitle}>{notice.title}</p>
          <p>{notice.detail}</p>
        </div>
      ) : null}
      {canAssign && state.kind === 'ready' ? (
        open ? (
          <OperatorAssignForm
            namespace={namespace}
            location={location}
            companyId={companyId}
            current={current}
            onSaved={async (replayed) => {
              await reloadAll()
              setSaved({ replayed })
              setOpen(false)
            }}
            onReload={reloadAll}
            onCancel={() => setOpen(false)}
          />
        ) : (
          <div className={styles.actions}>
            <Button
              type="button"
              variant="secondary"
              onClick={() => {
                setSaved(null)
                setOpen(true)
              }}
            >
              {current === null ? OPERATOR_TEXT.assign : OPERATOR_TEXT.change}
            </Button>
          </div>
        )
      ) : null}
    </section>
  )
}

function partyName(party: { readonly legalName: string; readonly tradeName: string | null }): string {
  return party.tradeName === null ? party.legalName : `${party.legalName} (${party.tradeName})`
}

function OperatorCard({ operator }: { readonly operator: OperatorDisplay | null }) {
  if (operator === null) {
    return (
      <p className={styles.muted} data-operator="none">
        {OPERATOR_TEXT.none}
      </p>
    )
  }
  return (
    <>
      <dl className={styles.card} data-operator={operator.partyId}>
        <div>
          <dt>{OPERATOR_TEXT.code}</dt>
          <dd className={styles.code}>{operator.code}</dd>
        </div>
        <div>
          <dt>{OPERATOR_TEXT.legalName}</dt>
          <dd>{operator.legalName}</dd>
        </div>
        {operator.tradeName === null ? null : (
          <div>
            <dt>{OPERATOR_TEXT.tradeName}</dt>
            <dd>{operator.tradeName}</dd>
          </div>
        )}
      </dl>
      {operator.isCurrentlyEligible ? null : (
        <div>
          <span className={styles.badgeWarn} data-eligible="false">
            {OPERATOR_TEXT.ineligible}
          </span>
          <p className={styles.muted}>{OPERATOR_TEXT.ineligibleDetail}</p>
        </div>
      )}
    </>
  )
}

type Phase =
  | { readonly kind: 'editing' }
  | { readonly kind: 'submitting'; readonly key: string; readonly payload: SetOperatorPayload }
  | {
      readonly kind: 'unknown'
      readonly key: string
      readonly payload: SetOperatorPayload
      readonly unknown: CommandUnknown | 'dependency'
      readonly lastRefusal?: OperatorFailure
    }
  | { readonly kind: 'failed'; readonly failure: OperatorFailure }

interface CandidateQueryState {
  readonly q: string
  readonly cursor: string | null
}

function OperatorAssignForm({
  namespace,
  location,
  companyId,
  current,
  onSaved,
  onReload,
  onCancel,
}: {
  readonly namespace: string
  readonly location: LocationSummary
  readonly companyId: string
  readonly current: OperatorDisplay | null
  readonly onSaved: (replayed: boolean) => Promise<void>
  readonly onReload: () => Promise<void>
  readonly onCancel: () => void
}) {
  const shell = useShellState()
  const shellDispatch = useShellDispatch()
  const boundary = useAuthBoundary()
  const baseId = useId()
  const [searchText, setSearchText] = useState('')
  const [searchError, setSearchError] = useState<string | null>(null)
  const [candidateQuery, setCandidateQuery] = useState<CandidateQueryState>({ q: '', cursor: null })
  const [picked, setPicked] = useState<OperatorCandidate | null>(null)
  const [reason, setReason] = useState('')
  const [errors, setErrors] = useState<{ readonly pick?: string; readonly reason?: string }>({})
  const [phase, setPhase] = useState<Phase>({ kind: 'editing' })
  const noticeRef = useRef<HTMLDivElement>(null)
  const searchRef = useRef<HTMLInputElement>(null)

  const busy = phase.kind === 'submitting'
  const frozen = busy || phase.kind === 'unknown'
  const dirty = frozen || picked !== null || reason.trim().length > 0

  useEffect(() => {
    shellDispatch({ type: dirty ? 'DIRTY_SET' : 'DIRTY_CLEAR', scope: DIRTY_SCOPE })
  }, [dirty, shellDispatch])
  useEffect(() => () => shellDispatch({ type: 'DIRTY_CLEAR', scope: DIRTY_SCOPE }), [shellDispatch])
  useEffect(() => {
    searchRef.current?.focus()
  }, [])
  useEffect(() => {
    if (phase.kind === 'failed' || phase.kind === 'unknown') noticeRef.current?.focus()
  }, [phase])

  const candidates = useBoundaryRead(
    [
      namespace,
      shell.routeEntryGeneration,
      'organization-location-operator-candidates',
      location.id,
      candidateQuery.q,
      candidateQuery.cursor,
    ],
    true,
    (signal) => fetchOperatorCandidates(companyId, location.id, candidateQuery, signal),
  )
  const candidateOutcome = candidates.data
  const invalidCursor =
    candidateOutcome?.type === 'http' &&
    candidateOutcome.status === 400 &&
    typeof candidateOutcome.body === 'object' &&
    candidateOutcome.body !== null &&
    (candidateOutcome.body as Record<string, unknown>).code === INVALID_CURSOR
  const candidateState = (
    candidates.isPending ? { kind: 'initial-loading' } : classifyOutcome(candidateOutcome ?? { type: 'malformed' })
  ) as RemoteViewState<OperatorCandidatePage>

  function search(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (frozen) return
    const q = searchText.trim()
    if (q.length > OPERATOR_SEARCH_MAX_LENGTH) {
      setSearchError(OPERATOR_TEXT.searchTooLong(OPERATOR_SEARCH_MAX_LENGTH))
      return
    }
    setSearchError(null)
    setPicked(null)
    setCandidateQuery({ q, cursor: null })
  }

  const settle = useCallback(
    async (outcome: OperatorOutcome, key: string, payload: SetOperatorPayload, previous?: Phase) => {
      if (outcome.type === 'applied') {
        setPhase({ kind: 'editing' })
        shellDispatch({ type: 'DIRTY_CLEAR', scope: DIRTY_SCOPE })
        await onSaved(outcome.replayed)
        return
      }
      if (outcome.type === 'unknown') {
        setPhase({ kind: 'unknown', key, payload, unknown: outcome.unknown })
        return
      }
      if (outcome.type === 'dependency-unavailable') {
        setPhase({ kind: 'unknown', key, payload, unknown: 'dependency' })
        return
      }
      // A non-replayed refusal on a resend is about THAT request, not about whether the first one was recorded.
      if (previous?.kind === 'unknown' && !outcome.replayed) {
        setPhase({ ...previous, lastRefusal: outcome.failure })
        return
      }
      setPhase({ kind: 'failed', failure: outcome.failure })
    },
    [onSaved, shellDispatch],
  )

  const send = useCallback(
    async (key: string, payload: SetOperatorPayload, previous?: Phase) => {
      setPhase({ kind: 'submitting', key, payload })
      const security = boundary.capture()
      const outcome = await setLocationOperator(companyId, key, payload)
      if (outcome.type === 'failed') {
        if (outcome.failure.kind === 'unauthenticated') boundary.report(401, security)
        if (outcome.failure.kind === 'forbidden') boundary.report(403, security)
      }
      await settle(outcome, key, payload, previous)
    },
    [boundary, companyId, settle],
  )

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (frozen) return
    const trimmed = reason.trim()
    const next: { pick?: string; reason?: string } = {}
    if (picked === null) next.pick = OPERATOR_TEXT.pickRequired
    if (trimmed.length === 0) next.reason = REASON_REQUIRED
    else if (trimmed.length > REASON_MAX_LENGTH) next.reason = REASON_TOO_LONG
    setErrors(next)
    if (picked === null || next.reason !== undefined) return
    void send(newIdempotencyKey(), {
      locationId: location.id,
      operatorPartyId: picked.partyId,
      // The version READ with the card at submit time; after a reload this is the fresh one.
      expectedVersion: location.version,
      reason: trimmed,
    })
  }

  const radioName = `${baseId}-operator`

  return (
    <div className={styles.form}>
      <h3>{OPERATOR_TEXT.formHeading}</h3>
      <form role="search" aria-label={OPERATOR_TEXT.searchFormLabel} onSubmit={search} noValidate>
        <div className={styles.row}>
          <Field
            className={styles.grow}
            label={OPERATOR_TEXT.searchLabel}
            help={OPERATOR_TEXT.searchHelp}
            error={searchError ?? undefined}
          >
            {(control) => (
              <input
                {...control}
                ref={searchRef}
                className={styles.input}
                type="search"
                autoComplete="off"
                value={searchText}
                disabled={frozen}
                onChange={(event) => setSearchText(event.target.value)}
              />
            )}
          </Field>
          <Button type="submit" variant="secondary" disabled={frozen}>
            {OPERATOR_TEXT.search}
          </Button>
        </div>
      </form>

      <form className={styles.form} onSubmit={submit} noValidate>
        {invalidCursor ? (
          <div className={styles.notice} role="alert" data-state="invalid-cursor">
            <p>{OPERATOR_TEXT.invalidCursor}</p>
            <div className={styles.actions}>
              <Button type="button" variant="secondary" onClick={() => setCandidateQuery({ q: candidateQuery.q, cursor: null })}>
                {OPERATOR_TEXT.firstPage}
              </Button>
            </div>
          </div>
        ) : candidateState.kind !== 'ready' ? (
          <StateBlock
            state={candidateState}
            subject={OPERATOR_TEXT.candidatesSubject}
            onRetry={() => void candidates.refetch()}
          />
        ) : candidateState.data.items.length === 0 ? (
          <p className={styles.muted} role="status" data-state="no-candidates">
            {OPERATOR_TEXT.noCandidates}
          </p>
        ) : (
          <fieldset
            className={styles.choices}
            aria-describedby={errors.pick === undefined ? undefined : `${baseId}-pick-error`}
            disabled={frozen}
          >
            <legend>{OPERATOR_TEXT.candidatesLegend}</legend>
            {candidateState.data.items.map((candidate) => (
              <label key={candidate.partyId} className={styles.choice} data-candidate={candidate.partyId}>
                <input
                  type="radio"
                  name={radioName}
                  value={candidate.partyId}
                  checked={picked?.partyId === candidate.partyId}
                  onChange={() => setPicked(candidate)}
                />
                <span>
                  <span className={styles.code}>{candidate.code}</span> — {partyName(candidate)}
                  {current !== null && current.partyId.toLowerCase() === candidate.partyId.toLowerCase()
                    ? ` ${OPERATOR_TEXT.current}`
                    : null}
                </span>
              </label>
            ))}
          </fieldset>
        )}
        {errors.pick === undefined ? null : (
          <p className={styles.error} id={`${baseId}-pick-error`}>
            {errors.pick}
          </p>
        )}
        {candidateState.kind === 'ready' && (candidateQuery.cursor !== null || candidateState.data.nextCursor !== null) ? (
          <div className={styles.actions}>
            {candidateQuery.cursor === null ? null : (
              <Button
                type="button"
                variant="ghost"
                disabled={frozen}
                onClick={() => setCandidateQuery({ q: candidateQuery.q, cursor: null })}
              >
                {OPERATOR_TEXT.firstPage}
              </Button>
            )}
            {candidateState.data.nextCursor === null ? null : (
              <Button
                type="button"
                variant="ghost"
                disabled={frozen}
                onClick={() => setCandidateQuery({ q: candidateQuery.q, cursor: candidateState.data.nextCursor })}
              >
                {OPERATOR_TEXT.nextPage}
              </Button>
            )}
          </div>
        ) : null}

        <Field label={REASON_LABEL} error={errors.reason}>
          {(control) => (
            <textarea
              {...control}
              className={styles.textarea}
              value={reason}
              maxLength={REASON_MAX_LENGTH + 100}
              disabled={frozen}
              onChange={(event) => setReason(event.target.value)}
            />
          )}
        </Field>

        <PhaseNotice phase={phase} noticeRef={noticeRef} />

        <div className={styles.actions}>
          {phase.kind === 'unknown' ? (
            <>
              <Button type="button" variant="primary" onClick={() => void send(phase.key, phase.payload, phase)}>
                {OPERATOR_TEXT.resend}
              </Button>
              <Button type="button" variant="secondary" onClick={() => void onReload().then(onCancel)}>
                {OPERATOR_TEXT.reload}
              </Button>
            </>
          ) : (
            <>
              <Button type="submit" variant="primary" disabled={busy}>
                {OPERATOR_TEXT.save}
              </Button>
              {phase.kind === 'failed' && phase.failure.kind === 'version-conflict' ? (
                <Button
                  type="button"
                  variant="secondary"
                  onClick={() => {
                    void onReload().then(() => setPhase({ kind: 'editing' }))
                  }}
                >
                  {OPERATOR_TEXT.reload}
                </Button>
              ) : null}
              <Button type="button" variant="ghost" disabled={busy} onClick={onCancel}>
                {CANCEL_LABEL}
              </Button>
            </>
          )}
        </div>
      </form>
    </div>
  )
}

function refusalText(failure: OperatorFailure): { title: string; detail: string } {
  if (failure.kind === 'ineligible') return { title: OPERATOR_TEXT.ineligibleTitle, detail: OPERATOR_TEXT.ineligibleRefusal }
  if (failure.kind === 'forbidden') return { title: failureText(failure).title, detail: OPERATOR_TEXT.forbiddenDetail }
  return failureText(failure)
}

function PhaseNotice({ phase, noticeRef }: { readonly phase: Phase; readonly noticeRef: React.RefObject<HTMLDivElement | null> }) {
  if (phase.kind === 'failed') {
    const text = refusalText(phase.failure)
    return (
      <div ref={noticeRef} tabIndex={-1} className={styles.notice} role="alert" data-outcome={`refused-${phase.failure.kind}`}>
        <p className={styles.noticeTitle}>{text.title}</p>
        <p>{text.detail}</p>
        {phase.failure.kind === 'version-conflict' ? (
          <p>
            {conflictVersionsText(
              phase.failure.conflict.expectedVersion,
              phase.failure.conflict.currentVersion,
              phase.failure.conflictIsCurrent,
            )}
          </p>
        ) : null}
      </div>
    )
  }
  if (phase.kind === 'unknown') {
    const text =
      phase.unknown === 'dependency'
        ? { title: OPERATOR_TEXT.dependencyTitle, detail: OPERATOR_TEXT.dependencyDetail }
        : unknownText(phase.unknown)
    return (
      <div
        ref={noticeRef}
        tabIndex={-1}
        className={styles.notice}
        role="alert"
        data-outcome={phase.unknown === 'dependency' ? 'dependency-unavailable' : 'unknown'}
      >
        <p className={styles.noticeTitle}>{text.title}</p>
        <p>{text.detail}</p>
        {phase.lastRefusal === undefined ? null : <p>{refusalText(phase.lastRefusal).detail}</p>}
      </div>
    )
  }
  return null
}
