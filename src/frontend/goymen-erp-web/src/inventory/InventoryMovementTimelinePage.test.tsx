import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation, useParams } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { InventoryMovementTimelinePage } from './InventoryMovementTimelinePage'
import { INVENTORY_MOVEMENT_TIMELINE_ROUTE, movementTimelineEntryState } from './capabilities'
import type { InventoryMovement, InventoryMovementTimelinePage as TimelinePage } from './movementTimelineContract'
import { MOVEMENT_TIMELINE_INVALID_QUERY } from './movementTimelineApi'
import type { ViewOutcome } from '../shell/uiState'

const COMPANY = '11111111-1111-4111-8111-111111111111'
const OTHER_COMPANY = '99999999-9999-4999-8999-999999999999'
const LABEL = '22222222-2222-4222-8222-222222222222'
const OTHER_LABEL = '23232323-2323-4323-8323-232323232323'
const LOCATION = '33333333-3333-4333-8333-333333333333'
const PRODUCT = '44444444-4444-4444-8444-444444444444'
const SOURCE = '55555555-5555-4555-8555-555555555555'
const SOURCE_LINE = '66666666-6666-4666-8666-666666666666'

const api = vi.hoisted(() => ({ timeline: vi.fn() }))
const scenario = vi.hoisted(() => ({
  companyId: '11111111-1111-4111-8111-111111111111', namespace: 'SENTETIK-a' as string | null,
  canOpenLabel: true, report: vi.fn(),
}))
vi.mock('./movementTimelineApi', async (importOriginal) => ({
  ...await importOriginal<typeof import('./movementTimelineApi')>(),
  fetchInventoryMovementTimeline: api.timeline,
}))
vi.mock('../shell/shellContext', () => ({
  useShellState: () => ({ sessionEpoch: 1, routeEntryGeneration: 1,
    activeContext: { userId: 'actor-a', companyId: scenario.companyId, scopeMode: 'single', capabilities: [] } }),
}))
vi.mock('../shell/shellState', () => ({
  contextKey: () => scenario.namespace === null ? null : [scenario.namespace],
  contextKeyString: (key: readonly string[]) => key[0],
  hasCapability: (_shell: unknown, capability: string) =>
    capability === 'inventory.stock-positions.list' && scenario.canOpenLabel,
}))
vi.mock('../shell/useAuthBoundary', () => ({
  useAuthBoundary: () => ({ capture: () => ({ namespace: scenario.namespace }), report: scenario.report }),
}))

function movement(id: string, at: string, overrides: Partial<InventoryMovement> = {}): InventoryMovement {
  return {
    movementId: id, kind: 'Receipt', occurredAtUtc: at, sourceKind: 'inventory.opening-count', sourceId: SOURCE,
    sourceLineId: SOURCE_LINE, sourceGrain: 'Line',
    lines: [{
      sequence: 1, direction: 'In', labelId: LABEL, labelCode: 'SYN-LBL-1', ownerKind: 'Company', ownerId: COMPANY,
      custodianKind: 'Company', custodianId: COMPANY, locationId: LOCATION, productId: PRODUCT, block: 'Free',
      quantityKg: '12.5',
    }],
    ...overrides,
  }
}

const NEWEST = movement('aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', '2026-10-08T09:00:00+00:00')
const OLDER = movement('bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', '2026-10-08T08:00:00+00:00', {
  kind: 'Block', sourceKind: 'inventory.test-block', sourceLineId: null, sourceGrain: 'Whole',
  lines: [
    { ...NEWEST.lines[0], labelId: OTHER_LABEL, labelCode: 'SYN-LBL-2', direction: 'Out' },
    { ...NEWEST.lines[0], labelId: OTHER_LABEL, labelCode: 'SYN-LBL-2', sequence: 2, block: 'Blocked' },
  ],
})

function ok(items: readonly InventoryMovement[], nextCursor: string | null = null): ViewOutcome<TimelinePage> {
  return { type: 'ok-single', data: { items, nextCursor } }
}

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => { resolve = done })
  return { promise, resolve }
}

function LabelReceipt() {
  const { labelId } = useParams()
  const location = useLocation()
  return <h1>Label route: {labelId} · {JSON.stringify(location.state ?? null)}</h1>
}

function mount(state: unknown = movementTimelineEntryState(LABEL, 'SYN')) {
  const element = () => <MemoryRouter initialEntries={[{ pathname: INVENTORY_MOVEMENT_TIMELINE_ROUTE, state }]}>
    <Routes>
      <Route path={INVENTORY_MOVEMENT_TIMELINE_ROUTE} element={<InventoryMovementTimelinePage />} />
      <Route path="/stok/etiket/:labelId" element={<LabelReceipt />} />
    </Routes>
  </MemoryRouter>
  const view = render(element())
  return { ...view, refresh: () => view.rerender(element()) }
}

const rows = () => screen.getAllByRole('row').filter((row) => row.hasAttribute('data-movement-id'))

beforeEach(() => {
  scenario.companyId = COMPANY; scenario.namespace = 'SENTETIK-a'; scenario.canOpenLabel = true
  scenario.report.mockReset()
  api.timeline.mockReset().mockResolvedValue(ok([NEWEST]))
})
afterEach(() => cleanup())

describe('InventoryMovementTimelinePage', () => {
  it('shows company-wide immutable movements honestly, with the stored label and source evidence', async () => {
    mount()

    expect(await screen.findByRole('heading', { level: 1, name: 'Şirket stok hareketleri' })).not.toBeNull()
    await screen.findByText('SYN-LBL-1')
    expect(api.timeline).toHaveBeenCalledTimes(1)
    expect(api.timeline).toHaveBeenCalledWith(COMPANY, null, expect.any(AbortSignal))
    expect(screen.getByText(/tüm etiketlerindeki/)).not.toBeNull()
    expect(screen.getByText(/geldiğiniz etikete göre filtrelenmez/)).not.toBeNull()
    expect(screen.getByText(/ters kayıt, düzeltme ya da kaynak belge bilgisi çıkarmaz/)).not.toBeNull()

    const [row] = rows()
    expect(row.getAttribute('data-movement-id')).toBe(NEWEST.movementId)
    expect(row.getAttribute('data-source-id')).toBe(SOURCE)
    expect(row.getAttribute('data-label-id')).toBe(LABEL)
    const cells = within(row).getAllByRole('cell').map((cell) => cell.textContent)
    expect(cells[0]).toBe('2026-10-08T09:00:00+00:00')
    expect(cells[1]).toContain('Giriş')
    expect(cells[2]).toContain('inventory.opening-count')
    expect(cells[2]).toContain(SOURCE)
    expect(cells[2]).toContain('satır …66666666')
    expect(cells[5]).toBe('Şirket malıŞirket gözetiminde')
    expect(cells[6]).toBe(LOCATION)
    expect(cells[7]).toBe('Serbest')
    expect(cells[8]).toBe('12.5 kg')
    expect(within(row).getByRole('link', { name: 'SYN-LBL-1' }).getAttribute('href')).toBe(`/stok/etiket/${LABEL}`)
    // The page is not a source-document browser: no inferred link to a source module.
    expect(within(row).getAllByRole('link')).toHaveLength(1)
    expect(screen.getByText('Bu şirketin kayıtlı en eski stok hareketine ulaşıldı.')).not.toBeNull()
    expect(screen.queryByRole('button', { name: 'Daha eski hareketleri yükle' })).toBeNull()
  })

  it('returns to the label it came from with the carried ST-01 filter', async () => {
    const user = userEvent.setup()
    mount()
    await screen.findByText('SYN-LBL-1')

    await user.click(screen.getByRole('link', { name: '← Etikete dön' }))

    expect(await screen.findByRole('heading', { name: `Label route: ${LABEL} · {"stockPositionsSearch":"SYN"}` }))
      .not.toBeNull()
  })

  it('offers no return link without a carried label and never reads a label filter from the URL', async () => {
    mount(null)
    await screen.findByText('SYN-LBL-1')
    expect(screen.queryByRole('link', { name: '← Etikete dön' })).toBeNull()
    mount({ movementTimelineFromLabelId: 'not-a-guid' })
    expect(screen.queryByRole('link', { name: '← Etikete dön' })).toBeNull()
    for (const [, cursor] of api.timeline.mock.calls) expect(cursor).toBeNull()
  })

  it('renders label codes as plain text when the label capability is absent', async () => {
    scenario.canOpenLabel = false
    mount()
    await screen.findByText('SYN-LBL-1')
    expect(screen.queryByRole('link', { name: 'SYN-LBL-1' })).toBeNull()
  })

  it('pages with the owner cursor and appends the next server page without client paging', async () => {
    const user = userEvent.setup()
    api.timeline.mockResolvedValueOnce(ok([NEWEST], 'cursor.1')).mockResolvedValueOnce(ok([OLDER]))
    mount()
    await screen.findByText('SYN-LBL-1')
    expect(screen.getByText('1 hareket gösteriliyor.')).not.toBeNull()

    await user.click(screen.getByRole('button', { name: 'Daha eski hareketleri yükle' }))

    expect(await screen.findAllByText('SYN-LBL-2')).toHaveLength(2)
    expect(api.timeline).toHaveBeenCalledTimes(2)
    expect(api.timeline).toHaveBeenLastCalledWith(COMPANY, 'cursor.1', expect.any(AbortSignal))
    expect(rows().map((row) => row.getAttribute('data-movement-id'))).toEqual([
      NEWEST.movementId, OLDER.movementId, OLDER.movementId,
    ])
    expect(screen.getByText('2 hareket gösteriliyor.')).not.toBeNull()
    expect(screen.getAllByText('Blokaj')).toHaveLength(2)
    expect(screen.getByText('Bu şirketin kayıtlı en eski stok hareketine ulaşıldı.')).not.toBeNull()
  })

  it('shows the empty state', async () => {
    api.timeline.mockResolvedValueOnce(ok([]))
    mount()
    expect(await screen.findByText('Bu şirkette kayıtlı stok hareketi yok.')).not.toBeNull()
  })

  it('fails closed on a malformed first page', async () => {
    api.timeline.mockResolvedValueOnce({ type: 'malformed' })
    mount()
    expect(await screen.findByText(/Beklenmeyen yanıt/)).not.toBeNull()
    expect(screen.queryByRole('table')).toBeNull()
  })

  it('shows a first-page 400 as an invalid query with a restart', async () => {
    const user = userEvent.setup()
    api.timeline.mockResolvedValueOnce({ type: 'http', status: 400, body: { code: MOVEMENT_TIMELINE_INVALID_QUERY } })
    mount()
    expect(await screen.findByText(/isteği geçersiz bulundu/)).not.toBeNull()
    await user.click(screen.getByRole('button', { name: 'Baştan yükle' }))
    await screen.findByText('SYN-LBL-1')
    expect(api.timeline).toHaveBeenLastCalledWith(COMPANY, null, expect.any(AbortSignal))
  })

  it.each([
    ['an unknown code', { code: 'company_context_invalid' }],
    ['no code', { status: 400 }],
    ['an empty code', { code: '' }],
    ['no body', undefined],
  ])('keeps a first-page 400 with %s in the shared fail-closed state, not an invalid query', async (_name, body) => {
    api.timeline.mockResolvedValueOnce({ type: 'http', status: 400, body })
    mount()
    expect(await screen.findByText(/Beklenmeyen yanıt/)).not.toBeNull()
    expect(screen.queryByText(/isteği geçersiz bulundu/)).toBeNull()
    expect(screen.queryByRole('table')).toBeNull()
    expect(scenario.report).not.toHaveBeenCalled()
  })

  it.each([
    [401, /Oturum sonlandı/],
    [403, /Yetkiniz yok/],
  ])('reports a first-page %s to the auth boundary and shows its shared state', async (status, text) => {
    api.timeline.mockResolvedValueOnce({ type: 'http', status, body: { status } })
    mount()
    expect(await screen.findByText(text)).not.toBeNull()
    expect(scenario.report).toHaveBeenCalledWith(status, { namespace: 'SENTETIK-a' })
    expect(screen.queryByRole('table')).toBeNull()
  })

  it('shows a 503 as unavailable and retries the first page', async () => {
    const user = userEvent.setup()
    api.timeline.mockResolvedValueOnce({ type: 'http', status: 503, body: { code: 'inventory_movement_timeline_unavailable' } })
    mount()
    expect(await screen.findByText(/Servis geçici olarak kullanılamıyor/)).not.toBeNull()
    expect(scenario.report).not.toHaveBeenCalled()
    await user.click(screen.getByRole('button', { name: 'Tekrar dene' }))
    await screen.findByText('SYN-LBL-1')
  })

  it('keeps the shown pages on an expired or foreign cursor and restarts from the first page', async () => {
    const user = userEvent.setup()
    api.timeline.mockResolvedValueOnce(ok([NEWEST], 'cursor.expired'))
      .mockResolvedValueOnce({ type: 'http', status: 400, body: { code: MOVEMENT_TIMELINE_INVALID_QUERY } })
      .mockResolvedValueOnce(ok([NEWEST]))
    mount()
    await screen.findByText('SYN-LBL-1')
    await user.click(screen.getByRole('button', { name: 'Daha eski hareketleri yükle' }))

    expect(await screen.findByText(/Devam bağlantısının süresi doldu/)).not.toBeNull()
    expect(rows()).toHaveLength(1)
    expect(screen.queryByRole('button', { name: 'Daha eski hareketleri yükle' })).toBeNull()

    await user.click(screen.getByRole('button', { name: 'Baştan yükle' }))
    await waitFor(() => expect(api.timeline).toHaveBeenCalledTimes(3))
    expect(api.timeline).toHaveBeenLastCalledWith(COMPANY, null, expect.any(AbortSignal))
    await waitFor(() => expect(screen.queryByText(/Devam bağlantısının süresi doldu/)).toBeNull())
  })

  it('drops every shown row when a continuation is forbidden and reports it', async () => {
    const user = userEvent.setup()
    api.timeline.mockResolvedValueOnce(ok([NEWEST], 'cursor.1')).mockResolvedValueOnce({ type: 'http', status: 403 })
    mount()
    await screen.findByText('SYN-LBL-1')
    await user.click(screen.getByRole('button', { name: 'Daha eski hareketleri yükle' }))

    expect(await screen.findByText(/Yetkiniz yok/)).not.toBeNull()
    expect(screen.queryByText('SYN-LBL-1')).toBeNull()
    expect(scenario.report).toHaveBeenCalledWith(403, { namespace: 'SENTETIK-a' })
  })

  it('retries the same cursor after an unavailable continuation', async () => {
    const user = userEvent.setup()
    api.timeline.mockResolvedValueOnce(ok([NEWEST], 'cursor.1'))
      .mockResolvedValueOnce({ type: 'http', status: 503 })
      .mockResolvedValueOnce(ok([OLDER]))
    mount()
    await screen.findByText('SYN-LBL-1')
    await user.click(screen.getByRole('button', { name: 'Daha eski hareketleri yükle' }))
    expect(await screen.findByText(/şu anda okunamadı/)).not.toBeNull()
    expect(rows()).toHaveLength(1)

    await user.click(screen.getByRole('button', { name: 'Tekrar dene' }))
    expect(await screen.findAllByText('SYN-LBL-2')).toHaveLength(2)
    expect(api.timeline.mock.calls.map(([, cursor]) => cursor)).toEqual([null, 'cursor.1', 'cursor.1'])
  })

  it('keeps the shown pages when a continuation 400 has an unknown code, without calling it an expired cursor', async () => {
    const user = userEvent.setup()
    api.timeline.mockResolvedValueOnce(ok([NEWEST], 'cursor.1'))
      .mockResolvedValueOnce({ type: 'http', status: 400, body: { code: 'company_context_invalid' } })
    mount()
    await screen.findByText('SYN-LBL-1')
    await user.click(screen.getByRole('button', { name: 'Daha eski hareketleri yükle' }))

    expect(await screen.findByText(/doğrulanamadı; gösterilen listeyi devam ettirmiyor/)).not.toBeNull()
    expect(screen.queryByText(/Devam bağlantısının süresi doldu/)).toBeNull()
    expect(rows()).toHaveLength(1)
  })

  it('appends an equal-instant continuation in SQL Server uniqueidentifier order', async () => {
    const user = userEvent.setup()
    const at = '2026-10-07T12:00:00+00:00'
    const sqlFirst = movement('00000000-0000-4000-8000-000000000001', at)
    const sqlSecond = movement('01000000-0000-4000-8000-000000000000', at)
    api.timeline.mockResolvedValueOnce(ok([sqlFirst], 'cursor.1')).mockResolvedValueOnce(ok([sqlSecond]))
    mount()
    await screen.findByText('SYN-LBL-1')
    await user.click(screen.getByRole('button', { name: 'Daha eski hareketleri yükle' }))

    await waitFor(() => expect(rows().map((row) => row.getAttribute('data-movement-id')))
      .toEqual([sqlFirst.movementId, sqlSecond.movementId]))
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it.each([
    ['repeats a shown movement', ok([NEWEST])],
    ['precedes the last shown movement at the same instant', ok([movement('ffffffff-ffff-4fff-8fff-ffffffffffff', NEWEST.occurredAtUtc)])],
    ['is newer than the shown list', ok([movement('cccccccc-cccc-4ccc-8ccc-cccccccccccc', '2026-10-08T10:00:00+00:00')])],
    ['is malformed', { type: 'malformed' } as ViewOutcome<TimelinePage>],
  ])('refuses a continuation that %s and keeps the shown pages', async (_name, outcome) => {
    const user = userEvent.setup()
    api.timeline.mockResolvedValueOnce(ok([NEWEST], 'cursor.1')).mockResolvedValueOnce(outcome)
    mount()
    await screen.findByText('SYN-LBL-1')
    await user.click(screen.getByRole('button', { name: 'Daha eski hareketleri yükle' }))

    expect(await screen.findByText(/doğrulanamadı; gösterilen listeyi devam ettirmiyor/)).not.toBeNull()
    expect(rows().map((row) => row.getAttribute('data-movement-id'))).toEqual([NEWEST.movementId])
  })

  it('aborts and ignores a late answer from the previous company after a context change', async () => {
    const late = deferred<ViewOutcome<TimelinePage>>()
    const signals: AbortSignal[] = []
    api.timeline.mockImplementationOnce((_company: string, _cursor: string | null, signal: AbortSignal) => {
      signals.push(signal)
      return late.promise
    }).mockResolvedValueOnce(ok([OLDER]))
    const view = mount()
    await waitFor(() => expect(api.timeline).toHaveBeenCalledTimes(1))

    scenario.companyId = OTHER_COMPANY; scenario.namespace = 'SENTETIK-b'
    view.refresh()
    await waitFor(() => expect(api.timeline).toHaveBeenCalledTimes(2))
    expect(api.timeline).toHaveBeenLastCalledWith(OTHER_COMPANY, null, expect.any(AbortSignal))
    expect(signals[0].aborted).toBe(true)
    expect(await screen.findAllByText('SYN-LBL-2')).toHaveLength(2)

    late.resolve(ok([NEWEST]))
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(screen.queryByText('SYN-LBL-1')).toBeNull()
    expect(scenario.report).not.toHaveBeenCalled()
  })

  it('reads nothing without one authorized company', async () => {
    scenario.namespace = null
    mount()
    expect(await screen.findByText(/Yetkili tek şirket bağlamı seçilmeden/)).not.toBeNull()
    expect(api.timeline).not.toHaveBeenCalled()
  })
})
