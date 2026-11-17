import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { COMPANY_A, commands, installFetch, json, noContent, problem, renderAt } from './testHarness'
import { SETTINGS_CAPABILITY } from './capabilities'
import { OPERATIONAL_POLICY_TEXT as T } from './operationalPolicyText'
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })
describe('KIM-S19 operational policy screens', () => {
  it('requires explicit scrap selection and keeps the same command on an unknown response', async () => {
    let count = 0
    const read = '/settings/company-policies/manufacturing-scrap-cost'
    const calls = installFetch({ capabilities: [SETTINGS_CAPABILITY.manufacturingScrapCost], respond: (url, init) => {
      if (url.endsWith(read)) return json({ companyId: COMPANY_A, includeScrapCost: true, version: '0', versions: [] })
      if (url.endsWith(read + '/versions') && init?.method === 'POST') return ++count === 1 ? problem(503) : noContent({ 'Idempotency-Replayed': 'true' })
      return undefined
    } })
    renderAt('/ayarlar/fire-maliyet-katilimi')
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: T.edit }))
    expect(screen.getByLabelText(T.value)).toHaveProperty('value', '')
    await user.selectOptions(screen.getByLabelText(T.value), 'exclude')
    await user.type(screen.getByLabelText(T.reason), 'SENTETIK değişiklik')
    await user.click(screen.getByRole('button', { name: T.save }))
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', T.unknown)
    await user.click(screen.getByRole('button', { name: T.retry }))
    await screen.findByText(T.replay)
    const sent = commands(calls)
    expect(sent).toHaveLength(2)
    expect(sent[0].body).toEqual({ expectedVersion: '0', includeScrapCost: false, reason: 'SENTETIK değişiklik' })
    expect(sent[1].body).toEqual(sent[0].body)
    expect(sent[1].headers['Idempotency-Key']).toBe(sent[0].headers['Idempotency-Key'])
  })
  it('shows the server retention value and never preselects a new duration', async () => {
    const calls = installFetch({ capabilities: [SETTINGS_CAPABILITY.notificationRetention], respond: (url) => url.endsWith('/settings/company-policies/notification-retention') ? json({ companyId: COMPANY_A, activeDays: 90, version: '0', versions: [] }) : undefined })
    renderAt('/ayarlar/bildirim-etkinlik-suresi')
    const user = userEvent.setup()
    expect(await screen.findByText(`${T.current}: 90 ${T.days}`)).toBeTruthy()
    await user.click(screen.getByRole('button', { name: T.edit }))
    expect(screen.getByLabelText(T.activeDays)).toHaveProperty('value', '')
    expect(commands(calls)).toHaveLength(0)
  })
})
