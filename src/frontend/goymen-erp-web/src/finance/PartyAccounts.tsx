/**
 * B-05 / M-03 (FNS-D14, KLN-D3): the Muhasebe > Cari Hesaplar panels over Finance's one open-item sub-ledger.
 *
 * - `OpenPartyAccounts` lists every party account with a still-open item: per currency the count and the server's
 *   exact sum of the open receivable and open payable items. Each row opens that account's ledger in the address.
 * - `PartyAccountOpenItems` lists the chosen account's open items by due date, as the server ordered them.
 *
 * Nothing is computed, converted or added here (OPEN-012); amounts are shown only as the server disclosed them.
 * A 401/403 is reported to the shared auth boundary, which decides what it means.
 */

import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState, type ViewOutcome } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { partyLedgerPath } from './capabilities'
import type { MoneyText } from './contract'
import { WithCurrencyCodes, currencyShown } from './currencyCodes'
import { fetchPartyAccount, fetchPartyAccounts } from './partyAccountsHttp'
import type { PartyAccountAnswer, PartyAccountListAnswer } from './partyAccountsContract'
import {
  ACCOUNTS_COLUMN_ACCOUNT,
  ACCOUNTS_COLUMN_CURRENCY,
  ACCOUNTS_COLUMN_PAYABLE,
  ACCOUNTS_COLUMN_PAYABLE_COUNT,
  ACCOUNTS_COLUMN_RECEIVABLE,
  ACCOUNTS_COLUMN_RECEIVABLE_COUNT,
  ACCOUNTS_EMPTY,
  ACCOUNTS_FORBIDDEN,
  ACCOUNTS_LOADING,
  ACCOUNTS_NOTICE,
  ACCOUNTS_OPEN,
  ACCOUNTS_SUBJECT,
  ACCOUNTS_TITLE,
  ACCOUNTS_TOO_MANY,
  DIRECTION_LABEL,
  ITEMS_COLUMN_CURRENCY,
  ITEMS_COLUMN_DIRECTION,
  ITEMS_COLUMN_DUE,
  ITEMS_COLUMN_ORIGINAL,
  ITEMS_COLUMN_OUTSTANDING,
  ITEMS_COLUMN_SOURCE,
  ITEMS_EMPTY,
  ITEMS_FORBIDDEN,
  ITEMS_LOADING,
  ITEMS_NO_DUE,
  ITEMS_NOTICE,
  ITEMS_REDACTED,
  ITEMS_SUBJECT,
  ITEMS_TITLE,
  ITEMS_TOO_MANY_FACTS,
  ITEMS_TOO_MANY_OPEN,
  ITEMS_WITHHELD,
} from './partyAccountsText'
import { accountReference, reference } from './partyLedgerText'
import { amountText } from './sourceView'
import styles from './Finance.module.css'

/** A date-only value shown the Turkish way (gg.aa.yyyy); the wire text is never parsed into a Date. */
function dayText(value: string): string {
  const [year, month, day] = value.split('-')
  return `${day}.${month}.${year}`
}

function moneyText(money: MoneyText): string {
  return amountText({ kind: 'visible', amount: money.amount })
}

function useBoundedRead<T>(key: readonly unknown[], load: (signal: AbortSignal) => Promise<ViewOutcome<T>>) {
  const boundary = useAuthBoundary()
  const query = useQuery({
    queryKey: key,
    queryFn: async ({ signal }): Promise<ViewOutcome<T>> => {
      // The bindings are captured before the read, so a late 401/403 of a departed actor is discarded.
      const snapshot = boundary.capture()
      const outcome = await load(signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, snapshot)
      }
      return outcome
    },
  })
  const state = (query.isPending
    ? { kind: 'initial-loading' }
    : classifyOutcome(query.data ?? { type: 'malformed' })) as RemoteViewState<T>
  return { state, refetch: () => { void query.refetch() } }
}

/**
 * The company's party accounts with open items. `labels` names an account the caller already knows (the picker's
 * active accounts); any other account is shown by its honest reference, never by a guessed name.
 */
export function OpenPartyAccounts({
  companyId,
  base,
  labels,
}: {
  readonly companyId: string
  readonly base: string
  readonly labels: ReadonlyMap<string, string>
}) {
  const read = useBoundedRead<PartyAccountListAnswer>(['finance', companyId, 'party-accounts'],
    (signal) => fetchPartyAccounts(companyId, signal))
  const { state } = read

  let body
  if (state.kind === 'initial-loading') body = <p role="status">{ACCOUNTS_LOADING}</p>
  else if (state.kind === 'forbidden') body = <p role="status">{ACCOUNTS_FORBIDDEN}</p>
  else if (state.kind !== 'ready') body = <StateBlock state={state} subject={ACCOUNTS_SUBJECT} onRetry={read.refetch} />
  else if (state.data.kind === 'refused') body = <p role="alert">{ACCOUNTS_TOO_MANY}</p>
  else if (state.data.list.accounts.length === 0) body = <p role="status">{ACCOUNTS_EMPTY}</p>
  else {
    const accounts = state.data.list.accounts
    body = (
      <WithCurrencyCodes>
        {(codes) => (
          <table data-party-accounts>
            <caption>{ACCOUNTS_TITLE}</caption>
            <thead>
              <tr>
                <th scope="col">{ACCOUNTS_COLUMN_ACCOUNT}</th>
                <th scope="col">{ACCOUNTS_COLUMN_CURRENCY}</th>
                <th scope="col">{ACCOUNTS_COLUMN_RECEIVABLE_COUNT}</th>
                <th scope="col">{ACCOUNTS_COLUMN_RECEIVABLE}</th>
                <th scope="col">{ACCOUNTS_COLUMN_PAYABLE_COUNT}</th>
                <th scope="col">{ACCOUNTS_COLUMN_PAYABLE}</th>
                <th scope="col"><span className={styles.hint}>{ACCOUNTS_OPEN}</span></th>
              </tr>
            </thead>
            <tbody>
              {accounts.flatMap((account) => {
                const key = `${account.partyId}:${account.companyAccountId}`
                const label = labels.get(key) ?? accountReference(account.partyId, account.companyAccountId)
                return account.currencies.map((currency, index) => (
                  <tr key={`${key}:${currency.currencyId}`} data-party-account={key}
                    data-party-account-currency={currency.currencyId}>
                    {index === 0 ? <th scope="row" rowSpan={account.currencies.length}>{label}</th> : null}
                    <td>{currencyShown(currency.currencyId, codes)}</td>
                    <td>{currency.receivableCount}</td>
                    <td data-amount-kind="visible">{moneyText(currency.openReceivable)}</td>
                    <td>{currency.payableCount}</td>
                    <td data-amount-kind="visible">{moneyText(currency.openPayable)}</td>
                    {index === 0 ? (
                      <td rowSpan={account.currencies.length}>
                        <Link to={partyLedgerPath(account.partyId, account.companyAccountId, undefined, base)}
                          aria-label={`${ACCOUNTS_OPEN}: ${label}`}>{ACCOUNTS_OPEN}</Link>
                      </td>
                    ) : null}
                  </tr>
                ))
              })}
            </tbody>
          </table>
        )}
      </WithCurrencyCodes>
    )
  }

  return (
    <section aria-labelledby="finance-party-accounts-title" className={styles.ledger}>
      <h2 id="finance-party-accounts-title">{ACCOUNTS_TITLE}</h2>
      <p className={styles.hint}>{ACCOUNTS_NOTICE}</p>
      {body}
    </section>
  )
}

/** The chosen account's open items; the balance summary above them is the ledger's own card. */
export function PartyAccountOpenItems({
  companyId,
  partyId,
  companyAccountId,
}: {
  readonly companyId: string
  readonly partyId: string
  readonly companyAccountId: string
}) {
  const read = useBoundedRead<PartyAccountAnswer>(['finance', companyId, 'party-account', partyId, companyAccountId],
    (signal) => fetchPartyAccount(companyId, partyId, companyAccountId, signal))
  const { state } = read

  let body
  if (state.kind === 'initial-loading') body = <p role="status">{ITEMS_LOADING}</p>
  else if (state.kind === 'forbidden') body = <p role="status">{ITEMS_FORBIDDEN}</p>
  else if (state.kind !== 'ready') body = <StateBlock state={state} subject={ITEMS_SUBJECT} onRetry={read.refetch} />
  else if (state.data.kind === 'refused') {
    body = <p role="alert">{state.data.refusal === 'TooManyFacts' ? ITEMS_TOO_MANY_FACTS : ITEMS_TOO_MANY_OPEN}</p>
  } else if (state.data.account.openItemsDisclosure === 'Redacted') body = <p role="status">{ITEMS_REDACTED}</p>
  else if (state.data.account.openItemsDisclosure === 'Unavailable') body = <p role="status">{ITEMS_WITHHELD}</p>
  else if (state.data.account.openItems.length === 0) body = <p role="status">{ITEMS_EMPTY}</p>
  else {
    const items = state.data.account.openItems
    body = (
      <WithCurrencyCodes>
        {(codes) => (
          <table data-party-account-open-items>
            <caption>{ITEMS_TITLE}</caption>
            <thead>
              <tr>
                <th scope="col">{ITEMS_COLUMN_DIRECTION}</th>
                <th scope="col">{ITEMS_COLUMN_DUE}</th>
                <th scope="col">{ITEMS_COLUMN_CURRENCY}</th>
                <th scope="col">{ITEMS_COLUMN_ORIGINAL}</th>
                <th scope="col">{ITEMS_COLUMN_OUTSTANDING}</th>
                <th scope="col">{ITEMS_COLUMN_SOURCE}</th>
              </tr>
            </thead>
            <tbody>
              {items.map((item) => (
                <tr key={item.openItemId} data-open-item={item.openItemId}>
                  <td>{DIRECTION_LABEL[item.direction]}</td>
                  <td>{item.dueOn === null ? ITEMS_NO_DUE : dayText(item.dueOn)}</td>
                  <td>{currencyShown(item.currencyId, codes)}</td>
                  <td data-amount-kind="visible">{moneyText(item.originalAmount)}</td>
                  <td data-amount-kind="visible">{moneyText(item.outstandingAmount)}</td>
                  <td>{reference(item.sourceId)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </WithCurrencyCodes>
    )
  }

  return (
    <section aria-labelledby="finance-party-account-items-title" className={styles.ledger}>
      <h2 id="finance-party-account-items-title">{ITEMS_TITLE}</h2>
      <p className={styles.hint}>{ITEMS_NOTICE}</p>
      {body}
    </section>
  )
}
