import { afterEach, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient } from '@tanstack/react-query'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { AppShell } from '../shell/AppShell'
import { CapabilityRoute } from '../shell/CapabilityRoute'
import { ShellProvider } from '../shell/ShellProvider'
import { COMPANY_A, COMPANY_B, IDENTITY, installFetch, json, problem } from '../settings/testHarness'
import { LOGISTICS_CAPABILITY } from './capabilities'
import { CUSTOMER_RETURN_CAPABILITY, type CustomerReturnContext } from './customerReturnApi'
import { CustomerReturnWeighing } from './CustomerReturnWeighing'
import type { CustomerReturnViewV1, RecordCustomerReturnWeighingV1 } from './customerReturnContract'
import { customerReturnText as T } from './customerReturnText'
import { WEIGHING_TEXT as WEIGHING } from './logisticsText'

const id = 'cccccccc-0000-4000-8000-000000000001'
const context: CustomerReturnContext = { companyId: COMPANY_A, salesOrderId: id, loadingInstructionId: id, loadingInstructionLineId: id, binding: 'SENTETIK', quantityUnit: 'Piece' }
const row: CustomerReturnViewV1 = { customerReturnId: id, companyId: COMPANY_A, loadingInstructionId: id, loadingInstructionLineId: id,
  sourceDeliveryId: id, sourceShipmentId: id, sourceOrderId: id, sourceOrderLineId: id, unitId: id, quantity: '1', sourceDeliveredQuantity: '2',
  status: 'Pending', version: '1', reason: 'SENTETIK özel iade nedeni', requestedBy: 'SENTETIK maker', occurredAtUtc: '2026-11-27T07:00:00Z', requestedAtUtc: '2026-11-27T07:01:00Z',
  decidedBy: null, decidedAtUtc: null, decisionNote: null, inventoryMovementId: null, originalLabelId: null, originalShipmentMovementId: null }
const caps = [LOGISTICS_CAPABILITY.instructionsDetail, CUSTOMER_RETURN_CAPABILITY.read, CUSTOMER_RETURN_CAPABILITY.recordWeighing]
const path = `/logistics/customer-returns/${id}/weighings`
function measured(request: RecordCustomerReturnWeighingV1, replayed = false) { return { return: { ...row, version: '2', weighing: { weighingId: request.weighingId,
  direction: request.direction, firstWeighingKg: request.firstWeighingKg, secondWeighingKg: request.secondWeighingKg, netKg: '13.125',
  recordedBy: IDENTITY.userId, recordedAtUtc: '2026-11-27T08:00:00Z' } }, isReplay: replayed, originalCorrelationId: 'original' } }
function mount(current = row, companyId = COMPANY_A, allowed = true, onRecorded = vi.fn()) {
  const router = createMemoryRouter([{ path: '/', element: <AppShell />, children: [{ path: 'weigh-return', element:
    <CapabilityRoute capability={[LOGISTICS_CAPABILITY.instructionsDetail]}><CustomerReturnWeighing context={{ ...context, companyId }} customerReturn={current} canRecord={allowed} onRecorded={onRecorded} /></CapabilityRoute> },
    { path: 'away', element: <p>SENTETIK başka ekran</p> }] }], { initialEntries: ['/weigh-return'] })
  render(<ShellProvider queryClient={new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })}><RouterProvider router={router} /></ShellProvider>)
  return router
}
async function fill() {
  const form = await screen.findByRole('region', { name: T.weighingTitle })
  fireEvent.change(within(form).getByRole('combobox', { name: WEIGHING.direction }), { target: { value: 'GrossFirst' } })
  fireEvent.change(within(form).getByLabelText(WEIGHING.first), { target: { value: '1013.125' } })
  fireEvent.change(within(form).getByLabelText(WEIGHING.second), { target: { value: '1000' } })
  return form
}
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks(); sessionStorage.clear(); Object.assign(IDENTITY, { authenticationSessionBinding: null }) })

it('sends only two actual weighing inputs and displays the seven server facts without a net input', async () => {
  const done = vi.fn(), calls = installFetch({ capabilities: caps, respond: (url, init) => url.includes(path) ? json(measured(JSON.parse(String(init?.body)) as RecordCustomerReturnWeighingV1)) : undefined })
  mount(row, COMPANY_A, true, done); const form = await fill()
  expect(within(form).queryByLabelText(WEIGHING.net)).toBeNull(); fireEvent.click(within(form).getByRole('button', { name: T.weighingRecord }))
  expect(await screen.findByText(T.commandSucceeded)).toBeTruthy(); expect(done).toHaveBeenCalledTimes(1)
  expect(screen.getByRole('region', { name: T.weighingRetained }).querySelectorAll('dt')).toHaveLength(7)
  const sent = calls.filter(call => call.url.includes(path) && call.method === 'POST'); expect(sent).toHaveLength(1)
  expect(Object.keys(sent[0]!.body as object).sort()).toEqual(['expectedVersion', 'weighingId', 'direction', 'firstWeighingKg', 'secondWeighingKg'].sort())
  expect(sent[0]!.body).toMatchObject({ expectedVersion: '1', direction: 'GrossFirst', firstWeighingKg: '1013.125', secondWeighingKg: '1000' })
})

it('keeps its original weighing id, version and key through unknown remount and a later terminal return', async () => {
  Object.assign(IDENTITY, { authenticationSessionBinding: 'SENTETIK-weigh-session' }); let posts = 0
  const calls = installFetch({ capabilities: caps, respond: (url, init) => {
    if (!url.includes(path)) return undefined
    const request = JSON.parse(String(init?.body)) as RecordCustomerReturnWeighingV1
    return ++posts === 1 ? problem(503) : json(measured(request, true), 200, { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'original' })
  } }); mount(); const form = await fill(); fireEvent.click(within(form).getByRole('button', { name: T.weighingRecord })); await screen.findByText(T.unknown)
  expect(sessionStorage.length).toBe(1); expect(JSON.stringify(sessionStorage)).not.toContain(row.reason); cleanup()
  mount({ ...row, status: 'Rejected', version: '3', decidedBy: 'checker', decidedAtUtc: '2026-11-27T09:00:00Z', weighing: {
    weighingId: COMPANY_A, direction: 'TareFirst', firstWeighingKg: '1000', secondWeighingKg: '1013.125', netKg: '13.125', recordedBy: 'other-current', recordedAtUtc: '2026-11-27T08:30:00Z' } })
  await screen.findByText(T.unknown); expect(screen.getByText(`${T.weighingVersion}: 1`)).toBeTruthy()
  fireEvent.click(screen.getByRole('button', { name: T.retry })); expect(await screen.findByText(T.weighingReplay)).toBeTruthy()
  const sent = calls.filter(call => call.url.includes(path)); expect(sent).toHaveLength(2)
  expect(sent[0]!.body).toEqual(sent[1]!.body); expect(sent[0]!.headers['Idempotency-Key']).toBe(sent[1]!.headers['Idempotency-Key'])
})

it('does not restore an unresolved command from another authenticated session', async () => {
  Object.assign(IDENTITY, { authenticationSessionBinding: 'SENTETIK-original-session' })
  const calls = installFetch({ capabilities: caps, respond: url => url.includes(path) ? problem(503) : undefined })
  mount(); const form = await fill(); fireEvent.click(within(form).getByRole('button', { name: T.weighingRecord }))
  await screen.findByText(T.unknown); expect(sessionStorage.length).toBe(1); cleanup()
  Object.assign(IDENTITY, { authenticationSessionBinding: 'SENTETIK-new-session' }); mount()
  await screen.findByRole('button', { name: T.weighingRecord })
  expect(screen.queryByRole('button', { name: T.retry })).toBeNull(); expect(screen.queryByText(T.unknown)).toBeNull()
  expect(calls.filter(call => call.url.includes(path))).toHaveLength(1)
})

it.each([{ capabilities: caps.filter(cap => cap !== CUSTOMER_RETURN_CAPABILITY.recordWeighing), allowed: true, company: COMPANY_A },
  { capabilities: caps, allowed: false, company: COMPANY_A }, { capabilities: caps, allowed: true, company: COMPANY_B }])('never exposes the weighing form outside its actual authority and company %j', async value => {
  const calls = installFetch({ capabilities: value.capabilities }); mount(row, value.company, value.allowed)
  await waitFor(() => expect(calls.some(call => call.url.endsWith('/shell/capabilities'))).toBe(true))
  expect(screen.queryByRole('button', { name: T.weighingRecord })).toBeNull(); expect(calls.some(call => call.url.includes(path))).toBe(false)
})

it('does not silently derive or submit a net when the two actual values need a native refusal', async () => {
  const calls = installFetch({ capabilities: caps, respond: url => url.includes(path) ? problem(422) : undefined }); mount(); const form = await fill()
  fireEvent.change(within(form).getByLabelText(WEIGHING.first), { target: { value: '1000' } })
  fireEvent.change(within(form).getByLabelText(WEIGHING.second), { target: { value: '1013.125' } })
  fireEvent.click(within(form).getByRole('button', { name: T.weighingRecord })); await screen.findByText(`${T.commandFailed} (422)`)
  expect(calls.filter(call => call.url.includes(path))).toHaveLength(1); expect(screen.queryByText(T.commandSucceeded)).toBeNull()
})

it('uses the existing quantity input guard for ambiguous separators and cannot mint a command while invalid', async () => {
  const calls = installFetch({ capabilities: caps }); mount(); const form = await fill()
  fireEvent.change(within(form).getByLabelText(WEIGHING.first), { target: { value: '13.125' } })
  expect(within(form).getByText(T.weighingInvalid)).toBeTruthy(); expect((within(form).getByRole('button', { name: T.weighingRecord }) as HTMLButtonElement).disabled).toBe(true)
  expect(calls.some(call => call.url.includes(path))).toBe(false)
})

it('keeps native POST 403 at the shell boundary and suppresses stale completion after route departure', async () => {
  const capabilities = [...caps]
  installFetch({ capabilities, respond: url => { if (!url.includes(path)) return undefined; capabilities.splice(0); return problem(403) } })
  mount(); const form = await fill(); fireEvent.click(within(form).getByRole('button', { name: T.weighingRecord }))
  await waitFor(() => expect(document.querySelector('[data-state="not-found"]')).not.toBeNull()); expect(screen.queryByText(T.commandSucceeded)).toBeNull()
  cleanup(); let finish!: (response: Response) => void
  const done = vi.fn(); installFetch({ capabilities: caps, respond: url => url.includes(path) ? new Promise(resolve => { finish = resolve }) : undefined })
  const router = mount(row, COMPANY_A, true, done), next = await fill(); fireEvent.click(within(next).getByRole('button', { name: T.weighingRecord }))
  await screen.findByText(T.sending); await act(async () => { await router.navigate('/away') })
  await act(async () => { finish(problem(503)); await Promise.resolve() }); expect(done).not.toHaveBeenCalled(); expect(screen.queryByText(T.commandSucceeded)).toBeNull()
})
