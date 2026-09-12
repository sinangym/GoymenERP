import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import { PRODUCT_CAPABILITY } from './capabilities'
import { resetPendingAttempts } from './pendingAttempt'
import {
  CLASS_FLAT,
  FAMILY_FLAT,
  PRODUCT_1,
  installFetch,
  json,
  productCard,
  productCore,
  references,
  renderProductApp,
} from './testHarness'

const DETAIL_ONLY = [PRODUCT_CAPABILITY.productsDetail]
const PATH = `/urun/${PRODUCT_1}`

afterEach(() => {
  cleanup()
  // The pending-attempt store outlives a component by design, so a case that ends
  // with an unresolved write would otherwise seed the next one.
  resetPendingAttempts()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const cardVisible = () => waitFor(() => screen.getByRole('heading', { name: 'Siyah sac' }))

describe('mounting', () => {
  it('mounts nothing and reads nothing without the detail capability', async () => {
    const { calls } = installFetch({ capabilities: [PRODUCT_CAPABILITY.productsList] })
    renderProductApp(PATH)

    await waitFor(() => expect(screen.getByText('Bulunamadı — sayfa')).toBeTruthy())
    expect(calls.some((call) => call.url.includes('/products/'))).toBe(false)
  })

  it('reads the card with the detail capability, and sends no company header', async () => {
    const { calls } = installFetch({ capabilities: DETAIL_ONLY })
    renderProductApp(PATH)

    await cardVisible()
    expect(screen.getByText('SAC-42')).toBeTruthy()
    const cardCalls = calls.filter((call) => call.url.includes('/products/'))
    expect(cardCalls.length).toBeGreaterThan(0)
    expect(cardCalls.every((call) => call.companyId === null)).toBe(true)
  })
})

describe('lifecycle', () => {
  it('prints what the state MEANS, not only its name', async () => {
    installFetch({ capabilities: DETAIL_ONLY })
    renderProductApp(PATH)

    await cardVisible()
    expect(screen.getByText('Taslak')).toBeTruthy()
    // "Taslak" alone does not tell an operator the product cannot be picked in an
    // order line, which is the whole point of the state.
    expect(screen.getByText('Kesin işlemlerde seçilemez.')).toBeTruthy()
  })

  it('banners an inactive product as closed in BOTH companies', async () => {
    installFetch({
      capabilities: DETAIL_ONLY,
      onCard: () => json(productCard({ product: productCore({ lifecycle: 'Inactive' }) })),
    })
    renderProductApp(PATH)

    await cardVisible()
    expect(screen.getByText(/her iki şirkette de yeni kullanıma kapalıdır/)).toBeTruthy()
  })

  it('claims nothing about a status it does not understand', async () => {
    installFetch({
      capabilities: DETAIL_ONLY,
      onCard: () => json(productCard({ product: productCore({ lifecycle: 'archived' }) })),
    })
    renderProductApp(PATH)

    await cardVisible()
    expect(screen.getByText('Bilinmeyen durum: archived')).toBeTruthy()
    expect(screen.getByText(/kullanılabilirliği hakkında bir sonuç çıkarmayın/)).toBeTruthy()
    // The inactive banner is a claim about a KNOWN state and must not be inherited.
    expect(screen.queryByText(/her iki şirkette de yeni kullanıma kapalıdır/)).toBeNull()
  })
})

describe('roles', () => {
  it('shows an unevaluated role as unevaluated, never as "Hayır"', async () => {
    installFetch({ capabilities: DETAIL_ONLY })
    renderProductApp(PATH)

    await cardVisible()
    expect(screen.getByTestId('product-role-productionInput').textContent).toBe('—')
    expect(screen.getByText('değerlendirilmedi')).toBeTruthy()
    // ...while a determined NO still reads as a determined NO.
    expect(screen.getByTestId('product-role-sellable').textContent).toBe('Hayır')
  })

  it('distinguishes a derived role from one a person typed', async () => {
    installFetch({ capabilities: DETAIL_ONLY })
    renderProductApp(PATH)

    await cardVisible()
    const row = screen.getByRole('row', { name: /Üretim çıktısı/ })
    // Only one of the two is safe to overwrite by hand.
    expect(within(row).getByText('ilişkiden türetildi')).toBeTruthy()
  })
})

describe('completeness', () => {
  it('lists the server’s missing fields rather than inventing a score', async () => {
    installFetch({ capabilities: DETAIL_ONLY })
    renderProductApp(PATH)

    await cardVisible()
    expect(screen.getByTestId('product-completeness').textContent).toBe('Eksik')
    expect(within(screen.getByTestId('product-missing-fields')).getByText(
      'references.units.stock',
    )).toBeTruthy()
  })

  it('never renders NotEvaluated as complete', async () => {
    // README states this rule explicitly.
    installFetch({
      capabilities: DETAIL_ONLY,
      onCard: () =>
        json(
          productCard({
            product: productCore({
              completeness: { evaluation: 'NotEvaluated', missingFields: [] },
            }),
          }),
        ),
    })
    renderProductApp(PATH)

    await cardVisible()
    expect(screen.getByTestId('product-completeness').textContent).toBe('Değerlendirilmedi')
    expect(screen.getByText(/Sunucu eksik alan bildirmedi/)).toBeTruthy()
  })

  it('names an unknown evaluation token instead of coercing it', async () => {
    installFetch({
      capabilities: DETAIL_ONLY,
      onCard: () =>
        json(
          productCard({
            product: productCore({ completeness: { evaluation: 'Partial', missingFields: [] } }),
          }),
        ),
    })
    renderProductApp(PATH)

    await cardVisible()
    expect(screen.getByTestId('product-completeness').textContent).toContain('Partial')
  })
})

describe('references', () => {
  it('shows an unassigned unit as unassigned, never as the stock unit', async () => {
    installFetch({
      capabilities: DETAIL_ONLY,
      onCard: () =>
        json(
          productCard({
            references: references({
              units: {
                stock: FAMILY_FLAT,
                purchase: null,
                sales: null,
                price: null,
                production: null,
                package: null,
              },
            }),
          }),
        ),
    })
    renderProductApp(PATH)

    await cardVisible()
    // DEC-PRD-006 keeps the six roles separate; a fallback would be the screen
    // inventing a conversion.
    expect(screen.getByTestId('product-unit-stock').textContent).toBe(FAMILY_FLAT)
    expect(screen.getByTestId('product-unit-production').textContent).toBe('atanmadı')
  })

  it('shows the SERVER-resolved labels, never a client lookup', async () => {
    installFetch({ capabilities: DETAIL_ONLY })
    renderProductApp(PATH)

    await cardVisible()
    expect(screen.getByTestId('product-group').textContent).toBe('Sac')
    expect(screen.getByTestId('product-primary-stock-unit').textContent).toBe('kg')
    // Nothing assigned reads as "atanmadı".
    expect(screen.getByTestId('product-class').textContent).toBe('atanmadı')
  })

  it('separates "nothing assigned" from "label could not be read"', async () => {
    /**
     * README makes a null label with a present id a DIFFERENT fact from a null id:
     * one says nobody chose, the other says the catalogue could not be resolved.
     * Printing one dash for both would hide a broken catalogue behind an empty field.
     */
    installFetch({
      capabilities: DETAIL_ONLY,
      onCard: () =>
        json(
          productCard({
            product: productCore({
              classId: CLASS_FLAT,
              labels: { group: 'Sac', class: null, family: null, primaryStockUnit: 'kg' },
            }),
          }),
        ),
    })
    renderProductApp(PATH)

    await cardVisible()
    expect(screen.getByTestId('product-class').textContent).toBe('ad okunamadı')
    expect(screen.getByTestId('product-family').textContent).toBe('atanmadı')
  })

  it('says which editors are still out of scope', async () => {
    installFetch({ capabilities: DETAIL_ONLY })
    renderProductApp(PATH)

    await cardVisible()
    expect(screen.getByText(/sonraki dilimde aile bazlı tamlık/)).toBeTruthy()
  })
})

describe('master vs label boundary', () => {
  it('says the card holds only the tracking POLICY reference', async () => {
    installFetch({ capabilities: DETAIL_ONLY })
    renderProductApp(PATH)

    await cardVisible()
    // DEC-PRD-003: lot, origin and quality/defect status belong to the physical
    // label record. Without this an empty policy reads as "no lots".
    expect(
      screen.getByText(/Parti, menşe ve kalite\/kusur durumu fiziksel etiket kaydında/),
    ).toBeTruthy()
  })

  it('shows no stock, price or lot FIGURE anywhere on the card', async () => {
    installFetch({ capabilities: DETAIL_ONLY })
    const { container } = renderProductApp(PATH)

    await cardVisible()
    const text = container.textContent ?? ''
    for (const forbidden of ['Stok miktarı', 'Bakiye', 'Parti no', 'Kalite durumu', '₺']) {
      expect(text).not.toContain(forbidden)
    }
    /**
     * "Fiyat birimi" IS present and allowed: the price UNIT is a master reference
     * (which unit a price is quoted in), while the amount is a company-scoped child
     * record. The card shows an id or "atanmadı", never a figure.
     */
    expect(screen.getByTestId('product-unit-price').textContent).toBe('atanmadı')
  })
})

describe('§13 states', () => {
  it('shows the non-disclosing not-found block for an unknown product', async () => {
    installFetch({ capabilities: DETAIL_ONLY, onCard: () => json({ code: 'not_found' }, 404) })
    renderProductApp(PATH)

    await waitFor(() => expect(screen.getByText('Bulunamadı — ürün')).toBeTruthy())
  })

  it('fails closed when the card describes a different product', async () => {
    // A well-formed 200 about another record would have the user looking at one
    // record believing it is another.
    installFetch({
      capabilities: DETAIL_ONLY,
      onCard: () =>
        json(productCard({ product: productCore({ id: '99999999-9999-9999-9999-999999999999' }) })),
    })
    renderProductApp(PATH)

    await waitFor(() => expect(screen.getByText('Beklenmeyen yanıt — ürün')).toBeTruthy())
  })

  it('offers a retry on a service failure', async () => {
    installFetch({
      capabilities: DETAIL_ONLY,
      onCard: () => json({ code: 'service_unavailable' }, 503),
    })
    renderProductApp(PATH)

    await waitFor(() => expect(screen.getByRole('button', { name: 'Tekrar dene' })).toBeTruthy())
  })
})

describe('edit entry point', () => {
  it('appears only with the edit capability AND only on a Draft', async () => {
    installFetch({ capabilities: DETAIL_ONLY })
    const { unmount } = renderProductApp(PATH)
    await cardVisible()
    expect(screen.queryByRole('link', { name: 'Düzenle' })).toBeNull()
    unmount()
    cleanup()

    installFetch({ capabilities: [...DETAIL_ONLY, PRODUCT_CAPABILITY.productsEdit] })
    const second = renderProductApp(PATH)
    await cardVisible()
    expect(screen.getByRole('link', { name: 'Düzenle' })).toBeTruthy()
    second.unmount()
    cleanup()

    // The domain refuses basic edit on a non-Draft (`lifecycle_conflict`), so
    // offering the control there would be offering a refusal.
    installFetch({
      capabilities: [...DETAIL_ONLY, PRODUCT_CAPABILITY.productsEdit],
      onCard: () => json(productCard({ product: productCore({ lifecycle: 'Active' }) })),
    })
    renderProductApp(PATH)
    await cardVisible()
    expect(screen.queryByRole('link', { name: 'Düzenle' })).toBeNull()
  })
})
