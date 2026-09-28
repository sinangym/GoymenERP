import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PRODUCT_ACTIVATE_CAPABILITY, PRODUCT_CAPABILITY } from './capabilities'
import { decodeDuplicateCandidates } from './contract'
import { resetPendingAttempts } from './pendingAttempt'
import {
  COMPANY_A,
  GROUP_SAC,
  PRODUCT_1,
  PRODUCT_2,
  duplicateCandidates,
  installFetch,
  json,
  mutation,
  productCard,
  productCore,
  renderProductApp,
  type RecordedCall,
} from './testHarness'

/*
 * Spec §10.2/§15: the server's exact-name duplicate preview. A name match WARNS with links to the other
 * cards and never blocks a save or an approval; the approval re-reads the warning before it sends.
 */

const DETAIL = `/urun/${PRODUCT_1}`
const EDIT = `/urun/${PRODUCT_1}/duzenle`
const PRODUCT_3 = '33333333-3333-3333-3333-333333333333'
const WITH_ACTIVATION = [PRODUCT_CAPABILITY.productsDetail, PRODUCT_ACTIVATE_CAPABILITY]

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

const cardVisible = () => waitFor(() => screen.getByRole('heading', { name: 'Siyah sac' }))
const duplicateReads = (calls: readonly RecordedCall[]) =>
  calls.filter((call) => call.method === 'GET' && call.url.includes('/products/duplicate-candidates'))
const paramsOf = (call: RecordedCall) => new URL(call.url, 'http://localhost').searchParams

const other = (id: string, code: string, lifecycle = 'Active') =>
  productCore({ id, code, name: 'SİYAH SAC', lifecycle, version: '3' })

describe('decoder', () => {
  it('accepts only the exact-name rule and one row per card', () => {
    expect(decodeDuplicateCandidates(duplicateCandidates())).not.toBeNull()
    expect(decodeDuplicateCandidates(duplicateCandidates({ rule: 'similarName' }))).toBeNull()
    expect(decodeDuplicateCandidates(duplicateCandidates({ hasMore: 'no' }))).toBeNull()
    expect(decodeDuplicateCandidates(duplicateCandidates({ name: '' }))).toBeNull()
    const row = other(PRODUCT_2, 'SAC-7')
    expect(decodeDuplicateCandidates(duplicateCandidates({ exactName: [row, row] }))).toBeNull()
  })
})

describe('Draft card', () => {
  it('lists the other cards with the same name, with links, and reads them again on every open', async () => {
    const { calls } = installFetch({
      capabilities: [PRODUCT_CAPABILITY.productsDetail],
      onDuplicates: () =>
        json(duplicateCandidates({ name: 'Siyah sac', exactName: [other(PRODUCT_2, 'SAC-7'), other(PRODUCT_3, 'SAC-9', 'Draft')] })),
    })
    const first = renderProductApp(DETAIL)
    await cardVisible()

    const list = await screen.findByTestId('product-duplicate-candidates')
    expect(within(list).getByRole('link', { name: 'SAC-7' }).getAttribute('href')).toBe(`/urun/${PRODUCT_2}`)
    expect(within(list).getByRole('link', { name: 'SAC-9' }).getAttribute('href')).toBe(`/urun/${PRODUCT_3}`)
    expect(list.textContent).toContain('Aktif')
    expect(list.textContent).toContain('Taslak')
    expect(screen.getByText('Bu adla kayıtlı başka ürün kartları var')).toBeTruthy()
    expect(screen.getByText(/engellemez/)).toBeTruthy()

    const [read] = duplicateReads(calls)
    expect(paramsOf(read).get('name')).toBe('Siyah sac')
    expect(paramsOf(read).get('excludeProductId')).toBe(PRODUCT_1)
    expect(read.companyId).toBe(COMPANY_A)

    // Close and reopen: the server is asked again, never a remembered answer.
    first.unmount()
    cleanup()
    renderProductApp(DETAIL)
    await cardVisible()
    await screen.findByTestId('product-duplicate-candidates')
    expect(duplicateReads(calls).length).toBe(2)
  })

  it('says plainly when no other card has the name, and when the check could not be made', async () => {
    installFetch({ capabilities: [PRODUCT_CAPABILITY.productsDetail] })
    const first = renderProductApp(DETAIL)
    await cardVisible()
    await waitFor(() => expect(screen.getByText('Sunucu kontrolünde bu adla başka ürün kartı yok.')).toBeTruthy())
    first.unmount()
    cleanup()

    installFetch({
      capabilities: [PRODUCT_CAPABILITY.productsDetail],
      onDuplicates: () => json(duplicateCandidates({ rule: 'fuzzy' })),
    })
    renderProductApp(DETAIL)
    await cardVisible()
    await waitFor(() => expect(screen.getByText(/Aynı ad kontrolü yapılamadı/)).toBeTruthy())
  })

  it('asks nothing for a card past approval', async () => {
    const { calls } = installFetch({
      capabilities: [PRODUCT_CAPABILITY.productsDetail],
      onCard: () => json(productCard({ product: productCore({ lifecycle: 'Active' }) })),
    })
    renderProductApp(DETAIL)
    await cardVisible()
    expect(screen.queryByTestId('product-duplicate-check')).toBeNull()
    expect(duplicateReads(calls)).toEqual([])
  })
})

describe('approval re-check', () => {
  it('does not send when the list changed since it was shown; the second press approves once', async () => {
    let reads = 0
    const { calls } = installFetch({
      capabilities: WITH_ACTIVATION,
      onDuplicates: () => {
        reads += 1
        // Another Draft with the same name appears between the open and the press.
        return json(duplicateCandidates({ exactName: reads === 1 ? [] : [other(PRODUCT_2, 'SAC-7', 'Draft')] }))
      },
      onActivate: () => json(mutation({ card: productCard({ product: productCore({ lifecycle: 'Active', version: '8' }) }) })),
    })
    renderProductApp(DETAIL)
    await cardVisible()
    await waitFor(() => expect(screen.getByText('Sunucu kontrolünde bu adla başka ürün kartı yok.')).toBeTruthy())

    await userEvent.click(screen.getByRole('button', { name: 'Ürünü aktifleştir' }))
    await waitFor(() => expect(screen.getByTestId('product-duplicates-changed')).toBeTruthy())
    expect(calls.some((call) => call.url.endsWith('/activation'))).toBe(false)
    const list = await screen.findByTestId('product-duplicate-candidates')
    expect(within(list).getByRole('link', { name: 'SAC-7' })).toBeTruthy()

    // The person has now seen the list the server holds; the warning does not block the approval.
    await userEvent.click(screen.getByRole('button', { name: 'Ürünü aktifleştir' }))
    await waitFor(() => expect(screen.getByText('Ürün aktifleştirildi.')).toBeTruthy())
    expect(calls.filter((call) => call.url.endsWith('/activation')).length).toBe(1)
  })

  it('approves on the first press when the list is as shown, even with other cards on it', async () => {
    const { calls } = installFetch({
      capabilities: WITH_ACTIVATION,
      onDuplicates: () => json(duplicateCandidates({ exactName: [other(PRODUCT_2, 'SAC-7')] })),
    })
    renderProductApp(DETAIL)
    await cardVisible()
    await screen.findByTestId('product-duplicate-candidates')

    await userEvent.click(screen.getByRole('button', { name: 'Ürünü aktifleştir' }))
    await waitFor(() => expect(screen.getByText('Ürün aktifleştirildi.')).toBeTruthy())
    expect(calls.filter((call) => call.url.endsWith('/activation')).length).toBe(1)
    expect(duplicateReads(calls).length).toBeGreaterThanOrEqual(2)
    expect(screen.queryByTestId('product-duplicates-changed')).toBeNull()
  })

  it('a failed re-check blocks nothing', async () => {
    let reads = 0
    const { calls } = installFetch({
      capabilities: WITH_ACTIVATION,
      onDuplicates: () => {
        reads += 1
        return reads === 1 ? json(duplicateCandidates()) : json({ code: 'internal_error' }, 500)
      },
    })
    renderProductApp(DETAIL)
    await cardVisible()
    await waitFor(() => expect(screen.getByText('Sunucu kontrolünde bu adla başka ürün kartı yok.')).toBeTruthy())

    await userEvent.click(screen.getByRole('button', { name: 'Ürünü aktifleştir' }))
    await waitFor(() => expect(screen.getByText('Ürün aktifleştirildi.')).toBeTruthy())
    expect(calls.filter((call) => call.url.endsWith('/activation')).length).toBe(1)
  })
})

describe('edit', () => {
  it('warns for the stored name on open and for a new name once the field is left, never blocking the save', async () => {
    const { calls } = installFetch({
      capabilities: [PRODUCT_CAPABILITY.productsEdit, PRODUCT_CAPABILITY.productsDetail],
      onDuplicates: (url) =>
        json(
          new URL(url, 'http://localhost').searchParams.get('name') === 'Galvaniz sac'
            ? duplicateCandidates({ name: 'Galvaniz sac', exactName: [productCore({ id: PRODUCT_2, code: 'SAC-7', name: 'galvaniz sac' })] })
            : duplicateCandidates({ name: 'Siyah sac' }),
        ),
    })
    renderProductApp(EDIT)
    await waitFor(() => screen.getByRole('heading', { name: 'Ürünü düzenle' }))
    await waitFor(() => expect(screen.getByText('Sunucu kontrolünde bu adla başka ürün kartı yok.')).toBeTruthy())
    expect(paramsOf(duplicateReads(calls)[0]).get('excludeProductId')).toBe(PRODUCT_1)

    const name = screen.getByLabelText(/Ürün adı/)
    await userEvent.clear(name)
    await userEvent.type(name, '  Galvaniz sac ')
    // Typing asks nothing and hides the answer for the old name.
    expect(screen.queryByTestId('product-duplicate-check')).toBeNull()
    await userEvent.tab()
    const list = await screen.findByTestId('product-duplicate-candidates')
    expect(within(list).getByRole('link', { name: 'SAC-7' }).getAttribute('href')).toBe(`/urun/${PRODUCT_2}`)
    expect(paramsOf(duplicateReads(calls).at(-1)!).get('name')).toBe('Galvaniz sac')

    await userEvent.type(screen.getByLabelText(/Değişiklik gerekçesi/), 'Ad düzeltildi')
    await userEvent.click(screen.getByRole('button', { name: 'Değişiklikleri kaydet' }))
    await waitFor(() => expect(calls.some((call) => call.method === 'PATCH')).toBe(true))
  })
})

describe('create', () => {
  it('shows the other same-named cards right after the Draft is saved', async () => {
    const { calls } = installFetch({
      capabilities: [PRODUCT_CAPABILITY.productsCreate, PRODUCT_CAPABILITY.productsList, PRODUCT_CAPABILITY.productsDetail],
      onDuplicates: () => json(duplicateCandidates({ name: 'Siyah sac', exactName: [other(PRODUCT_2, 'SAC-7')] })),
      onCreate: () => json(mutation(), 201),
    })
    renderProductApp('/urun/yeni')
    await waitFor(() => screen.getByRole('heading', { name: 'Yeni ürün' }))
    await userEvent.type(screen.getByLabelText(/Ürün adı/), 'Siyah sac')
    await waitFor(() => expect(screen.getByRole('option', { name: /Sac \(SAC\)/ })).toBeTruthy())
    await userEvent.selectOptions(screen.getByLabelText(/Kod grubu/), GROUP_SAC)
    await userEvent.click(screen.getByRole('button', { name: 'Taslak olarak kaydet' }))

    await waitFor(() => expect(screen.getByText('Taslak kaydedildi')).toBeTruthy())
    const list = await screen.findByTestId('product-duplicate-candidates')
    expect(within(list).getByRole('link', { name: 'SAC-7' }).getAttribute('href')).toBe(`/urun/${PRODUCT_2}`)
    expect(paramsOf(duplicateReads(calls)[0]).get('excludeProductId')).toBe(PRODUCT_1)
  })
})
