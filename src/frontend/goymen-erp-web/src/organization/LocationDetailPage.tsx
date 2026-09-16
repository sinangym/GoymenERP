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
import { Link, useLocation, useParams } from 'react-router'
import { fetchLocation } from './api'
import {
  setLocationActiveState,
  type CommandContext,
  type CommandUnknown,
} from './commandApi'
import type { LocationSummary } from './contract'
import {
  attemptReducer,
  initialAttemptState,
  isUnresolved,
  unresolvedSnapshot,
  type AttemptSnapshot,
  type AttemptState,
} from './activeStateAttempt'
import { ORGANIZATION_CAPABILITY } from './capabilities'
import { backTarget } from './detailReturn'
import {
  attemptStoreKey,
  recallAttempt,
  releaseAttempt,
  reserveAttempt,
  updateAttempt,
} from './unresolvedAttemptStore'
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
  retryRefusedText,
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
  /**
   * Identity of an outstanding command: session + actor + company + location.
   *
   * Not the shell namespace — that carries the capability generation, which a 403
   * refence changes, and surviving a refence is half the point. `sessionEpoch` is a
   * different dimension and IS included: the 403 branch leaves it alone, while 401 /
   * logout / actor change advance it, so an attempt survives a revoked capability but
   * never crosses into a new session. See unresolvedAttemptStore.
   */
  const storeKey = attemptStoreKey(
    shell.sessionEpoch,
    shell.activeContext?.userId ?? null,
    activeCompanyId,
    locationId ?? '',
  )

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
      <Frame namespace={namespace}>
        <StateBlock
          state={{ kind: 'no-authorized-company' }}
          subject="lokasyon kartı için tek şirket bağlamı"
        />
      </Frame>
    )
  }

  if (state.kind !== 'ready') {
    /**
     * The card cannot render, but an outstanding command must not go quiet just
     * because the READ failed. Saying nothing here is how the user concludes the
     * change never happened.
     */
    return (
      <Frame namespace={namespace}>
        <StateBlock state={state} subject="lokasyon" onRetry={() => void query.refetch()} />
        <OutstandingCommandBanner storeKey={storeKey} />
      </Frame>
    )
  }

  return (
    <Frame namespace={namespace}>
      <LocationCard location={state.data} />
      {canChangeStatus ? (
        <ActiveStateSection
          location={state.data}
          companyId={activeCompanyId ?? ''}
          binding={namespace ?? 'no-context'}
          storeKey={storeKey}
          refreshing={query.isFetching}
          onRefresh={refresh}
        />
      ) : null}
    </Frame>
  )
}

/**
 * The back link carries the list's return state straight through, and now also asks it
 * WHICH list to go back to.
 *
 * The row that opened this card recorded which page it was on, bound to the context
 * namespace. Passing it back unchanged is what makes "page 2 → card → change → back"
 * land on page 2; the list re-checks the namespace, so the same state after a company
 * switch is ignored and resets to page 1. Neither screen has to know the other's rule.
 *
 * TWO LISTS NOW OPEN THIS CARD (ORG-01's locations and ORG-03's work centers), so a
 * hardcoded `/ayarlar/organizasyon` would drop a user who arrived from the work centers
 * onto a list they never opened. That is a USABILITY cost, not an authority one: this
 * card and ORG-01 stand on the same `organization.locations.read`, so whoever opened the
 * card holds ORG-01 as well. `backTarget` resolves an ORIGIN TOKEN through a frozen table
 * and defaults to the location list for anything it does not recognise; see
 * ./detailReturn.ts for why a path is never taken from router state.
 */
function Frame({
  namespace,
  children,
}: {
  readonly namespace: string | null
  readonly children: React.ReactNode
}) {
  const routerLocation = useLocation()
  const back = backTarget(routerLocation.state, namespace)
  return (
    <section aria-labelledby="org-location-heading" className={styles.page}>
      <nav aria-label="Geri" className={styles.breadcrumb}>
        <Link to={back.path} state={routerLocation.state}>
          ← {back.label}
        </Link>
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

/**
 * Tells the user a command of theirs is still outstanding even though the card could
 * not be read. Deliberately offers no action: the only honest next steps need the
 * record, and the state block above already owns retrying that.
 */
function OutstandingCommandBanner({ storeKey }: { readonly storeKey: string | null }) {
  const pending = recallAttempt(storeKey)
  if (pending === null) return null
  return (
    <div className={styles.noticeWarn} role="alert" data-outcome="outstanding">
      <p className={styles.noticeTitle}>Sonucu bilinmeyen bir işleminiz var</p>
      <p>
        Daha önce gönderdiğiniz durum değişikliğinin sonucu hâlâ belirsiz ve kayıt şu an
        okunamıyor. İşlem kendiliğinden tekrarlanmadı ve iptal edilmedi. Kayıt yeniden
        okunduğunda aynı işlem anahtarıyla devam edebilirsiniz.
      </p>
    </div>
  )
}

/** Rebuilds the reducer state from a surviving unresolved attempt, if there is one. */
function restoredAttempt(storeKey: string | null): AttemptState {
  const stored = recallAttempt(storeKey)
  if (stored === null) return initialAttemptState
  return {
    phase: 'unknown',
    snapshot: stored.snapshot,
    reason: stored.reason,
    resends: stored.resends,
    lastRefusal: stored.lastRefusal,
  }
}

function ActiveStateSection({
  location,
  companyId,
  binding,
  storeKey,
  refreshing,
  onRefresh,
}: {
  readonly location: LocationSummary
  readonly companyId: string
  readonly binding: string
  readonly storeKey: string | null
  readonly refreshing: boolean
  readonly onRefresh: () => Promise<void>
}) {
  /**
   * Seeded from the store, so an attempt that outlived this component's unmount comes
   * back instead of starting `idle` with its key silently gone.
   */
  const [attempt, dispatch] = useReducer(attemptReducer, storeKey, restoredAttempt)
  // A restored attempt opens its dialog: the user has an outstanding command and needs
  // the controls that resolve it, not a closed modal hiding them.
  const [open, setOpen] = useState(() => recallAttempt(storeKey) !== null)

  /**
   * The store mirrors the reducer for exactly the unresolved phases.
   *
   * `submitting` counts: if this unmounts mid-flight the settle is dropped, so the
   * outcome is genuinely unknown and the key must survive.
   */
  useEffect(() => {
    if (attempt.phase === 'unknown') {
      updateAttempt(storeKey, {
        snapshot: attempt.snapshot,
        reason: attempt.reason,
        resends: attempt.resends,
        lastRefusal: attempt.lastRefusal,
      })
      return
    }
    if (attempt.phase === 'submitting') {
      // Already reserved synchronously before the request; this only refreshes it.
      updateAttempt(storeKey, {
        snapshot: attempt.snapshot,
        reason: attempt.unresolvedSince ?? { kind: 'aborted' },
        resends: attempt.resends,
      })
      return
    }
    if (attempt.phase === 'applied' || attempt.phase === 'failed') {
      // Terminal — and only THIS attempt is released, never whatever happens to be
      // stored. A settlement closes one command, not the slot.
      releaseAttempt(storeKey, attempt.snapshot.key)
      return
    }
    /**
     * `idle` deliberately does nothing.
     *
     * An unmounting or freshly-mounted owner passes through idle, and deleting here let
     * it erase a newer outstanding command reserved by whoever owns the screen now.
     * Abandonment is already handled where it happens: the trigger refuses to RESET an
     * unresolved attempt, and a resolved one was released above.
     */
  }, [attempt, storeKey])
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
        disabled={isUnresolved(attempt)}
        onClick={() => {
          // Never RESET over an unresolved attempt: that would drop the key whose
          // command may already be recorded (P1-1).
          if (!isUnresolved(attempt)) dispatch({ type: 'RESET' })
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
          storeKey={storeKey}
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

/** Tabbable controls this dialog can contain. */
const FOCUSABLE = 'a[href], button, textarea, input, select, [tabindex]:not([tabindex="-1"])'

function ActiveStateDialog({
  location,
  companyId,
  binding,
  storeKey,
  targetActive,
  attempt,
  dispatch,
  onClose,
  onRefresh,
}: {
  readonly location: LocationSummary
  readonly companyId: string
  readonly binding: string
  readonly storeKey: string | null
  readonly targetActive: boolean
  readonly attempt: AttemptState
  readonly dispatch: React.Dispatch<Parameters<typeof attemptReducer>[1]>
  readonly onClose: () => void
  readonly onRefresh: () => Promise<void>
}) {
  const shellDispatch = useShellDispatch()
  const boundary = useAuthBoundary()
  /**
   * Seeded from the attempt when one is already unresolved at mount — a restored
   * attempt must show the justification it was sent with, not an empty box the user
   * would read as "my text was lost".
   */
  const [reason, setReason] = useState(() => unresolvedSnapshot(attempt)?.payload.reason ?? '')
  const [reasonError, setReasonError] = useState<string | null>(null)
  const reasonRef = useRef<HTMLTextAreaElement>(null)
  const dialogRef = useRef<HTMLDivElement>(null)
  const noticeRef = useRef<HTMLDivElement>(null)

  const busy = attempt.phase === 'submitting'
  /**
   * THE FREEZE. While a key is unresolved the justification cannot be edited and the
   * ordinary submit is gone.
   *
   * Editing the reason changes the payload fingerprint, and a changed fingerprint is
   * precisely what mints a NEW key — which would turn a committed-but-unacknowledged
   * command into a SECOND command, and against a freshly-read state possibly into the
   * opposite activation. The only ways forward are the same-key resend and refreshing
   * the read.
   */
  const unresolved = isUnresolved(attempt)

  // The draft is dirty work: a company switch must warn before discarding it
  // (ERP-000K §13.1). Registered with the SHARED scope mechanism, not a local flag.
  const dirty = reason.trim().length > 0 || unresolved
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
    async (snapshot: AttemptSnapshot, resends: number, since?: CommandUnknown) => {
      /**
       * RESERVE FIRST, SEND SECOND — and synchronously.
       *
       * This runs before the first `await`, so the record exists before the request can
       * possibly be in flight. Doing it from the mirroring effect instead left a window:
       * effects run after the render commit, while `send()` is invoked in the same tick
       * as the dispatch, so a key could already be on the wire with nothing written
       * anywhere. Unmounting inside that window — the card re-read failing, the boundary
       * closing the mount — lost exactly the key this store exists to keep.
       */
      reserveAttempt(storeKey, {
        snapshot,
        reason: since ?? { kind: 'aborted' },
        resends,
      })

      const context: CommandContext = {
        companyId,
        locationId: location.id,
        idempotencyKey: snapshot.key,
      }
      /**
       * Captured BEFORE the request, so the shared boundary judges the answer against
       * the context the command was actually authorized under. A late 401/403 from a
       * context we have since left is then recognised as stale and closes nothing
       * (SAB-008/009) — the reducer owns that decision, not this component.
       */
      const security = boundary.capture()
      const outcome = await setLocationActiveState(context, snapshot.payload)

      /**
       * A command rejected by authorization is the same security fact as a rejected
       * read, and it goes to the SAME central boundary. Reporting it only inside this
       * modal left a dead session sitting behind a company card and a submit surface
       * that still looked usable.
       *
       * This does NOT resolve the attempt — see the reducer: a 401/403 on a retry says
       * nothing about an earlier request that may already be recorded.
       */
      if (outcome.type === 'failed') {
        if (outcome.failure.kind === 'unauthenticated') boundary.report(401, security)
        if (outcome.failure.kind === 'forbidden') boundary.report(403, security)
      }

      dispatch({ type: 'SETTLE', outcome, key: snapshot.key })
      // A 204 says the command was accepted, never what the record now is.
      if (outcome.type === 'applied') await onRefresh()
    },
    [boundary, companyId, dispatch, location.id, onRefresh, storeKey],
  )

  const submit = useCallback(
    (event: React.FormEvent) => {
      event.preventDefault()
      // Frozen: an unresolved attempt is advanced only by the explicit same-key resend.
      if (busy || unresolved) return

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

      const snapshot: AttemptSnapshot = {
        key: newIdempotencyKey(),
        binding,
        payload: { isActive: targetActive, expectedVersion: location.version, reason: trimmed },
      }
      dispatch({ type: 'SUBMIT', snapshot })
      void send(snapshot, 0)
    },
    [binding, busy, dispatch, location.version, reason, send, targetActive, unresolved],
  )

  const resend = useCallback(() => {
    if (attempt.phase !== 'unknown') return
    const snapshot = attempt.snapshot
    const { resends, reason } = attempt
    dispatch({ type: 'RESEND' })
    void send(snapshot, resends + 1, reason)
  }, [attempt, dispatch, send])

  /**
   * UNKNOWN recovery: refresh the READ only.
   *
   * It must not touch the attempt. Seeing the target state in a GET is not a receipt
   * for the command — the record could have reached that state another way — so
   * dropping the key here would abandon an unresolved command while telling the user
   * nothing about what became of it.
   */
  const refreshOnly = useCallback(async () => {
    await onRefresh()
  }, [onRefresh])

  /**
   * REFUSED recovery: the attempt is closed, so a new one may be started — and the
   * justification the user wrote is KEPT.
   *
   * After a version conflict the recommended next step is exactly "look at the fresh
   * record and decide again"; wiping the draft as a side effect of taking that step is
   * the opposite of helping.
   */
  const reloadForNewAttempt = useCallback(async () => {
    dispatch({ type: 'RESET' })
    setReasonError(null)
    await onRefresh()
  }, [dispatch, onRefresh])

  /**
   * Focus containment. `aria-modal` is a promise to assistive technology, not a
   * behaviour the browser implements for a plain div — without this, Tab walks out to
   * the trigger, the breadcrumb and the company picker underneath. That is both a
   * broken modal and, because those controls act on the page behind, a keyboard route
   * to losing an unresolved attempt.
   */
  function onKeyDown(event: React.KeyboardEvent<HTMLDivElement>) {
    if (event.key === 'Escape') {
      event.preventDefault()
      // Escape never abandons an in-flight or unresolved command silently — that would
      // hide an outcome the user needs to see.
      if (busy || unresolved) return
      onClose()
      return
    }
    if (event.key !== 'Tab') return
    const root = dialogRef.current
    if (root === null) return
    const focusable = [...root.querySelectorAll<HTMLElement>(FOCUSABLE)].filter(
      (element) => !element.hasAttribute('disabled') && element.tabIndex !== -1,
    )
    if (focusable.length === 0) return
    const first = focusable[0]
    const last = focusable[focusable.length - 1]
    const active = document.activeElement
    /**
     * Focus may sit on an element that is NOT tabbable: the outcome notices are
     * `tabIndex={-1}` so they can be focused programmatically when an answer arrives.
     * From there neither the "first" nor the "last" test matched and neither did
     * "outside the root", so the guard fell through and Shift+Tab walked out to the
     * breadcrumb behind the modal. Anything inside the dialog but off the tab ring is
     * therefore steered explicitly.
     */
    if (!root.contains(active)) {
      event.preventDefault()
      ;(event.shiftKey ? last : first).focus()
      return
    }
    if (!focusable.includes(active as HTMLElement)) {
      event.preventDefault()
      ;(event.shiftKey ? last : first).focus()
      return
    }
    if (event.shiftKey && active === first) {
      event.preventDefault()
      last.focus()
    } else if (!event.shiftKey && active === last) {
      event.preventDefault()
      first.focus()
    }
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
          onRefreshOnly={refreshOnly}
          onReloadForNewAttempt={reloadForNewAttempt}
          onResend={resend}
        />

        <form className={styles.form} onSubmit={submit} noValidate>
          <label className={styles.label} htmlFor="org-reason">
            {REASON_LABEL}
          </label>
          <p id="org-reason-help" className={styles.help}>
            {unresolved
              ? 'Sonucu bilinmeyen bir işlem sürüyor. Çözülene kadar gerekçe değiştirilemez ve yeni işlem başlatılamaz.'
              : `Bu değişiklik gerekçesiyle birlikte audit kaydına yazılır. En fazla ${REASON_MAX_LENGTH} karakter.`}
          </p>
          <textarea
            id="org-reason"
            ref={reasonRef}
            className={styles.textarea}
            value={reason}
            rows={3}
            maxLength={REASON_MAX_LENGTH}
            /* Frozen while unresolved — see `unresolved` above. Read-only rather than
               disabled so the text the user wrote stays readable and selectable. */
            readOnly={unresolved}
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
            {unresolved ? null : (
              <button type="submit" className={styles.primary} disabled={busy}>
                {confirmActionLabel(location.isActive)}
              </button>
            )}
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
  onRefreshOnly,
  onReloadForNewAttempt,
  onResend,
}: {
  readonly attempt: AttemptState
  readonly noticeRef: React.RefObject<HTMLDivElement | null>
  readonly onRefreshOnly: () => void
  readonly onReloadForNewAttempt: () => void
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
        {/* Closes the attempt and refreshes the record, KEEPING the justification. */}
        <button type="button" className={styles.secondary} onClick={onReloadForNewAttempt}>
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
      {attempt.lastRefusal === undefined ? null : (
        /* A later request was turned away. Shown, but it never claims the first one
           did not apply — the uncertainty above still owns that. */
        <p className={styles.retryRefused} data-refusal={attempt.lastRefusal.kind}>
          {retryRefusedText(attempt.lastRefusal)}
        </p>
      )}
      <div className={styles.actions}>
        {/* Same snapshot, same key — the server replays rather than writing twice. */}
        <button type="button" className={styles.primary} onClick={onResend}>
          Aynı işlem anahtarıyla tekrar gönder
        </button>
        {/* Refreshes the READ ONLY. The unresolved key and snapshot are kept. */}
        <button type="button" className={styles.secondary} onClick={onRefreshOnly}>
          Güncel kaydı yükle
        </button>
      </div>
    </div>
  )
}
