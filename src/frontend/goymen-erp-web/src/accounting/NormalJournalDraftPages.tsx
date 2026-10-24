import { useEffect, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { parseAmountInput } from '../finance/decimalText'
import { isDateOnly } from '../finance/dateOnly'
import type { SourcePage, SourceSummary } from '../finance/contract'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { fetchNormalJournalDraft, fetchNormalJournalDraftAccounts } from './api'
import { fetchNormalJournalDraftSource, fetchNormalJournalSources } from './normalJournalSourceApi'
import {
  ACCOUNTING_CAPABILITY,
  ACCOUNTING_ROUTE,
  journalDetailPath,
  normalDraftApprovalPath,
  normalDraftDetailPath,
} from './capabilities'
import type { CreateNormalJournalDraftLine, CreateNormalJournalDraftRequest, NormalJournalDraft, NormalJournalSide } from './contract'
import { sendNormalJournalCommand, type NormalJournalCommandOutcome, type NormalJournalCommandSnapshot } from './normalJournalApi'
import { clearNormalJournalCreate, recallNormalJournalCreate, storeNormalJournalCreate } from './normalJournalCreateAttempt'
import {
  NORMAL_JOURNAL_PERIOD_CODES,
  NORMAL_JOURNAL_TEXT as T,
  accountLabel,
  normalDraftSavedText,
  normalJournalApprovalFailureText,
  normalJournalCreateFailureText,
  normalPreparationVersionsText,
  normalSourceRefText,
} from './accountingText'
import { NormalJournalApprovalPeriod } from './NormalJournalApprovalPeriod'
import styles from './Accounting.module.css'

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const newId = () => crypto.randomUUID()
type LineForm = { readonly accountId: string; readonly side: NormalJournalSide; readonly amount: string }

function sourceLabel(source: SourceSummary): string {
  const number = GUID.test(source.sourceNumber) ? normalSourceRefText(source.id) : source.sourceNumber
  return `${number} - ${source.occurredOn}`
}

export function NormalJournalDraftCreatePage() {
  const shell = useShellState()
  const key = contextKey(shell)
  if (key === null) {
    return <StateBlock state={{ kind: 'initial-loading' }} subject={T.contextSubject} />
  }
  const namespace = contextKeyString(key)
  const context = shell.activeContext
  const attemptOwner = context?.scopeMode === 'single' && context.companyId !== null
    ? `${context.companyId}:${context.userId}` : 'unbound'
  return <NormalJournalDraftCreate key={`${namespace ?? 'none'}:${shell.routeEntryGeneration}`} namespace={namespace} attemptOwner={attemptOwner} />
}

function NormalJournalDraftCreate({ namespace, attemptOwner }: { readonly namespace: string; readonly attemptOwner: string }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const mountedRef = useRef(true)
  useEffect(() => {
    mountedRef.current = true
    return () => {
      mountedRef.current = false
    }
  }, [])
  const companyId = shell.activeContext?.companyId ?? null
  const single = shell.activeContext?.scopeMode === 'single' && companyId !== null
  const canCreate = hasCapability(shell, ACCOUNTING_CAPABILITY.normalDraftCreate)
  const recovered = useRef(recallNormalJournalCreate(attemptOwner))
  const [sourceId, setSourceId] = useState(recovered.current?.request.sourceId ?? '')
  const [transactionDate, setTransactionDate] = useState(recovered.current?.request.transactionDate ?? '')
  const [accountingDate, setAccountingDate] = useState(recovered.current?.request.accountingDate ?? '')
  const [valueDate, setValueDate] = useState(recovered.current?.request.valueDate ?? '')
  const [lines, setLines] = useState<readonly LineForm[]>(recovered.current?.request.lines ?? [
    { accountId: '', side: 'Debit', amount: '' },
    { accountId: '', side: 'Credit', amount: '' },
  ])
  const [error, setError] = useState<string | null>(null)
  const [snapshot, setSnapshot] = useState<NormalJournalCommandSnapshot | null>(recovered.current)
  const [outcome, setOutcome] = useState<NormalJournalCommandOutcome | null>(
    recovered.current === null ? null : { type: 'unknown', reason: 'network' },
  )
  const sending = snapshot !== null && outcome === null
  const unresolved = outcome?.type === 'unknown'
  const sourceListQuery = useQuery({
    queryKey: [namespace, shell.routeEntryGeneration, 'accounting', 'normal-journal-source-picker'],
    queryFn: async ({ signal }) => {
      const security = boundary.capture()
      const answer = await fetchNormalJournalSources(companyId ?? '', signal)
      if (answer.type === 'http' && (answer.status === 401 || answer.status === 403)) boundary.report(answer.status, security)
      return answer
    },
    enabled: single && canCreate,
    retry: false,
    staleTime: 0,
    refetchOnMount: 'always',
  })
  const sourceListState = (sourceListQuery.isPending
    ? { kind: 'initial-loading' }
    : classifyOutcome(sourceListQuery.data ?? { type: 'malformed' })) as RemoteViewState<SourcePage>
  const authorizedSources = sourceListState.kind === 'ready'
    ? sourceListState.data.items.filter(source => source.stage === 'Recorded') : []
  const sourceIsValid = GUID.test(sourceId)
  const preparationQuery = useQuery({
    queryKey: [namespace, shell.routeEntryGeneration, 'accounting', 'normal-journal-source', sourceId],
    queryFn: async ({ signal }) => {
      const security = boundary.capture()
      const answer = await fetchNormalJournalDraftSource(companyId ?? '', sourceId, signal)
      if (answer.type === 'http' && (answer.status === 401 || answer.status === 403)) boundary.report(answer.status, security)
      return answer
    },
    enabled: single && canCreate && sourceIsValid,
    retry: false,
    staleTime: 0,
    refetchOnMount: 'always',
  })
  const preparation = preparationQuery.data?.type === 'ok-single' ? preparationQuery.data.data : null
  const shownPreparation = useRef<string | null>(null)
  useEffect(() => {
    const next = preparation?.preparationVersion ?? null
    if (next !== null && shownPreparation.current !== null && shownPreparation.current !== next) {
      setLines(current => current.map(line => ({ ...line, accountId: '' })))
    }
    shownPreparation.current = next
  }, [preparation?.preparationVersion])

  if (!single) return <Frame title={T.draftTitle}><StateBlock state={{ kind: 'no-authorized-company' }} subject={T.draftSubject} /></Frame>

  const send = async (next: NormalJournalCommandSnapshot) => {
    if (next.operation === 'create' && !canCreate) return
    if (next.operation === 'create') storeNormalJournalCreate(attemptOwner, next)
    setSnapshot(next)
    setOutcome(null)
    const security = boundary.capture()
    const answer = await sendNormalJournalCommand(companyId, next)
    if (answer.type === 'failed') {
      if (answer.failure.kind === 'unauthenticated') boundary.report(401, security)
      if (answer.failure.kind === 'forbidden') boundary.report(403, security)
    }
    if (next.operation === 'create' && (answer.type === 'applied'
      || (answer.type === 'failed' && answer.failure.kind !== 'unauthenticated' && answer.failure.kind !== 'forbidden'))) {
      clearNormalJournalCreate(attemptOwner)
    }
    if (mountedRef.current) setOutcome(answer)
  }

  const submit = (event: React.FormEvent) => {
    event.preventDefault()
    if (!canCreate || sending || unresolved || outcome?.type === 'applied') return
    if (!GUID.test(sourceId) || !isDateOnly(transactionDate) || !isDateOnly(accountingDate)
        || (valueDate !== '' && !isDateOnly(valueDate))) return setError(T.invalidSourceAndDates)
    if (preparation === null) return setError(T.preparationRequired)
    const requestLines: CreateNormalJournalDraftLine[] = []
    for (const line of lines) {
      const amount = parseAmountInput(line.amount)
      if (!GUID.test(line.accountId) || !amount.ok) return setError(T.invalidLines)
      requestLines.push({ accountId: line.accountId, side: line.side, amount: amount.wire })
    }
    const request: CreateNormalJournalDraftRequest = {
      draftId: newId(), sourceId, transactionDate, accountingDate, valueDate: valueDate || null,
      expectedPreparationVersion: preparation.preparationVersion, lines: requestLines,
    }
    setError(null)
    void send({ operation: 'create', idempotencyKey: newId(), request })
  }

  if (outcome?.type === 'applied') {
    return <Frame title={T.createdTitle}>
      <p role="status" className={styles.noticeOk}>{normalDraftSavedText(outcome.draft.version)}</p>
      <div className={styles.actions}>
        <Link to={normalDraftDetailPath(outcome.draft.draftId)}>{T.reopen}</Link>
        {hasCapability(shell, ACCOUNTING_CAPABILITY.normalDraftApprove)
          ? <Link to={normalDraftApprovalPath(outcome.draft.draftId)}>{T.openApproval}</Link> : null}
      </div>
    </Frame>
  }

  return <Frame title={T.createTitle}>
    <p className={styles.help}>{T.createHelp}</p>
    {outcome?.type === 'unknown' ? <div role="alert" className={styles.noticeWarn}>{T.createUnknown}<button type="button" onClick={() => snapshot && void send(snapshot)}>{T.resend}</button></div> : null}
    {outcome?.type === 'failed' ? <p role="alert" className={styles.error}>{normalJournalCreateFailureText(outcome.failure)}</p> : null}
    <form className={styles.page} noValidate onSubmit={submit}>
      <div className={styles.form}>
        <div className={styles.control}><label htmlFor="normal-journal-source">{T.source}</label><select id="normal-journal-source" value={sourceId} disabled={sending || unresolved || sourceListState.kind !== 'ready'} onChange={event => {
          setSourceId(event.target.value)
          setSnapshot(null)
          setOutcome(null)
          setError(null)
          setLines(current => current.map(line => ({ ...line, accountId: '' })))
        }}><option value="">{T.chooseSource}</option>{authorizedSources.map(source => <option key={source.id} value={source.id}>{sourceLabel(source)}</option>)}</select></div>
        <label className={styles.control}>{T.transactionDate}<input type="date" value={transactionDate} disabled={sending || unresolved} onChange={event => setTransactionDate(event.target.value)} /></label>
        <label className={styles.control}>{T.accountingDate}<input type="date" value={accountingDate} disabled={sending || unresolved} onChange={event => setAccountingDate(event.target.value)} /></label>
        <label className={styles.control}>{T.valueDate}<input type="date" value={valueDate} disabled={sending || unresolved} onChange={event => setValueDate(event.target.value)} /></label>
      </div>
      {sourceListState.kind !== 'ready' ? <StateBlock state={sourceListState} subject={T.sourcesSubject} onRetry={() => void sourceListQuery.refetch()} /> : null}
      {sourceIsValid && preparationQuery.isPending ? <StateBlock state={{ kind: 'initial-loading' }} subject={T.preparationSubject} /> : null}
      {sourceIsValid && preparationQuery.data !== undefined && preparationQuery.data.type !== 'ok-single'
        ? <p role="alert" className={styles.error}>{T.preparationUnreadable}</p> : null}
      {preparation ? <p className={styles.help}>{normalPreparationVersionsText(preparation.accountPlanSourceVersion, preparation.postingRuleVersion)}</p> : null}
      {lines.map((line, index) => <div className={styles.form} key={index}>
        <div className={styles.control}><label htmlFor={`normal-journal-account-${index}`}>{T.account}</label><select id={`normal-journal-account-${index}`} value={line.accountId === '' ? '' : `${line.side}:${line.accountId}`} disabled={sending || unresolved || preparation === null} onChange={event => {
          const mapping = preparation?.mappings.find(candidate => `${candidate.side}:${candidate.accountId}` === event.target.value)
          setLines(current => current.map((item, i) => i === index
            ? { ...item, accountId: mapping?.accountId ?? '', side: mapping?.side ?? item.side } : item))
        }}><option value="">{T.chooseAccount}</option>{preparation?.mappings.map(mapping => <option key={`${mapping.side}:${mapping.accountId}`} value={`${mapping.side}:${mapping.accountId}`}>{mapping.accountCode} - {mapping.accountName} ({mapping.side === 'Debit' ? T.debit : T.credit})</option>)}</select></div>
        <label className={styles.control}>{T.amount}<input inputMode="decimal" value={line.amount} disabled={sending || unresolved} onChange={event => setLines(current => current.map((item, i) => i === index ? { ...item, amount: event.target.value } : item))} /></label>
        {lines.length > 2 ? <button type="button" disabled={sending || unresolved} onClick={() => setLines(current => current.filter((_, i) => i !== index))}>{T.removeLine}</button> : null}
      </div>)}
      <div className={styles.actions}><button type="button" disabled={sending || unresolved || preparation === null} onClick={() => setLines(current => [...current, { accountId: '', side: 'Debit', amount: '' }])}>{T.addLine}</button><button type="submit" disabled={sending || unresolved || preparation === null}>{T.saveDraft}</button><Link to={ACCOUNTING_ROUTE.journalsList}>{T.closeForm}</Link></div>
      {outcome?.type === 'failed' && outcome.failure.kind === 'preparation-version-conflict'
        ? <p role="alert" className={styles.noticeWarn}>{T.preparationChanged}<button type="button" onClick={() => void preparationQuery.refetch()}>{T.refreshPreparation}</button></p> : null}
      {error ? <p role="alert" className={styles.error}>{error}</p> : null}
    </form>
  </Frame>
}

export function NormalJournalDraftPage({ mode }: { readonly mode: 'own' | 'approve' }) {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const { draftId = '' } = useParams()
  return <NormalJournalDraftDetail key={`${namespace ?? 'none'}:${shell.routeEntryGeneration}:${draftId}:${mode}`} namespace={namespace} draftId={draftId} mode={mode} />
}

function NormalJournalDraftDetail({ namespace, draftId, mode }: { readonly namespace: string | null; readonly draftId: string; readonly mode: 'own' | 'approve' }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const mountedRef = useRef(true)
  useEffect(() => {
    mountedRef.current = true
    return () => {
      mountedRef.current = false
    }
  }, [])
  const companyId = shell.activeContext?.companyId ?? null
  const single = shell.activeContext?.scopeMode === 'single' && companyId !== null
  const [periodId, setPeriodId] = useState('')
  // BLG-05: bumped after a period refusal, so the open periods are read again and the stale choice is cleared.
  const [periodReload, setPeriodReload] = useState(0)
  const [snapshot, setSnapshot] = useState<NormalJournalCommandSnapshot | null>(null)
  const [outcome, setOutcome] = useState<NormalJournalCommandOutcome | null>(null)
  const inFlight = useRef(false)
  const query = useQuery({
    // Namespace first: the shell drops every query of a fenced context by `queryKey[0]`.
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'accounting', 'normal-journal-draft', draftId],
    enabled: single && GUID.test(draftId),
    queryFn: async ({ signal }) => {
      const captured = boundary.capture()
      const answer = await fetchNormalJournalDraft(companyId ?? '', draftId, signal)
      if (answer.type === 'http' && (answer.status === 401 || answer.status === 403)) boundary.report(answer.status, captured)
      return answer
    },
  })
  if (!single) return <Frame title={T.draftTitle}><StateBlock state={{ kind: 'no-authorized-company' }} subject={T.draftSubject} /></Frame>
  if (!GUID.test(draftId)) return <Frame title={T.draftTitle}><StateBlock state={{ kind: 'not-found' }} subject={T.draftSubject} /></Frame>
  const remote = (query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })) as RemoteViewState<NormalJournalDraft>
  if (remote.kind !== 'ready' && outcome?.type !== 'applied') return <Frame title={T.draftTitle}><StateBlock state={remote} subject={T.draftSubject} onRetry={() => void query.refetch()} /></Frame>
  const draft = outcome?.type === 'applied' ? outcome.draft : (remote as { kind: 'ready'; data: NormalJournalDraft }).data
  const sending = snapshot !== null && outcome === null
  const unresolved = outcome?.type === 'unknown'
  const send = async (next: NormalJournalCommandSnapshot, retry = false) => {
    if (inFlight.current || (unresolved && !retry)) return
    inFlight.current = true
    setSnapshot(next)
    setOutcome(null)
    try {
      const security = boundary.capture()
      const answer = await sendNormalJournalCommand(companyId, next)
      if (answer.type === 'failed') {
        if (answer.failure.kind === 'unauthenticated') boundary.report(401, security)
        if (answer.failure.kind === 'forbidden') boundary.report(403, security)
      }
      if (mountedRef.current) {
        setOutcome(answer)
        if (answer.type === 'failed' && answer.failure.kind === 'rule' && NORMAL_JOURNAL_PERIOD_CODES.has(answer.failure.code)) {
          setPeriodId('')
          setPeriodReload((current) => current + 1)
        }
      }
    } finally {
      inFlight.current = false
    }
  }
  const approve = () => {
    if (inFlight.current || unresolved) return
    // The period comes from the open-period picker; an empty or foreign value never leaves the client.
    if (!GUID.test(periodId)) return setOutcome({ type: 'failed', failure: { kind: 'validation', field: 'periodId' } })
    void send({ operation: 'approve', idempotencyKey: newId(), draftId, expectedVersion: draft.version, periodId })
  }
  return <Frame title={T.draftTitle}>
    <dl className={styles.form}><div className={styles.control}><dt>{T.status}</dt><dd>{draft.status}</dd></div><div className={styles.control}><dt>{T.version}</dt><dd>{draft.version}</dd></div><div className={styles.control}><dt>{T.sourceRef}</dt><dd>{normalSourceRefText(draft.sourceId)}</dd></div><div className={styles.control}><dt>{T.postingRule}</dt><dd>{draft.postingRuleVersion}</dd></div></dl>
    <NormalJournalDraftLines namespace={namespace ?? 'no-context'} companyId={companyId ?? ''} draft={draft} />
    {outcome?.type === 'unknown' ? <div role="alert" className={styles.noticeWarn}>{T.approvalUnknown}<button type="button" onClick={() => snapshot && void send(snapshot, true)}>{T.resend}</button></div> : null}
    {outcome?.type === 'failed' ? <p role="alert" className={styles.error}>{normalJournalApprovalFailureText(outcome.failure)}</p> : null}
    {mode === 'approve' && draft.status === 'Draft' ? <div className={styles.actions}><NormalJournalApprovalPeriod key={periodReload} reloadToken={periodReload} namespace={namespace ?? 'no-context'} companyId={companyId ?? ''} accountingDate={draft.accountingDate} value={periodId} onChange={setPeriodId} disabled={sending || unresolved} /><button type="button" disabled={sending || unresolved || periodId === ''} onClick={approve}>{T.approve}</button></div> : null}
    <div className={styles.actions}>{draft.status === 'Approved' && draft.postedJournalId && hasCapability(shell, ACCOUNTING_CAPABILITY.journalsDetail) ? <Link to={journalDetailPath(draft.postedJournalId)}>{T.openPostedJournal}</Link> : null}{mode === 'own' && hasCapability(shell, ACCOUNTING_CAPABILITY.normalDraftApprove) ? <Link to={normalDraftApprovalPath(draftId)}>{T.openApproval}</Link> : null}<Link to={ACCOUNTING_ROUTE.journalsList}>{T.backToJournals}</Link></div>
  </Frame>
}

function Frame({ title, children }: { readonly title: string; readonly children: React.ReactNode }) {
  return <section className={styles.page}><h1>{title}</h1>{children}</section>
}

/**
 * BLG-05b: the draft lines with each account named by the draft's own frozen account plan (code + name), never
 * as a technical id. While the names load the cell says so; when the plan does not hold an account, or the read
 * fails, the cell says the name is unavailable. A 401/403 goes to the shell boundary like every read.
 */
function NormalJournalDraftLines({ namespace, companyId, draft }: {
  readonly namespace: string
  readonly companyId: string
  readonly draft: NormalJournalDraft
}) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const query = useQuery({
    queryKey: [namespace, shell.routeEntryGeneration, 'accounting', 'normal-journal-draft-accounts', draft.draftId, draft.version],
    retry: false,
    queryFn: async ({ signal }) => {
      const captured = boundary.capture()
      const answer = await fetchNormalJournalDraftAccounts(companyId, draft, signal)
      if (answer.type === 'http' && (answer.status === 401 || answer.status === 403)) boundary.report(answer.status, captured)
      return answer
    },
  })
  const named = query.data?.type === 'ok-single' ? query.data.data.accounts : null
  const accountText = (lineNumber: number) => {
    if (query.isPending) return T.accountLoading
    const account = named?.find(candidate => candidate.lineNumber === lineNumber)
    return account?.accountCode != null && account.accountName != null
      ? accountLabel(account.accountCode, account.accountName)
      : T.accountUnavailable
  }
  return <div className={styles.tableWrap}><table className={styles.table}><thead><tr><th>{T.lineNumber}</th><th>{T.lineAccount}</th><th>{T.lineSide}</th><th className={styles.amount}>{T.amount}</th></tr></thead><tbody>{draft.lines.map(line => <tr key={line.lineNumber}><td>{line.lineNumber}</td><td>{accountText(line.lineNumber)}</td><td>{line.side === 'Debit' ? T.debit : T.credit}</td><td className={styles.amount}>{line.amount}</td></tr>)}</tbody></table></div>
}
