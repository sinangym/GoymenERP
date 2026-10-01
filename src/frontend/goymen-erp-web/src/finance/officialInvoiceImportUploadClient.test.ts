import { describe, expect, it, vi } from 'vitest'
import type { AuthBoundary } from '../shell/useAuthBoundary'
import { createOfficialInvoiceUploadClient, type OfficialInvoiceUploadAttemptContext } from './officialInvoiceImportUploadClient'

const companyId = '10000000-0000-0000-0000-000000000001'
const batchId = '11111111-1111-4111-8111-111111111111'
const checksum = 'a'.repeat(64)
const context: OfficialInvoiceUploadAttemptContext = {
  sessionEpoch: 7, actorId: 'maker', companyId, committedContextRevision: 3,
  requestFenceGeneration: 5, capabilityGeneration: '11', securityNamespace: 'finance-maker',
}
const snapshot = { ...context, namespace: context.securityNamespace }
const prepared = { utf8Csv: 'aW52b2ljZSxjc3Y=', sha256: checksum }
const preview = {
  templateVersion: 'finance-official-invoice-installment-csv.v1', sourceSha256: checksum, isValid: true,
  batch: { batchId, companyId, batchVersion: 'invoice-v1', batchChecksum: checksum, rows: [{}] }, issues: [],
}
const pending = {
  batchId, companyId, batchVersion: 'invoice-v1', batchChecksum: checksum,
  templateVersion: preview.templateVersion, status: 'Pending', version: '1', rowCount: 1,
  uploadedBy: 'maker', uploadedAtUtc: '2026-09-30T12:00:00Z', approvedBy: null, approvedAtUtc: null,
} as const

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
  it('prepares immutable bytes once with base64 and SHA-256', async () => {
    const file = { arrayBuffer: async () => new TextEncoder().encode('invoice,csv').buffer } as File
    const client = createOfficialInvoiceUploadClient(context, boundary(), vi.fn())
    const result = await client.prepare(file)
    expect(result.utf8Csv).toBe('aW52b2ljZSxjc3Y=')
    expect(result.sha256).toMatch(/^[0-9a-f]{64}$/)
  })

  it('previews then binds company, exact payload, checksum and replay header to upload success', async () => {
    const fetcher = vi.fn()
      .mockResolvedValueOnce(response(preview))
      .mockResolvedValueOnce(response({ outcome: 'Succeeded', resource: pending, isReplay: true,
        originalCorrelationId: 'corr-upload', issues: [] }, 200, true))
    const client = createOfficialInvoiceUploadClient(context, boundary(), fetcher)

    await expect(client.preview(batchId, 'invoice-v1', prepared, new AbortController().signal))
      .resolves.toMatchObject({ kind: 'ready', preview: { sourceSha256: checksum, batchId } })
    await expect(client.upload(batchId, 'invoice-v1', prepared, 'same-key', new AbortController().signal))
      .resolves.toEqual({ kind: 'succeeded', replayed: true, batch: pending })
    expect(fetcher.mock.calls.map((call) => call[0])).toEqual([
      '/api/v1/finance/official-invoice-imports/preview',
      '/api/v1/finance/official-invoice-import-batches',
    ])
    expect(JSON.parse(fetcher.mock.calls[0][1].body)).toEqual({ batchId, batchVersion: 'invoice-v1', utf8Csv: prepared.utf8Csv })
    expect(JSON.parse(fetcher.mock.calls[1][1].body)).toEqual({ batchId, batchVersion: 'invoice-v1', utf8Csv: prepared.utf8Csv })
    expect(fetcher.mock.calls[1][1].headers).toMatchObject({ 'X-Company-Id': companyId, 'Idempotency-Key': 'same-key' })
  })

  it.each([
    ['foreign preview company', { ...preview, batch: { ...preview.batch, companyId: '99999999-9999-4999-8999-999999999999' } }],
    ['wrong preview batch', { ...preview, batch: { ...preview.batch, batchId: '99999999-9999-4999-8999-999999999999' } }],
    ['wrong preview checksum', { ...preview, sourceSha256: 'b'.repeat(64),
      batch: { ...preview.batch, batchChecksum: 'b'.repeat(64) } }],
  ])('keeps %s unknown', async (_label, body) => {
    const client = createOfficialInvoiceUploadClient(context, boundary(), vi.fn().mockResolvedValue(response(body)))
    await expect(client.preview(batchId, 'invoice-v1', prepared, new AbortController().signal))
      .resolves.toEqual({ kind: 'unknown' })
  })

  it.each([
    ['foreign company', { ...pending, companyId: '99999999-9999-4999-8999-999999999999' }, true],
    ['wrong batch', { ...pending, batchId: '99999999-9999-4999-8999-999999999999' }, true],
    ['wrong version', { ...pending, batchVersion: 'other' }, true],
    ['wrong checksum', { ...pending, batchChecksum: 'b'.repeat(64) }, true],
    ['header/body replay drift', pending, false],
    ['approved status', { ...pending, status: 'Approved' }, true],
  ])('keeps %s upload unknown', async (_label, resource, bodyReplay) => {
    const fetcher = vi.fn().mockResolvedValue(response({ outcome: 'Succeeded', resource, isReplay: bodyReplay,
      originalCorrelationId: 'corr-upload', issues: [] }, 200, true))
    const client = createOfficialInvoiceUploadClient(context, boundary(), fetcher)
    await expect(client.upload(batchId, 'invoice-v1', prepared, 'same-key', new AbortController().signal))
      .resolves.toEqual({ kind: 'unknown' })
  })

  it('reports authorization refusal and preserves the outcome as unknown', async () => {
    const auth = boundary()
    const client = createOfficialInvoiceUploadClient(context, auth, vi.fn().mockResolvedValue(response({}, 403)))
    await expect(client.upload(batchId, 'invoice-v1', prepared, 'same-key', new AbortController().signal))
      .resolves.toEqual({ kind: 'unknown' })
    expect(auth.report).toHaveBeenCalledWith(403, snapshot)
  })

  it('does not fetch after the security context changes', async () => {
    const changed = { ...snapshot, requestFenceGeneration: 6, namespace: 'finance-maker-refenced' }
    const auth = { capture: () => changed, report: vi.fn() } as unknown as AuthBoundary
    const fetcher = vi.fn()
    const client = createOfficialInvoiceUploadClient(context, auth, fetcher)

    await expect(client.upload(batchId, 'invoice-v1', prepared, 'same-key', new AbortController().signal))
      .resolves.toEqual({ kind: 'unknown' })
    expect(fetcher).not.toHaveBeenCalled()
  })
})
