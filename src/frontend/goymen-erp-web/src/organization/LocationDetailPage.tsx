/**
 * ORG-02 — one location's card, and the authorized user's reasoned activate/deactivate.
 *
 * The slice ends here: list → card → justified state change → CURRENT view.
 *
 * THE RULE THAT SHAPES THIS FILE: a 204 is not a state.
 *
 * `PUT .../active-state` answers 204 with no body, no DTO and no version. A REPLAYED
 * 204 is the server repeating an earlier command's terminal answer, which is not a
 * claim about the record right now — an old deactivation replayed after someone
 * reactivated the location still answers 204. And a same-state command records a
 * NoChange audit entry while leaving the version untouched, so `version + 1` is not
 * something a client may predict either.
 *
 * Therefore this screen NEVER derives the resulting state from the command. After any
 * accepted command it re-READS the location and invalidates the list page behind it.
 * The notice says the command was accepted; the card says what the location is. Two
 * different facts, never merged.
 *
 * The change is confirmed in a MODAL because it is short and single-focus
 * (UI_DESIGN_SYSTEM §4) and because the target state deserves to be stated as a fact
 * before it is committed, not implied by a verb on a button.
 */

import { useCallback, useEffect, useReducer, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { fetchLocation } from './api'
import { setLocationActiveState, type CommandContext } from './commandApi'
import type { LocationSummary } from './contract'
import {
  attemptFingerprint,
  attemptReducer,
  initialAttemptState,
  type AttemptSnapshot,
  type AttemptState,
} from './activeStateAttempt'
import { ORGANIZATION_CAPABILITY } from './capabilities'
import {
  APPLIED_DETAIL,
  APPLIED_TITLE,
  CANCEL_LABEL,
  REASON_LABEL,
  REASON_MAX_LENGTH,
  REASON_REQUIRED,
  REASON_TOO_LONG,
  REPLAYED_DETAIL,
  REPLAYED_TITLE,
  actionHeading,
  activeStateLabel,
  confirmActionLabel,
  conflictVersionsText,
  externalLabel,
  failureText,
  targetStateSummary,
  unknownText,
} from './organizationText'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useShellDispatch, useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import styles from './LocationDetailPage.module.css'

/** Registered with the shell so a company switch with an unsent draft warns first. */
const DIRTY_SCOPE = 'organization.location.active-state'

/**
 * One key per snapshot. A UUID is comfortably inside the server's 1..128 canonical
 * length and contains no comma, which the endpoint rejects.
 */
function newIdempotencyKey(): string {
  const webCrypto = globalThis.crypto
  if (typeof webCrypto?.randomUUID === 'function') return webCrypto.randomUUID()
  return `org-loc-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 12)}`
}

export function LocationDetailPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const { locationId } = useParams()
  /**
   * Remounted per context AND per location. A detail screen is the dangerous one: the
   * location's name sits in the heading, so a stale render would put one company's
   * location under another company's label — and would leave a half-typed
   * justification and a frozen `expectedVersion` attached to a record that is no
   * longer the one on screen.
   */
  return (
    <LocationDetail
      key={`${namespace ?? 'no-context'}::${locationId ?? ''}`}
      namespace={namespace}
    />
  )
}

function LocationDetail({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const queryClient = useQueryClient()
  const { locationId } = useParams()
  const entryGeneration = shell.routeEntryGeneration
  const activeCompanyId = shell.activeContext?.companyId ?? null
  const scopeMode = shell.activeContext?.scopeMode ?? 'single'
  const singleCompanyContext = scopeMode === 'single' && activeCompanyId !== null
  const canChangeStatus = hasCapability(shell, ORGANIZATION_CAPABILITY.locationStatus)

  const query = useQuery({
    queryKey: [
      namespace ?? 'no-context',
      entryGeneration,
      'organization-location',
      locationId ?? '',
    ],
    enabled: namespace !== null && singleCompanyContext && (locationId ?? '').length > 0,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchLocation(activeCompanyId ?? '', locationId ?? '', signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, snapshot)
      }
      return outcome
    },
  })

  const refetch = query.refetch
  /**
   * Re-read this record AND drop the list page behind it.
   *
   * Business asks for the change to be visible "on the same list page", and the list
   * lives under the production 30 s `staleTime` — so without this, navigating back
   * would paint the pre-change rows from cache. Invalidation is scoped to this
   * context's namespace so nothing outside it is disturbed.
   */
  const refresh = useCallback(async () => {
    await refetch()
    if (namespace === null) return
    await queryClient.invalidateQueries({
      predicate: (item) =>
        item.queryKey[0] === namespace && item.queryKey[2] === 'organization-locations',
    })
  }, [namespace, queryClient, refetch])

  const state = (
    query.isPending
      ? { kind: 'initial-loading' }
      : classifyOutcome(query.data ?? { type: 'malformed' })
  ) as RemoteViewState<LocationSummary>

  if (!singleCompanyContext) {
    return (
      <Frame>
        <StateBlock
          state={{ kind: 'no-authorized-company' }}
          subject="lokasyon kartı için tek şirket bağlamı"
        />
      </Frame>
    )
  }

  if (state.kind !== 'ready') {
    return (
      <Frame>
        <StateBlock state={state} subject="lokasyon" onRetry={() => void query.refetch()} />
      </Frame>
    )
  }

  return (
    <Frame>
      <LocationCard location={state.data} />
      {canChangeStatus ? (
        <ActiveStateSection
          location={state.data}
          companyId={activeCompanyId ?? ''}
          binding={namespace ?? 'no-context'}
          refreshing={query.isFetching}
          onRefresh={refresh}
        />
      ) : null}
    </Frame>
  )
}

function Frame({ children }: { readonly children: React.ReactNode }) {
  return (
    <section aria-labelledby="org-location-heading" className={styles.page}>
      <nav aria-label="Geri" className={styles.breadcrumb}>
        <Link to="/ayarlar/organizasyon">← Organizasyon</Link>
      </nav>
      <h1 id="org-location-heading">Lokasyon</h1>
      {children}
    </section>
  )
}

function LocationCard({ location }: { readonly location: LocationSummary }) {
  return (
    <dl className={styles.card}>
      <div className={styles.field}>
        <dt>Kod</dt>
        <dd>{location.code}</dd>
      </div>
      <div className={styles.field}>
        <dt>Ad</dt>
        <dd>{location.name}</dd>
      </div>
      <div className={styles.field}>
        <dt>Tür</dt>
        <dd>{externalLabel(location.isExternal)}</dd>
      </div>
      <div className={styles.field}>
        <dt>Durum</dt>
        <dd>
          <span
            className={location.isActive ? styles.badgeActive : styles.badgePassive}
            data-state={location.isActive ? 'active' : 'passive'}
          >
            {activeStateLabel(location.isActive)}
          </span>
        </dd>
      </div>
    </dl>
  )
}

function ActiveStateSection({
  location,
  companyId,
  binding,
  refreshing,
  onRefresh,
}: {
  readonly location: LocationSummary
  readonly companyId: string
  readonly binding: string
  readonly refreshing: boolean
  readonly onRefresh: () => Promise<void>
}) {
  const [attempt, dispatch] = useReducer(attemptReducer, initialAttemptState)
  const [open, setOpen] = useState(false)
  const triggerRef = useRef<HTMLButtonElement>(null)
  const successRef = useRef<HTMLDivElement>(null)

  /**
   * The target is always the OPPOSITE of what the FRESH read says, recomputed on every
   * render rather than frozen when the dialog opened. A refresh showing that someone
   * else already flipped the record retargets the action instead of silently
   * re-submitting a stale intent.
   */
  const targetActive = !location.isActive

  const close = useCallback(() => {
    setOpen(false)
    triggerRef.current?.focus()
  }, [])

  /**
   * DERIVED, not closed by an effect. An accepted command ends the dialog, so that is
   * a function of the attempt phase — writing it back into state from an effect would
   * schedule a second render for something already knowable during the first.
   */
  const settled = attempt.phase === 'applied'
  const dialogOpen = open && !settled

  // The effect does only what an effect is for: moving focus, an external system.
  useEffect(() => {
    if (settled) successRef.current?.focus()
  }, [settled])

  return (
    <section aria-labelledby="org-status-heading" className={styles.actionSection}>
      <h2 id="org-status-heading" className={styles.sectionHeading}>
        Durum yönetimi
      </h2>

      {attempt.phase === 'applied' ? (
        <AppliedNotice
          replayed={attempt.replayed}
          refreshing={refreshing}
          noticeRef={successRef}
          onDismiss={() => {
            dispatch({ type: 'RESET' })
            setOpen(false)
          }}
        />
      ) : (
        <p className={styles.help}>
          Durum değişikliği gerekçe ister ve audit kaydına yazılır.
        </p>
      )}

      <button
        ref={triggerRef}
        type="button"
        className={styles.primary}
        onClick={() => {
          dispatch({ type: 'RESET' })
          setOpen(true)
        }}
      >
        {confirmActionLabel(location.isActive)}
      </button>

      {dialogOpen ? (
        <ActiveStateDialog
          location={location}
          companyId={companyId}
          binding={binding}
          targetActive={targetActive}
          attempt={attempt}
          dispatch={dispatch}
          onClose={close}
          onRefresh={onRefresh}
        />
      ) : null}
    </section>
  )
}

function AppliedNotice({
  replayed,
  refreshing,
  noticeRef,
  onDismiss,
}: {
  readonly replayed: boolean
  readonly refreshing: boolean
  readonly noticeRef: React.RefObject<HTMLDivElement | null>
  readonly onDismiss: () => void
}) {
  return (
    <div
      ref={noticeRef}
      tabIndex={-1}
      className={styles.noticeOk}
      role="status"
      aria-live="polite"
      data-outcome={replayed ? 'replayed' : 'applied'}
    >
      <p className={styles.noticeTitle}>{replayed ? REPLAYED_TITLE : APPLIED_TITLE}</p>
      <p>{refreshing ? 'Güncel kayıt yükleniyor…' : replayed ? REPLAYED_DETAIL : APPLIED_DETAIL}</p>
      <button type="button" className={styles.secondary} onClick={onDismiss}>
        Kapat
      </button>
    </div>
  )
}

function ActiveStateDialog({
  location,
  companyId,
  binding,
  targetActive,
  attempt,
  dispatch,
  onClose,
  onRefresh,
}: {
  readonly location: LocationSummary
  readonly companyId: string
  readonly binding: string
  readonly targetActive: boolean
  readonly attempt: AttemptState
  readonly dispatch: React.Dispatch<Parameters<typeof attemptReducer>[1]>
  readonly onClose: () => void
  readonly onRefresh: () => Promise<void>
}) {
  const shellDispatch = useShellDispatch()
  const [reason, setReason] = useState('')
  const [reasonError, setReasonError] = useState<string | null>(null)
  const reasonRef = useRef<HTMLTextAreaElement>(null)
  const dialogRef = useRef<HTMLDivElement>(null)
  const noticeRef = useRef<HTMLDivElement>(null)

  const busy = attempt.phase === 'submitting'
  const unresolved = attempt.phase === 'unknown'

  // The draft is dirty work: a company switch must warn before discarding it
  // (ERP-000K §13.1). Registered with the SHARED scope mechanism, not a local flag.
  const dirty = reason.trim().length > 0 || busy || unresolved
  useEffect(() => {
    shellDispatch({ type: dirty ? 'DIRTY_SET' : 'DIRTY_CLEAR', scope: DIRTY_SCOPE })
  }, [dirty, shellDispatch])
  useEffect(() => () => shellDispatch({ type: 'DIRTY_CLEAR', scope: DIRTY_SCOPE }), [shellDispatch])

  useEffect(() => {
    reasonRef.current?.focus()
  }, [])

  const phase = attempt.phase
  useEffect(() => {
    if (phase === 'failed' || phase === 'unknown') noticeRef.current?.focus()
  }, [phase])

  const send = useCallback(
    async (snapshot: AttemptSnapshot) => {
      const context: CommandContext = {
        companyId,
        locationId: location.id,
        idempotencyKey: snapshot.key,
      }
      const outcome = await setLocationActiveState(context, snapshot.payload)
      dispatch({ type: 'SETTLE', outcome, key: snapshot.key })
      // A 204 says the command was accepted, never what the record now is.
      if (outcome.type === 'applied') await onRefresh()
    },
    [companyId, dispatch, location.id, onRefresh],
  )

  const submit = useCallback(
    (event: React.FormEvent) => {
      event.preventDefault()
      if (busy) return

      const trimmed = reason.trim()
      // Mirrors the server's 1..1000-after-trim rule. A submit that fails here mints
      // NO key: nothing was attempted, so there is nothing to be uncertain about.
      if (trimmed.length === 0) {
        setReasonError(REASON_REQUIRED)
        reasonRef.current?.focus()
        return
      }
      if (trimmed.length > REASON_MAX_LENGTH) {
        setReasonError(REASON_TOO_LONG)
        reasonRef.current?.focus()
        return
      }
      setReasonError(null)

      const payload = {
        isActive: targetActive,
        expectedVersion: location.version,
        reason: trimmed,
      }
      const fingerprint = attemptFingerprint(binding, payload)
      const existing =
        attempt.phase === 'unknown' &&
        attemptFingerprint(attempt.snapshot.binding, attempt.snapshot.payload) === fingerprint
          ? attempt.snapshot
          : null

      // An unresolved attempt with the SAME content is re-sent under its OWN key, so
      // the server replays instead of writing twice.
      if (existing !== null) {
        dispatch({ type: 'RESEND' })
        void send(existing)
        return
      }

      const snapshot: AttemptSnapshot = { key: newIdempotencyKey(), binding, payload }
      dispatch({ type: 'SUBMIT', snapshot })
      void send(snapshot)
    },
    [attempt, binding, busy, dispatch, location.version, reason, send, targetActive],
  )

  const reload = useCallback(async () => {
    dispatch({ type: 'RESET' })
    setReason('')
    setReasonError(null)
    await onRefresh()
    onClose()
  }, [dispatch, onClose, onRefresh])

  function onKeyDown(event: React.KeyboardEvent<HTMLDivElement>) {
    if (event.key !== 'Escape') return
    event.preventDefault()
    // Escape never abandons an in-flight or unresolved command silently — that would
    // hide an outcome the user needs to see.
    if (busy || unresolved) return
    onClose()
  }

  return (
    <div className={styles.backdrop}>
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="org-dialog-title"
        aria-describedby="org-dialog-target"
        className={styles.dialog}
        onKeyDown={onKeyDown}
      >
        <h3 id="org-dialog-title" className={styles.dialogTitle}>
          {actionHeading(location.isActive)}
        </h3>
        <p id="org-dialog-target" className={styles.dialogTarget}>
          {targetStateSummary(location.code, location.isActive)}
        </p>

        <Outcome
          attempt={attempt}
          noticeRef={noticeRef}
          onReload={reload}
          onResend={() => {
            if (attempt.phase !== 'unknown') return
            const snapshot = attempt.snapshot
            dispatch({ type: 'RESEND' })
            void send(snapshot)
          }}
        />

        <form className={styles.form} onSubmit={submit} noValidate>
          <label className={styles.label} htmlFor="org-reason">
            {REASON_LABEL}
          </label>
          <p id="org-reason-help" className={styles.help}>
            Bu değişiklik gerekçesiyle birlikte audit kaydına yazılır. En fazla{' '}
            {REASON_MAX_LENGTH} karakter.
          </p>
          <textarea
            id="org-reason"
            ref={reasonRef}
            className={styles.textarea}
            value={reason}
            rows={3}
            maxLength={REASON_MAX_LENGTH}
            disabled={busy}
            aria-describedby={
              reasonError === null ? 'org-reason-help' : 'org-reason-help org-reason-error'
            }
            aria-invalid={reasonError !== null}
            onChange={(event) => setReason(event.target.value)}
          />
          {reasonError === null ? null : (
            <p id="org-reason-error" className={styles.fieldError} role="alert">
              {reasonError}
            </p>
          )}
          <div className={styles.actions}>
            <button type="submit" className={styles.primary} disabled={busy}>
              {busy ? 'Gönderiliyor…' : confirmActionLabel(location.isActive)}
            </button>
            <button
              type="button"
              className={styles.secondary}
              onClick={onClose}
              disabled={busy || unresolved}
            >
              {CANCEL_LABEL}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}

function Outcome({
  attempt,
  noticeRef,
  onReload,
  onResend,
}: {
  readonly attempt: AttemptState
  readonly noticeRef: React.RefObject<HTMLDivElement | null>
  readonly onReload: () => void
  readonly onResend: () => void
}) {
  if (attempt.phase === 'failed') {
    const { title, detail } = failureText(attempt.failure)
    return (
      <div
        ref={noticeRef}
        tabIndex={-1}
        className={styles.noticeError}
        role="alert"
        data-outcome={attempt.failure.kind}
      >
        <p className={styles.noticeTitle}>{title}</p>
        <p>{detail}</p>
        {attempt.failure.kind === 'version-conflict' ? (
          <p className={styles.conflict}>
            {conflictVersionsText(
              attempt.failure.conflict.expectedVersion,
              attempt.failure.conflict.currentVersion,
              attempt.failure.conflictIsCurrent,
            )}
          </p>
        ) : null}
        <button type="button" className={styles.secondary} onClick={onReload}>
          Güncel kaydı yükle
        </button>
      </div>
    )
  }

  if (attempt.phase !== 'unknown') return null

  const { title, detail } = unknownText(attempt.reason)
  return (
    <div
      ref={noticeRef}
      tabIndex={-1}
      className={styles.noticeWarn}
      role="alert"
      data-outcome="unknown"
    >
      <p className={styles.noticeTitle}>{title}</p>
      <p>{detail}</p>
      <div className={styles.actions}>
        {/* Same snapshot, same key — the server replays rather than writing twice. */}
        <button type="button" className={styles.primary} onClick={onResend}>
          Aynı işlem anahtarıyla tekrar gönder
        </button>
        <button type="button" className={styles.secondary} onClick={onReload}>
          Güncel kaydı yükle
        </button>
      </div>
    </div>
  )
}
