/**
 * ALC-S13 / UI-K09 — the Cari 360 tabs: Satış, Alış and Finans, each read ONLY through its owner module's port.
 *
 * - Satış: Sales' open-order report (API-41) asked for this one company account (`companyAccountId`); a group of another
 *   account makes the page malformed (the transport refuses it), so nothing foreign is shown.
 * - Alış: Purchasing's order list asked for this supplier (`supplierPartyId`, #671); a page holding another supplier's,
 *   another company's or another account's order is refused whole.
 * - Finans: Finance's party-account view (B-05, `GET /finance/party-accounts/{partyId}/{companyAccountId}`): the all-time
 *   balance summary per currency (API-04) and the open items, as Finance disclosed them. `Redacted`, `Unavailable` and
 *   the two stated 422 refusals each have their own sentence; nothing is converted or added across currencies (OPEN-012),
 *   and no list total is ever named a balance — the balance is the view's `currencies[].balance` only.
 *
 * Cari computes nothing here. A tab is offered only under its owner's route capability, and no tab is selected until the
 * user picks one, so opening the card still sends exactly the card's own reads. The company is the verified shell
 * company in `X-Company-Id`; no request carries a CompanyId of its own. A 401/403 goes to the shared auth boundary.
 */

import { useId, useRef, useState, type KeyboardEvent, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { CARI_360_TEXT, type Cari360Tab } from './cari360Text'
import { fetchOpenOrderReport, reportFilterKey, REPORT_PARAM } from '../sales/reportApi'
import type { OpenOrderGroup, OpenOrderReport } from '../sales/reportContract'
import { SALES_ROUTE } from '../sales/capabilities'
import { fetchPurchaseOrders } from '../purchasing/api'
import type { PurchaseOrderList } from '../purchasing/contract'
import { PURCHASING_CAPABILITY, PURCHASING_ROUTE, purchaseOrderDetailPath } from '../purchasing/capabilities'
import { PURCHASE_ORDER_STATUS_LABEL } from '../purchasing/purchasingText'
import { PURCHASE_ORDER_STATUS_TONE } from '../purchasing/purchaseOrderStatus'
import { fetchPartyAccount } from '../finance/partyAccountsHttp'
import type { PartyAccountAnswer, PartyAccountView } from '../finance/partyAccountsContract'
import type { AmountView, MoneyText } from '../finance/contract'
import type { PartyLedgerBalance } from '../finance/partyLedgerContract'
import { WithCurrencyCodes, currencyShown } from '../finance/currencyCodes'
import { amountShown, amountText } from '../finance/sourceView'
import {
  BALANCE_COLUMN_BALANCE,
  BALANCE_COLUMN_CREDIT,
  BALANCE_COLUMN_CURRENCY,
  BALANCE_COLUMN_DEBIT,
  BALANCE_COLUMN_OPEN_PAYABLE,
  BALANCE_COLUMN_OPEN_RECEIVABLE,
  BALANCE_EMPTY,
  BALANCE_NOTICE,
  BALANCE_SIDE_HINT,
  BALANCE_SIDE_SUFFIX,
  BALANCE_TITLE,
  LEDGER_PER_CURRENCY,
  reference,
} from '../finance/partyLedgerText'
import {
  DIRECTION_LABEL,
  ITEMS_COLUMN_CURRENCY,
  ITEMS_COLUMN_DIRECTION,
  ITEMS_COLUMN_DUE,
  ITEMS_COLUMN_ORIGINAL,
  ITEMS_COLUMN_OUTSTANDING,
  ITEMS_COLUMN_SOURCE,
  ITEMS_EMPTY,
  ITEMS_NO_DUE,
  ITEMS_NOTICE,
  ITEMS_TITLE,
  ITEMS_TOO_MANY_FACTS,
  ITEMS_TOO_MANY_OPEN,
} from '../finance/partyAccountsText'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState, type ViewOutcome } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StatusIndicator } from '../ui/StatusIndicator'
import cardStyles from './CariDetailPage.module.css'
import styles from './Cari360Tabs.module.css'

const T = CARI_360_TEXT
const TAB_ORDER: readonly Cari360Tab[] = ['sales', 'purchasing', 'finance']

export interface Cari360TabsProps {
  readonly partyId: string
  readonly companyAccountId: string
  /** The verified shell company; the account above was read for it. */
  readonly companyId: string
  /** The shell context namespace: every read is keyed under it, so another company's answer is never reused. */
  readonly namespace: string
  readonly canViewSales: boolean
  readonly canListPurchaseOrders: boolean
  readonly canOpenPurchaseOrder: boolean
  readonly canViewFinance: boolean
}

export function Cari360Tabs(props: Cari360TabsProps) {
  const tabs = TAB_ORDER.filter((tab) =>
    tab === 'sales' ? props.canViewSales : tab === 'purchasing' ? props.canListPurchaseOrders : props.canViewFinance)
  const [active, setActive] = useState<Cari360Tab | null>(null)
  const baseId = useId()
  const tabRefs = useRef<Partial<Record<Cari360Tab, HTMLButtonElement | null>>>({})
  if (tabs.length === 0) return null

  const selected = active !== null && tabs.includes(active) ? active : null
  const tabId = (tab: Cari360Tab) => `${baseId}-tab-${tab}`
  const panelId = `${baseId}-panel`

  function move(event: KeyboardEvent<HTMLButtonElement>, current: Cari360Tab) {
    const index = tabs.indexOf(current)
    let next: Cari360Tab | null = null
    if (event.key === 'ArrowRight') next = tabs[(index + 1) % tabs.length]
    else if (event.key === 'ArrowLeft') next = tabs[(index - 1 + tabs.length) % tabs.length]
    else if (event.key === 'Home') next = tabs[0]
    else if (event.key === 'End') next = tabs[tabs.length - 1]
    if (next === null) return
    event.preventDefault()
    setActive(next)
    tabRefs.current[next]?.focus()
  }

  return (
    <section aria-labelledby={`${baseId}-heading`} className={cardStyles.block} data-section="cari-360">
      <h2 id={`${baseId}-heading`} className={cardStyles.blockTitle}>{T.heading}</h2>
      <p className={cardStyles.blockNote}>{T.note}</p>
      <div className={styles.tabs}>
        <div role="tablist" aria-label={T.tabsLabel} className={styles.tabList}>
          {tabs.map((tab, index) => {
            const isSelected = tab === selected
            return (
              <button
                key={tab}
                ref={(node) => { tabRefs.current[tab] = node }}
                id={tabId(tab)}
                type="button"
                role="tab"
                className={styles.tab}
                aria-selected={isSelected}
                aria-controls={isSelected ? panelId : undefined}
                // Roving tabindex; with nothing picked yet the first tab is the way in.
                tabIndex={isSelected || (selected === null && index === 0) ? 0 : -1}
                data-cari-360-tab={tab}
                onClick={() => setActive(tab)}
                onKeyDown={(event) => move(event, tab)}
              >
                {T.tab[tab]}
              </button>
            )
          })}
        </div>
        {selected === null ? (
          <p className={styles.muted} data-testid="cari-360-pick">{T.pick}</p>
        ) : (
          <div id={panelId} role="tabpanel" aria-labelledby={tabId(selected)} tabIndex={0} className={styles.panel}
            data-cari-360-panel={selected}>
            {selected === 'sales' ? <SalesTab {...props} /> : null}
            {selected === 'purchasing' ? <PurchasingTab {...props} /> : null}
            {selected === 'finance' ? <FinanceTab {...props} /> : null}
          </div>
        )}
      </div>
    </section>
  )
}

/** One owner read: keyed under the shell context, 401/403 reported to the auth boundary with the bindings of its start. */
function useOwnerRead<V>(key: readonly unknown[], load: (signal: AbortSignal) => Promise<ViewOutcome<V>>) {
  const boundary = useAuthBoundary()
  const query = useQuery({
    queryKey: key,
    queryFn: async ({ signal }): Promise<ViewOutcome<V>> => {
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
    : classifyOutcome(query.data ?? { type: 'malformed' })) as RemoteViewState<V>
  return { state, retry: () => { void query.refetch() } }
}

/** A date-only wire value shown the Turkish way (gg.aa.yyyy); anything else is shown as received, never parsed. */
function dayText(value: string): string {
  const parts = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value)
  return parts === null ? value : `${parts[3]}.${parts[2]}.${parts[1]}`
}

/* ── Satış ─────────────────────────────────────────────────────────────────── */

function SalesTab({ partyId, companyAccountId, companyId, namespace }: Cari360TabsProps) {
  const S = T.sales
  const filterKey = reportFilterKey({ from: null, to: null, companyAccountId })
  const read = useOwnerRead<OpenOrderReport>(
    [namespace, 'cari-360', 'sales-open-orders', partyId, companyAccountId, companyId],
    (signal) => fetchOpenOrderReport(null, companyId, signal, filterKey),
  )
  const reportLink = (
    <Link className={styles.link} data-testid="cari-360-sales-report-link"
      to={`${SALES_ROUTE.reports}?${new URLSearchParams({ [REPORT_PARAM.tab]: 'open-orders', [REPORT_PARAM.account]: companyAccountId }).toString()}`}>
      {S.openReport}
    </Link>
  )
  const intro = <p className={styles.muted}>{S.intro}</p>
  const { state } = read
  if (state.kind === 'forbidden') return <p role="status">{S.forbidden}</p>
  if (state.kind !== 'ready') return <StateBlock state={state} subject={S.subject} onRetry={read.retry} />
  const { items, nextCursor } = state.data
  if (items.length === 0) {
    return (
      <>
        {intro}
        {nextCursor !== null ? (
          // The server's bounded scan ended this page before it found a group: never "none".
          <p className={styles.notice} role="status" data-testid="cari-360-sales-scan-continues">{S.scanContinues}</p>
        ) : (
          <p role="status" data-testid="cari-360-sales-empty">{S.empty}</p>
        )}
        {nextCursor !== null ? reportLink : null}
      </>
    )
  }
  const quantity = (group: OpenOrderGroup, value: string | null) =>
    value === null ? <span className={styles.muted}>{S.none}</span> : `${value} ${group.unitCode}`
  return (
    <>
      {intro}
      <div className={styles.scroll}>
        <table className={styles.table} data-cari-360-sales>
          <caption className={styles.caption}>{S.caption}</caption>
          <thead>
            <tr>
              <th scope="col">{S.product}</th>
              <th scope="col">{S.unit}</th>
              <th scope="col" className={styles.numeric}>{S.orderCount}</th>
              <th scope="col" className={styles.numeric}>{S.remaining}</th>
              <th scope="col" className={styles.numeric}>{S.awaitingAcceptance}</th>
            </tr>
          </thead>
          <tbody>
            {items.map((group) => (
              <tr key={group.id} data-group-id={group.id}>
                <th scope="row">{`${group.productCode} ${group.productName}`}</th>
                <td>{group.unitCode}</td>
                <td className={styles.numeric}>{group.orderCount}</td>
                <td className={styles.numeric}>
                  {quantity(group, group.remainingQuantity)}
                  {group.state === 'TooManyOrders' ? (
                    <StatusIndicator tone="warning">
                      <span data-testid={`cari-360-sales-too-many-${group.id}`}>{S.tooManyOrders(group.scopeOrderCount)}</span>
                    </StatusIndicator>
                  ) : group.complete ? null : (
                    <StatusIndicator tone="warning">
                      <span data-testid={`cari-360-sales-incomplete-${group.id}`}>{S.incomplete}</span>
                    </StatusIndicator>
                  )}
                </td>
                <td className={styles.numeric}>{quantity(group, group.awaitingAcceptanceQuantity)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {nextCursor !== null ? (
        <>
          <p className={styles.notice} role="status" data-testid="cari-360-sales-more">{S.more}</p>
          {reportLink}
        </>
      ) : null}
    </>
  )
}

/* ── Alış ──────────────────────────────────────────────────────────────────── */

/** Purchasing's supplier-filtered first page, refused whole if one order is bound to another account of the party. */
async function supplierOrders(companyId: string, partyId: string, companyAccountId: string,
  signal: AbortSignal): Promise<ViewOutcome<PurchaseOrderList>> {
  const outcome = await fetchPurchaseOrders(companyId, { supplierPartyId: partyId }, signal)
  if (outcome.type !== 'ok-single') return outcome
  return outcome.data.items.every((order) => order.companyAccountId === companyAccountId)
    ? outcome
    : { type: 'malformed' }
}

function PurchasingTab({ partyId, companyAccountId, companyId, namespace, canOpenPurchaseOrder }: Cari360TabsProps) {
  const P = T.purchasing
  const read = useOwnerRead<PurchaseOrderList>(
    [namespace, 'cari-360', 'purchase-orders', partyId, companyAccountId, companyId],
    (signal) => supplierOrders(companyId, partyId, companyAccountId, signal),
  )
  const intro = <p className={styles.muted}>{P.intro}</p>
  const { state } = read
  if (state.kind === 'forbidden') return <p role="status">{P.forbidden}</p>
  if (state.kind !== 'ready') return <StateBlock state={state} subject={P.subject} onRetry={read.retry} />
  const { items, nextCursor } = state.data
  const listLink = (
    <Link className={styles.link} data-testid="cari-360-purchase-orders-link"
      to={`${PURCHASING_ROUTE[PURCHASING_CAPABILITY.orderList]}?${new URLSearchParams({ supplierPartyId: partyId }).toString()}`}>
      {P.openList}
    </Link>
  )
  if (items.length === 0) {
    return (
      <>
        {intro}
        {nextCursor === null
          ? <p role="status" data-testid="cari-360-purchase-orders-empty">{P.empty}</p>
          : <><p className={styles.notice} role="status">{P.more}</p>{listLink}</>}
      </>
    )
  }
  return (
    <>
      {intro}
      <div className={styles.scroll}>
        <table className={styles.table} data-cari-360-purchase-orders>
          <caption className={styles.caption}>{P.caption}</caption>
          <thead>
            <tr>
              <th scope="col">{P.order}</th>
              <th scope="col">{P.status}</th>
              <th scope="col">{P.businessDate}</th>
              <th scope="col" className={styles.numeric}>{P.lineCount}</th>
            </tr>
          </thead>
          <tbody>
            {items.map((order) => (
              <tr key={order.id} data-purchase-order-id={order.id}>
                <th scope="row">
                  {canOpenPurchaseOrder
                    ? <Link className={styles.link} to={purchaseOrderDetailPath(order.id)}>{`…${order.id.slice(-8)}`}</Link>
                    : `…${order.id.slice(-8)}`}
                </th>
                <td>
                  <StatusIndicator tone={PURCHASE_ORDER_STATUS_TONE[order.status]}>
                    {PURCHASE_ORDER_STATUS_LABEL[order.status]}
                  </StatusIndicator>
                </td>
                <td>{dayText(order.effectiveOn)}</td>
                <td className={styles.numeric}>{order.lineCount}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {nextCursor !== null ? (
        <>
          <p className={styles.notice} role="status" data-testid="cari-360-purchase-orders-more">{P.more}</p>
          {listLink}
        </>
      ) : null}
    </>
  )
}

/* ── Finans ────────────────────────────────────────────────────────────────── */

function amountCell(view: AmountView): ReactNode {
  const shown = amountShown(view)
  return <td className={styles.numeric} data-amount-kind={shown.kind}>{amountText(shown)}</td>
}

function balanceText(balance: PartyLedgerBalance): string {
  const shown = amountText(amountShown(balance.amount))
  return balance.side === null ? shown : `${shown}${BALANCE_SIDE_SUFFIX[balance.side]}`
}

function moneyText(money: MoneyText): string {
  return amountText({ kind: 'visible', amount: money.amount })
}

function FinanceTab({ partyId, companyAccountId, companyId, namespace }: Cari360TabsProps) {
  const F = T.finance
  const read = useOwnerRead<PartyAccountAnswer>(
    [namespace, 'cari-360', 'finance-party-account', partyId, companyAccountId, companyId],
    (signal) => fetchPartyAccount(companyId, partyId, companyAccountId, signal),
  )
  const { state } = read
  if (state.kind === 'initial-loading') return <p role="status">{F.loading}</p>
  if (state.kind === 'forbidden') return <p role="status" data-testid="cari-360-finance-forbidden">{F.forbidden}</p>
  if (state.kind === 'service-unavailable') {
    return <p role="status" data-testid="cari-360-finance-unavailable">{F.unavailable}</p>
  }
  if (state.kind !== 'ready') return <StateBlock state={state} subject={F.subject} onRetry={read.retry} />
  const answer = state.data
  if (answer.kind === 'refused') {
    return (
      <p role="alert" data-testid="cari-360-finance-refused" data-refusal={answer.refusal}>
        {answer.refusal === 'TooManyFacts' ? ITEMS_TOO_MANY_FACTS : ITEMS_TOO_MANY_OPEN}
      </p>
    )
  }
  const view = answer.account
  // The decoder holds every amount of the view to the open items' disclosure, so one sentence covers the whole account.
  if (view.openItemsDisclosure === 'Redacted') {
    return <p role="status" data-testid="cari-360-finance-redacted">{F.redacted}</p>
  }
  if (view.openItemsDisclosure === 'Unavailable') {
    return <p role="status" data-testid="cari-360-finance-withheld">{F.withheld}</p>
  }
  return (
    <WithCurrencyCodes>
      {(codes) => (
        <>
          <p className={styles.muted}>{LEDGER_PER_CURRENCY}</p>
          <FinanceBalance view={view} codes={codes} />
          <FinanceOpenItems view={view} codes={codes} />
        </>
      )}
    </WithCurrencyCodes>
  )
}

type Codes = Parameters<typeof currencyShown>[1]

function FinanceBalance({ view, codes }: { readonly view: PartyAccountView; readonly codes: Codes }) {
  if (view.currencies.length === 0) return <p role="status" data-testid="cari-360-finance-balance-empty">{BALANCE_EMPTY}</p>
  return (
    <>
      <p className={styles.muted}>{BALANCE_NOTICE}</p>
      <div className={styles.scroll}>
        <table className={styles.table} data-cari-360-balance>
          <caption className={styles.caption}>{BALANCE_TITLE}</caption>
          <thead>
            <tr>
              <th scope="col">{BALANCE_COLUMN_CURRENCY}</th>
              <th scope="col" className={styles.numeric}>{BALANCE_COLUMN_DEBIT}</th>
              <th scope="col" className={styles.numeric}>{BALANCE_COLUMN_CREDIT}</th>
              <th scope="col" className={styles.numeric}>{BALANCE_COLUMN_BALANCE}</th>
              <th scope="col" className={styles.numeric}>{BALANCE_COLUMN_OPEN_RECEIVABLE}</th>
              <th scope="col" className={styles.numeric}>{BALANCE_COLUMN_OPEN_PAYABLE}</th>
            </tr>
          </thead>
          <tbody>
            {view.currencies.map((currency) => (
              <tr key={currency.currencyId} data-balance-currency={currency.currencyId}>
                <th scope="row">{currencyShown(currency.currencyId, codes)}</th>
                {amountCell(currency.debitTotal)}
                {amountCell(currency.creditTotal)}
                <td className={styles.numeric} data-amount-kind={amountShown(currency.balance.amount).kind}>
                  {balanceText(currency.balance)}
                </td>
                {amountCell(currency.openReceivable)}
                {amountCell(currency.openPayable)}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <p className={styles.muted}>{BALANCE_SIDE_HINT}</p>
    </>
  )
}

function FinanceOpenItems({ view, codes }: { readonly view: PartyAccountView; readonly codes: Codes }) {
  if (view.openItems.length === 0) return <p role="status" data-testid="cari-360-finance-items-empty">{ITEMS_EMPTY}</p>
  return (
    <>
      <p className={styles.muted}>{ITEMS_NOTICE}</p>
      <div className={styles.scroll}>
        <table className={styles.table} data-cari-360-open-items>
          <caption className={styles.caption}>{ITEMS_TITLE}</caption>
          <thead>
            <tr>
              <th scope="col">{ITEMS_COLUMN_DIRECTION}</th>
              <th scope="col">{ITEMS_COLUMN_DUE}</th>
              <th scope="col">{ITEMS_COLUMN_CURRENCY}</th>
              <th scope="col" className={styles.numeric}>{ITEMS_COLUMN_ORIGINAL}</th>
              <th scope="col" className={styles.numeric}>{ITEMS_COLUMN_OUTSTANDING}</th>
              <th scope="col">{ITEMS_COLUMN_SOURCE}</th>
            </tr>
          </thead>
          <tbody>
            {view.openItems.map((item) => (
              <tr key={item.openItemId} data-open-item={item.openItemId}>
                <td>{DIRECTION_LABEL[item.direction]}</td>
                <td>{item.dueOn === null ? ITEMS_NO_DUE : dayText(item.dueOn)}</td>
                <td>{currencyShown(item.currencyId, codes)}</td>
                <td className={styles.numeric} data-amount-kind="visible">{moneyText(item.originalAmount)}</td>
                <td className={styles.numeric} data-amount-kind="visible">{moneyText(item.outstandingAmount)}</td>
                <td>{reference(item.sourceId)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </>
  )
}
