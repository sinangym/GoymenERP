/**
 * Product (Ürün) U-01/U-02 contract — runtime decoding and validation.
 *
 * ┌─ CONTRACT STATUS ──────────────────────────────────────────────────────────┐
 * │ PROPOSED_JIT. There is NO merged Product API: `contracts/openapi/           │
 * │ goymen-erp-v1.json` on main@3e7dd98 publishes no `/products` path, and      │
 * │ `src/backend/Modules` has no Product module. Every name below is derived    │
 * │ from the business spec on PR #181 (`chatgpt/product-core-u01-u02-u07-u08-   │
 * │ u09-jit-v1`@cc2ea1e), clause by clause — NOT from a backend contract.       │
 * │                                                                            │
 * │ PRODUCT_U01_U02_IDENTITY_LIFECYCLE_JIT_SPEC_v1 §3 puts "exact route,        │
 * │ endpoint, permission string ve error code isimleri" OUT of business scope   │
 * │ and §5.4 says "Exact API field isimleri contract ile kilitlenir". So this   │
 * │ module is the ONE place that changes when CODEX-BACKEND-URUN publishes the  │
 * │ real DTOs: the screens, the form model and the view state consume the       │
 * │ types below and never re-read a raw response.                               │
 * │                                                                            │
 * │ Nothing here is evidence of a working API. The routes mount only on a       │
 * │ server-published capability (see ./capabilities.ts), and today the server   │
 * │ publishes none — so the real app shows no Product feature at all rather     │
 * │ than a screen backed by nothing.                                            │
 * └────────────────────────────────────────────────────────────────────────────┘
 *
 * Why the decoders exist even so (same reason as the Cari ones): a generated
 * client gives compile-time types, not runtime validation. A page that fails
 * closed on an out-of-range lifecycle or a role whose provenance contradicts its
 * value is the half that survives the generated-client move.
 */

import {
  isBoundUuid,
  isInstant,
  isLosslessVersion,
  isMachineToken,
  isNonEmptyString,
  isNullableInstant,
  isNullableMachineToken,
  isNullableText,
} from './wireGuards'

/**
 * Marks every symbol in this module as derived from the business spec rather than
 * from an accepted API contract. Asserted in the contract test so the marker cannot
 * be dropped silently when the real DTOs land — removing it is a deliberate edit.
 */
export const PRODUCT_CONTRACT_STATUS = 'PROPOSED_JIT' as const
export const PRODUCT_CONTRACT_SOURCE =
  'PRODUCT_U01_U02_IDENTITY_LIFECYCLE_JIT_SPEC_v1 (PR #181, cc2ea1e)'

/* ── Lifecycle (spec §6) ──────────────────────────────────────────────────── */

/**
 * The three binding states. `Draft` is where a new Product is born (§6.1) and is
 * deliberately NOT selectable in downstream transaction pickers; `Inactive` closes
 * new use in BOTH companies while history stays readable (§6, DEC-PRD-010).
 *
 * There is no `Deleted`. §6.4: a used Product is never physically deleted and the
 * UI offers no delete — so a fourth state would be a claim the domain does not make.
 */
export const PRODUCT_LIFECYCLE = ['Draft', 'Active', 'Inactive'] as const
export type ProductLifecycle = (typeof PRODUCT_LIFECYCLE)[number]

/**
 * A lifecycle as READ. Either one of the three states this build understands, or a
 * value it does not — carried verbatim, never guessed at.
 *
 * WHY THIS IS NOT JUST `ProductLifecycle`. The vocabulary stays exactly these three
 * strings, in exactly this casing: §6 names them, and accepting a second spelling
 * would hide a real contract drift and quietly establish a second schema truth. That
 * is a question for the schema owner, not for a decoder.
 *
 * What changes is what an out-of-vocabulary value COSTS. Rejecting the whole page
 * for one unrecognised token means a single odd row hides every valid one behind
 * "Beklenmeyen yanıt", which tells the operator nothing about what is wrong — and
 * that is not hypothetical: the Product backend candidate (#237) currently emits
 * `"draft"` where this reads `Draft` (review finding P1-1), so the first time the
 * two lanes connect, every list would be empty. A row that says its status is
 * unknown is both safer and a far louder signal to the person who can fix it.
 *
 * Strictness belongs on the WRITE side and stays there: `decodeProductDraftCreated`
 * still rejects any 201 that is not exactly `Draft`, because that one is a safety
 * check about an activation nobody asked for, not a display concern.
 */
export type ProductLifecycleValue =
  | { readonly known: true; readonly value: ProductLifecycle }
  | { readonly known: false; readonly raw: string }

function decodeLifecycle(value: unknown): ProductLifecycleValue | null {
  // Absent, blank or non-string is still fatal: that is a missing field, not an
  // unfamiliar one, and there is nothing to carry.
  //
  // So is an unprintable one, and that is NOT a step back from carrying unknown
  // values. UNFAMILIAR and MALFORMED are different: a status token in any sane
  // vocabulary is printable text, so a control or bidi character means the field is
  // broken, not that it names a state this build has not learned yet.
  if (!isMachineToken(value, 64)) return null
  return (PRODUCT_LIFECYCLE as readonly string[]).includes(value)
    ? { known: true, value: value as ProductLifecycle }
    : { known: false, raw: value }
}

/** The state when it is one this build understands, else null. */
export function knownLifecycle(lifecycle: ProductLifecycleValue): ProductLifecycle | null {
  return lifecycle.known ? lifecycle.value : null
}

/* ── Roles and provenance (spec §5.2, DEC-PRD-001) ────────────────────────── */

/**
 * The four commercial/operational roles. DEC-PRD-001 and AT-015: they are
 * INDEPENDENT of class/group/family, and one Product may carry several at once.
 * This is a record of four independent facts, not an enum or a bit flag — a
 * product that is both a production input and sellable is ordinary.
 */
export const PRODUCT_ROLE = [
  'purchasable',
  'sellable',
  'productionInput',
  'productionOutput',
] as const
export type ProductRoleName = (typeof PRODUCT_ROLE)[number]

/**
 * Where a role's value came from (§5.2).
 *
 *  - `manual`        — a user set it;
 *  - `derived`       — the server derived it from an accepted domain relationship;
 *  - `not-evaluated` — the server has NOT determined it yet.
 *
 * The third one is the point of this type. §5.2: "Henüz değerlendirilemeyen rol
 * sessizce `false` diye kalıcılaştırılmaz." Collapsing provenance into a plain
 * boolean is exactly that silent persist — an unevaluated role would render as
 * "not purchasable", which is a claim about the record nobody made.
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
 * Decode one role. Fails closed on the pairing, in BOTH directions:
 * a `not-evaluated` role carrying `true`/`false` is a server that persisted a
 * value it said it did not have, and a `manual`/`derived` role carrying `null` is
 * a value that went missing. Either way we cannot render it truthfully.
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
    // Every role is REQUIRED. An absent property is drift, not an implicit
    // "not-evaluated" — normalising it away would accept a server that stopped
    // sending a role and show the gap as a determined answer.
    if (!Object.hasOwn(source, name)) return null
    const role = decodeRole(source[name])
    if (role === null) return null
    out[name] = role
  }
  return out as ProductRoles
}

/** The roles a view should present as "carried", i.e. determined AND true. */
export function assertedRoles(roles: ProductRoles): ProductRoleName[] {
  return PRODUCT_ROLE.filter((name) => roles[name].value === true)
}

/** The roles the server has not determined. Rendered distinctly — never as "no". */
export function unevaluatedRoles(roles: ProductRoles): ProductRoleName[] {
  return PRODUCT_ROLE.filter((name) => roles[name].provenance === 'not-evaluated')
}

/* ── Classification (spec §5.2) ───────────────────────────────────────────── */

/**
 * Class / group / family, kept explicitly separate (§5.2). `Subgroup` is
 * deliberately absent: the spec calls it a candidate UI/data-dictionary field that
 * is "registry owner kararı olmadan bağlayıcı Product modeli sayılmaz".
 */
export interface ProductClassification {
  readonly class: string
  readonly group: string | null
  readonly family: string | null
}

function decodeClassification(raw: unknown): ProductClassification | null {
  if (typeof raw !== 'object' || raw === null) return null
  const c = raw as Record<string, unknown>
  if (!isNonEmptyString(c.class, 120)) return null
  if (!isNullableText(c.group, 120)) return null
  if (!isNullableText(c.family, 120)) return null
  return {
    class: c.class,
    group: (c.group as string | null) ?? null,
    family: (c.family as string | null) ?? null,
  }
}

/* ── Completeness (spec §8.2, §5.3) ───────────────────────────────────────── */

/**
 * Template / unit / package completeness as a THREE-valued fact (§8.2).
 *
 * `unknown` exists for the same reason `not-evaluated` does above: §9 forbids
 * fabricating a value for data the server does not support — "unsupported veriye
 * `0` uydurulmaz". A server that cannot yet evaluate package completeness must be
 * able to say so without the screen printing "eksik", which would send an operator
 * looking for a gap that may not exist.
 */
export const COMPLETENESS = ['complete', 'incomplete', 'unknown'] as const
export type Completeness = (typeof COMPLETENESS)[number]

export const COMPLETENESS_AREA = ['template', 'unit', 'package'] as const
export type CompletenessArea = (typeof COMPLETENESS_AREA)[number]

export type ProductCompleteness = Readonly<Record<CompletenessArea, Completeness>>

function decodeCompleteness(raw: unknown): ProductCompleteness | null {
  if (typeof raw !== 'object' || raw === null) return null
  const source = raw as Record<string, unknown>
  const out: Partial<Record<CompletenessArea, Completeness>> = {}
  for (const area of COMPLETENESS_AREA) {
    const value = source[area]
    if (typeof value !== 'string') return null
    if (!(COMPLETENESS as readonly string[]).includes(value)) return null
    out[area] = value as Completeness
  }
  return out as ProductCompleteness
}

/* ── Search match reason (spec §8.1) ──────────────────────────────────────── */

/**
 * Why this row matched (§8.1: "Alias eşleşmesi canonical Product'ı tek satır
 * döndürür ve eşleşme nedenini belirtir").
 *
 * `matchedAlias` is bound to the reason by the decoder: an alias match must name
 * the retired code it matched, and a non-alias match must not carry one. Showing
 * "eski kod" without saying WHICH old code is how an operator concludes they are
 * looking at the wrong record.
 */
export const MATCH_REASON = ['code', 'alias', 'name'] as const
export type MatchReason = (typeof MATCH_REASON)[number]

export interface ProductMatch {
  readonly reason: MatchReason | null
  readonly matchedAlias: string | null
}

function decodeMatch(raw: unknown): ProductMatch | null {
  if (typeof raw !== 'object' || raw === null) return null
  const m = raw as Record<string, unknown>
  const reason = m.reason
  if (reason !== null && typeof reason !== 'string') return null
  if (reason !== null && !(MATCH_REASON as readonly string[]).includes(reason)) return null
  if (!isNullableMachineToken(m.matchedAlias, 64)) return null
  const matchedAlias = (m.matchedAlias as string | null) ?? null
  if (reason === 'alias' && matchedAlias === null) return null
  if (reason !== 'alias' && matchedAlias !== null) return null
  return { reason: (reason as MatchReason | null) ?? null, matchedAlias }
}

/* ── List row (spec §8.2) ─────────────────────────────────────────────────── */

export interface ProductListItem {
  /** Immutable server-generated UUID (§5.1, DEC-PRD-004). */
  readonly id: string
  /** Visible code: group prefix + sequence, server-generated (§5.1, §7). */
  readonly code: string
  /** Global display name — one name for both Göymen companies (§4.1, §5.1). */
  readonly name: string
  readonly classification: ProductClassification
  readonly roles: ProductRoles
  readonly lifecycle: ProductLifecycleValue
  /**
   * Main stock unit code (DEC-PRD-007: kg for every product). `null` while a Draft
   * has not been given one — rendered as "atanmadı", never as an empty unit.
   */
  readonly primaryStockUnit: string | null
  readonly completeness: ProductCompleteness
  readonly match: ProductMatch
  readonly lastChangedAt: string
  /** Revision token, carried verbatim (§5.1, §14). */
  readonly version: string
}

/**
 * The count scope this slice accepts (§8.3: a count "aynı auth/filter/global
 * scope'u kullanır"). Exactly one token, so a number can never be read as
 * "how many products exist" — same rule the C-01 list applies to its own scope.
 */
export const PRODUCT_COUNT_SCOPE = 'filtered-authorized-visible-products'

export interface ProductPage {
  readonly items: readonly ProductListItem[]
  /** Opaque server token. §8.3: the frontend never parses or constructs one. */
  readonly nextCursor: string | null
  readonly hasMore: boolean
  /** int64 that may exceed the JS safe range: kept verbatim as a string. */
  readonly totalCount: string | null
  readonly countScope: string | null
}

function decodeTotalCount(value: unknown): { ok: true; value: string | null } | { ok: false } {
  if (value === null) return { ok: true, value: null }
  if (typeof value === 'number') {
    return Number.isSafeInteger(value) && value >= 0
      ? { ok: true, value: String(value) }
      : { ok: false }
  }
  if (typeof value === 'string' && /^(?:0|[1-9]\d*)$/.test(value)) return { ok: true, value }
  return { ok: false }
}

function decodeListItem(raw: unknown): ProductListItem | null {
  if (typeof raw !== 'object' || raw === null) return null
  const i = raw as Record<string, unknown>
  if (!isBoundUuid(i.id)) return null
  if (!isMachineToken(i.code, 64)) return null
  if (!isNonEmptyString(i.name, 250)) return null
  const lifecycle = decodeLifecycle(i.lifecycle)
  if (lifecycle === null) return null
  if (!isNullableMachineToken(i.primaryStockUnit, 32)) return null
  if (!isInstant(i.lastChangedAt)) return null
  if (!isLosslessVersion(i.version)) return null

  const classification = decodeClassification(i.classification)
  if (classification === null) return null
  const roles = decodeRoles(i.roles)
  if (roles === null) return null
  const completeness = decodeCompleteness(i.completeness)
  if (completeness === null) return null
  const match = decodeMatch(i.match)
  if (match === null) return null

  return {
    id: i.id,
    code: i.code,
    name: i.name,
    classification,
    roles,
    lifecycle,
    primaryStockUnit: (i.primaryStockUnit as string | null) ?? null,
    completeness,
    match,
    lastChangedAt: i.lastChangedAt,
    version: i.version,
  }
}

/**
 * Decode a `ProductPage`. Returns null — fail closed — when anything is off; the
 * caller maps that to `malformed-response`. One bad row rejects the whole page
 * rather than silently hiding a record, exactly as the C-01 decoder does.
 *
 * NOTE ON COMPANY BINDING. The C-01 decoder binds each row to the active company
 * because a Cari account IS company-scoped. A Product is deliberately the opposite:
 * `PRD-SCOPE-001=A` gives ONE global Product UUID/code/card shared by both Göymen
 * companies, and §15 is explicit that "Frontend Product'a CompanyId eklemez". So
 * there is no per-row company check here — adding one would encode a company
 * scope the locked decision removed. What IS enforced is that the page carries no
 * company-scoped number at all (no stock, no price); those live in child records.
 */
export function decodeProductPage(
  raw: unknown,
  /** Whether the request carried `includeCount=true`. Binds the answer to the ask. */
  countRequested = false,
): ProductPage | null {
  if (typeof raw !== 'object' || raw === null) return null
  const p = raw as Record<string, unknown>
  if (!Array.isArray(p.items)) return null
  if (typeof p.hasMore !== 'boolean') return null
  if (p.nextCursor !== null && !isNonEmptyString(p.nextCursor, 4096)) return null

  // Both count fields are required by the envelope; an absent property is drift.
  if (!Object.hasOwn(p, 'totalCount') || !Object.hasOwn(p, 'countScope')) return null

  const total = decodeTotalCount(p.totalCount)
  if (!total.ok) return null

  // Count and scope travel together or not at all: a count without a scope could be
  // read as "all products", and a scope without a count says nothing.
  if (total.value === null) {
    if (p.countScope !== null) return null
  } else if (p.countScope !== PRODUCT_COUNT_SCOPE) return null

  // A count we did not ask for counted something we never specified, so we cannot
  // label it.
  if (!countRequested && total.value !== null) return null

  // hasMore <-> nextCursor must agree, or paging cannot continue safely.
  if (p.hasMore && p.nextCursor === null) return null
  if (!p.hasMore && p.nextCursor !== null) return null

  const items: ProductListItem[] = []
  const seen = new Set<string>()
  for (const rawItem of p.items) {
    const item = decodeListItem(rawItem)
    if (item === null) return null
    // §8.3: "Bir Product çok role/alias sahip diye satır çoğaltılmaz."
    if (seen.has(item.id)) return null
    seen.add(item.id)
    items.push(item)
  }

  return {
    items,
    nextCursor: (p.nextCursor as string | null) ?? null,
    hasMore: p.hasMore,
    totalCount: total.value,
    countScope: total.value === null ? null : PRODUCT_COUNT_SCOPE,
  }
}

/* ── Product 360 detail (spec §9) ─────────────────────────────────────────── */

/** A retired code that still resolves to this Product (§7, DEC-PRD-004). */
export interface ProductAlias {
  readonly code: string
  readonly retiredAt: string
}

/** Pinned template + version (§5.3, U-09 contract). */
export interface ProductTemplateRef {
  readonly id: string
  readonly name: string
  readonly version: string
}

/**
 * Unit role assignments (DEC-PRD-006: stock, purchase, sales, price, production and
 * package units are SEPARATE). `null` means unassigned — it is not a fallback to the
 * stock unit, which would be the screen inventing a conversion.
 */
export const UNIT_ROLE = [
  'stock',
  'purchase',
  'sales',
  'price',
  'production',
  'package',
] as const
export type UnitRoleName = (typeof UNIT_ROLE)[number]
export type ProductUnitRoles = Readonly<Record<UnitRoleName, string | null>>

/**
 * The 360 sections the SERVER says it can serve (§9.7 + §9 closing paragraph:
 * "İlgili modül route'u yoksa bölüm 'capability unavailable' olarak
 * gizlenir/işaretlenir; sahte metrik gösterilmez").
 *
 * A section absent from this list renders as unavailable. It never renders as `0`,
 * and it never renders as an empty table that reads like "there are none".
 */
export const DETAIL_SECTION = ['template', 'units', 'packageStandards', 'aliasHistory'] as const
export type DetailSection = (typeof DETAIL_SECTION)[number]

export interface ProductDetail {
  readonly id: string
  readonly code: string
  readonly name: string
  readonly classification: ProductClassification
  readonly roles: ProductRoles
  readonly lifecycle: ProductLifecycleValue
  readonly completeness: ProductCompleteness
  readonly version: string
  readonly aliases: readonly ProductAlias[]
  readonly template: ProductTemplateRef | null
  readonly unitRoles: ProductUnitRoles
  /**
   * Product-level tracking POLICY only (§5.3, DEC-PRD-003). The physical lot /
   * label / origin values and their quality-defect status live on the stock label
   * record, not on the Product master — this field says how that record will be
   * tracked, never what any label currently holds.
   */
  readonly trackingPolicy: string | null
  readonly createdAt: string
  readonly createdBy: string
  readonly updatedAt: string
  readonly updatedBy: string
  readonly lastActivatedAt: string | null
  readonly lastDeactivatedAt: string | null
  readonly deactivationReason: string | null
  readonly availableSections: readonly DetailSection[]
}

function decodeAlias(raw: unknown): ProductAlias | null {
  if (typeof raw !== 'object' || raw === null) return null
  const a = raw as Record<string, unknown>
  if (!isMachineToken(a.code, 64)) return null
  if (!isInstant(a.retiredAt)) return null
  return { code: a.code, retiredAt: a.retiredAt }
}

function decodeTemplate(raw: unknown): ProductTemplateRef | null | 'invalid' {
  if (raw === null) return null
  if (typeof raw !== 'object') return 'invalid'
  const t = raw as Record<string, unknown>
  if (!isBoundUuid(t.id)) return 'invalid'
  if (!isNonEmptyString(t.name, 200)) return 'invalid'
  // The pinned version is carried verbatim: §5.3 requires the VERSION that was
  // pinned, and re-deriving or defaulting it would let a master change rewrite what
  // a historical record meant (DEC-PRDCT-008).
  if (!isMachineToken(t.version, 64)) return 'invalid'
  return { id: t.id, name: t.name, version: t.version }
}

function decodeUnitRoles(raw: unknown): ProductUnitRoles | null {
  if (typeof raw !== 'object' || raw === null) return null
  const source = raw as Record<string, unknown>
  const out: Partial<Record<UnitRoleName, string | null>> = {}
  for (const role of UNIT_ROLE) {
    if (!Object.hasOwn(source, role)) return null
    if (!isNullableMachineToken(source[role], 32)) return null
    out[role] = (source[role] as string | null) ?? null
  }
  return out as ProductUnitRoles
}

function decodeSections(raw: unknown): readonly DetailSection[] | null {
  if (!Array.isArray(raw)) return null
  const out: DetailSection[] = []
  for (const value of raw) {
    if (typeof value !== 'string') return null
    if (!(DETAIL_SECTION as readonly string[]).includes(value)) return null
    const section = value as DetailSection
    // A duplicate would double-render a section; it also means we cannot trust the
    // list to be the server's considered answer.
    if (out.includes(section)) return null
    out.push(section)
  }
  return out
}

/** Decode a Product 360 response, bound to the id that was requested. */
export function decodeProductDetail(raw: unknown, expectedId: string): ProductDetail | null {
  if (typeof raw !== 'object' || raw === null) return null
  const d = raw as Record<string, unknown>
  if (!isBoundUuid(d.id)) return null
  /**
   * The response must describe the record we asked for. A well-formed 200 about a
   * DIFFERENT Product is not a detail we may render: the user would be looking at
   * one record believing it is another, and any edit they then make would be aimed
   * at the wrong revision.
   */
  if (d.id !== expectedId) return null
  if (!isMachineToken(d.code, 64)) return null
  if (!isNonEmptyString(d.name, 250)) return null
  const lifecycle = decodeLifecycle(d.lifecycle)
  if (lifecycle === null) return null
  if (!isLosslessVersion(d.version)) return null
  if (!isNullableText(d.trackingPolicy, 120)) return null
  if (!isInstant(d.createdAt) || !isInstant(d.updatedAt)) return null
  if (!isNonEmptyString(d.createdBy, 200) || !isNonEmptyString(d.updatedBy, 200)) return null
  if (!isNullableInstant(d.lastActivatedAt) || !isNullableInstant(d.lastDeactivatedAt)) return null
  if (!isNullableText(d.deactivationReason, 500)) return null

  const classification = decodeClassification(d.classification)
  if (classification === null) return null
  const roles = decodeRoles(d.roles)
  if (roles === null) return null
  const completeness = decodeCompleteness(d.completeness)
  if (completeness === null) return null
  const unitRoles = decodeUnitRoles(d.unitRoles)
  if (unitRoles === null) return null
  const sections = decodeSections(d.availableSections)
  if (sections === null) return null

  const template = decodeTemplate(d.template)
  if (template === 'invalid') return null

  if (!Array.isArray(d.aliases)) return null
  const aliases: ProductAlias[] = []
  const seenAliasCodes = new Set<string>()
  for (const rawAlias of d.aliases) {
    const alias = decodeAlias(rawAlias)
    if (alias === null) return null
    // §7: a retired code is non-reusable, so it can appear at most once.
    if (seenAliasCodes.has(alias.code)) return null
    // ...and it can never equal the live code, or "eski kod" would label the
    // current one.
    if (alias.code === d.code) return null
    seenAliasCodes.add(alias.code)
    aliases.push(alias)
  }

  /**
   * A Draft has never been activated, and an Active product cannot carry a
   * deactivation reason. Both pairings are things the server states twice; when the
   * two statements disagree we cannot tell which one is true.
   */
  if (lifecycle.known) {
    if (lifecycle.value === 'Draft' && d.lastActivatedAt !== null) return null
    if (lifecycle.value !== 'Inactive' && d.deactivationReason !== null) return null
  }
  /**
   * Both checks are skipped for an unrecognised state, and that is the point: they
   * ask whether two statements about a KNOWN state agree. We cannot judge whether a
   * deactivation reason belongs to a status we do not understand, and inventing a
   * verdict either way would be worse than carrying the pair as the server sent it.
   */

  return {
    id: d.id,
    code: d.code,
    name: d.name,
    classification,
    roles,
    lifecycle,
    completeness,
    version: d.version,
    aliases,
    template,
    unitRoles,
    trackingPolicy: (d.trackingPolicy as string | null) ?? null,
    createdAt: d.createdAt,
    createdBy: d.createdBy,
    updatedAt: d.updatedAt,
    updatedBy: d.updatedBy,
    lastActivatedAt: (d.lastActivatedAt as string | null) ?? null,
    lastDeactivatedAt: (d.lastDeactivatedAt as string | null) ?? null,
    deactivationReason: (d.deactivationReason as string | null) ?? null,
    availableSections: sections,
  }
}

/* ── Draft create result (spec §6.1, §10) ─────────────────────────────────── */

export interface ProductDraftCreated {
  readonly id: string
  /** Server-generated. §7: the browser never predicted or sent it. */
  readonly code: string
  readonly name: string
  readonly lifecycle: ProductLifecycle
  readonly version: string
}

/**
 * Decode a `201 Created`. Fails closed unless the new record is a DRAFT.
 *
 * §6.1 is unconditional — "Yeni kayıt her zaman Draft doğar" — and §6.2 keeps save
 * and activate as separate commands with separate audit records. A 201 that says
 * `Active` therefore means the server activated something this screen never asked
 * to activate; reporting that as a normal success would hide an activation from the
 * person responsible for it. It is rejected, and the caller treats a malformed 201
 * as an UNKNOWN outcome (the record may exist) rather than a failure.
 */
export function decodeProductDraftCreated(raw: unknown): ProductDraftCreated | null {
  if (typeof raw !== 'object' || raw === null) return null
  const c = raw as Record<string, unknown>
  if (!isBoundUuid(c.id)) return null
  if (!isMachineToken(c.code, 64)) return null
  if (!isNonEmptyString(c.name, 250)) return null
  if (c.lifecycle !== 'Draft') return null
  if (!isLosslessVersion(c.version)) return null
  return { id: c.id, code: c.code, name: c.name, lifecycle: 'Draft', version: c.version }
}

/* ── Problem codes ────────────────────────────────────────────────────────── */

/**
 * Stable PLATFORM codes (`docs/features/PLATFORM_HTTP_ERROR_CONTRACT_v1.md` §2).
 * These are accepted and owned by the platform, so they are safe to branch on.
 */
export const PLATFORM_PROBLEM_CODE = {
  correlationMetadataInvalid: 'correlation_metadata_invalid',
  companyContextInvalid: 'company_context_invalid',
  authenticationRequired: 'authentication_required',
  forbidden: 'forbidden',
  internalError: 'internal_error',
  serviceUnavailable: 'service_unavailable',
} as const

/**
 * PROPOSED module-owned codes. The platform error contract §2 reserves module
 * `400`/`404`/`409` codes to the module OWNER, which for Product is
 * CODEX-BACKEND-URUN — so these are a request, not an accepted vocabulary.
 *
 * The screens must therefore stay correct when a code here never arrives, and when
 * one arrives that is not listed: `productProblemCodeOf` returns null for anything
 * unknown and the caller falls back to the status-based state. Branching on the
 * localized title/detail is never allowed.
 */
export const PRODUCT_PROBLEM_CODE = {
  /** §8.3: a cursor that no longer resolves. Recoverable by restarting the page. */
  invalidCursor: 'invalid_cursor',
  /** §10.2: code/alias exact collision is a hard block. */
  productCodeCollision: 'product_code_collision',
  /** §10.3: server-side field validation. */
  validationFailed: 'validation_failed',
  /** §14: the revision the client held is stale. */
  revisionConflict: 'revision_conflict',
  /** Command metadata / idempotency key refusals. */
  commandMetadataInvalid: 'command_metadata_invalid',
  idempotencyConflict: 'idempotency_conflict',
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
 * Field-level validation errors from a `validation_failed` body.
 *
 * §10.3: "Alan-level hata ilgili input ile programatik ilişkilidir". Only fields the
 * form actually renders are accepted — a server naming an unknown field would
 * otherwise produce an error message attached to nothing, which the user can
 * neither see nor clear.
 */
export function productFieldErrorsOf(
  raw: unknown,
  knownFields: readonly string[],
): Readonly<Record<string, string>> {
  if (typeof raw !== 'object' || raw === null) return {}
  const errors = (raw as Record<string, unknown>).errors
  if (typeof errors !== 'object' || errors === null) return {}
  const out: Record<string, string> = {}
  for (const field of knownFields) {
    const value = (errors as Record<string, unknown>)[field]
    if (typeof value === 'string' && value.trim().length > 0) out[field] = value.trim()
    else if (Array.isArray(value)) {
      const first = value.find((entry) => typeof entry === 'string' && entry.trim().length > 0)
      if (typeof first === 'string') out[field] = first.trim()
    }
  }
  return out
}
