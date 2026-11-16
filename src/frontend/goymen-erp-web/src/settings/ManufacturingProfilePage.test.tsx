import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { COMPANY_A, commands, installFetch, json, problem, renderAt } from './testHarness'
import { SETTINGS_CAPABILITY, SETTINGS_MANUFACTURING_PROFILE_CONTROL as CONTROL } from './capabilities'
import { manufacturingProfileFixture, MANUFACTURING_PROFILE_ID } from './manufacturingProfileFixtures'
import { MANUFACTURING_PROFILE_TEXT as T } from './manufacturingProfileText'

afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })
const path = '/ayarlar/uretim-parametre-profilleri'
const read = '/settings/manufacturing-parameter-profiles'
const receipt = { profileId: MANUFACTURING_PROFILE_ID, profileVersion: '1', stateVersion: '2', status: 'Published' }

describe('KIM-S15 manufacturing profile single flow', () => {
  it('shows the prepared profile without drawing writes for a read-only capability', async () => {
    const calls = installFetch({ capabilities: [SETTINGS_CAPABILITY.manufacturingParameters], respond: (url) => url.endsWith(read) ? json(manufacturingProfileFixture()) : undefined })
    renderAt(path)
    expect(await screen.findByText('Taslak')).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.publish })).toBeNull()
    expect(screen.queryByRole('button', { name: T.prepare })).toBeNull()
    expect(calls.filter((call) => call.url.endsWith('/organization/work-centers'))).toEqual([])
    expect(commands(calls)).toEqual([])
  })

  it('publishes a draft directly and retries unknown outcomes with the same body and key', async () => {
    let posted = 0
    const calls = installFetch({ capabilities: [SETTINGS_CAPABILITY.manufacturingParameters, CONTROL.parameters.publish], respond: (url, init) => {
      if (url.endsWith(read)) return json(manufacturingProfileFixture())
      if (init?.method === 'POST' && url.endsWith('/publication')) { posted += 1; return posted === 1 ? problem(503) : json(receipt, 200, { 'Idempotency-Replayed': 'true' }) }
      return undefined
    } })
    renderAt(path)
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: T.publish }))
    await user.type(screen.getByLabelText(T.reason), 'SENTETIK yayım')
    await user.click(screen.getByRole('button', { name: T.publish }))
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', T.unknown)
    await user.click(screen.getByRole('button', { name: T.retry }))
    expect(await screen.findByText(T.replay)).toBeTruthy()
    const sent = commands(calls)
    expect(sent).toHaveLength(2)
    expect(sent[0].body).toEqual({ expectedVersion: '1', reason: 'SENTETIK yayım' })
    expect(sent[1].body).toEqual(sent[0].body)
    expect(sent[1].headers['Idempotency-Key']).toBe(sent[0].headers['Idempotency-Key'])
    expect(sent[0].headers['X-Company-Id']).toBe(COMPANY_A)
    expect(calls.some((call) => call.url.endsWith('/approval'))).toBe(false)
  })

  it('prepares an explicit new value without calculating or copying a catalog default', async () => {
    const calls = installFetch({ capabilities: [SETTINGS_CAPABILITY.manufacturingParameters, CONTROL.parameters.prepare], respond: (url, init) => {
      if (init?.method === 'POST' && url.endsWith(read)) return json({ ...receipt, profileVersion: '2', stateVersion: '1', status: 'Draft' })
      if (url.endsWith(read)) return json(manufacturingProfileFixture())
      if (url.endsWith('/organization/work-centers')) return json([])
      return undefined
    } })
    renderAt(path)
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: T.prepare }))
    const value = screen.getByLabelText('Yoğunluk (kg/m3)')
    expect(value).toHaveProperty('value', '')
    await user.type(value, '7851.125')
    await user.type(screen.getByLabelText(T.reason), 'SENTETIK hazırlık')
    await user.click(screen.getByRole('button', { name: T.save }))
    expect(await screen.findByText(T.prepared)).toBeTruthy()
    expect(commands(calls)[0].body).toMatchObject({ expectedVersion: '1', process: 'Slitting', workCenterId: null, values: [{ key: 'densityKgPerCubicMetre', value: '7851.125' }] })
  })

  it.each([403, 404, 503])('shows read refusal %i without an invented profile', async (status) => {
    const calls = installFetch({ capabilities: [SETTINGS_CAPABILITY.manufacturingParameters], respond: (url) => url.endsWith(read) ? problem(status) : undefined })
    renderAt(path)
    await waitFor(() => expect(document.querySelector('[data-state]')).not.toBeNull())
    expect(screen.queryByRole('button', { name: T.publish })).toBeNull()
    expect(commands(calls)).toEqual([])
  })
})
