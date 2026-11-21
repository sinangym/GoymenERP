import { PAINT_LIMITS } from './contract'

export interface FrozenPaintFile {
  readonly fileName: string
  readonly contentBase64: string
  readonly sha256: string
}
export function validPaintFile(file: File): boolean {
  return file.size > 0 && file.size <= PAINT_LIMITS.fileBytes && file.name.length <= 255 && /\.xlsx$/i.test(file.name)
}
/** No workbook interpretation in the browser. Hash and base64 come from one immutable byte snapshot. */
export async function freezePaintFile(file: File): Promise<FrozenPaintFile> {
  if (!validPaintFile(file)) throw new Error('paint_file_invalid')
  const buffer = await new Promise<ArrayBuffer>((resolve, reject) => {
    const reader = new FileReader()
    reader.onerror = () => reject(new Error('paint_file_unreadable'))
    reader.onabort = () => reject(new Error('paint_file_unreadable'))
    reader.onload = () => reader.result instanceof ArrayBuffer ? resolve(reader.result) : reject(new Error('paint_file_unreadable'))
    reader.readAsArrayBuffer(file)
  })
  if (buffer.byteLength !== file.size) throw new Error('paint_file_unreadable')
  const bytes = new Uint8Array(buffer)
  const hash = new Uint8Array(await crypto.subtle.digest('SHA-256', buffer))
  const sha256 = Array.from(hash, (byte) => byte.toString(16).padStart(2, '0')).join('')
  let binary = ''
  for (let offset = 0; offset < bytes.length; offset += 8192) binary += String.fromCharCode(...bytes.subarray(offset, offset + 8192))
  return Object.freeze({ fileName: file.name, contentBase64: btoa(binary), sha256 })
}
