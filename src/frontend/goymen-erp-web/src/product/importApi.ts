import { API_BASE, originHeaders, type CommandOutcome, type ProductOrigin } from './api'
import { productProblemCodeOf } from './contract'
import {
  decodeProductImportPreview,
  decodeProductImportResult,
  type ProductImportColumnMapping,
  type ProductImportPreview,
  type ProductImportResult,
} from './importContract'
import type { ViewOutcome } from '../shell/uiState'

function form(file: File, mapping: ProductImportColumnMapping, preview?: ProductImportPreview): FormData {
  const data = new FormData()
  data.set('file', file, file.name)
  data.set('legacyCodeColumn', mapping.legacyCode.trim())
  data.set('nameColumn', mapping.name.trim())
  data.set('groupIdColumn', mapping.groupId.trim())
  if (preview !== undefined) {
    data.set('fileSha256', preview.fileSha256)
    data.set('previewToken', preview.previewToken)
  }
  return data
}

async function body(response: Response): Promise<unknown> {
  try { return await response.json() } catch { return null }
}

export async function previewProductImport(file: File, mapping: ProductImportColumnMapping,
  origin: ProductOrigin, signal?: AbortSignal): Promise<ViewOutcome<ProductImportPreview>> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}/products/imports/preview`, {
      method: 'POST', headers: originHeaders(origin), body: form(file, mapping), signal,
    })
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    if (!navigator.onLine) return { type: 'offline' }
    return { type: 'network-error' }
  }
  const value = await body(response)
  if (response.status !== 200) return { type: 'http', status: response.status, body: value }
  const preview = decodeProductImportPreview(value)
  return preview === null ? { type: 'malformed' } : { type: 'ok-single', data: preview }
}

export async function applyProductImport(file: File, mapping: ProductImportColumnMapping,
  preview: ProductImportPreview, idempotencyKey: string, origin: ProductOrigin,
  signal?: AbortSignal): Promise<CommandOutcome<ProductImportResult>> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}/products/imports`, {
      method: 'POST',
      headers: { ...originHeaders(origin), 'Idempotency-Key': idempotencyKey },
      body: form(file, mapping, preview),
      signal,
    })
  } catch {
    if (signal?.aborted) return { type: 'aborted' }
    if (!navigator.onLine) return { type: 'offline' }
    return { type: 'network-error' }
  }
  const value = await body(response)
  if (response.status !== 201) {
    return { type: 'http', status: response.status, code: productProblemCodeOf(value), body: value }
  }
  const result = decodeProductImportResult(value)
  return result === null ? { type: 'malformed' } : { type: 'succeeded', data: result }
}
