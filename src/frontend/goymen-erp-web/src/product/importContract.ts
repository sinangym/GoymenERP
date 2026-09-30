export interface ProductImportColumnMapping {
  readonly legacyCode: string
  readonly name: string
  readonly groupId: string
}

export interface ProductImportSourceRow {
  readonly rowNumber: number
  readonly legacyCode: string
  readonly name: string
  readonly groupId: string
}

export interface ProductImportError {
  readonly rowNumber: number | null
  readonly field: string
  readonly code: string
  readonly message: string
}

export interface ProductImportPreview {
  readonly fileSha256: string
  readonly previewToken: string
  readonly sourceRowCount: number
  readonly rows: readonly ProductImportSourceRow[]
  readonly errors: readonly ProductImportError[]
  readonly ready: boolean
}

export interface ProductImportCreated {
  readonly productId: string
  readonly code: string
  readonly legacyCode: string
  readonly name: string
  readonly groupId: string
}

export interface ProductImportResult {
  readonly products: readonly ProductImportCreated[]
  readonly isReplay: boolean
  readonly originalCorrelationId: string
}

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const SHA256 = /^[0-9A-F]{64}$/
const MAXIMUM_IMPORT_ROWS = 10_000
const FIRST_REJECTED_ROW_COUNT = MAXIMUM_IMPORT_ROWS + 1

function object(value: unknown): Record<string, unknown> | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null
}

function text(value: unknown): string | null {
  return typeof value === 'string' && value.trim().length > 0 ? value : null
}

function uuid(value: unknown): string | null {
  const candidate = text(value)
  return candidate !== null && UUID.test(candidate) && candidate !== '00000000-0000-0000-0000-000000000000'
    ? candidate
    : null
}

export function decodeProductImportPreview(value: unknown): ProductImportPreview | null {
  const raw = object(value)
  if (raw === null || typeof raw.fileSha256 !== 'string' || !SHA256.test(raw.fileSha256)
    || typeof raw.previewToken !== 'string' || !SHA256.test(raw.previewToken)
    || typeof raw.sourceRowCount !== 'number' || !Number.isInteger(raw.sourceRowCount) || raw.sourceRowCount < 0
    || raw.sourceRowCount > FIRST_REJECTED_ROW_COUNT || !Array.isArray(raw.rows) || !Array.isArray(raw.errors)
    || typeof raw.ready !== 'boolean') return null

  const rows: ProductImportSourceRow[] = []
  for (const valueRow of raw.rows) {
    const row = object(valueRow)
    if (row === null || typeof row.rowNumber !== 'number' || !Number.isInteger(row.rowNumber) || row.rowNumber < 2
      || text(row.legacyCode) === null || text(row.name) === null || uuid(row.groupId) === null) return null
    rows.push({
      rowNumber: Number(row.rowNumber),
      legacyCode: row.legacyCode as string,
      name: row.name as string,
      groupId: row.groupId as string,
    })
  }

  const errors: ProductImportError[] = []
  for (const valueError of raw.errors) {
    const error = object(valueError)
    if (error === null || (error.rowNumber !== null
      && (typeof error.rowNumber !== 'number' || !Number.isInteger(error.rowNumber) || error.rowNumber < 1))
      || text(error.field) === null || text(error.code) === null || text(error.message) === null) return null
    errors.push({
      rowNumber: error.rowNumber === null ? null : Number(error.rowNumber),
      field: error.field as string,
      code: error.code as string,
      message: error.message as string,
    })
  }

  const ready = raw.ready
  if (Number(raw.sourceRowCount) === FIRST_REJECTED_ROW_COUNT) {
    const tooManyRows = errors.length === 1 && errors[0]?.rowNumber === null
      && errors[0]?.field === 'workbook' && errors[0]?.code === 'product_import_too_many_rows'
    if (ready || rows.length !== 0 || !tooManyRows) return null
  }
  if (ready && (Number(raw.sourceRowCount) === 0 || errors.length > 0
    || rows.length !== Number(raw.sourceRowCount))) return null
  return {
    fileSha256: raw.fileSha256 as string,
    previewToken: raw.previewToken as string,
    sourceRowCount: Number(raw.sourceRowCount),
    rows,
    errors,
    ready,
  }
}

export function decodeProductImportResult(value: unknown): ProductImportResult | null {
  const raw = object(value)
  if (raw === null || !Array.isArray(raw.products) || raw.products.length === 0 || raw.products.length > 10_000
    || typeof raw.isReplay !== 'boolean' || text(raw.originalCorrelationId) === null) return null
  const products: ProductImportCreated[] = []
  for (const valueProduct of raw.products) {
    const product = object(valueProduct)
    const productId = product === null ? null : uuid(product.productId)
    const groupId = product === null ? null : uuid(product.groupId)
    if (product === null || productId === null || groupId === null || text(product.code) === null
      || text(product.legacyCode) === null || text(product.name) === null) return null
    products.push({
      productId,
      groupId,
      code: product.code as string,
      legacyCode: product.legacyCode as string,
      name: product.name as string,
    })
  }
  if (new Set(products.map((product) => product.productId)).size !== products.length
    || new Set(products.map((product) => product.code)).size !== products.length
    || new Set(products.map((product) => product.legacyCode)).size !== products.length) return null
  return {
    products,
    isReplay: raw.isReplay,
    originalCorrelationId: raw.originalCorrelationId as string,
  }
}

export function productImportErrorsCsv(errors: readonly ProductImportError[]): Blob {
  const csv = ['row,field,code,message', ...errors
    .map((error) => [error.rowNumber ?? '', error.field, error.code, error.message]
      .map((cell) => `"${String(cell).replaceAll('"', '""')}"`).join(','))]
    .join('\r\n') + '\r\n'
  return new Blob([new Uint8Array([0xEF, 0xBB, 0xBF]), csv], { type: 'text/csv;charset=utf-8' })
}
