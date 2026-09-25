import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { createPurchaseRequest, fetchPurchasingProductCandidates, type CreatePurchaseRequestOutcome } from './api'
import { PURCHASING_CAPABILITY, PURCHASING_ROUTE, purchaseRequestDetailPath } from './capabilities'
import type { CreatePurchaseRequestRequest, ProductPurchaseCandidate, ProductPurchaseCandidates } from './contract'
import { pendingPurchaseRequestCreateKey, recallPendingPurchaseRequestCreate, releasePendingPurchaseRequestCreate, reservePendingPurchaseRequestCreate, type PendingPurchaseRequestCreate } from './pendingRequestStore'
import { createRequestFence } from './requestFence'
import { isDateOnly, parseQuantityInput } from '../sales/wirePrimitives'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import styles from './purchasing.module.css'

type ReadState<T> = { readonly type: 'idle' | 'loading' } | { readonly type: 'ready'; readonly data: T } | { readonly type: 'failed'; readonly message: string }
type DraftLine = { readonly lineId: string; readonly productId: string; readonly quantity: string }
type Attempt =
  | { readonly phase: 'idle' }
  | { readonly phase: 'submitting'; readonly snapshot: PendingPurchaseRequestCreate }
  | { readonly phase: 'unknown'; readonly snapshot: PendingPurchaseRequestCreate; readonly reason: string }
  | { readonly phase: 'failed'; readonly message: string; readonly snapshot?: PendingPurchaseRequestCreate; readonly unresolvedSince?: string }
  | { readonly phase: 'completed'; readonly result: Extract<CreatePurchaseRequestOutcome, { type: 'applied' }> }

function newId(): string { return globalThis.crypto.randomUUID() }
function newLine(): DraftLine { return { lineId: newId(), productId: '', quantity: '' } }
function candidate(items: readonly ProductPurchaseCandidate[], id: string) { return items.find((item) => item.productId === id) }

export function PurchaseRequestCreatePage() {
  const shell = useShellState(); const key = contextKey(shell); const namespace = key === null ? null : contextKeyString(key)
  return <PurchaseRequestCreate key={`${namespace ?? 'no-context'}::${shell.routeEntryGeneration}`} namespace={namespace} />
}

function PurchaseRequestCreate({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState(); const boundary = useAuthBoundary()
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const actorId = shell.activeContext?.userId ?? null
  const storeKey = pendingPurchaseRequestCreateKey(shell.sessionEpoch, actorId, companyId)
  const recalled = recallPendingPurchaseRequestCreate(storeKey)
  const [attempt, setAttempt] = useState<Attempt>(() => recalled === null ? { phase: 'idle' } : { phase: 'unknown', snapshot: recalled, reason: 'Önceki gönderimin sonucu bilinmiyor.' })
  const [effectiveOn, setEffectiveOn] = useState(''); const [productSearch, setProductSearch] = useState('')
  const [products, setProducts] = useState<ReadState<ProductPurchaseCandidates>>({ type: 'idle' })
  const [lines, setLines] = useState<readonly DraftLine[]>(() => [newLine()])
  const [formError, setFormError] = useState<string | null>(null)
  const productFence = useRef(createRequestFence()); const commandGeneration = useRef(0); const inFlight = useRef(false)

  const readProducts = useCallback(async () => {
    if (companyId === null || !isDateOnly(effectiveOn)) { setFormError('Ürün aramadan önce geçerli bir iş tarihi seçin.'); return }
    const token = productFence.current.capture(); setProducts({ type: 'loading' }); const security = boundary.capture()
    const outcome = await fetchPurchasingProductCandidates(companyId, effectiveOn, productSearch, token.signal)
    if (!productFence.current.isCurrent(token)) return
    if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) boundary.report(outcome.status, security)
    setProducts(outcome.type === 'ok-single' ? { type: 'ready', data: outcome.data } : { type: 'failed', message: outcome.type === 'http' ? `Ürün kaynağı yanıt vermedi (${outcome.status}).` : 'Ürün kaynağına ulaşılamadı veya yanıt doğrulanamadı.' })
  }, [boundary, companyId, effectiveOn, productSearch])
  useEffect(() => () => { productFence.current.invalidate(); commandGeneration.current += 1 }, [namespace, shell.routeEntryGeneration])

  const send = useCallback(async (snapshot: PendingPurchaseRequestCreate, unresolvedSince?: string) => {
    if (companyId === null || actorId === null || snapshot.companyId !== companyId || snapshot.actorId !== actorId || inFlight.current) return
    inFlight.current = true; const generation = ++commandGeneration.current; reservePendingPurchaseRequestCreate(storeKey, snapshot)
    setAttempt({ phase: 'submitting', snapshot }); const security = boundary.capture()
    const outcome = await createPurchaseRequest(snapshot.payload, { companyId, idempotencyKey: snapshot.idempotencyKey })
    if (generation !== commandGeneration.current) return
    inFlight.current = false
    if (outcome.type === 'failed' && (outcome.status === 401 || outcome.status === 403)) boundary.report(outcome.status, security)
    if (outcome.type === 'unknown') { setAttempt({ phase: 'unknown', snapshot, reason: unresolvedSince ?? 'Sonuç bilinmiyor. Aynı anahtar ve içerikle yeniden gönderin.' }); return }
    if (outcome.type === 'failed' && unresolvedSince !== undefined) { setAttempt({ phase: 'failed', snapshot, unresolvedSince, message: `Talep kaydedilmedi (${outcome.code ?? outcome.status}).` }); return }
    releasePendingPurchaseRequestCreate(storeKey, snapshot.idempotencyKey)
    setAttempt(outcome.type === 'applied' ? { phase: 'completed', result: outcome } : { phase: 'failed', message: `Talep kaydedilmedi (${outcome.code ?? outcome.status}).` })
  }, [actorId, boundary, companyId, storeKey])

  function submit(event: FormEvent) {
    event.preventDefault()
    if (attempt.phase !== 'idle' && attempt.phase !== 'failed' || companyId === null || actorId === null || products.type !== 'ready' || !isDateOnly(effectiveOn)) return
    const payloadLines: CreatePurchaseRequestRequest['lines'][number][] = []
    for (const line of lines) {
      const product = candidate(products.data.items, line.productId); const quantity = parseQuantityInput(line.quantity)
      if (product === undefined || !quantity.ok) { setFormError('Her satırda Product kaynağından ürün/alış birimi ve sıfırdan büyük miktar zorunludur.'); return }
      payloadLines.push({ lineId: line.lineId, productId: product.productId, expectedProductVersion: product.productVersion, unitId: product.unitId, quantity: quantity.value })
    }
    setFormError(null)
    const payload: CreatePurchaseRequestRequest = { effectiveOn, lines: payloadLines }
    void send({ actorId, companyId, payload, idempotencyKey: newId() })
  }

  if (companyId === null || actorId === null) return <main className={styles.page}><p role="alert">Yetkili tek şirket bağlamı seçilmeden talep oluşturulamaz.</p></main>
  if (attempt.phase === 'completed') {
    const request = attempt.result.response.detail.request
    return <main className={styles.page}><section className={styles.successCard} role="status"><p className={styles.eyebrow}>P-01</p><h1>Satın alma talebi kaydedildi</h1><p className={styles.mono}>{request.id} · sürüm {request.version}</p>{attempt.result.replayed ? <p>Önceki kayıtlı sonuç yeniden gösterildi.</p> : null}<div className={styles.actions}>{hasCapability(shell, PURCHASING_CAPABILITY.requestDetail as never) ? <Link className={styles.primaryLink} to={purchaseRequestDetailPath(request.id)}>Aynı kaydı aç</Link> : null}<Link to={PURCHASING_ROUTE[PURCHASING_CAPABILITY.requestList]}>Listeye dön ve formu kapat</Link></div></section></main>
  }
  const frozen = attempt.phase === 'submitting' || attempt.phase === 'unknown' || (attempt.phase === 'failed' && attempt.unresolvedSince !== undefined)
  return <main className={styles.page}><header className={styles.header}><div><p className={styles.eyebrow}>P-01</p><h1>Yeni satın alma talebi</h1></div><Link to={PURCHASING_ROUTE[PURCHASING_CAPABILITY.requestList]}>← Satın alma talepleri</Link></header>
    {attempt.phase === 'unknown' ? <section className={styles.warningCard} role="alert"><p>{attempt.reason}</p><div className={styles.actions}><button type="button" onClick={() => void send(attempt.snapshot, attempt.reason)}>Aynı anahtarla yeniden gönder</button><Link to={PURCHASING_ROUTE[PURCHASING_CAPABILITY.requestList]}>Formu kapat</Link></div></section> : null}
    {attempt.phase === 'failed' && attempt.unresolvedSince !== undefined && attempt.snapshot !== undefined ? <section className={styles.warningCard} role="alert"><p>{attempt.message} Önceki gönderimin sonucu hâlâ bilinmiyor.</p><div className={styles.actions}><button type="button" onClick={() => void send(attempt.snapshot!, attempt.unresolvedSince)}>Aynı anahtarla yeniden gönder</button><Link className={styles.secondary} to={PURCHASING_ROUTE[PURCHASING_CAPABILITY.requestList]}>Formu kapat</Link></div></section> : attempt.phase === 'failed' ? <p role="alert" className={styles.error}>{attempt.message}</p> : null}{formError ? <p role="alert" className={styles.error}>{formError}</p> : null}
    <form className={styles.createForm} onSubmit={submit} noValidate><fieldset disabled={frozen}><legend>Talep kaynağı</legend><div className={styles.formGrid}><label htmlFor="request-effective-on">İş tarihi</label><input id="request-effective-on" type="date" value={effectiveOn} onChange={(event) => { productFence.current.invalidate(); setEffectiveOn(event.target.value); setProducts({ type: 'idle' }); setLines((current) => current.map((line) => ({ ...line, productId: '' }))) }} /><label htmlFor="request-product-search">Ürün ara</label><div className={styles.searchRow}><input id="request-product-search" value={productSearch} onChange={(event) => setProductSearch(event.target.value)} /><button type="button" onClick={() => void readProducts()}>Ara</button></div></div>{products.type === 'loading' ? <p role="status">Ürünler yükleniyor…</p> : products.type === 'failed' ? <p role="alert">{products.message}</p> : null}</fieldset>
    <fieldset disabled={frozen}><legend>Talep satırları</legend>{lines.map((line, index) => <div className={styles.formGrid} key={line.lineId}><label htmlFor={`request-product-${line.lineId}`}>Ürün {index + 1}</label><select id={`request-product-${line.lineId}`} value={line.productId} onChange={(event) => setLines((current) => current.map((item) => item.lineId === line.lineId ? { ...item, productId: event.target.value } : item))}><option value="">Seçiniz</option>{products.type === 'ready' ? products.data.items.map((item) => <option key={item.productId} value={item.productId}>{item.code} — {item.name} · {item.unitCode}</option>) : null}</select><label htmlFor={`request-quantity-${line.lineId}`}>Miktar</label><input id={`request-quantity-${line.lineId}`} inputMode="decimal" value={line.quantity} onChange={(event) => setLines((current) => current.map((item) => item.lineId === line.lineId ? { ...item, quantity: event.target.value } : item))} />{lines.length > 1 ? <button type="button" className={styles.secondary} onClick={() => setLines((current) => current.filter((item) => item.lineId !== line.lineId))}>Satırı kaldır</button> : null}</div>)}<button type="button" className={styles.secondary} onClick={() => setLines((current) => [...current, newLine()])}>Satır ekle</button></fieldset>
    <div className={styles.actions}><button type="submit" disabled={frozen}>{attempt.phase === 'submitting' ? 'Kaydediliyor…' : 'Taslak talebi kaydet'}</button></div></form>
  </main>
}
