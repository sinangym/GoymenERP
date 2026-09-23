import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { createPurchaseOrder, fetchPurchasingProductCandidates, fetchPurchasingSupplierOptions, type CreatePurchaseOrderOutcome } from './api'
import { PURCHASING_CAPABILITY, PURCHASING_ROUTE, purchaseOrderDetailPath } from './capabilities'
import type { CreatePurchaseOrderRequest, ProductPurchaseCandidates, PurchasingSupplierOffer } from './contract'
import { pendingPurchaseOrderCreateKey, recallPendingPurchaseOrderCreate, releasePendingPurchaseOrderCreate, reservePendingPurchaseOrderCreate, type PendingPurchaseOrderCreate } from './pendingCreateStore'
import { createRequestFence } from './requestFence'
import { isDateOnly, parseQuantityInput } from '../sales/wirePrimitives'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import styles from './purchasing.module.css'

type ReadState<T> =
  | { readonly type: 'idle' | 'loading' }
  | { readonly type: 'ready'; readonly data: T }
  | { readonly type: 'failed'; readonly message: string }

type Attempt =
  | { readonly phase: 'idle' }
  | { readonly phase: 'submitting'; readonly snapshot: PendingPurchaseOrderCreate }
  | { readonly phase: 'unknown'; readonly snapshot: PendingPurchaseOrderCreate; readonly reason: string }
  | { readonly phase: 'failed'; readonly message: string }
  | { readonly phase: 'completed'; readonly result: Extract<CreatePurchaseOrderOutcome, { type: 'applied' }> }

function newId(): string {
  const bytes = new Uint8Array(16)
  globalThis.crypto.getRandomValues(bytes)
  bytes[6] = (bytes[6] & 0x0f) | 0x40
  bytes[8] = (bytes[8] & 0x3f) | 0x80
  const hex = Array.from(bytes, (value) => value.toString(16).padStart(2, '0')).join('')
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
}

function readFailure(outcome: Exclude<Awaited<ReturnType<typeof fetchPurchasingSupplierOptions>>, { type: 'ok-single' }>): string {
  if (outcome.type === 'http') return outcome.status === 403 ? 'Bu şirket için tedarikçi okuma yetkiniz yok.' : `Tedarikçi kaynağı yanıt vermedi (${outcome.status}).`
  if (outcome.type === 'offline') return 'Çevrimdışısınız.'
  if (outcome.type === 'fence-stale') return 'Şirket veya oturum değişti; sonuç kullanılmadı.'
  return outcome.type === 'malformed' ? 'Tedarikçi kaynağı beklenen sözleşmeyle yanıt vermedi.' : 'Tedarikçi kaynağına ulaşılamadı.'
}

function commandFailure(outcome: Extract<CreatePurchaseOrderOutcome, { type: 'failed' }>): string {
  if (outcome.status === 401 || outcome.status === 403) return 'Sipariş oluşturma yetkiniz artık geçerli değil.'
  if (outcome.status === 409) return `Seçilen kayıtların güncel durumu değişti (${outcome.code ?? 'account_state'}). Kaynakları yenileyin.`
  if (outcome.status === 422) return `Ürün, alış birimi veya miktar kabul edilmedi (${outcome.code ?? 'validation_failed'}).`
  return `Sipariş kaydedilmedi (${outcome.code ?? outcome.status}).`
}

export function PurchaseOrderCreatePage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <PurchaseOrderCreate key={`${namespace ?? 'no-context'}::${shell.routeEntryGeneration}`} namespace={namespace} />
}

function PurchaseOrderCreate({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const actorId = shell.activeContext?.userId ?? null
  const storeKey = pendingPurchaseOrderCreateKey(shell.sessionEpoch, actorId, companyId)
  const recalled = recallPendingPurchaseOrderCreate(storeKey)
  const [attempt, setAttempt] = useState<Attempt>(() => recalled === null
    ? { phase: 'idle' }
    : { phase: 'unknown', snapshot: recalled, reason: 'Önceki gönderimin sonucu bilinmiyor.' })
  const [supplierSearch, setSupplierSearch] = useState('')
  const [productSearch, setProductSearch] = useState('')
  const [effectiveOn, setEffectiveOn] = useState('')
  const [quantity, setQuantity] = useState('')
  const [supplierId, setSupplierId] = useState('')
  const [productId, setProductId] = useState('')
  const [suppliers, setSuppliers] = useState<ReadState<PurchasingSupplierOffer>>({ type: 'loading' })
  const [products, setProducts] = useState<ReadState<ProductPurchaseCandidates>>({ type: 'idle' })
  const [formError, setFormError] = useState<string | null>(null)
  const supplierFence = useRef(createRequestFence())
  const productFence = useRef(createRequestFence())
  const commandGeneration = useRef(0)
  const inFlight = useRef(false)

  const readSuppliers = useCallback(async (cursor?: string, append = false) => {
    if (companyId === null) return
    const token = supplierFence.current.capture()
    if (!append) setSuppliers({ type: 'loading' })
    const security = boundary.capture()
    const outcome = await fetchPurchasingSupplierOptions(companyId, { q: supplierSearch, cursor }, token.signal)
    if (!supplierFence.current.isCurrent(token)) return
    if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) boundary.report(outcome.status, security)
    if (outcome.type !== 'ok-single') {
      setSuppliers({ type: 'failed', message: readFailure(outcome) })
      return
    }
    setSuppliers((current) => append && current.type === 'ready'
      ? { type: 'ready', data: { items: [...current.data.items, ...outcome.data.items.filter((item) => !current.data.items.some((old) => old.supplierPartyId === item.supplierPartyId && old.companyAccountId === item.companyAccountId))], nextCursor: outcome.data.nextCursor } }
      : { type: 'ready', data: outcome.data })
  }, [boundary, companyId, supplierSearch])

  const readProducts = useCallback(async () => {
    if (companyId === null || !isDateOnly(effectiveOn)) {
      setFormError('Ürün aramadan önce geçerli bir iş tarihi seçin.')
      return
    }
    const token = productFence.current.capture()
    setProducts({ type: 'loading' })
    const security = boundary.capture()
    const outcome = await fetchPurchasingProductCandidates(companyId, effectiveOn, productSearch, token.signal)
    if (!productFence.current.isCurrent(token)) return
    if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) boundary.report(outcome.status, security)
    setProducts(outcome.type === 'ok-single'
        ? { type: 'ready', data: outcome.data }
        : { type: 'failed', message: outcome.type === 'http' ? `Ürün kaynağı yanıt vermedi (${outcome.status}).` : 'Ürün kaynağına ulaşılamadı veya yanıt doğrulanamadı.' })
  }, [boundary, companyId, effectiveOn, productSearch])

  useEffect(() => {
    void readSuppliers()
    return () => { supplierFence.current.invalidate(); productFence.current.invalidate(); commandGeneration.current += 1 }
  }, [namespace, shell.routeEntryGeneration])

  const send = useCallback(async (snapshot: PendingPurchaseOrderCreate) => {
    if (companyId === null || actorId === null || snapshot.companyId !== companyId || snapshot.actorId !== actorId || inFlight.current) return
    inFlight.current = true
    const generation = ++commandGeneration.current
    reservePendingPurchaseOrderCreate(storeKey, snapshot)
    setAttempt({ phase: 'submitting', snapshot })
    const security = boundary.capture()
    const outcome = await createPurchaseOrder(snapshot.payload, { companyId: snapshot.companyId, idempotencyKey: snapshot.idempotencyKey })
    if (generation !== commandGeneration.current) return
    inFlight.current = false
    if (outcome.type === 'failed' && (outcome.status === 401 || outcome.status === 403)) boundary.report(outcome.status, security)
    if (outcome.type === 'unknown') {
      setAttempt({ phase: 'unknown', snapshot, reason: 'Sonuç bilinmiyor. Aynı anahtar ve içerikle yeniden gönderin.' })
      return
    }
    releasePendingPurchaseOrderCreate(storeKey, snapshot.idempotencyKey)
    setAttempt(outcome.type === 'applied'
      ? { phase: 'completed', result: outcome }
      : { phase: 'failed', message: commandFailure(outcome) })
  }, [actorId, boundary, companyId, storeKey])

  function submit(event: FormEvent) {
    event.preventDefault()
    if (attempt.phase === 'submitting' || attempt.phase === 'unknown' || companyId === null || actorId === null) return
    const supplier = suppliers.type === 'ready' ? suppliers.data.items.find((item) => `${item.supplierPartyId}|${item.companyAccountId}` === supplierId) : undefined
    const product = products.type === 'ready' ? products.data.items.find((item) => item.productId === productId) : undefined
    const parsedQuantity = parseQuantityInput(quantity)
    if (supplier === undefined || product === undefined || !isDateOnly(effectiveOn) || !parsedQuantity.ok) {
      setFormError('Tedarikçi hesabı, iş tarihi, ürün/alış birimi ve sıfırdan büyük miktar zorunludur.')
      return
    }
    setFormError(null)
    const payload: CreatePurchaseOrderRequest = {
      supplierPartyId: supplier.supplierPartyId,
      companyAccountId: supplier.companyAccountId,
      sourceRequestId: null,
      effectiveOn,
      lines: [{
        lineId: newId(), productId: product.productId, expectedProductVersion: product.productVersion,
        unitId: product.unitId, quantity: parsedQuantity.value, sourceRequestLineId: null,
      }],
    }
    void send({ actorId, companyId, payload, idempotencyKey: newId() })
  }

  if (companyId === null || actorId === null) return <main className={styles.page}><p role="alert">Yetkili tek şirket bağlamı seçilmeden sipariş oluşturulamaz.</p></main>
  if (attempt.phase === 'completed') {
    const order = attempt.result.response.detail.order
    return <main className={styles.page}><section className={styles.successCard} role="status"><p className={styles.eyebrow}>P-04</p><h1>Satın alma siparişi kaydedildi</h1><p className={styles.mono}>{order.id} · sürüm {order.version}</p>{attempt.result.replayed ? <p>Önceki kayıtlı sonuç yeniden gösterildi.</p> : null}<div className={styles.actions}>{hasCapability(shell, PURCHASING_CAPABILITY.orderDetail as never) ? <Link className={styles.primaryLink} to={purchaseOrderDetailPath(order.id)}>Aynı kaydı aç</Link> : null}{hasCapability(shell, PURCHASING_CAPABILITY.orderList as never) ? <Link to={PURCHASING_ROUTE[PURCHASING_CAPABILITY.orderList]}>Listeye dön ve formu kapat</Link> : null}</div></section></main>
  }
  const frozen = attempt.phase === 'submitting' || attempt.phase === 'unknown'
  return <main className={styles.page}><header className={styles.header}><div><p className={styles.eyebrow}>P-04</p><h1>Yeni satın alma siparişi</h1></div><Link to={PURCHASING_ROUTE[PURCHASING_CAPABILITY.orderList]}>← Satın alma siparişleri</Link></header>
    {attempt.phase === 'unknown' ? <section className={styles.warningCard} role="alert"><p>{attempt.reason}</p><div className={styles.actions}><button type="button" onClick={() => void send(attempt.snapshot)}>Aynı anahtarla yeniden gönder</button><button type="button" className={styles.secondary} onClick={() => { releasePendingPurchaseOrderCreate(storeKey, attempt.snapshot.idempotencyKey); setAttempt({ phase: 'idle' }) }}>Bu denemeyi bırak</button></div></section> : null}
    {attempt.phase === 'failed' ? <p role="alert" className={styles.error}>{attempt.message}</p> : null}
    {formError === null ? null : <p role="alert" className={styles.error}>{formError}</p>}
    <form className={styles.createForm} onSubmit={submit} noValidate>
      <fieldset disabled={frozen}><legend>Tedarikçi</legend><div className={styles.searchRow}><label htmlFor="supplier-search">Tedarikçi ara</label><input id="supplier-search" value={supplierSearch} onChange={(event) => setSupplierSearch(event.target.value)} /><button type="button" onClick={() => void readSuppliers()}>Ara</button></div>{suppliers.type === 'loading' ? <p role="status">Tedarikçiler yükleniyor…</p> : suppliers.type === 'failed' ? <p role="alert">{suppliers.message}</p> : <><label htmlFor="supplier">Tedarikçi cari hesabı</label><select id="supplier" value={supplierId} onChange={(event) => setSupplierId(event.target.value)}><option value="">Seçiniz</option>{suppliers.type === 'ready' ? suppliers.data.items.map((item) => <option key={`${item.supplierPartyId}|${item.companyAccountId}`} value={`${item.supplierPartyId}|${item.companyAccountId}`}>{item.label}</option>) : null}</select>{suppliers.type === 'ready' && suppliers.data.nextCursor !== null ? <button type="button" className={styles.secondary} onClick={() => void readSuppliers(suppliers.data.nextCursor ?? undefined, true)}>Daha fazla tedarikçi</button> : null}</>}
      </fieldset>
      <fieldset disabled={frozen}><legend>Sipariş satırı</legend><div className={styles.formGrid}><label htmlFor="effective-on">İş tarihi</label><input id="effective-on" type="date" value={effectiveOn} onChange={(event) => { setEffectiveOn(event.target.value); setProductId(''); setProducts({ type: 'idle' }) }} /><label htmlFor="product-search">Ürün ara</label><div className={styles.searchRow}><input id="product-search" value={productSearch} onChange={(event) => setProductSearch(event.target.value)} /><button type="button" onClick={() => void readProducts()}>Ara</button></div><label htmlFor="product">Ürün ve alış birimi</label>{products.type === 'loading' ? <p role="status">Ürünler yükleniyor…</p> : products.type === 'failed' ? <p role="alert">{products.message}</p> : <select id="product" value={productId} onChange={(event) => setProductId(event.target.value)}><option value="">Seçiniz</option>{products.type === 'ready' ? products.data.items.map((item) => <option key={item.productId} value={item.productId}>{item.code} — {item.name} · {item.unitCode}</option>) : null}</select>} {products.type === 'ready' && products.data.searchWindowFull ? <p className={styles.note}>Arama penceresi doldu; sonucu daraltmak için ürün kodu veya adı yazın.</p> : null}<label htmlFor="quantity">Miktar</label><input id="quantity" inputMode="decimal" value={quantity} onChange={(event) => setQuantity(event.target.value)} placeholder="Örn. 1250 veya 1,5" /></div></fieldset>
      <div className={styles.actions}><button type="submit" disabled={frozen}>{attempt.phase === 'submitting' ? 'Kaydediliyor…' : 'Taslak siparişi kaydet'}</button></div>
    </form>
  </main>
}
