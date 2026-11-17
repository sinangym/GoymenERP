import { afterEach, describe, expect, it, vi } from 'vitest'
import { freezePaintFile, validPaintFile } from './paintFile'
import { PAINT_LIMITS } from './contract'

afterEach(() => vi.unstubAllGlobals())
describe('paint membership immutable file envelope', () => {
  it('preserves one actual binary snapshot as both base64 and SHA input', async () => {
    const digest = vi.fn(async (_algorithm: string, bytes: ArrayBuffer) => {
      expect(Array.from(new Uint8Array(bytes))).toEqual([0, 1, 255])
      return new Uint8Array(32).fill(0xab).buffer
    })
    vi.stubGlobal('crypto', { subtle: { digest } })
    const result = await freezePaintFile(new File([new Uint8Array([0, 1, 255])], 'SENTETIK.xlsx'))
    expect(result).toEqual({ fileName: 'SENTETIK.xlsx', contentBase64: 'AAH/', sha256: 'ab'.repeat(32) })
    expect(Object.isFrozen(result)).toBe(true)
    expect(digest).toHaveBeenCalledTimes(1)
  })
  it.each([0, PAINT_LIMITS.fileBytes + 1])('refuses invalid byte length %i before reading', async (length) => {
    const file = new File([new Uint8Array(length)], 'SENTETIK.xlsx')
    expect(validPaintFile(file)).toBe(false)
    await expect(freezePaintFile(file)).rejects.toThrow('paint_file_invalid')
  })
  it('accepts exactly the allowed byte limit and refuses an alternate file extension', () => {
    expect(validPaintFile(new File([new Uint8Array(PAINT_LIMITS.fileBytes)], 'SENTETIK.XLSX'))).toBe(true)
    expect(validPaintFile(new File(['SENTETIK'], 'SENTETIK.xlsm'))).toBe(false)
    expect(validPaintFile(new File(['SENTETIK'], 'SENTETIK.csv'))).toBe(false)
  })
  it('does not offer a file envelope when digest generation fails', async () => {
    vi.stubGlobal('crypto', { subtle: { digest: vi.fn(async () => { throw new Error('SENTETIK digest failure') }) } })
    await expect(freezePaintFile(new File(['SENTETIK'], 'SENTETIK.xlsx'))).rejects.toThrow('SENTETIK digest failure')
  })
})
