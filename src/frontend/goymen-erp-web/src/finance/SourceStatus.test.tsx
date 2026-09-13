/**
 * DOM tests for the Finance source status view. Component evidence only — no API,
 * SQL or browser acceptance.
 */

import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { SourceStatus } from './SourceStatus'
import { AMOUNT_REDACTED, AMOUNT_UNAVAILABLE, AMOUNT_UNREADABLE, STAGE_NOTICE, UNALLOCATED_LABEL } from './financeText'
import { decodeSourceDetail } from './contract'
import { EXAMPLE_COMPANY, EXAMPLE_CURRENCY, NEGATIVE_EXAMPLE } from './contractExamples'
import { statusViewOf } from './sourceView'
import { sourceDetail } from './testHarness'

function viewOf(overrides: Parameters<typeof sourceDetail>[1]) {
  const decoded = decodeSourceDetail(sourceDetail(EXAMPLE_COMPANY, overrides), EXAMPLE_COMPANY)
  if (!decoded.ok) throw new Error('fixture must decode')
  return statusViewOf(decoded.value)
}

function unallocatedCell(): Element | null {
  return screen.getByText(UNALLOCATED_LABEL).nextElementSibling
}

function assertNoDigits(container: HTMLElement) {
  expect(container.textContent).not.toMatch(/[0-9]/)
  for (const element of container.querySelectorAll('*')) {
    for (const attribute of element.attributes) {
      // class names are CSS-module build hashes
      if (attribute.name !== 'class') expect(attribute.value).not.toMatch(/[0-9]/)
    }
  }
}

describe('SourceStatus', () => {
  it('a draft shows no unallocated amount even though the body carried the member', () => {
    render(<SourceStatus view={viewOf({})} />)
    screen.getByText('Taslak')
    screen.getByText('1.000')
    expect(screen.queryByText(UNALLOCATED_LABEL)).toBeNull()
    expect(screen.getByRole('note').textContent).toBe(STAGE_NOTICE.draft)
  })

  it('a recorded source with unavailable allocation says so and never renders 0', () => {
    render(<SourceStatus view={viewOf({ source: { stage: 'Recorded' }, unallocated: NEGATIVE_EXAMPLE.unavailableAllocation })} />)
    const cell = unallocatedCell()
    expect(cell?.textContent).toBe(AMOUNT_UNAVAILABLE)
    expect(cell?.getAttribute('data-amount-kind')).toBe('unavailable')
  })

  it('a known unallocated amount is shown exactly, without padding', () => {
    render(
      <SourceStatus
        view={viewOf({
          source: { stage: 'Recorded' },
          unallocated: { knowledge: 'Known', amount: { disclosure: 'Visible', money: { amount: '1000', currencyId: EXAMPLE_CURRENCY } } },
        })}
      />,
    )
    expect(unallocatedCell()?.textContent).toBe('1.000')
  })

  it('redacted amounts leave no digits anywhere in the DOM', () => {
    const { container } = render(
      <SourceStatus
        view={viewOf({
          source: { stage: 'Recorded', amount: NEGATIVE_EXAMPLE.redactedAmount },
          unallocated: { knowledge: 'Known', amount: NEGATIVE_EXAMPLE.redactedAmount },
        })}
      />,
    )
    expect(screen.getAllByText(AMOUNT_REDACTED)).toHaveLength(2)
    assertNoDigits(container)
  })

  it('a reversed source keeps its original amount and says it is not a bank refund', () => {
    render(<SourceStatus view={viewOf({ source: { stage: 'Reversed' }, unallocated: NEGATIVE_EXAMPLE.unavailableAllocation })} />)
    screen.getByText('1.000')
    expect(screen.getByRole('note').textContent).toBe(STAGE_NOTICE.reversed)
  })

  it('a non-grammar amount is not printed as money', () => {
    render(<SourceStatus view={{ stage: 'draft', amount: { kind: 'visible', amount: '1e3' } }} />)
    expect(screen.queryByText(/1e3/)).toBeNull()
    screen.getByText(AMOUNT_UNREADABLE)
  })
})
