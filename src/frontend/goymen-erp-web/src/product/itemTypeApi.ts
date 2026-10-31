/**
 * SAT-D134 (EK19) — the product item type (demir / hurda / hizmet), company-scoped, pinned to
 * `GET/PUT /api/v1/product-item-types/{productId}` (`ProductItemTypeEndpoints`). Reads send the selected company only; the
 * command carries one caller-owned `Idempotency-Key` (kept for a retry after an unknown outcome). Success is exactly the
 * contract's status; a 5xx or an off-contract answer is an UNKNOWN outcome; a refusal is read by its status and `code`.
 */

export const PRODUCT_ITEM_TYPES = ['Steel', 'Scrap', 'Service'] as const
export type ProductItemType = (typeof PRODUCT_ITEM_TYPES)[number]

export const ITEM_TYPE_PROBLEM = {
  versionConflict: 'product_item_type_version_conflict',
  reasonRequired: 'product_item_type_reason_required',
  idempotencyConflict: 'product_item_type_idempotency_conflict',
} as const

export interface ProductItemTypeView {
  readonly productId: string
  readonly version: string
  readonly itemType: ProductItemType | null
}

export type ItemTypeRead =
  | { readonly type: 'ok'; readonly data: ProductItemTypeView }
  | { readonly type: 'forbidden' }
  | { readonly type: 'failed' }

export type ItemTypeCommand =
  | { readonly type: 'applied' }
  | { readonly type: 'refused'; readonly status: number; readonly code: string | null }
  | { readonly type: 'unknown' }

const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED = import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

function headers(companyId: string): Record<string, string> {
  return { ...(DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}), 'X-Company-Id': companyId }
}

export function itemTypePath(productId: string): string {
  return `/product-item-types/${encodeURIComponent(productId)}`
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

export function decodeItemType(raw: unknown, companyId: string, productId: string): ProductItemTypeView | null {
  if (!isRecord(raw) || raw.companyId !== companyId || raw.productId !== productId || typeof raw.version !== 'string' || !/^(0|[1-9]\d*)$/.test(raw.version)) {
    return null
  }
  const itemType = raw.itemType
  if (itemType !== null && !PRODUCT_ITEM_TYPES.includes(itemType as ProductItemType)) return null
  if ((raw.version === '0') !== (itemType === null)) return null
  return { productId, version: raw.version, itemType: itemType as ProductItemType | null }
}

export async function fetchItemType(companyId: string, productId: string, signal?: AbortSignal): Promise<ItemTypeRead> {
  try {
    const response = await fetch(`${API_BASE}${itemTypePath(productId)}`, { headers: headers(companyId), signal })
    if (response.status === 401 || response.status === 403) return { type: 'forbidden' }
    if (response.status !== 200) return { type: 'failed' }
    const data = decodeItemType(await response.json(), companyId, productId)
    return data === null ? { type: 'failed' } : { type: 'ok', data }
  } catch {
    return { type: 'failed' }
  }
}

export async function setItemType(
  companyId: string,
  idempotencyKey: string,
  productId: string,
  body: { readonly expectedVersion: string; readonly itemType: ProductItemType; readonly reason: string },
): Promise<ItemTypeCommand> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}${itemTypePath(productId)}`, {
      method: 'PUT',
      headers: { ...headers(companyId), 'Content-Type': 'application/json', 'Idempotency-Key': idempotencyKey },
      body: JSON.stringify(body),
    })
  } catch {
    return { type: 'unknown' }
  }
  if (response.status === 204) return { type: 'applied' }
  if (response.status >= 500 || response.status < 400) return { type: 'unknown' }
  let raw: unknown = null
  try {
    raw = await response.json()
  } catch {
    raw = null
  }
  const code = isRecord(raw) && typeof raw.code === 'string' && raw.code.length > 0 ? raw.code : null
  return { type: 'refused', status: response.status, code }
}
