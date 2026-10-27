/**
 * GOY-DS-001 ConflictDiff — EK-46 LF-12 / AT-005. After a version conflict and a fresh read, shows what the user typed
 * next to what the current record holds, one row per field that differs.
 *
 * Presentational only, on top of DataTable: the caller decides which fields differ, passes both values as text and
 * owns every label (from its own text module; this primitive carries no text). It never picks a winner, never copies
 * a value into the form and never hides the user's input. With no differing field it draws nothing.
 */

import type { ReactNode } from 'react'
import { DataTable, type DataTableColumn } from './DataTable'

export interface ConflictDiffRow {
  /** Stable field key; also the row's `data-conflict-field`. */
  readonly field: string
  readonly label: ReactNode
  readonly mine: ReactNode
  readonly current: ReactNode
}

const COLUMNS = (
  fieldHeader: ReactNode,
  mineHeader: ReactNode,
  currentHeader: ReactNode,
): readonly DataTableColumn<ConflictDiffRow>[] => [
  { key: 'field', header: fieldHeader, cell: (row) => row.label, pin: 'start' },
  { key: 'mine', header: mineHeader, cell: (row) => row.mine },
  { key: 'current', header: currentHeader, cell: (row) => row.current },
]

export function ConflictDiff({
  label,
  fieldHeader,
  mineHeader,
  currentHeader,
  rows,
}: {
  /** The table's accessible name. */
  readonly label: string
  readonly fieldHeader: ReactNode
  readonly mineHeader: ReactNode
  readonly currentHeader: ReactNode
  readonly rows: readonly ConflictDiffRow[]
}) {
  if (rows.length === 0) return null
  return (
    <DataTable
      label={label}
      columns={COLUMNS(fieldHeader, mineHeader, currentHeader)}
      rows={rows}
      rowKey={(row) => row.field}
      rowAttributes={(row) => ({ 'data-conflict-field': row.field })}
    />
  )
}
