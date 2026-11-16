/**
 * FIN-S9 — Finans → Açılış bakiyeleri (FNS-D23).
 *
 * Cash, bank and party (cari) opening balances, entered only while the Settings opening period is open. This screen shows
 * the server's reads and sends the server's commands; it works out no figure.
 *
 * - The period is read first. While it is not Open (Closed or unreadable) only the notice is shown: no list, no form.
 * - A specialist proposes (`canPropose`); a manager approves or rejects (`canApprove`). The decision controls are not
 *   offered on the caller's own proposal (`isOwnProposal`); the server refuses a proposer's decision on its own and a
 *   database check backs it.
 * - A decision names the version the person looked at; a change in between is a server-side conflict.
 * - FNS-D85: an approved opening is corrected by a reversal — proposed by a proposer (`canPropose`), approved or rejected
 *   by an approver (`canApprove`) other than the reversal's proposer; its approval frees the target for a new proposal.
 * - The capabilities are the route owner's answer; the server checks every command on its own.
 *
 * Cash accounts, bank accounts and company accounts are named by identity as typed. A company, actor or session change
 * remounts the screen, so no answer of the previous context is drawn.
 */

import { useCallback, useState, type FormEvent, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState, type ViewOutcome } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import { isBoundUuid } from './contract'
import { isDateOnly } from './dateOnly'
import { formatDecimalTextTr, parseAmountInput } from './decimalText'
import type {
  OpeningBalanceCommandResult,
  OpeningBalanceCurrencyCandidates,
  OpeningBalanceDirection,
  OpeningBalanceTargetKind,
  OpeningBalanceView,
} from './OpeningBalanceContract'
import {
  PROPOSAL_TARGET,
  useOpeningBalanceAttempt,
  type OpeningBalanceAttempt,
  type OpeningBalanceAttemptBinding,
} from './OpeningBalanceCommand'
import {
  fetchOpeningBalanceCurrencyCandidates,
  fetchOpeningBalances,
  fetchOpeningPeriod,
  type OpeningBalanceOperation,
} from './OpeningBalanceHttp'
import {
  OPENING_BALANCE_DIRECTION,
  OPENING_BALANCE_REVERSAL_STATUS,
  OPENING_BALANCE_STATUS,
  OPENING_BALANCE_SUCCESS,
  OPENING_BALANCE_TARGET,
  OPENING_BALANCE_TEXT as T,
  OPENING_PERIOD_NOTICE,
  openingBalanceIssueText,
  shortOpeningReference,
} from './OpeningBalanceText'
import styles from './Finance.module.css'

const HEADING_ID = 'finance-opening-balances-heading'

export interface OpeningBalancePageProps {
  /** The caller holds `finance.opening-balances.propose` (route owner's capability answer). */
  readonly canPropose: boolean
  /** The caller holds `finance.opening-balances.approve` (route owner's capability answer). */
  readonly canApprove: boolean
}

const TARGET_KINDS: readonly OpeningBalanceTargetKind[] = ['CashAccount', 'BankAccount', 'PartyAccount']
const DIRECTIONS: readonly OpeningBalanceDirection[] = ['Debit', 'Credit']

function useBinding() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const actorId = shell.activeContext?.userId ?? null
  const attemptBinding: OpeningBalanceAttemptBinding | null = companyId === null || actorId === null
    ? null
    : { sessionBinding: shell.sessionBinding, actorId, companyId }
  return {
    shell,
    namespace,
    companyId,
    attemptBinding,
    mountKey: `${namespace ?? 'no-context'}::${shell.routeEntryGeneration}::${shell.sessionBinding ?? 'no-session'}`,
  }
}

export function OpeningBalancePage(props: OpeningBalancePageProps) {
  const { mountKey } = useBinding()
  return <OpeningBalanceScreen key={mountKey} {...props} />
}

/** Presentation only. */
function Instant({ value }: { readonly value: string }) {
  const parsed = new Date(value)
  return <time dateTime={value}>
    {Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString('tr-TR', { dateStyle: 'medium', timeStyle: 'short' })}
  </time>
}

type Names = ReadonlyMap<string, string>

function useQueryScope(namespace: string | null, generation: number) {
  return [namespace ?? 'no-context', generation, 'finance-opening-balances'] as const
}

type QueryScope = ReturnType<typeof useQueryScope>

/** A read that reports 401/403 to the shell boundary, like every Finance screen. */
function useReporter() {
  const boundary = useAuthBoundary()
  return useCallback(async <V,>(read: () => Promise<ViewOutcome<V>>) => {
    const snapshot = boundary.capture()
    const outcome = await read()
    if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) boundary.report(outcome.status, snapshot)
    return outcome
  }, [boundary])
}

/** After a definite success every read of this screen (the period included) is answered again. */
function useSettled(scope: QueryScope) {
  const client = useQueryClient()
  return useCallback((_operation: OpeningBalanceOperation, result: OpeningBalanceCommandResult) => {
    if (result.outcome === 'Succeeded') void client.invalidateQueries({ queryKey: scope })
  }, [client, scope])
}

function useReload(scope: QueryScope) {
  const client = useQueryClient()
  return useCallback(() => void client.invalidateQueries({ queryKey: scope }), [client, scope])
}

function OpeningBalanceScreen({ canPropose, canApprove }: OpeningBalancePageProps) {
  const { shell, namespace, companyId, attemptBinding } = useBinding()
  const scope = useQueryScope(namespace, shell.routeEntryGeneration)
  const reported = useReporter()
  const enabled = namespace !== null && companyId !== null

  const period = useQuery({
    queryKey: [...scope, 'period'],
    enabled,
    gcTime: 0,
    queryFn: ({ signal }) => reported(() => fetchOpeningPeriod(companyId ?? '', signal)),
  })
  const open = period.data?.type === 'ok-single' && period.data.data.state === 'Open'
  const list = useQuery({
    queryKey: [...scope, 'list'],
    enabled: enabled && open,
    gcTime: 0,
    queryFn: ({ signal }) => reported(() => fetchOpeningBalances(companyId ?? '', signal)),
  })
  const currencies = useQuery({
    queryKey: [...scope, 'currency-candidates'],
    enabled: enabled && open && canPropose,
    gcTime: 0,
    queryFn: ({ signal }) => reported(() => fetchOpeningBalanceCurrencyCandidates(companyId ?? '', signal)),
  })
  const currencyItems = currencies.data?.type === 'ok-single' ? currencies.data.data.items : []
  const currencyNames: Names = new Map(currencyItems.map((item) => [item.currencyId, item.code]))

  const frame = (body: ReactNode) => (
    <section className={styles.list} aria-labelledby={HEADING_ID} data-testid="finance-opening-balances">
      <h1 id={HEADING_ID}>{T.title}</h1>
      <p className={styles.notice}>{T.notice}</p>
      {body}
    </section>
  )

  if (companyId === null || attemptBinding === null) {
    return frame(<StateBlock state={{ kind: 'no-authorized-company' }} subject={T.listSubject} />)
  }
  if (period.data?.type !== 'ok-single') {
    const state = period.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(period.data)
    return frame(<StateBlock state={state as RemoteViewState<unknown>} subject={T.periodSubject}
      onRetry={() => void period.refetch()} />)
  }
  const periodState = period.data.data.state
  if (periodState !== 'Open') {
    return frame(<p className={styles.notice} role="status" data-period={periodState}>
      {OPENING_PERIOD_NOTICE[periodState]}
    </p>)
  }

  return frame(<>
    <OpeningList outcome={list.data} currencyNames={currencyNames} binding={attemptBinding} scope={scope}
      canPropose={canPropose} canApprove={canApprove} onRetry={() => void list.refetch()} />
    {canPropose
      ? <ProposeForm binding={attemptBinding} scope={scope} currencies={currencies.data}
          onRetryCurrencies={() => void currencies.refetch()} />
      : null}
  </>)
}

type ReasonOperation = Extract<OpeningBalanceOperation, 'reject' | 'reversal-propose' | 'reversal-reject'>

interface OpenReasonForm {
  readonly id: string
  readonly operation: ReasonOperation
}

/** What the row offers: a decision on a proposal, a reversal of an approved opening or a decision on that reversal. */
function rowActions(item: OpeningBalanceView, canPropose: boolean, canApprove: boolean) {
  const reversal = item.reversal
  const pendingReversal = item.status === 'Approved' && reversal?.status === 'Proposed'
  return {
    decide: canApprove && item.status === 'Proposed' && !item.isOwnProposal,
    ownProposal: item.status === 'Proposed' && item.isOwnProposal,
    proposeReversal: canPropose && item.status === 'Approved' && !pendingReversal,
    decideReversal: canApprove && pendingReversal && reversal?.isOwnProposal === false,
    ownReversal: pendingReversal && reversal?.isOwnProposal === true,
  }
}

function OpeningList({ outcome, currencyNames, binding, scope, canPropose, canApprove, onRetry }: {
  readonly outcome: ViewOutcome<{ readonly items: readonly OpeningBalanceView[] }> | undefined
  readonly currencyNames: Names
  readonly binding: OpeningBalanceAttemptBinding
  readonly scope: QueryScope
  readonly canPropose: boolean
  readonly canApprove: boolean
  readonly onRetry: () => void
}) {
  const [reasonForm, setReasonForm] = useState<OpenReasonForm | null>(null)
  const page = outcome?.type === 'ok-single' ? outcome.data : null
  if (page === null) {
    const state = outcome === undefined ? { kind: 'initial-loading' } : classifyOutcome(outcome)
    return <StateBlock state={state as RemoteViewState<unknown>} subject={T.listSubject} onRetry={onRetry} />
  }
  if (page.items.length === 0) return <p className={styles.hint}>{T.listEmpty}</p>
  const target = reasonForm === null ? null : page.items.find((item) => item.id === reasonForm.id) ?? null
  const acting = canPropose || canApprove
  const toggle = (id: string, operation: ReasonOperation) =>
    setReasonForm(reasonForm?.id === id && reasonForm.operation === operation ? null : { id, operation })
  const expanded = (id: string, operation: ReasonOperation) =>
    reasonForm?.id === id && reasonForm.operation === operation
  const formAllowed = (item: OpeningBalanceView, operation: ReasonOperation) => {
    const actions = rowActions(item, canPropose, canApprove)
    return operation === 'reject' ? actions.decide
      : operation === 'reversal-propose' ? actions.proposeReversal : actions.decideReversal
  }
  return <>
    <table>
      <caption>{T.listCaption}</caption>
      <thead><tr>
        <th scope="col">{T.column.reference}</th>
        <th scope="col">{T.column.target}</th>
        <th scope="col">{T.column.targetId}</th>
        <th scope="col">{T.column.party}</th>
        <th scope="col">{T.column.direction}</th>
        <th scope="col">{T.column.amount}</th>
        <th scope="col">{T.column.asOfDate}</th>
        <th scope="col">{T.column.status}</th>
        <th scope="col">{T.column.proposedAt}</th>
        <th scope="col">{T.column.decidedAt}</th>
        <th scope="col">{T.column.rejectionReason}</th>
        <th scope="col">{T.column.reversal}</th>
        {acting ? <th scope="col">{T.column.action}</th> : null}
      </tr></thead>
      <tbody>
        {page.items.map((item) => {
          const actions = rowActions(item, canPropose, canApprove)
          const reversal = item.reversal
          return <tr key={item.id} data-opening={item.id} data-status={item.status}
            data-reversal={reversal?.status ?? 'none'}>
            <td>{shortOpeningReference(item.id)}</td>
            <td>{OPENING_BALANCE_TARGET[item.targetKind]}</td>
            <td>{shortOpeningReference(item.targetId)}</td>
            <td>{item.partyId === null ? T.none : shortOpeningReference(item.partyId)}</td>
            <td>{item.direction === null ? T.none : OPENING_BALANCE_DIRECTION[item.direction]}</td>
            <td>{`${formatDecimalTextTr(item.amount) ?? item.amount} ${currencyNames.get(item.currencyId) ??
              shortOpeningReference(item.currencyId)}`}</td>
            <td>{item.asOfDate}</td>
            <td>{OPENING_BALANCE_STATUS[item.status]}</td>
            <td><Instant value={item.proposedAtUtc} /></td>
            <td>{item.decidedAtUtc === null ? T.none : <Instant value={item.decidedAtUtc} />}</td>
            <td>{item.rejectionReason ?? T.none}</td>
            <td>
              {reversal === null
                ? T.none
                : <>
                    <span>{OPENING_BALANCE_REVERSAL_STATUS[reversal.status]}</span>
                    <span className={styles.hint}>{` — ${reversal.reason}`}</span>
                    {reversal.rejectionReason === null
                      ? null
                      : <span className={styles.hint}>{` (${T.column.rejectionReason}: ${reversal.rejectionReason})`}</span>}
                  </>}
            </td>
            {acting
              ? <td>
                  {actions.decide
                    ? <>
                        <ApproveControl binding={binding} scope={scope} item={item} operation="approve" />
                        <Button type="button" variant="ghost" size="sm" aria-expanded={expanded(item.id, 'reject')}
                          onClick={() => toggle(item.id, 'reject')}>{T.rejectOpen}</Button>
                      </>
                    : null}
                  {actions.ownProposal && canApprove
                    ? <span className={styles.hint} data-own-proposal="true">{T.ownProposal}</span>
                    : null}
                  {actions.proposeReversal
                    ? <Button type="button" variant="secondary" size="sm"
                        aria-expanded={expanded(item.id, 'reversal-propose')}
                        onClick={() => toggle(item.id, 'reversal-propose')}>{T.reversalOpen}</Button>
                    : null}
                  {actions.decideReversal
                    ? <>
                        <ApproveControl binding={binding} scope={scope} item={item} operation="reversal-approve" />
                        <Button type="button" variant="ghost" size="sm"
                          aria-expanded={expanded(item.id, 'reversal-reject')}
                          onClick={() => toggle(item.id, 'reversal-reject')}>{T.reversalRejectOpen}</Button>
                      </>
                    : null}
                  {actions.ownReversal && canApprove
                    ? <span className={styles.hint} data-own-reversal="true">{T.reversalOwnProposal}</span>
                    : null}
                  {!actions.decide && !(actions.ownProposal && canApprove) && !actions.proposeReversal &&
                    !actions.decideReversal && !(actions.ownReversal && canApprove)
                    ? T.none
                    : null}
                </td>
              : null}
          </tr>
        })}
      </tbody>
    </table>
    {reasonForm !== null && target !== null && formAllowed(target, reasonForm.operation)
      ? <ReasonForm key={`${reasonForm.operation}:${target.id}:${target.version}:${target.reversal?.version ?? '-'}`}
          binding={binding} scope={scope} item={target} operation={reasonForm.operation}
          onClose={() => setReasonForm(null)} />
      : null}
  </>
}

function AttemptPanel({ attempt, operation, onReload }: {
  readonly attempt: OpeningBalanceAttempt
  readonly operation: OpeningBalanceOperation
  readonly onReload: () => void
}) {
  const state = attempt.state
  switch (state.phase) {
    case 'idle':
      return null
    case 'submitting':
      return <p className={styles.attempt} role="status">{T.submitting}</p>
    case 'unknown':
      return <div className={styles.attempt} role="alert" data-attempt="unknown">
        <p className={styles.attemptTitle}>{T.unknownTitle}</p>
        <p>{T.unknownBody}</p>
        <div className={styles.actions}>
          <Button type="button" variant="primary" onClick={() => void attempt.resend()}>{T.resend}</Button>
          <Button type="button" variant="ghost" onClick={attempt.abandon}>{T.abandon}</Button>
        </div>
      </div>
    case 'refused':
      return <div className={styles.attempt} role="alert" data-attempt="refused">
        <p className={styles.error}>{T.refusedStatus[state.status] ?? T.refusedOther}</p>
        <Button type="button" variant="ghost" onClick={attempt.reset}>{T.dismiss}</Button>
      </div>
    case 'answered': {
      const result = state.result
      const replay = result.isReplay ? <p className={styles.hint}>{T.replayNote}</p> : null
      if (result.outcome === 'Succeeded') {
        return <div className={styles.attempt} role="status" data-attempt="succeeded">
          <p>{OPENING_BALANCE_SUCCESS[operation]}</p>
          {replay}
          <Button type="button" variant="ghost" onClick={attempt.reset}>{T.dismiss}</Button>
        </div>
      }
      return <div className={styles.attempt} role="alert" data-attempt={result.outcome}>
        <p className={styles.error}>{T.outcome[result.outcome]}</p>
        {result.issues.length > 0
          ? <ul>{result.issues.map((issue) => <li key={`${issue.field}:${issue.code}`}>{openingBalanceIssueText(issue.code)}</li>)}</ul>
          : null}
        {replay}
        <div className={styles.actions}>
          {result.outcome === 'VersionConflict'
            ? <Button type="button" variant="secondary" onClick={onReload}>{T.reload}</Button>
            : null}
          <Button type="button" variant="ghost" onClick={attempt.reset}>{T.dismiss}</Button>
        </div>
      </div>
    }
  }
}

/** The approval of a proposal names the opening's version; the approval of a reversal names the reversal's. */
function ApproveControl({ binding, scope, item, operation }: {
  readonly binding: OpeningBalanceAttemptBinding
  readonly scope: QueryScope
  readonly item: OpeningBalanceView
  readonly operation: 'approve' | 'reversal-approve'
}) {
  const attempt = useOpeningBalanceAttempt(binding, operation, item.id, useSettled(scope))
  const reload = useReload(scope)
  const [blocked, setBlocked] = useState(false)
  const busy = attempt.state.phase === 'submitting'
  const expectedVersion = operation === 'approve' ? item.version : item.reversal?.version ?? null
  const approve = async () => {
    if (expectedVersion === null) return
    setBlocked(false)
    const result = await attempt.submit({ operation, openingBalanceId: item.id, body: { expectedVersion } })
    if (result === 'blocked') setBlocked(true)
  }
  return <div data-control={operation}>
    <Button type="button" variant="primary" size="sm" disabled={busy || expectedVersion === null} aria-busy={busy}
      onClick={() => void approve()}>{operation === 'approve' ? T.approve : T.reversalApprove}</Button>
    {blocked ? <p className={styles.error} role="alert">{T.blocked}</p> : null}
    <AttemptPanel attempt={attempt} operation={operation} onReload={reload} />
  </div>
}

const REASON_FORM_TEXT: Readonly<Record<ReasonOperation, {
  readonly heading: string
  readonly label: string
  readonly required: string
  readonly submit: string
  readonly variant: 'danger' | 'primary'
}>> = {
  reject: {
    heading: T.rejectHeading, label: T.rejectReason, required: T.rejectReasonRequired, submit: T.rejectSubmit,
    variant: 'danger',
  },
  'reversal-propose': {
    heading: T.reversalHeading, label: T.reversalReason, required: T.reversalReasonRequired, submit: T.reversalSubmit,
    variant: 'primary',
  },
  'reversal-reject': {
    heading: T.reversalRejectHeading, label: T.rejectReason, required: T.rejectReasonRequired, submit: T.rejectSubmit,
    variant: 'danger',
  },
}

/**
 * A command with a reason: the rejection of a proposal and the reversal proposal name the opening's version; the
 * rejection of a reversal names the reversal's version.
 */
function ReasonForm({ binding, scope, item, operation, onClose }: {
  readonly binding: OpeningBalanceAttemptBinding
  readonly scope: QueryScope
  readonly item: OpeningBalanceView
  readonly operation: ReasonOperation
  readonly onClose: () => void
}) {
  const attempt = useOpeningBalanceAttempt(binding, operation, item.id, useSettled(scope))
  const reload = useReload(scope)
  const [reason, setReason] = useState('')
  const [reasonError, setReasonError] = useState<string | null>(null)
  const [blocked, setBlocked] = useState(false)
  const busy = attempt.state.phase === 'submitting'
  const base = `opening-balance-${operation}`
  const text = REASON_FORM_TEXT[operation]
  const expectedVersion = operation === 'reversal-reject' ? item.reversal?.version ?? null : item.version

  const send = async (event: FormEvent) => {
    event.preventDefault()
    setBlocked(false)
    const trimmed = reason.trim()
    setReasonError(trimmed.length === 0 ? text.required : null)
    if (trimmed.length === 0 || expectedVersion === null) return
    const result = await attempt.submit({ operation, openingBalanceId: item.id,
      body: { expectedVersion, reason: trimmed } })
    if (result === 'blocked') setBlocked(true)
  }

  return <form className={styles.form} onSubmit={(event) => void send(event)} noValidate
    aria-labelledby={`${base}-heading`} data-form={operation}>
    <h2 id={`${base}-heading`}>{`${text.heading} — ${shortOpeningReference(item.id)}`}</h2>
    <div className={styles.field}>
      <label htmlFor={`${base}-reason`}>{text.label}</label>
      <textarea id={`${base}-reason`} value={reason} onChange={(event) => setReason(event.target.value)}
        aria-invalid={reasonError !== null} aria-describedby={reasonError === null ? undefined : `${base}-reason-error`} />
      {reasonError === null ? null : <p id={`${base}-reason-error`} className={styles.error}>{reasonError}</p>}
    </div>
    <div className={styles.actions}>
      <Button type="submit" variant={text.variant} disabled={busy} aria-busy={busy}>{text.submit}</Button>
      <Button type="button" variant="ghost" onClick={onClose}>{T.close}</Button>
    </div>
    {blocked ? <p className={styles.error} role="alert">{T.blocked}</p> : null}
    <AttemptPanel attempt={attempt} operation={operation} onReload={reload} />
  </form>
}

interface FormErrors {
  readonly targetKind: string | null
  readonly targetId: string | null
  readonly partyId: string | null
  readonly direction: string | null
  readonly currency: string | null
  readonly amount: string | null
  readonly asOfDate: string | null
}

const NO_ERRORS: FormErrors = {
  targetKind: null, targetId: null, partyId: null, direction: null, currency: null, amount: null, asOfDate: null,
}

function ProposeForm({ binding, scope, currencies, onRetryCurrencies }: {
  readonly binding: OpeningBalanceAttemptBinding
  readonly scope: QueryScope
  readonly currencies: ViewOutcome<OpeningBalanceCurrencyCandidates> | undefined
  readonly onRetryCurrencies: () => void
}) {
  const attempt = useOpeningBalanceAttempt(binding, 'propose', PROPOSAL_TARGET, useSettled(scope))
  const reload = useReload(scope)
  const [targetKind, setTargetKind] = useState<OpeningBalanceTargetKind | ''>('')
  const [targetId, setTargetId] = useState('')
  const [partyId, setPartyId] = useState('')
  const [direction, setDirection] = useState<OpeningBalanceDirection | ''>('')
  const [currencyId, setCurrencyId] = useState('')
  const [amount, setAmount] = useState('')
  const [asOfDate, setAsOfDate] = useState('')
  const [errors, setErrors] = useState<FormErrors>(NO_ERRORS)
  const [blocked, setBlocked] = useState(false)
  const base = 'opening-balance-propose'
  const party = targetKind === 'PartyAccount'

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setBlocked(false)
    const target = targetId.trim().toLowerCase()
    const partyValue = partyId.trim().toLowerCase()
    const parsed = parseAmountInput(amount)
    const next: FormErrors = {
      targetKind: targetKind === '' ? T.targetKindRequired : null,
      targetId: isBoundUuid(target) ? null : T.identityInvalid,
      partyId: party && !isBoundUuid(partyValue) ? T.identityInvalid : null,
      direction: party && direction === '' ? T.directionRequired : null,
      currency: currencyId.length === 0 ? T.currencyRequired : null,
      amount: parsed.ok ? null : T.amountError[parsed.error],
      asOfDate: isDateOnly(asOfDate) ? null : T.asOfDateRequired,
    }
    setErrors(next)
    if (Object.values(next).some((error) => error !== null) || !parsed.ok || targetKind === '') return
    const result = await attempt.submit({
      operation: 'propose',
      body: {
        targetKind,
        targetId: target,
        partyId: party ? partyValue : null,
        currencyId,
        direction: party && direction !== '' ? direction : null,
        amount: parsed.wire,
        asOfDate,
      },
    })
    if (result === 'blocked') setBlocked(true)
  }

  const described = (field: string, error: string | null, hint?: string) =>
    [hint, error === null ? undefined : `${base}-${field}-error`].filter(Boolean).join(' ') || undefined
  const fieldError = (field: string, error: string | null) =>
    error === null ? null : <p id={`${base}-${field}-error`} className={styles.error}>{error}</p>
  const busy = attempt.state.phase === 'submitting'
  const currencyItems = currencies?.type === 'ok-single' ? currencies.data.items : []

  return <form className={styles.form} onSubmit={(event) => void submit(event)} noValidate
    aria-labelledby={`${base}-heading`} data-form="propose">
    <h2 id={`${base}-heading`}>{T.proposeHeading}</h2>
    <div className={styles.field}>
      <label htmlFor={`${base}-kind`}>{T.targetKind}</label>
      <select id={`${base}-kind`} value={targetKind}
        onChange={(event) => setTargetKind(event.target.value as OpeningBalanceTargetKind | '')}
        aria-invalid={errors.targetKind !== null} aria-describedby={described('kind', errors.targetKind)}>
        <option value="">{T.targetKindPlaceholder}</option>
        {TARGET_KINDS.map((kind) => <option key={kind} value={kind}>{OPENING_BALANCE_TARGET[kind]}</option>)}
      </select>
      {fieldError('kind', errors.targetKind)}
    </div>
    <div className={styles.field}>
      <label htmlFor={`${base}-target`}>{T.targetId}</label>
      <input id={`${base}-target`} type="text" autoComplete="off" value={targetId}
        onChange={(event) => setTargetId(event.target.value)} aria-invalid={errors.targetId !== null}
        aria-describedby={described('target', errors.targetId)} />
      {fieldError('target', errors.targetId)}
    </div>
    {party
      ? <>
          <div className={styles.field}>
            <label htmlFor={`${base}-party`}>{T.partyId}</label>
            <input id={`${base}-party`} type="text" autoComplete="off" value={partyId}
              onChange={(event) => setPartyId(event.target.value)} aria-invalid={errors.partyId !== null}
              aria-describedby={described('party', errors.partyId)} />
            {fieldError('party', errors.partyId)}
          </div>
          <div className={styles.field}>
            <label htmlFor={`${base}-direction`}>{T.direction}</label>
            <select id={`${base}-direction`} value={direction}
              onChange={(event) => setDirection(event.target.value as OpeningBalanceDirection | '')}
              aria-invalid={errors.direction !== null} aria-describedby={described('direction', errors.direction)}>
              <option value="">{T.directionPlaceholder}</option>
              {DIRECTIONS.map((value) => <option key={value} value={value}>{OPENING_BALANCE_DIRECTION[value]}</option>)}
            </select>
            {fieldError('direction', errors.direction)}
          </div>
        </>
      : null}
    {currencies?.type !== 'ok-single'
      ? <StateBlock state={(currencies === undefined ? { kind: 'initial-loading' } : classifyOutcome(currencies)) as
          RemoteViewState<unknown>} subject={T.currencySubject} onRetry={onRetryCurrencies} />
      : currencyItems.length === 0
        ? <p className={styles.error} role="alert">{T.noCurrencies}</p>
        : <div className={styles.field}>
            <label htmlFor={`${base}-currency`}>{T.currency}</label>
            <select id={`${base}-currency`} value={currencyId} onChange={(event) => setCurrencyId(event.target.value)}
              aria-invalid={errors.currency !== null} aria-describedby={described('currency', errors.currency)}>
              <option value="">{T.currencyPlaceholder}</option>
              {currencyItems.map((item) => <option key={item.currencyId} value={item.currencyId}>
                {`${item.code} — ${item.displayLabel}`}
              </option>)}
            </select>
            {fieldError('currency', errors.currency)}
          </div>}
    <div className={styles.field}>
      <label htmlFor={`${base}-amount`}>{T.amount}</label>
      <input id={`${base}-amount`} type="text" inputMode="decimal" autoComplete="off" value={amount}
        onChange={(event) => setAmount(event.target.value)} aria-invalid={errors.amount !== null}
        aria-describedby={described('amount', errors.amount, `${base}-amount-hint`)} />
      <p id={`${base}-amount-hint`} className={styles.hint}>{T.amountHint}</p>
      {fieldError('amount', errors.amount)}
    </div>
    <div className={styles.field}>
      <label htmlFor={`${base}-date`}>{T.asOfDate}</label>
      <input id={`${base}-date`} type="date" value={asOfDate} onChange={(event) => setAsOfDate(event.target.value)}
        aria-invalid={errors.asOfDate !== null} aria-describedby={described('date', errors.asOfDate)} />
      {fieldError('date', errors.asOfDate)}
    </div>
    <div className={styles.actions}>
      <Button type="submit" variant="primary" disabled={busy} aria-busy={busy}>{T.proposeSubmit}</Button>
    </div>
    {blocked ? <p className={styles.error} role="alert">{T.blocked}</p> : null}
    <AttemptPanel attempt={attempt} operation="propose" onReload={reload} />
  </form>
}
