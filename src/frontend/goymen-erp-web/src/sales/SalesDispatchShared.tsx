/**
 * Pieces S-07 and S-08 share: the two-screen tabs, the customer cell and the order-line link. Presentation only; every wire
 * check is in ./dispatchApi.ts and ./dispatchContract.ts.
 */

import { Link, useLocation } from 'react-router'
import { SALES_CAPABILITY, SALES_ROUTE, salesDetailPath } from './capabilities'
import type { DispatchCustomer } from './dispatchContract'
import { DISPATCH_TEXT } from './dispatchText'
import { CAPABILITY, CAPABILITY_ROUTE } from '../shell/capabilities'
import { useShellState } from '../shell/shellContext'
import { hasCapability } from '../shell/shellState'
import styles from './SalesPool.module.css'

const C = DISPATCH_TEXT.common

/** Sevkiyat / İrsaliyeler — Faturaya Hazır: drawn only when the shell publishes both lists to this actor. */
export function DispatchTabs() {
  const { pathname } = useLocation()
  const shell = useShellState()
  const tabs = [
    hasCapability(shell, SALES_CAPABILITY.dispatchesList) ? { to: SALES_ROUTE.dispatches, label: C.dispatchesTab } : null,
    hasCapability(shell, SALES_CAPABILITY.invoiceCandidatesList) ? { to: SALES_ROUTE.invoiceCandidates, label: C.candidatesTab } : null,
  ].filter((tab) => tab !== null)
  if (tabs.length < 2) return null
  return (
    <nav aria-label={C.nav}>
      <ul className={styles.tabs}>
        {tabs.map((tab) => {
          const current = pathname === tab.to
          return (
            <li key={tab.to}>
              <Link to={tab.to} className={current ? styles.tabCurrent : styles.tab} aria-current={current ? 'page' : undefined}>
                {tab.label}
              </Link>
            </li>
          )
        })}
      </ul>
    </nav>
  )
}

/** The customer as the Cari owner presents it now; linked to the Cari card only with that card's own capability. */
export function DispatchCustomerCell({ customer }: { readonly customer: DispatchCustomer }) {
  const shell = useShellState()
  const label = `${customer.code} — ${customer.legalName}`
  return hasCapability(shell, CAPABILITY.cariPartiesDetail) ? (
    <Link className={styles.link} to={CAPABILITY_ROUTE[CAPABILITY.cariPartiesDetail].replace(':partyId', encodeURIComponent(customer.partyId))}>
      {label}
    </Link>
  ) : (
    <span>{label}</span>
  )
}

/** The Sales order (document number and line), linked to Sipariş 360 only with that screen's own capability. */
export function DispatchOrderCell({ orderId, documentNo, lineNo }: { readonly orderId: string; readonly documentNo: string; readonly lineNo: number }) {
  const shell = useShellState()
  const label = `${documentNo} / ${lineNo}`
  return hasCapability(shell, SALES_CAPABILITY.draftsDetail) ? (
    <Link className={styles.link} to={salesDetailPath(orderId)} aria-label={`${C.order} ${label}`}>
      {label}
    </Link>
  ) : (
    <span>{label}</span>
  )
}
