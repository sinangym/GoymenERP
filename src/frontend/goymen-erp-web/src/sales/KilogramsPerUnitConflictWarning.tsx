import type { KilogramsPerUnitTableConflict } from './contract'
import { KILOGRAMS_PER_UNIT_CONFLICT_TEXT as T } from './kilogramsPerUnitConflictText'

/**
 * SAT-D474 / SAT-D477: the approver's warning on a piece line whose kg/unit came from the card's entered direction while the kg/m
 * table holds the reversed direction with another value. Figures are the server's text, never recomputed here.
 */
export function KilogramsPerUnitConflictWarning({ lineNo, conflict }: {
  readonly lineNo: number
  readonly conflict: KilogramsPerUnitTableConflict
}) {
  return (
    <div role="note" aria-label={T.heading} data-testid={`sales-line-${lineNo}-kg-per-unit-conflict`}>
      <strong>{T.heading}</strong>
      <p>{T.explanation(conflict.selectedKilogramsPerUnit, conflict.otherKilogramsPerUnit, conflict.differenceKilogramsPerUnit)}</p>
      <p>{T.kgPerMetre(conflict.selectedKgPerMetre, conflict.otherKgPerMetre)}</p>
      <p>{T.correction}</p>
    </div>
  )
}
