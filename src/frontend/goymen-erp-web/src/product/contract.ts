/**
 * Product (Ürün) U-01/U-02 contract — runtime decoding and validation.
 *
 * ┌─ CONTRACT STATUS ──────────────────────────────────────────────────────────┐
 * │ PINNED. Aligned to the published Product backend contract at               │
 * │ `codex/urun-u01-u02-backend-20260912` @ `cb75e2f`:                          │
 * │   src/backend/Modules/Product/README.md                                    │
 * │   …/Application/Contracts/ProductDraftContractsV1.cs                       │
 * │   …/Application/Contracts/IProductService.cs                               │
 * │   …/Application/Contracts/ProductFieldCodesV1.cs                           │
 * │   src/backend/GoymenERP.Api/Endpoints/ProductEndpoints.cs                  │
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
import { decodeTechnicalOptions, type TechnicalOptions } from './technicalContract'

/**
 * Marks the module as aligned to a published backend source rather than to a
 * business-spec proposal. Asserted in the contract test so the provenance cannot be
 * lost silently.
 */
export const PRODUCT_CONTRACT_STATUS = 'PINNED' as const
export const PRODUCT_CONTRACT_SOURCE =
  'Product backend contract v1 (PR #237, cb75e2f): README.md + ProductDraftContractsV1.cs + IProductService.cs + ProductFieldCodesV1.cs + Endpoints/ProductEndpoints.cs'

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

/* ── Reference options (GET /products/reference-options) ──────────────────── */

/** `ProductReferenceOption(Guid Id, string DisplayName)`. */
export interface ProductReferenceOption {
  readonly id: string
  readonly displayName: string
}

/**
 * The catalogues `ProductReferenceOptions` can report as unavailable.
 *
 * README draws the line this list exists for: "An unavailable catalogue is
 * different from a valid empty catalogue and must not produce made-up choices." So
 * the screen treats them as two separate states — an unavailable catalogue hides
 * its field and says why, an empty one shows the field and says it is empty.
 * Rendering both the same way would present "nothing defined yet" for something we
 * simply could not read.
 */
export const REFERENCE_CATALOG = [
  'classes',
  'families',
  'units',
  'trackingPolicies',
  'templates',
] as const
export type ReferenceCatalog = (typeof REFERENCE_CATALOG)[number]

export interface ProductReferenceOptions {
  readonly technical?: TechnicalOptions
  readonly groupId: string
  readonly classId: string | null
  readonly classes: readonly ProductReferenceOption[]
  readonly families: readonly ProductReferenceOption[]
  readonly unavailableCatalogs: readonly ReferenceCatalog[]
}

function decodeOption(raw: unknown): ProductReferenceOption | null {
  if (typeof raw !== 'object' || raw === null) return null
  const o = raw as Record<string, unknown>
  if (!isBoundUuid(o.id)) return null
  // A catalogue display name is human text and stays Unicode.
  if (!isNonEmptyString(o.displayName, 200)) return null
  return { id: o.id, displayName: o.displayName }
}

function decodeOptionList(raw: unknown): readonly ProductReferenceOption[] | null {
  if (!Array.isArray(raw)) return null
  const out: ProductReferenceOption[] = []
  const seen = new Set<string>()
  for (const entry of raw) {
    const option = decodeOption(entry)
    if (option === null) return null
    // A duplicate id would render twice and make the selection ambiguous.
    if (seen.has(option.id)) return null
    seen.add(option.id)
    out.push(option)
  }
  return out
}

/**
 * Decode the reference options, bound to the group they were requested for.
 *
 * PRD-CARD-1 carries the released fields/dictionaries and Settings unit identities.
 * Missing legacy metadata offers no technical choices; malformed metadata fails closed.
 */
export function decodeReferenceOptions(
  raw: unknown,
  expectedGroupId: string,
): ProductReferenceOptions | null {
  if (typeof raw !== 'object' || raw === null) return null
  const o = raw as Record<string, unknown>
  if (!isBoundUuid(o.groupId)) return null
  // Options for another group would offer choices the create would then reject.
  if (o.groupId !== expectedGroupId) return null
  if (o.classId !== null && o.classId !== undefined && !isBoundUuid(o.classId)) return null

  const classes = decodeOptionList(o.classes)
  if (classes === null) return null
  const families = decodeOptionList(o.families)
  if (families === null) return null
  const technical = decodeTechnicalOptions(o)
  if (technical === null) return null

  if (!Array.isArray(o.unavailableCatalogs)) return null
  const unavailable: ReferenceCatalog[] = []
  for (const entry of o.unavailableCatalogs) {
    if (typeof entry !== 'string') return null
    // An unknown catalogue name is drift: we would not know which field to close.
    if (!(REFERENCE_CATALOG as readonly string[]).includes(entry)) return null
    const catalog = entry as ReferenceCatalog
    if (!unavailable.includes(catalog)) unavailable.push(catalog)
  }

  return {
    groupId: o.groupId,
    classId: (o.classId as string | null) ?? null,
    classes,
    families,
    technical,
    unavailableCatalogs: unavailable,
  }
}

/** Whether a catalogue could not be read — distinct from it being empty. */
export function isCatalogUnavailable(
  options: ProductReferenceOptions | null,
  catalog: ReferenceCatalog,
): boolean {
  return options !== null && options.unavailableCatalogs.includes(catalog)
}

/* ── Reference labels (ProductReferenceLabels) ────────────────────────────── */

/**
 * Server-resolved display names for the ids a product carries.
 *
 * README: nullable "when no value or unavailable source exists" — and those are NOT
 * the same fact. A null label on a product that HAS the id means the catalogue
 * could not be read; a null id means nothing is assigned. The screen keeps them
 * apart rather than printing one dash for both.
 *
 * These exist so the client never fans a UUID out into another module's read.
 */
export interface ProductReferenceLabels {
  readonly template?: string | null
  readonly familyFacts?: { readonly cins: string; readonly tur: string; readonly tip: string } | null
  readonly attributes?: Readonly<Record<string, string>> | null
  readonly units?: Readonly<Record<string, string>> | null
  readonly group: string | null
  readonly class: string | null
  readonly family: string | null
  readonly primaryStockUnit: string | null
}

const LABEL_KEYS = ['group', 'class', 'family', 'primaryStockUnit'] as const

function decodeLabels(raw: unknown): ProductReferenceLabels | null {
  if (raw === null || raw === undefined) {
    return { group: null, class: null, family: null, primaryStockUnit: null }
  }
  if (typeof raw !== 'object') return null
  const source = raw as Record<string, unknown>
  const out: Partial<Record<(typeof LABEL_KEYS)[number], string | null>> = {}
  for (const key of LABEL_KEYS) {
    const value = source[key]
    if (value === undefined || value === null) out[key] = null
    // Human text a catalogue owner maintains: bounded, but not held to the
    // machine-token alphabet — that would make this client stricter than the source.
    else if (isNonEmptyString(value, 200)) out[key] = value
    else return null
  }
  if (source.template != null && !isNonEmptyString(source.template, 200)) return null
  const facts = source.familyFacts
  if (facts != null && (typeof facts !== 'object' || Array.isArray(facts)
    || !['cins', 'tur', 'tip'].every(key => isNonEmptyString((facts as Record<string, unknown>)[key], 200)))) return null
  const attributes = source.attributes
  if (attributes != null && (typeof attributes !== 'object' || Array.isArray(attributes)
    || !Object.values(attributes).every(value => isNonEmptyString(value, 200)))) return null
  const units = source.units
  if (units != null && (typeof units !== 'object' || Array.isArray(units)
    || !Object.entries(units).every(([key, value]) => (UNIT_ROLE as readonly string[]).includes(key) && isNonEmptyString(value, 200)))) return null
  return { ...out, template: source.template ?? null, familyFacts: facts ?? null,
    attributes: attributes ?? null, units: units ?? null } as ProductReferenceLabels
}

/* ── Product core (ProductDraftView) ──────────────────────────────────────── */

/**
 * The product core, shared by list items and the card (README: "list items use the
 * same product core").
 *
 * `labels` and `completeness` travel with EVERY projection (list rows included), so
 * the client never resolves an id itself — which is the fan-out §19.5 forbids.
 *
 * Still absent because the source carries none: alias/match information. Not
 * invented here.
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
  readonly labels: ProductReferenceLabels
  /**
   * The server's own verdict. `ProductCardView.Completeness` is a compatibility
   * projection OF THIS value, not a second calculation, so this is the one the
   * screen reads — if the two ever disagree, the product's is the truth.
   */
  readonly completeness: ProductCompleteness
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
  const labels = decodeLabels(p.labels)
  if (labels === null) return null
  const completeness = decodeCompleteness(p.completeness)
  if (completeness === null) return null

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
    labels,
    completeness,
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
  /**
   * `card.completeness` is deliberately NOT read. The source makes it a computed
   * projection of `product.completeness`, so requiring it would let a serializer
   * detail become a decode failure — and reading it would give two places for the
   * same fact to disagree.
   */
  return { product, references }
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

/* ── Duplicate candidates (ProductDuplicateCandidates) ────────────────────── */

/**
 * `GET /products/duplicate-candidates?name&excludeProductId` — spec §10.2/§15, the read-only duplicate
 * preview. The server answers ONE decided rule, `exactName`: the cards whose name equals the given one
 * after the write path's trim, under the name column's Turkish case-insensitive collation, every lifecycle,
 * the excluded card left out. It is a WARNING to compare against — never a block (§10.2). No other rule is
 * accepted: an unknown `rule` is malformed, not silently shown as "similar".
 */
export const DUPLICATE_RULE_EXACT_NAME = 'exactName'

export interface ProductDuplicateCandidates {
  readonly rule: typeof DUPLICATE_RULE_EXACT_NAME
  /** The normalised name the server compared. */
  readonly name: string
  readonly exactName: readonly ProductCore[]
  /** More cards share the name than were listed. */
  readonly hasMore: boolean
}

export function decodeDuplicateCandidates(raw: unknown): ProductDuplicateCandidates | null {
  if (typeof raw !== 'object' || raw === null) return null
  const d = raw as Record<string, unknown>
  if (d.rule !== DUPLICATE_RULE_EXACT_NAME) return null
  if (!isNonEmptyString(d.name, 250)) return null
  if (typeof d.hasMore !== 'boolean') return null
  if (!Array.isArray(d.exactName)) return null
  const items: ProductCore[] = []
  const seen = new Set<string>()
  for (const rawItem of d.exactName) {
    const item = decodeCore(rawItem)
    if (item === null || seen.has(item.id)) return null
    seen.add(item.id)
    items.push(item)
  }
  return { rule: DUPLICATE_RULE_EXACT_NAME, name: d.name as string, exactName: items, hasMore: d.hasMore }
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

/**
 * `ProductFieldCodesV1.All` — the closed v1 set, verbatim.
 *
 * Listed so the Turkish mapping can be asserted complete in a test: a code the
 * server can send but this build has no sentence for would silently fall back to
 * the generic one, which is exactly the drift the list makes visible.
 */
export const PRODUCT_FIELD_CODE = [
  'invalid_name',
  'invalid_role_provenance',
  'invalid_reference',
  'invalid_code_prefix',
  'template_pair_required',
  'explicit_attribute_change_required',
  'null_not_allowed',
  'required',
  'invalid_metadata',
  'invalid_reason',
  'invalid_version',
  'derived_role_readonly',
  'template_required',
] as const
export type ProductFieldCode = (typeof PRODUCT_FIELD_CODE)[number]

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

/* ── Change history (ProductHistoryView) ──────────────────────────────────── */

/**
 * `GET /api/v1/products/{id}/history` → `ProductHistoryView(ProductId, Entries, Truncated)`,
 * U-01 spec §5.4 and §9 item 6 (CASE-PRD-U01-016). Source: `ProductHistoryContractsV1.cs`.
 *
 * Deliberately NOT on the wire and therefore not decoded: the actor, client address,
 * user agent, correlation and key material. The history says what changed, when, the
 * version step and the recorded reason — not who, until an Identity display source
 * exists for it.
 */
export const HISTORY_OPERATION = ['DraftCreate', 'DraftBasicEdit', 'Activate', 'Deactivate', 'Reactivate'] as const
export type HistoryOperation = (typeof HISTORY_OPERATION)[number]

/** `ProductHistoryFields.All`, in the server's display order. */
export const HISTORY_FIELD = [
  'name',
  'classId',
  'familyId',
  'lifecycle',
  'roles.purchasable',
  'roles.sellable',
  'roles.productionInput',
  'roles.productionOutput',
  'references.templateId',
  'references.templateVersion',
  'references.attributes',
  'references.units.stock',
  'references.units.purchase',
  'references.units.sales',
  'references.units.price',
  'references.units.production',
  'references.units.package',
  'references.trackingPolicyId',
] as const
export type HistoryField = (typeof HISTORY_FIELD)[number]

/** Server `ProductHistoryApiV1.PageSize`; a longer list is drift, not more history. */
export const HISTORY_PAGE_SIZE = 50
const HISTORY_REASON_MAX = 1000

export type HistoryOperationValue =
  | { readonly known: true; readonly value: HistoryOperation }
  | { readonly known: false; readonly raw: string }

export interface ProductHistoryEntry {
  readonly eventId: string
  readonly operation: HistoryOperationValue
  readonly occurredAtUtc: string
  readonly versionBefore: string | null
  readonly versionAfter: string
  readonly reason: string
  /** Server order; a key this client does not know is kept verbatim rather than dropped. */
  readonly changedFields: readonly string[]
}

export interface ProductHistory {
  readonly productId: string
  readonly entries: readonly ProductHistoryEntry[]
  readonly truncated: boolean
}

export function isHistoryField(value: string): value is HistoryField {
  return (HISTORY_FIELD as readonly string[]).includes(value)
}

function decodeHistoryEntry(raw: unknown): ProductHistoryEntry | null {
  if (typeof raw !== 'object' || raw === null) return null
  const e = raw as Record<string, unknown>
  if (!isBoundUuid(e.eventId)) return null
  if (!isMachineToken(e.operation, 64)) return null
  if (!isInstant(e.occurredAtUtc)) return null
  if (!isLosslessVersion(e.versionAfter)) return null
  if (e.versionBefore !== null && !isLosslessVersion(e.versionBefore)) return null
  // The reason is text a person wrote: bounded and non-blank, never re-validated as a token.
  if (!isNonEmptyString(e.reason, HISTORY_REASON_MAX)) return null
  const fields = e.changedFields
  if (!Array.isArray(fields) || fields.length > HISTORY_FIELD.length) return null
  if (!fields.every((field) => isMachineToken(field, 64))) return null
  if (new Set(fields).size !== fields.length) return null

  const operation: HistoryOperationValue = (HISTORY_OPERATION as readonly string[]).includes(e.operation)
    ? { known: true, value: e.operation as HistoryOperation }
    : { known: false, raw: e.operation }
  if (operation.known && operation.value === 'DraftCreate') {
    // A creation has no "before" and starts at 1; it has nothing to diff against.
    if (e.versionBefore !== null || e.versionAfter !== '1' || fields.length > 0) return null
  }
  if (operation.known && operation.value !== 'DraftCreate') {
    // An edit or a lifecycle move moves the revision by exactly one; compared as integers, never as doubles.
    if (typeof e.versionBefore !== 'string') return null
    if (BigInt(e.versionAfter) !== BigInt(e.versionBefore) + BigInt(1)) return null
  }

  return {
    eventId: e.eventId,
    operation,
    occurredAtUtc: e.occurredAtUtc,
    versionBefore: e.versionBefore as string | null,
    versionAfter: e.versionAfter,
    reason: e.reason,
    changedFields: fields as string[],
  }
}

/**
 * Decode a history, bound to the Product that was requested.
 *
 * Entry rules alone cannot see a MISSING event, so the entries are also read against each
 * other: one unbroken version chain, and — unless the server says it cut the list — an
 * oldest entry that is the creation. A history with a hole in it, or one that starts in
 * the middle, otherwise reaches the screen as if it were the whole story.
 *
 * The card's own version is deliberately NOT compared here: this decoder is given the
 * history alone, and the server's reader is the only layer that holds both. A history that
 * stops short of the card fails there, before it is serialized.
 */
export function decodeProductHistory(raw: unknown, expectedId: string): ProductHistory | null {
  if (typeof raw !== 'object' || raw === null) return null
  const h = raw as Record<string, unknown>
  if (h.productId !== expectedId) return null
  if (typeof h.truncated !== 'boolean') return null
  // An existing Product always has its creation event; an empty list is drift, not "no changes".
  if (!Array.isArray(h.entries) || h.entries.length === 0 || h.entries.length > HISTORY_PAGE_SIZE) return null
  const entries: ProductHistoryEntry[] = []
  const seen = new Set<string>()
  let previous: number | null = null
  for (const item of h.entries) {
    const entry = decodeHistoryEntry(item)
    if (entry === null || seen.has(entry.eventId)) return null
    const at = Date.parse(entry.occurredAtUtc)
    // Newest first, as the server orders it. A reordered list would misstate the sequence.
    if (previous !== null && at > previous) return null
    previous = at
    seen.add(entry.eventId)
    entries.push(entry)
  }

  // ONE CHAIN: each entry's predecessor is the next older entry. A gap, a creation event
  // that is not the oldest, and equal-time entries in the wrong order all break this.
  for (let index = 0; index + 1 < entries.length; index += 1) {
    if (entries[index].versionBefore !== entries[index + 1].versionAfter) return null
  }

  const oldest = entries[entries.length - 1]
  if (h.truncated) {
    // The server reports a cut only when it actually cut at the page size; a shorter list
    // that still claims to be cut is drift, and it would hide the missing creation event.
    if (entries.length !== HISTORY_PAGE_SIZE) return null
  } else if (!(oldest.operation.known && oldest.operation.value === 'DraftCreate')) {
    // An uncut list reaches back to the creation event (which the entry rules pin at 1).
    return null
  }

  return { productId: expectedId, entries, truncated: h.truncated }
}
