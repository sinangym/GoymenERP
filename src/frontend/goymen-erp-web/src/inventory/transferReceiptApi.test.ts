/** Synthetic owner-contract and immutable-intent source tests. No real SQL, authority or browser acceptance. */
import { afterEach, describe, expect, it, vi } from 'vitest'
import { initialShellState } from '../shell/shellState'
import { beginReceiptAttempt, decodeReceiptCandidates, decodeReceiptDocument, decodeReceiptDocuments, decodeReceiptWork, fetchReceiptCandidates,
  fetchReceiptDocuments, fetchReceiptWork, finishReceiptAttempt, leaveReceiptAttempt, mergeReceiptCandidatePages, mergeReceiptDocumentPages,
  receiptAttempt, receiptBinding, receiptCommandPurpose, receiptFrozen, resetReceiptAttemptsForTests, sendReceiptCommand,
  type ReceiptCandidate, type ReceiptCandidates, type ReceiptCommand, type ReceiptDocument, type ReceiptDocuments, type ReceiptFact,
  type ReceiptLoss, type ReceiptSnapshot, type ReceiptWork } from './transferReceiptApi'

const COMPANY = '11111111-1111-4111-8111-111111111111'
const TRANSFER = '22222222-2222-4222-8222-222222222222'
const SOURCE = '33333333-3333-4333-8333-333333333333'
const TARGET = '44444444-4444-4444-8444-444444444444'
const LINE = '55555555-5555-4555-8555-555555555555'
const LABEL = '66666666-6666-4666-8666-666666666666'
const DOCUMENT = '77777777-7777-4777-8777-777777777777'
const DOCLINE = '88888888-8888-4888-8888-888888888888'
const MOVEMENT = '99999999-9999-4999-8999-999999999999'
const ACTOR = 'SENTETIK-current-actor'
const OTHER = 'SENTETIK-other-actor'
const AT = '2026-11-17T12:00:00Z'
const work = (): ReceiptWork => ({ purpose: 'receive', transfer: { companyId: COMPANY, transferId: TRANSFER, version: '2', status: 'InTransit',
  sourceLocationId: SOURCE, sourceCustodianKind: 'Company', sourceCustodianId: COMPANY, destinationLocationId: TARGET, destinationCustodianKind: 'Company', destinationCustodianId: COMPANY,
  plannedAtUtc: '2026-11-17T09:00:00Z', dispatchedAtUtc: '2026-11-17T10:00:00Z', lines: [{ lineId: LINE, labelId: LABEL, expectedLabelVersion: '7', plannedKg: '10', actualShippedKg: '10', differenceKg: '0', status: 'InTransit' }] },
  facts: { companyId: COMPANY, transferId: TRANSFER, version: '2', status: 'InTransit', receipts: [], losses: [], overageRenewals: [] } })
const receipt = (overage = false): ReceiptFact => ({ lineId: LINE, dispatchedKg: '10', receivedKg: overage ? '12' : '8', shortageKg: overage ? '0' : '2', overageKg: overage ? '2' : '0',
  shortageMovementId: overage ? null : MOVEMENT, overageReason: overage ? 'SENTETIK fazla' : null, receivedAtUtc: AT, receivedBy: OTHER,
  overageStatus: overage ? 'Pending' : 'None', overageDecisionReason: null, overageDecidedAtUtc: null, overageDecidedBy: null, overageMovementId: null })
const loss = (): ReceiptLoss => ({ lineId: LINE, requestNumber: 2, explanation: 'SENTETIK kayıp', requestedAtUtc: '2026-11-17T11:00:00Z', requestedBy: OTHER,
  status: 'Pending', decisionReason: null, decidedAtUtc: null, decidedBy: null, movementId: null })
const draft = (): ReceiptDocument => ({ companyId: COMPANY, documentId: DOCUMENT, transferId: TRANSFER, version: '1', status: 'Draft', sourceTransferVersion: '2', receivedAtUtc: AT,
  lines: [{ documentLineId: DOCLINE, transferLineId: LINE, labelId: LABEL, dispatchedKg: '10', receivedKg: '8', overageReason: null }], createdBy: ACTOR,
  createdAtUtc: '2026-11-17T12:05:00Z', finalizedBy: null, finalizedAtUtc: null, finalizationReason: null, sourceVersion: 'inventory.transfer-receipt-document.v1' })
const effective = (): ReceiptDocument => ({ ...draft(), version: '2', status: 'Effective', finalizedBy: ACTOR, finalizedAtUtc: '2026-11-17T12:06:00Z', finalizationReason: 'SENTETIK kesinleştir' })
function context(kind: ReceiptCommand['kind']): ReceiptWork {
  const value = work(); const purpose = receiptCommandPurpose(kind)
  if (kind === 'overage-decision' || kind === 'overage-renewal') return { purpose, transfer: { ...value.transfer, version: '3', status: 'Accepted', lines: value.transfer.lines.map((line) => ({ ...line, status: 'Accepted' })) },
    facts: { ...value.facts, version: '3', status: 'Accepted', receipts: [{ ...receipt(true), ...(kind === 'overage-renewal' ? { overageStatus: 'Rejected' as const, overageDecisionReason: 'SENTETIK önceki ret', overageDecidedAtUtc: AT, overageDecidedBy: OTHER } : {}) }] } }
  return { ...value, purpose, facts: { ...value.facts, losses: kind === 'loss-decision' || kind === 'loss-withdrawal' ? [loss()] : [] } }
}
function command(kind: ReceiptCommand['kind']): ReceiptCommand {
  if (kind === 'create') return { kind, payload: { documentId: DOCUMENT, transferId: TRANSFER, expectedTransferVersion: '2', receivedAtUtc: AT,
    lines: [{ documentLineId: DOCLINE, transferLineId: LINE, receivedKg: '8', overageReason: null }] } }
  if (kind === 'finalize') return { kind, document: draft(), payload: { expectedDocumentVersion: '1', expectedTransferVersion: '2', reason: 'SENTETIK kesinleştir' } }
  if (kind === 'receive') return { kind, lineId: LINE, document: effective(), payload: { expectedTransferVersion: '2', receivedAtUtc: AT,
    incomingDocument: { kind: 'inventory.transfer.incoming', id: DOCUMENT, lineId: DOCLINE }, receivedKg: '8', overageReason: null, expectedIncomingDocumentVersion: '2' } }
  if (kind === 'overage-decision') return { kind, lineId: LINE, payload: { decision: 'Approve', decidedAtUtc: AT, reason: 'SENTETIK karar', requestNumber: 1 } }
  if (kind === 'overage-renewal') return { kind, lineId: LINE, payload: { expectedTransferVersion: '3', requestedAtUtc: AT, reason: 'SENTETIK yeni talep', overageKg: '1' } }
  if (kind === 'loss-request') return { kind, lineId: LINE, payload: { expectedTransferVersion: '2', requestedAtUtc: AT, explanation: 'SENTETIK kayıp' } }
  if (kind === 'loss-decision') return { kind, lineId: LINE, payload: { expectedTransferVersion: '2', decision: 'Approve', decidedAtUtc: AT, reason: 'SENTETIK karar', requestNumber: 2 } }
  return { kind, lineId: LINE, payload: { expectedTransferVersion: '2', requestNumber: 2, withdrawnAtUtc: AT, reason: 'SENTETIK geri çek' } }
}
const snapshot = (kind: ReceiptCommand['kind']) => beginReceiptAttempt('SENTETIK-binding', ACTOR, context(kind), command(kind), () => 'SENTETIK-key')!
function result(snapshot: ReceiptSnapshot, replay = false): unknown {
  const kind = snapshot.command.kind; const source = snapshot.work
  if (kind === 'create' || kind === 'finalize') return { document: kind === 'create' ? draft() : effective(), isReplay: replay, originalCorrelationId: 'SENTETIK-original' }
  if (kind === 'receive') return { transfer: { ...source.transfer, version: '3', status: 'Accepted', lines: source.transfer.lines.map((line) => ({ ...line, status: 'Accepted' })) },
    facts: { ...source.facts, version: '3', status: 'Accepted', receipts: [{ ...receipt(), receivedBy: ACTOR }] } }
  if (kind === 'loss-request') return { transfer: source.transfer, facts: { ...source.facts, losses: [{ ...loss(), requestNumber: 1, requestedAtUtc: AT, requestedBy: ACTOR }] } }
  if (kind === 'overage-renewal') return { transfer: source.transfer, facts: { ...source.facts, overageRenewals: [{ lineId: LINE, requestNumber: 2, overageKg: '1', reason: 'SENTETIK yeni talep', requestedAtUtc: AT, requestedBy: ACTOR, status: 'Pending', decisionReason: null, decidedAtUtc: null, decidedBy: null, movementId: null }] } }
  if (kind === 'overage-decision') return { transfer: source.transfer, facts: { ...source.facts, receipts: [{ ...receipt(true), overageStatus: 'Approved', overageDecisionReason: 'SENTETIK karar', overageDecidedAtUtc: AT, overageDecidedBy: ACTOR, overageMovementId: MOVEMENT }] } }
  if (kind === 'loss-decision') return { transfer: { ...source.transfer, version: '3', status: 'Closed', lines: source.transfer.lines.map((line) => ({ ...line, status: 'WrittenOff' })) },
    facts: { ...source.facts, version: '3', status: 'Closed', losses: [{ ...loss(), status: 'Approved', decisionReason: 'SENTETIK karar', decidedAtUtc: AT, decidedBy: ACTOR, movementId: MOVEMENT }] } }
  return { transfer: source.transfer, facts: { ...source.facts, losses: [{ ...loss(), status: 'Withdrawn', decisionReason: 'SENTETIK geri çek', decidedAtUtc: AT, decidedBy: ACTOR }] } }
}
const json = (raw: unknown, status = 200, headers: Record<string, string> = {}) => new Response(JSON.stringify(raw), { status, headers })
const page = (items: readonly ReceiptDocument[] = [draft()]): ReceiptDocuments => ({ companyId: COMPANY, transferId: TRANSFER, items, searchWindowFull: false, nextCursor: null })
const candidate = (): ReceiptCandidate => ({ transfer: { transferId: TRANSFER, companyId: COMPANY, version: '2', status: 'InTransit', sourceLocationId: SOURCE, destinationLocationId: TARGET, plannedAtUtc: work().transfer.plannedAtUtc, dispatchedAtUtc: work().transfer.dispatchedAtUtc },
  labelCodes: ['SENTETIK-ETIKET'], sourceLocationCode: 'KAYNAK', sourceLocationName: 'Sentetik kaynak', destinationLocationCode: 'HEDEF', destinationLocationName: 'Sentetik hedef' })
const candidates = (items = [candidate()]): ReceiptCandidates => ({ companyId: COMPANY, purpose: 'receive', query: null, limit: 25, items, searchWindowFull: false, nextCursor: null })
afterEach(() => { resetReceiptAttemptsForTests(); vi.unstubAllGlobals() })

describe('purpose work and immutable document owner proof', () => {
  it('reads the exact purpose and binds transfer facts company version and state', () => {
    expect(decodeReceiptWork(work(), COMPANY, TRANSFER, 'receive')).toEqual(work())
    expect(decodeReceiptDocument(draft(), work().transfer)).toEqual(draft())
    expect(decodeReceiptDocument(effective(), work().transfer)).toEqual(effective())
  })
  it('accepts the explicit dispatch work purpose without turning it into a receipt command authority', () => {
    const source: ReceiptWork = { ...work(), purpose: 'dispatch' }
    expect(decodeReceiptWork(source, COMPANY, TRANSFER, 'dispatch')).toEqual(source)
    expect(beginReceiptAttempt('SENTETIK-binding', ACTOR, source, command('receive'))).toBeNull()
  })
  it.each([
    { ...work(), purpose: 'loss-request' }, { ...work(), transfer: { ...work().transfer, companyId: TARGET } },
    { ...work(), transfer: { ...work().transfer, transferId: TARGET } }, { ...work(), facts: { ...work().facts, version: '3' } },
    { ...work(), facts: { ...work().facts, companyId: TARGET } }, { ...work(), facts: { ...work().facts, status: 'Accepted' } },
    { ...work(), facts: { ...work().facts, receipts: [{ ...receipt(), lineId: TARGET }] } }, { ...work(), cost: '10' },
  ])('refuses foreign purpose mismatched owner frames and unbound facts %#', (raw) => {
    expect(decodeReceiptWork(raw, COMPANY, TRANSFER, 'receive')).toBeNull()
  })
  it.each([
    { ...draft(), companyId: TARGET }, { ...draft(), transferId: TARGET }, { ...draft(), version: '2' }, { ...draft(), status: 'Effective' },
    { ...draft(), finalizedBy: ACTOR }, { ...draft(), sourceVersion: 'unknown' }, { ...draft(), createdAtUtc: '2026-02-31T12:00:00Z' },
    { ...draft(), lines: [{ ...draft().lines[0], transferLineId: TARGET }] }, { ...draft(), lines: [{ ...draft().lines[0], labelId: TARGET }] },
    { ...draft(), lines: [{ ...draft().lines[0], dispatchedKg: '9' }] }, { ...draft(), lines: [{ ...draft().lines[0], receivedKg: '8.0001' }] },
    { ...draft(), lines: [draft().lines[0], draft().lines[0]] }, { ...effective(), finalizationReason: null }, { ...draft(), price: '10' },
  ])('refuses a malformed or unrelated immutable incoming document %#', (raw) => { expect(decodeReceiptDocument(raw, work().transfer)).toBeNull() })
  it('does not treat contradictory zero overage or an unproved approved movement as known facts', () => {
    const raw = context('overage-decision')
    expect(decodeReceiptWork({ ...raw, facts: { ...raw.facts, receipts: [{ ...receipt(true), overageKg: '0' }] } }, COMPANY, TRANSFER, 'overage-decision')).toBeNull()
    expect(decodeReceiptWork({ ...raw, facts: { ...raw.facts, receipts: [{ ...receipt(true), overageStatus: 'Approved', overageDecisionReason: 'SENTETIK karar', overageDecidedAtUtc: AT, overageDecidedBy: ACTOR }] } }, COMPANY, TRANSFER, 'overage-decision')).toBeNull()
  })
  it('deduplicates identical document observations but refuses changed versions and cursor loops', () => {
    expect(mergeReceiptDocumentPages([page(), page()])).toEqual([draft()])
    expect(mergeReceiptDocumentPages([page(), page([effective()])])).toBeNull()
    expect(mergeReceiptDocumentPages([{ ...page(), nextCursor: 'SENTETIK-cursor' }, { ...page(), nextCursor: 'SENTETIK-cursor' }])).toBeNull()
    expect(decodeReceiptDocuments({ ...page(), searchWindowFull: true, nextCursor: 'SENTETIK-cursor' }, work().transfer)).toBeNull()
    expect(decodeReceiptDocuments({ ...page(), nextCursor: 'SENTETIK-cursor' }, work().transfer)).toBeNull()
  })
  it('binds candidate purpose company query limit cursor and safe actual display rows', () => {
    expect(decodeReceiptCandidates(candidates(), COMPANY, 'receive', null)).toEqual(candidates())
    expect(decodeReceiptCandidates({ ...candidates(), purpose: 'loss-request' }, COMPANY, 'receive', null)).toBeNull()
    expect(decodeReceiptCandidates({ ...candidates(), query: '' }, COMPANY, 'receive', null)).toBeNull()
    expect(decodeReceiptCandidates(candidates([{ ...candidate(), transfer: { ...candidate().transfer, companyId: TARGET } }]), COMPANY, 'receive', null)).toBeNull()
    expect(decodeReceiptCandidates(candidates([{ ...candidate(), labelCodes: ['bad\ncode'] }]), COMPANY, 'receive', null)).toBeNull()
    expect(mergeReceiptCandidatePages([candidates(), candidates()])).toEqual([candidate()])
    expect(mergeReceiptCandidatePages([candidates(), candidates([{ ...candidate(), transfer: { ...candidate().transfer, version: '3' } }])])).toBeNull()
  })
  it('sends the exact purpose routes and signed opaque cursors without a broad report read', async () => {
    const fetcher = vi.fn(async (input: RequestInfo | URL, _init?: RequestInit) => String(input).includes('/candidates') ? json({ ...candidates(), query: 'SENTETIK' }) : String(input).includes('receipt-work') ? json(work()) : json(page()))
    vi.stubGlobal('fetch', fetcher)
    expect((await fetchReceiptWork(COMPANY, TRANSFER, 'receive')).type).toBe('ok-single')
    expect((await fetchReceiptDocuments(work().transfer, 'SENTETIK/+=' )).type).toBe('ok-single')
    expect((await fetchReceiptCandidates(COMPANY, 'receive', 'SENTETIK', 'SENTETIK/+=' )).type).toBe('ok-single')
    expect(fetcher.mock.calls[0][0]).toBe(`/api/v1/inventory/transfers/${TRANSFER}/receipt-work?purpose=receive`)
    expect(String(fetcher.mock.calls[1][0])).toContain('cursor=SENTETIK%2F%2B%3D')
    expect(String(fetcher.mock.calls[2][0])).toContain('purpose=receive&limit=25&q=SENTETIK&cursor=SENTETIK%2F%2B%3D')
    expect(new Headers(fetcher.mock.calls[0][1]?.headers).get('X-Company-Id')).toBe(COMPANY)
  })
  it('discards late reads after abort and keeps unavailable distinct from true empty', async () => {
    const controller = new AbortController(); vi.stubGlobal('fetch', vi.fn(async () => { controller.abort(); return json(work()) }))
    expect(await fetchReceiptWork(COMPANY, TRANSFER, 'receive', controller.signal)).toEqual({ type: 'fence-stale' })
    vi.stubGlobal('fetch', vi.fn(async () => json({}, 503)))
    expect(await fetchReceiptDocuments(work().transfer, null)).toMatchObject({ type: 'http', status: 503 })
  })
})

describe('separate immutable receipt command intent and actual proof', () => {
  it.each(['create', 'finalize', 'receive', 'overage-decision', 'overage-renewal', 'loss-request', 'loss-decision', 'loss-withdrawal'] as const)(
    'sends and proves only the explicit %s owner action', async (kind) => {
      const request = snapshot(kind); expect(request).not.toBeNull()
      const fetcher = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => json(result(request), kind === 'finalize' ? 200 : 201)); vi.stubGlobal('fetch', fetcher)
      expect(await sendReceiptCommand(request)).toMatchObject({ type: 'applied', replayed: false })
      const expected = kind === 'create' ? '/inventory/transfer-receipt-documents' : kind === 'finalize' ? `/inventory/transfer-receipt-documents/${DOCUMENT}/finalize`
        : kind === 'receive' || kind.startsWith('overage-') ? `/inventory/transfers/${TRANSFER}/receive-lines/${LINE}${kind === 'receive' ? '' : `/${kind}`}` : `/inventory/transfers/${TRANSFER}/lines/${LINE}/${kind}`
      expect(fetcher.mock.calls[0][0]).toBe(`/api/v1${expected}`); expect(fetcher).toHaveBeenCalledTimes(1)
      expect(JSON.parse(String(fetcher.mock.calls[0][1]?.body))).toEqual(request.command.payload)
      expect(new Headers(fetcher.mock.calls[0][1]?.headers).get('Idempotency-Key')).toBe(request.key)
    })
  it.each(['create', 'finalize', 'receive', 'overage-decision', 'overage-renewal', 'loss-request', 'loss-decision', 'loss-withdrawal'] as const)(
    'accepts the positive original %s replay only with matching provenance', async (kind) => {
      const request = snapshot(kind); vi.stubGlobal('fetch', vi.fn(async () => json(result(request, true), 200, { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'SENTETIK-original' })))
      expect(await sendReceiptCommand(request)).toMatchObject({ type: 'applied', replayed: true })
    })
  it('never sends a received quantity time or document line different from the chosen actual effective document', () => {
    const source = command('receive'); if (source.kind !== 'receive') throw new Error('Synthetic fixture kind')
    for (const payload of [{ ...source.payload, receivedKg: '9' }, { ...source.payload, receivedAtUtc: '2026-11-17T13:00:00Z' },
      { ...source.payload, incomingDocument: { ...source.payload.incomingDocument, lineId: TARGET } }, { ...source.payload, expectedIncomingDocumentVersion: '1' }])
      expect(beginReceiptAttempt('SENTETIK-binding', ACTOR, work(), { ...source, payload })).toBeNull()
    expect(beginReceiptAttempt('SENTETIK-binding', ACTOR, work(), { ...source, document: draft() })).toBeNull()
  })
  it('does not confuse a line decision request with a newer pending request number', async () => {
    const request = snapshot('loss-decision'); const raw = result(request) as { transfer: unknown; facts: ReceiptWork['facts'] }
    vi.stubGlobal('fetch', vi.fn(async () => json({ ...raw, facts: { ...raw.facts, losses: [{ ...raw.facts.losses[0], requestNumber: 3 }] } }, 201)))
    expect((await sendReceiptCommand(request)).type).toBe('unknown')
  })
  it('rejects a positive response that rewrites transfer origin even when it carries the expected target receipt', async () => {
    const request = snapshot('receive'); const raw = result(request) as { transfer: ReceiptWork['transfer']; facts: ReceiptWork['facts'] }
    vi.stubGlobal('fetch', vi.fn(async () => json({ ...raw, transfer: { ...raw.transfer, sourceLocationId: TARGET } }, 201)))
    expect((await sendReceiptCommand(request)).type).toBe('unknown')
  })
  it('keeps a positive document with another creator or altered original facts unknown', async () => {
    const request = snapshot('create'); vi.stubGlobal('fetch', vi.fn(async () => json({ document: { ...draft(), createdBy: OTHER }, isReplay: false, originalCorrelationId: 'SENTETIK-original' }, 201)))
    expect((await sendReceiptCommand(request)).type).toBe('unknown')
  })
  it('does not claim maker-checker success for the same recorded requester and decider', async () => {
    const source = context('loss-decision'); const request = beginReceiptAttempt('SENTETIK-binding', ACTOR, { ...source, facts: { ...source.facts, losses: [{ ...loss(), requestedBy: ACTOR }] } }, command('loss-decision'))!
    vi.stubGlobal('fetch', vi.fn(async () => json(result(request), 201)))
    expect((await sendReceiptCommand(request)).type).toBe('unknown')
  })
  it('keeps service loss malformed success and an original later conflict frozen without a new identity', async () => {
    const request = snapshot('receive'); vi.stubGlobal('fetch', vi.fn(async () => json({}, 503)))
    const unknown = await sendReceiptCommand(request); finishReceiptAttempt(request, unknown)
    expect(receiptFrozen(receiptAttempt(request.binding))).toBe(true)
    expect(beginReceiptAttempt(request.binding, ACTOR, request.work, request.command, () => { throw new Error('Must retain original key') })).toBe(request)
    finishReceiptAttempt(request, { type: 'refused', status: 409, code: 'idempotency_conflict' })
    expect(receiptAttempt(request.binding)).toMatchObject({ phase: 'failed', unresolved: true })
    expect(beginReceiptAttempt(request.binding, ACTOR, context('loss-request'), command('loss-request'))).toBeNull()
  })
  it.each([401, 403])('keeps first final-authority refusal %i unresolved and retains the original document IDs after leaving', (status) => {
    const request = snapshot('create'); finishReceiptAttempt(request, { type: 'refused', status, code: null }); leaveReceiptAttempt(request.binding)
    expect(receiptFrozen(receiptAttempt(request.binding))).toBe(true)
    expect(beginReceiptAttempt(request.binding, ACTOR, request.work, request.command)).toBe(request)
  })
  it('isolates pending intents by API company actor and session while retaining route reentry', () => {
    const shell = { ...initialShellState, sessionEpoch: 1, activeContext: { userId: ACTOR, scopeMode: 'single' as const, companyId: COMPANY, capabilityGeneration: 'g1', capabilities: [] } }
    const binding = receiptBinding(shell)
    expect(receiptBinding({ ...shell, routeEntryGeneration: 2 })).toBe(binding)
    expect(receiptBinding({ ...shell, sessionEpoch: 2 })).not.toBe(binding)
    expect(receiptBinding({ ...shell, activeContext: { ...shell.activeContext, userId: OTHER } })).not.toBe(binding)
    expect(receiptBinding({ ...shell, activeContext: { ...shell.activeContext, companyId: TARGET } })).not.toBe(binding)
  })
})
