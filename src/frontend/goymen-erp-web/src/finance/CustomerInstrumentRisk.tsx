/**
 * DEC-FIN-016 risk tracking (user decision 2026-09-27): the customer's debt closes at COLLECTION, and the payment /
 * risk status of the customer's cheques and notes is tracked separately — here, as facts only.
 *
 * Per customer account and currency (and their total), the count and face amount by risk state as of a date — in the
 * portfolio (not yet due / overdue by the due date), at the bank for collection, dishonoured (karşılıksız /
 * protestolu), returned, collected — plus the open exposure (received, neither collected nor returned). No limit, no
 * threshold and no warning: OPEN-010 is open. Amounts are shown only as the server disclosed them; counts always.
 * Amounts of different currencies are never added.
 */

import { useState } from 'react'
import { StateBlock } from '../shell/StateBlock'
import { WithCurrencyCodes, currencyShown, type CurrencyCodes } from './currencyCodes'
import {
  RISK_AS_OF,
  RISK_CUSTOMERS,
  RISK_EMPTY,
  RISK_LABEL,
  RISK_NOTICE,
  RISK_TITLE,
  RISK_UNAVAILABLE,
} from './customerInstrumentText'
import {
  RISK_BUCKETS,
  decodeCustomerInstrumentRisk,
  type CustomerInstrumentRiskBucket,
  type CustomerInstrumentRiskFigures,
} from './customerInstrumentContract'
import { isDateOnly } from './dateOnly'
import { useFinanceRead } from './financePort'
import { amountShown, amountText } from './sourceView'
import styles from './Finance.module.css'

/** The browser's own calendar day: "as of today" is the user's today, not the server's UTC day. */
function today(): string {
  const now = new Date()
  const pad = (value: number) => String(value).padStart(2, '0')
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`
}

function accountReference(companyAccountId: string): string {
  return `Cari hesap ref. …${companyAccountId.slice(-8)}`
}

function Bucket({ bucket }: { readonly bucket: CustomerInstrumentRiskBucket }) {
  const amount = amountShown(bucket.amount)
  return (
    <>
      <td data-risk-count>{bucket.count}</td>
      <td data-amount-kind={amount.kind}>{amountText(amount)}</td>
    </>
  )
}

function FiguresTable({
  caption,
  figures,
}: {
  readonly caption: string
  readonly figures: CustomerInstrumentRiskFigures
}) {
  return (
    <table>
      <caption>{caption}</caption>
      <thead>
        <tr>
          <th scope="col">Durum</th>
          <th scope="col">Adet</th>
          <th scope="col">Tutar</th>
        </tr>
      </thead>
      <tbody>
        {RISK_BUCKETS.map((name) => (
          <tr key={name} data-risk-bucket={name}>
            <th scope="row">{RISK_LABEL[name]}</th>
            <Bucket bucket={figures[name]} />
          </tr>
        ))}
      </tbody>
    </table>
  )
}

function CustomersTable({
  lines,
  codes,
}: {
  readonly lines: readonly { readonly companyAccountId: string; readonly currencyId: string; readonly figures: CustomerInstrumentRiskFigures }[]
  readonly codes: CurrencyCodes
}) {
  return (
    <table>
      <caption>{RISK_CUSTOMERS}</caption>
      <thead>
        <tr>
          <th scope="col">Cari hesap</th>
          <th scope="col">Para birimi</th>
          {RISK_BUCKETS.map((name) => <th key={name} scope="col" colSpan={2}>{RISK_LABEL[name]}</th>)}
        </tr>
      </thead>
      <tbody>
        {lines.map((line) => (
          <tr key={`${line.companyAccountId}-${line.currencyId}`}>
            <th scope="row">{accountReference(line.companyAccountId)}</th>
            <td>{currencyShown(line.currencyId, codes)}</td>
            {RISK_BUCKETS.map((name) => <Bucket key={name} bucket={line.figures[name]} />)}
          </tr>
        ))}
      </tbody>
    </table>
  )
}

/**
 * The risk block: the company view (with the per-customer lines) when `party` is null, one customer account's view
 * otherwise. Hidden for an actor without the portfolio read grant, like the portfolio itself.
 */
export function CustomerInstrumentRiskBlock({
  party,
}: {
  readonly party: { readonly partyId: string; readonly companyAccountId: string } | null
}) {
  const [asOf, setAsOf] = useState(today)
  const valid = isDateOnly(asOf)
  const partyId = party?.partyId ?? null
  const companyAccountId = party?.companyAccountId ?? null
  const risk = useFinanceRead('customer-instrument-risk', [valid ? asOf : null, partyId, companyAccountId],
    (port, scope, signal) => port.customerInstrumentRisk(scope, { asOf, partyId, companyAccountId }, signal),
    decodeCustomerInstrumentRisk)

  if (risk.state.kind === 'forbidden') return null

  const body = (() => {
    if (!valid) return null
    if (risk.state.kind === 'service-unavailable') return <p role="status">{RISK_UNAVAILABLE}</p>
    if (risk.state.kind !== 'ready') return <StateBlock state={risk.state} subject={RISK_TITLE} onRetry={risk.refetch} />
    const view = risk.state.data
    if (view.totals.length === 0) return <p role="status">{RISK_EMPTY}</p>
    return (
      <WithCurrencyCodes>
        {(codes) => (
          <div className={styles.list}>
            {view.totals.map((total) => (
              <FiguresTable key={total.currencyId}
                caption={`${party === null ? 'Şirket toplamı' : 'Bu cari'} — ${currencyShown(total.currencyId, codes)}`}
                figures={total.figures} />
            ))}
            {party === null ? <CustomersTable lines={view.customers} codes={codes} /> : null}
          </div>
        )}
      </WithCurrencyCodes>
    )
  })()

  return (
    <section aria-label={RISK_TITLE} className={styles.detail}>
      <h2>{RISK_TITLE}</h2>
      <p className={styles.notice}>{RISK_NOTICE}</p>
      <div className={styles.field}>
        <label htmlFor={party === null ? 'risk-as-of-company' : 'risk-as-of-customer'}>{RISK_AS_OF}</label>
        <input id={party === null ? 'risk-as-of-company' : 'risk-as-of-customer'} type="date" value={asOf}
          onChange={(event) => setAsOf(event.target.value)} />
      </div>
      {body}
    </section>
  )
}
