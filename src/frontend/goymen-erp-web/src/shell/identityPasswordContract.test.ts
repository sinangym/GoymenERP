import { afterEach, describe, expect, it, vi } from 'vitest'
import { fetchIdentity } from './api'

const valid = { userId: 'SENTETIK', displayName: 'SENTETIK', email: null, allowedCompanyIds: [],
  authenticationSessionBinding: null, mustChangePassword: false, localCredentialVersion: null }

afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks() })

describe('Identity ID-2 required password fields', () => {
  it('accepts the server forced-change decision and canonical credential version together', async () => {
    const body = { ...valid, mustChangePassword: true, localCredentialVersion: '9223372036854775807' }
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify(body), { status: 200 })))
    expect(await fetchIdentity()).toEqual({ type: 'ok-single', data: body })
  })

  it('rejects absent mistyped inconsistent and out-of-range password metadata', async () => {
    const { mustChangePassword: _flag, ...missingFlag } = valid
    const { localCredentialVersion: _version, ...missingVersion } = valid
    for (const body of [missingFlag, missingVersion, { ...valid, mustChangePassword: 'false' },
      { ...valid, mustChangePassword: true }, { ...valid, localCredentialVersion: 1 },
      { ...valid, localCredentialVersion: '0' }, { ...valid, localCredentialVersion: '01' },
      { ...valid, localCredentialVersion: '9223372036854775808' }]) {
      vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify(body), { status: 200 })))
      expect(await fetchIdentity()).toEqual({ type: 'malformed' })
    }
  })
})
