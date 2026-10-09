import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react'
import { useLocation, useSearchParams } from 'react-router'
import type { SecuritySnapshot } from '../shell/authBoundary'
import type {
  OfficialInvoiceApprovalReply,
  OfficialInvoiceApprovedBatch,
  OfficialInvoicePendingBatch,
  OfficialInvoicePendingDetail,
  OfficialInvoicePendingPage,
} from './officialInvoiceImportApprovalContract'
import styles from './OfficialInvoiceImportApprovals.module.css'

export interface OfficialInvoiceApprovalClient {
  listPending(cursor: string | null, signal: AbortSignal): Promise<OfficialInvoicePendingPage>
  getPending(batchId: string, signal: AbortSignal): Promise<OfficialInvoicePendingDetail>
  approve(batchId: string, expectedVersion: string, idempotencyKey: string, signal: AbortSignal): Promise<OfficialInvoiceApprovalReply>
}

export interface OfficialInvoiceApprovalAttemptContext {
  readonly sessionEpoch: number
  readonly companyId: string
  readonly actorId: string
  readonly committedContextRevision: SecuritySnapshot['committedContextRevision']
  readonly requestFenceGeneration: SecuritySnapshot['requestFenceGeneration']
  readonly capabilityGeneration: SecuritySnapshot['capabilityGeneration']
  readonly securityNamespace: SecuritySnapshot['namespace']
}

interface StoredApprovalAttempt {
  readonly key: string
  readonly batch: OfficialInvoicePendingBatch
}

const attempts = new Map<string, StoredApprovalAttempt>()

interface ApprovalPageProps {
  readonly client: OfficialInvoiceApprovalClient
  readonly context: OfficialInvoiceApprovalAttemptContext
  readonly createIdempotencyKey?: () => string
  readonly onApprovedSource: (batch: OfficialInvoiceApprovedBatch) => void
}

const APPROVAL_ID_QUERY = 'approvalId'
const APPROVAL_NAMESPACE_STATE = 'officialInvoiceApprovalNamespace'
const APPROVAL_NAMESPACE_STORAGE_PREFIX = 'goymen.finance.official-invoice-approval.'
const boundUuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const emptyUuid = '00000000-0000-0000-0000-000000000000'

export function OfficialInvoiceImportApprovalsPage(props: ApprovalPageProps) {
  const location = useLocation()
  const [searchParams, setSearchParams] = useSearchParams()
  const approvalIds = searchParams.getAll(APPROVAL_ID_QUERY)
  const selectedApprovalId = approvalIds.length === 1 && isBoundUuid(approvalIds[0]) ? approvalIds[0] : null
  const invalidSelection = approvalIds.length > 0 && selectedApprovalId === null
  const namespace = JSON.stringify([
    props.context.sessionEpoch,
    props.context.companyId,
    props.context.actorId,
    props.context.committedContextRevision,
    props.context.requestFenceGeneration,
    props.context.capabilityGeneration,
    props.context.securityNamespace,
  ])
  const selectedNamespaces = [
    approvalLocationNamespace(location.state),
    selectedApprovalId === null ? null : readApprovalNamespace(selectedApprovalId),
  ].filter((value): value is string => value !== null)
  const selectApproval = useCallback((approvalId: string) => {
    writeApprovalNamespace(approvalId, namespace)
    const next = new URLSearchParams(searchParams)
    next.set(APPROVAL_ID_QUERY, approvalId)
    setSearchParams(next, { state: approvalLocationState(location.state, namespace) })
  }, [location.state, namespace, searchParams, setSearchParams])
  const clearSelection = useCallback(() => {
    if (selectedApprovalId !== null) removeApprovalNamespace(selectedApprovalId)
    const next = new URLSearchParams(searchParams)
    next.delete(APPROVAL_ID_QUERY)
    setSearchParams(next, { replace: true, state: approvalLocationState(location.state, null) })
  }, [location.state, searchParams, selectedApprovalId, setSearchParams])
  const bindSelection = useCallback(() => {
    if (selectedApprovalId !== null) writeApprovalNamespace(selectedApprovalId, namespace)
    setSearchParams(new URLSearchParams(searchParams), {
      replace: true,
      state: approvalLocationState(location.state, namespace),
    })
  }, [location.state, namespace, searchParams, selectedApprovalId, setSearchParams])
  const previousNamespace = useRef(namespace)
  const contextChanged = previousNamespace.current !== namespace
    || selectedNamespaces.some((selectedNamespace) => selectedNamespace !== namespace)
  useLayoutEffect(() => {
    previousNamespace.current = namespace
    if (contextChanged && approvalIds.length > 0) clearSelection()
    else if (approvalIds.length > 0 && selectedNamespaces.length === 0) bindSelection()
  }, [approvalIds.length, bindSelection, clearSelection, contextChanged, namespace, selectedNamespaces.length])
  return <OfficialInvoiceImportApprovalsPageState key={namespace} {...props}
    selectedApprovalId={contextChanged ? null : selectedApprovalId}
    invalidSelection={contextChanged ? false : invalidSelection}
    onSelectApproval={selectApproval} onClearSelection={clearSelection} />
}

function approvalLocationNamespace(state: unknown): string | null {
  if (state === null || typeof state !== 'object' || Array.isArray(state)) return null
  const value = (state as Record<string, unknown>)[APPROVAL_NAMESPACE_STATE]
  return typeof value === 'string' ? value : null
}

function approvalLocationState(state: unknown, namespace: string | null): Record<string, unknown> | null {
  const next = state !== null && typeof state === 'object' && !Array.isArray(state)
    ? { ...(state as Record<string, unknown>) }
    : {}
  if (namespace === null) delete next[APPROVAL_NAMESPACE_STATE]
  else next[APPROVAL_NAMESPACE_STATE] = namespace
  return Object.keys(next).length === 0 ? null : next
}

function readApprovalNamespace(approvalId: string): string | null {
  try {
    return globalThis.sessionStorage?.getItem(`${APPROVAL_NAMESPACE_STORAGE_PREFIX}${approvalId}`) ?? null
  } catch {
    return null
  }
}

function writeApprovalNamespace(approvalId: string, namespace: string): void {
  try {
    globalThis.sessionStorage?.setItem(`${APPROVAL_NAMESPACE_STORAGE_PREFIX}${approvalId}`, namespace)
  } catch {
    // Browser storage is an additional stale-link fence; API authority remains mandatory.
  }
}

function removeApprovalNamespace(approvalId: string): void {
  try {
    globalThis.sessionStorage?.removeItem(`${APPROVAL_NAMESPACE_STORAGE_PREFIX}${approvalId}`)
  } catch {
    // Browser storage is an additional stale-link fence; API authority remains mandatory.
  }
}

function OfficialInvoiceImportApprovalsPageState({ client, context,
  createIdempotencyKey = () => crypto.randomUUID(), onApprovedSource,
  selectedApprovalId, invalidSelection, onSelectApproval, onClearSelection,
}: ApprovalPageProps & {
  readonly selectedApprovalId: string | null
  readonly invalidSelection: boolean
  readonly onSelectApproval: (approvalId: string) => void
  readonly onClearSelection: () => void
}) {
  const [page, setPage] = useState<OfficialInvoicePendingPage | null>(null)
  const [detail, setDetail] = useState<OfficialInvoicePendingDetail | null>(null)
  const [settled, setSettled] = useState<OfficialInvoiceApprovedBatch | null>(null)
  const [phase, setPhase] = useState<'loading' | 'ready' | 'detail' | 'submitting' | 'unknown' | 'conflicted' | 'refused' | 'failed' | 'invalid-selection' | 'detail-failed'>('loading')
  const [issues, setIssues] = useState<readonly { readonly field: string; readonly code: string }[]>([])
  const request = useRef(0)
  const activeRequest = useRef<AbortController | null>(null)
  const selectedListIdentity = useRef<{ readonly batchId: string; readonly version: string } | null>(null)
  const namespace = JSON.stringify([
    context.sessionEpoch,
    context.companyId,
    context.actorId,
    context.committedContextRevision,
    context.requestFenceGeneration,
    context.capabilityGeneration,
    context.securityNamespace,
  ])
  const actorScope = JSON.stringify([context.sessionEpoch, context.companyId, context.actorId])
  const [recovery, setRecovery] = useState<StoredApprovalAttempt | null>(() => findAttempt(actorScope))

  const beginRequest = useCallback(() => {
    activeRequest.current?.abort()
    const controller = new AbortController()
    activeRequest.current = controller
    return { controller, generation: ++request.current }
  }, [])

  const fetchPage = useCallback((cursor: string | null = null) => {
    const { controller, generation } = beginRequest()
    void client.listPending(cursor, controller.signal).then((value) => {
      if (generation !== request.current || value.companyId !== context.companyId) return
      setPage(value)
      setPhase('ready')
    }).catch(() => { if (generation === request.current) setPhase('failed') })
    return () => controller.abort()
  }, [beginRequest, client, context.companyId])

  const fetchDetail = useCallback((batchId: string) => {
    const { controller, generation } = beginRequest()
    setDetail(null)
    setIssues([])
    setPhase('loading')
    void client.getPending(batchId, controller.signal).then((value) => {
      if (generation !== request.current) return
      const selectedFromList = selectedListIdentity.current?.batchId === batchId
        ? selectedListIdentity.current
        : null
      if (value.batch.companyId !== context.companyId || value.batch.batchId !== batchId
        || (selectedFromList !== null && value.batch.version !== selectedFromList.version)) {
        setPhase('detail-failed')
        return
      }
      setDetail(value)
      const outstanding = attempts.get(attemptSlot(actorScope, value.batch.batchId))
      setRecovery(outstanding ?? null)
      setPhase(outstanding === undefined ? 'detail' : 'unknown')
    }).catch(() => { if (generation === request.current) setPhase('detail-failed') })
    return () => controller.abort()
  }, [actorScope, beginRequest, client, context.companyId])

  useEffect(() => {
    if (invalidSelection) {
      request.current++
      activeRequest.current?.abort()
      setDetail(null)
      setIssues([])
      setPhase('invalid-selection')
      return
    }
    if (selectedApprovalId === null) {
      selectedListIdentity.current = null
      setDetail(null)
      setIssues([])
      setPhase('loading')
      return fetchPage()
    }
    return fetchDetail(selectedApprovalId)
  }, [fetchDetail, fetchPage, invalidSelection, namespace, selectedApprovalId])
  useEffect(() => () => {
    request.current++
    activeRequest.current?.abort()
  }, [])

  function approve(retry: boolean) {
    if (detail === null || detail.batch.uploadedBy === context.actorId) return
    const frozen = detail.batch
    const keyScope = attemptSlot(actorScope, frozen.batchId)
    const existing = attempts.get(keyScope)
    const attempt = existing ?? {
      key: createIdempotencyKey(),
      batch: frozen,
    }
    if (retry && existing === undefined) return
    attempts.set(keyScope, attempt)
    setRecovery(attempt)
    const { controller, generation } = beginRequest()
    setPhase('submitting')
    void client.approve(attempt.batch.batchId, attempt.batch.version, attempt.key, controller.signal).then((reply) => {
      if (generation !== request.current) return
      if (reply.kind === 'unknown') { setPhase('unknown'); return }
      if (reply.kind === 'conflicted') { attempts.delete(keyScope); setRecovery(null); setPhase('conflicted'); return }
      if (reply.kind === 'refused') {
        attempts.delete(keyScope); setRecovery(null); setIssues(reply.issues); setPhase('refused'); return
      }
      if (!sameApprovedIdentity(attempt.batch, reply.batch, context)) {
        setPhase('unknown')
        return
      }
      attempts.delete(keyScope)
      setRecovery(null)
      setSettled(reply.batch)
      setDetail(null)
      setPhase('loading')
      onClearSelection()
    }).catch(() => { if (generation === request.current) setPhase('unknown') })
  }

  function retryRecovery(attempt: StoredApprovalAttempt) {
    const keyScope = attemptSlot(actorScope, attempt.batch.batchId)
    const current = attempts.get(keyScope)
    if (current === undefined || current.key !== attempt.key) return
    const { controller, generation } = beginRequest()
    setPhase('submitting')
    void client.approve(current.batch.batchId, current.batch.version, current.key, controller.signal).then((reply) => {
      if (generation !== request.current) return
      if (reply.kind === 'unknown') { setPhase('unknown'); return }
      if (reply.kind === 'conflicted') { attempts.delete(keyScope); setRecovery(null); setPhase('conflicted'); return }
      if (reply.kind === 'refused') {
        attempts.delete(keyScope); setRecovery(null); setIssues(reply.issues); setPhase('failed'); return
      }
      if (!sameApprovedIdentity(current.batch, reply.batch, context)) { setPhase('unknown'); return }
      attempts.delete(keyScope)
      setRecovery(null)
      setSettled(reply.batch)
      setDetail(null)
      setPhase('loading')
      onClearSelection()
    }).catch(() => { if (generation === request.current) setPhase('unknown') })
  }

  function returnToQueue() {
    request.current++
    activeRequest.current?.abort()
    selectedListIdentity.current = null
    setDetail(null)
    setIssues([])
    setPhase('loading')
    onClearSelection()
  }

  return (
    <section className={styles.page} aria-labelledby="official-invoice-approvals-title">
      <h1 id="official-invoice-approvals-title">Resmî fatura aktarım onayları</h1>
      <p className={styles.notice}>Yükleyen kişi kendi dosyasını onaylayamaz. Onay yalnız değişmez kaynak snapshot'ını yayımlar.</p>
      {settled === null ? null : (
        <article className={styles.command} aria-label={`Onaylanan fatura kaynağı ${settled.batchId}`}>
          <p role="status">Kaynak onaylandı. Sürüm {settled.version}.</p>
          <button type="button" onClick={() => onApprovedSource(settled)}>Onaylanan kaynağı aktar</button>
        </article>
      )}
      {settled === null && detail === null && recovery !== null ? (
        <article className={styles.command} aria-label={`Sonucu bilinmeyen fatura onayı ${recovery.batch.batchId}`}>
          <p role="status">Önceki onayın sonucu bilinmiyor; pending listesine güvenmeden aynı anahtarla yeniden okuyun.</p>
          <button type="button" disabled={phase === 'submitting'} onClick={() => retryRecovery(recovery)}>
            Aynı onay anahtarıyla sonucu yeniden oku
          </button>
        </article>
      ) : null}
      {phase === 'loading' ? <p role="status">Onay bekleyen kaynaklar yükleniyor…</p> : null}
      {phase === 'invalid-selection' ? <div role="alert"><p>Bağlantıdaki onay kaydı geçersiz.</p>
        <button type="button" onClick={returnToQueue}>Onay kuyruğuna dön</button></div> : null}
      {phase === 'detail-failed' ? <div role="alert"><p>Seçilen onay kaydı güvenli biçimde açılamadı.</p>
        <button type="button" onClick={returnToQueue}>Onay kuyruğuna dön</button></div> : null}
      {phase === 'failed' ? <div role="alert"><p>Onay kuyruğu güvenli biçimde okunamadı.</p>
        <button type="button" onClick={() => { setDetail(null); setPhase('loading'); fetchPage() }}>Kuyruğu yeniden oku</button></div> : null}
      {detail === null && phase === 'ready' && page?.items.length === 0 ? <p role="status">Onay bekleyen kaynak yok.</p> : null}
      {detail === null && phase === 'ready' ? page?.items.map((batch) => (
        <article className={styles.command} key={batch.batchId}>
          <h2>{batch.batchVersion}</h2>
          <p>{batch.rowCount} taksit, yükleyen: {batch.uploadedBy}</p>
          <button type="button" onClick={() => {
            selectedListIdentity.current = { batchId: batch.batchId, version: batch.version }
            onSelectApproval(batch.batchId)
          }}>İncele</button>
        </article>
      )) : null}
      {detail === null && phase === 'ready' && page?.nextCursor !== null && page?.nextCursor !== undefined ? (
        <button type="button" onClick={() => { setPhase('loading'); fetchPage(page.nextCursor) }}>Sonraki sayfa</button>
      ) : null}
      {detail === null ? null : (
        <article className={styles.command} aria-label={`Fatura kaynağı ${detail.batch.batchId}`}>
          <h2>{detail.batch.batchVersion}</h2>
          <p>SHA-256: {detail.batch.batchChecksum}</p>
          <div className={styles.tableRegion}>
          <table className={styles.table}><thead><tr><th>Belge</th><th>Taksit</th><th>Tutar</th><th>Vade</th></tr></thead>
            <tbody>{detail.preview.batch.rows.map((row) => <tr key={row.openItemId}>
              <td data-label="Belge">{row.sourceDocumentId}</td>
              <td data-label="Taksit">{row.sourceInstallmentId}</td>
              <td data-label="Tutar">{row.amount}</td>
              <td data-label="Vade">{row.dueOn ?? 'Yok'}</td>
            </tr>)}</tbody></table>
          </div>
          {detail.batch.uploadedBy === context.actorId ? <p role="status">Bu kaynağı siz yüklediniz; başka bir yetkili onaylamalı.</p> : null}
          {phase === 'unknown' ? <div role="status"><p>Sonuç bilinmiyor; aynı anahtarla yeniden gönderin.</p>
            <button type="button" onClick={() => approve(true)}>Aynı anahtarla yeniden gönder</button></div> : null}
          {phase === 'conflicted' ? <div role="status"><p>Kayıt değişti; ayrıntıyı yeniden okuyun.</p>
            <button type="button" onClick={() => fetchDetail(detail.batch.batchId)}>Kaydı yeniden oku</button></div> : null}
          {phase === 'refused' ? <div role="status">{issues.map((issue) => <p key={`${issue.field}-${issue.code}`}>{issue.code}</p>)}</div> : null}
          {phase === 'submitting' ? <p role="status">Onay gönderiliyor…</p> : null}
          {phase === 'detail' || phase === 'refused' ? <button type="button"
            disabled={detail.batch.uploadedBy === context.actorId} onClick={() => approve(false)}>Onayla</button> : null}
          {phase !== 'submitting' && phase !== 'unknown' ? (
            <button type="button" onClick={returnToQueue}>Onay kuyruğuna dön</button>
          ) : null}
        </article>
      )}
    </section>
  )
}

function isBoundUuid(value: string | undefined): value is string {
  return value !== undefined && boundUuid.test(value) && value.toLowerCase() !== emptyUuid
}

function sameApprovedIdentity(pending: OfficialInvoicePendingBatch, approved: OfficialInvoiceApprovedBatch,
  context: OfficialInvoiceApprovalAttemptContext): boolean {
  return approved.batchId === pending.batchId
    && approved.companyId === context.companyId
    && approved.companyId === pending.companyId
    && approved.batchVersion === pending.batchVersion
    && approved.batchChecksum === pending.batchChecksum
    && approved.templateVersion === pending.templateVersion
    && approved.rowCount === pending.rowCount
    && approved.uploadedBy === pending.uploadedBy
    && approved.version === '2'
    && approved.approvedBy === context.actorId
}

function attemptSlot(actorScope: string, batchId: string): string {
  return `${actorScope}:${batchId}`
}

function findAttempt(actorScope: string): StoredApprovalAttempt | null {
  const prefix = `${actorScope}:`
  for (const [slot, attempt] of attempts) {
    if (slot.startsWith(prefix)) return attempt
  }
  return null
}
