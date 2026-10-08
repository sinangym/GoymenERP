/**
 * GOY-DS-001 DataTable — the semantic table, its header and its scroll area, nothing else.
 *
 * One real `<table>` with `scope="col"` headers and exactly one cell per column per row, so a row reads the same
 * to a screen reader, a test and a browser spec. The caller owns every fact: the rows it was given (in the order
 * it was given), each cell's content, which columns exist, the row keys and the row attributes. This component does
 * not sort, filter, select, page, total, virtualise or remember anything, and it never computes a value.
 *
 * Layout only: the header stays on top while the area scrolls, and a column marked `pin` stays at its edge while
 * the table scrolls sideways (the identity column at the start; the main quantity and the actions at the end).
 * Pinned end columns are stacked from the right in the order they are listed.
 */

import type { CSSProperties, ReactNode } from 'react'
import styles from './dataTable.module.css'

export type DataTableColumn<Row> = {
  /** Stable React key of the column. */
  readonly key: string
  readonly header: ReactNode
  readonly cell: (row: Row) => ReactNode
  /** `end` right-aligns the header and the cells in tabular figures (numbers). */
  readonly align?: 'start' | 'end'
  /** Keeps the column at that edge while the table scrolls sideways. */
  readonly pin?: 'start' | 'end'
  /** The width a pinned end column reserves, so the next pinned column can sit at its left. */
  readonly pinWidth?: string
  /** Extra class for this column's header and cells. */
  readonly className?: string
}

export type DataTableRowAttributes = { readonly [name: `data-${string}`]: string }

export function DataTable<Row>({
  label,
  columns,
  rows,
  rowKey,
  rowAttributes,
  className,
}: {
  /** The table's accessible name. */
  readonly label: string
  readonly columns: readonly DataTableColumn<Row>[]
  readonly rows: readonly Row[]
  readonly rowKey: (row: Row) => string
  readonly rowAttributes?: (row: Row) => DataTableRowAttributes
  readonly className?: string
}) {
  const placed = placeColumns(columns)
  return <div className={[styles.scroll, className].filter(Boolean).join(' ')}>
    <table className={styles.table} aria-label={label}>
      <thead><tr>
        {placed.map(({ column, cellClass, style }) =>
          <th key={column.key} scope="col" className={cellClass} style={style}>{column.header}</th>)}
      </tr></thead>
      <tbody>
        {rows.map((row) => <tr key={rowKey(row)} {...rowAttributes?.(row)}>
          {placed.map(({ column, cellClass, style }) =>
            <td key={column.key} className={cellClass} style={style}>{column.cell(row)}</td>)}
        </tr>)}
      </tbody>
    </table>
  </div>
}

type PlacedColumn<Row> = {
  readonly column: DataTableColumn<Row>
  readonly cellClass: string | undefined
  readonly style: CSSProperties | undefined
}

/** Each column's classes and, for a pinned end column after the first, its offset from the right edge. */
function placeColumns<Row>(columns: readonly DataTableColumn<Row>[]): readonly PlacedColumn<Row>[] {
  const endPinned = columns.filter((column) => column.pin === 'end')
  return columns.map((column) => {
    const classes = [
      column.align === 'end' ? styles.end : null,
      column.pin === 'start' ? styles.pinStart : null,
      column.pin === 'end' ? styles.pinEnd : null,
      column.pin === 'end' && endPinned.indexOf(column) === 0 ? styles.pinEdge : null,
      column.className ?? null,
    ].filter(Boolean)
    let style: CSSProperties | undefined
    if (column.pin === 'end') {
      // Pinned end columns listed after this one sit to its right; their reserved widths are its offset.
      const after = endPinned.slice(endPinned.indexOf(column) + 1).map((next) => next.pinWidth ?? '0px')
      const right = after.length === 0 ? '0px' : after.length === 1 ? after[0] : `calc(${after.join(' + ')})`
      style = {
        right,
        ...(column.pinWidth ? { minWidth: column.pinWidth, width: column.pinWidth } : {}),
      }
    }
    return { column, cellClass: classes.length > 0 ? classes.join(' ') : undefined, style }
  })
}
