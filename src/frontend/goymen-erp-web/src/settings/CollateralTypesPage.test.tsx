import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { COMPANY_A, commands, installFetch, json, noContent, problem, renderAt } from './testHarness'
import { SETTINGS_CAPABILITY } from './capabilities'
import { COLLATERAL_TYPES_TEXT as T, COLLATERAL_KIND_TEXT } from './collateralTypesText'
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })
const path = '/ayarlar/teminat-turleri'; const read = '/settings/collateral-types'
describe('SC32a collateral type screen', () => {
  it('starts empty, requires an explicit class and preserves same-key retry without client rate calculation', async () => {
    let posted = 0
    const calls = installFetch({ capabilities: [SETTINGS_CAPABILITY.collateralTypes], respond: (url, init) => {
      if (init?.method === 'POST') { posted += 1; return posted === 1 ? problem(503) : noContent({ 'Idempotency-Replayed': 'true' }) }
      if (url.endsWith(read)) return json({ companyId: COMPANY_A, entries: [] })
      return undefined
    } })
    renderAt(path); const user = userEvent.setup()
    expect(await screen.findByText(T.empty)).toBeTruthy()
    await user.click(screen.getByRole('button', { name: T.add }))
    expect(screen.getByLabelText(T.kind)).toHaveProperty('value', '')
    await user.type(screen.getByLabelText(T.code), 'DBS'); await user.type(screen.getByLabelText(T.name), 'Synthetic DBS')
    await user.selectOptions(screen.getByLabelText(T.kind), 'Dbs'); await user.type(screen.getByLabelText(T.reason), 'Synthetic')
    expect(screen.getByRole('option', { name: COLLATERAL_KIND_TEXT.Dbs })).toBeTruthy()
    await user.click(screen.getByRole('button', { name: T.save }))
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', T.unknown)
    await user.click(screen.getByRole('button', { name: T.retry }))
    expect(await screen.findByText(T.replay)).toBeTruthy()
    const sent = commands(calls); expect(sent).toHaveLength(2)
    expect(sent[0].body).toEqual({ code: 'DBS', name: 'Synthetic DBS', kind: 'Dbs', reason: 'Synthetic' })
    expect(sent[1].body).toEqual(sent[0].body); expect(sent[1].headers['Idempotency-Key']).toEqual(sent[0].headers['Idempotency-Key'])
  })
  it('shows a malformed owner response as an error and does not offer a create form', async () => {
    installFetch({ capabilities: [SETTINGS_CAPABILITY.collateralTypes], respond: url => url.endsWith(read) ? json({ companyId: COMPANY_A, entries: [{ kind: 'Cheque' }] }) : undefined })
    renderAt(path)
    expect(await screen.findByRole('alert')).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.add })).toBeNull()
  })
})
