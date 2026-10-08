// StateBlock behaviour contract, pinned before its retry button moved to the GOY-DS-001 Button:
// every state keeps its text, data-state, role and aria-live, and only the four current
// retryable states offer "Tekrar dene" — one click, one callback, no automatic retry.
import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { StateBlock } from './StateBlock'
import type { RemoteViewState } from './uiState'

const EXPECTED: ReadonlyArray<readonly [RemoteViewState<unknown>['kind'], string, string]> = [
  ['initial-loading', 'Yükleniyor', 'Bilgi getiriliyor.'],
  ['no-authorized-company', 'Yetkili şirket yok', 'Hesabınız için yetkilendirilmiş bir şirket bulunamadı.'],
  ['empty-no-data', 'Kayıt yok', 'Bu görünümde henüz kayıt yok.'],
  ['empty-filtered', 'Sonuç yok', 'Filtreye uyan kayıt bulunamadı.'],
  ['unauthenticated', 'Oturum sonlandı', 'Devam etmek için yeniden giriş yapın.'],
  ['forbidden', 'Yetkiniz yok', 'Bu içeriğe erişim yetkiniz bulunmuyor.'],
  ['not-found', 'Bulunamadı', 'İstenen kayıt bulunamadı.'],
  ['conflict', 'Çakışma', 'Kayıt başka bir işlemle değişti; farkı gözden geçirin.'],
  ['offline', 'Bağlantı yok', 'İnternet bağlantısı geldiğinde tekrar deneyin.'],
  ['network-error', 'Sunucuya ulaşılamadı', 'İstek tamamlanamadı ve yanıt alınamadı. Tekrar deneyin.'],
  ['service-unavailable', 'Servis geçici olarak kullanılamıyor', 'Kısa süre sonra tekrar deneyin.'],
  ['malformed-response', 'Beklenmeyen yanıt', 'Sunucu yanıtı beklenen biçimde değil. İşlem güvenli biçimde durduruldu.'],
  ['stale-rejected', 'Eski bağlam yanıtı yok sayıldı', 'Şirket bağlamı değişti; eski yanıt uygulanmadı.'],
]
const RETRYABLE = ['offline', 'network-error', 'service-unavailable', 'malformed-response']
const stateOf = (kind: string) => ({ kind }) as RemoteViewState<unknown>

describe('StateBlock', () => {
  it.each(EXPECTED)('%s keeps its title, detail, data-state, role and aria-live', (kind, title, detail) => {
    const { container } = render(<StateBlock state={stateOf(kind)} subject="Stok" />)
    const block = container.firstElementChild as HTMLElement
    expect(block.getAttribute('data-state')).toBe(kind)
    expect(block.getAttribute('role')).toBe(kind === 'initial-loading' ? 'status' : 'alert')
    expect(block.getAttribute('aria-live')).toBe(kind === 'initial-loading' ? 'polite' : 'assertive')
    const paragraphs = [...block.querySelectorAll('p')].map((p) => p.textContent)
    expect(paragraphs).toEqual([`${title} — Stok`, detail])
  })

  it('omits the subject suffix when the subject is empty', () => {
    render(<StateBlock state={stateOf('empty-no-data')} subject="" />)
    expect(screen.getByText('Kayıt yok')).toBeTruthy()
  })

  it('falls back to the malformed-response text for an unknown state', () => {
    const { container } = render(<StateBlock state={stateOf('something-new')} subject="" />)
    expect(container.textContent).toContain('Beklenmeyen yanıt')
    expect(container.firstElementChild?.getAttribute('data-state')).toBe('something-new')
  })

  it('ready shows its title and no detail paragraph', () => {
    const { container } = render(<StateBlock state={stateOf('ready')} subject="" />)
    expect([...container.querySelectorAll('p')].map((p) => p.textContent)).toEqual(['Hazır'])
  })

  it.each(RETRYABLE)('%s offers one retry button that calls the callback once per click', async (kind) => {
    const onRetry = vi.fn()
    render(<StateBlock state={stateOf(kind)} subject="Stok" onRetry={onRetry} />)
    const buttons = screen.getAllByRole('button')
    expect(buttons).toHaveLength(1)
    const retry = screen.getByRole('button', { name: 'Tekrar dene' })
    expect(retry).toHaveProperty('type', 'button')
    expect(onRetry).not.toHaveBeenCalled()
    await userEvent.setup().click(retry)
    expect(onRetry).toHaveBeenCalledTimes(1)
  })

  it.each(EXPECTED.map(([k]) => k).filter((k) => !RETRYABLE.includes(k)))('%s never offers retry, even with a callback', (kind) => {
    const onRetry = vi.fn()
    render(<StateBlock state={stateOf(kind)} subject="Stok" onRetry={onRetry} />)
    expect(screen.queryByRole('button')).toBeNull()
    expect(onRetry).not.toHaveBeenCalled()
  })

  it('offers no retry for a retryable state when no callback is given', () => {
    render(<StateBlock state={stateOf('network-error')} subject="Stok" />)
    expect(screen.queryByRole('button')).toBeNull()
  })

  it('the retry button submits no surrounding form', async () => {
    const onSubmit = vi.fn((e: { preventDefault(): void }) => e.preventDefault())
    render(
      <form onSubmit={onSubmit}>
        <StateBlock state={stateOf('offline')} subject="" onRetry={() => {}} />
      </form>,
    )
    await userEvent.setup().click(screen.getByRole('button', { name: 'Tekrar dene' }))
    expect(onSubmit).not.toHaveBeenCalled()
  })
})
