import { useCallback, useEffect, useRef, useState } from 'react'
import type {
  OfficialInvoiceApprovalReply,
  OfficialInvoiceApprovedBatch,
  OfficialInvoicePendingBatch,
  OfficialInvoicePendingDetail,
  OfficialInvoicePendingPage,
} from './officialInvoiceImportApprovalContract'
import styles from './Finance.module.css'

export interface OfficialInvoiceApprovalClient {
  listPending(cursor: string | null, signal: AbortSignal): Promise<OfficialInvoicePendingPage>
  getPending(batchId: string, signal: AbortSignal): Promise<OfficialInvoicePendingDetail>
  approve(batchId: string, expectedVersion: string, idempotencyKey: string, signal: AbortSignal): Promise<OfficialInvoiceApprovalReply>
}

export interface OfficialInvoiceApprovalAttemptContext {
  readonly sessionEpoch: number
  readonly companyId: string
  readonly actorId: string
}

const attempts = new Map<string, string>()

interface ApprovalPageProps {
  readonly client: OfficialInvoiceApprovalClient
  readonly context: OfficialInvoiceApprovalAttemptContext
  readonly createIdempotencyKey?: () => string
  readonly onApprovedSource: (batch: OfficialInvoiceApprovedBatch) => void
}

export function OfficialInvoiceImportApprovalsPage(props: ApprovalPageProps) {
  const namespace = JSON.stringify([
    props.context.sessionEpoch,
    props.context.companyId,
    props.context.actorId,
  ])
  return <OfficialInvoiceImportApprovalsPageState key={namespace} {...props} />
}

function OfficialInvoiceImportApprovalsPageState({ client, context,
  createIdempotencyKey = () => crypto.randomUUID(), onApprovedSource }: ApprovalPageProps) {
  const [page, setPage] = useState<OfficialInvoicePendingPage | null>(null)
  const [detail, setDetail] = useState<OfficialInvoicePendingDetail | null>(null)
  const [settled, setSettled] = useState<OfficialInvoiceApprovedBatch | null>(null)
  const [phase, setPhase] = useState<'loading' | 'ready' | 'detail' | 'submitting' | 'unknown' | 'conflicted' | 'refused' | 'failed'>('loading')
  const [issues, setIssues] = useState<readonly { readonly field: string; readonly code: string }[]>([])
  const request = useRef(0)
  const activeRequest = useRef<AbortController | null>(null)
  const namespace = JSON.stringify([context.sessionEpoch, context.companyId, context.actorId])

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

  useEffect(() => fetchPage(), [fetchPage, namespace])
  useEffect(() => () => {
    request.current++
    activeRequest.current?.abort()
  }, [])

  function open(batch: OfficialInvoicePendingBatch) {
    const { controller, generation } = beginRequest()
    setPhase('loading')
    void client.getPending(batch.batchId, controller.signal).then((value) => {
      if (generation !== request.current || value.batch.companyId !== context.companyId
        || value.batch.batchId !== batch.batchId || value.batch.version !== batch.version) return
      setDetail(value)
      setIssues([])
      setPhase('detail')
    }).catch(() => { if (generation === request.current) setPhase('failed') })
  }

  function approve(retry: boolean) {
    if (detail === null || detail.batch.uploadedBy === context.actorId) return
    const frozen = detail.batch
    const keyScope = `${namespace}:${frozen.batchId}`
    const key = retry ? attempts.get(keyScope) : (attempts.get(keyScope) ?? createIdempotencyKey())
    if (key === undefined) return
    attempts.set(keyScope, key)
    const { controller, generation } = beginRequest()
    setPhase('submitting')
    void client.approve(frozen.batchId, frozen.version, key, controller.signal).then((reply) => {
      if (generation !== request.current) return
      if (reply.kind === 'unknown') { setPhase('unknown'); return }
      if (reply.kind === 'conflicted') { attempts.delete(keyScope); setPhase('conflicted'); return }
      if (reply.kind === 'refused') { attempts.delete(keyScope); setIssues(reply.issues); setPhase('refused'); return }
      if (!sameApprovedIdentity(frozen, reply.batch, context)) {
        attempts.delete(keyScope)
        setPhase('failed')
        return
      }
      attempts.delete(keyScope)
      setSettled(reply.batch)
      setDetail(null)
      setPhase('loading')
      fetchPage()
    }).catch(() => { if (generation === request.current) setPhase('unknown') })
  }

  function returnToQueue() {
    request.current++
    activeRequest.current?.abort()
    setDetail(null)
    setIssues([])
    setPhase('ready')
  }

  return (
    <section aria-labelledby="official-invoice-approvals-title">
      <h1 id="official-invoice-approvals-title">Resmî fatura aktarım onayları</h1>
      <p className={styles.notice}>Yükleyen kişi kendi dosyasını onaylayamaz. Onay yalnız değişmez kaynak snapshot'ını yayımlar.</p>
      {settled === null ? null : (
        <article className={styles.command} aria-label={`Onaylanan fatura kaynağı ${settled.batchId}`}>
          <p role="status">Kaynak onaylandı. Sürüm {settled.version}.</p>
          <button type="button" onClick={() => onApprovedSource(settled)}>Onaylanan kaynağı aktar</button>
        </article>
      )}
      {phase === 'loading' ? <p role="status">Onay bekleyen kaynaklar yükleniyor…</p> : null}
      {phase === 'failed' ? <div role="alert"><p>Onay kuyruğu güvenli biçimde okunamadı.</p>
        <button type="button" onClick={() => { setDetail(null); setPhase('loading'); fetchPage() }}>Kuyruğu yeniden oku</button></div> : null}
      {detail === null && phase === 'ready' && page?.items.length === 0 ? <p role="status">Onay bekleyen kaynak yok.</p> : null}
      {detail === null && phase === 'ready' ? page?.items.map((batch) => (
        <article className={styles.command} key={batch.batchId}>
          <h2>{batch.batchVersion}</h2>
          <p>{batch.rowCount} taksit, yükleyen: {batch.uploadedBy}</p>
          <button type="button" onClick={() => open(batch)}>İncele</button>
        </article>
      )) : null}
      {detail === null && phase === 'ready' && page?.nextCursor !== null && page?.nextCursor !== undefined ? (
        <button type="button" onClick={() => { setPhase('loading'); fetchPage(page.nextCursor) }}>Sonraki sayfa</button>
      ) : null}
      {detail === null ? null : (
        <article className={styles.command} aria-label={`Fatura kaynağı ${detail.batch.batchId}`}>
          <h2>{detail.batch.batchVersion}</h2>
          <p>SHA-256: {detail.batch.batchChecksum}</p>
          <table><thead><tr><th>Belge</th><th>Taksit</th><th>Tutar</th><th>Vade</th></tr></thead>
            <tbody>{detail.preview.batch.rows.map((row) => <tr key={row.openItemId}>
              <td>{row.sourceDocumentId}</td><td>{row.sourceInstallmentId}</td><td>{row.amount}</td><td>{row.dueOn ?? 'Yok'}</td>
            </tr>)}</tbody></table>
          {detail.batch.uploadedBy === context.actorId ? <p role="status">Bu kaynağı siz yüklediniz; başka bir yetkili onaylamalı.</p> : null}
          {phase === 'unknown' ? <div role="status"><p>Sonuç bilinmiyor; aynı anahtarla yeniden gönderin.</p>
            <button type="button" onClick={() => approve(true)}>Aynı anahtarla yeniden gönder</button></div> : null}
          {phase === 'conflicted' ? <div role="status"><p>Kayıt değişti; ayrıntıyı yeniden okuyun.</p>
            <button type="button" onClick={() => open(detail.batch)}>Kaydı yeniden oku</button></div> : null}
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
