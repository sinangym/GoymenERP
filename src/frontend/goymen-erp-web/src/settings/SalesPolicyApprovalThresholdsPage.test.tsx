/**
 * F-029 S4b component cases — Ayarlar > Onay tutar eşikleri (SAT-D6; URN-D2/D3/D4).
 *
 * COMPONENT/DOM EVIDENCE ONLY: `fetch` is stubbed with SENTETIK fixtures (the amounts are synthetic, not the reviewed
 * catalog); the real API and SQL are the backend's Settings sales policy tests. What these cases establish: one block per
 * currency as the server sent it (named by its id), the empty and off-contract states, the ungranted route, the one-time
 * baseline transfer, what a per-currency record SENDS (expected version of that currency, the amount as text, null amount
 * when disabled, today as null), the amount and past date held back, the same key on a retry after an unknown outcome and
 * the stale refusal that re-reads.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { COMPANY_A, commands, installFetch, json, noContent, problem, renderAt } from './testHarness'
import { SETTINGS_CAPABILITY } from './capabilities'
import { POLICY_TEXT, sourceLabel } from './salesPolicyText'
import { CURRENCY_ONE, CURRENCY_TWO, thresholdsAfterTransfer, thresholdsBeforeTransfer } from './salesPolicyFixtures'

const PATH = '/ayarlar/onay-esikleri'

function reset() {
  vi.unstubAllGlobals()
  document.body.innerHTML = ''
}

afterEach(reset)

const isRead = (url: string) => url.endsWith('/settings/sales-policies/approval-thresholds')
const isBaseline = (url: string, init: RequestInit | undefined) =>
  init?.method === 'POST' && url.endsWith('/settings/sales-policies/approval-thresholds/baseline-transfer')
const isRecord = (url: string, init: RequestInit | undefined, currencyId: string = CURRENCY_ONE) =>
  init?.method === 'POST' && url.endsWith(`/settings/sales-policies/approval-thresholds/${currencyId}/versions`)

function install(respond: (url: string, init: RequestInit | undefined) => Response | Promise<Response> | undefined) {
  return installFetch({ capabilities: [SETTINGS_CAPABILITY.salesApprovalThresholds], respond })
}

const region = (currencyId: string) => screen.getByRole('region', { name: `${POLICY_TEXT.currencyHeading} ${currencyId}` })

async function openForm(user: ReturnType<typeof userEvent.setup>, currencyId: string = CURRENCY_ONE) {
  await screen.findByRole('region', { name: `${POLICY_TEXT.currencyHeading} ${currencyId}` })
  await user.click(within(region(currencyId)).getByRole('button', { name: POLICY_TEXT.newVersion }))
  return screen.getByRole('form', { name: POLICY_TEXT.newVersion })
}

describe('Onay tutar eşikleri — reads', () => {
  it('shows one block per currency with the compiled catalog before the transfer and no new-version button', async () => {
    const recorded = install((url) => (isRead(url) ? json(thresholdsBeforeTransfer()) : undefined))
    renderAt(PATH)

    await screen.findByRole('region', { name: `${POLICY_TEXT.currencyHeading} ${CURRENCY_ONE}` })
    const one = region(CURRENCY_ONE)
    expect(within(one).getByText('250000.5')).toBeTruthy()
    expect(within(one).getByText(POLICY_TEXT.enabled)).toBeTruthy()
    expect(within(one).getByText(sourceLabel('CompiledBaselineV0'))).toBeTruthy()
    const two = region(CURRENCY_TWO)
    expect(within(two).getByText(POLICY_TEXT.disabled)).toBeTruthy()
    expect(screen.queryByRole('button', { name: POLICY_TEXT.newVersion })).toBeNull()
    expect(screen.getByRole('region', { name: POLICY_TEXT.baselineHeading })).toBeTruthy()

    const read = recorded.find((request) => isRead(request.url))
    expect(read?.headers['X-Company-Id']).toBe(COMPANY_A)
    expect(commands(recorded)).toEqual([])
  })

  it('shows the stored versions after the transfer with a new-version button per currency', async () => {
    install((url) => (isRead(url) ? json(thresholdsAfterTransfer()) : undefined))
    renderAt(PATH)

    await screen.findByRole('region', { name: `${POLICY_TEXT.currencyHeading} ${CURRENCY_ONE}` })
    expect(within(within(region(CURRENCY_ONE)).getByRole('table')).getAllByRole('row')).toHaveLength(3)
    expect(within(region(CURRENCY_ONE)).getByText('275000')).toBeTruthy()
    expect(screen.getAllByRole('button', { name: POLICY_TEXT.newVersion })).toHaveLength(2)
    expect(screen.queryByRole('region', { name: POLICY_TEXT.baselineHeading })).toBeNull()
  })

  it('shows the empty state when the server serves no currency', async () => {
    install((url) => (isRead(url) ? json({ ...thresholdsAfterTransfer(), currencies: [] }) : undefined))
    renderAt(PATH)
    expect(await screen.findByText(POLICY_TEXT.thresholdEmpty)).toBeTruthy()
  })

  it('offers a retry when the source is unavailable, and reads again on it', async () => {
    const user = userEvent.setup()
    let reads = 0
    install((url) => {
      if (!isRead(url)) return undefined
      reads += 1
      return reads === 1 ? problem(503, { code: 'settings_provider_unavailable' }) : json(thresholdsAfterTransfer())
    })
    renderAt(PATH)

    await user.click(await screen.findByRole('button', { name: 'Tekrar dene' }))
    expect(await screen.findByRole('region', { name: `${POLICY_TEXT.currencyHeading} ${CURRENCY_ONE}` })).toBeTruthy()
  })

  it('shows nothing of an off-contract answer (a duplicate currency, another company)', async () => {
    const view = thresholdsAfterTransfer()
    install((url) => (isRead(url) ? json({ ...view, currencies: [view.currencies[0], view.currencies[0]] }) : undefined))
    renderAt(PATH)
    await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    expect(screen.queryByRole('region', { name: `${POLICY_TEXT.currencyHeading} ${CURRENCY_ONE}` })).toBeNull()
    reset()

    install((url) => (isRead(url) ? json({ ...view, companyId: '00000000-0000-4000-8000-000000000009' }) : undefined))
    renderAt(PATH)
    await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
  })

  it('an ungranted screen calls nothing', async () => {
    const recorded = installFetch({ capabilities: [], respond: () => json(thresholdsAfterTransfer()) })
    renderAt(PATH)
    await waitFor(() => expect(recorded.some((request) => request.url.endsWith('/shell/capabilities'))).toBe(true))
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(recorded.some((request) => request.url.includes('/settings/'))).toBe(false)
  })
})

describe('Onay tutar eşikleri — commands', () => {
  it('transfers the baseline once with the reason, then reads again', async () => {
    const user = userEvent.setup()
    let reads = 0
    const recorded = install((url, init) => {
      if (isRead(url)) {
        reads += 1
        return json(reads === 1 ? thresholdsBeforeTransfer() : thresholdsAfterTransfer())
      }
      return isBaseline(url, init) ? noContent() : undefined
    })
    renderAt(PATH)

    const baseline = await screen.findByRole('region', { name: POLICY_TEXT.baselineHeading })
    await user.type(within(baseline).getByLabelText(POLICY_TEXT.reasonLabel), 'SENTETIK devir')
    await user.click(within(baseline).getByRole('button', { name: POLICY_TEXT.baselineSubmit }))

    expect(await screen.findByText(POLICY_TEXT.baselineDone)).toBeTruthy()
    const [command] = commands(recorded)
    expect(command.body).toEqual({ reason: 'SENTETIK devir' })
    expect(command.headers['X-Company-Id']).toBe(COMPANY_A)
    expect(command.headers['Idempotency-Key']).toMatch(/.+/)
    await waitFor(() => expect(reads).toBeGreaterThanOrEqual(2))
    expect(await screen.findAllByRole('button', { name: POLICY_TEXT.newVersion })).toHaveLength(2)
  })

  it('records an enabled amount for one currency with that currency expected version and today as null', async () => {
    const user = userEvent.setup()
    const recorded = install((url, init) => {
      if (isRead(url)) return json(thresholdsAfterTransfer())
      return isRecord(url, init) ? noContent() : undefined
    })
    renderAt(PATH)

    const form = await openForm(user)
    const amount = within(form).getByLabelText(POLICY_TEXT.amountLabel) as HTMLInputElement
    expect(amount.value).toBe('275000')
    await user.clear(amount)
    await user.type(amount, '300000')
    await user.type(within(form).getByLabelText(POLICY_TEXT.reasonLabel), 'SENTETIK gerekçe')
    await user.click(within(form).getByRole('button', { name: POLICY_TEXT.save }))

    expect(await screen.findByText(POLICY_TEXT.saved)).toBeTruthy()
    const [command] = commands(recorded)
    expect(command.url).toMatch(new RegExp(`/settings/sales-policies/approval-thresholds/${CURRENCY_ONE}/versions$`))
    expect(command.body).toEqual({
      expectedVersion: '3',
      isEnabled: true,
      amount: '300000',
      reason: 'SENTETIK gerekçe',
      effectiveFrom: null,
    })
    expect(command.headers['Idempotency-Key']).toMatch(/.+/)
  })

  it('records a disabled threshold with no amount', async () => {
    const user = userEvent.setup()
    const recorded = install((url, init) => {
      if (isRead(url)) return json(thresholdsAfterTransfer())
      return isRecord(url, init) ? noContent() : undefined
    })
    renderAt(PATH)

    const form = await openForm(user)
    await user.click(within(form).getByLabelText(POLICY_TEXT.enabledOff))
    expect(within(form).queryByLabelText(POLICY_TEXT.amountLabel)).toBeNull()
    await user.type(within(form).getByLabelText(POLICY_TEXT.reasonLabel), 'SENTETIK')
    await user.click(within(form).getByRole('button', { name: POLICY_TEXT.save }))

    expect(await screen.findByText(POLICY_TEXT.saved)).toBeTruthy()
    expect(commands(recorded)[0].body).toEqual({
      expectedVersion: '3',
      isEnabled: false,
      amount: null,
      reason: 'SENTETIK',
      effectiveFrom: null,
    })
  })

  it('holds back an amount the server would not read and a past date, without sending', async () => {
    const user = userEvent.setup()
    const recorded = install((url, init) => {
      if (isRead(url)) return json(thresholdsAfterTransfer())
      return isRecord(url, init) ? noContent() : undefined
    })
    renderAt(PATH)

    const form = await openForm(user)
    const amount = within(form).getByLabelText(POLICY_TEXT.amountLabel)
    await user.clear(amount)
    await user.type(amount, '1,5')
    expect(within(form).getByText(POLICY_TEXT.amountError)).toBeTruthy()
    await user.type(within(form).getByLabelText(POLICY_TEXT.reasonLabel), 'SENTETIK')
    fireEvent.submit(form)
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(commands(recorded)).toEqual([])

    await user.clear(amount)
    await user.type(amount, '300000')
    fireEvent.change(within(form).getByLabelText(POLICY_TEXT.effectiveFromLabel), { target: { value: '2026-11-14' } })
    expect(within(form).getByText(POLICY_TEXT.effectiveFromError)).toBeTruthy()
    fireEvent.submit(form)
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(commands(recorded)).toEqual([])
  })

  it('keeps the same Idempotency-Key for the retry after an unknown outcome and shows the replay notice', async () => {
    const user = userEvent.setup()
    let attempts = 0
    const recorded = install((url, init) => {
      if (isRead(url)) return json(thresholdsAfterTransfer())
      if (!isRecord(url, init, CURRENCY_TWO)) return undefined
      attempts += 1
      return attempts === 1 ? problem(503) : noContent({ 'Idempotency-Replayed': 'true' })
    })
    renderAt(PATH)

    const form = await openForm(user, CURRENCY_TWO)
    await user.type(within(form).getByLabelText(POLICY_TEXT.reasonLabel), 'SENTETIK')
    await user.click(within(form).getByRole('button', { name: POLICY_TEXT.save }))
    expect(await screen.findByText(POLICY_TEXT.unknownOutcome)).toBeTruthy()
    await user.click(within(form).getByRole('button', { name: POLICY_TEXT.retrySame }))

    expect(await screen.findByText(POLICY_TEXT.savedReplay)).toBeTruthy()
    const [first, second] = commands(recorded)
    expect(first.body).toEqual({ expectedVersion: '1', isEnabled: false, amount: null, reason: 'SENTETIK', effectiveFrom: null })
    expect(second.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])
    expect(second.body).toEqual(first.body)
  })

  it('closes the form and reads again when the baseline was already transferred elsewhere', async () => {
    const user = userEvent.setup()
    let reads = 0
    install((url, init) => {
      if (isRead(url)) {
        reads += 1
        return json(reads === 1 ? thresholdsBeforeTransfer() : thresholdsAfterTransfer())
      }
      return isBaseline(url, init) ? problem(409, { code: 'settings_sales_policy_baseline_already_transferred' }) : undefined
    })
    renderAt(PATH)

    const baseline = await screen.findByRole('region', { name: POLICY_TEXT.baselineHeading })
    await user.type(within(baseline).getByLabelText(POLICY_TEXT.reasonLabel), 'SENTETIK devir')
    await user.click(within(baseline).getByRole('button', { name: POLICY_TEXT.baselineSubmit }))

    expect(
      await screen.findByText('Başlangıç değerleri bu şirkette zaten devralınmış. Güncel hâli yüklendi.'),
    ).toBeTruthy()
    await waitFor(() => expect(reads).toBeGreaterThanOrEqual(2))
    expect(await screen.findAllByRole('button', { name: POLICY_TEXT.newVersion })).toHaveLength(2)
  })
})
