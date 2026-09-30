import { describe, expect, it, vi } from 'vitest'
import type { AuthBoundary } from '../shell/useAuthBoundary'
import { createOfficialInvoiceApprovalClient } from './officialInvoiceImportApprovalClient'

const companyId = '10000000-0000-0000-0000-000000000001'
const batchId = '11111111-1111-4111-8111-111111111111'
const context = { sessionEpoch: 7, companyId, actorId: 'checker', committedContextRevision: 3,
  requestFenceGeneration: 5, capabilityGeneration: 11, securityNamespace: 'finance-checker' }
const batch = {
  batchId, companyId, batchVersion: 'invoice-v1', batchChecksum: 'a'.repeat(64),
  templateVersion: 'finance-official-invoice-installment-csv.v1', status: 'Pending', version: '1', rowCount: 1,
  uploadedBy: 'maker', uploadedAtUtc: '2026-09-30T12:00:00Z', approvedBy: null, approvedAtUtc: null,
} as const
const row = {
  openItemId: '33333333-3333-3333-3333-333333333333', sourceDocumentId: '44444444-4444-4444-4444-444444444444',
  sourceInstallmentId: '55555555-5555-5555-5555-555555555555', sourceVersion: 'invoice-v1',
  documentKind: 'official-invoice-installment', partyId: '66666666-6666-6666-6666-666666666666',
  companyAccountId: '77777777-7777-7777-7777-777777777777', currencyId: '22222222-2222-2222-2222-222222222222',
  amount: '1250.50', dueOn: '2026-10-31',
} as const
const detail = { batch, preview: { templateVersion: batch.templateVersion, sourceSha256: batch.batchChecksum, isValid: true,
  batch: { batchId, companyId, batchVersion: batch.batchVersion, batchChecksum: batch.batchChecksum, rows: [row] }, issues: [] } }
const approved = { ...batch, status: 'Approved', version: '2', approvedBy: 'checker', approvedAtUtc: '2026-09-30T13:00:00Z' }

function boundary(): AuthBoundary & { report: ReturnType<typeof vi.fn> } {
  const snapshot = { actorId: 'checker', sessionEpoch: 7, companyId,
    committedContextRevision: 3, requestFenceGeneration: 5, capabilityGeneration: 11, namespace: 'finance-checker' }
  return { capture: () => snapshot, report: vi.fn() }
}

function json(body: unknown, status = 200, headers?: Record<string, string>): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } })
}

describe('official invoice approval HTTP client', () => {
  it('binds company, exact GET paths and strict pending DTOs', async () => {
    const fetcher = vi.fn()
      .mockResolvedValueOnce(json({ companyId, items: [batch], nextCursor: 'next one' }))
      .mockResolvedValueOnce(json(detail))
    const client = createOfficialInvoiceApprovalClient(context, boundary(), fetcher)

    await expect(client.listPending(null, new AbortController().signal)).resolves.toMatchObject({ companyId })
    await expect(client.getPending(batchId, new AbortController().signal)).resolves.toMatchObject({ batch: { batchId } })
    expect(fetcher.mock.calls[0][0]).toBe('/api/v1/finance/official-invoice-import-batches/pending')
    expect(fetcher.mock.calls[1][0]).toBe(`/api/v1/finance/official-invoice-import-batches/${batchId}`)
    expect(fetcher.mock.calls[0][1]).toMatchObject({ method: 'GET', headers: expect.objectContaining({ 'X-Company-Id': companyId }) })
  })

  it.each([401, 403])('reports %i through the captured central auth boundary', async (status) => {
    const auth = boundary()
    const client = createOfficialInvoiceApprovalClient(context, auth, vi.fn().mockResolvedValue(json({ title: 'refused' }, status)))

    await expect(client.listPending('next one', new AbortController().signal)).rejects.toThrow(`pending-list-status-${status}`)
    expect(auth.report).toHaveBeenCalledWith(status, expect.objectContaining({ actorId: 'checker', companyId, sessionEpoch: 7 }))
  })

  it.each([401, 403])('keeps an approval %i nonterminal after reporting the boundary', async (status) => {
    const auth = boundary()
    const client = createOfficialInvoiceApprovalClient(context, auth,
      vi.fn().mockResolvedValue(json({ title: 'refused' }, status)))

    await expect(client.approve(batchId, '1', 'same-key', new AbortController().signal))
      .resolves.toEqual({ kind: 'unknown' })
    expect(auth.report).toHaveBeenCalledWith(status, expect.objectContaining({ capabilityGeneration: 11 }))
  })

  it('sends the exact approval path, body and key and accepts only matching 200 replay metadata', async () => {
    const fetcher = vi.fn().mockResolvedValue(json({ outcome: 'Succeeded', resource: approved, isReplay: true,
      originalCorrelationId: 'corr-1', issues: [] }, 200, { 'Idempotency-Replayed': 'true' }))
    const client = createOfficialInvoiceApprovalClient(context, boundary(), fetcher)

    await expect(client.approve(batchId, '1', 'stable-key', new AbortController().signal))
      .resolves.toEqual({ kind: 'succeeded', replayed: true, batch: approved })
    expect(fetcher.mock.calls[0][0]).toBe(`/api/v1/finance/official-invoice-import-batches/${batchId}/approval`)
    expect(fetcher.mock.calls[0][1]).toMatchObject({
      method: 'POST', headers: expect.objectContaining({ 'X-Company-Id': companyId, 'Idempotency-Key': 'stable-key' }),
      body: JSON.stringify({ batchId, expectedVersion: '1' }),
    })
  })

  it('keeps network, interrupted body and broad success outcomes unknown for same-key replay', async () => {
    const fetcher = vi.fn()
      .mockRejectedValueOnce(new Error('offline'))
      .mockResolvedValueOnce(new Response('{', { status: 200, headers: { 'Content-Type': 'application/json' } }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
    const client = createOfficialInvoiceApprovalClient(context, boundary(), fetcher)

    await expect(client.approve(batchId, '1', 'same-key', new AbortController().signal)).resolves.toEqual({ kind: 'unknown' })
    await expect(client.approve(batchId, '1', 'same-key', new AbortController().signal)).resolves.toEqual({ kind: 'unknown' })
    await expect(client.approve(batchId, '1', 'same-key', new AbortController().signal)).resolves.toEqual({ kind: 'unknown' })
    for (const call of fetcher.mock.calls) {
      expect(call[1].headers['Idempotency-Key']).toBe('same-key')
      expect(call[1].body).toBe(JSON.stringify({ batchId, expectedVersion: '1' }))
    }
  })

  it('rejects a foreign or malformed read instead of accepting another company', async () => {
    const foreign = { ...batch, companyId: '90000000-0000-0000-0000-000000000009' }
    const client = createOfficialInvoiceApprovalClient(context, boundary(),
      vi.fn().mockResolvedValue(json({ companyId, items: [foreign], nextCursor: null })))

    await expect(client.listPending(null, new AbortController().signal)).rejects.toThrow('pending-list-malformed')
  })

  it('refuses a success body that resolves after the captured security generation changes', async () => {
    let generation = 11
    let resolveBody!: (value: unknown) => void
    const body = new Promise<unknown>((resolve) => { resolveBody = resolve })
    const snapshot = () => ({ actorId: 'checker', sessionEpoch: 7, companyId,
      committedContextRevision: 3, requestFenceGeneration: 5, capabilityGeneration: generation,
      namespace: generation === 11 ? 'finance-checker' : 'finance-checker-refenced' })
    const auth: AuthBoundary = { capture: snapshot, report: vi.fn() }
    const response = { status: 200, headers: new Headers(), json: () => body } as Response
    const client = createOfficialInvoiceApprovalClient(context, auth, vi.fn().mockResolvedValue(response))

    const pending = client.approve(batchId, '1', 'same-key', new AbortController().signal)
    generation = 12
    resolveBody({ outcome: 'Succeeded', resource: approved, isReplay: false,
      originalCorrelationId: 'corr-1', issues: [] })

    await expect(pending).resolves.toEqual({ kind: 'unknown' })
  })
})
