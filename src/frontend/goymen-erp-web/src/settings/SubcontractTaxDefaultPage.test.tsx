/** Synthetic DOM/transport sources. Browser/HTTP/SQL runtime acceptance is NOT_RUN. */
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { ShellProvider } from '../shell/ShellProvider'
import { AppShell } from '../shell/AppShell'
import { SubcontractTaxDefaultPage } from './SubcontractTaxDefaultPage'
import { SUBCONTRACT_TAX_CAPABILITY as C, subcontractTaxPath, type SubcontractTaxSide } from './subcontractTaxContract'
import { SUBCONTRACT_TAX_TEXT as T } from './subcontractTaxText'
import { COMPANY_A, commands, installFetch, json, noContent, problem } from './testHarness'
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })
const empty = () => ({ companyId: COMPANY_A, current: null, version: '0', versions: [] })
const options = () => ({ companyId: COMPANY_A, side: 2, on: '2026-11-17', defaultVersion: '0', default: null,
  vatRates: [{ code: '20', name: 'SENTETIK KDV', percent: '20', taxCode: null }], withholdingRatios: [], exemptionCodes: [], withholdingCodes: [], sourceVersion: 'SENTETIK-lists:1' })
function mount(side: SubcontractTaxSide = 'purchasing') {
  const router = createMemoryRouter([{ path: '/', element: <AppShell />, children: [{ path: 'tax-policy-test', element: <SubcontractTaxDefaultPage side={side} /> }] }], { initialEntries: ['/tax-policy-test'] })
  render(<ShellProvider queryClient={new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })}><RouterProvider router={router} /></ShellProvider>)
}
describe('separate subcontract defaults', () => {
  it('records only an actual list selection and freezes the exact body and key after unknown outcome', async () => {
    const path = subcontractTaxPath('purchasing'); let count = 0
    const calls = installFetch({ capabilities: [C.purchasing], respond: (url, init) => {
      if (url.endsWith(path + '/options')) return json(options())
      if (url.endsWith(path)) return json(empty())
      if (url.endsWith(path + '/versions') && init?.method === 'POST') return ++count === 1 ? problem(503) : noContent({ 'Idempotency-Replayed': 'true' })
      return undefined
    } })
    mount(); const user = userEvent.setup(); await screen.findByText(T.empty)
    await user.selectOptions(await screen.findByLabelText(T.vat), '20')
    await user.type(screen.getByLabelText(T.reason), 'SENTETIK varsayılan')
    await user.click(screen.getByRole('button', { name: T.save })); await screen.findByText(T.unknown)
    expect(screen.getByLabelText(T.vat).closest('fieldset')).toHaveProperty('disabled', true)
    await user.click(screen.getByRole('button', { name: T.retry })); await screen.findByText(T.replay)
    const sent = commands(calls); expect(sent).toHaveLength(2)
    expect(sent[0].body).toEqual({ expectedVersion: '0', vatApplies: true, vatPercent: '20', exemptionCode: null,
      withholdingApplies: false, withholdingPercent: null, withholdingCode: null, reason: 'SENTETIK varsayılan' })
    expect(sent[1].body).toEqual(sent[0].body); expect(sent[1].headers['Idempotency-Key']).toBe(sent[0].headers['Idempotency-Key'])
  })
  it('never uses the Sales capability to show or fetch the Purchasing default', async () => {
    const calls = installFetch({ capabilities: [C.sales] }); mount()
    await screen.findByRole('heading', { name: T.purchasing })
    expect(calls.some(x => x.url.includes('purchasing-subcontract-tax-default'))).toBe(false); expect(commands(calls)).toHaveLength(0)
  })
  it.each([404, 409, 503])('keeps owner read refusal %s distinct from an empty policy', async status => {
    const path = subcontractTaxPath('purchasing')
    installFetch({ capabilities: [C.purchasing], respond: url => url.endsWith(path) ? problem(status) : url.endsWith(path + '/options') ? json(options()) : undefined })
    mount(); await screen.findByRole('heading', { name: T.purchasing }); expect(await screen.findByText(new RegExp('— ' + T.purchasing))).toBeTruthy()
    expect(screen.queryByText(T.empty)).toBeNull(); expect(screen.queryByRole('button', { name: T.save })).toBeNull()
  })
})
