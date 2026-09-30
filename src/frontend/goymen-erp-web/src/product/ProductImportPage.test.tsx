import { afterEach, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PRODUCT_CAPABILITY } from './capabilities'
import { COMPANY_A, GROUP_SAC, PRODUCT_1, installFetch, json, renderProductApp } from './testHarness'

const HASH_A = 'A'.repeat(64)
const HASH_B = 'B'.repeat(64)

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

it('previews one immutable workbook, applies all rows and exposes the created card for reopen', async () => {
  const user = userEvent.setup()
  const { calls } = installFetch({
    capabilities: [PRODUCT_CAPABILITY.productsCreate, PRODUCT_CAPABILITY.productsDetail],
    onImportPreview: () => json({
      fileSha256: HASH_A,
      previewToken: HASH_B,
      sourceRowCount: 1,
      rows: [{ rowNumber: 2, legacyCode: 'OLD-1', name: 'Galvaniz Rulo', groupId: GROUP_SAC }],
      errors: [],
      ready: true,
    }),
    onImportApply: () => json({
      products: [{ productId: PRODUCT_1, code: 'SAC-1', legacyCode: 'OLD-1', name: 'Galvaniz Rulo', groupId: GROUP_SAC }],
      isReplay: false,
      originalCorrelationId: 'corr-import-1',
    }, 201),
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
  expect(imports[1]?.idempotencyKey).toMatch(/^product-import-/)
  expect(imports[0]?.formData?.get('legacyCodeColumn')).toBe('Eski Kod')
  expect(imports[1]?.formData?.get('previewToken')).toBe(HASH_B)
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
