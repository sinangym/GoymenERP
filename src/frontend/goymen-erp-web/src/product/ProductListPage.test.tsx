import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PRODUCT_CAPABILITY } from './capabilities'
import { resetProductListView } from './listViewState'
import {
  PRODUCT_1,
  PRODUCT_2,
  installFetch,
  json,
  productItem,
  productPage,
  renderProductApp,
} from './testHarness'

const LIST_ONLY = [PRODUCT_CAPABILITY.productsList]
const LIST_AND_DETAIL = [PRODUCT_CAPABILITY.productsList, PRODUCT_CAPABILITY.productsDetail]

beforeEach(() => {
  resetProductListView()
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

async function rowsVisible() {
  return waitFor(() => screen.getByRole('table'))
}

describe('mounting', () => {
  it('renders nothing but a neutral not-found block without the list capability', async () => {
    /**
     * ERP-000K §10.2 non-disclosure: a denied module and a page that does not exist
     * must be indistinguishable. No Product read may leave the client either.
     */
    const { calls } = installFetch({ capabilities: [] })
    renderProductApp('/urun')

    await waitFor(() => expect(screen.getByText('Bulunamadı — sayfa')).toBeTruthy())
    expect(calls.some((call) => call.url.includes('/products'))).toBe(false)
  })

  it('reads the list once the server grants the capability', async () => {
    installFetch({ capabilities: LIST_ONLY })
    renderProductApp('/urun')

    await rowsVisible()
    expect(screen.getByText('SAC-42')).toBeTruthy()
    expect(screen.getByText('Siyah sac')).toBeTruthy()
  })
})

describe('global master scope', () => {
  it('never attaches a company header to a Product read', async () => {
    /**
     * `PRD-SCOPE-001=A` + JIT §15: the Product master is global and "Frontend
     * Product'a CompanyId eklemez". A company header here would be the client
     * asserting a scope the locked decision removed.
     */
    const { calls } = installFetch({ capabilities: LIST_ONLY })
    renderProductApp('/urun')

    await rowsVisible()
    const productCalls = calls.filter((call) => call.url.includes('/products'))
    expect(productCalls.length).toBeGreaterThan(0)
    expect(productCalls.every((call) => call.companyId === null)).toBe(true)
    // The shell's own calls still carry it — this is a Product-request rule, not a
    // claim that the shell stopped scoping anything.
    expect(calls.some((call) => call.url.includes('/shell/capabilities') && call.companyId !== null)).toBe(true)
  })

  it('says on screen that the card is shared by both companies', async () => {
    installFetch({ capabilities: LIST_ONLY })
    renderProductApp('/urun')

    await rowsVisible()
    // The shell's company selector sits two rows above; without this the list reads
    // as "this company's products".
    expect(screen.getByText(/iki Göymen şirketi için ortaktır/)).toBeTruthy()
  })
})

describe('role rendering', () => {
  it('never prints an unevaluated role as a determined no', async () => {
    installFetch({ capabilities: LIST_ONLY })
    renderProductApp('/urun')

    await rowsVisible()
    const row = screen.getByRole('row', { name: /SAC-42/ })
    // purchasable + productionOutput are true; sellable is a determined NO and is
    // simply absent; productionInput is unevaluated and is SAID so.
    expect(within(row).getByText(/Alınabilir, Üretim çıktısı/)).toBeTruthy()
    expect(within(row).getByText(/Üretim girdisi — değerlendirilmedi/)).toBeTruthy()
  })

  it('says "no role" only when every role was determined', async () => {
    const determined = productItem({
      roles: {
        purchasable: { value: false, provenance: 'manual' },
        sellable: { value: false, provenance: 'manual' },
        productionInput: { value: false, provenance: 'derived' },
        productionOutput: { value: false, provenance: 'derived' },
      },
    })
    installFetch({
      capabilities: LIST_ONLY,
      onList: () => json(productPage({ items: [determined] })),
    })
    renderProductApp('/urun')

    await rowsVisible()
    expect(screen.getByText(/Ticari\/operasyonel rol yok/)).toBeTruthy()
  })
})

describe('completeness', () => {
  it('reports only the areas the server called incomplete', async () => {
    // `package` is `unknown`; §9 forbids fabricating a value for what the server
    // could not evaluate, and a warning badge is a claim that something is missing.
    installFetch({ capabilities: LIST_ONLY })
    renderProductApp('/urun')

    await rowsVisible()
    const row = screen.getByRole('row', { name: /SAC-42/ })
    expect(within(row).getByText('Eksik: Birim')).toBeTruthy()
  })
})

describe('alias match', () => {
  it('names the retired code that matched', async () => {
    installFetch({
      capabilities: LIST_ONLY,
      onList: () =>
        json(productPage({ items: [productItem({ match: { reason: 'alias', matchedAlias: 'ESKI-9' } })] })),
    })
    renderProductApp('/urun')

    await rowsVisible()
    expect(screen.getByText(/eski kod: ESKI-9/)).toBeTruthy()
  })
})

describe('detail entitlement', () => {
  it('links the code only when the detail capability is granted', async () => {
    installFetch({ capabilities: LIST_AND_DETAIL })
    renderProductApp('/urun')

    await rowsVisible()
    expect(screen.getByRole('link', { name: 'SAC-42' })).toBeTruthy()
  })

  it('renders the code as plain text when it is not', async () => {
    // Holding the list must not imply holding the 360 — the Cari slice shipped that
    // the other way once (#172) and a list-only snapshot could call the detail API.
    installFetch({ capabilities: LIST_ONLY })
    renderProductApp('/urun')

    await rowsVisible()
    expect(screen.queryByRole('link', { name: 'SAC-42' })).toBeNull()
    expect(screen.getByText('SAC-42')).toBeTruthy()
  })
})

describe('§13 states', () => {
  it('distinguishes an empty corpus from an empty filter result', async () => {
    installFetch({ capabilities: LIST_ONLY, onList: () => json(productPage({ items: [] })) })
    const { unmount } = renderProductApp('/urun')

    await waitFor(() => expect(screen.getByText('Kayıt yok — ürün')).toBeTruthy())
    unmount()
    cleanup()
    resetProductListView()

    installFetch({ capabilities: LIST_ONLY, onList: () => json(productPage({ items: [] })) })
    renderProductApp('/urun')
    await waitFor(() => screen.getByLabelText(/Kod, eski kod veya ad ara/))
    await userEvent.type(screen.getByLabelText(/Kod, eski kod veya ad ara/), 'yok-bu')
    await waitFor(() => expect(screen.getByText('Sonuç yok — ürün')).toBeTruthy())
  })

  it('a 403 closes the capability, not just the list, and discloses nothing', async () => {
    /**
     * Issue #165 SAB-004/005: a local "forbidden" block over a capability that
     * stayed usable meant a route return or a refetch could mount the feature again
     * on the authority the server had just rejected. The shared boundary re-fences
     * the capability instead, so the gated route itself closes onto the same
     * non-disclosing block an unknown page gets.
     */
    installFetch({
      capabilities: LIST_ONLY,
      onList: () => json({ code: 'forbidden' }, 403),
    })
    renderProductApp('/urun')

    await waitFor(() => expect(document.querySelector('[data-state]')).not.toBeNull())
    expect(screen.queryByRole('table')).toBeNull()
    expect(screen.queryByText('SAC-42')).toBeNull()
  })

  it('fails closed on a malformed page instead of rendering a partial list', async () => {
    // One STRUCTURALLY bad row rejects the whole page: a filtered view would
    // silently hide that something arrived we could not validate.
    installFetch({
      capabilities: LIST_ONLY,
      onList: () => json(productPage({ items: [productItem({ version: 7 })] })),
    })
    renderProductApp('/urun')

    await waitFor(() => expect(screen.getByText('Beklenmeyen yanıt — ürün')).toBeTruthy())
    expect(screen.queryByRole('table')).toBeNull()
  })

  it('shows an unrecognised status as unknown, naming it, without losing the list', async () => {
    /**
     * The contract-drift case, and it is live: #237 emits `"draft"` where this reads
     * `Draft`. The row stays, its status says it is not understood, and the raw
     * token is printed so the report that reaches the schema owner is actionable.
     * Rejecting the page would show an empty list and explain nothing.
     */
    installFetch({
      capabilities: LIST_ONLY,
      onList: () =>
        json(
          productPage({
            items: [productItem(), productItem({ id: PRODUCT_2, code: 'BORU-7', lifecycle: 'draft' })],
          }),
        ),
    })
    renderProductApp('/urun')

    await rowsVisible()
    expect(screen.getByText('SAC-42')).toBeTruthy()
    const odd = screen.getByRole('row', { name: /BORU-7/ })
    expect(within(odd).getByText('Bilinmeyen durum: draft')).toBeTruthy()
    // It is NOT quietly read as the state it resembles.
    expect(within(odd).queryByText('Taslak')).toBeNull()
  })

  it('offers a restart, not a generic conflict, on an invalid cursor', async () => {
    /**
     * Both arrive as a 4xx and the shell maps 409 to "the record changed, review the
     * difference" — false here, and it sends the user hunting for an edit that never
     * happened. Branch on the stable CODE.
     */
    installFetch({
      capabilities: LIST_ONLY,
      onList: (url) =>
        url.includes('cursor=') ? json({ code: 'invalid_cursor' }, 409) : json(productPage({ hasMore: true, nextCursor: 'c1' })),
    })
    renderProductApp('/urun')

    await rowsVisible()
    await userEvent.click(screen.getByRole('button', { name: 'Sonraki sayfa' }))
    await waitFor(() => expect(screen.getByText('Sayfalama bağlantısı geçersiz')).toBeTruthy())
    expect(screen.getByRole('button', { name: 'Listeyi baştan yükle' })).toBeTruthy()
    expect(screen.queryByText(/farkı gözden geçirin/)).toBeNull()
  })
})

describe('paging', () => {
  it('keeps the rows already on screen when the next page fails', async () => {
    installFetch({
      capabilities: LIST_ONLY,
      onList: (url) =>
        url.includes('cursor=')
          ? json({ code: 'internal_error' }, 500)
          : json(productPage({ hasMore: true, nextCursor: 'c1' })),
    })
    renderProductApp('/urun')

    await rowsVisible()
    await userEvent.click(screen.getByRole('button', { name: 'Sonraki sayfa' }))

    await waitFor(() => expect(screen.getByText(/Sonraki sayfa alınamadı/)).toBeTruthy())
    // §8.3: a next-page failure must not discard what the user already scrolled through.
    expect(screen.getByText('SAC-42')).toBeTruthy()
    expect(screen.getByRole('table')).toBeTruthy()
  })

  it('appends the next page to the rows already loaded', async () => {
    const second = productItem({ id: PRODUCT_2, code: 'BORU-7', name: 'Dikişli boru' })
    installFetch({
      capabilities: LIST_ONLY,
      onList: (url) =>
        url.includes('cursor=')
          ? json(productPage({ items: [second] }))
          : json(productPage({ hasMore: true, nextCursor: 'c1' })),
    })
    renderProductApp('/urun')

    await rowsVisible()
    await userEvent.click(screen.getByRole('button', { name: 'Sonraki sayfa' }))

    await waitFor(() => expect(screen.getByText('BORU-7')).toBeTruthy())
    expect(screen.getByText('SAC-42')).toBeTruthy()
  })

  it('drops the cursor and the accumulated rows when a filter changes', async () => {
    /**
     * §8.1: "Filter değişince cursor sıfırlanır." A cursor is bound to the query
     * that produced it, so carrying one across a filter change asks the server to
     * continue a chain that no longer describes what is being asked for.
     */
    const second = productItem({ id: PRODUCT_2, code: 'BORU-7', name: 'Dikişli boru' })
    const { calls } = installFetch({
      capabilities: LIST_ONLY,
      onList: (url) =>
        url.includes('cursor=')
          ? json(productPage({ items: [second] }))
          : json(productPage({ hasMore: true, nextCursor: 'c1' })),
    })
    renderProductApp('/urun')

    await rowsVisible()
    await userEvent.click(screen.getByRole('button', { name: 'Sonraki sayfa' }))
    await waitFor(() => expect(screen.getByText('BORU-7')).toBeTruthy())

    await userEvent.selectOptions(screen.getByLabelText('Yaşam döngüsü'), 'Draft')

    await waitFor(() => {
      const withLifecycle = calls.filter((call) => call.url.includes('lifecycle=Draft'))
      expect(withLifecycle.length).toBeGreaterThan(0)
      expect(withLifecycle.every((call) => !call.url.includes('cursor='))).toBe(true)
    })
    // The previous page's rows were read under the previous query and go with it.
    await waitFor(() => expect(screen.queryByText('BORU-7')).toBeNull())
  })
})

describe('count', () => {
  it('labels what the number counts rather than printing the raw scope token', async () => {
    installFetch({
      capabilities: LIST_ONLY,
      onList: (url) =>
        url.includes('includeCount=true')
          ? json(productPage({ totalCount: '128', countScope: 'filtered-authorized-visible-products' }))
          : json(productPage()),
    })
    renderProductApp('/urun')

    await rowsVisible()
    // The count is not requested by default, so the caption says what is shown.
    expect(screen.getByText(/1 kayıt görüntüleniyor/)).toBeTruthy()
  })
})

describe('filters', () => {
  it('names the active filters so an empty result has a readable cause', async () => {
    installFetch({ capabilities: LIST_ONLY })
    renderProductApp('/urun')

    await rowsVisible()
    await userEvent.selectOptions(screen.getByLabelText('Yaşam döngüsü'), 'Inactive')

    await waitFor(() =>
      expect(screen.getByTestId('product-active-filters').textContent).toContain('Durum: Pasif'),
    )
  })

  it('makes the any/all role semantics explicit', async () => {
    // §8.1: "Role filter'ında any/all semantiği UI'da açık olur." The two readings
    // return different products the moment two roles are ticked.
    installFetch({ capabilities: LIST_ONLY })
    renderProductApp('/urun')

    await rowsVisible()
    expect(screen.getByLabelText('Rol eşleşmesi')).toBeTruthy()
    await userEvent.click(screen.getByRole('checkbox', { name: 'Alınabilir' }))
    await userEvent.selectOptions(screen.getByLabelText('Rol eşleşmesi'), 'all')

    await waitFor(() =>
      expect(screen.getByTestId('product-active-filters').textContent).toContain('Rol (hepsi)'),
    )
  })

  it('clears filters but keeps the chosen sort', async () => {
    installFetch({ capabilities: LIST_ONLY })
    renderProductApp('/urun')

    await rowsVisible()
    await userEvent.selectOptions(screen.getByLabelText('Sıralama'), 'name')
    await userEvent.selectOptions(screen.getByLabelText('Yaşam döngüsü'), 'Draft')
    await userEvent.click(screen.getByRole('button', { name: 'Filtreleri temizle' }))

    await waitFor(() => expect(screen.queryByTestId('product-active-filters')).toBeNull())
    expect((screen.getByLabelText('Sıralama') as HTMLSelectElement).value).toBe('name')
  })
})

describe('view retention', () => {
  it('restores the search term after opening a record and coming back', async () => {
    installFetch({ capabilities: LIST_AND_DETAIL })
    renderProductApp('/urun')

    await rowsVisible()
    await userEvent.type(screen.getByLabelText(/Kod, eski kod veya ad ara/), 'sac')
    await waitFor(() =>
      expect(screen.getByTestId('product-active-filters').textContent).toContain('Arama: “sac”'),
    )

    await userEvent.click(screen.getByRole('link', { name: 'SAC-42' }))
    await waitFor(() => expect(screen.getByRole('heading', { name: 'Siyah sac' })).toBeTruthy())

    await userEvent.click(screen.getByRole('link', { name: /Ürün listesine dön/ }))
    await waitFor(() =>
      expect((screen.getByLabelText(/Kod, eski kod veya ad ara/) as HTMLInputElement).value).toBe('sac'),
    )
  })

  it('re-reads the data rather than restoring the rows it showed before', async () => {
    // The rows are deliberately NOT remembered: `CapabilityRoute` bumps the entry
    // generation so a revisit cannot paint rows read under a grant the server may
    // since have revoked (#146/#148).
    const { calls } = installFetch({ capabilities: LIST_AND_DETAIL })
    renderProductApp('/urun')

    await rowsVisible()
    const before = calls.filter((call) => call.url.includes('/products?') || call.url.endsWith('/products')).length

    await userEvent.click(screen.getByRole('link', { name: 'SAC-42' }))
    await waitFor(() => expect(screen.getByRole('heading', { name: 'Siyah sac' })).toBeTruthy())
    await userEvent.click(screen.getByRole('link', { name: /Ürün listesine dön/ }))
    await rowsVisible()

    const after = calls.filter((call) => call.url.includes('/products?') || call.url.endsWith('/products')).length
    expect(after).toBeGreaterThan(before)
  })

  it('moves focus back to the row the user opened', async () => {
    installFetch({ capabilities: LIST_AND_DETAIL })
    renderProductApp('/urun')

    await rowsVisible()
    await userEvent.click(screen.getByRole('link', { name: 'SAC-42' }))
    await waitFor(() => expect(screen.getByRole('heading', { name: 'Siyah sac' })).toBeTruthy())
    await userEvent.click(screen.getByRole('link', { name: /Ürün listesine dön/ }))

    await waitFor(() =>
      expect(document.activeElement?.id).toBe(`product-row-${PRODUCT_1}`),
    )
  })
})

describe('create entry point', () => {
  it('appears only with the create capability', async () => {
    installFetch({ capabilities: LIST_ONLY })
    const { unmount } = renderProductApp('/urun')
    await rowsVisible()
    expect(screen.queryByRole('link', { name: 'Yeni ürün' })).toBeNull()
    unmount()
    cleanup()
    resetProductListView()

    installFetch({ capabilities: [...LIST_ONLY, PRODUCT_CAPABILITY.productsCreate] })
    renderProductApp('/urun')
    await rowsVisible()
    expect(screen.getByRole('link', { name: 'Yeni ürün' })).toBeTruthy()
  })
})
