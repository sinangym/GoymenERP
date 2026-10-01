import { useEffect, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { parseAmountInput } from '../finance/decimalText'
import { isDateOnly } from '../finance/dateOnly'
import { decodeReadResult, decodeSourcePage, type SourcePage, type SourceSummary } from '../finance/contract'
import { useFinanceRead } from '../finance/financePort'
import type { FinanceReceiptPort, FinanceScope, PortReply } from '../finance/port'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { fetchNormalJournalDraft } from './api'
import { fetchNormalJournalDraftSource } from './normalJournalSourceApi'
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
import styles from './Accounting.module.css'

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const newId = () => crypto.randomUUID()
type LineForm = { readonly accountId: string; readonly side: NormalJournalSide; readonly amount: string }

function sourceLabel(source: SourceSummary): string {
  const number = GUID.test(source.sourceNumber) ? `Kaynak ref. ...${source.id.slice(-8)}` : source.sourceNumber
  return `${number} - ${source.occurredOn}`
}

async function readAllAuthorizedSources(
  port: FinanceReceiptPort,
  scope: FinanceScope,
  signal: AbortSignal,
): Promise<PortReply> {
  const items: SourceSummary[] = []
  const sourceIds = new Set<string>()
  const cursors = new Set<string>()
  let cursor: string | null = null
  while (true) {
    const reply = await port.listSources(scope, { cursor }, signal)
    if (reply.kind === 'no-reply') return reply
    const decoded = decodeReadResult(reply.body, resource => decodeSourcePage(resource, scope.companyId))
    if (!decoded.ok) return { kind: 'body', body: undefined }
    if (decoded.value.outcome !== 'Found') return reply
    for (const source of decoded.value.resource.items) {
      if (sourceIds.has(source.id)) return { kind: 'body', body: undefined }
      sourceIds.add(source.id)
      items.push(source)
    }
    const next = decoded.value.resource.nextCursor
    if (next === null) break
    if (cursors.has(next)) return { kind: 'body', body: undefined }
    cursors.add(next)
    cursor = next
  }
  const resource: SourcePage = { companyId: scope.companyId, items, nextCursor: null }
  return { kind: 'body', body: { outcome: 'Found', resource } }
}

export function NormalJournalDraftCreatePage() {
  const shell = useShellState()
  const key = contextKey(shell)
  if (key === null) {
    return <StateBlock state={{ kind: 'initial-loading' }} subject="muhasebe şirket bağlamı" />
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
  const sourceList = useFinanceRead('normal-journal-source-picker', [],
    readAllAuthorizedSources, decodeSourcePage)
  const authorizedSources = sourceList.state.kind === 'ready'
    ? sourceList.state.data.items.filter(source => source.stage === 'Recorded') : []
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

  if (!single) return <Frame title="Normal fiş taslağı"><StateBlock state={{ kind: 'no-authorized-company' }} subject="normal fiş taslağı" /></Frame>

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
        || (valueDate !== '' && !isDateOnly(valueDate))) return setError('Kaynak ve tarihler geçerli olmalıdır.')
    if (preparation === null) return setError('Kaynağın onaylı hesap hazırlığı yüklenmelidir.')
    const requestLines: CreateNormalJournalDraftLine[] = []
    for (const line of lines) {
      const amount = parseAmountInput(line.amount)
      if (!GUID.test(line.accountId) || !amount.ok) return setError('Her satırda geçerli hesap, taraf ve pozitif tutar gerekir.')
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
    return <Frame title="Normal fiş taslağı kaydedildi">
      <p role="status" className={styles.noticeOk}>Taslak sürüm {outcome.draft.version} olarak kaydedildi.</p>
      <div className={styles.actions}>
        <Link to={normalDraftDetailPath(outcome.draft.draftId)}>Kaydı kapat ve yeniden aç</Link>
        {hasCapability(shell, ACCOUNTING_CAPABILITY.normalDraftApprove)
          ? <Link to={normalDraftApprovalPath(outcome.draft.draftId)}>Onay ekranını aç</Link> : null}
      </div>
    </Frame>
  }

  return <Frame title="Yeni normal fiş taslağı">
    <p className={styles.help}>Hesaplar yalnız sunucudaki onaylı Mikro aynası ve posting rule ile doğrulanır. Sağlayıcı hazır değilse kayıt yapılmaz.</p>
    {outcome?.type === 'unknown' ? <div role="alert" className={styles.noticeWarn}>Sonuç bilinmiyor. Aynı anahtar ve aynı içerikle yeniden gönderin.<button type="button" onClick={() => snapshot && void send(snapshot)}>Aynı isteği yeniden gönder</button></div> : null}
    {outcome?.type === 'failed' ? <p role="alert" className={styles.error}>İstek reddedildi: {outcome.failure.kind}</p> : null}
    <form className={styles.page} noValidate onSubmit={submit}>
      <div className={styles.form}>
        <label className={styles.control}>Onaylı finans kaynağı<select value={sourceId} disabled={sending || unresolved || sourceList.state.kind !== 'ready'} onChange={event => {
          setSourceId(event.target.value)
          setSnapshot(null)
          setOutcome(null)
          setError(null)
          setLines(current => current.map(line => ({ ...line, accountId: '' })))
        }}><option value="">Kaynak seçin</option>{authorizedSources.map(source => <option key={source.id} value={source.id}>{sourceLabel(source)}</option>)}</select></label>
        <label className={styles.control}>İşlem tarihi<input type="date" value={transactionDate} disabled={sending || unresolved} onChange={event => setTransactionDate(event.target.value)} /></label>
        <label className={styles.control}>Muhasebe tarihi<input type="date" value={accountingDate} disabled={sending || unresolved} onChange={event => setAccountingDate(event.target.value)} /></label>
        <label className={styles.control}>Valör tarihi<input type="date" value={valueDate} disabled={sending || unresolved} onChange={event => setValueDate(event.target.value)} /></label>
      </div>
      {sourceList.state.kind !== 'ready' ? <StateBlock state={sourceList.state} subject="yetkili finans kaynakları" onRetry={sourceList.refetch} /> : null}
      {sourceIsValid && preparationQuery.isPending ? <StateBlock state={{ kind: 'initial-loading' }} subject="onaylı hesap hazırlığı" /> : null}
      {sourceIsValid && preparationQuery.data !== undefined && preparationQuery.data.type !== 'ok-single'
        ? <p role="alert" className={styles.error}>Kaynağın onaylı hesap hazırlığı okunamadı.</p> : null}
      {preparation ? <p className={styles.help}>Onaylı ayna {preparation.accountPlanSourceVersion}; kural {preparation.postingRuleVersion}.</p> : null}
      {lines.map((line, index) => <div className={styles.form} key={index}>
        <label className={styles.control}>Onaylı hesap<select value={line.accountId === '' ? '' : `${line.side}:${line.accountId}`} disabled={sending || unresolved || preparation === null} onChange={event => {
          const mapping = preparation?.mappings.find(candidate => `${candidate.side}:${candidate.accountId}` === event.target.value)
          setLines(current => current.map((item, i) => i === index
            ? { ...item, accountId: mapping?.accountId ?? '', side: mapping?.side ?? item.side } : item))
        }}><option value="">Hesap seçin</option>{preparation?.mappings.map(mapping => <option key={`${mapping.side}:${mapping.accountId}`} value={`${mapping.side}:${mapping.accountId}`}>{mapping.accountCode} - {mapping.accountName} ({mapping.side === 'Debit' ? 'Borç' : 'Alacak'})</option>)}</select></label>
        <label className={styles.control}>Tutar<input inputMode="decimal" value={line.amount} disabled={sending || unresolved} onChange={event => setLines(current => current.map((item, i) => i === index ? { ...item, amount: event.target.value } : item))} /></label>
        {lines.length > 2 ? <button type="button" disabled={sending || unresolved} onClick={() => setLines(current => current.filter((_, i) => i !== index))}>Satırı kaldır</button> : null}
      </div>)}
      <div className={styles.actions}><button type="button" disabled={sending || unresolved || preparation === null} onClick={() => setLines(current => [...current, { accountId: '', side: 'Debit', amount: '' }])}>Satır ekle</button><button type="submit" disabled={sending || unresolved || preparation === null}>Taslağı kaydet</button><Link to={ACCOUNTING_ROUTE.journalsList}>Formu kapat</Link></div>
      {outcome?.type === 'failed' && outcome.failure.kind === 'preparation-version-conflict'
        ? <p role="alert" className={styles.noticeWarn}>Onaylı hesap hazırlığı değişti. Seçimleri yenileyin.<button type="button" onClick={() => void preparationQuery.refetch()}>Hazırlığı yenile</button></p> : null}
      {error ? <p role="alert" className={styles.error}>{error}</p> : null}
    </form>
  </Frame>
}

export function NormalJournalDraftPage({ mode }: { readonly mode: 'own' | 'approve' }) {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const { draftId = '' } = useParams()
  return <NormalJournalDraftDetail key={`${namespace ?? 'none'}:${shell.routeEntryGeneration}:${draftId}:${mode}`} draftId={draftId} mode={mode} />
}

function NormalJournalDraftDetail({ draftId, mode }: { readonly draftId: string; readonly mode: 'own' | 'approve' }) {
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
  const [snapshot, setSnapshot] = useState<NormalJournalCommandSnapshot | null>(null)
  const [outcome, setOutcome] = useState<NormalJournalCommandOutcome | null>(null)
  const inFlight = useRef(false)
  const query = useQuery({
    queryKey: [companyId, shell.routeEntryGeneration, 'normal-journal-draft', draftId],
    enabled: single && GUID.test(draftId),
    queryFn: async ({ signal }) => {
      const captured = boundary.capture()
      const answer = await fetchNormalJournalDraft(companyId ?? '', draftId, signal)
      if (answer.type === 'http' && (answer.status === 401 || answer.status === 403)) boundary.report(answer.status, captured)
      return answer
    },
  })
  if (!single) return <Frame title="Normal fiş taslağı"><StateBlock state={{ kind: 'no-authorized-company' }} subject="normal fiş taslağı" /></Frame>
  if (!GUID.test(draftId)) return <Frame title="Normal fiş taslağı"><StateBlock state={{ kind: 'not-found' }} subject="normal fiş taslağı" /></Frame>
  const remote = (query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })) as RemoteViewState<NormalJournalDraft>
  if (remote.kind !== 'ready' && outcome?.type !== 'applied') return <Frame title="Normal fiş taslağı"><StateBlock state={remote} subject="normal fiş taslağı" onRetry={() => void query.refetch()} /></Frame>
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
      if (mountedRef.current) setOutcome(answer)
    } finally {
      inFlight.current = false
    }
  }
  const approve = () => {
    if (inFlight.current || unresolved) return
    if (!GUID.test(periodId)) return setOutcome({ type: 'failed', failure: { kind: 'validation', field: 'periodId' } })
    void send({ operation: 'approve', idempotencyKey: newId(), draftId, expectedVersion: draft.version, periodId })
  }
  return <Frame title="Normal fiş taslağı">
    <dl className={styles.form}><div className={styles.control}><dt>Durum</dt><dd>{draft.status}</dd></div><div className={styles.control}><dt>Sürüm</dt><dd>{draft.version}</dd></div><div className={styles.control}><dt>Kaynak</dt><dd>Kaynak ref. ...{draft.sourceId.slice(-8)}</dd></div><div className={styles.control}><dt>Posting rule</dt><dd>{draft.postingRuleVersion}</dd></div></dl>
    <div className={styles.tableWrap}><table className={styles.table}><thead><tr><th>Satır</th><th>Hesap</th><th>Taraf</th><th className={styles.amount}>Tutar</th></tr></thead><tbody>{draft.lines.map(line => <tr key={line.lineNumber}><td>{line.lineNumber}</td><td className={styles.mono}>{line.accountId}</td><td>{line.side === 'Debit' ? 'Borç' : 'Alacak'}</td><td className={styles.amount}>{line.amount}</td></tr>)}</tbody></table></div>
    {outcome?.type === 'unknown' ? <div role="alert" className={styles.noticeWarn}>Onay sonucu bilinmiyor.<button type="button" onClick={() => snapshot && void send(snapshot, true)}>Aynı isteği yeniden gönder</button></div> : null}
    {outcome?.type === 'failed' ? <p role="alert" className={styles.error}>Onay reddedildi: {outcome.failure.kind}</p> : null}
    {mode === 'approve' && draft.status === 'Draft' ? <div className={styles.actions}><label className={styles.control}>Açık dönem kimliği<input value={periodId} disabled={sending || unresolved} onChange={event => setPeriodId(event.target.value)} /></label><button type="button" disabled={sending || unresolved} onClick={approve}>Normal taslağı onayla</button></div> : null}
    <div className={styles.actions}>{draft.status === 'Approved' && draft.postedJournalId && hasCapability(shell, ACCOUNTING_CAPABILITY.journalsDetail) ? <Link to={journalDetailPath(draft.postedJournalId)}>Oluşturulan fişi aç</Link> : null}{mode === 'own' && hasCapability(shell, ACCOUNTING_CAPABILITY.normalDraftApprove) ? <Link to={normalDraftApprovalPath(draftId)}>Onay ekranını aç</Link> : null}<Link to={ACCOUNTING_ROUTE.journalsList}>Fişlere dön</Link></div>
  </Frame>
}

function Frame({ title, children }: { readonly title: string; readonly children: React.ReactNode }) {
  return <section className={styles.page}><h1>{title}</h1>{children}</section>
}
