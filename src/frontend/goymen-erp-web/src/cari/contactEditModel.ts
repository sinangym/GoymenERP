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
 * WHY THIS FILE HOLDS NO FIELD TABLE. The exact DTO — property names, which
 * properties are nullable, and the one authoritative length for each — belongs to
 * Backend's OpenAPI and to nobody else. A table here would be a second truth that is
 * wrong the moment the real one lands. So the RULES are supplied by the caller and
 * the SEMANTICS live here, which is where the dangerous part is anyway: three of the
 * four rules below are about what the client must NOT say.
 *
 * THE FOUR WIRE RULES, verbatim from the handoff:
 *
 *  1. a field that is not sent does not change;
 *  2. an explicit `null` clears an OPTIONAL single field, and nothing else;
 *  3. an empty string becomes `null` after trimming;
 *  4. a collection element that is not sent is NOT a deletion and NOT a
 *     deactivation.
 *
 * Rule 4 is the one that silently destroys data if it is got wrong: an editor that
 * PUTs "the rows I still have" deletes every row it never loaded. So a row is only
 * ever added, updated or deactivated by an explicit intent, and a row that has
 * simply left the editor is reported back as retained.
 *
 * AND NOTHING IS NORMALIZED SILENTLY. A value longer than its limit is a refused
 * save, never a truncation, because a quietly shortened tax office or address line
 * is a wrong record that looks like a saved one.
 */

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
export type ValueKind = 'text' | 'phone' | 'email' | 'url'

export interface FieldRule {
  readonly scope: FieldScope
  /**
   * Required ONCE THE ROW IS BEING WRITTEN. An untouched, entirely empty row is not
   * a row with missing fields — see `isRowEmpty` — which is why the handoff says
   * that in a FILLED row the purpose, country and address line are required.
   */
  readonly required: boolean
  /**
   * The single authoritative maximum, from Backend's OpenAPI. The lengths in the
   * Business table are a technical proposal; this type exists so there is exactly one
   * place that has to change when the real contract is published.
   */
  readonly maxLength: number
  readonly kind: ValueKind
}

export type FieldProblem =
  /** Empty in a row that is being written. */
  | 'required'
  /** Over the contract length. The value is NOT truncated. */
  | 'too-long'
  /** Not shaped like an address. Not a claim about reachability either way. */
  | 'email-format'
  /** Only `http` and `https` are supported. */
  | 'url-unsupported'
  /** `null` offered for a field the contract does not let a client clear. */
  | 'not-clearable'
  /** A critical field was offered to the basic save. It has its own command. */
  | 'critical-scope'
  /** No rule for this key: an unruled field must never reach the wire. */
  | 'unknown-field'

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
 * A SHAPE check, deliberately narrow: one `@`, something before it, and a dotted
 * domain with no whitespace. It is not a deliverability test, not a DNS lookup and
 * not a commercial verification — a value that passes must never be presented as a
 * verified address.
 */
export function isEmailShaped(value: string): boolean {
  if (/\s/.test(value)) return false
  const at = value.indexOf('@')
  if (at <= 0 || at !== value.lastIndexOf('@')) return false
  const domain = value.slice(at + 1)
  if (domain.length === 0 || domain.startsWith('.') || domain.endsWith('.')) return false
  if (domain.includes('..')) return false
  return domain.includes('.')
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

/** One field, from raw input to the value the contract would carry — or a problem. */
export function checkField(rule: FieldRule, raw: string): FieldCheck {
  const value = rule.kind === 'phone' ? normalizePhone(raw) : trimToNull(raw)

  if (value === null) {
    if (rule.required) return { ok: false, problem: 'required' }
    return { ok: true, value: null }
  }
  // Refused, never shortened: a truncated value is a different record.
  if (value.length > rule.maxLength) return { ok: false, problem: 'too-long' }
  if (rule.kind === 'email' && !isEmailShaped(value)) return { ok: false, problem: 'email-format' }
  if (rule.kind === 'url' && !isSupportedWebUrl(value)) return { ok: false, problem: 'url-unsupported' }
  return { ok: true, value }
}

/** True when every value in the row is empty — an untouched row, not an invalid one. */
export function isRowEmpty(values: Readonly<Record<string, string>>): boolean {
  return Object.values(values).every((raw) => trimToNull(raw) === null)
}

export interface BasicPatch {
  /** Exactly the fields that changed. An untouched field is absent (rule 1). */
  readonly patch: Readonly<Record<string, FieldValue>>
  /** Per-field, so the screen can put each message beside its own input. */
  readonly problems: Readonly<Record<string, FieldProblem>>
  readonly changed: readonly string[]
}

/**
 * Build the basic save's payload: only what changed, and only what this save owns.
 *
 * `original` is the last read's values — the normalized ones, as the contract
 * carries them. `edited` is what is in the inputs now. A field whose normalized
 * input equals the original is NOT in the patch, so whitespace typed into an
 * untouched field cannot make it look edited, and nothing the user never opened is
 * re-asserted.
 */
export function buildBasicPatch(
  rules: Readonly<Record<string, FieldRule>>,
  original: Readonly<Record<string, FieldValue>>,
  edited: Readonly<Record<string, string>>,
): BasicPatch {
  const patch: Record<string, FieldValue> = {}
  const problems: Record<string, FieldProblem> = {}
  const changed: string[] = []

  for (const [key, raw] of Object.entries(edited)) {
    const rule = rules[key]
    if (rule === undefined) {
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
    const before = Object.hasOwn(original, key) ? original[key] : null
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

  return { patch, problems, changed }
}

/**
 * What the user did to one collection row. There is no "removed": a row leaving the
 * editor is not an instruction (rule 4). Passivity is chosen, not inferred.
 */
export type RowIntent = 'unchanged' | 'add' | 'update' | 'deactivate'

export interface EditedRow {
  /** The server's row identity. `null` only for a row the user just added. */
  readonly id: string | null
  readonly intent: RowIntent
  readonly values: Readonly<Record<string, string>>
}

export type RowElement =
  | { readonly action: 'add'; readonly fields: Readonly<Record<string, FieldValue>> }
  | { readonly action: 'update'; readonly id: string; readonly fields: Readonly<Record<string, FieldValue>> }
  /** Deactivation carries the identity and nothing else: it is not an edit. */
  | { readonly action: 'deactivate'; readonly id: string }

export type RowProblem =
  /** An add carrying a server id, or an update/deactivate without one. */
  | 'row-identity'
  /** An add with nothing in it. */
  | 'row-empty'

export interface CollectionPatch {
  readonly elements: readonly RowElement[]
  /** Keyed `<row index>.<field>`, plus `<row index>` for a whole-row problem. */
  readonly problems: Readonly<Record<string, FieldProblem | RowProblem>>
  /**
   * Rows the server holds that this editor is not speaking about. They are KEPT, and
   * named here so the screen can say so instead of letting the user believe a row
   * that scrolled out of a filter was removed.
   */
  readonly retainedIds: readonly string[]
}

/**
 * Build a collection's payload from explicit intents.
 *
 * `originals` is what the last read returned, keyed by row id. Anything in it that
 * no row mentions is reported as retained and NOTHING is emitted for it — the single
 * most destructive mistake available here is to treat "the editor no longer lists it"
 * as "delete it".
 */
export function buildCollectionPatch(
  rules: Readonly<Record<string, FieldRule>>,
  rows: readonly EditedRow[],
  originals: Readonly<Record<string, Readonly<Record<string, FieldValue>>>>,
): CollectionPatch {
  const elements: RowElement[] = []
  const problems: Record<string, FieldProblem | RowProblem> = {}
  const mentioned = new Set<string>()

  rows.forEach((row, index) => {
    if (row.id !== null) mentioned.add(row.id)
    if (row.intent === 'unchanged') return

    if (row.intent === 'deactivate') {
      if (row.id === null) {
        problems[`${index}`] = 'row-identity'
        return
      }
      elements.push({ action: 'deactivate', id: row.id })
      return
    }

    if ((row.intent === 'add') !== (row.id === null)) {
      // An add carrying a server id, or an update without one: neither can be sent.
      problems[`${index}`] = 'row-identity'
      return
    }
    if (row.intent === 'add' && isRowEmpty(row.values)) {
      problems[`${index}`] = 'row-empty'
      return
    }

    const before = row.id === null ? {} : (originals[row.id] ?? {})
    const built = buildBasicPatch(rules, before, row.values)
    for (const [key, problem] of Object.entries(built.problems)) {
      problems[`${index}.${key}`] = problem
    }
    if (Object.keys(built.problems).length > 0) return

    if (row.intent === 'add') {
      /**
       * An add sends the values the row actually has. Against an empty original that is
       * every non-empty field and nothing else: a new row has no "unchanged" fields, and
       * an absent field means unset rather than cleared.
       */
      elements.push({ action: 'add', fields: built.patch })
      return
    }
    // An update with nothing changed says nothing, so it sends nothing.
    if (built.changed.length === 0) return
    elements.push({ action: 'update', id: row.id as string, fields: built.patch })
  })

  const retainedIds = Object.keys(originals).filter((id) => !mentioned.has(id))
  return { elements, problems, retainedIds }
}

/**
 * Whether the basic save may be offered at all: something changed, and nothing is
 * wrong. "Save" stays disabled while there is no change — the handoff's own rule —
 * because a save that sends an empty patch spends an idempotency key and an audit
 * record to say nothing.
 */
export function canSubmitBasic(
  patch: BasicPatch,
  collections: readonly CollectionPatch[] = [],
): boolean {
  if (Object.keys(patch.problems).length > 0) return false
  if (collections.some((c) => Object.keys(c.problems).length > 0)) return false
  return patch.changed.length > 0 || collections.some((c) => c.elements.length > 0)
}
