/**
 * API-85b — Finans → Müşteri teminatları (F018 plan §L2; FNS-D57, FNS-D31, FNS-D58).
 *
 * A collateral is a MASTER record of a customer's security: a Settings collateral type, a currency, an amount and a
 * validity. This screen shows the server's reads and sends the server's commands; it works out no figure.
 *
 * - Record, change and release are ONE step each and effective at once (FNS-D57): no proposer, no approver, no
 *   threshold. A change or a release names the collateral revision the person looked at; a change in between is a
 *   server-side conflict. A release is a reverse record with a reason, never a delete.
 * - The types (and their rates) are Settings' (FNS-D31); a cheque or a note is never offered and the server refuses it.
 * - The weighted collateral and the documentary risk (FNS-D58) are the server's figures, per currency. The documentary
 *   risk is shown on its own and is never added to anything here.
 * - `canRecord` is the route owner's capability answer; the server checks every command on its own.
 *
 * Cari owns parties and accounts, so the account and the party are named by identity as typed. A company, actor or
 * session change remounts the screen, so no answer of the previous context is drawn.
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
import { amountInputFromWire, formatDecimalTextTr, parseAmountInput } from './decimalText'
import type {
  CollateralCommandResult,
  CollateralCurrencyCandidates,
  CollateralFigure,
  CollateralRecord,
  CollateralTypeCandidates,
} from './CollateralContract'
import {
  requiresCheckBeforeAbandon,
  useCollateralAttempt,
  type CollateralAttempt,
  type CollateralAttemptBinding,
} from './CollateralCommand'
import {
  fetchCollateralAccount,
  fetchCollateralCurrencyCandidates,
  fetchCollateralHistory,
  fetchCollateralTypeCandidates,
  type CollateralCommand,
  type CollateralOperation,
} from './CollateralHttp'
import {
  COLLATERAL_KIND,
  COLLATERAL_STATUS,
  COLLATERAL_SUCCESS,
  COLLATERAL_TEXT as T,
  collateralFigureReasonText,
  collateralIssueText,
  shortCollateralReference,
} from './CollateralText'
import styles from './Finance.module.css'

const HEADING_ID = 'finance-collateral-heading'
const ACCOUNT_HEADING_ID = 'finance-collateral-account-heading'

export interface CollateralPageProps {
  /** The caller holds `finance.collaterals.record` (route owner's capability answer). */
  readonly canRecord: boolean
}

function useBinding() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const actorId = shell.activeContext?.userId ?? null
  const attemptBinding: CollateralAttemptBinding | null = companyId === null || actorId === null
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

export function CollateralPage(props: CollateralPageProps) {
  const { mountKey } = useBinding()
  return <CollateralScreen key={mountKey} {...props} />
}

/** Presentation only. */
function Instant({ value }: { readonly value: string }) {
  const parsed = new Date(value)
  return <time dateTime={value}>
    {Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString('tr-TR', { dateStyle: 'medium', timeStyle: 'short' })}
  </time>
}

type Names = ReadonlyMap<string, string>

/** The candidate's code when the candidates read answered; otherwise the short reference the record carries. */
const nameOf = (names: Names, id: string) => names.get(id) ?? shortCollateralReference(id)

const amountText = (amount: string, currencyId: string, currencies: Names) =>
  `${formatDecimalTextTr(amount) ?? amount} ${nameOf(currencies, currencyId)}`

function useQueryScope(namespace: string | null, generation: number) {
  return [namespace ?? 'no-context', generation, 'finance-collaterals'] as const
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

interface Candidates {
  readonly types: ViewOutcome<CollateralTypeCandidates> | undefined
  readonly currencies: ViewOutcome<CollateralCurrencyCandidates> | undefined
  readonly retryTypes: () => void
  readonly retryCurrencies: () => void
  readonly typeNames: Names
  readonly currencyNames: Names
}

function CollateralScreen({ canRecord }: CollateralPageProps) {
  const { shell, namespace, companyId, attemptBinding } = useBinding()
  const scope = useQueryScope(namespace, shell.routeEntryGeneration)
  const reported = useReporter()
  const [accountInput, setAccountInput] = useState('')
  const [accountError, setAccountError] = useState(false)
  const [selected, setSelected] = useState<string | null>(null)

  const enabled = namespace !== null && companyId !== null
  const types = useQuery({
    queryKey: [...scope, 'type-candidates'],
    enabled,
    gcTime: 0,
    queryFn: ({ signal }) => reported(() => fetchCollateralTypeCandidates(companyId ?? '', signal)),
  })
  const currencies = useQuery({
    queryKey: [...scope, 'currency-candidates'],
    enabled,
    gcTime: 0,
    queryFn: ({ signal }) => reported(() => fetchCollateralCurrencyCandidates(companyId ?? '', signal)),
  })
  const typeList = types.data?.type === 'ok-single' ? types.data.data.items : []
  const currencyList = currencies.data?.type === 'ok-single' ? currencies.data.data.items : []
  const candidates: Candidates = {
    types: types.data,
    currencies: currencies.data,
    retryTypes: () => void types.refetch(),
    retryCurrencies: () => void currencies.refetch(),
    typeNames: new Map(typeList.map((item) => [item.collateralTypeId, item.code])),
    currencyNames: new Map(currencyList.map((item) => [item.currencyId, item.code])),
  }

  const frame = (body: ReactNode) => (
    <section className={styles.list} aria-labelledby={HEADING_ID} data-testid="finance-collaterals">
      <h1 id={HEADING_ID}>{T.title}</h1>
      <p className={styles.notice}>{T.notice}</p>
      {body}
    </section>
  )

  if (companyId === null || attemptBinding === null) {
    return frame(<StateBlock state={{ kind: 'no-authorized-company' }} subject={T.accountSubject} />)
  }

  const lookup = (event: FormEvent) => {
    event.preventDefault()
    const account = accountInput.trim().toLowerCase()
    if (!isBoundUuid(account)) {
      setAccountError(true)
      return
    }
    setAccountError(false)
    setSelected(account)
  }

  return frame(<>
    <form className={styles.form} onSubmit={lookup} noValidate aria-label={T.lookupHeading} data-form="lookup">
      <p className={styles.hint}>{T.lookupHint}</p>
      <div className={styles.field}>
        <label htmlFor="collateral-lookup-account">{T.companyAccountId}</label>
        <input id="collateral-lookup-account" type="text" autoComplete="off" value={accountInput}
          onChange={(event) => setAccountInput(event.target.value)} aria-invalid={accountError}
          aria-describedby={accountError ? 'collateral-lookup-account-error' : undefined} />
        {accountError
          ? <p id="collateral-lookup-account-error" className={styles.error} role="alert">{T.identityInvalid}</p>
          : null}
      </div>
      <div className={styles.actions}>
        <Button type="submit" variant="secondary">{T.show}</Button>
        {selected !== null
          ? <Button type="button" variant="ghost" onClick={() => setSelected(null)}>{T.close}</Button>
          : null}
      </div>
    </form>
    {selected !== null
      ? <AccountView key={selected} companyAccountId={selected} binding={attemptBinding} scope={scope}
          candidates={candidates} canRecord={canRecord} />
      : null}
  </>)
}

/** After a definite success every read of this screen is answered again. */
function useSettled(scope: QueryScope) {
  const client = useQueryClient()
  return useCallback((_operation: CollateralOperation, result: CollateralCommandResult) => {
    if (result.outcome === 'Succeeded') void client.invalidateQueries({ queryKey: scope })
  }, [client, scope])
}

function useReload(scope: QueryScope) {
  const client = useQueryClient()
  return useCallback(() => void client.invalidateQueries({ queryKey: scope }), [client, scope])
}

function FigureValue({ figure, currencies, testId }: {
  readonly figure: CollateralFigure
  readonly currencies: Names
  readonly testId: string
}) {
  if (figure.state !== 'Available') {
    return <span data-testid={testId} data-figure-state={figure.state}>{collateralFigureReasonText(figure.reasonCode)}</span>
  }
  if (figure.amounts.length === 0) return <span data-testid={testId} data-figure-state="Available">{T.figureZero}</span>
  return <ul data-testid={testId} data-figure-state="Available">
    {figure.amounts.map((line) => <li key={line.currencyId}>{amountText(line.amount, line.currencyId, currencies)}</li>)}
  </ul>
}

interface AccountProps {
  readonly companyAccountId: string
  readonly binding: CollateralAttemptBinding
  readonly scope: QueryScope
  readonly candidates: Candidates
  readonly canRecord: boolean
}

function AccountView({ companyAccountId, binding, scope, candidates, canRecord }: AccountProps) {
  const reported = useReporter()
  const [open, setOpen] = useState<{ readonly collateralId: string; readonly mode: 'change' | 'release' } | null>(null)
  const account = useQuery({
    queryKey: [...scope, 'account', companyAccountId],
    gcTime: 0,
    queryFn: ({ signal }) => reported(() => fetchCollateralAccount(binding.companyId, companyAccountId, signal)),
  })
  const history = useQuery({
    queryKey: [...scope, 'history', companyAccountId],
    gcTime: 0,
    queryFn: ({ signal }) => reported(() => fetchCollateralHistory(binding.companyId, companyAccountId, signal)),
  })

  const frame = (body: ReactNode) => (
    <section className={styles.detail} aria-labelledby={ACCOUNT_HEADING_ID} data-testid="finance-collateral-account">
      <h2 id={ACCOUNT_HEADING_ID}>{`${T.heldHeading} — ${shortCollateralReference(companyAccountId)}`}</h2>
      {body}
    </section>
  )

  const current = account.data?.type === 'ok-single' ? account.data.data : null
  if (current === null) {
    const state = account.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(account.data)
    return frame(<StateBlock state={state as RemoteViewState<unknown>} subject={T.accountSubject}
      onRetry={() => void account.refetch()} />)
  }
  const { typeNames, currencyNames } = candidates
  const toggle = (collateralId: string, mode: 'change' | 'release') =>
    setOpen(open?.collateralId === collateralId && open.mode === mode ? null : { collateralId, mode })
  const target = open === null ? null : current.held.find((record) => record.collateralId === open.collateralId) ?? null

  return frame(<>
    <h3>{T.figuresHeading}</h3>
    <dl className={styles.facts}>
      <div>
        <dt>{T.weightedHeading}</dt>
        <dd><FigureValue figure={current.weightedCollateral} currencies={currencyNames} testId="collateral-weighted" /></dd>
      </div>
      <div>
        <dt>{T.documentaryHeading}</dt>
        <dd><FigureValue figure={current.documentaryRisk} currencies={currencyNames} testId="collateral-documentary" /></dd>
      </div>
    </dl>
    <p className={styles.hint}>{T.weightedHint}</p>
    <p className={styles.hint}>{T.documentaryHint}</p>
    {current.held.length === 0
      ? <p className={styles.hint}>{T.heldEmpty}</p>
      : <table>
          <caption>{T.heldCaption}</caption>
          <thead><tr>
            <th scope="col">{T.column.collateral}</th>
            <th scope="col">{T.column.type}</th>
            <th scope="col">{T.column.amount}</th>
            <th scope="col">{T.column.validFrom}</th>
            <th scope="col">{T.column.validUntil}</th>
            <th scope="col">{T.column.documentReference}</th>
            <th scope="col">{T.column.revision}</th>
            {canRecord ? <th scope="col">{T.column.action}</th> : null}
          </tr></thead>
          <tbody>
            {current.held.map((record) => <tr key={record.collateralId} data-collateral={record.collateralId}>
              <td>{shortCollateralReference(record.collateralId)}</td>
              <td>{nameOf(typeNames, record.collateralTypeId)}</td>
              <td>{amountText(record.amount, record.currencyId, currencyNames)}</td>
              <td>{record.validFrom}</td>
              <td>{record.validUntil ?? T.openEnded}</td>
              <td>{record.documentReference ?? T.none}</td>
              <td>{record.revision}</td>
              {canRecord
                ? <td>
                    <Button type="button" variant="ghost" size="sm"
                      aria-expanded={open?.collateralId === record.collateralId && open.mode === 'change'}
                      onClick={() => toggle(record.collateralId, 'change')}>{T.changeOpen}</Button>
                    <Button type="button" variant="ghost" size="sm"
                      aria-expanded={open?.collateralId === record.collateralId && open.mode === 'release'}
                      onClick={() => toggle(record.collateralId, 'release')}>{T.releaseOpen}</Button>
                  </td>
                : null}
            </tr>)}
          </tbody>
        </table>}
    {canRecord && open !== null && target !== null
      ? open.mode === 'change'
        ? <CollateralForm key={`change:${target.id}`} mode="change" binding={binding} scope={scope}
            companyAccountId={companyAccountId} held={target} candidates={candidates} />
        : <ReleaseForm key={`release:${target.id}`} binding={binding} scope={scope} held={target} />
      : null}
    {canRecord
      ? <CollateralForm key="record" mode="record" binding={binding} scope={scope} companyAccountId={companyAccountId}
          held={null} candidates={candidates} defaultPartyId={current.held[0]?.partyId ?? ''} />
      : null}
    <HistoryTable outcome={history.data} typeNames={typeNames} currencyNames={currencyNames}
      onRetry={() => void history.refetch()} />
  </>)
}

function HistoryTable({ outcome, typeNames, currencyNames, onRetry }: {
  readonly outcome: ViewOutcome<{ readonly items: readonly CollateralRecord[] }> | undefined
  readonly typeNames: Names
  readonly currencyNames: Names
  readonly onRetry: () => void
}) {
  // An account with no collateral revision answers 404: that is an empty history, not an error.
  if (outcome?.type === 'http' && outcome.status === 404) return <p className={styles.hint}>{T.historyEmpty}</p>
  const page = outcome?.type === 'ok-single' ? outcome.data : null
  if (page === null) {
    const state = outcome === undefined ? { kind: 'initial-loading' } : classifyOutcome(outcome)
    return <StateBlock state={state as RemoteViewState<unknown>} subject={T.historySubject} onRetry={onRetry} />
  }
  if (page.items.length === 0) return <p className={styles.hint}>{T.historyEmpty}</p>
  return <table>
    <caption>{T.historyCaption}</caption>
    <thead><tr>
      <th scope="col">{T.column.collateral}</th>
      <th scope="col">{T.column.revision}</th>
      <th scope="col">{T.column.kind}</th>
      <th scope="col">{T.column.type}</th>
      <th scope="col">{T.column.amount}</th>
      <th scope="col">{T.column.validFrom}</th>
      <th scope="col">{T.column.validUntil}</th>
      <th scope="col">{T.column.documentReference}</th>
      <th scope="col">{T.column.releaseReason}</th>
      <th scope="col">{T.column.status}</th>
      <th scope="col">{T.column.recordedAt}</th>
    </tr></thead>
    <tbody>
      {page.items.map((record) => <tr key={record.id} data-record={record.id}>
        <td>{shortCollateralReference(record.collateralId)}</td>
        <td>{record.revision}</td>
        <td>{COLLATERAL_KIND[record.kind]}</td>
        <td>{nameOf(typeNames, record.collateralTypeId)}</td>
        <td>{amountText(record.amount, record.currencyId, currencyNames)}</td>
        <td>{record.validFrom}</td>
        <td>{record.validUntil ?? T.openEnded}</td>
        <td>{record.documentReference ?? T.none}</td>
        <td>{record.releaseReason ?? T.none}</td>
        <td>{COLLATERAL_STATUS[record.status]}</td>
        <td><Instant value={record.recordedAtUtc} /></td>
      </tr>)}
    </tbody>
  </table>
}

function AttemptPanel({ attempt, operation, onReload }: {
  readonly attempt: CollateralAttempt
  readonly operation: CollateralOperation
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
        {requiresCheckBeforeAbandon(operation) ? <p className={styles.error}>{T.unknownRecordWarning}</p> : null}
        <div className={styles.actions}>
          <Button type="button" variant="primary" onClick={() => void attempt.resend()}>{T.resend}</Button>
          {requiresCheckBeforeAbandon(operation)
            ? <Button type="button" variant="secondary" onClick={onReload}>{T.reload}</Button>
            : null}
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
          <p>{COLLATERAL_SUCCESS[operation]}</p>
          {replay}
          <Button type="button" variant="ghost" onClick={attempt.reset}>{T.dismiss}</Button>
        </div>
      }
      return <div className={styles.attempt} role="alert" data-attempt={result.outcome}>
        <p className={styles.error}>{T.outcome[result.outcome]}</p>
        {result.issues.length > 0
          ? <ul>{result.issues.map((issue) => <li key={`${issue.field}:${issue.code}`}>{collateralIssueText(issue.code)}</li>)}</ul>
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

function CandidateSelect<I>({ id, label, placeholder, empty, subject, outcome, items, value, onChange, onRetry, error,
  option }: {
  readonly id: string
  readonly label: string
  readonly placeholder: string
  readonly empty: string
  readonly subject: string
  readonly outcome: ViewOutcome<unknown> | undefined
  readonly items: readonly I[]
  readonly value: string
  readonly onChange: (value: string) => void
  readonly onRetry: () => void
  readonly error: string | null
  readonly option: (item: I) => { readonly value: string; readonly label: string }
}) {
  if (outcome?.type !== 'ok-single') {
    const state = outcome === undefined ? { kind: 'initial-loading' } : classifyOutcome(outcome)
    return <StateBlock state={state as RemoteViewState<unknown>} subject={subject} onRetry={onRetry} />
  }
  if (items.length === 0) return <p className={styles.error} role="alert">{empty}</p>
  return <div className={styles.field}>
    <label htmlFor={id}>{label}</label>
    <select id={id} value={value} onChange={(event) => onChange(event.target.value)}
      aria-invalid={error !== null} aria-describedby={error === null ? undefined : `${id}-error`}>
      <option value="">{placeholder}</option>
      {items.map((item) => {
        const shown = option(item)
        return <option key={shown.value} value={shown.value}>{shown.label}</option>
      })}
    </select>
    {error === null ? null : <p id={`${id}-error`} className={styles.error}>{error}</p>}
  </div>
}

interface FormErrors {
  readonly party: string | null
  readonly type: string | null
  readonly currency: string | null
  readonly amount: string | null
  readonly validFrom: string | null
  readonly validUntil: string | null
  readonly documentReference: string | null
}

const NO_ERRORS: FormErrors = {
  party: null, type: null, currency: null, amount: null, validFrom: null, validUntil: null, documentReference: null,
}

/**
 * Record (a new collateral on the account) or change (a new revision of a held one). A changed type must be one of
 * Settings' active candidates; a change that keeps an inactive type is checked by the server.
 */
function CollateralForm({ mode, binding, scope, companyAccountId, held, candidates, defaultPartyId = '' }: {
  readonly mode: 'record' | 'change'
  readonly binding: CollateralAttemptBinding
  readonly scope: QueryScope
  readonly companyAccountId: string
  readonly held: CollateralRecord | null
  readonly candidates: Candidates
  readonly defaultPartyId?: string
}) {
  const target = mode === 'record' ? companyAccountId : held?.collateralId ?? ''
  const attempt = useCollateralAttempt(binding, mode, target, useSettled(scope))
  const reload = useReload(scope)
  const [partyId, setPartyId] = useState(defaultPartyId)
  const [typeId, setTypeId] = useState(held?.collateralTypeId ?? '')
  const [currencyId, setCurrencyId] = useState(held?.currencyId ?? '')
  const [amount, setAmount] = useState(held === null ? '' : amountInputFromWire(held.amount))
  const [validFrom, setValidFrom] = useState(held?.validFrom ?? '')
  const [validUntil, setValidUntil] = useState(held?.validUntil ?? '')
  const [documentReference, setDocumentReference] = useState(held?.documentReference ?? '')
  const [errors, setErrors] = useState<FormErrors>(NO_ERRORS)
  const [blocked, setBlocked] = useState(false)
  const base = `collateral-${mode}`
  const typeItems = candidates.types?.type === 'ok-single' ? candidates.types.data.items : []
  const currencyItems = candidates.currencies?.type === 'ok-single' ? candidates.currencies.data.items : []

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setBlocked(false)
    const party = partyId.trim().toLowerCase()
    const parsed = parseAmountInput(amount)
    const until = validUntil.length === 0 ? null : validUntil
    const reference = documentReference.length === 0 ? null : documentReference
    const next: FormErrors = {
      party: mode === 'record' && !isBoundUuid(party) ? T.identityInvalid : null,
      type: typeId.length === 0 ? T.typeRequired : null,
      currency: currencyId.length === 0 ? T.currencyRequired : null,
      amount: parsed.ok ? null : T.amountError[parsed.error],
      validFrom: isDateOnly(validFrom) ? null : T.validFromRequired,
      validUntil: until !== null && (!isDateOnly(until) || (isDateOnly(validFrom) && until < validFrom))
        ? T.validityInvalid
        : null,
      // The server keeps the reference as given: a padded one is refused, never trimmed silently.
      documentReference: reference !== null && reference !== reference.trim() ? T.documentReferenceInvalid : null,
    }
    setErrors(next)
    if (Object.values(next).some((error) => error !== null) || !parsed.ok) return
    const payload = { collateralTypeId: typeId, currencyId, amount: parsed.wire, validFrom, validUntil: until,
      documentReference: reference }
    const command: CollateralCommand = mode === 'record' || held === null
      ? { operation: 'record', body: { partyId: party, companyAccountId, ...payload } }
      : { operation: 'change', collateralId: held.collateralId, body: { expectedRevision: held.revision, ...payload } }
    if (await attempt.submit(command) === 'blocked') setBlocked(true)
  }

  const described = (field: string, error: string | null, hint?: string) =>
    [hint, error === null ? undefined : `${base}-${field}-error`].filter(Boolean).join(' ') || undefined
  const busy = attempt.state.phase === 'submitting'
  return <form className={styles.form} onSubmit={(event) => void submit(event)} noValidate
    aria-labelledby={`${base}-heading`} data-form={mode}>
    <h3 id={`${base}-heading`}>
      {mode === 'record' ? T.recordHeading : `${T.changeHeading} — ${shortCollateralReference(target)}`}
    </h3>
    {mode === 'record'
      ? <div className={styles.field}>
          <label htmlFor={`${base}-party`}>{T.partyId}</label>
          <input id={`${base}-party`} type="text" autoComplete="off" value={partyId}
            onChange={(event) => setPartyId(event.target.value)} aria-invalid={errors.party !== null}
            aria-describedby={described('party', errors.party)} />
          {errors.party === null ? null : <p id={`${base}-party-error`} className={styles.error}>{errors.party}</p>}
        </div>
      : null}
    <CandidateSelect id={`${base}-type`} label={T.type} placeholder={T.typePlaceholder} empty={T.noTypes}
      subject={T.typeSubject} outcome={candidates.types} items={typeItems} value={typeId} onChange={setTypeId}
      onRetry={candidates.retryTypes} error={errors.type}
      option={(item) => ({ value: item.collateralTypeId, label: T.typeOption(item.code, item.name,
        formatDecimalTextTr(item.ratePercent) ?? item.ratePercent) })} />
    <CandidateSelect id={`${base}-currency`} label={T.currency} placeholder={T.currencyPlaceholder}
      empty={T.noCurrencies} subject={T.currencySubject} outcome={candidates.currencies} items={currencyItems}
      value={currencyId} onChange={setCurrencyId} onRetry={candidates.retryCurrencies} error={errors.currency}
      option={(item) => ({ value: item.currencyId, label: `${item.code} — ${item.displayLabel}` })} />
    <div className={styles.field}>
      <label htmlFor={`${base}-amount`}>{T.amount}</label>
      <input id={`${base}-amount`} type="text" inputMode="decimal" autoComplete="off" value={amount}
        onChange={(event) => setAmount(event.target.value)} aria-invalid={errors.amount !== null}
        aria-describedby={described('amount', errors.amount, `${base}-amount-hint`)} />
      <p id={`${base}-amount-hint`} className={styles.hint}>{T.amountHint}</p>
      {errors.amount === null ? null : <p id={`${base}-amount-error`} className={styles.error}>{errors.amount}</p>}
    </div>
    <div className={styles.field}>
      <label htmlFor={`${base}-valid-from`}>{T.validFrom}</label>
      <input id={`${base}-valid-from`} type="date" value={validFrom} onChange={(event) => setValidFrom(event.target.value)}
        aria-invalid={errors.validFrom !== null} aria-describedby={described('valid-from', errors.validFrom)} />
      {errors.validFrom === null ? null : <p id={`${base}-valid-from-error`} className={styles.error}>{errors.validFrom}</p>}
    </div>
    <div className={styles.field}>
      <label htmlFor={`${base}-valid-until`}>{T.validUntil}</label>
      <input id={`${base}-valid-until`} type="date" value={validUntil}
        onChange={(event) => setValidUntil(event.target.value)} aria-invalid={errors.validUntil !== null}
        aria-describedby={described('valid-until', errors.validUntil)} />
      {errors.validUntil === null
        ? null
        : <p id={`${base}-valid-until-error`} className={styles.error}>{errors.validUntil}</p>}
    </div>
    <div className={styles.field}>
      <label htmlFor={`${base}-document`}>{T.documentReference}</label>
      <input id={`${base}-document`} type="text" autoComplete="off" value={documentReference}
        onChange={(event) => setDocumentReference(event.target.value)} aria-invalid={errors.documentReference !== null}
        aria-describedby={described('document', errors.documentReference)} />
      {errors.documentReference === null
        ? null
        : <p id={`${base}-document-error`} className={styles.error}>{errors.documentReference}</p>}
    </div>
    <div className={styles.actions}>
      <Button type="submit" variant="primary" disabled={busy} aria-busy={busy}>
        {mode === 'record' ? T.recordSubmit : T.changeSubmit}
      </Button>
    </div>
    {blocked ? <p className={styles.error} role="alert">{T.blocked}</p> : null}
    <AttemptPanel attempt={attempt} operation={mode} onReload={reload} />
  </form>
}

function ReleaseForm({ binding, scope, held }: {
  readonly binding: CollateralAttemptBinding
  readonly scope: QueryScope
  readonly held: CollateralRecord
}) {
  const attempt = useCollateralAttempt(binding, 'release', held.collateralId, useSettled(scope))
  const reload = useReload(scope)
  const [reason, setReason] = useState('')
  const [reasonError, setReasonError] = useState<string | null>(null)
  const [blocked, setBlocked] = useState(false)
  const busy = attempt.state.phase === 'submitting'
  const base = 'collateral-release'

  const release = async (event: FormEvent) => {
    event.preventDefault()
    setBlocked(false)
    const trimmed = reason.trim()
    setReasonError(trimmed.length === 0 ? T.releaseReasonRequired : null)
    if (trimmed.length === 0) return
    const result = await attempt.submit({ operation: 'release', collateralId: held.collateralId,
      body: { expectedRevision: held.revision, reason: trimmed } })
    if (result === 'blocked') setBlocked(true)
  }

  return <form className={styles.form} onSubmit={(event) => void release(event)} noValidate
    aria-labelledby={`${base}-heading`} data-form="release">
    <h3 id={`${base}-heading`}>{`${T.releaseHeading} — ${shortCollateralReference(held.collateralId)}`}</h3>
    <p className={styles.hint}>{T.releaseHint}</p>
    <div className={styles.field}>
      <label htmlFor={`${base}-reason`}>{T.releaseReason}</label>
      <textarea id={`${base}-reason`} value={reason} onChange={(event) => setReason(event.target.value)}
        aria-invalid={reasonError !== null} aria-describedby={reasonError === null ? undefined : `${base}-reason-error`} />
      {reasonError === null ? null : <p id={`${base}-reason-error`} className={styles.error}>{reasonError}</p>}
    </div>
    <div className={styles.actions}>
      <Button type="submit" variant="danger" disabled={busy} aria-busy={busy}>{T.releaseSubmit}</Button>
    </div>
    {blocked ? <p className={styles.error} role="alert">{T.blocked}</p> : null}
    <AttemptPanel attempt={attempt} operation="release" onReload={reload} />
  </form>
}
