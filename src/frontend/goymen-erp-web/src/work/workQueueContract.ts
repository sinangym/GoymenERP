/**
 * B-25 Work queue — `GET /api/v1/work/queue`: the pending approvals of every connected source the signed-in actor may
 * see in the committed company, oldest first, in one list (native approval queue contract v1). Each source is the
 * owning module's own read (`INativeApprovalQueueAdapterV1`); the owner decides who sees a record and owns every
 * decision, so each item only names its source and links to the owner's own screen.
 *
 * The answer is judged whole, by the same rules the server applies to each source: exact members, ids that are real
 * GUIDs, UTC instants, `{module}.{subject}.v{n}` kinds owned by their module, links that are relative application
 * paths, and every item belonging to a source listed as readable. Anything else is not this contract and is refused.
 */

export const WORK_QUEUE_PATH = '/work/queue'

export const QUEUE_SOURCE_READY = 'Ready'
export const QUEUE_SOURCE_UNAVAILABLE = 'Unavailable'
export type WorkQueueSourceState = typeof QUEUE_SOURCE_READY | typeof QUEUE_SOURCE_UNAVAILABLE

/** The server's own bounds (`NativeApprovalQueueRulesV1`). */
export const QUEUE_MAX_MODULE_LENGTH = 64
export const QUEUE_MAX_KIND_LENGTH = 160
export const QUEUE_MAX_LINK_LENGTH = 512
export const QUEUE_MAX_ITEMS_PER_SOURCE = 50

export interface WorkQueueItem {
  readonly module: string
  readonly itemId: string
  readonly kind: string
  readonly createdAtUtc: string
  /** The owner's own screen for this record: a relative application path, already verified. */
  readonly link: string
}

export interface WorkQueueSource {
  readonly module: string
  readonly kind: string
  readonly state: WorkQueueSourceState
  /** The owner holds more pending records than the queue shows; its own screen lists them all. */
  readonly hasMore: boolean
}

export interface WorkQueue {
  readonly companyId: string
  readonly items: readonly WorkQueueItem[]
  readonly sources: readonly WorkQueueSource[]
}

type Obj = Record<string, unknown>

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const EMPTY_GUID = '00000000-0000-0000-0000-000000000000'
/** A UTC instant: `Z` or a zero offset, as the server writes a `DateTimeOffset` of offset zero. */
const UTC_INSTANT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|\+00:00)$/
const MODULE = /^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$/
const KIND = /^[a-z][a-z0-9]*(?:-[a-z0-9]+)*(?:\.[a-z0-9]+(?:-[a-z0-9]+)*)+\.v[1-9][0-9]*$/
const LINK = /^\/(?!\/)[A-Za-z0-9\-._~/%?=&+]*$/
const PERCENT_BYTE = /%([0-9A-Fa-f]{2})/g

const ROOT_KEYS = ['companyId', 'items', 'sources'] as const
const ITEM_KEYS = ['module', 'itemId', 'kind', 'createdAtUtc', 'link'] as const
const SOURCE_KEYS = ['module', 'kind', 'state', 'hasMore'] as const

function isObject(value: unknown): value is Obj {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function exact(value: Obj, keys: readonly string[]): boolean {
  const own = Object.keys(value)
  return own.length === keys.length && keys.every((key) => Object.hasOwn(value, key))
}

const isId = (value: unknown): value is string => typeof value === 'string' && GUID.test(value) && value !== EMPTY_GUID
const isUtcInstant = (value: unknown): value is string =>
  typeof value === 'string' && UTC_INSTANT.test(value) && !Number.isNaN(Date.parse(value))

export const isQueueModule = (value: unknown): value is string =>
  typeof value === 'string' && value.length > 0 && value.length <= QUEUE_MAX_MODULE_LENGTH && MODULE.test(value)

/** A kind is `{module}.{subject}.v{n}`: lowercase, owned by the module it names. */
export const isQueueKind = (module: unknown, kind: unknown): kind is string =>
  isQueueModule(module)
  && typeof kind === 'string'
  && kind.length > 0
  && kind.length <= QUEUE_MAX_KIND_LENGTH
  && KIND.test(kind)
  && kind.startsWith(`${module}.`)

/**
 * A link is a relative application path: one leading slash, no scheme or host, no backslash, no "//" anywhere, no
 * fragment, no "." or ".." segment (also percent-encoded), only URL-safe characters. Each path segment is judged
 * decoded byte by byte, like the server: "%2e%2e" is "..", and "%2f" or "%5c" would smuggle a separator.
 */
export function isQueueLink(value: unknown): value is string {
  if (typeof value !== 'string' || value.length === 0 || value.length > QUEUE_MAX_LINK_LENGTH) return false
  if (!LINK.test(value) || value.includes('//')) return false
  const queryStart = value.indexOf('?')
  const path = queryStart < 0 ? value : value.slice(0, queryStart)
  return path.split('/').every((segment) => {
    const decoded = segment.replace(PERCENT_BYTE, (_, hex: string) => String.fromCharCode(Number.parseInt(hex, 16)))
    return decoded !== '.' && decoded !== '..' && !decoded.includes('/') && !decoded.includes('\\')
  })
}

function decodeSource(value: unknown): WorkQueueSource | null {
  if (!isObject(value) || !exact(value, SOURCE_KEYS)) return null
  const { module, kind, state, hasMore } = value
  if (!isQueueKind(module, kind) || typeof hasMore !== 'boolean') return null
  if (state === QUEUE_SOURCE_READY) return { module: module as string, kind, state, hasMore }
  // An unavailable source shows nothing and claims nothing more.
  if (state === QUEUE_SOURCE_UNAVAILABLE && !hasMore) return { module: module as string, kind, state, hasMore }
  return null
}

function decodeItem(value: unknown): WorkQueueItem | null {
  if (!isObject(value) || !exact(value, ITEM_KEYS)) return null
  const { module, itemId, kind, createdAtUtc, link } = value
  if (!isQueueKind(module, kind) || !isId(itemId) || !isUtcInstant(createdAtUtc) || !isQueueLink(link)) return null
  return { module: module as string, itemId: itemId.toLowerCase(), kind, createdAtUtc, link }
}

/**
 * The whole queue, or null. Sources are unique by kind; every item belongs to a source listed as `Ready` with the same
 * module, an item id appears once per kind, and no source shows more than the server's bound.
 */
export function decodeWorkQueue(body: unknown): WorkQueue | null {
  if (!isObject(body) || !exact(body, ROOT_KEYS)) return null
  const { companyId, items, sources } = body
  if (!isId(companyId) || !Array.isArray(items) || !Array.isArray(sources)) return null

  const sourceByKind = new Map<string, WorkQueueSource>()
  for (const raw of sources) {
    const source = decodeSource(raw)
    if (source === null || sourceByKind.has(source.kind)) return null
    sourceByKind.set(source.kind, source)
  }

  const decoded: WorkQueueItem[] = []
  const seen = new Set<string>()
  const perKind = new Map<string, number>()
  for (const raw of items) {
    const item = decodeItem(raw)
    if (item === null) return null
    const source = sourceByKind.get(item.kind)
    if (source === undefined || source.state !== QUEUE_SOURCE_READY || source.module !== item.module) return null
    const key = `${item.kind}::${item.itemId}`
    if (seen.has(key)) return null
    seen.add(key)
    const count = (perKind.get(item.kind) ?? 0) + 1
    if (count > QUEUE_MAX_ITEMS_PER_SOURCE) return null
    perKind.set(item.kind, count)
    decoded.push(item)
  }

  return { companyId: companyId.toLowerCase(), items: decoded, sources: [...sourceByKind.values()] }
}

/** The answer is of the company that was asked for (a tenant-binding breach is not data). */
export const belongsToCompany = (queue: WorkQueue, companyId: string): boolean =>
  queue.companyId === companyId.toLowerCase()
