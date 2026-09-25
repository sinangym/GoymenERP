import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PRODUCT_CAPABILITY } from './capabilities'
import { resetPendingAttempts } from './pendingAttempt'
import {
  COMPANY_A,
  COMPANY_B,
  GROUP_SAC,
  PRODUCT_1,
  PRODUCT_2,
  installFetch,
  json,
  mutation,
  productCore,
  productPage,
  renderProductApp,
  type RecordedCall,
} from './testHarness'

/* U-02 §10.2 / CASE-PRD-U02-014: a name match warns and lets the person compare; it never blocks. */

const CREATE_AND_LIST = [PRODUCT_CAPABILITY.productsCreate, PRODUCT_CAPABILITY.productsList]
const PATH = '/urun/yeni'

afterEach(() => {
  cleanup()
  resetPendingAttempts()
  try {
    window.localStorage.clear()
  } catch {
    // Storage unavailable in this environment: nothing was remembered.
  }
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const formVisible = () => waitFor(() => screen.getByRole('heading', { name: 'Yeni ürün' }))
const nameInput = () => screen.getByLabelText(/Ürün adı/) as HTMLInputElement
const nameSearches = (calls: readonly RecordedCall[]) =>
  calls.filter((call) => call.method === 'GET' && /\/products\?/.test(call.url) && call.url.includes('q='))
const qOf = (call: RecordedCall) => new URL(call.url, 'http://localhost').searchParams.get('q')

async function typeNameAndLeave(name: string) {
  await userEvent.type(nameInput(), name)
  await userEvent.tab()
}

function deferred() {
  let resolve!: (response: Response) => void
  const promise = new Promise<Response>((done) => (resolve = done))
  return { promise, resolve }
}

type Row = ReturnType<typeof productCore>
const fold = (text: string) => text.toLocaleLowerCase('tr-TR')

/**
 * Answers the list search the way `ProductReadStore.BuildQuery` does, so a test cannot
 * pin an answer the real endpoint never gives: `q` is only trimmed
 * (`ProductQueryRules.Normalize`), and a row matches when its name or code CONTAINS it
 * case-insensitively (`Turkish_100_CI_AS`). Inner whitespace is collapsed on neither
 * side — stored names are only trimmed (`Product.NormalizeName`). Registered aliases are
 * not modelled; a code hit stands in for them.
 */
const serverSearch = (rows: readonly Row[]) => (url: string) => {
  const q = fold((new URL(url, 'http://localhost').searchParams.get('q') ?? '').trim())
  return json(productPage({ items: rows.filter((row) => fold(row.name).includes(q) || fold(row.code).includes(q)) }))
}

describe('name match warning', () => {
  it('searches once when the name field is left, not per keystroke, under the committed origin', async () => {
    const { calls } = installFetch({ capabilities: CREATE_AND_LIST })
    renderProductApp(PATH)
    await formVisible()

    await userEvent.type(nameInput(), 'Siyah sac')
    expect(nameSearches(calls)).toHaveLength(0)
    await userEvent.tab()

    await waitFor(() => expect(nameSearches(calls)).toHaveLength(1))
    const search = new URL(nameSearches(calls)[0].url, 'http://localhost')
    expect(search.searchParams.get('q')).toBe('Siyah sac')
    expect(search.searchParams.get('sort')).toBe('name')
    expect(nameSearches(calls)[0].companyId).toBe(COMPANY_A)
  })

  it('lists the server matches, labels an exact Turkish-folded name, and says it does not block', async () => {
    installFetch({
      capabilities: CREATE_AND_LIST,
      onList: serverSearch([
        productCore({ name: 'SİYAH SAC' }),
        productCore({ id: PRODUCT_2, code: 'SAC-43', name: 'Siyah sac rulo' }),
      ]),
    })
    renderProductApp(PATH)
    await formVisible()
    await typeNameAndLeave('siyah sac')

    const list = await waitFor(() => screen.getByTestId('product-name-matches'))
    const items = within(list).getAllByRole('listitem')
    expect(items[0].textContent).toBe('SAC-42 SİYAH SAC (aynı ad)')
    expect(items[1].textContent).toBe('SAC-43 Siyah sac rulo')
    expect(screen.getByText(/Kaydı engellemez/)).toBeTruthy()
    // Without the detail capability the code is plain text, never a link into a refusal.
    expect(within(list).queryAllByRole('link')).toHaveLength(0)
  })

  it('searches a doubled inner space as typed; only the identically spaced name is "aynı ad"', async () => {
    const { calls } = installFetch({
      capabilities: CREATE_AND_LIST,
      onList: serverSearch([
        productCore({ name: 'Siyah sac' }),
        productCore({ id: PRODUCT_2, code: 'SAC-43', name: 'SİYAH  SAC' }),
      ]),
    })
    renderProductApp(PATH)
    await formVisible()
    await typeNameAndLeave('siyah  sac')

    await waitFor(() => expect(nameSearches(calls)).toHaveLength(1))
    expect(qOf(nameSearches(calls)[0])).toBe('siyah  sac')
    // The server does not return the single-spaced name for a double-spaced term, so the
    // screen does not claim it either.
    const list = await waitFor(() => screen.getByTestId('product-name-matches'))
    expect(within(list).getAllByRole('listitem').map((item) => item.textContent)).toEqual([
      'SAC-43 SİYAH  SAC (aynı ad)',
    ])
  })

  it('titles a code-only hit as a name OR code match, never as "this name"', async () => {
    installFetch({
      capabilities: CREATE_AND_LIST,
      onList: serverSearch([productCore({ id: PRODUCT_2, code: 'SAC-99', name: 'Galvaniz rulo' })]),
    })
    renderProductApp(PATH)
    await formVisible()
    await typeNameAndLeave('Sac')

    const list = await waitFor(() => screen.getByTestId('product-name-matches'))
    expect(list.textContent).toBe('SAC-99 Galvaniz rulo')
    const panel = screen.getByTestId('product-name-match-check')
    expect(within(panel).getByText('Adında veya kodunda bu ifade geçen ürünler var')).toBeTruthy()
    expect(screen.queryByText('Bu adı içeren ürünler var')).toBeNull()
  })

  it('labels a 201–250 character identical name although only 200 characters are searched', async () => {
    const long = `Siyah sac ${'x'.repeat(230)}`
    expect(long.length).toBe(240)
    const { calls } = installFetch({
      capabilities: CREATE_AND_LIST,
      onList: serverSearch([productCore({ name: long })]),
    })
    renderProductApp(PATH)
    await formVisible()
    fireEvent.change(nameInput(), { target: { value: long } })
    await userEvent.click(nameInput())
    await userEvent.tab()

    const list = await waitFor(() => screen.getByTestId('product-name-matches'))
    expect(qOf(nameSearches(calls)[0])).toHaveLength(200)
    expect(within(list).getByRole('listitem').textContent).toBe(`SAC-42 ${long} (aynı ad)`)
  })

  it('hides the panel as soon as the name is no longer the one that was checked', async () => {
    installFetch({ capabilities: CREATE_AND_LIST, onList: serverSearch([productCore({ name: 'Siyah sac' })]) })
    renderProductApp(PATH)
    await formVisible()
    await typeNameAndLeave('Siyah sac')
    await waitFor(() => screen.getByTestId('product-name-matches'))

    await userEvent.click(nameInput())
    await userEvent.clear(nameInput())
    await userEvent.type(nameInput(), 'Galvaniz rulo')

    // Still in the field: nothing has been checked for this name, so nothing is claimed.
    expect(screen.queryByTestId('product-name-match-check')).toBeNull()
  })

  it('an Enter-submit checks the submitted name; the old name’s matches never stand beside it', async () => {
    let panelAtPost: string | null = 'not-captured'
    const { calls } = installFetch({
      capabilities: CREATE_AND_LIST,
      onList: serverSearch([
        productCore({ name: 'OLD Siyah sac' }),
        productCore({ id: PRODUCT_2, code: 'GLV-77', name: 'FRESH Galvaniz rulo' }),
      ]),
      onCreate: () => {
        panelAtPost = screen.queryByTestId('product-name-match-check')?.textContent ?? null
        return json({ code: 'service_unavailable' }, 503)
      },
    })
    renderProductApp(PATH)
    await formVisible()
    await typeNameAndLeave('Siyah sac')
    await waitFor(() => screen.getByTestId('product-name-matches'))
    await userEvent.selectOptions(screen.getByLabelText(/Kod grubu/), GROUP_SAC)

    await userEvent.click(nameInput())
    await userEvent.clear(nameInput())
    await userEvent.type(nameInput(), 'Galvaniz rulo{Enter}')

    await waitFor(() => expect(calls.some((call) => call.method === 'POST')).toBe(true))
    expect(panelAtPost ?? '').not.toContain('OLD')
    await waitFor(() => screen.getByTestId('product-create-unknown'))
    await waitFor(() =>
      expect(screen.getByTestId('product-name-matches').textContent).toBe('GLV-77 FRESH Galvaniz rulo'),
    )
    expect(nameSearches(calls).map(qOf)).toContain('Galvaniz rulo')
    expect(screen.queryByText(/OLD Siyah sac/)).toBeNull()
  })

  it('keeps one live region in place and ties a shown result to the name field', async () => {
    installFetch({ capabilities: CREATE_AND_LIST, onList: serverSearch([productCore({ name: 'Siyah sac' })]) })
    renderProductApp(PATH)
    await formVisible()

    // Present BEFORE any result, so the result arrives as an update a screen reader announces.
    const live = screen.getByTestId('product-name-match-live')
    expect(live.getAttribute('role')).toBe('status')
    expect(live.getAttribute('aria-live')).toBe('polite')
    expect(nameInput().getAttribute('aria-describedby') ?? '').not.toContain('product-name-match-check')

    await typeNameAndLeave('Siyah sac')
    const panel = await waitFor(() => within(live).getByTestId('product-name-match-check'))
    await waitFor(() => screen.getByTestId('product-name-matches'))
    expect((nameInput().getAttribute('aria-describedby') ?? '').split(' ')).toContain(panel.id)
  })

  it('links a match to its card only with the detail capability', async () => {
    installFetch({ capabilities: [...CREATE_AND_LIST, PRODUCT_CAPABILITY.productsDetail] })
    renderProductApp(PATH)
    await formVisible()
    await typeNameAndLeave('Siyah sac')

    const list = await waitFor(() => screen.getByTestId('product-name-matches'))
    expect(within(list).getByRole('link', { name: 'SAC-42' }).getAttribute('href')).toContain(`/urun/${PRODUCT_1}`)
  })

  it('never blocks the save: a matching name still creates a Draft', async () => {
    const { calls } = installFetch({
      capabilities: CREATE_AND_LIST,
      onCreate: () => json(mutation(), 201),
    })
    renderProductApp(PATH)
    await formVisible()
    await typeNameAndLeave('Siyah sac')
    await waitFor(() => screen.getByTestId('product-name-matches'))
    await userEvent.selectOptions(screen.getByLabelText(/Kod grubu/), GROUP_SAC)
    await userEvent.click(screen.getByRole('button', { name: 'Taslak olarak kaydet' }))

    await waitFor(() => expect(calls.some((call) => call.method === 'POST')).toBe(true))
  })

  it('says when there are more matches than it shows', async () => {
    installFetch({ capabilities: CREATE_AND_LIST, onList: () => json(productPage({ nextCursor: 'opaque' })) })
    renderProductApp(PATH)
    await formVisible()
    await typeNameAndLeave('Siyah sac')

    await waitFor(() => expect(screen.getByText(/Daha fazla eşleşme var/)).toBeTruthy())
  })

  it('reports no match neutrally, without claiming the name is unique', async () => {
    installFetch({ capabilities: CREATE_AND_LIST, onList: () => json(productPage({ items: [] })) })
    renderProductApp(PATH)
    await formVisible()
    await typeNameAndLeave('Galvaniz')

    await waitFor(() => expect(screen.getByText('Sunucu aramasında bu adı içeren ürün yok.')).toBeTruthy())
  })

  it('a failed check says so and does not block', async () => {
    installFetch({ capabilities: CREATE_AND_LIST, onList: () => json({ code: 'service_unavailable' }, 503) })
    renderProductApp(PATH)
    await formVisible()
    await typeNameAndLeave('Siyah sac')

    await waitFor(() => expect(screen.getByText(/Benzer ad kontrolü yapılamadı/)).toBeTruthy())
    expect((screen.getByRole('button', { name: 'Taslak olarak kaydet' }) as HTMLButtonElement).disabled).toBe(false)
  })

  it('shows only the latest checked name’s answer when an earlier one lands later', async () => {
    const first = deferred()
    installFetch({
      capabilities: CREATE_AND_LIST,
      onList: (url) =>
        new URL(url, 'http://localhost').searchParams.get('q') === 'Siyah'
          ? first.promise
          : json(productPage({ items: [productCore({ id: PRODUCT_2, code: 'SAC-43', name: 'LATEST Siyah sac' })] })),
    })
    renderProductApp(PATH)
    await formVisible()
    await typeNameAndLeave('Siyah')
    await userEvent.click(nameInput())
    await userEvent.type(nameInput(), ' sac')
    await userEvent.tab()
    await waitFor(() => expect(screen.getByTestId('product-name-matches').textContent).toContain('LATEST'))

    await act(async () => {
      first.resolve(json(productPage({ items: [productCore({ name: 'STALE Siyah' })] })))
    })
    await new Promise((settle) => setTimeout(settle, 50))
    expect(screen.getByTestId('product-name-matches').textContent).toContain('LATEST')
    expect(screen.queryByText(/STALE/)).toBeNull()
  })

  it('a check still in flight under company A never paints after switching to B', async () => {
    const pending = deferred()
    const { calls } = installFetch({ capabilities: CREATE_AND_LIST, onList: () => pending.promise })
    renderProductApp(PATH)
    await formVisible()
    await typeNameAndLeave('Siyah sac')
    await waitFor(() => expect(nameSearches(calls)).toHaveLength(1))
    expect(nameSearches(calls)[0].companyId).toBe(COMPANY_A)

    await userEvent.selectOptions(screen.getByLabelText('Şirket'), COMPANY_B)
    const dialog = await waitFor(() => screen.getByRole('dialog'))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Değişiklikleri bırak' }))
    await waitFor(() => {
      const picker = screen.getByLabelText('Şirket') as HTMLSelectElement
      expect(picker.disabled).toBe(false)
      expect(picker.value).toBe(COMPANY_B)
    })

    await act(async () => {
      pending.resolve(json(productPage({ items: [productCore({ name: 'LATE-A Siyah sac' })] })))
    })
    await new Promise((settle) => setTimeout(settle, 50))
    expect(screen.queryByText(/LATE-A/)).toBeNull()
    expect(screen.queryByTestId('product-name-match-check')).toBeNull()
    expect(nameSearches(calls).filter((call) => call.companyId === COMPANY_B)).toHaveLength(0)
  })

  it('an answer already in hand for company A is not shown while the same name is checked under B', async () => {
    // The switch remounts the form, but the query cache outlives it. Only the company
    // context in the check's key keeps A's finished answer for "Siyah sac" from being read
    // back as B's answer for the same name while B's own read is still out.
    const heldForB = deferred()
    let nameReads = 0
    const { calls } = installFetch({
      capabilities: CREATE_AND_LIST,
      onList: (url) => {
        if (!url.includes('q=')) return json(productPage())
        nameReads += 1
        return nameReads === 1
          ? json(productPage({ items: [productCore({ name: 'A-ONLY Siyah sac' })] }))
          : heldForB.promise
      },
    })
    renderProductApp(PATH)
    await formVisible()
    await typeNameAndLeave('Siyah sac')
    await waitFor(() => expect(screen.getByTestId('product-name-matches').textContent).toContain('A-ONLY'))

    await userEvent.selectOptions(screen.getByLabelText('Şirket'), COMPANY_B)
    const dialog = await waitFor(() => screen.getByRole('dialog'))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Değişiklikleri bırak' }))
    await waitFor(() => {
      const picker = screen.getByLabelText('Şirket') as HTMLSelectElement
      expect(picker.disabled).toBe(false)
      expect(picker.value).toBe(COMPANY_B)
    })
    await formVisible()
    await typeNameAndLeave('Siyah sac')

    // B asked for itself, and that answer has not arrived: nothing from A may stand in for it.
    await waitFor(() => expect(nameSearches(calls).map((call) => call.companyId)).toEqual([COMPANY_A, COMPANY_B]))
    await new Promise((settle) => setTimeout(settle, 50))
    expect(screen.queryByText(/A-ONLY/)).toBeNull()
    expect(screen.queryByTestId('product-name-matches')).toBeNull()
  })

  it('a 403 on the check goes through the capability boundary like every other read', async () => {
    const { calls } = installFetch({ capabilities: CREATE_AND_LIST, onList: () => json({ code: 'forbidden' }, 403) })
    renderProductApp(PATH)
    await formVisible()
    await typeNameAndLeave('Siyah sac')

    await waitFor(() => expect(nameSearches(calls)).toHaveLength(1))
    // Fail-closed: the refusal re-fences capabilities; the form does not stay open behind a
    // local "check failed" note as if the refusal were an outage.
    await waitFor(() => expect(screen.queryByRole('heading', { name: 'Yeni ürün' })).toBeNull())
    expect(screen.queryByText(/Benzer ad kontrolü yapılamadı/)).toBeNull()
  })

  it('does not search without the list capability, or for a one-character name', async () => {
    const createOnly = installFetch({ capabilities: [PRODUCT_CAPABILITY.productsCreate] })
    const first = renderProductApp(PATH)
    await formVisible()
    await typeNameAndLeave('Siyah sac')
    expect(nameSearches(createOnly.calls)).toHaveLength(0)
    expect(screen.queryByTestId('product-name-match-check')).toBeNull()
    expect(screen.queryByTestId('product-name-match-live')).toBeNull()
    first.unmount()
    cleanup()

    const withList = installFetch({ capabilities: CREATE_AND_LIST })
    renderProductApp(PATH)
    await formVisible()
    await typeNameAndLeave('S')
    expect(nameSearches(withList.calls)).toHaveLength(0)
  })
})
