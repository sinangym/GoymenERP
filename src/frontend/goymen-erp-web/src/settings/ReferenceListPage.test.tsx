/**
 * API-98/B-14 component cases — Ayarlar > Masraf ve hizmet tipleri and Ayarlar > Boya rengi ve hurda türü.
 *
 * COMPONENT/DOM EVIDENCE ONLY: `fetch` is stubbed with SENTETIK fixtures; the real API, SQL and audit are the backend's
 * Settings reference list tests. What these cases establish: the states (loading, empty with no suggested entry,
 * unavailable with retry, an off-contract answer, an ungranted route), that each screen reads only its own lists and its
 * own name opens it, what is SENT (company header, Idempotency-Key, the list and entry in the path, the exact body with the
 * expected version), invalid input held back, the same key on a retry after an unknown outcome, the replay notice, the
 * stale refusals that close the form and re-read, and a code conflict kept inside the form.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { COMPANY_A, IDENTITY, commands, installFetch, json, noContent, problem, renderAt } from './testHarness'
import { SETTINGS_CAPABILITY } from './capabilities'
import { REFERENCE_LIST_GROUP_TEXT, REFERENCE_LIST_LABEL, REFERENCE_LIST_TEXT as T } from './referenceListText'

const PURCHASING_PATH = '/ayarlar/referans-listeleri'
const PRODUCTION_PATH = '/ayarlar/uretim-referans-listeleri'
const ENTRY_1 = 'cccccccc-0000-4000-8000-00000000000c'
const ENTRY_2 = 'cccccccc-0000-4000-8000-00000000000d'

function reset() {
  vi.unstubAllGlobals()
  document.body.innerHTML = ''
}

afterEach(reset)

const isListRead = (url: string, slug: string, init: RequestInit | undefined) =>
  (init?.method ?? 'GET') === 'GET' && url.endsWith(`/settings/reference-lists/${slug}`)
const isCreate = (url: string, slug: string, init: RequestInit | undefined) =>
  init?.method === 'POST' && url.endsWith(`/settings/reference-lists/${slug}/entries`)
const isVersion = (url: string, slug: string, init: RequestInit | undefined) =>
  init?.method === 'POST' && new RegExp(`/settings/reference-lists/${slug}/entries/[0-9a-f-]+/versions$`).test(url)

function entry(id: string, code: string, name: string, active = true, version = '1') {
  return { id, code, name, active, version, recordedBy: IDENTITY.userId, recordedAtUtc: '2026-11-15T09:00:00+00:00' }
}

const expenseView = () => ({
  kind: 'ExpenseType',
  entries: [entry(ENTRY_1, 'NAKLIYE', 'SENTETIK nakliye', true, '2'), entry(ENTRY_2, 'GUMRUK', 'SENTETIK gümrük', false)],
})
const emptyView = (kind: string) => ({ kind, entries: [] })

function installPurchasing(respond: (url: string, init: RequestInit | undefined) => Response | Promise<Response> | undefined) {
  return installFetch({
    capabilities: [SETTINGS_CAPABILITY.purchasingReferenceLists],
    respond: (url, init) => respond(url, init) ?? (isListRead(url, 'service-types', init) ? json(emptyView('ServiceType')) : undefined),
  })
}

async function expenseSection() {
  return (await screen.findByRole('heading', { name: REFERENCE_LIST_LABEL.expenseType })).closest('section') as HTMLElement
}

describe('Reference lists — reads', () => {
  it('shows the loading state while a read is open', async () => {
    installPurchasing((url, init) => (isListRead(url, 'expense-types', init) ? new Promise<Response>(() => undefined) : undefined))
    renderAt(PURCHASING_PATH)
    await waitFor(() => expect(document.querySelector('[data-state="initial-loading"]')).not.toBeNull())
  })

  it('reads the expense and service type lists only, with the company header, and shows each entry', async () => {
    const recorded = installPurchasing((url, init) => (isListRead(url, 'expense-types', init) ? json(expenseView()) : undefined))
    renderAt(PURCHASING_PATH)

    expect(await screen.findByRole('heading', { name: REFERENCE_LIST_GROUP_TEXT.purchasing.title })).toBeTruthy()
    const table = await screen.findByRole('table', { name: REFERENCE_LIST_LABEL.expenseType })
    const active = within(table).getByText('NAKLIYE').closest('tr') as HTMLTableRowElement
    expect(within(active).getByText(T.active)).toBeTruthy()
    expect(within(active).getByText('2')).toBeTruthy()
    const inactive = within(table).getByText('GUMRUK').closest('tr') as HTMLTableRowElement
    expect(within(inactive).getByText(T.inactive)).toBeTruthy()

    const reads = recorded.filter((request) => request.url.includes('/settings/reference-lists/'))
    expect(reads.map((request) => request.url.split('/settings/reference-lists/')[1]).sort()).toEqual([
      'expense-types',
      'service-types',
    ])
    for (const read of reads) expect(read.headers['X-Company-Id']).toBe(COMPANY_A)
    expect(commands(recorded)).toEqual([])
  })

  it('says an empty list is empty and suggests no entry', async () => {
    installPurchasing((url, init) => (isListRead(url, 'expense-types', init) ? json(emptyView('ExpenseType')) : undefined))
    renderAt(PURCHASING_PATH)
    expect(await screen.findAllByText(T.empty)).toHaveLength(2)
    expect(screen.queryByRole('table')).toBeNull()
  })

  it('offers a retry when the source is unavailable, and reads again on it', async () => {
    const user = userEvent.setup()
    let reads = 0
    installPurchasing((url, init) => {
      if (!isListRead(url, 'expense-types', init)) return undefined
      reads += 1
      return reads === 1 ? problem(503, { code: 'settings_provider_unavailable' }) : json(expenseView())
    })
    renderAt(PURCHASING_PATH)

    await user.click(await screen.findByRole('button', { name: 'Tekrar dene' }))
    expect(await screen.findByRole('table', { name: REFERENCE_LIST_LABEL.expenseType })).toBeTruthy()
  })

  it('shows nothing of an off-contract answer', async () => {
    const view = expenseView()
    const offContract = [
      { ...view, kind: 'ServiceType' },
      { ...view, kind: 'OperationType' },
      { ...view, entries: [view.entries[0], { ...view.entries[1], code: 'NAKLIYE' }] },
      { ...view, entries: [view.entries[0], { ...view.entries[1], id: ENTRY_1 }] },
      { ...view, entries: [{ ...view.entries[0], code: 'nakliye' }] },
      { ...view, entries: [{ ...view.entries[0], name: ' SENTETIK ' }] },
      { ...view, entries: [{ ...view.entries[0], version: 2 }] },
      { ...view, entries: [{ ...view.entries[0], version: '0' }] },
      { ...view, entries: [{ ...view.entries[0], extra: true }] },
      { ...view, seeded: true },
    ]
    for (const body of offContract) {
      installPurchasing((url, init) => (isListRead(url, 'expense-types', init) ? json(body) : undefined))
      renderAt(PURCHASING_PATH)
      await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
      expect(screen.queryByRole('table', { name: REFERENCE_LIST_LABEL.expenseType })).toBeNull()
      reset()
    }
  })

  it('an ungranted screen calls nothing, and the purchasing name does not open the production screen', async () => {
    const recorded = installPurchasing(() => json(expenseView()))
    renderAt(PRODUCTION_PATH)
    await waitFor(() => expect(recorded.some((request) => request.url.endsWith('/shell/capabilities'))).toBe(true))
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(recorded.some((request) => request.url.includes('/settings/'))).toBe(false)
  })

  it('reads the paint color and scrap kind lists on the production screen', async () => {
    const recorded = installFetch({
      capabilities: [SETTINGS_CAPABILITY.productionReferenceLists],
      respond: (url, init) => {
        if (isListRead(url, 'paint-colors', init)) return json(emptyView('PaintColor'))
        if (isListRead(url, 'scrap-kinds', init)) return json(emptyView('ScrapKind'))
        return undefined
      },
    })
    renderAt(PRODUCTION_PATH)
    expect(await screen.findByRole('heading', { name: REFERENCE_LIST_LABEL.paintColor })).toBeTruthy()
    expect(await screen.findAllByText(T.empty)).toHaveLength(2)
    const reads = recorded.filter((request) => request.url.includes('/settings/reference-lists/'))
    expect(reads.map((request) => request.url.split('/settings/reference-lists/')[1]).sort()).toEqual([
      'paint-colors',
      'scrap-kinds',
    ])
  })
})

describe('Reference lists — commands', () => {
  it('adds an entry from empty fields and sends exactly what was typed', async () => {
    const user = userEvent.setup()
    let reads = 0
    const recorded = installPurchasing((url, init) => {
      if (isListRead(url, 'expense-types', init)) {
        reads += 1
        return json(emptyView('ExpenseType'))
      }
      return isCreate(url, 'expense-types', init) ? noContent() : undefined
    })
    renderAt(PURCHASING_PATH)

    const section = await expenseSection()
    await user.click(await within(section).findByRole('button', { name: T.add }))
    const code = within(section).getByLabelText(T.codeLabel, { exact: false }) as HTMLInputElement
    const name = within(section).getByLabelText(T.nameLabel, { exact: false }) as HTMLInputElement
    expect(code.value).toBe('')
    expect(name.value).toBe('')
    await user.type(code, 'NAKLIYE')
    await user.type(name, '  SENTETIK nakliye  ')
    await user.type(within(section).getByLabelText(T.reasonLabel, { exact: false }), '  SENTETIK gerekçe  ')
    await user.click(within(section).getByRole('button', { name: T.save }))

    expect(await within(section).findByText(T.saved)).toBeTruthy()
    const [command] = commands(recorded)
    expect(command.method).toBe('POST')
    expect(command.url).toMatch(/\/settings\/reference-lists\/expense-types\/entries$/)
    expect(command.body).toEqual({ code: 'NAKLIYE', name: 'SENTETIK nakliye', reason: 'SENTETIK gerekçe' })
    expect(command.headers['X-Company-Id']).toBe(COMPANY_A)
    expect(command.headers['Idempotency-Key']).toMatch(/.+/)
    await waitFor(() => expect(reads).toBeGreaterThanOrEqual(2))
  })

  it('holds back an invalid code', async () => {
    const user = userEvent.setup()
    const recorded = installPurchasing((url, init) =>
      isListRead(url, 'expense-types', init) ? json(emptyView('ExpenseType')) : undefined,
    )
    renderAt(PURCHASING_PATH)

    const section = await expenseSection()
    await user.click(await within(section).findByRole('button', { name: T.add }))
    await user.type(within(section).getByLabelText(T.codeLabel, { exact: false }), 'nakliye')
    await user.type(within(section).getByLabelText(T.nameLabel, { exact: false }), 'SENTETIK')
    await user.type(within(section).getByLabelText(T.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(within(section).getByRole('button', { name: T.save }))

    expect(within(section).getByText(T.codeError)).toBeTruthy()
    expect(commands(recorded)).toEqual([])
  })

  it('keeps a code conflict inside the form', async () => {
    const user = userEvent.setup()
    installPurchasing((url, init) => {
      if (isListRead(url, 'expense-types', init)) return json(expenseView())
      return isCreate(url, 'expense-types', init)
        ? problem(409, { code: 'settings_reference_list_code_conflict' })
        : undefined
    })
    renderAt(PURCHASING_PATH)

    const section = await expenseSection()
    await user.click(await within(section).findByRole('button', { name: T.add }))
    await user.type(within(section).getByLabelText(T.codeLabel, { exact: false }), 'NAKLIYE')
    await user.type(within(section).getByLabelText(T.nameLabel, { exact: false }), 'SENTETIK')
    await user.type(within(section).getByLabelText(T.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(within(section).getByRole('button', { name: T.save }))

    expect((await within(section).findByRole('alert')).textContent).toMatch(/zaten kullanılıyor/)
    expect(within(section).getByRole('button', { name: T.save })).toBeTruthy()
  })

  it('records a new version from the stored values with the entry version as the expected one', async () => {
    const user = userEvent.setup()
    const recorded = installPurchasing((url, init) => {
      if (isListRead(url, 'expense-types', init)) return json(expenseView())
      return isVersion(url, 'expense-types', init) ? noContent() : undefined
    })
    renderAt(PURCHASING_PATH)

    const section = await expenseSection()
    await user.click(await within(section).findByRole('button', { name: `NAKLIYE: ${T.edit}` }))
    const name = within(section).getByLabelText(T.nameLabel, { exact: false }) as HTMLInputElement
    expect(name.value).toBe('SENTETIK nakliye')
    const active = within(section).getByRole('checkbox', { name: T.activeLabel }) as HTMLInputElement
    expect(active.checked).toBe(true)
    await user.click(active)
    await user.type(within(section).getByLabelText(T.reasonLabel, { exact: false }), 'SENTETIK pasif')
    await user.click(within(section).getByRole('button', { name: T.save }))

    expect(await within(section).findByText(T.saved)).toBeTruthy()
    const [command] = commands(recorded)
    expect(command.url).toMatch(new RegExp(`/settings/reference-lists/expense-types/entries/${ENTRY_1}/versions$`))
    expect(command.body).toEqual({
      expectedVersion: '2',
      name: 'SENTETIK nakliye',
      active: false,
      reason: 'SENTETIK pasif',
    })
  })

  it('keeps the same Idempotency-Key for the retry after an unknown outcome and shows the replay notice', async () => {
    const user = userEvent.setup()
    let attempts = 0
    const recorded = installPurchasing((url, init) => {
      if (isListRead(url, 'expense-types', init)) return json(expenseView())
      if (!isVersion(url, 'expense-types', init)) return undefined
      attempts += 1
      return attempts === 1 ? problem(503) : noContent({ 'Idempotency-Replayed': 'true' })
    })
    renderAt(PURCHASING_PATH)

    const section = await expenseSection()
    await user.click(await within(section).findByRole('button', { name: `GUMRUK: ${T.edit}` }))
    await user.type(within(section).getByLabelText(T.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(within(section).getByRole('button', { name: T.save }))
    expect(await within(section).findByText(T.unknownOutcome)).toBeTruthy()
    await user.click(within(section).getByRole('button', { name: T.retrySame }))

    expect(await within(section).findByText(T.savedReplay)).toBeTruthy()
    const sent = commands(recorded)
    expect(sent).toHaveLength(2)
    expect(sent[1].headers['Idempotency-Key']).toBe(sent[0].headers['Idempotency-Key'])
  })

  it('closes the form and reads again after a version conflict', async () => {
    const user = userEvent.setup()
    let reads = 0
    installPurchasing((url, init) => {
      if (isListRead(url, 'expense-types', init)) {
        reads += 1
        return json(expenseView())
      }
      return isVersion(url, 'expense-types', init)
        ? problem(409, { code: 'settings_reference_list_version_conflict' })
        : undefined
    })
    renderAt(PURCHASING_PATH)

    const section = await expenseSection()
    await user.click(await within(section).findByRole('button', { name: `NAKLIYE: ${T.edit}` }))
    await user.type(within(section).getByLabelText(T.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(within(section).getByRole('button', { name: T.save }))

    expect(await within(section).findByText(T.staleReloaded)).toBeTruthy()
    expect(within(section).queryByRole('button', { name: T.save })).toBeNull()
    await waitFor(() => expect(reads).toBeGreaterThanOrEqual(2))
  })
})
