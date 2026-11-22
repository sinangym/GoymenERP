/**
 * AK-7 (URT-D112): the counted outputs of the order — declared and net pieces, net kg, the theoretical kg per piece
 * captured at declaration and the real kg per piece with its deviation, exactly as the server computed them. No
 * threshold is applied or shown: the deviation threshold is open (URT-D51 (5)). Nothing renders for an order without a
 * counted output.
 */

import type { OutputPieceView } from './contract'
import { OUTPUT_CLASS_LABEL } from './manufacturingText'
import { OUTPUT_PIECE_TEXT, signedText } from './outputPieceText'
import styles from './OrderDetailPage.module.css'

export function OutputPieceTable({ outputs }: { readonly outputs: readonly OutputPieceView[] }) {
  if (outputs.length === 0) return null
  return (
    <section aria-labelledby="mfg-output-pieces-heading" className={styles.section} data-testid="output-pieces">
      <h2 id="mfg-output-pieces-heading">{OUTPUT_PIECE_TEXT.heading}</h2>
      <p className={styles.help}>{OUTPUT_PIECE_TEXT.thresholdNote}</p>
      <div className={styles.tableWrap}>
        <table className={styles.table}>
          <caption className={styles.help}>{OUTPUT_PIECE_TEXT.caption}</caption>
          <thead>
            <tr>
              <th scope="col">{OUTPUT_PIECE_TEXT.output}</th>
              <th scope="col">{OUTPUT_PIECE_TEXT.outputClass}</th>
              <th scope="col">{OUTPUT_PIECE_TEXT.declaredPieces}</th>
              <th scope="col">{OUTPUT_PIECE_TEXT.netPieces}</th>
              <th scope="col">{OUTPUT_PIECE_TEXT.netKg}</th>
              <th scope="col">{OUTPUT_PIECE_TEXT.theoreticalKgPerPiece}</th>
              <th scope="col">{OUTPUT_PIECE_TEXT.realKgPerPiece}</th>
              <th scope="col">{OUTPUT_PIECE_TEXT.deviationKgPerPiece}</th>
              <th scope="col">{OUTPUT_PIECE_TEXT.deviationPercent}</th>
            </tr>
          </thead>
          <tbody>
            {outputs.map((output) => (
              <tr key={output.outputId} data-output={output.outputId}>
                <th scope="row" className={styles.mono}>{output.outputId}</th>
                <td>{OUTPUT_CLASS_LABEL[output.outputClass]}</td>
                <td>{output.declaredPieceCount}</td>
                <td>{output.netPieceCount}</td>
                <td>{output.netKg}</td>
                <td>{output.theoreticalKgPerPiece}</td>
                {output.realKgPerPiece === null ? (
                  <td colSpan={3} data-testid="no-net-pieces">{OUTPUT_PIECE_TEXT.noNetPieces}</td>
                ) : (
                  <>
                    <td>{output.realKgPerPiece}</td>
                    <td data-testid="deviation-kg">{signedText(output.deviationKgPerPiece!)}</td>
                    <td data-testid="deviation-percent">{signedText(output.deviationPercent!)}</td>
                  </>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  )
}
