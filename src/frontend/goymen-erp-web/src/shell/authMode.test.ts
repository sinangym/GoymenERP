import { afterEach, describe, expect, it, vi } from 'vitest'
import { LOCAL_ACCOUNT_AUTH_MODE, developmentIdentityHeaderEnabled, isLocalAccountMode } from './authMode'

afterEach(() => {
  vi.unstubAllEnvs()
})

describe('authMode — ID-1F', () => {
  it('local-account mode is named only by VITE_AUTH_MODE=LocalAccount', () => {
    vi.stubEnv('VITE_AUTH_MODE', LOCAL_ACCOUNT_AUTH_MODE)
    expect(isLocalAccountMode()).toBe(true)
    vi.stubEnv('VITE_AUTH_MODE', 'localaccount')
    expect(isLocalAccountMode()).toBe(false)
    vi.stubEnv('VITE_AUTH_MODE', '')
    expect(isLocalAccountMode()).toBe(false)
  })

  it('never enables the development identity header in local-account mode', () => {
    vi.stubEnv('VITE_AUTH_MODE', LOCAL_ACCOUNT_AUTH_MODE)
    vi.stubEnv('DEV', true)
    vi.stubEnv('VITE_BOOTSTRAP_IDENTITY_ENABLED', 'true')
    expect(developmentIdentityHeaderEnabled()).toBe(false)
  })

  it('keeps the earlier rule outside local-account mode', () => {
    vi.stubEnv('VITE_AUTH_MODE', '')
    vi.stubEnv('DEV', false)
    vi.stubEnv('VITE_BOOTSTRAP_IDENTITY_ENABLED', 'true')
    expect(developmentIdentityHeaderEnabled()).toBe(true)
    vi.stubEnv('VITE_BOOTSTRAP_IDENTITY_ENABLED', 'false')
    expect(developmentIdentityHeaderEnabled()).toBe(false)
    vi.stubEnv('DEV', true)
    expect(developmentIdentityHeaderEnabled()).toBe(true)
  })
})
