/**
 * F-025 / B-11 component cases — Ayarlar > Ana Veriler > Kurlar (UI-K03; SAT-D55, SAT-D169…D172, SAT-D188).
 *
 * COMPONENT/DOM EVIDENCE ONLY: `fetch` is stubbed with synthetic fixtures (SENTETIK rates, records and reasons); the real
 * API and SQL are the Settings exchange rate relational acceptance tests. What these cases establish: the states (loading,
 * empty, forbidden route, 503 retry, off-contract answer, another company), the controls shown only with their names and
 * only where the server would accept them, what is SENT (company header, Idempotency-Key, the day's version, the rate as
 * text and the reason), the same key on a retry after an unknown outcome, the re-read after a version conflict, and
 * "kur yoksa işlem durur": an Unavailable answer shows no rate at all.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { COMPANY_A, COMPANY_B, IDENTITY, commands, installFetch, json, noContent, problem, renderAt } from './testHarness'
import { SETTINGS_CAPABILITY, SETTINGS_EXCHANGE_RATE_CONTROL } from './capabilities'
import { EXCHANGE_RATE_INPUT, addDays } from './exchangeRateContract'
import {
  BULLETIN_STATE_LABEL,
  EXCHANGE_RATE_TEXT as T,
  formatDate,
  HALF_DAY_STATE_LABEL,
  IMPORT_OUTCOME_LABEL,
  unavailableReasonText,
} from './exchangeRateText'

const RATES_PATH = '/ayarlar/kurlar'
const RECORD = '71000000-0000-4000-8000-0000000000b1'
const HALF_RECORD = '71000000-0000-4000-8000-0000000000b2'
const SOURCE_CURRENCY = '71000000-0000-4000-8000-0000000000c1'
const TARGET_CURRENCY = '71000000-0000-4000-8000-0000000000c2'
const ALL_CONTROLS = [
  SETTINGS_CAPABILITY.exchangeRates,
  SETTINGS_EXCHANGE_RATE_CONTROL.correct,
  SETTINGS_EXCHANGE_RATE_CONTROL.markHalfDay,
  SETTINGS_EXCHANGE_RATE_CONTROL.import,
]

afterEach(() => {
  Object.assign(IDENTITY, { authenticationSessionBinding: null })
  sessionStorage.clear()
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

function isList(url: string): boolean {
  return /\/settings\/exchange-rates\?/.test(url)
}

function isResolution(url: string): boolean {
  return url.includes('/settings/exchange-rates/resolution?')
}

function rangeOf(url: string): { from: string; to: string } {
  const query = new URL(url, 'http://localhost').searchParams
  return { from: query.get('from') ?? '', to: query.get('to') ?? '' }
}

function bulletinRecord(date: string) {
  return {
    recordId: RECORD,
    recordVersion: '1',
    kind: 'Bulletin',
    provenance: 'Official',
    state: 'Published',
    rates: [{ currencyCode: 'USD', sellingRate: '34.1234' }],
    recordedBy: 'SENTETIK içe aktarım',
    recordedAtUtc: `${date}T13:00:00+00:00`,
    reason: null,
    sourceReference: 'SENTETIK bülten',
  }
}

/** A published, fully decided day (official bulletin, official full day). */
function publishedDay(date: string) {
  return {
    date,
    version: '2',
    bulletinState: 'Published',
    halfDayState: 'FullDay',
    halfDayProvenance: 'Official',
    rates: [
      { currencyCode: 'EUR', sellingRate: '37.5', provenance: 'Manual', recordId: RECORD, recordVersion: '1' },
      { currencyCode: 'USD', sellingRate: '34.1234', provenance: 'Official', recordId: RECORD, recordVersion: '1' },
    ],
    records: [
      bulletinRecord(date),
      {
        recordId: HALF_RECORD,
        recordVersion: '1',
        kind: 'HalfDay',
        provenance: 'Official',
        state: 'FullDay',
        rates: [],
        recordedBy: 'SENTETIK içe aktarım',
        recordedAtUtc: `${date}T13:00:00+00:00`,
        reason: null,
        sourceReference: null,
      },
    ],
  }
}

/** A day the importer has not seen: no record, version "0". */
function unrecordedDay(date: string) {
  return {
    date,
    version: '0',
    bulletinState: 'NotRecorded',
    halfDayState: 'Undetermined',
    halfDayProvenance: null,
    rates: [],
    records: [],
  }
}

function dayList(url: string, days: (from: string, to: string) => unknown[], overrides: Record<string, unknown> = {}) {
  const { from, to } = rangeOf(url)
  return {
    companyId: COMPANY_A,
    from,
    to,
    bulletinSourceEnabled: true,
    halfDaySourceEnabled: true,
    days: days(from, to),
    ...overrides,
  }
}

/** The last day of the range is published, the one before it was never recorded. */
function standardDays(_from: string, to: string) {
  return [unrecordedDay(addDays(to, -1)), publishedDay(to)]
}

function install(
  respond: (url: string, init: RequestInit | undefined) => Response | Promise<Response> | undefined,
  capabilities: readonly string[] = ALL_CONTROLS,
) {
  return installFetch({ capabilities, respond })
}

describe('Kurlar — reading', () => {
  it('shows the default range ending today in Türkiye, with the company header and no command', async () => {
    vi.useFakeTimers({ toFake: ['Date'], now: new Date('2026-11-14T22:30:00Z') })
    const recorded = install((url) => (isList(url) ? json(dayList(url, standardDays)) : undefined))
    renderAt(RATES_PATH)

    const table = await screen.findByRole('table', { name: T.tableCaption('2026-11-02', '2026-11-15') })
    const read = recorded.find((request) => isList(request.url))
    // 22:30 UTC is already the 15th in Istanbul (UTC+3): the default window is the 14 days ending that day.
    expect(rangeOf(read?.url ?? '')).toEqual({
      from: addDays('2026-11-15', 1 - EXCHANGE_RATE_INPUT.defaultListDays),
      to: '2026-11-15',
    })
    expect(read?.headers['X-Company-Id']).toBe(COMPANY_A)
    const published = within(table).getByRole('row', { name: new RegExp(formatDate('2026-11-15')) })
    expect(within(published).getByText(BULLETIN_STATE_LABEL.Published)).toBeTruthy()
    const rates = within(published).getByRole('list', { name: `${T.columnRates} — ${formatDate('2026-11-15')}` })
    expect(within(rates).getByText('USD 34.1234')).toBeTruthy()
    expect(within(rates).getByText('EUR 37.5')).toBeTruthy()
    expect(within(rates).getByText(T.manualBadge)).toBeTruthy()
    expect(within(table).getByText(BULLETIN_STATE_LABEL.NotRecorded)).toBeTruthy()
    expect(commands(recorded)).toEqual([])
  })

  it('shows a loading state, then the empty state with the lookup still available', async () => {
    let release: (response: Response) => void = () => undefined
    install((url) => (isList(url) ? new Promise<Response>((resolve) => (release = resolve)) : undefined))
    renderAt(RATES_PATH)

    await waitFor(() => expect(lastListUrl()).toBeDefined())
    expect(document.querySelector('[data-state="initial-loading"]')).not.toBeNull()
    release(json({ companyId: COMPANY_A, ...rangeOf(lastListUrl() ?? ''), bulletinSourceEnabled: true, halfDaySourceEnabled: true, days: [] }))
    await waitFor(() => expect(document.querySelector('[data-state="empty-no-data"]')).not.toBeNull())
    expect(screen.getByRole('heading', { name: T.resolutionHeading })).toBeTruthy()
  })

  it('offers a retry on 503, and shows nothing for an answer of another company', async () => {
    let reads = 0
    install((url) => {
      if (!isList(url)) return undefined
      reads += 1
      return reads === 1 ? problem(503, { code: 'settings_provider_unavailable' }) : json(dayList(url, standardDays))
    })
    const user = userEvent.setup()
    renderAt(RATES_PATH)
    await user.click(await screen.findByRole('button', { name: 'Tekrar dene' }))
    expect(await screen.findByRole('table')).toBeTruthy()
    vi.unstubAllGlobals()
    document.body.innerHTML = ''

    install((url) => (isList(url) ? json(dayList(url, standardDays, { companyId: COMPANY_B })) : undefined))
    renderAt(RATES_PATH)
    await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    expect(screen.queryByText('USD 34.1234')).toBeNull()
  })

  it('treats a rate on a day without a published bulletin as an off-contract answer', async () => {
    install((url) =>
      isList(url)
        ? json(dayList(url, (_from, to) => [{ ...publishedDay(to), bulletinState: 'Unverified' }]))
        : undefined,
    )
    renderAt(RATES_PATH)
    await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    expect(screen.queryByText('USD 34.1234')).toBeNull()
  })

  it('an ungranted screen calls nothing', async () => {
    const recorded = install(() => problem(500), [])
    renderAt(RATES_PATH)
    await waitFor(() => expect(recorded.some((request) => request.url.endsWith('/shell/capabilities'))).toBe(true))
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(recorded.some((request) => request.url.includes('/settings/'))).toBe(false)
  })

  it('refuses a range longer than the maximum in the form and reads nothing new', async () => {
    const user = userEvent.setup()
    const recorded = install((url) => (isList(url) ? json(dayList(url, standardDays)) : undefined))
    renderAt(RATES_PATH)
    await screen.findByRole('table')
    const reads = recorded.filter((request) => isList(request.url)).length
    const { to } = rangeOf(lastListUrl() ?? '')

    const range = screen.getByRole('form', { name: T.rangeHeading })
    // One day more than the maximum.
    fireEvent.change(within(range).getByLabelText(T.fromLabel, { exact: false }), {
      target: { value: addDays(to, -EXCHANGE_RATE_INPUT.maximumListDays) },
    })
    await user.click(within(range).getByRole('button', { name: T.show }))
    expect(await screen.findByText(T.rangeInvalid)).toBeTruthy()
    expect(recorded.filter((request) => isList(request.url))).toHaveLength(reads)
  })
})

describe('Kurlar — controls', () => {
  it('shows only the read view without a control name', async () => {
    install((url) => (isList(url) ? json(dayList(url, standardDays)) : undefined), [SETTINGS_CAPABILITY.exchangeRates])
    renderAt(RATES_PATH)
    await screen.findByRole('table')
    expect(screen.queryByRole('button', { name: new RegExp(`^${T.correct}`) })).toBeNull()
    expect(screen.queryByRole('button', { name: new RegExp(`^${T.markHalfDay}`) })).toBeNull()
    expect(screen.queryByRole('button', { name: new RegExp(`^${T.reverify}`) })).toBeNull()
    expect(screen.queryByRole('heading', { name: T.importHeading })).toBeNull()
  })

  it('offers each action only where the server would accept it', async () => {
    install((url) => (isList(url) ? json(dayList(url, standardDays)) : undefined))
    renderAt(RATES_PATH)
    await screen.findByRole('table')
    const today = rangeOf(lastListUrl() ?? '').to
    const yesterday = formatDate(addDays(today, -1))

    // Published, officially decided day: correct and reverify, but no half-day mark (409 half_day_determined).
    expect(screen.getByRole('button', { name: `${T.correct} — ${formatDate(today)}` })).toBeTruthy()
    expect(screen.getByRole('button', { name: `${T.reverify} — ${formatDate(today)}` })).toBeTruthy()
    expect(screen.queryByRole('button', { name: `${T.markHalfDay} — ${formatDate(today)}` })).toBeNull()
    // Unrecorded day: no correction (422 day_not_published), no reverification (422 day_not_final), a mark is possible.
    expect(screen.queryByRole('button', { name: `${T.correct} — ${yesterday}` })).toBeNull()
    expect(screen.queryByRole('button', { name: `${T.reverify} — ${yesterday}` })).toBeNull()
    expect(screen.getByRole('button', { name: `${T.markHalfDay} — ${yesterday}` })).toBeTruthy()
  })

  it('hides reverification and import while both sources are disabled, and says so', async () => {
    install((url) =>
      isList(url)
        ? json(dayList(url, standardDays, { bulletinSourceEnabled: false, halfDaySourceEnabled: false }))
        : undefined,
    )
    renderAt(RATES_PATH)
    await screen.findByRole('table')
    expect(screen.getByText(T.bulletinSourceDisabled)).toBeTruthy()
    expect(screen.getByText(T.halfDaySourceDisabled)).toBeTruthy()
    expect(screen.queryByRole('button', { name: new RegExp(`^${T.reverify}`) })).toBeNull()
    expect(screen.queryByRole('heading', { name: T.importHeading })).toBeNull()
  })
})

describe('Kurlar — commands', () => {
  it('corrects a rate with the day version, the rate as text, the reason and one Idempotency-Key, then re-reads', async () => {
    const user = userEvent.setup()
    let reads = 0
    const recorded = install((url, init) => {
      if (isList(url)) {
        reads += 1
        return json(dayList(url, standardDays))
      }
      return init?.method === 'POST' && /\/days\/\d{4}-\d{2}-\d{2}\/corrections$/.test(url) ? noContent() : undefined
    })
    renderAt(RATES_PATH)
    await screen.findByRole('table')
    const today = rangeOf(lastListUrl() ?? '').to

    await user.click(screen.getByRole('button', { name: `${T.correct} — ${formatDate(today)}` }))
    const form = screen.getByRole('form', { name: T.correctHeading(today) })
    await user.selectOptions(within(form).getByLabelText(T.currencyLabel, { exact: false }), 'USD')
    await user.type(within(form).getByLabelText(T.rateLabel, { exact: false }), '34.5000')
    await user.type(within(form).getByLabelText(T.reasonLabel, { exact: false }), '  SENTETIK düzeltme  ')
    await user.click(within(form).getByRole('button', { name: T.save }))

    expect(await screen.findByText(T.saved)).toBeTruthy()
    const [command] = commands(recorded)
    expect(command.url).toMatch(new RegExp(`/settings/exchange-rates/days/${today}/corrections$`))
    expect(command.body).toEqual({ expectedVersion: '2', currencyCode: 'USD', sellingRate: '34.5000', reason: 'SENTETIK düzeltme' })
    expect(command.headers['X-Company-Id']).toBe(COMPANY_A)
    expect(command.headers['Idempotency-Key']).toMatch(/.+/)
    await waitFor(() => expect(reads).toBeGreaterThanOrEqual(2))
  })

  it('refuses a rate that is not plain positive decimal text and sends nothing', async () => {
    const user = userEvent.setup()
    const recorded = install((url) => (isList(url) ? json(dayList(url, standardDays)) : undefined))
    renderAt(RATES_PATH)
    await screen.findByRole('table')
    const today = rangeOf(lastListUrl() ?? '').to

    await user.click(screen.getByRole('button', { name: `${T.correct} — ${formatDate(today)}` }))
    const form = screen.getByRole('form', { name: T.correctHeading(today) })
    await user.type(within(form).getByLabelText(T.rateLabel, { exact: false }), '34,5')
    await user.type(within(form).getByLabelText(T.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(within(form).getByRole('button', { name: T.save }))
    expect(await screen.findByText(T.rateInvalid)).toBeTruthy()
    expect(commands(recorded)).toEqual([])
  })

  it('freezes outer controls and restores the same key after a route remount with a code-less 503 outcome', async () => {
    Object.assign(IDENTITY, { authenticationSessionBinding: 'synthetic-exchange-session' })
    const user = userEvent.setup()
    let attempt = 0
    const recorded = install((url, init) => {
      if (isList(url)) return json(dayList(url, (from, to) => standardDays(from, to).filter(day => day.date >= from)))
      if (init?.method === 'POST' && url.endsWith('/reverification')) {
        attempt += 1
        return attempt === 1 ? new Response(null, { status: 503 }) : noContent({ 'Idempotency-Replayed': 'true' })
      }
      return undefined
    })
    const { router } = renderAt(RATES_PATH)
    await screen.findByRole('table')
    const today = rangeOf(lastListUrl() ?? '').to

    await user.click(screen.getByRole('button', { name: `${T.reverify} — ${formatDate(today)}` }))
    await user.type(screen.getByLabelText(T.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: T.save }))
    expect(await screen.findByText(T.unknownOutcome)).toBeTruthy()
    const rangeForm = screen.getByRole('form', { name: T.rangeHeading })
    expect(within(rangeForm).getByRole('button', { name: T.show }).matches(':disabled')).toBe(true)
    expect(within(rangeForm).getByLabelText(T.fromLabel).matches(':disabled')).toBe(true)
    const replace = screen.getByRole('button', { name: `${T.correct} — ${formatDate(today)}` })
    expect(replace.matches(':disabled')).toBe(true)
    await user.click(replace)
    await user.click(within(rangeForm).getByRole('button', { name: T.show }))
    expect(commands(recorded)).toHaveLength(1)
    await act(async () => { await router.navigate('/synthetic-away') })
    await act(async () => { await router.navigate(RATES_PATH) })
    await screen.findByText(T.unknownOutcome)
    expect(screen.getByRole('form', { name: T.reverifyHeading(today) })).toBeTruthy()
    expect((screen.getByLabelText(T.reasonLabel, { exact: false }) as HTMLTextAreaElement).value).toBe('SENTETIK')
    await user.click(screen.getByRole('button', { name: T.retrySame }))
    expect(await screen.findByText(T.savedReplay)).toBeTruthy()

    const sent = commands(recorded)
    expect(sent).toHaveLength(2)
    expect(sent[0].headers['Idempotency-Key']).toBe(sent[1].headers['Idempotency-Key'])
    expect(sent[0].body).toEqual({ expectedVersion: '2', reason: 'SENTETIK' })
    expect(sent[1].body).toEqual(sent[0].body)
    expect(sent[1].url).toBe(sent[0].url)
  })

  it('preserves an import through failed reads and freezes day actions until its original range is replayed', async () => {
    const user = userEvent.setup()
    let unavailable = false
    const recorded = install((url, init) => {
      if (isList(url)) return unavailable ? problem(503) : json(dayList(url, standardDays))
      return init?.method === 'POST' && url.endsWith('/imports') ? problem(503) : undefined
    })
    const { queryClient } = renderAt(RATES_PATH)
    await screen.findByRole('table')
    const today = rangeOf(lastListUrl() ?? '').to
    await user.click(screen.getByRole('button', { name: T.importSubmit }))
    await screen.findByText(T.unknownOutcome)
    expect(screen.getByRole('button', { name: `${T.correct} — ${formatDate(today)}` }).matches(':disabled')).toBe(true)
    unavailable = true
    await act(async () => { await queryClient.refetchQueries({ predicate: query => query.queryKey.includes('settings-exchange-rates') }) })
    await waitFor(() => expect(screen.queryByRole('button', { name: T.retrySame })).toBeNull())
    unavailable = false
    await act(async () => { await queryClient.refetchQueries({ predicate: query => query.queryKey.includes('settings-exchange-rates') }) })
    await user.click(await screen.findByRole('button', { name: T.retrySame }))
    const sent = commands(recorded)
    expect(sent).toHaveLength(2)
    expect(sent[1].headers['Idempotency-Key']).toBe(sent[0].headers['Idempotency-Key'])
    expect(sent[1].body).toEqual(sent[0].body)
  })

  it('marks a half day only after a choice, with version "0" for an unrecorded day', async () => {
    const user = userEvent.setup()
    const recorded = install((url, init) => {
      if (isList(url)) return json(dayList(url, standardDays))
      return init?.method === 'POST' && url.endsWith('/half-day-marks') ? noContent() : undefined
    })
    renderAt(RATES_PATH)
    await screen.findByRole('table')
    const yesterday = addDays(rangeOf(lastListUrl() ?? '').to, -1)

    await user.click(screen.getByRole('button', { name: `${T.markHalfDay} — ${formatDate(yesterday)}` }))
    await user.type(screen.getByLabelText(T.reasonLabel, { exact: false }), 'SENTETIK arife')
    // No default choice: without one the form sends nothing.
    await user.click(screen.getByRole('button', { name: T.save }))
    expect(commands(recorded)).toEqual([])
    await user.click(screen.getByRole('radio', { name: HALF_DAY_STATE_LABEL.HalfDay }))
    await user.click(screen.getByRole('button', { name: T.save }))

    expect(await screen.findByText(T.saved)).toBeTruthy()
    const [command] = commands(recorded)
    expect(command.url).toMatch(new RegExp(`/days/${yesterday}/half-day-marks$`))
    expect(command.body).toEqual({ expectedVersion: '0', state: 'HalfDay', reason: 'SENTETIK arife' })
  })

  it('closes the form and re-reads on a version conflict', async () => {
    const user = userEvent.setup()
    let reads = 0
    install((url, init) => {
      if (isList(url)) {
        reads += 1
        return json(dayList(url, standardDays))
      }
      return init?.method === 'POST' ? problem(409, { code: 'exchange_rate_version_conflict' }) : undefined
    })
    renderAt(RATES_PATH)
    await screen.findByRole('table')
    const today = rangeOf(lastListUrl() ?? '').to

    await user.click(screen.getByRole('button', { name: `${T.reverify} — ${formatDate(today)}` }))
    await user.type(screen.getByLabelText(T.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: T.save }))

    expect(await screen.findByText(T.conflict)).toBeTruthy()
    await waitFor(() => expect(reads).toBeGreaterThanOrEqual(2))
    expect(screen.queryByRole('button', { name: T.save })).toBeNull()
  })

  it('imports a range and shows what happened per day', async () => {
    const user = userEvent.setup()
    const recorded = install((url, init) => {
      if (isList(url)) return json(dayList(url, standardDays))
      if (init?.method === 'POST' && url.endsWith('/settings/exchange-rates/imports')) {
        const body = JSON.parse(String(init.body)) as { from: string; to: string }
        return json({ days: [{ date: body.to, bulletinOutcome: 'Recorded', halfDayOutcome: 'SourceDisabled' }] })
      }
      return undefined
    })
    renderAt(RATES_PATH)
    await screen.findByRole('table')

    await user.click(screen.getByRole('button', { name: T.importSubmit }))
    const summary = await screen.findByRole('table', { name: T.importSummaryCaption })
    expect(within(summary).getByText(IMPORT_OUTCOME_LABEL.Recorded)).toBeTruthy()
    expect(within(summary).getByText(IMPORT_OUTCOME_LABEL.SourceDisabled)).toBeTruthy()
    const [command] = commands(recorded)
    expect(Object.keys(command.body as object).sort()).toEqual(['from', 'to'])
    expect(command.headers['Idempotency-Key']).toMatch(/.+/)
  })
})

describe('Kurlar — rate lookup (kur yoksa işlem durur)', () => {
  function resolution(state: 'Resolved' | 'Unavailable', url: string) {
    const query = new URL(url, 'http://localhost').searchParams
    const base = {
      rateDay: query.get('rateDay'),
      sourceCurrencyId: SOURCE_CURRENCY,
      targetCurrencyId: TARGET_CURRENCY,
      currencyCode: query.get('currencyCode'),
      state,
      source: 'Tcmb',
      halfDayRuleApplied: false,
      previousFullBusinessDay: null,
    }
    return state === 'Resolved'
      ? { ...base, bulletinDate: '2026-11-13', sellingRate: '34.1234', provenance: 'Official', recordId: RECORD, recordVersion: '1', code: null, stoppedAt: null }
      : {
          ...base,
          bulletinDate: null,
          sellingRate: null,
          provenance: null,
          recordId: null,
          recordVersion: null,
          code: 'exchange_rate_day_unconfirmed',
          stoppedAt: '2026-11-12',
        }
  }

  it('shows "Kur yok — işlem durur" with the reason and no rate when the calculator has none', async () => {
    const user = userEvent.setup()
    const recorded = install((url) => {
      if (isResolution(url)) return json(resolution('Unavailable', url))
      return isList(url) ? json(dayList(url, standardDays)) : undefined
    })
    renderAt(RATES_PATH)
    await screen.findByRole('table')

    await user.type(lookupCurrency(), 'usd')
    await user.click(screen.getByRole('button', { name: T.resolve }))
    const answer = await screen.findByRole('status', { name: T.resolutionHeading })
    expect(within(answer).getByText(T.unavailable)).toBeTruthy()
    expect(within(answer).getByText(unavailableReasonText('exchange_rate_day_unconfirmed'))).toBeTruthy()
    expect(within(answer).getByText(T.unavailableStoppedAt('2026-11-12'))).toBeTruthy()
    expect(within(answer).queryByText(/34\.1234/)).toBeNull()
    const lookup = recorded.find((request) => isResolution(request.url))
    expect(new URL(lookup?.url ?? '', 'http://localhost').searchParams.get('currencyCode')).toBe('USD')
    expect(commands(recorded)).toEqual([])
  })

  it('shows the resolved rate and the bulletin it came from', async () => {
    const user = userEvent.setup()
    install((url) => {
      if (isResolution(url)) return json(resolution('Resolved', url))
      return isList(url) ? json(dayList(url, standardDays)) : undefined
    })
    renderAt(RATES_PATH)
    await screen.findByRole('table')

    await user.type(lookupCurrency(), 'USD')
    await user.click(screen.getByRole('button', { name: T.resolve }))
    const answer = await screen.findByRole('status', { name: T.resolutionHeading })
    expect(within(answer).getByText(T.resolvedRate('USD', '34.1234'))).toBeTruthy()
    expect(within(answer).getByText(T.resolvedBulletin('2026-11-13'))).toBeTruthy()
  })

  it('never shows a rate from an Unavailable answer that carries one (off-contract)', async () => {
    const user = userEvent.setup()
    install((url) => {
      if (isResolution(url)) return json({ ...resolution('Unavailable', url), sellingRate: '34.1234' })
      return isList(url) ? json(dayList(url, standardDays)) : undefined
    })
    renderAt(RATES_PATH)
    await screen.findByRole('table')

    await user.type(lookupCurrency(), 'USD')
    await user.click(screen.getByRole('button', { name: T.resolve }))
    await waitFor(() => expect(document.querySelectorAll('[data-state="malformed-response"]').length).toBeGreaterThan(0))
    expect(screen.queryByText(T.resolvedRate('USD', '34.1234'))).toBeNull()
  })

  it('refuses TRY in the form and asks nothing', async () => {
    const user = userEvent.setup()
    const recorded = install((url) => (isList(url) ? json(dayList(url, standardDays)) : undefined))
    renderAt(RATES_PATH)
    await screen.findByRole('table')

    await user.type(lookupCurrency(), 'TRY')
    await user.click(screen.getByRole('button', { name: T.resolve }))
    expect(await screen.findByText(T.resolutionInputInvalid)).toBeTruthy()
    expect(recorded.some((request) => isResolution(request.url))).toBe(false)
  })
})

/** The last list read sent (its range is today's default window unless the test changed it). */
function lastListUrl(): string | undefined {
  const calls = (globalThis.fetch as unknown as { mock: { calls: [RequestInfo | URL, RequestInit | undefined][] } }).mock.calls
  const urls = calls.map(([input]) => (typeof input === 'string' ? input : input.toString())).filter(isList)
  return urls.at(-1)
}

function lookupCurrency(): HTMLElement {
  const form = screen.getByRole('form', { name: T.resolutionHeading })
  return within(form).getByLabelText(T.resolutionCurrencyLabel, { exact: false })
}
