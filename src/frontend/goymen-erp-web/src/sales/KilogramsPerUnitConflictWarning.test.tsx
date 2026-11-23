import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import { KilogramsPerUnitConflictWarning } from './KilogramsPerUnitConflictWarning'
import { KILOGRAMS_PER_UNIT_CONFLICT_TEXT as T } from './kilogramsPerUnitConflictText'

afterEach(() => cleanup())

describe('SAT-D477 kg/m table conflict warning', () => {
  it('shows the pinned entered-direction figure, the reversed figure, the difference and the table correction note', () => {
    render(<KilogramsPerUnitConflictWarning lineNo={2} conflict={{
      selection: 'weight-table:entered-orientation', selectedKilogramsPerUnit: '21', otherKilogramsPerUnit: '21.6',
      differenceKilogramsPerUnit: '0.6', selectedKgPerMetre: '3.5', otherKgPerMetre: '3.6',
    }} />)

    const note = screen.getByRole('note', { name: T.heading })
    expect(note.getAttribute('data-testid')).toBe('sales-line-2-kg-per-unit-conflict')
    expect(screen.getByText(T.explanation('21', '21.6', '0.6'))).toBeTruthy()
    expect(screen.getByText(T.kgPerMetre('3.5', '3.6'))).toBeTruthy()
    expect(screen.getByText(T.correction)).toBeTruthy()
  })
})
