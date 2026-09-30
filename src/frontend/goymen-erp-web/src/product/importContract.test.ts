import { describe, expect, it } from 'vitest'
import { decodeProductImportPreview, decodeProductImportResult, productImportErrorsCsv } from './importContract'
import { GROUP_SAC, PRODUCT_1 } from './testHarness'

const HASH_A = 'A'.repeat(64)
const HASH_B = 'B'.repeat(64)

describe('Product import wire contract', () => {
  it('accepts a consistent ready preview and rejects a partial ready claim', () => {
    const value = {
      fileSha256: HASH_A,
      previewToken: HASH_B,
      sourceRowCount: 1,
      rows: [{ rowNumber: 2, legacyCode: 'OLD-1', name: 'Galvaniz Rulo', groupId: GROUP_SAC }],
      errors: [],
      ready: true,
    }
    expect(decodeProductImportPreview(value)?.rows[0]?.legacyCode).toBe('OLD-1')
    expect(decodeProductImportPreview({ ...value, sourceRowCount: 2 })).toBeNull()
  })

  it('rejects duplicate identities in an apply result', () => {
    const product = { productId: PRODUCT_1, code: 'SAC-1', legacyCode: 'OLD-1', name: 'Rulo', groupId: GROUP_SAC }
    expect(decodeProductImportResult({ products: [product], isReplay: false, originalCorrelationId: 'corr-1' }))
      .not.toBeNull()
    expect(decodeProductImportResult({ products: [product, product], isReplay: false, originalCorrelationId: 'corr-1' }))
      .toBeNull()
  })

  it('exports errors as UTF-8 BOM CSV with stable escaping', async () => {
    const blob = productImportErrorsCsv([
      { rowNumber: 4, field: 'name', code: 'invalid_name', message: 'Virgül, ve "tırnak"' },
    ])
    const bytes = await new Promise<Uint8Array>((resolve, reject) => {
      const reader = new FileReader()
      reader.onerror = () => reject(reader.error)
      reader.onload = () => resolve(new Uint8Array(reader.result as ArrayBuffer))
      reader.readAsArrayBuffer(blob)
    })
    expect([...bytes.slice(0, 3)]).toEqual([0xEF, 0xBB, 0xBF])
    expect(new TextDecoder().decode(bytes.slice(3))).toContain('"Virgül, ve ""tırnak"""')
  })
})
