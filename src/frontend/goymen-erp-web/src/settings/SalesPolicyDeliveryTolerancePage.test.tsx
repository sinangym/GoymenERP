/**
 * F-029 S4b component cases — Ayarlar > Teslim toleransı (SAT-D5; URN-D2/D3/D4).
 *
 * COMPONENT/DOM EVIDENCE ONLY: `fetch` is stubbed with SENTETIK fixtures (the percents are synthetic, not the reviewed
 * catalog); the real API and SQL are the backend's Settings sales policy tests. What these cases establish: the states
 * (loading, empty, unavailable with retry, off-contract answer, ungranted route), the compiled catalog before the one-time
 * baseline transfer and the stored timeline after it, what is SENT (company header, Idempotency-Key, the exact bodies with
 * the expected version and percents as text, today as null), the past date held back, the same key on a retry after an
 * unknown outcome, the replay notice and the stale refusal that closes the form and re-reads.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { COMPANY_A, commands, installFetch, json, noContent, problem, renderAt } from './testHarness'
import { SETTINGS_CAPABILITY } from './capabilities'
import { POLICY_TEXT, percentLabel, sourceLabel } from './salesPolicyText'
import { toleranceAfterTransfer, toleranceBeforeTransfer } from './salesPolicyFixtures'

const PATH = '/ayarlar/teslim-toleransi'

function reset() {
  vi.unstubAllGlobals()
  document.body.innerHTML = ''
}

afterEach(reset)

const isRead = (url: string) => url.endsWith('/settings/sales-policies/delivery-tolerance')
const isBaseline = (url: string, init: RequestInit | undefined) =>
  init?.method === 'POST' && url.endsWith('/settings/sales-policies/delivery-tolerance/baseline-transfer')
const isRecord = (url: string, init: RequestInit | undefined) =>
  init?.method === 'POST' && url.endsWith('/settings/sales-policies/delivery-tolerance/versions')

function install(respond: (url: string, init: RequestInit | undefined) => Response | Promise<Response> | undefined) {
  return installFetch({ capabilities: [SETTINGS_CAPABILITY.salesDeliveryTolerance], respond })
}

async function openForm(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('button', { name: POLICY_TEXT.newVersion }))
}

describe('Teslim toleransı — reads', () => {
  it('shows the loading state while the read is open', async () => {
    install((url) => (isRead(url) ? new Promise<Response>(() => undefined) : undefined))
    renderAt(PATH)
    await waitFor(() => expect(document.querySelector('[data-state="initial-loading"]')).not.toBeNull())
  })

  it('shows the compiled catalog before the transfer, with no recorder and no new-version button', async () => {
    const recorded = install((url) => (isRead(url) ? json(toleranceBeforeTransfer()) : undefined))
    renderAt(PATH)

    const table = await screen.findByRole('table', { name: POLICY_TEXT.toleranceTableLabel })
    const row = within(table).getAllByRole('row')[1]
    expect(within(row).getByText(percentLabel('7.25'))).toBeTruthy()
    expect(within(row).getByText(sourceLabel('CompiledBaselineV0'))).toBeTruthy()
    expect(within(row).getAllByText(POLICY_TEXT.notRecorded).length).toBeGreaterThanOrEqual(2)
    expect(screen.getByRole('button', { name: POLICY_TEXT.baselineSubmit })).toBeTruthy()
    expect(screen.queryByRole('button', { name: POLICY_TEXT.newVersion })).toBeNull()

    const read = recorded.find((request) => isRead(request.url))
    expect(read?.headers['X-Company-Id']).toBe(COMPANY_A)
    expect(commands(recorded)).toEqual([])
  })

  it('shows the stored timeline after the transfer and the version in force as the server marked it', async () => {
    install((url) => (isRead(url) ? json(toleranceAfterTransfer()) : undefined))
    renderAt(PATH)

    const table = await screen.findByRole('table', { name: POLICY_TEXT.toleranceTableLabel })
    expect(within(table).getAllByRole('row')).toHaveLength(3)
    expect(
      screen.getByText(
        `${POLICY_TEXT.toleranceInForce}: ${POLICY_TEXT.columnWarn} ${percentLabel('2')} · ` +
          `${POLICY_TEXT.columnApproval} ${percentLabel('4.5')} · ${POLICY_TEXT.columnBlock} ${percentLabel('9')}`,
      ),
    ).toBeTruthy()
    expect(screen.queryByRole('button', { name: POLICY_TEXT.baselineSubmit })).toBeNull()
    expect(screen.getByRole('button', { name: POLICY_TEXT.newVersion })).toBeTruthy()
  })

  it('shows the empty timeline with nothing in force', async () => {
    install((url) => (isRead(url) ? json({ ...toleranceAfterTransfer(), version: '0', versions: [] }) : undefined))
    renderAt(PATH)
    expect(await screen.findByText(POLICY_TEXT.toleranceEmpty)).toBeTruthy()
    expect(screen.getByText(POLICY_TEXT.toleranceNoneInForce)).toBeTruthy()
  })

  it('offers a retry when the source is unavailable, and reads again on it', async () => {
    const user = userEvent.setup()
    let reads = 0
    install((url) => {
      if (!isRead(url)) return undefined
      reads += 1
      return reads === 1 ? problem(503, { code: 'settings_provider_unavailable' }) : json(toleranceAfterTransfer())
    })
    renderAt(PATH)

    await user.click(await screen.findByRole('button', { name: 'Tekrar dene' }))
    expect(await screen.findByRole('table', { name: POLICY_TEXT.toleranceTableLabel })).toBeTruthy()
  })

  it('shows nothing of an off-contract answer (another company, a duplicate version)', async () => {
    install((url) => (isRead(url) ? json({ ...toleranceAfterTransfer(), companyId: '00000000-0000-4000-8000-000000000009' }) : undefined))
    renderAt(PATH)
    await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    reset()

    const view = toleranceAfterTransfer()
    install((url) => (isRead(url) ? json({ ...view, versions: [view.versions[1], view.versions[1]] }) : undefined))
    renderAt(PATH)
    await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    expect(screen.queryByRole('table', { name: POLICY_TEXT.toleranceTableLabel })).toBeNull()
  })

  it('an ungranted screen calls nothing', async () => {
    const recorded = installFetch({ capabilities: [], respond: () => json(toleranceAfterTransfer()) })
    renderAt(PATH)
    await waitFor(() => expect(recorded.some((request) => request.url.endsWith('/shell/capabilities'))).toBe(true))
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(recorded.some((request) => request.url.includes('/settings/'))).toBe(false)
  })
})

describe('Teslim toleransı — commands', () => {
  it('transfers the baseline once with the reason and one Idempotency-Key, then reads again', async () => {
    const user = userEvent.setup()
    let reads = 0
    const recorded = install((url, init) => {
      if (isRead(url)) {
        reads += 1
        return json(reads === 1 ? toleranceBeforeTransfer() : toleranceAfterTransfer())
      }
      return isBaseline(url, init) ? noContent() : undefined
    })
    renderAt(PATH)

    await user.type(await screen.findByLabelText(POLICY_TEXT.reasonLabel, { exact: false }), '  SENTETIK devir  ')
    await user.click(screen.getByRole('button', { name: POLICY_TEXT.baselineSubmit }))

    expect(await screen.findByText(POLICY_TEXT.baselineDone)).toBeTruthy()
    const [command] = commands(recorded)
    expect(command.method).toBe('POST')
    expect(command.body).toEqual({ reason: 'SENTETIK devir' })
    expect(command.headers['X-Company-Id']).toBe(COMPANY_A)
    expect(command.headers['Idempotency-Key']).toMatch(/.+/)
    await waitFor(() => expect(reads).toBeGreaterThanOrEqual(2))
    expect(await screen.findByRole('button', { name: POLICY_TEXT.newVersion })).toBeTruthy()
  })

  it('records a new version from today with the expected version, percents as text and today as null', async () => {
    const user = userEvent.setup()
    let reads = 0
    const recorded = install((url, init) => {
      if (isRead(url)) {
        reads += 1
        return json(toleranceAfterTransfer())
      }
      return isRecord(url, init) ? noContent() : undefined
    })
    renderAt(PATH)

    await openForm(user)
    // Pre-filled from the version in force (2 / 4.5 / 9).
    expect((screen.getByLabelText(POLICY_TEXT.warnLabel) as HTMLInputElement).value).toBe('2')
    const block = screen.getByLabelText(POLICY_TEXT.blockLabel)
    await user.clear(block)
    await user.type(block, ' 10.25 ')
    await user.type(screen.getByLabelText(POLICY_TEXT.reasonLabel, { exact: false }), 'SENTETIK gerekçe')
    await user.click(screen.getByRole('button', { name: POLICY_TEXT.save }))

    expect(await screen.findByText(POLICY_TEXT.saved)).toBeTruthy()
    const [command] = commands(recorded)
    expect(command.url).toMatch(/\/settings\/sales-policies\/delivery-tolerance\/versions$/)
    expect(command.body).toEqual({
      expectedVersion: '2',
      warnPercent: '2',
      approvalPercent: '4.5',
      blockPercent: '10.25',
      reason: 'SENTETIK gerekçe',
      effectiveFrom: null,
    })
    expect(command.headers['Idempotency-Key']).toMatch(/.+/)
    await waitFor(() => expect(reads).toBeGreaterThanOrEqual(2))
    expect(screen.queryByRole('button', { name: POLICY_TEXT.save })).toBeNull()
  })

  it('sends a later date as itself and holds a past date back without sending', async () => {
    const user = userEvent.setup()
    const recorded = install((url, init) => {
      if (isRead(url)) return json(toleranceAfterTransfer())
      return isRecord(url, init) ? noContent() : undefined
    })
    renderAt(PATH)

    await openForm(user)
    const date = screen.getByLabelText(POLICY_TEXT.effectiveFromLabel, { exact: false })
    await user.type(screen.getByLabelText(POLICY_TEXT.reasonLabel, { exact: false }), 'SENTETIK')
    fireEvent.change(date, { target: { value: '2026-11-14' } })
    expect(screen.getByText(POLICY_TEXT.effectiveFromError)).toBeTruthy()
    fireEvent.submit(screen.getByRole('button', { name: POLICY_TEXT.save }).closest('form') as HTMLFormElement)
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(commands(recorded)).toEqual([])

    fireEvent.change(date, { target: { value: '2026-12-01' } })
    expect(screen.queryByText(POLICY_TEXT.effectiveFromError)).toBeNull()
    await user.click(screen.getByRole('button', { name: POLICY_TEXT.save }))
    expect(await screen.findByText(POLICY_TEXT.saved)).toBeTruthy()
    expect((commands(recorded)[0].body as { effectiveFrom: unknown }).effectiveFrom).toBe('2026-12-01')
  })

  it('holds back a percent the server would not read', async () => {
    const user = userEvent.setup()
    const recorded = install((url, init) => {
      if (isRead(url)) return json(toleranceAfterTransfer())
      return isRecord(url, init) ? noContent() : undefined
    })
    renderAt(PATH)

    await openForm(user)
    const warn = screen.getByLabelText(POLICY_TEXT.warnLabel)
    await user.clear(warn)
    await user.type(warn, '2,5')
    expect(screen.getByText(POLICY_TEXT.percentError)).toBeTruthy()
    await user.type(screen.getByLabelText(POLICY_TEXT.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: POLICY_TEXT.save }))
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(commands(recorded)).toEqual([])
  })

  it('keeps the same Idempotency-Key for the retry after an unknown outcome and shows the replay notice', async () => {
    const user = userEvent.setup()
    let attempts = 0
    const recorded = install((url, init) => {
      if (isRead(url)) return json(toleranceAfterTransfer())
      if (!isRecord(url, init)) return undefined
      attempts += 1
      return attempts === 1 ? problem(503) : noContent({ 'Idempotency-Replayed': 'true' })
    })
    renderAt(PATH)

    await openForm(user)
    await user.type(screen.getByLabelText(POLICY_TEXT.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: POLICY_TEXT.save }))
    expect(await screen.findByText(POLICY_TEXT.unknownOutcome)).toBeTruthy()
    await user.click(screen.getByRole('button', { name: POLICY_TEXT.retrySame }))

    expect(await screen.findByText(POLICY_TEXT.savedReplay)).toBeTruthy()
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
        return json(toleranceAfterTransfer())
      }
      return isRecord(url, init) ? problem(409, { code: 'settings_sales_policy_version_conflict' }) : undefined
    })
    renderAt(PATH)

    await openForm(user)
    await user.type(screen.getByLabelText(POLICY_TEXT.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: POLICY_TEXT.save }))

    expect(await screen.findByText(POLICY_TEXT.staleReloaded)).toBeTruthy()
    expect(screen.queryByRole('button', { name: POLICY_TEXT.save })).toBeNull()
    await waitFor(() => expect(reads).toBeGreaterThanOrEqual(2))
  })

  it('shows a definite refusal inside the form and keeps it open', async () => {
    const user = userEvent.setup()
    install((url, init) => {
      if (isRead(url)) return json(toleranceAfterTransfer())
      return isRecord(url, init) ? problem(403, { code: 'settings_access_denied' }) : undefined
    })
    renderAt(PATH)

    await openForm(user)
    await user.type(screen.getByLabelText(POLICY_TEXT.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: POLICY_TEXT.save }))

    expect((await screen.findByRole('alert')).textContent).toBe('Bu işlem için yetkiniz yok.')
    expect(screen.getByRole('button', { name: POLICY_TEXT.save })).toBeTruthy()
  })
})
