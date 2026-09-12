import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import { PRODUCT_CAPABILITY } from './capabilities'
import { PRODUCT_1, installFetch, json, productDetail, renderProductApp } from './testHarness'

const DETAIL_ONLY = [PRODUCT_CAPABILITY.productsDetail]
const PATH = `/urun/${PRODUCT_1}`

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

async function detailVisible() {
  return waitFor(() => screen.getByRole('heading', { name: 'Siyah sac' }))
}

describe('mounting', () => {
  it('mounts nothing and reads nothing without the detail capability', async () => {
    const { calls } = installFetch({ capabilities: [PRODUCT_CAPABILITY.productsList] })
    renderProductApp(PATH)

    await waitFor(() => expect(screen.getByText('Bulunamadı — sayfa')).toBeTruthy())
    expect(calls.some((call) => call.url.includes('/products/'))).toBe(false)
  })

  it('reads the 360 with the detail capability', async () => {
    installFetch({ capabilities: DETAIL_ONLY })
    renderProductApp(PATH)

    await detailVisible()
    expect(screen.getByText('SAC-42')).toBeTruthy()
  })

  it('never attaches a company header to the detail read', async () => {
    const { calls } = installFetch({ capabilities: DETAIL_ONLY })
    renderProductApp(PATH)

    await detailVisible()
    const detailCalls = calls.filter((call) => call.url.includes('/products/'))
    expect(detailCalls.length).toBeGreaterThan(0)
    expect(detailCalls.every((call) => call.companyId === null)).toBe(true)
  })
})

describe('lifecycle', () => {
  it('prints what the state MEANS, not only its name', async () => {
    installFetch({
      capabilities: DETAIL_ONLY,
      onDetail: () => json(productDetail({ lifecycle: 'Draft', lastActivatedAt: null })),
    })
    renderProductApp(PATH)

    await detailVisible()
    expect(screen.getByText('Taslak')).toBeTruthy()
    // "Taslak" alone does not tell an operator the product cannot be picked in an
    // order line, which is the whole point of the state (§6.1, DEC-PRD-010).
    expect(screen.getByText('Kesin işlemlerde seçilemez.')).toBeTruthy()
  })

  it('banners an inactive product as closed in BOTH companies, with its reason', async () => {
    installFetch({
      capabilities: DETAIL_ONLY,
      onDetail: () =>
        json(
          productDetail({
            lifecycle: 'Inactive',
            deactivationReason: 'Üretimden kaldırıldı',
            lastDeactivatedAt: '2026-08-01T00:00:00Z',
          }),
        ),
    })
    renderProductApp(PATH)

    await detailVisible()
    expect(screen.getByText(/her iki şirkette de yeni kullanıma kapalıdır/)).toBeTruthy()
    expect(screen.getByText(/Üretimden kaldırıldı/)).toBeTruthy()
  })
})

describe('roles', () => {
  it('shows an unevaluated role as unevaluated, never as "Hayır"', async () => {
    installFetch({ capabilities: DETAIL_ONLY })
    renderProductApp(PATH)

    await detailVisible()
    // §5.2: an unevaluated role must not be persisted — or shown — as false.
    expect(screen.getByTestId('product-role-productionInput').textContent).toBe('—')
    expect(screen.getByText('değerlendirilmedi')).toBeTruthy()
    // ...while a determined NO still reads as a determined NO.
    expect(screen.getByTestId('product-role-sellable').textContent).toBe('Hayır')
  })

  it('distinguishes a derived role from one a person typed', async () => {
    installFetch({ capabilities: DETAIL_ONLY })
    renderProductApp(PATH)

    await detailVisible()
    const row = screen.getByRole('row', { name: /Üretim çıktısı/ })
    // Only one of the two is safe to overwrite by hand.
    expect(within(row).getByText('ilişkiden türetildi')).toBeTruthy()
  })
})

describe('completeness', () => {
  it('says "bilinmiyor" for what the server could not evaluate', async () => {
    installFetch({ capabilities: DETAIL_ONLY })
    renderProductApp(PATH)

    await detailVisible()
    // §9 forbids fabricating a value for unsupported data; "eksik" would send an
    // operator looking for a gap that may not exist.
    expect(screen.getByTestId('product-completeness-package').textContent).toBe('bilinmiyor')
    expect(screen.getByTestId('product-completeness-unit').textContent).toBe('eksik')
  })
})

describe('sections the server cannot serve', () => {
  it('marks an unlisted section unavailable rather than empty or zero', async () => {
    installFetch({ capabilities: DETAIL_ONLY })
    renderProductApp(PATH)

    await detailVisible()
    // `packageStandards` is absent from availableSections in the default fixture.
    const section = screen.getByTestId('section-packageStandards')
    expect(section.textContent).toContain('Bu bölüm şu anda kullanılamıyor')
    expect(section.textContent).toContain('Sayı veya özet üretilmez')
  })

  it('renders a listed section normally', async () => {
    installFetch({ capabilities: DETAIL_ONLY })
    renderProductApp(PATH)

    await detailVisible()
    const units = screen.getByTestId('section-units')
    expect(units.textContent).not.toContain('kullanılamıyor')
  })
})

describe('units', () => {
  it('shows an unassigned unit role as unassigned, never as the stock unit', async () => {
    installFetch({ capabilities: DETAIL_ONLY })
    renderProductApp(PATH)

    await detailVisible()
    // DEC-PRD-006 makes the six unit roles separate; a fallback to `kg` would be
    // the screen inventing a conversion.
    expect(screen.getByTestId('product-unit-stock').textContent).toBe('kg')
    expect(screen.getByTestId('product-unit-production').textContent).toBe('atanmadı')
  })
})

describe('template', () => {
  it('shows the PINNED template version', async () => {
    installFetch({ capabilities: DETAIL_ONLY })
    renderProductApp(PATH)

    await detailVisible()
    // DEC-PRDCT-008: a master change must not rewrite what a historical record
    // meant, so the version is carried, never re-derived from "current".
    expect(screen.getByText('v3')).toBeTruthy()
  })
})

describe('master vs label boundary', () => {
  it('says the card holds only the tracking POLICY', async () => {
    installFetch({ capabilities: DETAIL_ONLY })
    renderProductApp(PATH)

    await detailVisible()
    // DEC-PRD-003: lot, origin and quality/defect status belong to the physical
    // label record. Without this line an empty policy reads as "no lots".
    expect(screen.getByText(/Parti, menşe ve kalite\/kusur durumu fiziksel etiket kaydında/)).toBeTruthy()
  })

  it('shows no stock, price or lot FIGURE anywhere on the card', async () => {
    installFetch({ capabilities: DETAIL_ONLY })
    const { container } = renderProductApp(PATH)

    await detailVisible()
    const text = container.textContent ?? ''
    // §3/§9: the 360 does not pull physical stock, price or accounting detail into
    // the Product aggregate.
    for (const forbidden of ['Stok miktarı', 'Bakiye', 'Parti no', 'Kalite durumu', '₺']) {
      expect(text).not.toContain(forbidden)
    }
    /**
     * "Fiyat birimi" IS present and is allowed: DEC-PRD-006 makes the price UNIT a
     * master field (which unit a price is quoted in), while the price itself is a
     * company/customer-scoped child record. The distinction is the point — the card
     * says `kg`, never an amount.
     */
    expect(screen.getByTestId('product-unit-price').textContent).toBe('kg')
  })
})

describe('alias history', () => {
  it('labels a retired code as non-reusable', async () => {
    installFetch({ capabilities: DETAIL_ONLY })
    renderProductApp(PATH)

    await detailVisible()
    expect(screen.getByText('ESKI-1')).toBeTruthy()
    expect(screen.getByText(/tekrar kullanılamaz/)).toBeTruthy()
  })

  it('says so plainly when there is no alias', async () => {
    installFetch({
      capabilities: DETAIL_ONLY,
      onDetail: () => json(productDetail({ aliases: [] })),
    })
    renderProductApp(PATH)

    await detailVisible()
    expect(screen.getByText('Bu ürünün emekliye ayrılmış kodu yok.')).toBeTruthy()
  })
})

describe('§13 states', () => {
  it('shows the non-disclosing not-found block for an unknown product', async () => {
    installFetch({
      capabilities: DETAIL_ONLY,
      onDetail: () => json({ code: 'not_found' }, 404),
    })
    renderProductApp(PATH)

    await waitFor(() => expect(screen.getByText('Bulunamadı — ürün')).toBeTruthy())
  })

  it('fails closed when the response describes a different product', async () => {
    /**
     * A well-formed 200 about another record would have the user looking at one
     * product believing it is another — and any edit they then made would be aimed
     * at the wrong revision.
     */
    installFetch({
      capabilities: DETAIL_ONLY,
      onDetail: () => json(productDetail({ id: '99999999-9999-9999-9999-999999999999' })),
    })
    renderProductApp(PATH)

    await waitFor(() => expect(screen.getByText('Beklenmeyen yanıt — ürün')).toBeTruthy())
  })

  it('offers a retry on a service failure', async () => {
    installFetch({
      capabilities: DETAIL_ONLY,
      onDetail: () => json({ code: 'service_unavailable' }, 503),
    })
    renderProductApp(PATH)

    await waitFor(() => expect(screen.getByRole('button', { name: 'Tekrar dene' })).toBeTruthy())
  })
})

describe('navigation', () => {
  it('offers the way back only when the list capability is held', async () => {
    installFetch({ capabilities: DETAIL_ONLY })
    const { unmount } = renderProductApp(PATH)
    await detailVisible()
    expect(screen.queryByRole('link', { name: /Ürün listesine dön/ })).toBeNull()
    unmount()
    cleanup()

    installFetch({ capabilities: [...DETAIL_ONLY, PRODUCT_CAPABILITY.productsList] })
    renderProductApp(PATH)
    await detailVisible()
    expect(screen.getByRole('link', { name: /Ürün listesine dön/ })).toBeTruthy()
  })
})
