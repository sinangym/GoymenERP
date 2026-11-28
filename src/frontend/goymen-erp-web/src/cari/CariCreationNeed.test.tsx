import { useState } from 'react'
import { afterEach, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { AppShell } from '../shell/AppShell'
import { ShellProvider } from '../shell/ShellProvider'
import { WORK_CAPABILITY } from '../work/capabilities'
import { CariQuickCreate } from './CariQuickCreate'
import { resetPendingCreationNeeds } from './CariCreationNeed'
import { QUICK_CARI_TEXT as T } from './quickCreateText'
import { A_ID, B_ID, IDENTITY, installFetch, json, problem } from './commandPageHarness'
import type { CreateTaskBody } from '../work/collaborationApi'

const RECIPIENT = '11111111-1111-4111-8111-111111111111'
interface Call { readonly body: CreateTaskBody; readonly key: string | null; readonly company: string | null }
function setup(reply?: (call: Call, index: number) => Response | Promise<Response>, capabilities: readonly string[] | (() => readonly string[]) = [WORK_CAPABILITY.assignTask]) {
  const native = installFetch({ capabilities }), baseFetch = globalThis.fetch
  const calls: Call[] = [], people: string[] = []
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    if (url.includes('/work/people')) { people.push(url); return json({ companyId: new Headers(init?.headers).get('X-Company-Id'), people: [{ userId: RECIPIENT, displayName: 'Yetkili aday' }], hasMore: false }) }
    if (url.endsWith('/work/counterparty-creation-needs')) {
      const call = { body: JSON.parse(String(init?.body)) as CreateTaskBody, key: new Headers(init?.headers).get('Idempotency-Key'), company: new Headers(init?.headers).get('X-Company-Id') }
      calls.push(call); return reply === undefined ? json(task(call), 201) : reply(call, calls.length)
    }
    return baseFetch(input, init)
  }))
  return { calls, people, native }
}
function task(call: Call) {
  return { id: '22222222-2222-4222-8222-222222222222', companyId: call.company, ...call.body,
    assignerUserId: IDENTITY.userId, assignerDisplayName: 'Atayan', assigneeDisplayName: 'Yetkili aday',
    status: 'open', version: '1', createdAtUtc: '2026-11-28T08:00:00Z', closedAtUtc: null, closedByUserId: null, cancelReason: null }
}
function mount() {
  const created = vi.fn(async () => true)
  function Caller() {
    const [note, setNote] = useState(''), [sent, setSent] = useState(false), [unresolved, setUnresolved] = useState(false)
    return <form onSubmit={event => { event.preventDefault(); if (!unresolved) setSent(true) }}>
      <label>Korunan işlem<input value={note} onChange={event => setNote(event.target.value)} /></label>
      <button type="submit" disabled={unresolved}>Ana işlemi gönder</button>
      <CariQuickCreate initialRoles={1} onCreated={created} onUnresolvedChange={setUnresolved} />
      {sent ? <p>Ana işlem gönderildi</p> : null}
    </form>
  }
  const router = createMemoryRouter([{ path: '/', element: <AppShell />, children: [{ path: 'quick', element: <Caller /> }] }], { initialEntries: ['/quick'] })
  const view = render(<ShellProvider queryClient={new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })}><RouterProvider router={router} /></ShellProvider>)
  return { ...view, created }
}
async function fill() {
  await userEvent.click(await screen.findByRole('button', { name: T.needOpen }))
  await userEvent.type(screen.getByLabelText(T.needSubject), '  Cari talebi  ')
  await userEvent.type(screen.getByLabelText(T.needDetails), '  Ham açıklama  ')
  await userEvent.click(screen.getByRole('button', { name: T.needFind }))
  await screen.findByRole('option', { name: 'Yetkili aday' })
  expect((screen.getByLabelText(T.needRecipient) as HTMLSelectElement).value).toBe('')
  await userEvent.selectOptions(screen.getByLabelText(T.needRecipient), RECIPIENT)
  fireEvent.change(screen.getByLabelText(T.needDue), { target: { value: '2099-12-31T10:00' } })
}
afterEach(() => { cleanup(); resetPendingCreationNeeds(); localStorage.clear(); sessionStorage.clear(); vi.unstubAllGlobals(); vi.restoreAllMocks() })

it('Assign-only opens a real task from the denied native-create seam without creating a party or submitting the caller', async () => {
  const log = setup(); const { created } = mount()
  await screen.findByText(T.denied)
  expect(screen.queryByRole('button', { name: T.open })).toBeNull()
  await userEvent.type(screen.getByLabelText('Korunan işlem'), 'Korunacak')
  await fill()
  expect((screen.getByLabelText(T.needDue) as HTMLInputElement).value).toBe('2099-12-31T10:00')
  await userEvent.click(screen.getByRole('button', { name: T.needSend }))
  await screen.findByText(T.needCreated)
  expect(log.calls).toHaveLength(1)
  expect(log.calls[0].body).toEqual({ title: '  Cari talebi  ', details: '  Ham açıklama  ', assigneeUserId: RECIPIENT,
    dueAtUtc: new Date('2099-12-31T10:00').toISOString(), recordType: null, recordId: null })
  expect(log.calls[0].key).toMatch(/^[a-f0-9]{32}$/)
  expect(log.native.calls.filter(call => call.url.includes('/counterparties'))).toHaveLength(0)
  expect(created).not.toHaveBeenCalled()
  expect(screen.queryByText('Ana işlem gönderildi')).toBeNull()
  expect((screen.getByLabelText('Korunan işlem') as HTMLInputElement).value).toBe('Korunacak')
})
it('without TasksAssign offers neither a request control nor directory or mutation IO', async () => {
  const log = setup(undefined, []); mount()
  await screen.findByText(T.needDenied)
  expect(screen.queryByRole('button', { name: T.needOpen })).toBeNull()
  expect(log.people).toHaveLength(0); expect(log.calls).toHaveLength(0)
})
it('requires explicit recipient and deadline and prevents Enter from submitting the enclosing transaction', async () => {
  const log = setup(); mount()
  await userEvent.click(await screen.findByRole('button', { name: T.needOpen }))
  expect((screen.getByLabelText(T.needDue) as HTMLInputElement).value).toBe('')
  await userEvent.type(screen.getByLabelText(T.needSubject), 'Talep{enter}')
  expect(screen.queryByText('Ana işlem gönderildi')).toBeNull()
  expect(screen.getByRole('button', { name: T.needSend }).matches(':disabled')).toBe(true)
  expect(log.calls).toHaveLength(0)
})
it.each([503, 403])('keeps an uncertain %i task frozen across hiding and a true remount, then retries the same key/body', async status => {
  const log = setup((call, index) => index === 1 ? problem(status, null) : json(task(call), 201, {
    'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'original-command',
  }))
  let view = mount(); await fill()
  await userEvent.click(screen.getByRole('button', { name: T.needSend }))
  // 403 uses the real boundary; shell revalidates before the entry can return.
  await waitFor(() => expect(log.calls).toHaveLength(1))
  await waitFor(() => expect(screen.queryByRole('button', { name: T.needOpen })).toBeNull())
  view.unmount(); view = mount()
  await userEvent.click(await screen.findByRole('button', { name: T.needResume }))
  await screen.findByText(T.needUnknown)
  expect(screen.getByLabelText(T.needSubject).matches(':disabled')).toBe(true)
  expect((screen.getByRole('button', { name: 'Ana işlemi gönder' }) as HTMLButtonElement).disabled).toBe(true)
  await userEvent.click(screen.getByRole('button', { name: T.needHide }))
  await userEvent.click(screen.getByRole('button', { name: T.needResume }))
  await userEvent.click(screen.getByRole('button', { name: T.needRetry }))
  await screen.findByText(T.needReplayed)
  expect(log.calls).toHaveLength(2)
  expect(log.calls[1]).toEqual(log.calls[0])
  expect(log.people).toHaveLength(1)
})
it('hides a late task success after a company switch and does not reuse its key/body in the other company', async () => {
  let resolve!: (response: Response) => void
  const answer = new Promise<Response>(done => { resolve = done })
  const log = setup(() => answer); mount(); await fill()
  await userEvent.click(screen.getByRole('button', { name: T.needSend }))
  await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Şirket' }), B_ID)
  await screen.findByRole('button', { name: T.needOpen })
  await act(async () => resolve(json(task(log.calls[0]), 201)))
  expect(screen.queryByText(T.needCreated)).toBeNull()
  await userEvent.click(screen.getByRole('button', { name: T.needOpen }))
  expect((screen.getByLabelText(T.needSubject) as HTMLInputElement).value).toBe('')
  expect(log.calls).toHaveLength(1)
})
it('closes actual UI access on a revoked Assign capability and recovers only after a fresh authorized context', async () => {
  let granted = true
  const log = setup((call, index) => {
    if (index === 1) { granted = false; return problem(403, 'forbidden') }
    return json(task(call), 201, { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'original' })
  }, () => granted ? [WORK_CAPABILITY.assignTask] : [])
  mount(); await fill(); await userEvent.click(screen.getByRole('button', { name: T.needSend }))
  await screen.findByText(T.needDenied)
  expect(screen.queryByRole('button', { name: T.needRetry })).toBeNull()
  expect(log.calls).toHaveLength(1)
  granted = true
  await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Şirket' }), B_ID)
  await screen.findByRole('button', { name: T.needOpen })
  await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Şirket' }), A_ID)
  await userEvent.click(await screen.findByRole('button', { name: T.needResume }))
  await userEvent.click(screen.getByRole('button', { name: T.needRetry }))
  await screen.findByText(T.needReplayed)
  expect(log.calls).toHaveLength(2); expect(log.calls[1]).toEqual(log.calls[0])
})
