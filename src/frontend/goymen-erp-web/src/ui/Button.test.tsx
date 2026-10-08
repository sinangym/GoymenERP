import { describe, expect, it, vi } from 'vitest'
import { createRef } from 'react'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Button } from './Button'

describe('Button (GOY-DS-001)', () => {
  it('renders one native button with the caller text as its accessible name', () => {
    render(<Button type="button">Tekrar dene</Button>)
    const b = screen.getByRole('button', { name: 'Tekrar dene' })
    expect(b.tagName).toBe('BUTTON')
    expect(b).toHaveProperty('type', 'button')
  })

  it('keeps an explicit aria-label as the accessible name', () => {
    render(<Button type="button" aria-label="Diğer işlemler">⋯</Button>)
    expect(screen.getByRole('button', { name: 'Diğer işlemler' })).toBeTruthy()
  })

  it('passes the type through unchanged: submit submits its form, button does not', async () => {
    const onSubmit = vi.fn((e: { preventDefault(): void }) => e.preventDefault())
    render(
      <form onSubmit={onSubmit}>
        <Button type="button">Yerel</Button>
        <Button type="submit">Gönder</Button>
      </form>,
    )
    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: 'Yerel' }))
    expect(onSubmit).not.toHaveBeenCalled()
    await user.click(screen.getByRole('button', { name: 'Gönder' }))
    expect(onSubmit).toHaveBeenCalledTimes(1)
  })

  it('keeps form association through the native form attribute', async () => {
    const onSubmit = vi.fn((e: { preventDefault(): void }) => e.preventDefault())
    render(
      <>
        <form id="dis-form" onSubmit={onSubmit} />
        <Button type="submit" form="dis-form" name="islem" value="kaydet">Dışarıdan gönder</Button>
      </>,
    )
    const b = screen.getByRole('button', { name: 'Dışarıdan gönder' }) as HTMLButtonElement
    expect(b.form?.id).toBe('dis-form')
    expect(b.name).toBe('islem')
    expect(b.value).toBe('kaydet')
    await userEvent.setup().click(b)
    expect(onSubmit).toHaveBeenCalledTimes(1)
  })

  it('a disabled button does not call the caller handler', async () => {
    const onClick = vi.fn()
    render(<Button type="button" disabled onClick={onClick}>Kapalı</Button>)
    const b = screen.getByRole('button', { name: 'Kapalı' })
    expect(b).toHaveProperty('disabled', true)
    await userEvent.setup().click(b)
    expect(onClick).not.toHaveBeenCalled()
  })

  it('Enter and Space activate it exactly as a native button does', async () => {
    const onClick = vi.fn()
    render(<Button type="button" onClick={onClick}>Klavye</Button>)
    const user = userEvent.setup()
    screen.getByRole('button', { name: 'Klavye' }).focus()
    await user.keyboard('{Enter}')
    await user.keyboard(' ')
    expect(onClick).toHaveBeenCalledTimes(2)
  })

  it('forwards the ref and caller attributes, and appends the caller class', () => {
    const ref = createRef<HTMLButtonElement>()
    render(<Button type="reset" ref={ref} className="yerel" data-testid="b" aria-describedby="x">Sıfırla</Button>)
    const b = screen.getByTestId('b')
    expect(ref.current).toBe(b)
    expect(b).toHaveProperty('type', 'reset')
    expect(b.getAttribute('aria-describedby')).toBe('x')
    expect(b.className.split(' ')).toContain('yerel')
  })

  it('adds no busy, live-region or disabled state of its own', () => {
    render(<Button type="button" variant="primary">Kaydet</Button>)
    const b = screen.getByRole('button', { name: 'Kaydet' })
    expect(b.hasAttribute('aria-busy')).toBe(false)
    expect(b.hasAttribute('aria-live')).toBe(false)
    expect(b).toHaveProperty('disabled', false)
  })
})
