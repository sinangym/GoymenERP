/**
 * SAT-D134 (EK19): the item type of the product in the selected company — the card's only tax-relevant field. KDV and tevkifat
 * come from Ayarlar > Vergi's default for this type (SAT-D139) and may be changed on the order line (SAT-D93); the card never
 * shows or stores a rate. Without `product.item-type.manage` (granted to no role here) the server answers 403 and the panel
 * shows nothing; an ended session (401) says so — it is not a missing permission; a failed read says so instead of showing
 * "belirtilmemiş".
 */

import { useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import {
  ITEM_TYPE_PROBLEM,
  PRODUCT_ITEM_TYPES,
  fetchItemType,
  setItemType,
  type ProductItemType,
  type ProductItemTypeView,
} from './itemTypeApi'
import { ITEM_TYPE_LABEL, ITEM_TYPE_TEXT } from './itemTypeText'
import styles from './ProductDetailPage.module.css'

export function ProductItemTypePanel({
  companyId,
  productId,
  queryNamespace,
}: {
  readonly companyId: string
  readonly productId: string
  readonly queryNamespace: string
}) {
  const query = useQuery({
    queryKey: [queryNamespace, 'product-item-type', productId],
    queryFn: ({ signal }) => fetchItemType(companyId, productId, signal),
  })
  const [editing, setEditing] = useState(false)
  const [notice, setNotice] = useState<string | null>(null)

  if (query.isPending) {
    return (
      <section className={styles.section} aria-labelledby="product-item-type-heading" aria-busy="true">
        <h2 id="product-item-type-heading">{ITEM_TYPE_TEXT.heading}</h2>
        <p className={styles.note}>{ITEM_TYPE_TEXT.loading}</p>
      </section>
    )
  }

  const outcome = query.data
  if (outcome === undefined || outcome.type === 'forbidden') return null
  if (outcome.type === 'unauthenticated') {
    return (
      <section className={styles.section} aria-labelledby="product-item-type-heading" data-testid="product-item-type-unauthenticated">
        <h2 id="product-item-type-heading">{ITEM_TYPE_TEXT.heading}</h2>
        <p role="alert">{ITEM_TYPE_TEXT.unauthenticated}</p>
      </section>
    )
  }

  return (
    <section className={styles.section} aria-labelledby="product-item-type-heading">
      <h2 id="product-item-type-heading">{ITEM_TYPE_TEXT.heading}</h2>
      {notice !== null ? <p role="status">{notice}</p> : null}
      {outcome.type === 'failed' ? (
        <p role="alert">{ITEM_TYPE_TEXT.failed}</p>
      ) : (
        <>
          <p>
            <strong>{ITEM_TYPE_TEXT.label}:</strong>{' '}
            {outcome.data.itemType === null ? ITEM_TYPE_TEXT.unset : ITEM_TYPE_LABEL[outcome.data.itemType]}
          </p>
          <p className={styles.note}>{ITEM_TYPE_TEXT.note}</p>
          {editing ? (
            <ItemTypeForm
              companyId={companyId}
              view={outcome.data}
              onDone={(saved) => {
                setEditing(false)
                setNotice(saved)
                void query.refetch()
              }}
            />
          ) : (
            <button type="button" onClick={() => setEditing(true)}>
              {ITEM_TYPE_TEXT.change}
            </button>
          )}
        </>
      )}
    </section>
  )
}

function ItemTypeForm({
  companyId,
  view,
  onDone,
}: {
  readonly companyId: string
  readonly view: ProductItemTypeView
  readonly onDone: (notice: string | null) => void
}) {
  const [itemType, setType] = useState<ProductItemType | ''>(view.itemType ?? '')
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState<{ readonly key: string; readonly fingerprint: string } | null>(null)

  async function submit(event: FormEvent) {
    event.preventDefault()
    // An unset product gets no preselected type: the person chooses one explicitly.
    if (itemType === '') return
    const body = { expectedVersion: view.version, itemType, reason: reason.trim() }
    const fingerprint = JSON.stringify(body)
    const key = pending !== null && pending.fingerprint === fingerprint ? pending.key : crypto.randomUUID()
    setBusy(true)
    const outcome = await setItemType(companyId, key, view.productId, body)
    setBusy(false)
    if (outcome.type === 'unknown') {
      setPending({ key, fingerprint })
      setError(ITEM_TYPE_TEXT.unknownOutcome)
      return
    }

    setPending(null)
    if (outcome.type === 'applied') {
      onDone(ITEM_TYPE_TEXT.saved)
    } else if (outcome.code === ITEM_TYPE_PROBLEM.versionConflict) {
      onDone(ITEM_TYPE_TEXT.conflict)
    } else {
      setError(outcome.code === ITEM_TYPE_PROBLEM.reasonRequired ? ITEM_TYPE_TEXT.reasonRequired : ITEM_TYPE_TEXT.refused)
    }
  }

  return (
    <form onSubmit={(event) => void submit(event)} aria-label={ITEM_TYPE_TEXT.formLabel}>
      <label>
        {ITEM_TYPE_TEXT.label}
        <select required value={itemType} onChange={(event) => setType(event.target.value as ProductItemType | '')}>
          {view.itemType === null ? (
            <option value="" disabled>
              {ITEM_TYPE_TEXT.choose}
            </option>
          ) : null}
          {PRODUCT_ITEM_TYPES.map((option) => (
            <option key={option} value={option}>
              {ITEM_TYPE_LABEL[option]}
            </option>
          ))}
        </select>
      </label>
      <label>
        {ITEM_TYPE_TEXT.reasonLabel}
        <textarea required rows={2} maxLength={4000} value={reason} onChange={(event) => setReason(event.target.value)} />
      </label>
      {error !== null ? <p role="alert">{error}</p> : null}
      <button type="submit" disabled={busy || itemType === ''} aria-busy={busy}>
        {busy ? ITEM_TYPE_TEXT.saving : pending !== null ? ITEM_TYPE_TEXT.retrySame : ITEM_TYPE_TEXT.save}
      </button>
      <button type="button" disabled={busy} onClick={() => onDone(null)}>
        {ITEM_TYPE_TEXT.cancel}
      </button>
    </form>
  )
}
