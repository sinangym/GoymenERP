/**
 * S-11 (SAT-D28, NS-13, SAT-D346): the sales contracts of the active company — number, version, state, validity, the instalment
 * payment plan, the line / total discount rules and the clauses — with the create form and the discount decision actions.
 *
 * Whether a discount needs approval is the server's comparison with the limit configured in Settings at recording time; without a
 * configured limit every discount goes to approval and the screen says "sınır tanımlı değil" (SAT-D346). No limit, rate or amount
 * is computed here: amounts are the server's text at the money profile's scale and the plan total is the server's sum. The creator
 * never decides their own contract's discount; the server refuses it and the screen only shows the refusal. SAT-D361: without
 * the sales price read the server sends every amount null beside `amountDisclosure: Withheld`; the screen says "Gizli", never 0.
 * SAT-D362: the end date is optional; an empty end is sent as null and the card says "süresiz". A command whose
 * outcome is unknown (offline, network error, malformed answer, or a 502/503/504 gateway answer) keeps its key in memory and is
 * retried with the same key, never as a new request.
 *
 * STATUS: NOT ROUTED. The shell does not publish `SALES_CONTRACT_CAPABILITY` yet, so the container renders nothing until the
 * backend shell provider publishes it and the route is mounted.
 */
import { useId, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { decodeProblem } from './contract'
import { formatBusinessDate, formatInstant } from './format'
import { generateOrderAttemptKey, type OrderCommandOutcome } from './orderAttempt'
import {
  SALES_CONTRACT_CAPABILITY,
  SALES_CONTRACT_DISCOUNT_KIND,
  SALES_CONTRACT_LIMITS,
  SALES_CONTRACT_STATE,
  type CreateSalesContractBody,
  type SalesContract,
  type SalesContractApprovalBody,
  type SalesContractDiscountKind,
  type SalesContractRejectionBody,
} from './salesContract'
import {
  approveSalesContractDiscount,
  createSalesContract,
  fetchSalesContracts,
  rejectSalesContractDiscount,
  type SalesContractMutation,
} from './salesContractApi'
import { SALES_CONTRACT_TEXT as T } from './salesContractText'
import { isDateOnly, isReferenceId, parseDecimalInput, parseQuantityInput } from './wirePrimitives'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type ViewOutcome } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import pages from './SalesPages.module.css'

/** One command in flight or of unknown outcome: retried only with the same key and the same body. */
export type PendingSalesContractAction =
  | { readonly kind: 'create'; readonly key: string; readonly payload: CreateSalesContractBody }
  | { readonly kind: 'discount-approval'; readonly key: string; readonly contractId: string; readonly payload: SalesContractApprovalBody }
  | { readonly kind: 'discount-rejection'; readonly key: string; readonly contractId: string; readonly payload: SalesContractRejectionBody }

/** The container: gates the list on one bound company and the list capability, and each action on its own capability. */
export function SalesContracts() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const canRead = namespace !== null && companyId !== null && hasCapability(shell, SALES_CONTRACT_CAPABILITY.list)
  const canCreate = canRead && hasCapability(shell, SALES_CONTRACT_CAPABILITY.create)
  const canDecide = canRead && hasCapability(shell, SALES_CONTRACT_CAPABILITY.discountApprove)
  const boundary = useAuthBoundary()
  const query = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'sales-contracts'],
    enabled: canRead,
    gcTime: 0,
    staleTime: 0,
    retry: false,
    refetchOnWindowFocus: false,
    placeholderData: () => undefined,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchSalesContracts(companyId as string, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return signal.aborted ? { type: 'fence-stale' as const } : outcome
    },
  })

  async function send(action: PendingSalesContractAction): Promise<OrderCommandOutcome<SalesContractMutation>> {
    const snapshot = boundary.capture()
    const context = { idempotencyKey: action.key, companyId: companyId as string }
    const outcome = action.kind === 'create'
      ? await createSalesContract(action.payload, context)
      : action.kind === 'discount-approval'
        ? await approveSalesContractDiscount(action.contractId, action.payload, context)
        : await rejectSalesContractDiscount(action.contractId, action.payload, context)
    if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
    return outcome
  }

  if (!canRead) return null
  return (
    <SalesContractsPanel
      outcome={query.data}
      canCreate={canCreate}
      canDecide={canDecide}
      send={send}
      onRecorded={() => void query.refetch()}
    />
  )
}

interface InstallmentDraft { readonly dueDate: string; readonly amount: string }
interface RuleDraft { readonly kind: SalesContractDiscountKind; readonly label: string; readonly rate: string }

const EMPTY_INSTALLMENT: InstallmentDraft = { dueDate: '', amount: '' }
const EMPTY_RULE: RuleDraft = { kind: SALES_CONTRACT_DISCOUNT_KIND.line, label: '', rate: '' }

type Notice = { readonly text: string; readonly supportCode: string | null }

/**
 * Reads the create form into the wire body, or names the first problem. Only the shape is judged (ids, dates, ordering, positive
 * decimals as text, sizes); the scale, the rate bound and every rule are the server's (money profile, NS-13).
 */
export function readContractForm(form: {
  readonly party: string
  readonly account: string
  readonly currency: string
  readonly validFrom: string
  readonly validThrough: string
  readonly installments: readonly InstallmentDraft[]
  readonly rules: readonly RuleDraft[]
  readonly clauses: string
}): { readonly ok: true; readonly body: CreateSalesContractBody } | { readonly ok: false; readonly error: string } {
  const party = form.party.trim()
  const account = form.account.trim()
  const currency = form.currency.trim()
  if (!isReferenceId(party) || !isReferenceId(account) || !isReferenceId(currency)) return { ok: false, error: T.invalidReference }
  // SAT-D362: the start is required; an empty end is an open-ended contract (sent as null), a given end never precedes the start.
  const validThrough = form.validThrough.trim() === '' ? null : form.validThrough
  if (!isDateOnly(form.validFrom) || (validThrough !== null && (!isDateOnly(validThrough) || validThrough < form.validFrom))) {
    return { ok: false, error: T.invalidValidity }
  }
  if (form.installments.length < 1 || form.installments.length > SALES_CONTRACT_LIMITS.installments) {
    return { ok: false, error: T.invalidInstallments(SALES_CONTRACT_LIMITS.installments) }
  }
  const installments: { dueDate: string; amount: string }[] = []
  for (const [index, item] of form.installments.entries()) {
    const amount = parseQuantityInput(item.amount)
    if (!isDateOnly(item.dueDate) || !amount.ok) return { ok: false, error: T.invalidInstallment(index + 1) }
    if (index > 0 && item.dueDate < installments[index - 1].dueDate) {
      return { ok: false, error: T.invalidInstallments(SALES_CONTRACT_LIMITS.installments) }
    }
    installments.push({ dueDate: item.dueDate, amount: amount.value })
  }
  if (form.rules.length > SALES_CONTRACT_LIMITS.discountRules) return { ok: false, error: T.invalidRules(SALES_CONTRACT_LIMITS.discountRules) }
  const discountRules: { kind: SalesContractDiscountKind; label: string; ratePercent: string }[] = []
  for (const [index, rule] of form.rules.entries()) {
    const label = rule.label.trim()
    const rate = parseDecimalInput(rule.rate)
    if (label === '' || label.length > SALES_CONTRACT_LIMITS.label || !rate.ok || !/[1-9]/.test(rate.value)) {
      return { ok: false, error: T.invalidRule(index + 1, SALES_CONTRACT_LIMITS.label) }
    }
    discountRules.push({ kind: rule.kind, label, ratePercent: rate.value })
  }
  const clauses = form.clauses.trim()
  // The bound is the server's; longer clauses are refused here too, never cut.
  if (form.clauses.length > SALES_CONTRACT_LIMITS.clauses) return { ok: false, error: T.clausesTooLong(SALES_CONTRACT_LIMITS.clauses) }
  return {
    ok: true,
    body: {
      partyId: party, companyAccountId: account, currencyId: currency, validFrom: form.validFrom, validThrough,
      installments, discountRules, clauses: clauses === '' ? null : form.clauses,
    },
  }
}

/** The panel for one read outcome (undefined while loading). Exported so every outcome and action can be checked on its own. */
export function SalesContractsPanel({
  outcome, canCreate, canDecide, send, onRecorded,
}: {
  readonly outcome: ViewOutcome<readonly SalesContract[]> | undefined
  readonly canCreate: boolean
  readonly canDecide: boolean
  readonly send: (action: PendingSalesContractAction) => Promise<OrderCommandOutcome<SalesContractMutation>>
  readonly onRecorded: () => void
}) {
  const headingId = useId()
  const [party, setParty] = useState('')
  const [account, setAccount] = useState('')
  const [currency, setCurrency] = useState('')
  const [validFrom, setValidFrom] = useState('')
  const [validThrough, setValidThrough] = useState('')
  const [installments, setInstallments] = useState<readonly InstallmentDraft[]>([EMPTY_INSTALLMENT])
  const [rules, setRules] = useState<readonly RuleDraft[]>([])
  const [clauses, setClauses] = useState('')
  const [rejectionReasons, setRejectionReasons] = useState<Readonly<Record<string, string>>>({})
  const [pending, setPending] = useState<PendingSalesContractAction | null>(null)
  const [busy, setBusy] = useState(false)
  const [notice, setNotice] = useState<Notice | null>(null)
  const locked = busy || pending !== null

  const allowed = (kind: PendingSalesContractAction['kind']) => kind === 'create' ? canCreate : canDecide

  function resetForm() {
    setParty('')
    setAccount('')
    setCurrency('')
    setValidFrom('')
    setValidThrough('')
    setInstallments([EMPTY_INSTALLMENT])
    setRules([])
    setClauses('')
  }

  async function execute(action: PendingSalesContractAction) {
    if (!allowed(action.kind)) {
      setNotice({ text: T.noAuthority, supportCode: null })
      return
    }
    setBusy(true)
    setNotice({ text: T.submitting, supportCode: null })
    const result = await send(action)
    setBusy(false)
    if (result.type === 'succeeded') {
      setPending(null)
      const done = action.kind === 'create'
        ? result.data.contract.state === SALES_CONTRACT_STATE.active ? T.createdActive : T.createdPending
        : action.kind === 'discount-approval' ? T.approved : T.rejected
      setNotice({ text: result.data.isReplay ? done + T.replayed : done, supportCode: null })
      if (action.kind === 'create') resetForm()
      else setRejectionReasons((current) => ({ ...current, [action.contractId]: '' }))
      onRecorded()
      return
    }
    if (result.type === 'offline' || result.type === 'network-error' || result.type === 'aborted' || result.type === 'malformed') {
      // The server may have recorded it: keep the key so the retry replays instead of opening a second record.
      setPending(action)
      setNotice({ text: T.unknown, supportCode: null })
      return
    }
    if (GATEWAY_STATUSES.includes(result.status)) {
      // A gateway or unavailable answer does not say whether the command was recorded: keep the key for a same-key retry.
      setPending(action)
      setNotice({ text: T.retryPending, supportCode: result.problem?.correlationId ?? null })
      return
    }
    setPending(null)
    setNotice(failureNotice(result))
    if (result.status === 409) onRecorded()
  }

  function reserve(action: PendingSalesContractAction) {
    setPending(action)
    void execute(action)
  }

  function create() {
    const read = readContractForm({ party, account, currency, validFrom, validThrough, installments, rules, clauses })
    if (!read.ok) {
      setNotice({ text: read.error, supportCode: null })
      return
    }
    reserve({ kind: 'create', key: generateOrderAttemptKey(), payload: read.body })
  }

  function approve(item: SalesContract) {
    reserve({ kind: 'discount-approval', key: generateOrderAttemptKey(), contractId: item.id, payload: { expectedVersion: item.version } })
  }

  function reject(item: SalesContract) {
    const reason = (rejectionReasons[item.id] ?? '').trim()
    if (reason === '') {
      setNotice({ text: T.invalidRejection, supportCode: null })
      return
    }
    if (reason.length > SALES_CONTRACT_LIMITS.reason) {
      setNotice({ text: T.reasonTooLong(SALES_CONTRACT_LIMITS.reason), supportCode: null })
      return
    }
    reserve({
      kind: 'discount-rejection', key: generateOrderAttemptKey(), contractId: item.id,
      payload: { expectedVersion: item.version, reason },
    })
  }

  const updateInstallment = (index: number, change: Partial<InstallmentDraft>) =>
    setInstallments((current) => current.map((item, at) => at === index ? { ...item, ...change } : item))
  const updateRule = (index: number, change: Partial<RuleDraft>) =>
    setRules((current) => current.map((item, at) => at === index ? { ...item, ...change } : item))

  let body
  if (outcome?.type === 'ok-single') {
    const items = outcome.data
    body = (
      <>
        {notice === null ? null : (
          <p role="status" className={pages.notice} data-testid="sales-contract-notice">
            {notice.text}
            {notice.supportCode === null ? null : <> · {T.supportCode(notice.supportCode)}</>}
          </p>
        )}
        {pending !== null && !busy ? (
          <Button type="button" variant="secondary" disabled={!allowed(pending.kind)}
            onClick={() => void execute(pending)} data-testid="sales-contract-retry">{T.retry}</Button>
        ) : null}
        {canCreate ? (
          <form className={pages.line} data-testid="sales-contract-create" onSubmit={(event) => { event.preventDefault(); create() }}>
            <h3 className={pages.lineTitle}>{T.createHeading}</h3>
            <label htmlFor="sales-contract-party">{T.party}</label>
            <input id="sales-contract-party" value={party} disabled={locked} autoComplete="off" spellCheck={false}
              onChange={(event) => setParty(event.target.value)} />
            <label htmlFor="sales-contract-account">{T.account}</label>
            <input id="sales-contract-account" value={account} disabled={locked} autoComplete="off" spellCheck={false}
              onChange={(event) => setAccount(event.target.value)} />
            <label htmlFor="sales-contract-currency">{T.currency}</label>
            <input id="sales-contract-currency" value={currency} disabled={locked} autoComplete="off" spellCheck={false}
              onChange={(event) => setCurrency(event.target.value)} />
            <label htmlFor="sales-contract-valid-from">{T.validFrom}</label>
            <input id="sales-contract-valid-from" type="date" value={validFrom} disabled={locked}
              onChange={(event) => setValidFrom(event.target.value)} />
            <label htmlFor="sales-contract-valid-through">{T.validThrough}</label>
            <input id="sales-contract-valid-through" type="date" value={validThrough} disabled={locked}
              aria-describedby="sales-contract-valid-through-hint" onChange={(event) => setValidThrough(event.target.value)} />
            <p id="sales-contract-valid-through-hint" className={pages.muted}>{T.validThroughHint}</p>
            <fieldset data-testid="sales-contract-installments">
              <legend>{T.installmentsCaption}</legend>
              {installments.map((item, index) => (
                <div key={index} className={pages.actions}>
                  <label htmlFor={`sales-contract-installment-${index}-due`}>{T.installmentDueDate(index + 1)}</label>
                  <input id={`sales-contract-installment-${index}-due`} type="date" value={item.dueDate} disabled={locked}
                    onChange={(event) => updateInstallment(index, { dueDate: event.target.value })} />
                  <label htmlFor={`sales-contract-installment-${index}-amount`}>{T.installmentAmount(index + 1)}</label>
                  <input id={`sales-contract-installment-${index}-amount`} inputMode="decimal" value={item.amount} disabled={locked}
                    onChange={(event) => updateInstallment(index, { amount: event.target.value })} />
                  {installments.length > 1 ? (
                    <Button type="button" variant="secondary" disabled={locked}
                      onClick={() => setInstallments((current) => current.filter((_, at) => at !== index))}>
                      {T.removeInstallment(index + 1)}
                    </Button>
                  ) : null}
                </div>
              ))}
              <Button type="button" variant="secondary"
                disabled={locked || installments.length >= SALES_CONTRACT_LIMITS.installments}
                onClick={() => setInstallments((current) => [...current, EMPTY_INSTALLMENT])}>{T.addInstallment}</Button>
            </fieldset>
            <fieldset data-testid="sales-contract-rules">
              <legend>{T.discountCaption}</legend>
              {rules.map((rule, index) => (
                <div key={index} className={pages.actions}>
                  <label htmlFor={`sales-contract-rule-${index}-kind`}>{T.ruleKind(index + 1)}</label>
                  <select id={`sales-contract-rule-${index}-kind`} value={rule.kind} disabled={locked}
                    onChange={(event) => updateRule(index, { kind: event.target.value as SalesContractDiscountKind })}>
                    <option value={SALES_CONTRACT_DISCOUNT_KIND.line}>{T.kind.Line}</option>
                    <option value={SALES_CONTRACT_DISCOUNT_KIND.total}>{T.kind.Total}</option>
                  </select>
                  <label htmlFor={`sales-contract-rule-${index}-label`}>{T.ruleLabel(index + 1)}</label>
                  <input id={`sales-contract-rule-${index}-label`} value={rule.label} disabled={locked}
                    onChange={(event) => updateRule(index, { label: event.target.value })} />
                  <label htmlFor={`sales-contract-rule-${index}-rate`}>{T.ruleRate(index + 1)}</label>
                  <input id={`sales-contract-rule-${index}-rate`} inputMode="decimal" value={rule.rate} disabled={locked}
                    onChange={(event) => updateRule(index, { rate: event.target.value })} />
                  <Button type="button" variant="secondary" disabled={locked}
                    onClick={() => setRules((current) => current.filter((_, at) => at !== index))}>{T.removeRule(index + 1)}</Button>
                </div>
              ))}
              <Button type="button" variant="secondary" disabled={locked || rules.length >= SALES_CONTRACT_LIMITS.discountRules}
                onClick={() => setRules((current) => [...current, EMPTY_RULE])}>{T.addRule}</Button>
            </fieldset>
            <label htmlFor="sales-contract-clauses">{T.clausesInput}</label>
            <textarea id="sales-contract-clauses" value={clauses} disabled={locked} onChange={(event) => setClauses(event.target.value)} />
            <Button type="submit" variant="primary" disabled={locked}>{T.create}</Button>
          </form>
        ) : null}
        {items.length === 0 ? <p data-testid="sales-contract-empty">{T.empty}</p> : items.map((item) => (
          <ContractCard
            key={item.id}
            item={item}
            canDecide={canDecide}
            locked={locked}
            rejectionReason={rejectionReasons[item.id] ?? ''}
            onRejectionReason={(value) => setRejectionReasons((current) => ({ ...current, [item.id]: value }))}
            onApprove={() => approve(item)}
            onReject={() => reject(item)}
          />
        ))}
      </>
    )
  } else if (outcome?.type === 'http' && outcome.status === 403) {
    body = <p className={pages.muted} role="status" data-testid="sales-contract-forbidden">{T.forbidden}</p>
  } else {
    const problem = outcome?.type === 'http' ? decodeProblem(outcome.body, outcome.status) : null
    body = (
      <>
        <StateBlock
          state={outcome === undefined ? { kind: 'initial-loading' } : classifyOutcome(outcome)}
          subject={T.subject}
          onRetry={onRecorded}
        />
        {outcome === undefined ? null : (
          <p className={pages.muted} data-testid="sales-contract-unreadable">
            {outcome.type === 'http' && outcome.status === 503 ? T.serviceUnavailable : T.unreadable}
          </p>
        )}
        {problem?.correlationId == null ? null : (
          <p className={pages.muted} data-testid="sales-contract-support-code">{T.supportCode(problem.correlationId)}</p>
        )}
      </>
    )
  }
  return (
    <section className={pages.section} aria-labelledby={headingId} aria-busy={outcome === undefined} data-testid="sales-contracts">
      <h2 id={headingId}>{T.heading}</h2>
      <p className={pages.muted}>{T.note}</p>
      {body}
    </section>
  )
}

/** SAT-D361: an amount the server withheld; said to be hidden, never rendered as 0 or left blank. */
function WithheldAmount({ id }: { readonly id: string }) {
  return <span data-testid={id} aria-label={T.amountWithheldLabel} title={T.amountWithheldLabel}>{T.amountWithheld}</span>
}

function ContractCard({
  item, canDecide, locked, rejectionReason, onRejectionReason, onApprove, onReject,
}: {
  readonly item: SalesContract
  readonly canDecide: boolean
  readonly locked: boolean
  readonly rejectionReason: string
  readonly onRejectionReason: (value: string) => void
  readonly onApprove: () => void
  readonly onReject: () => void
}) {
  const id = `sales-contract-${item.contractNo}`
  return (
    <article className={pages.line} data-testid={id}>
      <h3 className={pages.lineTitle}>
        {T.contractHeading(item.contractNo)} · {T.versionFact(item.version)} · <span className={pages.badge} data-testid={`${id}-state`}>
          {T.state[item.state]}</span>
      </h3>
      <p data-testid={`${id}-validity`}>
        {T.validity(formatBusinessDate(item.validFrom), item.validThrough === null ? null : formatBusinessDate(item.validThrough))} ·{' '}
        {item.isInEffectToday ? T.inEffectToday : T.notInEffectToday}
      </p>
      <p className={pages.muted}>
        {T.partyFact(item.partyId)} · {T.accountFact(item.companyAccountId)} · {T.currencyFact(item.currencyId)}
      </p>
      <div className={pages.tableWrap}>
        <table className={pages.table} data-testid={`${id}-installments`}>
          <caption className={pages.caption}>{T.installmentsCaption}</caption>
          <thead>
            <tr>
              <th scope="col">{T.installmentColumn.no}</th>
              <th scope="col">{T.installmentColumn.dueDate}</th>
              <th scope="col" className={pages.numeric}>{T.installmentColumn.amount}</th>
            </tr>
          </thead>
          <tbody>
            {item.installments.map((installment) => (
              <tr key={installment.installmentNo}>
                <td>{installment.installmentNo}</td>
                <td>{formatBusinessDate(installment.dueDate)}</td>
                <td className={pages.numeric}>{installment.amount ?? <WithheldAmount id={`${id}-installment-${installment.installmentNo}-amount`} />}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <p data-testid={`${id}-total`}>
        {item.plannedTotalAmount === null ? T.plannedTotalWithheld : T.plannedTotal(item.plannedTotalAmount)}
      </p>
      {item.amountDisclosure === 'Withheld'
        ? <p className={pages.muted} role="note" data-testid={`${id}-amounts-withheld`}>{T.amountsWithheld}</p>
        : null}
      {item.discountRules.length === 0 ? <p data-testid={`${id}-no-discount`}>{T.noDiscount}</p> : (
        <div className={pages.tableWrap}>
          <table className={pages.table} data-testid={`${id}-rules`}>
            <caption className={pages.caption}>{T.discountCaption}</caption>
            <thead>
              <tr>
                <th scope="col">{T.discountColumn.no}</th>
                <th scope="col">{T.discountColumn.kind}</th>
                <th scope="col">{T.discountColumn.label}</th>
                <th scope="col" className={pages.numeric}>{T.discountColumn.rate}</th>
              </tr>
            </thead>
            <tbody>
              {item.discountRules.map((rule) => (
                <tr key={rule.ruleNo}>
                  <td>{rule.ruleNo}</td>
                  <td>{T.kind[rule.kind]}</td>
                  <td>{rule.label}</td>
                  <td className={pages.numeric}>{rule.ratePercent ?? T.amountWithheld}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {item.discountRules.length === 0 ? null : (
        <p data-testid={`${id}-limit`}>
          {!item.discountLimitDefined ? T.limitNotDefined
            : item.discountLimitPercent === null ? T.limitWithheld : T.limitDefined(item.discountLimitPercent)} ·{' '}
          {item.discountApprovalRequired ? T.approvalRequired : T.approvalNotRequired}
        </p>
      )}
      <h4>{T.clauses}</h4>
      <p className={pages.muted} data-testid={`${id}-clauses`} style={{ whiteSpace: 'pre-wrap' }}>{item.clauses ?? T.noClauses}</p>
      <p className={pages.muted}>{T.createdBy(item.createdBy, formatInstant(item.createdAtUtc))}</p>
      {item.decidedBy === null || item.decidedAtUtc === null ? null : (
        <p className={pages.muted} data-testid={`${id}-decided`}>
          {T.decidedBy(item.decidedBy, formatInstant(item.decidedAtUtc))}
          {item.rejectionReason === null ? null : ` · ${T.rejectionReasonFact(item.rejectionReason)}`}
        </p>
      )}
      {item.state === SALES_CONTRACT_STATE.pending && canDecide ? (
        <div className={pages.actions} data-testid={`${id}-decision`}>
          <label htmlFor={`${id}-reject-reason`}>{T.rejectionReason}</label>
          <input id={`${id}-reject-reason`} value={rejectionReason} disabled={locked}
            onChange={(event) => onRejectionReason(event.target.value)} />
          <Button type="button" variant="primary" disabled={locked} onClick={onApprove}>{T.approve}</Button>
          <Button type="button" variant="secondary" disabled={locked} onClick={onReject}>{T.reject}</Button>
        </div>
      ) : null}
    </article>
  )
}

/** Answers that do not say whether the command was recorded (a gateway or an unavailable server): the key is kept. */
const GATEWAY_STATUSES: readonly number[] = [502, 503, 504]

/** A refused command in words: the problem code the server named when known, else the status. */
function failureNotice(outcome: Extract<OrderCommandOutcome<SalesContractMutation>, { type: 'http' }>): Notice {
  const supportCode = outcome.problem?.correlationId ?? null
  const code = outcome.problem?.code ?? null
  if (outcome.status === 401) return { text: T.unauthenticated, supportCode }
  if (outcome.status === 403) return { text: T.noAuthority, supportCode }
  if (outcome.status === 404) return { text: T.notFound, supportCode }
  if (code !== null && T.problem[code] !== undefined) return { text: T.problem[code], supportCode }
  if (outcome.status === 409) return { text: T.conflict, supportCode }
  if (outcome.status >= 500) return { text: T.serviceUnavailable, supportCode }
  return { text: code === null ? T.failed : T.failedWithCode(code), supportCode }
}
