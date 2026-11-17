import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { ShellProvider } from '../shell/ShellProvider'
import { AppShell } from '../shell/AppShell'
import { PurchaseInvoiceVariancePolicyPage } from './PurchaseInvoiceVariancePolicyPage'
import { PURCHASE_INVOICE_VARIANCE_POLICY_PERMISSION as permission, PURCHASE_INVOICE_VARIANCE_POLICY_PATH as path } from './purchaseInvoiceVariancePolicyContract'
import { PURCHASE_INVOICE_VARIANCE_POLICY_TEXT as T } from './purchaseInvoiceVariancePolicyText'
import { COMPANY_A, commands, installFetch, json, noContent, problem } from './testHarness'

afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })
const empty = () => ({ companyId: COMPANY_A, version: '0', current: null, versions: [] })
function mount() {
  const router = createMemoryRouter([{ path: '/', element: <AppShell />, children: [{ path: 'invoice-policy-test', element: <PurchaseInvoiceVariancePolicyPage /> }] }], { initialEntries: ['/invoice-policy-test'] })
  render(<ShellProvider queryClient={new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })}><RouterProvider router={router} /></ShellProvider>)
}
describe('API-97 invoice variance policy screen', () => {
  it('shows zero only as editable initial fields and freezes the same publication after an unknown outcome', async () => {
    let publications = 0
    const calls = installFetch({ capabilities: [permission], respond: (url, init) => {
      if (url.endsWith(path)) return json(empty())
      if (url.endsWith(`${path}/publish`) && init?.method === 'POST') return ++publications === 1 ? problem(503) : noContent({ 'Idempotency-Replayed': 'true' })
      return undefined
    } })
    mount(); const user = userEvent.setup()
    expect(await screen.findByText(T.empty)).toBeTruthy(); expect(commands(calls)).toHaveLength(0)
    await user.click(screen.getByRole('button', { name: T.edit }))
    expect(screen.getByLabelText(T.amount)).toHaveProperty('value', '0')
    expect(screen.getByLabelText(T.rate)).toHaveProperty('value', '0')
    await user.clear(screen.getByLabelText(T.amount)); await user.type(screen.getByLabelText(T.amount), '0.01')
    await user.type(screen.getByLabelText(T.reason), 'SENTETIK açık yayın')
    await user.click(screen.getByRole('button', { name: T.publish }))
    expect(await screen.findByText(T.unknown)).toBeTruthy()
    expect(screen.getByLabelText(T.amount).closest('fieldset')).toHaveProperty('disabled', true)
    await user.click(screen.getByRole('button', { name: T.retry })); await screen.findByText(T.replay)
    const sent = commands(calls); expect(sent).toHaveLength(2)
    expect(sent[0].body).toEqual({ expectedVersion: '0', amountTolerance: '0.01', rateTolerance: '0', reason: 'SENTETIK açık yayın' })
    expect(sent[1].body).toEqual(sent[0].body); expect(sent[1].headers['Idempotency-Key']).toBe(sent[0].headers['Idempotency-Key'])
  })
  it.each([[404, 'Bulunamadı'], [409, 'Çakışma'], [503, 'Servis geçici olarak kullanılamıyor']] as const)('shows the distinct %s read state', async (status, title) => {
    installFetch({ capabilities: [permission], respond: (url) => url.endsWith(path) ? problem(status) : undefined })
    mount(); expect(await screen.findByText(`${title} — ${T.subject}`)).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.publish })).toBeNull()
  })
  it('rejects a malformed successful response and never offers a publication form', async () => {
    installFetch({ capabilities: [permission], respond: (url) => url.endsWith(path) ? json({ ...empty(), companyId: 'bbbbbbbb-0000-4000-8000-000000000002' }) : undefined })
    mount(); expect(await screen.findByText(`Beklenmeyen yanıt — ${T.subject}`)).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.edit })).toBeNull()
  })
  it('does not read or write the policy without its capability', async () => {
    const calls = installFetch({ capabilities: [] }); mount()
    expect(await screen.findByText(`Yetkiniz yok — ${T.subject}`)).toBeTruthy()
    expect(calls.some((call) => call.url.endsWith(path))).toBe(false)
    expect(commands(calls)).toHaveLength(0)
  })
  it('keeps loading distinct from an empty unpublished policy', async () => {
    installFetch({ capabilities: [permission], respond: (url) => url.endsWith(path) ? new Promise<Response>(() => {}) : undefined })
    mount(); expect(await screen.findByText(`Yükleniyor — ${T.subject}`)).toBeTruthy()
    expect(screen.queryByText(T.empty)).toBeNull(); expect(screen.queryByRole('button', { name: T.edit })).toBeNull()
  })
  it('shows offline without fabricating an empty policy or allowing a command', async () => {
    vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false)
    const calls = installFetch({ capabilities: [permission], respond: (url) => { if (url.endsWith(path)) throw new TypeError('SENTETIK network'); return undefined } })
    mount(); expect(await screen.findByText(`Bağlantı yok — ${T.subject}`)).toBeTruthy()
    expect(screen.queryByText(T.empty)).toBeNull(); expect(commands(calls)).toHaveLength(0)
  })
})
