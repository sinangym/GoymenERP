/**
 * Product (Ürün) U-01/U-02 contract — runtime decoding and validation.
 *
 * ┌─ CONTRACT STATUS ──────────────────────────────────────────────────────────┐
 * │ PINNED. Aligned to the published Product backend contract at               │
 * │ `codex/urun-u01-u02-backend-20260912` @ `3a7f2f8`:                          │
 * │   src/backend/Modules/Product/README.md                                    │
 * │   …/Application/Contracts/ProductDraftContractsV1.cs                       │
 * │   …/Application/Contracts/IProductService.cs                               │
 * │   …/Api/ProductEndpoints.cs                                                │
 * │                                                                            │
 * │ Every name below now comes from that source, not from the business spec.   │
 * │ Where the two differ the SOURCE wins and the difference is noted; where     │
 * │ the source is silent nothing is invented — the field is simply absent.      │
 * │                                                                            │
 * │ What the pin does NOT mean: those routes are not live. The module is not    │
 * │ mapped into the host, there is no Product service implementation, no        │
 * │ DbContext and no SQL acceptance. The screens still mount only on a          │
 * │ server-published capability, and none is published.                        │
 * └────────────────────────────────────────────────────────────────────────────┘
 *
 * Why the decoders exist even so: a generated client gives compile-time types,
 * not runtime validation. A page that fails closed on an out-of-range lifecycle or
 * a role whose provenance contradicts its value is the half that survives the
 * generated-client move.
 */

import {
  isBoundUuid,
  isInstant,
  isLosslessVersion,
  isMachineToken,
  isNonEmptyString,
} from './wireGuards'

/**
 * Marks the module as aligned to a published backend source rather than to a
 * business-spec proposal. Asserted in the contract test so the provenance cannot be
 * lost silently.
 */
export const PRODUCT_CONTRACT_STATUS = 'PINNED' as const
export const PRODUCT_CONTRACT_SOURCE =
  'Product backend contract v1 (PR #237, 3a7f2f8): README.md + ProductDraftContractsV1.cs + IProductService.cs + ProductEndpoints.cs'

/* ── Lifecycle ────────────────────────────────────────────────────────────── */

/**
 * README: "Lifecycle wire values are exactly Draft/Active/Inactive."
 *
 * There is no `Deleted`: §6.4 keeps a used Product undeleted, and the source
 * enum has three members.
 */
export const PRODUCT_LIFECYCLE = ['Draft', 'Active', 'Inactive'] as const
export type ProductLifecycle = (typeof PRODUCT_LIFECYCLE)[number]

/**
 * A lifecycle as READ. Either one of the three states the source defines, or a
 * value this build does not know — carried verbatim, never guessed at.
 *
 * The vocabulary is exactly the three strings above. What this type changes is the
 * COST of an out-of-vocabulary value: rejecting the whole page for one unrecognised
 * token means a single odd row hides every valid one behind "Beklenmeyen yanıt",
 * which tells the operator nothing. A row that says its status is unknown is safer
 * and a far louder signal to whoever can reconcile the contract.
 *
 * Strictness stays on the WRITE side: `decodeProductMutation` still rejects a
 * create whose result is not exactly `Draft`.
 */
export type ProductLifecycleValue =
  | { readonly known: true; readonly value: ProductLifecycle }
  | { readonly known: false; readonly raw: string }

function decodeLifecycle(value: unknown): ProductLifecycleValue | null {
  // Absent, blank, unprintable or non-string is fatal: that is a missing or broken
  // field, not an unfamiliar one, and there is nothing safe to carry.
  if (!isMachineToken(value, 64)) return null
  return (PRODUCT_LIFECYCLE as readonly string[]).includes(value)
    ? { known: true, value: value as ProductLifecycle }
    : { known: false, raw: value }
}

/** The state when it is one this build understands, else null. */
export function knownLifecycle(lifecycle: ProductLifecycleValue): ProductLifecycle | null {
  return lifecycle.known ? lifecycle.value : null
}

/* ── Roles and provenance ─────────────────────────────────────────────────── */

/**
 * `ProductRolesView` keys, in source order. DEC-PRD-001 and AT-015: independent of
 * group/class/family, and one Product may carry several at once.
 */
export const PRODUCT_ROLE = [
  'purchasable',
  'sellable',
  'productionInput',
  'productionOutput',
] as const
export type ProductRoleName = (typeof PRODUCT_ROLE)[number]

/**
 * `ProductRoleView.Provenance` — the source emits exactly these three strings.
 *
 * `not-evaluated` is the point of the type. README: "Not-evaluated requires null.
 * Explicit false is preserved." Collapsing provenance into a plain boolean would
 * render an undetermined role as "not purchasable", which is a claim nobody made.
 */
export const ROLE_PROVENANCE = ['manual', 'derived', 'not-evaluated'] as const
export type RoleProvenance = (typeof ROLE_PROVENANCE)[number]

export interface ProductRoleState {
  /** `null` if and only if `provenance === 'not-evaluated'`. Enforced by the decoder. */
  readonly value: boolean | null
  readonly provenance: RoleProvenance
}

export type ProductRoles = Readonly<Record<ProductRoleName, ProductRoleState>>

/**
 * Decode one role. Fails closed on the pairing in BOTH directions: a `not-evaluated`
 * role carrying a boolean is a server that persisted a value it said it did not
 * have, and a determined role carrying `null` is a value that went missing.
 */
function decodeRole(raw: unknown): ProductRoleState | null {
  if (typeof raw !== 'object' || raw === null) return null
  const r = raw as Record<string, unknown>
  const provenance = r.provenance
  if (typeof provenance !== 'string') return null
  if (!(ROLE_PROVENANCE as readonly string[]).includes(provenance)) return null
  if (provenance === 'not-evaluated') {
    return r.value === null ? { value: null, provenance: 'not-evaluated' } : null
  }
  if (typeof r.value !== 'boolean') return null
  return { value: r.value, provenance: provenance as RoleProvenance }
}

function decodeRoles(raw: unknown): ProductRoles | null {
  if (typeof raw !== 'object' || raw === null) return null
  const source = raw as Record<string, unknown>
  const out: Partial<Record<ProductRoleName, ProductRoleState>> = {}
  for (const name of PRODUCT_ROLE) {
    // `ProductRolesView` declares all four as non-nullable, so an absent one is
    // drift — not an implicit "not-evaluated", which would show a gap as an answer.
    if (!Object.hasOwn(source, name)) return null
    const role = decodeRole(source[name])
    if (role === null) return null
    out[name] = role
  }
  return out as ProductRoles
}

/** The roles a view should present as carried: determined AND true. */
export function assertedRoles(roles: ProductRoles): ProductRoleName[] {
  return PRODUCT_ROLE.filter((name) => roles[name].value === true)
}

/** The roles the server has not determined. Rendered distinctly — never as "no". */
export function unevaluatedRoles(roles: ProductRoles): ProductRoleName[] {
  return PRODUCT_ROLE.filter((name) => roles[name].provenance === 'not-evaluated')
}

/* ── Code groups (GET /api/v1/products/code-groups) ───────────────────────── */

/**
 * `ProductCodeGroup(Guid Id, string Name, string Prefix)`.
 *
 * `Prefix` is NON-nullable in the source, and the create validator structurally
 * checks it with `ProductCode.FromSequence(group.Prefix, 1)`. So a group that
 * cannot mint a code does not reach the client as a prefix-less entry — it fails
 * server-side. The decoder still enforces the canonical prefix shape the README
 * pins (uppercase ASCII `[A-Z0-9]`, 1..32), because offering a group the server
 * would then reject is a dead end presented as a choice.
 */
export interface ProductCodeGroup {
  readonly id: string
  readonly name: string
  readonly prefix: string
}

const CANONICAL_PREFIX_RE = /^[A-Z0-9]{1,32}$/

function decodeCodeGroup(raw: unknown): ProductCodeGroup | null {
  if (typeof raw !== 'object' || raw === null) return null
  const g = raw as Record<string, unknown>
  if (!isBoundUuid(g.id)) return null
  // A display label a person maintains — human text, deliberately not a machine token.
  if (!isNonEmptyString(g.name, 200)) return null
  if (!isMachineToken(g.prefix, 32)) return null
  if (!CANONICAL_PREFIX_RE.test(g.prefix)) return null
  return { id: g.id, name: g.name, prefix: g.prefix }
}

/** Decode the code-group list. Fails closed; a duplicate id rejects the whole list. */
export function decodeCodeGroupList(raw: unknown): readonly ProductCodeGroup[] | null {
  if (!Array.isArray(raw)) return null
  const out: ProductCodeGroup[] = []
  const seen = new Set<string>()
  for (const entry of raw) {
    const group = decodeCodeGroup(entry)
    if (group === null) return null
    if (seen.has(group.id)) return null
    seen.add(group.id)
    out.push(group)
  }
  return out
}

/* ── Product core (ProductDraftView) ──────────────────────────────────────── */

/**
 * The product core, shared by list items and the card (README: "list items use the
 * same product core").
 *
 * DELIBERATELY ABSENT, because the source does not carry them: display names for
 * group/class/family, a primary stock unit, per-area completeness, alias/match
 * information. Resolving ids to labels in the browser would be the client fan-out
 * §19.5 forbids, and inventing the rest would be the fabricated summary §9 forbids.
 * The gap is reported to the contract owner, not filled here.
 */
export interface ProductCore {
  readonly id: string
  readonly code: string
  readonly name: string
  readonly groupId: string
  readonly classId: string | null
  readonly familyId: string | null
  readonly roles: ProductRoles
  readonly lifecycle: ProductLifecycleValue
  /** Canonical positive decimal string up to Int64.MaxValue. Never a JS Number. */
  readonly version: string
  readonly createdAtUtc: string
  readonly updatedAtUtc: string
}

function decodeCore(raw: unknown): ProductCore | null {
  if (typeof raw !== 'object' || raw === null) return null
  const p = raw as Record<string, unknown>
  if (!isBoundUuid(p.id)) return null
  if (!isMachineToken(p.code, 128)) return null
  // A person-written name: bounded, but not held to the machine-token alphabet —
  // guarding it would make this client stricter than the server's own validator.
  if (!isNonEmptyString(p.name, 250)) return null
  if (!isBoundUuid(p.groupId)) return null
  if (p.classId !== null && !isBoundUuid(p.classId)) return null
  if (p.familyId !== null && !isBoundUuid(p.familyId)) return null
  if (!isLosslessVersion(p.version)) return null
  if (!isInstant(p.createdAtUtc) || !isInstant(p.updatedAtUtc)) return null

  const roles = decodeRoles(p.roles)
  if (roles === null) return null
  const lifecycle = decodeLifecycle(p.lifecycle)
  if (lifecycle === null) return null

  return {
    id: p.id,
    code: p.code,
    name: p.name,
    groupId: p.groupId,
    classId: (p.classId as string | null) ?? null,
    familyId: (p.familyId as string | null) ?? null,
    roles,
    lifecycle,
    version: p.version,
    createdAtUtc: p.createdAtUtc,
    updatedAtUtc: p.updatedAtUtc,
  }
}

/* ── References (ProductReferences) ───────────────────────────────────────── */

/** `ProductUnitReferences` — six independent UnitIds (DEC-PRD-006). */
export const UNIT_ROLE = [
  'stock',
  'purchase',
  'sales',
  'price',
  'production',
  'package',
] as const
export type UnitRoleName = (typeof UNIT_ROLE)[number]
export type ProductUnitReferences = Readonly<Record<UnitRoleName, string | null>>

/**
 * `ProductReferences(TemplateId?, TemplateVersion?, Attributes?, Units?, TrackingPolicyId?)`.
 *
 * Every member is optional in the source, and `Units` may be absent entirely — so
 * the decoder normalises a missing unit block to all-null rather than rejecting it.
 * An absent unit is "not assigned", which is a fact; it is never a fallback to the
 * stock unit, which would be the screen inventing a conversion.
 */
export interface ProductReferences {
  readonly templateId: string | null
  readonly templateVersion: string | null
  /** Keyed by FieldId. Values are opaque here — the template profile validates them. */
  readonly attributes: Readonly<Record<string, unknown>> | null
  readonly units: ProductUnitReferences
  readonly trackingPolicyId: string | null
}

const EMPTY_UNITS: ProductUnitReferences = {
  stock: null,
  purchase: null,
  sales: null,
  price: null,
  production: null,
  package: null,
}

function decodeUnits(raw: unknown): ProductUnitReferences | null {
  if (raw === null || raw === undefined) return EMPTY_UNITS
  if (typeof raw !== 'object') return null
  const source = raw as Record<string, unknown>
  const out: Partial<Record<UnitRoleName, string | null>> = {}
  for (const role of UNIT_ROLE) {
    const value = source[role]
    if (value === undefined || value === null) out[role] = null
    else if (isBoundUuid(value)) out[role] = value
    else return null
  }
  return out as ProductUnitReferences
}

function decodeReferences(raw: unknown): ProductReferences | null {
  if (raw === null || raw === undefined) {
    return {
      templateId: null,
      templateVersion: null,
      attributes: null,
      units: EMPTY_UNITS,
      trackingPolicyId: null,
    }
  }
  if (typeof raw !== 'object') return null
  const r = raw as Record<string, unknown>

  const templateId = r.templateId ?? null
  const templateVersion = r.templateVersion ?? null
  if (templateId !== null && !isBoundUuid(templateId)) return null
  if (templateVersion !== null && !isMachineToken(templateVersion, 64)) return null
  /**
   * The source pairs them at every write ("both together", `template_pair_required`),
   * so a read that carries one without the other describes a state the domain does
   * not allow — we cannot render a pinned version without knowing what it pins.
   */
  if ((templateId === null) !== (templateVersion === null)) return null

  const trackingPolicyId = r.trackingPolicyId ?? null
  if (trackingPolicyId !== null && !isBoundUuid(trackingPolicyId)) return null

  const attributesRaw = r.attributes ?? null
  if (attributesRaw !== null && (typeof attributesRaw !== 'object' || Array.isArray(attributesRaw))) {
    return null
  }

  const units = decodeUnits(r.units)
  if (units === null) return null

  return {
    templateId: (templateId as string | null) ?? null,
    templateVersion: (templateVersion as string | null) ?? null,
    attributes: (attributesRaw as Readonly<Record<string, unknown>> | null) ?? null,
    units,
    trackingPolicyId: (trackingPolicyId as string | null) ?? null,
  }
}

/* ── Completeness (ProductCompletenessView) ───────────────────────────────── */

/**
 * `{Evaluation, MissingFields}`.
 *
 * README: "NotEvaluated must not be rendered as complete or activatable." So the
 * evaluation token is carried as read and an unknown one is NOT coerced — the same
 * rule the lifecycle follows, and for the same reason.
 */
export const COMPLETENESS_EVALUATION = ['Complete', 'Incomplete', 'NotEvaluated'] as const
export type CompletenessEvaluation = (typeof COMPLETENESS_EVALUATION)[number]

export interface ProductCompleteness {
  readonly evaluation: CompletenessEvaluation | null
  /** The raw token when it is not one this build knows. */
  readonly rawEvaluation: string
  readonly missingFields: readonly string[]
}

function decodeCompleteness(raw: unknown): ProductCompleteness | null {
  if (typeof raw !== 'object' || raw === null) return null
  const c = raw as Record<string, unknown>
  if (!isMachineToken(c.evaluation, 64)) return null
  if (!Array.isArray(c.missingFields)) return null
  const missingFields: string[] = []
  for (const field of c.missingFields) {
    if (!isMachineToken(field, 200)) return null
    missingFields.push(field)
  }
  const known = (COMPLETENESS_EVALUATION as readonly string[]).includes(c.evaluation)
  return {
    evaluation: known ? (c.evaluation as CompletenessEvaluation) : null,
    rawEvaluation: c.evaluation,
    missingFields,
  }
}

/** Whether the card may be presented as complete. `null` evaluation never may. */
export function isComplete(completeness: ProductCompleteness): boolean {
  return completeness.evaluation === 'Complete'
}

/* ── Card (ProductCardView) ───────────────────────────────────────────────── */

export interface ProductCard {
  readonly product: ProductCore
  readonly references: ProductReferences
  readonly completeness: ProductCompleteness
}

/** Decode a card, bound to the id that was requested. */
export function decodeProductCard(raw: unknown, expectedId: string): ProductCard | null {
  if (typeof raw !== 'object' || raw === null) return null
  const c = raw as Record<string, unknown>
  const product = decodeCore(c.product)
  if (product === null) return null
  /**
   * The response must describe the record we asked for. A well-formed 200 about a
   * DIFFERENT product would have the user looking at one record believing it is
   * another — and any edit they then make would be aimed at the wrong revision.
   */
  if (product.id !== expectedId) return null
  const references = decodeReferences(c.references)
  if (references === null) return null
  const completeness = decodeCompleteness(c.completeness)
  if (completeness === null) return null
  return { product, references, completeness }
}

/* ── Page (ProductPage) ───────────────────────────────────────────────────── */

/**
 * `ProductPage(IReadOnlyList<ProductDraftView> Items, string? NextCursor)`.
 *
 * No `hasMore`, no total count, no count scope — the source publishes none, so the
 * screen shows none. `nextCursor !== null` is the only "there is more" signal, and
 * the cursor itself is opaque: the client never parses or constructs one.
 */
export interface ProductPage {
  readonly items: readonly ProductCore[]
  readonly nextCursor: string | null
}

export function decodeProductPage(raw: unknown): ProductPage | null {
  if (typeof raw !== 'object' || raw === null) return null
  const p = raw as Record<string, unknown>
  if (!Array.isArray(p.items)) return null
  if (!Object.hasOwn(p, 'nextCursor')) return null
  if (p.nextCursor !== null && !isNonEmptyString(p.nextCursor, 4096)) return null

  const items: ProductCore[] = []
  const seen = new Set<string>()
  for (const rawItem of p.items) {
    const item = decodeCore(rawItem)
    if (item === null) return null
    // One product is never two rows.
    if (seen.has(item.id)) return null
    seen.add(item.id)
    items.push(item)
  }

  return { items, nextCursor: (p.nextCursor as string | null) ?? null }
}

/* ── Mutation (ProductMutationResponse) ───────────────────────────────────── */

/**
 * `{Card, IsReplay}`.
 *
 * `isReplay` is a RESPONSE FIELD, not a header — README: "isReplay is an explicit
 * response indicator; replay remains the original successful operation status
 * (create 201, edit 200)."
 */
export interface ProductMutationResult {
  readonly card: ProductCard
  readonly isReplay: boolean
}

/**
 * Decode a mutation response.
 *
 * `expectedLifecycle` is enforced where the domain fixes it. For a CREATE that is
 * `Draft`, unconditionally: README and §6.1 make a new record born Draft, and
 * activation a separate command with its own audit record. A 201 that says `Active`
 * therefore means the server activated something this screen never asked to
 * activate — reporting that as an ordinary success would hide an activation from
 * the person responsible for it. The caller treats a malformed success as an
 * UNKNOWN outcome, not a failure: the record may exist.
 */
export function decodeProductMutation(
  raw: unknown,
  expectedId: string | null,
  expectedLifecycle: ProductLifecycle | null,
): ProductMutationResult | null {
  if (typeof raw !== 'object' || raw === null) return null
  const m = raw as Record<string, unknown>
  if (typeof m.isReplay !== 'boolean') return null
  if (typeof m.card !== 'object' || m.card === null) return null

  const core = decodeCore((m.card as Record<string, unknown>).product)
  if (core === null) return null
  if (expectedId !== null && core.id !== expectedId) return null
  if (expectedLifecycle !== null) {
    if (!core.lifecycle.known || core.lifecycle.value !== expectedLifecycle) return null
  }

  const card = decodeProductCard(m.card, core.id)
  if (card === null) return null
  return { card, isReplay: m.isReplay }
}

/* ── Problem codes ────────────────────────────────────────────────────────── */

/**
 * Stable PLATFORM codes (`docs/features/PLATFORM_HTTP_ERROR_CONTRACT_v1.md` §2).
 * The Product endpoints defer 401/403/500/503 to the host's canonical handling.
 */
export const PLATFORM_PROBLEM_CODE = {
  correlationMetadataInvalid: 'correlation_metadata_invalid',
  companyContextInvalid: 'company_context_invalid',
  authenticationRequired: 'authentication_required',
  forbidden: 'forbidden',
  internalError: 'internal_error',
  serviceUnavailable: 'service_unavailable',
} as const

/** `ProductApiV1` constants, verbatim. */
export const PRODUCT_PROBLEM_CODE = {
  validationFailed: 'validation_failed',
  invalidCursor: 'invalid_cursor',
  notFound: 'not_found',
  versionConflict: 'version_conflict',
  idempotencyConflict: 'idempotency_conflict',
  /** Editing a record that is not a Draft. */
  lifecycleConflict: 'lifecycle_conflict',
} as const

export type ProductProblemCode =
  | (typeof PLATFORM_PROBLEM_CODE)[keyof typeof PLATFORM_PROBLEM_CODE]
  | (typeof PRODUCT_PROBLEM_CODE)[keyof typeof PRODUCT_PROBLEM_CODE]

const KNOWN_PROBLEM_CODES = new Set<string>([
  ...Object.values(PLATFORM_PROBLEM_CODE),
  ...Object.values(PRODUCT_PROBLEM_CODE),
])

/** The stable machine code from a ProblemDetails body, or null when unrecognised. */
export function productProblemCodeOf(raw: unknown): ProductProblemCode | null {
  if (typeof raw !== 'object' || raw === null) return null
  const code = (raw as Record<string, unknown>).code
  return typeof code === 'string' && KNOWN_PROBLEM_CODES.has(code)
    ? (code as ProductProblemCode)
    : null
}

/**
 * The revision the record is actually on, from a `version_conflict` body.
 *
 * The edit screen needs it to tell the user what happened, and must NOT adopt it as
 * its own `expectedVersion` — that would turn a refusal into a blind overwrite of
 * whatever the other editor wrote. Returns null when the server did not say; the
 * screen then asks for a reload rather than guessing a number.
 */
export function currentVersionOf(raw: unknown): string | null {
  if (typeof raw !== 'object' || raw === null) return null
  const value = (raw as Record<string, unknown>).currentVersion
  return isLosslessVersion(value) ? value : null
}

/**
 * A field error from a `validation_failed` body.
 *
 * The source shape is `errors: [{field, code}]` — a list of MACHINE codes, not
 * localized messages. So the screen maps `code` to Turkish itself, and an unknown
 * code still reaches the right input with a general message rather than being
 * dropped: losing the field binding is worse than losing message precision.
 */
export interface ProductFieldError {
  readonly field: string
  readonly code: string
}

export function productFieldErrorsOf(raw: unknown): readonly ProductFieldError[] {
  if (typeof raw !== 'object' || raw === null) return []
  const errors = (raw as Record<string, unknown>).errors
  if (!Array.isArray(errors)) return []
  const out: ProductFieldError[] = []
  for (const entry of errors) {
    if (typeof entry !== 'object' || entry === null) continue
    const e = entry as Record<string, unknown>
    if (typeof e.field !== 'string' || e.field.length === 0) continue
    if (typeof e.code !== 'string' || e.code.length === 0) continue
    out.push({ field: e.field, code: e.code })
  }
  return out
}
