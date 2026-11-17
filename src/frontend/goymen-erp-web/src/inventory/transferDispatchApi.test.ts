/** Synthetic wire/retained-command source tests only. SQL/browser acceptance is NOT_RUN. */
import { afterEach, describe, expect, it, vi } from 'vitest'
import { initialShellState } from '../shell/shellState'
import type { TransferDetail } from './transferContract'
import { beginTransferDispatchAttempt, decodeTransferOutgoingDocument, decodeTransferOutgoingRead, fetchTransferOutgoing,
  finishTransferDispatchAttempt, leaveTransferDispatchAttempt, resetTransferDispatchAttemptsForTests, sendTransferDispatch,
  transferDispatchAttempt, transferDispatchBinding, transferDispatchFrozen, type TransferDispatchCommand, type TransferOutgoingDocument } from './transferDispatchApi'

const COMPANY = '11111111-1111-4111-8111-111111111111'
const TRANSFER = '22222222-2222-4222-8222-222222222222'
const LOCATION = '33333333-3333-4333-8333-333333333333'
const OTHER = '44444444-4444-4444-8444-444444444444'
const DOCUMENT = '55555555-5555-4555-8555-555555555555'
const LINE = '66666666-6666-4666-8666-666666666666'
const LINE2 = '77777777-7777-4777-8777-777777777777'
const LABEL = '88888888-8888-4888-8888-888888888888'
const LABEL2 = '99999999-9999-4999-8999-999999999999'
const ACTOR = 'SENTETIK-actor'
const AT = '2026-11-17T09:00:00Z'
const transfer = (): TransferDetail => ({ companyId: COMPANY, transferId: TRANSFER, version: '1', status: 'Planned', sourceLocationId: LOCATION,
  destinationLocationId: OTHER, sourceCustodianKind: 'Company', sourceCustodianId: COMPANY, destinationCustodianKind: 'Company', destinationCustodianId: COMPANY,
  plannedAtUtc: '2026-11-17T08:00:00Z', dispatchedAtUtc: null, lines: [
    { lineId: LINE, labelId: LABEL, expectedLabelVersion: '3', plannedKg: '5', actualShippedKg: null, differenceKg: null, status: 'Planned' },
    { lineId: LINE2, labelId: LABEL2, expectedLabelVersion: '4', plannedKg: '7', actualShippedKg: null, differenceKg: null, status: 'Planned' },
  ] })
const draft = (): TransferOutgoingDocument => ({ companyId: COMPANY, documentId: DOCUMENT, transferId: TRANSFER, orderedTransferLineIds: [LINE, LINE2],
  eDispatchNumber: null, status: 'Draft', version: 1, createdBy: ACTOR, createdAtUtc: AT, effectiveBy: null, effectiveAtUtc: null,
  cancelledBy: null, cancelledAtUtc: null, cancellationReason: null })
const effective = (): TransferOutgoingDocument => ({ ...draft(), status: 'Effective', version: 2, effectiveBy: ACTOR, effectiveAtUtc: '2026-11-17T09:05:00Z' })
const command = (kind: TransferDispatchCommand['kind']): TransferDispatchCommand => kind === 'create'
  ? { kind, payload: { documentId: DOCUMENT, transferId: TRANSFER, orderedTransferLineIds: [LINE, LINE2], eDispatchNumber: null, eDispatchAssociation: 1 } }
  : kind === 'dispatch' ? { kind, document: effective(), payload: { expectedTransferVersion: '1', dispatchedAtUtc: '2026-11-17T10:00:00Z', outgoingDocumentId: DOCUMENT, expectedOutgoingDocumentVersion: '2' } }
    : kind === 'finalize' ? { kind, document: draft(), payload: { expectedVersion: 1 } }
      : { kind, document: effective(), payload: { expectedVersion: 2, reason: 'SENTETIK belge iptali' } }
const snapshot = (kind: TransferDispatchCommand['kind']) => beginTransferDispatchAttempt('synthetic-binding', ACTOR, transfer(), command(kind), () => 'synthetic-key')!
const dispatched = () => ({ ...transfer(), version: '2', status: 'InTransit', dispatchedAtUtc: '2026-11-17T10:00:00Z',
  lines: transfer().lines.map((line) => ({ ...line, status: 'InTransit', actualShippedKg: line.plannedKg, differenceKg: '0' })) })
const cancelled = (): TransferOutgoingDocument => ({ ...effective(), version: 3, status: 'Cancelled', cancelledBy: ACTOR,
  cancelledAtUtc: '2026-11-17T09:10:00Z', cancellationReason: 'SENTETIK belge iptali' })
const body = (document: unknown, replay = false) => ({ document, isReplay: replay, originalCorrelationId: 'synthetic-original' })
const json = (raw: unknown, status = 200, headers: Record<string, string> = {}) => new Response(JSON.stringify(raw), { status, headers })
const replayHeaders = { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'synthetic-original' }
afterEach(() => { resetTransferDispatchAttemptsForTests(); vi.unstubAllGlobals() })

describe('actual transfer outgoing-document binding and lifecycle', () => {
  it('decodes each genuine lifecycle and distinguishes legacy association from explicit not-linked', () => {
    for (const document of [draft(), effective(), cancelled()]) expect(decodeTransferOutgoingDocument(document, transfer())).toEqual(document)
    expect(decodeTransferOutgoingRead({ document: draft(), association: 0, departureVersion: null }, transfer())?.association).toBe(0)
    expect(decodeTransferOutgoingRead({ document: draft(), association: 1, departureVersion: 1 }, transfer())?.association).toBe(1)
    expect(decodeTransferOutgoingRead({ document: draft(), association: 2, departureVersion: 1 }, transfer())?.association).toBe(2)
  })
  it.each([
    { ...draft(), companyId: OTHER }, { ...draft(), transferId: OTHER }, { ...draft(), documentId: 'bad' },
    { ...draft(), orderedTransferLineIds: [LINE2, LINE] }, { ...draft(), orderedTransferLineIds: [LINE, LINE] },
    { ...draft(), orderedTransferLineIds: [LINE] }, { ...draft(), orderedTransferLineIds: [LINE, LINE2, OTHER] },
    { ...draft(), version: '1' }, { ...draft(), version: 2 }, { ...draft(), status: 'Effective' },
    { ...effective(), effectiveBy: null }, { ...draft(), cancelledBy: ACTOR }, { ...cancelled(), cancellationReason: null },
    { ...draft(), createdAtUtc: '2026-02-31T09:00:00Z' }, { ...draft(), price: '10' },
  ])('rejects foreign reordered incomplete malformed or extended source %#', (raw) => {
    expect(decodeTransferOutgoingDocument(raw, transfer())).toBeNull()
  })
  it.each([
    { document: draft(), association: 0, departureVersion: 1 }, { document: draft(), association: 1, departureVersion: null },
    { document: draft(), association: 1, departureVersion: '1' }, { document: draft(), association: 2, departureVersion: 0 },
    { document: draft(), association: 3, departureVersion: 1 }, { document: { ...draft(), eDispatchNumber: 'SENTETIK-NUMBER' }, association: 1, departureVersion: 1 },
  ])('refuses contradictory association evidence rather than deriving not-linked from a null number %#', (raw) => {
    expect(decodeTransferOutgoingRead(raw, transfer())).toBeNull()
  })
  it('reads the real selected transfer without a GUID input and preserves 404 absence as a distinct outcome', async () => {
    const fetcher = vi.fn(async (_url: RequestInfo | URL, _init?: RequestInit) => json({}, 404)); vi.stubGlobal('fetch', fetcher)
    expect(await fetchTransferOutgoing(transfer())).toMatchObject({ type: 'http', status: 404 })
    expect(fetcher.mock.calls[0][0]).toBe(`/api/v1/logistics/transfer-outgoing-documents/by-transfer/${TRANSFER}`)
    expect(new Headers(fetcher.mock.calls[0][1]?.headers).get('X-Company-Id')).toBe(COMPANY)
  })
  it('rejects an empty or duplicate line source before connection and a late aborted read after await', async () => {
    const fetcher = vi.fn(async () => json({ document: draft(), association: 1, departureVersion: 1 })); vi.stubGlobal('fetch', fetcher)
    expect(await fetchTransferOutgoing({ ...transfer(), lines: [] })).toEqual({ type: 'malformed' }); expect(fetcher).not.toHaveBeenCalled()
    const controller = new AbortController(); fetcher.mockImplementation(async () => { controller.abort(); return json({ document: draft(), association: 1, departureVersion: 1 }) })
    expect(await fetchTransferOutgoing(transfer(), controller.signal)).toEqual({ type: 'fence-stale' })
  })
})

describe('separate explicit document and dispatch commands', () => {
  it.each(['create', 'finalize', 'cancel', 'dispatch'] as const)('sends only the frozen %s owner body and its own key', async (kind) => {
    const expected = kind === 'create' ? body(draft()) : kind === 'finalize' ? body(effective()) : kind === 'cancel' ? body(cancelled()) : dispatched()
    const status = kind === 'create' || kind === 'dispatch' ? 201 : 200
    const fetcher = vi.fn(async (_url: RequestInfo | URL, _init?: RequestInit) => json(expected, status)); vi.stubGlobal('fetch', fetcher)
    const request = snapshot(kind); expect(await sendTransferDispatch(request)).toEqual({ type: 'applied', replayed: false })
    const [url, init] = fetcher.mock.calls[0]
    expect(url).toBe(kind === 'create' ? '/api/v1/logistics/transfer-outgoing-documents' : kind === 'dispatch'
      ? `/api/v1/inventory/transfers/${TRANSFER}/dispatch` : `/api/v1/logistics/transfer-outgoing-documents/${DOCUMENT}/${kind}`)
    expect(JSON.parse(String(init?.body))).toEqual(request.command.payload)
    expect(new Headers(init?.headers).get('Idempotency-Key')).toBe('synthetic-key')
    expect(new Headers(init?.headers).get('X-Company-Id')).toBe(COMPANY)
    expect(JSON.parse(String(init?.body))).not.toHaveProperty('actualKg')
  })
  it.each(['create', 'finalize', 'cancel', 'dispatch'] as const)('accepts only a coherent positive original %s replay', async (kind) => {
    const raw = kind === 'create' ? body(draft(), true) : kind === 'finalize' ? body(effective(), true) : kind === 'cancel' ? body(cancelled(), true) : dispatched()
    vi.stubGlobal('fetch', vi.fn(async () => json(raw, kind === 'create' ? 201 : 200, replayHeaders)))
    expect(await sendTransferDispatch(snapshot(kind))).toEqual({ type: 'applied', replayed: true })
  })
  it.each([
    { ...draft(), companyId: OTHER }, { ...draft(), documentId: OTHER }, { ...draft(), transferId: OTHER },
    { ...draft(), orderedTransferLineIds: [LINE2, LINE] }, { ...draft(), createdBy: 'SENTETIK-other' }, effective(),
  ])('retains a wrong created document as unknown %#', async (document) => {
    vi.stubGlobal('fetch', vi.fn(async () => json(body(document), 201)))
    expect(await sendTransferDispatch(snapshot('create'))).toEqual({ type: 'unknown', reason: 'malformed-response' })
  })
  it.each([
    { ...effective(), documentId: OTHER }, { ...effective(), effectiveBy: 'SENTETIK-other' },
    { ...effective(), createdAtUtc: '2026-11-17T08:59:00Z' }, { ...effective(), createdBy: 'SENTETIK-other' },
  ])('refuses a finalize result that changes original identity or provenance %#', async (document) => {
    vi.stubGlobal('fetch', vi.fn(async () => json(body(document))))
    expect((await sendTransferDispatch(snapshot('finalize'))).type).toBe('unknown')
  })
  it('rejects a cancel result that rewrites the original effective fact or immutable cancellation reason', async () => {
    const request = snapshot('cancel')
    vi.stubGlobal('fetch', vi.fn(async () => json(body({ ...cancelled(), cancellationReason: 'SENTETIK changed' }))))
    expect((await sendTransferDispatch(request)).type).toBe('unknown')
    vi.stubGlobal('fetch', vi.fn(async () => json(body({ ...cancelled(), effectiveAtUtc: '2026-11-17T09:06:00Z' }))))
    expect((await sendTransferDispatch(request)).type).toBe('unknown')
  })
  it.each([
    { ...dispatched(), companyId: OTHER }, { ...dispatched(), sourceLocationId: OTHER }, { ...dispatched(), transferId: OTHER },
    { ...dispatched(), version: '3' }, { ...dispatched(), status: 'Accepted' },
    { ...dispatched(), lines: [...dispatched().lines].reverse() }, { ...dispatched(), dispatchedAtUtc: '2026-11-17T10:01:00Z' },
    { ...dispatched(), lines: [{ ...dispatched().lines[0], actualShippedKg: null }, dispatched().lines[1]] },
  ])('rejects a raw dispatch result for another source state or incomplete ordered set %#', async (raw) => {
    vi.stubGlobal('fetch', vi.fn(async () => json(raw, 201)))
    expect((await sendTransferDispatch(snapshot('dispatch'))).type).toBe('unknown')
  })
  it.each([{}, { document: draft(), isReplay: true, originalCorrelationId: 'synthetic-original' }])('does not resolve a malformed or unproven original response %#', async (raw) => {
    vi.stubGlobal('fetch', vi.fn(async () => json(raw, 201)))
    expect((await sendTransferDispatch(snapshot('create'))).type).toBe('unknown')
  })
  it.each([500, 503])('retains unavailable status %i as an unknown outcome', async (status) => {
    vi.stubGlobal('fetch', vi.fn(async () => json({}, status)))
    expect(await sendTransferDispatch(snapshot('create'))).toEqual({ type: 'unknown', reason: 'service-unavailable' })
  })
  it('retains offline and network failures without fabricating a current document', async () => {
    const request = snapshot('dispatch'); vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('SENTETIK network') }))
    vi.stubGlobal('navigator', { onLine: false }); expect(await sendTransferDispatch(request)).toEqual({ type: 'unknown', reason: 'offline' })
    vi.stubGlobal('navigator', { onLine: true }); expect(await sendTransferDispatch(request)).toEqual({ type: 'unknown', reason: 'network-error' })
  })
})

describe('transfer flow same-key isolation and original metadata', () => {
  it('refuses a foreign document, reordered create binding or mismatched source version before send', () => {
    expect(beginTransferDispatchAttempt('b', ACTOR, transfer(), { kind: 'create', payload: { documentId: DOCUMENT, transferId: OTHER, orderedTransferLineIds: [LINE, LINE2], eDispatchNumber: null, eDispatchAssociation: 1 } })).toBeNull()
    expect(beginTransferDispatchAttempt('b', ACTOR, transfer(), { kind: 'finalize', document: { ...draft(), transferId: OTHER }, payload: { expectedVersion: 1 } })).toBeNull()
    expect(beginTransferDispatchAttempt('b', ACTOR, transfer(), { kind: 'dispatch', document: effective(), payload: { expectedTransferVersion: '2', dispatchedAtUtc: AT, outgoingDocumentId: DOCUMENT, expectedOutgoingDocumentVersion: '2' } })).toBeNull()
  })
  it('clones and freezes full source metadata, retains the same document id and key, and blocks other targets until a positive replay', () => {
    const source = transfer(); const request = command('create')
    const first = beginTransferDispatchAttempt('bound', ACTOR, source, request, () => 'same-key')!
    const mutableSource = source as { lines: TransferDetail['lines'] }; mutableSource.lines = []
    leaveTransferDispatchAttempt('bound')
    expect(first.transfer.lines).toHaveLength(2); expect(Object.isFrozen(first.transfer.lines[0])).toBe(true)
    const other: TransferDispatchCommand = { kind: 'create', payload: { documentId: OTHER, transferId: OTHER,
      orderedTransferLineIds: [LINE, LINE2], eDispatchNumber: null, eDispatchAssociation: 1 } }
    expect(beginTransferDispatchAttempt('bound', ACTOR, { ...transfer(), transferId: OTHER }, other)).toBeNull()
    const retry = beginTransferDispatchAttempt('bound', ACTOR, first.transfer, first.command, () => 'never-used')!
    expect(retry).toBe(first)
    finishTransferDispatchAttempt(retry, { type: 'refused', status: 409, code: 'idempotency_conflict' })
    expect(transferDispatchFrozen(transferDispatchAttempt('bound'))).toBe(true)
    beginTransferDispatchAttempt('bound', ACTOR, first.transfer, first.command)
    finishTransferDispatchAttempt(first, { type: 'applied', replayed: true })
    expect(transferDispatchFrozen(transferDispatchAttempt('bound'))).toBe(false)
  })
  it('retains post-commit authority loss but permits correction after a fresh known validation refusal', () => {
    const request = snapshot('create'); finishTransferDispatchAttempt(request, { type: 'refused', status: 403, code: null })
    expect(transferDispatchFrozen(transferDispatchAttempt('synthetic-binding'))).toBe(true)
    resetTransferDispatchAttemptsForTests(); const fresh = snapshot('create')
    finishTransferDispatchAttempt(fresh, { type: 'refused', status: 422, code: 'validation_failed' })
    expect(transferDispatchFrozen(transferDispatchAttempt('synthetic-binding'))).toBe(false)
  })
  it('keeps route re-entry in the same binding and separates actor, company and server-session epochs', () => {
    const context = { userId: ACTOR, scopeMode: 'single' as const, companyId: COMPANY, capabilityGeneration: 'g1', capabilities: [] }
    const state = { ...initialShellState, activeContext: context, sessionEpoch: 1 }; const binding = transferDispatchBinding(state)
    expect(transferDispatchBinding({ ...state, routeEntryGeneration: 99 })).toBe(binding)
    expect(transferDispatchBinding({ ...state, sessionEpoch: 2 })).not.toBe(binding)
    expect(transferDispatchBinding({ ...state, activeContext: { ...context, userId: 'SENTETIK-other' } })).not.toBe(binding)
    expect(transferDispatchBinding({ ...state, activeContext: { ...context, companyId: OTHER } })).not.toBe(binding)
  })
})
