/**
 * WP14 report centre contract: paths, routes, types and strict decoders. Every decoder returns the typed value or null
 * (exact key sets, company checked); cells stay invariant text — numbers are never parsed for arithmetic here.
 */
export const REPORTS_PATH = '/reporting/reports'
export const reportPath = (reportId: string) => `${REPORTS_PATH}/${encodeURIComponent(reportId)}`
export const exportPath = (reportId: string) => `${reportPath(reportId)}/exports`
export const SAVED_VIEWS_PATH = '/reporting/saved-views'
export const savedViewPath = (viewId: string) => `${SAVED_VIEWS_PATH}/${viewId}`
export const savedViewPassivatePath = (viewId: string) => `${savedViewPath(viewId)}/passivate`
export const AUDITOR_PACKAGE_PATH = '/reporting/auditor-packages'
export const REPORTING_ROUTE = { center: '/raporlar', report: '/raporlar/:reportId', auditor: '/raporlar/denetci-paketi' } as const
export const reportRoute = (reportId: string) => `/raporlar/${encodeURIComponent(reportId)}`

export type ParameterKind = 'date' | 'guid' | 'choice' | 'whole-number' | 'decimal' | 'ascending-whole-numbers'
export type ColumnKind = 'text' | 'number' | 'date' | 'instant' | 'identifier' | 'flag'
export interface ReportParameter { readonly id: string; readonly kind: ParameterKind; readonly required: boolean; readonly choices: readonly string[]; readonly minimum: string | null; readonly maximum: string | null }
export interface ReportColumn { readonly id: string; readonly kind: ColumnKind }
export interface ReportDefinition { readonly id: string; readonly ownerModule: string; readonly parameters: readonly ReportParameter[]; readonly columns: readonly ReportColumn[]; readonly canExport: boolean }
export interface ReportCatalog { readonly companyId: string; readonly reports: readonly ReportDefinition[]; readonly canBuildAuditorPackage: boolean }
export interface SummaryItem { readonly id: string; readonly value: string }
export interface ReportTable {
  readonly companyId: string; readonly reportId: string; readonly parameters: Readonly<Record<string, string>>; readonly columns: readonly string[]
  readonly rows: readonly (readonly (string | null)[])[]; readonly page: number; readonly pageSize: number; readonly hasMore: boolean
  readonly summary: readonly SummaryItem[]; readonly sourceStatuses: readonly SummaryItem[]; readonly generatedAtUtc: string
}
export interface SavedView {
  readonly id: string; readonly companyId: string; readonly reportId: string; readonly name: string; readonly parameters: Readonly<Record<string, string>>
  readonly columns: readonly string[]; readonly version: string; readonly isActive: boolean; readonly createdAtUtc: string; readonly updatedAtUtc: string
}
export interface SavedViewPage { readonly companyId: string; readonly reportId: string; readonly items: readonly SavedView[] }
export interface SavedViewBody { readonly reportId: string; readonly name: string; readonly parameters: Readonly<Record<string, string>>; readonly columns: readonly string[]; readonly expectedVersion: string | null }

const PARAMETER_KINDS: readonly ParameterKind[] = ['date', 'guid', 'choice', 'whole-number', 'decimal', 'ascending-whole-numbers']
const COLUMN_KINDS: readonly ColumnKind[] = ['text', 'number', 'date', 'instant', 'identifier', 'flag']
const object = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value)
const exact = (value: Record<string, unknown>, names: readonly string[]) => Object.keys(value).length === names.length && names.every((name) => Object.hasOwn(value, name))
const guid = (value: unknown): value is string => typeof value === 'string' && /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i.test(value) && value !== '00000000-0000-0000-0000-000000000000'
const instant = (value: unknown): value is string => typeof value === 'string' && value.length <= 64 && /^\d{4}-\d\d-\d\dT.*(?:Z|[+-]\d\d:\d\d)$/.test(value) && Number.isFinite(Date.parse(value))
const wireId = (value: unknown): value is string => typeof value === 'string' && /^[a-z][a-zA-Z0-9:+-]{0,79}$/.test(value)
const text = (value: unknown, max: number): value is string => typeof value === 'string' && value.length <= max
const strings = (value: unknown, check: (item: unknown) => boolean): value is string[] => Array.isArray(value) && value.every(check)
const stringMap = (value: unknown): value is Record<string, string> => object(value) && Object.entries(value).every(([key, item]) => wireId(key) && text(item, 200))
const version = (value: unknown): value is string => typeof value === 'string' && /^[1-9]\d{0,18}$/.test(value) && BigInt(value) <= 9223372036854775807n
const summary = (value: unknown): value is SummaryItem[] => Array.isArray(value) && value.every((item) => object(item) && exact(item, ['id', 'value']) && wireId(item.id) && text(item.value, 200))

function decodeParameter(raw: unknown): ReportParameter | null {
  if (!object(raw) || !exact(raw, ['id', 'kind', 'required', 'choices', 'minimum', 'maximum']) || !wireId(raw.id)
    || !PARAMETER_KINDS.includes(raw.kind as ParameterKind) || typeof raw.required !== 'boolean' || !strings(raw.choices, wireId)
    || (raw.minimum !== null && !text(raw.minimum, 40)) || (raw.maximum !== null && !text(raw.maximum, 40))) return null
  return raw as unknown as ReportParameter
}

function decodeDefinition(raw: unknown): ReportDefinition | null {
  if (!object(raw) || !exact(raw, ['id', 'ownerModule', 'parameters', 'columns', 'canExport']) || !wireId(raw.id) || !wireId(raw.ownerModule)
    || typeof raw.canExport !== 'boolean' || !Array.isArray(raw.parameters) || !Array.isArray(raw.columns) || raw.columns.length === 0) return null
  const parameters = raw.parameters.map(decodeParameter)
  const columns = raw.columns.map((column) => object(column) && exact(column, ['id', 'kind']) && wireId(column.id) && COLUMN_KINDS.includes(column.kind as ColumnKind)
    ? column as unknown as ReportColumn : null)
  if (parameters.some((item) => item === null) || columns.some((item) => item === null)) return null
  if (new Set(columns.map((column) => column!.id)).size !== columns.length) return null
  return { id: raw.id, ownerModule: raw.ownerModule, parameters: parameters as ReportParameter[], columns: columns as ReportColumn[], canExport: raw.canExport }
}

export function decodeCatalog(raw: unknown, companyId: string): ReportCatalog | null {
  if (!object(raw) || !exact(raw, ['companyId', 'reports', 'canBuildAuditorPackage']) || raw.companyId !== companyId || !guid(companyId)
    || typeof raw.canBuildAuditorPackage !== 'boolean' || !Array.isArray(raw.reports)) return null
  const reports = raw.reports.map(decodeDefinition)
  if (reports.some((report) => report === null) || new Set(reports.map((report) => report!.id)).size !== reports.length) return null
  return { companyId, reports: reports as ReportDefinition[], canBuildAuditorPackage: raw.canBuildAuditorPackage }
}

export function decodeTable(raw: unknown, companyId: string, reportId: string, columns: readonly ReportColumn[]): ReportTable | null {
  if (!object(raw) || !exact(raw, ['companyId', 'reportId', 'parameters', 'columns', 'rows', 'page', 'pageSize', 'hasMore', 'summary', 'sourceStatuses', 'generatedAtUtc'])
    || raw.companyId !== companyId || raw.reportId !== reportId || !stringMap(raw.parameters) || !Array.isArray(raw.columns)
    || raw.columns.length !== columns.length || raw.columns.some((id, index) => id !== columns[index].id)
    || !Number.isInteger(raw.page) || (raw.page as number) < 1 || !Number.isInteger(raw.pageSize) || (raw.pageSize as number) < 1
    || typeof raw.hasMore !== 'boolean' || !summary(raw.summary) || !summary(raw.sourceStatuses) || !instant(raw.generatedAtUtc)
    || !Array.isArray(raw.rows) || raw.rows.length > (raw.pageSize as number)) return null
  for (const row of raw.rows) {
    if (!Array.isArray(row) || row.length !== columns.length) return null
    for (let index = 0; index < row.length; index++) {
      const cell: unknown = row[index]
      if (cell === null) continue
      if (!text(cell, 4000)) return null
      const kind = columns[index].kind
      if (kind === 'flag' && cell !== 'true' && cell !== 'false') return null
      if (kind === 'identifier' && !guid(cell)) return null
      if (kind === 'number' && !/^-?\d+(?:\.\d+)?$/.test(cell)) return null
      if (kind === 'date' && !/^\d{4}-\d\d-\d\d$/.test(cell)) return null
      if (kind === 'instant' && !instant(cell)) return null
    }
  }
  return raw as unknown as ReportTable
}

function decodeSavedViewItem(raw: unknown, companyId: string): SavedView | null {
  if (!object(raw) || !exact(raw, ['id', 'companyId', 'reportId', 'name', 'parameters', 'columns', 'version', 'isActive', 'createdAtUtc', 'updatedAtUtc'])
    || !guid(raw.id) || raw.companyId !== companyId || !wireId(raw.reportId) || !text(raw.name, 80) || (raw.name as string).trim().length === 0
    || !stringMap(raw.parameters) || !strings(raw.columns, wireId) || (raw.columns as string[]).length === 0 || !version(raw.version)
    || typeof raw.isActive !== 'boolean' || !instant(raw.createdAtUtc) || !instant(raw.updatedAtUtc)) return null
  return raw as unknown as SavedView
}

export const decodeSavedView = (companyId: string, reportId: string) => (raw: unknown): SavedView | null => {
  const view = decodeSavedViewItem(raw, companyId)
  return view !== null && view.reportId === reportId ? view : null
}

export function decodeSavedViews(raw: unknown, companyId: string, reportId: string): SavedViewPage | null {
  if (!object(raw) || !exact(raw, ['companyId', 'reportId', 'items']) || raw.companyId !== companyId || raw.reportId !== reportId || !Array.isArray(raw.items)) return null
  const items = raw.items.map((item) => decodeSavedViewItem(item, companyId))
  if (items.some((item) => item === null || item.reportId !== reportId || !item.isActive)) return null
  return { companyId, reportId, items: items as SavedView[] }
}

/** The stable `code` of a problem body, or null for a code-less problem. */
export function problemCodeOf(raw: unknown): string | null {
  return object(raw) && typeof raw.code === 'string' && /^[a-z_]{1,80}$/.test(raw.code) ? raw.code : null
}
