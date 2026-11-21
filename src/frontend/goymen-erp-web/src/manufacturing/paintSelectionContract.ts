export interface PlanPaintSelection {
  readonly productId: string
  readonly productVersion: string
  readonly membershipVersion: string
  readonly productSourceVersion: string
  readonly supplierPartyId: string
  readonly supplierCompanyAccountId: string
  readonly supplierPartyVersion: string
  readonly supplierAccountVersion: string
  readonly colorId: string | null
  readonly colorVersion: string | null
}
export interface PaintProduct { readonly productId: string; readonly productVersion: string; readonly membershipVersion: string; readonly sourceVersion: string; readonly code: string; readonly name: string }
export interface PaintSupplier { readonly partyId: string; readonly companyAccountId: string; readonly partyVersion: string; readonly accountVersion: string; readonly code: string; readonly name: string }
export interface PaintColor { readonly id: string; readonly version: string; readonly code: string; readonly name: string }
export interface PaintCatalog { readonly companyId: string; readonly products: readonly PaintProduct[]; readonly suppliers: readonly PaintSupplier[]; readonly colors: readonly PaintColor[]; readonly supplierSearchWindowFull: boolean }
type Json = Record<string, unknown>
const object = (v: unknown): v is Json => typeof v === 'object' && v !== null && !Array.isArray(v)
const guid = (v: unknown): v is string => typeof v === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(v) && !/^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(v)
const text = (v: unknown): v is string => typeof v === 'string' && v.length > 0 && v.length <= 256 && v.trim() === v && !/\p{Cc}/u.test(v)
const keys = (v: Json, names: readonly string[]) => Object.keys(v).length === names.length && names.every(k => Object.hasOwn(v, k))
const selectionKeys = ['productId', 'productVersion', 'membershipVersion', 'productSourceVersion', 'supplierPartyId', 'supplierCompanyAccountId', 'supplierPartyVersion', 'supplierAccountVersion', 'colorId', 'colorVersion'] as const
export function decodePaintSelection(v: unknown): PlanPaintSelection | undefined {
  if (!object(v) || !keys(v, selectionKeys) || !guid(v.productId) || !guid(v.supplierPartyId) || !guid(v.supplierCompanyAccountId)
    || !['productVersion', 'membershipVersion', 'productSourceVersion', 'supplierPartyVersion', 'supplierAccountVersion'].every(k => text(v[k]))
    || !(v.colorId === null && v.colorVersion === null || guid(v.colorId) && text(v.colorVersion))) return undefined
  return v as unknown as PlanPaintSelection
}
export function samePaintSelection(a: PlanPaintSelection | undefined, b: PlanPaintSelection | undefined): boolean {
  return a === undefined || b === undefined ? a === b : selectionKeys.every(k => a[k] === b[k])
}
export function decodePaintCatalog(v: unknown, companyId: string): PaintCatalog | undefined {
  if (!object(v) || !keys(v, ['companyId', 'products', 'suppliers', 'colors', 'supplierSearchWindowFull']) || v.companyId !== companyId
    || !Array.isArray(v.products) || !Array.isArray(v.suppliers) || !Array.isArray(v.colors) || typeof v.supplierSearchWindowFull !== 'boolean') return undefined
  if (!v.products.every(p => object(p) && keys(p, ['productId', 'productVersion', 'membershipVersion', 'sourceVersion', 'code', 'name'])
    && guid(p.productId) && ['productVersion', 'membershipVersion', 'sourceVersion', 'code', 'name'].every(k => text(p[k])))
    || !v.suppliers.every(p => object(p) && keys(p, ['partyId', 'companyAccountId', 'partyVersion', 'accountVersion', 'code', 'name'])
      && guid(p.partyId) && guid(p.companyAccountId) && ['partyVersion', 'accountVersion', 'code', 'name'].every(k => text(p[k])))
    || !v.colors.every(p => object(p) && keys(p, ['id', 'version', 'code', 'name']) && guid(p.id) && ['version', 'code', 'name'].every(k => text(p[k])))) return undefined
  if (new Set(v.products.map(p => p.productId.toLowerCase())).size !== v.products.length
    || new Set(v.suppliers.map(p => `${p.partyId}/${p.companyAccountId}`.toLowerCase())).size !== v.suppliers.length
    || new Set(v.colors.map(p => p.id.toLowerCase())).size !== v.colors.length) return undefined
  return v as unknown as PaintCatalog
}
