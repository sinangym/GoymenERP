import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { commands, installFetch, json, noContent, problem, renderAt } from './testHarness'
import { SETTINGS_CAPABILITY } from './capabilities'
import { moneyProfileFixture } from './moneyProfileFixtures'
import { MONEY_PROFILE_TEXT as T } from './moneyProfileText'

afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })
const path = '/ayarlar/para-profilleri'

describe('F029-S3 money profiles', () => {
  it('previews the changed scale before recording and keeps the same attempt on an unknown result', async () => {
    let attempts = 0
    const calls = installFetch({ capabilities: [SETTINGS_CAPABILITY.moneyProfiles], respond: (url, init) => {
      if (url.endsWith('/settings/money-profiles')) return json(moneyProfileFixture())
      if (init?.method === 'POST' && url.endsWith('/settings/money-profiles/versions')) {
        attempts += 1
        return attempts === 1 ? problem(503) : noContent({ 'Idempotency-Replayed': 'true' })
      }
      return undefined
    } })
    renderAt(path)
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: T.edit }))
    const price = screen.getByLabelText(T.priceScale)
    await user.clear(price)
    await user.type(price, '3')
    expect(screen.getByRole('region', { name: T.preview })).toBeTruthy()
    expect(commands(calls)).toEqual([])
    await user.type(screen.getByLabelText(T.reason), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: T.save }))
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', T.unknown)
    await user.click(screen.getByRole('button', { name: T.retry }))
    expect(await screen.findByText(T.replay)).toBeTruthy()
    const sent = commands(calls)
    expect(sent).toHaveLength(2)
    expect(sent[1].headers['Idempotency-Key']).toBe(sent[0].headers['Idempotency-Key'])
    expect(sent[0].body).toMatchObject({ expectedVersion: '1', scale: 2, unitPriceScale: 3, reason: 'SENTETIK' })
  })

  it.each([503, 403, 404])('shows a %i read state without editing a guessed profile', async (status) => {
    installFetch({ capabilities: [SETTINGS_CAPABILITY.moneyProfiles], respond: (url) => url.endsWith('/settings/money-profiles') ? problem(status) : undefined })
    renderAt(path)
    await waitFor(() => expect(document.querySelector('[data-state]')).not.toBeNull())
    expect(screen.queryByRole('button', { name: T.edit })).toBeNull()
  })

  it('keeps an empty company empty', async () => {
    installFetch({ capabilities: [SETTINGS_CAPABILITY.moneyProfiles], respond: (url) => url.endsWith('/settings/money-profiles') ? json({ ...moneyProfileFixture(), profiles: [] }) : undefined })
    renderAt(path)
    expect(await screen.findByText(T.empty)).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.edit })).toBeNull()
  })
})
