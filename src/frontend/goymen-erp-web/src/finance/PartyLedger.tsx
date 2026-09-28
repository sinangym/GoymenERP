/**
 * The first-release party ledger (cari hesap ekstresi) — one party account's RECORDED Finance facts in a date range.
 *
 * - The range lives in the URL (`partyLedgerPath`): picking it pushes ONE history entry, so opening a row's source and
 *   coming back keeps the range, and reopening the address shows the same ledger.
 * - OPEN-018: there is no carry-forward — the running balance starts at zero on the first day and the screen says so.
 * - OPEN-012: each currency is its own table; nothing is converted or added across currencies.
 * - Amounts, totals and balances are shown only as the server disclosed them (`Redacted` shows no digits and no side).
 * - Each row opens its source on the existing detail screens: a receipt or reversal on F-02, a cheque / note on the
 *   portfolio, a payment on its detail. An admitted open item has no screen yet and is shown by reference.
 *
 * The client computes nothing: sides, balances and totals are the server's.
 */

import { useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { StateBlock } from '../shell/StateBlock'
import {
  manualBankPaymentDetailPath,
  portfolioInstrumentPath,
  receiptDetailPath,
} from './capabilities'
import type { AmountView } from './contract'
import { WithCurrencyCodes, currencyShown, type CurrencyCodes } from './currencyCodes'
import { isDateOnly } from './dateOnly'
import { useFinanceRead } from './financePort'
import {
  decodePartyLedgerAnswer,
  type PartyLedgerBalance,
  type PartyLedgerCurrency,
  type PartyLedgerEntry,
} from './partyLedgerContract'
import {
  BALANCE_SIDE_HINT,
  BALANCE_SIDE_SUFFIX,
  CLOSING_BALANCE,
  COLUMN_BALANCE,
  COLUMN_CREDIT,
  COLUMN_DATE,
  COLUMN_DEBIT,
  COLUMN_DUE,
  COLUMN_KIND,
  COLUMN_SOURCE,
  KIND_LABEL,
  LEDGER_EMPTY,
  LEDGER_FORBIDDEN,
  LEDGER_FROM,
  LEDGER_NO_CARRY_FORWARD,
  LEDGER_NOTICE,
  LEDGER_PER_CURRENCY,
  LEDGER_RANGE_INVALID,
  LEDGER_REFUSED_INVALID,
  LEDGER_SHOW,
  LEDGER_TITLE,
  LEDGER_TO,
  LEDGER_TOO_MANY,
  LEDGER_UNAVAILABLE,
  OPEN_COLLECTION_RECEIPT,
  OPEN_INSTRUMENT,
  OPEN_ITEM_NO_SCREEN,
  OPEN_ORIGINAL,
  OPEN_PAYMENT,
  OPEN_RECEIPT,
  OPEN_REVERSAL,
  TOTALS,
  accountReference,
  reference,
} from './partyLedgerText'
import { amountShown, amountText } from './sourceView'
import styles from './Finance.module.css'

export interface PartyLedgerRange {
  readonly from: string
  readonly to: string
}

function isRange(range: PartyLedgerRange): boolean {
  return isDateOnly(range.from) && isDateOnly(range.to) && range.from <= range.to
}

/** A date-only value shown the Turkish way (gg.aa.yyyy); the wire text is never parsed into a Date. */
function dayText(value: string): string {
  const [year, month, day] = value.split('-')
  return `${day}.${month}.${year}`
}

function amountCell(view: AmountView) {
  const shown = amountShown(view)
  return <td data-amount-kind={shown.kind}>{amountText(shown)}</td>
}

function balanceText(balance: PartyLedgerBalance): string {
  const shown = amountText(amountShown(balance.amount))
  return balance.side === null ? shown : `${shown}${BALANCE_SIDE_SUFFIX[balance.side]}`
}

function SourceLinks({ entry }: { readonly entry: PartyLedgerEntry }) {
  switch (entry.kind) {
    case 'ManualReceipt':
      return <Link to={receiptDetailPath(entry.sourceId)}>{`${OPEN_RECEIPT} · ${reference(entry.sourceId)}`}</Link>
    case 'ManualReceiptReversal':
      return (
        <>
          <Link to={receiptDetailPath(entry.sourceId)}>{`${OPEN_REVERSAL} · ${reference(entry.sourceId)}`}</Link>
          {entry.reversesSourceId === null ? null : (
            <>
              {' '}
              <Link to={receiptDetailPath(entry.reversesSourceId)}>
                {`${OPEN_ORIGINAL} · ${reference(entry.reversesSourceId)}`}
              </Link>
            </>
          )}
        </>
      )
    case 'CustomerInstrumentCollection':
      return (
        <>
          {entry.instrumentId === null ? null : (
            <>
              <Link to={portfolioInstrumentPath(entry.instrumentId)}>
                {`${OPEN_INSTRUMENT} · ${reference(entry.instrumentId)}`}
              </Link>{' '}
            </>
          )}
          <Link to={receiptDetailPath(entry.sourceId)}>{`${OPEN_COLLECTION_RECEIPT} · ${reference(entry.sourceId)}`}</Link>
        </>
      )
    case 'ManualBankPayment':
      return <Link to={manualBankPaymentDetailPath(entry.sourceId)}>{`${OPEN_PAYMENT} · ${reference(entry.sourceId)}`}</Link>
    case 'OpenItem':
      return <span>{`${OPEN_ITEM_NO_SCREEN} · ${reference(entry.documentId ?? entry.sourceId)}`}</span>
  }
}

function CurrencyTable({ currency, codes }: { readonly currency: PartyLedgerCurrency; readonly codes: CurrencyCodes }) {
  const code = currencyShown(currency.currencyId, codes)
  return (
    <table data-ledger-currency={currency.currencyId}>
      <caption>{`${LEDGER_TITLE} — ${code}`}</caption>
      <thead>
        <tr>
          <th scope="col">{COLUMN_DATE}</th>
          <th scope="col">{COLUMN_KIND}</th>
          <th scope="col">{COLUMN_SOURCE}</th>
          <th scope="col">{COLUMN_DUE}</th>
          <th scope="col">{COLUMN_DEBIT}</th>
          <th scope="col">{COLUMN_CREDIT}</th>
          <th scope="col">{COLUMN_BALANCE}</th>
        </tr>
      </thead>
      <tbody>
        {currency.entries.map((entry) => (
          <tr key={entry.entryId} data-ledger-entry={entry.entryId} data-ledger-kind={entry.kind}>
            <td>{dayText(entry.occurredOn)}</td>
            <th scope="row">{KIND_LABEL[entry.kind]}</th>
            <td><SourceLinks entry={entry} /></td>
            <td>{entry.dueOn === null ? '' : dayText(entry.dueOn)}</td>
            {entry.side === 'Debit' ? amountCell(entry.amount) : <td />}
            {entry.side === 'Credit' ? amountCell(entry.amount) : <td />}
            <td data-ledger-balance data-amount-kind={amountShown(entry.runningBalance.amount).kind}>
              {balanceText(entry.runningBalance)}
            </td>
          </tr>
        ))}
      </tbody>
      <tfoot>
        <tr data-ledger-totals>
          <th scope="row" colSpan={4}>{TOTALS}</th>
          {amountCell(currency.debitTotal)}
          {amountCell(currency.creditTotal)}
          <td />
        </tr>
        <tr data-ledger-closing>
          <th scope="row" colSpan={6}>{CLOSING_BALANCE}</th>
          <td data-amount-kind={amountShown(currency.closingBalance.amount).kind}>{balanceText(currency.closingBalance)}</td>
        </tr>
      </tfoot>
    </table>
  )
}

function LedgerBody({
  partyId,
  companyAccountId,
  range,
}: {
  readonly partyId: string
  readonly companyAccountId: string
  readonly range: PartyLedgerRange
}) {
  const ledger = useFinanceRead('party-ledger', [partyId, companyAccountId, range.from, range.to],
    (port, scope, signal) => port.partyLedger(scope, { partyId, companyAccountId, from: range.from, to: range.to }, signal),
    decodePartyLedgerAnswer)

  if (ledger.state.kind === 'forbidden') return <p role="status">{LEDGER_FORBIDDEN}</p>
  if (ledger.state.kind === 'service-unavailable') return <p role="status">{LEDGER_UNAVAILABLE}</p>
  if (ledger.state.kind !== 'ready') return <StateBlock state={ledger.state} subject={LEDGER_TITLE} onRetry={ledger.refetch} />
  const answer = ledger.state.data
  if (answer.kind === 'refused') {
    return <p role="alert">{answer.refusal === 'TooManyEntries' ? LEDGER_TOO_MANY : LEDGER_REFUSED_INVALID}</p>
  }
  if (answer.ledger.currencies.length === 0) return <p role="status">{LEDGER_EMPTY}</p>
  return (
    <WithCurrencyCodes>
      {(codes) => (
        <div className={styles.ledger}>
          {answer.ledger.currencies.map((currency) => (
            <CurrencyTable key={currency.currencyId} currency={currency} codes={codes} />
          ))}
          <p className={styles.hint}>{BALANCE_SIDE_HINT}</p>
        </div>
      )}
    </WithCurrencyCodes>
  )
}

/**
 * The ledger of one party account. `range` is the URL's; `onRange` writes a new one (one history entry). The range
 * form only asks when the chosen dates are a valid range.
 */
export function PartyLedger({
  partyId,
  companyAccountId,
  range,
  onRange,
}: {
  readonly partyId: string
  readonly companyAccountId: string
  readonly range: PartyLedgerRange
  readonly onRange: (range: PartyLedgerRange) => void
}) {
  const [from, setFrom] = useState(range.from)
  const [to, setTo] = useState(range.to)
  const [invalid, setInvalid] = useState(false)
  // The URL is the truth: when it changes (back / forward), the form shows it again.
  const [shown, setShown] = useState(range)
  if (shown.from !== range.from || shown.to !== range.to) {
    setShown(range)
    setFrom(range.from)
    setTo(range.to)
    setInvalid(false)
  }

  const submit = (event: FormEvent) => {
    event.preventDefault()
    const next = { from, to }
    if (!isRange(next)) {
      setInvalid(true)
      return
    }
    setInvalid(false)
    if (next.from !== range.from || next.to !== range.to) onRange(next)
  }

  return (
    <section aria-labelledby="finance-party-ledger-title" className={styles.detail}>
      <h1 id="finance-party-ledger-title">{LEDGER_TITLE}</h1>
      <p className={styles.hint}>{accountReference(partyId, companyAccountId)}</p>
      <p className={styles.notice}>{LEDGER_NOTICE}</p>
      <p className={styles.notice} data-ledger-carry-forward="NoneOpen018"><strong>{LEDGER_NO_CARRY_FORWARD}</strong></p>
      <p className={styles.notice}>{LEDGER_PER_CURRENCY}</p>
      <form className={styles.actions} aria-label={LEDGER_TITLE} onSubmit={submit}>
        <div className={styles.field}>
          <label htmlFor="party-ledger-from">{LEDGER_FROM}</label>
          <input id="party-ledger-from" type="date" value={from} onChange={(event) => setFrom(event.target.value)} />
        </div>
        <div className={styles.field}>
          <label htmlFor="party-ledger-to">{LEDGER_TO}</label>
          <input id="party-ledger-to" type="date" value={to} onChange={(event) => setTo(event.target.value)} />
        </div>
        <button type="submit">{LEDGER_SHOW}</button>
      </form>
      {invalid ? <p role="alert" className={styles.error}>{LEDGER_RANGE_INVALID}</p> : null}
      {isRange(range)
        ? <LedgerBody partyId={partyId} companyAccountId={companyAccountId} range={range} />
        : <p role="status">{LEDGER_RANGE_INVALID}</p>}
    </section>
  )
}
