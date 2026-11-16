import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { COMPANY_A, commands, installFetch, json, noContent, problem, renderAt } from './testHarness'
import { SETTINGS_CAPABILITY } from './capabilities'
import { CONTRACT_DISCOUNT_LIMIT_TEXT as T } from './salesContractDiscountLimitText'
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })
const path = '/ayarlar/sozlesme-iskonto-siniri'; const read = '/settings/company-policies/sales-contract-discount-limit'
describe('SC32b contract discount limit screen', () => {
  it('starts unset with no suggested value and preserves exact same-key retry', async () => {
    let posted = 0
    const calls = installFetch({ capabilities: [SETTINGS_CAPABILITY.salesContractDiscountLimit], respond: (url,init) => {
      if (init?.method === 'POST') { posted += 1; return posted === 1 ? problem(503) : noContent({ 'Idempotency-Replayed': 'true' }) }
      if (url.endsWith(read)) return json({ companyId: COMPANY_A, limitPercent: null, version: '0', versions: [] })
      return undefined
    } })
    renderAt(path); const user = userEvent.setup()
    expect(await screen.findByText(`${T.current}: ${T.unset}`)).toBeTruthy()
    await user.click(screen.getByRole('button', { name: T.edit })); expect(screen.getByLabelText(T.percent)).toHaveProperty('value','')
    await user.type(screen.getByLabelText(T.percent), '0'); await user.type(screen.getByLabelText(T.reason), 'Synthetic explicit zero')
    await user.click(screen.getByRole('button', { name: T.save })); expect(await screen.findByRole('alert')).toHaveProperty('textContent',T.unknown)
    await user.click(screen.getByRole('button', { name: T.retry })); expect(await screen.findByText(T.replay)).toBeTruthy()
    const sent = commands(calls); expect(sent).toHaveLength(2)
    expect(sent[0].body).toEqual({ expectedVersion: '0', limitPercent: '0', reason: 'Synthetic explicit zero' })
    expect(sent[1].body).toEqual(sent[0].body); expect(sent[1].headers['Idempotency-Key']).toEqual(sent[0].headers['Idempotency-Key'])
  })
  it('renders a broken owner response as an error instead of a permissive limit', async () => {
    installFetch({ capabilities: [SETTINGS_CAPABILITY.salesContractDiscountLimit], respond: url => url.endsWith(read) ? json({ companyId: COMPANY_A, limitPercent: '100', version: '0', versions: [] }) : undefined })
    renderAt(path); expect(await screen.findByRole('alert')).toBeTruthy(); expect(screen.queryByRole('button', { name: T.edit })).toBeNull()
  })
})
