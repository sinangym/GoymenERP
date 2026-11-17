import { describe, expect, it, vi, afterEach } from 'vitest'
import { changeTwoFactor, decodeTwoFactorEnrollment, decodeTwoFactorReceipt, decodeTwoFactorState } from './twoFactorApi'
const secret = 'A'.repeat(32), recoveryCodes = Array.from({ length: 10 }, (_, i) => `${i.toString(16).toUpperCase().padStart(4, '0')}-AAAA-AAAA-AAAA-AAAA-AAAA-AAAA-AAAA`)
afterEach(() => vi.unstubAllGlobals())
describe('optional two factor wire contract', () => {
  it('separates known disabled state from missing, contradictory and credential-bearing data', () => {
    expect(decodeTwoFactorState({ credentialVersion: '1', enabled: false, recoveryCodesRemaining: 0 })).not.toBeNull()
    expect(decodeTwoFactorState({ credentialVersion: '1', enabled: false, recoveryCodesRemaining: 1 })).toBeNull()
    expect(decodeTwoFactorState({ credentialVersion: '1', enabled: true, recoveryCodesRemaining: 10, secret })).toBeNull()
    expect(decodeTwoFactorState({})).toBeNull()
  })
  it('accepts only a matching authenticator setup and ten distinct one-time codes', () => {
    const enrollment = { credentialVersion: '1', secretKey: secret, authenticatorUri: `otpauth://totp/Synthetic?secret=${secret}&digits=6&period=30&algorithm=SHA1`, recoveryCodes, enrollmentToken: 'synthetic-protected-token' }
    expect(decodeTwoFactorEnrollment(enrollment)).not.toBeNull()
    expect(decodeTwoFactorEnrollment({ ...enrollment, authenticatorUri: enrollment.authenticatorUri.replace(secret, 'B'.repeat(32)) })).toBeNull()
    expect(decodeTwoFactorEnrollment({ ...enrollment, recoveryCodes: Array(10).fill(recoveryCodes[0]) })).toBeNull()
    expect(decodeTwoFactorReceipt({ credentialVersion: '2', enabled: false, recoveryCodes })).toBeNull()
  })
  it('treats a contradictory or headerless replay as unknown and sends no secret through URLs', async () => {
    const fetch = vi.fn(async () => new Response(JSON.stringify({ credentialVersion: '2', enabled: true, recoveryCodes: null }), { status: 200, headers: { 'Idempotency-Replayed': 'true' } }))
    vi.stubGlobal('fetch', fetch)
    const body = { expectedVersion: '1', currentPassword: 'synthetic-secret', code: '123456', enrollmentToken: 'protected-token' }
    expect((await changeTwoFactor('synthetic-company', 'enable', body, 'same-key')).type).toBe('unknown')
    const [url, options] = fetch.mock.calls[0] as unknown as [string, RequestInit]
    expect(url).not.toContain(body.currentPassword)
    expect(url).not.toContain(body.code)
    expect(JSON.parse(String(options.body))).toEqual(body)
  })
})
