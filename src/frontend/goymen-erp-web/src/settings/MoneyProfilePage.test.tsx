import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { commands, COMPANY_B, installFetch, json, noContent, problem, renderAt } from './testHarness'
import { SETTINGS_CAPABILITY } from './capabilities'
import { cashTransferProfileFixture, moneyProfileFixture } from './moneyProfileFixtures'
import { MONEY_PROFILE_TEXT as T } from './moneyProfileText'

afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })
const path = '/ayarlar/para-profilleri'

describe('F029-S3 money profiles', () => {
  it('copies the actual bank payment values only on the explicit first-transfer action and freezes an unknown attempt', async () => {
    const fixture = cashTransferProfileFixture()
    const source = fixture.profiles[0].versions[0]
    const target = fixture.profiles[1]
    let attempts = 0
    const calls = installFetch({ capabilities: [SETTINGS_CAPABILITY.moneyProfiles], respond: (url, init) => {
      if (url.endsWith('/settings/money-profiles')) return json(fixture)
      if (init?.method === 'POST' && url.endsWith('/settings/money-profiles/versions')) {
        attempts++
        return attempts === 1 ? problem(503) : noContent({ 'Idempotency-Replayed': 'true' })
      }
      return undefined
    } })
    renderAt(path)
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: T.create }))
    const suggestion = within(screen.getByRole('region', { name: T.initialSuggestion }))
    expect(suggestion.getByText(fixture.companyId)).toBeTruthy()
    expect(suggestion.getByText(source.profileId)).toBeTruthy()
    expect(suggestion.getByText(source.version)).toBeTruthy()
    expect(screen.getByLabelText(T.scale)).toHaveProperty('value', '')
    expect(screen.getByLabelText(T.priceScale)).toHaveProperty('value', '')
    expect(commands(calls)).toHaveLength(0)
    await user.click(suggestion.getByRole('button', { name: T.useInitialSuggestion }))
    expect(screen.getByLabelText(T.scale)).toHaveProperty('value', '4')
    expect(screen.getByLabelText(T.priceScale)).toHaveProperty('value', '7')
    expect(screen.getByLabelText(T.active)).toHaveProperty('checked', false)
    expect(screen.getByRole('button', { name: T.save })).toHaveProperty('disabled', true)
    expect(commands(calls)).toHaveLength(0)
    await user.type(screen.getByLabelText(T.reason), 'SENTETIK ayrı profil')
    await user.click(screen.getByRole('button', { name: T.save }))
    await screen.findByText(T.unknown)
    expect(screen.getByRole('button', { name: T.useInitialSuggestion })).toHaveProperty('disabled', true)
    await user.click(screen.getByRole('button', { name: T.retry }))
    await screen.findByText(T.replay)
    const sent = commands(calls)
    expect(sent).toHaveLength(2)
    expect(sent[1].body).toEqual(sent[0].body)
    expect(sent[1].headers['Idempotency-Key']).toBe(sent[0].headers['Idempotency-Key'])
    expect(sent[0].body).toEqual({ expectedVersion: '0', currencyId: target.currencyId,
      operation: 'finance.cash-transfer.v1', scale: 4, unitPriceScale: 7, isActive: false,
      reason: 'SENTETIK ayrı profil', effectiveFrom: null })
  })

  it('does not guess a source for a first transfer profile when its same-currency bank payment profile is absent', async () => {
    const fixture = cashTransferProfileFixture()
    const calls = installFetch({ capabilities: [SETTINGS_CAPABILITY.moneyProfiles], respond: (url) =>
      url.endsWith('/settings/money-profiles') ? json({ ...fixture, profiles: [fixture.profiles[1]] }) : undefined })
    renderAt(path)
    await userEvent.setup().click(await screen.findByRole('button', { name: T.create }))
    expect(screen.getByText(T.initialSuggestionUnavailable)).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.useInitialSuggestion })).toBeNull()
    expect(screen.getByLabelText(T.scale)).toHaveProperty('value', '')
    expect(commands(calls)).toHaveLength(0)
  })

  it('discards a delayed former-company owner response instead of showing its initial suggestion in the new namespace', async () => {
    let release: ((response: Response) => void) | undefined
    let oldSignal: AbortSignal | null | undefined
    const calls = installFetch({ capabilities: [SETTINGS_CAPABILITY.moneyProfiles], respond: (url, init) => {
      if (!url.endsWith('/settings/money-profiles')) return undefined
      if (new Headers(init?.headers).get('X-Company-Id') === COMPANY_B)
        return json({ ...cashTransferProfileFixture(), companyId: COMPANY_B, profiles: [] })
      oldSignal = init?.signal
      return new Promise<Response>((resolve) => { release = resolve })
    } })
    renderAt(path)
    await waitFor(() => expect(release).toBeDefined())
    await userEvent.setup().selectOptions(screen.getByRole('combobox', { name: 'Şirket' }), COMPANY_B)
    await screen.findByText(T.empty)
    await act(async () => { release!(json(cashTransferProfileFixture())) })
    expect(oldSignal?.aborted).toBe(true)
    expect(screen.queryByRole('button', { name: T.create })).toBeNull()
    expect(screen.queryByText(T.initialSuggestion)).toBeNull()
    expect(commands(calls)).toHaveLength(0)
  })

  it('requires explicit values for a first credit currency profile and sends expected version zero', async () => {
    const fixture = moneyProfileFixture()
    const profile = { ...fixture.profiles[0], currencyCode: 'USD', currencyId: '66a81432-19e6-55ee-b9af-3927cb887dea',
      operation: 'finance.credit-plan', version: '0', versions: [] }
    const calls = installFetch({ capabilities: [SETTINGS_CAPABILITY.moneyProfiles], respond: (url, init) => {
      if (url.endsWith('/settings/money-profiles')) return json({ ...fixture, profiles: [profile] })
      if (init?.method === 'POST' && url.endsWith('/settings/money-profiles/versions')) return noContent()
      return undefined
    } })
    renderAt(path)
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: T.create }))
    expect(screen.getByLabelText(T.scale)).toHaveProperty('value', '')
    expect(screen.getByLabelText(T.active)).toHaveProperty('checked', false)
    expect(screen.getByRole('button', { name: T.save })).toHaveProperty('disabled', true)
    expect(commands(calls)).toHaveLength(0)
    await user.type(screen.getByLabelText(T.scale), '2')
    await user.click(screen.getByLabelText(T.active))
    await user.type(screen.getByLabelText(T.reason), 'SENTETIK banka belgesi')
    await user.click(screen.getByRole('button', { name: T.save }))
    expect(await screen.findByText(T.saved)).toBeTruthy()
    expect(commands(calls)[0].body).toMatchObject({ expectedVersion: '0', currencyId: profile.currencyId,
      operation: 'finance.credit-plan', scale: 2, unitPriceScale: null, isActive: true })
  })
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

  // 404 and 503 are the screen's own states (its heading stays). A read 403 is reported to the shell security boundary, which
  // revokes the capability and closes the route: the screen and its heading are gone (non-disclosing), unlike a plain 404.
  it.each([[503, 'service-unavailable', true, true], [403, 'not-found', false, false], [404, 'not-found', true, false]] as const)('shows a %i read state as %s (screen kept: %s, retry: %s) without editing a guessed profile', async (status, kind, screenKept, retry) => {
    const calls = installFetch({ capabilities: [SETTINGS_CAPABILITY.moneyProfiles], respond: (url) => url.endsWith('/settings/money-profiles') ? problem(status) : undefined })
    renderAt(path)
    await waitFor(() => expect(calls.some((call) => call.url.endsWith('/settings/money-profiles'))).toBe(true))
    await waitFor(() => expect(Array.from(document.querySelectorAll('[data-state]'), (block) => block.getAttribute('data-state'))).toEqual([kind]))
    expect(screen.queryByRole('heading', { name: T.title }) !== null).toBe(screenKept)
    expect(screen.queryByRole('button', { name: 'Tekrar dene' }) !== null).toBe(retry)
    expect(screen.queryByRole('button', { name: T.edit })).toBeNull()
  })

  it('keeps an empty company empty', async () => {
    installFetch({ capabilities: [SETTINGS_CAPABILITY.moneyProfiles], respond: (url) => url.endsWith('/settings/money-profiles') ? json({ ...moneyProfileFixture(), profiles: [] }) : undefined })
    renderAt(path)
    expect(await screen.findByText(T.empty)).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.edit })).toBeNull()
  })
})
