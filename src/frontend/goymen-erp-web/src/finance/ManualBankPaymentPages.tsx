import { FinanceDocumentNumberFacts } from './FinanceDocumentNumberFacts'
import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useShellState } from '../shell/shellContext'
import { isDateOnly } from './dateOnly'
import { amountInputFromWire, parseAmountInput } from './decimalText'
import { decodeReadResult, isBoundUuid, isOpaqueVersion, type ProfileReference } from './contract'
import { useFinancePort } from './financePort'
import { issueText, MANUAL_BANK_PAYMENT_SETTLEMENT_TEXT } from './financeText'
import {
  decodeManualBankPaymentCommandResult,
  decodeManualBankPaymentPage,
  decodeManualBankPaymentView,
  type ApproveManualBankPaymentRequest,
  type RecordManualBankPaymentRequest,
  type ManualBankPaymentDraftSaveRequest,
  type ManualBankPaymentView,
} from './manualBankPaymentContract'
import { PaymentReversalPanel } from './PaymentReversalPanel'
import { PaymentAllocationPanel } from './PaymentAllocationPanel'
import { RecordNotesSection } from '../work/RecordNotesSection'

export interface ManualBankPaymentEntryContext {
  readonly partyId: string
  readonly companyAccountId: string
  readonly treasuryAccountId: string
  readonly currencyId: string
  readonly profile: ProfileReference
}

interface CommonProps {
  readonly companyId: string
  onOpen(paymentId: string): void
}

function key(): string {
  return globalThis.crypto.randomUUID()
}

function PaymentFacts({ payment }: { readonly payment: ManualBankPaymentView }) {
  return (
    <dl>
      <FinanceDocumentNumberFacts number={payment.documentNumber} kind="voucher" recorded={payment.status === 'Recorded'} />
      <dt>Durum</dt><dd>{payment.status}</dd>
      <dt>Tutar</dt><dd>{payment.preparation.money.amount}</dd>
      <dt>İşlem tarihi</dt><dd>{payment.preparation.occurredOn}</dd>
      <dt>Banka referansı</dt><dd>{payment.preparation.bankReference ?? 'Yok'}</dd>
      <dt>Bağlı belge</dt><dd>{payment.linkedDocument?.documentId ?? 'Yok'}</dd>
      <dt>Hazırlayan</dt><dd>{payment.preparedBy}</dd>
      <dt>Onaylayan</dt><dd>{payment.approvedBy ?? 'Bekliyor'}</dd>
      <dt>Kesin kaydeden</dt><dd>{payment.recordedBy ?? 'Bekliyor'}</dd>
    </dl>
  )
}

export function ManualBankPaymentCreatePage({
  companyId,
  entry,
  onOpen,
}: CommonProps & { readonly entry: ManualBankPaymentEntryContext }) {
  const port = useFinancePort()
  const [amount, setAmount] = useState('')
  const [occurredOn, setOccurredOn] = useState('')
  const [valueOn, setValueOn] = useState('')
  const [bankReference, setBankReference] = useState('')
  const [description, setDescription] = useState('')
  const [documentId, setDocumentId] = useState('')
  const [documentVersion, setDocumentVersion] = useState('')
  const [message, setMessage] = useState<string | null>(null)
  const [saved, setSaved] = useState<ManualBankPaymentView | null>(null)
  const [busy, setBusy] = useState(false)
  const attemptKey = useRef(key())

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (port === null || busy) return
    const parsed = parseAmountInput(amount)
    const hasDocumentId = documentId.trim() !== ''
    const hasDocumentVersion = documentVersion.trim() !== ''
    if (!parsed.ok || !isDateOnly(occurredOn) || (valueOn !== '' && !isDateOnly(valueOn))
      || hasDocumentId !== hasDocumentVersion
      || (hasDocumentId && (!isBoundUuid(documentId.trim()) || !isOpaqueVersion(documentVersion.trim())))) {
      setMessage('Tutar ve tarih alanlarını kontrol edin.')
      return
    }
    const request: ManualBankPaymentDraftSaveRequest = {
      paymentId: null,
      expectedVersion: null,
      preparation: {
        partyId: entry.partyId,
        companyAccountId: entry.companyAccountId,
        treasuryAccountId: entry.treasuryAccountId,
        money: { amount: parsed.wire, currencyId: entry.currencyId },
        profile: entry.profile,
        occurredOn,
        valueOn: valueOn === '' ? null : valueOn,
        bankReference: bankReference.trim() === '' ? null : bankReference.trim(),
        description: description.trim() === '' ? null : description.trim(),
      },
      linkedDocument: hasDocumentId
        ? { documentId: documentId.trim(), expectedVersion: documentVersion.trim() }
        : null,
    }
    setBusy(true)
    const reply = await port.saveManualBankPaymentDraft({ companyId }, attemptKey.current, request)
      .catch(() => ({ kind: 'no-reply' } as const))
    setBusy(false)
    if (reply.kind === 'no-reply') {
      setMessage('Sonuç bilinmiyor. Aynı anahtarla tekrar doğrulanmalıdır.')
      return
    }
    const decoded = decodeManualBankPaymentCommandResult(reply.body, companyId)
    if (!decoded.ok || decoded.value.outcome !== 'Succeeded') {
      setMessage(decoded.ok && decoded.value.outcome === 'Denied' ? 'Bu işlem için yetkiniz yok.' : 'Taslak kaydedilemedi.')
      return
    }
    setSaved(decoded.value.resource)
    setMessage('Giden ödeme taslağı kaydedildi.')
  }

  if (saved !== null) {
    return (
      <section>
        <h1>Giden ödeme taslağı kaydedildi</h1>
        <PaymentFacts payment={saved} />
        <button type="button" onClick={() => onOpen(saved.id)}>Kaydı aç</button>
      </section>
    )
  }

  return (
    <section>
      <h1>Yeni giden banka ödemesi</h1>
      <p>Para etkisi yalnız ayrı onay ve gerçekleşme doğrulamasından sonra oluşur.</p>
      <form onSubmit={submit}>
        <label>Tutar<input aria-label="Tutar" value={amount} onChange={(event) => setAmount(event.target.value)} /></label>
        <label>İşlem tarihi<input aria-label="İşlem tarihi" value={occurredOn} onChange={(event) => setOccurredOn(event.target.value)} /></label>
        <label>Valör<input aria-label="Valör" value={valueOn} onChange={(event) => setValueOn(event.target.value)} /></label>
        <label>Banka referansı (opsiyonel)<input aria-label="Banka referansı (opsiyonel)" value={bankReference} onChange={(event) => setBankReference(event.target.value)} /></label>
        <label>Açıklama<textarea aria-label="Açıklama" value={description} onChange={(event) => setDescription(event.target.value)} /></label>
        <label>Belge kimliği (opsiyonel)<input aria-label="Belge kimliği (opsiyonel)" value={documentId} onChange={(event) => setDocumentId(event.target.value)} /></label>
        <label>Belge sürümü (opsiyonel)<input aria-label="Belge sürümü (opsiyonel)" value={documentVersion} onChange={(event) => setDocumentVersion(event.target.value)} /></label>
        <button type="submit" disabled={busy}>{busy ? 'Kaydediliyor' : 'Taslağı kaydet'}</button>
      </form>
      {message === null ? null : <p role="status">{message}</p>}
    </section>
  )
}

export function ManualBankPaymentListPage({
  companyId,
  onOpen,
  onCreate,
}: CommonProps & { readonly onCreate?: () => void }) {
  const port = useFinancePort()
  const [items, setItems] = useState<readonly ManualBankPaymentView[] | null>(null)
  const [message, setMessage] = useState('Yükleniyor')

  useEffect(() => {
    const controller = new AbortController()
    if (port === null) {
      setMessage('Hizmet kullanılamıyor.')
      return () => controller.abort()
    }
    void port.listManualBankPayments({ companyId }, { cursor: null }, controller.signal).then((reply) => {
      if (controller.signal.aborted || reply.kind === 'no-reply') return
      const decoded = decodeReadResult(reply.body, (resource) => decodeManualBankPaymentPage(resource, companyId))
      if (!decoded.ok || decoded.value.outcome !== 'Found') {
        setMessage('Giden ödemeler okunamadı.')
        return
      }
      setItems(decoded.value.resource.items)
      setMessage(decoded.value.resource.items.length === 0 ? 'Kayıt yok.' : '')
    })
    return () => controller.abort()
  }, [companyId, port])

  return (
    <section>
      <h1>Giden banka ödemeleri</h1>
      {onCreate === undefined ? null : <button type="button" onClick={onCreate}>Yeni</button>}
      {items === null || items.length === 0 ? <p role="status">{message}</p> : (
        <table><thead><tr><th>İşlem</th><th>Durum</th><th>Tutar</th><th /></tr></thead><tbody>
          {items.map((payment) => <tr key={payment.id}>
            <td>{payment.preparation.occurredOn}</td><td>{payment.status}</td>
            <td>{payment.preparation.money.amount}</td>
            <td><button type="button" onClick={() => onOpen(payment.id)}>Aç</button></td>
          </tr>)}
        </tbody></table>
      )}
    </section>
  )
}

type ManualBankPaymentDetailProps = CommonProps & {
  readonly actorId: string
  readonly paymentId: string
  readonly canEdit: boolean
  readonly canApprove: boolean
  readonly canRecord: boolean
  readonly canReversePropose?: boolean
  readonly canReverseApprove?: boolean
  readonly canReadAllocations?: boolean
  readonly canWriteAllocations?: boolean
}

type PaymentSettlementAttempt =
  | { readonly operation: 'approve'; readonly key: string; readonly request: ApproveManualBankPaymentRequest }
  | { readonly operation: 'record'; readonly key: string; readonly request: RecordManualBankPaymentRequest }

export function ManualBankPaymentDetailPage(props: ManualBankPaymentDetailProps) {
  const sessionEpoch = useShellState().sessionEpoch
  return <ManualBankPaymentDetailContent
    key={JSON.stringify([sessionEpoch, props.companyId, props.actorId, props.paymentId])} {...props} />
}

function ManualBankPaymentDetailContent({
  companyId,
  actorId,
  paymentId,
  canEdit,
  canApprove,
  canRecord,
  canReversePropose = false,
  canReverseApprove = false,
  canReadAllocations = false,
  canWriteAllocations = false,
  onOpen,
}: ManualBankPaymentDetailProps) {
  const port = useFinancePort()
  const [payment, setPayment] = useState<ManualBankPaymentView | null>(null)
  const [message, setMessage] = useState('Yükleniyor')
  const [confirmed, setConfirmed] = useState(false)
  const [busy, setBusy] = useState(false)
  const [editing, setEditing] = useState(false)
  const [unknownEdit, setUnknownEdit] = useState(false)
  const [unknownSettlement, setUnknownSettlement] = useState(false)
  const settlementAttempt = useRef<PaymentSettlementAttempt | null>(null)
  const settlementInFlight = useRef(false)
  const mounted = useRef(true)
  const liveSettlement = useRef({ port, canApprove, canRecord })
  liveSettlement.current = { port, canApprove, canRecord }
  useEffect(() => {
    mounted.current = true
    return () => { mounted.current = false }
  }, [])
  const [amount, setAmount] = useState('')
  const [occurredOn, setOccurredOn] = useState('')
  const [valueOn, setValueOn] = useState('')
  const [bankReference, setBankReference] = useState('')
  const [description, setDescription] = useState('')
  const [documentId, setDocumentId] = useState('')
  const [documentVersion, setDocumentVersion] = useState('')
  const editAttempt = useRef<{
    readonly key: string
    readonly request: ManualBankPaymentDraftSaveRequest
  } | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    if (port === null) return () => controller.abort()
    void port.manualBankPaymentDetail({ companyId }, paymentId, controller.signal).then((reply) => {
      if (controller.signal.aborted || reply.kind === 'no-reply') return
      const decoded = decodeReadResult(reply.body, (resource) => decodeManualBankPaymentView(resource, companyId))
      if (!decoded.ok || decoded.value.outcome !== 'Found' || decoded.value.resource.id !== paymentId) {
        setMessage('Giden ödeme okunamadı.')
        return
      }
      setPayment(decoded.value.resource)
      setEditing(false)
      setUnknownEdit(false)
      editAttempt.current = null
      if (settlementAttempt.current === null) setMessage('')
    })
    return () => controller.abort()
  }, [companyId, paymentId, port])

  const beginEdit = () => {
    if (payment === null || payment.status !== 'Draft' || !canEdit || busy || settlementAttempt.current !== null) return
    setAmount(amountInputFromWire(payment.preparation.money.amount))
    setOccurredOn(payment.preparation.occurredOn)
    setValueOn(payment.preparation.valueOn ?? '')
    setBankReference(payment.preparation.bankReference ?? '')
    setDescription(payment.preparation.description ?? '')
    setDocumentId(payment.linkedDocument?.documentId ?? '')
    setDocumentVersion(payment.linkedDocument?.version ?? '')
    editAttempt.current = null
    setUnknownEdit(false)
    setEditing(true)
    setMessage('')
  }

  const saveEdit = async (event: FormEvent) => {
    event.preventDefault()
    if (port === null || payment === null || payment.status !== 'Draft' || !canEdit || busy || settlementAttempt.current !== null) return

    let attempt = editAttempt.current
    if (attempt === null) {
      const parsed = parseAmountInput(amount)
      const hasDocumentId = documentId.trim() !== ''
      const hasDocumentVersion = documentVersion.trim() !== ''
      if (!parsed.ok || !isDateOnly(occurredOn) || (valueOn !== '' && !isDateOnly(valueOn))
        || hasDocumentId !== hasDocumentVersion
        || (hasDocumentId && (!isBoundUuid(documentId.trim()) || !isOpaqueVersion(documentVersion.trim())))) {
        setMessage('Tutar ve tarih alanlarını kontrol edin.')
        return
      }
      const request: ManualBankPaymentDraftSaveRequest = {
        paymentId: payment.id,
        expectedVersion: payment.version,
        preparation: {
          partyId: payment.preparation.partyId,
          companyAccountId: payment.preparation.companyAccountId,
          treasuryAccountId: payment.preparation.treasuryAccountId,
          money: { amount: parsed.wire, currencyId: payment.preparation.money.currencyId },
          profile: payment.preparation.profile,
          occurredOn,
          valueOn: valueOn === '' ? null : valueOn,
          bankReference: bankReference.trim() === '' ? null : bankReference.trim(),
          description: description.trim() === '' ? null : description.trim(),
        },
        linkedDocument: hasDocumentId
          ? { documentId: documentId.trim(), expectedVersion: documentVersion.trim() }
          : null,
      }
      attempt = { key: key(), request }
      editAttempt.current = attempt
    }

    setBusy(true)
    const reply = await port.saveManualBankPaymentDraft({ companyId }, attempt.key, attempt.request)
      .catch(() => ({ kind: 'no-reply' } as const))
    setBusy(false)
    if (reply.kind === 'no-reply') {
      setUnknownEdit(true)
      setMessage('Sonuç bilinmiyor. Aynı anahtar ve değişmeyen alanlarla tekrar deneyin.')
      return
    }

    editAttempt.current = null
    setUnknownEdit(false)
    const decoded = decodeManualBankPaymentCommandResult(reply.body, companyId)
    if (!decoded.ok || decoded.value.outcome !== 'Succeeded'
      || decoded.value.resource.id !== payment.id || decoded.value.resource.status !== 'Draft') {
      setMessage(decoded.ok && decoded.value.outcome === 'Denied'
        ? 'Bu işlem için yetkiniz yok.'
        : 'Taslak güncellenemedi.')
      return
    }
    setPayment(decoded.value.resource)
    setEditing(false)
    setMessage('Giden ödeme taslağı güncellendi.')
  }

  const settle = async (operation: 'approve' | 'record') => {
    if (port === null || payment === null || busy || settlementInFlight.current
      || (operation === 'approve' && !canApprove)
      || (operation === 'record' && !canRecord)) return
    let attempt = settlementAttempt.current
    if (attempt !== null && attempt.operation !== operation) return
    if (attempt === null) {
      if (operation === 'approve' && payment.status !== 'Draft'
        || operation === 'record' && (payment.status !== 'Approved' || !confirmed)) return
      attempt = operation === 'approve'
        ? { operation, key: key(), request: { paymentId, expectedVersion: payment.version } }
        : { operation, key: key(), request: { paymentId, expectedVersion: payment.version, bankResultConfirmed: confirmed } }
      settlementAttempt.current = attempt
    }
    settlementInFlight.current = true
    setBusy(true)
    const reply = await (attempt.operation === 'approve'
      ? port.approveManualBankPayment({ companyId }, attempt.key, attempt.request)
      : port.recordManualBankPayment({ companyId }, attempt.key, attempt.request))
      .catch(() => ({ kind: 'no-reply' } as const))
    if (!mounted.current) return
    settlementInFlight.current = false
    setBusy(false)
    const live = liveSettlement.current
    const permitted = operation === 'approve' ? live.canApprove : live.canRecord
    // A late reply after permission/transport change does not settle the old attempt.
    // Reauthorization of the same identity may retry its exact body and key.
    if (live.port !== port || !permitted || reply.kind === 'no-reply') {
      setUnknownSettlement(true)
      setMessage(MANUAL_BANK_PAYMENT_SETTLEMENT_TEXT.unknown)
      return
    }
    const decoded = decodeManualBankPaymentCommandResult(reply.body, companyId)
    if (!decoded.ok || decoded.value.outcome === 'Succeeded'
      && (decoded.value.resource.id !== paymentId
        || decoded.value.resource.status !== (operation === 'approve' ? 'Approved' : 'Recorded'))) {
      setUnknownSettlement(true)
      setMessage(MANUAL_BANK_PAYMENT_SETTLEMENT_TEXT.unknown)
      return
    }
    settlementAttempt.current = null
    setUnknownSettlement(false)
    if (decoded.value.outcome !== 'Succeeded') {
      const balanceIssue = decoded.value.issues.find((issue) =>
        issue.code === 'finance_treasury_balance_unavailable' || issue.code === 'finance_treasury_balance_insufficient')
      setMessage(balanceIssue === undefined ? 'İşlem tamamlanamadı.' : issueText(balanceIssue.code))
      return
    }
    setPayment(decoded.value.resource)
    setMessage(operation === 'approve' ? 'Ödeme onaylandı.' : 'Giden ödeme kesin kaydedildi.')
  }

  const pendingSettlement = settlementAttempt.current
  if (payment === null) return <section><h1>Giden banka ödemesi</h1><p role="status">{message}</p></section>
  return (
    <section>
      <h1>Giden banka ödemesi</h1>
      <PaymentFacts payment={payment} />
      <RecordNotesSection recordType="finance-manual-bank-payment" recordId={payment.id} />
      {editing && payment.status === 'Draft' && canEdit ? <form onSubmit={saveEdit}>
        <label>Tutar<input aria-label="Tutar" disabled={busy || unknownEdit} value={amount}
          onChange={(event) => setAmount(event.target.value)} /></label>
        <label>İşlem tarihi<input aria-label="İşlem tarihi" disabled={busy || unknownEdit} value={occurredOn}
          onChange={(event) => setOccurredOn(event.target.value)} /></label>
        <label>Valör<input aria-label="Valör" disabled={busy || unknownEdit} value={valueOn}
          onChange={(event) => setValueOn(event.target.value)} /></label>
        <label>Banka referansı (opsiyonel)<input aria-label="Banka referansı (opsiyonel)"
          disabled={busy || unknownEdit} value={bankReference}
          onChange={(event) => setBankReference(event.target.value)} /></label>
        <label>Açıklama<textarea aria-label="Açıklama" disabled={busy || unknownEdit} value={description}
          onChange={(event) => setDescription(event.target.value)} /></label>
        <label>Belge kimliği (opsiyonel)<input aria-label="Belge kimliği (opsiyonel)"
          disabled={busy || unknownEdit} value={documentId}
          onChange={(event) => setDocumentId(event.target.value)} /></label>
        <label>Belge sürümü (opsiyonel)<input aria-label="Belge sürümü (opsiyonel)"
          disabled={busy || unknownEdit} value={documentVersion}
          onChange={(event) => setDocumentVersion(event.target.value)} /></label>
        <button type="submit" disabled={busy}>{busy
          ? 'Kaydediliyor'
          : unknownEdit ? 'Aynı anahtarla tekrar dene' : 'Değişiklikleri kaydet'}</button>
        <button type="button" disabled={busy || unknownEdit} onClick={() => {
          editAttempt.current = null
          setEditing(false)
          setMessage('')
        }}>Vazgeç</button>
      </form> : null}
      {!editing && canEdit && payment.status === 'Draft'
        ? <button type="button" disabled={busy || pendingSettlement !== null} onClick={beginEdit}>Düzenle</button> : null}
      {!editing && !unknownSettlement && canApprove && payment.status === 'Draft' && payment.preparedBy !== actorId
        ? <button type="button" disabled={busy} onClick={() => void settle('approve')}>Onayla</button> : null}
      {!unknownSettlement && canRecord && payment.status === 'Approved' ? <>
        <label><input type="checkbox" checked={confirmed} disabled={busy || pendingSettlement !== null} onChange={(event) => setConfirmed(event.target.checked)} />Banka sonucu gerçekleşti</label>
        <button type="button" disabled={busy || !confirmed} onClick={() => void settle('record')}>Kesin kaydet</button>
      </> : null}
      {unknownSettlement && pendingSettlement !== null
        && (pendingSettlement.operation === 'approve' ? canApprove : canRecord)
        ? <button type="button" disabled={busy} onClick={() => void settle(pendingSettlement.operation)}>
          {busy ? MANUAL_BANK_PAYMENT_SETTLEMENT_TEXT.busy : MANUAL_BANK_PAYMENT_SETTLEMENT_TEXT.retry}
        </button> : null}
      {payment.status === 'Recorded'
        ? <button type="button" onClick={() => onOpen(payment.id)}>Kesin kaydı yeniden aç</button> : null}
      {payment.status === 'Recorded' && canReadAllocations
        ? <PaymentAllocationPanel paymentId={payment.id} canWrite={canWriteAllocations} /> : null}
      {payment.status === 'Recorded' && (canReversePropose || canReverseApprove)
        ? <PaymentReversalPanel payment={payment} canPropose={canReversePropose}
          canApprove={canReverseApprove} /> : null}
      {message === '' ? null : <p role="status">{message}</p>}
    </section>
  )
}
