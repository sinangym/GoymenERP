/**
 * U-05 new price list: name, currency, validity of the first version and its lines (products found through the
 * price-list product options: Active, sellable cards and their own stock/sales units). The list, its first version and
 * every line carry identities minted ONCE when the form opens or the line is added, so a retry of an unknown outcome
 * sends the same snapshot under the same key and never creates a second list. On success the screen opens the stored
 * list as the server answers it (save → reopen shows the server's record, never the form's).
 */

import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { useQueryClient } from '@tanstack/react-query'
import { useShellState } from '../../shell/shellContext'
import { hasCapability } from '../../shell/shellState'
import { PRICE_LIST_CAPABILITY, PRICE_LIST_ROUTE, priceListDetailPath } from './capabilities'
import { isWireDate, todayText } from './contract'
import { PriceListLinesEditor } from './PriceListLinesEditor'
import { buildLines, type PriceListLineRow } from './priceListLines'
import { isPriceListAttemptFrozen, livePriceListSnapshot, newIdentity } from './priceListAttempt'
import { PRICE_LIST_TEXT as T } from './priceListText'
import { attemptNotice, fieldErrorTexts, usePriceListCommand } from './usePriceListCommand'
import styles from './PriceListPages.module.css'

export function PriceListCreatePage() {
  const shell = useShellState()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [identity] = useState(() => ({ priceListId: newIdentity(), versionId: newIdentity() }))
  const [name, setName] = useState('')
  const [currency, setCurrency] = useState('TRY')
  const [validFrom, setValidFrom] = useState(() => todayText())
  const [validThrough, setValidThrough] = useState('')
  const [rows, setRows] = useState<PriceListLineRow[]>([])
  const [localError, setLocalError] = useState<string | null>(null)
  const command = usePriceListCommand(shell, 'product.price-list.create', (result) => {
    void queryClient.invalidateQueries({ queryKey: ['price-lists'] })
    navigate(priceListDetailPath(result.priceList.id), { replace: true })
  })

  const companyId = command.companyId
  const frozen = isPriceListAttemptFrozen(command.attempt)
  const notice = attemptNotice(command.attempt, T.notice.saved)

  function save() {
    setLocalError(null)
    if (name.trim() === '') return setLocalError(T.localErrors.name)
    if (!/^[A-Z]{3}$/.test(currency.trim())) return setLocalError(T.localErrors.currency)
    if (!isWireDate(validFrom)) return setLocalError(T.localErrors.validFrom)
    const lines = buildLines(rows)
    if (!lines.ok) return setLocalError(lines.message)
    const result = command.submit({
      kind: 'create',
      payload: {
        priceListId: identity.priceListId,
        versionId: identity.versionId,
        name: name.trim(),
        currencyCode: currency.trim(),
        channel: 'Sales',
        vatBasis: 'ExcludingVat',
        validFrom,
        validThrough: validThrough === '' ? null : validThrough,
        lines: lines.lines,
      },
    })
    if (result === 'unresolved-other-payload') setLocalError(T.notice.blockedOther)
    else if (result === 'no-context') setLocalError(T.notice.noContext)
  }

  return (
    <section className={styles.page} aria-labelledby="price-list-create-heading">
      {hasCapability(shell, PRICE_LIST_CAPABILITY.list) ? <Link className={styles.backLink} to={PRICE_LIST_ROUTE.list}>← {T.backToList}</Link> : null}
      <h1 id="price-list-create-heading" className={styles.title}>{T.createHeading}</h1>
      <p className={styles.muted}>{T.contextNote}</p>
      {companyId === null ? <p role="alert" className={styles.notice} data-tone="error">{T.notice.noContext}</p> : null}
      <fieldset className={styles.fieldset} disabled={frozen}>
        <div className={styles.grid}>
          <label htmlFor="price-list-name">{T.name}</label>
          <input id="price-list-name" value={name} maxLength={200} onChange={(event) => setName(event.target.value)} />
          <label htmlFor="price-list-currency">{T.currency}</label>
          <input id="price-list-currency" value={currency} maxLength={3} onChange={(event) => setCurrency(event.target.value.toUpperCase())} />
          <span>{T.channel}</span>
          <span>{T.channelSales}</span>
          <span>{T.vatBasis}</span>
          <span>{T.vatExcluded}</span>
          <label htmlFor="price-list-valid-from">{T.validFrom}</label>
          <input id="price-list-valid-from" type="date" value={validFrom} onChange={(event) => setValidFrom(event.target.value)} />
          <label htmlFor="price-list-valid-through">{T.validThrough}</label>
          <input id="price-list-valid-through" type="date" value={validThrough} onChange={(event) => setValidThrough(event.target.value)} />
        </div>
      </fieldset>
      {companyId === null ? null : <PriceListLinesEditor rows={rows} onChange={setRows} companyId={companyId} disabled={frozen} />}
      {localError === null ? null : <p role="alert" className={styles.notice} data-tone="error">{localError}</p>}
      {notice === null ? null : (
        <p role={notice.tone === 'error' ? 'alert' : 'status'} className={styles.notice} data-tone={notice.tone} data-testid="price-list-create-notice">
          {notice.text}
        </p>
      )}
      {fieldErrorTexts(command.attempt).map((text) => <p key={text} className={styles.fieldError}>{text}</p>)}
      <div className={styles.actions}>
        {command.attempt.phase !== 'sending' && livePriceListSnapshot(command.attempt) !== null ? (
          <>
            <button type="button" className={styles.secondaryButton} onClick={command.retry}>{T.notice.retry}</button>
            <button type="button" className={styles.linkButton} onClick={command.abandon}>{T.notice.abandon}</button>
          </>
        ) : null}
        <button type="button" className={styles.primaryButton} onClick={save} disabled={frozen || companyId === null} data-testid="price-list-save">
          {command.attempt.phase === 'sending' ? T.saving : T.save}
        </button>
      </div>
    </section>
  )
}
