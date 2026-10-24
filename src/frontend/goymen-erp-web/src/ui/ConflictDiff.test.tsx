import { describe, expect, it } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import { ConflictDiff } from './ConflictDiff'

const HEADERS = { fieldHeader: 'Alan', mineHeader: 'Sizin girdiniz', currentHeader: 'Güncel kayıt' }

describe('ConflictDiff (EK-46 LF-12)', () => {
  it('shows each differing field with the typed and the current value in a named table', () => {
    render(
      <ConflictDiff
        label="Girdiğiniz değerler ile güncel kayıt"
        {...HEADERS}
        rows={[{ field: 'name', label: 'Ad', mine: 'Hol-0 Boya', current: 'Hol-0 Depo' }]}
      />,
    )
    const table = screen.getByRole('table', { name: 'Girdiğiniz değerler ile güncel kayıt' })
    expect(within(table).getAllByRole('columnheader').map((cell) => cell.textContent)).toEqual([
      'Alan',
      'Sizin girdiniz',
      'Güncel kayıt',
    ])
    const [, row] = within(table).getAllByRole('row')
    expect(within(row).getAllByRole('cell').map((cell) => cell.textContent)).toEqual(['Ad', 'Hol-0 Boya', 'Hol-0 Depo'])
    expect(row.getAttribute('data-conflict-field')).toBe('name')
  })

  it('draws nothing when no field differs', () => {
    const { container } = render(<ConflictDiff label="Fark" {...HEADERS} rows={[]} />)
    expect(container.innerHTML).toBe('')
  })
})
