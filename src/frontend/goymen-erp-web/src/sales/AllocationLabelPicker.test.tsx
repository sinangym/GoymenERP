import { useState } from 'react'
import { MemoryRouter } from 'react-router'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { AllocationLabelPicker, type AllocationLabelChoice } from './AllocationLabelPicker'

const COMPANY = '10000000-0000-4000-8000-000000000001'
const OTHER_COMPANY = '99999999-9999-4999-8999-999999999999'
const LINE = '30000000-0000-4000-8000-000000000001'
const PRODUCT = '40000000-0000-4000-8000-000000000001'
const OTHER_PRODUCT = '40000000-0000-4000-8000-000000000002'
const LABEL = '50000000-0000-4000-8000-000000000001'
const LOT = '80000000-0000-4000-8000-000000000001'
const LOCATION = '90000000-0000-4000-8000-000000000001'

const report = vi.fn()
vi.mock('../shell/useAuthBoundary', () => ({
  useAuthBoundary: () => ({ capture: () => ({ token: 1 }), report }),
}))

const position = (overrides: Record<string, unknown> = {}) => ({
  companyId: COMPANY, labelId: LABEL, labelVersion: '11', labelCode: 'ETK-001', productId: PRODUCT,
  lot: { id: LOT, heatNumber: 'D-1', coilNumber: 'B-9' }, heatNumber: 'D-1', coilNumber: 'B-9',
  documentStatus: 'Posted', ownerKind: 'Company', ownerId: COMPANY, custodianKind: 'Company', custodianId: COMPANY,
  locationId: LOCATION, physicalKg: '20', freePhysicalKg: '20', blockedPhysicalKg: '0', activeAllocationKg: '0',
  availableKg: '20', ...overrides,
})

function answer(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

function Harness({ disabled = false, canOpenLabelHistory = false, onSelect }: {
  readonly disabled?: boolean
  readonly canOpenLabelHistory?: boolean
  readonly onSelect?: (choice: AllocationLabelChoice | null) => void
}) {
  const [selected, setSelected] = useState<AllocationLabelChoice | null>(null)
  return <MemoryRouter><AllocationLabelPicker companyId={COMPANY} productId={PRODUCT} lineId={LINE} disabled={disabled}
    canOpenLabelHistory={canOpenLabelHistory}
    selected={selected} onSelect={(choice) => { setSelected(choice); onSelect?.(choice) }} /></MemoryRouter>
}

async function searchFor(text: string) {
  if (text !== '') await userEvent.type(screen.getByLabelText('Etiket kodu ara'), text)
  await userEvent.click(screen.getByRole('button', { name: 'Etiketleri getir' }))
}

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.clearAllMocks()
})

describe('UI-13 allocation label picker', () => {
  it('searches only this line\'s product in this company and hands back the picked id and version', async () => {
    const fetch = vi.fn(async () => answer({ items: [position()], searchWindowFull: false }))
    vi.stubGlobal('fetch', fetch)
    const onSelect = vi.fn()
    render(<Harness onSelect={onSelect} />)
    expect(fetch).not.toHaveBeenCalled()
    expect(screen.getByRole('group', { name: 'Etiket seçimi' })).toBeTruthy()

    await searchFor('  ETK-0  ')
    const choose = await screen.findByRole('button', { name: 'ETK-001 etiketini seç' })
    expect(screen.getByRole('table', { name: 'Bu ürünün etiketleri' })).toBeTruthy()
    expect(screen.getByText('D-1 / B-9')).toBeTruthy()
    const [input, init] = fetch.mock.calls[0] as unknown as [string, RequestInit]
    const url = new URL(input, 'http://localhost')
    expect(url.pathname).toBe('/api/v1/inventory/positions')
    expect(Object.fromEntries(url.searchParams)).toEqual({ limit: '25', productId: PRODUCT, q: 'ETK-0' })
    expect(new Headers(init.headers).get('X-Company-Id')).toBe(COMPANY)
    expect(init.method ?? 'GET').toBe('GET')

    await userEvent.click(choose)
    expect(onSelect).toHaveBeenLastCalledWith({ labelId: LABEL, labelVersion: '11', labelCode: 'ETK-001', availableKg: '20' })
    expect(choose.getAttribute('aria-pressed')).toBe('true')
    expect(choose.textContent).toBe('Seçildi')

    await userEvent.click(screen.getByRole('button', { name: 'Seçimi kaldır' }))
    expect(onSelect).toHaveBeenLastCalledWith(null)
    expect(screen.getByText('Henüz etiket seçilmedi.')).toBeTruthy()
  })

  it('omits q for an empty search and shows the empty state', async () => {
    const fetch = vi.fn(async () => answer({ items: [], searchWindowFull: false }))
    vi.stubGlobal('fetch', fetch)
    render(<Harness />)
    await searchFor('')
    expect(await screen.findByTestId(`sales-allocation-label-empty-${LINE}`)).toBeTruthy()
    expect(screen.getByText('Bu ürün için aramaya uyan stoklu etiket yok.')).toBeTruthy()
    const url = new URL(String((fetch.mock.calls[0] as unknown as [string])[0]), 'http://localhost')
    expect(url.searchParams.has('q')).toBe(false)
    expect(screen.queryByRole('table')).toBeNull()
  })

  it('asks to narrow the search when the server window is full', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => answer({ items: [position()], searchWindowFull: true })))
    render(<Harness />)
    await searchFor('E')
    expect(await screen.findByText('Aramaya uyan etiketlerin yalnız ilk sayfası gösteriliyor; etiket kodunu daraltarak arayın.'))
      .toBeTruthy()
    expect(screen.getByRole('button', { name: 'ETK-001 etiketini seç' })).toBeTruthy()
  })

  it('shows an unavailable error with a retry that reads again', async () => {
    let calls = 0
    const fetch = vi.fn(async () => {
      calls += 1
      return calls === 1 ? answer({ status: 503 }, 503) : answer({ items: [position()], searchWindowFull: false })
    })
    vi.stubGlobal('fetch', fetch)
    render(<Harness />)
    await searchFor('ETK')
    const alert = await screen.findByRole('alert')
    expect(alert.textContent).toContain('Etiketler şu anda okunamıyor.')
    expect(screen.queryByRole('table')).toBeNull()
    await userEvent.click(screen.getByRole('button', { name: 'Tekrar ara' }))
    expect(await screen.findByRole('button', { name: 'ETK-001 etiketini seç' })).toBeTruthy()
    expect(screen.queryByRole('alert')).toBeNull()
    expect(fetch).toHaveBeenCalledTimes(2)
  })

  it('shows the network failure as unavailable, never as an empty result', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('Failed to fetch') }))
    render(<Harness />)
    await searchFor('ETK')
    expect((await screen.findByRole('alert')).textContent).toContain('Etiketler şu anda okunamıyor.')
    expect(screen.queryByTestId(`sales-allocation-label-empty-${LINE}`)).toBeNull()
  })

  it.each([
    ['another company\'s row', position({ companyId: OTHER_COMPANY })],
    ['another product\'s row', position({ productId: OTHER_PRODUCT })],
    ['a row whose components do not add up', position({ availableKg: '21' })],
  ])('fails closed on %s and offers nothing to pick', async (_name, row) => {
    vi.stubGlobal('fetch', vi.fn(async () => answer({ items: [row], searchWindowFull: false })))
    render(<Harness />)
    await searchFor('ETK')
    expect((await screen.findByRole('alert')).textContent).toContain('Etiket yanıtı doğrulanamadı; hiçbir etiket gösterilmedi.')
    expect(screen.queryByRole('button', { name: /etiketini seç$/ })).toBeNull()
  })

  it.each([401, 403])('reports %i to the auth boundary and shows no rows', async (status) => {
    vi.stubGlobal('fetch', vi.fn(async () => answer({ status }, status)))
    render(<Harness />)
    await searchFor('ETK')
    expect((await screen.findByRole('alert')).textContent).toBe('Etiket arama yetkisi yok ya da artık geçerli değil.')
    expect(report).toHaveBeenCalledWith(status, { token: 1 })
    expect(screen.queryByRole('button', { name: 'Tekrar ara' })).toBeNull()
  })

  it('shows only the newest search when an older answer arrives late', async () => {
    let releaseFirst: (response: Response) => void = () => undefined
    const fetch = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const q = new URL(String(input), 'http://localhost').searchParams.get('q')
      if (q === 'OLD') {
        return new Promise<Response>((resolve, reject) => {
          releaseFirst = resolve
          init?.signal?.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')))
        })
      }
      return Promise.resolve(answer({ items: [position({ labelCode: 'NEW-1' })], searchWindowFull: false }))
    })
    vi.stubGlobal('fetch', fetch)
    render(<Harness />)
    await userEvent.type(screen.getByLabelText('Etiket kodu ara'), 'OLD{Enter}')
    expect(screen.getByText('Etiketler okunuyor…')).toBeTruthy()
    await userEvent.clear(screen.getByLabelText('Etiket kodu ara'))
    await userEvent.type(screen.getByLabelText('Etiket kodu ara'), 'NEW{Enter}')
    expect(await screen.findByRole('button', { name: 'NEW-1 etiketini seç' })).toBeTruthy()
    // The superseded read was aborted; even if its answer still arrives, it is never shown.
    releaseFirst(answer({ items: [position({ labelCode: 'OLD-1' })], searchWindowFull: false }))
    await Promise.resolve()
    expect(screen.queryByRole('button', { name: 'OLD-1 etiketini seç' })).toBeNull()
    expect(fetch).toHaveBeenCalledTimes(2)
  })

  it('aborts an in-flight search when the picker unmounts', async () => {
    let signal: AbortSignal | undefined
    vi.stubGlobal('fetch', vi.fn((_input: RequestInfo | URL, init?: RequestInit) => {
      signal = init?.signal ?? undefined
      return new Promise<Response>(() => undefined)
    }))
    const view = render(<Harness />)
    await searchFor('ETK')
    await waitFor(() => expect(signal).toBeDefined())
    view.unmount()
    expect(signal?.aborted).toBe(true)
  })

  it('does not show a fresher version of the picked label as already chosen (chosen = id and version)', async () => {
    let version = '11'
    vi.stubGlobal('fetch', vi.fn(async () => answer({ items: [position({ labelVersion: version })], searchWindowFull: false })))
    render(<Harness />)
    await searchFor('ETK')
    await userEvent.click(await screen.findByRole('button', { name: 'ETK-001 etiketini seç' }))
    expect(screen.getByRole('button', { name: 'ETK-001 etiketi seçildi' }).getAttribute('aria-pressed')).toBe('true')

    version = '12'
    await userEvent.click(screen.getByRole('button', { name: 'Etiketleri getir' }))
    const fresher = await screen.findByRole('button', { name: 'ETK-001 etiketini seç' })
    expect(fresher.getAttribute('aria-pressed')).toBe('false')
    expect(fresher.textContent).toBe('Seç')
    // The pick still names the version it was read at; the command would carry 11, never a silently swapped 12.
    expect(screen.getByTestId(`sales-allocation-label-selected-${LINE}`).textContent).toContain('sürüm 11')
  })

  it.each([
    ['owned by a counterparty', { ownerKind: 'Counterparty', ownerId: OTHER_COMPANY }],
    ['held by a counterparty', { custodianKind: 'Counterparty', custodianId: OTHER_COMPANY }],
    ['owned by another company id', { ownerId: OTHER_COMPANY }],
  ])('lists a label %s as not selectable with the reason (SAT-D20 1)', async (_name, overrides) => {
    vi.stubGlobal('fetch', vi.fn(async () => answer({ items: [position(overrides)], searchWindowFull: false })))
    const onSelect = vi.fn()
    render(<Harness onSelect={onSelect} />)
    await searchFor('ETK')
    const cell = await screen.findByTestId(`sales-allocation-label-unselectable-${LABEL}`)
    expect(cell.textContent).toBe('Seçilemez: Stok şirketin kendi malı ve kendi gözetiminde değil.')
    expect(screen.getByTestId(`sales-allocation-label-row-${LABEL}`).getAttribute('data-selectable')).toBe('false')
    expect(screen.queryByRole('button', { name: /ETK-001 etiketini seç/ })).toBeNull()
    expect(screen.queryByTestId(`sales-allocation-label-option-${LABEL}`)).toBeNull()
    expect(onSelect).not.toHaveBeenCalled()
  })

  it('names every reason when a counterparty label is also fully allocated', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => answer({ items: [position({ ownerKind: 'Counterparty', ownerId: OTHER_COMPANY,
      activeAllocationKg: '20', availableKg: '0' })], searchWindowFull: false })))
    render(<Harness />)
    await searchFor('ETK')
    await userEvent.click(await screen.findByLabelText('Kullanılabilir miktarı sıfır olanları da göster'))
    expect(screen.getByTestId(`sales-allocation-label-unselectable-${LABEL}`).textContent).toBe(
      'Seçilemez: Stok şirketin kendi malı ve kendi gözetiminde değil. Serbest miktarın tamamı başka tahsislerde.')
  })

  it('hides zero-available labels by default and lists them, not selectable, once the filter is lifted (SAT-D20 3)', async () => {
    const BLOCKED = '50000000-0000-4000-8000-000000000002'
    const ALLOCATED = '50000000-0000-4000-8000-000000000003'
    const fetch = vi.fn(async () => answer({ items: [
      position(),
      position({ labelId: BLOCKED, labelCode: 'ETK-002', freePhysicalKg: '0', blockedPhysicalKg: '20', availableKg: '0' }),
      position({ labelId: ALLOCATED, labelCode: 'ETK-003', activeAllocationKg: '20', availableKg: '0' }),
    ], searchWindowFull: false }))
    vi.stubGlobal('fetch', fetch)
    render(<Harness />)
    await searchFor('ETK')
    expect(await screen.findByRole('button', { name: 'ETK-001 etiketini seç' })).toBeTruthy()
    const toggle = screen.getByLabelText('Kullanılabilir miktarı sıfır olanları da göster') as HTMLInputElement
    expect(toggle.checked).toBe(false)
    expect(screen.queryByText('ETK-002')).toBeNull()
    expect(screen.queryByText('ETK-003')).toBeNull()
    expect(screen.getByText('Kullanılabilir miktarı sıfır olan 2 etiket varsayılan filtreyle gizlendi.')).toBeTruthy()

    await userEvent.click(toggle)
    expect(toggle.checked).toBe(true)
    expect(screen.getByTestId(`sales-allocation-label-unselectable-${BLOCKED}`).textContent)
      .toBe('Seçilemez: Serbest stok yok; etiketteki miktar blokeli.')
    expect(screen.getByTestId(`sales-allocation-label-unselectable-${ALLOCATED}`).textContent)
      .toBe('Seçilemez: Serbest miktarın tamamı başka tahsislerde.')
    expect(screen.queryByTestId(`sales-allocation-label-option-${BLOCKED}`)).toBeNull()
    expect(screen.queryByTestId(`sales-allocation-label-option-${ALLOCATED}`)).toBeNull()
    expect(screen.queryByText(/varsayılan filtreyle gizlendi/)).toBeNull()
    // The filter is view-only: lifting it reads nothing again.
    expect(fetch).toHaveBeenCalledTimes(1)

    await userEvent.click(toggle)
    expect(screen.queryByText('ETK-002')).toBeNull()
  })

  it('says a page of only zero-available labels is hidden, never that nothing matched', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => answer({ items: [position({ activeAllocationKg: '20', availableKg: '0' })],
      searchWindowFull: true })))
    render(<Harness />)
    await searchFor('ETK')
    expect((await screen.findByTestId(`sales-allocation-label-all-hidden-${LINE}`)).textContent).toContain(
      'Aramaya uyan 1 etiketin hiçbirinde kullanılabilir miktar yok; varsayılan filtre bunları gizliyor.')
    expect(screen.queryByTestId(`sales-allocation-label-empty-${LINE}`)).toBeNull()
    expect(screen.queryByRole('table')).toBeNull()
    expect(screen.getByText(/yalnız ilk sayfasını döndürdü ve bu sayfadaki kullanılabilir miktarı sıfır olan etiketler gizlendi/))
      .toBeTruthy()
    await userEvent.click(screen.getByLabelText('Kullanılabilir miktarı sıfır olanları da göster'))
    expect(screen.getByTestId(`sales-allocation-label-unselectable-${LABEL}`)).toBeTruthy()
    expect(screen.queryByTestId(`sales-allocation-label-all-hidden-${LINE}`)).toBeNull()
    expect(screen.getByText('Aramaya uyan etiketlerin yalnız ilk sayfası gösteriliyor; etiket kodunu daraltarak arayın.'))
      .toBeTruthy()
  })

  it('links every row, selectable or not, to the Inventory label history when the viewer may open it (SAT-D20 2)', async () => {
    const OTHER = '50000000-0000-4000-8000-000000000002'
    vi.stubGlobal('fetch', vi.fn(async () => answer({ items: [
      position(), position({ labelId: OTHER, labelCode: 'ETK-002', ownerKind: 'Counterparty', ownerId: OTHER_COMPANY }),
    ], searchWindowFull: false })))
    render(<Harness canOpenLabelHistory />)
    await searchFor('ETK')
    const selectable = await screen.findByRole('link', { name: 'ETK-001 etiket geçmişini aç' })
    expect(selectable.getAttribute('href')).toBe(`/stok/etiket/${LABEL}`)
    const unselectable = screen.getByRole('link', { name: 'ETK-002 etiket geçmişini aç' })
    expect(unselectable.getAttribute('href')).toBe(`/stok/etiket/${OTHER}`)
    expect(screen.getByRole('columnheader', { name: 'Geçmiş' })).toBeTruthy()
  })

  it('renders no label-history link or column without the Inventory read capability', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => answer({ items: [position()], searchWindowFull: false })))
    render(<Harness />)
    await searchFor('ETK')
    expect(await screen.findByRole('button', { name: 'ETK-001 etiketini seç' })).toBeTruthy()
    expect(screen.queryByRole('link')).toBeNull()
    expect(screen.queryByRole('columnheader', { name: 'Geçmiş' })).toBeNull()
  })

  it('keeps every control inert while the panel is busy', () => {
    const fetch = vi.fn()
    vi.stubGlobal('fetch', fetch)
    render(<Harness disabled />)
    expect((screen.getByLabelText('Etiket kodu ara') as HTMLInputElement).disabled).toBe(true)
    expect((screen.getByRole('button', { name: 'Etiketleri getir' }) as HTMLButtonElement).disabled).toBe(true)
    expect((screen.getByLabelText('Kullanılabilir miktarı sıfır olanları da göster') as HTMLInputElement).disabled).toBe(true)
    expect(fetch).not.toHaveBeenCalled()
  })
})
