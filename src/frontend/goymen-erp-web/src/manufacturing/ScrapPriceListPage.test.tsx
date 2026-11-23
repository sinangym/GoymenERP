/**
 * URT-S13, URT-D72/D73 monthly scrap prices and their corrections — component/DOM tests through the real shell, gate and auth boundary, over SENTETIK
 * fixtures. NOT real-API, real-SQL or browser acceptance, and never reported as such.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { COMPANY_A, installFetch, json, problem, renderAt, type RecordedRequest } from './testHarness'
import { MANUFACTURING_CAPABILITY } from './capabilities'

const PATH = '/uretim/hurda-fiyatlari'
const YEAR = new Date().getFullYear()
const READER = [MANUFACTURING_CAPABILITY.scrapPriceList]
const RECORDER = [MANUFACTURING_CAPABILITY.scrapPriceList, MANUFACTURING_CAPABILITY.scrapPriceRecord]
const ORIGINAL_CORRELATION = 'c0000000-0000-4000-8000-0000000000aa'

/** SENTETIK: one recorded month. */
function scrapPrice(month: number, overrides: Record<string, unknown> = {}) {
  return {
    scrapPriceId: `f0000000-0000-4000-8000-0000000000${String(month).padStart(2, '0')}`,
    year: YEAR,
    month,
    tlPerKg: '7.25',
    version: 1,
    reason: null,
    recordedBy: 'sentetik-giren',
    recordedAtUtc: '2026-11-01T08:00:00+00:00',
    ...overrides,
  }
}

function scrapPriceList(items: unknown[], year = YEAR) {
  return { year, items }
}

function requestedYear(url: string): number {
  return Number(new URL(url, 'http://sentetik.invalid').searchParams.get('year'))
}

function stub(
  options: {
    capabilities?: readonly string[]
    reads?: Array<(url: string) => Response>
    writes?: Array<() => Response>
    corrections?: Array<() => Response>
  } = {},
): { recorded: RecordedRequest[] } {
  const reads = options.reads ?? [(url: string) => json(scrapPriceList([scrapPrice(10), scrapPrice(3)], requestedYear(url)))]
  const writes = options.writes ?? []
  const corrections = options.corrections ?? []
  let read = 0
  let write = 0
  let correction = 0
  const recorded = installFetch({
    capabilities: options.capabilities ?? RECORDER,
    respond: (url, init) => {
      if (/\/manufacturing\/scrap-prices\/[0-9]+\/[0-9]+\/corrections$/.test(url)) {
        if ((init?.method ?? 'GET') !== 'POST') return undefined
        const respond = corrections[Math.min(correction, corrections.length - 1)]
        correction += 1
        return respond?.()
      }
      if (!/\/manufacturing\/scrap-prices(\?|$)/.test(url)) return undefined
      if ((init?.method ?? 'GET') === 'POST') {
        const respond = writes[Math.min(write, writes.length - 1)]
        write += 1
        return respond?.()
      }
      const respond = reads[Math.min(read, reads.length - 1)]
      read += 1
      return respond(url)
    },
  })
  return { recorded }
}

const gets = (recorded: RecordedRequest[]) =>
  recorded.filter((request) => request.method === 'GET' && /\/manufacturing\/scrap-prices\?/.test(request.url))
const posts = (recorded: RecordedRequest[]) =>
  recorded.filter((request) => request.method === 'POST' && /\/manufacturing\/scrap-prices$/.test(request.url))
const correctionPosts = (recorded: RecordedRequest[]) =>
  recorded.filter(
    (request) => request.method === 'POST' && /\/manufacturing\/scrap-prices\/[0-9]+\/[0-9]+\/corrections$/.test(request.url),
  )

async function enter(user: ReturnType<typeof userEvent.setup>, month: number, price: string) {
  await user.selectOptions(await screen.findByLabelText('Ay'), String(month))
  await user.type(screen.getByLabelText('Hurda fiyatı (TL/kg)'), price)
  await user.click(screen.getByRole('button', { name: 'Fiyatı kaydet' }))
}

async function correct(user: ReturnType<typeof userEvent.setup>, month: number, price: string, reason: string) {
  const form = await screen.findByTestId('scrap-price-correction')
  await user.selectOptions(within(form).getByLabelText('Düzeltilecek ay'), String(month))
  if (price.length > 0) await user.type(within(form).getByLabelText('Yeni hurda fiyatı (TL/kg)'), price)
  if (reason.length > 0) await user.type(within(form).getByLabelText('Gerekçe'), reason)
  await user.click(within(form).getByRole('button', { name: 'Düzeltmeyi kaydet' }))
}

/** SENTETIK: month 3 corrected once, as the server answers a correction from version 1. */
const CORRECTED = () => scrapPrice(3, { tlPerKg: '7.5', version: 2, reason: 'sentetik gerekçe' })

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('ScrapPriceListPage', () => {
  it('shows the year’s recorded months latest first, as the server’s text, and names the months without a price', async () => {
    const { recorded } = stub()
    renderAt(PATH)

    const table = await screen.findByRole('table')
    const rows = within(table).getAllByRole('row').slice(1)
    expect(rows).toHaveLength(2)
    expect(within(rows[0]).getByText(`${YEAR} Ekim`)).toBeTruthy()
    expect(within(rows[1]).getByText(`${YEAR} Mart`)).toBeTruthy()
    expect(within(rows[0]).getByText('7.25')).toBeTruthy()
    expect(within(rows[0]).getByText('sentetik-giren')).toBeTruthy()

    const missing = screen.getByTestId('scrap-price-missing').textContent ?? ''
    expect(missing).toContain('Ocak')
    expect(missing).not.toContain('Mart')
    expect(missing).not.toContain('Ekim')
    expect(screen.getByText(/"maliyet yok" olarak kalır/)).toBeTruthy()

    const [first] = gets(recorded)
    expect(first.url).toMatch(new RegExp(`/api/v1/manufacturing/scrap-prices\\?year=${YEAR}$`))
    expect(first.headers['X-Company-Id']).toBe(COMPANY_A)
    expect(posts(recorded)).toHaveLength(0)
  })

  it('shows an empty year as empty and every month as without a price', async () => {
    stub({ reads: [(url) => json(scrapPriceList([], requestedYear(url)))] })
    renderAt(PATH)
    await waitFor(() => expect(document.querySelector('[data-state="empty-no-data"]')).not.toBeNull())
    expect(screen.queryByRole('table')).toBeNull()
    expect(screen.getByTestId('scrap-price-missing').textContent).toContain('Aralık')
  })

  it('reads another year when the year changes', async () => {
    const { recorded } = stub()
    renderAt(PATH)
    await screen.findByRole('table')
    fireEvent.change(screen.getByLabelText('Yıl'), { target: { value: String(YEAR - 1) } })
    await screen.findByText(`${YEAR - 1} Ekim`)
    expect(gets(recorded).at(-1)?.url).toMatch(new RegExp(`year=${YEAR - 1}$`))
  })

  it.each([
    ['another year', (url: string) => json(scrapPriceList([scrapPrice(3)], requestedYear(url) - 1))],
    ['months not latest first', () => json(scrapPriceList([scrapPrice(3), scrapPrice(10)]))],
    ['a numeric price', () => json(scrapPriceList([scrapPrice(3, { tlPerKg: 7.25 })]))],
    ['an extra key', () => json({ ...scrapPriceList([]), total: 0 })],
  ])('fails closed on %s', async (_name, read) => {
    stub({ reads: [read] })
    renderAt(PATH)
    await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    expect(screen.queryByRole('table')).toBeNull()
  })

  it.each([401, 403])('never exposes rows after the auth boundary receives %s', async (status) => {
    stub({ reads: [() => problem(status)] })
    renderAt(PATH)
    await screen.findByRole('alert')
    expect(screen.queryByRole('table')).toBeNull()
  })

  it('shows a retryable state for a 503 and reads again on retry', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({
      reads: [() => problem(503), (url) => json(scrapPriceList([scrapPrice(3)], requestedYear(url)))],
    })
    renderAt(PATH)
    await user.click(await screen.findByRole('button', { name: 'Tekrar dene' }))
    await screen.findByRole('table')
    expect(gets(recorded)).toHaveLength(2)
  })

  it('draws no entry form without the record capability', async () => {
    stub({ capabilities: READER })
    renderAt(PATH)
    await screen.findByRole('table')
    expect(screen.queryByRole('button', { name: 'Fiyatı kaydet' })).toBeNull()
    expect(screen.queryByLabelText('Hurda fiyatı (TL/kg)')).toBeNull()
  })

  it('does not open for an actor without the list capability, and asks nothing', async () => {
    const { recorded } = stub({ capabilities: [MANUFACTURING_CAPABILITY.orderList] })
    renderAt(PATH)
    await waitFor(() => expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
    expect(gets(recorded)).toHaveLength(0)
  })

  it('refuses a price that is not plain dotted notation before sending', async () => {
    const user = userEvent.setup()
    const { recorded } = stub()
    renderAt(PATH)
    await screen.findByRole('table')
    await enter(user, 5, '7,25')
    expect(await screen.findByText(/nokta ile ayrılmış bir sayı olarak girin/)).toBeTruthy()
    expect(posts(recorded)).toHaveLength(0)
  })

  it('sends the entered text unchanged with one key and reads the list again when applied', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({ writes: [() => json(scrapPrice(5), 201)] })
    renderAt(PATH)
    await screen.findByRole('table')
    await enter(user, 5, '7.250')

    expect(await screen.findByText('Ayın hurda fiyatı kaydedildi.')).toBeTruthy()
    const [sent] = posts(recorded)
    expect(sent.body).toEqual({ year: YEAR, month: 5, tlPerKg: '7.250' })
    expect(sent.headers['X-Company-Id']).toBe(COMPANY_A)
    expect(sent.headers['Content-Type']).toBe('application/json')
    expect(sent.headers['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/i)
    await waitFor(() => expect(gets(recorded)).toHaveLength(2))
  })

  it('says a replay is the first result when the server repeats it', async () => {
    const user = userEvent.setup()
    stub({
      writes: [
        () =>
          json(scrapPrice(5), 201, {
            'Idempotency-Replayed': 'true',
            'Idempotency-Original-Correlation-Id': ORIGINAL_CORRELATION,
          }),
      ],
    })
    renderAt(PATH)
    await screen.findByRole('table')
    await enter(user, 5, '7.25')
    expect(await screen.findByText(/sunucu ilk sonucu yineledi/)).toBeTruthy()
  })

  it('shows the already-recorded refusal and reads the list again', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({
      writes: [() => problem(409, { code: 'manufacturing_scrap_price_already_recorded' })],
    })
    renderAt(PATH)
    await screen.findByRole('table')
    await enter(user, 3, '8')
    expect(await screen.findByText(/zaten girilmiş; değiştirmek için gerekçeli düzeltmeyi kullanın/)).toBeTruthy()
    await waitFor(() => expect(gets(recorded)).toHaveLength(2))
  })

  it('shows the precision refusal as the server’s decision, never rounding', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({
      writes: [() => problem(422, { code: 'manufacturing_scrap_price_precision_exceeded' })],
    })
    renderAt(PATH)
    await screen.findByRole('table')
    await enter(user, 5, '7.12345')
    expect(await screen.findByText(/beşinci basamak yuvarlanmaz, reddedilir/)).toBeTruthy()
    expect(posts(recorded)[0].body).toEqual({ year: YEAR, month: 5, tlPerKg: '7.12345' })
  })

  it('keeps an unknown outcome and resends the same attempt with the same key', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({ writes: [() => problem(503), () => json(scrapPrice(5), 201)] })
    renderAt(PATH)
    await screen.findByRole('table')
    await enter(user, 5, '7.25')

    const held = await screen.findByTestId('scrap-price-pending')
    expect(within(held).getByText(/Gönderimin sonucu bilinmiyor/)).toBeTruthy()
    // No second entry while the first is unknown.
    expect(screen.queryByRole('button', { name: 'Fiyatı kaydet' })).toBeNull()

    await user.click(within(held).getByRole('button', { name: 'Yeniden gönder' }))
    expect(await screen.findByText('Ayın hurda fiyatı kaydedildi.')).toBeTruthy()
    const [first, second] = posts(recorded)
    expect(second.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])
    expect(second.body).toEqual(first.body)
    expect(screen.queryByTestId('scrap-price-pending')).toBeNull()
  })

  it('treats replay headers that disagree as an unknown outcome, not as applied', async () => {
    const user = userEvent.setup()
    stub({ writes: [() => json(scrapPrice(5), 201, { 'Idempotency-Replayed': 'true' })] })
    renderAt(PATH)
    await screen.findByRole('table')
    await enter(user, 5, '7.25')
    expect(await screen.findByTestId('scrap-price-pending')).toBeTruthy()
    expect(screen.queryByText('Ayın hurda fiyatı kaydedildi.')).toBeNull()
  })

  it('treats an answer for another month as an unknown outcome', async () => {
    const user = userEvent.setup()
    stub({ writes: [() => json(scrapPrice(6), 201)] })
    renderAt(PATH)
    await screen.findByRole('table')
    await enter(user, 5, '7.25')
    expect(await screen.findByTestId('scrap-price-pending')).toBeTruthy()
  })

  it('refuses a future month as the server’s decision on its business day (URT-D73)', async () => {
    const user = userEvent.setup()
    stub({ writes: [() => problem(422, { code: 'manufacturing_scrap_price_future_month' })] })
    renderAt(PATH)
    await screen.findByRole('table')
    await enter(user, 12, '7.25')
    expect(await screen.findByText(/Gelecek bir ay için hurda fiyatı girilmez/)).toBeTruthy()
  })

  it('treats a first entry answered with a later version as an unknown outcome', async () => {
    const user = userEvent.setup()
    stub({ writes: [() => json(scrapPrice(5, { version: 2, reason: 'sentetik' }), 201)] })
    renderAt(PATH)
    await screen.findByRole('table')
    await enter(user, 5, '7.25')
    expect(await screen.findByTestId('scrap-price-pending')).toBeTruthy()
  })

  it('shows each month’s effective version and the latest correction’s reason (URT-D72)', async () => {
    stub({ reads: [(url) => json(scrapPriceList([scrapPrice(10), CORRECTED()], requestedYear(url)))] })
    renderAt(PATH)
    const table = await screen.findByRole('table')
    const rows = within(table).getAllByRole('row').slice(1)
    expect(within(rows[0]).getByTestId('scrap-price-version').textContent).toBe('1')
    expect(within(rows[0]).getByTestId('scrap-price-reason').textContent).toBe('İlk giriş')
    expect(within(rows[1]).getByTestId('scrap-price-version').textContent).toBe('2')
    expect(within(rows[1]).getByTestId('scrap-price-reason').textContent).toBe('sentetik gerekçe')
    expect(within(rows[1]).getByText('7.5')).toBeTruthy()
  })

  it('fails closed on a corrected month without a reason or a first entry with one', async () => {
    stub({
      reads: [
        (url) =>
          json(
            scrapPriceList([scrapPrice(10, { version: 2, reason: null }), scrapPrice(3, { reason: 'x' })], requestedYear(url)),
          ),
      ],
    })
    renderAt(PATH)
    await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    expect(screen.queryByRole('table')).toBeNull()
  })

  it('draws no correction form without the record capability', async () => {
    stub({ capabilities: READER })
    renderAt(PATH)
    await screen.findByRole('table')
    expect(screen.queryByTestId('scrap-price-correction')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Düzeltmeyi kaydet' })).toBeNull()
  })

  it('sends a correction against the shown version with its reason and one key, and reads the list again', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({ corrections: [() => json(CORRECTED(), 200)] })
    renderAt(PATH)
    await screen.findByRole('table')
    await correct(user, 3, '7.50', 'sentetik gerekçe')

    expect(await screen.findByText('Ayın hurda fiyatı yeni sürümle düzeltildi.')).toBeTruthy()
    const [sent] = correctionPosts(recorded)
    expect(sent.url).toMatch(new RegExp(`/api/v1/manufacturing/scrap-prices/${YEAR}/3/corrections$`))
    expect(sent.body).toEqual({ expectedVersion: 1, tlPerKg: '7.50', reason: 'sentetik gerekçe' })
    expect(sent.headers['X-Company-Id']).toBe(COMPANY_A)
    expect(sent.headers['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/i)
    expect(posts(recorded)).toHaveLength(0)
    await waitFor(() => expect(gets(recorded)).toHaveLength(2))
  })

  it('refuses a correction without a reason before sending', async () => {
    const user = userEvent.setup()
    const { recorded } = stub()
    renderAt(PATH)
    await screen.findByRole('table')
    await correct(user, 3, '7.5', '')
    expect(await screen.findByText('Gerekçe boş olamaz ve en çok 256 karakter olabilir.')).toBeTruthy()
    expect(correctionPosts(recorded)).toHaveLength(0)
  })

  it('shows a stale version as a conflict and reads the list again', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({
      corrections: [() => problem(409, { code: 'manufacturing_version_conflict', currentVersion: '2' })],
    })
    renderAt(PATH)
    await screen.findByRole('table')
    await correct(user, 3, '7.5', 'sentetik gerekçe')
    expect(await screen.findByText(/başka bir sürümle değişti; düzeltme kaydedilmedi/)).toBeTruthy()
    await waitFor(() => expect(gets(recorded)).toHaveLength(2))
  })

  it.each([
    ['manufacturing_scrap_price_unchanged', /geçerli fiyatıyla aynı; yeni sürüm açılmadı/],
    ['manufacturing_scrap_price_future_month', /Gelecek bir ayın hurda fiyatı düzeltilmez/],
  ])('shows the %s refusal as the server’s decision', async (code, text) => {
    const user = userEvent.setup()
    stub({ corrections: [() => problem(422, { code })] })
    renderAt(PATH)
    await screen.findByRole('table')
    await correct(user, 3, '7.25', 'sentetik gerekçe')
    expect(await screen.findByText(text)).toBeTruthy()
  })

  it('keeps an unknown correction and resends the same attempt with the same key', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({ corrections: [() => problem(503), () => json(CORRECTED(), 200)] })
    renderAt(PATH)
    await screen.findByRole('table')
    await correct(user, 3, '7.5', 'sentetik gerekçe')

    const held = await screen.findByTestId('scrap-price-correction-pending')
    expect(screen.queryByRole('button', { name: 'Düzeltmeyi kaydet' })).toBeNull()
    await user.click(within(held).getByRole('button', { name: 'Yeniden gönder' }))
    expect(await screen.findByText('Ayın hurda fiyatı yeni sürümle düzeltildi.')).toBeTruthy()
    const [first, second] = correctionPosts(recorded)
    expect(second.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])
    expect(second.body).toEqual(first.body)
  })

  it('treats a correction answered with another version or reason as an unknown outcome', async () => {
    const user = userEvent.setup()
    stub({ corrections: [() => json(scrapPrice(3, { tlPerKg: '7.5', version: 3, reason: 'sentetik gerekçe' }), 200)] })
    renderAt(PATH)
    await screen.findByRole('table')
    await correct(user, 3, '7.5', 'sentetik gerekçe')
    expect(await screen.findByTestId('scrap-price-correction-pending')).toBeTruthy()
    expect(screen.queryByText('Ayın hurda fiyatı yeni sürümle düzeltildi.')).toBeNull()
  })
})
