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
 * The customer select and the line picker read the price-free Logistics approved-order-lines read
 * under the create grant (Sinan §4, #66 5738113623: the Sevkiyatçı sees orders, never prices); no
 * Sales grant is needed and no price can arrive in this browser. Nothing is guessed: no approved line
 * means no customer to pick; the picker shows the document, the line no, the product name, the unit
 * code, the ordered quantity text and the requested delivery date.
 * The client sends no product: the Sales owner answers it from the approved line on the server.
 */

import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useNavigate } from 'react-router'
import { createInstruction, fetchApprovedOrderLines } from './api'
import {
  LIMITS,
  LOGISTICS_PATH,
  isDateOnly,
  parseQuantityInput,
  type ApprovedOrderLine,
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
import { recallInstructionAttempt, rememberInstructionAttempt } from './pendingInstructionAttempts'
import { LOGISTICS_CAPABILITY, LOGISTICS_ROUTE, instructionDetailRoute } from './capabilities'
import { INSTRUCTION_CREATE_TEXT as T, problemCodeText } from './logisticsText'
import { formatBusinessDate, todayDateOnly } from './format'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
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

interface CreateFormSnapshot {
  readonly effectiveOn: string
  readonly customerKey: string
  readonly picked: readonly PickedLine[]
}

function createFormSnapshot(payload: CreateInstructionRequest): CreateFormSnapshot {
  return {
    effectiveOn: payload.effectiveOn,
    customerKey: `${payload.customerPartyId}|${payload.companyAccountId}`,
    picked: payload.lines.map((line) => ({
      lineId: line.lineId,
      sourceOrderId: line.sourceOrderId,
      sourceOrderLineId: line.sourceOrderLineId,
      unitId: line.unitId,
      quantityText: line.quantity,
    })),
  }
}

interface CustomerOption {
  readonly key: string
  readonly partyId: string
  readonly companyAccountId: string
  /** The Cari name as the read answers it. */
  readonly name: string
  /** The option text: the name; the document numbers in parentheses only when two customers share a name. */
  readonly label: string
}

/**
 * The distinct (customer party, account) pairs of the approved lines, shown by the customer's NAME from
 * Cari (Business 5745076917: a technical id or a document number is not presentation). Two customers
 * sharing a display name are told apart by their document numbers in parentheses; otherwise no
 * document number appears in the option.
 */
export function customerOptions(lines: readonly ApprovedOrderLine[]): readonly CustomerOption[] {
  const byKey = new Map<string, { partyId: string; companyAccountId: string; name: string; documents: string[] }>()
  for (const line of lines) {
    const key = `${line.customerPartyId}|${line.companyAccountId}`
    const entry = byKey.get(key) ?? { partyId: line.customerPartyId, companyAccountId: line.companyAccountId, name: line.customerName, documents: [] }
    if (!entry.documents.includes(line.documentNo)) entry.documents.push(line.documentNo)
    byKey.set(key, entry)
  }
  const entries = [...byKey.entries()]
  const nameCount = new Map<string, number>()
  for (const [, entry] of entries) nameCount.set(entry.name, (nameCount.get(entry.name) ?? 0) + 1)
  return entries.map(([key, entry]) => ({
    key,
    partyId: entry.partyId,
    companyAccountId: entry.companyAccountId,
    name: entry.name,
    label: (nameCount.get(entry.name) ?? 0) > 1 ? `${entry.name} (${entry.documents.join(', ')})` : entry.name,
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

  const [initial] = useState(() => recallInstructionAttempt<CreateInstructionRequest, CreateFormSnapshot>(binding))
  const [effectiveOn, setEffectiveOn] = useState(() => initial?.values.effectiveOn ?? todayDateOnly())
  const [customerKey, setCustomerKey] = useState(() => initial?.values.customerKey ?? '')
  const [picked, setPicked] = useState<readonly PickedLine[]>(() => initial?.values.picked ?? [])
  const [attempt, setAttempt] = useState<CreateAttempt>(() => initial?.attempt ?? { phase: 'idle' })
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

  // The approved order lines (the customer select's and the picker's one source), the price-free
  // Logistics read; never a default. Continuation follows the contract: `nextCursor = null` is the end.
  const first = useQuery({
    queryKey: [binding, 'logistics-approved-order-lines', 'first'],
    queryFn: async ({ signal }) => {
      const security = boundary.capture()
      const outcome = await fetchApprovedOrderLines(null, companyId, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, security)
      return outcome
    },
  })
  const [moreLines, setMoreLines] = useState<readonly ApprovedOrderLine[]>([])
  const [cursor, setCursor] = useState<string | null | undefined>(undefined)
  const [continuation, setContinuation] = useState<'idle' | 'loading' | RemoteViewState<unknown>>('idle')
  const loadingMoreRef = useRef(false)
  const firstPage = first.data?.type === 'ok-single' ? first.data.data : null
  const nextCursor = cursor === undefined ? (firstPage?.nextCursor ?? null) : cursor
  const allLines = useMemo(() => [...(firstPage?.items ?? []), ...moreLines], [firstPage, moreLines])
  const options = useMemo(() => customerOptions(allLines), [allLines])
  const customer = options.find((option) => option.key === customerKey) ?? null
  const customerLines = useMemo(
    () => (customer === null ? [] : allLines.filter((line) => line.customerPartyId === customer.partyId && line.companyAccountId === customer.companyAccountId)),
    [allLines, customer],
  )

  const loadMoreLines = useCallback(async () => {
    if (loadingMoreRef.current || nextCursor === null) return
    loadingMoreRef.current = true
    setContinuation('loading')
    const security = boundary.capture()
    const outcome = await fetchApprovedOrderLines(nextCursor, companyId, undefined)
    loadingMoreRef.current = false
    if (outcome.type === 'http') boundary.report(outcome.status, security)
    if (outcome.type === 'ok-single') {
      setMoreLines((rows) => {
        const seen = new Set([...(firstPage?.items ?? []), ...rows].map((row) => row.lineId))
        return [...rows, ...outcome.data.items.filter((row) => !seen.has(row.lineId))]
      })
      setCursor(outcome.data.nextCursor)
      setContinuation('idle')
      return
    }
    setContinuation(classifyOutcome(outcome) as RemoteViewState<unknown>)
  }, [boundary, companyId, firstPage, nextCursor])

  const send = useCallback(
    async (snapshot: AttemptSnapshot<CreateInstructionRequest>) => {
      const previous = attemptRef.current
      const inFlight: CreateAttempt = beginSend<CreateInstructionRequest, InstructionMutation>(snapshot)
      attemptRef.current = inFlight
      setAttempt(inFlight)
      const submitted = createFormSnapshot(snapshot.payload)
      rememberInstructionAttempt(snapshot.binding, inFlight, submitted)
      setServerMessages([])
      const security = boundary.capture()
      // Company and key travel FROM THE SNAPSHOT, never from the shell's current state.
      const outcome = await createInstruction(snapshot.payload, { idempotencyKey: snapshot.key, companyId: snapshot.companyId })
      const next = applyOutcome(inFlight, snapshot, snapshot.binding, outcome, previous)
      rememberInstructionAttempt(snapshot.binding, next, submitted)
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
    if (next.phase !== 'idle') rememberInstructionAttempt(next.snapshot.binding, next, createFormSnapshot(next.snapshot.payload))
  }

  function togglePick(source: ApprovedOrderLine, on: boolean) {
    setPicked((current) => {
      const existing = current.find((line) => line.sourceOrderLineId === source.lineId)
      if (on) {
        return existing !== undefined
          ? current
          : [...current, { lineId: generateClientId(), sourceOrderId: source.orderId, sourceOrderLineId: source.lineId, unitId: source.unitId, quantityText: '' }]
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

  const sourceState = first.data
  const sourceBlock =
    sourceState === undefined ? (
      <p role="status" className={styles.muted}>
        {T.sourcesLoading}
      </p>
    ) : sourceState.type === 'ok-single' ? (
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
          <legend data-testid="logistics-lines-heading">{customer === null ? T.field.lines : `${T.field.lines} — ${customer.name}`}</legend>
          <p className={styles.muted}>{T.picker.note}</p>
          {fieldError('logistics-lines')}
          {customer === null ? null : customerLines.length === 0 ? (
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
                    <th scope="col">{T.picker.unit}</th>
                    <th scope="col" className={styles.numeric}>{T.picker.ordered}</th>
                    <th scope="col">{T.picker.requestedDeliveryDate}</th>
                    <th scope="col">{T.picker.instructed}</th>
                  </tr>
                </thead>
                <tbody>
                  {customerLines.map((line) => {
                    const pick = picked.find((p) => p.sourceOrderLineId === line.lineId)
                    const quantityId = pick === undefined ? undefined : `logistics-line-${pick.lineId}-quantity`
                    const label = `${line.documentNo} / ${line.lineNo} / ${line.productName}`
                    return (
                      <tr key={line.lineId} data-testid={`logistics-pick-${line.documentNo}-${line.lineNo}`}>
                        <td>
                          <input type="checkbox" aria-label={label} checked={pick !== undefined} onChange={(event) => togglePick(line, event.target.checked)} />
                        </td>
                        <td>{line.documentNo}</td>
                        <td>{line.lineNo}</td>
                        <td>{line.productName}</td>
                        <td>{line.unitCode}</td>
                        <td className={styles.numeric}>{line.quantity}</td>
                        <td>{line.requestedDeliveryDate === null ? '—' : formatBusinessDate(line.requestedDeliveryDate)}</td>
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
                                  setPicked((current) => current.map((p) => (p.lineId === pick.lineId ? { ...p, quantityText: event.target.value } : p)))
                                }
                              />
                              {fieldError(quantityId)}
                            </>
                          )}
                        </td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>
          )}
          {firstPage === null ? null : typeof continuation === 'object' ? (
            <div className={styles.inlineError}>
              <p>{T.moreLinesFailed}</p>
              <button type="button" className={styles.secondaryButton} onClick={() => void loadMoreLines()}>
                {T.moreLines}
              </button>
            </div>
          ) : nextCursor === null ? null : (
            <button
              type="button"
              className={styles.secondaryButton}
              onClick={() => void loadMoreLines()}
              disabled={continuation === 'loading'}
              aria-busy={continuation === 'loading'}
              data-testid="logistics-more-lines"
            >
              {continuation === 'loading' ? T.sourcesLoading : T.moreLines}
            </button>
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
