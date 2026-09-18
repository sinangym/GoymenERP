/**
 * What leaving a company MEANS, in the shell's own words (Business #66 comment
 * 5653637829, AT-005/006/008): switching is leaving the screen. Unsaved edits on it are
 * discarded; a request already SENT to the server is not cancelled and not rolled back
 * by leaving, and its result is checked on return. The dialog cannot know whether a
 * command is in flight — the dirty scope is one flag — so its one sentence must be true
 * in both cases and must promise neither a cancellation nor a success.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import { SwitchConfirmDialog } from './SwitchConfirmDialog'

afterEach(() => cleanup())

function detailText(): string {
  return screen.getByRole('dialog').querySelector('#switch-confirm-detail')?.textContent ?? ''
}

describe('SwitchConfirmDialog — the text promises no cancellation and no success', () => {
  it('names the target, warns that UNSAVED edits are discarded, and says a SENT request is NOT cancelled', () => {
    render(<SwitchConfirmDialog targetCompanyLabel="Göymen Sac Profil" onStay={vi.fn()} onDiscard={vi.fn()} />)
    const detail = detailText()
    expect(detail).toContain('Göymen Sac Profil')
    expect(detail).toMatch(/kaydedilmemiş değişiklikler(i)? bırakılır/i)
    expect(detail).toMatch(/gönderilmiş .* iptal etmez/i)
    expect(detail).toMatch(/döndüğünüzde kontrol/i)
    // The old wording read as a cancellation of a pending command; none of these may return.
    expect(detail).not.toMatch(/bekleyen işlem/i)
    expect(detail).not.toMatch(/iptal edil|geri alın|başarı/i)
    expect(screen.getByRole('button', { name: 'Kal' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Değişiklikleri bırak' })).toBeTruthy()
  })

  it('says the same when the target company cannot be named', () => {
    render(<SwitchConfirmDialog targetCompanyLabel={null} onStay={vi.fn()} onDiscard={vi.fn()} />)
    const detail = detailText()
    expect(detail).toMatch(/^Şirket değiştirirseniz/)
    expect(detail).toMatch(/kaydedilmemiş değişiklikler(i)? bırakılır/i)
    expect(detail).toMatch(/gönderilmiş .* iptal etmez/i)
    expect(detail).not.toMatch(/bekleyen işlem/i)
  })

  it('keeps the title about unsaved changes and the dialog described by that text', () => {
    render(<SwitchConfirmDialog targetCompanyLabel="Göymen Sac Profil" onStay={vi.fn()} onDiscard={vi.fn()} />)
    const dialog = screen.getByRole('dialog')
    expect(dialog.getAttribute('aria-labelledby')).toBe('switch-confirm-title')
    expect(dialog.getAttribute('aria-describedby')).toBe('switch-confirm-detail')
    expect(screen.getByRole('heading', { name: 'Kaydedilmemiş değişiklikler var' })).toBeTruthy()
  })
})
