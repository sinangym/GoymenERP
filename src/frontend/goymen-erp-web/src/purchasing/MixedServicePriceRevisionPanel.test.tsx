import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { MixedServicePriceRevisionPanel, type MixedServicePriceRevisionPanelProps } from './MixedServicePriceRevisionPanel'
import { fetchMixedServicePricePreview, sendMixedServicePriceRevision, type MixedServicePricePreview } from './mixedServicePriceRevisionApi'
import { mixedPriceAnswer, mixedPriceFixture, mixedPriceId as id } from './mixedServicePriceRevisionFixtures.testSupport'
import { MIXED_SERVICE_PRICE_TEXT as T } from './mixedServicePriceRevisionText'
import { initialShellState, type ShellState } from '../shell/shellState'
import { PURCHASING_CAPABILITY } from './capabilities'
import { resetAdjustmentAttemptsForTests } from './useReceiptAdjustmentAttempt'
import type { AdjustmentOutcome } from './receiptAdjustmentApi'
import type { PurchaseOrderCommandResponse } from './contract'
import type { ViewOutcome } from '../shell/uiState'
const auth = vi.hoisted(() => ({ capture: vi.fn(() => ({})), report: vi.fn() }))
let shell: ShellState
vi.mock('../shell/shellContext', () => ({ useShellState: () => shell }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => auth }))
vi.mock('./mixedServicePriceRevisionApi', async original => ({ ...await original<typeof import('./mixedServicePriceRevisionApi')>(),
  fetchMixedServicePricePreview: vi.fn(), sendMixedServicePriceRevision: vi.fn() }))
const clients: QueryClient[] = []
function props(): MixedServicePriceRevisionPanelProps { return { fenceKey: 'SENTETIK-context-1', companyId: id(1), actorId: 'SENTETIK-actor', sessionEpoch: 1,
  order: mixedPriceFixture().parent.order, canEdit: true, onSettled: vi.fn(async () => undefined) } }
function mount(initial = props()) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0, refetchOnWindowFocus: false } } }); clients.push(client)
  const content = (value: MixedServicePriceRevisionPanelProps) => <QueryClientProvider client={client}><MixedServicePriceRevisionPanel {...value} /></QueryClientProvider>
  const view = render(content(initial)); return { ...view, client, refresh: (value: MixedServicePriceRevisionPanelProps) => view.rerender(content(value)) }
}
async function prepare() {
  fireEvent.click(screen.getByRole('button', { name: T.open })); await screen.findByRole('article', { name: mixedPriceFixture().services[0].service.documentNumber })
  fireEvent.change(screen.getByLabelText(`${mixedPriceFixture().services[0].service.documentNumber} — SENTETIK hizmet — ${T.revised}`), { target: { value: '0' } })
  fireEvent.change(screen.getByLabelText(T.reason), { target: { value: 'SENTETIK native reason' } })
}
beforeEach(() => {
  vi.clearAllMocks(); resetAdjustmentAttemptsForTests(); sessionStorage.clear()
  shell = { ...initialShellState, phase: 'idle', sessionEpoch: 1, committedContextRevision: 1,
    activeContext: { userId: 'SENTETIK-actor', companyId: id(1), scopeMode: 'single', capabilityGeneration: 'g1', capabilities: [PURCHASING_CAPABILITY.orderSupplierRevise] } }
  vi.mocked(fetchMixedServicePricePreview).mockResolvedValue({ type: 'ok-single', data: mixedPriceFixture() })
  vi.mocked(sendMixedServicePriceRevision).mockResolvedValue({ type: 'unknown', reason: 'network-error' })
})
afterEach(() => { cleanup(); clients.splice(0).forEach(x => x.clear()); resetAdjustmentAttemptsForTests(); vi.restoreAllMocks() })
describe('mixed price panel controlled DOM and native attempt protocol (NOT_BROWSER)', () => {
  it('opens the genuine Approved preview and sends current exact full service selection with native zero and reason', async () => {
    const p = props(); mount(p); expect(fetchMixedServicePricePreview).not.toHaveBeenCalled(); await prepare()
    expect(screen.getByText(T.note)).toBeTruthy(); expect(screen.getByText('10')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: T.save })); await screen.findByText(T.unknown)
    expect(vi.mocked(sendMixedServicePriceRevision).mock.calls[0].slice(0, 3)).toEqual([id(1), id(10), { expectedVersion: '4',
      services: mixedPriceFixture().services.map(x => x.selection), changes: [{ serviceOrderId: id(2), lines: [{ lineId: id(6), unitPrice: '0' }] }], reason: 'SENTETIK native reason' }])
    expect(screen.queryByRole('article')).toBeNull(); expect(sessionStorage.length).toBe(0)
  })
  it('restores unknown across a genuine remount and later parent version without GET or replacement body/key', async () => {
    const p = props(), view = mount(p); await prepare(); fireEvent.click(screen.getByRole('button', { name: T.save })); await screen.findByText(T.unknown)
    const first = vi.mocked(sendMixedServicePriceRevision).mock.calls[0]; view.unmount()
    const later = { ...p, fenceKey: 'SENTETIK-context-2', order: { ...p.order, version: '6' } }
    mount(later); expect(await screen.findByRole('button', { name: T.retry })).toBeTruthy()
    expect(fetchMixedServicePricePreview).toHaveBeenCalledTimes(1); expect(screen.queryByRole('article')).toBeNull()
    vi.mocked(sendMixedServicePriceRevision).mockResolvedValue({ type: 'applied', response: mixedPriceAnswer() })
    fireEvent.click(screen.getByRole('button', { name: T.retry })); await screen.findByText(T.saved)
    expect(vi.mocked(sendMixedServicePriceRevision).mock.calls[1]).toEqual(first); expect(fetchMixedServicePricePreview).toHaveBeenCalledTimes(1)
    expect(p.onSettled).toHaveBeenCalledTimes(1)
  })
  it('suppresses cached late human facts while pending and never needs a new human read to retry', async () => {
    const p = props(), view = mount(p); await prepare(); fireEvent.click(screen.getByRole('button', { name: T.save })); await screen.findByText(T.unknown)
    const latest = mixedPriceFixture(); const changed = { ...latest, services: latest.services.map(x => ({ ...x, preparedBy: 'SENTETIK late other maker',
      service: { ...x.service, documentNumber: 'SENTETIK-LATE-OTHER-RECORD' } })) }
    act(() => { view.client.setQueryData([p.fenceKey, 'mixed-service-price-preview', p.order.id, p.order.version], { type: 'ok-single', data: changed }) })
    expect(screen.queryByText('SENTETIK late other maker')).toBeNull(); expect(screen.queryByText('SENTETIK-LATE-OTHER-RECORD')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: T.retry })); await waitFor(() => expect(sendMixedServicePriceRevision).toHaveBeenCalledTimes(2))
    expect(fetchMixedServicePricePreview).toHaveBeenCalledTimes(1)
  })
  it.each([401, 403])('keeps even the first ambiguous %s frozen and reports the real auth boundary', async status => {
    vi.mocked(sendMixedServicePriceRevision).mockResolvedValue({ type: 'unknown', reason: 'service-unavailable', status })
    mount(); await prepare(); fireEvent.click(screen.getByRole('button', { name: T.save })); await screen.findByText(T.unknown)
    expect(auth.report).toHaveBeenCalledWith(status, expect.anything()); const first = vi.mocked(sendMixedServicePriceRevision).mock.calls[0]
    fireEvent.click(screen.getByRole('button', { name: T.retry })); await waitFor(() => expect(sendMixedServicePriceRevision).toHaveBeenCalledTimes(2))
    expect(vi.mocked(sendMixedServicePriceRevision).mock.calls[1]).toEqual(first)
  })
  it.each(['capability', 'baseline'])('absent %s authority produces no GET, editor, or POST', fault => {
    const p = props(); if (fault === 'capability') shell = { ...shell, activeContext: { ...shell.activeContext!, capabilities: [] } }
    mount(fault === 'baseline' ? { ...p, canEdit: false } : p)
    expect(screen.getByText(T.denied)).toBeTruthy(); expect(screen.queryByRole('button', { name: T.open })).toBeNull()
    expect(fetchMixedServicePricePreview).not.toHaveBeenCalled(); expect(sendMixedServicePriceRevision).not.toHaveBeenCalled()
  })
  it.each(['actor', 'company', 'session'])('does not expose or resend the previous unknown attempt after %s changes', async fault => {
    const p = props(), view = mount(p); await prepare(); fireEvent.click(screen.getByRole('button', { name: T.save })); await screen.findByText(T.unknown)
    const next = { ...p, fenceKey: 'SENTETIK-new-context', actorId: fault === 'actor' ? 'SENTETIK-other' : p.actorId,
      companyId: fault === 'company' ? id(99) : p.companyId, sessionEpoch: fault === 'session' ? 2 : p.sessionEpoch }
    shell = { ...shell, sessionEpoch: next.sessionEpoch, activeContext: { ...shell.activeContext!, companyId: next.companyId, userId: next.actorId! } }
    view.refresh(next); expect(screen.queryByRole('button', { name: T.retry })).toBeNull(); expect(sendMixedServicePriceRevision).toHaveBeenCalledTimes(1)
  })
  it.each(['actor', 'company', 'session'])('hides a genuinely in-flight human GET after %s changes', async fault => {
    let finish: ((value: ViewOutcome<MixedServicePricePreview>) => void) | undefined
    vi.mocked(fetchMixedServicePricePreview).mockImplementation(() => new Promise(resolve => { finish = resolve }))
    const p = props(), view = mount(p)
    fireEvent.click(screen.getByRole('button', { name: T.open }))
    await waitFor(() => expect(fetchMixedServicePricePreview).toHaveBeenCalledTimes(1))
    const next = { ...p, fenceKey: 'SENTETIK-later-context', actorId: fault === 'actor' ? 'SENTETIK-other' : p.actorId,
      companyId: fault === 'company' ? id(99) : p.companyId, sessionEpoch: fault === 'session' ? 2 : p.sessionEpoch }
    shell = { ...shell, sessionEpoch: next.sessionEpoch, activeContext: { ...shell.activeContext!, companyId: next.companyId, userId: next.actorId! } }
    view.refresh(next)
    await act(async () => { finish?.({ type: 'ok-single', data: mixedPriceFixture() }) })
    expect(screen.queryByRole('article')).toBeNull(); expect(screen.queryByRole('button', { name: T.save })).toBeNull()
    expect(sendMixedServicePriceRevision).not.toHaveBeenCalled()
  })
  it('drops a late POST completion after company switch without announcing or refreshing the discarded company', async () => {
    let finish: ((value: AdjustmentOutcome<PurchaseOrderCommandResponse>) => void) | undefined
    vi.mocked(sendMixedServicePriceRevision).mockImplementation(() => new Promise(resolve => { finish = resolve }))
    const p = props(), view = mount(p); await prepare(); fireEvent.click(screen.getByRole('button', { name: T.save })); await screen.findByText(T.busy)
    shell = { ...shell, activeContext: { ...shell.activeContext!, companyId: id(99) } }
    view.refresh({ ...p, companyId: id(99), fenceKey: 'SENTETIK-other-company' })
    await act(async () => { finish?.({ type: 'applied', response: mixedPriceAnswer() }) })
    expect(p.onSettled).not.toHaveBeenCalled(); expect(screen.queryByText(T.saved)).toBeNull()
  })
  it('clears editable prices and reason after409 before using a fresh child version under the same Approved parent4', async () => {
    const original = mixedPriceFixture(), later: MixedServicePricePreview = { ...original, services: original.services.map(entry => ({ ...entry,
      updatedAtUtc: '2026-11-28T11:00:00Z', priceRevision: 1, service: { ...entry.service, version: '3' }, selection: { ...entry.selection, expectedVersion: '3' } })) }
    vi.mocked(fetchMixedServicePricePreview).mockResolvedValueOnce({ type: 'ok-single', data: original }).mockResolvedValue({ type: 'ok-single', data: later })
    vi.mocked(sendMixedServicePriceRevision).mockResolvedValueOnce({ type: 'failed', status: 409, isReplay: false, code: 'SENTETIK-stale' })
      .mockResolvedValue({ type: 'unknown', reason: 'network-error' })
    mount(); await prepare(); fireEvent.click(screen.getByRole('button', { name: T.save }))
    await waitFor(() => expect(fetchMixedServicePricePreview).toHaveBeenCalledTimes(2))
    const label = `${later.services[0].service.documentNumber} — SENTETIK hizmet — ${T.revised}`
    await waitFor(() => expect((screen.getByLabelText(label) as HTMLInputElement).value).toBe(''))
    expect((screen.getByLabelText(T.reason) as HTMLTextAreaElement).value).toBe('')
    fireEvent.click(screen.getByRole('button', { name: T.save })); expect(sendMixedServicePriceRevision).toHaveBeenCalledTimes(1)
    fireEvent.change(screen.getByLabelText(label), { target: { value: '2' } }); fireEvent.change(screen.getByLabelText(T.reason), { target: { value: 'SENTETIK newly prepared reason' } })
    fireEvent.click(screen.getByRole('button', { name: T.save })); await screen.findByText(T.unknown)
    const calls = vi.mocked(sendMixedServicePriceRevision).mock.calls
    expect(calls[1][2]).toEqual({ expectedVersion: '4', services: later.services.map(x => x.selection), changes: [{ serviceOrderId: id(2), lines: [{ lineId: id(6), unitPrice: '2' }] }], reason: 'SENTETIK newly prepared reason' })
    expect(calls[1][3]).not.toBe(calls[0][3])
  })
  it.each(['version', 'facts'])('does not reuse prices or reason after close/open when fresh child %s changes without a parent version change', async fault => {
    const original = mixedPriceFixture(), later: MixedServicePricePreview = { ...original, services: original.services.map(entry => ({ ...entry,
      service: { ...entry.service, version: fault === 'version' ? '3' : entry.service.version,
        lines: entry.service.lines.map(line => ({ ...line, serviceName: fault === 'facts' ? 'SENTETIK newly viewed service' : line.serviceName })) },
      selection: { ...entry.selection, expectedVersion: fault === 'version' ? '3' : entry.selection.expectedVersion } })) }
    vi.mocked(fetchMixedServicePricePreview).mockResolvedValueOnce({ type: 'ok-single', data: original }).mockResolvedValue({ type: 'ok-single', data: later })
    mount(); await prepare(); fireEvent.click(screen.getByRole('button', { name: T.close })); expect(screen.queryByRole('article')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: T.open })); await waitFor(() => expect(fetchMixedServicePricePreview).toHaveBeenCalledTimes(2))
    const label = `${later.services[0].service.documentNumber} — ${later.services[0].service.lines[0].serviceName} — ${T.revised}`
    await waitFor(() => expect((screen.getByLabelText(label) as HTMLInputElement).value).toBe(''))
    expect((screen.getByLabelText(T.reason) as HTMLTextAreaElement).value).toBe('')
    fireEvent.click(screen.getByRole('button', { name: T.save })); expect(sendMixedServicePriceRevision).not.toHaveBeenCalled()
  })
  it('clears an open editable intent when complete human facts change in the cache with the same identities and versions', async () => {
    const p = props(), view = mount(p); await prepare(); const original = mixedPriceFixture()
    const later: MixedServicePricePreview = { ...original, services: original.services.map(entry => ({ ...entry,
      service: { ...entry.service, lines: entry.service.lines.map(line => ({ ...line, serviceName: 'SENTETIK refreshed human service' })) } })) }
    act(() => view.client.setQueryData([p.fenceKey, 'mixed-service-price-preview', p.order.id, p.order.version], { type: 'ok-single', data: later }))
    expect((await screen.findByLabelText(`${later.services[0].service.documentNumber} — SENTETIK refreshed human service — ${T.revised}`) as HTMLInputElement).value).toBe('')
    expect((screen.getByLabelText(T.reason) as HTMLTextAreaElement).value).toBe('')
    fireEvent.click(screen.getByRole('button', { name: T.save })); expect(sendMixedServicePriceRevision).not.toHaveBeenCalled()
  })
  it('clears an editable intent when its fence changes even if the full human preview and parent version are unchanged', async () => {
    const p = props(), view = mount(p); await prepare(); view.refresh({ ...p, fenceKey: 'SENTETIK-fresh-read-fence' })
    await waitFor(() => expect(fetchMixedServicePricePreview).toHaveBeenCalledTimes(2))
    await waitFor(() => expect((screen.getByLabelText(T.reason) as HTMLTextAreaElement).value).toBe(''))
    expect((screen.getByLabelText(`${mixedPriceFixture().services[0].service.documentNumber} — SENTETIK hizmet — ${T.revised}`) as HTMLInputElement).value).toBe('')
    expect(sendMixedServicePriceRevision).not.toHaveBeenCalled()
  })
  it('keeps an unknown attempt frozen through close/open and late cached new child versions instead of restoring an editable form', async () => {
    const p = props(), view = mount(p); await prepare(); fireEvent.click(screen.getByRole('button', { name: T.save })); await screen.findByText(T.unknown)
    const first = vi.mocked(sendMixedServicePriceRevision).mock.calls[0], original = mixedPriceFixture()
    const later: MixedServicePricePreview = { ...original, services: original.services.map(entry => ({ ...entry, service: { ...entry.service, version: '3' }, selection: { ...entry.selection, expectedVersion: '3' } })) }
    act(() => view.client.setQueryData([p.fenceKey, 'mixed-service-price-preview', p.order.id, p.order.version], { type: 'ok-single', data: later }))
    fireEvent.click(screen.getByRole('button', { name: T.close })); fireEvent.click(screen.getByRole('button', { name: T.open }))
    expect(screen.queryByRole('article')).toBeNull(); expect(screen.queryByLabelText(T.reason)).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: T.retry })); await waitFor(() => expect(sendMixedServicePriceRevision).toHaveBeenCalledTimes(2))
    expect(vi.mocked(sendMixedServicePriceRevision).mock.calls[1]).toEqual(first); expect(fetchMixedServicePricePreview).toHaveBeenCalledTimes(1)
  })
  it.each(['malformed', 'forbidden', 'unavailable', 'stale'])('keeps %s preview outside a new command form', async fault => {
    vi.mocked(fetchMixedServicePricePreview).mockResolvedValue(fault === 'malformed' ? { type: 'malformed' } : { type: 'http', status: fault === 'forbidden' ? 403 : fault === 'stale' ? 409 : 503 })
    mount(); fireEvent.click(screen.getByRole('button', { name: T.open })); await waitFor(() => expect(fetchMixedServicePricePreview).toHaveBeenCalledTimes(1))
    expect(screen.queryByRole('button', { name: T.save })).toBeNull(); expect(sendMixedServicePriceRevision).not.toHaveBeenCalled()
  })
})
