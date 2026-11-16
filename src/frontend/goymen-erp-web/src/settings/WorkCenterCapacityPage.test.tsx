/**
 * URT-D89 component cases — Ayarlar > İş merkezi kapasitesi.
 *
 * COMPONENT/DOM EVIDENCE ONLY: `fetch` is stubbed with SENTETIK fixtures; the real API, SQL and audit are the backend's
 * Settings work center capacity tests. What these cases establish: the states (loading, no work centers, unavailable with
 * retry, an off-contract answer, a refused work center list, an ungranted route), that a work center without a capacity
 * says "hesaplanamaz" and a missing buffer reads as zero, that nothing is pre-filled with an invented value, what is SENT
 * (company header, Idempotency-Key, the work center in the path, the exact body with the work center's expected version
 * and a null buffer pair), a Turkish decimal comma read losslessly, invalid input held back, the same key on a retry after
 * an unknown outcome, the replay notice, the stale refusals that close the form and re-read, and a definite refusal kept
 * inside the form.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { COMPANY_A, IDENTITY, commands, installFetch, json, noContent, problem, renderAt } from './testHarness'
import { SETTINGS_CAPABILITY } from './capabilities'
import { WORK_CENTER_CAPACITY_TEXT as T, safetyBufferLabel } from './workCenterCapacityText'

const PATH = '/ayarlar/is-merkezi-kapasitesi'
const PROFILE_1 = 'cccccccc-0000-4000-8000-00000000000c'
const PROFILE_2 = 'cccccccc-0000-4000-8000-00000000000d'
const LOCATION = 'dddddddd-0000-4000-8000-00000000000d'
const PRESS = 'eeeeeeee-0000-4000-8000-00000000000e'
const LINE = 'ffffffff-0000-4000-8000-00000000000f'

function reset() {
  vi.unstubAllGlobals()
  document.body.innerHTML = ''
}

afterEach(reset)

const isRead = (url: string) => url.endsWith('/settings/work-center-capacities')
const isWorkCenterRead = (url: string) => url.endsWith('/organization/work-centers')
const isRecord = (url: string, init: RequestInit | undefined) =>
  init?.method === 'POST' && /\/settings\/work-center-capacities\/[0-9a-f-]+\/versions$/.test(url)

function workCenter(id: string, code: string, name: string, isActive = true) {
  return { id, locationId: LOCATION, code, name, kind: 'Production', isActive, canOpenLocationDetail: false }
}

const workCenters = () => [workCenter(PRESS, 'PRES-1', 'Pres hattı'), workCenter(LINE, 'DLM-1', 'Dilme hattı')]

function entry(
  profileId: string,
  version: string,
  capacity: string,
  bufferValue: string | null,
  bufferUnit: string | null,
  isCurrent: boolean,
) {
  return {
    profileId,
    version,
    inputCapacityKgPerHour: capacity,
    safetyBufferValue: bufferValue,
    safetyBufferUnit: bufferUnit,
    isCurrent,
    recordedBy: IDENTITY.userId,
    recordedAtUtc: '2026-11-15T09:00:00+00:00',
    reason: `SENTETIK sürüm ${version}`,
  }
}

/** The press has two versions (current 1250.5 kg/h with a 6-hour buffer); the slitting line has no capacity. */
function recordedView() {
  return {
    capacityUnit: 'kg/h',
    workCenters: [
      {
        workCenterId: PRESS,
        version: '2',
        versions: [entry(PROFILE_2, '2', '1250.5', '6', 'Hours', true), entry(PROFILE_1, '1', '900', null, null, false)],
      },
    ],
  }
}

const emptyView = () => ({ capacityUnit: 'kg/h', workCenters: [] })

function install(
  respond: (url: string, init: RequestInit | undefined) => Response | Promise<Response> | undefined,
  centers: () => Response = () => json(workCenters()),
) {
  return installFetch({
    capabilities: [SETTINGS_CAPABILITY.workCenterCapacity],
    respond: (url, init) => (isWorkCenterRead(url) ? centers() : respond(url, init)),
  })
}

async function rowOf(name: string) {
  const table = await screen.findByRole('table', { name: T.tableLabel })
  const row = within(table).getByText(name).closest('tr')
  expect(row).not.toBeNull()
  return row as HTMLTableRowElement
}

async function openForm(user: ReturnType<typeof userEvent.setup>, name: string) {
  await user.click(await screen.findByRole('button', { name: `${name}: ${T.open}` }))
  await user.click(await screen.findByRole('button', { name: T.newVersion }))
}

describe('İş merkezi kapasitesi — reads', () => {
  it('shows the loading state while the read is open', async () => {
    install((url) => (isRead(url) ? new Promise<Response>(() => undefined) : undefined))
    renderAt(PATH)
    await waitFor(() => expect(document.querySelector('[data-state="initial-loading"]')).not.toBeNull())
  })

  it('lists every work center with its current capacity, a missing one as not computable and no buffer as zero', async () => {
    const recorded = install((url) => (isRead(url) ? json(recordedView()) : undefined))
    renderAt(PATH)

    const press = await rowOf('Pres hattı')
    expect(within(press).getByText('1250,5')).toBeTruthy()
    expect(within(press).getByText(safetyBufferLabel('6', 'Hours'))).toBeTruthy()
    expect(within(press).getByText('2')).toBeTruthy()
    const line = await rowOf('Dilme hattı')
    expect(within(line).getByText(T.noCapacity)).toBeTruthy()

    for (const path of ['/settings/work-center-capacities', '/organization/work-centers']) {
      const read = recorded.find((request) => request.url.endsWith(path))
      expect(read?.headers['X-Company-Id']).toBe(COMPANY_A)
    }
    expect(commands(recorded)).toEqual([])
  })

  it('shows a work center history newest first, the older version without a buffer reading as zero', async () => {
    const user = userEvent.setup()
    install((url) => (isRead(url) ? json(recordedView()) : undefined))
    renderAt(PATH)

    await user.click(await screen.findByRole('button', { name: `Pres hattı: ${T.open}` }))
    const history = screen.getByRole('table', { name: T.historyHeading('Pres hattı') })
    const rows = within(history).getAllByRole('row')
    expect(rows).toHaveLength(3)
    expect(within(rows[1]).getByText(T.isCurrent)).toBeTruthy()
    expect(within(rows[2]).getByText(T.isPrevious)).toBeTruthy()
    expect(within(rows[2]).getByText(T.noBuffer)).toBeTruthy()
  })

  it('says so when the company has no work center', async () => {
    install(
      (url) => (isRead(url) ? json(emptyView()) : undefined),
      () => json([]),
    )
    renderAt(PATH)
    expect(await screen.findByText(T.empty)).toBeTruthy()
    expect(screen.queryByRole('table', { name: T.tableLabel })).toBeNull()
  })

  it('offers a retry when the source is unavailable, and reads again on it', async () => {
    const user = userEvent.setup()
    let reads = 0
    install((url) => {
      if (!isRead(url)) return undefined
      reads += 1
      return reads === 1 ? problem(503, { code: 'settings_provider_unavailable' }) : json(recordedView())
    })
    renderAt(PATH)

    await user.click(await screen.findByRole('button', { name: 'Tekrar dene' }))
    expect(await screen.findByRole('table', { name: T.tableLabel })).toBeTruthy()
  })

  it('shows the work center read state instead of a list it cannot name', async () => {
    install(
      (url) => (isRead(url) ? json(recordedView()) : undefined),
      () => problem(503),
    )
    renderAt(PATH)
    await waitFor(() => expect(document.querySelector('[data-state="service-unavailable"]')).not.toBeNull())
    expect(screen.queryByRole('table', { name: T.tableLabel })).toBeNull()
  })

  it('shows nothing of an off-contract answer', async () => {
    const view = recordedView()
    const press = view.workCenters[0]
    const offContract = [
      // two current entries
      { ...view, workCenters: [{ ...press, versions: [press.versions[0], { ...press.versions[1], isCurrent: true }] }] },
      // a buffer value without its unit
      { ...view, workCenters: [{ ...press, versions: [{ ...press.versions[0], safetyBufferUnit: null }, press.versions[1]] }] },
      // an unknown buffer unit
      { ...view, workCenters: [{ ...press, versions: [{ ...press.versions[0], safetyBufferUnit: 'Weeks' }, press.versions[1]] }] },
      // a zero capacity, and a capacity sent as a JSON number
      { ...view, workCenters: [{ ...press, versions: [{ ...press.versions[0], inputCapacityKgPerHour: '0' }, press.versions[1]] }] },
      { ...view, workCenters: [{ ...press, versions: [{ ...press.versions[0], inputCapacityKgPerHour: 1250.5 }, press.versions[1]] }] },
      // a listed work center with an empty timeline, the same work center twice, another unit, an extra key
      { ...view, workCenters: [{ ...press, version: '0', versions: [] }] },
      { ...view, workCenters: [press, press] },
      { ...view, capacityUnit: 'ton/h' },
      { ...view, safetyBufferValue: '0' },
    ]
    for (const body of offContract) {
      install((url) => (isRead(url) ? json(body) : undefined))
      renderAt(PATH)
      await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
      expect(screen.queryByRole('table', { name: T.tableLabel })).toBeNull()
      reset()
    }
  })

  it('an ungranted screen calls nothing', async () => {
    const recorded = installFetch({ capabilities: [], respond: () => json(recordedView()) })
    renderAt(PATH)
    await waitFor(() => expect(recorded.some((request) => request.url.endsWith('/shell/capabilities'))).toBe(true))
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(recorded.some((request) => request.url.includes('/settings/'))).toBe(false)
    expect(recorded.some((request) => request.url.endsWith('/organization/work-centers'))).toBe(false)
  })
})

describe('İş merkezi kapasitesi — commands', () => {
  it('starts a first capacity empty with no buffer, and sends what was typed with a Turkish decimal comma', async () => {
    const user = userEvent.setup()
    let reads = 0
    const recorded = install((url, init) => {
      if (isRead(url)) {
        reads += 1
        return json(recordedView())
      }
      return isRecord(url, init) ? noContent() : undefined
    })
    renderAt(PATH)

    await openForm(user, 'Dilme hattı')
    const capacity = screen.getByLabelText(T.capacityLabel, { exact: false }) as HTMLInputElement
    expect(capacity.value).toBe('')
    expect((screen.getByRole('radio', { name: T.bufferNone }) as HTMLInputElement).checked).toBe(true)

    await user.type(capacity, ' 800,25 ')
    await user.type(screen.getByLabelText(T.reasonLabel, { exact: false }), '  SENTETIK gerekçe  ')
    await user.click(screen.getByRole('button', { name: T.save }))

    expect(await screen.findByText(T.saved)).toBeTruthy()
    const [command] = commands(recorded)
    expect(command.method).toBe('POST')
    expect(command.url).toMatch(new RegExp(`/settings/work-center-capacities/${LINE}/versions$`))
    expect(command.body).toEqual({
      expectedVersion: '0',
      inputCapacityKgPerHour: '800.25',
      safetyBufferValue: null,
      safetyBufferUnit: null,
      reason: 'SENTETIK gerekçe',
    })
    expect(command.headers['X-Company-Id']).toBe(COMPANY_A)
    expect(command.headers['Idempotency-Key']).toMatch(/.+/)
    await waitFor(() => expect(reads).toBeGreaterThanOrEqual(2))
  })

  it('pre-fills from the current version, sends its version as the expected one and a day buffer with its unit', async () => {
    const user = userEvent.setup()
    const recorded = install((url, init) => {
      if (isRead(url)) return json(recordedView())
      return isRecord(url, init) ? noContent() : undefined
    })
    renderAt(PATH)

    await openForm(user, 'Pres hattı')
    expect((screen.getByLabelText(T.capacityLabel, { exact: false }) as HTMLInputElement).value).toBe('1250,5')
    expect((screen.getByRole('radio', { name: 'saat' }) as HTMLInputElement).checked).toBe(true)
    await user.click(screen.getByRole('radio', { name: 'gün' }))
    expect(screen.getByText(T.bufferDaysNote)).toBeTruthy()
    const bufferInput = screen.getByLabelText(T.bufferValueLabel, { exact: false })
    await user.clear(bufferInput)
    await user.type(bufferInput, '1.5')
    await user.type(screen.getByLabelText(T.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: T.save }))

    expect(await screen.findByText(T.saved)).toBeTruthy()
    const [command] = commands(recorded)
    expect(command.url).toMatch(new RegExp(`/settings/work-center-capacities/${PRESS}/versions$`))
    expect(command.body).toEqual({
      expectedVersion: '2',
      inputCapacityKgPerHour: '1250.5',
      safetyBufferValue: '1.5',
      safetyBufferUnit: 'Days',
      reason: 'SENTETIK',
    })
  })

  it('removing the buffer sends the null pair', async () => {
    const user = userEvent.setup()
    const recorded = install((url, init) => {
      if (isRead(url)) return json(recordedView())
      return isRecord(url, init) ? noContent() : undefined
    })
    renderAt(PATH)

    await openForm(user, 'Pres hattı')
    await user.click(screen.getByRole('radio', { name: T.bufferNone }))
    expect(screen.queryByLabelText(T.bufferValueLabel, { exact: false })).toBeNull()
    await user.type(screen.getByLabelText(T.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: T.save }))

    expect(await screen.findByText(T.saved)).toBeTruthy()
    expect(commands(recorded)[0].body).toMatchObject({ safetyBufferValue: null, safetyBufferUnit: null })
  })

  it('holds back a capacity or buffer the server would not read', async () => {
    const user = userEvent.setup()
    const recorded = install((url, init) => {
      if (isRead(url)) return json(recordedView())
      return isRecord(url, init) ? noContent() : undefined
    })
    renderAt(PATH)

    await openForm(user, 'Pres hattı')
    const capacity = screen.getByLabelText(T.capacityLabel, { exact: false })
    await user.clear(capacity)
    await user.type(capacity, '0')
    expect(screen.getByText(T.capacityError)).toBeTruthy()
    await user.clear(capacity)
    await user.type(capacity, '12.1234567')
    expect(screen.getByText(T.capacityError)).toBeTruthy()
    await user.clear(capacity)
    await user.type(capacity, '100')
    const bufferInput = screen.getByLabelText(T.bufferValueLabel, { exact: false })
    await user.clear(bufferInput)
    await user.type(bufferInput, '-1')
    expect(screen.getByText(T.bufferError)).toBeTruthy()
    await user.type(screen.getByLabelText(T.reasonLabel, { exact: false }), 'SENTETIK')
    fireEvent.submit(screen.getByRole('button', { name: T.save }).closest('form') as HTMLFormElement)
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(commands(recorded)).toEqual([])
  })

  it('keeps the same Idempotency-Key for the retry after an unknown outcome and shows the replay notice', async () => {
    const user = userEvent.setup()
    let attempts = 0
    const recorded = install((url, init) => {
      if (isRead(url)) return json(recordedView())
      if (!isRecord(url, init)) return undefined
      attempts += 1
      return attempts === 1 ? problem(503) : noContent({ 'Idempotency-Replayed': 'true' })
    })
    renderAt(PATH)

    await openForm(user, 'Pres hattı')
    await user.type(screen.getByLabelText(T.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: T.save }))
    expect(await screen.findByText(T.unknownOutcome)).toBeTruthy()
    await user.click(screen.getByRole('button', { name: T.retrySame }))

    expect(await screen.findByText(T.savedReplay)).toBeTruthy()
    const [first, second] = commands(recorded)
    expect(second.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])
    expect(second.body).toEqual(first.body)
  })

  it.each([
    [409, 'settings_work_center_capacity_version_conflict', T.staleReloaded],
    [404, 'settings_work_center_capacity_work_center_not_found', T.workCenterGone],
  ])('closes the form and reads again on %s %s', async (status, code, text) => {
    const user = userEvent.setup()
    let reads = 0
    install((url, init) => {
      if (isRead(url)) {
        reads += 1
        return json(recordedView())
      }
      return isRecord(url, init) ? problem(status, { code }) : undefined
    })
    renderAt(PATH)

    await openForm(user, 'Pres hattı')
    await user.type(screen.getByLabelText(T.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: T.save }))

    expect(await screen.findByText(text)).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.save })).toBeNull()
    await waitFor(() => expect(reads).toBeGreaterThanOrEqual(2))
  })

  it('shows a definite refusal inside the form and keeps it open', async () => {
    const user = userEvent.setup()
    install((url, init) => {
      if (isRead(url)) return json(recordedView())
      return isRecord(url, init) ? problem(403, { code: 'settings_access_denied' }) : undefined
    })
    renderAt(PATH)

    await openForm(user, 'Pres hattı')
    await user.type(screen.getByLabelText(T.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: T.save }))

    expect((await screen.findByRole('alert')).textContent).toBe('Bu işlem için yetkiniz yok.')
    expect(screen.getByRole('button', { name: T.save })).toBeTruthy()
  })
})
