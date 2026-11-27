import { afterEach, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { AppShell } from '../shell/AppShell'
import { ShellProvider } from '../shell/ShellProvider'
import { COMPANY_A, COMPANY_B, IDENTITY, installFetch, json, problem } from '../settings/testHarness'
import { AI_PANEL_CAPABILITY } from '../ai/aiCapabilities'
import { AI_FEEDBACK_TEXT as FEEDBACK } from '../ai/aiFeedbackText'
import { WorkSalesSummary } from './WorkSalesSummary'
import { WORK_SALES_SUMMARY_TEXT as T } from './workSalesSummaryText'

const draftId = 'cccccccc-0000-4000-8000-000000000001', answerId = 'dddddddd-0000-4000-8000-000000000002'
const toolId = 'sales.draft-summary.v1', targetKind = 'sales.order.draft-summary', digest = 'sha256:' + 'a'.repeat(64)
const summary = 'SENTETIK yalnız kayıtlı durum ve miktar özeti'
const capabilities = [AI_PANEL_CAPABILITY, 'sales.drafts.detail']
const ready = { availability: 'ready', panelAvailable: true, tools: [{ toolId, version: '1', ownerModule: 'Sales', dataClass: 'Internal' }] }
const content = { draftId, version: '7', revision: 'Rev.0', state: 'Draft', approvalState: 'PendingApproval',
  lines: [{ lineId: draftId, lineNo: 1, productId: draftId, orderUnitId: draftId, quantity: '1.2500' }] }
const source = { targetKind, targetId: draftId, title: 'SENTETIK satış taslağı', sourceVersion: digest, asOfUtc: '2026-11-27T08:00:00Z', dataClass: 'Internal' }
function reply() { return { outcome: 'answered', toolResults: [{ toolId, toolVersion: '1', outcome: 'succeeded', contentJson: JSON.stringify(content), source: { ...source } }],
  answer: { text: summary, citations: [{ toolId, toolVersion: '1', targetKind, targetId: draftId, title: source.title, sourceVersion: digest, asOfUtc: source.asOfUtc }],
    modelDeployment: 'SENTETIK-local', policyVersion: 'SENTETIK-policy', answerId } } }
function mount(companyId = COMPANY_A, version = '7') {
  const router = createMemoryRouter([{ path: '/', element: <AppShell />, children: [
    { path: 'summary-test', element: <WorkSalesSummary companyId={companyId} draftId={draftId} expectedVersion={version} /> },
    { path: 'summary-next', element: <WorkSalesSummary companyId={COMPANY_A} draftId={draftId} expectedVersion="8" /> },
    { path: 'summary-away', element: <p>SENTETIK başka kayıt</p> },
  ] }], { initialEntries: ['/summary-test'] })
  render(<ShellProvider queryClient={new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0, staleTime: 0 } } })}><RouterProvider router={router} /></ShellProvider>)
  return router
}
function install(options: { readonly capabilities?: readonly string[]; readonly ai?: unknown; readonly answer?: () => Response | Promise<Response> } = {}) {
  return installFetch({ capabilities: options.capabilities ?? capabilities, respond: (url, init) => {
    if (url.endsWith('/ai/capabilities')) return json(options.ai ?? ready)
    if (url.endsWith('/ai/ask') && init?.method === 'POST') return options.answer?.() ?? json(reply())
    if (url.includes(`/ai/answers/${answerId}/feedback`)) return json({ answerId, version: '0', kind: null, reasonCode: null })
    return undefined
  } })
}
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks(); sessionStorage.clear(); Object.assign(IDENTITY, { authenticationSessionBinding: null }) })

it('asks only on explicit request and reuses grounded citations and the real AnswerId feedback surface', async () => {
  const calls = install(); mount()
  const button = await screen.findByRole('button', { name: T.request })
  expect(calls.filter(call => call.url.endsWith('/ai/ask'))).toHaveLength(0)
  fireEvent.click(button)
  expect(await screen.findByText(summary)).toBeTruthy(); expect(screen.getByText(T.scope)).toBeTruthy()
  expect(await screen.findByRole('region', { name: FEEDBACK.heading })).toBeTruthy()
  const ask = calls.filter(call => call.url.endsWith('/ai/ask')); expect(ask).toHaveLength(1)
  expect(ask[0]!.headers['X-Company-Id']).toBe(COMPANY_A)
  expect(ask[0]!.body).toEqual({ question: T.question, openRecordContext: { targetKind, targetId: draftId } })
  expect(calls.filter(call => call.method === 'POST')).toHaveLength(1)
  expect(JSON.stringify(sessionStorage)).not.toContain(summary)
})

it.each([
  { availability: 'disabled', panelAvailable: false, tools: [] },
  { availability: 'unavailable', panelAvailable: false, tools: [] },
  { availability: 'ready', panelAvailable: true, tools: [] },
  { ...ready, tools: [{ ...ready.tools[0], toolId: 'sales.order-status.v1' }] },
])('does not ask or fabricate a summary when the runtime or exact owner tool is unavailable %j', async ai => {
  const calls = install({ ai }); mount(); expect(await screen.findByText(T.unavailable)).toBeTruthy()
  expect(screen.queryByRole('button', { name: T.request })).toBeNull(); expect(calls.some(call => call.url.endsWith('/ai/ask'))).toBe(false)
})

it.each([{ caps: [] }, { caps: ['sales.order-drafts.approve'] }, { caps: [AI_PANEL_CAPABILITY] }, { caps: ['sales.drafts.detail'] }])('never treats approval or partial access as Sales read authority %j', async ({ caps }) => {
  const calls = install({ capabilities: caps }); mount()
  await waitFor(() => expect(calls.some(call => call.url.endsWith('/shell/capabilities'))).toBe(true))
  expect(calls.some(call => call.url.endsWith('/ai/capabilities') || call.url.endsWith('/ai/ask'))).toBe(false)
})

it.each([{ company: COMPANY_B, version: '7' }, { company: COMPANY_A, version: '07' }, { company: COMPANY_A, version: '7\n' }])('rejects a foreign company or malformed native row version before any AI read %j', async value => {
  const calls = install(); mount(value.company, value.version)
  await waitFor(() => expect(calls.some(call => call.url.endsWith('/shell/capabilities'))).toBe(true))
  expect(calls.some(call => call.url.endsWith('/ai/capabilities') || call.url.endsWith('/ai/ask'))).toBe(false)
})

it.each([
  { version: '8' }, { version: digest }, { draftId: COMPANY_B }, { privatePrice: '100' }, { lines: [{ ...content.lines[0], quantity: '1.2500\n' }] },
])('withholds the text and feedback when the owner content does not match this exact Work record/version %j', async delta => {
  const value = reply(); value.toolResults[0]!.contentJson = JSON.stringify({ ...content, ...delta })
  const calls = install({ answer: () => json(value) }); mount(); fireEvent.click(await screen.findByRole('button', { name: T.request }))
  expect(await screen.findByText(T.stale)).toBeTruthy(); expect(screen.queryByText(summary)).toBeNull()
  expect(calls.some(call => call.url.includes('/ai/answers/'))).toBe(false)
})

it.each(['tool', 'target', 'digest', 'citation', 'answer-id', 'second-tool'])('does not show a mismatched or unreceipted released answer: %s', async fault => {
  const value = reply()
  if (fault === 'tool') value.toolResults[0]!.toolId = 'sales.order-status.v1'
  if (fault === 'target') value.toolResults[0]!.source.targetId = COMPANY_B
  if (fault === 'digest') { value.toolResults[0]!.source.sourceVersion = '7'; value.answer.citations[0]!.sourceVersion = '7' }
  if (fault === 'citation') value.answer.citations[0]!.asOfUtc = '2026-11-27T09:00:00Z'
  if (fault === 'answer-id') value.answer.answerId = '00000000-0000-0000-0000-000000000000'
  if (fault === 'second-tool') value.toolResults.push({ ...value.toolResults[0]! })
  const calls = install({ answer: () => json(value) }); mount(); fireEvent.click(await screen.findByRole('button', { name: T.request }))
  await waitFor(() => expect(calls.filter(call => call.url.endsWith('/ai/ask'))).toHaveLength(1))
  await waitFor(() => expect(screen.queryByRole('button', { name: T.pending })).toBeNull())
  expect(screen.queryByText(summary)).toBeNull(); expect(calls.some(call => call.url.includes('/ai/answers/'))).toBe(false)
})

it('keeps cannot-answer distinct from a grounded summary and never opens feedback for it', async () => {
  const calls = install({ answer: () => json({ outcome: 'cannot_answer', toolResults: [], answer: null }) }); mount()
  fireEvent.click(await screen.findByRole('button', { name: T.request })); expect(await screen.findByText(T.cannotAnswer)).toBeTruthy()
  expect(screen.queryByText(summary)).toBeNull(); expect(calls.some(call => call.url.includes('/ai/answers/'))).toBe(false)
})

it('does not release a late summary after moving to another record version', async () => {
  let release!: (value: Response) => void
  const calls = install({ answer: () => new Promise(resolve => { release = resolve }) }); const router = mount()
  fireEvent.click(await screen.findByRole('button', { name: T.request })); await screen.findByRole('button', { name: T.pending })
  await act(async () => { await router.navigate('/summary-next') }); await screen.findByRole('button', { name: T.request })
  await act(async () => { release(json(reply())); await Promise.resolve() })
  expect(screen.queryByText(summary)).toBeNull(); expect(calls.some(call => call.url.includes('/ai/answers/'))).toBe(false)
})

it('drops an in-flight summary on a committed company switch and does not carry it into another session', async () => {
  let release!: (value: Response) => void
  Object.assign(IDENTITY, { authenticationSessionBinding: 'SENTETIK-summary-old' })
  const calls = install({ answer: () => new Promise(resolve => { release = resolve }) }); mount()
  fireEvent.click(await screen.findByRole('button', { name: T.request })); await screen.findByRole('button', { name: T.pending })
  await userEvent.setup().selectOptions(screen.getByRole('combobox', { name: 'Şirket' }), COMPANY_B)
  expect(await screen.findByText(T.unavailable)).toBeTruthy()
  await act(async () => { release(json(reply())); await Promise.resolve() }); expect(screen.queryByText(summary)).toBeNull()
  expect(calls.some(call => call.url.includes('/ai/answers/'))).toBe(false)
  cleanup(); Object.assign(IDENTITY, { authenticationSessionBinding: 'SENTETIK-summary-new' }); install(); mount()
  await screen.findByRole('button', { name: T.request }); expect(screen.queryByText(summary)).toBeNull()
})

it('reports a fresh Ask 403 through the real shell boundary without showing text or feedback', async () => {
  const caps = [...capabilities]
  const calls = installFetch({ capabilities: caps, respond: (url, init) => {
    if (url.endsWith('/ai/capabilities')) return json(ready)
    if (url.endsWith('/ai/ask') && init?.method === 'POST') { caps.splice(0); return problem(403) }
    return undefined
  } }); mount(); fireEvent.click(await screen.findByRole('button', { name: T.request }))
  expect(await screen.findByText(T.unavailable)).toBeTruthy(); expect(screen.queryByText(summary)).toBeNull()
  expect(calls.filter(call => call.url.endsWith('/ai/ask'))).toHaveLength(1)
})
