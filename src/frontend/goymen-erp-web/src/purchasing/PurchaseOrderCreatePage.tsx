import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { createPurchaseOrder, fetchPurchasingCurrencyCandidates, fetchPurchasingProductCandidates, fetchPurchasingSupplierOptions, type CreatePurchaseOrderOutcome } from './api'
import { PURCHASING_CAPABILITY, PURCHASING_ROUTE, purchaseOrderDetailPath } from './capabilities'
import type { CreatePurchaseOrderLinePriceRequest, CreatePurchaseOrderRequest, ProductPurchaseCandidates, PurchasingCurrencyCandidates, PurchasingSupplierOffer } from './contract'
import { pendingPurchaseOrderCreateKey, recallPendingPurchaseOrderCreate, releasePendingPurchaseOrderCreate, reservePendingPurchaseOrderCreate, type PendingPurchaseOrderCreate } from './pendingCreateStore'
import { createRequestFence } from './requestFence'
import { isDateOnly, parseDecimalInput, parseQuantityInput } from '../sales/wirePrimitives'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { Button } from '../ui/Button'
import { CREATE_FORM_TEXT as F, ORDER_CREATE_TEXT as T, ORDER_PRICE_TEXT as P } from './purchasingText'
import styles from './purchasing.module.css'

type ReadState<T> =
  | { readonly type: 'idle' | 'loading' }
  | { readonly type: 'ready'; readonly data: T }
  | { readonly type: 'failed'; readonly message: string }

type Attempt =
  | { readonly phase: 'idle' }
  | { readonly phase: 'submitting'; readonly snapshot: PendingPurchaseOrderCreate }
  | { readonly phase: 'unknown'; readonly snapshot: PendingPurchaseOrderCreate; readonly reason: string }
  | { readonly phase: 'failed'; readonly message: string; readonly snapshot?: PendingPurchaseOrderCreate; readonly unresolvedSince?: string }
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
  if (outcome.type === 'http') return outcome.status === 403 ? T.supplierReadDenied : T.supplierSourceHttp(outcome.status)
  if (outcome.type === 'offline') return T.offline
  if (outcome.type === 'fence-stale') return T.fenceStale
  return outcome.type === 'malformed' ? T.supplierMalformed : T.supplierUnreachable
}

function commandFailure(outcome: Extract<CreatePurchaseOrderOutcome, { type: 'failed' }>): string {
  if (outcome.status === 401 || outcome.status === 403) return T.createDenied
  if (outcome.status === 409) return T.stateChanged(outcome.code ?? 'account_state')
  // A2a-1: a refused price or currency names its own rule (`lines[i].price` / `currencyId`); the screen never decides it.
  if (outcome.status === 422 && outcome.priceCode !== undefined) return P.priceRejected(outcome.priceCode)
  if (outcome.status === 422) return T.lineRejected(outcome.code ?? 'validation_failed')
  return T.notSaved(outcome.code ?? outcome.status)
}

type DiscountKind = 'none' | 'percent' | 'amount'

/**
 * A2a-1 (ALS-D5): the typed price as wire text — empty price = no price ("fiyat bekliyor", ALS-D18). Only the shape is judged
 * here (a positive decimal, a percent within 0..100); the decimals, the unit and every amount are the server's.
 */
function priceOf(unitPrice: string, kind: DiscountKind, discount: string):
  | { readonly ok: true; readonly price: CreatePurchaseOrderLinePriceRequest | null }
  | { readonly ok: false; readonly message: string } {
  if (unitPrice.trim() === '') {
    return kind === 'none' || discount.trim() === '' ? { ok: true, price: null } : { ok: false, message: P.discountWithoutPrice }
  }
  // Sinan (#66 6704170096): an explicit 0 is a price (line amount 0), apart from an empty price.
  const parsed = parseDecimalInput(unitPrice)
  if (!parsed.ok) return { ok: false, message: parsed.error === 'ambiguous-separator' ? P.ambiguousSeparator : P.priceInvalid }
  if (kind === 'none') return { ok: true, price: { unitPrice: parsed.value } }
  const value = parseDecimalInput(discount)
  if (!value.ok) return { ok: false, message: value.error === 'ambiguous-separator' ? P.ambiguousSeparator : P.discountInvalid }
  if (kind === 'percent' && percentOver100(value.value)) return { ok: false, message: P.discountInvalid }
  return { ok: true, price: kind === 'percent' ? { unitPrice: parsed.value, discountPercent: value.value } : { unitPrice: parsed.value, discountAmount: value.value } }
}

/** #944 review P3: the percent bound compared as canonical text, never through a JS number. */
function percentOver100(canonical: string): boolean {
  const [integer, fraction = ''] = canonical.split('.')
  if (integer.length !== 3) return integer.length > 3
  return integer > '100' || (integer === '100' && /[1-9]/.test(fraction))
}

function currencyFailure(outcome: Exclude<Awaited<ReturnType<typeof fetchPurchasingCurrencyCandidates>>, { type: 'ok-single' }>): string {
  if (outcome.type === 'http' && outcome.status === 503) return P.currencyUnavailable
  if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) return P.currencyDenied
  return P.currencyFailed
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
    : { phase: 'unknown', snapshot: recalled, reason: F.previousUnknown })
  const [supplierSearch, setSupplierSearch] = useState('')
  const [productSearch, setProductSearch] = useState('')
  const [effectiveOn, setEffectiveOn] = useState('')
  const [quantity, setQuantity] = useState('')
  const [supplierId, setSupplierId] = useState('')
  const [productId, setProductId] = useState('')
  const [suppliers, setSuppliers] = useState<ReadState<PurchasingSupplierOffer>>({ type: 'loading' })
  const [products, setProducts] = useState<ReadState<ProductPurchaseCandidates>>({ type: 'idle' })
  const [currencies, setCurrencies] = useState<ReadState<PurchasingCurrencyCandidates>>({ type: 'loading' })
  const [currencyId, setCurrencyId] = useState('')
  const [unitPrice, setUnitPrice] = useState('')
  const [discountKind, setDiscountKind] = useState<DiscountKind>('none')
  const [discount, setDiscount] = useState('')
  const currencyFence = useRef(createRequestFence())
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
      setFormError(F.dateBeforeProductSearch)
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
        : { type: 'failed', message: outcome.type === 'http' ? F.productSourceHttp(outcome.status) : F.productSourceFailed })
  }, [boundary, companyId, effectiveOn, productSearch])

  // A2a-1: the purchasing currencies; a failure never blocks the form — the order is then opened without prices.
  const readCurrencies = useCallback(async () => {
    if (companyId === null) return
    const token = currencyFence.current.capture()
    setCurrencies({ type: 'loading' })
    const outcome = await fetchPurchasingCurrencyCandidates(companyId, token.signal)
    if (!currencyFence.current.isCurrent(token)) return
    setCurrencies(outcome.type === 'ok-single' ? { type: 'ready', data: outcome.data } : { type: 'failed', message: currencyFailure(outcome) })
  }, [companyId])

  useEffect(() => {
    void readSuppliers()
    void readCurrencies()
    return () => { supplierFence.current.invalidate(); productFence.current.invalidate(); currencyFence.current.invalidate(); commandGeneration.current += 1 }
  }, [namespace, shell.routeEntryGeneration])

  const send = useCallback(async (snapshot: PendingPurchaseOrderCreate, unresolvedSince?: string) => {
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
      setAttempt({ phase: 'unknown', snapshot, reason: unresolvedSince ?? F.unknownRetry })
      return
    }
    if (outcome.type === 'failed' && unresolvedSince !== undefined) {
      setAttempt({ phase: 'failed', snapshot, unresolvedSince, message: commandFailure(outcome) })
      return
    }
    releasePendingPurchaseOrderCreate(storeKey, snapshot.idempotencyKey)
    setAttempt(outcome.type === 'applied'
      ? { phase: 'completed', result: outcome }
      : { phase: 'failed', message: commandFailure(outcome) })
  }, [actorId, boundary, companyId, storeKey])

  function submit(event: FormEvent) {
    event.preventDefault()
    if (attempt.phase === 'submitting' || attempt.phase === 'unknown' || (attempt.phase === 'failed' && attempt.unresolvedSince !== undefined) || companyId === null || actorId === null) return
    const supplier = suppliers.type === 'ready' ? suppliers.data.items.find((item) => `${item.supplierPartyId}|${item.companyAccountId}` === supplierId) : undefined
    const product = products.type === 'ready' ? products.data.items.find((item) => item.productId === productId) : undefined
    const parsedQuantity = parseQuantityInput(quantity)
    if (supplier === undefined || product === undefined || !isDateOnly(effectiveOn) || !parsedQuantity.ok) {
      setFormError(T.fieldsRequired)
      return
    }
    const priceAvailable = currencies.type === 'ready' && currencies.data.items.length > 0
    const priced = priceAvailable ? priceOf(unitPrice, discountKind, discount) : { ok: true as const, price: null }
    if (!priced.ok) {
      setFormError(priced.message)
      return
    }
    const currency = currencies.type === 'ready' ? currencies.data.items.find((item) => item.currencyId === currencyId) : undefined
    if (priced.price !== null && currency === undefined) {
      setFormError(P.currencyRequired)
      return
    }
    setFormError(null)
    const line = {
      lineId: newId(), productId: product.productId, expectedProductVersion: product.productVersion,
      unitId: product.unitId, quantity: parsedQuantity.value, sourceRequestLineId: null,
    }
    const payload: CreatePurchaseOrderRequest = {
      supplierPartyId: supplier.supplierPartyId,
      companyAccountId: supplier.companyAccountId,
      sourceRequestId: null,
      effectiveOn,
      lines: [priced.price === null ? line : { ...line, price: priced.price }],
      ...(currency === undefined ? {} : { currencyId: currency.currencyId }),
    }
    void send({ actorId, companyId, payload, idempotencyKey: newId() })
  }

  if (companyId === null || actorId === null) return <main className={styles.page}><p role="alert">{T.noContext}</p></main>
  if (attempt.phase === 'completed') {
    const order = attempt.result.response.detail.order
    return <main className={styles.page}><section className={styles.successCard} role="status"><p className={styles.eyebrow}>{T.eyebrow}</p><h1>{T.saved}</h1><p className={styles.mono}>{F.version(order.id, order.version)}</p>{attempt.result.replayed ? <p>{F.replayed}</p> : null}<div className={styles.actions}>{hasCapability(shell, PURCHASING_CAPABILITY.orderDetail) ? <Link className={styles.primaryLink} to={purchaseOrderDetailPath(order.id)}>{F.openSame}</Link> : null}{hasCapability(shell, PURCHASING_CAPABILITY.orderList) ? <Link to={PURCHASING_ROUTE[PURCHASING_CAPABILITY.orderList]}>{F.backToList}</Link> : null}</div></section></main>
  }
  const frozen = attempt.phase === 'submitting' || attempt.phase === 'unknown' || (attempt.phase === 'failed' && attempt.unresolvedSince !== undefined)
  // #944 review P3: without a purchasing currency to choose, no price can be sent; the price inputs stay off (unpriced order).
  const priceInputsOff = !(currencies.type === 'ready' && currencies.data.items.length > 0)
  return <main className={styles.page}><header className={styles.header}><div><p className={styles.eyebrow}>{T.eyebrow}</p><h1>{T.heading}</h1></div><Link to={PURCHASING_ROUTE[PURCHASING_CAPABILITY.orderList]}>{T.back}</Link></header>
    {attempt.phase === 'unknown' ? <section className={styles.warningCard} role="alert"><p>{attempt.reason}</p><div className={styles.actions}><Button type="button" onClick={() => void send(attempt.snapshot, attempt.reason)}>{F.retrySameKey}</Button><Link className={styles.secondary} to={PURCHASING_ROUTE[PURCHASING_CAPABILITY.orderList]}>{F.closeForm}</Link></div></section> : null}
    {attempt.phase === 'failed' && attempt.unresolvedSince !== undefined && attempt.snapshot !== undefined ? <section className={styles.warningCard} role="alert"><p>{attempt.message} {F.stillUnknown}</p><div className={styles.actions}><Button type="button" onClick={() => void send(attempt.snapshot!, attempt.unresolvedSince)}>{F.retrySameKey}</Button><Link className={styles.secondary} to={PURCHASING_ROUTE[PURCHASING_CAPABILITY.orderList]}>{F.closeForm}</Link></div></section> : attempt.phase === 'failed' ? <p role="alert" className={styles.error}>{attempt.message}</p> : null}
    {formError === null ? null : <p role="alert" className={styles.error}>{formError}</p>}
    <form className={styles.createForm} onSubmit={submit} noValidate>
      <fieldset disabled={frozen}><legend>{T.supplierLegend}</legend><div className={styles.searchRow}><label htmlFor="supplier-search">{T.supplierSearch}</label><input id="supplier-search" value={supplierSearch} onChange={(event) => setSupplierSearch(event.target.value)} /><Button type="button" onClick={() => void readSuppliers()}>{F.search}</Button></div>{suppliers.type === 'loading' ? <p role="status">{T.suppliersLoading}</p> : suppliers.type === 'failed' ? <p role="alert">{suppliers.message}</p> : <><label htmlFor="supplier">{T.supplierAccount}</label><select id="supplier" value={supplierId} onChange={(event) => setSupplierId(event.target.value)}><option value="">{F.choose}</option>{suppliers.type === 'ready' ? suppliers.data.items.map((item) => <option key={`${item.supplierPartyId}|${item.companyAccountId}`} value={`${item.supplierPartyId}|${item.companyAccountId}`}>{item.label}</option>) : null}</select>{suppliers.type === 'ready' && suppliers.data.nextCursor !== null ? <Button type="button" onClick={() => void readSuppliers(suppliers.data.nextCursor ?? undefined, true)}>{T.moreSuppliers}</Button> : null}</>}
      </fieldset>
      <fieldset disabled={frozen}><legend>{T.lineLegend}</legend><div className={styles.formGrid}><label htmlFor="effective-on">{F.businessDate}</label><input id="effective-on" type="date" value={effectiveOn} onChange={(event) => { productFence.current.invalidate(); setEffectiveOn(event.target.value); setProductId(''); setProducts({ type: 'idle' }) }} /><label htmlFor="product-search">{F.productSearch}</label><div className={styles.searchRow}><input id="product-search" value={productSearch} onChange={(event) => setProductSearch(event.target.value)} /><Button type="button" onClick={() => void readProducts()}>{F.search}</Button></div><label htmlFor="product">{T.product}</label>{products.type === 'loading' ? <p role="status">{F.productsLoading}</p> : products.type === 'failed' ? <p role="alert">{products.message}</p> : <select id="product" value={productId} onChange={(event) => setProductId(event.target.value)}><option value="">{F.choose}</option>{products.type === 'ready' ? products.data.items.map((item) => <option key={item.productId} value={item.productId}>{F.productOption(item.code, item.name, item.unitCode)}</option>) : null}</select>} {products.type === 'ready' && products.data.searchWindowFull ? <p className={styles.note}>{T.searchWindowFull}</p> : null}<label htmlFor="quantity">{F.quantity}</label><input id="quantity" inputMode="decimal" value={quantity} onChange={(event) => setQuantity(event.target.value)} placeholder={T.quantityPlaceholder} /></div></fieldset>
      <fieldset disabled={frozen} data-testid="purchase-order-price"><legend>{P.legend}</legend><p className={styles.note}>{P.note}</p><div className={styles.formGrid}>
        <label htmlFor="currency">{P.currency}</label>{currencies.type === 'loading' ? <p role="status">{P.currencyLoading}</p> : currencies.type === 'failed' ? <p role="alert">{currencies.message}</p> : currencies.type === 'ready' && currencies.data.items.length === 0 ? <p role="status">{P.currencyNone}</p> : <select id="currency" value={currencyId} onChange={(event) => setCurrencyId(event.target.value)}><option value="">{F.choose}</option>{currencies.type === 'ready' ? currencies.data.items.map((item) => <option key={item.currencyId} value={item.currencyId}>{item.displayLabel}</option>) : null}</select>}
        <label htmlFor="unit-price">{P.unitPrice}</label><input id="unit-price" inputMode="decimal" disabled={priceInputsOff} value={unitPrice} onChange={(event) => setUnitPrice(event.target.value)} placeholder={P.unitPricePlaceholder} aria-describedby="price-unit" />
        <span />{(() => { const product = products.type === 'ready' ? products.data.items.find((item) => item.productId === productId) : undefined; return <p id="price-unit" className={styles.mono}>{product === undefined ? P.priceUnitPending : P.priceUnit(product.unitCode)}</p> })()}
        <label htmlFor="discount-kind">{P.discountKind}</label><select id="discount-kind" disabled={priceInputsOff} value={discountKind} onChange={(event) => setDiscountKind(event.target.value as DiscountKind)}><option value="none">{P.discountNone}</option><option value="percent">{P.discountPercent}</option><option value="amount">{P.discountAmount}</option></select>
        {discountKind === 'none' ? null : <><label htmlFor="discount-value">{P.discountValue}</label><input id="discount-value" inputMode="decimal" disabled={priceInputsOff} value={discount} onChange={(event) => setDiscount(event.target.value)} placeholder={P.discountPlaceholder} /></>}
      </div></fieldset>
      <div className={styles.actions}><Button type="submit" variant="primary" disabled={frozen}>{attempt.phase === 'submitting' ? F.saving : T.submit}</Button></div>
    </form>
  </main>
}
