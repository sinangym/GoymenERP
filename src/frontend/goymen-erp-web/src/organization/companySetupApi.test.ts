import { afterEach, describe, expect, it, vi } from 'vitest'
import { sendCompanySetup } from './companySetupApi'

const id = 'aaaaaaaa-0000-0000-0000-000000000001'
const request = { companyId: id, code: 'SENTETIK', legalName: 'SENTETIK şirket', vkn: '9000000033', expectedVersion: '0' as const, reason: 'SENTETIK talep' }
const company = { companyId: id, code: request.code, legalName: request.legalName, lifecycleState: 'SetupPending', version: 1 }
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks() })
describe('KIM-D56 command transport', () => {
  it('treats a successful foreign company or missing replay provenance as an unknown write', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ ...company, companyId: 'bbbbbbbb-0000-0000-0000-000000000002' }), { status: 201 })))
    expect((await sendCompanySetup('open', id, request, 'SENTETIK-key')).type).toBe('unknown')
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify(company), { status: 201, headers: { 'Idempotency-Replayed': 'true' } })))
    expect((await sendCompanySetup('open', id, request, 'SENTETIK-key')).type).toBe('unknown')
  })
  it('keeps a malformed conflict or service failure retryable with its original key', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status: 409 })))
    expect(await sendCompanySetup('open', id, request, 'SENTETIK-key')).toEqual({ type: 'unknown', state: { kind: 'malformed-response' } })
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status: 503 })))
    expect(await sendCompanySetup('open', id, request, 'SENTETIK-key')).toEqual({ type: 'unknown', state: { kind: 'service-unavailable' } })
  })
  it('sends the explicit target context only for selection and validates both replay headers', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ id, version: '1', status: 'Active', roleAssignmentId: id }), {
      status: 201, headers: { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'SENTETIK-original' },
    }))
    vi.stubGlobal('fetch', fetch)
    expect(await sendCompanySetup('assign', id, { userId: id, expectedCompanyVersion: 1, reason: 'SENTETIK seçim' }, 'SENTETIK-key'))
      .toEqual({ type: 'applied', replayed: true })
    expect(fetch.mock.calls[0]?.[1].headers).toMatchObject({ 'X-Company-Id': id, 'Idempotency-Key': 'SENTETIK-key' })
  })
})
