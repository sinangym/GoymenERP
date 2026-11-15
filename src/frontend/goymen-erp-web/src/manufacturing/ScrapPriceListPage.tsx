/**
 * URT-S13 — the company's monthly scrap prices, pinned to `GET /api/v1/manufacturing/scrap-prices?year=` and
 * `POST /api/v1/manufacturing/scrap-prices` (SAT-D316 (3)-(4), URT-D47, URT-D65).
 *
 * THE RULES THAT SHAPE THIS FILE
 *
 *  - One price per company and calendar month, in TL per kg, never per material. A month without a price is shown as
 *    such, with the consequence the server applies: its scrap weighings stay "maliyet yok" and are not finalised. No
 *    price is suggested, defaulted or carried over from another month here.
 *  - The price is the server's canonical text, shown as received; the screen never parses it to a number or computes
 *    a cost from it (the cost is the backend calculator's).
 *  - A recorded month is never changed: the screen offers no edit; the server answers a second entry for the month with
 *    409 and the list is read again. Who may enter, with which approval, and whether a past or future month may be
 *    entered are not decided, so the form is drawn only under the record capability and applies no date rule.
 *  - The entry is one frozen attempt with one idempotency key. An unknown outcome keeps that attempt (same key, same
 *    body) for "Yeniden gönder"; a new key is minted only after the attempt settled as applied or refused.
 *  - Remounted per company context: one company's prices are never shown under another context.
 */

import { useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { fetchScrapPrices, pendingScrapPriceKey, readPendingScrapPrice, recordScrapPrice, releasePendingScrapPrice, reservePendingScrapPrice, type ScrapPriceAttempt } from './scrapPriceApi'
import {
  SCRAP_PRICE_FIRST_MONTH,
  SCRAP_PRICE_LAST_MONTH,
  isScrapPriceEntryText,
  isScrapPriceYear,
  monthsWithoutPrice,
  type ScrapPriceList,
} from './scrapPriceContract'
import { SCRAP_PRICE_TEXT, scrapPriceFailureText, scrapPriceMonthLabel } from './scrapPriceText'
import { MANUFACTURING_CAPABILITY, orderListPath } from './capabilities'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import styles from './ScrapPriceListPage.module.css'

/** Presentation only: an unreadable instant is shown as it came, never rewritten. */
function formatInstant(value: string): string {
  const parsed = new Date(value)
  return Number.isNaN(parsed.getTime())
    ? value
    : parsed.toLocaleString('tr-TR', { dateStyle: 'medium', timeStyle: 'short' })
}

function newIdempotencyKey(): string {
  const webCrypto = globalThis.crypto
  if (typeof webCrypto?.randomUUID === 'function') return webCrypto.randomUUID()
  // Fallback only where randomUUID is missing; still a v4-shaped id the server accepts.
  const hex = Array.from({ length: 32 }, () => Math.floor(Math.random() * 16).toString(16))
  hex[12] = '4'
  hex[16] = ((parseInt(hex[16], 16) & 0x3) | 0x8).toString(16)
  const s = hex.join('')
  return `${s.slice(0, 8)}-${s.slice(8, 12)}-${s.slice(12, 16)}-${s.slice(16, 20)}-${s.slice(20)}`
}

/** The calendar year of the browser's today: only the list's starting point, never a rule on what may be entered. */
function currentYear(): number {
  return new Date().getFullYear()
}

const MONTHS: readonly number[] = Array.from(
  { length: SCRAP_PRICE_LAST_MONTH - SCRAP_PRICE_FIRST_MONTH + 1 },
  (_, index) => SCRAP_PRICE_FIRST_MONTH + index,
)

type Notice =
  | { readonly kind: 'applied'; readonly replayed: boolean }
  | { readonly kind: 'failed'; readonly text: string }
  | null

export function ScrapPriceListPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  // Remounted per context: one company's page must never be shown under another context.
  return <ScrapPrices key={namespace ?? 'no-context'} namespace={namespace} />
}

function ScrapPrices({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const companyId = shell.activeContext?.companyId ?? null
  const actorId = shell.activeContext?.userId ?? null
  const singleCompanyContext = (shell.activeContext?.scopeMode ?? 'single') === 'single' && companyId !== null
  const canRecord = hasCapability(shell, MANUFACTURING_CAPABILITY.scrapPriceRecord)
  const canOpenOrders = hasCapability(shell, MANUFACTURING_CAPABILITY.orderList)
  const pendingKey = pendingScrapPriceKey(shell.sessionBinding, actorId, companyId)

  const [yearText, setYearText] = useState(() => String(currentYear()))
  const [year, setYear] = useState(currentYear)
  const [month, setMonth] = useState<number>(SCRAP_PRICE_FIRST_MONTH)
  const [priceText, setPriceText] = useState('')
  const [priceError, setPriceError] = useState(false)
  const [sending, setSending] = useState(false)
  const [notice, setNotice] = useState<Notice>(null)
  // The attempt whose outcome is unknown: held across remounts by the transport's slot, shown here for a resend.
  const [pending, setPending] = useState<ScrapPriceAttempt | null>(() => readPendingScrapPrice(pendingKey))

  const list = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'manufacturing-scrap-prices', year],
    enabled: namespace !== null && singleCompanyContext,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchScrapPrices(companyId ?? '', year, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, snapshot)
      }
      return outcome
    },
  })

  async function send(attempt: ScrapPriceAttempt) {
    if (sending || companyId === null) return
    if (!reservePendingScrapPrice(pendingKey, attempt)) {
      // Another unresolved attempt holds the slot: only that one may be sent again.
      setPending(readPendingScrapPrice(pendingKey))
      return
    }
    setSending(true)
    setNotice(null)
    const snapshot = boundary.capture()
    const outcome = await recordScrapPrice(companyId, attempt)
    setSending(false)
    if (outcome.type === 'unknown') {
      setPending(attempt)
      return
    }
    releasePendingScrapPrice(pendingKey, attempt.idempotencyKey)
    setPending(null)
    if (outcome.type === 'applied') {
      setPriceText('')
      setNotice({ kind: 'applied', replayed: outcome.replayed })
      void list.refetch()
      return
    }
    if (outcome.status === 401 || outcome.status === 403) boundary.report(outcome.status, snapshot)
    setNotice({ kind: 'failed', text: scrapPriceFailureText(outcome.status, outcome.code, outcome.field) })
    if (outcome.status === 409) void list.refetch()
  }

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (pending !== null) return
    const entered = priceText.trim()
    if (!isScrapPriceEntryText(entered)) {
      setPriceError(true)
      return
    }
    setPriceError(false)
    void send(Object.freeze({ idempotencyKey: newIdempotencyKey(), year, month, tlPerKg: entered }))
  }

  const header = (
    <header className={styles.header}>
      <h1 id="mfg-scrap-price-heading" className={styles.title}>
        {SCRAP_PRICE_TEXT.heading}
      </h1>
      <div className={styles.headerActions}>
        <Field label={SCRAP_PRICE_TEXT.year} error={isScrapPriceYear(Number(yearText)) ? undefined : SCRAP_PRICE_TEXT.yearInvalid}>
          {(control) => (
            <input
              {...control}
              className={`${styles.input} ${styles.yearInput}`}
              inputMode="numeric"
              value={yearText}
              onChange={(event) => {
                const value = event.target.value
                setYearText(value)
                const parsed = /^[0-9]{1,4}$/.test(value) ? Number(value) : Number.NaN
                if (isScrapPriceYear(parsed)) {
                  setYear(parsed)
                  setNotice(null)
                }
              }}
            />
          )}
        </Field>
        {canOpenOrders ? (
          <Link className={styles.secondaryLink} to={orderListPath()}>
            {SCRAP_PRICE_TEXT.orders}
          </Link>
        ) : null}
      </div>
    </header>
  )

  if (!singleCompanyContext) {
    return (
      <section className={styles.page} aria-labelledby="mfg-scrap-price-heading">
        {header}
        <StateBlock state={{ kind: 'no-authorized-company' }} subject={SCRAP_PRICE_TEXT.contextSubject} />
      </section>
    )
  }

  const loaded: ScrapPriceList | null = list.data?.type === 'ok-single' ? list.data.data : null
  // A list whose year is not the one shown now (the year changed while it was in flight) is never shown under it.
  const shown = loaded !== null && loaded.year === year ? loaded : null

  const form = canRecord ? (
    <form className={styles.form} onSubmit={submit} aria-labelledby="mfg-scrap-price-form-heading" noValidate>
      <h2 id="mfg-scrap-price-form-heading" className={styles.formHeading}>
        {SCRAP_PRICE_TEXT.formHeading}
      </h2>
      <p className={styles.help}>{SCRAP_PRICE_TEXT.formHelp}</p>
      {pending !== null ? (
        <div className={styles.warning} role="status" data-testid="scrap-price-pending">
          <p>
            {SCRAP_PRICE_TEXT.pendingHeld}: {pending.year} {scrapPriceMonthLabel(pending.month)} — {pending.tlPerKg}{' '}
            TL/kg
          </p>
          <p>{SCRAP_PRICE_TEXT.unknown}</p>
          <Button
            type="button"
            variant="primary"
            onClick={() => void send(pending)}
            disabled={sending}
            aria-busy={sending}
          >
            {sending ? SCRAP_PRICE_TEXT.submitting : SCRAP_PRICE_TEXT.resend}
          </Button>
        </div>
      ) : (
        <>
          <div className={styles.formRow}>
            <Field label={SCRAP_PRICE_TEXT.month}>
              {(control) => (
                <select
                  {...control}
                  className={styles.select}
                  value={month}
                  onChange={(event) => setMonth(Number(event.target.value))}
                >
                  {MONTHS.map((value) => (
                    <option key={value} value={value}>
                      {year} {scrapPriceMonthLabel(value)}
                    </option>
                  ))}
                </select>
              )}
            </Field>
            <Field label={SCRAP_PRICE_TEXT.price} error={priceError ? SCRAP_PRICE_TEXT.priceInvalid : undefined}>
              {(control) => (
                <input
                  {...control}
                  className={styles.input}
                  inputMode="decimal"
                  autoComplete="off"
                  value={priceText}
                  onChange={(event) => {
                    setPriceText(event.target.value)
                    setPriceError(false)
                  }}
                />
              )}
            </Field>
          </div>
          <Button type="submit" variant="primary" disabled={sending} aria-busy={sending}>
            {sending ? SCRAP_PRICE_TEXT.submitting : SCRAP_PRICE_TEXT.submit}
          </Button>
        </>
      )}
      {notice?.kind === 'applied' ? (
        <p className={styles.notice} role="status">
          {notice.replayed ? SCRAP_PRICE_TEXT.appliedReplay : SCRAP_PRICE_TEXT.applied}
        </p>
      ) : null}
      {notice?.kind === 'failed' ? (
        <p className={styles.error} role="alert">
          {notice.text}
        </p>
      ) : null}
    </form>
  ) : null

  if (shown === null) {
    const state: RemoteViewState<unknown> =
      list.data === undefined || loaded !== null
        ? { kind: 'initial-loading' }
        : (classifyOutcome(list.data) as RemoteViewState<unknown>)
    return (
      <section className={styles.page} aria-labelledby="mfg-scrap-price-heading">
        {header}
        <p className={styles.help}>{SCRAP_PRICE_TEXT.costNote}</p>
        <StateBlock state={state} subject={SCRAP_PRICE_TEXT.subject} onRetry={() => void list.refetch()} />
        {form}
      </section>
    )
  }

  const missing = monthsWithoutPrice(shown)

  return (
    <section className={styles.page} aria-labelledby="mfg-scrap-price-heading" data-testid="scrap-price-list">
      {header}
      <p className={styles.help}>{SCRAP_PRICE_TEXT.costNote}</p>
      {shown.items.length === 0 ? (
        <StateBlock state={{ kind: 'empty-no-data' }} subject={SCRAP_PRICE_TEXT.subject} />
      ) : (
        <div className={styles.tableWrap}>
          <table className={styles.table}>
            <caption className={styles.caption}>{SCRAP_PRICE_TEXT.caption}</caption>
            <thead>
              <tr>
                <th scope="col">{SCRAP_PRICE_TEXT.month}</th>
                <th scope="col" className={styles.numeric}>
                  {SCRAP_PRICE_TEXT.price}
                </th>
                <th scope="col">{SCRAP_PRICE_TEXT.recordedAt}</th>
                <th scope="col">{SCRAP_PRICE_TEXT.recordedBy}</th>
              </tr>
            </thead>
            <tbody>
              {shown.items.map((item) => (
                <tr key={item.scrapPriceId} data-scrap-price-month={item.month}>
                  <th scope="row">
                    {item.year} {scrapPriceMonthLabel(item.month)}
                  </th>
                  <td className={styles.numeric} data-testid="scrap-price-tl-per-kg">
                    {item.tlPerKg}
                  </td>
                  <td>{formatInstant(item.recordedAtUtc)}</td>
                  <td className={styles.mono}>{item.recordedBy}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <p className={styles.muted} data-testid="scrap-price-missing">
        {missing.length === 0
          ? SCRAP_PRICE_TEXT.noMissingMonths
          : `${SCRAP_PRICE_TEXT.missingMonths}: ${missing.map(scrapPriceMonthLabel).join(', ')}`}
      </p>
      {form}
    </section>
  )
}
