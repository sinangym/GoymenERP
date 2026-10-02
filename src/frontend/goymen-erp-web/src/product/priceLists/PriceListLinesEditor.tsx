import { useState } from 'react'
import { fetchProductOptions } from './api'
import type { PriceListProductOption, PriceListProductUnit } from './contract'
import type { PriceListLineRow } from './priceListLines'
import { newIdentity } from './priceListAttempt'
import { PRICE_LIST_TEXT as T } from './priceListText'
import styles from './PriceListPages.module.css'

function unitLabel(unit: PriceListProductUnit): string {
  const role = T.unitRole[unit.role] ?? unit.role
  return unit.code === null ? role : `${unit.code} (${role})`
}

export function PriceListLinesEditor({
  rows,
  onChange,
  companyId,
  disabled,
}: {
  readonly rows: readonly PriceListLineRow[]
  readonly onChange: (rows: PriceListLineRow[]) => void
  readonly companyId: string
  readonly disabled: boolean
}) {
  const [query, setQuery] = useState('')
  const [searching, setSearching] = useState(false)
  const [options, setOptions] = useState<{ items: readonly PriceListProductOption[]; hasMore: boolean } | null>(null)
  const [searchError, setSearchError] = useState<string | null>(null)

  async function search() {
    setSearching(true)
    setSearchError(null)
    const outcome = await fetchProductOptions(query, companyId)
    setSearching(false)
    if (outcome.type === 'ok-single') setOptions(outcome.data)
    else {
      setOptions(null)
      setSearchError(outcome.type === 'http' && outcome.status === 403 ? T.notice.forbidden : T.notice.unexpected)
    }
  }

  function add(option: PriceListProductOption) {
    onChange([...rows, {
      lineId: newIdentity(),
      productId: option.productId,
      productLabel: `${option.code} · ${option.name}`,
      units: option.units,
      priceUnitId: option.units[0]?.unitId ?? '',
      unitPrice: '',
      floorUnitPrice: '',
    }])
  }

  function change(lineId: string, patch: Partial<PriceListLineRow>) {
    onChange(rows.map((row) => (row.lineId === lineId ? { ...row, ...patch } : row)))
  }

  return (
    <fieldset className={styles.fieldset} disabled={disabled} data-testid="price-list-lines">
      <legend>{T.linesHeading}</legend>
      {rows.length === 0 ? <p className={styles.muted}>{T.noLines}</p> : null}
      {rows.map((row, index) => (
        <div key={row.lineId} className={styles.lineRow} data-testid="price-list-line">
          <label>
            {T.product}
            <span data-testid="price-list-line-product">{index + 1}. {row.productLabel}</span>
          </label>
          <label>
            {T.priceUnit}
            {row.units === null ? (
              <span className={styles.identifier}>{row.priceUnitId}</span>
            ) : (
              <select value={row.priceUnitId} onChange={(event) => change(row.lineId, { priceUnitId: event.target.value })}>
                {row.units.map((unit) => <option key={unit.unitId} value={unit.unitId}>{unitLabel(unit)}</option>)}
              </select>
            )}
          </label>
          <label>
            {T.unitPrice}
            <input inputMode="decimal" value={row.unitPrice} aria-label={`${index + 1}. ${T.unitPrice}`}
              onChange={(event) => change(row.lineId, { unitPrice: event.target.value })} />
          </label>
          <label>
            {T.floorUnitPrice}
            <input inputMode="decimal" value={row.floorUnitPrice} aria-label={`${index + 1}. ${T.floorUnitPrice}`}
              onChange={(event) => change(row.lineId, { floorUnitPrice: event.target.value })} />
          </label>
          <button type="button" className={styles.linkButton} onClick={() => onChange(rows.filter((candidate) => candidate.lineId !== row.lineId))}>
            {T.removeLine}
          </button>
        </div>
      ))}

      <div className={styles.picker}>
        <label htmlFor="price-list-product-search">{T.productSearch}</label>
        <div className={styles.actions}>
          <input id="price-list-product-search" value={query} onChange={(event) => setQuery(event.target.value)}
            onKeyDown={(event) => { if (event.key === 'Enter') { event.preventDefault(); void search() } }} />
          <button type="button" className={styles.secondaryButton} onClick={() => void search()} disabled={searching}>
            {searching ? T.searching : T.search}
          </button>
        </div>
        {searchError === null ? null : <p role="alert" className={styles.fieldError}>{searchError}</p>}
        {options === null ? null : options.items.length === 0 ? <p className={styles.muted}>{T.noProducts}</p> : (
          <ul className={styles.results} data-testid="price-list-product-options">
            {options.items.map((option) => (
              <li key={option.productId}>
                <span>{option.code} · {option.name}</span>
                <button type="button" className={styles.secondaryButton} onClick={() => add(option)}
                  disabled={rows.some((row) => row.productId === option.productId && row.priceUnitId === option.units[0]?.unitId)}>
                  {T.addLine}
                </button>
              </li>
            ))}
          </ul>
        )}
        {options?.hasMore === true ? <p className={styles.muted}>{T.moreProducts}</p> : null}
      </div>
    </fieldset>
  )
}
