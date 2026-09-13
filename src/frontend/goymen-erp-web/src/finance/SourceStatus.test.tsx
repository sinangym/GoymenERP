/**
 * DOM tests for the Finance source status view. Component evidence only — no API,
 * SQL or browser acceptance.
 */

import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { SourceSummary, RecordGateNotice } from './SourceStatus'
import {
  AMOUNT_UNREADABLE,
  AMOUNT_WITHHELD,
  RECORD_GATE_REASON,
  RECORD_GATE_TITLE,
  STAGE_NOTICE,
  UNALLOCATED_LABEL,
  UNALLOCATED_UNAVAILABLE,
} from './financeText'
import { RECORD_GATE_NOT_DECLARED, recordGateFrom } from './sourceView'

const VISIBLE = { kind: 'visible', amount: '1000', currencyLabel: 'CUR' } as const

function unallocatedCell(): Element | null {
  return screen.getByText(UNALLOCATED_LABEL).nextElementSibling
}

describe('SourceSummary', () => {
  it('a draft shows no unallocated amount and says it has no money effect', () => {
    const { container } = render(<SourceSummary view={{ stage: 'draft', amount: VISIBLE }} />)
    screen.getByText('Taslak')
    screen.getByText('1.000 CUR')
    expect(screen.queryByText(UNALLOCATED_LABEL)).toBeNull()
    expect(screen.getByRole('note').textContent).toBe(STAGE_NOTICE.draft)
    expect(container.textContent).not.toMatch(/tahsil edildi|bakiye:/i)
  })

  it('a recorded source shows the server unallocated text exactly, without padding', () => {
    render(
      <SourceSummary
        view={{
          stage: 'recorded',
          amount: VISIBLE,
          unallocated: { kind: 'server', amount: '1000', currencyLabel: 'CUR' },
        }}
      />,
    )
    expect(unallocatedCell()?.textContent).toBe('1.000 CUR')
  })

  it('an unavailable unallocated amount is never rendered as zero', () => {
    render(
      <SourceSummary
        view={{ stage: 'recorded', amount: VISIBLE, unallocated: { kind: 'unavailable' } }}
      />,
    )
    const cell = unallocatedCell()
    expect(cell?.textContent).toBe(UNALLOCATED_UNAVAILABLE)
    expect(cell?.textContent).not.toMatch(/[0-9]/)
    expect(cell?.getAttribute('data-amount-kind')).toBe('unavailable')
  })

  it('a withheld amount leaves no digits anywhere in the DOM', () => {
    const { container } = render(
      <SourceSummary
        view={{ stage: 'recorded', amount: { kind: 'withheld' }, unallocated: { kind: 'withheld' } }}
      />,
    )
    expect(screen.getAllByText(AMOUNT_WITHHELD)).toHaveLength(2)
    expect(container.textContent).not.toMatch(/[0-9]/)
    // No amount parked in an attribute either (class names are build hashes, skipped).
    for (const element of container.querySelectorAll('*')) {
      for (const attribute of element.attributes) {
        if (attribute.name !== 'class') expect(attribute.value).not.toMatch(/[0-9]/)
      }
    }
  })

  it('a reversed source keeps its original amount and says it is not a bank refund', () => {
    render(
      <SourceSummary
        view={{
          stage: 'reversed',
          amount: VISIBLE,
          unallocated: { kind: 'server', amount: '0', currencyLabel: 'CUR' },
        }}
      />,
    )
    screen.getByText('1.000 CUR')
    expect(screen.getByRole('note').textContent).toBe(STAGE_NOTICE.reversed)
  })

  it('a non-canonical amount that slipped past a decoder is not printed as money', () => {
    render(
      <SourceSummary
        view={{ stage: 'draft', amount: { kind: 'visible', amount: '1e3', currencyLabel: 'CUR' } }}
      />,
    )
    expect(screen.queryByText(/1e3/)).toBeNull()
    screen.getByText(AMOUNT_UNREADABLE)
  })
})

describe('RecordGateNotice', () => {
  it('is closed by default and says why, as a polite status', () => {
    render(<RecordGateNotice gate={RECORD_GATE_NOT_DECLARED} />)
    const status = screen.getByRole('status')
    expect(status.getAttribute('aria-live')).toBe('polite')
    expect(status.textContent).toContain(RECORD_GATE_TITLE)
    expect(status.textContent).toContain(RECORD_GATE_REASON['not-declared'])
  })

  it('lists every server reason', () => {
    render(
      <RecordGateNotice
        gate={recordGateFrom(false, ['precision-profile-missing', 'period-not-open'])}
      />,
    )
    expect(screen.getAllByRole('listitem')).toHaveLength(2)
  })

  it('an empty closed reason list is not silence', () => {
    expect(recordGateFrom(false, [])).toEqual(RECORD_GATE_NOT_DECLARED)
  })

  it('renders nothing only when the server opened the gate', () => {
    const { container } = render(<RecordGateNotice gate={recordGateFrom(true, [])} />)
    expect(container.innerHTML).toBe('')
  })
})
