import { afterEach, expect, it, vi } from 'vitest'
import { act, cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PRODUCT_CAPABILITY } from './capabilities'
import { resetPendingAttempts } from './pendingAttempt'
import {
  COMPANY_A,
  COMPANY_B,
  GROUP_SAC,
  PRODUCT_1,
  installFetch,
  json,
  renderProductApp,
} from './testHarness'

const HASH_A = 'A'.repeat(64)
const HASH_B = 'B'.repeat(64)
const PATH = '/urun/ice-aktar'
const CREATE_AND_LIST = [PRODUCT_CAPABILITY.productsCreate, PRODUCT_CAPABILITY.productsList]

const preview = () => ({
  fileSha256: HASH_A,
  previewToken: HASH_B,
  sourceRowCount: 1,
  rows: [{ rowNumber: 2, legacyCode: 'OLD-1', name: 'Galvaniz Rulo', groupId: GROUP_SAC }],
  errors: [],
  ready: true,
})

const imported = (isReplay = false) => ({
  products: [{ productId: PRODUCT_1, code: 'SAC-1', legacyCode: 'OLD-1', name: 'Galvaniz Rulo', groupId: GROUP_SAC }],
  isReplay,
  originalCorrelationId: 'corr-import-1',
})

afterEach(() => {
  cleanup()
  resetPendingAttempts()
  try { window.localStorage.clear() } catch { /* Storage is optional in the test DOM. */ }
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

async function readyImport() {
  const user = userEvent.setup()
  await waitFor(() => screen.getByRole('heading', { name: 'Excel’den ürün içe aktar' }))
  await user.upload(screen.getByLabelText('Excel dosyası (.xlsx)'),
    new File([new Uint8Array([1, 2, 3])], 'urunler.xlsx', {
      type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    }))
  await user.type(screen.getByLabelText('Eski kod sütunu'), 'Eski Kod')
  await user.type(screen.getByLabelText('Ürün adı sütunu'), 'Ürün Adı')
  await user.type(screen.getByLabelText('Grup kimliği sütunu'), 'Grup Kimliği')
  await user.click(screen.getByRole('button', { name: 'Önizle' }))
  await waitFor(() => expect(screen.getByText('1 satır içe aktarmaya hazır.')).toBeTruthy())
  return user
}

function expectSameApply(first: FormData, retry: FormData) {
  expect(retry.get('legacyCodeColumn')).toBe(first.get('legacyCodeColumn'))
  expect(retry.get('nameColumn')).toBe(first.get('nameColumn'))
  expect(retry.get('groupIdColumn')).toBe(first.get('groupIdColumn'))
  expect(retry.get('fileSha256')).toBe(first.get('fileSha256'))
  expect(retry.get('previewToken')).toBe(first.get('previewToken'))
  const firstFile = first.get('file') as File
  const retryFile = retry.get('file') as File
  expect({ name: retryFile.name, size: retryFile.size, type: retryFile.type })
    .toEqual({ name: firstFile.name, size: firstFile.size, type: firstFile.type })
}

const switchedTo = (companyId: string) => waitFor(() => {
  const picker = screen.getByLabelText('Şirket') as HTMLSelectElement
  expect(picker.disabled).toBe(false)
  expect(picker.value).toBe(companyId)
})

it('previews one immutable workbook, applies all rows and exposes the created card for reopen', async () => {
  const user = userEvent.setup()
  const { calls } = installFetch({
    capabilities: [PRODUCT_CAPABILITY.productsCreate, PRODUCT_CAPABILITY.productsDetail],
    onImportPreview: () => json(preview()),
    onImportApply: () => json(imported(), 201),
  })
  renderProductApp('/urun/ice-aktar')

  await waitFor(() => screen.getByRole('heading', { name: 'Excel’den ürün içe aktar' }))
  await user.upload(screen.getByLabelText('Excel dosyası (.xlsx)'),
    new File([new Uint8Array([1, 2, 3])], 'urunler.xlsx', { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }))
  await user.type(screen.getByLabelText('Eski kod sütunu'), 'Eski Kod')
  await user.type(screen.getByLabelText('Ürün adı sütunu'), 'Ürün Adı')
  await user.type(screen.getByLabelText('Grup kimliği sütunu'), 'Grup Kimliği')
  await user.click(screen.getByRole('button', { name: 'Önizle' }))

  await waitFor(() => expect(screen.getByText('1 satır içe aktarmaya hazır.')).toBeTruthy())
  expect(screen.getByText('OLD-1')).toBeTruthy()
  await user.click(screen.getByRole('button', { name: 'Tümünü içe aktar' }))

  await waitFor(() => expect(screen.getByText('1 ürün taslağı kaydedildi.')).toBeTruthy())
  const created = screen.getByRole('link', { name: 'SAC-1 — Galvaniz Rulo' })
  expect(created.getAttribute('href')).toBe(`/urun/${PRODUCT_1}`)
  const imports = calls.filter((call) => call.url.includes('/products/imports'))
  expect(imports).toHaveLength(2)
  expect(imports.every((call) => call.companyId === COMPANY_A)).toBe(true)
  expect(imports[0]?.idempotencyKey).toBeNull()
  expect(imports[1]?.idempotencyKey).toMatch(/^[0-9a-f]{32}$/)
  expect(imports[0]?.formData?.get('legacyCodeColumn')).toBe('Eski Kod')
  expect(imports[1]?.formData?.get('previewToken')).toBe(HASH_B)
})

it('retries a service-unavailable apply with the exact same key, workbook and preview binding', async () => {
  const { calls } = installFetch({
    capabilities: CREATE_AND_LIST,
    onImportPreview: () => json(preview()),
    onImportApply: (attempt) => attempt === 1 ? json({}, 503) : json(imported(true), 201),
  })
  renderProductApp(PATH)
  const user = await readyImport()

  await user.click(screen.getByRole('button', { name: 'Tümünü içe aktar' }))
  await waitFor(() => expect(screen.getByTestId('product-import-unknown')).toBeTruthy())
  await user.click(screen.getByRole('button', { name: 'Aynı isteği yeniden dene' }))
  await waitFor(() => expect(screen.getByText('1 ürün taslağı kaydedildi.')).toBeTruthy())

  const applies = calls.filter(call => call.url.endsWith('/products/imports') && call.method === 'POST')
  expect(applies).toHaveLength(2)
  expect(applies[1]?.idempotencyKey).toBe(applies[0]?.idempotencyKey)
  expect(applies[0]?.formData).not.toBeNull()
  expect(applies[1]?.formData).not.toBeNull()
  expectSameApply(applies[0].formData as FormData, applies[1].formData as FormData)
})

it('keeps the exact attempt after a malformed 201 and accepts its same-key replay', async () => {
  const { calls } = installFetch({
    capabilities: CREATE_AND_LIST,
    onImportPreview: () => json(preview()),
    onImportApply: (attempt) => attempt === 1 ? json({ unexpected: true }, 201) : json(imported(true), 201),
  })
  renderProductApp(PATH)
  const user = await readyImport()

  await user.click(screen.getByRole('button', { name: 'Tümünü içe aktar' }))
  await waitFor(() => expect(screen.getByTestId('product-import-unknown')).toBeTruthy())
  await user.click(screen.getByRole('button', { name: 'Aynı isteği yeniden dene' }))
  await waitFor(() => expect(screen.getByText('1 ürün taslağı kaydedildi.')).toBeTruthy())

  const applies = calls.filter(call => call.url.endsWith('/products/imports') && call.method === 'POST')
  expect(applies).toHaveLength(2)
  expect(applies[1]?.idempotencyKey).toBe(applies[0]?.idempotencyKey)
  expectSameApply(applies[0].formData as FormData, applies[1].formData as FormData)
})

it('restores an unresolved import after a 403 capability re-fence without writing and retries the same attempt on demand', async () => {
  const first = installFetch({
    capabilities: CREATE_AND_LIST,
    onImportPreview: () => json(preview()),
    onImportApply: (attempt) => attempt === 1 ? json({}, 503) : json({ code: 'forbidden' }, 403),
  })
  const { router } = renderProductApp(PATH)
  const user = await readyImport()

  await user.click(screen.getByRole('button', { name: 'Tümünü içe aktar' }))
  await waitFor(() => expect(screen.getByTestId('product-import-unknown')).toBeTruthy())
  await user.click(screen.getByRole('button', { name: 'Aynı isteği yeniden dene' }))
  await waitFor(() => expect(screen.queryByRole('heading', { name: 'Excel’den ürün içe aktar' })).toBeNull())
  const firstApply = first.calls.find(call => call.url.endsWith('/products/imports') && call.method === 'POST')
  expect(firstApply?.idempotencyKey).not.toBeNull()

  const reentry = installFetch({
    capabilities: CREATE_AND_LIST,
    capabilityGeneration: 'gen-2',
    onImportApply: () => json(imported(true), 201),
  })
  await act(async () => { await router.navigate('/urun') })
  await act(async () => { await router.navigate(PATH) })
  await waitFor(() => expect(screen.getByTestId('product-import-unknown')).toBeTruthy())
  expect(reentry.calls.filter(call => call.url.endsWith('/products/imports') && call.method === 'POST')).toHaveLength(0)

  await user.click(screen.getByRole('button', { name: 'Aynı isteği yeniden dene' }))
  await waitFor(() => expect(screen.getByText('1 ürün taslağı kaydedildi.')).toBeTruthy())
  const retried = reentry.calls.find(call => call.url.endsWith('/products/imports') && call.method === 'POST')
  expect(retried?.idempotencyKey).toBe(firstApply?.idempotencyKey)
  expect(retried?.companyId).toBe(firstApply?.companyId)
  expectSameApply(firstApply?.formData as FormData, retried?.formData as FormData)
})

it('drops an unresolved import only after the user confirms a company switch', async () => {
  installFetch({
    capabilities: CREATE_AND_LIST,
    onImportPreview: () => json(preview()),
    onImportApply: () => json({}, 503),
  })
  const { router } = renderProductApp(PATH)
  const user = await readyImport()

  await user.click(screen.getByRole('button', { name: 'Tümünü içe aktar' }))
  await waitFor(() => expect(screen.getByTestId('product-import-unknown')).toBeTruthy())
  await user.selectOptions(screen.getByLabelText('Şirket'), COMPANY_B)
  const dialog = await waitFor(() => screen.getByRole('dialog'))
  await user.click(within(dialog).getByRole('button', { name: 'Değişiklikleri bırak' }))
  await switchedTo(COMPANY_B)

  await user.selectOptions(screen.getByLabelText('Şirket'), COMPANY_A)
  await switchedTo(COMPANY_A)
  await act(async () => { await router.navigate('/urun') })
  await act(async () => { await router.navigate(PATH) })
  await waitFor(() => screen.getByRole('heading', { name: 'Excel’den ürün içe aktar' }))
  expect(screen.queryByTestId('product-import-unknown')).toBeNull()
  expect((screen.getByLabelText('Excel dosyası (.xlsx)') as HTMLInputElement).files).toHaveLength(0)
})

it('shows a downloadable error list and never offers apply for a rejected preview', async () => {
  const user = userEvent.setup()
  const createObjectURL = vi.fn(() => 'blob:errors')
  const revokeObjectURL = vi.fn()
  class StubUrl extends URL {
    static createObjectURL = createObjectURL
    static revokeObjectURL = revokeObjectURL
  }
  vi.stubGlobal('URL', StubUrl)
  const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
  installFetch({
    capabilities: [PRODUCT_CAPABILITY.productsCreate],
    onImportPreview: () => json({
      fileSha256: HASH_A,
      previewToken: HASH_B,
      sourceRowCount: 1,
      rows: [],
      errors: [{ rowNumber: 2, field: 'legacyCode', code: 'existing_legacy_code', message: 'Eski kod kayıtlı.' }],
      ready: false,
    }),
  })
  renderProductApp('/urun/ice-aktar')

  await waitFor(() => screen.getByRole('heading', { name: 'Excel’den ürün içe aktar' }))
  await user.upload(screen.getByLabelText('Excel dosyası (.xlsx)'), new File([new Uint8Array([1])], 'urunler.xlsx'))
  await user.type(screen.getByLabelText('Eski kod sütunu'), 'Eski Kod')
  await user.type(screen.getByLabelText('Ürün adı sütunu'), 'Ürün Adı')
  await user.type(screen.getByLabelText('Grup kimliği sütunu'), 'Grup Kimliği')
  await user.click(screen.getByRole('button', { name: 'Önizle' }))

  await waitFor(() => expect(screen.getByText('Eski kod kayıtlı.')).toBeTruthy())
  expect(screen.queryByRole('button', { name: 'Tümünü içe aktar' })).toBeNull()
  await user.click(screen.getByRole('button', { name: 'Hata listesini indir' }))
  expect(createObjectURL).toHaveBeenCalledOnce()
  expect(revokeObjectURL).toHaveBeenCalledWith('blob:errors')
  expect(click).toHaveBeenCalledOnce()
})
