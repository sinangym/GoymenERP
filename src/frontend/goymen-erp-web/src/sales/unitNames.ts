import type { DraftDetail } from './contract'

/**
 * TEK-11: the unit codes the order itself carries — each line's order unit and the summary's product snapshots. A unit
 * is named only when the order already names it; otherwise the screen shows its stored id, never a guessed code.
 */
export function orderUnitCodes(detail: DraftDetail): ReadonlyMap<string, string> {
  const codes = new Map<string, string>()
  for (const snapshot of detail.draft.productSnapshots ?? []) codes.set(snapshot.orderUnitId, snapshot.orderUnitCode)
  for (const line of detail.lines) if (line.orderUnitCode !== undefined) codes.set(line.orderUnitId, line.orderUnitCode)
  return codes
}
