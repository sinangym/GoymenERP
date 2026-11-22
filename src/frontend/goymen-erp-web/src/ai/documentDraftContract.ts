/**
 * WP-B3 — belgeden taslak wire contract.
 *
 *   POST /api/v1/ai/document-drafts → AiDocumentDraftResponseV1
 *
 * The request carries only the draft kind and the document (a file as base64 with its media type, or a pasted text with
 * its source). No company, actor, recipient, permission or approval member exists: the company is request context and
 * nothing in a document can widen what happens. The decoder takes EXACTLY the contract's members and fails closed
 * (null → `malformed`), and `createsRecord` must be false: a draft never creates a record.
 */

export const AI_DOCUMENT_DRAFT_PATH = '/ai/document-drafts'

/** Navigation state key the owner screen reads to prefill its form (the owner revalidates everything). */
export const AI_DOCUMENT_DRAFT_STATE_KEY = 'aiDocumentDraft'

export const DRAFT_KINDS = ['sales-order', 'supplier-confirmation', 'incoming-invoice'] as const
export type DraftKind = (typeof DRAFT_KINDS)[number]

export const DRAFT_MEDIA_TYPES = [
  'application/xml',
  'text/xml',
  'text/csv',
  'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  'application/pdf',
  'image/png',
  'image/jpeg',
  'image/tiff',
] as const
export type DraftMediaType = (typeof DRAFT_MEDIA_TYPES)[number]

export const DRAFT_TEXT_SOURCES = ['email', 'pasted'] as const
export type DraftTextSource = (typeof DRAFT_TEXT_SOURCES)[number]

export const DRAFT_READING_TIERS = ['structured-file', 'pdf-text', 'ocr', 'pasted-text', 'local-model'] as const
export type DraftReadingTier = (typeof DRAFT_READING_TIERS)[number]

export const DRAFT_FIELD_STATUSES = ['verified', 'unverified', 'invalid'] as const
export type DraftFieldStatus = (typeof DRAFT_FIELD_STATUSES)[number]

/** Stable problem codes of POST /ai/document-drafts (AiEndpoints). */
export const DRAFT_PROBLEM_CODE = {
  disabled: 'ai_document_drafts_disabled',
  invalid: 'ai_document_draft_invalid',
  unreadable: 'ai_document_unreadable',
} as const

export const DRAFT_INVALID_FIELDS = ['draftKind', 'mediaType', 'content', 'text', 'textSource'] as const
export type DraftInvalidField = (typeof DRAFT_INVALID_FIELDS)[number]

/** The owner screen codes the server names; the UI maps each to a shell route and never invents one. */
export const DRAFT_SCREEN_CODES = ['sales.drafts.create', 'purchasing.invoices.import', 'purchasing.orders.list'] as const
export type DraftScreenCode = (typeof DRAFT_SCREEN_CODES)[number]

export type DraftRequest =
  | { readonly draftKind: DraftKind; readonly mediaType: DraftMediaType; readonly contentBase64: string }
  | { readonly draftKind: DraftKind; readonly text: string; readonly textSource: DraftTextSource }

export interface DraftField {
  readonly code: string
  readonly value: string | null
  readonly status: DraftFieldStatus
  readonly reasonCode: string | null
  readonly source: DraftReadingTier
}

export interface DraftCounterparty {
  readonly taxId: DraftField
  readonly name: DraftField
  readonly matchedPartyId: string | null
}

export interface DraftLine {
  readonly lineNumber: number
  readonly productCode: DraftField
  readonly description: DraftField
  readonly quantity: DraftField
  readonly unit: DraftField
  readonly unitPrice: DraftField
  readonly lineAmount: DraftField
  readonly section: DraftField
  readonly wallThickness: DraftField
  readonly length: DraftField
  readonly matchedProductId: string | null
  readonly matchedProductVersion: string | null
}

export interface DocumentDraft {
  readonly draftKind: DraftKind
  readonly readingTier: DraftReadingTier
  readonly source: {
    readonly sha256: string
    readonly mediaType: string
    readonly byteLength: number
    /** AI-D40: kept as long as a record opened from the draft; otherwise passive after `passivateAfterUtc`. */
    readonly retentionDecision: string
    readonly storedAtUtc: string
    readonly passivateAfterUtc: string
  }
  readonly quarantineFindings: readonly string[]
  readonly documentNumber: DraftField
  readonly documentDate: DraftField
  readonly currency: DraftField
  readonly counterparty: DraftCounterparty
  readonly lines: readonly DraftLine[]
  readonly payableAmount: DraftField
  readonly openIn: { readonly ownerModule: string; readonly screenCode: DraftScreenCode }
  readonly createsRecord: false
}

const DRAFT_KEYS = [
  'draftKind', 'readingTier', 'source', 'quarantineFindings', 'documentNumber', 'documentDate', 'currency',
  'counterparty', 'lines', 'payableAmount', 'openIn', 'createsRecord',
] as const
const FIELD_KEYS = ['code', 'value', 'status', 'reasonCode', 'source'] as const
const COUNTERPARTY_KEYS = ['taxId', 'name', 'matchedPartyId'] as const
const LINE_KEYS = [
  'lineNumber', 'productCode', 'description', 'quantity', 'unit', 'unitPrice', 'lineAmount', 'section',
  'wallThickness', 'length', 'matchedProductId', 'matchedProductVersion',
] as const
const SOURCE_KEYS = ['sha256', 'mediaType', 'byteLength', 'retentionDecision', 'storedAtUtc', 'passivateAfterUtc'] as const
const UTC_INSTANT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|\+00:00)$/
const OPEN_IN_KEYS = ['ownerModule', 'screenCode'] as const
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const SHA256 = /^[0-9a-f]{64}$/
const CODE = /^[a-z][a-z0-9_]{0,63}$/

type Obj = Record<string, unknown>

function exact(value: unknown, keys: readonly string[]): Obj | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const members = Object.keys(value)
  if (members.length !== keys.length || !members.every((member) => keys.includes(member))) return null
  return value as Obj
}

const isText = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0
const oneOf = <T extends string>(list: readonly T[], value: unknown): value is T =>
  typeof value === 'string' && (list as readonly string[]).includes(value)
const isNullableUuid = (value: unknown): value is string | null => value === null || (typeof value === 'string' && UUID.test(value))

function decodeField(value: unknown, expectedCode: string): DraftField | null {
  const field = exact(value, FIELD_KEYS)
  if (field === null || field.code !== expectedCode) return null
  if (field.value !== null && typeof field.value !== 'string') return null
  if (!oneOf(DRAFT_FIELD_STATUSES, field.status) || !oneOf(DRAFT_READING_TIERS, field.source)) return null
  if (field.reasonCode !== null && (typeof field.reasonCode !== 'string' || !CODE.test(field.reasonCode))) return null
  // A verified field carries a value and no reason; an invalid one always names its reason.
  if (field.status === 'verified' && (field.value === null || field.reasonCode !== null)) return null
  if (field.status === 'invalid' && field.reasonCode === null) return null
  return {
    code: field.code,
    value: field.value as string | null,
    status: field.status,
    reasonCode: field.reasonCode as string | null,
    source: field.source,
  }
}

function decodeLine(value: unknown): DraftLine | null {
  const line = exact(value, LINE_KEYS)
  if (line === null || typeof line.lineNumber !== 'number' || !Number.isInteger(line.lineNumber) || line.lineNumber < 1) return null
  const fields = {
    productCode: decodeField(line.productCode, 'productCode'),
    description: decodeField(line.description, 'description'),
    quantity: decodeField(line.quantity, 'quantity'),
    unit: decodeField(line.unit, 'unit'),
    unitPrice: decodeField(line.unitPrice, 'unitPrice'),
    lineAmount: decodeField(line.lineAmount, 'lineAmount'),
    section: decodeField(line.section, 'section'),
    wallThickness: decodeField(line.wallThickness, 'wallThickness'),
    length: decodeField(line.length, 'length'),
  }
  if (Object.values(fields).some((field) => field === null)) return null
  if (!isNullableUuid(line.matchedProductId)) return null
  if (line.matchedProductVersion !== null && !isText(line.matchedProductVersion)) return null
  if ((line.matchedProductId === null) !== (line.matchedProductVersion === null)) return null
  return {
    lineNumber: line.lineNumber,
    ...(fields as { [K in keyof typeof fields]: DraftField }),
    matchedProductId: line.matchedProductId,
    matchedProductVersion: line.matchedProductVersion as string | null,
  }
}

export function decodeDocumentDraft(value: unknown): DocumentDraft | null {
  const body = exact(value, DRAFT_KEYS)
  if (body === null || !oneOf(DRAFT_KINDS, body.draftKind) || !oneOf(DRAFT_READING_TIERS, body.readingTier)) return null
  if (body.createsRecord !== false) return null
  const source = exact(body.source, SOURCE_KEYS)
  if (source === null || typeof source.sha256 !== 'string' || !SHA256.test(source.sha256) || !isText(source.mediaType)) return null
  if (typeof source.byteLength !== 'number' || !Number.isInteger(source.byteLength) || source.byteLength < 1) return null
  if (!isText(source.retentionDecision)) return null
  if (typeof source.storedAtUtc !== 'string' || !UTC_INSTANT.test(source.storedAtUtc)) return null
  if (typeof source.passivateAfterUtc !== 'string' || !UTC_INSTANT.test(source.passivateAfterUtc)) return null
  if (Date.parse(source.passivateAfterUtc) <= Date.parse(source.storedAtUtc)) return null
  if (!Array.isArray(body.quarantineFindings)) return null
  const findings = body.quarantineFindings as unknown[]
  if (!findings.every((finding) => typeof finding === 'string' && CODE.test(finding))) return null
  const openIn = exact(body.openIn, OPEN_IN_KEYS)
  if (openIn === null || !isText(openIn.ownerModule) || !oneOf(DRAFT_SCREEN_CODES, openIn.screenCode)) return null
  const counterparty = exact(body.counterparty, COUNTERPARTY_KEYS)
  if (counterparty === null || !isNullableUuid(counterparty.matchedPartyId)) return null
  const taxId = decodeField(counterparty.taxId, 'counterpartyTaxId')
  const name = decodeField(counterparty.name, 'counterpartyName')
  const documentNumber = decodeField(body.documentNumber, 'documentNumber')
  const documentDate = decodeField(body.documentDate, 'documentDate')
  const currency = decodeField(body.currency, 'currency')
  const payableAmount = decodeField(body.payableAmount, 'payableAmount')
  if (taxId === null || name === null || documentNumber === null || documentDate === null || currency === null || payableAmount === null) {
    return null
  }
  if (!Array.isArray(body.lines)) return null
  const lines: DraftLine[] = []
  for (const entry of body.lines as unknown[]) {
    const line = decodeLine(entry)
    if (line === null || line.lineNumber !== lines.length + 1) return null
    lines.push(line)
  }
  return {
    draftKind: body.draftKind,
    readingTier: body.readingTier,
    source: {
      sha256: source.sha256,
      mediaType: source.mediaType,
      byteLength: source.byteLength,
      retentionDecision: source.retentionDecision,
      storedAtUtc: source.storedAtUtc,
      passivateAfterUtc: source.passivateAfterUtc,
    },
    quarantineFindings: findings as string[],
    documentNumber,
    documentDate,
    currency,
    counterparty: { taxId, name, matchedPartyId: counterparty.matchedPartyId },
    lines,
    payableAmount,
    openIn: { ownerModule: openIn.ownerModule, screenCode: openIn.screenCode },
    createsRecord: false,
  }
}

/** The known `invalidFields` of an invalid-draft problem; unknown names are dropped, never shown. */
export function draftInvalidFields(body: unknown): readonly DraftInvalidField[] {
  if (typeof body !== 'object' || body === null || Array.isArray(body)) return []
  const fields = (body as Obj).invalidFields
  if (!Array.isArray(fields)) return []
  return DRAFT_INVALID_FIELDS.filter((field) => (fields as unknown[]).includes(field))
}

/** The reason codes of an unreadable-document problem (stable codes only; never file content). */
export function draftUnreadableReasons(body: unknown): readonly string[] {
  if (typeof body !== 'object' || body === null || Array.isArray(body)) return []
  const reasons = (body as Obj).invalidFields
  if (!Array.isArray(reasons)) return []
  return (reasons as unknown[]).filter((reason): reason is string => typeof reason === 'string' && CODE.test(reason))
}

/** Maps a picked file to an accepted media type by its declared type or extension; null when not accepted. */
export function mediaTypeOf(file: { readonly name: string; readonly type: string }): DraftMediaType | null {
  if (oneOf(DRAFT_MEDIA_TYPES, file.type)) return file.type
  const extension = file.name.toLowerCase().split('.').pop() ?? ''
  const byExtension: Record<string, DraftMediaType> = {
    xml: 'application/xml',
    csv: 'text/csv',
    xlsx: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    pdf: 'application/pdf',
    png: 'image/png',
    jpg: 'image/jpeg',
    jpeg: 'image/jpeg',
    tif: 'image/tiff',
    tiff: 'image/tiff',
  }
  return byExtension[extension] ?? null
}
