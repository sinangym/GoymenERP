/** Synthetic component evidence only. No endpoint, SQL, grant or production acceptance is asserted here. */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { initialShellState, type ShellState } from '../../shell/shellState'
import type { ViewOutcome } from '../../shell/uiState'
import { fetchPaintCandidates, fetchPaintCatalog, sendPaintCommand, validatePaintFile } from './api'
import { PAINT_MEMBERSHIP_CAPABILITY, PAINT_SOURCE, type PaintCandidates, type PaintCatalog, type PaintMutation, type PaintPreview } from './contract'
import { PaintMembershipPage } from './PaintMembershipPage'
import { resetPaintAttemptsForTests } from './paintAttempt'
import { freezePaintFile } from './paintFile'
import { PAINT_TEXT as T } from './paintMembershipText'

const COMPANY = '11111111-1111-1111-1111-111111111111'
const PRODUCT = '22222222-2222-2222-2222-222222222222'
const OTHER = '33333333-3333-3333-3333-333333333333'
const SHA = 'a'.repeat(64)
const security = vi.hoisted(() => ({ capture: vi.fn(() => ({ actorId: 'synthetic-actor', sessionEpoch: 1,
  companyId: '11111111-1111-1111-1111-111111111111', committedContextRevision: 1,
  requestFenceGeneration: 1, capabilityGeneration: 'g1', namespace: 'synthetic-namespace' })), report: vi.fn() }))
let currentShell: ShellState
vi.mock('../../shell/shellContext', () => ({ useShellState: () => currentShell }))
vi.mock('../../shell/useAuthBoundary', () => ({ useAuthBoundary: () => security }))
vi.mock('./api', () => ({ PAINT_API_BASE: '/api/v1', fetchPaintCandidates: vi.fn(), fetchPaintCatalog: vi.fn(), validatePaintFile: vi.fn(), sendPaintCommand: vi.fn() }))
vi.mock('./paintFile', async (importOriginal) => {
  const original = await importOriginal<typeof import('./paintFile')>()
  return { ...original, freezePaintFile: vi.fn(async (file: File) => Object.freeze({ fileName: file.name, contentBase64: 'AAH/', sha256: 'a'.repeat(64) })) }
})
const clients: QueryClient[] = []
function catalog(overrides: Partial<PaintCatalog> = {}): PaintCatalog {
  return { outcome: 2, companyId: COMPANY, version: '0', entries: [], sourceVersion: PAINT_SOURCE, ...overrides }
}
function preview(overrides: Partial<PaintPreview> = {}): PaintPreview {
  return { companyId: COMPANY, version: '0', fileSha256: SHA, productCodeColumn: 'Ürün kodu', previewToken: 'synthetic-preview',
    sourceRowCount: 1, rows: [{ rowNumber: 2, productId: PRODUCT, productVersion: '7', membershipVersion: '0', productCode: '00012', productName: 'SENTETIK boya', alreadyMember: false }],
    errors: [], ready: true, ...overrides }
}
function mutation(overrides: Partial<PaintMutation> = {}): PaintMutation {
  return { companyId: COMPANY, version: '2', changedEntries: [], removedProductIds: [], isReplay: false, originalCorrelationId: 'synthetic-original', ...overrides }
}
function memberCatalog(): PaintCatalog {
  return catalog({ outcome: 1, version: '1', entries: [{ productId: PRODUCT, productVersion: '7', membershipVersion: '1', productCode: '00012', productName: 'SENTETIK boya', sourceVersion: PAINT_SOURCE }] })
}
function candidates(overrides: Partial<PaintCandidates> = {}): PaintCandidates {
  return { outcome: 1, companyId: COMPANY, version: '1', q: null, limit: 25, searchWindowFull: false, sourceVersion: PAINT_SOURCE,
    items: [{ productId: PRODUCT, productVersion: '7', membershipVersion: '0', productCode: '00012', productName: 'SENTETIK aday', alreadyMember: false }], ...overrides }
}
function mount() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0, gcTime: 0, refetchOnWindowFocus: false } } })
  clients.push(client)
  const element = () => <QueryClientProvider client={client}><PaintMembershipPage /></QueryClientProvider>
  const result = render(element())
  return { ...result, refresh: () => result.rerender(element()) }
}
async function selectFile() {
  const user = userEvent.setup()
  await user.upload(screen.getByLabelText(T.file), new File([new Uint8Array([0, 1, 255])], 'SENTETIK.xlsx', { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }))
  await waitFor(() => expect((screen.getByRole('button', { name: T.validate }) as HTMLButtonElement).disabled).toBe(false))
  return user
}
async function readyPreview() {
  const user = await selectFile()
  await user.click(screen.getByRole('button', { name: T.validate }))
  await screen.findByText(T.ready)
  return user
}
beforeEach(() => {
  vi.clearAllMocks()
  let key = 0
  vi.stubGlobal('crypto', { randomUUID: () => `synthetic-key-${++key}` })
  currentShell = { ...initialShellState, phase: 'idle', committedContextRevision: 1, sessionEpoch: 1,
    activeContext: { userId: 'synthetic-actor', scopeMode: 'single', companyId: COMPANY, capabilityGeneration: 'g1', capabilities: [PAINT_MEMBERSHIP_CAPABILITY] } }
  vi.mocked(fetchPaintCatalog).mockResolvedValue({ type: 'ok-single', data: catalog() })
  vi.mocked(fetchPaintCandidates).mockResolvedValue({ type: 'ok-single', data: candidates() })
  vi.mocked(validatePaintFile).mockResolvedValue({ type: 'ok-single', data: preview() })
  vi.mocked(sendPaintCommand).mockResolvedValue({ type: 'applied', data: mutation() })
  vi.mocked(freezePaintFile).mockResolvedValue(Object.freeze({ fileName: 'SENTETIK.xlsx', contentBase64: 'AAH/', sha256: SHA }))
})
afterEach(() => {
  cleanup()
  clients.splice(0).forEach((client) => client.clear())
  resetPaintAttemptsForTests()
  vi.unstubAllGlobals()
})

describe('paint membership read states', () => {
  it('shows loading before source resolution and keeps commands absent', () => {
    vi.mocked(fetchPaintCatalog).mockReturnValue(new Promise(() => {}))
    mount()
    expect(screen.getByText(/Yükleniyor/)).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.apply })).toBeNull()
  })
  it('keeps first-source ERP edits closed while allowing a real file preview', async () => {
    mount()
    expect(await screen.findByText(T.noSource)).toBeTruthy()
    await readyPreview()
    expect((screen.getByRole('button', { name: T.add }) as HTMLButtonElement).disabled).toBe(true)
    expect((screen.getByRole('button', { name: T.apply }) as HTMLButtonElement).disabled).toBe(false)
  })
  it('distinguishes a real empty source from no source', async () => {
    vi.mocked(fetchPaintCatalog).mockResolvedValue({ type: 'ok-single', data: catalog({ outcome: 1, version: '2' }) })
    mount()
    expect(await screen.findByText(T.empty)).toBeTruthy()
    expect(screen.queryByText(T.noSource)).toBeNull()
  })
  it.each<readonly [ViewOutcome<PaintCatalog>, string]>([
    [{ type: 'http', status: 401 }, 'Oturum sonlandı'],
    [{ type: 'http', status: 403 }, 'Yetkiniz yok'],
    [{ type: 'http', status: 404 }, 'Bulunamadı'],
    [{ type: 'http', status: 409 }, 'Çakışma'],
    [{ type: 'http', status: 503 }, 'Servis geçici olarak kullanılamıyor'],
    [{ type: 'offline' }, 'Bağlantı yok'],
    [{ type: 'network-error' }, 'Sunucuya ulaşılamadı'],
    [{ type: 'malformed' }, 'Beklenmeyen yanıt'],
  ])('renders a distinct safe read state %j', async (outcome, text) => {
    vi.mocked(fetchPaintCatalog).mockResolvedValue(outcome)
    mount()
    expect(await screen.findByText(new RegExp(text))).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.apply })).toBeNull()
    if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
      expect(security.report).toHaveBeenCalledWith(outcome.status, security.capture.mock.results[0].value)
    }
  })
  it('treats owner-unavailable outcome as unavailable rather than an empty source', async () => {
    vi.mocked(fetchPaintCatalog).mockResolvedValue({ type: 'ok-single', data: catalog({ outcome: 3 }) })
    mount()
    expect(await screen.findByText(/Servis geçici olarak kullanılamıyor/)).toBeTruthy()
    expect(screen.queryByText(T.noSource)).toBeNull()
  })
  it('does not read anything without the one required capability', () => {
    currentShell = { ...currentShell, activeContext: { ...currentShell.activeContext!, capabilities: [] } }
    mount()
    expect(screen.getByText(/Yetkiniz yok/)).toBeTruthy()
    expect(fetchPaintCatalog).not.toHaveBeenCalled()
  })
})

describe('paint membership actual Product candidate maintenance', () => {
  function initialized(version = '1') {
    vi.mocked(fetchPaintCatalog).mockResolvedValue({ type: 'ok-single', data: catalog({ outcome: 1, version }) })
  }
  it('does not offer candidate discovery before the first actual Excel source', async () => {
    mount(); await screen.findByText(T.noSource)
    expect(screen.queryByLabelText(T.candidateQuery)).toBeNull()
    expect(fetchPaintCandidates).not.toHaveBeenCalled()
  })
  it('adds an actual searched Product using all three owner versions without selecting another workbook', async () => {
    initialized()
    vi.mocked(fetchPaintCandidates).mockResolvedValue({ type: 'ok-single', data: candidates({ q: '00012' }) })
    mount(); await screen.findByLabelText(T.candidateQuery)
    const user = userEvent.setup()
    await user.type(screen.getByLabelText(T.candidateQuery), '00012')
    await user.click(screen.getByRole('button', { name: T.search }))
    await screen.findByRole('table', { name: T.candidates })
    expect(fetchPaintCandidates).toHaveBeenCalledWith(COMPANY, '00012', expect.any(AbortSignal))
    await user.type(screen.getByLabelText(T.reason), 'SENTETIK ERP ekleme')
    await user.click(screen.getByRole('button', { name: T.add }))
    expect(sendPaintCommand).toHaveBeenCalledWith(COMPANY, { kind: 'set', productId: PRODUCT, payload: {
      expectedVersion: '1', expectedProductVersion: '7', expectedMembershipVersion: '0', isMember: true, reason: 'SENTETIK ERP ekleme',
    } }, 'paint-synthetic-key-1')
    expect(validatePaintFile).not.toHaveBeenCalled()
    expect(freezePaintFile).not.toHaveBeenCalled()
  })
  it('requires an explicit reason and never adds an existing member twice', async () => {
    initialized()
    mount(); await screen.findByLabelText(T.candidateQuery)
    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: T.search }))
    await screen.findByRole('button', { name: T.add })
    await user.click(screen.getByRole('button', { name: T.add }))
    expect(screen.getByText(T.reasonRequired)).toBeTruthy()
    expect(sendPaintCommand).not.toHaveBeenCalled()
    vi.mocked(fetchPaintCandidates).mockResolvedValue({ type: 'ok-single', data: candidates({ items: [{
      ...candidates().items[0], alreadyMember: true, membershipVersion: '1',
    }] }) })
    await user.click(screen.getByRole('button', { name: T.search }))
    await screen.findByText(T.alreadyMember)
    expect(screen.queryByRole('button', { name: T.add })).toBeNull()
  })
  it('blocks a candidate from an older list and clears it when search text changes', async () => {
    initialized('2')
    mount(); await screen.findByLabelText(T.candidateQuery)
    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: T.search }))
    await screen.findByText(T.candidateExpired)
    expect((screen.getByRole('button', { name: T.add }) as HTMLButtonElement).disabled).toBe(true)
    await user.type(screen.getByLabelText(T.candidateQuery), 'changed')
    expect(screen.queryByRole('table', { name: T.candidates })).toBeNull()
    expect(sendPaintCommand).not.toHaveBeenCalled()
  })
  it('distinguishes a current empty candidate window without creating a source or guessing a Product', async () => {
    initialized()
    vi.mocked(fetchPaintCandidates).mockResolvedValue({ type: 'ok-single', data: candidates({ items: [] }) })
    mount(); await screen.findByLabelText(T.candidateQuery)
    await userEvent.setup().click(screen.getByRole('button', { name: T.search }))
    expect(await screen.findByText(T.noCandidates)).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.add })).toBeNull()
    expect(sendPaintCommand).not.toHaveBeenCalled()
  })
  it.each<readonly [ViewOutcome<PaintCandidates>, string]>([
    [{ type: 'http', status: 401 }, 'Oturum sonlandı'], [{ type: 'http', status: 403 }, 'Yetkiniz yok'],
    [{ type: 'http', status: 404 }, 'Bulunamadı'], [{ type: 'http', status: 409 }, 'Çakışma'],
    [{ type: 'http', status: 503 }, 'Servis geçici olarak kullanılamıyor'], [{ type: 'offline' }, 'Bağlantı yok'],
    [{ type: 'network-error' }, 'Sunucuya ulaşılamadı'], [{ type: 'malformed' }, 'Beklenmeyen yanıt'],
  ])('keeps the candidate failure state distinct and add closed %j', async (outcome, text) => {
    initialized()
    vi.mocked(fetchPaintCandidates).mockResolvedValue(outcome)
    mount(); await screen.findByLabelText(T.candidateQuery)
    await userEvent.setup().click(screen.getByRole('button', { name: T.search }))
    expect(await screen.findByText(new RegExp(text))).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.add })).toBeNull()
    if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403))
      expect(security.report).toHaveBeenCalledWith(outcome.status, expect.anything())
  })
  it('shows loading during the actual candidate read', async () => {
    initialized()
    vi.mocked(fetchPaintCandidates).mockReturnValue(new Promise(() => {}))
    mount(); await screen.findByLabelText(T.candidateQuery)
    await userEvent.setup().click(screen.getByRole('button', { name: T.search }))
    expect(await screen.findByText(/Yükleniyor/)).toBeTruthy()
    expect((screen.getByRole('button', { name: T.search }) as HTMLButtonElement).disabled).toBe(true)
  })
  it('aborts and hides a former company candidate read after the committed company changes', async () => {
    initialized()
    let resolve!: (value: ViewOutcome<PaintCandidates>) => void
    vi.mocked(fetchPaintCandidates).mockReturnValue(new Promise((done) => { resolve = done }))
    const page = mount(); await screen.findByLabelText(T.candidateQuery)
    await userEvent.setup().click(screen.getByRole('button', { name: T.search }))
    const signal = vi.mocked(fetchPaintCandidates).mock.calls[0][2]!
    currentShell = { ...currentShell, committedContextRevision: 2, activeContext: { ...currentShell.activeContext!, companyId: OTHER } }
    vi.mocked(fetchPaintCatalog).mockResolvedValue({ type: 'ok-single', data: catalog({ companyId: OTHER }) })
    page.refresh()
    await screen.findByText(T.noSource)
    await act(async () => { resolve({ type: 'ok-single', data: candidates() }) })
    expect(signal.aborted).toBe(true)
    expect(screen.queryByText('SENTETIK aday')).toBeNull()
    expect(screen.queryByRole('button', { name: T.add })).toBeNull()
    expect(sendPaintCommand).not.toHaveBeenCalled()
  })
  it('retains the same searched-product payload and key when a write result is ambiguous', async () => {
    initialized()
    vi.mocked(sendPaintCommand).mockResolvedValueOnce({ type: 'unknown', reason: 'network-error' })
    mount(); await screen.findByLabelText(T.candidateQuery)
    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: T.search }))
    await screen.findByRole('button', { name: T.add })
    await user.type(screen.getByLabelText(T.reason), 'SENTETIK ekleme')
    await user.click(screen.getByRole('button', { name: T.add }))
    await screen.findByText(T.unknown)
    expect((screen.getByLabelText(T.candidateQuery) as HTMLInputElement).disabled).toBe(true)
    await user.click(screen.getByRole('button', { name: T.retry }))
    await waitFor(() => expect(sendPaintCommand).toHaveBeenCalledTimes(2))
    expect(vi.mocked(sendPaintCommand).mock.calls[1]).toEqual(vi.mocked(sendPaintCommand).mock.calls[0])
  })
  it('discards candidates after an owner concurrency refusal and refreshes the actual catalog', async () => {
    initialized()
    vi.mocked(sendPaintCommand).mockResolvedValue({ type: 'refused', status: 409, body: null })
    mount(); await screen.findByLabelText(T.candidateQuery)
    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: T.search }))
    await screen.findByRole('button', { name: T.add })
    await user.type(screen.getByLabelText(T.reason), 'SENTETIK ekleme')
    await user.click(screen.getByRole('button', { name: T.add }))
    await screen.findByText(/Çakışma/)
    await waitFor(() => expect(fetchPaintCatalog).toHaveBeenCalledTimes(2))
    expect(screen.queryByRole('table', { name: T.candidates })).toBeNull()
  })
})

describe('paint membership frozen preview and explicit commands', () => {
  it('binds an explicit apply to frozen bytes, mapping, hash, token and version', async () => {
    mount()
    await screen.findByText(T.noSource)
    const user = await selectFile()
    await user.clear(screen.getByLabelText(T.column))
    await user.type(screen.getByLabelText(T.column), 'Kod')
    vi.mocked(validatePaintFile).mockResolvedValue({ type: 'ok-single', data: preview({ productCodeColumn: 'Kod' }) })
    await user.click(screen.getByRole('button', { name: T.validate }))
    await screen.findByText(T.ready)
    expect(sendPaintCommand).not.toHaveBeenCalled()
    await user.type(screen.getByLabelText(T.sourceLabel), 'SENTETIK kaynak')
    await user.type(screen.getByLabelText(T.reason), 'SENTETIK gerekçe')
    await user.click(screen.getByRole('button', { name: T.apply }))
    await waitFor(() => expect(sendPaintCommand).toHaveBeenCalledTimes(1))
    expect(validatePaintFile).toHaveBeenCalledWith(COMPANY, { fileName: 'SENTETIK.xlsx', contentBase64: 'AAH/', productCodeColumn: 'Kod' }, SHA, expect.any(AbortSignal))
    expect(sendPaintCommand).toHaveBeenCalledWith(COMPANY, { kind: 'import', expectedProductIds: [PRODUCT], payload: {
      expectedVersion: '0', fileName: 'SENTETIK.xlsx', contentBase64: 'AAH/', productCodeColumn: 'Kod', fileSha256: SHA,
      previewToken: 'synthetic-preview', sourceLabel: 'SENTETIK kaynak', reason: 'SENTETIK gerekçe',
    } }, 'paint-synthetic-key-1')
    expect(screen.getByText(T.absentUnchanged)).toBeTruthy()
  })
  it('refuses apply without mandatory source and reason', async () => {
    mount(); await screen.findByText(T.noSource)
    const user = await readyPreview()
    await user.click(screen.getByRole('button', { name: T.apply }))
    expect(screen.getByText(T.metadataRequired)).toBeTruthy()
    expect(sendPaintCommand).not.toHaveBeenCalled()
  })
  it('shows source row errors and never offers an invalid preview apply', async () => {
    vi.mocked(validatePaintFile).mockResolvedValue({ type: 'ok-single', data: preview({ ready: false, rows: [], errors: [{ rowNumber: 2, field: 'productCode', code: 'invalid', message: 'SENTETIK eşleşmeyen kod' }] }) })
    mount(); await screen.findByText(T.noSource)
    const user = await selectFile()
    await user.click(screen.getByRole('button', { name: T.validate }))
    expect(await screen.findByText(T.rowError(2, 'SENTETIK eşleşmeyen kod'))).toBeTruthy()
    expect((screen.getByRole('button', { name: T.apply }) as HTMLButtonElement).disabled).toBe(true)
  })
  it('discards an accepted preview when the mapped column changes', async () => {
    mount(); await screen.findByText(T.noSource)
    const user = await readyPreview()
    await user.type(screen.getByLabelText(T.column), ' değişti')
    expect(screen.queryByText(T.ready)).toBeNull()
    expect((screen.getByRole('button', { name: T.apply }) as HTMLButtonElement).disabled).toBe(true)
  })
  it('invalidates a preview when a different file is selected', async () => {
    mount(); await screen.findByText(T.noSource)
    const user = await readyPreview()
    await user.upload(screen.getByLabelText(T.file), new File(['SENTETIK different'], 'SENTETIK-2.xlsx'))
    expect(screen.queryByText(T.ready)).toBeNull()
    expect((screen.getByRole('button', { name: T.apply }) as HTMLButtonElement).disabled).toBe(true)
  })
  it('removes only the selected actual member using all three versions', async () => {
    vi.mocked(fetchPaintCatalog).mockResolvedValue({ type: 'ok-single', data: memberCatalog() })
    mount()
    await screen.findByRole('button', { name: T.remove })
    const user = userEvent.setup()
    await user.type(screen.getByLabelText(T.reason), 'SENTETIK çıkarma')
    await user.click(screen.getByRole('button', { name: T.remove }))
    expect(sendPaintCommand).toHaveBeenCalledWith(COMPANY, { kind: 'set', productId: PRODUCT, payload: {
      expectedVersion: '1', expectedProductVersion: '7', expectedMembershipVersion: '1', isMember: false, reason: 'SENTETIK çıkarma',
    } }, 'paint-synthetic-key-1')
  })
  it('adds one real preview row after the initial source exists without a guessed product id', async () => {
    vi.mocked(fetchPaintCatalog).mockResolvedValue({ type: 'ok-single', data: catalog({ outcome: 1, version: '1' }) })
    vi.mocked(validatePaintFile).mockResolvedValue({ type: 'ok-single', data: preview({ version: '1' }) })
    mount(); await screen.findByText(T.empty)
    const user = await readyPreview()
    await user.type(screen.getByLabelText(T.reason), 'SENTETIK ekleme')
    await user.click(screen.getByRole('button', { name: T.add }))
    expect(sendPaintCommand).toHaveBeenCalledWith(COMPANY, { kind: 'set', productId: PRODUCT, payload: {
      expectedVersion: '1', expectedProductVersion: '7', expectedMembershipVersion: '0', isMember: true, reason: 'SENTETIK ekleme',
    } }, 'paint-synthetic-key-1')
  })
  it('keeps an ambiguous write frozen and resends exactly the same snapshot and key', async () => {
    vi.mocked(fetchPaintCatalog).mockResolvedValue({ type: 'ok-single', data: memberCatalog() })
    vi.mocked(sendPaintCommand).mockResolvedValueOnce({ type: 'unknown', reason: 'network-error' })
      .mockResolvedValueOnce({ type: 'applied', data: mutation({ isReplay: true }) })
    mount(); await screen.findByRole('button', { name: T.remove })
    const user = userEvent.setup()
    await user.type(screen.getByLabelText(T.reason), 'SENTETIK çıkarma')
    await user.click(screen.getByRole('button', { name: T.remove }))
    await screen.findByText(T.unknown)
    expect((screen.getByLabelText(T.reason).closest('fieldset') as HTMLFieldSetElement).disabled).toBe(true)
    await user.click(screen.getByRole('button', { name: T.retry }))
    await waitFor(() => expect(sendPaintCommand).toHaveBeenCalledTimes(2))
    expect(vi.mocked(sendPaintCommand).mock.calls[1]).toEqual(vi.mocked(sendPaintCommand).mock.calls[0])
    expect(await screen.findByText(T.replayed)).toBeTruthy()
  })
  it('retains the exact pending command across route unmount and return', async () => {
    vi.mocked(fetchPaintCatalog).mockResolvedValue({ type: 'ok-single', data: memberCatalog() })
    vi.mocked(sendPaintCommand).mockResolvedValueOnce({ type: 'unknown', reason: 'offline' })
    const first = mount(); await screen.findByRole('button', { name: T.remove })
    const user = userEvent.setup()
    await user.type(screen.getByLabelText(T.reason), 'SENTETIK çıkarma')
    await user.click(screen.getByRole('button', { name: T.remove }))
    await screen.findByText(T.unknown)
    first.unmount()
    currentShell = { ...currentShell, routeEntryGeneration: 2 }
    mount()
    await screen.findByText(T.unknown)
    await user.click(screen.getByRole('button', { name: T.retry }))
    await waitFor(() => expect(sendPaintCommand).toHaveBeenCalledTimes(2))
    expect(vi.mocked(sendPaintCommand).mock.calls[1]).toEqual(vi.mocked(sendPaintCommand).mock.calls[0])
  })
  it('does not show a former company preview that resolves after company switch', async () => {
    let resolve!: (outcome: ViewOutcome<PaintPreview>) => void
    vi.mocked(validatePaintFile).mockReturnValue(new Promise((done) => { resolve = done }))
    const page = mount(); await screen.findByText(T.noSource)
    const user = await selectFile()
    await user.click(screen.getByRole('button', { name: T.validate }))
    currentShell = { ...currentShell, committedContextRevision: 2, activeContext: { ...currentShell.activeContext!, companyId: OTHER } }
    vi.mocked(fetchPaintCatalog).mockResolvedValue({ type: 'ok-single', data: catalog({ companyId: OTHER }) })
    page.refresh()
    await act(async () => { resolve({ type: 'ok-single', data: preview() }) })
    expect(screen.queryByText(T.ready)).toBeNull()
    expect(screen.queryByText('SENTETIK boya')).toBeNull()
    expect(sendPaintCommand).not.toHaveBeenCalled()
  })
  it('hides an old member immediately on session reset and discards its pending file', async () => {
    vi.mocked(fetchPaintCatalog).mockResolvedValue({ type: 'ok-single', data: memberCatalog() })
    const page = mount()
    const table = await screen.findByRole('table', { name: T.current })
    expect(within(table).getByText('SENTETIK boya')).toBeTruthy()
    await selectFile()
    currentShell = { ...currentShell, sessionEpoch: 2, sessionInvalid: true, activeContext: null, phase: 'no-active-company' }
    page.refresh()
    expect(screen.queryByText('SENTETIK boya')).toBeNull()
    expect(screen.queryByText(SHA)).toBeNull()
    expect(screen.queryByRole('button', { name: T.apply })).toBeNull()
  })
  it('keeps a late former-company command success out of the new company screen', async () => {
    let resolve!: (value: Awaited<ReturnType<typeof sendPaintCommand>>) => void
    vi.mocked(fetchPaintCatalog).mockResolvedValue({ type: 'ok-single', data: memberCatalog() })
    vi.mocked(sendPaintCommand).mockReturnValue(new Promise((done) => { resolve = done }))
    const page = mount(); await screen.findByRole('button', { name: T.remove })
    const user = userEvent.setup()
    await user.type(screen.getByLabelText(T.reason), 'SENTETIK çıkarma')
    await user.click(screen.getByRole('button', { name: T.remove }))
    currentShell = { ...currentShell, committedContextRevision: 2, activeContext: { ...currentShell.activeContext!, companyId: OTHER } }
    vi.mocked(fetchPaintCatalog).mockResolvedValue({ type: 'ok-single', data: catalog({ companyId: OTHER }) })
    page.refresh()
    await screen.findByText(T.noSource)
    await act(async () => { resolve({ type: 'applied', data: mutation() }) })
    expect(screen.queryByText(T.succeeded)).toBeNull()
    expect(screen.queryByText('SENTETIK boya')).toBeNull()
    expect(screen.queryByText(T.unknown)).toBeNull()
    expect(fetchPaintCatalog).toHaveBeenLastCalledWith(OTHER, expect.any(AbortSignal))
  })
  it('makes a stale table preview unusable until it is validated again', async () => {
    vi.mocked(fetchPaintCatalog).mockResolvedValue({ type: 'ok-single', data: catalog({ outcome: 1, version: '2' }) })
    vi.mocked(validatePaintFile).mockResolvedValue({ type: 'ok-single', data: preview({ version: '1' }) })
    mount(); await screen.findByText(T.empty)
    await readyPreview()
    expect(screen.getByText(T.previewExpired)).toBeTruthy()
    expect((screen.getByRole('button', { name: T.apply }) as HTMLButtonElement).disabled).toBe(true)
    expect((screen.getByRole('button', { name: T.add }) as HTMLButtonElement).disabled).toBe(true)
  })
  it('reports a command 403 through the captured central auth boundary', async () => {
    vi.mocked(fetchPaintCatalog).mockResolvedValue({ type: 'ok-single', data: memberCatalog() })
    vi.mocked(sendPaintCommand).mockResolvedValue({ type: 'refused', status: 403, body: null })
    mount(); await screen.findByRole('button', { name: T.remove })
    const user = userEvent.setup()
    await user.type(screen.getByLabelText(T.reason), 'SENTETIK çıkarma')
    await user.click(screen.getByRole('button', { name: T.remove }))
    await screen.findByText(/Yetkiniz yok/)
    expect(security.report).toHaveBeenCalledWith(403, security.capture.mock.results[0].value)
  })
  it('discards a refused concurrency preview and re-reads current membership', async () => {
    vi.mocked(fetchPaintCatalog).mockResolvedValue({ type: 'ok-single', data: memberCatalog() })
    vi.mocked(sendPaintCommand).mockResolvedValue({ type: 'refused', status: 409, body: null })
    mount(); await screen.findByRole('button', { name: T.remove })
    const user = userEvent.setup()
    await user.type(screen.getByLabelText(T.reason), 'SENTETIK çıkarma')
    await user.click(screen.getByRole('button', { name: T.remove }))
    await screen.findByText(/Çakışma/)
    await waitFor(() => expect(fetchPaintCatalog).toHaveBeenCalledTimes(2))
  })
})
