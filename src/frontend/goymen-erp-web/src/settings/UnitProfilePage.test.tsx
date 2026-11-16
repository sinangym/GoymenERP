/**
 * F-029 S1/S2 (URN-D2…D5) component cases — Ayarlar > Birim profilleri.
 *
 * COMPONENT/DOM EVIDENCE ONLY: `fetch` is stubbed with SENTETIK fixtures; the real API, SQL, the forward-fix migration and
 * the prerequisite port's catalog-then-table answer are the backend's Settings unit profile tests. What these cases
 * establish: the states (loading, the approved catalog before the transfer, the stored timeline after it, unavailable with
 * retry, off-contract answer, an answer for another company, ungranted route), the one-time baseline transfer with its
 * effect stated first, what is SENT (company header, Idempotency-Key, the exact body with the unit's expected version,
 * today as null), the chosen unit's version in force pre-filling the form, a scale above Ö2's kilogram bound and a past
 * start held back, the effect preview, the same key on a retry after an unknown outcome, the replay notice, the stale
 * refusal that closes the form and re-reads, and a definite refusal kept inside the form.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { COMPANY_A, COMPANY_B, IDENTITY, commands, installFetch, json, noContent, problem, renderAt } from './testHarness'
import { SETTINGS_CAPABILITY } from './capabilities'
import { POLICY_TEXT } from './salesPolicyText'
import { UNIT_PROFILE_TEXT as T, profileValueLabel, unitLabel } from './unitProfileText'

const PATH = '/ayarlar/birim-profilleri'
const KG = 'eeeeeeee-0000-4000-8000-00000000000e'
const PIECE = 'ffffffff-0000-4000-8000-00000000000f'
const KG_PROFILE_1 = 'dddddddd-0000-4000-8000-000000000001'
const KG_PROFILE_2 = 'dddddddd-0000-4000-8000-000000000002'
const PIECE_PROFILE_1 = 'dddddddd-0000-4000-8000-000000000003'
const TODAY = '2026-11-15'
const KG_CAPTION = `${T.unitTableLabel} — ${unitLabel('kg', 'Kütle')}`
const PIECE_CAPTION = `${T.unitTableLabel} — ${unitLabel('adet', 'Sayı')}`

function reset() {
  vi.unstubAllGlobals()
  document.body.innerHTML = ''
}

afterEach(reset)

const isRead = (url: string) => url.endsWith('/settings/unit-profiles')
const isBaseline = (url: string, init: RequestInit | undefined) =>
  init?.method === 'POST' && url.endsWith('/settings/unit-profiles/baseline-transfer')
const isRecord = (url: string, init: RequestInit | undefined) =>
  init?.method === 'POST' && url.endsWith('/settings/unit-profiles/versions')

function catalogEntry(profileId: string, scale: number) {
  return {
    profileId,
    version: '1',
    scale,
    isActive: true,
    effectiveFrom: '2026-01-01',
    effectiveThrough: null,
    state: 'InForce',
    sourceKind: 'CompiledBaselineV0',
    decisionReference: 'URN-D5',
    recordedBy: null,
    recordedAtUtc: null,
    reason: null,
  }
}

function storedEntry(
  profileId: string,
  version: string,
  scale: number,
  state: string,
  effectiveFrom: string,
  effectiveThrough: string | null,
) {
  return {
    ...catalogEntry(profileId, scale),
    version,
    effectiveFrom,
    effectiveThrough,
    state,
    sourceKind: version === '1' ? 'CompiledBaselineV0' : 'UserMaintained',
    recordedBy: IDENTITY.userId,
    recordedAtUtc: '2026-11-15T09:00:00+00:00',
    reason: `SENTETIK sürüm ${version}`,
  }
}

const OPTIONS = [
  { unitId: PIECE, unitCode: 'adet', dimensionCode: 'Sayı', isKilogram: false, maximumScale: 28 },
  { unitId: KG, unitCode: 'kg', dimensionCode: 'Kütle', isKilogram: true, maximumScale: 3 },
]

function catalogView() {
  return {
    companyId: COMPANY_A,
    today: TODAY,
    isBaselineTransferred: false,
    units: [
      {
        unitId: PIECE,
        unitCode: 'adet',
        dimensionCode: 'Sayı',
        isKilogram: false,
        version: '0',
        versions: [catalogEntry(PIECE_PROFILE_1, 0)],
      },
      {
        unitId: KG,
        unitCode: 'kg',
        dimensionCode: 'Kütle',
        isKilogram: true,
        version: '0',
        versions: [catalogEntry(KG_PROFILE_1, 3)],
      },
    ],
    unitOptions: OPTIONS,
  }
}

function storedView() {
  return {
    companyId: COMPANY_A,
    today: TODAY,
    isBaselineTransferred: true,
    units: [
      {
        unitId: PIECE,
        unitCode: 'adet',
        dimensionCode: 'Sayı',
        isKilogram: false,
        version: '1',
        versions: [storedEntry(PIECE_PROFILE_1, '1', 0, 'InForce', '2026-01-01', null)],
      },
      {
        unitId: KG,
        unitCode: 'kg',
        dimensionCode: 'Kütle',
        isKilogram: true,
        version: '2',
        versions: [
          storedEntry(KG_PROFILE_2, '2', 2, 'InForce', '2026-11-10', null),
          storedEntry(KG_PROFILE_1, '1', 3, 'Ended', '2026-01-01', '2026-11-09'),
        ],
      },
    ],
    unitOptions: OPTIONS,
  }
}

function install(respond: (url: string, init: RequestInit | undefined) => Response | Promise<Response> | undefined) {
  return installFetch({ capabilities: [SETTINGS_CAPABILITY.unitProfiles], respond })
}

async function openForm(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('button', { name: T.newVersion }))
}

async function chooseUnit(user: ReturnType<typeof userEvent.setup>, unitId: string) {
  await user.selectOptions(screen.getByRole('combobox', { name: T.unitLabel }), unitId)
}

function activeRadio(label: string) {
  return within(screen.getByRole('group', { name: T.activeLegend })).getByRole('radio', { name: label })
}

describe('Birim profilleri — reads', () => {
  it('shows the loading state while the read is open', async () => {
    install((url) => (isRead(url) ? new Promise<Response>(() => undefined) : undefined))
    renderAt(PATH)
    await waitFor(() => expect(document.querySelector('[data-state="initial-loading"]')).not.toBeNull())
  })

  it('shows the approved catalog per unit before the transfer and offers only the transfer', async () => {
    const recorded = install((url) => (isRead(url) ? json(catalogView()) : undefined))
    renderAt(PATH)

    const kg = await screen.findByRole('table', { name: KG_CAPTION })
    const rows = within(kg).getAllByRole('row')
    expect(rows).toHaveLength(2)
    expect(within(rows[1]).getByText('3')).toBeTruthy()
    expect(within(rows[1]).getByText(T.active)).toBeTruthy()
    expect(screen.getByRole('table', { name: PIECE_CAPTION })).toBeTruthy()
    expect(screen.getByText(T.baselinePending)).toBeTruthy()
    expect(screen.getByText(T.baselinePreview)).toBeTruthy()
    expect(screen.getByRole('button', { name: T.baselineSubmit })).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.newVersion })).toBeNull()

    const read = recorded.find((request) => isRead(request.url))
    expect(read?.headers['X-Company-Id']).toBe(COMPANY_A)
    expect(commands(recorded)).toEqual([])
  })

  it('shows the stored timeline after the transfer with the state the server marked, and no transfer action', async () => {
    install((url) => (isRead(url) ? json(storedView()) : undefined))
    renderAt(PATH)

    const kg = await screen.findByRole('table', { name: KG_CAPTION })
    const rows = within(kg).getAllByRole('row')
    expect(rows).toHaveLength(3)
    expect(within(rows[1]).getByText('Yürürlükte')).toBeTruthy()
    expect(within(rows[2]).getByText('09.11.2026')).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.baselineSubmit })).toBeNull()
    expect(screen.getByRole('button', { name: T.newVersion })).toBeTruthy()
    expect(screen.queryByText(T.noneInForce)).toBeNull()
  })

  it('says that nothing is in force for a unit whose every stored version has ended', async () => {
    const view = storedView()
    install((url) =>
      isRead(url)
        ? json({
            ...view,
            units: [
              view.units[0],
              { ...view.units[1], version: '1', versions: [storedEntry(KG_PROFILE_1, '1', 3, 'Ended', '2026-01-01', '2026-11-09')] },
            ],
          })
        : undefined,
    )
    renderAt(PATH)

    expect(await screen.findByText(T.noneInForce)).toBeTruthy()
  })

  it('shows the empty state when the company has no unit profile', async () => {
    install((url) => (isRead(url) ? json({ ...storedView(), units: [] }) : undefined))
    renderAt(PATH)

    expect(await screen.findByText(T.empty)).toBeTruthy()
    expect(screen.queryByRole('table', { name: KG_CAPTION })).toBeNull()
  })

  it('offers a retry when the source is unavailable, and reads again on it', async () => {
    const user = userEvent.setup()
    let reads = 0
    install((url) => {
      if (!isRead(url)) return undefined
      reads += 1
      return reads === 1 ? problem(503, { code: 'settings_provider_unavailable' }) : json(storedView())
    })
    renderAt(PATH)

    await user.click(await screen.findByRole('button', { name: 'Tekrar dene' }))
    expect(await screen.findByRole('table', { name: KG_CAPTION })).toBeTruthy()
  })

  it('shows nothing of an off-contract answer or an answer for another company', async () => {
    const view = storedView()
    const catalog = catalogView()
    const [piece, kg] = view.units
    const offContract = [
      { ...view, companyId: COMPANY_B },
      // A kilogram profile above Ö2's bound.
      { ...view, units: [piece, { ...kg, versions: [{ ...kg.versions[0], scale: 4 }, kg.versions[1]] }] },
      { ...view, units: [piece, { ...kg, versions: [{ ...kg.versions[0], scale: 2.5 }, kg.versions[1]] }] },
      { ...view, units: [piece, { ...kg, versions: [kg.versions[0], { ...kg.versions[1], state: 'InForce' }] }] },
      { ...view, units: [piece, { ...kg, version: '3' }] },
      { ...view, units: [piece, { ...kg, versions: [] }] },
      { ...view, units: [piece, piece] },
      { ...view, unitOptions: [OPTIONS[0], { ...OPTIONS[1], maximumScale: 28 }] },
      { ...view, unitOptions: [OPTIONS[0], OPTIONS[0]] },
      { ...view, scale: 3 },
      // Before the transfer a catalog entry names no recorder and the unit's expected version is "0".
      { ...catalog, units: [{ ...catalog.units[0], version: '1' }, catalog.units[1]] },
      {
        ...catalog,
        units: [{ ...catalog.units[0], versions: [{ ...catalog.units[0].versions[0], reason: 'SENTETIK' }] }, catalog.units[1]],
      },
    ]
    for (const body of offContract) {
      install((url) => (isRead(url) ? json(body) : undefined))
      renderAt(PATH)
      await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
      expect(screen.queryByRole('table', { name: KG_CAPTION })).toBeNull()
      reset()
    }
  })

  it('an ungranted screen calls nothing', async () => {
    const recorded = installFetch({ capabilities: [], respond: () => json(storedView()) })
    renderAt(PATH)
    await waitFor(() => expect(recorded.some((request) => request.url.endsWith('/shell/capabilities'))).toBe(true))
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(recorded.some((request) => request.url.includes('/settings/'))).toBe(false)
  })
})

describe('Birim profilleri — baseline transfer (URN-D5)', () => {
  it('transfers the baseline once with the reason and one Idempotency-Key, then reads again', async () => {
    const user = userEvent.setup()
    let reads = 0
    const recorded = install((url, init) => {
      if (isRead(url)) {
        reads += 1
        return json(reads === 1 ? catalogView() : storedView())
      }
      return isBaseline(url, init) ? noContent() : undefined
    })
    renderAt(PATH)

    await user.type(await screen.findByLabelText(POLICY_TEXT.reasonLabel), '  SENTETIK devir  ')
    await user.click(screen.getByRole('button', { name: T.baselineSubmit }))

    expect(await screen.findByText(T.baselineDone)).toBeTruthy()
    const [command] = commands(recorded)
    expect(command.method).toBe('POST')
    expect(command.url).toMatch(/\/settings\/unit-profiles\/baseline-transfer$/)
    expect(command.body).toEqual({ reason: 'SENTETIK devir' })
    expect(command.headers['X-Company-Id']).toBe(COMPANY_A)
    expect(command.headers['Idempotency-Key']).toMatch(/.+/)
    await waitFor(() => expect(reads).toBeGreaterThanOrEqual(2))
    expect(await screen.findByRole('button', { name: T.newVersion })).toBeTruthy()
  })

  it('re-reads with the stale notice when the transfer was already made elsewhere', async () => {
    const user = userEvent.setup()
    let reads = 0
    install((url, init) => {
      if (isRead(url)) {
        reads += 1
        return json(reads === 1 ? catalogView() : storedView())
      }
      return isBaseline(url, init) ? problem(409, { code: 'settings_unit_profile_already_transferred' }) : undefined
    })
    renderAt(PATH)

    await user.type(await screen.findByLabelText(POLICY_TEXT.reasonLabel), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: T.baselineSubmit }))

    expect(await screen.findByText('Başlangıç değerleri zaten devralınmış. Güncel hâli yüklendi.')).toBeTruthy()
    await waitFor(() => expect(reads).toBeGreaterThanOrEqual(2))
  })
})

describe('Birim profilleri — new version', () => {
  it('pre-fills from the chosen unit in force, previews the effect and records the exact body with one Idempotency-Key', async () => {
    const user = userEvent.setup()
    let reads = 0
    const recorded = install((url, init) => {
      if (isRead(url)) {
        reads += 1
        return json(storedView())
      }
      return isRecord(url, init) ? noContent() : undefined
    })
    renderAt(PATH)

    await openForm(user)
    expect(screen.getByText(T.previewIncomplete)).toBeTruthy()
    await chooseUnit(user, KG)
    const scale = screen.getByLabelText(T.scaleLabel) as HTMLInputElement
    expect(scale.value).toBe('2')
    expect((activeRadio(T.activeOn) as HTMLInputElement).checked).toBe(true)

    await user.clear(scale)
    await user.type(scale, '3')
    await user.click(activeRadio(T.activeOff))
    const preview = screen.getByRole('region', { name: T.previewHeading })
    expect(within(preview).getByText(`${T.previewCurrentPrefix} ${profileValueLabel(2, true)}`)).toBeTruthy()
    expect(within(preview).getByText(`${T.previewNextPrefix} ${profileValueLabel(3, false)}`)).toBeTruthy()
    expect(within(preview).getByText(T.previewHistory)).toBeTruthy()

    await user.type(screen.getByLabelText(POLICY_TEXT.reasonLabel), '  SENTETIK gerekçe  ')
    await user.click(screen.getByRole('button', { name: T.save }))

    expect(await screen.findByText(T.saved)).toBeTruthy()
    const [command] = commands(recorded)
    expect(command.method).toBe('POST')
    expect(command.url).toMatch(/\/settings\/unit-profiles\/versions$/)
    expect(command.body).toEqual({
      expectedVersion: '2',
      unitId: KG,
      scale: 3,
      isActive: false,
      reason: 'SENTETIK gerekçe',
      effectiveFrom: null,
    })
    expect(command.headers['X-Company-Id']).toBe(COMPANY_A)
    expect(command.headers['Idempotency-Key']).toMatch(/.+/)
    await waitFor(() => expect(reads).toBeGreaterThanOrEqual(2))
    expect(screen.queryByRole('button', { name: T.save })).toBeNull()
  })

  it('sends a later start as itself with the unit’s own stored version as expected', async () => {
    const user = userEvent.setup()
    const recorded = install((url, init) => {
      if (isRead(url)) return json(storedView())
      return isRecord(url, init) ? noContent() : undefined
    })
    renderAt(PATH)

    await openForm(user)
    await chooseUnit(user, PIECE)
    fireEvent.change(screen.getByLabelText(POLICY_TEXT.effectiveFromLabel, { exact: false }), {
      target: { value: '2026-12-01' },
    })
    await user.type(screen.getByLabelText(POLICY_TEXT.reasonLabel), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: T.save }))

    expect(await screen.findByText(T.saved)).toBeTruthy()
    expect(commands(recorded)[0].body).toEqual({
      expectedVersion: '1',
      unitId: PIECE,
      scale: 0,
      isActive: true,
      reason: 'SENTETIK',
      effectiveFrom: '2026-12-01',
    })
  })

  it('holds back a kilogram scale above Ö2’s bound, a non-integer scale and a past start', async () => {
    const user = userEvent.setup()
    const recorded = install((url, init) => {
      if (isRead(url)) return json(storedView())
      return isRecord(url, init) ? noContent() : undefined
    })
    renderAt(PATH)

    await openForm(user)
    await chooseUnit(user, KG)
    await user.type(screen.getByLabelText(POLICY_TEXT.reasonLabel), 'SENTETIK')
    const form = screen.getByRole('button', { name: T.save }).closest('form') as HTMLFormElement
    const scale = screen.getByLabelText(T.scaleLabel)

    await user.clear(scale)
    await user.type(scale, '4')
    expect(screen.getByText(T.scaleError)).toBeTruthy()
    fireEvent.submit(form)

    await user.clear(scale)
    await user.type(scale, '1.5')
    expect(screen.getByText(T.scaleError)).toBeTruthy()
    fireEvent.submit(form)

    await user.clear(scale)
    await user.type(scale, '3')
    expect(screen.queryByText(T.scaleError)).toBeNull()
    const date = screen.getByLabelText(POLICY_TEXT.effectiveFromLabel, { exact: false })
    fireEvent.change(date, { target: { value: '2026-11-14' } })
    expect(screen.getByText(POLICY_TEXT.effectiveFromError)).toBeTruthy()
    fireEvent.submit(form)

    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(commands(recorded)).toEqual([])
  })

  it('accepts a scale above three for a unit that is not the kilogram', async () => {
    const user = userEvent.setup()
    const recorded = install((url, init) => {
      if (isRead(url)) return json(storedView())
      return isRecord(url, init) ? noContent() : undefined
    })
    renderAt(PATH)

    await openForm(user)
    await chooseUnit(user, PIECE)
    const scale = screen.getByLabelText(T.scaleLabel)
    await user.clear(scale)
    await user.type(scale, '6')
    expect(screen.queryByText(T.scaleError)).toBeNull()
    await user.type(screen.getByLabelText(POLICY_TEXT.reasonLabel), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: T.save }))

    expect(await screen.findByText(T.saved)).toBeTruthy()
    expect((commands(recorded)[0].body as { scale: number }).scale).toBe(6)
  })

  it('keeps the same Idempotency-Key for the retry after an unknown outcome and shows the replay notice', async () => {
    const user = userEvent.setup()
    let attempts = 0
    const recorded = install((url, init) => {
      if (isRead(url)) return json(storedView())
      if (!isRecord(url, init)) return undefined
      attempts += 1
      return attempts === 1 ? problem(503) : noContent({ 'Idempotency-Replayed': 'true' })
    })
    renderAt(PATH)

    await openForm(user)
    await chooseUnit(user, KG)
    await user.type(screen.getByLabelText(POLICY_TEXT.reasonLabel), 'SENTETIK')
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
        return json(storedView())
      }
      return isRecord(url, init) ? problem(409, { code: 'settings_unit_profile_version_conflict' }) : undefined
    })
    renderAt(PATH)

    await openForm(user)
    await chooseUnit(user, KG)
    await user.type(screen.getByLabelText(POLICY_TEXT.reasonLabel), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: T.save }))

    expect(await screen.findByText(T.staleReloaded)).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.save })).toBeNull()
    await waitFor(() => expect(reads).toBeGreaterThanOrEqual(2))
  })

  it('shows a definite refusal inside the form and keeps it open', async () => {
    const user = userEvent.setup()
    install((url, init) => {
      if (isRead(url)) return json(storedView())
      return isRecord(url, init) ? problem(403, { code: 'settings_access_denied' }) : undefined
    })
    renderAt(PATH)

    await openForm(user)
    await chooseUnit(user, KG)
    await user.type(screen.getByLabelText(POLICY_TEXT.reasonLabel), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: T.save }))

    expect((await screen.findByRole('alert')).textContent).toBe('Bu işlem için yetkiniz yok.')
    expect(screen.getByRole('button', { name: T.save })).toBeTruthy()
  })

  it('offers no new version when no unit identity can take a profile', async () => {
    install((url) => (isRead(url) ? json({ ...storedView(), unitOptions: [] }) : undefined))
    renderAt(PATH)

    expect((await screen.findByRole('button', { name: T.newVersion })).hasAttribute('disabled')).toBe(true)
  })
})
