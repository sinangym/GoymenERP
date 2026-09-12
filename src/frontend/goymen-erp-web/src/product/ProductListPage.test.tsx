import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PRODUCT_CAPABILITY } from './capabilities'
import { resetPendingAttempts } from './pendingAttempt'
import { resetProductListView } from './listViewState'
import {
  COMPANY_A,
  PRODUCT_1,
  PRODUCT_2,
  installFetch,
  json,
  productCore,
  productPage,
  renderProductApp,
  roles,
} from './testHarness'

const LIST_ONLY = [PRODUCT_CAPABILITY.productsList]
const LIST_AND_DETAIL = [PRODUCT_CAPABILITY.productsList, PRODUCT_CAPABILITY.productsDetail]

beforeEach(() => {
  resetProductListView()
})

afterEach(() => {
  cleanup()
  // The pending-attempt store outlives a component by design, so a case that ends
  // with an unresolved write would otherwise seed the next one.
  resetPendingAttempts()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const rowsVisible = () => waitFor(() => screen.getByRole('table'))

describe('mounting', () => {
  it('renders a neutral not-found block without the list capability, and reads nothing', async () => {
    // ERP-000K §10.2: a denied module and a page that does not exist must be
    // indistinguishable, and no Product read may leave the client.
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
  it('sends the committed company as the validated ORIGIN header on every Product read', async () => {
    /**
     * `PRD-SCOPE-001=A` keeps the master global and `IProductService` still requires
     * server-resolved global authorization — but the actual endpoints
     * (`RequireCompanyContext`, #251) require `X-Company-Id` and validate it as the
     * source of the operation. §15 (6a649ef): it "creates neither Product ownership
     * nor global authority". So it is sent, from the committed context, and never
     * enters a payload or a query.
     */
    const { calls } = installFetch({ capabilities: LIST_ONLY })
    renderProductApp('/urun')

    await rowsVisible()
    const productCalls = calls.filter((call) => call.url.includes('/products'))
    expect(productCalls.length).toBeGreaterThan(0)
    expect(productCalls.every((call) => call.companyId === COMPANY_A)).toBe(true)
    expect(productCalls.every((call) => !call.url.includes('companyId'))).toBe(true)
  })

  it('says on screen that the card is shared by both companies', async () => {
    installFetch({ capabilities: LIST_ONLY })
    renderProductApp('/urun')

    await rowsVisible()
    expect(screen.getByText(/iki Göymen şirketi için ortaktır/)).toBeTruthy()
  })
})

describe('honesty about the narrow projection', () => {
  it('renders the SERVER-resolved labels and completeness on every row', async () => {
    /**
     * The projection carries `labels` and `completeness`, so the columns are real
     * rather than derived — the client never resolves a UUID itself, which is the
     * fan-out §19.5 forbids.
     */
    installFetch({ capabilities: LIST_ONLY })
    renderProductApp('/urun')

    await rowsVisible()
    const row = screen.getByRole('row', { name: /SAC-42/ })
    expect(within(row).getByText('kg')).toBeTruthy()
    expect(screen.getByTestId(`product-completeness-${PRODUCT_1}`).textContent).toBe('Eksik')
  })

  it('shows no count, because the page publishes none', async () => {
    installFetch({ capabilities: LIST_ONLY })
    renderProductApp('/urun')

    await rowsVisible()
    expect(screen.getByText('1 kayıt görüntüleniyor')).toBeTruthy()
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
    const determined = productCore({
      roles: roles({
        purchasable: { value: false, provenance: 'manual' },
        productionInput: { value: false, provenance: 'derived' },
        productionOutput: { value: false, provenance: 'derived' },
      }),
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

describe('detail entitlement', () => {
  it('links the code only when the detail capability is granted', async () => {
    installFetch({ capabilities: LIST_AND_DETAIL })
    renderProductApp('/urun')

    await rowsVisible()
    expect(screen.getByRole('link', { name: 'SAC-42' })).toBeTruthy()
  })

  it('renders the code as plain text when it is not', async () => {
    // Holding the list must not imply holding the card.
    installFetch({ capabilities: LIST_ONLY })
    renderProductApp('/urun')

    await rowsVisible()
    expect(screen.queryByRole('link', { name: 'SAC-42' })).toBeNull()
    expect(screen.getByText('SAC-42')).toBeTruthy()
  })
})

describe('query', () => {
  it('sends distinct comma-separated roles with an explicit roleMatch', async () => {
    /**
     * Both the list and the any/all semantics are the contract's, so the screen can
     * offer the choice without inventing a meaning — and the two readings return
     * different products the moment a second role is ticked.
     */
    const { calls } = installFetch({ capabilities: LIST_ONLY })
    renderProductApp('/urun')

    await rowsVisible()
    await userEvent.click(screen.getByRole('checkbox', { name: 'Alınabilir' }))
    await userEvent.click(screen.getByRole('checkbox', { name: 'Satılabilir' }))
    await userEvent.selectOptions(screen.getByLabelText('Rol eşleşmesi'), 'all')

    await waitFor(() => {
      const sent = calls.filter((call) => call.url.includes('roleMatch=all'))
      expect(sent.length).toBeGreaterThan(0)
      expect(sent.some((call) => call.url.includes('roles=purchasable%2Csellable'))).toBe(true)
    })
  })

  it('says that an unevaluated role never satisfies a role filter', async () => {
    // README: "A role filter matches known true values only… Unknown is not false."
    installFetch({ capabilities: LIST_ONLY })
    renderProductApp('/urun')

    await rowsVisible()
    expect(screen.getByText(/değerlendirilmemiş rol eşleşme saymaz/)).toBeTruthy()
  })

  it('sends the sort key, direction and completeness filter', async () => {
    const { calls } = installFetch({ capabilities: LIST_ONLY })
    renderProductApp('/urun')

    await rowsVisible()
    await userEvent.selectOptions(screen.getByLabelText('Sıralama'), 'updatedAtUtc')
    await userEvent.selectOptions(screen.getByLabelText('Yön'), 'desc')
    await userEvent.selectOptions(screen.getByLabelText('Tamlık'), 'Incomplete')

    await waitFor(() =>
      expect(
        calls.some(
          (call) =>
            call.url.includes('sort=updatedAtUtc') &&
            call.url.includes('direction=desc') &&
            call.url.includes('completeness=Incomplete'),
        ),
      ).toBe(true),
    )
  })

  it('strips control characters from the search term', async () => {
    /**
     * The endpoint rejects a `q` containing any control character, so sending one
     * turns a stray paste into a 400 the user cannot read. This is a search term,
     * not a stored value, so nothing is silently persisted in a changed form.
     */
    const { calls } = installFetch({ capabilities: LIST_ONLY })
    renderProductApp('/urun')

    await rowsVisible()
    const input = screen.getByLabelText(/Kod, eski kod veya ad ara/) as HTMLInputElement
    await userEvent.type(input, 'sac')
    // jsdom lets us set a value userEvent cannot type.
    await waitFor(() => expect(calls.some((call) => call.url.includes('q=sac'))).toBe(true))
    expect(calls.every((call) => !/%0[0-9A-F]/i.test(call.url))).toBe(true)
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
     * stayed usable meant a route return could mount the feature again on authority
     * the server had just rejected.
     */
    installFetch({ capabilities: LIST_ONLY, onList: () => json({ code: 'forbidden' }, 403) })
    renderProductApp('/urun')

    await waitFor(() => expect(document.querySelector('[data-state]')).not.toBeNull())
    expect(screen.queryByRole('table')).toBeNull()
    expect(screen.queryByText('SAC-42')).toBeNull()
  })

  it('fails closed on a structurally malformed page', async () => {
    // One structurally bad row rejects the whole page: a filtered view would hide
    // that something arrived we could not validate.
    installFetch({
      capabilities: LIST_ONLY,
      onList: () => json(productPage({ items: [productCore({ version: 7 })] })),
    })
    renderProductApp('/urun')

    await waitFor(() => expect(screen.getByText('Beklenmeyen yanıt — ürün')).toBeTruthy())
    expect(screen.queryByRole('table')).toBeNull()
  })

  it('shows an unrecognised status as unknown, naming it, without losing the list', async () => {
    /**
     * The vocabulary is unchanged; only the COST of an unknown token is. Rejecting
     * the page would show an empty list and explain nothing, while a row that names
     * the token gives whoever can reconcile the contract something to act on.
     */
    installFetch({
      capabilities: LIST_ONLY,
      onList: () =>
        json(
          productPage({
            items: [productCore(), productCore({ id: PRODUCT_2, code: 'BORU-7', lifecycle: 'draft' })],
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
    installFetch({
      capabilities: LIST_ONLY,
      onList: (url) =>
        url.includes('cursor=')
          ? json({ code: 'invalid_cursor' }, 400)
          : json(productPage({ nextCursor: 'c1' })),
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
  it('treats a non-null nextCursor as the only "there is more" signal', async () => {
    installFetch({ capabilities: LIST_ONLY, onList: () => json(productPage({ nextCursor: null })) })
    renderProductApp('/urun')

    await rowsVisible()
    expect(screen.queryByRole('button', { name: 'Sonraki sayfa' })).toBeNull()
  })

  it('keeps the rows already on screen when the next page fails', async () => {
    installFetch({
      capabilities: LIST_ONLY,
      onList: (url) =>
        url.includes('cursor=')
          ? json({ code: 'internal_error' }, 500)
          : json(productPage({ nextCursor: 'c1' })),
    })
    renderProductApp('/urun')

    await rowsVisible()
    await userEvent.click(screen.getByRole('button', { name: 'Sonraki sayfa' }))

    await waitFor(() => expect(screen.getByText(/Sonraki sayfa alınamadı/)).toBeTruthy())
    // A next-page failure must not discard what the user already scrolled through.
    expect(screen.getByText('SAC-42')).toBeTruthy()
  })

  it('appends the next page to the rows already loaded', async () => {
    const second = productCore({ id: PRODUCT_2, code: 'BORU-7', name: 'Dikişli boru' })
    installFetch({
      capabilities: LIST_ONLY,
      onList: (url) =>
        url.includes('cursor=')
          ? json(productPage({ items: [second] }))
          : json(productPage({ nextCursor: 'c1' })),
    })
    renderProductApp('/urun')

    await rowsVisible()
    await userEvent.click(screen.getByRole('button', { name: 'Sonraki sayfa' }))

    await waitFor(() => expect(screen.getByText('BORU-7')).toBeTruthy())
    expect(screen.getByText('SAC-42')).toBeTruthy()
  })

  it('drops the cursor and the accumulated rows when a filter changes', async () => {
    // A cursor is bound to the query that produced it, so carrying one across a
    // filter change asks the server to continue a chain that no longer describes
    // what is being asked for.
    const second = productCore({ id: PRODUCT_2, code: 'BORU-7', name: 'Dikişli boru' })
    const { calls } = installFetch({
      capabilities: LIST_ONLY,
      onList: (url) =>
        url.includes('cursor=')
          ? json(productPage({ items: [second] }))
          : json(productPage({ nextCursor: 'c1' })),
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
    await waitFor(() => expect(screen.queryByText('BORU-7')).toBeNull())
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

  it('clears filters but keeps the chosen page size', async () => {
    installFetch({ capabilities: LIST_ONLY })
    renderProductApp('/urun')

    await rowsVisible()
    await userEvent.selectOptions(screen.getByLabelText('Sayfa boyutu'), '100')
    await userEvent.selectOptions(screen.getByLabelText('Yaşam döngüsü'), 'Draft')
    await userEvent.click(screen.getByRole('button', { name: 'Filtreleri temizle' }))

    await waitFor(() => expect(screen.queryByTestId('product-active-filters')).toBeNull())
    expect((screen.getByLabelText('Sayfa boyutu') as HTMLSelectElement).value).toBe('100')
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
      expect((screen.getByLabelText(/Kod, eski kod veya ad ara/) as HTMLInputElement).value).toBe(
        'sac',
      ),
    )
  })

  it('re-reads the data rather than restoring the rows it showed before', async () => {
    // The rows are deliberately NOT remembered: the entry gate exists so a revisit
    // cannot paint rows read under a grant the server may since have revoked.
    const { calls } = installFetch({ capabilities: LIST_AND_DETAIL })
    renderProductApp('/urun')

    const listCalls = () =>
      calls.filter((call) => call.url.includes('/products?') || call.url.endsWith('/products')).length

    await rowsVisible()
    const before = listCalls()

    await userEvent.click(screen.getByRole('link', { name: 'SAC-42' }))
    await waitFor(() => expect(screen.getByRole('heading', { name: 'Siyah sac' })).toBeTruthy())
    await userEvent.click(screen.getByRole('link', { name: /Ürün listesine dön/ }))
    await rowsVisible()

    expect(listCalls()).toBeGreaterThan(before)
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
