/**
 * KLN-D54 (STK-S37) — the company's structural profile weight table: the kg/m of each standard profile (UPN, NPI, IPE, HEA, HEB,
 * HEM), imported by an administrator from the standard or catalogue in hand with its source label, versioned and audited. A
 * structural profile's weight on the product card is read from this table and never typed. Pinned to
 * `GET /api/v1/product-profile-weight-tables/current` and `POST /api/v1/product-profile-weight-tables`
 * (`ProductProfileWeightTableEndpoints`), both under `product.profile-weights.import` in the selected company.
 *
 * Import format (slice spec): a UTF-8 text file whose first line is `series;size;kgPerMetre` and then one profile per line,
 * `;` separated, the decimal point a dot (`IPE;200;22.4`). The browser reads the file and sends the rows as JSON; the file
 * itself is never uploaded (B-26). A value is never rounded or invented: a row the server would refuse is refused here with its
 * line number, and the server checks it again.
 */

import { PROFILE_SERIES, isProfileSize, type ProfileSeries } from './dimensionApi'

export const PROFILE_WEIGHT_SOURCE_LABEL_MAXIMUM_LENGTH = 200
export const PROFILE_WEIGHT_REASON_MAXIMUM_LENGTH = 4000
export const PROFILE_WEIGHT_MAXIMUM_ROWS = 2000
/** kg/m carries at most six decimals (the column's scale; `ProductDimensionInputLimitsV1`). */
export const PROFILE_WEIGHT_DECIMALS = 6
export const PROFILE_WEIGHT_FILE_HEADER = 'series;size;kgPerMetre'

export const PROFILE_WEIGHT_PROBLEM = {
  rowsInvalid: 'product_profile_weight_rows_invalid',
  sourceRequired: 'product_profile_weight_source_required',
  sourceTooLong: 'product_profile_weight_source_too_long',
  reasonRequired: 'product_profile_weight_reason_required',
  reasonTooLong: 'product_profile_weight_reason_too_long',
  idempotencyConflict: 'product_profile_weight_idempotency_conflict',
  versionConflict: 'product_profile_weight_version_conflict',
} as const

export interface ProfileWeightRow {
  readonly series: ProfileSeries
  readonly size: string
  readonly kgPerMetre: number
}

export interface ProfileWeightTableView {
  /** "0" while no table has been imported. */
  readonly version: string
  readonly sourceLabel: string | null
  readonly reason: string | null
  readonly importedAtUtc: string | null
  readonly importedBy: string | null
  readonly rows: readonly ProfileWeightRow[]
}

export type ProfileWeightRead =
  | { readonly type: 'ok'; readonly data: ProfileWeightTableView }
  | { readonly type: 'unauthenticated' }
  | { readonly type: 'forbidden' }
  | { readonly type: 'unavailable' }
  | { readonly type: 'failed' }

export type ProfileWeightCommand =
  | { readonly type: 'applied'; readonly replayed: boolean }
  | { readonly type: 'refused'; readonly status: number; readonly code: string | null; readonly detail: string | null }
  | { readonly type: 'unknown' }

const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED = import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

export const PROFILE_WEIGHT_PATH = '/product-profile-weight-tables'
export const PROFILE_WEIGHT_CURRENT_PATH = `${PROFILE_WEIGHT_PATH}/current`

function headers(companyId: string): Record<string, string> {
  return { ...(DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}), 'X-Company-Id': companyId }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function hasExactKeys(value: Record<string, unknown>, keys: readonly string[]): boolean {
  const own = Object.keys(value)
  return own.length === keys.length && keys.every((key) => Object.hasOwn(value, key))
}

const VIEW_KEYS = ['companyId', 'version', 'sourceLabel', 'reason', 'importedAtUtc', 'importedBy', 'rows'] as const
const ROW_KEYS = ['series', 'size', 'kgPerMetre'] as const

function isNullableText(value: unknown): value is string | null {
  return value === null || (typeof value === 'string' && value.length > 0)
}

function isWeight(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value) && value > 0
}

function decodeRow(raw: unknown): ProfileWeightRow | null {
  if (!isRecord(raw) || !hasExactKeys(raw, ROW_KEYS)) return null
  if (!PROFILE_SERIES.includes(raw.series as ProfileSeries) || !isProfileSize(raw.size) || !isWeight(raw.kgPerMetre)) return null
  return { series: raw.series as ProfileSeries, size: raw.size, kgPerMetre: raw.kgPerMetre }
}

/** Strict: exactly the view's members, the selected company, and either "no table" (all empty) or a complete imported table. */
export function decodeProfileWeightTable(raw: unknown, companyId: string): ProfileWeightTableView | null {
  if (!isRecord(raw) || !hasExactKeys(raw, VIEW_KEYS)) return null
  if (typeof raw.companyId !== 'string' || raw.companyId.toLowerCase() !== companyId.toLowerCase()) return null
  if (typeof raw.version !== 'string' || !/^(0|[1-9]\d{0,18})$/.test(raw.version)) return null
  if (!isNullableText(raw.sourceLabel) || !isNullableText(raw.reason) || !isNullableText(raw.importedBy)) return null
  if (raw.importedAtUtc !== null && (typeof raw.importedAtUtc !== 'string' || Number.isNaN(Date.parse(raw.importedAtUtc)))) {
    return null
  }
  if (!Array.isArray(raw.rows)) return null
  const rows: ProfileWeightRow[] = []
  for (const item of raw.rows) {
    const row = decodeRow(item)
    if (row === null) return null
    rows.push(row)
  }
  const none = raw.version === '0'
  const header = [raw.sourceLabel, raw.reason, raw.importedAtUtc, raw.importedBy]
  if (none ? header.some((value) => value !== null) || rows.length !== 0 : header.some((value) => value === null) || rows.length === 0) {
    return null
  }
  return {
    version: raw.version,
    sourceLabel: raw.sourceLabel,
    reason: raw.reason,
    importedAtUtc: raw.importedAtUtc as string | null,
    importedBy: raw.importedBy,
    rows,
  }
}

export async function fetchProfileWeightTable(companyId: string, signal?: AbortSignal): Promise<ProfileWeightRead> {
  try {
    const response = await fetch(`${API_BASE}${PROFILE_WEIGHT_CURRENT_PATH}`, { headers: headers(companyId), signal })
    if (response.status === 401) return { type: 'unauthenticated' }
    if (response.status === 403) return { type: 'forbidden' }
    if (response.status === 503) return { type: 'unavailable' }
    if (response.status !== 200) return { type: 'failed' }
    const data = decodeProfileWeightTable(await response.json(), companyId)
    return data === null ? { type: 'failed' } : { type: 'ok', data }
  } catch {
    return { type: 'failed' }
  }
}

export interface ImportProfileWeightBody {
  readonly expectedVersion: string
  readonly sourceLabel: string
  readonly reason: string
  readonly rows: readonly ProfileWeightRow[]
}

export async function importProfileWeightTable(
  companyId: string,
  idempotencyKey: string,
  body: ImportProfileWeightBody,
): Promise<ProfileWeightCommand> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}${PROFILE_WEIGHT_PATH}`, {
      method: 'POST',
      headers: { ...headers(companyId), 'Content-Type': 'application/json', 'Idempotency-Key': idempotencyKey },
      body: JSON.stringify(body),
    })
  } catch {
    return { type: 'unknown' }
  }
  if (response.status === 204) return { type: 'applied', replayed: response.headers.get('Idempotency-Replayed') === 'true' }
  if (response.status >= 500 || response.status < 400) return { type: 'unknown' }
  let raw: unknown = null
  try {
    raw = await response.json()
  } catch {
    raw = null
  }
  const code = isRecord(raw) && typeof raw.code === 'string' && raw.code.length > 0 ? raw.code : null
  const detail = isRecord(raw) && typeof raw.detail === 'string' && raw.detail.length > 0 ? raw.detail : null
  return { type: 'refused', status: response.status, code, detail }
}

export type ParsedFile =
  | { readonly ok: true; readonly rows: readonly ProfileWeightRow[] }
  /** `line` is the 1-based line of the file (the header is line 1); null when the file as a whole is wrong. */
  | { readonly ok: false; readonly reason: ParseRefusal; readonly line: number | null }

export type ParseRefusal = 'header' | 'empty' | 'tooMany' | 'columns' | 'series' | 'size' | 'weight' | 'duplicate'

/** One kg/m as written in the file: a positive decimal with a dot and at most six decimals. Never rounded. */
export function parseWeight(text: string): number | null {
  if (!new RegExp(`^\\d+(\\.\\d{1,${PROFILE_WEIGHT_DECIMALS}})?$`).test(text)) return null
  const value = Number(text)
  if (!Number.isFinite(value) || value <= 0 || !Number.isSafeInteger(Math.round(value * 10 ** PROFILE_WEIGHT_DECIMALS))) {
    return null
  }
  return value
}

/** Reads the import file: header first, then `series;size;kgPerMetre` lines. Blank lines are skipped; a BOM is ignored. */
export function parseProfileWeightFile(text: string): ParsedFile {
  const lines = text.replace(/^﻿/, '').split(/\r?\n/)
  if ((lines[0] ?? '').trim() !== PROFILE_WEIGHT_FILE_HEADER) return { ok: false, reason: 'header', line: 1 }
  const rows: ProfileWeightRow[] = []
  const seen = new Set<string>()
  for (let index = 1; index < lines.length; index += 1) {
    const line = lines[index].trim()
    if (line === '') continue
    const number = index + 1
    const cells = line.split(';').map((cell) => cell.trim())
    if (cells.length !== 3) return { ok: false, reason: 'columns', line: number }
    const [series, size, weightText] = cells
    if (!PROFILE_SERIES.includes(series as ProfileSeries)) return { ok: false, reason: 'series', line: number }
    if (!isProfileSize(size)) return { ok: false, reason: 'size', line: number }
    const kgPerMetre = parseWeight(weightText)
    if (kgPerMetre === null) return { ok: false, reason: 'weight', line: number }
    const key = `${series}\u0000${size}`
    if (seen.has(key)) return { ok: false, reason: 'duplicate', line: number }
    seen.add(key)
    rows.push({ series: series as ProfileSeries, size, kgPerMetre })
    if (rows.length > PROFILE_WEIGHT_MAXIMUM_ROWS) return { ok: false, reason: 'tooMany', line: null }
  }
  if (rows.length === 0) return { ok: false, reason: 'empty', line: null }
  return { ok: true, rows }
}
