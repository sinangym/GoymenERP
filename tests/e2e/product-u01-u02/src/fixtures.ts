/**
 * Synthetic Product fixtures for the T lane (controlled responses).
 *
 * They mirror the component harness (`src/product/testHarness.tsx`), so what the
 * real browser is fed has exactly the wire shapes the pinned contract (`#237`
 * `cb75e2f`: `ProductPage`, `ProductCardView`, `ProductMutationResponse`,
 * `ProductCodeGroup`, `ProductReferenceOptions`) defines. Nothing here is real
 * data; every id is a synthetic UUID. The actor and company directory are the
 * shared ones of the Cari T lane, because the shell is the same shell.
 */
export { A_ID, B_ID, IDENTITY, DIRECTORY, SHELL_CAPABILITY_CONTRACT_VERSION } from '../../cari-acceptance-009/src/fixtures'

export const PRODUCT_1 = '11111111-1111-1111-1111-111111111111'
export const PRODUCT_2 = '22222222-2222-2222-2222-222222222222'
export const GROUP_SAC = 'aaaaaaa1-0000-0000-0000-000000000001'
export const CLASS_FLAT = 'cccccccc-0000-0000-0000-000000000001'
export const FAMILY_FLAT = 'bbbbbbb1-0000-0000-0000-000000000001'

/** The four Product capability ids — names only; the server publishes them, or not. */
export const PRODUCT_CAP = {
  list: 'product.items.list',
  detail: 'product.items.detail',
  create: 'product.items.create',
  edit: 'product.items.edit',
} as const
export const ALL_PRODUCT_CAPS: readonly string[] = [PRODUCT_CAP.list, PRODUCT_CAP.detail, PRODUCT_CAP.create, PRODUCT_CAP.edit]

export const PROBLEM = {
  validationFailed: 'validation_failed',
  invalidCursor: 'invalid_cursor',
  notFound: 'not_found',
  versionConflict: 'version_conflict',
  idempotencyConflict: 'idempotency_conflict',
  lifecycleConflict: 'lifecycle_conflict',
  forbidden: 'forbidden',
} as const

export function capabilitySnapshot(capabilities: readonly string[], generation = 'gen-1') {
  return { contractVersion: 'SHELL-CAPABILITY-V1', generation, capabilities }
}

export function codeGroups() {
  return [{ id: GROUP_SAC, name: 'Sac', prefix: 'SAC' }]
}

export function referenceOptions(over: Record<string, unknown> = {}) {
  return {
    groupId: GROUP_SAC, classId: null,
    classes: [{ id: CLASS_FLAT, displayName: 'Yassı ürün' }],
    families: [{ id: FAMILY_FLAT, displayName: 'Soğuk hadde' }],
    units: [], trackingPolicies: [], templates: [], unavailableCatalogs: [],
    ...over,
  }
}

export function roles(over: Record<string, unknown> = {}) {
  return {
    purchasable: { value: true, provenance: 'manual' },
    sellable: { value: false, provenance: 'manual' },
    productionInput: { value: null, provenance: 'not-evaluated' },
    productionOutput: { value: true, provenance: 'derived' },
    ...over,
  }
}

export function productCore(over: Record<string, unknown> = {}) {
  return {
    id: PRODUCT_1, code: 'SAC-42', name: 'Siyah sac',
    groupId: GROUP_SAC, classId: null, familyId: null,
    roles: roles(), lifecycle: 'Draft', version: '7',
    createdAtUtc: '2026-01-01T00:00:00+00:00', updatedAtUtc: '2026-09-12T07:00:00+00:00',
    labels: { group: 'Sac', class: null, family: null, primaryStockUnit: 'kg' },
    completeness: { evaluation: 'Incomplete', missingFields: ['references.units.stock'] },
    ...over,
  }
}

export function productPage(over: Record<string, unknown> = {}) {
  return { items: [productCore()], nextCursor: null, ...over }
}

export function references(over: Record<string, unknown> = {}) {
  return {
    templateId: null, templateVersion: null, attributes: null,
    units: { stock: null, purchase: null, sales: null, price: null, production: null, package: null },
    trackingPolicyId: null,
    ...over,
  }
}

export function productCard(over: Record<string, unknown> = {}) {
  const product = (over.product ?? productCore()) as Record<string, unknown>
  return { product, references: references(), completeness: product.completeness, ...over }
}

export function mutation(over: Record<string, unknown> = {}) {
  return { card: productCard(), isReplay: false, ...over }
}
