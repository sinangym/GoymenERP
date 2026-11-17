import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { initialShellState, type ShellState } from '../shell/shellState'
import { DispatchKilogramsPanel } from './DispatchKilogramsPanel'
import { decodeDispatchKilograms, type DispatchKilograms, type RecordKilograms } from './dispatchKilogramsContract'
import { sendDispatchKilograms } from './dispatchKilogramsApi'
import { DISPATCH_KG_TEXT as T } from './dispatchKilogramsText'
import type { DispatchNote } from './contract'
const boundary = vi.hoisted(() => ({ capture: vi.fn(() => ({ synthetic: true })), report: vi.fn() }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => boundary }))
const company = '20110000-0000-4000-8000-000000000001', other = '20110000-0000-4000-8000-000000000002'
const noteId = '20110000-0000-4000-8000-000000000003', product = '20110000-0000-4000-8000-000000000004'
const shipment = '20110000-0000-4000-8000-000000000005', recordId = '20110000-0000-4000-8000-000000000006'
const instruction = '20110000-0000-4000-8000-000000000007'
const shell = (companyId = company): ShellState => ({ ...initialShellState, phase: 'idle', activeContext: {
  userId: 'synthetic-operator', scopeMode: 'single', companyId, capabilityGeneration: 'synthetic', capabilities: [],
} })
const note: DispatchNote = { dispatchNoteId: noteId, status: 'Draft', number: null, createdBy: 'synthetic', createdAtUtc: '2026-11-17T00:00:00Z',
  approvedBy: null, approvedAtUtc: null, cancelledBy: null, cancelledAtUtc: null, cancelReason: null, eDispatchNumber: null,
  lines: [{ lineNo: 1, shipmentId: shipment, instructionLineId: instruction, sourceOrderId: instruction, sourceOrderLineId: instruction, productId: product, unitId: instruction, quantity: '3' }] }
const value = (): DispatchKilograms => ({ companyId: company, recordId, dispatchNoteId: noteId, instructionId: instruction,
  theoreticalKg: '3', scaleKg: '3.001', differenceKg: '0.001', totalExcessKg: '0', bandLevel: 'NotEvaluated', warningBandPercent: null,
  approvalBandPercent: null, blockBandPercent: null, excessRatioPercent: '0', approvalRatePercent: null, decision: 'NoExcess', status: 'Recorded', version: 1,
  products: [{ productId: product, noteKg: '3', scaleKg: '3.001', differenceKg: '0.001', remainingPhysicalKg: '1', physicallyCoveredKg: '0.001', excessKg: '0', targetLineNo: null }],
  enteredBy: ['synthetic'], enteredByComplete: true, recordedBy: 'synthetic', recordedAtUtc: '2026-11-17T00:00:00Z', approvedBy: null, approvedAtUtc: null,
  kilogramAllocation: { lines: [{ lineNo: 1, shipmentId: shipment, productId: product, theoreticalKg: '3', kilograms: '3.001' }], finalizedDocument: null } })
const json = (body: unknown, status = 200, headers: Record<string, string> = {}) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } })
function mount(state = shell()) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0, gcTime: 0 } } })
  return render(<QueryClientProvider client={client}><DispatchKilogramsPanel shell={state} note={note} companyId={state.activeContext!.companyId!} /></QueryClientProvider>)
}
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })

describe('Dispatch kilogram source contract and screen', () => {
  it('preserves decimal text and rejects a foreign company, missing proof field, duplicate source and floating point kg', () => {
    expect(decodeDispatchKilograms(value(), company, noteId)).toEqual(value())
    expect(decodeDispatchKilograms(value(), other, noteId)).toBeNull()
    expect(decodeDispatchKilograms({ ...value(), scaleKg: 3.001 }, company, noteId)).toBeNull()
    expect(decodeDispatchKilograms({ ...value(), unexpected: true }, company, noteId)).toBeNull()
    expect(decodeDispatchKilograms({ ...value(), kilogramAllocation: undefined }, company, noteId)).toBeNull()
    const line = value().kilogramAllocation!.lines[0]
    expect(decodeDispatchKilograms({ ...value(), kilogramAllocation: { lines: [line, line], finalizedDocument: null } }, company, noteId)).toBeNull()
  })
  it.each([[401, T.unauthenticated], [403, T.forbidden], [404, T.empty], [409, T.conflict], [503, T.unavailable]])('distinguishes read status %s', async (status, text) => {
    vi.stubGlobal('fetch', vi.fn(async () => json({}, Number(status))))
    mount(); expect(await screen.findByText(String(text))).not.toBeNull()
    if (status === 401 || status === 403) expect(boundary.report).toHaveBeenCalledWith(status, { synthetic: true })
    if (status !== 404) expect(screen.queryByRole('button', { name: T.save })).toBeNull()
  })
  it('refuses malformed success instead of rendering a partial record', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json({ ...value(), products: undefined })))
    mount(); expect(await screen.findByText(T.malformed)).not.toBeNull()
    expect(screen.queryByText(T.recorded)).toBeNull()
  })
  it('shows offline distinctly from a bad response', async () => {
    vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false)
    vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('synthetic offline') }))
    mount(); expect(await screen.findByText(T.offline)).not.toBeNull()
  })
  it('sends the exact text once and keeps the same frozen command and key when the answer is lost', async () => {
    const posts: RequestInit[] = []
    vi.stubGlobal('fetch', vi.fn(async (_url: unknown, init?: RequestInit) => {
      if (init?.method !== 'POST') return json({}, 404)
      posts.push(init)
      if (posts.length === 1) throw new TypeError('synthetic lost response')
      const payload = JSON.parse(String(init.body)) as RecordKilograms
      return json({ record: { ...value(), recordId: payload.recordId }, isReplay: true, originalCorrelationId: 'synthetic-original' }, 200,
        { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'synthetic-original' })
    }))
    mount(); const user = userEvent.setup()
    await user.type(await screen.findByLabelText(`${T.scale} — 1`), '3.001')
    await user.click(screen.getByRole('button', { name: T.save }))
    expect(await screen.findByText(T.unknown)).not.toBeNull()
    expect(screen.getByRole('button', { name: T.save }).matches(':disabled')).toBe(true)
    await user.click(screen.getByRole('button', { name: T.retryCommand }))
    expect(await screen.findByText(T.replayed)).not.toBeNull()
    expect(posts).toHaveLength(2); expect(posts[1].body).toBe(posts[0].body)
    expect(new Headers(posts[1].headers).get('Idempotency-Key')).toBe(new Headers(posts[0].headers).get('Idempotency-Key'))
    expect(JSON.parse(String(posts[0].body)).products[0].scaleKg).toBe('3.001')
  })
  it('drops a late company response after unmount', async () => {
    let release!: (value: Response) => void
    vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(resolve => { release = resolve })))
    const panel = mount(); panel.unmount(); release(json(value()))
    await waitFor(() => expect(screen.queryByText(T.recorded)).toBeNull())
  })
  it('keeps a mismatched replay receipt or a different record as an unknown transport answer', async () => {
    const payload: RecordKilograms = { recordId, dispatchNoteId: noteId, products: [{ productId: product, scaleKg: '3.001' }], finalizedDocument: null }
    vi.stubGlobal('fetch', vi.fn(async () => json({ record: value(), isReplay: true, originalCorrelationId: 'synthetic' })))
    const result = await sendDispatchKilograms({ binding: 'synthetic', companyId: company, path: '/logistics/weighing-differences', key: 'synthetic', payload }, noteId)
    expect(result.type).toBe('malformed')
  })
  it('requires the exact selected document version and declared per-line kg in a success receipt', async () => {
    const proof = { ettn: other, kind: 'ElectronicDispatch' as const, documentId: instruction, documentVersion: 2, sha256: 'a'.repeat(64) }
    const payload: RecordKilograms = { recordId, dispatchNoteId: noteId, products: [], finalizedDocument: {
      ettn: proof.ettn, kind: proof.kind, documentId: proof.documentId, expectedDocumentVersion: 2, expectedDocumentSha256: proof.sha256,
      lines: [{ lineNo: 1, shipmentId: shipment, productId: product, kilograms: '3.001' }],
    } }
    const snapshot = { binding: 'synthetic', companyId: company, path: '/logistics/weighing-differences', key: 'synthetic-external', payload }
    const record = { ...value(), kilogramAllocation: { ...value().kilogramAllocation!, finalizedDocument: proof } }
    vi.stubGlobal('fetch', vi.fn(async () => json({ record, isReplay: false, originalCorrelationId: 'synthetic' })))
    expect((await sendDispatchKilograms(snapshot, noteId)).type).toBe('succeeded')
    vi.stubGlobal('fetch', vi.fn(async () => json({ record: { ...record, kilogramAllocation: { ...record.kilogramAllocation,
      finalizedDocument: { ...proof, documentVersion: 3 } } }, isReplay: false, originalCorrelationId: 'synthetic' })))
    expect((await sendDispatchKilograms(snapshot, noteId)).type).toBe('malformed')
    vi.stubGlobal('fetch', vi.fn(async () => json({ record: { ...record, kilogramAllocation: { ...record.kilogramAllocation,
      lines: [{ ...record.kilogramAllocation.lines[0], kilograms: '3.002' }] } }, isReplay: false, originalCorrelationId: 'synthetic' })))
    expect((await sendDispatchKilograms(snapshot, noteId)).type).toBe('malformed')
  })
})
