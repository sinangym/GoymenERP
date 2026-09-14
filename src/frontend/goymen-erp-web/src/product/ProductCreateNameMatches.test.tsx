import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PRODUCT_CAPABILITY } from './capabilities'
import { resetPendingAttempts } from './pendingAttempt'
import {
  COMPANY_A,
  GROUP_SAC,
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
const nameInput = () => screen.getByLabelText(/Ürün adı/)
const nameSearches = (calls: readonly RecordedCall[]) =>
  calls.filter((call) => call.method === 'GET' && /\/products\?/.test(call.url) && call.url.includes('q='))

async function typeNameAndLeave(name: string) {
  await userEvent.type(nameInput(), name)
  await userEvent.tab()
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
      onList: () =>
        json(
          productPage({
            items: [
              productCore({ name: 'SİYAH SAC' }),
              productCore({ id: PRODUCT_2, code: 'SAC-43', name: 'Siyah sac rulo' }),
            ],
          }),
        ),
    })
    renderProductApp(PATH)
    await formVisible()
    await typeNameAndLeave('siyah  sac')

    const list = await waitFor(() => screen.getByTestId('product-name-matches'))
    const items = within(list).getAllByRole('listitem')
    expect(items[0].textContent).toBe('SAC-42 SİYAH SAC (aynı ad)')
    expect(items[1].textContent).toBe('SAC-43 Siyah sac rulo')
    expect(screen.getByText(/Kaydı engellemez/)).toBeTruthy()
    // Without the detail capability the code is plain text, never a link into a refusal.
    expect(within(list).queryAllByRole('link')).toHaveLength(0)
  })

  it('links a match to its card only with the detail capability', async () => {
    installFetch({ capabilities: [...CREATE_AND_LIST, PRODUCT_CAPABILITY.productsDetail] })
    renderProductApp(PATH)
    await formVisible()
    await typeNameAndLeave('Siyah sac')

    const list = await waitFor(() => screen.getByTestId('product-name-matches'))
    expect(within(list).getByRole('link', { name: 'SAC-42' }).getAttribute('href')).toContain('/urun/')
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

  it('does not search without the list capability, or for a one-character name', async () => {
    const createOnly = installFetch({ capabilities: [PRODUCT_CAPABILITY.productsCreate] })
    const first = renderProductApp(PATH)
    await formVisible()
    await typeNameAndLeave('Siyah sac')
    expect(nameSearches(createOnly.calls)).toHaveLength(0)
    expect(screen.queryByTestId('product-name-match-check')).toBeNull()
    first.unmount()
    cleanup()

    const withList = installFetch({ capabilities: CREATE_AND_LIST })
    renderProductApp(PATH)
    await formVisible()
    await typeNameAndLeave('S')
    expect(nameSearches(withList.calls)).toHaveLength(0)
  })
})
