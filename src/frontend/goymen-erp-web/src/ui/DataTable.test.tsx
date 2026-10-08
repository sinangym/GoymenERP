import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { DataTable, type DataTableColumn } from './DataTable'
import styles from './dataTable.module.css'

type Row = { readonly id: string; readonly code: string; readonly kg: string }

const ROWS: readonly Row[] = [
  { id: 'b', code: 'ETK-2', kg: '5' },
  { id: 'a', code: 'ETK-1', kg: '10' },
  { id: 'c', code: 'ETK-3', kg: '0,5' },
]

const COLUMNS: readonly DataTableColumn<Row>[] = [
  { key: 'code', header: 'Etiket', pin: 'start', cell: (row) => row.code },
  { key: 'kg', header: 'Kullanılabilir kg', align: 'end', pin: 'end', pinWidth: '8rem', cell: (row) => row.kg },
  { key: 'actions', header: 'İşlemler', pin: 'end', pinWidth: '6rem', cell: (row) => <button type="button">{row.code} işlemleri</button> },
]

const cells = (tr: HTMLElement) => within(tr).getAllByRole('cell')

describe('GOY-DS-001 DataTable', () => {
  it('is one named table with column headers and exactly one cell per column per row', () => {
    render(<DataTable label="Stok" columns={COLUMNS} rows={ROWS} rowKey={(row) => row.id} />)
    const table = screen.getByRole('table', { name: 'Stok' })
    const headers = within(table).getAllByRole('columnheader')
    expect(headers.map((header) => header.textContent)).toEqual(['Etiket', 'Kullanılabilir kg', 'İşlemler'])
    for (const header of headers) expect(header.getAttribute('scope')).toBe('col')
    const rows = within(table).getAllByRole('row')
    expect(rows).toHaveLength(1 + ROWS.length)
    for (const tr of rows.slice(1)) expect(cells(tr)).toHaveLength(COLUMNS.length)
    expect(screen.getAllByRole('table')).toHaveLength(1)
  })

  it('keeps the caller\'s rows in the given order and adds no value, total or row of its own', () => {
    render(<DataTable label="Stok" columns={COLUMNS} rows={ROWS} rowKey={(row) => row.id} />)
    const body = screen.getAllByRole('row').slice(1)
    expect(body.map((tr) => cells(tr)[0].textContent)).toEqual(['ETK-2', 'ETK-1', 'ETK-3'])
    expect(body.map((tr) => cells(tr)[1].textContent)).toEqual(['5', '10', '0,5'])
    expect(screen.getByRole('table').querySelector('tfoot')).toBeNull()
    expect(screen.queryByRole('checkbox')).toBeNull()
    // Headers are plain text: no sorting control.
    for (const header of screen.getAllByRole('columnheader')) {
      expect(header.getAttribute('aria-sort')).toBeNull()
      expect(within(header).queryByRole('button')).toBeNull()
    }
  })

  it('draws only the header when there are no rows', () => {
    render(<DataTable label="Stok" columns={COLUMNS} rows={[]} rowKey={(row) => row.id} />)
    expect(screen.getAllByRole('row')).toHaveLength(1)
    expect(screen.queryAllByRole('cell')).toHaveLength(0)
  })

  it('puts the caller\'s data attributes on each row', () => {
    render(<DataTable
      label="Stok" columns={COLUMNS} rows={ROWS} rowKey={(row) => row.id}
      rowAttributes={(row) => ({ 'data-label-id': row.id })}
    />)
    expect(screen.getAllByRole('row').slice(1).map((tr) => tr.getAttribute('data-label-id'))).toEqual(['b', 'a', 'c'])
  })

  it('pins the identity column at the start and stacks the pinned end columns from the right', () => {
    render(<DataTable label="Stok" columns={COLUMNS} rows={ROWS} rowKey={(row) => row.id} />)
    const [header] = screen.getAllByRole('row')
    const [code, kg, actions] = within(header).getAllByRole('columnheader')
    const [codeCell, kgCell, actionsCell] = cells(screen.getAllByRole('row')[1])
    for (const [head, cell] of [[code, codeCell], [kg, kgCell], [actions, actionsCell]]) {
      expect(cell.className).toBe(head.className)
      expect(cell.getAttribute('style')).toBe(head.getAttribute('style'))
    }
    expect(code.classList.contains(styles.pinStart)).toBe(true)
    expect(code.style.right).toBe('')
    // The first pinned end column sits left of the columns pinned after it and draws the edge.
    expect(kg.classList.contains(styles.pinEnd)).toBe(true)
    expect(kg.classList.contains(styles.pinEdge)).toBe(true)
    expect(kg.classList.contains(styles.end)).toBe(true)
    expect(kg.style.right).toBe('6rem')
    expect(kg.style.width).toBe('8rem')
    expect(actions.classList.contains(styles.pinEnd)).toBe(true)
    expect(actions.classList.contains(styles.pinEdge)).toBe(false)
    expect(actions.style.right).toBe('0px')
  })

  it('adds the widths of every later pinned end column to an earlier one\'s offset', () => {
    const three: readonly DataTableColumn<Row>[] = [
      { key: 'a', header: 'A', pin: 'end', pinWidth: '5rem', cell: (row) => row.code },
      { key: 'b', header: 'B', pin: 'end', pinWidth: '4rem', cell: (row) => row.kg },
      { key: 'c', header: 'C', pin: 'end', pinWidth: '3rem', cell: (row) => row.id },
    ]
    render(<DataTable label="Stok" columns={three} rows={ROWS} rowKey={(row) => row.id} />)
    const [a, b, c] = screen.getAllByRole('columnheader')
    // `calc(4rem + 3rem)`, as the browser's style object normalises it.
    expect(a.style.right).toBe('calc(7rem)')
    expect(b.style.right).toBe('3rem')
    expect(c.style.right).toBe('0px')
  })

  it('leaves an unpinned, start-aligned column without a layout class or style', () => {
    const plain: readonly DataTableColumn<Row>[] = [{ key: 'code', header: 'Etiket', cell: (row) => row.code }]
    render(<DataTable label="Stok" columns={plain} rows={ROWS} rowKey={(row) => row.id} />)
    const header = screen.getByRole('columnheader')
    expect(header.getAttribute('class')).toBeNull()
    expect(header.getAttribute('style')).toBeNull()
  })

  it('wraps the table in its scroll area and keeps the caller\'s class on it', () => {
    const { container } = render(<DataTable label="Stok" columns={COLUMNS} rows={ROWS} rowKey={(row) => row.id} className="page-area" />)
    const area = container.firstElementChild as HTMLElement
    expect(area.classList.contains(styles.scroll)).toBe(true)
    expect(area.classList.contains('page-area')).toBe(true)
    expect(area.firstElementChild?.tagName).toBe('TABLE')
  })
})
