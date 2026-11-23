import { useContext } from 'react'
import { useQuery } from '@tanstack/react-query'
import type { DraftDetail } from './contract'
import type { InvoiceLine } from './invoiceFinalizationContract'
import { fetchDraftKilogramPrices } from './kilogramPriceApi'
import type { InvoiceKilogramPrices, OrderKilogramPrices } from './kilogramPriceContract'
import { KILOGRAM_PRICE_TEXT as T } from './salesText'
import { contextKey, contextKeyString, hasCapability, type ShellState } from '../shell/shellState'
import { useShellState } from '../shell/shellContext'
import { SALES_CAPABILITY } from './capabilities'
import { SalesCurrencySourceContext } from './referenceSource'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { kilogramPriceText } from './kilogramPriceText'
import styles from './SalesPages.module.css'

/**
 * The document currency's code for a kg price (SAT-D439: the response's own currency, no rate). The currencies reference is the
 * create form's read (sales.drafts.create, as on the risk card): without it, or while it is not read, a neutral text stands
 * for the document currency — never a guessed code, and never the raw identity (a GUID says nothing to the user).
 */
function useCurrencyName(shell: ShellState, companyId: string, effectiveOn: string): (currencyId: string) => string {
  const useCurrencies = useContext(SalesCurrencySourceContext)
  const currencies = useCurrencies(hasCapability(shell, SALES_CAPABILITY.draftsCreate) ? companyId : '', effectiveOn)
  return (currencyId) => (currencies.status === 'ready'
    ? currencies.data.find((candidate) => candidate.currencyId.toLowerCase() === currencyId.toLowerCase())?.code
    : undefined) ?? T.currencyUnnamed
}

/**
 * SAT-D436 (AK-5): the order's kg prices and realization — read only by a caller who may see prices (a withheld line means
 * the price read is not held: nothing is asked). Closed until its formula golden is in main: the server then says so.
 */
export function SalesOrderKilogramPrices({ shell, detail, companyId }: {
  readonly shell: ShellState
  readonly detail: DraftDetail
  readonly companyId: string
}) {
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const boundary = useAuthBoundary()
  const currencyName = useCurrencyName(shell, companyId, detail.draft.businessDate)
  const disclosed = detail.lines.length > 0 && detail.lines.every((line) => line.priceDisclosure === 'Visible')
  const query = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'sales-draft-kilogram-prices', detail.draft.id, detail.draft.version],
    enabled: namespace !== null && disclosed,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchDraftKilogramPrices(detail.draft.id, companyId, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })
  if (!disclosed) return null
  const outcome = query.data
  // Like the line pricing beside it: an information panel never raises a second alert over the order's own states; a
  // denied or unreadable read says so quietly (401/403 are reported to the shell boundary above).
  if (outcome === undefined) return null
  if (outcome.type !== 'ok-single') {
    return <p className={styles.muted} data-testid="sales-kilogram-prices-unreadable">{T.unreadable}</p>
  }
  return <OrderKilogramPricesView prices={outcome.data} currency={currencyName(outcome.data.currencyId)} />
}

export function OrderKilogramPricesView({ prices, currency }: { readonly prices: OrderKilogramPrices; readonly currency: string }) {
  return (
    <section className={styles.actions} aria-label={T.heading} data-testid="sales-kilogram-prices">
      <h3>{T.heading}</h3>
      <p className={styles.muted}>{T.note}</p>
      {prices.state === 'FormulaGoldenPending' || prices.lines === null ? (
        <p className={styles.muted} data-testid="sales-kilogram-prices-pending">{T.pending}</p>
      ) : (
        <>
          {prices.lines.map((line) => (
            <dl key={line.lineId} className={styles.facts} aria-label={T.line(line.lineNo)} data-testid={`sales-kilogram-line-${line.lineNo}`}>
              <div><dt>{T.theoreticalKg}</dt><dd>{line.theoreticalKg ?? T.none}</dd></div>
              <div><dt>{T.theoreticalPrice}</dt><dd data-testid={`sales-kilogram-line-${line.lineNo}-theoretical`}>{kilogramPriceText(line.theoreticalPrice, currency)}</dd></div>
              <div><dt>{T.invoicedQuantity}</dt><dd>{line.invoicedQuantity}</dd></div>
              <div><dt>{T.invoicedAmount}</dt><dd>{line.invoicedAmount ?? T.none}</dd></div>
              <div><dt>{T.actualKg}</dt><dd>{line.actualKg ?? T.none}</dd></div>
              <div><dt>{T.actualPrice}</dt><dd data-testid={`sales-kilogram-line-${line.lineNo}-actual`}>{kilogramPriceText(line.actualPrice, currency)}</dd></div>
              <div><dt>{T.kgDifference}</dt><dd>{line.kgDifference ?? T.none}</dd></div>
              <div><dt>{T.deviationPercent}</dt><dd>{line.deviationPercent === null ? T.none : T.percent(line.deviationPercent)}</dd></div>
            </dl>
          ))}
          <dl className={styles.facts} aria-label={T.heading}>
            <div><dt>{T.documentTheoretical}</dt><dd data-testid="sales-kilogram-document-theoretical">{kilogramPriceText(prices.documentTheoreticalPrice!, currency)}</dd></div>
            <div><dt>{T.documentActual}</dt><dd data-testid="sales-kilogram-document-actual">{kilogramPriceText(prices.documentActualPrice!, currency)}</dd></div>
          </dl>
        </>
      )}
    </section>
  )
}

/** The invoice block's container: resolves the invoice currency's code for the view. */
export function InvoiceKilogramPrices({ prices, lines, currencyId, companyId, businessDate }: {
  readonly prices: InvoiceKilogramPrices
  readonly lines: readonly InvoiceLine[]
  readonly currencyId: string
  readonly companyId: string
  readonly businessDate: string
}) {
  const currencyName = useCurrencyName(useShellState(), companyId, businessDate)
  return <InvoiceKilogramPricesView prices={prices} lines={lines} currency={currencyName(currencyId)} />
}

/** SAT-D434 / SAT-D436: an invoice's kg prices (present only on an invoice with a piece line). */
export function InvoiceKilogramPricesView({ prices, lines, currency }: {
  readonly prices: InvoiceKilogramPrices
  readonly lines: readonly InvoiceLine[]
  readonly currency: string
}) {
  const lineNoOf = (lineId: string) => lines.find((line) => line.lineId.toLowerCase() === lineId.toLowerCase())?.lineNo
  // A kg price line the invoice does not have is not this invoice's answer: the block is withheld, never shown as "0. satır".
  const numbered = prices.lines?.map((line) => ({ line, lineNo: lineNoOf(line.lineId) })) ?? []
  if (numbered.some((entry) => entry.lineNo === undefined)) {
    return <p className={styles.muted} data-testid="sales-invoice-kilogram-prices-unmatched">{T.unmatchedLine}</p>
  }
  return (
    <section className={styles.actions} aria-label={T.heading} data-testid="sales-invoice-kilogram-prices">
      <h4>{T.heading}</h4>
      <p className={styles.muted}>{T.note}</p>
      <dl className={styles.facts} aria-label={T.invoiceTheoreticalKg}>
        {lines.filter((line) => line.theoreticalKg !== undefined).map((line) => (
          <div key={line.lineId}>
            <dt>{T.line(line.lineNo)}</dt>
            <dd data-testid={`sales-invoice-kilogram-info-${line.lineNo}`}>
              {`${T.invoiceTheoreticalKg}: ${line.theoreticalKg ?? T.none} · ${T.invoiceActualKg}: ${line.actualKg ?? T.none}`}
            </dd>
          </div>
        ))}
      </dl>
      {prices.state === 'FormulaGoldenPending' || prices.lines === null ? (
        <p className={styles.muted} data-testid="sales-invoice-kilogram-prices-pending">{T.pending}</p>
      ) : (
        <dl className={styles.facts} aria-label={T.heading}>
          {numbered.map(({ line, lineNo }) => (
            <div key={line.lineId}>
              <dt>{T.line(lineNo!)}</dt>
              <dd data-testid={`sales-invoice-kilogram-line-${lineNo}`}>
                {`${T.theoreticalPrice}: ${kilogramPriceText(line.theoreticalPrice, currency)} · ${T.actualPrice}: ${kilogramPriceText(line.actualPrice, currency)}`}
              </dd>
            </div>
          ))}
          <div><dt>{T.documentTheoretical}</dt><dd data-testid="sales-invoice-kilogram-document-theoretical">{kilogramPriceText(prices.documentTheoreticalPrice!, currency)}</dd></div>
          <div><dt>{T.documentActual}</dt><dd data-testid="sales-invoice-kilogram-document-actual">{kilogramPriceText(prices.documentActualPrice!, currency)}</dd></div>
        </dl>
      )}
    </section>
  )
}
