/**
 * URT-S13 monthly scrap prices — component/DOM tests through the real shell, gate and auth boundary, over SENTETIK
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
  } = {},
): { recorded: RecordedRequest[] } {
  const reads = options.reads ?? [(url: string) => json(scrapPriceList([scrapPrice(10), scrapPrice(3)], requestedYear(url)))]
  const writes = options.writes ?? []
  let read = 0
  let write = 0
  const recorded = installFetch({
    capabilities: options.capabilities ?? RECORDER,
    respond: (url, init) => {
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

async function enter(user: ReturnType<typeof userEvent.setup>, month: number, price: string) {
  await user.selectOptions(await screen.findByLabelText('Ay'), String(month))
  await user.type(screen.getByLabelText('Hurda fiyatı (TL/kg)'), price)
  await user.click(screen.getByRole('button', { name: 'Fiyatı kaydet' }))
}

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
    expect(await screen.findByText(/zaten girilmiş; girilmiş ay değiştirilmez/)).toBeTruthy()
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
})
