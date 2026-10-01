import type { DraftDetail } from './contract'
import type { LinePricing } from './linePricing'
import { LINE_PRICING_TEXT as T } from './salesText'
import type { ShellState } from '../shell/shellState'
import { useDraftLinePricing } from './useDraftLinePricing'
import styles from './SalesPages.module.css'

/**
 * AT-018 / AT-021: each line's reference list price, where the unit price came from, the discount and the maturity
 * surcharge as given, and the SERVER's net — read-only, shown to a caller who may see prices.
 */
export function SalesDraftLinePricing({ shell, detail, companyId }: {
  readonly shell: ShellState
  readonly detail: DraftDetail
  readonly companyId: string
}) {
  const query = useDraftLinePricing(shell, detail, companyId)
  if (detail.lines.some((line) => line.priceDisclosure === 'Withheld')) {
    return <p className={styles.muted} data-testid="sales-line-pricing-withheld">{T.withheld}</p>
  }
  const outcome = query.data
  if (outcome === undefined) return null
  if (outcome.type !== 'ok-single') {
    return <p className={styles.muted} data-testid="sales-line-pricing-unreadable">{T.unreadable}</p>
  }
  return (
    <section className={styles.actions} aria-label={T.heading} data-testid="sales-line-pricing">
      <h3>{T.heading}</h3>
      <p className={styles.muted}>{T.note}</p>
      {outcome.data.lines.map((line) => <PricingFacts key={line.lineId} line={line} />)}
    </section>
  )
}

function adjustment(percent: string | null, amount: string | null): string {
  if (percent !== null) return T.percent(percent)
  if (amount !== null) return T.amount(amount)
  return T.none
}

function PricingFacts({ line }: { readonly line: LinePricing }) {
  const reference = line.priceReference
  return (
    <dl className={styles.facts} data-testid={`sales-line-${line.lineNo}-pricing`}>
      <div>
        <dt>{T.reference}</dt>
        <dd data-testid={`sales-line-${line.lineNo}-reference`}>
          {reference === null ? T.noReference
            : `${T.referenceText(reference.priceListName, reference.versionNo, reference.unitPrice, reference.currencyCode)}`
              + (reference.floorUnitPrice === null ? '' : ` (${T.floorText(reference.floorUnitPrice)})`)
              + ` (${T.tierText(reference.lowerQuantity, reference.upperQuantity)})`}
        </dd>
      </div>
      <div>
        <dt>{T.unitPrice}</dt>
        <dd data-testid={`sales-line-${line.lineNo}-unit-price`}>{line.unitPrice ?? T.none}</dd>
      </div>
      <div>
        <dt>{T.source}</dt>
        <dd data-testid={`sales-line-${line.lineNo}-source`}>
          {line.priceSource === 'Reference' ? T.sourceReference : line.priceSource === 'Manual' ? T.sourceManual : T.none}
        </dd>
      </div>
      <div>
        <dt>{T.discount}</dt>
        <dd data-testid={`sales-line-${line.lineNo}-discount-adjustment`}>{adjustment(line.discountPercent, line.discountAmount)}</dd>
      </div>
      <div>
        <dt>{T.surcharge}</dt>
        <dd data-testid={`sales-line-${line.lineNo}-surcharge`}>{adjustment(line.surchargePercent, line.surchargeAmount)}</dd>
      </div>
      <div>
        <dt>{T.net}</dt>
        <dd data-testid={`sales-line-${line.lineNo}-net`}>{line.netUnitPrice ?? T.none}</dd>
      </div>
    </dl>
  )
}
