/**
 * F-25 — Finans → Masraf ödemeleri (FNS-D25): a payment not tied to a party, under the one "Masraf" item kept by
 * identity, with a mandatory description.
 *
 * The flow is the server's: a draft is saved, a DIFFERENT person approves it, and once the money has left the bank
 * or the till it is recorded. The screen shows the server's decisions only — eligibility, the money profile, the
 * currency and the balance check (FNS-D11/D22: a record that would take the account below zero is refused) are all
 * answered by the backend; nothing about money is worked out here.
 *
 * A command whose answer could not be read keeps its idempotency key and exact body in memory: the inputs lock and
 * only "Aynı isteği yeniden gönder" is offered, so a retry can never create a second payment. A company, actor or
 * session change remounts the screen and nothing of the previous context is drawn.
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
import { StatusIndicator, type StatusTone } from '../ui/StatusIndicator'
import { currencyShown, WithCurrencyCodes, type CurrencyCodes } from './currencyCodes'
import { isDateOnly } from './dateOnly'
import { formatDecimalTextTr, parseAmountInput } from './decimalText'
import {
  EXPENSE_DESCRIPTION_MAX_LENGTH,
  EXPENSE_REFERENCE_MAX_LENGTH,
  EXPENSE_SOURCE_KINDS,
  type ExpensePaymentSourceKind,
  type ExpensePaymentStatus,
  type ExpensePaymentView,
} from './expensePaymentContract'
import {
  fetchBankAccountMaster,
  fetchCashAccounts,
  fetchExpenseDraftContext,
  fetchExpenseItems,
  fetchExpensePayment,
  fetchExpensePayments,
  forgetPendingExpenseAttempt,
  generateExpenseAttemptKey,
  pendingExpenseAttemptKey,
  recallPendingExpenseAttempt,
  rememberPendingExpenseAttempt,
  sendExpensePaymentCommand,
  type ExpensePaymentCommand,
  type ExpensePaymentCommandOutcome,
  type PendingExpenseAttempt,
  type UnknownReason,
} from './expensePaymentHttp'
import {
  EXPENSE_PAYMENT_TEXT as T,
  expenseItemLabel,
  issueMessage,
  SOURCE_KIND_LABEL,
  STATUS_LABEL,
} from './expensePaymentText'
import styles from './Finance.module.css'

const HEADING_ID = 'finance-expense-payments-heading'

/** The dot's colour only — the status text carries the meaning. */
const STATUS_TONE: Record<ExpensePaymentStatus, StatusTone> = { Draft: 'neutral', Approved: 'info', Recorded: 'success' }

export interface ExpensePaymentPageProps {
  readonly companyId: string
  readonly actorId: string
  readonly canDraft: boolean
  readonly canApprove: boolean
  readonly canRecord: boolean
}

interface Binding {
  readonly namespace: string
  readonly generation: number
  readonly sessionEpoch: number
}

/** The screen is keyed on the context and the route entry: a switch drops every row, form and late answer. */
export function ExpensePaymentPage(props: ExpensePaymentPageProps) {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const frame = (body: ReactNode) => (
    <section aria-labelledby={HEADING_ID} data-testid="finance-expense-payments">
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

function Instant({ value }: { readonly value: string | null }) {
  if (value === null) return <>{T.noValue}</>
  const parsed = new Date(value)
  return <time dateTime={value}>
    {Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString('tr-TR', { dateStyle: 'medium', timeStyle: 'short' })}
  </time>
}

const amountText = (wire: string) => formatDecimalTextTr(wire) ?? wire

function Status({ status }: { readonly status: ExpensePaymentStatus }) {
  return <StatusIndicator tone={STATUS_TONE[status]}>{STATUS_LABEL[status]}</StatusIndicator>
}

interface WorkspaceProps extends ExpensePaymentPageProps {
  readonly binding: Binding
}

function Workspace(props: WorkspaceProps) {
  const { companyId, canDraft } = props
  const [openId, setOpenId] = useState<string | null>(null)
  const [drafting, setDrafting] = useState(false)
  const client = useQueryClient()
  const { namespace, generation } = props.binding
  const listKey = useMemo(() => [namespace, generation, 'finance-expense-payments', companyId] as const,
    [namespace, generation, companyId])
  const items = useExpenseItems(companyId, props.binding)
  const itemCodes = useMemo(
    () => items.data?.type === 'ok-single' ? new Map(items.data.data.items.map((item) => [item.id, item.code])) : null,
    [items.data],
  )

  /** After a decided command the list is read again from its first page. */
  const refreshList = useCallback(() => {
    void client.invalidateQueries({ queryKey: listKey })
  }, [client, listKey])

  const saved = useCallback((payment: ExpensePaymentView) => {
    setDrafting(false)
    setOpenId(payment.id)
    refreshList()
  }, [refreshList])

  return <WithCurrencyCodes>{(codes) => <>
    <p>{T.intro}</p>
    {canDraft && !drafting
      ? <div className={styles.actions}>
          <Button type="button" variant="primary" onClick={() => setDrafting(true)}>{T.newPayment}</Button>
        </div>
      : null}
    {canDraft && drafting
      ? <DraftForm {...props} codes={codes} onCancel={() => setDrafting(false)} onSaved={saved} />
      : null}
    <PaymentList {...props} listKey={listKey} codes={codes} itemCodes={itemCodes} onOpen={setOpenId} />
    {openId === null ? null : (
      <PaymentDetail key={openId} {...props} paymentId={openId} codes={codes} itemCodes={itemCodes}
        onClose={() => setOpenId(null)} onChanged={refreshList} />
    )}
  </>}</WithCurrencyCodes>
}

// ---------------------------------------------------------------------------------------------------------------
// List
// ---------------------------------------------------------------------------------------------------------------

/** Item id → stable code from the catalog read; `null` while it is not read. */
type ItemCodes = ReadonlyMap<string, string> | null

/** FNS-D25: the item is stored by identity; its label comes from the catalog's code, never guessed from the id. */
const itemShown = (expenseItemId: string, itemCodes: ItemCodes) =>
  itemCodes === null ? T.noValue : expenseItemLabel(itemCodes.get(expenseItemId) ?? null)

function useExpenseItems(companyId: string, binding: Binding) {
  const boundRead = useBoundRead()
  return useQuery({
    queryKey: [binding.namespace, binding.generation, 'finance-expense-items', companyId],
    gcTime: 0,
    queryFn: ({ signal }) => boundRead(() => fetchExpenseItems(companyId, signal)),
  })
}

interface ListProps extends WorkspaceProps {
  readonly listKey: readonly unknown[]
  readonly codes: CurrencyCodes
  readonly itemCodes: ItemCodes
  onOpen(paymentId: string): void
}

const NO_ROWS: readonly ExpensePaymentView[] = []

function PaymentList({ companyId, listKey, codes, itemCodes, onOpen }: ListProps) {
  const boundRead = useBoundRead()
  const [more, setMore] = useState<readonly ExpensePaymentView[]>(NO_ROWS)
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
    queryFn: ({ signal }) => boundRead(() => fetchExpensePayments(companyId, null, signal)),
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
    const outcome = await boundRead(() => fetchExpensePayments(companyId, nextCursor, controller.signal))
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

  const columns: readonly DataTableColumn<ExpensePaymentView>[] = [
    { key: 'occurredOn', header: T.columnOccurredOn, cell: (row) => formatDate(row.preparation.occurredOn) },
    { key: 'source', header: T.columnSource, cell: (row) => SOURCE_KIND_LABEL[row.preparation.sourceKind] },
    { key: 'item', header: T.columnItem, cell: (row) => itemShown(row.preparation.expenseItemId, itemCodes) },
    {
      key: 'amount',
      header: T.columnAmount,
      align: 'end',
      cell: (row) => `${amountText(row.preparation.money.amount)} ${currencyShown(row.preparation.money.currencyId, codes)}`,
    },
    { key: 'description', header: T.columnDescription, cell: (row) => row.preparation.description },
    { key: 'status', header: T.columnStatus, cell: (row) => <Status status={row.status} /> },
    {
      key: 'action',
      header: T.columnAction,
      cell: (row) => <Button type="button" size="sm" variant="ghost" onClick={() => onOpen(row.id)}
        aria-label={T.openName(formatDate(row.preparation.occurredOn), row.preparation.description)}>{T.open}</Button>,
    },
  ]

  return <div className={styles.list}>
    <DataTable label={T.listCaption} columns={columns} rows={rows} rowKey={(row) => row.id}
      rowAttributes={(row) => ({ 'data-expense-payment-id': row.id })} />
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
// Commands
// ---------------------------------------------------------------------------------------------------------------

type FailedOutcome = Extract<ExpensePaymentCommandOutcome, { kind: 'failed' }>

type AttemptState =
  | { readonly phase: 'idle' }
  | { readonly phase: 'sending' }
  | { readonly phase: 'applied'; readonly resource: ExpensePaymentView; readonly replayed: boolean }
  | { readonly phase: 'failed'; readonly failure: FailedOutcome }
  | { readonly phase: 'unknown'; readonly reason: UnknownReason | null; readonly attempt: PendingExpenseAttempt }

/**
 * One command's attempt. The key is remembered BEFORE the request leaves and forgotten only on a decided answer; an
 * unknown answer keeps it, and the next send resends that exact attempt rather than the new body.
 */
function useExpenseCommand(companyId: string, storeKey: string, onApplied: (resource: ExpensePaymentView) => void) {
  const boundary = useAuthBoundary()
  const [state, setState] = useState<AttemptState>(() => {
    const pending = recallPendingExpenseAttempt(storeKey)
    return pending === null ? { phase: 'idle' } : { phase: 'unknown', reason: null, attempt: pending }
  })
  const busy = useRef(false)

  const send = useCallback(async (command: ExpensePaymentCommand) => {
    if (busy.current) return
    busy.current = true
    const attempt = recallPendingExpenseAttempt(storeKey) ?? { key: generateExpenseAttemptKey(), command }
    rememberPendingExpenseAttempt(storeKey, attempt)
    setState({ phase: 'sending' })
    const snapshot = boundary.capture()
    const outcome = await sendExpensePaymentCommand(companyId, attempt.key, attempt.command)
    busy.current = false
    if (outcome.kind === 'unknown') {
      setState({ phase: 'unknown', reason: outcome.reason, attempt })
      return
    }
    forgetPendingExpenseAttempt(storeKey)
    if (outcome.kind === 'applied') {
      setState({ phase: 'applied', resource: outcome.resource, replayed: outcome.replayed })
      onApplied(outcome.resource)
      return
    }
    if (outcome.status === 401 || outcome.status === 403) boundary.report(outcome.status, snapshot)
    setState({ phase: 'failed', failure: outcome })
  }, [boundary, companyId, onApplied, storeKey])

  const retry = useCallback(() => {
    if (state.phase === 'unknown') void send(state.attempt.command)
  }, [send, state])

  return { state, send, retry, locked: state.phase === 'sending' || state.phase === 'unknown' }
}

function failureText(failure: FailedOutcome): string {
  switch (failure.status) {
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
      return failure.outcome === 'VersionConflict' ? T.conflict : T.failedHeading
  }
}

/** The server's answer to the last attempt; the issue codes are shown as the server gave them, in Turkish. */
function AttemptPanel({ state, onRetry, onReload }: {
  readonly state: AttemptState
  readonly onRetry: () => void
  readonly onReload?: () => void
}) {
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
    <p className={styles.attemptTitle}>{failureText(failure)}</p>
    {failure.issues.length > 0
      ? <ul>{failure.issues.map((issue) => <li key={`${issue.field}:${issue.code}`}>{issueMessage(issue.code)}</li>)}</ul>
      : null}
    {failure.outcome === 'VersionConflict' && onReload !== undefined
      ? <Button type="button" variant="secondary" onClick={onReload}>{T.reload}</Button>
      : null}
  </div>
}

// ---------------------------------------------------------------------------------------------------------------
// Detail, approval and record
// ---------------------------------------------------------------------------------------------------------------

interface DetailProps extends WorkspaceProps {
  readonly paymentId: string
  readonly codes: CurrencyCodes
  readonly itemCodes: ItemCodes
  onClose(): void
  onChanged(): void
}

function PaymentDetail(props: DetailProps) {
  const { companyId, paymentId, binding } = props
  const boundRead = useBoundRead()
  const client = useQueryClient()
  const { namespace, generation } = binding
  const detailKey = useMemo(() => [namespace, generation, 'finance-expense-payment', companyId, paymentId] as const,
    [namespace, generation, companyId, paymentId])
  const read = useQuery({
    queryKey: detailKey,
    gcTime: 0,
    queryFn: ({ signal }) => boundRead(() => fetchExpensePayment(companyId, paymentId, signal)),
  })
  const payment = read.data?.type === 'ok-single' ? read.data.data : null

  const { onChanged } = props
  /** The decided command's answer replaces the detail whole; the list is read again. */
  const applied = useCallback((resource: ExpensePaymentView) => {
    client.setQueryData(detailKey, { type: 'ok-single', data: resource } satisfies ViewOutcome<ExpensePaymentView>)
    onChanged()
  }, [client, detailKey, onChanged])

  return <section className={styles.detail} aria-label={T.detailHeading} data-testid="finance-expense-payment-detail">
    <h2>{T.detailHeading}</h2>
    {payment === null
      ? <StateBlock state={stateOf(read.data)} subject={T.detailSubject} onRetry={() => void read.refetch()} />
      : <>
          <PaymentFacts payment={payment} codes={props.codes} itemCodes={props.itemCodes} />
          <PaymentCommands {...props} payment={payment} onApplied={applied} onReload={() => void read.refetch()} />
        </>}
    <div className={styles.actions}>
      <Button type="button" variant="ghost" onClick={props.onClose}>{T.close}</Button>
    </div>
  </section>
}

function PaymentFacts({ payment, codes, itemCodes }: {
  readonly payment: ExpensePaymentView
  readonly codes: CurrencyCodes
  readonly itemCodes: ItemCodes
}) {
  const { preparation } = payment
  return <dl className={styles.facts}>
    <div><dt>{T.columnStatus}</dt><dd><Status status={payment.status} /></dd></div>
    <div><dt>{T.columnSource}</dt><dd>{SOURCE_KIND_LABEL[preparation.sourceKind]}</dd></div>
    <div><dt>{T.columnItem}</dt><dd>{itemShown(preparation.expenseItemId, itemCodes)}</dd></div>
    <div><dt>{T.columnAmount}</dt><dd>{amountText(preparation.money.amount)}</dd></div>
    <div><dt>{T.currency}</dt><dd>{currencyShown(preparation.money.currencyId, codes)}</dd></div>
    <div><dt>{T.occurredOn}</dt><dd>{formatDate(preparation.occurredOn)}</dd></div>
    <div><dt>{T.valueOnShort}</dt><dd>{preparation.valueOn === null ? T.noValue : formatDate(preparation.valueOn)}</dd></div>
    <div><dt>{T.columnDescription}</dt><dd>{preparation.description}</dd></div>
    <div><dt>{T.referenceShort}</dt><dd>{preparation.reference ?? T.noValue}</dd></div>
    <div><dt>{T.preparedBy}</dt><dd>{payment.preparedBy}</dd></div>
    <div><dt>{T.preparedAt}</dt><dd><Instant value={payment.preparedAtUtc} /></dd></div>
    <div><dt>{T.approvedBy}</dt><dd>{payment.approvedBy ?? T.noValue}</dd></div>
    <div><dt>{T.approvedAt}</dt><dd><Instant value={payment.approvedAtUtc} /></dd></div>
    <div><dt>{T.recordedBy}</dt><dd>{payment.recordedBy ?? T.noValue}</dd></div>
    <div><dt>{T.recordedAt}</dt><dd><Instant value={payment.recordedAtUtc} /></dd></div>
  </dl>
}

interface CommandsProps extends DetailProps {
  readonly payment: ExpensePaymentView
  onApplied(resource: ExpensePaymentView): void
  onReload(): void
}

function PaymentCommands(props: CommandsProps) {
  const { payment } = props
  if (payment.status === 'Draft' && props.canApprove) return <ApproveCommand {...props} />
  if (payment.status === 'Approved' && props.canRecord) return <RecordCommand {...props} />
  return null
}

function ApproveCommand({ companyId, actorId, binding, payment, onApplied, onReload }: CommandsProps) {
  const storeKey = pendingExpenseAttemptKey('approve', binding.sessionEpoch, actorId, companyId, payment.id)
  const command = useExpenseCommand(companyId, storeKey, onApplied)
  // Maker ≠ checker is the server's rule; the screen only does not offer the button to the maker.
  const own = payment.preparedBy === actorId
  return <div className={styles.command}>
    {own
      ? <p className={styles.hint}>{T.ownDraftCannotApprove}</p>
      : command.state.phase !== 'unknown'
        ? <Button type="button" variant="primary" disabled={command.locked} aria-busy={command.state.phase === 'sending'}
            onClick={() => void command.send({ action: 'approve', body: { paymentId: payment.id, expectedVersion: payment.version } })}>
            {command.state.phase === 'sending' ? T.approving : T.approve}
          </Button>
        : null}
    <AttemptPanel state={command.state} onRetry={command.retry} onReload={onReload} />
  </div>
}

function RecordCommand({ companyId, actorId, binding, payment, onApplied, onReload }: CommandsProps) {
  const storeKey = pendingExpenseAttemptKey('record', binding.sessionEpoch, actorId, companyId, payment.id)
  const command = useExpenseCommand(companyId, storeKey, onApplied)
  const [confirmed, setConfirmed] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const submit = () => {
    if (!confirmed) {
      setError(T.recordNeedsConfirm)
      return
    }
    setError(null)
    void command.send({
      action: 'record',
      body: { paymentId: payment.id, expectedVersion: payment.version, paymentConfirmed: true },
    })
  }
  return <div className={styles.command}>
    <p className={styles.hint}>{T.balanceNote}</p>
    {command.state.phase !== 'unknown' ? <>
      <label>
        <input type="checkbox" checked={confirmed} disabled={command.locked}
          onChange={(event) => setConfirmed(event.target.checked)} /> {T.recordConfirm}
      </label>
      {error === null ? null : <p className={styles.error} role="alert">{error}</p>}
      <Button type="button" variant="primary" disabled={command.locked} aria-busy={command.state.phase === 'sending'}
        onClick={submit}>
        {command.state.phase === 'sending' ? T.recording : T.record}
      </Button>
    </> : null}
    <AttemptPanel state={command.state} onRetry={command.retry} onReload={onReload} />
  </div>
}

// ---------------------------------------------------------------------------------------------------------------
// Draft
// ---------------------------------------------------------------------------------------------------------------

interface AccountOption {
  readonly id: string
  readonly displayName: string
  readonly currencyId: string
}

interface DraftProps extends WorkspaceProps {
  readonly codes: CurrencyCodes
  onCancel(): void
  onSaved(payment: ExpensePaymentView): void
}

interface FieldErrors {
  readonly amount?: string
  readonly description?: string
  readonly reference?: string
  readonly occurredOn?: string
}

/** A single-answer read mapped to another shape; every other outcome passes through, a list answer is malformed. */
function mapSingle<A, B>(outcome: ViewOutcome<A>, map: (data: A) => B): ViewOutcome<B> {
  if (outcome.type === 'ok-single') return { type: 'ok-single', data: map(outcome.data) }
  if (outcome.type === 'ok') return { type: 'malformed' }
  return outcome
}

/** Only what the server would also take: an approved, active account; for a bank, one declared for money movement. */
function useAccounts(companyId: string, binding: Binding, sourceKind: ExpensePaymentSourceKind | '') {
  const boundRead = useBoundRead()
  return useQuery({
    queryKey: [binding.namespace, binding.generation, 'finance-expense-accounts', companyId, sourceKind],
    enabled: sourceKind !== '',
    gcTime: 0,
    queryFn: async ({ signal }): Promise<ViewOutcome<readonly AccountOption[]>> => {
      if (sourceKind === 'Bank') {
        return mapSingle(await boundRead(() => fetchBankAccountMaster(companyId, signal)), (page) => page.items
          .filter((item) => item.account.isActive && item.card.moneyEligibility === 'Eligible' && item.card.iban !== null)
          .map((item) => ({ id: item.account.id, displayName: item.account.displayName, currencyId: item.account.currencyId })))
      }
      return mapSingle(await boundRead(() => fetchCashAccounts(companyId, signal)), (list) => list.items
        .filter((item) => item.status === 'Approved' && item.isActive)
        .map((item) => ({ id: item.id, displayName: item.displayName, currencyId: item.currencyId })))
    },
  })
}

function DraftForm(props: DraftProps) {
  const { companyId, actorId, binding, codes, onCancel, onSaved } = props
  const boundRead = useBoundRead()
  const [sourceKind, setSourceKind] = useState<ExpensePaymentSourceKind | ''>('')
  const [accountId, setAccountId] = useState('')
  const [occurredOn, setOccurredOn] = useState('')
  const [valueOn, setValueOn] = useState('')
  const [amount, setAmount] = useState('')
  const [description, setDescription] = useState('')
  const [reference, setReference] = useState('')
  const [itemId, setItemId] = useState('')
  const [errors, setErrors] = useState<FieldErrors>({})

  const storeKey = pendingExpenseAttemptKey('draft', binding.sessionEpoch, actorId, companyId, null)
  const command = useExpenseCommand(companyId, storeKey, onSaved)

  const accounts = useAccounts(companyId, binding, sourceKind)
  const accountList = accounts.data?.type === 'ok-single' ? accounts.data.data : null

  const items = useExpenseItems(companyId, binding)
  const catalog = items.data?.type === 'ok-single' ? items.data.data.items : null
  // FNS-D25: one item today; a single catalog entry is taken as given, more than one must be chosen.
  const chosenItemId = catalog !== null && catalog.length === 1 ? catalog[0].id : itemId

  const contextReady = sourceKind !== '' && accountId !== '' && isDateOnly(occurredOn)
  const draftContext = useQuery({
    queryKey: [binding.namespace, binding.generation, 'finance-expense-draft-context', companyId, sourceKind, accountId, occurredOn],
    enabled: contextReady,
    gcTime: 0,
    queryFn: ({ signal }) => boundRead(() => fetchExpenseDraftContext(companyId, {
      sourceKind: sourceKind as ExpensePaymentSourceKind,
      treasuryAccountId: accountId,
      occurredOn,
    }, signal)),
  })
  const context = contextReady && draftContext.data?.type === 'ok-single' ? draftContext.data.data : null

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (command.locked) return
    const parsed = parseAmountInput(amount)
    const trimmedDescription = description.trim()
    const trimmedReference = reference.trim()
    const next: FieldErrors = {
      ...(parsed.ok ? {} : { amount: T.invalidAmount[parsed.error] }),
      ...(trimmedDescription.length === 0
        ? { description: T.descriptionRequired }
        : trimmedDescription.length > EXPENSE_DESCRIPTION_MAX_LENGTH
          ? { description: T.descriptionTooLong(EXPENSE_DESCRIPTION_MAX_LENGTH) }
          : {}),
      ...(trimmedReference.length > EXPENSE_REFERENCE_MAX_LENGTH
        ? { reference: T.referenceTooLong(EXPENSE_REFERENCE_MAX_LENGTH) }
        : {}),
      ...(isDateOnly(occurredOn) ? {} : { occurredOn: T.dateRequired }),
    }
    setErrors(next)
    if (Object.keys(next).length > 0 || !parsed.ok || context === null || sourceKind === '' || chosenItemId === '') return
    void command.send({
      action: 'draft',
      body: {
        paymentId: null,
        expectedVersion: null,
        preparation: {
          sourceKind,
          treasuryAccountId: accountId,
          expenseItemId: chosenItemId,
          money: { amount: parsed.wire, currencyId: context.currencyId },
          profile: context.profile,
          occurredOn,
          valueOn: valueOn === '' ? null : valueOn,
          reference: trimmedReference === '' ? null : trimmedReference,
          description: trimmedDescription,
        },
      },
    })
  }

  const locked = command.locked

  return <form className={styles.form} onSubmit={submit} aria-label={T.formHeading} noValidate>
    <h2>{T.formHeading}</h2>
    <fieldset disabled={locked}>
      <Field label={T.sourceKind}>
        {(control) => <select {...control} value={sourceKind}
          onChange={(event) => {
            setSourceKind(event.target.value as ExpensePaymentSourceKind | '')
            setAccountId('')
          }}>
          <option value="">{T.sourceKindChoose}</option>
          {EXPENSE_SOURCE_KINDS.map((kind) => <option key={kind} value={kind}>{SOURCE_KIND_LABEL[kind]}</option>)}
        </select>}
      </Field>

      {sourceKind === '' ? null : accountList === null
        ? <StateBlock state={stateOf(accounts.data)} subject={T.accountsSubject} onRetry={() => void accounts.refetch()} />
        : accountList.length === 0
          ? <p className={styles.hint}>{T.noEligibleAccount}</p>
          : <Field label={T.account}>
              {(control) => <select {...control} value={accountId} onChange={(event) => setAccountId(event.target.value)}>
                <option value="">{T.accountChoose}</option>
                {accountList.map((account) => <option key={account.id} value={account.id}>
                  {T.accountOption(account.displayName, currencyShown(account.currencyId, codes))}
                </option>)}
              </select>}
            </Field>}

      <Field label={T.occurredOn} error={errors.occurredOn}>
        {(control) => <input {...control} type="date" value={occurredOn} onChange={(event) => setOccurredOn(event.target.value)} />}
      </Field>

      {!contextReady
        ? <p className={styles.hint}>{T.draftContextWaiting}</p>
        : context === null
          ? <StateBlock state={stateOf(draftContext.data)} subject={T.draftContextSubject}
              onRetry={() => void draftContext.refetch()} />
          : <dl className={styles.facts}>
              <div><dt>{T.currency}</dt><dd>{currencyShown(context.currencyId, codes)}</dd></div>
            </dl>}

      {catalog === null
        ? <StateBlock state={stateOf(items.data)} subject={T.expenseItemsSubject} onRetry={() => void items.refetch()} />
        : catalog.length === 0
          ? <p className={styles.error} role="alert">{T.expenseItemsEmpty}</p>
          : catalog.length === 1
            ? <dl className={styles.facts}><div><dt>{T.expenseItem}</dt><dd>{expenseItemLabel(catalog[0].code)}</dd></div></dl>
            : <Field label={T.expenseItem}>
                {(control) => <select {...control} value={itemId} onChange={(event) => setItemId(event.target.value)}>
                  <option value="">{T.expenseItemChoose}</option>
                  {catalog.map((item) => <option key={item.id} value={item.id}>{expenseItemLabel(item.code)}</option>)}
                </select>}
              </Field>}

      <Field label={T.amount} help={T.amountHelp} error={errors.amount}>
        {(control) => <input {...control} inputMode="decimal" autoComplete="off" value={amount}
          onChange={(event) => setAmount(event.target.value)} />}
      </Field>
      <Field label={T.description} help={T.descriptionHelp} error={errors.description}>
        {(control) => <textarea {...control} required value={description}
          onChange={(event) => setDescription(event.target.value)} />}
      </Field>
      <Field label={T.reference} error={errors.reference}>
        {(control) => <input {...control} autoComplete="off" value={reference} onChange={(event) => setReference(event.target.value)} />}
      </Field>
      <Field label={T.valueOn}>
        {(control) => <input {...control} type="date" value={valueOn} onChange={(event) => setValueOn(event.target.value)} />}
      </Field>
    </fieldset>

    <div className={styles.actions}>
      {command.state.phase !== 'unknown'
        ? <Button type="submit" variant="primary" disabled={locked || context === null || chosenItemId === ''}
            aria-busy={command.state.phase === 'sending'}>
            {command.state.phase === 'sending' ? T.saving : T.saveDraft}
          </Button>
        : null}
      <Button type="button" variant="ghost" disabled={locked} onClick={onCancel}>{T.cancelForm}</Button>
    </div>
    <AttemptPanel state={command.state} onRetry={command.retry} />
  </form>
}
