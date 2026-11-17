/**
 * F-32 — Finans → Virman ve döviz bozdurma (FNS-D24, API-32/34): money moved between two treasury accounts of the
 * company, or exchanged between two currencies at one rate, recorded as ONE transfer carrying both legs.
 *
 * The screen shows the server's decisions only: account eligibility, each leg's currency and money profile, the
 * exchange quote and separate actual TCMB comparison, the period and the source balance (FNS-D11: refused,
 * never clipped) are all answered by the backend. Nothing about money is worked out here: a same-currency transfer
 * sends one typed amount on both legs; an exchange sends both actual amounts with a null rate for server calculation.
 *
 * The flow is preview → explicit confirmation → commit. A commit whose answer could not be read keeps its idempotency
 * key and exact body in memory: the inputs lock and only "Aynı isteği yeniden gönder" is offered, so a retry can never
 * move the money twice. A company, actor or session change remounts the screen and nothing of the previous context
 * is drawn.
 */

import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState, type ViewOutcome } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import { DataTable, type DataTableColumn } from '../ui/DataTable'
import { Field } from '../ui/Field'
import {
  CASH_TRANSFER_ACCOUNT_KINDS,
  CASH_TRANSFER_DESCRIPTION_MAX_LENGTH,
  CASH_TRANSFER_KINDS,
  CASH_TRANSFER_REFERENCE_MAX_LENGTH,
  type CashTransferAccountContext,
  type CashTransferAccountKind,
  type CashTransferExchangeSnapshot,
  type CashTransferKind,
  type CashTransferPreparation,
  type CashTransferView,
  type CommitCashTransferRequest,
} from './cashTransferContract'
import {
  commitCashTransfer,
  fetchBankAccountMaster,
  fetchCashAccounts,
  fetchCashTransfer,
  fetchCashTransferAccountContext,
  fetchCashTransfers,
  forgetPendingCashTransferAttempt,
  generateCashTransferAttemptKey,
  pendingCashTransferAttemptKey,
  previewCashTransfer,
  recallPendingCashTransferAttempt,
  rememberPendingCashTransferAttempt,
  samePreparation,
  type CashTransferCommandOutcome,
  type CashTransferPreviewOutcome,
  type PendingCashTransferAttempt,
  type UnknownReason,
} from './cashTransferHttp'
import {
  ACCOUNT_KIND_LABEL,
  CASH_TRANSFER_TEXT as T,
  issueMessage,
  TRANSFER_KIND_LABEL,
} from './cashTransferText'
import { currencyShown, WithCurrencyCodes, type CurrencyCodes } from './currencyCodes'
import { isDateOnly } from './dateOnly'
import { formatDecimalTextTr, parseAmountInput } from './decimalText'
import styles from './Finance.module.css'

const HEADING_ID = 'finance-cash-transfers-heading'

export interface CashTransferPageProps {
  readonly companyId: string
  readonly actorId: string
  readonly canCommit: boolean
}

interface Binding {
  readonly namespace: string
  readonly generation: number
  readonly sessionEpoch: number
}

/** The screen is keyed on the context and the route entry: a switch drops every row, form and late answer. */
export function CashTransferPage(props: CashTransferPageProps) {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const frame = (body: ReactNode) => (
    <section aria-labelledby={HEADING_ID} data-testid="finance-cash-transfers">
      <h1 id={HEADING_ID}>{T.heading}</h1>
      {body}
    </section>
  )
  if (namespace === null) return frame(<StateBlock state={{ kind: 'no-authorized-company' }} subject={T.subject} />)
  const binding: Binding = { namespace, generation: shell.routeEntryGeneration, sessionEpoch: shell.sessionEpoch }
  return frame(
    <Workspace key={`${namespace}::${shell.routeEntryGeneration}::${shell.sessionEpoch}::${props.companyId}::${props.actorId}`}
      {...props} binding={binding} />,
  )
}

/** Runs a read with the auth boundary: a 401/403 is reported to the shell, never handled here. */
function useBoundRead() {
  const boundary = useAuthBoundary()
  return useCallback(async <D,>(read: () => Promise<ViewOutcome<D>>): Promise<ViewOutcome<D>> => {
    const snapshot = boundary.capture()
    const outcome = await read()
    if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) boundary.report(outcome.status, snapshot)
    return outcome
  }, [boundary])
}

const stateOf = <D,>(data: ViewOutcome<D> | undefined): RemoteViewState<unknown> =>
  data === undefined ? { kind: 'initial-loading' } : classifyOutcome(data) as RemoteViewState<unknown>

/** Presentation only: the wire's `yyyy-MM-dd` in Turkish order. */
function formatDate(value: string): string {
  const parts = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value)
  return parts === null ? value : `${parts[3]}.${parts[2]}.${parts[1]}`
}

function Instant({ value }: { readonly value: string }) {
  const parsed = new Date(value)
  return <time dateTime={value}>
    {Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString('tr-TR', { dateStyle: 'medium', timeStyle: 'short' })}
  </time>
}

const amountText = (wire: string) => formatDecimalTextTr(wire) ?? wire

/** Account id → display name from the account reads; `null` while they are not read. */
type AccountNames = ReadonlyMap<string, string> | null

const accountShown = (kind: CashTransferAccountKind, id: string, names: AccountNames) =>
  names?.get(`${kind}:${id}`) ?? T.accountShown(ACCOUNT_KIND_LABEL[kind], id)

interface WorkspaceProps extends CashTransferPageProps {
  readonly binding: Binding
}

function Workspace(props: WorkspaceProps) {
  const { companyId, canCommit, binding } = props
  const [openId, setOpenId] = useState<string | null>(null)
  const [composing, setComposing] = useState(false)
  const client = useQueryClient()
  const { namespace, generation } = binding
  const listKey = useMemo(() => [namespace, generation, 'finance-cash-transfers', companyId] as const,
    [namespace, generation, companyId])
  const bank = useAccounts(companyId, binding, 'Bank')
  const cash = useAccounts(companyId, binding, 'Cash', true)
  const names = useMemo<AccountNames>(() => {
    const map = new Map<string, string>()
    let any = false
    for (const [kind, query] of [['Bank', bank], ['Cash', cash]] as const) {
      if (query.data?.type !== 'ok-single') continue
      any = true
      for (const account of query.data.data.all) map.set(`${kind}:${account.id}`, account.displayName)
    }
    return any ? map : null
  }, [bank, cash])

  const refreshList = useCallback(() => {
    void client.invalidateQueries({ queryKey: listKey })
  }, [client, listKey])

  const committed = useCallback((transfer: CashTransferView) => {
    setComposing(false)
    setOpenId(transfer.id)
    refreshList()
  }, [refreshList])

  return <WithCurrencyCodes>{(codes) => <>
    <p>{T.intro}</p>
    {canCommit && !composing
      ? <div className={styles.actions}>
          <Button type="button" variant="primary" onClick={() => setComposing(true)}>{T.newTransfer}</Button>
        </div>
      : null}
    {canCommit && composing
      ? <TransferForm {...props} codes={codes} onCancel={() => setComposing(false)} onCommitted={committed} />
      : null}
    <TransferList {...props} listKey={listKey} codes={codes} names={names} onOpen={setOpenId} />
    {openId === null ? null : (
      <TransferDetail key={openId} {...props} transferId={openId} codes={codes} names={names}
        onClose={() => setOpenId(null)} />
    )}
  </>}</WithCurrencyCodes>
}

// ---------------------------------------------------------------------------------------------------------------
// Accounts
// ---------------------------------------------------------------------------------------------------------------

interface AccountOption {
  readonly id: string
  readonly displayName: string
  readonly currencyId: string
}

interface AccountLists {
  /** Every account the read returned, for showing names on stored transfers. */
  readonly all: readonly AccountOption[]
  /** Only what the server would also take: an approved, active account; for a bank, one declared for money. */
  readonly eligible: readonly AccountOption[]
}

/** A single-answer read mapped to another shape; every other outcome passes through, a list answer is malformed. */
function mapSingle<A, B>(outcome: ViewOutcome<A>, map: (data: A) => B): ViewOutcome<B> {
  if (outcome.type === 'ok-single') return { type: 'ok-single', data: map(outcome.data) }
  if (outcome.type === 'ok') return { type: 'malformed' }
  return outcome
}

function useAccounts(companyId: string, binding: Binding, kind: CashTransferAccountKind | '', enabled = true) {
  const boundRead = useBoundRead()
  return useQuery({
    queryKey: [binding.namespace, binding.generation, 'finance-cash-transfer-accounts', companyId, kind],
    enabled: enabled && kind !== '',
    gcTime: 0,
    queryFn: async ({ signal }): Promise<ViewOutcome<AccountLists>> => {
      if (kind === 'Bank') {
        return mapSingle(await boundRead(() => fetchBankAccountMaster(companyId, signal)), (page) => ({
          all: page.items.map((item) => ({
            id: item.account.id, displayName: item.account.displayName, currencyId: item.account.currencyId,
          })),
          eligible: page.items
            .filter((item) => item.account.isActive && item.card.moneyEligibility === 'Eligible' && item.card.iban !== null)
            .map((item) => ({ id: item.account.id, displayName: item.account.displayName, currencyId: item.account.currencyId })),
        }))
      }
      return mapSingle(await boundRead(() => fetchCashAccounts(companyId, signal)), (list) => ({
        all: list.items.map((item) => ({ id: item.id, displayName: item.displayName, currencyId: item.currencyId })),
        eligible: list.items
          .filter((item) => item.status === 'Approved' && item.isActive)
          .map((item) => ({ id: item.id, displayName: item.displayName, currencyId: item.currencyId })),
      }))
    },
  })
}

// ---------------------------------------------------------------------------------------------------------------
// List
// ---------------------------------------------------------------------------------------------------------------

interface ListProps extends WorkspaceProps {
  readonly listKey: readonly unknown[]
  readonly codes: CurrencyCodes
  readonly names: AccountNames
  onOpen(transferId: string): void
}

const NO_ROWS: readonly CashTransferView[] = []

function TransferList({ companyId, listKey, codes, names, onOpen }: ListProps) {
  const boundRead = useBoundRead()
  const [more, setMore] = useState<readonly CashTransferView[]>(NO_ROWS)
  const [cursor, setCursor] = useState<string | null | undefined>(undefined)
  const [continuation, setContinuation] = useState<'idle' | 'loading' | RemoteViewState<unknown>>('idle')
  const loading = useRef(false)
  const inFlight = useRef<AbortController | null>(null)
  const dropInFlight = useCallback(() => {
    inFlight.current?.abort()
    inFlight.current = null
    loading.current = false
  }, [])
  useEffect(() => dropInFlight, [dropInFlight])

  const first = useQuery({
    queryKey: listKey,
    gcTime: 0,
    queryFn: ({ signal }) => boundRead(() => fetchCashTransfers(companyId, null, signal)),
  })
  const page = first.data?.type === 'ok-single' ? first.data.data : null

  // Later pages continue ONE first-page answer: a new answer drops them before it is drawn.
  const answeredAt = first.dataUpdatedAt
  const [continues, setContinues] = useState(answeredAt)
  if (continues !== answeredAt) {
    setContinues(answeredAt)
    setMore(NO_ROWS)
    setCursor(undefined)
    setContinuation('idle')
  }
  useEffect(() => dropInFlight, [answeredAt, dropInFlight])
  const current = continues === answeredAt
  const shownMore = current ? more : NO_ROWS
  const nextCursor = !current || cursor === undefined ? page?.nextCursor ?? null : cursor

  const loadMore = useCallback(async () => {
    if (loading.current || page === null || nextCursor === null) return
    loading.current = true
    setContinuation('loading')
    const controller = new AbortController()
    inFlight.current = controller
    const outcome = await boundRead(() => fetchCashTransfers(companyId, nextCursor, controller.signal))
    if (inFlight.current !== controller || controller.signal.aborted) return
    inFlight.current = null
    loading.current = false
    if (outcome.type !== 'ok-single') {
      setContinuation(classifyOutcome(outcome) as RemoteViewState<unknown>)
      return
    }
    setMore((existing) => {
      const seen = new Set([...page.items, ...existing].map((item) => item.id))
      return [...existing, ...outcome.data.items.filter((item) => !seen.has(item.id))]
    })
    setCursor(outcome.data.nextCursor)
    setContinuation('idle')
  }, [boundRead, companyId, nextCursor, page])

  if (page === null) {
    return <StateBlock state={stateOf(first.data)} subject={T.subject} onRetry={() => void first.refetch()} />
  }
  const rows = [...page.items, ...shownMore]
  if (rows.length === 0 && nextCursor === null) return <StateBlock state={{ kind: 'empty-no-data' }} subject={T.subject} />

  const columns: readonly DataTableColumn<CashTransferView>[] = [
    { key: 'occurredOn', header: T.columnOccurredOn, cell: (row) => formatDate(row.preparation.occurredOn) },
    { key: 'kind', header: T.columnKind, cell: (row) => TRANSFER_KIND_LABEL[row.preparation.kind] },
    {
      key: 'source',
      header: T.columnSource,
      cell: (row) => accountShown(row.preparation.sourceKind, row.preparation.sourceAccountId, names),
    },
    {
      key: 'sourceAmount',
      header: T.columnSourceAmount,
      align: 'end',
      cell: (row) => `${amountText(row.preparation.sourceMoney.amount)} ${currencyShown(row.preparation.sourceMoney.currencyId, codes)}`,
    },
    {
      key: 'target',
      header: T.columnTarget,
      cell: (row) => accountShown(row.preparation.targetKind, row.preparation.targetAccountId, names),
    },
    {
      key: 'targetAmount',
      header: T.columnTargetAmount,
      align: 'end',
      cell: (row) => `${amountText(row.preparation.targetMoney.amount)} ${currencyShown(row.preparation.targetMoney.currencyId, codes)}`,
    },
    {
      key: 'action',
      header: T.columnAction,
      cell: (row) => <Button type="button" size="sm" variant="ghost" onClick={() => onOpen(row.id)}
        aria-label={T.openName(formatDate(row.preparation.occurredOn), TRANSFER_KIND_LABEL[row.preparation.kind])}>
        {T.open}
      </Button>,
    },
  ]

  return <div className={styles.list}>
    <DataTable label={T.listCaption} columns={columns} rows={rows} rowKey={(row) => row.id}
      rowAttributes={(row) => ({ 'data-cash-transfer-id': row.id })} />
    {nextCursor !== null ? (
      <div className={styles.actions}>
        {typeof continuation === 'object'
          ? <StateBlock state={continuation} subject={T.nextPageSubject} onRetry={() => void loadMore()} />
          : <Button type="button" variant="secondary" onClick={() => void loadMore()}
              disabled={continuation === 'loading'} aria-busy={continuation === 'loading'}>
              {continuation === 'loading' ? T.loadingMore : T.loadMore}
            </Button>}
      </div>
    ) : null}
  </div>
}

// ---------------------------------------------------------------------------------------------------------------
// Detail
// ---------------------------------------------------------------------------------------------------------------

interface DetailProps extends WorkspaceProps {
  readonly transferId: string
  readonly codes: CurrencyCodes
  readonly names: AccountNames
  onClose(): void
}

function TransferDetail({ companyId, transferId, binding, codes, names, onClose }: DetailProps) {
  const boundRead = useBoundRead()
  const read = useQuery({
    queryKey: [binding.namespace, binding.generation, 'finance-cash-transfer', companyId, transferId],
    gcTime: 0,
    queryFn: ({ signal }) => boundRead(() => fetchCashTransfer(companyId, transferId, signal)),
  })
  const transfer = read.data?.type === 'ok-single' ? read.data.data : null

  return <section className={styles.detail} aria-label={T.detailHeading} data-testid="finance-cash-transfer-detail">
    <h2>{T.detailHeading}</h2>
    {transfer === null
      ? <StateBlock state={stateOf(read.data)} subject={T.detailSubject} onRetry={() => void read.refetch()} />
      : <TransferFacts transfer={transfer} codes={codes} names={names} />}
    <div className={styles.actions}>
      <Button type="button" variant="ghost" onClick={onClose}>{T.close}</Button>
    </div>
  </section>
}

function TransferFacts({ transfer, codes, names }: {
  readonly transfer: CashTransferView
  readonly codes: CurrencyCodes
  readonly names: AccountNames
}) {
  const { preparation } = transfer
  return <dl className={styles.facts}>
    <div><dt>{T.columnKind}</dt><dd>{TRANSFER_KIND_LABEL[preparation.kind]}</dd></div>
    <div><dt>{T.columnSource}</dt><dd>{accountShown(preparation.sourceKind, preparation.sourceAccountId, names)}</dd></div>
    <div><dt>{T.columnSourceAmount}</dt>
      <dd>{amountText(preparation.sourceMoney.amount)} {currencyShown(preparation.sourceMoney.currencyId, codes)}</dd></div>
    <div><dt>{T.columnTarget}</dt><dd>{accountShown(preparation.targetKind, preparation.targetAccountId, names)}</dd></div>
    <div><dt>{T.columnTargetAmount}</dt>
      <dd>{amountText(preparation.targetMoney.amount)} {currencyShown(preparation.targetMoney.currencyId, codes)}</dd></div>
    <div><dt>{T.rate}</dt><dd>{preparation.rate === null ? T.noValue : amountText(preparation.rate)}</dd></div>
    <div><dt>{T.occurredOn}</dt><dd>{formatDate(preparation.occurredOn)}</dd></div>
    <div><dt>{T.valueOnShort}</dt><dd>{preparation.valueOn === null ? T.noValue : formatDate(preparation.valueOn)}</dd></div>
    <div><dt>{T.descriptionShort}</dt><dd>{preparation.description ?? T.noValue}</dd></div>
    <div><dt>{T.referenceShort}</dt><dd>{preparation.reference ?? T.noValue}</dd></div>
    <div><dt>{T.recordedBy}</dt><dd>{transfer.recordedBy}</dd></div>
    <div><dt>{T.recordedAt}</dt><dd><Instant value={transfer.recordedAtUtc} /></dd></div>
    {transfer.exchange === undefined ? null : <ExchangeFacts exchange={transfer.exchange} />}
  </dl>
}

function ExchangeFacts({ exchange }: { readonly exchange: CashTransferExchangeSnapshot }) {
  return <>
    <div><dt>{T.rate}</dt><dd>{amountText(exchange.transactionRate)}</dd></div>
    <div><dt>{T.comparisonRate}</dt><dd>{amountText(exchange.comparisonRate)}</dd></div>
    <div><dt>{T.rateDeviation}</dt><dd>{amountText(exchange.rateDeviation)}</dd></div>
    <div><dt>{T.comparisonSourceDay}</dt><dd>{[exchange.sourceComparison, exchange.targetComparison]
      .filter((source) => source.source === 'Tcmb').map((source) => source.bulletinDate === null
        ? T.noValue : formatDate(source.bulletinDate)).join(' / ')}</dd></div>
    <div><dt>{T.comparisonMeaning}</dt><dd>{T.comparisonIsNotProfit}</dd></div>
  </>
}

// ---------------------------------------------------------------------------------------------------------------
// Commit
// ---------------------------------------------------------------------------------------------------------------

type FailedOutcome = Extract<CashTransferCommandOutcome, { kind: 'failed' }>

type AttemptState =
  | { readonly phase: 'idle' }
  | { readonly phase: 'sending' }
  | { readonly phase: 'applied'; readonly resource: CashTransferView; readonly replayed: boolean }
  | { readonly phase: 'failed'; readonly failure: FailedOutcome }
  | { readonly phase: 'unknown'; readonly reason: UnknownReason | null; readonly attempt: PendingCashTransferAttempt }

/**
 * The commit's attempt. The key is remembered BEFORE the request leaves and forgotten only on a decided answer; an
 * unknown answer keeps it, and the next send resends that exact attempt rather than the new body.
 */
function useCommitCommand(companyId: string, storeKey: string, onApplied: (resource: CashTransferView) => void) {
  const boundary = useAuthBoundary()
  const [state, setState] = useState<AttemptState>(() => {
    const pending = recallPendingCashTransferAttempt(storeKey)
    return pending === null ? { phase: 'idle' } : { phase: 'unknown', reason: null, attempt: pending }
  })
  const busy = useRef(false)

  const send = useCallback(async (body: CommitCashTransferRequest) => {
    if (busy.current) return
    busy.current = true
    const attempt = recallPendingCashTransferAttempt(storeKey) ?? { key: generateCashTransferAttemptKey(), body }
    rememberPendingCashTransferAttempt(storeKey, attempt)
    setState({ phase: 'sending' })
    const snapshot = boundary.capture()
    const outcome = await commitCashTransfer(companyId, attempt.key, attempt.body)
    busy.current = false
    if (outcome.kind === 'unknown') {
      setState({ phase: 'unknown', reason: outcome.reason, attempt })
      return
    }
    forgetPendingCashTransferAttempt(storeKey)
    if (outcome.kind === 'applied') {
      setState({ phase: 'applied', resource: outcome.resource, replayed: outcome.replayed })
      onApplied(outcome.resource)
      return
    }
    if (outcome.status === 401 || outcome.status === 403) boundary.report(outcome.status, snapshot)
    setState({ phase: 'failed', failure: outcome })
  }, [boundary, companyId, onApplied, storeKey])

  const retry = useCallback(() => {
    if (state.phase === 'unknown') void send(state.attempt.body)
  }, [send, state])

  return { state, send, retry, locked: state.phase === 'sending' || state.phase === 'unknown' }
}

function statusText(status: 400 | 401 | 403 | 404 | 409 | 415 | 422, outcome: string | null): string {
  switch (status) {
    case 400:
    case 415:
      return T.badRequest
    case 401:
      return T.sessionEnded
    case 403:
      return T.forbiddenCommand
    case 404:
      return T.notFound
    default:
      return outcome === 'VersionConflict' || outcome === 'IdempotencyConflict' ? T.conflict : T.failedHeading
  }
}

function IssueList({ issues }: { readonly issues: readonly { readonly field: string; readonly code: string }[] }) {
  return issues.length === 0
    ? null
    : <ul>{issues.map((issue) => <li key={`${issue.field}:${issue.code}`}>{issueMessage(issue.code)}</li>)}</ul>
}

/** The server's answer to the last commit attempt; the issue codes are shown as the server gave them, in Turkish. */
function AttemptPanel({ state, onRetry }: { readonly state: AttemptState; readonly onRetry: () => void }) {
  if (state.phase === 'idle' || state.phase === 'sending') return null
  if (state.phase === 'applied') {
    return <p className={styles.notice} role="status">{state.replayed ? T.appliedReplay : T.applied}</p>
  }
  if (state.phase === 'unknown') {
    return <div className={styles.attempt} role="alert">
      <p className={styles.attemptTitle}>{T.unknownHeading}</p>
      <p>{state.reason === null ? T.previousUnknown : T.unknownBody}</p>
      <Button type="button" variant="primary" onClick={onRetry}>{T.retrySame}</Button>
    </div>
  }
  const { failure } = state
  return <div className={styles.attempt} role="alert">
    <p className={styles.attemptTitle}>{statusText(failure.status, failure.outcome)}</p>
    <IssueList issues={failure.issues} />
  </div>
}

type PreviewState =
  | { readonly phase: 'idle' }
  | { readonly phase: 'checking' }
  | { readonly phase: 'done'; readonly outcome: CashTransferPreviewOutcome; readonly sent: CashTransferPreparation }

function PreviewPanel({ state, current }: {
  readonly state: PreviewState
  readonly current: CashTransferPreparation | null
}) {
  if (state.phase !== 'done') return null
  const { outcome } = state
  if (outcome.kind === 'accepted') {
    return current !== null && samePreparation(state.sent, current)
      ? <><p className={styles.notice} role="status">{T.previewAccepted}</p>
          {outcome.exchange === undefined ? null : <dl className={styles.facts}><ExchangeFacts exchange={outcome.exchange} /></dl>}</>
      : <p className={styles.hint} role="status">{T.previewStale}</p>
  }
  if (outcome.kind === 'unknown') return <p className={styles.error} role="alert">{T.previewUnknown}</p>
  return <div className={styles.attempt} role="alert">
    <p className={styles.attemptTitle}>{outcome.status === 422 ? T.previewRefused : statusText(outcome.status, null)}</p>
    <IssueList issues={outcome.issues} />
  </div>
}

// ---------------------------------------------------------------------------------------------------------------
// Form
// ---------------------------------------------------------------------------------------------------------------

interface FormProps extends WorkspaceProps {
  readonly codes: CurrencyCodes
  onCancel(): void
  onCommitted(transfer: CashTransferView): void
}

interface FieldErrors {
  readonly sourceAmount?: string
  readonly targetAmount?: string
  readonly rate?: string
  readonly description?: string
  readonly reference?: string
  readonly occurredOn?: string
  readonly valueOn?: string
  readonly targetAccount?: string
}

/** The account read and the context read for one leg; the context is read only once account and date are chosen. */
function useLeg(companyId: string, binding: Binding, kind: CashTransferAccountKind | '', accountId: string,
  occurredOn: string) {
  const boundRead = useBoundRead()
  const accounts = useAccounts(companyId, binding, kind)
  const ready = kind !== '' && accountId !== '' && isDateOnly(occurredOn)
  const context = useQuery({
    queryKey: [binding.namespace, binding.generation, 'finance-cash-transfer-account-context', companyId, kind, accountId,
      occurredOn],
    enabled: ready,
    gcTime: 0,
    queryFn: ({ signal }) => boundRead(() => fetchCashTransferAccountContext(companyId, {
      accountKind: kind as CashTransferAccountKind,
      accountId,
      occurredOn,
    }, signal)),
  })
  return {
    accounts,
    list: accounts.data?.type === 'ok-single' ? accounts.data.data.eligible : null,
    ready,
    context,
    value: ready && context.data?.type === 'ok-single' ? context.data.data : null,
  }
}

type Leg = ReturnType<typeof useLeg>

function LegFields({ heading, kind, accountId, leg, codes, error, onKind, onAccount }: {
  readonly heading: string
  readonly kind: CashTransferAccountKind | ''
  readonly accountId: string
  readonly leg: Leg
  readonly codes: CurrencyCodes
  readonly error?: string
  onKind(kind: CashTransferAccountKind | ''): void
  onAccount(accountId: string): void
}) {
  return <fieldset className={styles.form}>
    <legend>{heading}</legend>
    <Field label={T.accountKind}>
      {(control) => <select {...control} value={kind}
        onChange={(event) => onKind(event.target.value as CashTransferAccountKind | '')}>
        <option value="">{T.accountKindChoose}</option>
        {CASH_TRANSFER_ACCOUNT_KINDS.map((option) => <option key={option} value={option}>{ACCOUNT_KIND_LABEL[option]}</option>)}
      </select>}
    </Field>
    {kind === '' ? null : leg.list === null
      ? <StateBlock state={stateOf(leg.accounts.data)} subject={T.accountsSubject} onRetry={() => void leg.accounts.refetch()} />
      : leg.list.length === 0
        ? <p className={styles.hint}>{T.noEligibleAccount}</p>
        : <Field label={T.account} error={error}>
            {(control) => <select {...control} value={accountId} onChange={(event) => onAccount(event.target.value)}>
              <option value="">{T.accountChoose}</option>
              {leg.list?.map((account) => <option key={account.id} value={account.id}>
                {T.accountOption(account.displayName, currencyShown(account.currencyId, codes))}
              </option>)}
            </select>}
          </Field>}
    {!leg.ready
      ? <p className={styles.hint}>{T.contextWaiting}</p>
      : leg.value === null
        ? <StateBlock state={stateOf(leg.context.data)} subject={T.contextSubject} onRetry={() => void leg.context.refetch()} />
        : <dl className={styles.facts}>
            <div><dt>{T.currency}</dt><dd>{currencyShown(leg.value.currencyId, codes)}</dd></div>
          </dl>}
  </fieldset>
}

interface Built {
  readonly preparation: CashTransferPreparation | null
  readonly errors: FieldErrors
}

function build(input: {
  readonly kind: CashTransferKind | ''
  readonly sourceKind: CashTransferAccountKind | ''
  readonly sourceAccountId: string
  readonly source: CashTransferAccountContext | null
  readonly targetKind: CashTransferAccountKind | ''
  readonly targetAccountId: string
  readonly target: CashTransferAccountContext | null
  readonly sourceAmount: string
  readonly targetAmount: string
  readonly occurredOn: string
  readonly valueOn: string
  readonly reference: string
  readonly description: string
}): Built {
  const exchange = input.kind === 'CurrencyExchange'
  const source = parseAmountInput(input.sourceAmount)
  const target = exchange ? parseAmountInput(input.targetAmount) : source
  const description = input.description.trim()
  const reference = input.reference.trim()
  const sameAccount = input.sourceKind !== '' && input.sourceKind === input.targetKind
    && input.sourceAccountId !== '' && input.sourceAccountId === input.targetAccountId
  const errors: FieldErrors = {
    ...(source.ok ? {} : { sourceAmount: T.invalidAmount[source.error] }),
    ...(target.ok || !exchange ? {} : { targetAmount: T.invalidAmount[target.error] }),
    ...(description.length > CASH_TRANSFER_DESCRIPTION_MAX_LENGTH
      ? { description: T.descriptionTooLong(CASH_TRANSFER_DESCRIPTION_MAX_LENGTH) }
      : {}),
    ...(reference.length > CASH_TRANSFER_REFERENCE_MAX_LENGTH
      ? { reference: T.referenceTooLong(CASH_TRANSFER_REFERENCE_MAX_LENGTH) }
      : {}),
    ...(isDateOnly(input.occurredOn) ? {} : { occurredOn: T.dateRequired }),
    ...(input.valueOn !== '' && isDateOnly(input.occurredOn) && input.valueOn < input.occurredOn
      ? { valueOn: T.valueOnBeforeOccurred }
      : {}),
    ...(sameAccount ? { targetAccount: T.sameAccount } : {}),
  }
  if (Object.keys(errors).length > 0 || input.kind === '' || input.sourceKind === '' || input.targetKind === ''
    || input.source === null || input.target === null || !source.ok || !target.ok) {
    return { preparation: null, errors }
  }
  return {
    errors,
    preparation: {
      kind: input.kind,
      sourceKind: input.sourceKind,
      sourceAccountId: input.sourceAccountId,
      sourceMoney: { amount: source.wire, currencyId: input.source.currencyId },
      sourceProfile: input.source.profile,
      targetKind: input.targetKind,
      targetAccountId: input.targetAccountId,
      targetMoney: { amount: target.wire, currencyId: input.target.currencyId },
      targetProfile: input.target.profile,
      rate: null,
      occurredOn: input.occurredOn,
      valueOn: input.valueOn === '' ? null : input.valueOn,
      reference: reference === '' ? null : reference,
      description: description === '' ? null : description,
    },
  }
}

function TransferForm(props: FormProps) {
  const { companyId, actorId, binding, codes, onCancel, onCommitted } = props
  const boundary = useAuthBoundary()
  const [kind, setKind] = useState<CashTransferKind | ''>('')
  const [sourceKind, setSourceKind] = useState<CashTransferAccountKind | ''>('')
  const [sourceAccountId, setSourceAccountId] = useState('')
  const [targetKind, setTargetKind] = useState<CashTransferAccountKind | ''>('')
  const [targetAccountId, setTargetAccountId] = useState('')
  const [occurredOn, setOccurredOn] = useState('')
  const [valueOn, setValueOn] = useState('')
  const [sourceAmount, setSourceAmount] = useState('')
  const [targetAmount, setTargetAmount] = useState('')
  const [reference, setReference] = useState('')
  const [description, setDescription] = useState('')
  const [errors, setErrors] = useState<FieldErrors>({})
  const [preview, setPreview] = useState<PreviewState>({ phase: 'idle' })
  const [confirmed, setConfirmed] = useState(false)
  const [confirmError, setConfirmError] = useState<string | null>(null)
  const previewBusy = useRef(false)

  const storeKey = pendingCashTransferAttemptKey(binding.sessionEpoch, actorId, companyId)
  const command = useCommitCommand(companyId, storeKey, onCommitted)

  const sourceLeg = useLeg(companyId, binding, sourceKind, sourceAccountId, occurredOn)
  const targetLeg = useLeg(companyId, binding, targetKind, targetAccountId, occurredOn)
  const exchange = kind === 'CurrencyExchange'

  const built = build({
    kind, sourceKind, sourceAccountId, source: sourceLeg.value, targetKind, targetAccountId, target: targetLeg.value,
    sourceAmount, targetAmount, occurredOn, valueOn, reference, description,
  })
  // A hint only: the server answers cash_transfer_currency_mismatch / _currency_same with the same rule.
  const currencyHint = sourceLeg.value === null || targetLeg.value === null || kind === ''
    ? null
    : kind === 'SameCurrency' && sourceLeg.value.currencyId !== targetLeg.value.currencyId
      ? T.sameCurrencyNeedsSame
      : exchange && sourceLeg.value.currencyId === targetLeg.value.currencyId
        ? T.exchangeNeedsDifferent
        : null

  const accepted = preview.phase === 'done' && preview.outcome.kind === 'accepted'
    && built.preparation !== null && samePreparation(preview.sent, built.preparation)
    ? preview.sent
    : null

  const check = async (event: FormEvent) => {
    event.preventDefault()
    if (command.locked || previewBusy.current) return
    setErrors(built.errors)
    if (built.preparation === null) return
    previewBusy.current = true
    const sent = built.preparation
    setPreview({ phase: 'checking' })
    setConfirmed(false)
    const snapshot = boundary.capture()
    const outcome = await previewCashTransfer(companyId, sent)
    previewBusy.current = false
    if (outcome.kind === 'refused' && (outcome.status === 401 || outcome.status === 403)) {
      boundary.report(outcome.status, snapshot)
    }
    setPreview({ phase: 'done', outcome, sent })
  }

  const commit = () => {
    if (accepted === null) return
    if (!confirmed) {
      setConfirmError(T.commitNeedsConfirm)
      return
    }
    setConfirmError(null)
    void command.send({ preparation: accepted, transferConfirmed: true })
  }

  const locked = command.locked

  return <form className={styles.form} onSubmit={(event) => void check(event)} aria-label={T.formHeading} noValidate>
    <h2>{T.formHeading}</h2>
    <fieldset disabled={locked}>
      <Field label={T.kind}>
        {(control) => <select {...control} value={kind}
          onChange={(event) => setKind(event.target.value as CashTransferKind | '')}>
          <option value="">{T.kindChoose}</option>
          {CASH_TRANSFER_KINDS.map((option) => <option key={option} value={option}>{TRANSFER_KIND_LABEL[option]}</option>)}
        </select>}
      </Field>

      <Field label={T.occurredOn} error={errors.occurredOn}>
        {(control) => <input {...control} type="date" value={occurredOn} onChange={(event) => setOccurredOn(event.target.value)} />}
      </Field>

      <LegFields heading={T.sourceLegHeading} kind={sourceKind} accountId={sourceAccountId} leg={sourceLeg} codes={codes}
        onKind={(next) => { setSourceKind(next); setSourceAccountId('') }} onAccount={setSourceAccountId} />
      <LegFields heading={T.targetLegHeading} kind={targetKind} accountId={targetAccountId} leg={targetLeg} codes={codes}
        error={errors.targetAccount}
        onKind={(next) => { setTargetKind(next); setTargetAccountId('') }} onAccount={setTargetAccountId} />

      {currencyHint === null ? null : <p className={styles.error} role="alert">{currencyHint}</p>}

      <Field label={exchange ? T.sourceAmount : T.sameCurrencyAmount} help={T.amountHelp} error={errors.sourceAmount}>
        {(control) => <input {...control} inputMode="decimal" autoComplete="off" value={sourceAmount}
          onChange={(event) => setSourceAmount(event.target.value)} />}
      </Field>
      {exchange ? <>
        <Field label={T.targetAmount} help={T.amountHelp} error={errors.targetAmount}>
          {(control) => <input {...control} inputMode="decimal" autoComplete="off" value={targetAmount}
            onChange={(event) => setTargetAmount(event.target.value)} />}
        </Field>
        <p className={styles.hint}>{T.rateCalculated}</p>
        <p className={styles.hint}>{T.exchangeRuleNote}</p>
      </> : null}

      <Field label={T.valueOn} error={errors.valueOn}>
        {(control) => <input {...control} type="date" value={valueOn} onChange={(event) => setValueOn(event.target.value)} />}
      </Field>
      <Field label={T.reference} error={errors.reference}>
        {(control) => <input {...control} autoComplete="off" value={reference} onChange={(event) => setReference(event.target.value)} />}
      </Field>
      <Field label={T.description} error={errors.description}>
        {(control) => <textarea {...control} value={description} onChange={(event) => setDescription(event.target.value)} />}
      </Field>
    </fieldset>

    <div className={styles.actions}>
      <Button type="submit" variant="secondary" disabled={locked || preview.phase === 'checking'}
        aria-busy={preview.phase === 'checking'}>
        {preview.phase === 'checking' ? T.previewing : T.preview}
      </Button>
      <Button type="button" variant="ghost" disabled={locked} onClick={onCancel}>{T.cancelForm}</Button>
    </div>
    <PreviewPanel state={preview} current={built.preparation} />

    {accepted !== null || command.state.phase === 'unknown' ? <div className={styles.command}>
      <p className={styles.hint}>{T.balanceNote}</p>
      {command.state.phase !== 'unknown' ? <>
        <label>
          <input type="checkbox" checked={confirmed} disabled={locked}
            onChange={(event) => setConfirmed(event.target.checked)} /> {T.commitConfirm}
        </label>
        {confirmError === null ? null : <p className={styles.error} role="alert">{confirmError}</p>}
        <Button type="button" variant="primary" disabled={locked} aria-busy={command.state.phase === 'sending'}
          onClick={commit}>
          {command.state.phase === 'sending' ? T.committing : T.commit}
        </Button>
      </> : null}
    </div> : null}
    <AttemptPanel state={command.state} onRetry={command.retry} />
  </form>
}
