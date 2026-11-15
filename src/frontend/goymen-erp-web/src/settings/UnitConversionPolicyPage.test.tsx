/**
 * F-004 / KIM-D37 component cases — Ayarlar > Birim çevrim politikası.
 *
 * COMPONENT/DOM EVIDENCE ONLY: `fetch` is stubbed with SENTETIK fixtures; the real API, SQL and the dependent
 * calculations' refusal are the backend's Settings unit conversion policy tests. What these cases establish: the states
 * (loading, empty naming the fail-closed refusal, the timeline, unavailable with retry, off-contract answer, an answer for
 * another company, ungranted route), that the decision's suggestions are shown as reminders and never pre-fill the form,
 * that the coefficient rounding direction is decided (KLN-D88: shown, not chosen, always sent as AwayFromZero), what is SENT (company header,
 * Idempotency-Key, the exact body with the expected version, values as text, today as null), invalid input and a past
 * start held back, the same key on a retry after an unknown outcome, the replay notice, the stale refusal that closes the
 * form and re-reads, and a definite refusal kept inside the form.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { COMPANY_A, COMPANY_B, IDENTITY, commands, installFetch, json, noContent, problem, renderAt } from './testHarness'
import { SETTINGS_CAPABILITY } from './capabilities'
import { POLICY_TEXT } from './salesPolicyText'
import { UNIT_CONVERSION_TEXT as T, baseLabel, boundaryLabel, roundingLabel } from './unitConversionPolicyText'

const PATH = '/ayarlar/birim-cevrim-politikasi'
const PROFILE = 'dddddddd-0000-4000-8000-00000000000d'
const TODAY = '2026-11-15'

function reset() {
  vi.unstubAllGlobals()
  document.body.innerHTML = ''
}

afterEach(reset)

const isRead = (url: string) => url.endsWith('/settings/unit-conversion-policy')
const isRecord = (url: string, init: RequestInit | undefined) =>
  init?.method === 'POST' && url.endsWith('/settings/unit-conversion-policy/versions')

function entry(version: string, state: string, effectiveFrom: string, effectiveThrough: string | null) {
  return {
    profileId: PROFILE,
    version,
    densityGramsPerCubicCentimetre: '2.5',
    coefficientDecimals: 4,
    coefficientRounding: 'AwayFromZero',
    documentTotalDecimals: 3,
    documentTotalRounding: 'ToEven',
    wholeUnitRounding: 'ToPositiveInfinity',
    weighbridgeTolerancePercent: '0.75',
    toleranceBoundary: 'Exclusive',
    toleranceBase: 'WeighedKg',
    effectiveFrom,
    effectiveThrough,
    state,
    recordedBy: IDENTITY.userId,
    recordedAtUtc: '2026-11-15T09:00:00+00:00',
    reason: `SENTETIK sürüm ${version}`,
  }
}

function recordedView() {
  return {
    companyId: COMPANY_A,
    today: TODAY,
    version: '2',
    versions: [entry('2', 'InForce', '2026-11-10', null), entry('1', 'Ended', '2026-11-01', '2026-11-09')],
  }
}

const emptyView = () => ({ companyId: COMPANY_A, today: TODAY, version: '0', versions: [] })

function install(respond: (url: string, init: RequestInit | undefined) => Response | Promise<Response> | undefined) {
  return installFetch({ capabilities: [SETTINGS_CAPABILITY.unitConversionPolicy], respond })
}

async function openForm(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('button', { name: T.newVersion }))
}

function choose(legend: string, label: string) {
  return within(screen.getByRole('group', { name: legend })).getByRole('radio', { name: label })
}

async function fillEverything(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText(T.densityLabel, { exact: false }), ' 7.85 ')
  await user.type(screen.getByLabelText(T.coefficientDecimalsLabel, { exact: false }), '3')
  await user.type(screen.getByLabelText(T.documentTotalDecimalsLabel, { exact: false }), '2')
  await user.click(choose(T.documentTotalRoundingLegend, roundingLabel('AwayFromZero')))
  await user.click(choose(T.wholeUnitRoundingLegend, roundingLabel('ToNegativeInfinity')))
  await user.type(screen.getByLabelText(T.toleranceLabel, { exact: false }), '1')
  await user.click(choose(T.boundaryLegend, boundaryLabel('Inclusive')))
  await user.click(choose(T.baseLegend, baseLabel('TheoreticalKg')))
  await user.type(screen.getByLabelText(T.reasonLabel, { exact: false }), '  SENTETIK gerekçe  ')
}

describe('Birim çevrim politikası — reads', () => {
  it('shows the loading state while the read is open', async () => {
    install((url) => (isRead(url) ? new Promise<Response>(() => undefined) : undefined))
    renderAt(PATH)
    await waitFor(() => expect(document.querySelector('[data-state="initial-loading"]')).not.toBeNull())
  })

  it('shows the timeline as the server sent it, the values as text and the state the server marked', async () => {
    const recorded = install((url) => (isRead(url) ? json(recordedView()) : undefined))
    renderAt(PATH)

    const table = await screen.findByRole('table', { name: T.tableLabel })
    const rows = within(table).getAllByRole('row')
    expect(rows).toHaveLength(3)
    expect(within(rows[1]).getByText('2.5')).toBeTruthy()
    expect(within(rows[1]).getByText(`4 ondalık · ${roundingLabel('AwayFromZero')}`)).toBeTruthy()
    expect(within(rows[1]).getByText(T.openEnded)).toBeTruthy()
    expect(within(rows[1]).getByText('Yürürlükte')).toBeTruthy()
    expect(within(rows[2]).getByText('09.11.2026')).toBeTruthy()
    expect(screen.queryByText(T.noneInForce)).toBeNull()

    const read = recorded.find((request) => isRead(request.url))
    expect(read?.headers['X-Company-Id']).toBe(COMPANY_A)
    expect(commands(recorded)).toEqual([])
  })

  it('shows the empty state naming the fail-closed refusal and offers no value of its own', async () => {
    install((url) => (isRead(url) ? json(emptyView()) : undefined))
    renderAt(PATH)

    expect(await screen.findByText(T.empty)).toBeTruthy()
    expect(screen.queryByRole('table', { name: T.tableLabel })).toBeNull()
  })

  it('says that nothing is in force when every stored version has ended', async () => {
    install((url) =>
      isRead(url)
        ? json({ ...emptyView(), version: '1', versions: [entry('1', 'Ended', '2026-11-01', '2026-11-09')] })
        : undefined,
    )
    renderAt(PATH)

    expect(await screen.findByText(T.noneInForce)).toBeTruthy()
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

  it('shows nothing of an off-contract answer or an answer for another company', async () => {
    const view = recordedView()
    const offContract = [
      { ...view, companyId: COMPANY_B },
      { ...view, versions: [view.versions[0], { ...view.versions[1], state: 'InForce' }] },
      { ...view, versions: [{ ...view.versions[0], coefficientRounding: 'Down' }, view.versions[1]] },
      { ...view, versions: [{ ...view.versions[0], densityGramsPerCubicCentimetre: '7.850' }, view.versions[1]] },
      { ...view, versions: [{ ...view.versions[0], weighbridgeTolerancePercent: '101' }, view.versions[1]] },
      { ...view, version: '3' },
      { ...view, density: '7.85' },
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
  })
})

describe('Birim çevrim politikası — commands', () => {
  it('shows the suggestions as reminders only, the decided coefficient direction as fixed text, and pre-fills nothing', async () => {
    const user = userEvent.setup()
    install((url) => (isRead(url) ? json(emptyView()) : undefined))
    renderAt(PATH)

    await openForm(user)
    const hints = screen.getByRole('list', { name: T.hintsHeading })
    expect(within(hints).getByText(T.hintDensity)).toBeTruthy()
    expect(within(hints).getByText(T.hintCoefficientRounding)).toBeTruthy()
    expect(within(hints).getByText(T.hintTolerance)).toBeTruthy()

    for (const label of [T.densityLabel, T.coefficientDecimalsLabel, T.documentTotalDecimalsLabel, T.toleranceLabel]) {
      expect((screen.getByLabelText(label, { exact: false }) as HTMLInputElement).value).toBe('')
    }
    for (const radio of screen.getAllByRole('radio')) expect((radio as HTMLInputElement).checked).toBe(false)

    const coefficient = screen.getByRole('group', { name: T.coefficientRoundingLegend })
    expect(within(coefficient).queryAllByRole('radio')).toHaveLength(0)
    expect(within(coefficient).getByText(roundingLabel('AwayFromZero'))).toBeTruthy()
    expect(within(coefficient).getByText(T.coefficientRoundingDecided)).toBeTruthy()
  })

  it('records the first version with every value as typed, today as null and one Idempotency-Key, then reads again', async () => {
    const user = userEvent.setup()
    let reads = 0
    const recorded = install((url, init) => {
      if (isRead(url)) {
        reads += 1
        return json(emptyView())
      }
      return isRecord(url, init) ? noContent() : undefined
    })
    renderAt(PATH)

    await openForm(user)
    await fillEverything(user)
    await user.click(screen.getByRole('button', { name: T.save }))

    expect(await screen.findByText(T.saved)).toBeTruthy()
    const [command] = commands(recorded)
    expect(command.method).toBe('POST')
    expect(command.url).toMatch(/\/settings\/unit-conversion-policy\/versions$/)
    expect(command.body).toEqual({
      expectedVersion: '0',
      densityGramsPerCubicCentimetre: '7.85',
      coefficientDecimals: 3,
      coefficientRounding: 'AwayFromZero',
      documentTotalDecimals: 2,
      documentTotalRounding: 'AwayFromZero',
      wholeUnitRounding: 'ToNegativeInfinity',
      weighbridgeTolerancePercent: '1',
      toleranceBoundary: 'Inclusive',
      toleranceBase: 'TheoreticalKg',
      reason: 'SENTETIK gerekçe',
      effectiveFrom: null,
    })
    expect(command.headers['X-Company-Id']).toBe(COMPANY_A)
    expect(command.headers['Idempotency-Key']).toMatch(/.+/)
    await waitFor(() => expect(reads).toBeGreaterThanOrEqual(2))
    expect(screen.queryByRole('button', { name: T.save })).toBeNull()
  })

  it('pre-fills from the version in force and sends a later start as itself with the stored version as expected', async () => {
    const user = userEvent.setup()
    const recorded = install((url, init) => {
      if (isRead(url)) return json(recordedView())
      return isRecord(url, init) ? noContent() : undefined
    })
    renderAt(PATH)

    await openForm(user)
    expect((screen.getByLabelText(T.densityLabel, { exact: false }) as HTMLInputElement).value).toBe('2.5')
    expect((choose(T.baseLegend, baseLabel('WeighedKg')) as HTMLInputElement).checked).toBe(true)
    fireEvent.change(screen.getByLabelText(POLICY_TEXT.effectiveFromLabel, { exact: false }), {
      target: { value: '2026-12-01' },
    })
    await user.type(screen.getByLabelText(T.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: T.save }))

    expect(await screen.findByText(T.saved)).toBeTruthy()
    expect(commands(recorded)[0].body).toEqual({
      expectedVersion: '2',
      densityGramsPerCubicCentimetre: '2.5',
      coefficientDecimals: 4,
      coefficientRounding: 'AwayFromZero',
      documentTotalDecimals: 3,
      documentTotalRounding: 'ToEven',
      wholeUnitRounding: 'ToPositiveInfinity',
      weighbridgeTolerancePercent: '0.75',
      toleranceBoundary: 'Exclusive',
      toleranceBase: 'WeighedKg',
      reason: 'SENTETIK',
      effectiveFrom: '2026-12-01',
    })
  })

  it('sends the decided coefficient direction even when the version in force carries another one', async () => {
    const user = userEvent.setup()
    const view = recordedView()
    const legacy = { ...view, versions: [{ ...view.versions[0], coefficientRounding: 'ToZero' }, view.versions[1]] }
    const recorded = install((url, init) => {
      if (isRead(url)) return json(legacy)
      return isRecord(url, init) ? noContent() : undefined
    })
    renderAt(PATH)

    await openForm(user)
    await user.type(screen.getByLabelText(T.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: T.save }))

    expect(await screen.findByText(T.saved)).toBeTruthy()
    expect((commands(recorded)[0].body as { coefficientRounding: string }).coefficientRounding).toBe('AwayFromZero')
  })

  it('holds back a value the server would not read, a missing direction and a past start', async () => {
    const user = userEvent.setup()
    const recorded = install((url, init) => {
      if (isRead(url)) return json(emptyView())
      return isRecord(url, init) ? noContent() : undefined
    })
    renderAt(PATH)

    await openForm(user)
    await fillEverything(user)
    const form = screen.getByRole('button', { name: T.save }).closest('form') as HTMLFormElement

    const density = screen.getByLabelText(T.densityLabel, { exact: false })
    await user.clear(density)
    await user.type(density, '7,85')
    expect(screen.getByText(T.densityError)).toBeTruthy()
    fireEvent.submit(form)
    await user.clear(density)
    await user.type(density, '7.85')

    const tolerance = screen.getByLabelText(T.toleranceLabel, { exact: false })
    await user.clear(tolerance)
    await user.type(tolerance, '100.5')
    expect(screen.getByText(T.toleranceError)).toBeTruthy()
    fireEvent.submit(form)
    await user.clear(tolerance)
    await user.type(tolerance, '1')

    const decimals = screen.getByLabelText(T.coefficientDecimalsLabel, { exact: false })
    await user.clear(decimals)
    await user.type(decimals, '13')
    expect(screen.getByText(T.decimalsError)).toBeTruthy()
    fireEvent.submit(form)
    await user.clear(decimals)
    await user.type(decimals, '3')

    const date = screen.getByLabelText(POLICY_TEXT.effectiveFromLabel, { exact: false })
    fireEvent.change(date, { target: { value: '2026-11-14' } })
    expect(screen.getByText(POLICY_TEXT.effectiveFromError)).toBeTruthy()
    fireEvent.submit(form)

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

    await openForm(user)
    await user.type(screen.getByLabelText(T.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: T.save }))
    expect(await screen.findByText(T.unknownOutcome)).toBeTruthy()
    await user.click(screen.getByRole('button', { name: T.retrySame }))

    expect(await screen.findByText(T.savedReplay)).toBeTruthy()
    const [first, second] = commands(recorded)
    expect(second.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])
    expect(second.body).toEqual(first.body)
  })

  it('closes the form and reads again on a version conflict', async () => {
    const user = userEvent.setup()
    let reads = 0
    install((url, init) => {
      if (isRead(url)) {
        reads += 1
        return json(recordedView())
      }
      return isRecord(url, init)
        ? problem(409, { code: 'settings_unit_conversion_policy_version_conflict' })
        : undefined
    })
    renderAt(PATH)

    await openForm(user)
    await user.type(screen.getByLabelText(T.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: T.save }))

    expect(await screen.findByText(T.staleReloaded)).toBeTruthy()
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

    await openForm(user)
    await user.type(screen.getByLabelText(T.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: T.save }))

    expect((await screen.findByRole('alert')).textContent).toBe('Bu işlem için yetkiniz yok.')
    expect(screen.getByRole('button', { name: T.save })).toBeTruthy()
  })
})
