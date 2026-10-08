import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { Field } from './Field'

describe('Field (GOY-DS-001)', () => {
  it('labels the control the caller renders', () => {
    render(<Field label="Sipariş tarihi">{(c) => <input type="date" {...c} />}</Field>)
    const input = screen.getByLabelText('Sipariş tarihi')
    expect(input.getAttribute('type')).toBe('date')
    expect(input.hasAttribute('aria-describedby')).toBe(false)
    expect(input.hasAttribute('aria-invalid')).toBe(false)
  })

  it('generates a distinct id per field', () => {
    render(
      <>
        <Field label="A">{(c) => <input {...c} />}</Field>
        <Field label="B">{(c) => <input {...c} />}</Field>
      </>,
    )
    const a = screen.getByLabelText('A'); const b = screen.getByLabelText('B')
    expect(a.id).toBeTruthy()
    expect(a.id).not.toBe(b.id)
  })

  it('uses the caller id when given', () => {
    render(<Field id="f-cur" label="Para birimi">{(c) => <select {...c}><option>TRY</option></select>}</Field>)
    expect(screen.getByLabelText('Para birimi').id).toBe('f-cur')
  })

  it('describes the control by help, then error, and keeps the caller error text verbatim', () => {
    const message = 'Nokta binlik ayırıcı mı ondalık mı anlaşılmıyor. 1000 ya da 1,5 biçiminde yazın.'
    render(
      <Field id="qty" label="Miktar" help="kg" error={message} describedBy="ext-hint">
        {(c) => <input {...c} />}
      </Field>,
    )
    const input = screen.getByLabelText('Miktar')
    expect(input.getAttribute('aria-describedby')).toBe('ext-hint qty-help qty-error')
    expect(input.getAttribute('aria-invalid')).toBe('true')
    expect(document.getElementById('qty-help')?.textContent).toBe('kg')
    expect(document.getElementById('qty-error')?.textContent).toBe(message)
  })

  it('marks invalid only when the caller passes an error; it never validates the value', () => {
    render(
      <>
        <Field label="Boş">{(c) => <input {...c} defaultValue="" />}</Field>
        <Field label="Sıfır">{(c) => <input {...c} defaultValue="0" />}</Field>
      </>,
    )
    for (const name of ['Boş', 'Sıfır']) {
      const input = screen.getByLabelText(name)
      expect(input.hasAttribute('aria-invalid')).toBe(false)
      expect(input.hasAttribute('required')).toBe(false)
    }
    expect((screen.getByLabelText('Boş') as HTMLInputElement).value).toBe('')
    expect((screen.getByLabelText('Sıfır') as HTMLInputElement).value).toBe('0')
  })

  it('leaves required, min, max and type to the caller', () => {
    render(<Field label="Adet">{(c) => <input {...c} type="number" required min={1} max={500} />}</Field>)
    const input = screen.getByLabelText('Adet')
    expect(input.getAttribute('type')).toBe('number')
    expect(input.hasAttribute('required')).toBe(true)
    expect(input.getAttribute('min')).toBe('1')
    expect(input.getAttribute('max')).toBe('500')
  })

  it('renders no live region and no alert role for the error', () => {
    render(<Field label="Cari" error="Cari ve bu şirketteki hesabını seçin.">{(c) => <input {...c} />}</Field>)
    expect(screen.queryByRole('alert')).toBeNull()
    expect(document.querySelector('[aria-live]')).toBeNull()
  })
})
