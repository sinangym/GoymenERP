import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { COMPANY_A, commands, installFetch, json, noContent, problem, renderAt } from './testHarness'
import { SETTINGS_CAPABILITY } from './capabilities'
import { FINANCE_OPENING_PERIOD_TEXT as T } from './financeOpeningPeriodText'
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })
const path = '/ayarlar/finans-acilis-donemi'
const read = '/settings/company-policies/finance-opening-period'
describe('KIM-S19 finance opening period screen', () => {
  it('requires an explicit new state and reason, preserving same-key retry on an unknown result', async () => {
    let posted = 0
    const calls = installFetch({ capabilities: [SETTINGS_CAPABILITY.financeOpeningPeriod], respond: (url, init) => {
      if (url.endsWith(read)) return json({ companyId: COMPANY_A, state: 0, version: '0', versions: [] })
      if (init?.method === 'POST' && url.endsWith(read + '/versions')) { posted += 1; return posted === 1 ? problem(503) : noContent({ 'Idempotency-Replayed': 'true' }) }
      return undefined
    } })
    renderAt(path)
    const user = userEvent.setup()
    expect(await screen.findByText(`${T.state}: ${T.unrecorded}`)).toBeTruthy()
    await user.click(screen.getByRole('button', { name: T.edit }))
    expect(screen.getByLabelText(T.newState)).toHaveProperty('value', '')
    await user.selectOptions(screen.getByLabelText(T.newState), 'open')
    await user.type(screen.getByLabelText(T.reason), 'SENTETIK açılış')
    await user.click(screen.getByRole('button', { name: T.save }))
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', T.unknown)
    await user.click(screen.getByRole('button', { name: T.retry }))
    expect(await screen.findByText(T.replay)).toBeTruthy()
    const sent = commands(calls)
    expect(sent).toHaveLength(2)
    expect(sent[0].body).toEqual({ expectedVersion: '0', open: true, reason: 'SENTETIK açılış' })
    expect(sent[1].headers['Idempotency-Key']).toBe(sent[0].headers['Idempotency-Key'])
    expect(sent[1].body).toEqual(sent[0].body)
  })
})
