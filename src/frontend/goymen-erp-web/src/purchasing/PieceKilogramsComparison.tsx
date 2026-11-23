import type { PieceKilograms } from './pieceKilogramsContract'
import { GOODS_RECEIPT_TEXT as T } from './goodsReceiptText'
import styles from './purchasing.module.css'

interface Props { readonly comparison: PieceKilograms; readonly testId: string }

/**
 * AK-3b (BS-16/BS-17, golden TA4): the theoretical vs weighed kilograms of a piece receipt (single or bulk), drawn from the
 * server's recorded comparison. A difference outside the tolerance is a warning and a missing tolerance a note; neither blocks —
 * the receipt already stands. Nothing is computed here: the sign shown is only the server's own text made explicit.
 */
export function PieceKilogramsComparison({ comparison, testId }: Props) {
  const difference = comparison.differenceKg
  const signed = difference === '0' || difference.startsWith('-') ? difference : `+${difference}`
  return <section aria-label={T.pieceHeading} data-testid={testId} data-outcome={comparison.varianceOutcome}>
    <h4>{T.pieceHeading}</h4>
    <dl>
      <dt>{T.pieceCount}</dt><dd>{comparison.pieceCount}</dd>
      <dt>{T.pieceKilogramsPerUnit}</dt><dd>{T.pieceKg(comparison.kilogramsPerUnit)}</dd>
      <dt>{T.pieceTheoreticalKg}</dt><dd>{T.pieceKg(comparison.theoreticalQuantityKg)}</dd>
      <dt>{T.pieceMeasuredKg}</dt><dd>{T.pieceKg(comparison.measuredQuantityKg)}</dd>
      <dt>{T.pieceDifferenceKg}</dt><dd>{T.pieceKg(signed)}</dd>
      <dt>{T.pieceTolerance}</dt><dd>{comparison.tolerancePercent ?? T.pieceToleranceNotSet}</dd>
      <dt>{T.pieceOutcome}</dt><dd>{T.pieceOutcomeText[comparison.varianceOutcome]}</dd>
    </dl>
    {comparison.varianceOutcome === 'OutsideTolerance'
      ? <p role="alert" className={styles.warning}>{T.pieceOutsideTolerance}</p> : null}
    {comparison.varianceOutcome === 'ToleranceNotSet'
      ? <p role="note" className={styles.note}>{T.pieceToleranceNotSetNote}</p> : null}
  </section>
}
