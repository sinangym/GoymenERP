import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { ShellProvider } from '../shell/ShellProvider'
import { AppShell } from '../shell/AppShell'
import { COMPANY_A, installFetch, json } from '../settings/testHarness'
import { reportingRoutes } from './reportingRoutes'
import { REPORTING_TEXT as T } from './reportingText'

afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })

function mount(path: string) {
  const router = createMemoryRouter([{ path: '/', element: <AppShell />, children: reportingRoutes }], { initialEntries: [path] })
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
  render(<ShellProvider queryClient={queryClient}><RouterProvider router={router} /></ShellProvider>)
}

const LABEL = '1a000000-0000-4000-8000-000000000001'
const agingDefinition = {
  id: 'stock-aging', ownerModule: 'inventory', canExport: true,
  parameters: [
    { id: 'asOf', kind: 'date', required: true, choices: [], minimum: null, maximum: null },
    { id: 'ageBasis', kind: 'choice', required: true, choices: ['label-created', 'first-incoming'], minimum: null, maximum: null },
    { id: 'bucketDays', kind: 'ascending-whole-numbers', required: true, choices: [], minimum: '1', maximum: '36500' },
  ],
  columns: [{ id: 'labelCode', kind: 'text' }, { id: 'labelId', kind: 'identifier' }, { id: 'physicalKg', kind: 'number' }, { id: 'bucket', kind: 'text' }],
}
const catalog = (reports: unknown[], canBuildAuditorPackage = false) => ({ companyId: COMPANY_A, reports, canBuildAuditorPackage })
const table = {
  companyId: COMPANY_A, reportId: 'stock-aging', parameters: { ageBasis: 'label-created', asOf: '2026-11-22', bucketDays: '30,60' },
  columns: ['labelCode', 'labelId', 'physicalKg', 'bucket'], rows: [['SENTETIK-ETK-1', LABEL, '1250.5', '0-30']], page: 1, pageSize: 50, hasMore: false,
  summary: [{ id: 'bucketKg:0-30', value: '1250.5' }, { id: 'pageTotalKg', value: '1250.5' }], sourceStatuses: [], generatedAtUtc: '2026-11-22T09:00:00Z',
}
const noViews = { companyId: COMPANY_A, reportId: 'stock-aging', items: [] }
const problem = (status: number, code: string) => new Response(JSON.stringify({ status, code }), { status, headers: { 'Content-Type': 'application/problem+json' } })

describe('report centre', () => {
  it('lists only the reports the server returns for the active company and the auditor package when allowed', async () => {
    const calls = installFetch({ capabilities: [], respond: (url) => url.endsWith('/reporting/reports') ? json(catalog([agingDefinition], true)) : undefined })
    mount('/raporlar')
    expect(await screen.findByRole('link', { name: T.reports['stock-aging'] })).toBeTruthy()
    expect(screen.getByRole('link', { name: T.auditorOpen })).toBeTruthy()
    expect(screen.queryByRole('link', { name: T.reports.otif })).toBeNull()
    expect(calls.filter((call) => call.url.includes('/reporting/')).every((call) => call.headers['X-Company-Id'] === COMPANY_A)).toBe(true)
  })

  it('shows the empty state when nothing may be opened', async () => {
    installFetch({ capabilities: [], respond: (url) => url.endsWith('/reporting/reports') ? json(catalog([])) : undefined })
    mount('/raporlar')
    expect(await screen.findByText(T.noReports)).toBeTruthy()
  })

  it('answers a report the actor may not open like an unknown page and reads no rows', async () => {
    const calls = installFetch({ capabilities: [], respond: (url) => url.endsWith('/reporting/reports') ? json(catalog([agingDefinition])) : undefined })
    mount('/raporlar/intercompany')
    await waitFor(() => expect(calls.some((call) => call.url.endsWith('/reporting/reports'))).toBe(true))
    await waitFor(() => expect(screen.queryByRole('heading', { level: 1 })).toBeNull())
    expect(calls.some((call) => call.url.includes('/reporting/reports/intercompany'))).toBe(false)
  })

  it('runs the report with the typed parameters, renders server numbers and page-only summary, and exports through the server', async () => {
    const createObjectURL = vi.fn((_blob: Blob) => 'blob:report')
    const revokeObjectURL = vi.fn()
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL, revokeObjectURL }))
    const calls = installFetch({ capabilities: [], respond: (url, init) => {
      if (url.endsWith('/reporting/reports')) return json(catalog([agingDefinition]))
      if (url.includes('/reporting/saved-views?')) return json(noViews)
      if (url.includes('/reporting/reports/stock-aging/exports') && init?.method === 'POST') {
        return new Response('Etiket\r\n', { status: 200, headers: { 'Content-Type': 'text/csv; charset=utf-8', 'Content-Disposition': "attachment; filename=stock-aging.csv" } })
      }
      if (url.includes('/reporting/reports/stock-aging?')) return json(table)
      return undefined
    } })
    mount('/raporlar/stock-aging')
    const user = userEvent.setup()
    await screen.findByRole('heading', { level: 1, name: T.reports['stock-aging'] })
    await user.type(screen.getByLabelText(/Ölçüm tarihi/), '2026-11-22')
    await user.selectOptions(screen.getByLabelText(/Yaş başlangıcı/), 'label-created')
    await user.type(screen.getByLabelText(/Yaş aralığı sınırları/), '30,60')
    await user.click(screen.getByRole('button', { name: T.run }))
    expect(await screen.findByText('SENTETIK-ETK-1')).toBeTruthy()
    expect(screen.getByText(T.pageOnly)).toBeTruthy()
    expect(screen.getByText(`${T.bucketPrefix}0-30`)).toBeTruthy()
    const run = calls.find((call) => call.url.includes('/reporting/reports/stock-aging?'))!
    expect(run.url).toContain('asOf=2026-11-22')
    expect(run.url).toContain('bucketDays=30%2C60')
    await user.click(screen.getByRole('button', { name: T.exportCsv }))
    expect(await screen.findByText(T.exported)).toBeTruthy()
    const exported = calls.find((call) => call.url.includes('/exports'))!
    expect(exported.body).toEqual({ format: 'csv', parameters: { asOf: '2026-11-22', ageBasis: 'label-created', bucketDays: '30,60' }, page: 1, pageSize: 50,
      columns: ['labelCode', 'labelId', 'physicalKg', 'bucket'] })
    expect(createObjectURL).toHaveBeenCalledOnce()
  })

  it('names a not-connected source instead of showing an empty report', async () => {
    installFetch({ capabilities: [], respond: (url) => {
      if (url.endsWith('/reporting/reports')) return json(catalog([{ ...agingDefinition, id: 'otif', parameters: [] }]))
      if (url.includes('/reporting/saved-views?')) return json({ ...noViews, reportId: 'otif' })
      if (url.includes('/reporting/reports/otif?')) return problem(503, 'reporting_source_not_connected')
      return undefined
    } })
    mount('/raporlar/otif')
    await userEvent.setup().click(await screen.findByRole('button', { name: T.run }))
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', T.problems.reporting_source_not_connected)
    expect(screen.queryByRole('table')).toBeNull()
  })

  it('keeps the same idempotency key when a saved-view outcome is unknown and drops it once applied', async () => {
    let attempts = 0
    const calls = installFetch({ capabilities: [], respond: (url, init) => {
      if (url.endsWith('/reporting/reports')) return json(catalog([agingDefinition]))
      if (url.includes('/reporting/saved-views?')) return json(noViews)
      if (url.endsWith('/reporting/saved-views') && init?.method === 'POST') {
        attempts++
        if (attempts === 1) return new Response(null, { status: 503 })
        return new Response(JSON.stringify({ id: 'cccccccc-0000-4000-8000-000000000001', companyId: COMPANY_A, reportId: 'stock-aging', name: 'SENTETIK',
          parameters: {}, columns: ['labelCode', 'labelId', 'physicalKg', 'bucket'], version: '1', isActive: true,
          createdAtUtc: '2026-11-22T09:00:00Z', updatedAtUtc: '2026-11-22T09:00:00Z' }), { status: 201, headers: { 'Content-Type': 'application/json' } })
      }
      return undefined
    } })
    mount('/raporlar/stock-aging')
    const user = userEvent.setup()
    await user.type(await screen.findByLabelText(T.savedViewName), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: T.saveView }))
    expect(await screen.findByText(T.viewUnknown)).toBeTruthy()
    await user.click(screen.getByRole('button', { name: T.saveView }))
    expect(await screen.findByText(T.viewSaved)).toBeTruthy()
    const posts = calls.filter((call) => call.url.endsWith('/reporting/saved-views') && call.method === 'POST')
    expect(posts).toHaveLength(2)
    expect(posts[0].headers['Idempotency-Key']).toBe(posts[1].headers['Idempotency-Key'])
  })
})
