import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PRODUCT_CAPABILITY } from './capabilities'
import { resetPendingAttempts } from './pendingAttempt'
import { PRODUCT_CREATE_TEXT } from './productText'
import {
  CLASS_FLAT,
  COMPANY_A,
  COMPANY_B,
  FAMILY_FLAT,
  GROUP_SAC,
  installFetch,
  json,
  mutation,
  productCard,
  productCore,
  referenceOptions,
  renderProductApp,
  type HarnessConfig,
} from './testHarness'

const CREATE_ONLY = [PRODUCT_CAPABILITY.productsCreate]
const PATH = '/urun/yeni'

afterEach(() => {
  cleanup()
  // The pending-attempt store outlives a component by design, so a case that ends
  // with an unresolved write would otherwise seed the next one.
  resetPendingAttempts()
  // The shell remembers the committed company in localStorage. Where storage works
  // (CI), a case that switched company would otherwise boot the next case in the
  // other company and a "switch" to it would be a no-op — which is exactly how two
  // switch cases passed locally and failed on CI.
  try {
    window.localStorage.clear()
  } catch {
    // Storage unavailable in this environment: nothing was remembered.
  }
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const formVisible = () => waitFor(() => screen.getByRole('heading', { name: 'Yeni ürün' }))

/**
 * A switch has COMMITTED only when the picker is enabled again on the new value.
 * The DOM value alone flips the moment the option is picked, before the shell has
 * done anything — waiting on it proved nothing.
 */
const switchedTo = (companyId: string) =>
  waitFor(() => {
    const picker = screen.getByLabelText('Şirket') as HTMLSelectElement
    expect(picker.disabled).toBe(false)
    expect(picker.value).toBe(companyId)
  })

/**
 * The code-group read has ANSWERED. Until it does the submit is closed (there is no
 * group to create under), so a click before this is a click on a disabled button —
 * the form renders first, and on a slow machine the read can still be pending.
 */
const groupsAnswered = () =>
  waitFor(() => expect(screen.getByRole('option', { name: /Sac \(SAC\)/ })).toBeTruthy())

/**
 * The reference-options read that picking a group starts has ANSWERED.
 *
 * A reconnect while that read is still in flight does not issue a new one: TanStack
 * joins the request already running. A case that swaps the fake fetch and then
 * reconnects must wait for this first, or the swapped answer is never asked for —
 * which is exactly what a slow machine does to it (the first read is still pending).
 */
const referencesAnswered = () =>
  waitFor(() => expect(screen.getByRole('option', { name: 'Yassı ürün' })).toBeTruthy())

/** Makes every stale query refetch through TanStack's own reconnect path. */
function reconnect() {
  act(() => {
    window.dispatchEvent(new Event('offline'))
    window.dispatchEvent(new Event('online'))
  })
}

async function fillMinimum() {
  await userEvent.type(screen.getByLabelText(/Ürün adı/), 'Siyah sac')
  // The group is a REFERENCE the server offered, never typed text.
  await groupsAnswered()
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
    // The select is rendered before the code-group read answers; the option is not.
    await waitFor(() => expect(within(group).getByRole('option', { name: /Sac \(SAC\)/ })).toBeTruthy())
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

  it('offers class and family as REFERENCES scoped to the chosen group', async () => {
    const { calls } = installFetch({ capabilities: CREATE_ONLY })
    renderProductApp(PATH)

    await formVisible()
    // Nothing is asked for until a group is chosen: there would be nothing to
    // scope by, and the answer could not be trusted.
    expect(calls.some((call) => call.url.includes('reference-options'))).toBe(false)

    await fillMinimum()
    await waitFor(() =>
      expect(calls.some((call) => call.url.includes(`reference-options?groupId=${GROUP_SAC}`))).toBe(
        true,
      ),
    )
    await waitFor(() => expect(screen.getByRole('option', { name: 'Yassı ürün' })).toBeTruthy())
  })

  it('sends a chosen class and family as ids, and omits them when unchosen', async () => {
    const { calls } = installFetch({ capabilities: CREATE_ONLY })
    renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await waitFor(() => screen.getByRole('option', { name: 'Yassı ürün' }))
    await userEvent.selectOptions(screen.getByLabelText(/Ürün sınıfı/), CLASS_FLAT)
    // Choosing a class starts a read scoped by it. Until that answers, the class is not
    // among the offered ones and the form refuses to send it — as it should.
    await waitFor(() => expect(calls.some((call) => call.url.includes(`classId=${CLASS_FLAT}`))).toBe(true))
    await waitFor(() => screen.getByRole('option', { name: 'Soğuk hadde' }))
    await userEvent.click(saveButton())

    await waitFor(() => expect(calls.some((call) => call.method === 'POST')).toBe(true))
    const body = JSON.parse(calls.find((call) => call.method === 'POST')?.body ?? '{}')
    expect(body.classId).toBe(CLASS_FLAT)
    // ABSENT rather than null: both are optional, and omitting claims less.
    expect(Object.hasOwn(body, 'familyId')).toBe(false)
  })

  it('closes a catalogue the server could not read, and says which', async () => {
    /**
     * README: "An unavailable catalogue is different from a valid empty catalogue
     * and must not produce made-up choices." An empty one stays open and says it is
     * empty; an unavailable one is closed and says it could not be read.
     */
    installFetch({
      capabilities: CREATE_ONLY,
      onOptions: () =>
        json(referenceOptions({ classes: [], unavailableCatalogs: ['classes'] })),
    })
    renderProductApp(PATH)

    await formVisible()
    await fillMinimum()

    await waitFor(() =>
      expect((screen.getByLabelText(/Ürün sınıfı/) as HTMLSelectElement).disabled).toBe(true),
    )
    expect(screen.getByText(/Bu katalog şu anda okunamıyor/)).toBeTruthy()
    // The families catalogue was fine, so its field stays open.
    expect((screen.getByLabelText(/Ürün ailesi/) as HTMLSelectElement).disabled).toBe(false)
  })

  it('offers no arbitrary template or tracking-policy editor and explains the release boundary', async () => {
    installFetch({ capabilities: CREATE_ONLY })
    renderProductApp(PATH)

    await formVisible()
    expect(screen.queryByLabelText(/Takip politikası/)).toBeNull()
    expect(screen.queryByLabelText(/Şablon/)).toBeNull()
    expect(screen.getByLabelText('Stok birimi (kg)')).toBeTruthy()
    expect(screen.getByText(/Takip politikası henüz düzenlenemez/)).toBeTruthy()
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
    await groupsAnswered()
    await userEvent.click(saveButton())

    await waitFor(() => expect(screen.getByTestId('product-create-error-summary')).toBeTruthy())
    // Nothing was sent, so there is nothing to be idempotent about.
    expect(calls.filter((call) => call.method === 'POST')).toHaveLength(0)
  })

  it('associates each error with its input and focuses the summary', async () => {
    installFetch({ capabilities: CREATE_ONLY })
    renderProductApp(PATH)

    await formVisible()
    await groupsAnswered()
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
    // The committed company travels as the validated ORIGIN header (§15) — and
    // only there: it is not in the payload, and it is not the Product's owner.
    expect(post?.companyId).toBe(COMPANY_A)
    expect(JSON.parse(post?.body ?? '{}')).not.toHaveProperty('companyId')
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

describe('the validated origin (X-Company-Id)', () => {
  it('goes out on every Product read and write as the committed company, and never in a payload', async () => {
    /**
     * §15 (6a649ef): the header "carries the server-validated source of the
     * operation; it creates neither Product ownership nor global authority". The
     * actual endpoints require it (`RequireCompanyContext`), so it is on every
     * request — reference reads included — while the payload stays free of it.
     */
    const { calls } = installFetch({ capabilities: CREATE_ONLY })
    renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(saveButton())
    await waitFor(() => expect(calls.some((call) => call.method === 'POST')).toBe(true))

    const product = calls.filter((call) => call.url.includes('/products'))
    expect(product.length).toBeGreaterThanOrEqual(3) // code-groups, reference-options, POST
    for (const call of product) expect(call.companyId, `${call.method} ${call.url}`).toBe(COMPANY_A)
    for (const call of product.filter((c) => c.body !== null)) {
      expect(JSON.parse(call.body as string)).not.toHaveProperty('companyId')
    }
  })

  it('is FROZEN with the attempt: the same-key retry carries the origin of the first send', async () => {
    const { calls } = installFetch({
      capabilities: CREATE_ONLY,
      onCreate: (attempt) => (attempt === 1 ? json({}, 503) : json(mutation(), 201)),
    })
    renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(saveButton())
    await waitFor(() => expect(screen.getByTestId('product-create-unknown')).toBeTruthy())
    await userEvent.click(screen.getByRole('button', { name: 'Aynı işlemi tekrar dene' }))
    await waitFor(() => expect(calls.filter((call) => call.method === 'POST')).toHaveLength(2))

    const posts = calls.filter((call) => call.method === 'POST')
    expect(posts[0].companyId).toBe(COMPANY_A)
    expect(posts[1].companyId).toBe(posts[0].companyId)
    expect(posts[1].idempotencyKey).toBe(posts[0].idempotencyKey)
    expect(posts[1].body).toBe(posts[0].body)
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
  it('reports a command 403 to the shared boundary, which closes the capability', async () => {
    /**
     * A refusal of an authenticated write is the same fact as a refusal of a read:
     * the central boundary re-fences the capability so a route return cannot mount
     * the feature again on authority the server just rejected. Handling it only
     * locally left the capability snapshot looking valid.
     */
    installFetch({ capabilities: CREATE_ONLY, onCreate: () => json({ code: 'forbidden' }, 403) })
    renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(saveButton())

    await waitFor(() => expect(document.querySelector('[data-state]')).not.toBeNull())
    expect(screen.queryByRole('button', { name: 'Taslak olarak kaydet' })).toBeNull()
  })

  it('reports a reference-read 403 to the boundary too', async () => {
    // `code-groups` is an authenticated read under the same global authority.
    installFetch({ capabilities: CREATE_ONLY, onGroups: () => json({ code: 'forbidden' }, 403) })
    renderProductApp(PATH)

    await waitFor(() => expect(document.querySelector('[data-state]')).not.toBeNull())
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

describe('the shell dirty gate', () => {
  it('makes a company switch ASK while the form is dirty, and keeps the form', async () => {
    /**
     * The screen remounts on a namespace change, which is how an attempt is kept
     * from crossing contexts. But a remount is destruction, not protection: without
     * the shell's dirty scope the picker switched straight through and the form and
     * its idempotency key went with it, unasked.
     */
    installFetch({ capabilities: CREATE_ONLY })
    renderProductApp(PATH)

    await formVisible()
    await userEvent.type(screen.getByLabelText(/Ürün adı/), 'Siyah sac')
    await userEvent.selectOptions(screen.getByLabelText('Şirket'), COMPANY_B)

    const dialog = await waitFor(() => screen.getByRole('dialog'))
    expect(within(dialog).getByRole('button', { name: 'Kal' })).toBeTruthy()
    // The form is still there behind the dialog — "Kal" has something to keep.
    expect((screen.getByLabelText(/Ürün adı/) as HTMLInputElement).value).toBe('Siyah sac')
  })

  it('asks on a company switch while an outcome is UNRESOLVED, form untouched', async () => {
    // The pending write is the thing a silent switch must not carry, even when the
    // fields look pristine.
    installFetch({ capabilities: CREATE_ONLY, onCreate: () => json({}, 503) })
    renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(saveButton())
    await waitFor(() => expect(screen.getByTestId('product-create-unknown')).toBeTruthy())

    await userEvent.selectOptions(screen.getByLabelText('Şirket'), COMPANY_B)
    expect(await waitFor(() => screen.getByRole('dialog'))).toBeTruthy()
  })

  it('lets a switch through once the form is clean again', async () => {
    // The gate must not become a trap: an empty form is not unsaved work.
    installFetch({ capabilities: CREATE_ONLY })
    renderProductApp(PATH)

    await formVisible()
    await userEvent.selectOptions(screen.getByLabelText('Şirket'), COMPANY_B)
    expect(screen.queryByRole('dialog')).toBeNull()
  })
})

describe('SPA exit guard', () => {
  it('blocks a navigation the screen did not initiate', async () => {
    /**
     * Back, a sidebar link and a row link never went through the local Cancel
     * handler, so an unresolved write could leave with a single click. The blocker
     * covers the router-driven exits; the shell's dirty gate covers the company
     * switch, which is not a navigation.
     */
    installFetch({
      capabilities: [...CREATE_ONLY, PRODUCT_CAPABILITY.productsList],
      onCreate: () => json({}, 503),
    })
    const { router } = renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(saveButton())
    await waitFor(() => expect(screen.getByTestId('product-create-unknown')).toBeTruthy())

    // A router-driven departure: the screen's own Cancel handler is not involved.
    await act(async () => {
      await router.navigate('/urun')
    })

    const dialog = await waitFor(() => screen.getByRole('alertdialog'))
    expect(within(dialog).getByRole('button', { name: 'Sayfada kal' })).toBeTruthy()
    // Staying keeps the unresolved attempt visible rather than losing it.
    await userEvent.click(within(dialog).getByRole('button', { name: 'Sayfada kal' }))
    await waitFor(() => expect(screen.getByTestId('product-create-unknown')).toBeTruthy())
  })

  it('lets the navigation through when nothing is unresolved', async () => {
    // The guard must not become a trap: an untouched form is not unsaved work.
    installFetch({ capabilities: [...CREATE_ONLY, PRODUCT_CAPABILITY.productsList] })
    const { router } = renderProductApp(PATH)

    await formVisible()
    await act(async () => {
      await router.navigate('/urun')
    })
    await waitFor(() => expect(screen.getByRole('heading', { name: 'Ürünler' })).toBeTruthy())
    expect(screen.queryByRole('alertdialog')).toBeNull()
  })

  it('proceeds when the user chooses to leave', async () => {
    installFetch({
      capabilities: [...CREATE_ONLY, PRODUCT_CAPABILITY.productsList],
      onCreate: () => json({}, 503),
    })
    const { router } = renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(saveButton())
    await waitFor(() => expect(screen.getByTestId('product-create-unknown')).toBeTruthy())

    await act(async () => {
      await router.navigate('/urun')
    })
    const dialog = await waitFor(() => screen.getByRole('alertdialog'))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Değişiklikleri bırak' }))

    await waitFor(() => expect(screen.getByRole('heading', { name: 'Ürünler' })).toBeTruthy())
  })
})

describe('a refused retry', () => {
  it('does not report the record as not created', async () => {
    /**
     * First send commits, its response is lost, and the same-key retry is refused.
     * The refusal answers the RETRY only — saying "Kayıt oluşturulmadı" would be a
     * claim it cannot support.
     *
     * An `idempotency_conflict` is used rather than a 403 because an AUTH refusal
     * re-fences the capability and closes the route; both behaviours are correct,
     * and only one can be observed at a time.
     */
    installFetch({
      capabilities: CREATE_ONLY,
      onCreate: (attempt) =>
        attempt === 1 ? json({}, 503) : json({ code: 'idempotency_conflict' }, 409),
    })
    renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(saveButton())
    await waitFor(() => expect(screen.getByTestId('product-create-unknown')).toBeTruthy())

    await userEvent.click(screen.getByRole('button', { name: 'Aynı işlemi tekrar dene' }))

    await waitFor(() =>
      expect(screen.getByTestId('product-create-unknown').textContent).toContain(
        'ilk denemenin uygulanmadığı anlamına gelmez',
      ),
    )
    expect(screen.queryByText('Kayıt oluşturulmadı.')).toBeNull()
  })
})

describe('an unknown attempt across a REAL capability re-fence', () => {
  /**
   * The lifecycle Backend traced on #238, driven through the real shell in ONE
   * tree — no second render, no reset: an unknown outcome; a same-key retry the
   * server refuses with 403; the boundary re-fencing the capability and closing
   * the route underneath the form; and a fresh entry the server answers with a NEW
   * capability generation. Same actor, same session, same origin, same context —
   * so the person finds their own attempt, and nothing is retried for them.
   */
  async function reachRefusedRetry() {
    const first = installFetch({
      capabilities: CREATE_ONLY,
      onCreate: (attempt) =>
        attempt === 1 ? json({}, 503) : json({ code: 'forbidden' }, 403),
    })
    const { router } = renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(saveButton())
    await waitFor(() => expect(screen.getByTestId('product-create-unknown')).toBeTruthy())

    await userEvent.click(screen.getByRole('button', { name: 'Aynı işlemi tekrar dene' }))
    // The 403 re-fences the capability: the route closes onto the neutral block
    // and the form — and, before this fix, its key — is gone.
    await waitFor(() => expect(document.querySelector('[data-state]')).not.toBeNull())
    expect(screen.queryByTestId('product-create-unknown')).toBeNull()

    const firstKey = first.calls.find((call) => call.method === 'POST')?.idempotencyKey ?? null
    expect(firstKey).not.toBeNull()
    return { router, firstKey: firstKey as string }
  }

  /** A fresh entry the server now answers with a DIFFERENT capability generation. */
  async function reenterAuthorized(
    router: ReturnType<typeof renderProductApp>['router'],
    onCreate?: HarnessConfig['onCreate'],
  ) {
    const mock = installFetch({
      capabilities: CREATE_ONLY,
      capabilityGeneration: 'gen-2',
      onCreate,
    })
    await act(async () => {
      await router.navigate('/urun')
    })
    await act(async () => {
      await router.navigate(PATH)
    })
    await formVisible()
    return mock
  }

  it('is found again under a NEW capability generation, and is not retried on its own', async () => {
    const { router } = await reachRefusedRetry()
    const { calls } = await reenterAuthorized(router)

    await waitFor(() => expect(screen.getByTestId('product-create-unknown')).toBeTruthy())
    // The refusal answered the RETRY; the first send is still unresolved, and the
    // screen keeps saying so instead of reporting the record as not created.
    expect(screen.getByText(PRODUCT_CREATE_TEXT.retryRefusedStillUnknown)).toBeTruthy()
    // Fresh access is a separate fence and was spent: the entry was re-verified
    // against the server before anything mounted.
    expect(calls.some((call) => call.url.includes('/shell/capabilities'))).toBe(true)
    // Restoring is not retrying: no write left the client on its own.
    expect(calls.filter((call) => call.method === 'POST')).toHaveLength(0)
  })

  it('retries with the SAME key when the person asks, and accepts the server’s replay', async () => {
    const { router, firstKey } = await reachRefusedRetry()
    const { calls } = await reenterAuthorized(router, () =>
      json(mutation({ isReplay: true }), 201),
    )

    await waitFor(() => expect(screen.getByTestId('product-create-unknown')).toBeTruthy())
    await userEvent.click(screen.getByRole('button', { name: 'Aynı işlemi tekrar dene' }))

    await waitFor(() => expect(screen.getByText(PRODUCT_CREATE_TEXT.replayedNote)).toBeTruthy())
    const posts = calls.filter((call) => call.method === 'POST')
    expect(posts).toHaveLength(1)
    // One click, one product: the key the server already saw.
    expect(posts[0].idempotencyKey).toBe(firstKey)
  })

  it('is not shown under another committed context, and is still there on return', async () => {
    /**
     * With the form already closed by the 403 nobody is asked about the switch,
     * so nothing was abandoned: the attempt is simply not THIS context's. It is
     * neither shown here nor destroyed, and it is waiting where it was started.
     */
    const { router } = await reachRefusedRetry()
    installFetch({ capabilities: CREATE_ONLY, capabilityGeneration: 'gen-2' })

    await userEvent.selectOptions(screen.getByLabelText('Şirket'), COMPANY_B)
    await switchedTo(COMPANY_B)
    await reenterAuthorized(router)
    expect(screen.queryByTestId('product-create-unknown')).toBeNull()

    await userEvent.selectOptions(screen.getByLabelText('Şirket'), COMPANY_A)
    await switchedTo(COMPANY_A)
    await reenterAuthorized(router)
    await waitFor(() => expect(screen.getByTestId('product-create-unknown')).toBeTruthy())
  })

  it('is dropped when the person confirms a company switch — "bekleyen işlem bırakılır"', async () => {
    /**
     * The shell's dialog promises exactly that. The form cannot keep the promise —
     * it is unmounted the instant the switch is confirmed — so the observer outside
     * the gate does, for the context that was being left.
     */
    installFetch({ capabilities: CREATE_ONLY, onCreate: () => json({}, 503) })
    const { router } = renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(saveButton())
    await waitFor(() => expect(screen.getByTestId('product-create-unknown')).toBeTruthy())

    await userEvent.selectOptions(screen.getByLabelText('Şirket'), COMPANY_B)
    const dialog = await waitFor(() => screen.getByRole('dialog'))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Değişiklikleri bırak' }))
    await switchedTo(COMPANY_B)

    // Back under the context the attempt was started in: it is gone, because the
    // person was asked and answered.
    await userEvent.selectOptions(screen.getByLabelText('Şirket'), COMPANY_A)
    await switchedTo(COMPANY_A)
    await act(async () => {
      await router.navigate('/urun')
    })
    await act(async () => {
      await router.navigate(PATH)
    })
    await formVisible()
    expect(screen.queryByTestId('product-create-unknown')).toBeNull()
  })

  /**
   * The teardown that is NOT an answer. The shell removes the whole outlet — the
   * dialog included — when the directory read stops being ready, and it does so
   * with the switch question still open. Nobody said "bırak".
   */
  async function directoryFailsWhileAsking() {
    installFetch({ capabilities: CREATE_ONLY, onCreate: () => json({}, 503) })
    const { router, queryClient } = renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(saveButton())
    await waitFor(() => expect(screen.getByTestId('product-create-unknown')).toBeTruthy())

    await userEvent.selectOptions(screen.getByLabelText('Şirket'), COMPANY_B)
    await waitFor(() => screen.getByRole('dialog'))

    // A reconnect while ANY read is still in flight joins it instead of issuing a new one (see
    // `referencesAnswered`), so the swapped 503 below would never be asked for. On a loaded runner the
    // group pick's options read (or another) can still be pending here; wait until nothing is.
    await waitFor(() => expect(queryClient.isFetching()).toBe(0))
    installFetch({
      capabilities: CREATE_ONLY,
      onDirectory: () => json({}, 503),
      onCreate: () => json({}, 503),
    })
    reconnect()
    await waitFor(() => expect(document.querySelector('[data-state]')).not.toBeNull())
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(screen.queryByTestId('product-create-unknown')).toBeNull()

    // The directory recovers; the question is still open, and still unanswered.
    installFetch({ capabilities: CREATE_ONLY, onCreate: () => json({}, 503) })
    await userEvent.click(screen.getByRole('button', { name: 'Tekrar dene' }))
    const dialog = await waitFor(() => screen.getByRole('dialog'))
    return { router, dialog }
  }

  it('survives a directory failure while the switch question is open, and "Kal" keeps it', async () => {
    const { dialog } = await directoryFailsWhileAsking()
    await userEvent.click(within(dialog).getByRole('button', { name: 'Kal' }))

    await formVisible()
    await waitFor(() => expect(screen.getByTestId('product-create-unknown')).toBeTruthy())
  })

  it('survives that failure, and is dropped only when the answer is then "bırak"', async () => {
    const { router, dialog } = await directoryFailsWhileAsking()
    await userEvent.click(within(dialog).getByRole('button', { name: 'Değişiklikleri bırak' }))
    await switchedTo(COMPANY_B)

    installFetch({ capabilities: CREATE_ONLY })
    await userEvent.selectOptions(screen.getByLabelText('Şirket'), COMPANY_A)
    await switchedTo(COMPANY_A)
    await act(async () => {
      await router.navigate('/urun')
    })
    await act(async () => {
      await router.navigate(PATH)
    })
    await formVisible()
    expect(screen.queryByTestId('product-create-unknown')).toBeNull()
  })

  it('keeps a canonical 201 that lands after a reference read closed the screen', async () => {
    /**
     * The other way a send outlives its screen: the POST is in flight when a
     * reference read answers 403, the boundary closes the route, and the server's
     * 201 then lands in a continuation nobody can see. That answer is the only
     * record the write succeeded; settling it there left the fresh return an empty
     * form, one click away from a second key for a product that exists.
     */
    let answer: ((response: Response) => void) | null = null
    installFetch({
      capabilities: CREATE_ONLY,
      onCreate: () =>
        new Promise<Response>((resolve) => {
          answer = resolve
        }),
    })
    const { router } = renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(saveButton())
    await waitFor(() => expect(answer).not.toBeNull())
    await referencesAnswered()

    // The next reference read is refused; TanStack's reconnect refetch issues it.
    installFetch({ capabilities: CREATE_ONLY, onOptions: () => json({ code: 'forbidden' }, 403) })
    reconnect()
    await waitFor(() => expect(document.querySelector('[data-state]')).not.toBeNull())
    expect(screen.queryByRole('button', { name: 'Taslak olarak kaydet' })).toBeNull()

    // The canonical answer, with nobody there to show it.
    await act(async () => {
      ;(answer as unknown as (response: Response) => void)(json(mutation(), 201))
    })

    const { calls } = installFetch({ capabilities: CREATE_ONLY, capabilityGeneration: 'gen-2' })
    await act(async () => {
      await router.navigate('/urun')
    })
    await act(async () => {
      await router.navigate(PATH)
    })
    await waitFor(() => expect(screen.getByRole('heading', { name: 'Taslak kaydedildi' })).toBeTruthy())
    // The server's code, from the answer that was kept — and no write of our own.
    expect(screen.getByText(/SAC-42/)).toBeTruthy()
    expect(calls.filter((call) => call.method === 'POST')).toHaveLength(0)

    // Shown once, settled: the next entry is a fresh form.
    await act(async () => {
      await router.navigate('/urun')
    })
    await act(async () => {
      await router.navigate(PATH)
    })
    await formVisible()
    expect(screen.queryByRole('heading', { name: 'Taslak kaydedildi' })).toBeNull()
    expect(screen.queryByTestId('product-create-unknown')).toBeNull()
  })

  it('is kept when the screen is torn down while the send is still in flight', async () => {
    /**
     * The first send, not only a retry: the key is in the store BEFORE the await,
     * so a screen that disappears mid-flight leaves it behind. The harness tears
     * the tree down to stand in for the route closing — the in-tree evidence for a
     * closure is the gen-2 cases above; this one covers the interval they cannot.
     */
    let answer: ((response: Response) => void) | null = null
    const first = installFetch({
      capabilities: CREATE_ONLY,
      onCreate: () =>
        new Promise<Response>((resolve) => {
          answer = resolve
        }),
    })
    const { unmount } = renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(saveButton())
    await waitFor(() => expect(answer).not.toBeNull())
    const firstKey = first.calls.find((call) => call.method === 'POST')?.idempotencyKey

    unmount()
    cleanup()
    // The answer arrives after the screen is gone: nobody is there to render it.
    await act(async () => {
      ;(answer as unknown as (response: Response) => void)(json({}, 503))
    })

    const { calls } = installFetch({ capabilities: CREATE_ONLY })
    renderProductApp(PATH)
    await formVisible()
    await waitFor(() => expect(screen.getByTestId('product-create-unknown')).toBeTruthy())
    expect(calls.filter((call) => call.method === 'POST')).toHaveLength(0)

    await userEvent.click(screen.getByRole('button', { name: 'Aynı işlemi tekrar dene' }))
    await waitFor(() => expect(calls.filter((call) => call.method === 'POST')).toHaveLength(1))
    expect(calls.find((call) => call.method === 'POST')?.idempotencyKey).toBe(firstKey)
  })

  it('keeps a 201 that resolves in the SAME batch as the refusal that closes the screen', async () => {
    /**
     * The remaining interval of the unseen-success gap: the boundary's 403 and the
     * canonical 201 land in one React batch. The form is still mounted when the
     * send continuation runs — so "mounted" was never an acknowledgement — and the
     * commit of that batch closes the gate before any success screen exists. The
     * only acknowledgement is a completed screen's post-render effect.
     */
    let answerCreate: ((response: Response) => void) | null = null
    let answerOptions: ((response: Response) => void) | null = null
    installFetch({
      capabilities: CREATE_ONLY,
      onCreate: () =>
        new Promise<Response>((resolve) => {
          answerCreate = resolve
        }),
    })
    const { router } = renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(saveButton())
    await waitFor(() => expect(answerCreate).not.toBeNull())
    await referencesAnswered()

    installFetch({
      capabilities: CREATE_ONLY,
      onOptions: () =>
        new Promise<Response>((resolve) => {
          answerOptions = resolve
        }),
    })
    reconnect()
    await waitFor(() => expect(answerOptions).not.toBeNull())

    // Both answers inside ONE act: one batch, one commit, and that commit closes the route.
    await act(async () => {
      ;(answerOptions as unknown as (response: Response) => void)(json({ code: 'forbidden' }, 403))
      ;(answerCreate as unknown as (response: Response) => void)(json(mutation(), 201))
    })
    await waitFor(() => expect(document.querySelector('[data-state]')).not.toBeNull())
    expect(screen.queryByRole('heading', { name: 'Taslak kaydedildi' })).toBeNull()

    const { calls } = installFetch({ capabilities: CREATE_ONLY, capabilityGeneration: 'gen-2' })
    await act(async () => {
      await router.navigate('/urun')
    })
    await act(async () => {
      await router.navigate(PATH)
    })
    await waitFor(() => expect(screen.getByRole('heading', { name: 'Taslak kaydedildi' })).toBeTruthy())
    expect(screen.getByText(/SAC-42/)).toBeTruthy()
    expect(calls.filter((call) => call.method === 'POST')).toHaveLength(0)
  })

  it('is dropped when the user chooses to leave', async () => {
    installFetch({
      capabilities: [...CREATE_ONLY, PRODUCT_CAPABILITY.productsList],
      onCreate: () => json({}, 503),
    })
    const { router } = renderProductApp(PATH)

    await formVisible()
    await fillMinimum()
    await userEvent.click(saveButton())
    await waitFor(() => expect(screen.getByTestId('product-create-unknown')).toBeTruthy())

    await act(async () => {
      await router.navigate('/urun')
    })
    const dialog = await waitFor(() => screen.getByRole('alertdialog'))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Değişiklikleri bırak' }))
    await waitFor(() => expect(screen.getByRole('heading', { name: 'Ürünler' })).toBeTruthy())

    // Same tree, same context, straight back: the user was asked and said yes;
    // keeping the key would resurrect a decision they just made.
    await act(async () => {
      await router.navigate(PATH)
    })
    await formVisible()
    expect(screen.queryByTestId('product-create-unknown')).toBeNull()
  })
})

describe('unsaved guard', () => {
  it('asks ONCE when Cancel is pressed, and staying keeps the input', async () => {
    /**
     * Cancel used to open its own dialog and then navigate, which the blocker caught
     * a second time — the user answered "leave" and was asked again. There is now
     * one guard for every router exit.
     */
    installFetch({ capabilities: [...CREATE_ONLY, PRODUCT_CAPABILITY.productsList] })
    renderProductApp(PATH)

    await formVisible()
    await userEvent.type(screen.getByLabelText(/Ürün adı/), 'Siyah sac')
    await userEvent.click(screen.getByRole('button', { name: 'Vazgeç' }))

    const dialog = await waitFor(() => screen.getByRole('alertdialog'))
    expect(screen.getAllByRole('alertdialog')).toHaveLength(1)
    // Focus lands on the SAFE option, not the destructive one.
    expect(document.activeElement).toBe(within(dialog).getByRole('button', { name: 'Sayfada kal' }))

    await userEvent.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('alertdialog')).toBeNull())
    expect((screen.getByLabelText(/Ürün adı/) as HTMLInputElement).value).toBe('Siyah sac')
  })

  it('leaves on the FIRST "leave" — the choice is not asked twice', async () => {
    installFetch({ capabilities: [...CREATE_ONLY, PRODUCT_CAPABILITY.productsList] })
    renderProductApp(PATH)

    await formVisible()
    await userEvent.type(screen.getByLabelText(/Ürün adı/), 'Siyah sac')
    await userEvent.click(screen.getByRole('button', { name: 'Vazgeç' }))

    const dialog = await waitFor(() => screen.getByRole('alertdialog'))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Değişiklikleri bırak' }))

    await waitFor(() => expect(screen.getByRole('heading', { name: 'Ürünler' })).toBeTruthy())
    expect(screen.queryByRole('alertdialog')).toBeNull()
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

describe('technical card: fixed family facts and the three open unit roles (P03.1 r2)', () => {
  const TEMPLATE = '81000000-0000-4000-8000-000000000001'
  const KG = '81000000-0000-4000-8000-000000000002'
  const technicalOptions = () => referenceOptions({
    templates: [{ id: TEMPLATE, version: '1', displayName: 'Sentetik sac şablonu', fields: [{ key: 'thickness',
      kind: 'measureMillimetre', role: null, required: true, dictionary: null, allowsCandidate: false,
      appliesWhenField: null, appliesWhenCode: null, parentField: null }] }],
    familyFacts: [{ id: FAMILY_FLAT, code: 'TEST', displayName: 'Sentetik aile', cins: 'Metal', tur: 'Sac', tip: 'Düz',
      templateId: TEMPLATE, templateVersion: '1' }],
    dictionaries: [],
    units: [{ id: KG, displayName: 'Kilogram', symbol: 'kg', isKilogram: true }],
  })
  const factOf = (term: string) => screen.queryByText(term, { selector: 'dt' })?.nextElementSibling?.textContent

  it('shows the chosen family’s server Cins/Tür/Tip and sends only the open unit roles', async () => {
    const { calls } = installFetch({ capabilities: [PRODUCT_CAPABILITY.productsCreate],
      onOptions: () => json(technicalOptions()) })
    renderProductApp('/urun/yeni')
    await screen.findByRole('heading', { name: 'Yeni ürün' })
    await userEvent.type(screen.getByLabelText(/Ürün adı/), 'Sentetik teknik ürün')
    await screen.findByRole('option', { name: /Sac \(SAC\)/ })
    await userEvent.selectOptions(screen.getByLabelText(/Kod grubu/), GROUP_SAC)
    await screen.findByRole('option', { name: 'Soğuk hadde' })
    expect(factOf('Cins')).toBeUndefined()
    await userEvent.selectOptions(screen.getByLabelText('Ürün ailesi', { exact: false }), FAMILY_FLAT)
    expect([factOf('Cins'), factOf('Tür'), factOf('Tip')]).toEqual(['Metal', 'Sac', 'Düz'])
    for (const label of ['Fiyat birimi', 'Üretim birimi', 'Paket birimi'])
      expect(screen.queryByRole('combobox', { name: label })).toBeNull()
    expect(screen.queryByText(/henüz yazmaya açık olmayan/)).toBeNull()
    await userEvent.type(screen.getByLabelText(/thickness/), '1.25')
    await userEvent.selectOptions(screen.getByLabelText('Stok birimi (kg)'), KG)
    await userEvent.click(screen.getByRole('button', { name: 'Taslak olarak kaydet' }))
    await screen.findByRole('heading', { name: 'Taslak kaydedildi' })
    const post = JSON.parse(calls.find(c => c.method === 'POST')!.body!)
    expect(post.references.units).toEqual({ stock: KG })
  })

  it('shows no family facts while the family catalogue is unavailable', async () => {
    installFetch({ capabilities: [PRODUCT_CAPABILITY.productsCreate],
      onOptions: () => json({ ...technicalOptions(), unavailableCatalogs: ['families'] }) })
    renderProductApp('/urun/yeni')
    await screen.findByRole('heading', { name: 'Yeni ürün' })
    await screen.findByRole('option', { name: /Sac \(SAC\)/ })
    await userEvent.selectOptions(screen.getByLabelText(/Kod grubu/), GROUP_SAC)
    expect(factOf('Cins')).toBeUndefined()
  })
})
