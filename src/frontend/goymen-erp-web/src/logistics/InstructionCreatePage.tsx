/**
 * L-03 Yeni Yükleme Talimatı — a Draft with lines on APPROVED sales lines, pinned to
 * `POST /api/v1/logistics/loading-instructions` (LOG-4a, #350).
 *
 * The screen is thin on purpose; the rules live in tested modules:
 *  - ./contract.ts — the wire shape and the quantity TEXT canonicalisation (no JS number, ever);
 *  - ./instructionAttempt.ts — one key per snapshot, unknown keeps it, a changed payload does not
 *    open a second attempt while the first is unresolved;
 *  - ./api.ts — 201 first commit vs 201 + replay, and "this answer describes THIS attempt".
 *
 * The customer select and the line picker read the Sales owner's APPROVED drafts (the list with
 * `approvalState=Approved`, then each draft's lines) — ASSUMPTION-LOG4B-1. Nothing is guessed: no
 * approved draft means no customer to pick; the picker shows the document, the line no, the product
 * name, the ordered quantity text and the unit id — and never a price (Business 5743412166).
 * The client sends no product: the Sales owner answers it from the approved line on the server.
 */

import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useNavigate } from 'react-router'
import { createInstruction, fetchApprovedDraftLines, fetchApprovedDrafts, type ApprovedDraftLines, type SourceOutcome } from './api'
import {
  LIMITS,
  LOGISTICS_PATH,
  isDateOnly,
  parseQuantityInput,
  type ApprovedDraftSummary,
  type CreateInstructionRequest,
  type InstructionLineRequest,
  type InstructionMutation,
  type QuantityInputError,
} from './contract'
import {
  abandon,
  applyOutcome,
  attemptBinding,
  beginSend,
  decideSubmit,
  generateAttemptKey,
  generateClientId,
  isOutcomeStillUnknown,
  liveSnapshot,
  type AttemptSnapshot,
  type AttemptState,
} from './instructionAttempt'
import { createNotice } from './instructionAttemptNotice'
import { LOGISTICS_CAPABILITY, LOGISTICS_ROUTE, instructionDetailRoute } from './capabilities'
import { INSTRUCTION_CREATE_TEXT as T, problemCodeText } from './logisticsText'
import { todayDateOnly } from './format'
import { useShellDispatch, useShellState } from '../shell/shellContext'
import { hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { useCompanyDirectory } from '../shell/useShellOrchestrator'
import styles from './LogisticsPages.module.css'

/** The shell's dirty scope: a company switch ASKS while this form holds work. */
export const LOGISTICS_CREATE_DIRTY_SCOPE = 'logistics.instruction.create'

type CreateAttempt = AttemptState<CreateInstructionRequest, InstructionMutation>

export function InstructionCreatePage() {
  const shell = useShellState()
  const binding = attemptBinding(shell, LOGISTICS_CREATE_DIRTY_SCOPE)
  const companyId = shell.activeContext?.companyId ?? null
  if (binding === null || companyId === null) {
    // A group view is not a write target; nothing can be bound here.
    return (
      <section className={styles.page}>
        <h1 className={styles.title}>{T.heading}</h1>
        <p role="status">{T.singleCompanyOnly}</p>
      </section>
    )
  }
  // A new actor, session or company is a new form; a capability re-check is not.
  return <CreateForm key={binding} binding={binding} companyId={companyId} />
}

/** A picked approved line with the instructed quantity the person typed; `lineId` is minted once and kept on retry. */
interface PickedLine {
  readonly lineId: string
  readonly sourceOrderId: string
  readonly sourceOrderLineId: string
  readonly unitId: string
  readonly quantityText: string
}

interface CustomerOption {
  readonly key: string
  readonly partyId: string
  readonly companyAccountId: string
  readonly label: string
  readonly draftIds: readonly string[]
}

/** The distinct (party, account) pairs of the approved drafts, labelled by their document numbers. */
export function customerOptions(drafts: readonly ApprovedDraftSummary[]): readonly CustomerOption[] {
  const byKey = new Map<string, { partyId: string; companyAccountId: string; documents: string[]; draftIds: string[] }>()
  for (const draft of drafts) {
    const key = `${draft.partyId}|${draft.companyAccountId}`
    const entry = byKey.get(key) ?? { partyId: draft.partyId, companyAccountId: draft.companyAccountId, documents: [], draftIds: [] }
    entry.documents.push(draft.documentNo)
    entry.draftIds.push(draft.id)
    byKey.set(key, entry)
  }
  return [...byKey.entries()].map(([key, entry]) => ({
    key,
    partyId: entry.partyId,
    companyAccountId: entry.companyAccountId,
    label: entry.documents.join(', '),
    draftIds: entry.draftIds,
  }))
}

function quantityError(error: QuantityInputError): string {
  switch (error) {
    case 'required':
      return T.validation.quantityRequired
    case 'invalid':
      return T.validation.quantityInvalid
    case 'ambiguous-separator':
      return T.validation.quantityAmbiguous
    case 'not-positive':
      return T.validation.quantityNotPositive
    case 'scale':
      return T.validation.quantityScale
    default:
      return T.validation.quantityTooWide
  }
}

function CreateForm({ binding, companyId }: { readonly binding: string; readonly companyId: string }) {
  const shell = useShellState()
  const dispatch = useShellDispatch()
  const boundary = useAuthBoundary()
  const navigate = useNavigate()
  const directory = useCompanyDirectory()
  const company = directory.find((entry) => entry.id === companyId)

  const [effectiveOn, setEffectiveOn] = useState(() => todayDateOnly())
  const [customerKey, setCustomerKey] = useState('')
  const [picked, setPicked] = useState<readonly PickedLine[]>([])
  const [attempt, setAttempt] = useState<CreateAttempt>({ phase: 'idle' })
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [serverMessages, setServerMessages] = useState<readonly string[]>([])
  const [blockedChange, setBlockedChange] = useState(false)
  const [summaryRequest, setSummaryRequest] = useState(0)
  const summaryRef = useRef<HTMLDivElement>(null)

  const attemptRef = useRef(attempt)
  const currentBindingRef = useRef<string | null>(binding)
  const mountedRef = useRef(false)
  const liveBinding = attemptBinding(shell, LOGISTICS_CREATE_DIRTY_SCOPE)
  useEffect(() => {
    attemptRef.current = attempt
    currentBindingRef.current = liveBinding
  })
  useEffect(() => {
    mountedRef.current = true
    return () => {
      mountedRef.current = false
    }
  }, [])

  const dirty = isOutcomeStillUnknown(attempt) || (attempt.phase !== 'completed' && (picked.length > 0 || customerKey !== ''))
  useEffect(() => {
    dispatch({ type: dirty ? 'DIRTY_SET' : 'DIRTY_CLEAR', scope: LOGISTICS_CREATE_DIRTY_SCOPE })
  }, [dirty, dispatch])
  useEffect(() => () => dispatch({ type: 'DIRTY_CLEAR', scope: LOGISTICS_CREATE_DIRTY_SCOPE }), [dispatch])
  useEffect(() => {
    if (summaryRequest > 0) summaryRef.current?.focus()
  }, [summaryRequest])

  // The approved drafts (the customer select's source), read from the real Sales list; never a default.
  const drafts = useQuery({
    queryKey: [binding, 'logistics-approved-drafts'],
    queryFn: async ({ signal }) => {
      const security = boundary.capture()
      const outcome = await fetchApprovedDrafts(companyId, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, security)
      return outcome
    },
  })
  const options = useMemo(() => (drafts.data?.type === 'ready' ? customerOptions(drafts.data.data.items) : []), [drafts.data])
  const customer = options.find((option) => option.key === customerKey) ?? null

  // The chosen customer's approved lines, one detail read per draft; read only after a customer is picked.
  const lines = useQuery({
    queryKey: [binding, 'logistics-approved-lines', customerKey],
    enabled: customer !== null,
    queryFn: async ({ signal }) => {
      const security = boundary.capture()
      const result: ApprovedDraftLines[] = []
      for (const draftId of customer?.draftIds ?? []) {
        const outcome = await fetchApprovedDraftLines(draftId, companyId, signal)
        if (outcome.type === 'http') boundary.report(outcome.status, security)
        if (outcome.type !== 'ready') {
          const failed: SourceOutcome<readonly ApprovedDraftLines[]> = outcome
          return failed
        }
        result.push(outcome.data)
      }
      const ready: SourceOutcome<readonly ApprovedDraftLines[]> = { type: 'ready', data: result }
      return ready
    },
  })

  const send = useCallback(
    async (snapshot: AttemptSnapshot<CreateInstructionRequest>) => {
      const previous = attemptRef.current
      const inFlight: CreateAttempt = beginSend<CreateInstructionRequest, InstructionMutation>(snapshot)
      attemptRef.current = inFlight
      setAttempt(inFlight)
      setServerMessages([])
      const security = boundary.capture()
      // Company and key travel FROM THE SNAPSHOT, never from the shell's current state.
      const outcome = await createInstruction(snapshot.payload, { idempotencyKey: snapshot.key, companyId: snapshot.companyId })
      const next = applyOutcome(inFlight, snapshot, snapshot.binding, outcome, previous)
      if (outcome.type === 'http') boundary.report(outcome.status, security)
      // Departed: another company, actor or session, or no screen at all. Nothing is painted or navigated.
      if (!mountedRef.current || currentBindingRef.current !== snapshot.binding) return
      attemptRef.current = next
      setAttempt(next)
      if (next.phase === 'failed' && next.problem !== null && next.problem.errors.length > 0) {
        const fieldErrors: Record<string, string> = {}
        const unplaced: string[] = []
        for (const error of next.problem.errors) {
          const message = `${error.field}: ${problemCodeText(error.code) ?? error.code}`
          const match = /^lines\[(\d+)\]/.exec(error.field)
          const line = match === null ? undefined : snapshot.payload.lines[Number(match[1])]
          if (line === undefined) unplaced.push(message)
          else fieldErrors[`logistics-line-${line.lineId}-quantity`] = message
        }
        setErrors(fieldErrors)
        setServerMessages(unplaced)
        setSummaryRequest((n) => n + 1)
      }
    },
    [boundary],
  )

  function validate(): { readonly ok: true; readonly payload: CreateInstructionRequest } | { readonly ok: false; readonly errors: Record<string, string> } {
    const found: Record<string, string> = {}
    if (effectiveOn.trim() === '') found['logistics-effective-on'] = T.validation.effectiveOnRequired
    else if (!isDateOnly(effectiveOn)) found['logistics-effective-on'] = T.validation.dateInvalid
    if (customer === null) found['logistics-customer'] = customerKey === '' ? T.validation.customerRequired : T.validation.customerNotOffered
    if (picked.length === 0) found['logistics-lines'] = T.validation.linesRequired
    if (picked.length > LIMITS.lines) found['logistics-lines'] = T.validation.tooManyLines
    const requestLines: InstructionLineRequest[] = []
    for (const line of picked) {
      const parsed = parseQuantityInput(line.quantityText)
      if (!parsed.ok) {
        found[`logistics-line-${line.lineId}-quantity`] = quantityError(parsed.error)
        continue
      }
      requestLines.push({
        lineId: line.lineId,
        sourceOrderId: line.sourceOrderId,
        sourceOrderLineId: line.sourceOrderLineId,
        unitId: line.unitId,
        quantity: parsed.value,
      })
    }
    if (Object.keys(found).length > 0 || customer === null) return { ok: false, errors: found }
    return {
      ok: true,
      payload: { customerPartyId: customer.partyId, companyAccountId: customer.companyAccountId, effectiveOn, lines: requestLines },
    }
  }

  function onSubmit(event: React.FormEvent) {
    event.preventDefault()
    const validation = validate()
    if (!validation.ok) {
      // Nothing was sent, so no key is minted.
      setErrors(validation.errors)
      setServerMessages([])
      setSummaryRequest((n) => n + 1)
      return
    }
    setErrors({})
    const decision = decideSubmit(attemptRef.current, { path: LOGISTICS_PATH, binding, companyId, payload: validation.payload }, generateAttemptKey)
    if (decision.type === 'blocked') {
      if (decision.reason === 'unresolved-other-payload') setBlockedChange(true)
      return
    }
    setBlockedChange(false)
    void send(decision.snapshot)
  }

  function retry() {
    const live = liveSnapshot(attemptRef.current)
    if (live === null || attemptRef.current.phase === 'submitting') return
    setBlockedChange(false)
    void send(live)
  }

  function leaveAttempt() {
    const next = abandon(attemptRef.current)
    attemptRef.current = next
    setBlockedChange(false)
    setAttempt(next)
  }

  function togglePick(source: { readonly draftId: string; readonly lineId: string; readonly unitId: string }, on: boolean) {
    setPicked((current) => {
      const existing = current.find((line) => line.sourceOrderLineId === source.lineId)
      if (on) {
        return existing !== undefined
          ? current
          : [...current, { lineId: generateClientId(), sourceOrderId: source.draftId, sourceOrderLineId: source.lineId, unitId: source.unitId, quantityText: '' }]
      }
      return current.filter((line) => line.sourceOrderLineId !== source.lineId)
    })
  }

  const notice = createNotice(attempt, blockedChange)
  const errorEntries = useMemo(() => Object.entries(errors), [errors])

  if (attempt.phase === 'completed') {
    const instruction = attempt.result.detail.instruction
    return (
      <section className={styles.page} aria-labelledby="logistics-created-heading">
        <div role="status" className={styles.success} data-testid="logistics-created">
          <h1 id="logistics-created-heading" className={styles.title}>
            {T.attempt.completed}
          </h1>
          <p className={styles.identifier} data-testid="logistics-created-id">
            {instruction.id}
          </p>
          {attempt.result.isReplay ? <p data-testid="logistics-created-replay">{T.attempt.replayed}</p> : null}
          <div className={styles.actions}>
            {hasCapability(shell, LOGISTICS_CAPABILITY.instructionsDetail) ? (
              <button type="button" className={styles.primaryButton} onClick={() => void navigate(instructionDetailRoute(instruction.id))}>
                {T.attempt.openInstruction}
              </button>
            ) : null}
            {hasCapability(shell, LOGISTICS_CAPABILITY.instructionsList) ? <Link to={LOGISTICS_ROUTE.list}>{T.backToList}</Link> : null}
          </div>
        </div>
      </section>
    )
  }

  const frozen = attempt.phase === 'submitting'
  const fieldError = (id: string) =>
    errors[id] === undefined ? null : (
      <p id={`${id}-error`} className={styles.fieldError}>
        {errors[id]}
      </p>
    )

  const sourceState = drafts.data
  const sourceBlock =
    sourceState === undefined ? (
      <p role="status" className={styles.muted}>
        {T.sourcesLoading}
      </p>
    ) : sourceState.type === 'ready' ? (
      options.length === 0 ? (
        <p role="status" className={styles.notice} data-tone="unknown" data-testid="logistics-no-approved">
          {T.noApprovedDrafts}
        </p>
      ) : null
    ) : (
      <p role="alert" className={styles.notice} data-tone="error" data-testid="logistics-sources-failed">
        {sourceState.type === 'http' && sourceState.status === 403 ? T.sourcesForbidden : T.sourcesFailed}
      </p>
    )

  return (
    <section className={styles.page} aria-labelledby="logistics-create-heading">
      <header className={styles.header}>
        <h1 id="logistics-create-heading" className={styles.title}>
          {T.heading}
        </h1>
        {hasCapability(shell, LOGISTICS_CAPABILITY.instructionsList) ? (
          <Link className={styles.backLink} to={LOGISTICS_ROUTE.list}>
            ← {T.backToList}
          </Link>
        ) : null}
      </header>

      {notice === null ? null : (
        <div className={styles.notice} data-tone={notice.tone} role={notice.live === 'assertive' ? 'alert' : 'status'} aria-live={notice.live}>
          <p data-testid="logistics-create-notice">{notice.message}</p>
          {notice.actions.length > 0 ? (
            <div className={styles.actions}>
              <button type="button" className={styles.primaryButton} onClick={retry}>
                {T.attempt.retry}
              </button>
              <button type="button" className={styles.secondaryButton} onClick={leaveAttempt}>
                {T.attempt.abandon}
              </button>
            </div>
          ) : null}
        </div>
      )}

      {sourceBlock}

      <form onSubmit={onSubmit} noValidate>
        {errorEntries.length > 0 || serverMessages.length > 0 ? (
          <div ref={summaryRef} tabIndex={-1} role="alert" className={styles.errorSummary}>
            <p>{isOutcomeStillUnknown(attempt) ? T.errorSummaryUnresolved : T.errorSummary}</p>
            <ul>
              {errorEntries.map(([id, message]) => (
                <li key={id}>
                  <a
                    href={`#${id}`}
                    onClick={(event) => {
                      event.preventDefault()
                      document.getElementById(id)?.focus()
                    }}
                  >
                    {message}
                  </a>
                </li>
              ))}
              {serverMessages.map((message, index) => (
                <li key={`server-${index}`}>{message}</li>
              ))}
            </ul>
          </div>
        ) : null}

        <fieldset disabled={frozen} className={styles.fieldset}>
          <legend>{T.heading}</legend>
          <div className={styles.grid}>
            <label htmlFor="logistics-effective-on">{T.field.effectiveOn}</label>
            <input
              type="date"
              id="logistics-effective-on"
              value={effectiveOn}
              aria-invalid={errors['logistics-effective-on'] !== undefined ? true : undefined}
              onChange={(event) => setEffectiveOn(event.target.value)}
            />
            {fieldError('logistics-effective-on')}

            <label htmlFor="logistics-customer">{T.field.customer}</label>
            <select
              id="logistics-customer"
              value={customerKey}
              aria-invalid={errors['logistics-customer'] !== undefined ? true : undefined}
              onChange={(event) => {
                setCustomerKey(event.target.value)
                setPicked([])
              }}
            >
              <option value="">{T.choose}</option>
              {options.map((option) => (
                <option key={option.key} value={option.key}>
                  {option.label}
                </option>
              ))}
            </select>
            {fieldError('logistics-customer')}
          </div>
        </fieldset>

        <fieldset disabled={frozen} className={styles.fieldset} id="logistics-lines" tabIndex={-1}>
          <legend>{T.field.lines}</legend>
          <p className={styles.muted}>{T.picker.note}</p>
          {fieldError('logistics-lines')}
          {customer === null ? null : lines.data === undefined ? (
            <p role="status" className={styles.muted}>
              {T.sourcesLoading}
            </p>
          ) : lines.data.type !== 'ready' ? (
            <p role="alert" className={styles.fieldError} data-testid="logistics-lines-failed">
              {lines.data.type === 'http' && lines.data.status === 403 ? T.sourcesForbidden : T.sourcesFailed}
            </p>
          ) : lines.data.data.every((draft) => draft.lines.length === 0) ? (
            <p role="status" className={styles.muted} data-testid="logistics-no-lines">
              {T.noLinesForCustomer}
            </p>
          ) : (
            <div className={styles.tableWrap}>
              <table className={styles.table} data-testid="logistics-line-picker">
                <thead>
                  <tr>
                    <th scope="col">{T.picker.pick}</th>
                    <th scope="col">{T.picker.document}</th>
                    <th scope="col">{T.picker.lineNo}</th>
                    <th scope="col">{T.picker.product}</th>
                    <th scope="col" className={styles.numeric}>{T.picker.ordered}</th>
                    <th scope="col">{T.picker.unit}</th>
                    <th scope="col">{T.picker.instructed}</th>
                  </tr>
                </thead>
                <tbody>
                  {lines.data.data.flatMap((draft) =>
                    draft.lines.map((line) => {
                      const pick = picked.find((p) => p.sourceOrderLineId === line.lineId)
                      const quantityId = pick === undefined ? undefined : `logistics-line-${pick.lineId}-quantity`
                      const label = `${draft.draft.documentNo} / ${line.lineNo} / ${line.productName}`
                      return (
                        <tr key={line.lineId} data-testid={`logistics-pick-${draft.draft.documentNo}-${line.lineNo}`}>
                          <td>
                            <input
                              type="checkbox"
                              aria-label={label}
                              checked={pick !== undefined}
                              onChange={(event) => togglePick({ draftId: draft.draft.id, lineId: line.lineId, unitId: line.orderUnitId }, event.target.checked)}
                            />
                          </td>
                          <td>{draft.draft.documentNo}</td>
                          <td>{line.lineNo}</td>
                          <td>{line.productName}</td>
                          <td className={styles.numeric}>{line.quantity}</td>
                          <td className={styles.identifier}>{line.orderUnitId}</td>
                          <td>
                            {pick === undefined || quantityId === undefined ? null : (
                              <>
                                <input
                                  type="text"
                                  inputMode="decimal"
                                  autoComplete="off"
                                  id={quantityId}
                                  aria-label={`${T.field.instructed}: ${label}`}
                                  aria-invalid={errors[quantityId] !== undefined ? true : undefined}
                                  aria-describedby={errors[quantityId] !== undefined ? `${quantityId}-error` : undefined}
                                  value={pick.quantityText}
                                  onChange={(event) =>
                                    setPicked((current) =>
                                      current.map((p) => (p.lineId === pick.lineId ? { ...p, quantityText: event.target.value } : p)),
                                    )
                                  }
                                />
                                {fieldError(quantityId)}
                              </>
                            )}
                          </td>
                        </tr>
                      )
                    }),
                  )}
                </tbody>
              </table>
            </div>
          )}
        </fieldset>

        <section className={styles.summary} aria-labelledby="logistics-summary-heading">
          <h2 id="logistics-summary-heading">{T.summary.heading}</h2>
          <p>
            {T.summary.company}: <strong>{company === undefined ? companyId : `${company.code} — ${company.legalName}`}</strong>
          </p>
          <p className={styles.muted}>{T.summary.draft}</p>
          <p className={styles.muted}>{T.summary.noEffects}</p>
        </section>

        <div className={styles.actions}>
          <button type="submit" className={styles.primaryButton} disabled={frozen || options.length === 0} aria-busy={frozen} data-testid="logistics-create-button">
            {T.save}
          </button>
        </div>
      </form>
    </section>
  )
}
