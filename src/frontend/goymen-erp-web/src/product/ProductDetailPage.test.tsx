import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PRODUCT_ACTIVATE_CAPABILITY, PRODUCT_CAPABILITY } from './capabilities'
import { resetPendingAttempts } from './pendingAttempt'
import {
  COMPANY_A,
  CLASS_FLAT,
  FAMILY_FLAT,
  PRODUCT_1,
  installFetch,
  json,
  mutation,
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

  it('reads the card with the detail capability, under the committed company as the validated origin', async () => {
    // §15: the origin header is the server-validated source of the read — not the
    // Product's owner. It is sent on the card read exactly as on the list read.
    const { calls } = installFetch({ capabilities: DETAIL_ONLY })
    renderProductApp(PATH)

    await cardVisible()
    expect(screen.getByText('SAC-42')).toBeTruthy()
    const cardCalls = calls.filter((call) => call.url.includes('/products/'))
    expect(cardCalls.length).toBeGreaterThan(0)
    expect(cardCalls.every((call) => call.companyId === COMPANY_A)).toBe(true)
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
    expect(screen.getByTestId('product-unit-stock').textContent).toBe('ad okunamadı')
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
    expect(screen.getByText(/Takip politikası henüz düzenlenemez/)).toBeTruthy()
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

describe('activation control', () => {
  const WITH_ACTIVATION = [...DETAIL_ONLY, PRODUCT_ACTIVATE_CAPABILITY]

  it('appears only with its child capability and only for a known Draft', async () => {
    installFetch({ capabilities: DETAIL_ONLY })
    const first = renderProductApp(PATH)
    await cardVisible()
    expect(screen.queryByRole('button', { name: 'Ürünü aktifleştir' })).toBeNull()
    first.unmount()
    cleanup()

    installFetch({ capabilities: WITH_ACTIVATION })
    const second = renderProductApp(PATH)
    await cardVisible()
    expect(screen.getByRole('button', { name: 'Ürünü aktifleştir' })).toBeTruthy()
    second.unmount()
    cleanup()

    installFetch({
      capabilities: WITH_ACTIVATION,
      onCard: () => json(productCard({ product: productCore({ lifecycle: 'Active' }) })),
    })
    renderProductApp(PATH)
    await cardVisible()
    expect(screen.queryByRole('button', { name: 'Ürünü aktifleştir' })).toBeNull()
  })

  it('posts the exact card version with one key and shows Active only from the re-read card', async () => {
    let historyReads = 0
    let cardReads = 0
    const { calls } = installFetch({
      capabilities: WITH_ACTIVATION,
      onCard: () => {
        cardReads += 1
        return json(
          productCard({
            product: productCore(cardReads === 1 ? {} : { lifecycle: 'Active', version: '8' }),
          }),
        )
      },
      onHistory: () => {
        historyReads += 1
        return json({ productId: PRODUCT_1, entries: [], truncated: false })
      },
      onActivate: () =>
        json(
          mutation({
            card: productCard({ product: productCore({ lifecycle: 'Active', version: '8' }) }),
          }),
        ),
    })
    renderProductApp(PATH)
    await cardVisible()

    await userEvent.click(screen.getByRole('button', { name: 'Ürünü aktifleştir' }))
    await waitFor(() => expect(screen.getByText('Ürün aktifleştirildi.')).toBeTruthy())
    await waitFor(() => expect(screen.getByText('Aktif')).toBeTruthy())
    expect(cardReads).toBe(2)
    await waitFor(() => expect(historyReads).toBeGreaterThanOrEqual(2))

    const request = calls.find((call) => call.url.endsWith(`/products/${PRODUCT_1}/activation`))
    expect(request?.method).toBe('POST')
    expect(request?.companyId).toBe(COMPANY_A)
    expect(request?.idempotencyKey).toMatch(/^[0-9a-f]{32}$/)
    expect(JSON.parse(request?.body ?? '{}')).toEqual({ expectedVersion: '7' })
  })

  it('retries an unknown outcome with the same payload, origin and idempotency key', async () => {
    const { calls } = installFetch({
      capabilities: WITH_ACTIVATION,
      onActivate: (attempt) => {
        if (attempt === 1) throw new TypeError('connection dropped')
        return json(
          mutation({
            card: productCard({ product: productCore({ lifecycle: 'Active', version: '8' }) }),
            isReplay: true,
          }),
        )
      },
    })
    renderProductApp(PATH)
    await cardVisible()

    await userEvent.click(screen.getByRole('button', { name: 'Ürünü aktifleştir' }))
    await waitFor(() => expect(screen.getByTestId('product-activation-unknown')).toBeTruthy())
    await userEvent.click(screen.getByRole('button', { name: 'Aynı aktivasyonu tekrar dene' }))
    await waitFor(() => expect(screen.getByText(/ikinci bir değişiklik uygulanmadı/)).toBeTruthy())

    const requests = calls.filter((call) => call.url.endsWith(`/products/${PRODUCT_1}/activation`))
    expect(requests).toHaveLength(2)
    expect(requests[1].idempotencyKey).toBe(requests[0].idempotencyKey)
    expect(requests[1].companyId).toBe(requests[0].companyId)
    expect(requests[1].body).toBe(requests[0].body)
  })

  it('stops on a stale version and requires a fresh card instead of rebasing', async () => {
    const { calls } = installFetch({
      capabilities: WITH_ACTIVATION,
      onActivate: () => json({ code: 'version_conflict', currentVersion: '8' }, 409),
    })
    renderProductApp(PATH)
    await cardVisible()

    await userEvent.click(screen.getByRole('button', { name: 'Ürünü aktifleştir' }))
    await waitFor(() => expect(screen.getByText(/Ürün siz bakarken değişti/)).toBeTruthy())
    expect(screen.queryByText('Ürün aktifleştirildi.')).toBeNull()
    expect(calls.filter((call) => call.url.endsWith(`/products/${PRODUCT_1}/activation`))).toHaveLength(1)
  })

  it('re-reads the card once after a confirmed activation and never paints the command card’s partial labels', async () => {
    /**
     * The real answers differ, and that difference is the point: the card read resolves
     * every catalogue (class, family, template, facts, units), while the activation
     * command's card carries the group label only (ProductDraftOperation.Card). A harness
     * that answered both with the same labels could not tell a re-read from a paint.
     */
    const UNIT_KG = 'dddddddd-0000-0000-0000-000000000001'
    const TEMPLATE = 'eeeeeeee-0000-0000-0000-000000000009'
    const FULL_LABELS = {
      group: 'Sac',
      class: 'Yassı ürün',
      family: 'Soğuk hadde',
      primaryStockUnit: 'Kilogram',
      template: 'Levha şablonu',
      familyFacts: { cins: 'Çelik levha', tur: 'Soğuk haddelenmiş', tip: 'Rulo form' },
      units: { stock: 'Kilogram', sales: 'Kilogram' },
    }
    const cardWith = (lifecycle: string, version: string, labels: Record<string, unknown>) =>
      productCard({
        product: productCore({ classId: CLASS_FLAT, familyId: FAMILY_FLAT, lifecycle, version, labels }),
        references: references({
          templateId: TEMPLATE,
          templateVersion: '1',
          units: { stock: UNIT_KG, purchase: null, sales: UNIT_KG, price: null, production: null, package: null },
        }),
      })
    const expectFullLabels = () => {
      expect(screen.getByTestId('product-class').textContent).toBe('Yassı ürün')
      expect(screen.getByTestId('product-family').textContent).toBe('Soğuk hadde')
      expect(screen.getByTestId('product-primary-stock-unit').textContent).toBe('Kilogram')
      expect(screen.getByTestId('product-unit-stock').textContent).toBe('Kilogram')
      expect(screen.getByTestId('product-unit-sales').textContent).toBe('Kilogram')
      for (const text of ['Levha şablonu', 'Çelik levha', 'Soğuk haddelenmiş', 'Rulo form']) {
        expect(screen.getByText(text)).toBeTruthy()
      }
      expect(screen.queryAllByText('ad okunamadı')).toHaveLength(0)
    }

    let cardReads = 0
    let answerReRead: (response: Response) => void = () => undefined
    const reRead = new Promise<Response>((resolve) => {
      answerReRead = resolve
    })
    installFetch({
      capabilities: WITH_ACTIVATION,
      onCard: () => {
        cardReads += 1
        return cardReads === 1 ? json(cardWith('Draft', '7', FULL_LABELS)) : reRead
      },
      onActivate: () =>
        json(
          mutation({
            card: cardWith('Active', '8', { group: 'Sac', class: null, family: null, primaryStockUnit: null }),
          }),
        ),
    })
    renderProductApp(PATH)
    await cardVisible()
    expectFullLabels()

    await userEvent.click(screen.getByRole('button', { name: 'Ürünü aktifleştir' }))
    await waitFor(() => expect(screen.getByText('Ürün aktifleştirildi.')).toBeTruthy())
    await waitFor(() => expect(cardReads).toBe(2))
    // The confirmed answer is in, the re-read is not: the last server card stands, whole.
    // No optimistic Active, and nothing from the command's partial card.
    expect(screen.getByText('Taslak')).toBeTruthy()
    expect(screen.queryByText('Aktif')).toBeNull()
    expectFullLabels()

    answerReRead(json(cardWith('Active', '8', FULL_LABELS)))
    await waitFor(() => expect(screen.getByText('Aktif')).toBeTruthy())
    expect(screen.getByText('Ürün aktifleştirildi.')).toBeTruthy()
    expectFullLabels()
    expect(cardReads).toBe(2)
  })

  it('after a stale refusal, "Kartı yenile" re-reads the card and offers activation again on the fresh version with a new key', async () => {
    let cardReads = 0
    const { calls } = installFetch({
      capabilities: WITH_ACTIVATION,
      onCard: () => {
        cardReads += 1
        return json(productCard({ product: productCore({ version: cardReads === 1 ? '7' : '8' }) }))
      },
      onActivate: (attempt) =>
        attempt === 1
          ? json({ code: 'version_conflict', expectedVersion: '7', currentVersion: '8' }, 409)
          : json(mutation({ card: productCard({ product: productCore({ lifecycle: 'Active', version: '9' }) }) })),
    })
    const activations = () => calls.filter((call) => call.url.endsWith(`/products/${PRODUCT_1}/activation`))
    renderProductApp(PATH)
    await cardVisible()

    await userEvent.click(screen.getByRole('button', { name: 'Ürünü aktifleştir' }))
    await waitFor(() => expect(screen.getByText(/Ürün siz bakarken değişti/)).toBeTruthy())
    expect(screen.queryByRole('button', { name: 'Ürünü aktifleştir' })).toBeNull()

    await userEvent.click(screen.getByRole('button', { name: 'Kartı yenile' }))
    await waitFor(() => expect(screen.getByRole('button', { name: 'Ürünü aktifleştir' })).toBeTruthy())
    expect(cardReads).toBe(2)
    expect(screen.queryByText(/Ürün siz bakarken değişti/)).toBeNull()
    // The fresh card is offered, not acted on: nothing was re-sent or rebased on its own.
    expect(activations()).toHaveLength(1)

    await userEvent.click(screen.getByRole('button', { name: 'Ürünü aktifleştir' }))
    await waitFor(() => expect(screen.getByText('Ürün aktifleştirildi.')).toBeTruthy())
    const [refused, fresh] = activations()
    expect(activations()).toHaveLength(2)
    expect(JSON.parse(refused.body ?? '{}')).toEqual({ expectedVersion: '7' })
    expect(JSON.parse(fresh.body ?? '{}')).toEqual({ expectedVersion: '8' })
    expect(fresh.idempotencyKey).toMatch(/^[0-9a-f]{32}$/)
    expect(fresh.idempotencyKey).not.toBe(refused.idempotencyKey)
  })

  it('re-fences the control after a forbidden activation response', async () => {
    const capabilities = [...WITH_ACTIVATION]
    installFetch({
      capabilities,
      onActivate: () => {
        capabilities.splice(capabilities.indexOf(PRODUCT_ACTIVATE_CAPABILITY), 1)
        return json({ code: 'forbidden' }, 403)
      },
    })
    renderProductApp(PATH)
    await cardVisible()

    await userEvent.click(screen.getByRole('button', { name: 'Ürünü aktifleştir' }))
    await waitFor(() =>
      expect(screen.queryByRole('button', { name: 'Ürünü aktifleştir' })).toBeNull(),
    )
    expect(screen.queryByText('Ürün aktifleştirildi.')).toBeNull()
  })
})
