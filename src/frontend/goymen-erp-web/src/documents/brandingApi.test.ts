import { afterEach, describe, expect, it, vi } from 'vitest'
import { fetchBranding, fetchTemplate, writeBranding } from './brandingApi'
import { decodeTemplatePublication, type SetBranding } from './brandingContract'
const company = '58000000-0000-4000-8000-000000000001'
const command: SetBranding = { expectedVersion: '0', logo: null, letterhead: null, reason: 'SENTETIK' }
const branding = { companyId: company, version: '1', logo: null, letterhead: null, preparedBy: 'SENTETIK-maker', preparedAtUtc: '2026-11-17T08:00:00Z', reason: 'SENTETIK' }
afterEach(() => { vi.unstubAllGlobals() })
describe('Documents branding transport source contract', () => {
  it('accepts a genuine empty branding and configured upload policy without inventing an asset', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ companyId: company, branding: null,
      uploadPolicy: { allowedContentTypes: ['image/png'], maximumUploadBytes: 1024, canUpload: false } }), { status: 200 })))
    expect((await fetchBranding(company)).type).toBe('ok-single')
  })
  it('rejects foreign-company malformed and same-maker publications instead of publishing a partial result', () => {
    const asset = { companyId: company, documentId: '58000000-0000-4000-8000-000000000002', versionNo: 2, contentType: 'image/png',
      sizeBytes: 10, sha256: 'a'.repeat(64), scannerId: 'SENTETIK', uploadedBy: 'SENTETIK', uploadedAtUtc: '2026-11-17T08:00:00Z' }
    const value = { publicationId: '58000000-0000-4000-8000-000000000003', companyId: company, module: 2, templateCode: 'inventory.material-label.technical',
      version: '1', templateVersion: 'pub.1', draftVersion: '1', brandingVersion: '1', logo: asset, letterhead: null,
      preparedBy: 'SENTETIK-maker', publishedBy: 'SENTETIK-checker', publishedAtUtc: '2026-11-17T08:00:00Z', reason: 'SENTETIK' }
    expect(decodeTemplatePublication(value, company, 2)).not.toBeNull()
    expect(decodeTemplatePublication({ ...value, publishedBy: value.preparedBy }, company, 2)).toBeNull()
    expect(decodeTemplatePublication({ ...value, templateVersion: 'pub.2' }, company, 2)).toBeNull()
    expect(decodeTemplatePublication({ ...value, logo: { ...asset, companyId: value.publicationId } }, company, 2)).toBeNull()
    expect(decodeTemplatePublication({ ...value, html: '<script/>' }, company, 2)).toBeNull()
  })
  it('carries exact payload and key and validates original replay headers', async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response(JSON.stringify({ branding, isReplay: true, originalCorrelationId: 'SENTETIK-original' }),
      { status: 200, headers: { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'SENTETIK-original' } }))
    vi.stubGlobal('fetch', fetcher)
    expect((await writeBranding(company, 'SENTETIK-key', { kind: 'branding', payload: command })).type).toBe('applied')
    expect(fetcher.mock.calls[0][1].body).toBe(JSON.stringify(command)); expect(fetcher.mock.calls[0][1].headers['Idempotency-Key']).toBe('SENTETIK-key')
  })
  it.each([401, 403, 404, 409, 422])('preserves code-less terminal %i even without JSON', async (status) => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status })))
    expect(await writeBranding(company, 'SENTETIK-key', { kind: 'branding', payload: command })).toEqual({ type: 'refused', status, code: null })
  })
  it('keeps 503 and malformed success unknown so the original key is not discarded', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(new Response(null, { status: 503 })).mockResolvedValueOnce(new Response('{}', { status: 200 })))
    expect((await writeBranding(company, 'SENTETIK-key', { kind: 'branding', payload: command })).type).toBe('unknown')
    expect((await writeBranding(company, 'SENTETIK-key', { kind: 'branding', payload: command })).type).toBe('unknown')
  })
  it('does not reinterpret another module or an unavailable body as an empty template', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ companyId: company, module: 1, templateCode: 'sales.order-pdf', draft: null, publication: null, currentBranding: null }), { status: 200 })))
    expect(await fetchTemplate(company, 2)).toEqual({ type: 'malformed' })
  })
})
