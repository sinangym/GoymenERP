import type { GoodsReceiptQuantityComparison } from './goodsReceiptComparisonContract'
import { GOODS_RECEIPT_TEXT as T } from './goodsReceiptText'

interface Props { readonly comparison: GoodsReceiptQuantityComparison }

export function GoodsReceiptQuantityComparison({ comparison }: Props) {
  const difference = comparison.measuredMinusAcceptedKg
  const signedDifference = difference === null || difference === '0' || difference.startsWith('-') ? difference : `+${difference}`

  return <section aria-label={T.comparisonHeading} data-testid={`goods-receipt-comparison-${comparison.acceptanceId}`}>
    <h4>{T.comparisonHeading}</h4>
    <dl>
      <dt>{T.comparisonAccepted}</dt><dd>{comparison.acceptedQuantity}</dd>
      <dt>{T.comparisonMeasured}</dt><dd>{comparison.measuredQuantityKg} kg</dd>
      {comparison.comparisonStatus === 'SameKilogramUnit' && signedDifference !== null
        ? <><dt>{T.comparisonDifference}</dt><dd>{signedDifference} kg</dd></>
        : null}
    </dl>
    {comparison.comparisonStatus === 'Unavailable' ? <p>{T.comparisonUnavailable}</p> : null}
  </section>
}