import { useQuery } from '@tanstack/react-query'
import type { DraftDetail } from './contract'
import type { InvoiceLine } from './invoiceFinalizationContract'
import { fetchDraftKilogramPrices } from './kilogramPriceApi'
import type { InvoiceKilogramPrices, KilogramPrice, OrderKilogramPrices } from './kilogramPriceContract'
import { KILOGRAM_PRICE_TEXT as T } from './salesText'
import { contextKey, contextKeyString, type ShellState } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome } from '../shell/uiState'
import styles from './SalesPages.module.css'

/** A server kg price as text: the value per kg, or why it is not shown — never a figure the screen computed. */
export function kilogramPriceText(price: KilogramPrice): string {
  return price.state === 'Shown' && price.value !== null ? T.perKg(price.value) : T.state[price.state]
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
  if (outcome?.type !== 'ok-single') {
    return (
      <section className={styles.actions} aria-label={T.heading} data-testid="sales-kilogram-prices">
        <StateBlock state={outcome === undefined ? { kind: 'initial-loading' } : classifyOutcome(outcome)} subject={T.subject}
          onRetry={() => void query.refetch()} />
      </section>
    )
  }
  return <OrderKilogramPricesView prices={outcome.data} />
}

export function OrderKilogramPricesView({ prices }: { readonly prices: OrderKilogramPrices }) {
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
              <div><dt>{T.theoreticalPrice}</dt><dd data-testid={`sales-kilogram-line-${line.lineNo}-theoretical`}>{kilogramPriceText(line.theoreticalPrice)}</dd></div>
              <div><dt>{T.invoicedQuantity}</dt><dd>{line.invoicedQuantity}</dd></div>
              <div><dt>{T.invoicedAmount}</dt><dd>{line.invoicedAmount ?? T.none}</dd></div>
              <div><dt>{T.actualKg}</dt><dd>{line.actualKg ?? T.none}</dd></div>
              <div><dt>{T.actualPrice}</dt><dd data-testid={`sales-kilogram-line-${line.lineNo}-actual`}>{kilogramPriceText(line.actualPrice)}</dd></div>
              <div><dt>{T.kgDifference}</dt><dd>{line.kgDifference ?? T.none}</dd></div>
              <div><dt>{T.deviationPercent}</dt><dd>{line.deviationPercent === null ? T.none : T.percent(line.deviationPercent)}</dd></div>
            </dl>
          ))}
          <dl className={styles.facts} aria-label={T.heading}>
            <div><dt>{T.documentTheoretical}</dt><dd data-testid="sales-kilogram-document-theoretical">{kilogramPriceText(prices.documentTheoreticalPrice!)}</dd></div>
            <div><dt>{T.documentActual}</dt><dd data-testid="sales-kilogram-document-actual">{kilogramPriceText(prices.documentActualPrice!)}</dd></div>
          </dl>
        </>
      )}
    </section>
  )
}

/** SAT-D434 / SAT-D436: an invoice's kg prices (present only on an invoice with a piece line). */
export function InvoiceKilogramPricesView({ prices, lines }: {
  readonly prices: InvoiceKilogramPrices
  readonly lines: readonly InvoiceLine[]
}) {
  const lineNoOf = (lineId: string) => lines.find((line) => line.lineId === lineId)?.lineNo ?? 0
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
          {prices.lines.map((line) => (
            <div key={line.lineId}>
              <dt>{T.line(lineNoOf(line.lineId))}</dt>
              <dd data-testid={`sales-invoice-kilogram-line-${lineNoOf(line.lineId)}`}>
                {`${T.theoreticalPrice}: ${kilogramPriceText(line.theoreticalPrice)} · ${T.actualPrice}: ${kilogramPriceText(line.actualPrice)}`}
              </dd>
            </div>
          ))}
          <div><dt>{T.documentTheoretical}</dt><dd data-testid="sales-invoice-kilogram-document-theoretical">{kilogramPriceText(prices.documentTheoreticalPrice!)}</dd></div>
          <div><dt>{T.documentActual}</dt><dd data-testid="sales-invoice-kilogram-document-actual">{kilogramPriceText(prices.documentActualPrice!)}</dd></div>
        </dl>
      )}
    </section>
  )
}
