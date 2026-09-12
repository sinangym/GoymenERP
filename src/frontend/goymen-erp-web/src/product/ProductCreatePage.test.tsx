import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PRODUCT_CAPABILITY } from './capabilities'
import { GROUP_SAC, installFetch, json, mutation, productCard, productCore, renderProductApp } from './testHarness'

const CREATE_ONLY = [PRODUCT_CAPABILITY.productsCreate]
const PATH = '/urun/yeni'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const formVisible = () => waitFor(() => screen.getByRole('heading', { name: 'Yeni ürün' }))

async function fillMinimum() {
  await userEvent.type(screen.getByLabelText(/Ürün adı/), 'Siyah sac')
  // The group is a REFERENCE the server offered, never typed text.
  await waitFor(() => expect(screen.getByRole('option', { name: /Sac \(SAC\)/ })).toBeTruthy())
  await userEvent.selectOptions(screen.getByLabelText(/Kod grubu/), GROUP_SAC)
}

const saveButton = () => screen.getByRole('button', { name: 'Taslak olarak kaydet' })

describe('mounting', () => {
  it('mounts nothing without the create capability', async () => {
    installFetch({ capabilities: [PRODUCT_CAPABILITY.productsList] })
    renderProductApp(PATH)

    await waitFor(() => expect(screen.getByText('Bulunamadı — sayfa')).toBeTruthy())
  })

  it('never resolves the static create path as a product id', async () => {
    const { calls } = installFetch({ capabilities: CREATE_ONLY })
    renderProductApp(PATH)

    await formVisible()
    expect(calls.some((call) => call.url.includes('/products/yeni'))).toBe(false)
  })
})

describe('the code group is a reference', () => {
  it('is offered as a choice with its prefix, never as free text', async () => {
    installFetch({ capabilities: CREATE_ONLY })
    renderProductApp(PATH)

    await formVisible()
    const group = await waitFor(() => screen.getByLabelText(/Kod grubu/))
    expect(group.tagName).toBe('SELECT')
    expect(within(group).getByRole('option', { name: /Sac \(SAC\)/ })).toBeTruthy()
  })

  it('cannot create at all when the code-group read fails', async () => {
    // The group is a real technical dependency of CREATION, not of the screen: the
    // form is shown and the submit is closed, with the reason stated.
    installFetch({ capabilities: CREATE_ONLY, onGroups: () => json({}, 503) })
    renderProductApp(PATH)

    await formVisible()
    await waitFor(() => expect(screen.getByTestId('product-groups-unavailable')).toBeTruthy())
    expect((saveButton() as HTMLButtonElement).disabled).toBe(true)
  })

  it('rejects a group whose prefix is not the canonical format', async () => {
    /**
     * README pins uppercase ASCII [A-Z0-9] and names `ÇELİK` as unsupported and
     * owner-correctable. Offering it would be a dead end presented as a choice.
     */
    installFetch({
      capabilities: CREATE_ONLY,
      onGroups: () => json([{ id: GROUP_SAC, name: 'Çelik', prefix: 'ÇELİK' }]),
    })
    renderProductApp(PATH)

    await formVisible()
    await waitFor(() => expect(screen.getByTestId('product-groups-unavailable')).toBeTruthy())
  })

  it('says so when the catalogue is empty', async () => {
    installFetch({ capabilities: CREATE_ONLY, onGroups: () => json([]) })
    renderProductApp(PATH)

    await formVisible()
    await waitFor(() => expect(screen.getByTestId('product-groups-empty')).toBeTruthy())
  })
})

describe('what the screen deliberately does not offer', () => {
  it('has no product code field and says the server generates it', async () => {
    installFetch({ capabilities: CREATE_ONLY })
    renderProductApp(PATH)

    await formVisible()
    expect(screen.queryByLabelText(/Ürün kodu/)).toBeNull()
    expect(screen.getByText(/Ürün kodunu sunucu/)).toBeTruthy()
  })

  it('offers no class, family or reference field, and says why', async () => {
    /**
     * The request accepts them, but no catalogue read is published — so the client
     * could only invent a reference, which §19.3 forbids. Saying so is what makes
     * this a known gap rather than a screen that looks finished.
     */
    installFetch({ capabilities: CREATE_ONLY })
    renderProductApp(PATH)

    await formVisible()
    expect(screen.queryByLabelText(/Ürün ailesi/)).toBeNull()
    expect(screen.queryByLabelText(/Ürün sınıfı/)).toBeNull()
    expect(screen.queryByLabelText(/Takip politikası/)).toBeNull()
    expect(screen.getByText(/bu sürümde sunulmuyor/)).toBeTruthy()
  })

  it('has no activate action — the contract includes no activation command', async () => {
    installFetch({ capabilities: CREATE_ONLY })
    renderProductApp(PATH)

    await formVisible()
    expect(screen.queryByRole('button', { name: /Aktive/i })).toBeNull()
    expect(screen.getByText(/aktivasyon ayrı bir işlemdir/i)).toBeTruthy()
  })

  it('offers no AI draft panel, because no AI capability is published', async () => {
    installFetch({ capabilities: CREATE_ONLY })
    const { container } = renderProductApp(PATH)

    await formVisible()
    expect((container.textContent ?? '').toLowerCase()).not.toContain('yapay zeka')
    expect(screen.queryByText(/AI öner/i)).toBeNull()
  })
})

describe('validation', () => {
  it('sends nothing and mints no key when the form fails client-side', async () => {
    const { calls } = installFetch({ capabilities: CREATE_ONLY })
    renderProductApp(PATH)

    await formVisible()
    await userEvent.click(saveButton())

    await waitFor(() => expect(screen.getByTestId('product-create-error-summary')).toBeTruthy())
    // Nothing was sent, so there is nothing to be idempotent about.
    expect(calls.filter((call) => call.method === 'POST')).toHaveLength(0)
  })

  it('associates each error with its input and focuses the summary', async () => {
    installFetch({ capabilities: CREATE_ONLY })
    renderProductApp(PATH)

    await formVisible()
    await userEvent.click(saveButton())

    const name = await waitFor(() => screen.getByLabelText(/Ürün adı/))
    expect(name.getAttribute('aria-invalid')).toBe('true')
    expect(name.getAttribute('aria-describedby')).toContain('name-error')
    // ONE focus rule: the summary takes focus and offers the jump, so a screen
    // reader announces why nothing happened before naming the fields.
    await waitFor(() =>
      expect(document.activeElement).toBe(screen.getByTestId('product-create-error-summary')),
    )
  })

  it('maps a server field code to Turkish and keeps the user input', async () => {
    installFetch({
      capabilities: CREATE_ONLY,
      onCreate: () => json({ code: 'validation_failed', errors: [{ field: 'name', code: 'invalid_name' }] }, 400),
    })
    renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(saveButton())

    await waitFor(() => expect(screen.getByText('Ürün adı geçersiz.')).toBeTruthy())
    // A failed save keeps what was typed.
    expect((screen.getByLabelText(/Ürün adı/) as HTMLInputElement).value).toBe('Siyah sac')
  })

  it('falls back to a general sentence for a field code it does not know', async () => {
    /**
     * The server sends machine codes, so an unknown one is expected rather than
     * exceptional. Losing the FIELD binding would be worse than losing message
     * precision, so the error still reaches its input.
     */
    installFetch({
      capabilities: CREATE_ONLY,
      onCreate: () =>
        json({ code: 'validation_failed', errors: [{ field: 'groupId', code: 'brand_new' }] }, 400),
    })
    renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(saveButton())

    await waitFor(() => expect(screen.getByText('Sunucu bu alanı reddetti.')).toBeTruthy())
    expect(screen.getByLabelText(/Kod grubu/).getAttribute('aria-invalid')).toBe('true')
  })
})

describe('roles', () => {
  it('starts every role unanswered', async () => {
    installFetch({ capabilities: CREATE_ONLY })
    renderProductApp(PATH)

    await formVisible()
    const unset = screen.getAllByRole('radio', { name: 'Belirtilmedi' })
    expect(unset).toHaveLength(4)
    expect(unset.every((radio) => (radio as HTMLInputElement).checked)).toBe(true)
  })

  it('sends the tagged write shape and omits an unanswered role', async () => {
    const { calls } = installFetch({ capabilities: CREATE_ONLY })
    renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    // The first "Evet" is `purchasable` — roles render in declaration order.
    await userEvent.click(screen.getAllByRole('radio', { name: 'Evet' })[0])
    await userEvent.click(saveButton())

    await waitFor(() => expect(calls.some((call) => call.method === 'POST')).toBe(true))
    const body = JSON.parse(calls.find((call) => call.method === 'POST')?.body ?? '{}')
    expect(body.roles).toEqual({ purchasable: { value: true, provenance: 'manual' } })
  })

  it('sends an explicit manual false when the user answered no', async () => {
    const { calls } = installFetch({ capabilities: CREATE_ONLY })
    renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(screen.getAllByRole('radio', { name: 'Hayır' })[1])
    await userEvent.click(saveButton())

    await waitFor(() => expect(calls.some((call) => call.method === 'POST')).toBe(true))
    const body = JSON.parse(calls.find((call) => call.method === 'POST')?.body ?? '{}')
    // "Explicit false is preserved" — and it is never `derived`, which is server-only.
    expect(body.roles).toEqual({ sellable: { value: false, provenance: 'manual' } })
  })

  it('omits the roles member entirely when nothing was answered', async () => {
    const { calls } = installFetch({ capabilities: CREATE_ONLY })
    renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(saveButton())

    await waitFor(() => expect(calls.some((call) => call.method === 'POST')).toBe(true))
    const body = JSON.parse(calls.find((call) => call.method === 'POST')?.body ?? '{}')
    expect(Object.hasOwn(body, 'roles')).toBe(false)
  })
})

describe('the request itself', () => {
  it('carries the create minimum, no company id, no server-owned field', async () => {
    const { calls } = installFetch({ capabilities: CREATE_ONLY })
    renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(saveButton())

    await waitFor(() => expect(calls.some((call) => call.method === 'POST')).toBe(true))
    const post = calls.find((call) => call.method === 'POST')
    expect(post?.url).toContain('/products')
    expect(post?.companyId).toBeNull()
    const body = JSON.parse(post?.body ?? '{}')
    expect(body.name).toBe('Siyah sac')
    expect(body.groupId).toBe(GROUP_SAC)
    // `JsonUnmappedMemberHandling.Disallow` would reject any of these outright.
    for (const forbidden of ['lifecycle', 'companyId', 'code', 'version', 'expectedVersion']) {
      expect(Object.hasOwn(body, forbidden)).toBe(false)
    }
  })

  it('carries exactly one idempotency key in the contract’s alphabet', async () => {
    const { calls } = installFetch({ capabilities: CREATE_ONLY })
    renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(saveButton())

    await waitFor(() => expect(calls.some((call) => call.method === 'POST')).toBe(true))
    const key = calls.find((call) => call.method === 'POST')?.idempotencyKey ?? ''
    expect(key).toMatch(/^[0-9a-f]{32}$/)
    expect(key.length).toBeLessThanOrEqual(128)
  })
})

describe('success', () => {
  it('reports the Draft and shows the server-assigned code only now', async () => {
    installFetch({ capabilities: CREATE_ONLY, onCreate: () => json(mutation(), 201) })
    renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(saveButton())

    await waitFor(() => expect(screen.getByText('Taslak kaydedildi')).toBeTruthy())
    expect(screen.getByText('SAC-42 — Siyah sac')).toBeTruthy()
  })

  it('reads isReplay from the response BODY', async () => {
    // `isReplay` is a response field in this contract, not a header.
    installFetch({
      capabilities: CREATE_ONLY,
      onCreate: () => json(mutation({ isReplay: true }), 201),
    })
    renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(saveButton())

    await waitFor(() => expect(screen.getByText(/ikinci bir kayıt oluşturulmadı/)).toBeTruthy())
  })

  it('rejects a 201 that says the product was created Active', async () => {
    /**
     * A new record is always born Draft; activation is a separate command with its
     * own audit record. Reporting this as an ordinary success would hide an
     * activation from the person responsible for it. It becomes an UNKNOWN outcome,
     * not a failure — the record does exist.
     */
    installFetch({
      capabilities: CREATE_ONLY,
      onCreate: () =>
        json(mutation({ card: productCard({ product: productCore({ lifecycle: 'Active' }) }) }), 201),
    })
    renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(saveButton())

    await waitFor(() => expect(screen.getByTestId('product-create-unknown')).toBeTruthy())
    expect(screen.queryByText('Taslak kaydedildi')).toBeNull()
  })

  it('does not accept a 200 as a create — but calls it uncertain, not failed', async () => {
    /**
     * "Replay remains the original successful operation status (create 201, edit
     * 200)", so a 200 here is not a success this client can describe. It is NOT
     * reported as a failure either: the server said 2xx, so telling the user their
     * record was not created would very likely be false. The same key replays.
     */
    installFetch({ capabilities: CREATE_ONLY, onCreate: () => json(mutation(), 200) })
    renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(saveButton())

    await waitFor(() => expect(screen.getByTestId('product-create-unknown')).toBeTruthy())
    expect(screen.queryByText('Taslak kaydedildi')).toBeNull()
  })
})

describe('unknown outcome', () => {
  it('retries the SAME key rather than starting a new attempt', async () => {
    const { calls } = installFetch({
      capabilities: CREATE_ONLY,
      onCreate: (attempt) => (attempt === 1 ? json({}, 503) : json(mutation(), 201)),
    })
    renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(saveButton())

    await waitFor(() => expect(screen.getByTestId('product-create-unknown')).toBeTruthy())
    // The copy must not invite a fresh save: the Draft may already exist.
    expect(screen.getByText(/yeni bir kayıt oluşmaz/)).toBeTruthy()

    await userEvent.click(screen.getByRole('button', { name: 'Aynı işlemi tekrar dene' }))
    await waitFor(() => expect(screen.getByText('Taslak kaydedildi')).toBeTruthy())

    const posts = calls.filter((call) => call.method === 'POST')
    expect(posts).toHaveLength(2)
    // "An unknown outcome must not be retried with a new key."
    expect(posts[0].idempotencyKey).toBe(posts[1].idempotencyKey)
  })

  it('reuses the key when the unchanged form is submitted again', async () => {
    const { calls } = installFetch({
      capabilities: CREATE_ONLY,
      onCreate: (attempt) => (attempt === 1 ? json({}, 503) : json(mutation(), 201)),
    })
    renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(saveButton())
    await waitFor(() => expect(screen.getByTestId('product-create-unknown')).toBeTruthy())

    await userEvent.click(saveButton())
    await waitFor(() => expect(screen.getByText('Taslak kaydedildi')).toBeTruthy())

    const posts = calls.filter((call) => call.method === 'POST')
    expect(posts[0].idempotencyKey).toBe(posts[1].idempotencyKey)
  })

  it('mints a new key once the payload actually changed', async () => {
    const { calls } = installFetch({
      capabilities: CREATE_ONLY,
      onCreate: (attempt) => (attempt === 1 ? json({}, 503) : json(mutation(), 201)),
    })
    renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(saveButton())
    await waitFor(() => expect(screen.getByTestId('product-create-unknown')).toBeTruthy())

    await userEvent.type(screen.getByLabelText(/Ürün adı/), ' galvaniz')
    await userEvent.click(saveButton())
    await waitFor(() => expect(screen.getByText('Taslak kaydedildi')).toBeTruthy())

    const posts = calls.filter((call) => call.method === 'POST')
    // A CHANGED payload is a different business attempt and must not replay onto
    // the previous one.
    expect(posts[0].idempotencyKey).not.toBe(posts[1].idempotencyKey)
  })
})

describe('definite refusals', () => {
  it('reports a 403 as unauthorized rather than as an unknown outcome', async () => {
    installFetch({ capabilities: CREATE_ONLY, onCreate: () => json({ code: 'forbidden' }, 403) })
    renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(saveButton())

    await waitFor(() => expect(screen.getByText('Yetkiniz yok')).toBeTruthy())
    expect(screen.queryByTestId('product-create-unknown')).toBeNull()
  })

  it('reports an idempotency conflict as a definite refusal', async () => {
    installFetch({
      capabilities: CREATE_ONLY,
      onCreate: () => json({ code: 'idempotency_conflict' }, 409),
    })
    renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(saveButton())

    await waitFor(() => expect(screen.getByTestId('product-create-failed')).toBeTruthy())
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

    await userEvent.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('alertdialog')).toBeNull())
    expect((screen.getByLabelText(/Ürün adı/) as HTMLInputElement).value).toBe('Siyah sac')
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
    await userEvent.click(saveButton())
    await waitFor(() => expect(screen.getByTestId('product-create-unknown')).toBeTruthy())

    await userEvent.click(screen.getByRole('button', { name: 'Vazgeç' }))
    // The Draft may exist and nothing confirmed it; walking away silently is how an
    // unresolved write is forgotten.
    expect(screen.getByRole('alertdialog')).toBeTruthy()
  })
})
