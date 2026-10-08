import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { StatusIndicator } from './StatusIndicator'

describe('StatusIndicator (GOY-DS-001)', () => {
  it('shows exactly the caller text', () => {
    render(<StatusIndicator tone="warning">Onay bekliyor</StatusIndicator>)
    const el = screen.getByText('Onay bekliyor')
    expect(el.textContent).toBe('Onay bekliyor')
    expect(el.getAttribute('data-tone')).toBe('warning')
  })

  it('does not translate or infer anything from the text it is given', () => {
    render(<StatusIndicator>Approved</StatusIndicator>)
    expect(screen.getByText('Approved').getAttribute('data-tone')).toBe('neutral')
    expect(screen.queryByText('Onaylı')).toBeNull()
  })

  it('keeps the dot decorative so the text alone is announced', () => {
    render(<StatusIndicator tone="success">Onaylı</StatusIndicator>)
    const el = screen.getByText('Onaylı')
    const dot = el.querySelector('[aria-hidden="true"]')
    expect(dot).not.toBeNull()
    expect(dot?.textContent).toBe('')
  })

  it('is not a live region and has no role', () => {
    render(
      <>
        <StatusIndicator tone="info">Sipariş</StatusIndicator>
        <StatusIndicator tone="danger">Reddedildi</StatusIndicator>
      </>,
    )
    expect(document.querySelector('[aria-live]')).toBeNull()
    expect(document.querySelector('[role]')).toBeNull()
  })
})
