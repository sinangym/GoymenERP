import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { Link, useLocation } from 'react-router'
import { fetchWorkCenters } from '../organization/api'
import type { WorkCenterSummary } from '../organization/contract'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { isKilogramText } from './contract'
import { MANUFACTURING_CAPABILITY, orderListPath, parsePlanCreateLabel, planDetailPath } from './capabilities'
import {
  fetchParentRollCandidates,
  PARENT_ROLL_PRESELECT_LIMIT,
  type ParentRollCandidate,
  type ParentRollCandidates,
} from './planReferences'
import {
  savePlan,
  type PlanTargetBasis,
  type SavePlanOutcome,
  type SavePlanRequest,
} from './planCommandApi'
import {
  pendingPlanCreateKey,
  readPendingPlanCreate,
  releasePendingPlanCreate,
  reservePendingPlanCreate,
  type PendingPlanCreate,
  type PendingPlanCreateSlot,
} from './pendingPlanStore'
import { PlanPreviewPanel } from './PlanPreviewPanel'
import { cutToLengthDraftInput, planPreviewInput, usePlanPreview, type CutToLengthLineDraft } from './usePlanPreview'
import { CutToLengthLinesEditor } from './CutToLengthLinesEditor'
import { CUT_LINE_TEXT } from './planEditText'
import { TARGET_BASIS_LABEL } from './manufacturingText'
import { PLAN_CREATE_TEXT, PLAN_FORM_TEXT, readFailureText, saveFailureText } from './planText'
import styles from './PlanPage.module.css'
import { Button } from '../ui/Button'

type ReadState<T> =
  | { readonly type: 'idle' | 'loading' }
  | { readonly type: 'ready'; readonly data: T }
  | { readonly type: 'failed'; readonly message: string }

type Attempt =
  | { readonly phase: 'idle' }
  | { readonly phase: 'submitting'; readonly snapshot: PendingPlanCreate }
  | { readonly phase: 'unknown'; readonly snapshot: PendingPlanCreate; readonly message: string }
  | {
      readonly phase: 'failed'
      readonly message: string
      readonly snapshot?: PendingPlanCreate
      readonly unresolvedSince?: string
    }
  | { readonly phase: 'completed'; readonly result: Extract<SavePlanOutcome, { type: 'applied' }> }
  /** The stored slot cannot be trusted either way: nothing is sent from this form (M01.3 K2 #728/6180459736). */
  | { readonly phase: 'blocked'; readonly message: string }

const UNKNOWN_TEXT = PLAN_CREATE_TEXT.unknown

const BLOCKED_TEXT = PLAN_CREATE_TEXT.blocked

/**
 * M01.3 r5 (#66/6187473245): the server answered `authenticationSessionBinding: null`. Without a server session identity
 * no pending create can be bound to this session, so none is stored or sent; nothing local stands in for the server.
 */
const NO_SESSION_TEXT = PLAN_CREATE_TEXT.noSession

const NOT_DURABLE_TEXT = PLAN_CREATE_TEXT.notDurable

/** Only a proven-empty slot opens the form; an unreadable one keeps it closed for this mount. */
function initialAttempt(slot: PendingPlanCreateSlot, sessionBinding: string | null): Attempt {
  if (slot.state === 'absent') return { phase: 'idle' }
  if (slot.state === 'pending') return { phase: 'unknown', snapshot: slot.pending, message: UNKNOWN_TEXT }
  return { phase: 'blocked', message: sessionBinding === null ? NO_SESSION_TEXT : BLOCKED_TEXT }
}

/** AT-063: the roll an ST-01 row named, as far as Inventory's candidate read confirmed it. */
type Preselection =
  | { readonly type: 'none' | 'loading' }
  | { readonly type: 'selected'; readonly labelCode: string }
  | { readonly type: 'refused'; readonly labelCode: string; readonly code: string }
  | { readonly type: 'missing'; readonly windowFull: boolean }
  | { readonly type: 'failed'; readonly message: string }

interface PreselectedLabel {
  readonly labelId: string
  readonly labelCode: string
}

interface SelectedInput {
  readonly candidate: ParentRollCandidate
  readonly plannedKg: string
}

interface SlitLineForm {
  readonly id: string
  readonly bandWidthMm: string
  readonly count: string
}

function newId(): string {
  const bytes = new Uint8Array(16)
  globalThis.crypto.getRandomValues(bytes)
  bytes[6] = (bytes[6] & 0x0f) | 0x40
  bytes[8] = (bytes[8] & 0x3f) | 0x80
  const hex = Array.from(bytes, (value) => value.toString(16).padStart(2, '0')).join('')
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
}

function commandFailure(outcome: Extract<SavePlanOutcome, { type: 'failed' }>): string {
  return saveFailureText(outcome.status, outcome.code)
}

function positiveKilograms(value: string): boolean {
  return isKilogramText(value) && /[1-9]/.test(value)
}

export function PlanCreatePage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const location = useLocation()
  const preselect = parsePlanCreateLabel(location.search)
  return <PlanCreate
    key={`${namespace ?? 'no-context'}:${shell.routeEntryGeneration}:${preselect?.labelId ?? ''}`}
    namespace={namespace}
    preselect={preselect}
  />
}

function PlanCreate({ namespace, preselect }: {
  readonly namespace: string | null
  readonly preselect: PreselectedLabel | null
}) {
  const shell = useShellState()
  const authBoundary = useAuthBoundary()
  const { capture: captureSecurity, report: reportSecurity } = authBoundary
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const actorId = shell.activeContext?.userId ?? null
  const sessionBinding = shell.sessionBinding
  const storeKey = pendingPlanCreateKey(sessionBinding, actorId, companyId)
  const [attempt, setAttempt] = useState<Attempt>(() => initialAttempt(readPendingPlanCreate(storeKey), sessionBinding))
  const [workCenters, setWorkCenters] = useState<ReadState<readonly WorkCenterSummary[]>>({ type: 'loading' })
  const [workCenterId, setWorkCenterId] = useState('')
  const [search, setSearch] = useState(preselect?.labelCode ?? '')
  const [preselection, setPreselection] = useState<Preselection>({ type: preselect === null ? 'none' : 'loading' })
  const preselectId = preselect?.labelId ?? null
  const preselectCode = preselect?.labelCode ?? ''
  const [candidates, setCandidates] = useState<ReadState<ParentRollCandidates>>({ type: 'idle' })
  const [inputs, setInputs] = useState<readonly SelectedInput[]>([])
  const [targetBasis, setTargetBasis] = useState<'' | PlanTargetBasis>('')
  const [targetKg, setTargetKg] = useState('')
  const [slittingEnabled, setSlittingEnabled] = useState(false)
  const [slitLines, setSlitLines] = useState<readonly SlitLineForm[]>([])
  const [windingTargetKg, setWindingTargetKg] = useState('')
  // M01.3: no row by default — a CutToLength plan without authored rows sends no cut definition.
  const [cutLines, setCutLines] = useState<readonly CutToLengthLineDraft[]>([])
  const [formError, setFormError] = useState<string | null>(null)
  const candidateGeneration = useRef(0)
  const commandGeneration = useRef(0)
  const inFlight = useRef(false)

  useEffect(() => {
    const controller = new AbortController()
    async function read() {
      if (companyId === null) return
      setWorkCenters({ type: 'loading' })
      const security = captureSecurity()
      const outcome = await fetchWorkCenters(companyId, controller.signal)
      if (controller.signal.aborted) return
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        reportSecurity(outcome.status, security)
      }
      setWorkCenters(
        outcome.type === 'ok-single'
          ? { type: 'ready', data: outcome.data }
          : { type: 'failed', message: readFailureText(PLAN_FORM_TEXT.workCenter, outcome) },
      )
    }
    void read()
    return () => {
      controller.abort()
      candidateGeneration.current += 1
      commandGeneration.current += 1
    }
  }, [captureSecurity, companyId, namespace, reportSecurity])

  // AT-063 "Üretim planla" from ST-01: the named label is looked up through the SAME Inventory candidate read a manual
  // search uses (its code as the query, the owner's widest window) and selected only when that exact id answers as
  // draft-selectable. Nothing is trusted from the URL; the save still sends the candidate's label version.
  useEffect(() => {
    if (companyId === null || preselectId === null) return
    const controller = new AbortController()
    const generation = ++candidateGeneration.current
    async function read(company: string, labelId: string) {
      setCandidates({ type: 'loading' })
      const security = captureSecurity()
      const outcome = await fetchParentRollCandidates(company, preselectCode, controller.signal, PARENT_ROLL_PRESELECT_LIMIT)
      if (controller.signal.aborted || generation !== candidateGeneration.current) return
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        reportSecurity(outcome.status, security)
      }
      if (outcome.type !== 'ok-single') {
        const message = readFailureText(PLAN_FORM_TEXT.parentRoll, outcome)
        setCandidates({ type: 'failed', message })
        setPreselection({ type: 'failed', message })
        return
      }
      setCandidates({ type: 'ready', data: outcome.data })
      const found = outcome.data.items.find((item) => item.labelId.toLowerCase() === labelId.toLowerCase())
      if (found === undefined) {
        setPreselection({ type: 'missing', windowFull: outcome.data.searchWindowFull })
        return
      }
      if (!found.draftSelectable) {
        setPreselection({ type: 'refused', labelCode: found.labelCode, code: found.draftRefusalCode ?? 'refused' })
        return
      }
      setInputs((current) => current.some((input) => input.candidate.labelId === found.labelId)
        ? current
        : [...current, { candidate: found, plannedKg: '' }])
      setPreselection({ type: 'selected', labelCode: found.labelCode })
    }
    void read(companyId, preselectId)
    return () => controller.abort()
  }, [captureSecurity, companyId, preselectCode, preselectId, reportSecurity])

  const readCandidates = useCallback(async () => {
    if (companyId === null) return
    const generation = ++candidateGeneration.current
    const controller = new AbortController()
    setCandidates({ type: 'loading' })
    const security = captureSecurity()
    const outcome = await fetchParentRollCandidates(companyId, search, controller.signal)
    if (generation !== candidateGeneration.current) return
    if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
      reportSecurity(outcome.status, security)
    }
    setCandidates(
      outcome.type === 'ok-single'
        ? { type: 'ready', data: outcome.data }
        : { type: 'failed', message: readFailureText(PLAN_FORM_TEXT.parentRoll, outcome) },
    )
  }, [captureSecurity, companyId, reportSecurity, search])

  const send = useCallback(
    async (snapshot: PendingPlanCreate, unresolvedSince?: string) => {
      if (
        companyId === null ||
        actorId === null ||
        snapshot.companyId !== companyId ||
        snapshot.actorId !== actorId ||
        inFlight.current
      ) {
        return
      }
      // Nothing is sent unless the exact attempt is durably stored for a reload first.
      const reservation = reservePendingPlanCreate(storeKey, snapshot)
      if (reservation.type === 'unreadable') {
        setAttempt({ phase: 'blocked', message: sessionBinding === null ? NO_SESSION_TEXT : BLOCKED_TEXT })
        return
      }
      if (reservation.type === 'occupied') {
        setAttempt({ phase: 'unknown', snapshot: reservation.pending, message: UNKNOWN_TEXT })
        return
      }
      if (reservation.type === 'not-durable') {
        setAttempt({ phase: 'failed', message: NOT_DURABLE_TEXT })
        return
      }
      inFlight.current = true
      const generation = ++commandGeneration.current
      setAttempt({ phase: 'submitting', snapshot })
      const security = captureSecurity()
      const outcome = await savePlan(snapshot.request, {
        companyId: snapshot.companyId,
        idempotencyKey: snapshot.idempotencyKey,
      })
      if (generation !== commandGeneration.current) return
      inFlight.current = false
      if (outcome.type === 'failed' && (outcome.status === 401 || outcome.status === 403)) {
        reportSecurity(outcome.status, security)
      }
      if (outcome.type === 'unknown') {
        setAttempt({
          phase: 'unknown',
          snapshot,
          message: unresolvedSince ?? PLAN_CREATE_TEXT.sendUnknown,
        })
        return
      }
      if (outcome.type === 'failed' && unresolvedSince !== undefined) {
        setAttempt({
          phase: 'failed',
          snapshot,
          unresolvedSince,
          message: commandFailure(outcome),
        })
        return
      }
      releasePendingPlanCreate(storeKey, snapshot.idempotencyKey)
      setAttempt(
        outcome.type === 'applied'
          ? { phase: 'completed', result: outcome }
          : { phase: 'failed', message: commandFailure(outcome) },
      )
    },
    [actorId, captureSecurity, companyId, reportSecurity, sessionBinding, storeKey],
  )

  // AT-061 / AT-062: the button-less backend preview of what is typed (DEC-PRDCT-005). It writes nothing and never
  // gates the save below.
  const selectedWorkCenter = workCenters.type === 'ready'
    ? workCenters.data.find((workCenter) => workCenter.id === workCenterId)
    : undefined
  const slittingActive = selectedWorkCenter?.kind === 'Slitting' && slittingEnabled
  const cutToLengthLine = selectedWorkCenter?.kind === 'CutToLength'
  const previewInput = planPreviewInput(
    workCenterId,
    inputs.map(({ candidate, plannedKg }) => ({
      parentRollId: candidate.labelId,
      label: candidate.labelCode,
      labelVersion: candidate.labelVersion,
      plannedKg,
    })),
    targetBasis,
    targetKg,
    {
      enabled: slittingActive,
      lines: slitLines.map((line, index) => ({ ...line, label: PLAN_CREATE_TEXT.band(index + 1) })),
      windingTargetKg,
    },
    // Authored rows are validated before preview AND save; only a CutToLength line sends them.
    cutToLengthLine && cutLines.length > 0 ? cutToLengthDraftInput(cutLines) : null,
  )
  const preview = usePlanPreview(companyId, previewInput, authBoundary)

  function labelOf(parentRollId: string): string {
    return inputs.find((input) => input.candidate.labelId === parentRollId)?.candidate.labelCode ?? parentRollId
  }

  function workCenterLabel(id: string): string {
    const found = workCenters.type === 'ready' ? workCenters.data.find((workCenter) => workCenter.id === id) : undefined
    return found === undefined ? id : `${found.code} — ${found.name}`
  }

  function addCandidate(candidate: ParentRollCandidate) {
    if (!candidate.draftSelectable || inputs.some((input) => input.candidate.labelId === candidate.labelId)) return
    setInputs((current) => [...current, { candidate, plannedKg: '' }])
  }

  function submit(event: FormEvent) {
    event.preventDefault()
    if (
      companyId === null ||
      actorId === null ||
      attempt.phase === 'submitting' ||
      attempt.phase === 'unknown' ||
      attempt.phase === 'blocked' ||
      (attempt.phase === 'failed' && attempt.unresolvedSince !== undefined)
    ) {
      return
    }
    if (workCenterId === '' || inputs.length === 0 || inputs.some((input) => !positiveKilograms(input.plannedKg))) {
      setFormError(PLAN_FORM_TEXT.formRequired)
      return
    }
    if ((targetBasis === '') !== (targetKg === '') || (targetKg !== '' && !positiveKilograms(targetKg))) {
      setFormError(PLAN_FORM_TEXT.targetPair)
      return
    }
    if (previewInput.type !== 'ready') {
      setFormError(previewInput.reasons.join(' '))
      return
    }
    setFormError(null)
    const request: SavePlanRequest = {
      planId: newId(),
      workCenterId,
      inputs: inputs.map(({ candidate, plannedKg }) => ({
        parentRollId: candidate.labelId,
        plannedKg,
        expectedLabelVersion: candidate.labelVersion,
      })),
      target: targetBasis === '' ? null : { basis: targetBasis, kg: targetKg },
      slittingSpec: previewInput.request.slittingSpec ?? null,
      // The same validated ordered definition the preview sent; omitted when no row was authored.
      ...(previewInput.request.cutToLengthSpec === undefined ? {} : { cutToLengthSpec: previewInput.request.cutToLengthSpec }),
    }
    void send({ actorId, companyId, idempotencyKey: newId(), request })
  }

  if (companyId === null || actorId === null) {
    return <main className={styles.page}><p role="alert">{PLAN_CREATE_TEXT.noContext}</p></main>
  }
  if (attempt.phase === 'blocked') {
    return (
      <main className={styles.page} aria-labelledby="manufacturing-plan-create-heading">
        <header className={styles.header}>
          <div>
            <p className={styles.eyebrow}>{PLAN_FORM_TEXT.eyebrow}</p>
            <h1 id="manufacturing-plan-create-heading">{PLAN_CREATE_TEXT.heading}</h1>
          </div>
          <Link to={orderListPath()}>{PLAN_FORM_TEXT.backToOrders}</Link>
        </header>
        <p className={styles.error} role="alert">{attempt.message}</p>
      </main>
    )
  }
  if (attempt.phase === 'completed') {
    const plan = attempt.result.plan
    return (
      <main className={styles.page}>
        <section className={styles.successCard} role="status">
          <p className={styles.eyebrow}>{PLAN_FORM_TEXT.eyebrow}</p>
          <h1>{PLAN_CREATE_TEXT.savedTitle}</h1>
          <p className={styles.mono}>{PLAN_CREATE_TEXT.savedReference(plan.planId, plan.version)}</p>
          {attempt.result.replayed ? <p>{PLAN_CREATE_TEXT.replayed}</p> : null}
          <div className={styles.actions}>
            {hasCapability(shell, MANUFACTURING_CAPABILITY.planDetail) ? (
              <Link className={styles.primaryLink} to={planDetailPath(plan.planId)}>
                {PLAN_CREATE_TEXT.reopen}
              </Link>
            ) : null}
            <Link className={styles.secondary} to={orderListPath()}>{PLAN_CREATE_TEXT.backToOrders}</Link>
          </div>
        </section>
      </main>
    )
  }

  const unresolved = attempt.phase === 'unknown' || (attempt.phase === 'failed' && attempt.unresolvedSince !== undefined)
  const frozen = attempt.phase === 'submitting' || unresolved
  const retrySnapshot = attempt.phase === 'unknown' || attempt.phase === 'failed' ? attempt.snapshot : undefined
  const retryReason = attempt.phase === 'unknown' ? attempt.message : attempt.phase === 'failed' ? attempt.unresolvedSince : undefined

  return (
    <main className={styles.page} aria-labelledby="manufacturing-plan-create-heading">
      <header className={styles.header}>
        <div>
          <p className={styles.eyebrow}>{PLAN_FORM_TEXT.eyebrow}</p>
          <h1 id="manufacturing-plan-create-heading">{PLAN_CREATE_TEXT.heading}</h1>
        </div>
        <Link to={orderListPath()}>{PLAN_FORM_TEXT.backToOrders}</Link>
      </header>
      {unresolved && retrySnapshot !== undefined ? (
        <section className={styles.warningCard} role="alert">
          <p>{attempt.phase === 'failed' ? attempt.message : attempt.message}</p>
          <p>{PLAN_CREATE_TEXT.unresolvedNote}</p>
          <div className={styles.actions}>
            <Button type="button" onClick={() => void send(retrySnapshot, retryReason)}>
              {PLAN_CREATE_TEXT.resend}
            </Button>
            <Link className={styles.secondary} to={orderListPath()}>{PLAN_CREATE_TEXT.closeForm}</Link>
          </div>
        </section>
      ) : null}
      {attempt.phase === 'failed' && !unresolved ? <p className={styles.error} role="alert">{attempt.message}</p> : null}
      {formError === null ? null : <p className={styles.error} role="alert">{formError}</p>}
      {preselection.type === 'loading' ? <p role="status">{PLAN_CREATE_TEXT.preselectLoading}</p> : null}
      {preselection.type === 'selected' ? (
        <p role="status" className={styles.note}>
          {PLAN_CREATE_TEXT.preselectAdded(preselection.labelCode)}
        </p>
      ) : null}
      {preselection.type === 'refused' ? (
        <p role="alert" className={styles.error}>
          {PLAN_CREATE_TEXT.preselectRefused(preselection.labelCode, preselection.code)}
        </p>
      ) : null}
      {preselection.type === 'missing' ? (
        <p role="alert" className={styles.error}>
          {PLAN_CREATE_TEXT.preselectMissing(preselection.windowFull)}
        </p>
      ) : null}
      {preselection.type === 'failed' ? <p role="alert" className={styles.error}>{preselection.message}</p> : null}
      <form className={styles.form} onSubmit={submit} noValidate>
        <fieldset disabled={frozen}>
          <legend>{PLAN_FORM_TEXT.workCenter}</legend>
          {workCenters.type === 'loading' ? <p role="status">{PLAN_FORM_TEXT.workCentersLoading}</p> : null}
          {workCenters.type === 'failed' ? <p role="alert">{workCenters.message}</p> : null}
          {workCenters.type === 'ready' ? (
            <select aria-label={PLAN_FORM_TEXT.workCenter} value={workCenterId} onChange={(event) => setWorkCenterId(event.target.value)}>
              <option value="">{PLAN_CREATE_TEXT.choose}</option>
              {workCenters.data.map((workCenter) => (
                <option key={workCenter.id} value={workCenter.id}>
                  {workCenter.code} — {workCenter.name}{workCenter.isActive ? '' : PLAN_FORM_TEXT.inactiveSuffix}
                </option>
              ))}
            </select>
          ) : null}
        </fieldset>

        <fieldset disabled={frozen}>
          <legend>{PLAN_CREATE_TEXT.candidates}</legend>
          <p className={styles.note}>
            {PLAN_CREATE_TEXT.candidatesNote}
          </p>
          <div className={styles.searchRow}>
            <label htmlFor="parent-roll-search">{PLAN_CREATE_TEXT.search}</label>
            <input id="parent-roll-search" maxLength={64} value={search} onChange={(event) => setSearch(event.target.value)} />
            <Button type="button" onClick={() => void readCandidates()}>{PLAN_CREATE_TEXT.searchAction}</Button>
          </div>
          {candidates.type === 'loading' ? <p role="status">{PLAN_FORM_TEXT.rollsLoading}</p> : null}
          {candidates.type === 'failed' ? <p role="alert">{candidates.message}</p> : null}
          {candidates.type === 'ready' ? (
            <>
              <div className={styles.tableWrap}>
                <table className={styles.table}>
                  <thead><tr><th scope="col">{PLAN_FORM_TEXT.label}</th><th scope="col">{PLAN_CREATE_TEXT.productLot}</th><th scope="col">{PLAN_FORM_TEXT.physicalKg}</th><th scope="col">{PLAN_CREATE_TEXT.document}</th><th scope="col">{PLAN_FORM_TEXT.choice}</th></tr></thead>
                  <tbody>
                    {candidates.data.items.map((candidate) => {
                      const selected = inputs.some((input) => input.candidate.labelId === candidate.labelId)
                      return (
                        <tr key={candidate.labelId}>
                          <th scope="row">{candidate.labelCode}<div className={styles.mono}>{candidate.labelId}</div></th>
                          <td><span className={styles.mono}>{candidate.productId}</span><br />{PLAN_CREATE_TEXT.lot(candidate.lot.id)}</td>
                          <td>{candidate.physicalKg} kg<br /><span className={styles.muted}>{PLAN_CREATE_TEXT.candidateBalances(candidate.unblockedPhysicalKg, candidate.blockedPhysicalKg)}</span></td>
                          <td>{candidate.documentStatus}</td>
                          <td>
                            <Button type="button" disabled={!candidate.draftSelectable || selected} onClick={() => addCandidate(candidate)}>
                              {selected ? PLAN_CREATE_TEXT.added : candidate.draftSelectable ? PLAN_FORM_TEXT.add : PLAN_FORM_TEXT.unselectable}
                            </Button>
                            {!candidate.draftSelectable ? <div className={styles.muted}>{candidate.draftRefusalCode}</div> : null}
                          </td>
                        </tr>
                      )
                    })}
                  </tbody>
                </table>
              </div>
              {candidates.data.searchWindowFull ? <p className={styles.note}>{PLAN_CREATE_TEXT.searchWindowFull}</p> : null}
            </>
          ) : null}
        </fieldset>

        <fieldset disabled={frozen}>
          <legend>{PLAN_FORM_TEXT.plannedInputs}</legend>
          {inputs.length === 0 ? <p>{PLAN_CREATE_TEXT.noInputs}</p> : (
            <div className={styles.tableWrap}>
              <table className={styles.table}>
                <thead><tr><th scope="col">{PLAN_FORM_TEXT.label}</th><th scope="col">{PLAN_FORM_TEXT.labelVersion}</th><th scope="col">{PLAN_FORM_TEXT.plannedKg}</th><th scope="col">{PLAN_FORM_TEXT.action}</th></tr></thead>
                <tbody>
                  {inputs.map((input) => (
                    <tr key={input.candidate.labelId}>
                      <th scope="row">{input.candidate.labelCode}</th>
                      <td>{input.candidate.labelVersion}</td>
                      <td>
                        <input
                          aria-label={PLAN_FORM_TEXT.plannedKgFor(input.candidate.labelCode)}
                          inputMode="decimal"
                          value={input.plannedKg}
                          onChange={(event) => setInputs((current) => current.map((row) => row.candidate.labelId === input.candidate.labelId ? { ...row, plannedKg: event.target.value } : row))}
                        />
                      </td>
                      <td><Button type="button" onClick={() => setInputs((current) => current.filter((row) => row.candidate.labelId !== input.candidate.labelId))}>{PLAN_FORM_TEXT.remove}</Button></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </fieldset>

        <fieldset disabled={frozen}>
          <legend>{PLAN_FORM_TEXT.optionalTarget}</legend>
          <div className={styles.formGrid}>
            <label htmlFor="plan-target-basis">{PLAN_FORM_TEXT.targetBasis}</label>
            <select id="plan-target-basis" value={targetBasis} onChange={(event) => setTargetBasis(event.target.value as '' | PlanTargetBasis)}>
              <option value="">{PLAN_FORM_TEXT.targetNone}</option>
              <option value="InputMass">{TARGET_BASIS_LABEL.InputMass}</option>
              <option value="GoodOutputMass">{TARGET_BASIS_LABEL.GoodOutputMass}</option>
            </select>
            <label htmlFor="plan-target-kg">{PLAN_FORM_TEXT.targetKg}</label>
            <input id="plan-target-kg" inputMode="decimal" value={targetKg} onChange={(event) => setTargetKg(event.target.value)} />
          </div>
        </fieldset>
        {selectedWorkCenter?.kind === 'Slitting' ? (
          <fieldset disabled={frozen}>
            <legend>{PLAN_FORM_TEXT.slitting}</legend>
            <label>
              <input
                type="checkbox"
                checked={slittingEnabled}
                onChange={(event) => {
                  const checked = event.target.checked
                  setSlittingEnabled(checked)
                  if (checked && slitLines.length === 0) {
                    setSlitLines([{ id: newId(), bandWidthMm: '', count: '' }])
                  }
                }}
              />{' '}
              {PLAN_CREATE_TEXT.addSlitting}
            </label>
            {slittingEnabled ? (
              <>
                <p className={styles.note}>{PLAN_CREATE_TEXT.slittingNote}</p>
                <div className={styles.tableWrap}>
                  <table className={styles.table}>
                    <thead><tr><th scope="col">{PLAN_FORM_TEXT.sequence}</th><th scope="col">{PLAN_CREATE_TEXT.bandWidthMm}</th><th scope="col">{PLAN_CREATE_TEXT.count}</th><th scope="col">{PLAN_FORM_TEXT.action}</th></tr></thead>
                    <tbody>
                      {slitLines.map((line, index) => (
                        <tr key={line.id}>
                          <th scope="row">{index + 1}</th>
                          <td><input aria-label={PLAN_CREATE_TEXT.bandWidthFor(index + 1)} inputMode="decimal" value={line.bandWidthMm} onChange={(event) => setSlitLines((current) => current.map((item) => item.id === line.id ? { ...item, bandWidthMm: event.target.value } : item))} /></td>
                          <td><input aria-label={PLAN_CREATE_TEXT.bandCountFor(index + 1)} inputMode="numeric" value={line.count} onChange={(event) => setSlitLines((current) => current.map((item) => item.id === line.id ? { ...item, count: event.target.value } : item))} /></td>
                          <td><Button type="button" onClick={() => setSlitLines((current) => current.filter((item) => item.id !== line.id))}>{PLAN_FORM_TEXT.remove}</Button></td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
                <div className={styles.actions}>
                  <Button type="button" onClick={() => setSlitLines((current) => [...current, { id: newId(), bandWidthMm: '', count: '' }])}>{PLAN_CREATE_TEXT.addBand}</Button>
                </div>
                <div className={styles.formGrid}>
                  <label htmlFor="plan-winding-target-kg">{PLAN_CREATE_TEXT.windingTarget}</label>
                  <input id="plan-winding-target-kg" inputMode="decimal" value={windingTargetKg} onChange={(event) => setWindingTargetKg(event.target.value)} />
                </div>
              </>
            ) : null}
          </fieldset>
        ) : null}
        {cutToLengthLine ? (
          <fieldset disabled={frozen}>
            <legend>{CUT_LINE_TEXT.legend}</legend>
            <CutToLengthLinesEditor idPrefix="plan-create-cut" lines={cutLines} onChange={setCutLines} keepOne={false} />
          </fieldset>
        ) : null}
        <PlanPreviewPanel state={preview} labelOf={labelOf} workCenterLabel={workCenterLabel} />
        <div className={styles.actions}>
          <Button variant="primary" type="submit" disabled={frozen}>{attempt.phase === 'submitting' ? PLAN_CREATE_TEXT.saving : PLAN_CREATE_TEXT.save}</Button>
        </div>
      </form>
    </main>
  )
}
