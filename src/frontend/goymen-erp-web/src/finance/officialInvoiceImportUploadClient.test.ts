import { describe, expect, it, vi } from 'vitest'
import type { AuthBoundary } from '../shell/useAuthBoundary'
import { createOfficialInvoiceUploadClient, type OfficialInvoiceUploadAttemptContext } from './officialInvoiceImportUploadClient'

const companyId = '10000000-0000-0000-0000-000000000001'
const batchId = '11111111-1111-4111-8111-111111111111'
const context: OfficialInvoiceUploadAttemptContext = {
  sessionEpoch: 7,
  actorId: 'maker',
  companyId,
  committedContextRevision: 3,
  requestFenceGeneration: 5,
  capabilityGeneration: '11',
  securityNamespace: 'finance-maker',
}
const snapshot = { ...context, namespace: context.securityNamespace }
const pending = {
  batchId, companyId, batchVersion: 'invoice-v1', batchChecksum: 'a'.repeat(64),
  templateVersion: 'finance-official-invoice-installment-csv.v1', status: 'Pending', version: '1', rowCount: 1,
  uploadedBy: 'maker', uploadedAtUtc: '2026-09-30T12:00:00Z', approvedBy: null, approvedAtUtc: null,
} as const
const file = new File(['invoice,csv'], 'invoice.csv', { type: 'text/csv' })

function boundary() {
  return { capture: () => snapshot, report: vi.fn() } as unknown as AuthBoundary
}

function response(body: unknown, status = 200, replayed = false) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', 'Idempotency-Replayed': String(replayed) },
  })
}

describe('official invoice import upload client', () => {
  it('binds company, exact request and replay header to the complete pending resource', async () => {
    const fetcher = vi.fn().mockResolvedValue(response({ outcome: 'Succeeded', resource: pending, isReplay: true,
      originalCorrelationId: 'corr-upload', issues: [] }, 200, true))
    const client = createOfficialInvoiceUploadClient(context, boundary(), fetcher)

    await expect(client.upload(batchId, 'invoice-v1', file, 'same-key', new AbortController().signal))
      .resolves.toEqual({ kind: 'succeeded', replayed: true, batch: pending })
    expect(fetcher).toHaveBeenCalledTimes(1)
    const [url, init] = fetcher.mock.calls[0]
    expect(url).toBe('/api/v1/finance/official-invoice-import-batches')
    expect(init.headers).toMatchObject({ 'X-Company-Id': companyId, 'Idempotency-Key': 'same-key' })
    expect(JSON.parse(init.body)).toEqual({ batchId, batchVersion: 'invoice-v1', utf8Csv: 'aW52b2ljZSxjc3Y=' })
  })

  it.each([
    ['foreign company', { ...pending, companyId: '99999999-9999-4999-8999-999999999999' }, true],
    ['wrong batch', { ...pending, batchId: '99999999-9999-4999-8999-999999999999' }, true],
    ['wrong version', { ...pending, batchVersion: 'other' }, true],
    ['header/body replay drift', pending, false],
    ['approved status', { ...pending, status: 'Approved' }, true],
  ])('keeps %s unknown', async (_label, resource, bodyReplay) => {
    const fetcher = vi.fn().mockResolvedValue(response({ outcome: 'Succeeded', resource, isReplay: bodyReplay,
      originalCorrelationId: 'corr-upload', issues: [] }, 200, true))
    const client = createOfficialInvoiceUploadClient(context, boundary(), fetcher)
    await expect(client.upload(batchId, 'invoice-v1', file, 'same-key', new AbortController().signal))
      .resolves.toEqual({ kind: 'unknown' })
  })

  it('reports authorization refusal and preserves the outcome as unknown', async () => {
    const auth = boundary()
    const fetcher = vi.fn().mockResolvedValue(response({}, 403))
    const client = createOfficialInvoiceUploadClient(context, auth, fetcher)
    await expect(client.upload(batchId, 'invoice-v1', file, 'same-key', new AbortController().signal))
      .resolves.toEqual({ kind: 'unknown' })
    expect(auth.report).toHaveBeenCalledWith(403, snapshot)
  })

  it('does not fetch when the security context changes while file bytes are being prepared', async () => {
    let release!: (value: ArrayBuffer) => void
    const body = new Promise<ArrayBuffer>((resolve) => { release = resolve })
    const deferredFile = { arrayBuffer: () => body } as File
    let current = snapshot
    const auth = { capture: () => current, report: vi.fn() } as unknown as AuthBoundary
    const fetcher = vi.fn()
    const pendingUpload = createOfficialInvoiceUploadClient(context, auth, fetcher)
      .upload(batchId, 'invoice-v1', deferredFile, 'same-key', new AbortController().signal)

    current = { ...snapshot, requestFenceGeneration: 6, namespace: 'finance-maker-refenced' }
    release(new TextEncoder().encode('invoice,csv').buffer)

    await expect(pendingUpload).resolves.toEqual({ kind: 'unknown' })
    expect(fetcher).not.toHaveBeenCalled()
  })
})
