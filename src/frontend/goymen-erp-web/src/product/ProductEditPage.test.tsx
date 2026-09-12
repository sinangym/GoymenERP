import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PRODUCT_CAPABILITY } from './capabilities'
import { resetPendingAttempts } from './pendingAttempt'
import { PRODUCT_EDIT_TEXT } from './productText'
import {
  CLASS_FLAT,
  COMPANY_B,
  GROUP_SAC,
  PRODUCT_1,
  installFetch,
  json,
  mutation,
  productCard,
  productCore,
  referenceOptions,
  renderProductApp,
} from './testHarness'

const EDIT_ONLY = [PRODUCT_CAPABILITY.productsEdit]
const PATH = `/urun/${PRODUCT_1}/duzenle`

afterEach(() => {
  cleanup()
  // The pending-attempt store outlives a component by design, so a case that ends
  // with an unresolved write would otherwise seed the next one.
  resetPendingAttempts()
  // See the create tests: the shell's stored company preference must not leak.
  try {
    window.localStorage.clear()
  } catch {
    // Storage unavailable in this environment: nothing was remembered.
  }
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const formVisible = () => waitFor(() => screen.getByRole('heading', { name: 'Ürünü düzenle' }))
const saveButton = () => screen.getByRole('button', { name: 'Değişiklikleri kaydet' })

async function changeNameAndReason() {
  const name = screen.getByLabelText(/Ürün adı/)
  await userEvent.clear(name)
  await userEvent.type(name, 'Galvaniz sac')
  await userEvent.type(screen.getByLabelText(/Değişiklik gerekçesi/), 'Ad düzeltildi')
}

describe('mounting', () => {
  it('mounts nothing and writes nothing without the edit capability', async () => {
    const { calls } = installFetch({ capabilities: [PRODUCT_CAPABILITY.productsDetail] })
    renderProductApp(PATH)

    await waitFor(() => expect(screen.getByText('Bulunamadı — sayfa')).toBeTruthy())
    expect(calls.some((call) => call.method === 'PATCH')).toBe(false)
  })

  it('never resolves the static edit segment as a product id', async () => {
    const { calls } = installFetch({ capabilities: EDIT_ONLY })
    renderProductApp(PATH)

    await formVisible()
    expect(calls.some((call) => call.url.includes('/products/duzenle'))).toBe(false)
  })
})

describe('the diff, not the form', () => {
  it('sends ONLY the changed field, with expectedVersion and a reason', async () => {
    const { calls } = installFetch({ capabilities: EDIT_ONLY })
    renderProductApp(PATH)

    await formVisible()
    await changeNameAndReason()
    await userEvent.click(saveButton())

    await waitFor(() => expect(calls.some((call) => call.method === 'PATCH')).toBe(true))
    const patch = calls.find((call) => call.method === 'PATCH')
    // The contract's route: PATCH /api/v1/products/{id}/draft.
    expect(patch?.url).toContain(`/products/${PRODUCT_1}/draft`)
    expect(patch?.companyId).toBeNull()
    const body = JSON.parse(patch?.body ?? '{}')
    /**
     * An omitted member is `Optional` with no value, which the server reads as
     * unchanged. Sending every field would make each untouched input a write, so a
     * colleague's concurrent change would be silently reverted by someone who never
     * looked at it.
     */
    expect(body.changes).toEqual({ name: 'Galvaniz sac' })
    expect(body.expectedVersion).toBe('7')
    expect(body.reason).toBe('Ad düzeltildi')
  })

  it('sends a role change in the tagged write shape', async () => {
    const { calls } = installFetch({ capabilities: EDIT_ONLY })
    renderProductApp(PATH)

    await formVisible()
    // `sellable` is currently a manual false; answer yes.
    await userEvent.click(screen.getAllByRole('radio', { name: 'Evet' })[1])
    await userEvent.type(screen.getByLabelText(/Değişiklik gerekçesi/), 'Rol düzeltildi')
    await userEvent.click(saveButton())

    await waitFor(() => expect(calls.some((call) => call.method === 'PATCH')).toBe(true))
    const body = JSON.parse(calls.find((call) => call.method === 'PATCH')?.body ?? '{}')
    expect(body.changes.roles).toEqual({ sellable: { value: true, provenance: 'manual' } })
    expect(Object.hasOwn(body.changes, 'name')).toBe(false)
  })

  it('refuses a save that would write nothing, and sends no request', async () => {
    const { calls } = installFetch({ capabilities: EDIT_ONLY })
    renderProductApp(PATH)

    await formVisible()
    await userEvent.click(saveButton())

    await waitFor(() => expect(screen.getByTestId('product-edit-error-summary')).toBeTruthy())
    expect(calls.filter((call) => call.method === 'PATCH')).toHaveLength(0)
  })

  it('requires a reason for a real change', async () => {
    installFetch({ capabilities: EDIT_ONLY })
    renderProductApp(PATH)

    await formVisible()
    const name = screen.getByLabelText(/Ürün adı/)
    await userEvent.clear(name)
    await userEvent.type(name, 'Galvaniz sac')
    await userEvent.click(saveButton())

    // One name change is visible in both Göymen companies.
    await waitFor(() =>
      expect(screen.getByTestId('product-edit-error-summary').textContent).toContain(
        'Değişiklik gerekçesi',
      ),
    )
  })
})

describe('derived roles', () => {
  it('offers no control for a derived role and shows its provenance', async () => {
    installFetch({ capabilities: EDIT_ONLY })
    renderProductApp(PATH)

    await formVisible()
    /**
     * `derived` is server-only and `PreserveDerived` rejects an overwrite. A
     * disabled radio group would suggest the value is merely locked right now
     * rather than not this screen's to set.
     */
    const derived = screen.getByTestId('edit-role-derived-productionOutput')
    expect(derived.textContent).toContain('ilişkiden türetildi')
    const group = derived.closest('fieldset')
    expect(group === null ? [] : within(group).queryAllByRole('radio')).toHaveLength(0)
  })
})

describe('stale version', () => {
  it('stops, shows both versions, keeps the input and offers only a reload', async () => {
    installFetch({
      capabilities: EDIT_ONLY,
      onEdit: () => json({ code: 'version_conflict', expectedVersion: '7', currentVersion: '9' }, 409),
    })
    renderProductApp(PATH)

    await formVisible()
    await changeNameAndReason()
    await userEvent.click(saveButton())

    const stale = await waitFor(() => screen.getByTestId('product-edit-stale'))
    expect(stale.textContent).toContain('7')
    expect(stale.textContent).toContain('9')
    /**
     * No rebase and no overwrite without the user reviewing the difference. There is
     * deliberately no "save anyway" — the other editor's change is a fact this
     * person has not seen.
     */
    expect(screen.queryByRole('button', { name: /yine de|zorla/i })).toBeNull()
    expect(screen.getByRole('button', { name: 'Güncel hâli yükle' })).toBeTruthy()
    expect((screen.getByLabelText(/Ürün adı/) as HTMLInputElement).value).toBe('Galvaniz sac')
  })

  it('never adopts the returned version as its own expectedVersion', async () => {
    const { calls } = installFetch({
      capabilities: EDIT_ONLY,
      onEdit: () => json({ code: 'version_conflict', currentVersion: '9' }, 409),
    })
    renderProductApp(PATH)

    await formVisible()
    await changeNameAndReason()
    await userEvent.click(saveButton())
    await waitFor(() => screen.getByTestId('product-edit-stale'))

    await userEvent.click(saveButton())
    await waitFor(() => expect(calls.filter((c) => c.method === 'PATCH').length).toBe(2))

    // Adopting `9` would turn a refusal into a blind overwrite of whatever the other
    // editor wrote.
    for (const call of calls.filter((c) => c.method === 'PATCH')) {
      expect(JSON.parse(call.body ?? '{}').expectedVersion).toBe('7')
    }
  })
})

describe('lifecycle conflict', () => {
  it('says basic edit is Draft-only rather than showing a generic failure', async () => {
    installFetch({
      capabilities: EDIT_ONLY,
      onEdit: () => json({ code: 'lifecycle_conflict' }, 409),
    })
    renderProductApp(PATH)

    await formVisible()
    await changeNameAndReason()
    await userEvent.click(saveButton())

    await waitFor(() => expect(screen.getByTestId('product-edit-lifecycle')).toBeTruthy())
    expect(screen.getByText(/yalnız taslak kayıtta yapılabilir/)).toBeTruthy()
  })
})

describe('unknown outcome', () => {
  it('retries the SAME key and claims no rollback', async () => {
    const { calls } = installFetch({
      capabilities: EDIT_ONLY,
      onEdit: (attempt) =>
        attempt === 1
          ? json({}, 503)
          : json(mutation({ card: productCard({ product: productCore({ version: '8' }) }) })),
    })
    renderProductApp(PATH)

    await formVisible()
    await changeNameAndReason()
    await userEvent.click(saveButton())

    const unknown = await waitFor(() => screen.getByTestId('product-edit-unknown'))
    expect(unknown.textContent).toContain('ikinci bir değişiklik uygulanmaz')
    // No rollback is asserted: the change may have applied.
    expect(unknown.textContent).not.toMatch(/geri alındı|uygulanmadı/)

    await userEvent.click(screen.getByRole('button', { name: 'Aynı işlemi tekrar dene' }))
    await waitFor(() => expect(screen.getByText('Değişiklikler kaydedildi')).toBeTruthy())

    const patches = calls.filter((call) => call.method === 'PATCH')
    expect(patches).toHaveLength(2)
    expect(patches[0].idempotencyKey).toBe(patches[1].idempotencyKey)
  })

  it('treats an empty 204 as unknown rather than success', async () => {
    /**
     * With no body there is no canonical value or version to show, and painting the
     * submitted draft as the server's result is exactly what the spec forbids. The
     * contract returns `Results.Ok(...)`, so an empty success is drift.
     */
    installFetch({
      capabilities: EDIT_ONLY,
      onEdit: () => new Response(null, { status: 204 }),
    })
    renderProductApp(PATH)

    await formVisible()
    await changeNameAndReason()
    await userEvent.click(saveButton())

    await waitFor(() => expect(screen.getByTestId('product-edit-unknown')).toBeTruthy())
    expect(screen.queryByText('Değişiklikler kaydedildi')).toBeNull()
  })
})

describe('a refused retry', () => {
  it('does not turn into "değişiklik uygulanmadı"', async () => {
    /**
     * The first PATCH commits, its response is lost, and the same-key retry is
     * refused. The refusal answers the RETRY — it is not evidence that the first
     * write rolled back, so the screen keeps saying the outcome is unknown.
     *
     * An `idempotency_conflict` is used rather than a 403 because an AUTH refusal
     * closes the capability (see the next test) and the screen stops rendering
     * altogether. Both behaviours are correct; only one of them can be observed at
     * a time.
     */
    installFetch({
      capabilities: EDIT_ONLY,
      onEdit: (attempt) =>
        attempt === 1 ? json({}, 503) : json({ code: 'idempotency_conflict' }, 409),
    })
    renderProductApp(PATH)

    await formVisible()
    await changeNameAndReason()
    await userEvent.click(saveButton())
    await waitFor(() => expect(screen.getByTestId('product-edit-unknown')).toBeTruthy())

    await userEvent.click(screen.getByRole('button', { name: 'Aynı işlemi tekrar dene' }))

    await waitFor(() =>
      expect(screen.getByTestId('product-edit-unknown').textContent).toContain(
        'ilk denemenin uygulanmadığı anlamına gelmez',
      ),
    )
    expect(screen.queryByText('Değişiklik uygulanmadı.')).toBeNull()
  })

  it('reports an auth refusal of the retry to the boundary, and still claims no rollback', async () => {
    /**
     * Security wins the render: a 401/403 re-fences the capability and the gated
     * route closes, exactly as it does for a read. What must NOT happen — and does
     * not — is the screen announcing that the earlier change was not applied on the
     * way out. Saying nothing is honest here; saying "uygulanmadı" would not be.
     *
     * What happens to the attempt AFTER the route closes is the next case.
     */
    installFetch({
      capabilities: EDIT_ONLY,
      onEdit: (attempt) => (attempt === 1 ? json({}, 503) : json({ code: 'forbidden' }, 403)),
    })
    renderProductApp(PATH)

    await formVisible()
    await changeNameAndReason()
    await userEvent.click(saveButton())
    await waitFor(() => expect(screen.getByTestId('product-edit-unknown')).toBeTruthy())

    await userEvent.click(screen.getByRole('button', { name: 'Aynı işlemi tekrar dene' }))

    await waitFor(() => expect(document.querySelector('[data-state]')).not.toBeNull())
    expect(screen.queryByText('Değişiklik uygulanmadı.')).toBeNull()
  })

  it('reuses the SAME key when the unchanged payload is submitted again', async () => {
    // The attempt is still live after the refusal; a fresh key would risk a second
    // write against a record the first send may already have changed.
    const { calls } = installFetch({
      capabilities: EDIT_ONLY,
      onEdit: (attempt) =>
        attempt === 1
          ? json({}, 503)
          : attempt === 2
            ? json({ code: 'idempotency_conflict' }, 409)
            : json(mutation({ card: productCard({ product: productCore({ version: '8' }) }) })),
    })
    renderProductApp(PATH)

    await formVisible()
    await changeNameAndReason()
    await userEvent.click(saveButton())
    await waitFor(() => screen.getByTestId('product-edit-unknown'))
    await userEvent.click(screen.getByRole('button', { name: 'Aynı işlemi tekrar dene' }))
    await waitFor(() =>
      expect(screen.getByTestId('product-edit-unknown').textContent).toContain(
        'ilk denemenin uygulanmadığı',
      ),
    )
    await userEvent.click(screen.getByRole('button', { name: 'Aynı işlemi tekrar dene' }))
    await waitFor(() => expect(screen.getByText('Değişiklikler kaydedildi')).toBeTruthy())

    const keys = calls.filter((call) => call.method === 'PATCH').map((call) => call.idempotencyKey)
    expect(keys).toHaveLength(3)
    expect(new Set(keys).size).toBe(1)
  })
})

describe('an unknown edit across a REAL capability re-fence', () => {
  it('is found again under a NEW capability generation, and retries with the same key only when asked', async () => {
    /**
     * Same lifecycle as the create screen, in one tree: unknown → same-key retry
     * refused with 403 → route closed by the boundary → fresh entry answered with
     * a new generation. The edit is bound to THIS record, and its expectedVersion
     * is the one the person actually saw — the card is re-read, but the attempt is
     * never rebased.
     */
    const first = installFetch({
      capabilities: EDIT_ONLY,
      onEdit: (attempt) => (attempt === 1 ? json({}, 503) : json({ code: 'forbidden' }, 403)),
    })
    const { router } = renderProductApp(PATH)

    await formVisible()
    await changeNameAndReason()
    await userEvent.click(saveButton())
    await waitFor(() => expect(screen.getByTestId('product-edit-unknown')).toBeTruthy())
    await userEvent.click(screen.getByRole('button', { name: 'Aynı işlemi tekrar dene' }))
    await waitFor(() => expect(document.querySelector('[data-state]')).not.toBeNull())
    const firstKey = first.calls.find((call) => call.method === 'PATCH')?.idempotencyKey

    const { calls } = installFetch({
      capabilities: EDIT_ONLY,
      capabilityGeneration: 'gen-2',
      onEdit: () =>
        json(
          mutation({
            card: productCard({ product: productCore({ name: 'Galvaniz sac', version: '8' }) }),
            isReplay: true,
          }),
        ),
    })
    await act(async () => {
      await router.navigate(`/urun/${PRODUCT_1}`)
    })
    await act(async () => {
      await router.navigate(PATH)
    })
    await formVisible()

    await waitFor(() => expect(screen.getByTestId('product-edit-unknown')).toBeTruthy())
    expect(screen.getByText(PRODUCT_EDIT_TEXT.retryRefusedStillUnknown)).toBeTruthy()
    expect(calls.filter((call) => call.method === 'PATCH')).toHaveLength(0)

    await userEvent.click(screen.getByRole('button', { name: 'Aynı işlemi tekrar dene' }))
    await waitFor(() => expect(screen.getByText(PRODUCT_EDIT_TEXT.replayedNote)).toBeTruthy())
    const patches = calls.filter((call) => call.method === 'PATCH')
    expect(patches).toHaveLength(1)
    expect(patches[0].idempotencyKey).toBe(firstKey)
    // The snapshot's expectedVersion is still the one the person saw: '7'.
    expect(JSON.parse(patches[0].body ?? '{}').expectedVersion).toBe('7')
  })
})

describe('a canonical answer that lands after the screen is gone', () => {
  it('is kept and shown on the fresh return, with no write of its own', async () => {
    // The edit twin of the create case: PATCH in flight, a reference read answers
    // 403, the route closes, the 200 lands unseen.
    let answer: ((response: Response) => void) | null = null
    installFetch({
      capabilities: EDIT_ONLY,
      onEdit: () =>
        new Promise<Response>((resolve) => {
          answer = resolve
        }),
    })
    const { router } = renderProductApp(PATH)

    await formVisible()
    await changeNameAndReason()
    await userEvent.click(saveButton())
    await waitFor(() => expect(answer).not.toBeNull())

    installFetch({ capabilities: EDIT_ONLY, onOptions: () => json({ code: 'forbidden' }, 403) })
    act(() => {
      window.dispatchEvent(new Event('offline'))
      window.dispatchEvent(new Event('online'))
    })
    await waitFor(() => expect(document.querySelector('[data-state]')).not.toBeNull())
    expect(screen.queryByRole('button', { name: 'Değişiklikleri kaydet' })).toBeNull()

    await act(async () => {
      ;(answer as unknown as (response: Response) => void)(
        json(mutation({ card: productCard({ product: productCore({ name: 'Galvaniz sac', version: '8' }) }) })),
      )
    })

    const { calls } = installFetch({ capabilities: EDIT_ONLY, capabilityGeneration: 'gen-2' })
    await act(async () => {
      await router.navigate(`/urun/${PRODUCT_1}`)
    })
    await act(async () => {
      await router.navigate(PATH)
    })
    await waitFor(() => expect(screen.getByText('Değişiklikler kaydedildi')).toBeTruthy())
    expect(calls.filter((call) => call.method === 'PATCH')).toHaveLength(0)
  })
})

describe('roles', () => {
  it('sends a cleared role as the contract’s tagged not-evaluated value', async () => {
    // `ToManualState` accepts it, so "Belirtilmedi" is a state the user can assert.
    const { calls } = installFetch({ capabilities: EDIT_ONLY })
    renderProductApp(PATH)

    await formVisible()
    // `purchasable` is a manual true; move it back to unanswered.
    await userEvent.click(screen.getAllByRole('radio', { name: 'Belirtilmedi' })[0])
    await userEvent.type(screen.getByLabelText(/Değişiklik gerekçesi/), 'Rol geri alındı')
    await userEvent.click(saveButton())

    await waitFor(() => expect(calls.some((call) => call.method === 'PATCH')).toBe(true))
    const body = JSON.parse(calls.find((call) => call.method === 'PATCH')?.body ?? '{}')
    expect(body.changes.roles).toEqual({
      purchasable: { value: null, provenance: 'not-evaluated' },
    })
  })
})

describe('the shell dirty gate', () => {
  it('makes a company switch ASK while a change is pending', async () => {
    installFetch({ capabilities: EDIT_ONLY })
    renderProductApp(PATH)

    await formVisible()
    await changeNameAndReason()
    await userEvent.selectOptions(screen.getByLabelText('Şirket'), COMPANY_B)

    const dialog = await waitFor(() => screen.getByRole('dialog'))
    expect(within(dialog).getByRole('button', { name: 'Kal' })).toBeTruthy()
    expect((screen.getByLabelText(/Ürün adı/) as HTMLInputElement).value).toBe('Galvaniz sac')
  })
})

describe('success', () => {
  it('shows the SERVER’s values and version, not the submitted draft', async () => {
    installFetch({
      capabilities: EDIT_ONLY,
      onEdit: () =>
        json(
          mutation({
            card: productCard({
              product: productCore({ name: 'Galvaniz sac (sunucu)', version: '8' }),
            }),
          }),
        ),
    })
    renderProductApp(PATH)

    await formVisible()
    await changeNameAndReason()
    await userEvent.click(saveButton())

    await waitFor(() => expect(screen.getByText('Değişiklikler kaydedildi')).toBeTruthy())
    // "Gönderilen taslak server sonucu gibi boyanmaz."
    expect(screen.getByText(/Galvaniz sac \(sunucu\)/)).toBeTruthy()
    expect(screen.getByText('Güncel sürüm: 8')).toBeTruthy()
  })

  it('rejects an edit result about a different product', async () => {
    installFetch({
      capabilities: EDIT_ONLY,
      onEdit: () =>
        json(
          mutation({
            card: productCard({
              product: productCore({ id: '99999999-9999-9999-9999-999999999999' }),
            }),
          }),
        ),
    })
    renderProductApp(PATH)

    await formVisible()
    await changeNameAndReason()
    await userEvent.click(saveButton())

    await waitFor(() => expect(screen.getByTestId('product-edit-unknown')).toBeTruthy())
  })
})

describe('unsaved guard', () => {
  it('leaves on the FIRST "leave" — Cancel is not asked twice', async () => {
    // Cancel and the blocker used to guard the same exit, so the user answered the
    // same question twice.
    installFetch({ capabilities: [...EDIT_ONLY, PRODUCT_CAPABILITY.productsDetail] })
    renderProductApp(PATH)

    await formVisible()
    await changeNameAndReason()
    await userEvent.click(screen.getByRole('button', { name: 'Vazgeç' }))

    const first = await waitFor(() => screen.getByRole('alertdialog'))
    expect(screen.getAllByRole('alertdialog')).toHaveLength(1)
    await userEvent.click(within(first).getByRole('button', { name: 'Değişiklikleri bırak' }))

    await waitFor(() => expect(screen.queryByRole('alertdialog')).toBeNull())
    await waitFor(() => expect(screen.getByRole('heading', { name: 'Siyah sac' })).toBeTruthy())
  })

  it('asks before leaving with a real change, and Escape means stay', async () => {
    installFetch({ capabilities: [...EDIT_ONLY, PRODUCT_CAPABILITY.productsDetail] })
    renderProductApp(PATH)

    await formVisible()
    await changeNameAndReason()
    await userEvent.click(screen.getByRole('button', { name: 'Vazgeç' }))

    const dialog = await waitFor(() => screen.getByRole('alertdialog'))
    expect(document.activeElement).toBe(within(dialog).getByRole('button', { name: 'Sayfada kal' }))
    await userEvent.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('alertdialog')).toBeNull())
    expect((screen.getByLabelText(/Ürün adı/) as HTMLInputElement).value).toBe('Galvaniz sac')
  })

  it('does not ask when only a reason was typed', async () => {
    // Typing a justification and then changing nothing leaves nothing to save, and
    // prompting about it would train people to dismiss the guard.
    installFetch({ capabilities: [...EDIT_ONLY, PRODUCT_CAPABILITY.productsDetail] })
    renderProductApp(PATH)

    await formVisible()
    await userEvent.type(screen.getByLabelText(/Değişiklik gerekçesi/), 'not')
    await userEvent.click(screen.getByRole('button', { name: 'Vazgeç' }))

    expect(screen.queryByRole('alertdialog')).toBeNull()
  })
})

describe('scope', () => {
  it('offers no group or code control — the contract freezes both', async () => {
    installFetch({ capabilities: EDIT_ONLY })
    renderProductApp(PATH)

    await formVisible()
    expect(screen.queryByLabelText(/Kod grubu/)).toBeNull()
    expect(screen.queryByLabelText(/Ürün kodu/)).toBeNull()
    expect(screen.getByText(/kod grubu ve ürün kodu değişmez/)).toBeTruthy()
  })

  it('offers class and family, scoped to the record’s own group', async () => {
    const { calls } = installFetch({ capabilities: EDIT_ONLY })
    renderProductApp(PATH)

    await formVisible()
    await waitFor(() =>
      expect(calls.some((call) => call.url.includes(`reference-options?groupId=${GROUP_SAC}`))).toBe(
        true,
      ),
    )
    expect(screen.getByLabelText(/Ürün sınıfı/)).toBeTruthy()
  })

  it('sends an explicit null to CLEAR a reference', async () => {
    /**
     * An explicit `null` clears a nullable scalar reference — a distinct operation
     * from leaving the member out, which means "unchanged".
     */
    const { calls } = installFetch({
      capabilities: EDIT_ONLY,
      onCard: () => json(productCard({ product: productCore({ classId: CLASS_FLAT }) })),
    })
    renderProductApp(PATH)

    await formVisible()
    await waitFor(() => screen.getByRole('option', { name: 'Yassı ürün' }))
    await userEvent.selectOptions(screen.getByLabelText(/Ürün sınıfı/), '')
    await userEvent.type(screen.getByLabelText(/Değişiklik gerekçesi/), 'Sınıf bağı kaldırıldı')
    await userEvent.click(saveButton())

    await waitFor(() => expect(calls.some((call) => call.method === 'PATCH')).toBe(true))
    const body = JSON.parse(calls.find((call) => call.method === 'PATCH')?.body ?? '{}')
    expect(body.changes.classId).toBeNull()
    expect(Object.hasOwn(body.changes, 'name')).toBe(false)
  })

  it('keeps the record’s own class selectable when the catalogue no longer offers it', async () => {
    // Otherwise merely opening the form would silently drop a reference the user
    // never touched.
    installFetch({
      capabilities: EDIT_ONLY,
      onCard: () =>
        json(
          productCard({
            product: productCore({
              classId: CLASS_FLAT,
              labels: { group: 'Sac', class: 'Eski sınıf', family: null, primaryStockUnit: 'kg' },
            }),
          }),
        ),
      onOptions: () => json(referenceOptions({ classes: [] })),
    })
    renderProductApp(PATH)

    await formVisible()
    await waitFor(() =>
      expect((screen.getByLabelText(/Ürün sınıfı/) as HTMLSelectElement).value).toBe(CLASS_FLAT),
    )
    expect(screen.getByRole('option', { name: 'Eski sınıf' })).toBeTruthy()
  })

  it('states the global impact before the fields', async () => {
    installFetch({ capabilities: EDIT_ONLY })
    renderProductApp(PATH)

    await formVisible()
    expect(screen.getByText(/her iki şirkette de görünür/)).toBeTruthy()
  })
})
