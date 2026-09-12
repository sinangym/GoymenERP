/**
 * The account-scoped contact/address edit: what a basic save is allowed to send,
 * and what "the user did not touch this" means once it reaches the wire.
 *
 * SOURCE. CHATGPT-BUSINESS handoff of 12 September (issue #66 comment
 * `5642102538`, field dictionary in #232 comment `5642117532`) plus CARI-DEC-004 §6:
 * address, phone, e-mail and ordinary contact changes are applied directly with the
 * update permission and an audit record, while legal name, official identifiers,
 * registry numbers and IBAN go through the proposal + single-approver flow. The
 * first write is the SELECTED `CompanyPartyAccount`'s operational contact and
 * address; the shared Party identity is read-only here.
 *
 * WHY THIS FILE HOLDS NO FIELD NAME AND NO LENGTH. The exact DTO — property names,
 * which properties are nullable, and the one authoritative maximum for each —
 * belongs to Backend's OpenAPI and to nobody else. This module is the SEMANTICS;
 * ./contactInformationContract.ts is the single place that names properties, and it
 * says which source revision it was read from. `FieldRule.maxLength` is optional
 * for the same reason: until Backend publishes the number, the server's own
 * validation decides and nothing here invents a limit — or, worse, truncates to one.
 *
 * THE WIRE RULES, from the handoff and confirmed against Backend's DTO:
 *
 *  1. an omitted property is retained — for scalars AND for whole collections;
 *  2. an explicit `null` clears an OPTIONAL scalar, and nothing else;
 *  3. an empty string becomes `null` after trimming;
 *  4. an omitted collection element is NOT a deletion: passivity is an explicit
 *     `deactivate` operation, and a row that merely left the editor is retained;
 *  5. a `null` ARRAY is rejected by the server, so an unchanged collection is
 *     omitted entirely — never sent as `null` and never sent as `[]` to mean
 *     "empty it";
 *  6. an unknown property is rejected by the server (`JsonUnmappedMemberHandling
 *     .Disallow`), so a field with no rule is refused here rather than sent.
 *
 * Rule 4 is the one that silently destroys data if it is got wrong: an editor that
 * sends "the rows I still have" deletes every row it never loaded.
 *
 * CHILD IDENTITY. Every child element carries an id, including a brand-new one —
 * the client mints it. It must be minted when the ROW IS CREATED in the form and
 * never at submit time: a same-key retry has to carry the identical payload, and a
 * freshly minted id would turn a retry into a different request, which the server
 * then refuses as an idempotency conflict.
 */

/**
 * What the server does with one child element. There is no `delete`: a used address
 * is deactivated, not removed, and reactivation is its own operation rather than an
 * update that happens to flip a flag.
 */
export type ChildOperation = 'upsert' | 'deactivate' | 'reactivate'

/** What the editor is doing with a row. `unchanged` emits nothing at all. */
export type RowIntent = 'unchanged' | ChildOperation

/**
 * Which save a field belongs to. `critical` fields (legal name, official
 * identifiers, registry ids, bank accounts) can be EDITED in the card, but their
 * change goes to the proposal + approval command — never into the basic payload, and
 * never announced together with it as one "everything was saved".
 */
export type FieldScope = 'basic' | 'critical'

/**
 * How a value is checked. It says nothing about how the value is stored or
 * displayed, and no kind here makes any claim about the value being real: an
 * e-mail that passes `email` has a plausible SHAPE, and has not been verified,
 * delivered to, or matched against any registry.
 */
export type ValueKind = 'text' | 'phone' | 'email' | 'url' | 'country'

export interface FieldRule {
  readonly scope: FieldScope
  /**
   * A CLOSED set published by the API owner. A value outside it is refused here,
   * because the server refuses it too and this way the message lands on the field.
   *
   * Absent means open text — not "any value is fine", just that the contract does not
   * restrict it. The difference matters: a closed set may be rendered as a choice, and
   * an open one must never be, or the user is offered a list nobody promised.
   */
  readonly options?: readonly string[]
  /**
   * Fixed once the row exists on the server. A channel's `kind` is the published case:
   * changing it for an existing UUID is refused, so the edit is offered as a new
   * channel rather than a silent reinterpretation of the old one.
   */
  readonly immutableOnExisting?: boolean
  /**
   * Not applicable in THIS row's current shape, so any value is refused. An extension
   * belongs to a phone: without a phone, or on an e-mail channel, it has nothing to
   * extend. This is how "clearing a phone must clear its extension too" surfaces — as a
   * visible refusal on the extension, never as a value the client deletes by itself.
   */
  readonly forbidden?: boolean
  /**
   * Required ONCE THE ROW IS BEING WRITTEN. An untouched, entirely empty row is not
   * a row with missing fields — see `isRowEmpty` — which is why the handoff says
   * that in a FILLED row the purpose, country and address line are required.
   */
  readonly required: boolean
  /**
   * The one authoritative maximum, from Backend's OpenAPI. ABSENT means Backend has
   * not published it yet: no client-side length check runs, the server's 400 decides,
   * and the value is never shortened to fit a number we guessed.
   */
  readonly maxLength?: number
  readonly kind: ValueKind
}

export type FieldProblem =
  /** Empty in a row that is being written. */
  | 'required'
  /** Over the published contract length. The value is NOT truncated. */
  | 'too-long'
  /** Not shaped like an address. Not a claim about reachability either way. */
  | 'email-format'
  /** Only `http` and `https` are supported. */
  | 'url-unsupported'
  /** Outside the closed set the contract publishes for this field. */
  | 'not-allowed'
  /**
   * Not two uppercase ASCII letters. That is the whole rule the API owner states —
   * there is no registry check, so a value that passes is well-formed and has NOT
   * been confirmed to be a country that exists.
   */
  | 'country-format'
  /** `null` offered for a field the contract does not let a client clear. */
  | 'not-clearable'
  /** A critical field was offered to the basic save. It has its own command. */
  | 'critical-scope'
  /** No rule for this key. The server rejects unknown properties; so do we. */
  | 'unknown-field'
  /** Changed on a row the server already holds, where the contract fixes it. */
  | 'immutable'
  /** Has no meaning in this row as it stands — an extension with no phone to extend. */
  | 'not-applicable'
  /** Carries a control character the contract rejects. */
  | 'control-characters'

export type RowProblem =
  /** A row with no id. Every child change carries one, new rows included. */
  | 'row-identity'
  /** An upsert with nothing in it. */
  | 'row-empty'
  /** Two operations for the same id in one collection. The server rejects the command. */
  | 'row-duplicate'
  /** A status change asked for a row the server does not hold yet. */
  | 'status-needs-existing'
  /** More operations in one collection than one request may carry. */
  | 'too-many-operations'

/** A field's value as the contract carries it: a trimmed string, or explicit null. */
export type FieldValue = string | null

export type FieldCheck =
  | { readonly ok: true; readonly value: FieldValue }
  | { readonly ok: false; readonly problem: FieldProblem }

/**
 * Trim, then treat the empty string as absent (rule 3).
 *
 * The trim is the ONLY thing done to the string. Nothing is collapsed, re-cased,
 * re-spaced or reordered: for a phone number or an address line every remaining
 * character is the user's data, and a client that tidies it is guessing.
 */
export function trimToNull(raw: string): FieldValue {
  const trimmed = raw.trim()
  return trimmed.length === 0 ? null : trimmed
}

/**
 * A phone is kept exactly as typed, minus surrounding whitespace.
 *
 * The handoff is explicit that a leading `+` or `0` and the extension survive, and
 * that no country or operator is assumed. So there is no stripping of `+`, no
 * dropping of a national trunk `0`, no re-grouping of digits and no `+90` prepended
 * to a bare local number: every one of those is a claim about a numbering plan the
 * client cannot know, and each of them silently changes who gets called.
 */
export function normalizePhone(raw: string): FieldValue {
  return trimToNull(raw)
}

/**
 * A SHAPE check, deliberately narrow: no whitespace, exactly one `@` with something on
 * each side, and no dangling or doubled dot in the domain. It is not a deliverability
 * test, not a DNS lookup and not a commercial verification — a value that passes must
 * never be presented as a verified address.
 *
 * A DOTTED DOMAIN IS NOT REQUIRED, and requiring one was a second business rule this
 * client invented. The pinned contract validates "one syntactically valid mailbox", and
 * the server's own validator accepts `kisi@example` — so a counterparty whose stored
 * e-mail has no dotted domain could not have their PHONE corrected, because the section
 * refused to save on a field the user never touched and the server was content with.
 * Nothing here may be stricter than the contract it consumes.
 */
export function isEmailShaped(value: string): boolean {
  if (/\s/.test(value)) return false
  const at = value.indexOf('@')
  if (at <= 0 || at !== value.lastIndexOf('@')) return false
  const domain = value.slice(at + 1)
  if (domain.length === 0 || domain.startsWith('.') || domain.endsWith('.')) return false
  return !domain.includes('..')
}

/** `http`/`https` only, and it must parse as an absolute URL. */
export function isSupportedWebUrl(value: string): boolean {
  let parsed: URL
  try {
    parsed = new URL(value)
  } catch {
    return false
  }
  return parsed.protocol === 'http:' || parsed.protocol === 'https:'
}

/**
 * Exactly two uppercase ASCII letters — the whole rule the API owner states.
 *
 * There is NO registry confirmation behind it, so `XX` passes: the value is
 * well-formed, and nothing here claims it names a country that exists. And there is
 * no default: a form that pre-filled `TR` would be inventing the counterparty's
 * country, which is precisely what a foreign party's card must not do.
 */
export function isCountryCodeShaped(value: string): boolean {
  return /^[A-Z]{2}$/.test(value)
}

/** One field, from raw input to the value the contract would carry — or a problem. */
export function checkField(rule: FieldRule, raw: string): FieldCheck {
  const value = rule.kind === 'phone' ? normalizePhone(raw) : trimToNull(raw)

  // Refused before anything else, including `required`: a field that does not apply
  // cannot be missing.
  if (rule.forbidden === true) {
    return value === null ? { ok: true, value: null } : { ok: false, problem: 'not-applicable' }
  }

  if (value === null) {
    if (rule.required) return { ok: false, problem: 'required' }
    return { ok: true, value: null }
  }
  if (hasControlCharacters(value)) return { ok: false, problem: 'control-characters' }
  // A closed set is checked before anything else: for those fields nothing but
  // membership is meaningful, and a length rule would be beside the point.
  if (rule.options !== undefined && !rule.options.includes(value)) {
    return { ok: false, problem: 'not-allowed' }
  }
  // Refused, never shortened: a truncated value is a different record. When Backend
  // has not published a maximum there is simply no check here.
  if (rule.maxLength !== undefined && value.length > rule.maxLength) {
    return { ok: false, problem: 'too-long' }
  }
  if (rule.kind === 'email' && !isEmailShaped(value)) return { ok: false, problem: 'email-format' }
  if (rule.kind === 'url' && !isSupportedWebUrl(value)) return { ok: false, problem: 'url-unsupported' }
  if (rule.kind === 'country' && !isCountryCodeShaped(value)) return { ok: false, problem: 'country-format' }
  return { ok: true, value }
}

/**
 * A control character INSIDE the value, after the surrounding whitespace is gone.
 *
 * The contract rejects control characters, and its own validator does so BEFORE trimming
 * — a CR or LF that vanished in a trim was a real validation hole upstream. The surrounding
 * whitespace is not the interesting case here, because the contract trims it too and this
 * client sends the trimmed value; what would otherwise reach the server untouched is a
 * control character in the MIDDLE of an address line or a name, and that comes back as a
 * 400 with no field map to place it on. So it is caught here, next to the field.
 */
export function hasControlCharacters(value: string): boolean {
  for (const char of value) {
    const code = char.codePointAt(0) ?? 0
    if (code <= 0x1f || code === 0x7f) return true
  }
  return false
}

/** True when every value in the row is empty — an untouched row, not an invalid one. */
export function isRowEmpty(values: Readonly<Record<string, string>>): boolean {
  return Object.values(values).every((raw) => trimToNull(raw) === null)
}

/**
 * A v4 UUID for a row the user just added, from `getRandomValues`.
 *
 * Not `crypto.randomUUID`: that is only defined in a secure context, and a
 * plain-HTTP intranet host must not silently fall back to something weaker — the
 * same reason the idempotency key is built this way.
 *
 * Call it when the ROW APPEARS, not when the form is submitted. The id is part of
 * the payload a same-key retry has to reproduce exactly.
 */
export function generateRowId(source: Pick<Crypto, 'getRandomValues'> = globalThis.crypto): string {
  const bytes = new Uint8Array(16)
  source.getRandomValues(bytes)
  bytes[6] = (bytes[6] & 0x0f) | 0x40
  bytes[8] = (bytes[8] & 0x3f) | 0x80
  const hex: string[] = []
  for (const byte of bytes) hex.push(byte.toString(16).padStart(2, '0'))
  const s = hex.join('')
  return `${s.slice(0, 8)}-${s.slice(8, 12)}-${s.slice(12, 16)}-${s.slice(16, 20)}-${s.slice(20)}`
}

export interface FieldPatch {
  /** Exactly the fields that changed. An untouched field is absent (rule 1). */
  readonly patch: Readonly<Record<string, FieldValue>>
  /** Per-field, so the screen can put each message beside its own input. */
  readonly problems: Readonly<Record<string, FieldProblem>>
  /** The fields that are IN the patch — valid, and different from the read. */
  readonly changed: readonly string[]
  /**
   * The fields the user actually altered, valid or not.
   *
   * Separate from `changed` because they answer different questions, and conflating them
   * lost work: an edit whose ONLY change is invalid produces an empty patch, and a screen
   * that derived "is there unsaved work" from the patch then reported the form clean —
   * no field error, a closed Save, and no warning when the user navigated away or
   * switched company. Whether something may be SENT and whether something would be LOST
   * are not the same fact.
   */
  readonly touched: readonly string[]
}

/**
 * Diff one flat group of fields: only what changed, and only what this save owns.
 *
 * `original` is the last read's values — the normalized ones, as the contract
 * carries them. `edited` is what is in the inputs now. A field whose normalized
 * input equals the original is NOT in the patch, so whitespace typed into an
 * untouched field cannot make it look edited, and nothing the user never opened is
 * re-asserted.
 */
export function buildFieldPatch(
  rules: Readonly<Record<string, FieldRule>>,
  original: Readonly<Record<string, FieldValue>>,
  edited: Readonly<Record<string, string>>,
): FieldPatch {
  const patch: Record<string, FieldValue> = {}
  const problems: Record<string, FieldProblem> = {}
  const changed: string[] = []
  const touched: string[] = []

  for (const [key, raw] of Object.entries(edited)) {
    /**
     * Recorded FIRST, before any rule can reject the value: an alteration the contract
     * refuses is still an alteration, and the screen has to know it is there.
     */
    const previous = Object.hasOwn(original, key) ? original[key] : null
    if (trimToNull(raw) !== previous) touched.push(key)

    const rule = rules[key]
    if (rule === undefined) {
      // The server rejects an unknown property outright, so sending one would fail
      // the whole save. Refusing here keeps the failure next to the field.
      problems[key] = 'unknown-field'
      continue
    }
    /**
     * The basic/critical boundary, enforced where the payload is built rather than
     * where the form is drawn. A mixed edit is legitimate — the user corrects a phone
     * and proposes a new legal name in one sitting — but the two effects have different
     * commands, different authority and different outcomes, and one "saved" message for
     * both would report an approval that has not happened.
     */
    if (rule.scope === 'critical') {
      problems[key] = 'critical-scope'
      continue
    }

    const checked = checkField(rule, raw)
    if (!checked.ok) {
      problems[key] = checked.problem
      continue
    }
    const before = previous
    if (checked.value === before) continue
    /**
     * Clearing is allowed only where the contract says a client may clear (rule 2).
     * `checkField` already refuses an empty required field, so this is the case where a
     * rule is optional in the form but the contract does not accept `null` for it.
     */
    if (checked.value === null && before !== null && rule.required) {
      problems[key] = 'not-clearable'
      continue
    }
    patch[key] = checked.value
    changed.push(key)
  }

  return { patch, problems, changed, touched }
}

export interface EditedRow {
  /** Always present: the server's id, or the id the FORM minted when it was added. */
  readonly id: string
  readonly intent: RowIntent
  readonly values: Readonly<Record<string, string>>
  /** A contact's channels. Rows that own no nested collection leave this out. */
  readonly children?: readonly EditedRow[]
}

/** The last read's rows, keyed by id, with their nested rows alongside. */
export interface OriginalRow {
  readonly values: Readonly<Record<string, FieldValue>>
  readonly children?: Readonly<Record<string, OriginalRow>>
}

/**
 * Refine a row's rules from the row's OWN values.
 *
 * Some limits are conditional in the contract: a channel's value is a 32-character
 * phone or a 254-character mailbox depending on its `kind`, and its extension applies
 * only to a phone. Resolving that here keeps every check on one path, so a conditional
 * rule blocks the save exactly like a fixed one instead of being validated twice in two
 * places that can disagree.
 */
export type RuleRefiner = (
  values: Readonly<Record<string, string>>,
  base: Readonly<Record<string, FieldRule>>,
) => Readonly<Record<string, FieldRule>>

/** How a row's nested collection is named and ruled on the wire. */
export interface NestedSpec {
  /** The property the nested array sits on, e.g. the channels of a contact. */
  readonly property: string
  readonly rules: Readonly<Record<string, FieldRule>>
  readonly refine?: RuleRefiner
}

/** One request may carry at most this many operations per collection (pinned contract). */
export const MAX_OPERATIONS_PER_COLLECTION = 100

export interface CollectionPatch {
  /** Wire-ready elements: `{ id, operation, ...changed fields, [nested]: [...] }`. */
  readonly elements: readonly Readonly<Record<string, unknown>>[]
  /** Any row was altered — including a row whose alteration is currently invalid. */
  readonly touched: boolean
  /** Keyed `<index>` for a row problem, `<index>.<field>`, `<index>.<n>.<field>`. */
  readonly problems: Readonly<Record<string, FieldProblem | RowProblem>>
  /**
   * Rows the server holds that this editor is not speaking about. They are KEPT, and
   * named here so the screen can say so instead of letting the user believe a row
   * that scrolled out of a filter was removed.
   */
  readonly retainedIds: readonly string[]
}

/**
 * Build one collection's elements from explicit intents.
 *
 * `originals` is what the last read returned, keyed by row id. Anything in it that
 * no row mentions is reported as retained and NOTHING is emitted for it — the single
 * most destructive mistake available here is to treat "the editor no longer lists it"
 * as "delete it".
 */
export function buildCollectionPatch(
  rules: Readonly<Record<string, FieldRule>>,
  rows: readonly EditedRow[],
  originals: Readonly<Record<string, OriginalRow>>,
  nested?: NestedSpec,
  refine?: RuleRefiner,
): CollectionPatch {
  const elements: Readonly<Record<string, unknown>>[] = []
  const problems: Record<string, FieldProblem | RowProblem> = {}
  const mentioned = new Set<string>()
  let touched = false

  rows.forEach((row, index) => {
    if (row.id.length === 0) {
      problems[`${index}`] = 'row-identity'
      touched = true
      return
    }
    // Two operations for one id are rejected by the server for the WHOLE command, so
    // the second one is refused here where it can be pointed at.
    if (mentioned.has(row.id)) {
      problems[`${index}`] = 'row-duplicate'
      touched = true
      return
    }
    mentioned.add(row.id)

    const before = originals[row.id]
    const isNew = before === undefined

    // Passivity is chosen, never inferred, and it is not an edit: the element carries
    // the identity and the operation and nothing else — no value fields and no nested
    // updates. A status change also needs a row the server already holds.
    if (row.intent === 'deactivate' || row.intent === 'reactivate') {
      // An explicitly requested status change is unsaved work whether or not it can be
      // sent, so it counts as touched before the identity check can reject it.
      touched = true
      if (isNew) {
        problems[`${index}`] = 'status-needs-existing'
        return
      }
      elements.push({ id: row.id, operation: row.intent })
      return
    }
    if (row.intent === 'upsert' && isNew && isRowEmpty(row.values)) {
      problems[`${index}`] = 'row-empty'
      // A row the user added and left blank IS unsaved work: it is on screen, it blocks
      // the save, and the screen must be able to say so and offer to drop it.
      touched = true
      return
    }

    let rowFailed = false
    const rowRules = refine === undefined ? rules : refine(row.values, rules)
    const built = buildFieldPatch(rowRules, before?.values ?? {}, row.values)
    if (built.touched.length > 0) touched = true
    for (const [key, problem] of Object.entries(built.problems)) {
      problems[`${index}.${key}`] = problem
      rowFailed = true
    }
    // Fixed-for-an-existing-row fields: the change is refused rather than sent, so the
    // user is offered a new row instead of a rewritten one.
    if (!isNew) {
      for (const key of built.changed) {
        if (rowRules[key]?.immutableOnExisting === true) {
          problems[`${index}.${key}`] = 'immutable'
          rowFailed = true
        }
      }
    }

    let nestedElements: readonly Readonly<Record<string, unknown>>[] = []
    if (nested !== undefined && row.children !== undefined) {
      const child = buildCollectionPatch(
        nested.rules,
        row.children,
        before?.children ?? {},
        undefined,
        nested.refine,
      )
      if (child.touched) touched = true
      for (const [key, problem] of Object.entries(child.problems)) {
        problems[`${index}.${nested.property}.${key}`] = problem
        rowFailed = true
      }
      nestedElements = child.elements
    }
    if (rowFailed) return

    /**
     * The operation is derived from what actually changed, not from the form's flag.
     *
     * A row whose own fields did not move but whose nested rows did is still sent, as
     * an `upsert` carrying only the nested array: omission retains the scalars, so it
     * says "leave my own fields alone, here is what changed underneath" — and without
     * it a corrected phone channel would have no element to travel in. Conversely a row
     * the user opened and left as it was sends nothing, whatever its flag says, because
     * a stale flag must not be able to spend a write.
     */
    if (built.changed.length === 0 && nestedElements.length === 0) return

    const element: Record<string, unknown> = { id: row.id, operation: 'upsert', ...built.patch }
    if (nested !== undefined && nestedElements.length > 0) element[nested.property] = nestedElements
    elements.push(element)
  })

  const retainedIds = Object.keys(originals).filter((id) => !mentioned.has(id))
  return { elements, touched, problems, retainedIds }
}

/** One collection as it appears in the `changes` object. */
export interface CollectionSpec {
  /** The property the array sits on. */
  readonly property: string
  readonly rules: Readonly<Record<string, FieldRule>>
  readonly refine?: RuleRefiner
  readonly nested?: NestedSpec
}

export interface ChangesSpec {
  /** The flat, nullable scalars of the operational contact block. */
  readonly scalars: Readonly<Record<string, FieldRule>>
  /** Conditional scalar rules — an extension applies only while there is a phone. */
  readonly refineScalars?: RuleRefiner
  readonly collections: readonly CollectionSpec[]
}

export interface EditedState {
  readonly scalars: Readonly<Record<string, string>>
  /** Keyed by the collection's wire property. */
  readonly collections: Readonly<Record<string, readonly EditedRow[]>>
}

export interface OriginalState {
  readonly scalars: Readonly<Record<string, FieldValue>>
  readonly collections: Readonly<Record<string, Readonly<Record<string, OriginalRow>>>>
}

export interface ChangesBody {
  /**
   * The `changes` object exactly as it goes on the wire. Every absent key is a
   * deliberate retain, and no key is ever `null` for a collection (rule 5).
   */
  readonly changes: Readonly<Record<string, unknown>>
  readonly problems: Readonly<Record<string, FieldProblem | RowProblem>>
  /** Per collection property: the ids this editor did not speak about. */
  readonly retained: Readonly<Record<string, readonly string[]>>
  /**
   * The user has unsaved work — valid or not.
   *
   * This is what the dirty scope, the leave/switch warning and the enabled Save are
   * derived from. It is deliberately NOT `hasChanges`: an edit that is currently invalid
   * must still be defended, or the screen silently discards it.
   */
  readonly dirty: boolean
  /** There is something the contract would accept sending. */
  readonly hasChanges: boolean
}

/**
 * Build the whole `changes` object for the basic save.
 *
 * Problems are keyed so a screen can find each message's own input:
 * `<scalar>`, `<collection>.<index>`, `<collection>.<index>.<field>` and
 * `<collection>.<index>.<nested>.<index>.<field>`.
 */
export function buildChanges(
  spec: ChangesSpec,
  original: OriginalState,
  edited: EditedState,
): ChangesBody {
  const scalarRules =
    spec.refineScalars === undefined ? spec.scalars : spec.refineScalars(edited.scalars, spec.scalars)
  const scalars = buildFieldPatch(scalarRules, original.scalars, edited.scalars)
  const changes: Record<string, unknown> = { ...scalars.patch }
  const problems: Record<string, FieldProblem | RowProblem> = { ...scalars.problems }
  const retained: Record<string, readonly string[]> = {}
  let collectionElements = 0
  let touched = scalars.touched.length > 0

  for (const collection of spec.collections) {
    const rows = edited.collections[collection.property] ?? []
    const originals = original.collections[collection.property] ?? {}
    const built = buildCollectionPatch(
      collection.rules,
      rows,
      originals,
      collection.nested,
      collection.refine,
    )
    for (const [key, problem] of Object.entries(built.problems)) {
      problems[`${collection.property}.${key}`] = problem
    }
    retained[collection.property] = built.retainedIds
    if (built.touched) touched = true
    /**
     * One request carries at most 100 operations per collection. Over that the server
     * rejects the whole command, so the refusal belongs here where it can name which
     * collection — and it is a REQUEST limit, not a cap on how many rows the card may
     * hold, so the message must not read as "this counterparty has too many addresses".
     */
    if (built.elements.length > MAX_OPERATIONS_PER_COLLECTION) {
      problems[collection.property] = 'too-many-operations'
    }
    /**
     * An unchanged collection is OMITTED. Not `null` — the server rejects that — and
     * not `[]`, which would read as an instruction even though it carries none.
     */
    if (built.elements.length > 0) {
      changes[collection.property] = built.elements
      collectionElements += built.elements.length
    }
  }

  return {
    changes,
    problems,
    retained,
    dirty: touched,
    hasChanges: scalars.changed.length > 0 || collectionElements > 0,
  }
}

/**
 * Whether the save may be offered at all: something changed, and nothing is wrong.
 * "Save" stays disabled while there is no change — the handoff's own rule — because
 * a save that sends an empty patch spends an idempotency key and an audit record to
 * say nothing.
 */
export function canSubmitChanges(body: ChangesBody): boolean {
  return body.hasChanges && Object.keys(body.problems).length === 0
}
