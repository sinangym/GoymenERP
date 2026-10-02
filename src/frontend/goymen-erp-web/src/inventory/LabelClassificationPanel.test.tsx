import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { LabelClassificationPanel } from './LabelClassificationPanel'

const COMPANY = '11111111-1111-1111-1111-111111111111'
const LABEL = '22222222-2222-2222-2222-222222222222'
const api = vi.hoisted(() => ({ classify: vi.fn() }))
const auth = vi.hoisted(() => ({ report: vi.fn(), generation: 1 }))

vi.mock('./labelClassificationApi', () => ({ classifyInventoryLabel: api.classify }))
vi.mock('../shell/useAuthBoundary', () => ({
  useAuthBoundary: () => ({ capture: () => ({ generation: auth.generation }), report: auth.report }),
}))

function applied() {
  return {
    type: 'applied', replayed: true,
    response: {
      labelId: LABEL, labelVersion: '4', classification: 'SecondQuality',
      classifiedAtUtc: '2026-10-02T10:00:00Z',
    },
  }
}

function mount(namespace = 'SENTETIK-a', labelVersion = '3') {
  return render(<LabelClassificationPanel
    labelId={LABEL}
    labelVersion={labelVersion}
    current={null}
    namespace={namespace}
    actorId="actor-a"
    companyId={COMPANY}
    onChanged={vi.fn()}
  />)
}

beforeEach(() => {
  api.classify.mockReset()
  auth.report.mockReset()
  auth.generation = 1
})

afterEach(() => {
  cleanup()
  try { sessionStorage.clear() } catch { /* no storage */ }
})

describe('LabelClassificationPanel frozen attempt', () => {
  it('restores an unknown attempt after a versioned remount and reconciles the same key and body', async () => {
    api.classify.mockResolvedValueOnce({ type: 'unknown', reason: 'network-error' })
      .mockResolvedValueOnce(applied())
    const user = userEvent.setup()
    const first = mount()
    await user.selectOptions(screen.getByLabelText('Yeni sınıf'), 'SecondQuality')
    await user.click(screen.getByRole('button', { name: 'Sınıfı kaydet' }))
    await screen.findByText(/Sonuç bilinmiyor/)
    const original = api.classify.mock.calls[0]
    first.unmount()

    mount('SENTETIK-a', '9')
    await user.click(screen.getByRole('button', { name: 'Aynı anahtar ve içerikle yeniden gönder' }))
    await screen.findByText(/Önceki sınıflandırma sonucu doğrulandı/)
    expect(api.classify.mock.calls[1]).toEqual(original)
    expect(original[1]).toEqual({ expectedLabelVersion: '3', classification: 'SecondQuality' })
  })

  it('keeps the frozen attempt through 403 and uses only that attempt after authority is restored', async () => {
    api.classify.mockResolvedValueOnce({ type: 'unknown', reason: 'service-unavailable' })
      .mockResolvedValueOnce({ type: 'failed', status: 403, code: 'inventory_label_classification_forbidden' })
      .mockResolvedValueOnce(applied())
    const user = userEvent.setup()
    const first = mount()
    await user.selectOptions(screen.getByLabelText('Yeni sınıf'), 'SecondQuality')
    await user.click(screen.getByRole('button', { name: 'Sınıfı kaydet' }))
    await screen.findByText(/Sonuç bilinmiyor/)
    const original = api.classify.mock.calls[0]
    first.unmount()

    const denied = mount()
    await user.click(screen.getByRole('button', { name: 'Aynı anahtar ve içerikle yeniden gönder' }))
    await screen.findByText(/yetkiniz yok/)
    expect(auth.report).toHaveBeenCalledWith(403, { generation: 1 })
    expect(api.classify.mock.calls[1]).toEqual(original)
    denied.unmount()

    mount()
    await user.click(screen.getByRole('button', { name: 'Aynı anahtar ve içerikle yeniden gönder' }))
    await waitFor(() => expect(api.classify).toHaveBeenCalledTimes(3))
    expect(api.classify.mock.calls[2]).toEqual(original)
  })

  it('does not expose another shell namespace unresolved attempt', async () => {
    api.classify.mockResolvedValueOnce({ type: 'unknown', reason: 'network-error' })
    const user = userEvent.setup()
    const first = mount('SENTETIK-a')
    await user.selectOptions(screen.getByLabelText('Yeni sınıf'), 'SecondQuality')
    await user.click(screen.getByRole('button', { name: 'Sınıfı kaydet' }))
    await screen.findByText(/Sonuç bilinmiyor/)
    first.unmount()

    mount('SENTETIK-b')
    expect(screen.getByRole('button', { name: 'Sınıfı kaydet' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Aynı anahtar ve içerikle yeniden gönder' })).toBeNull()
    expect((screen.getByLabelText('Yeni sınıf') as HTMLSelectElement).disabled).toBe(false)
  })
})
