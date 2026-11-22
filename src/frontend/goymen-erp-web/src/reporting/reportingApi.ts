/**
 * WP14 report centre transport. Reads send the committed shell company (`X-Company-Id`); the server fences every answer to
 * it. An export or an auditor package is a POST because the server writes an export record and an audit event before the
 * file leaves; the browser only saves the bytes under the server's name. Saved-view commands reuse the shared command
 * sender (one Idempotency-Key per attempt, kept by the caller for a retry after an unknown outcome).
 */
import { API_BASE, readJson, send, type CommandContext } from '../settings/discountLimitApi'
import { attachmentFileName, saveOrderPdf } from '../sales/orderPdf'
import type { ViewOutcome } from '../shell/uiState'
import {
  AUDITOR_PACKAGE_PATH, REPORTS_PATH, SAVED_VIEWS_PATH, decodeCatalog, decodeSavedView, decodeSavedViews, decodeTable, exportPath,
  problemCodeOf, reportPath, savedViewPassivatePath, savedViewPath,
  type ReportCatalog, type ReportColumn, type ReportTable, type SavedViewBody, type SavedViewPage,
} from './reportingContract'

const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED = import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

export function fetchReportCatalog(companyId: string, signal?: AbortSignal): Promise<ViewOutcome<ReportCatalog>> {
  return readJson(REPORTS_PATH, companyId, (raw) => decodeCatalog(raw, companyId), signal)
}

export function runReport(companyId: string, reportId: string, columns: readonly ReportColumn[], parameters: Readonly<Record<string, string>>,
  page: number, pageSize: number, signal?: AbortSignal): Promise<ViewOutcome<ReportTable>> {
  const search = new URLSearchParams()
  for (const [name, value] of Object.entries(parameters)) if (value.trim().length > 0) search.set(name, value.trim())
  search.set('page', String(page))
  search.set('pageSize', String(pageSize))
  return readJson(`${reportPath(reportId)}?${search.toString()}`, companyId, (raw) => decodeTable(raw, companyId, reportId, columns), signal)
}

export function fetchSavedViews(companyId: string, reportId: string, signal?: AbortSignal): Promise<ViewOutcome<SavedViewPage>> {
  return readJson(`${SAVED_VIEWS_PATH}?reportId=${encodeURIComponent(reportId)}`, companyId, (raw) => decodeSavedViews(raw, companyId, reportId), signal)
}

export const createSavedView = (context: CommandContext, body: SavedViewBody, signal?: AbortSignal) =>
  send('POST', SAVED_VIEWS_PATH, context, body, 201, decodeSavedView(context.companyId, body.reportId), signal)

export const updateSavedView = (context: CommandContext, viewId: string, body: SavedViewBody, signal?: AbortSignal) =>
  send('PUT', savedViewPath(viewId), context, body, 200, decodeSavedView(context.companyId, body.reportId), signal)

export const passivateSavedView = (context: CommandContext, viewId: string, reportId: string, expectedVersion: string, signal?: AbortSignal) =>
  send('POST', savedViewPassivatePath(viewId), context, { expectedVersion }, 200, decodeSavedView(context.companyId, reportId), signal)

export type FileOutcome =
  | { readonly type: 'saved'; readonly fileName: string }
  | { readonly type: 'refused'; readonly status: number; readonly code: string | null }
  | { readonly type: 'failed' }

const MEDIA_TYPES = new Set(['text/csv', 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', 'application/zip'])

async function downloadFile(companyId: string, path: string, body: object, fallbackName: string, signal?: AbortSignal): Promise<FileOutcome> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}${path}`, {
      method: 'POST',
      headers: {
        ...(DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}),
        'Content-Type': 'application/json',
        'X-Company-Id': companyId,
      },
      body: JSON.stringify(body),
      signal,
    })
  } catch {
    return { type: 'failed' }
  }
  if (response.status !== 200) {
    let raw: unknown = null
    try { raw = await response.json() } catch { raw = null }
    return { type: 'refused', status: response.status, code: problemCodeOf(raw) }
  }
  const mediaType = (response.headers.get('Content-Type') ?? '').split(';')[0].trim().toLowerCase()
  if (!MEDIA_TYPES.has(mediaType)) return { type: 'failed' }
  try {
    const blob = await response.blob()
    if (blob.size === 0) return { type: 'failed' }
    const fileName = attachmentFileName(response.headers.get('Content-Disposition')) ?? fallbackName
    saveOrderPdf(blob, fileName)
    return { type: 'saved', fileName }
  } catch {
    return { type: 'failed' }
  }
}

export function exportReport(companyId: string, reportId: string, format: 'csv' | 'xlsx', parameters: Readonly<Record<string, string>>,
  page: number, pageSize: number, columns: readonly string[], signal?: AbortSignal): Promise<FileOutcome> {
  return downloadFile(companyId, exportPath(reportId), { format, parameters, page, pageSize, columns }, `${reportId}.${format}`, signal)
}

export function buildAuditorPackage(companyId: string, from: string, to: string, signal?: AbortSignal): Promise<FileOutcome> {
  return downloadFile(companyId, AUDITOR_PACKAGE_PATH, { from, to }, 'denetci-paketi.zip', signal)
}
