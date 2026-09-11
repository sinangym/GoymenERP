/**
 * CARI-LINK-WORKSPACE-001 — link an existing Party to the TARGET company
 * (CARI_C02_LINK_JIT_SPEC_v1 §2.2, §6, §7, §8; route `cari.company-link`).
 *
 * The target is the shell's committed company, shown read-only. Candidates come
 * from ONE fresh, target-scoped server read; the browser never walks other
 * companies and never unions or re-filters locally (§6.3). A selection is valid
 * only while the Party is present in the CURRENT candidate page — a navigation
 * hint or a remembered id is not authority (§7.2). The link command carries the
 * chosen roles and nothing else: no reason, no company, no copy of the source
 * roles (§7.3, §7.4).
 *
 * Authority to link comes from ONE thing: a successful read of the current query
 * issued AFTER the last broken continuation or terminal link failure. Rows kept in
 * memory, a previous query's rows shown while a new one loads, and any page React
 * Query still holds from before such a moment are never authority (§7.2;
 * C-004-P2-001, C-005-P2-001, C-DESKTOP-002-P2-001/002).
 *
 * Party count never changes here. One 201 = one new target account.
 */

import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import { hashKey, keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { fetchLinkCandidates, linkCompanyAccount, type LinkCompanyAccountPayload } from './commandApi'
import type { LinkCandidate, LinkCandidatePage } from './candidateContract'
import type { CounterpartyMutationResult } from './mutationContract'
import { PROBLEM_CODE, problemCodeOf, rolesFromFlags } from './contract'
import { isBoundUuid } from './wireGuards'
import { validateLinkForm, type LinkFormErrors } from './linkForm'
import { canBeginAttempt, isAttemptFrozen, repeatsUnresolvedAttempt } from './commandAttempt'
import { useCommandAttempt } from './useCommandAttempt'
import { CariRoleFieldset } from './CariRoleFieldset'
import { CommandAttemptNotice } from './CommandAttemptNotice'
import { CariBackLink } from './CariBackLink'
import { CARI_LINK_TEXT as TEXT, CARI_KIND_LABEL, CARI_ROLE_LABEL } from './cariCommandText'
import { classifyOutcome, type RemoteViewState, type ViewOutcome } from '../shell/uiState'
import { useShellDispatch, useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { API_BASE, contractReadSeq, isCommandContractReady } from './clientContract'
import { CAPABILITY } from '../shell/capabilities'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { useCompanyDirectory } from '../shell/useShellOrchestrator'
import { StateBlock } from '../shell/StateBlock'
import styles from './CariLinkWorkspacePage.module.css'

/** The dirty scope this workspace registers with the shell (§7.1, CLINK-AC-018). */
export const CARI_LINK_DIRTY_SCOPE = 'cari.company-link'

const LINK_OPERATION_PREFIX = 'counterparty.link:'

/** The selection error's id — referenced by every candidate radio (UX-AC-001). */
const SELECTION_ERROR_ID = 'cari-link-selection-error'

/**
 * The only continuation failures that keep the rows already loaded: transport
 * did not get through or the service is down. Same allowlist as C-01, for the
 * same reason — 401/403/malformed must never hide behind "kept rows".
 */
const CONTINUATION_SURVIVABLE = new Set(['offline', 'network-error', 'service-unavailable'])

/**
 * What the candidate query stores: the read's outcome AND the sequence number of
 * the GET that produced it (per mount, in issue order). The number is what tells a
 * read issued after a broken continuation from a page React Query kept from before
 * it (C-005-P2-001) — the outcome alone cannot; both look 'ready'.
 */
interface CandidateRead {
  readonly seq: number
  readonly outcome: ViewOutcome<LinkCandidatePage>
}

/**
 * Remounted on every context change (same `key` fence as C-01/C-03). Candidate
 * rows, cursor, selection, source summaries, chosen roles and the in-memory
 * idempotency attempt all belong to ONE target company; a new target starts empty
 * (§7.1). `confirming-switch` keeps the key so "Kal" finds everything as it was.
 */
export function CariLinkWorkspacePage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <CariLinkWorkspace key={namespace ?? 'no-context'} namespace={namespace} />
}

function CariLinkWorkspace({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const dispatch = useShellDispatch()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const directory = useCompanyDirectory()
  const boundary = useAuthBoundary()
  const [searchParams] = useSearchParams()
  const attempt = useCommandAttempt<LinkCompanyAccountPayload, CounterpartyMutationResult>()

  const entryGeneration = shell.routeEntryGeneration
  const targetCompanyId = shell.activeContext?.companyId ?? null
  const singleCompanyContext = shell.activeContext?.scopeMode === 'single' && targetCompanyId !== null
  const canOpenDetail = hasCapability(shell, CAPABILITY.cariPartiesDetail)
  const targetCompany = useMemo(
    () => directory.find((company) => company.id === targetCompanyId) ?? null,
    [directory, targetCompanyId],
  )
  const targetLabel =
    targetCompany === null ? TEXT.targetCompanyUnknown : `${targetCompany.legalName} (${targetCompany.code})`

  // --- candidate search (fresh, target-scoped, q/cursor only) -----------------
  const [qDraft, setQDraft] = useState('')
  const [q, setQ] = useState('')
  const [cursor, setCursor] = useState<string | undefined>(undefined)
  const [loaded, setLoaded] = useState<readonly LinkCandidate[]>([])

  // A `?partyId=` hint is only the radio's INITIAL value. It becomes a selection
  // solely by appearing in the fresh candidate page (§7.2) — it triggers no
  // request of its own and never reaches the link command by itself.
  const hint = searchParams.get('partyId')
  const [selectedPartyId, setSelectedPartyId] = useState<string | null>(
    hint !== null && isBoundUuid(hint) ? hint : null,
  )
  const [roles, setRoles] = useState(0)
  const [showErrors, setShowErrors] = useState(false)
  const [announce, setAnnounce] = useState('')
  // Focus targets: the validation summary (UX-AC-001) and the abandoned-attempt
  // box. The summary is focused after the render that shows it, hence a request
  // counter consumed by an effect rather than a direct call.
  const summaryRef = useRef<HTMLDivElement>(null)
  const unresolvedRef = useRef<HTMLDivElement>(null)
  const [summaryFocusRequest, setSummaryFocusRequest] = useState(0)

  const phase = attempt.state.phase
  const frozen = isAttemptFrozen(attempt.state)
  /**
   * A new attempt may start from `idle` or from an ABANDONED (unfrozen) one — and
   * only while compatibility is currently proven for this target company.
   *
   * The transport refuses a write without readiness either way, but a button that
   * stays enabled invites a submit that cannot go and burns an attempt to say so.
   * Readiness can lapse under the user: a candidate read that came back valid but
   * UNACKNOWLEDGED never opened the gate, and a later continuation that failed on
   * the network closes it. The rows and the selection survive both — they are still
   * server data — so only the button changes.
   */
  const writeReady = isCommandContractReady(
    namespace === null ? null : { origin: API_BASE, namespace },
  )
  const canSubmit = canBeginAttempt(attempt.state) && writeReady
  const completed = attempt.state.phase === 'completed' ? attempt.state : null
  const failure = attempt.state.phase === 'failed' ? attempt.state.failure : null
  const failedSnapshot = attempt.state.phase === 'failed' ? attempt.state.snapshot : null

  // Every candidate GET is numbered when it is issued and the number travels with
  // its outcome (CandidateRead). `authorityBarrier` is the number of the last read
  // that broke a continuation: only a read issued after it can be authority.
  const fetchSeqRef = useRef(0)
  const [authorityBarrier, setAuthorityBarrier] = useState(0)

  const firstPageKey = useMemo(
    () => [namespace ?? 'no-context', entryGeneration, 'link-candidates', { q, cursor: undefined }] as const,
    [namespace, entryGeneration, q],
  )

  const query = useQuery({
    // INV-NS + entry scope, exactly like C-01: the target context owns the cache
    // namespace and a revisit cannot be served from a previous entry's rows.
    queryKey: [namespace ?? 'no-context', entryGeneration, 'link-candidates', { q, cursor }],
    // Paused (not cleared) while the user answers the dirty-switch dialog.
    enabled: namespace !== null && singleCompanyContext && shell.phase === 'idle',
    placeholderData: keepPreviousData,
    queryFn: async ({ signal }): Promise<CandidateRead> => {
      // Numbered when ISSUED: a read that starts after a break can prove it. This
      // runs when React Query sends the GET, not during render.
      // oxlint-disable-next-line react/refs -- callback invoked by React Query, not render
      const seq = ++fetchSeqRef.current
      const snapshot = boundary.capture()
      const outcome = await fetchLinkCandidates(
        targetCompanyId ?? '',
        { q: q.length > 0 ? q : undefined, cursor },
        signal,
        // The candidate read IS the fresh, target-company exchange that proves
        // compatibility for the link about to be written (spec §4.2).
        namespace === null ? null : { origin: API_BASE, namespace },
      )
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, snapshot)
      }
      return { seq, outcome }
    },
  })

  const read = query.data
  const outcome = read?.outcome
  const classified = classifyOutcome(outcome ?? { type: 'malformed' }) as RemoteViewState<LinkCandidatePage>
  // On the first page, placeholder data is a PREVIOUS query's read (keepPreviousData).
  // Its rows may stay on screen while the new read loads (previousQueryRows below);
  // a previous FAILURE, however, says nothing about the page now loading — typically
  // the broken continuation shown while its recovery is in flight — so that is
  // plain loading, not the old error with a second retry button.
  const state: RemoteViewState<LinkCandidatePage> =
    query.isPending || (query.isPlaceholderData && cursor === undefined && classified.kind !== 'ready')
      ? { kind: 'initial-loading' }
      : classified
  const page = state.kind === 'ready' ? state.data : null

  // Same accumulation rules as C-01 (see CariListPage for the reasoning).
  const awaitingContinuation = cursor !== undefined && query.isPlaceholderData
  const accumulated =
    page === null || awaitingContinuation
      ? loaded
      : cursor === undefined
        ? page.items
        : [...loaded, ...page.items]
  const crossPageDuplicate = new Set(accumulated.map((item) => item.id)).size !== accumulated.length
  const items = crossPageDuplicate ? [] : accumulated
  const continuationFailed =
    cursor !== undefined && loaded.length > 0 && !crossPageDuplicate && CONTINUATION_SURVIVABLE.has(state.kind)

  // --- selection authority (§7.2, C-004-P2-001, C-005-P2-001) --------------------
  // Rows can be on screen, or kept in memory, without being authority:
  //  - the PREVIOUS query's rows while a new search loads (keepPreviousData with
  //    the cursor unset) — they answer a different question;
  //  - the rows loaded before a continuation failed in a way that hides the table
  //    (malformed / 400 / 404: not in CONTINUATION_SURVIVABLE) — the read is broken;
  //  - after such a break, ANY read issued before it: the first page React Query
  //    may still hold for this search (production keeps it fresh for 30 s), and the
  //    broken read itself shown as placeholder while its recovery loads — however
  //    'ready' a cached page looks.
  //  - after a terminal link failure (404 / already_linked), anything read before
  //    the server said so — the rows on screen included — until the re-read lands.
  // In all cases the remembered id is only a hint, exactly like `?partyId=`.
  // Authority to POST comes from ONE thing: a successful read of the CURRENT
  // query, issued after the last break or terminal failure, whose rows are the
  // ones displayed. A
  // survivable continuation failure keeps its already-loaded rows AND their
  // authority (they are current-query server data); a same-query continuation in
  // flight keeps them too.
  const previousQueryRows = cursor === undefined && query.isPlaceholderData
  const readAfterBreak = (read?.seq ?? 0) > authorityBarrier
  const rowsAuthoritative =
    !crossPageDuplicate && readAfterBreak && (state.kind === 'ready' || continuationFailed)
  // The remembered choice, wherever the rows came from; drives the radio only.
  const remembered = items.find((item) => item.id === selectedPartyId) ?? null
  // FRESH selection: the id counts only while an authoritative current page
  // contains it. A refetch that drops the Party drops the selection with it.
  const selected = rowsAuthoritative && !previousQueryRows ? remembered : null
  const selectionSuspended = selected === null && remembered !== null

  function resetPaging() {
    setCursor(undefined)
    setLoaded([])
  }

  function commitSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (frozen) return
    /**
     * A pending retry check was bound to the candidate query the user WAS looking
     * at. Changing the search withdraws that question: its answer — even a
     * successful one — cannot license a write for the list they are looking at now.
     */
    attempt.invalidateRetry()
    setQ(qDraft.trim())
    resetPaging()
  }

  function loadMore() {
    if (frozen || page === null || !page.hasMore || page.nextCursor === null) return
    setLoaded(items)
    setCursor(page.nextCursor)
  }

  // --- invalid cursor: drop the chain, read a FRESH first page (§8) -------------
  const isInvalidCursor =
    outcome?.type === 'http' && outcome.status === 400 && problemCodeOf(outcome.body) === PROBLEM_CODE.invalidCursor
  const recoveredCursor = useRef<string | null>(null)
  const [recovering, setRecovering] = useState(false)
  // oxlint-disable-next-line react/set-state-in-effect -- server-triggered, see C-01
  useEffect(() => {
    if (isInvalidCursor && cursor !== undefined && recoveredCursor.current !== cursor) {
      recoveredCursor.current = cursor
      // Recovery is a fresh read, not a cache hit: the first page cached earlier
      // in this entry is removed so the cursorless GET actually goes out.
      queryClient.removeQueries({ queryKey: firstPageKey, exact: true })
      setCursor(undefined)
      setLoaded([])
      setRecovering(true)
    }
  }, [isInvalidCursor, cursor, firstPageKey, queryClient])
  // "Recovered" is announced only once the fresh first page has actually landed.
  useEffect(() => {
    if (recovering && cursor === undefined && state.kind === 'ready' && !query.isPlaceholderData) {
      // oxlint-disable-next-line react/set-state-in-effect -- completes the server-triggered recovery above
      setRecovering(false)
      setAnnounce(TEXT.cursorRecovered)
    }
  }, [recovering, cursor, state.kind, query.isPlaceholderData])

  // --- broken continuation: the chain ends here (C-004-P2-001, C-005-P2-001) ------
  // A continuation that did not survive (malformed / 400 / 404 / a row repeated
  // across pages — anything outside CONTINUATION_SURVIVABLE) means the read is
  // broken. The rows already loaded stay in memory only so the remembered choice
  // can be shown as SUSPENDED. Nothing read up to this point may become authority
  // again: the barrier moves to this read's number, and every page React Query
  // still holds for this entry's candidate reads — the first page AND any earlier
  // continuation — is removed. Production keeps them fresh for 30 s, so a paging
  // reset, or "Daha fazla" after the recovery, would otherwise be served from them
  // without any request (F-DESKTOP-006). Recovery is a read ISSUED after this
  // moment, and only its success restores authority.
  const continuationBroken =
    cursor !== undefined &&
    !query.isPlaceholderData &&
    (crossPageDuplicate ||
      (state.kind !== 'ready' && state.kind !== 'initial-loading' && !CONTINUATION_SURVIVABLE.has(state.kind)))
  const brokenReadSeq = continuationBroken ? (read?.seq ?? 0) : 0
  // React Query's hash of the query observed right now; at a break, it names the
  // broken read's own cache entry.
  const observedQueryHash = hashKey([namespace ?? 'no-context', entryGeneration, 'link-candidates', { q, cursor }])
  // The broken read's entry, remembered until the observer has left it (below).
  const brokenQueryHashRef = useRef<string | null>(null)
  useEffect(() => {
    if (brokenReadSeq === 0) return
    // oxlint-disable-next-line react/set-state-in-effect -- server-triggered, like the invalid-cursor recovery above
    setAuthorityBarrier((current) => Math.max(current, brokenReadSeq))
    // Every OTHER page React Query retains for this entry goes now. The broken read's
    // own entry cannot go yet: it is the observed query, and removing an observed
    // query makes React Query refetch the very continuation that broke.
    brokenQueryHashRef.current = observedQueryHash
    queryClient.removeQueries({
      queryKey: [namespace ?? 'no-context', entryGeneration, 'link-candidates'],
      predicate: (cached) => cached.queryHash !== observedQueryHash,
    })
  }, [brokenReadSeq, observedQueryHash, namespace, entryGeneration, queryClient])
  // … and the broken read's entry goes the moment the observer has moved on —
  // recovery, a new search, an invalid-cursor reset. Paging onto that cursor again
  // is then a fresh GET, never the malformed page production would still hold fresh
  // for 30 s (C-DESKTOP-002-P2-001).
  useEffect(() => {
    const pending = brokenQueryHashRef.current
    if (pending === null || pending === observedQueryHash) return
    brokenQueryHashRef.current = null
    queryClient.removeQueries({
      queryKey: [namespace ?? 'no-context', entryGeneration, 'link-candidates'],
      predicate: (cached) => cached.queryHash === pending,
    })
  }, [observedQueryHash, namespace, entryGeneration, queryClient])

  // --- dirty scope (§7.1) --------------------------------------------------------
  // A remembered choice is user state even while its authority is suspended.
  const dirty = frozen || (completed === null && (remembered !== null || roles !== 0))
  useEffect(() => {
    dispatch({ type: dirty ? 'DIRTY_SET' : 'DIRTY_CLEAR', scope: CARI_LINK_DIRTY_SCOPE })
  }, [dirty, dispatch])
  useEffect(() => () => dispatch({ type: 'DIRTY_CLEAR', scope: CARI_LINK_DIRTY_SCOPE }), [dispatch])

  function touch() {
    // Any change after a definite failure starts a NEW business attempt.
    if (failure !== null) attempt.abandon()
  }

  // --- link command ----------------------------------------------------------------
  const validation = validateLinkForm({ hasFreshSelection: selected !== null, roles })
  const errors: LinkFormErrors = showErrors && !validation.ok ? validation.errors : {}
  const errorList = [errors.selection, errors.roles].filter(
    (message): message is string => message !== undefined,
  )
  // After "İşlemi bırak" the attempt is still unresolved. The same Party with the
  // same roles IS that attempt (C-004-P1-001): the notice offers its same-key
  // retry, and `submit` refuses to mint a new key for it.
  const repeatsUnresolved =
    selected !== null &&
    validation.ok &&
    repeatsUnresolvedAttempt(attempt.state, `${LINK_OPERATION_PREFIX}${selected.id}`, validation.payload)

  // UX-AC-001: the validation summary receives focus on an invalid submit. It is
  // rendered by the same state change that requests the focus, so the focus is
  // applied by the effect below, after that render.
  useEffect(() => {
    if (summaryFocusRequest > 0) summaryRef.current?.focus()
  }, [summaryFocusRequest])

  function onLinkSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!canSubmit || targetCompanyId === null) return
    const result = validateLinkForm({ hasFreshSelection: selected !== null, roles })
    if (!result.ok || selected === null) {
      // Client-invalid: the summary lists every error and takes focus; each
      // control is linked to its own error text. No key is minted (§4.3).
      setShowErrors(true)
      setSummaryFocusRequest((count) => count + 1)
      return
    }
    setShowErrors(false)
    const partyId = selected.id
    const payload = result.payload
    const companyId = targetCompanyId
    const outcome = attempt.submit(`${LINK_OPERATION_PREFIX}${partyId}`, payload, (idempotencyKey, signal) =>
      linkCompanyAccount({ companyId, idempotencyKey, contract: { origin: API_BASE, namespace: namespace ?? '' } }, partyId, payload, signal),
    )
    // The unchanged payload of an abandoned, unresolved attempt: nothing was sent
    // and no key was minted; focus lands on the explanation already on screen.
    if (outcome === 'unchanged') unresolvedRef.current?.focus()
  }

  // 404 / already_linked: the candidate set has changed under us. The selection is
  // dropped and a FRESH first page is read — that read, not the error, is the
  // authority on what remains linkable (§8). Nothing read up to this moment — the
  // rows still on screen, or a read already in flight — may authorize another link:
  // the barrier moves to the last GET issued so far, so only a read issued after the
  // failure restores authority, and "yenilendi" is announced when that read has
  // landed, not by the error (C-DESKTOP-002-P2-002). Applied once per failed snapshot.
  const handledFailureRef = useRef<string | null>(null)
  const [reconciling, setReconciling] = useState(false)
  useEffect(() => {
    if (failedSnapshot === null || handledFailureRef.current === failedSnapshot.key) return
    handledFailureRef.current = failedSnapshot.key
    if (failure !== 'not-found' && failure !== 'already-linked') return
    // Captured BEFORE the invalidation below issues the re-read, which takes the next number.
    const lastIssued = fetchSeqRef.current
    // oxlint-disable-next-line react/set-state-in-effect -- reconciles a definite server outcome, once per snapshot
    setAuthorityBarrier((current) => Math.max(current, lastIssued))
    setSelectedPartyId(null)
    setCursor(undefined)
    setLoaded([])
    setReconciling(true)
    void queryClient.invalidateQueries({
      queryKey: [namespace ?? 'no-context', entryGeneration, 'link-candidates'],
    })
  }, [failedSnapshot, failure, namespace, entryGeneration, queryClient])
  useEffect(() => {
    if (reconciling && cursor === undefined && readAfterBreak && state.kind === 'ready' && !query.isPlaceholderData) {
      // oxlint-disable-next-line react/set-state-in-effect -- completes the server-triggered reconciliation above
      setReconciling(false)
      setAnnounce(TEXT.candidatesRefreshed)
    }
  }, [reconciling, cursor, readAfterBreak, state.kind, query.isPlaceholderData])

  // Completion ONCE per snapshot; a replay settles the same snapshot (§4.4).
  const completedKeyRef = useRef<string | null>(null)
  useEffect(() => {
    if (completed === null || completedKeyRef.current === completed.snapshot.key) return
    completedKeyRef.current = completed.snapshot.key
    if (namespace !== null) {
      void queryClient.invalidateQueries({ predicate: (cached) => cached.queryKey[0] === namespace })
    }
    if (canOpenDetail) void navigate(`/cari/${completed.result.id}`)
  }, [completed, canOpenDetail, namespace, navigate, queryClient])

  const announcement = completed === null ? announce : TEXT.announceLinked(completed.result.code)

  if (!singleCompanyContext) {
    return (
      <section aria-labelledby="cari-link-heading" className={styles.page}>
        <h1 id="cari-link-heading">{TEXT.title}</h1>
        <StateBlock state={{ kind: 'no-authorized-company' }} subject={TEXT.noSingleCompany} />
      </section>
    )
  }

  // A 403/401 on the command, or on the candidate read: nothing of the workspace
  // is shown — no candidate count, no Party names, no form (§8).
  const readDenied =
    state.kind === 'forbidden' || state.kind === 'unauthenticated' ? state.kind : null
  const closedKind = failure === 'forbidden' || failure === 'unauthenticated' ? failure : readDenied
  if (closedKind !== null) {
    return (
      <section aria-labelledby="cari-link-heading" className={styles.page}>
        <h1 id="cari-link-heading">{TEXT.title}</h1>
        <CariBackLink label={TEXT.backToList} />
        <StateBlock state={{ kind: closedKind }} subject={TEXT.linkSubject} />
      </section>
    )
  }

  if (completed !== null && !canOpenDetail) {
    const record = completed.result
    return (
      <section aria-labelledby="cari-link-heading" className={styles.page}>
        <h1 id="cari-link-heading">{TEXT.title}</h1>
        <CariBackLink label={TEXT.backToList} />
        <div role="status" aria-live="polite" className={styles.announce}>
          {announcement}
        </div>
        <div className={styles.success} data-outcome="linked">
          <h2 className={styles.successTitle}>{TEXT.successTitle}</h2>
          <dl className={styles.facts}>
            <Fact label={TEXT.successCode} value={record.code} />
            <Fact label={TEXT.colName} value={record.legalName} />
            <Fact label={TEXT.successCompany} value={targetLabel} />
            <Fact
              label={TEXT.successRoles}
              value={rolesFromFlags(record.account.roles)
                .map((role) => CARI_ROLE_LABEL[role])
                .join(', ')}
            />
          </dl>
          <p className={styles.successNote}>{TEXT.successNoDetail}</p>
          <button
            type="button"
            className={styles.secondary}
            onClick={() => {
              attempt.abandon()
              setSelectedPartyId(null)
              setRoles(0)
              setShowErrors(false)
              resetPaging()
            }}
          >
            {TEXT.linkAnother}
          </button>
        </div>
      </section>
    )
  }

  const summaryId = 'cari-link-summary'
  const showSummary = showErrors && errorList.length > 0
  const failedPartyId =
    failedSnapshot !== null && failedSnapshot.operation.startsWith(LINK_OPERATION_PREFIX)
      ? failedSnapshot.operation.slice(LINK_OPERATION_PREFIX.length)
      : null

  return (
    <section aria-labelledby="cari-link-heading" className={styles.page}>
      <h1 id="cari-link-heading">{TEXT.title}</h1>
      <CariBackLink label={TEXT.backToList} />
      <div role="status" aria-live="polite" className={styles.announce}>
        {announcement}
      </div>

      <p className={styles.intro}>{TEXT.intro}</p>
      <div className={styles.readonly}>
        <span className={styles.readonlyLabel}>{TEXT.targetCompany}</span>
        <span className={styles.readonlyValue}>{targetLabel}</span>
      </div>

      <form role="search" className={styles.search} onSubmit={commitSearch}>
        <label className={styles.field}>
          <span>{TEXT.searchLabel}</span>
          <input
            type="search"
            value={qDraft}
            maxLength={200}
            placeholder={TEXT.searchPlaceholder}
            disabled={frozen}
            onChange={(event) => setQDraft(event.target.value)}
          />
        </label>
        <button type="submit" className={styles.secondary} disabled={frozen}>
          {TEXT.searchButton}
        </button>
      </form>

      {(state.kind !== 'ready' && !continuationFailed) || crossPageDuplicate ? (
        <StateBlock
          state={crossPageDuplicate ? { kind: 'malformed-response' } : state}
          subject={TEXT.subject}
          onRetry={() => {
            if (cursor === undefined) {
              // The first page itself failed: read it again.
              void query.refetch()
              return
            }
            // A broken continuation: leave the chain and read the FIRST page — from
            // the server, since its retained copy went with the break (see
            // continuationBroken) — never a refetch of the broken continuation. The
            // rows kept in memory carry the remembered choice as SUSPENDED until it
            // lands; only that read can make it a selection again.
            setCursor(undefined)
          }}
        />
      ) : items.length === 0 ? (
        <StateBlock state={{ kind: q.length > 0 ? 'empty-filtered' : 'empty-no-data' }} subject="aday" />
      ) : (
        <>
          <div className={styles.tableScroll} tabIndex={0} role="region" aria-label={TEXT.candidatesRegion}>
            <table className={styles.table} aria-busy={query.isFetching}>
              <caption className={styles.caption}>{TEXT.candidatesCaption}</caption>
              <thead>
                <tr>
                  <th scope="col">{TEXT.colSelect}</th>
                  <th scope="col">{TEXT.colCode}</th>
                  <th scope="col">{TEXT.colName}</th>
                  <th scope="col">{TEXT.colKind}</th>
                  <th scope="col">{TEXT.colSources}</th>
                </tr>
              </thead>
              <tbody>
                {items.map((item) => (
                  <tr key={item.id} data-selected={remembered?.id === item.id ? 'true' : undefined}>
                    <td>
                      {/* The radio shows the REMEMBERED choice so it does not flicker
                          while a fresh page loads; authority is `selected`, shown by
                          the selection line below. The selection error is linked to
                          every radio (UX-AC-001), not only announced in the summary. */}
                      <input
                        type="radio"
                        name="cari-link-candidate"
                        value={item.id}
                        checked={remembered?.id === item.id}
                        disabled={frozen}
                        aria-label={TEXT.candidateLabel(item.code, item.legalName)}
                        aria-invalid={errors.selection ? true : undefined}
                        aria-describedby={errors.selection ? SELECTION_ERROR_ID : undefined}
                        onChange={() => {
                          touch()
                          setSelectedPartyId(item.id)
                        }}
                      />
                    </td>
                    <td className={styles.code}>{item.code}</td>
                    <td>
                      <span className={styles.legalName}>{item.legalName}</span>
                      {item.tradeName !== null && item.tradeName !== item.legalName ? (
                        <span className={styles.tradeName}>{item.tradeName}</span>
                      ) : null}
                    </td>
                    <td>{CARI_KIND_LABEL[item.kind]}</td>
                    <td>
                      {/* Source facts only: company label (directory, never a raw id),
                          roles and the SOURCE account state. Nothing here is a
                          target default; the target roles are chosen below (§7.3). */}
                      <ul className={styles.sources}>
                        {item.visibleSourceAccounts.map((account) => {
                          const company = directory.find((entry) => entry.id === account.companyId)
                          const label =
                            company === undefined ? TEXT.unknownCompany : `${company.legalName} (${company.code})`
                          return (
                            <li key={account.id} data-account-state={account.isActive ? 'active' : 'inactive'}>
                              <span className={styles.sourceCompany}>{label}</span>
                              <span>
                                {rolesFromFlags(account.roles)
                                  .map((role) => CARI_ROLE_LABEL[role])
                                  .join(', ')}
                              </span>
                              <span className={styles.sourceState}>
                                {account.isActive ? TEXT.sourceActive : TEXT.sourceInactive}
                              </span>
                            </li>
                          )
                        })}
                      </ul>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {continuationFailed ? (
            <div className={styles.continuationError} role="alert">
              <span>{TEXT.continuationFailed}</span>
              <button type="button" onClick={() => void query.refetch()} disabled={query.isFetching}>
                {query.isFetching ? TEXT.loading : TEXT.retry}
              </button>
            </div>
          ) : page?.hasMore ? (
            <button
              type="button"
              className={styles.more}
              onClick={loadMore}
              disabled={query.isFetching || frozen}
            >
              {query.isFetching ? TEXT.loading : TEXT.loadMore}
            </button>
          ) : null}
        </>
      )}

      <form
        className={styles.form}
        noValidate
        onSubmit={onLinkSubmit}
        aria-labelledby="cari-link-form-heading"
        aria-describedby={showSummary ? summaryId : undefined}
      >
        <h2 id="cari-link-form-heading" className={styles.formHeading}>
          {TEXT.formHeading}
        </h2>

        {/* Validation summary first in the form: it takes focus on an invalid
            submit (UX-AC-001), so the controls it lists follow it in tab order. */}
        {showSummary ? (
          <div id={summaryId} ref={summaryRef} tabIndex={-1} role="alert" className={styles.summary}>
            <p>{TEXT.errorSummary}</p>
            <ul>
              {errorList.map((message) => (
                <li key={message}>{message}</li>
              ))}
            </ul>
          </div>
        ) : null}

        {/* `fresh`: an authoritative current-page row. `suspended`: a remembered
            choice the current read has not (yet) confirmed — no name is shown,
            because the rows it came from are not authority. */}
        <p
          className={styles.selection}
          data-selection={selected !== null ? 'fresh' : selectionSuspended ? 'suspended' : 'none'}
        >
          <span className={styles.readonlyLabel}>{TEXT.selectedCandidate}: </span>
          {selected !== null
            ? TEXT.candidateLabel(selected.code, selected.legalName)
            : selectionSuspended
              ? TEXT.selectionSuspended
              : TEXT.noSelection}
        </p>
        <FieldError id={SELECTION_ERROR_ID} message={errors.selection} />

        <fieldset className={styles.fields} disabled={frozen}>
          <CariRoleFieldset
            idPrefix="cari-link"
            legend={TEXT.rolesLegend}
            value={roles}
            onChange={(next) => {
              touch()
              setRoles(next)
            }}
            error={errors.roles}
          />
        </fieldset>

        <CommandAttemptNotice
          state={attempt.state}
          onRetry={() =>
            attempt.retry(async () => {
              /**
               * The retry stands on its OWN completed read, not on a resolved
               * promise. TanStack can settle a refetch from cached data, or cancel
               * and revert it when the query key changes — in both cases the promise
               * resolves and nothing was read. The recorded-read counter moving is
               * what distinguishes them.
               */
              const before = contractReadSeq()
              await query.refetch()
              return (
                contractReadSeq() > before &&
                isCommandContractReady(namespace === null ? null : { origin: API_BASE, namespace })
              )
            })
          }
          onAbandon={attempt.abandon}
          onAcknowledge={attempt.abandon}
          repeatsUnresolved={repeatsUnresolved}
          unresolvedRef={unresolvedRef}
          extra={
            // An existing relation is shown only through a fresh authorized read —
            // the C-03 route — and only when that grant is held (§8, CLINK-AC-015).
            failure === 'already-linked' && canOpenDetail && failedPartyId !== null ? (
              <p className={styles.verify}>
                <Link to={`/cari/${failedPartyId}`}>{TEXT.verifyInDetail}</Link>
              </p>
            ) : null
          }
        />

        <div className={styles.actions}>
          {/* A disabled control has to say why (UX review §8.7/64, §12/10): the gate is
              closed because compatibility is not currently proven here, which is a fact
              about the connection, not about the selection the user made. */}
          {!writeReady ? (
            <p id="cari-link-not-ready" className={styles.notReady} role="status">
              Bu şirkette yazma uyumu şu an doğrulanmadı. Aday listesini yeniden okuyun;
              seçiminiz korunur.
            </p>
          ) : null}

          <button
            type="submit"
            className={styles.primary}
            disabled={!canSubmit}
            aria-busy={phase === 'submitting' || undefined}
          >
            {phase === 'submitting' ? TEXT.submitting : TEXT.submit}
          </button>
        </div>
      </form>
    </section>
  )
}

function FieldError({ id, message }: { readonly id: string; readonly message?: string }) {
  if (!message) return null
  return (
    <p id={id} className={styles.error}>
      {message}
    </p>
  )
}

function Fact({ label, value }: { readonly label: string; readonly value: string }) {
  return (
    <div className={styles.fact}>
      <dt>{label}</dt>
      <dd>{value}</dd>
    </div>
  )
}
