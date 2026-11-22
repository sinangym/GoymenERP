import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { ShellProvider } from '../shell/ShellProvider'
import { AppShell } from '../shell/AppShell'
import { COMPANY_A, installFetch, json, problem } from '../settings/testHarness'
import { workRoutes } from './workRoutes'
import { RecordNotesPanel } from './RecordNotesPanel'
import { COLLABORATION_TEXT as T } from './collaborationText'
import { WORK_CAPABILITY } from './capabilities'

/** SENTETIK data only. */
const RECORD = 'cccccccc-0000-4000-8000-000000000001'
const ACTOR = '99999999-9999-4999-8999-999999999999'
const READER = 'eeeeeeee-0000-4000-8000-000000000001'
const note = (overrides: Record<string, unknown> = {}) => ({
  id: 'ffffffff-0000-4000-8000-000000000001', companyId: COMPANY_A, recordType: 'sales-order-draft', recordId: RECORD,
  authorUserId: ACTOR, authorDisplayName: 'SENTETIK Yazar', body: 'SENTETIK teslim notu', correctsNoteId: null, mentionedUserIds: [],
  createdAtUtc: '2026-11-22T09:00:00+00:00', passivatedAtUtc: null, passivationReason: null, readByMeAtUtc: null, reads: [], ...overrides,
})
const notePage = (items: unknown[]) => ({ companyId: COMPANY_A, recordType: 'sales-order-draft', recordId: RECORD, items, hasMore: false })
const task = (overrides: Record<string, unknown> = {}) => ({
  id: '12345678-0000-4000-8000-000000000001', companyId: COMPANY_A, recordType: null, recordId: null, title: 'SENTETIK sayım', details: null,
  assignerUserId: READER, assignerDisplayName: 'SENTETIK Atayan', assigneeUserId: ACTOR, assigneeDisplayName: 'SENTETIK Ben',
  dueAtUtc: '2099-11-23T09:00:00+00:00', status: 'open', version: '1', createdAtUtc: '2026-11-22T09:00:00+00:00',
  closedAtUtc: null, closedByUserId: null, cancelReason: null, ...overrides,
})

afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks(); sessionStorage.clear() })

function mount(path: string, extra: { path: string; element: React.ReactNode }[] = []) {
  const router = createMemoryRouter([{ path: '/', element: <AppShell />, children: [...workRoutes, ...extra] }], { initialEntries: [path] })
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0, staleTime: 0 } } })
  render(<ShellProvider queryClient={queryClient}><RouterProvider router={router} /></ShellProvider>)
}
const mountPanel = () => mount('/not-paneli', [{ path: 'not-paneli', element: <RecordNotesPanel recordType="sales-order-draft" recordId={RECORD} /> }])

describe('W1-C record notes panel', () => {
  it('lists the record notes and saves a new note with a mention under one idempotency key', async () => {
    let saved = false
    const calls = installFetch({ capabilities: [], respond: (url, init) => {
      if (url.includes('/work/people')) return json({ companyId: COMPANY_A, people: [{ userId: READER, displayName: 'SENTETIK Okur' }], hasMore: false })
      if (url.includes(`/work/records/sales-order-draft/${RECORD}/notes`) && init?.method === 'POST') {
        saved = true
        return json(note({ id: 'ffffffff-0000-4000-8000-000000000002', body: 'SENTETIK yeni', mentionedUserIds: [READER] }), 201)
      }
      if (url.includes(`/work/records/sales-order-draft/${RECORD}/notes?`)) return json(notePage(saved ? [note({ id: 'ffffffff-0000-4000-8000-000000000002', body: 'SENTETIK yeni' }), note()] : [note()]))
      return undefined
    } })
    mountPanel()
    expect(await screen.findByText('SENTETIK teslim notu')).toBeTruthy()
    const user = userEvent.setup()
    await user.type(screen.getByLabelText(T.noteLabel), 'SENTETIK yeni')
    await user.click(screen.getAllByRole('button', { name: T.mentionSearch })[0])
    await user.click(await screen.findByRole('button', { name: T.mentionAdd }))
    await user.click(screen.getByRole('button', { name: T.addNote }))
    expect(await screen.findByText(T.noteSaved)).toBeTruthy()
    const post = calls.find((call) => call.method === 'POST' && call.url.includes('/notes'))!
    expect(post.body).toEqual({ body: 'SENTETIK yeni', mentionedUserIds: [READER], correctsNoteId: null })
    expect(post.headers['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/)
    expect(post.headers['X-Company-Id']).toBe(COMPANY_A)
  })

  it('shows the mention refusal by its code and keeps the same key after an unknown outcome', async () => {
    let attempt = 0
    const calls = installFetch({ capabilities: [], respond: (url, init) => {
      if (url.includes('/notes') && init?.method === 'POST') {
        attempt += 1
        return attempt === 1 ? problem(503) : problem(422, { code: 'work_mention_refused' })
      }
      if (url.includes('/notes?')) return json(notePage([]))
      return undefined
    } })
    mountPanel()
    expect(await screen.findByText(T.notesEmpty)).toBeTruthy()
    const user = userEvent.setup()
    await user.type(screen.getByLabelText(T.noteLabel), 'SENTETIK not')
    await user.click(screen.getByRole('button', { name: T.addNote }))
    expect(await screen.findByText(T.unknownOutcome)).toBeTruthy()
    await user.click(screen.getByRole('button', { name: T.addNote }))
    expect(await screen.findByText(T.mentionRefused)).toBeTruthy()
    const keys = calls.filter((call) => call.method === 'POST').map((call) => call.headers['Idempotency-Key'])
    expect(keys).toHaveLength(2)
    expect(keys[0]).toBe(keys[1])
  })

  it('answers a 404 record with the non-disclosing not-found state and renders no form text from another company', async () => {
    installFetch({ capabilities: [], respond: (url) => url.includes('/notes?') ? problem(404) : undefined })
    mountPanel()
    await waitFor(() => expect(screen.getByTestId('work-record-notes').textContent).not.toContain('SENTETIK'))
    expect(screen.queryByText('SENTETIK teslim notu')).toBeNull()
  })

  it('rejects a note page for another record as malformed', async () => {
    installFetch({ capabilities: [], respond: (url) => url.includes('/notes?') ? json({ ...notePage([note()]), recordId: READER }) : undefined })
    mountPanel()
    await screen.findByTestId('work-record-notes')
    await waitFor(() => expect(screen.queryByText('SENTETIK teslim notu')).toBeNull())
  })
})

describe('WP9 note read receipts', () => {
  it('lets a reader mark another person\'s note read without a key and shows who read it', async () => {
    let read = false
    const calls = installFetch({ capabilities: [], respond: (url, init) => {
      if (url.endsWith('/work/notes/ffffffff-0000-4000-8000-000000000001/read') && init?.method === 'POST') {
        read = true
        return json({ noteId: 'ffffffff-0000-4000-8000-000000000001', readAtUtc: '2026-11-22T10:00:00+00:00' })
      }
      if (url.includes('/notes?')) return json(notePage([note({ authorUserId: READER, authorDisplayName: 'SENTETIK Okur',
        readByMeAtUtc: read ? '2026-11-22T10:00:00+00:00' : null,
        reads: read ? [{ userId: ACTOR, displayName: 'SENTETIK Ben', readAtUtc: '2026-11-22T10:00:00+00:00' }] : [] })]))
      return undefined
    } })
    mountPanel()
    expect(await screen.findByText(new RegExp(T.noteNoReaders))).toBeTruthy()
    await userEvent.setup().click(screen.getByRole('button', { name: T.noteMarkRead }))
    expect(await screen.findByText(T.noteReadSaved)).toBeTruthy()
    await waitFor(() => expect(screen.getByTestId('work-note-reads-ffffffff-0000-4000-8000-000000000001').textContent).toContain('SENTETIK Ben'))
    expect(screen.queryByRole('button', { name: T.noteMarkRead })).toBeNull()
    const post = calls.find((call) => call.method === 'POST')!
    expect(post.headers['Idempotency-Key']).toBeUndefined()
    expect(post.headers['X-Company-Id']).toBe(COMPANY_A)
  })

  it('offers no read button on the author\'s own note', async () => {
    installFetch({ capabilities: [], respond: (url) => url.includes('/notes?') ? json(notePage([note()])) : undefined })
    mountPanel()
    expect(await screen.findByText('SENTETIK teslim notu')).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.noteMarkRead })).toBeNull()
  })
})

describe('W1-C my tasks', () => {
  it('lists only my tasks and completes one with its expected version', async () => {
    let done = false
    const calls = installFetch({ capabilities: [WORK_CAPABILITY.myTasks], respond: (url, init) => {
      if (url.includes('/work/tasks/') && url.endsWith('/completion') && init?.method === 'POST') {
        done = true
        return json(task({ status: 'completed', version: '2', closedAtUtc: '2026-11-22T10:00:00+00:00', closedByUserId: ACTOR }))
      }
      if (url.includes('/work/tasks/mine')) return json({ companyId: COMPANY_A, items: done ? [] : [task()], hasMore: false })
      return undefined
    } })
    mount('/gorevlerim')
    expect(await screen.findByText('SENTETIK sayım')).toBeTruthy()
    await userEvent.setup().click(screen.getByRole('button', { name: T.complete }))
    expect(await screen.findByText(T.taskCompleted)).toBeTruthy()
    const post = calls.find((call) => call.method === 'POST')!
    expect(post.body).toEqual({ expectedVersion: '1' })
    expect(calls.some((call) => call.url.includes('assignee'))).toBe(false)
  })

  it('shows a stale version refusal by its code', async () => {
    installFetch({ capabilities: [WORK_CAPABILITY.myTasks], respond: (url, init) => {
      if (url.endsWith('/completion') && init?.method === 'POST') return problem(409, { code: 'version_conflict' })
      if (url.includes('/work/tasks/mine')) return json({ companyId: COMPANY_A, items: [task()], hasMore: false })
      return undefined
    } })
    mount('/gorevlerim')
    await userEvent.setup().click(await screen.findByRole('button', { name: T.complete }))
    expect(await screen.findByText(T.versionConflict)).toBeTruthy()
  })

  it('shows the filtered empty state when no open task exists', async () => {
    installFetch({ capabilities: [WORK_CAPABILITY.myTasks], respond: (url) => url.includes('/work/tasks/mine') ? json({ companyId: COMPANY_A, items: [], hasMore: false }) : undefined })
    mount('/gorevlerim')
    expect(await screen.findByRole('heading', { name: T.tasksHeading })).toBeTruthy()
    await waitFor(() => expect(screen.queryByRole('table')).toBeNull())
  })

  it('WP9: without the work.tasks.mine capability the page shows the neutral not-found block and reads no task', async () => {
    const calls = installFetch({ capabilities: [], respond: (url) => url.includes('/work/tasks/mine') ? json({ companyId: COMPANY_A, items: [task()], hasMore: false }) : undefined })
    mount('/gorevlerim')
    await waitFor(() => expect(calls.some((call) => call.url.endsWith('/shell/capabilities'))).toBe(true))
    await waitFor(() => expect(screen.queryByRole('heading', { name: T.tasksHeading })).toBeNull())
    expect(screen.queryByText('SENTETIK sayım')).toBeNull()
    expect(calls.some((call) => call.url.includes('/work/tasks/mine'))).toBe(false)
  })
})

describe('W1-C collaboration inbox', () => {
  it('shows the now class with its unread count and marks an item read', async () => {
    let read = false
    const item = { id: '87654321-0000-4000-8000-000000000001', companyId: COMPANY_A, kind: 'mention', class: 'now', sourceId: 'ffffffff-0000-4000-8000-000000000001',
      recordType: 'sales-order-draft', recordId: RECORD, actorUserId: READER, recordedAtUtc: '2026-11-22T09:00:00+00:00', visibleAtUtc: '2026-11-22T09:00:00+00:00' }
    const calls = installFetch({ capabilities: [], respond: (url, init) => {
      if (url.includes('/work/notifications?')) return json({ companyId: COMPANY_A, items: [], hasMore: false, nextCursor: null })
      if (url.endsWith('/read') && init?.method === 'POST') { read = true; return json({ id: item.id, readAtUtc: '2026-11-22T09:05:00+00:00' }) }
      if (url.includes('/work/inbox-notifications?')) return json({ companyId: COMPANY_A, items: [{ ...item, readAtUtc: read ? '2026-11-22T09:05:00+00:00' : null }],
        hasMore: false, unreadNowCount: read ? 0 : 1, unreadDigestCount: 0 })
      return undefined
    } })
    mount('/bildirimlerim')
    expect(await screen.findByText(T.kindMention)).toBeTruthy()
    expect(screen.getByRole('button', { name: new RegExp(`${T.classNow} \\(1`) })).toBeTruthy()
    await userEvent.setup().click(screen.getByRole('button', { name: T.markRead }))
    await waitFor(() => expect(screen.queryByRole('button', { name: T.markRead })).toBeNull())
    expect(screen.getByTestId(`work-inbox-${item.id}`).textContent).toContain(T.read)
    expect(calls.some((call) => call.url.includes('notificationClass=now'))).toBe(true)
  })

  it('reads the digest class separately and says when nothing was released yet', async () => {
    const calls = installFetch({ capabilities: [], respond: (url) => {
      if (url.includes('/work/notifications?')) return json({ companyId: COMPANY_A, items: [], hasMore: false, nextCursor: null })
      if (url.includes('/work/inbox-notifications?')) return json({ companyId: COMPANY_A, items: [], hasMore: false, unreadNowCount: 0, unreadDigestCount: 0 })
      return undefined
    } })
    mount('/bildirimlerim')
    await userEvent.setup().click(await screen.findByRole('button', { name: new RegExp(T.classDigest) }))
    expect(await screen.findByText(T.inboxEmptyDigest)).toBeTruthy()
    expect(calls.some((call) => call.url.includes('notificationClass=digest'))).toBe(true)
  })
})
