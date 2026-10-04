/**
 * U-05 price lists in the REAL shell (AppShell, ShellProvider, CapabilityRoute) with a stubbed fetch: create → save →
 * the stored list reopened as the server answers it; one key kept across a lost reply; the draft revised, published
 * and a new version opened, each re-reading the server; the controls follow their own capabilities. Component
 * evidence only (NOT_ENV): the answers here are the harness's.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { ShellProvider } from '../../shell/ShellProvider'
import { AppShell } from '../../shell/AppShell'
import { SHELL_CAPABILITY_CONTRACT_VERSION } from '../../shell/api'
import { PRICE_LIST_CAPABILITY } from './capabilities'
import { formatMoneyText, parseMoneyInput } from './contract'
import { priceListRoutes } from './priceListRoutes'
import { resetRetainedPriceListAttempts } from './priceListAttempt'
import { PRICE_LIST_TEXT as T, PRICE_LIST_CODE_TEXT } from './priceListText'

const COMPANY_A = '99999999-9999-9999-9999-999999999999'
const PRODUCT = '22222222-2222-2222-2222-222222222222'
const KG = '44444444-4444-4444-4444-444444444444'
const LIST = 'cccccccc-cccc-cccc-cccc-cccccccccccc'
const LIST_51 = 'dddddddd-dddd-4ddd-8ddd-dddddddddddd'
const ALL = Object.values(PRICE_LIST_CAPABILITY)

interface Call { readonly url: string; readonly method: string; readonly key: string | null; readonly body: string | null; readonly company: string | null }

function json(body: unknown, status = 200, headers: Record<string, string> = {}): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } })
}

function line(overrides: Record<string, unknown> = {}) {
  return { lineId: 'l1', lineNo: 1, productId: PRODUCT, productCode: 'SYN-1', productName: 'Sentetik bant', priceUnitId: KG,
    unitPrice: '42000.5', floorUnitPrice: '40000', lowerQuantity: '0', upperQuantity: null, ...overrides }
}

function version(overrides: Record<string, unknown> = {}) {
  return { versionId: 'v1', versionNo: 1, state: 'Draft', status: 'Draft', validFrom: '2026-10-01', validThrough: null,
    createdAtUtc: '2026-09-30T09:00:00Z', createdBy: 'u', publishedAtUtc: null, publishedBy: null, lines: [line()], ...overrides }
}

function view(overrides: Record<string, unknown> = {}) {
  return { id: LIST, name: 'Yurt içi satış', currencyCode: 'TRY', channel: 'Sales', vatBasis: 'ExcludingVat', version: '1',
    createdAtUtc: '2026-09-30T09:00:00Z', updatedAtUtc: '2026-09-30T09:00:00Z', activeVersionNo: null, draftVersionNo: 1,
    versions: [version()], ...overrides }
}

function summary(overrides: Record<string, unknown> = {}) {
  return { id: LIST, name: 'Yurt içi satış', currencyCode: 'TRY', channel: 'Sales', vatBasis: 'ExcludingVat',
    version: '3', activeVersionNo: 1, activeValidFrom: '2026-10-01', draftVersionNo: 2, versionCount: 2,
    updatedAtUtc: '2026-09-30T09:00:00Z', ...overrides }
}

function install(handlers: {
  capabilities?: readonly string[]
  onDetail?: () => Response | Promise<Response>
  onList?: (url: string) => Response | Promise<Response>
  onOptions?: (url: string) => Response | Promise<Response>
  onCommand?: (call: Call, count: number) => Response | Promise<Response>
}) {
  const calls: Call[] = []
  let commands = 0
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = typeof input === 'string' ? input : input.toString()
    const headers = (init?.headers ?? {}) as Record<string, string>
    const call: Call = { url, method: (init?.method ?? 'GET').toUpperCase(), key: headers['Idempotency-Key'] ?? null,
      body: typeof init?.body === 'string' ? init.body : null, company: headers['X-Company-Id'] ?? null }
    calls.push(call)
    if (url.includes('/identity/me')) return json({ userId: '77777777-7777-7777-7777-777777777777', displayName: 'Fiyat', email: null, allowedCompanyIds: [COMPANY_A] })
    if (url.includes('/organization/companies')) return json([{ id: COMPANY_A, code: 'GDC', legalName: 'Göymen A', isPlaceholder: true }])
    if (url.includes('/shell/capabilities')) {
      return json({ contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION, generation: 'gen-1', capabilities: handlers.capabilities ?? ALL })
    }
    if (url.includes('/price-lists/product-options')) return handlers.onOptions!(url)
    if (url.includes('/price-lists') && call.method === 'POST') return handlers.onCommand!(call, ++commands)
    if (/\/price-lists\/[^/?]+$/.test(url)) return handlers.onDetail!()
    if (url.includes('/price-lists')) return handlers.onList!(url)
    throw new Error(`unexpected fetch ${url}`)
  }))
  return { calls, posts: () => calls.filter((call) => call.method === 'POST') }
}

function renderApp(path: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0, refetchOnWindowFocus: false } } })
  const router = createMemoryRouter([{ path: '/', element: <AppShell />, children: priceListRoutes }], { initialEntries: [path] })
  render(<ShellProvider queryClient={queryClient}><RouterProvider router={router} /></ShellProvider>)
  return router
}

afterEach(() => {
  cleanup()
  resetRetainedPriceListAttempts()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('money text', () => {
  it('reads what a person types without guessing thousands and shows the server digits the Turkish way', () => {
    expect(parseMoneyInput('42000,5')).toBe('42000.5')
    expect(parseMoneyInput(' 0042000.50 ')).toBe('42000.50')
    expect(parseMoneyInput('42.000,50')).toBeNull()
    expect(parseMoneyInput('1,23456')).toBeNull()
    expect(parseMoneyInput('-1')).toBeNull()
    expect(formatMoneyText('42000.5')).toBe('42.000,5')
    expect(formatMoneyText('999')).toBe('999')
  })
})

describe('U-05 price lists', () => {
  it('lists the company lists with the version that prices today', async () => {
    install({ onList: () => json({ items: [summary()], nextCursor: null }) })
    renderApp('/fiyat-listeleri')
    const table = await screen.findByTestId('price-list-table')
    expect(within(table).getByRole('link', { name: 'Yurt içi satış' }).getAttribute('href')).toBe(`/fiyat-listeleri/${LIST}`)
    expect(table.textContent).toContain(T.activeFrom(1, '01.10.2026'))
    expect(table.textContent).toContain(T.draftVersion(2))
    expect(screen.getByTestId('price-list-new')).toBeTruthy()
  })

  it('uses the opaque continuation and exposes the next record without losing page one', async () => {
    const harness = install({
      onList: (url) => url.includes('cursor=opaque%2Frecord-51%3D%3D')
        ? json({ items: [summary({ id: LIST_51, name: '51. fiyat listesi' })], nextCursor: null })
        : json({ items: [summary()], nextCursor: 'opaque/record-51==' }),
    })
    renderApp('/fiyat-listeleri')
    expect(await screen.findByRole('link', { name: 'Yurt içi satış' })).toBeTruthy()

    await userEvent.click(screen.getByRole('button', { name: T.more }))

    expect(await screen.findByRole('link', { name: '51. fiyat listesi' })).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Yurt içi satış' })).toBeTruthy()
    expect(harness.calls.filter((call) => call.url.includes('/price-lists'))
      .map((call) => call.url)).toEqual(['/api/v1/price-lists', '/api/v1/price-lists?cursor=opaque%2Frecord-51%3D%3D'])
  })

  it('reports a product-option 403 to the central auth boundary', async () => {
    install({ onOptions: () => json({ code: 'forbidden' }, 403) })
    renderApp('/fiyat-listeleri/yeni')
    await screen.findByLabelText(T.productSearch)

    await userEvent.click(screen.getByRole('button', { name: T.search }))

    await waitFor(() => expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
    expect(screen.queryByLabelText(T.productSearch)).toBeNull()
  })

  it('reports a command 401 to the central auth boundary and ends the current session', async () => {
    install({ onDetail: () => json(view()), onCommand: () => json({ code: 'authentication_required' }, 401) })
    renderApp(`/fiyat-listeleri/${LIST}`)

    await userEvent.click(await screen.findByTestId('price-list-publish'))

    expect(await screen.findByText(/Oturum sonlandı/i)).toBeTruthy()
  })

  it('retains one unknown create across departure while the departed callback cannot navigate', async () => {
    let stored = view()
    let releaseFirst!: () => void
    const firstResponse = new Promise<Response>((resolve) => {
      releaseFirst = () => resolve(json({ priceList: stored, isReplay: false, originalCorrelationId: 'first-create' }, 201))
    })
    const harness = install({
      onList: () => json({ items: [summary()], nextCursor: null }),
      onDetail: () => json(stored),
      onCommand: (call, count) => {
        const sent = JSON.parse(call.body ?? '{}') as { priceListId: string; versionId: string }
        stored = view({ id: sent.priceListId, versions: [version({ versionId: sent.versionId })] })
        if (count === 1) return firstResponse

        return json({ priceList: stored, isReplay: true, originalCorrelationId: 'first-create' }, 200, {
          'Idempotency-Replayed': 'true',
          'Idempotency-Original-Correlation-Id': 'first-create',
        })
      },
    })
    const router = renderApp('/fiyat-listeleri/yeni')
    await userEvent.type(await screen.findByLabelText(T.name), 'Terk edilen kayıt')
    await userEvent.click(screen.getByTestId('price-list-save'))
    await waitFor(() => expect(harness.posts()).toHaveLength(1))

    await act(async () => { await router.navigate('/fiyat-listeleri') })
    await screen.findByTestId('price-list-table')
    await act(async () => { await router.navigate('/fiyat-listeleri/yeni') })
    await screen.findByText(T.notice.unknown)

    await act(async () => {
      releaseFirst()
      await new Promise((resolve) => setTimeout(resolve, 0))
    })
    expect(router.state.location.pathname).toBe('/fiyat-listeleri/yeni')
    expect(screen.getByText(T.notice.unknown)).toBeTruthy()

    await userEvent.click(screen.getByRole('button', { name: T.notice.retry }))
    expect(await screen.findByRole('heading', { name: 'Yurt içi satış' })).toBeTruthy()
    const posts = harness.posts()
    expect(posts).toHaveLength(2)
    expect(posts[1].key).toBe(posts[0].key)
    expect(posts[1].body).toBe(posts[0].body)
  })

  it('creates a list, keeps one key across a lost reply, and opens the stored list as the server answers it', async () => {
    let reads = 0
    let stored = view({ versions: [version({ lines: [
      line({ upperQuantity: '10' }),
      line({ lineId: 'l2', lineNo: 2, lowerQuantity: '10', upperQuantity: '20' }),
    ] })] })
    const harness = install({
      onOptions: () => json({ items: [{ productId: PRODUCT, code: 'SYN-1', name: 'Sentetik bant', units: [{ unitId: KG, role: 'Sales', code: 'kg' }] }],
        hasMore: false }),
      onCommand: (call, count) => {
        const submitted = JSON.parse(call.body ?? '{}') as {
          priceListId: string
          versionId: string
          lines: [{ lineId: string }, { lineId: string }]
        }
        stored = view({ id: submitted.priceListId, versions: [version({ versionId: submitted.versionId, lines: [
          line({ lineId: submitted.lines[0].lineId, upperQuantity: '10' }),
          line({ lineId: submitted.lines[1].lineId, lineNo: 2, lowerQuantity: '10', upperQuantity: '20' }),
        ] })] })
        if (count === 1) throw new TypeError('Failed to fetch')
        return json({ priceList: stored, isReplay: true, originalCorrelationId: 'c1' }, 200, {
          'Idempotency-Replayed': 'true',
          'Idempotency-Original-Correlation-Id': 'c1',
        })
      },
      onDetail: () => {
        reads += 1
        return json(stored)
      },
    })
    renderApp('/fiyat-listeleri/yeni')
    await userEvent.type(await screen.findByLabelText(T.name), 'Yurt içi satış')
    await userEvent.clear(screen.getByLabelText(T.validFrom))
    await userEvent.type(screen.getByLabelText(T.validFrom), '2026-10-01')
    await userEvent.type(screen.getByLabelText(T.productSearch), 'SYN')
    await userEvent.click(screen.getByRole('button', { name: T.search }))
    await userEvent.click(await screen.findByRole('button', { name: T.addLine }))
    await userEvent.click(screen.getByRole('button', { name: T.addLine }))
    await userEvent.type(screen.getByLabelText(`1. ${T.unitPrice}`), '42000,5')
    await userEvent.type(screen.getByLabelText(`1. ${T.floorUnitPrice}`), '40000')
    await userEvent.type(screen.getByLabelText(`1. ${T.upperQuantity}`), '10')
    await userEvent.type(screen.getByLabelText(`2. ${T.unitPrice}`), '41000')
    await userEvent.clear(screen.getByLabelText(`2. ${T.lowerQuantity}`))
    await userEvent.type(screen.getByLabelText(`2. ${T.lowerQuantity}`), '10')
    await userEvent.type(screen.getByLabelText(`2. ${T.upperQuantity}`), '20')
    await userEvent.click(screen.getByTestId('price-list-save'))

    await screen.findByText(T.notice.unknown)
    cleanup()
    renderApp('/fiyat-listeleri/yeni')
    await screen.findByText(T.notice.unknown)
    await userEvent.click(screen.getByRole('button', { name: T.notice.retry }))
    expect(await screen.findByRole('heading', { name: 'Yurt içi satış' })).toBeTruthy()
    expect(screen.getAllByTestId('price-list-line-price')[0].textContent).toBe('42.000,5')
    expect(screen.getAllByTestId('price-list-line-quantity-tier').map((node) => node.textContent))
      .toEqual(['0 ≤ miktar < 10', '10 ≤ miktar < 20'])
    expect(reads).toBeGreaterThan(0)

    const posts = harness.posts()
    expect(posts).toHaveLength(2)
    expect(posts[0].key).toBe(posts[1].key)
    expect(posts[0].body).toBe(posts[1].body)
    expect(posts[0].company).toBe(COMPANY_A)
    const sent = JSON.parse(posts[0].body ?? '{}') as Record<string, unknown>
    expect(stored.id).toBe(sent.priceListId)
    expect(stored.versions[0].versionId).toBe(sent.versionId)
    expect(sent).toMatchObject({ name: 'Yurt içi satış', currencyCode: 'TRY', channel: 'Sales', vatBasis: 'ExcludingVat', validFrom: '2026-10-01',
      validThrough: null })
    expect((sent.lines as Record<string, unknown>[])[0]).toMatchObject({ productId: PRODUCT, priceUnitId: KG, unitPrice: '42000.5', floorUnitPrice: '40000',
      lowerQuantity: '0', upperQuantity: '10' })
    expect((sent.lines as Record<string, unknown>[])[1]).toMatchObject({ productId: PRODUCT, priceUnitId: KG, unitPrice: '41000', floorUnitPrice: null,
      lowerQuantity: '10', upperQuantity: '20' })
  })

  it('does not send a price it cannot read', async () => {
    const harness = install({
      onOptions: () => json({ items: [{ productId: PRODUCT, code: 'SYN-1', name: 'Sentetik bant', units: [{ unitId: KG, role: 'Sales', code: 'kg' }] }],
        hasMore: false }),
    })
    renderApp('/fiyat-listeleri/yeni')
    await userEvent.type(await screen.findByLabelText(T.name), 'Liste')
    await userEvent.click(screen.getByRole('button', { name: T.search }))
    await userEvent.click(await screen.findByRole('button', { name: T.addLine }))
    await userEvent.type(screen.getByLabelText(`1. ${T.unitPrice}`), '42.000,50')
    await userEvent.click(screen.getByTestId('price-list-save'))
    expect((await screen.findByRole('alert')).textContent).toBe(T.localErrors.price(1))
    expect(harness.posts()).toHaveLength(0)
  })

  it('revises the draft with the expected version, publishes it and reopens it as the server stored it', async () => {
    let state = view({ versions: [version({ lines: [line({ lowerQuantity: '10', upperQuantity: '20' })] })] })
    const harness = install({
      onDetail: () => json(state),
      onCommand: (call) => {
        if (call.url.endsWith('/draft-revision')) {
          state = view({ version: '2', versions: [version({ lines: [line({ unitPrice: '41000', floorUnitPrice: null })] })] })
        } else {
          state = view({ version: '3', activeVersionNo: 1, draftVersionNo: null, versions: [version({ state: 'Published', status: 'Active',
            publishedBy: 'yayinci', publishedAtUtc: '2026-10-01T08:00:00Z', lines: [line({ unitPrice: '41000', floorUnitPrice: null })] })] })
        }
        return json({ priceList: state, isReplay: false, originalCorrelationId: 'c' })
      },
    })
    renderApp(`/fiyat-listeleri/${LIST}`)
    await userEvent.click(await screen.findByTestId('price-list-edit'))
    const price = screen.getByLabelText(`1. ${T.unitPrice}`)
    expect((price as HTMLInputElement).value).toBe('42000,5')
    expect((screen.getByLabelText(`1. ${T.lowerQuantity}`) as HTMLInputElement).value).toBe('10')
    expect((screen.getByLabelText(`1. ${T.upperQuantity}`) as HTMLInputElement).value).toBe('20')
    await userEvent.clear(price)
    await userEvent.type(price, '41000')
    await userEvent.clear(screen.getByLabelText(`1. ${T.floorUnitPrice}`))
    await userEvent.click(screen.getByTestId('price-list-save-draft'))
    await waitFor(() => expect(screen.getByTestId('price-list-version').textContent).toBe('2'))
    const revision = JSON.parse(harness.posts()[0].body ?? '{}') as Record<string, unknown>
    expect(revision).toMatchObject({ expectedVersion: '1', name: 'Yurt içi satış', validFrom: '2026-10-01' })
    expect((revision.lines as Record<string, unknown>[])[0]).toMatchObject({ lineId: 'l1', unitPrice: '41000', floorUnitPrice: null,
      lowerQuantity: '10', upperQuantity: '20' })

    await userEvent.click(screen.getByTestId('price-list-publish'))
    await waitFor(() => expect(screen.getByTestId('price-list-version-1-status').textContent).toBe('Geçerli'))
    expect(JSON.parse(harness.posts()[1].body ?? '{}')).toEqual({ expectedVersion: '2', versionNo: 1 })
    expect(harness.posts()[1].url).toBe(`/api/v1/price-lists/${LIST}/publication`)
    expect(screen.queryByTestId('price-list-publish')).toBeNull()
    expect(screen.getByTestId('price-list-start-version')).toBeTruthy()
  })

  it('keeps a lost publish reachable after reopen even when the fresh GET no longer has a draft', async () => {
    let state = view()
    const harness = install({
      onDetail: () => json(state),
      onCommand: (_call, count) => {
        state = view({ version: '2', activeVersionNo: 1, draftVersionNo: null, versions: [version({
          state: 'Published', status: 'Active', publishedBy: 'yayinci', publishedAtUtc: '2026-10-01T08:00:00Z',
        })] })
        if (count === 1) throw new TypeError('Failed to fetch')
        return json({ priceList: state, isReplay: true, originalCorrelationId: 'publish-corr' }, 200, {
          'Idempotency-Replayed': 'true',
          'Idempotency-Original-Correlation-Id': 'publish-corr',
        })
      },
    })
    renderApp(`/fiyat-listeleri/${LIST}`)
    await userEvent.click(await screen.findByTestId('price-list-publish'))
    await screen.findByText(T.notice.unknown)

    cleanup()
    renderApp(`/fiyat-listeleri/${LIST}`)
    await screen.findByText(T.notice.unknown)
    expect(screen.queryByTestId('price-list-publish')).toBeNull()
    await userEvent.click(screen.getByRole('button', { name: T.notice.retry }))

    await waitFor(() => expect(screen.queryByText(T.notice.unknown)).toBeNull())
    expect(screen.getByTestId('price-list-version-1-status').textContent).toBe('Geçerli')
    const posts = harness.posts()
    expect(posts).toHaveLength(2)
    expect(posts[0].key).toBe(posts[1].key)
    expect(posts[0].body).toBe(posts[1].body)
    expect(posts[0].url).toBe(`/api/v1/price-lists/${LIST}/publication`)
  })

  it('keeps a lost start-version reachable after reopen even when the fresh GET already has the new draft', async () => {
    const published = version({ state: 'Published', status: 'Active', publishedBy: 'yayinci', publishedAtUtc: '2026-10-01T08:00:00Z' })
    let state = view({ version: '2', activeVersionNo: 1, draftVersionNo: null, versions: [published] })
    const harness = install({
      onDetail: () => json(state),
      onCommand: (call, count) => {
        const request = JSON.parse(call.body ?? '{}') as { versionId?: string; validFrom?: string; validThrough?: string | null }
        state = view({ version: '3', activeVersionNo: 1, draftVersionNo: 2, versions: [published, version({
          versionId: request.versionId, versionNo: 2, validFrom: request.validFrom, validThrough: request.validThrough, lines: [],
        })] })
        if (count === 1) throw new TypeError('Failed to fetch')
        return json({ priceList: state, isReplay: true, originalCorrelationId: 'start-corr' }, 200, {
          'Idempotency-Replayed': 'true',
          'Idempotency-Original-Correlation-Id': 'start-corr',
        })
      },
    })
    renderApp(`/fiyat-listeleri/${LIST}`)
    await userEvent.click(await screen.findByTestId('price-list-start-version'))
    await screen.findByText(T.notice.unknown)

    cleanup()
    renderApp(`/fiyat-listeleri/${LIST}`)
    await screen.findByText(T.notice.unknown)
    expect(screen.queryByTestId('price-list-start-version')).toBeNull()
    await userEvent.click(screen.getByRole('button', { name: T.notice.retry }))

    await waitFor(() => expect(screen.queryByText(T.notice.unknown)).toBeNull())
    expect(screen.getByTestId('price-list-version-2-status').textContent).toBe('Taslak')
    const posts = harness.posts()
    expect(posts).toHaveLength(2)
    expect(posts[0].key).toBe(posts[1].key)
    expect(posts[0].body).toBe(posts[1].body)
    expect(posts[0].url).toBe(`/api/v1/price-lists/${LIST}/versions`)
  })

  it('a stale version is refused with its reason and the list is re-read', async () => {
    let reads = 0
    install({
      onDetail: () => json(++reads === 1 ? view() : view({ version: '5' })),
      onCommand: () => json({ status: 409, code: 'version_conflict', errors: [{ field: 'expectedVersion', code: 'version_conflict' }] }, 409),
    })
    renderApp(`/fiyat-listeleri/${LIST}`)
    await userEvent.click(await screen.findByTestId('price-list-publish'))
    await waitFor(() => expect(screen.getByTestId('price-list-version').textContent).toBe('5'))
    expect(screen.getAllByTestId('price-list-notice').some((node) => node.textContent === `${T.notice.conflict} ${PRICE_LIST_CODE_TEXT.version_conflict}`))
      .toBe(true)
  })

  it('a reader sees versions and no command; each command follows its own capability', async () => {
    install({ capabilities: [PRICE_LIST_CAPABILITY.list, PRICE_LIST_CAPABILITY.detail], onDetail: () => json(view()) })
    renderApp(`/fiyat-listeleri/${LIST}`)
    expect((await screen.findByTestId('price-list-version-1-status')).textContent).toBe('Taslak')
    expect(screen.queryByTestId('price-list-edit')).toBeNull()
    expect(screen.queryByTestId('price-list-publish')).toBeNull()
    cleanup()
    vi.unstubAllGlobals()

    install({ capabilities: [PRICE_LIST_CAPABILITY.detail, PRICE_LIST_CAPABILITY.publish], onDetail: () => json(view()) })
    renderApp(`/fiyat-listeleri/${LIST}`)
    expect(await screen.findByTestId('price-list-publish')).toBeTruthy()
    expect(screen.queryByTestId('price-list-edit')).toBeNull()
  })
})
