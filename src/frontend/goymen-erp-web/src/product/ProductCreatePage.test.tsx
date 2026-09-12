import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PRODUCT_CAPABILITY } from './capabilities'
import { PRODUCT_1, installFetch, json, renderProductApp } from './testHarness'

const CREATE_ONLY = [PRODUCT_CAPABILITY.productsCreate]
const PATH = '/urun/yeni'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

async function formVisible() {
  return waitFor(() => screen.getByRole('heading', { name: 'Yeni ürün' }))
}

async function fillMinimum() {
  await userEvent.type(screen.getByLabelText(/Ürün adı/), 'Siyah sac')
  await userEvent.type(screen.getByLabelText(/Ürün sınıfı/), 'Yassı ürün')
}

function created(over: Record<string, unknown> = {}) {
  return { id: PRODUCT_1, code: 'SAC-42', name: 'Siyah sac', lifecycle: 'Draft', version: '1', ...over }
}

describe('mounting', () => {
  it('mounts nothing without the create capability', async () => {
    installFetch({ capabilities: [PRODUCT_CAPABILITY.productsList] })
    renderProductApp(PATH)

    await waitFor(() => expect(screen.getByText('Bulunamadı — sayfa')).toBeTruthy())
  })

  it('never resolves the static create path as a product id', async () => {
    /**
     * The Cari slice shipped this bug (#172 P1-2): `/cari/yeni` matched the
     * `:partyId` route, so the detail screen mounted and issued a read for a record
     * that cannot exist.
     */
    const { calls } = installFetch({ capabilities: CREATE_ONLY })
    renderProductApp(PATH)

    await formVisible()
    expect(calls.some((call) => call.url.includes('/products/yeni'))).toBe(false)
  })
})

describe('what the screen deliberately does not offer', () => {
  it('has no product code field and says the server generates it', async () => {
    installFetch({ capabilities: CREATE_ONLY })
    renderProductApp(PATH)

    await formVisible()
    // §5.1/§7: the code is server-generated, and the admin manual-code exception
    // needs a permission string that does not exist yet.
    expect(screen.queryByLabelText(/Ürün kodu/)).toBeNull()
    expect(screen.getByText(/Ürün kodunu sunucu üretir/)).toBeTruthy()
  })

  it('has no activate action — activation is a separate, gated command', async () => {
    installFetch({ capabilities: CREATE_ONLY })
    renderProductApp(PATH)

    await formVisible()
    // §6.2/§6.6: save and activate are separate commands, and activation stays
    // fail-closed until the platform/permission/persistence gates pass.
    expect(screen.queryByRole('button', { name: /Aktive/i })).toBeNull()
    expect(screen.getByText(/aktivasyon ayrı bir işlemdir/i)).toBeTruthy()
  })

  it('offers no AI draft panel, because no AI capability is published', async () => {
    installFetch({ capabilities: CREATE_ONLY })
    const { container } = renderProductApp(PATH)

    await formVisible()
    // §12 allows an AI draft only when the capability is open. A panel backed by
    // nothing would be a fabricated feature.
    expect((container.textContent ?? '').toLowerCase()).not.toContain('yapay zeka')
    expect(screen.queryByText(/AI öner/i)).toBeNull()
  })
})

describe('validation', () => {
  it('sends nothing and mints no key when the form fails client-side', async () => {
    const { calls } = installFetch({ capabilities: CREATE_ONLY })
    renderProductApp(PATH)

    await formVisible()
    await userEvent.click(screen.getByRole('button', { name: 'Taslak olarak kaydet' }))

    await waitFor(() => expect(screen.getByTestId('product-create-error-summary')).toBeTruthy())
    // Nothing was sent, so there is nothing to be idempotent about.
    expect(calls.filter((call) => call.method === 'POST')).toHaveLength(0)
  })

  it('associates each error with its input and focuses the first one', async () => {
    installFetch({ capabilities: CREATE_ONLY })
    renderProductApp(PATH)

    await formVisible()
    await userEvent.click(screen.getByRole('button', { name: 'Taslak olarak kaydet' }))

    const name = await waitFor(() => screen.getByLabelText(/Ürün adı/))
    // §10.3: programmatically associated, not just a red border.
    expect(name.getAttribute('aria-invalid')).toBe('true')
    expect(name.getAttribute('aria-describedby')).toContain('name-error')
    // ONE focus rule: the summary takes focus and offers the jump, so a screen
    // reader announces why nothing happened before naming the individual fields.
    await waitFor(() =>
      expect(document.activeElement).toBe(screen.getByTestId('product-create-error-summary')),
    )
  })

  it('jumps to a field from the summary', async () => {
    installFetch({ capabilities: CREATE_ONLY })
    renderProductApp(PATH)

    await formVisible()
    await userEvent.type(screen.getByLabelText(/Ürün adı/), 'Siyah sac')
    await userEvent.click(screen.getByRole('button', { name: 'Taslak olarak kaydet' }))

    const summary = await waitFor(() => screen.getByTestId('product-create-error-summary'))
    await userEvent.click(within(summary).getByRole('button'))
    expect(document.activeElement).toBe(screen.getByLabelText(/Ürün sınıfı/))
  })

  it('lets the server win on a field it names, and keeps the user input', async () => {
    installFetch({
      capabilities: CREATE_ONLY,
      onCreate: () =>
        json({ code: 'validation_failed', errors: { name: 'Bu ad başka üründe kullanılıyor.' } }, 400),
    })
    renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(screen.getByRole('button', { name: 'Taslak olarak kaydet' }))

    await waitFor(() => expect(screen.getByText('Bu ad başka üründe kullanılıyor.')).toBeTruthy())
    // AT-006: a failed save keeps what was typed.
    expect((screen.getByLabelText(/Ürün adı/) as HTMLInputElement).value).toBe('Siyah sac')
  })
})

describe('roles', () => {
  it('starts every role unanswered', async () => {
    installFetch({ capabilities: CREATE_ONLY })
    renderProductApp(PATH)

    await formVisible()
    // A checkbox cannot represent "not answered"; §5.2 forbids persisting that as
    // a determined no.
    const unset = screen.getAllByRole('radio', { name: 'Belirtilmedi' })
    expect(unset).toHaveLength(4)
    expect(unset.every((radio) => (radio as HTMLInputElement).checked)).toBe(true)
  })

  it('omits an unanswered role from the payload instead of sending false', async () => {
    const { calls } = installFetch({ capabilities: CREATE_ONLY })
    renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    // The first "Evet" is `purchasable` — the roles render in declaration order.
    await userEvent.click(screen.getAllByRole('radio', { name: 'Evet' })[0])
    await userEvent.click(screen.getByRole('button', { name: 'Taslak olarak kaydet' }))

    await waitFor(() => expect(calls.some((call) => call.method === 'POST')).toBe(true))
    const post = calls.find((call) => call.method === 'POST')
    const body = JSON.parse(post?.body ?? '{}')
    // Only the answered role travels; the rest stay `not-evaluated` server-side.
    expect(body.roles).toEqual({ purchasable: true })
  })

  it('sends an explicit no when the user answered no', async () => {
    const { calls } = installFetch({ capabilities: CREATE_ONLY })
    renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    const noRadios = screen.getAllByRole('radio', { name: 'Hayır' })
    await userEvent.click(noRadios[1])
    await userEvent.click(screen.getByRole('button', { name: 'Taslak olarak kaydet' }))

    await waitFor(() => expect(calls.some((call) => call.method === 'POST')).toBe(true))
    const body = JSON.parse(calls.find((call) => call.method === 'POST')?.body ?? '{}')
    expect(body.roles).toEqual({ sellable: false })
  })
})

describe('the request itself', () => {
  it('carries no company id and no lifecycle', async () => {
    const { calls } = installFetch({ capabilities: CREATE_ONLY })
    renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(screen.getByRole('button', { name: 'Taslak olarak kaydet' }))

    await waitFor(() => expect(calls.some((call) => call.method === 'POST')).toBe(true))
    const post = calls.find((call) => call.method === 'POST')
    expect(post?.companyId).toBeNull()
    const body = JSON.parse(post?.body ?? '{}')
    // §6.1: the state is the server's to set. §15: the frontend adds no CompanyId.
    expect(Object.hasOwn(body, 'lifecycle')).toBe(false)
    expect(Object.hasOwn(body, 'companyId')).toBe(false)
    expect(Object.hasOwn(body, 'code')).toBe(false)
  })

  it('carries exactly one idempotency key', async () => {
    const { calls } = installFetch({ capabilities: CREATE_ONLY })
    renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(screen.getByRole('button', { name: 'Taslak olarak kaydet' }))

    await waitFor(() => expect(calls.some((call) => call.method === 'POST')).toBe(true))
    expect(calls.find((call) => call.method === 'POST')?.idempotencyKey).toMatch(/^[0-9a-f]{32}$/)
  })
})

describe('success', () => {
  it('reports the Draft and shows the server-assigned code only now', async () => {
    installFetch({ capabilities: CREATE_ONLY, onCreate: () => json(created(), 201) })
    renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(screen.getByRole('button', { name: 'Taslak olarak kaydet' }))

    await waitFor(() => expect(screen.getByText('Taslak kaydedildi')).toBeTruthy())
    // §7: the code was never predicted before the response arrived.
    expect(screen.getByText('SAC-42 — Siyah sac')).toBeTruthy()
  })

  it('says a replay was not a second record', async () => {
    installFetch({
      capabilities: CREATE_ONLY,
      onCreate: () => json(created(), 201, { 'Idempotency-Replayed': 'true' }),
    })
    renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(screen.getByRole('button', { name: 'Taslak olarak kaydet' }))

    await waitFor(() => expect(screen.getByText(/ikinci bir kayıt oluşturulmadı/)).toBeTruthy())
  })

  it('offers the detail only when the detail capability is held', async () => {
    installFetch({ capabilities: CREATE_ONLY, onCreate: () => json(created(), 201) })
    const { unmount } = renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(screen.getByRole('button', { name: 'Taslak olarak kaydet' }))
    await waitFor(() => expect(screen.getByText('Taslak kaydedildi')).toBeTruthy())
    expect(screen.queryByRole('link', { name: 'Ürün kartını aç' })).toBeNull()
    unmount()
    cleanup()

    installFetch({
      capabilities: [...CREATE_ONLY, PRODUCT_CAPABILITY.productsDetail],
      onCreate: () => json(created(), 201),
    })
    renderProductApp(PATH)
    await formVisible()
    await fillMinimum()
    await userEvent.click(screen.getByRole('button', { name: 'Taslak olarak kaydet' }))
    await waitFor(() => expect(screen.getByRole('link', { name: 'Ürün kartını aç' })).toBeTruthy())
  })

  it('rejects a 201 that says the product was created Active', async () => {
    /**
     * §6.1/§6.2. Reporting that as an ordinary success would hide an activation
     * from the person responsible for it. It becomes an UNKNOWN outcome, not a
     * failure — the record does exist.
     */
    installFetch({
      capabilities: CREATE_ONLY,
      onCreate: () => json(created({ lifecycle: 'Active' }), 201),
    })
    renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(screen.getByRole('button', { name: 'Taslak olarak kaydet' }))

    await waitFor(() => expect(screen.getByTestId('product-create-unknown')).toBeTruthy())
    expect(screen.queryByText('Taslak kaydedildi')).toBeNull()
  })
})

describe('unknown outcome', () => {
  it('retries the SAME key rather than starting a new attempt', async () => {
    const { calls } = installFetch({
      capabilities: CREATE_ONLY,
      onCreate: (attempt) => (attempt === 1 ? json({}, 503) : json(created(), 201)),
    })
    renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(screen.getByRole('button', { name: 'Taslak olarak kaydet' }))

    await waitFor(() => expect(screen.getByTestId('product-create-unknown')).toBeTruthy())
    // The copy must not invite a fresh save: the Draft may already exist.
    expect(screen.getByText(/yeni bir kayıt oluşmaz/)).toBeTruthy()

    await userEvent.click(screen.getByRole('button', { name: 'Aynı işlemi tekrar dene' }))
    await waitFor(() => expect(screen.getByText('Taslak kaydedildi')).toBeTruthy())

    const posts = calls.filter((call) => call.method === 'POST')
    expect(posts).toHaveLength(2)
    // One key per SNAPSHOT, never per click — the server replays instead of
    // creating a second product.
    expect(posts[0].idempotencyKey).toBe(posts[1].idempotencyKey)
  })

  it('reuses the key when the unchanged form is submitted again', async () => {
    const { calls } = installFetch({
      capabilities: CREATE_ONLY,
      onCreate: (attempt) => (attempt === 1 ? json({}, 503) : json(created(), 201)),
    })
    renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(screen.getByRole('button', { name: 'Taslak olarak kaydet' }))
    await waitFor(() => expect(screen.getByTestId('product-create-unknown')).toBeTruthy())

    await userEvent.click(screen.getByRole('button', { name: 'Taslak olarak kaydet' }))
    await waitFor(() => expect(screen.getByText('Taslak kaydedildi')).toBeTruthy())

    const posts = calls.filter((call) => call.method === 'POST')
    expect(posts[0].idempotencyKey).toBe(posts[1].idempotencyKey)
  })

  it('mints a new key once the payload actually changed', async () => {
    const { calls } = installFetch({
      capabilities: CREATE_ONLY,
      onCreate: (attempt) => (attempt === 1 ? json({}, 503) : json(created(), 201)),
    })
    renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(screen.getByRole('button', { name: 'Taslak olarak kaydet' }))
    await waitFor(() => expect(screen.getByTestId('product-create-unknown')).toBeTruthy())

    await userEvent.type(screen.getByLabelText(/Ürün adı/), ' galvaniz')
    await userEvent.click(screen.getByRole('button', { name: 'Taslak olarak kaydet' }))
    await waitFor(() => expect(screen.getByText('Taslak kaydedildi')).toBeTruthy())

    const posts = calls.filter((call) => call.method === 'POST')
    // A CHANGED payload is a different business attempt and must not replay onto
    // the previous one.
    expect(posts[0].idempotencyKey).not.toBe(posts[1].idempotencyKey)
  })
})

describe('definite refusals', () => {
  it('reports a code collision as a hard block that created nothing', async () => {
    installFetch({
      capabilities: CREATE_ONLY,
      onCreate: () => json({ code: 'product_code_collision' }, 409),
    })
    renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(screen.getByRole('button', { name: 'Taslak olarak kaydet' }))

    await waitFor(() => expect(screen.getByText('Bu kod zaten kullanılıyor')).toBeTruthy())
    expect(screen.getByText(/Kayıt oluşturulmadı/)).toBeTruthy()
    // A definite refusal is NOT an unknown outcome.
    expect(screen.queryByTestId('product-create-unknown')).toBeNull()
  })

  it('reports a 403 as unauthorized rather than as an unknown outcome', async () => {
    installFetch({ capabilities: CREATE_ONLY, onCreate: () => json({ code: 'forbidden' }, 403) })
    renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(screen.getByRole('button', { name: 'Taslak olarak kaydet' }))

    await waitFor(() => expect(screen.getByText('Yetkiniz yok')).toBeTruthy())
    expect(screen.queryByTestId('product-create-unknown')).toBeNull()
  })
})

describe('unsaved guard', () => {
  it('asks before leaving with typed input, and staying keeps it', async () => {
    installFetch({ capabilities: [...CREATE_ONLY, PRODUCT_CAPABILITY.productsList] })
    renderProductApp(PATH)

    await formVisible()
    await userEvent.type(screen.getByLabelText(/Ürün adı/), 'Siyah sac')
    await userEvent.click(screen.getByRole('button', { name: 'Vazgeç' }))

    const dialog = await waitFor(() => screen.getByRole('alertdialog'))
    // Focus lands on the SAFE option, not the destructive one.
    expect(document.activeElement).toBe(within(dialog).getByRole('button', { name: 'Sayfada kal' }))

    await userEvent.click(within(dialog).getByRole('button', { name: 'Sayfada kal' }))
    await waitFor(() => expect(screen.queryByRole('alertdialog')).toBeNull())
    expect((screen.getByLabelText(/Ürün adı/) as HTMLInputElement).value).toBe('Siyah sac')
  })

  it('treats Escape as "stay"', async () => {
    installFetch({ capabilities: [...CREATE_ONLY, PRODUCT_CAPABILITY.productsList] })
    renderProductApp(PATH)

    await formVisible()
    await userEvent.type(screen.getByLabelText(/Ürün adı/), 'Siyah sac')
    await userEvent.click(screen.getByRole('button', { name: 'Vazgeç' }))
    await waitFor(() => screen.getByRole('alertdialog'))

    await userEvent.keyboard('{Escape}')
    // The destructive option must never be what a stray key press picks.
    await waitFor(() => expect(screen.queryByRole('alertdialog')).toBeNull())
    expect(screen.getByRole('heading', { name: 'Yeni ürün' })).toBeTruthy()
  })

  it('does not ask when nothing was typed', async () => {
    installFetch({ capabilities: [...CREATE_ONLY, PRODUCT_CAPABILITY.productsList] })
    renderProductApp(PATH)

    await formVisible()
    await userEvent.click(screen.getByRole('button', { name: 'Vazgeç' }))

    expect(screen.queryByRole('alertdialog')).toBeNull()
  })

  it('asks even with an untouched form while an outcome is unresolved', async () => {
    installFetch({
      capabilities: [...CREATE_ONLY, PRODUCT_CAPABILITY.productsList],
      onCreate: () => json({}, 503),
    })
    renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(screen.getByRole('button', { name: 'Taslak olarak kaydet' }))
    await waitFor(() => expect(screen.getByTestId('product-create-unknown')).toBeTruthy())

    await userEvent.click(screen.getByRole('button', { name: 'Vazgeç' }))
    // The Draft may exist and nothing confirmed it; walking away silently is how
    // an unresolved write is forgotten.
    expect(screen.getByRole('alertdialog')).toBeTruthy()
  })
})
