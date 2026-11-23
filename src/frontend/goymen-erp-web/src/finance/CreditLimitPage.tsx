/**
 * API-85a — Finans → Cari kredi limitleri (F018 plan §L1; FNS-D27 / FNS-D27a; one step, FNS-D56 / FNS-D57(3)).
 *
 * A credit limit is a MASTER record: one currency and one amount per company account, or "limit yok". This screen
 * shows the server's reads and sends the server's commands; it works out no risk, no balance and no comparison.
 *
 * - An approver (`finance.credit-limits.approve`) sets, changes or removes a limit in ONE step (FNS-D27). Every such
 *   command names the account revision the person looked at; a change in between is a server-side conflict.
 * - A proposer (`finance.credit-limits.propose`) proposes; the proposal changes nothing until an approver OTHER than
 *   the proposer approves it (FNS-D27a). The server refuses a self-decision; the screen only hides the buttons for the
 *   caller's own pending proposal, it is not the guard.
 * - The two authority flags are passed in by the route owner; the server checks every command on its own.
 *
 * A removed limit is "limit yok", never zero and never unlimited. A company, actor or session change remounts the
 * screen, so no answer of the previous context is drawn.
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
import { formatDecimalTextTr, parseAmountInput } from './decimalText'
import type {
  CreditLimitAccount,
  CreditLimitCommandResult,
  CreditLimitCurrencyCandidates,
  CreditLimitKind,
  CreditLimitRecord,
} from './CreditLimitContract'
import { useCreditLimitAttempt, type CreditLimitAttempt, type CreditLimitAttemptBinding } from './CreditLimitCommand'
import {
  fetchCreditLimitAccount,
  fetchCreditLimitCurrencyCandidates,
  fetchCreditLimitHistory,
  fetchCreditLimits,
  type CreditLimitCommand,
  type CreditLimitOperation,
} from './CreditLimitHttp'
import {
  CREDIT_LIMIT_KIND,
  CREDIT_LIMIT_STATUS,
  CREDIT_LIMIT_SUCCESS,
  CREDIT_LIMIT_TEXT as T,
  creditLimitAccountName,
  creditLimitCurrencyReference,
  creditLimitIssueText,
  shortCreditLimitReference,
} from './CreditLimitText'
import styles from './Finance.module.css'

const HEADING_ID = 'finance-credit-limit-heading'
const DETAIL_HEADING_ID = 'finance-credit-limit-detail-heading'
const NEW_HEADING_ID = 'finance-credit-limit-new-heading'

export interface CreditLimitPageProps {
  /** The caller holds `finance.credit-limits.propose` (route owner's capability answer). */
  readonly canPropose: boolean
  /** The caller holds `finance.credit-limits.approve` (route owner's capability answer). */
  readonly canApprove: boolean
}

function useBinding() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const actorId = shell.activeContext?.userId ?? null
  const attemptBinding: CreditLimitAttemptBinding | null = companyId === null || actorId === null
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

export function CreditLimitPage(props: CreditLimitPageProps) {
  const { mountKey } = useBinding()
  return <CreditLimitScreen key={mountKey} {...props} />
}

/** Presentation only. */
function Instant({ value }: { readonly value: string }) {
  const parsed = new Date(value)
  return <time dateTime={value}>
    {Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString('tr-TR', { dateStyle: 'medium', timeStyle: 'short' })}
  </time>
}

type CurrencyNames = ReadonlyMap<string, string>

/** The candidate's code when the candidates read answered; otherwise the short reference the record carries. */
const currencyName = (names: CurrencyNames, currencyId: string) =>
  names.get(currencyId) ?? creditLimitCurrencyReference(shortCreditLimitReference(currencyId))

function LimitValue({ record, names }: { readonly record: CreditLimitRecord | null; readonly names: CurrencyNames }) {
  if (record === null) return <>{T.noLimitNever}</>
  if (record.kind === 'Remove' || record.amount === null || record.currencyId === null) return <>{T.noLimitRemoved}</>
  return <span data-amount-kind="limit">
    {`${formatDecimalTextTr(record.amount) ?? record.amount} ${currencyName(names, record.currencyId)}`}
  </span>
}

function PendingValue({ account, names }: { readonly account: CreditLimitAccount; readonly names: CurrencyNames }) {
  const pending = account.pending
  if (pending === null) return <>{T.noPending}</>
  return <>
    {CREDIT_LIMIT_KIND[pending.kind]}
    {pending.kind === 'Set' ? <>{' — '}<LimitValue record={pending} names={names} /></> : null}
    {account.isOwnPending ? <span className={styles.hint}>{` ${T.ownPending}`}</span> : null}
  </>
}

function useQueryScope(namespace: string | null, generation: number) {
  return [namespace ?? 'no-context', generation, 'finance-credit-limits'] as const
}

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

function CreditLimitScreen({ canPropose, canApprove }: CreditLimitPageProps) {
  const { shell, namespace, companyId, attemptBinding } = useBinding()
  const scope = useQueryScope(namespace, shell.routeEntryGeneration)
  const reported = useReporter()
  const [selected, setSelected] = useState<string | null>(null)
  const [creating, setCreating] = useState(false)

  const list = useQuery({
    queryKey: [...scope, 'list'],
    enabled: namespace !== null && companyId !== null,
    gcTime: 0,
    queryFn: ({ signal }) => reported(() => fetchCreditLimits(companyId ?? '', signal)),
  })
  const candidates = useQuery({
    queryKey: [...scope, 'currency-candidates'],
    enabled: namespace !== null && companyId !== null && (canApprove || canPropose),
    gcTime: 0,
    queryFn: ({ signal }) => reported(() => fetchCreditLimitCurrencyCandidates(companyId ?? '', signal)),
  })
  const candidateList = candidates.data?.type === 'ok-single' ? candidates.data.data : null
  const names: CurrencyNames = new Map((candidateList?.items ?? []).map((item) => [item.currencyId, item.code]))

  const frame = (body: ReactNode) => (
    <section className={styles.list} aria-labelledby={HEADING_ID} data-testid="finance-credit-limits">
      <h1 id={HEADING_ID}>{T.title}</h1>
      <p className={styles.notice}>{T.notice}</p>
      {body}
    </section>
  )

  if (companyId === null || attemptBinding === null) {
    return frame(<StateBlock state={{ kind: 'no-authorized-company' }} subject={T.subject} />)
  }
  const page = list.data?.type === 'ok-single' ? list.data.data : null
  if (page === null) {
    const state = list.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(list.data)
    return frame(<StateBlock state={state as RemoteViewState<unknown>} subject={T.subject} onRetry={() => void list.refetch()} />)
  }

  const editor = canApprove || canPropose
  const newAccount = creating && editor
    ? <NewAccountLimit binding={attemptBinding} scope={scope} candidates={candidates.data}
        canApprove={canApprove} onRetryCandidates={() => void candidates.refetch()} onClose={() => setCreating(false)} />
    : editor
      ? <div className={styles.actions}>
          <Button type="button" variant="secondary" onClick={() => setCreating(true)}>{T.newHeading}</Button>
        </div>
      : null

  return frame(<>
    {page.items.length === 0
      ? <StateBlock state={{ kind: 'empty-no-data' }} subject={T.subject} />
      : <table>
          <caption>{T.tableCaption}</caption>
          <thead><tr>
            <th scope="col">{T.column.account}</th>
            <th scope="col">{T.column.party}</th>
            <th scope="col">{T.column.limit}</th>
            <th scope="col">{T.column.pending}</th>
            <th scope="col">{T.column.action}</th>
          </tr></thead>
          <tbody>
            {page.items.map((account) => {
              const reference = shortCreditLimitReference(account.companyAccountId)
              const open = selected === account.companyAccountId
              return <tr key={account.companyAccountId} data-account={account.companyAccountId}>
                <td>{reference}</td>
                <td>{shortCreditLimitReference(account.partyId)}</td>
                <td><LimitValue record={account.effective} names={names} /></td>
                <td><PendingValue account={account} names={names} /></td>
                <td>
                  <Button type="button" variant="ghost" size="sm" aria-label={creditLimitAccountName(reference)}
                    aria-expanded={open} onClick={() => setSelected(open ? null : account.companyAccountId)}>
                    {open ? T.close : T.open}
                  </Button>
                </td>
              </tr>
            })}
          </tbody>
        </table>}
    {canApprove || canPropose ? null : <p className={styles.hint}>{T.pendingHidden}</p>}
    {selected !== null
      ? <AccountDetail key={selected} companyAccountId={selected} binding={attemptBinding} scope={scope} names={names}
          candidates={candidates.data} onRetryCandidates={() => void candidates.refetch()}
          canPropose={canPropose} canApprove={canApprove} />
      : null}
    {newAccount}
  </>)
}

type QueryScope = ReturnType<typeof useQueryScope>

/** After a definite success every read of this screen is answered again. */
function useSettled(scope: QueryScope) {
  const client = useQueryClient()
  return useCallback((_operation: CreditLimitOperation, result: CreditLimitCommandResult) => {
    if (result.outcome === 'Succeeded') void client.invalidateQueries({ queryKey: scope })
  }, [client, scope])
}

interface DetailProps {
  readonly companyAccountId: string
  readonly binding: CreditLimitAttemptBinding
  readonly scope: QueryScope
  readonly names: CurrencyNames
  readonly candidates: ViewOutcome<CreditLimitCurrencyCandidates> | undefined
  readonly onRetryCandidates: () => void
  readonly canPropose: boolean
  readonly canApprove: boolean
}

function AccountDetail({ companyAccountId, binding, scope, names, candidates, onRetryCandidates, canPropose,
  canApprove }: DetailProps) {
  const reported = useReporter()
  const account = useQuery({
    queryKey: [...scope, 'account', companyAccountId],
    gcTime: 0,
    queryFn: ({ signal }) => reported(() => fetchCreditLimitAccount(binding.companyId, companyAccountId, signal)),
  })
  const history = useQuery({
    queryKey: [...scope, 'history', companyAccountId],
    gcTime: 0,
    queryFn: ({ signal }) => reported(() => fetchCreditLimitHistory(binding.companyId, companyAccountId, signal)),
  })

  const frame = (body: ReactNode) => (
    <section className={styles.detail} aria-labelledby={DETAIL_HEADING_ID} data-testid="finance-credit-limit-detail">
      <h2 id={DETAIL_HEADING_ID}>{`${T.detailHeading} — ${shortCreditLimitReference(companyAccountId)}`}</h2>
      {body}
    </section>
  )

  const current = account.data?.type === 'ok-single' ? account.data.data : null
  if (current === null) {
    const state = account.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(account.data)
    return frame(<StateBlock state={state as RemoteViewState<unknown>} subject={T.accountSubject}
      onRetry={() => void account.refetch()} />)
  }
  const pending = current.pending
  const decide = canApprove && pending !== null && !current.isOwnPending

  return frame(<>
    <dl className={styles.facts}>
      <div><dt>{T.effectiveHeading}</dt><dd><LimitValue record={current.effective} names={names} /></dd></div>
      <div><dt>{T.pendingHeading}</dt><dd><PendingValue account={current} names={names} /></dd></div>
    </dl>
    {decide && pending !== null ? <ProposalDecision binding={binding} scope={scope} proposal={pending}
      target={current.companyAccountId} /> : null}
    {canApprove
      ? <LimitForm mode="set" binding={binding} scope={scope} target={current} candidates={candidates}
          onRetryCandidates={onRetryCandidates} />
      : null}
    {canApprove && current.effective?.kind === 'Set'
      ? <RemoveLimit binding={binding} scope={scope} target={current} />
      : null}
    {canPropose && pending === null
      ? <LimitForm mode="propose" binding={binding} scope={scope} target={current} candidates={candidates}
          onRetryCandidates={onRetryCandidates} />
      : null}
    <HistoryTable outcome={history.data} names={names} onRetry={() => void history.refetch()} />
  </>)
}

function HistoryTable({ outcome, names, onRetry }: {
  readonly outcome: ViewOutcome<{ readonly items: readonly CreditLimitRecord[] }> | undefined
  readonly names: CurrencyNames
  readonly onRetry: () => void
}) {
  const page = outcome?.type === 'ok-single' ? outcome.data : null
  if (page === null) {
    const state = outcome === undefined ? { kind: 'initial-loading' } : classifyOutcome(outcome)
    return <StateBlock state={state as RemoteViewState<unknown>} subject={T.historySubject} onRetry={onRetry} />
  }
  if (page.items.length === 0) return <p className={styles.hint}>{T.historyEmpty}</p>
  return <table>
    <caption>{T.historyCaption}</caption>
    <thead><tr>
      <th scope="col">{T.column.kind}</th>
      <th scope="col">{T.column.limit}</th>
      <th scope="col">{T.column.status}</th>
      <th scope="col">{T.column.direct}</th>
      <th scope="col">{T.column.proposedAt}</th>
      <th scope="col">{T.column.decidedAt}</th>
      <th scope="col">{T.column.reason}</th>
    </tr></thead>
    <tbody>
      {page.items.map((record) => <tr key={record.id} data-record={record.id}>
        <td>{CREDIT_LIMIT_KIND[record.kind]}</td>
        <td>{record.kind === 'Set' ? <LimitValue record={record} names={names} /> : T.noLimit}</td>
        <td>{CREDIT_LIMIT_STATUS[record.status]}</td>
        <td>{record.isDirect ? T.directRecord : T.proposalRecord}</td>
        <td><Instant value={record.proposedAtUtc} /></td>
        <td>{record.decidedAtUtc === null ? '' : <Instant value={record.decidedAtUtc} />}</td>
        <td>{record.rejectionReason ?? ''}</td>
      </tr>)}
    </tbody>
  </table>
}

/** What a set or proposal names: the account and the revision the person looked at. */
interface LimitTarget {
  readonly partyId: string
  readonly companyAccountId: string
  readonly revision: string
}

function AttemptPanel({ attempt, operation, onReload }: {
  readonly attempt: CreditLimitAttempt
  readonly operation: CreditLimitOperation
  readonly onReload?: () => void
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
          <p>{CREDIT_LIMIT_SUCCESS[operation]}</p>
          {replay}
          <Button type="button" variant="ghost" onClick={attempt.reset}>{T.dismiss}</Button>
        </div>
      }
      return <div className={styles.attempt} role="alert" data-attempt={result.outcome}>
        <p className={styles.error}>{T.outcome[result.outcome]}</p>
        {result.issues.length > 0
          ? <ul>{result.issues.map((issue) => <li key={`${issue.field}:${issue.code}`}>{creditLimitIssueText(issue.code)}</li>)}</ul>
          : null}
        {replay}
        <div className={styles.actions}>
          {result.outcome === 'VersionConflict' && onReload !== undefined
            ? <Button type="button" variant="secondary" onClick={onReload}>{T.reload}</Button>
            : null}
          <Button type="button" variant="ghost" onClick={attempt.reset}>{T.dismiss}</Button>
        </div>
      </div>
    }
  }
}

function useReload(scope: QueryScope) {
  const client = useQueryClient()
  return useCallback(() => void client.invalidateQueries({ queryKey: scope }), [client, scope])
}

function CurrencyField({ id, candidates, value, onChange, onRetry, error }: {
  readonly id: string
  readonly candidates: ViewOutcome<CreditLimitCurrencyCandidates> | undefined
  readonly value: string
  readonly onChange: (value: string) => void
  readonly onRetry: () => void
  readonly error: string | null
}) {
  const list = candidates?.type === 'ok-single' ? candidates.data : null
  if (list === null) {
    const state = candidates === undefined ? { kind: 'initial-loading' } : classifyOutcome(candidates)
    return <StateBlock state={state as RemoteViewState<unknown>} subject={T.currencySubject} onRetry={onRetry} />
  }
  if (list.items.length === 0) return <p className={styles.error} role="alert">{T.noCandidates}</p>
  return <div className={styles.field}>
    <label htmlFor={id}>{T.currency}</label>
    <select id={id} value={value} onChange={(event) => onChange(event.target.value)}
      aria-invalid={error !== null} aria-describedby={error === null ? undefined : `${id}-error`}>
      <option value="">{T.currencyPlaceholder}</option>
      {list.items.map((item) => <option key={item.currencyId} value={item.currencyId}>{`${item.code} — ${item.displayLabel}`}</option>)}
    </select>
    {error === null ? null : <p id={`${id}-error`} className={styles.error}>{error}</p>}
  </div>
}

function LimitForm({ mode, binding, scope, target, candidates, onRetryCandidates, idPrefix = 'credit-limit' }: {
  readonly mode: 'set' | 'propose'
  readonly binding: CreditLimitAttemptBinding
  readonly scope: QueryScope
  readonly target: LimitTarget
  readonly candidates: ViewOutcome<CreditLimitCurrencyCandidates> | undefined
  readonly onRetryCandidates: () => void
  readonly idPrefix?: string
}) {
  const attempt = useCreditLimitAttempt(binding, mode, target.companyAccountId, useSettled(scope))
  const reload = useReload(scope)
  const [kind, setKind] = useState<CreditLimitKind>('Set')
  const [currencyId, setCurrencyId] = useState('')
  const [amount, setAmount] = useState('')
  const [currencyError, setCurrencyError] = useState<string | null>(null)
  const [amountError, setAmountError] = useState<string | null>(null)
  const [blocked, setBlocked] = useState(false)
  const base = `${idPrefix}-${mode}`
  const removing = mode === 'propose' && kind === 'Remove'

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setBlocked(false)
    let command: CreditLimitCommand
    if (removing) {
      command = {
        operation: 'propose' as const,
        body: { partyId: target.partyId, companyAccountId: target.companyAccountId, kind: 'Remove' as const,
          currencyId: null, amount: null, expectedRevision: target.revision },
      }
    } else {
      const parsed = parseAmountInput(amount)
      const currencyOk = currencyId.length > 0
      setCurrencyError(currencyOk ? null : T.currencyRequired)
      setAmountError(parsed.ok ? null : T.amountError[parsed.error])
      if (!parsed.ok || !currencyOk) return
      command = mode === 'set'
        ? { operation: 'set' as const,
            body: { partyId: target.partyId, companyAccountId: target.companyAccountId, currencyId, amount: parsed.wire,
              expectedRevision: target.revision } }
        : { operation: 'propose' as const,
            body: { partyId: target.partyId, companyAccountId: target.companyAccountId, kind: 'Set' as const, currencyId,
              amount: parsed.wire, expectedRevision: target.revision } }
    }
    if (await attempt.submit(command) === 'blocked') setBlocked(true)
  }

  const busy = attempt.state.phase === 'submitting'
  return <form className={styles.form} onSubmit={(event) => void submit(event)} noValidate
    aria-labelledby={`${base}-heading`} data-form={mode}>
    <h3 id={`${base}-heading`}>{mode === 'set' ? T.setHeading : T.proposeHeading}</h3>
    {mode === 'propose'
      ? <fieldset className={styles.field}>
          <legend>{T.proposeKind}</legend>
          <label><input type="radio" name={`${base}-kind`} checked={kind === 'Set'} onChange={() => setKind('Set')} />
            {T.proposeKindSet}</label>
          <label><input type="radio" name={`${base}-kind`} checked={kind === 'Remove'} onChange={() => setKind('Remove')} />
            {T.proposeKindRemove}</label>
        </fieldset>
      : null}
    {removing
      ? <p className={styles.hint}>{T.removeHint}</p>
      : <>
          <CurrencyField id={`${base}-currency`} candidates={candidates} value={currencyId} onChange={setCurrencyId}
            onRetry={onRetryCandidates} error={currencyError} />
          <div className={styles.field}>
            <label htmlFor={`${base}-amount`}>{T.amount}</label>
            <input id={`${base}-amount`} type="text" inputMode="decimal" autoComplete="off" value={amount}
              onChange={(event) => setAmount(event.target.value)} aria-invalid={amountError !== null}
              aria-describedby={`${base}-amount-hint${amountError === null ? '' : ` ${base}-amount-error`}`} />
            <p id={`${base}-amount-hint`} className={styles.hint}>{T.amountHint}</p>
            {amountError === null ? null : <p id={`${base}-amount-error`} className={styles.error}>{amountError}</p>}
          </div>
        </>}
    <div className={styles.actions}>
      <Button type="submit" variant="primary" disabled={busy} aria-busy={busy}>
        {mode === 'set' ? T.setSubmit : T.proposeSubmit}
      </Button>
    </div>
    {blocked ? <p className={styles.error} role="alert">{T.blocked}</p> : null}
    <AttemptPanel attempt={attempt} operation={mode} onReload={reload} />
  </form>
}

function RemoveLimit({ binding, scope, target }: {
  readonly binding: CreditLimitAttemptBinding
  readonly scope: QueryScope
  readonly target: LimitTarget
}) {
  const attempt = useCreditLimitAttempt(binding, 'remove', target.companyAccountId, useSettled(scope))
  const reload = useReload(scope)
  const [blocked, setBlocked] = useState(false)
  const busy = attempt.state.phase === 'submitting'
  const remove = async () => {
    setBlocked(false)
    const result = await attempt.submit({
      operation: 'remove',
      body: { partyId: target.partyId, companyAccountId: target.companyAccountId, expectedRevision: target.revision },
    })
    if (result === 'blocked') setBlocked(true)
  }
  return <div className={styles.command} data-form="remove">
    <h3>{T.removeHeading}</h3>
    <p className={styles.hint}>{T.removeHint}</p>
    <Button type="button" variant="danger" disabled={busy} aria-busy={busy} onClick={() => void remove()}>
      {T.removeSubmit}
    </Button>
    {blocked ? <p className={styles.error} role="alert">{T.blocked}</p> : null}
    <AttemptPanel attempt={attempt} operation="remove" onReload={reload} />
  </div>
}

function ProposalDecision({ binding, scope, proposal, target }: {
  readonly binding: CreditLimitAttemptBinding
  readonly scope: QueryScope
  readonly proposal: CreditLimitRecord
  readonly target: string
}) {
  const settled = useSettled(scope)
  const reload = useReload(scope)
  const approval = useCreditLimitAttempt(binding, 'approve', target, settled)
  const rejection = useCreditLimitAttempt(binding, 'reject', target, settled)
  const [reason, setReason] = useState('')
  const [reasonError, setReasonError] = useState<string | null>(null)
  const [blocked, setBlocked] = useState(false)
  const busy = approval.state.phase === 'submitting' || rejection.state.phase === 'submitting'

  const approve = async () => {
    setBlocked(false)
    const result = await approval.submit({ operation: 'approve', proposalId: proposal.id,
      body: { expectedVersion: proposal.version } })
    if (result === 'blocked') setBlocked(true)
  }
  const reject = async () => {
    setBlocked(false)
    const trimmed = reason.trim()
    setReasonError(trimmed.length === 0 ? T.rejectReasonRequired : null)
    if (trimmed.length === 0) return
    const result = await rejection.submit({ operation: 'reject', proposalId: proposal.id,
      body: { expectedVersion: proposal.version, reason: trimmed } })
    if (result === 'blocked') setBlocked(true)
  }

  return <div className={styles.command} data-form="decision">
    <div className={styles.actions}>
      <Button type="button" variant="primary" disabled={busy} onClick={() => void approve()}>{T.approve}</Button>
    </div>
    <div className={styles.field}>
      <label htmlFor="credit-limit-reject-reason">{T.rejectReason}</label>
      <textarea id="credit-limit-reject-reason" value={reason} onChange={(event) => setReason(event.target.value)}
        aria-invalid={reasonError !== null}
        aria-describedby={reasonError === null ? undefined : 'credit-limit-reject-reason-error'} />
      {reasonError === null ? null : <p id="credit-limit-reject-reason-error" className={styles.error}>{reasonError}</p>}
    </div>
    <div className={styles.actions}>
      <Button type="button" variant="danger" disabled={busy} onClick={() => void reject()}>{T.reject}</Button>
    </div>
    {blocked ? <p className={styles.error} role="alert">{T.blocked}</p> : null}
    <AttemptPanel attempt={approval} operation="approve" onReload={reload} />
    <AttemptPanel attempt={rejection} operation="reject" onReload={reload} />
  </div>
}

/**
 * An account with no limit yet: Cari owns parties and accounts, so this screen offers no picker and names the two
 * identities as typed. The server checks that the account belongs to the party and may carry a limit; revision "0"
 * says "no limit was ever effective here".
 */
function NewAccountLimit({ binding, scope, candidates, canApprove, onRetryCandidates, onClose }: {
  readonly binding: CreditLimitAttemptBinding
  readonly scope: QueryScope
  readonly candidates: ViewOutcome<CreditLimitCurrencyCandidates> | undefined
  readonly canApprove: boolean
  readonly onRetryCandidates: () => void
  readonly onClose: () => void
}) {
  const [partyId, setPartyId] = useState('')
  const [companyAccountId, setCompanyAccountId] = useState('')
  const [target, setTarget] = useState<LimitTarget | null>(null)
  const [error, setError] = useState(false)

  const confirm = (event: FormEvent) => {
    event.preventDefault()
    const party = partyId.trim().toLowerCase()
    const account = companyAccountId.trim().toLowerCase()
    if (!isBoundUuid(party) || !isBoundUuid(account)) {
      setError(true)
      return
    }
    setError(false)
    setTarget({ partyId: party, companyAccountId: account, revision: '0' })
  }

  return <section className={styles.detail} aria-labelledby={NEW_HEADING_ID} data-testid="finance-credit-limit-new">
    <h2 id={NEW_HEADING_ID}>{T.newHeading}</h2>
    {target === null
      ? <form className={styles.form} onSubmit={confirm} noValidate>
          <p className={styles.hint}>{T.newHint}</p>
          <div className={styles.field}>
            <label htmlFor="credit-limit-new-party">{T.partyId}</label>
            <input id="credit-limit-new-party" type="text" autoComplete="off" value={partyId}
              onChange={(event) => setPartyId(event.target.value)} aria-invalid={error} />
          </div>
          <div className={styles.field}>
            <label htmlFor="credit-limit-new-account">{T.companyAccountId}</label>
            <input id="credit-limit-new-account" type="text" autoComplete="off" value={companyAccountId}
              onChange={(event) => setCompanyAccountId(event.target.value)} aria-invalid={error} />
          </div>
          {error ? <p className={styles.error} role="alert">{T.identityInvalid}</p> : null}
          <div className={styles.actions}>
            <Button type="submit" variant="secondary">{T.open}</Button>
            <Button type="button" variant="ghost" onClick={onClose}>{T.close}</Button>
          </div>
        </form>
      : <>
          <LimitForm mode={canApprove ? 'set' : 'propose'} binding={binding} scope={scope} target={target}
            candidates={candidates} onRetryCandidates={onRetryCandidates} idPrefix="credit-limit-new" />
          <div className={styles.actions}>
            <Button type="button" variant="ghost" onClick={onClose}>{T.close}</Button>
          </div>
        </>}
  </section>
}
